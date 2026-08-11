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

    public RoundState State { get; private set; } = RoundState.Flying;
    public int Tube { get; }
    public double Age { get; private set; }

    public double3 PositionEcl { get; private set; }
    public double3 VelocityEcl { get; private set; }
    public double3 OffsetFromPlatform { get; private set; }
    private double3 LaunchOffset { get; }
    public double3 TravelSinceLaunch => OffsetFromPlatform - LaunchOffset;
    public double3 VelocityLocal => VelocityEcl - _frameVelocityEcl;
    public double Speed => Vec.Len(VelocityLocal);
    public double DistanceFlown { get; private set; }
    public IReadOnlyList<double3> TrailOffsets => _trail;
    public double3 LaunchAnchorPartFrame { get; set; }

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

    /// <summary>True when it was the ground that stopped this round rather than a body or a fuse.</summary>
    public bool HitGround { get; private set; }

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

    public void Update(double dt, TargetState? target, double3 gravity, double3 frameVelocityEcl,
                       double3 platformEcl, MunitionProfile munition, double mediumDensityRatio = 1.0)
    {
        if (State != RoundState.Flying) return;
        if (!double.IsFinite(dt) || dt <= 0.0) return;

        _frameVelocityEcl = frameVelocityEcl;

        // Unguided: losing the target leaves it flying with nothing to fuse against.
        if (target is null) TargetRef = null;

        _haveGround = munition.HitsTerrain && Ground is not null
                      && Ground.TryGround(PositionEcl, out _groundCentre, out _groundRadius)
                      && double.IsFinite(_groundRadius) && _groundRadius > 0.0;

        int steps = Math.Min(Interceptor.MaxSubSteps, Math.Max(1, (int)Math.Ceiling(dt / Interceptor.SubStep)));
        double h = dt / steps;
        double elapsed = 0.0;

        for (int i = 0; i < steps && State == RoundState.Flying; i++)
        {
            // Incremented after the step, so the round's position and the back-dated target share
            // an instant. Splitting them across a sub-step costs ~142 m at 29.8 km/s.
            Step(h, elapsed, dt, target, gravity, munition, mediumDensityRatio);
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
        // Buoyancy: a round denser than its medium still sinks, one at its neutral density
        // neither sinks nor rises. Zero disables it, so nothing that flies only in air changes.
        double3 accel = munition.NeutralDensityRatio > 0f
            ? gravity * (1.0 - mediumDensityRatio / munition.NeutralDensityRatio)
            : gravity;

        // Drag on airspeed, scaled by density. No thrust term: a slug coasts from the muzzle.
        double airspeed = Vec.Len(localVelocity);
        if (munition.DragK > 0f && airspeed > 1e-6 && mediumDensityRatio > 0.0)
        {
            accel -= localVelocity * (munition.DragK * airspeed * mediumDensityRatio);
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
            double was = Vec.Len(before - _groundCentre) - _groundRadius;
            double now = Vec.Len(PositionEcl - _groundCentre) - _groundRadius;

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
