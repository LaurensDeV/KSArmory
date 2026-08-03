using KSA;
using StarMap.API;

namespace AirDefence;

/// <summary>
/// StarMap entry point. StarMap loads the assembly named by mod.toml's EntryAssembly and
/// instantiates the first type carrying <see cref="StarMapModAttribute"/>, then dispatches
/// to the attributed methods below.
///
/// Frame work is wrapped so a fault degrades the mod instead of taking the game down, and
/// repeated faults disable it rather than filling the log.
/// </summary>
[StarMapMod]
public sealed class AirDefenceMod
{
    private const int FaultLimit = 10;

    private double _lastSimSpeed = 1.0;
    private readonly Config _config = new();
    private DefenceBattery? _battery;
    private Ui? _ui;
    private int _faults;
    private bool _disabled;

    [StarMapImmediateLoad]
    public void OnImmediateLoad(Mod mod)
    {
        Log.Info($"loading (mod id: {mod.Id})");
    }

    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        _battery = new DefenceBattery(_config);
        _ui = new Ui(_config, _battery);
        Log.Info($"ready - {Arsenal.PantsirS1.TubeCount} tubes, safe. Open the 'Air Defence' panel to arm.");
    }

    /// <summary>
    /// Simulation tick.
    ///
    /// <para>StarMap passes a <em>player-time</em> clock and delta, and those are deliberately
    /// ignored. Player time is wall-clock: it runs through a pause, so the battery used to
    /// mature a lock and fire into a frozen world, and it ignores timewarp, so under warp the
    /// world moved many seconds while rounds moved one frame. Both were seen in game. The
    /// simulation clock is the one that matches what the world did.</para>
    /// </summary>
    [StarMapAfterOnFrame]
    public void OnAfterFrame(double currentPlayerTime, double dtPlayer)
    {
        if (_disabled || _battery is null) return;
        if (!KsaWorld.InFlight) return;

        // Sim speed and pause state change what everything else in the log means, and they
        // change because someone moved a slider - so record them rather than inferring them
        // later from frozen timestamps, which is a mistake already made once.
        double speed = KsaWorld.SimulationSpeed;
        if (Math.Abs(speed - _lastSimSpeed) > 1e-9)
        {
            Log.Info($"simulation speed {_lastSimSpeed:F2}x -> {speed:F2}x"
                     + (KsaWorld.IsPaused ? " (paused)" : ""));
            _lastSimSpeed = speed;
        }

        // Nothing here: the simulation runs in OnAfterGui, alongside the drawing it feeds.
    }

    /// <summary>
    /// Panel and world overlay.
    ///
    /// The gizmo drawing has to happen here, not in the frame hook. KSA's whole frame runs
    /// inside OnFrame: it calls GizmosRenderer.ResetInstances() near the top, draws the UI,
    /// then renders. A postfix on OnFrame therefore lands *after* the render, so anything it
    /// submits is cleared by the next frame's reset before it is ever drawn. This hook is a
    /// postfix on OnDrawUiViewports, which sits between the reset and the render.
    /// </summary>
    [StarMapAfterGui]
    public void OnAfterGui(double dt)
    {
        if (_disabled || _ui is null || _battery is null) return;

        try
        {
            // Simulate here, immediately before drawing, rather than in the frame hook.
            //
            // KSA's order within a frame is: reset gizmos -> draw UI (this hook) -> render ->
            // postfix on OnFrame. So a simulation step in the frame hook lands AFTER this pass,
            // and every draw necessarily used an offset produced one frame earlier while
            // anchoring it to the platform's position now. A round is drawn as
            // `AnchorEgo + OffsetFromPlatform`, so that one-frame gap put it exactly one step of
            // the platform's ecliptic motion downrange - measured at 0.999 steps along the
            // orbital direction with 0.4 m across it, on all 221 samples taken. About 600 m at
            // 1x, and the same shift at launch as at the intercept, because a rigid drag moves
            // the whole flight equally.
            //
            // Correcting it at draw time cannot work: the drag is the platform's motion over one
            // step, so any correction carries a dt that changes frame to frame, and it comes
            // straight back as the `v * dstep` jitter fixed in Interceptor.Update. Tried, and it
            // reintroduced exactly that.
            //
            // Stepping here removes the gap instead of compensating for it. The offset and the
            // anchor are then produced in the same pass, so they share an epoch by construction
            // and there is no dt anywhere in the placement.
            if (KsaWorld.InFlight) StepSimulation(dt);

            _ui.Draw();

            if (KsaWorld.InFlight) Visuals.Draw(_battery, _config);
        }
        catch (Exception e)
        {
            Fault("gui", e);
        }
    }

    /// <summary>
    /// One simulation step, run from the GUI hook so it shares an epoch with the draw.
    /// </summary>
    private void StepSimulation(double dtPlayer)
    {
        if (_battery is null) return;

        // Every frame, before the clock gate. This reads where the world is, and the whole
        // overlay is drawn against it — leaving it inside the gated step froze the drawing's
        // frame of reference whenever the simulation did not advance.
        _battery.SampleWorld();

        // The pause guard: no simulated time, no step, so nothing fires into a frozen world.
        if (!KsaWorld.IsPaused && double.IsFinite(dtPlayer) && dtPlayer > 0.0)
        {
            // Simulated seconds elapsed over THIS frame, READ from the engine rather than
            // estimated from the frame time.
            //
            // The drawn offset advances the platform across the stepping interval to meet the
            // round, so that interval has to be the one the platform sample actually moved
            // over. dtPlayer alone ignores warp, so rounds crawled while the world raced.
            // dtPlayer * SimulationSpeed corrects for warp but is still a guess at what the
            // engine did, and a probe measured the error directly: the assumed step
            // missed the real one by up to 0.9 ms, which against ~29.8 km/s of ecliptic
            // motion is 27 m of misplacement, alternating sign frame to frame. Worst at 0.1x
            // and 2x, and worst of all on the frame the speed changes - where the engine
            // applies one step at the old rate while the estimate has already switched to the
            // new one. That is the jump.
            //
            // GetLastSimStep().DeltaTime is not an approximation of that interval, it is that
            // interval: measured against the platform's own displacement over its own
            // velocity - two independent readings off the same vehicle - it agreed to four
            // decimal places on every frame sampled, at every speed from 0.01x to 4x.
            //
            // An earlier attempt at this was reverted for causing jitter. That jitter was the
            // drawn offset's own phase error, fixed separately in Interceptor.Update - see
            // the offset note in CLAUDE.md - and it was never about the step at all.
            double dtSim = KsaWorld.SimStepSeconds;

            // Fall back to the estimate only if the engine has nothing to report - a load,
            // or a frame before the first step. Better a slightly wrong step than none.
            if (!double.IsFinite(dtSim) || dtSim <= 0.0)
                dtSim = dtPlayer * KsaWorld.SimulationSpeed;
            _battery.Update(Math.Min(dtSim, Interceptor.MaxFaithfulStep));
        }

        // Outside the clock gate on purpose. Placing the round bodies is drawing, not
        // simulating, and it has to happen on every rendered frame or the rounds sit still
        // through any frame that advanced no simulated time while the world moved past
        // them. Cheap, and it only reads state.
        _battery.SyncRoundBodies();
}

    [StarMapUnload]
    public void Unload()
    {
        _battery?.Reset();
        _battery = null;
        _ui = null;
        Log.Info("unloaded");
    }

    private void Fault(string where, Exception e)
    {
        _faults++;
        Log.Error($"{where} failed ({_faults}/{FaultLimit})", e);

        if (_faults < FaultLimit) return;

        _disabled = true;
        _battery?.Reset();
        Log.Error("too many faults - air defence disabled for this session");
    }
}
