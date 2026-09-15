using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The bomb sight on a world that turns like Earth, over ground that rises and falls. A store released 5 km over the
/// equator at 250 m/s is flown as a real round, inertially, over terrain turning under it; the ring drawn at release
/// has to sit on the ground it strikes, turned back to where that ground is at release. A sight holding the planet
/// still is flown beside it, to show the turn is large enough to see.
/// </summary>
public class BombSightSpinTests(ITestOutputHelper output)
{
    private const double Step = 0.05;
    private const double SurfaceGravity = 9.80665;
    private const double Radius = 6_371_000.0;
    private const double ReleaseSpeed = 250.0;

    // Spin about +Y, so a release over +Z is over the equator and the ground under it moves east along +X.
    private static readonly double3 Spin = new(0, 7.2921e-5, 0);
    private static readonly double3 Release = new(0, 0, Radius + 5_000.0);

    // No air, so the planet's turn is the only thing that can move the ring.
    private static readonly MunitionProfile Store = new()
    {
        Name = "SpinStore",
        DisplayName = "airless store",
        LaunchSpeed = 0f,
        BoostSeconds = 0f,
        BoostAccel = 0f,
        DragK = 0f,
        MaxFlightSeconds = 300f,
        FuseRadius = 0f,
        FuseArmSeconds = 0f,
        ChargeKg = 1f,
        HitsTerrain = true,
        Guidance = GuidanceMode.None,
    };

    // Ridges running north and south, 150 m high and 20 km apart, fixed to the planet. The turn carries 15 km of
    // them past the frame's origin in the half-minute of a fall, so ground read in the wrong place stops the store on
    // the wrong height.
    private static double SurfaceRadius(double eastRadians)
        => Radius + (150.0 * Math.Sin(2.0 * Math.PI * eastRadians * Radius / 20_000.0));

    // The ground as the planet had it at release, which is all a sight has.
    private sealed class GroundAtRelease : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = SurfaceRadius(Math.Atan2(positionEcl.X, positionEcl.Z));
            return true;
        }
    }

    // The ground as it is, turned on since release.
    private sealed class TurningGround : IGroundTest
    {
        public double Seconds;

        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = SurfaceRadius(Math.Atan2(positionEcl.X, positionEcl.Z) - (Spin.Y * Seconds));
            return true;
        }
    }

    private static double3 GravityAt(double3 p) => p * (-SurfaceGravity * Radius * Radius / Math.Pow(Vec.Len(p), 3));

    private static double3 GroundVelocityAt(double3 p) => Vec.Cross(Spin, p);

    // Where a point turning with the planet was, seconds earlier.
    private static double3 TurnBack(double3 p, double seconds)
    {
        double a = Spin.Y * seconds;
        return new double3((p.X * Math.Cos(a)) - (p.Z * Math.Sin(a)), p.Y, (p.X * Math.Sin(a)) + (p.Z * Math.Cos(a)));
    }

    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(0.0, 1.0)]
    public void TheRingSitsOnTheGroundTheStoreStrikes(double east, double north)
    {
        double3 overGround = Vec.Unit(new double3(east, north, 0)) * ReleaseSpeed;
        double3 groundVelocity = GroundVelocityAt(Release);

        var turning = new TurningGround();
        var round = new Slug(Release, groundVelocity + overGround, null, 0, Release, Vec.Zero)
        {
            Munition = Store,
            Ground = turning,
            GravityAt = (p, _) => GravityAt(p),
            AirDensityAt = (_, _) => 0.0,
        };

        double flown = 0.0;
        while (round.State == RoundState.Flying && flown < 200.0)
        {
            flown += Step;
            turning.Seconds = flown;
            round.Update(Step, null, GravityAt(round.PositionEcl), Vec.Zero, Release, Store, 0.0);
        }

        Assert.True(round.HitGround, "the store never came down");
        double fall = flown + round.DetonationElapsedInFrame;
        double3 struck = TurnBack(round.PositionEcl, fall);

        Assert.True(BombSight.TryPredict(Release, overGround, groundVelocity, Vec.Cross(Spin, groundVelocity), Vec.Zero,
                                         GroundVelocityAt, Store, GravityAt, _ => 0.0, new GroundAtRelease(), Step,
                                         new List<double3>(), out double3 ring));
        Assert.True(BombSight.TryPredict(Release, overGround, groundVelocity, Vec.Zero, groundVelocity,
                                         _ => groundVelocity, Store, GravityAt, _ => 0.0, new GroundAtRelease(), Step,
                                         new List<double3>(), out double3 still));

        double miss = Vec.Len(ring - struck);
        double held = Vec.Len(still - struck);
        output.WriteLine($"{(east > 0.0 ? "east" : "north")}: {fall:F1} s of fall, ring {miss:F2} m off, "
                         + $"held still {held:F2} m off");

        Assert.True(miss < 3.0, $"the ring is {miss:F2} m from where the store strikes");
        Assert.True(held > 10.0, $"holding the planet still is only {held:F2} m off, so this proves nothing");
    }
}
