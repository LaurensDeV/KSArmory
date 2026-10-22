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
                    Log.Warn($"pack '{id}' carries definitions but the mod is disabled in manifest.toml, "
                             + "so its parts are not loaded and nothing in it is registered");
                    continue;
            }

            bool core = SameFolder(FolderOf(entry), ModFolder);
            List<(string, string)> definitions = [];
            foreach (string file in files.Order(StringComparer.Ordinal))
            {
                if (Read(id, file) is { } text) definitions.Add((Path.GetFileName(file), text));
            }

            // Already registered by RegisterCore, before any pack could need them.
            if (core) continue;

            // A pack that registered from code under its own Id has said what it is; read again here,
            // every definition would be refused as already claimed.
            if (Catalogue.Registrations.Any(r => r.Source == id))
            {
                if (definitions.Count > 0)
                {
                    Log.Info($"pack '{id}' registered from code, so its {PackScan.FolderName}/ folder is not read as well");
                }
                continue;
            }

            PackFiles.Register(definitions, id);
        }
    }

    /// <summary>
    /// This mod's own definitions, registered as one unit. Called from KSArmory's own
    /// <c>[StarMapBeforeMain]</c>, ahead of every pack that ships code — see
    /// <see cref="CoreDefinitions"/>.
    /// </summary>
    public static void RegisterCore()
    {
        // For testing a pack against an empty catalogue: nothing of KSArmory's, not even its rounds.
        if (Environment.GetEnvironmentVariable("KSARMORY_NO_BUILTINS") == "1")
        {
            Log.Info("built-in definitions: none registered (KSARMORY_NO_BUILTINS)");
            return;
        }

        string folder = Path.Combine(ModFolder, PackScan.FolderName);

        List<(string, string)> files = [];
        try
        {
            foreach (string file in Directory.GetFiles(folder, PackScan.FilePattern))
            {
                files.Add((Path.GetFileName(file), File.ReadAllText(file)));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error($"built-in definitions cannot be read from {folder} - {e.Message}");
            return;
        }

        foreach (CoreFileResult file in CoreDefinitions.Register(files, BuiltInFamilies.ReadSwitches()))
        {
            BuiltInFamilies.Remember(file.Family);
            Log.Info($"built-in definitions '{file.FileName}': {file.Result.Registered} registered"
                     + (file.Disabled ? $", family {file.Family} switched off: its parts are structure" : "")
                     + (file.Result.Complete ? "" : $", {file.Result.Faults.Count} refused"));

            foreach (PackFault fault in file.Result.Faults) Log.Warn($"{file.FileName}: {fault}");
        }
    }

    private static string ModFolder => Path.GetDirectoryName(typeof(InstalledPacks).Assembly.Location) ?? "";

    private static bool SameFolder(string a, string b)
    {
        try
        {
            return a.Length > 0 && b.Length > 0
                   && string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                                    Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // A weapon file's text, or null for one that could not be read or that describes bodies instead.
    private static string? Read(string id, string file)
    {
        string definitions;
        try
        {
            definitions = File.ReadAllText(file);
        }
        catch (IOException e)
        {
            Log.Warn($"pack '{id}': {Path.GetFileName(file)} cannot be read - {e.Message}");
            return null;
        }

        // A bodies file sits beside the weapon packs and is told from one by its root: handed to the pack
        // reader it would be refused as a pack on every load.
        if (BodyReader.IsBodies(definitions))
        {
            foreach (PackFault fault in BodyCatalogue.Register(definitions, id)) Log.Warn(fault.ToString());
            Log.Info($"pack '{id}': {Path.GetFileName(file)} describes bodies");
            return null;
        }

        return definitions;
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
