using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>A burst above the air: the field-held bubble, calibrated on Starfish, and what it does elsewhere.</summary>
public class DebrisBubbleTests(ITestOutputHelper output)
{
    private const double Radius = 6.371e6;
    private static readonly BodyAir Earth = new(101_325, 1.225, 8_000, 167_000, 9.81, Radius, BodyTraits.Default);
    private static readonly double3 Axis = new(0, 0, 1);

    private static double3 Over(double latDeg, double altitude)
    {
        double lat = double.DegreesToRadians(latDeg);
        return new double3(Math.Cos(lat), 0, Math.Sin(lat)) * (Radius + altitude);
    }

    /// <summary>Starfish, 1.4 Mt at 400 km over Johnston Island: 1,840 by 680 km, an equal volume of about 474 km.</summary>
    [Fact]
    public void StarfishIsTheCalibration()
    {
        double tesla = DebrisBubble.FieldTesla(3.1e-5, Radius, Over(16.7, 400_000), Axis);
        double r = DebrisBubble.EqualVolumeRadius(1.4e9, tesla);
        double along = r * Math.Pow(DebrisBubble.Stretch, 2.0 / 3.0);
        output.WriteLine($"Starfish: {tesla * 1e9:F0} nT, equal volume {r / 1000:F0} km, {2 * along / 1000:F0} by {2 * r / Math.Cbrt(DebrisBubble.Stretch) / 1000:F0} km");

        Assert.InRange(r, 440_000, 510_000);
        Assert.InRange(2 * along, 1_600_000, 2_100_000);
    }

    [Fact]
    public void ASmallYieldMakesASmallBubbleAndAWeakFieldALargeOne()
    {
        double tesla = 3.0e-5;
        Assert.Equal(Math.Cbrt(0.01), DebrisBubble.EqualVolumeRadius(1.4e7, tesla) / DebrisBubble.EqualVolumeRadius(1.4e9, tesla), 9);
        Assert.True(DebrisBubble.EqualVolumeRadius(1.4e9, tesla / 2) > DebrisBubble.EqualVolumeRadius(1.4e9, tesla));
    }

    [Fact]
    public void TheFieldTakesOverFromTheAirHighUp()
    {
        double tesla = DebrisBubble.FieldTesla(3.1e-5, Radius, Over(16.7, 150_000), Axis);
        Assert.True(DebrisBubble.FieldShare(tesla, Earth.PressureAt(100_000)) < 0.05);
        Assert.InRange(DebrisBubble.FieldShare(tesla, Earth.PressureAt(150_000)), 0.1, 0.9);
        Assert.Equal(1.0, DebrisBubble.FieldShare(tesla, Earth.PressureAt(200_000)));
    }

    [Fact]
    public void ItGrowsInASecondAndIsGoneBySixteen()
    {
        double full = DebrisBubble.EqualVolumeRadius(1.4e9, 3e-5);
        Assert.True(DebrisBubble.At(1.4e9, 1.2, 3e-5).Radius > 0.9 * full);
        Assert.False(DebrisBubble.At(1.4e9, 10.0, 3e-5).Spent);
        Assert.True(DebrisBubble.At(1.4e9, 16.0, 3e-5).Spent);
    }

    [Fact]
    public void WithNoFieldTheDebrisRunsOutAndFades()
    {
        DebrisBubble.Look early = DebrisBubble.At(1.4e9, 1.0, 0.0);
        DebrisBubble.Look later = DebrisBubble.At(1.4e9, 2.0, 0.0);

        Assert.False(early.Held);
        Assert.Equal(2.0 * early.Radius, later.Radius, 6);
        Assert.True(later.Radiance < early.Radiance);
        Assert.True(DebrisBubble.At(1.4e9, DebrisBubble.BallisticSeconds, 0.0).Spent);
        Assert.Equal(1.0, DebrisBubble.FieldShare(0.0, 0.0));
    }
}
