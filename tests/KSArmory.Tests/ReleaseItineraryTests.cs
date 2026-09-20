using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The release schedule for a bus with several targets: when each one gets its warheads, in what
/// order, and whether the bus can pay for the hops.
///
/// <para>Data only — nothing here flies a bus anywhere, and the one thing it must prove is that a
/// set of <em>one</em> is bit for bit today's release, because every accuracy measurement this mod
/// has is taken against that shot.</para>
/// </summary>
public class ReleaseItineraryTests(ITestOutputHelper Out)
{
    /// <summary>Read off the config rather than typed, so a change to the gate moves the tests with it.</summary>
    private static double Gate => new IcbmConfig().ReleaseBeforeArrivalSeconds;

    /// <summary>The 6,179 km coast above the deploy floor, which is the geometry six targets fit in.</summary>
    private static ReleaseItinerary.Bus SixThousand
        => new(Gate, CoastSeconds: 1315.0, Warheads: 6);

    /// <summary>The 2,000 km one, whose whole coast is barely longer than the gate.</summary>
    private static ReleaseItinerary.Bus TwoThousand
        => new(Gate, CoastSeconds: 351.0, Warheads: 6);

    private static ReleaseItinerary.Target[] Reaching(params double[] reachMetres)
    {
        ReleaseItinerary.Target[] set = new ReleaseItinerary.Target[reachMetres.Length];
        for (int i = 0; i < set.Length; i++) set[i] = new ReleaseItinerary.Target(1, reachMetres[i]);

        return set;
    }

    // ---------------------------------------------------------------- a set of one

    /// <summary>
    /// The case every flight so far is. One target releases at the gate and spends what a
    /// single-target flight already spends, so nothing about the schedule reaches it.
    /// </summary>
    [Fact]
    public void ASetOfOneIsExactlyTodaysRelease()
    {
        ReleaseItinerary one = ReleaseItinerary.AtTheTrimCeiling(1, SixThousand);

        Assert.Equal(1, one.Count);
        Assert.Equal(Gate, one.Stops[0].BeforeArrivalSeconds);
        Assert.Equal(Gate, one.FirstBeforeArrivalSeconds);
        Assert.Equal(Gate, one.LastBeforeArrivalSeconds);

        // No hop, and the spend is the single-target flight's own.
        Assert.Equal(0.0, one.Stops[0].HopMetresPerSecond);
        Assert.Equal(ReleaseItinerary.SingleTargetMetresPerSecond, one.NeedsMetresPerSecond, 6);
        Assert.Equal(1, one.Fits);
    }

    /// <summary>
    /// And it fits whatever the budget says. The baseline is what the shot already spends rather
    /// than something the schedule asks for, so a budget under it must not refuse the one release
    /// that flies today.
    /// </summary>
    [Fact]
    public void ASetOfOneFitsABudgetThatCouldNotPayForIt()
    {
        ReleaseItinerary.Bus broke = SixThousand with { BudgetMetresPerSecond = 0.0 };

        Assert.Equal(1, ReleaseItinerary.AtTheTrimCeiling(1, broke).Fits);
    }

    /// <summary>
    /// A short coast does not refuse it either: the gate is a ceiling on the wait, not a
    /// precondition, and a coast shorter than it releases as soon as the altitude allows.
    /// </summary>
    [Fact]
    public void ASetOfOneFitsACoastShorterThanTheGate()
    {
        ReleaseItinerary.Bus close = SixThousand with { CoastSeconds = Gate - 100.0 };

        Assert.Equal(1, ReleaseItinerary.AtTheTrimCeiling(1, close).CoastFits);
    }

    // ---------------------------------------------------------------- the schedule

    /// <summary>
    /// The rule: start early enough that the <em>last</em> release still happens at today's gate.
    /// That is what makes the one-target case above unchanged, so it is asserted for every size.
    /// </summary>
    [Fact]
    public void TheLastReleaseIsAlwaysTodaysGate()
    {
        for (int n = 1; n <= 6; n++)
        {
            ReleaseItinerary plan = ReleaseItinerary.AtTheTrimCeiling(n, SixThousand);

            Assert.Equal(n, plan.Count);
            Assert.Equal(Gate, plan.LastBeforeArrivalSeconds, 6);
            Assert.Equal(Gate + ((n - 1) * ReleaseItinerary.MedianHopSeconds),
                         plan.FirstBeforeArrivalSeconds, 6);

            for (int k = 1; k < n; k++)
            {
                Assert.Equal(ReleaseItinerary.MedianHopSeconds,
                             plan.Stops[k - 1].BeforeArrivalSeconds - plan.Stops[k].BeforeArrivalSeconds,
                             6);
            }
        }
    }

