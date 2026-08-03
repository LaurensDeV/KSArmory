using Brutal.Numerics;

namespace AirDefence;

internal enum RoundState
{
    Flying,
    Detonated,
    Expired,
}

/// <summary>Sampled target state for one frame, in the ecliptic frame.</summary>
internal readonly record struct TargetState(double3 PositionEcl, double3 VelocityEcl, double Radius);

/// <summary>
/// A single anti-air round.
///
/// The round is integrated by this mod rather than handed to KSA's vehicle physics: it exists
/// for a few seconds, needs sub-frame accuracy at closing speeds where one frame is hundreds
/// of metres, and must not perturb the save. Its state lives entirely in the ecliptic frame.
///
/// Deliberately free of KSA types so the guidance and fuse can be exercised headlessly -
/// see tests/AirDefence.Tests. The caller samples the target once per frame and passes it in;
/// the round extrapolates that state across its own sub-steps.
/// </summary>
internal sealed class Interceptor
{
    private const int TrailCapacity = 32;
    private const double TrailIntervalSeconds = 0.05;

    /// <summary>Fixed integration step. Frames are subdivided to this, which keeps the
    /// guidance stable and stops fast targets tunnelling through the fuse radius.</summary>
    private const double SubStep = 0.005;

    /// <summary>Most sub-steps one <see cref="Advance"/> will run, however long the frame.</summary>
    private const int MaxSubSteps = 64;

    /// <summary>
    /// Longest frame this can integrate without coarsening. Beyond it the sub-step clamp starts
    /// stretching each step, and a round doing 700 m/s begins skipping past its own fuse radius
    /// — so <see cref="SimClock"/> refuses to step at all rather than let that happen quietly.
    /// </summary>
    public const double MaxFaithfulStep = SubStep * MaxSubSteps;

    public double3 PositionEcl;
    public double3 VelocityEcl;

    /// <summary>Seconds since launch.</summary>
    public double Age { get; private set; }

    /// <summary>
    /// Opaque handle to whatever the round is chasing (a KSA Vehicle in the game).
    /// Compared by reference only.
    ///
    /// <para>This is the round's *assignment*, and it outlives the seeker: it is cleared only
    /// when the target is gone entirely. Whether the round can currently steer towards it is
    /// <see cref="SeekerInView"/>. Conflating the two meant losing the seeker also switched off
    /// the proximity fuse and the closest-approach tracking.</para>
    /// </summary>
    public object? TargetRef { get; private set; }

    public RoundState State { get; private set; } = RoundState.Flying;

    /// <summary>Miss distance recorded at detonation (m). Meaningful once detonated.</summary>
    public double MissDistance { get; private set; }

    /// <summary>
    /// How far into the current frame the round detonated (s). Positions elsewhere in the world
    /// are sampled at the frame start, so anything comparing against <see cref="PositionEcl"/>
    /// must advance them by this much first — in the ecliptic frame that gap is thousands of
    /// metres, enough for a blast to find nothing at all.
    /// </summary>
    public double DetonationElapsedInFrame { get; private set; }

    /// <summary>
    /// Closest the round ever got to its target (m), whatever its fate. A round that expires
    /// with this at a few hundred metres was guided but under-ranged; one that expires with it
    /// still in kilometres never converged at all.
    /// </summary>
    public double ClosestApproach { get; private set; } = double.MaxValue;

    /// <summary>
    /// Distance flown through the local frame (m). Measured against the frame rather than
    /// absolutely — otherwise it just reports the planet's motion around its star, which for
    /// Earth is ~30 km per second of flight and tells you nothing about the round.
    /// </summary>
    public double DistanceFlown { get; private set; }

    /// <summary>Frame velocity from the last update, so telemetry can be reported locally.</summary>
    private double3 _frameVelocityEcl;

    /// <summary>Tube number, for display.</summary>
    public int Tube { get; init; }

    /// <summary>
    /// Position relative to the launch platform, at the end of the last update.
    ///
    /// Absolute <see cref="PositionEcl"/> is useless for drawing: the round is integrated to the
    /// end of the frame while everything it is drawn against was sampled at the start, and in
    /// the ecliptic frame that gap is ~500 m at 60 fps. Expressing the round against the
    /// platform — advanced to the same instant — cancels the frame's motion and leaves only
    /// real, local displacement.
    /// </summary>
    public double3 OffsetFromPlatform { get; private set; }

    /// <summary>
    /// <see cref="OffsetFromPlatform"/> at the moment of launch, so displacement *since* launch
    /// can be taken as a difference.
    ///
    /// That difference is a pure delta between two Ecl positions, which makes it the only safe
    /// thing to hand to anything anchored to the vehicle's physics origin: the platform position
    /// these offsets are measured from is the analytic orbit position, and the two disagree by
    /// enough to matter. See DrawAnchor.
    /// </summary>
    public double3 LaunchOffset { get; private set; }

