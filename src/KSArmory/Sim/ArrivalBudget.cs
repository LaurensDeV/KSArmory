using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The steepest arrival this stack can actually pay for, which is what turns
/// <see cref="IcbmConfig.MinArrivalAngleDeg"/> from a number an operator guesses at into one they
/// can see the limit of.
///
/// <para>Arrival angle is the dominant precision lever — <c>docs/ARRIVAL-ANGLE.md</c> prices 7.5 to
/// 20 degrees at eight times the velocity sensitivity and sixty-two times the immunity to a
/// drag-model error — and it is bought with propellant. Asking for one the stack cannot fly is not
/// refused: the shot goes anyway at whatever it can afford and says so afterwards. Knowing the
/// ceiling <em>before</em> the launch is a different thing, and this is it.</para>
///
/// <para><b>It is a search, and the caller pays for it.</b> Each probe is one cheapest-arc solve —
/// the same call guidance makes several times a second — so a bisection is a handful of them.
/// Cheap enough to run on a slow cadence, far too dear to run per frame, which is why nothing here
/// caches or schedules: whoever calls it decides how often.</para>
/// </summary>
internal static class ArrivalBudget
{
    /// <summary>Nothing steeper is worth offering: past this an arc is a vertical drop.</summary>
    public const double SteepestConsideredDeg = 80.0;

    /// <summary>
    /// How finely the ceiling is resolved, in degrees.
    ///
    /// <para>Half a degree is well under what the lever is worth — the trade turns over a span of
    /// several degrees — and every halving is another solve.</para>
    /// </summary>
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

        // A floor of zero is the unconstrained shot. If that is unaffordable no angle is, and the
        // answer is a number rather than a refusal: the operator is short of propellant, which the
        // reach readout already says in its own words.
        if (!Affordable(body, positionCci, velocityCci, aimCci, 0.0, loft, availableMetresPerSecond))
        {
            return CanSolve(body, positionCci, velocityCci, aimCci, 0.0, loft) ? 0.0 : double.NaN;
        }

        // Bisected rather than stepped, and on affordability rather than on cost. Cost is not
        // monotonic in the floor over the whole range -- a steeper arc can be cheaper than a
        // marginally shallower one where the flight-time search jumps families -- but *affordable*
        // is what the operator is choosing between, and taking the boundary is the honest reading
        // of a ceiling either way.
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

    // Leaving now, which is the question a launch asks. A vehicle in orbit has a window search for
    // when to go, and that search takes the floor as a constraint of its own -- so what belongs
    // here is the immediate cost, not a second opinion about the departure.
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
