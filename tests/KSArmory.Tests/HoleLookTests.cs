using Xunit;

namespace KSArmory.Tests;

/// <summary>How big a shell's mark on a hull is.</summary>
public class HoleLookTests
{
    [Fact]
    public void TheHoleIsOneAndAHalfCalibres()
    {
        Assert.Equal(0.030, HoleLook.RadiiFor(20.0, 0.05).Core, 9);
        Assert.Equal(0.1905, HoleLook.RadiiFor(127.0, 3.3).Core, 9);
    }

    /// <summary>The soot follows the charge by its cube root, as every other radius of a burst does.</summary>
    [Fact]
    public void EightTimesTheChargeIsTwiceTheSoot()
    {
        double small = HoleLook.RadiiFor(20.0, 0.5).Scorch;
        double large = HoleLook.RadiiFor(20.0, 4.0).Scorch;

        Assert.Equal(2.0 * small, large, 9);
    }

    [Fact]
    public void AnInertRoundStillLeavesSootRoundItsHole()
    {
        (double core, double scorch) = HoleLook.RadiiFor(30.0, 0.0);

        Assert.True(core > 0.0);
        Assert.Equal(core * HoleLook.LeastScorchInCores, scorch, 9);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    public void ARoundWithNoCalibreIsMarkedAsTheSmallestShipped(double calibre)
    {
        Assert.Equal(HoleLook.RadiiFor(HoleLook.UnstatedCalibreMm, 0.05), HoleLook.RadiiFor(calibre, 0.05));
    }
}
