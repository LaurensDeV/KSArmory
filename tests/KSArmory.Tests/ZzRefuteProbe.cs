using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

public sealed class ZzRefuteProbe(ITestOutputHelper o)
{
    private const double Re = 6_371_000.0;
    private const double Mu = 3.986004418e14;

    private sealed class SphereGround : IGroundTest
    {
        public int Calls;
        public bool TryGround(double3 p, out double3 centre, out double radius)
        {
            Calls++;
            // Mimic a little of the real lookup's shape without KSA: a normalise + a few flops.
            double3 dir = Vec.Unit(p);
            centre = new double3(0, 0, 0);
            radius = Re + 100.0 * dir.Y;
            return true;
        }
    }

    private static double Density(double3 p)
    {
        double h = Vec.Len(p) - Re;
        return h <= 0 ? 1.0 : Math.Exp(-h / 8500.0);
    }

    private static double3 Gravity(double3 p)
    {
        double r = Vec.Len(p);
        return p * (-Mu / (r * r * r));
    }

    private void One(string label, double altitude, double speed, double climbFraction)
    {
        double3 release = new(0, Re + altitude, 0);
        // Mostly downrange with a climb component, as a pitch programme gives.
        double3 vel = new(speed * Math.Sqrt(Math.Max(0, 1 - climbFraction * climbFraction)),
                          speed * climbFraction, 0);

        var raw = new SphereGround();
        var ground = new CoarseGroundTest(raw);
        List<double3> path = [];

        // warm
        ground.Reset(); raw.Calls = 0;
        BombSight.TryPredict(release, vel, Vec.Zero, Arsenal.ReentryVehicleMk21,
                             Gravity, Density, ground, 0.05, path, out _);

        int reps = 20;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool ok = false;
        for (int i = 0; i < reps; i++)
        {
            ground.Reset();
            ok = BombSight.TryPredict(release, vel, Vec.Zero, Arsenal.ReentryVehicleMk21,
                                      Gravity, Density, ground, 0.05, path, out _);
        }
        sw.Stop();

        o.WriteLine($"{label}: {sw.Elapsed.TotalMilliseconds / reps:F3} ms/solve, "
                    + $"outer steps {path.Count - 1}, ground samples {ground.Sampled}, "
                    + $"inner ground calls {raw.Calls}, hit={ok}");
    }

    [Fact]
    public void Cost()
    {
        o.WriteLine($"SubStep={Arsenal.ReentryVehicleMk21.SubStep} MaxSubSteps={Arsenal.ReentryVehicleMk21.MaxSubSteps} "
                    + $"Powered={Arsenal.ReentryVehicleMk21.Powered} HitsTerrain={Arsenal.ReentryVehicleMk21.HitsTerrain}");
        One("pad-ish     2 km / 100 m/s", 2_000, 100, 0.9);
        One("early ascent 5 km / 300 m/s", 5_000, 300, 0.8);
        One("pitch prog  60 km / 2000 m/s", 60_000, 2000, 0.6);
        One("high       200 km / 5000 m/s", 200_000, 5000, 0.3);
        One("coast     1000 km / 7000 m/s", 1_000_000, 7000, 0.1);
    }
}
