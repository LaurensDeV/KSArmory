using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Letting a magazine go along one line, and what it does when the vehicle will not co-operate.
/// </summary>
public class ReleaseSequenceTests(ITestOutputHelper Out)
{
    // What the bus throws at, which is what prices a cant. Off the profile rather than repeated,
    // because the sequencer takes it from the munition now and a second copy here would drift.
    private static readonly double Ejection = LoudKick.MetresPerSecond;

    private const double Step = 1.0 / 60.0;

    private static double3[] Axes()
    {
        Tube[] tubes = CantedRing.Tubes;
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
                                   int tubesLeft = 6, double3 noseNow = default)
        => new(ReadyToDeploy: true, NextTube: tube, TubesLeft: tubesLeft, NextTubeAxisCci: axisNow,
               NoseAxisCci: noseNow,
               EjectionMetresPerSecond: Ejection, SweepMetresPerSecond: sweep, SecondsLeftToDeploy: windowSeconds,
               HeldDirectionCci: new double3(1, 0, 0), HeldRollCci: new double3(0, 0, 1));

    // A tube axis a stated number of degrees off the line, turned about one fixed perpendicular so
    // the whole sweep is one degree of freedom.
    private static double3 OffTheLine(double3 reference, double degrees)
        => Vec.Unit(doubleQuat.CreateFromAxisAngle(Vec.AnyPerpendicular(reference),
                                                   degrees * Math.PI / 180.0) * reference);

    /// <summary>
    /// One magazine per sequence. A launcher reloads — three seconds after a salvo it is full again
    /// — and a ballistic missile deploys once.
    ///
    /// <para>Flown 2026-08-24 before the latch: a six-tube bus put <b>sixty</b> warheads down. The
    /// first six grouped at 2.88-2.90 km and every salvo after them went as the previous one landed,
    /// the last of them from a few hundred metres up, 606 km from the aim point. The scenario scored
    /// all sixty — docs/MIRV-NEXT.md item 0c.</para>
    /// </summary>
    [Fact]
    public void AReloadedLauncherDoesNotGetASecondSalvo()
    {
        ReleaseSequence deploy = Started(out double3[] axes, out _);

        // Let the whole magazine go, one tube at a time on its own axis.
        int released = 0;

        for (int tube = 0; tube < axes.Length; tube++)
        {
            for (int frame = 0; frame < 60 * 120; frame++)
            {
                ReleaseCommand r = deploy.Update(
                    Step, At(tube, axes[tube], 0.0, tubesLeft: axes.Length - tube));

                if (r.ReleaseNow) { released++; break; }
            }
        }

        Assert.Equal(axes.Length, released);
        Assert.False(deploy.Emptied, "it latched before the last round had gone");

        // The magazine reports empty, which is the frame the latch is taken on.
        deploy.Update(Step, At(-1, Vec.Zero, 0.0, tubesLeft: 0));
        Assert.True(deploy.Emptied, "an emptied magazine did not end the sequence");

        // ...and now the launcher reloads, exactly as it does in flight.
        int again = 0;

        for (int tube = 0; tube < axes.Length; tube++)
        {
            for (int frame = 0; frame < 60 * 120; frame++)
            {
                ReleaseCommand r = deploy.Update(
                    Step, At(tube, axes[tube], 0.0, tubesLeft: axes.Length - tube));

                if (r.ReleaseNow) { again++; break; }
            }
        }

        Out.WriteLine($"{released} away, {again} after the reload");

        Assert.Equal(0, again);

        // A reset is a new flight, and it does fill again.
        deploy.Reset();
        Assert.False(deploy.Emptied);
    }

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

    /// <summary>
    /// The two gates that flew were independent thresholds, and independent thresholds compare
    /// nothing. One budget swaps both of these verdicts round, because what reaches the round is the
    /// sum: 0.4° while sweeping 0.045 m/s is 0.059 m/s at the tube, and a full degree while dead
    /// still is 0.035.
    /// </summary>
    [Theory]
    [InlineData(0.4, 0.045, false)]
    [InlineData(1.0, 0.000, true)]
    public void ItIsTheSumThatDecides(double offDegrees, double sweep, bool releases)
    {
        ReleaseSequence deploy = Started(out _, out double3 reference);

        ReleaseCommand r = deploy.Update(Step, At(0, OffTheLine(reference, offDegrees), sweep));

        Out.WriteLine($"{ReleaseSequence.LateralFromCant(offDegrees, Ejection) + sweep:F3} m/s at the tube: {r.Said}");
        Assert.Equal(releases, r.ReleaseNow);
    }

