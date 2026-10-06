using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The game's stack flown with an attitude that has inertia, near cutoff at its throttle floor.
/// <see cref="IcbmConfig.ShortShotSlowsLineSeconds"/>; <c>docs/SHORT-RANGE.md</c>, "Why 150 km failed".
///
/// <para>The rig reproduces the flown fault before it judges the fix: with the line followed to the end,
/// most of these flights spin up at the floor, as every flown core that ran dry did.</para>
/// </summary>
public class FloorHoldTests(ITestOutputHelper Out)
{
    internal const double R = 6_371_000.0;

    internal static BallisticBody Earth => new(3.986004418e14, R, new double3(0, 0, 1), 7.2921159e-5);

    internal static readonly double[] Ranges = [150.0, 200.0, 300.0, 418.0, 500.0];

    internal static IcbmConfig Config(double slowLineSeconds, bool finishInTheAir = false, bool solveWithDrag = false)
        => new()
        {
            Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, ArrivalPreference = 0.5,
            FlyAnyRange = true, ShortShotSlowsLineSeconds = slowLineSeconds, ShortShotFinishesInTheAir = finishInTheAir,
            ShortShotSolvesWithDrag = solveWithDrag, SolidsLeaveMetresPerSecond = 0.0, DropSolidsUnderWeight = false,
        };

    internal readonly record struct Outcome(double FloorRateDegPerSec, double ResidualMetresPerSecond, double MissKm);

    internal static Outcome Fly(double km, double slowLineSeconds, double step, double jitter, bool finishInTheAir = false,
                                bool solveWithDrag = false, double throw_ = 0.0, bool throwHandedOver = true)
    {
        IcbmFlightRig rig = GameStackShortRangeTests.GameStack(true);
        rig.AttitudeHasInertia = true;
        rig.StepJitter = jitter;
        rig.Warhead = Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21);
        rig.ReleaseLaunchSpeed = throw_;
        rig.HandsOverTheThrow = throwHandedOver;

