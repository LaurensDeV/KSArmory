using System.Text.RegularExpressions;

namespace KSArmory;

/// <summary>
/// A save realigned with a part that has lost subparts since it was written — <b>text in, text
/// out</b>, so every case is testable with no game.
///
/// <para>KSA pairs a saved part's subparts with its definition <em>by position</em>, bounded by the
/// save's count and indexing the definition (<c>KSA.PartTree.Deserialize</c>), so a part with fewer
/// subparts than a save lists throws <c>IndexOutOfRangeException</c> inside the load and closes the
/// game. Dropping the saved entries that no longer line up is the whole repair. Fewer saved than
/// declared loads as it is: the extra subparts start unconfigured.</para>
///
/// <para>The same rules as <c>tools/repair-saves.py</c>, which this is a port of: entries are aligned
/// by name with the decoration of past Id renames taken off, and a subpart carrying save data of its
/// own is never dropped, because that discards state.</para>
/// </summary>
public static class SaveRepair
{
    /// <summary>What repairing one file came to.</summary>
    /// <param name="Text">The repaired file, or null when nothing needed changing.</param>
    /// <param name="Report">One line per part that did not match its definition.</param>
    public sealed record Result(string? Text, int PartsRepaired, int EntriesDropped, int PartsRefused,
                                IReadOnlyList<string> Report);

    // Both spellings: a craft built around the part writes RootPartRef, one carrying it bolted to
    // something else writes PartRef.
    private static readonly Regex PartOpen = new(@"<(Root)?PartRef\s+InstanceOf=""([^""]*)""", RegexOptions.Compiled);
    private static readonly Regex SubPart = new(@"^(\s*)<SubPartRef\s+InstanceOf=""([^""]*)""([^>]*?)(/?)>\s*$",
                                                RegexOptions.Compiled);

    // Every way one assembly has been spelled across the Id renames.
    private static readonly Regex Noise = new(@"(?i)(^KSArmory_|_?Sub_?[Pp]art_?|^Prefab_|^Launcher_|\d+$|_$)",
                                              RegexOptions.Compiled);

    /// <summary>A subpart name with the era-specific decoration taken off.</summary>
    public static string Canonical(string name)
    {
        string previous;
        do
        {
            previous = name;
            name = Noise.Replace(name, "");
        } while (name != previous);

        return name.ToLowerInvariant();
    }

    /// <summary>
    /// Repairs every part in <paramref name="save"/> that <paramref name="declaredFor"/> answers for:
    /// the part's current subparts, in declaration order, as the template Ids they instance — or null
    /// for a part this repair should leave alone.
    /// </summary>
    public static Result Repair(string save, Func<string, IReadOnlyList<string>?> declaredFor)
    {
        List<string> lines = SplitKeepingEndings(save);
        HashSet<int> dropped = [];
        List<string> report = [];
        int repaired = 0, refused = 0;

        int index = 0;
        while (index < lines.Count)
        {
            Match opened = PartOpen.Match(lines[index]);
            if (!opened.Success)
            {
                index++;
                continue;
            }

            string partId = opened.Groups[2].Value;
            int indent = lines[index].Length - lines[index].TrimStart().Length;

            // The part's own subparts are the SubPartRef lines one level inside it; a nested part's
            // are further in, and the block ends at the next part or the closing tag.
            List<(int Line, string Name)> saved = [];
            int stateful = 0;
            int cursor = index + 1;
            for (; cursor < lines.Count; cursor++)
            {
                string stripped = lines[cursor].Trim();
                if (EndsBlock(stripped)) break;

                Match sub = SubPart.Match(lines[cursor].TrimEnd('\r', '\n'));
                if (!sub.Success || sub.Groups[1].Value.Length != indent + 2) continue;

                // An open tag: the subpart carries save data of its own, which is counted but never dropped.
                if (sub.Groups[4].Value.Length == 0) stateful++;
                else saved.Add((cursor, Canonical(sub.Groups[2].Value)));
            }

            // Only a part that does not fit is a problem; one whose subparts carry state and still fit
            // loads as it is.
            IReadOnlyList<string>? declared = declaredFor(partId);
            if (declared is not null && saved.Count + stateful > declared.Count && stateful > 0)
            {
                refused++;
                report.Add($"{partId}: {saved.Count + stateful} saved against {declared.Count} declared, "
                           + "and a subpart carries save data of its own; left alone");
            }
            else if (declared is not null && saved.Count > declared.Count)
            {
                List<int> surplus = Surplus(saved, declared.Select(Canonical).ToList());
                repaired++;
                dropped.UnionWith(surplus);
                report.Add($"{partId}: {saved.Count} saved against {declared.Count} declared, dropped {surplus.Count}");
            }

            index = cursor;
        }

        string? text = dropped.Count == 0
                           ? null
                           : string.Concat(lines.Where((_, n) => !dropped.Contains(n)));
        return new Result(text, repaired, dropped.Count, refused, report);
    }

    // Which saved entries have no counterpart, walking the two lists together. Greedy rather than a
    // full alignment: they differ by whole assemblies added or dropped, never by a reordering.
    private static List<int> Surplus(List<(int Line, string Name)> saved, List<string> declared)
    {
        List<int> surplus = [];
        int want = 0;

        foreach ((int line, string name) in saved)
        {
            if (want < declared.Count && name == declared[want])
            {
                want++;
                continue;
            }

            surplus.Add(line);
        }

        return surplus;
    }

    private static bool EndsBlock(string stripped)
        => stripped.StartsWith("</PartRef", StringComparison.Ordinal)
           || stripped.StartsWith("</RootPartRef", StringComparison.Ordinal)
           || stripped.StartsWith("<PartRef", StringComparison.Ordinal)
           || stripped.StartsWith("<RootPartRef", StringComparison.Ordinal);

    // Line endings kept, so a file this changes by one entry is otherwise byte for byte what it was.
    private static List<string> SplitKeepingEndings(string text)
    {
        List<string> lines = [];
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            lines.Add(text[start..(i + 1)]);
            start = i + 1;
        }

        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }
}
