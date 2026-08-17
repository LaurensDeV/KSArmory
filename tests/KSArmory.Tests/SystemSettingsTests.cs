using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Settings surviving a session. The round trip is the contract: what was chosen is what comes
/// back, and anything a newer version added arrives at its default rather than at zero.
/// </summary>
public class SystemSettingsTests
{
    private static SystemConfig Configured()
    {
        SystemConfig c = new()
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
        SystemConfig saved = Configured();
        SystemConfig loaded = new();

        SystemSettings.From(saved).ApplyTo(loaded);

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
        SystemSettings older = new();
        SystemConfig loaded = new() { MissilesEnabled = false, TurretTracking = false };

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
        SystemSettings settings = SystemSettings.From(Configured());
        SystemConfig loaded = new();

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
        SystemConfig config = Configured();

        Assert.False(SystemSettings.From(config).Differs(SystemSettings.From(config)));

        config.RoundsPerTarget += 1;
        Assert.True(SystemSettings.From(config).Differs(SystemSettings.From(Configured())));
    }

    [Fact]
    public void AChangedTeamListCounts()
    {
        SystemConfig config = Configured();
        SystemSettings before = SystemSettings.From(config);

        config.Iff.AlliedTeams.Add("Amber");

        Assert.True(SystemSettings.From(config).Differs(before));
    }

    /// <summary>
    /// Settings a system carries deliberately, and why each is not persisted. Everything else on
    /// <see cref="SystemConfig"/> has to survive a save.
    /// </summary>
    private static readonly Dictionary<string, string> NotPersisted = new()
    {
        ["MouseFire"] = "restoring a tool that fires means the first click after a load launches",
    };

    // Every public instance field and property a config declares, by name.
    private static HashSet<string> Members(Type config)
        => config
           .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
           .Select(f => f.Name)
           .Concat(config.GetProperties().Select(p => p.Name))
           .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<System.Reflection.FieldInfo> Persistable()
        => typeof(SystemConfig)
           .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
           .Where(f => !NotPersisted.ContainsKey(f.Name));

    // Any value that is not the default, so a field left out of the round trip comes back
    // visibly different rather than accidentally right.
    private static void Poke(System.Reflection.FieldInfo field, SystemConfig config)
    {
        object current = field.GetValue(config)!;

        field.SetValue(config, current switch
        {
            bool b => !b,
            int i => i + 7,
            float f => f + 13.5f,
            _ => throw new InvalidOperationException(
                     $"SystemConfig.{field.Name} is a {field.FieldType.Name}, which this test does "
                     + "not know how to vary — teach it, do not exempt the field"),
        });
    }

    /// <summary>
    /// Every setting a system carries has to survive a save, and the three lists that make that
    /// happen — <c>From</c>, <c>ApplyTo</c> and <c>Differs</c> — are written out by hand.
    ///
    /// <para>A field added to <see cref="SystemConfig"/> and forgotten in any of them is silently
    /// not persisted: nothing throws, nothing is logged, and the setting is simply back at its
    /// default on the next load. Four shipped that way — the scope's range and pop-out, the bomb
    /// sight, and whether the set was told to stop transmitting.</para>
    ///
    /// <para>Exempting a field is deliberate and has to name its reason, which is what stops this
    /// being satisfied by adding names to a list.</para>
    /// </summary>
    [Fact]
    public void EverySystemConfigFieldSurvivesASave()
    {
        SystemConfig written = new();
        foreach (System.Reflection.FieldInfo f in Persistable()) Poke(f, written);

        SystemConfig read = new();
        SystemSettings.From(written).ApplyTo(read);

        foreach (System.Reflection.FieldInfo f in Persistable())
        {
            Assert.True(Equals(f.GetValue(written), f.GetValue(read)),
                        $"SystemConfig.{f.Name} does not survive a save — it is missing from "
                        + "SystemSettings.From or ApplyTo, and comes back at its default");
        }
    }

    /// <summary>
    /// And <c>Differs</c> has to notice each of them change, or the store decides nothing has
    /// happened and never writes the file — which loses the setting just as completely as leaving
    /// it out of <c>From</c>, and looks like the save working.
    /// </summary>
    [Fact]
    public void DiffersNoticesEverySettingThatIsPersisted()
    {
        foreach (System.Reflection.FieldInfo f in Persistable())
        {
            SystemConfig before = new();
            SystemConfig after = new();
            Poke(f, after);

            Assert.True(SystemSettings.From(after).Differs(SystemSettings.From(before)),
                        $"SystemConfig.{f.Name} can change without Differs noticing, so the save "
                        + "is never written");
        }
    }

