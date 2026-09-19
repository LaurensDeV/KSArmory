using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Which installed mods KSArmory reads weapons out of.
///
/// Thin, and the one case worth the file is <see cref="PackAvailability.Disabled"/>: a pack that
/// is present but switched off must be *reported* rather than skipped, because KSA writes a newly
/// discovered mod into the manifest disabled and says nothing about having done it. Silence there
/// is indistinguishable from never having installed the pack.
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
