using System;

namespace KSArmory;

/// <summary>
/// How much of the world's time the engine is actually delivering per second of wall clock.
///
/// <para><b>The number is simulated seconds per real second, and nothing else will do.</b> The
/// engine's own <c>achievedSpeedFraction</c> looks like the answer and is not: it divides the
/// solver's deadline, <c>0.9 x min(frameTime, 1/30)</c>, by what the solve took — so once the frame
/// is longer than a thirtieth of a second the numerator stops growing and the fraction reads 1.000
/// however far behind the world falls. Measured: eight rockets reported 1.000 while advancing
/// 10 s of world per 24 s of wall clock.</para>
///
/// <para>So both terms are taken from outside the engine's accounting — the simulated clock's own
/// advance against a real one — and the fraction is kept only as the diagnostic that says whether
/// the <em>solver</em> is the reason.</para>
///
/// <para><b>This is the instrument for every throughput lever</b>, because they are all the same
/// question: how much more work fits before a second of world costs more than a second of clock.
/// <c>docs/METRE-LEVEL.md</c> section 5 asks for it by name.</para>
/// </summary>
internal sealed class SolverLoad
{
    /// <summary>How often a summary is worth a line. Long, because the interesting part is a trend.</summary>
    public const double ReportIntervalSeconds = 10.0;

    private readonly List<double> _fractions = [];
    private readonly List<double> _tickMs = [];
    private double _sim;
    private double _wall;
    private int _frames;

    /// <summary>Frames summarised so far in the current interval.</summary>
    public int Samples => _frames;

    /// <summary>
    /// Take one frame's reading.
    /// </summary>
    /// <param name="fraction">The engine's achieved speed fraction — a diagnostic, not the answer.</param>
    /// <param name="tickMs">What the vehicle solver's slowest recent tick took, in milliseconds.</param>
    /// <param name="simSeconds">How far the simulated clock advanced.</param>
    /// <param name="wallSeconds">How much real time passed while it did.</param>
    public void Sample(double fraction, double tickMs, double simSeconds, double wallSeconds)
    {
        if (double.IsFinite(fraction) && fraction > 0.0) _fractions.Add(fraction);
        if (double.IsFinite(tickMs) && tickMs >= 0.0) _tickMs.Add(tickMs);

        // A paused world advances no simulated time while the clock runs, and a scene load is a
        // whole second of wall with nothing behind it. Neither is the engine failing to keep up,
        // so neither is counted -- the ratio would read as a stall and mean nothing.
        if (!double.IsFinite(simSeconds) || simSeconds <= 0.0) return;
        if (!double.IsFinite(wallSeconds) || wallSeconds <= 0.0) return;

        _sim += simSeconds;
        _wall += wallSeconds;
        _frames++;
    }

    /// <summary>Whether enough wall clock has passed to be worth summarising.</summary>
    public bool Due => _wall >= ReportIntervalSeconds && _frames > 0;

    /// <summary>
    /// One line describing the interval, and start a new one.
    /// </summary>
    public string Take(int vehicles)
    {
        double[] f = [.. _fractions];
        double[] t = [.. _tickMs];
        Array.Sort(f);
        Array.Sort(t);

        double rate = _wall > 0.0 ? _sim / _wall : double.NaN;

        string said = $"solver load: {vehicles} vehicle(s), {rate:F2}x real time "
                      + $"({_sim:F1} s of world in {_wall:F1} s); tick {Median(t):F3} ms median, "
                      + $"{Percentile(t, 1.0):F3} ms worst; engine says {Median(f):F3} "
                      + $"median {Percentile(f, 0.0):F3} lowest, {Slowed(f)} frame(s) held "
                      + $"over {_frames} frames";

        _fractions.Clear();
        _tickMs.Clear();
        _sim = 0.0;
        _wall = 0.0;
        _frames = 0;

        return said;
    }

    // How many frames the engine ran the world slower than asked. The median cannot see this: KSA
    // drops its speed fraction in one frame when the vehicle solver overruns and recovers it by a
    // tenth per frame, so a real hold is tens of frames in a window of hundreds. That is why the
    // fraction is summarised by its lowest value and a count where the tick times beside it are not.
    private static int Slowed(double[] sorted)
    {
        int n = 0;
        for (int i = 0; i < sorted.Length && sorted[i] < 0.999; i++) n++;
        return n;
    }

    private static double Median(double[] sorted) => Percentile(sorted, 0.5);

    // Nearest-rank on an already-sorted array. The samples are a few hundred frames and the
    // question is which order of magnitude the tick is in, so interpolating buys nothing.
    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return double.NaN;

        int i = (int)(p * (sorted.Length - 1));
        return sorted[Math.Clamp(i, 0, sorted.Length - 1)];
    }
}
