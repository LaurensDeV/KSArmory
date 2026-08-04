using Brutal.Numerics;
using KSA;

namespace KSArmory;

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

    /// <summary>The cannon, which pitch with the launcher. Null if this system carries none.</summary>
    public Part? GunsPart { get; private set; }

    /// <summary>The optical head, which points at whatever the battery is watching.</summary>
    public Part? OpticPart { get; private set; }

    /// <summary>Where the head is looking, in the launcher part's frame.</summary>
    public double3 OpticDirectionPartFrame => _optic.Direction;

    /// <summary>True once the head has caught up with what it was told to look at.</summary>
    public bool OpticOnTarget => _optic.OnTarget;

    /// <summary>The search array's current angle. Cosmetic - the radar model is a cone search.</summary>
    public double RadarSpinRad { get; private set; }

    /// <summary>Azimuth drive state. Pure maths, no KSA types — see <see cref="Turret"/>.</summary>
    public Turret Turret { get; } = new();

    // Which drives the engine is still accepting writes for, latched per assembly.
    private DriveStatus _drives;

    /// <summary>True while the engine still accepts writes for this assembly.</summary>
    public bool DriveWorks(DriveChannel channel) => _drives.Works(channel);

    /// <summary>True once the engine has refused any drive on this launcher.</summary>
    public bool AnyDriveRefused => _drives.AnyRefused;

    // The weapon system this battery is running, and what it fires. Read through the config rather
    // than captured at construction: the battery does not know which launcher it has until it finds
    // the part, and the panel can retune the profile while an engagement is under way.
    private LauncherProfile _profile => _config.Launcher;
    private MunitionProfile _munition => _config.Munition;

    /// <summary>How far the platform moved between the last two frames (m, Ecl).</summary>
    public double3 PlatformStepEcl { get; private set; }

    // Which launcher on the platform this battery runs, by part order. One battery per craft, so
    // it is always the first; keying on the ordinal rather than the Part reference is what
    // survives KSA rebuilding the part tree during staging and docking.
    private const int LauncherOrdinal = 0;
    private readonly List<(Part, LauncherProfile)> _launcherScratch = [];

    private bool _hasPlatformSample;
    private bool _loggedSubParts;
    private double _spinPhase;
    private readonly List<Part> _missileBodies = [];
    private readonly List<Part> _finBodies = [];

    // Which tubes still hold a round, and which fires next. See Magazine — the bookkeeping is pure
    // and lives in Sim/ so it is testable, because getting it wrong produces a salvo that looks
    // like it never left rather than an error.
    private readonly Magazine _magazine = new();

    // The cannon's belt and burst timing. A second weapon on the same mount, sharing the
    // platform, the sensor and the aim, and differing in what it throws and how far.
    // Where the optical head is looking. Rate-limited, so it sweeps onto a track rather than
    // snapping to it the frame the radar produces one.
    private readonly PointingDrive _optic = new();

    private readonly GunChannel _guns = new();
    private int _nextBarrel;
    private double _gunTrace;
    private double _gunReloadTimer;

    /// <summary>Rounds left in the cannon belt.</summary>
    public int GunAmmo => _guns.Ammo;

    /// <summary>True while the cannon are mid-burst, so the panel can say so.</summary>
    public bool GunsFiring => _guns.Firing;

    // The fin set belonging to a tube, or null if the launcher carries none.
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

    // Trace one frame in this many, so a debug log stays readable at 60 fps.
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
    /// True when the launcher is actually pointing where it is about to shoot, so rounds do not
    /// leave tubes aimed somewhere else.
    ///
    /// <para>Always true when nothing is being laid — tracking off, driven from the panel, or a
    /// launcher whose profile declares no training gear — so it cannot deadlock fire control on
    /// a launcher that will never move. A launcher that <em>should</em> move and cannot is a
    /// different case, and holds fire. See <see cref="FireGate"/>.</para>
    /// </summary>
    public bool IsLaid => FireGate.IsLaid(
        aiming: _config.TurretTracking && !_config.TurretManual && !_config.TurretSpin,
        trains: _profile.Trains,
        drivesAccepted: _drives.AimingAccepted,
        assembliesResolved: _profile.PodsMarker is null || PodsPart is not null,
        settled: Turret.IsLaid(_profile.SettleSeconds));

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
    /// <para>Sets <see cref="PlatformEcl"/>, <see cref="Boresight"/> and <see cref="MountEcl"/> —
    /// the frame of reference the whole overlay is drawn against. <see cref="DrawAnchor"/> pairs
    /// <see cref="PlatformEcl"/> with an Ego position sampled fresh every frame; if this half goes
    /// stale while that one does not, the pair no longer describes one instant and the overlay
    /// slides off the craft.</para>
    ///
    /// <para>Sampling only: reads the world, resolves parts, advances nothing.</para>
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

        // Rounds store position relative to the platform, so a change of platform has to be
        // announced: their offsets are now measured from somewhere else.
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
        if (LauncherPart.FindNth(Platform, LauncherOrdinal, _launcherScratch) is var (part, profile))
        {
            bool changed = !ReferenceEquals(profile, _config.Launcher) || Launcher is null;
            Launcher = part;
            _config.Select(profile);
            profile.ConfigureTurret(Turret);

            // The set is fitted to this launcher, so it filters on that system's sensor rather
            // than on whichever one the config points at.
            Radar.Sensor = _config.Sensor;

            // A different weapon system carries a different number of rounds, so the magazine
            // is sized when one is first recognised rather than at construction.
            if (changed)
            {
                _magazine.Resize(profile.TubeCount, profile.MagazineDepth);
                _guns.Fill(profile.GunAmmo);
                _nextBarrel = 0;
            }
        }
        else
        {
            Launcher = null;
        }

        TurretPart = Launcher is null ? null : LauncherPart.FindTurret(Launcher, _profile);
        PodsPart = Launcher is null ? null : LauncherPart.FindPods(Launcher, _profile);
        RadarPart = Launcher is null ? null : LauncherPart.FindRadar(Launcher, _profile);
        GunsPart = Launcher is null ? null : LauncherPart.FindGuns(Launcher, _profile);
        OpticPart = Launcher is null ? null : LauncherPart.FindOptic(Launcher, _profile);
        MountEcl = LauncherPart.ResolveOriginEcl(Platform, Launcher);

        // After the launcher is resolved: the part-relative modes read the part's own mounting.
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

        // Rounds before fire control, so a round fired this frame is not integrated until the
        // next one. TravelSinceLaunch differences two platform-relative offsets, which cancels the
        // platform's ~29.8 km/s only while the sample advances alongside the round. Integrating a
        // new round in its own launch frame leaves the sample still for one step, so a frame of
        // ecliptic motion lands in travel permanently - measured at 658.78 m of travel at an age
        // of 0.04 s on a round doing 124 m/s.
        //
        // The cost is one frame before a new round moves, which is correct anyway: it is still in
        // the tube on the frame the trigger is pulled.
        UpdateRounds(dt);
        UpdateFireControl(dt);
        UpdateGunFireControl(dt);
        TrimEvents();

        if (_config.DiagnosticDump)
        {
            Diagnostics.Tick(this, _config, _clock, _config.DiagnosticIntervalSeconds);
        }
    }

    // Decides which craft the battery is mounted on. The launcher is a physical part, so the
    // battery belongs to the craft carrying it and stays there rather than following control.
    // Preference order: an explicit pin, then the craft you are flying if it has a launcher, then
    // whatever the battery is already on, then any loaded craft with one. Falls back to the
    // controlled vehicle only when the part requirement is switched off.
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

    // Where the search cone points this frame. Local "up" unless the sensor says otherwise, which
    // is what a ground site wants; the part-relative modes are for a launcher on something that
    // manoeuvres. Every failure falls back to local up rather than a zero vector, because a cone
    // with no direction detects nothing at all.
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

    // Tells each track how many rounds are already committed to it.
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
        if (!_config.MissilesEnabled) return;
        if (Ammo <= 0 || _salvoTimer > 0.0) return;
        if (!Radar.HasFiringSolution) return;

        // Wait for the launcher to settle on the aim point. Auto-engage returns quietly rather
        // than announcing a refusal, because it will be back next frame.
        if (!IsLaid) return;

        Track target = Radar.Locked!;
        if (!ThreatModel.MayEngage(target, _config.Iff)) return;
        if (!ThreatModel.HasSalvoCapacity(target, _config.RoundsPerTarget)) return;

        // Detection reaches 36 km; the round reaches 20 km. Without this the battery empties
        // itself at contacts it cannot possibly catch, which is what every 8.7 km crossing shot
        // that expired at 22 s was doing.
        if (!ThreatModel.InEngagementEnvelope(target, _config.Sensor)) return;

        Fire(target);
    }

    // Sends one shell down whichever barrel is next in the cycle.
    //
    // Which way the optical head looks, in the launcher part's frame: at the locked contact if
    // there is one, otherwise along the turret's own facing.
    private double3 OpticAimPartFrame()
    {
        if (Radar.Locked is { } locked && Platform is not null
            && LauncherPart.TryDirectionToPartFrame(Platform, locked.PositionEcl - MountEcl,
                                                    out double3 toTarget))
        {
            return toTarget;
        }

        return TubeGeometry.TurretRotation(Turret.BearingRad) * TubeGeometry.OpticRestDirection;
    }

    // Where the turret points: the target itself while the missiles have the engagement, and a
    // ballistic solution once the cannon do.
    //
    // One traverse ring serves both weapons, so it can only solve for one at a time — the same
    // choice a real fire-control computer makes. A missile steers and needs only to be pointed;
    // a shell arrives where it was thrown, and at 4 km it flies for 4.5 s, during which a 300 m/s
    // target crosses 1.4 km and the round falls 100 m. Laying on the target itself misses by
    // both of those together.
    private double3 AimPointEcl(Track aim)
    {
        if (!GunsHaveTheEngagement(aim)) return aim.PositionEcl;

        MunitionProfile shell = Arsenal.MunitionNamed(_profile.GunMunition!);
        double3 gravity = Platform is null ? Vec.Zero : KsaWorld.GravityAt(Platform, MountEcl);

        // Relative to the platform, not absolute: the round is launched with the platform's
        // velocity already in it, so the only motion it has to lead is the difference.
        double3 relative = aim.VelocityEcl - KsaWorld.VelocityEcl(Platform!);

        return BallisticLead.TrySolve(MountEcl, aim.PositionEcl, relative,
                                      shell.LaunchSpeed, gravity, out double3 lead)
            ? lead
            : aim.PositionEcl;
    }

    // Whether the cannon are the weapon this engagement belongs to: inside their envelope, and
    // with the missiles either switched off or unable to reach.
    private bool GunsHaveTheEngagement(Track aim)
        => _profile.HasCannon && _config.GunsEnabled && !_guns.IsEmpty
           && aim.Range >= _profile.GunMinRange && aim.Range <= _profile.GunMaxRange;

    // Fired along the barrel: the lead is in where the turret is pointing, so aiming the shell
    // itself as well would apply it twice.
    private void FireGun(Track track)
    {
        if (Platform is null || Launcher is null || GunsPart is not { } guns) return;

        int barrel = _nextBarrel % _profile.GunMuzzles.Length;
        _nextBarrel = (_nextBarrel + 1) % _profile.GunMuzzles.Length;

        if (!LauncherPart.TryGetGunMuzzleEcl(Platform, Launcher, guns, _profile, barrel,
                                             PlatformEcl, out double3 muzzle, out double3 axis))
        {
            return;
        }

        MunitionProfile shell = Arsenal.MunitionNamed(_profile.GunMunition!);
        double3 platformVel = KsaWorld.VelocityEcl(Platform);

        // Negative tube numbers mark the cannon: the magazine owns 0..TubeCount-1, and a shell
        // must never be mistaken for a missile that could claim a tube back.
        _rounds.Add(new Slug(muzzle, platformVel + axis * shell.LaunchSpeed, track.Vehicle,
                             -(barrel + 1), PlatformEcl, platformVel)
        {
            Munition = shell,
            Aimpoint = Aimpoint.OnVehicle(track.Vehicle, track.PositionEcl, track.VelocityEcl,
                                          KsaWorld.MeanRadius(track.Vehicle)),
        });
    }

    // The cannon, which run on their own belt and their own envelope.
    //
    // Deliberately not gated on the missile channel's state. The two cover each other: the
    // missiles need 1.2 km to arm and steer, so anything closer is the cannon's problem and stays
    // untouchable if the guns wait for the launcher to run dry.
    private void UpdateGunFireControl(double dt)
    {
        if (!_profile.HasCannon)
        {
            _gunTrace += dt;
            if (_gunTrace >= 5.0)
            {
                _gunTrace = 0.0;
                Log.Debug(() => $"cannon: none on {_profile.DisplayName} "
                                + $"(munition={_profile.GunMunition ?? "null"} "
                                + $"barrels={_profile.GunMuzzles.Length})");
            }
            return;
        }

        bool wantToFire = _config.AutoEngage && _config.Armed && _config.GunsEnabled
                          && IsOperational && IsLaid
                          && _drives.Works(DriveChannel.Guns)
                          && Radar.Locked is { } locked
                          && ThreatModel.MayEngage(locked, _config.Iff)
                          && locked.Range >= _profile.GunMinRange
                          && locked.Range <= _profile.GunMaxRange;

        // Say why the cannon are silent. Every gate below is invisible from outside, and "no
        // shooting" is the same symptom for all of them.
        _gunTrace += dt;
        if (_gunTrace >= 1.0)
        {
            _gunTrace = 0.0;
            Log.Debug(() =>
            {
                string range = Radar.Locked is { } t ? $"{t.Range:F0} m" : "no lock";
                return $"cannon: want={wantToFire} ammo={_guns.Ammo} burst={_guns.BurstRemaining} "
                       + $"cd={_guns.Cooldown:F3} armed={_config.Armed} auto={_config.AutoEngage} "
                       + $"enabled={_config.GunsEnabled} laid={IsLaid} drive={_drives.Works(DriveChannel.Guns)} "
                       + $"part={(GunsPart is not null)} range={range} "
                       + $"envelope={_profile.GunMinRange:F0}-{_profile.GunMaxRange:F0} m";
            });
        }

        // Belt resupply, on its own timer: the launcher's reload gate returns early when the
        // tubes are empty, so a shared one would leave the cannon dry for as long as the
        // missiles took to come back.
        if (_guns.IsEmpty && _profile.GunReloadSeconds > 0f)
        {
            _gunReloadTimer += dt;
            if (_gunReloadTimer >= _profile.GunReloadSeconds)
            {
                _gunReloadTimer = 0.0;
                _guns.Fill(_profile.GunAmmo);
                Announce("cannon belt replaced");
            }
            return;
        }
        _gunReloadTimer = 0.0;

        int fired = _guns.Step(dt, wantToFire, _profile);
        if (fired <= 0) return;

        for (int i = 0; i < fired; i++) FireGun(Radar.Locked!);
        Log.Debug(() => $"cannon: {fired} round(s) away, {_guns.Ammo} left");
        if (_guns.IsEmpty) Announce("cannon belt empty");
    }

    // Slews the turret onto whatever the radar is holding, and writes the result to the part.
    // Priority is the lock, then the most urgent threat, then rest. Following a threat before the
    // lock has settled is what makes the vehicle look like it is *watching* the sky rather than
    // reacting a second late. The bearing has to be computed in the part's own frame, not in Ecl:
    // the turret rotates about the part's X axis, and the platform's own attitude is what relates
    // the two.
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
                && LauncherPart.TryDirectionToPartFrame(Platform, AimPointEcl(aim) - MountEcl, out double3 partFrame))
            {
                Turret.Track(partFrame);
            }
            else
            {
                Turret.Stow();
            }
        }

        Turret.Update(dt, _profile.SlewRateRad, _profile.ElevationRateRad);

        // Each assembly latches on its own refusal. The drive keeps integrating either way, so the
        // drawn facing line goes on showing where the battery believes it is pointing — which is
        // the only thing that distinguishes a refused write from a wrong solution.
        if (TurretPart is not null && _drives.Works(DriveChannel.Turret)
            && !LauncherPart.TryApplyTurretBearing(TurretPart, Turret.BearingRad))
        {
            Refuse(DriveChannel.Turret, "turret traverse");
        }

        if (PodsPart is not null && _drives.Works(DriveChannel.Pods)
            && !LauncherPart.TryApplyPodAim(PodsPart, _profile, Turret.BearingRad, Turret.ElevationRad))
        {
            Refuse(DriveChannel.Pods, "pod elevation");
        }

        // The cannon follow the same aim as the pods: one turret, one solution.
        if (GunsPart is not null && _drives.Works(DriveChannel.Guns)
            && !LauncherPart.TryApplyGunAim(GunsPart, _profile, Turret.BearingRad, Turret.ElevationRad))
        {
            Refuse(DriveChannel.Guns, "cannon elevation");
        }

        // The optical head points at what the battery is watching, and falls back to wherever
        // the turret faces so it never sits skewed across the hull with nothing to look at.
        _optic.Update(dt, OpticAimPartFrame(), _profile.OpticSlewRateRad);

        if (OpticPart is not null && _drives.Works(DriveChannel.Optic)
            && !LauncherPart.TryApplyOpticAim(OpticPart, _profile, Turret.BearingRad,
                                              _optic.Direction))
        {
            Refuse(DriveChannel.Optic, "optical head");
        }

        // The search array turns regardless of what the battery is doing - it is looking, not
        // aiming - so it is driven off the clock rather than off the track.
        if (RadarPart is not null && _drives.Works(DriveChannel.Radar))
        {
            if (!_config.SearchRadarStopped)
            {
                RadarSpinRad = Turret.WrapPi(
                    RadarSpinRad + _profile.SearchRadarRpm * (Math.Tau / 60.0) * dt);
            }
            if (!LauncherPart.TryApplyRadarSpin(RadarPart, _profile, Turret.BearingRad, RadarSpinRad))
            {
                Refuse(DriveChannel.Radar, "search array spin");
            }
        }
    }

    private void Refuse(DriveChannel channel, string what)
    {
        if (_drives.Refuse(channel)) Announce($"{what} rejected by the engine; that assembly is frozen");
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
    /// transform is a drawing job: the battery only steps when simulated time advances, so a frame
    /// rendered without a step would leave the bodies behind while the world moved on. Placement
    /// reads state and changes none, so running it more often than the simulation is free.</para>
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

            // Tube numbers are unique among rounds in the air. Two sharing one body would write
            // it twice a frame and it would flip between their positions.
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

        // Every tube not in the air is seated first, spent or not, and only then hidden. See
        // TubeVisual for why "hide without seating" is not one of the options.
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

    // The threat the turret should be watching when there is no firing solution yet.
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
        if (!ThreatModel.MayEngage(track, _config.Iff))
        {
            Announce($"refused: {KsaWorld.DisplayName(track.Vehicle)} is {track.Allegiance}");
            return false;
        }
        if (!IsLaid) { Announce("refused: launcher still slewing"); return false; }

        // Takes the round as it picks the tube. Nothing between here and the round being added
        // can fail, so a tube is never claimed without a round.
        if (!_magazine.TryTakeTube(_rounds, out int tube))
        {
            Announce("refused: no free tube");
            return false;
        }

        double3 platformVel = KsaWorld.VelocityEcl(Platform);

        // From the tube itself, using where the pods are aimed. The ring about the boresight
        // below is a fallback for a launcher with no pods: it ignores traverse and elevation.
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

        // Along the tube, so the round emerges pointing where the launcher points. The fallback
        // slews to the target and adds loft instead, which is what a launcher with fixed tubes
        // needs and what a laid one must not do.
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
            Aimpoint = Aimpoint.OnVehicle(track.Vehicle, track.PositionEcl, track.VelocityEcl,
                                          KsaWorld.MeanRadius(track.Vehicle)),
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
        _magazine.Resize(_profile.TubeCount, _profile.MagazineDepth);
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
            double mediumDensity = KsaWorld.MediumDensityRatioAt(Platform!, round.PositionEcl);

            // The platform's velocity defines the local frame: it carries the parent body's
            // orbital and rotational motion, which is not airspeed and not a heading.
            round.Update(dt, SampleTarget(round), gravity, platformVelocityEcl, PlatformEcl,
                         round.Munition, mediumDensity);


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

    // Reads the round's target out of the world once per frame. Returns null when the target is
    // gone, which breaks the round's lock and leaves it coasting.
    private TargetState? SampleTarget(IProjectile round)
    {
        // A fixed position needs nothing from the world and can never be lost, so a round aimed
        // at a coordinate keeps its aimpoint until it arrives or expires.
        if (round.Aimpoint.Kind == AimpointKind.Point) return round.Aimpoint.ToTargetState();

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
        if (round.Munition.Guidance == GuidanceMode.CommandLink && Platform is not null)
        {
            double3 toTarget = KsaWorld.PositionEcl(target) - PlatformEcl;
            if (!ThreatModel.InSensorVolume(toTarget, Boresight, _config.Sensor)) return null;
        }

        return new TargetState(
            KsaWorld.PositionEcl(target),
            KsaWorld.VelocityEcl(target),
            KsaWorld.MeanRadius(target));
    }

    // Applies a warhead burst. KSA has no partial-damage model exposed, so the effect is binary:
    // anything inside the lethal radius is destroyed, anything between lethal and blast radius is
    // reported as a near miss and survives.
    private void Detonate(IProjectile round)
    {
        // Only a whole craft can be destroyed. KSA exposes no component damage, so a round aimed
        // at a part or a coordinate arrives, reports, and destroys nothing.
        if (round.Aimpoint.Kind != AimpointKind.Vehicle && round.Aimpoint.Handle is not null)
        {
            Announce($"round {round.Tube} arrived at its {round.Aimpoint.Kind} aimpoint");
            return;
        }

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
            double lethalRange = round.Munition.LethalRadius + KsaWorld.MeanRadius(intended);
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

            if (dist <= round.Munition.LethalRadius)
            {
                _pendingKills.Add(v);
            }
            else if (dist <= round.Munition.BlastRadius)
            {
                Announce($"near miss on {KsaWorld.DisplayName(v)} at {dist:F0} m");
            }
        }
    }

    // Destroys queued targets after the blast sweep, so we never mutate the engine's vehicle
    // collection while walking it.
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
        _magazine.Resize(_profile.TubeCount, _profile.MagazineDepth);
        _salvoTimer = 0.0;
        _reloadTimer = 0.0;
        PlatformPinned = false;
        Platform = null;

        // The latches record what one vehicle's part tree refused. A different platform gets a
        // fresh assessment rather than inheriting the last one's failures.
        _drives.Clear();
        RoundBodiesWork = true;
        OpticPart = null;
        _optic.Reset();
        _guns.Fill(_profile.GunAmmo);
        _guns.Reset();
        _nextBarrel = 0;
        _gunReloadTimer = 0.0;
    }
}
