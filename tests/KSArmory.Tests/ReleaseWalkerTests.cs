using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The cursor the flight actuates through, and the three numbers it hands back.
///
/// <para><b>The property the whole phase rests on is that a set of one changes nothing</b>, and it
/// is asserted here on the numbers the flight reads rather than on the plan: a walker holding a
/// one-stop plan gives the gate as NaN — so <c>IcbmProgram</c> reads the setting live — the
/// magazine untouched, and the lead as the target of every round. That is the whole of
/// what <c>Ksa/IcbmComputer.cs</c> asks it, so nothing in the loop can execute without
/// <see cref="ReleaseWalker.Walking"/>, which is false for every one-stop set.</para>
/// </summary>
public class ReleaseWalkerTests(ITestOutputHelper Out)
{
    private static double Gate => new IcbmConfig().ReleaseBeforeArrivalSeconds;

    private static ReleaseItinerary.Bus SixThousand
        => new(Gate, CoastSeconds: 1315.0, Warheads: 6);

    private static double[] Reach => [410.0];

    // A chain the bus is aimed at one END of, walked inward.
    private static ReleaseItinerary.Target[] Outward(int targets, double spacingMetres,
                                                     int warheadsEach = 1)
    {
        ReleaseItinerary.Target[] set = new ReleaseItinerary.Target[targets];

        for (int i = 0; i < targets; i++)
        {
            set[i] = new ReleaseItinerary.Target(warheadsEach, (targets - i) * spacingMetres,
                                                 i == 0 ? 0.0 : spacingMetres);
        }

        return set;
    }

    private static ReleaseWalker Walking(int targets, double spacingMetres = 4000.0,
                                         int warheadsEach = 1)
    {
        ReleaseWalker walker = new();
        walker.Plan(ReleaseLoop.Plan(Outward(targets, spacingMetres, warheadsEach), SixThousand,
                                     Reach, lead: 0));
        return walker;
    }

    // ---------------------------------------------------------- a set of one changes nothing

    /// <summary>
    /// <b>The one that matters.</b> Whatever the budget, the coast, the magazine or the spacing, a
    /// walker given a one-target set is not walking — so the gate stays the setting, the sequencer
    /// gets the whole magazine, and every round is recorded against the lead.
    /// </summary>
    [Fact]
    public void ASetOfOneLeavesEveryNumberTheFlightReadsUnchanged()
    {
        foreach (int warheads in new[] { 1, 2, 6 })
        foreach (double coast in new[] { 0.0, 351.0, 1315.0, double.PositiveInfinity })
        foreach (double budget in new[] { 0.0, 16.1, 60.0, double.PositiveInfinity })
        {
            ReleaseItinerary.Bus bus = SixThousand with
            {
                CoastSeconds = coast,
                BudgetMetresPerSecond = budget,
            };

            ReleaseWalker walker = new();
            walker.Plan(ReleaseLoop.Plan(Outward(1, 4000.0, warheads), bus, Reach, lead: 0));

            Assert.False(walker.Walking);
            Assert.False(walker.Done);
            Assert.True(double.IsNaN(walker.GateOverrideSeconds(Gate)));
            Assert.Equal(6, walker.TubesLeft(6));
            Assert.Equal(3, walker.TargetIndex(3));

            // And it stays that way once warheads start leaving, which is when a walk would
            // otherwise begin handing over.
            for (int n = 0; n < 6; n++) walker.WarheadAway();

            Assert.False(walker.Walking);
            Assert.False(walker.Done);
            Assert.True(double.IsNaN(walker.GateOverrideSeconds(Gate)));
            Assert.Equal(6, walker.TubesLeft(6));
            Assert.Equal(3, walker.TargetIndex(3));
        }
    }

