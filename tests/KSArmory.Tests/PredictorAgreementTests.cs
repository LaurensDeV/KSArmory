using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Whether the two models of one fall agree: <see cref="ImpactPredictor"/>, which the aim
/// correction converges against, and <see cref="Slug"/>, which is what actually arrives.
///
/// <para><b>They disagree in flight, and by most of the miss.</b> At 2,000 km a warhead lands 4 m
/// from its own release prediction; at 12,902 km it lands <b>157 m</b> from it, 62% of the miss, and
/// the walk correlates with the miss at rho=+0.707. The predictor's own step is not the cause — it
/// is converged to a few metres at 2 s. So the question is whether the two integrators disagree at
/// all when handed identical physics, or whether the divergence is something the game supplies to
/// one of them.</para>
/// </summary>
public sealed class PredictorAgreementTests
{
    private readonly ITestOutputHelper _out;
    public PredictorAgreementTests(ITestOutputHelper o) => _out = o;

    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static readonly BallisticBody Body = new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    /// <summary>A planet at the origin, so Ecl and Cci are the same frame for this comparison.</summary>
    private sealed class Ball : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = R;
            return true;
        }
    }

    /// <summary>The reentry vehicle with its drag removed, so only the integrators are compared.</summary>
    private static MunitionProfile Vacuum() => new()
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
    };

    /// <summary>Fly the round to the ground on the same inverse-square field the predictor uses.</summary>
    private static (double3 Point, double Seconds) FlyTheRound(double3 p, double3 v, double dt)
    {
        var round = new Slug(p, v, null, 1, p, Vec.Zero)
        {
            Munition = Vacuum(),
            Ground = new Ball(),
        };

        for (int i = 0; i < 4_000_000 && round.State == RoundState.Flying; i++)
        {
            round.Update(dt, null, Body.GravityCci(round.PositionEcl), Vec.Zero, Vec.Zero,
                         round.Munition);
        }

        return (round.PositionEcl, round.Age);
    }

    /// <summary>
    /// Handed the same field and the same sphere, the round arrives at a different time from the
    /// predictor, and the error scales with the round's step — so it is the round's integration,
    /// not the predictor's, that walks. Pinned rather than asserted away: this is the fault, and a
    /// fix is measured against it.
    /// </summary>
    [Fact]
    public void TheRoundArrivesLateAndTheErrorScalesWithItsStep()
    {
        // A long fall: the regime where 12,902 km shots walk 157 m off their own release probe.
        double3 p = new(R + 400_000.0, 0, 0);
        double3 v = new(0, 7400.0, 0);

        Assert.True(ImpactPredictor.TryPredict(Body, p, v, 0.25, 6.0 * 3600.0,
                                               out ImpactPredictor.Impact predicted));

        var (coarse, tCoarse) = FlyTheRound(p, v, 1.0 / 30.0);
        var (fine, tFine) = FlyTheRound(p, v, 1.0 / 240.0);

        double gapCoarse = (coarse - predicted.PointCci).Length();
        double gapFine = (fine - predicted.PointCci).Length();

        _out.WriteLine($"flight {predicted.Seconds:F0} s at {Vec.Len(predicted.VelocityCci):F0} m/s");
        _out.WriteLine($"  33.3 ms: {gapCoarse:F0} m, {tCoarse - predicted.Seconds:+0.000;-0.000} s");
        _out.WriteLine($"   4.2 ms: {gapFine:F0} m, {tFine - predicted.Seconds:+0.000;-0.000} s");

        // Hundreds of metres at the step the game actually runs, and it comes down with the step.
        Assert.True(gapCoarse > 500.0, $"expected the coarse step to walk; it was {gapCoarse:F0} m");
        Assert.True(gapFine < gapCoarse / 2.0,
                    $"a finer step should halve it: {gapFine:F0} against {gapCoarse:F0}");
    }

    /// <summary>
    /// And the walk is along the round's own track, not across it — which is why it reads as
    /// downrange bias in flight (309 m down against 3 m cross) and looks exactly like guidance error.
    /// </summary>
    [Fact]
    public void TheWalkIsAlongTheTrackAndNotAcrossIt()
    {
        double3 p = new(R + 400_000.0, 0, 0);
        double3 v = new(0, 7400.0, 0);

        Assert.True(ImpactPredictor.TryPredict(Body, p, v, 0.25, 6.0 * 3600.0,
                                               out ImpactPredictor.Impact predicted));

        var (point, _) = FlyTheRound(p, v, 1.0 / 60.0);

        double3 along = Vec.Unit(predicted.VelocityCci);
        double3 gap = point - predicted.PointCci;
        double downrange = Math.Abs(Vec.Dot(gap, along));
        double square = (gap - along * Vec.Dot(gap, along)).Length();

        _out.WriteLine($"{downrange:F0} m along the track, {square:F0} m square to it");

        Assert.True(downrange > 10.0 * square,
                    $"expected an along-track walk: {downrange:F0} along against {square:F0} square");
    }
}
