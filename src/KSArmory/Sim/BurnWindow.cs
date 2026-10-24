using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// <em>When</em> to start burning, which is a separate question from how to fly the burn.
///
/// <para><see cref="BallisticArc"/> answers "what would leaving now cost", which is the whole question on a
/// pad. In orbit the cost swings by orders of magnitude across a revolution: a target just passed over
/// costs eleven km/s now and two hundred m/s most of a revolution later.</para>
/// </summary>
internal static class BurnWindow
{
    /// <summary>
    /// How many revolutions the search looks across.
    ///
    /// <para>Sixteen low orbits are a day, in which the planet turns a target off the track back under
    /// it. No amount of waiting fixes an inclination.</para>
    /// </summary>
    public const int Revolutions = 16;

    /// <summary>
    /// Cheap geometric samples across the whole horizon, before anything is solved properly.
    ///
    /// <para>The cost is dominated by how far the target sits off the plane, a dot product, so only the
    /// moments it likes are solved.</para>
    /// </summary>
    public const int CoarseSamples = 256;

    /// <summary>How many of those moments are then costed properly.</summary>
    public const int Candidates = 8;

    /// <summary>
    /// True-cost samples across the first revolution, on top of the geometric scan.
    ///
    /// <para>The geometry cannot see phasing: a target just passed over is dead in the plane and still
    /// unreachable.</para>
    /// </summary>
    public const int NearSamples = 32;

    /// <summary>
    /// How much worse an earlier window may be and still be taken.
    ///
    /// <para>The earliest window within this of the day's best wins, rather than waiting hours for a few
    /// per cent.</para>
    /// </summary>
    public const double GoodEnoughFraction = 0.15;

    /// <summary>The horizon searched for a trajectory that never comes round (s).</summary>
    public const double UnboundHorizonSeconds = 3600.0;

    /// <summary>What leaving at some future moment would cost, and what leaving now would.</summary>
    internal readonly record struct Window(
        double WaitSeconds,
        BallisticArc.Solution Arc,
        double Cost,
        double CostIfLeavingNow,
        double3 BurnDirectionCci,

        /// <summary>
        /// The closest the target ever comes to the plane being flown in, across the whole horizon,
        /// in radians.
        ///
        /// <para>Separates "wait" from "you cannot get there": well above zero is an inclination the orbit
        /// does not have.</para>
        /// </summary>
        double ClosestOffPlaneRadians,

        /// <summary>
        /// The steepest arrival among the windows this search actually costed, in degrees.
        ///
        /// <para>What to tell an operator whose floor found nothing. <b>Not the steepest arc that exists</b>,
        /// the steepest the search chose; meaningful only from an unconstrained search.</para>
        /// </summary>
        double SteepestArrivalDeg)
    {
        /// <summary>How much of the shot waiting saves. Infinite when leaving now is impossible.</summary>
        public double Saving => CostIfLeavingNow - Cost;
    }

    /// <summary>
    /// The cheapest moment to leave, searched across <see cref="Revolutions"/> revolutions: the
    /// first costed at every step, the rest scanned on the plane angle and only the best few solved.
    /// </summary>
    /// <param name="minArrivalDeg">
    /// The shallowest arrival any window may have. Zero is off.
    ///
    /// <para>Threaded into the cost, so a departure with no arc steep enough is not a window, and waiting
    /// cannot produce a shallower arrival than leaving now.</para>
    /// </param>
    public static bool TryFind(BallisticBody body, double3 positionCci, double3 velocityCci,
                               double3 aimNowCci, out Window window, double loft = 1.0,
                               double minArrivalDeg = 0.0)
    {
        window = default;

        if (!body.IsUsable) return false;
        if (!Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci)) return false;

        double period = Kepler.PeriodSeconds(body.Mu, positionCci, velocityCci);
        bool bound = double.IsFinite(period) && period > 0.0;
        double horizon = bound ? period * Revolutions : UnboundHorizonSeconds;

        double costNow = CostAt(body, positionCci, velocityCci, aimNowCci, 0.0, loft, period,
                                double.NaN, minArrivalDeg, out BallisticArc.Solution arcNow,
                                out double3 directionNow, out _);

        double best = costNow;
        double bestWait = 0.0;
        BallisticArc.Solution bestArc = arcNow;
        double3 bestDirection = directionNow;
        double bestSeed = double.IsFinite(costNow) ? arcNow.CheapestFlightSeconds : double.NaN;

        double closest = double.PositiveInfinity;
        double steepest = double.IsFinite(costNow) ? arcNow.ArrivalAngleDeg : double.NegativeInfinity;

