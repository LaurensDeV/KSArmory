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

    /// <summary>Overridden by the wavelength sweep, which asks whether a result is fixture-specific.</summary>
    private static double ActiveWavelength = 40.0;

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
        double amplitude = slope * ActiveWavelength / (2.0 * Math.PI);
        return R + amplitude * Math.Sin(2.0 * Math.PI * along / ActiveWavelength);
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
        => Fly(slope, packed, nudge, 0.25);

    private static (double3 Landed, double3 Predicted, double3 Velocity) Fly(double slope, bool packed,
                                                                             double nudge,
                                                                             double predictorStep)
        => Fly(slope, packed, nudge, predictorStep, 1.0);

    private static (double3 Landed, double3 Predicted, double3 Velocity) Fly(double slope, bool packed,
                                                                             double nudge,
                                                                             double predictorStep,
                                                                             double subStepMs)
    {
        double3 v = Release(out double3 from, nudge);
        Ground ground = new(slope, packed);

        Assert.True(ImpactPredictor.TryPredict(Earth, from, v, 2.0, 12_000.0,
                                               out ImpactPredictor.Impact hit,
                                               p => Surface(p, slope, packed), null,
                                               new ImpactPredictor.Drag(DensityAt, Arsenal.ReentryVehicleMk21),
                                               atmosphericStepSeconds: predictorStep, stopOnTheSurface: true));

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

    /// <summary>
    /// The other half of the walk's bias: the <em>prediction's</em> own step.
    /// </summary>
    /// <remarks>
    /// <para>The round integrates at 1 ms — 5.5 m of track — and
    /// <see cref="ImpactPredictor.AtmosphericStepSeconds"/> is <b>0.25 s</b>, which at 5,500 m/s is
    /// 1,375 m. If the prediction were converged the walk would read the round's own sub-step bias
    /// and nothing more, which `SubStepConvergenceTests` puts at -5.36 mm; flown it is -10.3 mm.</para>
    ///
    /// <para><c>docs/ACCURACY-PLAN.md</c> records "refining the predictor's step" as ruled out, and
    /// says in its own retrospective that the test which ruled it out passed <b>no terrain</b> — a
    /// mean sphere, the one surface a step cannot undersample. This asks it again with ground under
    /// it.</para>
    /// </remarks>
    [Fact]
    public void ThePredictorsOwnStepIsTheOtherHalfOfTheWalksBias()
    {
        double[] predictorSteps = [0.25, 0.125, 0.0625, 0.03125, 0.015625, 0.0078125];
        const double Slope = 0.15;
        double[] nudges = [.. Enumerable.Range(0, 40).Select(i => -1.0 + i * 0.05)];

        Out.WriteLine($"round fixed at its shipped 1 ms, ground slope {Slope}, 40 release states\n");
        Out.WriteLine($"{"predictor step (s)",20} {"walk bias (mm)",16} {"walk sd (mm)",14}");

        foreach (double step in predictorSteps)
        {
            List<double> walk = [];
            foreach (double nudge in nudges)
            {
                (double3 landed, double3 predicted, double3 vel) = Fly(Slope, true, nudge, step);
                Assert.True(ArrivalFrame.TryAt(landed, vel, out ArrivalFrame frame));
                walk.Add(frame.Resolve(landed - predicted).Y * 1000.0);
            }
            double m = walk.Average();
            double sd = Math.Sqrt(walk.Sum(x => (x - m) * (x - m)) / walk.Count);
            Out.WriteLine($"{step,20:F7} {m,16:F3} {sd,14:F3}");
        }

        Out.WriteLine("\n   The round's own sub-step bias at 1 ms is -5.36 mm (SubStepConvergenceTests).");
        Out.WriteLine("   A converged prediction should leave the walk at that and no more.");
    }

    /// <summary>
    /// The sub-step again, but over ground. On a sphere it is a pure bias
    /// (<see cref="SubStepConvergenceTests"/>, sd 0.002 mm); over terrain the sub-step is the width
    /// of the bracket the round solves its crossing across — 22 m at 4 ms, 5.5 m at 1 ms, 1.4 m at
    /// 0.25 ms, against a prediction that refines its own to under a metre — so it should move the
    /// walk's SCATTER too.
    /// </summary>
    /// <remarks>
    /// This is the quantitative form of the secondary read declared for the 2026-09-17 night, and it
    /// is written down before that night's arms have been looked at.
    /// </remarks>
    [Fact]
    public void OverGroundTheSubStepMovesTheScatterAndNotOnlyTheBias()
    {
        double[] steps = [4.0, 1.0, 0.25, 0.0625, 0.03125];
        const double Slope = 0.15;
        double[] nudges = [.. Enumerable.Range(0, 40).Select(i => -1.0 + i * 0.05)];

        Out.WriteLine($"ground slope {Slope}, 40 release states, prediction at its shipped 0.25 s");
        Out.WriteLine("the engine's float terrain tread is 0.31 m of ground\n");
        Out.WriteLine($"{"round sub-step (ms)",21} {"bracket (m)",13} {"walk bias (mm)",16} {"walk sd (mm)",14}");

        Dictionary<double, double> sd = [];
        foreach (double st_ in steps)
        {
            List<double> walk = [];
            foreach (double nudge in nudges)
            {
                (double3 landed, double3 predicted, double3 vel) = Fly(Slope, true, nudge, 0.25, st_);
                Assert.True(ArrivalFrame.TryAt(landed, vel, out ArrivalFrame frame));
                walk.Add(frame.Resolve(landed - predicted).Y * 1000.0);
            }
            double m = walk.Average();
            double s2 = Math.Sqrt(walk.Sum(x => (x - m) * (x - m)) / walk.Count);
            sd[st_] = s2;
            Out.WriteLine($"{st_,21:F2} {st_ / 1000.0 * 5500.0,13:F1} {m,16:F3} {s2,14:F3}");
        }

        Out.WriteLine($"\n   coarse/base {sd[4.0] / sd[1.0]:F2}x   fine/base {sd[0.25] / sd[1.0]:F2}x");
        Out.WriteLine("   Flown, the within-rocket walk sd at 1 ms is 19.8 mm.");
    }

    /// <summary>
    /// Whether the coarse sub-step's sign flip is real or an artefact of relief tuned to one scale.
    /// A 4 ms bracket is 22 m of track; against 40 m of relief that is half a wavelength, which is
    /// not a regime the fixture was built for.
    /// </summary>
    [Fact]
    public void TheCoarseSubStepsSignFlipIsCheckedAgainstTheReliefScale()
    {
        double[] wavelengths = [40.0, 100.0, 250.0, 600.0, 1500.0];
        double[] steps = [4.0, 1.0];
        const double Slope = 0.15;
        double[] nudges = [.. Enumerable.Range(0, 24).Select(i => -1.0 + i * 0.08)];

        Out.WriteLine($"slope {Slope} held; only the relief's wavelength changes\n");
        Out.WriteLine($"{"wavelength (m)",15} {"4 ms bias",12} {"4 ms sd",10} {"1 ms bias",12} {"1 ms sd",10}");

        try
        {
            foreach (double w in wavelengths)
            {
                ActiveWavelength = w;
                List<string> cells = [];
                foreach (double st_ in steps)
                {
                    List<double> walk = [];
                    foreach (double nudge in nudges)
                    {
                        (double3 landed, double3 predicted, double3 vel) = Fly(Slope, true, nudge, 0.25, st_);
                        Assert.True(ArrivalFrame.TryAt(landed, vel, out ArrivalFrame frame));
                        walk.Add(frame.Resolve(landed - predicted).Y * 1000.0);
                    }
                    double m = walk.Average();
                    double sd = Math.Sqrt(walk.Sum(x => (x - m) * (x - m)) / walk.Count);
                    cells.Add($"{m,12:F2}{sd,10:F2}");
                }
                Out.WriteLine($"{w,15:F0}{string.Concat(cells)}");
            }
        }
        finally { ActiveWavelength = Wavelength; }

        Out.WriteLine("\n   A sign flip that survives every relief scale is the mechanism;");
        Out.WriteLine("   one that only appears at 40 m is the fixture.");
    }
}
