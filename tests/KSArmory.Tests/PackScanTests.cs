using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A <see cref="PackAvailability.Disabled"/> pack is reported, not skipped: KSA writes a newly
/// discovered mod into the manifest disabled and says nothing, so silence reads as never installed.
/// </summary>

public class PackScanTests
{
    [Fact]
    public void AModWithNoDefinitionsIsNotAPackAndIsNotWorthMentioning()
    {
        Assert.Equal(PackAvailability.NothingToRead, PackScan.Of(enabled: true, definitionFiles: 0));
        Assert.Equal(PackAvailability.NothingToRead, PackScan.Of(enabled: false, definitionFiles: 0));
    }

    [Fact]
    public void AnEnabledModCarryingDefinitionsIsRead()
    {
        Assert.Equal(PackAvailability.Ready, PackScan.Of(enabled: true, definitionFiles: 1));
        Assert.Equal(PackAvailability.Ready, PackScan.Of(enabled: true, definitionFiles: 5));
    }

    /// <summary>
    /// Not registered — KSA has not loaded its parts, so every launcher in it would name a part
    /// nothing declares. Reported all the same, because that is the failure with no other symptom.
    /// </summary>
    [Fact]
    public void APackWhoseModIsSwitchedOffIsReportedRatherThanSilentlySkipped()
    {
        Assert.Equal(PackAvailability.Disabled, PackScan.Of(enabled: false, definitionFiles: 1));
    }
}
