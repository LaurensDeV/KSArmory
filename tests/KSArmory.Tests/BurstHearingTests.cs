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

    [Fact]
    public void ThePickupIsAsRecordedAtAnniesRangeAndFallsAsOneOverTheRangePastIt()
    {
        Assert.Equal(1.0, BurstHearing.PickupLoudness(16.0, 10_700.0), 9);
        Assert.Equal(1.0, BurstHearing.PickupLoudness(16.0, 500.0), 9);
        Assert.Equal(0.5, BurstHearing.PickupLoudness(16.0, 21_400.0), 9);
        Assert.Equal(0.5, BurstHearing.PickupLoudness(2.0, 10_700.0), 9);
    }

    [Fact]
    public void ThePickupIsNotPlayedFainterThanAShareOfTheRecording()
    {
        Assert.Equal(0.0, BurstHearing.PickupLoudness(16.0, 10_700.0 / BurstHearing.FaintestPickup * 1.01));
        Assert.Equal(0.0, BurstHearing.PickupLoudness(0.0, 1_000.0));
        Assert.Equal(0.0, BurstHearing.PickupLoudness(16.0, double.NaN));
    }
}
