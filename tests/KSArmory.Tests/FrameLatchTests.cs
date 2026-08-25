using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The mod is stepped from two hooks because KSA guards one of them: <c>Program.OnFrame</c> wraps
/// the whole UI pass in <c>if (DrawUI)</c>, and the ToggleUi key — F2 — flips it. A simulation
/// living only in the GUI pass therefore stops dead while the UI is hidden, and the world does not.
/// </summary>
public class FrameLatchTests
{
    [Fact]
    public void TheFirstHookToAskOwnsTheFrameAndTheSecondDoesNot()
    {
        FrameLatch latch = new();

        Assert.True(latch.Claim());
        Assert.False(latch.Claim());
        Assert.False(latch.Claim());
    }

    [Fact]
    public void AFrameWhoseFirstHookNeverRanIsStillStepped()
    {
        FrameLatch latch = new();

        // The UI is hidden, so only the ungated hook reaches it.
        Assert.True(latch.Claim());
        latch.EndFrame();

        Assert.True(latch.Claim());
        latch.EndFrame();
    }

    /// <summary>
    /// The failure worth a test of its own. Claiming twice costs one duplicated frame; a latch that
    /// is never released stops the mod for the rest of the session — no rounds, no fire control, no
    /// guidance — and nothing about that looks like an error from outside.
    /// </summary>
    [Fact]
    public void EndingTheFrameAlwaysReleasesItHoweverManyTimesItWasClaimed()
    {
        FrameLatch latch = new();

        latch.Claim();
        latch.Claim();
        Assert.True(latch.Claimed);

        latch.EndFrame();

        Assert.False(latch.Claimed);
        Assert.True(latch.Claim());
    }

    [Fact]
    public void EndingAFrameNobodyClaimedIsNotAnError()
    {
        FrameLatch latch = new();

        latch.EndFrame();
        latch.EndFrame();

        Assert.True(latch.Claim());
    }
}
