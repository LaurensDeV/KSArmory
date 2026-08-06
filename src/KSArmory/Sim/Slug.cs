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
    public MunitionProfile Munition { get; init; } = Arsenal.Cannon30Mm;

    public object? TargetRef { get; private set; }

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

        if (target is { } t)
        {
            // Back-dated to this round's epoch: the sample is end-of-frame, the round is mid-step.
            // Without it the line of sight carries a whole frame of the planet's motion.
            double3 targetPos = t.PositionEcl + t.VelocityEcl * (elapsedInFrame - frameSeconds);
            double3 r = targetPos - PositionEcl;
            double3 v = t.VelocityEcl - VelocityEcl;

            ClosestApproach = Math.Min(ClosestApproach, Vec.Len(r));

            if (Age >= munition.FuseArmSeconds)
            {
                // Analytic closest approach across the sub-step, so a fast round cannot step over
                // a small target. This is what lets FuseRadius go to zero for a contact hit.
                double trigger = munition.FuseRadius + t.Radius;
                double tCa = Vec.TimeOfClosestApproach(r, v, h);
                double miss = Vec.Len(r + v * tCa);

                if (miss <= trigger)
                {
                    PositionEcl += VelocityEcl * tCa;
                    MissDistance = miss;
                    ClosestApproach = Math.Min(ClosestApproach, miss);

                    // Negative: the world sample is end-of-step, so the caller advances the world
                    // backward by this much to place the burst.
                    DetonationElapsedInFrame = elapsedInFrame + tCa - frameSeconds;
                    State = RoundState.Detonated;
                    return;
                }
            }
        }

        VelocityEcl += accel * h;

        double3 stepEcl = VelocityEcl * h;
        PositionEcl += stepEcl;

        // Local, not absolute: absolute displacement reports ~30 km per second of the planet's
        // orbit regardless of what the round did.
        DistanceFlown += Vec.Len(stepEcl - _frameVelocityEcl * h);
    }
}
