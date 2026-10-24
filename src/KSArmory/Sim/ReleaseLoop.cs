namespace KSArmory;

/// <summary>Why a bus is releasing everything at one place rather than walking an itinerary.</summary>
internal enum ReleaseWalkHold
{
    /// <summary>It is walking one, and <see cref="ReleaseWalk.Itinerary"/> says where.</summary>
    Walking,

    /// <summary>
    /// One stop. The loop does not run and nothing about it is reachable — see <see cref="ReleaseLoop.Plan"/>.
    /// </summary>
    OneStop,

    /// <summary>No warhead is assigned anywhere, so there is no stop to fly to.</summary>
    NoStops,

    /// <summary>
    /// The first stop is not the place the bus is flying to.
    ///
    /// <para><b>Unreachable from a click, and kept as a guard.</b> <see cref="TargetSet.ElectFarthestLead"/>
    /// aims the booster at the itinerary's first stop; from anywhere else the walk would cost about
    /// twice what it is priced at. <c>docs/MIRV-TARGETS.md</c>.</para>
    /// </summary>
    NotWhereTheBusIsAimed,

    /// <summary>
    /// Some hop costs more than one trim pass will fly.
    ///
    /// <para><b>Unreachable from a click, and kept as a guard</b>: <see cref="ReachDisplay"/> draws its
    /// ring around the stop the next hop leaves from, which is what the itinerary charges.</para>
    /// </summary>
    HopBeyondOnePass,
}

/// <summary>Whether the bus can fly the next hop, and which limit refuses it.</summary>
internal enum HopHold
{
    /// <summary>One pass will fly it and the budget can pay for it.</summary>
    WillFly,

    /// <summary>More than one trim pass will spend, which the trim refuses whole.</summary>
    BeyondOnePass,

    /// <summary>
    /// The flight's whole trim budget will not cover it.
    ///
    /// <para>Distinct from <see cref="BeyondOnePass"/> because nothing downstream refuses it: the
    /// trim ends, the correction finishes on the same reading, and the release happens with no
    /// divert flown at all.</para>
    /// </summary>
    BeyondTheBudget,
}

/// <summary>A walk planned, with everything that was refused on the way to it.</summary>
/// <param name="Dropped">
/// Stops the budget or the coast would not reach, taken off the plan before it was flown rather
/// than discovered at the last one. Their warheads stay aboard, which is what an unassigned warhead
/// already does.
/// </param>
internal readonly record struct ReleaseWalk(ReleaseItinerary Itinerary, ReleaseWalkHold Hold,
                                            int Dropped, double HopMetresPerSecond)
{
    /// <remarks>
    /// The stop count is tested because <see cref="ReleaseWalkHold.Walking"/> is the enum's zero, so a
    /// <c>default</c> walk would otherwise claim to be one.
    /// </remarks>
    public bool Walks => Hold == ReleaseWalkHold.Walking && Stops > 1;

    public int Stops => Itinerary.Count;

    /// <summary>
    /// The targets in the order the bus visits them: the walk's stops, or with no walk the lead and then the rest by
    /// index. A target the walk dropped is not in it.
    /// </summary>
    public int[] VisitOrder(int targets, int lead)
    {
        if (Walks)
        {
            int[] walked = new int[Stops];
            for (int k = 0; k < walked.Length; k++) walked[k] = Itinerary.Stops[k].Target;
            return walked;
        }

        if (targets <= 0) return [];

        int first = lead >= 0 && lead < targets ? lead : 0;
        int[] order = new int[targets];
        order[0] = first;
        for (int i = 0, k = 1; i < targets; i++)
        {
            if (i != first) order[k++] = i;
        }

        return order;
    }

    /// <summary>What the panel and the log say about the plan, in one line.</summary>
    public string Say()
        => Hold switch
        {
            ReleaseWalkHold.Walking when !Walks => "no walk planned",

            ReleaseWalkHold.Walking =>
                $"{Stops} stops, {Itinerary.NeedsMetresPerSecond:F1} m/s of "
                + $"{Itinerary.Means.WalkBudgetMetresPerSecond:F0}"
                + (Dropped > 0
                       ? $" -- {Dropped} target{(Dropped == 1 ? "" : "s")} dropped, "
                         + "their warheads stay aboard"
                       : ""),

            ReleaseWalkHold.OneStop => "one target, released as every flight before it",
            ReleaseWalkHold.NoStops => "no warhead is assigned to anywhere",

            ReleaseWalkHold.NotWhereTheBusIsAimed =>
                "the first stop is not where the booster aimed, so the walk would cost about twice "
                + "what it is priced at; every warhead goes to the target the bus is on",

            ReleaseWalkHold.HopBeyondOnePass =>
                $"a hop wants {HopMetresPerSecond:F1} m/s and one pass will fly "
                + $"{BusTrim.MaxMetresPerSecond:F0}; every warhead goes to the target the bus is on",

            _ => "no walk",
        };
}

