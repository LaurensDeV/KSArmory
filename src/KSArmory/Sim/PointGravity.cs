using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The pull a round is flown with: its body's inverse square, aimed where the body was at the
/// round's own instant, plus the body's own fall toward its primary.
///
/// <para>One composition for every thing this mod integrates, because the two terms are easy to lose
/// one at a time and each costs hundreds of metres. The celestial sample arrives at the step's end
/// while a round is read part-way through it, so a pull aimed at the sample is a step of the body's
/// ~30 km/s out; and a round integrated against its body alone in <c>Ecl</c> is left behind by the
/// fall the ground shares. <c>docs/FRAMES-AND-EPOCHS.md</c> and <c>docs/MIRV-NEXT.md</c> item 2.</para>
/// </summary>
public static class PointGravity
{
    /// <summary>
    /// <c>mu/r²</c> toward <paramref name="centreEcl"/>. Zero for no <paramref name="mu"/>, and inside a
    /// metre of the centre, where the direction is noise.
    /// </summary>
    public static double3 Toward(double3 centreEcl, double mu, double3 positionEcl)
    {
        if (mu <= 0.0) return Vec.Zero;

        double3 toCentre = centreEcl - positionEcl;
        double dist2 = Vec.Len2(toCentre);
        if (dist2 < 1.0) return Vec.Zero;

        return Vec.Unit(toCentre) * (mu / dist2);
    }

    /// <summary>
    /// What a round at <paramref name="positionEcl"/> falls with, <paramref name="secondsIntoFrame"/>
    /// against the body's sample — negative, since the sample is at the step's end.
    /// </summary>
    public static double3 OnRound(double mu, double3 bodySampleEcl, double3 bodyVelocityEcl,
                                  double3 bodyFallEcl, double3 positionEcl, double secondsIntoFrame)
        => Toward(bodySampleEcl + (bodyVelocityEcl * secondsIntoFrame), mu, positionEcl) + bodyFallEcl;
}
