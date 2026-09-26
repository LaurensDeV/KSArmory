using Xunit;

namespace KSArmory.Tests;

/// <summary>Which sky dispatches a frame draws when more are asked for than it will pay for.</summary>
public class SkyDispatchTests
{
    private static List<int> Choose(params float[] kinds)
    {
        List<int> keep = [];
        SkyDispatch.Choose(kinds, keep);
        return keep;
    }

    /// <summary>
    /// Three bursts at 400 km: three glows, six curtains and three shells. Every glow and shell is drawn,
    /// and the two newest bursts' curtains -- where the brightest alone drew one of each kind and left
    /// the other bursts with nothing.
    /// </summary>
    [Fact]
    public void SeveralBurstsEachShowTheirSky()
    {
        List<float> kinds = [];
        for (int burst = 0; burst < 3; burst++)
        {
            kinds.Add(SkyDispatch.Glow);
            kinds.Add(SkyDispatch.Aurora);
            kinds.Add(SkyDispatch.Aurora);
            kinds.Add(SkyDispatch.Debris);
        }

        List<int> keep = Choose([.. kinds]);

        Assert.Equal(3, keep.Count(i => kinds[i] == SkyDispatch.Glow));
        Assert.Equal(3, keep.Count(i => kinds[i] == SkyDispatch.Debris));
        Assert.Equal([5, 6, 9, 10], keep.Where(i => kinds[i] == SkyDispatch.Aurora));
    }

    /// <summary>
    /// The newest are kept whatever their brightness, so the choice does not move between frames as two
    /// bursts' curtains cross -- ranked by brightness, the one drawn jumped between them.
    /// </summary>
    [Fact]
    public void TheNewestAreKeptSoTheChoiceHoldsStill()
    {
        Assert.Equal([2, 3, 4, 5], Choose(SkyDispatch.Aurora, SkyDispatch.Aurora, SkyDispatch.Aurora, SkyDispatch.Aurora,
                                          SkyDispatch.Aurora, SkyDispatch.Aurora));
        Assert.Equal([1, 2, 3], Choose(SkyDispatch.Debris, SkyDispatch.Debris, SkyDispatch.Debris, SkyDispatch.Debris));
    }

    [Fact]
    public void WithinTheBudgetEverythingIsDrawn()
    {
        Assert.Equal([0, 1, 2, 3], Choose(SkyDispatch.Glow, SkyDispatch.Debris, SkyDispatch.Aurora, SkyDispatch.RedWave));
    }
}
