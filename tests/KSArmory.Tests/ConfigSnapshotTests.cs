using Xunit;

namespace KSArmory.Tests;

public class ConfigSnapshotTests
{
    [Fact]
    public void RestorePutsBackWhatAScenarioOverrides()
    {
        var config = new Config { ShaderPass = 1f, NuclearClouds = true, DrawOverlays = false, DiagnosticIntervalSeconds = 5f };
        Config before = config.Snapshot();

        config.ShaderPass = 0f;
        config.NuclearClouds = false;
        config.DrawOverlays = true;
        config.DiagnosticIntervalSeconds = 0.5f;

        config.Restore(before);

        Assert.Equal(1f, config.ShaderPass);
        Assert.True(config.NuclearClouds);
        Assert.False(config.DrawOverlays);
        Assert.Equal(5f, config.DiagnosticIntervalSeconds);
    }

    [Fact]
    public void TheSnapshotIsNotMovedByLaterChanges()
    {
        var config = new Config { ShaderPass = 1f };
        Config before = config.Snapshot();

        config.ShaderPass = 0f;

        Assert.Equal(1f, before.ShaderPass);
    }

    [Fact]
    public void TheTeamRosterIsTheSameListAfterARestore()
    {
        var config = new Config();
        var roster = config.TeamNames;
        Config before = config.Snapshot();

        config.Restore(before);

        Assert.Same(roster, config.TeamNames);
    }
}
