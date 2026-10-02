using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// <see cref="IcbmConfig.WalkStartsAtCutoff"/>: a walk's first release due as the coast begins, rather
/// than its last one landing on the gate.
/// </summary>
public class WalkFromCutoffTests
{
    private const double Gate = 420.0;
    private const double Coast = 1_315.0;

    private static ReleaseItinerary Chain(int targets, bool fromCutoff)
        => ReleaseItinerary.Chain(targets, 1_000.0, [1_000.0],
                                  new ReleaseItinerary.Bus(Gate, Coast, 6, FromCutoff: fromCutoff));

    [Fact]
    public void FromCutoffTheFirstReleaseIsDueAsTheCoastBegins()
    {
        ReleaseItinerary walk = Chain(6, fromCutoff: true);

        Assert.Equal(Coast, walk.FirstBeforeArrivalSeconds, 6);
        Assert.Equal(Coast - 5 * ReleaseItinerary.MedianHopSeconds, walk.LastBeforeArrivalSeconds, 6);
        Assert.Equal(6, walk.CoastFits);
        Assert.False(walk.StartsBeforeCutoff);
    }

    [Fact]
    public void OffTheLastReleaseLandsOnTheGate()
    {
        ReleaseItinerary walk = Chain(6, fromCutoff: false);

        Assert.Equal(Gate, walk.LastBeforeArrivalSeconds, 6);
        Assert.Equal(Gate + 5 * ReleaseItinerary.MedianHopSeconds, walk.FirstBeforeArrivalSeconds, 6);
    }

    [Fact]
    public void ASetOfOneIsTheSingleTargetShotEitherWay()
    {
        Assert.Equal(Gate, Chain(1, fromCutoff: true).FirstBeforeArrivalSeconds, 9);
        Assert.Equal(Gate, Chain(1, fromCutoff: false).FirstBeforeArrivalSeconds, 9);
    }

    /// <summary>Before cutoff the coast is not known, and the walk is planned as it always was.</summary>
    [Fact]
    public void AnUnknownCoastLeavesTheGate()
    {
        ReleaseItinerary walk = ReleaseItinerary.Chain(
            6, 1_000.0, [1_000.0], new ReleaseItinerary.Bus(Gate, double.NaN, 6, FromCutoff: true));

        Assert.Equal(Gate, walk.LastBeforeArrivalSeconds, 6);
    }

    /// <summary>A coast too short to start any earlier never ends a walk before the gate.</summary>
    [Fact]
    public void NeverLaterThanTheGate()
    {
        ReleaseItinerary walk = ReleaseItinerary.Chain(
            6, 1_000.0, [1_000.0], new ReleaseItinerary.Bus(Gate, 500.0, 6, FromCutoff: true));

        Assert.Equal(Gate, walk.LastBeforeArrivalSeconds, 6);
    }
}
