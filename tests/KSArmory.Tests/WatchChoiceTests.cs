using Xunit;

namespace KSArmory.Tests;

public class WatchChoiceTests
{
    private static TrackState Threat(double secondsToClosest, double range = 5_000.0)
        => new() { IsThreat = true, TimeToClosestApproach = secondsToClosest, Range = range };

    private static TrackState Passing(double range) => new() { IsThreat = false, Range = range };

    // Two threats a few tenths of a second apart swap places on noise; following the scan's own pick
    // flips the head between them every time they do.
    [Fact]
    public void TwoNearlyEqualThreatsDoNotTradeTheWatch()
    {
        var tracks = new List<TrackState> { Threat(10.2), Threat(10.0) };

        int watched = 0;
        for (int scan = 0; scan < 20; scan++)
        {
            int fresh = scan % 2 == 0 ? 1 : 0;
            int next = WatchChoice.Choose(tracks, fresh, watched);
            Assert.Equal(0, next);
            watched = next;
        }
    }

    [Fact]
    public void AClearlySoonerThreatTakesTheWatch()
        => Assert.Equal(1, WatchChoice.Choose([Threat(20.0), Threat(5.0)], fresh: 1, held: 0));

    [Fact]
    public void AThreatTakesTheWatchFromAPasserBy()
        => Assert.Equal(1, WatchChoice.Choose([Passing(2_000.0), Threat(60.0)], fresh: 1, held: 0));

    [Fact]
    public void APasserByOnlyTakesTheWatchFromAnotherWhenClearlyNearer()
    {
        Assert.Equal(0, WatchChoice.Choose([Passing(10_000.0), Passing(9_500.0)], fresh: 1, held: 0));
        Assert.Equal(1, WatchChoice.Choose([Passing(10_000.0), Passing(6_000.0)], fresh: 1, held: 0));
    }

    [Fact]
    public void AContactThatLeftTheScopeHandsOverAtOnce()
        => Assert.Equal(0, WatchChoice.Choose([Threat(30.0)], fresh: 0, held: -1));
}
