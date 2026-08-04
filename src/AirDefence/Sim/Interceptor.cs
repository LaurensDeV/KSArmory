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

    private const int MaxSubSteps = 64;

    /// <summary>Longest step integrable without coarsening; SimClock refuses beyond it.</summary>
    public const double MaxFaithfulStep = SubStep * MaxSubSteps;

    public double3 PositionEcl;
    public double3 VelocityEcl;

    /// <summary>Seconds since launch.</summary>
    public double Age { get; private set; }

    /// <summary>
    /// Opaque handle to whatever the round is chasing (a KSA Vehicle in the game).
    /// Null once the seeker has broken lock. Compared by reference only.
    /// </summary>
    public object? TargetRef { get; private set; }

    public RoundState State { get; private set; } = RoundState.Flying;

    /// <summary>Miss distance recorded at detonation (m). Meaningful once detonated.</summary>
    public double MissDistance { get; private set; }

    /// <summary>
    /// When the round detonated, relative to the world sample this update was given (s).
    /// <b>Negative</b>, between <c>-dt</c> and zero.
    ///
    /// <para>KSA refreshes vehicle Ecl state once per frame to the state at the <em>end</em> of
    /// the step being integrated, so a burst — which happens somewhere inside that step — is
    /// always at or before the sampled instant. Anything comparing a world position against
    /// <see cref="PositionEcl"/> therefore advances it by this much, which moves it
    /// <em>backward</em>.</para>
    ///
    /// <para>The sign matters: in the ecliptic frame the gap is hundreds of metres per frame, so
    /// getting it the wrong way round doubles the error rather than cancelling it, and a blast
    /// finds nothing at all. This used to be measured from the frame start on the assumption that
    /// samples arrived there; the engine source says otherwise.</para>
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

    /// <summary>Displacement since launch. Frame-independent, so safe to rotate into any frame.</summary>
    public double3 TravelSinceLaunch => OffsetFromPlatform - LaunchOffset;

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

    /// <param name="frameVelocityEcl">
    /// Velocity of the frame the round was launched into — the platform's. <b>Required, not
    /// optional, and this is why:</b> it seeds <see cref="VelocityLocal"/>, which is what a round's
    /// body is oriented along. Leaving it to the first <see cref="Update"/> left it zero for the
    /// round's first frame, so <c>VelocityLocal</c> degenerated to <see cref="VelocityEcl"/> — the
    /// platform's ~29.8 km/s of ecliptic motion — and the body was drawn pointing along Earth's
    /// orbit for exactly one frame before snapping into its tube.
    ///
    /// <para>A round <em>is</em> drawn on its launch frame: <c>Fire</c> runs in fire control, which
    /// is after the round update, and <c>SyncRoundBodies</c> then draws every round including one
    /// created moments earlier that has never been integrated.</para>
    /// </param>
    public Interceptor(double3 positionEcl, double3 velocityEcl, object target, int tube,
                       double3 platformEcl, double3 frameVelocityEcl)
    {
        PositionEcl = positionEcl;
        VelocityEcl = velocityEcl;
        TargetRef = target;
        Tube = tube;
        OffsetFromPlatform = positionEcl - platformEcl;
        LaunchOffset = OffsetFromPlatform;
        TrailOffsets.Add(OffsetFromPlatform);

        // Seeded here so the round is orientable from birth rather than from its first step.
        _frameVelocityEcl = frameVelocityEcl;
    }

    public bool SeekerInView { get; private set; } = true;

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
    /// How far the fins have deployed, 0 stowed to 1 at full span.
    ///
    /// <para>Drawn by scaling the fin subpart radially — the body axis is left alone — so the
    /// fins lie flat against the casing inside the tube and flick out once the round is away.
    /// Pure presentation: the flight model has no notion of fins, and this changes nothing
    /// about how the round flies.</para>
    /// </summary>
    public double FinDeployment(MunitionProfile munition)
    {
        if (munition.FinDeploySeconds <= 0f) return 1.0;
        return Math.Clamp(Age / munition.FinDeploySeconds, 0.0, 1.0);
    }

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
        double3 frameVelocityEcl, double3 platformEcl, MunitionProfile munition,
        double airDensityRatio = 1.0)
    {
        if (State != RoundState.Flying) return;

        if (target is null) TargetRef = null;

        _frameVelocityEcl = frameVelocityEcl;

        // Measured BEFORE the round is integrated, against the platform sampled at the start of
        // this same frame. Both are therefore at the same instant already, and the expression
        // contains no dt at all - which is the whole point, because it is dt that jitters.
        //
        // Measured in game, over frames that visibly jumped:
        //
        //     platform moved 639.09 m | v * current step 621.85 m | v * previous step 639.1 m
        //
        // The platform's displacement between two samples matches the step reported at the
        // EARLIER frame, not the current one - the step and the sample are one frame out of
        // phase. Extrapolating the platform forward by `frameVelocityEcl * dt` therefore
        // re-projects it by a dt that no longer describes the interval it is meant to, and every
        // wobble in the frame time comes out multiplied by ~29.8 km/s of ecliptic motion. A
        // 0.6 ms wobble is 17 m; changing the sim speed swings the step by 17 ms, which is 500 m
        // in a single frame. That is the jump.
        //
        // Without the dt term the difference between consecutive frames is
        //
        //     (v + local) * dt  -  v * dt   =   local * dt
        //
        // the round flying and nothing else, whatever dt does. The cancellation needs the
        // platform to have moved by exactly `v * dt` over the interval we integrated across, and
        // the measurement above is what establishes that it does.
        //
        // Costs one frame of visual lag, which is imperceptible and, unlike the alternatives,
        // constant. See CLAUDE.md, which documented this form before the code drifted off it.
        int steps = Math.Clamp((int)Math.Ceiling(dt / SubStep), 1, MaxSubSteps);
        double h = dt / steps;
        double elapsed = 0.0;

        for (int i = 0; i < steps && State == RoundState.Flying; i++)
        {
            Step(h, elapsed, dt, target, gravity, frameVelocityEcl, munition, airDensityRatio);
            elapsed += h;
        }

        // Measured AFTER the step, against the platform sampled this same frame, with no
        // extrapolation whatsoever.
        //
        // Write the update index as k: the platform sample is Q(k), and the round integrates from
        // P(k-1) to P(k). Measured in game over thousands of frames, to within 5 m on all but two
        // of them:
        //
        //     ( P(k) - P(k-1) ) - ( Q(k) - Q(k-1) )  =  localVelocity * dt
        //
        // So P(k) and Q(k) advance in lockstep, and P(k) - Q(k) therefore changes by exactly the
        // round's own flight each frame. That is the whole requirement, and it is the reason this
        // form cannot jitter.
        //
        // Both forms tried before pair mismatched instants and both leak the same term:
        //
        //   P(k-1) - Q(k)                     the round's motion at frame k-1 against the
        //                                     platform's at frame k
        //   P(k) - ( Q(k) + v*dt )            re-projects Q by a dt that has already changed
        //
        // Each differences to `local*dt - v*dstep`, and at ~29.8 km/s a 1 ms wobble in the step
        // is 30 m while a speed change swinging it 17 ms is 500 m - in a single frame. Measured
        // side by side in flight they agreed to 0.6 m, which is what proved they share a cause
        // rather than being alternatives.
        OffsetFromPlatform = PositionEcl - platformEcl;

        _trailTimer += dt;
        if (_trailTimer >= TrailIntervalSeconds)
        {
            _trailTimer = 0.0;
            TrailOffsets.Add(OffsetFromPlatform);
            if (TrailOffsets.Count > TrailCapacity) TrailOffsets.RemoveAt(0);
        }
    }

    /// <param name="frameSeconds">
    /// The full step this sub-step belongs to. The target sample is one whole <c>frameSeconds</c>
    /// ahead of the round's pre-step position, so it has to be back-dated by exactly this much —
    /// see the note at the extrapolation below.
    /// </param>
    private void Step(
        double h, double elapsedInFrame, double frameSeconds, TargetState? target,
        double3 gravity, double3 frameVelocityEcl, MunitionProfile munition,
        double airDensityRatio)
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
        // Scaled by air density, so one profile is right on the pad, climbing out and in orbit.
        // The ratio is 1.0 at sea level, which is where every DragK in the arsenal was tuned, so
        // this changes nothing at low altitude and removes drag entirely in vacuum. Before it, a
        // round fired in orbit was scrubbed as though at sea level.
        double airspeed = Vec.Len(localVelocity);
        if (munition.DragK > 0f && airspeed > 1e-6 && airDensityRatio > 0.0)
        {
            accel -= localVelocity * (munition.DragK * airspeed * airDensityRatio);
        }

        if (target is { } t)
        {
            // Back-date the target sample to the round's own epoch, then extrapolate to this
            // sub-step.
            //
            // KSA writes every vehicle's Ecl state once per frame, at the top of OnFrame, to the
            // state at GetLastSimStep().NextTime - the END of the step this update is about to
            // integrate the round across. The platform sample is used that way and is correct.
            // The target sample was not: it was extrapolated FORWARD from an end-of-step value
            // while being differenced against the round's PRE-step position, so every line of
            // sight carried a constant
            //
            //     r = r_true + targetVelocityEcl * frameSeconds
            //
            // and proportional navigation, doing its job perfectly, flew a clean intercept on a
            // ghost displaced by one frame of the planet's ~29.8 km/s of ecliptic motion.
            //
            // That is 450-680 m, not the 10-15 m the log used to report. MissDistance could never
            // show it: it is a threshold crossing with a one-sub-step horizon, so it is bounded by
            // the fuse radius whatever the round actually does. Confirmed three ways - headlessly,
            // where the miss vector came out 0.96-0.999 aligned with the ecliptic carrier and
            // back-dating collapsed the true closest approach onto MissDistance at every step
            // size; from a flight log, where fitting the same-instant `tgt` trace gave a closest
            // approach of 679 m against |V_ecl| * dt = 676 m; and by predicting the outcome of the
            // endgame sub-step experiment that had already been run and reverted - a whole-frame
            // bias cannot be helped by subdividing the frame, it only converges harder on the
            // same wrong point.
            //
            // Subtracting frameSeconds puts the target back at the instant the round is actually
            // at, which is what makes the common ecliptic motion cancel.
            double3 targetPos = t.PositionEcl + t.VelocityEcl * (elapsedInFrame - frameSeconds);
            double3 r = targetPos - PositionEcl;
            double3 v = t.VelocityEcl - VelocityEcl;

            ClosestApproach = Math.Min(ClosestApproach, Vec.Len(r));

            // Command-linked rounds always steer; a seeker round has a gimbal limit, recomputed
            // each sub-step so losing the target is not permanent.
            SeekerInView = munition.Guidance == GuidanceMode.CommandLink
                           || Vec.AngleBetween(r, localVelocity) <= munition.SeekerFovRad;

            if (SeekerInView) accel += GuidanceAccel(r, v, localVelocity, gravity, munition);

            {
                // The fuse does not ask the seeker's permission; tying them together scored
                // direct hits as misses.
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
                        // Reported on the same epoch as the geometry that produced it. The
                        // battery advances the world forward by this much to place the burst
                        // (DefenceBattery.Detonate and the blast sweep), so it has to be measured
                        // from the target's back-dated instant too. Correcting the extrapolation
                        // above without correcting this leaves the blast wrong by V*frameSeconds
                        // in the opposite direction - the two are one change, not two.
                        DetonationElapsedInFrame = elapsedInFrame + tCa - frameSeconds;
                        State = RoundState.Detonated;
                        return;
                    }
                }
            }
        }

        if (!Vec.IsFinite(accel)) accel = gravity;

        VelocityEcl += accel * h;

        double3 stepEcl = VelocityEcl * h;
        DistanceFlown += Vec.Len((VelocityEcl - frameVelocityEcl) * h);
        PositionEcl += stepEcl;

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
