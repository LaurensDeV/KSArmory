namespace KSArmory;

/// <summary>One of KSArmory's own definition files, and what registering it produced.</summary>
public readonly record struct CoreFileResult(string FileName, PackResult Result);

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
    public static IReadOnlyList<CoreFileResult> Register(IEnumerable<(string FileName, string Text)> files)
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
            results.Add(new CoreFileResult(ordered[i].FileName, Catalogue.Register(contents)));
        }

        return results;
    }
}
