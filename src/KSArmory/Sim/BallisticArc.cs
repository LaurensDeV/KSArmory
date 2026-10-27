using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The free-flight problem: what a vehicle must be doing at burnout for the fall afterwards to
/// arrive at a place on a turning planet.
///
/// <para>Parameterised by <em>flight time</em>: the aim point's position at arrival depends on it, so
/// choosing the time first turns a fixed point into a single solve. The same call answers on the pad
/// and mid-burn, so there is no separate launch solver.</para>
/// </summary>
internal static class BallisticArc
{
    /// <summary>A trajectory that arrives, and everything about it worth showing or acting on.</summary>
    internal readonly record struct Solution(
        double3 RequiredVelocityCci,
        double3 ArrivalVelocityCci,
        double3 ImpactCciAtArrival,
        double FlightSeconds,
        double CheapestFlightSeconds,
        double ApogeeRadius,
        double LowestRadius)
    {
        /// <summary>What this shot still costs from a stated velocity — the number guidance nulls.</summary>
        public double3 VelocityToGain(double3 currentVelocityCci) => RequiredVelocityCci - currentVelocityCci;

        /// <summary>
        /// How far below the local horizontal this arc comes in, in degrees.
        ///
        /// <para>The arc's own angle, in vacuum at the mean sphere: within half a degree of what lands over
        /// 10 to 30 degrees, and only a graze bends much (<c>docs/ARRIVAL-ANGLE.md</c>).</para>
        /// </summary>
        public double ArrivalAngleDeg => DescentAngleDeg(ImpactCciAtArrival, ArrivalVelocityCci);
    }

    /// <summary>Nothing usable comes of a shot that arrives sooner than this.</summary>
    public const double MinFlightSeconds = 20.0;

    /// <summary>Degrees a velocity points below the local horizontal at a point, positive descending.</summary>
    public static double DescentAngleDeg(double3 pointCci, double3 velocityCci)
        => Vec.AngleBetween(pointCci, velocityCci) * 180.0 / Math.PI - 90.0;

    /// <summary>
    /// The transfer that departs <paramref name="fromCci"/> now and arrives at the aim point
    /// exactly <paramref name="flightSeconds"/> later.
    ///
    /// <para><paramref name="aimNowCci"/> is where the target is <em>at this instant</em>; this carries it,
    /// so an already-carried point gets the rotation twice.</para>
    /// </summary>
    public static bool TrySolve(BallisticBody body, double3 fromCci, double3 aimNowCci,
                                double flightSeconds, out Solution solution, bool longWay = false)
    {
        solution = default;

        if (!body.IsUsable) return false;
        if (!(flightSeconds >= MinFlightSeconds)) return false;

        double3 arrivalCci = body.CarryCci(aimNowCci, flightSeconds);

        if (!Lambert.TrySolve(fromCci, arrivalCci, flightSeconds, body.Mu, out Lambert.Transfer t, longWay))
        {
            return false;
        }

        Extremes(body.Mu, fromCci, t.DepartureVelocityCci, arrivalCci, out double apogee, out double lowest);

        solution = new Solution(t.DepartureVelocityCci, t.ArrivalVelocityCci, arrivalCci,
                                flightSeconds, flightSeconds, apogee, lowest);
        return true;
    }

