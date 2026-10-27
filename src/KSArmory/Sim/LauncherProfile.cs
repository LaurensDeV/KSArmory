using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// One tube: where its mouth is, and optionally which way it points.
///
/// <para><see cref="Direction"/> is in the same frame as <see cref="Position"/> — the pod's, or the
/// launcher part's for a launcher with no pods. <b>Zero means "share the pod axis"</b>, which is
/// what a launcher with parallel tubes wants and what the model generator emits, so the common case
/// stays a bare position.</para>
///
/// <para>A per-tube direction is what a splayed bundle needs — a VLS with divergence, an MLRS, or
/// anything whose tubes are not a parallel block.</para>
/// </summary>
public readonly record struct Tube(double3 Position, double3 Direction)
{
    /// <summary>A tube that points wherever the pods do. This is the generated form.</summary>
    public Tube(double x, double y, double z) : this(new double3(x, y, z), default) { }

    /// <summary>False when this tube simply follows the pod axis.</summary>
    public bool HasOwnDirection => !Direction.Equals(default(double3));
}

/// <summary>
/// One launch platform: which part it is, where its tubes are, what moves, and what it fires.
///
/// <para>A new weapon system is one of these, declared as a <c>&lt;Launcher&gt;</c> in a definition file. Nothing in the
/// simulation or the KSA binding knows about the Pantsir specifically — they ask a profile. A fixed
/// launcher with no turret is the same type with <see cref="TurretMarker"/> left null.</para>
///
/// <para>A generated launcher's geometry is pasted from what <c>tools/model/pantsir.py</c> prints
/// and checked against the mesh by <c>tools/validate-parts.py</c>. Do not hand-edit it.</para>
/// </summary>
public sealed class LauncherProfile
{
    /// <summary>
    /// What a weapons system is running before it has found its launcher part.
    ///
    /// <para>Deliberately not the first registered launcher: seeding from one makes an unadopted
    /// system claim that weapon's reach and that weapon's radar, and makes an empty registry a
    /// type-initialiser crash rather than a quiet nothing. <see cref="PartId"/> is blank, so no
    /// lookup can return it.</para>
    /// </summary>
    public static readonly LauncherProfile Unfitted = new()
    {
        PartId = "",
        DisplayName = "no launcher",
        Munition = "",
        Sensor = "",
        Tubes = [],
    };

