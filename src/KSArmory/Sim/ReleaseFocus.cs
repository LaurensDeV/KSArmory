using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The separation velocity that lands a round thrown from one tube where the tubes' mean would land,
/// and the mean where it was aimed.
///
/// <para>Predictions are of the <em>mean</em> mouth and the bus's roll is not held, so each tube lands
/// on the ground image of the ring; a kick of about <c>-offset / T</c> cancels it whatever the roll,
/// and over a ring the kicks sum to zero. <c>docs/ACCURACY-PLAN.md</c> item 41, 3db.</para>
///
/// <para>Solved from the conic, or flown through the air, rather than taken as <c>-offset / T</c>:
/// gravity's gradient bends the answer by about <c>(nT)²</c>, a few per cent at 340 s.</para>
///
/// <para>The mean's own miss is cancelled once per round after the aim commits, with nothing reading it
/// back; fed into the aim loop instead it is chased (<c>docs/ACCURACY-PLAN.md</c> 3co).</para>
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
    /// The most the release probe's miss may be given back as a separation velocity, in m/s, so a separation
    /// cannot become a burn. A 2 m miss costs 3.2 mm/s downrange and 6.0 across at 340 s; the cap refuses
    /// about 1 flight in 80 on the shipped arm (<c>docs/ACCURACY-PLAN.md</c> 3dh).
    /// </summary>
    public const double MaxMissKickMetresPerSecond = 0.010;

    /// <summary>
    /// The minimum-norm velocity that cancels where <paramref name="offsetCci"/> would put the round
    /// on the ground.
    ///
    /// <para>Square to the arrival <em>as the ground sees it</em>: along that line a displacement is only an
    /// earlier or later arrival, and an inertial arrival velocity would leave the planet's turn in.</para>
    /// </summary>
    /// <param name="positionCci">The state the prediction is flown from — the tubes' mean mouth.</param>
    /// <param name="velocityCci">What every round leaves with before the kick.</param>
    /// <param name="flightSeconds">That state's own flight time, from the prediction that flew it.</param>
    /// <param name="offsetCci">
    /// Where this tube's mouth sits from the mean, taken off the part frame and turned, never a difference of
    /// two positions: that carries 30 m per millisecond of mismatch at the ecliptic's speed.
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

        // Flown at its own size: one coast, exact, rather than a matrix column multiplied back through.
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
    /// <para>Along the ground only, so the crossing's height stays the separate correction
    /// <see cref="IcbmConfig.PredictionStopsOnTheSurface"/> makes.</para>
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
    /// <para>Measured square to local up, a miss over ground falling away at gradient <c>g</c> is cancelled wrong
    /// by <c>1 − tan γ / (tan γ − g)</c> of itself: a fifth at 0.1 on a 32° arrival.</para>
    ///
    /// <para>False where a lookup gives no ground or the chord rises further than it runs: a failed lookup
    /// reads as the mean sphere, kilometres off under high ground.</para>
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
    /// A sphere rather than the height field: each column is two landings metres apart, and the ground's texture
    /// across them is not the arc's sensitivity. The slope to the target is <see cref="TryMissOnTheGround"/>'s.
    /// </param>
    internal readonly record struct Air(ImpactPredictor.Drag Drag, double StepSeconds, double GroundRadiusMetres,
                                        bool StopOnTheSurface,
                                        bool StopOnTheTerrain = false);

    /// <summary>
    /// How a round's landing moves with its release state, flown through the air: where the nominal state lands
    /// and how, and one column per axis of release position and of release velocity, each the landing's
    /// displacement per unit, carried to the arrival.
    ///
    /// <para>Coasted in vacuum for the drag flight's time the arc ends kilometres under the ground, which left
    /// 8.8 mm of the Mk 21's spread headless at 340 s; flown, 0.001 mm. <c>docs/ACCURACY-PLAN.md</c> 3eq.</para>
    ///
    /// <para>Seven predictor flights, so flown once a salvo and carried to each release by <see cref="For"/>.</para>
    /// </summary>
    /// <param name="ArrivalSecondsPerMetre">
    /// How much later the round arrives per metre of release position, one component per axis.
    /// </param>
    /// <param name="ArrivalSecondsPerMetrePerSecond">
    /// The same per m/s of release velocity. Not recoverable from the landing columns, which are landings on one
    /// sphere; it is the third row <see cref="DivertFootprint.ArrivalClock.Pinned"/> needs.
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
        /// How far along the coast these columns may be carried to a release, in seconds of its flight time:
        /// 0.004 mm of the spread at 2 s, headless at 340 s. Assumes one coast between the two flight times.
        /// </summary>
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
        /// A change <c>τ</c> later is <c>δr − τ·δv</c>, <c>δv − τ·G·δr</c> earlier, so the position columns lose
        /// <c>τ·V·G</c> and the velocity columns <c>τ·P</c>; uncarried, 2.9 mm of spread per second.
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
    internal enum MissOutcome { NotAsked, Cancelled, Unsolved, OverTheCap, AtTheCap }

    /// <summary>What <see cref="Kick"/> gives a round, and which of its parts it could give.</summary>
    /// <param name="MissKickCci">
    /// The probe miss's own kick as solved, and past the cap as well, so a refusal can say how large
    /// it was. In <paramref name="KickCci"/> when <paramref name="Miss"/> is Cancelled, and cut to the cap
    /// along the same line when it is AtTheCap.
    /// </param>
    internal readonly record struct Separation(double3 KickCci, double3 RingKickCci, bool RingFocused,
                                               bool SpinCancelled, double3 MissKickCci, MissOutcome Miss,
                                               bool ThroughTheAir = false);

    /// <summary>
    /// The whole velocity a round is given as it separates: its ring focused on the mean's impact, the
    /// spin it was thrown with given back, the release probe's miss cancelled, any of them, or none.
    ///
    /// <para>The spin is given back exactly, so the round leaves on the state the prediction flew. The three are
    /// summed: each is a first-order displacement of the same prediction. <c>docs/ACCURACY-PLAN.md</c> 41, 42.</para>
    /// </summary>
    /// <param name="spinCci">What the round was thrown with at its mouth: <see cref="Slug.SpinVelocityEcl"/>, turned.</param>
    /// <param name="cancelMiss">
    /// The release probe's impact and target, or null to leave the mean where the aim loop put it.
    /// Refused past <paramref name="missCapMetresPerSecond"/>, or taken at it under
    /// <paramref name="missStopsAtTheCap"/>.
    /// </param>
    /// <param name="missStopsAtTheCap">
    /// <see cref="IcbmConfig.MissKickStopsAtTheCap"/>: a miss kick over the cap is given at the cap along its own
    /// line rather than refused whole.
    /// </param>
    /// <param name="throughTheAir">
    /// The ring's image and both solves flown through the air, or null to coast them in vacuum.
    /// Carried to this release along the coast; the spin is given back exactly either way.
    /// </param>
    public static Separation Kick(BallisticBody body, double3 positionCci, double3 velocityCci,
                                  double flightSeconds, double3 offsetCci, double3 spinCci,
                                  bool focusRing, bool cancelSpin, ProbeMiss? cancelMiss = null,
                                  FlownSensitivity? throughTheAir = null,
                                  double missCapMetresPerSecond = MaxMissKickMetresPerSecond,
                                  bool missStopsAtTheCap = false)
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
                double length = Vec.Len(missKick);

                if (!(length <= missCapMetresPerSecond))
                {
                    if (missStopsAtTheCap && missCapMetresPerSecond > 0.0 && double.IsFinite(length))
                    {
                        kick += missKick * (missCapMetresPerSecond / length);
                        miss = MissOutcome.AtTheCap;
                    }
                    else
                    {
                        miss = MissOutcome.OverTheCap;
                    }
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
    /// <para>The sensitivities <see cref="TryKick"/> cancels, read forwards, so a flight that kicks nothing can
    /// be checked against them (<c>docs/ACCURACY-PLAN.md</c> 3db). Slid along the arrival as the ground sees it
    /// back onto the arrival height.</para>
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

    // Per m/s of release velocity along each axis. A small step on purpose: a solve against millimetres a
    // second wants the slope, not a difference of two coasts far apart.
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
