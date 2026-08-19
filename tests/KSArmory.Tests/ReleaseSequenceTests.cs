using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Letting a magazine go along one line, and what it does when the vehicle will not co-operate.
/// </summary>
public class ReleaseSequenceTests(ITestOutputHelper Out)
{
    private const double Step = 1.0 / 60.0;

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

    // A tube axis a stated number of degrees off the line, turned about one fixed perpendicular so
    // the whole sweep is one degree of freedom.
    private static double3 OffTheLine(double3 reference, double degrees)
        => Vec.Unit(doubleQuat.CreateFromAxisAngle(Vec.AnyPerpendicular(reference),
                                                   degrees * Math.PI / 180.0) * reference);

    [Fact]
    public void ItWillNotReleaseWhileTheTubeIsOffTheLine()
    {
        ReleaseSequence deploy = Started(out double3[] axes, out _);

        ReleaseCommand r = deploy.Update(Step, At(0, axes[0], 0.0));

        Assert.False(r.ReleaseNow);
        Assert.Contains("turning onto tube 1", r.Said);
        Out.WriteLine(r.Said);
    }

    [Fact]
    public void NorWhileTheTubesAreSweeping()
    {
        ReleaseSequence deploy = Started(out _, out double3 reference);

        ReleaseCommand r = deploy.Update(Step, At(0, reference, sweep: 0.5));

        Assert.False(r.ReleaseNow);
        Assert.Contains("settling", r.Said);
    }

    [Fact]
    public void ItReleasesOnceTheTubeIsOnTheLineAndStill()
    {
        ReleaseSequence deploy = Started(out _, out double3 reference);

        ReleaseCommand r = deploy.Update(Step, At(0, reference, sweep: 0.0));

        Assert.True(r.ReleaseNow);
        Out.WriteLine(r.Said);
    }

    /// <summary>
    /// Every release leaves a record, not only the ones that went wrong. Six impact points are only
    /// diagnosable against the six release states that produced them, and nothing else keeps those.
    /// </summary>
    [Fact]
    public void EveryReleaseSaysWhichTubeWentAndOnWhat()
    {
        ReleaseSequence deploy = Started(out _, out double3 reference);

        ReleaseCommand r = deploy.Update(Step, At(2, reference, sweep: 0.01));

        Assert.True(r.ReleaseNow);
        Assert.Contains("releasing tube 3", r.Said);
        Assert.Contains("off the line", r.Said);
        Assert.Contains("0.010 m/s", r.Said);
        Out.WriteLine(r.Said);
    }

    /// <summary>
    /// The decision about pacing, made explicit. A sequencer with nothing to turn for lets the whole
    /// magazine go as fast as the steadiness gate allows, because warheads off one release state
    /// share a time of flight and a paced salvo gives each of them a different one.
    /// </summary>
    [Fact]
    public void TheWarheadsGoTogetherWhenThereIsNothingToTurnFor()
    {
        ReleaseSequence deploy = new();

        for (int tube = 0; tube < 6; tube++)
        {
            ReleaseCommand r = deploy.Update(Step, At(tube, Vec.Zero, sweep: 0.0, tubesLeft: 6 - tube));
            Assert.True(r.ReleaseNow, $"tube {tube + 1} was held back with nothing to turn for");
        }
    }

    /// <summary>
    /// The command is one constant rotation of a frozen attitude, so an error that grows is the
    /// vehicle failing to hold it. Waiting that out to the per-tube timeout costs the salvo its time
    /// on target for an answer already in.
    /// </summary>
    [Fact]
    public void AnErrorThatGrowsIsGivenUpOnRatherThanWaitedOut()
    {
        ReleaseSequence deploy = Started(out _, out double3 reference);

        ReleaseCommand r = default;
        double elapsed = 0.0;

        // Six degrees off and drifting further at the rate one flight measured, which is the shape
        // a bus with no attitude authority leaves in the log.
        for (int i = 0; i < 60 * 30 && !r.ReleaseNow; i++)
        {
            elapsed = i * Step;
            r = deploy.Update(Step, At(0, OffTheLine(reference, 6.0 + 0.63 * elapsed), 0.0));
        }

        Out.WriteLine($"{r.Said} (after {elapsed:F1} s)");

        Assert.True(r.ReleaseNow);
        Assert.True(elapsed < 5.0,
                    $"a diverging turn should be given up on in seconds, not at the {ReleaseSequence.PerTubeTimeoutSeconds:F0} s "
                    + $"timeout; this one took {elapsed:F1} s");
        Assert.Contains("not following the turn", r.Said);
    }

