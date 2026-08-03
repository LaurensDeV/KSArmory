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

        try
        {
            // Every frame, before the clock gate. This reads where the world is, and the whole
            // overlay is drawn against it — leaving it inside the gated step froze the drawing's
            // frame of reference whenever the simulation did not advance.
            _battery.SampleWorld();

            // Step on the PLAYER frame delta, as the original did.
            //
            // Not KSA's simulation step. The drawn offset advances the platform across one
            // interval to meet the round, and that interval has to be the one the platform
            // sample actually moved over - which is the frame. Stepping on the simulation delta
            // while sampling per frame makes the two disagree by a fraction of a millisecond,
            // and at 29.8 km/s that is tens of metres of jitter, every frame. Confirmed by
            // bisect: the build before this changed draws dead centre.
            //
            // The pause guard is kept, because that half of the simulation-clock work was right
            // and is cheap: no simulated time, no step, so nothing fires into a frozen world.
            // Timewarp scaling is knowingly given up for now - it is the lesser of the two.
            if (!KsaWorld.IsPaused && double.IsFinite(dtPlayer) && dtPlayer > 0.0)
            {
                // Simulated seconds elapsed over THIS frame: the wall-clock delta scaled by the
                // warp factor.
                //
                // The interval matters more than the number. The drawn offset advances the
                // platform across the stepping interval to meet the round, so that interval must
                // be the one the platform sample actually moved over - which is this frame.
                // dtPlayer alone is that interval but ignores warp, so rounds crawled while the
                // world raced. The engine's own applied step accounts for warp but spans a
                // different interval, and that mismatch times ~29.8 km/s of ecliptic motion is
                // the jitter this cost an evening.
                //
                // dtPlayer * SimulationSpeed is both at once: this frame's interval, expressed
                // in simulated seconds. Identical to the confirmed-good build at 1x.
                double dtSim = dtPlayer * KsaWorld.SimulationSpeed;
                _battery.Update(Math.Min(dtSim, Interceptor.MaxFaithfulStep));
            }

            // Outside the clock gate on purpose. Placing the round bodies is drawing, not
            // simulating, and it has to happen on every rendered frame or the rounds sit still
            // through any frame that advanced no simulated time while the world moved past
            // them. Cheap, and it only reads state.
            _battery.SyncRoundBodies();
        }
        catch (Exception e)
        {
            Fault("frame update", e);
        }
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
            _ui.Draw();

            if (KsaWorld.InFlight) Visuals.Draw(_battery, _config);
        }
        catch (Exception e)
        {
            Fault("gui", e);
        }
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