        Span<double> whenCci = stackalloc double[Candidates];
        Span<double> howBad = stackalloc double[Candidates];
        for (int i = 0; i < Candidates; i++) { whenCci[i] = double.NaN; howBad[i] = double.PositiveInfinity; }

        // Stage one: geometry only.
        for (int i = 1; i <= CoarseSamples; i++)
        {
            double wait = horizon * i / CoarseSamples;

            if (!StateAt(body, positionCci, velocityCci, wait, period, bound,
                         out double3 from, out double3 moving))
            {
                continue;
            }

            if (from.Length() <= body.SurfaceRadius) break;

            double3 aimThen = body.CarryCci(aimNowCci, wait);
            double offPlane = OrbitPlane.OffPlaneRadians(from, moving, aimThen);
            closest = Math.Min(closest, offPlane);

            Consider(whenCci, howBad, wait, OrbitPlane.PlaneChangeCost(Vec.Len(moving), offPlane));
        }

        // Stage two: the first revolution costed properly, for phasing.
        double near = bound ? period : horizon;

        for (int i = 1; i <= NearSamples; i++)
        {
            double wait = near * i / NearSamples;
            Weigh(body, positionCci, velocityCci, aimNowCci, wait, loft, period, minArrivalDeg,
                  ref best, ref bestWait, ref bestArc, ref bestDirection, ref bestSeed, ref steepest);
        }

        // Stage three: the handful of later moments the geometry liked.
        for (int i = 0; i < Candidates; i++)
        {
            if (!double.IsFinite(whenCci[i])) continue;

            Weigh(body, positionCci, velocityCci, aimNowCci, whenCci[i], loft, period, minArrivalDeg,
                  ref best, ref bestWait, ref bestArc, ref bestDirection, ref bestSeed, ref steepest);
        }

        if (!double.IsFinite(best)) return false;

        // The earliest that is nearly as good: the rest of what waiting costs is not in m/s.
        double allowed = best * (1.0 + GoodEnoughFraction);

        if (bestWait > 0.0)
        {
            for (int i = 1; i <= NearSamples; i++)
            {
                double wait = near * i / NearSamples;
                if (wait >= bestWait) break;

                double cost = CostAt(body, positionCci, velocityCci, aimNowCci, wait, loft, period,
                                     bestSeed, minArrivalDeg, out BallisticArc.Solution arc,
                                     out double3 direction, out _);

                if (!(cost <= allowed)) continue;

                best = cost;
                bestWait = wait;
                bestArc = arc;
                bestDirection = direction;
                break;
            }
        }

        if (bestWait > 0.0)
        {
            double span = horizon / CoarseSamples;
            double refined = Refine(body, positionCci, velocityCci, aimNowCci, loft, period,
                                    Math.Max(0.0, bestWait - span), bestWait + span, bestSeed,
                                    minArrivalDeg);

            double refinedCost = CostAt(body, positionCci, velocityCci, aimNowCci, refined, loft,
                                        period, bestSeed, minArrivalDeg,
                                        out BallisticArc.Solution refinedArc,
                                        out double3 refinedDirection, out _);

            // Kept only if it improves: golden section can walk into an unreachable region.
            if (double.IsFinite(refinedCost) && refinedCost <= best)
            {
                best = refinedCost;
                bestWait = refined;
                bestArc = refinedArc;
                bestDirection = refinedDirection;
            }
        }

        if (!double.IsFinite(closest))
        {
            closest = OrbitPlane.OffPlaneRadians(positionCci, velocityCci, aimNowCci);
        }

