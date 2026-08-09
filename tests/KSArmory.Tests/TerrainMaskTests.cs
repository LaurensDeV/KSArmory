using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The skyline test. Two things are worth pinning: that it finds a ridge the mean sphere lets
/// through, which is the whole reason it exists, and that it costs nothing when nothing can
/// possibly be in the way — because it runs once per contact per scan and the cost is the reason
/// it ships switched off.
/// </summary>
public class TerrainMaskTests
{
    private const double Radius = 6371000.0;

    private static readonly double3 Centre = new(0, 0, 0);

    /// <summary>Flat ground everywhere except a wall of a given height over a band of longitude.</summary>
    private sealed class Ridge(double fromDeg, double toDeg, double height) : ITerrainHeights
    {
        public int Asked { get; private set; }

        public bool TryHeight(double3 dirFromCentre, out double metres)
        {
            Asked++;

            double deg = double.RadiansToDegrees(Math.Atan2(dirFromCentre.Y, dirFromCentre.X));
            metres = deg >= fromDeg && deg <= toDeg ? height : 0.0;

            return true;
        }
    }

    private sealed class Unreadable : ITerrainHeights
    {
        public int Asked { get; private set; }

        public bool TryHeight(double3 dirFromCentre, out double metres)
        {
            Asked++;
            metres = 0.0;

            return false;
        }
    }

    // A point on the sphere at this longitude and altitude, in the equatorial plane.
    private static double3 At(double longitudeDeg, double altitude)
    {
        double a = double.DegreesToRadians(longitudeDeg);

        return new double3(Math.Cos(a), Math.Sin(a), 0) * (Radius + altitude);
    }

    /// <summary>
    /// The case the mean sphere cannot see: both ends well above the surface, the straight line
    /// between them clearing the sphere, and a mountain standing through it.
    /// </summary>
    [Fact]
    public void ARidgeBetweenThemBlocks_WhereTheMeanSphereDoesNot()
    {
        double3 eye = At(0.0, 50.0);
        double3 target = At(0.4, 400.0);

        Assert.False(LineOfSight.Blocked(eye, target, Centre, Radius),
            "the mean sphere already blocks this, so the test is not exercising terrain");

        var ridge = new Ridge(0.15, 0.25, 3000.0);

        Assert.True(TerrainMask.Blocked(eye, target, Centre, Radius, 9000.0, 32, 30.0, ridge));
    }

    [Fact]
    public void FlatGroundBetweenThemDoesNotBlock()
    {
        double3 eye = At(0.0, 50.0);
        double3 target = At(0.4, 400.0);

        var flat = new Ridge(0.0, 0.0, 0.0);

        Assert.False(TerrainMask.Blocked(eye, target, Centre, Radius, 9000.0, 32, 30.0, flat));
    }

    /// <summary>
    /// A ridge lower than the line clears it. Without this the test above would pass on any
    /// terrain at all and would be measuring nothing.
    /// </summary>
    [Fact]
    public void ARidgeUnderTheLineDoesNotBlock()
    {
        double3 eye = At(0.0, 4000.0);
        double3 target = At(0.4, 4000.0);

        var ridge = new Ridge(0.15, 0.25, 1500.0);

        Assert.False(TerrainMask.Blocked(eye, target, Centre, Radius, 9000.0, 32, 30.0, ridge));
    }

    /// <summary>
    /// The saving that makes this affordable: a contact whose line never comes within the body's
    /// highest terrain costs no lookups at all.
    /// </summary>
    [Fact]
    public void ALineThatStaysAboveTheHighestTerrainAsksNothing()
    {
        double3 eye = At(0.0, 40000.0);
        double3 target = At(0.4, 40000.0);

        var ridge = new Ridge(0.15, 0.25, 3000.0);

        Assert.False(TerrainMask.Blocked(eye, target, Centre, Radius, 9000.0, 32, 30.0, ridge));
        Assert.Equal(0, ridge.Asked);
    }

    [Fact]
    public void TheSampleCountIsTheCeilingOnWhatOneLookCosts()
    {
        double3 eye = At(0.0, 50.0);
        double3 target = At(0.4, 400.0);

        var flat = new Ridge(0.0, 0.0, 0.0);

        TerrainMask.Blocked(eye, target, Centre, Radius, 9000.0, 12, 30.0, flat);

        Assert.Equal(12, flat.Asked);
    }