    /// <summary>
    /// The cheapest arrival, searched over flight time.
    ///
    /// <para>The objective is the length of <see cref="Solution.VelocityToGain"/>, the fuel and, mid-boost,
    /// what steering nulls. A coarse scan first, because the cost has a second minimum at the long way
    /// round; then a golden section in the best bracket.</para>
    /// </summary>
    /// <param name="loft">
    /// Multiplies the flight time chosen by the search. One is the cheapest shot; above one lofts
    /// the trajectory, below depresses it, and both cost more. A depressed shot that would fly
    /// through the planet is refused rather than returned.
    /// </param>
    /// <param name="seedFlightSeconds">
    /// The flight time the previous cycle settled on: skips the coarse scan and keeps the search out of the
    /// other basin. NaN when there is nothing to go on.
    /// </param>
    /// <param name="minArrivalDeg">
    /// The shallowest arrival, in degrees below the local horizontal; zero is off.
    ///
    /// <para><b>A bound rather than a nudge</b>, unlike <paramref name="loft"/>: a predicate is idempotent,
    /// so seeding the next search with a constrained answer is safe where a lofted one is not.</para>
    /// </param>
    public static bool TryCheapest(BallisticBody body, double3 fromCci, double3 fromVelocityCci,
                                   double3 aimNowCci, out Solution solution,
                                   double loft = 1.0, bool longWay = false,
                                   double seedFlightSeconds = double.NaN,
                                   double minArrivalDeg = 0.0,
                                   double minToGain = 0.0)
    {
        solution = default;
        if (!body.IsUsable) return false;

        double horizon = FlightTimeHorizon(body);
        if (!(horizon > MinFlightSeconds)) return false;

        double floor = double.IsFinite(minArrivalDeg) && minArrivalDeg > 0.0 ? minArrivalDeg : 0.0;

        double refined = Search(body, fromCci, fromVelocityCci, aimNowCci, longWay, floor,
                                horizon, seedFlightSeconds);

        // The arrivals that satisfy a floor are not one interval (steep at both ends of flight time,
        // shallow in the middle), so a seeded bracket can hold nothing feasible.
        if (!double.IsFinite(refined) && floor > 0.0 && double.IsFinite(seedFlightSeconds))
        {
            refined = Search(body, fromCci, fromVelocityCci, aimNowCci, longWay, floor,
                             horizon, double.NaN);
        }

        if (!double.IsFinite(refined)) return false;

        double chosen = Math.Clamp(refined * loft, MinFlightSeconds, horizon);

        // Where the two disagree the bound wins: a depressing loft is what the floor refuses.
        if (floor > 0.0 && !(ArrivalAt(body, fromCci, aimNowCci, chosen, longWay) >= floor))
        {
            chosen = refined;
        }

        if (minToGain > 0.0)
        {
            chosen = Absorbing(body, fromCci, fromVelocityCci, aimNowCci, chosen, horizon, longWay, floor, minToGain);
        }

        if (!TrySolve(body, fromCci, aimNowCci, chosen, out solution, longWay)) return false;

        // The cheapest time is carried out separately for the next seed; seeding with the lofted
        // time compounds the loft every cycle.
        solution = solution with { CheapestFlightSeconds = refined };

        // A depressed arc through the planet looks reasonable in every other number.
        return solution.LowestRadius >= body.SurfaceRadius - 1.0;
    }

    // The flight time whose arc needs exactly minToGain, on the lofted side of the cheapest: past
    // the cheapest arc the cost climbs with the flight time all the way to escape, so a stack that
    // cannot avoid adding that much has an arc for it. Below the cheapest the same costs are on
    // depressed arcs, which graze the air. Where nothing reaches it, the dearest arc found.
    private static double Absorbing(BallisticBody body, double3 fromCci, double3 fromVelocityCci,
                                    double3 aimNowCci, double from, double horizon, bool longWay,
                                    double floorDeg, double minToGain)
    {
        double CostOf(double t) => CostAt(body, fromCci, fromVelocityCci, aimNowCci, t, longWay, floorDeg);

        if (CostOf(from) >= minToGain) return from;

        const int Steps = 64;
        double step = (horizon - from) / Steps;
        double lo = from, best = from, bestCost = CostOf(from);

        for (int i = 1; i <= Steps; i++)
        {
            double t = from + step * i;
            double cost = CostOf(t);
            if (!double.IsFinite(cost)) continue;

            if (cost >= minToGain)
            {
                double hi = t;
                for (int k = 0; k < 40; k++)
                {
                    double mid = 0.5 * (lo + hi);
                    double c = CostOf(mid);
                    if (double.IsFinite(c) && c >= minToGain) hi = mid; else lo = mid;
                }
                return hi;
            }

            lo = t;
            if (cost > bestCost) { bestCost = cost; best = t; }
        }

        return best;
    }

    /// <summary>
    /// How long a shot may take before the search stops considering it.
    ///
    /// <para>Three surface-skimming orbital periods, so the scan's resolution means the same on any body.</para>
    /// </summary>
    public static double FlightTimeHorizon(BallisticBody body)
    {
        if (!body.IsUsable) return 0.0;
        double r = body.SurfaceRadius;
        return 3.0 * 2.0 * Math.PI * Math.Sqrt(r * r * r / body.Mu);
    }

    // How many flight times the coarse scan looks at across the whole horizon.
    private const int ScanSamples = 96;

