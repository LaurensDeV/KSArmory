using KSA;

namespace KSArmory;

/// <summary>
/// One <see cref="WeaponSystem"/> per weapons system in the world, each with its own
/// <see cref="SystemConfig"/>.
///
/// <para>Every craft carrying a recognised part is crewed, permanently and independently: a
/// battery is pinned to the craft it was created for and never moves, so arming one site, sending
/// it a target or putting it on a team says nothing about any other.</para>
///
/// <para>Keyed by <see cref="Vehicle"/> reference, which is what a craft compares by. Entries
/// appear when a system is surveyed and are dropped when the craft dies — a battery outliving its
/// platform would keep a destroyed vehicle alive in this dictionary for the session.</para>
/// </summary>
internal sealed class WeaponSystems(Config config)
{
    internal sealed record Entry(WeaponSystem Battery, SystemConfig Policy);

    private readonly Config _config = config;
    private readonly Dictionary<Vehicle, Entry> _entries = [];
    private readonly List<Vehicle> _scratch = [];

    // Which save's settings the live batteries are holding. Loading a save switches the bucket
    // before the craft are rebuilt, so without this the next periodic write stamps the outgoing
    // session's settings onto the save just opened -- which reads as a save that will not keep
    // what it was given.
    private string _scope = string.Empty;

    // When the open save was last written. Settings are written to disk on the frame this
    // advances -- i.e. when the player saves -- and at no other time. Writing continuously stops
    // a reload restoring anything: the file is then always already up to date with the session,
    // so there is nothing older to come back to.
    private long _savedAt;


    public int Count => _entries.Count;

    /// <summary>Every crewed system, in no particular order.</summary>
    public IEnumerable<Entry> All => _entries.Values;

    // Names already reported as shared, so it is said once each rather than every survey.
    private readonly HashSet<string> _reportedShared = [];

    private void WarnIfNameIsTaken(Vehicle craft)
    {
        string name = KsaWorld.DisplayName(craft);

        foreach (Vehicle other in _entries.Keys)
        {
            if (!string.Equals(KsaWorld.DisplayName(other), name, StringComparison.Ordinal)) continue;
            if (!_reportedShared.Add(name)) return;

            Log.Warn($"two crewed craft are both called '{name}'; they share one settings entry, "
                     + "so each will restore the other's and the last saved wins. Rename one.");
            return;
        }
    }

    /// <summary>The battery running on a craft, or null if it carries no weapons system.</summary>
    public Entry? For(Vehicle? craft)
        => craft is not null && _entries.TryGetValue(craft, out Entry? e) ? e : null;

    /// <summary>
    /// Brings the roster in line with what has been surveyed: crew anything new, forget anything
    /// destroyed.
    /// </summary>
    public void Sync(IReadOnlyList<(Vehicle Craft, WeaponInventory Inventory)> systems)
    {
        for (int i = 0; i < systems.Count; i++)
        {
            Vehicle craft = systems[i].Craft;
            if (!KsaWorld.IsAlive(craft) || _entries.ContainsKey(craft)) continue;

            // Settings are keyed on the display name, which craft from one blueprint share. They
            // will restore each other's and overwrite each other on save, and nothing else would
            // ever say so.
            WarnIfNameIsTaken(craft);

            SystemConfig policy = new();

            // Whatever this craft was last set to. Applied before the battery exists so its first
            // frame runs on the restored settings rather than on defaults it then overwrites.
            if (SettingsStore.For(KsaWorld.DisplayName(craft)) is { } stored)
            {
                stored.ApplyTo(policy);

                // And put its teams back on the session's roster. Memberships are stored per
                // system and the names are session-wide, so restoring only the first half leaves
                // every system sure of its allegiance in a world that has forgotten the teams
                // exist. Every contact is then Unknown, which is engageable by default.
                stored.DeclareTeams(_config.TeamNames);
            }

            WeaponSystem battery = new(_config, policy);

            // Pinned on creation, so ResolvePlatform leaves it alone. Without this every battery
            // would independently elect the craft being flown and they would all pile onto it.
            battery.PinPlatform(craft);

            _entries[craft] = new Entry(battery, policy);
            Log.Info($"crewed {KsaWorld.DisplayName(craft)}");
        }

        _scratch.Clear();
        foreach (KeyValuePair<Vehicle, Entry> kv in _entries)
        {
            if (!KsaWorld.IsAlive(kv.Key)) _scratch.Add(kv.Key);
        }

        foreach (Vehicle craft in _scratch)
        {
            _entries[craft].Battery.Reset();

            // Anything keyed on the system rather than on the craft has to be told, or its entry
            // outlives the craft and keeps a destroyed vehicle reachable for the session.
            Diagnostics.Forget(_entries[craft].Battery);

            _entries.Remove(craft);
            Log.Info("a crewed system was destroyed");
        }
    }

