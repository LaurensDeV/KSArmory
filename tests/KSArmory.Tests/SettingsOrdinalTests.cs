using Xunit;

namespace KSArmory.Tests;

public class SettingsOrdinalTests
{
    private static readonly string[] Craft = ["Hull", "Ciws", "Tank", "Rack"];

    // The CIWS's family switched off leaves the rack the first launcher left; it must keep the settings
    // it had as the second, not take the CIWS's.
    [Fact]
    public void ALauncherAfterASwitchedOffOneKeepsItsOwnSettings()
    {
        Assert.Equal(1, SettingsOrdinal.Of(0, Craft, 3, id => id == "Ciws"));
    }

    [Fact]
    public void WithNothingSwitchedOffTheOrdinalIsUnchanged()
    {
        Assert.Equal(1, SettingsOrdinal.Of(1, Craft, 3, _ => false));
    }
}
