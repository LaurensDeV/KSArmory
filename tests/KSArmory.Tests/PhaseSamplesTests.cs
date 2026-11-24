using Xunit;

namespace KSArmory.Tests;

public class PhaseSamplesTests
{
    [Fact]
    public void ThePercentileIsTheNearestRankAndIgnoresUnfiniteSamples()
    {
        var samples = new PhaseSamples();
        foreach (double v in new[] { 5.0, 1.0, double.NaN, 4.0, 2.0, 3.0, double.PositiveInfinity }) samples.Add(v);

        Assert.Equal(5, samples.Count);
        Assert.Equal(3.0, samples.Percentile(0.5));
        Assert.Equal(5.0, samples.Percentile(0.95));
        Assert.Equal(1.0, samples.Percentile(0.0));
        Assert.Equal(5.0, samples.Max);
    }

    [Fact]
    public void ClearingForgetsTheMaximumToo()
    {
        var samples = new PhaseSamples();
        samples.Add(7.0);
        samples.Clear();
        samples.Add(2.0);

        Assert.Equal(2.0, samples.Max);
        Assert.True(double.IsNaN(new PhaseSamples().Percentile(0.5)));
    }
}
