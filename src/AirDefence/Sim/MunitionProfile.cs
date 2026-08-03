namespace AirDefence;

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

    // ---- Boost ----------------------------------------------------------
    /// <summary>Speed the round leaves the rail at, relative to the platform (m/s).</summary>
    public float LaunchSpeed = 60f;

    /// <summary>Seconds of powered flight after launch.</summary>
    public float BoostSeconds = 2.2f;

    /// <summary>Axial acceleration during boost (m/s^2).</summary>
    public float BoostAccel = 260f;

    /// <summary>Round self-destructs this long after launch.</summary>
    public float MaxFlightSeconds = 22f;

    // ---- Guidance -------------------------------------------------------
    /// <summary>Proportional-navigation constant. 3-5 is the classic range.</summary>
    public float NavConstant = 4f;

    /// <summary>Lateral acceleration limit (g). Airframes cap out; ours does too.</summary>
    public float MaxLateralG = 30f;

    /// <summary>Seeker gimbal limit, half-angle off the round's velocity vector (degrees).</summary>
    public float SeekerFovDeg = 55f;

    /// <summary>Fraction of local gravity the autopilot compensates for.</summary>
    public float GravityCompensation = 1f;

    /// <summary>Quadratic drag coefficient, k in a = -k*|v|*v. Zero for vacuum-like flight.</summary>
    public float DragK = 4.0e-5f;

    // ---- Warhead --------------------------------------------------------
    /// <summary>Proximity fuse trigger radius (m).</summary>
    public float FuseRadius = 22f;

    /// <summary>Fuse stays safe for this long after launch, so we never kill the platform.</summary>
    public float FuseArmSeconds = 0.6f;

    /// <summary>Radius inside which a detonation is unconditionally lethal (m).</summary>
    public float LethalRadius = 30f;

    /// <summary>Radius at which blast effect falls to zero (m).</summary>
    public float BlastRadius = 90f;

    public float SeekerFovRad => float.DegreesToRadians(SeekerFovDeg);
    public double MaxLateralAccel => MaxLateralG * 9.80665;
}
