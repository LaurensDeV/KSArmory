using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The two levers on what a walk may be sent to: <see cref="IcbmConfig.PriceEachHopAtItsSlot"/> and
/// <see cref="IcbmConfig.SpendTheSplitReserveAfterTheFirstStop"/>. Both off is the planner as it was.
/// </summary>
public class WalkPricingTests
{
    private const double Gate = 420.0;

    // A coasting bus whose columns read 955 m per m/s with 1,200 s to go, the coast's own figure at
    // 6,179 km (docs/MIRV-TARGETS.md). The coast is left unknown so only the budget and the hop bound.
    private const double MeasuredAt = 1_200.0;
    private const double MeasuredReach = 955.0;

    private static ReleaseItinerary.Bus Bus(bool perSlot = false, bool reserve = false)
        => new(Gate, double.NaN, 6,
               ReachMeasuredAtSeconds: perSlot ? MeasuredAt : double.NaN,
               ReserveAfterFirstStopMetresPerSecond: reserve ? ReleaseLoop.SplitNullReserveMetresPerSecond : 0.0);

    // A chain the bus is aimed at one end of, one warhead a stop, as ReleaseItinerary.Chain builds it.
    private static ReleaseItinerary.Target[] Chain(int targets, double spacingMetres)
    {
        ReleaseItinerary.Target[] set = new ReleaseItinerary.Target[targets];
        for (int i = 0; i < targets; i++) set[i] = new ReleaseItinerary.Target(1, (targets - 1 - i) * spacingMetres, spacingMetres);

        return set;
    }

    private static DivertFootprint MidCoast()
    {
        Assert.True(ArrivalFrame.TryAt(new double3(6_371_000.0, 0, 0), new double3(0, 7_000.0, -900.0),
                                       out ArrivalFrame frame));

        return new DivertFootprint(MeasuredReach, MeasuredReach, 0.0, DivertFootprint.ArrivalClock.Pinned, frame,
                                   MeasuredAt, FromTheRealState: true);
    }

    private static ReachDisplay Display(ReleaseItinerary.Bus bus, bool perSlot,
                                        params ReachDisplay.Placed[] placed)
        => ReachDisplay.For(MidCoast(), placed, bus, IcbmPhase.Coast, salvoAway: false, TargetSet.MaxTargets,
                            2_000.0, lead: 0, ReachHold.Unflown, perSlot);

    [Fact]
    public void PricedPerSlotTheLastHopCostsWhatTheGateReachBuys()
    {
        ReleaseItinerary asGiven = ReleaseItinerary.Plan(Chain(4, 3_000.0), Bus(), [MeasuredReach]);
        ReleaseItinerary perSlot = ReleaseItinerary.Plan(Chain(4, 3_000.0), Bus(perSlot: true), [MeasuredReach]);

        Assert.Equal(3_000.0 / MeasuredReach, asGiven.Stops[3].HopMetresPerSecond);
        Assert.Equal(3_000.0 / (MeasuredReach * Gate / MeasuredAt), perSlot.Stops[3].HopMetresPerSecond, 9);

        // 3.14 m/s a hop at the reach measured now; 6.85, 7.77 and 8.97 at their own slots.
        Assert.Equal(16.1 + 3 * 3_000.0 / MeasuredReach, asGiven.NeedsMetresPerSecond, 9);
        Assert.InRange(perSlot.NeedsMetresPerSecond, 39.5, 40.0);
        for (int k = 2; k < 4; k++)
        {
            Assert.True(perSlot.Stops[k].HopMetresPerSecond > perSlot.Stops[k - 1].HopMetresPerSecond);
        }
    }

    [Fact]
    public void ASpreadWalkedAtTheReachMeasuredNowIsRefusedAtItsOwnSlots()
    {
        ReleaseWalk asGiven = ReleaseLoop.Plan(Chain(4, 4_000.0), Bus(), [MeasuredReach], lead: 0);
        ReleaseWalk perSlot = ReleaseLoop.Plan(Chain(4, 4_000.0), Bus(perSlot: true), [MeasuredReach], lead: 0);

        Assert.True(asGiven.Walks);
        Assert.Equal(4_000.0 / MeasuredReach, asGiven.HopMetresPerSecond);

        // The last hop is bought 420 s out, where a metre a second moves the landing 334 m: 12.0 m/s,
        // more than one pass flies.
        Assert.Equal(ReleaseWalkHold.HopBeyondOnePass, perSlot.Hold);
        Assert.Equal(4_000.0 / (MeasuredReach * Gate / MeasuredAt), perSlot.HopMetresPerSecond, 9);
    }

