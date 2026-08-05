using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The line between a setting that belongs to one battery and one that belongs to the session.
///
/// <para>These read as trivial, and they are — but the split is the whole reason the type exists,
/// and the failure mode of getting it wrong is silent. A field that drifts back onto
/// <see cref="Config"/> arms every site on the map at once, or lets one installation's team list
/// disagree with another's, and neither shows up as an error.</para>
/// </summary>
public class BatteryConfigTests
{
    [Fact]
    public void TwoBatteriesArmIndependently()
    {
        var north = new BatteryConfig();
        var south = new BatteryConfig();

        north.Armed = true;

        Assert.True(north.Armed);
        Assert.False(south.Armed);
    }

    [Fact]
    public void AndEngageIndependently()
    {
        var north = new BatteryConfig { AutoEngage = true, MissilesEnabled = false };
        var south = new BatteryConfig();

        Assert.True(north.AutoEngage);
        Assert.False(south.AutoEngage);
        Assert.False(north.MissilesEnabled);
        Assert.True(south.MissilesEnabled);
    }

    [Fact]
    public void AndAimIndependently()
    {
        var north = new BatteryConfig();
        var south = new BatteryConfig();

        north.TurretManual = true;
        north.TurretManualBearingDeg = 90f;
        south.MouseAim = true;

        Assert.True(north.TurretManual);
        Assert.False(south.TurretManual);
        Assert.Equal(0f, south.TurretManualBearingDeg);
        Assert.True(south.MouseAim);
        Assert.False(north.MouseAim);
    }

    /// <summary>
    /// A fresh battery is safe, tracking, and carrying both weapons. Anything else would mean a
    /// site that starts shooting the moment it is discovered.
    /// </summary>
    [Fact]
    public void AFreshBatteryIsSafe()
    {
        var battery = new BatteryConfig();

        Assert.False(battery.Armed);
        Assert.False(battery.AutoEngage);
        Assert.True(battery.MissilesEnabled);
        Assert.True(battery.GunsEnabled);
        Assert.True(battery.TurretTracking);
        Assert.Equal(-1, battery.OpticViewport);
    }

    /// <summary>
    /// The other half of the split: what the session decides stays on Config, so two batteries
    /// cannot disagree about who is hostile.
    /// </summary>
    [Fact]
    public void TheSessionKeepsWhoIsHostile()
    {
        var world = new Config();
        world.TeamNames.Add("Red");
        world.Iff.OwnTeam = "Blue";

        // Nothing on a battery can contradict it: the policy object has no such field, which is
        // the point. If one appears here, two sites can fight over who is an enemy.
        Assert.Contains("Red", world.TeamNames);
        Assert.Equal("Blue", world.Iff.OwnTeam);
        Assert.Null(typeof(BatteryConfig).GetField("TeamNames"));
        Assert.Null(typeof(BatteryConfig).GetProperty("Iff"));
    }
}