/// <summary>What one stop of a walk wants of the flight this frame.</summary>
/// <param name="Target">
/// Which entry of the caller's own target list the bus is aimed at now — what moves
/// <see cref="TargetSet.LeadIndex"/>.
/// </param>
/// <param name="ReleaseHere">This stop still owes warheads.</param>
/// <param name="Handover">
/// This stop is done and another follows, so the bus re-aims. Never true on the last stop: there is
/// nothing to hop to, and a hop asked for there spends the tank on nobody.
/// </param>
/// <param name="Finished">Every stop that fits has had its warheads.</param>
internal readonly record struct ReleaseStep(int Target, int Warheads, int Away, bool ReleaseHere,
                                            bool Handover, bool Finished, int NextTarget,
                                            double HopMetresPerSecond, double BeforeArrivalSeconds,
                                            double NextHopMetresPerSecond = 0.0,
                                            double NextPassMetresPerSecond = 0.0)
{
    /// <summary>The line a night is scored off: which warheads went where, and what it cost.</summary>
    public string Say(int stop, int stops, double spentMetresPerSecond, double leftMetresPerSecond)
        => $"stop {stop + 1} of {stops}: target {Target + 1} takes {Warheads} warhead"
           + $"{(Warheads == 1 ? "" : "s")}, released {BeforeArrivalSeconds:F0} s before arrival"
           + (HopMetresPerSecond > 0.0 ? $", hop cost {HopMetresPerSecond:F2} m/s" : ", no hop")
           + $" ({spentMetresPerSecond:F1} m/s spent, {leftMetresPerSecond:F1} left)";
}

/// <summary>
/// Walking a bus between the places its warheads are going: which stop it is on, how many leave
/// there, and when it moves to the next.
///
/// <para><b>The decision half only</b>: <see cref="IcbmProgram.CorrectCoastArc"/>, <see cref="BusTrim"/>,
/// <see cref="PostBoostAim"/> and <see cref="ReleaseSequence"/> act on it.</para>
///
/// <para><b>A set of one never produces a walk</b> (<see cref="ReleaseWalkHold.OneStop"/>), so a
/// single-target flight runs none of this; <c>ReleaseLoopTests.NoSetOfOneEverProducesAWalk</c>.</para>
///
/// <para>A hop is solved to <see cref="IcbmProgram.CommittedArrivalFromNow"/>, the clock
/// <see cref="DivertFootprint.ArrivalClock.Pinned"/> draws the reach on, so the drawn reach and the
/// flown one agree.</para>
///
/// <para>A hop's first pass is flown as a fresh null, so it is bounded by
/// <see cref="BusTrim.MaxMetresPerSecond"/>, or <see cref="HopCeilingFor"/> under
/// <see cref="IcbmConfig.HopsFlyTheirOwnCeiling"/>; the walk is refused up front when a hop is over.</para>
/// </summary>
internal static class ReleaseLoop
{
    /// <summary>
    /// What the tank keeps beyond the trim budget for nulling the split, in metres a second: one null
    /// at the most a pass will fly, which <c>PostBoostAimTests.TheBudgetLeavesEnoughToNullASeparation</c>
    /// holds the budget to.
    /// </summary>
    public const double SplitNullReserveMetresPerSecond = BusTrim.MaxMetresPerSecond;

