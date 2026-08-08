namespace KSArmory.Sim;

/// <summary>What a report is about. The endpoint labels the issue from this.</summary>
public enum ReportKind
{
    Bug,
    Idea,
}

/// <summary>
/// A report being written, and the rules it has to satisfy before it is worth sending.
///
/// <para>The limits are the endpoint's own, repeated here on purpose: checking them locally turns
/// a refusal that costs a round trip and reads as a server error into a greyed-out button. They
/// are the outer bound, not a second opinion — the service still enforces its own, because a mod
/// is a thing a stranger can edit.</para>
/// </summary>
public sealed class ReportDraft
{
    public const int MaxSummary = 120;
    public const int MaxDetail = 4_000;
    public const int MaxLog = 12_000;

    /// <summary>Short enough that a one-word summary is refused before it is sent.</summary>
    public const int MinSummary = 8;

    public ReportKind Kind = ReportKind.Bug;
    public string Summary = string.Empty;
    public string Detail = string.Empty;

    /// <summary>Whether to attach the mod's own log. On for a bug, off for an idea.</summary>
    public bool AttachLog = true;

    /// <summary>What the endpoint calls this kind on the wire.</summary>
    public static string Wire(ReportKind kind) => kind == ReportKind.Bug ? "bug" : "idea";

    /// <summary>
    /// Whether reporting is offered at all, given the game build this was compiled against and
    /// the one it is running on.
    ///
    /// <para>Any difference closes it, newer or older alike. A report against a build this mod
    /// was never compiled for describes a combination nobody can reproduce or fix, and KSA's
    /// internals move between builds — that is the whole reason this repository pins one.</para>
    ///
    /// <para>It gates <em>both</em> buttons. Leaving feedback open would make it the way to file
    /// a bug report, which is the thing being prevented.</para>
    ///
    /// <para>An unknown build on either side is treated as matching: refusing on missing
    /// information would silently remove the buttons for anyone whose game did not answer, and a
    /// stray report is cheaper than a player with no way to say anything.</para>
    /// </summary>
    public static bool GameIsSupported(string? builtFor, string? running)
    {
        if (string.IsNullOrWhiteSpace(builtFor) || string.IsNullOrWhiteSpace(running)) return true;

        return Normalise(builtFor) == Normalise(running);
    }

    // "2026.8.5.5168" and "2026.8.5.5168.0" are the same build said two ways: the lock writes four
    // components and an assembly version always reports four, but a Version can render a trailing
    // zero the lock never had.
    private static string Normalise(string version)
    {
        string trimmed = version.Trim().TrimStart('v', 'V');

        while (trimmed.EndsWith(".0", StringComparison.Ordinal)
               && trimmed.Count(c => c == '.') > 2)
        {
            trimmed = trimmed[..^2];
        }

        return trimmed;
    }

    /// <summary>
    /// Why this cannot be sent yet, or null when it can.
    ///
    /// <para>One reason at a time, in the order someone fills the form in: telling them the detail
    /// is too long while the summary is still empty is noise.</para>
    /// </summary>
    public static string? Problem(string? summary, string? detail)
    {
        string subject = (summary ?? string.Empty).Trim();
        string body = (detail ?? string.Empty).Trim();

        if (subject.Length == 0) return "a one-line summary is needed";
        if (subject.Length < MinSummary) return "the summary is too short to act on";
        if (subject.Length > MaxSummary) return $"the summary is {subject.Length - MaxSummary} characters too long";
        if (body.Length > MaxDetail) return $"the detail is {body.Length - MaxDetail} characters too long";

        return null;
    }

    /// <summary>
    /// The last <paramref name="limit"/> characters of a log, cut at a line boundary.
    ///
    /// <para>The end, not the beginning: a log runs to tens of thousands of characters and what
    /// went wrong is at the bottom of it. Cutting mid-line would put a fragment at the top of the
    /// issue that reads like a truncated message rather than a truncated file.</para>
    /// </summary>
    public static string Tail(string? log, int limit = MaxLog)
    {
        if (string.IsNullOrEmpty(log)) return string.Empty;
        if (log.Length <= limit) return log;

        string cut = log[^limit..];

        // Drop the first, partial line -- unless that would throw away nearly everything, which
        // is the case for a log with no line breaks at all.
        int firstBreak = cut.IndexOf('\n');
        if (firstBreak >= 0 && firstBreak < cut.Length / 2) cut = cut[(firstBreak + 1)..];

        return cut;
    }
}
