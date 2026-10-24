using KSA;

namespace KSArmory;

/// <summary>
/// The player's switch for each of KSArmory's own weapon families, kept in <c>KSArmory/builtins.json</c>
/// in the KSA user directory beside the loose settings. Read once, before any save is open, because the
/// catalogue is built then and frozen; a change takes effect at the next start.
/// </summary>
public static class BuiltInFamilies
{
    private const string FileName = "builtins.json";

    /// <summary>Every family the core files declared at this start, in file order.</summary>
    public static IReadOnlyList<string> Loaded => _loaded;

    public static IReadOnlySet<string> OffAtStart => _offAtStart;

    /// <summary>The families the player has now asked to be off, which may differ until a restart.</summary>
    public static IReadOnlySet<string> Wanted => _wanted;

    private static readonly List<string> _loaded = [];
    private static HashSet<string> _offAtStart = [];
    private static HashSet<string> _wanted = [];

    public static HashSet<string> ReadSwitches()
    {
        try
        {
            string path = FilePath();
            _offAtStart = BuiltInSwitches.Read(File.Exists(path) ? File.ReadAllText(path) : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"built-in families: {FileName} cannot be read, so every family is on - {e.Message}");
            _offAtStart = [];
        }

        _wanted = [.. _offAtStart];
        return _offAtStart;
    }

    public static void Remember(string? family)
    {
        if (family is { Length: > 0 } && !_loaded.Contains(family)) _loaded.Add(family);
    }

    /// <summary>Asks for a family on or off at the next start, and writes the file.</summary>
    public static void Want(string family, bool on)
    {
        if (on) _wanted.Remove(family);
        else _wanted.Add(family);
        Write();
    }

    public static void WantAll(bool on)
    {
        if (on) _wanted.Clear();
        else _wanted.UnionWith(_loaded);
        Write();
    }

    private static void Write()
    {
        try
        {
            string path = FilePath();
            if (Path.GetDirectoryName(path) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
            File.WriteAllText(path, BuiltInSwitches.Write(_wanted));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"built-in families: {FileName} cannot be written - {e.Message}");
        }
    }

    /// <summary>
    /// Takes a switched-off family's parts out of the editor's list. A craft already carrying one still
    /// loads, since a hidden template is still a template.
    /// </summary>
    public static void HideDisabledParts()
    {
        int hidden = 0;
        foreach (string partId in Catalogue.DisabledParts)
        {
            try
            {
                if (ModLibrary.Get<PartTemplate>(partId) is { } template && !template.IsHidden)
                {
                    template.EditorTags.Add(EditorTag.Hidden);
                    hidden++;
                }
            }
            catch
            {
                // A part the library cannot answer for is one the editor cannot list either.
            }
        }

        if (hidden > 0) Log.Info($"built-in families: {hidden} part(s) of switched-off families hidden from the editor");
    }

    private static string FilePath()
    {
        string root = Directory.GetParent(Log.Folder)?.FullName ?? Log.Folder;
        return Path.Combine(root, PackScan.FolderName, FileName);
    }
}
