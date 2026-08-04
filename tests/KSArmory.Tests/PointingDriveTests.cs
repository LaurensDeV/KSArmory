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
            Vec.Dot(Vec.Unit(drive.Direction), TubeGeometry.OpticRestDirection), -1.0, 1.0));

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
        double3 behind = -TubeGeometry.OpticRestDirection;

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

        Assert.Equal(TubeGeometry.OpticRestDirection.Y, drive.Direction.Y, 9);
    }

    [Fact]
    public void RotationFromToCarriesTheRestDirectionOntoTheAim()
    {
        double3 aim = Vec.Unit(new double3(1, 2, -3));
        doubleQuat rotation = TubeGeometry.RotationFromTo(TubeGeometry.OpticRestDirection, aim);

        double3 pointed = rotation * TubeGeometry.OpticRestDirection;

        Assert.Equal(aim.X, pointed.X, 9);
        Assert.Equal(aim.Y, pointed.Y, 9);
        Assert.Equal(aim.Z, pointed.Z, 9);
    }
}
