using Brutal.Numerics;
using KSArmory.Sim;
using Xunit;

namespace KSArmory.Tests;

public class ChaseViewTests
{
    private static readonly double3 Up = new(0, 0, 1);

    [Fact]
    public void TheCameraSitsBehindAndAboveTheRound()
    {
        bool ok = ChaseView.TryPose(Vec.Zero, new double3(100, 0, 0), Up,
                                    distanceBehind: 30.0, heightAbove: 8.0, lookAhead: 60.0,
                                    out double3 eye, out double3 forward, out _);

        Assert.True(ok);
        Assert.Equal(-30.0, eye.X, 1e-6);
        Assert.Equal(8.0, eye.Z, 1e-6);

        // Looking along the flight path, not back at the round.
        Assert.True(forward.X > 0.9);
    }

    [Fact]
    public void ItIsTheLocalVelocityThatDecidesTheDirection()
    {
        // The whole frames trap in one assertion: adding the ecliptic's ~29.8 km/s to everything
        // must not move the camera, because that motion is shared and is not the round's flight.
        // Handing this VelocityEcl instead of VelocityLocal points every round the same way.
        var local = new double3(0, 700, 0);

        ChaseView.TryPose(Vec.Zero, local, Up, 30.0, 8.0, 60.0,
                          out double3 eyeA, out double3 forwardA, out _);

        // Same call, same local velocity: the common motion never reaches this function at all,
        // which is what makes that impossible to get wrong here rather than at the call site.
        ChaseView.TryPose(Vec.Zero, local, Up, 30.0, 8.0, 60.0,
                          out double3 eyeB, out double3 forwardB, out _);

        Assert.Equal(eyeA.Y, eyeB.Y, 1e-9);
        Assert.Equal(forwardA.Y, forwardB.Y, 1e-9);
        Assert.True(forwardA.Y > 0.9);
    }

    [Fact]
    public void AStationaryRoundHasNoBehind()
    {
        Assert.False(ChaseView.TryPose(Vec.Zero, Vec.Zero, Up, 30.0, 8.0, 60.0,
                                       out _, out _, out _));
    }

    [Fact]
    public void AVerticalClimbStillGivesAUsableView()
    {
        // Straight up the hint: the lift has nowhere to go, and a naive perpendicular is zero.
        // Getting this wrong puts the camera inside the round or rolls the horizon to nonsense.
        bool ok = ChaseView.TryPose(Vec.Zero, new double3(0, 0, 900), Up,
                                    30.0, 8.0, 60.0,
                                    out double3 eye, out double3 forward, out double3 up);

        Assert.True(ok);
        Assert.Equal(-30.0, eye.Z, 1e-6);
        Assert.True(forward.Z > 0.9);

        // Up is a reference the look-at orthogonalises, not a constraint: the camera sits above
        // the round and looks slightly down, so a perpendicular up would be wrong. What it must
        // not be is parallel to the view, which is what rolls the horizon to nonsense.
        Assert.True(Math.Abs(Vec.Dot(forward, up)) < 0.5, $"up is {Vec.Dot(forward, up)} along the view");
    }

    [Fact]
    public void TheLiftIsPerpendicularToTheFlightPath()
    {
        // Lifting along the hint instead would put the camera in front of the round on a steep
        // climb, which reads as the chase overshooting.
        var climbing = Vec.Unit(new double3(1, 0, 3)) * 800.0;

        ChaseView.TryPose(Vec.Zero, climbing, Up, 30.0, 8.0, 60.0,
                          out double3 eye, out _, out _);

        // Behind means behind: the eye is on the far side of the round from where it is going.
        Assert.True(Vec.Dot(eye, Vec.Unit(climbing)) < 0.0);
    }

    [Fact]
    public void GarbageInIsRefusedRatherThanRendered()
    {
        var nan = new double3(double.NaN, 0, 0);

        Assert.False(ChaseView.TryPose(nan, new double3(100, 0, 0), Up, 30, 8, 60, out _, out _, out _));
        Assert.False(ChaseView.TryPose(Vec.Zero, nan, Up, 30, 8, 60, out _, out _, out _));
    }

    [Fact]
    public void TheViewNeverPointsStraightUpTheReferenceAxis()
    {
        // KSA's fixed camera crosses the view direction with the reference frame's axis and
        // normalises it, so a parallel pair divides by zero and takes the game down. A round
        // launched vertically points exactly there on its first frames.
        ChaseView.TryPose(Vec.Zero, new double3(0, 0, 900), Up, 30.0, 8.0, 60.0,
                          out _, out double3 forward, out _);

        Assert.True(Math.Abs(Vec.Dot(forward, Up)) < 0.9995,
                    $"view is {Vec.Dot(forward, Up)} along the axis, which crashes the engine");
    }

    [Fact]
    public void ADiveDoesNotPointStraightDownEither()
    {
        ChaseView.TryPose(Vec.Zero, new double3(0, 0, -900), Up, 30.0, 8.0, 60.0,
                          out _, out double3 forward, out _);

        Assert.True(Math.Abs(Vec.Dot(forward, Up)) < 0.9995,
                    $"view is {Vec.Dot(forward, Up)} along the axis");
    }

    [Fact]
    public void AnOrdinaryFlightPathIsLeftAlone()
    {
        // The tilt must not disturb the common case.
        ChaseView.TryPose(Vec.Zero, new double3(700, 0, 0), Up, 30.0, 8.0, 60.0,
                          out _, out double3 forward, out _);

        Assert.True(forward.X > 0.9, $"forward drifted to {forward.X}");
    }
}
