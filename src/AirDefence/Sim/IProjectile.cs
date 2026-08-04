using Brutal.Numerics;

namespace AirDefence;

/// <summary>
/// Anything this mod puts in the air and simulates itself: a guided missile, a gun round, a bomb.
///
/// <para><b>Why an interface rather than more fields on <see cref="MunitionProfile"/>.</b> A profile
/// makes one round behave differently from another <em>within one flight model</em> — burn harder,
/// steer harder, fuse wider. It cannot express a different <em>kind</em> of weapon, because
/// <see cref="Interceptor"/>'s loop is integrate → guide → fuse and a gun slug has no guidance
/// stage at all while a beam has no flight. No amount of profile fields reaches those; they are
/// separate implementations.</para>
///
/// <para>The surface here was taken from what the KSA side actually reads off a round, not from
/// what a projectile might plausibly want. Everything on it has a caller.</para>
///
/// <para>Lives in Sim/ and must stay free of KSA types, like everything it describes.</para>
/// </summary>
internal interface IProjectile
{
    /// <summary>Flying, detonated or expired. The battery reaps on anything but flying.</summary>
    RoundState State { get; }

    /// <summary>Which tube it left, numbered from one. Selects its body subpart.</summary>
    int Tube { get; }

    /// <summary>Seconds since launch.</summary>
    double Age { get; }

    // ---- Where it is ----------------------------------------------------

    /// <summary>Absolute position in the ecliptic frame.</summary>
    double3 PositionEcl { get; }

    /// <summary>Absolute velocity in the ecliptic frame. Carries the platform's ~29.8 km/s.</summary>
    double3 VelocityEcl { get; }

    /// <summary>
    /// Position relative to the launch platform, measured after the step against the platform
    /// sample from the same frame. <b>This, not <see cref="PositionEcl"/>, is what gets drawn</b> —
    /// see docs/FRAMES-AND-EPOCHS.md.
    /// </summary>
    double3 OffsetFromPlatform { get; }

    /// <summary>
    /// Displacement since launch. Frame-independent, so it is the only safe thing to hand to
    /// anything anchored to the vehicle's physics origin rather than its orbit position.
    /// </summary>
    double3 TravelSinceLaunch { get; }

    /// <summary>
    /// Velocity relative to the moving frame — the airspeed vector, and the direction the body
    /// points. <b>Never <see cref="VelocityEcl"/>:</b> that carries the planet's orbital motion and
    /// would point every projectile the same way.
    /// </summary>
    double3 VelocityLocal { get; }

    /// <summary>Local speed. Reported, and used to decide whether a heading is meaningful.</summary>
    double Speed { get; }

    /// <summary>Distance flown through the local frame, not around the Sun.</summary>
    double DistanceFlown { get; }

    /// <summary>Recent platform-relative positions for the trail, oldest first.</summary>
    IReadOnlyList<double3> TrailOffsets { get; }

    /// <summary>
    /// Where it left from, in the launcher part's own frame. Set by the battery at launch and
    /// never read by the simulation — it exists so the body can be anchored to its tube.
    /// </summary>
    double3 LaunchAnchorPartFrame { get; set; }

    // ---- What it is chasing ---------------------------------------------

    /// <summary>Opaque handle to the target, compared by reference. Null once lock is lost.</summary>
    object? TargetRef { get; }

    /// <summary>
    /// Whether it is still being steered. Always false for something unguided, which is a real
    /// answer rather than a missing one — the panel shows it and a gun round genuinely has no lock.
    /// </summary>
    bool HasLock { get; }

    /// <summary>Whether guidance can currently see or be commanded onto the target.</summary>
    bool SeekerInView { get; }

    // ---- How it ended ----------------------------------------------------

    /// <summary>
    /// Range at the fuse trigger. <b>Not a miss distance</b> — it is bounded by the fuse radius
    /// whatever the projectile did. The honest number is measured by the caller.
    /// </summary>
    double MissDistance { get; }

    /// <summary>Closest it ever got to its target, whatever its fate.</summary>
    double ClosestApproach { get; }

    /// <summary>
    /// When it detonated, relative to the world sample the update was given. Negative, between
    /// <c>-dt</c> and zero, because samples arrive at the end of the step.
    /// </summary>
    double DetonationElapsedInFrame { get; }

    // ---- Behaviour -------------------------------------------------------

    /// <summary>
    /// How far this round's fins have deployed, 0 to 1. Returns 1 for anything with no fins to
    /// animate, so the caller needs no special case.
    /// </summary>
    double FinDeployment(MunitionProfile munition);

    /// <summary>
    /// Advances by <paramref name="dt"/> simulated seconds.
    /// </summary>
    /// <param name="target">
    /// Sampled at the <em>end</em> of this step, the way KSA hands vehicle state over. An
    /// implementation that aims must back-date it; one that does not may ignore it.
    /// </param>
    /// <param name="airDensityRatio">
    /// Air density as a fraction of sea level, so drag coefficients tuned on the pad stay correct
    /// in orbit.
    /// </param>
    void Update(double dt, TargetState? target, double3 gravity, double3 frameVelocityEcl,
                double3 platformEcl, MunitionProfile munition, double airDensityRatio = 1.0);
}
