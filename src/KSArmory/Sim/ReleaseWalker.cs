namespace KSArmory;

/// <summary>
/// The cursor <see cref="ReleaseLoop.Step"/> is written against: which stop a bus is on, how many
/// of that stop's warheads have gone, and the three numbers a flight reads off it.
///
/// <para><b>Every statement a walk makes is behind <see cref="Walking"/>, and it is false for a set
/// of one.</b> <see cref="ReleaseLoop.Plan"/> answers <see cref="ReleaseWalkHold.OneStop"/> for any
/// one-stop set, so <see cref="GateOverrideSeconds"/> is NaN, <see cref="TubesLeft"/> hands back the
/// magazine it was given and <see cref="TargetIndex"/> hands back the lead — each of them the value
/// a flight with no walk reads. That is what makes a single-target shot the one every
/// accuracy measurement on this mod was taken against, and it is pinned in
/// <c>ReleaseWalkerTests</c> rather than left to reading <c>Ksa/IcbmComputer.cs</c>.</para>
///
/// <para><b>A plan is committed by the first warhead leaving.</b> Until then the set can still be
/// edited and the walk is re-planned as the reach is re-flown; after it the bus has already been
/// somewhere, and re-ordering the stops behind it would aim it at a place it has been.</para>
/// </summary>
internal sealed class ReleaseWalker
{
    private ReleaseWalk _walk;
    private bool _committed;

    /// <summary>The plan being flown, or a default one holding no stops.</summary>
    public ReleaseWalk Walk => _walk;

    /// <summary>Which stop the bus is on, counted from zero.</summary>
    public int Stop { get; private set; }

    /// <summary>How many warheads have left at this stop — what ends it.</summary>
    public int AwayThisStop { get; private set; }

    /// <summary>Every stop that fits has had its warheads, so nothing is left to re-aim for.</summary>
    public bool Finished { get; private set; }

    /// <summary>
    /// Whether the flight is doing something a single-target one does not. <b>The one gate</b>: no
    /// statement of the loop runs with this false.
    /// </summary>
    public bool Walking => _walk.Walks && !Finished;

    /// <summary>A walk that ran to its end, as opposed to one that never existed.</summary>
    public bool Done => _walk.Walks && Finished;

    /// <summary>
    /// Whether a warhead has gone, so the plan is the one being flown rather than one being made.
    ///
    /// <para>What a caller has to ask before letting the target list be edited: a stop names an
    /// index into that list, so taking an entry out from under a committed walk sends the bus to
    /// whichever place slid into the gap.</para>
    /// </summary>
    public bool Committed => _committed;

    /// <summary>What this stop wants of the flight right now.</summary>
    public ReleaseStep Step => ReleaseLoop.Step(_walk, Stop, AwayThisStop);

    /// <summary>
    /// Take a freshly planned walk, refusing one once the first warhead has gone.
    /// </summary>
    /// <returns>Whether it was taken.</returns>
    public bool Plan(in ReleaseWalk walk)
    {
        if (_committed) return false;

        _walk = walk;
        Stop = 0;
        AwayThisStop = 0;
        Finished = false;
        return true;
    }

    /// <summary>One warhead has left, which is also what commits the plan.</summary>
    public void WarheadAway()
    {
        _committed = true;
        if (_walk.Walks && !Finished) AwayThisStop++;
    }

    /// <summary>Move to the next stop, which is the frame the bus re-aims.</summary>
    public void Advance()
    {
        if (!Walking) return;

        Stop++;
        AwayThisStop = 0;
    }

    /// <summary>End the walk, so nothing more is released and the trim has nobody left to aim for.</summary>
    public void Finish() => Finished = true;

    /// <summary>Back to no plan at all, for a flight being started over.</summary>
    public void Reset()
    {
        _walk = default;
        _committed = false;
        Stop = 0;
        AwayThisStop = 0;
        Finished = false;
    }

