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
            ShortShotSolvesWithDrag = solveWithDrag,
        };

    internal readonly record struct Outcome(double FloorRateDegPerSec, double ResidualMetresPerSecond, double MissKm);

    internal static Outcome Fly(double km, double slowLineSeconds, double step, double jitter, bool finishInTheAir = false,
                                bool solveWithDrag = false)
    {
        IcbmFlightRig rig = GameStackShortRangeTests.GameStack(true);
        rig.AttitudeHasInertia = true;
        rig.StepJitter = jitter;
        rig.Warhead = Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21);

        IcbmProgram program = new(Config(slowLineSeconds, finishInTheAir, solveWithDrag));
        double3 aim = GameStackShortRangeTests.South(km * 1000.0);
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, step, 6_000.0);

        double miss = double.NaN;
        if (ImpactPredictor.TryPredict(Earth, flight.CutoffPositionCci, flight.CutoffVelocityCci, 1.0,
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
