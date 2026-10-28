using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// An unguided round — a shell, a bomb, or a rocket. Ballistics, a contact fuse, and a motor if the
/// munition declares a boost.
///
/// <para>Not an <see cref="Interceptor"/> with its nav constant zeroed: it has no seeker, lock or
/// command link, so most of that flight model would be dead code behind guards.</para>
///
/// <para>It obeys the same frame and epoch rules regardless — those belong to the engine, not to
/// the weapon. See docs/FRAMES-AND-EPOCHS.md.</para>
/// </summary>
internal sealed class Slug : IProjectile
{
    private const int TrailCapacity = 32;
    private const double TrailIntervalSeconds = 0.05;

    private readonly List<double3> _trail = new(TrailCapacity);
    private double3 _frameVelocityEcl;
    private double _trailTimer;

    // The ground under the round, sampled once a frame. Held rather than re-read because the
    // terrain query is the expensive call and a sphere of this radius is the surface for the few
    // metres of ground track one frame covers.
    private double3 _groundCentre;
    private double _groundRadius;
    private double3 _groundSampledAtEcl;
    private double _groundSampledOverSeconds;
    private bool _haveGround;

    // The ground under where the next sub-step begins, and whether it was read there. Only kept
    // while the round re-reads the ground near impact.
    private double _radiusBefore;
    private bool _radiusBeforeFresh;

    public Slug(double3 positionEcl, double3 velocityEcl, object? target, int tube,
                double3 platformEcl, double3 frameVelocityEcl)
    {
        PositionEcl = positionEcl;
        VelocityEcl = velocityEcl;
        TargetRef = target;
        Tube = tube;
        OffsetFromPlatform = positionEcl - platformEcl;
        LaunchOffset = OffsetFromPlatform;
        _trail.Add(OffsetFromPlatform);

        // Seeded here: a round is drawn on its launch frame, before any Update, and needs a
        // usable VelocityLocal to orient by.
        _frameVelocityEcl = frameVelocityEcl;
    }

    /// <summary>
    /// The air where the round is, asked per sub-step rather than once a frame.
    ///
    /// <para>Null falls back to the frame's single sample, which is what a round flying through no
    /// atmosphere worth resolving needs.</para>
    /// </summary>
    /// <param name="secondsIntoFrame">
    /// How far into the frame the sub-step is. The world's own bodies are sampled once a frame and
    /// stand still through it, while the round moves — carrying the planet's ~30 km/s of ecliptic
    /// travel with it — so a lookup measured against a frozen body reads an altitude that ramps
    /// across the frame. This is what lets the far side put that back.
    /// </param>
    public Func<double3, double, double>? AirDensityAt { get; set; }

    /// <summary>
    /// Gravity at a position and a time into the frame, or null to use the vector handed in.
    /// <see cref="AirDensityAt"/>'s convention: the body sample is end-of-frame, the round mid-step.
    /// </summary>
    public Func<double3, double, double3>? GravityAt { get; set; }

    /// <summary>
    /// How far along its arrival this round is, asked of the caller because only it knows the body.
    /// Null answers <see cref="Approach.Unknown"/>, which is the age limit and nothing else.
    /// </summary>
    /// <remarks>Position and velocity in the ecliptic; the caller converts.</remarks>
    public Func<double3, double3, Approach>? ApproachAt { get; set; }

    // Seconds spent where arriving happens; a round the ground stops is reaped on this, not on Age.
    // A release 100 km up takes 152 s to land. Unclassified, it advances every step, as Age does.
    private double _arrivingSeconds;

    /// <summary>
    /// The air's motion at a stated position and time into the frame, re-read per sub-step.
    /// </summary>
    /// <remarks>
    /// Held for the frame, it is ω×150 m out of step over a re-entering round's frame of ground, which
    /// tilts the drag: 2.3–8.3 mm, sign by launch azimuth. <c>docs/ACCURACY-PLAN.md</c> 3dx Rank 2.
    /// </remarks>
    public Func<double3, double, double3>? AirVelocityAt { get; set; }

    /// <summary>
    /// Whether this round reads <see cref="AirVelocityAt"/> per sub-step rather than holding the
    /// frame's sample. Off, so a round nobody asks holds it.
    /// </summary>
    public bool AirVelocityAtOwnSubStep { get; set; }

