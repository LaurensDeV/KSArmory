using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary><see cref="IcbmConfig.ShortShotPushesThroughAtDragShare"/>: <c>docs/ICBM-OUTSTANDING.md</c> 1.12.</summary>
public class PushThroughDragShareTests
{
    private static double EngagedSecondsIntoTheRamp(IcbmFlightRig rig, double km, double step, double share)
    {
        double ramp = 0.0, engaged = double.NaN;
        rig.AimLoop = new Watch((p, h) =>
        {
            ramp = p.Phase == IcbmPhase.ClosedLoop && p.Countdown < IcbmProgram.ThrottleDownSeconds ? ramp + h : 0.0;
            if (double.IsNaN(engaged) && p.Phase == IcbmPhase.ClosedLoop
                && p.HoldDirectionBelowNow <= IcbmProgram.PushesThroughHoldBelow && p.VelocityToGain > 2.0)
                engaged = ramp;
        });
        IcbmConfig config = FloorHoldTests.Config(0.5, true, true);
        config.ShortShotPushesThroughAtDragShare = share;
        rig.Fly(new IcbmProgram(config), GameStackShortRangeTests.South(km * 1000.0), step, 6_000.0);
        return engaged;
    }

    private sealed class Watch(Action<IcbmProgram, double> after) : IcbmFlightRig.IAimLoop
    {
        public double3 Apply(double3 aimNowCci) => aimNowCci;
        public bool IsSteady => true;
        public void AfterUpdate(IcbmProgram p, in IcbmCommand command, double3 aimNowCci, double h) => after(p, h);
    }

    // Flown, LowTWR's ramp reads 0.60 three seconds in; the time alone waits ten.
    [Fact]
    public void ARampSettledAgainstItsDragPushesThroughEarly()
    {
        double engaged = EngagedSecondsIntoTheRamp(LowTwrPushStudy.LowTwr(), 200.0, 0.02, 0.6);
        Assert.True(engaged < 6.0, $"pushed {engaged:F1} s into the ramp");
    }

    [Fact]
    public void AStackWhoseRampIsClosingPushesNoEarlierThanTheTime()
    {
        foreach (double km in FloorHoldTests.LowArcs)
        {
            double withShare = EngagedSecondsIntoTheRamp(GameStack(), km, 0.02, 0.6);
            double timeAlone = EngagedSecondsIntoTheRamp(GameStack(), km, 0.02, 0.0);
            Assert.True(withShare.Equals(timeAlone), $"{km:F0} km pushed {withShare:F1} s into the ramp against {timeAlone:F1}");
        }
    }

    private static IcbmFlightRig GameStack()
    {
        IcbmFlightRig rig = GameStackShortRangeTests.GameStack(true);
        rig.AttitudeHasInertia = true;
        rig.StepJitter = 0.5;
        return rig;
    }
}
