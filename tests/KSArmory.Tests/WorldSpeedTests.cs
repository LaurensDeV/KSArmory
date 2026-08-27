using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What one world clock runs at when several flights each want something.
///
/// <para>There is one world and one speed. A scripted shot wants 0.01x while it is set up, 1x for
/// the ascent, 100x through the coast and 1x again for the release — so with several rockets in one
/// world the last writer wins and the rest are flown at a speed they did not choose.</para>
/// </summary>
public class WorldSpeedTests
{
    /// <summary>Nobody asking is not a request for 1x. The world is left alone.</summary>
    [Fact]
    public void NoOpinionLeavesTheWorldAlone()
    {
        Assert.True(double.IsNaN(WorldSpeed.Slowest([])));
        Assert.True(double.IsNaN(WorldSpeed.Slowest([double.NaN, double.NaN])));
    }

    /// <summary>One flight gets exactly what it asked for, which is what makes this a no-op
    /// against the single-rocket behaviour it replaced.</summary>
    [Theory]
    [InlineData(0.01)]
    [InlineData(1.0)]
    [InlineData(100.0)]
    public void OneFlightGetsWhatItAskedFor(double speed)
    {
        Assert.Equal(speed, WorldSpeed.Slowest([speed]), 9);
    }

    /// <summary>
    /// The slowest wins, because a speed is a ceiling on what can still be simulated faithfully and
    /// the tightest ceiling binds. A flight still being set up at 0.01x must not be dragged along at
    /// the 100x another one is coasting at — it would be picked up hundreds of kilometres from
    /// where the others were.
    /// </summary>
    [Fact]
    public void TheSlowestRequestWins()
    {
        Assert.Equal(0.01, WorldSpeed.Slowest([100.0, 1.0, 0.01, 8.0]), 9);
        Assert.Equal(1.0, WorldSpeed.Slowest([100.0, 100.0, 1.0]), 9);
    }

    /// <summary>A flight that has not decided is skipped rather than counted as a stop.</summary>
    [Fact]
    public void UndecidedFlightsDoNotStopTheWorld()
    {
        Assert.Equal(8.0, WorldSpeed.Slowest([double.NaN, 8.0, double.NaN]), 9);
    }

    /// <summary>
    /// And a non-positive speed is a bug rather than a request. Honouring one stops the world for
    /// every other flight sharing it, which is the most expensive way for a bad number to arrive.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.PositiveInfinity)]
    public void ANonSpeedIsIgnoredRatherThanStoppingTheWorld(double bad)
    {
        Assert.Equal(4.0, WorldSpeed.Slowest([bad, 4.0]), 9);
    }
}
