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
    /// <summary>
    /// How many revolutions the search looks across.
    ///
    /// <para>One is not enough, and the reason is the planet rather than the orbit. A revolution
    /// takes about ninety minutes, in which the ground turns some twenty-two degrees underneath —
    /// so a target off the track stays off it, and a search bounded by one revolution reports the
    /// only thing it can see: that reaching it costs a plane change of kilometres a second. Wait
    /// sixteen revolutions and the planet has turned right round, bringing the target under the
    /// track, and the same shot costs a deorbit.</para>
    ///
    /// <para>What this still cannot do is reach a latitude the orbit never gets to. No amount of
    /// waiting fixes an inclination.</para>
    /// </summary>
    public const int Revolutions = 16;

    /// <summary>
    /// Cheap geometric samples across the whole horizon, before anything is solved properly.
    ///
    /// <para>A trajectory solve at each of these would be thousands of them. What decides the cost
    /// is overwhelmingly how far the target sits off the plane, and that is a dot product — so the
    /// scan is done on the geometry and only the handful of moments it likes are solved for
    /// real.</para>
    /// </summary>
    public const int CoarseSamples = 256;

    /// <summary>How many of those moments are then costed properly.</summary>
    public const int Candidates = 8;

    /// <summary>
    /// True-cost samples across the first revolution, on top of the geometric scan.
    ///
    /// <para>The geometry only sees the plane, and the plane is not the only thing that makes a
    /// departure expensive. A target the vehicle has just passed over is dead in the plane and
    /// still unreachable, because the arc to it would have to go backwards — so the first
    /// revolution is sampled properly rather than being filtered.</para>
    /// </summary>
    public const int NearSamples = 32;

    /// <summary>
    /// How much worse an earlier window may be and still be taken.
    ///
    /// <para>The cheapest departure in a day is not the one to want. Waiting twenty hours to save
    /// the last few per cent is not a trade a weapon should make on its own, so the earliest
    /// window within this of the best wins.</para>
    /// </summary>
    public const double GoodEnoughFraction = 0.15;

    /// <summary>For a trajectory that never comes round, there is no repeat to wait for.</summary>
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
        /// <para>The number that separates "wait" from "you cannot get there". The instantaneous
        /// angle says the target is off the plane; this says whether it is ever going to stop
        /// being, and a floor well above zero is an inclination the orbit does not have.</para>
        /// </summary>
        double ClosestOffPlaneRadians)
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

        double period = Kepler.PeriodSeconds(body.Mu, positionCci, velocityCci);
        bool bound = double.IsFinite(period) && period > 0.0;
        double horizon = bound ? period * Revolutions : UnboundHorizonSeconds;

        double costNow = CostAt(body, positionCci, velocityCci, aimNowCci, 0.0, loft, period,
                                double.NaN, out BallisticArc.Solution arcNow,
                                out double3 directionNow, out _);

        double best = costNow;
        double bestWait = 0.0;
        BallisticArc.Solution bestArc = arcNow;
        double3 bestDirection = directionNow;
        double bestSeed = double.IsFinite(costNow) ? arcNow.CheapestFlightSeconds : double.NaN;

        double closest = double.PositiveInfinity;

        Span<double> whenCci = stackalloc double[Candidates];
        Span<double> howBad = stackalloc double[Candidates];
        for (int i = 0; i < Candidates; i++) { whenCci[i] = double.NaN; howBad[i] = double.PositiveInfinity; }

        // Stage one: geometry only. What a shot costs is dominated by how far the target sits off
        // the plane being flown in, and that is a dot product rather than a trajectory solve.
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

        // Stage two: the first revolution costed properly at every step, because phasing is not
        // visible to the geometry — a target just passed over is dead in the plane and still
        // unreachable, since the arc to it would have to go backwards.
        double near = bound ? period : horizon;

        for (int i = 1; i <= NearSamples; i++)
        {
            double wait = near * i / NearSamples;
            Weigh(body, positionCci, velocityCci, aimNowCci, wait, loft, period,
                  ref best, ref bestWait, ref bestArc, ref bestDirection, ref bestSeed);
        }

        // Stage three: the handful of later moments the geometry liked.
        for (int i = 0; i < Candidates; i++)
        {
            if (!double.IsFinite(whenCci[i])) continue;

            Weigh(body, positionCci, velocityCci, aimNowCci, whenCci[i], loft, period,
                  ref best, ref bestWait, ref bestArc, ref bestDirection, ref bestSeed);
        }

        if (!double.IsFinite(best)) return false;

        // The cheapest departure in a day is not the one to want. Take the earliest that is nearly
        // as good, because the other half of what waiting costs is not measured in metres a second.
        double allowed = best * (1.0 + GoodEnoughFraction);

        if (bestWait > 0.0)
        {
            for (int i = 1; i <= NearSamples; i++)
            {
                double wait = near * i / NearSamples;
                if (wait >= bestWait) break;

                double cost = CostAt(body, positionCci, velocityCci, aimNowCci, wait, loft, period,
                                     bestSeed, out BallisticArc.Solution arc, out double3 direction, out _);

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
                                    Math.Max(0.0, bestWait - span), bestWait + span, bestSeed);

            double refinedCost = CostAt(body, positionCci, velocityCci, aimNowCci, refined, loft,
                                        period, bestSeed, out BallisticArc.Solution refinedArc,
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

        if (!double.IsFinite(closest))
        {
            closest = OrbitPlane.OffPlaneRadians(positionCci, velocityCci, aimNowCci);
        }

        window = new Window(bestWait, bestArc, best, costNow, bestDirection, closest);
        return true;
    }

    // Costs one departure and keeps it if it beats what is held.
    private static void Weigh(BallisticBody body, double3 positionCci, double3 velocityCci,
                              double3 aimNowCci, double wait, double loft, double period,
                              ref double best, ref double bestWait, ref BallisticArc.Solution bestArc,
                              ref double3 bestDirection, ref double bestSeed)
    {
        double cost = CostAt(body, positionCci, velocityCci, aimNowCci, wait, loft, period,
                             bestSeed, out BallisticArc.Solution arc, out double3 direction, out _);

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

    // The vehicle's own state repeats every revolution, so a coast of many hours is asked for as a
    // coast of less than one. That is exact for a two-body orbit and it keeps the solver away from
    // the many-revolution case, where its iteration is at its least well behaved.
    private static bool StateAt(BallisticBody body, double3 positionCci, double3 velocityCci,
                                double wait, double period, bool bound,
                                out double3 from, out double3 moving)
    {
        double coast = bound ? wait % period : wait;
        return Kepler.TryCoast(body.Mu, positionCci, velocityCci, coast, out from, out moving);
    }

    private static double CostAt(BallisticBody body, double3 positionCci, double3 velocityCci,
                                 double3 aimNowCci, double wait, double loft, double period,
                                 double seed, out BallisticArc.Solution arc,
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
                                 double3 aimNowCci, double loft, double period,
                                 double lo, double hi, double seed)
    {
        const double Ratio = 0.6180339887498949;
        double c = hi - (hi - lo) * Ratio;
        double d = lo + (hi - lo) * Ratio;

        for (int i = 0; i < 24 && hi - lo > 1.0; i++)
        {
            double costC = CostAt(body, positionCci, velocityCci, aimNowCci, c, loft, period, seed, out _, out _, out _);
            double costD = CostAt(body, positionCci, velocityCci, aimNowCci, d, loft, period, seed, out _, out _, out _);

            if (costC < costD) hi = d; else lo = c;

            c = hi - (hi - lo) * Ratio;
            d = lo + (hi - lo) * Ratio;
        }

        return 0.5 * (lo + hi);
    }
}
