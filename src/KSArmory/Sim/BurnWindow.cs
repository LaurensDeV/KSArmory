using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// <em>When</em> to start burning, which is a separate question from how to fly the burn.
///
/// <para><see cref="BallisticArc"/> answers "what would it cost to leave from here, now". That is
/// the whole question on a launch pad, where waiting achieves nothing. It is not the question in
/// orbit, where the vehicle is being carried round and the cost of a shot swings by orders of
/// magnitude across a single revolution — the cheapest moment to leave may be forty minutes
/// away.</para>
///
/// <para><b>Ignoring that does not produce a worse shot; it produces a wild one.</b> A target the
/// vehicle has just passed over has no affordable arc to it at all: forward the short way means
/// reversing the entire orbital velocity, and the long way round passes through the planet. A
/// search that can only leave now returns the first of those, at eleven kilometres a second, and a
/// computer that believes it burns the tank dry and lands on the wrong continent. Searching over
/// departure time finds the same target for two hundred metres a second, most of a revolution
/// later.</para>
/// </summary>
internal static class BurnWindow
{
    /// <summary>How many departure times are tried before the best is refined.</summary>
    public const int Samples = 32;

    /// <summary>For a trajectory that never comes round, there is no repeat to wait for.</summary>
    public const double UnboundHorizonSeconds = 3600.0;

    /// <summary>What leaving at some future moment would cost, and what leaving now would.</summary>
    internal readonly record struct Window(
        double WaitSeconds,
        BallisticArc.Solution Arc,
        double Cost,
        double CostIfLeavingNow,
        double3 BurnDirectionCci)
    {
        /// <summary>Nothing is gained by waiting, so the burn may as well start.</summary>
        public bool IsNow => WaitSeconds <= 0.0;

        /// <summary>How much of the shot waiting saves. Infinite when leaving now is impossible.</summary>
        public double Saving => CostIfLeavingNow - Cost;
    }

    /// <summary>
    /// The cheapest moment to leave, searched across one revolution.
    ///
    /// <para>One revolution is the natural horizon: past it the geometry repeats, so a longer
    /// search re-examines answers it already has. What that does <em>not</em> cover is waiting
    /// several revolutions for the planet to turn a target under the ground track, which is a
    /// real thing to want and is not built.</para>
    /// </summary>
    public static bool TryFind(BallisticBody body, double3 positionCci, double3 velocityCci,
                               double3 aimNowCci, out Window window, double loft = 1.0)
    {
        window = default;

        if (!body.IsUsable) return false;
        if (!Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci)) return false;

        double horizon = Kepler.PeriodSeconds(body.Mu, positionCci, velocityCci);
        if (!double.IsFinite(horizon) || horizon <= 0.0) horizon = UnboundHorizonSeconds;

        double costNow = CostAt(body, positionCci, velocityCci, aimNowCci, 0.0, loft,
                                double.NaN, out BallisticArc.Solution arcNow,
                                out double3 directionNow, out _);

        double best = costNow;
        double bestWait = 0.0;
        BallisticArc.Solution bestArc = arcNow;
        double3 bestDirection = directionNow;
        double seed = double.IsFinite(costNow) ? arcNow.CheapestFlightSeconds : double.NaN;

        // Kept separately from the running seed. The refinement happens around the best sample, and
        // seeding it with the flight time of the *last* sample searched sends the trajectory solver
        // hunting in a bracket that does not contain the answer — which reads as no trajectory
        // existing at all.
        double bestSeed = seed;

        for (int i = 1; i <= Samples; i++)
        {
            double wait = horizon * i / Samples;

            double cost = CostAt(body, positionCci, velocityCci, aimNowCci, wait, loft, seed,
                                 out BallisticArc.Solution arc, out double3 direction,
                                 out bool hitTheGround);

            // Past the point where the vehicle is already down there is nothing left to wait for.
            if (hitTheGround) break;

            if (double.IsFinite(cost)) seed = arc.CheapestFlightSeconds;

            if (cost < best)
            {
                best = cost;
                bestWait = wait;
                bestArc = arc;
                bestDirection = direction;
                bestSeed = arc.CheapestFlightSeconds;
            }
        }

        if (!double.IsFinite(best)) return false;

        if (bestWait > 0.0)
        {
            double span = horizon / Samples;
            double refined = Refine(body, positionCci, velocityCci, aimNowCci, loft,
                                    Math.Max(0.0, bestWait - span), bestWait + span, bestSeed);

            double refinedCost = CostAt(body, positionCci, velocityCci, aimNowCci, refined, loft,
                                        bestSeed, out BallisticArc.Solution refinedArc,
                                        out double3 refinedDirection, out _);

            // The refinement is an improvement or it is nothing. Golden section over a function
            // with unreachable regions in it can walk into one, and the sampled answer is already
            // a real departure time with a real arc behind it.
            if (double.IsFinite(refinedCost) && refinedCost <= best)
            {
                best = refinedCost;
                bestWait = refined;
                bestArc = refinedArc;
                bestDirection = refinedDirection;
            }
        }

        window = new Window(bestWait, bestArc, best, costNow, bestDirection);
        return true;
    }

    private static double CostAt(BallisticBody body, double3 positionCci, double3 velocityCci,
                                 double3 aimNowCci, double wait, double loft, double seed,
                                 out BallisticArc.Solution arc, out double3 burnDirectionCci,
                                 out bool hitTheGround)
    {
        arc = default;
        burnDirectionCci = Vec.Zero;
        hitTheGround = false;

        double3 from = positionCci;
        double3 moving = velocityCci;

        if (wait > 0.0 && !Kepler.TryCoast(body.Mu, positionCci, velocityCci, wait, out from, out moving))
        {
            return double.PositiveInfinity;
        }

        if (from.Length() <= body.SurfaceRadius)
        {
            hitTheGround = true;
            return double.PositiveInfinity;
        }

        // The aim point has moved too, by the whole wait. Carrying it here rather than inside the
        // arc solver is the same rule as everywhere else: whoever knows the interval applies it.
        double3 aimThen = body.CarryCci(aimNowCci, wait);

        if (!BallisticArc.TryCheapest(body, from, moving, aimThen, out arc, loft, false, seed))
        {
            return double.PositiveInfinity;
        }

        double3 toGain = arc.RequiredVelocityCci - moving;
        burnDirectionCci = Vec.Unit(toGain);
        return Vec.Len(toGain);
    }

    private static double Refine(BallisticBody body, double3 positionCci, double3 velocityCci,
                                 double3 aimNowCci, double loft, double lo, double hi, double seed)
    {
        const double Ratio = 0.6180339887498949;
        double c = hi - (hi - lo) * Ratio;
        double d = lo + (hi - lo) * Ratio;

        for (int i = 0; i < 24 && hi - lo > 1.0; i++)
        {
            double costC = CostAt(body, positionCci, velocityCci, aimNowCci, c, loft, seed, out _, out _, out _);
            double costD = CostAt(body, positionCci, velocityCci, aimNowCci, d, loft, seed, out _, out _, out _);

            if (costC < costD) hi = d; else lo = c;

            c = hi - (hi - lo) * Ratio;
            d = lo + (hi - lo) * Ratio;
        }

        return 0.5 * (lo + hi);
    }
}
