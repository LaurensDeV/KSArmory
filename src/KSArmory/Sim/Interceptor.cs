using Brutal.Numerics;

namespace KSArmory;

internal enum RoundState
{
    Flying,
    Detonated,
    Expired,

    /// <summary>
    /// Destroyed in the air by somebody else. Distinct from <see cref="Detonated"/>: this round's
    /// warhead never fired, so nothing is owed a blast at the wreck.
    /// </summary>
    ShotDown,
}

/// <summary>
/// Sampled target state for one frame, in the ecliptic frame.
///
/// <para><paramref name="Handle"/> is opaque and defaulted: <c>Sim/</c> only ever compares it or
/// hands it back, and a caller with nothing to identify leaves it out.</para>
///
/// <para><paramref name="Emitting"/> is whether the contact is radiating this frame, which only
/// <see cref="GuidanceMode.AntiRadiation"/> reads. It defaults to <c>true</c> because a caller that
/// has no notion of emission is describing a target every other seeker can still see.</para>
/// </summary>
internal readonly record struct TargetState(double3 PositionEcl, double3 VelocityEcl, double Radius,
                                            object? Handle = null, bool Emitting = true);

/// <summary>
/// A guided round: proportional navigation, boost, proximity fuse, and the ground it stops on.
///
/// <para>Integrated by this mod rather than by KSA's vehicle physics, because its fuse needs
/// sub-frame accuracy: one frame at its closing speed steps clean over the fuse radius. The caller samples the
/// target once per frame; the round extrapolates it across its own sub-steps.</para>
/// </summary>
internal sealed class Interceptor : IProjectile
{
    private const int TrailCapacity = 32;
    private const double TrailIntervalSeconds = 0.05;

    /// <summary>
    /// Fixed integration step. Frames are subdivided to this, which keeps the guidance stable and
    /// stops fast targets tunnelling through the fuse radius.
    ///
    /// <para>Also the default for <see cref="MunitionProfile.SubStep"/>, which is what a
    /// <see cref="Slug"/> sub-steps at.</para>
    /// </summary>
    internal const double SubStep = 0.005;

    internal const int MaxSubSteps = 64;

    /// <summary>
    /// Longest step integrable without coarsening, and the default <see cref="SimClock.MaxStep"/> and
    /// <see cref="MunitionProfile.MaxFaithfulStepSeconds"/> hold a round with no opinion to.
    /// </summary>
    public const double MaxFaithfulStep = SubStep * MaxSubSteps;

    /// <inheritdoc cref="IProjectile.PositionEcl"/>
    public double3 PositionEcl { get; private set; }

    /// <inheritdoc cref="IProjectile.VelocityEcl"/>
    public double3 VelocityEcl { get; private set; }

    /// <summary>Seconds since launch.</summary>
    public double Age { get; private set; }

    /// <summary>
    /// Opaque handle to whatever the round is chasing (a KSA Vehicle in the game).
    /// Null once the seeker has broken lock. Compared by reference only.
    /// </summary>
    public object? TargetRef { get; private set; }

    /// <inheritdoc cref="IProjectile.Aimpoint"/>
    public Aimpoint Aimpoint { get; set; }

    /// <inheritdoc cref="IProjectile.Retarget"/>
    public void Retarget(Aimpoint aim)
    {
        Aimpoint = aim;
        TargetRef = aim.Handle;
    }

    /// <inheritdoc cref="IProjectile.FaithfulStepSeconds"/>
    public double FaithfulStepSeconds
        => _lastDensity > Medium.NoticeableDensity
               ? Math.Min(Munition.MaxFaithfulStepSeconds, Medium.FaithfulStepInAir)
               : Munition.MaxFaithfulStepSeconds;

    // What the round last flew through, so it can say what step it needs before the next one.
    private double _lastDensity;

