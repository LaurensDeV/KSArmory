using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Which way out a prediction that found no impact took.
///
/// <para>From outside every exit is the same <c>false</c>, and the release probe's flown failures took
/// the one that costs most: the whole six-hour horizon at the air step, about 85,000 steps, for a state
/// a millimetre from one that lands. <c>docs/ACCURACY-PLAN.md</c> 3ep.</para>
/// </summary>
public class PredictorEndingTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    // What KsaWorld.MediumDensityRatioAt reads over Earth: exponential air to the top of the
    // atmosphere, and the ocean anywhere under the mean sphere, whether or not there is land above it.
    internal static double KsaMedium(double3 pointCci)
    {
        double altitude = Vec.Len(pointCci) - R;
        if (altitude < 0.0) return 1025.0 / Medium.ReferenceDensityKgPerM3;
        return altitude >= 167_410.0 ? 0.0 : Math.Exp(-altitude / 8_000.0);
    }

    // Round 6's own release on GeoSat FAT 4_1, 2026-09-17-airepoch shot 003, and the ground its salvo
    // stopped on at 24 S 62 W.
    internal static readonly double3 FlownPositionCci = new(3851501.6, -5923005.6, -1394966.0);
    internal static readonly double3 FlownVelocityCci = new(214.1032, 3024.3040, -3918.9163);
    internal const double FlownGroundMetres = 203.9;

    [Fact]
    public void AnArcThatLandsSaysSo()
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, FlownPositionCci, FlownVelocityCci, 2.0,
                                               ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit,
                                               out ImpactPredictor.Ending ending, _ => R + 4_000.0, null,
                                               new ImpactPredictor.Drag(KsaMedium, Arsenal.ReentryVehicleMk21),
                                               stopOnTheSurface: true));

        Assert.Equal(ImpactPredictor.Exit.Landed, ending.Exit);
        Assert.Equal(hit.Seconds, ending.Seconds);
        Assert.True(ending.Steps > 0);
        Assert.InRange(ending.DensestRatio, 0.5, 1.0);
    }

    [Fact]
    public void AClosedOrbitClearOfTheGroundIsNeverFlown()
    {
        double3 at = new(R + 400_000.0, 0, 0);

        Assert.False(ImpactPredictor.TryPredict(Earth, at, new double3(0, Math.Sqrt(Mu / at.X), 0), 2.0, 600.0,
                                                out _, out ImpactPredictor.Ending ending));

        Assert.Equal(ImpactPredictor.Exit.NeverComesDown, ending.Exit);
        Assert.Equal(0, ending.Steps);
        Assert.Equal(R + 400_000.0, ending.PeriapsisRadius, 1e-3);
    }

    [Fact]
    public void AnEscapeRunsOutOfTimeWithNoClosedConic()
    {
        Assert.False(ImpactPredictor.TryPredict(Earth, new double3(R + 400_000.0, 0, 0), new double3(0, 12_000.0, 0),
                                                2.0, 600.0, out _, out ImpactPredictor.Ending ending));

        Assert.Equal(ImpactPredictor.Exit.OutOfTime, ending.Exit);
        Assert.True(ending.Seconds >= 600.0);
        Assert.Equal(300, ending.Steps);
        Assert.True(double.IsNaN(ending.PeriapsisRadius));
    }

    [Fact]
    public void StartingUnderTheGroundAndSinkingEndsUnderground()
    {
        Assert.False(ImpactPredictor.TryPredict(Earth, new double3(R - 10.0, 0, 0), new double3(-1.0, 0, 0),
                                                2.0, 600.0, out _, out ImpactPredictor.Ending ending));

        Assert.Equal(ImpactPredictor.Exit.Underground, ending.Exit);
        Assert.Equal(1, ending.Steps);
    }

    [Fact]
    public void AStepThatLeavesTheNumbersEndsNotFinite()
    {
        Assert.False(ImpactPredictor.TryPredict(Earth, FlownPositionCci, FlownVelocityCci, 2.0, 600.0,
                                                out _, out ImpactPredictor.Ending ending, null, null,
                                                new ImpactPredictor.Drag(_ => 1e300, Arsenal.ReentryVehicleMk21)));

        Assert.Equal(ImpactPredictor.Exit.NotFinite, ending.Exit);
    }

    [Fact]
    public void AStateThatIsNotANumberIsUnflyable()
    {
        Assert.False(ImpactPredictor.TryPredict(Earth, new double3(double.NaN, 0, 0), FlownVelocityCci, 2.0, 600.0,
                                                out _, out ImpactPredictor.Ending ending));

        Assert.Equal(ImpactPredictor.Exit.Unflyable, ending.Exit);
    }

    /// <summary>
    /// The flown failure, as the log will now name it: the air step's stages dip under the mean sphere
    /// beneath 204 m of ground, read the ocean there, and throw the warhead off the planet faster than it
    /// can escape — so nothing is ever under the ground again and the whole horizon is flown.
    /// </summary>
    [Fact]
    public void TheFlownFailureIsAHorizonFlownAfterReadingTheOceanUnderTheLand()
    {
        int failed = 0;

        foreach (double3 from in AlongTheTrack())
        {
            if (ImpactPredictor.TryPredict(Earth, from, FlownVelocityCci, 2.0, ImpactPredictor.DefaultMaxSeconds,
                                           out _, out ImpactPredictor.Ending ending, _ => R + FlownGroundMetres, null,
                                           new ImpactPredictor.Drag(KsaMedium, Arsenal.ReentryVehicleMk21),
                                           stopOnTheSurface: true))
            {
                continue;
            }

            failed++;
            if (failed == 1) Out.WriteLine(ending.Said(R));

            Assert.Equal(ImpactPredictor.Exit.OutOfTime, ending.Exit);
            Assert.Equal(ImpactPredictor.AtmosphericStepSeconds, ending.StepSeconds);
            Assert.True(ending.DensestRatio > 800.0, $"densest {ending.DensestRatio}");
            Assert.True(ending.FastestMetresPerSecond > 11_200.0, $"fastest {ending.FastestMetresPerSecond}");
        }

        Out.WriteLine($"{failed} of {Samples} releases along two seconds of track found no impact");
        Assert.True(failed > 0);
    }

    internal const int Samples = 1000;

    // Two coarse steps of track, so every phase of the step grid against the ground is flown.
    internal static IEnumerable<double3> AlongTheTrack()
    {
        double3 along = Vec.Unit(FlownVelocityCci);
        double spacing = 2.0 * 2.0 * Vec.Len(FlownVelocityCci) / Samples;

        for (int i = 0; i < Samples; i++) yield return FlownPositionCci + along * (i * spacing);
    }
}
