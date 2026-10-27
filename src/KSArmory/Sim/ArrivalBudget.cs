using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The steepest arrival this stack can actually pay for, which is what turns
/// <see cref="IcbmConfig.MinArrivalAngleDeg"/> from a number an operator guesses at into one they
/// can see the limit of.
///
/// <para>A steeper arrival is bought with propellant (<c>docs/ARRIVAL-ANGLE.md</c>), and one the stack
/// cannot fly is flown at what it can afford; this is the ceiling known before the launch.</para>
///
/// <para><b>It is a search, and the caller pays for it</b>: a cheapest-arc solve per probe, too dear per
/// frame, and nothing here caches or schedules.</para>
/// </summary>
internal static class ArrivalBudget
{
    /// <summary>Nothing steeper is worth offering: past this an arc is a vertical drop.</summary>
    public const double SteepestConsideredDeg = 80.0;

    /// <summary>How finely the ceiling is resolved, in degrees; every halving is another solve.</summary>
    public const double ResolutionDeg = 0.5;

    /// <summary>
    /// The steepest arrival that costs no more than <paramref name="availableMetresPerSecond"/>, or
    /// NaN when even a free arrival cannot be solved from here.
    ///
    /// <para>Zero is a real answer and means the stack cannot afford <em>any</em> arc to that
    /// target — the same state the reach assessment calls
    /// <see cref="IcbmReach.ShortOfPropellant"/>, not a failure to search.</para>
    /// </summary>
    public static double SteepestAffordableDeg(BallisticBody body, double3 positionCci,
                                               double3 velocityCci, double3 aimCci,
                                               double availableMetresPerSecond, double loft = 1.0)
    {
        if (!body.IsUsable) return double.NaN;
        if (!Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci) || !Vec.IsFinite(aimCci))
        {
            return double.NaN;
        }

        if (!(availableMetresPerSecond > 0.0) || !double.IsFinite(availableMetresPerSecond))
        {
            return double.NaN;
        }

        // A floor of zero is the unconstrained shot; if that is unaffordable no angle is.
        if (!Affordable(body, positionCci, velocityCci, aimCci, 0.0, loft, availableMetresPerSecond))
        {
            return CanSolve(body, positionCci, velocityCci, aimCci, 0.0, loft) ? 0.0 : double.NaN;
        }

        // Bisected on affordability, not cost: cost is not monotonic in the floor where the
        // flight-time search jumps families.
        double lo = 0.0;
        double hi = SteepestConsideredDeg;

        while (hi - lo > ResolutionDeg)
        {
            double mid = 0.5 * (lo + hi);

            if (Affordable(body, positionCci, velocityCci, aimCci, mid, loft, availableMetresPerSecond))
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static bool Affordable(BallisticBody body, double3 positionCci, double3 velocityCci,
                                   double3 aimCci, double minArrivalDeg, double loft,
                                   double availableMetresPerSecond)
    {
        return Cost(body, positionCci, velocityCci, aimCci, minArrivalDeg, loft)
               <= availableMetresPerSecond;
    }

    private static bool CanSolve(BallisticBody body, double3 positionCci, double3 velocityCci,
                                 double3 aimCci, double minArrivalDeg, double loft)
    {
        return double.IsFinite(Cost(body, positionCci, velocityCci, aimCci, minArrivalDeg, loft));
    }

    // Leaving now: a vehicle in orbit has its own window search, which takes the floor itself.
    private static double Cost(BallisticBody body, double3 positionCci, double3 velocityCci,
                               double3 aimCci, double minArrivalDeg, double loft)
    {
        if (!BallisticArc.TryCheapest(body, positionCci, velocityCci, aimCci, out BallisticArc.Solution arc,
                                      loft, false, double.NaN, minArrivalDeg))
        {
            return double.PositiveInfinity;
        }

        return Vec.Len(arc.RequiredVelocityCci - velocityCci);
    }
}
