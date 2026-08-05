using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A body between the eye and what it is looking at.
///
/// <para>The case that matters is a marker on a system on the far side of the planet: without
/// this it reads as a bearing the operator could act on.</para>
/// </summary>
public class LineOfSightTests
{
    private const double R = 6_000_000.0;
    private static readonly double3 Centre = new(0, 0, 0);

    [Fact]
    public void TheFarSideOfTheWorldIsBlocked()
    {
        double3 eye = new(R + 100_000, 0, 0);
        double3 target = new(-R, 0, 0);

        Assert.True(LineOfSight.Blocked(eye, target, Centre, R));
    }

    [Fact]
    public void SomethingOverheadIsNot()
    {
        double3 target = new(R, 0, 0);
        double3 eye = new(R + 100_000, 0, 0);

        Assert.False(LineOfSight.Blocked(eye, target, Centre, R));
    }

    /// <summary>
    /// A craft standing on the surface touches the body's own sphere. Counting that would report
    /// everything on the ground as hidden, which is every ground installation this mod has.
    /// </summary>
    [Fact]
    public void AnEndpointOnTheSurfaceDoesNotBlockItself()
    {
        double3 target = new(0, R, 0);

        // Straight overhead, off to one side, and low over the horizon: all visible.
        Assert.False(LineOfSight.Blocked(new double3(0, R + 5000, 0), target, Centre, R));
        Assert.False(LineOfSight.Blocked(new double3(1000, R + 5000, 0), target, Centre, R));
        Assert.False(LineOfSight.Blocked(new double3(50_000, R + 1000, 0), target, Centre, R));
    }

    /// <summary>The body behind the viewer blocks nothing, however big it is.</summary>
    [Fact]
    public void ABodyBehindTheEyeIsNotInTheWay()
    {
        double3 eye = new(R + 1000, 0, 0);
        double3 target = new(R + 500_000, 0, 0);

        Assert.False(LineOfSight.Blocked(eye, target, Centre, R));
    }

    [Fact]
    public void TwoCraftClearOfThePlanetSeeEachOther()
    {
        double3 eye = new(R * 3, 0, 0);
        double3 target = new(R * 3, R, 0);

        Assert.False(LineOfSight.Blocked(eye, target, Centre, R));
    }

    /// <summary>Just past the limb is the case a naive "is it in front" test gets wrong.</summary>
    [Fact]
    public void JustOverTheHorizonIsBlocked()
    {
        // Eye 200 km up; target on the surface a long way round, well past the geometric horizon.
        double3 eye = new(0, R + 200_000, 0);
        double angle = 0.6; // rad round the body, far beyond the ~0.25 rad horizon from that height
        double3 target = new(R * Math.Sin(angle), R * Math.Cos(angle), 0);

        Assert.True(LineOfSight.Blocked(eye, target, Centre, R));
    }

    [Fact]
    public void DegenerateInputIsNotBlocked()
    {
        double3 a = new(1, 0, 0);

        Assert.False(LineOfSight.Blocked(a, a, Centre, R));
        Assert.False(LineOfSight.Blocked(a, new double3(2, 0, 0), Centre, 0.0));
        Assert.False(LineOfSight.Blocked(a, new double3(2, 0, 0), Centre, double.NaN));
        Assert.False(LineOfSight.Blocked(new double3(double.NaN, 0, 0), a, Centre, R));
    }
}
