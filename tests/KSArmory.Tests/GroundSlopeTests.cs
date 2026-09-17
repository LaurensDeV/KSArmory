using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Judging a target's ground before a night is spent flying at it.
/// </summary>
public class GroundSlopeTests(ITestOutputHelper Out)
{
    private const double R = 6_371_000.0;

    private static MapFrame Frame()
    {
        MapFrame? f = MapFrame.TryAt(Vec.Zero, new double3(R, 0, 0), new double3(0, 0, 1));
        Assert.NotNull(f);
        return f.Value;
    }

    /// <summary>A plane tilted by a stated amount along east, read back as that amount.</summary>
    private static Func<double3, double> Tilted(double slope)
        => dir =>
        {
            MapFrame f = Frame();
            // How far east this direction is of the anchor, to first order.
            double east = Vec.Dot(dir * R, f.East);
            return R + slope * east;
        };

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.05)]
    [InlineData(0.30)]
    public void ASlopeIsReadBackAsItself(double slope)
    {
        Assert.True(GroundSlope.TryAround(Frame(), Tilted(slope), metres: 10.0, spokes: 16,
                                          arrivalRadians: Math.PI / 4.0, out GroundSlope.Reading r));

        // Spokes run all round, so the median of |slope · cos(angle)| is well under the peak.
        Out.WriteLine($"tilt {slope:F2}: median {r.Median:F4}, worst {r.Worst:F4}, gain {r.Gain:F3}");

        Assert.True(r.Worst <= slope + 1e-6, "no spoke may read steeper than the plane is");
        Assert.Equal(slope, r.Median, 6);
    }

    /// <summary>At 45 degrees the gain IS the slope, which is what makes it a useful anchor.</summary>
    [Fact]
    public void AtFortyFiveDegreesTheGainIsTheSlope()
    {
        Assert.True(GroundSlope.TryAround(Frame(), Tilted(0.30), 10.0, 8, Math.PI / 4.0,
                                          out GroundSlope.Reading r));
        Assert.Equal(r.Median, r.Gain, 9);
    }

    /// <summary>
    /// The one that matters: past gain one there is no fixed point, and that is reported as a
    /// verdict rather than as a large number.
    /// </summary>
    [Fact]
    public void PastGainOneThereIsNoFixedPoint()
    {
        // A 0.30 slope arriving at 10 degrees: 0.30 / tan(10) = 1.70.
        Assert.True(GroundSlope.TryAround(Frame(), Tilted(0.30), 10.0, 8,
                                          10.0 * Math.PI / 180.0, out GroundSlope.Reading steep));
        Assert.True(GroundSlope.TryAround(Frame(), Tilted(0.30), 10.0, 8,
                                          60.0 * Math.PI / 180.0, out GroundSlope.Reading shallow));

        Out.WriteLine($"0.30 slope at 10 deg: {steep.Describe()}");
        Out.WriteLine($"0.30 slope at 60 deg: {shallow.Describe()}");

        Assert.False(steep.HasAFixedPoint);
        Assert.True(double.IsPositiveInfinity(steep.Amplification));

        Assert.True(shallow.HasAFixedPoint);
        Assert.True(shallow.Amplification is > 1.0 and < 1.3);

        // The same ground: only the angle the round comes in at separates them.
        Assert.Equal(steep.Median, shallow.Median, 9);
    }

    /// <summary>Ground the field will not answer for makes no claim, rather than reading as flat.</summary>
    [Fact]
    public void AnUnreadableFieldMakesNoClaim()
    {
        Assert.False(GroundSlope.TryAround(Frame(), _ => double.NaN, 10.0, 8, Math.PI / 4.0, out _));
        Assert.False(GroundSlope.TryAround(Frame(), Tilted(0.1), 0.0, 8, Math.PI / 4.0, out _));
        Assert.False(GroundSlope.TryAround(Frame(), Tilted(0.1), 10.0, 8, 0.0, out _));
    }
}