    /// <summary>
    /// The air where the round is, asked per sub-step and back-dated as a <see cref="Slug"/> asks
    /// it. Null holds the frame's single sample.
    ///
    /// <para>Not a refinement for a missile, however much its guidance dominates its flight. The body
    /// its altitude is measured from was sampled at the frame's end, so the frame's sample reads up to
    /// a frame of the planet's ~30 km/s as altitude — below the waterline for a round leaving a site
    /// near sea level, where the drag is 840 times the air's and stops it when the motor does.</para>
    /// </summary>
    public Func<double3, double, double>? AirDensityAt { get; set; }

    /// <summary>
    /// Where the ground is. Every missile asks, unlike a shell: a salvo is a dozen rounds rather than
    /// a burst's hundred and fifty, and one fired downhill otherwise flies through the hill still
    /// steering. Not <see cref="MunitionProfile.HitsTerrain"/>, which also means a store that ends by
    /// landing. Sampled once a frame at the round's own instant and held, as a <see cref="Slug"/>
    /// holds it.
    /// </summary>
    public IGroundTest? Ground { get; set; }

    /// <summary>How far the ground's centre has moved by a time into the frame; see <see cref="Slug.GroundCentreDriftAt"/>.</summary>
    public Func<double, double3>? GroundCentreDriftAt { get; set; }

    /// <summary>It flew into the ground, and its warhead went off there.</summary>
    public bool HitGround { get; private set; }

    private bool _haveGround;
    private double3 _groundCentre;
    private double _groundRadius;

    private double3 GroundCentre(double secondsIntoFrame)
        => GroundCentreDriftAt is { } drift ? _groundCentre + drift(secondsIntoFrame) : _groundCentre;

    public RoundState State { get; private set; } = RoundState.Flying;

    /// <inheritdoc cref="IProjectile.ShootDown"/>
    public void ShootDown()
    {
        if (State == RoundState.Flying) State = RoundState.ShotDown;
    }

    /// <summary>
    /// Always null. This round is proximity-fused, so it kills by being near rather than by
    /// arriving, and its lethality is decided from <see cref="MissDistance"/>.
    /// </summary>
    public object? StruckBody => null;

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
    /// <para>The sign matters: the gap is hundreds of metres per frame, so reversing it doubles
    /// the error rather than cancelling it and the blast finds nothing.</para>
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

    private double3 _frameVelocityEcl;

    /// <inheritdoc cref="IProjectile.Tube"/>
    public int Tube { get; init; }

    /// <inheritdoc cref="IProjectile.OffsetFromPlatform"/>
    public double3 OffsetFromPlatform { get; private set; }

    /// <summary>
    /// <see cref="OffsetFromPlatform"/> at the moment of launch, so <see cref="TravelSinceLaunch"/> is
    /// a difference and carries none of the gap between the platform's orbit and physics positions.
    /// </summary>
    public double3 LaunchOffset { get; private set; }

    /// <summary>Displacement since launch. Frame-independent, so safe to rotate into any frame.</summary>
    public double3 TravelSinceLaunch => OffsetFromPlatform - LaunchOffset;

    /// <inheritdoc cref="IProjectile.Reanchor"/>
    public void Reanchor(double3 offsetDelta)
    {
        if (!Vec.IsFinite(offsetDelta)) return;

        OffsetFromPlatform += offsetDelta;
        LaunchOffset += offsetDelta;

        for (int i = 0; i < _trail.Count; i++) _trail[i] += offsetDelta;
    }


    /// <summary>
    /// Where this round left from, in the launcher part's own frame. Set by the system at
    /// launch and never read by the simulation — it exists so the round's *body* can be placed
    /// against the tube it came out of rather than against the platform's orbit position.
    /// </summary>
    public double3 LaunchAnchorPartFrame { get; set; }

    /// <inheritdoc />
    public double3 ReleaseHeadingEcl { get; set; }

    /// <inheritdoc />
    public doubleQuat LaunchAttitude { get; set; }

