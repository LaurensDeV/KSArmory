namespace AirDefence;

/// <summary>
/// Decides what to do with the simulation step KSA has just applied.
///
/// <para><b>Never player time.</b> That is wall-clock: it keeps running while the game is paused,
/// so the radar would mature a firing solution and launch into a frozen world, and it ignores
/// timewarp, so the world outruns the rounds.</para>
///
/// <para><b>And it must be the step KSA applied, not one the mod measures.</b> Differencing a
/// clock from a postfix hook can land a step out of phase, and the round then integrates over a
/// different span than the world moved by — multiplied by ~29.8 km/s, a sub-millisecond wobble is
/// tens of metres of alternating lateral error. <c>Universe.GetLastSimStep().DeltaTime</c> is the
/// applied step and cannot be out of phase with itself.</para>
///
/// <para>Stateless on purpose: with no differencing there is no previous sample to hold, and
/// therefore no priming, no reset, and no way to be wrong across a scene change.</para>
/// </summary>
internal static class SimClock
{
    /// <summary>What the caller should do with this frame.</summary>
    internal enum State
    {
        /// <summary>Paused, or no simulated time passed. Do nothing at all.</summary>
        Idle,

        /// <summary>Step the simulation by the reported delta.</summary>
        Run,

        /// <summary>
        /// More time passed than can be integrated faithfully. Abandon rounds in flight and
        /// reset tracking rather than stepping.
        /// </summary>
        Skipped,
    }

    /// <summary>
    /// Largest step that can still be integrated at full fidelity. Derived from the
    /// interceptor's own sub-step budget rather than picked, so tightening one tightens both.
    /// </summary>
    public const double MaxStep = Interceptor.MaxFaithfulStep;

    /// <summary>
    /// Classifies the step KSA has just applied.
    /// </summary>
    /// <param name="stepSeconds">Simulated seconds the world just advanced by.</param>
    /// <param name="paused">Whether the game is paused.</param>
    /// <param name="dt">Seconds to advance by; zero unless the result is <see cref="State.Run"/>.</param>
    public static State Classify(double stepSeconds, bool paused, out double dt)
    {
        dt = 0.0;

        // Pause is checked as well as the step, not instead of it: a paused game reports no
        // step anyway; saying so explicitly keeps firing-while-paused unreachable.
        if (paused) return State.Idle;

        // A non-finite or negative step is not something to reason about. Neither is zero.
        if (!double.IsFinite(stepSeconds) || stepSeconds <= 0.0) return State.Idle;

        // Past MaxStep the interceptor's sub-step clamp starts stretching each step, and a round
        // at 700 m/s begins skipping over its own fuse radius. Refusing is the honest answer.
        if (stepSeconds > MaxStep) return State.Skipped;

        dt = stepSeconds;
        return State.Run;
    }
}
