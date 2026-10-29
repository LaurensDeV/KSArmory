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

    private readonly SolverLoad _solver = new();

    // What this mod costs the frame, split by what costs it. SolverLoad says what the frame cost
    // in total and what the engine's solver took; this is the other side of that subtraction.
    private readonly FrameBudget _budget = new();

    private readonly List<Vehicle> _vehicleScratch = [];

    private readonly System.Diagnostics.Stopwatch _wallClock = System.Diagnostics.Stopwatch.StartNew();

    private double _lastWall;

    // One WeaponSystem per launcher fitted, each with its own policy; Config stays shared.
    private WeaponSystems? _roster;

    // One head per optical director fitted. Crewed independently of the weapons: a craft with a
    // director and no armament gets one, and a craft with a launcher and no director gets none.
    private OpticalHeads? _heads;
    private SearchSets? _searchSets;
    private readonly List<OpticalHeads.Entry> _lockEntries = [];
    private readonly List<OpticalHead> _lockHeads = [];
    private Countermeasures? _countermeasures;

    private IcbmComputers? _icbms;

    private readonly List<double3> _trajectory = [];

    private readonly SiteDesignator _sites = new();
    private Ui? _ui;
    private int _faults;
    private int _viewTrace;

    // Overrun bookkeeping. See ReportOverrun.
    private readonly OverrunLog _overruns = new();
    private bool _disabled;
    private readonly WarpPolicy _warp = new();

    // Holds the main view on one system without handing it the controls.
    private readonly WatchCamera _watch = new();
    private readonly ChaseCamera _chase = new();
    private readonly SightCamera _sight = new();

    // For the cameras: a move tracking a round runs on simulated time, zero while paused.
    private double _lastSimStep;

    // Kept here, not on WeaponSystem, which holds nothing that exists only to be looked at.
    private readonly Dictionary<WeaponSystem, BombSightOverlay> _sights = [];

    // With no panel -- a headless run -- every craft keeps its sight.
    private bool ShowsSight(WeaponSystems.Entry e)
        => _ui is null || ReferenceEquals(e.Craft, _ui.Focused);

    // IcbmOverlay already draws where it lands, and each sight solve is a flown trajectory a
    // climbing rocket would pay for the whole way up.
    private bool FlyingABallisticShot(WeaponSystem system)
        => _icbms?.For(system.Platform)?.Config.Armed == true;

    private BombSightOverlay SightFor(WeaponSystem system)
    {
        if (_sights.TryGetValue(system, out BombSightOverlay? sight)) return sight;

        sight = new BombSightOverlay();
        _sights[system] = sight;
        return sight;
    }

    // What a store already gone can still be walked onto, kept and swept as the pipper is.
    private readonly Dictionary<WeaponSystem, StoreReach> _storeReach = [];

    private StoreReach ReachFor(WeaponSystem system)
    {
        if (_storeReach.TryGetValue(system, out StoreReach? reach)) return reach;

        reach = new StoreReach();
        _storeReach[system] = reach;
        return reach;
    }

    // Every round in the world, as things a sensor can hold. Rebuilt each simulated step.
    private readonly List<IContact> _airborne = [];

    // What each craft's weapons are doing between them, as against what each is doing alone.
    private readonly Armaments _armaments = new();
    private readonly CraftPicture _picture = new();
    private readonly TravelSweeps _travel = new();

    // Development tool: pick a craft up and set it down somewhere else.
    private readonly CraftMover _mover = new();

    // Development tool: click the world to set off a warhead there.
    private readonly BurstTool _bursts = new();
    private readonly Designator _designator = new();

    private MotorSound _motors = null!;
    private readonly MotorPlume _plumes = new();
    private readonly SpinPlume _spinPlumes = new();
    private MotorSmoke _smoke = null!;
    private readonly MuzzleFlash _flashes = new();
    private DecoyEffects _decoyEffects = null!;
    private GunSound _gunSound = null!;
    private ScenarioRunner _scenario = null!;
    private Bridge? _bridge;

    // StarMap runs this before the BeforeMain of any mod depending on this one, which is every pack
    // that ships code; ImmediateLoad comes after all of them.
    [StarMapBeforeMain]
    public void OnBeforeMain() => InstalledPacks.RegisterCore();

    [StarMapImmediateLoad]
    public void OnImmediateLoad(Mod mod)
    {
        // Before anything asks KSA to compile a shader: the generated header names this machine's
        // own install, and a .comp that includes it will not compile until it is there.
        CoreShaderInclude.Write(mod.DirectoryPath);

        Log.Info($"loading (mod id: {mod.Id})");

        // Says why the panel does or does not offer reporting.
        bool supported = ReportDraft.GameIsSupported(Build.KsaBuild, Build.KsaRunning);
        Log.Info($"KSArmory {Build.Version} built for KSA {Build.KsaBuild ?? "?"}, "
                 + $"running {Build.KsaRunning ?? "?"} - reporting {(supported ? "on" : "off")}");
    }

    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        // A pack that ships code has called Armoury.Register by now (StarMap runs a dependent's entry
        // point before this postfix on ModLibrary.LoadAll); these are the asset-only ones. Frozen
        // after, so no registry grows under a crewed system.
        InstalledPacks.RegisterAll();
        BuiltInFamilies.HideDisabledParts();

        JitWarmup.Start();

        Catalogue.Freeze();
        ReportRegistrations();

        // Only now: a pack registers before KSA has loaded any part to check a profile against.
        foreach (PackFault fault in Catalogue.Audit(new DeclaredParts())) Log.Warn(fault.ToString());

        // Before anything is crewed: without it nothing this mod does can point a vehicle.
        AttitudeHook.Install();

        // Puts a hidden-UI frame's step before the render; refused, the frame postfix steps it a
        // frame late.
        PreRenderHook.Install(StepOnce);
        CloudPassHook.Install(() => _config.ShaderPass);

        // The only way to see a save load: StarMap has no hook for one, and the mod never leaves
        // the flight scene across it. A refusal costs one reload's worth of stale rounds.
        WorldReloadHook.Install();

        DoseRateHook.Install();

        PlanetMapHook.Install(() => _ui?.PlanetMap);

        GroundTrackHook.Install((window, at, size, cropMin, cropMax) =>
        {
            if (_icbms is not { } computers) return;
            if (!_config.DrawGroundTrackMarks && !_config.DrawGroundTrackCalibration) return;

            IcbmComputer? computer =
                KsaWorld.TryGroundTrackCraft(window, out Vehicle? craft) ? computers.For(craft)
                                                                        : null;

            GroundTrackMarks.Draw(computer, at, size, cropMin, cropMax,
                                  _config.DrawGroundTrackCalibration);
        });

        // Which save is open and when the game writes one, which the settings kept in a save follow.
        SaveHook.Install();

        // A round's body is a part of its launcher, and KSA draws none of a craft under a pixel across.
        // A refusal leaves that cull, so a long shot's body vanishes where it always did.
        RoundBodyDrawHook.Install(craft => _roster?.HasRoundsInFlight(craft) == true);

        LooseBodyDrawHook.Install((viewport, frameIndex) =>
        {
            if (_roster is not { } roster) return;

            using (_budget.Measure("shell bodies"))
            {
                foreach (WeaponSystems.Entry e in roster.All)
                {
                    e.Weapon.DrawShellBodies(viewport, frameIndex);
                    e.Weapon.DrawFarBodies(viewport, frameIndex);
                }
            }

            if (roster.Loose.Count == 0) return;

            using (_budget.Measure("loose bodies"))
            {
                IReadOnlyList<WeaponSystem> loose = roster.Loose;
                for (int i = 0; i < loose.Count; i++) loose[i].DrawLooseBodies(viewport, frameIndex);
            }
        });

        _roster = new WeaponSystems(_config);
        _heads = new OpticalHeads(_config);
        _roster.DirectorFor = part => _heads.OnPart(part);
        _searchSets = new SearchSets(_config);
        _roster.PictureFor = _picture.BestFor;
        _heads.PictureFor = _picture.BestFor;
        _countermeasures = new Countermeasures();
        _icbms = new IcbmComputers(_config);
        _motors = new MotorSound(_config);
        _smoke = new MotorSmoke(_config);
        _decoyEffects = new DecoyEffects(_config);
        _gunSound = new GunSound(_config);
        _scenario = new ScenarioRunner(_config, _warp, SightFor);
        if (Build.Developer)
        {
            _scenario.Begin(ScenarioRunner.Requested());
            _bridge = new Bridge(_config, craft => _roster?.For(craft)?.Weapon,
                                 () => _roster?.All ?? [], () => _roster?.Loose ?? [],
                                 _countermeasures, craft => _ui?.Manage(craft),
                                 () => _heads?.All ?? [], craft => _icbms?.For(craft),
                                 () => _searchSets?.All ?? [], _picture.BestFor,
                                 () =>
                                 {
                                     _faults = FaultLimit - 1;
                                     Fault("bridge", new InvalidOperationException("the fault limit was tripped from the bridge"));
                                 });
        }

        Log.Info(Build.Developer
                     ? "developer install: the bridge, the scenario runner and the developer controls are on"
                     : "player install: developer tools are off");
        _ui = new Ui(_config, _roster, _heads, _countermeasures, _searchSets, _picture, _icbms, _warp, _watch, _mover, _bursts, ReachFor);
        Log.Info(Catalogue.Launchers.Count > 0
                     ? $"ready - {string.Join(", ", Catalogue.Launchers.Select(l => l.DisplayName))}"
                     : "ready - no weapons: every built-in family is switched off and no installed pack defines one");

        // Logged: every link of this chain fails silently inside KSA.
        Log.Info($"particles graphics setting: {(Detonation.ParticlesEnabled ? "on" : "OFF")}");
        foreach (string preset in WarheadExplosion.Presets)
        {
            Log.Info($"warhead explosion {preset}: {(Detonation.Resolves(preset) ? "ok" : "DID NOT RESOLVE")}");
        }

        // A pack's own effects, checked now rather than at the first shot, which would fall back quietly.
        foreach (MunitionProfile round in Catalogue.Munitions)
        {
            if (round.BurstEffect is { } burst && !Detonation.Resolves(burst))
                Log.Warn($"{round.Name}: BurstEffect=\"{burst}\" is not an explosion any mod declares; it bursts as its charge's own");
            if (round.PlumeEffect is { } plume && !EmitterPool.Declared(plume))
                Log.Warn($"{round.Name}: PlumeEffect=\"{plume}\" is not a particle emitter any mod declares; it burns with KSArmory's");
        }
        foreach (LauncherProfile launcher in Catalogue.Launchers)
        {
            if (launcher.MuzzleFlashEffect is { } flash && !EmitterPool.Declared(flash))
                Log.Warn($"{launcher.PartId}: MuzzleFlashEffect=\"{flash}\" is not a particle emitter any mod declares; it flashes with KSArmory's");
        }
    }

    // What registered: a pack disabled or whose assembly never loaded never reaches here at all.
    private static void ReportRegistrations()
    {
        if (Catalogue.Registrations.Count == 0) return;

        foreach (PackResult pack in Catalogue.Registrations)
        {
            // Reported file by file when they registered.
            if (pack.Source == PackReader.BuiltInSource) continue;

            Log.Info($"pack '{pack.Source}': {pack.Registered} registered"
                     + (pack.Complete ? "" : $", {pack.Faults.Count} refused"));

            foreach (PackFault fault in pack.Faults) Log.Warn(fault.ToString());
        }
    }

    /// <summary>
    /// Simulation tick. The player-time clock StarMap passes is ignored for simulation: it runs
    /// through a pause and ignores timewarp.
    /// </summary>
    [StarMapAfterOnFrame]
    public void OnAfterFrame(double currentPlayerTime, double dtPlayer)
    {
        try
        {
            if (_disabled || _roster is null) return;

            // Logged, since they change what everything else in the log means.
            if (KsaWorld.InFlight)
            {
                double speed = KsaWorld.SimulationSpeed;
                if (Math.Abs(speed - _lastSimSpeed) > 1e-9)
                {
                    Log.Info($"simulation speed {_lastSimSpeed:F2}x -> {speed:F2}x"
                             + (KsaWorld.IsPaused ? " (paused)" : ""));
                    _lastSimSpeed = speed;
                }
            }

            // The fallback, and a no-op on every frame the GUI pass ran. See StepOnce.
            StepOnce(dtPlayer);

            // After the step, so a command sees this frame's world and its simulated step.
            _bridge?.Update(dtPlayer, KsaWorld.InFlightScene && !KsaWorld.IsPaused ? _lastSimStep : 0.0);
        }
        catch (Exception e)
        {
            Fault("frame", e);
        }
        finally
        {
            // Unconditionally: the only hook KSA always calls, and a latch left set stops the mod
            // for the session.
            _frame.EndFrame();
        }
    }

    // From whichever hook reaches it first: F2 skips the GUI pass, which is still preferred because
    // a step there shares an epoch with the draw. CLAUDE.md, "Not done", has why.
    private void StepOnce(double dtPlayer)
    {
        if (_disabled || _roster is null) return;
        if (!_frame.Claim()) return;

        KsaWorld.BeginFrame();
        KsaWorld.AuditBodiesOnce();

        // Before anything integrates, or the previous world's rounds are stepped once more.
        if (WorldReloadHook.ConsumeReload()) ForgetTheWorld();

        // The scene, not the craft: losing the vehicle being flown does not end the flight, and
        // gating on it froze every round in the air.
        if (KsaWorld.InFlightScene)
        {
            using (_budget.Measure("sim", top: true)) StepSimulation(dtPlayer);
        }

        // Not in the GUI hook, which F2 skips: a borrowed view left unrestated freezes. Outside the
        // flight gate, so a view is still handed back on the way out.
        using (_budget.Measure("cameras")) DriveCameras(dtPlayer);

        // Outside the flight gate: a rail is built in the editor, where nothing else here runs.
        using (_budget.Measure("store carriage")) StoreCarriage.Update();

        // The UI pass reads clicks when it runs; with the UI hidden it does not run at all.
        if (!KsaWorld.UiDrawn && KsaWorld.InFlight) UpdateWorldClicks();
    }

    // InFlightScene stays true across a load, so the out-of-flight path never runs, and every system
    // still flying rounds has just gone loose into the new scene.
    private void ForgetTheWorld()
    {
        Log.Info("a save was loaded - forgetting the previous world");

        // Discard, not Clear, which would write this session's settings over the save being opened.
        _roster?.Discard();
        _armaments.Clear();
        _picture.Clear();
        _heads?.Clear();
        _searchSets?.Clear();
        _countermeasures?.Discard();
        _icbms?.Clear();
        KsaWorld.Wreckage.Clear();

        NuclearClouds.Clear();
        BulletHoles.Clear();
        GroundSplats.Clear();
        Leaks.Clear();
        StoreMass.Clear();
        PartFires.Clear();
        DamagedEngines.Clear();

        // Keyed on craft DeserializeSave has destroyed; CollectTeams rebuilds it, but not while paused.
        KsaWorld.TeamRoster.Clear();

        Markers.Forget();

        // The sight forgets rather than releases: DeserializeSave has already unfollowed every camera.
        _chase.Release();
        _sight.Forget();

        // The universe clock restarted at the save's own time.
        KsaWorld.ResetSimStepTracking();
    }

    // From the UI pass, or from the step when F2 skips it: ImGui's frame, and the mouse, are read
    // before KSA's DrawUI gate.
    private void UpdateWorldClicks()
    {
        if (_roster is null || _ui is null) return;

        _mover.Update(_config);
        _bursts.Update(_config);

        // Only the system the panel is showing, or one click fires every launcher in the world.
        if (_roster.For(_ui.Focused) is { } aimed) _designator.Update(aimed.Weapon, aimed.Policy);

        if (_icbms?.For(_ui.Focused) is { } aimedSite) _sites.Update(aimedSite);

        // Shift-click locks the shown installation's turret and every director it carries.
        WeaponSystems.Entry? weapon = _roster.For(_ui.Focused);
        _lockHeads.Clear();
        _heads?.On(_ui.Focused, _lockEntries);
        foreach (OpticalHeads.Entry e in _lockEntries) _lockHeads.Add(e.Head);
        _lockEntries.Clear();

        TargetLock.Update(weapon?.Weapon, _lockHeads,
                          weapon is null ? null : (at, what) => _roster.DesignateWeapon(weapon, at, what));
    }

    // Claim order: the chase reads the sight's base field from the frame before, so swapping them
    // flies a transition begun through a magnified sight down a three-degree straw.
    private void DriveCameras(double dt)
    {
        if (_roster is null || _ui is null) return;

        _watch.Apply(dt);
        _bridge?.DriveCamera();

        // After the watch camera, which would fight it. A ridden round whose craft is destroyed is
        // followed through its own system, which no craft the panel can focus carries any more.
        if (_chase.RiddenSystem is { Platform: null } riding)
        {
            _chase.Apply(riding, enabled: true, dt, _lastSimStep, _config.FreezeChaseTransition,
                         _sight.BaseFovDeg);
        }
        else if (_roster.For(_ui.Focused) is { } selected && _roster.LastReleasing(selected) is var chased)
        {
            _chase.Apply(chased.Weapon, chased.Policy.ChaseRounds && KsaWorld.InFlightScene,
                         dt, _lastSimStep, _config.FreezeChaseTransition, _sight.BaseFovDeg);
        }
        else
        {
            _chase.Release();
        }

        DriveCameraWindows(dt);

        if (KsaWorld.InFlight && _heads?.Driving(_ui.Focused) is { } head)
        {
            TakeMainView(head.Head, head.Policy);
        }
        else if (KsaWorld.InFlightScene)
        {
            // The scene, not InFlight: being shot while looking through a director clears the
            // controlled vehicle and leaves the scene running, and that case needs the hand-back.
            _sight.Release();
        }
        else
        {
            // Out of flight the recording describes a scene that no longer exists.
            _sight.Forget();
        }
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
    /// Panel and world overlay. Gizmos are drawn here: this postfix on OnDrawUiViewports sits between
    /// GizmosRenderer.ResetInstances() and the render, where a postfix on OnFrame would be cleared.
    /// </summary>
    [StarMapAfterGui]
    public void OnAfterGui(double dt)
    {
        if (_disabled || _ui is null || _roster is null) return;

        try
        {
            // First, so the drawing reads this frame's state and shares its epoch; see StepOnce.
            StepOnce(dt);

            _drawFrom = System.Diagnostics.Stopwatch.GetTimestamp();

            using (_budget.Measure("ui")) _ui.Draw();

            // Outside the overlay switch: for a shell with no body this is the round itself.
            if (KsaWorld.InFlight)
            {
                using (_budget.Measure("shells")) foreach (WeaponSystems.Entry e in _roster.All) Visuals.DrawShellStream(e.Weapon);
                if (_heads is not null && !LaserBeams.Painting) foreach (OpticalHeads.Entry e in _heads.All) Visuals.DrawBeam(e.Head);
            }

            // Outside the overlay switch: a sight the operator aims with, not a diagnostic.
            GroundRings.BeginFrame();
            GroundSplats.Paint();
            BulletHoles.Enabled = _config.BulletHoles;
            Leaks.Enabled = _config.TankLeaks;
            BulletHoles.Prune();
            ShellTracers.BallBrightness = _config.BallRoundBrightness;
            foreach (WeaponSystems.Entry e in _roster.All)
            {
                if (e.Policy.DrawBombSight && !FlyingABallisticShot(e.Weapon) && ShowsSight(e))
                {
                    // One pipper per weapon rather than per station: where the next release lands.
                    if (_ui.AimsTheSight(e))
                    {
                        using (_budget.Measure("sight")) SightFor(e.Weapon).Draw(e.Weapon);
                    }

                    using (_budget.Measure("store reach")) ReachFor(e.Weapon).Draw(e.Weapon);
                }
            }

            if (KsaWorld.InFlight && _config.DrawOverlays)
            {
                if (_config.DrawOverlayForFocusedOnly)
                {
                    if (_roster.For(_ui.Focused) is { } shown) Visuals.Draw(shown.Weapon, _config);
                }
                else
                {
                    foreach (WeaponSystems.Entry e in _roster.All) Visuals.Draw(e.Weapon, _config);
                }

                Visuals.DrawDecoys(Countermeasures.Live);
            }

            // Not behind the overlay switch: this is the shot, not a diagnostic. Drawn in a scripted
            // run too: skipping it there (11.1 of a 15.1 ms mod frame) did not move the wall clock.
            if (KsaWorld.InFlight && _icbms is not null)
            {
                using (_budget.Measure("icbmdraw")) IcbmOverlay.Draw(_icbms, _icbms.For(_ui.Focused), _trajectory);
            }

            // Under the panel, which is submitted first: ImGui draws windows in submission order.
            if (KsaWorld.InFlight && _config.DrawSystemMarkers)
                using (_budget.Measure("markers")) Markers.Draw(_ui.Systems, _ui.Focused, dt);

            if (KsaWorld.InFlight && _config.DrawPartHealth)
                using (_budget.Measure("health bars")) PartHealthBars.Draw();

            // On the trigger's weapon, so the brackets are on what FIRE would shoot; after the
            // markers, so a lock reads over a system bracket.
            if (KsaWorld.InFlight && _config.DrawLockCue
                && _ui.TriggerWeaponOn(KsaWorld.ControlledVehicle ?? _ui.Focused) is { } engaging)
            {
                LockCueOverlay.Draw(engaging);
            }

            // Only the brackets; the cameras were driven from StepOnce.
            if (KsaWorld.InFlight) ChaseHud.Draw(_chase);

            // After the panel, so a click on a window is not also a click on the world behind it.
            if (KsaWorld.InFlight)
            {
                UpdateWorldClicks();

                _mover.Draw(_config);
                _bursts.Draw(_config);
                if (_roster.For(_ui.Focused) is { } aimed) _designator.Draw(aimed.Weapon, aimed.Policy);
                if (_icbms?.For(_ui.Focused) is { } aimedSite) _sites.Draw(aimedSite);
                TargetLock.Draw(_roster.For(_ui.Focused)?.Weapon, _heads?.Driving(_ui.Focused)?.Head);
            }
            // Asked of the claim, not the setting: the sight yields the view to the chase without
            // releasing it, and must not paint over the chase's picture.
            if (KsaWorld.InFlight && _heads?.Driving(_ui.Focused) is { } head
                && head.Policy.Viewport == KsaWorld.MainViewportIndex
                && ViewClaim.SightPaints(true, true, _sight.Holding, _chase.HoldsMainView))
            {
                Sight.Draw(head.Head, head.Policy, _roster.For(_ui.Focused)?.Weapon);
            }

            // Each window a head drives, then its sight, with the weapon on that head's own craft.
            if (KsaWorld.InFlightScene && _heads is not null)
            {
                foreach (OpticalHeads.Entry e in _heads.All)
                {
                    if (!_windowsDriven.Contains(e.Policy.Viewport)) continue;

                    ISightPicture? weapon = _roster.For(e.Head.Platform)?.Weapon;
                    OpticalHeads.Entry shown = e;

                    if (!DirectorWindow.Draw(e.Head, e.Policy, ViewportTitle(e.Head),
                                             surface => Sight.Draw(shown.Head, shown.Policy, weapon, surface))
                        && e.Policy.Viewport >= 0)
                    {
                        Sight.Draw(e.Head, e.Policy, weapon);
                    }
                }
            }

        }
        catch (Exception e)
        {
            Fault("gui", e);
        }
        finally
        {
            // The whole GUI pass in one span, closed in a finally so a fault is still measured.
            if (_drawFrom != 0L)
            {
                _budget.Book("draw",
                             System.Diagnostics.Stopwatch.GetElapsedTime(_drawFrom).TotalMilliseconds,
                             top: true);
                _drawFrom = 0L;
            }
        }
    }

    private long _drawFrom;

    // Claimed by whichever hook steps the frame, released by the one that cannot be skipped.
    private readonly FrameLatch _frame = new();

    // One simulation step. Run through StepOnce, never called directly.
    private void StepSimulation(double dtPlayer)
    {
        if (_roster is null) return;

        // At the start, so a pass never inherits a census from a frame a craft was destroyed out of.
        KsaWorld.InvalidateCensus();

        // Before the clock gate and the sample: the overlay is drawn against it, and a director
        // fitted this frame is read this frame.
        if (_heads is not null)
        {
            using (_budget.Measure("headsync"))
            {
                _heads.Sync(KsaWorld.Vehicles);
            }
        }

        using (_budget.Measure("dispsync")) _countermeasures?.Sync(KsaWorld.Vehicles);
        using (_budget.Measure("setsync")) _searchSets?.Sync(KsaWorld.Vehicles);

        _roster.RetireDestroyed();
        _roster.ShareStationSettings();
        _roster.ReconcileSteerables();

        using (_budget.Measure("sample")) foreach (WeaponSystems.Entry e in _roster.All) e.Weapon.SampleWorld();
        using (_budget.Measure("travel")) _travel.Step(_roster.All, _ui?.Focused);
        using (_budget.Measure("headsample")) _heads?.SampleWorld();
        using (_budget.Measure("setsample")) _searchSets?.SampleWorld();

        // Gated on the step the engine applied, not on the pause flag: on the frame speed drops to
        // zero the engine still applies one real step, and the platform sample moves with it.
        {
            // From the engine, never dtPlayer * SimulationSpeed, which misses by up to 0.9 ms, 27 m
            // at 29.8 km/s. Consumed, not peeked: see KsaWorld.ConsumeSimStep.
            double dtSim;
            using (_budget.Measure("consume")) dtSim = KsaWorld.ConsumeSimStep();

            // For the cameras, which cannot consume the step themselves.
            _lastSimStep = double.IsFinite(dtSim) && dtSim > 0.0 ? dtSim : 0.0;

            // No step reported, no step taken: an estimate would integrate across an interval the
            // world did not move over, all of it landing in the drawn offset.
            bool skipped;
            using (_budget.Measure("clock"))
            {
                skipped = SimClock.Classify(dtSim, KsaWorld.IsPaused, out _) == SimClock.State.Skipped;
            }

            if (skipped)
            {
                ReportOverrun(dtSim);
            }

            using (_budget.Measure("warp")) ApplyWarpPolicy(dtSim);

            // The clamp discards time; the warp policy takes effect only from the next frame.
            if (double.IsFinite(dtSim) && dtSim > 0.0)
            {
                double faithful;
                using (_budget.Measure("faithful")) faithful = FaithfulStepInFlight();

                double step = Math.Min(dtSim, faithful);

                // Before anything reads it; the rounds collected below carry their shooter's side.
                using (_budget.Measure("teams"))
                {
                    CollectTeams();
                    _searchSets?.AdoptTeams();
                }

                // Once, not per system, which would be quadratic in systems.
                using (_budget.Measure("airborne")) CollectAirborne(step);

                // After the airborne sample, which the warning receivers read, and before any round
                // is flown, so a seeker reads each decoy at the end of the step it integrates across.
                // On the clamped step for the same reason: a decoy and the round judging it share one.
                using (_budget.Measure("decoys")) _countermeasures?.Update(step, _airborne);

                // Before the fire loop, or which system under-counts is the roster's iteration order.
                using (_budget.Measure("allocate")) _armaments.Refresh(_roster.All);

                using (_budget.Measure("fire")) foreach (WeaponSystems.Entry e in _roster.All) e.Weapon.Update(step, _airborne);

                // After the fire loop, so a round that left this step has already come off.
                using (_budget.Measure("store mass")) WantStoreMass();

                // After the crewed ones on the same step, so a round is stepped once whichever side
                // of its launcher's death it is on.
                using (_budget.Measure("loose")) _roster.UpdateLoose(step, _airborne);

                // After every launcher radar has scanned and before the heads aim, so a director takes
                // this step's picture; a beam weapon, stepped in the fire loop above, takes the last one.
                using (_budget.Measure("search sets")) _searchSets?.Update(dtSim, _airborne);
                using (_budget.Measure("picture")) _picture.Refresh(_roster.All, _searchSets?.All ?? []);

                // Unclamped: a director has no fuse, and the clamp biting only on long frames shook
                // the picture with the display's pacing above about 13x.
                using (_budget.Measure("heads")) _heads?.Update(dtSim, _airborne);

                // Unclamped too: a cutoff timed against a truncated step would hide the cost.
                if (_icbms is not null)
                {
                    using (_budget.Measure("icbm"))
                    {
                        _icbms.Update(dtSim, dtPlayer, _roster, _config.TraceWarhead);
                    }
                }
            }
        }

        // Outside the clock gate: placing bodies is drawing, needed on every rendered frame.
        using (_budget.Measure("bodies"))
        {
            foreach (WeaponSystems.Entry e in _roster.All) e.Weapon.SyncRoundBodies();
        }

        // Per frame, because a wobble is a shape and the periodic dump cannot see one.
        using (_budget.Measure("rates")) if (KsaWorld.ControlledVehicle is { } flown) Diagnostics.SampleBodyRates(flown, dtPlayer);

        // On player time: what is measured is the engine keeping up with the wall clock.
        if (Log.Threshold <= Log.Level.Debug)
        {
            // Against a real clock: dtPlayer is clamped at a thirtieth, and at eight rockets read
            // "achieved 1.000" while the world advanced 10 s per 24 s of wall.
            double wallNow = _wallClock.Elapsed.TotalSeconds;

            // Before _lastWall moves, and shared by both instruments.
            double sinceLast = _lastWall > 0.0 ? wallNow - _lastWall : 0.0;

            if (sinceLast > 0.0)
            {
                using (_budget.Measure("gauge"))
                {
                    _solver.Sample(KsaWorld.AchievedSpeedFraction, KsaWorld.VehicleSolverTickMs,
                                   _lastSimStep, sinceLast);
                }
            }

            _lastWall = wallNow;

            if (_solver.Due)
            {
                _vehicleScratch.Clear();
                KsaWorld.CollectVehicles(_vehicleScratch);
                Log.Debug(_solver.Take(_vehicleScratch.Count));
            }

            // Closed here so FrameBudget's window and SolverLoad's are one.
            _budget.EndFrame(sinceLast);

            if (_budget.Due) Log.Debug(_budget.Take());
        }

        // One span for the rest, which comes after _budget.EndFrame and would otherwise go unmeasured.
        using (_budget.Measure("tail"))
        {

        // After the rounds have been stepped, so effects sit where the round now is.
        ShellTracers.BeginFrame();
        LaserBeams.BeginFrame();
        if (_heads is not null) foreach (OpticalHeads.Entry e in _heads.All) LaserBeams.Collect(e.Head);
        foreach (WeaponSystems.Entry e in _roster.All)
        {
            _motors.Update(e.Weapon);
            _plumes.Update(e.Weapon);
            _spinPlumes.Update(e.Weapon);
            _smoke.Update(e.Weapon);
            _flashes.Update(e.Weapon);
            ShellTracers.Collect(e.Weapon);
            _gunSound.Update(e.Weapon);

            // Per weapon, not per station, and only on the craft the panel shows: each solve flies
            // BombSight.MaxSteps steps.
            if (e.Policy.DrawBombSight && !FlyingABallisticShot(e.Weapon) && ShowsSight(e))
            {
                if (_ui?.AimsTheSight(e) ?? true)
                {
                    using (_budget.Measure("sight solve")) SightFor(e.Weapon).Update(e.Weapon, _lastSimStep);
                }

                // Only a store falling with nothing marked has a reach worth solving; it clears itself
                // for every other station.
                using (_budget.Measure("reach solve")) ReachFor(e.Weapon).Update(e.Weapon);
            }
            else
            {
                SightFor(e.Weapon).Clear();
                ReachFor(e.Weapon).Clear();
            }
        }

        // The effects that ride the round rather than the mount, for rounds outliving their launcher.
        IReadOnlyList<WeaponSystem> loose = _roster.Loose;
        for (int i = 0; i < loose.Count; i++)
        {
            _plumes.Update(loose[i]);
            _spinPlumes.Update(loose[i]);
            _smoke.Update(loose[i]);
            ShellTracers.Collect(loose[i]);
        }

        // The newest complete profiler frame, which the GPU has finished with.
        CloudPassCost.Sample();

        // Stepped here, not in the UI pass, which F2 skips; gated on the scene, not the craft.
        using (_budget.Measure("clouds"))
        {
            NuclearClouds.RedWaves = _config.RedWave;
            if (_config.NuclearClouds) NuclearClouds.Update(_lastSimStep);
            else NuclearClouds.Clear();
        }

        // Stepped here: CloudPass writes it into the scene image so it survives a hidden HUD.
        BurstFlash.Update(_lastSimStep);

        // And the bangs and fronts still on their way.
        BurstSound.Update(_lastSimStep);
        using (_budget.Measure("fronts")) BlastArrivals.Update(_lastSimStep);
        using (_budget.Measure("leaks")) Leaks.Update(_lastSimStep);
        using (_budget.Measure("fires")) PartFires.Update(_lastSimStep, _config, draw: true);
        DamagedEngines.Update();
        BlastShake.Update(_lastSimStep);
        AttitudeHook.CheckShoves(_lastSimStep);

        // Pruned, or a sight keeps its system for the session after the craft has gone.
        if (_sights.Count > _roster.Count)
        {
            _sights.Clear();
            foreach (WeaponSystems.Entry e in _roster.All) SightFor(e.Weapon);
        }

        if (_storeReach.Count > _roster.Count)
        {
            _storeReach.Clear();
            foreach (WeaponSystems.Entry e in _roster.All) ReachFor(e.Weapon);
        }

        // Nobody's, so no sweep: a decoy is released the frame it is spent.
        using (_budget.Measure("decoy fx")) _decoyEffects.Update(Countermeasures.Live);

        // Every effect that holds a pooled emitter or a channel, so a craft destroyed mid-salvo
        // does not keep one for the session.
        _motors.Sweep(_roster);
        _plumes.Sweep(_roster);
        _spinPlumes.Sweep(_roster);
        _smoke.Sweep(_roster);
        _flashes.Sweep(_roster);
        _gunSound.Sweep(_roster);

        // After everything has run, so a scenario reads this frame's state. Its time budget runs on
        // player time so an unattended run cannot hang.
        _scenario.Update(_roster, _icbms, _lastSimStep, dtPlayer);

        // Every frame, not on a timer: a save and a load can both fit inside half a second.
        switch (_roster.Remember())
        {
            case WeaponSystems.SettingsMoment.Saved: _icbms?.WriteDown(); break;
            case WeaponSystems.SettingsMoment.Reopened: _icbms?.Adopt(); break;
        }
        }
    }

    [StarMapUnload]
    public void Unload()
    {
        // Or unloading mid-salvo leaves the world stuck at the speed the policy wound it down to.
        if (_warp.Holding && KsaWorld.SetSimulationSpeed(_warp.HeldSpeed))
        {
            Log.Info($"timewarp restored to {_warp.HeldSpeed:F0}x - unloading");
        }
        _warp.Clear();
        _watch.Release();
        _chase.Release();
        _sight.Release();

        // After the cameras have let go, so nothing is mid-write when the controller is swapped back.
        KsaWorld.RestoreStockController();
        _mover.Release();

        // Pooled emitters and channels belong to the game, and nothing else gives them back.
        _motors?.StopAll();
        _gunSound?.StopAll();
        _plumes?.ReleaseAll();
        _spinPlumes?.ReleaseAll();
        _decoyEffects?.ReleaseAll();
        _flashes?.ReleaseAll();
        PartFires.Clear();

        Markers.Forget();

        LetGoOfTheGame();
        KsaWorld.ResetSimStepTracking();
        _roster = null;
        _ui = null;
        Log.Info("unloaded");

        // Last: the log batches its writes.
        Log.Shutdown();
    }

    // One main view, so one head at a time: the one on the craft the panel shows.
    private void TakeMainView(OpticalHead system, OpticConfig policy)
    {
        bool wantsMainView = policy.Viewport == KsaWorld.MainViewportIndex;

        // Every frame, the optic off included: that hands the view back and resumes after a chase.
        ViewAction did = _sight.Apply(system, wantsMainView, outranked: _chase.HoldsMainView,
                                      policy.Magnification);

        // Taking the view back by hand switches the optic off, or the next frame takes it again.
        if (did == ViewAction.StandDown) policy.Viewport = -1;
    }

    // Every head with a window drives it, whichever craft the panel shows.
    private HashSet<int> _windowsDriven = [], _windowsDrivenBefore = [];

    private void DriveCameraWindows(double dt)
    {
        (_windowsDrivenBefore, _windowsDriven) = (_windowsDriven, _windowsDrivenBefore);
        _windowsDriven.Clear();
        SensorPass.BeginFrame();

        if (_heads is not null && KsaWorld.InFlightScene)
        {
            Vehicle? focused = _ui?.Focused;
            int main = KsaWorld.MainViewportIndex;

            foreach (OpticalHeads.Entry e in _heads.All)
            {
                int index = e.Policy.Viewport;
                if (index < 0 || index == main) continue;

                // Two heads on one window would fight for it every frame. The first keeps it.
                if (!_windowsDriven.Add(index))
                {
                    e.Policy.Viewport = -1;
                    continue;
                }

                bool trace = ReferenceEquals(e.Head.Platform, focused);
                if (DriveWindow(e.Head, e.Policy, dt, trace)) SensorPass.Want(index, e.Policy.Sensor);

                // Kept through a frame the eye could not be resolved, so the window is not closed
                // under the player for it; dropped once the head lets the window go.
                if (e.Policy.Viewport != index) _windowsDriven.Remove(index);
            }
        }

        // With KSA's own window taken off, nothing else would close it and it would render unseen.
        foreach (int released in _windowsDrivenBefore)
        {
            if (_windowsDriven.Contains(released)) continue;

            KsaWorld.ResetViewportFov(released);
            KsaWorld.CloseCameraWindow(released);
        }
    }

    // One camera window on one head. It follows nothing, so it is positioned outright.
    private bool DriveWindow(OpticalHead system, OpticConfig policy, double dt, bool traced)
    {
        // The player closed it: switch the director off rather than drive a viewport nobody shows.
        if (!KsaWorld.IsUsableCameraWindow(policy.Viewport))
        {
            policy.Viewport = -1;
            Log.Info("camera: that window was closed; the director is off");
            return false;
        }

        if (system.OpticPart is null) return false;

        bool trace = false;
        if (traced)
        {
            _viewTrace += 1;
            trace = _viewTrace % 60 == 0;
        }

        // As this window's camera draws it: following nothing, it draws craft at their simulated
        // positions, metres from the main camera's physics ones.
        double3 anchor = system.PlatformEcl;
        if (system.Platform is { } platform
            && KsaWorld.TryDrawnCentreEcl(platform, out double3 drawnEcl, policy.Viewport))
        {
            anchor = drawnEcl;
        }

        if (!system.TryOpticViewEclAt(anchor, out double3 eye, out double3 forward, policy.Viewport))
        {
            if (trace) Log.Debug(() => "camera: could not resolve the optical head's eye");
            return false;
        }

        // Nothing should move the pose between a write and the render; a drift says something did.
        if (trace) ProbeViewDrift(policy.Viewport);

        // On a second monitor the title bar is all that identifies the window.
        KsaWorld.NameViewport(policy.Viewport, ViewportTitle(system));

        // Against the engine's default field: a window has no player's field to borrow.
        double fovDeg = SightZoom.FovDegreesFor(SightZoom.DefaultFovDeg, policy.Magnification);

        // Local "up" at the launcher, which is what the boresight already is — so the horizon
        // sits level rather than rolling with the ecliptic.
        bool took = KsaWorld.TryLookFromViewport(policy.Viewport, eye, forward,
                                                 system.Boresight, fovDeg, dt);
        if (trace)
        {
            Log.Debug(() => $"camera: view {policy.Viewport} of {KsaWorld.ViewportCount} "
                            + $"took={took} fov={fovDeg:F1} drawnGap={Vec.Len(anchor - system.PlatformEcl):F2} m "
                            + $"eye={eye.X:F0},{eye.Y:F0},{eye.Z:F0} "
                            + $"fwd={forward.X:F3},{forward.Y:F3},{forward.Z:F3}");
        }

        if (!took)
        {
            policy.Viewport = -1;
            Log.Warn("camera: could not drive that view; released it");
        }

        return took;
    }

    // Where the camera window was left, so the next pass can say whether anything moved it.
    private double3 _wroteEye, _wroteForward;
    private bool _wroteView;

    private void ProbeViewDrift(int viewport)
    {
        if (!KsaWorld.TryReadViewportPose(viewport, out double3 eye, out double3 forward, out _))
        {
            return;
        }

        if (_wroteView)
        {
            double moved = Vec.Len(eye - _wroteEye);
            double turned = double.RadiansToDegrees(Vec.AngleBetween(_wroteForward, forward));

            string against = KsaWorld.TryReadViewportPose(KsaWorld.MainViewportIndex,
                                                          out double3 mainEye, out _, out _)
                           ? $" mainSep {Vec.Len(mainEye - eye):F1} m"
                           : string.Empty;

            Log.Debug(() => $"camera probe: view {viewport} drifted {moved:F2} m, "
                            + $"{turned:F2} deg since the last write{against}");
        }

        _wroteEye = eye;
        _wroteForward = forward;
        _wroteView = true;
    }

    // The craft too: two craft can carry the same pod.
    private static string ViewportTitle(OpticalHead head)
    {
        string part = head.Profile.DisplayName;

        return head.Platform is { IsDisposed: false } craft
             ? $"{part} - {KsaWorld.DisplayName(craft)}"
             : part;
    }

    // Every part every frame, so a reload or a switch turned off puts its mass back too.
    private void WantStoreMass()
    {
        foreach (WeaponSystems.Entry e in _roster!.All)
        {
            WeaponSystem w = e.Weapon;
            if (w.Launcher is not { } part || w.Platform is not { } craft) continue;

            LauncherProfile p = w.Profile;

            // A bus's warheads come off only under ShedWarheadMass: they change every accuracy number.
            double perRound = _icbms?.For(craft) is { } computer && !computer.Config.ShedWarheadMass
                            ? 0.0
                            : p.StoreMassKg;

            double shed = StoreLoad.ShedKg(perRound, w.MagazineFull, w.Ammo)
                        + StoreLoad.ShedKg(p.GunRoundMassKg, p.GunAmmo, w.GunAmmo);

            StoreMass.Want(craft, part, shed);
        }

        _countermeasures?.WantStoreMass();
    }

    // Rebuilt every frame, since it keys on live Vehicles. The rank, not the dictionary's order,
    // settles a craft whose installations disagree about their side.
    private void CollectTeams()
    {
        KsaWorld.TeamRoster.Clear();

        if (_roster is not null)
        {
            foreach (WeaponSystems.Entry e in _roster.All)
            {
                KsaWorld.TeamRoster.Declare(e.Craft, e.Policy.Iff.OwnTeam, e.Ordinal);
                _config.RulesFor(e.Policy.Iff.OwnTeam).ApplyTo(e.Policy.Iff);
            }
        }

        if (_heads is null) return;

        foreach (OpticalHeads.Entry h in _heads.All)
        {
            KsaWorld.TeamRoster.Declare(h.Head.Platform, h.Policy.Iff.OwnTeam, TeamRoster.DirectorRank);
            _config.RulesFor(h.Policy.Iff.OwnTeam).ApplyTo(h.Policy.Iff);
        }
    }

    // A round carries its shooter's side and name, carried because a loose system has no craft to
    // look them up by. Once, before any system updates, and carried forward by the step to the end
    // of the frame, where KSA reports vehicle state: ~1 mm of error against a frame of closing motion.
    private void CollectAirborne(double step)
    {
        _airborne.Clear();
        if (_roster is null) return;

        foreach (WeaponSystems.Entry e in _roster.All)
        {
            WeaponSystem system = e.Weapon;
            if (system.Platform is not { } platform) continue;

            AddAirborne(system.Rounds, KsaWorld.DisplayName(platform), system.Team, platform,
                        KsaWorld.ParentBody(platform), step);
        }

        // Loose rounds are still contacts, with no craft to anchor them; RoundContact declines to draw.
        IReadOnlyList<WeaponSystem> loose = _roster.Loose;
        for (int i = 0; i < loose.Count; i++)
        {
            AddAirborne(loose[i].Rounds, loose[i].LooseName, loose[i].Team, null, loose[i].EffectBody, step);
        }
    }

    private void AddAirborne(IReadOnlyList<IProjectile> rounds, string firedBy, string? team,
                             KSA.Vehicle? anchor, Celestial? body, double step)
    {
        for (int i = 0; i < rounds.Count; i++)
        {
            IProjectile r = rounds[i];
            if (r.State != RoundState.Flying) continue;

            double3 at = r.PositionEcl + r.VelocityEcl * step;
            _airborne.Add(new RoundContact(r, firedBy, team, anchor, at, r.VelocityEcl,
                                           CoastingAcceleration(r, body, at)));
        }
    }

    // What a sensor holding the round would measure while it coasts: its body's pull and the round's
    // own drag, so a lead against it is flown against the round's own model. Without the body's fall,
    // like a craft's AccelerationEcl and the lead's ground frame: the fall is common to all three.
    private static double3 CoastingAcceleration(IProjectile round, Celestial? body, double3 at)
    {
        if (body is null) return Vec.Zero;

        double3 air = round.VelocityEcl - KsaWorld.GroundVelocityAt(body, at);
        double3 total = Medium.Coasting(KsaWorld.GravityAt(body, at), air, round.Munition,
                                        KsaWorld.MediumDensityRatioAt(body, at));

        return Vec.IsFinite(total) ? total : Vec.Zero;
    }

    // OverrunLog holds the counting and the rate limit.
    private void ReportOverrun(double stepSeconds)
    {
        // Only rounds take the clamped step, so an empty sky lost nothing; a scene load is that case.
        bool anyInFlight = false;
        _roster?.FaithfulStep(out anyInFlight);

        OverrunLog.Notice notice = _overruns.Observe(stepSeconds, anyInFlight);
        if (notice == OverrunLog.Notice.Silent) return;

        string what =
            $"step {stepSeconds * 1000.0:F0} ms exceeds the {Interceptor.MaxFaithfulStep * 1000.0:F0} ms "
            + $"a round can integrate faithfully; clamped and carried on. "
            + $"{_overruns.Frames} frame(s), {_overruns.DiscardedSeconds:F2} s of simulated time discarded.";

        if (notice == OverrunLog.Notice.Lagging) Log.Warn($"{what} Rounds in flight will lag the world.");
        else Log.Debug($"{what} Nothing was in the air across it.");
    }

    private double FaithfulStepInFlight()
    {
        return _roster?.FaithfulStep(out _) ?? Interceptor.MaxFaithfulStep;
    }

    private void ApplyWarpPolicy(double dtSim)
    {
        if (_roster is null) return;

        // Any round anywhere, and the fussiest round in the air.
        _roster.FaithfulStep(out bool anyInFlight);

        // The step the world is asked to hold to, not the one a round survives: clamping only drops
        // the difference.
        double faithful = _roster.WarpTargetStep();

        // A guided burn too: cut off a step late at warp, it is kilometres a second out.
        bool anyBurning = false;
        double icbmFaithful = _icbms is null
                                  ? double.MaxValue
                                  : _icbms.FaithfulStep(out anyBurning);

        WarpDecision d = _warp.Decide(dtSim, KsaWorld.SimulationSpeed,
                                      anyInFlight || anyBurning, _config.LimitWarpInFlight,
                                      Math.Min(faithful, icbmFaithful),
                                      KsaWorld.IsAutoWarpActive);

        switch (d.Action)
        {
            case WarpAction.Slow:
            case WarpAction.Restore:
                // A refused write is not an error here: the policy waits for the value it asked
                // for to appear and abandons on its own if it never does.
                if (KsaWorld.SetSimulationSpeed(d.Speed))
                {
                    Log.Info(d.Action == WarpAction.Slow
                                 ? $"timewarp held at {d.Speed:F1}x - {d.Why} --"
                                   + $"{(anyInFlight ? $" rounds{_roster.WhoHasRoundsUp()}" : "")}"
                                   + $"{(anyBurning ? $" flights{_icbms?.WhoNeedsShortSteps()}" : "")}"
                                 : $"timewarp restored to {d.Speed:F0}x - {d.Why}");
                }
                break;

            case WarpAction.Yield:
                Log.Warn($"timewarp not held - {d.Why}");
                break;

            case WarpAction.Abandon:
                foreach (WeaponSystems.Entry e in _roster.All) e.Weapon.AbandonFlight(d.Why);

                IReadOnlyList<WeaponSystem> stranded = _roster.Loose;
                for (int i = 0; i < stranded.Count; i++) stranded[i].AbandonFlight(d.Why);

                // A burn the world outran is stood down with an explanation.
                _icbms?.AbandonBurns(d.Why);
                break;

            case WarpAction.None:
            default:
                break;
        }
    }

    private void Fault(string where, Exception e)
    {
        _faults++;
        Log.Error($"{where} failed ({_faults}/{FaultLimit})", e);

        if (_faults < FaultLimit) return;

        _disabled = true;
        LetGoOfTheGame();
        Log.Error("too many faults - air defence disabled for this session");
    }

    // Shared by unloading and by the fault limit, so a hook added to one cannot be missed by the other.
    private void LetGoOfTheGame()
    {
        _icbms?.WriteDown();
        _roster?.Clear();
        _armaments.Clear();
        _picture.Clear();
        _heads?.Clear();
        _heads = null;
        _searchSets?.Clear();
        _searchSets = null;
        _countermeasures?.Discard();
        _countermeasures = null;
        _icbms?.Clear();
        _icbms = null;
        KsaWorld.Wreckage.Clear();
        AttitudeHook.Remove();
        PreRenderHook.Remove();
        CloudPassHook.Remove();
        WorldReloadHook.Remove();
        DoseRateHook.Remove();
        Radioactivity.Clear();
        GroundTrackHook.Remove();
        PlanetMapHook.Remove();
        SaveHook.Remove();
        RoundBodyDrawHook.Remove();
        LooseBodyDrawHook.Remove();
    }
}