    // The cheapest flight time that satisfies the floor, or NaN if nothing here does. Seeded, it
    // is a bracket round last cycle's answer; unseeded, a scan of the whole horizon.
    private static double Search(BallisticBody body, double3 fromCci, double3 fromVelocityCci,
                                 double3 aimNowCci, bool longWay, double floorDeg,
                                 double horizon, double seedFlightSeconds)
    {
        double lo, hi, anchor;

        if (double.IsFinite(seedFlightSeconds) && seedFlightSeconds > MinFlightSeconds)
        {
            lo = Math.Max(MinFlightSeconds, seedFlightSeconds * 0.6);
            hi = Math.Min(horizon, seedFlightSeconds * 1.6);
            anchor = Math.Clamp(seedFlightSeconds, lo, hi);
        }
        else
        {
            double best = double.PositiveInfinity;
            double bestTime = double.NaN;

            for (int i = 0; i <= ScanSamples; i++)
            {
                double t = MinFlightSeconds + (horizon - MinFlightSeconds) * i / ScanSamples;
                double cost = CostAt(body, fromCci, fromVelocityCci, aimNowCci, t, longWay, floorDeg);
                if (cost < best) { best = cost; bestTime = t; }
            }

            if (!double.IsFinite(bestTime)) return double.NaN;

            double span = (horizon - MinFlightSeconds) / ScanSamples;
            lo = Math.Max(MinFlightSeconds, bestTime - span);
            hi = Math.Min(horizon, bestTime + span);
            anchor = bestTime;
        }

        double refined = GoldenSection.Minimise(
            t => CostAt(body, fromCci, fromVelocityCci, aimNowCci, t, longWay, floorDeg), lo, hi, 48, 0.01);

        if (floorDeg <= 0.0) return refined;

        // Golden section can walk into the unreachable edge a floor puts in a bracket, so the refinement
        // is kept only if it improves on the already-costed anchor.
        double refinedCost = CostAt(body, fromCci, fromVelocityCci, aimNowCci, refined, longWay, floorDeg);
        double anchorCost = CostAt(body, fromCci, fromVelocityCci, aimNowCci, anchor, longWay, floorDeg);

        if (double.IsFinite(refinedCost) && refinedCost <= anchorCost) return refined;
        return double.IsFinite(anchorCost) ? anchor : double.NaN;
    }

    // The arrival angle of one flight time, for deciding whether a lofted time still satisfies the
    // floor. NaN for a time that has no arc at all, which fails every comparison and so is refused.
    private static double ArrivalAt(BallisticBody body, double3 fromCci, double3 aimNowCci,
                                    double flightSeconds, bool longWay)
        => TrySolve(body, fromCci, aimNowCci, flightSeconds, out Solution s, longWay)
               ? s.ArrivalAngleDeg
               : double.NaN;

    private static double CostAt(BallisticBody body, double3 fromCci, double3 fromVelocityCci,
                                 double3 aimNowCci, double flightSeconds, bool longWay,
                                 double floorDeg)
    {
        if (!TrySolve(body, fromCci, aimNowCci, flightSeconds, out Solution s, longWay))
        {
            return double.PositiveInfinity;
        }
        if (s.LowestRadius < body.SurfaceRadius - 1.0) return double.PositiveInfinity;
        if (floorDeg > 0.0 && !(s.ArrivalAngleDeg >= floorDeg)) return double.PositiveInfinity;
        return Vec.Len(s.VelocityToGain(fromVelocityCci));
    }

    // Highest and lowest radius between the arc's ends, off the conic: a coarse sample walks past a
    // perigee inside the planet.
    private static void Extremes(double mu, double3 rCci, double3 vCci, double3 arrivalCci,
                                 out double apogeeRadius, out double lowestRadius)
    {
        double r1 = rCci.Length();
        double r2 = arrivalCci.Length();
        apogeeRadius = Math.Max(r1, r2);
        lowestRadius = Math.Min(r1, r2);

        double3 h = Vec.Cross(rCci, vCci);
        double hLen = h.Length();
        if (!(hLen > 0.0) || !(mu > 0.0) || !(r1 > 0.0)) return;

        double3 eVec = Vec.Cross(vCci, h) / mu - rCci / r1;
        double e = eVec.Length();
        double p = hLen * hLen / mu;
        if (!double.IsFinite(p) || !(p > 0.0)) return;

        if (e < 1e-9)
        {
            apogeeRadius = p;
            lowestRadius = p;
            return;
        }

        double3 hHat = h / hLen;
        double3 eHat = eVec / e;

        double nu1 = Math.Atan2(Vec.Dot(Vec.Cross(eHat, rCci), hHat), Vec.Dot(eHat, rCci));
        double swept = SweptAngle(rCci, arrivalCci, hHat);

        double periapsis = p / (1.0 + e);
        if (Sweeps(nu1, swept, 0.0)) lowestRadius = Math.Min(lowestRadius, periapsis);

        // Apoapsis only exists for a closed arc; a depressed shot can be hyperbolic, and then the
        // highest point on it is simply whichever end is higher.
        if (e < 1.0 && Sweeps(nu1, swept, Math.PI))
        {
            apogeeRadius = Math.Max(apogeeRadius, p / (1.0 - e));
        }
    }

    private static double SweptAngle(double3 fromCci, double3 toCci, double3 normal)
    {
        double angle = Math.Atan2(Vec.Dot(Vec.Cross(fromCci, toCci), normal), Vec.Dot(fromCci, toCci));
        return angle < 0.0 ? angle + 2.0 * Math.PI : angle;
    }

    // Does an arc starting at true anomaly `from` and sweeping `swept` radians forward pass through
    // `target`? Both windings are tested because the long way round covers more than a full turn
    // of true anomaly on a low-eccentricity arc.
    private static bool Sweeps(double from, double swept, double target)
    {
        for (int k = -1; k <= 2; k++)
        {
            double t = target + 2.0 * Math.PI * k;
            if (t > from && t < from + swept) return true;
        }
        return false;
    }
}
