using Brutal.Numerics;
using KSA;

namespace AirDefence;

/// <summary>Something worth telling the operator about, surfaced in the panel.</summary>
internal readonly record struct BatteryEvent(double AtSeconds, string Message);

/// <summary>
/// The air-defence battery: a six-round launcher, its radar, and the fire-control logic
/// that decides when to commit rounds. Mounted on a platform vehicle, which is normally
/// whatever the player is flying but can be pinned so the site keeps defending itself
/// after the player switches away.
/// </summary>
internal sealed class DefenceBattery(Config config)
{
    private readonly Config _config = config;
    private readonly List<IProjectile> _rounds = [];
    private readonly List<Vehicle> _blastScratch = [];
    private readonly List<Vehicle> _pendingKills = [];
    private readonly List<BatteryEvent> _events = [];

    private Vehicle? _lastPlatform;
    private double _salvoTimer;
    private double _reloadTimer;
    private double _clock;

    /// <summary>The vehicle the launcher is mounted on.</summary>
    public Vehicle? Platform { get; private set; }

    /// <summary>True when the operator pinned the platform rather than following control.</summary>
    public bool PlatformPinned { get; private set; }

    public Radar Radar { get; } = new(config);

    /// <summary>Rounds left in the launcher.</summary>
    public int Ammo => _magazine.Ammo;

    public IReadOnlyList<IProjectile> Rounds => _rounds;

    public IReadOnlyList<BatteryEvent> Events => _events;

    /// <summary>Seconds left on the reload cycle, or zero when not reloading.</summary>
    public double ReloadRemaining => _reloadTimer;

    /// <summary>Current radar boresight in Ecl. Local "up" at the platform.</summary>
    public double3 Boresight { get; private set; } = new(0, 0, 1);

    /// <summary>The launcher part on the platform, or null if none is fitted.</summary>
    public Part? Launcher { get; private set; }

    /// <summary>The launcher's turret subpart, which the mod slews onto the track.</summary>
    public Part? TurretPart { get; private set; }

    /// <summary>The missile pods, which elevate on the turret's trunnions.</summary>
    public Part? PodsPart { get; private set; }

    /// <summary>The search array, which turns continuously on its own turntable.</summary>
    public Part? RadarPart { get; private set; }

    /// <summary>The search array's current angle. Cosmetic - the radar model is a cone search.</summary>
    public double RadarSpinRad { get; private set; }

    /// <summary>Azimuth drive state. Pure maths, no KSA types — see <see cref="Turret"/>.</summary>
    public Turret Turret { get; } = new();

    /// <summary>
    /// False once KSA has refused a turret write. Writing every frame to an API the engine
    /// ignores would fill the log and hide the one message that matters, so the first failure
    /// turns it off for the session.
    /// </summary>
    public bool TurretDriveWorks { get; private set; } = true;

    /// <summary>
    /// The weapon system this battery is running, and what it fires.
    ///
    /// Read through the config rather than captured at construction: the battery does not know
    /// which launcher it has until it finds the part, and the panel can retune the profile
    /// while an engagement is under way.
    /// </summary>
    private LauncherProfile _profile => _config.Launcher;
    private MunitionProfile _munition => _config.Munition;

    /// <summary>How far the platform moved between the last two frames (m, Ecl).</summary>
    public double3 PlatformStepEcl { get; private set; }

    private bool _hasPlatformSample;
    private bool _loggedSubParts;
    private double _spinPhase;
    private readonly List<Part> _missileBodies = [];
    private readonly List<Part> _finBodies = [];

    /// <summary>
    /// Which tubes still hold a round, and which fires next. See <see cref="Magazine"/> — the
    /// bookkeeping is pure and lives in Sim/ so it is testable, because getting it wrong produces
    /// a salvo that looks like it never left rather than an error.
    /// </summary>
    private readonly Magazine _magazine = new();

    /// <summary>The fin set belonging to a tube, or null if the launcher carries none.</summary>
    private Part? FinsFor(int index) => index >= 0 && index < _finBodies.Count ? _finBodies[index] : null;

    /// <summary>
    /// False once KSA has refused to place a round body. Unlike the turret, these travel
    /// kilometres from the vehicle they belong to, so the engine may well decline — hence the
    /// gizmo tracers stay on as a fallback rather than being replaced.
    /// </summary>
    public bool RoundBodiesWork { get; private set; } = true;

    /// <summary>How many round bodies the launcher actually carries. Zero means tracers only.</summary>
    public int RoundBodyCount => _missileBodies.Count;

    /// <summary>
    /// Frames rendered, while unpaused, that advanced no simulated time.
    ///
    /// <para>Diagnostic for round bodies appearing to stutter or teleport. If this climbs, the
    /// render rate is outrunning the simulation clock, and anything positioned only on a
    /// simulation step will visibly lag the world. Expected to stay at zero: KSA derives its
    /// step from the frame delta, so every frame should advance the clock.</para>
    /// </summary>
    public int FramesWithoutSimStep { get; set; }

    /// <summary>Trace one frame in this many, so a debug log stays readable at 60 fps.</summary>
    private const int BodyTraceEveryFrames = 15;

    private int _bodyFrame;
    private bool _warnedDuplicateTube;

    /// <summary>Where rounds actually leave from: the launcher part, or the hull without one.</summary>
    public double3 MountEcl { get; private set; }