    /// <summary>
    /// Six targets at 6,179 km, as the panel would read it.
    ///
    /// <para>Measurement, not an assertion: the schedule this settles on is 745 s of coast for six,
    /// which is comfortably inside that shot's 1,315 and nowhere near the 351 a 2,000 km shot
    /// has.</para>
    /// </summary>
    [Fact]
    public void WhatTheItineraryLooksLike()
    {
        ReleaseItinerary plan = ReleaseItinerary.AtTheTrimCeiling(6, SixThousand);

        Out.WriteLine(plan.Describe());

        foreach (ReleaseItinerary.Stop stop in plan.Stops)
        {
            Out.WriteLine($"   target {stop.Target + 1}: released {stop.BeforeArrivalSeconds,6:F1} s "
                          + $"before arrival, hop {stop.HopMetresPerSecond,5:F1} m/s, "
                          + $"{stop.SpentMetresPerSecond,5:F1} m/s spent"
                          + (stop.Affordable ? "" : "  -- beyond the budget"));
        }
    }

    // ---------------------------------------------------------------- the order

    /// <summary>
    /// Farthest first. The set is flown in order of the reach it demands, because the earliest slot
    /// is the one with the most of it.
    /// </summary>
    [Fact]
    public void TheFarthestTargetGetsTheEarliestSlot()
    {
        ReleaseItinerary plan = ReleaseItinerary.Plan(Reaching(12_000.0, 90_000.0, 40_000.0),
                                                      SixThousand);

        Assert.Equal([1, 2, 0], plan.Stops.Select(s => s.Target).ToArray());

        // And the earliest slot is the earliest release, which is the whole point of the ordering.
        Assert.True(plan.Stops[0].BeforeArrivalSeconds > plan.Stops[^1].BeforeArrivalSeconds);
    }

    /// <summary>Two targets the same distance out keep the order the player chose.</summary>
    [Fact]
    public void TargetsWithTheSameReachKeepTheOrderTheyWereChosenIn()
    {
        ReleaseItinerary plan = ReleaseItinerary.Plan(Reaching(40_000.0, 40_000.0, 40_000.0),
                                                      SixThousand);

        Assert.Equal([0, 1, 2], plan.Stops.Select(s => s.Target).ToArray());
    }

    /// <summary>
    /// <b>Why</b> farthest first, priced against the decay that motivates it: the along-track
    /// sensitivity at 6,179 km runs 2,755 m per m/s at cutoff down to 688 at the gate
    /// (<see cref="MirvDivertTests"/>), so a slot's leverage is what the order is spending.
    ///
    /// <para>The endpoints are flown and the slots between them interpolated, so what this pins is
    /// the <em>rule</em> — the dearest reach belongs where the leverage is — rather than a
    /// prediction of any one shot.</para>
    /// </summary>
    [Fact]
    public void TheDearestReachGoesWhereTheLeverageIs()
    {
        double[] reach = [12_000.0, 90_000.0, 40_000.0, 65_000.0];

        // Metres of ground per m/s at each slot, falling as the bus descends.
        double[] leverage = new double[reach.Length];
        for (int k = 0; k < leverage.Length; k++)
        {
            leverage[k] = 2755.0 + ((688.0 - 2755.0) * k / (leverage.Length - 1));
        }

        ReleaseItinerary plan = ReleaseItinerary.Plan(Reaching(reach), SixThousand);

        double Cost(IEnumerable<int> order)
        {
            double total = 0.0;
            int k = 0;
            foreach (int target in order) total += reach[target] / leverage[k++];

            return total;
        }

        int[] flown = [.. plan.Stops.Select(s => s.Target)];
        double farthestFirst = Cost(flown);
        double nearestFirst = Cost(flown.Reverse());

        Out.WriteLine($"farthest first {farthestFirst:F1} m/s against {nearestFirst:F1} m/s reversed");

        Assert.True(farthestFirst < nearestFirst,
                    $"farthest first costs {farthestFirst:F1} m/s against {nearestFirst:F1} reversed");
    }

    // ---------------------------------------------------------------- the budget

