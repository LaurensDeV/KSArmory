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

    /// <summary>Whether the draft as it stands would be accepted.</summary>
    public bool CanSend => Problem(Summary, Detail) is null;

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
