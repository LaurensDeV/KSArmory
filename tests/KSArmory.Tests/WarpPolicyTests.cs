using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The rules for keeping the world slow enough to simulate what is in the air.
///
/// <para>Every one of these fails against the behaviour this replaced, which clamped the step and
/// returned nothing — there was no decision to assert on at all.</para>
/// </summary>
public class WarpPolicyTests
{
    private const double Faithful = Interceptor.MaxFaithfulStep;

    // A step at 60 fps for the given warp, which is how the engine actually produces one.
    private static double StepAt(double warp) => warp / 60.0;

    [Fact]
    public void ANormalStepIsLeftAlone()
    {
        var policy = new WarpPolicy();

        WarpDecision d = policy.Decide(StepAt(1.0), 1.0, roundsInFlight: true, enabled: true);

        Assert.Equal(WarpAction.None, d.Action);
        Assert.False(policy.Holding);
    }

    [Fact]
    public void WarpBelowTheLimitIsLeftAlone()
    {
        var policy = new WarpPolicy();

        // 10x at 60 fps is 167 ms, comfortably inside the 320 ms a round can integrate.
        WarpDecision d = policy.Decide(StepAt(10.0), 10.0, roundsInFlight: true, enabled: true);

        Assert.Equal(WarpAction.None, d.Action);
        Assert.False(policy.Holding);
    }

    [Fact]
    public void AnOverrunWithRoundsUpAsksForALowerSpeed()
    {
        var policy = new WarpPolicy();

        WarpDecision d = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);

