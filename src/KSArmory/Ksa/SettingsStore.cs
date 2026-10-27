using System.Text.Json;

namespace KSArmory;

/// <summary>
/// Remembers each system's settings, in a folder of the mod's own <b>inside the save</b>:
/// <c>saves/&lt;save&gt;/KSArmory/systems.json</c>, and each ballistic computer's setup beside it in
/// <c>ballistic.json</c>, keyed the same way.
///
/// <para><b>Keyed on the craft's display name, which is not unique.</b> Craft built from one
/// blueprint share an Id and so one entry; a <c>Vehicle</c> reference does not survive a save and the
/// Id is the only thing that does. The roster reports the collision when it crews the second craft.
/// </para>
///
/// <para>KSA's save format cannot be extended (<c>UniverseData</c> is a fixed XML-mapped class), but a
/// save is a directory, so deleting, copying or renaming one carries these settings with it. With no
/// save open they fall back to the KSA user directory, and the first save opened adopts them.</para>
///
/// <para>Every failure is swallowed and logged: a missing settings file starts from defaults.</para>
/// </summary>
internal static class SettingsStore
{
    private const string ModFolderName = "KSArmory";
    private const string FileName = "systems.json";
    private const string BallisticFileName = "ballistic.json";

    /// <summary>Reported as the scope when no save is open.</summary>
    public const string NoSave = "(no save)";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static Dictionary<string, SystemSettings> _stored = [];
    private static Dictionary<string, BallisticSettings> _ballistic = [];

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

    /// <summary>What was last written down for a craft's ballistic computer in the open save, or null.</summary>
    public static BallisticSettings? BallisticFor(string craftId)
    {
        Load();
        return _ballistic.TryGetValue(craftId, out BallisticSettings? s) ? s : null;
    }

    /// <returns>True if the store changed and needs saving.</returns>
    public static bool RememberBallistic(string craftId, BallisticSettings now)
    {
        if (string.IsNullOrWhiteSpace(craftId)) return false;

        Load();
        if (_ballistic.TryGetValue(craftId, out BallisticSettings? was) && !now.Differs(was)) return false;

        _ballistic[craftId] = now;
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
                // player deleted, as a directory containing nothing but these settings.
                if (InSave() && !Directory.Exists(SaveRoot())) return;

                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(_stored, Options));

            // Only once there is something to say, so a save with no computer gets no file.
            string ballistic = Sibling(path, BallisticFileName);
            if (_ballistic.Count > 0 || File.Exists(ballistic))
            {
                File.WriteAllText(ballistic, BallisticSettings.Write(_ballistic));
            }

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
        _ballistic = [];

        try
        {
            if (File.Exists(path))
            {
                _stored = ReadOrEmpty(path);
                _ballistic = ReadBallistic(Sibling(path, BallisticFileName));
                Log.Info($"settings: loaded {_stored.Count} system(s) and {_ballistic.Count} "
                         + $"ballistic computer(s) from {path}");
                return;
            }

            // A save that has never had settings written adopts whatever the session had set
            // before it was opened.
            if (InSave())
            {
                _stored = ReadOrEmpty(LoosePath());
                _ballistic = ReadBallistic(Sibling(LoosePath(), BallisticFileName));
                Log.Info($"settings: {path} is new; carrying {_stored.Count} system(s) and "
                         + $"{_ballistic.Count} ballistic computer(s) in");
            }
        }
        catch (Exception e)
        {
            _stored = [];
            _ballistic = [];
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

    private static Dictionary<string, BallisticSettings> ReadBallistic(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];

            List<string> faults = [];
            if (!BallisticSettings.TryRead(File.ReadAllText(path), out Dictionary<string, BallisticSettings> read,
                                           out string why, faults))
            {
                Log.Warn($"settings: {path} ignored, {why}");
                return [];
            }

            foreach (string fault in faults) Log.Warn($"settings: {path}: {fault}");
            return read;
        }
        catch (Exception e)
        {
            Log.Warn($"settings: could not read {path} ({e.Message})");
            return [];
        }
    }

    private static string Sibling(string path, string fileName)
        => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? "", fileName);

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
    // vehicles/. Not Logs/, which is a folder people clear out.
    private static string LoosePath()
    {
        string logs = Log.Folder;
        string root = Directory.GetParent(logs)?.FullName ?? logs;
        return System.IO.Path.Combine(root, ModFolderName, FileName);
    }
}
