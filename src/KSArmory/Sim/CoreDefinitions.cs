namespace KSArmory;

/// <summary>One of KSArmory's own definition files, its family, and what registering it produced.</summary>
public readonly record struct CoreFileResult(string FileName, string? Family, bool Disabled, PackResult Result);

/// <summary>
/// KSArmory's own definition files, registered as one unit and before anyone else's.
///
/// <para><b>Before</b>, because a pack that ships code registers from its own
/// <c>[StarMapBeforeMain]</c> and resolves <c>KSArmory:X</c> against the catalogue as it stands
/// then. StarMap holds such a pack back until this mod has initialised, so registering from this
/// mod's own <c>BeforeMain</c> is early enough; the convention scan in
/// <c>[StarMapAllModsLoaded]</c> is not.</para>
///
/// <para><b>As one unit</b>, because a launcher in one file may name a round or a sensor declared
/// in another. Every file's names are collected first and each file is read against all of them,
/// so neither the file order nor the family split decides whether a reference resolves.</para>
///
/// <para>Text in, like <see cref="PackReader"/>: the game and the tests hand it the same files, so
/// the suite cannot register the built-ins differently from the game.</para>
/// </summary>
public static class CoreDefinitions
{
    /// <param name="disabled">
    /// Families the player switched off. Their rounds, sensors and decoys still register, so a pack using
    /// one keeps loading; their launchers, optics, search sets and dispensers do not, so the parts load as
    /// structure and nothing else.
    /// </param>
    public static IReadOnlyList<CoreFileResult> Register(IEnumerable<(string FileName, string Text)> files,
                                                         IReadOnlySet<string>? disabled = null)
    {
        // Ordinal, because the filesystem gives no order and the catalogue is first-wins.
        List<(string FileName, string Text)> ordered =
            [.. files.Where(f => !BodyReader.IsBodies(f.Text))
                     .OrderBy(f => f.FileName, StringComparer.Ordinal)];

        // Read for their names alone; a launcher or dispenser naming another file's round faults here, harmlessly.
        List<PackContents> declared =
            [.. ordered.Select(f => PackReader.Read(f.Text, PackReader.BuiltInSource, [], []))];

        List<CoreFileResult> results = [];
        for (int i = 0; i < ordered.Count; i++)
        {
            List<MunitionProfile> munitions = [.. Catalogue.Munitions];
            List<SensorProfile> sensors = [.. Catalogue.Sensors];
            List<DecoyProfile> decoys = [.. Catalogue.Decoys];
            for (int j = 0; j < declared.Count; j++)
            {
                if (j == i) continue;
                munitions.AddRange(declared[j].Munitions);
                sensors.AddRange(declared[j].Sensors);
                decoys.AddRange(declared[j].Decoys);
            }

            PackContents contents = PackReader.Read(ordered[i].Text, PackReader.BuiltInSource, munitions, sensors, decoys);
            bool off = contents.Family is { } family && disabled?.Contains(family) == true;
            if (off) contents = LibraryOnly(contents);

            results.Add(new CoreFileResult(ordered[i].FileName, contents.Family, off, Catalogue.Register(contents)));
        }

        return results;
    }

    private static PackContents LibraryOnly(PackContents read)
    {
        Catalogue.Disable(read.Launchers.Select(l => l.PartId),
                          read.Launchers.Select(l => l.PartId)
                              .Concat(read.Optics.Select(o => o.PartId))
                              .Concat(read.SearchSets.Select(s => s.PartId))
                              .Concat(read.Dispensers.Select(d => d.PartId)));

        return new PackContents
        {
            Source = read.Source,
            Family = read.Family,
            Munitions = read.Munitions,
            Sensors = read.Sensors,
            Decoys = read.Decoys,
            Faults = read.Faults,
        };
    }
}
