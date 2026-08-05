using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Settings surviving a session. The round trip is the contract: what was chosen is what comes
/// back, and anything a newer version added arrives at its default rather than at zero.
/// </summary>
public class BatterySettingsTests
{
    private static BatteryConfig Configured()
    {
        BatteryConfig c = new()
        {
            Armed = true,
            AutoEngage = true,
            ProtectControlledVehicle = false,
            MissilesEnabled = false,
            GunsEnabled = true,
            RoundsPerTarget = 4,
            MouseAim = true,
            TurretTracking = false,
            TurretManual = true,
            TurretManualBearingDeg = 137.5f,
            TurretManualElevationDeg = 12.25f,
            TurretSpin = true,
            SearchRadarStopped = true,
        };

        c.Iff.OwnTeam = "Blue";
        c.Iff.EngageUnknown = false;
        c.Iff.EngageNeutral = true;
        c.Iff.ProtectFriendly = false;
        c.Iff.AlliedTeams.Add("Green");
        c.Iff.NeutralTeams.Add("Grey");
        return c;
    }

    [Fact]
    public void EverythingChosenComesBack()
    {
        BatteryConfig saved = Configured();
        BatteryConfig loaded = new();

        BatterySettings.From(saved).ApplyTo(loaded);

        Assert.True(loaded.Armed);
        Assert.True(loaded.AutoEngage);
        Assert.False(loaded.ProtectControlledVehicle);
        Assert.False(loaded.MissilesEnabled);
        Assert.Equal(4, loaded.RoundsPerTarget);
        Assert.True(loaded.MouseAim);
        Assert.False(loaded.TurretTracking);
        Assert.True(loaded.TurretManual);
        Assert.Equal(137.5f, loaded.TurretManualBearingDeg, 3);
        Assert.Equal(12.25f, loaded.TurretManualElevationDeg, 3);
        Assert.True(loaded.TurretSpin);
        Assert.True(loaded.SearchRadarStopped);

        Assert.Equal("Blue", loaded.Iff.OwnTeam);
        Assert.False(loaded.Iff.EngageUnknown);
        Assert.True(loaded.Iff.EngageNeutral);
        Assert.False(loaded.Iff.ProtectFriendly);
        Assert.Contains("Green", loaded.Iff.AlliedTeams);
        Assert.Contains("Grey", loaded.Iff.NeutralTeams);
    }

    /// <summary>
    /// A file written before a setting existed must load it at its default. Zero would be the
    /// obvious result of deserialising a missing field, and for MissilesEnabled or TurretTracking
    /// that silently disarms half a battery.
    /// </summary>
    [Fact]
    public void AMissingSettingArrivesAtItsDefaultNotAtZero()
    {
        BatterySettings older = new();
        BatteryConfig loaded = new() { MissilesEnabled = false, TurretTracking = false };

        older.ApplyTo(loaded);

        Assert.True(loaded.MissilesEnabled);
        Assert.True(loaded.GunsEnabled);
        Assert.True(loaded.TurretTracking);
        Assert.True(loaded.ProtectControlledVehicle);
        Assert.True(loaded.Iff.EngageUnknown);
        Assert.True(loaded.Iff.ProtectFriendly);
        Assert.Equal(2, loaded.RoundsPerTarget);
        Assert.Equal(55f, loaded.TurretManualElevationDeg, 3);
    }

    /// <summary>Applying twice must not stack the team lists.</summary>
    [Fact]
    public void ApplyingTwiceDoesNotDuplicateTeams()
    {
        BatterySettings settings = BatterySettings.From(Configured());
        BatteryConfig loaded = new();

        settings.ApplyTo(loaded);
        settings.ApplyTo(loaded);

        Assert.Single(loaded.Iff.AlliedTeams);
        Assert.Single(loaded.Iff.NeutralTeams);
    }

    /// <summary>
    /// Nothing is written when nothing changed — this is called every half second, and rewriting
    /// an unchanged file that often is how a settings file gets corrupted by a crash.
    /// </summary>
    [Fact]
    public void UnchangedSettingsCompareEqual()
    {
        BatteryConfig config = Configured();

        Assert.False(BatterySettings.From(config).Differs(BatterySettings.From(config)));

        config.RoundsPerTarget += 1;
        Assert.True(BatterySettings.From(config).Differs(BatterySettings.From(Configured())));
    }

    [Fact]
    public void AChangedTeamListCounts()
    {
        BatteryConfig config = Configured();
        BatterySettings before = BatterySettings.From(config);

        config.Iff.AlliedTeams.Add("Amber");

        Assert.True(BatterySettings.From(config).Differs(before));
    }

    /// <summary>
    /// The optical head's viewport is deliberately not carried: it names an index in the session
    /// that saved it, and a new session need not have that window at all.
    /// </summary>
    [Fact]
    public void TheOpticViewportIsNotRestored()
    {
        BatteryConfig saved = new() { OpticViewport = 3 };
        BatteryConfig loaded = new();

        BatterySettings.From(saved).ApplyTo(loaded);

        Assert.Equal(-1, loaded.OpticViewport);
    }

    /// <summary>
    /// The shape the store writes: save, then craft. Two saves with a craft of the same name are
    /// exactly what the scoping is for, and JSON round-tripping the nested form is the part that
    /// would silently lose settings if it broke.
    /// </summary>
    [Fact]
    public void SettingsRoundTripNestedByCraftWithinSave()
    {
        Dictionary<string, Dictionary<string, BatterySettings>> stored = new()
        {
            ["Campaign"] = new() { ["AA Defence Site"] = BatterySettings.From(Configured()) },
            ["Sandbox"] = new() { ["AA Defence Site"] = new BatterySettings { Armed = false } },
        };

        string json = System.Text.Json.JsonSerializer.Serialize(stored);
        var back = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, Dictionary<string, BatterySettings>>>(json)!;

        // The same craft name, two saves, two answers -- which is the whole point.
        Assert.True(back["Campaign"]["AA Defence Site"].Armed);
        Assert.False(back["Sandbox"]["AA Defence Site"].Armed);
        Assert.Equal("Blue", back["Campaign"]["AA Defence Site"].OwnTeam);
    }

    /// <summary>
    /// A file written before the scoping existed is a flat craft->settings map. It has to still
    /// read, or upgrading the mod silently discards everything anyone had set.
    /// </summary>
    [Fact]
    public void TheOlderFlatFileStillDeserialises()
    {
        string legacy = System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, BatterySettings> { ["Old Site"] = BatterySettings.From(Configured()) });

        var flat = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, BatterySettings>>(legacy)!;

        Assert.True(flat["Old Site"].Armed);
        Assert.Equal(4, flat["Old Site"].RoundsPerTarget);

        // And it must NOT read as the nested shape, which is what makes the fallback necessary
        // rather than merely tidy.
        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer
                .Deserialize<Dictionary<string, Dictionary<string, BatterySettings>>>(legacy));
    }
}
