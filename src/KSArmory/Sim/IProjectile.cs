using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Anything this mod puts in the air and simulates itself: a guided missile, a gun round, a bomb.
///
/// <para>A <see cref="MunitionProfile"/> varies one round within a single flight model — burn
/// harder, steer harder, fuse wider. It cannot express a different <em>kind</em> of weapon:
/// <see cref="Interceptor"/>'s loop is integrate → guide → fuse, and a slug has no guidance stage
/// while a beam has no flight. Those are separate implementations of this.</para>
///
/// <para>Every member here has a caller on the KSA side. Must stay free of KSA types.</para>
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
    /// sample from the same frame. This, not <see cref="PositionEcl"/>, is what gets drawn.
    /// See docs/FRAMES-AND-EPOCHS.md.
    /// </summary>
    double3 OffsetFromPlatform { get; }

    /// <summary>
    /// Displacement since launch. Frame-independent, so it is the only safe thing to hand to
    /// anything anchored to the vehicle's physics origin rather than its orbit position.
    /// </summary>
    double3 TravelSinceLaunch { get; }

    /// <summary>
    /// Velocity relative to the moving frame — the airspeed vector, and the direction the body
    /// points. Never <see cref="VelocityEcl"/>, which carries the planet's orbital motion and
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

    /// <summary>
    /// Which way it left, in Ecl. Set at launch and never updated afterwards.
    ///
    /// <para>A body released in vacuum keeps the attitude it was let go with. Reading the
    /// launcher's <em>current</em> tube axis instead ties a round that has already gone to the
    /// craft that dropped it, so rolling the launcher rolls the rounds in flight with it.</para>
    /// </summary>
    double3 ReleaseHeadingEcl { get; set; }

    /// <summary>
    /// The platform's attitude when this round left, as Asmb2Ecl. Set at launch, never updated.
    ///
    /// <para>The launch anchor is a point in the world written down in the launcher's frame, so it
    /// has to be carried back through however far the craft has turned since. Without this a
    /// spinning launcher swings every round already in flight around with it.</para>
    /// </summary>
    doubleQuat LaunchAttitude { get; set; }

    /// <summary>
    /// Which round this is. A launcher flying more than one weapon steps and fuses each by its
    /// own numbers, so the projectile carries them rather than the battery holding one set.
    /// </summary>
    MunitionProfile Munition { get; init; }

    // ---- What it is chasing ---------------------------------------------

    /// <summary>Opaque handle to the target, compared by reference. Null once lock is lost.</summary>
    object? TargetRef { get; }

    /// <summary>
    /// What this round is shooting at — a craft, a component, or a fixed position. The kinematics
    /// arrive per frame through <see cref="Update"/>; this is the identity.
    /// </summary>
    Aimpoint Aimpoint { get; set; }

    /// <summary>Whether it is still being steered. Always false for something unguided.</summary>
    bool HasLock { get; }

    /// <summary>Whether guidance can currently see or be commanded onto the target.</summary>
    bool SeekerInView { get; }

    // ---- How it ended ----------------------------------------------------

    /// <summary>
    /// Range at the fuse trigger. Not a miss distance: it is bounded by the fuse radius whatever
    /// the projectile did.
    /// </summary>
    double MissDistance { get; }

    /// <summary>Closest it ever got to its target, whatever its fate.</summary>
    double ClosestApproach { get; }

    /// <summary>
    /// When it detonated, relative to the world sample the update was given. Negative, between
    /// <c>-dt</c> and zero, because samples arrive at the end of the step.
    /// </summary>
    double DetonationElapsedInFrame { get; }

    /// <summary>
    /// The body this round physically met, or null if it met nothing.
    ///
    /// <para>Null is the answer for a proximity-fused warhead, which is not required to touch
    /// anything: what it kills is settled by its miss distance. A round that names a body has
    /// struck that one, whatever fire control was aiming at.</para>
    /// </summary>
    object? StruckBody { get; }

    // ---- Behaviour -------------------------------------------------------

    /// <summary>
    /// Ends this round because something else destroyed it in the air.
    ///
    /// <para>The only state change a projectile does not decide for itself, and the reason it is
    /// on the interface rather than beside the fuse: what shoots a round down is another weapon,
    /// which reaches it through this and nothing else. A round already finished ignores it, so a
    /// shell and a warhead arriving in the same frame cannot end it twice.</para>
    ///
    /// <para>Sets no miss distance and fires no warhead. Its own launcher reaps it and says so.</para>
    /// </summary>
    void ShootDown();

    /// <summary>
    /// How far this round's fins have deployed, 0 to 1. Returns 1 for anything with no fins to
    /// animate, so the caller needs no special case.
    /// </summary>
    double FinDeployment(MunitionProfile munition);

    /// <summary>
    /// The lateral acceleration the round is currently commanding, in the ecliptic frame. Zero for
    /// anything that does not steer, and for anything that has lost what it was steering at.
    ///
    /// <para>Presentation only: it is what the blades are drawn deflected by, through
    /// <see cref="FinMixer"/>. Nothing reads it back, so the flight model is unaffected by whether
    /// a round has fins drawn at all.</para>
    /// </summary>
    double3 SteeringCommandEcl { get; }

    /// <summary>
    /// Moves every stored offset onto a different anchor, without moving the round.
    /// </summary>
    /// <param name="offsetDelta">
    /// The old anchor's position minus the new one's, both sampled at the same instant. Adding it
    /// to an offset measured from the old anchor gives the offset from the new one.
    /// </param>
    /// <remarks>
    /// <para>Needed because the offsets are the drawn quantity and there are several of them: the
    /// current one, the launch one that <see cref="TravelSinceLaunch"/> differences against, and
    /// every point of the trail. <see cref="OffsetFromPlatform"/> alone self-corrects on the next
    /// step, so a partial re-anchor looks right and leaves the trail drawn to wherever the old
    /// anchor was — the width of a planet, when a round outlives the craft that fired it and takes
    /// the body it is orbiting as its anchor instead.</para>
    ///
    /// <para>Shifting the launch offset by the same delta is what keeps
    /// <see cref="TravelSinceLaunch"/> invariant, which its own contract requires: it is a
    /// difference of two offsets against one anchor, so it must not notice the anchor changing.
    /// </para>
    /// </remarks>
    void Reanchor(double3 offsetDelta);

    /// <summary>
    /// Advances by <paramref name="dt"/> simulated seconds.
    /// </summary>
    /// <param name="target">
    /// Sampled at the <em>end</em> of this step, the way KSA hands vehicle state over. An
    /// implementation that aims must back-date it.
    /// </param>
    /// <param name="mediumDensityRatio">
    /// Density of whatever the round is flying through, as a fraction of sea-level air. Water is
    /// ~840, vacuum 0 — the flight model never asks which it is.
    /// </param>
    void Update(double dt, TargetState? target, double3 gravity, double3 frameVelocityEcl,
                double3 platformEcl, MunitionProfile munition, double mediumDensityRatio = 1.0);
}
