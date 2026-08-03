namespace AirDefence;

/// <summary>
/// Turns the game's simulation clock into a frame delta the battery can safely step with.
///
/// <para><b>The mod used to run on player time.</b> That is wall-clock time, and it is wrong in
/// two ways that both showed up in game. Under timewarp the world advances many seconds per
/// frame while rounds advanced one frame's worth, so tracking and intercepts fell apart. While
/// <em>paused</em> player time kept running, so the radar kept accumulating dwell, matured a
/// firing solution, and launched into a frozen world.</para>
///
/// <para>Simulation time fixes both by construction: it does not advance while paused, and it
/// advances at the warped rate so a step covers the same span the world moved.</para>
///
/// <para>What it cannot fix is a step too large to integrate. <see cref="Interceptor"/>
/// subdivides internally but clamps to 64 sub-steps, so beyond
/// <see cref="Interceptor.MaxFaithfulStep"/> of simulated time a round would silently be
/// integrated too coarsely — at 700 m/s that is metres per sub-step turning into tens, straight
/// through a proximity fuse. Past that the honest answer is to stand down rather than to
/// pretend, which is what <see cref="State.Skipped"/> means.</para>
/// </summary>
internal sealed class SimClock
{
    /// <summary>What the caller should do with this frame.</summary>
    internal enum State
    {
        /// <summary>First sample. Nothing to step yet; the clock now has a reference.</summary>
        Priming,

        /// <summary>Paused, or no simulated time has passed. Do nothing at all.</summary>
        Idle,

        /// <summary>Step the simulation by the reported delta.</summary>
        Run,

        /// <summary>
        /// More time passed than can be simulated faithfully, or the clock went backwards.
        /// Abandon rounds in flight and reset tracking rather than stepping.
        /// </summary>
        Skipped,
    }

    /// <summary>
    /// Largest simulated step that can still be integrated at full fidelity. Derived from the
    /// interceptor's own sub-step budget rather than picked, so tightening one tightens both.
    /// </summary>
    public double MaxStep { get; init; } = Interceptor.MaxFaithfulStep;

    private double _last;
    private bool _primed;

    /// <summary>
    /// Samples the simulation clock.
    /// </summary>
    /// <param name="simSeconds">Elapsed simulated seconds, monotonic within a session.</param>
    /// <param name="paused">Whether the game is paused.</param>
    /// <param name="dt">Simulated seconds to advance by; zero unless the result is
    /// <see cref="State.Run"/>.</param>
    public State Advance(double simSeconds, bool paused, out double dt)
    {
        dt = 0.0;

        // A non-finite clock is not something to reason about; treat it as a discontinuity and
        // resynchronise on the next good sample.
        if (!double.IsFinite(simSeconds))
        {
            _primed = false;
            return State.Skipped;
        }

        if (!_primed)
        {
            _last = simSeconds;
            _primed = true;
            return State.Priming;
        }

        double elapsed = simSeconds - _last;
        _last = simSeconds;

        // Pause is checked as well as the delta, not instead of it: a paused game reports no
        // elapsed time anyway, but saying so explicitly is what stops a future change to the
        // clock quietly reintroducing firing-while-paused.
        if (paused || elapsed == 0.0) return State.Idle;

        // Backwards means the session's clock was replaced - loading a save, changing scene.
        // Nothing in flight relates to the new world.
        if (elapsed < 0.0) return State.Skipped;

        if (elapsed > MaxStep) return State.Skipped;

        dt = elapsed;
        return State.Run;
    }

    /// <summary>Forgets the reference sample, so the next call primes instead of stepping.</summary>
    public void Reset() => _primed = false;
}
