using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// <see cref="IcbmConfig.HopsSizeTheirOwnSlots"/> on walks wider than a 60 m/s budget reaches, all three walk flags on
/// (<c>docs/WIDE-SPREAD.md</c> step 2).
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
                                              Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21), DensityAt);
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
    public void ThePostBoostBusWalksSevenTargetsFiftyKilometresApartOnItsNozzles()
    {
        IcbmProgram program = new(new IcbmConfig
        {
            Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, ArrivalPreference = 0.5, FlyAnyRange = true,
            PulsesBelowTheFloor = false, SpendTheSplitReserveAfterTheFirstStop = true,
            WalkStartsAtCutoff = true, HopsFlyTheirOwnCeiling = true, PriceSlotsOnTheGateChord = true,
            HopsSizeTheirOwnSlots = true, TrimBudgetMetresPerSecond = 1_000.0,
        });

        PastCutoffRig rig = PastCutoffRig.For(GameStackShortRangeTests.GameStack(true), program,
                                              Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21), DensityAt);
        rig.ThrusterMetresPerSecond2 = Arsenal.PostBoostBus.TrimAccelerationMetresPerSecond2;
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