    /// <summary>
    /// The system to show when nothing has been chosen — the one being flown if it is armed,
    /// otherwise any of them. Never null while the roster is not empty.
    /// </summary>
    public Vehicle? Default()
    {
        if (For(KsaWorld.ControlledVehicle) is not null) return KsaWorld.ControlledVehicle;

        foreach (KeyValuePair<Vehicle, Entry> kv in _entries) return kv.Key;
        return null;
    }

    /// <summary>
    /// Writes down anything that has changed. Cheap to call often: the store compares against what
    /// it already holds and only reports a change when there is one.
    /// </summary>
    public void Remember()
    {
        string scope = SettingsStore.CurrentScope;
        if (_scope.Length == 0)
        {
            // First pass: Sync has already applied whatever this bucket holds.
            _scope = scope;
        }
        else if (scope != _scope)
        {
            // A different save is open. Adopt what it says instead of writing over it.
            _scope = scope;
            _savedAt = KsaWorld.CurrentSaveStamp();
            Adopt();
            return;
        }

        // Only when the game saves. A load then finds the settings as they were at that moment,
        // because nothing has been written over them since.
        long stamp = KsaWorld.CurrentSaveStamp();
        if (stamp == 0 || stamp == _savedAt) return;

        bool firstSight = _savedAt == 0;
        _savedAt = stamp;

        // The first stamp seen is the save as it already was, not the player saving.
        if (firstSight) return;

        Log.Info("settings: the game saved, writing the systems' settings with it");

        bool changed = false;
        foreach (KeyValuePair<Vehicle, Entry> kv in _entries)
        {
            if (!KsaWorld.IsAlive(kv.Key)) continue;
            changed |= SettingsStore.Remember(KsaWorld.DisplayName(kv.Key), kv.Value.Policy);
        }

        if (changed) SettingsStore.Save();
    }

    // Re-reads every live battery's settings from the store. Used when the save changes under a
    // roster that is still holding the previous one's.
    private void Adopt()
    {
        int applied = 0;
        foreach (KeyValuePair<Vehicle, Entry> kv in _entries)
        {
            if (!KsaWorld.IsAlive(kv.Key)) continue;
            if (SettingsStore.For(KsaWorld.DisplayName(kv.Key)) is not { } stored) continue;

            stored.ApplyTo(kv.Value.Policy);
            applied++;
        }

        if (applied > 0) Log.Info($"settings: re-read {applied} system(s) for the open save");
    }

    /// <summary>
    /// Writes now, whatever the save has done. For unload, and for anything that has to take
    /// effect immediately rather than at the next save.
    /// </summary>
    public void WriteNow()
    {
        bool changed = false;
        foreach (KeyValuePair<Vehicle, Entry> kv in _entries)
        {
            if (!KsaWorld.IsAlive(kv.Key)) continue;
            changed |= SettingsStore.Remember(KsaWorld.DisplayName(kv.Key), kv.Value.Policy);
        }

        if (changed) SettingsStore.Save();
    }

    public void Clear()
    {
        // Last chance: a battery about to be forgotten still holds settings someone chose.
        WriteNow();

        foreach (Entry e in _entries.Values) e.Battery.Reset();
        _entries.Clear();
    }
}
