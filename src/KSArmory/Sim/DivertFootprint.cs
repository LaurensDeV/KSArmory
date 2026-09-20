using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// How far one bus can move its own landing, as an ellipse on the ground.
///
/// <para>Every later target is a place the bus can divert to with the propellant it has left, and
/// <see cref="ReleaseFocus.FlownSensitivity"/> already flies the columns that say how far a metre a
/// second of release velocity moves the landing — once per salvo, for the separation kicks. This is
/// those same columns read as a region rather than as a correction, so the display costs no flying.</para>
///
/// <para><b>Which clock the arrival is on decides the shape, and there is no default.</b>
/// <see cref="ArrivalClock.Pinned"/> is what the flight does and is nearly a circle;
/// <see cref="ArrivalClock.Free"/> is a long ellipse along the track and is only honest if a release loop
/// re-commits the arrival per destination. Drawing the free one where the flight pins the clock shows a
/// reach 2–12x too long, so <see cref="TryFrom"/> takes the choice rather than offering one.
/// <c>docs/MIRV-TARGETS.md</c>.</para>
///
/// <para><b>Free-clock, the long axis belongs to the arrival angle, not to the range.</b> It runs about
/// <c>450 · cot γ</c> metres per m/s at the release epoch, so it is 25:1 at 9°, 3:1 at 20° and 2:1 at
/// 33°. Pinned, both axes collapse onto the cross-track reach, about <c>0.96 · t_go</c>. A typed shape
/// would be wrong at every geometry under either clock.</para>
/// </summary>
/// <param name="OrientationRad">
/// Which way the long axis points, from the frame's downrange. Free-clock it is nearly always zero — the
/// major axis sits within a tenth of a degree of the ground track at every geometry measured — and
/// computed anyway, because "nearly always" is how an assumption gets shipped. <b>Pinned it means
/// nothing</b>, the two axes agreeing to within 3%, and only <see cref="CostMetresPerSecond"/> should read
/// it there.
/// </param>
/// <param name="FromTheRealState">
/// Whether the columns came from the bus's own state, or the reach is the release epoch's alone. A
/// footprint drawn from the pad is an estimate whose long axis was out by 1.04x, 0.51x and 0.59x at three
/// flown geometries, because <c>cot γ</c> belongs to the arc actually flown and the pad does not know it
/// yet. The short axis is exact from anywhere, being <c>0.96 · t_go</c> on a time the player already set —
/// <b>and pinned that is the whole footprint</b>, which is what <see cref="TryAtTheEpoch"/> is.
/// </param>
internal readonly record struct DivertFootprint(double SemiMajorMetresPerMetrePerSecond,
                                                double SemiMinorMetresPerMetrePerSecond,
                                                double OrientationRad,
                                                DivertFootprint.ArrivalClock Clock,
                                                ArrivalFrame Frame,
                                                double FlightSeconds,
                                                bool FromTheRealState)
{
    /// <summary>What the arrival instant is allowed to do while the bus moves its landing.</summary>
    internal enum ArrivalClock
    {
        /// <summary>
        /// Held where the flight holds it. <c>IcbmProgram</c> latches the arrival at closed-loop handover and
        /// <c>ResolveCoastArc</c> solves every later arc to that same instant, so a divert is an
        /// exactly-determined 3x3 rather than a minimum-norm 2x3: three unknowns, two ground conditions and the
        /// clock. <b>This is the reach a player actually has today.</b>
        ///
        /// <para>The extra condition costs along the track and nothing across it — 1.94x at the 6,179 km release
        /// gate, 2.56x there at cutoff and 11.94x at 12,902 km, against 1.00x across at every geometry, because
        /// moving a target across the track does not change when it is reached.</para>
        /// </summary>
        Pinned,

        /// <summary>
        /// Left wherever it falls, which is the minimum-norm solve <see cref="ReleaseFocus"/> does for a
        /// separation kick. <b>Only a reach a release loop that re-commits the arrival per destination could
        /// offer</b>, and nothing does that today — the latch lives inside <c>Resolve</c>, which the coast
        /// never reaches.
        /// </summary>
        Free,
    }

    /// <summary>
    /// How far a metre a second of divert moves the landing, per second still to go at the release.
    ///
    /// <para><b>The floor of the measured band, not its middle.</b> Swept over 2,002–12,787 km of range,
    /// 13.3°–52.2° of arrival and loft 1.0 and 1.35, the pinned reach at a 420 s release runs
    /// <b>0.936 to 0.967 · t_go</b> while the <em>free</em> long axis over the same matrix swings 497 to
    /// 1,816 m per m/s — so the pinned reach is the epoch and nothing else. Over 60 to 760 s the floor is
    /// 0.889, at 745 s, which is what this is rounded down from. The Moon reads 0.977–0.980 at the same
    /// epoch, so Earth is the binding body: the ratio is <c>sinc(sqrt(mu/R³) · t_go)</c> and Earth has about
    /// the shortest surface-grazing period in the system.</para>
    ///
    /// <para>A floor rather than a fit because it is drawn before the flight can check it: promising ground
    /// the bus cannot reach is the one failure a region display makes silently.</para>
    /// </summary>
    public const double PinnedMetresPerSecondToGo = 0.88;

    /// <summary>
    /// The release epochs <see cref="PinnedMetresPerSecondToGo"/> is a floor over, in seconds before
    /// arrival.
    ///
    /// <para>It decays with the epoch — 0.96 at 420 s, 0.90 at 745, <b>0.76 at 1,200</b> — because the
    /// out-of-plane response is harmonic about the body and saturates over a quarter period. So a release
    /// gate outside this is refused rather than estimated: the constant stops being a floor and the disc
    /// stops being a disc, the two axes parting 16% at 1,200 s.</para>
    /// </summary>
    public const double EpochLeastSeconds = 60.0;

    /// <inheritdoc cref="EpochLeastSeconds"/>
    public const double EpochMostSeconds = 760.0;

    /// <summary>
    /// The reach a release epoch alone gives, for a flight that has not flown yet.
    /// </summary>
    /// <remarks>
    /// <b>Nothing here reads a state, and that is what makes it honest.</b> The tempting version flies the
    /// columns from the guidance's projected cutoff — but before the vehicle has flown that projection is
    /// the pad, so the arc ploughs through the whole atmosphere; the aim loop read <b>1,522 km</b> of
    /// phantom miss off exactly that and <see cref="AimCorrection.DepartureIsWorthObserving"/> is the guard
    /// it needed. Pinned, there is nothing in the footprint the arc could tell us: both axes are the epoch.
    /// </remarks>
    /// <param name="frame">
    /// The axes the reach is measured in, at the landing <em>as it will be at arrival</em> — the same epoch
    /// <see cref="TryFrom"/>'s frame belongs to, so the carries in <c>ReachDisplay</c> mean the same thing
    /// under both. Pinned the orientation means nothing and only the plane is read.
    /// </param>
    /// <param name="secondsToGo">
    /// How long after the release the warheads arrive, which under <see cref="ReleaseItinerary"/> is
    /// <see cref="IcbmConfig.ReleaseBeforeArrivalSeconds"/> exactly: every further target is released
    /// <em>earlier</em> and the last one lands on the gate, so a new target's hop is always bought there.
    /// </param>
    public static bool TryAtTheEpoch(ArrivalFrame frame, double secondsToGo, out DivertFootprint footprint)
    {
        footprint = default;

        if (!(secondsToGo >= EpochLeastSeconds) || !(secondsToGo <= EpochMostSeconds)) return false;

        double reach = PinnedMetresPerSecondToGo * secondsToGo;

        footprint = new DivertFootprint(reach, reach, 0.0, ArrivalClock.Pinned, frame, secondsToGo,
                                        FromTheRealState: false);
        return true;
    }

    /// <summary>
    /// Read the ellipse off the columns a salvo has already flown.
    /// </summary>
    /// <remarks>
    /// The arrival frame is taken against the ground-relative velocity, as <c>ReleaseFocus.TryCancel</c>
    /// does: the semi-axes are the same either way, because both frames span one horizontal plane, but
    /// the orientation and the arrival angle are not — inertially they read 5.5° and a degree out.
    /// </remarks>
    public static bool TryFrom(BallisticBody body, ReleaseFocus.FlownSensitivity flown, ArrivalClock clock,
                               bool fromTheRealState, out DivertFootprint footprint)
    {
        footprint = default;

        double3 overGround = flown.ArrivalVelocityCci - body.GroundVelocityCci(flown.ArrivedCci);
        if (!ArrivalFrame.TryAt(flown.ArrivedCci, overGround, out ArrivalFrame frame)) return false;

        // The 2x3 map from release velocity to ground displacement, as its two rows.
        double3 along = new(Vec.Dot(flown.VelocityX, frame.Downrange), Vec.Dot(flown.VelocityY, frame.Downrange),
                            Vec.Dot(flown.VelocityZ, frame.Downrange));
        double3 cross = new(Vec.Dot(flown.VelocityX, frame.Cross), Vec.Dot(flown.VelocityY, frame.Cross),
                            Vec.Dot(flown.VelocityZ, frame.Cross));

        if (clock == ArrivalClock.Pinned)
        {
            // Holding the arrival leaves only the release velocities square to the clock row, so the reach is
            // the same map with that one direction taken out of each row — a projection, not a second solve.
            // A row that cannot move the clock at all constrains nothing, and the free answer is then the
            // pinned one rather than a refusal.
            double3 slower = flown.ArrivalSecondsPerMetrePerSecond;
            double authority = Vec.Len2(slower);

            if (!double.IsFinite(authority)) return false;

            if (authority > 0.0)
            {
                along = along - slower * (Vec.Dot(along, slower) / authority);
                cross = cross - slower * (Vec.Dot(cross, slower) / authority);
            }
        }

        // The singular values of that map are the square roots of the eigenvalues of its row Gram,
        // which is 2x2 and closed form -- no sweep, and nothing to converge.
        double m11 = Vec.Dot(along, along);
        double m12 = Vec.Dot(along, cross);
        double m22 = Vec.Dot(cross, cross);

        double trace = m11 + m22;
        double gap = Math.Sqrt(Math.Max(0.0, ((trace * trace) / 4.0) - ((m11 * m22) - (m12 * m12))));

        double major = Math.Sqrt(Math.Max(0.0, (trace / 2.0) + gap));
        double minor = Math.Sqrt(Math.Max(0.0, (trace / 2.0) - gap));

        if (!(major > 0.0) || !double.IsFinite(major) || !double.IsFinite(minor)) return false;

        footprint = new DivertFootprint(major, minor, 0.5 * Math.Atan2(2.0 * m12, m11 - m22), clock,
                                        frame, flown.FlightSeconds, fromTheRealState);
        return true;
    }

    /// <summary>How far the landing moves along the ground track per metre a second of divert.</summary>
    public double AlongTrackMetresPerMetrePerSecond => Edge(1.0, 0.0);

    /// <summary>The same across the track, which is the axis the arrival clock leaves alone.</summary>
    public double CrossTrackMetresPerMetrePerSecond => Edge(0.0, 1.0);

    /// <summary>What a wanted ground displacement costs, in metres a second of divert.</summary>
    /// <remarks>
    /// The inverse of the ellipse: a displacement resolved onto the axes, each divided by that axis's
    /// reach. This is what a cursor shows as "3 targets, 12 m/s left".
    /// </remarks>
    public double CostMetresPerSecond(double alongMetres, double crossMetres)
    {
        double cos = Math.Cos(OrientationRad), sin = Math.Sin(OrientationRad);

        double onMajor = (alongMetres * cos) + (crossMetres * sin);
        double onMinor = (crossMetres * cos) - (alongMetres * sin);

        double major = onMajor / SemiMajorMetresPerMetrePerSecond;
        double minor = SemiMinorMetresPerMetrePerSecond > 0.0 ? onMinor / SemiMinorMetresPerMetrePerSecond
                                                              : (onMinor == 0.0 ? 0.0 : double.PositiveInfinity);

        return Math.Sqrt((major * major) + (minor * minor));
    }

    /// <summary>Whether a displacement is inside what the stated budget can pay for.</summary>
    public bool Reaches(double alongMetres, double crossMetres, double budgetMetresPerSecond)
        => CostMetresPerSecond(alongMetres, crossMetres) <= budgetMetresPerSecond;

    // The ellipse's edge along one bearing. The cost is homogeneous in the displacement, so a unit
    // direction's cost inverts straight into how far that direction reaches for one metre a second.
    private double Edge(double alongUnit, double crossUnit)
    {
        double cost = CostMetresPerSecond(alongUnit, crossUnit);
        return cost > 0.0 ? 1.0 / cost : double.PositiveInfinity;
    }
}
