using System.Globalization;
using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What the reader accepts, and — mostly — what it refuses.
///
/// A loader's interesting behaviour is almost entirely in what it will not take and what it says
/// about it, because every refusal it declines to make is a weapon that loads wrong and flies
/// with nothing on screen to show for it.
/// </summary>
public class PackReaderTests
{
    private static readonly MunitionProfile[] NoRounds = [];
    private static readonly SensorProfile[] NoSets = [];

    private static PackContents Read(string xml, string source = "TestPack")
        => PackReader.Read(xml, source, Arsenal.Munitions, Arsenal.Sensors);

    private static string Wrap(string body) => $"<WeaponPack Schema=\"1\">{body}</WeaponPack>";

    private const string ARound =
        """<Munition Name="Dart" DisplayName="Dart" Guidance="Seeker" MaxRange="9000" />""";

    private const string ASet =
        """<Sensor Name="Eye" DisplayName="Eye" Range="12000" />""";

    private const string ARail =
        """
        <Launcher PartId="TestPack_Prefab_Rail" DisplayName="Test rail"
                  Munition="Dart" Sensor="Eye">
          <Tube Position="0, 0, 0.9" Direction="1, 0, 0" />
        </Launcher>
        """;

    // ---- Accepting ------------------------------------------------------

    [Fact]
    public void AWholePackComesThroughWithItsNumbers()
    {
        PackContents pack = Read(Wrap(ARound + ASet + ARail));

        Assert.Empty(pack.Faults);
        Assert.Equal(3, pack.Accepted);

        MunitionProfile round = Assert.Single(pack.Munitions);
        Assert.Equal(GuidanceMode.Seeker, round.Guidance);
        Assert.Equal(9000f, round.MaxRange);

        // Untouched fields keep the profile's own default rather than zero, which is what lets a
        // pack state only what differs and what makes a field added later invisible to it.
        Assert.Equal(new MunitionProfile { Name = "x", DisplayName = "x" }.DragK, round.DragK);

        LauncherProfile rail = Assert.Single(pack.Launchers);
        Assert.Equal(1, rail.TubeCount);
        Assert.True(rail.Tubes[0].HasOwnDirection);
        Assert.Equal(0.9, rail.Tubes[0].Position.Z, 9);
    }

    [Fact]
    public void APacksOwnNamesCarryThePackAndItsReferencesFindThem()
    {
        PackContents pack = Read(Wrap(ARound + ASet + ARail));

        Assert.Equal("TestPack:Dart", pack.Munitions[0].Name);
        Assert.Equal("TestPack:Eye", pack.Sensors[0].Name);
        Assert.Equal("TestPack:Dart", pack.Launchers[0].Munition);
        Assert.Equal("TestPack:Eye", pack.Launchers[0].Sensor);
    }

    /// <summary>
    /// Two packs shipping a round of the same name are two rounds. Without the qualifier the
    /// second registers under a name the first already holds and one of them silently wins.
    /// </summary>
    [Fact]
    public void TwoPacksNamingARoundAlikeProduceTwoDifferentRounds()
    {
        MunitionProfile mine = Read(Wrap(ARound), "PackA").Munitions[0];
        MunitionProfile theirs = Read(Wrap(ARound), "PackB").Munitions[0];

        Assert.NotEqual(mine.Name, theirs.Name);
    }

    [Fact]
    public void APackCanFireOneOfTheBuiltInRounds()
    {
        PackContents pack = Read(Wrap(
            ASet + """
            <Launcher PartId="TestPack_Prefab_Gun" DisplayName="Test mount"
                      Munition="KSArmory:20MM" Sensor="Eye" GunMunition="KSArmory:20MM">
              <Muzzle At="0, 0.1, 1.2" />
            </Launcher>
            """));

        Assert.Empty(pack.Faults);

        // The built-ins predate qualification and keep bare keys, so the prefix is stripped
        // rather than kept -- otherwise the launcher holds a name the catalogue cannot answer.
        Assert.Equal("20MM", pack.Launchers[0].Munition);
        Assert.True(pack.Launchers[0].HasCannon);
    }