    /// <summary>
    /// Six targets do not fit today, and the type says so rather than flying five and calling it
    /// six: five hops at <see cref="BusTrim.MaxMetresPerSecond"/> on top of the single-target
    /// flight's own spend is 66.1 m/s against a 60 m/s cap.
    /// </summary>
    [Fact]
    public void SixTargetsDoNotFitTodaysBudget()
    {
        ReleaseItinerary plan = ReleaseItinerary.AtTheTrimCeiling(6, SixThousand);

        double needs = ReleaseItinerary.SingleTargetMetresPerSecond + (5.0 * BusTrim.MaxMetresPerSecond);

        Assert.Equal(needs, plan.NeedsMetresPerSecond, 6);
        Assert.True(needs > PostBoostAim.MaxTrimMetresPerSecond);

        // Five do, with the fifth arriving on 56.1 m/s of the 60.
        Assert.Equal(5, plan.BudgetFits);
        Assert.Equal(6, plan.CoastFits);
        Assert.Equal(5, plan.Fits);
        Assert.Equal(ReleaseItinerary.SingleTargetMetresPerSecond + (4.0 * BusTrim.MaxMetresPerSecond),
                     plan.FitsWithinMetresPerSecond, 6);

        Assert.Contains("only 5 fit the budget", plan.Describe());
        Out.WriteLine(plan.Describe());
    }

    /// <summary>
    /// And what raising the cap to fit six would cost, which is why this reports rather than
    /// assuming: a 66.1 m/s correction on the smallest bus the rack is flown on leaves 3.9 m/s,
    /// under the <see cref="BusTrim.MaxMetresPerSecond"/> one separation null costs.
    /// </summary>
    /// <remarks>
    /// <c>PostBoostAimTests.TheBudgetLeavesEnoughToNullASeparation</c> is the rule this would break,
    /// and the shipped bus's own tank is 72 m/s across both lateral axes — so six targets are a
    /// decision about the reserve and about which bus, not a constant to raise.
    /// </remarks>
    [Fact]
    public void RaisingTheBudgetForSixTargetsWouldSpendTheSeparationNull()
    {
        const double smallestTankMetresPerSecond = 70.0;

        double needs = ReleaseItinerary.AtTheTrimCeiling(6, SixThousand).NeedsMetresPerSecond;

        Assert.True(smallestTankMetresPerSecond - needs < BusTrim.MaxMetresPerSecond,
                    $"six targets at {needs:F1} m/s would still leave a "
                    + $"{BusTrim.MaxMetresPerSecond:F0} m/s null on a "
                    + $"{smallestTankMetresPerSecond:F0} m/s bus, so nothing here is a trade");

        Out.WriteLine($"six targets need {needs:F1} m/s against a cap of "
                      + $"{PostBoostAim.MaxTrimMetresPerSecond:F0}, and leave "
                      + $"{smallestTankMetresPerSecond - needs:F1} m/s of the smallest tank against the "
                      + $"{BusTrim.MaxMetresPerSecond:F0} a separation null costs");
    }

    /// <summary>
    /// What a hop costs is how far the landing has to move over what a metre a second is worth
    /// there — <b>not</b> <see cref="BusTrim.MaxMetresPerSecond"/>, which is a ceiling on one solve.
    /// </summary>
    /// <remarks>
    /// The reach here is the pinned along-track figure at 6,179 km, 355 m per m/s at cutoff falling
    /// to 178 at the gate. Six targets 10 km apart then cost a fraction of what charging the ceiling
    /// per hop reads, which is the difference between a feature and a refusal.
    /// </remarks>
    [Fact]
    public void AHopCostsWhatTheLandingMovesNotWhatOnePassMaySpend()
    {
        double[] reach = [688.0, 636.0, 582.0, 526.0, 469.0, 410.0];

        ReleaseItinerary priced = ReleaseItinerary.Chain(6, 4_000.0, reach, SixThousand);
        ReleaseItinerary ceiling = ReleaseItinerary.AtTheTrimCeiling(6, SixThousand);

        Out.WriteLine($"priced  {priced.Describe()}");
        Out.WriteLine($"ceiling {ceiling.Describe()}");

        Assert.Equal(6, priced.Fits);
        Assert.Equal(5, ceiling.BudgetFits);
        Assert.True(priced.NeedsMetresPerSecond < ceiling.NeedsMetresPerSecond);

        // Every hop is the same 4 km, so its price is only the reach at the slot it is bought in.
        for (int k = 1; k < priced.Count; k++)
        {
            Assert.Equal(4_000.0 / reach[k], priced.Stops[k].HopMetresPerSecond, 9);
        }
    }