    /// <summary>
    /// The platform's Ecl position at the moment this update ran. Everything else the battery
    /// records — mount, tracks, rounds — is from the same instant, so this is the reference the
    /// overlay must difference against. Re-reading the platform's position at draw time instead
    /// mixes instants a frame apart, which at ~29.8 km/s of ecliptic motion is ~500 m of error.
    /// </summary>
    public double3 PlatformEcl { get; private set; }

    /// <summary>True when the battery has everything it needs to shoot.</summary>
    public bool IsOperational => Platform is not null && (Launcher is not null || !_config.RequireLauncherPart);

    /// <summary>
    /// True when the launcher is actually pointing where it is about to shoot.
    ///
    /// <para>Without this the battery fired the instant it had a lock, while the turret was
    /// still swinging round — rounds left tubes that were aimed somewhere else entirely, which
    /// looked exactly as wrong as it was. Guidance recovered and the intercepts still worked,
    /// which is precisely why it needed to be watched rather than measured.</para>
    ///
    /// <para>Always true when nothing is driving the turret — tracking switched off, no pods
    /// fitted, or the engine refusing the transform write — so this can never deadlock fire
    /// control on a launcher that is never going to move.</para>
    /// </summary>
    public bool IsLaid
    {
        get
        {
            if (!_config.TurretTracking || _config.TurretManual || _config.TurretSpin) return true;
            if (PodsPart is null || !TurretDriveWorks) return true;
            return Turret.IsLaid(_profile.SettleSeconds);
        }
    }

    public void PinPlatform(Vehicle? v)
    {
        Platform = v;
        PlatformPinned = v is not null;
        Announce(v is null ? "platform released, following control" : $"platform pinned to {KsaWorld.DisplayName(v)}");
    }

    /// <summary>
    /// Re-reads where the world is. <b>Must run every rendered frame, not every simulation
    /// step.</b>
    ///
    /// <para>This sets <see cref="PlatformEcl"/>, <see cref="Boresight"/> and
    /// <see cref="MountEcl"/> — the frame of reference the entire overlay is drawn against.
    /// <c>Visuals</c> hands <see cref="PlatformEcl"/> to <c>KsaWorld.BeginDraw</c> as the
    /// anchor's Ecl half, and <see cref="DrawAnchor"/> pairs it with an Ego position sampled
    /// fresh every frame. If this half goes stale while that half does not, the pair no longer
    /// describes one instant and the whole overlay slides off the craft and jitters.</para>
    ///
    /// <para>That is exactly what happened when stepping moved onto the simulation clock:
    /// <c>Update</c> had always run once per frame, so the invariant held by accident rather
    /// than by design. Gating it on the clock left the overlay's reference frozen on any frame
    /// the simulation did not advance. Confirmed by bisect — the commit before that change
    /// draws dead centre.</para>
    ///
    /// <para>Sampling only: it reads the world and resolves parts, and advances nothing.</para>
    /// </summary>

    public void SampleWorld()
    {
        ResolvePlatform();
        if (Platform is null)
        {
            Radar.Reset();
            _rounds.Clear();
            return;
        }

        // Taking control of another craft re-homes the battery to it. Rounds already in flight
        // store their position relative to the platform, so without re-basing they would jump
        // by the distance between the two craft and appear to fly off course. Their actual
        // trajectory is untouched - this only keeps the bookkeeping honest.
        if (!ReferenceEquals(Platform, _lastPlatform))
        {
            if (_lastPlatform is not null && _rounds.Count > 0)
            {
                Announce($"platform changed to {KsaWorld.DisplayName(Platform)}, re-basing {_rounds.Count} round(s) in flight");
            }
            _lastPlatform = Platform;
        }

        // The platform's movement since the last frame, measured rather than derived. This is
        // what advances it to the round's instant when offsets are taken - see Interceptor.
        double3 sampled = KsaWorld.PositionEcl(Platform);
        PlatformStepEcl = _hasPlatformSample ? sampled - PlatformEcl : Vec.Zero;
        _hasPlatformSample = true;
        PlatformEcl = sampled;

        // Whichever registered weapon system is fitted, if any. Selecting it points the
        // config's profiles at that system, so everything downstream - drives, guidance, the
        // panel - follows without knowing which launcher this is.
        if (LauncherPart.Find(Platform) is var (part, profile))
        {
            bool changed = !ReferenceEquals(profile, _config.Launcher) || Launcher is null;
            Launcher = part;
            _config.Select(profile);
            profile.ConfigureTurret(Turret);

            // A different weapon system carries a different number of rounds, so the magazine
            // is sized when one is first recognised rather than at construction.
            if (changed) _magazine.Resize(profile.TubeCount);
        }
        else
        {
            Launcher = null;
        }

        TurretPart = Launcher is null ? null : LauncherPart.FindTurret(Launcher, _profile);
        PodsPart = Launcher is null ? null : LauncherPart.FindPods(Launcher, _profile);
        RadarPart = Launcher is null ? null : LauncherPart.FindRadar(Launcher, _profile);
        MountEcl = LauncherPart.ResolveOriginEcl(Platform, Launcher);

        // After the launcher is resolved, not before: the part-relative modes read the part's own
        // mounting, and resolving them against last frame's launcher would point the cone at
        // whatever was fitted previously for one frame after a craft change.
        Boresight = ResolveBoresight();

        // Say what the launcher is actually made of, once. If the turret is never found, this
        // is the line that says whether the subpart Ids survived into the runtime unchanged.
        if (Launcher is not null && !_loggedSubParts)
        {
            _loggedSubParts = true;
            LauncherPart.FindMissiles(Launcher, _munition, _missileBodies);
            LauncherPart.FindFins(Launcher, _munition, _finBodies);
            Log.Info($"launcher subparts: {LauncherPart.DescribeSubParts(Launcher)}");
            Log.Debug($"round bodies found: {_missileBodies.Count}, fin sets {_finBodies.Count} (need {_profile.TubeCount})");
            if (TurretPart is null) Log.Warn("turret subpart not found - the turret will not slew");
            if (_missileBodies.Count == 0) Log.Warn("no round bodies - rounds will draw as tracers only");
        }

    }