    /// <summary>
    /// When the warheads may first go, in seconds before arrival, or NaN for the setting the flight
    /// has always read.
    /// </summary>
    /// <remarks>
    /// NaN rather than <paramref name="gateSeconds"/> so that a flight with no walk reads
    /// <see cref="IcbmConfig.ReleaseBeforeArrivalSeconds"/> <em>live</em>: latching the number here
    /// would freeze a setting the player can still move mid-flight.
    /// </remarks>
    public double GateOverrideSeconds(double gateSeconds)
        => Walking ? ReleaseLoop.FirstBeforeArrivalSeconds(_walk, gateSeconds) : double.NaN;

    /// <summary>
    /// How much of the magazine <see cref="ReleaseSequence"/> may let go, which is this stop's quota
    /// rather than everything loaded.
    /// </summary>
    /// <remarks>
    /// Zero once the walk is over, so a rack that reloads — which is what a rack does a few seconds
    /// after a salvo — cannot send warheads the plan never assigned anywhere.
    /// </remarks>
    public int TubesLeft(int loaded)
        => !_walk.Walks ? loaded
         : Finished ? 0
         : ReleaseLoop.TubesLeftForStop(Step, loaded);

    /// <summary>Which target a warhead leaving now is being sent to.</summary>
    public int TargetIndex(int lead) => Walking ? Step.Target : lead;

    /// <summary>
    /// The line a night is scored off, one per stop.
    /// </summary>
    /// <remarks>
    /// <b>The shape is the contract</b>, not the prose: <c>tools/shot-report.py</c> and
    /// <c>Ksa/BallisticScenario.cs</c> read the target index and the count out of it, so it is
    /// formatted here where a test can hold it rather than at the call site.
    /// </remarks>
    /// <param name="target">Counted from zero, as <see cref="TargetSet.Entries"/> indexes.</param>
    /// <param name="divertMetresPerSecond">
    /// What the trim spent on this stop — the hop from the one before, or, on the first, everything
    /// since cutoff: the separation null and the first aim correction, which no hop pays for.
    /// </param>
    public static string SayRelease(int stop, int stops, string craft, int target, string site,
                                    int warheads, double divertMetresPerSecond,
                                    double leftMetresPerSecond)
        => $"released {stop} of {stops} on {craft}: target {target} {site}, "
           + $"{warheads} warhead{(warheads == 1 ? "" : "s")}, "
           + $"divert {divertMetresPerSecond:F2} m/s, {leftMetresPerSecond:F1} m/s left";

    /// <summary>
    /// What the whole walk came to: every stop, what went there, and what it cost in all.
    /// </summary>
    /// <param name="wentTo">
    /// One entry per stop flown, in flown order — the target as <see cref="TargetSet.Entries"/>
    /// indexes it, its description, and how many warheads actually left there, which is fewer than
    /// the quota when the rack could not fill it.
    /// </param>
    /// <param name="keptAboard">
    /// Warheads no stop took. <b>They stay aboard rather than being scattered</b>, which is what an
    /// unassigned warhead has always done.
    /// </param>
    public static string SayWalk(string craft, IReadOnlyList<(int Target, string Site, int Warheads)> wentTo,
                                 double spentMetresPerSecond, double budgetMetresPerSecond,
                                 int keptAboard)
    {
        string[] stops = new string[wentTo.Count];

        for (int k = 0; k < wentTo.Count; k++)
        {
            stops[k] = $"target {wentTo[k].Target} {wentTo[k].Site} took {wentTo[k].Warheads}";
        }

        return $"walk complete on {craft}: {wentTo.Count} stop{(wentTo.Count == 1 ? "" : "s")}"
               + (stops.Length > 0 ? $" -- {string.Join("; ", stops)}" : "")
               + $" -- {spentMetresPerSecond:F1} m/s of {budgetMetresPerSecond:F0} spent, "
               + $"{keptAboard} warhead{(keptAboard == 1 ? "" : "s")} kept aboard";
    }
}
