using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// A warhead flown about a planet that is where KSA's is: 1.5e11 m out in the ecliptic, moving at
/// 29.8 km/s, and spinning.
/// </summary>
/// <remarks>
/// <para>Every other rig flies a planet at the origin, where anything carried by the planet's own
/// travel is identically zero. Two terms of the flown walk were exactly that and no rig could show
/// either (<c>docs/ACCURACY-PLAN.md</c> 3el).</para>
///
/// <para><b>The ecliptic accumulator is scatter, not the walk.</b> Carrying the position at 1.5e11 m,
/// where a double's step is 30.5 µm, moves a landing by up to ±11 mm with a jittering frame and
/// averages near zero at every frame rate.</para>
///
/// <para><b>The air read against where the planet will be is a bias proportional to the frame.</b>
/// The spin's radius measured to the end-of-frame body sample carries a frame of its travel, and the
/// false wind that makes tilts the drag one way on every frame.</para>
/// </remarks>
public class MovingPlanetProbe(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static readonly double3 SunSide = new(1.2e11, -8.93e10, 0.0);
    private static readonly double3 Orbital = new(1.782e4, 2.395e4, 0.0);
    private static readonly double3 Spin =
        new double3(0.0, -Math.Sin(23.44 * Math.PI / 180.0), Math.Cos(23.44 * Math.PI / 180.0)) * 7.2921159e-5;

    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0);

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    public enum Air { Still, BackDated, HeldFromPreStep }

    /// <summary>A planet on a straight line through the ecliptic, sampled at each frame's end as KSA samples it.</summary>
    private sealed class World(double3 origin, double3 velocity) : IGroundTest
    {
        public double EndOfFrame { get; set; }

        public double3 BodyAt(double secondsIntoFrame) => origin + velocity * (EndOfFrame + secondsIntoFrame);

        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = BodyAt(0.0);
            surfaceRadius = R;
            return true;
        }
    }

    private static double3 Release(out double3 from)
    {
        from = new double3(R + 877_000.0, 0, 0);
        const double Range = 1_500_000.0;
        double lat = -26.485 * Math.PI / 180.0;
        double3 target = new(R * Math.Cos(Range / R), R * Math.Sin(Range / R) * Math.Cos(lat),
                             R * Math.Sin(Range / R) * Math.Sin(lat));
        double speed = Math.Sqrt(Mu / (R + 877_000.0));
        double3 coasting = new(0, speed * Math.Cos(lat), speed * Math.Sin(lat));
        Assert.True(BallisticArc.TryCheapest(Earth, from, coasting, target, out BallisticArc.Solution s));
        return s.RequiredVelocityCci;
    }

    /// <summary>Where the round lands relative to the planet, with the game's back-dated lookups.</summary>
    private static (double3 At, double3 Velocity) Fly(double3 origin, double3 bodyVelocity,
                                                     Func<double> frameSeconds, Air air)
    {
        double3 v = Release(out double3 from);

        World world = new(origin, bodyVelocity);
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;

        Slug round = new(origin + from, bodyVelocity + v, null, 1, origin + from, Vec.Zero)
        {
            Munition = warhead,
            ResampleGroundNearImpact = true,
            SecondOrder = true,
            StopOnTheTerrain = true,
            GroundQueryAtOwnEpoch = true,
            AirVelocityAtOwnSubStep = air == Air.BackDated,
        };

        double3 Pull(double3 p, double s)
        {
            double3 toBody = world.BodyAt(s) - p;
            return Vec.Unit(toBody) * (Mu / Vec.Len2(toBody));
        }

        RoundFields fields = new(
            GravityAt: Pull,
            AirDensityAt: (p, s) => DensityAt(p - world.BodyAt(s)),
            Ground: world,
            GroundCentreDriftAt: s => bodyVelocity * s,
            GroundQueryDriftAt: (_, s) => bodyVelocity * s,
            AirVelocityAt: air == Air.BackDated ? (p, s) => bodyVelocity + Vec.Cross(Spin, p - world.BodyAt(s)) : null);

        double t = 0.0;
        for (int i = 0; i < 2_000_000 && round.State == RoundState.Flying; i++)
        {
            double dt = frameSeconds();
            t += dt;
            world.EndOfFrame = t;

            double3 held = air switch
            {
                Air.Still => bodyVelocity,
                Air.HeldFromPreStep => bodyVelocity + Vec.Cross(Spin, round.PositionEcl - world.BodyAt(0.0)),
                _ => bodyVelocity + Vec.Cross(Spin, round.PositionEcl - world.BodyAt(-dt)),
            };

            RoundDriver.Fly(round, dt, null, Pull(round.PositionEcl, 0.0), held, Vec.Zero, warhead, 0.0, fields);
        }

        Assert.True(round.HitGround);

        double3 body = world.BodyAt(round.DetonationElapsedInFrame);
        return (round.PositionEcl - body, round.VelocityEcl - bodyVelocity);
    }

    [Fact]
    public void TheEclipticAccumulatorScattersWithAJitteringFrameAndDoesNotWalk()
    {
        Out.WriteLine("frames uniform over [0.5, 1.5] x the mean; each seed flies one frame sequence about the origin "
                      + "and in the ecliptic; landing difference in mm\n");

        List<double> all = [];
        foreach (double ms in new[] { 26.0, 48.0 })
        {
            List<double3> rows = [];
            for (int seed = 0; seed < 4; seed++)
            {
                Random a = new(1000 + seed), b = new(1000 + seed);
                (double3 o, double3 ov) = Fly(Vec.Zero, Vec.Zero, () => ms * (0.5 + a.NextDouble()) / 1000.0, Air.Still);
                (double3 e, _) = Fly(SunSide, Orbital, () => ms * (0.5 + b.NextDouble()) / 1000.0, Air.Still);
                Assert.True(ArrivalFrame.TryAt(o, ov, out ArrivalFrame f));
                double3 d = f.Resolve(e - o) * 1000.0;
                rows.Add(d);
                all.Add(Vec.Len(d));
            }

            Out.WriteLine($"{ms,5:F0} ms: down {string.Join(" ", rows.Select(r => $"{r.Y,6:F2}"))}   "
                          + $"cross {string.Join(" ", rows.Select(r => $"{r.Z,6:F2}"))}");
        }

        Assert.All(all, d => Assert.InRange(d, 0.0, 30.0));
    }

    [Fact]
    public void AirReadAgainstTheEndOfFrameBodyIsABiasProportionalToTheFrame()
    {
        Out.WriteLine("held from the pre-step position against the end-of-frame body, minus the back-dated per-sub-step "
                      + "air, arrival frame, mm\n");

        List<(double Ms, double Miss)> rows = [];
        foreach (double ms in new[] { 10.0, 20.0, 40.0 })
        {
            (double3 truth, double3 tv) = Fly(SunSide, Orbital, () => ms / 1000.0, Air.BackDated);
            (double3 held, _) = Fly(SunSide, Orbital, () => ms / 1000.0, Air.HeldFromPreStep);

            Assert.True(ArrivalFrame.TryAt(truth, tv, out ArrivalFrame f));
            double3 d = f.Resolve(held - truth) * 1000.0;
            rows.Add((ms, Vec.Len(d)));
            Out.WriteLine($"{ms,5:F0} ms: down {d.Y,7:F2}  cross {d.Z,7:F2}");
        }

        Assert.True(rows[0].Miss > 5.0, $"at 10 ms the term should be millimetres; {rows[0].Miss:F2}");
        Assert.InRange(rows[2].Miss / rows[0].Miss, 3.0, 5.0);
    }
}