    /// <summary>
    /// Advances the battery by <paramref name="dt"/> simulated seconds.
    ///
    /// <para>Separate from <see cref="SampleWorld"/> on purpose: this is gated on the simulation
    /// clock, so it does not run while paused or on a frame that advanced no time, whereas the
    /// world sample must run regardless. See <see cref="SampleWorld"/> for what conflating the
    /// two cost.</para>
    /// </summary>
    public void Update(double dt)
    {
        if (Platform is null) return;

        _clock += dt;

        Radar.Scan(Platform, Boresight, dt);
        AttributeRoundsToTracks();
        UpdateTurret(dt);

        // Rounds before fire control, so a round fired this frame is NOT integrated until the
        // next one.
        //
        // A round is created from the platform sample this update was handed, and everything
        // drawn from it is a difference against that sample: TravelSinceLaunch is
        // `OffsetFromPlatform - LaunchOffset`, and both terms are `roundPosition - platformSample`.
        // That cancels the platform's ~29.8 km/s of ecliptic motion only while the sample advances
        // alongside the round. Integrate a brand new round in its own launch frame and the sample
        // stands still for one step, so the round's *ecliptic* displacement lands in travel
        // instead of its local one - and because travel is a difference from launch, the error is
        // permanent rather than transient.
        //
        // Measured in game: travel reading 658.78 m at an age of 0.04 s on a round doing 124 m/s,
        // against 29800 * 0.022 = 656 m for one frame of ecliptic motion. The round bodies left
        // the tube that far out and stayed that far out for the whole flight, which is why the
        // launch point and the impact point were displaced by the same amount. The gizmo tracers
        // were unaffected, because they draw from the offset directly and never difference it
        // against launch - so for several rounds of testing the two renderers disagreed about
        // where the same round was.
        //
        // Firing after this call costs the new round one frame before it moves, which is correct
        // anyway: it is still in the tube on the frame the trigger is pulled.
        UpdateRounds(dt);
        UpdateFireControl(dt);
        TrimEvents();

        if (_config.DiagnosticDump)
        {
            Diagnostics.Tick(this, _config, _clock, _config.DiagnosticIntervalSeconds);
        }
    }

    /// <summary>
    /// Decides which craft the battery is mounted on.
    ///
    /// <para>The launcher is a physical part, so the battery belongs to the craft carrying it and
    /// stays there. It does not follow the player around: chasing control meant that taking the
    /// target's seat re-homed the battery onto the target, which then could not be shot at, and
    /// rounds already in flight had to be re-based mid-engagement.</para>
    ///
    /// <para>Preference order: an explicit pin, then the craft you are flying if it has a
    /// launcher, then whatever the battery is already on, then any loaded craft with one.
    /// Falls back to the controlled vehicle only when the part requirement is switched off.</para>
    /// </summary>
    private void ResolvePlatform()
    {
        if (PlatformPinned)
        {
            if (KsaWorld.IsAlive(Platform)) return;
            Announce("pinned platform lost");
            PlatformPinned = false;
        }

        // Flying a craft that carries a launcher: that is the one you mean.
        Vehicle? controlled = KsaWorld.ControlledVehicle;
        if (KsaWorld.IsAlive(controlled) && LauncherPart.IsMounted(controlled))
        {
            SetPlatform(controlled);
            return;
        }

        // Otherwise stay put, so switching away to watch does not move the battery.
        if (KsaWorld.IsAlive(Platform) && LauncherPart.IsMounted(Platform)) return;

        // The current platform is gone or lost its launcher; adopt any craft that has one.
        KsaWorld.CollectVehicles(_blastScratch);
        foreach (Vehicle v in _blastScratch)
        {
            if (LauncherPart.IsMounted(v))
            {
                SetPlatform(v);
                return;
            }
        }

        // No launcher anywhere. With the part requirement off the battery still works from the
        // hull of whatever you are flying, which is how it is tested without opening the editor.
        SetPlatform(_config.RequireLauncherPart ? null : controlled);
    }

    private void SetPlatform(Vehicle? v)
    {
        if (ReferenceEquals(Platform, v)) return;

        if (v is not null && Platform is not null)
        {
            Announce($"battery moved to {KsaWorld.DisplayName(v)}");
        }
        Platform = v;
    }

    /// <summary>
    /// Where the search cone points this frame.
    ///
    /// <para>Local "up" unless the sensor profile says otherwise, which is what a ground site wants
    /// and what this mod did unconditionally before <see cref="BoresightMode"/> existed. The
    /// part-relative modes exist for a launcher on something that manoeuvres: on a pitched-over
    /// booster or anything in orbit, "up" is not where the threats are.</para>
    ///
    /// <para>Every failure falls back to local up rather than to a zero vector — a cone with no
    /// direction sees nothing at all, and a battery that silently stops detecting is a much worse
    /// failure than one pointed conservatively at the sky.</para>
    /// </summary>
    private double3 ResolveBoresight()
    {
        if (Platform is not { } platform) return Boresight;

        if (Launcher is { } launcher
            && TubeGeometry.TryBoresightPartFrame(_profile, _config.Sensor.BoresightSource,
                                                  Turret.BearingRad, Turret.ElevationRad,
                                                  out double3 partFrame)
            && LauncherPart.TryLauncherDirectionEcl(platform, launcher, partFrame, out double3 ecl))
        {
            return ecl;
        }

        return KsaWorld.LocalUp(platform);
    }