    /// <summary>
    /// Solve the ground crossing against the terrain under it rather than against the chord joining
    /// the sub-step's two height samples.
    /// </summary>
    /// <remarks>
    /// The samples are 5.5 m of ground apart at a 5,500 m/s arrival, and the prediction point-samples
    /// the field, so the chord carried 57% of the walk's variance. <c>docs/ACCURACY-PLAN.md</c> 3dv.
    /// Needs <see cref="ResampleGroundNearImpact"/>.
    /// </remarks>
    public bool StopOnTheTerrain { get; set; }

    /// <summary>
    /// Take the drag at the sub-step's midpoint velocity rather than at the velocity it starts with.
    /// </summary>
    /// <remarks>
    /// A re-entering round sheds ~450 m/s², so the start-of-step speed always over-drags, one-signed,
    /// for the whole fall. Only for a <see cref="SecondOrder"/> round. <c>docs/ACCURACY-PLAN.md</c> 3dx.
    /// </remarks>
    public bool DragAtMidpointVelocity { get; set; }

    /// <inheritdoc cref="IProjectile.FaithfulStepSeconds"/>
    public double FaithfulStepSeconds
        => _lastDensity > Medium.NoticeableDensity
               ? Math.Min(Munition.PreferredStep, Medium.FaithfulStepInAir)
               : Munition.PreferredStep;

    // What the round last flew through, so it can say what step it needs before the next one.
    private double _lastDensity;

    public RoundState State { get; private set; } = RoundState.Flying;

    /// <inheritdoc cref="IProjectile.ShootDown"/>
    public void ShootDown()
    {
        if (State == RoundState.Flying) State = RoundState.ShotDown;
    }
    public int Tube { get; }
    public double Age { get; internal set; }

    public double3 PositionEcl { get; private set; }
    public double3 VelocityEcl { get; private set; }

    /// <summary>
    /// What its launcher's rotation gave it at the tube, already inside <see cref="VelocityEcl"/>.
    ///
    /// <para>Kept as thrown rather than re-derived: a release prediction leaves spin out, so this is
    /// the whole of what the round's velocity adds to the one predicted, whatever arm it was measured
    /// on.</para>
    /// </summary>
    /// <remarks>A velocity difference, so it carries no frame's motion.</remarks>
    public double3 SpinVelocityEcl { get; init; }

    public double3 OffsetFromPlatform { get; private set; }
    private double3 LaunchOffset { get; set; }
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
    /// Add a velocity the round leaves its tube with, on top of the one it was released with.
    ///
    /// <para>Refused once the round has taken a step. A kick is solved against the state the round
    /// was released in, so one landing on a round already in flight is a velocity change nobody
    /// solved for.</para>
    /// </summary>
    /// <param name="kickEcl">A velocity difference, so it carries no frame's motion.</param>
    public bool TryAddSeparationVelocity(double3 kickEcl)
    {
        if (Age != 0.0 || State != RoundState.Flying || !Vec.IsFinite(kickEcl)) return false;

        VelocityEcl += kickEcl;
        return true;
    }

    public double3 VelocityLocal => VelocityEcl - _frameVelocityEcl;
    public double Speed => Vec.Len(VelocityLocal);
    public double DistanceFlown { get; private set; }
    public IReadOnlyList<double3> TrailOffsets => _trail;
    public double3 LaunchAnchorPartFrame { get; set; }

    /// <inheritdoc />
    public double3 ReleaseHeadingEcl { get; set; }

    /// <inheritdoc />
    public doubleQuat LaunchAttitude { get; set; }

    /// <summary>
    /// The launching craft's velocity in the round's own frame at release, so a motor pushes along
    /// the velocity the round has gained rather than everything it inherited. Zero for a launcher
    /// standing still.
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

    // A round the ground stops runs its clock only where it could be arriving; one that can never
    // arrive is caught by RoundReach instead. Anything else is reaped on its age.
    private void Reap(MunitionProfile munition, double dt)
    {
        if (State != RoundState.Flying) return;

        Approach approach = munition.HitsTerrain && ApproachAt is { } ask
                            ? ask(PositionEcl, VelocityEcl)
                            : Approach.Unknown;

        if (approach == Approach.Impossible)
        {
            State = RoundState.Expired;
            return;
        }

        // Unknown runs the clock too, or a round that can be neither judged nor stopped is immortal.
        if (approach is not (Approach.Coasting or Approach.Landing)) _arrivingSeconds += dt;

        if (_arrivingSeconds >= munition.MaxFlightSeconds) State = RoundState.Expired;
    }

