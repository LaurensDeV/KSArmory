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

        try
        {
            switch (SimClock.Classify(KsaWorld.SimStepSeconds, KsaWorld.IsPaused, out double dt))
            {
                case SimClock.State.Run:
                    _battery.Update(dt);
                    break;

                case SimClock.State.Skipped:
                    // More simulated time passed than can be integrated, or the clock was
                    // replaced. Anything in flight is meaningless now.
                    _battery.AbandonFlight("simulation time jumped");
                    break;

                case SimClock.State.Idle:
                    // Counted, not ignored. If KSA ever renders frames that advance no
                    // simulated time, that is invisible from inside the game and changes how
                    // everything here behaves — so the panel reports it rather than leaving it
                    // to be guessed at. Paused frames are excluded; those are meant to be idle.
                    if (!KsaWorld.IsPaused) _battery.FramesWithoutSimStep++;
                    break;
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
