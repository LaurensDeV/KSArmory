using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Moving warheads between targets, which is what a click on the map does.
///
/// <para>The lead is the pool: a designation hands it everything aboard, and a target that takes
/// none rides there anyway. So these are really tests that the books balance — whatever a gesture
/// does, the warheads still add up to what is carried.</para>
/// </summary>
public class TargetSetShiftTests
{
    private const int Aboard = 7;

    private static AimSite Place(double lon) => new("Earth", -24.0, lon, "");

    private static TargetSet WithTwo()
    {
        TargetSet set = new();
        set.SetOnly(Place(-62.0), Aboard);
        set.TryAdd(Place(-61.0));
        return set;
    }

    [Fact]
    public void ADesignationPutsEveryWarheadOnTheOneTarget()
    {
        TargetSet set = new();
        set.SetOnly(Place(-62.0), Aboard);

        Assert.Equal(Aboard, set.Assigned);
    }

    [Fact]
    public void AddedTargetsStartEmpty()
    {
        // The asymmetry this exists to paper over: clicking a second place changes nothing about
        // the shot until a warhead is moved onto it.
        Assert.Equal(0, WithTwo().Entries[1].Warheads);
    }

    [Fact]
    public void TakingOneForANewTargetTakesItFromTheFullest()
    {
        TargetSet set = WithTwo();

        Assert.True(set.Shift(1, 1, Aboard));

        Assert.Equal(1, set.Entries[1].Warheads);
        Assert.Equal(Aboard - 1, set.Entries[0].Warheads);
        Assert.Equal(Aboard, set.Assigned);
    }

    [Fact]
    public void ANewTargetThatBecameTheLeadCanStillBeGivenWarheads()
    {
        // AddTarget re-elects the lead to whichever target is farthest, so the empty new one often
        // IS the lead by the time a warhead is wanted, and taking "from the lead" takes from nothing.
        TargetSet set = WithTwo();
        set.SetLead(1);

        Assert.True(set.Shift(1, 2, Aboard));

        Assert.Equal(2, set.Entries[1].Warheads);
        Assert.Equal(Aboard - 2, set.Entries[0].Warheads);
        Assert.Equal(Aboard, set.Assigned);
    }

    [Fact]
    public void GivingOneBackLeavesItRidingToTheLead()
    {
        TargetSet set = WithTwo();
        set.Shift(1, 2, Aboard);

        Assert.True(set.Shift(1, -1, Aboard));

        // It is not credited anywhere: it simply stops being assigned, and an unassigned warhead
        // already rides to the lead.
        Assert.Equal(1, set.Entries[1].Warheads);
        Assert.Equal(Aboard - 1, set.Assigned);
    }

    [Fact]
    public void AnUnassignedRemainderIsSpentBeforeAnyTargetIsTouched()
    {
        TargetSet set = WithTwo();
        set.SetWarheads(0, 4, Aboard);      // three now ride to the lead unassigned

        Assert.True(set.Shift(1, 3, Aboard));

        Assert.Equal(4, set.Entries[0].Warheads);   // the full one kept all of its own
        Assert.Equal(3, set.Entries[1].Warheads);
    }

    [Fact]
    public void TakingMoreThanThereIsIsRefusedRatherThanClamped()
    {
        TargetSet set = WithTwo();

        Assert.False(set.Shift(1, Aboard + 1, Aboard));
        Assert.Equal(0, set.Entries[1].Warheads);
        Assert.Equal(Aboard, set.Assigned);
    }

    [Fact]
    public void ATargetCannotTakeFromItself()
    {
        TargetSet set = WithTwo();

        // Nothing else holds any, so there is nowhere for one to come from.
        Assert.False(set.Shift(0, 1, Aboard));
    }

    [Fact]
    public void AGestureNeverGoesBelowNothing()
    {
        TargetSet set = WithTwo();

        Assert.False(set.Shift(1, -1, Aboard));
        Assert.Equal(0, set.Entries[1].Warheads);
    }
}
