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

    /// <summary>
    /// The view is held on a burst for as long as there is something still happening, which for a
    /// conventional round is no time at all and for a nuclear one is the cloud's whole rise.
    ///
    /// <para>A flat three seconds showed about a twenty-fifth of a mushroom cloud and took the
    /// camera away mid-event. Reported from a stream, which is the only place anybody watches one
    /// all the way through.</para>
    /// </summary>
    [Theory]
    [InlineData(0.02)]            // a 20 mm shell
    [InlineData(20.0)]            // a 57E6 warhead
    [InlineData(999.0)]           // just under the cloud threshold
    public void AConventionalBurstIsHeldOnlyLongEnoughToSeeIt(double chargeKg)
    {
        Assert.Equal(ChaseView.MinLingerSeconds, ChaseView.LingerSeconds(chargeKg));
    }

    /// <summary>A burst that grows a cloud is held until the cloud stops changing shape.</summary>
    [Theory]
    [InlineData(MushroomCloud.ThresholdKg)]
    [InlineData(300_000.0)]       // the B61 at its lowest yield
    [InlineData(20_000_000.0)]    // a Mk 21 at 20 kt
    public void ANuclearBurstIsHeldForTheCloudsRise(double chargeKg)
    {
        Assert.Equal(MushroomCloud.RiseSeconds, ChaseView.LingerSeconds(chargeKg));
    }

    /// <summary>
    /// And the threshold it turns on is the one that decides whether a cloud exists at all, rather
    /// than a second number that could drift from it — a hold sized for a cloud nobody drew would
    /// be the camera taken hostage for nothing.
    /// </summary>
    [Fact]
    public void TheHoldTurnsOnTheSameThresholdTheCloudDoes()
    {
        double under = MushroomCloud.ThresholdKg - 1.0;

        Assert.Equal(ChaseView.MinLingerSeconds, ChaseView.LingerSeconds(under));
        Assert.True(ChaseView.LingerSeconds(MushroomCloud.ThresholdKg) > ChaseView.MinLingerSeconds);
    }
}
