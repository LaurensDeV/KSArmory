using Brutal.Numerics;
using KSArmory;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The planet getting in the way. Earth-sized numbers throughout, because the interesting case is
/// a battery on the deck against something low and far off.
/// </summary>
public class HorizonTests
{
    private const double Earth = 6_371_000.0;

    [Fact]
    public void SomethingOnTheFarSideIsHidden()
    {
        var centre = Vec.Zero;
        var eye = new double3(Earth + 10.0, 0, 0);
        var far = new double3(-(Earth + 10.0), 0, 0);

        Assert.True(LineOfSight.Blocked(eye, far, centre, Earth));
    }

    [Fact]
    public void SomethingOverheadIsNot()
    {
        var centre = Vec.Zero;
        var eye = new double3(Earth + 10.0, 0, 0);
        var above = new double3(Earth + 10_000.0, 0, 0);

        Assert.False(LineOfSight.Blocked(eye, above, centre, Earth));
    }

    [Fact]
    public void ANeighbourOnThePadIsStillVisible()
    {
        // The case that would break everything if the geometry were too eager: two craft a
        // kilometre apart on the same pad. The chord dips 2 cm over that distance and the pad is
        // metres up, so the ground is not between them.
        var centre = Vec.Zero;
        var eye = new double3(Earth + 8.0, 0, 0);
        var near = Vec.Unit(new double3(Earth, 1_000.0, 0)) * (Earth + 8.0);

        Assert.False(LineOfSight.Blocked(eye, near, centre, Earth));
    }

    [Fact]
    public void TwoThingsAtExactlySeaLevelCannotSeeEachOther()
    {
        // Not a defect: the straight line between two points on a sphere runs through it. It is
        // the same fact HorizonRange states as zero, and it is why altitude is what buys range.
        var centre = Vec.Zero;
        var eye = new double3(Earth, 0, 0);
        var far = Vec.Unit(new double3(Earth, 100_000.0, 0)) * Earth;

        Assert.True(LineOfSight.Blocked(eye, far, centre, Earth));
    }

    [Theory]
    // A mast at 10 m sees about 11.3 km; the classic sailor's horizon.
    [InlineData(10.0, 0.0, 11_000, 12_000)]
    // A target at 100 m adds its own 35.7 km on top.
    [InlineData(10.0, 100.0, 46_000, 48_000)]
    // An aircraft at 10 km is visible from a very long way off, which is the whole argument for
    // flying low.
    [InlineData(10.0, 10_000.0, 365_000, 370_000)]
    public void TheHorizonGrowsWithBothAltitudes(double eyeAlt, double targetAlt, double low, double high)
    {
        double range = LineOfSight.HorizonRange(Earth, eyeAlt, targetAlt);

        Assert.InRange(range, low, high);
    }

    [Fact]
    public void AGroundToGroundHorizonIsZero()
    {
        // Two things at sea level cannot see each other at any distance over a smooth sphere.
        Assert.Equal(0.0, LineOfSight.HorizonRange(Earth, 0.0, 0.0));
    }

    [Fact]
    public void TerrainMarginHidesWhatTheSmoothSphereWouldShow()
    {
        // A contact just clearing the geometric limb is exactly the case a ridge would block, and
        // the case the mean sphere is wrong about.
        var centre = Vec.Zero;
        var eye = new double3(Earth + 100.0, 0, 0);

        // Grazing: far enough round the curve that the line passes just outside the sphere.
        double angle = 0.008;
        var target = new double3(Math.Cos(angle), Math.Sin(angle), 0) * (Earth + 100.0);

        Assert.False(LineOfSight.BlockedByTerrain(eye, target, centre, Earth, 0.0));
        Assert.True(LineOfSight.BlockedByTerrain(eye, target, centre, Earth, 2_000.0));
    }

    [Fact]
    public void ANegativeMarginCannotOpenUpTheHorizon()
    {
        // Otherwise a mistyped setting lets a sensor see straight through the planet, which is
        // what the masking exists to stop.
        var centre = Vec.Zero;
        var eye = new double3(Earth + 10.0, 0, 0);
        var far = new double3(-(Earth + 10.0), 0, 0);

        Assert.True(LineOfSight.BlockedByTerrain(eye, far, centre, Earth, -1_000_000.0));
    }
}
