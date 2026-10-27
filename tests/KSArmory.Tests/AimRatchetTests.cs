using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// <see cref="AimCorrection.Freeze"/>, run at every release, reverts to the banked aim, and with the
/// flat 250 m band nothing banks below 250 m: flown reverts up to 153.3 m against 2.0–4.8 m for a
/// band that follows the miss. <c>docs/ACCURACY-PLAN.md</c> 3ca.
/// </summary>
public class AimRatchetTests
{
    private static double3 Target => new(6_371_000.0, 0.0, 0.0);
    private static double3 LandedBy(double metres) => Target + new double3(0.0, metres, 0.0);

    private static AimCorrection Walked(bool tracksTheMiss, params double[] misses)
    {
        AimCorrection aim = new();

        foreach (double m in misses) aim.Observe(LandedBy(m), Target, tracksTheMiss);

        return aim;
    }

    /// <summary>
    /// The first reading always banks, because the best starts at infinity. After that the flat
    /// band needs 250 m of improvement, which a converged shot can never produce.
    /// </summary>
    [Theory]
    [InlineData(250.0)]
    [InlineData(120.0)]
    [InlineData(12.0)]
    [InlineData(1.0)]
    public void TheFlatBandCannotBankAnythingBetterOnceUnderIt(double settled)
    {
        // Bank once at `settled`, then hand it a run of steadily better readings.
        AimCorrection aim = Walked(false, settled, settled / 2, settled / 8, settled / 64, 0.0);

        Assert.Equal(settled, aim.BestMissMetres, 6);
    }

    [Theory]
    [InlineData(250.0)]
    [InlineData(12.0)]
    public void FollowingTheMissKeepsTheRatchetAlive(double settled)
    {
        AimCorrection aim = Walked(true, settled, settled / 2, settled / 8, settled / 64, 0.0);

        Assert.True(aim.BestMissMetres < settled / 8,
                    $"banked {aim.BestMissMetres:F2} m from a {settled:F0} m start");
    }

    /// <summary>
    /// The other arm of the same comparison is dead too, so nothing stops the loop on its own and
    /// the shipped aim is decided entirely by the terminal freeze.
    /// </summary>
    [Fact]
    public void NorCanTheFlatBandEverCountAPassAsWorse()
    {
        double[] misses = new double[4 * AimCorrection.WorseBeforeStopping];
        misses[0] = 40.0;
        // Ten times worse than the banked best, and still inside a 250 m band.
        for (int i = 1; i < misses.Length; i++) misses[i] = 240.0;

        Assert.False(Walked(false, misses).Settled);
    }
}