    /// <summary>
    /// Displacement since launch, through the local frame. Frame-independent, so safe to rotate
    /// into any frame.
    ///
    /// <para><b>Accumulated, not differenced.</b> It used to be
    /// <c>OffsetFromPlatform - LaunchOffset</c>, two positions subtracted a frame apart, and
    /// every variation of that carried a term multiplied by dt. Since the velocities involved
    /// include the platform's ~29.8 km/s of ecliptic motion, a frame time wobbling by under a
    /// millisecond moved the drawn round by tens of metres, and a whole step of it displaced
    /// rounds ~500 m from the launcher. Three arrangements of that subtraction were tried in
    /// game; two zigzagged and one was displaced.</para>
    ///
    /// <para>Integrating the round's velocity <em>relative to the local frame</em> has no such
    /// term. It is a sum of bounded local motion — a few metres per frame at a few hundred m/s —
    /// so it is smooth by construction, starts at exactly zero, and does not depend on which
    /// instant the platform position was sampled at. That last property is the point: the
    /// question that caused all of this no longer has to be answered.</para>
    /// </summary>
    public double3 TravelSinceLaunch { get; private set; }

    /// <summary>
    /// Where this round left from, in the launcher part's own frame. Set by the battery at
    /// launch and never read by the simulation — it exists so the round's *body* can be placed
    /// against the tube it came out of rather than against the platform's orbit position.
    /// </summary>
    public double3 LaunchAnchorPartFrame { get; set; }

    /// <summary>
    /// Recent positions for the smoke trail, oldest first, as platform-relative offsets.
    /// Stored this way for the same reason: absolute points recorded across 1.6 s of trail
    /// would be smeared over ~48 km of the planet's motion around its star.
    /// </summary>
    public readonly List<double3> TrailOffsets = new(TrailCapacity);

    private double _trailTimer;

    public Interceptor(double3 positionEcl, double3 velocityEcl, object target, int tube, double3 platformEcl)
    {
        PositionEcl = positionEcl;
        VelocityEcl = velocityEcl;
        TargetRef = target;
        Tube = tube;
        OffsetFromPlatform = positionEcl - platformEcl;
        LaunchOffset = OffsetFromPlatform;
        TrailOffsets.Add(OffsetFromPlatform);
    }

    /// <summary>
    /// True while the seeker can see the target — inside the gimbal cone about the flight path.
    /// Recomputed every sub-step, so it can come back after being lost.
    /// </summary>
    public bool SeekerInView { get; private set; } = true;

    /// <summary>
    /// True when the round is both assigned a target and steering towards it. Losing the seeker
    /// stops the steering, not the warhead — see the fuse in <c>Step</c>.
    /// </summary>
    public bool HasLock => TargetRef is not null && SeekerInView;

    /// <summary>
    /// Velocity relative to the moving frame — the round's airspeed vector, and the direction
    /// it is actually pointing.
    ///
    /// Not <see cref="VelocityEcl"/>: that carries the platform's ~29.8 km/s of ecliptic
    /// motion, so using it to orient anything points every round the same way regardless of
    /// where it is going. That mistake has been made in this codebase before.
    /// </summary>
    public double3 VelocityLocal => VelocityEcl - _frameVelocityEcl;

    public double Speed => Vec.Len(VelocityLocal);

    /// <summary>
    /// Advances the round by <paramref name="dt"/> seconds, subdividing internally.
    /// </summary>
    /// <param name="target">
    /// Target state sampled at the start of this frame, or null if the target is gone.
    /// Extrapolated linearly across sub-steps.
    /// </param>
    /// <param name="gravity">Gravitational acceleration at the round, in Ecl (m/s^2).</param>
    /// <param name="frameVelocityEcl">
    /// Velocity of the local frame the round flies through — in practice the launch platform's,
    /// which carries the parent body's orbital and rotational motion. Ecliptic velocities are
    /// absolute: near Earth they are dominated by ~29.8 km/s of solar orbit, which is not
    /// airspeed and not a heading. Subtracting this is what makes drag, the boost axis, the
    /// seeker cone and the guidance frame refer to the world the round is actually flying in.
    /// Pass zero for a frame that is already local.
    /// </param>
    /// <param name="platformEcl">
    /// The platform's position at the start of this update, i.e. the same instant every other
    /// world position was sampled. Used only to express the round's offset for drawing.
    /// </param>
    public void Update(
        double dt, TargetState? target, double3 gravity,
        double3 frameVelocityEcl, double3 platformEcl, MunitionProfile munition)
    {
        if (State != RoundState.Flying) return;

        if (target is null) TargetRef = null;

        _frameVelocityEcl = frameVelocityEcl;

        int steps = Math.Clamp((int)Math.Ceiling(dt / SubStep), 1, MaxSubSteps);
        double h = dt / steps;
        double elapsed = 0.0;

        for (int i = 0; i < steps && State == RoundState.Flying; i++)
        {
            Step(h, elapsed, target, gravity, frameVelocityEcl, munition);
            elapsed += h;
        }

        // Where the round sits relative to the platform: where it left from, plus how far it has
        // flown through the local frame since. Neither term is a cross-frame subtraction of
        // ecliptic positions, so neither can carry a frame of the planet's motion.
        OffsetFromPlatform = LaunchOffset + TravelSinceLaunch;

        _trailTimer += dt;
        if (_trailTimer >= TrailIntervalSeconds)
        {
            _trailTimer = 0.0;
            TrailOffsets.Add(OffsetFromPlatform);
            if (TrailOffsets.Count > TrailCapacity) TrailOffsets.RemoveAt(0);
        }
    }

