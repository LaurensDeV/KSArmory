using Xunit;

namespace KSArmory.Tests;

public class ViewShakeTests
{
    [Fact]
    public void FivePsiShakesAtFullAndAFifthOfItAtLessThanHalf()
    {
        Assert.Equal(1.0, ViewShake.Strength(ViewShake.FullPascals), 9);
        Assert.Equal(1.0, ViewShake.Strength(ViewShake.FullPascals * 10.0), 9);
        Assert.InRange(ViewShake.Strength(ViewShake.FullPascals * 0.2), 0.4, 0.5);
        Assert.Equal(0.0, ViewShake.Strength(0.0));
    }

    /// <summary>Nothing before the front arrives or after it has rattled out, and never more than the most.</summary>
    [Fact]
    public void ItShakesOnlyWhileTheFrontRattlesAndNeverPastTheMost()
    {
        const double seconds = 1.2;
        Assert.Equal((0.0, 0.0, 0.0), ViewShake.At(1.0, -0.01, seconds, 7));
        Assert.Equal((0.0, 0.0, 0.0), ViewShake.At(1.0, seconds + 0.01, seconds, 7));

        double most = 0.0;
        for (double t = 0.0; t <= seconds; t += 0.002)
        {
            (double x, double y, double roll) = ViewShake.At(1.0, t, seconds, 7);
            Assert.InRange(Math.Abs(x), 0.0, ViewShake.MostShare);
            Assert.InRange(Math.Abs(y), 0.0, ViewShake.MostShare);
            Assert.InRange(Math.Abs(roll), 0.0, ViewShake.MostShare);
            most = Math.Max(most, Math.Abs(x));
        }

        Assert.True(most > ViewShake.MostShare * 0.3);
    }

    /// <summary>The jolt is at the front of it: the first tenth rattles harder than the last half.</summary>
    [Fact]
    public void ItDiesAway()
    {
        const double seconds = 1.5;
        double early = 0.0, late = 0.0;

        for (double t = 0.03; t < seconds * 0.15; t += 0.001) early = Math.Max(early, Math.Abs(ViewShake.At(1.0, t, seconds, 3).X));
        for (double t = seconds * 0.5; t < seconds; t += 0.001) late = Math.Max(late, Math.Abs(ViewShake.At(1.0, t, seconds, 3).X));

        Assert.True(early > late * 3.0);
    }

    [Fact]
    public void AWeakFrontDrawsNothing()
    {
        for (double t = 0.0; t < 1.0; t += 0.01)
        {
            Assert.Equal((0.0, 0.0, 0.0), ViewShake.At(0.02, t, 1.0, 1));
        }
    }
}
