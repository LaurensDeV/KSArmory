using Xunit;

namespace AirDefence.Tests;

/// <summary>
/// The step deduplication, which used to sit in <c>KsaWorld</c> behind KSA's <c>SimTime</c> and so
/// could not be tested — despite guarding a failure that <b>compounds</b>.
///
/// <para>The engine answers "the last step applied", not "a step since you last asked". Ask twice
/// without it stepping and it reports the same step twice; integrating it twice puts real,
/// permanent motion into a round that the world never made. Reported from play as: pause, select
/// 0.05x, pause again, repeat, and the round walks further off every cycle.</para>
///
/// <para><b>Only a test that consumes repeatedly can see this.</b> Every single-step test passes
/// against a gate that does nothing at all, which is exactly why the bug shipped.</para>
/// </summary>
public class StepGateTests
{
    /// <summary>Stands in for KSA's <c>SimTime</c>. All the gate needs from one is equality.</summary>
    private readonly record struct Tick(double Seconds);

    [Fact]
    public void TheFirstStepIsHandedOver()
    {
        var gate = new StepGate<Tick>();

        Assert.Equal(0.016, gate.Consume(new Tick(1.0), 0.016));
        Assert.True(gate.HasIntegrated);
    }

    [Fact]
    public void TheSameStepIsNeverHandedOverTwice()
    {
        var gate = new StepGate<Tick>();
        var step = new Tick(1.0);

        Assert.Equal(0.016, gate.Consume(step, 0.016));
        Assert.Equal(0.0, gate.Consume(step, 0.016));
        Assert.Equal(0.0, gate.Consume(step, 0.016));
    }

    /// <summary>
    /// <b>Regression, commit 291144a.</b> The accumulation itself, which is the shape the bug
    /// actually had. A frame that renders without the engine stepping must contribute nothing, no
    /// matter how many of them there are — otherwise every repeat adds a full step of the
    /// platform's ~29.8 km/s of ecliptic motion to an integrated position, permanently.
    /// </summary>
    [Fact]
    public void RepeatedFramesOnOneStepIntegrateItExactlyOnce()
    {
        var gate = new StepGate<Tick>();
        var step = new Tick(42.0);

        double integrated = 0.0;
        for (int frame = 0; frame < 100; frame++) integrated += gate.Consume(step, 0.016);

        Assert.Equal(0.016, integrated, 12);
    }

    [Fact]
    public void EachNewStepIsHandedOverOnceAndTheTotalIsTheRealElapsedTime()
    {
        var gate = new StepGate<Tick>();

        // Ten real steps, each rendered three times - the pattern a game that renders faster than
        // it simulates actually produces.
        double integrated = 0.0;
        double clock = 0.0;

        for (int step = 0; step < 10; step++)
        {
            clock += 0.02;
            for (int frame = 0; frame < 3; frame++) integrated += gate.Consume(new Tick(clock), 0.02);
        }

        Assert.Equal(0.2, integrated, 12);
    }

    [Fact]
    public void AChangingStepSizeIsPassedThroughUntouched()
    {
        var gate = new StepGate<Tick>();

        // Changing simulation speed swings the step by ~17 ms. The gate must not smooth, clamp or
        // otherwise reinterpret it - the whole reason it deduplicates on the timestamp rather than
        // by differencing a clock is to keep the value the engine actually applied.
        Assert.Equal(0.0166, gate.Consume(new Tick(1.0), 0.0166), 12);
        Assert.Equal(0.0001, gate.Consume(new Tick(2.0), 0.0001), 12);
        Assert.Equal(0.4000, gate.Consume(new Tick(3.0), 0.4000), 12);
    }

    /// <summary>
    /// Whether a step is one the simulation can faithfully integrate is a different question with
    /// a different answer — see <see cref="SimClock"/>. Conflating the two here would hide a bad
    /// step behind a duplicate one.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void OddDeltasAreForwardedRatherThanFilteredHere(double delta)
    {
        var gate = new StepGate<Tick>();

        double got = gate.Consume(new Tick(1.0), delta);

        Assert.Equal(double.IsNaN(delta), double.IsNaN(got));
        if (!double.IsNaN(delta)) Assert.Equal(delta, got);
    }

    [Fact]
    public void ResettingLetsTheSameTimestampThroughAgain()
    {
        var gate = new StepGate<Tick>();
        var step = new Tick(7.0);

        Assert.Equal(0.02, gate.Consume(step, 0.02));
        Assert.Equal(0.0, gate.Consume(step, 0.02));

        // A scene change can restart the clock, so the gate must not hold a stale timestamp
        // against a genuinely new world.
        gate.Reset();
        Assert.False(gate.HasIntegrated);
        Assert.Equal(0.02, gate.Consume(step, 0.02));
    }

    [Fact]
    public void AClockThatRunsBackwardsStillCountsAsANewStep()
    {
        // Loading a save moves simulated time to wherever the save was written, which can be
        // earlier. That is a new step to integrate, not a duplicate to swallow.
        var gate = new StepGate<Tick>();

        Assert.Equal(0.02, gate.Consume(new Tick(500.0), 0.02));
        Assert.Equal(0.02, gate.Consume(new Tick(10.0), 0.02));
    }

    [Fact]
    public void AFreshGateHasIntegratedNothing()
    {
        Assert.False(new StepGate<Tick>().HasIntegrated);
    }
}
