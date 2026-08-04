using Xunit;

namespace AirDefence.Tests;

/// <summary>
/// The step deduplication, which guards a failure that <b>compounds</b>: the engine reports the
/// last step applied, not one since you last asked, so integrating a repeat adds motion the world
/// never made and it lands in an integrated position.
///
/// <para>Only a test that consumes repeatedly can see this — every single-step test passes against
/// a gate that does nothing at all.</para>
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
    /// A frame that renders without the engine stepping contributes nothing, however many of them
    /// there are. Otherwise each repeat permanently adds a full step of ~29.8 km/s of ecliptic
    /// motion to an integrated position.
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
        // reinterpret it: deduplicating on the timestamp is what keeps the applied value intact.
        Assert.Equal(0.0166, gate.Consume(new Tick(1.0), 0.0166), 12);
        Assert.Equal(0.0001, gate.Consume(new Tick(2.0), 0.0001), 12);
        Assert.Equal(0.4000, gate.Consume(new Tick(3.0), 0.4000), 12);
    }

    /// <summary>
    /// Whether a step can be faithfully integrated is a separate question — see
    /// <see cref="SimClock"/>. Merging the two would hide a bad step behind a duplicate one.
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
        // Loading a save can move simulated time backwards. That is a new step, not a duplicate.
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
