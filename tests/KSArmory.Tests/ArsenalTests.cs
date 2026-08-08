using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The registry contract, which is what adding a weapon system relies on.
///
/// These are cheap and mostly obvious, which is the point: the failure mode of a registry is a
/// typo in a name or a mismatched count, and neither shows up until something silently picks
/// the wrong profile in game.
/// </summary>
public class ArsenalTests
{
    [Fact]
    public void EveryLauncherNamesARegisteredMunitionAndSensor()
    {
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            Assert.Equal(launcher.Munition, Arsenal.MunitionNamed(launcher.Munition).Name);
            Assert.Equal(launcher.Sensor, Arsenal.SensorNamed(launcher.Sensor).Name);

            // The cannon's round as well as the missile's. Arsenal.Named falls back to element
            // zero, which for munitions is a 20 kg missile at 45 m/s under a rocket boost: a gun
            // naming a shell that does not exist compiles, loads, passes every other gate and
            // fires warheads out of its barrel.
            if (launcher.GunMunition is { } shell)
            {
                Assert.Equal(shell, Arsenal.MunitionNamed(shell).Name);
            }
        }
    }

    [Fact]
    public void LauncherPartIdsAreUniqueAndNonEmpty()
    {
        // Discovery is by part Id, so a duplicate would make which system you get depend on
        // registration order.
        var seen = new HashSet<string>();
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            Assert.False(string.IsNullOrWhiteSpace(launcher.PartId));
            Assert.True(seen.Add(launcher.PartId), $"duplicate part Id {launcher.PartId}");
        }
    }

    [Fact]
    public void MunitionAndSensorNamesAreUnique()
    {
        Assert.Equal(Arsenal.Munitions.Count, Arsenal.Munitions.Select(m => m.Name).Distinct().Count());
        Assert.Equal(Arsenal.Sensors.Count, Arsenal.Sensors.Select(s => s.Name).Distinct().Count());
    }

    [Fact]
    public void LauncherLookupMatchesOnPartIdAndRejectsAnythingElse()
    {
        Assert.Same(Arsenal.PantsirS1, Arsenal.LauncherForPart(Arsenal.PantsirS1.PartId));
        Assert.Null(Arsenal.LauncherForPart("SomeOtherMod_Prefab_Thing"));
        Assert.Null(Arsenal.LauncherForPart(null));
        Assert.Null(Arsenal.LauncherForPart(""));
    }

    [Fact]
    public void UnknownNamesFallBackRatherThanThrow()
    {
        // A launcher naming a round that does not exist is a typo in Arsenal, not a reason for
        // the game to fall over mid-flight.
        Assert.NotNull(Arsenal.MunitionNamed("no such round"));
        Assert.NotNull(Arsenal.SensorNamed("no such sensor"));
    }

    [Fact]
    public void EveryLauncherHasAsManyTubesAsItHasTubePositions()
    {
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            Assert.Equal(launcher.Tubes.Length, launcher.TubeCount);
        }
    }

    /// <summary>
    /// A launcher must be able to shoot with <em>something</em>: tubes, a cannon, or both.
    ///
    /// <para>This replaces an assertion that every launcher has at least one tube, which was true
    /// only while every launcher carried missiles. A CIWS is a gun and nothing else, and
    /// <c>TubeCount</c> of zero is a supported shape rather than a broken profile — but a launcher
    /// with neither is a part that can never fire, and nothing else would report that.</para>
    /// </summary>
    [Fact]
    public void EveryLauncherCanActuallyShootWithSomething()
    {
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            Assert.True(launcher.TubeCount > 0 || launcher.HasCannon,
                        $"{launcher.DisplayName} has neither tubes nor a cannon");
        }
    }

    [Fact]
    public void ATurretedLauncherDeclaresThePiecesItAnimates()
    {
        // A traverse has to carry something, or it turns and nothing follows. What it carries can
        // be pods or a cannon -- a CIWS traverses a gun and has no launcher assembly at all -- but
        // whichever it declares needs a trunnion offset, because a pivot measured from nothing is
        // an assembly that swings around the mount instead of elevating in place.
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            if (launcher.TurretMarker is null) continue;

            Assert.True(launcher.PodsMarker is not null || launcher.GunsMarker is not null,
                        $"{launcher.DisplayName} traverses but carries nothing that moves with it");

            if (launcher.PodsMarker is not null)
            {
                Assert.True(Vec.Len(launcher.PodPivotFromTurret) > 0.0,
                            $"{launcher.DisplayName} elevates pods with no trunnion offset");
            }

            if (launcher.GunsMarker is not null)
            {
                Assert.True(Vec.Len(launcher.GunPivotFromTurret) > 0.0,
                            $"{launcher.DisplayName} elevates guns with no trunnion offset");
            }
        }
    }

    [Fact]
    public void ProfilesAreSelectableAndDriveTheTurretLimits()
    {
        (MunitionProfile munition, SensorProfile sensor) = Arsenal.LoadoutFor(Arsenal.PantsirS1);

        Assert.Equal("57E6", munition.Name);
        Assert.Equal("1RS1", sensor.Name);

        var turret = new Turret();
        Arsenal.PantsirS1.ConfigureTurret(turret);
        Assert.Equal(float.DegreesToRadians(Arsenal.PantsirS1.MaxElevationDeg), turret.MaxElevationRad, 9);
        Assert.Equal(float.DegreesToRadians(Arsenal.PantsirS1.ForwardArcDeg), turret.ForwardArcRad, 9);
        Assert.Equal(float.DegreesToRadians(Arsenal.PantsirS1.ForwardPlateauDeg), turret.ForwardPlateauRad, 9);
    }

    /// <summary>
    /// The registry ships more than one system, and they have to resolve to different weapons.
    ///
    /// <para>Every other assertion in this file is satisfied by a registry of one, which is the
    /// state the suite was stuck in while the Pantsir was the only entry. This one distinguishes
    /// "picked the right system" from "picked the only system", and it is what a third entry
    /// inherits.</para>
    /// </summary>
    [Fact]
    public void TwoRegisteredSystemsResolveToDifferentWeapons()
    {
        Assert.True(Arsenal.Launchers.Count >= 2);

        (MunitionProfile pantsirRound, SensorProfile pantsirSet) = Arsenal.LoadoutFor(Arsenal.PantsirS1);
        (MunitionProfile railRound, SensorProfile railSet) = Arsenal.LoadoutFor(Arsenal.SidewinderRail);

        Assert.NotSame(pantsirRound, railRound);
        Assert.NotSame(pantsirSet, railSet);

        Assert.Same(Arsenal.SidewinderRail, Arsenal.LauncherForPart(Arsenal.SidewinderRail.PartId));
        Assert.Same(Arsenal.PantsirS1, Arsenal.LauncherForPart(Arsenal.PantsirS1.PartId));
    }

    /// <summary>
    /// The AIM-9J boosts hard and then coasts, which is what a Sidewinder does and what decides
    /// how it looks in flight.
    ///
    /// <para>The Mk 17 is a booster with no sustainer: about 2.2 s of thrust to roughly Mach 2.5,
    /// then drag for the rest of the flight. Written down because the alternative is not a
    /// slightly different number but a different weapon — a five-second burn holds speed like the
    /// Pantsir's two-stage round, which is exactly how this shipped and exactly what looked
    /// wrong.</para>
    /// </summary>
    [Fact]
    public void TheSidewinderBoostsBrieflyAndThenCoasts()
    {
        MunitionProfile round = Arsenal.Missile9J;

        // A booster, not a sustainer. Anything approaching the flight time is the wrong shape.
        Assert.InRange(round.BoostSeconds, 1.5f, 3.0f);
        Assert.True(round.BoostSeconds < round.MaxFlightSeconds / 10f);

        // Peak speed at burnout, before drag: about Mach 2.5 at sea level.
        double peak = round.LaunchSpeed + (round.BoostAccel * round.BoostSeconds);
        Assert.InRange(peak, 800.0, 900.0);

        // And it must actually bleed that off, or "coasts" means "holds speed forever".
        Assert.True(round.DragK > 0f);
    }

    /// <summary>
    /// The rail is the shipped example of a launcher with nothing that moves, so the shape
    /// <see cref="AFixedLauncherIsJustAProfileWithNothingThatMoves"/> describes has to hold for a
    /// real entry rather than only for one the test builds.
    /// </summary>
    [Fact]
    public void TheSidewinderRailIsAFixedLauncherAndSaysSo()
    {
        LauncherProfile rail = Arsenal.SidewinderRail;

        Assert.Null(rail.TurretMarker);
        Assert.Null(rail.PodsMarker);
        Assert.False(rail.Trains);
        Assert.False(rail.HasCannon);
        Assert.Equal(1, rail.TubeCount);

        // A fixed launcher's rounds have no pods to follow, so the tube is the only thing that
        // says which way they leave. Without a direction they would depart along whatever the
        // fallback produces, which for a rail is into the craft it is bolted to.
        Assert.True(rail.Tubes[0].HasOwnDirection);

        // Never refilled: the round count in the panel is the number of rails fitted.
        Assert.Equal(0f, rail.ReloadSeconds);

        // Leaves along the rail and pushed clear of the mount, rather than pivoting onto the
        // target at the muzzle. A rail has no walls to hold the round in, so separation is
        // outward as well as forward -- without it the round departs along the skin of whatever
        // carries it.
        Assert.True(rail.LaunchAlongTube);
        Assert.True(rail.EjectAwayFromMount > 0f);
        Assert.Equal(0f, rail.LaunchLoft);

        // And coasts before it steers, so the turn onto the target happens clear of the craft.
        Assert.True(Arsenal.MunitionNamed(rail.Munition).SeparationSeconds > 0f);
    }

    [Fact]
    public void AFixedLauncherIsJustAProfileWithNothingThatMoves()
    {
        // The shape a future non-turreted system takes. Nothing should require a turret.
        var fixedLauncher = new LauncherProfile
        {
            PartId = "Test_Prefab_FixedRail",
            DisplayName = "test rail",
            Munition = "57E6",
            Sensor = "1RS1",
            Tubes = [new(1, 0, 0), new(1, 0, 0.4)],
            LaunchAlongTube = false,
        };

        Assert.Null(fixedLauncher.TurretMarker);
        Assert.Null(fixedLauncher.PodsMarker);
        Assert.Equal(2, fixedLauncher.TubeCount);
        Assert.NotNull(Arsenal.MunitionNamed(fixedLauncher.Munition));
    }

    /// <summary>
    /// A prefab is one Part with its radar, optical head and cannon as SubParts, so the survey
    /// walking parts finds a launcher and stops. The roles it carries have to be declared, or a
    /// system with a camera reports as having none.
    /// </summary>
    [Fact]
    public void ThePantsirReportsTheRolesItCarriesInside()
    {
        List<SurveyedPart> parts =
            [new SurveyedPart(Arsenal.PantsirS1.PartId, default, doubleQuat.Identity)];

        WeaponInventory inv = WeaponSurvey.Survey(parts, Arsenal.Components);

        Assert.Equal(1, inv.CountOf(WeaponRole.Launcher));
        Assert.Equal(1, inv.CountOf(WeaponRole.Sensor));
        Assert.Equal(1, inv.CountOf(WeaponRole.Camera));
        Assert.Equal(1, inv.CountOf(WeaponRole.Gun));
        Assert.Equal(1, inv.CountOf(WeaponRole.FireControl));
    }

    /// <summary>
    /// Declared roles are not a licence to invent parts: a built-in still needs the part it is
    /// built into, so a craft carrying nothing of ours reports nothing.
    /// </summary>
    [Fact]
    public void BuiltInRolesNeedThePartTheyAreBuiltInto()
    {
        List<SurveyedPart> parts = [new SurveyedPart("SomeoneElsesTank", default, doubleQuat.Identity)];

        Assert.False(WeaponSurvey.Survey(parts, Arsenal.Components).IsWeaponSystem);
    }
}
