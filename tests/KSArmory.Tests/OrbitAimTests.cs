using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Recovering the orbit camera's angles from its own basis.
///
/// <para>These reconstruct what KSA's OrbitController does — build a forward by rotating a
/// horizontal about the frame's vertical by the azimuth, then about the camera's right by the
/// elevation — and assert that solving for a direction and feeding the answer back through that
/// construction lands on it. That round trip is the whole contract: the frame the angles are
/// measured in is private, so the only way to aim the camera is to invert its own output.</para>
/// </summary>
public class OrbitAimTests
{
    // The controller's construction, reproduced so a solved pair can be checked against it.
    private static (double3 Forward, double3 Right) Build(double3 frameX, double3 frameUp,
                                                          double azimuth, double elevation)
    {
        double3 horizontal = Rotate(frameX, frameUp, azimuth);
        double3 right = Vec.Unit(Vec.Cross(horizontal, frameUp));
        double3 forward = Rotate(horizontal, right, elevation);
        return (forward, right);
    }

    private static double3 Rotate(double3 v, double3 axis, double a)
    {
        double c = Math.Cos(a), s = Math.Sin(a);
        return v * c + Vec.Cross(axis, v) * s + axis * (Vec.Dot(axis, v) * (1.0 - c));
    }

    private static readonly double3 FrameX = new(1, 0, 0);
    private static readonly double3 FrameUp = new(0, 0, 1);

    [Theory]
    [InlineData(0.0, 0.0, 1.2, 0.3)]
    [InlineData(0.7, -0.2, -2.0, 0.9)]
    [InlineData(-2.5, 0.4, 0.1, -0.6)]
    [InlineData(3.0, 1.0, -3.0, -1.0)]
    public void SolvingForADirectionReproducesIt(double azimuth, double elevation,
                                                 double wantAzimuth, double wantElevation)
    {
        (double3 forward, double3 right) = Build(FrameX, FrameUp, azimuth, elevation);
        (double3 desired, _) = Build(FrameX, FrameUp, wantAzimuth, wantElevation);

        Assert.True(OrbitAim.TrySolve(forward, right, azimuth, elevation, desired,
                                      out double toAz, out double toEl));

        (double3 got, _) = Build(FrameX, FrameUp, toAz, toEl);

        // The angles may differ by a turn; the direction is what matters.
        Assert.True(Vec.Len(got - desired) < 1e-6,
                    $"aimed {Vec.Len(got - desired):E2} away from the target direction");
    }

    [Fact]
    public void AimingAtWhereItAlreadyLooksChangesNothing()
    {
        (double3 forward, double3 right) = Build(FrameX, FrameUp, 0.4, 0.2);

        Assert.True(OrbitAim.TrySolve(forward, right, 0.4, 0.2, forward,
                                      out double toAz, out double toEl));

        Assert.Equal(0.4, toAz, 6);
        Assert.Equal(0.2, toEl, 6);
    }

    /// <summary>
    /// Straight up has no azimuth — every azimuth points there. Inventing one would spin the
    /// camera on its way to a target directly overhead.
    /// </summary>
    [Fact]
    public void StraightUpLeavesTheAzimuthAlone()
    {
        (double3 forward, double3 right) = Build(FrameX, FrameUp, 1.1, 0.0);

        Assert.True(OrbitAim.TrySolve(forward, right, 1.1, 0.0, FrameUp,
                                      out double toAz, out double toEl));

        Assert.Equal(1.1, toAz, 9);
        Assert.Equal(Math.PI / 2, toEl, 6);
    }

    /// <summary>
    /// A target expressed a whole turn away is the same target. Easing towards it must cover the
    /// 0.4 rad between them, not the 6.7 rad the raw difference suggests.
    /// </summary>
    [Fact]
    public void ItTakesTheShortWayRound()
    {
        double az = 3.0, el = 0.0;

        OrbitAim.Ease(ref az, ref el, 3.4 + 2 * Math.PI, 0.0, rate: 1000.0, dt: 1.0);

        Assert.Equal(3.4, az, 3);
    }

    /// <summary>Half open, [-pi, pi): exactly half a turn comes back as -pi.</summary>
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(Math.PI, -Math.PI)]
    [InlineData(-Math.PI + 0.1, -Math.PI + 0.1)]
    [InlineData(3 * Math.PI, -Math.PI)]
    [InlineData(-3 * Math.PI, -Math.PI)]
    [InlineData(2 * Math.PI + 0.25, 0.25)]
    public void WrapKeepsAnglesInRange(double given, double expected)
        => Assert.Equal(expected, OrbitAim.WrapPi(given), 9);

    /// <summary>
    /// Easing has to be framerate independent, or the same nudge takes a different time on a
    /// different machine — and this one is watched, so it would be obvious.
    /// </summary>
    [Fact]
    public void EasingDoesNotDependOnFramerate()
    {
        double coarseAz = 0.0, coarseEl = 0.0;
        OrbitAim.Ease(ref coarseAz, ref coarseEl, 1.0, 0.0, rate: 3.0, dt: 1.0);

        double fineAz = 0.0, fineEl = 0.0;
        for (int i = 0; i < 100; i++) OrbitAim.Ease(ref fineAz, ref fineEl, 1.0, 0.0, 3.0, 0.01);

        Assert.Equal(coarseAz, fineAz, 6);
    }

    [Fact]
    public void EasingApproachesWithoutOvershooting()
    {
        double az = 0.0, el = 0.0;
        for (int i = 0; i < 200; i++) OrbitAim.Ease(ref az, ref el, 1.0, 0.5, 4.0, 1.0 / 60.0);

        Assert.True(az <= 1.0 + 1e-9, $"overshot to {az}");
        Assert.True(OrbitAim.Arrived(az, el, 1.0, 0.5, 1e-3));
    }

    [Fact]
    public void ArrivedIsFalseUntilItIs()
    {
        Assert.False(OrbitAim.Arrived(0.0, 0.0, 1.0, 0.0, 0.01));
        Assert.True(OrbitAim.Arrived(1.0, 0.5, 1.0, 0.5, 0.01));
    }

    [Fact]
    public void DegenerateInputIsRefusedRatherThanGuessed()
    {
        double3 zero = default;
        double3 ok = new(1, 0, 0);

        Assert.False(OrbitAim.TrySolve(zero, ok, 0, 0, ok, out _, out _));
        Assert.False(OrbitAim.TrySolve(ok, zero, 0, 0, ok, out _, out _));
        Assert.False(OrbitAim.TrySolve(ok, ok, 0, 0, zero, out _, out _));
        Assert.False(OrbitAim.TrySolve(ok, ok, double.NaN, 0, ok, out _, out _));
    }
}
