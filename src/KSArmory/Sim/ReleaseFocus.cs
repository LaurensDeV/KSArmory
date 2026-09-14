using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The separation velocity that lands a round thrown from one tube where the tubes' mean would land,
/// and the mean where it was aimed.
///
/// <para>A bus's tubes sit on a ring square to the release line and every round leaves its own
/// mouth with the same velocity, while every release prediction is of the <em>mean</em> mouth. So
/// the aim loop lands the mean on the target and the rounds on the ground image of the ring around
/// it — and the bus's roll is not held, so no fixed per-tube aim can remove that. A velocity at
/// separation can: a position offset at release lands where a velocity of about
/// <c>offset / T</c> does, so the opposite kick lands the round on the mean's impact whatever the
/// roll. Over a whole ring the kicks sum to zero, so the group's centre does not move.
/// <c>docs/ACCURACY-PLAN.md</c> item 41.</para>
///
/// <para><b>Solved from the conic rather than taken as <c>-offset / T</c></b>, because gravity's
/// gradient bends both sensitivities by about <c>(nT)²</c>: a few per cent at 340 s and most of the
/// answer at 1,500.</para>
///
/// <para><b>The mean's own miss is what the aim loop leaves</b>, because it stops on its payback rule
/// while the predicted impact drifts at the holding cost. The same solve cancels it once per round,
/// after the aim has committed and with nothing reading it back — the hold fed into the loop instead
/// is chased by it, <c>docs/ACCURACY-PLAN.md</c> 3co.</para>
/// </summary>
internal static class ReleaseFocus
{
    // Clear of the coast's rounding at a planet's radius, and small enough that the arc's curvature
    // over the step does not reach the millimetres a second this is solving for.
    private const double VelocityStepMetresPerSecond = 0.01;

    // A plane square to the arrival that the velocity cannot move the impact across is a geometry
    // no kick can answer, and a refusal is worth more than the huge one the inverse would return.
    private const double LeastAuthority = 1e-9;

    /// <summary>
    /// The most the release probe's miss may be given back as a separation velocity, in metres a second.
    ///
    /// <para>A separation that could correct metres a second would be a guidance burn nobody pays for.
    /// The 2 m the payback rule leaves costs 3.2 mm/s downrange and 6.0 across at 340 s; this clears
    /// both and stays under twice the 6 mm/s of spin a separation already gives back, so a miss needing
    /// more is not the loop's residual.</para>
    /// </summary>
    public const double MaxMissKickMetresPerSecond = 0.010;

    /// <summary>
    /// The minimum-norm velocity that cancels where <paramref name="offsetCci"/> would put the round
    /// on the ground.
    ///
    /// <para>"On the ground" is square to the arrival <em>as the ground sees it</em>: a displacement
    /// along that line is only an earlier or later arrival at the same place, so cancelling the
    /// other two components cancels the landing to first order — including the spin the planet
    /// turns through across that difference in time, which an inertial arrival velocity would
    /// leave in.</para>
    /// </summary>
    /// <param name="positionCci">The state the prediction is flown from — the tubes' mean mouth.</param>
    /// <param name="velocityCci">What every round leaves with before the kick.</param>
    /// <param name="flightSeconds">That state's own flight time, from the prediction that flew it.</param>
    /// <param name="offsetCci">
    /// Where this tube's mouth sits from the mean. A length in a direction, taken off the part frame
    /// and turned: a difference of two positions carries 30 m per millisecond of mismatch at the
    /// ecliptic's speed, and the kick would fire that faithfully into the ground.
    /// </param>
    public static bool TryKick(BallisticBody body, double3 positionCci, double3 velocityCci,
                               double flightSeconds, double3 offsetCci, out double3 kickCci)
    {
        kickCci = Vec.Zero;

        if (!body.IsUsable || !(flightSeconds > 0.0) || !double.IsFinite(flightSeconds)) return false;
        if (!Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci) || !Vec.IsFinite(offsetCci)) return false;
        if (Vec.Len2(offsetCci) == 0.0) return true;

        double mu = body.Mu;