    public object? TargetRef { get; private set; }

    /// <summary>
    /// Everything this round can run into, refreshed by the caller each frame, so a shell strikes a
    /// bystander as well as its target. End-of-frame samples; <see cref="Step"/> back-dates them.
    /// </summary>
    public IReadOnlyList<TargetState> Contacts { get; set; } = [];

    /// <summary>
    /// What decides whether this round actually met a body; null means the bounding sphere decides.
    /// Not on <see cref="IProjectile"/>, so it cannot be wired into a proximity-fused round.
    /// </summary>
    public IHullTest? Hull { get; set; }

    /// <inheritdoc cref="IProjectile.StruckBody"/>
    public object? StruckBody { get; private set; }

    /// <summary>
    /// Where the ground is, for a round the terrain stops. Null otherwise: a terrain sample per shell
    /// across a 150-shell burst buys nothing.
    /// </summary>
    public IGroundTest? Ground { get; set; }

    /// <summary>
    /// How far the sampled ground centre has moved by a stated time into the frame, back-dated the
    /// same way <see cref="AirDensityAt"/> is. Null holds the sample for the frame, which is what
    /// every rig that models no body motion wants and what <see cref="RoundFields.Held"/> means.
    /// </summary>
    public Func<double, double3>? GroundCentreDriftAt { get; set; }

    private double3 GroundCentre(double secondsIntoFrame)
        => GroundCentreDriftAt is { } drift
               ? _groundCentre + drift(secondsIntoFrame)
               : _groundCentre;

    /// <summary>
    /// How far the ground <em>under a stated point</em> has moved by a stated time into the frame —
    /// the body's travel plus its spin at that radius, which <see cref="GroundCentreDriftAt"/> lacks
    /// because the centre is on the spin axis. <see cref="AirDensityAt"/>'s convention.
    /// </summary>
    public Func<double3, double, double3>? GroundQueryDriftAt { get; set; }

    /// <summary>
    /// Back-date a terrain query by the full ground velocity, not the centre's alone: the query is
    /// answered at the frame's end rotation. <c>docs/ACCURACY-PLAN.md</c> 3cw.
    /// Needs <see cref="GroundQueryDriftAt"/>; <see cref="IcbmConfig.GroundQueryAtOwnEpoch"/> sets it.
    /// </summary>
    public bool GroundQueryAtOwnEpoch { get; set; }

    // The query is against a body sample one applied step newer, so the point is walked FORWARD by
    // that ground's travel; the seconds arrive negative, hence the subtraction.
    private double3 GroundQueryAt(double3 positionEcl, double secondsIntoFrame)
        => GroundQueryAtOwnEpoch && GroundQueryDriftAt is { } query
               ? positionEcl - query(positionEcl, secondsIntoFrame)
               : positionEcl - (GroundCentreDriftAt?.Invoke(secondsIntoFrame) ?? Vec.Zero);

    /// <summary>
    /// Re-read the ground under every sub-step within <see cref="GroundResampleBandMetres"/> of the
    /// surface. Held, a slope stops it on ground it has left, times <c>cot(gamma)</c>. <c>docs/ACCURACY-PLAN.md</c> 3cr.
    /// </summary>
    public bool ResampleGroundNearImpact { get; set; }

    /// <summary>
    /// Integrate each sub-step to second order: drift half, read gravity and the air there, kick,
    /// drift the rest. First order holds an <c>a·h/2</c> velocity error for the whole fall.
    /// <c>docs/ACCURACY-PLAN.md</c> 3cu.
    /// </summary>
    public bool SecondOrder { get; set; }

    /// <summary>Whether this is one of the belt's tracer rounds: <see cref="MunitionProfile.TracerEvery"/>.</summary>
    public bool Tracer { get; init; }

    /// <summary>
    /// How far above the held surface the re-reading starts: it clears a frame's terrain change — a
    /// 30% slope over 90 m of track is 27 m — plus a sub-step's drop.
    /// </summary>
    public const double GroundResampleBandMetres = 200.0;

    /// <summary>True when it was the ground that stopped this round rather than a body or a fuse.</summary>
    public bool HitGround { get; private set; }

    /// <summary>True when its fuse fired at <see cref="MunitionProfile.BurstHeightMetres"/> over the ground.</summary>
    public bool BurstAtHeight { get; private set; }

