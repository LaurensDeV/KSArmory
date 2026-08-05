namespace KSArmory.Feedback;

/// <summary>
/// Whether a report's log may be rendered on a public page.
///
/// <para>Separate from the endpoint, and taking the judgement as a delegate, because this is the
/// half worth pinning: the model's scores are measured elsewhere and cannot run without the
/// weights, while the <em>policy</em> — every line, all of it, or nothing — is a property of this
/// function and holds whatever the model says.</para>
/// </summary>
public static class LogGate
{
    /// <summary>
    /// True when every line of the log was read and none of them was refused.
    ///
    /// <para>Two rules, and both have already been got wrong here. <b>Per line</b>, because one
    /// abusive line among a dozen dull ones dilutes to nothing when they are judged together —
    /// measured at insult 0.95 alone against 0.34 in company. <b>All of it</b>, because the
    /// condensing limits can cut a hostile log short, and publishing the part past the cut would
    /// mean publishing exactly the part nobody scored.</para>
    /// </summary>
    public static bool MayPublish(string? log, Func<string, bool> refuses)
    {
        Guard.Condensed condensed = Guard.Condense(log);

        return condensed.Whole && condensed.Lines.All(line => !refuses(line));
    }
}
