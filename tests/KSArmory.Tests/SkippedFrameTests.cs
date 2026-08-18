using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A frame in which the mod's hook never runs. KSA's screenshot capture sets
/// <c>Program.DrawUI = false</c>, and the engine guards the call this mod postfixes with it, so
/// the hook is not called at all — while the world keeps stepping.
/// </summary>
public class SkippedFrameTests
{
    /// <summary>Nanosecond stamps, the way UniverseTime carries them.</summary>
    private readonly record struct Stamp(long Nanos) : IEquatable<Stamp>;

    private static double Span(Stamp from, Stamp to) => (to.Nanos - from.Nanos) / 1e9;

    private static Stamp At(double seconds) => new((long)(seconds * 1e9));

    [Fact]
    public void AMissedFrameIsIntegratedOnTheNextOne()
    {
        StepGate<Stamp> gate = new();

        // Frame 1 runs: the world stepped 22.5 ms and so do we.
        Assert.Equal(0.0225, gate.Consume(At(0.0225), 0.0225, Span), 9);

        // Frame 2 is the capture -- the hook never runs, so nothing is consumed.

        // Frame 3: the engine reports only the LAST step, 26.5 ms. The world has advanced
        // 22.5 + 26.5 = 49 ms since we integrated, and the missed 22.5 ms is what the round
        // must still be given.
        double taken = gate.Consume(At(0.0715), 0.0265, Span);

        Assert.Equal(0.049, taken, 9);
    }

    [Fact]
    public void TheDeficitIsTheDistanceTheBombWasThrown()
    {
        StepGate<Stamp> gate = new();
        gate.Consume(At(0.0225), 0.0225, Span);

        double reported = 0.0265;
        double taken = gate.Consume(At(0.0715), reported, Span);

        // Everything on Earth carries the planet's ecliptic motion. A round that fails to
        // integrate an interval the platform did move across puts the whole of it into the
        // drawn offset -- 656 m was measured in flight for a single missed frame.
        const double EclipticSpeed = 29_800.0;
        double leaked = (taken - reported) * EclipticSpeed;

        Assert.InRange(leaked, 670.0, 671.0);
        Assert.True(leaked > 500.0, "a missed frame must be worth hundreds of metres, or this test proves nothing");
    }

    [Fact]
    public void AnOrdinaryFrameIsUnchanged()
    {
        StepGate<Stamp> gate = new();
        gate.Consume(At(0.016), 0.016, Span);

        // The span and the step agree when no frame was missed, so nothing is added.
        Assert.Equal(0.016, gate.Consume(At(0.032), 0.016, Span), 9);
    }

    [Fact]
    public void ARepeatedStepIsStillRefused()
    {
        StepGate<Stamp> gate = new();
        gate.Consume(At(0.016), 0.016, Span);

        // The gate's whole reason for existing survives the new argument.
        Assert.Equal(0.0, gate.Consume(At(0.016), 0.016, Span));
    }

    [Fact]
    public void TheFirstStepHasNoGapToClose()
    {
        StepGate<Stamp> gate = new();

        // Universe time is enormous; without a previous stamp the span is meaningless and the
        // reported step is the only honest answer.
        Assert.Equal(0.016, gate.Consume(At(86_400.0), 0.016, Span), 9);
    }

    [Fact]
    public void AClockRunningShortDoesNotShortenTheStep()
    {
        StepGate<Stamp> gate = new();
        gate.Consume(At(0.016), 0.016, Span);

        // The step is what moved the world; a span disagreeing downward does not get to undo it.
        Assert.Equal(0.016, gate.Consume(At(0.020), 0.016, Span), 9);
    }
}
