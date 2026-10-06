using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The M197's elevation actuators and belt, posed from the gun's elevation. Drawn only, so what
/// matters is that the rod stays on the gun's pin and on the line out of its cylinder.
/// </summary>
public class ActuatorLinkageTests
{
    private static readonly LauncherProfile Gun = Arsenal.M197;

    public static TheoryData<double, double> Poses() => new()
    {
        { 0.0, -10.0 }, { 0.0, 50.0 }, { 60.0, 25.0 }, { -110.0, 50.0 }, { 35.0, -5.0 },
    };

    [Fact]
    public void AtTheModelledElevationNothingMoves()
    {
        (DrivePose cylinder, DrivePose rod) = TubeGeometry.ActuatorPoses(Gun, 0.0, Gun.GunReferenceElevationRad);

        Assert.True(Vec.Len(cylinder.Position - (Gun.TurretPivot + Gun.GunCylinderPinFromTurret)) < 1e-12);
        Assert.True(Vec.Len(rod.Position - (Gun.TurretPivot + Gun.GunRodPinFromTurret)) < 1e-12);
        Assert.True(Vec.Len(rod.Rotation * new double3(0, 1, 0) - new double3(0, 1, 0)) < 1e-12);
    }

    [Theory]
    [MemberData(nameof(Poses))]
    public void TheRodRidesTheGunsPin(double bearingDeg, double elevationDeg)
    {
        double bearing = double.DegreesToRadians(bearingDeg), elevation = double.DegreesToRadians(elevationDeg);
        DrivePose gun = TubeGeometry.GunPose(Gun, bearing, elevation);
        double3 pinOnGun = gun.Position + gun.Rotation * (Gun.GunRodPinFromTurret - Gun.GunPivotFromTurret);

        (_, DrivePose rod) = TubeGeometry.ActuatorPoses(Gun, bearing, elevation);

        Assert.True(Vec.Len(rod.Position - pinOnGun) < 1e-9, $"{Vec.Len(rod.Position - pinOnGun):E2} m off the gun's pin");
    }

    [Theory]
    [MemberData(nameof(Poses))]
    public void CylinderAndRodStayOnTheLineBetweenThePins(double bearingDeg, double elevationDeg)
    {
        double bearing = double.DegreesToRadians(bearingDeg), elevation = double.DegreesToRadians(elevationDeg);
        (DrivePose cylinder, DrivePose rod) = TubeGeometry.ActuatorPoses(Gun, bearing, elevation);

        double3 modelledAxis = Vec.Unit(Gun.GunRodPinFromTurret - Gun.GunCylinderPinFromTurret);
        double3 between = Vec.Unit(rod.Position - cylinder.Position);

        Assert.True(Vec.Len(cylinder.Rotation * modelledAxis - between) < 1e-9);
        Assert.True(Vec.Len(rod.Rotation * modelledAxis - between) < 1e-9);
    }

    [Fact]
    public void DepressingTheGunExtendsTheRodAndRaisingItRetracts()
    {
        double rest = ActuatorLinkage.Length(Gun.GunCylinderPinFromTurret, Gun.GunRodPinFromTurret);
        double Length(double elevationDeg)
        {
            (DrivePose c, DrivePose r) = TubeGeometry.ActuatorPoses(Gun, 0.0, double.DegreesToRadians(elevationDeg));
            return ActuatorLinkage.Length(c.Position, r.Position);
        }

        Assert.InRange(Length(Gun.MaxElevationDeg) - rest, 0.35, 0.45);
        Assert.True(Length(Gun.MinElevationDeg) < rest);
    }

    [Fact]
    public void TheBeltTurnsAFifthOfTheGun()
    {
        DrivePose belt = TubeGeometry.FeedPose(Gun, 0.0, double.DegreesToRadians(50));
        double3 forward = belt.Rotation * new double3(0, 1, 0);

        Assert.Equal(10.0, double.RadiansToDegrees(Math.Atan2(forward.X, forward.Y)), 6);
    }
}

/// <summary>The M197's barrels spin up while it fires, run down after, and stay on the gun.</summary>
public class GunRotorTests
{
    private static readonly LauncherProfile Gun = Arsenal.M197;

    [Fact]
    public void AThirdOfATurnPutsEachBarrelWhereTheNextOneWas()
    {
        // The muzzles are modelled at rest, so a rotor turning about any other axis would carry
        // the barrels off the positions the rounds leave from.
        double3 pivot = Gun.GunRotorPivotFromTurret - Gun.GunPivotFromTurret;
        doubleQuat third = doubleQuat.CreateFromAxisAngle(TubeGeometry.GunAxisGunFrame(Gun), Math.Tau / 3.0);

        foreach (double3 muzzle in Gun.GunMuzzles)
        {
            double3 moved = pivot + third * (muzzle - pivot);
            double nearest = Gun.GunMuzzles.Min(m => Vec.Len(m - moved));
            Assert.True(nearest < 1e-3, $"a barrel lands {nearest * 1000:F1} mm from any other");
        }
    }

    [Fact]
    public void ItSpinsUpWhileFiringAndRunsDownAfter()
    {
        var rotor = new GunRotor();
        double full = GunRotor.FiringRateRadPerSec(Gun);

        for (int i = 0; i < 20; i++) rotor.Update(0.02, true, full, Gun.GunRotorSpinUpSeconds, Gun.GunRotorSpinDownSeconds);
        Assert.Equal(full, rotor.RateRadPerSec, 9);
        Assert.Equal(730.0 / 3.0, full * 60.0 / Math.Tau, 3);

        for (int i = 0; i < 25; i++) rotor.Update(0.02, false, full, Gun.GunRotorSpinUpSeconds, Gun.GunRotorSpinDownSeconds);
        Assert.Equal(full / 2.0, rotor.RateRadPerSec, 6);

        for (int i = 0; i < 100; i++) rotor.Update(0.02, false, full, Gun.GunRotorSpinUpSeconds, Gun.GunRotorSpinDownSeconds);
        Assert.Equal(0.0, rotor.RateRadPerSec, 9);
    }

    [Fact]
    public void TheClusterRidesTheGun()
    {
        double bearing = double.DegreesToRadians(40), elevation = double.DegreesToRadians(30);
        DrivePose gun = TubeGeometry.GunPose(Gun, bearing, elevation);
        DrivePose rotor = TubeGeometry.RotorPose(Gun, bearing, elevation, 1.234);

        double3 pivotOnGun = gun.Position + gun.Rotation * (Gun.GunRotorPivotFromTurret - Gun.GunPivotFromTurret);
        Assert.True(Vec.Len(rotor.Position - pivotOnGun) < 1e-9);
        double3 bore = gun.Rotation * TubeGeometry.GunAxisGunFrame(Gun);
        Assert.True(Vec.Len(rotor.Rotation * TubeGeometry.GunAxisGunFrame(Gun) - bore) < 1e-9);
    }
}
