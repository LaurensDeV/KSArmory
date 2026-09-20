using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// When a bus's target list may be edited. The panel and the world-click only actuate these, so
/// this is the whole of what can be wrong about either.
/// </summary>
public class TargetEditTests
{
    /// <summary>
    /// The safety property the whole feature rests on: a click with no region to test it against
    /// designates, at every phase — so a set nothing can price is never assembled.
    /// </summary>
    [Fact]
    public void ClickingWithNoReachDesignates()
    {
        foreach (IcbmPhase phase in Enum.GetValues<IcbmPhase>())
        {
            Assert.Equal(TargetClick.Designate, TargetEdit.ClickDoes(0, phase, reachIsKnown: false));
            Assert.Equal(TargetClick.Designate, TargetEdit.ClickDoes(1, phase, reachIsKnown: false));
            Assert.Equal(TargetClick.Designate,
                         TargetEdit.ClickDoes(TargetSet.MaxTargets, phase, reachIsKnown: false));
        }
    }

    /// <summary>
    /// With a place already named and a reach that can refuse the click, both sides of cutoff add.
    /// </summary>
    /// <remarks>
    /// Before the burn that is what puts every target on the list while the booster's aim is still a
    /// free variable, which is the one thing the release loop cannot arrange for itself — it refuses
    /// outright a plan whose first stop is not the one the bus is flying to.
    /// </remarks>
    [Fact]
    public void ClickingWithAReachAddsToWhatIsAlreadyAimedAt()
    {
        foreach (IcbmPhase phase in Enum.GetValues<IcbmPhase>())
        {
            if (phase == IcbmPhase.NoSolution) continue;

            Assert.Equal(TargetClick.Add, TargetEdit.ClickDoes(1, phase, reachIsKnown: true));
            Assert.Equal(TargetClick.Add,
                         TargetEdit.ClickDoes(TargetSet.MaxTargets, phase, reachIsKnown: true));
        }
    }

    /// <summary>
    /// A shot with no trajectory to the place it is aimed at designates, whatever the reach says.
    /// </summary>
    /// <remarks>
    /// Adding to a set the booster cannot deliver builds a list around a landing that is not going to
    /// happen; re-aiming is the only useful thing a click can do there.
    /// </remarks>
    [Fact]
    public void AShotWithNoTrajectoryDesignates()
    {
        Assert.Equal(TargetClick.Designate,
                     TargetEdit.ClickDoes(1, IcbmPhase.NoSolution, reachIsKnown: true));
    }

    /// <summary>Clearing the target is how a coasting bus is aimed somewhere else, and then a click starts over.</summary>
    [Fact]
    public void ACoastWithNothingAimedAtStillDesignates()
    {
        Assert.Equal(TargetClick.Designate, TargetEdit.ClickDoes(0, IcbmPhase.Coast, reachIsKnown: true));
    }

    /// <summary>
    /// The booster's aim may be moved onto another entry until the arrival is committed, and never
    /// after.
    /// </summary>
    /// <remarks>
    /// <b>This is the one rule in the file that can lose a shot.</b> Before the commit the guidance
    /// re-solves velocity-to-be-gained against the vehicle's actual state every cycle, so moving the
    /// aim costs propellant. After it the arc is pinned to an instant chosen for somewhere else, and
    /// past cutoff there is no engine left at all — the bus would have to divert the whole way, which
    /// is the 5–45 km ending rather than the 140 m one.
    /// </remarks>
    [Fact]
    public void TheAimMayMoveUntilTheArrivalIsCommitted()
    {
        foreach (IcbmPhase phase in Enum.GetValues<IcbmPhase>())
        {
            Assert.False(TargetEdit.LeadMayMove(phase, arrivalCommitted: true));

            Assert.Equal(phase != IcbmPhase.Coast,
                         TargetEdit.LeadMayMove(phase, arrivalCommitted: false));
        }
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
