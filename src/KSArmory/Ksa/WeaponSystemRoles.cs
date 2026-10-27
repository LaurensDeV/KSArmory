using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The craft a weapons system is bolted to, and where it was when the system last sampled the world.
/// One instant: paired with a sample from another frame it is ~500 m of ecliptic motion
/// (<c>docs/FRAMES-AND-EPOCHS.md</c>).
/// </summary>
internal interface IWeaponPlatform
{
    Vehicle? Platform { get; }

    double3 PlatformEcl { get; }
}

/// <summary>
/// Paired by <see cref="Catalogue.LoadoutFor"/> and owned by the installation, so a reader cannot
/// pick up whichever system resolved last.
/// </summary>
internal interface IWeaponLoadout
{
    LauncherProfile Profile { get; }

    MunitionProfile Munition { get; }

    SensorProfile Sensor { get; }
}

/// <summary>
/// Each round carries its own munition, so a consumer never asks what kind of launcher threw it.
/// </summary>
internal interface IRoundsInFlight : IWeaponPlatform
{
    IReadOnlyList<IProjectile> Rounds { get; }
}

/// <summary>
/// What an effect anchored to a weapon needs, narrowed so the emitter release contract stays visible.
/// </summary>
internal interface IEffectSource : IRoundsInFlight
{
    Part? Launcher { get; }

    /// <summary>
    /// A celestial, not the craft: every emitter sets <c>Context.Astronomical</c> and leaves
    /// <c>Context.Vehicle</c> null, so a round keeps its plume after its launcher dies.
    /// </summary>
    Celestial? EffectBody { get; }

    /// <summary>
    /// The drawn body while there is one, so a flame sits on the mesh; once the launcher is gone, the
    /// round's own position against its anchor, which is exact with no part to disagree with.
    /// </summary>
    bool TryRoundEffectEcl(IProjectile round, out double3 ecl);

    /// <summary>
    /// Which way a round's body was last drawn, mesh to Ecl -- what a nozzle on the body is placed through.
    /// False before its body has been drawn once.
    /// </summary>
    bool TryRoundAttitudeEcl(IProjectile round, out doubleQuat attitudeEcl);

    LauncherProfile Profile { get; }

    bool PlumesEnabled { get; }

    /// <summary>
    /// Where the cannon's flashes belong, one per barrel cluster. A mount with a sponson either
    /// side has two, and averaging them would put one flash on the hull between them.
    /// </summary>
    int GunFlashPointsEcl(Span<double3> into);

    bool HasGunFlash();

    /// <summary>What holds a flash and a sound open.</summary>
    bool GunsFiring { get; }

    int GunShotsFired { get; }

    /// <summary>Simulated seconds; infinite before the first round.</summary>
    double GunSecondsSinceShot { get; }

    /// <summary>
    /// Whether this shell is drawn as a body, or will be once the next sync lends it the free one —
    /// so what marks a shell that has no body can stand aside for one that does.
    /// </summary>
    bool ShellDrawnAsBody(IProjectile round);
}

/// <summary>
/// The optical head, the contact it brackets, and where it is looking: enough to paint a sight
/// over the camera the head drives and to point that camera, with no way to move the head itself.
///
/// <para>Carries <see cref="IWeaponPlatform"/>: <c>FixedController</c> places a camera at
/// <c>following.GetPositionEcl() + CameraOffset</c> in its own pass, so the offset must be a pure
/// separation from the followed craft.</para>
/// </summary>
internal interface IOpticalHead : IWeaponPlatform
{
    Part? OpticPart { get; }

    bool OpticOnTarget { get; }

    /// <summary>Aimed at what the weapon on its part cued it with.</summary>
    bool FollowsCue { get; }

    /// <summary>True while its own craft stands across its line of sight.</summary>
    bool Masked { get; }

    bool AtGimbalLimit { get; }

    bool HasLaser { get; }

    /// <summary>Its laser is wanted and held off by <see cref="Masked"/>.</summary>
    bool LaserInhibited { get; }

    LaserSpot? Spot { get; }

    Track? LockedTrack { get; }

    /// <summary>
    /// Read-only on purpose: designating is a command, and the sight and chase camera only read.
    /// </summary>
    Aimpoint Designation { get; }

    /// <summary>
    /// Local "up", which is what the sight's horizontal reference is drawn against. Always the
    /// site's true vertical — a horizon measured against anything else is not a horizon.
    /// </summary>
    double3 Boresight { get; }

    /// <summary>
    /// Not the same question as <see cref="Boresight"/>: rigid with the head unless the operator
    /// asks for a levelled picture.
    /// </summary>
    double3 RollReferenceEcl { get; }

    /// <summary>
    /// False when the launcher, head or pose cannot be resolved; the caller then drives nothing
    /// rather than pointing a camera at the origin.
    /// </summary>
    bool TryOpticViewEcl(out double3 eyeEcl, out double3 forwardEcl);

    /// <inheritdoc cref="OpticalHead.TryOpticViewEclAt"/>
    bool TryOpticViewEclAt(double3 platformEcl, out double3 eyeEcl, out double3 forwardEcl, int viewIndex = -1);
}

/// <summary>
/// What a weapon beside a director contributes to the sight it paints; optional in
/// <c>Sight.Draw</c>. No commands, so painting it every frame from the draw hook is safe.
/// </summary>
internal interface ISightPicture : IWeaponPlatform, IWeaponLoadout
{
    int Ammo { get; }

    int GunAmmo { get; }

    int BeamSecondsLeft { get; }

    bool BeamLasing { get; }

    /// <summary>What every lead is measured from.</summary>
    double3 MountEcl { get; }

    bool IsLaid { get; }

