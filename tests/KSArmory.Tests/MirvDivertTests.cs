using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What a bus can actually divert a warhead by, and what that costs — phase 0 of
/// <c>docs/MIRV-TARGETS.md</c>.
///
/// <para><b>Measurement only.</b> Nothing here asserts an improvement and nothing here is flown by
/// the game; the assertions are sanity checks on the rig, so a geometry that stops reconstructing
/// fails loudly rather than printing nonsense.</para>
///
/// <para><b>The release state is the one that matters, not the cutoff state.</b>
/// <see cref="IcbmConfig.ReleaseBeforeArrivalSeconds"/> holds the warheads until 420 s before
/// arrival, and the flown night lets them go at a median 356 s — so the divert leverage a second
/// target would be aimed with is the leverage left at <em>that</em> epoch, which is a fraction of
/// the cutoff figures <c>docs/ICBM-GUIDANCE.md</c> quotes.</para>
///
/// <para>Flown against the mean sphere on purpose: a sensitivity is a property of the arc, and real
/// relief makes two probes land on different hillsides — the same reason
/// <see cref="HoldingCost"/> refuses a terrain lookup.</para>
/// </summary>
public sealed class MirvDivertTests(ITestOutputHelper Out)
{
    private static BallisticBody Earth => DeorbitShot.Earth;

    private const double R = DeorbitShot.R;
    private const double Mu = DeorbitShot.Mu;

    /// <summary>The predictor's coarse step, as the flight itself flies it.</summary>
    private const double Step = 0.25;

    private static ImpactPredictor.Drag Air => new(DeorbitShot.DensityAt, Arsenal.ReentryVehicleMk21);

    /// <summary>One shot, as a release state: where the bus is and what it is doing.</summary>
    private readonly record struct Shot(string Name, double3 PositionCci, double3 VelocityCci);

    /// <summary>
    /// The state a warhead is actually released on at 6,179 km, off the flown log.
    ///
    /// <para><c>~/shots/2026-09-17-threearm</c>, shot 001, <c>GeoSat FAT_1</c> round 1 — the
    /// <c>warhead trace ... round 1 away</c> line. 866.8 km up, 4,889 m/s inertial, 359.3 s of
    /// flight left.</para>
    /// </summary>
    private static Shot Flown6179 => new("6,179 km, flown release",
                                         new double3(3_835_254.4, -5_998_331.6, -1_302_454.7),
                                         new double3(279.7753, 2844.5295, -3967.0558));

    /// <summary>
    /// The other two geometries the batch nights are scored on, reconstructed the way
    /// <see cref="MissSensitivityTests"/> reconstructs them — cutoff altitude, downrange distance
    /// and flight time, all three off the flights' own logs.
    /// </summary>
    private static Shot Cutoff(string name, double altitude, double downrange, double flightSeconds)
    {
        double3 from = new(R + altitude, 0, 0);
        double theta = downrange / R;
        double3 arrival = new(R * Math.Cos(theta), R * Math.Sin(theta), 0);

        Assert.True(BallisticArc.TrySolve(Earth, from, Earth.UncarryCci(arrival, flightSeconds),
                                          flightSeconds, out BallisticArc.Solution arc),
                    $"{name} does not reconstruct");

        return new Shot(name, from, arc.RequiredVelocityCci);
    }

    private static Shot Cutoff2000 => Cutoff("2,000 km, at cutoff", 142_000.0, 2_000_000.0, 425.0);

    private static Shot Cutoff12902 => Cutoff("12,902 km, at cutoff", 157_000.0, 12_902_000.0, 1881.0);

    /// <summary>The state <paramref name="seconds"/> earlier on the same conic, for an earlier separation.</summary>
    private static Shot Back(Shot shot, double seconds, string? name = null)
    {
        Assert.True(Kepler.TryCoast(Mu, shot.PositionCci, -shot.VelocityCci, seconds,
                                    out double3 p, out double3 v));

        return shot with { PositionCci = p, VelocityCci = -v, Name = name ?? shot.Name };
    }

    /// <summary>The state <paramref name="seconds"/> later on the same conic.</summary>
    private static Shot Forward(Shot shot, double seconds, string? name = null)
    {
        Assert.True(Kepler.TryCoast(Mu, shot.PositionCci, shot.VelocityCci, seconds,
                                    out double3 p, out double3 v));

        return shot with { PositionCci = p, VelocityCci = v, Name = name ?? shot.Name };
    }

