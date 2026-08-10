using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The optical head's gimbal. Unlike the turret it has no axes, which is deliberate: an
/// air-defence sight spends its time looking near straight up, and bearing-and-elevation has a
/// singularity exactly there.
/// </summary>
public class PointingDriveTests
{
    private static readonly double Rate = double.DegreesToRadians(90);

    /// <summary>
    /// The reason this type exists. Writing the commanded direction straight to the part put the
    /// head on a new track within one frame, which reads as a glitch rather than as a sensor.
    /// </summary>
    [Fact]
    public void TheHeadSweepsOntoATrackRatherThanSnapping()
    {
        var drive = new PointingDrive();
        double3 behind = new(0, -1, 0);            // a full half turn away

        drive.Update(0.016, behind, Rate);

        double turned = Math.Acos(Math.Clamp(
            Vec.Dot(Vec.Unit(drive.Direction), OpticGeometry.RestDirection), -1.0, 1.0));

        Assert.True(turned <= Rate * 0.016 + 1e-9,
                    $"turned {double.RadiansToDegrees(turned):F1}° in one frame at 90°/s");
        Assert.False(drive.OnTarget);
    }

    [Fact]
    public void ItArrivesAndStops()
    {
        var drive = new PointingDrive();
        double3 command = Vec.Unit(new double3(1, 1, 0));

        for (int i = 0; i < 200; i++) drive.Update(0.016, command, Rate);

        Assert.True(drive.OnTarget);
        Assert.Equal(command.X, drive.Direction.X, 6);
        Assert.Equal(command.Y, drive.Direction.Y, 6);
        Assert.Equal(command.Z, drive.Direction.Z, 6);
    }

    /// <summary>
    /// Straight up is where an air-defence sight lives, and it is the pole an axis-based drive
    /// cannot express. The head must pass through it without a singularity.
    /// </summary>
    [Fact]
    public void LookingStraightUpIsNotSpecial()
    {
        var drive = new PointingDrive();
        double3 up = new(1, 0, 0);

        for (int i = 0; i < 200; i++) drive.Update(0.016, up, Rate);

        Assert.True(drive.OnTarget);
        Assert.True(Vec.IsFinite(drive.Direction));
        Assert.Equal(1.0, Vec.Len(drive.Direction), 6);
    }

    [Fact]
    public void ExactlyBehindTurnsRatherThanGoingNaN()
    {
        var drive = new PointingDrive();
        double3 behind = -OpticGeometry.RestDirection;

        for (int i = 0; i < 300; i++) drive.Update(0.016, behind, Rate);

        Assert.True(Vec.IsFinite(drive.Direction));
        Assert.True(drive.OnTarget);
    }

    [Fact]
    public void AJunkCommandLeavesTheHeadWhereItWas()
    {
        var drive = new PointingDrive();
        drive.Update(0.5, new double3(1, 1, 0), Rate);
        double3 before = drive.Direction;

        drive.Update(0.5, Vec.Zero, Rate);
        drive.Update(0.5, new double3(double.NaN, 0, 0), Rate);

        Assert.Equal(before.X, drive.Direction.X, 9);
        Assert.Equal(before.Y, drive.Direction.Y, 9);
        Assert.Equal(before.Z, drive.Direction.Z, 9);
    }

    [Fact]
    public void ANonPositiveStepDoesNotMoveIt()
    {
        var drive = new PointingDrive();
        drive.Update(0.0, new double3(0, -1, 0), Rate);
        drive.Update(-1.0, new double3(0, -1, 0), Rate);

        Assert.Equal(OpticGeometry.RestDirection.Y, drive.Direction.Y, 9);
    }

    [Fact]
    public void RotationFromToCarriesTheRestDirectionOntoTheAim()
    {
        double3 aim = Vec.Unit(new double3(1, 2, -3));
        doubleQuat rotation = TubeGeometry.RotationFromTo(OpticGeometry.RestDirection, aim);

        double3 pointed = rotation * OpticGeometry.RestDirection;

        Assert.Equal(aim.X, pointed.X, 9);
        Assert.Equal(aim.Y, pointed.Y, 9);
        Assert.Equal(aim.Z, pointed.Z, 9);
    }

    /// <summary>
    /// Both ends legal is not enough. Turning from one bearing to the opposite one at low
    /// elevation takes the shortest rotation, and that arc goes over the top or under the bottom
    /// — so a head with a depression floor sweeps its window through its own mast getting there
    /// unless the caller re-clamps what it actually reached.
    /// </summary>
    [Fact]
    public void ALimitedHeadNeedsItsPathClampedAndNotOnlyItsCommand()
    {
        OpticProfile director = new()
        {
            PartId = "test",
            DisplayName = "test",
            Sensor = "test",
            BaseMarker = "Base",
            HeadMarker = "Head",
            HeadPivot = new(0.63, 0.0, 0.0),
            MinElevationDeg = -20f,
            MaxElevationDeg = 85f,
        };

        // Both ends depressed 15 degrees, five above the floor, on bearings 170 degrees apart.
        // The shortest rotation between two directions at the same depression bulges *away* from
        // the mount plane, so the arc between them dips well past the floor even though neither
        // end does. Exactly opposed would not do: the cross product vanishes there and the drive
        // picks an arbitrary perpendicular, which happens to stay level and proves nothing.
        static double3 At(double bearingDeg, double elevationDeg)
        {
            double b = double.DegreesToRadians(bearingDeg);
            double e = double.DegreesToRadians(elevationDeg);
            return Vec.Unit(new double3(Math.Sin(e), Math.Cos(e) * Math.Cos(b),
                                        Math.Cos(e) * Math.Sin(b)));
        }

        double3 from = At(0.0, -15.0);
        double3 to = At(170.0, -15.0);

        PointingDrive loose = new();
        PointingDrive held = new();
        loose.Hold(from);
        held.Hold(from);

        double floor = float.DegreesToRadians(director.MinElevationDeg);
        double worstLoose = 0.0;
        double worstHeld = 0.0;

        for (int step = 0; step < 400; step++)
        {
            loose.Update(1.0 / 60.0, to, director.SlewRateRad);

            held.Update(1.0 / 60.0, to, director.SlewRateRad);
            held.Hold(OpticGeometry.ClampToTravel(director, held.Direction));

            worstLoose = Math.Min(worstLoose, OpticGeometry.ElevationRad(loose.Direction));
            worstHeld = Math.Min(worstHeld, OpticGeometry.ElevationRad(held.Direction));
        }

        // Unconstrained, the shortest arc between two opposed level directions passes through a
        // pole -- which is straight down through the mount.
        Assert.True(worstLoose < floor - 0.2,
            $"the unclamped path only reached {double.RadiansToDegrees(worstLoose):F1} deg, "
            + "so this test is not exercising the fault it is named for");

        Assert.True(worstHeld >= floor - 1e-6,
            $"the clamped path dipped to {double.RadiansToDegrees(worstHeld):F1} deg, "
            + $"below the {director.MinElevationDeg:F0} deg floor");
    }
}