    [Fact]
    public void ALauncherBringsTheComponentRowThatMakesItVisible()
    {
        PackContents pack = Read(Wrap(ARound + ASet + ARail));

        ComponentProfile component = Assert.Single(pack.Components);
        Assert.Equal(pack.Launchers[0].PartId, component.PartId);
        Assert.Equal(WeaponRole.Launcher, component.Role);
        Assert.Contains(component.Provides, p => p.Role == WeaponRole.FireControl);
        Assert.Contains(component.Provides, p => p.Role == WeaponRole.Sensor && p.DisplayName == "Eye");
    }

    // ---- Refusing -------------------------------------------------------

    [Fact]
    public void OneBadDefinitionDoesNotCostThePackItsOthers()
    {
        PackContents pack = Read(Wrap(
            ARound + ASet + ARail + """<Munition DisplayName="nameless" />"""));

        Assert.Single(pack.Faults);
        Assert.Equal(3, pack.Accepted);
    }

    /// <summary>
    /// The reason every attribute is consumed. A misspelling that is merely ignored leaves the
    /// author looking at a number in their file that nothing reads.
    /// </summary>
    [Fact]
    public void AMisspeltAttributeIsRefusedRatherThanIgnored()
    {
        PackContents pack = Read(Wrap("""<Munition Name="Dart" DisplayName="Dart" NavConstnat="6" />"""));

        Assert.Empty(pack.Munitions);
        Assert.Contains(pack.Faults, f => f.Reason.Contains("NavConstnat"));
    }

    [Fact]
    public void ANameNothingCarriesIsRefusedAtReadRatherThanFlownAsElementZero()
    {
        PackContents pack = Read(Wrap(
            ASet + """
            <Launcher PartId="TestPack_Prefab_Rail" DisplayName="Test rail"
                      Munition="NoSuchRound" Sensor="Eye">
              <Tube Position="0, 0, 0.9" />
            </Launcher>
            """));

        Assert.Empty(pack.Launchers);
        Assert.Empty(pack.Components);
        Assert.Contains(pack.Faults, f => f.Reason.Contains("names nothing registered"));
    }

    [Theory]
    [InlineData("Guidance=\"Telepathy\"", "Guidance")]
    [InlineData("MaxRange=\"far\"", "MaxRange")]
    [InlineData("FinsPerRound=\"4.5\"", "FinsPerRound")]
    [InlineData("TimedFuse=\"yes\"", "TimedFuse")]
    public void AValueThatIsNotWhatTheFieldTakesIsRefusedAndNamed(string attribute, string expected)
    {
        PackContents pack = Read(Wrap($"""<Munition Name="Dart" DisplayName="Dart" {attribute} />"""));

        Assert.Empty(pack.Munitions);
        Assert.Contains(pack.Faults, f => f.Reason.Contains(expected));
    }

    [Fact]
    public void ARoundDeclaredTwiceIsRefusedTheSecondTime()
    {
        PackContents pack = Read(Wrap(ARound + ARound));

        Assert.Single(pack.Munitions);
        Assert.Contains(pack.Faults, f => f.Reason.Contains("declared twice"));
    }

    [Fact]
    public void ALauncherThatCanShootWithNothingIsRefused()
    {
        PackContents pack = Read(Wrap(
            ARound + ASet + """
            <Launcher PartId="TestPack_Prefab_Empty" DisplayName="Empty"
                      Munition="Dart" Sensor="Eye" />
            """));

        Assert.Empty(pack.Launchers);
        Assert.Contains(pack.Faults, f => f.Reason.Contains("cannot shoot"));
    }

    /// <summary>
    /// A trunnion at the turret's own centre swings the assembly about the vehicle rather than
    /// about a pivot, which is a pod orbiting the hull and no error anywhere.
    /// </summary>
    [Fact]
    public void ElevatingGearWithNoTrunnionOffsetIsRefused()
    {
        PackContents pack = Read(Wrap(
            ARound + ASet + """
            <Launcher PartId="TestPack_Prefab_Turret" DisplayName="Turret"
                      Munition="Dart" Sensor="Eye"
                      TurretMarker="Turret" PodsMarker="Pods">
              <Tube Position="0, 0, 1" />
            </Launcher>
            """));

        Assert.Empty(pack.Launchers);
        Assert.Contains(pack.Faults, f => f.Reason.Contains("PodPivotFromTurret"));
    }

