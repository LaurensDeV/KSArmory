using System.Runtime.InteropServices;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>Something worth telling the operator about, surfaced in the panel.</summary>
internal readonly record struct SystemEvent(double AtSeconds, string Message);

/// <summary>
/// One weapons system: its launcher, its sensor and its fire control. Pinned to the craft carrying
/// the launcher part, so the site keeps defending itself after the player switches away.
/// </summary>
internal sealed class WeaponSystem(Config config, SystemConfig policy, int launcherOrdinal = 0,
                                   Func<Vehicle, bool>? craftIsEmitting = null)
    : IWeaponSystemView, IManualFire, ISightPicture, IEffectSource
{
    private readonly Config _config = config;

    // From the roster, which knows every system. Null leaves every contact silent, so an
    // anti-radiation round is blind rather than homing on something nobody said was radiating.
    private readonly Func<Vehicle, bool>? _craftIsEmitting = craftIsEmitting;

    private readonly SystemConfig _policy = policy;
    private bool _loadoutSized;

    private readonly List<IProjectile> _rounds = [];

    // A set, because own rounds are rejected from the whole airborne list per contact per frame.
    // Kept in step with _rounds by AddRound and DropRound, the only ways in or out.
    private readonly HashSet<IProjectile> _roundSet = new(ReferenceEqualityComparer.Instance);

    // Turned on from the last drawn attitude, not re-derived: there is no one turn between the
    // opposite directions a store thrown upwards goes up and comes down.
    private readonly Dictionary<IProjectile, DrawnAttitude> _drawnAttitudes = new(ReferenceEqualityComparer.Instance);

    // Rounds drawn with no air to turn them, until the frame the air first does.
    private readonly HashSet<IProjectile> _inVacuum = new(ReferenceEqualityComparer.Instance);

    // The meshes a loose round is drawn with, taken off its subparts while the launcher was alive.
    private sealed record LooseBody(PartModel? Body, PartModel?[] Blades, PartModel? FinSet);
    private readonly Dictionary<IProjectile, LooseBody> _looseBodies = new(ReferenceEqualityComparer.Instance);

    // Rounds further from their launcher than FarBodyMetres, drawn as instances by DrawFarBodies.
    private readonly Dictionary<IProjectile, LooseBody> _farBodies = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Past this a round is drawn as an instance off the camera, not its launcher's subpart, whose
    /// placement is packed to <c>float</c>: 0.6 mm here, about half a metre at 4,000 km.
    /// </summary>
    public const double FarBodyMetres = 10_000.0;

    private readonly record struct DrawnAttitude(doubleQuat Ecl, double Age);

    // Craft an unguided round could run into, rebuilt at most once a frame.
    private readonly List<TargetState> _contactScratch = [];
    private bool _contactsFresh;

    // Somebody else's rounds in the air, rebuilt every frame.
    private readonly List<IContact> _incoming = [];

    // Keyed by the round: a round chasing another has only its opaque TargetRef to find it by.
    private readonly Dictionary<object, IContact> _incomingByHandle = new(ReferenceEqualityComparer.Instance);

    private readonly List<Vehicle> _pendingKills = [];

    // Drained beside the whole-craft kills, behind one solver barrier. Keyed on the craft so a part
    // two rounds reach in one frame is not handed to PartFailureEvent twice.
    private readonly Dictionary<Vehicle, List<Part>> _pendingPartKills =
        new(ReferenceEqualityComparer.Instance);

    private readonly List<DamageablePart> _partScratch = [];

    // What the ground under the burst being applied adds to its blast, measured once per burst.
    private GroundReflection _ground = GroundReflection.FreeAir;

    // And its front, in the air it went off in, built beside it.
    private BlastFront _front = BlastFront.SeaLevel(0.0);
    private readonly List<Part> _partHandles = [];
    private readonly List<int> _failedParts = [];
    private readonly List<(int Index, double Share)> _partShares = [];
    private readonly List<(int Index, double PressureRatio, double GapMetres)> _dentLoads = [];

    // Craft one burst has already damaged. See where it is cleared for why this is not _pendingKills.
    private readonly List<Vehicle> _burstDamaged = [];
    private readonly List<SystemEvent> _events = [];

    private Vehicle? _lastPlatform;

    // Set when the craft dies with rounds up: the body becomes what their offsets are measured from.
    private Celestial? _looseBody;
    private string _looseName = "";
    private double _salvoTimer;
    private double _reloadTimer;
    private double _clock;

    public Vehicle? Platform { get; private set; }

    /// <inheritdoc cref="IEffectSource.EffectBody"/>
    public Celestial? EffectBody => _looseBody ?? Platform?.Parent as Celestial;

    /// <inheritdoc cref="IEffectSource.TryRoundEffectEcl"/>
    public bool TryRoundEffectEcl(IProjectile round, out double3 ecl)
    {
        // Where the craft or body is now, not the last stepped sample: in the viewport pass, where the
        // engine clamps a following camera to the ground, the sample is a step behind the planet.
        if (_looseBody is { } body)
        {
            ecl = KsaWorld.PositionEcl(body) + round.OffsetFromPlatform;
            return Vec.IsFinite(ecl);
        }

        ecl = Vec.Zero;
        if (Platform is not { } platform || Launcher is not { } launcher) return false;

        return LauncherPart.TryGetBodyEcl(platform, launcher, round.LaunchAnchorPartFrame,
                                          round.TravelSinceLaunch, KsaWorld.PositionEcl(platform),
                                          round.LaunchAttitude, out ecl);
    }

    /// <summary>What decides whether a loose system is worth keeping.</summary>
    public int RoundsInFlight => _rounds.Count;

    /// <summary>Captured before the craft went.</summary>
    public string LooseName => _looseName;

    /// <summary>Also its rounds' side; outlives the craft with a loose system's policy.</summary>
    public string? Team => _policy.Iff.OwnTeam;

    /// <summary>True when the operator pinned the platform rather than following control.</summary>
    public bool PlatformPinned { get; private set; }

    public Radar Radar { get; } = new(config, policy);

    public Track? LockedTrack => Radar.Locked;

    public int Ammo => _magazine.Ammo;

    public int MagazineFull => _magazine.Full;

    public IReadOnlyList<IProjectile> Rounds => _rounds;

    /// <summary>
    /// The instant a round stops flying, before it is reaped: a per-frame observer sees it a step
    /// short of the impact. Must not throw: it runs inside the engine's frame hook.
    /// </summary>
    public Action<IProjectile>? RoundEnded;

    public IReadOnlyList<SystemEvent> Events => _events;

    /// <summary>Zero when not reloading.</summary>
    public double ReloadRemaining => _reloadTimer;

    /// <summary>Local "up" at the platform.</summary>
    public double3 Boresight { get; private set; } = new(0, 0, 1);

    public Part? Launcher { get; private set; }

    /// <summary>
    /// Kept while lost, to follow by identity: KSA moves a part whole through a split, and two racks
    /// of one kind renumber when one drops.
    /// </summary>
    public Part? HeldPart { get; private set; }

    public Part? TurretPart { get; private set; }

    public Part? PodsPart { get; private set; }

    /// <summary>
    /// Pods found if declared, legitimately absent if not: a rail has none, and a launcher whose
    /// declared pods were not found must not read tube coordinates against an identity.
    /// </summary>
    public bool TubesResolved => Profile.PodsMarker is null || PodsPart is not null;

    /// <summary>
    /// Beats the set's own pick while it lives. An aimpoint, not a contact handle, so a place on the
    /// ground can be named.
    /// </summary>
    public Aimpoint Designation { get; private set; } = Aimpoint.Nothing;

    /// <summary>What is designated, for the panel to name.</summary>
    public string DesignationName { get; private set; } = "nothing";

    public void Designate(Aimpoint aim, string what, bool asInstrument = false)
    {
        Point(aim, what, log: true);
        SendStoresInTheAir(what, Steerable, asInstrument);
    }

    /// <summary>
    /// Takes a designation without sending anything already in the air at it. A weapon with
    /// several stations points each, then sends its falling stores once for the whole group.
    /// </summary>
    public void Point(Aimpoint aim, string what, bool log)
    {
        Designation = aim;
        DesignationName = what;
        _whyNotDesignated = "";

        // The cursor wins the branch order, so a designation with mouse aim on would never drive.
        // Switched off, so the tick boxes say the order was replaced.
        bool wasOnCursor = _policy.MouseAim || _policy.MouseFire;
        _policy.MouseAim = false;
        _policy.MouseFire = false;

        if (log)
        {
            Log.Info($"{Profile.DisplayName} tracking {what}"
                     + (wasOnCursor ? " (mouse aim and mouse fire off: it now follows this)" : ""));
        }
    }

    // Pushed once, never read live: the round carries its own aimpoint, so it outlives its launcher.
    // Only a store steering its fall, only the one released last, and only inside its reach
    // (CLAUDE.md, "Design decisions worth not re-litigating"). asInstrument is the drop scenario's.
    public void SendStoresInTheAir(string what, IProjectile? steerable, bool asInstrument = false)
    {
        // Designating nothing does not recall a store.
        if (Designation.Kind == AimpointKind.None) return;

        foreach (IProjectile round in _rounds)
        {
            if (round.State != RoundState.Flying || !round.Munition.SteersItsFall) continue;
            if (!asInstrument && !ReferenceEquals(round, steerable)) continue;

            // Before the write: the region is around where the store comes down untouched.
            TailKitReach reach = StoreReach.SolveNow(this, round);

            if (!asInstrument && !StoreRetarget.Reaches(reach, Designation.PositionEcl))
            {
                Announce($"{RoundLabel.For(round.Tube)} keeps its aim: {what} is "
                         + reach.Describe(Designation.PositionEcl));
                continue;
            }

            round.Retarget(Designation);
            Announce($"{RoundLabel.For(round.Tube)} now steering at {what} - "
                     + reach.Describe(Designation.PositionEcl));
        }
    }

    /// <summary>Hands it back to its own set.</summary>
    public void ClearDesignation()
    {
        if (Designation.Kind == AimpointKind.None) return;

        Log.Info($"{Profile.DisplayName} released {DesignationName}");
        Designation = Aimpoint.Nothing;
        DesignationName = "nothing";
    }

    public Part? RadarPart { get; private set; }

    public Part? GunsPart { get; private set; }

    /// <summary>The barrel that recoils inside the cannon, when the profile declares one.</summary>
    public Part? BarrelPart { get; private set; }

    /// <summary>The cannon actuator's cylinder, rod and feed, when the profile declares them.</summary>
    public Part? GunCylinderPart { get; private set; }
    public Part? GunRodPart { get; private set; }
    public Part? GunFeedPart { get; private set; }

    /// <summary>A rotary cannon's barrel cluster, when the profile declares one.</summary>
    public Part? GunRotorPart { get; private set; }

    private readonly GunRotor _rotor = new();

    /// <summary>
    /// Rides the traverse; the whole of a launcher's involvement with a sight. The head on it is an
    /// <c>OpticalHead</c>, crewed separately, which this system neither drives nor knows of.
    /// </summary>
    public Part? OpticBasePart { get; private set; }

    /// <summary>
    /// The search array's current angle, whether or not the launcher has an array to show it.
    /// Cosmetic - the radar model is a cone search.
    /// </summary>
    public double RadarSpinRad { get; private set; }

    // The array's own clock. Its angle is decoration -- a search set never stops and never aims,
    // so nothing reads it back -- and the engine's step beats with the display's frame pacing, so
    // advancing on it turns the array three times as far on alternate frames. See Sim/SmoothedStep.
    private readonly SmoothedStep _spinStep = new();

    // The drives' clock: the raw step carries the display's frame pacing, which moves a rate-limited
    // traverse three times as far on alternate frames. Total time is preserved.
    private readonly SmoothedStep _driveStep = new();

    public Turret Turret { get; } = new();

    private DriveStatus _drives;

    public bool DriveWorks(DriveChannel channel) => _drives.Works(channel);

    public bool AnyDriveRefused => _drives.AnyRefused;

    /// <summary>
    /// This system's own, not the session's, and the shared <see cref="Catalogue"/> instance, so
    /// panel tuning reaches every system running it. Resolved when the launcher part is found.
    /// </summary>
    public LauncherProfile Profile { get; private set; } = LauncherProfile.Unfitted;

    /// <inheritdoc cref="Profile"/>
    public MunitionProfile Munition { get; private set; } = MunitionProfile.None;

    /// <summary>Refused for a different round, whose magazine, bodies and reach this system was not built for.</summary>
    public void FlyRoundsAs(MunitionProfile munition)
    {
        if (munition.Name != Munition.Name) return;

        // Kept: the loadout is re-paired from the catalogue every frame the launcher is found.
        _roundsAs = munition;
        Munition = munition;
    }

    // What FlyRoundsAs asked for, re-applied after each re-pairing while it is still the same round.
    private MunitionProfile? _roundsAs;

    // Falls back to the missile so a launcher with no cannon still answers.
    private MunitionProfile Shell => Profile.GunMunition is { } named
                                         ? Catalogue.MunitionNamed(named)
                                         : Munition;

    /// <inheritdoc cref="Profile"/>
    public SensorProfile Sensor { get; private set; } = SensorProfile.None;

    public bool PlumesEnabled => _config.MotorPlume && _config.DrawExplosions;

    /// <summary>How far the platform moved between the last two frames (m, Ecl).</summary>
    public double3 PlatformStepEcl { get; private set; }

    /// <summary>
    /// Where <see cref="HeldPart"/> sits in the craft's launcher order, and what finds it until a part
    /// is held. It follows the part: the ordinal renumbers when a neighbour drops.
    /// </summary>
    public int LauncherOrdinal { get; private set; } = launcherOrdinal;
    private readonly List<(Part, LauncherProfile)> _launcherScratch = [];

    private bool _hasPlatformSample;
    private bool _loggedSubParts;
    private double _spinPhase;
    private readonly List<Part> _missileBodies = [];
    // What a shell is drawn as (DrawShellBodies), from its munition's BodyModel.
    private PartModel? _shellModel;
    private int _gunShotsFired;
    private double _lastGunShotClock = double.NegativeInfinity;
    private double _lastReleaseClock = double.NegativeInfinity;

    // Taken before anything moves the arm: a later read sees the pose it was last written with.
    private readonly List<Part> _ejectorArms = [];
    private readonly Dictionary<Part, DrivePose> _ejectorArmRest = new(ReferenceEqualityComparer.Instance);
    private readonly List<Part> _finBodies = [];

    private readonly Magazine _magazine = new();

    private readonly GunChannel _guns = new();
    private int _nextBarrel;

    // Picks each barrel's tracers.
    private long[] _barrelRounds = [];
    private double _gunTrace;
    private double _gunReloadTimer;

    // A burst outlives its trigger, so Radar.Locked is often null while its tail is still leaving.
    private Track? _burstTrack;

    // Whether the burst in progress is one the operator fired, which sweeping the gun does not cut.
    private bool _operatorBurst;
    private bool _manualTrigger;

    // Solved by AimPointEcl, kept only while the ring is tracking (TurretMode.Claims).
    private bool _ringIsOnGunLead;

    // As _ringIsOnGunLead: rounds leave along the tube, so a missile committed to the lock while the
    // tube follows the cursor departs up to 180 degrees off.
    private bool _ringIsOnCursor;

    // For a timed fuse; zero with no solution, so no shell is fused for a flight nobody computed.
    private double _gunFlightTime;

    // Kept for the sight: a second solve at draw time reads the target at a later instant.
    private double3 _ringAimEcl;
    private bool _ringAimValid;

    /// <summary>False when stowed, driven by hand or following the cursor.</summary>
    public bool TryRingAimEcl(out double3 aimEcl, out bool isGunLead)
    {
        aimEcl = _ringAimEcl;
        isGunLead = _ringIsOnGunLead;

        return _ringAimValid;
    }

    /// <summary>The claim the fire ladder reads, valid aim or not.</summary>
    public bool RingClaimsGunLead => _ringIsOnGunLead;

    public double GunFlightSeconds => _gunFlightTime;

    public int GunAmmo => _guns.Ammo;

    public bool GunsFiring => _guns.Firing;

    public int GunShotsFired => _gunShotsFired;

    public double GunSecondsSinceShot => _clock - _lastGunShotClock;

    public bool ShellDrawnAsBody(IProjectile round)
        => RoundLabel.IsGunRound(round.Tube)
           && _shellModel is not null && _config.UseRoundBodies && LooseBodyDrawHook.Installed;

    // Simulated seconds, so the sweep holds in a pause and slows with slow motion.
    private double _finTestSeconds;

    // The fin set belonging to a tube, or null if the launcher carries none.
    private Part? FinsFor(int index) => index >= 0 && index < _finBodies.Count ? _finBodies[index] : null;

    // Undeflected unless Config.FinTestSweep moves the drawn blades. TrySeatMissile places only a
    // single fin set; without this the blades sit where the XML left them, inside the bomb.
    private void SeatFinsFor(int tube)
    {
        if (Munition.FinsPerRound <= 0) return;
        if (!LauncherPart.TryGetSeatedPartFrame(PodsPart, Profile, tube, Munition.BodyLength,
                                                out double3 seated)) return;
        if (!LauncherPart.TryGetTubeAxisPartFrame(PodsPart, Profile, tube, out double3 axis)) return;

        doubleQuat rotation = FireGeometry.RotationFromNose(axis);
        for (int blade = 0; blade < Munition.FinsPerRound; blade++)
        {
            if (FinsFor(tube * Munition.FinsPerRound + blade) is not { } part) continue;

            double roll = FinMixer.FinRollRad(blade, Munition.FinsPerRound, Math.PI / 4.0);
            // Through the mixer, so only a pose real steering would produce.
            double deflect = _config.FinTestSweep
                                 ? FinMixer.DeflectionRad(
                                       FinTest.CommandBodyFrame(_finTestSeconds,
                                                                Munition.MaxLateralAccel),
                                       roll, Munition.MaxLateralAccel, Munition.FinDeflectionRad)
                                 : 0.0;
            LauncherPart.TryPlaceFin(part, seated, rotation, Munition.FinHingeStation, roll, deflect);
        }
    }

    // Every blade, not the first: the rest would hang where they were last written.
    private void HideFinsFor(int tube)
    {
        int per = Math.Max(1, Munition.FinsPerRound);
        for (int blade = 0; blade < per; blade++)
        {
            if (FinsFor(tube * per + blade) is { } part) LauncherPart.HideMissile(part);
        }
    }

    /// <summary>False once KSA has refused to place a round body; the gizmo tracers stay as the fallback.</summary>
    public bool RoundBodiesWork { get; private set; } = true;

    /// <summary>Zero means tracers only.</summary>
    public int RoundBodyCount => _missileBodies.Count;

    /// <summary>
    /// Unpaused frames that advanced no simulated time. Expected to stay at zero, since KSA derives
    /// its step from the frame delta; climbing, it explains round bodies stuttering.
    /// </summary>
    public int FramesWithoutSimStep { get; set; }

    private const int BodyTraceEveryFrames = 15;

    private int _bodyFrame;
    private bool _warnedDuplicateTube;
    private bool _bodyGatesReported;

    /// <summary>The launcher part, or the hull without one.</summary>
    public double3 MountEcl { get; private set; }

    /// <summary>
    /// At the instant this update ran, as is everything else the system records, so the overlay
    /// differences against it. Re-read at draw time it is a frame apart: ~500 m.
    /// </summary>
    public double3 PlatformEcl { get; private set; }

    public bool IsOperational => Platform is not null && Launcher is not null;

    /// <summary>
    /// Always true when nothing is being laid, so a launcher that never moves cannot deadlock fire
    /// control; one that should move and cannot holds fire. See <see cref="FireGate"/>.
    /// </summary>
    public bool IsLaid => FireGate.IsLaid(
        aiming: Aiming,
        trains: Profile.Trains,
        drivesAccepted: _drives.AimingAccepted,
        assembliesResolved: TubesResolved,
        settled: Turret.IsLaid(Profile.SettleSeconds));

    /// <summary>
    /// For the cannon, which share only the traverse with the pods: <see cref="IsLaid"/> reads the
    /// pods' latch and subpart.
    /// </summary>
    public bool GunsAreLaid => FireGate.IsLaid(
        aiming: Aiming,
        trains: Profile.Trains,
        drivesAccepted: _drives.GunAimingAccepted,
        assembliesResolved: Profile.GunsMarker is null || GunsPart is not null,
        settled: Turret.IsLaid(Profile.SettleSeconds));

    // Mouse aim and mouse fire count, so the drives settle before a round leaves along the tube.
    private bool Aiming => (_policy.TurretTracking || _policy.MouseAim || _policy.MouseFire)
                           && !_policy.TurretManual && !_policy.TurretSpin;

    private bool _announcedLoss;

    public void PinPlatform(Vehicle? v)
    {
        Platform = v;
        PlatformPinned = v is not null;
        _announcedLoss = false;
        Announce(v is null ? "platform released, following control" : $"platform pinned to {KsaWorld.DisplayName(v)}");
    }

    /// <summary>
    /// <b>Every rendered frame, not every simulation step:</b> <see cref="DrawAnchor"/> pairs
    /// <see cref="PlatformEcl"/> with an Ego position sampled every frame. Reads; advances nothing.
    /// </summary>
    public void SampleWorld()
    {
        ResolvePlatform();
        if (Platform is null)
        {
            Radar.Reset();
            ClearRounds();
            return;
        }

        // Craft gone, not yet retired: the last sample stands, which the hand-over is anchored at.
        if (!KsaWorld.IsAlive(Platform)) return;

        if (!ReferenceEquals(Platform, _lastPlatform))
        {
            if (_lastPlatform is not null && _rounds.Count > 0)
            {
                Announce($"platform changed to {KsaWorld.DisplayName(Platform)}, re-basing {_rounds.Count} round(s) in flight");
                ReanchorRounds(KsaWorld.PositionEcl(Platform));
            }
            _lastPlatform = Platform;
        }

        // Measured rather than derived; Interceptor advances it to the round's instant.
        double3 sampled = KsaWorld.PositionEcl(Platform);
        PlatformStepEcl = _hasPlatformSample ? sampled - PlatformEcl : Vec.Zero;
        _hasPlatformSample = true;
        PlatformEcl = sampled;

        _sampledBody = KsaWorld.ParentBody(Platform);
        _bodyAtSampleEcl = _sampledBody is { } under ? KsaWorld.PositionEcl(under) : Vec.Zero;

        if (ResolveLauncher(Platform) is var (part, profile))
        {
            // One-shot, not "missing last frame": a tree rebuilt during staging fails a read for a
            // frame, and a followed launcher would be refilled.
            bool changed = !ReferenceEquals(profile, Profile) || !_loadoutSized;
            Launcher = part;
            Profile = profile;
            (Munition, Sensor) = Catalogue.LoadoutFor(profile);
            if (_roundsAs is { } flown && flown.Name == Munition.Name) Munition = flown;
            profile.ConfigureTurret(Turret);

            Radar.Sensor = Sensor;

            if (changed)
            {
                _magazine.Resize(profile.TubeCount, profile.MagazineDepth);
                _guns.Fill(profile.GunAmmo);
                _nextBarrel = 0;
                _burstTrack = null;
                _loadoutSized = true;
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
        BarrelPart = Launcher is null ? null : LauncherPart.FindBarrel(Launcher, Profile);
        GunCylinderPart = Launcher is null ? null : LauncherPart.FindGunCylinder(Launcher, Profile);
        GunRodPart = Launcher is null ? null : LauncherPart.FindGunRod(Launcher, Profile);
        GunFeedPart = Launcher is null ? null : LauncherPart.FindGunFeed(Launcher, Profile);
        GunRotorPart = Launcher is null ? null : LauncherPart.FindGunRotor(Launcher, Profile);
        if (Launcher is null) _ejectorArms.Clear();
        else LauncherPart.FindEjectorArms(Launcher, Profile, _ejectorArms);
        foreach (Part arm in _ejectorArms)
        {
            if (!_ejectorArmRest.ContainsKey(arm))
            {
                _ejectorArmRest[arm] = new DrivePose(arm.PositionParentAsmb, arm.Asmb2ParentAsmb);
            }
        }
        OpticBasePart = Launcher is null ? null : LauncherPart.FindOpticBase(Launcher, Profile);
        MountEcl = LauncherPart.ResolveOriginEcl(Platform, Launcher);

        // After the launcher is resolved: the part-relative modes read the part's own mounting.
        Boresight = ResolveBoresight();

        // Once: says whether the subpart Ids survived into the runtime.
        if (Launcher is not null && !_loggedSubParts)
        {
            _loggedSubParts = true;
            // Only with tubes: a gun's round can be the missile's profile, and the search would
            // take the shell bodies.
            if (Profile.TubeCount > 0)
            {
                LauncherPart.FindMissiles(Launcher, Munition, _missileBodies);
                LauncherPart.FindFins(Launcher, Munition, _finBodies);
            }
            else
            {
                _missileBodies.Clear();
                _finBodies.Clear();
            }

            _shellModel = Profile.HasCannon ? LauncherPart.ModelOfTemplate(Shell.BodyModel) : null;

            Log.Info($"launcher subparts: {LauncherPart.DescribeSubParts(Launcher)}");
            Log.Debug($"round bodies found: {_missileBodies.Count}, fin sets {_finBodies.Count} (need {Profile.TubeCount}), "
                      + $"shell model {(_shellModel is null ? "none" : Shell.BodyModel)}");
            if (Profile.HasCannon && Shell.BodyModel is { } shellModel && _shellModel is null)
            {
                Log.Warn($"no subpart template '{shellModel}' - shells will draw as streaks only");
            }
            // Only where declared: a rack or a rail has no turret by design.
            if (Profile.TurretMarker is not null && TurretPart is null)
            {
                Log.Warn("turret subpart not found - the turret will not slew");
            }
            if (Profile.TubeCount > 0 && _missileBodies.Count == 0) Log.Warn("no round bodies - rounds will draw as tracers only");

            // The art declares the bodies and the profile the tubes; validate-parts.py keeps them equal.
            else if (_missileBodies.Count != Profile.TubeCount)
            {
                Log.Warn($"{_missileBodies.Count} round bodies for {Profile.TubeCount} tube(s) on "
                         + $"{Profile.DisplayName}: only the first "
                         + $"{Math.Min(_missileBodies.Count, Profile.TubeCount)} can be drawn.");
            }
        }

    }

    /// <summary>
    /// Gated on the simulation clock, unlike <see cref="SampleWorld"/>, which runs regardless.
    /// </summary>
    /// <param name="airborne">Every round in the world; filtered here, so the radar knows no weapon.</param>
    public void Update(double dt, IReadOnlyList<IContact>? airborne = null)
    {
        if (Platform is null || !KsaWorld.IsAlive(Platform)) return;

        if (double.IsFinite(dt) && dt > 0.0) _finTestSeconds += dt;

        _clock += dt;
        _frameSimStep = double.IsFinite(dt) ? dt : 0.0;

        FillIncoming(airborne);

        Radar.Cue = CuedHandle();
        Radar.Scan(Platform, Boresight, dt, _incoming);
        if (!Profile.FiresBeam) CueDirector();
        AttributeRoundsToTracks();
        UpdateTurret(dt);

        // Rounds before fire control, so a new round waits a frame: integrated in its launch frame,
        // the sample stands still for a step and a frame of ecliptic motion lands in its travel.
        UpdateRounds(dt);
        UpdateFireControl(dt);
        UpdateGunFireControl(dt);
        TrimEvents();

        if (_config.DiagnosticDump)
        {
            Diagnostics.Tick(this, _policy, _clock, _config.DiagnosticIntervalSeconds);
        }
    }

    /// <summary>
    /// The first gate stopping the missiles, or null. Auto-engage is not one: no manual fire path
    /// consults it, and stopping there would hide every gate below.
    /// </summary>
    public string? Hold { get; private set; } = "not started";

    /// <summary>
    /// The ladder asked of the armament the trigger fires, and whether its first rung binds the
    /// trigger: reach and salvo count are automatic fire's economy, which the trigger ignores.
    /// </summary>
    public FireHold? TriggerHold { get; private set; } = new("not started", BindsTrigger: true);

    // Of the handle: only a craft carries a set, so a shell or a coordinate answers false.
    private bool TargetIsEmitting(Track track)
        => track.Contact.Handle is Vehicle craft && (_craftIsEmitting?.Invoke(craft) ?? false);

    // Sim/FireLadder.cs's inputs, sampled into one value so no rung reads a later instant.
    private FireHold? Holding(bool tubes, MunitionProfile munition, Track? locked, string? designated,
                              BeamConditions? beam = null)
    {
        double offTubeDeg = double.NaN;
        bool withinSeeker = !tubes || locked is null
                            || SeekerCanTake(munition, locked.PositionEcl, operatorHeld: false, out offTubeDeg);

        return FireLadder.Holding(
            new FireConditions
            {
                HasPlatform = Platform is not null,
                IsOperational = IsOperational,

                HasTubes = tubes,

                MagazineEmpty = _magazine.IsEmpty,
                ReloadSeconds = _reloadTimer,
                Ammo = Ammo,
                SalvoSeconds = _salvoTimer,
                BeltEmpty = _guns.IsEmpty,

                HasFiringSolution = Radar.IsFiringSolution(locked),
                TrackCount = Radar.Tracks.Count,

                IsLaid = IsLaid,
                GunsAreLaid = GunsAreLaid,
                RingIsOnGunLead = _ringIsOnGunLead,
                RingIsOnCursor = _ringIsOnCursor,
                LaunchAlongTube = Profile.LaunchAlongTube,

                Locked = locked,
                LockedIsEmitting = locked is not null && TargetIsEmitting(locked),
                LockedWithinSeeker = withinSeeker,
                LockedOffTubeDeg = offTubeDeg,
                LockedName = locked?.Contact.DisplayName ?? "",
                DesignatedName = designated,
                Beam = beam,
            },
            _policy,
            munition);
    }

    // A pin, then the controlled craft if it has a launcher, then the current one, then any craft
    // with one, else none. A pinned system stays on its dead craft until the roster retires it.
    private void ResolvePlatform()
    {
        if (PlatformPinned)
        {
            if (!KsaWorld.IsAlive(Platform) && !_announcedLoss)
            {
                Announce("pinned platform lost, holding its rounds for the roster");
                _announcedLoss = true;
            }

            return;
        }

        Vehicle? controlled = KsaWorld.ControlledVehicle;
        if (KsaWorld.IsAlive(controlled) && LauncherPart.IsMounted(controlled))
        {
            SetPlatform(controlled);
            return;
        }

        if (KsaWorld.IsAlive(Platform) && LauncherPart.IsMounted(Platform)) return;

        IReadOnlyList<Vehicle> world = KsaWorld.Vehicles;
        for (int i = 0; i < world.Count; i++)
        {
            Vehicle v = world[i];

            if (LauncherPart.IsMounted(v))
            {
                SetPlatform(v);
                return;
            }
        }

        // Not the controlled craft: the kill path refuses its own platform.
        SetPlatform(null);
    }

    private void SetPlatform(Vehicle? v)
    {
        if (ReferenceEquals(Platform, v)) return;

        if (v is not null && Platform is not null)
        {
            Announce($"system moved to {KsaWorld.DisplayName(v)}");
        }
        Platform = v;
    }

    // Every failure falls back to local up: a cone with no direction detects nothing.
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

    // Assigned by Armaments.Refresh before any system steps; null for a loose system.
    public TargetAllocation? CraftRounds { get; set; }

    // Tells each track how many rounds are already committed to it.
    private void AttributeRoundsToTracks()
    {
        foreach (Track t in Radar.Tracks) t.RoundsAssigned = 0;

        // The craft's whole commitment, or two rails each fire a full salvo.
        if (_config.ShareTargetsAcrossWeapons && CraftRounds is { } craft)
        {
            foreach (Track t in Radar.Tracks) t.RoundsAssigned = craft.CommittedTo(t.Contact.Handle);
            return;
        }

        foreach (IProjectile round in _rounds)
        {
            // Any handle: a track can be a round, or rounds committed to one go uncounted.
            if (round.TargetRef is null) continue;

            if (Radar.TrackFor(round.TargetRef) is { } t) t.RoundsAssigned++;
        }
    }

    private void UpdateFireControl(double dt)
    {
        if (Profile.FiresBeam && Profile.TubeCount == 0 && !Profile.HasCannon)
        {
            UpdateBeamFireControl(dt);
            return;
        }

        // Off the fit, so the panel and the ladder agree on what this system is.
        bool tubes = WeaponFit.Of(Profile, Sensor).FirstOf(ArmamentKind.Tubes) is not null;

        FireHold? held = Holding(tubes, Munition, Radar.Locked, designated: null);
        string? hold = held?.Reason;

        // Logged on change, for whoever is not looking at the panel.
        if (hold != Hold)
        {
            Announce(hold is null ? "clear to fire"
                     : held is { BindsTrigger: false } ? $"auto-engage held: {hold}"
                     : $"holding fire: {hold}");
        }

        Hold = hold;
        TriggerHold = Holding(tubes && TriggerArmament == ArmamentKind.Tubes, TriggerMunition, TriggerTarget,
                              Designation.Kind == AimpointKind.Vehicle ? DesignationName : null);
        if (_salvoTimer > 0.0) _salvoTimer = Math.Max(0.0, _salvoTimer - dt);

        // TubeCount too: a launcher with no tubes is always empty and would reload forever.
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

        // Here, not in the ladder, so "clear to fire" still means the trigger will work.
        if (!_policy.AutoEngage) return;

        Track target = Radar.Locked!;
        if (!ThreatModel.MayEngage(target, _policy.Iff)) return;
        if (!ThreatModel.HasSalvoCapacity(target, _policy.RoundsPerTarget)) return;

        // The set sees further than the round reaches; outside the envelope the magazine is spent on
        // contacts it cannot catch.
        if (!ThreatModel.InEngagementEnvelope(target, Munition)) return;

        // A store, tail kit or not, only falls onto a fixed point and cannot chase a track.
        if (!Munition.Powered) return;

        Fire(target);
    }

    /// <summary>The director on a part, from the roster, which knows which heads are crewed.</summary>
    public Func<Part, OpticalHead?>? DirectorFor { get; set; }

    /// <summary>The contact this craft's picture hands out, taken by a beam on auto-engage.</summary>
    public Func<Vehicle?, object?>? PictureFor { get; set; }

    private OpticalHead? _beamHead;
    private readonly BeamTrigger _beam = new();

    public OpticalHead? BeamHead => _beamHead;

    /// <summary>Whole seconds, as a beam's tally counts.</summary>
    public int BeamSecondsLeft
        => _beamHead is { } head && double.IsFinite(head.ChemicalSecondsLeft)
               ? (int)Math.Ceiling(Math.Max(0.0, head.ChemicalSecondsLeft))
               : 0;

    public bool BeamLasing => _beam.Lasing;

    public int Remaining(ArmamentKind kind) => kind switch
    {
        ArmamentKind.Belt => _guns.Ammo,
        ArmamentKind.Beam => BeamSecondsLeft,
        _ => Ammo,
    };

    // A beam weapon lays nothing: it cues the director on its part and fires its beam while the
    // ladder allows. A designation comes ahead of the set's pick.
    private void UpdateBeamFireControl(double dt)
    {
        _beamHead = Launcher is { } part ? DirectorFor?.Invoke(part) : null;

        // A shift-click reaches every director but only the selected weapon, so the director's
        // designation counts too.
        bool own = Designation.Kind != AimpointKind.None;
        Aimpoint designation = own ? Designation : _beamHead?.Designation ?? Aimpoint.Nothing;
        string designatedName = own ? DesignationName : _beamHead?.DesignationName ?? "";

        Track? target = designation.Kind switch
        {
            AimpointKind.None => Radar.Locked,
            AimpointKind.Vehicle => Radar.TrackFor(designation.Handle),
            _ => null,
        };

        Aimpoint cue = designation.Kind != AimpointKind.None ? designation
                       : target is not null ? AimpointOf(target)
                       : Aimpoint.Nothing;
        if (cue.Kind != AimpointKind.None) _beamHead?.Cue(cue, commands: true);

        BeamConditions beam = new()
        {
            HeadResolved = _beamHead is not null,
            SecondsLeft = _beamHead?.ChemicalSecondsLeft ?? 0.0,
            Masked = _beamHead?.Masked ?? false,
            OnTarget = _beamHead?.OnTarget ?? false,
            CloudInTheWay = _beamHead?.CueBehindCloud ?? false,
            ReachMetres = _beamHead?.Profile.LaserRangeMetres ?? 0.0,
        };

        string? designated = designation.Kind == AimpointKind.Vehicle ? designatedName : null;
        FireHold? held = Holding(tubes: false, MunitionProfile.None, target, designated, beam);
        string? hold = held?.Reason;

        if (hold != Hold)
        {
            Announce(hold is null ? "clear to fire"
                     : held is { BindsTrigger: false } ? $"auto-engage held: {hold}"
                     : $"holding fire: {hold}");
        }

        Hold = hold;
        TriggerHold = held;

        bool autoWants = held is null && _policy.AutoEngage;
        bool mayLase = held is not { BindsTrigger: true } && (cue.Kind == AimpointKind.None || beam.OnTarget);
        _beamHead?.CommandBeam(_beam.Step(dt, autoWants, mayLase));
    }

    private object? CuedHandle()
    {
        if (Designation.Kind == AimpointKind.Vehicle) return Designation.Handle;
        if (Profile.FiresBeam && _beamHead?.Designation is { Kind: AimpointKind.Vehicle } head) return head.Handle;
        if (Profile.FiresBeam && _policy.AutoEngage) return PictureFor?.Invoke(Platform);
        return null;
    }

    // A director on this launcher's part is cued with what the set holds, as a Pantsir's radar cues
    // its 1TPP1; the director takes it only with its own tracking on.
    private void CueDirector()
    {
        if (Launcher is not { } part || DirectorFor?.Invoke(part) is not { } head) return;

        Aimpoint cue = Designation.Kind != AimpointKind.None ? Designation
                       : (Radar.Watched ?? Radar.Locked) is { } track ? AimpointOf(track)
                       : Aimpoint.Nothing;
        if (cue.Kind != AimpointKind.None) head.Cue(cue, commands: false);
    }

    private static Aimpoint AimpointOf(Track track)
        => track.Contact is VehicleContact craft
               ? Aimpoint.OnVehicle(craft.Vehicle, track.PositionEcl, track.VelocityEcl, KsaWorld.MeanRadius(craft.Vehicle))
               : Aimpoint.AtPoint(track.PositionEcl);

    // One press of the trigger: a dwell, which the director's own state then runs or holds.
    private bool PressBeam()
    {
        if (TriggerHold is { BindsTrigger: true } refused)
        {
            Announce($"refused: {refused.Reason}");
            return false;
        }

        double shot = _beamHead?.Profile.Beam?.ShotSeconds ?? 5.0;
        _beam.Press(shot);
        Announce($"lasing for {shot:F0} s");
        return true;
    }

    // From the trunnion, not the part origin: the drive lays the bore parallel to (target - origin),
    // and from the origin the tubes sit 2.5-3.3 m off that line, a fixed miss and so a shrinking
    // angle: 1.7 degrees at 100 m, 0.17 at 1 km.
    private double3 AimOriginEcl
    {
        get
        {
            if (Platform is null || Launcher is null || !Profile.Trains) return MountEcl;

            // A gun-only mount has no pods.
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

    private bool TryCursorAimPartFrame(out double3 partFrame)
    {
        partFrame = default;

        // Mouse fire too, or the round leaves along a stale bearing.
        return (_policy.MouseAim || _policy.MouseFire)
               && Platform is not null
               && KsaWorld.TryCursorAimEcl(AimOriginEcl, out double3 dirEcl)
               && LauncherPart.TryDirectionToPartFrame(Platform, Launcher, dirEcl, out partFrame);
    }

    // Laid so the shell lands where it is sent, flown as a tracked lead is: along the line of sight a
    // craft 9.7 km off drew a shell that came down halfway. False with tubes, whose rounds steer.
    // From the muzzle: a metre of launch height is over ten metres of range on this landing.
    private bool TryGunLay(double3 targetEcl, double3 targetVelocityEcl, double3 targetAccelerationEcl,
                           bool onTheGround, out double3 partFrame)
    {
        partFrame = default;

        if (Profile.TubeCount > 0 || Profile.GunMunition is null) return false;
        if (Platform is not { } platform || Launcher is null) return false;

        double3 muzzle = GunsPart is { } guns
                         && LauncherPart.TryGetGunMuzzleEcl(platform, Launcher, guns, Profile, 0, PlatformEcl,
                                                            out double3 atMuzzle, out _)
                             ? atMuzzle
                             : MountEcl;

        double3 fromMount = targetEcl - PlatformEcl;

        // Out of reach last time and barely moved since: reused for half a second, or until the target
        // moves a fifth of a percent of the range. Each search flies shells for most of their life.
        if (_gunLayReach < 1.0 && Vec.Len2(_groundLayDirection) > 0.0
            && _clock - _gunLaySearchedAt < ReachReuseSeconds
            && Vec.Len(fromMount - _gunLayFromMount) < ReachReuseFraction * Vec.Len(fromMount))
        {
            _gunLayRangeMetres = Vec.Len(targetEcl - muzzle);
            _gunLayShortMetres = Math.Max(0.0, _gunLayRangeMetres - _gunLayReachMetres);
            return LauncherPart.TryDirectionToPartFrame(platform, Launcher, _groundLayDirection, out partFrame);
        }

        // Beyond reach, as far as it goes on the way, over the ground for a target on it
        // (BallisticLead.TrySolveFlownOrReach).
        Func<double, BallisticLead.Place?>? along = onTheGround
            ? BallisticLead.AlongTheGround(GroundTest.Shared, muzzle, targetEcl,
                                           at => KsaWorld.GroundVelocityAt(platform, at),
                                           at => KsaWorld.GroundAccelerationAt(platform, at))
            : null;

        if (!BallisticLead.TrySolveFlownOrReach(muzzle, KsaWorld.VelocityEcl(platform),
                                                KsaWorld.GroundVelocityAt(platform, PlatformEcl),
                                                KsaWorld.GroundAccelerationAt(platform, PlatformEcl),
                                                KsaWorld.BodyVelocityAt(platform),
                                                targetEcl, targetVelocityEcl, targetAccelerationEcl,
                                                null, Shell,
                                                _leadGravityAt ??= LeadGravityAt, _leadDensityAt ??= LeadDensityAt,
                                                _groundLayDirection, _gunLayReach,
                                                out double3 lay, out _, out _gunLayReach, along,
                                                _leadGroundVelocityAt ??= LeadGroundVelocityAt))
        {
            _groundLayDirection = Vec.Zero;
            _gunLayReach = 1.0;
            _gunLayShortMetres = 0.0;
            return false;
        }

        double3 reached = _gunLayReach >= 1.0 ? targetEcl
                          : along?.Invoke(_gunLayReach) is { } place ? place.Position
                          : muzzle + ((targetEcl - muzzle) * _gunLayReach);

        _gunLaySearchedAt = _clock;
        _gunLayFromMount = fromMount;
        _gunLayRangeMetres = Vec.Len(targetEcl - muzzle);
        _gunLayReachMetres = Vec.Len(reached - muzzle);
        _gunLayShortMetres = Math.Max(0.0, _gunLayRangeMetres - _gunLayReachMetres);
        _groundLayDirection = lay - muzzle;
        return LauncherPart.TryDirectionToPartFrame(platform, Launcher, _groundLayDirection, out partFrame);
    }

    // Kept between frames, except the range and the shortfall, which are this frame's (cleared in UpdateTurret).
    private double _gunLayReach = 1.0;
    private double _gunLayReachMetres;
    private double _gunLaySearchedAt = double.NegativeInfinity;
    private double3 _gunLayFromMount;
    private double _gunLayRangeMetres;
    private double _gunLayShortMetres;

    private const double ReachReuseSeconds = 0.5;
    private const double ReachReuseFraction = 0.002;

    /// <summary>Zero within reach, and when no gun lay is driving the turret.</summary>
    public double GunLayShortMetres => _gunLayShortMetres;

    /// <summary>Zero when no gun lay is driving.</summary>
    public double GunLayRangeMetres => _gunLayRangeMetres;

    private double _frameSimStep;
    private readonly ContactHold _designatedContact = new();
    private Vehicle? _contactOf;

    private bool HeldContact(Vehicle designated, bool touching)
    {
        if (!ReferenceEquals(_contactOf, designated))
        {
            _contactOf = designated;
            _designatedContact.Reset();
        }
        return _designatedContact.Update(touching, _frameSimStep);
    }

    private bool _lastTouching, _lastRests;
    private int _touchFlips, _restFlips;
    private double _contactSince;

    // Once a second under a verbose log: the flips decide the lay's acceleration and its reach search.
    private bool SayContact(Vehicle designated, bool touching, bool rests)
    {
        if (touching != _lastTouching) _touchFlips++;
        if (rests != _lastRests) _restFlips++;
        _lastTouching = touching;
        _lastRests = rests;
        _contactSince += _frameSimStep;
        if (_contactSince >= 1.0)
        {
            if (_touchFlips > 0 || _restFlips > 0)
            {
                Log.Debug($"{KsaWorld.DisplayName(designated)}: ground contact flipped {_touchFlips} time(s) in "
                          + $"{_contactSince:F1} s, the lay's {_restFlips}; resting now {rests}");
            }
            _touchFlips = 0;
            _restFlips = 0;
            _contactSince = 0.0;
        }
        return rests;
    }

    private bool TryGunGroundLay(double3 groundEcl, double3 groundVelocityEcl, out double3 partFrame)
    {
        partFrame = default;

        return Platform is { } platform
               && TryGunLay(groundEcl, groundVelocityEcl, KsaWorld.GroundAccelerationAt(platform, groundEcl),
                            onTheGround: true, out partFrame);
    }

    // Where the last ground lay pointed, so the next solve starts beside it.
    private double3 _groundLayDirection;

    // The target while the missiles have the engagement, the ballistic lead once the cannon do: one
    // ring serves both. At 4 km a shell flies 4.5 s, in which a 300 m/s target crosses 1.4 km.
    private double3 AimPointEcl(Track aim)
    {
        _ringIsOnGunLead = false;
        _gunFlightTime = 0.0;
        _ringAimEcl = aim.PositionEcl;
        _ringAimValid = true;

        if (!GunsHaveTheEngagement(aim)) return aim.PositionEcl;

        MunitionProfile shell = Catalogue.MunitionNamed(Profile.GunMunition!);

        // The flight time from the same solve as the aim, so a timed fuse bursts where the gun points.
        // Re-solved from nothing before giving up: a solve alternating with a failure swings the
        // barrels through the whole lead every other frame.
        if (!SolveGunLead(aim, shell, _gunLeadDirection, out double3 lead, out double flightTime)
            && (Vec.Len2(_gunLeadDirection) == 0.0
                || !SolveGunLead(aim, shell, Vec.Zero, out lead, out flightTime)))
        {
            _gunLeadDirection = Vec.Zero;
            return aim.PositionEcl;
        }

        _gunFlightTime = flightTime;
        _gunLeadDirection = lead - MountEcl;

        // Recorded, not recomputed at the missile gate: a failed solve leaves the ring on the target.
        _ringIsOnGunLead = true;
        _ringAimEcl = lead;

        return lead;
    }

    private bool SolveGunLead(Track aim, MunitionProfile shell, double3 hint, out double3 lead, out double flightTime)
        => BallisticLead.TrySolveFlown(MountEcl, KsaWorld.VelocityEcl(Platform!),
                                       KsaWorld.GroundVelocityAt(Platform!, PlatformEcl),
                                       KsaWorld.GroundAccelerationAt(Platform!, PlatformEcl),
                                       KsaWorld.BodyVelocityAt(Platform!),
                                       aim.PositionEcl, aim.VelocityEcl, aim.AccelerationEcl, aim.DragShape,
                                       shell, _leadGravityAt ??= LeadGravityAt,
                                       _leadDensityAt ??= LeadDensityAt,
                                       hint, out lead, out flightTime,
                                       _leadGroundVelocityAt ??= LeadGroundVelocityAt);

    // A solve starts next to its last answer, a pass or two rather than the whole search.
    private double3 _gunLeadDirection;

    private double3 LeadGravityAt(double3 positionEcl)
        => Platform is null ? Vec.Zero : KsaWorld.GravityAt(Platform, positionEcl);

    // The air alone: a target on the sea sits on the waterline, and a lay reading the ocean under it stops its shell.
    private double LeadDensityAt(double3 positionEcl)
        => Platform is null ? 1.0 : KsaWorld.AirDensityRatioAt(Platform, positionEcl);

    // On a turning world the air where the shell is does not move with the air at the mount.
    private double3 LeadGroundVelocityAt(double3 positionEcl)
        => Platform is null ? Vec.Zero : KsaWorld.GroundVelocityAt(Platform, positionEcl);

    private Func<double3, double3>? _leadGravityAt;
    private Func<double3, double>? _leadDensityAt;
    private Func<double3, double3>? _leadGroundVelocityAt;

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

        if (_barrelRounds.Length != Profile.GunMuzzles.Length) _barrelRounds = new long[Profile.GunMuzzles.Length];
        long fedThrough = _barrelRounds[barrel]++;

        if (!LauncherPart.TryGetGunMuzzleEcl(Platform, Launcher, guns, Profile, barrel,
                                             PlatformEcl, out double3 muzzle, out double3 axis))
        {
            return;
        }

        MunitionProfile shell = Catalogue.MunitionNamed(Profile.GunMunition!);

        // A round leaves with the craft's motion and flies in the ground's.
        double3 platformVel = KsaWorld.VelocityEcl(Platform);
        double3 frameVel = KsaWorld.GroundVelocityAt(Platform, PlatformEcl);

        // In the launcher's frame: the tracer and body are placed from this plus the travel, never
        // from the analytic position, metres off a landed craft.
        TubeGeometry.TryGunMuzzlePartFrame(Profile, barrel, guns.PositionParentAsmb,
                                           guns.Asmb2ParentAsmb, out double3 muzzlePart);

        bool designatedCraft = track is null && Designation.Kind == AimpointKind.Vehicle
                               && Designation.Handle is Vehicle designated && KsaWorld.IsAlive(designated);

        // A null track is a craft designated by hand, or a burst's tail. -(barrel + 1) is the tube
        // sentinel for a shell, which only RoundLabel decodes.
        Slug slug = new(muzzle, platformVel + axis * shell.LaunchSpeed,
                        track?.Contact.Handle ?? (designatedCraft ? Designation.Handle : null),
                        -(barrel + 1), PlatformEcl, frameVel)
        {
            Munition = shell,
            LaunchAnchorPartFrame = muzzlePart,
            ReleaseHeadingEcl = axis,
            LaunchAttitude = Platform?.Asmb2Ego ?? doubleQuat.Identity,

            // As the lead flies it: first order is 3.8 m off at 15.7 km at 60 fps, against 1.3 m.
            SecondOrder = true,

            // At the starting speed drag is always too large: 2.8 m short at 23 km, 0.08 m this way.
            DragAtMidpointVelocity = true,

            Tracer = TracerLook.IsTracer(fedThrough, barrel, shell.TracerEvery),
        };
        if (designatedCraft) slug.Aimpoint = Designation;

        if (track is not null)
        {
            slug.Aimpoint = Aimpoint.OnVehicle(track.Contact.Handle, track.PositionEcl, track.VelocityEcl,
                                               track.Contact.MeanRadius);

            // Without a solve it is zero, and the shell falls back to its proximity fuse.
            if (shell.TimedFuse) slug.FuseSeconds = _gunFlightTime;

            // A gun still laying misses by this angle times the range, which reads like a lead error.
            if (_ringIsOnGunLead && Vec.Len2(_gunLeadDirection) > 0.0)
            {
                double off = Vec.AngleBetween(axis, _gunLeadDirection);
                double reach = Vec.Len(_gunLeadDirection);
                Log.Debug(() => $"  shell away: barrel {off * 1000.0:F2} mrad off the lead, "
                                + $"{off * reach:F1} m at the aim point, fuse {_gunFlightTime:F2} s");
            }
        }
        AddRound(slug);
        _gunShotsFired++;
        _lastGunShotClock = _clock;
    }

    // Not gated on the missiles: inside their 1.2 km minimum the cannon are all there is.
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

        // Rechecked: a frame passes between FireBurst's click and this step.
        bool manual = _manualTrigger && _policy.GunsEnabled
                      && IsOperational && GunsAreLaid;

        bool wantToFire = manual
                          || _policy.AutoEngage && _policy.GunsEnabled
                          && IsOperational && GunsAreLaid
                          && Radar.Locked is { } locked
                          && ThreatModel.MayEngage(locked, _policy.Iff)
                          && locked.Range >= Shell.MinRange
                          && locked.Range <= Shell.MaxRange;

        _gunTrace += dt;
        if (_gunTrace >= 1.0)
        {
            _gunTrace = 0.0;
            Log.Debug(() =>
            {
                string range = Radar.Locked is { } t ? $"{t.Range:F0} m" : "no lock";
                return $"cannon: want={wantToFire} ammo={_guns.Ammo} burst={_guns.BurstRemaining} "
                       + $"cd={_guns.Cooldown:F3} auto={_policy.AutoEngage} "
                       + $"enabled={_policy.GunsEnabled} laid={GunsAreLaid} drive={_drives.Works(DriveChannel.Guns)} "
                       + $"part={(GunsPart is not null)} range={range} "
                       + $"envelope={Shell.MinRange:F0}-{Shell.MaxRange:F0} m";
            });
        }

        // Its own timer: the launcher's reload gate returns early while the tubes are empty.
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

        // Latched: GunChannel runs a burst to its end after the lock is lost.
        if (wantToFire) _burstTrack = Radar.Locked;

        // Whose the burst is, decided as it starts: a step that finds none in progress may begin one.
        if (_guns.BurstRemaining <= 0) _operatorBurst = manual;

        // A flickering lock does not cut a burst short.
        bool mayContinue = FireGate.BurstMayContinue(_operatorBurst, GunsAreLaid,
                                                     _burstTrack is { Contact.IsAlive: false });

        int fired = _guns.Step(dt, wantToFire, Profile, mayContinue);
        _manualTrigger = false;

        // A shell must not carry a destroyed vehicle.
        if (_burstTrack is { } held && !held.Contact.IsAlive) _burstTrack = null;
        if (fired <= 0) return;

        for (int i = 0; i < fired; i++) FireGun(_burstTrack);
        Log.Debug(() => $"cannon: {fired} round(s) away, {_guns.Ammo} left");
        if (_guns.IsEmpty) Announce("cannon belt empty");
        if (_guns.BurstRemaining <= 0) _burstTrack = null;
    }

    // Ground is re-read every frame: held in the ecliptic it is left behind at ~29.8 km/s plus the
    // site's spin. See Aimpoint.NeedsResampling.
    private bool TryDesignatedAim(out double3 partFrame)
    {
        partFrame = Vec.Zero;

        if (Platform is null || Launcher is null)
        {
            WhyNotDesignated("no platform or no launcher part");
            return false;
        }

        if (Designation.Kind == AimpointKind.Vehicle)
        {
            if (Designation.Handle is not Vehicle craft || !KsaWorld.IsAlive(craft))
            {
                ClearDesignation();
                return false;
            }

            Designation = Designation.Resampled(KsaWorld.PositionEcl(craft), KsaWorld.VelocityEcl(craft));
        }
        else if (Designation.NeedsResampling)
        {
            if (!KsaWorld.TryGroundAnchorEcl(Designation.Handle, Designation.Anchor,
                                             out double3 groundEcl, out double3 groundVel))
            {
                WhyNotDesignated("the ground anchor would not resolve");
                return false;
            }

            Designation = Designation.Resampled(groundEcl, groundVel);
        }

        // From the trunnion: see AimOriginEcl.
        double3 origin = AimOriginEcl;

        // Keyed apart when the shell is thrown short, so moving out of reach and back is said again.
        void SayGunLaid(string how)
        {
            double km = Vec.Len(Designation.PositionEcl - origin) / 1000.0;

            if (_gunLayShortMetres > 1.0)
            {
                WhyNotDesignated("driving beyond reach",
                                 $"a gun laid to its longest reach, "
                                 + $"{(_gunLayRangeMetres - _gunLayShortMetres) / 1000.0:F1} km, "
                                 + $"{_gunLayShortMetres / 1000.0:F1} km short of it at {km:F1} km");
            }
            else
            {
                WhyNotDesignated("driving", $"{how} {km:F1} km out");
            }
        }

        // A place on the ground is somewhere a gun's shell has to land, not a line to lay it on.
        if (Designation.Kind == AimpointKind.Ground
            && TryGunGroundLay(Designation.PositionEcl, Designation.VelocityEcl, out partFrame))
        {
            SayGunLaid("a gun laid to land");
            return true;
        }

        // So is a craft: resting, with the ground's acceleration; flying, with its own.
        if (Designation.Kind == AimpointKind.Vehicle && Designation.Handle is Vehicle designated
            && KsaWorld.RestsOnSurface(designated) is var touching
            && SayContact(designated, touching, HeldContact(designated, touching)) is var rests
            && TryGunLay(Designation.PositionEcl, Designation.VelocityEcl,
                         rests
                             ? KsaWorld.GroundAccelerationAt(Platform, Designation.PositionEcl)
                             : KsaWorld.AccelerationEcl(designated),
                         onTheGround: rests, out partFrame))
        {
            SayGunLaid("a gun laid on it");
            return true;
        }

        if (!LauncherPart.TryDirectionToPartFrame(Platform, Launcher,
                                                  Designation.PositionEcl - origin, out partFrame))
        {
            WhyNotDesignated("the direction would not convert into the part's frame");
            return false;
        }

        if (Vec.Len2(partFrame) < 0.5)
        {
            WhyNotDesignated("the direction came back degenerate",
                             $"(len2 {Vec.Len2(partFrame):E2}, range "
                             + $"{Vec.Len(Designation.PositionEcl - origin) / 1000.0:F1} km)");
            return false;
        }

        WhyNotDesignated("driving", $"at {Vec.Len(Designation.PositionEcl - origin) / 1000.0:F1} km");

        return true;
    }

    // Once per state, keyed on the state, never the message: one carrying the range changes every
    // frame, a synchronous file write each.
    private string _whyNotDesignated = "";

    private void WhyNotDesignated(string state, string detail = "")
    {
        if (_whyNotDesignated == state) return;

        _whyNotDesignated = state;

        string tail = detail.Length > 0 ? $" {detail}" : "";

        // Keyed apart only so a change between driving states is said again.
        if (state.StartsWith("driving", StringComparison.Ordinal))
        {
            Log.Info($"{Profile.DisplayName}: turret on {DesignationName} -- driving{tail}");
            return;
        }

        Log.Warn($"{Profile.DisplayName}: designation on {DesignationName} is not driving the "
                 + $"turret -- {state}{tail}");
    }

    // In the part's own frame, not Ecl: the platform's attitude relates the two. Tracking takes the
    // lock, then the most urgent threat, so the mount watches the sky before the lock settles.
    private void UpdateTurret(double dt)
    {
        // Evened out for the drives only; anything integrating the world takes the raw step.
        dt = _driveStep.Next(dt);

        // Cleared so the lead claim read after the chain is the one AimPointEcl made this frame.
        _ringIsOnGunLead = false;
        _ringAimValid = false;
        _gunLayRangeMetres = 0.0;
        _gunLayShortMetres = 0.0;
        bool watchedLay = false;
        TurretRung rung;

        if (_policy.TurretSpin)
        {
            rung = TurretRung.Spin;
            // A bearing running away at the slew rate, with the manual elevation: both axes at once.
            _spinPhase = Turret.WrapPi(_spinPhase + Profile.SlewRateRad * dt);
            Turret.Point(_spinPhase, double.DegreesToRadians(_policy.TurretManualElevationDeg));
        }
        else if (_policy.TurretManual)
        {
            rung = TurretRung.Manual;
            Turret.Point(double.DegreesToRadians(_policy.TurretManualBearingDeg),
                         double.DegreesToRadians(_policy.TurretManualElevationDeg));
        }
        else if (TryCursorAimPartFrame(out double3 cursorFrame))
        {
            // Ahead of the radar and the tracking switch: the operator is the sensor.
            rung = TurretRung.Cursor;

            // Over ground a gun is laid to land there; over sky or a craft the operator is the solution.
            if (KsaWorld.TryCursorGroundEcl(out double3 ground)
                && TryGunGroundLay(ground, KsaWorld.GroundVelocityAt(Platform!, ground), out double3 layFrame))
            {
                Turret.Track(layFrame);
            }
            else
            {
                Turret.Track(cursorFrame);
            }
        }
        else if (Designation.Kind != AimpointKind.None && TryDesignatedAim(out double3 designated))
        {
            // Ahead of the tracking switch: naming something is the instruction to follow it.
            rung = TurretRung.Designated;
            Turret.Track(designated);
        }
        else if (!_policy.TurretTracking)
        {
            rung = TurretRung.Stowed;
            Turret.Stow();
        }
        else
        {
            Track? aim = Radar.Locked ?? MostUrgentThreat();
            double3 aimEcl = aim is null ? Vec.Zero : AimPointEcl(aim) - AimOriginEcl;

            if (aim is not null && Platform is not null
                && LauncherPart.TryDirectionToPartFrame(Platform, Launcher, aimEcl, out double3 partFrame))
            {
                rung = TurretRung.Tracking;
                Turret.Track(partFrame);
                WatchLay(aim, aimEcl, partFrame);
                watchedLay = true;
            }
            else
            {
                rung = TurretRung.Stowed;
                Turret.Stow();
            }
        }

        (_ringIsOnGunLead, _ringIsOnCursor) = TurretMode.Claims(rung, _ringIsOnGunLead);
        if (!watchedLay) _layHandle = null;

        Turret.Update(dt, Profile.SlewRateRad, Profile.ElevationRateRad);

        // Each assembly latches its own refusal, and the drive keeps integrating, so the facing line
        // still tells a refused write from a wrong solution.
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

        if (GunsPart is not null && _drives.Works(DriveChannel.Guns)
            && !LauncherPart.TryApplyGunAim(GunsPart, Profile, Turret.BearingRad, Turret.ElevationRad))
        {
            Refuse(DriveChannel.Guns, "cannon elevation");
        }

        // Drawn only: the round has left before the barrel moves, so a refused write freezes a
        // barrel and nothing that shoots. Held with the cannon, or it slides off a frozen breech.
        if (BarrelPart is not null && _drives.Works(DriveChannel.Recoil) && _drives.Works(DriveChannel.Guns)
            && !LauncherPart.TryApplyBarrelAim(BarrelPart, Profile, Turret.BearingRad, Turret.ElevationRad,
                                               GunRecoil.Offset(_clock - _lastGunShotClock,
                                                                Profile.GunRecoilMetres,
                                                                Profile.GunRecoilSeconds,
                                                                Profile.GunReturnSeconds)))
        {
            Refuse(DriveChannel.Recoil, "barrel recoil");
        }

        // Drawn only: EjectSpeed has already thrown the round, and the arms show it.
        if (_ejectorArms.Count > 0 && _drives.Works(DriveChannel.Ejector))
        {
            double reach = Ejector.ArmOffset(_clock - _lastReleaseClock, Profile.EjectSpeed,
                                             Profile.EjectorStrokeMetres, Profile.EjectorReturnSeconds);
            foreach (Part arm in _ejectorArms)
            {
                DrivePose rest = _ejectorArmRest[arm];
                if (!LauncherPart.TryApplyPose(arm, rest with { Position = rest.Position + new double3(reach, 0.0, 0.0) },
                                               "ejector arm"))
                {
                    Refuse(DriveChannel.Ejector, "ejector arm");
                    break;
                }
            }
        }

        if ((GunCylinderPart ?? GunRodPart ?? GunFeedPart) is not null
            && _drives.Works(DriveChannel.Linkage) && _drives.Works(DriveChannel.Guns))
        {
            (DrivePose cylinder, DrivePose rod) = TubeGeometry.ActuatorPoses(Profile, Turret.BearingRad, Turret.ElevationRad);
            bool written = (GunCylinderPart is null || LauncherPart.TryApplyPose(GunCylinderPart, cylinder, "actuator cylinder"))
                && (GunRodPart is null || LauncherPart.TryApplyPose(GunRodPart, rod, "actuator rod"))
                && (GunFeedPart is null || LauncherPart.TryApplyPose(
                        GunFeedPart, TubeGeometry.FeedPose(Profile, Turret.BearingRad, Turret.ElevationRad), "feed"));
            if (!written) Refuse(DriveChannel.Linkage, "cannon actuator and feed");
        }

        if (GunRotorPart is not null && _drives.Works(DriveChannel.Linkage) && _drives.Works(DriveChannel.Guns))
        {
            bool firing = _clock - _lastGunShotClock <= 2.0 * Profile.GunRoundInterval;
            _rotor.Update(dt, firing, GunRotor.FiringRateRadPerSec(Profile),
                          Profile.GunRotorSpinUpSeconds, Profile.GunRotorSpinDownSeconds);
            if (!LauncherPart.TryApplyPose(GunRotorPart,
                    TubeGeometry.RotorPose(Profile, Turret.BearingRad, Turret.ElevationRad, _rotor.AngleRad),
                    "barrel cluster"))
            {
                Refuse(DriveChannel.Linkage, "barrel cluster");
            }
        }

        // Before any head updates, as KSArmoryMod orders them: the head reads this transform back.
        if (OpticBasePart is not null && _drives.Works(DriveChannel.Optic)
            && !LauncherPart.TryApplyOpticBase(OpticBasePart, Profile, Turret.BearingRad))
        {
            Refuse(DriveChannel.Optic, "director base");
        }

        // Off the clock, not the track. Turned with no array modelled, since the scope reads it; not
        // when the engine froze the mesh, so the sweep stops with it.
        bool frozen = RadarPart is not null && !_drives.Works(DriveChannel.Radar);

        if (Profile.SearchRadarFaces > 0 && !frozen && !_policy.SearchRadarStopped)
        {
            RadarSpinRad = Turret.WrapPi(
                RadarSpinRad + Profile.SearchRadarRpm * (Math.Tau / 60.0) * _spinStep.Next(dt));
        }

        if (RadarPart is not null && !frozen
            && !LauncherPart.TryApplyRadarSpin(RadarPart, Profile, Turret.BearingRad, RadarSpinRad))
        {
            Refuse(DriveChannel.Radar, "search array spin");
        }
    }

    // A jump past the settle tolerance restarts the settle clock, which looks like the mount bobbing
    // while the gun holds fire. One jump onto the lead as a target comes into reach is expected.
    private object? _layHandle;
    private double3 _layAimEcl;
    private double3 _layAimPart;
    private bool _layOnLead;
    private double _layFlightSeconds;
    private double3 _layAcceleration;
    private double _layJumpLoggedAt = double.NegativeInfinity;
    private int _layJumpsUnlogged;

    // Splits a jump into the aim moving and the mount turning under it, with what fed the lead.
    private void WatchLay(Track aim, double3 aimEcl, double3 partFrame)
    {
        object handle = aim.Contact.Handle;

        if (ReferenceEquals(handle, _layHandle) && Platform is not null)
        {
            double bearing = Math.Abs(Turret.WrapPi(Turret.BearingTo(partFrame) - Turret.BearingTo(_layAimPart)));
            double elevation = Math.Abs(Turret.ElevationTo(partFrame) - Turret.ElevationTo(_layAimPart));

            if (Math.Max(bearing, elevation) > Turret.OnTargetToleranceRad)
            {
                double world = Vec.AngleBetween(aimEcl, _layAimEcl);
                double mount = LauncherPart.TryDirectionToPartFrame(Platform, Launcher, _layAimEcl,
                                                                    out double3 lastInThisFrame)
                    ? Vec.AngleBetween(lastInThisFrame, _layAimPart)
                    : double.NaN;

                if (_clock - _layJumpLoggedAt >= 0.5)
                {
                    int unlogged = _layJumpsUnlogged;
                    string lead = $"lead {(_layOnLead ? "solved" : "none")} -> {(_ringIsOnGunLead ? "solved" : "none")}";

                    Log.Debug($"lay jumped {double.RadiansToDegrees(bearing):F1} deg in bearing and "
                             + $"{double.RadiansToDegrees(elevation):F1} in elevation on {aim.Contact.DisplayName}: "
                             + $"the aim moved {double.RadiansToDegrees(world):F2} deg in the world and the mount "
                             + $"turned {double.RadiansToDegrees(mount):F2} under it; {lead}, flight "
                             + $"{_layFlightSeconds:F2} -> {_gunFlightTime:F2} s, target acceleration "
                             + $"{Vec.Len(_layAcceleration):F1} -> {Vec.Len(aim.AccelerationEcl):F1} m/s2"
                             + (unlogged > 0 ? $" ({unlogged} more since the last line)" : ""));

                    _layJumpLoggedAt = _clock;
                    _layJumpsUnlogged = 0;
                }
                else
                {
                    _layJumpsUnlogged++;
                }
            }
        }

        _layHandle = handle;
        _layAimEcl = aimEcl;
        _layAimPart = partFrame;
        _layOnLead = _ringIsOnGunLead;
        _layFlightSeconds = _gunFlightTime;
        _layAcceleration = aim.AccelerationEcl;
    }

    private void Refuse(DriveChannel channel, string what)
    {
        if (_drives.Refuse(channel)) Announce($"{what} rejected by the engine; that assembly is frozen");
    }

    /// <summary>
    /// Each tube's subpart, hidden until its round is in the air; tube N is body N-1. A shell's
    /// attitude only is turned here, and <see cref="DrawShellBodies"/> draws it.
    /// <para><b>Every rendered frame, not every simulation step</b>, or a frame with no step leaves
    /// the bodies behind. It reads state and changes none.</para>
    /// </summary>
    public void SyncRoundBodies()
    {
        if (Platform is not { } platform || Launcher is not { } launcher || !KsaWorld.IsAlive(platform)) return;
        if ((_missileBodies.Count == 0 && _shellModel is null) || !RoundBodiesWork) return;

        // Hidden, or the bodies stay frozen where last written.
        if (!_config.UseRoundBodies)
        {
            for (int i = 0; i < _missileBodies.Count; i++) LauncherPart.HideMissile(_missileBodies[i]);
            for (int i = 0; i < _finBodies.Count; i++) LauncherPart.HideMissile(_finBodies[i]);
            return;
        }

        TurnShellBodies(platform, launcher);

        // Both counts, from two files that can disagree: one more body than tube would throw from
        // inside the frame hook.
        int slots = Math.Min(_missileBodies.Count, Profile.TubeCount);
        if (slots <= 0) return;

        // Once per system, every gate at once: an undrawn body looks the same whichever stopped it.
        if (!_bodyGatesReported)
        {
            _bodyGatesReported = true;
            Log.Info($"round bodies for {Profile.PartId}: {_missileBodies.Count} bodies, "
                     + $"{Profile.TubeCount} tubes, tubesResolved={TubesResolved}, "
                     + $"plan[0]={_magazine.Plan(0, false)}, ammo={Ammo}");
        }

        Span<bool> flying = stackalloc bool[slots];

        _bodyFrame++;
        bool trace = Log.Threshold <= Log.Level.Debug && _bodyFrame % BodyTraceEveryFrames == 0;

        foreach (IProjectile round in _rounds)
        {
            int index = round.Tube - 1;
            if (index < 0 || index >= slots) continue;

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

            // What it left along, captured at launch, not the live tube, which rolls rounds in flight
            // with the craft. Boresight is the last resort only: a bomb sight's is square to the tube.
            double3 release = Vec.IsFinite(round.ReleaseHeadingEcl)
                              && Vec.Len2(round.ReleaseHeadingEcl) > 1e-9
                                  ? round.ReleaseHeadingEcl
                                  : LauncherPart.TryGetTubeAxisEcl(platform, launcher, PodsPart, Profile,
                                                                   index, out double3 tubeEcl)
                                      ? tubeEcl
                                      : Boresight;

            // In vacuum nothing weathervanes. Turned over the round's own age, so a pause turns nothing.
            double density = KsaWorld.MediumDensityRatioAt(platform, round.PositionEcl);
            DrawnAttitude drawn = _drawnAttitudes.TryGetValue(round, out DrawnAttitude was)
                                      ? was
                                      : new DrawnAttitude(LauncherPart.ReleaseAttitudeEcl(launcher, release,
                                                                                          round.LaunchAttitude),
                                                          0.0);

            drawn = Carry(drawn, round, density);
            _drawnAttitudes[round] = drawn;

            // Not flying, so the loop below seats and hides its subpart, and DrawFarBodies draws it.
            if (_farBodies.ContainsKey(round) || Vec.Len(round.TravelSinceLaunch) > FarBodyMetres)
            {
                if (!_farBodies.ContainsKey(round)) _farBodies[round] = BodiesOf(round, index);
                continue;
            }

            if (!LauncherPart.TryPlaceMissile(platform, launcher, _missileBodies[index],
                                              round.LaunchAnchorPartFrame, round.TravelSinceLaunch,
                                              drawn.Ecl, round.LaunchAttitude,
                                              out double3 bodyPos, out doubleQuat bodyRot))
            {
                RoundBodiesWork = false;
                Announce("round bodies rejected by the engine; falling back to tracers");
                return;
            }
            flying[index] = true;

            // Hinged blades, or the single set that opens by scaling, as FinsPerRound declares.
            if (Munition.FinsPerRound > 0)
            {
                // Ecliptic to the body's frame: Asmb2Ego's conjugate, then the body's rotation off.
                double3 commandBody = Vec.Zero;
                if (!round.SteeringCommandEcl.Equals(Vec.Zero))
                {
                    double3 inAsmb = doubleQuat.Conjugate(platform.Asmb2Ego) * round.SteeringCommandEcl;
                    commandBody = doubleQuat.Conjugate(bodyRot) * inAsmb;
                }

                for (int blade = 0; blade < Munition.FinsPerRound; blade++)
                {
                    if (FinsFor(index * Munition.FinsPerRound + blade) is not { } part) continue;

                    double roll = FinMixer.FinRollRad(blade, Munition.FinsPerRound, Math.PI / 4.0);
                    double deflect = FinMixer.DeflectionRad(commandBody, roll,
                                                            Munition.MaxLateralAccel,
                                                            Munition.FinDeflectionRad);

                    LauncherPart.TryPlaceFin(part, bodyPos, bodyRot,
                                             Munition.FinHingeStation, roll, deflect);
                }
            }
            else if (FinsFor(index) is { } finSet)
            {
                LauncherPart.TryPlaceFins(finSet, bodyPos, bodyRot,
                                          round.FinDeployment(Munition), Munition);
            }

            // Smooth travel with a jumping drawn position is the frame conversion or the engine; jumping
            // travel is the simulation.
            if (trace)
            {
                IProjectile r = round;

                double tgtRange = -1.0;
                if (r.TargetRef is Vehicle tv && KsaWorld.IsAlive(tv))
                    tgtRange = Vec.Len(KsaWorld.PositionEcl(tv) - r.PositionEcl);
                else if (r.TargetRef is not null
                         && _incomingByHandle.TryGetValue(r.TargetRef, out IContact? tc) && tc.IsAlive)
                    tgtRange = Vec.Len(tc.PositionEcl - r.PositionEcl);

                // The drawn offset against PositionEcl - PlatformEcl, which should agree.
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

                // "Wrong way up" is no air, no speed, or a wrong release heading; all three printed.
                double rho = KsaWorld.MediumDensityRatioAt(platform, r.PositionEcl);
                double spd = Vec.Len(r.VelocityLocal);
                double3 drawnNose = drawn.Ecl * FireGeometry.NoseAxis;
                Log.Debug(() =>
                    $"attitude t{r.Tube}: rho {rho:F4} speed {spd:F1} m/s q {rho * spd * spd:F1} " +
                    $"(needs {BodyAttitude.NoAuthoritySpeed * BodyAttitude.NoAuthoritySpeed:F0} to " +
                    $"start, {BodyAttitude.FullAuthoritySpeed * BodyAttitude.FullAuthoritySpeed:F0} " +
                    $"for full) | drawn-vs-velocity " +
                    $"{double.RadiansToDegrees(Vec.AngleBetween(drawnNose, r.VelocityLocal)):F1} deg, drawn-vs-release " +
                    $"{double.RadiansToDegrees(Vec.AngleBetween(drawnNose, r.ReleaseHeadingEcl)):F1} deg");

                if (rho <= 0.0 && platform.Parent is Celestial medium)
                    Log.Debug(() => $"  no medium -- {KsaWorld.MediumDiagnosis(medium, r.PositionEcl)}");
            }
        }

        // Seated first, spent or not, then hidden: see TubeVisual.
        for (int i = 0; i < _missileBodies.Count && i < Profile.TubeCount; i++)
        {
            TubeVisual plan = _magazine.Plan(i, flying[i]);
            if (!Magazine.RequiresSeating(plan)) continue;

            bool seated = TubesResolved
                          && LauncherPart.TrySeatMissile(PodsPart, Profile, _missileBodies[i],
                                                         FinsFor(i), i, Munition);

            if (seated && Magazine.IsVisible(plan))
            {
                SeatFinsFor(i);
                continue;
            }

            LauncherPart.HideMissile(_missileBodies[i]);
            HideFinsFor(i);
        }
    }

    // Only the attitude: DrawShellBodies places it, in the render pass.
    private void TurnShellBodies(Vehicle platform, Part launcher)
    {
        if (_shellModel is null) return;

        foreach (IProjectile round in _rounds)
        {
            if (!RoundLabel.IsGunRound(round.Tube) || round.State != RoundState.Flying) continue;

            double3 release = Vec.IsFinite(round.ReleaseHeadingEcl) && Vec.Len2(round.ReleaseHeadingEcl) > 1e-9
                                  ? round.ReleaseHeadingEcl
                                  : Boresight;

            // A missile's rule, keyed by the round rather than a tube.
            double density = KsaWorld.MediumDensityRatioAt(platform, round.PositionEcl);
            DrawnAttitude drawn = _drawnAttitudes.TryGetValue(round, out DrawnAttitude was)
                                      ? was
                                      : new DrawnAttitude(LauncherPart.ReleaseAttitudeEcl(launcher, release,
                                                                                          round.LaunchAttitude),
                                                          0.0);
            _drawnAttitudes[round] = new DrawnAttitude(BodyAttitude.Turn(drawn.Ecl, round.VelocityLocal, density,
                                                                         round.Age - drawn.Age),
                                                       round.Age);
        }
    }

    /// <summary>
    /// Every shell for one viewport as one instanced draw, no subpart each. Off the camera's view of
    /// the craft plus <see cref="TryRoundEffectEcl"/> less its position, both read now, so the
    /// ecliptic motion cancels.
    /// </summary>
    public void DrawShellBodies(IViewport viewport, int frameIndex)
    {
        if (_shellModel is null || !_config.UseRoundBodies || Platform is not { } platform
            || !KsaWorld.IsAlive(platform) || viewport.GetCamera() is not { } camera) return;

        double3 platformEgo = camera.GetPositionEgo(platform);
        double3 platformEcl = KsaWorld.PositionEcl(platform);

        foreach (IProjectile round in _rounds)
        {
            if (!RoundLabel.IsGunRound(round.Tube) || round.State != RoundState.Flying
                || !_drawnAttitudes.TryGetValue(round, out DrawnAttitude drawn)
                || !TryRoundEffectEcl(round, out double3 ecl)) continue;

            double3 ego = platformEgo + (ecl - platformEcl);
            if (Vec.IsFinite(ego)) AddInstance(_shellModel, double3.One, drawn.Ecl, ego, viewport, frameIndex);
        }
    }

    /// <summary>Rounds past <see cref="FarBodyMetres"/>, placed as <see cref="DrawShellBodies"/> places a shell.</summary>
    public void DrawFarBodies(IViewport viewport, int frameIndex)
    {
        if (_farBodies.Count == 0 || !_config.UseRoundBodies || Platform is not { } platform
            || !KsaWorld.IsAlive(platform) || viewport.GetCamera() is not { } camera) return;

        double3 platformEgo = camera.GetPositionEgo(platform);
        double3 platformEcl = KsaWorld.PositionEcl(platform);

        foreach (IProjectile round in _rounds)
        {
            if (round.State != RoundState.Flying || !_farBodies.TryGetValue(round, out LooseBody? looks)
                || !_drawnAttitudes.TryGetValue(round, out DrawnAttitude drawn)
                || !TryRoundEffectEcl(round, out double3 ecl)) continue;

            double3 ego = platformEgo + (ecl - platformEcl);
            if (Vec.IsFinite(ego)) AddRoundInstances(looks, drawn, ego, round, viewport, frameIndex);
        }
    }

    private Track? MostUrgentThreat()
    {
        int i = ThreatModel.IndexOfMostUrgent(Radar.Tracks);
        return i >= 0 ? Radar.Tracks[i] : null;
    }

    /// <summary>False when refused, which is announced.</summary>
    public bool Fire(Track track)
    {
        if (!track.Contact.IsAlive) { Announce("refused: target gone"); return false; }
        if (!ThreatModel.MayEngage(track, _policy.Iff))
        {
            Announce($"refused: {track.Contact.DisplayName} is {track.Allegiance}");
            return false;
        }

        // The ladder's gate, refused rather than re-targeted: what to shoot at is the operator's.
        if (Munition.Guidance == GuidanceMode.AntiRadiation && !TargetIsEmitting(track))
        {
            Announce($"refused: {track.Contact.DisplayName} is not radiating");
            return false;
        }

        bool away = Commit(Aimpoint.OnVehicle(track.Contact.Handle, track.PositionEcl,
                                              track.VelocityEcl, track.Contact.MeanRadius),
                           $"{track.Contact.DisplayName} ({track.Range / 1000.0:F1} km)");

        // Counted as it leaves, or every system on the craft fires before the tally catches up.
        if (away)
        {
            CraftRounds?.Commit(track.Contact.Handle);
            track.RoundsAssigned++;
        }

        return away;
    }

    /// <summary>
    /// Lets one store go. An unguided one gets no aimpoint; a tail kit is released onto the
    /// designation, and with none it falls as a dumb bomb.
    /// </summary>
    public bool Release()
    {
        if (Munition.Powered)
        {
            Announce($"refused: the {Munition.DisplayName} is guided - give it something to shoot at");
            return false;
        }

        bool marked = Munition.Steers && Designation.Kind != AimpointKind.None;
        bool released = marked ? Commit(Designation, DesignationName) : Commit(Aimpoint.Nothing, Munition.DisplayName);
        if (!released) return false;

        Steerable = Munition.SteersItsFall && _rounds.Count > 0 ? _rounds[^1] : null;
        return true;
    }

    /// <summary>
    /// The store released last, which marks inside its reach steer until the weapon releases again.
    /// <c>WeaponSystems.ReconcileSteerables</c> clears it when another station has released since.
    /// </summary>
    public IProjectile? Steerable
    {
        get => _steerable is { State: RoundState.Flying } flying ? flying : null;
        private set => _steerable = value;
    }

    private IProjectile? _steerable;

    /// <summary>A count of rounds across every launcher, not a time.</summary>
    public long ReleasedAt { get; private set; }

    private static long _releases;

    public void LockSteerable() => _steerable = null;

    /// <inheritdoc cref="IManualFire.NextTube"/>
    public int NextTube
        => _magazine.TryPeekTube(_rounds, out int tube) ? tube : -1;

    /// <inheritdoc cref="IManualFire.TubesReadyToFire"/>
    public int TubesReadyToFire => _magazine.CountReadyToFire(_rounds);

    /// <inheritdoc cref="IManualFire.TubeAxesEcl"/>
    public int TubeAxesEcl(Span<double3> into)
    {
        if (Platform is null || Launcher is null || !Profile.LaunchAlongTube) return 0;
        if (into.Length < Profile.Tubes.Length) return 0;

        for (int tube = 0; tube < Profile.Tubes.Length; tube++)
        {
            if (!LauncherPart.TryGetTubeAxisEcl(Platform, Launcher, PodsPart, Profile, tube,
                                                out double3 axis)
                || !Vec.IsFinite(axis))
            {
                return 0;
            }

            into[tube] = Vec.Unit(axis);
        }

        return Profile.Tubes.Length;
    }

    /// <inheritdoc cref="IManualFire.CanSeparate"/>
    public bool CanSeparate => LauncherSeparation.CanSeparate(Launcher);

    /// <inheritdoc cref="IManualFire.NextStageSeparatesIt"/>
    public bool NextStageSeparatesIt => LauncherSeparation.NextStageSeparates(Platform, Launcher);

    /// <inheritdoc cref="IManualFire.StagesAfterTheRelease"/>
    public int StagesAfterTheRelease => LauncherSeparation.StagesAfterTheRelease(Platform, Launcher);

    /// <inheritdoc cref="IManualFire.MoveReleaseLast"/>
    public bool MoveReleaseLast(out string why) => LauncherSeparation.TryMoveReleaseLast(Platform, Launcher, out why);

    /// <inheritdoc cref="IManualFire.Separate"/>
    public bool Separate() => LauncherSeparation.Separate(Platform, Launcher);

    /// <inheritdoc cref="IManualFire.TryMeanReleaseStateEcl"/>
    public bool TryMeanReleaseStateEcl(out double3 positionEcl, out double3 velocityEcl,
                                       out double spinSpeed)
    {
        positionEcl = Vec.Zero;
        velocityEcl = Vec.Zero;
        spinSpeed = 0.0;

        if (Platform is null || Launcher is null || !TubesResolved || !Profile.LaunchAlongTube) return false;

        double3 platformVel = KsaWorld.VelocityEcl(Platform);
        double3 centreOfMass = KsaWorld.CentreOfMassEcl(Platform);
        double3 angular = KsaWorld.AngularVelocityEcl(Platform);

        double3 sumPos = Vec.Zero;
        double3 sumVel = Vec.Zero;
        double sumSpin = 0.0;
        int found = 0;

        for (int tube = 0; tube < Profile.Tubes.Length; tube++)
        {
            if (!LauncherPart.TryGetTubeMuzzleEcl(Platform, Launcher, PodsPart, Profile, tube,
                                                  PlatformEcl, out double3 mouth)
                || !LauncherPart.TryGetTubeAxisEcl(Platform, Launcher, PodsPart, Profile, tube,
                                                   out double3 axis)
                || !Vec.IsFinite(mouth) || !Vec.IsFinite(axis))
            {
                continue;
            }

            // The steady terms Commit builds from; the spin is reported beside them, not added.
            double3 spin = FireGeometry.SpinVelocity(angular, mouth, centreOfMass);

            sumPos += mouth;
            sumVel += platformVel + Vec.Unit(axis) * Munition.LaunchSpeed;
            sumSpin += Vec.Len(spin);
            found++;
        }

        if (found == 0) return false;

        positionEcl = sumPos / found;
        velocityEcl = sumVel / found;
        spinSpeed = sumSpin / found;
        return Vec.IsFinite(positionEcl) && Vec.IsFinite(velocityEcl);
    }

    /// <inheritdoc cref="IManualFire.TubeCount"/>
    public int TubeCount => _magazine.TubeCount;

    /// <inheritdoc cref="IManualFire.TryTubeOffsetFromMeanEcl"/>
    public bool TryTubeOffsetFromMeanEcl(int tube, out double3 offsetEcl)
    {
        offsetEcl = Vec.Zero;

        if (Platform is null || Launcher is null || !TubesResolved || !Profile.LaunchAlongTube) return false;

        return LauncherPart.TryGetTubeOffsetFromMeanEcl(Platform, Launcher, PodsPart, Profile, tube,
                                                        out offsetEcl);
    }

    /// <inheritdoc cref="IManualFire.TryTubeSpinEcl"/>
    public bool TryTubeSpinEcl(int tube, out double3 spinEcl, out double3 armEcl, out double3 angularVelocityEcl)
    {
        spinEcl = armEcl = angularVelocityEcl = Vec.Zero;

        if (Platform is null || Launcher is null || !TubesResolved) return false;

        angularVelocityEcl = KsaWorld.AngularVelocityEcl(Platform);
        return LauncherPart.TryGetTubeSpinEcl(Platform, Launcher, PodsPart, Profile, tube, angularVelocityEcl,
                                              out spinEcl, out armEcl);
    }

    /// <summary>
    /// Commits a round to a place rather than a craft: a track's gates have no meaning there, and
    /// the operator pointing at a place has said what they want.
    /// </summary>
    public bool FireAt(double3 pointEcl)
    {
        if (!Vec.IsFinite(pointEcl)) { Announce("refused: designation is not a position"); return false; }

        double range = Platform is null ? 0.0 : Vec.Len(pointEcl - PlatformEcl);

        if (TriggerArmament == ArmamentKind.Beam)
        {
            Aimpoint point = KsaWorld.TryAnchorToGround(pointEcl, out object? ground, out double3 at)
                                 ? Aimpoint.OnGround(ground!, at, pointEcl, Vec.Zero)
                                 : Aimpoint.AtPoint(pointEcl);
            Point(point, $"a designated point ({range / 1000.0:F1} km)", log: true);
            return PressBeam();
        }

        // A gun shoots where it points, so the reach gate below does not apply: it would refuse the
        // shot rather than let it fall short. Said, because the belt does not come back.
        if (TriggerArmament == ArmamentKind.Belt)
        {
            if (Profile.HasCannon && range > Shell.MaxRange)
            {
                Announce($"firing short: {range / 1000.0:F1} km is past the shell's "
                         + $"{Shell.MaxRange / 1000.0:F1} km");
            }

            return FireBurst();
        }

        // Or a designation past the horizon spends a round on somewhere it can never arrive.
        if (range > Munition.MaxRange)
        {
            Announce($"refused: {range / 1000.0:F1} km is beyond the round's {Munition.MaxRange / 1000.0:F0} km reach");
            return false;
        }

        Aimpoint aim = KsaWorld.TryAnchorToGround(pointEcl, out object? body, out double3 anchor)
                           ? Aimpoint.OnGround(body!, anchor, pointEcl, Vec.Zero)
                           : Aimpoint.AtPoint(pointEcl);

        return Commit(aim, $"a designated point ({range / 1000.0:F1} km)");
    }

    // The launcher's own gates, a tube, the launch geometry and the round.
    private bool Commit(Aimpoint aim, string what)
    {
        if (TriggerArmament == ArmamentKind.Beam) return PressBeam();

        // Every gate below is about a magazine a tubeless launcher does not own.
        if (Profile.TubeCount == 0) return FireBurst();

        if (Platform is null) { Announce("refused: no platform"); return false; }
        if (!IsOperational) { Announce("refused: no launcher part fitted"); return false; }

        // Named: another launcher on the craft may be loaded.
        if (Ammo <= 0)
        {
            Announce($"refused: {Profile.DisplayName} ({LauncherOrdinal + 1}) is empty");
            return false;
        }
        if (!IsLaid) { Announce("refused: launcher still slewing"); return false; }

        // Nothing between here and AddRound can fail without returning the tube.
        if (!_magazine.TryTakeTube(_rounds, out int tube))
        {
            Announce(Profile.TubeCount == 0
                         ? $"refused: {Profile.DisplayName} carries no missiles"
                         : "refused: no free tube");
            return false;
        }

        Launch launch = LaunchFrom(tube, aim);

        // A seeker released outside its gimbal never recovers, so refused here and the tube returned.
        // Waived for a ground designation on a launcher that cannot train, which has no way to bring
        // it inside; a trained one has no such excuse.
        double3 toAim = aim.PositionEcl - launch.Position;
        if (!FireGate.CanGuideOntoAimpoint(Munition.Guidance,
                                           aim.Kind == AimpointKind.Ground && !Profile.Trains,
                                           Munition.SeekerFovRad, launch.Direction, toAim))
        {
            _magazine.Return(tube);
            double offDeg = double.RadiansToDegrees(Vec.AngleBetween(toAim, launch.Direction));
            Announce($"refused: {what} is {offDeg:F0} deg off the tube, "
                     + $"past the seeker's {Munition.SeekerFovDeg:F0} deg - point the launcher at it");
            return false;
        }

        // The only place a munition's implementation is chosen: unpowered rounds, a tail kit
        // included, are slugs, flown as the bomb sight flies them. An ejector's push is laid along
        // its stroke; a sight reading it at once lands a store 0.1 m further from its rack.
        double strokeSeconds = Ejector.StrokeSeconds(Profile.EjectSpeed, Profile.EjectorStrokeMetres);
        bool stroked = strokeSeconds > 0.0 && !launch.Eject.Equals(Vec.Zero);
        double3 leaving = stroked ? launch.Velocity - launch.Eject : launch.Velocity;
        double3 ejectAccel = stroked
            ? Vec.Unit(launch.Eject) * Ejector.Acceleration(Profile.EjectSpeed, Profile.EjectorStrokeMetres)
            : Vec.Zero;

        AddRound(!Munition.Powered
            ? new Slug(launch.Position, leaving, aim.Handle, tube + 1, PlatformEcl, launch.FrameVelocity)
            {
                Munition = Munition,
                LaunchAnchorPartFrame = launch.AnchorPartFrame,
                ReleaseHeadingEcl = launch.Heading,
                LaunchAttitude = Platform?.Asmb2Ego ?? doubleQuat.Identity,
                SpinVelocityEcl = launch.Spin,
                LaunchFrameVelocityLocal = launch.PlatformVelocity + launch.Eject - launch.FrameVelocity,
                MotorDelaySeconds = Profile.MotorDelaySeconds,
                EjectAccelerationEcl = ejectAccel,
                EjectSeconds = stroked ? strokeSeconds : 0.0,
                Aimpoint = aim,
            }
            : new Interceptor(launch.Position, leaving, aim.Handle, tube + 1, PlatformEcl,
                              launch.FrameVelocity)
            {
                Munition = Munition,
                LaunchAnchorPartFrame = launch.AnchorPartFrame,
                ReleaseHeadingEcl = launch.Heading,
                LaunchAttitude = Platform?.Asmb2Ego ?? doubleQuat.Identity,

                // Inherited rather than earned, so the motor pushes along the round. Differenced here
                // from this frame's samples, so the ~29.8 km/s cancels.
                LaunchFrameVelocityLocal = launch.PlatformVelocity + launch.Eject - launch.FrameVelocity,
                MotorDelaySeconds = Profile.MotorDelaySeconds,
                EjectAccelerationEcl = ejectAccel,
                EjectSeconds = stroked ? strokeSeconds : 0.0,
                Aimpoint = aim,
            });
        _salvoTimer = Profile.SalvoSpacing;
        _lastReleaseClock = _clock;

        Announce(aim.Kind == AimpointKind.None
                     ? $"round {tube + 1} released - {what}"
                     : $"round {tube + 1} away at {what}"
                       + Ejection(launch.Direction, launch.PlatformVelocity, launch.Position, launch.Spin,
                                  launch.Eject));
        return true;
    }

    /// <inheritdoc cref="IWeaponSystemView.TryNextReleaseEcl"/>
    public bool TryNextReleaseEcl(out double3 positionEcl, out double3 velocityEcl)
    {
        positionEcl = Vec.Zero;
        velocityEcl = Vec.Zero;

        if (Platform is null || Launcher is null || !TubesResolved) return false;

        // No tube to release from is no release to predict. Falling back to tube 0 answers about a
        // store that is not there, which is a pipper still drawn on the ground after the rack's
        // last one has gone -- and, once a store in the air has a region of its own, two rings
        // saying different things about the same weapon.
        int tube = NextTube;
        if (tube < 0) return false;

        Launch launch = LaunchFrom(tube, Aimpoint.Nothing);
        positionEcl = launch.Position;
        velocityEcl = launch.Velocity;

        return launch.FromTube && Vec.IsFinite(positionEcl) && Vec.IsFinite(velocityEcl);
    }

    // One expression for the release and anything predicting it: a sight solved from a tidier launch
    // put its ring 94 m off, flown, off a climbing rack whose ejector throws 50 degrees off axis.
    private readonly record struct Launch(bool FromTube, double3 Position, double3 AnchorPartFrame,
                                          double3 Direction, double3 Heading, double3 Spin,
                                          double3 Velocity, double3 PlatformVelocity,
                                          double3 FrameVelocity, double3 Eject);

    private Launch LaunchFrom(int tube, Aimpoint aim)
    {
        Vehicle platform = Platform!;
        double3 platformVel = KsaWorld.VelocityEcl(platform);
        double3 frameVel = KsaWorld.GroundVelocityAt(platform, PlatformEcl);

        // From the tube as the pods are aimed; the boresight ring below ignores traverse and elevation.
        double3 launchAnchorPartFrame = Vec.Zero;
        double3 tubeMouth = Vec.Zero;
        bool fromTube = Launcher is not null && TubesResolved
                        && LauncherPart.TryGetTubeMuzzleEcl(platform, Launcher, PodsPart, Profile, tube,
                                                            PlatformEcl, out tubeMouth)
                        // Seated, not at the mouth: the mesh is modelled about its centre.
                        && LauncherPart.TryGetSeatedPartFrame(PodsPart, Profile, tube,
                                                              Munition.BodyLength,
                                                              out launchAnchorPartFrame);

        double3 launchPos = fromTube
            ? tubeMouth
            : Launcher is not null
                ? LauncherPart.MuzzleEcl(Profile, MountEcl, Boresight, tube)
                : MountEcl + Boresight * Profile.MuzzleOffset;

        // Along the tube; the fallback slews to the target with loft, which a laid launcher must not.
        double3 tubeAxis = Vec.Zero;
        bool alongTube = fromTube && Profile.LaunchAlongTube
                         && LauncherPart.TryGetTubeAxisEcl(platform, Launcher!, PodsPart, Profile, tube, out tubeAxis);

        // An empty aimpoint's position is the ecliptic origin, the Sun.
        double3 aimForGeometry = aim.Kind == AimpointKind.None
                                     ? launchPos + Boresight
                                     : aim.PositionEcl;

        double3 launchDir = FireGeometry.LaunchDirection(
            alongTube, tubeAxis, launchPos, aimForGeometry, Boresight, Profile.LaunchLoft,
            Profile.EjectAwayFromMount);

        // Which way it points, not which way it is pushed: EjectAwayFromMount biases launchDir 50
        // degrees off the tube. In vacuum nothing turns it back.
        double3 releaseHeading = alongTube && !Vec.Unit(tubeAxis).Equals(Vec.Zero)
                                     ? Vec.Unit(tubeAxis)
                                     : launchDir;

        // A turning platform's tube is already moving; from the centre of mass it pivots about.
        double3 spinVel = FireGeometry.SpinVelocity(KsaWorld.AngularVelocityEcl(platform), launchPos,
                                                    KsaWorld.CentreOfMassEcl(platform));

        // Out of the mounting face; the direction stays the tube's, which the gimbal is measured off.
        double3 eject = Vec.Zero;
        if (Profile.EjectSpeed > 0f && Launcher is { } launcher
            && LauncherPart.TryLauncherDirectionEcl(platform, launcher, TubeGeometry.TraverseAxis, out double3 mountNormal))
        {
            eject = FireGeometry.Ejection(mountNormal, Profile.EjectSpeed);
        }
        double slide = eject.Equals(Vec.Zero) ? Munition.LaunchSpeed : 0.0;

        return new Launch(fromTube, launchPos, launchAnchorPartFrame, launchDir, releaseHeading, spinVel,
                          platformVel + spinVel + eject + (launchDir * slide), platformVel, frameVel, eject);
    }

    /// <summary>
    /// For the designation ring, before a round is spent. From tube zero and the mount, metres off
    /// the tube used against kilometres of range; true with nothing to measure against.
    /// </summary>
    public bool CanGuideOnto(double3 pointEcl)
    {
        // A shell is not guided at all, and the trigger on the cannon never asks.
        if (TriggerArmament == ArmamentKind.Belt) return true;

        return SeekerCanTake(Munition, pointEcl, operatorHeld: !Profile.Trains, out _);
    }

    // One rule for the ring, the ladder and the trigger. The angle is NaN when true for want of a tube.
    private bool SeekerCanTake(MunitionProfile munition, double3 pointEcl, bool operatorHeld, out double offTubeDeg)
    {
        offTubeDeg = double.NaN;
        if (Platform is null || Launcher is null) return true;
        if (!LauncherPart.TryGetTubeAxisEcl(Platform, Launcher, PodsPart, Profile, 0, out double3 axis))
        {
            return true;
        }

        double3 toPoint = pointEcl - MountEcl;
        offTubeDeg = double.RadiansToDegrees(Vec.AngleBetween(toPoint, axis));
        return FireGate.CanGuideOntoAimpoint(munition.Guidance, operatorHeld, munition.SeekerFovRad, axis, toPoint);
    }

    /// <summary>
    /// Along wherever the mount is laid, solving no lead: over the sky the operator is the solution,
    /// and over the ground the lay already is. Every refusal is announced.
    /// <paramref name="rounds"/> above zero fires that many rather than the profile's burst.
    /// </summary>
    public bool FireBurst(int rounds = 0)
    {
        if (!Profile.HasCannon) { Announce("refused: no cannon fitted"); return false; }
        if (Platform is null) { Announce("refused: no platform"); return false; }
        if (!IsOperational) { Announce("refused: no launcher part fitted"); return false; }
        if (!_policy.GunsEnabled) { Announce("refused: cannon switched off"); return false; }
        if (_guns.IsEmpty) { Announce("refused: belt empty"); return false; }
        if (!GunsAreLaid) { Announce("refused: cannon still laying"); return false; }

        _guns.NextBurstRounds = Math.Max(rounds, 0);
        _manualTrigger = true;
        return true;
    }

    /// <summary>Of the armament the trigger fires: a gun reads zero from the magazine forever.</summary>
    public bool ReadyToFire => TriggerArmament switch
    {
        ArmamentKind.Tubes => Ammo > 0 && IsLaid,
        ArmamentKind.Beam => TriggerHold is not { BindsTrigger: true },
        _ => Profile.HasCannon && !_guns.IsEmpty && GunsAreLaid,
    };

    /// <summary>
    /// One point per barrel cluster (<see cref="TubeGeometry.ClusterMuzzles"/>): a sponson either side
    /// averages to the centreline, where nothing is firing.
    /// </summary>
    public int GunFlashPointsEcl(Span<double3> into)
    {
        if (!Profile.HasCannon || Platform is null || Launcher is null) return 0;
        if (GunsPart is not { } guns) return 0;
        if (into.IsEmpty) return 0;

        int barrels = Profile.GunMuzzles.Length;
        if (barrels <= 0) return 0;

        Span<int> group = stackalloc int[barrels];
        int groups = TubeGeometry.ClusterMuzzles(Profile.GunMuzzles,
                                                 TubeGeometry.GunFlashClusterMetres, group);
        if (groups <= 0) return 0;

        Span<double3> sums = stackalloc double3[Math.Min(groups, into.Length)];
        Span<int> counts = stackalloc int[sums.Length];

        for (int i = 0; i < barrels; i++)
        {
            int g = group[i];
            if (g < 0 || g >= sums.Length) continue;

            if (!LauncherPart.TryGetGunMuzzleEcl(Platform, Launcher, guns, Profile, i, PlatformEcl,
                                                 out double3 muzzle, out _))
            {
                continue;
            }

            sums[g] += muzzle;
            counts[g]++;
        }

        // Resolved clusters only, packed to the front.
        int found = 0;
        for (int g = 0; g < sums.Length; g++)
        {
            if (counts[g] == 0) continue;

            double3 at = sums[g] / counts[g];
            if (!Vec.IsFinite(at)) continue;

            into[found++] = at;
        }

        return found;
    }

    public bool HasGunFlash()
    {
        Span<double3> one = stackalloc double3[1];
        return GunFlashPointsEcl(one) > 0;
    }

    /// <summary>
    /// Tubes and a belt are two weapons to the trigger. A launcher without one answers the other
    /// whatever was asked.
    /// </summary>
    public ArmamentKind TriggerArmament
    {
        get => WeaponFit.TriggerOn(Profile, _triggerArmament);
        set
        {
            if (value == _triggerArmament) return;

            _triggerArmament = value;
            Log.Info($"{Profile.DisplayName} ({LauncherOrdinal + 1}) trigger on the "
                     + TriggerArmament switch
                     {
                         ArmamentKind.Belt => "cannon",
                         ArmamentKind.Beam => "beam",
                         _ => "tubes",
                     });
        }
    }

    private ArmamentKind _triggerArmament = ArmamentKind.Tubes;

    public MunitionProfile TriggerMunition => TriggerArmament switch
    {
        ArmamentKind.Belt => Shell,
        ArmamentKind.Beam => MunitionProfile.None,
        _ => Munition,
    };

    // A designated craft ahead of the lock, as the turret follows it. Its track, so the IFF is the
    // set's; null while the set cannot see it.
    private Track? TriggerTarget => Designation.Kind == AimpointKind.Vehicle
                                        ? Radar.TrackFor(Designation.Handle)
                                        : Radar.Locked;

    /// <summary>
    /// A burst along the guns, the beam, a store released, or a round at the designated craft or else
    /// the lock.
    /// </summary>
    public bool FireAtLock()
    {
        // A gun is aimed, not locked on.
        if (TriggerArmament == ArmamentKind.Belt) return FireBurst();
        if (TriggerArmament == ArmamentKind.Beam) return PressBeam();

        // A store is released with no lock. Powered, not guidance: a tail kit is still released.
        if (!Munition.Powered) return Release();

        if (TriggerTarget is { } target) return Fire(target);

        Announce(Designation.Kind == AimpointKind.Vehicle
                     ? $"refused: {DesignationName} is not on the radar"
                     : "refused: no lock - shift-click a craft to lock it");
        return false;
    }

    public void Reload()
    {
        if (Profile.FiresBeam && _beamHead is { } head)
        {
            head.RefillChemicals();
            Announce($"laser chemicals refilled - {head.ChemicalSecondsLeft:F0} s");
            if (Profile.TubeCount == 0 && !Profile.HasCannon) return;
        }

        _magazine.Resize(Profile.TubeCount, Profile.MagazineDepth);
        _reloadTimer = 0.0;

        // The belt too: a mount that reloads by hand has GunReloadSeconds zero, so no resupply.
        if (Profile.HasCannon)
        {
            _guns.Fill(Profile.GunAmmo);
            _guns.Reset();
            _gunReloadTimer = 0.0;
        }

        // A tube whose round is still flying keeps its body, so nothing reappears: said.
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

    /// <summary>
    /// Removes rounds in flight without detonating, and turns auto-engage off, or it fires again on
    /// the next lock.
    /// </summary>
    public void SafeAll()
    {
        int n = _rounds.Count;
        ClearRounds();
        _beam.Stop();

        bool wasEngaging = _policy.AutoEngage;
        _policy.AutoEngage = false;

        if (n > 0 || wasEngaging)
        {
            Announce($"safe - {n} round(s) removed{(wasEngaging ? ", auto-engage off" : "")}");
        }
    }

    // The only ways in and out, so _roundSet cannot drift from _rounds.
    private void AddRound(IProjectile round)
    {
        ReleasedAt = ++_releases;
        _rounds.Add(round);
        _roundSet.Add(round);
    }

    private void DropRound(int index)
    {
        _roundSet.Remove(_rounds[index]);
        _drawnAttitudes.Remove(_rounds[index]);
        _inVacuum.Remove(_rounds[index]);
        _seekers.Remove(_rounds[index]);
        _looseBodies.Remove(_rounds[index]);
        _farBodies.Remove(_rounds[index]);
        _rounds.RemoveAt(index);
    }

    private void ClearRounds()
    {
        _rounds.Clear();
        _roundSet.Clear();
        _drawnAttitudes.Clear();
        _inVacuum.Clear();
        _seekers.Clear();
        _looseBodies.Clear();
        _farBodies.Clear();
    }

    // Every offset at once: OffsetFromPlatform alone sorts itself out next step, which makes a
    // partial re-anchor look right. See IProjectile.Reanchor.
    private void ReanchorRounds(double3 toEcl)
    {
        if (!Vec.IsFinite(toEcl)) return;

        double3 shift = PlatformEcl - toEcl;
        for (int i = 0; i < _rounds.Count; i++) _rounds[i].Reanchor(shift);
    }

    // The part it holds, wherever it now sits on this craft; its ordinal only until it holds one.
    private (Part Part, LauncherProfile Profile)? ResolveLauncher(Vehicle platform)
    {
        if (HeldPart is not { } held)
        {
            (Part Part, LauncherProfile Profile)? found = LauncherPart.FindNth(platform, LauncherOrdinal, _launcherScratch);
            if (found is { } first) HeldPart = first.Part;
            return found;
        }

        LauncherPart.FindAll(platform, _launcherScratch);
        for (int i = 0; i < _launcherScratch.Count; i++)
        {
            if (!ReferenceEquals(_launcherScratch[i].Item1, held)) continue;

            LauncherOrdinal = i;
            return _launcherScratch[i];
        }

        return null;
    }

    /// <summary>
    /// Follows the launcher onto the craft a decoupler carried it to, as this same object, so the
    /// magazine, rounds, settings and teams come along. Not <see cref="GoLoose"/>, which is for one that died.
    /// </summary>
    /// <param name="held">The exact part it was followed onto, or null to take whatever sits at the ordinal.</param>
    public void Rehome(Vehicle craft, int ordinal, Part? held = null)
    {
        if (!KsaWorld.IsAlive(craft)) return;

        double3 toEcl = KsaWorld.PositionEcl(craft);
        if (!Vec.IsFinite(toEcl)) return;

        int aboard = Ammo;
        int flying = _rounds.Count;
        double moved = Vec.Len(toEcl - PlatformEcl);

        ReanchorRounds(toEcl);

        PlatformEcl = toEcl;
        PlatformStepEcl = Vec.Zero;
        LauncherOrdinal = ordinal;
        HeldPart = held;

        // Found again in the new part tree rather than written to the old craft's.
        _loggedSubParts = false;
        _missileBodies.Clear();
        _finBodies.Clear();

        // Or a latch from the old stack keeps IsLaid false for good.
        _drives.Clear();
        RoundBodiesWork = true;

        PinPlatform(craft);
        _lastPlatform = craft;

        Announce($"launcher decoupled onto {KsaWorld.DisplayName(craft)} as launcher {ordinal + 1}, "
                 + $"{moved:F0} m away - {aboard} round(s) aboard, {flying} in flight");
    }

    // The body under the platform, sampled at the same instant as the platform itself.
    private Celestial? _sampledBody;
    private double3 _bodyAtSampleEcl;

    /// <summary>Hands the rounds to the body they fly over, the craft that fired them destroyed.</summary>
    /// <returns>False with nothing in the air to keep.</returns>
    public bool GoLoose(Celestial? body, string firedBy)
    {
        if (_rounds.Count == 0 || body is null) return false;

        // At the platform's last sample, the instant the offsets are measured at: a craft that died
        // in the engine's step was sampled a frame ago, a frame of ~30 km/s.
        double3 bodyEcl = ReferenceEquals(body, _sampledBody) && Vec.IsFinite(_bodyAtSampleEcl)
                              ? _bodyAtSampleEcl
                              : KsaWorld.PositionEcl(body);
        if (!Vec.IsFinite(bodyEcl)) return false;

        ReanchorRounds(bodyEcl);
        CaptureLooseBodies();

        _looseBody = body;
        _looseName = firedBy;

        Platform = null;
        _lastPlatform = null;
        Launcher = null;
        Radar.Reset();
        Hold = "launcher destroyed";

        PlatformEcl = bodyEcl;
        PlatformStepEcl = Vec.Zero;

        Announce($"{firedBy} destroyed - {_rounds.Count} round(s) still in the air");
        return true;
    }

    /// <summary>
    /// Its rounds and nothing else: no fire control, whose own-round filter would walk every round
    /// in the world against every round it has up.
    /// </summary>
    public void UpdateLoose(double dt, IReadOnlyList<IContact>? airborne = null)
    {
        if (_looseBody is not { } body || _rounds.Count == 0) return;

        _clock += dt;

        // Paired with the rounds as a platform's was. See docs/FRAMES-AND-EPOCHS.md.
        double3 sampled = KsaWorld.PositionEcl(body);
        if (Vec.IsFinite(sampled))
        {
            PlatformStepEcl = sampled - PlatformEcl;
            PlatformEcl = sampled;
        }

        FillIncoming(airborne);
        UpdateRounds(dt);
        TurnLooseBodies(body);
    }

    // Before the parts are released: only the subpart says which template a round's body is.
    private void CaptureLooseBodies()
    {
        if (!RoundBodiesWork || !_config.UseRoundBodies) return;

        foreach (IProjectile round in _rounds)
        {
            int index = round.Tube - 1;
            if (index < 0 || index >= _missileBodies.Count || !_drawnAttitudes.ContainsKey(round)) continue;

            _looseBodies[round] = BodiesOf(round, index);
        }

        foreach (IProjectile round in _rounds)
        {
            if (_shellModel is not null && RoundLabel.IsGunRound(round.Tube) && _drawnAttitudes.ContainsKey(round))
            {
                _looseBodies[round] = new LooseBody(_shellModel, [], null);
            }
        }

        // A launcher shed as debris would carry a second copy of every round.
        for (int i = 0; i < _missileBodies.Count; i++) LauncherPart.HideMissile(_missileBodies[i]);
        for (int i = 0; i < _finBodies.Count; i++) LauncherPart.HideMissile(_finBodies[i]);
    }

    private LooseBody BodiesOf(IProjectile round, int index)
    {
        var blades = new PartModel?[Math.Max(0, round.Munition.FinsPerRound)];
        for (int b = 0; b < blades.Length; b++) blades[b] = LauncherPart.ModelOf(FinsFor(index * blades.Length + b));

        return new LooseBody(LauncherPart.ModelOf(_missileBodies[index]), blades,
                             blades.Length == 0 ? LauncherPart.ModelOf(FinsFor(index)) : null);
    }

    private void TurnLooseBodies(Celestial body)
    {
        foreach (IProjectile round in _rounds)
        {
            if (!_looseBodies.ContainsKey(round) || !_drawnAttitudes.TryGetValue(round, out DrawnAttitude was)) continue;

            double density = KsaWorld.MediumDensityRatioAt(body, round.PositionEcl);
            _drawnAttitudes[round] = Carry(was, round, density);
        }
    }

    // Onto the airflow and rolled by its spin motors; the turn adds no roll, so the two compose.
    private DrawnAttitude Carry(DrawnAttitude was, IProjectile round, double density)
    {
        // The nose the air first finds: docs/RV-POINTING.md.
        if (BodyAttitude.Authority(round.VelocityLocal, density) <= 0.0)
        {
            _inVacuum.Add(round);
        }
        else if (_inVacuum.Remove(round))
        {
            double off = double.RadiansToDegrees(Vec.AngleBetween(was.Ecl * FireGeometry.NoseAxis, round.VelocityLocal));
            Log.Info($"{RoundLabel.For(round.Tube)} meets the air {round.Age:F0} s after release, nose {off:F1} deg off its airflow");
        }

        doubleQuat turned = BodyAttitude.StillBesideLauncher(round.TravelSinceLaunch, round.Munition.BodyLength)
                                ? was.Ecl
                                : BodyAttitude.Turn(was.Ecl, round.VelocityLocal, density, round.Age - was.Age);
        if (Profile.ReleaseSpin is { } spin)
        {
            turned = BodyAttitude.Roll(turned, spin.RollRadAt(round.Age) - spin.RollRadAt(was.Age));
        }
        return new DrawnAttitude(turned, round.Age);
    }

    /// <inheritdoc cref="IEffectSource.TryRoundAttitudeEcl"/>
    public bool TryRoundAttitudeEcl(IProjectile round, out doubleQuat attitudeEcl)
    {
        bool known = _drawnAttitudes.TryGetValue(round, out DrawnAttitude drawn);
        attitudeEcl = known ? drawn.Ecl : doubleQuat.Identity;
        return known;
    }

    /// <summary>
    /// Off the camera's view of the body plus the round's offset, the plume's pairing, so the planet's
    /// motion cancels.
    /// </summary>
    public void DrawLooseBodies(IViewport viewport, int frameIndex)
    {
        if (_looseBody is not { } body || _looseBodies.Count == 0 || viewport.GetCamera() is not { } camera) return;

        double3 bodyEgo = camera.GetPositionEgo(body);
        int drawnCount = 0;
        double nearest = double.PositiveInfinity;

        foreach (IProjectile round in _rounds)
        {
            if (round.State != RoundState.Flying
                || !_looseBodies.TryGetValue(round, out LooseBody? looks)
                || !_drawnAttitudes.TryGetValue(round, out DrawnAttitude drawn)) continue;

            double3 ego = bodyEgo + round.OffsetFromPlatform;
            if (!Vec.IsFinite(ego)) continue;

            AddRoundInstances(looks, drawn, ego, round, viewport, frameIndex);
            drawnCount += looks.Body is null ? 0 : 1;
            nearest = Math.Min(nearest, Vec.Len(ego));
        }

        if (_looseBodiesReported || drawnCount == 0) return;
        _looseBodiesReported = true;
        Log.Info($"{_looseName}: drawing {drawnCount} round bodies with no launcher, nearest {nearest:F0} m from the camera");
    }

    private bool _looseBodiesReported;

    private static void AddRoundInstances(LooseBody looks, DrawnAttitude drawn, double3 ego, IProjectile round,
                                          IViewport viewport, int frameIndex)
    {
        MunitionProfile munition = round.Munition;
        AddInstance(looks.Body, double3.One, drawn.Ecl, ego, viewport, frameIndex);

        for (int b = 0; b < looks.Blades.Length; b++)
        {
            double roll = FinMixer.FinRollRad(b, looks.Blades.Length, Math.PI / 4.0);
            doubleQuat rotation = drawn.Ecl * doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), roll);
            AddInstance(looks.Blades[b], double3.One, rotation,
                        ego + (drawn.Ecl * new double3(munition.FinHingeStation, 0, 0)), viewport, frameIndex);
        }

        AddInstance(looks.FinSet, TubeGeometry.FinScale(munition, round.FinDeployment(munition)),
                    drawn.Ecl, ego, viewport, frameIndex);
    }

    private static void AddInstance(PartModel? model, double3 scale, doubleQuat rotation, double3 ego,
                                    IViewport viewport, int frameIndex)
    {
        if (model is null) return;

        double4x4 matrix = double4x4.CreateScale(scale) * double4x4.CreateFromQuaternion(rotation)
                           * double4x4.CreateTranslation(ego);
        model.AddInstance(new PartModel.PerInstanceData { ModelMatrix = float4x4.Pack(in matrix) },
                          viewport, frameIndex);
    }

    // Shared with UpdateLoose, so a loose system splashes and is splashed as a crewed one.
    private void FillIncoming(IReadOnlyList<IContact>? airborne)
    {
        _incoming.Clear();
        _incomingByHandle.Clear();
        if (airborne is null) return;

        for (int i = 0; i < airborne.Count; i++)
        {
            // Never its own salvo: with no team every round is Unknown, which is engageable.
            if (airborne[i].Handle is IProjectile r && _roundSet.Contains(r)) continue;

            _incoming.Add(airborne[i]);
            _incomingByHandle[airborne[i].Handle] = airborne[i];
        }
    }

    // Which way a round is thrown against the platform's track: on a deorbit a metre a second is
    // kilometres, so it is stated rather than inferred.
    private string Ejection(double3 launchDirEcl, double3 platformVelEcl, double3 launchPosEcl,
                           double3 spinVelEcl, double3 ejectEcl)
    {
        bool ejected = !ejectEcl.Equals(Vec.Zero);
        if (Munition.LaunchSpeed <= 0f && !ejected) return "";

        double3 heading = Vec.Unit(platformVelEcl);
        double3 thrown = Vec.Unit(launchDirEcl);
        if (heading.Equals(Vec.Zero) || thrown.Equals(Vec.Zero)) return "";

        double degrees = Vec.AngleBetween(thrown, heading) * 180.0 / Math.PI;

        // Radial is the sensitive axis. Metres is the tube's offset; much more is the analytic and
        // physics positions disagreeing.
        if (Platform?.Parent is not Celestial parent) return "";

        double3 up = Vec.Unit(launchPosEcl - KsaWorld.PositionEcl(parent));
        double radial = Vec.Dot(launchPosEcl - PlatformEcl, up);

        // The spin is invisible from the craft's orbit state: the tube's lever arm sweeping.
        string push = ejected
            ? $"ejected {Vec.Len(ejectEcl):F1} m/s off the mount"
              + (Munition.TotalBoostSeconds > 0f ? $", motor at {Profile.MotorDelaySeconds:F2} s" : "")
            : $"{Munition.LaunchSpeed:F1} m/s off the tube";
        return $" ({push}, {degrees:F0} deg from the platform's"
               + $" track, launched {radial:+0.0;-0.0;0.0} m radially off the orbit position,"
               + $" {Vec.Len(spinVelEcl):F3} m/s of spin at the tube)";
    }

    private double3 GroundVelocityAtRound(double3 positionEcl)
        => _looseBody is { } body
               ? KsaWorld.GroundVelocityAt(body, positionEcl)
               : KsaWorld.GroundVelocityAt(Platform!, positionEcl);

    // With the body's own fall toward its primary, or the round is left behind by half of it times
    // the coast squared: 431 m over six minutes. docs/MIRV-NEXT.md item 2.
    private double3 GravityAtRound(double3 positionEcl, double simStep)
    {
        Celestial? body = _looseBody ?? (Platform is null ? null : KsaWorld.ParentBody(Platform));

        if (body is null) return KsaWorld.GravityAt(Platform!, positionEcl);

        // At the body half-way through the frame: the sample is at the frame's end, a frame of the
        // body's travel (513 m at 30 km/s and 17 ms) from the pre-step round. docs/MIRV-NEXT.md item 2.
        return KsaWorld.PullOnRound(body, positionEcl, _bodyVelocityEcl, -0.5 * simStep);
    }

    private Func<double3, double, double>? _airDensityAt;
    private double3 _bodyVelocityEcl;

    private Func<double3, double3, Approach>? _approachAt;

    // RoundReach's maths, body-centred inertial: the body's velocity comes off and its spin does not.
    // Both terms are this frame's samples, so the ~29.8 km/s cancels.
    private Approach ApproachAt(double3 positionEcl, double3 velocityEcl)
    {
        Celestial? body = _looseBody ?? (Platform is null ? null : KsaWorld.ParentBody(Platform));
        if (body is null) return Approach.Unknown;

        double ceiling = KsaWorld.ArrivalCeilingRadius(body);
        if (!(ceiling > 0.0)) return Approach.Unknown;

        // _bodyVelocityEcl, not a fresh read: the instant the round was stepped against.
        return RoundReach.Classify(KsaWorld.BodyMu(body),
                                         positionEcl - KsaWorld.PositionEcl(body),
                                         velocityEcl - _bodyVelocityEcl,
                                         ceiling, KsaWorld.LowestGroundRadius(body));
    }

    // KSA samples celestials once a frame, so a round crossing the frame against a frozen body reads
    // an altitude ramping by kilometres. The body's travel is put back per sub-step.
    private double AirDensityIntoFrame(double3 positionEcl, double secondsIntoFrame)
        => MediumAtRound(positionEcl - (_bodyVelocityEcl * secondsIntoFrame));

    // Composed as GravityAtRound, so the body's fall travels with it, but per sub-step: the centre
    // moves within the frame.
    private double3 GravityIntoFrame(double3 positionEcl, double secondsIntoFrame)
    {
        Celestial? body = _looseBody ?? (Platform is null ? null : KsaWorld.ParentBody(Platform));

        if (body is null) return KsaWorld.GravityAt(Platform!, positionEcl);

        return KsaWorld.PullOnRound(body, positionEcl, _bodyVelocityEcl, secondsIntoFrame);
    }

    private Func<double3, double, double3>? _gravityAt;

    // secondsIntoFrame is negative: this walks the centre back to the sub-step's own instant.
    private double3 GroundCentreDriftIntoFrame(double secondsIntoFrame)
        => _bodyVelocityEcl * secondsIntoFrame;

    private Func<double, double3>? _groundDriftAt;

    // A terrain query also needs the spin off: the engine answers through the frame's end rotation,
    // ~400 m/s at mid latitudes. Read at the point, since the spin is a cross product with its radius.
    private double3 GroundQueryDriftIntoFrame(double3 positionEcl, double secondsIntoFrame)
        => GroundVelocityIntoFrame(positionEcl, secondsIntoFrame) * secondsIntoFrame;

    // Where the round is: a re-entering round crosses ~150 m of ground a frame. ImpactPredictor
    // recomputes this at every RK stage.
    private double3 AirVelocityIntoFrame(double3 positionEcl, double secondsIntoFrame)
        => GroundVelocityIntoFrame(positionEcl, secondsIntoFrame);

    // Back-dated as AirDensityIntoFrame: undated, the radius carries a frame of the body's travel,
    // 0.055 m/s of wind always one way at 25 ms (docs/ACCURACY-PLAN.md 3el).
    private double3 GroundVelocityIntoFrame(double3 positionEcl, double secondsIntoFrame)
        => GroundVelocityAtRound(positionEcl - (_bodyVelocityEcl * secondsIntoFrame));

    private Func<double3, double, double3>? _airVelocityAt;

    private Func<double3, double, double3>? _groundQueryDriftAt;

    private double3 BodyVelocityEcl()
    {
        try
        {
            Celestial? body = _looseBody ?? (Platform?.Parent as Celestial);
            double3 v = body?.GetVelocityEcl() ?? Vec.Zero;
            return Vec.IsFinite(v) ? v : Vec.Zero;
        }
        catch
        {
            return Vec.Zero;
        }
    }

    private double MediumAtRound(double3 positionEcl)
        => _looseBody is { } body
               ? KsaWorld.MediumDensityRatioAt(body, positionEcl)
               : KsaWorld.MediumDensityRatioAt(Platform!, positionEcl);

    private void UpdateRounds(double dt)
    {
        if (_rounds.Count == 0) return;

        // The candidate list is built at most once for a burst of dozens of shells.
        _contactsFresh = false;
        _bodyVelocityEcl = BodyVelocityEcl();

        for (int i = _rounds.Count - 1; i >= 0; i--)
        {
            IProjectile round = _rounds[i];

            // Shot down inside another system's update, so collected here a frame later.
            if (round.State == RoundState.ShotDown)
            {
                Announce($"{RoundLabel.For(round.Tube)} was shot down after {round.Age:F1}s, " +
                         $"{round.DistanceFlown / 1000.0:F1} km out");

                // Here, on the one frame it is reaped: BallisticScenario waits for every warhead to end.
                RoundEnded?.Invoke(round);
                DropRound(i);
                continue;
            }

            // Read at the round as it stands, with the body aimed at half a step back (GravityAtRound).
            // Moving the sample point instead flew and lost. docs/KSA-FRAME-ORDER.md section 5.
            double3 gravity = GravityAtRound(round.PositionEcl, dt);

            // At the round, not the platform: a climbing round leaves the air behind.
            double mediumDensity = MediumAtRound(round.PositionEcl);

            // The ground under the round, not the launcher, back-dated to the frame's start: see
            // GroundVelocityIntoFrame.
            double3 airVelocity = GroundVelocityAtRound(round.PositionEcl + _bodyVelocityEcl * dt);

            // What it could run into, not what it was aimed at.
            if (round is Slug slug)
            {
                slug.Contacts = ContactCandidates();
                slug.Hull = HullTest.Shared;
            }

            // The driver the headless rig uses too. Lookups cached: a burst is 150 shells.
            RoundDriver.Fly(round, dt, Seek(round, SampleTarget(round), dt), gravity, airVelocity, PlatformEcl,
                            round.Munition, mediumDensity,
                            new RoundFields(_gravityAt ??= GravityIntoFrame,
                                            _airDensityAt ??= AirDensityIntoFrame,
                                            GroundTest.Shared,
                                            _groundDriftAt ??= GroundCentreDriftIntoFrame,
                                            _groundQueryDriftAt ??= GroundQueryDriftIntoFrame,
                                            _approachAt ??= ApproachAt,
                                            _airVelocityAt ??= AirVelocityIntoFrame));

            // Not "no longer flying": a shot-down round is reaped above, a frame later.
            if (round.State is RoundState.Detonated or RoundState.Expired or RoundState.Dud) RoundEnded?.Invoke(round);

            switch (round.State)
            {
                case RoundState.Detonated:
                    Detonate(round);
                    DropRound(i);
                    break;
                case RoundState.Dud:
                    Dud(round);
                    DropRound(i);
                    break;
                case RoundState.Expired:
                    Announce(
                        $"{RoundLabel.For(round.Tube)} expired after {round.Age:F1}s - " +
                        $"closest {(round.ClosestApproach == double.MaxValue ? "n/a" : $"{round.ClosestApproach:F0} m")}, " +
                        $"flew {round.DistanceFlown / 1000.0:F1} km, final speed {round.Speed:F0} m/s, " +
                        $"lock={round.HasLock}");
                    DropRound(i);
                    break;
            }
        }

        ApplyPendingKills();
    }

    // Not MissDistance, which is bounded by the fuse radius. Re-sampled, since a place on a body moves.
    private string MissFromAimpoint(IProjectile round)
    {
        if (round.Aimpoint.Kind is AimpointKind.None) return string.Empty;
        if (SampleTarget(round) is not { } target) return string.Empty;

        double3 burst = round.PositionEcl;
        if (!Vec.IsFinite(burst) || !Vec.IsFinite(target.PositionEcl)) return string.Empty;

        // To the burst's instant inside the frame: up to 507 m at 60 fps, a common bias. The blast
        // sweep and the diagnostic do the same, so the three agree.
        double3 aimAtBurst = InFrame.AtBurst(target.PositionEcl, target.VelocityEcl, round.DetonationElapsedInFrame);
        if (!Vec.IsFinite(aimAtBurst)) aimAtBurst = target.PositionEcl;

        double miss = Vec.Len(burst - aimAtBurst);
        if (!double.IsFinite(miss)) return string.Empty;

        string where = WhereOnTheGround(burst, aimAtBurst);

        return $", {Distance.Say(miss)} from the aim point{where}";
    }

    // Beside a steady-target lead's miss: the two agreeing is an unled acceleration. The track is
    // against the ground; the ecliptic velocity points along the planet's orbit.
    private void LogLeadCheck(IProjectile round)
    {
        if (round is not Slug { BurstOnTime: true } shell || round.Aimpoint.Kind != AimpointKind.Vehicle) return;
        if (Platform is not { } platform || SampleTarget(round) is not { } target) return;

        double3 targetAtBurst = InFrame.AtBurst(target.PositionEcl, target.VelocityEcl, round.DetonationElapsedInFrame);
        double3 up = -KsaWorld.GravityAt(platform, targetAtBurst);
        double3 track = round.Aimpoint.VelocityEcl - KsaWorld.GroundVelocityAt(platform, targetAtBurst);
        if (!Vec.IsFinite(targetAtBurst) || !Vec.IsFinite(up) || !Vec.IsFinite(track)) return;

        LeadError.Split miss = LeadError.Resolve(round.PositionEcl - targetAtBurst, track, up);
        LeadError.Split steady = LeadError.Resolve(
            LeadError.SteadyTargetMiss(round.Aimpoint.VelocityEcl, target.VelocityEcl, shell.FuseSeconds),
            track, up);

        Log.Info($"  lead check: burst {miss} of the target; a lead assuming it held its velocity "
                 + $"would have missed by {steady}");
    }

    // Short or long is energy, left or right the plane or the clock.
    private string WhereOnTheGround(double3 burstEcl, double3 aimEcl)
    {
        Celestial? body = Platform is { } craft ? KsaWorld.ParentBody(craft) : _looseBody;
        if (body is null) return string.Empty;

        try
        {
            double3 centre = body.GetPositionEcl();
            double burstLat = body.GetLatitudeFromCce(burstEcl - centre);
            double burstLon = body.GetLongitudeFromCce(burstEcl - centre);
            double aimLat = body.GetLatitudeFromCce(aimEcl - centre);
            double aimLon = body.GetLongitudeFromCce(aimEcl - centre);

            return $" (landed {burstLat:F3},{burstLon:F3} aimed {aimLat:F3},{aimLon:F3})";
        }
        catch
        {
            return string.Empty;
        }
    }

    // Once per frame. Null when the target is gone, which breaks the lock and leaves it coasting.
    private TargetState? SampleTarget(IProjectile round)
    {
        // Re-read every frame, or left behind at ~29.8 km/s and read as closing speed.
        if (round.Aimpoint.Kind == AimpointKind.Ground)
        {
            if (KsaWorld.TryGroundAnchorEcl(round.Aimpoint.Handle, round.Aimpoint.Anchor,
                                            out double3 groundEcl, out double3 groundVel))
            {
                round.Aimpoint = round.Aimpoint.Resampled(groundEcl, groundVel);
            }

            return round.Aimpoint.ToTargetState();
        }

        if (round.Aimpoint.Kind == AimpointKind.Point) return round.Aimpoint.ToTargetState();

        double3 positionEcl;
        double3 velocityEcl;
        double radius;
        object handle;
        bool emitting;

        // KSA holds no state for a round: it comes off the airborne list, sampled for every system
        // before any steps, so roster order cannot matter. See RoundContact.
        if (round.TargetRef is IProjectile)
        {
            if (!_incomingByHandle.TryGetValue(round.TargetRef, out IContact? hostile)
                || !hostile.IsAlive)
            {
                return null;
            }

            positionEcl = hostile.PositionEcl;
            velocityEcl = hostile.VelocityEcl;
            radius = hostile.MeanRadius;
            handle = hostile.Handle;

            // A round carries no set; an anti-radiation seeker falls back to the emission it remembers.
            emitting = false;
        }
        else if (round.TargetRef is Vehicle target && KsaWorld.IsAlive(target))
        {
            positionEcl = KsaWorld.PositionEcl(target);
            velocityEcl = KsaWorld.VelocityEcl(target);
            radius = KsaWorld.MeanRadius(target);
            handle = target;

            // Every frame: a set shutting down mid-flight is the counter to an anti-radiation round.
            emitting = _craftIsEmitting?.Invoke(target) ?? false;
        }
        else return null;

        // A command-linked round is guided only while the set can see the target; the fuse still
        // works. Sight, not the track list, which carries ProtectControlledVehicle: that belongs at
        // the kill, in Detonate.
        if (round.Munition.NeedsUplink)
        {
            // No set left is the end of the uplink, not an exemption from it.
            if (Platform is null) return null;

            double3 toTarget = positionEcl - PlatformEcl;
            var signature = new ThreatModel.ContactSignature(radius, double.PositiveInfinity);

            if (!ThreatModel.InSensorVolume(toTarget, Boresight, Sensor, signature)) return null;

            // Nor while chaff has the set's track: the set is steering on the cloud, not the target.
            if (Radar.ChaffBroke(handle))
            {
                Announce($"{RoundLabel.For(round.Tube)} lost its uplink: the set's track went to chaff");
                return null;
            }
        }

        return new TargetState(positionEcl, velocityEcl, radius, handle, emitting);
    }

    // Held for each round's whole flight.
    private readonly Dictionary<IProjectile, SeekerLock> _seekers = [];
    private static readonly Random _seduction = new();

    // Only on the round's own step, never from SampleTarget, which would roll a decoy's chance twice.
    private TargetState? Seek(IProjectile round, TargetState? target, double dt)
    {
        if (target is not { } t || !round.Munition.Seducible) return target;
        if (t.Handle is not (Vehicle or IProjectile)) return target;

        if (!_seekers.TryGetValue(round, out SeekerLock? seeker))
        {
            if (Countermeasures.Live.Count == 0) return target;
            _seekers[round] = seeker = new SeekerLock(_seduction);
        }

        Decoy? before = seeker.OnDecoy;
        double signature = SignatureOf(round.Munition.Band, t);
        SeekerPick pick = seeker.Choose(round.Munition, round.PositionEcl, round.VelocityEcl, round.VelocityLocal,
                                        t, signature, Countermeasures.Live, dt);

        if (seeker.OnDecoy is { } taken && !ReferenceEquals(taken, before))
        {
            string unit = round.Munition.Band == SeekerBand.Infrared ? "kW/sr" : "m²";
            Announce($"{RoundLabel.For(round.Tube)} was taken by {taken.Profile.DisplayName} "
                     + $"{Vec.Len(taken.PositionEcl - t.PositionEcl):F0} m from its target "
                     + $"({taken.Signature:F1} {unit} against the target's {signature:F1})");
        }
        else if (before is not null && seeker.OnDecoy is null)
        {
            Announce(pick == SeekerPick.Lost
                         ? $"{RoundLabel.For(round.Tube)} lost its decoy and flies on blind"
                         : $"{RoundLabel.For(round.Tube)} lost its decoy and looks for its target again");
        }

        return seeker.Steer(pick, t);
    }

    private static double SignatureOf(SeekerBand band, TargetState target) => band switch
    {
        SeekerBand.Infrared => target.Handle switch
        {
            Vehicle v => KsaWorld.HeatOf(v),
            IProjectile p => Signature.HeatOfRound(p.MotorBurning),
            _ => Signature.AirframeKwPerSr,
        },
        _ => RadarSignature.CrossSectionFor(target.Radius),
    };

    // The platform excepted, and built at most once a frame.
    private List<TargetState> ContactCandidates()
    {
        if (_contactsFresh) return _contactScratch;

        _contactsFresh = true;
        _contactScratch.Clear();

        IReadOnlyList<Vehicle> world = KsaWorld.Vehicles;
        for (int i = 0; i < world.Count; i++)
        {
            Vehicle v = world[i];
            if (ReferenceEquals(v, Platform)) continue;

            _contactScratch.Add(new TargetState(KsaWorld.PositionEcl(v), KsaWorld.VelocityEcl(v),
                                                KsaWorld.MeanRadius(v), v));
        }

        return _contactScratch;
    }

    // The fuse's verdict on what it struck, then the splash; what breaks is decided in Damage.
    private void Detonate(IProjectile round)
    {
        // A part aimpoint arrives and destroys nothing. The kind, not the handle: Ground carries one too.
        if (round.Aimpoint.Kind == AimpointKind.Part)
        {
            Announce($"{RoundLabel.For(round.Tube)} arrived at its {round.Aimpoint.Kind} aimpoint");
            return;
        }

        // A round a decoy took fused on the decoy, so everything measured against its target is not.
        bool seduced = _seekers.TryGetValue(round, out SeekerLock? seeker) && seeker.OnDecoy is not null;

        double3 burst = round.PositionEcl;
        _ground = GroundFor(burst, round.DetonationElapsedInFrame, round.Munition.ChargeKg);
        _front = FrontFor(burst, round.DetonationElapsedInFrame, round.Munition.ChargeKg);
        MunitionProfile judged = JudgedAs(round.Munition, burst, round.DetonationElapsedInFrame);

        string fuse = round is Slug { BurstOnTime: true } timed
                          ? $" (timed, {timed.FuseSeconds:F2} s)"
                          : string.Empty;

        // MissDistance is the range at the fuse trigger, bounded by the fuse radius, never a miss.
        string how = round switch
        {
            Slug { HitGround: true } or Interceptor { HitGround: true } => "on the ground",
            Slug { BurstAtHeight: true } => $"at its fuse height, {round.Munition.BurstHeightMetres:F0} m over the ground",
            _ when round.StruckBody is not null => "on contact",
            _ when seduced => $"on {seeker!.OnDecoy!.Profile.DisplayName}",
            _ when double.IsFinite(round.MissDistance) => $"with the target at {round.MissDistance:F0} m",
            _ => "with nothing in range",
        };

        Announce($"{RoundLabel.For(round.Tube)} detonated{fuse} {how}{MissFromAimpoint(round)}");
        LogLeadCheck(round);

        // Per burst, not per frame: two rounds at opposite ends of one booster each break their own parts.
        _burstDamaged.Clear();

        // fuse is what the kill is judged on; atBurst should match it; drawn disagreeing alone means
        // the drawing is wrong. Each pairs one instant: across 2.8 ms of ecliptic motion is 73 m.
        if (round.TargetRef is Vehicle logTarget && KsaWorld.IsAlive(logTarget))
        {
            double intoFrame = round.DetonationElapsedInFrame;
            double3 targetEcl = KsaWorld.PositionEcl(logTarget);
            double3 targetVel = KsaWorld.VelocityEcl(logTarget);

            double atBurst = Vec.Len(targetEcl + targetVel * intoFrame - burst);

            // Both sides back-dated. A loose system has no platform, and a throw here leaves the round
            // unreaped to burst every frame.
            double3 anchorVelocity = Platform is { } host ? KsaWorld.VelocityEcl(host) : Vec.Zero;
            double3 offsetAtBurst = DrawAnchor.OffsetAtBurst(round.OffsetFromPlatform, anchorVelocity, intoFrame);
            double drawn = Vec.Len(offsetAtBurst - (targetEcl + (targetVel * intoFrame) - PlatformEcl
                                                    - (anchorVelocity * intoFrame)));

            // As rendered: KSA draws a vehicle at its physics position, not the analytic one.
            double onScreen = -1.0;
            if (KsaWorld.HasAnchor && KsaWorld.TryVehicleEgo(logTarget, out double3 targetEgo))
                onScreen = Vec.Len(KsaWorld.AnchorEgo + offsetAtBurst - targetEgo);

            Log.Debug(() =>
                $"  detonation: fuse {round.MissDistance:F1} m, atBurst {atBurst:F1} m, " +
                $"onScreen {(onScreen < 0 ? "n/a" : $"{onScreen:F1} m")}, " +
                $"drawn {drawn:F1} m; {intoFrame * 1000.0:F1}ms into the frame; " +
                $"sim {KsaWorld.SimulationSpeed:F2}x step {KsaWorld.SimStepSeconds * 1000.0:F1}ms");
        }

        // The burst's offset into the frame, which every sweep advances the world to.
        double elapsed = round.DetonationElapsedInFrame;

        // A hole on whatever hull it touched, whoever's: it is paint.
        if (round.StruckBody is Vehicle holed) BulletHoles.Strike(holed, round);

        // What it met, else what it was aimed at, on the fuse's separation. Not for a round a decoy
        // took, whose miss distance is to the decoy; the splash still judges what it burst beside.
        if (!seduced && (round.StruckBody ?? round.TargetRef) is Vehicle intended && KsaWorld.IsAlive(intended))
        {
            double lethalRange = judged.LethalRadius + KsaWorld.MeanRadius(intended);
            if (round.MissDistance <= lethalRange)
            {
                // Said, or an immune target looks like a miss.
                if (ReferenceEquals(intended, Platform))
                {
                    Announce($"hit on {KsaWorld.DisplayName(intended)} ignored - it is now the system's own platform");
                }
                else if (_policy.ProtectControlledVehicle && ReferenceEquals(intended, KsaWorld.ControlledVehicle))
                {
                    Announce($"hit on {KsaWorld.DisplayName(intended)} ignored - you are flying it (untick 'Never target the vehicle I'm flying')");
                }
                else
                {
                    // The fuse ruled it lethal: the sweep decides what breaks, never whether.
                    Damage(intended, burst, elapsed, judged, confirmed: true);
                }
            }
        }

        // The same for a round in the air, off the airborne list: KSA holds no state for it. Nobody
        // flies a round, and the own salvo was filtered out.
        if (!seduced
            && (round.StruckBody ?? round.TargetRef) is IProjectile hit
            && hit.State == RoundState.Flying
            && _incomingByHandle.TryGetValue(hit, out IContact? hitContact))
        {
            if (round.MissDistance <= judged.LethalRadius + hitContact.MeanRadius)
            {
                hit.ShootDown();
                Announce($"intercepted {hitContact.DisplayName} at {round.MissDistance:F1} m");
            }
        }

        // The splash over every other round: a warhead need not touch what it kills.
        for (int i = 0; i < _incoming.Count; i++)
        {
            IContact contact = _incoming[i];
            if (contact.Handle is not IProjectile other || other.State != RoundState.Flying) continue;

            double gap = BlastSweep.SurfaceGap(contact.PositionEcl, contact.VelocityEcl, elapsed,
                                               burst, contact.MeanRadius);

            if (BlastSweep.Effect(gap, judged) != BlastEffect.Lethal) continue;

            other.ShootDown();
            Announce($"intercepted {contact.DisplayName} at {gap:F0} m");
        }

        Splash(burst, elapsed, judged);

        // Sized off the charge, as the damage radii are.
        if (_config.DrawExplosions)
        {
            ReportBurstPlacement(round, burst);

            // EffectBody too: a loose system has no platform and a ground store no target craft.
            Detonation.Explode(DrawnBurstEcl(round, burst), round.Munition.ChargeKg,
                               round.TargetRef as Vehicle ?? Platform, EffectBody, round.Munition.BurstEffect);

            // A shell's smoke outlasts its flash; a missile's burst is KSA's explosion alone.
            if (RoundLabel.IsGunRound(round.Tube))
            {
                FlakPuff.Throw(DrawnBurstEcl(round, burst), round.Munition.ChargeKg,
                               double.IsFinite(_ground.Height) ? _ground.Height : double.PositiveInfinity,
                               round.TargetRef as Vehicle ?? Platform, EffectBody);
            }

            // NuclearClouds keeps it past this system. Its height is fire control's, off the burst
            // carried with the ground: a loose round has no platform to carry the drawn point.
            NuclearClouds.Begin(DrawnBurstEcl(round, burst),
                                round.TargetRef as Vehicle ?? Platform,
                                round.Munition.ChargeKg, EffectBody,
                                double.IsFinite(_ground.Height) ? _ground.Height : null);
        }
    }

    // A round stopped before its fuze armed: the hit, and no blast, flash or cloud.
    private void Dud(IProjectile round)
    {
        string what = round.StruckBody switch
        {
            Vehicle v => KsaWorld.DisplayName(v),
            IProjectile => "a round in the air",
            _ => "the ground",
        };
        Announce($"{RoundLabel.For(round.Tube)} struck {what} {round.Age:F2} s out, before its fuze armed at "
                 + $"{round.Munition.FuseArmSeconds:F2} s: a dud");

        if (round.StruckBody is IProjectile { State: RoundState.Flying } other) other.ShootDown();
        if (round.StruckBody is not Vehicle struck || !KsaWorld.IsAlive(struck)) return;

        BulletHoles.Strike(struck, round);
        if (ReferenceEquals(struck, Platform)) return;
        if (_policy.ProtectControlledVehicle && ReferenceEquals(struck, KsaWorld.ControlledVehicle)) return;

        StrikeInert(struck, round.PositionEcl, round.DetonationElapsedInFrame);
    }

    // The part a dud struck pays one share, as a strike does; nothing near it is touched.
    private void StrikeInert(Vehicle v, double3 at, double elapsed)
    {
        if (_pendingKills.Contains(v)) return;

        if (!_config.DamageIndividualParts || !KsaWorld.CanQueuePartFailures
            || !KsaWorld.TryCollectDamageableParts(v, KsaWorld.PositionEcl(v), _partScratch, _partHandles))
        {
            QueueWholeCraft(v);
            return;
        }

        if (StruckPart(v, at, elapsed) is not { } index) return;

        Part part = _partHandles[index];
        if (!PartHealth.World.Hit(part, 1.0, _config.DamageScale))
        {
            Announce($"hit {part.Id} on {KsaWorld.DisplayName(v)}: {PartHealth.World.Of(part):P0} left");
            return;
        }

        // KSA's own fragment guard, asked rather than reproduced.
        if (KsaWorld.LosingThatManyPartsIsFatal(1, _partHandles.Count))
        {
            QueueWholeCraft(v);
            return;
        }

        if (!_pendingPartKills.TryGetValue(v, out List<Part>? queued))
        {
            queued = [];
            _pendingPartKills[v] = queued;
        }

        if (queued.Contains(part)) return;

        queued.Add(part);
        Announce($"1 part(s) broken off {KsaWorld.DisplayName(v)}");
    }

    // Measurement only: the burst carried by the platform's velocity (the effects) against the
    // ground's (the chase camera and scoring), apart by the craft's motion over a fraction of a frame.
    private void ReportBurstPlacement(IProjectile round, double3 burst)
    {
        if (Log.Threshold > Log.Level.Debug || EffectBody is not { } body) return;

        double3 drawn = DrawnBurstEcl(round, burst);
        double3 onTheGround = BlastSweep.GroundAtSample(burst, KsaWorld.GroundVelocityAt(body, burst),
                                                        round.DetonationElapsedInFrame);

        if (!Vec.IsFinite(drawn) || !Vec.IsFinite(onTheGround)) return;

        double gap = Vec.Len(drawn - onTheGround);
        Log.Debug(() => $"  burst drawn {gap:F1} m from the ground it reached "
                        + $"({round.DetonationElapsedInFrame:F3} s into the step)");
    }

    // Where the round was drawn, at the step's end the explosion is anchored in: carried with the
    // platform, up to a step of 29.8 km/s, and through the camera's physics-against-analytic gap.
    private double3 DrawnBurstEcl(IProjectile round, double3 analyticEcl)
    {
        if (Platform is not { } platform) return analyticEcl;
        if (!KsaWorld.TryVehicleEgo(platform, out double3 platformEgo)) return analyticEcl;

        double3 offset = DrawAnchor.OffsetAtBurst(round.OffsetFromPlatform, KsaWorld.VelocityEcl(platform),
                                                  round.DetonationElapsedInFrame);
        if (!KsaWorld.TryEgoToEcl(platformEgo + offset, out double3 drawn))
        {
            return analyticEcl;
        }

        double3 carried = InFrame.AtSample(analyticEcl, KsaWorld.VelocityEcl(platform), round.DetonationElapsedInFrame);
        double residual = Vec.Len(drawn - carried);
        if (residual > 1.0)
        {
            Log.Debug(() => $"  burst drawn {residual:F1} m from where the round struck, after carrying it "
                            + $"{Vec.Len(carried - analyticEcl):F1} m to the step's end");
        }

        return drawn;
    }

    // Shared by a round's detonation and SplashAt, so a bridge burst is judged as a warhead is.
    private void Splash(double3 burst, double elapsed, MunitionProfile munition, bool spareOwn = true)
    {
        IReadOnlyList<Vehicle> caught = KsaWorld.Vehicles;

        for (int i = 0; i < caught.Count; i++)
        {
            Vehicle v = caught[i];

            // Never broken, but dented: a dent breaks nothing.
            if (spareOwn && (ReferenceEquals(v, Platform)
                             || (_policy.ProtectControlledVehicle && ReferenceEquals(v, KsaWorld.ControlledVehicle))))
            {
                DentOnly(v, burst, elapsed, munition);
                continue;
            }

            if (_pendingKills.Contains(v)) continue;
            if (_burstDamaged.Contains(v)) continue;

            double gap = ReflectedGap(v, BlastSweep.SurfaceGap(KsaWorld.PositionEcl(v), KsaWorld.VelocityEcl(v),
                                                               elapsed, burst, KsaWorld.MeanRadius(v)), munition);

            switch (BlastSweep.Effect(gap, munition))
            {
                case BlastEffect.Lethal:
                    Damage(v, burst, elapsed, munition, confirmed: false);
                    break;

                case BlastEffect.NearMiss:
                    // The sphere is a half-diagonal, so a burst beside a long booster can touch it from
                    // "a hundred metres"; the part sweep is the exact test.
                    if (!Damage(v, burst, elapsed, munition, confirmed: false))
                    {
                        Announce($"near miss on {KsaWorld.DisplayName(v)} at {gap:F0} m");
                    }

                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>
    /// A burst with no round, judged as a warhead's, for the bridge. <paramref name="spareOwn"/>
    /// false judges this craft and the one flown like any other. <paramref name="inFrame"/> is a
    /// round's <c>DetonationElapsedInFrame</c>: between minus one step and zero, <paramref name="burstEcl"/> then.
    /// </summary>
    public void SplashAt(double3 burstEcl, MunitionProfile munition, bool spareOwn = true, double inFrame = 0.0)
    {
        ArgumentNullException.ThrowIfNull(munition);

        _burstDamaged.Clear();
        _ground = GroundFor(burstEcl, Math.Min(inFrame, 0.0), munition.ChargeKg);
        _front = FrontFor(burstEcl, Math.Min(inFrame, 0.0), munition.ChargeKg);
        Splash(burstEcl, Math.Min(inFrame, 0.0), JudgedAs(munition, burstEcl, Math.Min(inFrame, 0.0)), spareOwn);
        ApplyPendingKills();
    }

    // With the burst carried to the sample: uncarried, its height is up to a step of 30 km/s wrong.
    private GroundReflection GroundFor(double3 burst, double elapsed, double chargeKg)
    {
        if ((EffectBody ?? Detonation.BodyFor(Platform)) is not { } body) return GroundReflection.FreeAir;

        double3 atSample = BlastSweep.GroundAtSample(burst, KsaWorld.GroundVelocityAt(body, burst), elapsed);
        return KsaWorld.GroundReflectionAt(body, atSample, chargeKg);
    }

    private BlastFront FrontFor(double3 burst, double elapsed, double chargeKg)
    {
        if ((EffectBody ?? Detonation.BodyFor(Platform)) is not { } body) return BlastFront.SeaLevel(chargeKg);

        double3 atSample = BlastSweep.GroundAtSample(burst, KsaWorld.GroundVelocityAt(body, burst), elapsed);
        double height = double.IsFinite(_ground.Height) ? Math.Max(_ground.Height, 0.0) : double.PositiveInfinity;
        return BlastFront.For(chargeKg, KsaWorld.AirAt(body, atSample), KsaWorld.BodyAirOf(body).SoundMetresPerSecond,
                              height);
    }

    // BlastAltitude.EquivalentChargeKg at the burst carried to the sample; the same profile where that
    // is the whole charge.
    private MunitionProfile JudgedAs(MunitionProfile munition, double3 burst, double elapsed)
    {
        if (munition.ChargeKg < MushroomCloud.ThresholdKg) return munition;
        if ((EffectBody ?? Detonation.BodyFor(Platform)) is not { } body) return munition;

        double3 atSample = BlastSweep.GroundAtSample(burst, KsaWorld.GroundVelocityAt(body, burst), elapsed);
        AmbientAir air = KsaWorld.AirAt(body, atSample);
        double charge = BlastAltitude.EquivalentChargeKg(munition.ChargeKg, air,
                                                         KsaWorld.BodyAirOf(body).Traits.XRayOpacity);
        if ((float)charge == munition.ChargeKg) return munition;

        MunitionProfile judged = munition.Copy();
        judged.ChargeKg = (float)charge;

        Log.Info($"burst in air at {air.PressureRatio:E2} of sea level's pressure: its blast is "
                 + $"{BlastAltitude.Efficiency(munition.ChargeKg, air):P1} of a low burst's, so it breaks what "
                 + $"{MushroomCloud.KilotonsFor(charge):G3} kt would ({judged.LethalRadius / 1000.0:F2} km lethal, "
                 + $"{judged.BlastRadius / 1000.0:F2} km blast)");
        return judged;
    }

    // The ground's reflection multiplies the charge, so the distance by its cube root.
    private double ReflectedGap(Vehicle v, double gap, MunitionProfile munition)
        => gap / Math.Cbrt(_ground.GainAt(KsaWorld.PositionEcl(v), munition.ChargeKg));

    private void Reflect(MunitionProfile munition)
    {
        for (int i = 0; i < _partScratch.Count; i++)
        {
            DamageablePart p = _partScratch[i];
            _partScratch[i] = p with { Reflection = _ground.GainAt(p.PositionEcl, munition.ChargeKg) };
        }
    }

    private void DentOnly(Vehicle v, double3 burst, double elapsed, MunitionProfile munition)
    {
        if (!KsaWorld.IsAlive(v)) return;

        double gap = ReflectedGap(v, BlastSweep.SurfaceGap(KsaWorld.PositionEcl(v), KsaWorld.VelocityEcl(v),
                                                           elapsed, burst, KsaWorld.MeanRadius(v)), munition);
        if (BlastSweep.Effect(gap, munition) == BlastEffect.Untouched) return;

        if (!KsaWorld.TryCollectDamageableParts(v, KsaWorld.PositionEcl(v), _partScratch, _partHandles)) return;
        Reflect(munition);

        Dent(v, burst, elapsed, munition, failed: null, mayBreak: false);
    }

    // Held until the front reaches each part; with no air there is no front to wait for.
    private void Dent(Vehicle v, double3 burst, double elapsed, MunitionProfile munition,
                      IReadOnlyCollection<int>? failed, bool mayBreak)
    {
        _dentLoads.Clear();
        BlastDamage.Loads(burst, elapsed, KsaWorld.VelocityEcl(v), CollectionsMarshal.AsSpan(_partScratch),
                          munition, failed, _dentLoads);
        if (_dentLoads.Count == 0) return;

        double airRatio = KsaWorld.AirDensityRatioAt(v, burst);
        bool air = airRatio > Medium.NoticeableDensity;
        // At the sample, which the burst is up to a step of the planet's motion behind.
        double3 groundAtSample = BlastSweep.GroundAtSample(burst, KsaWorld.GroundVelocityAt(v, burst), elapsed);

        double first = double.MaxValue;
        double last = 0.0;
        foreach ((int index, double ratio, double gap) in _dentLoads)
        {
            double due = air ? _front.ArrivalSeconds(gap) - elapsed : 0.0;
            BlastArrivals.Queue(v, _partHandles[index], groundAtSample, -elapsed, air ? airRatio : 0.0,
                                munition.ChargeKg, _partScratch[index].CrashTolerancePascals, mayBreak,
                                _config.DamageScale, _partScratch[index].Reflection, _ground, _front);

            first = Math.Min(first, due);
            last = Math.Max(last, due);
        }

        Log.Info($"blast loads {_dentLoads.Count} part(s) of {KsaWorld.DisplayName(v)} short of breaking; "
                 + $"the front would arrive in {Math.Max(first, 0.0):F2}-{Math.Max(last, 0.0):F2} s were it to stay put");
    }

    // Queued, never applied: ApplyPendingKills drains it, as the engine's solvers are live here.
    // `confirmed` is the fuse's lethal verdict, which an empty sweep cannot overturn. True when
    // anything was queued.
    private bool Damage(Vehicle v, double3 burst, double elapsed, MunitionProfile munition,
                        bool confirmed)
    {
        if (!KsaWorld.IsAlive(v)) return false;
        if (_pendingKills.Contains(v)) return true;

        _burstDamaged.Add(v);

        // Without the engine's event slot (KsaWorld.TryQueuePartFailure) whole craft are destroyed.
        if (!_config.DamageIndividualParts || !KsaWorld.CanQueuePartFailures)
        {
            if (!confirmed && BlastSweep.Effect(
                    ReflectedGap(v, BlastSweep.SurfaceGap(KsaWorld.PositionEcl(v), KsaWorld.VelocityEcl(v),
                                                          elapsed, burst, KsaWorld.MeanRadius(v)), munition),
                    munition) != BlastEffect.Lethal)
            {
                return false;
            }

            return QueueWholeCraft(v);
        }

        // An unreadable part tree is not a bulletproof craft: the whole-craft rule.
        if (!KsaWorld.TryCollectDamageableParts(v, KsaWorld.PositionEcl(v), _partScratch, _partHandles))
        {
            return confirmed
                ? QueueWholeCraft(v)
                : QueueWholeCraftIfLethal(v, burst, elapsed, munition);
        }

        Reflect(munition);

        // At a scale of one a fresh part breaks exactly where the sweep alone broke it.
        _failedParts.Clear();
        _partShares.Clear();
        BlastDamage.Shares(burst, elapsed, KsaWorld.VelocityEcl(v),
                           CollectionsMarshal.AsSpan(_partScratch), munition, _partShares);
        foreach ((int index, double share) in _partShares)
        {
            if (PartHealth.World.Hit(_partHandles[index], share, _config.DamageScale)) _failedParts.Add(index);
        }

        // Before the craft's fate is decided: the dents are for a craft that survives.
        Dent(v, burst, elapsed, munition, _failedParts, mayBreak: true);

        // KSA logs no part's crash tolerance, and it is what sets a warhead's reach against that part.
        if (Log.Threshold <= Log.Level.Debug)
        {
            string craft = KsaWorld.DisplayName(v);
            double3 velocity = KsaWorld.VelocityEcl(v);
            foreach (DamageablePart p in _partScratch)
            {
                double gap = BlastSweep.SurfaceGap(p.PositionEcl, velocity, elapsed, burst, p.RadiusMetres);
                double reach = BlastDamage.FailureRadius(munition.ChargeKg * p.Reflection, p.CrashTolerancePascals);
                double health = PartHealth.World.Of(_partHandles[p.Index]);
                Log.Debug($"blast on {craft}: {_partHandles[p.Index].Id} gap {gap:F1} m, reach {reach:F1} m "
                          + $"at {p.CrashTolerancePascals / 1e6:F2} MPa, health {health:P0}"
                          + $"{(_failedParts.Contains(p.Index) ? ", breaks" : string.Empty)}");
            }
        }

        if (_failedParts.Count == 0)
        {
            // Only the fuse's verdict overrides the sweep: the struck part pays a whole share.
            if (!confirmed) return false;
            if (StruckPart(v, burst, elapsed) is not { } struck) return QueueWholeCraft(v);
            if (!StrikeBreaks(v, struck)) return true;

            _failedParts.Add(struck);
        }

        // KSA's own fragment guard, asked rather than reproduced.
        if (KsaWorld.LosingThatManyPartsIsFatal(_failedParts.Count, _partHandles.Count))
        {
            return QueueWholeCraft(v);
        }

        if (!_pendingPartKills.TryGetValue(v, out List<Part>? queued))
        {
            queued = [];
            _pendingPartKills[v] = queued;
        }

        int added = 0;
        for (int i = 0; i < _failedParts.Count; i++)
        {
            Part part = _partHandles[_failedParts[i]];

            // Twice to PartFailureEvent is a split the engine cannot make.
            if (queued.Contains(part)) continue;

            queued.Add(part);
            added++;
        }

        if (added == 0) return true;

        Announce($"{added} part(s) broken off {KsaWorld.DisplayName(v)}");
        return true;
    }

    // The part nearest a strike, or null for a craft with no part to charge.
    private int? StruckPart(Vehicle v, double3 burst, double elapsed)
    {
        double3 velocity = KsaWorld.VelocityEcl(v);
        int? nearest = null;
        double best = double.PositiveInfinity;
        foreach (DamageablePart p in _partScratch)
        {
            double gap = BlastSweep.SurfaceGap(p.PositionEcl, velocity, elapsed, burst, p.RadiusMetres);
            if (gap < best) (best, nearest) = (gap, p.Index);
        }

        return nearest;
    }

    // Tops this burst's charge on the struck part up to a whole share; true when it breaks.
    private bool StrikeBreaks(Vehicle v, int struck)
    {
        double paid = 0.0;
        foreach ((int index, double share) in _partShares)
        {
            if (index == struck) paid = Math.Min(share, 1.0);
        }

        Part part = _partHandles[struck];
        if (PartHealth.World.Hit(part, 1.0 - paid, _config.DamageScale)) return true;

        Announce($"hit {part.Id} on {KsaWorld.DisplayName(v)}: {PartHealth.World.Of(part):P0} left");
        return false;
    }

    private bool QueueWholeCraft(Vehicle v)
    {
        if (_pendingKills.Contains(v)) return true;

        _pendingKills.Add(v);
        return true;
    }

    private bool QueueWholeCraftIfLethal(Vehicle v, double3 burst, double elapsed,
                                         MunitionProfile munition)
    {
        double gap = ReflectedGap(v, BlastSweep.SurfaceGap(KsaWorld.PositionEcl(v), KsaWorld.VelocityEcl(v),
                                                           elapsed, burst, KsaWorld.MeanRadius(v)), munition);

        return BlastSweep.Effect(gap, munition) == BlastEffect.Lethal && QueueWholeCraft(v);
    }

    private void ApplyPendingKills()
    {
        if (_pendingKills.Count == 0 && _pendingPartKills.Count == 0) return;

        // The workers enumerate the vehicle list while this hook runs (KsaWorld.WaitForVehicleSolvers);
        // joined once for the whole batch, which is why kills are deferred.
        KsaWorld.WaitForVehicleSolvers();

        foreach (Vehicle v in _pendingKills)
        {
            if (!KsaWorld.IsAlive(v)) continue;
            Announce($"destroyed {KsaWorld.DisplayName(v)}");
            KsaWorld.Destroy(v, blastSeverity: 50f);
        }

        // After the whole-craft kills, which IsAlive then filters out. Land on the next frame.
        foreach ((Vehicle v, List<Part> parts) in _pendingPartKills)
        {
            if (KsaWorld.IsAlive(v)) KsaWorld.TryQueuePartFailure(v, parts);
        }

        _pendingKills.Clear();
        _pendingPartKills.Clear();
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
    /// For more simulated time than can be integrated: a huge step would fly rounds through their
    /// targets, and dwell restarts. <b>A round the ground stops is kept</b>: it has no target to fly
    /// through, and deleting it is not recoverable where lagging is.
    /// </summary>
    public void AbandonFlight(string why)
    {
        bool hadRounds = _rounds.Count > 0;

        int kept = DropRoundsWithATargetToLose();
        _pendingKills.Clear();
        _pendingPartKills.Clear();
        Radar.Reset();
        _salvoTimer = 0.0;
        _warnedDuplicateTube = false;

        // Only when nothing was kept: this hides every body, a survivor's too. The per-frame pass
        // hides the dropped ones anyway.
        if (kept == 0)
        {
            for (int i = 0; i < _missileBodies.Count; i++) LauncherPart.HideMissile(_missileBodies[i]);
            for (int i = 0; i < _finBodies.Count; i++) LauncherPart.HideMissile(_finBodies[i]);
        }

        if (hadRounds)
        {
            Announce(kept > 0
                         ? $"rounds abandoned: {why} - {kept} still falling, kept and lagging"
                         : $"rounds abandoned: {why}");
        }
        else Log.Debug(() => $"tracking reset: {why}");
    }

    // Returns how many were kept, so the line does not read as a despawn.
    private int DropRoundsWithATargetToLose()
    {
        for (int i = _rounds.Count - 1; i >= 0; i--)
        {
            if (!_rounds[i].Munition.HitsTerrain) DropRound(i);
        }

        return _rounds.Count;
    }

    public void Reset()
    {
        HeldPart = null;
        ClearRounds();
        _steerable = null;
        _pendingKills.Clear();
        _pendingPartKills.Clear();
        _events.Clear();
        Radar.Reset();
        _magazine.Resize(Profile.TubeCount, Profile.MagazineDepth);
        _salvoTimer = 0.0;
        _reloadTimer = 0.0;
        PlatformPinned = false;
        Platform = null;

        _drives.Clear();
        RoundBodiesWork = true;
        _guns.Fill(Profile.GunAmmo);
        _guns.Reset();
        _nextBarrel = 0;
        _gunReloadTimer = 0.0;
    }
}
