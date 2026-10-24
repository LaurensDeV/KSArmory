using System;

namespace KSArmory;

/// <summary>
/// How much of the world's time the engine is actually delivering per second of wall clock.
///
/// <para>Simulated seconds per real second. The engine's <c>achievedSpeedFraction</c> divides the
/// solver's deadline, <c>0.9 x min(frameTime, 1/30)</c>, by the solve's time, so past a 1/30 s frame
/// it reads 1.000 however far behind the world falls: eight rockets read 1.000 at 10 s of world per
/// 24 s of wall clock. It is kept only to say whether the solver is the reason.
/// <c>docs/METRE-LEVEL.md</c> section 5b.</para>
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

    /// <summary>Take one frame's reading.</summary>
    /// <param name="fraction">The engine's achieved speed fraction — a diagnostic, not the answer.</param>
    /// <param name="tickMs">What the vehicle solver's slowest recent tick took, in milliseconds.</param>
    /// <param name="simSeconds">How far the simulated clock advanced.</param>
    /// <param name="wallSeconds">How much real time passed while it did.</param>
    public void Sample(double fraction, double tickMs, double simSeconds, double wallSeconds)
    {
        if (double.IsFinite(fraction) && fraction > 0.0) _fractions.Add(fraction);
        if (double.IsFinite(tickMs) && tickMs >= 0.0) _tickMs.Add(tickMs);

        // A pause or a scene load advances no simulated time, and is not the engine falling behind.
        if (!double.IsFinite(simSeconds) || simSeconds <= 0.0) return;
        if (!double.IsFinite(wallSeconds) || wallSeconds <= 0.0) return;

        _sim += simSeconds;
        _wall += wallSeconds;
        _frames++;
    }

    /// <summary>Whether enough wall clock has passed to be worth summarising.</summary>
    public bool Due => _wall >= ReportIntervalSeconds && _frames > 0;

    /// <summary>One line describing the interval, and start a new one.</summary>
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

    // A count, because the median cannot see a hold: KSA drops the fraction in one frame when the
    // solver overruns and recovers it a tenth per frame, tens of frames in a window of hundreds.
    private static int Slowed(double[] sorted)
    {
        int n = 0;
        for (int i = 0; i < sorted.Length && sorted[i] < 0.999; i++) n++;
        return n;
    }

    private static double Median(double[] sorted) => Percentile(sorted, 0.5);

    // Nearest-rank on an already-sorted array.
    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return double.NaN;

        int i = (int)(p * (sorted.Length - 1));
        return sorted[Math.Clamp(i, 0, sorted.Length - 1)];
    }
}
