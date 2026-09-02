using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// An unguided kinetic round — a gun slug. Ballistics and a contact fuse, nothing else.
///
/// <para>Not an <see cref="Interceptor"/> with its nav constant zeroed: it has no seeker, lock,
/// boost, fins or command link, so most of that flight model would be dead code behind guards.</para>
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
    ///
    /// <para>Exactly <see cref="AirDensityAt"/>'s shape and convention, and for the same reason: the
    /// celestial sample the pull is measured from arrives at the frame's end while the round is
    /// part-way through it. A delegate lets the caller put the body back <em>per sub-step</em>
    /// instead of once per frame — and, because the caller composes it, whatever else is folded into
    /// gravity travels with it rather than being overwritten.</para>
    /// </summary>
    public Func<double3, double, double3>? GravityAt { get; set; }

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
    public double Age { get; private set; }

    public double3 PositionEcl { get; private set; }
    public double3 VelocityEcl { get; private set; }
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

    public double3 VelocityLocal => VelocityEcl - _frameVelocityEcl;
    public double Speed => Vec.Len(VelocityLocal);
    public double DistanceFlown { get; private set; }
    public IReadOnlyList<double3> TrailOffsets => _trail;
    public double3 LaunchAnchorPartFrame { get; set; }

    /// <inheritdoc />
    public double3 ReleaseHeadingEcl { get; set; }

    /// <inheritdoc />
    public doubleQuat LaunchAttitude { get; set; }

    /// <inheritdoc cref="IProjectile.Munition"/>
    public required MunitionProfile Munition { get; init; }

    public object? TargetRef { get; private set; }

    /// <summary>
    /// Everything this round can run into, refreshed by the caller each frame.
    ///
    /// <para>A shell does not know what it was fired at. Fusing only against a designated target
    /// means a hand-aimed burst passes clean through a hull and expires, and a commanded one flies
    /// through a bystander untouched — so what a gun hits would depend on what fire control
    /// happened to be thinking about, which is not a property a kinetic round has.</para>
    ///
    /// <para>World samples, end-of-frame: <see cref="Step"/> back-dates them to the round's own
    /// epoch, the same way it does the designated target.</para>
    /// </summary>
    public IReadOnlyList<TargetState> Contacts { get; set; } = [];

    /// <summary>
    /// What decides whether this round actually met a body, supplied by the caller each frame.
    ///
    /// <para>Null in a world with no geometry to ask — the test project is one — and then the
    /// target's bounding sphere is what decides it.</para>
    ///
    /// <para>Deliberately not on <see cref="IProjectile"/>. Requiring a strike is what makes this
    /// round kinetic; a proximity-fused warhead must keep bursting near things, and putting the
    /// hook on the interface is an invitation to wire it into one.</para>
    /// </summary>
    public IHullTest? Hull { get; set; }

    /// <inheritdoc cref="IProjectile.StruckBody"/>
    public object? StruckBody { get; private set; }

    /// <summary>
    /// Where the ground is, supplied by the caller for a round the terrain stops.
    ///
    /// <para>Null for everything aimed upwards, which is every other round in the arsenal: a shell
    /// passes through a hill because nothing has ever asked where the hill was, and paying for that
    /// answer across a 150-shell burst buys nothing. A bomb has no other way to arrive.</para>
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

    /// <summary>True when it was the ground that stopped this round rather than a body or a fuse.</summary>
    public bool HitGround { get; private set; }

    /// <summary>
    /// The surface radius the crossing was last tested against, and whether there was one. Sampled
    /// once per frame at the round's own position, so it is up to a frame of ground stale by the
    /// time a sub-step crosses it — which is what <c>docs/MIRV-NEXT.md</c> item 8k is measuring.
    /// </summary>
    public double GroundRadiusUsed => _haveGround ? _groundRadius : double.NaN;

    /// <summary>
    /// Where the round was standing when it last sampled the ground, which is not where it stops.
    ///
    /// <para><b>Measurement only.</b> The radius is held for a whole frame, so the round stops
    /// against a surface read some distance back along its own track — and the height field's
    /// difference across that distance is what the round's stopping height is wrong by. Flown at
    /// 12,902 km the rounds stopped 13 to 174 m off the true surface, and that error times
    /// <c>cot(gamma)</c> is the whole of their walk from the release probe at r = 0.99.</para>
    ///
    /// <para>Two things displace it and the log cannot separate them without this: the round's own
    /// travel over the ground within the frame, and the frame-newer body sample
    /// <c>Ksa/GroundTest.cs</c> differences against — see <c>docs/KSA-FRAME-ORDER.md</c> section 5.
    /// </para>
    /// </summary>
    public double3 GroundSampledAtEcl => _groundSampledAtEcl;

    /// <inheritdoc cref="GroundSampledAtEcl"/>
    public double GroundSampledOverSeconds => _groundSampledOverSeconds;

    /// <summary>
    /// The round's own view of how high it ended: its final position against the centre AND radius
    /// it tested the crossing with. Near zero means the crossing landed where it meant to, so any
    /// disagreement with an altitude measured against a freshly sampled centre is the centre, not
    /// the round. <c>docs/MIRV-NEXT.md</c> item 8k.
    /// </summary>
    public double StopAltitudeAgainstOwnGround =>
        _haveGround ? Vec.Len(PositionEcl - GroundCentre(DetonationElapsedInFrame)) - _groundRadius
                    : double.NaN;

    /// <inheritdoc cref="IProjectile.Aimpoint"/>
    public Aimpoint Aimpoint { get; set; }

    /// <summary>Always false. A slug is not steered, so it has nothing to lose lock on.</summary>
    public bool HasLock => false;

    /// <summary>Always true. There is no seeker to be blinded, so nothing gates its flight.</summary>
    public bool SeekerInView => true;

    /// <summary>
    /// Time of flight this shell was fused for, or zero for none.
    ///
    /// <para>Set at the trigger from the lead solution, so it bursts where the target is going
    /// rather than where it was.</para>
    /// </summary>
    public double FuseSeconds { get; set; }

    /// <summary>
    /// True when the timed fuse fired rather than the proximity one.
    ///
    /// <para>Recorded because the two are indistinguishable from the outside: a burst is a burst,
    /// and "did the flak setting do anything" is otherwise a judgement call made by eye at a
    /// kilometre.</para>
    /// </summary>
    public bool BurstOnTime { get; private set; }

    public double MissDistance { get; private set; }
    public double ClosestApproach { get; private set; } = double.MaxValue;
    public double DetonationElapsedInFrame { get; private set; }

    /// <summary>No fins to deploy. Full span from the moment it exists.</summary>
    public double FinDeployment(MunitionProfile munition) => 1.0;

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

        _groundSampledAtEcl = PositionEcl;
        _groundSampledOverSeconds = dt;

        _haveGround = munition.HitsTerrain && Ground is not null
                      && Ground.TryGround(PositionEcl, out _groundCentre, out _groundRadius)
                      && double.IsFinite(_groundRadius) && _groundRadius > 0.0;

        int steps = Math.Min(munition.MaxSubSteps, Math.Max(1, (int)Math.Ceiling(dt / munition.SubStep)));
        double h = dt / steps;
        double elapsed = 0.0;

        for (int i = 0; i < steps && State == RoundState.Flying; i++)
        {
            // Re-read inside the loop, because air density is the one thing the round flies
            // through that changes materially within a frame. It falls off on an 8 km scale
            // height, and a re-entering round covers a kilometre a frame at ordinary speeds and
            // more under warp, so holding the frame's first sample for the whole frame flies the
            // round through the thinner air it had at the top of it. Measured against a 1 ms
            // reference on a 2,700 km deorbit: a 170 ms frame lands 510 m long sampling once and
            // 249 m sampling per sub-step, and a 320 ms frame 1,046 m against 550 m.
            // Back-dated, like every other sample this round is measured against: the body it is
            // differenced from was sampled at the end of the frame, and the round is part-way
            // through it. Passing the time *into* the frame instead offsets the lookup by a whole
            // frame of the planet's ~30 km/s -- 0.9 km at normal speed and 3.9 km at eight times,
            // read as altitude, on air that falls off over 8 km. That makes the error grow with the
            // step and jump when the step changes, which is what a warp change does mid-flight.
            double density = AirDensityAt?.Invoke(PositionEcl, elapsed - dt) ?? mediumDensityRatio;
            if (!double.IsFinite(density) || density < 0.0) density = mediumDensityRatio;
            _lastDensity = density;

            // Incremented after the step, so the round's position and the back-dated target share
            // an instant. Splitting them across a sub-step costs ~142 m at 29.8 km/s.
            // Re-read per sub-step when the caller offers it, back-dated exactly as the air is.
            double3 pull = GravityAt?.Invoke(PositionEcl, elapsed - dt) ?? gravity;
            if (!Vec.IsFinite(pull)) pull = gravity;

            Step(h, elapsed, dt, target, pull, munition, density);
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

        if (State == RoundState.Flying && Age >= munition.MaxFlightSeconds) State = RoundState.Expired;
    }

    private void Step(double h, double elapsedInFrame, double frameSeconds, TargetState? target,
                      double3 gravity, MunitionProfile munition, double mediumDensityRatio)
    {
        Age += h;

        double3 localVelocity = VelocityEcl - _frameVelocityEcl;

        // No thrust term between them: a slug coasts from the muzzle.
        double3 accel = Medium.Buoyancy(gravity, munition, mediumDensityRatio);
        accel -= Medium.Drag(localVelocity, munition, mediumDensityRatio);

        // A guided tail kit: fin authority on a fall, not a motor. Same navigation law the
        // missiles use, so a bomb leads a point on a turning planet for the same reason a round
        // leads a crossing aircraft - and clamped by the profile to a few g rather than thirty.
        //
        // Both terms are differenced here rather than upstream: the target is a place on the
        // ground, so its velocity is the planet's ~29.8 km/s plus its spin, and steering on
        // VelocityEcl alone would read that whole frame as closing speed and pull full lateral g
        // across it. SampleTarget resamples it every frame for the same reason.
        //
        // And the sample is back-dated to the instant this sub-step is at, exactly as Interceptor
        // does it. The sample arrives having already moved across the whole frame, so pairing it
        // with a mid-step position leaks that motion into the range vector -- half a kilometre a
        // frame, which the steering then reads as the target sliding sideways.
        SteeringCommandEcl = Vec.Zero;
        if (munition.Guidance == GuidanceMode.Inertial && target is { } aim)
        {
            double3 aimPos = aim.PositionEcl + aim.VelocityEcl * (elapsedInFrame - frameSeconds);

            SteeringCommandEcl = Interceptor.GuidanceAccel(aimPos - PositionEcl,
                                                           aim.VelocityEcl - VelocityEcl,
                                                           localVelocity, gravity, munition);
            accel += SteeringCommandEcl;
        }

        // Before proximity: a shell fused for a time bursts then, whether or not anything is near,
        // which is the whole point of flak. A live target still gets a miss distance measured at
        // the burst, so lethality is decided the same way as any other detonation.
        if (FuseSeconds > 0.0 && Age >= FuseSeconds)
        {
            double tBurst = Math.Max(0.0, h - (Age - FuseSeconds));

            PositionEcl += VelocityEcl * tBurst;
            DetonationElapsedInFrame = elapsedInFrame + tBurst - frameSeconds;

            if (target is { } fused)
            {
                double3 at = fused.PositionEcl
                             + fused.VelocityEcl * (elapsedInFrame + tBurst - frameSeconds);

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

        if (Age >= munition.FuseArmSeconds)
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

                if (ContactSweep.TryStrike(r, v, h, munition.FuseRadius, body.Radius,
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
                State = RoundState.Detonated;
                return;
            }
        }

        VelocityEcl += accel * h;

        double3 stepEcl = VelocityEcl * h;
        double3 before = PositionEcl;
        PositionEcl += stepEcl;

        if (_haveGround)
        {
            // The radius is a property of the ground and keeps for the frame; the centre is a
            // POSITION, and the body it names moves at ~30 km/s while the round carries the same.
            // Sampled once at the frame's end and held, it drifts against the round by
            // bodyVelocity x (frame - elapsed) -- flown at 248-412 m of stop height, which at a
            // 13.8 deg arrival is 1.0-1.7 km of ground. Back-dated exactly like the density lookup
            // above, and by the caller for the same reason: the round is handed a vector and knows
            // nothing about bodies. docs/MIRV-NEXT.md item 8l.
            double3 centreWas = GroundCentre(elapsedInFrame - frameSeconds);
            double3 centreNow = GroundCentre(elapsedInFrame + h - frameSeconds);

            double was = Vec.Len(before - centreWas) - _groundRadius;
            double now = Vec.Len(PositionEcl - centreNow) - _groundRadius;

            if (now <= 0.0)
            {
                // Back to where it crossed, so the burst is on the surface rather than up to a
                // sub-step underneath it. Linear across the step: the round's own drop over 1.5 m
                // is not where the curvature lives.
                double f = was > 0.0 ? was / (was - now) : 0.0;

                PositionEcl = before + stepEcl * Math.Clamp(f, 0.0, 1.0);
                MissDistance = 0.0;
                HitGround = true;
                DetonationElapsedInFrame = elapsedInFrame + h * Math.Clamp(f, 0.0, 1.0) - frameSeconds;
                State = RoundState.Detonated;
                return;
            }
        }

        // Local, not absolute: absolute displacement reports ~30 km per second of the planet's
        // orbit regardless of what the round did.
        DistanceFlown += Vec.Len(stepEcl - _frameVelocityEcl * h);
    }
}