    [Fact]
    public void PricedPerSlotTheRingIsDrawnAtTheSlotTheNextAddIsFlownIn()
    {
        ReachDisplay.Placed lead = new(0.0, 0.0, 3);

        ReachDisplay asGiven = Display(Bus(), perSlot: false, lead);
        ReachDisplay perSlot = Display(Bus(), perSlot: true, lead);

        Assert.Equal(MeasuredReach * BusTrim.MaxMetresPerSecond, asGiven.SemiMinorMetres);
        Assert.Equal(MeasuredReach * BusTrim.MaxMetresPerSecond * Gate / MeasuredAt, perSlot.SemiMinorMetres, 6);

        // 5 km across: inside 9.55 km at the reach measured now, outside 3.34 km at the gate's.
        Assert.Equal(ReachVerdict.Adds, asGiven.Verdict(TargetClick.Add, true, 0.0, 5_000.0));
        Assert.Equal(ReachVerdict.OutsideReach, perSlot.Verdict(TargetClick.Add, true, 0.0, 5_000.0));

        // And the planner agrees with the ring about that click.
        ReachDisplay.Placed there = new(0.0, 5_000.0, 3);
        Assert.True(Display(Bus(), perSlot: false, lead, there).Flown.Walks);
        Assert.Equal(ReleaseWalkHold.HopBeyondOnePass, Display(Bus(), perSlot: true, lead, there).Flown.Hold);
    }

    [Fact]
    public void ASlotEarlierThanTheReachWasMeasuredAtIsNotPromisedMore()
    {
        ReleaseItinerary.Bus bus = Bus(perSlot: true) with { ReachMeasuredAtSeconds = 300.0 };

        Assert.Equal(1.0, bus.SlotReachScale(Gate));
        Assert.Equal(0.5, bus.SlotReachScale(150.0));
    }

    [Fact]
    public void TheSplitReserveLetsASixStopWalkTheBudgetCut()
    {
        ReleaseWalk asSet = ReleaseLoop.Plan(Chain(6, 9_500.0), Bus(), [1_000.0], lead: 0);
        ReleaseWalk withReserve = ReleaseLoop.Plan(Chain(6, 9_500.0), Bus(reserve: true), [1_000.0], lead: 0);

        // 16.1 + 5 x 9.5 = 63.6 m/s: over the 60 the budget allows, under 70 with the reserve.
        Assert.Equal(5, asSet.Stops);
        Assert.Equal(1, asSet.Dropped);
        Assert.Equal(6, withReserve.Stops);
        Assert.Equal(0, withReserve.Dropped);
        Assert.Equal(63.6, withReserve.Itinerary.NeedsMetresPerSecond, 9);
        Assert.Equal(70.0, withReserve.Itinerary.Means.WalkBudgetMetresPerSecond);
    }

    [Fact]
    public void TheReserveIsNotTheFirstStopsToSpend()
    {
        ReleaseItinerary.Bus bus = Bus(reserve: true);

        Assert.Equal(PostBoostAim.MaxTrimMetresPerSecond, bus.BudgetAt(0));
        Assert.Equal(PostBoostAim.MaxTrimMetresPerSecond + ReleaseLoop.SplitNullReserveMetresPerSecond, bus.BudgetAt(1));
        Assert.Equal(0.0, ReleaseLoop.ReserveReleased(spendIt: true, firstStopAway: false));
        Assert.Equal(0.0, ReleaseLoop.ReserveReleased(spendIt: false, firstStopAway: true));
    }

    [Fact]
    public void TheReserveLiftsABudgetButNeverNoneOrUnlimited()
    {
        Assert.Equal(70.0, ReleaseLoop.BudgetWithReserve(60.0, 10.0));
        Assert.Equal(60.0, ReleaseLoop.BudgetWithReserve(60.0, 0.0));
        Assert.Equal(0.0, ReleaseLoop.BudgetWithReserve(0.0, 10.0));
        Assert.Equal(-1.0, ReleaseLoop.BudgetWithReserve(-1.0, 10.0));
    }

