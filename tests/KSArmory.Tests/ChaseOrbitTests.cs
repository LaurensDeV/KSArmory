using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Looking around a chased round with the mouse, and the view settling back behind it.
/// </summary>
public class ChaseOrbitTests
{
    private const double Dt = 1.0 / 60.0;

    private static readonly double3 Up = new(0, 0, 1);
    private static readonly double3 EngineAxis = new(0, 0, 1);

    // The chase's own pose for a round flying level along +X: behind, above, looking past it.
    private static void Pose(out double3 eye, out double3 forward, out double3 up)
    {
        Assert.True(ChaseView.TryPose(Vec.Zero, new double3(200, 0, 0), aimEcl: null, Up, EngineAxis,
                                      distanceBehind: 26.0, heightAbove: 6.0, lookAhead: 120.0,
                                      out eye, out forward, out up));
    }

    [Fact]
    public void LevelItIsThePoseItWasGiven()
    {
        Pose(out double3 eye, out double3 forward, out double3 up);

        ChaseView.Orbit(eye, forward, up, Up, 0.0, 0.0, 1.0, double.NegativeInfinity, EngineAxis,
                        out double3 e, out double3 f, out double3 u);

        Assert.Equal(eye, e);
        Assert.Equal(forward, f);
        Assert.Equal(up, u);
    }

    /// <summary>
    /// The whole rig turns, so where the round sits in the picture does not move: dragging round
    /// it looks at it from somewhere else, rather than looking away from it.
    /// </summary>
    [Fact]
    public void TheRoundStaysWhereItWasInThePicture()
    {
        Pose(out double3 eye, out double3 forward, out double3 up);

        ChaseView.Orbit(eye, forward, up, Up, yaw: 1.1, pitch: 0.4, zoom: 1.7, double.NegativeInfinity,
                        EngineAxis, out double3 e, out double3 f, out double3 u);

        double3 toRoundWas = Vec.Unit(-eye);
        double3 toRoundNow = Vec.Unit(-e);

        Assert.Equal(Vec.Dot(forward, toRoundWas), Vec.Dot(f, toRoundNow), 9);
        Assert.Equal(Vec.Dot(up, toRoundWas), Vec.Dot(u, toRoundNow), 9);
        Assert.Equal(Vec.Len(eye) * 1.7, Vec.Len(e), 6);
    }

    /// <summary>Dragging down lifts the camera to look from above, as KSA's orbit camera does.</summary>
    [Fact]
    public void APositivePitchRaisesTheEye()
    {
        Pose(out double3 eye, out double3 forward, out double3 up);

        ChaseView.Orbit(eye, forward, up, Up, 0.0, 0.5, 1.0, double.NegativeInfinity, EngineAxis,
                        out double3 raised, out double3 lookingDown, out _);

        Assert.True(raised.Z > eye.Z, $"eye went from {eye.Z:F1} m to {raised.Z:F1} m");
        Assert.True(lookingDown.Z < forward.Z);
    }

    /// <summary>A view turned into the ground stops at the floor, and the yaw still happens.</summary>
    [Fact]
    public void ThePitchGivesWayToTheFloor()
    {
        Pose(out double3 eye, out double3 forward, out double3 up);

        ChaseView.Orbit(eye, forward, up, Up, yaw: 0.8, pitch: -1.3, zoom: 1.0, lowestEye: 0.0, EngineAxis,
                        out double3 e, out _, out _);

        Assert.True(e.Z >= -1e-6, $"the eye went {-e.Z:F2} m under the floor");
        Assert.True(Math.Abs(Math.Atan2(e.Y, e.X) - Math.Atan2(eye.Y, eye.X)) > 0.5, "the yaw was refused with the pitch");
    }

    [Fact]
    public void APressHasToMoveBeforeItDrags()
    {
        ChaseOrbit orbit = new();
        orbit.Move(500, 400);
        orbit.Press();
        orbit.Move(501, 400);

        Assert.False(orbit.Dragging);

        orbit.Advance(Dt, buttonHeld: true);
        Assert.True(orbit.IsLevel);

        orbit.Move(503, 400);
        Assert.True(orbit.Dragging);
    }

    [Fact]
    public void ADragTurnsItAtKsasOwnRate()
    {
        ChaseOrbit orbit = new();
        orbit.Move(500, 400);
        orbit.Press();
        orbit.Move(600, 450);
        orbit.Advance(Dt, buttonHeld: true);

        Assert.Equal(-100 * ChaseOrbit.RadiansPerPixel, orbit.Yaw, 12);
        Assert.Equal(50 * ChaseOrbit.RadiansPerPixel, orbit.Pitch, 12);
    }

    /// <summary>
    /// Let go, it stays put for a moment and then goes back behind the round, arriving exactly
    /// rather than approaching it for ever.
    /// </summary>
    [Fact]
    public void LettingGoEasesItBackBehindTheRound()
    {
        ChaseOrbit orbit = new();
        orbit.Move(0, 0);
        orbit.Press();
        orbit.Move(400, -200);
        orbit.Advance(Dt, buttonHeld: true);
        orbit.Release();

        double yaw = orbit.Yaw;
        orbit.Advance(ChaseOrbit.HoldSeconds * 0.5, buttonHeld: false);
        Assert.Equal(yaw, orbit.Yaw);

        double last = Math.Abs(orbit.Yaw);
        for (int i = 0; i < 60 * 5; i++)
        {
            orbit.Advance(Dt, buttonHeld: false);
            Assert.True(Math.Abs(orbit.Yaw) <= last, "the way back reversed");
            last = Math.Abs(orbit.Yaw);
        }

        Assert.True(orbit.IsLevel, $"still {orbit.Yaw:F5} rad off after five seconds");
    }

    /// <summary>
    /// A release made over a panel never reaches the camera, and the drag must end regardless
    /// rather than holding the view off the round until the next right-click.
    /// </summary>
    [Fact]
    public void ADragEndsWhenTheButtonIsNoLongerHeld()
    {
        ChaseOrbit orbit = new();
        orbit.Move(0, 0);
        orbit.Press();
        orbit.Move(80, 0);
        orbit.Advance(Dt, buttonHeld: true);

        orbit.Advance(Dt, buttonHeld: false);
        Assert.False(orbit.Dragging);

        for (int i = 0; i < 60 * 5; i++) orbit.Advance(Dt, buttonHeld: false);
        Assert.True(orbit.IsLevel);
    }

    [Fact]
    public void TheWayBackIsTheShortWayRound()
    {
        ChaseOrbit orbit = new();
        orbit.Move(0, 0);
        orbit.Press();
        orbit.Move(-1500, 0);                   // 4.5 rad, which is 1.78 rad short of a whole turn
        orbit.Advance(Dt, buttonHeld: true);

        Assert.InRange(orbit.Yaw, -Math.PI, Math.PI);
        Assert.Equal(4.5 - (2.0 * Math.PI), orbit.Yaw, 9);
    }

    [Fact]
    public void TheWheelStepsItInAndOutByKsasFactor()
    {
        ChaseOrbit orbit = new();
        orbit.Scroll(1.0);
        orbit.Advance(Dt, buttonHeld: false);

        Assert.Equal(1.0 / ChaseOrbit.ZoomPerNotch, orbit.Zoom, 12);
    }

    [Fact]
    public void ThePitchStopsShortOfStraightOver()
    {
        ChaseOrbit orbit = new();
        orbit.Move(0, 0);
        orbit.Press();
        orbit.Move(0, 5000);
        orbit.Advance(Dt, buttonHeld: true);

        Assert.Equal(ChaseOrbit.MaxPitchRad, orbit.Pitch, 12);
    }
}
