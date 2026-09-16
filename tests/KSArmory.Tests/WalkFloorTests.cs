using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// How large the walk's irreducible scatter is, on ground shaped the way the engine's is.
/// </summary>
/// <remarks>
/// <para><see cref="TerrainStaircaseTests"/> establishes the mechanism and that no sub-step reaches
/// the scatter, but its relief is a single 40 m sinusoid stepped through uniformly — which
/// <b>aliases</b>, and its absolute sd moved by 3x with the sampling
/// (<c>docs/ACCURACY-PLAN.md</c> 3eh). Two changes make the number mean something: relief summed
/// over octaves as <c>EarthErosion</c> is, and release states drawn at <b>random</b> so there is no
/// step to alias with.</para>
///
/// <para>The instrument is checked before it is used: two independent seeds must agree.</para>
/// </remarks>
public class WalkFloorTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0);

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 0.0);

    /// <summary>
    /// Seven octaves from 600 m down to 9 m, amplitude halving as frequency doubles — the shape
    /// <c>docs/KSA-TERRAIN.md</c> records for the band between the base grid and the float
    /// staircase. Scaled so the summed peak slope is the one asked for, and read at a direction
    /// packed to <c>float3</c> exactly as <c>Celestial.cs:833</c> reads it.
    /// </summary>
    private static double Surface(double3 point, double slope, bool packed)
    {
        double3 dir = Vec.Unit(point);
        if (packed) dir = new double3((float)dir.X, (float)dir.Y, (float)dir.Z);
        double along = R * Math.Atan2(dir.Y, dir.X);

        double height = 0.0, slopeSum = 0.0;
        double wavelength = 600.0, amplitude = 1.0;
        for (int i = 0; i < 7; i++)
        {
            height += amplitude * Math.Sin(2.0 * Math.PI * along / wavelength + i * 1.7);
            slopeSum += amplitude * 2.0 * Math.PI / wavelength;
            wavelength *= 0.5;
            amplitude *= 0.5;
        }

        return R + height * (slope / slopeSum);
    }

    private sealed class Ground(double slope, bool packed) : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = Surface(positionEcl, slope, packed);
            return true;
        }
    }

    private static double3 Release(out double3 from, double nudge, double range)
    {
        from = new double3(R + 877_000.0, 0, 0);
        double3 target = new(R * Math.Cos(range / R), R * Math.Sin(range / R), 0.0);
        double speed = Math.Sqrt(Mu / (R + 877_000.0));
        double3 coasting = new(0, speed, 0);
        Assert.True(BallisticArc.TryCheapest(Earth, from, coasting, target, out BallisticArc.Solution s));
        return s.RequiredVelocityCci + Vec.Unit(s.RequiredVelocityCci) * nudge;
    }

    /// <summary>The walk for one release, in the arrival frame, with the arrival angle it came in at.</summary>
    private static (double Walk, double Gamma) Walk(double slope, bool packed, double nudge,
                                                    double subStepMs, double range)
    {
        double3 v = Release(out double3 from, nudge, range);

        Assert.True(ImpactPredictor.TryPredict(Earth, from, v, 2.0, 12_000.0,
                                               out ImpactPredictor.Impact hit,
                                               p => Surface(p, slope, packed), null,
                                               new ImpactPredictor.Drag(DensityAt, Arsenal.ReentryVehicleMk21),
                                               atmosphericStepSeconds: 0.25, stopOnTheSurface: true));

        MunitionProfile warhead = Arsenal.RoundAtSubStep(Arsenal.ReentryVehicleMk21, subStepMs / 1000.0);
        Slug round = new(from, v, null, 1, from, Vec.Zero)
        {
            Munition = warhead,
            ResampleGroundNearImpact = true,
            SecondOrder = true,
            StopOnTheTerrain = true,
        };

        RoundFields fields = new(
            GravityAt: (p, _) => Vec.Unit(-p) * (Mu / Vec.Len2(p)),
            AirDensityAt: (p, _) => DensityAt(p),
            Ground: new Ground(slope, packed));

        for (int i = 0; i < 2_000_000 && round.State == RoundState.Flying; i++)
        {
            RoundDriver.Fly(round, 0.035, null,
                            Vec.Unit(-round.PositionEcl) * (Mu / Vec.Len2(round.PositionEcl)),
                            Vec.Zero, Vec.Zero, warhead, 0.0, fields);
        }

        Assert.True(round.HitGround);
        double3 landed = round.PositionEcl, vel = round.VelocityEcl;
        Assert.True(ArrivalFrame.TryAt(landed, vel, out ArrivalFrame frame));

        return (frame.Resolve(landed - hit.GroundFixedPointCci).Y * 1000.0,
                Math.Asin(Math.Clamp(-Vec.Dot(Vec.Unit(vel), Vec.Unit(landed)), -1.0, 1.0)));
    }

    private static (double Mean, double Sd, double Gamma) Sample(int seed, int n, double slope,
                                                                 bool packed, double subStepMs,
                                                                 double range)
    {
        Random rng = new(seed);
        List<double> walk = [];
        double gamma = 0.0;
        for (int i = 0; i < n; i++)
        {
            (double w, double g) = Walk(slope, packed, (rng.NextDouble() - 0.5) * 4.0, subStepMs, range);
            walk.Add(w);
            gamma = g;
        }
        double m = walk.Average();
        return (m, Math.Sqrt(walk.Sum(x => (x - m) * (x - m)) / walk.Count), gamma);
    }

    /// <summary>The instrument check: two seeds, same answer, or nothing below is worth reading.</summary>
    [Fact]
    public void TwoSeedsAgree()
    {
        const int N = 90;
        (double m1, double s1, _) = Sample(1, N, 0.15, true, 1.0, 1_500_000.0);
        (double m2, double s2, _) = Sample(2, N, 0.15, true, 1.0, 1_500_000.0);

        Out.WriteLine($"{N} random release states each, slope 0.15, 1 ms, 1,500 km\n");
        Out.WriteLine($"   seed 1: mean {m1,8:F3} mm   sd {s1,8:F3} mm");
        Out.WriteLine($"   seed 2: mean {m2,8:F3} mm   sd {s2,8:F3} mm");
        Out.WriteLine($"\n   sd ratio {Math.Max(s1, s2) / Math.Min(s1, s2):F3}x"
                      + $"   (uniform stepping on one sinusoid moved it 3.1x)");

        Assert.True(Math.Max(s1, s2) / Math.Min(s1, s2) < 1.35,
                    $"the two seeds must agree: {s1:F3} against {s2:F3}");
    }

    /// <summary>
    /// How the floor grows with the ground, on the validated instrument. The site's own slope
    /// distribution, measured off the night's kick lines, is median 0.149, 75th 0.345, 90th 0.823.
    /// </summary>
    [Fact]
    public void TheFloorAgainstTheGroundsSlope()
    {
        double[] slopes = [0.05, 0.15, 0.35, 0.80];
        const int N = 60;

        Out.WriteLine($"{N} random release states, 1 ms, 1,500 km\n");
        Out.WriteLine($"{"slope",8} {"packed sd",12} {"exact sd",11} {"ratio",8} {"packed bias",13}");

        foreach (double slope in slopes)
        {
            (double mp, double sp, _) = Sample(11, N, slope, true, 1.0, 1_500_000.0);
            (_, double se, _) = Sample(11, N, slope, false, 1.0, 1_500_000.0);
            Out.WriteLine($"{slope,8:F2} {sp,12:F3} {se,11:F3} {sp / Math.Max(se, 1e-9),8:F2}x {mp,13:F3}");
        }

        Out.WriteLine("\n   Flown, the within-rocket walk sd is 19.8 mm down.");
    }

    /// <summary>The floor against the sub-step, re-asked on the instrument that does not alias.</summary>
    [Fact]
    public void TheFloorDoesNotMoveWithTheSubStep()
    {
        double[] steps = [4.0, 1.0, 0.25];
        const int N = 60;

        Out.WriteLine($"{N} random release states, slope 0.15, 1,500 km\n");
        Out.WriteLine($"{"sub-step (ms)",14} {"bracket (m)",13} {"bias (mm)",12} {"sd (mm)",10}");

        foreach (double st in steps)
        {
            (double m, double sd, _) = Sample(11, N, 0.15, true, st, 1_500_000.0);
            Out.WriteLine($"{st,14:F2} {st / 1000.0 * 5500.0,13:F1} {m,12:F3} {sd,10:F3}");
        }
    }

    /// <summary>And against the arrival angle, which is the lever the floor should obey.</summary>
    [Fact]
    public void TheFloorAgainstTheArrivalAngle()
    {
        double[] ranges = [2_600_000.0, 1_900_000.0, 1_500_000.0, 1_100_000.0, 800_000.0];
        const int N = 60;

        Out.WriteLine($"{N} random release states, slope 0.15, 1 ms\n");
        Out.WriteLine($"{"range (km)",12} {"arrival (deg)",15} {"cot gamma",11} {"sd (mm)",10} {"sd / cot",10}");

        foreach (double range in ranges)
        {
            (_, double sd, double gamma) = Sample(11, N, 0.15, true, 1.0, range);
            double cot = 1.0 / Math.Tan(gamma);
            Out.WriteLine($"{range / 1000.0,12:F0} {gamma * 180.0 / Math.PI,15:F2} {cot,11:F3}"
                          + $" {sd,10:F3} {sd / cot,10:F3}");
        }

        Out.WriteLine("\n   A flat last column means the floor is a height error times cot gamma.");
    }
}
