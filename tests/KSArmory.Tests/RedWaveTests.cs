using Xunit;

namespace KSArmory.Tests;

/// <summary>Teak's red wave: its size and fade, where the red survives, and which bursts send one.</summary>
public class RedWaveTests
{
    private static readonly BodyAir Earth = new(101_325, 1.225, 8_000, 167_000, 9.81, 6.371e6, BodyTraits.Default);

    /// <summary>Teak's was a sphere 965 km across six minutes after the burst.</summary>
    [Fact]
    public void TeaksIsNineHundredAndSixtyFiveKilometresAcrossAtSixMinutes()
    {
        Assert.InRange(2.0 * RedWave.Radius(360.0), 930_000, 1_000_000);
        Assert.True(RedWave.Nits(3_800, 360.0) > 0.0);
        Assert.Equal(0.0, RedWave.Nits(3_800, RedWave.LifeSeconds));
        Assert.True(RedWave.Nits(3_800, 300.0) < RedWave.Nits(3_800, 60.0));
    }

    [Fact]
    public void TheRedSurvivesAboutAHundredAndFiftyKilometresUpOnEarth()
    {
        Assert.InRange(RedWave.RedAltitude(Earth), 145_000, 160_000);
        Assert.Equal(0.0, RedWave.RedAltitude(BodyAir.Airless(1.62, 1.737e6, BodyTraits.Default)));
    }

    [Fact]
    public void OnlyABurstHighInTheAirSendsOne()
    {
        Assert.True(RedWave.Lights(Earth.AirAt(77_000).DensityRatio));
        Assert.False(RedWave.Lights(Earth.AirAt(40_000).DensityRatio));
        Assert.False(RedWave.Lights(0.0));
    }

    [Fact]
    public void ItIsDrawnWithTheOtherSkyKindsUnderTheCap()
    {
        List<int> keep = [];
        SkyDispatch.Choose([SkyDispatch.Debris, SkyDispatch.Debris, SkyDispatch.Glow, SkyDispatch.Aurora,
                            SkyDispatch.RedWave], keep);
        Assert.Contains(4, keep);
    }
}
