using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Launch geometry, and the orientation of a round's body.
///
/// Both of these were wrong in-game in ways that were only visible by watching: rounds left the
/// tube at a slightly different angle to the tube, and a body oriented off the wrong velocity
/// points every round the same way. Neither shows up in a hit-or-miss test, because guidance
/// recovers from both.
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
        // The guard on the whole change: these two are *not* the same shot. If they ever
        // converge, this test is no longer proving anything.
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
}
