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
}
