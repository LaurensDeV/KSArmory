using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// When a bus's target list may be edited. The panel and the world-click only actuate these, so
/// this is the whole of what can be wrong about either.
/// </summary>
public class TargetEditTests
{
    /// <summary>
    /// The safety property the whole feature rests on: nothing adds to the list before cutoff, so a
    /// shot flown to one place cannot reach any of the new paths.
    /// </summary>
    [Fact]
    public void ClickingBeforeTheBurnIsOverDesignates()
    {
        foreach (IcbmPhase phase in Enum.GetValues<IcbmPhase>())
        {
            if (phase == IcbmPhase.Coast) continue;

            Assert.Equal(TargetClick.Designate, TargetEdit.ClickDoes(0, phase));
            Assert.Equal(TargetClick.Designate, TargetEdit.ClickDoes(1, phase));
            Assert.Equal(TargetClick.Designate, TargetEdit.ClickDoes(TargetSet.MaxTargets, phase));
        }
    }

    /// <summary>Designating in the coast would reset the program, which is the flight it is half-way through.</summary>
    [Fact]
    public void ClickingDuringTheCoastAddsToWhatIsAlreadyAimedAt()
    {
        Assert.Equal(TargetClick.Add, TargetEdit.ClickDoes(1, IcbmPhase.Coast));
        Assert.Equal(TargetClick.Add, TargetEdit.ClickDoes(TargetSet.MaxTargets, IcbmPhase.Coast));
    }

    /// <summary>Clearing the target is how a coasting bus is aimed somewhere else, and then a click starts over.</summary>
    [Fact]
    public void ACoastWithNothingAimedAtStillDesignates()
    {
        Assert.Equal(TargetClick.Designate, TargetEdit.ClickDoes(0, IcbmPhase.Coast));
    }

    [Fact]
    public void TheTargetTheFlightIsAimedAtIsNotRemovable()
    {
        Assert.False(TargetEdit.MayRemove(0, targets: 3, lead: 0));
        Assert.True(TargetEdit.MayRemove(1, targets: 3, lead: 0));
        Assert.True(TargetEdit.MayRemove(2, targets: 3, lead: 0));
    }

    /// <summary>
    /// The booster need not be flying to the first place chosen, so the rule follows the lead rather
    /// than index zero.
    /// </summary>
    [Fact]
    public void AndThatIsTheLeadRatherThanTheFirstChosen()
    {
        Assert.True(TargetEdit.MayRemove(0, targets: 3, lead: 2));
        Assert.True(TargetEdit.MayRemove(1, targets: 3, lead: 2));
        Assert.False(TargetEdit.MayRemove(2, targets: 3, lead: 2));
    }

    [Fact]
    public void AnEntryThatIsNotThereIsNotRemovable()
    {
        Assert.False(TargetEdit.MayRemove(3, targets: 3, lead: 0));
        Assert.False(TargetEdit.MayRemove(-1, targets: 3, lead: 0));
        Assert.False(TargetEdit.MayRemove(1, targets: 1, lead: 0));
    }

    /// <summary>
    /// The magazine reads six again a few seconds after the salvo, so the count the list is planned
    /// against has to stop believing it once a warhead has gone.
    /// </summary>
    [Fact]
    public void TheSalvosOwnSizeBeatsWhatTheMagazineReports()
    {
        Assert.Equal(4, TargetEdit.WarheadsAboard(loaded: 6, salvoSize: 4));
    }

    [Fact]
    public void BeforeAnySalvoItIsWhatTheMagazineHolds()
    {
        Assert.Equal(4, TargetEdit.WarheadsAboard(loaded: 4, salvoSize: 0));
    }

    /// <summary>A designation made before anything has read the rack still takes the whole bus.</summary>
    [Fact]
    public void ARackNobodyHasReadIsTakenAsFull()
    {
        Assert.Equal(TargetSet.MaxTargets, TargetEdit.WarheadsAboard(loaded: 0, salvoSize: 0));
    }
}
