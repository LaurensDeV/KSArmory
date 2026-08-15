using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The lock cue's arithmetic. Cheap, and worth pinning because the whole claim of the cue is that
/// the animation reports real dwell — a bracket that closes on a timer of its own would look
/// identical and mean nothing.
/// </summary>
public class LockCueTests
{
    [Theory]
    [InlineData(false, false, false, false, LockPhase.None)]
    [InlineData(true, false, false, false, LockPhase.Acquiring)]
    [InlineData(true, true, false, false, LockPhase.Locked)]
    [InlineData(true, true, true, false, LockPhase.ClearToFire)]
    [InlineData(true, true, false, true, LockPhase.Held)]
    public void PhaseReadsTheChainInOrder(bool track, bool locked, bool clear, bool held,
                                          LockPhase expected)
    {
        Assert.Equal(expected, LockCue.Phase(track, locked, clear, held));
    }

    /// <summary>
    /// A gate refusing outranks a firing solution. Fire control can report both at once, and
    /// drawing "ready" over a system that will not shoot is the exact confusion the header strip
    /// exists to prevent.
    /// </summary>
    [Fact]
    public void BeingRefusedBeatsBeingClear()
    {
        Assert.Equal(LockPhase.Held,
                     LockCue.Phase(hasTrack: true, locked: true, clearToFire: true, held: true));
    }

    [Fact]
    public void AcquisitionIsTheDwellFraction()
    {
        Assert.Equal(0.00f, LockCue.Acquisition(0.0, 2.0), 4);
        Assert.Equal(0.25f, LockCue.Acquisition(0.5, 2.0), 4);
        Assert.Equal(1.00f, LockCue.Acquisition(2.0, 2.0), 4);
    }

    /// <summary>Dwell past what is needed is still a lock, not more than one.</summary>
    [Fact]
    public void AcquisitionSaturates()
    {
        Assert.Equal(1f, LockCue.Acquisition(90.0, 2.0), 4);
    }

    /// <summary>
    /// A sensor that needs no dwell locks on sight. Dividing by its zero would leave the brackets
    /// open forever on the one sensor that is always ready.
    /// </summary>
    [Fact]
    public void NoDwellRequiredIsAlreadyAcquired()
    {
        Assert.Equal(1f, LockCue.Acquisition(0.0, 0.0), 4);
    }

    [Fact]
    public void StandoffClosesFromOpenToTightAcrossTheDwell()
    {
        Assert.Equal(LockCue.OpenStandoff, LockCue.Standoff(0f), 4);
        Assert.Equal(1f, LockCue.Standoff(1f), 4);

        // Linear, so half closed means half way there. The cue's honesty rests on this.
        float mid = 0.5f * (LockCue.OpenStandoff + 1f);
        Assert.Equal(mid, LockCue.Standoff(0.5f), 4);
    }

    /// <summary>
    /// The bracket has to actually shrink as dwell builds. Asserting the ends alone passes against
    /// a constant, which is the shape of the bug this cue would have.
    /// </summary>
    [Fact]
    public void StandoffIsStrictlyDecreasing()
    {
        float previous = LockCue.Standoff(0f);

        for (int step = 1; step <= 10; step++)
        {
            float now = LockCue.Standoff(step / 10f);
            Assert.True(now < previous, $"standoff did not close at {step / 10f:F1}");
            previous = now;
        }
    }

    [Fact]
    public void TheCaretPointsAwayFromTheMiddleOfTheView()
    {
        Assert.True(LockCue.TryCaretDirection(new float2(1920f, 540f), new float2(960f, 540f),
                                              out float2 right));
        Assert.Equal(1f, right.X, 4);
        Assert.Equal(0f, right.Y, 4);

        Assert.True(LockCue.TryCaretDirection(new float2(960f, 0f), new float2(960f, 540f),
                                              out float2 up));
        Assert.Equal(0f, up.X, 4);
        Assert.Equal(-1f, up.Y, 4);
    }

    /// <summary>A contact clamped onto the centre has no direction, and must not produce a NaN one.</summary>
    [Fact]
    public void ACaretOnTheCentreHasNoDirection()
    {
        Assert.False(LockCue.TryCaretDirection(new float2(960f, 540f), new float2(960f, 540f), out _));
    }

    /// <summary>
    /// The two <see cref="Reticle.Build"/> forms are the same sight, so the bool one has to be
    /// exactly the ends of the continuous one. Otherwise the HUD and the gunner's sight drift.
    /// </summary>
    [Theory]
    [InlineData(true, 1f)]
    [InlineData(false, LockCue.OpenStandoff)]
    public void TheContinuousBuildReproducesTheBoolOne(bool settled, float standoff)
    {
        Span<ReticleStroke> a = stackalloc ReticleStroke[Reticle.MaxStrokes];
        Span<ReticleStroke> b = stackalloc ReticleStroke[Reticle.MaxStrokes];

        int na = Reticle.Build(new float2(400f, 300f), 40f, settled, a);
        int nb = Reticle.Build(new float2(400f, 300f), 40f, standoff, settled, b);

        Assert.Equal(na, nb);
        for (int i = 0; i < na; i++) Assert.Equal(a[i], b[i]);
    }
}
