using Xunit;

namespace KSArmory.Tests;

/// <summary>Each regime reproduces the threshold it replaces on Earth, and moves by what differs elsewhere.</summary>
public class BurstRegimeTests
{
    private static readonly BodyAir Earth = new(101_325, 1.225, 8_000, 167_000, 9.81, 6.371e6, BodyTraits.Default);

    private static readonly double[] Airs = [1.0, 0.13, 0.015, 0.0101, 0.01, 0.0099, 1e-3, 1e-4, 1e-5, 1e-6, 0.0];

    [Fact]
    public void OnEarthTheMushroomStandsWhereItAlwaysDid()
    {
        foreach (double air in Airs)
        {
            Assert.Equal(!MushroomCloud.IsThin(air), BurstRegime.StandsAsMushroom(new AmbientAir(air, air), 8_000));
        }
    }

    [Fact]
    public void AShortScaleHeightStopsTheMushroomInThickerAir()
    {
        AmbientAir air = new(0.02, 0.02);
        Assert.True(BurstRegime.StandsAsMushroom(air, 8_000));
        Assert.False(BurstRegime.StandsAsMushroom(air, 4_000));
        Assert.True(BurstRegime.StandsAsMushroom(new AmbientAir(0.005, 0.005), 16_000));
    }

    [Fact]
    public void OnEarthXRaysEscapeWhereTheGlowAlwaysLit()
    {
        foreach (double altitude in new[] { 0.0, 50_000.0, 85_000.0, 92_000.0, 93_000.0, 100_000.0, 170_000.0 })
        {
            double air = Earth.AirAt(altitude).DensityRatio;
            Assert.Equal(XRayGlow.Lights(air), BurstRegime.XRaysEscape(Earth, altitude));
        }
    }

    [Fact]
    public void AnOpaqueAirHoldsTheXRaysHigher()
    {
        BodyAir opaque = Earth with { Traits = BodyTraits.Default with { XRayOpacity = 10.0 } };
        Assert.True(BurstRegime.XRaysEscape(Earth, 95_000));
        Assert.False(BurstRegime.XRaysEscape(opaque, 95_000));
        Assert.True(BurstRegime.Blasts(1e9, Earth.AirAt(90_000)));
        Assert.False(BurstRegime.Blasts(1e9, Earth.AirAt(200_000)));
    }
}
