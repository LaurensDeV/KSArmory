using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The free-flight problem: what a vehicle must be doing at burnout for the fall afterwards to
/// arrive at a place on a turning planet.
///
/// <para>Parameterised by <em>flight time</em> rather than by energy or by launch angle, because
/// that is the one parameter that makes the rotating target tractable. The aim point's position at
/// arrival depends on how long the flight takes, and its position is what the transfer has to be
/// solved against — so choosing the time first collapses what would be a fixed point into a single
/// solve. Every flight time gives a valid arc; the search over them is what picks a shot.</para>
///
/// <para>The same call answers "how do I launch" and "how do I finish the burn I am in the middle
/// of", because both are the same question asked from a different state. That is why there is no
/// separate launch solver: the guidance loop calls this from wherever it currently is, every
/// cycle, and the answer converges on the pad and at burnout alike.</para>
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
    }

    /// <summary>Nothing usable comes of a shot that arrives sooner than this.</summary>
    public const double MinFlightSeconds = 20.0;

    /// <summary>
    /// The transfer that departs <paramref name="fromCci"/> now and arrives at the aim point
    /// exactly <paramref name="flightSeconds"/> later.
    ///
    /// <para><paramref name="aimNowCci"/> is where the target is <em>at this instant</em>. Carrying
    /// it forward is this function's job and not the caller's — handing in an already-carried
    /// point is how the rotation ends up applied twice.</para>
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
    /// <para>The objective is the length of <see cref="Solution.VelocityToGain"/> from the state
    /// handed in, which is both the fuel this shot costs and, mid-boost, exactly what the steering
    /// law is trying to drive to zero. So the same search serves the launch decision and the
    /// closed loop, and neither needs a notion of "energy" at all.</para>
    ///
    /// <para>A coarse scan first, then a golden section inside the best bracket. The scan is not
    /// timidity about the maths: the cost curve has a second minimum at the long-way-round arc, and
    /// a search that starts by assuming one basin walks into it from some launch geometries.</para>
    /// </summary>
    /// <param name="loft">
    /// Multiplies the flight time chosen by the search. One is the cheapest shot; above one lofts
    /// the trajectory, below depresses it, and both cost more. A depressed shot that would fly
    /// through the planet is refused rather than returned.
    /// </param>
    /// <param name="seedFlightSeconds">
    /// The flight time the previous cycle settled on. Buys two things: the coarse scan is skipped,
    /// which is most of the cost of a cycle, and the search cannot cross into the other basin
    /// halfway up the ascent. Pass NaN when there is nothing to go on.
    /// </param>
    public static bool TryCheapest(BallisticBody body, double3 fromCci, double3 fromVelocityCci,
                                   double3 aimNowCci, out Solution solution,
                                   double loft = 1.0, bool longWay = false,
                                   double seedFlightSeconds = double.NaN)
    {
        solution = default;
        if (!body.IsUsable) return false;

        double horizon = FlightTimeHorizon(body);
        if (!(horizon > MinFlightSeconds)) return false;

        double lo, hi;

        if (double.IsFinite(seedFlightSeconds) && seedFlightSeconds > MinFlightSeconds)
        {
            lo = Math.Max(MinFlightSeconds, seedFlightSeconds * 0.6);
            hi = Math.Min(horizon, seedFlightSeconds * 1.6);
        }
        else
        {
            const int ScanSamples = 96;
            double best = double.PositiveInfinity;
            double bestTime = double.NaN;

            for (int i = 0; i <= ScanSamples; i++)
            {
                double t = MinFlightSeconds + (horizon - MinFlightSeconds) * i / ScanSamples;
                double cost = CostAt(body, fromCci, fromVelocityCci, aimNowCci, t, longWay);
                if (cost < best) { best = cost; bestTime = t; }
            }

            if (!double.IsFinite(bestTime)) return false;

            double span = (horizon - MinFlightSeconds) / ScanSamples;
            lo = Math.Max(MinFlightSeconds, bestTime - span);
            hi = Math.Min(horizon, bestTime + span);
        }

        double refined = GoldenSection(body, fromCci, fromVelocityCci, aimNowCci, lo, hi, longWay);

        double chosen = Math.Clamp(refined * loft, MinFlightSeconds, horizon);

        if (!TrySolve(body, fromCci, aimNowCci, chosen, out solution, longWay)) return false;

        // The cheapest time is carried out separately from the one actually flown, and the caller
        // seeds the next search with the cheapest. Seeding with the lofted time instead applies the
        // loft factor again on every cycle, and a shot asked to fly 1.4 times the cheapest ends up
        // flying it to a power of the number of guidance cycles.
        solution = solution with { CheapestFlightSeconds = refined };

        // An arc whose lowest point is inside the planet is a line drawn through it. That is what a
        // depressed shot becomes past a certain point, and it is the one failure here that looks
        // entirely reasonable in every other number.
        return solution.LowestRadius >= body.SurfaceRadius - 1.0;
    }

    /// <summary>
    /// How long a shot may take before the search stops considering it.
    ///
    /// <para>Three times the period of a circular orbit skimming the surface. Wide enough for any
    /// lofted shot on any body, and bounded rather than arbitrary so the scan's resolution means
    /// the same thing on the Moon as on Earth.</para>
    /// </summary>
    public static double FlightTimeHorizon(BallisticBody body)
    {
        if (!body.IsUsable) return 0.0;
        double r = body.SurfaceRadius;
        return 3.0 * 2.0 * Math.PI * Math.Sqrt(r * r * r / body.Mu);
    }

    private static double CostAt(BallisticBody body, double3 fromCci, double3 fromVelocityCci,
                                 double3 aimNowCci, double flightSeconds, bool longWay)
    {
        if (!TrySolve(body, fromCci, aimNowCci, flightSeconds, out Solution s, longWay))
        {
            return double.PositiveInfinity;
        }
        if (s.LowestRadius < body.SurfaceRadius - 1.0) return double.PositiveInfinity;
        return Vec.Len(s.VelocityToGain(fromVelocityCci));
    }

    private static double GoldenSection(BallisticBody body, double3 fromCci, double3 fromVelocityCci,
                                        double3 aimNowCci, double lo, double hi, bool longWay)
    {
        const double Ratio = 0.6180339887498949;
        double c = hi - (hi - lo) * Ratio;
        double d = lo + (hi - lo) * Ratio;

        for (int i = 0; i < 48 && hi - lo > 0.01; i++)
        {
            if (CostAt(body, fromCci, fromVelocityCci, aimNowCci, c, longWay)
                < CostAt(body, fromCci, fromVelocityCci, aimNowCci, d, longWay))
            {
                hi = d;
            }
            else
            {
                lo = c;
            }
            c = hi - (hi - lo) * Ratio;
            d = lo + (hi - lo) * Ratio;
        }

        return 0.5 * (lo + hi);
    }

    // Highest and lowest radius reached between the two ends of the arc. Both come off the conic
    // rather than out of an integration, because the question is about the whole arc rather than
    // about any sampled point on it: a coarse sample walks straight past a perigee inside the
    // planet, which is the case that matters.
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
