using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Item 45. What is left of the ballistic miss once the release kick has cancelled everything the
/// prediction gets wrong about <em>where</em> — which is what it gets wrong about <em>flying
/// there</em>.
///
/// <para>Since item 43 each warhead is kicked by its own release probe's miss, so the landing is no
/// longer scored against the ground: it is scored against the prediction the warhead was aimed by.
/// What is left is the two flight models disagreeing, and they do not fly alike — the predictor
/// steps 2.0 s in vacuum and 0.25 s in air against the round's 1 ms sub-step.
/// <c>docs/ACCURACY-PLAN.md</c> 3dl and 3dm.</para>
///
/// <para><b>Measurement, not a gate.</b> What is asserted is the shape each sweep settled on — the
/// predictor's step buys nothing and the round's sub-step buys everything — so that the day either
/// stops being true, this says so. The numbers go to the test output.</para>
///
/// <para><b>It does not reproduce the flight and is not meant to.</b> A rig flies a planet at the
/// origin, which is the one case where a frame carrier is identically zero; see
/// <see cref="WarheadTrace"/>'s own note. This prices <em>which knob</em> the term hangs on, never
/// how many metres it is worth in game.</para>
/// </summary>
public class PredictorStepGapTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private const double ScaleHeight = 8_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 0.0);

    private static double DensityAt(double3 pointCci)
        => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    private static double3 GravityAt(double3 pointCci)
    {
        double r = Vec.Len(pointCci);
        return Vec.Unit(-pointCci) * (Mu / (r * r));
    }

    private sealed class Ball : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = R;
            return true;
        }
    }

    /// <summary>
    /// A release at the flown altitude — the MIRV shots release near 877 km — over 1,500 km of
    /// ground, which arrives at 32 degrees as they do.
    ///
    /// <para>The range is chosen for the <em>arrival</em> and not the other way round: 800 km
    /// arrives at 49.8 degrees and 2,600 km at 20.3, and every metre here scales as
    /// cot(gamma).</para>
    /// </summary>
    private static double3 ReleaseArc(out double3 from, out double3 target)
    {
        from = new double3(R + 877_000.0, 0, 0);

        const double Range = 1_500_000.0;
        target = new double3(R * Math.Cos(Range / R), R * Math.Sin(Range / R), 0);

        double3 coasting = new(0, Math.Sqrt(Mu / (R + 877_000.0)), 0);
        Assert.True(BallisticArc.TryCheapest(Earth, from, coasting, target, out BallisticArc.Solution s));
        return s.RequiredVelocityCci;
    }

    /// <summary>
    /// Where the warhead comes down, flown exactly as the round flies it — through
    /// <see cref="RoundDriver"/> and with the lookups the game supplies, never
    /// <see cref="RoundFields.Held"/>.
    /// </summary>
    /// <remarks>
    /// The density lookup is the one that decides this measurement. Held at the frame's first
    /// sample, a re-entering warhead flies the whole frame through the thinner air it had at the top
    /// of it, and the rig then prices its own shortcut rather than the round: 13.75 m on this
    /// geometry against 3.72 m re-read.
    /// </remarks>
    private static double3 FlyTheRound(double3 fromCci, double3 velocityCci, MunitionProfile munition,
                                       double dt, bool secondOrder = true)
    {
        Slug round = new(fromCci, velocityCci, null, 1, fromCci, Vec.Zero)
        {
            Munition = munition,
            ResampleGroundNearImpact = true,

            // What IcbmComputer sets on every released warhead, off IcbmConfig.SecondOrderWarheads.
            // The property defaults to false and nothing flies that way, so a rig that leaves it
            // alone prices a first-order round: 1.54 m against 0.01 m at the shipped 1 ms.
            SecondOrder = secondOrder,
        };

        RoundFields fields = new(
            GravityAt: (point, _) => GravityAt(point),
            AirDensityAt: (point, _) => DensityAt(point),
            Ground: new Ball());

        for (int i = 0; i < (int)(3000.0 / dt) && round.State == RoundState.Flying; i++)
        {
            RoundDriver.Fly(round, dt, null, GravityAt(round.PositionEcl), Vec.Zero, Vec.Zero,
                            munition, DensityAt(round.PositionEcl), fields);
        }

        Assert.NotEqual(RoundState.Flying, round.State);
        return round.PositionEcl;
    }

    private static ImpactPredictor.Impact Predict(double3 fromCci, double3 velocityCci,
                                                  MunitionProfile munition,
                                                  double vacuumStep, double airStep)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, fromCci, velocityCci, vacuumStep, 12_000.0,
                                               out ImpactPredictor.Impact hit, null, null,
                                               new ImpactPredictor.Drag(DensityAt, munition),
                                               atmosphericStepSeconds: airStep,
                                               stopOnTheSurface: true));
        return hit;
    }

    private static double GroundMetres(double3 a, double3 b) => R * Vec.AngleBetween(a, b);

    /// <summary>The Mk 21 with a stated sub-step, which is the knob the term turned out to hang on.</summary>
    private static MunitionProfile AtSubStep(double seconds)
    {
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21.Copy();
        warhead.SubStepSeconds = (float)seconds;
        return warhead;
    }

    /// <summary>
    /// The geometry, stated rather than assumed — the whole term scales as cot(gamma), so an entry
    /// quoting metres off this rig is only about the flown shot if the arrival matches it.
    /// </summary>
    [Fact]
    public void TheRigArrivesWhereTheFlownShotDoes()
    {
        double3 velocity = ReleaseArc(out double3 from, out double3 _);
        ImpactPredictor.Impact hit = Predict(from, velocity, Arsenal.ReentryVehicleMk21, 2.0, 0.25);

        double3 up = Vec.Unit(hit.PointCci);
        double arrival = Vec.AngleBetween(up, hit.VelocityCci) * 180.0 / Math.PI - 90.0;

        Out.WriteLine($"release {(Vec.Len(from) - R) / 1000.0:F1} km, {hit.Seconds:F1} s of flight, "
                      + $"arriving at {Vec.Len(hit.VelocityCci):F0} m/s, {arrival:F1} deg below "
                      + "the horizontal");
        Out.WriteLine("the flown shots: 877 km, 344-362 s, 3,976-5,627 m/s, 32.0-32.4 deg");


        Assert.InRange(arrival, 30.0, 34.0);
    }

    /// <summary>
    /// <b>Refuted.</b> The predictor's two steps were the declared suspect in 3dl, on a 70:1 ratio
    /// against the round. Forty times finer moves the arrival by less than a tenth of a millimetre:
    /// it bisects onto the crossing near the ground, so its step never reaches the answer.
    /// </summary>
    [Fact]
    public void ThePredictorsOwnStepBuysNothing()
    {
        double3 velocity = ReleaseArc(out double3 from, out double3 _);
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;
        double3 landed = FlyTheRound(from, velocity, warhead, 1.0 / 60.0);

        Out.WriteLine($"{"vacuum (s)",12} {"air (s)",10} {"gap to the round (m)",24}");

        double coarsest = double.NaN, finest = double.NaN;

        foreach ((double vac, double air) in new[]
                 {
                     (2.0, 0.25), (1.0, 0.25), (0.5, 0.25), (0.25, 0.25),
                     (2.0, 0.1), (2.0, 0.05), (2.0, 1.0 / 60.0), (0.05, 1.0 / 60.0),
                 })
        {
            double gap = GroundMetres(Predict(from, velocity, warhead, vac, air).GroundFixedPointCci,
                                      landed);
            Out.WriteLine($"{vac,12:F3} {air,10:F4} {gap,24:F6}");

            if (double.IsNaN(coarsest)) coarsest = gap;
            finest = gap;
        }

        Out.WriteLine($"\nshipped 2.0/0.25 s against 0.05/(1/60) s: {Math.Abs(finest - coarsest) * 1000.0:F3} mm apart");

        Assert.True(Math.Abs(finest - coarsest) < 0.001,
                    $"the predictor's step used to buy nothing; it now moves the arrival "
                    + $"{Math.Abs(finest - coarsest):F4} m, so 3dm's refutation wants re-reading");
    }

    /// <summary>
    /// <b>The sub-step is worth millimetres on the round the game flies, and metres on one nothing
    /// flies.</b> <see cref="Slug.SecondOrder"/> defaults to false and
    /// <see cref="IcbmConfig.SecondOrderWarheads"/> ships true, so a rig that leaves the property
    /// alone measures a first-order round — 1.54 m at the shipped 1 ms against <b>4.7 mm</b>.
    ///
    /// <para>That is what makes <see cref="IcbmConfig.WarheadSubStepMs"/> a dead lever: eight times
    /// finer recovers 4 mm of a 21 mm flown walk, which no night can resolve.
    /// <c>docs/ACCURACY-PLAN.md</c> 3dn.</para>
    ///
    /// <para>Both columns are kept because the pair is the finding. <c>RoundIntegratorOrderTests</c>
    /// makes the same point in vacuum, where second order is exact for a conic and the sub-step
    /// moves the landing 0.000 m; this is the case with drag, where it does not vanish and is still
    /// millimetres.</para>
    /// </summary>
    [Fact]
    public void TheSubStepIsMillimetresOnTheRoundTheGameFlies()
    {
        double3 velocity = ReleaseArc(out double3 from, out double3 _);
        double3 predicted = Predict(from, velocity, Arsenal.ReentryVehicleMk21, 2.0, 0.25)
            .GroundFixedPointCci;

        Out.WriteLine($"{"sub-step (ms)",14} {"gap to the prediction (m)",26}");

        double shipped = double.NaN, finest = double.NaN;

        Out.WriteLine($"{"",14} {"second order (flown)",26} {"first order",18}");

        foreach (double ms in new[] { 5.0, 2.5, 1.0, 0.5, 0.25, 0.125 })
        {
            MunitionProfile warhead = AtSubStep(ms / 1000.0);
            double gap = GroundMetres(predicted, FlyTheRound(from, velocity, warhead, 1.0 / 60.0));
            double first = GroundMetres(predicted,
                                        FlyTheRound(from, velocity, warhead, 1.0 / 60.0, false));

            Out.WriteLine($"{ms,14:F3} {gap,26:F4} {first,18:F4}");

            if (ms == 1.0) shipped = gap;
            finest = gap;
        }

        Out.WriteLine($"\nshipped 1 ms {shipped * 1000.0:F1} mm, at 0.125 ms {finest * 1000.0:F1} mm "
                      + $"-- an arm can recover {(shipped - finest) * 1000.0:F1} mm");
        Out.WriteLine("the flown walk is 21 mm, so the sub-step is at most a fifth of it and the");
        Out.WriteLine("arm is far below what twelve paired blocks can resolve.");

        Assert.True(shipped < 0.010,
                    $"the flown warhead is second order, so its sub-step should be worth millimetres; "
                    + $"{shipped:F4} m says the rig has stopped flying the round the game flies");
    }

    /// <summary>
    /// The frame rate, which is what a throughput lever would move. It never reaches a 1 ms
    /// sub-step, so nothing bought there can be spent here — the two levers are independent.
    /// </summary>
    [Fact]
    public void TheFrameRateNeverReachesTheSubStep()
    {
        double3 velocity = ReleaseArc(out double3 from, out double3 _);
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;
        double3 predicted = Predict(from, velocity, warhead, 2.0, 0.25).GroundFixedPointCci;

        double at30 = GroundMetres(predicted, FlyTheRound(from, velocity, warhead, 1.0 / 30.0));
        double at60 = GroundMetres(predicted, FlyTheRound(from, velocity, warhead, 1.0 / 60.0));

        Out.WriteLine($"round stepped at 1/30 s: {at30:F4} m; at 1/60 s: {at60:F4} m");

        Assert.Equal(at30, at60, 3);
    }
}