    /// <summary>
    /// The three shots as they are at the epoch the warheads are let go of today — 420 s before
    /// arrival by <see cref="IcbmConfig.ReleaseBeforeArrivalSeconds"/>, a median 356 s in flight.
    /// </summary>
    private static Shot Release2000 => Forward(Cutoff2000, 425.0 - 360.0, "2,000 km, at the release gate");

    private static Shot Release12902 => Forward(Cutoff12902, 1881.0 - 360.0, "12,902 km, at the release gate");

    /// <summary>The 6,179 km shot at its own cutoff, back along the conic the flown release sits on.</summary>
    private static Shot Cutoff6179 => Back(Flown6179, 1340.0 - 359.3, "6,179 km, at cutoff");

    private static bool Land(Shot shot, double3 kickCci, out ImpactPredictor.Impact impact)
        => ImpactPredictor.TryPredict(Earth, shot.PositionCci, shot.VelocityCci + kickCci, Step,
                                      ImpactPredictor.DefaultMaxSeconds, out impact,
                                      terrainRadiusAt: null, pathCci: null, drag: Air,
                                      stopOnTheSurface: true);

    /// <summary>
    /// The frame a miss is read in at the arrival: along the ground track, across it, and the clock.
    ///
    /// <para>Taken against the <em>ground</em>, so the track is the arrival velocity less what the
    /// planet's own turn contributes — the same frame <see cref="ReleaseFocus"/> cancels a ring in,
    /// and the one a player reads a footprint in.</para>
    /// </summary>
    private readonly record struct GroundFrame(double3 Downrange, double3 Cross, double3 Up);

    private static GroundFrame FrameAt(ImpactPredictor.Impact impact)
    {
        double3 up = Vec.Unit(impact.PointCci);
        double3 overGround = impact.VelocityCci - Earth.GroundVelocityCci(impact.PointCci);
        double3 downrange = Vec.Unit(overGround - up * Vec.Dot(overGround, up));

        return new GroundFrame(downrange, Vec.Unit(Vec.Cross(up, downrange)), up);
    }

    /// <summary>
    /// How a release velocity moves the landing and the arrival clock: three columns, one per axis
    /// of the release state's own frame.
    /// </summary>
    /// <param name="Along">Metres of downrange movement per m/s, per axis.</param>
    /// <param name="Cross">Metres across the track per m/s, per axis.</param>
    /// <param name="Clock">Seconds of arrival delay per m/s, per axis.</param>
    private readonly record struct Jacobian(double3 Along, double3 Cross, double3 Clock,
                                            double FlightSeconds, double ArrivalDeg,
                                            double3[] Axes, string[] Names);

    /// <summary>
    /// The columns, flown. Central differences at half a metre a second, which is the scale the
    /// trim actually works at and small enough that the arc's curvature does not enter.
    /// </summary>
    private static Jacobian Columns(Shot shot)
    {
        Assert.True(Land(shot, Vec.Zero, out ImpactPredictor.Impact nominal), $"{shot.Name} does not land");

        GroundFrame frame = FrameAt(nominal);

        double3 prograde = Vec.Unit(shot.VelocityCci);
        double3 up = Vec.Unit(shot.PositionCci);
        double3 cross = Vec.Unit(Vec.Cross(up, prograde));

        // Radial square to prograde rather than straight up, so the three are orthonormal and the
        // singular values below mean what they say.
        double3 radial = Vec.Unit(Vec.Cross(prograde, cross));

        double3[] axes = [prograde, radial, cross];
        double along = 0.0, across = 0.0, clock = 0.0;
        double3 alongCol = default, crossCol = default, clockCol = default;

        const double delta = 0.5;

        for (int i = 0; i < 3; i++)
        {
            Assert.True(Land(shot, axes[i] * delta, out ImpactPredictor.Impact plus));
            Assert.True(Land(shot, axes[i] * -delta, out ImpactPredictor.Impact minus));

            // Ground-fixed, because the two probes arrive at different instants and an inertial
            // difference would carry the planet's turn across that gap.
            double3 moved = (plus.GroundFixedPointCci - minus.GroundFixedPointCci) / (2.0 * delta);

            along = Vec.Dot(moved, frame.Downrange);
            across = Vec.Dot(moved, frame.Cross);
            clock = (plus.Seconds - minus.Seconds) / (2.0 * delta);

            alongCol = Set(alongCol, i, along);
            crossCol = Set(crossCol, i, across);
            clockCol = Set(clockCol, i, clock);
        }

        double arrival = BallisticArc.DescentAngleDeg(
            nominal.PointCci, nominal.VelocityCci - Earth.GroundVelocityCci(nominal.PointCci));

        return new Jacobian(alongCol, crossCol, clockCol, nominal.Seconds, arrival, axes,
                            ["prograde", "radial", "cross-track"]);
    }

