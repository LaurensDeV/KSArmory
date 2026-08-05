using System.Text.Json;

namespace KSArmory;

/// <summary>
/// Remembers each system's settings between sessions, in a JSON file beside the log.
///
/// <para>KSA's own save format is not reachable from a mod — a save is written by the engine from
/// its own state and there is no hook to add to it — so this is a file of the mod's own, keyed by
/// the craft's Id. That is what makes it survive a save being loaded: the Id is the same string
/// the vessel had when the settings were chosen.</para>
///
/// <para>The consequence, and it is worth knowing: the file is <b>per installation, not per
/// save</b>. Two saves with a craft of the same name share its settings, and a craft renamed in
/// game arrives with defaults. KSA's own launch-time uniquing means names rarely collide within a
/// world, and nothing here is precious enough to be worth a migration when it does.</para>
///
/// <para>Every failure is swallowed and logged. A settings file is a convenience: a mod that
/// refuses to run because it could not read one is worse than one that starts with defaults.</para>
/// </summary>
internal static class SettingsStore
{
    private const string FileName = "KSArmory-batteries.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static Dictionary<string, BatterySettings> _stored = [];
    private static bool _loaded;

    /// <summary>Reads the file, once. Missing or unreadable is an empty store, not an error.</summary>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            string path = Path();
            if (!File.Exists(path)) return;

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return;

            _stored = JsonSerializer.Deserialize<Dictionary<string, BatterySettings>>(json, Options)
                      ?? [];
            Log.Info($"settings: loaded {_stored.Count} system(s) from {path}");
        }
        catch (Exception e)
        {
            _stored = [];
            Log.Warn($"settings: could not read {FileName} ({e.Message}); starting from defaults");
        }
    }

    /// <summary>What was last written down for a craft, or null.</summary>
    public static BatterySettings? For(string craftId)
    {
        Load();
        return _stored.TryGetValue(craftId, out BatterySettings? s) ? s : null;
    }

    /// <summary>
    /// Records a craft's settings. Writes nothing if they have not changed, so this can be called
    /// every frame without touching the disk.
    /// </summary>
    /// <returns>True if the store changed and needs saving.</returns>
    public static bool Remember(string craftId, BatteryConfig config)
    {
        if (string.IsNullOrWhiteSpace(craftId)) return false;

        Load();
        BatterySettings now = BatterySettings.From(config);

        if (_stored.TryGetValue(craftId, out BatterySettings? was) && !now.Differs(was)) return false;

        _stored[craftId] = now;
        return true;
    }

    public static void Save()
    {
        try
        {
            string path = Path();
            string? folder = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            File.WriteAllText(path, JsonSerializer.Serialize(_stored, Options));
        }
        catch (Exception e)
        {
            Log.Warn($"settings: could not write {FileName} ({e.Message})");
        }
    }

    // Beside the log, which is the one directory this mod already knows how to find on every
    // platform. See Log.Folder for the search it does.
    private static string Path() => System.IO.Path.Combine(Log.Folder, FileName);
}
