namespace KSArmory;

/// <summary>
/// Decides what to do with the simulation step KSA has just applied — never player time, which runs
/// through a pause and ignores timewarp.
///
/// <para>The step comes from the engine's own step boundaries (<c>KsaWorld.ConsumeSimStep</c>), never
/// a clock the mod reads from its hook: out of phase by a sub-millisecond, ~29.8 km/s makes it tens
/// of metres of alternating lateral error. Stateless, so nothing here can be wrong across a scene change.</para>
/// </summary>
internal static class SimClock
{
    internal enum State
    {
        /// <summary>Paused, or no simulated time passed. Do nothing at all.</summary>
        Idle,

        Run,

        /// <summary>
        /// More time passed than can be integrated faithfully. Abandon rounds in flight and
        /// reset tracking rather than stepping.
        /// </summary>
        Skipped,
    }

    /// <summary>
    /// Largest step a round with no opinion can be integrated across, from the interceptor's sub-step
    /// budget. A default: <see cref="MunitionProfile.MaxFaithfulStepSeconds"/> decides per round.
    /// </summary>
    public const double MaxStep = Interceptor.MaxFaithfulStep;

    /// <param name="dt">Seconds to advance by; zero unless the result is <see cref="State.Run"/>.</param>
    public static State Classify(double stepSeconds, bool paused, out double dt,
                                 double maxStep = MaxStep)
    {
        dt = 0.0;

        // A paused game reports no step anyway; checked as well so firing while paused stays unreachable.
        if (paused) return State.Idle;

        if (!double.IsFinite(stepSeconds) || stepSeconds <= 0.0) return State.Idle;

        // Past it the sub-step clamp stretches each sub-step, and the curvature the fuse solve drops
        // grows as the square.
        if (stepSeconds > maxStep) return State.Skipped;

        dt = stepSeconds;
        return State.Run;
    }

    /// <summary>
    /// Player time while the world runs and none while it is paused: the clock for how long something
    /// is watched, so slow motion does not stretch it and a pause holds it.
    /// </summary>
    /// <param name="playerSeconds">The wall-clock frame time.</param>
    /// <param name="stepSeconds">Simulated seconds the world just advanced by; zero while paused.</param>
    public static double Viewing(double playerSeconds, double stepSeconds)
        => double.IsFinite(stepSeconds) && stepSeconds > 0.0 && double.IsFinite(playerSeconds)
               ? Math.Max(0.0, playerSeconds)
               : 0.0;
}