        IcbmProgram program = new(Config(slowLineSeconds, finishInTheAir, solveWithDrag));
        double3 aim = GameStackShortRangeTests.South(km * 1000.0);
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, step, 6_000.0);

        double miss = double.NaN;
        // Released at cutoff, each warhead leaves with its tube's throw along the line the stack points.
        double3 thrown = flight.CutoffVelocityCci + Vec.Unit(flight.CoastDirectionCci) * throw_;
        if (ImpactPredictor.TryPredict(Earth, flight.CutoffPositionCci, thrown, 1.0,
                                       ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit, null, null,
                                       new ImpactPredictor.Drag(p => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0),
                                                                Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21))))
        {
            miss = R * Vec.AngleBetween(hit.GroundFixedPointCci, Earth.CarryCci(aim, flight.CutoffSeconds)) / 1000.0;
        }

        return new Outcome(rig.PeakFloorRateDegPerSec, program.ResidualAtCutoff, miss);
    }

    // The arcs the slowed line acts on: an arc that climbs above the release altitude is flown with the
    // line followed, as without the setting.
    internal static readonly double[] LowArcs = [150.0, 200.0, 300.0, 418.0];

    [Fact]
    public void AStackAtItsFloorDoesNotChaseWhatIsLeftIntoASpin()
    {
        int spunOff = 0, spunOn = 0;
        double worstOn = 0.0;

        foreach (double step in new[] { 0.02, 0.025 })
        foreach (double km in LowArcs)
        {
            Outcome off = Fly(km, 0.0, step, 0.5);
            Outcome on = Fly(km, 0.5, step, 0.5);
            Out.WriteLine($"{km,4:F0} km {step * 1000,2:F0} ms: off {off.FloorRateDegPerSec,6:F1} deg/s {off.ResidualMetresPerSecond,6:F1} m/s, "
                          + $"on {on.FloorRateDegPerSec,6:F1} deg/s {on.ResidualMetresPerSecond,6:F1} m/s");

            if (off.FloorRateDegPerSec > 20.0) spunOff++;
            if (on.FloorRateDegPerSec > 20.0) spunOn++;
            worstOn = Math.Max(worstOn, on.ResidualMetresPerSecond);
        }

        // The rig has to show the fault, or a quiet result with the flag on says nothing about it.
        Assert.True(spunOff >= 5, $"the rig spun on only {spunOff} of 8 with the line followed to the end");
        Assert.True(spunOn <= 2, $"{spunOn} of 8 still spun with the line slowed");
        Assert.True(worstOn < 30.0, $"slowing the line left {worstOn:F1} m/s ungained");
    }

    // Cut off in the air on a vacuum arc, a shot falls short by the drag; solved with it, it does not.
    // Only where the arc stays under the release altitude and the warheads leave at cutoff: above it they
    // are released as a long shot's are, which the rig does not fly.
    [Fact]
    public void AShotFinishedInTheAirLandsWhenItsArcIsSolvedWithDrag()
    {
        double worstVacuum = 0.0, worstDrag = 0.0;
        int spun = 0;

        foreach (double step in new[] { 0.02, 0.025 })
        foreach (double km in LowArcs)
        {
            Outcome vacuum = Fly(km, 0.5, step, 0.5, finishInTheAir: true);
            Outcome drag = Fly(km, 0.5, step, 0.5, finishInTheAir: true, solveWithDrag: true);
            Out.WriteLine($"{km,4:F0} km {step * 1000,2:F0} ms: vacuum arc {vacuum.MissKm,6:F2} km, "
                          + $"drag solved {drag.MissKm,6:F2} km, {drag.FloorRateDegPerSec,5:F1} deg/s at the floor");

            worstVacuum = Math.Max(worstVacuum, vacuum.MissKm);
            worstDrag = Math.Max(worstDrag, drag.MissKm);
            if (drag.FloorRateDegPerSec > 20.0) spun++;
        }

        Assert.True(worstVacuum > 2.0, $"the vacuum arc missed by at most {worstVacuum:F2} km, so this measures nothing");
        Assert.True(worstDrag < 0.5, $"solved with drag it still missed by {worstDrag:F2} km");
        Assert.Equal(0, spun);
    }

    // A warhead leaves its tube at half a metre a second along the line, and flown without that every low arc
    // landed 69-214 m long. The drag solve has to fly the throw the warhead will actually get.
    [Fact]
    public void TheDragSolveFliesTheTubesThrow()
    {
        double unseen = 0.0, seen = 0.0, worstSeen = 0.0;

        foreach (double km in LowArcs)
        {
            Outcome without = Fly(km, 0.5, 0.02, 0.5, true, true, throw_: 0.5, throwHandedOver: false);
            Outcome with = Fly(km, 0.5, 0.02, 0.5, true, true, throw_: 0.5, throwHandedOver: true);
            Out.WriteLine($"{km,4:F0} km: throw left out {without.MissKm * 1000,6:F0} m, flown {with.MissKm * 1000,6:F0} m");

            unseen += without.MissKm / LowArcs.Length;
            seen += with.MissKm / LowArcs.Length;
            worstSeen = Math.Max(worstSeen, with.MissKm);
        }

        Assert.True(unseen - seen > 0.05, $"flying the throw moved the mean miss from {unseen:F3} to {seen:F3} km");
        Assert.True(worstSeen < 0.3, $"flown with the throw it still missed by {worstSeen:F2} km");
    }
}

/// <summary>
/// A stack whose solids overshoot a short shot, flown with the throttle lever carried through the solid
/// stage as KSA's is. <see cref="IcbmConfig.SolidsLeaveMetresPerSecond"/>.
/// </summary>
public class SolidsLeaveTests(ITestOutputHelper Out)
{
    private (double PeakRate, double Residual) Fly(double km, double leave, double step, bool drop = false)
    {
        IcbmFlightRig rig = GameStackShortRangeTests.GameStack(true);
        rig.AttitudeHasInertia = true;
        rig.StepJitter = 0.5;
        rig.LeverMovesUnderSolids = true;
        rig.Warhead = Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21);

