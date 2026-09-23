using Brutal.Numerics;
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

    /// <summary>
    /// Clear of its fireball, and for a nuclear charge clear of its cloud as well. The B61 at its
    /// lowest yield is both: six fireball radii is about 1.05 km and the cloud that stands there is
    /// 1.31 km tall, so the cloud is what sets the distance and the fireball rule is still met.
    /// </summary>
    [Fact]
    public void ABigWarheadIsWatchedFromClearOfItsFireball()
    {
        double bomb = ChaseView.StopShortMetres(300_000.0);

        Assert.True(bomb >= 6.0 * Warhead.FireballRadius(300_000.0) - 1e-6);
        Assert.Equal(1.6 * MushroomCloud.DrawnCloudTop(0.3), bomb, 6);
    }

    private static readonly double3 Level800 = new(800.0, 0.0, 0.0);
    private static readonly double3 Down = new(0.0, 0.0, -9.81);

    [Fact]
    public void TheChaseStopsOnlyOnceWhatIsLeftIsInsideTheDistance()
    {
        // A 5-inch shell at 800 m/s: 160 m to go rides on, 56 m to go stops.
        Assert.False(ChaseView.StopsShort(timeToGo: 0.2, Level800, Down, chargeKg: 3.3));
        Assert.True(ChaseView.StopsShort(timeToGo: 0.07, Level800, Down, chargeKg: 3.3));
    }

    [Fact]
    public void NothingToCountDownToNeverStopsIt()
    {
        Assert.False(ChaseView.StopsShort(double.NaN, Level800, Down, 3.3));
        Assert.False(ChaseView.StopsShort(-1.0, Level800, Down, 3.3));
    }

    /// <summary>
    /// A store thrown upwards is barely moving at the top of its climb, so the time to go times the
    /// speed there reads as nothing left. It has its whole fall to go: 710 m and 12 s, from a B61
    /// thrown 510 m up from a craft climbing at 100 m/s at 200 m.
    /// </summary>
    [Fact]
    public void AStoreAtTheTopOfItsClimbIsNotAlmostThere()
    {
        double3 atTheTop = new(4.0, 0.0, 0.0);
        double fall = Math.Sqrt(2.0 * 710.0 / 9.81);

        Assert.True(fall * 4.0 < ChaseView.StopShortMetres(3.3));
        Assert.False(ChaseView.StopsShort(fall, atTheTop, Down, chargeKg: 3.3));
        Assert.InRange(Vec.Len(ChaseView.ArrivalFromRound(fall, atTheTop, Down)), 700.0, 730.0);
    }

    /// <summary>
    /// A bomb whose whole flight is inside the distance it is watched from is ridden down to its last
    /// seconds rather than let go of at release, which left the view kilometres from it for the whole
    /// of a thrown-up drop.
    /// </summary>
    [Fact]
    public void AFlightInsideTheDistanceIsRiddenToItsLastSeconds()
    {
        const double b61 = 300_000.0;
        double3 falling = new(0.0, 0.0, -60.0);

        Assert.False(ChaseView.StopsShort(ChaseView.WatchSeconds(b61) + 6.0, falling, Down, b61));
        Assert.True(ChaseView.StopsShort(ChaseView.WatchSeconds(b61) - 0.5, falling, Down, b61));
    }

    /// <summary>A bigger warhead is let go of earlier, and a conventional round is left to the distance.</summary>
    [Fact]
    public void TheWatchGrowsWithTheYield()
    {
        Assert.Equal(ChaseView.MinWatchSeconds, ChaseView.WatchSeconds(3.3));
        Assert.Equal(ChaseView.MinWatchSeconds, ChaseView.WatchSeconds(MushroomCloud.ThresholdKg - 1.0));
        Assert.InRange(ChaseView.WatchSeconds(0.3e6), 4.0, 4.5);
        Assert.InRange(ChaseView.WatchSeconds(10.0e6), 13.0, 14.0);
        Assert.InRange(ChaseView.WatchSeconds(340.0e6), 43.0, 44.0);

        // A 340 kt bomb falling at 250 m/s 30 s out is inside its watch; a 0.3 kt one is not.
        double3 falling = new(0.0, 0.0, -250.0);
        Assert.True(ChaseView.StopsShort(30.0, falling, Vec.Zero, 340.0e6));
        Assert.False(ChaseView.StopsShort(30.0, falling, Vec.Zero, 0.3e6));
    }

    /// <summary>
    /// Held a few hundred metres off, the middle of a 0.3 kt column is nearly straight up and the
    /// view ends on whatever is flying overhead. The burst is kept in the frame instead.
    /// </summary>
    [Fact]
    public void AHeldEyeKeepsTheBurstInFrame()
    {
        double lift = ChaseView.CloudAimHeightMetres(0.3e6);
        double3 toBurst = new(300.0, 0.0, -150.0);
        double3 toColumn = toBurst + new double3(0.0, 0.0, lift);

        double3 near = ChaseView.WatchBurstForward(toBurst, toColumn, 60.0);
        Assert.True(Vec.AngleBetween(Vec.Unit(toColumn), Vec.Unit(toBurst)) > double.DegreesToRadians(40.0));
        Assert.Equal(18.0, double.RadiansToDegrees(Vec.AngleBetween(near, Vec.Unit(toBurst))), 6);
        Assert.True(near.Z > Vec.Unit(toBurst).Z);

        // From as far as the column height was chosen at, it is looked at as it always was.
        double3 farBurst = new(2000.0, 0.0, -150.0);
        double3 farColumn = farBurst + new double3(0.0, 0.0, lift);
        Assert.Equal(Vec.Unit(farColumn), ChaseView.WatchBurstForward(farBurst, farColumn, 60.0));
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
        Assert.Equal(ChaseView.MinLingerSeconds, InAir(chargeKg));
    }

    /// <summary>A burst that grows a cloud is held until the cloud stops changing shape.</summary>
    [Theory]
    [InlineData(MushroomCloud.ThresholdKg)]
    [InlineData(300_000.0)]       // the B61 at its lowest yield
    [InlineData(20_000_000.0)]    // a Mk 21 at 20 kt
    public void ANuclearBurstIsHeldForTheCloudsRise(double chargeKg)
    {
        Assert.Equal(MushroomCloud.RiseSeconds, InAir(chargeKg));
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

        Assert.Equal(ChaseView.MinLingerSeconds, InAir(under));
        Assert.True(InAir(MushroomCloud.ThresholdKg) > ChaseView.MinLingerSeconds);
    }

    /// <summary>
    /// A charge that grows a cloud is watched from far enough to see the cloud, which is the thing
    /// it made. Six fireball radii clears the fireball and leaves the camera under a cloud four
    /// times taller than its own distance — and inside the radius that same charge is lethal to.
    /// </summary>
    [Theory]
    [InlineData(0.3)]
    [InlineData(20.0)]
    [InlineData(340.0)]
    public void ANuclearBurstIsWatchedFromFarEnoughToSeeItsCloud(double kt)
    {
        double chargeKg = kt * 1.0e6;
        double stand = ChaseView.StopShortMetres(chargeKg);

        // The whole cloud is in front of the camera rather than over it.
        Assert.True(stand > MushroomCloud.DrawnCloudTop(kt));

        // And the camera is not standing where the warhead would kill it.
        Assert.True(stand > Warhead.LethalRadius(chargeKg));
    }

    /// <summary>A conventional round is unaffected: its stand-off is still the fireball's.</summary>
    [Fact]
    public void AConventionalBurstKeepsItsOwnStandOff()
    {
        double justUnder = MushroomCloud.ThresholdKg - 1.0;

        Assert.Equal(Math.Max(60.0, 6.0 * Warhead.FireballRadius(justUnder)),
                     ChaseView.StopShortMetres(justUnder), 6);
    }

    /// <summary>
    /// The case every test above was written against: a burst on the ground of a body with air,
    /// where the thing being watched is the cloud. The airless ones are in
    /// <see cref="AirlessBurstTests"/>, which is also where the gravity comes in.
    /// </summary>
    private static double InAir(double chargeKg)
        => ChaseView.LingerSeconds(chargeKg, hasAir: true, 9.81, 0.0);
}
