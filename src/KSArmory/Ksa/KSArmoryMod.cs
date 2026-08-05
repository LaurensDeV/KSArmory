using Brutal.Numerics;
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

    // One battery today, so one policy. When ResolvePlatform stops electing a single launcher
    // this becomes one per battery and Config stays shared.
    private readonly BatteryConfig _policy = new();
    private DefenceBattery? _battery;
    private Ui? _ui;
    private int _faults;
    private int _viewTrace;

    // Overrun bookkeeping. See ReportOverrun.
    private const int OverrunReportEvery = 120;
    private int _overrunFrames;
    private double _overrunDiscarded;
    private bool _disabled;
    private readonly WarpPolicy _warp = new();

    // Last kitten reported, so the character is logged once per EVA rather than every frame.
    private string _lastKittenSeen = string.Empty;

    [StarMapImmediateLoad]
    public void OnImmediateLoad(Mod mod)
    {
        Log.Info($"loading (mod id: {mod.Id})");
    }

    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        _battery = new DefenceBattery(_config, _policy);
        _ui = new Ui(_config, _policy, _battery, _warp);
        Log.Info($"ready - {_config.Launcher.DisplayName}, {_config.Launcher.TubeCount} tubes, safe. "
                 + "Open the 'KSArmory' panel to arm.");

        // Logged, not just shown in the panel. Every link of this chain fails silently inside
        // KSA, so without a record the only symptom is a kitten with no gun -- and that looks
        // identical whether the XML never loaded, a reference did not resolve, or the mesh did.
        List<(string What, string Id, bool Resolved)> chain = [];
        KsaWorld.CollectArmedChain(chain);
        foreach ((string what, string id, bool resolved) in chain)
        {
            Log.Info($"armed kitten {what}: {id} {(resolved ? "ok" : "DID NOT RESOLVE")}");
        }
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

            // Last, and every frame. KSA's controller writes the camera from its own mode, so a
            // view taken earlier in the frame is simply overwritten before anything renders.
            if (KsaWorld.InFlight && _policy.OpticViewport >= 0)
            {
                TakeOpticView(dt);
                Sight.Draw(_battery, _config, _policy);
            }
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

        // Reported off the *controlled* vehicle, not the battery's platform: whether a gun
        // renders has nothing to do with whether the battery mounted, and gating it on that hid
        // the answer behind an unrelated tick box.
        ReportControlledKitten();

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
            if (SimClock.Classify(dtSim, KsaWorld.IsPaused, out _) == SimClock.State.Skipped)
            {
                ReportOverrun(dtSim);
            }

            ApplyWarpPolicy(dtSim);

            // Still clamped, and it still discards time: the frame that overran cannot be
            // un-run, and the policy above only takes effect from the next one. What it stops
            // is the *next* thousand frames doing the same thing silently.
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
        // Give the speed back before letting go of it, or unloading mid-salvo leaves the world
        // stuck at whatever the policy had wound it down to, with nothing left to wind it back.
        if (_warp.Holding && KsaWorld.SetSimulationSpeed(_warp.HeldSpeed))
        {
            Log.Info($"timewarp restored to {_warp.HeldSpeed:F0}x - unloading");
        }
        _warp.Clear();

        _battery?.Reset();
        KsaWorld.ResetSimStepTracking();
        _battery = null;
        _ui = null;
        Log.Info("unloaded");
    }

    // Puts the view on the launcher's optical head. Returns quietly when the launcher has none or
    // the head cannot be resolved: the toggle is allowed to be on for a craft that cannot honour
    // it, and stealing the camera to nowhere would be worse than ignoring it.
    private void TakeOpticView(double dt)
    {
        if (_battery?.Platform is not { } platform || _battery.Launcher is not { } launcher) return;
        if (_battery.OpticPart is null) return;

        _viewTrace += 1;
        bool trace = _viewTrace % 60 == 0;

        if (!LauncherPart.TryGetOpticViewEcl(platform, launcher, _config.Launcher,
                                             _battery.Turret.BearingRad,
                                             _battery.OpticDirectionPartFrame,
                                             _battery.PlatformEcl,
                                             out double3 eye, out double3 forward))
        {
            if (trace) Log.Debug(() => "camera: could not resolve the optical head's eye");
            return;
        }

        // Local "up" at the launcher, which is what the boresight already is — so the horizon
        // sits level rather than rolling with the ecliptic.
        bool took = KsaWorld.TryLookFromViewport(_policy.OpticViewport, eye, forward,
                                                 _battery.Boresight, dt);
        if (trace)
        {
            Log.Debug(() => $"camera: view {_policy.OpticViewport} of {KsaWorld.ViewportCount} "
                            + $"took={took} eye={eye.X:F0},{eye.Y:F0},{eye.Z:F0} "
                            + $"fwd={forward.X:F3},{forward.Y:F3},{forward.Z:F3}");
        }

        if (!took)
        {
            _policy.OpticViewport = -1;
            Log.Warn("camera: could not drive that view; released it");
        }
    }

    // Says how much simulated time the clamp threw away, and how often. Rate-limited because
    // sustained warp overruns every frame, and a line per frame buries the first one - which is
    // the only one that says when it started.
    private void ReportOverrun(double stepSeconds)
    {
        _overrunFrames++;
        _overrunDiscarded += stepSeconds - Interceptor.MaxFaithfulStep;

        if (_overrunFrames != 1 && _overrunFrames % OverrunReportEvery != 0) return;

        Log.Warn($"step {stepSeconds * 1000.0:F0} ms exceeds the {Interceptor.MaxFaithfulStep * 1000.0:F0} ms "
                 + $"a round can integrate faithfully; clamped and carried on. "
                 + $"{_overrunFrames} frame(s), {_overrunDiscarded:F2} s of simulated time discarded. "
                 + $"Rounds in flight will lag the world.");
    }

    // Keeps the world slow enough to simulate what is in the air, and gives the speed back when
    // it lands. WarpPolicy holds the reasoning and all of the arithmetic.
    private void ApplyWarpPolicy(double dtSim)
    {
        if (_battery is null) return;

        WarpDecision d = _warp.Decide(dtSim, KsaWorld.SimulationSpeed,
                                      _battery.Rounds.Count > 0, _config.LimitWarpInFlight);

        switch (d.Action)
        {
            case WarpAction.Slow:
            case WarpAction.Restore:
                // A refused write is not an error here: the policy waits for the value it asked
                // for to appear and abandons on its own if it never does.
                if (KsaWorld.SetSimulationSpeed(d.Speed))
                {
                    Log.Info(d.Action == WarpAction.Slow
                                 ? $"timewarp held at {d.Speed:F1}x - {d.Why}"
                                 : $"timewarp restored to {d.Speed:F0}x - {d.Why}");
                }
                break;

            case WarpAction.Yield:
                Log.Warn($"timewarp not held - {d.Why}");
                break;

            case WarpAction.Abandon:
                _battery.AbandonFlight(d.Why);
                break;

            case WarpAction.None:
            default:
                break;
        }
    }

    // Says what character the kitten being flown was built with. That is the one fact that
    // separates a gun that will not render from a kitten armed after it was already walking.
    private void ReportControlledKitten()
    {
        Vehicle? controlled = KsaWorld.ControlledVehicle;
        string id = $"{KsaWorld.DisplayName(controlled)}|{KsaWorld.CharacterOf(controlled) ?? ""}";
        if (id == _lastKittenSeen) return;

        _lastKittenSeen = id;
        if (KsaWorld.CharacterOf(controlled) is { } character)
        {
            Log.Info($"flying kitten {KsaWorld.DisplayName(controlled)} wearing '{character}'"
                     + (character == KsaWorld.ArmedCharacterId ? " - armed" : " - NOT armed"));
        }
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
