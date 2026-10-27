namespace KSArmory;

/// <summary>
/// What one world clock should run at when several flights each have an opinion.
///
/// <para>The slowest request wins, as in <see cref="WarpPolicy"/>: a speed is a ceiling on what can be
/// simulated faithfully. Nobody asking is no opinion, not a request for 1x.</para>
/// </summary>
internal static class WorldSpeed
{
    /// <summary>The speed to run at, or NaN when nothing has an opinion.</summary>
    /// <param name="wanted">One request per flight; NaN is undecided, skipped rather than read as zero.</param>
    public static double Slowest(IReadOnlyList<double> wanted)
    {
        double slowest = double.NaN;

        for (int i = 0; i < wanted.Count; i++)
        {
            double one = wanted[i];

            // Non-positive is a bug, and honouring it would stop the world for every other flight.
            if (!double.IsFinite(one) || one <= 0.0) continue;

            if (double.IsNaN(slowest) || one < slowest) slowest = one;
        }

        return slowest;
    }

    /// <summary>
    /// The speed that lands a coast on <paramref name="wantedStepSeconds"/>, whatever the machine's
    /// frame time.
    ///
    /// <para>A speed's step is <c>speed x frame time</c> and moves with the machine, and the trim's precision
    /// is linear in the step: 0.245 m/s left at 66 ms against 0.420 at 108 (<c>docs/MIRV-NEXT.md</c> 8ac).</para>
    ///
    /// <para>Between 1x and <paramref name="ceiling"/>; an unreadable frame time takes the ceiling.</para>
    /// </summary>
    public static double ForStep(double wantedStepSeconds, double frameSeconds, double ceiling)
    {
        if (!(wantedStepSeconds > 0.0) || !(ceiling >= 1.0)) return 1.0;

        if (!(frameSeconds > 0.0) || !double.IsFinite(frameSeconds)) return ceiling;

        return Math.Clamp(wantedStepSeconds / frameSeconds, 1.0, ceiling);
    }
}
