using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class StoreRetargetTests
{
    /// <summary>
    /// The store released last follows the marks after it, whether or not it went out onto one, until
    /// its weapon releases again. Which of those marks it takes is <see cref="StoreRetarget.Reaches"/>'s.
    /// </summary>
    [Theory]
    [InlineData(false, true)]      // still the latest: steer it
    [InlineData(true, false)]      // the next has gone since: the marks are the next one's
    public void OnlyTheLatestStoreIsSteered(bool since, bool expected)
        => Assert.Equal(expected, StoreRetarget.Takes(since));

    private static TailKitReach Region(TailKitHold hold)
        => new(hold, SecondsToGo: 30.0, ImpactEcl: new double3(0, 0, 0), RadiusMetres: 500.0);

    [Theory]
    [InlineData(400.0, true)]      // inside what the fins can still walk
    [InlineData(600.0, false)]     // past it: steered there it would hit nothing
    public void OnlyAPlaceTheFinsCanReachIsTaken(double metres, bool expected)
        => Assert.Equal(expected, StoreRetarget.Reaches(Region(TailKitHold.Known), new double3(metres, 0, 0)));

    /// <summary>
    /// No region makes no claim: a store still climbing off its rack has no landing to move yet,
    /// and refusing on that would make a designation do nothing with no reason anyone could act on.
    /// </summary>
    [Theory]
    [InlineData("NoLanding")]
    [InlineData("Unreadable")]
    public void AnUnflownRegionRefusesNothing(string hold)
        => Assert.True(StoreRetarget.Reaches(Region(Enum.Parse<TailKitHold>(hold)), new double3(50_000, 0, 0)));
}
