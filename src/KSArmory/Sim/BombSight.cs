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
/// <para>The step is coarser than the round's own and the prediction is not run every frame: it
/// is a few hundred integrations, and nothing about a bomb's fall changes fast enough to notice.
/// The caller decides how often.</para>
/// </summary>
internal static class BombSight
{
    /// <summary>Long enough for a drop from any altitude a store is released at.</summary>
    public const int MaxSteps = 2048;

    /// <param name="releaseEcl">Where the store would leave, i.e. the tube.</param>
    /// <param name="velocityEcl">What it would leave with — the craft's motion plus the ejector.</param>
    /// <param name="frameVelocityEcl">The ground's frame, which is what its airspeed is measured against.</param>
    /// <param name="gravityAt">Gravity at a position. Supplied because only the caller knows the body.</param>
    /// <param name="densityAt">Air density there, as a multiple of sea level, for the drag term.</param>
    /// <param name="ground">Where the surface is. Without one there is nothing to arrive at.</param>
    /// <param name="pathEcl">Filled with the trajectory, release first, impact last.</param>
    /// <param name="impactEcl">Where it lands.</param>
    public static bool TryPredict(double3 releaseEcl, double3 velocityEcl, double3 frameVelocityEcl,
                                  MunitionProfile munition,
                                  Func<double3, double3> gravityAt,
                                  Func<double3, double> densityAt,
                                  IGroundTest? ground,
                                  double stepSeconds,
                                  List<double3> pathEcl, out double3 impactEcl)
    {
        pathEcl.Clear();
        impactEcl = default;

        if (!Vec.IsFinite(releaseEcl) || !Vec.IsFinite(velocityEcl)) return false;
        if (!double.IsFinite(stepSeconds) || stepSeconds <= 0.0) return false;
        if (ground is null) return false;

        // A throwaway round, flown exactly as the real one will be. The tube number is arbitrary:
        // nothing here reaches a magazine.
        Slug shot = new(releaseEcl, velocityEcl, null, 0, releaseEcl, frameVelocityEcl)
        {
            Munition = munition,
            Ground = ground,
        };

        pathEcl.Add(releaseEcl);

        for (int i = 0; i < MaxSteps && shot.State == RoundState.Flying; i++)
        {
            shot.Update(stepSeconds, null, gravityAt(shot.PositionEcl), frameVelocityEcl,
                        releaseEcl, munition, densityAt(shot.PositionEcl));

            pathEcl.Add(shot.PositionEcl);
        }

        // Only a round the ground stopped has an impact point. One that ran out of life was still
        // falling, and drawing a pipper where it happened to be would be an answer to a question
        // nobody asked.
        if (!shot.HitGround) return false;

        impactEcl = shot.PositionEcl;
        return true;
    }
}
