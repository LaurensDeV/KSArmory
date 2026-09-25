using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// A burst's heat and light by the air it went off in: G&amp;D's pulse law, the thermal fraction, the
/// glow fitted to Orange and Teak, the double pulse merging, and a vacuum's flash of its own.
/// </summary>
public class ThermalAltitudeTests(ITestOutputHelper output)
{
    private const double Kt = 1.0e6;

    [Fact]
    public void AtSeaLevelEveryScaleIsOne()
    {
        Assert.Equal(1.0, ThermalAltitude.PulseScale(1.0));
        Assert.Equal(1.0, ThermalAltitude.GlowScale(1.0));
        Assert.Equal(0.0, ThermalAltitude.SinglePulse(1.0));
        Assert.Equal(FlashGlare.ThermalFraction, ThermalAltitude.ThermalFraction(1.0));
    }

    [Fact]
    public void ThePulseFollowsGlasstonesHighLawAndIsHeldAtItsLimit()
    {
        double at100kft = 0.038 / 0.0417 * Math.Pow(0.0147, 0.36);
        Assert.Equal(at100kft, ThermalAltitude.PulseScale(0.0147), 12);
        Assert.Equal(at100kft, ThermalAltitude.PulseScale(1e-5), 12);

        double last = 1.0;
        for (double air = 1.0; air > 1e-3; air *= 0.8)
        {
            double scale = ThermalAltitude.PulseScale(air);
            Assert.True(scale <= last + 1e-12);
            last = scale;
        }
    }

    [Fact]
    public void TheStratosphereGivesMoreOfItselfAsLight()
    {
        Assert.Equal(0.6, ThermalAltitude.ThermalFraction(0.005), 12);
        Assert.Equal(0.25, ThermalAltitude.ThermalFraction(5e-5), 12);
        Assert.Equal(0.03, ThermalAltitude.ThermalFraction(1e-6), 12);

        double sea = FlashGlare.Suns(1000, 50_000, MushroomCloud.PeakGlow);
        double high = FlashGlare.Suns(1000, 50_000, MushroomCloud.PeakGlow, 0.005);
        output.WriteLine($"1 Mt from 50 km: {sea:F0} suns low, {high:F0} at 0.005 of the air");
        Assert.True(high > 2.0 * sea);
    }

    /// <summary>Orange (3.8 Mt, 43 km) glowed about 18 s; Teak (3.8 Mt, 77 km) about 2.5.</summary>
    [Fact]
    public void TheGlowFitsOrangeAndTeak()
    {
        double orange = MushroomCloud.FlashSeconds(3800, 2.1e-3);
        double teak = MushroomCloud.FlashSeconds(3800, 2.4e-5);
        output.WriteLine($"Orange glows {orange:F1} s, Teak {teak:F1} s");

        Assert.InRange(orange, 15.0, 21.0);
        Assert.InRange(teak, 2.0, 3.0);
    }

    [Fact]
    public void HighUpThePulseIsSingle()
    {
        Assert.Equal(1.0, ThermalAltitude.SinglePulse(0.003));

        // No dip between the first flash and the burn: the glow only ever falls after its peak.
        double charge = 1000 * Kt;
        double peak = MushroomCloud.FlashAt(charge, 0.0, 40_000, 0.003).Glow;
        double trough = double.MaxValue;
        double after = 0.0;
        for (double age = 0.0; age < 1.0; age += 0.005)
        {
            double glow = MushroomCloud.FlashAt(charge, age, 40_000, 0.003).Glow;
            trough = Math.Min(trough, glow);
            after = glow;
        }

        Assert.True(peak > 0.0);
        Assert.Equal(trough, after, 6);
    }

    /// <summary>
    /// A burst with no air round it flashes and is gone within a second, where it had been drawn as a
    /// thin-air fireball eight times its size glowing for seconds.
    /// </summary>
    [Fact]
    public void AVacuumBurstIsAFlash()
    {
        double charge = 20 * Kt;
        MushroomCloud.Flash first = MushroomCloud.FlashAt(charge, 0.05, 0.0, 0.0);

        Assert.False(first.Spent);
        Assert.True(first.Radius <= MushroomCloud.FireballRadius(20));
        Assert.True(MushroomCloud.FlashAt(charge, 1.0, 0.0, 0.0).Spent);
    }
}