    private static double3 Set(double3 v, int i, double value)
        => i switch
        {
            0 => new double3(value, v.Y, v.Z),
            1 => new double3(v.X, value, v.Z),
            _ => new double3(v.X, v.Y, value),
        };

    /// <summary>
    /// The footprint one metre a second of divert reaches, as the two semi-axes of the ellipse a
    /// sphere of release velocity maps onto the ground.
    ///
    /// <para>The singular values of the 2x3 ground Jacobian. There are only two because a velocity
    /// change along one direction moves the arrival clock and nothing else — which is exactly the
    /// direction a staggered arrival is bought along.</para>
    /// </summary>
    private static (double Major, double Minor, double3 MajorAxis, double3 MinorAxis) Footprint(Jacobian j)
    {
        // J^T J is 3x3, symmetric, rank 2. Its eigenvalues are the squared semi-axes.
        double[,] m = new double[3, 3];

        double[] a = [j.Along.X, j.Along.Y, j.Along.Z];
        double[] c = [j.Cross.X, j.Cross.Y, j.Cross.Z];

        for (int i = 0; i < 3; i++)
        {
            for (int k = 0; k < 3; k++) m[i, k] = a[i] * a[k] + c[i] * c[k];
        }

        (double value, double[] vector)[] eigen = Jacobi(m);
        Array.Sort(eigen, (x, y) => y.value.CompareTo(x.value));

        double3 Axis((double value, double[] vector) e)
            => Vec.Unit(j.Axes[0] * e.vector[0] + j.Axes[1] * e.vector[1] + j.Axes[2] * e.vector[2]);

        return (Math.Sqrt(Math.Max(0.0, eigen[0].value)), Math.Sqrt(Math.Max(0.0, eigen[1].value)),
                Axis(eigen[0]), Axis(eigen[1]));
    }

