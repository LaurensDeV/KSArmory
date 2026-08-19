using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Why an orbital shot can cost four kilometres a second when a deorbit costs a hundred metres:
/// the target is not in the plane the vehicle is already flying in, and no amount of waiting or
/// propellant framing says so.
/// </summary>
public class OrbitPlaneTests
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static double3 At(double lat, double lon)
        => new(R * Math.Cos(lat) * Math.Cos(lon), R * Math.Cos(lat) * Math.Sin(lon), R * Math.Sin(lat));

    [Fact]
    public void ATargetOnTheGroundTrackIsInPlane()
    {
        double3 position = new(R + 300_000.0, 0, 0);
        double3 velocity = new(0, Math.Sqrt(Mu / (R + 300_000.0)), 0);

        double off = OrbitPlane.OffPlaneRadians(position, velocity, At(0.0, 1.2));

        Assert.True(off * 180.0 / Math.PI < 0.001, $"{off * 180.0 / Math.PI:F3} degrees off plane");
    }

    /// <summary>
    /// An equatorial orbit cannot reach a temperate latitude at all without turning the plane, and
    /// the angle it is off by is exactly the target's latitude.
    /// </summary>
    [Theory]
    [InlineData(20.0)]
    [InlineData(47.6)]
    [InlineData(70.0)]
    public void FromAnEquatorialOrbitTheTargetsLatitudeIsTheWholeGap(double latitudeDeg)
    {
        double3 position = new(R + 300_000.0, 0, 0);
        double3 velocity = new(0, Math.Sqrt(Mu / (R + 300_000.0)), 0);

        double off = OrbitPlane.OffPlaneRadians(position, velocity, At(-latitudeDeg * Math.PI / 180.0, -1.17));

        Assert.Equal(latitudeDeg, off * 180.0 / Math.PI, 1);
    }

    /// <summary>
    /// And the cost explains the number on the panel. A shot reported at 3.7 km/s from a 207 km
    /// orbit is not a deorbit that went wrong; it is a twenty-seven degree plane change.
    /// </summary>
    [Fact]
    public void TheCostAccountsForAnOtherwiseInexplicableBurn()
    {
        double speed = 7_790.0;
        double estimate = OrbitPlane.PlaneChangeCost(speed, 27.5 * Math.PI / 180.0);

        Assert.True(Math.Abs(estimate - 3_700.0) < 150.0,
                    $"a 27.5 degree turn at {speed:F0} m/s came out at {estimate:F0} m/s");
    }

    [Fact]
    public void NothingDegenerateReturnsAnAngle()
    {
        Assert.Equal(0.0, OrbitPlane.OffPlaneRadians(Vec.Zero, Vec.Zero, Vec.Zero));
        Assert.Equal(0.0, OrbitPlane.PlaneChangeCost(7800.0, 0.0), 9);
    }


    /// <summary>
    /// The point of searching more than one revolution. A target off the ground track costs a plane
    /// change to reach <em>now</em> — kilometres a second — and costs a deorbit if you wait for the
    /// planet to bring it under the plane. One revolution cannot see that: the ground turns about
    /// twenty-two degrees in ninety minutes, so within one orbit the target is still off the track
    /// and the only answer available is the expensive one.
    /// </summary>
    [Fact]
    public void WaitingForThePlanetToTurnMakesAnOffTrackTargetCheap()
    {
        BallisticBody earth = new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

        // A 50 degree orbit, which reaches the target's latitude but is not over it yet.
        double altitude = 300_000.0;
        double3 position = new(R + altitude, 0, 0);
        double speed = Math.Sqrt(Mu / (R + altitude));
        double3 velocity = new(0, speed * Math.Cos(0.87), speed * Math.Sin(0.87));

        double3 target = At(-0.72, 2.4);

        Assert.True(BurnWindow.TryFind(earth, position, velocity, target, out BurnWindow.Window window));

        Assert.True(double.IsFinite(window.CostIfLeavingNow));
        Assert.True(window.Cost < window.CostIfLeavingNow,
                    $"waiting cost {window.Cost:F0} m/s against {window.CostIfLeavingNow:F0} now");

        // And the saving is the kind that matters: a plane change's worth, not a rounding.
        Assert.True(window.Saving > 1_000.0,
                    $"only saved {window.Saving:F0} m/s by waiting {window.WaitSeconds / 60.0:F0} min");

        // It had to look past one revolution to find it.
        double period = Kepler.PeriodSeconds(Mu, position, velocity);
        Assert.True(window.WaitSeconds > period,
                    $"found it {window.WaitSeconds / 60.0:F0} min out, inside one {period / 60.0:F0} min orbit");
    }
}
