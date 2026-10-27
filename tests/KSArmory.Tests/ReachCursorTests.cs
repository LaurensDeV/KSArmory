using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Over a placed set a click adds only while the reach is known, and the reach comes and goes, so
/// the same gesture turns destructive with nothing on screen changing unless the cursor says so.
/// </summary>

public class ReachCursorTests
{
    [Fact]
    public void ADesignationOverNothingNeedsNoWarning()
    {
        Assert.Equal("", ReachDisplay.CursorSays(ReachVerdict.Designates, 0));
    }

    [Theory]
    [InlineData(1, "replaces 1 target")]
    [InlineData(3, "replaces 3 targets")]
    public void ADesignationOverAPlacedSetSaysWhatItCosts(int targets, string expected)
    {
        Assert.Equal(expected, ReachDisplay.CursorSays(ReachVerdict.Designates, targets));
    }

    [Fact]
    public void AnAddSaysNothingBecauseItTakesTheClick()
    {
        Assert.Equal("", ReachDisplay.CursorSays(ReachVerdict.Adds, 3));
    }

    [Fact]
    public void ARefusalStillSaysWhy()
    {
        Assert.Equal("outside reach", ReachDisplay.CursorSays(ReachVerdict.OutsideReach, 3));
        Assert.Equal("another world", ReachDisplay.CursorSays(ReachVerdict.OffBody, 3));
        Assert.Equal("reach not known", ReachDisplay.CursorSays(ReachVerdict.Unknown, 3));
    }
}