    /// <summary>
    /// A bus that cannot null its residual rate has a sweep no waiting improves, so the pointing is
    /// the only term left and the release goes at the pointing's own best. Flown, the tube reached
    /// the line four seconds in against a sweep that floored at 0.113 m/s, was refused for being
    /// unsteady, and went fifteen seconds later at 5.1° — a larger lateral error, not a smaller one.
    /// </summary>
    [Fact]
    public void ABusWithAFloorUnderItsSweepReleasesAtThePointingsBest()
    {
        ReleaseSequence deploy = Started(out _, out double3 reference);

        ReleaseCommand r = default;
        double elapsed = 0.0;

        // A window giving each of six warheads the ~24 s that flight's deadline came to.
        for (int i = 0; i < 60 * 60 && !r.ReleaseNow; i++)
        {
            elapsed = i * Step;
            r = deploy.Update(Step, At(0, OffTheLine(reference, FlownOffDegrees(elapsed)),
                                       sweep: 0.113, windowSeconds: 143.0));
        }

        double lateral = 0.113 + ReleaseSequence.LateralFromCant(r.OffLineDegrees, Ejection);
        Out.WriteLine($"{elapsed:F1} s  {r.Said}  ({lateral:F3} m/s at the tube)");

        Assert.True(r.ReleaseNow);
        Assert.True(elapsed < 6.0,
                    $"the best the vehicle offered was four seconds in; this released at {elapsed:F1} s");
        Assert.True(r.OffLineDegrees < 1.5,
                    $"released {r.OffLineDegrees:F1} deg off the line, having been on it earlier");

        // The turn worked — it is the vehicle that will not hold still — so the rest are still
        // turned onto rather than dumped on the mean axis.
        ReleaseCommand next = deploy.Update(Step, At(1, OffTheLine(reference, 6.0), sweep: 0.113,
                                                     windowSeconds: 143.0));
        Assert.False(next.ReleaseNow);
        Assert.Contains("turning onto tube 2", next.Said);
    }

    /// <summary>
    /// A vehicle that can hold is released inside the budget and not a moment before. It crosses the
    /// line three times on the way, each time with the tubes sweeping hard, and none of those is the
    /// best it will give — a release taken at one of them would be several times the dispersion of
    /// waiting.
    /// </summary>
    [Fact]
    public void AVehicleThatCanHoldIsStillReleasedInsideTheBudget()
    {
        ReleaseSequence deploy = Started(out _, out double3 reference);

        ReleaseCommand r = default;
        double elapsed = 0.0;
        double sweep = 0.0;
        bool temptedMidSwing = false;

        for (int i = 0; i < 60 * 30 && !r.ReleaseNow; i++)
        {
            elapsed = i * Step;
            double off = Math.Abs(SettlingOffDegrees(elapsed));
            sweep = SettlingSweep(elapsed);

            if (off < 0.5 && sweep > 4.0 * ReleaseSequence.LateralBudgetMetresPerSecond)
            {
                temptedMidSwing = true;
            }

            r = deploy.Update(Step, At(0, OffTheLine(reference, off), sweep));
        }

        double lateral = sweep + ReleaseSequence.LateralFromCant(r.OffLineDegrees, Ejection);
        Out.WriteLine($"{elapsed:F1} s  {r.Said}  ({lateral:F3} m/s at the tube)");

        Assert.True(r.ReleaseNow);
        Assert.True(temptedMidSwing, "the model never put the tube on the line while sweeping hard, "
                                     + "so it does not exercise what it claims to");
        Assert.True(lateral <= ReleaseSequence.LateralBudgetMetresPerSecond,
                    $"a vehicle that can hold released at {lateral:F3} m/s at the tube, outside the "
                    + $"{ReleaseSequence.LateralBudgetMetresPerSecond:F3} budget it could have met");
    }

    // The three angles one flight left in the log, joined by straight lines: 6.0° when the turn was
    // commanded, 0.5° at 4.2 s, 7.0° at 8.5 s, and back to the 5.1° it was eventually released at.
    private static double FlownOffDegrees(double seconds)
        => seconds <= 4.2 ? 6.0 - (5.5 / 4.2) * seconds
         : seconds <= 8.5 ? 0.5 + (6.5 / 4.3) * (seconds - 4.2)
         : seconds <= 18.5 ? 7.0 - (1.9 / 10.0) * (seconds - 8.5)
         : 5.1;

    // A stack with authority: it swings onto the commanded attitude, overshoots, and settles.
    private static double SettlingOffDegrees(double seconds)
        => 6.0 * Math.Exp(-seconds / 2.0) * Math.Cos(2.0 * seconds);

    // The tube's own motion, 2.6 m out from the axis the vehicle is turning about.
    private static double SettlingSweep(double seconds)
        => 2.6 * Math.Abs(6.0 * Math.Exp(-seconds / 2.0)
                          * (-0.5 * Math.Cos(2.0 * seconds) - 2.0 * Math.Sin(2.0 * seconds)))
           * Math.PI / 180.0;

    [Fact]
    public void NothingLeftToFireHoldsTheNominalLine()
    {
        ReleaseSequence deploy = Started(out _, out _);

        ReleaseCommand r = deploy.Update(Step, At(-1, Vec.Zero, 0.0));

        Assert.False(r.ReleaseNow);
        Assert.Equal(new double3(1, 0, 0), r.DirectionCci);
    }
}