    private void Step(
        double h, double elapsedInFrame, TargetState? target,
        double3 gravity, double3 frameVelocityEcl, MunitionProfile munition)
    {
        Age += h;

        if (Age > munition.MaxFlightSeconds)
        {
            State = RoundState.Expired;
            return;
        }

        // The round's motion through the local world. Everything about how the airframe
        // behaves - which way it points, what the air does to it - is about this, not about
        // the absolute ecliptic velocity it inherited from the planet.
        double3 localVelocity = VelocityEcl - frameVelocityEcl;

        double3 accel = gravity;

        // Boost motor: axial thrust along the flight path.
        if (Age <= munition.BoostSeconds)
        {
            double3 axis = Vec.Unit(localVelocity);
            if (!axis.Equals(Vec.Zero)) accel += axis * munition.BoostAccel;
        }

        // Quadratic drag on airspeed, so a coasting round bleeds speed instead of holding it.
        double airspeed = Vec.Len(localVelocity);
        if (munition.DragK > 0f && airspeed > 1e-6)
        {
            accel -= localVelocity * (munition.DragK * airspeed);
        }

        if (target is { } t)
        {
            // Extrapolate the frame-sampled target to this sub-step.
            double3 targetPos = t.PositionEcl + t.VelocityEcl * elapsedInFrame;
            double3 r = targetPos - PositionEcl;
            double3 v = t.VelocityEcl - VelocityEcl;

            ClosestApproach = Math.Min(ClosestApproach, Vec.Len(r));

            // Seeker gimbal limit: the target must be inside the cone about the flight path for
            // the round to *steer*. Recomputed every sub-step rather than latched, so a target
            // that swings back into the cone is picked up again instead of being written off.
            SeekerInView = Vec.AngleBetween(r, localVelocity) <= munition.SeekerFovRad;

            if (SeekerInView) accel += GuidanceAccel(r, v, localVelocity, gravity, munition);

            // The proximity fuse does not ask the seeker's permission, and neither does a real
            // one. Tying them together scored direct hits as misses: closing on a crossing
            // target drives the line of sight past the gimbal limit while the two are still
            // hundreds of metres apart, and the round then coasted through the target and flew
            // on to expiry. Every expired round in the flight log had lost lock.
            //
            // Uses relative motion across the sub-step, so a target that would cross the trigger
            // radius between samples still sets it off.
            if (Age >= munition.FuseArmSeconds)
            {
                double trigger = munition.FuseRadius + t.Radius;
                double tCa = Vec.TimeOfClosestApproach(r, v, h);
                double miss = Vec.Len(r + v * tCa);
                if (miss <= trigger)
                {
                    // Detonate where the round actually is at closest approach.
                    PositionEcl += VelocityEcl * tCa;
                    MissDistance = miss;
                    DetonationElapsedInFrame = elapsedInFrame + tCa;
                    State = RoundState.Detonated;
                    return;
                }
            }
        }

        if (!Vec.IsFinite(accel)) accel = gravity;

        VelocityEcl += accel * h;

        double3 localStep = (VelocityEcl - frameVelocityEcl) * h;
        TravelSinceLaunch += localStep;
        DistanceFlown += Vec.Len(localStep);
        PositionEcl += VelocityEcl * h;

        if (!Vec.IsFinite(PositionEcl) || !Vec.IsFinite(VelocityEcl))
        {
            State = RoundState.Expired;
        }
    }

    /// <summary>
    /// True proportional navigation.
    ///
    /// The line-of-sight rotation rate is omega = (r x v) / (r . r), and the commanded
    /// acceleration is N * (omega x Vc) where Vc = -v is the closing velocity vector.
    /// Steering to null the LOS rotation puts the round on a collision triangle, which is
    /// what lets it lead a crossing target instead of chasing its tail.
    ///
    /// Gravity is biased out so the law is not fighting the fall, then the command is
    /// projected perpendicular to the flight path - an airframe pulls lateral g, it does
    /// not add axial thrust - and clipped to the structural limit.
    /// </summary>
    internal static double3 GuidanceAccel(
        double3 r, double3 v, double3 missileVelocity, double3 gravity, MunitionProfile munition)
    {
        double rangeSq = Vec.Len2(r);
        if (rangeSq < 1e-6) return Vec.Zero;

        double3 omega = Vec.Cross(r, v) / rangeSq;
        double3 closingVelocity = -v;
        double3 command = Vec.Cross(omega, closingVelocity) * munition.NavConstant;

        command -= gravity * munition.GravityCompensation;
        command = Vec.RejectFrom(command, missileVelocity);

        return Vec.ClampLength(command, munition.MaxLateralAccel);
    }
}
