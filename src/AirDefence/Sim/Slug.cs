using Brutal.Numerics;

namespace AirDefence;

/// <summary>
/// An unguided kinetic round — a gun slug. Ballistics and a contact fuse, nothing else.
///
/// <para><b>This is why <see cref="IProjectile"/> exists.</b> A slug is not an
/// <see cref="Interceptor"/> with its nav constant set to zero: it has no seeker, no lock, no
/// boost, no fins, no command link, and it never steers, so most of that flight model is dead code
/// for it and every branch would need a guard. It is a different implementation of the same
/// contract, which is the distinction a profile field cannot draw.</para>
///
/// <para>It still has to obey every frame and epoch rule the guided round does — the target sample
/// arrives at the end of the step and must be back-dated, the drawn offset is taken after the step
/// with no extrapolation, and the body is oriented off local velocity. Those are properties of the
/// engine, not of the weapon. See docs/FRAMES-AND-EPOCHS.md.</para>
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

        // Seeded so the round is orientable on the frame it is fired, which is a frame it is
        // genuinely drawn on. Same trap that pointed missiles along Earth's orbit at launch.
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

    public object? TargetRef { get; private set; }

    /// <summary>Always false. A slug is not steered, so it has nothing to lose lock on.</summary>
    public bool HasLock => false;

    /// <summary>Always true. There is no seeker to be blinded, so nothing gates its flight.</summary>
    public bool SeekerInView => true;

    public double MissDistance { get; private set; }
    public double ClosestApproach { get; private set; } = double.MaxValue;
    public double DetonationElapsedInFrame { get; private set; }

    /// <summary>No fins to deploy. Full span from the moment it exists.</summary>
    public double FinDeployment(MunitionProfile munition) => 1.0;

    public void Update(double dt, TargetState? target, double3 gravity, double3 frameVelocityEcl,
                       double3 platformEcl, MunitionProfile munition, double airDensityRatio = 1.0)
    {
        if (State != RoundState.Flying) return;
        if (!double.IsFinite(dt) || dt <= 0.0) return;

        _frameVelocityEcl = frameVelocityEcl;

        // A slug is unguided, so losing the target does not end its flight - it keeps going and
        // simply has nothing left to fuse against.
        if (target is null) TargetRef = null;

        int steps = Math.Min(Interceptor.MaxSubSteps, Math.Max(1, (int)Math.Ceiling(dt / Interceptor.SubStep)));
        double h = dt / steps;
        double elapsed = 0.0;

        for (int i = 0; i < steps && State == RoundState.Flying; i++)
        {
            // elapsed is incremented AFTER the step, so the round's start-of-sub-step position is
            // paired with the target back-dated to that same instant. Incrementing first pairs it
            // with the END of the sub-step instead, and at 29.8 km/s that half-step is ~142 m of
            // phantom separation. Caught by ProjectileContractTests on this file's first run.
            Step(h, elapsed, dt, target, gravity, munition, airDensityRatio);
            elapsed += h;
        }

        // After the step, against the platform sample from this same frame, with no extrapolation.
        // Both terms then advance in lockstep and the difference is the round's own flight.
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
                      double3 gravity, MunitionProfile munition, double airDensityRatio)
    {
        Age += h;

        double3 localVelocity = VelocityEcl - _frameVelocityEcl;
        double3 accel = gravity;

        // Drag on airspeed, scaled by air density. No thrust term at all: a slug is coasting from
        // the instant it leaves the barrel, which is the whole difference from a boosted round.
        double airspeed = Vec.Len(localVelocity);
        if (munition.DragK > 0f && airspeed > 1e-6 && airDensityRatio > 0.0)
        {
            accel -= localVelocity * (munition.DragK * airspeed * airDensityRatio);
        }

        if (target is { } t)
        {
            // Back-dated to this round's epoch. The sample is where the target will be at the END
            // of the frame; the round is mid-step. Skipping this leaves the line of sight carrying
            // a whole frame of the planet's motion, which is what made guided rounds chase a ghost
            // hundreds of metres away.
            double3 targetPos = t.PositionEcl + t.VelocityEcl * (elapsedInFrame - frameSeconds);
            double3 r = targetPos - PositionEcl;
            double3 v = t.VelocityEcl - VelocityEcl;

            ClosestApproach = Math.Min(ClosestApproach, Vec.Len(r));

            if (Age >= munition.FuseArmSeconds)
            {
                // Analytic closest approach across the sub-step, so a fast slug cannot step over a
                // small target. This is what lets FuseRadius go to zero for a contact hit.
                double trigger = munition.FuseRadius + t.Radius;
                double tCa = Vec.TimeOfClosestApproach(r, v, h);
                double miss = Vec.Len(r + v * tCa);

                if (miss <= trigger)
                {
                    PositionEcl += VelocityEcl * tCa;
                    MissDistance = miss;
                    ClosestApproach = Math.Min(ClosestApproach, miss);

                    // Negative: measured against a world sample taken at the end of the step, so
                    // the caller advances the world BACKWARD by this much to place the burst.
                    DetonationElapsedInFrame = elapsedInFrame + tCa - frameSeconds;
                    State = RoundState.Detonated;
                    return;
                }
            }
        }

        VelocityEcl += accel * h;

        double3 stepEcl = VelocityEcl * h;
        PositionEcl += stepEcl;

        // Local, not absolute: absolute displacement is dominated by the planet's orbit and would
        // report ~30 km per second of flight regardless of what the slug did.
        DistanceFlown += Vec.Len(stepEcl - _frameVelocityEcl * h);
    }
}
