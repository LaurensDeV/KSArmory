using System.Text;

namespace KSArmory;

/// <summary>
/// Writes to stdout and to a file beside KSA's own logs.
///
/// The file matters: StarMap mods only reach stdout, which lands in whatever terminal launched
/// the game and is awkward to read (and impossible to read after the fact). KSA's own
/// <c>KittenSpaceAgency.log</c> is written by its internal logger, which mods cannot reach.
/// So this mod keeps its own, in the same folder, where it can be tailed from outside the game.
///
/// Lines are batched rather than written one at a time. A salvo at full rate is hundreds of
/// outcome lines a second, and an open/write/close each is a syscall storm on the frame thread.
/// What that costs is a bounded tail on a hard crash, so <see cref="FlushIntervalMs"/> bounds it
/// and anything above <see cref="Level.Info"/> goes straight to disk.
/// </summary>
internal static class Log
{
    private const string Prefix = "[KSArmory]";

    // Guards the buffer and the writer. Not only the frame and GUI hooks: FeedbackClient reports
    // the outcome of a send from a thread-pool task.
    private static readonly object Gate = new();

    private static string? _path;
    private static bool _resolved;
    private static bool _fileBroken;

    // The most a crash may take with it. 100 ms is six frames at 60 fps -- inside the moment the
    // fault happened, so the lines that explain it survive -- while turning ~750 writes a second
    // into ten.
    private const int FlushIntervalMs = 100;

    // A single frame can dump thousands of lines (the verbose world dump walks every craft), which
    // the interval alone would let accumulate unbounded. 64 KB is a few hundred lines.
    private const int FlushBytes = 64 * 1024;

    private static readonly StringBuilder Pending = new();
    private static long _lastFlushMs = Environment.TickCount64;

    // Fires whether or not anything is logging, so a session that goes quiet still gets its last
    // lines to disk. Checking the interval on the way through Write cannot do that: the write that
    // would notice never comes.
    private static Timer? _ticker;

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

    /// <summary>
    /// Full path of the log file, or null if no writable location was found.
    ///
    /// <para>Flushes on the way past, because every reader of the file goes through here. A bug
    /// report attaching the tail would otherwise be missing the lines that prompted it.</para>
    /// </summary>
    public static string? FilePath
    {
        get
        {
            lock (Gate)
            {
                EnsureResolved();
                FlushLocked();
                return _path;
            }
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

            // Here rather than in EnsureResolved, which runs once ever: Shutdown stops the ticker,
            // and a mod loaded again in the same process has to get one back.
            _ticker ??= new Timer(_ => Flush(), null, FlushIntervalMs, FlushIntervalMs);

            Pending.Append(line).Append(Environment.NewLine);

            // Anything that went wrong goes to disk now. A warning is often the last thing written
            // before whatever it was warning about takes the process down.
            if (level >= Level.Warn
                || Pending.Length >= FlushBytes
                || Environment.TickCount64 - _lastFlushMs >= FlushIntervalMs)
            {
                FlushLocked();
            }
        }
    }

    /// <summary>
    /// Writes anything still buffered and stops the background flush. For unload, after the last
    /// line: a timer held in a static field would otherwise go on firing into a mod that is no
    /// longer loaded.
    /// </summary>
    public static void Shutdown()
    {
        lock (Gate)
        {
            _ticker?.Dispose();
            _ticker = null;
            FlushLocked();
        }
    }

    private static void Flush()
    {
        lock (Gate) FlushLocked();
    }

    // Caller holds Gate.
    private static void FlushLocked()
    {
        _lastFlushMs = Environment.TickCount64;

        if (Pending.Length == 0 || _path is null || _fileBroken) return;

        try
        {
            File.AppendAllText(_path, Pending.ToString());
        }
        catch
        {
            // Disk full, file locked, permissions. Stop trying rather than throwing every
            // frame - stdout still works.
            _fileBroken = true;
        }

        Pending.Clear();
    }

    /// <summary>
    /// The folder the log went to, which is the one place this mod knows how to find on every
    /// platform. Anything else it writes goes beside it rather than repeating the search.
    /// </summary>
    public static string Folder
    {
        get
        {
            lock (Gate) EnsureResolved();
            return _path is null ? Path.GetTempPath() : Path.GetDirectoryName(_path) ?? Path.GetTempPath();
        }
    }

    // Picks a log location once: KSA's own Logs folder if it can be found, otherwise the user's
    // temp directory.
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