    /// <summary>
    /// The surface radius the crossing was tested against, or NaN. The frame's sample, up to a frame of
    /// ground stale, unless the round <see cref="ResampleGroundNearImpact"/>. <c>docs/MIRV-NEXT.md</c> 8k.
    /// </summary>
    public double GroundRadiusUsed => _haveGround ? _groundRadius : double.NaN;

    /// <summary>
    /// Where the surface the round stopped against was read — the start of its last frame, or the
    /// crossing for a round that <see cref="ResampleGroundNearImpact"/>. Measurement only.
    /// </summary>
    public double3 GroundSampledAtEcl => _groundSampledAtEcl;

    /// <inheritdoc cref="GroundSampledAtEcl"/>
    public double GroundSampledOverSeconds => _groundSampledOverSeconds;

    /// <summary>
    /// How high it ended against the centre and radius it tested the crossing with. Near zero puts any
    /// disagreement with a freshly sampled altitude on the centre. <c>docs/MIRV-NEXT.md</c> 8k.
    /// </summary>
    public double StopAltitudeAgainstOwnGround =>
        _haveGround ? Vec.Len(PositionEcl - GroundCentre(DetonationElapsedInFrame)) - _groundRadius
                    : double.NaN;

    /// <inheritdoc cref="IProjectile.Aimpoint"/>
    public Aimpoint Aimpoint { get; set; }

    /// <inheritdoc cref="IProjectile.Retarget"/>
    public void Retarget(Aimpoint aim)
    {
        Aimpoint = aim;
        TargetRef = aim.Handle;
    }

    /// <summary>Always false. A slug is not steered, so it has nothing to lose lock on.</summary>
    public bool HasLock => false;

    /// <summary>Always true. There is no seeker to be blinded, so nothing gates its flight.</summary>
    public bool SeekerInView => true;

    /// <summary>Time of flight this shell was fused for, set from the lead solution; zero for none.</summary>
    public double FuseSeconds { get; set; }

    /// <summary>True when the timed fuse fired rather than the proximity one, which no burst shows.</summary>
    public bool BurstOnTime { get; private set; }

    public double MissDistance { get; private set; }
    public double ClosestApproach { get; private set; } = double.MaxValue;
    public double DetonationElapsedInFrame { get; private set; }

    /// <summary>How far a fin set has opened, 0 folded to 1 at full span. Drawn only.</summary>
    public double FinDeployment(MunitionProfile munition)
    {
        if (munition.FinDeploySeconds <= 0f) return 1.0;
        return Math.Clamp(Age / munition.FinDeploySeconds, 0.0, 1.0);
    }

    // Along the velocity gained since release, as Interceptor's motor pushes: the total carries the craft's
    // own velocity, and a rocket fired off a craft moving any other way would be driven along its track.
    private double3 Thrust(double3 localVelocity, MunitionProfile munition)
    {
        if (!((IProjectile)this).MotorBurning) return Vec.Zero;
        return ThrustAxis(localVelocity) * munition.BoostAccelAt(Age - MotorDelaySeconds);
    }

    /// <inheritdoc cref="IProjectile.SteeringCommandEcl"/>
    public double3 SteeringCommandEcl { get; private set; }