        if (!Kepler.TryCoast(mu, positionCci, velocityCci, flightSeconds, out double3 arrived,
                             out double3 arrivalVelocity))
        {
            return false;
        }

        // The offset's own displacement, flown at its own size: one coast, and exact rather than a
        // column of a matrix it would then be multiplied back through.
        if (!Kepler.TryCoast(mu, positionCci + offsetCci, velocityCci, flightSeconds,
                             out double3 offsetArrived, out _))
        {
            return false;
        }

        return TryCancel(body, positionCci, velocityCci, flightSeconds, arrived, arrivalVelocity,
                         offsetArrived - arrived, out kickCci);
    }

    /// <summary>
    /// The minimum-norm velocity that moves a prediction's impact onto its target along the ground.
    ///
    /// <para><b>Along the ground, and only along it.</b> The miss is taken square to local up at the
    /// impact, so the height the crossing was found at is kept: a prediction stopping under the
    /// surface keeps what <see cref="IcbmConfig.PredictionStopsOnTheSurface"/> removes, and the two
    /// stay separate corrections. A displacement along the ground slides back onto itself along the
    /// arrival, so cancelling it square to that arrival cancels it on the ground.</para>
    /// </summary>
    /// <param name="impactCci">
    /// Where the prediction flown from this state comes down, fixed to the ground at this state's
    /// instant: <see cref="ImpactPredictor.Impact.GroundFixedPointCci"/>.
    /// </param>
    /// <param name="targetCci">Where that prediction was aimed, at the same instant.</param>
    public static bool TryMissKick(BallisticBody body, double3 positionCci, double3 velocityCci,
                                   double flightSeconds, double3 impactCci, double3 targetCci,
                                   out double3 kickCci)
    {
        kickCci = Vec.Zero;

        if (!body.IsUsable || !(flightSeconds > 0.0) || !double.IsFinite(flightSeconds)) return false;
        if (!Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci)) return false;
        if (!Vec.IsFinite(impactCci) || !Vec.IsFinite(targetCci) || Vec.Len2(impactCci) == 0.0) return false;

        double3 up = Vec.Unit(impactCci);
        double3 miss = impactCci - targetCci;
        double3 alongGround = miss - up * Vec.Dot(miss, up);

        if (Vec.Len2(alongGround) == 0.0) return true;

        if (!Kepler.TryCoast(body.Mu, positionCci, velocityCci, flightSeconds, out double3 arrived,
                             out double3 arrivalVelocity))
        {
            return false;
        }

        // Both points are fixed to the ground at the release, and by the arrival the ground has turned.
        return TryCancel(body, positionCci, velocityCci, flightSeconds, arrived, arrivalVelocity,
                         body.CarryCci(alongGround, flightSeconds), out kickCci);
    }

    // The smallest velocity whose displacement at the arrival cancels movedCci square to the arrival
    // as the ground sees it.
    private static bool TryCancel(BallisticBody body, double3 positionCci, double3 velocityCci,
                                  double flightSeconds, double3 arrived, double3 arrivalVelocity,
                                  double3 movedCci, out double3 kickCci)
    {
        kickCci = Vec.Zero;

        double3 alongGround = arrivalVelocity - body.GroundVelocityCci(arrived);
        if (Vec.Len2(alongGround) <= 0.0) return false;

        double3 across = Vec.AnyPerpendicular(alongGround);
        double3 square = Vec.Unit(Vec.Cross(alongGround, across));

        double b1 = Vec.Dot(movedCci, across);
        double b2 = Vec.Dot(movedCci, square);

        if (!TryVelocityColumns(body.Mu, positionCci, velocityCci, flightSeconds, arrived,
                                out double3 alongX, out double3 alongY, out double3 alongZ))
        {
            return false;
        }

        // The velocity sensitivity projected onto that plane, one row per axis of it.
        double3 row1 = new(Vec.Dot(alongX, across), Vec.Dot(alongY, across), Vec.Dot(alongZ, across));
        double3 row2 = new(Vec.Dot(alongX, square), Vec.Dot(alongY, square), Vec.Dot(alongZ, square));

        // Two conditions on three unknowns: kick = -Aᵀ (A Aᵀ)⁻¹ b, the smallest velocity meeting both.
        double m11 = Vec.Dot(row1, row1);
        double m12 = Vec.Dot(row1, row2);
        double m22 = Vec.Dot(row2, row2);
        double det = m11 * m22 - m12 * m12;

        if (!(det > LeastAuthority * m11 * m22)) return false;

        double y1 = (m22 * b1 - m12 * b2) / det;
        double y2 = (m11 * b2 - m12 * b1) / det;

        kickCci = -(row1 * y1 + row2 * y2);
        return Vec.IsFinite(kickCci);
    }

    /// <summary>Where a release prediction lands and what it was aimed at, both fixed to the ground at the release.</summary>
    internal readonly record struct ProbeMiss(double3 ImpactCci, double3 TargetCci);

    /// <summary>What became of the release probe's miss at a separation.</summary>
    internal enum MissOutcome { NotAsked, Cancelled, Unsolved, OverTheCap }

    /// <summary>What <see cref="Kick"/> gives a round, and which of its parts it could give.</summary>
    /// <param name="MissKickCci">
    /// The probe miss's own kick as solved, and past the cap as well, so a refusal can say how large
    /// it was. Only in <paramref name="KickCci"/> when <paramref name="Miss"/> is Cancelled.
    /// </param>
    internal readonly record struct Separation(double3 KickCci, double3 RingKickCci, bool RingFocused,
                                               bool SpinCancelled, double3 MissKickCci, MissOutcome Miss);

    /// <summary>
    /// The whole velocity a round is given as it separates: its ring focused on the mean's impact, the
    /// spin it was thrown with given back, the release probe's miss cancelled, any of them, or none.
    ///
    /// <para>The spin comes back exactly rather than as the least velocity cancelling where it lands,
    /// so the round leaves on the very state the release prediction flew, whatever the arc, the air or
    /// the arm the spin was measured on. <c>docs/ACCURACY-PLAN.md</c> items 41 and 42.</para>
    ///
    /// <para>The three are summed rather than solved together: each is a first-order displacement of
    /// the same prediction, so each cancels its own and none moves another's.</para>
    /// </summary>
    /// <param name="spinCci">What the round was thrown with at its mouth: <see cref="Slug.SpinVelocityEcl"/>, turned.</param>
    /// <param name="cancelMiss">
    /// The release probe's impact and target, or null to leave the mean where the aim loop put it.
    /// Refused past <see cref="MaxMissKickMetresPerSecond"/>.
    /// </param>
    public static Separation Kick(BallisticBody body, double3 positionCci, double3 velocityCci,
                                  double flightSeconds, double3 offsetCci, double3 spinCci,
                                  bool focusRing, bool cancelSpin, ProbeMiss? cancelMiss = null)
    {
        double3 ringKick = Vec.Zero;
        bool focused = focusRing && TryKick(body, positionCci, velocityCci, flightSeconds, offsetCci, out ringKick);
        if (!focused) ringKick = Vec.Zero;

        bool cancelled = cancelSpin && Vec.IsFinite(spinCci);
        double3 kick = cancelled ? ringKick - spinCci : ringKick;

        double3 missKick = Vec.Zero;
        MissOutcome miss = MissOutcome.NotAsked;

        if (cancelMiss is { } probe)
        {
            if (!TryMissKick(body, positionCci, velocityCci, flightSeconds, probe.ImpactCci, probe.TargetCci,
                             out missKick))
            {
                missKick = Vec.Zero;
                miss = MissOutcome.Unsolved;
            }
            else if (!(Vec.Len(missKick) <= MaxMissKickMetresPerSecond))
            {
                miss = MissOutcome.OverTheCap;
            }
            else
            {
                kick += missKick;
                miss = MissOutcome.Cancelled;
            }
        }

        return new Separation(kick, ringKick, focused, cancelled, missKick, miss);
    }

    /// <summary>
    /// Where a round lands from where the release prediction lands, for what its own release state adds
    /// to the one predicted: its mouth's offset, and a velocity of its own.
    ///
    /// <para>The same sensitivities <see cref="TryKick"/> cancels, read forwards, so a flight that
    /// kicks nothing can still be checked against them: the ring's image is item 41, the spin's
    /// item 42. <c>docs/ACCURACY-PLAN.md</c> 3db.</para>
    ///
    /// <para>The displacement is slid along the arrival <em>as the ground sees it</em> back onto the
    /// height it arrived at, since a displacement along that line is only an earlier or later arrival
    /// at the same place. What is left lies in the ground's plane at the arrival.</para>
    /// </summary>
    /// <param name="offsetCci">Where the round's mouth sits from the predicted one, as a length in a direction.</param>
    /// <param name="velocityOffsetCci">What the round leaves with beyond the predicted velocity.</param>
    /// <param name="shiftCci">In the body's inertial frame at the arrival, square to local up there.</param>
    public static bool TryLandingShift(BallisticBody body, double3 positionCci, double3 velocityCci,
                                       double flightSeconds, double3 offsetCci, double3 velocityOffsetCci,
                                       out double3 shiftCci)
    {
        shiftCci = Vec.Zero;

        if (!body.IsUsable || !(flightSeconds > 0.0) || !double.IsFinite(flightSeconds)) return false;
        if (!Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci)) return false;
        if (!Vec.IsFinite(offsetCci) || !Vec.IsFinite(velocityOffsetCci)) return false;

        double mu = body.Mu;

        if (!Kepler.TryCoast(mu, positionCci, velocityCci, flightSeconds, out double3 arrived,
                             out double3 arrivalVelocity))
        {
            return false;
        }

        double3 up = Vec.Unit(arrived);
        double3 alongGround = arrivalVelocity - body.GroundVelocityCci(arrived);
        double sinking = -Vec.Dot(alongGround, up);
        if (!(sinking > 0.0)) return false;

        double3 moved = Vec.Zero;

        if (Vec.Len2(offsetCci) > 0.0)
        {
            if (!Kepler.TryCoast(mu, positionCci + offsetCci, velocityCci, flightSeconds,
                                 out double3 offsetArrived, out _))
            {
                return false;
            }

            moved += offsetArrived - arrived;
        }

        if (Vec.Len2(velocityOffsetCci) > 0.0)
        {
            if (!TryVelocityColumns(mu, positionCci, velocityCci, flightSeconds, arrived,
                                    out double3 alongX, out double3 alongY, out double3 alongZ))
            {
                return false;
            }

            moved += alongX * velocityOffsetCci.X + alongY * velocityOffsetCci.Y
                     + alongZ * velocityOffsetCci.Z;
        }

        shiftCci = moved + alongGround * (Vec.Dot(moved, up) / sinking);
        return Vec.IsFinite(shiftCci);
    }

    // How far the arrival moves per metre a second of release velocity along each axis, one coast per
    // axis. Linear on purpose: a solve against millimetres a second wants the slope, not a difference
    // of two coasts that far apart.
    private static bool TryVelocityColumns(double mu, double3 positionCci, double3 velocityCci,
                                           double flightSeconds, double3 arrived,
                                           out double3 alongX, out double3 alongY, out double3 alongZ)
    {
        alongY = alongZ = Vec.Zero;

        return TryColumn(new double3(VelocityStepMetresPerSecond, 0, 0), out alongX)
               && TryColumn(new double3(0, VelocityStepMetresPerSecond, 0), out alongY)
               && TryColumn(new double3(0, 0, VelocityStepMetresPerSecond), out alongZ);

        bool TryColumn(double3 nudge, out double3 column)
        {
            column = Vec.Zero;
            if (!Kepler.TryCoast(mu, positionCci, velocityCci + nudge, flightSeconds,
                                 out double3 nudgedArrived, out _))
            {
                return false;
            }

            column = (nudgedArrived - arrived) / VelocityStepMetresPerSecond;
            return Vec.IsFinite(column);
        }
    }
}