    /// <summary>
    /// The launching craft's velocity in the round's own frame at release, so the motor can push
    /// along the round rather than along everything it inherited.
    ///
    /// <para>Zero for a launcher standing still, for which thrusting along the flight path and
    /// along the tube are the same thing. Set at launch and never updated.</para>
    /// </summary>
    public double3 LaunchFrameVelocityLocal { get; set; }

    /// <inheritdoc />
    public double MotorDelaySeconds { get; init; }

    /// <inheritdoc />
    public double3 EjectAccelerationEcl { get; init; }

    /// <inheritdoc />
    public double EjectSeconds { get; init; }

    // Where "gained" is measured from once a delayed motor lights: the velocity at ignition. Measured from
    // release instead, it holds the fall and the ejector's push, and the motor would drive the round down.
    private double3? _gainedFromAtIgnition;

    private double3 ThrustAxis(double3 localVelocity)
    {
        if (MotorDelaySeconds > 0.0) _gainedFromAtIgnition ??= localVelocity;
        double3 axis = Vec.Unit(localVelocity - (_gainedFromAtIgnition ?? LaunchFrameVelocityLocal));
        return axis.Equals(Vec.Zero) ? Vec.Unit(ReleaseHeadingEcl) : axis;
    }

    /// <inheritdoc cref="IProjectile.Munition"/>
    public required MunitionProfile Munition { get; init; }

    // Recent positions for the smoke trail, oldest first, as platform-relative offsets. Stored this
    // way for the same reason: absolute points recorded across 1.6 s of trail would be smeared over
    // ~48 km of the planet's motion around its star.
    private readonly List<double3> _trail = new(TrailCapacity);

    /// <inheritdoc cref="IProjectile.TrailOffsets"/>
    public IReadOnlyList<double3> TrailOffsets => _trail;

    private double _trailTimer;

    /// <param name="frameVelocityEcl">
    /// Velocity of the frame the round launches into — the platform's. Required, because it seeds
    /// <see cref="VelocityLocal"/>, which is what the body is oriented along, and a round is drawn
    /// on its launch frame before any <see cref="Update"/>. Left unseeded, <c>VelocityLocal</c>
    /// degenerates to <see cref="VelocityEcl"/> and the body points along the planet's orbit.
    /// </param>
    public Interceptor(double3 positionEcl, double3 velocityEcl, object? target, int tube,
                       double3 platformEcl, double3 frameVelocityEcl)
    {
        PositionEcl = positionEcl;
        VelocityEcl = velocityEcl;
        TargetRef = target;
        Tube = tube;
        OffsetFromPlatform = positionEcl - platformEcl;
        LaunchOffset = OffsetFromPlatform;
        _trail.Add(OffsetFromPlatform);

        _frameVelocityEcl = frameVelocityEcl;
    }

    public bool SeekerInView { get; private set; } = true;

    public bool HasLock => TargetRef is not null && SeekerInView;

    /// <summary>
    /// Whether an <see cref="GuidanceMode.AntiRadiation"/> round has ever heard its target, and so
    /// has somewhere to go if the set shuts down. Always false for every other guidance.
    /// </summary>
    public bool HasEmission { get; private set; }

    // Where the emission last came from, the velocity it was seen with, and the round's own age at
    // that moment. Replayed forward rather than stored as a bare coordinate -- see Step.
    private double3 _emissionPosEcl;
    private double3 _emissionVelEcl;
    private double _emissionAge;

    // The aimpoint is a place someone designated rather than something the round has to find, so
    // there is nothing for a gimbal limit or an emission to lose.
    private bool OperatorHeld => Aimpoint.Kind == AimpointKind.Ground;

    /// <summary>
    /// Velocity relative to the moving frame — the round's airspeed vector, and the direction it
    /// points. Not <see cref="VelocityEcl"/>, which carries the platform's ~29.8 km/s and would
    /// orient every round the same way.
    /// </summary>
    public double3 VelocityLocal => VelocityEcl - _frameVelocityEcl;

