using Xunit;

namespace KSArmory.Tests;

public class ChaseStopShortTests
{
    [Fact]
    public void AShellIsWatchedFromTheFloor()
    {
        Assert.Equal(60.0, ChaseView.StopShortMetres(3.3), 1e-9);
        Assert.Equal(60.0, ChaseView.StopShortMetres(0.16), 1e-9);
    }

    [Fact]
    public void ABigWarheadIsWatchedFromClearOfItsFireball()
    {
        double bomb = ChaseView.StopShortMetres(300_000.0);

        Assert.True(bomb >= 6.0 * Warhead.FireballRadius(300_000.0) - 1e-6);
        Assert.InRange(bomb, 1000.0, 1100.0);
    }

    [Fact]
    public void TheChaseStopsOnlyOnceWhatIsLeftIsInsideTheDistance()
    {
        // A 5-inch shell at 800 m/s: 160 m to go rides on, 56 m to go stops.
        Assert.False(ChaseView.StopsShort(timeToGo: 0.2, speed: 800.0, stopShortMetres: 60.0));
        Assert.True(ChaseView.StopsShort(timeToGo: 0.07, speed: 800.0, stopShortMetres: 60.0));
    }

    [Fact]
    public void NothingToCountDownToNeverStopsIt()
    {
        Assert.False(ChaseView.StopsShort(double.NaN, 800.0, 60.0));
        Assert.False(ChaseView.StopsShort(0.05, double.NaN, 60.0));
        Assert.False(ChaseView.StopsShort(-1.0, 800.0, 60.0));
    }
}
