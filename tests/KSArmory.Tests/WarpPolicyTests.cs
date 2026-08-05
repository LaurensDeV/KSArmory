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
    /// If the world will not go slower, there is nothing honest left to do. A lost salvo the
    /// player is told about beats the 124 km miss that clamping produced in flight.
    /// </summary>
    [Fact]
    public void AWorldThatWillNotSlowDownEndsInAbandon()
    {
        var policy = new WarpPolicy();

        // The engine ignores every request: the speed and the step never change.
        WarpDecision last = WarpDecision.Nothing;
        for (int i = 0; i <= WarpPolicy.AttemptsBeforeAbandon; i++)
        {
            last = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
        }

        Assert.Equal(WarpAction.Abandon, last.Action);
        Assert.False(policy.Holding);
    }

    [Fact]
    public void ItDoesNotAbandonWhileTheSlowdownIsStillTakingEffect()
    {
        var policy = new WarpPolicy();

        // The speed write lands a frame later than the read, so the first repeats must not kill
        // the salvo. Exactly AttemptsBeforeAbandon overrunning frames are survivable.
        for (int i = 0; i < WarpPolicy.AttemptsBeforeAbandon; i++)
        {
            WarpDecision d = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
            Assert.Equal(WarpAction.Slow, d.Action);
        }
    }

    [Fact]
    public void RecoveringInsideTheLimitForgetsTheFailedAttempts()
    {
        var policy = new WarpPolicy();

        for (int i = 0; i < WarpPolicy.AttemptsBeforeAbandon; i++)
        {
            policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);
        }

        // The slowdown takes effect, then the player warps up again later in the same flight.
        policy.Decide(StepAt(1.0), 1.0, roundsInFlight: true, enabled: true);
        WarpDecision again = policy.Decide(StepAt(600.0), 600.0, roundsInFlight: true, enabled: true);

        Assert.Equal(WarpAction.Slow, again.Action);
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
}