    public void Update(double dt, TargetState? target, double3 gravity, double3 frameVelocityEcl,
                       double3 platformEcl, MunitionProfile munition, double mediumDensityRatio = 1.0)
    {
        if (State != RoundState.Flying) return;
        if (!double.IsFinite(dt) || dt <= 0.0) return;

        _frameVelocityEcl = frameVelocityEcl;

        // Losing the target leaves it flying with nothing to fuse against, and — for a tail-kit
        // round — nothing to steer at either, so it finishes the fall ballistically.
        if (target is null) TargetRef = null;

        // At the round's own epoch: the ground test differences against a body sample one applied step
        // newer, and the raw position reads the field bodyVelocity*dt away, 548 to 8,051 m in flight.
        // Only the position moves; GroundCentre back-dates the centre, and both would correct twice.
        double3 atOwnEpoch = GroundQueryAt(PositionEcl, -dt);

        _groundSampledAtEcl = atOwnEpoch;
        _groundSampledOverSeconds = dt;

        _haveGround = munition.HitsTerrain && Ground is not null
                      && Ground.TryGround(atOwnEpoch, out _groundCentre, out _groundRadius)
                      && double.IsFinite(_groundRadius) && _groundRadius > 0.0;

        // The frame's own sample is the ground under where its first sub-step begins.
        _radiusBefore = _groundRadius;
        _radiusBeforeFresh = _haveGround;

        int steps = Math.Min(munition.MaxSubSteps, Math.Max(1, (int)Math.Ceiling(dt / munition.SubStep)));
        double h = dt / steps;
        double elapsed = 0.0;

        for (int i = 0; i < steps && State == RoundState.Flying; i++)
        {
            // Per sub-step: a re-entering round crosses a kilometre a frame of air on an 8 km scale
            // height. Back-dated (readWhen <= 0): the body is an end-of-frame sample, and the time
            // into the frame instead reads altitude a frame of ~30 km/s off, 0.9 km at 1x.
            // A second-order round reads half a sub-step on, where its kick belongs.
            double half = SecondOrder ? 0.5 * h : 0.0;
            double3 readAt = PositionEcl + VelocityEcl * half;
            double readWhen = elapsed + half - dt;

            double density = AirDensityAt?.Invoke(readAt, readWhen) ?? mediumDensityRatio;
            if (!double.IsFinite(density) || density < 0.0) density = mediumDensityRatio;
            _lastDensity = density;

            // Re-read per sub-step when the caller offers it, back-dated exactly as the air is.
            double3 pull = GravityAt?.Invoke(readAt, readWhen) ?? gravity;
            if (!Vec.IsFinite(pull)) pull = gravity;

            // Gated on the round, not on the lookup being supplied: the gun's shells share these
            // fields, and their lead is solved by a model that would then disagree with them.
            if (AirVelocityAtOwnSubStep
                && AirVelocityAt?.Invoke(readAt, readWhen) is { } moving && Vec.IsFinite(moving))
            {
                _frameVelocityEcl = moving;
            }

            Step(h, elapsed, dt, target, pull, munition, density);

            // Incremented after the step, so the round's position and the back-dated target share
            // an instant. Splitting them across a sub-step costs ~142 m at 29.8 km/s.
            elapsed += h;
        }

        // After the step, against this frame's platform sample, no extrapolation: both terms then
        // advance in lockstep and the difference is the round's own flight.
        OffsetFromPlatform = PositionEcl - platformEcl;

        _trailTimer += dt;
        if (_trailTimer >= TrailIntervalSeconds)
        {
            _trailTimer = 0.0;
            _trail.Add(OffsetFromPlatform);
            if (_trail.Count > TrailCapacity) _trail.RemoveAt(0);
        }

        Reap(munition, dt);
    }

