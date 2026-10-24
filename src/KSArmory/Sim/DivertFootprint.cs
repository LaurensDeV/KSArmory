using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// How far one bus can move its own landing, as an ellipse on the ground.
///
/// <para>The columns <see cref="ReleaseFocus.FlownSensitivity"/> already flies for the separation kicks,
/// read as a region, so the display costs no flying.</para>
///
/// <para><b>Which clock the arrival is on decides the shape, and there is no default.</b>
/// <see cref="ArrivalClock.Pinned"/> is what the flight does, nearly a circle of about
/// <c>0.96 · t_go</c>; <see cref="ArrivalClock.Free"/> is a long ellipse, about <c>450 · cot γ</c> along
/// the track, 2–12x too long where the flight pins the clock. <c>docs/ICBM-GUIDANCE.md</c>, "Several
/// targets".</para>
/// </summary>
/// <param name="OrientationRad">
/// Which way the long axis points, from the frame's downrange: within a tenth of a degree of the track
/// free-clock, computed anyway. <b>Pinned it means nothing</b> and only <see cref="CostMetresPerSecond"/>
/// reads it.
/// </param>
/// <param name="Axes">
/// What a metre of landing costs on each of the bus's own control axes: <see cref="BusTrim"/> fires one
/// axis at a time, so a divert costs the sum of its parts. Unset where not to hand.
/// </param>
/// <param name="FromTheRealState">
/// Whether the columns came from the bus's own state, or the reach is the release epoch's alone. From the
/// pad the long axis is an estimate, since <c>cot γ</c> belongs to the arc flown; the short axis,
/// <c>0.96 · t_go</c>, is exact anywhere, <b>and pinned it is the whole footprint</b>
/// (<see cref="TryAtTheEpoch"/>).
/// </param>
internal readonly record struct DivertFootprint(double SemiMajorMetresPerMetrePerSecond,
                                                double SemiMinorMetresPerMetrePerSecond,
                                                double OrientationRad,
                                                DivertFootprint.ArrivalClock Clock,
                                                ArrivalFrame Frame,
                                                double FlightSeconds,
                                                bool FromTheRealState,
                                                DivertFootprint.AxisPrice Axes = default)
{
    /// <summary>
    /// The velocity change per metre of landing along and across the track, resolved on the bus's nose, right and
    /// down, with the arrival pinned.
    /// </summary>
    /// <param name="ArrivalSecondsPerMetreAlong">
    /// How much later the arrival comes per metre the landing moves along the track, on a free arrival's cheapest arc;
    /// zero pinned, where the clock is held.
    /// </param>
    /// <param name="ArrivalSecondsPerMetreAcross"><inheritdoc cref="ArrivalSecondsPerMetreAlong"/></param>
    internal readonly record struct AxisPrice(double3 PerMetreAlong, double3 PerMetreAcross,
                                              double ArrivalSecondsPerMetreAlong = 0.0,
                                              double ArrivalSecondsPerMetreAcross = 0.0)
    {
        public bool Known => Vec.IsFinite(PerMetreAlong) && Vec.IsFinite(PerMetreAcross)
                             && (Vec.Len2(PerMetreAlong) > 0.0 || Vec.Len2(PerMetreAcross) > 0.0);
    }

    /// <summary>
    /// The most a divert's per-axis sum can be over its length, which is three orthogonal axes taking equal
    /// parts — what a divert is priced at where the axes are not known, so the price stays a floor.
    /// </summary>
    public static readonly double AxisSumCeiling = Math.Sqrt(3.0);


    /// <summary>What the arrival instant is allowed to do while the bus moves its landing.</summary>
    internal enum ArrivalClock
    {
        /// <summary>
        /// Held where the flight holds it (latched at closed-loop handover, every later arc solved to it), so
        /// a divert is an exactly-determined 3x3: two ground conditions and the clock. <b>The reach a player
        /// has today.</b> The clock costs along the track and nothing across it.
        /// </summary>
        Pinned,

        /// <summary>
        /// Left wherever it falls, the minimum-norm solve <see cref="ReleaseFocus"/> does for a kick. <b>Only
        /// a release loop that re-commits the arrival per destination could offer it</b>, and none does.
        /// </summary>
        Free,
    }

    /// <summary>
    /// How far a metre a second of divert moves the landing, per second still to go at the release.
    ///
    /// <para><b>The floor of the measured band, not its middle</b>, because it is drawn before the flight
    /// can check it: 0.889 at 745 s over 60–760 s, rounded down. <c>docs/ICBM-GUIDANCE.md</c>, "The pad can
    /// draw the pinned reach".</para>
    /// </summary>
    public const double PinnedMetresPerSecondToGo = 0.88;

    /// <summary>
    /// The release epochs <see cref="PinnedMetresPerSecondToGo"/> is a floor over, in seconds before
    /// arrival.
    ///
    /// <para>The ratio decays with the epoch (0.76 at 1,200 s), so a release gate outside this is refused
    /// rather than estimated.</para>
    /// </summary>
    public const double EpochLeastSeconds = 60.0;

    /// <inheritdoc cref="EpochLeastSeconds"/>
    public const double EpochMostSeconds = 760.0;

    /// <summary>
    /// The reach a release epoch alone gives, for a flight that has not flown yet.
    /// </summary>
    /// <remarks>
    /// <b>Nothing here reads a state</b>: before the flight the projected cutoff is the pad, whose arc
    /// ploughs through the atmosphere (<see cref="AimCorrection.DepartureIsWorthObserving"/>). Pinned, both
    /// axes are the epoch's anyway.
    /// </remarks>
    /// <param name="frame">
    /// The axes the reach is measured in, at the landing <em>as it will be at arrival</em> — the same epoch
    /// <see cref="TryFrom"/>'s frame belongs to, so the carries in <c>ReachDisplay</c> mean the same thing
    /// under both. Pinned the orientation means nothing and only the plane is read.
    /// </param>
    /// <param name="secondsToGo">
    /// How long after the release the warheads arrive: <see cref="IcbmConfig.ReleaseBeforeArrivalSeconds"/>
    /// under <see cref="ReleaseItinerary"/>, whose last release lands on the gate.
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
            // Holding the arrival projects the clock row's direction out of each row. A row that cannot
            // move the clock constrains nothing, and the pinned answer is the free one.
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

    /// <summary>
    /// What the trim pays to move the landing this far: the divert's parts on the bus's axes summed, or
    /// <see cref="AxisSumCeiling"/> times its length where the axes are not known.
    /// </summary>
    public double TrimCostMetresPerSecond(double alongMetres, double crossMetres)
    {
        if (!Axes.Known) return AxisSumCeiling * CostMetresPerSecond(alongMetres, crossMetres);

        double3 dv = (Axes.PerMetreAlong * alongMetres) + (Axes.PerMetreAcross * crossMetres);
        return Math.Abs(dv.X) + Math.Abs(dv.Y) + Math.Abs(dv.Z);
    }

    /// <summary>
    /// How much later a free divert this far makes the arrival, negative for earlier: about 0.23 s per km uprange at
    /// 12,902 km, which is 92 s on a 400 km hop. Zero where the axes are not known or the clock is pinned.
    /// </summary>
    public double ArrivalShiftSeconds(double alongMetres, double crossMetres)
        => Axes.Known ? (Axes.ArrivalSecondsPerMetreAlong * alongMetres) + (Axes.ArrivalSecondsPerMetreAcross * crossMetres)
                      : 0.0;

    /// <summary>
    /// What one engine pushing along the divert pays to move the landing this far: the velocity change's length, or
    /// <see cref="CostMetresPerSecond"/> where the axes are not known.
    /// </summary>
    public double LengthCostMetresPerSecond(double alongMetres, double crossMetres)
        => Axes.Known
               ? Vec.Len((Axes.PerMetreAlong * alongMetres) + (Axes.PerMetreAcross * crossMetres))
               : CostMetresPerSecond(alongMetres, crossMetres);

    /// <summary>
    /// The worst ratio of what the trim pays to the divert's length over every bearing on the ground: what the
    /// ring is shrunk by so that every point inside it is affordable.
    /// </summary>
    public double WorstAxisFactor()
    {
        if (!Axes.Known) return AxisSumCeiling;

        double worst = 1.0;
        for (int i = 0; i < AxisFactorBearings; i++)
        {
            double bearing = Math.PI * i / AxisFactorBearings;
            double along = Math.Cos(bearing), cross = Math.Sin(bearing);
            double length = CostMetresPerSecond(along, cross);

            if (length > 0.0 && double.IsFinite(length))
            {
                worst = Math.Max(worst, TrimCostMetresPerSecond(along, cross) / length);
            }
        }

        return Math.Min(worst, AxisSumCeiling);
    }

    // Over half a turn, since the cost is even in the displacement: a degree apart.
    private const int AxisFactorBearings = 180;

    /// <summary>
    /// Read the per-axis price off the same columns <see cref="TryFrom"/> reads the ellipse off, for a bus
    /// holding <paramref name="noseCci"/>, <paramref name="rightCci"/> and <paramref name="downCci"/>.
    /// </summary>
    /// <remarks>
    /// Pinned, a divert is the exactly-determined 3x3 the trim solves — the two ground rows and the clock — so
    /// each column of its inverse is a cross product of the other two rows over the determinant. Free, it is the
    /// minimum-norm answer to the two ground rows alone, which is the velocity the cheapest arc asks for.
    /// </remarks>
    public static bool TryAxisPrice(BallisticBody body, ReleaseFocus.FlownSensitivity flown, double3 noseCci,
                                    double3 rightCci, double3 downCci, out AxisPrice price,
                                    ArrivalClock clock = ArrivalClock.Pinned)
    {
        price = default;

        if (!(Vec.Len2(noseCci) > 0.0) || !(Vec.Len2(rightCci) > 0.0) || !(Vec.Len2(downCci) > 0.0)) return false;

        double3 overGround = flown.ArrivalVelocityCci - body.GroundVelocityCci(flown.ArrivedCci);
        if (!ArrivalFrame.TryAt(flown.ArrivedCci, overGround, out ArrivalFrame frame)) return false;

        double3 along = new(Vec.Dot(flown.VelocityX, frame.Downrange), Vec.Dot(flown.VelocityY, frame.Downrange),
                            Vec.Dot(flown.VelocityZ, frame.Downrange));
        double3 cross = new(Vec.Dot(flown.VelocityX, frame.Cross), Vec.Dot(flown.VelocityY, frame.Cross),
                            Vec.Dot(flown.VelocityZ, frame.Cross));
        double3 perAlong, perAcross;
        if (clock == ArrivalClock.Free)
        {
            double aa = Vec.Dot(along, along), ac = Vec.Dot(along, cross), cc = Vec.Dot(cross, cross);
            double gram = (aa * cc) - (ac * ac);
            if (!(gram > 0.0) || !double.IsFinite(gram)) return false;

            perAlong = ((along * cc) - (cross * ac)) / gram;
            perAcross = ((cross * aa) - (along * ac)) / gram;
        }
        else
        {
            double3 slower = flown.ArrivalSecondsPerMetrePerSecond;

            double det = Vec.Dot(along, Vec.Cross(cross, slower));
            if (!(Math.Abs(det) > 0.0) || !double.IsFinite(det)) return false;

            perAlong = Vec.Cross(cross, slower) / det;
            perAcross = Vec.Cross(slower, along) / det;
        }

        double3 nose = Vec.Unit(noseCci), right = Vec.Unit(rightCci), down = Vec.Unit(downCci);
        double3 later = flown.ArrivalSecondsPerMetrePerSecond;
        bool free = clock == ArrivalClock.Free && Vec.IsFinite(later);

        price = new AxisPrice(new double3(Vec.Dot(perAlong, nose), Vec.Dot(perAlong, right), Vec.Dot(perAlong, down)),
                              new double3(Vec.Dot(perAcross, nose), Vec.Dot(perAcross, right),
                                          Vec.Dot(perAcross, down)),
                              free ? Vec.Dot(later, perAlong) : 0.0,
                              free ? Vec.Dot(later, perAcross) : 0.0);
        return price.Known;
    }

    /// <summary>How many epochs <see cref="IcbmConfig.FreeHopsMeasureTheirSlots"/> flies the reach at past the first.</summary>
    public const int LaterEpochs = 4;

    /// <summary>
    /// <see cref="LaterEpochs"/> epochs before arrival, falling, evenly spaced on log axes from
    /// <paramref name="secondsToGo"/> down to <paramref name="gateSeconds"/>; empty where the gate is not before it.
    /// </summary>
    public static double[] LaterEpochsBeforeArrival(double secondsToGo, double gateSeconds)
    {
        if (!(gateSeconds > 0.0) || !(secondsToGo > gateSeconds) || !double.IsFinite(secondsToGo)) return [];

        double[] epochs = new double[LaterEpochs];
        for (int i = 1; i <= LaterEpochs; i++)
        {
            epochs[i - 1] = secondsToGo * Math.Pow(gateSeconds / secondsToGo, (double)i / LaterEpochs);
        }

        return epochs;
    }

    /// <summary>
    /// The free reach, with its per-axis price on the given axes, flown from where a vacuum coast from
    /// <paramref name="positionCci"/> is <paramref name="beforeArrivalSeconds"/> before an arrival
    /// <paramref name="secondsToGo"/> away: <see cref="IcbmConfig.FreeHopsMeasureTheirSlots"/>.
    /// </summary>
    public static bool TryFreeLater(BallisticBody body, double3 positionCci, double3 velocityCci, double secondsToGo,
                                    double beforeArrivalSeconds, in ReleaseFocus.Air air, double3 noseCci,
                                    double3 rightCci, double3 downCci, out DivertFootprint later)
    {
        later = default;

        double coast = secondsToGo - beforeArrivalSeconds;
        if (!(coast > 0.0) || !(beforeArrivalSeconds > 0.0)
            || !Kepler.TryCoast(body.Mu, positionCci, velocityCci, coast, out double3 p, out double3 v))
        {
            return false;
        }

        if (ReleaseFocus.FlownSensitivity.TryFly(body, p, v, air) is not { } flown
            || !TryFrom(body, flown, ArrivalClock.Free, fromTheRealState: true, out DivertFootprint footprint)
            || !TryAxisPrice(body, flown, noseCci, rightCci, downCci, out AxisPrice axes, ArrivalClock.Free))
        {
            return false;
        }

        later = footprint with { Axes = axes };
        return true;
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