    [Fact]
    public void AHopTheBudgetRefusesIsFlownOnceTheReserveIsReleased()
    {
        double released = ReleaseLoop.BudgetWithReserve(60.0, ReleaseLoop.ReserveReleased(true, true));

        Assert.Equal(HopHold.BeyondTheBudget, ReleaseLoop.CanFlyTheHop(8.0, 0.2, double.NaN, 55.0, 60.0));
        Assert.Equal(HopHold.WillFly, ReleaseLoop.CanFlyTheHop(8.0, 0.2, double.NaN, 55.0, released));
    }

    [Fact]
    public void TheCorrectionRunsOnPastSixtyOnlyWhenTheBudgetSaysSo()
    {
        static PostBoostSituation Spent(double budget)
            => new(TrimSettled: false, ReleaseDirectionCci: new double3(1, 0, 0), PredictedMissMetres: 500.0,
                   AimHasSettled: false, TrimSpentMetresPerSecond: 65.0, BudgetMetresPerSecond: budget);

        Assert.True(new PostBoostAim().Update(0.02, Spent(PostBoostAim.MaxTrimMetresPerSecond)).MayRelease);
        Assert.False(new PostBoostAim().Update(0.02, Spent(70.0)).MayRelease);
    }

    [Fact]
    public void TheFirstStopIsAwayOnceItsWholeQuotaHasGone()
    {
        ReleaseItinerary.Target[] set =
        [
            new(3, 4_000.0, 0.0),
            new(3, 0.0, 4_000.0),
        ];

        ReleaseWalker walker = new();
        Assert.True(walker.Plan(ReleaseLoop.Plan(set, Bus(), [1_000.0], lead: 0)));

        Assert.False(walker.FirstStopAway);
        walker.WarheadAway();
        walker.WarheadAway();
        Assert.False(walker.FirstStopAway);
        walker.WarheadAway();
        Assert.True(walker.FirstStopAway);
        walker.Advance();
        Assert.True(walker.FirstStopAway);
    }

    [Fact]
    public void ASetOfOneNeverReleasesTheReserve()
    {
        ReleaseWalker walker = new();
        Assert.True(walker.Plan(ReleaseLoop.Plan([new ReleaseItinerary.Target(6, 0.0)], Bus(reserve: true),
                                                 [1_000.0], lead: 0)));

        for (int i = 0; i < 6; i++) walker.WarheadAway();

        Assert.False(walker.FirstStopAway);
    }

    /// <summary>
    /// Both off, every number the planner and the ring hand back is the old arithmetic exactly: the
    /// reach as given per slot, the hop its distance over that, the budget the one set.
    /// </summary>
    [Fact]
    public void BothOffThePlanIsTheReachAsGivenAndTheBudgetAsSet()
    {
        double[] reach = [955.0, 900.0, 850.0];
        ReleaseItinerary plan = ReleaseItinerary.Plan(Chain(6, 3_700.0), Bus(), reach);

        double spent = ReleaseItinerary.SingleTargetMetresPerSecond;

        for (int k = 0; k < plan.Count; k++)
        {
            double at = reach[Math.Min(k, reach.Length - 1)];
            double hop = k == 0 ? 0.0 : 3_700.0 / at;
            spent += hop;

            Assert.Equal(at, plan.Stops[k].ReachMetresPerMetrePerSecond);
            Assert.Equal(hop, plan.Stops[k].HopMetresPerSecond);
            Assert.Equal(spent, plan.Stops[k].SpentMetresPerSecond);
            Assert.Equal(k == 0 || spent <= PostBoostAim.MaxTrimMetresPerSecond + 1e-9, plan.Stops[k].Affordable);
        }

        Assert.Equal(Math.Max(0.0, PostBoostAim.MaxTrimMetresPerSecond - spent), plan.LeftMetresPerSecond);
        Assert.EndsWith($"{spent:F1} m/s of 60", plan.Describe());

        ReachDisplay ring = Display(Bus(), perSlot: false, new ReachDisplay.Placed(0.0, 0.0, 3));
        Assert.Equal(MeasuredReach * BusTrim.MaxMetresPerSecond, ring.SemiMinorMetres);
        Assert.Equal(PostBoostAim.MaxTrimMetresPerSecond - ReleaseItinerary.SingleTargetMetresPerSecond,
                     ring.LeftMetresPerSecond);
    }
}