    /// <summary>The two ways of asking the trade are one sum, so they have to agree at the boundary.</summary>
    [Fact]
    public void TheWidestSpacingIsExactlyWhereTheCountDrops()
    {
        double[] reach = [688.0, 636.0, 582.0, 526.0, 469.0, 410.0];

        double widest = ReleaseItinerary.SpacingWithin(6, reach, SixThousand);

        Assert.Equal(6, ReleaseItinerary.TargetsWithin(widest, reach, SixThousand));
        Assert.Equal(5, ReleaseItinerary.TargetsWithin(widest * 1.01, reach, SixThousand));

        // And the chain built at that spacing is the one that just fits.
        ReleaseItinerary plan = ReleaseItinerary.Chain(6, widest, reach, SixThousand);

        Assert.Equal(6, plan.BudgetFits);
        Assert.Equal(PostBoostAim.MaxTrimMetresPerSecond, plan.NeedsMetresPerSecond, 6);

        Out.WriteLine($"widest neighbour spacing {widest / 1000.0:F1} km, "
                      + $"a chain {5.0 * widest / 1000.0:F1} km end to end");
    }

    /// <summary>A set of one pays no hop, so no spacing can price it out.</summary>
    [Fact]
    public void OneTargetHasNoSpacingToAfford()
    {
        Assert.True(double.IsPositiveInfinity(ReleaseItinerary.SpacingWithin(1, [688.0], SixThousand)));
        Assert.Equal(6, ReleaseItinerary.TargetsWithin(0.0, [688.0], SixThousand));
    }

    /// <summary>
    /// What the budget still buys, which is the other half of the question a player asks — and it is
    /// a floor, because adding a target moves every release earlier where the reach is greater.
    /// </summary>
    [Fact]
    public void WhatIsLeftIsReportedAsGroundRatherThanAsVelocity()
    {
        double[] reach = [688.0, 636.0, 582.0];

        ReleaseItinerary plan = ReleaseItinerary.Chain(3, 10_000.0, reach, SixThousand);

        double hops = (10_000.0 / reach[1]) + (10_000.0 / reach[2]);

        Assert.Equal(ReleaseItinerary.SingleTargetMetresPerSecond + hops, plan.NeedsMetresPerSecond, 9);
        Assert.Equal(PostBoostAim.MaxTrimMetresPerSecond - plan.NeedsMetresPerSecond,
                     plan.LeftMetresPerSecond, 9);
        Assert.Equal(plan.LeftMetresPerSecond * reach[^1], plan.LeftBuysMetres, 6);

        Out.WriteLine($"{plan.Describe()}; {plan.LeftMetresPerSecond:F1} m/s left, "
                      + $"which still moves a landing {plan.LeftBuysMetres / 1000.0:F0} km");
    }

    /// <summary>An unpriced set falls back to the ceiling, which bounds it rather than estimating it.</summary>
    [Fact]
    public void AnUnpricedHopIsBoundedByTheCeilingRatherThanFree()
    {
        ReleaseItinerary plan = ReleaseItinerary.Chain(3, 10_000.0, null, SixThousand);

        Assert.Equal(BusTrim.MaxMetresPerSecond, plan.Stops[1].HopMetresPerSecond);
        Assert.Equal(ReleaseItinerary.SingleTargetMetresPerSecond + (2.0 * BusTrim.MaxMetresPerSecond),
                     plan.NeedsMetresPerSecond, 6);
    }

    /// <summary>A reach list shorter than the set reuses its last and dearest entry, never its first.</summary>
    [Fact]
    public void AShortReachListOverstatesRatherThanFlatters()
    {
        double[] two = [688.0, 410.0];

        ReleaseItinerary plan = ReleaseItinerary.Chain(4, 10_000.0, two, SixThousand);

        for (int k = 1; k < plan.Count; k++)
        {
            Assert.Equal(10_000.0 / 410.0, plan.Stops[k].HopMetresPerSecond, 9);
        }
    }

    /// <summary>A target that names its own hop is charged that, and the first stop is charged nothing.</summary>
    [Fact]
    public void OnlyTheStopsAfterTheFirstPayAHop()
    {
        ReleaseItinerary.Target[] set =
        [
            new(1, 90_000.0, HopMetresPerSecond: 8.0),
            new(1, 40_000.0, HopMetresPerSecond: 3.0),
        ];

        ReleaseItinerary plan = ReleaseItinerary.Plan(set, SixThousand);

        Assert.Equal(0.0, plan.Stops[0].HopMetresPerSecond);
        Assert.Equal(3.0, plan.Stops[1].HopMetresPerSecond);
        Assert.Equal(ReleaseItinerary.SingleTargetMetresPerSecond + 3.0, plan.NeedsMetresPerSecond, 6);
    }