    /// <summary>Tells each track how many rounds are already committed to it.</summary>
    private void AttributeRoundsToTracks()
    {
        foreach (Track t in Radar.Tracks) t.RoundsAssigned = 0;

        foreach (IProjectile round in _rounds)
        {
            if (round.TargetRef is not Vehicle target) continue;
            Track? t = Radar.Tracks.Find(x => ReferenceEquals(x.Vehicle, target));
            if (t is not null) t.RoundsAssigned++;
        }
    }

    private void UpdateFireControl(double dt)
    {
        if (_salvoTimer > 0.0) _salvoTimer = Math.Max(0.0, _salvoTimer - dt);

        // Reload cycle.
        if (_magazine.IsEmpty && _profile.ReloadSeconds > 0f)
        {
            if (_reloadTimer <= 0.0) _reloadTimer = _profile.ReloadSeconds;
            _reloadTimer -= dt;
            if (_reloadTimer <= 0.0)
            {
                _reloadTimer = 0.0;
                _magazine.RefillAll();
                Announce("launcher reloaded");
            }
            return;
        }

        if (!_config.AutoEngage || !_config.Armed || !IsOperational) return;
        if (Ammo <= 0 || _salvoTimer > 0.0) return;
        if (!Radar.HasFiringSolution) return;

        // Wait for the launcher to settle on the aim point. Auto-engage returns quietly rather
        // than announcing a refusal, because it will be back next frame.
        if (!IsLaid) return;

        Track target = Radar.Locked!;
        if (!ThreatModel.HasSalvoCapacity(target, _config.RoundsPerTarget)) return;

        // Detection reaches 36 km; the round reaches 20 km. Without this the battery empties
        // itself at contacts it cannot possibly catch, which is what every 8.7 km crossing shot
        // that expired at 22 s was doing.
        if (!ThreatModel.InEngagementEnvelope(target, _config.Sensor)) return;

        Fire(target);
    }

    /// <summary>
    /// Slews the turret onto whatever the radar is holding, and writes the result to the part.
    ///
    /// <para>Priority is the lock, then the most urgent threat, then rest. Following a threat
    /// before the lock has settled is what makes the vehicle look like it is *watching* the sky
    /// rather than reacting a second late.</para>
    ///
    /// <para>The bearing has to be computed in the part's own frame, not in Ecl: the turret
    /// rotates about the part's X axis, and the platform's own attitude is what relates the
    /// two.</para>
    /// </summary>
    private void UpdateTurret(double dt)
    {
        if (_config.TurretSpin)
        {
            // Command a bearing that runs away at the slew rate, so the turret chases it
            // forever. Nothing depends on this - it exists so "does the mesh move at all" can
            // be answered from across the launchpad.
            // Elevation still comes from the manual slider, so the two can be driven together:
            // spinning while pitching is the quickest way to see both axes composing properly.
            _spinPhase = Turret.WrapPi(_spinPhase + _profile.SlewRateRad * dt);
            Turret.Point(_spinPhase, double.DegreesToRadians(_config.TurretManualElevationDeg));
        }
        else if (_config.TurretManual)
        {
            Turret.Point(double.DegreesToRadians(_config.TurretManualBearingDeg),
                         double.DegreesToRadians(_config.TurretManualElevationDeg));
        }
        else if (!_config.TurretTracking)
        {
            Turret.Stow();
        }
        else
        {
            Track? aim = Radar.Locked ?? MostUrgentThreat();

            if (aim is not null && Platform is not null
                && LauncherPart.TryDirectionToPartFrame(Platform, aim.PositionEcl - MountEcl, out double3 partFrame))
            {
                Turret.Track(partFrame);
            }
            else
            {
                Turret.Stow();
            }
        }

        Turret.Update(dt, _profile.SlewRateRad, _profile.ElevationRateRad);

        if (!TurretDriveWorks) return;

        if (TurretPart is not null)
        {
            TurretDriveWorks = LauncherPart.TryApplyTurretBearing(TurretPart, Turret.BearingRad);
        }

        if (TurretDriveWorks && PodsPart is not null)
        {
            TurretDriveWorks = LauncherPart.TryApplyPodAim(PodsPart, _profile, Turret.BearingRad, Turret.ElevationRad);
        }

        // The search array turns regardless of what the battery is doing - it is looking, not
        // aiming - so it is driven off the clock rather than off the track.
        if (TurretDriveWorks && RadarPart is not null)
        {
            if (!_config.SearchRadarStopped)
            {
                RadarSpinRad = Turret.WrapPi(
                    RadarSpinRad + _profile.SearchRadarRpm * (Math.Tau / 60.0) * dt);
            }
            TurretDriveWorks = LauncherPart.TryApplyRadarSpin(RadarPart, _profile, Turret.BearingRad, RadarSpinRad);
        }

        if (!TurretDriveWorks) Announce("turret drive rejected by the engine; holding position");
    }