        Assert.Equal(WarpAction.Slow, d.Action);
        Assert.True(d.Speed < 600.0);
        Assert.True(policy.Holding);
        Assert.Equal(600.0, policy.HeldSpeed);
    }

    /// <summary>
    /// The requested speed must actually produce a step inside the limit, at the frame rate
    /// implied by what was just measured. Asking for "something lower" is not enough.
    /// </summary>
    [Theory]
    [InlineData(30.0)]
    [InlineData(120.0)]
    [InlineData(600.0)]
    [InlineData(1200.0)]
    public void TheRequestedSpeedProducesAStepInsideTheLimit(double warp)
    {
        var policy = new WarpPolicy();
        double step = StepAt(warp);

        WarpDecision d = policy.Decide(step, warp, roundsInFlight: true, enabled: true);

        Assert.Equal(WarpAction.Slow, d.Action);

        // frameTime is step/warp, so the step the requested speed will produce is speed*frameTime.
        double frameTime = step / warp;
        Assert.True(d.Speed * frameTime <= Faithful,
                    $"{warp}x -> {d.Speed}x still steps {d.Speed * frameTime * 1000:F0} ms");
    }

    [Fact]
    public void NothingHappensWhenTheAirIsClear()
    {
        var policy = new WarpPolicy();

        WarpDecision d = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: false, enabled: true);

        Assert.Equal(WarpAction.None, d.Action);
        Assert.False(policy.Holding);
    }

    [Fact]
    public void TheSettingTurnsTheWholeMechanismOff()
    {
        var policy = new WarpPolicy();

        WarpDecision d = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: false);

        Assert.Equal(WarpAction.None, d.Action);
        Assert.False(policy.Holding);
    }

    [Fact]
    public void TheSpeedComesBackWhenTheRoundsLand()
    {
        var policy = new WarpPolicy();

        WarpDecision held = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
        Assert.Equal(WarpAction.Slow, held.Action);

        // The world is now sitting at what we asked for, and the last round has landed.
        WarpDecision back = policy.Decide(StepAt(held.Speed), held.Speed,
                                          roundsInFlight: false, enabled: true);

        Assert.Equal(WarpAction.Restore, back.Action);
        Assert.Equal(600.0, back.Speed);
        Assert.False(policy.Holding);
    }

    [Fact]
    public void TurningTheSettingOffGivesTheSpeedBack()
    {
        var policy = new WarpPolicy();

        WarpDecision held = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
        WarpDecision back = policy.Decide(StepAt(held.Speed), held.Speed,
                                          roundsInFlight: true, enabled: false);

        Assert.Equal(WarpAction.Restore, back.Action);
        Assert.Equal(600.0, back.Speed);
    }

    /// <summary>
    /// A player who moves the speed while it is held has overridden us. Restoring then would undo
    /// a deliberate choice — the one way this feature could take control and not give it back.
    /// </summary>
    [Fact]
    public void APlayerWhoChangesSpeedWhileHeldIsNotOverridden()
    {
        var policy = new WarpPolicy();

        policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);

        // They set 4x themselves; the rounds then land.
        WarpDecision back = policy.Decide(StepAt(4.0), 4.0, roundsInFlight: false, enabled: true);

        Assert.Equal(WarpAction.None, back.Action);
        Assert.False(policy.Holding);
    }

    /// <summary>
    /// If the world will not take the speed at all, there is nothing honest left to do. KSA
    /// rejects a speed change outright while its own auto-warp runs, and a salvo the player is
    /// told about beats the 124 km miss that clamping produced in flight.
    /// </summary>
    [Fact]
    public void AWorldThatNeverTakesTheSpeedEndsInAbandon()
    {
        var policy = new WarpPolicy();

        // The request is never observed: the speed stays where it was, frame after frame.
        WarpDecision last = WarpDecision.Nothing;
        for (int i = 0; i <= WarpPolicy.FramesAwaitingWrite + 1; i++)
        {
            last = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
        }

        Assert.Equal(WarpAction.Abandon, last.Action);
        Assert.False(policy.Holding);
    }

    [Fact]
    public void ItDoesNotAbandonWhileTheWriteIsStillLanding()
    {
        var policy = new WarpPolicy();

        for (int i = 0; i < WarpPolicy.FramesAwaitingWrite; i++)
        {
            WarpDecision d = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
            Assert.NotEqual(WarpAction.Abandon, d.Action);
        }
    }

    /// <summary>
    /// The step arriving on the frame a write takes effect still measures the interval *before*
    /// it. Dividing by that again reduces on top of a reduction already in flight: 30x becomes
    /// 9.9x and then 3.2x, and the pair repeats for as long as the salvo lasts.
    /// </summary>
    [Fact]
    public void AStaleStepDoesNotReduceTheSpeedTwice()
    {
        var policy = new WarpPolicy();

        WarpDecision first = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
        Assert.Equal(WarpAction.Slow, first.Action);

        // The write lands, but this frame's step still describes the interval at 600x.
        WarpDecision onLanding = policy.Decide(StepAt(600.0), first.Speed,
                                               roundsInFlight: true, enabled: true);
        Assert.Equal(WarpAction.None, onLanding.Action);

        // And the settle step after it, still carrying the old interval.
        WarpDecision settling = policy.Decide(StepAt(600.0), first.Speed,
                                              roundsInFlight: true, enabled: true);
        Assert.Equal(WarpAction.None, settling.Action);

        Assert.True(policy.Holding);
        Assert.Equal(600.0, policy.HeldSpeed);
    }

    /// <summary>
    /// Once a step measured at the speed we asked for arrives and is still too long, reducing
    /// again is correct — that is the slow-frame case, not the stale-step one.
    /// </summary>
    [Fact]
    public void AFreshStepThatStillOverrunsDoesReduceAgain()
    {
        var policy = new WarpPolicy();

        WarpDecision first = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
        policy.Decide(StepAt(600.0), first.Speed, roundsInFlight: true, enabled: true);   // lands
        policy.Decide(StepAt(600.0), first.Speed, roundsInFlight: true, enabled: true);   // settles

        // A genuinely long frame: at the held speed the step is still over the limit.
        WarpDecision again = policy.Decide(Interceptor.MaxFaithfulStep * 2.0, first.Speed,
                                           roundsInFlight: true, enabled: true);

        Assert.Equal(WarpAction.Slow, again.Action);
        Assert.True(again.Speed < first.Speed);
    }

    /// <summary>
    /// The player's warp control and KSA's auto-warp write the same field this does. Trading
    /// writes with them frame by frame is a loop neither side wins, and in flight it produced a
    /// 10x/3.2x oscillation that ran for the whole salvo. The mod is the guest; it stands down.
    /// </summary>
    [Fact]
    public void SomethingElseDrivingTheSpeedMakesItStandDown()
    {
        var policy = new WarpPolicy();

        WarpDecision held = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
        Assert.Equal(WarpAction.Slow, held.Action);

        WarpDecision last = WarpDecision.Nothing;
        for (int i = 0; i <= WarpPolicy.OverridesBeforeYielding + 1; i++)
        {
            // The write lands, then something puts the speed straight back up.
            policy.Decide(StepAt(600.0), held.Speed, roundsInFlight: true, enabled: true);
            policy.Decide(StepAt(600.0), held.Speed, roundsInFlight: true, enabled: true);
            last = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
            if (last.Action == WarpAction.Yield) break;
        }

        Assert.Equal(WarpAction.Yield, last.Action);
        Assert.True(policy.Yielded);
        Assert.False(policy.Holding);
    }

    /// <summary>Having stood down, it must not restart the fight on the next overrunning frame.</summary>
    [Fact]
    public void OnceStoodDownItStaysDownForTheSalvo()
    {
        var policy = new WarpPolicy();

        WarpDecision held = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
        for (int i = 0; i <= WarpPolicy.OverridesBeforeYielding + 1; i++)
        {
            policy.Decide(StepAt(600.0), held.Speed, roundsInFlight: true, enabled: true);
            policy.Decide(StepAt(600.0), held.Speed, roundsInFlight: true, enabled: true);
            policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
        }

        Assert.True(policy.Yielded);
        for (int i = 0; i < 5; i++)
        {
            WarpDecision d = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
            Assert.Equal(WarpAction.None, d.Action);
        }
    }

    [Fact]
    public void TheAirClearingClearsTheStandDownToo()
    {
        var policy = new WarpPolicy();

        WarpDecision held = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
        for (int i = 0; i <= WarpPolicy.OverridesBeforeYielding + 1; i++)
        {
            policy.Decide(StepAt(600.0), held.Speed, roundsInFlight: true, enabled: true);
            policy.Decide(StepAt(600.0), held.Speed, roundsInFlight: true, enabled: true);
            policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
        }
        Assert.True(policy.Yielded);

        policy.Decide(StepAt(600.0), 600.0, roundsInFlight: false, enabled: true);
        Assert.False(policy.Yielded);

        // A fresh salvo gets a fresh attempt.
        WarpDecision again = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
        Assert.Equal(WarpAction.Slow, again.Action);
    }

    /// <summary>
    /// Asking for a speed at or above the current one is not a reduction, and issuing it would
    /// latch a hold that never converges.
    /// </summary>
    [Fact]
    public void ItNeverAsksForASpeedItIsAlreadyAtOrAbove()
    {
        var policy = new WarpPolicy();

        // Barely over the limit: the computed target is close to the current speed.
        WarpDecision d = policy.Decide(Interceptor.MaxFaithfulStep * 1.01, 1.0,
                                       roundsInFlight: true, enabled: true);

        Assert.True(d.Action != WarpAction.Slow || d.Speed < 1.0);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ANonFiniteStepChangesNothing(double step)
    {
        var policy = new WarpPolicy();

        WarpDecision d = policy.Decide(step, 600.0, roundsInFlight: true, enabled: true);

        Assert.Equal(WarpAction.None, d.Action);
        Assert.False(policy.Holding);
    }

    [Fact]
    public void APausedWorldIsNotSomethingToSlowDown()
    {
        var policy = new WarpPolicy();

        WarpDecision d = policy.Decide(0.0, 0.0, roundsInFlight: true, enabled: true);

        Assert.Equal(WarpAction.None, d.Action);
        Assert.False(policy.Holding);
    }

    /// <summary>
    /// A low frame rate reaches the limit at a lower warp, and the policy must react to the step
    /// it was given rather than to the warp factor — the two only agree at 60 fps.
    /// </summary>
    [Fact]
    public void ItReactsToTheStepNotTheWarpFactor()
    {
        var policy = new WarpPolicy();

        // 10x, but at 15 fps that is a 667 ms step — over the limit despite the modest warp.
        WarpDecision d = policy.Decide(10.0 / 15.0, 10.0, roundsInFlight: true, enabled: true);

        Assert.Equal(WarpAction.Slow, d.Action);
        Assert.True(d.Speed < 10.0);
    }

    /// <summary>
    /// The fight against another writer *produces* good frames: our value lands, one frame runs
    /// inside the limit, and the speed goes straight back up. A stand-down budget cleared by a
    /// good step therefore never reaches its threshold, and the loop runs for the whole salvo.
    ///
    /// <para>This is the shape of the log it was taken from — 28 hold lines across three salvos,
    /// each reading "120.0x still overruns" a few frames after the previous request landed.</para>
    /// </summary>
    [Fact]
    public void AnAlternatingFightStillEndsInStandingDown()
    {
        var policy = new WarpPolicy();

        WarpDecision last = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
        Assert.Equal(WarpAction.Slow, last.Action);

        for (int cycle = 0; cycle < 20 && last.Action != WarpAction.Yield; cycle++)
        {
            double asked = last.Action == WarpAction.Slow ? last.Speed : 11.5;

            // Our write lands, then the settle step passes.
            policy.Decide(StepAt(asked), asked, roundsInFlight: true, enabled: true);
            policy.Decide(StepAt(asked), asked, roundsInFlight: true, enabled: true);

            // Then a genuinely idle frame: holding, settled, and the step inside the limit. This
            // is the one that reaches the good-step branch, and clearing the budget there is what
            // made the loop run forever. Without this call the test cannot see the defect at all.
            policy.Decide(StepAt(asked), asked, roundsInFlight: true, enabled: true);

            // Something else puts the speed straight back up.
            last = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
        }

        Assert.Equal(WarpAction.Yield, last.Action);
        Assert.True(policy.Yielded);
    }

    // ---- The step is the round's, not a constant -------------------------
    //
    // The world is slowed to whatever step the rounds in the air need. For a hard-manoeuvring
    // interceptor that is a fraction of a second, which costs the player their timewarp for a
    // twenty-second engagement. For a round that coasts ballistically it can be far longer, and
    // holding the world down for a flight lasting minutes is what trips Abandon below.

    [Fact]
    public void ARoundThatToleratesALongerStepBuysBackMoreSpeed()
    {
        var tight = new WarpPolicy();
        var loose = new WarpPolicy();

        // Long enough that both have to slow. At a step the loose round tolerates it is not
        // slowed at all, which is the point of the whole change but makes the two incomparable.
        const double Step = 20.0;

        WarpDecision a = tight.Decide(Step, 100.0, roundsInFlight: true, enabled: true, Faithful);
        WarpDecision b = loose.Decide(Step, 100.0, roundsInFlight: true, enabled: true, Faithful * 20.0);

        Assert.Equal(WarpAction.Slow, a.Action);
        Assert.Equal(WarpAction.Slow, b.Action);
        Assert.True(b.Speed > a.Speed,
            $"a round tolerating twenty times the step was held to {b.Speed:F1}x against "
            + $"{a.Speed:F1}x, so the step it declares is not reaching the decision");
    }

    [Fact]
    public void AStepTheRoundToleratesIsNotReducedAtAll()
    {
        var policy = new WarpPolicy();

        // Twice the interceptor's limit, which for an interceptor would be reduced.
        WarpDecision d = policy.Decide(Faithful * 2.0, 10.0, roundsInFlight: true, enabled: true,
                                       Faithful * 4.0);

        Assert.Equal(WarpAction.None, d.Action);
    }

    /// <summary>
    /// The default is the interceptor's, so every caller that does not care is unchanged.
    /// </summary>
    [Fact]
    public void OmittingTheStepIsTheSameAsAskingForTheInterceptors()
    {
        var a = new WarpPolicy();
        var b = new WarpPolicy();

        WarpDecision withDefault = a.Decide(Faithful * 3.0, 50.0, roundsInFlight: true, enabled: true);
        WarpDecision explicitly = b.Decide(Faithful * 3.0, 50.0, roundsInFlight: true, enabled: true,
                                           Interceptor.MaxFaithfulStep);

        Assert.Equal(withDefault.Action, explicitly.Action);
        Assert.Equal(withDefault.Speed, explicitly.Speed, 9);
    }
}
