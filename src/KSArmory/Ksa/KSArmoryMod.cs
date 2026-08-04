using KSA;
using StarMap.API;

namespace KSArmory;

/// <summary>
/// StarMap entry point. StarMap loads the assembly named by mod.toml's EntryAssembly and
/// instantiates the first type carrying <see cref="StarMapModAttribute"/>, then dispatches
/// to the attributed methods below.
///
/// Frame work is wrapped so a fault degrades the mod instead of taking the game down, and
/// repeated faults disable it rather than filling the log.
/// </summary>
[StarMapMod]
public sealed class KSArmoryMod
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
    /// ignored. Player time is wall-clock: it runs through a pause, so a battery on it matures a
    /// lock and fires into a frozen world, and it ignores timewarp, so the world outruns the
    /// rounds. The simulation clock is the one that matches what the world did.</para>
    /// </summary>
    [StarMapAfterOnFrame]
    public void OnAfterFrame(double currentPlayerTime, double dtPlayer)
    {
        if (_disabled || _battery is null) return;
        if (!KsaWorld.InFlight) return;

        // Sim speed and pause state change what everything else in the log means, so record them
        // rather than inferring them later from frozen timestamps.
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
            // Simulate here, not in the frame hook. KSA's order is reset gizmos -> draw UI (this
            // hook) -> render -> postfix on OnFrame, so a step in the frame hook lands after this
            // pass and every draw would anchor a one-frame-old offset to the platform's position
            // now - about 600 m of ecliptic motion at 1x.
            //
            // Compensating at draw time cannot work: the drag is one step of platform motion, so
            // any correction carries a dt that changes and returns as jitter. Stepping here makes
            // the offset and the anchor share an epoch by construction.
            if (KsaWorld.InFlight) StepSimulation(dt);

            _ui.Draw();

            if (KsaWorld.InFlight) Visuals.Draw(_battery, _config);
        }
        catch (Exception e)
        {
            Fault("gui", e);
        }
    }

    // One simulation step, run from the GUI hook so it shares an epoch with the draw.
    private void StepSimulation(double dtPlayer)
    {
        if (_battery is null) return;

        // Every frame, before the clock gate. This reads where the world is, and the whole
        // overlay is drawn against it — leaving it inside the gated step froze the drawing's
        // frame of reference whenever the simulation did not advance.
        _battery.SampleWorld();

        // Gate on the step the engine applied, not on the pause flag. Universe.IsPaused() is
        // `simulationSpeed == 0.0`, a statement about the setting rather than about whether the
        // world moved: on the frame speed drops to zero the engine still applies one real step, so
        // the platform sample advances while a flag-gated round does not. The drawn offset is a
        // difference of integrated positions, so that step stays in permanently and every pause
        // adds another.
        {
            // Read from the engine, not estimated from the frame time. The drawn offset advances
            // the platform across the stepping interval to meet the round, so that interval must
            // be the one the sample actually moved over. dtPlayer * SimulationSpeed is a guess
            // that misses by up to 0.9 ms - 27 m against 29.8 km/s - and misses worst on the frame
            // the speed changes.
            //
            // Consumed, not peeked: the engine answers with the last step, so asking twice without
            // it having stepped returns the same one. See KsaWorld.ConsumeSimStep.
            double dtSim = KsaWorld.ConsumeSimStep();

            // No step reported, no step taken - never substitute an estimate. The engine reports
            // nothing exactly when it advanced nothing, so an estimate would integrate the round
            // across an interval the world did not move over, and the whole of that lands in the
            // drawn offset. Skipping costs one frame of round motion and nothing accumulates.
            if (double.IsFinite(dtSim) && dtSim > 0.0)
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
        KsaWorld.ResetSimStepTracking();
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
