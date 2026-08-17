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
/// <para>Shared by the two sweeps a burst runs — over craft, and over other rounds in the air —
/// which differ in what they <em>do</em> about an answer but must not differ in how they measure
/// it. A shell that killed by touching is judged on the fuse's own separation instead; this is
/// what a warhead does to everything it did not touch.</para>
/// </summary>
internal static class BlastSweep
{
    /// <summary>
    /// The gap between a burst and a body's <b>surface</b>, with both taken at the burst's own
    /// instant.
    ///
    /// <para><paramref name="sinceSample"/> is what pairs them. The body's position was sampled
    /// before the round finished its step, so comparing it to the burst as it stands measures one
    /// step of the closing motion and calls it a miss distance — carrying it forward by its own
    /// velocity is what puts the two on one clock. Both terms are ecliptic and the answer is a
    /// difference, so the 29.8 km/s they share cancels and never reaches the result.</para>
    ///
    /// <para>The surface rather than the centre, because <paramref name="meanRadius"/> is a
    /// craft's half-diagonal: a warhead going off against the skin of a long booster is metres
    /// from it and a hundred metres from the point the engine calls its position.</para>
    /// </summary>
    public static double SurfaceGap(double3 sampledPositionEcl, double3 velocityEcl,
                                    double sinceSample, double3 burstEcl, double meanRadius)
        => Vec.Len(sampledPositionEcl + (velocityEcl * sinceSample) - burstEcl) - meanRadius;

    /// <summary>
    /// What a gap that size means for this warhead.
    ///
    /// <para>Both radii come off the one charge, so they cannot be set into a state where the
    /// lethal radius is the larger — see <see cref="Warhead"/>. Kills are binary because KSA has
    /// no damage model below destruction, which is why a near miss is a thing to announce rather
    /// than a thing to apply.</para>
    /// </summary>
    public static BlastEffect Effect(double gap, MunitionProfile munition)
    {
        ArgumentNullException.ThrowIfNull(munition);

        if (gap <= munition.LethalRadius) return BlastEffect.Lethal;

        return gap <= munition.BlastRadius ? BlastEffect.NearMiss : BlastEffect.Untouched;
    }
}
