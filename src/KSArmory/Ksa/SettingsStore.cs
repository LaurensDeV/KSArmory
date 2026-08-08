using System.Text.Json;

namespace KSArmory;

/// <summary>
/// Remembers each system's settings, in a folder of the mod's own <b>inside the save</b>:
/// <c>saves/&lt;save&gt;/KSArmory/systems.json</c>.
///
/// <para><b>Keyed on the craft's display name, which is not unique.</b> A squadron built from one
/// blueprint shares an Id, so those craft share one entry: they all restore the same settings and
/// the last one saved overwrites the rest. There is no fix here to make, because a
/// <c>Vehicle</c> reference does not survive a save and the Id is the only thing that does. The
/// collision is reported when the roster crews the second craft, which is where it is visible.
/// </para>
///
/// <para>KSA's save format cannot be extended — <c>UniverseData</c> is a fixed XML-mapped class —
/// and StarMap has no save or load hook. But a save is a <em>directory</em>, so the next best
/// thing to being in the save is being in it, beside the <c>universe.xml</c> it belongs to. The
/// mod-named subfolder is so several mods can do this without agreeing on filenames.</para>
///
/// <para>That placement makes the awkward cases disappear rather than need handling. Deleting a
/// save deletes these settings with it, so a new save under the same name cannot inherit a
/// stranger's armed batteries. Copying a save copies them; renaming takes them along. None of that
/// needs code, which is the whole reason for choosing it over one file keyed by save name.</para>
///
/// <para>A session with no save open — a fresh sandbox — has nowhere to put them and falls back to
/// a folder in the KSA user directory. Those settings are adopted by the first save that opens,
/// which is what someone who set a battery up and then saved would expect.</para>
///
/// <para>Every failure is swallowed and logged. A settings file is a convenience: a mod that
/// refuses to run because it could not read one is worse than one that starts from defaults.</para>
/// </summary>
internal static class SettingsStore
{
    private const string ModFolderName = "KSArmory";
    // Named for what the panel calls them, not for the class that happens to run one today.
    // A fixed emplacement or a sensor mast is a weapons system and is not a battery.
    private const string FileName = "systems.json";

    /// <summary>Reported as the scope when no save is open.</summary>
    public const string NoSave = "(no save)";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static Dictionary<string, SystemSettings> _stored = [];

    // Which file _stored came from. Re-read when it changes: that is a different save's settings,
    // and holding the previous one's would write them into it.
    private static string _loadedFrom = string.Empty;

    /// <summary>The save whose settings are in use, or <see cref="NoSave"/>.</summary>
    public static string CurrentScope
    {
        get
        {
            string id = KsaWorld.CurrentSaveId();
            return string.IsNullOrWhiteSpace(id) ? NoSave : id;
        }
    }

    /// <summary>What was last written down for a craft in the open save, or null.</summary>
    public static SystemSettings? For(string craftId)
    {
        Load();
        return _stored.TryGetValue(craftId, out SystemSettings? s) ? s : null;
    }

    /// <summary>
    /// Records a craft's settings. Reports nothing when they have not changed, so this can be
    /// called as often as the caller likes.
    /// </summary>
    /// <returns>True if the store changed and needs saving.</returns>
    public static bool Remember(string craftId, SystemConfig config)
    {
        if (string.IsNullOrWhiteSpace(craftId)) return false;

        Load();
        SystemSettings now = SystemSettings.From(config);

        if (_stored.TryGetValue(craftId, out SystemSettings? was) && !now.Differs(was)) return false;

        _stored[craftId] = now;
        return true;
    }

    /// <summary>Forgets a craft, so it starts from defaults again.</summary>
    public static void Forget(string craftId)
    {
        Load();
        if (_stored.Remove(craftId)) Save();
    }

    public static void Save()
    {
        string path = Path();
        try
        {
            if (System.IO.Path.GetDirectoryName(path) is { Length: > 0 } folder)
            {
                // A save's own folder must already exist. Creating it would resurrect a save the
                // player deleted, as a directory containing nothing but our settings.
                if (InSave() && !Directory.Exists(SaveRoot())) return;

                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(_stored, Options));
            _loadedFrom = path;
        }
        catch (Exception e)
        {
            Log.Warn($"settings: could not write {path} ({e.Message})");
        }
    }

    // Reads the file for whichever save is open, unless that is the one already in memory.
    private static void Load()
    {
        string path = Path();
        if (path == _loadedFrom) return;

        _loadedFrom = path;
        _stored = [];

        try
        {
            if (File.Exists(path))
            {
                _stored = ReadOrEmpty(path);
                Log.Info($"settings: loaded {_stored.Count} system(s) from {path}");
                return;
            }

            // A save that has never had settings written adopts whatever the session had set
            // before it was opened.
            if (InSave())
            {
                _stored = ReadOrEmpty(LoosePath());
                Log.Info($"settings: {path} is new; carrying {_stored.Count} system(s) in");
            }
        }
        catch (Exception e)
        {
            _stored = [];
            Log.Warn($"settings: could not read {path} ({e.Message}); starting from defaults");
        }
    }

    private static Dictionary<string, SystemSettings> ReadOrEmpty(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];

            string json = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(json)
                       ? []
                       : JsonSerializer.Deserialize<Dictionary<string, SystemSettings>>(json, Options)
                         ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static bool InSave() => CurrentScope != NoSave;

    private static string SaveRoot()
        => KsaWorld.TrySaveFolder(KsaWorld.CurrentSaveId(), out string folder) ? folder : string.Empty;

    private static string Path()
    {
        if (InSave() && SaveRoot() is { Length: > 0 } save)
        {
            return System.IO.Path.Combine(save, ModFolderName, FileName);
        }

        return LoosePath();
    }

    // Where settings go with no save to put them in: the KSA user directory, beside saves/ and
    // vehicles/. Not Logs/, which is where this started and is a folder people clear out.
    private static string LoosePath()
    {
        string logs = Log.Folder;
        string root = Directory.GetParent(logs)?.FullName ?? logs;
        return System.IO.Path.Combine(root, ModFolderName, FileName);
    }
}
