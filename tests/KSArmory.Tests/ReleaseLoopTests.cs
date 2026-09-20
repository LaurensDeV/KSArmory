using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The cursor round the release: which stop a bus is on, how many warheads leave there, and when it
/// hops to the next.
///
/// <para>Decision only — nothing here flies a bus. The one thing it has to prove is that a set of
/// <em>one</em> never produces a walk at all, because that is what makes a single-target flight the
/// shot every accuracy measurement on this mod was taken against.</para>
/// </summary>
public class ReleaseLoopTests(ITestOutputHelper Out)
{
    private static double Gate => new IcbmConfig().ReleaseBeforeArrivalSeconds;

    /// <summary>The 6,179 km coast above the deploy floor, which is the geometry six targets fit in.</summary>
    private static ReleaseItinerary.Bus SixThousand
        => new(Gate, CoastSeconds: 1315.0, Warheads: 6);

    /// <summary>The 2,000 km one, whose whole coast is barely longer than the gate.</summary>
    private static ReleaseItinerary.Bus TwoThousand
        => new(Gate, CoastSeconds: 351.0, Warheads: 6);

    /// <summary>What a metre a second of divert buys on the ground at 6,179 km, pinned.</summary>
    private static double[] Reach => [410.0];

    // A chain the bus is aimed at one END of, walked outward: entry 0 is where the booster went and
    // costs no hop, and each one after it is `spacingMetres` further along.
    private static ReleaseItinerary.Target[] Outward(int targets, double spacingMetres,
                                                     int warheadsEach = 1)
    {
        ReleaseItinerary.Target[] set = new ReleaseItinerary.Target[targets];

        // Descending reach, so Plan's own farthest-first sort keeps this order: the rank is what
        // that field orders by, and here the walk's own order is the one being stated.
        for (int i = 0; i < targets; i++)
        {
            set[i] = new ReleaseItinerary.Target(warheadsEach, (targets - i) * spacingMetres,
                                                 i == 0 ? 0.0 : spacingMetres);
        }

        return set;
    }

    // ---------------------------------------------------------------- a set of one

    /// <summary>
    /// <b>The property the whole phase rests on.</b> No set of one produces a walk, whatever the
    /// budget, the coast, the magazine or the spacing — so a single-target flight holds no walk and
    /// not one statement of the loop runs.
    /// </summary>
    [Fact]
    public void NoSetOfOneEverProducesAWalk()
    {
        foreach (int warheads in new[] { 1, 2, 6 })
        foreach (double coast in new[] { 0.0, 100.0, 351.0, 1315.0, 1772.0, double.PositiveInfinity })
        foreach (double budget in new[] { 0.0, 1.0, 16.1, 60.0, 1000.0, double.PositiveInfinity })
        foreach (double reach in new[] { 0.0, 410.0, 2860.0, double.NaN })
        foreach (int assigned in new[] { 1, 3, 6 })
        {
            ReleaseItinerary.Bus bus = new(Gate, coast, warheads,
                                           BudgetMetresPerSecond: budget);

            ReleaseWalk walk = ReleaseLoop.Plan([new ReleaseItinerary.Target(assigned, 0.0)], bus,
                                                double.IsNaN(reach) ? null : [reach], lead: 0);

            Assert.False(walk.Walks);
            Assert.Equal(ReleaseWalkHold.OneStop, walk.Hold);
        }
    }

    /// <summary>
    /// And its release is at today's gate to the last bit, which is what lets the flight read the
    /// schedule unconditionally.
    /// </summary>
    [Fact]
    public void ASetOfOneReleasesAtExactlyTodaysGate()
    {
        ReleaseWalk walk = ReleaseLoop.Plan([new ReleaseItinerary.Target(6, 0.0)], SixThousand,
                                            Reach, lead: 0);

        Assert.Equal(Gate, ReleaseLoop.FirstBeforeArrivalSeconds(walk, Gate));
        Assert.Equal(Gate, walk.Itinerary.LastBeforeArrivalSeconds);
    }

    /// <summary>A bus with nothing assigned anywhere has no stops and so no walk.</summary>
    [Fact]
    public void NothingAssignedIsNoWalk()
    {
        ReleaseWalk walk = ReleaseLoop.Plan(
            [new ReleaseItinerary.Target(0, 0.0), new ReleaseItinerary.Target(0, 4_000.0)],
            SixThousand, Reach, lead: 0);

        Assert.Equal(ReleaseWalkHold.NoStops, walk.Hold);
        Assert.Equal(0, walk.Stops);
    }

    // ---------------------------------------------------------------- the walk

