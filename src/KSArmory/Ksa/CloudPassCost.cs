using KSA;

namespace KSArmory;

/// <summary>
/// What the shader pass costs the GPU, read back from the timestamps KSA's profiler keeps per
/// <c>ProfilerTag</c> region: timing an asynchronous dispatch from C# measures only the recording.
/// <para>The whole frame is sampled beside it: 2 ms on a 30 ms frame and on an 8 ms one are different answers.</para>
/// </summary>
internal static class CloudPassCost
{
    // What CloudPass tags its dispatch with. Matched by name because that is what the profiler
    // stores; a tag object of this mod's own is not what the engine hands back.
    private const string TagName = "KSArmory Cloud";

    // The stages CloudPass tags inside that region, which share its name as a prefix.
    private const string StagePrefix = "KSArmory Cloud: ";

    // Each stage's total and the frames it ran in, so a stage is reported against its own frames.
    private static readonly Dictionary<string, (double TotalMs, int Frames)> _stages = [];
    private static readonly Dictionary<string, double> _frameStages = [];

    private static int _frames;
    private static int _passFrames;
    private static double _framePeakMs;
    private static double _passTotalMs;
    private static double _passPeakMs;
    private static double _frameTotalMs;
    private static int _lastFrameIndex = -1;
    private static bool _warned;

    public static bool HasSamples => _frames > 0;

    /// <summary>Turns KSA's GPU profiler on, which it need not be.</summary>
    public static void Begin()
    {
        try
        {
            Profiler.Gpu.IsActive = true;
            Profiler.Gpu.Paused = false;
        }
        catch (Exception e)
        {
            Warn($"could not start the GPU profiler: {e.Message}");
        }

        Reset();
    }

    /// <summary>Forgets what has been measured, so a run starts clean.</summary>
    public static void Reset()
    {
        _frames = 0;
        _passFrames = 0;
        _framePeakMs = 0.0;
        _passTotalMs = 0.0;
        _passPeakMs = 0.0;
        _frameTotalMs = 0.0;
        _lastFrameIndex = -1;
        _stages.Clear();
    }

    /// <summary>
    /// Reads the last complete frame, so one still being written is never half-read, and only once,
    /// so a frame is not counted twice when the render outruns the simulation.
    /// </summary>
    public static void Sample()
    {
        try
        {
            if (!Profiler.Gpu.IsActive) return;

            int index = Profiler.Gpu.LastCompleteFrameIndex;
            if (index < 0 || index == _lastFrameIndex) return;

            ProfilerFrame[] frames = Profiler.Gpu.Frames;
            if (index >= frames.Length) return;

            ProfilerFrame frame = frames[index];
            if (!frame.IsValid) return;

            _lastFrameIndex = index;

            double pass = 0.0;
            double whole = 0.0;
            _frameStages.Clear();

            foreach (Sample sample in frame.AsSpan())
            {
                double ms = ProfilerWindowBase.TicksToMs(sample.Ticks);

                // Root samples only for the frame's own total: a nested region is already inside
                // its parent, so summing everything counts the same microsecond several times.
                if (sample.ParentIndex == ushort.MaxValue) whole += ms;

                string name = sample.Id.ToString();
                if (name == TagName) pass += ms;
                else if (name.StartsWith(StagePrefix, StringComparison.Ordinal))
                {
                    _frameStages[name] = _frameStages.GetValueOrDefault(name) + ms;
                }
            }

            foreach ((string name, double ms) in _frameStages)
            {
                (double total, int count) = _stages.GetValueOrDefault(name);
                _stages[name] = (total + ms, count + 1);
            }

            // Counted whether or not the pass ran, so a run with it off is the baseline.
            _frames++;
            _frameTotalMs += whole;
            _framePeakMs = Math.Max(_framePeakMs, whole);

            if (pass <= 0.0) return;

            _passFrames++;
            _passTotalMs += pass;
            _passPeakMs = Math.Max(_passPeakMs, pass);
        }
        catch (Exception e)
        {
            Warn($"could not read the GPU profiler: {e.Message}");
        }
    }

    /// <summary>What it cost, as a line somebody reads. Empty when nothing was measured.</summary>
    public static string Report()
    {
        if (_frames <= 0) return string.Empty;

        double frame = _frameTotalMs / _frames;

        // A shader KSA failed to compile at load says so, rather than reading as the "pass off"
        // control; asked of a build that ran, since a pass switched off is never built.
        string had = _passFrames > 0
                         ? $"pass {_passTotalMs / _passFrames:F2} ms a frame over {_passFrames}, "
                           + $"peak {_passPeakMs:F2}"
                         : CloudPass.BuildFailed ? "pass FAILED TO BUILD" : "pass off";

        // What a ground mark dispatches over, beside what the pass costs: a mark is permanent, so
        // the share of the screen it covers is what it costs for the rest of the session.
        string marks = CloudPass.LastMarkTile < 1.0
                           ? $", a mark covers {CloudPass.LastMarkTile:P1} of the screen"
                           : string.Empty;

        // Each stage for the frames it ran in: the flash runs only while a glare lingers, and its mean
        // over frames it never ran in would hide what it costs when it does.
        string stages = string.Join(", ", _stages.Select(
            s => $"{s.Key[StagePrefix.Length..]} {s.Value.TotalMs / s.Value.Frames:F2}"
                 + (s.Value.Frames < _passFrames ? $" over {s.Value.Frames}" : "")));

        return $"gpu: {had}; whole frame {frame:F2} ms, peak {_framePeakMs:F2}, over {_frames} frames"
               + marks + (stages.Length > 0 ? $"; stages {stages}" : "");
    }

    private static void Warn(string what)
    {
        if (_warned) return;

        _warned = true;
        Log.Warn($"cloud pass cost: {what}");
    }
}
