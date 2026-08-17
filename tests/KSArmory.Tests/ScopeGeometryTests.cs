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

    /// <summary>
    /// One trace per radiating face, spread evenly, and off the array's own angle.
    ///
    /// <para>The Pantsir's array is a double-sided wedge: both faces look at once, so its picture
    /// refreshes twice a revolution and the scope has to say so. A count rather than a flag, so a
    /// three-face set needs no new concept.</para>
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryRadiatingFaceGetsATraceEvenlySpread(int faces)
    {
        Span<double> into = stackalloc double[ScopeGeometry.MaxSweepFaces];
        int count = ScopeGeometry.SweepBearings(0.0, 0.0, faces, into);

        Assert.Equal(faces, count);

        double step = Math.Tau / faces;
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i * step, into[i], 9);
            Assert.InRange(into[i], 0.0, Math.Tau);
        }
    }

    /// <summary>The array's angle turns the traces, and the craft's heading carries them round.</summary>
    [Fact]
    public void TheSweepFollowsTheArrayAndTheCraft()
    {
        Span<double> into = stackalloc double[ScopeGeometry.MaxSweepFaces];

        ScopeGeometry.SweepBearings(0.0, 90.0 * Deg, 2, into);
        Assert.Equal(90.0, into[0] / Deg, 6);
        Assert.Equal(270.0, into[1] / Deg, 6);

        // Turn the craft 90 degrees and the whole picture comes with it.
        ScopeGeometry.SweepBearings(90.0 * Deg, 90.0 * Deg, 2, into);
        Assert.Equal(180.0, into[0] / Deg, 6);
        Assert.Equal(0.0, into[1] / Deg, 6);
    }

    /// <summary>It wraps rather than running off, however far the array has turned.</summary>
    [Fact]
    public void TheSweepWrapsRatherThanRunningOff()
    {
        Span<double> into = stackalloc double[ScopeGeometry.MaxSweepFaces];

        ScopeGeometry.SweepBearings(0.0, 401.0 * Math.Tau + (Math.PI / 2.0), 1, into);
        Assert.InRange(into[0], 0.0, Math.Tau);
        Assert.Equal(90.0, into[0] / Deg, 5);

        // And a negative angle is still a bearing, not a negative number.
        ScopeGeometry.SweepBearings(0.0, -Math.PI / 2.0, 1, into);
        Assert.Equal(270.0, into[0] / Deg, 6);
    }

    /// <summary>A set with no rotating array draws no sweep rather than one pinned at north.</summary>
    [Fact]
    public void ASetWithNoArrayDrawsNothing()
    {
        Span<double> into = stackalloc double[ScopeGeometry.MaxSweepFaces];

        Assert.Equal(0, ScopeGeometry.SweepBearings(0.0, 0.0, 0, into));
        Assert.Equal(0, ScopeGeometry.SweepBearings(0.0, 0.0, -1, into));
        Assert.Equal(0, ScopeGeometry.SweepBearings(double.NaN, 0.0, 2, into));
        Assert.Equal(0, ScopeGeometry.SweepBearings(0.0, double.NaN, 2, into));
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
