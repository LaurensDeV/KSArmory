using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Which aim the burn's freeze keeps. The readings are the shape of every rocket on 2026-10-11's 6,181 km night: the
/// best banked at 160 m, a swing out, and the loop then converging to metres that the flat 250 m band cannot bank.
/// <c>docs/ICBM-OUTSTANDING.md</c> 1.16.
/// </summary>
public class AimFreezeTests
{
    private static double3 Target => new(6_371_000.0, 0.0, 0.0);
    private static double3 LandedBy(double metres) => Target + new double3(0.0, metres, 0.0);

    // The bias each reading was taken with, in order.
    private static (AimCorrection Aim, double3[] ReadWith) Walked(params double[] misses)
    {
        AimCorrection aim = new();
        double3[] with = new double3[misses.Length];

        for (int i = 0; i < misses.Length; i++)
        {
            with[i] = aim.BiasCci;
            aim.Observe(LandedBy(misses[i]), Target);
        }

        return (aim, with);
    }

    [Fact]
    public void TheFreezeRevertsAConvergedLoopToAStaleBest()
    {
        var (aim, with) = Walked(160.0, 600.0, 40.0, 4.0);

        aim.Freeze();

        Assert.Equal(with[0], aim.BiasCci);
    }

    [Fact]
    public void KeepingABetterRecentAimFreezesOnTheReadingThatMissedLeast()
    {
        var (aim, with) = Walked(160.0, 600.0, 40.0, 4.0);

        aim.Freeze(keepABetterRecentAim: true);

        Assert.Equal(with[3], aim.BiasCci);
        Assert.True(aim.Settled);
    }

    [Fact]
    public void RecentAimsWorseThanTheBestStillRevertToTheBest()
    {
        var (aim, with) = Walked(40.0, 200.0, 300.0);

        aim.Freeze(keepABetterRecentAim: true);

        Assert.Equal(with[0], aim.BiasCci);
    }

    // Shot 1 of 2026-10-11's evening night: converged to metres, then the prediction jumped 1.4 km a second before the
    // arrival latched.
    [Fact]
    public void AJumpJustBeforeTheLatchDoesNotSendTheFreezeBackToTheStaleBest()
    {
        var (aim, with) = Walked(160.0, 600.0, 40.0, 4.0, 1_380.0);

        aim.Freeze(keepABetterRecentAim: true);

        Assert.Equal(with[3], aim.BiasCci);
    }

    [Fact]
    public void AReadingOlderThanTheWindowIsNotKept()
    {
        double[] misses = [160.0, 4.0, .. Enumerable.Repeat(170.0, 10)];
        var (aim, with) = Walked(misses);

        aim.Freeze(keepABetterRecentAim: true);

        Assert.Equal(with[0], aim.BiasCci);
    }
}
