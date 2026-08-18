using System.IO;

using KSA;

namespace KSArmory;

/// <summary>
/// Finds weapon definitions in other mods' folders and registers them.
///
/// <para>This is what lets a pack be **assets only** — no assembly, no entry point, nothing to
/// compile. <see cref="Armoury"/> stays for a pack that ships code and wants to build its
/// definitions at runtime; a pack that merely declares weapons needs neither.</para>
///
/// <para>KSArmory still knows nothing about any particular pack. It reads the manifest KSA already
/// keeps, looks in the same place inside every mod, and registers whatever it finds — the same
/// relationship KSA has with its own mods folder.</para>
/// </summary>
public static class InstalledPacks
{
    /// <summary>
    /// Every enabled mod's definitions, registered. Call before the catalogue is frozen.
    /// </summary>
    public static void RegisterAll()
    {
        foreach (ModEntry entry in Installed())
        {
            string id = entry.Id;
            if (string.IsNullOrEmpty(id)) continue;

            string[] files;
            try
            {
                string folder = Path.Combine(FolderOf(entry), PackScan.FolderName);
                files = Directory.Exists(folder)
                            ? Directory.GetFiles(folder, PackScan.FilePattern)
                            : [];
            }
            catch (IOException e)
            {
                Log.Warn($"pack '{id}': cannot be read - {e.Message}");
                continue;
            }

            switch (PackScan.Of(entry.Enabled, files.Length))
            {
                case PackAvailability.NothingToRead:
                    continue;

                case PackAvailability.Disabled:
                    Log.Warn($"pack '{id}' carries weapons but the mod is disabled in manifest.toml, "
                             + "so its parts are not loaded and its weapons are not registered");
                    continue;
            }

            foreach (string file in files) Read(id, file);
        }
    }

    private static void Read(string id, string file)
    {
        string definitions;
        try
        {
            definitions = File.ReadAllText(file);
        }
        catch (IOException e)
        {
            Log.Warn($"pack '{id}': {Path.GetFileName(file)} cannot be read - {e.Message}");
            return;
        }

        Armoury.Register(definitions, id);
    }

    private static IEnumerable<ModEntry> Installed()
    {
        try
        {
            return ModLibrary.Manifest?.Mods ?? [];
        }
        catch
        {
            return [];
        }
    }

    // A disabled mod is not in the library at all, and it is the one this most needs to find -- so
    // the manifest entry's own folder is the fallback rather than the answer of last resort.
    private static string FolderOf(ModEntry entry)
    {
        try
        {
            if (ModLibrary.Find(entry.Id) is { DirectoryPath: { Length: > 0 } path }) return path;

            return Path.Combine(ModLibrary.LocalModsFolderPath, entry.Id);
        }
        catch
        {
            return "";
        }
    }
}
