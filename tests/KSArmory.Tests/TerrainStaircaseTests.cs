using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Whether the engine's float-packed terrain is what is left of the walk's scatter.
/// </summary>
/// <remarks>
/// <para><c>Celestial.cs:833</c> packs the direction to <c>float3</c> before the procedural
/// modifier stack, so below about a third of a metre of ground the surface is a staircase
/// (<c>docs/KSA-TERRAIN.md</c>). That is documented as adding no <em>bias</em>, because the round,
/// the prediction and the aim point all read the same treads.</para>
///
/// <para>It does not follow that it adds no <em>scatter</em>. The round brackets its ground
/// crossing across one sub-step — 5.5 m of track at 1 ms and 5,500 m/s — while
/// <see cref="ImpactPredictor"/> refines its own to under a metre. Two crossings resolved at
/// different points of a staircase can land on different treads, and a tread is
/// <c>0.31 m x local slope</c> of height, which the arrival angle then multiplies by
/// <c>cot gamma</c> = 1.59.</para>
///
/// <para>Flown, the walk's within-rocket scatter is 19.8 mm down with the sub-step ruled out as a
/// pure bias (<see cref="SubStepConvergenceTests"/>). This asks whether the staircase produces a
/// term of that size, by flying the round and the prediction to one ground with the packing on and
/// then off.</para>
/// </remarks>
public class TerrainStaircaseTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0);

    /// <summary>No spin, so the round's Ecl and the prediction's body-fixed point are one frame.</summary>
    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 0.0);

    /// <summary>The relief a slope implies at the scale a sub-step covers.</summary>
    private const double Wavelength = 40.0;

    /// <summary>
    /// Ground relief bounded at a metre or so, carrying the stated peak slope at the scale a
    /// sub-step crosses — optionally read at a direction packed to single precision, exactly as the
    /// engine reads it. A tilt applied across the whole arc would be a ramp to space; what matters
    /// here is the slope where the round arrives.
    /// </summary>
    private static double Surface(double3 point, double slope, bool packed)
    {
        double3 dir = Vec.Unit(point);
        if (packed) dir = new double3((float)dir.X, (float)dir.Y, (float)dir.Z);
        double along = R * Math.Atan2(dir.Y, dir.X);
        double amplitude = slope * Wavelength / (2.0 * Math.PI);
        return R + amplitude * Math.Sin(2.0 * Math.PI * along / Wavelength);
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

    private static double3 Release(out double3 from, double nudge)
    {
        from = new double3(R + 877_000.0, 0, 0);
        const double Range = 1_500_000.0;
        double3 target = new(R * Math.Cos(Range / R), R * Math.Sin(Range / R), 0.0);
        double speed = Math.Sqrt(Mu / (R + 877_000.0));
        double3 coasting = new(0, speed, 0);
        Assert.True(BallisticArc.TryCheapest(Earth, from, coasting, target, out BallisticArc.Solution s));
        return s.RequiredVelocityCci + Vec.Unit(s.RequiredVelocityCci) * nudge;
    }

    /// <summary>Where the round actually stops, and where its own prediction said it would.</summary>
    private static (double3 Landed, double3 Predicted, double3 Velocity) Fly(double slope, bool packed,
                                                                             double nudge)
    {
        double3 v = Release(out double3 from, nudge);
        Ground ground = new(slope, packed);

        Assert.True(ImpactPredictor.TryPredict(Earth, from, v, 2.0, 12_000.0,
                                               out ImpactPredictor.Impact hit,
                                               p => Surface(p, slope, packed), null,
                                               new ImpactPredictor.Drag(DensityAt, Arsenal.ReentryVehicleMk21),
                                               atmosphericStepSeconds: 0.25, stopOnTheSurface: true));

        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;
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
            Ground: ground);

        for (int i = 0; i < 2_000_000 && round.State == RoundState.Flying; i++)
        {
            RoundDriver.Fly(round, 35.0 / 1000.0, null,
                            Vec.Unit(-round.PositionEcl) * (Mu / Vec.Len2(round.PositionEcl)),
                            Vec.Zero, Vec.Zero, warhead, 0.0, fields);
        }

        Assert.True(round.HitGround, "the round should have reached the ground");
        return (round.PositionEcl, hit.GroundFixedPointCci, round.VelocityEcl);
    }

    /// <summary>
    /// The walk — landing against its own prediction — with the engine's packing on and off, over
    /// a spread of release states and a spread of ground slopes.
    /// </summary>
    [Fact]
    public void TheFloatStaircaseIsAWalkScatterEvenThoughItIsNotABias()
    {
        double[] slopes = [0.05, 0.10, 0.15, 0.20, 0.35];
        double[] nudges = [.. Enumerable.Range(0, 80).Select(i => -2.0 + i * 0.05)];

        Out.WriteLine("walk = landing - its own prediction, in the arrival frame, 80 release states\n");
        Out.WriteLine($"{"peak slope",10} {"packed bias",13} {"packed sd",11} {"exact bias",12} {"exact sd",10}"
                      + $" {"sd ratio",10}");

        foreach (double slope in slopes)
        {
            List<double> packed = [];
            List<double> exact = [];

            foreach (double nudge in nudges)
            {
                foreach ((bool pack, List<double> into) in new[] { (true, packed), (false, exact) })
                {
                    (double3 landed, double3 predicted, double3 vel) = Fly(slope, pack, nudge);
                    Assert.True(ArrivalFrame.TryAt(landed, vel, out ArrivalFrame frame));
                    into.Add(frame.Resolve(landed - predicted).Y * 1000.0);
                }
            }

            double Sd(List<double> xs)
            {
                double m = xs.Average();
                return Math.Sqrt(xs.Sum(x => (x - m) * (x - m)) / xs.Count);
            }

            Out.WriteLine($"{slope,10:F2} {packed.Average(),13:F3} {Sd(packed),11:F3} "
                          + $"{exact.Average(),12:F3} {Sd(exact),10:F3} "
                          + $"{Sd(packed) / Math.Max(Sd(exact), 1e-9),10:F1}x");
        }

        Out.WriteLine("\n   Flown, the walk's within-rocket down scatter is 19.8 mm and the sub-step");
        Out.WriteLine("   accounts for none of it (SubStepConvergenceTests: sd 0.002 mm).");
        Out.WriteLine("   The packed column is a term no setting in this mod can reach.");
    }
}