    /// <summary>
    /// <b>The mirror, and the one nothing had.</b> The same three numbers on a set of <em>two</em>:
    /// the gate opens a hop early, the sequencer is bounded to the stop's quota rather than handed
    /// the magazine, and the target advances at the handover.
    /// </summary>
    /// <remarks>
    /// All three took their not-walking branch on one flight — the gate read 422 s against a
    /// setting of 420, six warheads left consecutively, and every round was recorded against the
    /// lead. One cause, and this is the shape that names it: they are three readings of
    /// <see cref="ReleaseWalker.Walking"/>.
    /// </remarks>
    [Fact]
    public void ASetOfTwoDrivesEveryNumberTheFlightReads()
    {
        // Every coast here is at least `gate + hop`, and every budget covers the single-target
        // flight's own 16.1 m/s plus a 4 km hop, so each of these is a set that genuinely walks.
        foreach (int warheads in new[] { 2, 4, 6 })
        foreach (double coast in new[] { Gate + ReleaseItinerary.MedianHopSeconds, 1315.0, 1772.0 })
        foreach (double budget in new[] { 60.0, 1000.0, double.PositiveInfinity })
        {
            ReleaseItinerary.Bus bus = SixThousand with
            {
                CoastSeconds = coast,
                BudgetMetresPerSecond = budget,
                Warheads = warheads,
            };

            int each = warheads / 2;

            ReleaseWalker walker = new();
            walker.Plan(ReleaseLoop.Plan(Outward(2, 4000.0, each), bus, Reach, lead: 0));

            string what = $"{warheads} warheads, coast {coast:F1}, budget {budget:F0}";

            Assert.True(walker.Walking, what);
            Assert.Equal(2, walker.Walk.Stops);

            // The gate, and it is finite: NaN is what a flight with no walk reads, and it released
            // on the setting.
            double gate = walker.GateOverrideSeconds(Gate);
            Assert.True(double.IsFinite(gate), what);
            Assert.Equal(Gate + ReleaseItinerary.MedianHopSeconds, gate, 6);

            // The quota, and it is LESS than the magazine: handed the magazine, one stop empties it.
            Assert.Equal(each, walker.TubesLeft(warheads));
            Assert.True(walker.TubesLeft(warheads) < warheads, what);

            // The target, against a lead nothing should fall back to.
            Assert.Equal(0, walker.TargetIndex(99));

            for (int n = 0; n < each; n++) walker.WarheadAway();

            Assert.True(walker.Step.Handover, what);
            walker.Advance();

            Assert.Equal(1, walker.TargetIndex(99));
            Assert.Equal(each, walker.TubesLeft(each));
            Assert.True(double.IsFinite(walker.GateOverrideSeconds(Gate)), what);
        }
    }

    /// <summary>A walk of one releases at exactly today's gate, which is what the program reads.</summary>
    [Fact]
    public void AWalkOfSeveralOpensTheGateEarlyAndAWalkOfOneDoesNot()
    {
        Assert.True(double.IsNaN(Walking(1).GateOverrideSeconds(Gate)));

        // Against the stops actually planned rather than the targets asked for: six 4 km apart on
        // a 410 m per m/s reach is 64.9 m/s of a 60 m/s budget, so the set is cut to five before it
        // is flown.
        for (int n = 2; n <= 6; n++)
        {
            ReleaseWalker walker = Walking(n);
            double open = walker.GateOverrideSeconds(Gate);

            Assert.Equal(Gate + ((walker.Walk.Stops - 1) * ReleaseItinerary.MedianHopSeconds),
                         open, 6);

            Out.WriteLine($"{n} targets -> {walker.Walk.Stops} stops, first release {open:F0} s "
                          + "before arrival");
        }

        Assert.Equal(5, Walking(6).Walk.Stops);
    }

    /// <summary>
    /// A walker that has never been planned is not walking, which is what a flight holds from the
    /// frame it is designated until the reach has been priced.
    /// </summary>
    /// <remarks>
    /// <see cref="ReleaseWalkHold.Walking"/> is the enum's zero, so a default
    /// <see cref="ReleaseWalk"/> claims the hold that means "it is walking one" — and a walker that
    /// believed it would hand over at once, finish with no stops flown, and report the salvo over
    /// before a single warhead left.
    /// </remarks>
    [Fact]
    public void AWalkerWithNoPlanIsNotWalking()
    {
        ReleaseWalker walker = new();

        Assert.False(walker.Walking);
        Assert.False(walker.Done);
        Assert.False(default(ReleaseWalk).Walks);
        Assert.Equal("no walk planned", default(ReleaseWalk).Say());
        Assert.True(double.IsNaN(walker.GateOverrideSeconds(Gate)));
        Assert.Equal(6, walker.TubesLeft(6));
        Assert.Equal(2, walker.TargetIndex(2));
    }

    // ---------------------------------------------------------- the walk itself

