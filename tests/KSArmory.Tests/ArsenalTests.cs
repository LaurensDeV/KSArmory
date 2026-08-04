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
            Assert.True(launcher.TubeCount > 0, $"{launcher.DisplayName} has no tubes");
        }
    }

    [Fact]
    public void ATurretedLauncherDeclaresThePiecesItAnimates()
    {
        // Traverse without pods would leave the tubes behind when the turret moved, and the
        // pod pivot is meaningless without a turret pivot to measure it from.
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            if (launcher.TurretMarker is null) continue;

            Assert.NotNull(launcher.PodsMarker);
            Assert.True(Vec.Len(launcher.PodPivotFromTurret) > 0.0,
                        $"{launcher.DisplayName} elevates but has no trunnion offset");
        }
    }

    [Fact]
    public void ProfilesAreSelectableAndDriveTheTurretLimits()
    {
        var config = new Config();
        config.Select(Arsenal.PantsirS1);

        Assert.Same(Arsenal.PantsirS1, config.Launcher);
        Assert.Equal("57E6", config.Munition.Name);
        Assert.Equal("1RS1", config.Sensor.Name);

        var turret = new Turret();
        config.Launcher.ConfigureTurret(turret);
        Assert.Equal(float.DegreesToRadians(Arsenal.PantsirS1.MaxElevationDeg), turret.MaxElevationRad, 9);
        Assert.Equal(float.DegreesToRadians(Arsenal.PantsirS1.ForwardArcDeg), turret.ForwardArcRad, 9);
        Assert.Equal(float.DegreesToRadians(Arsenal.PantsirS1.ForwardPlateauDeg), turret.ForwardPlateauRad, 9);
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
}