    /// <summary>Apart from <see cref="IsLaid"/>: the cannon share only the traverse with the tubes.</summary>
    bool GunsAreLaid { get; }

    /// <inheritdoc cref="WeaponSystem.TryRingAimEcl"/>
    bool TryRingAimEcl(out double3 aimEcl, out bool isGunLead);

    /// <summary>Zero if the lead did not solve.</summary>
    double GunFlightSeconds { get; }
}

/// <summary>
/// A weapon an operator aims at a place by hand. It answers whether the shot would be taken
/// before a round is spent, and then takes it.
/// </summary>
internal interface IManualFire : IWeaponPlatform, IWeaponLoadout
{
    int Ammo { get; }

    bool IsLaid { get; }

    bool FireAt(double3 pointEcl);

    /// <summary>
    /// Flies the rounds as another profile of the same round from here on, which is what a ballistic computer's
    /// arm changes one rocket's warheads with. Refused for a different round.
    /// </summary>
    void FlyRoundsAs(MunitionProfile munition);

    /// <summary>
    /// The state a released round would leave with, averaged over the tubes: a tube mouth metres
    /// from the orbit state, thrown along the tube. Measured on a coasting bus: ten degrees, ten
    /// metres and 0.04 m/s off the craft's own state. Per-tube differences are <see cref="ReleaseFocus"/>'s.
    /// </summary>
    /// <param name="spinSpeed">
    /// Not in <paramref name="velocityEcl"/>: a transient the guidance chases rather than converges
    /// on, a cutoff residual of 4.31 m/s against 0.15. It decides whether to release, not where.
    /// </param>
    bool TryMeanReleaseStateEcl(out double3 positionEcl, out double3 velocityEcl, out double spinSpeed);

    /// <summary>
    /// One tube's mouth from the mean <see cref="TryMeanReleaseStateEcl"/> predicts from. Turned off
    /// the part frame, not differenced from world positions: 30 m per millisecond of mismatch.
    /// </summary>
    /// <param name="tube">Counted from zero, in the order the profile lists them.</param>
    bool TryTubeOffsetFromMeanEcl(int tube, out double3 offsetEcl);

    /// <summary>The ring's size, not how many are loaded; from the same launcher as the offsets.</summary>
    int TubeCount { get; }

    /// <summary>
    /// A tube mouth's velocity from the craft's turning, as KSA gives a part it splits off. No position
    /// enters it. What a round was actually thrown with is <see cref="Slug.SpinVelocityEcl"/>.
    /// </summary>
    /// <param name="tube">Counted from zero, in the order the profile lists them.</param>
    /// <param name="armEcl">The mouth from the centre of mass; a couple of metres on a bus.</param>
    bool TryTubeSpinEcl(int tube, out double3 spinEcl, out double3 armEcl, out double3 angularVelocityEcl);

    /// <summary>Whether a decoupler holds this launcher on: a property of the part, known before the shot.</summary>
    bool CanSeparate { get; }

    /// <summary>Unlike <see cref="CanSeparate"/>, asks whether the next queued stage is the one.</summary>
    bool NextStageSeparatesIt { get; }

    /// <summary>How many stages still fire after the one that lets the launcher off; zero when it is last.</summary>
    int StagesAfterTheRelease { get; }

    /// <summary>Move the launcher's release to the end of the staging list. Only from <c>AttitudeHook</c>'s window.</summary>
    bool MoveReleaseLast(out string why);

    /// <summary>False with no joint. The split lands on the following frame and cannot be undone.</summary>
    bool Separate();

    /// <summary>Which tube fires next, or -1 when nothing more will be handed out.</summary>
    int NextTube { get; }

    /// <summary>How many rounds could still go, which sets each one's share of the release window.</summary>
    int TubesReadyToFire { get; }

    /// <summary>
    /// All or nothing: a partial set averages to a direction the aim correction did not assume.
    /// </summary>
    int TubeAxesEcl(Span<double3> into);

    bool CanGuideOnto(double3 pointEcl);

    bool FireBurst(int rounds = 0);

    /// <summary>Which armament the manual trigger fires, and so which one a click on the world does.</summary>
    ArmamentKind TriggerArmament { get; }

    /// <summary>The round that armament throws, whose reach a designation is judged against.</summary>
    MunitionProfile TriggerMunition { get; }

    /// <summary>Of whichever weapon it carries: a gun-only system's magazine reads zero forever.</summary>
    bool ReadyToFire { get; }
}

/// <summary>
/// A weapons system as the overlay and the diagnostic dump read it, with none of the commands.
/// </summary>
internal interface IWeaponSystemView : IRoundsInFlight, IWeaponLoadout
{
    Part? Launcher { get; }

    /// <summary>The missile pods, which the tube offsets are measured from.</summary>
    Part? PodsPart { get; }

    /// <summary>Where rounds actually leave from: the launcher part, or the hull without one.</summary>
    double3 MountEcl { get; }

    double3 Boresight { get; }

    /// <summary>
    /// The launch a release makes, ejector and spin included. False with no next release, including
    /// every tube still holding the round it fired.
    /// </summary>
    bool TryNextReleaseEcl(out double3 positionEcl, out double3 velocityEcl);

    int Ammo { get; }

    bool TubesResolved { get; }

    bool IsOperational { get; }

    Turret Turret { get; }

    /// <summary>Cosmetic, like the array itself.</summary>
    double RadarSpinRad { get; }

    Radar Radar { get; }

    /// <summary>False once the engine has refused to place a round body; tracers then stand in.</summary>
    bool RoundBodiesWork { get; }

    /// <summary>Zero means tracers only.</summary>
    int RoundBodyCount { get; }
}
