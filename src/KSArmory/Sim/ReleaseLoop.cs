namespace KSArmory;

/// <summary>Why a bus is releasing everything at one place rather than walking an itinerary.</summary>
internal enum ReleaseWalkHold
{
    /// <summary>It is walking one, and <see cref="ReleaseWalk.Itinerary"/> says where.</summary>
    Walking,

    /// <summary>
    /// One stop, which is every flight this mod has measured. The loop does not run and nothing
    /// about it is reachable — see <see cref="ReleaseLoop.Plan"/>.
    /// </summary>
    OneStop,

    /// <summary>No warhead is assigned anywhere, so there is no stop to fly to.</summary>
    NoStops,

    /// <summary>
    /// The first stop is not the place the bus is flying to.
    ///
    /// <para><b>Which is every real set today, and it is the one thing phase 3 cannot decide for
    /// itself.</b> <see cref="ReleaseItinerary"/> orders farthest-reach-first and takes the first
    /// stop to be the one "the bus already arrives on", which needs the booster aimed at the
    /// farthest target; <see cref="TargetEdit.ClickDoes"/> only adds a target during the coast, so
    /// the booster is always aimed at the first chosen and the farthest does not exist when it
    /// flies. Flown as ordered from there the bus walks out to the far end and back — twice the
    /// ground the itinerary charges — so the walk is refused rather than over-promised.
    /// <c>docs/MIRV-TARGETS.md</c> has both ways out.</para>
    /// </summary>
    NotWhereTheBusIsAimed,

    /// <summary>
    /// Some hop costs more than one trim pass will fly.
    ///
    /// <para>A gap between what phase 2 refuses and what phase 3 pays: <see cref="ReachDisplay"/>
    /// bounds each <em>click</em> to one hop from where the warheads land <em>now</em>, and the
    /// itinerary charges the hop between <em>consecutive stops</em> — so two targets on opposite
    /// edges of the ring are twice its radius apart, 20 m/s for a 10 m/s ring, and every click was
    /// accepted.</para>
    /// </summary>
    HopBeyondOnePass,
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
    /// <summary>Whether the flight does anything it has not always done.</summary>
    /// <remarks>
    /// The stop count is part of the test because <see cref="ReleaseWalkHold.Walking"/> is the
    /// enum's zero, so a <c>default</c> walk — what a caller holds before anything has been planned
    /// — would otherwise claim to be one, with no stops to fly.
    /// </remarks>
    public bool Walks => Hold == ReleaseWalkHold.Walking && Stops > 1;

    public int Stops => Itinerary.Count;

    /// <summary>What the panel and the log say about the plan, in one line.</summary>
    public string Say()
        => Hold switch
        {
            ReleaseWalkHold.Walking when !Walks => "no walk planned",

            ReleaseWalkHold.Walking =>
                $"{Stops} stops, {Itinerary.NeedsMetresPerSecond:F1} m/s of "
                + $"{Itinerary.Means.BudgetMetresPerSecond:F0}"
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
                                            double NextHopMetresPerSecond = 0.0)
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
/// <para><b>The decision half only.</b> Nothing here aims a vehicle, solves an arc or fires a
/// thruster — the pieces that do all exist (<see cref="IcbmProgram.CorrectCoastArc"/> re-solves the
/// arc to wherever the aim now is, <see cref="BusTrim"/> nulls onto it, <see cref="PostBoostAim"/>
/// corrects it and <see cref="ReleaseSequence"/> lets the magazine go). What did not exist is the
/// cursor round them, and this is it.</para>
///
/// <para><b>A set of one never produces a walk.</b> <see cref="Plan"/> answers
/// <see cref="ReleaseWalkHold.OneStop"/> for it, which is the whole of what makes a single-target
/// flight unchanged: the caller holds no walk, so no statement of the loop's runs. Every accuracy
/// measurement this mod has is taken against that shot, so it is pinned by test rather than left to
/// inspection — <c>ReleaseLoopTests.NoSetOfOneEverProducesAWalk</c>.</para>
///
/// <para><b>The arrival stays where the burn committed it.</b> A hop is solved pinned, because
/// <see cref="IcbmProgram.ResolveCoastArc"/> solves to <see cref="IcbmProgram.CommittedArrivalFromNow"/>
/// and the coast never reaches the latch — which is also the clock
/// <see cref="DivertFootprint.ArrivalClock.Pinned"/> draws the reach on, so the ground the player is
/// shown and the ground the bus can get to are the same ground.</para>
///
/// <para><b>And nothing here raises a ceiling.</b> A hop is flown as a fresh null with the
/// correction's pass count back at zero, so <see cref="PostCutoffSequence.CeilingFor"/> hands it
/// <see cref="BusTrim.MaxMetresPerSecond"/> — the same 10 m/s <see cref="ReachDisplay"/> draws the
/// ring at. The walk is checked against it and refused when a hop is over, rather than the trim
/// discovering it with the warheads aboard.</para>
/// </summary>
internal static class ReleaseLoop
{
    /// <summary>
    /// Plan a walk, or say why there is not one.
    /// </summary>
    /// <param name="targets">
    /// The places, in the caller's own order — <see cref="ReleaseItinerary.Stop.Target"/> indexes
    /// back into this list, so it must be the list <paramref name="lead"/> indexes too. A caller
    /// that drops a target it could not resolve has to drop it from both or the walk aims at the
    /// wrong entry.
    /// </param>
    /// <param name="lead">
    /// Which of them the booster flew to — <see cref="TargetSet.LeadIndex"/>. The walk has to start
    /// there, because that is the trajectory the bus arrives on and the only stop that costs
    /// nothing.
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
            dearest = Math.Max(dearest, plan.Stops[k].HopMetresPerSecond);
        }

