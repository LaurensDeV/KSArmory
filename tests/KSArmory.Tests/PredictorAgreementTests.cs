using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Whether the two models of one fall agree: <see cref="ImpactPredictor"/>, which the aim
/// correction converges against, and <see cref="Slug"/>, which is what actually arrives.
///
/// <para><b>Flown as the game flies it, they agree to about fifty metres over a twenty-minute fall,
/// and the frame rate does not enter.</b> That is the point of the two things this fixture sets that
/// a naive one does not: the reentry vehicle's own millisecond sub-step, and a per-sub-step
/// <see cref="Slug.GravityAt"/>. Omit either and the round is handed one gravity sample per frame,
/// which fabricates a frame-rate-dependent error of over a kilometre — a round nobody flies.</para>
///
/// <para>So the <b>157 m</b> a warhead walks from its release probe at 12,902 km is not this: the
/// integrators account for about a third of it and no part of it moves with the frame rate. The rest
/// is somewhere the drag model, the terrain or the warp differ between the two.</para>
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

        // The reentry vehicle's own, which is the whole point: it already sub-steps at a
        // millisecond, so a comparison run at the profile default measures a round nobody flies.
        SubStepSeconds = Arsenal.ReentryVehicleMk21.SubStepSeconds,
    };

    /// <summary>Fly the round to the ground on the same inverse-square field the predictor uses.</summary>
    private static (double3 Point, double Seconds) FlyTheRound(double3 p, double3 v, double dt)
    {
        var round = new Slug(p, v, null, 1, p, Vec.Zero)
        {
            Munition = Vacuum(),
            Ground = new Ball(),

            // What RoundFields hands a round in flight. Without it the frame's single sample is
            // held across every sub-step, and the round picks up an error that scales with the
            // frame rather than with its own step.
            GravityAt = (position, _) => Body.GravityCci(position),
        };

        for (int i = 0; i < 4_000_000 && round.State == RoundState.Flying; i++)
        {
            round.Update(dt, null, Body.GravityCci(round.PositionEcl), Vec.Zero, Vec.Zero,
                         round.Munition);
        }

        return (round.PositionEcl, round.Age);
    }

    /// <summary>
    /// Configured as the game configures it, the round and the predictor agree to tens of metres
    /// over a twenty-minute fall — and the answer does not move with the frame rate, which is what
    /// says the round is integrating on its own clock rather than the display's.
    /// </summary>
    [Fact]
    public void TheRoundAndThePredictorAgreeAndTheFrameRateDoesNotEnter()
    {
        double3 p = new(R + 400_000.0, 0, 0);
        double3 v = new(0, 7400.0, 0);

        Assert.True(ImpactPredictor.TryPredict(Body, p, v, 0.25, 6.0 * 3600.0,
                                               out ImpactPredictor.Impact predicted));

        var (at30, t30) = FlyTheRound(p, v, 1.0 / 30.0);
        var (at60, t60) = FlyTheRound(p, v, 1.0 / 60.0);

        double gap30 = (at30 - predicted.PointCci).Length();
        double gap60 = (at60 - predicted.PointCci).Length();

        _out.WriteLine($"flight {predicted.Seconds:F0} s at {Vec.Len(predicted.VelocityCci):F0} m/s");
        _out.WriteLine($"  30 fps: {gap30:F1} m, {t30 - predicted.Seconds:+0.000;-0.000} s");
        _out.WriteLine($"  60 fps: {gap60:F1} m, {t60 - predicted.Seconds:+0.000;-0.000} s");

        // The display must not move the impact. A player with a better card gets the same shot.
        Assert.True(Math.Abs(gap30 - gap60) < 5.0,
                    $"the frame rate moved the impact: {gap30:F1} m at 30 fps, {gap60:F1} at 60");

        // Tens of metres over a 1,200 s fall, not hundreds.
        Assert.True(gap30 < 150.0, $"{gap30:F0} m apart, which is more than the integrators owe");
    }

    /// <summary>
    /// And the fault a naive fixture invents: one gravity sample a frame, held across every
    /// sub-step, is worth over a kilometre and moves with the display. It is what
    /// <see cref="RoundFields.GravityAt"/> exists to prevent, and it is why this file sets it.
    /// </summary>
    [Fact]
    public void OneGravitySampleAFrameIsWorthAKilometreAndMovesWithTheFrameRate()
    {
        double3 p = new(R + 400_000.0, 0, 0);
        double3 v = new(0, 7400.0, 0);

        Assert.True(ImpactPredictor.TryPredict(Body, p, v, 0.25, 6.0 * 3600.0,
                                               out ImpactPredictor.Impact predicted));

        static double Frozen(double3 p, double3 v, double dt, double3 aim)
        {
            var round = new Slug(p, v, null, 1, p, Vec.Zero)
            {
                Munition = Vacuum(),
                Ground = new Ball(),
            };

            for (int i = 0; i < 8_000_000 && round.State == RoundState.Flying; i++)
            {
                round.Update(dt, null, Body.GravityCci(round.PositionEcl), Vec.Zero, Vec.Zero,
                             round.Munition);
            }

            return (round.PositionEcl - aim).Length();
        }

        double slow = Frozen(p, v, 1.0 / 30.0, predicted.PointCci);
        double fast = Frozen(p, v, 1.0 / 60.0, predicted.PointCci);

        _out.WriteLine($"frozen gravity: {slow:F0} m at 30 fps, {fast:F0} m at 60");

        Assert.True(slow > 1000.0, $"expected a kilometre of invented error, got {slow:F0} m");
        Assert.True(slow > 1.5 * fast, $"and expected it to move with the frame: {slow:F0} vs {fast:F0}");
    }
}
