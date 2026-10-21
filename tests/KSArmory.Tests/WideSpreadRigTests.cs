using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// <see cref="IcbmConfig.HopsSizeTheirOwnSlots"/> on walks wider than a 60 m/s budget reaches, all three walk flags on
/// (<c>docs/WIDE-SPREAD.md</c> step 2), and <see cref="IcbmConfig.HopsFreeTheirArrival"/> on walks wider than a pinned
/// arrival pays for, with the plan remade after the split, a long hop's burn solved away, and hops on the
/// post-boost bus's engine (step 5).
/// </summary>
public class WideSpreadRigTests(ITestOutputHelper Out)
{
    private const double R = 6_371_000.0;

    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0);

    // The Heavy bus as HopCeilingRigTests flies it, with its loaded mass so the jets push harder as warheads leave.
    private PastCutoffRig Walk(double rangeMetres, int targets, double spacingMetres, bool sized, double budget,
                               bool slotsFollowTheJets = true, double propellantKg = double.NaN, bool guard = true)
    {
        IcbmProgram program = new(new IcbmConfig
        {
            Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, ArrivalPreference = 0.5, FlyAnyRange = true,
            PulsesBelowTheFloor = false, SpendTheSplitReserveAfterTheFirstStop = true,
            WalkStartsAtCutoff = true, HopsFlyTheirOwnCeiling = true, PriceSlotsOnTheGateChord = true,
            HopsSizeTheirOwnSlots = sized, TrimBudgetMetresPerSecond = budget,
        });

        PastCutoffRig rig = PastCutoffRig.For(GameStackShortRangeTests.GameStack(true), program,
                                              BuiltIns.ReentryVehicleMk21.Copy(), DensityAt);
        rig.ThrusterMetresPerSecond2 = 0.55;
        rig.ShoveMetresPerSecond = 1.0;
        rig.BusMassKg = 2_900.0;
        rig.SlotsFollowTheJets = slotsFollowTheJets;
        rig.PropellantKg = propellantKg;
        rig.GuardsTheTank = guard;
        for (int k = 0; k < targets; k++)
        {
            rig.PlannedTargets.Add((GameStackShortRangeTests.South(rangeMetres - k * spacingMetres), 1));
        }
        rig.PlannedLead = 0;

        rig.Fly(GameStackShortRangeTests.South(rangeMetres));
        Out.WriteLine($"{rangeMetres / 1_000.0:F0} km, {targets} at {spacingMetres / 1_000.0:F0} km, sized {sized}, "
                      + $"budget {budget:F0}, jets {slotsFollowTheJets}: planned {rig.Plan.Stops} ({rig.Plan.Hold}), "
                      + $"flew {rig.Stops.Count}, curtailed {rig.Curtailed}, ran dry {rig.RanDry}, "
                      + $"{rig.PropellantUsedKg:F1} kg used");
        for (int k = 0; k < rig.Stops.Count; k++)
        {
            Out.WriteLine($"  stop {k + 1}: landed {rig.Stops[k].LandedMetres:F2} m, divert "
                          + $"{rig.Stops[k].DivertMetresPerSecond:F2} m/s, priced "
                          + $"{rig.Plan.Itinerary.Stops[k].HopMetresPerSecond:F2}, slot "
                          + $"{rig.Plan.Itinerary.Stops[k].SlotSeconds:F0} s");
        }

        return rig;
    }

    [Fact]
    public void ARaisedBudgetWidensAWalkOnlyOnceItIsPlannedOn()
    {
        PastCutoffRig shipped = Walk(6_181_000.0, 6, 16_000.0, sized: false, budget: 140.0);
        PastCutoffRig sized = Walk(6_181_000.0, 6, 16_000.0, sized: true, budget: 140.0);

        Assert.True(shipped.Stops.Count < 6, $"planned on 60 m/s it still walked {shipped.Stops.Count} stops");
        Assert.Equal(6, sized.Stops.Count);
        Assert.False(sized.Curtailed, "a stop the plan paid for was not flown");
        Assert.All(sized.Stops, stop => Assert.True(stop.LandedMetres < 0.01, $"landed {stop.LandedMetres:F3} m out"));
    }

    [Fact]
    public void AHopLongerThanTheTrimClockLandsOnlyWhenTheClockFollowsItsBurn()
    {
        PastCutoffRig fixedClock = Walk(6_181_000.0, 3, 60_000.0, sized: true, budget: 140.0, slotsFollowTheJets: false);
        PastCutoffRig ownClock = Walk(6_181_000.0, 3, 60_000.0, sized: true, budget: 140.0);

        Assert.True(ownClock.Stops.Count > 1, $"the fixture walks {ownClock.Stops.Count} stop");
        Assert.Contains(fixedClock.Stops, stop => stop.LandedMetres > 1_000.0);
        Assert.All(ownClock.Stops, stop => Assert.True(stop.LandedMetres < 0.01, $"landed {stop.LandedMetres:F3} m out"));
    }

    [Fact]
    public void ThreeTargetsSixtyKilometresApartWalkOnTheHeavyBusFromTwelveThousandKilometres()
    {
        PastCutoffRig rig = Walk(12_902_000.0, 3, 60_000.0, sized: true, budget: 140.0);

        Assert.Equal(3, rig.Stops.Count);
        Assert.False(rig.Curtailed);
        Assert.All(rig.Stops, stop => Assert.True(stop.LandedMetres < 0.01, $"landed {stop.LandedMetres:F3} m out"));
    }

    // The Heavy bus's own 134.3 kg (docs/ICBM-GUIDANCE.md), against a budget the tank cannot pay.
    [Fact]
    public void ABudgetPastTheTankEndsTheWalkAStopEarlyRatherThanRunningDry()
    {
        PastCutoffRig unguarded = Walk(6_181_000.0, 3, 50_000.0, sized: true, budget: 200.0, propellantKg: 134.3,
                                       guard: false);
        PastCutoffRig guarded = Walk(6_181_000.0, 3, 50_000.0, sized: true, budget: 200.0, propellantKg: 134.3);

        Assert.True(unguarded.RanDry, "the fixture never reached the bottom of the tank");
        Assert.Contains(unguarded.Stops, stop => stop.LandedMetres > 1_000.0);

        Assert.False(guarded.RanDry, "the guarded walk emptied the tank");
        Assert.True(guarded.Curtailed, "the guarded walk did not end early");
        Assert.All(guarded.Stops, stop => Assert.True(stop.LandedMetres < 0.01, $"landed {stop.LandedMetres:F3} m out"));
    }

    // The post-boost bus on its nozzles alone: 0.40 m/s² forward, 3.8 t wet, 500 kg at 2,912 m/s (docs/POSTBOOST-BUS.md).
    [Fact]
    public void TheHeavyMirvBusWalksSevenTargetsFiftyKilometresApartOnItsNozzles()
    {
        IcbmProgram program = new(new IcbmConfig
        {
            Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, ArrivalPreference = 0.5, FlyAnyRange = true,
            PulsesBelowTheFloor = false, SpendTheSplitReserveAfterTheFirstStop = true,
            WalkStartsAtCutoff = true, HopsFlyTheirOwnCeiling = true, PriceSlotsOnTheGateChord = true,
            HopsSizeTheirOwnSlots = true, TrimBudgetMetresPerSecond = 1_000.0,
        });

        PastCutoffRig rig = PastCutoffRig.For(GameStackShortRangeTests.GameStack(true), program,
                                              BuiltIns.ReentryVehicleMk21.Copy(), DensityAt);
        rig.ThrusterMetresPerSecond2 = BuiltIns.MirvBus.TrimAccelerationMetresPerSecond2;
        rig.ShoveMetresPerSecond = 1.0;
        rig.BusMassKg = 3_800.0;
        rig.PropellantKg = 500.0;
        rig.JetExhaustVelocity = 2_912.0;
        for (int k = 0; k < 7; k++) rig.PlannedTargets.Add((GameStackShortRangeTests.South(12_902_000.0 - k * 50_000.0), 1));
        rig.PlannedLead = 0;

        rig.Fly(GameStackShortRangeTests.South(12_902_000.0));
        Out.WriteLine($"planned {rig.Plan.Stops}, flew {rig.Stops.Count}, {rig.PropellantUsedKg:F0} kg used, "
                      + $"spent {rig.Stops[^1].SpentMetresPerSecond:F0} m/s");

        Assert.Equal(7, rig.Stops.Count);
        Assert.False(rig.Curtailed);
        Assert.False(rig.RanDry);
        Assert.All(rig.Stops, stop => Assert.True(stop.LandedMetres < 0.01, $"landed {stop.LandedMetres:F3} m out"));
    }

    // Either bus on its own tank, a budget only the tank bounds, the targets laid uprange of each other along the track.
    private PastCutoffRig Hops(bool postBoostBus, double rangeMetres, int targets, double spacingMetres, bool free,
                               bool remade = false, bool resolve = false, bool engine = false, bool shift = false)
    {
        IcbmProgram program = new(new IcbmConfig
        {
            Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, ArrivalPreference = 0.5, FlyAnyRange = true,
            PulsesBelowTheFloor = false, SpendTheSplitReserveAfterTheFirstStop = true,
            WalkStartsAtCutoff = true, HopsFlyTheirOwnCeiling = true, PriceSlotsOnTheGateChord = true,
            HopsSizeTheirOwnSlots = true, TrimBudgetMetresPerSecond = 1_000.0, HopsFreeTheirArrival = free,
            PriceTheTankStopByStop = remade, FreeHopsMeasureTheirSlots = remade, WalkShrinksAfterTheSplit = remade,
            HopsResolveAfterTheirBurn = resolve, HopsBurnTheEngine = engine, FreeHopsCountTheArrivalTheyMove = shift,
        });

        PastCutoffRig rig = PastCutoffRig.For(GameStackShortRangeTests.GameStack(true), program,
                                              BuiltIns.ReentryVehicleMk21.Copy(), DensityAt);
        rig.ShoveMetresPerSecond = 1.0;
        if (postBoostBus)
        {
            rig.ThrusterMetresPerSecond2 = BuiltIns.MirvBus.TrimAccelerationMetresPerSecond2;
            rig.BusMassKg = 3_800.0;
            rig.PropellantKg = 500.0;
            rig.JetExhaustVelocity = 2_912.0;
            rig.EngineThrustNewtons = BuiltIns.MirvBus.HopEngineThrustNewtons;
            rig.EngineExhaustVelocity = BuiltIns.MirvBus.HopEngineExhaustVelocity;
        }
        else
        {
            rig.ThrusterMetresPerSecond2 = 0.55;
            rig.BusMassKg = 2_900.0;
            rig.PropellantKg = 134.3;
        }
        for (int k = 0; k < targets; k++)
        {
            rig.PlannedTargets.Add((GameStackShortRangeTests.South(rangeMetres - k * spacingMetres), 1));
        }
        rig.PlannedLead = 0;

        rig.Fly(GameStackShortRangeTests.South(rangeMetres));
        Out.WriteLine($"{(postBoostBus ? "post-boost" : "Heavy")} {rangeMetres / 1_000.0:F0} km, {targets} at "
                      + $"{spacingMetres / 1_000.0:F0} km, {(free ? "free" : "pinned")}{(remade ? ", remade" : "")}{(resolve ? ", re-solved" : "")}: planned {rig.Plan.Stops} "
                      + $"({rig.Plan.Hold}), flew {rig.Stops.Count}, curtailed {rig.Curtailed}, ran dry {rig.RanDry}, "
                      + $"{rig.PropellantUsedKg:F1} kg used");
        for (int k = 0; k < rig.Stops.Count; k++)
        {
            string priced = k < rig.Plan.Itinerary.Stops.Count ? $"{rig.Plan.Itinerary.Stops[k].HopMetresPerSecond:F2}" : "-";
            Out.WriteLine($"  stop {k + 1}: landed {rig.Stops[k].LandedMetres:F4} m, divert "
                          + $"{rig.Stops[k].DivertMetresPerSecond:F2} m/s, priced {priced}");
        }

        return rig;
    }

    [Theory]
    [InlineData(false, 6_181_000.0, 6, 40_000.0)]
    [InlineData(false, 12_902_000.0, 6, 60_000.0)]
    [InlineData(true, 12_902_000.0, 7, 100_000.0)]
    public void AFreeArrivalWalksEveryStopWhereThePinnedOneFallsShort(bool postBoostBus, double rangeMetres, int targets,
                                                                      double spacingMetres)
    {
        PastCutoffRig pinned = Hops(postBoostBus, rangeMetres, targets, spacingMetres, free: false);
        PastCutoffRig free = Hops(postBoostBus, rangeMetres, targets, spacingMetres, free: true);

        Assert.True(pinned.Stops.Count < targets, $"the pinned walk flew all {targets} stops");
        Assert.Equal(targets, free.Stops.Count);
        Assert.False(free.Curtailed, "the free walk ended early");
        Assert.False(free.RanDry, "the free walk emptied the tank");
        Assert.All(free.Stops, stop => Assert.True(stop.LandedMetres < 0.01, $"landed {stop.LandedMetres:F3} m out"));
    }

    // A hop flown past its price by more than the tank's margin is one the tank was not held back for.
    [Theory]
    [InlineData(false, 6_181_000.0, 6, 25_000.0)]
    [InlineData(true, 12_902_000.0, 7, 100_000.0)]
    public void AFreeHopFliesWithinTheTanksMarginOfItsPriceOnceThePlanIsRemadeAfterTheSplit(
        bool postBoostBus, double rangeMetres, int targets, double spacingMetres)
    {
        static bool Honest(PastCutoffRig rig, int k)
        {
            double ratio = rig.Stops[k].DivertMetresPerSecond / rig.Plan.Itinerary.Stops[k].HopMetresPerSecond;
            return ratio <= 1.0 / (1.0 - ReleaseLoop.TankMargin) && ratio >= 0.85;
        }

        PastCutoffRig stale = Hops(postBoostBus, rangeMetres, targets, spacingMetres, free: true);
        PastCutoffRig remade = Hops(postBoostBus, rangeMetres, targets, spacingMetres, free: true, remade: true);

        Assert.Contains(Enumerable.Range(1, stale.Stops.Count - 1), k => !Honest(stale, k));

        Assert.Equal(targets, remade.Stops.Count);
        Assert.False(remade.Curtailed);
        Assert.False(remade.RanDry);
        Assert.All(remade.Stops, stop => Assert.True(stop.LandedMetres < 0.01, $"landed {stop.LandedMetres:F3} m out"));
        Assert.All(Enumerable.Range(1, remade.Stops.Count - 1),
                   k => Assert.True(Honest(remade, k), $"hop {k} flew {remade.Stops[k].DivertMetresPerSecond:F2} m/s "
                                                       + $"on a price of {remade.Plan.Itinerary.Stops[k].HopMetresPerSecond:F2}"));
    }

    // Two targets 300 km apart on the post-boost bus: a 180 m/s hop at 0.40 m/s² burns for about 450 s.
    [Fact]
    public void ALongHopsBurnIsSolvedAwayBeforeTheAimCorrectionReadsItAsAMiss()
    {
        PastCutoffRig asFlown = Hops(true, 6_181_000.0, 2, 300_000.0, free: true);
        PastCutoffRig resolved = Hops(true, 6_181_000.0, 2, 300_000.0, free: true, resolve: true);

        Assert.Equal(2, asFlown.Stops.Count);
        Assert.True(asFlown.Stops[1].LandedMetres > 1_000.0, $"landed {asFlown.Stops[1].LandedMetres:F1} m out");

        Assert.Equal(2, resolved.Stops.Count);
        Assert.All(resolved.Stops, stop => Assert.True(stop.LandedMetres < 0.01, $"landed {stop.LandedMetres:F3} m out"));
        Assert.True(resolved.Stops[1].DivertMetresPerSecond < asFlown.Stops[1].DivertMetresPerSecond);
    }

    // The post-boost bus from 6,181 km, where 0.40 m/s2 of nozzles runs out of coast before it runs out of tank.
    [Fact]
    public void TheEngineWalksSevenTargetsAHundredKilometresApartWhereTheNozzlesRunOutOfCoast()
    {
        PastCutoffRig nozzles = Hops(true, 6_181_000.0, 7, 100_000.0, free: true, resolve: true);
        PastCutoffRig engine = Hops(true, 6_181_000.0, 7, 100_000.0, free: true, resolve: true, engine: true);

        Assert.True(nozzles.Stops.Count < 7, $"the nozzles walked all {nozzles.Stops.Count}");
        Assert.All(nozzles.Stops, stop => Assert.Equal(0.0, stop.EngineMetresPerSecond));

        Assert.Equal(7, engine.Stops.Count);
        Assert.False(engine.Curtailed);
        Assert.False(engine.RanDry);
        Assert.All(engine.Stops.Skip(1), stop => Assert.True(stop.EngineMetresPerSecond > HopBurn.EngineAboveMetresPerSecond,
                                                               $"a hop flew {stop.EngineMetresPerSecond:F1} m/s on the engine"));
        Assert.All(engine.Stops, stop => Assert.True(stop.LandedMetres < 0.01, $"landed {stop.LandedMetres:F3} m out"));
    }

    /// <summary>
    /// What a free arrival would buy (<c>docs/WIDE-SPREAD.md</c> step 4, branch A): an along-track hop from the coast's
    /// start, solved to the committed arrival against left to find its own, vacuum, two-body.
    /// </summary>
    [Theory]
    [InlineData(6_181_000.0, 2.5)]
    [InlineData(12_902_000.0, 5.0)]
    public void AFreeArrivalMakesAnAlongTrackHopSeveralTimesCheaper(double rangeMetres, double atLeast)
    {
        const double mu = 3.986004418e14;
        BallisticBody earth = new(mu, R, new double3(0, 0, 1), 7.2921159e-5);
        double3 cutoff = new(R + 200_000.0, 0, 0);

        double3 Along(double metres) => new double3(Math.Cos(metres / R), Math.Sin(metres / R), 0) * R;

        Assert.True(BallisticArc.TryCheapest(earth, cutoff, Vec.Zero, Along(rangeMetres), out BallisticArc.Solution arc));
        Assert.True(Kepler.TryCoast(mu, cutoff, arc.RequiredVelocityCci, 30.0, out double3 p, out double3 v));
        double toGo = arc.FlightSeconds - 30.0;

        foreach (double hop in new[] { -50_000.0, 50_000.0, 300_000.0 })
        {
            double3 aimNow = earth.CarryCci(Along(rangeMetres + hop), 30.0);
            Assert.True(BallisticArc.TrySolve(earth, p, aimNow, toGo, out BallisticArc.Solution pinned));
            Assert.True(BallisticArc.TryCheapest(earth, p, v, aimNow, out BallisticArc.Solution free, seedFlightSeconds: toGo));

            double ratio = Vec.Len(pinned.RequiredVelocityCci - v) / Vec.Len(free.RequiredVelocityCci - v);
            Out.WriteLine($"{rangeMetres / 1_000.0:F0} km, {hop / 1_000.0:+0;-0} km: pinned "
                          + $"{Vec.Len(pinned.RequiredVelocityCci - v):F1} m/s, free {Vec.Len(free.RequiredVelocityCci - v):F1} "
                          + $"({ratio:F2}x), arrival {free.FlightSeconds - toGo:+0;-0} s");
            Assert.True(ratio > atLeast, $"a free arrival bought only {ratio:F2}x");
        }
    }
}
