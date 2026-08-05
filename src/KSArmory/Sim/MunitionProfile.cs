namespace KSArmory;

/// <summary>How a round is told where to go.</summary>
public enum GuidanceMode
{
    /// <summary>
    /// The round finds the target itself, within a gimbal limit about its own flight path.
    /// Losing the target inside that cone stops it steering.
    /// </summary>
    Seeker,

    /// <summary>
    /// The launcher tracks the target and uplinks steering commands — the round carries no
    /// seeker. It therefore cannot be blinded by a hard-manoeuvring target, and its gimbal
    /// limit is irrelevant; what breaks the engagement is the *launcher* losing the track.
    /// This is how the 57E6 and most short-range point-defence rounds actually work.
    /// </summary>
    CommandLink,
}

/// <summary>
/// Everything that makes one round behave differently from another: how it burns, how it
/// steers, how far it can see, and what it does when it gets there.
///
/// <para>A second missile type is a second instance of this — no new class, no branch in
/// <see cref="Interceptor"/>. <see cref="Interceptor"/> is the flight model; this is the round
/// flying it.</para>
///
/// <para>Fields rather than properties, and mutable, because the panel edits them live by
/// reference while an engagement is in progress. That is how the tuning sliders work.</para>
/// </summary>
public sealed class MunitionProfile
{
    /// <summary>Registry key. Referenced by <see cref="LauncherProfile.Munition"/>.</summary>
    public required string Name { get; init; }

    /// <summary>Shown in the panel.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Subpart marker for this round's body mesh, matched against the launcher's subpart Ids.
    /// Null means the round has no model and draws as a tracer only.
    /// </summary>
    public string? BodyMarker { get; init; }

    /// <summary>
    /// Subpart marker for this round's fin set, matched the same way as <see cref="BodyMarker"/>.
    /// Null means the round has no separate fins, and nothing is animated.
    /// </summary>
    public string? FinMarker { get; init; }

    // ---- Boost ----------------------------------------------------------
    /// <summary>Speed the round leaves the rail at, relative to the platform (m/s).</summary>
    /// <summary>
    /// Length of the round's body mesh (m). The mesh is modelled about its centre — see
    /// build_missile in tools/model/pantsir.py — so a round placed at a tube mouth sits half
    /// out of it. This is what lets the mod seat it properly instead.
    /// </summary>
    public float BodyLength = 3.10f;

    /// <summary>
    /// Seconds the fins take to snap from stowed to full span after launch.
    ///
    /// <para>A flick, not a hinge easing open.</para>
    /// </summary>
    public float FinDeploySeconds = 0.18f;

    /// <summary>Fin span while stowed, as a fraction of full. Small enough to clear the bore.</summary>
    public float FinStowedScale = 0.06f;

    public float LaunchSpeed = 45f;

    /// <summary>Seconds of powered flight after launch.</summary>
    public float BoostSeconds = 2.4f;

    /// <summary>Axial acceleration during boost (m/s^2).</summary>
    public float BoostAccel = 520f;

    /// <summary>Round self-destructs this long after launch.</summary>
    public float MaxFlightSeconds = 30f;

    // ---- Guidance -------------------------------------------------------
    /// <summary>Proportional-navigation constant. 3-5 is the classic range.</summary>
    public float NavConstant = 4f;

    /// <summary>Lateral acceleration limit (g). Airframes cap out; ours does too.</summary>
    public float MaxLateralG = 35f;

    /// <summary>Seeker gimbal limit, half-angle off the round's velocity vector (degrees).</summary>
    /// <summary>
    /// How the round is steered. <see cref="GuidanceMode.CommandLink"/> ignores
    /// <see cref="SeekerFovDeg"/> entirely.
    /// </summary>
    public GuidanceMode Guidance = GuidanceMode.CommandLink;

    public float SeekerFovDeg = 55f;

    /// <summary>Fraction of local gravity the autopilot compensates for.</summary>
    public float GravityCompensation = 1f;

    /// <summary>
    /// Medium density ratio at which this round is neutrally buoyant, in the same units as
    /// <see cref="DragK"/> is scaled by — multiples of sea-level air. Zero disables buoyancy.
    ///
    /// <para>A torpedo sits near 840, the density of water, so it neither sinks nor rises once
    /// submerged while still falling normally through air. Gravity is scaled by
    /// <c>1 - medium / this</c>, so a round denser than its medium still sinks and a lighter one
    /// rises.</para>
    /// </summary>
    public float NeutralDensityRatio;

    /// <summary>
    /// Quadratic drag coefficient, k in <c>a = -k*|v|*v</c>, <b>at sea level</b>.
    ///
    /// <para>Scaled at runtime by the density where the round is, so one profile is correct on the
    /// pad, climbing out and in orbit. Zero disables drag outright.</para>
    /// </summary>
    public float DragK = 3.0e-5f;

    // ---- Warhead --------------------------------------------------------
    /// <summary>Proximity fuse trigger radius (m).</summary>
    public float FuseRadius = 15f;

    /// <summary>Fuse stays safe for this long after launch, so we never kill the platform.</summary>
    public float FuseArmSeconds = 0.6f;

    /// <summary>
    /// Explosive charge (kg). <b>This is the warhead</b> — the radii below are read off it.
    ///
    /// <para>One figure rather than three, because three independent radii can describe a warhead
    /// whose lethal radius exceeds its blast radius, and because a round's reach is not a free
    /// choice: it follows from what it carries. <see cref="Warhead"/> has the scaling.</para>
    /// </summary>
    public float ChargeKg = 20f;

    /// <summary>Radius inside which a detonation is unconditionally lethal (m).</summary>
    public float LethalRadius => (float)Warhead.LethalRadius(ChargeKg);

    /// <summary>Radius at which blast effect falls to zero (m).</summary>
    public float BlastRadius => (float)Warhead.BlastRadius(ChargeKg);

    /// <summary>Roughly how big the burst should look (m).</summary>
    public float FireballRadius => (float)Warhead.FireballRadius(ChargeKg);

    public float SeekerFovRad => float.DegreesToRadians(SeekerFovDeg);
    public double MaxLateralAccel => MaxLateralG * 9.80665;
}