    [Fact]
    public void ARollNodHeadWithoutTheBodyThatRollsIsRefused()
    {
        PackContents pack = Read(Wrap(
            ASet + """
            <Optic PartId="TestPack_Prefab_Pod" DisplayName="Pod" Sensor="Eye" Gimbal="RollNod"
                   BaseMarker="Body" HeadMarker="Head" HeadPivot="0, 0, 1.2" />
            """));

        Assert.Empty(pack.Optics);
        Assert.Contains(pack.Faults, f => f.Reason.Contains("RollMarker"));
    }

    // ---- The file itself ------------------------------------------------

    [Fact]
    public void AFileThatIsNotXmlIsRefusedWholeAndSaysSo()
    {
        PackContents pack = Read("this is not xml <<<");

        Assert.Equal(0, pack.Accepted);
        PackFault fault = Assert.Single(pack.Faults);
        Assert.Equal("WeaponPack", fault.Element);
    }

    [Fact]
    public void AFileWrittenForALaterSchemaIsRefusedWholeRatherThanReadAsFarAsItGoes()
    {
        PackContents pack = Read($"""
            <WeaponPack Schema="{PackReader.Schema + 1}">{ARound}</WeaponPack>
            """);

        Assert.Empty(pack.Munitions);
        Assert.Contains(pack.Faults, f => f.Reason.Contains("Update KSArmory"));
    }

    [Fact]
    public void AFileDeclaringNoSchemaIsRefused()
    {
        PackContents pack = Read($"<WeaponPack>{ARound}</WeaponPack>");

        Assert.Empty(pack.Munitions);
        Assert.Contains(pack.Faults, f => f.Reason.Contains("Schema"));
    }

    [Fact]
    public void AnEmptyPackIsNotAFault()
    {
        PackContents pack = Read(Wrap(""));

        Assert.Empty(pack.Faults);
        Assert.Equal(0, pack.Accepted);
    }

    [Fact]
    public void APackNeedsNothingRegisteredToBeReadable()
    {
        PackContents pack = PackReader.Read(Wrap(ARound + ASet + ARail), "TestPack", NoRounds, NoSets);

        Assert.Empty(pack.Faults);
        Assert.Equal(3, pack.Accepted);
    }

    /// <summary>
    /// The one that costs a whole flight model. On a machine whose decimal separator is a comma,
    /// a culture-sensitive parse reads 2.4 as 24 — ten times the boost, and nothing to see.
    /// </summary>
    [Fact]
    public void NumbersAreReadTheSameWhateverTheMachinesCulture()
    {
        CultureInfo was = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            MunitionProfile round = Read(Wrap(
                """<Munition Name="Dart" DisplayName="Dart" BoostSeconds="2.4" DragK="3.0e-5" />"""))
                .Munitions[0];

            Assert.Equal(2.4f, round.BoostSeconds, 5);
            Assert.Equal(3.0e-5f, round.DragK, 9);
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    [Fact]
    public void AVectorIsThreeNumbersAndAnythingElseIsRefused()
    {
        Assert.Contains(Read(Wrap(
            ARound + ASet + """
            <Launcher PartId="TestPack_Prefab_Rail" DisplayName="Test rail"
                      Munition="Dart" Sensor="Eye" TurretPivot="0, 1">
              <Tube Position="0, 0, 1" />
            </Launcher>
            """)).Faults, f => f.Reason.Contains("TurretPivot"));
    }

    [Fact]
    public void AnAngleIsWrittenInDegreesAndHeldInRadians()
    {
        LauncherProfile rail = Read(Wrap(
            ARound + ASet + """
            <Launcher PartId="TestPack_Prefab_Rail" DisplayName="Test rail"
                      Munition="Dart" Sensor="Eye" PodReferenceElevationDeg="22">
              <Tube Position="0, 0, 1" />
            </Launcher>
            """)).Launchers[0];

        Assert.Equal(double.DegreesToRadians(22), rail.PodReferenceElevationRad, 9);
    }

    [Fact]
    public void AnElementThisBuildDoesNotKnowIsRefusedRatherThanSkipped()
    {
        PackContents pack = Read(Wrap("<Torpedo Name=\"Fish\" />"));

        Assert.Contains(pack.Faults, f => f.Element == "Torpedo");
    }
}
