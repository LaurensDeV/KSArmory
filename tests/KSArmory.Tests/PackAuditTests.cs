using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The audit, against a stand-in part library.
///
/// What it catches is the failure that has no other symptom: a profile naming a part nothing
/// declared registers, resolves, matches nothing on any craft, and is indistinguishable from a
/// mod that was never installed.
/// </summary>
public class PackAuditTests
{
    private sealed class Library(params string[] declarations) : IPartCatalogue
    {
        // "PartId:SubA,SubB" per entry, which is the whole of what the audit asks.
        private readonly Dictionary<string, string[]> _parts = declarations.ToDictionary(
            d => d.Split(':')[0],
            d => d.Contains(':') ? d.Split(':')[1].Split(',') : []);

        public bool Declares(string partId) => _parts.ContainsKey(partId);

        public IReadOnlyList<string> SubPartIdsOf(string partId)
            => _parts.TryGetValue(partId, out string[]? subs) ? subs : [];
    }

    private static LauncherProfile Rail(string partId, string? turret = null, string? pods = null)
        => new()
        {
            PartId = partId,
            DisplayName = "rail",
            Munition = "x",
            Sensor = "y",
            Tubes = [new Tube(0, 0, 1)],
            TurretMarker = turret,
            PodsMarker = pods,
        };

    private static IReadOnlyList<PackFault> Audit(IPartCatalogue library,
                                                  LauncherProfile[]? launchers = null,
                                                  OpticProfile[]? optics = null)
        => PackAudit.Run(launchers ?? [], optics ?? [], library, _ => "TestPack");

    [Fact]
    public void APartThatLoadedWithItsMarkersIntactRaisesNothing()
    {
        Assert.Empty(Audit(new Library("Pack_Prefab_Rail:Rail_Turret,Rail_Pods"),
                           [Rail("Pack_Prefab_Rail", turret: "Turret", pods: "Pods")]));
    }

    [Fact]
    public void ALauncherNamingAPartNothingDeclaredIsReported()
    {
        PackFault fault = Assert.Single(Audit(new Library("Pack_Prefab_Other"),
                                              [Rail("Pack_Prefab_Rail")]));

        Assert.Equal("Pack_Prefab_Rail", fault.Name);
        Assert.Contains("no part with this Id", fault.Reason);
    }

    /// <summary>
    /// One complaint, not six. A part that did not load fails every marker on it too, and burying
    /// the cause under its consequences is how a report stops being read.
    /// </summary>
    [Fact]
    public void AMissingPartIsReportedOnceRatherThanOncePerMarker()
    {
        Assert.Single(Audit(new Library(),
                            [Rail("Pack_Prefab_Rail", turret: "Turret", pods: "Pods")]));
    }

    [Fact]
    public void AMarkerMatchingNoSubpartIsReported()
    {
        PackFault fault = Assert.Single(Audit(new Library("Pack_Prefab_Rail:Rail_Turret"),
                                              [Rail("Pack_Prefab_Rail", turret: "Turret", pods: "Pods")]));

        Assert.Contains("PodsMarker", fault.Reason);
        Assert.Contains("matches no subpart", fault.Reason);
    }

    /// <summary>
    /// The substring trap. Resolution takes the first hit, so two matches means the articulating
    /// assembly is whichever the part happens to list first — and reordering the XML changes it.
    /// </summary>
    [Fact]
    public void AMarkerMatchingTwoSubpartsIsReportedBecauseTheFirstOneWins()
    {
        PackFault fault = Assert.Single(Audit(new Library("Pack_Prefab_Rail:Rail_Pods,Rail_PodsCover"),
                                              [Rail("Pack_Prefab_Rail", pods: "Pods")]));

        Assert.Contains("matches 2 subparts", fault.Reason);
    }

    [Fact]
    public void MarkersMatchCaseInsensitivelyBecauseThatIsHowTheGameResolvesThem()
    {
        Assert.Empty(Audit(new Library("Pack_Prefab_Rail:rail_PODS"),
                           [Rail("Pack_Prefab_Rail", pods: "Pods")]));
    }

    [Fact]
    public void AHeadIsCheckedTheSameWayAndItsRollBodyOnlyWhenItHasOne()
    {
        OpticProfile mast = new()
        {
            PartId = "Pack_Prefab_Eye",
            DisplayName = "eye",
            Sensor = "y",
            BaseMarker = "Optic_Base",
            HeadMarker = "Optic_Head",
            HeadPivot = default,
        };

        Assert.Empty(Audit(new Library("Pack_Prefab_Eye:Optic_Base,Optic_Head"), optics: [mast]));
        Assert.Single(Audit(new Library("Pack_Prefab_Eye:Optic_Base"), optics: [mast]));
    }

    [Fact]
    public void TheFaultNamesThePackSoAPlayerKnowsWhoseProblemItIs()
    {
        IReadOnlyList<PackFault> faults = PackAudit.Run(
            [Rail("Pack_Prefab_Rail")], [], new Library(), _ => "SomeonesPack");

        Assert.Equal("SomeonesPack", faults[0].Source);
    }
}
