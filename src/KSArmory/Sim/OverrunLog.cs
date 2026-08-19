namespace KSArmory;

/// <summary>
/// The tally of simulated time the round clamp threw away, and what is worth saying about it.
///
/// <para>The clamp runs whatever is in the air: with an empty sky the step is still cut to
/// <see cref="Interceptor.MaxFaithfulStep"/>, so the discarded seconds are counted the same way
/// either way. What differs is the consequence. Rounds are the only thing integrated across that
/// gap, so with nothing flying there is nothing behind the world afterwards — and a scene load
/// reports a step tens of seconds long into exactly that state, every time. A warning that cries
/// wolf on every load is one nobody reads on the flight where it is real.</para>
///
/// <para>Which is why the two are rate-limited apart rather than sharing a budget. They share one
/// counter and the load frame spends the report the first real overrun needs, leaving the next
/// hundred-odd silent — losing the one line that says when the lag started, which is the whole
/// reason there is a limit rather than a line per frame.</para>
/// </summary>
internal sealed class OverrunLog
{
    /// <summary>What this overrun is worth saying, if anything.</summary>
    internal enum Notice
    {
        /// <summary>Counted, and the rate limit has already spoken for this kind.</summary>
        Silent,

        /// <summary>Worth recording and nothing more: nothing was being integrated across it.</summary>
        Idle,

        /// <summary>Rounds were in the air, and they are now behind the world.</summary>
        Lagging,
    }

    /// <summary>
    /// Report the first, then one in this many. Sustained warp overruns every frame, and a line
    /// per frame buries the first one — which is the only one that says when it started.
    /// </summary>
    public const int ReportEvery = 120;

    private int _lagging;
    private int _idle;

    /// <summary>Overrun frames this session, both kinds.</summary>
    public int Frames => _lagging + _idle;

    /// <summary>Simulated seconds the clamp has thrown away, both kinds.</summary>
    public double DiscardedSeconds { get; private set; }

    /// <summary>
    /// Counts one overrun frame and answers what to say about it.
    /// </summary>
    /// <param name="stepSeconds">The step the world took, longer than one a round can integrate.</param>
    /// <param name="anyInFlight">Whether anything was in the air to be left behind by the clamp.</param>
    public Notice Observe(double stepSeconds, bool anyInFlight,
                          double maxStep = Interceptor.MaxFaithfulStep)
    {
        DiscardedSeconds += stepSeconds - maxStep;

        int seen = anyInFlight ? ++_lagging : ++_idle;
        if (seen != 1 && seen % ReportEvery != 0) return Notice.Silent;

        return anyInFlight ? Notice.Lagging : Notice.Idle;
    }
}
