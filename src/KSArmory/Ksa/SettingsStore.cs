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
/// <para>Keyed by save first, then by craft. KSA's save format cannot be extended —
/// <c>UniverseData</c> is a fixed XML-mapped class — and StarMap has no save or load hook, so the
/// save's Id is used to scope this file instead. Two saves with a craft of the same name therefore
/// keep their own settings, which they did not when this was one flat map.</para>
///
/// <para>A craft renamed in game still arrives with defaults, and a session never loaded from a
/// save lands in a shared bucket. Both are honest limits of keying on names rather than on
/// something the engine would keep for us.</para>
///
/// <para>Every failure is swallowed and logged. A settings file is a convenience: a mod that
/// refuses to run because it could not read one is worse than one that starts with defaults.</para>
/// </summary>
internal static class SettingsStore
{
    private const string FileName = "KSArmory-batteries.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    // save id -> craft id -> settings.
    private static Dictionary<string, Dictionary<string, BatterySettings>> _stored = [];
    private static bool _loaded;

    // Where settings go when no save has been selected — a fresh sandbox, or a session started
    // without opening the save browser. Named rather than dropped: losing what someone set
    // because the game had not been saved yet would be worse than sharing one bucket.
    private const string NoSave = "(no save)";

    private static string _lastScope = string.Empty;

    private static string Scope()
    {
        string id = KsaWorld.CurrentSaveId();
        string scope = string.IsNullOrWhiteSpace(id) ? NoSave : id;

        // Worth a line: which save the settings are being read from and written to is otherwise
        // invisible, and it is the thing that decides whether they appear to have been lost.
        if (scope != _lastScope)
        {
            _lastScope = scope;
            Log.Info($"settings: using the '{scope}' bucket");
        }

        return scope;
    }

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

            _stored = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, BatterySettings>>>(
                          json, Options) ?? [];

            int systems = 0;
            foreach (Dictionary<string, BatterySettings> bucket in _stored.Values) systems += bucket.Count;
            Log.Info($"settings: loaded {systems} system(s) across {_stored.Count} save(s)");
        }
        catch (JsonException)
        {
            // The first version of this file was one flat craft->settings map. Read it into the
            // shared bucket rather than discarding settings someone chose.
            if (TryReadFlat()) return;

            _stored = [];
            Log.Warn($"settings: {FileName} is not readable; starting from defaults");
        }
        catch (Exception e)
        {
            _stored = [];
            Log.Warn($"settings: could not read {FileName} ({e.Message}); starting from defaults");
        }
    }

    private static bool TryReadFlat()
    {
        try
        {
            var flat = JsonSerializer.Deserialize<Dictionary<string, BatterySettings>>(
                           File.ReadAllText(Path()), Options);
            if (flat is null || flat.Count == 0) return false;

            _stored = new Dictionary<string, Dictionary<string, BatterySettings>> { [NoSave] = flat };
            Log.Info($"settings: carried {flat.Count} system(s) over from the un-scoped file");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Which bucket is in use — the save's Id, or the shared one. Changes when a save is selected,
    /// which is the moment anything holding settings has to re-read rather than write.
    /// </summary>
    public static string CurrentScope => Scope();

    /// <summary>What was last written down for a craft, or null.</summary>
    public static BatterySettings? For(string craftId)
    {
        Load();

        // This save first, then the shared bucket: settings chosen before a save existed should
        // still apply once it does, rather than silently reverting to defaults on first save.
        if (_stored.TryGetValue(Scope(), out Dictionary<string, BatterySettings>? bucket)
            && bucket.TryGetValue(craftId, out BatterySettings? found))
        {
            return found;
        }

        return _stored.TryGetValue(NoSave, out Dictionary<string, BatterySettings>? shared)
               && shared.TryGetValue(craftId, out BatterySettings? fallback)
                   ? fallback
                   : null;
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
        string scope = Scope();

        if (!_stored.TryGetValue(scope, out Dictionary<string, BatterySettings>? bucket))
        {
            bucket = [];
            _stored[scope] = bucket;
        }

        if (bucket.TryGetValue(craftId, out BatterySettings? was) && !now.Differs(was)) return false;

        bucket[craftId] = now;
        return true;
    }

    /// <summary>
    /// Forgets a craft's settings, in every bucket, so it starts from defaults again.
    ///
    /// <para>Needed because persistence is silent: a switch flicked once now survives every
    /// restart, and without this the only way back is to find and edit the file.</para>
    /// </summary>
    public static void Forget(string craftId)
    {
        Load();

        bool changed = false;
        foreach (Dictionary<string, BatterySettings> bucket in _stored.Values)
        {
            changed |= bucket.Remove(craftId);
        }

        if (changed) Save();
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