    /// <summary>
    /// Moves the round bodies to match the rounds the mod is simulating.
    ///
    /// <para>Each tube has a subpart of its own, hidden until that round is in the air. It is
    /// the same trick the turret uses — write the transform, reset the cache — applied to
    /// something that travels kilometres rather than turning on the spot, which is why the
    /// gizmo tracers stay available as a fallback.</para>
    ///
    /// <para>Rounds are indexed from one, so tube N is body N-1.</para>
    /// </summary>
    /// <para><b>Called every rendered frame, not every simulation step.</b> Writing a subpart
    /// transform is a drawing job, and the two cadences are not the same: the battery only
    /// steps when simulated time advances, so leaving this inside <see cref="Update"/> meant
    /// that any frame KSA rendered without advancing the clock left the bodies where they were
    /// while the camera and the world moved on. Rounds then hold still and jump, which is what
    /// "teleporting" looks like. Placement reads state and changes none, so running it more
    /// often than the simulation is free and correct.</para>
    public void SyncRoundBodies()
    {
        if (Platform is not { } platform || Launcher is not { } launcher) return;
        if (_missileBodies.Count == 0 || !RoundBodiesWork) return;

        // Switched off by the operator: hide every body so the tracers are what is seen, rather
        // than leaving twelve missiles frozen wherever they were last written.
        if (!_config.UseRoundBodies)
        {
            for (int i = 0; i < _missileBodies.Count; i++) LauncherPart.HideMissile(_missileBodies[i]);
            return;
        }

        Span<bool> flying = stackalloc bool[_profile.TubeCount];

        _bodyFrame++;
        bool trace = Log.Threshold <= Log.Level.Debug && _bodyFrame % BodyTraceEveryFrames == 0;

        foreach (IProjectile round in _rounds)
        {
            int index = round.Tube - 1;
            if (index < 0 || index >= _missileBodies.Count) continue;

            // Two rounds sharing one body would write it twice a frame and it would appear to
            // flip between their positions - a hard, fast zigzag. Tube numbers are meant to be
            // unique among rounds in the air, so if this ever fires it is the explanation.
            if (flying[index])
            {
                if (!_warnedDuplicateTube)
                {
                    _warnedDuplicateTube = true;
                    Log.Warn($"two rounds share tube {round.Tube}; their body will flicker between them");
                    Announce($"tube {round.Tube} double-booked - see the log");
                }
                continue;
            }

            // Point it along the flight path, falling back to straight up for a round that has
            // somehow stopped - better than the undefined direction of a zero vector.
            double3 heading = Vec.Len2(round.VelocityLocal) > 1e-6 ? round.VelocityLocal : Boresight;

            if (!LauncherPart.TryPlaceMissile(platform, launcher, _missileBodies[index],
                                              round.LaunchAnchorPartFrame, round.TravelSinceLaunch,
                                              heading, out double3 bodyPos, out doubleQuat bodyRot))
            {
                RoundBodiesWork = false;
                Announce("round bodies rejected by the engine; falling back to tracers");
                return;
            }
            flying[index] = true;




            // Fins ride the body's own transform and open over the first fraction of a second.
            if (FinsFor(index) is { } finSet)
            {
                LauncherPart.TryPlaceFins(finSet, bodyPos, bodyRot,
                                          round.FinDeployment(_munition), _munition);
            }

            // Everything the placement depends on, so a zigzag can be attributed rather than
            // guessed at: if travel is smooth but the drawn position is not, the fault is in the
            // frame conversion or in the engine; if travel itself jumps, it is the simulation.
            if (trace)
            {
                IProjectile r = round;

                // Range to the target and whether the seeker can still see it. Without these a
                // miss in the log is just a number at the end; with them the flight shows where
                // it stopped converging, and whether the seeker had dropped it by then.
                double tgtRange = -1.0;
                if (r.TargetRef is Vehicle tv && KsaWorld.IsAlive(tv))
                    tgtRange = Vec.Len(KsaWorld.PositionEcl(tv) - r.PositionEcl);

                // The drawn offset against the true one. OffsetFromPlatform is accumulated from
                // local velocity; PositionEcl - PlatformEcl is the same quantity taken directly.
                // They should agree. At detonation they have been seen 800 m apart while the
                // fuse and the blast agreed to the decimal, so the round is killing the target
                // and being rendered somewhere else entirely. This shows where that opens up.
                double drift = Vec.Len(r.OffsetFromPlatform - (r.PositionEcl - PlatformEcl));

                Log.Debug(() =>
                    $"body t{r.Tube}: [sim {KsaWorld.SimulationSpeed:F2}x " +
                    $"step {KsaWorld.SimStepSeconds * 1000.0:F1}ms " +
                    $"{(KsaWorld.IsPaused ? "PAUSED" : "running")}] " +
                    $"travel {Vec.Len(r.TravelSinceLaunch):F1} m " +
                    $"({r.TravelSinceLaunch.X:F1},{r.TravelSinceLaunch.Y:F1},{r.TravelSinceLaunch.Z:F1}) " +
                    $"anchor ({r.LaunchAnchorPartFrame.X:F2},{r.LaunchAnchorPartFrame.Y:F2},{r.LaunchAnchorPartFrame.Z:F2}) " +
                    $"localspeed {Vec.Len(r.VelocityLocal):F0} m/s age {r.Age:F2}s " +
                    $"tgt {(tgtRange < 0 ? "gone" : $"{tgtRange:F0} m")} " +
                    $"link {(r.SeekerInView ? "on" : "OFF")} " +
                    $"drift {drift:F1} m");
            }
        }

        // Every tube that is not in the air gets its body seated, spent or not, and only then are
        // the spent ones hidden. The plan comes from Magazine, where TubeVisual documents why
        // "hide without seating" is not one of the answers - it is the launch flash.
        for (int i = 0; i < _missileBodies.Count && i < _profile.TubeCount; i++)
        {
            TubeVisual plan = _magazine.Plan(i, flying[i]);
            if (!Magazine.RequiresSeating(plan)) continue;

            bool seated = PodsPart is { } loadedPods
                          && LauncherPart.TrySeatMissile(loadedPods, _profile, _missileBodies[i],
                                                         FinsFor(i), i, _munition);

            if (seated && Magazine.IsVisible(plan)) continue;

            LauncherPart.HideMissile(_missileBodies[i]);
            if (FinsFor(i) is { } spentFins) LauncherPart.HideMissile(spentFins);
        }
    }

