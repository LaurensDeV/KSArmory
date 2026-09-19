using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A barrel's run back and return. Drawn only, so what matters is that it never draws a barrel out
/// of battery when the gun has not fired, and never leaves one run back for good.
/// </summary>
public class GunRecoilTests
{
    private const double Travel = 0.30;
    private const double Run = 0.08;
    private const double Return = 0.60;

    private static double At(double t) => GunRecoil.Offset(t, Travel, Run, Return);

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    [InlineData(-0.01)]
    public void AGunThatHasNotFiredIsInBattery(double since) => Assert.Equal(0.0, At(since));

    [Fact]
    public void TheRoundLeavesBeforeTheBarrelMoves() => Assert.Equal(0.0, At(0.0));

    [Fact]
    public void ItRunsBackToFullTravelAndNoFurther()
    {
        Assert.Equal(Travel, At(Run), 12);
        for (double t = 0.0; t <= Run + Return + 0.1; t += 0.001)
        {
            Assert.InRange(At(t), 0.0, Travel + 1e-12);
        }
    }

    [Fact]
    public void TheRunIsMonotonicAndSoIsTheReturn()
    {
        for (double t = 0.001; t <= Run; t += 0.001) Assert.True(At(t) >= At(t - 0.001) - 1e-12, $"run at {t}");
        for (double t = Run + 0.001; t <= Run + Return; t += 0.001) Assert.True(At(t) <= At(t - 0.001) + 1e-12, $"return at {t}");
    }

    [Fact]
    public void RecoilIsQuickerThanCounterRecoil()
    {
        // Half the travel is reached far sooner on the way back than it is lost on the way home.
        double run = 0.0, home = 0.0;
        for (double t = 0.0; t <= Run; t += 1e-4) if (At(t) < Travel / 2) run = t;
        for (double t = Run; t <= Run + Return; t += 1e-4) if (At(t) > Travel / 2) home = t - Run;
        Assert.True(run < home, $"half travel in {run:F4} s, back to half in {home:F4} s");
    }

    [Fact]
    public void ItIsBackInBatteryOnceCounterRecoilEnds()
    {
        Assert.Equal(0.0, At(Run + Return));
        Assert.Equal(0.0, At(Run + Return + 5.0));
    }

    [Fact]
    public void NoTravelMeansNothingMoves()
    {
        for (double t = 0.0; t < 1.0; t += 0.05) Assert.Equal(0.0, GunRecoil.Offset(t, 0.0, Run, Return));
    }
}