    /// <summary>
    /// What the budget gains once a walk's first stop has let its warheads go, which is after the split
    /// has been nulled — <see cref="IcbmConfig.SpendTheSplitReserveAfterTheFirstStop"/>.
    /// </summary>
    public static double ReserveReleased(bool spendIt, bool firstStopAway)
        => spendIt && firstStopAway ? SplitNullReserveMetresPerSecond : 0.0;

    /// <summary>
    /// A flight's trim budget with <paramref name="reserveMetresPerSecond"/> added — never to one that is
    /// none (zero) or unlimited (negative), whose meaning a reserve does not change.
    /// </summary>
    public static double BudgetWithReserve(double budgetMetresPerSecond, double reserveMetresPerSecond)
        => budgetMetresPerSecond > 0.0 && double.IsFinite(budgetMetresPerSecond)
           && reserveMetresPerSecond > 0.0 && double.IsFinite(reserveMetresPerSecond)
               ? budgetMetresPerSecond + reserveMetresPerSecond
               : budgetMetresPerSecond;

    /// <summary>
    /// The budget a walk is planned and corrected on under <see cref="IcbmConfig.HopsSizeTheirOwnSlots"/>: the
    /// flight's own, an unlimited one (negative) as no bound at all.
    /// </summary>
    public static double PlanningBudget(double budgetMetresPerSecond)
        => budgetMetresPerSecond < 0.0 ? double.PositiveInfinity : budgetMetresPerSecond;

    /// <summary>
    /// The share of what the tank can still pay that the trim may not plan on: flown hops ran 1.03-1.08x their price
    /// at 12,902 km in the rig.
    /// </summary>
    public const double TankMargin = 0.10;

    /// <summary>
    /// The most the trim may have spent by the end of the flight on what the tank still holds: what it has spent, and
    /// the rest of the tank on the bus's dearest axis less <see cref="TankMargin"/>. NaN where any input is unreadable.
    /// </summary>
    public static double TankCeilingMetresPerSecond(double spentMetresPerSecond, double propellantKg, double massKg,
                                                    double worstAxisExhaustVelocity)
    {
        if (!(propellantKg >= 0.0) || !(massKg > propellantKg) || !(worstAxisExhaustVelocity > 0.0)
            || !double.IsFinite(massKg) || !double.IsFinite(worstAxisExhaustVelocity) || !double.IsFinite(spentMetresPerSecond))
        {
            return double.NaN;
        }

        double payable = worstAxisExhaustVelocity * Math.Log(massKg / (massKg - propellantKg));
        return Math.Max(0.0, spentMetresPerSecond) + (1.0 - TankMargin) * payable;
    }

    /// <summary>A budget held under <paramref name="tankCeiling"/>; an unread tank leaves it as it was.</summary>
    public static double CappedByTheTank(double budgetMetresPerSecond, double tankCeiling)
        => double.IsFinite(tankCeiling) ? Math.Min(PlanningBudget(budgetMetresPerSecond), tankCeiling) : budgetMetresPerSecond;

    /// <summary>
    /// The budget a walk is planned on with the tank read: the first stop's, so that it and the reserve released after
    /// it stay under <paramref name="tankCeiling"/>.
    /// </summary>
    public static double PlanningBudget(double budgetMetresPerSecond, double tankCeiling, double reserveMetresPerSecond)
        => double.IsFinite(tankCeiling)
               ? Math.Max(0.0, Math.Min(PlanningBudget(budgetMetresPerSecond), tankCeiling - Math.Max(0.0, reserveMetresPerSecond)))
               : PlanningBudget(budgetMetresPerSecond);

    /// <summary>
    /// Whether a fresh plan of <paramref name="freshStops"/> may replace the walker's <paramref name="heldStops"/>. Once a
    /// turning bus has split, only one of the same stops, whose first release is the one the split was timed on — or,
    /// under <see cref="IcbmConfig.WalkShrinksAfterTheSplit"/>, one of fewer, whose first release is never earlier.
    /// </summary>
    public static bool MayReplan(bool turnsAtTheSplit, bool didSplit, int freshStops, int heldStops, bool mayShrink)
        => !(turnsAtTheSplit && didSplit) || freshStops == heldStops
           || (mayShrink && freshStops > 0 && freshStops < heldStops);

