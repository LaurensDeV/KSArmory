using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The scope's face. Every one of these is a convention that looks right in a screenshot when it is
/// wrong, which is why they are pinned rather than eyeballed: a mirrored bearing, a slant range or
/// an inverted Y all draw a plausible-looking scope showing the wrong thing.
/// </summary>
public class ScopeGeometryTests
{
    private const double Deg = Math.PI / 180.0;

    /// <summary>
    /// Compass convention: zero is north, and it runs clockwise through east.
    ///
    /// <para>The mathematical convention — zero at east, anticlockwise — is the one Atan2 gives if
    /// its arguments are passed in the order that reads naturally, and it mirrors the whole scope
    /// about its north–south line.</para>
    /// </summary>
    [Theory]
    [InlineData(0.0, 1.0, 0.0)]        // due north
    [InlineData(1.0, 0.0, 90.0)]       // due east
    [InlineData(0.0, -1.0, 180.0)]     // due south
    [InlineData(-1.0, 0.0, 270.0)]     // due west
    [InlineData(1.0, 1.0, 45.0)]       // north-east
    public void BearingRunsClockwiseFromNorth(double east, double north, double expectedDeg)
    {
        Assert.Equal(expectedDeg, ScopeGeometry.BearingRad(east, north) / Deg, 6);
    }

    /// <summary>And it is always a bearing, never a negative angle.</summary>
    [Fact]
    public void BearingIsAlwaysPositive()
    {
        for (int deg = 0; deg < 360; deg += 7)
        {
            double r = deg * Deg;
            double bearing = ScopeGeometry.BearingRad(Math.Sin(r), Math.Cos(r));

            Assert.InRange(bearing, 0.0, Math.Tau);
            Assert.Equal(deg, bearing / Deg, 6);
        }
    }

    /// <summary>North is up the screen, which is negative Y. East is to the right.</summary>
    [Fact]
    public void NorthIsUpTheScreenAndEastIsRight()
    {
        float2 north = ScopeGeometry.Plot(0.0, 500.0, 1000.0);
        float2 east = ScopeGeometry.Plot(90.0 * Deg, 500.0, 1000.0);

        Assert.Equal(0.0f, north.X, 5);
        Assert.Equal(-0.5f, north.Y, 5);
        Assert.Equal(0.5f, east.X, 5);
        Assert.Equal(0.0f, east.Y, 5);
    }

    /// <summary>A contact on top of the site is at the centre, whatever its bearing.</summary>
    [Fact]
    public void ZeroRangeIsTheCentre()
    {
        for (int deg = 0; deg < 360; deg += 30)
        {
            float2 at = ScopeGeometry.Plot(deg * Deg, 0.0, 1000.0);
            Assert.Equal(0.0f, Math.Abs(at.X) + Math.Abs(at.Y), 5);
        }
    }

    /// <summary>
    /// A PPI shows ground track, so an overflight belongs at the centre however high it is.
    ///
    /// <para>Slant range would place an aircraft passing directly over the site out at its own
    /// altitude — a contact that closes and then never arrives.</para>
    /// </summary>
    [Fact]
    public void RangeIsAlongTheGroundNotThroughTheAir()
    {
        Assert.Equal(0.0, ScopeGeometry.GroundRange(0.0, 0.0), 6);
        Assert.Equal(5000.0, ScopeGeometry.GroundRange(3000.0, 4000.0), 6);
    }

    /// <summary>
    /// A contact past the range setting is pinned to the rim on its own bearing, not dropped.
    ///
    /// <para>It has to still say which way, or something closing from outside the setting appears
    /// out of nowhere when it crosses. The caller draws it differently so it is not read as being
    /// at the rim.</para>
    /// </summary>
    [Fact]
    public void ADistantContactIsHeldAtTheRimOnItsBearing()
    {
        float2 at = ScopeGeometry.Plot(90.0 * Deg, 40_000.0, 10_000.0);

        Assert.True(ScopeGeometry.Beyond(40_000.0, 10_000.0));
        Assert.Equal(1.0f, at.X, 5);
        Assert.Equal(0.0f, at.Y, 5);
        Assert.False(ScopeGeometry.Beyond(9_999.0, 10_000.0));
    }

    /// <summary>The sweep goes round once per revolution and wraps cleanly.</summary>
    [Fact]
    public void TheSweepTurnsOnceARevolution()
    {
        Assert.Equal(0.0, ScopeGeometry.SweepBearingRad(0.0, 4.0), 6);
        Assert.Equal(Math.PI / 2.0, ScopeGeometry.SweepBearingRad(1.0, 4.0), 6);
        Assert.Equal(Math.PI, ScopeGeometry.SweepBearingRad(2.0, 4.0), 6);

        // Wraps rather than running off, and lands back where it started.
        Assert.Equal(0.0, ScopeGeometry.SweepBearingRad(4.0, 4.0), 6);
        Assert.Equal(Math.PI / 2.0, ScopeGeometry.SweepBearingRad(401.0, 4.0), 6);
    }

    /// <summary>Nothing here may hand a NaN to a draw call.</summary>
    [Fact]
    public void GarbageInDoesNotReachTheDrawList()
    {
        foreach (float2 at in new[]
        {
            ScopeGeometry.Plot(double.NaN, 100.0, 1000.0),
            ScopeGeometry.Plot(0.0, double.NaN, 1000.0),
            ScopeGeometry.Plot(0.0, 100.0, 0.0),
            ScopeGeometry.Plot(0.0, 100.0, double.NaN),
        })
        {
            Assert.True(float.IsFinite(at.X) && float.IsFinite(at.Y), $"({at.X}, {at.Y})");
        }

        Assert.Equal(0.0, ScopeGeometry.SweepBearingRad(double.NaN, 4.0), 6);
        Assert.Equal(0.0, ScopeGeometry.SweepBearingRad(1.0, 0.0), 6);
        Assert.True(ScopeGeometry.Beyond(10.0, 0.0), "no range setting means nothing is on the face");
    }

    /// <summary>The rings are inside the rim, in order, and the rim is not one of them.</summary>
    [Fact]
    public void TheRingsSitInsideTheRim()
    {
        Assert.NotEmpty(ScopeGeometry.Rings);

        float previous = 0f;
        foreach (float ring in ScopeGeometry.Rings)
        {
            Assert.InRange(ring, 0.01f, 0.99f);
            Assert.True(ring > previous, "the rings must run outward in order");
            previous = ring;
        }

        Assert.Equal(5000.0, ScopeGeometry.RingRange(10_000.0, 1), 6);
        Assert.Equal(0.0, ScopeGeometry.RingRange(10_000.0, 99), 6);
    }
}
