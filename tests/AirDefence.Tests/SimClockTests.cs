using Xunit;

namespace AirDefence.Tests;

/// <summary>
/// The frame clock, which is what stops the battery firing into a paused game.
///
/// <para>Both behaviours pinned here were reported from play: firing while the simulation was
/// paused, and tracking falling apart under timewarp. The cause was the same in both — the mod
/// stepped on StarMap's player-time delta, which is wall-clock and therefore keeps running when
/// the world is frozen and stays at 1x when the world is warped.</para>
/// </summary>
public class SimClockTests
{
    private static SimClock Clock() => new();

    [Fact]
    public void TheFirstSampleOnlyEstablishesAReference()
    {
        // Whatever the clock already read, the first frame cannot know how much of it elapsed
        // while we were not looking.
        SimClock c = Clock();
        Assert.Equal(SimClock.State.Priming, c.Advance(12345.0, paused: false, out double dt));
        Assert.Equal(0.0, dt);
    }

    [Fact]
    public void APausedGameAdvancesNothing()
    {
        SimClock c = Clock();
        c.Advance(100.0, paused: false, out _);

        // Simulated time does not move while paused, so this is what the clock really sees.
        Assert.Equal(SimClock.State.Idle, c.Advance(100.0, paused: true, out double dt));
        Assert.Equal(0.0, dt);
    }

    [Fact]
    public void APausedGameAdvancesNothingEvenIfTheClockMoves()
    {
        // Belt and braces. If some future build let simulated time creep while paused, the mod
        // must still not accumulate dwell and fire - which is exactly the bug that was seen.
        SimClock c = Clock();
        c.Advance(100.0, paused: false, out _);

        Assert.Equal(SimClock.State.Idle, c.Advance(100.05, paused: true, out double dt));
        Assert.Equal(0.0, dt);
    }

    [Fact]
    public void RealTimePassingGivesTheSimulatedDelta()
    {
        SimClock c = Clock();
        c.Advance(100.0, paused: false, out _);

        Assert.Equal(SimClock.State.Run, c.Advance(100.016, paused: false, out double dt));
        Assert.Equal(0.016, dt, 9);
    }

    [Fact]
    public void ModestTimewarpStillRuns()
    {
        // 10x warp at 60 fps is ~0.167 s of simulated time per frame, well inside what the
        // interceptor can integrate. This must keep working, not stand down.
        SimClock c = Clock();
        c.Advance(0.0, paused: false, out _);

        Assert.Equal(SimClock.State.Run, c.Advance(0.167, paused: false, out double dt));
        Assert.Equal(0.167, dt, 9);
    }

    [Fact]
    public void AStepTooLargeToIntegrateStandsDownRatherThanCoarsening()
    {
        SimClock c = Clock();
        c.Advance(0.0, paused: false, out _);

        // Past Interceptor.MaxFaithfulStep a round at 700 m/s starts stepping over its own fuse
        // radius. Refusing is the honest answer.
        Assert.Equal(SimClock.State.Skipped,
            c.Advance(Interceptor.MaxFaithfulStep + 0.001, paused: false, out double dt));
        Assert.Equal(0.0, dt);
    }

    [Fact]
    public void TheBudgetIsExactlyWhatTheInterceptorCanIntegrate()
    {
        // Tied to the interceptor's own sub-step budget rather than a number picked here, so
        // the two cannot drift apart.
        SimClock c = Clock();
        c.Advance(0.0, paused: false, out _);

        Assert.Equal(SimClock.State.Run,
            c.Advance(Interceptor.MaxFaithfulStep, paused: false, out double dt));
        Assert.Equal(Interceptor.MaxFaithfulStep, dt, 9);
    }

    [Fact]
    public void AClockThatGoesBackwardsIsADiscontinuity()
    {
        // Loading a save replaces the session clock. Nothing in flight relates to the new world.
        SimClock c = Clock();
        c.Advance(5000.0, paused: false, out _);

        Assert.Equal(SimClock.State.Skipped, c.Advance(12.0, paused: false, out double dt));
        Assert.Equal(0.0, dt);
    }

    [Fact]
    public void ADiscontinuityStillLeavesTheClockUsable()
    {
        // After standing down it must resynchronise on the new timeline rather than reporting a
        // second huge delta forever.
        SimClock c = Clock();
        c.Advance(5000.0, paused: false, out _);
        c.Advance(12.0, paused: false, out _);            // backwards: Skipped

        Assert.Equal(SimClock.State.Run, c.Advance(12.016, paused: false, out double dt));
        Assert.Equal(0.016, dt, 9);
    }

    [Fact]
    public void ANonFiniteClockIsRefusedAndThenRecovers()
    {
        SimClock c = Clock();
        c.Advance(100.0, paused: false, out _);

        Assert.Equal(SimClock.State.Skipped, c.Advance(double.NaN, paused: false, out _));

        // Having dropped its reference, the next good sample primes rather than differencing
        // against NaN.
        Assert.Equal(SimClock.State.Priming, c.Advance(200.0, paused: false, out _));
        Assert.Equal(SimClock.State.Run, c.Advance(200.02, paused: false, out double dt));
        Assert.Equal(0.02, dt, 9);
    }

    [Fact]
    public void ResetMakesTheNextSamplePrimeAgain()
    {
        // Leaving flight uses this, so re-entering does not report the whole time away as one
        // enormous frame.
        SimClock c = Clock();
        c.Advance(100.0, paused: false, out _);
        c.Reset();

        Assert.Equal(SimClock.State.Priming, c.Advance(9000.0, paused: false, out _));
        Assert.Equal(SimClock.State.Run, c.Advance(9000.01, paused: false, out double dt));
        Assert.Equal(0.01, dt, 9);
    }

    [Fact]
    public void APauseThenResumeDoesNotDeliverTheTimeSpentPaused()
    {
        // The reported symptom in full: pause, wait, resume. Simulated time did not move, so
        // there is nothing to deliver and nothing to fire with.
        SimClock c = Clock();
        c.Advance(500.0, paused: false, out _);

        for (int frame = 0; frame < 120; frame++)
            Assert.Equal(SimClock.State.Idle, c.Advance(500.0, paused: true, out _));

        Assert.Equal(SimClock.State.Run, c.Advance(500.016, paused: false, out double dt));
        Assert.Equal(0.016, dt, 9);
    }
}
