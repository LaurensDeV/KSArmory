using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The ring a map draws round a rocket: how far it could still throw a warhead.
///
/// <para>A ceiling rather than a promise — it prices the velocity still to be gained and knows
/// nothing about what the climb costs — so what these pin is that it behaves like a bound: it
/// never shrinks when there is more propellant, it is zero when there is none, and it does not
/// claim more than half a turn, which is as far as a ballistic shot goes.</para>
/// </summary>
public class BoosterReachTests
{
    private static readonly BallisticBody Earth = new(3.986004418e14, 6_371_000.0,
                                                      new double3(0, 0, 1), 7.2921159e-5);

    private static readonly double3 OnTheEquator = new(6_371_000.0, 0, 0);

    private static double Reach(double deltaV, double minArrivalDeg = 0.0)
        => BoosterReach.CeilingMetres(Earth, OnTheEquator, Earth.GroundVelocityCci(OnTheEquator),
                                      deltaV, minArrivalDeg);

    [Fact]
    public void NoPropellantReachesNowhere()
    {
        Assert.Equal(0.0, Reach(0.0));
        Assert.Equal(0.0, Reach(double.NaN));
    }

    [Fact]
    public void AnUnusableBodyReachesNowhere()
    {
        BallisticBody none = new(0.0, 0.0, new double3(0, 0, 1), 0.0);

        Assert.Equal(0.0, BoosterReach.CeilingMetres(none, OnTheEquator, double3.Zero, 9000.0, 0.0));
    }

    [Fact]
    public void MorePropellantNeverReachesLess()
    {
        double last = -1.0;

        for (double dv = 500.0; dv <= 12_000.0; dv += 500.0)
        {
            double got = Reach(dv);

            Assert.True(got >= last, $"{dv:F0} m/s reached {got / 1000.0:F0} km, "
                                     + $"less than the step before");
            last = got;
        }
    }

    [Fact]
    public void ItNeverClaimsMoreThanHalfATurn()
    {
        // Past half a turn the short way round is the other side of the same arc, so there is no
        // such thing as a longer ballistic shot.
        double half = System.Math.PI * Earth.SurfaceRadius;

        Assert.True(Reach(50_000.0) <= half + 1.0);
    }

    [Fact]
    public void AnIntercontinentalStackReachesIntercontinentally()
    {
        // Enough to be in no doubt it is a long-range shot, and not so much that it is orbital.
        double got = Reach(7_000.0);

        Assert.True(got > 2_000_000.0, $"7 km/s reached only {got / 1000.0:F0} km");
    }

    [Fact]
    public void AShortRangeStackIsHeldToAShortRange()
    {
        Assert.True(Reach(1_000.0) < Reach(7_000.0));
    }

    [Fact]
    public void DemandingASteeperArrivalNeverReachesFurther()
    {
        // The arrival floor is a bound on the search, so it can only cost range.
        Assert.True(Reach(7_000.0, minArrivalDeg: 35.0) <= Reach(7_000.0) + 1.0);
    }
}
