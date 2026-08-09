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

    // One battery per weapons system, each with its own policy; Config stays shared.
    private WeaponSystems? _roster;
    private Ui? _ui;
    private int _faults;
    private int _viewTrace;

    // Overrun bookkeeping. See ReportOverrun.
    private const int OverrunReportEvery = 120;
    private int _overrunFrames;
    private double _overrunDiscarded;
    private bool _disabled;
    private readonly WarpPolicy _warp = new();

    // Holds the main view on one system without handing it the controls.
    private readonly WatchCamera _watch = new();
    private readonly ChaseCamera _chase = new();
    private readonly SightCamera _sight = new();

    // The simulated step this frame, stashed for the cameras. Zero while paused, and scaled by
    // timewarp and by the panel's slow-motion buttons -- which is exactly what a camera move that
    // is supposed to track a round has to run on. Player time keeps ticking through a pause.
    private double _lastSimStep;

    // One per system, made on demand and forgotten with the craft. A dictionary rather than a
    // field on WeaponSystem because a sight is drawing, and WeaponSystem is deliberately free of
    // anything that only exists to be looked at.
    private readonly Dictionary<WeaponSystem, BombSightOverlay> _sights = [];

    private BombSightOverlay SightFor(WeaponSystem battery)
    {
        if (_sights.TryGetValue(battery, out BombSightOverlay? sight)) return sight;

        sight = new BombSightOverlay();
        _sights[battery] = sight;
        return sight;
    }

    // Every round in the world, as things a sensor can hold. Rebuilt each simulated step.
    private readonly List<IContact> _airborne = [];

    // Development tool: pick a craft up and set it down somewhere else.
    private readonly CraftMover _mover = new();

    // Development tool: click the world to set off a warhead there.
    private readonly BurstTool _bursts = new();
    private readonly Designator _designator = new();
    private MotorSound _motors = null!;
    private readonly MotorPlume _plumes = new();
    private readonly MuzzleFlash _flashes = new();
    private readonly TracerTrail _tracers = new();
    private GunSound _gunSound = null!;
    private ScenarioRunner _scenario = null!;

    // Last kitten reported, so the character is logged once per EVA rather than every frame.
    private string _lastKittenSeen = string.Empty;

    [StarMapImmediateLoad]
    public void OnImmediateLoad(Mod mod)
    {
        Log.Info($"loading (mod id: {mod.Id})");

        // Which KSA this was built for against which it is running, and therefore whether the
        // panel offers reporting. Without it, buttons missing from the panel is a mystery from
        // the outside and unanswerable from a log.
        bool supported = Sim.ReportDraft.GameIsSupported(Build.KsaBuild, Build.KsaRunning);
        Log.Info($"KSArmory {Build.Version} built for KSA {Build.KsaBuild ?? "?"}, "
                 + $"running {Build.KsaRunning ?? "?"} - reporting {(supported ? "on" : "off")}");
    }

    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        _roster = new WeaponSystems(_config);
        _motors = new MotorSound(_config);
        _gunSound = new GunSound(_config);
        _scenario = new ScenarioRunner(_config);
        _scenario.Begin(ScenarioRunner.Requested());
        _ui = new Ui(_config, _roster, _warp, _watch, _mover, _bursts);
        Log.Info($"ready - {string.Join(", ", Arsenal.Launchers.Select(l => l.DisplayName))}, safe. "
                 + "Open the 'KSArmory' panel to arm.");

        // Logged, not just shown in the panel. Every link of this chain fails silently inside
        // KSA, so without a record the only symptom is a kitten with no gun -- and that looks
        // identical whether the XML never loaded, a reference did not resolve, or the mesh did.
        Log.Info($"particles graphics setting: {(Detonation.ParticlesEnabled ? "on" : "OFF")}, "
                 + $"screen-space particles: {(Detonation.SoftParticles ? "on" : "off")}");
        Log.Info($"warhead effect {Detonation.Fireball}: "
                 + $"{(Detonation.Resolves(Detonation.Fireball) ? "ok" : "DID NOT RESOLVE")}");
        Log.Info($"warhead effect {Detonation.Airburst}: "
                 + $"{(Detonation.Resolves(Detonation.Airburst) ? "ok" : "DID NOT RESOLVE")}");

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
        if (_disabled || _roster is null) return;
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
    /// Opens the main menu bar before KSA fills it, so "Mods" sits alongside File and Universe.
    /// Nothing else belongs here: the overlay and the panel need the world stepped first.
    /// </summary>
    [StarMapBeforeGui]
    public void OnBeforeGui(double dt)
    {
        if (_disabled || _ui is null) return;

        try { _ui.DrawMenuBarEntry(); }
        catch { /* Cosmetic. Never take KSA's GUI pass down for a menu item. */ }
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
        if (_disabled || _ui is null || _roster is null) return;

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
            KsaWorld.BeginFrame();
            if (KsaWorld.InFlight) StepSimulation(dt);

            _ui.Draw();

            // Outside the overlay switch on purpose: a shell has no subpart body, so this is the
            // round itself rather than an annotation of it, and behind a debug switch a firing
            // cannon puts almost nothing on screen.
            if (KsaWorld.InFlight)
            {
                foreach (WeaponSystems.Entry e in _roster.All) Visuals.DrawShellStream(e.Battery);
            }

            // Outside the debug overlay switch, and deliberately: this is a sight the operator
            // aims with, not an annotation of how the mod is thinking. Same reasoning as the
            // shell stream above.
            foreach (WeaponSystems.Entry e in _roster.All)
            {
                if (e.Policy.DrawBombSight) SightFor(e.Battery).Draw(e.Battery);
            }

            if (KsaWorld.InFlight && _config.DrawOverlays)
            {
                if (_config.DrawOverlayForFocusedOnly)
                {
                    if (_roster.For(_ui.Focused) is { } shown) Visuals.Draw(shown.Battery, _config);
                }
                else
                {
                    foreach (WeaponSystems.Entry e in _roster.All) Visuals.Draw(e.Battery, _config);
                }
            }

            // Over the world, under the panel: ImGui draws windows in submission order, and the
            // panel is submitted first, so a full-screen overlay added here sits above the scene
            // and below anything the operator is reading.
            if (KsaWorld.InFlight && _config.DrawSystemMarkers)
                Markers.Draw(_ui.Systems, _ui.Focused, dt);

            // Both of these write a camera, and both must be last and every frame: KSA's
            // controller writes from its own mode, so a view taken earlier in the frame is
            // overwritten before anything renders.
            _watch.Apply(dt);

            // After the watch camera: both write the view, and the chase takes it outright, so
            // letting the watch nudge afterwards would fight it every frame.
            if (_roster.For(_ui.Focused) is { } chased)
            {
                _chase.Apply(chased.Battery, chased.Policy.ChaseRounds && KsaWorld.InFlight,
                             dt, _lastSimStep, _config.FreezeChaseTransition);
            }
            else
            {
                _chase.Release();
            }

            // After the camera has been placed, so the brackets are projected through this frame's
            // view rather than the one before it.
            if (KsaWorld.InFlight) ChaseHud.Draw(_chase);

            // After the panel, so a click on a window is not also a click on the world behind it.
            if (KsaWorld.InFlight)
            {
                _mover.Update(_config);
                _mover.Draw(_config);
                _bursts.Update(_config);
                _bursts.Draw(_config);

                // Only the system the panel is showing. Every crewed battery reading the same
                // cursor would fire every launcher in the world at one click.
                if (_roster.For(_ui.Focused) is { } aimed)
                {
                    _designator.Update(aimed.Battery, aimed.Policy);
                    _designator.Draw(aimed.Battery, aimed.Policy);
                }
            }
            // Last, and every frame. KSA's controller writes the camera from its own mode, so a
            // view taken earlier in the frame is simply overwritten before anything renders.
            if (KsaWorld.InFlight && _roster.For(_ui.Focused) is { } focused)
            {
                TakeOpticView(focused.Battery, focused.Policy, dt);

                // Asked of the claim, not of the setting: the sight yields the main view to the
                // chase without releasing it, and painting through that leaves its bracket over a
                // picture of something else, stacked under the chase's own.
                if (ViewClaim.SightPaints(focused.Policy.OpticViewport >= 0,
                                          focused.Policy.OpticViewport == KsaWorld.MainViewportIndex,
                                          _sight.Holding, _chase.HoldsMainView))
                {
                    Sight.Draw(focused.Battery, focused.Policy);
                }
            }
            else if (KsaWorld.InFlight)
            {
                // Nothing is being shown, so nothing may be holding the player's view on its
                // behalf. Skipping this is how a sight survives the craft it was looking through.
                _sight.Release();
            }
            else
            {
                // Out of flight the recording describes a scene that no longer exists, and
                // restoring a dead scene's camera mode and follow onto the editor is a view the
                // player cannot account for. The new scene brings its own camera.
                _sight.Forget();
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
        if (_roster is null) return;

        // Every frame, before the clock gate. This reads where the world is and the whole
        // overlay is drawn against it, so inside the gated step the drawing's frame of
        // reference freezes on every frame that advances no simulated time.
        foreach (WeaponSystems.Entry e in _roster.All) e.Battery.SampleWorld();

        // Reported off the *controlled* vehicle, not the battery's platform: whether a gun
        // renders has nothing to do with whether the battery mounted, so gating it on that
        // would hide the answer behind an unrelated condition.
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

            // Kept for the cameras, which run later in this same hook. The step is consumed
            // exactly once per frame, so they cannot ask for it themselves.
            _lastSimStep = double.IsFinite(dtSim) && dtSim > 0.0 ? dtSim : 0.0;

            // No step reported, no step taken - never substitute an estimate. The engine reports
            // nothing exactly when it advanced nothing, so an estimate would integrate the round
            // across an interval the world did not move over, and the whole of that lands in the
            // drawn offset. Skipping costs one frame of round motion and nothing accumulates.
            if (SimClock.Classify(dtSim, KsaWorld.IsPaused, out _) == SimClock.State.Skipped)
            {
                ReportOverrun(dtSim);
            }

            ApplyWarpPolicy(dtSim);

            // Clamped, and the clamp discards time: the frame that overran cannot be un-run,
            // and the policy above only takes effect from the next one. What it stops is the
            // *next* thousand frames doing the same thing silently.
            if (double.IsFinite(dtSim) && dtSim > 0.0)
            {
                double step = Math.Min(dtSim, FaithfulStepInFlight());

                // Gathered once, not once per system: every crewed system scans the same sky, and
                // building this per system would be quadratic in how many are in the world.
                CollectAirborne();

                foreach (WeaponSystems.Entry e in _roster.All) e.Battery.Update(step, _airborne);
            }
        }

        // Outside the clock gate on purpose. Placing the round bodies is drawing, not
        // simulating, and it has to happen on every rendered frame or the rounds sit still
        // through any frame that advanced no simulated time while the world moved past
        // them. Cheap, and it only reads state.
        foreach (WeaponSystems.Entry e in _roster.All) e.Battery.SyncRoundBodies();

        // After the rounds have been stepped, so a motor is heard where its round now is
        // rather than where it was at the start of the frame.
        foreach (WeaponSystems.Entry e in _roster.All)
        {
            _motors.Update(e.Battery);
            _plumes.Update(e.Battery);
            _flashes.Update(e.Battery);
            _tracers.Update(e.Battery);
            _gunSound.Update(e.Battery);

            // Its own switch and its own solve, per system: it costs a few hundred integration
            // steps and two aircraft can sensibly disagree about wanting one.
            if (e.Policy.DrawBombSight) SightFor(e.Battery).Update(e.Battery, _lastSimStep);
            else SightFor(e.Battery).Clear();
        }

        // A sight outlives nothing: without this the dictionary keeps a system for the session
        // after its craft has gone, which is the leak every pooled effect below sweeps for.
        if (_sights.Count > _roster.Count)
        {
            _sights.Clear();
            foreach (WeaponSystems.Entry e in _roster.All) SightFor(e.Battery);
        }

        // Every effect that holds a pooled emitter or a channel, so a craft destroyed mid-salvo
        // does not keep one for the session.
        _motors.Sweep(_roster);
        _plumes.Sweep(_roster);
        _tracers.Sweep(_roster);
        _flashes.Sweep(_roster);
        _gunSound.Sweep(_roster);

        // After the batteries have run, so a scenario reads the state this frame produced rather
        // than the one before it.
        _scenario.Update(_roster, dtPlayer);

        // The panel has no change notification, so settings are written by comparing against what
        // is already stored. Every frame rather than on a timer: a save and a load both fit inside
        // half a second, and a load in that window loses the settings twice over, once because
        // they were never written for that save and again when a later check writes the
        // freshly-defaulted ones over the file. It is a file timestamp, not work.
        _roster.Remember();
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
        _watch.Release();
        _chase.Release();
        _sight.Release();

        // After the cameras have let go, so nothing is mid-write when the controller is swapped
        // back. The mod's own controller would otherwise outlive it for the rest of the session.
        KsaWorld.RestoreStockController();
        _mover.Release();

        // Pooled emitters and audio channels belong to the game and nothing else gives them
        // back: unloading while anything is burning or firing would keep them for the process.
        _motors?.StopAll();
        _gunSound?.StopAll();
        _plumes?.ReleaseAll();
        _tracers?.ReleaseAll();
        _flashes?.ReleaseAll();

        // Markers pin the craft they are showing, so a destroyed one stays reachable otherwise.
        Markers.Forget();

        _roster?.Clear();
        KsaWorld.ResetSimStepTracking();
        _roster = null;
        _ui = null;
        Log.Info("unloaded");
    }

    // Puts the view on the launcher's optical head, on whichever window the player chose.
    //
    // The main view and a secondary one are driven by different mechanisms and cannot share one:
    // a secondary camera follows nothing, so it is positioned outright, while the main camera is
    // following the player's craft and KSA places it at following.GetPositionEcl() + CameraOffset
    // during its own pass. The main view is also the only one that draws a planet - see
    // docs/BLOCKED-ON-KSA.md - so it is the one worth having and the one that has to be given back.
    private void TakeOpticView(WeaponSystem battery, SystemConfig policy, double dt)
    {
        bool wantsMainView = policy.OpticViewport == KsaWorld.MainViewportIndex;

        // Asked every frame, including when the optic is off: that is what hands the view back
        // after the player switches it off, and what lets the sight resume once the chase is done.
        ViewAction did = _sight.Apply(battery, wantsMainView, outranked: _chase.HoldsMainView,
                                      policy.OpticMagnification);

        // Taking the view back by hand switches the optic off, rather than merely releasing it
        // once. The setting is what asks for the view, so leaving it on means the very next frame
        // takes it straight back -- which reads as the mod refusing to let go.
        if (did == ViewAction.StandDown)
        {
            policy.OpticViewport = -1;
            return;
        }

        if (wantsMainView || policy.OpticViewport < 0) return;

        if (battery.OpticPart is null) return;
        if (!battery.TryOpticViewEcl(out double3 eye, out double3 forward))
        {
            _viewTrace += 1;
            if (_viewTrace % 60 == 0) Log.Debug(() => "camera: could not resolve the optical head's eye");
            return;
        }

        _viewTrace += 1;
        bool trace = _viewTrace % 60 == 0;

        // Local "up" at the launcher, which is what the boresight already is — so the horizon
        // sits level rather than rolling with the ecliptic.
        bool took = KsaWorld.TryLookFromViewport(policy.OpticViewport, eye, forward,
                                                 battery.Boresight, dt);
        if (trace)
        {
            Log.Debug(() => $"camera: view {policy.OpticViewport} of {KsaWorld.ViewportCount} "
                            + $"took={took} eye={eye.X:F0},{eye.Y:F0},{eye.Z:F0} "
                            + $"fwd={forward.X:F3},{forward.Y:F3},{forward.Z:F3}");
        }

        if (!took)
        {
            policy.OpticViewport = -1;
            Log.Warn("camera: could not drive that view; released it");
        }
    }

    // Every round any crewed system has in the air, wrapped as contacts so a radar can see them.
    //
    // A round carries its shooter's craft name rather than its own, which is what makes it
    // inherit that side's allegiance: a launcher's own salvo reads as friendly to everything on
    // its team without anything having to know a round from a craft.
    private void CollectAirborne()
    {
        _airborne.Clear();
        if (_roster is null) return;

        foreach (WeaponSystems.Entry e in _roster.All)
        {
            WeaponSystem system = e.Battery;
            if (system.Platform is not { } platform) continue;

            string name = KsaWorld.DisplayName(platform);
            IReadOnlyList<IProjectile> rounds = system.Rounds;

            for (int i = 0; i < rounds.Count; i++)
            {
                if (rounds[i].State != RoundState.Flying) continue;

                _airborne.Add(new RoundContact(rounds[i], name, platform));
            }
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

    // The shortest step any round in the air needs, which is what the world is held down to and
    // what the integration is clamped at. Nothing flying means nothing to protect.
    private double FaithfulStepInFlight()
    {
        double faithful = double.MaxValue;

        if (_roster is not null)
        {
            foreach (WeaponSystems.Entry e in _roster.All)
            {
                foreach (IProjectile round in e.Battery.Rounds)
                {
                    faithful = Math.Min(faithful, round.Munition.MaxFaithfulStepSeconds);
                }
            }
        }

        return faithful is double.MaxValue ? Interceptor.MaxFaithfulStep : faithful;
    }

    // Keeps the world slow enough to simulate what is in the air, and gives the speed back when
    // it lands. WarpPolicy holds the reasoning and all of the arithmetic.
    private void ApplyWarpPolicy(double dtSim)
    {
        if (_roster is null) return;

        // Any round anywhere: the step has to be small enough for the busiest battery, not for
        // the one the panel happens to be showing. And small enough for the fussiest round in the
        // air, which is the one that manoeuvres hardest: a ballistic weapon alongside an
        // interceptor must not let the interceptor be stepped over.
        bool anyInFlight = false;
        double faithful = double.MaxValue;

        foreach (WeaponSystems.Entry e in _roster.All)
        {
            foreach (IProjectile round in e.Battery.Rounds)
            {
                anyInFlight = true;
                faithful = Math.Min(faithful, round.Munition.MaxFaithfulStepSeconds);
            }
        }

        if (!anyInFlight) faithful = Interceptor.MaxFaithfulStep;

        WarpDecision d = _warp.Decide(dtSim, KsaWorld.SimulationSpeed,
                                      anyInFlight, _config.LimitWarpInFlight, faithful);

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
                foreach (WeaponSystems.Entry e in _roster.All) e.Battery.AbandonFlight(d.Why);
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
        if (controlled is null) { _lastKittenSeen = string.Empty; return; }

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
        _roster?.Clear();
        Log.Error("too many faults - air defence disabled for this session");
    }
}
