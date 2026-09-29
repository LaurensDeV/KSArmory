using System.Diagnostics;
using System.Text;
using KSA;

namespace KSArmory;

/// <summary>
/// Repairs every save and library craft holding a part that has lost subparts, once, when the game
/// starts — before any can be loaded, since loading one unrepaired closes the game. The rules are
/// <see cref="SaveRepair"/>'s; this finds the files and asks the loaded part templates what each part
/// declares now, so a weapon pack's parts are covered as well as this mod's.
///
/// <para><b>Temporary</b>: it ships for the release that removed the Mk 42's twenty shell subparts
/// and is removed in the one after. <c>docs/CODE-HEALTH.md</c> has the item.</para>
/// </summary>
internal static class SaveRepairs
{
    // Beside the file it was taken from, and never overwritten: KSA keeps a universe.xml.bak of its
    // own there, and the first original is the one worth keeping.
    private const string BackupSuffix = ".ksarmory-repair.bak";

    private static readonly Dictionary<string, IReadOnlyList<string>?> _declared = [];

    public static void RunAll()
    {
        try
        {
            if (Directory.GetParent(Log.Folder)?.FullName is not { } root) return;

            Stopwatch clock = Stopwatch.StartNew();
            int files = 0, repaired = 0;

            foreach (string file in Candidates(root))
            {
                files++;
                if (TryRepair(file)) repaired++;
            }

            Log.Info($"save repair: {repaired} of {files} save(s) and craft realigned with the parts as declared now, "
                     + $"{clock.Elapsed.TotalMilliseconds:F0} ms");
        }
        catch (Exception e)
        {
            Log.Warn($"save repair did not run: {e.Message}");
        }
    }

    private static IEnumerable<string> Candidates(string root)
    {
        foreach ((string folder, string name) in new[] { ("saves", "universe.xml"), ("vehicles", "vehicle.xml") })
        {
            string dir = Path.Combine(root, folder);
            if (!Directory.Exists(dir)) continue;

            foreach (string entry in Directory.EnumerateDirectories(dir))
            {
                string file = Path.Combine(entry, name);
                if (File.Exists(file)) yield return file;
            }
        }
    }

    private static bool TryRepair(string file)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(file);

            // Written back as it was read: with its byte-order mark if it had one.
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            UTF8Encoding encoding = new(bom);
            string text = encoding.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));

            // Most saves hold no part of ours, and those are not worth parsing.
            if (!text.Contains("KSArmory", StringComparison.Ordinal)) return false;

            SaveRepair.Result result = SaveRepair.Repair(text, DeclaredFor);
            string where = Path.GetFileName(Path.GetDirectoryName(file)) ?? file;
            foreach (string line in result.Report) Log.Info($"  save repair, '{where}': {line}");

            if (result.Text is null) return false;

            string backup = file + BackupSuffix;
            if (!File.Exists(backup)) File.Copy(file, backup);

            File.WriteAllText(file, result.Text, encoding);
            Log.Info($"save repair: '{where}' rewritten, {result.EntriesDropped} subpart entries dropped; "
                     + $"the original is {Path.GetFileName(backup)}");
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"save repair: {file} left as it was ({e.Message})");
            return false;
        }
    }

    // What a part declares now, as the template Ids its subparts instance -- for this mod's parts and
    // any pack's, and null for everything else, which this has no business rewriting.
    private static IReadOnlyList<string>? DeclaredFor(string partId)
    {
        if (_declared.TryGetValue(partId, out IReadOnlyList<string>? cached)) return cached;

        IReadOnlyList<string>? declared = null;
        if (partId.StartsWith("KSArmory_", StringComparison.Ordinal) || Catalogue.LauncherForPart(partId) is not null)
        {
            try
            {
                if (ModLibrary.Get<PartTemplate>(partId) is { } template)
                {
                    declared = template.SubPartInstances
                                       .Select(s => string.IsNullOrEmpty(s.InstanceOf) ? s.Id : s.InstanceOf)
                                       .ToList();
                }
            }
            catch
            {
                // A part nothing declares any more; KSA refuses that save by another route.
            }
        }

        _declared[partId] = declared;
        return declared;
    }
}
