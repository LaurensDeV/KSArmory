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

    /// <summary>Frames longer than this are treated as a hitch and stepped at this length.</summary>
    private const double MaxStep = 0.1;

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
    /// Simulation tick. StarMap passes the player-time clock and the frame delta; we use the
    /// delta and clamp it so a stall cannot teleport rounds through their targets.
    /// </summary>
    [StarMapAfterOnFrame]
    public void OnAfterFrame(double currentPlayerTime, double dtPlayer)
    {
        if (_disabled || _battery is null) return;
        if (!double.IsFinite(dtPlayer) || dtPlayer <= 0.0) return;
        if (!KsaWorld.InFlight) return;

        try
        {
            _battery.Update(Math.Min(dtPlayer, MaxStep));
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
