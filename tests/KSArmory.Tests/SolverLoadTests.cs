using Xunit;

namespace KSArmory.Tests;

public sealed class SolverLoadTests
{
    private static SolverLoad Filled(double fraction, double tickMs, int frames)
    {
        SolverLoad load = new();
        double step = SolverLoad.ReportIntervalSeconds / (frames - 1);

        for (int i = 0; i < frames; i++) load.Sample(fraction, tickMs, step, step);

        return load;
    }

    [Fact]
    public void NothingIsDueBeforeTheIntervalHasPassed()
    {
        SolverLoad load = new();
        load.Sample(1.0, 4.0, 0.5, SolverLoad.ReportIntervalSeconds / 2.0);

        Assert.False(load.Due);
    }

    [Fact]
    public void AnIntervalWithNoFramesInItIsNotDue()
    {
        SolverLoad load = new();
        load.Sample(double.NaN, double.NaN, double.NaN, SolverLoad.ReportIntervalSeconds * 2.0);

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
    /// A world running behind the clock says so, whatever the engine's own fraction claims.
    ///
    /// <para>This is the whole reason the ratio is taken from outside: the engine divides its
    /// solver's deadline by the solve, and that deadline stops growing once a frame is longer than
    /// a thirtieth of a second — so it reports 1.000 while the world falls behind. Measured at
    /// eight rockets as exactly that, 10 s of world in 24 s of wall.</para>
    /// </summary>
    [Fact]
    public void AWorldRunningBehindTheClockSaysSo()
    {
        SolverLoad load = new();
        double step = SolverLoad.ReportIntervalSeconds / 99.0;

        // Half a second of world per second of clock, with the engine insisting it is keeping up.
        for (int i = 0; i < 100; i++) load.Sample(1.0, 4.0, step * 0.5, step);

        Assert.True(load.Due);

        string said = load.Take(vehicles: 4);

        Assert.Contains("4 vehicle(s)", said);
        Assert.Contains("0.50x real time", said);
        Assert.Contains("engine says 1.000", said);
    }

    [Fact]
    public void AFractionThatNeverLeavesOneReportsAWorstOfOne()
    {
        string said = Filled(1.0, 3.5, 200).Take(vehicles: 2);

        Assert.Contains("1.00x real time", said);
        Assert.Contains("2 vehicle(s)", said);
        Assert.Contains("200 frames", said);
    }
}