        IcbmConfig config = FloorHoldTests.Config(0.5, true, true);
        config.SolidsLeaveMetresPerSecond = leave;
        config.DropSolidsUnderWeight = drop;
        IcbmProgram program = new(config);
        rig.Fly(program, GameStackShortRangeTests.South(km * 1000.0), step, 6_000.0);

        return (rig.PeakClosedLoopRateDegPerSec, program.ResidualAtCutoff);
    }

    // Shots the solids alone can carry: the stage after them is left only a margin.
    [Fact]
    public void TheStageAfterSolidsThatCarryTheShotDoesNotSpin()
    {
        int spunOff = 0;
        double worstRate = 0.0, worstResidual = 0.0;

        foreach (double km in new[] { 25.0, 40.0, 60.0, 100.0 })
        foreach (double step in new[] { 0.02, 0.025 })
        {
            var off = Fly(km, 0.0, step);
            var on = Fly(km, 30.0, step);
            Out.WriteLine($"{km,3:F0} km {step * 1000,2:F0} ms: matched {off.PeakRate,6:F1} deg/s {off.Residual,5:F2} m/s | "
                          + $"30 m/s left {on.PeakRate,6:F1} deg/s {on.Residual,5:F2} m/s");

            if (off.PeakRate > 60.0) spunOff++;
            worstRate = Math.Max(worstRate, on.PeakRate);
            worstResidual = Math.Max(worstResidual, on.Residual);
        }

        Assert.True(spunOff >= 6, $"matched to the solids only {spunOff} of 8 spun, so this measures nothing");
        Assert.True(worstRate < 15.0, $"leaving a margin still turned at {worstRate:F1} deg/s");
        Assert.True(worstResidual < 1.5, $"leaving a margin left {worstResidual:F2} m/s at cutoff");
    }

    // Shots the stage after the solids carries most of: what sags the path is the solids' tail-off.
    [Fact]
    public void SolidsDroppedUnderWeightHandOverWithoutASwing()
    {
        int swungCarried = 0;
        double worstRate = 0.0, worstResidual = 0.0;

        foreach (double km in new[] { 150.0, 200.0, 300.0, 418.0 })
        foreach (double step in new[] { 0.02, 0.025 })
        {
            var off = Fly(km, 30.0, step);
            var on = Fly(km, 30.0, step, drop: true);
            Out.WriteLine($"{km,3:F0} km {step * 1000,2:F0} ms: carried {off.PeakRate,6:F1} deg/s {off.Residual,5:F2} m/s | "
                          + $"dropped {on.PeakRate,6:F1} deg/s {on.Residual,5:F2} m/s");

            if (off.PeakRate > 35.0) swungCarried++;
            worstRate = Math.Max(worstRate, on.PeakRate);
            worstResidual = Math.Max(worstResidual, on.Residual);
        }

        Assert.True(swungCarried >= 6, $"carried through the tail-off only {swungCarried} of 8 swung, so this measures nothing");
        Assert.True(worstRate < 30.0, $"dropped under weight it still turned at {worstRate:F1} deg/s");
        Assert.True(worstResidual < 1.0, $"dropped under weight it left {worstResidual:F2} m/s at cutoff");
    }
}

/// <summary>
/// How far a metre a second left at cutoff moves a low arc's landing, along the thrust, up and across --
/// which turns the residual at cutoff into metres on the ground.
/// </summary>
[Trait("kind", "study")]
public class LowArcSensitivityStudy(ITestOutputHelper Out)
{
    [Fact]
    public void WhatAMetreASecondAtCutoffCosts()
    {
        MunitionProfile warhead = Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21);
        Func<double3, double> air = p => Math.Exp(-Math.Max(0.0, Vec.Len(p) - FloorHoldTests.R) / 8_000.0);
        BallisticBody earth = FloorHoldTests.Earth;

