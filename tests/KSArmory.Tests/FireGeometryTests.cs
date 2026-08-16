using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Launch geometry, and the orientation of a round's body.
///
/// Both fail in ways only visible by watching: a round leaving at a slightly different angle to
/// the tube it came out of, and a body oriented off the wrong velocity pointing every round the
/// same way. Neither shows up in a hit-or-miss test, because guidance recovers from both.
/// </summary>
public class FireGeometryTests
{
    private static readonly double3 Up = new(1, 0, 0);

    /// <summary>A tube laid 40 degrees up and 20 degrees off the nose.</summary>
    private static double3 TubeAxis()
    {
        double elevation = double.DegreesToRadians(40);
        double bearing = double.DegreesToRadians(20);
        double horizontal = Math.Cos(elevation);
        return new double3(Math.Sin(elevation),
                           horizontal * Math.Cos(bearing),
                           horizontal * Math.Sin(bearing));
    }

    [Fact]
    public void AlongTube_LeavesExactlyOnTheTubeAxis()
    {
        double3 axis = TubeAxis();
        double3 target = new(500, 4000, 0);

        double3 launch = FireGeometry.LaunchDirection(
            alongTube: true, axis, Vec.Zero, target, Up, loft: 0.35);

        Assert.Equal(0.0, Vec.AngleBetween(axis, launch), 9);
    }

    [Fact]
    public void AlongTube_IgnoresLoftEntirely()
    {
        // The tube's own elevation *is* the loft. Adding more cants the round off the rail,
        // which is exactly the "exits at a slightly different angle" symptom.
        double3 axis = TubeAxis();
        double3 target = new(500, 4000, 0);

        double3 none = FireGeometry.LaunchDirection(true, axis, Vec.Zero, target, Up, 0.0);
        double3 lots = FireGeometry.LaunchDirection(true, axis, Vec.Zero, target, Up, 1.5);

        Assert.Equal(0.0, Vec.AngleBetween(none, lots), 9);
    }

    [Fact]
    public void AlongTube_FallsBackWhenTheAxisIsUnusable()
    {
        double3 target = new(0, 4000, 0);

        double3 launch = FireGeometry.LaunchDirection(
            alongTube: true, Vec.Zero, Vec.Zero, target, Up, loft: 0.0);

        // No tube axis to use, so it aims at the target rather than returning a zero vector.
        Assert.Equal(0.0, Vec.AngleBetween(new double3(0, 1, 0), launch), 9);
    }

    [Fact]
    public void Fallback_LeadsTowardTheTargetAndLofts()
    {
        double3 target = new(0, 4000, 0);       // dead level, straight ahead

        double3 flat = FireGeometry.LaunchDirection(false, Vec.Zero, Vec.Zero, target, Up, 0.0);
        double3 lofted = FireGeometry.LaunchDirection(false, Vec.Zero, Vec.Zero, target, Up, 0.5);

        Assert.Equal(0.0, Vec.AngleBetween(new double3(0, 1, 0), flat), 9);
        Assert.True(lofted.X > 0.0, "loft must tilt the shot up");
        Assert.True(Vec.AngleBetween(flat, lofted) > 0.1, "loft must actually change the angle");
    }

    [Fact]
    public void Fallback_AndTubeAxisGenuinelyDisagree()
    {
        // These two are *not* the same shot. If they ever converge, every other assertion about
        // which one a launcher uses stops proving anything.
        double3 axis = TubeAxis();
        double3 target = new(500, 4000, 0);

        double3 tube = FireGeometry.LaunchDirection(true, axis, Vec.Zero, target, Up, 0.35);
        double3 slewed = FireGeometry.LaunchDirection(false, axis, Vec.Zero, target, Up, 0.35);

        Assert.True(Vec.AngleBetween(tube, slewed) > double.DegreesToRadians(5),
                    "the tube axis and the slewed shot should differ noticeably");
    }

    [Fact]
    public void RotationFromNose_PointsTheNoseAlongTheDirection()
    {
        foreach (double3 direction in new[]
                 {
                     new double3(0, 1, 0), new double3(0, 0, 1), new double3(-1, 0, 0),
                     new double3(0.3, -0.5, 0.81), TubeAxis(),
                 })
        {
            double3 nosed = FireGeometry.RotationFromNose(direction) * FireGeometry.NoseAxis;
            Assert.Equal(0.0, Vec.AngleBetween(Vec.Unit(direction), nosed), 6);
        }
    }

