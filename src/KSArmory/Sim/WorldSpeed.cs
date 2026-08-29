namespace KSArmory;

/// <summary>
/// What one world clock should run at when several flights each have an opinion.
///
/// <para><b>There is one world and one speed.</b> A scripted shot asks for 0.01x while it is being
/// set up, 1x to fly the ascent, 100x through the coast and 1x again for the release — and with
/// several rockets in one world each of them asks, so whoever writes last wins and the others are
/// flown at a speed they did not choose. A flight set up at 100x is picked up hundreds of
/// kilometres from where the others were.</para>
///
/// <para><b>The slowest request wins</b>, which is the same rule <see cref="WarpPolicy"/> follows
/// against the player: a speed is a ceiling on what can still be simulated faithfully, so the
/// tightest ceiling binds. Nobody asking is not a request for 1x — it is no opinion at all, and the
/// world is left alone.</para>
/// </summary>
internal static class WorldSpeed
{
    /// <summary>
    /// The speed to run at, or NaN when nothing has an opinion.
    /// </summary>
    /// <param name="wanted">
    /// One request per flight. NaN entries are skipped rather than treated as zero — a flight that
    /// has not decided yet must not stop the world.
    /// </param>
    public static double Slowest(IReadOnlyList<double> wanted)
    {
        double slowest = double.NaN;

        for (int i = 0; i < wanted.Count; i++)
        {
            double one = wanted[i];

            // Non-positive is not a speed. It reaches here as a bug rather than as a request, and
            // honouring it would stop the world for every other flight in it.
            if (!double.IsFinite(one) || one <= 0.0) continue;

            if (double.IsNaN(slowest) || one < slowest) slowest = one;
        }

        return slowest;
    }

    /// <summary>
    /// The speed that lands a coast on <paramref name="wantedStepSeconds"/>, whatever the machine's
    /// frame time.
    ///
    /// <para><b>A speed is not a step, and the difference is what makes two nights incomparable.</b>
    /// <see cref="WarpPolicy"/> only ever reduces speed when the step overruns; below that bound it
    /// does nothing, so the step a coast actually gets is <c>speed x frame time</c> and moves with
    /// the machine. Two nights thirty hours apart integrated their coasts at 66 ms and 108 ms for
    /// that reason alone — <c>docs/MIRV-NEXT.md</c> <b>8ac</b>.</para>
    ///
    /// <para>It matters because the trim's achievable precision is
    /// <c>max(BusTrim.SettledMetresPerSecond, 0.5 x accel x step)</c>, which is linear in the step:
    /// measured headlessly at 0.245 m/s left on the bus at 66 ms against 0.420 at 108, and the miss
    /// is thousands of metres per metre a second. Asking for a step rather than a speed is what
    /// makes a slow machine cost wall clock instead of accuracy.</para>
    ///
    /// <para>Never above <paramref name="ceiling"/> and never below 1x: this is for spending a coast
    /// faster, and a frame slow enough to want less than real time has bigger problems. An
    /// unreadable frame time takes the ceiling — a pace nobody can measure is not a reason to
    /// crawl.</para>
    /// </summary>
    public static double ForStep(double wantedStepSeconds, double frameSeconds, double ceiling)
    {
        if (!(wantedStepSeconds > 0.0) || !(ceiling >= 1.0)) return 1.0;

        if (!(frameSeconds > 0.0) || !double.IsFinite(frameSeconds)) return ceiling;

        return Math.Clamp(wantedStepSeconds / frameSeconds, 1.0, ceiling);
    }
}