    /// <summary>The most times one hop is solved again from where its burn left the bus.</summary>
    public const int MaxHopRounds = 4;

    /// <summary>
    /// Whether a hop's trim, done, is solved again from where the bus now is before anything reads a miss
    /// (<see cref="IcbmConfig.HopsResolveAfterTheirBurn"/>): while the round just flown spent more than one pass would,
    /// since the position it drifted off by grows with that burn's length and its velocity change together.
    /// </summary>
    public static bool ResolvesTheHopAgain(int roundsSoFar, double spentThisRoundMetresPerSecond)
        => roundsSoFar < MaxHopRounds && spentThisRoundMetresPerSecond > BusTrim.MaxMetresPerSecond;

    /// <summary>How long a hop's divert burn takes at the bus's trim acceleration, or zero where either is unknown.</summary>
    public static double HopBurnSeconds(double hopMetresPerSecond, double accelerationMetresPerSecond2)
        => hopMetresPerSecond > 0.0 && double.IsFinite(hopMetresPerSecond)
           && accelerationMetresPerSecond2 > 0.0 && double.IsFinite(accelerationMetresPerSecond2)
               ? hopMetresPerSecond / accelerationMetresPerSecond2
               : 0.0;

    /// <param name="targets">
    /// The places, in the caller's own order — <see cref="ReleaseItinerary.Stop.Target"/> indexes
    /// back into this list, so it must be the list <paramref name="lead"/> indexes too. A caller
    /// that drops a target it could not resolve has to drop it from both or the walk aims at the
    /// wrong entry.
    /// </param>
    /// <param name="lead">
    /// Which of them the booster flew to, <see cref="TargetSet.LeadIndex"/>: the only stop that costs nothing.
    /// </param>
    public static ReleaseWalk Plan(IReadOnlyList<ReleaseItinerary.Target>? targets,
                                   in ReleaseItinerary.Bus bus,
                                   IReadOnlyList<double>? reachMetresPerMetrePerSecond, int lead)
    {
        ReleaseItinerary plan = TrimToWhatFits(targets, bus, reachMetresPerMetrePerSecond,
                                               out int dropped);

        if (plan.Count == 0) return new ReleaseWalk(plan, ReleaseWalkHold.NoStops, dropped, 0.0);
        if (plan.Count == 1) return new ReleaseWalk(plan, ReleaseWalkHold.OneStop, dropped, 0.0);

        if (plan.Stops[0].Target != lead)
        {
            return new ReleaseWalk(plan, ReleaseWalkHold.NotWhereTheBusIsAimed, dropped, 0.0);
        }

        double dearest = 0.0;
        for (int k = 0; k < plan.Count; k++)
        {
            dearest = Math.Max(dearest, plan.Stops[k].PassFlies);
        }

        return dearest > bus.PassCeilingMetresPerSecond
                   ? new ReleaseWalk(plan, ReleaseWalkHold.HopBeyondOnePass, dropped, dearest)
                   : new ReleaseWalk(plan, ReleaseWalkHold.Walking, dropped, dearest);
    }

    /// <summary>
    /// When the first release is due, in seconds before arrival — what the coast's release gate has
    /// to become for a walk to have room for all of it.
    /// </summary>
    /// <remarks>
    /// A walk of one is exactly <paramref name="gateSeconds"/>: the schedule counts back from the gate,
    /// so the flight can read this unconditionally without moving the single-target shot.
    /// </remarks>
    public static double FirstBeforeArrivalSeconds(in ReleaseWalk walk, double gateSeconds)
        => walk.Stops > 0 && double.IsFinite(walk.Itinerary.FirstBeforeArrivalSeconds)
               ? walk.Itinerary.FirstBeforeArrivalSeconds
               : gateSeconds;