    /// <summary>The threat the turret should be watching when there is no firing solution yet.</summary>
    private Track? MostUrgentThreat()
    {
        int i = ThreatModel.IndexOfMostUrgent(Radar.Tracks);
        return i >= 0 ? Radar.Tracks[i] : null;
    }

    /// <summary>
    /// Commits one round to <paramref name="track"/>. Returns false when the shot is refused,
    /// which the panel reports rather than failing silently.
    /// </summary>
    public bool Fire(Track track)
    {
        if (!_config.Armed) { Announce("refused: not armed"); return false; }
        if (Platform is null) { Announce("refused: no platform"); return false; }
        if (!IsOperational) { Announce("refused: no launcher part fitted"); return false; }
        if (Ammo <= 0) { Announce("refused: launcher empty"); return false; }
        if (!KsaWorld.IsAlive(track.Vehicle)) { Announce("refused: target gone"); return false; }
        if (!IsLaid) { Announce("refused: launcher still slewing"); return false; }

        // Takes the round as it picks the tube. Nothing between here and the round being added
        // can fail, so there is no window in which a tube is claimed but no round exists.
        if (!_magazine.TryTakeTube(_rounds, out int tube))
        {
            Announce("refused: no free tube");
            return false;
        }

        double3 platformVel = KsaWorld.VelocityEcl(Platform);

        // Leave from the tube itself, taken from where the pods are actually aimed. The ring
        // about the boresight below is only a fallback for a launcher with no pods fitted -
        // it ignores traverse and elevation entirely, which since the turret started moving
        // meant rounds appearing wherever the ring sat rather than at a tube mouth.
        double3 launchAnchorPartFrame = Vec.Zero;
        double3 tubeMouth = Vec.Zero;
        bool fromTube = Launcher is not null && PodsPart is { } pods
                        && LauncherPart.TryGetTubeMuzzleEcl(Platform, Launcher, pods, _profile, tube,
                                                            PlatformEcl, out tubeMouth)
                        // Seated, not at the mouth: the body mesh is modelled about its centre,
                        // so anchoring at the mouth starts the round half out of the tube. From
                        // the seated point it emerges as it accelerates, which is what a tube
                        // launch looks like.
                        && LauncherPart.TryGetSeatedPartFrame(pods, _profile, tube,
                                                              _munition.BodyLength,
                                                              out launchAnchorPartFrame);

        double3 launchPos = fromTube
            ? tubeMouth
            : Launcher is not null
                ? LauncherPart.MuzzleEcl(_profile, MountEcl, Boresight, tube)
                : MountEcl + Boresight * _profile.MuzzleOffset;

        // Leave along the tube. That is what a tube launcher does, and it is only possible now
        // that the pods genuinely aim: the round emerges pointing where the launcher is
        // pointing, and guidance takes it from there.
        //
        // The fallback below slews to the target and adds loft instead. That was the right
        // answer while the tubes were fixed pointing up - firing along local "up" put anything
        // low in the sky outside the seeker cone immediately - but against a laid launcher it
        // sends the round off at a visibly different angle to the tube it just came out of.
        double3 tubeAxis = Vec.Zero;
        bool alongTube = fromTube && _profile.LaunchAlongTube
                         && LauncherPart.TryGetTubeAxisEcl(Platform, Launcher!, PodsPart!, _profile, tube, out tubeAxis);

        double3 launchDir = FireGeometry.LaunchDirection(
            alongTube, tubeAxis, launchPos, track.PositionEcl, Boresight, _profile.LaunchLoft);

        double3 launchVel = platformVel + launchDir * _munition.LaunchSpeed;

        // platformVel is the frame the round launches into. Passing it here is what makes the body
        // orientable on its very first drawn frame - see the Interceptor constructor.
        _rounds.Add(new Interceptor(launchPos, launchVel, track.Vehicle, tube + 1, PlatformEcl, platformVel)
        {
            LaunchAnchorPartFrame = launchAnchorPartFrame,
        });
        _salvoTimer = _profile.SalvoSpacing;

        Announce($"round {tube + 1} away at {KsaWorld.DisplayName(track.Vehicle)} ({track.Range / 1000.0:F1} km)");
        return true;
    }

    /// <summary>Manual trigger: shoots at whatever the radar currently holds.</summary>
    public bool FireAtLock()
    {
        if (Radar.Locked is null) { Announce("refused: no lock"); return false; }
        return Fire(Radar.Locked);
    }

    public void Reload()
    {
        _magazine.Resize(_profile.TubeCount);
        _reloadTimer = 0.0;
        Announce("launcher reloaded by hand");
    }

    /// <summary>Removes every round in flight without detonating them.</summary>
    public void SafeAll()
    {
        int n = _rounds.Count;
        _rounds.Clear();
        if (n > 0) Announce($"{n} round(s) safed");
    }