    /// <summary>Three stops, walked in the itinerary's order, one warhead each.</summary>
    [Fact]
    public void AWalkVisitsEveryStopInOrder()
    {
        ReleaseWalk walk = ReleaseLoop.Plan(Outward(3, 4_000.0), SixThousand, Reach, lead: 0);

        Assert.True(walk.Walks);
        Assert.Equal(3, walk.Stops);

        List<int> visited = [];
        int stop = 0;
        int away = 0;

        for (int frame = 0; frame < 100; frame++)
        {
            ReleaseStep step = ReleaseLoop.Step(walk, stop, away);
            if (step.Finished) break;

            if (step.ReleaseHere)
            {
                visited.Add(step.Target);
                away++;
                continue;
            }

            Assert.True(step.Handover);
            stop++;
            away = 0;
        }

        Assert.Equal([0, 1, 2], visited);
        Out.WriteLine(walk.Say());
    }

    /// <summary>
    /// Warheads sharing a target go together, and the hop happens once they all have.
    /// </summary>
    [Fact]
    public void WarheadsSharingATargetGoBeforeTheHop()
    {
        ReleaseWalk walk = ReleaseLoop.Plan(Outward(2, 4_000.0, warheadsEach: 3), SixThousand,
                                            Reach, lead: 0);

        Assert.True(walk.Walks);

        Assert.True(ReleaseLoop.Step(walk, 0, 0).ReleaseHere);
        Assert.True(ReleaseLoop.Step(walk, 0, 2).ReleaseHere);

        ReleaseStep done = ReleaseLoop.Step(walk, 0, 3);
        Assert.False(done.ReleaseHere);
        Assert.True(done.Handover);
        Assert.Equal(1, done.NextTarget);

        Assert.True(ReleaseLoop.Step(walk, 1, 3).Finished);
    }

    /// <summary>The last stop hands over to nothing, because a hop there spends the tank on nobody.</summary>
    [Fact]
    public void TheLastStopHopsNowhere()
    {
        ReleaseWalk walk = ReleaseLoop.Plan(Outward(3, 4_000.0), SixThousand, Reach, lead: 0);

        ReleaseStep last = ReleaseLoop.Step(walk, 2, 1);

        Assert.False(last.Handover);
        Assert.True(last.Finished);
        Assert.Equal(-1, last.NextTarget);
    }

    /// <summary>
    /// The first release is earlier than the gate by a hop per stop after the first, and the last
    /// still lands on the gate.
    /// </summary>
    [Fact]
    public void AWalkStartsEarlyAndEndsOnTheGate()
    {
        ReleaseWalk walk = ReleaseLoop.Plan(Outward(4, 4_000.0), SixThousand, Reach, lead: 0);

        Assert.Equal(Gate + (3.0 * ReleaseItinerary.MedianHopSeconds),
                     ReleaseLoop.FirstBeforeArrivalSeconds(walk, Gate), 9);
        Assert.Equal(Gate, walk.Itinerary.LastBeforeArrivalSeconds, 9);
    }

    /// <summary>
    /// The sequencer counts down to empty, so a stop's quota is handed to it as the whole magazine —
    /// and never more than is loaded.
    /// </summary>
    [Fact]
    public void AStopIsGivenItsOwnQuotaRatherThanTheMagazine()
    {
        ReleaseWalk walk = ReleaseLoop.Plan(Outward(2, 4_000.0, warheadsEach: 3), SixThousand,
                                            Reach, lead: 0);

        Assert.Equal(3, ReleaseLoop.TubesLeftForStop(ReleaseLoop.Step(walk, 0, 0), loaded: 6));
        Assert.Equal(1, ReleaseLoop.TubesLeftForStop(ReleaseLoop.Step(walk, 0, 2), loaded: 6));
        Assert.Equal(0, ReleaseLoop.TubesLeftForStop(ReleaseLoop.Step(walk, 0, 3), loaded: 6));

        // A plan made against six and flown with two must not wait for a warhead that is not there.
        Assert.Equal(2, ReleaseLoop.TubesLeftForStop(ReleaseLoop.Step(walk, 0, 0), loaded: 2));
    }

    // ---------------------------------------------------------------- what it refuses

    /// <summary>
    /// <b>The contradiction phase 3 cannot decide for itself.</b> A set ordered the way
    /// <see cref="ReachDisplay"/> orders it — farthest from the bus's landing first — does not start
    /// where the booster aimed, because the booster aimed at the first target chosen and every other
    /// one was added during the coast. Flown as ordered the bus walks out and back, so the walk is
    /// refused rather than priced at half what it costs.
    /// </summary>
    [Fact]
    public void ASetOrderedFarthestFirstDoesNotStartWhereTheBusIs()
    {
        // Entry 0 is the booster's target and sits at the centre of the ring, so it ranks last.
        ReleaseItinerary.Target[] asDrawn =
        [
            new(1, 0.0, 0.0),
            new(1, 4_000.0, 4_000.0),
            new(1, 8_000.0, 4_000.0),
        ];

        ReleaseWalk walk = ReleaseLoop.Plan(asDrawn, SixThousand, Reach, lead: 0);

        Assert.Equal(ReleaseWalkHold.NotWhereTheBusIsAimed, walk.Hold);
        Assert.False(walk.Walks);
        Assert.Equal(2, walk.Itinerary.Stops[0].Target);

        Out.WriteLine(walk.Say());
    }

