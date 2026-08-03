namespace AirDefence;

/// <summary>
/// Writes to stdout and to a file beside KSA's own logs.
///
/// The file matters: StarMap mods only reach stdout, which lands in whatever terminal launched
/// the game and is awkward to read (and impossible to read after the fact). KSA's own
/// <c>KittenSpaceAgency.log</c> is written by its internal logger, which mods cannot reach.
/// So we keep our own, in the same folder, where it can be tailed from outside the game.
/// </summary>
internal static class Log
{
    private const string Prefix = "[AirDefence]";

    /// <summary>Guards the writer; frame and GUI hooks can both log.</summary>
    private static readonly object Gate = new();

    private static string? _path;
    private static bool _resolved;
    private static bool _fileBroken;

    /// <summary>Full path of the log file, or null if no writable location was found.</summary>
    public static string? FilePath
    {
        get
        {
            EnsureResolved();
            return _path;
        }
    }

    public static void Info(string message) => Write("INFO ", message);

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? e = null) =>
        Write("ERROR", e is null ? message : $"{message}: {e}");

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {level} {message}";

        Console.WriteLine($"{Prefix} {line}");

        lock (Gate)
        {
            EnsureResolved();
            if (_path is null || _fileBroken) return;

            try
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch
            {
                // Disk full, file locked, permissions. Stop trying rather than throwing every
                // frame - stdout still works.
                _fileBroken = true;
            }
        }
    }

    /// <summary>
    /// Picks a log location once: KSA's own Logs folder if we can find it, otherwise the
    /// user's temp directory.
    /// </summary>
    private static void EnsureResolved()
    {
        if (_resolved) return;
        _resolved = true;

        foreach (string candidate in CandidateDirectories())
        {
            try
            {
                if (!Directory.Exists(candidate)) continue;

                string path = Path.Combine(candidate, "AirDefence.log");
                // Truncate per session so the file reflects this run, not every run ever.
                File.WriteAllText(path, $"=== AirDefence session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
                _path = path;
                Console.WriteLine($"{Prefix} logging to {path}");
                return;
            }
            catch
            {
                // Try the next candidate.
            }
        }
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        // Alongside KittenSpaceAgency.log, which is where anyone would look first.
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(documents))
        {
            yield return Path.Combine(documents, "My Games", "Kitten Space Agency", "Logs");
        }

        yield return Path.GetTempPath();
    }
}
