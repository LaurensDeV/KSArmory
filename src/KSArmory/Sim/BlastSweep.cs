using Brutal.Numerics;

namespace KSArmory;

/// <summary>What a warhead did to one body near it.</summary>
internal enum BlastEffect
{
    /// <summary>Outside the blast entirely.</summary>
    Untouched,

    /// <summary>Inside the blast and outside the lethal radius: it survives, and is worth saying so.</summary>
    NearMiss,

    /// <summary>Inside the lethal radius.</summary>
    Lethal,
}

/// <summary>
/// How near a burst a body was, and what that does to it.
///
/// <para>Shared by the sweep over craft and the one over rounds, so they cannot measure differently. A
/// shell that killed by touching is judged on the fuse's own separation instead.</para>
/// </summary>
internal static class BlastSweep
{
    /// <summary>
    /// The gap between a burst and a body's <b>surface</b>, with both taken at the burst's own
    /// instant.
    ///
    /// <para>The body was sampled at the step's end and the round burst part-way through it, so the body
    /// is carried back by <paramref name="sinceSample"/> on its own velocity. The surface rather than the
    /// centre, because <paramref name="meanRadius"/> is a craft's half-diagonal.</para>
    /// </summary>
    public static double SurfaceGap(double3 sampledPositionEcl, double3 velocityEcl,
                                    double sinceSample, double3 burstEcl, double meanRadius)
        => Vec.Len(InFrame.AtBurst(sampledPositionEcl, velocityEcl, sinceSample) - burstEcl) - meanRadius;

    /// <summary>
    /// Where the ground a burst went off on is at the world sample: the burst carried by that ground's
    /// velocity over the rest of the step. <paramref name="sinceSample"/> is negative; anything anchored
    /// to the body at the sample starts from here, or sits up to a step of the planet's ~30 km/s off.
    /// </summary>
    public static double3 GroundAtSample(double3 burstEcl, double3 groundVelocityEcl, double sinceSample)
        => InFrame.AtSample(burstEcl, groundVelocityEcl, sinceSample);

    /// <summary>
    /// What a gap that size means for this warhead.
    ///
    /// <para>The verdict on the whole body; which parts a burst breaks is <see cref="BlastDamage"/>'s.</para>
    /// </summary>
    public static BlastEffect Effect(double gap, MunitionProfile munition)
    {
        ArgumentNullException.ThrowIfNull(munition);

        if (gap <= munition.LethalRadius) return BlastEffect.Lethal;

        return gap <= munition.BlastRadius ? BlastEffect.NearMiss : BlastEffect.Untouched;
    }
}