    /// <summary>Jacobi eigenvalues of a 3x3 symmetric matrix, which is all this file needs of linear algebra.</summary>
    private static (double, double[])[] Jacobi(double[,] a)
    {
        double[,] v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

        for (int sweep = 0; sweep < 64; sweep++)
        {
            double off = a[0, 1] * a[0, 1] + a[0, 2] * a[0, 2] + a[1, 2] * a[1, 2];
            if (off < 1e-18 * (a[0, 0] * a[0, 0] + a[1, 1] * a[1, 1] + a[2, 2] * a[2, 2] + 1.0)) break;

            for (int p = 0; p < 2; p++)
            {
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-30) continue;

                    double theta = 0.5 * Math.Atan2(2.0 * a[p, q], a[q, q] - a[p, p]);
                    double c = Math.Cos(theta), s = Math.Sin(theta);

                    for (int i = 0; i < 3; i++)
                    {
                        double aip = a[i, p], aiq = a[i, q];
                        a[i, p] = c * aip - s * aiq;
                        a[i, q] = s * aip + c * aiq;
                    }

                    for (int i = 0; i < 3; i++)
                    {
                        double api = a[p, i], aqi = a[q, i];
                        a[p, i] = c * api - s * aqi;
                        a[q, i] = s * api + c * aqi;

                        double vip = v[i, p], viq = v[i, q];
                        v[i, p] = c * vip - s * viq;
                        v[i, q] = s * vip + c * viq;
                    }
                }
            }
        }

        return
        [
            (a[0, 0], [v[0, 0], v[1, 0], v[2, 0]]),
            (a[1, 1], [v[0, 1], v[1, 1], v[2, 1]]),
            (a[2, 2], [v[0, 2], v[1, 2], v[2, 2]]),
        ];
    }

    /// <summary>
    /// The smallest release velocity that moves the landing by a wanted ground displacement,
    /// leaving the arrival clock wherever it falls.
    ///
    /// <para>Two conditions on three unknowns, so it is the same minimum-norm solve
    /// <see cref="ReleaseFocus"/> does — and pinning the clock as well would price a divert nobody
    /// asked for: a target moved along the track is reached sooner or later, and only a second
    /// warhead on the <em>same</em> point cares when.</para>
    /// </summary>
    private static double3 MinimumNorm(Jacobian j, double alongMetres, double crossMetres)
    {
        double[] a = [j.Along.X, j.Along.Y, j.Along.Z];
        double[] c = [j.Cross.X, j.Cross.Y, j.Cross.Z];

        double m11 = Dot(a, a), m12 = Dot(a, c), m22 = Dot(c, c);
        double det = m11 * m22 - m12 * m12;

        Assert.True(det > 0.0, "the ground columns are degenerate");

        double y1 = (m22 * alongMetres - m12 * crossMetres) / det;
        double y2 = (m11 * crossMetres - m12 * alongMetres) / det;

        return j.Axes[0] * (a[0] * y1 + c[0] * y2)
               + j.Axes[1] * (a[1] * y1 + c[1] * y2)
               + j.Axes[2] * (a[2] * y1 + c[2] * y2);

        static double Dot(double[] x, double[] y) => x[0] * y[0] + x[1] * y[1] + x[2] * y[2];
    }

    /// <summary>The release velocity that moves the landing by a wanted ground displacement and clock change.</summary>
    private static double3 Solve(Jacobian j, double alongMetres, double crossMetres, double clockSeconds)
    {
        double[,] m =
        {
            { j.Along.X, j.Along.Y, j.Along.Z },
            { j.Cross.X, j.Cross.Y, j.Cross.Z },
            { j.Clock.X, j.Clock.Y, j.Clock.Z },
        };

        double[] b = [alongMetres, crossMetres, clockSeconds];

        // Gaussian elimination with partial pivoting; three rows, and the matrix is well conditioned
        // because the clock row is the one direction the ground rows cannot see.
        for (int col = 0; col < 3; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < 3; row++)
            {
                if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col])) pivot = row;
            }

            if (pivot != col)
            {
                for (int k = 0; k < 3; k++) (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);
                (b[col], b[pivot]) = (b[pivot], b[col]);
            }

            for (int row = col + 1; row < 3; row++)
            {
                double f = m[row, col] / m[col, col];
                for (int k = col; k < 3; k++) m[row, k] -= f * m[col, k];
                b[row] -= f * b[col];
            }
        }

        double[] x = new double[3];
        for (int row = 2; row >= 0; row--)
        {
            double sum = b[row];
            for (int k = row + 1; k < 3; k++) sum -= m[row, k] * x[k];
            x[row] = sum / m[row, row];
        }

        return j.Axes[0] * x[0] + j.Axes[1] * x[1] + j.Axes[2] * x[2];
    }

    private static string Say(Jacobian j)
        => $"flight {j.FlightSeconds:F0} s, arrives {j.ArrivalDeg:F1} deg";

    // ---------------------------------------------------------------------------------------
    // 1. Divert sensitivity
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// How far one metre a second of bus velocity moves the landing, at the epoch the warheads are
    /// actually let go, and at the cutoff the plan's estimates were taken from.
    /// </summary>
    [Fact]
    public void WhatOneMetrePerSecondOfDivertIsWorth()
    {
        Shot[] shots =
        [
            Cutoff2000, Release2000,
            Cutoff12902, Release12902,
            Cutoff6179, Flown6179,
        ];

        foreach (Shot shot in shots)
        {
            Jacobian j = Columns(shot);
            (double major, double minor, double3 majorAxis, _) = Footprint(j);

            Out.WriteLine($"{shot.Name}: {Say(j)}, {Earth.AltitudeOf(shot.PositionCci) / 1000.0:F0} km up");

            for (int i = 0; i < 3; i++)
            {
                double along = i == 0 ? j.Along.X : i == 1 ? j.Along.Y : j.Along.Z;
                double across = i == 0 ? j.Cross.X : i == 1 ? j.Cross.Y : j.Cross.Z;
                double clock = i == 0 ? j.Clock.X : i == 1 ? j.Clock.Y : j.Clock.Z;

                Out.WriteLine($"   {j.Names[i],-12} {along,9:F0} m along {across,9:F0} m across "
                              + $"{clock,7:F3} s of clock, per m/s");
            }

            Out.WriteLine($"   footprint per 10 m/s: {major / 100.0:F1} km x {minor / 100.0:F1} km "
                          + $"(major axis {Vec.Dot(majorAxis, j.Axes[0]):+0.00;-0.00} prograde, "
                          + $"{Vec.Dot(majorAxis, j.Axes[1]):+0.00;-0.00} radial, "
                          + $"{Vec.Dot(majorAxis, j.Axes[2]):+0.00;-0.00} cross)");
            Out.WriteLine("");
        }
    }

    /// <summary>
    /// The plan's reach numbers rest on the footprint staying linear out to tens of kilometres.
    /// Flown at the release epoch, along both semi-axes.
    /// </summary>
    [Fact]
    public void HowFarTheFirstOrderFootprintHolds()
    {
        Shot[] shots = [Flown6179, Release2000, Release12902];

        foreach (Shot shot in shots)
        {
            Jacobian j = Columns(shot);
            (double major, double minor, double3 majorAxis, double3 minorAxis) = Footprint(j);

            Assert.True(Land(shot, Vec.Zero, out ImpactPredictor.Impact nominal));

            Out.WriteLine($"{shot.Name}: {Say(j)}");

            foreach ((string what, double3 axis, double slope) in
                     new[] { ("major", majorAxis, major), ("minor", minorAxis, minor) })
            {
                foreach (double dv in new[] { 1.0, 5.0, 10.0, 20.0, 40.0 })
                {
                    if (!Land(shot, axis * dv, out ImpactPredictor.Impact moved)) break;

                    double flown = Vec.Len(moved.GroundFixedPointCci - nominal.GroundFixedPointCci);
                    double linear = slope * dv;

                    Out.WriteLine($"   {what} {dv,5:F0} m/s: flown {flown / 1000.0,8:F2} km against "
                                  + $"{linear / 1000.0,8:F2} km linear "
                                  + $"({100.0 * (flown - linear) / linear,6:F1}%)");
                }
            }

            Out.WriteLine("");
        }
    }

    // ---------------------------------------------------------------------------------------
    // 2. What the bus can pay
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The bus's own divert budget, off the part XML and the save rather than off the guidance cap.
    ///
    /// <para><b>Everything here is read rather than guessed.</b> The tank holds 51.6452 kg of MMH
    /// and 82.6323 kg of NTO in <c>saves/SOLVER SCALE 8/universe.xml</c> — which is
    /// <see cref="ReactantMixVolume"/>, a full 0.336 m inner sphere at KSA's own storage densities.
    /// The nozzle's effective exhaust velocity is KSA's own vacuum design chain for MMH/NTO at
    /// 7 bar through an area ratio of 40, and the count and the cosine loss per direction are what
    /// <c>tools/model/checkring.py --translation</c> reads off the same XML.</para>
    /// </summary>
    [Fact]
    public void WhatTheBusCanActuallySpend()
    {
        const double propellant = 51.6452 + 82.6323;

        // KSA's vacuum design chain: 7 bar of MMH/NTO at a mixture ratio of 1.6, thermal efficiency
        // 0.95, expansion efficiency 0.70, area ratio 40 through a 0.13 m exit. Computed rather than
        // measured, and it lands within 1% of the 394 N the part XML claims per nozzle.
        const double thrustPerNozzle = 389.6;
        const double exhaustVelocity = 2906.0;

        // Four nozzles push along the axis and six push across it at 45 degrees, so a lateral metre
        // a second costs 1.41 times the propellant an axial one does.
        (string what, double units, int nozzles)[] directions =
        [
            ("fore/aft", 4.000, 4),
            ("lateral", 4.243, 6),
        ];

        // Measured in flight: 0.559 m/s2 on the tail, 0.572 alternating across, over 96 buses.
        double massFromFlight = 4.000 * thrustPerNozzle / 0.559;

        Out.WriteLine($"propellant {propellant:F1} kg, bus about {massFromFlight:F0} kg wet "
                      + $"(4 x {thrustPerNozzle:F0} N over the 0.559 m/s2 flown on the tail)");

        foreach ((string what, double units, int nozzles) in directions)
        {
            double effective = exhaustVelocity * units / nozzles;
            double deltaV = effective * Math.Log(massFromFlight / (massFromFlight - propellant));

            Out.WriteLine($"   {what,-9}: {units * thrustPerNozzle,7:F0} N, {nozzles} nozzles, "
                          + $"effective exhaust {effective:F0} m/s -> {deltaV:F0} m/s of divert");
        }

        // A divert with both lateral components is fired one axis at a time, so it pays the L1 norm
        // rather than the Euclidean: the worst case is 45 degrees between the two lateral axes.
        double lateralEffective = exhaustVelocity * 4.243 / 6.0;
        double diagonal = lateralEffective * Math.Log(massFromFlight / (massFromFlight - propellant))
                          / Math.Sqrt(2.0);

        Out.WriteLine($"   worst case: a divert on the two lateral axes together is {diagonal:F0} m/s, "
                      + "because BusTrim fires one direction at a time");
        Out.WriteLine($"   guidance caps the correction at {PostBoostAim.MaxTrimMetresPerSecond:F0} m/s "
                      + $"and one solve at {BusTrim.MaxMetresPerSecond:F0} m/s");
        Out.WriteLine("   flown: a single-target shot spends a median 16.1 m/s (96 buses, "
                      + "2026-09-17-threearm), worst 21.8");

        Assert.InRange(massFromFlight, 2_000.0, 4_000.0);
    }

    /// <summary>The footprint the bus's own propellant reaches, per geometry.</summary>
    [Fact]
    public void WhatThatBudgetReaches()
    {
        // What is left of the flown budget after one target's separation null and aim correction.
        double[] budgets = [20.0, 50.0, 100.0];

        Shot[] shots = [Flown6179, Cutoff6179, Release2000, Cutoff2000, Release12902, Cutoff12902];

        foreach (Shot shot in shots)
        {
            Jacobian j = Columns(shot);
            (double major, double minor, _, _) = Footprint(j);

            Out.WriteLine($"{shot.Name}: {Say(j)}");

            foreach (double budget in budgets)
            {
                Out.WriteLine($"   {budget,5:F0} m/s reaches {major * budget / 1000.0,7:F1} km x "
                              + $"{minor * budget / 1000.0,6:F1} km");
            }

            // What the trim's own resolution is worth on the ground here, which is the floor under
            // a target released at this epoch however well the correction converges.
            Out.WriteLine($"   the trim's {BusTrim.SettledMetresPerSecond:F2} m/s stop band is "
                          + $"{major * BusTrim.SettledMetresPerSecond:F0} m of miss");
            Out.WriteLine("");
        }
    }

    /// <summary>
    /// How long the bus stays high enough to release, which is the clock a target list is really
    /// bounded by: <see cref="IcbmConfig.DeployAltitudeMetres"/> is 100 km and the descent through
    /// it is the end of the sequence whatever the arrival time says.
    /// </summary>
    [Fact]
    public void HowLongThereIsToReleaseIn()
    {
        // A re-aim is a median 65 s from separation to release over 96 flown buses; a divert to a
        // fresh target adds its own burn on top, at the 0.56 m/s2 the thrusters are measured at.
        const double perTarget = 65.0;

        (Shot from, double coast)[] runs =
        [
            (Cutoff6179, 1340.0),
            (Flown6179, 359.3),
            (Cutoff2000, 425.0),
            (Cutoff12902, 1881.0),
        ];

        foreach ((Shot from, double coast) in runs)
        {
            double lo = 0.0, hi = coast;

            for (int i = 0; i < 60; i++)
            {
                double mid = 0.5 * (lo + hi);
                Shot at = Forward(from, mid);

                if (Earth.AltitudeOf(at.PositionCci) >= new IcbmConfig().DeployAltitudeMetres) lo = mid;
                else hi = mid;
            }

            Out.WriteLine($"{from.Name}: {coast:F0} s to arrival, above 100 km for {lo:F0} s of it "
                          + $"-- {(int)(lo / perTarget)} re-aims at {perTarget:F0} s each, before any "
                          + "divert burn");
        }
    }

    // ---------------------------------------------------------------------------------------
    // 3. Staggered arrivals
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// What it costs to put a second warhead on the same target a stated interval later — the divert
    /// that changes the arrival clock and nothing else.
    ///
    /// <para>A warhead released later from a coasting bus shares the bus's conic and arrives at the
    /// same instant, so the stagger has to be bought as a velocity. The interval has to clear the
    /// previous burst: at <see cref="Warhead.LethalRadius"/> for the Mk 21's charge, over the speed
    /// the round is doing when it arrives.</para>
    /// </summary>
    [Fact]
    public void WhatAStaggeredArrivalCosts()
    {
        double lethal = Warhead.LethalRadius(Arsenal.ReentryVehicleMk21.ChargeKg);

        Shot[] shots = [Release2000, Flown6179, Cutoff6179, Cutoff12902];

        foreach (Shot shot in shots)
        {
            Jacobian j = Columns(shot);
            (double major, double minor, _, _) = Footprint(j);

            Assert.True(Land(shot, Vec.Zero, out ImpactPredictor.Impact nominal));

            double arrivalSpeed = Vec.Len(nominal.VelocityCci - Earth.GroundVelocityCci(nominal.PointCci));
            double clears = lethal / arrivalSpeed;

            Out.WriteLine($"{shot.Name}: {Say(j)}, arriving at {arrivalSpeed:F0} m/s -- "
                          + $"{lethal / 1000.0:F1} km of lethal radius is {clears:F2} s of flight");

            foreach (double seconds in new[] { 0.5, 1.0, 2.0 })
            {
                double3 kick = Solve(j, 0.0, 0.0, seconds);

                // Flown back through the predictor, because the stagger is solved off first-order
                // columns and the thing it has to deliver is a real arrival.
                Assert.True(Land(shot, kick, out ImpactPredictor.Impact late));

                double moved = Vec.Len(late.GroundFixedPointCci - nominal.GroundFixedPointCci);

                Out.WriteLine($"   {seconds:F1} s later costs {Vec.Len(kick):F3} m/s "
                              + $"-- flown, it arrives {late.Seconds - nominal.Seconds:F2} s later "
                              + $"and {moved:F0} m from the same point");
            }

            // The other way to keep two warheads out of each other's burst: land the second one
            // clear of the first rather than after it. It buys the same separation off the cheap
            // axis of the footprint instead of the expensive one.
            Out.WriteLine($"   or {lethal / 1000.0:F1} km clear on the ground: "
                          + $"{lethal / major:F2} m/s along the track, {lethal / minor:F2} m/s across it");
            Out.WriteLine("");
        }
    }

    // ---------------------------------------------------------------------------------------
    // 4. What waiting costs
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// What a second of holding costs the warheads still aboard, and how the divert leverage decays
    /// while the bus works through its targets.
    ///
    /// <para>Two different things, and the plan's open question is the second. The holding cost is
    /// what <see cref="PostBoostAim"/> prices a further correction cycle against; the decay is why a
    /// late target is dearer to reach than an early one.</para>
    /// </summary>
    [Fact]
    public void WhatHoldingTheLaterTargetsCosts()
    {
        // A re-aim is a median 65 s from separation to release over 96 flown buses.
        const double perTarget = 65.0;

        (Shot first, double coast)[] runs =
        [
            (Cutoff6179, 1340.0),
            (Flown6179, 359.3),
            (Cutoff12902, 1881.0),
            (Cutoff2000, 425.0),
        ];

        foreach ((Shot first, double coast) in runs)
        {
            Out.WriteLine($"{first.Name}, {coast:F0} s of coast to arrival:");

            for (int target = 0; target < 6; target++)
            {
                double elapsed = target * perTarget;
                if (elapsed + 30.0 >= coast) break;

                Shot at = target == 0 ? first : Forward(first, elapsed);
                if (Earth.AltitudeOf(at.PositionCci) < 100_000.0) break;

                Jacobian j = Columns(at);
                (double major, double minor, _, _) = Footprint(j);

                double3 kick = Vec.Unit(at.VelocityCci) * Arsenal.ReentryVehicleMk21.LaunchSpeed;

                bool held = HoldingCost.TryMeasure(Earth, at.PositionCci, at.VelocityCci, kick, Step,
                                                  out double holding, Air, stopOnTheSurface: true);

                Out.WriteLine($"   target {target + 1} at t+{elapsed,4:F0} s: "
                              + $"{Earth.AltitudeOf(at.PositionCci) / 1000.0,6:F0} km up, "
                              + $"{j.FlightSeconds,5:F0} s of flight left, "
                              + $"{major,6:F0} x {minor,5:F0} m per m/s"
                              + (held ? $", holding {holding:F2} m/s" : ", holding unmeasurable"));
            }

            Out.WriteLine("");
        }
    }

    /// <summary>
    /// The same decay read the other way round: what one target's divert costs if it is bought late
    /// rather than early. It is the number that decides whether the release loop should start at
    /// cutoff or at the release gate.
    /// </summary>
    [Fact]
    public void WhatDelayingADivertCosts()
    {
        Shot cutoff = Cutoff6179;

        Out.WriteLine("6,179 km: what 50 km along the track and 20 km across it cost, "
                      + "bought at each moment of the coast");

        foreach (double elapsed in new[] { 0.0, 200.0, 400.0, 600.0, 800.0, 900.0, 980.0 })
        {
            Shot at = elapsed == 0.0 ? cutoff : Forward(cutoff, elapsed);
            if (Earth.AltitudeOf(at.PositionCci) < new IcbmConfig().DeployAltitudeMetres) break;

            Jacobian j = Columns(at);

            Out.WriteLine($"   t+{elapsed,4:F0} s ({j.FlightSeconds,5:F0} s of flight left, "
                          + $"{Earth.AltitudeOf(at.PositionCci) / 1000.0,6:F0} km up): "
                          + $"{Vec.Len(MinimumNorm(j, 50_000.0, 0.0)),6:F2} m/s along, "
                          + $"{Vec.Len(MinimumNorm(j, 0.0, 20_000.0)),6:F2} m/s across");
        }
    }

    /// <summary>
    /// What the flight's <em>pinned</em> arrival costs a divert, against the free clock every other
    /// number in this file is priced with.
    /// </summary>
    /// <remarks>
    /// <para><see cref="MinimumNorm"/> leaves the arrival instant wherever it falls — two ground
    /// conditions on three unknowns — and the whole footprint is priced that way. The flight does not:
    /// <c>IcbmProgram</c> latches the arrival at closed-loop handover and <c>ResolveCoastArc</c> solves
    /// every later arc to that same instant, so a divert between destinations is an exactly-determined
    /// three-by-three and strictly dearer.</para>
    ///
    /// <para><b>This is the number that decides whether a release loop may keep one arrival for the
    /// whole itinerary</b>, or has to re-commit it per destination — which would need a re-latch after
    /// cutoff that does not exist today, the latch living inside <c>Resolve</c>, which the coast never
    /// reaches.</para>
    /// </remarks>
    [Fact]
    public void ThePinnedArrivalIsWhatADivertActuallyCosts()
    {
        Out.WriteLine("a 50 km hop along the track and a 20 km hop across it, m/s\n");
        Out.WriteLine($"{"",-38}{"free clock",22}{"pinned arrival",24}");

        foreach (Shot shot in new[] { Release2000, Flown6179, Release12902,
                                      Cutoff2000, Cutoff6179, Cutoff12902 })
        {
            Jacobian j = Columns(shot);

            foreach ((double along, double cross, string what) in
                     new[] { (50_000.0, 0.0, "50 km along"), (0.0, 20_000.0, "20 km across") })
            {
                double free = Vec.Len(MinimumNorm(j, along, cross));
                double pinned = Vec.Len(Solve(j, along, cross, 0.0));

                Out.WriteLine($"{shot.Name,-26}{what,-12}{free,10:F2} m/s{pinned,18:F2} m/s"
                              + $"{pinned / free,10:F2}x");

                // The pinned solve satisfies one more condition out of the same three unknowns, so it
                // can never be the cheaper of the two.
                Assert.True(pinned >= free - 1e-9,
                            $"{shot.Name} {what}: pinned {pinned:F3} under free {free:F3}");
            }
        }
    }
}