        foreach (double km in new[] { 25.0, 150.0, 200.0, 300.0, 418.0 })
        {
            IcbmFlightRig rig = GameStackShortRangeTests.GameStack(true);
            rig.AttitudeHasInertia = true;
            rig.Warhead = warhead;
            IcbmProgram program = new(FloorHoldTests.Config(0.5, true, true));
            IcbmFlightRig.Flight flight = rig.Fly(program, GameStackShortRangeTests.South(km * 1000.0), 0.02, 6_000.0);

            double3 r = flight.CutoffPositionCci, v = flight.CutoffVelocityCci;
            double3 along = Vec.Unit(v - earth.GroundVelocityCci(r));
            double3 up = Vec.Unit(r - along * Vec.Dot(r, along));
            double3 across = Vec.Unit(Vec.Cross(along, up));

            if (!Land(r, v, out double3 base0)) { Out.WriteLine($"{km} km: no landing"); continue; }

            string row = $"{km,4:F0} km, cut at {earth.AltitudeOf(r) / 1000.0,4:F0} km:";
            foreach ((string name, double3 axis) in new[] { ("along", along), ("up", up), ("across", across) })
            {
                row += Land(r, v + axis, out double3 moved) ? $" {name} {FloorHoldTests.R * Vec.AngleBetween(base0, moved),6:F0} m" : $" {name} --";
            }
            Out.WriteLine(row + " per m/s");
        }

        bool Land(double3 r, double3 v, out double3 point)
        {
            point = default;
            if (!ImpactPredictor.TryPredict(earth, r, v, 1.0, ImpactPredictor.DefaultMaxSeconds,
                                            out ImpactPredictor.Impact hit, drag: new ImpactPredictor.Drag(air, warhead)))
            {
                return false;
            }
            point = hit.GroundFixedPointCci;
            return true;
        }
    }
}

/// <summary>
/// <see cref="FloorHoldTests"/> across frame steps, jitter and settings, for the record of how the setting
/// was chosen.
/// </summary>
[Trait("kind", "study")]
public class FloorHoldStudy(ITestOutputHelper Out)
{
    [Fact]
    public void TheSlowedLineAcrossStepsAndSettings()
    {
        foreach ((double seconds, bool inAir, bool drag) in new[]
                 {
                     (0.0, false, false), (0.5, false, false), (0.5, true, false), (0.5, true, true), (0.0, false, true),
                 })
        {
            int flights = 0, spun = 0, over10 = 0;
            double worst = 0.0;
            List<double> misses = [];

            foreach (double step in new[] { 0.017, 0.02, 0.025, 0.033 })
            foreach (double jitter in new[] { 0.5, 0.2 })
            foreach (double km in FloorHoldTests.Ranges)
            {
                FloorHoldTests.Outcome o = FloorHoldTests.Fly(km, seconds, step, jitter, inAir, drag);
                flights++;
                if (o.FloorRateDegPerSec > 20.0) spun++;
                if (o.ResidualMetresPerSecond > 10.0) over10++;
                worst = Math.Max(worst, o.ResidualMetresPerSecond);
                if (double.IsFinite(o.MissKm)) misses.Add(o.MissKm);
            }

            misses.Sort();
            Out.WriteLine($"{seconds:F2} s{(inAir ? ", in the air" : "")}{(drag ? ", drag solved" : "")}: spun at the floor {spun,2}/{flights}, over 10 m/s ungained {over10,2}/{flights}, "
                          + $"worst {worst,6:F1} m/s, miss median {misses[misses.Count / 2]:F2} km, worst {misses[^1]:F2} km");
        }
    }
}
