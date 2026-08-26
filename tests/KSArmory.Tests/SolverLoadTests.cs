using Xunit;

namespace KSArmory.Tests;

public sealed class SolverLoadTests
{
    private static SolverLoad Filled(double fraction, double tickMs, int frames)
    {
        SolverLoad load = new();
        double step = SolverLoad.ReportIntervalSeconds / (frames - 1);

        for (int i = 0; i < frames; i++) load.Sample(fraction, tickMs, step);

        return load;
    }

    [Fact]
    public void NothingIsDueBeforeTheIntervalHasPassed()
    {
        SolverLoad load = new();
        load.Sample(1.0, 4.0, SolverLoad.ReportIntervalSeconds / 2.0);

        Assert.False(load.Due);
    }

    [Fact]
    public void AnIntervalWithNoFramesInItIsNotDue()
    {
        SolverLoad load = new();
        load.Sample(double.NaN, double.NaN, SolverLoad.ReportIntervalSeconds * 2.0);

        Assert.False(load.Due);
        Assert.Equal(0, load.Samples);
    }

    [Fact]
    public void TakingTheSummaryStartsANewInterval()
    {
        SolverLoad load = Filled(1.0, 4.0, 100);
        Assert.True(load.Due);

        load.Take(vehicles: 1);

        Assert.False(load.Due);
        Assert.Equal(0, load.Samples);
    }

    /// <summary>
    /// The worst frame is reported, not just the typical one.
    ///
    /// <para>A world that keeps up except when it does not is a world whose shots were flown at two
    /// different step distributions, and a median alone cannot say so. The engine drops the fraction
    /// instantly and recovers it on an average, so the minimum is the only sample that survives.</para>
    /// </summary>
    [Fact]
    public void TheWorstFrameSurvivesIntoTheSummary()
    {
        SolverLoad load = new();
        double step = SolverLoad.ReportIntervalSeconds / 99.0;

        for (int i = 0; i < 99; i++) load.Sample(1.0, 4.0, step);
        load.Sample(0.25, 40.0, step);

        Assert.True(load.Due);

        string said = load.Take(vehicles: 4);

        Assert.Contains("4 vehicle(s)", said);
        Assert.Contains("0.250 worst", said);
        Assert.Contains("1.000 median", said);
    }

    [Fact]
    public void AFractionThatNeverLeavesOneReportsAWorstOfOne()
    {
        string said = Filled(1.0, 3.5, 200).Take(vehicles: 2);

        Assert.Contains("1.000 median, 1.000 worst", said);
        Assert.Contains("2 vehicle(s)", said);
        Assert.Contains("200 frames", said);
    }
}
