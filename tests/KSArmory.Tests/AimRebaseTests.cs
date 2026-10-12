using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The aim across a step in the plant under it -- the solids' jettison inside the closed loop, which moved the far
/// coast's prediction 1.1 m to 1.57 km in one cycle. <c>docs/ICBM-OUTSTANDING.md</c> 1.14.
/// </summary>
public class AimRebaseTests
{
    private static double3 Target => new(6_371_000.0, 0.0, 0.0);
    private static double3 LandedBy(double metres) => Target + new double3(0.0, metres, 0.0);

    // Converged to metres on the solid stage's projection, as the far coast was for two minutes.
    private static AimCorrection Converged()
    {
        AimCorrection aim = new();
        foreach (double miss in new[] { 300.0, 20.0, 2.0, 1.0 }) aim.Observe(LandedBy(miss), Target);
        return aim;
    }

    [Fact]
    public void AStepUnderAConvergedLoopSettlesItBackOntoTheOldBest()
    {
        AimCorrection aim = Converged();

        for (int i = 0; i < AimCorrection.WorseBeforeStopping + 2; i++) aim.Observe(LandedBy(1_570.0), Target);

        Assert.True(aim.Settled);
    }

    [Fact]
    public void RebasedTheLoopKeepsCorrectingThroughTheStep()
    {
        AimCorrection aim = Converged();
        double3 before = aim.BiasCci;

        aim.Rebase();
        for (int i = 0; i < AimCorrection.WorseBeforeStopping + 2; i++) aim.Observe(LandedBy(1_570.0), Target);

        Assert.False(aim.Settled);
        Assert.NotEqual(before, aim.BiasCci);
    }
}
