using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Whether a released warhead lands where its own fall says it should — against the one reference
/// that is exact rather than converged: a point mass in vacuum, where the fall is a conic and
/// <see cref="Kepler"/> says where it meets the sphere.
///
/// <para><b>The first-order step lands metres short, and the sub-step is the size of it.</b> Moving
/// on the velocity a sub-step ends with is leapfrog started with an extra half-kick of <c>a·h/2</c>,
/// held for the whole fall. <see cref="Slug.SecondOrder"/> reads gravity half a sub-step on and moves
/// on the mean of the two velocities, which is leapfrog itself. <c>docs/ACCURACY-PLAN.md</c> 3cu.</para>
/// </summary>
public sealed class RoundIntegratorOrderTests
{
    private readonly ITestOutputHelper _out;
    public RoundIntegratorOrderTests(ITestOutputHelper o) => _out = o;

    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static readonly BallisticBody Body = new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    // A flown release state, 943 km up and descending at 1,436 m/s: a 380 s fall arriving at ~32°.
    private static readonly double3 P0 = new(3334368.3, -6358379.3, -1399776.7);
    private static readonly double3 V0 = new(95.8429, 2608.2053, -4113.1550);

    private sealed class Ball : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = R;
            return true;
        }
    }

    private static MunitionProfile Vacuum(float subStepSeconds) => new()
    {
        Name = "TESTRV",
        DisplayName = "test reentry vehicle",
        Guidance = GuidanceMode.None,
        LaunchSpeed = 0f,
        BoostSeconds = 0f,
        BoostAccel = 0f,
        MaxFlightSeconds = 7200f,
        DragK = 0f,
        FuseRadius = 0f,
        ChargeKg = 300f,
        HitsTerrain = true,
        SubStepSeconds = subStepSeconds,
    };

    private static double3 Fly(bool secondOrder, float subStepSeconds, double frameSeconds = 0.017)
    {
        var round = new Slug(P0, V0, null, 1, P0, Vec.Zero)
        {
            Munition = Vacuum(subStepSeconds),
            Ground = new Ball(),
            GravityAt = (position, _) => Body.GravityCci(position),
            SecondOrder = secondOrder,
        };

        for (int i = 0; i < 1_000_000 && round.State == RoundState.Flying; i++)
        {
            round.Update(frameSeconds, null, Body.GravityCci(round.PositionEcl), Vec.Zero, Vec.Zero,
                         round.Munition);
        }

        Assert.True(round.HitGround, "the round never reached the ground");
        return round.PositionEcl;
    }

    // Where the conic meets the sphere, bisected to far below anything the assertions resolve.
    private static (double3 Point, double3 Velocity) Exact()
    {
        double lo = 0.0;
        double hi = 1.0;

        while (Above(hi))
        {
            lo = hi;
            hi += 1.0;
            Assert.True(hi < 7200.0, "the conic never met the sphere");
        }

        for (int i = 0; i < 80; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Above(mid)) lo = mid;
            else hi = mid;
        }

        Assert.True(Kepler.TryCoast(Mu, P0, V0, hi, out double3 point, out double3 velocity));
        return (point, velocity);

        static bool Above(double t)
            => Kepler.TryCoast(Mu, P0, V0, t, out double3 r, out _) && Vec.Len(r) > R;
    }

    // Signed along the ground track at impact: negative is short.
    private static double Downrange(double3 landed, double3 point, double3 velocity)
    {
        double3 up = Vec.Unit(point);
        double3 along = Vec.Unit(velocity - up * Vec.Dot(velocity, up));
        return Vec.Dot(landed - point, along);
    }

    [Fact]
    public void ASecondOrderWarheadLandsWhereTheConicDoes()
    {
        var (point, velocity) = Exact();
        double3 landed = Fly(secondOrder: true, Arsenal.ReentryVehicleMk21.SubStepSeconds);

        double gap = Vec.Len(landed - point);
        _out.WriteLine($"second order: {gap:F3} m from the conic, {Downrange(landed, point, velocity):F3} m downrange");

        Assert.True(gap < 0.1, $"{gap:F3} m from where the conic meets the sphere");
    }

    [Fact]
    public void AFirstOrderWarheadLandsMetresShortOfTheConic()
    {
        var (point, velocity) = Exact();
        double3 landed = Fly(secondOrder: false, Arsenal.ReentryVehicleMk21.SubStepSeconds);

        double downrange = Downrange(landed, point, velocity);
        _out.WriteLine($"first order: {downrange:+0.000;-0.000} m downrange of the conic");

        // What the flown walk is made of. If this stops failing the fixture has stopped seeing it.
        Assert.True(downrange < -1.0, $"{downrange:F3} m downrange, where the fault is metres short");
    }

    [Fact]
    public void HalvingTheSubStepBarelyMovesASecondOrderWarhead()
    {
        double3 whole = Fly(secondOrder: true, 0.001f);
        double3 halved = Fly(secondOrder: true, 0.0005f);

        double moved = Vec.Len(whole - halved);
        _out.WriteLine($"second order: halving the sub-step moves the landing {moved:F3} m");

        Assert.True(moved < 0.05, $"halving the sub-step moved the landing {moved:F3} m");
    }

    [Fact]
    public void HalvingTheSubStepMovesAFirstOrderWarheadByHalfItsError()
    {
        double3 whole = Fly(secondOrder: false, 0.001f);
        double3 halved = Fly(secondOrder: false, 0.0005f);

        double moved = Vec.Len(whole - halved);
        _out.WriteLine($"first order: halving the sub-step moves the landing {moved:F3} m");

        // The error is linear in the step, which is what a first-order method is.
        Assert.True(moved > 0.5, $"halving the sub-step moved the landing only {moved:F3} m");
    }
}
