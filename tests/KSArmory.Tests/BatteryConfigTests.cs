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
public class SystemConfigTests
{
    [Fact]
    public void TwoBatteriesArmIndependently()
    {
        var north = new SystemConfig();
        var south = new SystemConfig();

        north.Armed = true;

        Assert.True(north.Armed);
        Assert.False(south.Armed);
    }

    [Fact]
    public void AndEngageIndependently()
    {
        var north = new SystemConfig { AutoEngage = true, MissilesEnabled = false };
        var south = new SystemConfig();

        Assert.True(north.AutoEngage);
        Assert.False(south.AutoEngage);
        Assert.False(north.MissilesEnabled);
        Assert.True(south.MissilesEnabled);
    }

    [Fact]
    public void AndAimIndependently()
    {
        var north = new SystemConfig();
        var south = new SystemConfig();

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
        var battery = new SystemConfig();

        Assert.False(battery.Armed);
        Assert.False(battery.AutoEngage);
        Assert.True(battery.MissilesEnabled);
        Assert.True(battery.GunsEnabled);
        Assert.True(battery.TurretTracking);
        Assert.Equal(-1, battery.OpticViewport);
    }

    /// <summary>
    /// The other half of the split. Which side a battery takes is its own — two sites in one
    /// world on opposite sides is the whole case — while the roster of team names belongs to the
    /// session, because a name labels a craft the same way whoever is looking at it.
    /// </summary>
    [Fact]
    public void EachBatteryPicksItsOwnSide()
    {
        var world = new Config();
        world.TeamNames.Add("Red");

        var north = new SystemConfig();
        var south = new SystemConfig();
        north.Iff.OwnTeam = "Blue";
        south.Iff.OwnTeam = "Red";

        Assert.Equal(Allegiance.Hostile, north.Iff.Classify("Red"));
        Assert.Equal(Allegiance.Friendly, south.Iff.Classify("Red"));

        // The names are not duplicated per battery, and the policy is not shared: one craft is
        // on one team however many batteries are looking at it, and each decides for itself
        // what that means.
        Assert.Contains("Red", world.TeamNames);
        Assert.Null(typeof(SystemConfig).GetField("TeamNames"));
        Assert.Null(typeof(Config).GetProperty("Iff"));
    }
}
