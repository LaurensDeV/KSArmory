namespace KSArmory;

/// <summary>
/// How hard the engine's vehicle solver is working, and whether it is keeping up.
///
/// <para>The world advances by <c>dtPlayer x achievedFraction x simSpeed</c>, so simulated seconds
/// per wall second are <c>achievedFraction x simSpeed</c> and do not depend on the frame rate at
/// all — which is why an unattended render profile made a shot <em>slower</em> rather than faster.
/// The fraction is the engine dividing its solver's deadline by what the solve actually took, so it
/// is the one number that says whether more work in the world costs wall clock.</para>
///
/// <para><b>It is the instrument for both of the untested throughput levers</b> — several rockets
/// in one world, and several game instances — because both are the same question: how much more
/// vehicle work fits before the fraction leaves 1.0. <c>docs/METRE-LEVEL.md</c> section 5 asks for
/// it by name and nothing has ever recorded it.</para>
///
/// <para>Summarised over an interval rather than printed per frame. The fraction drops instantly
/// and recovers on a slow average, so a single sample catches whichever side of that it landed on;
/// what the levers are bounded by is the <em>worst</em> moment, not the typical one.</para>
/// </summary>
internal sealed class SolverLoad
{
    /// <summary>How often a summary is worth a line. Long, because the interesting part is a trend.</summary>
    public const double ReportIntervalSeconds = 10.0;

    private readonly List<double> _fractions = [];
    private readonly List<double> _tickMs = [];
    private double _since;

    /// <summary>Frames summarised so far in the current interval.</summary>
    public int Samples => _fractions.Count;

    /// <summary>
    /// Take one frame's reading.
    /// </summary>
    /// <param name="fraction">The engine's achieved speed fraction, 1.0 when it is keeping up.</param>
    /// <param name="tickMs">What the vehicle solver's slowest recent tick took, in milliseconds.</param>
    /// <param name="stepSeconds">Wall-clock seconds since the last reading.</param>
    public void Sample(double fraction, double tickMs, double stepSeconds)
    {
        if (double.IsFinite(fraction) && fraction > 0.0) _fractions.Add(fraction);
        if (double.IsFinite(tickMs) && tickMs >= 0.0) _tickMs.Add(tickMs);
        if (double.IsFinite(stepSeconds) && stepSeconds > 0.0) _since += stepSeconds;
    }

    /// <summary>Whether enough wall clock has passed to be worth summarising.</summary>
    public bool Due => _since >= ReportIntervalSeconds && _fractions.Count > 0;

    /// <summary>
    /// One line describing the interval, and start a new one.
    ///
    /// <para>The fraction's <em>minimum</em> is reported beside its median because that is the
    /// quantity a throughput lever is bounded by: a world that keeps up except when it does not is
    /// a world whose shots were flown at two different step distributions.</para>
    /// </summary>
    public string Take(int vehicles)
    {
        double[] f = [.. _fractions];
        double[] t = [.. _tickMs];
        System.Array.Sort(f);
        System.Array.Sort(t);

        string said = $"solver load: {vehicles} vehicle(s), achieved "
                      + $"{Median(f):F3} median, {f[0]:F3} worst; tick {Median(t):F2} ms median, "
                      + $"{Percentile(t, 0.9):F2} ms p90 over {_fractions.Count} frames";

        _fractions.Clear();
        _tickMs.Clear();
        _since = 0.0;

        return said;
    }

    private static double Median(double[] sorted) => Percentile(sorted, 0.5);

    // Nearest-rank on an already-sorted array. The samples are a few hundred frames and the
    // question is which order of magnitude the tick is in, so interpolating buys nothing.
    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return double.NaN;

        int i = (int)(p * (sorted.Length - 1));
        return sorted[System.Math.Clamp(i, 0, sorted.Length - 1)];
    }
}
