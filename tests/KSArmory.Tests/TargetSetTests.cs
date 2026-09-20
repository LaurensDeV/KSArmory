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
}
