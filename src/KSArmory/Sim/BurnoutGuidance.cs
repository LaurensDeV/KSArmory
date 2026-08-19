using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where to point and when to stop, so that what happens after the engines quit arrives on the
/// target. The closed-loop half of the ICBM computer.
///
/// <para>Velocity-to-be-gained guidance, which is what a ballistic missile has always used. The
/// loop asks <see cref="BallisticArc"/> what velocity would put a free fall on the target from
/// where the vehicle will be at cutoff, subtracts what it will have by then, and thrusts along the
/// difference until there is none left.</para>
///
/// <para><b>The property that makes it robust is terminal, not gradual.</b> Because the required
/// velocity is re-solved against the vehicle's <em>actual</em> state every cycle, the shot is
/// exact at the instant the difference reaches zero, whatever happened on the way there — a
/// staging transient, a wrong pitch program, an engine that underperforms, air drag nobody
/// modelled. None of it accumulates, because none of it is remembered. What a bad path costs is
/// propellant, not accuracy.</para>
///
/// <para>That is also why there is no separate launch solver and no stored trajectory: the same
/// call answers on the pad and one second before cutoff, and the answer on the pad is only a plan
/// in the sense that it is the first of several thousand.</para>
/// </summary>
internal static class BurnoutGuidance
{
    /// <summary>
    /// How many times the cutoff state is re-predicted per cycle.
    ///
    /// <para>The prediction needs the burn time, which needs the velocity to gain, which needs the
    /// prediction. Three passes settle it to well under a metre per second from a standing start;
    /// in flight the previous cycle's answer is the seed and one would do.</para>
    /// </summary>
    public const int RefinementPasses = 3;

    /// <summary>Below this the burn is finished and further steering is noise.</summary>
    public const double CutoffMetresPerSecond = 0.01;

    /// <summary>What to do about it this instant.</summary>
    /// <param name="ToGainVectorCci">
    /// Velocity still to gain, as a vector.
    ///
    /// <para>Its <em>length</em> is what the burn is trying to zero, but its direction is what
    /// decides what a residual costs: on a deorbit, a metre a second left along the track is 1.8 km
    /// of miss and the same metre left radially is 3.4 km. A caller holding only the length cannot
    /// tell those apart, and cannot tell whether burning on would still help.</para>
    /// </param>
    internal readonly record struct Command(
        double3 ThrustDirectionCci,
        double VelocityToGain,
        double3 ToGainVectorCci,
        double SecondsToCutoff,
        double3 CutoffPositionCci,
        BallisticArc.Solution Arc,
        bool HeldTheArrival)
    {
        /// <summary>The burn is done. Anything further is spending propellant on making it worse.</summary>
        public bool AtCutoff => VelocityToGain <= CutoffMetresPerSecond;
    }

    /// <param name="aimNowCci">Where the target is at this instant, before any carry.</param>
    /// <param name="cutoffSeed">
    /// Last cycle's time to cutoff. Only a starting point for the refinement — a wrong one costs an
    /// iteration, never an answer.
    /// </param>
    /// <param name="flightSeed">Last cycle's flight time, which keeps the trajectory search local.</param>
    /// <param name="arrivalFromNowSeconds">
    /// Hold the arrival to this many seconds from now, rather than re-choosing the cheapest one
    /// every cycle. Once a shot is committed this is what stops it chasing its own trajectory: the
    /// cheapest arc from the vehicle's <em>current</em> state converges on whatever the vehicle is
    /// already doing, so multiplying that by a loft factor pushes the answer further out every
    /// cycle and the shot runs away. Pass NaN before commitment, when following the cheapest is
    /// exactly right.
    /// </param>
    public static bool TrySteer(BallisticBody body, double3 positionCci, double3 velocityCci,
                                double3 aimNowCci, BoosterPerformance booster,
                                out Command command, double loft = 1.0, bool longWay = false,
                                double cutoffSeed = 0.0, double flightSeed = double.NaN,
                                double arrivalFromNowSeconds = double.NaN)
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
        BallisticArc.Solution arc = default;
        double toGain = 0.0;
        bool solved = false;