    /// <summary>Part Id declared in the mod's GameData XML. Must match exactly.</summary>
    public required string PartId { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Registry keys for what this launcher shoots and what it sees with.</summary>
    public required string Munition { get; init; }
    public required string Sensor { get; init; }

    /// <summary>
    /// What each armament is called in front of an operator. These name the status row, the
    /// switch that lets the armament engage and its tuning section, so a launcher that lobs
    /// bombs says "Bombs" everywhere without the panel knowing what a bomb is.
    /// </summary>
    public string TubeArmamentLabel { get; init; } = "Missiles";

    /// <inheritdoc cref="TubeArmamentLabel"/>
    public string GunArmamentLabel { get; init; } = "Cannon";

    /// <inheritdoc cref="TubeArmamentLabel"/>
    public string BeamArmamentLabel { get; init; } = "Laser";

    /// <summary>
    /// Fires the high-energy beam of the optical director on the same part. The beam's numbers stay
    /// on that <see cref="OpticProfile.Beam"/>, and the director aims it: this launcher has nothing
    /// of its own that moves.
    /// </summary>
    public bool FiresBeam { get; init; }

    /// <summary>Traverses in azimuth. Null for a launcher that cannot train.</summary>
    public string? TurretMarker { get; init; }

    /// <summary>Elevates on the turret's trunnions, and carries the tubes.</summary>
    public string? PodsMarker { get; init; }

    /// <summary>Turns continuously. Cosmetic.</summary>
    public string? RadarMarker { get; init; }

    /// <summary>Cannon that elevate on their own trunnion. Null for a launcher with none.</summary>
    public string? GunsMarker { get; init; }

    /// <summary>
    /// A barrel that runs back along the bore when the cannon fire. It rides the cannon's trunnion
    /// rather than one of its own. Null for cannon with nothing that recoils.
    /// </summary>
    public string? GunBarrelMarker { get; init; }

    /// <summary>
    /// An actuator between the traverse and the cannon: a cylinder pinned to what traverses and a
    /// rod pinned to the cannon, posed from the elevation by <see cref="ActuatorLinkage"/>. Null for
    /// cannon with no actuator drawn. Drawn only.
    /// </summary>
    public string? GunCylinderMarker { get; init; }
    public string? GunRodMarker { get; init; }

    /// <summary>
    /// A body that turns with the cannon at <see cref="GunFeedRatio"/> of its elevation, about a pin
    /// of its own on the traverse: an ammunition feed. Null for none. Drawn only.
    /// </summary>
    public string? GunFeedMarker { get; init; }

    /// <summary>
    /// A rotary cannon's barrel cluster, spun about the bore while it fires (<see cref="GunRotor"/>).
    /// Null for cannon whose barrels do not turn. Drawn only.
    /// </summary>
    public string? GunRotorMarker { get; init; }

    /// <summary>
    /// An optical director's base, riding the traverse. Null for a launcher carrying none, which
    /// is most of them.
    ///
    /// <para>The launcher's only interest in a director is <em>carrying</em> it: this is here so
    /// the traverse writes the base's transform along with everything else that rides it. What the
    /// head then does is none of the launcher's business — it reads where its base ended up and
    /// aims itself, through its own <c>OpticProfile</c>. See <see cref="OpticGeometry.MountFrame"/>
    /// for why that direction of dependency is the one that generalises.</para>
    /// </summary>
    public string? OpticBaseMarker { get; init; }

    /// <summary>The length of <see cref="Tubes"/>, and the rounds carried unless <see cref="MagazineDepth"/> says more.</summary>
    public int TubeCount => Tubes.Length;

    /// <summary>
    /// Every tube in firing order, in the frame of whichever subpart carries them — the pods if
    /// there are any, otherwise the launcher part itself.
    ///
    /// <para>Positions here are generated by <c>tools/model/pantsir.py</c> and checked against the
    /// exported mesh by <c>tools/validate-parts.py</c>. Directions are not: the generator only
    /// knows parallel tubes, so a splayed launcher declares them by hand.</para>
    /// </summary>
    public required Tube[] Tubes { get; init; }

    /// <summary>Where the turret's traverse axis sits in the part's frame.</summary>
    public double3 TurretPivot { get; init; }

    /// <summary>Where the pods' trunnion sits relative to the turret's axis.</summary>
    public double3 PodPivotFromTurret { get; init; }

    /// <summary>Where the search array's turntable sits relative to the turret's axis.</summary>
    public double3 RadarPivotFromTurret { get; init; }

    /// <summary>Where a carried director's base sits relative to the turret's axis.</summary>
    public double3 OpticBaseFromTurret { get; init; }

    /// <summary>Where the cannon trunnion sits relative to the turret's axis.</summary>
    public double3 GunPivotFromTurret { get; init; }

    /// <summary>
    /// Elevation the cannon are modelled at. Runtime elevation is a rotation away from it, the
    /// same convention as <see cref="PodReferenceElevationRad"/>.
    /// </summary>
    public double GunReferenceElevationRad { get; init; }

    /// <summary>
    /// The actuator's cylinder pin, on the traverse, and its rod pin, on the cannon at
    /// <see cref="GunReferenceElevationRad"/> — both relative to the turret's axis, and each the
    /// pivot its body's mesh is exported recentred on.
    /// </summary>
    public double3 GunCylinderPinFromTurret { get; init; }
    public double3 GunRodPinFromTurret { get; init; }

    /// <summary>Where the feed's pin sits relative to the turret's axis.</summary>
    public double3 GunFeedPivotFromTurret { get; init; }

    /// <summary>How far the feed turns per radian the cannon turns.</summary>
    public double GunFeedRatio { get; init; }

    /// <summary>
    /// A point on the barrel cluster's spin axis at <see cref="GunReferenceElevationRad"/>, relative
    /// to the turret's axis; the cluster's mesh is exported recentred on it.
    /// </summary>
    public double3 GunRotorPivotFromTurret { get; init; }

    /// <summary>
    /// Elevation the pods are modelled at. Runtime elevation is a rotation <em>away</em> from it,
    /// so a refused transform write leaves the vehicle in a pose that looks right.
    /// </summary>
    public double PodReferenceElevationRad { get; init; }

    /// <summary>Distance from the part origin to the tube muzzles along the boresight (m).</summary>
    public double MuzzleForwardOffset { get; init; }

    /// <summary>
    /// Radius of the ring the *fallback* launch positions are spread on (m), used only when
    /// the tube-carrying subpart cannot be resolved.
    /// </summary>
    public double TubeRingRadius { get; init; }

    public float SlewRateDeg = 70f;
    public float ElevationRateDeg = 45f;
    public float SettleSeconds = 0.35f;
    public float SearchRadarRpm = 20f;

    /// <summary>
    /// How many faces of the search array radiate, and therefore how many sweeps a scope draws.
    ///
    /// <para>One for an ordinary single-face set. The Pantsir's array is a double-sided wedge, so
    /// both of its faces are looking at once and the scope shows two traces half a turn apart —
    /// which is why its picture refreshes twice per revolution rather than once.</para>
    ///
    /// <para>A count rather than a flag, so a three- or four-face set needs no new concept. Zero or
    /// less means a set with no rotating array at all, and draws no sweep.</para>
    /// </summary>
    public int SearchRadarFaces = 1;

    /// <summary>Elevation travel, and the higher floor over the vehicle's own bodywork.</summary>
    public float MinElevationDeg;
    public float MaxElevationDeg = 82f;
    public float ForwardMinElevationDeg = 15f;
    public float ForwardArcDeg = 80f;
    public float ForwardPlateauDeg = 62f;

    /// <summary>
    /// How far either side of forward the turret may traverse. 180 is all the way round; less is a
    /// mount whose host is in the way behind it, and then it turns the long way rather than through
    /// the host.
    /// </summary>
    public float TraverseLimitDeg = 180f;

    /// <summary>
    /// Elevation the launcher returns to with nothing to look at. Defaults to the pods' modelled
    /// pose, so a stowed launcher sits where its mesh was built and a refused write is invisible.
    /// </summary>
    public double RestElevationRad => double.IsFinite(RestElevationDeg)
        ? double.DegreesToRadians(RestElevationDeg)
        : PodReferenceElevationRad;

    /// <summary>NaN follows <see cref="PodReferenceElevationRad"/>; set it to override.</summary>
    public float RestElevationDeg = float.NaN;

    /// <summary>True when something has to be aimed before this launcher can shoot.</summary>
    public bool Trains => TurretMarker is not null || PodsMarker is not null;

    /// <summary>
    /// Rounds carried in total. Zero means one per tube, which is what a missile launcher wants.
    ///
    /// <para>A gun sets this far above <see cref="TubeCount"/>: one or two barrels and a belt of
    /// hundreds. Tubes then cycle rather than empty, and only the reserve runs down.</para>
    /// </summary>
    public int MagazineDepth;

    /// <summary>Seconds between consecutive rounds of a salvo.</summary>
    public float SalvoSpacing = 0.45f;

    /// <summary>
    /// Seconds after it empties before a launcher refills itself. Zero is never, which is the
    /// default: a launcher is refilled only when somebody presses Reload.
    /// </summary>
    public float ReloadSeconds;

    /// <summary>
    /// Eject along the tube rather than slewing onto the target at launch. Only sensible for a
    /// launcher that aims; a fixed one has to slew, and wants <see cref="LaunchLoft"/>.
    /// </summary>
    public bool LaunchAlongTube = true;

    /// <summary>Bias toward the boresight for launchers that cannot aim. See FireGeometry.</summary>
    public float LaunchLoft = 0.35f;

    /// <summary>
    /// A tilt of the launch direction toward the launcher's boresight, as a fraction of the tube axis.
    /// Only along the tube, and only where the boresight is not the tube itself, which is the bomb rack.
    /// </summary>
    public float EjectAwayFromMount;

    /// <summary>
    /// m/s an ejector pushes the round out of the mounting face (+X), in place of the munition's
    /// LaunchSpeed along the tube. Zero leaves the round to slide off its rail.
    /// </summary>
    public float EjectSpeed;

    /// <summary>
    /// Seconds from release to the motor lighting, so an ejected round falls clear of its carrier first.
    /// On the launcher rather than the munition, which is shared with rails that light at once.
    /// </summary>
    public float MotorDelaySeconds;

    /// <summary>
    /// A store's own carriage -- its rail or ejector -- every subpart whose Id holds this. Shown only while
    /// the part is attached to something, so the part list and a part being dragged show the store alone.
    /// </summary>
    public string? CarriageMarker { get; init; }

    /// <summary>The arms an ejector pushes its round off with, every subpart whose Id holds this. Drawn only.</summary>
    public string? EjectorArmMarker { get; init; }

    /// <summary>
    /// How far each arm runs out of the mounting face (+X) on release, m, which is also the stroke the
    /// round is pushed along to reach EjectSpeed. Zero pushes it all at once and moves nothing.
    /// </summary>
    public float EjectorStrokeMetres;

    /// <summary>Seconds for the arms to come back after they have held at full travel.</summary>
    public float EjectorReturnSeconds = 0.8f;

    /// <summary>Standoff used when no launcher part is fitted at all (m).</summary>
    public float MuzzleOffset = 8f;

    /// <summary>
    /// Registry key of the round the cannon fire, or null for a launcher with none.
    ///
    /// <para>A second weapon on the same mount rather than a second launcher: it shares the
    /// platform, the sensor and the aim, and differs in what it throws and how far.</para>
    /// </summary>
    public string? GunMunition { get; init; }

    /// <summary>Each barrel's muzzle in the cannon subpart's own frame, at its modelled pose.</summary>
    public double3[] GunMuzzles { get; init; } = [];

    /// <summary>Rounds in the belt, across all barrels.</summary>
    public int GunAmmo = 480;

    /// <summary>
    /// What one tube round weighs as carried (kg), which the part's mass includes and gives up when
    /// the round leaves. Zero sheds nothing. Not <see cref="MunitionProfile.MassKg"/>, which sets the
    /// round's drag and for a gun is the projectile alone.
    /// </summary>
    public float StoreMassKg;

    /// <summary>
    /// The spin the seated rounds' own motors give them as they leave, or null for none. On the launcher
    /// rather than the munition because the motors are part of the round body this launcher seats: the
    /// MIRV bus seats the same Mk 21 in a body that has none.
    /// </summary>
    public RoundSpin? ReleaseSpin;

    /// <summary>
    /// What the bus's trim nozzles push it at along one axis, fully loaded (m/s²), which sizes how long a hop's
    /// divert burn takes under <see cref="IcbmConfig.HopsSizeTheirOwnSlots"/>. NaN where nobody measured it.
    /// </summary>
    public double TrimAccelerationMetresPerSecond2 = double.NaN;

    /// <summary>
    /// The trim nozzles' exhaust velocity as their dearest axis spends it (m/s): what a metre a second fired that way
    /// costs the tank, which bounds the budget under <see cref="IcbmConfig.HopsSizeTheirOwnSlots"/>. NaN reads no tank.
    /// </summary>
    public double TrimWorstAxisExhaustVelocity = double.NaN;

    /// <summary>
    /// The bus's own main engine, which flies a hop under <see cref="IcbmConfig.HopsBurnTheEngine"/> (N); NaN where the
    /// bus has none.
    /// </summary>
    public double HopEngineThrustNewtons = double.NaN;

    /// <summary>That engine's exhaust velocity (m/s), which prices a hop it flies.</summary>
    public double HopEngineExhaustVelocity = double.NaN;

    /// <summary>
    /// What that engine pushes the bus at fully loaded (m/s²), which sizes a hop's slot before the bus is alone to weigh.
    /// </summary>
    public double HopEngineAccelerationMetresPerSecond2 = double.NaN;

    /// <summary>What one complete round in the belt weighs (kg), case and charge included; see <see cref="StoreMassKg"/>.</summary>
    public float GunRoundMassKg;

    /// <summary>Cyclic rate, rounds per minute across all barrels.</summary>
    public float GunRoundsPerMinute = 2500f;

    /// <summary>Rounds per burst, and the pause between bursts.</summary>
    public int GunBurstRounds = 12;
    public float GunBurstGapSeconds = 0.55f;

    /// <summary>
    /// Seconds after it runs dry before the belt replaces itself. Zero is never, which is the
    /// default, as <see cref="ReloadSeconds"/>.
    /// </summary>
    public float GunReloadSeconds;

    /// <summary>
    /// How far the barrel runs back (m), how long the run takes, and how long counter-recoil takes to
    /// bring it back into battery (s). Drawn only: the round has left before the barrel moves.
    /// </summary>
    public float GunRecoilMetres;
    public float GunRecoilSeconds = 0.08f;
    public float GunReturnSeconds = 0.6f;

    /// <summary>How long the barrel cluster takes to come up to speed and to run down (s).</summary>
    public float GunRotorSpinUpSeconds = 0.2f;
    public float GunRotorSpinDownSeconds = 1.0f;

    /// <summary>
    /// The cannon's sound while firing, by <c>ModLibrary</c> Id. Null plays the shared recording,
    /// retuned toward this gun's rate; a gun naming its own is played as recorded, because a
    /// recording of the right gun has nothing to be retuned toward.
    /// </summary>
    public string? GunSoundId { get; init; }

    /// <summary>
    /// A one-shot for every round fired, by <c>ModLibrary</c> Id. Null for none.
    ///
    /// <para>For a gun slow enough to be heard shot by shot. Past a few hundred rounds a minute the
    /// gunshots fuse into a pitch, and that is what <see cref="GunSoundId"/> carries instead. A gun
    /// with a gunshot and no <see cref="GunSoundId"/> has no loop at all, rather than the shared one.</para>
    /// </summary>
    public string? GunshotSoundId { get; init; }

    /// <summary>The particle emitter at the muzzles while the guns fire, by <c>ModLibrary</c> Id. Null is KSArmory's.</summary>
    public string? MuzzleFlashEffect { get; init; }

    public double GunRoundInterval => GunRoundsPerMinute > 0f ? 60.0 / GunRoundsPerMinute : 0.0;

    public bool HasCannon => GunMunition is not null && GunMuzzles.Length > 0;

    public double SlewRateRad => float.DegreesToRadians(SlewRateDeg);
    public double ElevationRateRad => float.DegreesToRadians(ElevationRateDeg);

    public void ConfigureTurret(Turret turret)
    {
        turret.MinElevationRad = float.DegreesToRadians(MinElevationDeg);
        turret.MaxElevationRad = float.DegreesToRadians(MaxElevationDeg);
        turret.ForwardMinElevationRad = float.DegreesToRadians(ForwardMinElevationDeg);
        turret.ForwardArcRad = float.DegreesToRadians(ForwardArcDeg);
        turret.ForwardPlateauRad = float.DegreesToRadians(ForwardPlateauDeg);
        turret.TraverseLimitRad = float.DegreesToRadians(TraverseLimitDeg);
        turret.RestElevationRad = RestElevationRad;
        turret.SeatElevation(RestElevationRad);
    }
}