    private void UpdateRounds(double dt)
    {
        if (_rounds.Count == 0) return;

        double3 platformVelocityEcl = KsaWorld.VelocityEcl(Platform!);

        for (int i = _rounds.Count - 1; i >= 0; i--)
        {
            IProjectile round = _rounds[i];
            double3 gravity = KsaWorld.GravityAt(Platform!, round.PositionEcl);

            // Read at the round's own position, not the platform's. A round climbing out of the
            // atmosphere leaves the air behind long before the launcher does, and that is the
            // whole point of scaling drag rather than fixing it per profile.
            double airDensity = KsaWorld.AirDensityRatioAt(Platform!, round.PositionEcl);

            // The platform's velocity defines the local frame: it carries the parent body's
            // orbital and rotational motion, which is not airspeed and not a heading.
            round.Update(dt, SampleTarget(round), gravity, platformVelocityEcl, PlatformEcl,
                         _munition, airDensity);


            switch (round.State)
            {
                case RoundState.Detonated:
                    Detonate(round);
                    _rounds.RemoveAt(i);
                    break;
                case RoundState.Expired:
                    // Report how it failed: converged-but-short reads very differently from
                    // never-converged, and the numbers say which.
                    Announce(
                        $"round {round.Tube} expired after {round.Age:F1}s - " +
                        $"closest {(round.ClosestApproach == double.MaxValue ? "n/a" : $"{round.ClosestApproach:F0} m")}, " +
                        $"flew {round.DistanceFlown / 1000.0:F1} km, final speed {round.Speed:F0} m/s, " +
                        $"lock={round.HasLock}");
                    _rounds.RemoveAt(i);
                    break;
            }
        }

        ApplyPendingKills();
    }

    /// <summary>
    /// Reads the round's target out of the world once per frame. Returns null when the target
    /// is gone, which breaks the round's lock and leaves it coasting.
    /// </summary>
    private TargetState? SampleTarget(IProjectile round)
    {
        if (round.TargetRef is not Vehicle target || !KsaWorld.IsAlive(target)) return null;

        // A command-linked round is steered from here, so it is only guided while the launcher
        // can still *see* what it is shooting at. Losing sight breaks the uplink and the round
        // coasts - the realistic failure mode for this weapon, and the one that replaces a
        // seeker being blinded. The fuse still works; see Interceptor.Step.
        //
        // Sight, not the track list. The track list has the operator's policy applied to it -
        // notably ProtectControlledVehicle - so testing against it meant that taking the
        // target's seat cut the uplink to every round already flying at it, turning a
        // deliberate safety rule into a guaranteed miss. The policy belongs at the kill, where
        // Detonate already declines and says why.
        if (_munition.Guidance == GuidanceMode.CommandLink && Platform is not null)
        {
            double3 toTarget = KsaWorld.PositionEcl(target) - PlatformEcl;
            if (!ThreatModel.InSensorVolume(toTarget, Boresight, _config.Sensor)) return null;
        }

        return new TargetState(
            KsaWorld.PositionEcl(target),
            KsaWorld.VelocityEcl(target),
            KsaWorld.MeanRadius(target));
    }

