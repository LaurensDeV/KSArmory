using Xunit;

namespace KSArmory.Tests;

public class StoreRetargetTests
{
    [Theory]
    [InlineData("None", false, false, true)]       // dropped with nothing marked
    [InlineData("Ground", false, false, false)]    // already sent somewhere: stays
    [InlineData("Vehicle", false, false, false)]
    [InlineData("Ground", true, false, true)]      // the last one released
    [InlineData("Ground", false, true, true)]      // the one being watched
    public void OnlyAnUncommittedTheLatestOrTheChasedStoreTakesANewMark(
        string current, bool releasedLast, bool beingChased, bool expected)
        => Assert.Equal(expected, StoreRetarget.Takes(Enum.Parse<AimpointKind>(current), releasedLast, beingChased));
}