    // ---------------------------------------------------------------- the clock

    /// <summary>
    /// A hop interval that would put the first release before cutoff is said, not clipped. At
    /// 2,000 km the whole usable coast is 351 s against a 420 s gate, so the itinerary starts
    /// before the bus exists and only one target fits.
    /// </summary>
    [Fact]
    public void AHopIntervalCanPushTheFirstReleaseBeforeCutoff()
    {
        ReleaseItinerary plan = ReleaseItinerary.AtTheTrimCeiling(3, TwoThousand);

        Assert.True(plan.StartsBeforeCutoff);
        Assert.Equal(3, plan.Count);
        Assert.Equal(1, plan.CoastFits);
        Assert.Equal(1, plan.Fits);
        Assert.Contains("only 1 fit the coast", plan.Describe());

        Out.WriteLine(plan.Describe());
    }

    /// <summary>
    /// And the times reported are the asked-for set's, not the set that fits — because dropping a
    /// target moves every release later, so a trimmed itinerary is a different schedule rather than
    /// a prefix of this one, and the caller has to re-plan.
    /// </summary>
    [Fact]
    public void TrimmingTheSetIsAReplanRatherThanAPrefix()
    {
        ReleaseItinerary asked = ReleaseItinerary.AtTheTrimCeiling(6, SixThousand);
        ReleaseItinerary trimmed = ReleaseItinerary.AtTheTrimCeiling(asked.Fits, SixThousand);

        Assert.Equal(5, trimmed.Count);
        Assert.NotEqual(asked.Stops[0].BeforeArrivalSeconds, trimmed.Stops[0].BeforeArrivalSeconds);
        Assert.Equal(ReleaseItinerary.MedianHopSeconds,
                     asked.Stops[0].BeforeArrivalSeconds - trimmed.Stops[0].BeforeArrivalSeconds, 6);

        // What does not move is the end of it, which is what keeps the last release on the gate.
        Assert.Equal(asked.LastBeforeArrivalSeconds, trimmed.LastBeforeArrivalSeconds, 6);
    }

    // ---------------------------------------------------------------- the empty cases

    [Fact]
    public void AnEmptySetIsNoItinerary()
    {
        foreach (ReleaseItinerary plan in new[]
                 {
                     ReleaseItinerary.AtTheTrimCeiling(0, SixThousand),
                     ReleaseItinerary.Plan(null, SixThousand),
                     ReleaseItinerary.Plan([], SixThousand),
                 })
        {
            Assert.Equal(0, plan.Count);
            Assert.Equal(0, plan.Fits);
            Assert.Equal(0.0, plan.NeedsMetresPerSecond);
            Assert.False(plan.StartsBeforeCutoff);
            Assert.Equal("no targets", plan.Describe());
        }
    }

    /// <summary>A place nothing is released at is not a stop: the hop would be spent and nothing would land.</summary>
    [Fact]
    public void ATargetWithNoWarheadIsNotAStop()
    {
        ReleaseItinerary.Target[] set = [new(2, 90_000.0), new(0, 40_000.0), new(1, 12_000.0)];

        ReleaseItinerary plan = ReleaseItinerary.Plan(set, SixThousand);

        Assert.Equal([0, 2], plan.Stops.Select(s => s.Target).ToArray());
        Assert.Equal(0, plan.WithoutWarheads);
    }

    /// <summary>
    /// More targets than warheads: the ones the magazine does not reach are counted and said,
    /// rather than dropped or flown to empty. They are the last ones flown, which by the ordering
    /// are the ones asking the least reach.
    /// </summary>
    [Fact]
    public void MoreTargetsThanWarheadsAreSaidRatherThanDropped()
    {
        ReleaseItinerary plan = ReleaseItinerary.AtTheTrimCeiling(8, SixThousand);

        Assert.Equal(6, plan.Count);
        Assert.Equal(2, plan.WithoutWarheads);
        Assert.Contains("2 targets get no warhead", plan.Describe());
    }

    /// <summary>A target asking for more than is left gets what is left, and is still a stop.</summary>
    [Fact]
    public void ATargetGetsWhatTheMagazineHasLeft()
    {
        ReleaseItinerary.Target[] set = [new(5, 90_000.0), new(4, 40_000.0), new(1, 12_000.0)];

        ReleaseItinerary plan = ReleaseItinerary.Plan(set, SixThousand);

        Assert.Equal([5, 1], plan.Stops.Select(s => s.Warheads).ToArray());
        Assert.Equal(1, plan.WithoutWarheads);
    }
}