    [Fact]
    public void NoSamplesMeansNoLookupsAndNoClaim()
    {
        double3 eye = At(0.0, 50.0);
        double3 target = At(0.4, 400.0);

        var ridge = new Ridge(0.15, 0.25, 3000.0);

        Assert.False(TerrainMask.Blocked(eye, target, Centre, Radius, 9000.0, 0, 30.0, ridge));
        Assert.Equal(0, ridge.Asked);
    }

    /// <summary>
    /// An unreadable height field must not read as flat ground: that would put every sensor's
    /// horizon at the mean sphere, which is a planet-sized change with nothing to announce it.
    /// Silence is the only safe answer, and the mean-sphere test in front still holds.
    /// </summary>
    [Fact]
    public void AnUnreadableFieldMakesNoClaim()
    {
        double3 eye = At(0.0, 50.0);
        double3 target = At(0.4, 400.0);

        var unreadable = new Unreadable();

        Assert.False(TerrainMask.Blocked(eye, target, Centre, Radius, 9000.0, 8, 30.0, unreadable));
        Assert.True(unreadable.Asked > 0, "it should still have tried");
    }

    /// <summary>
    /// Both ends routinely stand on the ground. Sampling either endpoint finds the terrain the
    /// endpoint is standing on, so everything on a planet would be hidden from everything else.
    /// </summary>
    [Fact]
    public void TwoSitesOnFlatGroundCanSeeEachOther()
    {
        double3 eye = At(0.0, 8.0);
        double3 target = At(0.02, 8.0);

        var flat = new Ridge(0.0, 0.0, 0.0);

        Assert.False(TerrainMask.Blocked(eye, target, Centre, Radius, 9000.0, 32, 30.0, flat));
    }

    /// <summary>
    /// A coarse height map read beside a launcher standing on a slope finds the slope. The
    /// clearance is what stops that blinding a site along its own ground.
    /// </summary>
    [Fact]
    public void TheClearanceDecidesWhetherGrazingTerrainCounts()
    {
        double3 eye = At(0.0, 60.0);
        double3 target = At(0.4, 60.0);

        // A rise that stands just above the line, by less than a hundred metres.
        var bump = new Ridge(0.15, 0.25, 110.0);

        Assert.True(TerrainMask.Blocked(eye, target, Centre, Radius, 9000.0, 32, 0.0, bump));
        Assert.False(TerrainMask.Blocked(eye, target, Centre, Radius, 9000.0, 32, 300.0, bump));
    }

    [Fact]
    public void TheBandIsTheWholeSegmentWhenItRunsUnderTheCeilingThroughout()
    {
        double3 eye = At(0.0, 100.0);
        double3 target = At(0.4, 100.0);

        Assert.True(TerrainMask.TryBandBelow(eye, target, Centre, Radius + 9000.0,
                                             out double from, out double to));

        Assert.Equal(0.0, from, 9);
        Assert.Equal(1.0, to, 9);
    }

    [Fact]
    public void ThereIsNoBandForALineThatNeverComesClose()
    {
        double3 eye = At(0.0, 40000.0);
        double3 target = At(0.4, 40000.0);

        Assert.False(TerrainMask.TryBandBelow(eye, target, Centre, Radius + 9000.0, out _, out _));
    }

    /// <summary>
    /// One end low and the other high: only the low part of the line can be blocked, and the band
    /// is what stops the samples being spread over the part that cannot be.
    /// </summary>
    [Fact]
    public void TheBandCoversOnlyTheLowEndOfAClimbingLine()
    {
        double3 eye = At(0.0, 100.0);
        double3 target = At(2.0, 200000.0);

        Assert.True(TerrainMask.TryBandBelow(eye, target, Centre, Radius + 9000.0,
                                             out double from, out double to));

        Assert.Equal(0.0, from, 9);
        Assert.True(to < 0.2, $"the band ran to {to:F3} of a line that climbs to 200 km");
    }

    [Fact]
    public void ItRefusesInputItCannotUse()
    {
        double3 eye = At(0.0, 50.0);
        var flat = new Ridge(0.0, 0.0, 0.0);

        Assert.False(TerrainMask.Blocked(eye, eye, Centre, Radius, 9000.0, 8, 30.0, flat));
        Assert.False(TerrainMask.Blocked(eye, At(0.4, 400.0), Centre, 0.0, 9000.0, 8, 30.0, flat));
        Assert.False(TerrainMask.Blocked(eye, At(0.4, 400.0), Centre, Radius, 9000.0, 8, 30.0, null!));
    }
}
