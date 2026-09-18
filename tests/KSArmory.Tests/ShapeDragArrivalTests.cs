using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What shipping the Mk 21's real drag does to every published arrival-angle number.
///
/// <para><c>docs/ARRIVAL-ANGLE.md</c>, <c>docs/ICBM-GUIDANCE.md</c> and <c>docs/METRE-LEVEL.md</c>
/// were all computed on <c>Arsenal.ReentryVehicleMk21</c>'s hand-typed <c>DragK = 1.5e-5</c>.
/// <see cref="IcbmConfig.WarheadDragFromItsShape"/> flies
/// <see cref="Arsenal.Mk21WithDragFromShape"/> instead, whose drag comes from mass, calibre and
/// coefficient. This measures the same things on both rounds so the docs can be corrected with
/// numbers rather than caveated.</para>
///
/// <para><b>Measurement only.</b> The method is <c>ArrivalAngleTests</c>'s, reproduced here rather
/// than reinvented: a circular platform braked purely retrograde, flown through
/// <see cref="ImpactPredictor"/> at the same step and horizon, with the floor found by sweeping the
/// brake at 10 m/s. <see cref="ReproducesThePublishedFloorTable"/> pins that it still returns the
/// published constant-drag numbers before anything new is read off it.</para>
///
/// <para><b>The planet sits at the origin and does not move</b>, per <see cref="DeorbitShot"/>, so
/// nothing here can see an epoch fault.</para>
/// </summary>
public class ShapeDragArrivalTests(ITestOutputHelper Out)
{
    private const double Mu = DeorbitShot.Mu;
    private const double R = DeorbitShot.R;

    private static BallisticBody Earth => DeorbitShot.Earth;

    /// <summary>The round every published number was computed on.</summary>
    private static MunitionProfile Constant => Arsenal.ReentryVehicleMk21;