    /// <summary>One frame of the walk, from the cursor the caller keeps.</summary>
    /// <param name="stop">Which stop the bus is on, counted from zero.</param>
    /// <param name="awayThisStop">
    /// How many warheads have left at this stop; the magazine's own count cannot say which stop spent it.
    /// </param>
    public static ReleaseStep Step(in ReleaseWalk walk, int stop, int awayThisStop)
    {
        int stops = walk.Stops;

        if (stops == 0 || stop < 0 || stop >= stops)
        {
            return new ReleaseStep(-1, 0, 0, false, false, true, -1, 0.0, double.NaN, 0.0);
        }

        ReleaseItinerary.Stop at = walk.Itinerary.Stops[stop];
        int away = Math.Max(0, awayThisStop);
        bool owes = away < at.Warheads;
        bool more = stop + 1 < stops;

        return new ReleaseStep(at.Target, at.Warheads, away, owes, !owes && more, !owes && !more,
                               more ? walk.Itinerary.Stops[stop + 1].Target : -1,
                               at.HopMetresPerSecond, at.BeforeArrivalSeconds,
                               more ? walk.Itinerary.Stops[stop + 1].HopMetresPerSecond : 0.0,
                               more ? walk.Itinerary.Stops[stop + 1].PassFlies : 0.0);
    }

    /// <summary>
    /// What one trim pass has to fly to reach the next stop: the hop, plus whatever the bus still
    /// owes the solution it is on.
    /// </summary>
    /// <remarks>
    /// The trim is handed the hop plus the residual already on the bus, and refuses a whole pass over
    /// its ceiling (docs/ICBM-GUIDANCE.md, "Several targets"). The sum is an upper bound by the triangle
    /// inequality, so it errs toward refusing.
    /// </remarks>
    public static double PassMustFly(double hopMetresPerSecond, double owedMetresPerSecond)
        => Math.Max(0.0, Finite(hopMetresPerSecond)) + Math.Max(0.0, Finite(owedMetresPerSecond));

    /// <summary>
    /// How far a priced hop's pass may run over what it was priced at before the trim refuses it.
    /// The runaway the per-pass ceiling guards against grows tenfold in ten cycles, so a quarter is
    /// still a bound on it.
    /// </summary>
    public const double HopCeilingMargin = 1.25;

    /// <summary>
    /// The ceiling for the first pass after a hand-over, or NaN for <see cref="BusTrim.MaxMetresPerSecond"/>.
    /// </summary>
    /// <remarks>
    /// <b>The pass is a known velocity change between two solutions</b>, priced before the hand-over, so
    /// it is bounded by that price rather than by the constant sized for a decoupler's shove.
    /// <see cref="BusTrim.CeilingFor"/> never lets it below the constant, so a hop priced under it flies
    /// exactly as it does with the setting off.
    /// </remarks>
    public static double HopCeilingFor(bool ownCeiling, double hopMetresPerSecond, double owedMetresPerSecond,
                                       double passMetresPerSecond = double.NaN)
    {
        if (!ownCeiling) return double.NaN;

        double flies = double.IsFinite(passMetresPerSecond) && passMetresPerSecond > 0.0
                           ? PassMustFly(passMetresPerSecond, owedMetresPerSecond)
                           : PassMustFly(hopMetresPerSecond, owedMetresPerSecond);

        return HopCeilingMargin * flies;
    }

    /// <summary>
    /// Whether one pass will fly it — the same comparison <see cref="BusTrim.Update"/> makes, on the
    /// same ceiling.
    /// </summary>
    /// <param name="ceilingMetresPerSecond">
    /// What the loop above the trim allows, as <see cref="PostCutoffSequence.CeilingFor"/> gives it.
    /// </param>
    public static bool OnePassWillFly(double hopMetresPerSecond, double owedMetresPerSecond,
                                      double ceilingMetresPerSecond)
        => PassMustFly(hopMetresPerSecond, owedMetresPerSecond)
           <= BusTrim.CeilingFor(ceilingMetresPerSecond);

