using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// KSArmory's own files registered as one unit. These register into the one static catalogue under
/// the built-in source, so every name is one nothing shipped will ever use, and every round declares
/// its shape so the registry-wide drag invariant holds over them too.
/// </summary>
[Collection("catalogue")]
public class CoreDefinitionsTests
{
    private const string Rounds = """
        <WeaponPack Schema="1">
          <Munition Name="CORETEST_BOLT" DisplayName="Bolt" MaxRange="7000" MassKg="10" CalibreMm="100" DragCoefficient="0.3" />
          <Sensor Name="CORETEST_EYE" DisplayName="Eye" Range="9000" />
        </WeaponPack>
        """;

    private static string RailNaming(string part, string round, string sensor) => $"""
        <WeaponPack Schema="1">
          <Launcher PartId="{part}" DisplayName="Rail" Munition="{round}" Sensor="{sensor}">
            <Tube Position="0, 0, 0.8" Direction="1, 0, 0" />
          </Launcher>
        </WeaponPack>
        """;

    // The launcher's file sorts first, so a reader resolving against what is registered so far
    // would refuse it.
    [Fact]
    public void ALauncherMayNameARoundDeclaredInALaterFile()
    {
        IReadOnlyList<CoreFileResult> results = CoreDefinitions.Register(
        [
            ("b-rounds.xml", Rounds),
            ("a-rail.xml", RailNaming("CORETEST_Prefab_Rail", "CORETEST_BOLT", "KSArmory:CORETEST_EYE")),
        ]);

        Assert.All(results, r => Assert.True(r.Result.Complete, string.Join("; ", r.Result.Faults)));

        LauncherProfile? rail = Catalogue.LauncherForPart("CORETEST_Prefab_Rail");
        Assert.NotNull(rail);
        Assert.Equal(7000f, Catalogue.LoadoutFor(rail).Munition.MaxRange);
    }

    [Fact]
    public void FilesRegisterInOrdinalOrderWhateverOrderTheyAreHandedIn()
    {
        IReadOnlyList<CoreFileResult> results = CoreDefinitions.Register(
        [
            ("c.xml", """<WeaponPack Schema="1"><Munition Name="CORETEST_C" DisplayName="C" MassKg="10" CalibreMm="100" DragCoefficient="0.3" /></WeaponPack>"""),
            ("B.xml", """<WeaponPack Schema="1"><Munition Name="CORETEST_B" DisplayName="B" MassKg="10" CalibreMm="100" DragCoefficient="0.3" /></WeaponPack>"""),
            ("a.xml", """<WeaponPack Schema="1"><Munition Name="CORETEST_A" DisplayName="A" MassKg="10" CalibreMm="100" DragCoefficient="0.3" /></WeaponPack>"""),
        ]);

        Assert.Equal(["B.xml", "a.xml", "c.xml"], results.Select(r => r.FileName));
    }

    [Fact]
    public void ABodiesFileIsLeftForTheBodyCatalogue()
    {
        IReadOnlyList<CoreFileResult> results = CoreDefinitions.Register(
            [("Bodies.xml", """<Bodies><Body Id="CoreTestWorld" /></Bodies>""")]);

        Assert.Empty(results);
    }

    [Fact]
    public void AReferenceToNothingAnyFileDeclaresIsStillRefused()
    {
        IReadOnlyList<CoreFileResult> results = CoreDefinitions.Register(
            [("rail.xml", RailNaming("CORETEST_Prefab_Orphan", "CORETEST_NOTHING", "CORETEST_NOTHING"))]);

        Assert.False(Assert.Single(results).Result.Complete);
        Assert.Null(Catalogue.LauncherForPart("CORETEST_Prefab_Orphan"));
    }

