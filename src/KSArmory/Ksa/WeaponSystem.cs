using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>Something worth telling the operator about, surfaced in the panel.</summary>
internal readonly record struct SystemEvent(double AtSeconds, string Message);

/// <summary>
/// One weapons system: its launcher, its sensor, and the fire-control logic that decides when
/// to commit rounds. Mounted on a platform vehicle, normally the craft carrying the launcher
/// part, and pinned there so the site keeps defending itself after the player switches away.
/// </summary>
internal sealed class WeaponSystem(Config config, SystemConfig policy)
    : IWeaponSystemView, IManualFire, IOpticalHead, ISightPicture, IEffectSource
{
    private readonly Config _config = config;

    // This installation's own settings. Shared Config stays for the session-wide ones.
    private readonly SystemConfig _policy = policy;
    private readonly List<IProjectile> _rounds = [];
    private readonly List<Vehicle> _blastScratch = [];

    // Craft an unguided round could run into, rebuilt at most once a frame.
    private readonly List<TargetState> _contactScratch = [];
    private bool _contactsFresh;

    // Rounds in the air that are somebody else's, rebuilt every frame. A field rather than a
    // local, so filtering costs no allocation on a path every system runs every frame.
    private readonly List<IContact> _incoming = [];
    private readonly List<Vehicle> _pendingKills = [];
    private readonly List<SystemEvent> _events = [];

    private Vehicle? _lastPlatform;
    private double _salvoTimer;
    private double _reloadTimer;
    private double _clock;

    /// <summary>The vehicle the launcher is mounted on.</summary>
    public Vehicle? Platform { get; private set; }

    /// <summary>True when the operator pinned the platform rather than following control.</summary>
    public bool PlatformPinned { get; private set; }

    public Radar Radar { get; } = new(config, policy);

    /// <summary>What the set is holding, for a reader that has no business with the set.</summary>
    public Track? LockedTrack => Radar.Locked;

    /// <summary>Rounds left in the launcher.</summary>
    public int Ammo => _magazine.Ammo;

    public IReadOnlyList<IProjectile> Rounds => _rounds;

    public IReadOnlyList<SystemEvent> Events => _events;

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

    /// <summary>
    /// True when the tubes are where the profile says they are: pods found if the profile declares
    /// them, and legitimately absent if it does not.
    ///
    /// <para>The distinction is the whole difference between a rail and a broken launcher. A fixed
    /// launcher has to seat its rounds and fire them off its tubes with no pods in sight; one whose
    /// declared pods the engine did not hand over must do neither, because every tube coordinate
    /// would then be read against an identity that is not where the tubes are.</para>
    /// </summary>
    public bool TubesResolved => Profile.PodsMarker is null || PodsPart is not null;

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

    /// <summary>
    /// Where the head is looking from and along what, both in Ecl.
    ///
    /// <para>Anchored to <see cref="PlatformEcl"/>, this frame's sample, so a caller differencing
    /// the eye against it gets a separation carrying no epoch at all. That is what lets the sight
    /// hand KSA an offset the engine applies during its own pass.</para>
    /// </summary>
    public bool TryOpticViewEcl(out double3 eyeEcl, out double3 forwardEcl)
        => TryOpticViewEclAt(PlatformEcl, out eyeEcl, out forwardEcl);

    /// <summary>
    /// The same view, resolved against a platform position sampled by the caller.
    ///
    /// <para>For the one caller that runs inside the engine's viewport pass, where "now" is a
    /// different instant from the mod's own sample and the difference is a frame of the planet's
    /// motion.</para>
    ///
    /// <para>While the head is settled it is <em>tracking</em>, so the view is re-solved onto the
    /// target's own position at this instant rather than left along the axis the drive was turned
    /// to a frame ago. That difference is one frame of the target's angular travel, which scales
    /// with simulation speed and is most of the picture at magnification. While it is still
    /// slewing the head's own axis is used, because a target sliding towards the middle is what
    /// slewing looks like and hiding it would be a lie.</para>
    /// </summary>
    public bool TryOpticViewEclAt(double3 platformEcl, out double3 eyeEcl, out double3 forwardEcl)
    {
        eyeEcl = forwardEcl = Vec.Zero;

        if (Platform is not { } platform || Launcher is not { } launcher) return false;
        if (OpticPart is null) return false;

        if (!LauncherPart.TryGetOpticViewEcl(platform, launcher, Profile, Turret.BearingRad,
                                             OpticDirectionPartFrame, platformEcl,
                                             out eyeEcl, out forwardEcl))
        {
            return false;
        }

        if (!_optic.OnTarget || Radar.Locked is not { } locked) return true;
        if (!locked.Contact.TryDrawEgo(out double3 ego)) return true;
        if (!KsaWorld.TryEgoToEcl(ego, out double3 drawnEcl)) return true;

        double3 toTarget = drawnEcl - eyeEcl;
        if (Vec.Len2(toTarget) > 1.0) forwardEcl = Vec.Unit(toTarget);

        return true;
    }

    /// <summary>The search array's current angle. Cosmetic - the radar model is a cone search.</summary>
    public double RadarSpinRad { get; private set; }

    // The array's own clock. Its angle is decoration -- a search set never stops and never aims,
    // so nothing reads it back -- and the engine's step beats with the display's frame pacing, so
    // advancing on it turns the array three times as far on alternate frames. See Sim/SmoothedStep.
    private readonly SmoothedStep _spinStep = new();

    // The drives' clock. A traverse is a rate-limited slew, so its angle advances by rate x step
    // -- and the engine's step carries the display's frame pacing, which moves the turret three
    // times as far on alternate frames while the hull it sits on does not. That reads as a
    // stuttering turret. Total time is preserved, so it still arrives and settles when it would
    // have, and IsLaid is unaffected in aggregate.
    private readonly SmoothedStep _driveStep = new();

    /// <summary>Azimuth drive state. Pure maths, no KSA types — see <see cref="Turret"/>.</summary>
    public Turret Turret { get; } = new();

    // Which drives the engine is still accepting writes for, latched per assembly.
    private DriveStatus _drives;

    /// <summary>True while the engine still accepts writes for this assembly.</summary>
    public bool DriveWorks(DriveChannel channel) => _drives.Works(channel);

    /// <summary>True once the engine has refused any drive on this launcher.</summary>
    public bool AnyDriveRefused => _drives.AnyRefused;

    /// <summary>
    /// The weapon system this battery is running, and what it fires and sees with.
    ///
    /// <para>The battery's own, not the session's: two sites in one world can be different
    /// systems, and anything reading the config's selection instead gets whichever battery
    /// updated last. They are the shared <see cref="Arsenal"/> instances, so retuning one from
    /// the panel still reaches every battery running that system, which is the point.</para>
    ///
    /// <para>Resolved when the launcher part is found rather than at construction — until then
    /// the battery does not know what it is.</para>
    /// </summary>
    public LauncherProfile Profile { get; private set; } = Arsenal.Launchers[0];

    /// <inheritdoc cref="Profile"/>
    public MunitionProfile Munition { get; private set; } = Arsenal.MunitionNamed(Arsenal.Launchers[0].Munition);

    // The cannon's round, which is a different profile from the missile above and carries its own
    // reach. Falls back to the missile so a launcher with no cannon still answers.
    private MunitionProfile Shell => Profile.GunMunition is { } named
                                         ? Arsenal.MunitionNamed(named)
                                         : Munition;

    /// <inheritdoc cref="Profile"/>
    public SensorProfile Sensor { get; private set; } = Arsenal.SensorNamed(Arsenal.Launchers[0].Sensor);

    /// <summary>Whether this battery's rounds may draw a motor plume.</summary>
    public bool PlumesEnabled => _config.MotorPlume && _config.DrawExplosions;

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

    // What the current burst was started against. A burst outlives its trigger by design, so
    // Radar.Locked is routinely null while the tail of one is still leaving the barrel.
    private Track? _burstTrack;
    private bool _manualTrigger;

    // Whether the turret is laid on the cannon's ballistic lead rather than on the target. Set by
    // AimPointEcl, which is the only place the choice is made.
    private bool _ringIsOnGunLead;

    // Whether the ring is laid on the operator's cursor. The same problem as _ringIsOnGunLead and
    // the same answer: rounds leave along the tube, so a missile that auto-engage commits to the
    // radar's lock while the tube follows the cursor departs at whatever angle those two differ
    // by -- which is unbounded, and up to 180 degrees.
    private bool _ringIsOnCursor;

    // Time of flight from the gun's last lead solve, for a timed fuse. Zero when there is no
    // solution, which is also what stops a shell being fused for a flight nobody computed.
    private double _gunFlightTime;

    // Where the ring was actually sent this frame, kept so the sight can draw it. Reported rather
    // than re-solved at draw time: a second solve would take the target's position from a later
    // instant and put the pipper somewhere the turret was never sent.
    private double3 _ringAimEcl;
    private bool _ringAimValid;

    /// <summary>
    /// Where the launcher is laid, and whether that is the cannon's ballistic lead rather than the
    /// target itself. False when it is stowed, driven by hand or following the cursor.
    /// </summary>
    public bool TryRingAimEcl(out double3 aimEcl, out bool isGunLead)
    {
        aimEcl = _ringAimEcl;
        isGunLead = _ringIsOnGunLead;

        return _ringAimValid;
    }

    /// <summary>Time of flight the gun's lead solved for, or zero if it did not solve.</summary>
    public double GunFlightSeconds => _gunFlightTime;

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
        aiming: Aiming,
        trains: Profile.Trains,
        drivesAccepted: _drives.AimingAccepted,
        assembliesResolved: TubesResolved,
        settled: Turret.IsLaid(Profile.SettleSeconds));

    /// <summary>
    /// The same question for the cannon, which share only the traverse with the pods.
    ///
    /// <para>Asking <see cref="IsLaid"/> instead reads the missiles' drive latch and the missiles'
    /// subpart, so a refused pod elevation — or a pods marker that resolved to nothing — silenced
    /// a cannon that was working perfectly.</para>
    /// </summary>
    public bool GunsAreLaid => FireGate.IsLaid(
        aiming: Aiming,
        trains: Profile.Trains,
        drivesAccepted: _drives.GunAimingAccepted,
        assembliesResolved: Profile.GunsMarker is null || GunsPart is not null,
        settled: Turret.IsLaid(Profile.SettleSeconds));

    // Slewing onto something, rather than stowed or driven from the panel. Mouse aim counts:
    // the drives are chasing a cursor, so fire control must still wait for them to settle or
    // rounds leave along a tube that is still swinging.
    // Designating counts as aiming. Firing at the cursor without it leaves the launcher wherever
    // it was already pointing -- stowed, on a radar track, or on the gun's lead -- and the round
    // departs along that and is hauled round by guidance over kilometres of arc. On a launcher
    // that cannot train, that off-axis start is a limit on the seeker and is expected; on one with
    // a turret it is simply a turret nobody told.
    private bool Aiming => (_policy.TurretTracking || _policy.MouseAim || _policy.MouseFire)
                           && !_policy.TurretManual && !_policy.TurretSpin;

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

        // Whichever registered weapon system is fitted, if any. Adopting it points this battery's
        // profiles at that system, so everything downstream - drives, guidance, the panel -
        // follows without knowing which launcher this is.
        if (LauncherPart.FindNth(Platform, LauncherOrdinal, _launcherScratch) is var (part, profile))
        {
            bool changed = !ReferenceEquals(profile, Profile) || Launcher is null;
            Launcher = part;
            Profile = profile;
            (Munition, Sensor) = Arsenal.LoadoutFor(profile);
            profile.ConfigureTurret(Turret);

            // The set is fitted to this launcher, so it filters on that system's sensor rather
            // than on whichever one the panel is showing.
            Radar.Sensor = Sensor;

            // A different weapon system carries a different number of rounds, so the magazine
            // is sized when one is first recognised rather than at construction.
            if (changed)
            {
                _magazine.Resize(profile.TubeCount, profile.MagazineDepth);
                _guns.Fill(profile.GunAmmo);
                _nextBarrel = 0;
                _burstTrack = null;
            }
        }
        else
        {
            Launcher = null;
        }

        TurretPart = Launcher is null ? null : LauncherPart.FindTurret(Launcher, Profile);
        PodsPart = Launcher is null ? null : LauncherPart.FindPods(Launcher, Profile);
        RadarPart = Launcher is null ? null : LauncherPart.FindRadar(Launcher, Profile);
        GunsPart = Launcher is null ? null : LauncherPart.FindGuns(Launcher, Profile);
        OpticPart = Launcher is null ? null : LauncherPart.FindOptic(Launcher, Profile);
        MountEcl = LauncherPart.ResolveOriginEcl(Platform, Launcher);

        // After the launcher is resolved: the part-relative modes read the part's own mounting.
        Boresight = ResolveBoresight();

        // Say what the launcher is actually made of, once. If the turret is never found, this
        // is the line that says whether the subpart Ids survived into the runtime unchanged.
        if (Launcher is not null && !_loggedSubParts)
        {
            _loggedSubParts = true;
            LauncherPart.FindMissiles(Launcher, Munition, _missileBodies);
            LauncherPart.FindFins(Launcher, Munition, _finBodies);
            Log.Info($"launcher subparts: {LauncherPart.DescribeSubParts(Launcher)}");
            Log.Debug($"round bodies found: {_missileBodies.Count}, fin sets {_finBodies.Count} (need {Profile.TubeCount})");
            if (TurretPart is null) Log.Warn("turret subpart not found - the turret will not slew");
            if (_missileBodies.Count == 0) Log.Warn("no round bodies - rounds will draw as tracers only");
        }

    }

    /// <summary>
    /// Advances the battery by <paramref name="dt"/> simulated seconds.
    ///
    /// <para>Separate from <see cref="SampleWorld"/> on purpose: this is gated on the simulation
    /// clock, so it does not run while paused or on a frame that advanced no time, whereas the
    /// world sample must run regardless.</para>
    /// </summary>
    /// <param name="airborne">
    /// Every round in the world, so this system can see the ones that are not its own. Filtered
    /// here rather than in the radar: which rounds are mine is the system's business, and a
    /// sensor that had to know would be a sensor that knows what a weapon is.
    /// </param>
    public void Update(double dt, IReadOnlyList<IContact>? airborne = null)
    {
        if (Platform is null) return;

        _clock += dt;

        _incoming.Clear();
        if (airborne is not null)
        {
            for (int i = 0; i < airborne.Count; i++)
            {
                // Never this system's own salvo. Teams would usually cover this, but one with no team
                // set reads every contact as Unknown, which is engageable -- and a launcher must
                // not shoot down its own missiles as they leave the tubes.
                if (airborne[i].Handle is IProjectile r && _rounds.Contains(r)) continue;

                _incoming.Add(airborne[i]);
            }
        }

        Radar.Scan(Platform, Boresight, dt, _incoming);
        AttributeRoundsToTracks();
        UpdateTurret(dt);

        // Rounds before fire control, so a round fired this frame is not integrated until the
        // next one. TravelSinceLaunch differences two platform-relative offsets, which cancels the
        // platform's ~29.8 km/s only while the sample advances alongside the round. Integrating a
        // new round in its own launch frame leaves the sample still for one step, so a frame of
        // ecliptic motion lands in travel permanently: 658.78 m of travel at an age of 0.04 s on
        // a round doing 124 m/s.
        //
        // The cost is one frame before a new round moves, which is correct anyway: it is still in
        // the tube on the frame the trigger is pulled.
        UpdateRounds(dt);
        UpdateFireControl(dt);
        UpdateGunFireControl(dt);
        TrimEvents();

        if (_config.DiagnosticDump)
        {
            Diagnostics.Tick(this, _config, _policy, _clock, _config.DiagnosticIntervalSeconds);
        }
    }

    /// <summary>
    /// Why the missiles are not launching, or null when nothing is stopping them.
    ///
    /// <para>Every gate returns quietly and looks identical from outside: an unarmed system, one
    /// with no lock and one whose drives have not settled all sit there doing nothing. Naming the
    /// first gate that says no is the difference between reading the panel and reading the
    /// source.</para>
    /// </summary>
    public string? Hold { get; private set; } = "not started";

    // In order, and the order is the fire sequence's: the first answer is the one to act on.
    private string? Holding()
    {
        if (Platform is null) return "no platform";
        if (!IsOperational) return _config.RequireLauncherPart && Launcher is null
                                       ? "no launcher part on this craft"
                                       : "not operational";

        // Which weapon this ladder is about. The rungs below are the missile sequence, and a
        // launcher with no tubes fails "out of rounds" at every one of them forever: its magazine
        // is empty by construction and its belt is what shoots. Asking the missile ladder about
        // one reports it holding fire while its cannon are audibly firing.
        WeaponFit fit = WeaponFit.Of(Profile, Sensor);
        bool hasTubes = fit.FirstOf(ArmamentKind.Tubes) is not null;

        if (hasTubes && _magazine.IsEmpty && _reloadTimer > 0.0)
        {
            return $"reloading ({_reloadTimer:F0} s)";
        }

        if (!_policy.Armed) return "safe -- master arm is off";
        if (!_policy.AutoEngage) return "auto-engage is off";

        if (hasTubes)
        {
            if (!_policy.MissilesEnabled) return "missiles are switched off";
            if (Munition.Guidance == GuidanceMode.None) return "unguided - release it by hand";
            if (Ammo <= 0) return "out of rounds";
            if (_salvoTimer > 0.0) return "between salvos";
        }
        else
        {
            if (!_policy.GunsEnabled) return "cannon are switched off";
            if (_guns.IsEmpty) return "belt empty";
        }

        if (!Radar.HasFiringSolution)
        {
            return Radar.Tracks.Count == 0
                       ? "nothing detected"
                       : $"no firing solution yet ({Radar.Tracks.Count} track(s))";
        }

        // Each weapon settles on its own gear, so a system with no pods must not be asked whether
        // its pods have stopped moving.
        if (hasTubes)
        {
            if (!IsLaid) return "drives still settling";
            if (!FireGate.MissilesMayFire(_ringIsOnGunLead, Profile.LaunchAlongTube))
            {
                return "the cannon has the bearing";
            }

            // The operator owns the ring, so an automatic launch would leave along the cursor and
            // turn onto whatever the radar locked. Held rather than re-aimed: the cursor is a
            // deliberate command, and taking the ring back to shoot would fight the player.
            if (!FireGate.MissilesMayFire(_ringIsOnCursor, Profile.LaunchAlongTube))
            {
                return "the cursor has the bearing";
            }
        }
        else if (!GunsAreLaid)
        {
            return "drives still settling";
        }

        if (Radar.Locked is not { } locked) return "no lock";
        if (!ThreatModel.MayEngage(locked, _policy.Iff)) return "target is not engageable (IFF)";
        if (!ThreatModel.HasSalvoCapacity(locked, _policy.RoundsPerTarget)) return "salvo committed";
        if (!ThreatModel.InEngagementEnvelope(locked, Munition))
        {
            // With the numbers: "out of reach" is read as too far, and the usual cause is a
            // target that came inside the minimum instead.
            return $"target out of reach ({locked.Range / 1000.0:F1} km, envelope "
                   + $"{Munition.MinRange / 1000f:F1}-"
                   + $"{Munition.MaxRange / 1000f:F1} km)";
        }

        return null;
    }

    // Decides which craft the battery is mounted on. The launcher is a physical part, so the
    // battery belongs to the craft carrying it and stays there rather than following control.
    // Preference order: an explicit pin, then the controlled craft if it has a launcher, then
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

        // A controlled craft that carries a launcher is the one meant.
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
        // hull of the controlled craft, which is how it is tested without opening the editor.
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
            && TubeGeometry.TryBoresightPartFrame(Profile, Sensor.BoresightSource,
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
            Track? t = Radar.Tracks.Find(x => ReferenceEquals(x.Contact.Handle, target));
            if (t is not null) t.RoundsAssigned++;
        }
    }

    private void UpdateFireControl(double dt)
    {
        string? hold = Holding();

        // Logged on change, not every frame: a panel line answers "why is it not shooting" only
        // for whoever is looking at the panel.
        if (hold != Hold)
        {
            Announce(hold is null ? "clear to fire" : $"holding fire: {hold}");
        }

        Hold = hold;
        if (_salvoTimer > 0.0) _salvoTimer = Math.Max(0.0, _salvoTimer - dt);

        // Reload cycle.
        // TubeCount, not just IsEmpty: a launcher with no tubes is empty by definition and
        // would otherwise cycle a reload forever, announcing one every few seconds.
        if (Profile.TubeCount > 0 && _magazine.IsEmpty && Profile.ReloadSeconds > 0f)
        {
            if (_reloadTimer <= 0.0) _reloadTimer = Profile.ReloadSeconds;
            _reloadTimer -= dt;
            if (_reloadTimer <= 0.0)
            {
                _reloadTimer = 0.0;
                _magazine.RefillAll();
                Announce("launcher reloaded");
            }
            return;
        }

        if (Hold is not null) return;

        Track target = Radar.Locked!;
        if (!ThreatModel.MayEngage(target, _policy.Iff)) return;
        if (!ThreatModel.HasSalvoCapacity(target, _policy.RoundsPerTarget)) return;

        // Detection reaches 36 km; the round reaches 20 km. Without this the battery empties
        // itself at contacts it cannot possibly catch: an 8.7 km crossing shot expires at 22 s
        // having never closed.
        if (!ThreatModel.InEngagementEnvelope(target, Munition)) return;

        // A round that cannot steer has no business being launched at a track: it would leave the
        // rail and fall, and the log would record a shot at something it was never going to reach.
        // WhyNotFiring says the same thing to the operator.
        if (Munition.Guidance == GuidanceMode.None) return;

        Fire(target);
    }

    // Sends one shell down whichever barrel is next in the cycle.
    //
    // Which way the optical head looks, in the launcher part's frame: at the locked contact if
    // there is one, otherwise along the turret's own facing.
    // Where the cursor points, in the launcher part's frame. False unless mouse aim is on and the
    // cursor is over a viewport whose camera gives a usable ray.
    // Where a bearing to something should be measured from: the trunnion the tubes swing on, not
    // the launcher part's origin.
    //
    // The drive lays the bore *parallel* to (target - origin), and that bore passes through the
    // trunnion. Measured from the part origin the tube bundle sits 2.5 to 3.3 m off that line at
    // every bearing and elevation; measured from the trunnion it is on it exactly, because that is
    // how the model is built. Parallel but displaced misses by the perpendicular part of the
    // displacement -- a fixed distance, so a shrinking angle: 1.7 degrees at 100 m, 0.17 at 1 km,
    // nothing at 20. Which is why it reads as slightly off up close and fine far away.
    private double3 AimOriginEcl
    {
        get
        {
            if (Platform is null || Launcher is null || !Profile.Trains) return MountEcl;

            // Whichever assembly is doing the aiming. A gun-only mount has no pods to swing.
            double3 pivot = PodsPart is not null ? Profile.PodPivotFromTurret
                                                 : Profile.GunPivotFromTurret;

            double3 trunnion = Profile.TurretPivot
                               + (TubeGeometry.TurretRotation(Turret.BearingRad) * pivot);

            return LauncherPart.TryPartPointEcl(Platform, Launcher, trunnion, PlatformEcl,
                                                out double3 ecl)
                       ? ecl
                       : MountEcl;
        }
    }

    // Where a bearing for the *optical head* should be measured from: the head's own pivot.
    //
    // The same displaced-parallel error <see cref="AimOriginEcl"/> exists for, and it bites far
    // harder here for one reason: a launcher is judged by where its rounds arrive, and a sight is
    // judged by where the picture is pointing. The head stands 4.1 m up and 1.1 m forward of the
    // part origin, so a command measured from the origin lays the head parallel to the right
    // bearing and displaced off it -- half a degree at 700 m, and at 16x half a degree is a sixth
    // of the picture.
    //
    // OpticEyeForward is deliberately not added. It runs along the aim itself, so it moves the eye
    // up and down the line rather than off it, and contributes exactly nothing to this.
    private double3 OpticOriginEcl
    {
        get
        {
            if (Platform is null || Launcher is null || !Profile.Trains) return MountEcl;

            double3 pivot = Profile.TurretPivot
                            + (TubeGeometry.TurretRotation(Turret.BearingRad) * Profile.OpticPivotFromTurret);

            return LauncherPart.TryPartPointEcl(Platform, Launcher, pivot, PlatformEcl,
                                                out double3 ecl)
                       ? ecl
                       : MountEcl;
        }
    }

    private bool TryCursorAimPartFrame(out double3 partFrame)
    {
        partFrame = default;

        // The designator too, not only the aim switch: a tool that sends a round at the cursor
        // has to point the launcher at it first, or the round leaves along a stale bearing.
        return (_policy.MouseAim || _policy.MouseFire)
               && Platform is not null
               && KsaWorld.TryCursorAimEcl(AimOriginEcl, out double3 dirEcl)
               && LauncherPart.TryDirectionToPartFrame(Platform, Launcher, dirEcl, out partFrame);
    }

    private double3 OpticAimPartFrame()
    {
        // The head watches what the launcher is aimed at, so it follows the cursor too — without
        // this it keeps staring at a radar track while the tubes point somewhere else entirely.
        if (TryCursorAimPartFrame(out double3 cursorFrame)) return cursorFrame;

        // Where the target is *drawn*, not where fire control has it. The two differ by metres,
        // which is nothing to a turret laying a 20 m warhead and is the whole picture to a sight:
        // at 16x the field is three degrees, so the gap that a launcher can ignore puts the target
        // a third of the way to the edge. Falls back to the analytic position rather than refusing
        // — a head that stops following because a craft cannot be placed is worse than one that
        // follows a few metres out.
        if (Radar.Locked is { } locked && Platform is not null)
        {
            double3 targetEcl = locked.PositionEcl;
            if (locked.Contact.TryDrawEgo(out double3 ego) && KsaWorld.TryEgoToEcl(ego, out double3 drawn))
            {
                targetEcl = drawn;
            }

            if (LauncherPart.TryDirectionToPartFrame(Platform, Launcher, targetEcl - OpticOriginEcl,
                                                     out double3 toTarget))
            {
                return toTarget;
            }
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
        _ringIsOnGunLead = false;
        _gunFlightTime = 0.0;
        _ringAimEcl = aim.PositionEcl;
        _ringAimValid = true;

        if (!GunsHaveTheEngagement(aim)) return aim.PositionEcl;

        MunitionProfile shell = Arsenal.MunitionNamed(Profile.GunMunition!);
        double3 gravity = Platform is null ? Vec.Zero : KsaWorld.GravityAt(Platform, MountEcl);

        // The flight time comes back from the same solve that produced the aim point, which is
        // what a timed fuse needs: a burst time derived separately would go off somewhere the gun
        // is not pointing. Without it FuseSeconds stays zero and Slug falls back to proximity, so
        // the panel's timed-airburst switch has nothing to act on.
        if (!BallisticLead.TrySolve(MountEcl, KsaWorld.VelocityEcl(Platform!),
                                    aim.PositionEcl, aim.VelocityEcl,
                                    shell.LaunchSpeed, gravity, out double3 lead,
                                    out double flightTime))
        {
            return aim.PositionEcl;
        }

        _gunFlightTime = flightTime;

        // Recorded rather than recomputed at the missile gate: a solve that fails leaves the ring
        // on the target, which the missiles can use, so only the write that actually happened
        // decides whether they are held.
        _ringIsOnGunLead = true;
        _ringAimEcl = lead;

        return lead;
    }

    // Whether the cannon are the weapon this engagement belongs to: inside their envelope, and
    // with the missiles either switched off or unable to reach.
    private bool GunsHaveTheEngagement(Track aim)
        => FireGate.GunsHaveTheEngagement(Profile.HasCannon, _policy.GunsEnabled, !_guns.IsEmpty,
                                          aim.Range, Shell.MinRange, Shell.MaxRange);

    // Fired along the barrel: the lead is in where the turret is pointing, so aiming the shell
    // itself as well would apply it twice.
    private void FireGun(Track? track)
    {
        if (Platform is null || Launcher is null || GunsPart is not { } guns) return;

        int barrel = _nextBarrel % Profile.GunMuzzles.Length;
        _nextBarrel = (_nextBarrel + 1) % Profile.GunMuzzles.Length;

        if (!LauncherPart.TryGetGunMuzzleEcl(Platform, Launcher, guns, Profile, barrel,
                                             PlatformEcl, out double3 muzzle, out double3 axis))
        {
            return;
        }

        MunitionProfile shell = Arsenal.MunitionNamed(Profile.GunMunition!);

        // A round leaves with the craft's motion; it flies in the ground's. The two differ only
        // once a launcher is moving, and then the second is what airspeed and heading mean.
        double3 platformVel = KsaWorld.VelocityEcl(Platform);
        double3 frameVel = KsaWorld.GroundVelocityAt(Platform, PlatformEcl);

        // Negative tube numbers mark the cannon: the magazine owns 0..TubeCount-1, and a shell
        // must never be mistaken for a missile that could claim a tube back.
        //
        // A null track is the tail of a burst whose target died: the shell is unguided and aimed
        // by the turret, so it still flies, with nothing to fuse against.
        // The muzzle in the launcher's own frame. Anything drawn against the round -- the tracer,
        // and a body if one is ever declared -- is placed from this plus the travel since launch,
        // never from the platform's analytic position, which sits metres off a landed craft.
        TubeGeometry.TryGunMuzzlePartFrame(Profile, barrel, guns.PositionParentAsmb,
                                           guns.Asmb2ParentAsmb, out double3 muzzlePart);

        Slug slug = new(muzzle, platformVel + axis * shell.LaunchSpeed, track?.Contact.Handle,
                        -(barrel + 1), PlatformEcl, frameVel)
        {
            Munition = shell,
            LaunchAnchorPartFrame = muzzlePart,
        };
        if (track is not null)
        {
            slug.Aimpoint = Aimpoint.OnVehicle(track.Contact.Handle, track.PositionEcl, track.VelocityEcl,
                                               track.Contact.MeanRadius);

            // Flak: burst at the intercept the ring was laid on. Without a solve there is no time
            // to burn, and the shell falls back to its proximity fuse.
            if (shell.TimedFuse) slug.FuseSeconds = _gunFlightTime;
        }
        _rounds.Add(slug);
    }

    // The cannon, which run on their own belt and their own envelope.
    //
    // Deliberately not gated on the missile channel's state. The two cover each other: the
    // missiles need 1.2 km to arm and steer, so anything closer is the cannon's problem and stays
    // untouchable if the guns wait for the launcher to run dry.
    private void UpdateGunFireControl(double dt)
    {
        if (!Profile.HasCannon)
        {
            _gunTrace += dt;
            if (_gunTrace >= 5.0)
            {
                _gunTrace = 0.0;
                Log.Debug(() => $"cannon: none on {Profile.DisplayName} "
                                + $"(munition={Profile.GunMunition ?? "null"} "
                                + $"barrels={Profile.GunMuzzles.Length})");
            }
            return;
        }

        // Rechecked rather than trusted from FireBurst: a frame passes between the click and this
        // step, and arming, the belt and the lay can all move in it.
        bool manual = _manualTrigger && _policy.Armed && _policy.GunsEnabled
                      && IsOperational && GunsAreLaid;

        bool wantToFire = manual
                          || _policy.AutoEngage && _policy.Armed && _policy.GunsEnabled
                          && IsOperational && GunsAreLaid
                          && Radar.Locked is { } locked
                          && ThreatModel.MayEngage(locked, _policy.Iff)
                          && locked.Range >= Shell.MinRange
                          && locked.Range <= Shell.MaxRange;

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
                       + $"cd={_guns.Cooldown:F3} armed={_policy.Armed} auto={_policy.AutoEngage} "
                       + $"enabled={_policy.GunsEnabled} laid={GunsAreLaid} drive={_drives.Works(DriveChannel.Guns)} "
                       + $"part={(GunsPart is not null)} range={range} "
                       + $"envelope={Shell.MinRange:F0}-{Shell.MaxRange:F0} m";
            });
        }

        // Belt resupply, on its own timer: the launcher's reload gate returns early when the
        // tubes are empty, so a shared one would leave the cannon dry for as long as the
        // missiles took to come back.
        if (_guns.IsEmpty && Profile.GunReloadSeconds > 0f)
        {
            _gunReloadTimer += dt;
            if (_gunReloadTimer >= Profile.GunReloadSeconds)
            {
                _gunReloadTimer = 0.0;
                _guns.Fill(Profile.GunAmmo);
                Announce("cannon belt replaced");
            }
            _manualTrigger = false;
            return;
        }
        _gunReloadTimer = 0.0;

        // Latch the track while the trigger is down. GunChannel deliberately runs a started burst
        // to its end after wantToFire goes false, and losing the lock is one of the ways it does
        // — so reading Radar.Locked per round hands the tail of every such burst a null.
        if (wantToFire) _burstTrack = Radar.Locked;

        int fired = _guns.Step(dt, wantToFire, Profile);
        _manualTrigger = false;
        if (fired <= 0) return;

        // A flickering track keeps its vehicle; a destroyed one does not, and a shell must not
        // carry a reference to it.
        if (_burstTrack is { } held && !held.Contact.IsAlive) _burstTrack = null;

        for (int i = 0; i < fired; i++) FireGun(_burstTrack);
        Log.Debug(() => $"cannon: {fired} round(s) away, {_guns.Ammo} left");
        if (_guns.IsEmpty) Announce("cannon belt empty");
        if (_guns.BurstRemaining <= 0) _burstTrack = null;
    }

    // Slews the turret onto whatever the radar is holding, and writes the result to the part.
    // Priority is the lock, then the most urgent threat, then rest. Following a threat before the
    // lock has settled is what makes the vehicle look like it is *watching* the sky rather than
    // reacting a second late. The bearing has to be computed in the part's own frame, not in Ecl:
    // the turret rotates about the part's X axis, and the platform's own attitude is what relates
    // the two.
    private void UpdateTurret(double dt)
    {
        // Evened out for the drives only. Everything that integrates the world -- rounds, fuses,
        // the belt -- takes the step as the engine reports it.
        dt = _driveStep.Next(dt);

        // Cleared here rather than in the branches that do not set it, so a rung added later
        // cannot leave a stale claim on the ring.
        _ringIsOnCursor = false;
        _ringAimValid = false;

        if (_policy.TurretSpin)
        {
            // Command a bearing that runs away at the slew rate, so the turret chases it
            // forever. Nothing depends on this - it exists so "does the mesh move at all" can
            // be answered from across the launchpad.
            // Elevation still comes from the manual slider, so the two can be driven together:
            // spinning while pitching is the quickest way to see both axes composing properly.
            _spinPhase = Turret.WrapPi(_spinPhase + Profile.SlewRateRad * dt);
            Turret.Point(_spinPhase, double.DegreesToRadians(_policy.TurretManualElevationDeg));
        }
        else if (_policy.TurretManual)
        {
            Turret.Point(double.DegreesToRadians(_policy.TurretManualBearingDeg),
                         double.DegreesToRadians(_policy.TurretManualElevationDeg));
        }
        else if (TryCursorAimPartFrame(out double3 cursorFrame))
        {
            // Ahead of the radar, and ahead of the tracking switch: with mouse aim on the operator
            // *is* the sensor, so needing to enable radar tracking first would be surprising. The
            // drives stay rate-limited, so this points towards the cursor rather than snapping.
            _ringIsOnGunLead = false;
            _ringIsOnCursor = true;
            Turret.Track(cursorFrame);
        }
        else if (!_policy.TurretTracking)
        {
            Turret.Stow();
        }
        else
        {
            Track? aim = Radar.Locked ?? MostUrgentThreat();

            if (aim is not null && Platform is not null
                && LauncherPart.TryDirectionToPartFrame(Platform, Launcher, AimPointEcl(aim) - MountEcl, out double3 partFrame))
            {
                Turret.Track(partFrame);
            }
            else
            {
                Turret.Stow();
            }
        }

        Turret.Update(dt, Profile.SlewRateRad, Profile.ElevationRateRad);

        // Each assembly latches on its own refusal. The drive keeps integrating either way, so the
        // drawn facing line goes on showing where the battery believes it is pointing — which is
        // the only thing that distinguishes a refused write from a wrong solution.
        if (TurretPart is not null && _drives.Works(DriveChannel.Turret)
            && !LauncherPart.TryApplyTurretBearing(TurretPart, Turret.BearingRad))
        {
            Refuse(DriveChannel.Turret, "turret traverse");
        }

        if (PodsPart is not null && _drives.Works(DriveChannel.Pods)
            && !LauncherPart.TryApplyPodAim(PodsPart, Profile, Turret.BearingRad, Turret.ElevationRad))
        {
            Refuse(DriveChannel.Pods, "pod elevation");
        }

        // The cannon follow the same aim as the pods: one turret, one solution.
        if (GunsPart is not null && _drives.Works(DriveChannel.Guns)
            && !LauncherPart.TryApplyGunAim(GunsPart, Profile, Turret.BearingRad, Turret.ElevationRad))
        {
            Refuse(DriveChannel.Guns, "cannon elevation");
        }

        // The optical head points at what the battery is watching, and falls back to wherever
        // the turret faces so it never sits skewed across the hull with nothing to look at.
        _optic.Update(dt, OpticAimPartFrame(), Profile.OpticSlewRateRad);

        if (OpticPart is not null && _drives.Works(DriveChannel.Optic)
            && !LauncherPart.TryApplyOpticAim(OpticPart, Profile, Turret.BearingRad,
                                              _optic.Direction))
        {
            Refuse(DriveChannel.Optic, "optical head");
        }

        // The search array turns regardless of what the battery is doing - it is looking, not
        // aiming - so it is driven off the clock rather than off the track.
        if (RadarPart is not null && _drives.Works(DriveChannel.Radar))
        {
            if (!_policy.SearchRadarStopped)
            {
                RadarSpinRad = Turret.WrapPi(
                    RadarSpinRad + Profile.SearchRadarRpm * (Math.Tau / 60.0) * _spinStep.Next(dt));
            }
            if (!LauncherPart.TryApplyRadarSpin(RadarPart, Profile, Turret.BearingRad, RadarSpinRad))
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

        Span<bool> flying = stackalloc bool[Profile.TubeCount];

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

            // Along the airflow once there is enough of it to mean anything, easing off the tube
            // the round left before that. A store released rather than fired has no airspeed at
            // the moment it lets go, so the tube is the only thing that says which way it points.
            //
            // The tube, emphatically not Boresight. A PartForward sensor boresights on the part's
            // +X -- its mounting face's outward normal -- while a tube points along +Y, so the two
            // are perpendicular by construction on every craft at every attitude, so the boresight
            // draws a released store across its own axis. Falling back to it is still right when
            // the tube cannot be resolved: some direction beats none.
            double3 release = LauncherPart.TryGetTubeAxisEcl(platform, launcher, PodsPart, Profile,
                                                             index, out double3 tubeEcl)
                                  ? tubeEcl
                                  : Boresight;

            double3 heading = BodyAttitude.Heading(round.VelocityLocal, release);

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
                                          round.FinDeployment(Munition), Munition);
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
                // They should agree. 800 m apart at detonation while the fuse and the blast agree
                // to the decimal means the round is killing the target and being rendered
                // somewhere else entirely. This shows where that opens up.
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
        for (int i = 0; i < _missileBodies.Count && i < Profile.TubeCount; i++)
        {
            TubeVisual plan = _magazine.Plan(i, flying[i]);
            if (!Magazine.RequiresSeating(plan)) continue;

            bool seated = TubesResolved
                          && LauncherPart.TrySeatMissile(PodsPart, Profile, _missileBodies[i],
                                                         FinsFor(i), i, Munition);

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
        if (!track.Contact.IsAlive) { Announce("refused: target gone"); return false; }
        if (!ThreatModel.MayEngage(track, _policy.Iff))
        {
            Announce($"refused: {track.Contact.DisplayName} is {track.Allegiance}");
            return false;
        }

        return Commit(Aimpoint.OnVehicle(track.Contact.Handle, track.PositionEcl, track.VelocityEcl,
                                         track.Contact.MeanRadius),
                      $"{track.Contact.DisplayName} ({track.Range / 1000.0:F1} km)");
    }

    /// <summary>
    /// Lets one round go with nothing to aim it at.
    ///
    /// <para>A bomb is released rather than fired: it carries no seeker and no uplink, so where it
    /// lands was decided by where the aircraft was and what it was doing at the moment the operator
    /// let it go. Passing it an aimpoint would be a lie the flight model then ignores.</para>
    /// </summary>
    public bool Release()
    {
        if (Munition.Guidance != GuidanceMode.None)
        {
            Announce($"refused: the {Munition.DisplayName} is guided - give it something to shoot at");
            return false;
        }

        return Commit(Aimpoint.Nothing, Munition.DisplayName);
    }

    /// <summary>
    /// Commits one round to a position in the world rather than to a craft.
    ///
    /// <para>The gates a track brings with it — alive, and on a side that may be engaged — have no
    /// meaning for a coordinate, and there is deliberately no substitute: an operator pointing at
    /// a place has said what they want. Everything after the aimpoint is identical, which is why
    /// this and <see cref="Fire(Track)"/> share <c>Commit</c> rather than being written twice.</para>
    /// </summary>
    public bool FireAt(double3 pointEcl)
    {
        if (!Vec.IsFinite(pointEcl)) { Announce("refused: designation is not a position"); return false; }

        double range = Platform is null ? 0.0 : Vec.Len(pointEcl - PlatformEcl);

        // The round's own reach. Without this gate a designation the cursor solve puts beyond the
        // horizon is committed, and the round is spent flying at somewhere it can never arrive.
        // Said out loud, because a designation that is simply too far is an ordinary thing for an
        // operator to do and worth being told about.
        if (range > Munition.MaxRange)
        {
            Announce($"refused: {range / 1000.0:F1} km is beyond the round's {Munition.MaxRange / 1000.0:F0} km reach");
            return false;
        }

        // Anchored to the body it sits on, never held as the coordinate it was when it was named.
        Aimpoint aim = KsaWorld.TryAnchorToGround(pointEcl, out object? body, out double3 anchor)
                           ? Aimpoint.OnGround(body!, anchor, pointEcl, Vec.Zero)
                           : Aimpoint.AtPoint(pointEcl);

        return Commit(aim, $"a designated point ({range / 1000.0:F1} km)");
    }

    // Everything a shot needs once it is known what is being shot at: the gates that belong to the
    // launcher rather than to the target, a tube, the launch geometry, and the round.
    private bool Commit(Aimpoint aim, string what)
    {
        // A launcher with no tubes has exactly one weapon, and every gate below is about the
        // magazine it does not own. Sending it down this path refuses every manual shot on an
        // empty magazine, which leaves a working cannon with no trigger at all.
        if (Profile.TubeCount == 0) return FireBurst();

        if (!_policy.Armed) { Announce("refused: not armed"); return false; }
        if (Platform is null) { Announce("refused: no platform"); return false; }
        if (!IsOperational) { Announce("refused: no launcher part fitted"); return false; }
        if (Ammo <= 0) { Announce("refused: launcher empty"); return false; }
        if (!IsLaid) { Announce("refused: launcher still slewing"); return false; }

        // Takes the round as it picks the tube. Nothing between here and the round being added
        // can fail, so a tube is never claimed without a round.
        if (!_magazine.TryTakeTube(_rounds, out int tube))
        {
            // A launcher with no tubes has none to be free. Saying so is the difference between
            // "this weapon carries no missiles" and "wait a moment", which is what the generic
            // message reads as.
            Announce(Profile.TubeCount == 0
                         ? $"refused: {Profile.DisplayName} carries no missiles"
                         : "refused: no free tube");
            return false;
        }

        double3 platformVel = KsaWorld.VelocityEcl(Platform);
        double3 frameVel = KsaWorld.GroundVelocityAt(Platform, PlatformEcl);

        // From the tube itself, using where the pods are aimed. The ring about the boresight
        // below is a fallback for a launcher with no pods: it ignores traverse and elevation.
        double3 launchAnchorPartFrame = Vec.Zero;
        double3 tubeMouth = Vec.Zero;
        bool fromTube = Launcher is not null && TubesResolved
                        && LauncherPart.TryGetTubeMuzzleEcl(Platform, Launcher, PodsPart, Profile, tube,
                                                            PlatformEcl, out tubeMouth)
                        // Seated, not at the mouth: the body mesh is modelled about its centre,
                        // so anchoring at the mouth starts the round half out of the tube. From
                        // the seated point it emerges as it accelerates, which is what a tube
                        // launch looks like.
                        && LauncherPart.TryGetSeatedPartFrame(PodsPart, Profile, tube,
                                                              Munition.BodyLength,
                                                              out launchAnchorPartFrame);

        double3 launchPos = fromTube
            ? tubeMouth
            : Launcher is not null
                ? LauncherPart.MuzzleEcl(Profile, MountEcl, Boresight, tube)
                : MountEcl + Boresight * Profile.MuzzleOffset;

        // Along the tube, so the round emerges pointing where the launcher points. The fallback
        // slews to the target and adds loft instead, which is what a launcher with fixed tubes
        // needs and what a laid one must not do.
        double3 tubeAxis = Vec.Zero;
        bool alongTube = fromTube && Profile.LaunchAlongTube
                         && LauncherPart.TryGetTubeAxisEcl(Platform, Launcher!, PodsPart, Profile, tube, out tubeAxis);

        // Nothing to point at is not the origin of the ecliptic. A released round takes the tube's
        // own direction, and the no-tube fallback takes the boresight -- reading aim.PositionEcl
        // through an empty aimpoint sends it at the Sun.
        double3 aimForGeometry = aim.Kind == AimpointKind.None
                                     ? launchPos + Boresight
                                     : aim.PositionEcl;

        double3 launchDir = FireGeometry.LaunchDirection(
            alongTube, tubeAxis, launchPos, aimForGeometry, Boresight, Profile.LaunchLoft,
            Profile.EjectAwayFromMount);

        // A seeker round released outside its own gimbal limit never steers and never recovers, so
        // this is the last point at which that is still a refusal rather than a round flying away
        // for its whole life. The tube goes back: the shot was never taken.
        // Operator-held waives the gimbal limit, because a launcher that cannot be pointed has no
        // way to bring a designated place inside it -- the rail's 92 to 116 degrees off is that,
        // and is a limit on the seeker rather than a fault. A launcher that *trains* has no
        // such excuse: waiving it there lets a round leave along a stale tube and says nothing.
        double3 toAim = aim.PositionEcl - launchPos;
        if (!FireGate.CanGuideOntoAimpoint(Munition.Guidance,
                                           aim.Kind == AimpointKind.Ground && !Profile.Trains,
                                           Munition.SeekerFovRad, launchDir, toAim))
        {
            _magazine.Return(tube);
            double offDeg = double.RadiansToDegrees(Vec.AngleBetween(toAim, launchDir));
            Announce($"refused: {what} is {offDeg:F0} deg off the tube, "
                     + $"past the seeker's {Munition.SeekerFovDeg:F0} deg - point the launcher at it");
            return false;
        }

        double3 launchVel = platformVel + launchDir * Munition.LaunchSpeed;

        // Unguided rounds are slugs: no seeker, lock, boost, fins or command link, so an
        // Interceptor with its steering switched off would be that whole flight model behind
        // guards. Which implementation a munition gets is decided here and only here.
        //
        // platformVel is the frame the round launches into. Passing it here is what makes the body
        // orientable on its very first drawn frame - see the Interceptor constructor.
        _rounds.Add(Munition.Guidance == GuidanceMode.None
            ? new Slug(launchPos, launchVel, aim.Handle, tube + 1, PlatformEcl, frameVel)
            {
                Munition = Munition,
                LaunchAnchorPartFrame = launchAnchorPartFrame,
                Aimpoint = aim,
            }
            : new Interceptor(launchPos, launchVel, aim.Handle, tube + 1, PlatformEcl, frameVel)
            {
                LaunchAnchorPartFrame = launchAnchorPartFrame,
                Aimpoint = aim,
            });
        _salvoTimer = Profile.SalvoSpacing;

        Announce(aim.Kind == AimpointKind.None
                     ? $"round {tube + 1} released - {what}"
                     : $"round {tube + 1} away at {what}");
        return true;
    }

    /// <summary>
    /// Whether a round fired now could steer onto <paramref name="pointEcl"/>, so the designation
    /// ring can answer before a round is spent rather than after.
    ///
    /// <para>Measured from tube zero and the mount rather than from the tube the shot would
    /// actually use — metres apart on a launcher, against kilometres of range, so the angle is the
    /// same to well within the gimbal limit being tested. True when there is nothing to measure
    /// against: a preview should not report a refusal the shot itself would not make.</para>
    /// </summary>
    public bool CanGuideOnto(double3 pointEcl)
    {
        if (Platform is null || Launcher is null) return true;
        if (!LauncherPart.TryGetTubeAxisEcl(Platform, Launcher, PodsPart, Profile, 0, out double3 axis))
        {
            return true;
        }

        // Same rule the shot itself is held to, so the ring answers the question the trigger will.
        // Held true regardless it would read green however far off the tube the click is, which is
        // the one thing this preview exists to say.
        return FireGate.CanGuideOntoAimpoint(Munition.Guidance, operatorHeld: !Profile.Trains,
                                             Munition.SeekerFovRad, axis, pointEcl - MountEcl);
    }

    /// <summary>
    /// Opens a cannon burst along wherever the mount is already laid.
    ///
    /// <para>The operator is the fire-control solution here: mouse aim puts the barrels under the
    /// cursor and this pulls the trigger. It solves no lead for that reason — a lead applied on
    /// top of a shot the operator is eyeballing walks the shells off the point aimed at, and the
    /// automatic path already computes one for the target it chose.</para>
    ///
    /// <para>Every refusal is announced. "Nothing happened" is the same symptom for a safe
    /// launcher, a switched-off cannon, an empty belt and a mount still slewing.</para>
    /// </summary>
    public bool FireBurst()
    {
        if (!Profile.HasCannon) { Announce("refused: no cannon fitted"); return false; }
        if (!_policy.Armed) { Announce("refused: not armed"); return false; }
        if (Platform is null) { Announce("refused: no platform"); return false; }
        if (!IsOperational) { Announce("refused: no launcher part fitted"); return false; }
        if (!_policy.GunsEnabled) { Announce("refused: cannon switched off"); return false; }
        if (_guns.IsEmpty) { Announce("refused: belt empty"); return false; }
        if (!GunsAreLaid) { Announce("refused: cannon still laying"); return false; }

        _manualTrigger = true;
        return true;
    }

    /// <summary>
    /// Whether a manual shot would be taken right now, asked of whichever weapon the launcher
    /// actually carries. A gun-only launcher reads zero from the magazine forever.
    /// </summary>
    public bool ReadyToFire => Profile.TubeCount > 0
                                   ? Ammo > 0 && IsLaid
                                   : Profile.HasCannon && !_guns.IsEmpty && GunsAreLaid;

    /// <summary>
    /// Where the cannon's flash belongs, in Ecl: the centre of the barrel cluster.
    ///
    /// <para>Averaged over the muzzles rather than taken from whichever barrel fired last. The six
    /// sit within 10 cm of each other so the difference cannot be seen, and the average stays on
    /// the cluster axis as the gun elevates instead of hopping barrel to barrel.</para>
    /// </summary>
    public bool TryGunFlashEcl(out double3 ecl, out double3 axisEcl)
    {
        ecl = axisEcl = Vec.Zero;
        if (!Profile.HasCannon || Platform is null || Launcher is null) return false;
        if (GunsPart is not { } guns) return false;

        double3 sum = Vec.Zero;
        int found = 0;
        for (int i = 0; i < Profile.GunMuzzles.Length; i++)
        {
            if (!LauncherPart.TryGetGunMuzzleEcl(Platform, Launcher, guns, Profile, i, PlatformEcl,
                                                 out double3 muzzle, out double3 axis))
            {
                continue;
            }

            sum += muzzle;
            axisEcl = axis;
            found++;
        }

        if (found == 0) return false;

        ecl = sum / found;
        return Vec.IsFinite(ecl);
    }

    /// <summary>Manual trigger: shoots at whatever the radar currently holds.</summary>
    public bool FireAtLock()
    {
        // A gun-only mount is aimed rather than locked on to, so its trigger is a trigger. Making
        // it demand a lock first would leave the one weapon that is meant to be hand-aimed as the
        // only one that cannot be.
        if (Profile.TubeCount == 0) return FireBurst();

        // A bomb is released, not launched at something: it cannot steer, so a lock would tell it
        // nothing and demanding one leaves the trigger dead. Same reasoning as the gun-only mount
        // above -- what is being hand-aimed here is the aircraft.
        if (Munition.Guidance == GuidanceMode.None) return Release();

        if (Radar.Locked is null) { Announce("refused: no lock"); return false; }
        return Fire(Radar.Locked);
    }

    public void Reload()
    {
        _magazine.Resize(Profile.TubeCount, Profile.MagazineDepth);
        _reloadTimer = 0.0;

        // The belt is ammunition too. Refilling only the tubes leaves a launcher whose whole
        // armament is a cannon permanently dry, with the button appearing to do nothing -- and
        // the automatic resupply cannot cover it, since a mount that reloads by hand sets
        // GunReloadSeconds to zero precisely to switch that off.
        if (Profile.HasCannon)
        {
            _guns.Fill(Profile.GunAmmo);
            _guns.Reset();
            _gunReloadTimer = 0.0;
        }

        // A tube whose last round is still flying keeps its body, so the reload is real but nothing
        // reappears on the launcher and it looks like the button did nothing. On a single-rail
        // launcher that is every reload made before the previous round lands.
        int held = 0;
        for (int i = 0; i < Profile.TubeCount; i++)
        {
            if (Magazine.IsOccupied(_rounds, i)) held++;
        }

        if (Profile.TubeCount == 0)
        {
            Announce($"belt replaced by hand - {_guns.Ammo} rounds");
            return;
        }

        Announce(held > 0
                     ? $"launcher reloaded by hand - {held} tube(s) still hold a round in the air"
                     : "launcher reloaded by hand");
    }

    /// <summary>Removes every round in flight without detonating them.</summary>
    /// <summary>
    /// Makes the battery safe: rounds in flight are removed without detonating, and the master arm
    /// goes off.
    ///
    /// <para>Disarming is the point. Clearing the air while armed and auto-engaging simply fires
    /// again on the next lock, which is the opposite of what anyone reaching for a button called
    /// "safe" wants at the moment they reach for it.</para>
    /// </summary>
    public void SafeAll()
    {
        int n = _rounds.Count;
        _rounds.Clear();

        bool wasArmed = _policy.Armed;
        _policy.Armed = false;

        if (n > 0 || wasArmed)
        {
            Announce($"safe - {n} round(s) removed{(wasArmed ? ", master arm off" : "")}");
        }
    }


    private void UpdateRounds(double dt)
    {
        if (_rounds.Count == 0) return;

        // The ground under the launcher, not the launcher. Identical for a site standing still on
        // it, and the difference is the whole behaviour of a store released from something moving.
        // See KsaWorld.GroundVelocityAt.
        double3 platformVelocityEcl = KsaWorld.GroundVelocityAt(Platform!, PlatformEcl);

        // A burst is dozens of shells and the world does not move between them, so the candidate
        // list is built at most once here rather than once per round.
        _contactsFresh = false;

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
            // Everything it could run into, which is not the same list as what it was aimed at,
            // and the geometry that decides whether it truly met any of them.
            if (round is Slug slug)
            {
                slug.Contacts = ContactCandidates();
                slug.Hull = HullTest.Shared;
                slug.Ground = GroundTest.Shared;
            }

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
        // A place on a body has to be re-read every frame. Held as the coordinate it was when it
        // was designated, it is left behind by the planet at ~29.8 km/s - and the round then reads
        // that whole frame velocity as closing speed and turns hard across it.
        if (round.Aimpoint.Kind == AimpointKind.Ground)
        {
            if (KsaWorld.TryGroundAnchorEcl(round.Aimpoint.Handle, round.Aimpoint.Anchor,
                                            out double3 groundEcl, out double3 groundVel))
            {
                round.Aimpoint = round.Aimpoint.Resampled(groundEcl, groundVel);
            }

            return round.Aimpoint.ToTargetState();
        }

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
            var signature = new ThreatModel.ContactSignature(KsaWorld.MeanRadius(target),
                                                             double.PositiveInfinity);

            if (!ThreatModel.InSensorVolume(toTarget, Boresight, Sensor, signature)) return null;
        }

        return new TargetState(
            KsaWorld.PositionEcl(target),
            KsaWorld.VelocityEcl(target),
            KsaWorld.MeanRadius(target),
            target);
    }

    // Every craft a round could run into this frame, the platform excepted: a mount does not
    // shoot the craft it is bolted to, and a shell is armed 33 m from the muzzle anyway.
    //
    // Built at most once a frame rather than once per round -- a burst is dozens of shells and the
    // world does not move between them.
    private List<TargetState> ContactCandidates()
    {
        if (_contactsFresh) return _contactScratch;

        _contactsFresh = true;
        _contactScratch.Clear();

        KsaWorld.CollectVehicles(_blastScratch);
        foreach (Vehicle v in _blastScratch)
        {
            if (ReferenceEquals(v, Platform)) continue;

            _contactScratch.Add(new TargetState(KsaWorld.PositionEcl(v), KsaWorld.VelocityEcl(v),
                                                KsaWorld.MeanRadius(v), v));
        }

        return _contactScratch;
    }

    // Applies a warhead burst. KSA has no partial-damage model exposed, so the effect is binary:
    // anything inside the lethal radius is destroyed, anything between lethal and blast radius is
    // reported as a near miss and survives.
    private void Detonate(IProjectile round)
    {
        // KSA exposes no component damage, so a round aimed at a *part* arrives, reports and
        // destroys nothing. Every other kind falls through to the blast sweep below, which is what
        // makes an airburst over a position do anything at all.
        //
        // The kind, not the handle. Ground carries the body it sits on, so a handle being present
        // does not mean the aimpoint is a part, and testing the handle makes every designated shot
        // arrive, announce, and do nothing whatsoever.
        if (round.Aimpoint.Kind == AimpointKind.Part)
        {
            Announce($"round {round.Tube} arrived at its {round.Aimpoint.Kind} aimpoint");
            return;
        }

        double3 burst = round.PositionEcl;
        // Which fuse fired, because a burst looks the same either way and the flak setting is
        // otherwise unanswerable from a log or a bug report.
        string fuse = round is Slug { BurstOnTime: true } timed
                          ? $" (timed, {timed.FuseSeconds:F2} s)"
                          : string.Empty;

        Announce($"round {round.Tube} detonated{fuse}, miss distance {round.MissDistance:F0} m");

        // Which effect is decided after the blast sweep, once it is known whether anything died.
        _burstKilled = false;

        // Three measurements of the same event, because "the burst went off beside the target"
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
        // Both sides of each of these must be taken at one instant. A round advanced into the
        // frame differenced against a target sampled at the frame start reports a gap that is
        // nothing but ecliptic velocity times the step: 73 m across 2.8 ms. Comparing across
        // instants is the mistake this whole file exists to avoid.
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
            // atBurst agree, the round is killing the target and being painted somewhere else.
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

        // What the round actually met, falling back to what it was aimed at. A kinetic round names
        // its victim: fire control decides what to shoot at, it does not decide what a shell in
        // the air passes through, and scoring a strike on a bystander against the target's lethal
        // range destroys something the round never reached.
        //
        // The separation itself is settled by the fuse, which did the extrapolation properly.
        // Trust that number rather than re-deriving it.
        if ((round.StruckBody ?? round.TargetRef) is Vehicle intended && KsaWorld.IsAlive(intended))
        {
            double lethalRange = round.Munition.LethalRadius + KsaWorld.MeanRadius(intended);
            if (round.MissDistance <= lethalRange)
            {
                // Say why a lethal hit did not kill. Taking control of the target makes it
                // immune, which looks exactly like the round missing unless it is announced.
                if (ReferenceEquals(intended, Platform))
                {
                    Announce($"hit on {KsaWorld.DisplayName(intended)} ignored - it is now the battery's own platform");
                }
                else if (_policy.ProtectControlledVehicle && ReferenceEquals(intended, KsaWorld.ControlledVehicle))
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
            if (_policy.ProtectControlledVehicle && ReferenceEquals(v, KsaWorld.ControlledVehicle)) continue;
            if (_pendingKills.Contains(v)) continue;

            double3 posAtBurst = KsaWorld.PositionEcl(v) + KsaWorld.VelocityEcl(v) * elapsed;
            double dist = Vec.Len(posAtBurst - burst) - KsaWorld.MeanRadius(v);

            if (dist <= round.Munition.LethalRadius)
            {
                _pendingKills.Add(v);
                _burstKilled = true;
            }
            else if (dist <= round.Munition.BlastRadius)
            {
                Announce($"near miss on {KsaWorld.DisplayName(v)} at {dist:F0} m");
            }
        }

        // After the sweep, so a kill and a miss look different. Sized off the charge, which is
        // also what the damage radii come from -- so what is seen and what died cannot drift
        // apart, and a 30 mm shell cannot paint a missile's fireball.
        if (_config.DrawExplosions)
        {
            Detonation.Show(_burstKilled ? Detonation.Fireball : Detonation.Airburst,
                            DrawnBurstEcl(round, burst), round.TargetRef as Vehicle ?? Platform,
                            (float)Warhead.EffectScale(round.Munition.ChargeKg));
        }

        // Outside the drawing switch: a burst that cannot be seen but can be heard is still
        // information, and the effects tick box is about what is drawn.
        Detonation.Bang(DrawnBurstEcl(round, burst), round.TargetRef as Vehicle ?? Platform,
                        (float)Warhead.EffectScale(round.Munition.ChargeKg), _config);
    }

    // Whether the blast sweep just now found something to destroy. Only meaningful inside the
    // detonation it belongs to.
    private bool _burstKilled;

    // Where the burst has to be put so it appears where the round was *drawn*.
    //
    // round.PositionEcl is the analytic position the simulation works in; a vehicle is drawn at
    // its physics position, and the two differ - which is the whole reason DrawAnchor exists and
    // why round bodies are anchored to the tube rather than to the orbit position. The particle
    // system takes Ecl, so the drawn position is converted back through the camera rather than
    // the analytic one being handed over.
    private double3 DrawnBurstEcl(IProjectile round, double3 analyticEcl)
    {
        if (Platform is not { } platform) return analyticEcl;
        if (!KsaWorld.TryVehicleEgo(platform, out double3 platformEgo)) return analyticEcl;
        if (!KsaWorld.TryEgoToEcl(platformEgo + round.OffsetFromPlatform, out double3 drawn))
        {
            return analyticEcl;
        }

        double slip = Vec.Len(drawn - analyticEcl);
        if (slip > 1.0) Log.Debug(() => $"  burst moved {slip:F1} m to where the round is drawn");

        return drawn;
    }

    // Destroys queued targets after the blast sweep, so the engine's vehicle collection is never
    // mutated while it is being walked.
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
        _events.Add(new SystemEvent(_clock, message));
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
        _magazine.Resize(Profile.TubeCount, Profile.MagazineDepth);
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
        _guns.Fill(Profile.GunAmmo);
        _guns.Reset();
        _nextBarrel = 0;
        _gunReloadTimer = 0.0;
    }
}
