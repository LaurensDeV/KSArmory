using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A recoiling barrel rides the cannon's trunnion and slides along the bore. A slide along any fixed
/// axis is indistinguishable from this at the pose the model was built in, and wrong everywhere else.
/// </summary>
public class BarrelPoseTests
{
    private static LauncherProfile Gun() => new()
    {
        PartId = "Test_Prefab_Gun",
        DisplayName = "test gun",
        Munition = "5IN54",
        Sensor = "MK68",
        Tubes = [],
        TurretMarker = "Turret",
        GunsMarker = "Guns",
        GunBarrelMarker = "Barrel",
        TurretPivot = new(0.017, 0.0, 0.0),
        GunPivotFromTurret = new(1.5, 0.5, 0.0),
        GunReferenceElevationRad = 0.2,
    };

    public static TheoryData<double, double> Lays => new()
    {
        { 0.0, 0.0 }, { 0.0, 0.2 }, { 1.1, 0.7 }, { -2.4, 1.3 }, { 3.0, -0.25 },
    };

    [Theory, MemberData(nameof(Lays))]
    public void UnrecoiledTheBarrelIsExactlyTheCannon(double bearing, double elevation)
    {
        DrivePose gun = TubeGeometry.GunPose(Gun(), bearing, elevation);
        DrivePose barrel = TubeGeometry.BarrelPose(Gun(), bearing, elevation, 0.0);

        Assert.True(Vec.Len(gun.Position - barrel.Position) < 1e-12);
        Assert.Equal(gun.Rotation, barrel.Rotation);
    }

    [Theory, MemberData(nameof(Lays))]
    public void RecoilRunsBackAlongTheBoreAtEveryLay(double bearing, double elevation)
    {
        const double recoil = 0.3;
        LauncherProfile p = Gun();
        DrivePose gun = TubeGeometry.GunPose(p, bearing, elevation);
        DrivePose barrel = TubeGeometry.BarrelPose(p, bearing, elevation, recoil);

        double3 bore = TubeGeometry.GunAxisPartFrame(p, gun.Rotation);
        double3 moved = barrel.Position - gun.Position;

        Assert.True(Vec.Len(moved + bore * recoil) < 1e-12,
            $"moved ({moved.X:F6},{moved.Y:F6},{moved.Z:F6}) against bore ({bore.X:F6},{bore.Y:F6},{bore.Z:F6})");
    }
}
