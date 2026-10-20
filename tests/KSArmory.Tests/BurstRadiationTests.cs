using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class BurstRadiationTests
{
    private const double Radius = 6_371_000.0;
    private static readonly BodyAir Earth = new(101_325, 1.225, 8_000, 167_000, 9.81, Radius, BodyTraits.Default);

    private static double SeaLevelPrompt(double kt, double slant) => BurstRadiation.PromptDoseSv(kt, slant, 1.225 * slant);

    [Theory]
    [InlineData(1_170.0, 4.2)]
    [InlineData(1_620.0, 0.8)]
    [InlineData(2_090.0, 0.15)]
    public void ThePromptDoseReproducesHiroshimasMeasuredKermaToAQuarter(double slant, double gray)
        => Assert.InRange(SeaLevelPrompt(15.0, slant) / gray, 0.75, 1.25);

    [Fact]
    public void AMegatonsPromptDosePutsFiveSievertsNearThreeKilometres()
        => Assert.InRange(SeaLevelPrompt(1_000.0, 2_900.0), 3.5, 7.0);

    [Fact]
    public void InVacuumThePromptDoseFallsAsTheInverseSquare()
        => Assert.Equal(0.25, BurstRadiation.PromptDoseSv(1_000.0, 200_000.0, 0.0)
                              / BurstRadiation.PromptDoseSv(1_000.0, 100_000.0, 0.0), 9);

    [Fact]
    public void ThePromptRateDeliversTheWholeDose()
    {
        double dose = 0.0, dt = 0.001;
        for (double t = 0.0; t < 200.0; t += dt) dose += BurstRadiation.PromptRate(1.0, 1_000.0, t + (0.5 * dt)) * dt;

        Assert.Equal(1.0, dose, 3);
    }

    [Fact]
    public void FalloutFollowsTheSevenTenRule()
        => Assert.InRange(BurstRadiation.Decay(7.0 * 3600.0) / BurstRadiation.Decay(3600.0), 0.09, 0.11);

    [Fact]
    public void FissionShareRunsFromWholeToHalfWithoutAStep()
    {
        Assert.Equal(1.0, BurstRadiation.FissionShare(100.0), 12);
        Assert.Equal(0.5, BurstRadiation.FissionShare(1_000.0), 12);
        Assert.Equal(0.75, BurstRadiation.FissionShare(Math.Sqrt(10.0) * 100.0), 9);
    }

    [Theory]
    [InlineData(1_000.0, true)]
    [InlineData(1.0, false)]
    public void ThePlumeLaysExactlyTheLocalFallout(double kt, bool hasAir)
    {
        BurstRadiation.Plume plume = BurstRadiation.Plume.For(kt, 1.0, hasAir);
        double reach = hasAir ? 30.0 * plume.Wind * plume.MeanFallSeconds : 8.0 * plume.StemSigma;
        double step = hasAir ? 500.0 : plume.StemSigma / 40.0;

        double sum = 0.0;
        for (double x = -8.0 * plume.StemSigma; x < reach; x += step)
        {
            double across = 8.0 * (plume.CapRadius + (BurstRadiation.CrosswindSpread * Math.Max(x, 0.0)) + plume.StemSigma);
            for (double y = -across; y < across; y += step)
            {
                sum += plume.UnitRate(x + (0.5 * step), y + (0.5 * step)) * step * step;
            }
        }

        Assert.Equal(1.0, sum / (BurstRadiation.PlaneSvM2PerSecondPerKt * plume.FissionKt * plume.Local), 2);
    }

    [Fact]
    public void ThePointSourceHoldsWhatThePlaneHolds()
    {
        double lambda = BurstRadiation.GammaColumnKgPerM2 / 1.225;
        double sum = 0.0, dr = 0.01;
        for (double r = 0.0; r < 40.0 * lambda; r += dr)
        {
            double rho = r + (0.5 * dr);
            double d = Math.Sqrt((rho * rho) + 1.0);
            sum += BurstRadiation.PointSvM2PerSecondPerKt / (4.0 * Math.PI * d * d) * Math.Exp(-d / lambda) * 2.0 * Math.PI * rho * dr;
        }

        Assert.Equal(1.0, sum / BurstRadiation.PlaneSvM2PerSecondPerKt, 2);
    }

    [Fact]
    public void AllTheLocalFalloutLandsAndTheRestStaysUp()
    {
        BurstRadiation.Plume plume = BurstRadiation.Plume.For(1_000.0, 1.0, true);

        Assert.Equal(plume.FissionKt, plume.AloftKt(0.0), 9);
        Assert.Equal(plume.FissionKt * (1.0 - plume.Local), plume.AloftKt(1.0e7), 3);
    }

    [Fact]
    public void AnAirBurstLaysNothing()
    {
        BurstRadiation.Plume plume = BurstRadiation.Plume.For(20.0, 0.0, true);

        Assert.False(plume.Lays);
        Assert.Equal(0.0, plume.GroundRate(0.0, 0.0, 3600.0));
    }

    [Fact]
    public void FalloutDownwindArrivesWithTheWind()
    {
        BurstRadiation.Plume plume = BurstRadiation.Plume.For(1_000.0, 1.0, true);
        double x = 100_000.0, arrival = x / plume.Wind;

        Assert.Equal(0.0, plume.GroundRate(x, 0.0, 0.5 * arrival));
        Assert.True(plume.GroundRate(x, 0.0, 1.5 * arrival) > 0.0);
    }

    [Fact]
    public void AShellDeepInTheFieldHoldsForAYearAndOneFurtherOutForDays()
    {
        Assert.Equal(BurstRadiation.BeltLongLifeSeconds, BurstRadiation.BeltLifeSeconds(1.2));
        Assert.Equal(BurstRadiation.BeltShortestLifeSeconds, BurstRadiation.BeltLifeSeconds(4.0));
    }

    [Fact]
    public void TheColumnIsTheAirAlongThePath()
    {
        double3 a = new(Radius + 1.0, 0, 0);

        Assert.Equal(1.225 * 1_000.0, BurstRadiation.Column(Earth, a, a + new double3(0, 1_000.0, 0)), 0);
        Assert.InRange(BurstRadiation.Column(Earth, a, a * (1.0 + (166_000.0 / Radius)), 4_000) / Earth.ColumnAbove(0.0), 0.99, 1.01);
    }
}
