using Xunit;

namespace KSArmory.Tests;

/// <summary>The bang and the shake share one gate, on the pressure at the ear.</summary>
public class BurstHearingTests
{
    [Fact]
    public void TheGateIsFiftyPascalsAndLoudnessRisesToOne()
    {
        Assert.False(BurstHearing.Heard(49.9));
        Assert.True(BurstHearing.Heard(50.0));
        Assert.Equal(0.0, BurstHearing.Loudness(40.0));
        Assert.True(BurstHearing.Loudness(60.0) is > 0.0 and < 1.0);
        Assert.Equal(1.0, BurstHearing.Loudness(3_000.0));
    }

    [Fact]
    public void TheFrontReachesTheEarOnceAsItPasses()
    {
        Assert.False(BurstHearing.Reached(100, 900, 1000));
        Assert.True(BurstHearing.Reached(900, 1000, 1000));
        Assert.False(BurstHearing.Reached(1000, 1100, 1000));
    }
}