    /// <summary>Each stop gets its own quota, and the walk hands over exactly when it is met.</summary>
    [Fact]
    public void AStopEndsOnItsQuotaAndTheNextOneStarts()
    {
        ReleaseWalker walker = Walking(3, warheadsEach: 2);

        Assert.True(walker.Walking);
        Assert.Equal(3, walker.Walk.Stops);
        Assert.Equal(0, walker.Step.Target);
        Assert.Equal(2, walker.TubesLeft(6));

        walker.WarheadAway();
        Assert.Equal(1, walker.TubesLeft(6));
        Assert.True(walker.Step.ReleaseHere);

        walker.WarheadAway();
        Assert.Equal(0, walker.TubesLeft(6));
        Assert.False(walker.Step.ReleaseHere);
        Assert.True(walker.Step.Handover);
        Assert.Equal(1, walker.Step.NextTarget);

        walker.Advance();
        Assert.Equal(1, walker.Stop);
        Assert.Equal(0, walker.AwayThisStop);
        Assert.Equal(1, walker.Step.Target);
        Assert.Equal(2, walker.TubesLeft(6));
    }

    /// <summary>The last stop hands over to nobody, and finishing is what ends the trim.</summary>
    [Fact]
    public void TheLastStopFinishesTheWalkRatherThanHandingOver()
    {
        ReleaseWalker walker = Walking(2);

        walker.WarheadAway();
        walker.Advance();
        walker.WarheadAway();

        Assert.False(walker.Step.Handover);
        Assert.True(walker.Step.Finished);
        Assert.False(walker.Done);

        walker.Finish();

        Assert.True(walker.Done);
        Assert.False(walker.Walking);

        // A rack that has reloaded cannot send warheads the plan never assigned anywhere.
        Assert.Equal(0, walker.TubesLeft(6));
    }

    /// <summary>
    /// A plan is committed by the first warhead leaving, so a set edited behind a bus that has
    /// already been somewhere cannot re-order the stops under it.
    /// </summary>
    [Fact]
    public void APlanIsRefusedOnceTheFirstWarheadHasGone()
    {
        ReleaseWalker walker = Walking(3);
        Assert.Equal(3, walker.Walk.Stops);

        Assert.True(walker.Plan(ReleaseLoop.Plan(Outward(2, 4000.0), SixThousand, Reach, lead: 0)));
        Assert.Equal(2, walker.Walk.Stops);

        walker.WarheadAway();

        Assert.False(walker.Plan(ReleaseLoop.Plan(Outward(6, 4000.0), SixThousand, Reach, lead: 0)));
        Assert.Equal(2, walker.Walk.Stops);
    }

    // ---------------------------------------------------------- the lines a night is scored off

    /// <summary>
    /// <b>The scoring contract.</b> The target index is zero-based, as
    /// <see cref="TargetSet.Entries"/> indexes, and the stop is one-based.
    /// </summary>
    [Fact]
    public void TheReleaseLineCarriesTheTargetIndexAndTheCount()
    {
        string one = ReleaseWalker.SayRelease(1, 3, "GeoSat FAT_1", 0, "24.0S 62.0W", 2, 16.12,
                                              43.88);

        Assert.Equal("released 1 of 3 on GeoSat FAT_1: target 0 24.0S 62.0W, 2 warheads, "
                     + "divert 16.12 m/s, 43.9 m/s left", one);

        string last = ReleaseWalker.SayRelease(3, 3, "GeoSat FAT_1", 2, "24.1S 62.0W", 1, 3.40,
                                               37.10);

        Assert.Equal("released 3 of 3 on GeoSat FAT_1: target 2 24.1S 62.0W, 1 warhead, "
                     + "divert 3.40 m/s, 37.1 m/s left", last);

        Out.WriteLine(one);
        Out.WriteLine(last);
    }

    /// <summary>And the summary names every stop, what went there and what the walk cost.</summary>
    [Fact]
    public void TheWalkSummaryNamesEveryStop()
    {
        string said = ReleaseWalker.SayWalk("GeoSat FAT_1",
                                            [(0, "24.0S 62.0W", 2), (2, "24.1S 62.0W", 3)],
                                            26.4, 60.0, 1);

        Assert.Equal("walk complete on GeoSat FAT_1: 2 stops -- target 0 24.0S 62.0W took 2; "
                     + "target 2 24.1S 62.0W took 3 -- 26.4 m/s of 60 spent, 1 warhead kept aboard",
                     said);

        Out.WriteLine(said);
    }

    /// <summary>A reset walker is a flight that never had a plan, which is what designating is.</summary>
    [Fact]
    public void ResettingForgetsThePlanAndTheCommit()
    {
        ReleaseWalker walker = Walking(3);
        walker.WarheadAway();
        walker.Reset();

        Assert.False(walker.Walking);
        Assert.Equal(0, walker.Walk.Stops);
        Assert.True(walker.Plan(ReleaseLoop.Plan(Outward(4, 4000.0), SixThousand, Reach, lead: 0)));
        Assert.Equal(4, walker.Walk.Stops);
    }
}