    /// <summary>The round the switch flies: drag from 270 kg, 550 mm and a Cd of 0.1.</summary>
    private static MunitionProfile Physical => Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21);

    // ---------------------------------------------------------- ArrivalAngleTests' method, verbatim

    private static MunitionProfile Sectional(float dragK)
        => new() { Name = "PROBE", DisplayName = "drag probe", DragK = dragK };

    private static ImpactPredictor.Impact Fly(double3 from, double3 v, MunitionProfile? through)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, from, v, 1.0, 40_000.0,
                                               out ImpactPredictor.Impact hit, null, null,
                                               through is null
                                                   ? null
                                                   : new ImpactPredictor.Drag(DeorbitShot.DensityAt, through)));
        return hit;
    }

    private static double Descent(double3 pointCci, double3 velocityCci)
        => Vec.AngleBetween(pointCci, velocityCci) * 180.0 / Math.PI - 90.0;

    private static double3 Braked(double altitude, double brake)
        => new(0, Math.Sqrt(Mu / (R + altitude)) - brake, 0);

    private static double GammaInAir(double altitude, double brake, MunitionProfile round)
    {
        double3 from = new(R + altitude, 0, 0);
        double3 v = Braked(altitude, brake);

        if (Kepler.PeriapsisRadius(Mu, from, v) > R) return double.NaN;

        ImpactPredictor.Impact hit = Fly(from, v, round);
        return Descent(hit.PointCci, hit.VelocityCci);
    }

    /// <summary>
    /// The shallowest arrival the air permits this round, and everything at that brake.
    ///
    /// <para><paramref name="firstBrake"/> is on the interface because
    /// <c>ArrivalAngleTests</c> sweeps from <b>120</b> m/s for the three-platform table and from
    /// <b>130</b> for the sectional-density table, and on a nearly drag-free round the two grids
    /// answer differently — 1.13 deg against the published 1.40 at <c>1.5e-7</c>. Reproducing a
    /// published number means reproducing its grid.</para>
    /// </summary>
    private static (double Degrees, double Brake, double RangeMetres, double KeptSpeed)
        DragFloor(double altitude, MunitionProfile round, double firstBrake = 120.0)
    {
        double3 from = new(R + altitude, 0, 0);
        double best = double.PositiveInfinity, at = 0.0, range = 0.0, kept = 0.0;

        for (double brake = firstBrake; brake < Math.Sqrt(Mu / (R + altitude)); brake += 10.0)
        {
            double3 v = Braked(altitude, brake);
            if (Kepler.PeriapsisRadius(Mu, from, v) > R) continue;

            ImpactPredictor.Impact hit = Fly(from, v, round);
            double gamma = Descent(hit.PointCci, hit.VelocityCci);
            if (double.IsNaN(gamma) || gamma >= best) continue;

            best = gamma;
            at = brake;
            range = R * Vec.AngleBetween(from, hit.PointCci);
            kept = Vec.Len(hit.VelocityCci);
        }

        return (best, at, range, kept);
    }

    // ---------------------------------------------------------------------------- 1. validation

    /// <summary>
    /// The method reproduces what <c>docs/ARRIVAL-ANGLE.md</c> published, before anything new is
    /// read off it.
    ///
    /// <para>Two tables: the three circular platforms — 7.06, 7.00, 6.96 — and the sectional-density
    /// sweep whose anchor row is <c>1.5e-5 | 1x (Mk 21) | 7.00 deg | 8,565 km | 2,858 m/s |
    /// 2,414 m/s</c>.</para>
    /// </summary>
    [Fact]
    public void ReproducesThePublishedFloorTable()
    {
        Out.WriteLine("published: 300 km 7.06 deg, 400 km 7.00 deg, 500 km 6.96 deg");

        foreach ((double altitude, double published) in new[]
                 { (300_000.0, 7.06), (400_000.0, 7.00), (500_000.0, 6.96) })
        {
            (double degrees, double brake, double _, double _) = DragFloor(altitude, Constant);
            Out.WriteLine($"  circular {altitude / 1000,3:F0} km -> {degrees,6:F2} deg "
                          + $"(published {published:F2}), braking {brake:F0} m/s");

            Assert.Equal(published, degrees, 2);
        }

        Out.WriteLine("");
        Out.WriteLine("published floor table, 400 km platform:");
        Out.WriteLine("  DragK     vs Mk 21 | floor  |    at    | keeping | straight drop");

        double3 from = new(R + 400_000.0, 0, 0);

        foreach ((float dragK, double floorDeg, double rangeKm, double keptMps, double dropMps) in new[]
                 {
                     (1.5e-4f, 17.21, 2_323.0, 784.0, 910.0),
                     (1.5e-5f, 7.00, 8_565.0, 2_858.0, 2_414.0),
                     (5e-6f, 4.43, 12_857.0, 4_417.0, 2_613.0),
                     (1.5e-6f, 2.76, 15_607.0, 5_994.0, 2_687.0),
                     (1.5e-7f, 1.40, 15_897.0, 7_663.0, 2_716.0),
                 })
        {
            (double best, double _, double range, double kept) =
                DragFloor(400_000.0, Sectional(dragK), firstBrake: 130.0);
            double dropped = Vec.Len(Fly(from, Vec.Zero, Sectional(dragK)).VelocityCci);

            Out.WriteLine($"  {dragK:E1} {1.5e-5 / dragK,7:F1}x | {best,5:F2} deg "
                          + $"| {range / 1000,6:F0} km | {kept,5:F0} m/s | {dropped,5:F0} m/s"
                          + $"   (published {floorDeg:F2} deg, {rangeKm:F0} km, {keptMps:F0}, {dropMps:F0})");

            Assert.Equal(floorDeg, best, 2);
            Assert.Equal(rangeKm, range / 1000.0, 0);
            Assert.Equal(keptMps, kept, 0);
            Assert.Equal(dropMps, dropped, 0);
        }
    }

    // ------------------------------------------------------------------- 2. the physical round

    /// <summary>
    /// The same table row for the round the switch actually flies, beside the constant's.
    ///
    /// <para><see cref="Arsenal.Mk21WithDragFromShape"/> is a copy with mass, calibre and
    /// coefficient set, so its drag comes through <see cref="MunitionProfile.AppliedDragK"/>
    /// rather than off the field. The ratio is reported rather than assumed.</para>
    /// </summary>
    [Fact]
    public void TheFloorTableRowForThePhysicalRound()
    {
        Out.WriteLine($"constant DragK   {Constant.AppliedDragK:E4}  (DragFromShape "
                      + $"{Constant.DragFromShape})");
        Out.WriteLine($"physical DragK   {Physical.AppliedDragK:E4}  (DragFromShape "
                      + $"{Physical.DragFromShape}; {Physical.MassKg:F0} kg, "
                      + $"{Physical.CalibreMm:F0} mm, Cd {Physical.DragCoefficient:F2})");
        Out.WriteLine($"the physical round is {Physical.AppliedDragK / Constant.AppliedDragK:F3}x "
                      + $"the drag");
        Out.WriteLine("");

        double3 from = new(R + 400_000.0, 0, 0);

        Out.WriteLine("floor, from three circular platforms:");
        Out.WriteLine("  platform | constant | physical");

        foreach (double altitude in new[] { 300_000.0, 400_000.0, 500_000.0 })
        {
            (double c, double _, double _, double _) = DragFloor(altitude, Constant);
            (double p, double _, double _, double _) = DragFloor(altitude, Physical);

            Out.WriteLine($"  {altitude / 1000,5:F0} km | {c,6:F2} deg | {p,6:F2} deg");
        }

        Out.WriteLine("");
        Out.WriteLine("the floor row, 400 km platform:");
        Out.WriteLine("  round    | DragK      | vs Mk 21 | floor  | brake  |    at    | keeping | drop");

        foreach ((string name, MunitionProfile round) in new[]
                 { ("constant", Constant), ("physical", Physical) })
        {
            (double best, double brake, double range, double kept) = DragFloor(400_000.0, round);
            double dropped = Vec.Len(Fly(from, Vec.Zero, round).VelocityCci);

            Out.WriteLine($"  {name,-8} | {round.AppliedDragK:E3} | "
                          + $"{Constant.AppliedDragK / round.AppliedDragK,7:F2}x | {best,5:F2} deg | "
                          + $"{brake,4:F0} m/s | {range / 1000,6:F0} km | {kept,5:F0} m/s | "
                          + $"{dropped,5:F0} m/s");
        }
    }

    // ---------------------------------------------------- 3. what a floor setting actually buys

    /// <summary>
    /// The behavioural risk: <see cref="IcbmConfig.MinArrivalAngleDeg"/> constrains the
    /// <em>vacuum</em> arc, so the real arrival it buys is the round's business.
    ///
    /// <para><c>docs/ARRIVAL-ANGLE.md</c>: "The floor is applied to the vacuum arc, because the arc
    /// is what the search has." <see cref="BallisticArc.TryCheapest"/> takes no munition at all, so
    /// which shots <see cref="IcbmReach.TooShallow"/> refuses cannot move with the round's drag —
    /// what moves is the arrival a satisfied floor delivers.</para>
    /// </summary>
    [Theory]
    [InlineData(20.0)]
    [InlineData(45.0)]
    public void AVacuumFloorBuysADifferentRealArrivalOnEachRound(double aheadDeg)
    {
        const double Altitude = 400_000.0;

        double3 platform = new(R + Altitude, 0, 0);
        double3 circular = new(0, Math.Sqrt(Mu / (R + Altitude)), 0);

        double a = aheadDeg * Math.PI / 180.0;
        double3 aim = new(R * Math.Cos(a), R * Math.Sin(a), 0);

        Out.WriteLine($"400 km circular platform, target {R * a / 1000:F0} km ahead.");
        Out.WriteLine("MinArrivalAngleDeg is applied to the vacuum arc; what lands is the round's.");
        Out.WriteLine("");
        Out.WriteLine("  set | arc arrives | constant lands | physical lands | c-p gap | "
                      + "constant speed | physical speed");

        foreach (double floor in new[] { 0.0, 7.0, 15.0, 20.0, 32.0, 45.0 })
        {
            if (!BallisticArc.TryCheapest(Earth, platform, circular, aim,
                                          out BallisticArc.Solution s, 1.0, false, double.NaN, floor))
            {
                Out.WriteLine($"  {floor,3:F0} | no arc satisfies this floor");
                continue;
            }

            ImpactPredictor.Impact c = Fly(platform, s.RequiredVelocityCci, Constant);
            ImpactPredictor.Impact p = Fly(platform, s.RequiredVelocityCci, Physical);

            double cd = Descent(c.PointCci, c.VelocityCci);
            double pd = Descent(p.PointCci, p.VelocityCci);

            Out.WriteLine($"  {floor,3:F0} | {s.ArrivalAngleDeg,8:F2} deg | {cd,11:F2} deg | "
                          + $"{pd,11:F2} deg | {pd - cd,6:F2} deg | {Vec.Len(c.VelocityCci),10:F0} m/s"
                          + $" | {Vec.Len(p.VelocityCci),10:F0} m/s");
        }

        Out.WriteLine("");
        Out.WriteLine("and where each lands, which is the other half of the same arc-vs-air gap:");
        Out.WriteLine("  set | arc range | constant | physical");

        foreach (double floor in new[] { 0.0, 7.0, 15.0, 20.0, 32.0, 45.0 })
        {
            if (!BallisticArc.TryCheapest(Earth, platform, circular, aim,
                                          out BallisticArc.Solution s, 1.0, false, double.NaN, floor))
            {
                continue;
            }

            ImpactPredictor.Impact vac = Fly(platform, s.RequiredVelocityCci, null);
            ImpactPredictor.Impact c = Fly(platform, s.RequiredVelocityCci, Constant);
            ImpactPredictor.Impact p = Fly(platform, s.RequiredVelocityCci, Physical);

            Out.WriteLine($"  {floor,3:F0} | {R * Vec.AngleBetween(platform, vac.PointCci) / 1000,6:F0} km"
                          + $" | {R * Vec.AngleBetween(platform, c.PointCci) / 1000,6:F0} km"
                          + $" | {R * Vec.AngleBetween(platform, p.PointCci) / 1000,6:F0} km");
        }
    }

    /// <summary>
    /// The same gap approached from the side <c>ArrivalFloorTests</c> approaches it: does the arc
    /// still agree with what lands to within half a degree, on the physical round?
    ///
    /// <para><c>ArrivalFloorTests.TheArcAndWhatLandsAgreeToWithinHalfADegree</c> asserts that on the
    /// constant, and it is the justification for measuring the floor on the arc at all. It flies
    /// <c>DeorbitShot.Warhead</c> explicitly, so it keeps passing whatever ships — this is the
    /// question it was asking, asked of the round that will fly.</para>
    /// </summary>
    [Fact]
    public void WhetherTheArcStillAgreesWithWhatLandsOnThePhysicalRound()
    {
        const double Altitude = 400_000.0;

        double3 platform = new(R + Altitude, 0, 0);
        double3 circular = new(0, Math.Sqrt(Mu / (R + Altitude)), 0);

        double a = 20.0 * Math.PI / 180.0;
        double3 aim = new(R * Math.Cos(a), R * Math.Sin(a), 0);

        Out.WriteLine("  floor | arc arrives | constant lands | physical lands | constant err | physical err");

        double worstConstant = 0.0, worstPhysical = 0.0;

        foreach (double floor in new[] { 10.0, 12.0, 15.0, 20.0, 30.0 })
        {
            Assert.True(BallisticArc.TryCheapest(Earth, platform, circular, aim,
                                                 out BallisticArc.Solution s, 1.0, false,
                                                 double.NaN, floor));

            double c = Descent(Fly(platform, s.RequiredVelocityCci, Constant).PointCci,
                               Fly(platform, s.RequiredVelocityCci, Constant).VelocityCci);
            double p = Descent(Fly(platform, s.RequiredVelocityCci, Physical).PointCci,
                               Fly(platform, s.RequiredVelocityCci, Physical).VelocityCci);

            worstConstant = Math.Max(worstConstant, Math.Abs(c - s.ArrivalAngleDeg));
            worstPhysical = Math.Max(worstPhysical, Math.Abs(p - s.ArrivalAngleDeg));

            Out.WriteLine($"  {floor,5:F0} | {s.ArrivalAngleDeg,8:F2} deg | {c,11:F2} deg | "
                          + $"{p,11:F2} deg | {c - s.ArrivalAngleDeg,9:F2} deg | "
                          + $"{p - s.ArrivalAngleDeg,9:F2} deg");
        }

        Out.WriteLine("");
        Out.WriteLine($"worst disagreement: constant {worstConstant:F2} deg, physical {worstPhysical:F2} deg"
                      + "  (the published bound is 0.5)");
    }

    // ------------------------------------------------------- 4. ICBM-GUIDANCE's entry table

    /// <summary>
    /// <c>docs/ICBM-GUIDANCE.md</c>'s entry table — ground per height, and the speed a round keeps
    /// through entry — for both rounds.
    ///
    /// <para><b>The published column is Allen–Eggers</b>, recovered rather than assumed:
    /// <c>v/v_entry = exp(-k·H / sin γ)</c> at <c>k = 1.5e-5</c> and the 8 km scale height gives
    /// 75, 56 and 25 % at 25, 12 and 5 degrees — the published rows to the digit.
    /// <see cref="TheEntryTableIsAllenEggers"/> pins that. This flies the brake family through the
    /// predictor beside it, with two stated speed references, because a closed form assuming a flat
    /// planet and a constant flight-path angle is a weaker assumption on the round that bends
    /// more.</para>
    /// </summary>
    [Fact]
    public void TheEntryTableForBothRounds()
    {
        const double Altitude = 400_000.0;
        const double EntryRadius = R + 100_000.0;

        double3 from = new(R + Altitude, 0, 0);

        (double cFloor, double cFloorBrake, double _, double _) = DragFloor(Altitude, Constant);
        (double pFloor, double pFloorBrake, double _, double _) = DragFloor(Altitude, Physical);

        Out.WriteLine($"floors: constant {cFloor:F2} deg, physical {pFloor:F2} deg");
        Out.WriteLine("");
        Out.WriteLine("  arrival asked | round    | actual | ground/height | entry m/s | vacuum m/s "
                      + "| impact m/s | vs entry | vs vacuum");

        foreach (double asked in new[] { 25.0, 12.0, 5.0 })
        {
            foreach ((string name, MunitionProfile round, double floor, double floorBrake) in new[]
                     {
                         ("constant", Constant, cFloor, cFloorBrake),
                         ("physical", Physical, pFloor, pFloorBrake),
                     })
            {
                if (asked < floor)
                {
                    Out.WriteLine($"  {asked,10:F0} deg | {name,-8} | unreachable: nothing arrives "
                                  + $"shallower than {floor:F2} deg");
                    continue;
                }

                double brake = BrakeForArrival(Altitude, asked, floorBrake, round);
                double3 v = Braked(Altitude, brake);

                ImpactPredictor.Impact hit = Fly(from, v, round);
                double gamma = Descent(hit.PointCci, hit.VelocityCci);

                // Vis-viva on the departure conic, at 100 km: the speed the round arrives at the
                // air with, before the air has had any of it.
                double energy = 0.5 * Vec.Dot(v, v) - Mu / Vec.Len(from);
                double entry = Math.Sqrt(2.0 * (energy + Mu / EntryRadius));

                double impact = Vec.Len(hit.VelocityCci);
                double vacuum = Vec.Len(Fly(from, v, null).VelocityCci);

                Out.WriteLine($"  {asked,10:F0} deg | {name,-8} | {gamma,5:F2} | "
                              + $"{1.0 / Math.Tan(gamma * Math.PI / 180.0),10:F2} km/km | "
                              + $"{entry,9:F0} | {vacuum,10:F0} | {impact,10:F0} | "
                              + $"{100.0 * impact / entry,7:F0} % | {100.0 * impact / vacuum,8:F0} %");
            }
        }

        Out.WriteLine("");
        Out.WriteLine("and at each round's own floor, which is the shallowest entry it can make:");

        foreach ((string name, MunitionProfile round, double floor, double floorBrake) in new[]
                 {
                     ("constant", Constant, cFloor, cFloorBrake),
                     ("physical", Physical, pFloor, pFloorBrake),
                 })
        {
            double3 v = Braked(Altitude, floorBrake);
            ImpactPredictor.Impact hit = Fly(from, v, round);
            double energy = 0.5 * Vec.Dot(v, v) - Mu / Vec.Len(from);
            double entry = Math.Sqrt(2.0 * (energy + Mu / EntryRadius));
            double impact = Vec.Len(hit.VelocityCci);
            double vacuum = Vec.Len(Fly(from, v, null).VelocityCci);

            Out.WriteLine($"  {name,-8} floor {floor,5:F2} deg: entry {entry:F0} m/s, "
                          + $"vacuum {vacuum:F0} m/s, impact {impact:F0} m/s, "
                          + $"kept {100.0 * impact / entry:F0} % of entry, "
                          + $"{100.0 * impact / vacuum:F0} % of vacuum");
        }
    }

    /// <summary>
    /// The published entry table, reproduced in closed form, and the same three rows for the round
    /// the switch flies.
    ///
    /// <para>Allen–Eggers: a ballistic entry at a constant flight-path angle through an exponential
    /// atmosphere keeps <c>exp(-k·H / sin γ)</c> of the speed it entered with, where <c>k</c> is the
    /// round's drag constant in reference air and <c>H</c> is the scale height. Every term is a
    /// number the mod already carries, which is what makes the doc's row checkable rather than
    /// merely plausible.</para>
    /// </summary>
    [Fact]
    public void TheEntryTableIsAllenEggers()
    {
        static double Kept(double k, double gammaDeg)
            => Math.Exp(-k * DeorbitShot.ScaleHeight / Math.Sin(gammaDeg * Math.PI / 180.0));

        Out.WriteLine($"scale height {DeorbitShot.ScaleHeight:F0} m; "
                      + $"constant k {Constant.AppliedDragK:E4}, physical k {Physical.AppliedDragK:E4}");
        Out.WriteLine("");
        Out.WriteLine("  arrival | ground/height | constant keeps | published | physical keeps");

        foreach ((double gamma, double published) in new[] { (25.0, 75.0), (12.0, 56.0), (5.0, 25.0) })
        {
            double c = 100.0 * Kept(Constant.AppliedDragK, gamma);
            double p = 100.0 * Kept(Physical.AppliedDragK, gamma);

            Out.WriteLine($"  {gamma,5:F0} deg | {1.0 / Math.Tan(gamma * Math.PI / 180.0),10:F2} km/km"
                          + $" | {c,13:F0} % | {published,8:F0} % | {p,14:F1} %");

            Assert.Equal(published, c, 0);
        }

        Out.WriteLine("");
        Out.WriteLine("and at each round's own floor:");

        foreach ((string name, MunitionProfile round, double floor) in new[]
                 { ("constant", Constant, 7.00), ("physical", Physical, 12.09) })
        {
            Out.WriteLine($"  {name,-8} {floor,5:F2} deg: keeps "
                          + $"{100.0 * Kept(round.AppliedDragK, floor):F1} %");
        }
    }

    // The brake that arrives at a stated angle, ArrivalAngleTests' bisection with the round named.
    private static double BrakeForArrival(double altitude, double degrees, double floorBrake,
                                          MunitionProfile round)
    {
        double lo = floorBrake, hi = Math.Sqrt(Mu / (R + altitude));

        for (int i = 0; i < 48; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (GammaInAir(altitude, mid, round) < degrees) lo = mid; else hi = mid;
        }

        return 0.5 * (lo + hi);
    }

    // -------------------------------------------------- 5. the 54.6 km vacuum-vs-drag shortfall

    /// <summary>
    /// <c>docs/ICBM-GUIDANCE.md</c>'s "54.6 km short": the same cutoff state through the vacuum
    /// model and through each round's air.
    ///
    /// <para>The geometry is <c>PredictedDragTests.Deorbit</c>'s — a 200 km pick-up aimed 2,764 km
    /// downrange — reproduced here so both rounds fly it. That file's own numbers stay on the
    /// constant.</para>
    /// </summary>
    [Fact]
    public void TheVacuumVersusDragShortfallForBothRounds()
    {
        double3 from = new(R + 200_000.0, 0, 0);
        const double Range = 2_764_000.0;
        double3 target = new(R * Math.Cos(Range / R), R * Math.Sin(Range / R), 0);
        double3 circular = new(0, Math.Sqrt(Mu / (R + 200_000.0)), 0);

        Assert.True(BallisticArc.TryCheapest(Earth, from, circular, target,
                                             out BallisticArc.Solution s));

        ImpactPredictor.Impact vac = Fly(from, s.RequiredVelocityCci, null);
        double vacKm = R * Vec.AngleBetween(from, vac.PointCci) / 1000.0;

        Out.WriteLine($"vacuum arc lands {vacKm:F0} km downrange, arriving at "
                      + $"{Descent(vac.PointCci, vac.VelocityCci):F2} deg "
                      + $"at {Vec.Len(vac.VelocityCci):F0} m/s");
        Out.WriteLine("  published: vacuum 2,764 km, round 2,709 km, 54.6 km short");
        Out.WriteLine("");

        foreach ((string name, MunitionProfile round) in new[]
                 { ("constant", Constant), ("physical", Physical) })
        {
            ImpactPredictor.Impact hit = Fly(from, s.RequiredVelocityCci, round);
            double km = R * Vec.AngleBetween(from, hit.PointCci) / 1000.0;

            Out.WriteLine($"  {name,-8}: lands {km,7:F1} km, {vacKm - km,6:F1} km short of the "
                          + $"vacuum arc, arriving {Descent(hit.PointCci, hit.VelocityCci),5:F2} deg "
                          + $"at {Vec.Len(hit.VelocityCci),5:F0} m/s");
        }
    }

    // ------------------------------------------------ 6. METRE-LEVEL's drag-model floor term

    /// <summary>
    /// <c>docs/METRE-LEVEL.md</c>'s floor: the ground a ten per cent error in the drag model moves
    /// the impact, which is the term no correction loop removes because its only observer shares
    /// the model.
    ///
    /// <para><c>ArrivalAngleTests</c> prices it as the constant against <c>1.65e-5</c>. Ten per cent
    /// of the physical round is a different absolute step, so the term is re-priced on each round's
    /// own drag rather than on a shared constant.</para>
    /// </summary>
    [Fact]
    public void TheDragModelTermForBothRounds()
    {
        const double Altitude = 400_000.0;
        double3 from = new(R + Altitude, 0, 0);

        (double cFloor, double cFloorBrake, double _, double _) = DragFloor(Altitude, Constant);
        (double pFloor, double pFloorBrake, double _, double _) = DragFloor(Altitude, Physical);

        Out.WriteLine("what a 10% error in the drag model costs, in ground:");
        Out.WriteLine("  arrival | round    | reach     | brake  | 10% drag moves it");

        foreach (double asked in new[] { 7.5, 10.0, 15.0, 20.0, 30.0, 45.0 })
        {
            foreach ((string name, MunitionProfile round, double floor, double floorBrake) in new[]
                     {
                         ("constant", Constant, cFloor, cFloorBrake),
                         ("physical", Physical, pFloor, pFloorBrake),
                     })
            {
                if (asked < floor)
                {
                    Out.WriteLine($"  {asked,5:F1} deg | {name,-8} | unreachable (floor {floor:F2} deg)");
                    continue;
                }

                double brake = BrakeForArrival(Altitude, asked, floorBrake, round);
                double3 v = Braked(Altitude, brake);

                MunitionProfile heavier = round.Copy();
                if (round.DragFromShape) heavier.DragCoefficient = round.DragCoefficient * 1.1f;
                else heavier.DragK = round.DragK * 1.1f;

                ImpactPredictor.Impact air = Fly(from, v, round);
                ImpactPredictor.Impact off = Fly(from, v, heavier);

                double moved = DeorbitShot.GroundMetres(air.GroundFixedPointCci, off.GroundFixedPointCci);

                Out.WriteLine($"  {asked,5:F1} deg | {name,-8} | "
                              + $"{R * Vec.AngleBetween(from, air.PointCci) / 1000,6:F0} km | "
                              + $"{brake,5:F0} m/s | {moved,9:F0} m");
            }
        }
    }
}
