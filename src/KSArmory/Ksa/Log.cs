namespace KSArmory;

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
    private const string Prefix = "[KSArmory]";

    // Guards the writer; frame and GUI hooks can both log.
    private static readonly object Gate = new();

    private static string? _path;
    private static bool _resolved;
    private static bool _fileBroken;

    /// <summary>How much detail reaches the log.</summary>
    public enum Level
    {
        /// <summary>Developer detail: spawn maths, per-object dumps, geometry read-backs.</summary>
        Debug,

        /// <summary>What the battery did. The default for a release build.</summary>
        Info,

        /// <summary>Only things that went wrong.</summary>
        Warn,
        Error,
        Off,
    }

    /// <summary>
    /// The threshold. Debug builds keep everything; release builds start at
    /// <see cref="Level.Info"/>, because the developer detail is measured in hundreds of lines
    /// per engagement and buries the handful that matter.
    ///
    /// Raised or lowered at runtime from the panel, so a user chasing a bug can turn the
    /// detail back on without a special build.
    /// </summary>
#if DEBUG
    public static Level Threshold { get; set; } = Level.Debug;
#else
    public static Level Threshold { get; set; } = Level.Info;
#endif

    /// <summary>True when this build was compiled with debug symbols and assertions.</summary>
    public static bool IsDebugBuild =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>Full path of the log file, or null if no writable location was found.</summary>
    public static string? FilePath
    {
        get
        {
            EnsureResolved();
            return _path;
        }
    }

    /// <summary>
    /// Developer detail. Takes a delegate rather than a string so that the interpolation cost
    /// is not paid when the line is going to be discarded — most of these are inside loops over
    /// every vehicle in the scene.
    /// </summary>
    public static void Debug(Func<string> message)
    {
        if (Threshold > Level.Debug) return;
        Write(Level.Debug, "DEBUG", message());
    }

    public static void Debug(string message) => Write(Level.Debug, "DEBUG", message);

    public static void Info(string message) => Write(Level.Info, "INFO ", message);

    public static void Warn(string message) => Write(Level.Warn, "WARN ", message);

    public static void Error(string message, Exception? e = null) =>
        Write(Level.Error, "ERROR", e is null ? message : $"{message}: {e}");

    private static void Write(Level level, string tag, string message)
    {
        if (level < Threshold) return;

        string line = $"{DateTime.Now:HH:mm:ss.fff} {tag} {message}";

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
    /// The folder the log went to, which is the one place this mod knows how to find on every
    /// platform. Anything else it writes goes beside it rather than repeating the search.
    /// </summary>
    public static string Folder
    {
        get
        {
            EnsureResolved();
            return _path is null ? Path.GetTempPath() : Path.GetDirectoryName(_path) ?? Path.GetTempPath();
        }
    }

    // Picks a log location once: KSA's own Logs folder if we can find it, otherwise the user's temp
    // directory.
    private static void EnsureResolved()
    {
        if (_resolved) return;
        _resolved = true;

        foreach (string candidate in CandidateDirectories())
        {
            try
            {
                if (!Directory.Exists(candidate)) continue;

                string path = Path.Combine(candidate, "KSArmory.log");
                // Truncate per session so the file reflects this run, not every run ever.
                File.WriteAllText(path, $"=== KSArmory session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
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

    // Where the log might go, best first. KSA runs on Linux as well as Windows and puts its user
    // data in a different place on each, so this tries the plausible locations rather than assuming
    // one — and only ever uses a directory that already exists, so a wrong guess costs nothing but
    // a failed Directory.Exists. The temp directory is the last resort and always works, which is
    // why the mod can say where it is logging on startup rather than silently going quiet.
    private static IEnumerable<string> CandidateDirectories()
    {
        const string game = "Kitten Space Agency";

        // Windows, and Wine or Proton, which map Documents into the prefix the same way.
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(documents))
        {
            yield return Path.Combine(documents, "My Games", game, "Logs");
        }

        // Linux. XDG_DATA_HOME when set, otherwise the default it stands for.
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? "";
        if (string.IsNullOrEmpty(xdgData) && !string.IsNullOrEmpty(home))
        {
            xdgData = Path.Combine(home, ".local", "share");
        }

        if (!string.IsNullOrEmpty(xdgData))
        {
            yield return Path.Combine(xdgData, game, "Logs");
            yield return Path.Combine(xdgData, game);
        }

        if (!string.IsNullOrEmpty(home))
        {
            yield return Path.Combine(home, ".config", game, "Logs");
            // Some Linux ports keep the Windows-style layout under $HOME regardless.
            yield return Path.Combine(home, "My Games", game, "Logs");
        }

        yield return Path.GetTempPath();
    }
}
