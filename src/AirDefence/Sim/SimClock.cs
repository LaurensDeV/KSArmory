namespace AirDefence;

/// <summary>
/// Decides what to do with the simulation step KSA has just applied.
///
/// <para><b>The mod used to run on player time.</b> That is wall-clock, and it is wrong twice
/// over, both seen in game. Under timewarp the world advances many seconds per frame while
/// rounds advanced one frame's worth, so tracking fell apart. While <em>paused</em>, player
/// time kept running, so the radar accumulated dwell, matured a firing solution and launched
/// into a frozen world.</para>
///
/// <para><b>And it must be the step KSA applied, not one the mod measures.</b> Differencing a
/// clock from a postfix hook can land a step out of phase — the round then integrates over a
/// different span than the world moved by, and the difference is multiplied by the platform's
/// ~29.8 km/s of ecliptic velocity. A frame-time wobble of under a millisecond becomes tens of
/// metres of lateral error, alternating in sign, which in flight is a hard zigzag with the
/// vertical axis left clean because the orbital velocity barely projects onto it. That is what
/// the flight log showed. <c>Universe.GetLastSimStep().DeltaTime</c> is the applied step and
/// cannot be out of phase with itself, so there is nothing left to measure.</para>
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
        // step anyway, but saying so explicitly is what stops a future change quietly
        // reintroducing firing-while-paused.
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