    [Fact]
    public void RotationFromNose_SurvivesTheDegenerateCases()
    {
        // Straight backwards makes the cross product vanish; normalising it would be NaN, and
        // a NaN transform takes the whole body out of the world rather than pointing it wrong.
        double3 reversed = FireGeometry.RotationFromNose(new double3(-1, 0, 0)) * FireGeometry.NoseAxis;
        Assert.True(Vec.IsFinite(reversed));
        Assert.Equal(0.0, Vec.AngleBetween(new double3(-1, 0, 0), reversed), 6);

        double3 stalled = FireGeometry.RotationFromNose(Vec.Zero) * FireGeometry.NoseAxis;
        Assert.True(Vec.IsFinite(stalled));

        double3 forward = FireGeometry.RotationFromNose(new double3(1, 0, 0)) * FireGeometry.NoseAxis;
        Assert.Equal(0.0, Vec.AngleBetween(new double3(1, 0, 0), forward), 6);
    }

    // ---- Spin at release -------------------------------------------------

    /// <summary>Earth's ecliptic motion, which both positions carry and which must cancel.</summary>
    private static readonly double3 SolarFrame = new(0, 29_800, 0);

    /// <summary>
    /// The whole frame contract, as an invariance: the answer is a difference of two points, so
    /// moving the frame they are both expressed in cannot change it.
    ///
    /// <para>Handing this a pre-computed lever arm from Ksa/ instead would put that subtraction at
    /// a call site no test reaches — see docs/FRAMES-AND-EPOCHS.md. At 29.8 km/s, leaking the
    /// frame's own motion into a cross product is not a small error.</para>
    /// </summary>
    [Fact]
    public void SpinVelocityIsUnchangedByTheFramesOwnMotion()
    {
        double3 omega = new(0, 0, 0.35);
        double3 tube = new(1.73, 0.96, 0);
        double3 com = new(0.1, 0, -0.4);

        double3 here = FireGeometry.SpinVelocity(omega, tube, com);
        double3 shifted = FireGeometry.SpinVelocity(omega, tube + SolarFrame, com + SolarFrame);

        Assert.True(Vec.Len(here - shifted) < 1e-9,
            $"the frame leaked in: {Fmt(here)} against {Fmt(shifted)}");
        Assert.True(Vec.Len(here) > 0.1, "the test geometry is degenerate");
    }

    /// <summary>
    /// A warhead on a spinning bus leaves on a tangent — which is what makes six of them fan
    /// apart instead of trailing the launcher in a clump.
    /// </summary>
    [Fact]
    public void ATubeOffTheAxisLeavesOnATangent()
    {
        double3 omega = new(1.0, 0, 0);                 // rolling about +X
        double3 com = double3.Zero;
        double3 tube = new(0, 2.0, 0);                  // 2 m out on +Y

        double3 v = FireGeometry.SpinVelocity(omega, tube, com);

        Assert.Equal(2.0, Vec.Len(v), 9);               // |omega| * r
        Assert.True(Vec.Len(v - new double3(0, 0, 2.0)) < 1e-9, $"expected +Z tangent, got {Fmt(v)}");
        Assert.True(Math.Abs(Vec.Dot(Vec.Unit(v), Vec.Unit(tube))) < 1e-9, "a tangent is not radial");
    }

    /// <summary>A tube on the axis is not going anywhere, however fast the craft rolls.</summary>
    [Fact]
    public void ATubeOnTheSpinAxisGainsNothing()
    {
        double3 v = FireGeometry.SpinVelocity(new double3(4.0, 0, 0),
                                              new double3(9.0, 0, 0), double3.Zero);
        Assert.True(Vec.Len(v) < 1e-12, $"on-axis tube should gain nothing, got {Fmt(v)}");
    }

    /// <summary>A craft that will not report its spin is not spinning, rather than NaN.</summary>
    [Fact]
    public void AnUnreadableSpinIsNotAThrow()
    {
        double3 bad = new(double.NaN, 0, 0);
        Assert.True(Vec.Len(FireGeometry.SpinVelocity(bad, new double3(1, 1, 1), double3.Zero)) < 1e-12);
        Assert.True(Vec.Len(FireGeometry.SpinVelocity(new double3(0, 0, 1), bad, double3.Zero)) < 1e-12);
    }

    private static string Fmt(double3 v) => $"({v.X:F4}, {v.Y:F4}, {v.Z:F4})";
}