        for (int pass = 0; pass < RefinementPasses; pass++)
        {
            // Gravity is held at its value here rather than integrated. Over a burn of a couple of
            // minutes on an arc that never doubles its radius that is a small error, and it is one
            // the next cycle sees and removes; integrating it would be a second flight model to
            // keep in step with the first.
            double3 gravity = body.GravityCci(positionCci);

            cutoffPosition = positionCci
                           + velocityCci * timeToCutoff
                           + gravity * (0.5 * timeToCutoff * timeToCutoff)
                           + thrustDir * booster.ThrustDisplacement(timeToCutoff);

            // The first pass extrapolates from a standing start along whatever direction happened
            // to be seeded, holding gravity constant over the whole burn — so for a shot whose
            // required velocity is nearly horizontal it puts the predicted cutoff tens of
            // kilometres underground. No arc departs from inside the planet, so the solve then
            // fails and the shot is refused as unreachable when it is nothing of the kind. Lifting
            // the prediction back to the surface costs the converged answer nothing, because by the
            // last pass it is nowhere near the ground.
            double cutoffRadius = cutoffPosition.Length();
            if (cutoffRadius > 1.0 && cutoffRadius < body.SurfaceRadius)
            {
                cutoffPosition *= body.SurfaceRadius / cutoffRadius;
            }

            double3 velocityAtCutoffUnpowered = velocityCci + gravity * timeToCutoff;

            // The arc departs at cutoff, not now, so the target has to be carried to the cutoff
            // instant before it is handed over. Without this the whole solve is out by the planet's
            // turn during the remaining burn - forty-odd kilometres a hundred seconds before
            // cutoff. The loop converges anyway, because the term goes to zero as the burn ends,
            // which is exactly why it is easy to leave in and never see.
            double3 aimAtCutoff = body.CarryCci(aimNowCci, timeToCutoff);

            bool solvedArc = false;
            heldTheArrival = false;

            if (double.IsFinite(arrivalFromNowSeconds)
                && arrivalFromNowSeconds - timeToCutoff >= BallisticArc.MinFlightSeconds)
            {
                solvedArc = BallisticArc.TrySolve(body, cutoffPosition, aimAtCutoff,
                                                  arrivalFromNowSeconds - timeToCutoff, out arc, longWay)
                            && arc.LowestRadius >= body.SurfaceRadius - 1.0;

                heldTheArrival = solvedArc;
            }

            // Falling back rather than failing, and this is the important half. A fixed arrival
            // pins the transfer angle, and a pinned angle can land on the one geometry Lambert
            // cannot answer: two points opposite each other about the centre, where no plane is
            // determined. Returning false there leaves the caller holding the previous cycle's
            // answer — which is to say flying the burn open loop, with the velocity still to gain
            // frozen at whatever it was when the trouble started.
            if (!solvedArc)
            {
                solvedArc = BallisticArc.TryCheapest(body, cutoffPosition, velocityAtCutoffUnpowered,
                                                     aimAtCutoff, out arc, loft, longWay, flightSeed);
            }

            if (!solvedArc) return false;

            flightSeed = arc.CheapestFlightSeconds;

            double3 toGainVector = arc.RequiredVelocityCci - velocityAtCutoffUnpowered;
            toGain = Vec.Len(toGainVector);
            toGainOut = toGainVector;

            double3 next = Vec.Unit(toGainVector);
            if (!next.Equals(Vec.Zero)) thrustDir = next;

            // Left infinite when the stack cannot thrust at all, and that matters: a caller that
            // runs this down as a cutoff countdown reads a zero here as "the burn is finished" and
            // ends a flight that has not started, or one whose engine has just failed, reporting it
            // as a completed shot.
            wanted = booster.SecondsToGain(toGain);
            if (double.IsNaN(wanted)) wanted = 0.0;

            // The *prediction* is capped at the propellant this stage has, and the reported time is
            // not. A cutoff state predicted from a burn the stack cannot perform is a point
            // hundreds of kilometres from anywhere the vehicle will go, and the arc solved from it
            // usually does not exist at all — but the uncapped number is the honest answer to how
            // long the burn has left, and capping the one that ends the burn cuts the engines at
            // every staging.
            double available = booster.BurnSecondsRemaining;
            timeToCutoff = available > 0.0 ? Math.Min(wanted, available) : wanted;
            if (!double.IsFinite(timeToCutoff)) timeToCutoff = 0.0;

            solved = true;
        }

        if (!solved) return false;

        command = new Command(thrustDir, toGain, toGainOut, wanted, cutoffPosition, arc, heldTheArrival);
        return true;
    }

    /// <summary>
    /// Whether the propellant left can finish the shot, with a stated margin.
    ///
    /// <para>Asked continuously rather than once at launch. A stack that could reach the target
    /// from the pad and cannot reach it from where a bad ascent has put it is the ordinary failure,
    /// and it is worth saying so while there is still a burn left to redirect.</para>
    /// </summary>
    public static bool CanReach(in Command command, BoosterPerformance booster, double marginFraction = 0.05)
        => booster.DeltaVRemaining >= command.VelocityToGain * (1.0 + marginFraction);
}
