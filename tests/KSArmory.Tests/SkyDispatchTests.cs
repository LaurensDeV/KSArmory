using Xunit;

namespace KSArmory.Tests;

/// <summary>Which sky dispatches a frame draws when more are asked for than it will pay for.</summary>
public class SkyDispatchTests
{
    private static List<int> Choose(params (float, double)[] wanted)
    {
        List<int> keep = [];
        SkyDispatch.Choose(wanted, 4, keep);
        return keep;
    }

    /// <summary>
    /// Six bursts at 100 km: four glows, four curtains and six shells asked for. The shells are brightest
    /// in their own units; kept by brightness alone, nothing else would be drawn.
    /// </summary>
    [Fact]
    public void ABusStillShowsItsGlowAndAurora()
    {
        List<(float, double)> wanted = [];
        for (int i = 0; i < 4; i++) wanted.Add((SkyDispatch.Glow, 0.4 + (0.01 * i)));
        for (int i = 0; i < 4; i++) wanted.Add((SkyDispatch.Aurora, 0.2 + (0.01 * i)));
        for (int i = 0; i < 6; i++) wanted.Add((SkyDispatch.Debris, 50.0 + i));

        List<int> keep = [];
        SkyDispatch.Choose(wanted, 4, keep);

        Assert.Equal(4, keep.Count);
        Assert.Contains(keep, i => wanted[i].Item1 == SkyDispatch.Glow);
        Assert.Contains(keep, i => wanted[i].Item1 == SkyDispatch.Aurora);
        Assert.Contains(keep, i => wanted[i].Item1 == SkyDispatch.Debris);

        // Each kind's brightest is the one kept.
        Assert.Contains(3, keep);
        Assert.Contains(7, keep);
        Assert.Contains(13, keep);
    }

    [Fact]
    public void WithinTheBudgetEverythingIsDrawn()
    {
        Assert.Equal([0, 1, 2], Choose((SkyDispatch.Glow, 1), (SkyDispatch.Debris, 9), (SkyDispatch.Aurora, 0.1)));
    }

    [Fact]
    public void OneKindAloneFillsTheBudgetBrightestFirst()
    {
        Assert.Equal([4, 3, 2, 1], Choose((SkyDispatch.Debris, 1), (SkyDispatch.Debris, 2), (SkyDispatch.Debris, 3),
                                          (SkyDispatch.Debris, 4), (SkyDispatch.Debris, 5)));
    }
}
