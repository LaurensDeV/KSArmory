namespace KSArmory;

/// <summary>
/// One mod's definition files read as one unit: every name each file declares is known to the others,
/// so a launcher may name a round or a sensor in another file and the file order decides nothing.
/// KSArmory's own files and every pack's go through it, so a pack split the way the built-ins are
/// resolves the way they do.
/// </summary>
public static class PackFiles
{
    /// <summary>
    /// Each file read, in ordinal order of its name: the filesystem gives no order, and the catalogue is
    /// first-wins. A bodies file is left out, for the body catalogue.
    /// </summary>
    public static IReadOnlyList<(string FileName, PackContents Contents)> Read(
        IEnumerable<(string FileName, string Text)> files, string source)
    {
        List<(string FileName, string Text)> ordered =
            [.. files.Where(f => !BodyReader.IsBodies(f.Text))
                     .OrderBy(f => f.FileName, StringComparer.Ordinal)];

        // Read for their names alone; a definition naming another file's round faults here, harmlessly.
        List<PackContents> declared = [.. ordered.Select(f => PackReader.Read(f.Text, source, [], []))];

        List<(string, PackContents)> read = [];
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

            read.Add((ordered[i].FileName, PackReader.Read(ordered[i].Text, source, munitions, sensors, decoys)));
        }

        return read;
    }

    /// <summary>A pack's files read as one unit and registered, one result per file.</summary>
    public static IReadOnlyList<(string FileName, PackResult Result)> Register(
        IEnumerable<(string FileName, string Text)> files, string source)
        => [.. Read(files, source).Select(f => (f.FileName, Catalogue.Register(f.Contents)))];
}
