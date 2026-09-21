using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where a store released right now would land, and the path it would take getting there.
///
/// <para>Flown rather than solved. A closed form for a ballistic fall exists only without drag,
/// and this round has drag that grows as it falls into thicker air — so the sight steps the
/// <em>same</em> <see cref="Slug"/> the bomb will actually be, through the same gravity, the same
/// density and the same ground. Whatever the flight model does, the pipper does too, including
/// anything about it that is wrong. A sight derived from a tidier model than the round flies is a
/// sight that lies.</para>
///
/// <para><b>And against a planet that turns.</b> The fall is flown in a frame carried with the ground
/// under the release, against a planet sampled once — so gravity and air are read where the round is
/// against where that ground has carried the frame round the centre, the ground's height where the round
/// is in the frame, which turns with it, the frame's own acceleration is taken off the pull, and the
/// landing is carried back onto the ground that will be under it. Held still, the sight puts the ring
/// tens of metres from where a five-kilometre drop strikes.</para>
///
/// <para>The step is coarser than the round's own and the prediction is rate-limited by its caller: it
/// is MaxSteps integrations, each sub-stepped by the round, and nothing about a bomb's fall changes fast enough to notice.
/// The caller decides how often.</para>
/// </summary>
internal static class BombSight
{
    /// <summary>Long enough for a drop from any altitude a store is released at.</summary>
    public const int MaxSteps = 2048;

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
                                  double3? steerAtEcl = null)
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

        // A throwaway round, flown exactly as the real one will be. The tube number is arbitrary:
        // nothing here reaches a magazine. Its own lookups are back-dated into the step, so they are
        // carried to the instant they belong to rather than to the step's end.
        Slug shot = new(releaseEcl, velocityOverGround, null, 0, releaseEcl, Vec.Zero)
        {
            Munition = munition,
            Ground = carried,
            GravityAt = (p, intoStep) => gravityAt(p + frame.Drift(carried.Seconds + intoStep)) - groundAccelerationEcl,
            AirDensityAt = (p, intoStep) => densityAt(p + frame.Drift(carried.Seconds + intoStep)),
        };

        pathEcl.Add(releaseEcl);

        // Fixed for the whole flight: a place on the ground does not move in a frame carried with
        // the ground.
        TargetState? aim = steerAtEcl is { } steer ? new TargetState(steer, Vec.Zero, 0.0) : null;

        for (int i = 0; i < MaxSteps && shot.State == RoundState.Flying; i++)
        {
            carried.Seconds += stepSeconds;

            double3 at = shot.PositionEcl + frame.Drift(carried.Seconds);
            shot.Update(stepSeconds, aim, gravityAt(at) - groundAccelerationEcl, Vec.Zero,
                        releaseEcl, munition, densityAt(at));

            pathEcl.Add(shot.PositionEcl);
        }

        // Only a round the ground stopped has an impact point. One that ran out of life was still
        // falling, and drawing a pipper where it happened to be would be an answer to a question
        // nobody asked.
        if (!shot.HitGround) return false;

        // Where it lands is a place in the carried frame at the moment it lands. The ground there moves
        // against the release's ground by the spin across the distance between them, and a ring drawn now
        // marks that ground where it is now.
        double seconds = carried.Seconds + shot.DetonationElapsedInFrame;
        impactEcl = shot.PositionEcl - ((groundVelocityAt(shot.PositionEcl) - groundVelocityEcl) * seconds);
        return Vec.IsFinite(impactEcl);
    }

    // How far the ground under the release has carried the frame round the body's centre, t seconds on.
    private readonly record struct CarriedFrame(double3 Spin, double3 Acceleration)
    {
        public double3 Drift(double t) => (Spin * t) + (Acceleration * (0.5 * t * t));
    }

    // The ground, answered in the carried frame. The terrain is asked where the round is in that frame rather than
    // against the body as sampled: the frame turns with the ground, so the ground under a point in it is the ground
    // that was there at release, and only the centre has moved against it. Asked at the carried point instead, the
    // height is read kilometres upwind -- 15 km over a half-minute fall at the equator.
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
