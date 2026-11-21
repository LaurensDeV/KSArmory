using System.Collections.Generic;

namespace KSArmory;

/// <summary>
/// Samples of one quantity over one phase of a flight, summarised when the phase ends -- so a night logged at INFO still
/// says how a phase went, where the per-sample line is DEBUG.
/// </summary>
public sealed class PhaseSamples
{
    private readonly List<double> _values = [];
    private readonly List<double> _sorted = [];

    public int Count => _values.Count;

    public double Max { get; private set; } = double.NaN;

    public void Add(double value)
    {
        if (!double.IsFinite(value)) return;
        _values.Add(value);
        if (!(value <= Max)) Max = value;
    }

    /// <summary>The value <paramref name="fraction"/> of the samples are at or under, nearest-rank; NaN with none.</summary>
    public double Percentile(double fraction)
    {
        if (_values.Count == 0) return double.NaN;
        _sorted.Clear();
        _sorted.AddRange(_values);
        _sorted.Sort();
        int rank = (int)Math.Ceiling(Math.Clamp(fraction, 0.0, 1.0) * _sorted.Count);
        return _sorted[Math.Clamp(rank - 1, 0, _sorted.Count - 1)];
    }

    public void Clear()
    {
        _values.Clear();
        Max = double.NaN;
    }
}
