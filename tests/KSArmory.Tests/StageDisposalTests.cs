using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Whether a spent stage may be taken out of the world.
///
/// <para>The cost of being wrong is asymmetric and the tests are written for that: leaving a stage
/// costs about 2 ms of frame time, and taking the wrong one destroys something in the player's
/// world that nobody shot at. So every uncertain case has to answer no.</para>
/// </summary>
public class StageDisposalTests
{
    private const double Clear = StageDisposal.ClearOfTheCraftMetres;

    /// <summary>Off is the default and the default has to mean nothing happens.</summary>
    [Fact]
    public void NothingIsDisposedUnlessItWasAskedFor()
    {
        Assert.False(StageDisposal.MayDispose(false, watchedByTheClearance: false, Clear * 100.0));
    }

    /// <summary>
    /// The half the clearance is still reading is never taken, whatever it costs and however far
    /// away it is. <see cref="SeparationClearance"/> reads an unreadable distance as a blind clock
    /// rather than as clearance, so removing the stack would authorise the trim while the bus is
    /// still beside where it was.
    /// </summary>
    [Theory]
    [InlineData(Clear)]
    [InlineData(Clear * 1000.0)]
    public void TheStackTheClearanceIsWatchingIsNeverTaken(double metres)
    {
        Assert.False(StageDisposal.MayDispose(true, watchedByTheClearance: true, metres));
    }

    /// <summary>
    /// An unreadable distance is not a disposable one. Everywhere else in this mod a part tree
    /// mid-rebuild answers with nothing; here reading that as "far away" destroys a vehicle chosen
    /// at random.
    /// </summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnUnreadableDistanceIsNotDisposable(double metres)
    {
        Assert.False(StageDisposal.MayDispose(true, watchedByTheClearance: false, metres));
    }

    /// <summary>And a stage still beside the craft waits, because the census identifies it by
    /// being new and nearest — which is exactly what something adjacent to the craft looks like.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(Clear - 1.0)]
    public void AStageStillNearTheCraftWaits(double metres)
    {
        Assert.False(StageDisposal.MayDispose(true, watchedByTheClearance: false, metres));
    }

    /// <summary>What is left is the case it exists for.</summary>
    [Theory]
    [InlineData(Clear)]
    [InlineData(Clear * 50.0)]
    public void AnAscentStageWellClearOfTheCraftGoes(double metres)
    {
        Assert.True(StageDisposal.MayDispose(true, watchedByTheClearance: false, metres));
    }
}
