namespace KSArmory;

/// <summary>What a report is about. The endpoint labels the issue from this.</summary>
public enum ReportKind
{
    Bug,
    Idea,
}

/// <summary>
/// A report being written, and the rules it has to satisfy before it is worth sending.
///
/// <para>The limits repeat <c>services/feedback/Program.cs</c>'s, so a refusal is a greyed-out button rather
/// than a round trip; the service still enforces its own.</para>
/// </summary>
public sealed class ReportDraft
{
    public const int MaxSummary = 120;
    public const int MaxDetail = 4_000;
    public const int MaxLog = 12_000;

    public const int MinSummary = 8;

    public ReportKind Kind = ReportKind.Bug;
    public string Summary = string.Empty;
    public string Detail = string.Empty;

    /// <summary>Whether to attach the mod's own log. The window sets it on for a bug, off for an idea.</summary>
    public bool AttachLog = true;

    /// <summary>What the endpoint calls this kind on the wire.</summary>
    public static string Wire(ReportKind kind) => kind == ReportKind.Bug ? "bug" : "idea";

    /// <summary>
    /// Whether reporting is offered at all, given the game build this was compiled against and
    /// the one it is running on.
    ///
    /// <para>Any difference closes both buttons, newer or older: nobody can reproduce a build this mod was
    /// not compiled for. An unknown build on either side matches, so a silent game keeps the buttons.</para>
    /// </summary>
    public static bool GameIsSupported(string? builtFor, string? running)
    {
        if (string.IsNullOrWhiteSpace(builtFor) || string.IsNullOrWhiteSpace(running)) return true;

        return Normalise(builtFor) == Normalise(running);
    }

    // "2026.8.5.5168" and "2026.8.5.5168.0" are one build: a Version can render a trailing zero the
    // lock never had.
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
    /// <para>One reason at a time, in the order the form is filled in.</para>
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
    /// <para>The end, because what went wrong is at the bottom.</para>
    /// </summary>
    public static string Tail(string? log, int limit = MaxLog)
    {
        if (string.IsNullOrEmpty(log)) return string.Empty;
        if (log.Length <= limit) return log;

        string cut = log[^limit..];

        // Drops the partial first line, unless that loses most of a log with no line breaks.
        int firstBreak = cut.IndexOf('\n');
        if (firstBreak >= 0 && firstBreak < cut.Length / 2) cut = cut[(firstBreak + 1)..];

        return cut;
    }
}
