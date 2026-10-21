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
        => PackReader.Read(xml, source, Catalogue.Munitions, Catalogue.Sensors);

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
    public void ABeltsTracersAreReadOffTheRound()
    {
        PackContents pack = Read(Wrap(
            """<Munition Name="Shell" DisplayName="Shell" Guidance="None" TracerEvery="5" TracerBurnSeconds="2.5" />"""));

        MunitionProfile round = Assert.Single(pack.Munitions);
        Assert.Equal(5, round.TracerEvery);
        Assert.Equal(2.5f, round.TracerBurnSeconds);
    }

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

    [Fact]
    public void AFireControlLabelNamesTheRowAndAbsentItFollowsTheDisplayName()
    {
        string labelled = ARail.Replace("<Launcher ", "<Launcher FireControlLabel=\"Rail fire control\" ");

        ComponentProfile named = Assert.Single(Read(Wrap(ARound + ASet + labelled)).Components);
        ComponentProfile plain = Assert.Single(Read(Wrap(ARound + ASet + ARail)).Components);

        Assert.Equal("Rail fire control", Assert.Single(named.Provides, p => p.Role == WeaponRole.FireControl).DisplayName);
        Assert.Equal($"{Read(Wrap(ARound + ASet + ARail)).Launchers[0].DisplayName} fire control",
                     Assert.Single(plain.Provides, p => p.Role == WeaponRole.FireControl).DisplayName);
    }

    private const string AChinGun =
        """
        <Launcher PartId="TestPack_Prefab_Chin" DisplayName="Chin gun" Munition="Dart" Sensor="Eye"
                  TurretMarker="Yoke" GunsMarker="Gun" GunPivotFromTurret="0.3, -0.1, 0"
                  GunMunition="Dart" GunBarrelMarker="Barrel"
                  GunCylinderMarker="Cyl" GunCylinderPinFromTurret="0.03, 0.37, 0"
                  GunRodMarker="Rod" GunRodPinFromTurret="0.33, 0.37, 0"
                  GunFeedMarker="Belt" GunFeedPivotFromTurret="0.06, -0.1, 0" GunFeedRatio="0.2"
                  GunRotorMarker="Barrels" GunRotorPivotFromTurret="0.5, 0.7, 0"
                  GunRotorSpinUpSeconds="0.3" GunRotorSpinDownSeconds="1.5"
                  GunRecoilMetres="0.3" GunRecoilSeconds="0.1" GunReturnSeconds="0.5"
                  TraverseLimitDeg="110" GunSoundId="Loop" GunshotSoundId="Bang">
          <Muzzle At="0, 2.6, 0" />
        </Launcher>
        """;

    [Fact]
    public void AGunsArticulationAndSoundsAreReadAsDeclared()
    {
        PackContents pack = Read(Wrap(ARound + ASet + AChinGun));
        Assert.Empty(pack.Faults);
        LauncherProfile gun = Assert.Single(pack.Launchers);

        Assert.Equal(("Barrel", "Cyl", "Rod", "Belt", "Barrels"),
                     (gun.GunBarrelMarker, gun.GunCylinderMarker, gun.GunRodMarker, gun.GunFeedMarker, gun.GunRotorMarker));
        Assert.Equal(0.37, gun.GunCylinderPinFromTurret.Y, 9);
        Assert.Equal(0.33, gun.GunRodPinFromTurret.X, 9);
        Assert.Equal(0.06, gun.GunFeedPivotFromTurret.X, 9);
        Assert.Equal(0.2, gun.GunFeedRatio, 9);
        Assert.Equal(0.7, gun.GunRotorPivotFromTurret.Y, 9);
        Assert.Equal((0.3f, 1.5f), (gun.GunRotorSpinUpSeconds, gun.GunRotorSpinDownSeconds));
        Assert.Equal((0.3f, 0.1f, 0.5f), (gun.GunRecoilMetres, gun.GunRecoilSeconds, gun.GunReturnSeconds));
        Assert.Equal(110f, gun.TraverseLimitDeg);
        Assert.Equal(("Loop", "Bang"), (gun.GunSoundId, gun.GunshotSoundId));
    }

    [Theory]
    [InlineData("GunCylinderPinFromTurret")]
    [InlineData("GunRodPinFromTurret")]
    [InlineData("GunFeedPivotFromTurret")]
    [InlineData("GunRotorPivotFromTurret")]
    public void ALinkageMarkerWithNoPinIsRefused(string pin)
    {
        string unpinned = System.Text.RegularExpressions.Regex.Replace(AChinGun, $"{pin}=\"[^\"]*\"", "");

        PackContents pack = Read(Wrap(ARound + ASet + unpinned));

        Assert.Empty(pack.Launchers);
        Assert.Contains(pack.Faults, f => f.Reason.Contains(pin));
    }

    [Fact]
    public void ADirectorsAndAnInfraredSetsSensorFieldsAreRead()
    {
        PackContents pack = Read(Wrap(
            """
            <Sensor Name="Eye" DisplayName="Eye" CuedOnly="true" />
            <Sensor Name="Heat" DisplayName="Heat" Infrared="true" IrThresholdWPerM2="1e-6" LookDownPenalty="10" />
            """));

        Assert.Empty(pack.Faults);
        Assert.True(pack.Sensors[0].CuedOnly);
        Assert.False(pack.Sensors[1].CuedOnly);
        Assert.True(pack.Sensors[1].Infrared);
        Assert.Equal((1e-6f, 10f), (pack.Sensors[1].IrThresholdWPerM2, pack.Sensors[1].LookDownPenalty));
    }

    private const string AMast =
        """
        <Optic PartId="TestPack_Prefab_Mast" DisplayName="Mast sight" Sensor="Eye"
               BaseMarker="Base" HeadMarker="Head" HeadPivot="0.6, 0, 0" />
        """;

    [Fact]
    public void ADirectorStandingAloneBringsItsOwnCameraRow()
    {
        PackContents pack = Read(Wrap(ASet + AMast));

        ComponentProfile row = Assert.Single(pack.Components);
        Assert.Equal(("TestPack_Prefab_Mast", WeaponRole.Camera, "Mast sight"), (row.PartId, row.Role, row.DisplayName));
    }

    // A director on a launcher's own part is that launcher's to declare, with <Provides>.
    [Fact]
    public void ADirectorOnALaunchersPartBringsNoSecondRow()
    {
        string onTheRail = AMast.Replace("TestPack_Prefab_Mast", "TestPack_Prefab_Rail");

        PackContents pack = Read(Wrap(ARound + ASet + ARail + onTheRail));

        Assert.Equal(WeaponRole.Launcher, Assert.Single(pack.Components).Role);
    }

    [Fact]
    public void ASearchSetIsReadWithItsSensorRowNamedByPanelName()
    {
        PackContents pack = Read(Wrap(ASet +
            """
            <SearchSet PartId="TestPack_Prefab_Irst" DisplayName="IRST" Sensor="Eye"
                       PanelName="Infrared search" HeadMarker="Head" HeadPivot="0.1, 0.2, 0"
                       HeadTravelDeg="50" ScanPeriodSeconds="4" />
            """));

        Assert.Empty(pack.Faults);
        SearchSetProfile set = Assert.Single(pack.SearchSets);
        Assert.Equal(("Head", 50f, 4f), (set.HeadMarker, set.HeadTravelDeg, set.ScanPeriodSeconds));
        Assert.Equal("TestPack:Eye", set.Sensor);

        ComponentProfile row = Assert.Single(pack.Components);
        Assert.Equal((WeaponRole.Sensor, "Infrared search"), (row.Role, row.DisplayName));
    }

    private const string AFlare =
        """<Decoy Name="Spark" DisplayName="Spark" Kind="Flare" MassKg="0.4" PeakSignature="25" LifeSeconds="4.5" DragPerMetre="0.004" />""";

    [Fact]
    public void ADispenserTakesItsKindFromItsDecoyAndBringsACountermeasuresRow()
    {
        PackContents pack = Read(Wrap(AFlare +
            """<Dispenser PartId="TestPack_Prefab_Pod" DisplayName="Flare pod" Decoy="Spark" Count="15" EjectSpeed="20" />"""));

        Assert.Empty(pack.Faults);
        DecoyProfile decoy = Assert.Single(pack.Decoys);
        Assert.Equal(("TestPack:Spark", DecoyKind.Flare, 25f, 4.5f), (decoy.Name, decoy.Kind, decoy.PeakSignature, decoy.LifeSeconds));

        DispenserProfile pod = Assert.Single(pack.Dispensers);
        Assert.Equal((DecoyKind.Flare, "TestPack:Spark", 15, 20f), (pod.Kind, pod.Decoy, pod.Count, pod.EjectSpeed));

        ComponentProfile row = Assert.Single(pack.Components);
        Assert.Equal((WeaponRole.Countermeasures, "Flare pod"), (row.Role, row.DisplayName));
    }

    [Fact]
    public void ADispenserStatingAKindItsDecoyIsNotIsRefused()
    {
        PackContents pack = Read(Wrap(AFlare +
            """<Dispenser PartId="TestPack_Prefab_Pod" DisplayName="Pod" Decoy="Spark" Kind="Chaff" />"""));

        Assert.Empty(pack.Dispensers);
        Assert.Contains(pack.Faults, f => f.Element == "Dispenser" && f.Reason.Contains("Flare"));
    }

    [Fact]
    public void ADispenserNamingNoDecoyAnywhereIsRefused()
    {
        PackContents pack = Read(Wrap("""<Dispenser PartId="TestPack_Prefab_Pod" DisplayName="Pod" Decoy="Nothing" />"""));

        Assert.Empty(pack.Dispensers);
        Assert.Contains(pack.Faults, f => f.Element == "Dispenser");
    }

    [Fact]
    public void ADecoyWithNoKindIsRefused()
    {
        PackContents pack = Read(Wrap("""<Decoy Name="Spark" DisplayName="Spark" />"""));

        Assert.Empty(pack.Decoys);
        Assert.Contains(pack.Faults, f => f.Reason.Contains("Kind"));
    }

    private const string ALaserHead =
        """
        <Optic PartId="TestPack_Prefab_Laser" DisplayName="Laser head" Sensor="Eye"
               BaseMarker="Base" HeadMarker="Head" HeadPivot="0, 2.5, 0">
          <Beam PowerWatts="2e6" ApertureMetres="1.2" ShotSeconds="3" />
        </Optic>
        """;

    private const string ALaser =
        """
        <Launcher PartId="TestPack_Prefab_Laser" DisplayName="Laser" Sensor="Eye"
                  FiresBeam="true" BeamArmamentLabel="Beam" MintRows="false">
          <Provides Role="Camera" DisplayName="Laser head" />
          <Provides Role="FireControl" DisplayName="Laser fire control" />
        </Launcher>
        """;

    [Fact]
    public void ABeamWeaponNeedsNoRoundAndFiresItsHeadsBeam()
    {
        PackContents pack = Read(Wrap(ASet + ALaserHead + ALaser));

        Assert.Empty(pack.Faults);
        LauncherProfile laser = Assert.Single(pack.Launchers);
        Assert.True(laser.FiresBeam);
        Assert.Equal(("", "Beam"), (laser.Munition, laser.BeamArmamentLabel));

        BeamProfile beam = Assert.Single(pack.Optics).Beam!;
        Assert.Equal((2e6f, 1.2f, 3f), (beam.PowerWatts, beam.ApertureMetres, beam.ShotSeconds));
        Assert.Equal(new BeamProfile().ChemicalSeconds, beam.ChemicalSeconds);
    }

    [Fact]
    public void MintRowsFalseListsExactlyTheDeclaredRows()
    {
        ComponentProfile row = Assert.Single(Read(Wrap(ASet + ALaserHead + ALaser)).Components);

        Assert.Equal([(WeaponRole.Camera, "Laser head"), (WeaponRole.FireControl, "Laser fire control")],
                     row.Provides.Select(p => (p.Role, p.DisplayName)));
    }

    [Fact]
    public void ABeamWeaponWhoseHeadCarriesNoBeamIsRefused()
    {
        string noBeam = ALaserHead.Replace("<Beam PowerWatts=\"2e6\" ApertureMetres=\"1.2\" ShotSeconds=\"3\" />", "");

        PackContents pack = Read(Wrap(ASet + noBeam + ALaser));

        Assert.Empty(pack.Launchers);
        Assert.DoesNotContain(pack.Components, c => c.Role == WeaponRole.Launcher);
        Assert.Contains(pack.Faults, f => f.Element == "Launcher" && f.Reason.Contains("<Beam>"));
    }

    [Fact]
    public void AHeadCarriesOneBeam()
    {
        string twoBeams = ALaserHead.Replace("<Beam ", "<Beam /><Beam ");

        Assert.Contains(Read(Wrap(ASet + twoBeams)).Faults, f => f.Reason.Contains("one beam"));
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
    [InlineData("Munition")]
    [InlineData("GunMunition")]
    public void ALauncherNamingARefusedRoundIsToldWhyTheRoundWasRefused(string attribute)
    {
        PackContents pack = Read(Wrap(
            ASet + $"""
            <Munition Name="Dart" DisplayName="Dart" Spread="0.3" />
            <Launcher PartId="TestPack_Prefab_Rail" DisplayName="Test rail"
                      Munition="Dart" Sensor="Eye" {(attribute == "GunMunition" ? "GunMunition=\"Dart\"" : "")}>
              <Tube Position="0, 0, 0.9" />
              <Muzzle At="0, 0, 1" />
            </Launcher>
            """));

        Assert.Empty(pack.Launchers);
        PackFault launcher = Assert.Single(pack.Faults, f => f.Element == "Launcher" && f.Reason.StartsWith(attribute + "="));
        Assert.Contains("was refused above", launcher.Reason);
        Assert.Contains("Spread", launcher.Reason);
    }

    [Fact]
    public void ALauncherNamingARefusedSensorIsToldWhyTheSensorWasRefused()
    {
        PackContents pack = Read(Wrap(
            ARound + """
            <Sensor Name="Eye" DisplayName="Eye" Rnage="9000" />
            <Launcher PartId="TestPack_Prefab_Rail" DisplayName="Test rail"
                      Munition="Dart" Sensor="Eye">
              <Tube Position="0, 0, 0.9" />
            </Launcher>
            """));

        PackFault launcher = Assert.Single(pack.Faults, f => f.Element == "Launcher");
        Assert.Contains("was refused above", launcher.Reason);
        Assert.Contains("Rnage", launcher.Reason);
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

    /// <summary>
    /// A round's drag from what it is: mass, calibre and coefficient come through and decide it over any DragK,
    /// and a round given only some of them is refused rather than flying on a DragK nobody meant.
    /// </summary>
    [Fact]
    public void ARoundsDragComesFromItsMassCalibreAndCoefficient()
    {
        MunitionProfile round = Read(Wrap(
            """<Munition Name="Shell" DisplayName="Shell" MassKg="31.75" CalibreMm="127" DragCoefficient="0.306" DragK="9" />"""))
            .Munitions[0];

        Assert.True(round.DragFromShape);
        Assert.InRange(round.AppliedDragK, 7.45e-5, 7.51e-5);

        PackContents partial = Read(Wrap("""<Munition Name="Half" DisplayName="Half" MassKg="31.75" CalibreMm="127" />"""));
        Assert.Empty(partial.Munitions);
        Assert.Contains(partial.Faults, f => f.ToString().Contains("DragCoefficient", StringComparison.Ordinal));
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