    /// <summary>
    /// <b>Every click was inside the ring and the hop between two of them is not.</b>
    /// <see cref="ReachDisplay"/> bounds a click to one hop from where the warheads land now, so two
    /// targets on opposite edges are twice the ring's radius apart — and that hop is what the bus
    /// actually flies.
    /// </summary>
    [Fact]
    public void AHopBeyondOnePassIsRefusedRatherThanFlown()
    {
        // 410 m per m/s pinned: the ring is 10 m/s wide, so its radius is 4.1 km and two targets on
        // opposite edges are 8.2 km apart -- 20 m/s, twice what one pass will fly.
        double radius = BusTrim.MaxMetresPerSecond * Reach[0];

        ReleaseItinerary.Target[] opposite =
        [
            new(1, radius, 0.0),
            new(1, radius, 2.0 * radius),
        ];

        ReleaseWalk walk = ReleaseLoop.Plan(opposite, SixThousand, Reach, lead: 0);

        Assert.Equal(ReleaseWalkHold.HopBeyondOnePass, walk.Hold);
        Assert.Equal(2.0 * BusTrim.MaxMetresPerSecond, walk.HopMetresPerSecond, 6);

        Out.WriteLine(walk.Say());
    }

    /// <summary>
    /// A set the coast is too short for is cut down before it is flown, not discovered at the last
    /// stop — and the cut is a re-plan, so the shorter walk's releases are later than the longer
    /// one's would have been.
    /// </summary>
    [Fact]
    public void ASetTheCoastCannotHoldIsCutBeforeItIsFlown()
    {
        ReleaseWalk walk = ReleaseLoop.Plan(Outward(4, 2_000.0), TwoThousand, Reach, lead: 0);

        // 351 s of coast against a 420 s gate leaves no room to start early, so one stop is all
        // that fits -- which is a bus releasing everything where it already is.
        Assert.Equal(ReleaseWalkHold.OneStop, walk.Hold);
        Assert.Equal(3, walk.Dropped);
        Assert.Equal(Gate, ReleaseLoop.FirstBeforeArrivalSeconds(walk, Gate));

        Out.WriteLine(walk.Say());
    }

    /// <summary>
    /// A set the budget will not pay for is cut to what it will, and the walk that is left starts
    /// where the bus is.
    /// </summary>
    [Fact]
    public void ASetTheBudgetCannotPayForIsCutToWhatItCan()
    {
        // 16.1 m/s already spent on the single-target flight, and each 4 km hop costs 9.76 at this
        // reach -- so 20 m/s pays for the flight and nothing else, 30 for one hop.
        ReleaseItinerary.Bus tight = SixThousand with { BudgetMetresPerSecond = 30.0 };

        ReleaseWalk walk = ReleaseLoop.Plan(Outward(4, 4_000.0), tight, Reach, lead: 0);

        Assert.True(walk.Walks);
        Assert.Equal(2, walk.Stops);
        Assert.Equal(2, walk.Dropped);
        Assert.Equal(0, walk.Itinerary.Stops[0].Target);
        Assert.Equal(1, walk.Itinerary.Stops[1].Target);

        Out.WriteLine(walk.Say());
    }

    /// <summary>
    /// The stops a cut leaves still name the caller's own entries, because that is what moves the
    /// aim. A re-plan renumbers its own input, so getting this wrong aims the bus at somebody else's
    /// target and nothing says so.
    /// </summary>
    [Fact]
    public void ACutSetStillNamesTheCallersOwnTargets()
    {
        ReleaseItinerary.Bus tight = SixThousand with { BudgetMetresPerSecond = 30.0 };

        ReleaseWalk walk = ReleaseLoop.Plan(Outward(6, 4_000.0), tight, Reach, lead: 0);

        for (int k = 0; k < walk.Stops; k++)
        {
            Assert.Equal(k, walk.Itinerary.Stops[k].Target);
        }
    }

    /// <summary>Six targets 4 km apart, as the line a night is scored off.</summary>
    [Fact]
    public void WhatAWalkLooksLike()
    {
        ReleaseWalk walk = ReleaseLoop.Plan(Outward(6, 4_000.0), SixThousand, Reach, lead: 0);

        Out.WriteLine(walk.Say());

        for (int k = 0; k < walk.Stops; k++)
        {
            ReleaseItinerary.Stop at = walk.Itinerary.Stops[k];

            Out.WriteLine(ReleaseLoop.Step(walk, k, 0).Say(
                k, walk.Stops, at.SpentMetresPerSecond,
                walk.Itinerary.Means.BudgetMetresPerSecond - at.SpentMetresPerSecond));
        }
    }
}