    /// <remarks>
    /// Two limits: a pass's ceiling, and the flight's budget, which nothing downstream refuses -- past
    /// it the trim just ends and the release goes undiverted (docs/ICBM-GUIDANCE.md, "Several targets").
    /// The whole pass has to be payable, or the warhead lands between two targets.
    /// </remarks>
    /// <param name="passMetresPerSecond">
    /// What the pass flies for the hop where that is less than its price — <see cref="ReleaseStep.NextPassMetresPerSecond"/>.
    /// NaN judges the pass on the price.
    /// </param>
    public static HopHold CanFlyTheHop(double hopMetresPerSecond, double owedMetresPerSecond,
                                       double ceilingMetresPerSecond, double spentMetresPerSecond,
                                       double budgetMetresPerSecond, double passMetresPerSecond = double.NaN)
    {
        double wants = PassMustFly(hopMetresPerSecond, owedMetresPerSecond);
        double flies = double.IsFinite(passMetresPerSecond) && passMetresPerSecond > 0.0
                           ? PassMustFly(passMetresPerSecond, owedMetresPerSecond)
                           : wants;

        if (flies > BusTrim.CeilingFor(ceilingMetresPerSecond)) return HopHold.BeyondOnePass;

        // A nanometre a second of slack, for ReleaseItinerary's own reason: the affordable spacing
        // is the exact root of this comparison and lands on either side of it by one ulp.
        return double.IsFinite(budgetMetresPerSecond)
               && Math.Max(0.0, Finite(spentMetresPerSecond)) + wants > budgetMetresPerSecond + 1e-9
                   ? HopHold.BeyondTheBudget
                   : HopHold.WillFly;
    }

    // A reading nobody could take is no demand rather than an infinite one: a trim that is not
    // armed owes nothing that can be measured, and refusing every hop on that ends every walk
    // before it starts.
    private static double Finite(double metresPerSecond)
        => double.IsFinite(metresPerSecond) ? metresPerSecond : 0.0;

    /// <summary>
    /// How many of the magazine a stop may have, for the sequencer that counts down to empty.
    /// </summary>
    /// <remarks>
    /// <see cref="ReleaseSequence"/> ends a deployment when the count it is given reaches zero and
    /// latches that, so a stop's quota is handed to it as the whole magazine and the sequencer is
    /// restarted at each handover. Clamped to what is actually loaded: a plan made against six
    /// warheads and flown with five must not wait for a sixth that is not there.
    /// </remarks>
    public static int TubesLeftForStop(in ReleaseStep step, int loaded)
        => Math.Max(0, Math.Min(Math.Max(0, loaded), step.Warheads - step.Away));

    // A re-plan rather than a prefix: dropping a stop moves every release later, so the shorter set
    // may fit a stop the longer one did not. Each pass drops at least one, which bounds the loop.
    private static ReleaseItinerary TrimToWhatFits(IReadOnlyList<ReleaseItinerary.Target>? targets,
                                                   in ReleaseItinerary.Bus bus,
                                                   IReadOnlyList<double>? reach, out int dropped)
    {
        dropped = 0;

        ReleaseItinerary plan = ReleaseItinerary.Plan(targets, bus, reach);
        if (plan.Count == 0) return plan;

        for (int guard = 0; guard <= TargetSet.MaxTargets; guard++)
        {
            int fits = plan.Fits;
            if (fits >= plan.Count) return plan;

            ReleaseItinerary.Target[] kept = new ReleaseItinerary.Target[fits];
            for (int k = 0; k < fits; k++) kept[k] = targets![plan.Stops[k].Target];

            dropped += plan.Count - fits;

            // Re-planned against the same list, which is what keeps Stop.Target meaning what it
            // meant: the kept set is re-indexed here and put back below.
            ReleaseItinerary shorter = ReleaseItinerary.Plan(kept, bus, reach);
            ReleaseItinerary.Stop[] stops = new ReleaseItinerary.Stop[shorter.Count];

            for (int k = 0; k < stops.Length; k++)
            {
                stops[k] = shorter.Stops[k] with { Target = plan.Stops[shorter.Stops[k].Target].Target };
            }

            plan = new ReleaseItinerary(stops, shorter.Means, shorter.WithoutWarheads);
        }

        return plan;
    }
}