    /// <summary>
    /// Applies a warhead burst. KSA has no partial-damage model exposed, so the effect is
    /// binary: anything inside the lethal radius is destroyed, anything between lethal and
    /// blast radius is reported as a near miss and survives.
    /// </summary>
    private void Detonate(IProjectile round)
    {
        double3 burst = round.PositionEcl;
        Announce($"round {round.Tube} detonated, miss distance {round.MissDistance:F0} m");

        // Three measurements of the same event, because "the burst went off beside the drone"
        // needs a number to be actionable.
        //
        //   fuse    - what the fuse decided, between sub-steps. The kill is judged on this.
        //   atBurst - the same separation recomputed with the target advanced to the burst
        //             instant, exactly as the splash path below does it. Should match the fuse.
        //   drawn   - what the player sees: both positions as they are rendered, relative to the
        //             platform. The round from its accumulated local travel, the target from its
        //             frame-sampled position. If this is the one that disagrees, the simulation
        //             is right and the drawing is wrong.
        //
        // An earlier version of this line compared the round advanced into the frame against a
        // target sampled at the frame start, and reported a 73 m gap that was nothing but the
        // ecliptic velocity times 2.8 ms. Comparing across instants is the mistake this whole
        // file exists to avoid; do not reintroduce it here.
        if (round.TargetRef is Vehicle logTarget && KsaWorld.IsAlive(logTarget))
        {
            double intoFrame = round.DetonationElapsedInFrame;
            double3 targetEcl = KsaWorld.PositionEcl(logTarget);
            double3 targetVel = KsaWorld.VelocityEcl(logTarget);

            double atBurst = Vec.Len(targetEcl + targetVel * intoFrame - burst);
            double drawn = Vec.Len(round.OffsetFromPlatform - (targetEcl - PlatformEcl));

            // The separation as *rendered*. Everything above is the analytic frame the simulation
            // works in; KSA draws a vehicle at its physics position, which is not the same place.
            // KsaWorld.TryVehicleEgo says so outright: deriving a draw position from
            // GetPositionEcl "visibly misses the craft". If this number is large while fuse and
            // atBurst agree, the round is killing the target and being painted somewhere else -
            // which is exactly what has been reported.
            double onScreen = -1.0;
            if (KsaWorld.HasAnchor && KsaWorld.TryVehicleEgo(logTarget, out double3 targetEgo))
                onScreen = Vec.Len(KsaWorld.AnchorEgo + round.OffsetFromPlatform - targetEgo);

            Log.Debug(() =>
                $"  detonation: fuse {round.MissDistance:F1} m, atBurst {atBurst:F1} m, " +
                $"onScreen {(onScreen < 0 ? "n/a" : $"{onScreen:F1} m")}, " +
                $"drawn {drawn:F1} m; {intoFrame * 1000.0:F1}ms into the frame; " +
                $"sim {KsaWorld.SimulationSpeed:F2}x step {KsaWorld.SimStepSeconds * 1000.0:F1}ms");
        }

        // The round advanced through this frame's sub-steps; every vehicle's cached position is
        // from the frame start. Comparing the two directly puts the target kilometres away in
        // the ecliptic frame, so the blast finds nothing. Advance the world to match the burst.
        double elapsed = round.DetonationElapsedInFrame;

        // The intended target is settled by the fuse, which did the extrapolation properly.
        // Trust that number rather than re-deriving it.
        if (round.TargetRef is Vehicle intended && KsaWorld.IsAlive(intended))
        {
            double lethalRange = _munition.LethalRadius + KsaWorld.MeanRadius(intended);
            if (round.MissDistance <= lethalRange)
            {
                // Say why a lethal hit did not kill. Taking control of the target makes it
                // immune, which looks exactly like the round missing unless we announce it.
                if (ReferenceEquals(intended, Platform))
                {
                    Announce($"hit on {KsaWorld.DisplayName(intended)} ignored - it is now the battery's own platform");
                }
                else if (_config.ProtectControlledVehicle && ReferenceEquals(intended, KsaWorld.ControlledVehicle))
                {
                    Announce($"hit on {KsaWorld.DisplayName(intended)} ignored - you are flying it (untick 'Never target the vehicle I'm flying')");
                }
                else if (!_pendingKills.Contains(intended))
                {
                    _pendingKills.Add(intended);
                }
            }
        }

        KsaWorld.CollectVehicles(_blastScratch);

        foreach (Vehicle v in _blastScratch)
        {
            if (ReferenceEquals(v, Platform)) continue;
            if (_config.ProtectControlledVehicle && ReferenceEquals(v, KsaWorld.ControlledVehicle)) continue;
            if (_pendingKills.Contains(v)) continue;

            double3 posAtBurst = KsaWorld.PositionEcl(v) + KsaWorld.VelocityEcl(v) * elapsed;
            double dist = Vec.Len(posAtBurst - burst) - KsaWorld.MeanRadius(v);

            if (dist <= _munition.LethalRadius)
            {
                _pendingKills.Add(v);
            }
            else if (dist <= _munition.BlastRadius)
            {
                Announce($"near miss on {KsaWorld.DisplayName(v)} at {dist:F0} m");
            }
        }
    }

    /// <summary>
    /// Destroys queued targets after the blast sweep, so we never mutate the engine's
    /// vehicle collection while walking it.
    /// </summary>
    private void ApplyPendingKills()
    {
        if (_pendingKills.Count == 0) return;

        // Join the engine's vehicle solvers before disposing anything. Destroying a vehicle removes
        // it from the list those worker jobs are enumerating, and this hook runs while they are
        // live - see KsaWorld.WaitForVehicleSolvers for the frame order that makes that unavoidable
        // without the barrier.
        //
        // Taken once for the whole batch rather than per kill, which is most of why kills are
        // deferred into this list in the first place.
        KsaWorld.WaitForVehicleSolvers();

        foreach (Vehicle v in _pendingKills)
        {
            if (!KsaWorld.IsAlive(v)) continue;
            Announce($"destroyed {KsaWorld.DisplayName(v)}");
            KsaWorld.Destroy(v, blastSeverity: 50f);
        }

        // Any round still chasing a corpse loses its lock rather than flying at a dangling ref.
        _pendingKills.Clear();
    }

    private void Announce(string message)
    {
        _events.Add(new BatteryEvent(_clock, message));
        Log.Info(message);
    }

    private void TrimEvents()
    {
        const int keep = 12;
        if (_events.Count > keep) _events.RemoveRange(0, _events.Count - keep);
    }

    /// <summary>
    /// Drops everything in flight and forgets what the radar was holding, without touching the
    /// platform, the magazine or the player's settings.
    ///
    /// <para>Called when more simulated time passed than can be integrated — heavy timewarp, or
    /// a load that replaced the clock. Rounds mid-flight relate to a world that no longer
    /// exists, and stepping them by a huge delta would fly them through their targets. Tracking
    /// is cleared too so dwell restarts rather than granting an instant firing solution off
    /// time that was never simulated.</para>
    /// </summary>
    public void AbandonFlight(string why)
    {
        bool hadRounds = _rounds.Count > 0;

        _rounds.Clear();
        _pendingKills.Clear();
        Radar.Reset();
        _salvoTimer = 0.0;
        _warnedDuplicateTube = false;

        // Hide the round bodies that were riding those interceptors, or they freeze mid-air.
        for (int i = 0; i < _missileBodies.Count; i++) LauncherPart.HideMissile(_missileBodies[i]);

        if (hadRounds) Announce($"rounds abandoned: {why}");
        else Log.Debug(() => $"tracking reset: {why}");
    }

    public void Reset()
    {
        _rounds.Clear();
        _pendingKills.Clear();
        _events.Clear();
        Radar.Reset();
        _magazine.Resize(_profile.TubeCount);
        _salvoTimer = 0.0;
        _reloadTimer = 0.0;
        PlatformPinned = false;
        Platform = null;
    }
}
