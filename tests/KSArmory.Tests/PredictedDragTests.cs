using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The prediction and the round have to be flying the same trajectory.
///
/// <para>They were not. The predictor flew in vacuum and the released warhead flew through air, and
/// on a shallow deorbit arrival — where the path through the atmosphere is a dozen times longer
/// than the height lost — that is tens of kilometres, always short. Worse, it was unclosable:
/// <see cref="AimCorrection"/> observes the prediction, so a difference the prediction cannot see
/// is a difference no amount of correcting removes.</para>
/// </summary>
public class PredictedDragTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private const double ScaleHeight = 8_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 0.0);

    private static double DensityAt(double3 pointCci)
        => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    private sealed class Ball : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = R;
            return true;
        }
    }

    /// <summary>A deorbit from 200 km arriving about 2,764 km downrange — the flown shot.</summary>
    private static BallisticArc.Solution Deorbit(out double3 from, out double3 target)
    {
        from = new double3(R + 200_000.0, 0, 0);
        double range = 2_764_000.0;
        target = new double3(R * Math.Cos(range / R), R * Math.Sin(range / R), 0);

        double3 circular = new(0, Math.Sqrt(Mu / (R + 200_000.0)), 0);
        Assert.True(BallisticArc.TryCheapest(Earth, from, circular, target, out BallisticArc.Solution s));
        return s;
    }

    /// <summary>Where the round the bus actually drops comes down, flown as the round flies it.</summary>
    private static double3 FlyTheRound(double3 fromCci, double3 velocityCci, MunitionProfile munition)
    {
        Slug round = new(fromCci, velocityCci, null, 1, fromCci, Vec.Zero)
        {
            Munition = munition,
            Ground = new Ball(),
        };

        const double dt = 1.0 / 60.0;
        for (int i = 0; i < 60 * 3000 && round.State == RoundState.Flying; i++)
        {
            double r = Vec.Len(round.PositionEcl);
            double3 gravity = Vec.Unit(-round.PositionEcl) * (Mu / (r * r));
            round.Update(dt, null, gravity, Vec.Zero, Vec.Zero, munition, DensityAt(round.PositionEcl));
        }

        Assert.NotEqual(RoundState.Flying, round.State);
        return round.PositionEcl;
    }

    private static double GroundMetres(double3 a, double3 b) => R * Vec.AngleBetween(a, b);

    private const double EarthSpin = 7.2921159e-5;

    private static BallisticBody Spinning => new(Mu, R, new double3(0, 0, 1), EarthSpin);

    /// <summary>
    /// Where the round comes down, as a place on the ground, with the air's motion chosen by the
    /// caller — at the round, or at some other point, which is the thing under test.
    /// </summary>
    private static double3 FlyOverSpinningGround(double3 fromCci, double3 velocityCci,
                                                 MunitionProfile munition, bool airAtTheRound)
    {
        Slug round = new(fromCci, velocityCci, null, 1, fromCci, Vec.Zero)
        {
            Munition = munition,
            Ground = new Ball(),
        };

        BallisticBody body = Spinning;
        const double dt = 1.0 / 60.0;
        double elapsed = 0.0;

        for (int i = 0; i < 60 * 3000 && round.State == RoundState.Flying; i++)
        {
            double r = Vec.Len(round.PositionEcl);
            double3 gravity = Vec.Unit(-round.PositionEcl) * (Mu / (r * r));

            // The whole question: the air over the round, or the air over what threw it.
            double3 air = body.GroundVelocityCci(airAtTheRound ? round.PositionEcl : fromCci);

            round.Update(dt, null, gravity, air, fromCci, munition, DensityAt(round.PositionEcl));
            elapsed += dt;
        }

        Assert.NotEqual(RoundState.Flying, round.State);

        // Back to a place on the ground, so two flights of different length are comparable.
        return body.UncarryCci(round.PositionEcl, elapsed);
    }

    /// <summary>
    /// Sampling the air at the launcher rather than at the round is worth nothing for a shell and
    /// kilometres for a warhead a quarter of the way round the planet — the air over the two points
    /// moves in measurably different directions.
    /// </summary>
    [Fact]
    public void TheAirIsMeasuredOverTheRoundRatherThanOverWhatThrewIt()
    {
        BallisticArc.Solution arc = Deorbit(out double3 from, out double3 _);
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;

        double3 atRound = FlyOverSpinningGround(from, arc.RequiredVelocityCci, warhead, true);
        double3 atPlatform = FlyOverSpinningGround(from, arc.RequiredVelocityCci, warhead, false);

        double apart = GroundMetres(atRound, atPlatform);
        Out.WriteLine($"air sampled at the platform moves the impact {apart / 1000.0:F1} km");

        Assert.True(apart > 1_000.0,
                    $"the two air frames should differ over this range; they were "
                    + $"{apart / 1000.0:F2} km apart, so this no longer measures anything");
    }

    [Fact]
    public void APredictionInVacuumLandsTensOfKilometresBeyondTheRoundItPredicts()
    {
        BallisticArc.Solution arc = Deorbit(out double3 from, out double3 target);

        Assert.True(ImpactPredictor.TryPredict(Earth, from, arc.RequiredVelocityCci, 1.0, 12_000.0,
                                               out ImpactPredictor.Impact vacuum));

        double3 landed = FlyTheRound(from, arc.RequiredVelocityCci, Arsenal.ReentryVehicleMk21);
        double apart = GroundMetres(vacuum.GroundFixedPointCci, landed);

        Out.WriteLine($"vacuum prediction to round: {apart / 1000.0:F1} km");

        Assert.True(apart > 20_000.0,
                    $"a vacuum prediction should be far from where the round lands; it was "
                    + $"{apart / 1000.0:F1} km, so this geometry no longer exercises the fault");
    }

    /// <summary>And giving the prediction the round's own drag closes it, which is the whole fix.</summary>
    [Fact]
    public void PredictingWithTheWarheadsDragLandsWhereTheRoundLands()
    {
        BallisticArc.Solution arc = Deorbit(out double3 from, out double3 target);
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;

        Assert.True(ImpactPredictor.TryPredict(Earth, from, arc.RequiredVelocityCci, 1.0, 12_000.0,
                                               out ImpactPredictor.Impact predicted, null, null,
                                               new ImpactPredictor.Drag(DensityAt, warhead)));

        double3 landed = FlyTheRound(from, arc.RequiredVelocityCci, warhead);
        double apart = GroundMetres(predicted.GroundFixedPointCci, landed);

        Out.WriteLine($"drag prediction to round:   {apart / 1000.0:F2} km");
        Out.WriteLine($"  (the shot itself falls {GroundMetres(target, landed) / 1000.0:F1} km short "
                      + "of the target, which is what the aim correction is then able to see)");

        Assert.True(apart < 2_000.0,
                    $"the prediction and the round should agree; they were {apart / 1000.0:F1} km apart");
    }

    /// <summary>
    /// The whole feature, end to end: solve, predict the round rather than the bus, correct the aim
    /// by what the prediction loses, and the round arrives.
    ///
    /// <para>This is the test that would have failed against the shipped code. The correction loop
    /// was already right; it was reading an instrument that could not see the error.</para>
    /// </summary>
    [Fact]
    public void CorrectingAgainstADragAwarePredictionPutsTheRoundOnTheTarget()
    {
        Deorbit(out double3 from, out double3 target);
        double3 circular = new(0, Math.Sqrt(Mu / (R + 200_000.0)), 0);
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;

        AimCorrection correction = new();
        double3 flown = default;

        for (int i = 0; i < 12; i++)
        {
            double3 aim = correction.Apply(target);
            Assert.True(BallisticArc.TryCheapest(Earth, from, circular, aim, out BallisticArc.Solution s));

            Assert.True(ImpactPredictor.TryPredict(Earth, from, s.RequiredVelocityCci, 1.0, 12_000.0,
                                                   out ImpactPredictor.Impact hit, null, null,
                                                   new ImpactPredictor.Drag(DensityAt, warhead)));

            correction.Observe(hit.GroundFixedPointCci, target);
            flown = FlyTheRound(from, s.RequiredVelocityCci, warhead);
        }

        double miss = GroundMetres(flown, target);
        Out.WriteLine($"after twelve corrections the round lands {miss / 1000.0:F2} km from the target");
        Out.WriteLine($"  aim moved {Vec.Len(correction.BiasCci) / 1000.0:F1} km");

        Assert.True(miss < 2_000.0, $"the round landed {miss / 1000.0:F1} km from the target");
    }

    /// <summary>
    /// A round with no drag is unaffected, so the arithmetic above the atmosphere is untouched.
    /// </summary>
    [Fact]
    public void ARoundWithNoDragPredictsExactlyAsItDidInVacuum()
    {
        BallisticArc.Solution arc = Deorbit(out double3 from, out double3 _);
        MunitionProfile inert = new()
        {
            Name = "TESTINERT",
            DisplayName = "drag-free round",
            Guidance = GuidanceMode.None,
            DragK = 0f,
        };

        Assert.True(ImpactPredictor.TryPredict(Earth, from, arc.RequiredVelocityCci, 1.0, 12_000.0,
                                               out ImpactPredictor.Impact vacuum));
        Assert.True(ImpactPredictor.TryPredict(Earth, from, arc.RequiredVelocityCci, 1.0, 12_000.0,
                                               out ImpactPredictor.Impact withAir, null, null,
                                               new ImpactPredictor.Drag(DensityAt, inert)));

        Assert.True(GroundMetres(vacuum.GroundFixedPointCci, withAir.GroundFixedPointCci) < 1.0);
    }
}
