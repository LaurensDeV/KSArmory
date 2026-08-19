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


    /// <summary>
    /// Where a plane change belongs, checked rather than assumed. Reaching a target off to the side
    /// is cheapest from the node — a quarter of an orbit before the target — and the burn there is
    /// almost entirely normal to the plane being flown in.
    ///
    /// <para>That is a textbook result and the search is not told it. It falls out of costing
    /// departures and taking the cheapest, which is the only reason it is worth testing: if the
    /// search ever stops finding it, the shots get expensive and nothing else says why.</para>
    /// </summary>
    [Theory]
    [InlineData(45.0)]
    [InlineData(90.0)]
    [InlineData(135.0)]
    public void ThePlaneChangeIsCheapestAQuarterOrbitBeforeTheTarget(double targetLongitudeDeg)
    {
        // No planet rotation, so the only thing varying is where along the orbit the burn happens.
        BallisticBody earth = new(Mu, R, new double3(0, 0, 1), 0.0);

        double altitude = 300_000.0;
        double3 position = new(R + altitude, 0, 0);
        double speed = Math.Sqrt(Mu / (R + altitude));
        double3 velocity = new(0, speed, 0);

        double3 target = At(20.0 * Math.PI / 180.0, targetLongitudeDeg * Math.PI / 180.0);
        double period = Kepler.PeriodSeconds(Mu, position, velocity);

        double best = double.PositiveInfinity;
        double bestBefore = 0.0;
        double bestNormalShare = 0.0;

        for (int i = 0; i <= 360; i++)
        {
            double wait = period * i / 360.0;
            if (!Kepler.TryCoast(Mu, position, velocity, wait, out double3 r, out double3 v)) continue;
            if (!BallisticArc.TryCheapest(earth, r, v, target, out BallisticArc.Solution arc)) continue;

            double3 change = arc.RequiredVelocityCci - v;
            double cost = Vec.Len(change);
            if (cost >= best) continue;

            best = cost;
            double departure = Math.Atan2(r.Y, r.X) * 180.0 / Math.PI;
            bestBefore = ((targetLongitudeDeg - departure) % 360.0 + 360.0) % 360.0;
            bestNormalShare = Math.Abs(Vec.Dot(Vec.Unit(change), Vec.Unit(Vec.Cross(r, v))));
        }

        Assert.True(Math.Abs(bestBefore - 90.0) < 10.0,
                    $"cheapest departure was {bestBefore:F0} deg before the target, not a quarter orbit");

        Assert.True(bestNormalShare > 0.9,
                    $"only {bestNormalShare * 100:F0}% of the burn was normal to the plane");
    }
}