    private void Step(double h, double elapsedInFrame, double frameSeconds, TargetState? target,
                      double3 gravity, MunitionProfile munition, double mediumDensityRatio)
    {
        Age += h;

        double3 localVelocity = VelocityEcl - _frameVelocityEcl;

        double3 accel = Medium.Buoyancy(gravity, munition, mediumDensityRatio) + Thrust(localVelocity, munition)
                        + (EjectAccelerationEcl * Ejector.ShareOfStep(Age - h, h, EjectSeconds));

        double3 dragAt = localVelocity;
        if (DragAtMidpointVelocity && SecondOrder)
        {
            double3 first = accel - Medium.Drag(localVelocity, munition, mediumDensityRatio)
                            - Medium.ChuteDrag(localVelocity, munition, mediumDensityRatio, Age);
            dragAt = localVelocity + first * (0.5 * h);
        }

        double3 drag = Medium.Drag(dragAt, munition, mediumDensityRatio)
                       + Medium.ChuteDrag(dragAt, munition, mediumDensityRatio, Age);
        accel -= drag;

        // Both terms differenced here: the aim's velocity carries the planet's ~29.8 km/s, which
        // steering on VelocityEcl alone reads as closing speed. Back-dated to this sub-step as
        // Interceptor does, or half a kilometre a frame leaks into the range vector.
        SteeringCommandEcl = Vec.Zero;
        if (munition.SteersItsFall && target is { } aim)
        {
            double3 aimPos = InFrame.AtBurst(aim.PositionEcl, aim.VelocityEcl, elapsedInFrame - frameSeconds);

            SteeringCommandEcl = TailKit.Command(aimPos - PositionEcl, aim.VelocityEcl - VelocityEcl,
                                                 localVelocity, gravity, -drag, munition);
            accel += SteeringCommandEcl;
        }

        // Before proximity: a timed shell bursts then whether or not anything is near.
        if (FuseSeconds > 0.0 && Age >= FuseSeconds)
        {
            double tBurst = Math.Max(0.0, h - (Age - FuseSeconds));

            PositionEcl += VelocityEcl * tBurst;
            DetonationElapsedInFrame = elapsedInFrame + tBurst - frameSeconds;

            if (target is { } fused)
            {
                double3 at = InFrame.AtBurst(fused.PositionEcl, fused.VelocityEcl, DetonationElapsedInFrame);

                MissDistance = Vec.Len(at - PositionEcl);
                ClosestApproach = Math.Min(ClosestApproach, MissDistance);
            }
            else
            {
                // Nothing to be near. Not zero, which would read as a direct hit to anything
                // deciding lethality from it.
                MissDistance = double.PositiveInfinity;
            }

            BurstOnTime = true;
            State = RoundState.Detonated;
            return;
        }

        // Back-dating to this round's epoch: the samples are end-of-frame, the round is mid-step.
        // Without it every separation below carries a whole frame of the planet's motion.
        double backdate = elapsedInFrame - frameSeconds;

        if (target is { } t)
        {
            ClosestApproach = Math.Min(
                ClosestApproach,
                Vec.Len(t.PositionEcl + t.VelocityEcl * backdate - PositionEcl));
        }

        // A shell runs into anything in its way before it has armed, as it runs into the ground; until
        // then its proximity fuse is dead and what it runs into stops it as a dud.
        double trigger = Age >= munition.FuseArmSeconds ? munition.FuseRadius : 0.0;

        {
            bool struck = false;
            double soonest = double.MaxValue;
            double at = 0.0;
            object? hitBody = null;

            // Earliest, not first found: between two bodies a round hits the one it reaches, and
            // list order is whatever the caller's enumeration happened to be.
            void Consider(TargetState body)
            {
                if (!Vec.IsFinite(body.PositionEcl) || !Vec.IsFinite(body.VelocityEcl)) return;

                double3 r = body.PositionEcl + body.VelocityEcl * backdate - PositionEcl;
                double3 v = body.VelocityEcl - VelocityEcl;

                // The designated target already contributes its own; anything else would report
                // the nearest bystander as how close the round came to what it was shooting at.
                if (target is null) ClosestApproach = Math.Min(ClosestApproach, Vec.Len(r));

                if (ContactSweep.TryStrike(r, v, h, trigger, body.Radius,
                                           Hull, body.Handle,
                                           out double when, out double miss)
                    && when < soonest)
                {
                    struck = true;
                    soonest = when;
                    at = miss;
                    hitBody = body.Handle;
                }
            }

            if (target is { } designated) Consider(designated);
            for (int i = 0; i < Contacts.Count; i++) Consider(Contacts[i]);

            if (struck)
            {
                PositionEcl += VelocityEcl * soonest;
                MissDistance = at;
                StruckBody = hitBody;
                ClosestApproach = Math.Min(ClosestApproach, at);

                // Negative: the world sample is end-of-step, so the caller advances the world
                // backward by this much to place the burst.
                DetonationElapsedInFrame = elapsedInFrame + soonest - frameSeconds;
                State = Age - h + soonest >= munition.FuseArmSeconds ? RoundState.Detonated : RoundState.Dud;
                return;
            }
        }

        double3 velocityWas = VelocityEcl;
        VelocityEcl += accel * h;

        // Second order moves on the mean of the two ends, which is the half-drift either side of the
        // kick; first order on the velocity the step ends with.
        double3 stepEcl = (SecondOrder ? (velocityWas + VelocityEcl) * 0.5 : VelocityEcl) * h;
        double3 before = PositionEcl;
        PositionEcl += stepEcl;

        if (_haveGround)
        {
            // The centre is a position moving at ~30 km/s; held for the frame it drifts against the
            // round by bodyVelocity x (frame - elapsed), so it is back-dated. docs/MIRV-NEXT.md 8l.
            double3 centreWas = GroundCentre(elapsedInFrame - frameSeconds);
            double3 centreNow = GroundCentre(elapsedInFrame + h - frameSeconds);

            double radiusWas = _groundRadius;
            double radiusNow = _groundRadius;
            bool reread = false;

            // Near the surface, the ground under both ends of this step rather than the sphere the
            // frame began with, which on a slope is the height of ground the round has left.
            if (ResampleGroundNearImpact
                && Vec.Len(PositionEcl - centreNow) - _groundRadius < GroundResampleBandMetres
                && TryRadiusUnder(PositionEcl, elapsedInFrame + h - frameSeconds, out double under))
            {
                if (!_radiusBeforeFresh)
                {
                    _radiusBeforeFresh = TryRadiusUnder(before, elapsedInFrame - frameSeconds,
                                                        out _radiusBefore);
                }

                radiusWas = _radiusBeforeFresh ? _radiusBefore : _groundRadius;
                radiusNow = under;
                reread = true;

                _radiusBefore = under;
                _radiusBeforeFresh = true;
            }
            else
            {
                _radiusBeforeFresh = false;
            }

            double was = Vec.Len(before - centreWas) - radiusWas;
            double now = Vec.Len(PositionEcl - centreNow) - radiusNow;

            // The height fuse, crossed on the way down. Linear across the step like the ground
            // crossing below; a store already under the height when it arms bursts on the ground.
            double fuseAt = munition.BurstHeightMetres;
            if (fuseAt > 0.0 && Age >= munition.FuseArmSeconds && was > fuseAt && now <= fuseAt)
            {
                double g = Math.Clamp((was - fuseAt) / (was - now), 0.0, 1.0);

                PositionEcl = before + stepEcl * g;
                MissDistance = double.PositiveInfinity;
                BurstAtHeight = true;
                DetonationElapsedInFrame = elapsedInFrame + h * g - frameSeconds;
                State = RoundState.Detonated;
                return;
            }

            if (now <= 0.0)
            {
                double f = Math.Clamp(was > 0.0 ? was / (was - now) : 0.0, 0.0, 1.0);

                // That solves against the chord (StopOnTheTerrain). One query at the crossing and one
                // false-position step; the terrain moves centimetres over it, so a second buys nothing.
                double crossRadius = radiusWas + (radiusNow - radiusWas) * f;

                if (StopOnTheTerrain && reread && was > 0.0)
                {
                    double atCross = elapsedInFrame + h * f - frameSeconds;
                    double3 crossEcl = before + stepEcl * f;

                    if (TryRadiusUnder(crossEcl, atCross, out double there))
                    {
                        double alt = Vec.Len(crossEcl - GroundCentre(atCross)) - there;

                        crossRadius = there;

                        if (Math.Abs(was - alt) > 1e-12)
                        {
                            f = Math.Clamp(f * was / (was - alt), 0.0, 1.0);

                            // Moves nothing; makes GroundRadiusUsed the radius under
                            // GroundSampledAtEcl, which the trace's surface line pairs.
                            if (TryRadiusUnder(before + stepEcl * f,
                                               elapsedInFrame + h * f - frameSeconds,
                                               out double settled))
                            {
                                crossRadius = settled;
                            }
                        }
                    }
                }

                PositionEcl = before + stepEcl * f;
                MissDistance = 0.0;
                HitGround = true;
                DetonationElapsedInFrame = elapsedInFrame + h * f - frameSeconds;
                State = Age - h + (h * f) >= munition.FuseArmSeconds ? RoundState.Detonated : RoundState.Dud;

                // What it stopped against is the ground where it crossed, so that is what the
                // measurements report rather than the frame's first sample.
                if (reread)
                {
                    _groundRadius = crossRadius;
                    _groundSampledAtEcl = GroundQueryAt(PositionEcl, DetonationElapsedInFrame);
                    _groundSampledOverSeconds = -DetonationElapsedInFrame;
                }

                return;
            }
        }

        // Local: absolute displacement carries ~30 km/s of the planet's orbit.
        DistanceFlown += Vec.Len(stepEcl - _frameVelocityEcl * h);
    }

    // The ground under a point the round passed at a stated time into the frame, back-dated exactly
    // as the frame's first sample is in Update.
    private bool TryRadiusUnder(double3 positionEcl, double secondsIntoFrame, out double radius)
    {
        radius = double.NaN;
        if (Ground is not { } ground) return false;

        double3 atOwnEpoch = GroundQueryAt(positionEcl, secondsIntoFrame);
        return ground.TryGround(atOwnEpoch, out _, out radius) && double.IsFinite(radius) && radius > 0.0;
    }
}
