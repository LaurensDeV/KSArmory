using System.Diagnostics;

namespace KSArmory;

/// <summary>
/// What this mod costs the frame, split by the work that costs it.
///
/// <para><b>Frame time is the only thing that buys simulation rate</b> — the engine advances the
/// world by at most a thirtieth of a second per frame, so <c>sim rate = 33.3 ms / frame time</c>
/// (<c>docs/METRE-LEVEL.md</c> §5). <see cref="SolverLoad"/> already says what the frame costs in
/// total and what the engine's own vehicle solver took of it; on eight rockets those were 78.7 ms
/// and 13–20, leaving about sixty milliseconds belonging to nobody.</para>
///
/// <para><b>Three mod-side suspects were eliminated by turning them off one at a time</b>, which
/// is not the same as measuring the mod. An ablation can only clear the things somebody thought
/// of, and it clears them one at a time against a number that moves by itself. This measures every
/// span instead, so what is left over is genuinely the engine's rather than merely unattributed.
/// </para>
///
/// <para>Cheap enough to leave on: two timestamp reads per span, a dictionary add, and a string
/// built once every <see cref="ReportIntervalSeconds"/>. It reports the <b>worst</b> frame beside
/// the mean, because a cost that arrives on one frame in thirty is what a mean hides and what a
/// player feels.</para>
/// </summary>
internal sealed class FrameBudget
{
    /// <summary>How often it reports, in real seconds. Long enough that the line is rare.</summary>
    public const double ReportIntervalSeconds = 10.0;

    private readonly Dictionary<string, double> _total = [];
    private readonly Dictionary<string, double> _worst = [];
    private readonly Dictionary<string, double> _thisFrame = [];

    private double _wall;
    private int _frames;
    private double _worstFrameMs;

    /// <summary>A span being timed. Disposing it books the time against its name.</summary>
    public readonly struct Span(FrameBudget budget, string name, long from) : IDisposable
    {
        public void Dispose() =>
            budget.Book(name, Stopwatch.GetElapsedTime(from).TotalMilliseconds);
    }

    /// <summary>Time a block of work: <c>using (budget.Measure("icbm")) { ... }</c>.</summary>
    public Span Measure(string name) => new(this, name, Stopwatch.GetTimestamp());

    private void Book(string name, double ms)
    {
        _thisFrame[name] = (_thisFrame.TryGetValue(name, out double had) ? had : 0.0) + ms;
    }

    /// <summary>Close the frame off. <paramref name="wallSeconds"/> is real time, not simulated.</summary>
    public void EndFrame(double wallSeconds)
    {
        if (double.IsFinite(wallSeconds) && wallSeconds > 0.0) _wall += wallSeconds;

        _frames++;

        double frameMs = 0.0;

        foreach ((string name, double ms) in _thisFrame)
        {
            frameMs += ms;

            _total[name] = (_total.TryGetValue(name, out double sum) ? sum : 0.0) + ms;

            if (!_worst.TryGetValue(name, out double worst) || ms > worst) _worst[name] = ms;
        }

        if (frameMs > _worstFrameMs) _worstFrameMs = frameMs;

        _thisFrame.Clear();
    }

    /// <summary>Whether there is a window worth reporting.</summary>
    public bool Due => _wall >= ReportIntervalSeconds && _frames > 0;

    /// <summary>
    /// One line, and the window is reset. Spans are listed dearest first, because the question
    /// this exists to answer is which one to look at.
    /// </summary>
    public string Take()
    {
        double mean = 0.0;
        foreach (double sum in _total.Values) mean += sum;
        mean /= _frames;

        List<string> names = [.. _total.Keys];
        names.Sort((a, b) => _total[b].CompareTo(_total[a]));

        var said = new System.Text.StringBuilder();
        said.Append($"mod frame: {mean:F2} ms mean, {_worstFrameMs:F2} ms worst, over {_frames} frames");

        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            said.Append($" | {name} {_total[name] / _frames:F2}/{_worst[name]:F2}");
        }

        _total.Clear();
        _worst.Clear();
        _wall = 0.0;
        _frames = 0;
        _worstFrameMs = 0.0;

        return said.ToString();
    }
}