    /// <summary>
    /// A bus that cannot point lets one go and then stops trying, rather than spending the whole
    /// coast failing six times over.
    /// </summary>
    [Fact]
    public void ABusThatCannotPointReleasesAnywayAndStopsTryingForTheRest()
    {
        ReleaseSequence deploy = Started(out double3[] axes, out _);

        ReleaseCommand r = default;
        double elapsed = 0.0;

        for (int i = 0; i < 60 * 61 && !r.ReleaseNow; i++)
        {
            elapsed = i * Step;
            r = deploy.Update(Step, At(0, axes[0], 0.0));
        }

        Out.WriteLine($"{r.Said} (after {elapsed:F1} s)");

        Assert.True(r.ReleaseNow);
        Assert.Contains("stopped closing on the line", r.Said);

        // A turn that never closes is settled in ten seconds; sixty is the timeout for one that is
        // still coming round.
        Assert.True(elapsed < ReleaseSequence.NoProgressSeconds + 2.0,
                    $"a stalled turn should be given up on near {ReleaseSequence.NoProgressSeconds:F0} s; "
                    + $"this one took {elapsed:F1} s");

        // The next tube is not turned onto at all, so it goes as soon as it is steady.
        ReleaseCommand next = deploy.Update(Step, At(1, axes[1], 0.0));
        Assert.True(next.ReleaseNow);
    }

    /// <summary>
    /// One tube's whole clock spent not settling is the evidence. Making every later tube pay the
    /// same wait for the same answer is what turned a salvo into three minutes, and each of those
    /// waits puts the next warhead on a different release state.
    /// </summary>
    [Fact]
    public void AVehicleThatWillNotSettleDoesNotMakeEveryTubePayTheWait()
    {
        ReleaseSequence deploy = new();

        int fired = 0;
        double elapsed = 0.0;

        // The sweep one flight sat at against a 0.05 m/s gate, and a window that gives each of six
        // warheads about half a minute.
        for (int i = 0; i < 60 * 600 && fired < 6; i++)
        {
            elapsed = i * Step;
            ReleaseCommand r = deploy.Update(Step, At(fired, Vec.Zero, sweep: 0.082,
                                                      windowSeconds: 170.0, tubesLeft: 6 - fired));
            if (r.ReleaseNow)
            {
                Out.WriteLine($"{elapsed,6:F1} s  {r.Said}");
                fired++;
            }
        }

        Assert.Equal(6, fired);
        Assert.True(elapsed < 60.0,
                    $"a salvo off a vehicle that will not settle took {elapsed:F0} s; the evidence is "
                    + "in after the first tube");
    }

    [Fact]
    public void RunningOutOfWindowStopsRepointingRatherThanHoldingWarheads()
    {
        ReleaseSequence deploy = Started(out double3[] axes, out _);

        // Twenty seconds left and six to go is under the floor per warhead.
        ReleaseCommand r = deploy.Update(Step, At(0, axes[0], 0.0, windowSeconds: 20.0));

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

        Assert.True(deploy.Update(Step, At(0, Vec.Zero, sweep: 0.0)).ReleaseNow);
        Assert.False(deploy.Update(Step, At(0, Vec.Zero, sweep: 0.5)).ReleaseNow);

        ReleaseCommand late = default;
        for (int i = 0; i < 60 * 61 && !late.ReleaseNow; i++)
        {
            late = deploy.Update(Step, At(0, Vec.Zero, sweep: 0.5));
        }

        Assert.True(late.ReleaseNow);
        Assert.Contains("scatter", late.Said);
    }

    /// <summary>
    /// The per-tube clock is reset rather than carried, and the check has to straddle the deadline
    /// to say anything: below it both a reset clock and an inherited one hold their round.
    /// </summary>
    [Fact]
    public void EachTubeGetsItsOwnClock()
    {
        ReleaseSequence deploy = Started(out double3[] axes, out double3 reference);

        // Sixty seconds of window over six warheads is a ten-second deadline each. The tube closes
        // on the line the whole time, so nothing gives up on the turn.
        for (int i = 0; i < 60 * 10 - 6; i++)
        {
            double off = 6.0 - 0.4 * (i * Step);
            deploy.Update(Step, At(0, OffTheLine(reference, off), 0.0, windowSeconds: 60.0));
        }

        // Nine and a bit seconds are on tube 1's clock. Tube 2 must not start there.
        ReleaseCommand r = deploy.Update(Step, At(1, axes[1], 0.0, windowSeconds: 60.0, tubesLeft: 6));
        Assert.False(r.ReleaseNow);
    }

    /// <summary>
    /// A tube whose axis will not resolve makes no claim about being on the line. Reading the angle
    /// between a degenerate direction and the reference as zero releases every warhead the instant
    /// the launcher's part tree stops answering.
    /// </summary>
    [Fact]
    public void AnUnreadableTubeAxisIsNotATubeOnTheLine()
    {
        ReleaseSequence deploy = Started(out _, out _);

        ReleaseCommand r = deploy.Update(Step, At(0, Vec.Zero, sweep: 0.0));

        Assert.False(r.ReleaseNow);
        Assert.Contains("blind", r.Said);
        Out.WriteLine(r.Said);
    }

    [Fact]
    public void NothingLeftToFireHoldsTheNominalLine()
    {
        ReleaseSequence deploy = Started(out _, out _);

        ReleaseCommand r = deploy.Update(Step, At(-1, Vec.Zero, 0.0));

        Assert.False(r.ReleaseNow);
        Assert.Equal(new double3(1, 0, 0), r.DirectionCci);
    }
}
