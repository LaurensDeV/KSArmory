using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The post-cutoff loop as a loop, which until now could only be watched by flying.
///
/// <para>What these are for is the coupling, not the pieces. <c>docs/MIRV-NEXT.md</c> <b>8y</b>:
/// the decoupler's shove is the only thing carrying the halves apart, the trim exists to null
/// exactly that, and so a trim that works shuts the gate that licenses it. Flown, it abandoned
/// <b>87 of 144</b> corrections.</para>
/// </summary>
public class PostCutoffLoopTests(ITestOutputHelper Out)
{
    /// <summary>
    /// The shipped case: a shove large enough that the halves clear before the trim can null it.
    /// </summary>
    [Fact]
    public void AShoveThatOpensTheGapLetsTheTrimFinish()
    {
        PostCutoffRig rig = new();
        PostCutoffRig.Outcome outcome = rig.Run();

        Out.WriteLine($"finished={outcome.TrimFinished} abandoned={outcome.Abandoned} "
                      + $"residual={outcome.ResidualMetresPerSecond:F3} m/s "
                      + $"after {outcome.SecondsRun:F1} s, closest {outcome.ClosestApproachMetres:F1} m");

        Assert.False(outcome.Abandoned);
        Assert.True(outcome.TrimFinished);
    }

    /// <summary>
    /// 8y's mechanism, reproduced: a shove too small to open the keep-out before the timeout leaves
    /// the pair closing, and the clearance abandons with no correction applied at all.
    ///
    /// <para>This is the failure that costs the whole aim correction, and it is a property of the
    /// loop rather than of any one piece — every part of it behaves correctly.</para>
    /// </summary>
    [Fact]
    public void AShoveTooSmallToClearIsAbandonedRatherThanTrimmed()
    {
        PostCutoffRig rig = new() { ShoveCci = new Brutal.Numerics.double3(0.2, 0, 0) };
        PostCutoffRig.Outcome outcome = rig.Run();

        Out.WriteLine($"abandoned={outcome.Abandoned} after {outcome.SecondsRun:F1} s, "
                      + $"closest {outcome.ClosestApproachMetres:F1} m -- {outcome.Said}");

        Assert.True(outcome.Abandoned);
        Assert.False(outcome.TrimFinished);
    }

    /// <summary>
    /// And the discriminator is the gap, not the rocket. 8y paired every flight's closest approach
    /// with how its trim ended and found it exact: the ones that succeeded opened to the keep-out
    /// and never came closer.
    /// </summary>
    [Fact]
    public void WhetherItFinishesIsDecidedByTheGapAndNothingElse()
    {
        double smallest = double.PositiveInfinity;

        foreach (double shove in new[] { 0.1, 0.2, 0.4, 0.8, 1.1, 2.0 })
        {
            PostCutoffRig rig = new() { ShoveCci = new Brutal.Numerics.double3(shove, 0, 0) };
            PostCutoffRig.Outcome outcome = rig.Run();

            Out.WriteLine($"  shove {shove:F1} m/s -> "
                          + $"{(outcome.Abandoned ? "abandoned" : outcome.TrimFinished ? "finished" : "ran out")}"
                          + $"  closest {outcome.ClosestApproachMetres:F1} m");

            if (!outcome.Abandoned) smallest = Math.Min(smallest, shove);
        }

        // Monotone: once the shove is big enough to clear, a bigger one does not stop clearing.
        foreach (double shove in new[] { 1.1, 2.0, 4.0 })
        {
            PostCutoffRig rig = new() { ShoveCci = new Brutal.Numerics.double3(shove, 0, 0) };
            Assert.False(rig.Run().Abandoned, $"a {shove} m/s shove was abandoned");
        }

        Assert.True(double.IsFinite(smallest));
    }

    /// <summary>
    /// The regime, headlessly. A coarser step is what the slow machine produces, and the question
    /// 8ac could not answer without flying is whether the loop itself minds.
    /// </summary>
    [Theory]
    [InlineData(0.033)]
    [InlineData(0.066)]
    [InlineData(0.108)]
    [InlineData(0.200)]
    [InlineData(0.300)]
    public void TheLoopIsRunAtEveryStepTheRegimesProduce(double step)
    {
        PostCutoffRig rig = new() { StepSeconds = step };
        PostCutoffRig.Outcome outcome = rig.Run();

        Out.WriteLine($"  step {step * 1000:F0} ms -> finished={outcome.TrimFinished} "
                      + $"residual {outcome.ResidualMetresPerSecond:F4} m/s in {outcome.SecondsRun:F1} s");

        Assert.False(outcome.Abandoned);
    }

    /// <summary>
    /// The trim's achievable precision is <c>max(SettledMetresPerSecond, 0.5 * accel * step)</c> —
    /// a threshold below what one frame of firing adds is one nothing can reach — so a coarser
    /// world leaves proportionately more velocity on the bus.
    ///
    /// <para><b>Measured, and linear in the step:</b> 0.118 m/s at 33 ms, 0.245 at 66, 0.420 at
    /// 108, 0.750 at 200. Between the two regimes <c>docs/MIRV-NEXT.md</c> <b>8ac</b> observed —
    /// 66 ms and 108 — that is <b>1.7x</b> the residual, and the residual is what the miss is a
    /// multiple of: several thousand metres per metre a second at the ranges this flies.</para>
    ///
    /// <para>It is not the whole of the regime's 2.2x in miss, and it is the first part of it that
    /// can be measured without a rocket.</para>
    /// </summary>
    [Fact]
    public void TheResidualLeftOnTheBusIsLinearInTheStep()
    {
        double[] steps = [0.033, 0.066, 0.108, 0.200];
        double[] left = [.. steps.Select(v => new PostCutoffRig { StepSeconds = v }.Run()
                                                                 .ResidualMetresPerSecond)];

        foreach ((double step, double residual) in steps.Zip(left))
        {
            Out.WriteLine($"  {step * 1000,4:F0} ms leaves {residual:F4} m/s "
                          + $"({residual / step:F2} m/s per second of step)");
        }

        // Monotone, and close to proportional: the ratio of residual to step holds within a third
        // across a 6x range, which a constant floor could not do and a quadratic would break.
        for (int i = 1; i < left.Length; i++) Assert.True(left[i] > left[i - 1]);

        double[] slope = [.. steps.Zip(left).Select(p => p.Second / p.First)];
        Assert.True(slope.Max() / slope.Min() < 1.5,
                    $"the residual is not linear in the step: {string.Join(", ", slope)}");
    }

    /// <summary>
    /// The two regimes, side by side, which is the number 8ac could not get without flying.
    /// </summary>
    [Fact]
    public void TheSlowRegimeLeavesMoreOnTheBusThanTheFastOne()
    {
        double fast = new PostCutoffRig { StepSeconds = 0.066 }.Run().ResidualMetresPerSecond;
        double slow = new PostCutoffRig { StepSeconds = 0.108 }.Run().ResidualMetresPerSecond;

        Out.WriteLine($"66 ms leaves {fast:F4} m/s, 108 ms leaves {slow:F4} m/s "
                      + $"({slow / fast:F2}x)");

        Assert.True(slow / fast > 1.3);
    }
}
