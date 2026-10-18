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
                               bool slotsFollowTheJets = true)
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
        for (int k = 0; k < targets; k++)
        {
            rig.PlannedTargets.Add((GameStackShortRangeTests.South(rangeMetres - k * spacingMetres), 1));
        }
        rig.PlannedLead = 0;

        rig.Fly(GameStackShortRangeTests.South(rangeMetres));
        Out.WriteLine($"{rangeMetres / 1_000.0:F0} km, {targets} at {spacingMetres / 1_000.0:F0} km, sized {sized}, "
                      + $"budget {budget:F0}, jets {slotsFollowTheJets}: planned {rig.Plan.Stops} ({rig.Plan.Hold}), "
                      + $"flew {rig.Stops.Count}, curtailed {rig.Curtailed}");
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
}
