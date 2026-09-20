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
/// answer at 1,500. <b>Or flown through the air</b>, <see cref="FlownSensitivity"/>, because the conic
/// flown for a drag arc's time ends under the ground.</para>
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
    /// A 2 m miss, where the payback rule usually releases, costs 3.2 mm/s downrange and 6.0 across at
    /// 340 s; this clears both and stays under twice the 6 mm/s of spin a separation already gives back.
    /// A long correction cycle raises that threshold — one of 23.4 s released 6 m out and would have
    /// asked about 12.6 mm/s — so the cap refuses that tail, 1 flight in 80 on the shipped arm.</para>
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

        return TryVelocityColumns(mu, positionCci, velocityCci, flightSeconds, arrived,
                                  out double3 alongX, out double3 alongY, out double3 alongZ)
               && TryCancel(body, arrived, arrivalVelocity, alongX, alongY, alongZ, offsetArrived - arrived,
                            out kickCci);
    }

    /// <summary>
    /// <see cref="TryKick"/> with the sensitivities flown through the air rather than coasted in vacuum.
    /// </summary>
    public static bool TryKick(BallisticBody body, FlownSensitivity flown, double3 offsetCci, out double3 kickCci)
    {
        kickCci = Vec.Zero;

        if (!body.IsUsable || !Vec.IsFinite(offsetCci)) return false;
        if (Vec.Len2(offsetCci) == 0.0) return true;

        return TryCancel(body, flown.ArrivedCci, flown.ArrivalVelocityCci, flown.VelocityX, flown.VelocityY,
                         flown.VelocityZ, flown.Moved(offsetCci), out kickCci);
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
    /// <param name="groundRadiusAt">
    /// The ground's radius under a point fixed to it at this state's instant, to measure the miss as
    /// <see cref="TryMissOnTheGround"/> does; null, or a chord that cannot be trusted, measures it square
    /// to local up.
    /// </param>
    /// <param name="flown">
    /// The sensitivities flown through the air, or null to coast them in vacuum. The miss is still carried
    /// over <paramref name="flightSeconds"/>, this release's own.
    /// </param>
    public static bool TryMissKick(BallisticBody body, double3 positionCci, double3 velocityCci,
                                   double flightSeconds, double3 impactCci, double3 targetCci,
                                   out double3 kickCci, Func<double3, double>? groundRadiusAt = null,
                                   FlownSensitivity? flown = null)
    {
        kickCci = Vec.Zero;

        if (!body.IsUsable || !(flightSeconds > 0.0) || !double.IsFinite(flightSeconds)) return false;
        if (!Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci)) return false;
        if (!Vec.IsFinite(impactCci) || !Vec.IsFinite(targetCci) || Vec.Len2(impactCci) == 0.0) return false;

        double3 up = Vec.Unit(impactCci);
        double3 miss = impactCci - targetCci;
        double3 alongGround = groundRadiusAt is not null
                              && TryMissOnTheGround(impactCci, targetCci, groundRadiusAt, out double3 chord)
            ? chord
            : miss - up * Vec.Dot(miss, up);

        if (Vec.Len2(alongGround) == 0.0) return true;

        // Both points are fixed to the ground at the release, and by the arrival the ground has turned.
        double3 moved = body.CarryCci(alongGround, flightSeconds);

        if (flown is not null)
        {
            return TryCancel(body, flown.ArrivedCci, flown.ArrivalVelocityCci, flown.VelocityX, flown.VelocityY,
                             flown.VelocityZ, moved, out kickCci);
        }

        if (!Kepler.TryCoast(body.Mu, positionCci, velocityCci, flightSeconds, out double3 arrived,
                             out double3 arrivalVelocity))
        {
            return false;
        }

        return TryVelocityColumns(body.Mu, positionCci, velocityCci, flightSeconds, arrived,
                                  out double3 alongX, out double3 alongY, out double3 alongZ)
               && TryCancel(body, arrived, arrivalVelocity, alongX, alongY, alongZ, moved, out kickCci);
    }

    /// <summary>
    /// The miss from a target to an impact along the ground as it lies: both lifted onto the ground at their
    /// own directions, so the chord between them rises with whatever slope is under it.
    ///
    /// <para><b>What the kick has to cancel on ground that is not level.</b> The arc is moved square to its
    /// arrival by the miss's component there, and a crossing slides back along the arrival onto whatever
    /// surface the miss was measured on. Measured square to local up, a miss over ground falling away
    /// downrange at <c>g</c> is cancelled wrong by <c>1 − tan γ / (tan γ − g)</c> of itself — a fifth of it at
    /// 0.1 on a 32° arrival. Measured along the chord, it is cancelled on the ground the round meets.</para>
    ///
    /// <para>False where either lookup gives no ground or the chord rises further than it runs: a lookup that
    /// fails reads as the mean sphere, which under high ground is kilometres from the other end's answer.</para>
    /// </summary>
    /// <param name="groundRadiusAt">The ground's radius under a point fixed to it, as a prediction reads it.</param>
    public static bool TryMissOnTheGround(double3 impactCci, double3 targetCci, Func<double3, double> groundRadiusAt,
                                          out double3 chordCci)
    {
        chordCci = Vec.Zero;

        if (!Vec.IsFinite(impactCci) || !Vec.IsFinite(targetCci)) return false;
        if (Vec.Len2(impactCci) == 0.0 || Vec.Len2(targetCci) == 0.0) return false;

        double impactRadius = groundRadiusAt(impactCci);
        double targetRadius = groundRadiusAt(targetCci);
        if (!(impactRadius > 0.0) || !(targetRadius > 0.0) || !double.IsFinite(impactRadius + targetRadius)) return false;

        double3 up = Vec.Unit(impactCci);
        double3 chord = up * impactRadius - Vec.Unit(targetCci) * targetRadius;
        double rise = Vec.Dot(chord, up);

        if (!(Math.Abs(rise) <= Vec.Len(chord - up * rise))) return false;

        chordCci = chord;
        return true;
    }

    // The smallest velocity whose displacement at the arrival cancels movedCci square to the arrival
    // as the ground sees it, given how far the arrival moves per metre a second along each axis.
    private static bool TryCancel(BallisticBody body, double3 arrived, double3 arrivalVelocity,
                                  double3 alongX, double3 alongY, double3 alongZ,
                                  double3 movedCci, out double3 kickCci)
    {
        kickCci = Vec.Zero;

        double3 alongGround = arrivalVelocity - body.GroundVelocityCci(arrived);
        if (Vec.Len2(alongGround) <= 0.0) return false;
        if (!Vec.IsFinite(movedCci)) return false;

        double3 across = Vec.AnyPerpendicular(alongGround);
        double3 square = Vec.Unit(Vec.Cross(alongGround, across));

        double b1 = Vec.Dot(movedCci, across);
        double b2 = Vec.Dot(movedCci, square);

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

    /// <summary>
    /// What a release state is flown through to build a <see cref="FlownSensitivity"/>: the release probe's own
    /// drag and step, down to a sphere through the ground under the probe's impact.
    /// </summary>
    /// <param name="GroundRadiusMetres">
    /// A sphere rather than the height field, because every column is two landings a few metres apart and the
    /// ground's texture across them is not the arc's sensitivity. Cancelled square to the arrival, a landing
    /// moved on the sphere is cancelled on any ground through the same point; the slope between the probe's
    /// impact and the target is <see cref="TryMissOnTheGround"/>'s.
    /// </param>
    internal readonly record struct Air(ImpactPredictor.Drag Drag, double StepSeconds, double GroundRadiusMetres,
                                        bool StopOnTheSurface,
                                        bool StopOnTheTerrain = false);

    /// <summary>
    /// How a round's landing moves with its release state, flown through the air: where the nominal state lands
    /// and how, and one column per axis of release position and of release velocity, each the landing's
    /// displacement per unit, carried to the arrival.
    ///
    /// <para><b>What a vacuum coast leaves.</b> Coasted for the drag flight's time, the vacuum arc is kilometres
    /// under the ground, and the coasted solve cancels the ring's image there; what it leaves on the ground goes
    /// with that depth. Headless at 340 s and 32°: 1.9 km and 0.19% of the image on the Mk 21's constant, 7.8 km
    /// and 0.80% on its drag from its shape — 2.1 and 8.8 mm of the group's spread, against 3.1 and 8.5 flown.
    /// Flown through the air, 0.001 mm on both. <c>docs/ACCURACY-PLAN.md</c> 3eq.</para>
    ///
    /// <para><b>Seven flights of the predictor, so it is flown once a salvo</b> and carried along the coast to
    /// each release after it: <see cref="For"/>. Every warhead of a salvo leaves the same arc within a fraction of
    /// a second, and the same columns taken as they were cost 2.9 mm of the spread per second between the flight
    /// and the release.</para>
    /// </summary>
    /// <param name="ArrivalSecondsPerMetre">
    /// How much later the round arrives per metre of release position, one component per axis.
    /// </param>
    /// <param name="ArrivalSecondsPerMetrePerSecond">
    /// The same per metre a second of release velocity. <b>Not recoverable from the landing columns</b>: those
    /// are two landings on one sphere, so the radial part that decides <em>when</em> the crossing happens is
    /// projected out of them. The seven flights carry it for nothing —
    /// <see cref="ImpactPredictor.Impact.Seconds"/> is already in hand — and it is the third row
    /// <see cref="DivertFootprint.ArrivalClock.Pinned"/> needs.
    /// </param>
    internal sealed record FlownSensitivity(double3 ArrivedCci, double3 ArrivalVelocityCci, double FlightSeconds,
                                            double3 PositionX, double3 PositionY, double3 PositionZ,
                                            double3 VelocityX, double3 VelocityY, double3 VelocityZ,
                                            double3 ArrivalSecondsPerMetre,
                                            double3 ArrivalSecondsPerMetrePerSecond)
    {
        // Metres of landing per step: large beside the predictor's micrometres of crossing noise, and small
        // beside the 8 km the air changes over.
        private const double PositionStepMetres = 1.0;

        /// <summary>
        /// How far along the coast these columns may be carried to a release, in seconds of its flight time.
        /// </summary>
        /// <remarks>
        /// Carried, they leave 0.004 mm of the spread at 2 s and 0.03 mm at 5, headless at 340 s; a salvo lets
        /// its six go inside 0.15 s. The carry reads the interval off the two flight times, so it assumes one
        /// coast between them.
        /// </remarks>
        public const double ReusableWithinSeconds = 2.0;

        /// <summary>
        /// How far the ground under a release's impact may be from the sphere these columns were flown to, in
        /// metres. Ten leaves 0.008 mm of the spread, headless; fifty, 0.04.
        /// </summary>
        public const double ReusableWithinMetres = 10.0;

        /// <summary>
        /// Whether a release whose probe flies <paramref name="flightSeconds"/> down to ground
        /// <paramref name="groundRadiusMetres"/> from the centre may reuse these columns.
        /// </summary>
        public bool Covers(double flightSeconds, double groundRadiusMetres)
            => double.IsFinite(flightSeconds) && Math.Abs(FlightSeconds - flightSeconds) <= ReusableWithinSeconds
               && double.IsFinite(groundRadiusMetres)
               && Math.Abs(Vec.Len(ArrivedCci) - groundRadiusMetres) <= ReusableWithinMetres;

        /// <summary>
        /// These columns for a release further along the same coast, whose own flight is
        /// <paramref name="flightSeconds"/>: the same arrival, reached from a state <c>τ</c> later.
        /// </summary>
        /// <remarks>
        /// A change at the later state is the change <c>τ</c> earlier that coasts into it: <c>δr − τ·δv</c> and
        /// <c>δv − τ·G·δr</c>, with <c>G</c> the gravity gradient, so the position columns lose <c>τ·V·G</c> and
        /// the velocity columns <c>τ·P</c>. Left out, the second costs 2.9 mm of the spread a second and the first
        /// 0.33 mm at half a second. The two arrival-clock rows are an output of the same state and carry the same
        /// way.
        /// </remarks>
        /// <param name="positionCci">The later release's position, where the gradient is taken.</param>
        public FlownSensitivity For(double mu, double3 positionCci, double flightSeconds)
        {
            double tau = FlightSeconds - flightSeconds;
            if (tau == 0.0 || !double.IsFinite(tau)) return this;

            double r = Vec.Len(positionCci);
            double3 up = Vec.Unit(positionCci);
            double k = mu / (r * r * r);

            // Column j of V·G, with G = k·(3·up·upᵀ − I) symmetric.
            double3 VG(double upJ, double3 velocityJ)
                => ((VelocityX * up.X + VelocityY * up.Y + VelocityZ * up.Z) * (3.0 * upJ) - velocityJ) * k;

            // The same product for a row rather than a matrix, which is what the arrival clock is.
            double3 RowG(double3 row) => (up * (3.0 * Vec.Dot(row, up)) - row) * k;

            return this with
            {
                FlightSeconds = flightSeconds,
                PositionX = PositionX - VG(up.X, VelocityX) * tau,
                PositionY = PositionY - VG(up.Y, VelocityY) * tau,
                PositionZ = PositionZ - VG(up.Z, VelocityZ) * tau,
                VelocityX = VelocityX - PositionX * tau,
                VelocityY = VelocityY - PositionY * tau,
                VelocityZ = VelocityZ - PositionZ * tau,
                ArrivalSecondsPerMetre = ArrivalSecondsPerMetre - RowG(ArrivalSecondsPerMetrePerSecond) * tau,
                ArrivalSecondsPerMetrePerSecond = ArrivalSecondsPerMetrePerSecond - ArrivalSecondsPerMetre * tau,
            };
        }

        /// <summary>Where a release position offset moves the landing, at the arrival.</summary>
        public double3 Moved(double3 offsetCci)
            => PositionX * offsetCci.X + PositionY * offsetCci.Y + PositionZ * offsetCci.Z;

        /// <summary>Null where any of the seven flights does not come down.</summary>
        public static FlownSensitivity? TryFly(BallisticBody body, double3 positionCci, double3 velocityCci, in Air air)
        {
            if (!body.IsUsable || !Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci)) return null;
            if (!(air.GroundRadiusMetres > 0.0) || !double.IsFinite(air.GroundRadiusMetres)) return null;

            Air through = air;

            if (!TryLand(positionCci, velocityCci, out ImpactPredictor.Impact nominal)) return null;

            double seconds = nominal.Seconds;

            if (!TryColumn(new double3(PositionStepMetres, 0, 0), Vec.Zero, PositionStepMetres, out double3 px,
                           out double tpx)
                || !TryColumn(new double3(0, PositionStepMetres, 0), Vec.Zero, PositionStepMetres, out double3 py,
                              out double tpy)
                || !TryColumn(new double3(0, 0, PositionStepMetres), Vec.Zero, PositionStepMetres, out double3 pz,
                              out double tpz)
                || !TryColumn(Vec.Zero, new double3(VelocityStepMetresPerSecond, 0, 0), VelocityStepMetresPerSecond,
                              out double3 vx, out double tvx)
                || !TryColumn(Vec.Zero, new double3(0, VelocityStepMetresPerSecond, 0), VelocityStepMetresPerSecond,
                              out double3 vy, out double tvy)
                || !TryColumn(Vec.Zero, new double3(0, 0, VelocityStepMetresPerSecond), VelocityStepMetresPerSecond,
                              out double3 vz, out double tvz))
            {
                return null;
            }

            return new FlownSensitivity(nominal.PointCci, nominal.VelocityCci, seconds, px, py, pz, vx, vy, vz,
                                        new double3(tpx, tpy, tpz), new double3(tvx, tvy, tvz));

            bool TryLand(double3 p, double3 v, out ImpactPredictor.Impact impact)
                => ImpactPredictor.TryPredict(body, p, v, through.StepSeconds, ImpactPredictor.DefaultMaxSeconds,
                                              out impact, _ => through.GroundRadiusMetres, null, through.Drag,
                                              stopOnTheSurface: through.StopOnTheSurface,
                                              stopOnTheTerrain: through.StopOnTheTerrain);

            // Ground-fixed landings differenced, then carried: the two arrive at different instants, and a
            // difference of inertial crossings would carry the ground's turn across that gap.
            bool TryColumn(double3 dp, double3 dv, double step, out double3 column, out double delaySeconds)
            {
                column = Vec.Zero;
                delaySeconds = 0.0;
                if (!TryLand(positionCci + dp, velocityCci + dv, out ImpactPredictor.Impact moved)) return false;

                column = body.CarryCci(moved.GroundFixedPointCci - nominal.GroundFixedPointCci, seconds) / step;
                delaySeconds = (moved.Seconds - seconds) / step;
                return Vec.IsFinite(column) && double.IsFinite(delaySeconds);
            }
        }
    }

    /// <summary>
    /// Where a release prediction lands and what it was aimed at, both fixed to the ground at the release, and
    /// the ground to measure the miss between them over — null for square to local up.
    /// </summary>
    internal readonly record struct ProbeMiss(double3 ImpactCci, double3 TargetCci,
                                              Func<double3, double>? GroundRadiusAt = null);

    /// <summary>What became of the release probe's miss at a separation.</summary>
    internal enum MissOutcome { NotAsked, Cancelled, Unsolved, OverTheCap }

    /// <summary>What <see cref="Kick"/> gives a round, and which of its parts it could give.</summary>
    /// <param name="MissKickCci">
    /// The probe miss's own kick as solved, and past the cap as well, so a refusal can say how large
    /// it was. Only in <paramref name="KickCci"/> when <paramref name="Miss"/> is Cancelled.
    /// </param>
    internal readonly record struct Separation(double3 KickCci, double3 RingKickCci, bool RingFocused,
                                               bool SpinCancelled, double3 MissKickCci, MissOutcome Miss,
                                               bool ThroughTheAir = false);

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
    /// <param name="shrinkToward">
    /// The mean of the miss kicks already applied in this release, and how much of a warhead's own
    /// departure from it to keep. Null keeps all of it, which is what every flight before this did.
    ///
    /// <para>The kick cancels what the probe says <em>this</em> warhead will miss by, and flown that
    /// differential behaves as noise injected one for one — the landing regresses on it at -1.090,
    /// an interval containing -1 (<c>docs/ACCURACY-PLAN.md</c> 3ef). Shrinking it toward what the
    /// siblings already asked for keeps the common part, which is worth 459 mm of centre, and
    /// discards the part that is not shared.</para>
    /// </param>
    /// <param name="throughTheAir">
    /// The ring's image and both solves flown through the air, or null to coast them in vacuum as every flight
    /// before this did. Carried to this release along the coast; the spin is given back exactly either way.
    /// </param>
    public static Separation Kick(BallisticBody body, double3 positionCci, double3 velocityCci,
                                  double flightSeconds, double3 offsetCci, double3 spinCci,
                                  bool focusRing, bool cancelSpin, ProbeMiss? cancelMiss = null,
                                  (double3 Mean, double Keep)? shrinkToward = null,
                                  FlownSensitivity? throughTheAir = null)
    {
        throughTheAir = throughTheAir?.For(body.Mu, positionCci, flightSeconds);

        double3 ringKick = Vec.Zero;
        bool focused = focusRing
                       && (throughTheAir is not null
                               ? TryKick(body, throughTheAir, offsetCci, out ringKick)
                               : TryKick(body, positionCci, velocityCci, flightSeconds, offsetCci, out ringKick));
        if (!focused) ringKick = Vec.Zero;

        bool cancelled = cancelSpin && Vec.IsFinite(spinCci);
        double3 kick = cancelled ? ringKick - spinCci : ringKick;

        double3 missKick = Vec.Zero;
        MissOutcome miss = MissOutcome.NotAsked;

        if (cancelMiss is { } probe)
        {
            if (!TryMissKick(body, positionCci, velocityCci, flightSeconds, probe.ImpactCci, probe.TargetCci,
                             out missKick, probe.GroundRadiusAt, throughTheAir))
            {
                missKick = Vec.Zero;
                miss = MissOutcome.Unsolved;
            }
            else
            {
                // Before the cap, so the cap still bounds what is actually fired.
                if (shrinkToward is { } toward)
                {
                    missKick = toward.Mean + (missKick - toward.Mean) * toward.Keep;
                }

                if (!(Vec.Len(missKick) <= MaxMissKickMetresPerSecond))
                {
                    miss = MissOutcome.OverTheCap;
                }
                else
                {
                    kick += missKick;
                    miss = MissOutcome.Cancelled;
                }
            }
        }

        return new Separation(kick, ringKick, focused, cancelled, missKick, miss, throughTheAir is not null);
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
