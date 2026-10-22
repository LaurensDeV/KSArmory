using Xunit;

namespace KSArmory.Tests;

public class BuiltInSwitchesTests
{
    [Fact]
    public void WhatIsWrittenReadsBack()
    {
        Assert.Equal(new HashSet<string> { "Guns", "Strategic" },
                     BuiltInSwitches.Read(BuiltInSwitches.Write(["Strategic", "Guns"])));
    }

    // An unreadable file must not switch anything off: every weapon disappearing is the worse surprise.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Disabled\": 3}")]
    public void AnythingUnreadableSwitchesNothingOff(string? json)
    {
        Assert.Empty(BuiltInSwitches.Read(json));
    }
}
