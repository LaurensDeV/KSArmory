using System.Globalization;

namespace KSArmory.Tests;

/// <summary>A table written by <c>Reference/make_reference.py</c>, read from the source tree rather than copied.</summary>
internal static class ReferenceTable
{
    public static IReadOnlyList<(string Label, double[] Values)> Read(string fileName)
    {
        DirectoryInfo? at = new(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "KSArmory.sln"))) at = at.Parent;
        if (at is null) throw new InvalidOperationException("cannot find the repository root");

        string path = Path.Combine(at.FullName, "tests", "KSArmory.Tests", "Reference", fileName);
        List<(string, double[])> rows = [];
        bool header = true;
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (header) { header = false; continue; }

            string[] cells = line.Split(',');
            rows.Add((cells[0], cells.Skip(1).Select(c => double.Parse(c, CultureInfo.InvariantCulture)).ToArray()));
        }
        return rows;
    }
}
