using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Letting six warheads go one at a time, and what it does when it cannot.
/// </summary>
public class ReleaseSequenceTests(ITestOutputHelper Out)
{
    private static double3[] Axes()
    {
        Tube[] tubes = Arsenal.MirvBus.Tubes;
        double3[] axes = new double3[tubes.Length];
        for (int i = 0; i < tubes.Length; i++) axes[i] = Vec.Unit(tubes[i].Direction);
        return axes;
    }

    private static ReleaseSequence Started(out double3[] axes, out double3 reference)
    {
        axes = Axes();
        ReleaseSequence deploy = new();
        Assert.True(deploy.Begin(axes));
        reference = deploy.ReferenceCci;
        return deploy;
    }

    private static ReleaseSituation At(int tube, double3 axisNow, double sweep,
                                   double windowSeconds = double.PositiveInfinity,
                                   int tubesLeft = 6)
        => new(ReadyToDeploy: true, NextTube: tube, TubesLeft: tubesLeft, NextTubeAxisCci: axisNow,
               SweepMetresPerSecond: sweep, SecondsLeftToDeploy: windowSeconds,
               HeldDirectionCci: new double3(1, 0, 0), HeldRollCci: new double3(0, 0, 1));

    [Fact]
    public void ItWillNotReleaseWhileTheTubeIsOffTheLine()
    {
        ReleaseSequence deploy = Started(out double3[] axes, out _);

        ReleaseCommand r = deploy.Update(1.0 / 60.0, At(0, axes[0], 0.0));

        Assert.False(r.ReleaseNow);
        Assert.Contains("turning onto tube 1", r.Said);
        Out.WriteLine(r.Said);
    }

    [Fact]
    public void NorWhileTheTubesAreSweeping()
    {
        ReleaseSequence deploy = Started(out _, out double3 reference);

        ReleaseCommand r = deploy.Update(1.0 / 60.0, At(0, reference, sweep: 0.5));

        Assert.False(r.ReleaseNow);
        Assert.Contains("settling", r.Said);
    }

    [Fact]
    public void ItReleasesOnceTheTubeIsOnTheLineAndStill()
    {
        ReleaseSequence deploy = Started(out _, out double3 reference);

        ReleaseCommand r = deploy.Update(1.0 / 60.0, At(0, reference, sweep: 0.0));

        Assert.True(r.ReleaseNow);
        Assert.Equal("", r.Said);
    }

    /// <summary>
    /// A bus that cannot point lets one go late and then stops trying, rather than spending the
    /// whole coast failing six times over.
    /// </summary>
    [Fact]
    public void ABusThatCannotPointReleasesAnywayAndStopsTryingForTheRest()
    {
        ReleaseSequence deploy = Started(out double3[] axes, out _);

        ReleaseCommand r = default;
        for (int i = 0; i < 60 * 61 && !r.ReleaseNow; i++) r = deploy.Update(1.0 / 60.0, At(0, axes[0], 0.0));

        Assert.True(r.ReleaseNow);
        Assert.Contains("off the line", r.Said);
        Out.WriteLine(r.Said);

        // The next tube is not turned onto at all, so it goes as soon as it is steady.
        ReleaseCommand next = deploy.Update(1.0 / 60.0, At(1, axes[1], 0.0));
        Assert.True(next.ReleaseNow);
    }

    [Fact]
    public void RunningOutOfWindowStopsRepointingRatherThanHoldingWarheads()
    {
        ReleaseSequence deploy = Started(out double3[] axes, out _);

        // Twenty seconds left and six to go is under the floor per warhead.
        ReleaseCommand r = deploy.Update(1.0 / 60.0, At(0, axes[0], 0.0, windowSeconds: 20.0));

        Assert.True(r.ReleaseNow);
    }

    /// <summary>
    /// With nothing latched it is exactly the gate that flew before it: steady, or give up and say
    /// so. That is the behaviour six salvos were flown on, so it must survive being moved here.
    /// </summary>
    [Fact]
    public void BeforeItBeginsItIsExactlyTheOldSteadyGate()
    {
        ReleaseSequence deploy = new();

        Assert.True(deploy.Update(1.0 / 60.0, At(0, Vec.Zero, sweep: 0.0)).ReleaseNow);
        Assert.False(deploy.Update(1.0 / 60.0, At(0, Vec.Zero, sweep: 0.5)).ReleaseNow);

        ReleaseCommand late = default;
        for (int i = 0; i < 60 * 61 && !late.ReleaseNow; i++)
        {
            late = deploy.Update(1.0 / 60.0, At(0, Vec.Zero, sweep: 0.5));
        }

        Assert.True(late.ReleaseNow);
        Assert.Contains("scatter", late.Said);
    }

    [Fact]
    public void EachTubeGetsItsOwnClock()
    {
        ReleaseSequence deploy = Started(out double3[] axes, out _);

        for (int i = 0; i < 60 * 50; i++) deploy.Update(1.0 / 60.0, At(0, axes[0], 0.0));

        // Tube 1 went; tube 2 must not inherit its fifty seconds of waiting.
        ReleaseCommand r = deploy.Update(1.0 / 60.0, At(1, axes[1], 0.0));
        Assert.False(r.ReleaseNow);
    }

    [Fact]
    public void NothingLeftToFireHoldsTheNominalLine()
    {
        ReleaseSequence deploy = Started(out _, out _);

        ReleaseCommand r = deploy.Update(1.0 / 60.0, At(-1, Vec.Zero, 0.0));

        Assert.False(r.ReleaseNow);
        Assert.Equal(new double3(1, 0, 0), r.DirectionCci);
    }
}