        window = new Window(bestWait, bestArc, best, costNow, bestDirection, closest,
                            double.IsFinite(steepest) ? steepest : double.NaN);
        return true;
    }

    /// <summary>
    /// The window a hold has already chosen, refined where it is rather than searched for afresh.
    ///
    /// <para>A fresh search can swap a near window whose cost drifts across <see cref="GoodEnoughFraction"/>
    /// for another at the last minute. False when the kept window can no longer be solved.</para>
    /// </summary>
    public static bool TryKeep(BallisticBody body, double3 positionCci, double3 velocityCci, double3 aimNowCci,
                               double keptWaitSeconds, out Window window, double loft = 1.0, double minArrivalDeg = 0.0)
    {
        window = default;

        if (!body.IsUsable || !(keptWaitSeconds >= 0.0)) return false;
        if (!Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci)) return false;

        double period = Kepler.PeriodSeconds(body.Mu, positionCci, velocityCci);
        bool bound = double.IsFinite(period) && period > 0.0;
        double span = (bound ? period : UnboundHorizonSeconds) / NearSamples;

        double costNow = CostAt(body, positionCci, velocityCci, aimNowCci, 0.0, loft, period, double.NaN, minArrivalDeg,
                                out _, out _, out _);

        double wait = Refine(body, positionCci, velocityCci, aimNowCci, loft, period,
                             Math.Max(0.0, keptWaitSeconds - span), keptWaitSeconds + span, double.NaN, minArrivalDeg);

        double cost = CostAt(body, positionCci, velocityCci, aimNowCci, wait, loft, period, double.NaN, minArrivalDeg,
                             out BallisticArc.Solution arc, out double3 direction, out _);

        if (!double.IsFinite(cost)) return false;

        double offPlane = StateAt(body, positionCci, velocityCci, wait, period, bound, out double3 from, out double3 moving)
                              ? OrbitPlane.OffPlaneRadians(from, moving, body.CarryCci(aimNowCci, wait))
                              : double.NaN;

        window = new Window(wait, arc, cost, costNow, direction, offPlane, arc.ArrivalAngleDeg);
        return true;
    }

    // Costs one departure and keeps it if it beats what is held.
    private static void Weigh(BallisticBody body, double3 positionCci, double3 velocityCci,
                              double3 aimNowCci, double wait, double loft, double period,
                              double minArrivalDeg,
                              ref double best, ref double bestWait, ref BallisticArc.Solution bestArc,
                              ref double3 bestDirection, ref double bestSeed, ref double steepest)
    {
        double cost = CostAt(body, positionCci, velocityCci, aimNowCci, wait, loft, period,
                             bestSeed, minArrivalDeg, out BallisticArc.Solution arc,
                             out double3 direction, out _);

        if (double.IsFinite(cost)) steepest = Math.Max(steepest, arc.ArrivalAngleDeg);

        if (!(cost < best)) return;

        best = cost;
        bestWait = wait;
        bestArc = arc;
        bestDirection = direction;
        bestSeed = arc.CheapestFlightSeconds;
    }

    // Keeps the best few moments, worst-first, without sorting anything.
    private static void Consider(Span<double> when, Span<double> howBad, double wait, double proxy)
    {
        if (!double.IsFinite(proxy)) return;

        int worst = 0;
        for (int i = 1; i < howBad.Length; i++)
        {
            if (howBad[i] > howBad[worst]) worst = i;
        }

        if (proxy >= howBad[worst]) return;

        howBad[worst] = proxy;
        when[worst] = wait;
    }

    // The state repeats every revolution, so a long coast is asked for modulo the period: exact for two
    // bodies, and it keeps the solver out of the many-revolution case.
    private static bool StateAt(BallisticBody body, double3 positionCci, double3 velocityCci,
                                double wait, double period, bool bound,
                                out double3 from, out double3 moving)
    {
        double coast = bound ? wait % period : wait;
        return Kepler.TryCoast(body.Mu, positionCci, velocityCci, coast, out from, out moving);
    }

    private static double CostAt(BallisticBody body, double3 positionCci, double3 velocityCci,
                                 double3 aimNowCci, double wait, double loft, double period,
                                 double seed, double minArrivalDeg, out BallisticArc.Solution arc,
                                 out double3 burnDirectionCci, out bool hitTheGround)
    {
        arc = default;
        burnDirectionCci = Vec.Zero;
        hitTheGround = false;

        double3 from = positionCci;
        double3 moving = velocityCci;
        bool bound = double.IsFinite(period) && period > 0.0;

        if (wait > 0.0 && !StateAt(body, positionCci, velocityCci, wait, period, bound, out from, out moving))
        {
            return double.PositiveInfinity;
        }

        if (from.Length() <= body.SurfaceRadius)
        {
            hitTheGround = true;
            return double.PositiveInfinity;
        }

        // Carried by the wait here; the arc solver carries it over the flight.
        double3 aimThen = body.CarryCci(aimNowCci, wait);

        if (!BallisticArc.TryCheapest(body, from, moving, aimThen, out arc, loft, false, seed,
                                      minArrivalDeg))
        {
            return double.PositiveInfinity;
        }

        double3 toGain = arc.RequiredVelocityCci - moving;
        burnDirectionCci = Vec.Unit(toGain);
        return Vec.Len(toGain);
    }

    private static double Refine(BallisticBody body, double3 positionCci, double3 velocityCci,
                                 double3 aimNowCci, double loft, double period,
                                 double lo, double hi, double seed, double minArrivalDeg)
        => GoldenSection.Minimise(
            t => CostAt(body, positionCci, velocityCci, aimNowCci, t, loft, period, seed, minArrivalDeg,
                        out _, out _, out _),
            lo, hi, 24, 1.0);
}
