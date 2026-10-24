using Brutal.Numerics;

namespace KSArmory;

/// <summary>Where a round the ground stops has got to, as the thing that decides what reaps it.</summary>
internal enum Approach
{
    /// <summary>
    /// Nothing could be read about it — no body, no ceiling, or a trajectory this does not answer.
    /// The age limit is the reaper, exactly as it is for a round nothing stops.
    /// </summary>
    Unknown,

    /// <summary>It can never reach the ground, and nothing else will ever reap it.</summary>
    Impossible,

    /// <summary>
    /// On its way but not yet where arriving happens. A coast is not the round being stuck, so the
    /// clock is held.
    /// </summary>
    Coasting,

    /// <summary>
    /// Where arriving happens, on a path this cannot vouch for ending on the ground, so the clock runs.
    /// </summary>
    Arriving,

    /// <summary>
    /// Where arriving happens, on a path that can only end on the ground. The ground is the reaper, so the
    /// clock is held.
    /// </summary>
    Landing,
}

/// <summary>
/// Whether a round the ground stops can still get to it, and whether it is getting there yet.
///
/// <para>A long fall is long, not stuck (a B61 from 250 km takes 246 s), so what is reaped is the store
/// that can <em>never</em> arrive: a conic whose periapsis is above the ceiling. Exact there, since
/// there is no drag, and drag only ever lowers a periapsis.</para>
///
/// <para>Inside the ceiling, a conic whose periapsis is under the lowest ground can only land, and
/// runs no clock. <see cref="Approach.Arriving"/> is geometric rather than atmospheric, so an airless
/// body still has a clock that can run. CLAUDE.md, "A store that falls is reaped on the fall".</para>
/// </summary>
internal static class RoundReach
{
    /// <param name="ceilingRadius">
    /// The top of the atmosphere, or the highest ground on an airless body. Not the mean radius: a
    /// conic that clears it can still meet a mountain, and this decides whether to destroy a round.
    /// </param>
    /// <param name="floorRadius">
    /// A radius the ground is never below, or zero where that cannot be read, which never answers
    /// <see cref="Approach.Landing"/>.
    /// </param>
    public static Approach Classify(double mu, double3 positionCci, double3 velocityCci,
                                    double ceilingRadius, double floorRadius = 0.0)
    {
        if (!(ceilingRadius > 0.0) || !Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci))
        {
            return Approach.Unknown;
        }

        if (Vec.Len(positionCci) <= ceilingRadius)
        {
            return EndsOnTheGround(mu, positionCci, velocityCci, floorRadius) ? Approach.Landing : Approach.Arriving;
        }

        double periapsis = Kepler.PeriapsisRadius(mu, positionCci, velocityCci);

        // NaN covers an open trajectory and a purely radial one, which want opposite verdicts.
        if (!double.IsFinite(periapsis)) return Approach.Unknown;

        return periapsis <= ceilingRadius ? Approach.Coasting : Approach.Impossible;
    }

    // A round already under the floor has gone through ground that could not be read, and only the clock
    // will end it.
    private static bool EndsOnTheGround(double mu, double3 positionCci, double3 velocityCci, double floorRadius)
    {
        if (!(floorRadius > 0.0) || Vec.Len(positionCci) <= floorRadius) return false;

        double periapsis = Kepler.PeriapsisRadius(mu, positionCci, velocityCci);
        return double.IsFinite(periapsis) && periapsis <= floorRadius;
    }
}
