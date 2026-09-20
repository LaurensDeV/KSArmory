namespace KSArmory;

/// <summary>
/// When a bus lets each of its targets' warheads go, in what order, and whether it can pay for the
/// hops between them.
///
/// <para><b>Today's release gate is a single-target optimisation.</b>
/// <see cref="IcbmConfig.ReleaseBeforeArrivalSeconds"/> holds the warheads until 420 s before
/// arrival, which shrinks what the ejection kick has left to grow into and is why a group lands
/// millimetres from its aim. For several targets it is ruinous, because the bus's reach collapses as
/// the arrival approaches: six hops of <see cref="BusTrim.MaxMetresPerSecond"/> spaced
/// <see cref="MedianHopSeconds"/> apart reach 3.5 km down to <b>0.4 km</b> started from the gate,
/// against 10.7 down to 8.9 km started from cutoff — and the Mk 21's
/// <see cref="Warhead.LethalRadius"/> is 2.0 km, so targets four to six would land inside each
/// other's lethal circles. Six targets that are one pattern.</para>
///
/// <para><b>So the itinerary is started early and <em>ends</em> at the gate</b> rather than
/// beginning there: the first release is <c>gate + (N-1) x hop</c> seconds before arrival and each
/// one after it is a hop later. <b>A set of one is then exactly today's answer</b>, which is the
/// property worth the most here — every accuracy measurement this mod has is taken against the
/// single-target shot, so a schedule that moved it would move all of them.</para>
///
/// <para><b>The dearest reach goes in the earliest slot.</b> Reach falls steeply as the bus
/// descends: 50 km of along-track divert at 6,179 km costs 18.2 m/s bought at cutoff, 26.7 at
/// +400 s, 47.8 at +800 and 72.5 at the gate. Nothing accrues against waiting — the holding cost
/// itself <em>falls</em>, 1.14 to 1.00 to 0.92 m/s over that same coast — so what a late target
/// loses is reach and only reach. Farthest first, and since the first stop is the one the bus
/// already arrives on, <b>the booster's aim is the farthest target</b>.</para>
///
/// <para><b>It reports rather than truncates.</b> Dropping a target moves every release later, so a
/// trimmed set is a different schedule and not a prefix of this one: <see cref="Fits"/> says how
/// many the budget and the coast reach, and the caller re-plans. <c>docs/MIRV-TARGETS.md</c> is the
/// whole plan.</para>
/// </summary>
internal readonly record struct ReleaseItinerary(IReadOnlyList<ReleaseItinerary.Stop> Stops,
                                                 ReleaseItinerary.Bus Means,
                                                 int WithoutWarheads)
{
    /// <summary>
    /// How long a re-aim between two targets takes, in seconds.
    ///
    /// <para>A median 65.1 s over 96 flown buses (p10 58.6, p90 73.2, worst 80.3): 4 s of
    /// clearance, ~16 s of separation null and a median four correction passes at ~11 s each. A
    /// divert burn adds its own on top, at the 0.56 m/s² the thrusters are measured at.</para>
    /// </summary>
    public const double MedianHopSeconds = 65.1;

    /// <summary>
    /// What one target already costs, in metres a second of trim.
    ///
    /// <para>The flown median of a single-target shot over 96 buses (p90 17.9, worst 21.8) — the
    /// separation null and the aim correction, neither of which a second target makes cheaper. It is
    /// the floor every itinerary starts from rather than a hop.</para>
    /// </summary>
    public const double SingleTargetMetresPerSecond = 16.1;

    /// <summary>One place the bus is sent, and all the schedule needs to know about it.</summary>
    /// <param name="Warheads">
    /// How many of the bus's warheads leave here. <b>A target with none is not a stop</b>: a divert
    /// to a place nothing is released at spends the hop and lands nothing.
    /// </param>
    /// <param name="ReachMetres">
    /// How far its landing sits from the trajectory the booster flew. This <em>orders</em> the set
    /// and does nothing else, which is why it can be read at any one epoch: the decay scales every
    /// target's cost together. It is a proxy rather than an optimum, because the two axes of the
    /// reach ellipse decay at different rates — 4.0x along the track against 2.7x across it over the
    /// 6,179 km coast — so a set spread across both axes can rank differently at the two ends.
    /// </param>
    /// <param name="HopMetresPerSecond">
    /// What the hop onto it costs, or NaN for <see cref="Bus.HopMetresPerSecond"/>. Pricing a
    /// particular displacement belongs to <c>DivertFootprint</c> and depends on the epoch the hop
    /// lands in — which is what this schedule decides — so the per-pass ceiling is what a plan can be
    /// built on before one exists.
    /// </param>
    internal readonly record struct Target(int Warheads, double ReachMetres,
                                           double HopMetresPerSecond = double.NaN);

    /// <summary>What the bus has to spend, and how long it has to spend it in.</summary>
    /// <param name="GateSeconds">
    /// <see cref="IcbmConfig.ReleaseBeforeArrivalSeconds"/>, which is when the <em>last</em> release
    /// happens. Everything else is counted back from it.
    /// </param>
    /// <param name="CoastSeconds">
    /// From the earliest release the flight allows — cutoff, above
    /// <see cref="IcbmConfig.DeployAltitudeMetres"/> — to arrival: 1,315 s at 6,179 km and 351 at
    /// 2,000, so the short shot is the one that runs out of clock rather than propellant.
    /// </param>
    /// <param name="Warheads">
    /// What the magazine holds, which is the other bound on how many targets can be stops.
    /// </param>
    /// <param name="BudgetMetresPerSecond">
    /// <see cref="PostBoostAim.MaxTrimMetresPerSecond"/>. Non-finite is no bound rather than a
    /// refusal of everything.
    /// </param>
    /// <param name="SpentMetresPerSecond">What a single-target flight costs before any hop.</param>
    /// <param name="HopMetresPerSecond">
    /// What a hop costs when the target does not say — <see cref="BusTrim.MaxMetresPerSecond"/>, the
    /// most one trim pass will fly. A bigger one needs that ceiling raised deliberately
    /// (<see cref="IcbmConfig.TrimCeilingFromBudget"/>) rather than discovered as a refused solve.
    /// </param>
    internal readonly record struct Bus(double GateSeconds, double CoastSeconds, int Warheads,
                                        double HopSeconds = MedianHopSeconds,
                                        double BudgetMetresPerSecond = PostBoostAim.MaxTrimMetresPerSecond,
                                        double SpentMetresPerSecond = SingleTargetMetresPerSecond,
                                        double HopMetresPerSecond = BusTrim.MaxMetresPerSecond);

    /// <summary>One release, in the order it is flown.</summary>
    /// <param name="Target">Which of the targets as given, since the order flown is not the order chosen.</param>
    /// <param name="HopMetresPerSecond">What getting here cost, which is nothing for the first stop.</param>
    /// <param name="SpentMetresPerSecond">The running total, starting from the single-target flight's own.</param>
    /// <param name="Affordable">
    /// Whether the budget still covers it. <b>The first stop is affordable whatever the budget
    /// says</b>: the bus arrives on that trajectory, so refusing it would refuse the shot that
    /// already flies.
    /// </param>
    internal readonly record struct Stop(int Target, int Warheads, double BeforeArrivalSeconds,
                                         double HopMetresPerSecond, double SpentMetresPerSecond,
                                         bool Affordable);

    /// <summary>
    /// When the <paramref name="index"/>'th of <paramref name="targets"/> releases, in seconds
    /// before arrival.
    ///
    /// <para>Counted back from the gate rather than forward from cutoff, so the last one lands on it
    /// exactly whatever the set's size.</para>
    /// </summary>
    public static double BeforeArrivalSeconds(int index, int targets, double gateSeconds,
                                              double hopSeconds)
    {
        // A hop that is not a number is no hop, which collapses the itinerary onto the gate -- the
        // one shape that is known to fly.
        double hop = double.IsFinite(hopSeconds) ? Math.Max(0.0, hopSeconds) : 0.0;

        return gateSeconds + (Math.Max(0, targets - 1 - index) * hop);
    }

    /// <summary>Order a set, time it and cost it.</summary>
    public static ReleaseItinerary Plan(IReadOnlyList<Target>? targets, in Bus bus)
    {
        if (targets is null || targets.Count == 0) return new ReleaseItinerary([], bus, 0);

        int count = targets.Count;
        int[] order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;

        // Ties keep the order the player chose, which is the only one they stated.
        Array.Sort(order, (a, b) =>
        {
            int byReach = Ranked(targets[b].ReachMetres).CompareTo(Ranked(targets[a].ReachMetres));
            return byReach != 0 ? byReach : a.CompareTo(b);
        });

        // The warheads go out in the order flown, so a magazine that runs out costs the targets with
        // the least reach demanded rather than whichever were named last.
        int left = Math.Max(0, bus.Warheads);
        List<(int Target, int Warheads)> taken = new(count);
        int without = 0;

        foreach (int i in order)
        {
            int want = Math.Max(0, targets[i].Warheads);
            int got = Math.Min(want, left);

            if (got <= 0)
            {
                if (want > 0) without++;
                continue;
            }

            left -= got;
            taken.Add((i, got));
        }

        Stop[] stops = new Stop[taken.Count];
        double spent = Math.Max(0.0, bus.SpentMetresPerSecond);

        for (int k = 0; k < stops.Length; k++)
        {
            double hop = k == 0 ? 0.0 : HopOnto(targets[taken[k].Target], bus);
            spent += hop;

            stops[k] = new Stop(taken[k].Target, taken[k].Warheads,
                                BeforeArrivalSeconds(k, stops.Length, bus.GateSeconds, bus.HopSeconds),
                                hop, spent, k == 0 || Within(spent, bus.BudgetMetresPerSecond));
        }

        return new ReleaseItinerary(stops, bus, without);
    }

    /// <summary>
    /// A set of <paramref name="targets"/> with nothing said about where they are: one warhead each
    /// and every hop at <see cref="Bus.HopMetresPerSecond"/>.
    ///
    /// <para>What a panel can answer before a footprint exists, and the arithmetic a six-target set
    /// is refused on.</para>
    /// </summary>
    public static ReleaseItinerary AtTheTrimCeiling(int targets, in Bus bus)
    {
        Target[] set = new Target[Math.Max(0, targets)];
        for (int i = 0; i < set.Length; i++) set[i] = new Target(1, 0.0);

        return Plan(set, bus);
    }

    public int Count => Stops?.Count ?? 0;

    /// <summary>How many stops the budget reaches, counted from the first.</summary>
    public int BudgetFits
    {
        get
        {
            int fits = 0;
            while (fits < Count && Stops[fits].Affordable) fits++;

            return fits;
        }
    }

    /// <summary>
    /// How many stops the coast is long enough for.
    ///
    /// <para>At least one whatever the coast says: the gate is a ceiling on the wait rather than a
    /// precondition, so a shot whose whole coast is shorter than it releases as soon as the altitude
    /// allows — which is what a single-target flight already does.</para>
    ///
    /// <para><b>Which is also why a short shot fits one and only one.</b> At 2,000 km the usable
    /// coast is 351 s against a 420 s gate, so ending on the gate leaves nothing to start early in.
    /// Several targets there are a reason to move the gate, not a schedule this can produce.</para>
    /// </summary>
    public int CoastFits
    {
        get
        {
            if (Count == 0) return 0;

            double hop = Means.HopSeconds;
            if (!double.IsFinite(Means.CoastSeconds) || !double.IsFinite(hop) || hop <= 0.0) return Count;

            double room = Means.CoastSeconds - Means.GateSeconds;
            int fits = room < 0.0 ? 1 : 1 + (int)Math.Floor(room / hop);

            return Math.Clamp(fits, 1, Count);
        }
    }

    /// <summary>How many targets this bus can actually be sent to, which is the lower of the two bounds.</summary>
    public int Fits => Math.Min(BudgetFits, CoastFits);

    /// <summary>
    /// What the whole set costs, which is what <see cref="PostBoostAim.MaxTrimMetresPerSecond"/>
    /// would have to become for it to fit.
    /// </summary>
    public double NeedsMetresPerSecond => Count == 0 ? 0.0 : Stops[^1].SpentMetresPerSecond;

    /// <summary>What the last target that fits costs, by the time the bus has reached it.</summary>
    public double FitsWithinMetresPerSecond => Fits == 0 ? 0.0 : Stops[Fits - 1].SpentMetresPerSecond;

    public double FirstBeforeArrivalSeconds => Count == 0 ? double.NaN : Stops[0].BeforeArrivalSeconds;

    public double LastBeforeArrivalSeconds => Count == 0 ? double.NaN : Stops[^1].BeforeArrivalSeconds;

    /// <summary>Whether the first release would have to happen before the bus is there to make it.</summary>
    public bool StartsBeforeCutoff => Count > 0 && double.IsFinite(Means.CoastSeconds)
                                      && FirstBeforeArrivalSeconds > Means.CoastSeconds;

    /// <summary>What the panel says about the itinerary, in one line, with every refusal named.</summary>
    public string Describe()
    {
        if (Count == 0) return "no targets";

        string when = Count == 1
                          ? $"released {LastBeforeArrivalSeconds:F0} s before arrival"
                          : $"first release {FirstBeforeArrivalSeconds:F0} s before arrival, "
                            + $"last at {LastBeforeArrivalSeconds:F0} s";

        string line = $"{Count} target{(Count == 1 ? "" : "s")}, {when}, "
                      + $"{NeedsMetresPerSecond:F1} m/s of {Means.BudgetMetresPerSecond:F0}";

        List<string> refused = [];

        if (BudgetFits < Count) refused.Add($"only {BudgetFits} fit the budget");
        if (CoastFits < Count) refused.Add($"only {CoastFits} fit the coast");
        if (WithoutWarheads > 0)
        {
            refused.Add($"{WithoutWarheads} target{(WithoutWarheads == 1 ? " gets" : "s get")} no warhead");
        }

        return refused.Count == 0 ? line : $"{line} -- {string.Join(", ", refused)}";
    }

    // NaN compares equal to nothing, so a reach the footprint could not price makes the comparison
    // inconsistent and Array.Sort is entitled to throw on that. An unpriced target ranks first: the
    // last slot is the one no later pass can recover from.
    private static double Ranked(double reachMetres)
        => double.IsFinite(reachMetres) ? reachMetres : double.MaxValue;

    private static double HopOnto(in Target target, in Bus bus)
    {
        double asked = double.IsFinite(target.HopMetresPerSecond)
                           ? target.HopMetresPerSecond
                           : bus.HopMetresPerSecond;

        return double.IsFinite(asked) ? Math.Max(0.0, asked) : 0.0;
    }

    private static bool Within(double spent, double budget)
        => !double.IsFinite(budget) || spent <= budget;
}
