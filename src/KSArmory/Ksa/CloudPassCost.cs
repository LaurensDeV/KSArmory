using KSA;

namespace KSArmory;

/// <summary>
/// What the shader pass costs the GPU, read out of KSA's own profiler.
///
/// <para>A compute dispatch is asynchronous, so timing it from C# measures the recording rather
/// than the work. The engine already writes timestamps around every region tagged with a
/// <c>ProfilerTag</c> and keeps them per frame, so the number exists — this reads it back instead
/// of asking somebody to open a window and look.</para>
///
/// <para><b>The whole frame is sampled beside it</b>, because the pass's own milliseconds are not
/// the question. What decides whether the raymarch can replace the pens is what it does to the
/// frame, and a pass that costs 2 ms on a 30 ms frame is a different answer from the same 2 ms on
/// a 8 ms one.</para>
/// </summary>
internal static class CloudPassCost
{
    // What CloudPass tags its dispatch with. Matched by name because that is what the profiler
    // stores; a tag object of this mod's own is not what the engine hands back.
    private const string TagName = "KSArmory Cloud";

    private static int _frames;
    private static double _passTotalMs;
    private static double _passPeakMs;
    private static double _frameTotalMs;
    private static int _lastFrameIndex = -1;
    private static bool _warned;

    /// <summary>Whether anything has been measured yet.</summary>
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
        _passTotalMs = 0.0;
        _passPeakMs = 0.0;
        _frameTotalMs = 0.0;
        _lastFrameIndex = -1;
    }

    /// <summary>
    /// Reads the newest complete frame, once. Called every frame and cheap: it takes the last
    /// COMPLETE index, so a frame still being written is never half-read, and the index is
    /// remembered so the same frame is not counted twice when the render outruns the simulation.
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

            foreach (Sample sample in frame.AsSpan())
            {
                double ms = ProfilerWindowBase.TicksToMs(sample.Ticks);

                // Root samples only for the frame's own total: a nested region is already inside
                // its parent, so summing everything counts the same microsecond several times.
                if (sample.ParentIndex == ushort.MaxValue) whole += ms;

                if (sample.Id.ToString() == TagName) pass += ms;
            }

            if (pass <= 0.0) return;

            _frames++;
            _passTotalMs += pass;
            _frameTotalMs += whole;
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

        double mean = _passTotalMs / _frames;
        double frame = _frameTotalMs / _frames;
        double share = frame > 0.0 ? mean / frame : 0.0;

        return $"shader pass: {mean:F2} ms a frame, peak {_passPeakMs:F2}, "
               + $"of a {frame:F2} ms GPU frame ({share:P0}), over {_frames} frames";
    }

    private static void Warn(string what)
    {
        if (_warned) return;

        _warned = true;
        Log.Warn($"cloud pass cost: {what}");
    }
}
