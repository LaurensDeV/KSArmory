namespace KSArmory;

/// <summary>
/// The cursor <see cref="ReleaseLoop.Step"/> is written against: which stop a bus is on and how many
/// of that stop's warheads have gone.
///
/// <para><b>Every statement a walk makes is behind <see cref="Walking"/>, false for a set of one</b>,
/// so a single-target flight reads exactly what it would with no walk; <c>ReleaseWalkerTests</c> pins
/// it. The first warhead leaving commits the plan.</para>
/// </summary>
internal sealed class ReleaseWalker
{
    private ReleaseWalk _walk;
    private bool _committed;

    public ReleaseWalk Walk => _walk;

    /// <summary>Which stop the bus is on, counted from zero.</summary>
    public int Stop { get; private set; }

    /// <summary>How many warheads have left at this stop — what ends it.</summary>
    public int AwayThisStop { get; private set; }

    /// <summary>Every stop that fits has had its warheads, so nothing is left to re-aim for.</summary>
    public bool Finished { get; private set; }

    /// <summary>The one gate: no statement of the loop runs with this false.</summary>
    public bool Walking => _walk.Walks && !Finished && !Curtailed;

    /// <summary>
    /// The walk ended at the stop the bus is on, which takes every warhead left: what a hop the trim
    /// will not fly comes to, since the bus is already trimmed onto this target.
    /// </summary>
    public bool Curtailed { get; private set; }

    /// <summary>A walk that ran to its end, as opposed to one that never existed.</summary>
    public bool Done => _walk.Walks && Finished;

    /// <summary>
    /// Whether a warhead has gone. Asked before the target list is edited: a stop names an index into
    /// it, so removing an entry under a committed walk sends the bus to whatever slid into the gap.
    /// </summary>
    public bool Committed => _committed;

    /// <summary>
    /// Whether a walk's first stop has let all its warheads go, which is after the split has been
    /// nulled. Never for a set of one.
    /// </summary>
    public bool FirstStopAway => _walk.Walks && (Stop > 0 || !Step.ReleaseHere);

    public ReleaseStep Step => ReleaseLoop.Step(_walk, Stop, AwayThisStop);

    /// <returns>False once the first warhead has gone.</returns>
    public bool Plan(in ReleaseWalk walk)
    {
        if (_committed) return false;

        _walk = walk;
        Stop = 0;
        AwayThisStop = 0;
        Finished = false;
        Curtailed = false;
        return true;
    }

    public void WarheadAway()
    {
        _committed = true;
        if (_walk.Walks && !Finished) AwayThisStop++;
    }

    public void Advance()
    {
        if (!Walking) return;

        Stop++;
        AwayThisStop = 0;
    }

    public void Finish() => Finished = true;

    /// <summary>
    /// For a hop the trim will refuse. Not <see cref="Finish"/>, which takes <see cref="TubesLeft"/> to
    /// zero and leaves the rest of the magazine aboard.
    /// </summary>
    public void StopHere()
    {
        if (!Walking) return;

        Curtailed = true;
    }

    public void Reset()
    {
        _walk = default;
        _committed = false;
        Stop = 0;
        AwayThisStop = 0;
        Finished = false;
        Curtailed = false;
        _coastSeconds = double.NaN;
    }

    private double _coastSeconds = double.NaN;

    /// <summary>
    /// The coast a set is planned against: its own length, latched the first time it is known.
    /// </summary>
    /// <remarks>
    /// <para>Not what is left of it: handed the countdown, <see cref="ReleaseItinerary.CoastFits"/> stops
    /// holding another hop at <c>gate + hop</c>, which is the frame the first release becomes due, and
    /// the walk never starts.</para>
    ///
    /// <para>NaN until the vehicle is coasting, which leaves the count unbounded by the coast.</para>
    /// </remarks>
    /// <param name="secondsToArrival"><see cref="IcbmProgram.CommittedArrivalFromNow"/>; only the first is kept.</param>
    public double CoastForPlanning(double secondsToArrival, bool coasting)
    {
        if (!coasting) return double.NaN;

        if (!double.IsFinite(_coastSeconds) && double.IsFinite(secondsToArrival)
            && secondsToArrival > 0.0)
        {
            _coastSeconds = secondsToArrival;
        }

        return _coastSeconds;
    }

    /// <summary>
    /// When the warheads may first go, in seconds before arrival, or NaN with no walk — so the flight
    /// reads <see cref="IcbmConfig.ReleaseBeforeArrivalSeconds"/> live, a setting the player can move mid-flight.
    /// </summary>
    public double GateOverrideSeconds(double gateSeconds)
        => Walking ? ReleaseLoop.FirstBeforeArrivalSeconds(_walk, gateSeconds) : double.NaN;

    /// <summary>
    /// <see cref="GateOverrideSeconds"/>, or <see cref="IcbmConfig.SingleTargetReleaseBeforeArrivalSeconds"/> for one
    /// target. Several keep the walk's gate before a walk is planned: the split and the first hop's price count from it.
    /// </summary>
    public double GateOverrideSeconds(IcbmConfig config, int targets)
    {
        double walk = GateOverrideSeconds(config.ReleaseBeforeArrivalSeconds);
        if (double.IsFinite(walk) || targets > 1) return walk;
        return config.SingleTargetReleaseBeforeArrivalSeconds >= 0.0 ? config.SingleTargetReleaseBeforeArrivalSeconds : double.NaN;
    }

    /// <summary>
    /// How much of the magazine <see cref="ReleaseSequence"/> may let go: this stop's quota. Zero once
    /// the walk is over, so a rack that reloads cannot send warheads the plan never assigned.
    /// </summary>
    public int TubesLeft(int loaded)
        => !_walk.Walks || Curtailed ? loaded
         : Finished ? 0
         : ReleaseLoop.TubesLeftForStop(Step, loaded);

    /// <summary>Which target a warhead leaving now is being sent to; a curtailed walk's is the stop it is on.</summary>
    public int TargetIndex(int lead)
        => _walk.Walks && !Finished ? Step.Target : lead;

    /// <summary>
    /// The line a night is scored off, one per stop. Its shape is a contract: <c>tools/shot-report.py</c>
    /// parses it, so it is formatted here where a test can hold it.
    /// </summary>
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

    /// <param name="wentTo">
    /// One entry per stop flown, in flown order, with how many warheads actually left there — fewer than
    /// the quota when the rack could not fill it.
    /// </param>
    /// <param name="keptAboard">Warheads no stop took; they stay aboard rather than being scattered.</param>
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
