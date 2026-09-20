using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The target list, which is data only: nothing here flies a bus anywhere.
/// </summary>
public class TargetSetTests
{
    private static AimSite At(double lat, double lon) => new("Earth", lat, lon, "");

    /// <summary>
    /// The case every flight so far is, and the one the release loop must not cost anything:
    /// one target holding the whole salvo is the plan that flies today.
    /// </summary>
    [Fact]
    public void OneTargetHoldingEveryWarheadIsTodaysSalvo()
    {
        TargetSet set = new();
        set.SetOnly(At(-24.0, -62.0), 6);

        Assert.Equal(1, set.Count);
        Assert.Equal(6, set.Assigned);
        Assert.Equal(At(-24.0, -62.0), set.Primary);
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, set.ReleasePlan(6));
    }

    [Fact]
    public void AnUnresolvedPlaceIsNotATarget()
    {
        TargetSet set = new();

        Assert.False(set.TryAdd(AimSite.None));
        Assert.Equal(0, set.Count);
        Assert.Equal("no targets", set.Describe(6));
    }

    [Fact]
    public void TheSeventhTargetIsRefused()
    {
        TargetSet set = new();
        for (int i = 0; i < TargetSet.MaxTargets; i++) Assert.True(set.TryAdd(At(-20.0 - i, -60.0)));

        Assert.False(set.TryAdd(At(-40.0, -60.0)));
        Assert.Equal(TargetSet.MaxTargets, set.Count);
    }

    /// <summary>
    /// A new target takes nothing from one already aimed at. A click on the map that silently moved
    /// warheads off another target is the one behaviour this refuses to have.
    /// </summary>
    [Fact]
    public void AddingATargetTakesNoWarheadsFromTheOthers()
    {
        TargetSet set = new();
        set.SetOnly(At(-24.0, -62.0), 6);
        Assert.True(set.TryAdd(At(-25.0, -63.0)));

        Assert.Equal(6, set.Entries[0].Warheads);
        Assert.Equal(0, set.Entries[1].Warheads);
        Assert.Equal(6, set.Assigned);
    }

    [Theory]
    [InlineData(1, new[] { 6 })]
    [InlineData(2, new[] { 3, 3 })]
    [InlineData(4, new[] { 2, 2, 1, 1 })]
    [InlineData(5, new[] { 2, 1, 1, 1, 1 })]
    [InlineData(6, new[] { 1, 1, 1, 1, 1, 1 })]
    public void BalancingSpreadsEvenlyAndGivesTheRemainderToTheEarliest(int targets, int[] expected)
    {
        TargetSet set = new();
        for (int i = 0; i < targets; i++) set.TryAdd(At(-20.0 - i, -60.0));

        set.Balance(6);

        Assert.Equal(expected, set.Entries.Select(e => e.Warheads).ToArray());
        Assert.Equal(6, set.Assigned);
    }

    /// <summary>Unassigned warheads ride the bus down, and the panel has to say so.</summary>
    [Fact]
    public void WarheadsNobodyAskedForAreKeptAboardAndSaidSo()
    {
        TargetSet set = new();
        set.TryAdd(At(-24.0, -62.0));
        set.SetWarheads(0, 2, available: 6);

        Assert.Equal(2, set.Assigned);
        Assert.Equal("1 target, 4 warheads kept aboard", set.Describe(6));
        Assert.Equal(new[] { 0, 0, -1, -1, -1, -1 }, set.ReleasePlan(6));
    }

    [Fact]
    public void ATargetCannotTakeWarheadsAnotherHasAlreadyBeenGiven()
    {
        TargetSet set = new();
        set.TryAdd(At(-24.0, -62.0));
        set.TryAdd(At(-25.0, -63.0));

        Assert.Equal(4, set.SetWarheads(0, 4, available: 6));
        Assert.Equal(2, set.SetWarheads(1, 5, available: 6));
        Assert.Equal(6, set.Assigned);
    }

    [Fact]
    public void RemovingATargetGivesItsWarheadsBackToTheSpares()
    {
        TargetSet set = new();
        set.TryAdd(At(-24.0, -62.0));
        set.TryAdd(At(-25.0, -63.0));
        set.Balance(6);

        Assert.True(set.RemoveAt(0));
        Assert.Equal(3, set.Assigned);
        Assert.Equal(At(-25.0, -63.0), set.Primary);
        Assert.Equal("1 target, 3 warheads kept aboard", set.Describe(6));
    }

    /// <summary>
    /// The place the flight is aimed at is a question the set answers, not the order the player
    /// happened to click in: the release schedule flies the farthest reach first.
    /// </summary>
    [Fact]
    public void TheFlightIsAimedAtTheLeadRatherThanTheFirstChosen()
    {
        TargetSet set = new();
        set.SetOnly(At(-24.0, -62.0), 6);
        set.TryAdd(At(-25.0, -63.0));

        Assert.Equal(0, set.LeadIndex);
        Assert.Equal(At(-24.0, -62.0), set.Primary);

        Assert.True(set.SetLead(1));
        Assert.Equal(At(-25.0, -63.0), set.Primary);
    }

    [Fact]
    public void AnEntryThatIsNotThereCannotBeTheLead()
    {
        TargetSet set = new();
        set.TryAdd(At(-24.0, -62.0));

        Assert.False(set.SetLead(1));
        Assert.False(set.SetLead(-1));
        Assert.Equal(0, set.LeadIndex);
    }

    /// <summary>A lead taken away aims the flight at the first place chosen, not at its neighbour.</summary>
    [Fact]
    public void RemovingTheLeadFallsBackToTheFirstChosen()
    {
        TargetSet set = new();
        set.TryAdd(At(-24.0, -62.0));
        set.TryAdd(At(-25.0, -63.0));
        set.TryAdd(At(-26.0, -64.0));
        set.SetLead(2);

        Assert.True(set.RemoveAt(2));
        Assert.Equal(0, set.LeadIndex);
        Assert.Equal(At(-24.0, -62.0), set.Primary);
    }

    /// <summary>Removing anything before the lead must not slide the aim onto a different place.</summary>
    [Fact]
    public void RemovingBeforeTheLeadKeepsTheFlightAimedWhereItWas()
    {
        TargetSet set = new();
        set.TryAdd(At(-24.0, -62.0));
        set.TryAdd(At(-25.0, -63.0));
        set.TryAdd(At(-26.0, -64.0));
        set.SetLead(2);

        Assert.True(set.RemoveAt(0));
        Assert.Equal(1, set.LeadIndex);
        Assert.Equal(At(-26.0, -64.0), set.Primary);
    }

    /// <summary>A new designation is a new shot, so the lead goes back to the one place it names.</summary>
    [Fact]
    public void DesignatingAgainPutsTheLeadBackOnTheOnlyEntry()
    {
        TargetSet set = new();
        set.TryAdd(At(-24.0, -62.0));
        set.TryAdd(At(-25.0, -63.0));
        set.SetLead(1);

        set.SetOnly(At(-30.0, -70.0), 6);

        Assert.Equal(0, set.LeadIndex);
        Assert.Equal(At(-30.0, -70.0), set.Primary);
    }

    /// <summary>The plan is what the flight reads, so it has to survive a bus with fewer rounds left.</summary>
    [Fact]
    public void ThePlanIsBoundedByTheWarheadsActuallyAboard()
    {
        TargetSet set = new();
        set.TryAdd(At(-24.0, -62.0));
        set.TryAdd(At(-25.0, -63.0));
        set.Balance(6);

        Assert.Equal(new[] { 0, 0, 0 }, set.ReleasePlan(3));
        Assert.Empty(set.ReleasePlan(0));
    }

    // ------------------------------------------------- the booster flies to the farthest

    /// <summary>
    /// The booster is aimed at whichever target is farthest downrange, not at the first clicked.
    /// </summary>
    /// <remarks>
    /// The release schedule walks inward from wherever the booster puts the bus, and the first stop
    /// is the only one that costs no hop. Aimed anywhere but an end of the set the walk goes out and
    /// back and spends about twice what the itinerary charges, which is exactly what
    /// <c>ReleaseWalkHold.NotWhereTheBusIsAimed</c> refuses.
    /// </remarks>
    [Fact]
    public void TheLeadIsTheFarthestTargetRatherThanTheFirstChosen()
    {
        TargetSet set = new();
        set.TryAdd(At(-24.0, -62.0));
        set.TryAdd(At(-25.0, -63.0));
        set.TryAdd(At(-26.0, -64.0));

        Assert.True(set.ElectFarthestLead([4_000_000.0, 9_000_000.0, 6_000_000.0]));
        Assert.Equal(1, set.LeadIndex);
        Assert.Equal(At(-25.0, -63.0), set.Primary);
    }

    /// <summary>
    /// <b>A set of one is exactly the shot every accuracy measurement on this mod is taken
    /// against</b>, and nothing the multi-target work added can move it.
    /// </summary>
    /// <remarks>
    /// The flight reads <see cref="TargetSet.Primary"/> and <see cref="TargetSet.ReleasePlan"/> and
    /// nothing else about the list, so this is the whole of what has to hold. Stated as an
    /// invariance rather than as a value: the election is run at every range there is, including the
    /// ones no world could answer, and neither reading is allowed to move.
    /// </remarks>
    [Fact]
    public void ASetOfOneIsUnmovedByEverythingTheMultiTargetWorkAdded()
    {
        TargetSet set = new();
        set.TryAdd(At(-24.0, -62.0));
        set.Balance(6);

        AimSite was = set.Primary;
        int[] plan = set.ReleasePlan(6);

        foreach (double range in new[] { 0.0, 1.0, 9_000_000.0, double.NaN, double.PositiveInfinity,
                                         double.NegativeInfinity })
        {
            Assert.False(set.ElectFarthestLead([range]));

            Assert.Equal(0, set.LeadIndex);
            Assert.Equal(was, set.Primary);
            Assert.Equal(plan, set.ReleasePlan(6));
        }

        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, plan);
    }

    /// <summary>Ties keep the lead where it is, so an election is not a source of movement.</summary>
    [Fact]
    public void ATieLeavesTheLeadAlone()
    {
        TargetSet set = new();
        set.TryAdd(At(-24.0, -62.0));
        set.TryAdd(At(-25.0, -63.0));
        set.SetLead(1);

        Assert.False(set.ElectFarthestLead([5_000_000.0, 5_000_000.0]));
        Assert.Equal(1, set.LeadIndex);
    }

    /// <summary>
    /// A range the world could not resolve ranks nearest rather than winning, and never throws.
    /// </summary>
    /// <remarks>
    /// The aim is the one thing in the set that must not move on a reading nobody can make: a NaN
    /// taken as farthest would fly the booster at a place that could not be located.
    /// </remarks>
    [Fact]
    public void ARangeNobodyCouldReadNeverWinsTheAim()
    {
        TargetSet set = new();
        set.TryAdd(At(-24.0, -62.0));
        set.TryAdd(At(-25.0, -63.0));

        Assert.False(set.ElectFarthestLead([5_000_000.0, double.NaN]));
        Assert.Equal(0, set.LeadIndex);

        // And a list that does not describe this set is refused outright rather than half applied.
        Assert.False(set.ElectFarthestLead([1.0]));
        Assert.Equal(0, set.LeadIndex);
    }

    /// <summary>
    /// Even a set whose ranges are all unreadable keeps a lead that is on the list, because
    /// everything downstream indexes with it.
    /// </summary>
    [Fact]
    public void TheLeadIsAlwaysAnEntryThatIsThere()
    {
        TargetSet set = new();
        set.TryAdd(At(-24.0, -62.0));
        set.TryAdd(At(-25.0, -63.0));

        set.ElectFarthestLead([double.NaN, double.NaN]);

        Assert.InRange(set.LeadIndex, 0, set.Count - 1);
    }
}