    /// <summary>
    /// A battery carries nothing about an optical head. A director is a part in its own right
    /// with its own OpticConfig, crewed per part rather than per weapons system, so a battery's
    /// settings have no business naming one — and a craft can carry a director and no weapon.
    /// </summary>
    [Fact]
    public void ABatterySettingCarriesNothingAboutAnOpticalHead()
    {
        // Settings only a director has, which is not the same as every setting a director has:
        // both configs implement ISensorPolicy, so ProtectControlledVehicle and MouseAim are on
        // each of them and belong to each of them. Matching the words "Optic" and "Sight" instead
        // calls DrawBombSight a director's business, and it is not — a bomb sight is the release
        // pipper for a rack, owned by the system that drops the store.
        HashSet<string> headOnly = Members(typeof(OpticConfig));
        headOnly.ExceptWith(Members(typeof(SystemConfig)));

        Assert.NotEmpty(headOnly);

        foreach (System.Reflection.PropertyInfo p in typeof(SystemSettings).GetProperties())
        {
            Assert.DoesNotContain(p.Name, headOnly);
        }
    }

    /// <summary>
    /// The shape the store writes: save, then craft. Two saves with a craft of the same name are
    /// exactly what the scoping is for, and JSON round-tripping the nested form is the part that
    /// would silently lose settings if it broke.
    /// </summary>
    [Fact]
    public void SettingsRoundTripNestedByCraftWithinSave()
    {
        Dictionary<string, Dictionary<string, SystemSettings>> stored = new()
        {
            ["Campaign"] = new() { ["AA Defence Site"] = SystemSettings.From(Configured()) },
            ["Sandbox"] = new() { ["AA Defence Site"] = new SystemSettings { Armed = false } },
        };

        string json = System.Text.Json.JsonSerializer.Serialize(stored);
        var back = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, Dictionary<string, SystemSettings>>>(json)!;

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
            new Dictionary<string, SystemSettings> { ["Old Site"] = SystemSettings.From(Configured()) });

        var flat = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, SystemSettings>>(legacy)!;

        Assert.True(flat["Old Site"].Armed);
        Assert.Equal(4, flat["Old Site"].RoundsPerTarget);

        // And it must NOT read as the nested shape, which is what makes the fallback necessary
        // rather than merely tidy.
        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer
                .Deserialize<Dictionary<string, Dictionary<string, SystemSettings>>>(legacy));
    }

    // ---- Teams survive a reload only if both halves come back ------------
    //
    // The memberships are stored per system and the names are session-wide. Restoring one without
    // the other leaves every system certain of its allegiance in a world that has forgotten the
    // teams exist, which classifies every contact Unknown, which is engageable by default: a
    // two-sided world silently becomes a free-for-all with the panel still showing the old sides.

    [Fact]
    public void RestoringASystemPutsItsTeamsBackOnTheSessionRoster()
    {
        var config = new SystemConfig();
        config.Iff.OwnTeam = "Red";
        config.Iff.AlliedTeams.Add("Crimson");
        config.Iff.NeutralTeams.Add("Trader");

        SystemSettings saved = SystemSettings.From(config);

        List<string> names = [];
        saved.DeclareTeams(names);

        Assert.Contains("Red", names);
        Assert.Contains("Crimson", names);
        Assert.Contains("Trader", names);
    }

    [Fact]
    public void DeclaringTeamsTwiceDoesNotDuplicateThem()
    {
        var config = new SystemConfig();
        config.Iff.OwnTeam = "Red";

        SystemSettings saved = SystemSettings.From(config);

        List<string> names = [];
        saved.DeclareTeams(names);
        saved.DeclareTeams(names);

        // Two systems on the same side is the normal case, and each declares on restore.
        Assert.Single(names);

        // And the match is how the radar matches names, which ignores case.
        var other = new SystemConfig();
        other.Iff.OwnTeam = "red";
        SystemSettings.From(other).DeclareTeams(names);
        Assert.Single(names);
    }

    [Fact]
    public void ASystemWithNoTeamDeclaresNothing()
    {
        List<string> names = [];
        SystemSettings.From(new SystemConfig()).DeclareTeams(names);

        Assert.Empty(names);
    }
}
