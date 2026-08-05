using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What happens when the engine refuses a subpart transform write.
///
/// <para>The refusal is invisible in flight: the mesh freezes at its last accepted pose while the
/// drive model carries on, so the launcher looks stopped rather than broken and rounds still
/// leave tubes. Only the drawn facing line disagrees. These pin the two rules that keep that from
/// turning into rounds fired along a stale transform.</para>
/// </summary>
public class DriveFailureTests
{
    private static LauncherProfile Profile(string? turretMarker = null, double podPoseRad = 0.0) => new()
    {
        PartId = "Test_Prefab_Rail",
        DisplayName = "test rail",
        Munition = "57E6",
        Sensor = "1RS1",
        Tubes = [new(1, 0, 0), new(1, 0, 0.4)],
        TurretMarker = turretMarker,
        PodReferenceElevationRad = podPoseRad,
    };

    [Fact]
    public void ARefusedChannelDoesNotFreezeTheOthers()
    {
        var drives = new DriveStatus();
        drives.Refuse(DriveChannel.Radar);

        Assert.False(drives.Works(DriveChannel.Radar));
        Assert.True(drives.Works(DriveChannel.Turret));
        Assert.True(drives.Works(DriveChannel.Pods));
        Assert.True(drives.Works(DriveChannel.Guns));
        Assert.True(drives.AimingAccepted);
    }

    [Fact]
    public void RefusingReportsOnlyTheFirstTimeSoTheLogCarriesOneLine()
    {
        var drives = new DriveStatus();

        Assert.True(drives.Refuse(DriveChannel.Pods));
        Assert.False(drives.Refuse(DriveChannel.Pods));
        Assert.True(drives.Refuse(DriveChannel.Guns));
    }

    [Fact]
    public void EitherAimingChannelFailingTakesTheLauncherOffTarget()
    {
        var turretGone = new DriveStatus();
        turretGone.Refuse(DriveChannel.Turret);
        Assert.False(turretGone.AimingAccepted);

        var podsGone = new DriveStatus();
        podsGone.Refuse(DriveChannel.Pods);
        Assert.False(podsGone.AimingAccepted);
    }

    [Fact]
    public void ClearForgetsEveryChannel()
    {
        var drives = new DriveStatus();
        drives.Refuse(DriveChannel.Turret);
        drives.Refuse(DriveChannel.Radar);
        Assert.True(drives.AnyRefused);

        drives.Clear();

        Assert.False(drives.AnyRefused);
        Assert.True(drives.AimingAccepted);
    }

    /// <summary>
    /// The case that matters: a launcher that should be aiming and cannot must hold fire. Treating
    /// it as laid ejects rounds along whatever transform the tubes froze at, and guidance recovers
    /// from that well enough that nothing downstream reports a problem.
    /// </summary>
    [Fact]
    public void ARefusedDriveHoldsFire()
    {
        Assert.False(FireGate.IsLaid(aiming: true, trains: true, drivesAccepted: false,
                                     assembliesResolved: true, settled: true));
    }

    [Fact]
    public void ALauncherWithNothingToAimIsAlwaysLaid()
    {
        Assert.True(FireGate.IsLaid(aiming: true, trains: false, drivesAccepted: true,
                                    assembliesResolved: false, settled: false));
    }

    [Fact]
    public void StowedOrHandDrivenLauncherDoesNotWaitToBeLaid()
    {
        Assert.True(FireGate.IsLaid(aiming: false, trains: true, drivesAccepted: false,
                                    assembliesResolved: false, settled: false));
    }

    [Fact]
    public void AnUnresolvedElevatingSubpartHoldsFire()
    {
        Assert.False(FireGate.IsLaid(aiming: true, trains: true, drivesAccepted: true,
                                     assembliesResolved: false, settled: true));
    }

    [Fact]
    public void ALaidLauncherFires()
    {
        Assert.True(FireGate.IsLaid(aiming: true, trains: true, drivesAccepted: true,
                                    assembliesResolved: true, settled: true));
    }

    [Fact]
    public void ATrainingLauncherStillWaitsForTheDrivesToSettle()
    {
        Assert.False(FireGate.IsLaid(aiming: true, trains: true, drivesAccepted: true,
                                     assembliesResolved: true, settled: false));
    }

    [Fact]
    public void AProfileDeclaringNoMovingGearDoesNotTrain()
    {
        LauncherProfile fixedRail = Profile();

        Assert.False(fixedRail.Trains);
        Assert.True(FireGate.IsLaid(aiming: true, trains: fixedRail.Trains, drivesAccepted: true,
                                    assembliesResolved: false, settled: false));
    }

    [Fact]
    public void ALauncherStowsToItsOwnModelledPoseNotThePantsirs()
    {
        LauncherProfile profile = Profile("turret", double.DegreesToRadians(30));

        var turret = new Turret();
        profile.ConfigureTurret(turret);
        turret.Stow();

        Assert.Equal(double.DegreesToRadians(30), turret.CommandElevationRad!.Value, 9);
    }

    /// <summary>
    /// Stowing goes through the depression floor, so a launcher modelled below its own forward
    /// cutout can never sit at the pose its mesh was built in — and the point of the reference
    /// convention is that a refused elevation write leaves the vehicle looking right.
    /// </summary>
    [Fact]
    public void EveryRegisteredLauncherCanStowToItsOwnModelledPose()
    {
        foreach (LauncherProfile profile in Arsenal.Launchers)
        {
            if (!profile.Trains) continue;

            var turret = new Turret();
            profile.ConfigureTurret(turret);
            turret.Stow();

            Assert.Equal(profile.RestElevationRad, turret.CommandElevationRad!.Value, 9);
        }
    }

    [Fact]
    public void RestElevationOverridesThePodPoseWhenItIsSet()
    {
        LauncherProfile profile = Profile("turret", double.DegreesToRadians(30));
        profile.RestElevationDeg = 30f;

        var turret = new Turret();
        profile.ConfigureTurret(turret);
        turret.Stow();

        Assert.Equal(double.DegreesToRadians(30), turret.CommandElevationRad!.Value, 9);
    }

    /// <summary>
    /// The cannon and the missiles share only the traverse, so a refused pod elevation must not
    /// silence a gun whose own drive the engine is still accepting.
    /// </summary>
    [Fact]
    public void ARefusedPodDriveDoesNotStopTheCannonAiming()
    {
        var drives = new DriveStatus();
        drives.Refuse(DriveChannel.Pods);

        Assert.False(drives.AimingAccepted);
        Assert.True(drives.GunAimingAccepted);
    }

    [Fact]
    public void ARefusedGunDriveDoesNotStopTheMissilesAiming()
    {
        var drives = new DriveStatus();
        drives.Refuse(DriveChannel.Guns);

        Assert.True(drives.AimingAccepted);
        Assert.False(drives.GunAimingAccepted);
    }

    /// <summary>The traverse is shared, so losing it stops both.</summary>
    [Fact]
    public void ARefusedTraverseStopsBoth()
    {
        var drives = new DriveStatus();
        drives.Refuse(DriveChannel.Turret);

        Assert.False(drives.AimingAccepted);
        Assert.False(drives.GunAimingAccepted);
    }
}