        return dearest > BusTrim.MaxMetresPerSecond
                   ? new ReleaseWalk(plan, ReleaseWalkHold.HopBeyondOnePass, dropped, dearest)
                   : new ReleaseWalk(plan, ReleaseWalkHold.Walking, dropped, dearest);
    }

    /// <summary>
    /// When the first release is due, in seconds before arrival — what the coast's release gate has
    /// to become for a walk to have room for all of it.
    /// </summary>
    /// <remarks>
    /// <b>A walk of one is exactly <paramref name="gateSeconds"/>.</b>
    /// <see cref="ReleaseItinerary.BeforeArrivalSeconds"/> counts back from the gate, so the last
    /// release lands on it whatever the set's size and a single stop is both the first and the last.
    /// That is what lets the flight read this number unconditionally without moving the shot every
    /// measurement on this mod was taken against.
    /// </remarks>
    public static double FirstBeforeArrivalSeconds(in ReleaseWalk walk, double gateSeconds)
        => walk.Stops > 0 && double.IsFinite(walk.Itinerary.FirstBeforeArrivalSeconds)
               ? walk.Itinerary.FirstBeforeArrivalSeconds
               : gateSeconds;

    /// <summary>One frame of the walk, from the cursor the caller keeps.</summary>
    /// <param name="stop">Which stop the bus is on, counted from zero.</param>
    /// <param name="awayThisStop">
    /// How many warheads have left at this stop. Counted per stop rather than over the flight,
    /// because a stop's quota is what ends it and the magazine's own count cannot say which stop
    /// spent it.
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
                               more ? walk.Itinerary.Stops[stop + 1].HopMetresPerSecond : 0.0);
    }

    /// <summary>
    /// What one trim pass has to fly to reach the next stop: the hop, plus whatever the bus still
    /// owes the solution it is on.
    /// </summary>
    /// <remarks>
    /// <para><b>The planner and the trim judged different quantities, and the difference is a
    /// kilometres-scale miss that reads as an arrival.</b> A hop is priced as the velocity change
    /// between two solutions; <see cref="BusTrim"/> is handed the whole difference between the
    /// vehicle's velocity and the new solution's, so a residual left on the bus by the release it
    /// has just made is added to it. Flown: a 9.73 m/s hop against a 10 m/s ceiling reached the trim
    /// as 10.29, which refuses the <em>whole</em> pass — the bus never moved, and three warheads
    /// already assigned to the next target left on the old solution, 4.00 km away.</para>
    ///
    /// <para>A bound rather than the demand itself, by the triangle inequality: it cannot exceed the
    /// two added, and may be as little as their difference. So it refuses some hops the trim would
    /// have flown, which is the direction to be wrong in — over-promising is what puts warheads
    /// somewhere nobody aimed them.</para>
    /// </remarks>
    public static double PassMustFly(double hopMetresPerSecond, double owedMetresPerSecond)
        => Math.Max(0.0, Finite(hopMetresPerSecond)) + Math.Max(0.0, Finite(owedMetresPerSecond));

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

    // The set cut down to what the budget and the coast actually reach. A re-plan rather than a
    // prefix: dropping a stop moves every release later -- the schedule is counted back from the
    // gate over however many stops there are -- so the shorter set has more coast to fit in and may
    // fit a stop the longer one did not. Iterated to a fixed point, bounded by the set's own size
    // because each pass drops at least one.
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
