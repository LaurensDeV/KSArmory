using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What one craft's weapons have in the air between them.
///
/// <para>The rule worth pinning is that the tally is per craft and keyed on the target itself, so
/// two weapons holding two separate tracks for one aircraft still agree about how hard it is being
/// engaged.</para>
/// </summary>
public class TargetAllocationTests
{
    private sealed class Contact(string name)
    {
        public override string ToString() => name;
    }

    [Fact]
    public void NothingIsCommittedToBeginWith()
    {
        TargetAllocation a = new();

        Assert.Equal(0, a.CommittedTo(new Contact("bandit")));
        Assert.Equal(0, a.TargetCount);
    }

    /// <summary>
    /// The whole point: two weapons firing at one aircraft produce one tally, so the second one to
    /// ask sees what the first already committed. Counting per weapon is what lets both fire a
    /// full salvo at the same target while each believes it is under the limit.
    /// </summary>
    [Fact]
    public void RoundsFromDifferentWeaponsLandOnOneTally()
    {
        Contact bandit = new("bandit");
        TargetAllocation a = new();

        // The first rail's two rounds, then the second rail's one.
        a.Commit(bandit);
        a.Commit(bandit);
        a.Commit(bandit);

        Assert.Equal(3, a.CommittedTo(bandit));
        Assert.Equal(1, a.TargetCount);
    }

    [Fact]
    public void TargetsAreCountedApart()
    {
        Contact one = new("one");
        Contact two = new("two");
        TargetAllocation a = new();

        a.Commit(one);
        a.Commit(two);
        a.Commit(two);

        Assert.Equal(1, a.CommittedTo(one));
        Assert.Equal(2, a.CommittedTo(two));
        Assert.Equal(2, a.TargetCount);
    }

    /// <summary>
    /// By reference, never by equality. Two sensors hold their own track object for one aircraft,
    /// so a tally keyed on anything a weapon derives would have nothing in common with its
    /// neighbour's.
    /// </summary>
    [Fact]
    public void TwoContactsThatLookAlikeAreStillTwoContacts()
    {
        Contact a1 = new("bandit");
        Contact a2 = new("bandit");
        TargetAllocation a = new();

        a.Commit(a1);

        Assert.Equal(1, a.CommittedTo(a1));
        Assert.Equal(0, a.CommittedTo(a2));
    }

    /// <summary>
    /// A round with nothing to steer at is not engaging anything. Counting a bomb falling on a
    /// coordinate would hold the craft's other weapons off a target nobody is shooting at.
    /// </summary>
    [Fact]
    public void ARoundWithNoTargetIsNotCommittedToOne()
    {
        TargetAllocation a = new();

        a.Commit(null);

        Assert.Equal(0, a.TargetCount);
        Assert.Equal(0, a.CommittedTo(null));
    }

    /// <summary>
    /// Rebuilt each frame rather than decremented, because a round can leave the air by detonating,
    /// being shot down, going loose or losing its lock, and no one place sees all four.
    /// </summary>
    [Fact]
    public void ClearingForgetsEverything()
    {
        Contact bandit = new("bandit");
        TargetAllocation a = new();

        a.Commit(bandit);
        a.Clear();

        Assert.Equal(0, a.CommittedTo(bandit));
        Assert.Equal(0, a.TargetCount);
    }
}
