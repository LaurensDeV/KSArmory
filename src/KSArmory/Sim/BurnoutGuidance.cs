using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where to point and when to stop, so that what happens after the engines quit arrives on the
/// target. The closed-loop half of the ICBM computer.
///
/// <para>Velocity-to-be-gained: <see cref="BallisticArc"/> gives the velocity that puts a free fall on the
/// target from the predicted cutoff, and the stack thrusts along what it still lacks. Re-solved against
/// the actual state every cycle, so a bad path costs propellant, not accuracy, and there is no stored
/// trajectory (<c>docs/ICBM-GUIDANCE.md</c>, "The algorithm").</para>
/// </summary>
internal static class BurnoutGuidance
{
    /// <summary>
    /// How many times the cutoff state is re-predicted per cycle.
    ///
    /// <para>The prediction needs the burn time, which needs the prediction: three passes settle it from
    /// a standing start, and in flight the previous cycle's answer seeds it.</para>
    /// </summary>
    public const int RefinementPasses = 3;

    /// <summary>Below this the burn is finished and further steering is noise.</summary>
    public const double CutoffMetresPerSecond = 0.01;

    /// <summary>What to do about it this instant.</summary>
    /// <param name="ToGainVectorCci">
    /// Velocity still to gain, as a vector.
    ///
    /// <para>The direction decides what a residual costs: on a deorbit a metre a second along the track
    /// is 1.8 km of miss and radially 3.4 km.</para>
    /// </param>
    internal readonly record struct Command(
        double3 ThrustDirectionCci,
        double VelocityToGain,
        double3 ToGainVectorCci,
        double SecondsToCutoff,
        double3 CutoffPositionCci,
        BallisticArc.Solution Arc,
        bool HeldTheArrival,
        double CarrySeconds = double.NaN)
    {
        /// <summary>The burn is done; anything further makes it worse.</summary>
        public bool AtCutoff => VelocityToGain <= CutoffMetresPerSecond;
    }

    /// <param name="aimNowCci">Where the target is at this instant, before any carry.</param>
    /// <param name="cutoffSeed">
    /// Last cycle's time to cutoff, a seed: a wrong one costs an iteration, never an answer.
    /// </param>
    /// <param name="flightSeed">Last cycle's flight time, which keeps the trajectory search local.</param>
    /// <param name="arrivalFromNowSeconds">
    /// Hold the arrival to this many seconds from now. Once committed, re-choosing the cheapest arc times a
    /// loft every cycle pushes the answer out and the shot runs away. NaN before commitment.
    /// </param>
    /// <param name="minArrivalDeg">
    /// The shallowest arrival the shot may have. Zero is off, and then a committed arrival is held
    /// whatever it arrives at — which is the whole of the default path.
    ///
    /// <para><b>Pinning the arrival instant does not pin the arrival angle</b>: the cheaper split inside it
    /// is the longer coast, a shallower arrival (a 20 degree floor flew at 15.51, at 3,459 km). So a held
    /// arc under the floor is refused and the constrained search re-commits; a predicate is idempotent
    /// where a loft multiplier is not.</para>
    /// </param>
    public static bool TrySteer(BallisticBody body, double3 positionCci, double3 velocityCci,
                                double3 aimNowCci, BoosterPerformance booster,
                                out Command command, double loft = 1.0, bool longWay = false,
                                double cutoffSeed = 0.0, double flightSeed = double.NaN,
                                double arrivalFromNowSeconds = double.NaN,
                                double minArrivalDeg = 0.0,
                                double minToGain = 0.0)
    {
        command = default;

        if (!body.IsUsable) return false;
        if (!Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci)) return false;

        double timeToCutoff = double.IsFinite(cutoffSeed) && cutoffSeed > 0.0 ? cutoffSeed : 0.0;
        double wanted = timeToCutoff;
        bool heldTheArrival = false;
        double3 thrustDir = Vec.Unit(velocityCci);
        if (thrustDir.Equals(Vec.Zero)) thrustDir = Vec.Unit(positionCci);

        double3 cutoffPosition = positionCci;
        double3 toGainOut = Vec.Zero;
        double carried = 0.0;
        BallisticArc.Solution arc = default;
        double toGain = 0.0;
        bool solved = false;

        for (int pass = 0; pass < RefinementPasses; pass++)
        {
            // Gravity held constant over the burn: a small error the next cycle removes.
            double3 gravity = body.GravityCci(positionCci);

            cutoffPosition = positionCci
                           + velocityCci * timeToCutoff
                           + gravity * (0.5 * timeToCutoff * timeToCutoff)
                           + thrustDir * booster.ThrustDisplacement(timeToCutoff);

            // A first pass along the seeded direction can predict a cutoff tens of kilometres
            // underground, from which no arc departs; lifting it to the surface costs the converged
            // answer nothing.
            double cutoffRadius = cutoffPosition.Length();
            if (cutoffRadius > 1.0 && cutoffRadius < body.SurfaceRadius)
            {
                cutoffPosition *= body.SurfaceRadius / cutoffRadius;
            }

            double3 velocityAtCutoffUnpowered = velocityCci + gravity * timeToCutoff;

            // The arc departs at cutoff, so the target is carried to that instant: forty-odd km a
            // hundred seconds out, and invisible because it goes to zero as the burn ends.
            double3 aimAtCutoff = body.CarryCci(aimNowCci, timeToCutoff);
            carried = timeToCutoff;

            bool solvedArc = false;
            heldTheArrival = false;

            if (double.IsFinite(arrivalFromNowSeconds) && !(minToGain > 0.0)
                && arrivalFromNowSeconds - timeToCutoff >= BallisticArc.MinFlightSeconds)
            {
                solvedArc = BallisticArc.TrySolve(body, cutoffPosition, aimAtCutoff,
                                                  arrivalFromNowSeconds - timeToCutoff, out arc, longWay)
                            && arc.LowestRadius >= body.SurfaceRadius - 1.0
                            && (!(minArrivalDeg > 0.0) || arc.ArrivalAngleDeg >= minArrivalDeg);

                heldTheArrival = solvedArc;
            }

            // Fall back rather than fail: a pinned arrival can land on the antipodal geometry Lambert
            // cannot answer, and false would leave the burn flown open loop on a frozen answer.
            if (!solvedArc)
            {
                solvedArc = BallisticArc.TryCheapest(body, cutoffPosition, velocityAtCutoffUnpowered,
                                                     aimAtCutoff, out arc, loft, longWay, flightSeed,
                                                     minArrivalDeg, minToGain);
            }

            if (!solvedArc) return false;

            flightSeed = arc.CheapestFlightSeconds;

            double3 toGainVector = arc.RequiredVelocityCci - velocityAtCutoffUnpowered;
            toGain = Vec.Len(toGainVector);
            toGainOut = toGainVector;

            double3 next = Vec.Unit(toGainVector);
            if (!next.Equals(Vec.Zero)) thrustDir = next;

            // Infinite when the stack cannot thrust: a zero would read as a finished burn.
            wanted = booster.SecondsToGain(toGain);
            if (double.IsNaN(wanted)) wanted = 0.0;

            // The prediction is capped at this stage's propellant and the reported time is not:
            // capping the reported one cuts the engines at every staging.
            double available = booster.BurnSecondsRemaining;
            timeToCutoff = available > 0.0 ? Math.Min(wanted, available) : wanted;
            if (!double.IsFinite(timeToCutoff)) timeToCutoff = 0.0;

            solved = true;
        }

        if (!solved) return false;

        command = new Command(thrustDir, toGain, toGainOut, wanted, cutoffPosition, arc, heldTheArrival, carried);
        return true;
    }
}
