using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// A warhead's prediction is flown through the air alone, never the ocean the medium reports under the
/// mean sphere.
///
/// <para>A warhead stops at the ground or the waterline, so it never flies through water; but
/// <c>KsaWorld.MediumDensityRatioAt</c> reads the ocean anywhere under the mean sphere, land above it or not.
/// Under the 204 m of ground at 24 S 62 W, the stages of the air step that crosses the ground reach there,
/// read 837x the air, and throw the warhead off the planet — the release probe's flown failures.
/// <c>IcbmComputer.DensityRatioAt</c> asks for the air alone; this cannot reach it, and pins what that
/// buys. <c>docs/ACCURACY-PLAN.md</c> 3ep.</para>
/// </summary>
public class PredictedMediumTests(ITestOutputHelper Out)
{
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(3.986004418e14, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double AirAlone(double3 pointCci)
    {
        double altitude = Math.Max(0.0, Vec.Len(pointCci) - R);
        return altitude >= 167_410.0 ? 0.0 : Math.Exp(-altitude / 8_000.0);
    }

    [Fact]
    public void TheAirAloneLandsEveryReleaseAndMovesNoneThatAlreadyLanded()
    {
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;
        int oceanFailed = 0, landed = 0;

        foreach (double3 from in PredictorEndingTests.AlongTheTrack())
        {
            bool throughTheOcean = Predict(from, PredictorEndingTests.KsaMedium, warhead,
                                           PredictorEndingTests.FlownGroundMetres, out ImpactPredictor.Impact wet);
            Assert.True(Predict(from, AirAlone, warhead, PredictorEndingTests.FlownGroundMetres,
                                out ImpactPredictor.Impact dry));

            if (!throughTheOcean)
            {
                oceanFailed++;
                continue;
            }

            // Over this ground the ocean is only ever read by a step the crossing throws away, so every
            // prediction it did not wreck is the same number to the bit.
            landed++;
            Assert.Equal(wet.GroundFixedPointCci, dry.GroundFixedPointCci);
            Assert.Equal(wet.VelocityCci, dry.VelocityCci);
            Assert.Equal(wet.Seconds, dry.Seconds);
        }

        Out.WriteLine($"through the ocean {oceanFailed} of {PredictorEndingTests.Samples} found no impact; "
                      + $"through the air alone none, and the other {landed} landed identically");
        Assert.True(oceanFailed > 0);
    }

    /// <summary>
    /// At the waterline the ocean does not fail loudly — it answers, and the answer is wrong.
    /// </summary>
    /// <remarks>
    /// Under 204 m of ground the stages that read the ocean belong to a step the crossing discards, so a
    /// prediction it does not wreck is identical. At sea level the ground <i>is</i> the mean sphere, so the
    /// stages of the step that lands read 837x the air and the impact moves — which no failure path reports
    /// and no `release probe:` line can show. It is the aim a player clicking an ocean gets.
    /// </remarks>
    [Fact]
    public void AtTheWaterlineTheOceanMovesTheLandingRatherThanFailing()
    {
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;
        int moved = 0, failed = 0;
        double worst = 0.0;

        foreach (double3 from in PredictorEndingTests.AlongTheTrack())
        {
            Assert.True(Predict(from, AirAlone, warhead, 0.0, out ImpactPredictor.Impact dry));

            if (!Predict(from, PredictorEndingTests.KsaMedium, warhead, 0.0, out ImpactPredictor.Impact wet))
            {
                failed++;
                continue;
            }

            double apart = Vec.Len(wet.GroundFixedPointCci - dry.GroundFixedPointCci);
            if (apart == 0.0) continue;

            moved++;
            worst = Math.Max(worst, apart);
        }

        Out.WriteLine($"at sea level, through the ocean: {failed} of {PredictorEndingTests.Samples} found no impact "
                      + $"and {moved} landed somewhere else, the furthest {worst / 1000.0:F1} km off; "
                      + "through the air alone every one landed");

        Assert.True(moved > 0, "the ocean has to move a landing here, or this pins nothing");
        Assert.True(worst > 1_000.0, $"a moved landing is a wrong answer, not a rounding; worst {worst:F3} m");
    }

    private static bool Predict(double3 from, Func<double3, double> medium, MunitionProfile warhead,
                                double groundMetres, out ImpactPredictor.Impact hit)
        => ImpactPredictor.TryPredict(Earth, from, PredictorEndingTests.FlownVelocityCci, 2.0,
                                      ImpactPredictor.DefaultMaxSeconds, out hit,
                                      _ => R + groundMetres, null,
                                      new ImpactPredictor.Drag(medium, warhead), stopOnTheSurface: true);
}
