using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The air a fireball ionised: a sphere a radar cannot see through, for a time that grows with the
/// yield, and nothing at all once it has cleared.
/// </summary>
public class FireballBlackoutTests
{
    private const double Kt = 1.0e6;

    [Fact]
    public void ABiggerBurstBlacksOutLongerAndWider()
    {
        Assert.True(FireballBlackout.Seconds(340.0 * Kt) > FireballBlackout.Seconds(0.3 * Kt) * 5.0);
        Assert.True(FireballBlackout.Radius(340.0 * Kt, 1.0) > FireballBlackout.Radius(0.3 * Kt, 0.1) * 5.0);
    }

    [Fact]
    public void ItClearsByShrinkingRatherThanSwitchingOff()
    {
        double charge = 340.0 * Kt;
        double life = FireballBlackout.Seconds(charge);
        double full = FireballBlackout.Radius(charge, 1.0);

        Assert.Equal(full, FireballBlackout.Radius(charge, life * 0.5));
        Assert.InRange(FireballBlackout.Radius(charge, life * 0.85), full * 0.4, full * 0.6);
        Assert.Equal(0.0, FireballBlackout.Radius(charge, life + 0.01));
    }

    [Fact]
    public void AConventionalChargeBlacksOutNothing()
    {
        Assert.Equal(0.0, FireballBlackout.Radius(500.0, 0.1));
    }

    [Fact]
    public void ABeamThroughTheRegionIsLost()
    {
        double3 radar = new(0.0, 0.0, 0.0);
        double3 contact = new(30_000.0, 0.0, 1_500.0);
        double3 burst = new(15_000.0, 0.0, 700.0);

        Assert.True(FireballBlackout.Blocks(radar, contact, burst, 1_000.0));
        Assert.False(FireballBlackout.Blocks(radar, contact, burst + new double3(0.0, 5_000.0, 0.0), 1_000.0));
    }

    [Fact]
    public void EitherEndInsideTheRegionIsLost()
    {
        double3 burst = new(15_000.0, 0.0, 0.0);

        Assert.True(FireballBlackout.Blocks(burst, new double3(40_000.0, 0.0, 0.0), burst, 500.0));
        Assert.True(FireballBlackout.Blocks(new double3(0.0, 0.0, 0.0), burst, burst, 500.0));
    }
}
