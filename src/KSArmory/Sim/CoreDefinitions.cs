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
/// in another: <see cref="PackFiles"/>, which every pack's files go through too.</para>
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
        List<CoreFileResult> results = [];
        foreach ((string fileName, PackContents read) in PackFiles.Read(files, PackReader.BuiltInSource))
        {
            PackContents contents = read;
            bool off = contents.Family is { } family && disabled?.Contains(family) == true;
            if (off) contents = LibraryOnly(contents);
            else if (contents.Family is { } owner) Catalogue.Owns(owner, PartsOf(contents));

            results.Add(new CoreFileResult(fileName, contents.Family, off, Catalogue.Register(contents)));
        }

        return results;
    }

    private static IEnumerable<string> PartsOf(PackContents read)
        => read.Launchers.Select(l => l.PartId)
               .Concat(read.Optics.Select(o => o.PartId))
               .Concat(read.SearchSets.Select(s => s.PartId))
               .Concat(read.Dispensers.Select(d => d.PartId));

    private static PackContents LibraryOnly(PackContents read)
    {
        Catalogue.Disable(read.Launchers.Select(l => l.PartId), PartsOf(read));

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
