namespace KSArmory.Feedback;

/// <summary>
/// Whether a report's log may be rendered on a public page. The judgement is a delegate so the
/// policy can be tested without the model's weights.
/// </summary>
public static class LogGate
{
    /// <summary>
    /// True when every line of the log was read and none of them was refused. <b>Per line</b>,
    /// because one abusive line judged among dull ones dilutes: insult 0.95 alone, 0.34 in company.
    /// <b>All of it</b>, because a log the condensing limits cut short has a part nobody scored.
    /// </summary>
    public static bool MayPublish(string? log, Func<string, bool> refuses)
    {
        Guard.Condensed condensed = Guard.Condense(log);

        return condensed.Whole && condensed.Lines.All(line => !refuses(line));
    }
}
