using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where a store released right now would land, and the path it would take getting there.
///
/// <para>Flown rather than solved: the sight steps the <em>same</em> <see cref="Slug"/> the bomb will be,
/// through the same gravity, air and ground, so the pipper goes wherever the round will.</para>
///
/// <para><b>And against a planet that turns</b>, in a frame carried with the ground under the release:
/// gravity and air are read against the drifted centre, the frame's acceleration is taken off the pull,
/// and the landing is carried onto the ground that will be under it. CLAUDE.md, "The bomb sight is
/// flown, not solved", has the flown numbers.</para>
///
/// <para>The caller decides how often it runs.</para>
/// </summary>
internal static class BombSight
{
    /// <summary>Long enough for a drop from any altitude a store is released at.</summary>
    public const int MaxSteps = 2048;

    /// <summary>
    /// The step a store under a parachute is flown at, whatever the sight would otherwise use. A
    /// canopy stretches the fall to minutes -- Tsar Bomba's took 188 s from 10.5 km -- past what
    /// <see cref="MaxSteps"/> reaches at a twentieth of a second, and at the 20-30 m/s it falls there
    /// this is under ten metres of it.
    /// </summary>
    public const double ChuteStepSeconds = 0.30;

    /// <summary>The step to fly <paramref name="munition"/> at, given the one the sight would like.</summary>
    public static double StepFor(MunitionProfile munition, double stepSeconds)
        => munition.HasChute ? Math.Max(stepSeconds, ChuteStepSeconds) : stepSeconds;

    /// <param name="releaseEcl">Where the store would leave, i.e. the tube.</param>
    /// <param name="velocityOverGround">What it would leave with, against the ground under the release.</param>
    /// <param name="groundVelocityEcl">That ground's velocity, which the store's is measured against.</param>
    /// <param name="groundAccelerationEcl">That ground's acceleration: on a world that turns, the spin carrying it round.</param>
    /// <param name="bodyVelocityEcl">The velocity of the body that ground belongs to.</param>
    /// <param name="groundVelocityAt">The ground's velocity under any point, to carry the landing onto the ground that will be there.</param>
    /// <param name="gravityAt">Gravity at a position against the body as sampled. Supplied because only the caller knows the body.</param>
    /// <param name="densityAt">Air density there, as a multiple of reference air, for the drag term.</param>
    /// <param name="ground">Where the surface is. Without one there is nothing to arrive at.</param>
    /// <param name="pathEcl">Filled with the trajectory in the carried frame, release first, impact last.</param>
    /// <param name="impactEcl">The ground it lands on, where that ground is at release.</param>
    /// <param name="inheritedOverGround">
    /// The launching craft's velocity over that ground, which a motor does not push along. Zero for a
    /// store with no motor, which never reads it.
    /// </param>
    /// <param name="steerAtEcl">
    /// A place to steer at, for a store whose kit steers its fall. Null flies the ballistic drop,
    /// which is what a pipper wants. Given one, the flight is the round under its own guidance law
    /// — which is what makes <see cref="TailKitReach"/> able to ask how far the kit can move a
    /// landing by flying it there rather than by reasoning about it. It is a fixed point in the
    /// carried frame, which is what a place on the ground <em>is</em> in this frame.
    /// </param>
    public static bool TryPredict(double3 releaseEcl, double3 velocityOverGround, double3 groundVelocityEcl,
                                  double3 groundAccelerationEcl, double3 bodyVelocityEcl,
                                  Func<double3, double3> groundVelocityAt,
                                  MunitionProfile munition,
                                  Func<double3, double3> gravityAt,
                                  Func<double3, double> densityAt,
                                  IGroundTest? ground,
                                  double stepSeconds,
                                  List<double3> pathEcl, out double3 impactEcl,
                                  double3? steerAtEcl = null, double startAge = 0.0,
                                  double3 inheritedOverGround = default)
    {
        pathEcl.Clear();
        impactEcl = default;

        if (!Vec.IsFinite(releaseEcl) || !Vec.IsFinite(velocityOverGround)) return false;
        if (!Vec.IsFinite(groundVelocityEcl) || !Vec.IsFinite(groundAccelerationEcl) || !Vec.IsFinite(bodyVelocityEcl))
        {
            return false;
        }

        if (!double.IsFinite(stepSeconds) || stepSeconds <= 0.0) return false;
        if (ground is null) return false;

        var frame = new CarriedFrame(groundVelocityEcl - bodyVelocityEcl, groundAccelerationEcl);
        var carried = new CarriedGround(ground, frame);

        // Its own lookups are back-dated into the step, so they are carried to their own instant. At the
        // store's age, so a canopy already open and a fuse already armed stay so.
        Slug shot = new(releaseEcl, velocityOverGround, null, 0, releaseEcl, Vec.Zero)
        {
            Age = Math.Max(startAge, 0.0),
            Munition = munition,
            LaunchFrameVelocityLocal = inheritedOverGround,
            Ground = carried,
            GravityAt = (p, intoStep) => gravityAt(p + frame.Drift(carried.Seconds + intoStep)) - groundAccelerationEcl,
            AirDensityAt = (p, intoStep) => densityAt(p + frame.Drift(carried.Seconds + intoStep)),
        };

        pathEcl.Add(releaseEcl);

        // Fixed: a place on the ground does not move in a frame carried with the ground.
        TargetState? aim = steerAtEcl is { } steer ? new TargetState(steer, Vec.Zero, 0.0) : null;

        for (int i = 0; i < MaxSteps && shot.State == RoundState.Flying; i++)
        {
            carried.Seconds += stepSeconds;

            double3 at = shot.PositionEcl + frame.Drift(carried.Seconds);
            shot.Update(stepSeconds, aim, gravityAt(at) - groundAccelerationEcl, Vec.Zero,
                        releaseEcl, munition, densityAt(at));

            pathEcl.Add(shot.PositionEcl);
        }

        // One that ran out of life was still falling and has no impact point. One that burst at its fuse
        // height marks the ground under the burst.
        if (!shot.HitGround && !shot.BurstAtHeight) return false;

        double3 arrived = shot.PositionEcl;
        if (shot.BurstAtHeight)
        {
            if (!carried.TryGround(arrived, out double3 centre, out double radius)) return false;
            arrived = centre + (Vec.Unit(arrived - centre) * radius);
        }

        // The ground at the landing moves against the release's ground by the spin across the distance
        // between them; the ring marks that ground where it is now.
        double seconds = carried.Seconds + shot.DetonationElapsedInFrame;
        impactEcl = arrived - ((groundVelocityAt(arrived) - groundVelocityEcl) * seconds);
        return Vec.IsFinite(impactEcl);
    }

    // How far the ground under the release has carried the frame round the body's centre, t seconds on.
    private readonly record struct CarriedFrame(double3 Spin, double3 Acceleration)
    {
        public double3 Drift(double t) => (Spin * t) + (Acceleration * (0.5 * t * t));
    }

    // The terrain is asked where the round is in the carried frame, which turns with the ground; only the
    // centre moves. Asked at the drifted point, the height is read 15 km upwind over a half-minute fall.
    private sealed class CarriedGround(IGroundTest inner, CarriedFrame frame) : IGroundTest
    {
        public double Seconds;

        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            bool found = inner.TryGround(positionEcl, out centreEcl, out surfaceRadius);
            centreEcl -= frame.Drift(Seconds);
            return found;
        }
    }
}
