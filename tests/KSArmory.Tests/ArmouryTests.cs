using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Registration, which is the half <see cref="PackReaderTests"/> does not reach: reading a
/// definition says whether it is well formed, registering says whether the catalogue will have it.
///
/// These share one static catalogue, so they register under names nothing else uses and assert
/// against what they put in rather than against a count.
/// </summary>
[Collection("catalogue")]
public class ArmouryTests
{
    private static string Pack(string source, string round = "Bolt", string part = "Rail")
        => $"""
           <WeaponPack Schema="1">
             <Munition Name="{round}" DisplayName="{round}" MaxRange="7000" />
             <Sensor Name="Eye" DisplayName="Eye" Range="9000" />
             <Launcher PartId="{source}_Prefab_{part}" DisplayName="{source} rail"
                       Munition="{round}" Sensor="Eye">
               <Tube Position="0, 0, 0.8" Direction="1, 0, 0" />
             </Launcher>
           </WeaponPack>
           """;

    [Fact]
    public void ARegisteredLauncherIsFoundByPartIdLikeAnyOther()
    {
        PackResult result = Armoury.Register(Pack("AlphaPack"), "AlphaPack");

        Assert.True(result.Complete, string.Join("; ", result.Faults));
        Assert.Equal(3, result.Registered);

        LauncherProfile? found = Catalogue.LauncherForPart("AlphaPack_Prefab_Rail");
        Assert.NotNull(found);
        Assert.Equal("AlphaPack:Bolt", found.Munition);

        // And the loadout pairs through the same call fire control uses, so a registered launcher
        // is not a special case anywhere downstream.
        (MunitionProfile round, SensorProfile set) = Catalogue.LoadoutFor(found);
        Assert.Equal(7000f, round.MaxRange);
        Assert.Equal(9000f, set.Range);
    }

    /// <summary>
    /// A launcher absent from the components registry is adopted by fire control and invisible to
    /// the panel, which is indistinguishable from a part that never loaded.
    /// </summary>
    [Fact]
    public void ARegisteredLauncherIsAlsoARecognisedComponent()
    {
        Armoury.Register(Pack("BravoPack"), "BravoPack");

        Assert.Contains(Catalogue.Components,
                        c => c.PartId == "BravoPack_Prefab_Rail" && c.Role == WeaponRole.Launcher);
    }

    [Fact]
    public void APartIdSomethingElseAlreadyClaimsIsRefused()
    {
        string stealing = $"""
            <WeaponPack Schema="1">
              <Munition Name="Bolt" DisplayName="Bolt" />
              <Sensor Name="Eye" DisplayName="Eye" />
              <Launcher PartId="{BuiltIns.PantsirS1.PartId}" DisplayName="Not a Pantsir"
                        Munition="Bolt" Sensor="Eye">
                <Tube Position="0, 0, 1" />
              </Launcher>
            </WeaponPack>
            """;

        PackResult result = Armoury.Register(stealing, "ThiefPack");

        Assert.Contains(result.Faults, f => f.Reason.Contains("already registered"));
        Assert.Same(BuiltIns.PantsirS1, Catalogue.LauncherForPart(BuiltIns.PantsirS1.PartId));
    }

    /// <summary>
    /// The subtle one. A refused round leaves its name in the catalogue carrying somebody else's
    /// profile, so a launcher naming it resolves — and flies a weapon its author never shipped.
    /// Registering is refused for the name being taken, not for the name being absent.
    /// </summary>
    [Fact]
    public void ALauncherWhoseRoundLostTheNameIsRefusedRatherThanFlyingSomebodyElses()
    {
        Armoury.Register(Pack("CharliePack", round: "Spike", part: "One"), "CharliePack");

        // The same name a second time: the round is refused, and the launcher that names it would
        // otherwise quietly adopt the first one.
        PackResult second = Armoury.Register(Pack("CharliePack", round: "Spike", part: "Two"), "CharliePack");

        Assert.Contains(second.Faults, f => f.Reason.Contains("already claims"));
        Assert.Null(Catalogue.LauncherForPart("CharliePack_Prefab_Two"));
    }

    [Fact]
    public void RegisteringIsRecordedWhetherOrNotItWorked()
    {
        int before = Catalogue.Registrations.Count;
        Armoury.Register("not xml at all <<<", "BrokenPack");

        Assert.Equal(before + 1, Catalogue.Registrations.Count);
        PackResult last = Catalogue.Registrations[^1];
        Assert.Equal("BrokenPack", last.Source);
        Assert.Equal(0, last.Registered);
        Assert.False(last.Complete);
    }
}

/// <summary>
/// The freeze, alone in its own class because it cannot be undone: once shut, every later
/// registration in the process is refused.
/// </summary>
[Collection("catalogue")]
public class CatalogueFreezeTests
{
    [Fact]
    public void NothingRegistersOnceTheRosterHasBeenBuilt()
    {
        Assert.True(Armoury.IsOpen);

        int launchers = Catalogue.Launchers.Count;
        Catalogue.Freeze();

        try
        {
            PackResult late = Armoury.Register("""
                <WeaponPack Schema="1">
                  <Munition Name="Late" DisplayName="Late" />
                </WeaponPack>
                """, "LatePack");

            Assert.False(Armoury.IsOpen);
            Assert.Equal(0, late.Registered);
            Assert.Contains(late.Faults, f => f.Reason.Contains("after the roster"));
            Assert.Equal(launchers, Catalogue.Launchers.Count);
        }
        finally
        {
            Catalogue.Reopen();
        }
    }
}
