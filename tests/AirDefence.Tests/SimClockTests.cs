using Xunit;

namespace AirDefence.Tests;

/// <summary>
/// What the battery does with the simulation step KSA reports.
///
/// <para>Two behaviours here came from play. The battery fired while the game was paused, and
/// tracking fell apart when the simulation speed changed — both because the mod stepped on
/// StarMap's player-time delta, which is wall-clock and so keeps running through a pause and
/// stays at 1× under warp.</para>
///
/// <para>The third came from a flight log: rounds zigzagging laterally with the vertical axis
/// clean. Reading the step KSA applied, rather than differencing a clock around it, is what
/// removes the phase error behind that — see <see cref="SimClock"/>.</para>
/// </summary>
public class SimClockTests
{
    [Fact]
    public void APausedGameAdvancesNothing()
    {
        Assert.Equal(SimClock.State.Idle, SimClock.Classify(0.0, paused: true, out double dt));
        Assert.Equal(0.0, dt);
    }

    [Fact]
    public void APausedGameAdvancesNothingEvenIfAStepIsReported()
    {
        // Belt and braces. If a future build ever reported a step while paused, the mod must
        // still not accumulate dwell and fire - which is exactly the bug that was seen.
        Assert.Equal(SimClock.State.Idle, SimClock.Classify(0.05, paused: true, out double dt));
        Assert.Equal(0.0, dt);
    }

    [Fact]
    public void AnOrdinaryStepRuns()
    {
        Assert.Equal(SimClock.State.Run, SimClock.Classify(0.016, paused: false, out double dt));
        Assert.Equal(0.016, dt, 9);
    }

    [Fact]
    public void TheStepIsPassedThroughExactly()
    {
        // The whole point of reading the applied step is that it is not adjusted, rounded or
        // re-derived on the way through. Whatever KSA moved the world by is what the round
        // integrates over, or the difference reappears multiplied by 29.8 km/s.
        foreach (double step in new[] { 0.0011, 0.016, 0.0167, 0.019993, 0.2 })
        {
            Assert.Equal(SimClock.State.Run, SimClock.Classify(step, paused: false, out double dt));
            Assert.Equal(step, dt, 12);
        }
    }

    [Fact]
    public void ModestTimewarpStillRuns()
    {
        // 10x warp at 60 fps is ~0.167 s per step, well inside what the interceptor can
        // integrate. This must keep working, not stand down.
        Assert.Equal(SimClock.State.Run, SimClock.Classify(0.167, paused: false, out double dt));
        Assert.Equal(0.167, dt, 9);
    }

    [Fact]
    public void AStepTooLargeToIntegrateStandsDownRatherThanCoarsening()
    {
        // Past Interceptor.MaxFaithfulStep a round at 700 m/s starts stepping over its own fuse
        // radius. Refusing is the honest answer.
        Assert.Equal(SimClock.State.Skipped,
            SimClock.Classify(Interceptor.MaxFaithfulStep + 0.001, paused: false, out double dt));
        Assert.Equal(0.0, dt);
    }

    [Fact]
    public void TheBudgetIsExactlyWhatTheInterceptorCanIntegrate()
    {
        // Tied to the interceptor's own sub-step budget rather than a number picked here, so the
        // two cannot drift apart.
        Assert.Equal(SimClock.State.Run,
            SimClock.Classify(Interceptor.MaxFaithfulStep, paused: false, out double dt));
        Assert.Equal(Interceptor.MaxFaithfulStep, dt, 9);
    }

    [Fact]
    public void AZeroOrNegativeStepDoesNothing()
    {
        Assert.Equal(SimClock.State.Idle, SimClock.Classify(0.0, paused: false, out _));
        Assert.Equal(SimClock.State.Idle, SimClock.Classify(-0.016, paused: false, out double dt));
        Assert.Equal(0.0, dt);
    }

    [Fact]
    public void ANonFiniteStepDoesNothing()
    {
        Assert.Equal(SimClock.State.Idle, SimClock.Classify(double.NaN, paused: false, out _));
        Assert.Equal(SimClock.State.Idle, SimClock.Classify(double.PositiveInfinity, paused: false, out _));
    }

    [Fact]
    public void ThereIsNoStateToGetWrongAcrossASceneChange()
    {
        // Stateless by design. The previous version held a reference sample and needed priming,
        // a reset on leaving flight, and a rule for the clock going backwards on load - three
        // ways to be wrong that differencing created and that reading the applied step removes.
        Assert.Equal(SimClock.State.Run, SimClock.Classify(0.016, paused: false, out _));
        Assert.Equal(SimClock.State.Idle, SimClock.Classify(0.0, paused: true, out _));
        Assert.Equal(SimClock.State.Run, SimClock.Classify(0.016, paused: false, out double dt));
        Assert.Equal(0.016, dt, 9);
    }
}
