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

    [Fact]
    public void TheCameraSitsFarBackWhileTheRoundIsStillFar()
        => Assert.Equal(22.0, ChaseView.StandOff(5_000, 2_000, 50, 22.0, 6.0), 1e-9);

    [Fact]
    public void AndClosesRightInAtTheEnd()
        => Assert.Equal(6.0, ChaseView.StandOff(10, 2_000, 50, 22.0, 6.0), 1e-9);

    [Fact]
    public void TheClosingIsMonotonic()
    {
        // A camera that comes in and then backs off again would read as a mistake, not a move.
        double previous = double.MaxValue;

        for (double range = 3_000; range >= 0; range -= 25)
        {
            double distance = ChaseView.StandOff(range, 2_000, 50, 22.0, 6.0);

            Assert.True(distance <= previous + 1e-9, $"backed off at {range} m");
            previous = distance;
        }
    }

    [Fact]
    public void AnUnknownRangeKeepsTheFullStandOff()
    {
        // An unguided round has nothing to converge on; it must not be filmed from six metres.
        Assert.Equal(22.0, ChaseView.StandOff(double.NaN, 2_000, 50, 22.0, 6.0), 1e-9);
    }

    [Fact]
    public void ClosingOnTimeStartsTheMomentTheChaseDoes()
    {
        // Normalised against the flight remaining when the view was taken, so the camera is
        // already easing in on the first frame rather than waiting for a distance threshold.
        const double atTake = 9.0;

        double first = ChaseView.StandOff(atTake, atTake, 0.35, 26.0, 7.0);
        double middle = ChaseView.StandOff(atTake / 2.0, atTake, 0.35, 26.0, 7.0);
        double last = ChaseView.StandOff(0.2, atTake, 0.35, 26.0, 7.0);

        Assert.Equal(26.0, first, 1e-9);
        Assert.True(middle < first - 1.0, $"had not started closing: {middle}");
        Assert.Equal(7.0, last, 1e-9);
    }

    [Fact]
    public void TheClosingAcceleratesIntoTheImpact()
    {
        // tomservo's point: a symmetric ease is flat at both ends, so it is slowest exactly where
        // the arrival happens. The last tenth of the flight must move the camera further than the
        // first tenth does.
        double atStart = ChaseView.StandOff(10.0, 10.0, 0.0, 26.0, 7.0)
                         - ChaseView.StandOff(9.0, 10.0, 0.0, 26.0, 7.0);

        double atImpact = ChaseView.StandOff(1.0, 10.0, 0.0, 26.0, 7.0)
                          - ChaseView.StandOff(0.0, 10.0, 0.0, 26.0, 7.0);

        Assert.True(atImpact > atStart * 2.0,
                    $"closing does not accelerate: {atStart:F2} m early against {atImpact:F2} m late");
    }
}
