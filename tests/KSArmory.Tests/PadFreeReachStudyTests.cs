using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What a free arrival's reach looks like before the flight: the pad's cheapest conic coasted to the release gate and
/// its columns flown there, against the pinned disc the pad draws today and the reach the coast prices the same walk on.
/// <c>docs/WIDE-SPREAD.md</c> step 8.
/// </summary>
[Trait("kind", "study")]
public class PadFreeReachStudyTests(ITestOutputHelper Out)
{
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(3.986004418e14, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0);

    private static IcbmConfig Wide() => new()
    {
        Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, ArrivalPreference = 0.5, FlyAnyRange = true,
        PulsesBelowTheFloor = false, SpendTheSplitReserveAfterTheFirstStop = true,
        WalkStartsAtCutoff = true, HopsFlyTheirOwnCeiling = true, PriceSlotsOnTheGateChord = true,
        HopsSizeTheirOwnSlots = true, TrimBudgetMetresPerSecond = 1_000.0, HopsFreeTheirArrival = true,
        PriceTheTankStopByStop = true, FreeHopsMeasureTheirSlots = true, WalkShrinksAfterTheSplit = true,
        HopsResolveAfterTheirBurn = true, HopsBurnTheEngine = true, FreeHopsCountTheArrivalTheyMove = true,
        EngineHopsClearTheStack = true,
    };

    // IcbmComputer.RefreshReach's Bus on the pad: no coast yet, no tank read before the split.
    private static ReleaseItinerary.Bus PadBus(IcbmConfig c) => new(
        c.ReleaseBeforeArrivalSeconds, double.NaN, 7, FromCutoff: true,
        ReserveAfterFirstStopMetresPerSecond: ReleaseLoop.SplitNullReserveMetresPerSecond,
        HopsFlyTheirOwnCeiling: true,
        BudgetMetresPerSecond: ReleaseLoop.PlanningBudget(c.TrimBudgetMetresPerSecond, double.NaN,
                                                          ReleaseLoop.SplitNullReserveMetresPerSecond),
        HopAccelerationMetresPerSecond2: BuiltIns.MirvBus.HopEngineAccelerationMetresPerSecond2, FreeArrival: true,
        TankKg: 500.0, MassKg: 3_800.0,
        TankExhaustVelocity: BuiltIns.MirvBus.HopEngineExhaustVelocity, StoreMassKg: 250.0,
        HopTurnSeconds: HopBurn.TurnsSeconds, PassesMetresPerSecondPerStop: ReleaseItinerary.EnginePassesMetresPerSecond);

    private static int Placed(DivertFootprint footprint, IcbmConfig c, double spacing)
    {
        List<ReachDisplay.Placed> placed = [new(0.0, 0.0, 1)];
        for (int k = 1; k < 7; k++)
        {
            ReachDisplay d = ReachDisplay.For(footprint, placed, PadBus(c), IcbmPhase.Idle, false, 7, 2_000.0, 0,
                                              ReachHold.EpochUnmeasured, c.PriceEachHopAtItsSlot,
                                              c.PriceHopsAtWhatTheTrimPays, c.PriceSlotsOnTheGateChord, null,
                                              engineHops: true, c.FreeHopsCountTheArrivalTheyMove);
            if (d.Verdict(TargetClick.Add, true, -spacing * k, 0.0) != ReachVerdict.Adds) return k;
            placed.Add(new(-spacing * k, 0.0, 1));
        }

        return 7;
    }

    private static bool TryPadFree(double3 pad, double3 aim, double gate, out DivertFootprint footprint)
    {
        footprint = default;
        BallisticBody earth = Earth;
        if (!BallisticArc.TryCheapest(earth, pad, earth.GroundVelocityCci(pad), aim, out BallisticArc.Solution arc)) return false;
        if (!Kepler.TryCoast(earth.Mu, pad, arc.RequiredVelocityCci, arc.FlightSeconds - gate, out double3 p, out double3 v))
        {
            return false;
        }

        ReleaseFocus.Air air = new(new ImpactPredictor.Drag(DensityAt, BuiltIns.ReentryVehicleMk21), 2.0, R, true);
        return ReleaseFocus.FlownSensitivity.TryFly(earth, p, v, air) is { } flown
               && DivertFootprint.TryFrom(earth, flown, DivertFootprint.ArrivalClock.Free, fromTheRealState: false,
                                          out footprint);
    }

    [Theory]
    [InlineData(2_000_000.0)]
    [InlineData(6_181_000.0)]
    [InlineData(12_902_000.0)]
    public void ThePadsFreeReachAgainstWhatTheWalkFlies(double range)
    {
        IcbmConfig c = Wide();
        double3 pad = GameStackShortRangeTests.GameStack(true).PositionCci;
        double3 aim = GameStackShortRangeTests.South(range);

        Assert.True(DivertFootprint.TryAtTheEpoch(default, c.ReleaseBeforeArrivalSeconds, out DivertFootprint pinned));
        Assert.True(TryPadFree(pad, aim, c.ReleaseBeforeArrivalSeconds, out DivertFootprint free), "no free pad reach");
        Out.WriteLine($"{range / 1000:F0} km: pinned {pinned.SemiMajorMetresPerMetrePerSecond:F0} m per m/s, free "
                      + $"{free.SemiMajorMetresPerMetrePerSecond:F0} x {free.SemiMinorMetresPerMetrePerSecond:F0}");

        foreach (double spacing in new[] { 50_000.0, 100_000.0, 150_000.0, 200_000.0, 300_000.0, 400_000.0 })
        {
            int onPinned = Placed(pinned, c, spacing);
            int onFree = Placed(free, Wide(), spacing);

            PastCutoffRig rig = PastCutoffRig.For(GameStackShortRangeTests.GameStack(true), new IcbmProgram(Wide()),
                                                  BuiltIns.ReentryVehicleMk21.Copy(), DensityAt);
            rig.ShoveMetresPerSecond = 1.0;
            rig.ThrusterMetresPerSecond2 = BuiltIns.MirvBus.TrimAccelerationMetresPerSecond2;
            rig.BusMassKg = 3_800.0;
            rig.PropellantKg = 500.0;
            rig.JetExhaustVelocity = 2_912.0;
            rig.EngineThrustNewtons = BuiltIns.MirvBus.HopEngineThrustNewtons;
            rig.EngineExhaustVelocity = BuiltIns.MirvBus.HopEngineExhaustVelocity;
            for (int k = 0; k < 7; k++) rig.PlannedTargets.Add((GameStackShortRangeTests.South(range - k * spacing), 1));
            rig.PlannedLead = 0;
            rig.Fly(aim);

            int within = rig.Stops.Count(s => s.LandedMetres < 2_000.0);
            BallisticArc.TryCheapest(Earth, pad, Earth.GroundVelocityCci(pad), aim, out BallisticArc.Solution padArc);
            Out.WriteLine($"  pad conic {padArc.FlightSeconds:F0} s, the coast flown {rig.CoastSeconds:F0} s");
            Out.WriteLine($"  {spacing / 1000:F0} km apart: pad places {onPinned} pinned, {onFree} free; the coast planned "
                          + $"{rig.Plan.Stops}, flew {rig.Stops.Count} ({within} within 2 km), curtailed {rig.Curtailed}");
        }
    }
}