    // A switched-off family's parts must still load on a craft, and a pack using its rounds must too.
    [Fact]
    public void ASwitchedOffFamilyKeepsItsRoundsAndSensorsAndLosesItsParts()
    {
        string family = """
            <WeaponPack Schema="1" Family="CoreTestOff">
              <Munition Name="CORETEST_OFFROUND" DisplayName="Round" MassKg="10" CalibreMm="100" DragCoefficient="0.3" />
              <Sensor Name="CORETEST_OFFEYE" DisplayName="Eye" />
              <Launcher PartId="CORETEST_Prefab_OffRail" DisplayName="Rail" Munition="CORETEST_OFFROUND" Sensor="CORETEST_OFFEYE">
                <Tube Position="0, 0, 0.8" Direction="1, 0, 0" />
              </Launcher>
            </WeaponPack>
            """;

        CoreFileResult result = Assert.Single(CoreDefinitions.Register([("off.xml", family)], new HashSet<string> { "CoreTestOff" }));

        Assert.True(result.Disabled);
        Assert.Equal("CoreTestOff", result.Family);
        Assert.NotNull(Catalogue.TryMunitionNamed("CORETEST_OFFROUND"));
        Assert.NotNull(Catalogue.TrySensorNamed("CORETEST_OFFEYE"));
        Assert.Null(Catalogue.LauncherForPart("CORETEST_Prefab_OffRail"));
        Assert.DoesNotContain(Catalogue.Components, c => c.PartId == "CORETEST_Prefab_OffRail");
        Assert.True(Catalogue.IsDisabledLauncher("CORETEST_Prefab_OffRail"));
        Assert.Contains("CORETEST_Prefab_OffRail", Catalogue.DisabledParts);
    }
    // Switching a family off is how a pack replaces it, so a part the pack defines again must stop
    // reading as switched off: hidden from the editor and counted twice in the settings ordinal otherwise.
    [Fact]
    public void APackDefiningASwitchedOffPartMakesItAWeaponAgain()
    {
        string family = """
            <WeaponPack Schema="1" Family="CoreTestSwap">
              <Munition Name="CORETEST_SWAPROUND" DisplayName="Round" MassKg="10" CalibreMm="100" DragCoefficient="0.3" />
              <Sensor Name="CORETEST_SWAPEYE" DisplayName="Eye" />
              <Launcher PartId="CORETEST_Prefab_SwapRail" DisplayName="Rail" Munition="CORETEST_SWAPROUND" Sensor="CORETEST_SWAPEYE">
                <Tube Position="0, 0, 0.8" Direction="1, 0, 0" />
              </Launcher>
              <Launcher PartId="CORETEST_Prefab_SwapKept" DisplayName="Rail" Munition="CORETEST_SWAPROUND" Sensor="CORETEST_SWAPEYE">
                <Tube Position="0, 0, 0.8" Direction="1, 0, 0" />
              </Launcher>
            </WeaponPack>
            """;
        CoreDefinitions.Register([("swap.xml", family)], new HashSet<string> { "CoreTestSwap" });

        PackResult replacement = Armoury.Register("""
            <WeaponPack Schema="1">
              <Launcher PartId="CORETEST_Prefab_SwapRail" DisplayName="Better rail"
                        Munition="KSArmory:CORETEST_SWAPROUND" Sensor="KSArmory:CORETEST_SWAPEYE">
                <Tube Position="0, 0, 0.8" Direction="1, 0, 0" />
              </Launcher>
            </WeaponPack>
            """, "CoreTestSwapPack");

        Assert.True(replacement.Complete, string.Join("; ", replacement.Faults));
        Assert.Equal("Better rail", Catalogue.LauncherForPart("CORETEST_Prefab_SwapRail")?.DisplayName);
        Assert.False(Catalogue.IsDisabledLauncher("CORETEST_Prefab_SwapRail"));
        Assert.DoesNotContain("CORETEST_Prefab_SwapRail", Catalogue.DisabledParts);
        Assert.True(Catalogue.IsDisabledLauncher("CORETEST_Prefab_SwapKept"));
        Assert.Contains("CORETEST_Prefab_SwapKept", Catalogue.DisabledParts);
    }
    [Fact]
    public void APackRefusedABuiltInPartIsToldWhichFamilyToSwitchOff()
    {
        string family = """
            <WeaponPack Schema="1" Family="CoreTestHeld">
              <Munition Name="CORETEST_HELDROUND" DisplayName="Round" MassKg="10" CalibreMm="100" DragCoefficient="0.3" />
              <Sensor Name="CORETEST_HELDEYE" DisplayName="Eye" />
              <Launcher PartId="CORETEST_Prefab_HeldRail" DisplayName="Rail" Munition="CORETEST_HELDROUND" Sensor="CORETEST_HELDEYE">
                <Tube Position="0, 0, 0.8" Direction="1, 0, 0" />
              </Launcher>
            </WeaponPack>
            """;
        CoreDefinitions.Register([("held.xml", family)]);

        PackResult refused = Armoury.Register("""
            <WeaponPack Schema="1">
              <Launcher PartId="CORETEST_Prefab_HeldRail" DisplayName="Better rail"
                        Munition="KSArmory:CORETEST_HELDROUND" Sensor="KSArmory:CORETEST_HELDEYE">
                <Tube Position="0, 0, 0.8" Direction="1, 0, 0" />
              </Launcher>
            </WeaponPack>
            """, "CoreTestHeldPack");

        Assert.Contains("built-in family CoreTestHeld", Assert.Single(refused.Faults).Reason);
        Assert.Equal("Rail", Catalogue.LauncherForPart("CORETEST_Prefab_HeldRail")?.DisplayName);
    }
}
