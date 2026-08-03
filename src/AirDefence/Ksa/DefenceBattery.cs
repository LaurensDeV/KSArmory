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
    private readonly List<Interceptor> _rounds = [];
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
    public int Ammo { get; private set; }

    public IReadOnlyList<Interceptor> Rounds => _rounds;

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

    private bool _loggedSubParts;
    private double _spinPhase;
    private readonly List<Part> _missileBodies = [];

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

    public void Update(double dt)
    {
        _clock += dt;

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

        PlatformEcl = KsaWorld.PositionEcl(Platform);
        Boresight = KsaWorld.LocalUp(Platform);
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
            if (changed) Ammo = profile.TubeCount;
        }
        else
        {
            Launcher = null;
        }

        TurretPart = Launcher is null ? null : LauncherPart.FindTurret(Launcher, _profile);
        PodsPart = Launcher is null ? null : LauncherPart.FindPods(Launcher, _profile);
        RadarPart = Launcher is null ? null : LauncherPart.FindRadar(Launcher, _profile);
        MountEcl = LauncherPart.ResolveOriginEcl(Platform, Launcher);

        // Say what the launcher is actually made of, once. If the turret is never found, this
        // is the line that says whether the subpart Ids survived into the runtime unchanged.
        if (Launcher is not null && !_loggedSubParts)
        {
            _loggedSubParts = true;
            LauncherPart.FindMissiles(Launcher, _munition, _missileBodies);
            Log.Info($"launcher subparts: {LauncherPart.DescribeSubParts(Launcher)}");
            Log.Debug($"round bodies found: {_missileBodies.Count} (need {_profile.TubeCount})");
            if (TurretPart is null) Log.Warn("turret subpart not found - the turret will not slew");
            if (_missileBodies.Count == 0) Log.Warn("no round bodies - rounds will draw as tracers only");
        }

        Radar.Scan(Platform, Boresight, dt);
        AttributeRoundsToTracks();
        UpdateTurret(dt);
        UpdateFireControl(dt);
        UpdateRounds(dt);
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

    /// <summary>Tells each track how many rounds are already committed to it.</summary>
    private void AttributeRoundsToTracks()
    {
        foreach (Track t in Radar.Tracks) t.RoundsAssigned = 0;

        foreach (Interceptor round in _rounds)
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
        if (Ammo == 0 && _profile.ReloadSeconds > 0f)
        {
            if (_reloadTimer <= 0.0) _reloadTimer = _profile.ReloadSeconds;
            _reloadTimer -= dt;
            if (_reloadTimer <= 0.0)
            {
                _reloadTimer = 0.0;
                Ammo = _profile.TubeCount;
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

        foreach (Interceptor round in _rounds)
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
                                              heading))
            {
                RoundBodiesWork = false;
                Announce("round bodies rejected by the engine; falling back to tracers");
                return;
            }
            flying[index] = true;

            // Everything the placement depends on, so a zigzag can be attributed rather than
            // guessed at: if travel is smooth but the drawn position is not, the fault is in the
            // frame conversion or in the engine; if travel itself jumps, it is the simulation.
            if (trace)
            {
                Interceptor r = round;
                Log.Debug(() =>
                    $"body t{r.Tube}: travel {Vec.Len(r.TravelSinceLaunch):F1} m " +
                    $"({r.TravelSinceLaunch.X:F1},{r.TravelSinceLaunch.Y:F1},{r.TravelSinceLaunch.Z:F1}) " +
                    $"anchor ({r.LaunchAnchorPartFrame.X:F2},{r.LaunchAnchorPartFrame.Y:F2},{r.LaunchAnchorPartFrame.Z:F2}) " +
                    $"localspeed {Vec.Len(r.VelocityLocal):F0} m/s age {r.Age:F2}s");
            }
        }

        for (int i = 0; i < _missileBodies.Count && i < _profile.TubeCount; i++)
        {
            if (!flying[i]) LauncherPart.HideMissile(_missileBodies[i]);
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

        int tube = _profile.TubeCount - Ammo;

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
                        && LauncherPart.TryGetTubeMuzzlePartFrame(pods, _profile, tube,
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
                         && LauncherPart.TryGetTubeAxisEcl(Platform, Launcher!, PodsPart!, _profile, out tubeAxis);

        double3 launchDir = FireGeometry.LaunchDirection(
            alongTube, tubeAxis, launchPos, track.PositionEcl, Boresight, _profile.LaunchLoft);

        double3 launchVel = platformVel + launchDir * _munition.LaunchSpeed;

        _rounds.Add(new Interceptor(launchPos, launchVel, track.Vehicle, tube + 1, PlatformEcl)
        {
            LaunchAnchorPartFrame = launchAnchorPartFrame,
        });
        Ammo--;
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
        Ammo = _profile.TubeCount;
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
            Interceptor round = _rounds[i];
            double3 gravity = KsaWorld.GravityAt(Platform!, round.PositionEcl);

            // The platform's velocity defines the local frame: it carries the parent body's
            // orbital and rotational motion, which is not airspeed and not a heading.
            round.Update(dt, SampleTarget(round), gravity, platformVelocityEcl, PlatformEcl, _munition);

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
    private static TargetState? SampleTarget(Interceptor round)
    {
        if (round.TargetRef is not Vehicle target || !KsaWorld.IsAlive(target)) return null;

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
    private void Detonate(Interceptor round)
    {
        double3 burst = round.PositionEcl;
        Announce($"round {round.Tube} detonated, miss distance {round.MissDistance:F0} m");

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
        Ammo = _profile.TubeCount;
        _salvoTimer = 0.0;
        _reloadTimer = 0.0;
        PlatformPinned = false;
        Platform = null;
    }
}