    public double Speed => Vec.Len(VelocityLocal);

    /// <inheritdoc cref="IProjectile.SteeringCommandEcl"/>
    public double3 SteeringCommandEcl { get; private set; }

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
    /// Target state sampled at the end of this step, or null if the target is gone. Back-dated
    /// to the round's epoch and extrapolated linearly across sub-steps.
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
    /// The platform's position as sampled this frame, i.e. the same instant every other world
    /// position was sampled. Used only to express the round's offset for drawing.
    /// </param>
    public void Update(
        double dt, TargetState? target, double3 gravity,
        double3 frameVelocityEcl, double3 platformEcl, MunitionProfile munition,
        double mediumDensityRatio = 1.0)
    {
        if (State != RoundState.Flying) return;

        // A negative h would integrate the round backwards. SimClock and the frame hook both
        // refuse these already; this holds the IProjectile contract regardless.
        if (!double.IsFinite(dt) || dt <= 0.0) return;

        if (target is null) TargetRef = null;

        _frameVelocityEcl = frameVelocityEcl;

        // At the round's own instant: the body sample is a frame newer, and asked with the raw
        // pre-step position it reads the height field a frame of ~30 km/s away.
        double3 atOwnEpoch = PositionEcl - (GroundCentreDriftAt?.Invoke(-dt) ?? Vec.Zero);
        _haveGround = Ground is { } ground
                      && ground.TryGround(atOwnEpoch, out _groundCentre, out _groundRadius)
                      && double.IsFinite(_groundRadius) && _groundRadius > 0.0;

        int steps = Math.Clamp((int)Math.Ceiling(dt / SubStep), 1, MaxSubSteps);
        double h = dt / steps;
        double elapsed = 0.0;

        for (int i = 0; i < steps && State == RoundState.Flying; i++)
        {
            double density = AirDensityAt?.Invoke(PositionEcl, elapsed - dt) ?? mediumDensityRatio;
            if (!double.IsFinite(density) || density < 0.0) density = mediumDensityRatio;

            Step(h, elapsed, dt, target, gravity, frameVelocityEcl, munition, density);
            elapsed += h;
        }

        // After the step, against the platform sampled this same frame, with no extrapolation: any
        // other pairing leaks v*dstep, 30 m per ms of step wobble. docs/FRAMES-AND-EPOCHS.md, "Rules that follow".
        OffsetFromPlatform = PositionEcl - platformEcl;

        _trailTimer += dt;
        if (_trailTimer >= TrailIntervalSeconds)
        {
            _trailTimer = 0.0;
            _trail.Add(OffsetFromPlatform);
            if (_trail.Count > TrailCapacity) _trail.RemoveAt(0);
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
        double mediumDensityRatio)
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

        _lastDensity = mediumDensityRatio;

        double3 accel = Medium.Buoyancy(gravity, munition, mediumDensityRatio)
                        + (EjectAccelerationEcl * Ejector.ShareOfStep(Age - h, h, EjectSeconds));

        // Thrust along the velocity gained since release, never the velocity it has: a rail round
        // inherits its craft's velocity, and thrusting along that drove an AGM-88 1,050 m/s the craft's way.
        if (((IProjectile)this).MotorBurning)
        {
            // Nothing gained yet, which is a round its rail imparts no speed to at all: the heading it
            // left along is the only thing that says which way it is pointing.
            double3 axis = ThrustAxis(localVelocity);
            if (!axis.Equals(Vec.Zero)) accel += axis * munition.BoostAccelAt(Age - MotorDelaySeconds);
        }

        accel -= Medium.Drag(localVelocity, munition, mediumDensityRatio);

        if (target is { } t)
        {
            // The sample is end-of-step and the round starts the step at its beginning: back-dated, the
            // ecliptic motion cancels; not, PN flies onto a ghost 450-680 m away, which MissDistance,
            // bounded by the fuse radius, cannot show. docs/FRAMES-AND-EPOCHS.md, "Rules that follow".
            double3 targetPos = InFrame.AtBurst(t.PositionEcl, t.VelocityEcl, elapsedInFrame - frameSeconds);
            double3 targetVel = t.VelocityEcl;

            // A silent set is remembered as a position and the velocity it was seen with, replayed on
            // the round's own clock: the velocity carries the planet's ~29.8 km/s, which a bare point loses.
            bool homingOnMemory = false;
            if (munition.Guidance == GuidanceMode.AntiRadiation && !OperatorHeld)
            {
                if (t.Emitting)
                {
                    _emissionPosEcl = targetPos;
                    _emissionVelEcl = targetVel;
                    _emissionAge = Age;
                    HasEmission = true;
                }
                else if (HasEmission)
                {
                    targetPos = _emissionPosEcl + _emissionVelEcl * (Age - _emissionAge);
                    targetVel = _emissionVelEcl;
                    homingOnMemory = true;
                }
            }

            double3 r = targetPos - PositionEcl;
            double3 v = targetVel - VelocityEcl;

            ClosestApproach = Math.Min(ClosestApproach, Vec.Len(r));

            // Command-linked rounds always steer; a seeker round has a gimbal limit, recomputed
            // each sub-step so losing the target is not permanent.
            //
            // A designated place is steered the same way whatever the round carries: the operator
            // is holding it, so there is nothing for a gimbal limit to lose. Without this a rail
            // can only shoot where it already points, which for a rail bolted to a stack is
            // straight along the stack and nowhere useful.
            //
            // An anti-radiation round that has never heard its target is blind rather than
            // off-axis: there is no emission to point a gimbal at, so it coasts.
            bool blind = munition.Guidance == GuidanceMode.AntiRadiation
                         && !OperatorHeld && !t.Emitting && !homingOnMemory;

            SeekerInView = !blind
                           && (munition.Guidance == GuidanceMode.CommandLink
                               || OperatorHeld
                               || Vec.AngleBetween(r, localVelocity) <= munition.SeekerFovRad);

            // Nothing steers until it is clear of what launched it. A rail-launched round leaves
            // along its rail and turns after separation; guiding from the first sub-step turns it
            // into the craft carrying it.
            SteeringCommandEcl = Vec.Zero;
            if (SeekerInView && Age >= MotorDelaySeconds + munition.SeparationSeconds)
            {
                SteeringCommandEcl = GuidanceAccel(r, v, localVelocity, gravity, munition);
                accel += SteeringCommandEcl;
            }

            {
                // The fuse does not ask the seeker's permission; tying them together scores
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
                        // On the back-dated target's epoch, the same rule as the back-dating above:
                        // measured from the sampled instant the blast lands V*frameSeconds the other way.
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
        double3 before = PositionEcl;
        PositionEcl += stepEcl;

        if (_haveGround)
        {
            double was = Vec.Len(before - GroundCentre(elapsedInFrame - frameSeconds)) - _groundRadius;
            double now = Vec.Len(PositionEcl - GroundCentre(elapsedInFrame + h - frameSeconds)) - _groundRadius;

            // Only crossing down from above. A tube can sit a hair under the coarse height field,
            // and a round that started below it would otherwise burst on its own rail.
            if (was > 0.0 && now <= 0.0)
            {
                double f = Math.Clamp(was / (was - now), 0.0, 1.0);

                PositionEcl = before + stepEcl * f;
                // Not a fuse range: zero would read as a direct hit on the target. The blast sweep
                // judges what was near where it went off.
                MissDistance = double.PositiveInfinity;
                HitGround = true;
                DetonationElapsedInFrame = elapsedInFrame + h * f - frameSeconds;
                State = RoundState.Detonated;
                return;
            }
        }

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
