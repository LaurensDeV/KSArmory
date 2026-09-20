using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The order a walk is flown in, and the second limit that can empty the tank before it ends.
///
/// <para><b>The flight this exists for.</b> Four targets 1 km apart walked <b>3 → 0 → 1 → 2</b> —
/// out to the near end and back — which is 5 km of travel for a 3 km chain. Its four stops spent
/// 11.21, 17.08 and 31.72 m/s, reaching <b>60.01 of a 60 m/s budget</b>, and the fourth released at
/// <c>divert 0.00</c> a kilometre from where its warhead was sent.</para>
/// </summary>
public class ReleaseWalkOrderTests(ITestOutputHelper Out)
{
    // Four collinear targets a kilometre apart, clicked near to far, each taking one warhead.
    // ElectFarthestLead makes the last one the lead, which is what the flight does.
    private static ReachDisplay.Placed[] AChain(int targets = 4, double spacingMetres = 1000.0)
    {
        ReachDisplay.Placed[] placed = new ReachDisplay.Placed[targets];
        for (int i = 0; i < targets; i++) placed[i] = new(i * spacingMetres, 0.0, 1);

        return placed;
    }

    private static int[] FlownOrder(ReleaseItinerary.Target[] set)
    {
        int[] order = new int[set.Length];
        for (int i = 0; i < set.Length; i++) order[i] = i;

        // Plan's own comparison: descending rank, ties keeping the caller's order.
        Array.Sort(order, (a, b) =>
        {
            int byRank = set[b].ReachMetres.CompareTo(set[a].ReachMetres);
            return byRank != 0 ? byRank : a.CompareTo(b);
        });

        return order;
    }

    /// <summary>
    /// <b>The zigzag.</b> Led from the far end, the walk goes to the near neighbour each time rather
    /// than to whichever was clicked next — which for a chain is the monotone walk.
    /// </summary>
    [Fact]
    public void AChainIsWalkedInOrderRatherThanInTheOrderItWasClicked()
    {
        ReachDisplay.Walk walk = ReachDisplay.Order(AChain(), lead: 3);

        Assert.Equal(new[] { 3, 2, 1, 0 }, FlownOrder(walk.Set));

        // Every hop is one span, so the chain costs its own length and nothing more.
        foreach (int i in new[] { 2, 1, 0 }) Assert.Equal(1000.0, walk.Set[i].HopMetres, 6);

        double travelled = walk.Set.Sum(t => double.IsFinite(t.HopMetres) ? t.HopMetres : 0.0);
        Assert.Equal(3000.0, travelled, 6);

        Out.WriteLine($"order {string.Join(" -> ", FlownOrder(walk.Set))}, {travelled:F0} m walked");
    }

    /// <summary>It walks outward just as happily when the lead is the near end.</summary>
    [Fact]
    public void AChainLedFromTheNearEndWalksOutward()
    {
        ReachDisplay.Walk walk = ReachDisplay.Order(AChain(), lead: 0);

        Assert.Equal(new[] { 0, 1, 2, 3 }, FlownOrder(walk.Set));
        Assert.Equal(3000.0, walk.Set.Sum(t => double.IsFinite(t.HopMetres) ? t.HopMetres : 0.0), 6);
    }

    /// <summary>
    /// And it is the <em>walk</em> that shortens, not just the ordering: the chosen order costs the
    /// out-and-back that the flight paid for.
    /// </summary>
    [Fact]
    public void TheChosenOrderWouldHaveCostTheOutAndBack()
    {
        ReachDisplay.Placed[] placed = AChain();

        // What the flown order did: 3 -> 0 is three spans, then 0 -> 1 -> 2 is two more.
        double zigzag = 3000.0 + 1000.0 + 1000.0;
        double chain = ReachDisplay.Order(placed, lead: 3).Set
                                   .Sum(t => double.IsFinite(t.HopMetres) ? t.HopMetres : 0.0);

        Assert.Equal(5000.0, zigzag, 6);
        Assert.True(chain < zigzag, $"the chain walks {chain:F0} m against the zigzag's {zigzag:F0}");
        Out.WriteLine($"{zigzag:F0} m flown against {chain:F0} m as a chain");
    }

    /// <summary>A target off the line still goes where it is nearest, and nothing is visited twice.</summary>
    [Fact]
    public void EveryStopIsVisitedExactlyOnce()
    {
        ReachDisplay.Placed[] placed =
        [
            new(0.0, 0.0, 1), new(4000.0, 0.0, 1), new(1000.0, 500.0, 1), new(2000.0, 0.0, 1),
        ];

        int[] order = FlownOrder(ReachDisplay.Order(placed, lead: 1).Set);

        Assert.Equal(4, order.Length);
        Assert.Equal(4, order.Distinct().Count());
        Assert.Equal(1, order[0]);
        Out.WriteLine($"order {string.Join(" -> ", order)}");
    }

    // ------------------------------------------------- the budget, which refuses nothing downstream

    /// <summary>
    /// <b>The second limit.</b> A hop inside the per-pass ceiling can still be one the flight cannot
    /// pay for — and nothing below refuses it, so the release happens with no divert flown.
    /// </summary>
    [Fact]
    public void AHopTheBudgetCannotPayForIsRefusedEvenThoughOnePassWouldFlyIt()
    {
        const double Hop = 2.4;
        const double Owed = 0.1;
        const double Budget = 60.0;

        // Where the flown fourth stop stood: 60.01 m/s already spent.
        Assert.True(ReleaseLoop.OnePassWillFly(Hop, Owed, double.NaN),
                    "one pass would fly it -- the ceiling is not what refuses this");

        Assert.Equal(HopHold.BeyondTheBudget,
                     ReleaseLoop.CanFlyTheHop(Hop, Owed, double.NaN, 60.01, Budget));

        // And with room, it flies.
        Assert.Equal(HopHold.WillFly,
                     ReleaseLoop.CanFlyTheHop(Hop, Owed, double.NaN, 28.29, Budget));

        // The whole pass has to be payable, not merely its first metre.
        Assert.Equal(HopHold.WillFly,
                     ReleaseLoop.CanFlyTheHop(Hop, Owed, double.NaN, Budget - Hop - Owed, Budget));

        Assert.Equal(HopHold.BeyondTheBudget,
                     ReleaseLoop.CanFlyTheHop(Hop, Owed, double.NaN, Budget - Hop, Budget));
    }

    /// <summary>The per-pass ceiling is asked first, because it is the tighter of the two.</summary>
    [Fact]
    public void TheCeilingIsNamedAheadOfTheBudget()
    {
        Assert.Equal(HopHold.BeyondOnePass,
                     ReleaseLoop.CanFlyTheHop(16.2, 0.0, double.NaN, 0.0, 60.0));

        // Even with the budget also exhausted, the pass is what a reader has to act on.
        Assert.Equal(HopHold.BeyondOnePass,
                     ReleaseLoop.CanFlyTheHop(16.2, 0.0, double.NaN, 59.0, 60.0));
    }

    /// <summary>A budget nobody stated bounds nothing, so an unset one cannot end a walk.</summary>
    [Fact]
    public void NoBudgetRefusesNothing()
    {
        Assert.Equal(HopHold.WillFly,
                     ReleaseLoop.CanFlyTheHop(2.4, 0.1, double.NaN, 1e6, double.PositiveInfinity));

        Assert.Equal(HopHold.WillFly,
                     ReleaseLoop.CanFlyTheHop(2.4, 0.1, double.NaN, double.NaN, 60.0));
    }
}
