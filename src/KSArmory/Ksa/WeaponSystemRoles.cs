using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The craft a weapons system is bolted to, and where that craft was when the system last
/// sampled the world.
///
/// <para>The pair travels together because it is one instant: everything a system measures is
/// differenced against <see cref="PlatformEcl"/> and drawn against <see cref="Platform"/>, and
/// pairing one of them with a sample from another frame is ~500 m of ecliptic motion. See
/// <c>docs/FRAMES-AND-EPOCHS.md</c>.</para>
/// </summary>
internal interface IWeaponPlatform
{
    Vehicle? Platform { get; }

    double3 PlatformEcl { get; }
}

/// <summary>
/// Which weapon system this is: the launcher, the round it throws and the set it sees with.
///
/// <para>The three are paired by <see cref="Arsenal.LoadoutFor"/> and belong to the installation
/// running them, so a reader handed this cannot pick up whichever system resolved last.</para>
/// </summary>
internal interface IWeaponLoadout
{
    LauncherProfile Profile { get; }

    MunitionProfile Munition { get; }

    SensorProfile Sensor { get; }
}

/// <summary>
/// What a weapons system has in the air. Motor sound, plumes and the chase camera need this and
/// nothing else, and each round carries its own munition, so none of them has to ask what kind of
/// launcher threw it.
/// </summary>
internal interface IRoundsInFlight : IWeaponPlatform
{
    IReadOnlyList<IProjectile> Rounds { get; }
}

/// <summary>
/// What an effect anchored to a weapon needs: the rounds, the platform they are measured from,
/// the launcher part they are placed against, and whether effects are wanted at all.
///
/// <para>The plume, the tracers and the muzzle flash between them read five members of a class
/// with fifty-two. Narrowing the surface to those five is what keeps the emitter release contract
/// visible to whoever writes the next effect.</para>
/// </summary>
internal interface IEffectSource : IRoundsInFlight
{
    /// <summary>The launcher part, which everything drawn on the weapon is placed against.</summary>
    Part? Launcher { get; }

    /// <summary>The profile, for the rates and geometry an effect is sized from.</summary>
    LauncherProfile Profile { get; }

    /// <summary>Whether the player wants effects at all.</summary>
    bool PlumesEnabled { get; }

    /// <summary>Where the cannon's flash belongs, if it has cannon.</summary>
    bool TryGunFlashEcl(out double3 ecl, out double3 axisEcl);

    /// <summary>True while the cannon are firing, which is what holds a flash and a sound open.</summary>
    bool GunsFiring { get; }
}

/// <summary>
/// The optical head, the contact it brackets, and where it is looking: enough to paint a sight
/// over the camera the head drives and to point that camera, with no way to move the head itself.
///
/// <para>Carries <see cref="IWeaponPlatform"/> because a camera on the head has to be measured
/// from the craft the head is bolted to. KSA's <c>FixedController</c> places a camera at
/// <c>following.GetPositionEcl() + CameraOffset</c> during its own frame pass, so the offset must
/// be a pure separation from the followed craft and never a position sampled here.</para>
/// </summary>
internal interface IOpticalHead : IWeaponPlatform
{
    Part? OpticPart { get; }

    /// <summary>True once the head has caught up with what it was told to look at.</summary>
    bool OpticOnTarget { get; }

    /// <summary>The contact the sensor is holding, or null.</summary>
    Track? LockedTrack { get; }

    /// <summary>Local "up" at the launcher, which is what keeps a sight's horizon level.</summary>
    double3 Boresight { get; }

    /// <summary>
    /// Where the head is looking from and along what, both in Ecl. False when the launcher, the
    /// head or the pose cannot be resolved — the caller then draws and drives nothing rather than
    /// pointing a camera at the origin.
    /// </summary>
    bool TryOpticViewEcl(out double3 eyeEcl, out double3 forwardEcl);
}

/// <summary>
/// A weapon an operator aims at a place by hand. It answers whether the shot would be taken
/// before a round is spent, and then takes it.
/// </summary>
internal interface IManualFire : IWeaponPlatform, IWeaponLoadout
{
    int Ammo { get; }

    /// <summary>True when the launcher is pointing where it is about to shoot.</summary>
    bool IsLaid { get; }

    bool FireAt(double3 pointEcl);

    bool CanGuideOnto(double3 pointEcl);

    /// <summary>Opens a cannon burst along wherever the mount is laid.</summary>
    bool FireBurst();

    /// <summary>
    /// Whether a manual shot would be taken now, asked of whichever weapon this system carries.
    /// A gun-only system reads zero from the magazine forever, so the magazine cannot answer it.
    /// </summary>
    bool ReadyToFire { get; }
}

/// <summary>
/// A weapons system as a reader sees it: what the overlay draws and what the diagnostic dump
/// prints.
///
/// <para>None of the commands are here, so neither of those can fire, reload or re-platform the
/// system it is describing. That is the whole reason this is not the class.</para>
/// </summary>
internal interface IWeaponSystemView : IRoundsInFlight, IWeaponLoadout
{
    /// <summary>The launcher part on the platform, or null if none is fitted.</summary>
    Part? Launcher { get; }

    /// <summary>The missile pods, which the tube offsets are measured from.</summary>
    Part? PodsPart { get; }

    /// <summary>Where rounds actually leave from: the launcher part, or the hull without one.</summary>
    double3 MountEcl { get; }

    /// <summary>Current radar boresight in Ecl.</summary>
    double3 Boresight { get; }

    int Ammo { get; }

    /// <summary>True when the tubes are where the profile says they are.</summary>
    bool TubesResolved { get; }

    /// <summary>True when the system has everything it needs to shoot.</summary>
    bool IsOperational { get; }

    Turret Turret { get; }

    Radar Radar { get; }

    /// <summary>False once the engine has refused to place a round body; tracers then stand in.</summary>
    bool RoundBodiesWork { get; }

    /// <summary>How many round bodies the launcher carries. Zero means tracers only.</summary>
    int RoundBodyCount { get; }
}
