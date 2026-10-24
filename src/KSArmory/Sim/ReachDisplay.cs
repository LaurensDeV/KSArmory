using Brutal.Numerics;

namespace KSArmory;

/// <summary>Why a bus has a region on the ground to draw, or has not.</summary>
internal enum ReachHold
{
    /// <summary>There is a region, and it is <see cref="ReachDisplay.SemiMajorMetres"/> across.</summary>
    Drawn,

    /// <summary>
    /// The booster has no trajectory to the place it is aimed at, so there is no landing for a
    /// divert reach to be measured around.
    /// </summary>
    NoShot,

    /// <summary>
    /// Before the burn the reach is <see cref="DivertFootprint.TryAtTheEpoch"/>'s, and this gate is outside
    /// the band it was measured over, where the estimate stops being a floor.
    /// </summary>
    EpochUnmeasured,

    /// <summary>The columns did not come down, so nothing prices a divert.</summary>
    Unflown,

    /// <summary>The warheads have gone; there is nothing left to divert.</summary>
    SalvoAway,

    /// <summary>The trim budget the set already needs leaves nothing to move a landing with.</summary>
    Spent,

    /// <summary>A bus goes to at most six places and six are chosen.</summary>
    Full,

    /// <summary>
    /// Nothing is editing the list, so the seven predictor flights are not spent: the reach is priced
    /// only while a coast has designate mode on or a second target placed.
    /// </summary>
    NotAsked,
}

/// <summary>What a click where the cursor is would do, as far as the reach is concerned.</summary>
internal enum ReachVerdict
{
    /// <summary>The click starts the shot over rather than adding to it, so the bus's reach bounds nothing.</summary>
    Designates,

    /// <summary>Inside the region: the click adds a target.</summary>
    Adds,

    /// <summary>Outside it. A hop the trim cannot pay for is not a target.</summary>
    OutsideReach,

    /// <summary>Another world, which is an interplanetary transfer rather than a longer arc.</summary>
    OffBody,

    /// <summary>Six already.</summary>
    Full,

    /// <summary>There is no region to test against, and why is <see cref="ReachDisplay.Hold"/>.</summary>
    Unknown,
}

/// <summary>
/// The bus's reach as something to draw, to refuse a click against and to read off the panel — one
/// answer for all three, so a click refused inside the ring cannot happen.
///
/// <para>Pinned, because the flight latches the arrival at closed-loop handover; before the burn it is
/// <see cref="DivertFootprint.TryAtTheEpoch"/>, after it the flown columns
/// (<see cref="DivertFootprint.FromTheRealState"/>). It bounds an add, never a designation, which is the
/// booster's reach and is not built. <c>docs/ICBM-GUIDANCE.md</c>, "Several targets".</para>
///
/// <para>The ring is centred on the stop the next hop leaves from (<see cref="NextHopAlongMetres"/>), not
/// the landing: the itinerary charges consecutive stops, so a ring round the landing accepts two targets
/// on opposite edges, 20 m/s apart against a 10 m/s ring.</para>
/// </summary>
/// <param name="Footprint">
/// The ellipse per metre a second. Meaningless unless <see cref="Hold"/> is
/// <see cref="ReachHold.Drawn"/>.
/// </param>
/// <param name="LeftMetresPerSecond">
/// What the trim budget still has after the set already chosen is paid for.
/// </param>
/// <param name="HopMetresPerSecond">
/// What <em>one</em> hop may spend, which sizes the ring and bounds the refusal — the lower of what is
/// left and <see cref="ReleaseItinerary.Bus.PassCeilingMetresPerSecond"/>. Sized on the whole budget, the
/// ring would promise ground the release loop cannot get to.
/// </param>
/// <param name="BuysMetres">
/// What the whole budget buys on the ground (<see cref="ReleaseItinerary.LeftBuysMetres"/>): every hop
/// left, where the ring is one. Zero where no warhead is assigned anywhere.
/// </param>
/// <param name="RoomForMore">
/// How many further targets the budget reaches at <paramref name="SpacingMetres"/>, never negative.
/// </param>
/// <param name="SpacingMetres">
/// The closest two targets are allowed to be. <c>docs/ICBM-GUIDANCE.md</c>, "How close two targets may be".
/// </param>
/// <param name="NextHopAlongMetres">
/// Downrange from the landing to the stop the next hop leaves from, where the ring is centred. Zero
/// while the lead is the last stop, which is every set of one.
/// </param>
/// <param name="NextHopCrossMetres"><inheritdoc cref="NextHopAlongMetres"/></param>
/// <param name="NextHopFromTarget">
/// Which of the placed targets that stop is, counted from one as the player sees them, or <c>0</c> for
/// the landing itself.
/// </param>
internal readonly record struct ReachDisplay(ReachHold Hold,
                                             DivertFootprint Footprint,
                                             double LeftMetresPerSecond,
                                             double HopMetresPerSecond,
                                             double BuysMetres,
                                             int Targets,
                                             int RoomForMore,
                                             double SpacingMetres,
                                             double NextHopAlongMetres = 0.0,
                                             double NextHopCrossMetres = 0.0,
                                             int NextHopFromTarget = 0)
{
    /// <param name="AlongMetres">Downrange from where the bus's warheads would land now.</param>
    /// <param name="CrossMetres">Across the track from the same place.</param>
    internal readonly record struct Placed(double AlongMetres, double CrossMetres, int Warheads);

    public static ReachDisplay None(ReachHold hold, int targets)
        => new(hold, default, 0.0, 0.0, 0.0, targets, 0, 0.0);

    /// <param name="footprint">
    /// The pinned ellipse flown from the bus's own state, or null where no set of columns came down.
    /// </param>
    /// <param name="placed">The targets already chosen, in the order the player chose them.</param>
    /// <param name="lead">
    /// Which of <paramref name="placed"/> the booster is flying to: the stop the walk starts from, since
    /// <c>ReleaseLoop</c> refuses a plan whose first stop is not the lead.
    /// </param>
    /// <param name="unpriced">The hold when no footprint came in; only the caller knows which way it asked.</param>
    /// <param name="pricePerSlot">
    /// <see cref="IcbmConfig.PriceEachHopAtItsSlot"/>: each hop, and the ring, at the reach of the slot
    /// it is flown in rather than at the reach the footprint was measured with.
    /// </param>
    /// <param name="chordFromTheGate">
    /// <see cref="IcbmConfig.PriceSlotsOnTheGateChord"/>: each slot at the larger of the two floors
    /// <see cref="ReleaseItinerary.Bus.SlotReachScale"/> knows, rather than the line from the origin alone.
    /// </param>
    /// <param name="priceAtTheTrim">
    /// <see cref="IcbmConfig.PriceHopsAtWhatTheTrimPays"/>: each hop at
    /// <see cref="DivertFootprint.TrimCostMetresPerSecond"/>, and the ring, the refusal and the readouts at
    /// the worst bearing's, so a point the ring takes is one the planner can pay for.
    /// </param>
    /// <param name="engineHops">
    /// <see cref="IcbmConfig.HopsBurnTheEngine"/>: a hop is paid along its length, as one engine pushing along it pays,
    /// rather than <see cref="DivertFootprint.TrimCostMetresPerSecond"/>'s sum over the nozzles' axes.
    /// </param>
    /// <param name="later">
    /// <see cref="IcbmConfig.FreeHopsMeasureTheirSlots"/>: the same reach flown from where the coast will be at later
    /// epochs, falling, which gives each free hop its own <see cref="ReleaseItinerary.Target.Slots"/>. Null, or any
    /// footprint without its axes, leaves every hop on <see cref="ReleaseItinerary.FreeSlotExponent"/>.
    /// </param>
    /// <param name="countArrivalShift">
    /// <see cref="IcbmConfig.FreeHopsCountTheArrivalTheyMove"/>: each free hop carries the arrival it moves, read off the
    /// footprint's axes at its own epoch. The shift shrinks as the time to go does, so this overstates it, and the price,
    /// for every later stop.
    /// </param>
    public static ReachDisplay For(DivertFootprint? footprint, IReadOnlyList<Placed>? placed,
                                   in ReleaseItinerary.Bus means, IcbmPhase phase, bool salvoAway,
                                   int maxTargets, double spacingMetres, int lead,
                                   ReachHold unpriced, bool pricePerSlot = false,
                                   bool priceAtTheTrim = false, bool chordFromTheGate = false,
                                   IReadOnlyList<DivertFootprint>? later = null, bool engineHops = false,
                                   bool countArrivalShift = false)
    {
        int targets = placed?.Count ?? 0;

        if (salvoAway) return None(ReachHold.SalvoAway, targets);
        if (phase == IcbmPhase.NoSolution) return None(ReachHold.NoShot, targets);
        if (footprint is not { } reach) return None(unpriced, targets);

        ReleaseItinerary.Bus bus = pricePerSlot
            ? means with
            {
                ReachMeasuredAtSeconds = reach.FlightSeconds,
                ReachMeasuredMetresPerMetrePerSecond = chordFromTheGate ? reach.SemiMinorMetresPerMetrePerSecond
                                                                        : double.NaN,
            }
            : means;

        // The smaller axis, which pinned is within 3% of the other. One number, which the itinerary
        // scales per slot under pricePerSlot exactly as the ring is, so the two cannot disagree.
        double perMetrePerSecond = reach.SemiMinorMetresPerMetrePerSecond;
        double[] atEachSlot = [perMetrePerSecond];

        double axisFactor = priceAtTheTrim && !engineHops ? reach.WorstAxisFactor() : 1.0;

        Func<DivertFootprint, double, double, double> pays = engineHops
            ? (f, along, cross) => f.LengthCostMetresPerSecond(along, cross)
            : (f, along, cross) => f.TrimCostMetresPerSecond(along, cross);

        // Expressed in straight-line metres, so the itinerary's division by the reach and its per-slot
        // scaling carry over unchanged.
        Func<double, double, double>? hopMetres = priceAtTheTrim
            ? (along, cross) => pays(reach, along, cross) * perMetrePerSecond
            : null;

        Func<double, double, ReleaseItinerary.SlotCurve?>? slots = null;
        if (hopMetres is not null && bus.FreeArrival && later is { Count: > 0 } && reach.Axes.Known
            && later.All(f => f.Axes.Known))
        {
            DivertFootprint[] epochs = [reach, .. later];
            double[] seconds = epochs.Select(f => f.FlightSeconds).ToArray();
            slots = (along, cross) => ReleaseItinerary.SlotCurve.From(
                seconds, epochs.Select(f => pays(f, along, cross)).ToArray());
        }

        Func<double, double, double>? shift = countArrivalShift && bus.FreeArrival && reach.Axes.Known
            ? reach.ArrivalShiftSeconds
            : null;

        Walk walk = Order(placed, lead, hopMetres, slots, shift);
        ReleaseItinerary itinerary = ReleaseItinerary.Plan(walk.Set, bus, atEachSlot);

        // Planned off the same ordered set the ring is drawn from, rather than built again beside
        // it: a walk flown from a second construction can disagree with the picture about which
        // hops are being bought, and the picture is the one a player has used.
        ReleaseWalk flown = ReleaseLoop.Plan(walk.Set, bus, atEachSlot, lead);

        double left = itinerary.LeftMetresPerSecond;
        double hop = Math.Min(left / axisFactor, bus.PassCeilingMetresPerSecond);
        int room = Math.Max(0, ReleaseItinerary.TargetsWithin(spacingMetres, [perMetrePerSecond / axisFactor], bus)
                               - targets);

        // The next add is the new last stop, so the ring is drawn at the reach of that slot.
        // With slots of their own the next stop's gate depends on its hop, so it is taken at the latest it can be.
        double ringScale = !bus.PricesEachSlot ? 1.0
                         : bus.SizesSlots ? bus.SlotReachScale(bus.GateSeconds)
                         : bus.SlotReachScale(bus.GateFor(itinerary.Count + 1));

        ReachDisplay display = new(ReachHold.Drawn, reach, left, hop, itinerary.LeftBuysMetres / axisFactor,
                                   targets, room, spacingMetres,
                                   walk.AlongMetres, walk.CrossMetres, walk.FromTarget)
        {
            Flown = flown,
            SlotReachScale = ringScale,
            AxisFactor = axisFactor,
        };

        if (targets >= maxTargets) return display with { Hold = ReachHold.Full };
        if (!(hop > 0.0)) return display with { Hold = ReachHold.Spent };

        return display;
    }

    /// <summary>
    /// The walk the flight flies, planned off the same ordered set the ring is drawn from. Empty for
    /// every <see cref="None"/>, so a caller flying one latches it: once the first warhead is away the
    /// display is no longer priced.
    /// </summary>
    public ReleaseWalk Flown { get; init; }

    /// <summary>
    /// The share of <see cref="Footprint"/> the next hop is drawn and refused at — one unless
    /// <see cref="IcbmConfig.PriceEachHopAtItsSlot"/> prices it at a later slot than the footprint was
    /// measured at.
    /// </summary>
    public double SlotReachScale { get; init; }

    /// <summary>
    /// What the budget left is divided by before it sizes the ring, for the trim paying a divert axis by axis —
    /// one unless <see cref="IcbmConfig.PriceHopsAtWhatTheTrimPays"/>. One pass's own ceiling is a length and is
    /// not divided.
    /// </summary>
    public double AxisFactor { get; init; } = 1.0;

    private double RingScale => SlotReachScale is > 0.0 and <= 1.0 ? SlotReachScale : 1.0;

    public bool HasRegion => Hold == ReachHold.Drawn;

    /// <summary>
    /// Whether the reach was flown, which a full set and a spent budget still are: the walk is planned
    /// off those offsets, so a set that fills the bus must be measurable though no ring is drawn.
    /// </summary>
    public bool HasFootprint => Hold is ReachHold.Drawn or ReachHold.Full or ReachHold.Spent;

    public bool CentredOnTheLanding => NextHopAlongMetres == 0.0 && NextHopCrossMetres == 0.0;

    public double SemiMajorMetres
        => HasRegion ? Footprint.SemiMajorMetresPerMetrePerSecond * HopMetresPerSecond * RingScale : 0.0;

    public double SemiMinorMetres
        => HasRegion ? Footprint.SemiMinorMetresPerMetrePerSecond * HopMetresPerSecond * RingScale : 0.0;

    /// <param name="click">
    /// What the list and the phase already say the click is; a designation is let through whatever the
    /// bus's reach says.
    /// </param>
    public ReachVerdict Verdict(TargetClick click, bool onTheBody, double alongMetres,
                                double crossMetres)
    {
        if (!onTheBody) return ReachVerdict.OffBody;
        if (click == TargetClick.Designate) return ReachVerdict.Designates;
        if (Hold == ReachHold.Full) return ReachVerdict.Full;
        if (!HasRegion) return ReachVerdict.Unknown;

        // An offset the world could not resolve is not a point inside the region. Zero is the
        // centre of it, so a caller that could not answer must say so rather than pass one.
        if (!double.IsFinite(alongMetres) || !double.IsFinite(crossMetres)) return ReachVerdict.Unknown;

        // From the stop the hop leaves from, against one hop's ceiling: what the ring is drawn at.
        return Footprint.Reaches(alongMetres - NextHopAlongMetres, crossMetres - NextHopCrossMetres,
                                 HopMetresPerSecond * RingScale)
                   ? ReachVerdict.Adds
                   : ReachVerdict.OutsideReach;
    }

    /// <summary>
    /// The ring's two semi-axes in the frame the landing is read in now. The columns' frame stands where
    /// the ground will be at arrival, so they are carried back a flight: half an hour of Earth's spin is 830 km.
    /// </summary>
    public bool TryAxes(BallisticBody body, out double3 majorCci, out double3 minorCci)
    {
        majorCci = Vec.Zero;
        minorCci = Vec.Zero;
        if (!HasRegion) return false;

        double cos = Math.Cos(Footprint.OrientationRad), sin = Math.Sin(Footprint.OrientationRad);

        double3 major = Footprint.Frame.Downrange * cos + Footprint.Frame.Cross * sin;
        double3 minor = Footprint.Frame.Cross * cos - Footprint.Frame.Downrange * sin;

        majorCci = body.CarryCci(major, -Footprint.FlightSeconds) * SemiMajorMetres;
        minorCci = body.CarryCci(minor, -Footprint.FlightSeconds) * SemiMinorMetres;

        return Vec.IsFinite(majorCci) && Vec.IsFinite(minorCci);
    }

    /// <summary>
    /// Where the ring's centre sits, as a ground displacement from the landing — the exact inverse
    /// of <see cref="TryOffsets"/>, so the ring is drawn around the point a click is measured from.
    /// </summary>
    public bool TryCentreOffset(BallisticBody body, out double3 offsetCci)
    {
        offsetCci = Vec.Zero;
        if (!HasRegion) return false;

        double3 carried = (Footprint.Frame.Downrange * NextHopAlongMetres)
                          + (Footprint.Frame.Cross * NextHopCrossMetres);

        offsetCci = body.CarryCci(carried, -Footprint.FlightSeconds);

        return Vec.IsFinite(offsetCci);
    }

    /// <param name="offsetCci">
    /// The separation between two ground-fixed points, both read in the frame of the instant the
    /// columns were flown from — never two inertial positions, which differ by the planet's travel.
    /// </param>
    public bool TryOffsets(BallisticBody body, double3 offsetCci, out double alongMetres,
                           out double crossMetres)
    {
        alongMetres = 0.0;
        crossMetres = 0.0;
        if (!HasFootprint || !Vec.IsFinite(offsetCci)) return false;

        double3 resolved = Footprint.Frame.Resolve(body.CarryCci(offsetCci, Footprint.FlightSeconds));

        alongMetres = resolved.Y;
        crossMetres = resolved.Z;

        return double.IsFinite(alongMetres) && double.IsFinite(crossMetres);
    }

    public string Say()
    {
        if (!HasRegion)
        {
            return Hold switch
            {
                ReachHold.NoShot => "Divert reach: no trajectory reaches the place it is aimed at, so there "
                                    + "is no landing to divert from",
                ReachHold.EpochUnmeasured =>
                    "Divert reach: not known before the burn -- the release is set outside "
                    + $"{DivertFootprint.EpochLeastSeconds:F0} to {DivertFootprint.EpochMostSeconds:F0} s "
                    + "before arrival, which is the band the estimate was measured over",
                ReachHold.Unflown => "Divert reach: not known -- no trajectory off this state comes down",
                ReachHold.SalvoAway => "Divert reach: the warheads have gone",
                ReachHold.Spent => $"Divert reach: none left -- the {Targets} chosen need the whole trim budget",
                ReachHold.Full => $"Divert reach: {Targets} targets already, which is all a bus goes to",
                ReachHold.NotAsked => "Divert reach: not priced -- turn on Designate by clicking the world "
                                      + "to place another target",
                _ => "Divert reach: none",
            };
        }

        string more = RoomForMore == 0
                          ? $"no room for another {Distance.Say(SpacingMetres)} away"
                          : $"room for {RoomForMore} more at {Distance.Say(SpacingMetres)} apart";

        string from = CentredOnTheLanding ? "from where the warheads land"
                                          : $"from target {NextHopFromTarget}";

        return $"Divert reach: {Distance.Say(SemiMinorMetres)} {from} -- {more}";
    }

    public string SayBudget()
    {
        if (!HasRegion) return "";

        string buys = BuysMetres > 0.0
                          ? $", which moves a landing {Distance.Say(BuysMetres)} in all"
                          : ", and no warhead is assigned anywhere yet";

        string how = Footprint.FromTheRealState
                         ? ""
                         : " -- estimated from the release epoch until the burn is over, and how many "
                           + "of them the coast fits is known then too";

        return $"{LeftMetresPerSecond:F1} m/s of trim left{buys}; one hop may spend "
               + $"{HopMetresPerSecond:F1}{how}";
    }

    public static string CursorSays(ReachVerdict verdict, int targets = 0)
        => verdict switch
        {
            ReachVerdict.OutsideReach => "outside reach",
            ReachVerdict.OffBody => "another world",
            ReachVerdict.Full => "six targets already",
            ReachVerdict.Unknown => "reach not known",

            // A click adds only while the reach is known, so the same gesture can silently replace the set.
            ReachVerdict.Designates when targets > 0 =>
                $"replaces {targets} target{(targets == 1 ? "" : "s")}",

            _ => "",
        };

    public static bool Takes(ReachVerdict verdict)
        => verdict is ReachVerdict.Designates or ReachVerdict.Adds;

    /// <param name="Set">
    /// One entry per place given, <b>in the caller's own order</b> — so
    /// <see cref="ReleaseItinerary.Stop.Target"/> indexes back into the same list the lead does,
    /// which is what <c>ReleaseLoop</c> requires of a plan it is asked to fly.
    /// </param>
    /// <param name="AlongMetres">Where the last stop leaves the bus, as a ground offset from the landing.</param>
    /// <param name="CrossMetres"><inheritdoc cref="AlongMetres"/></param>
    /// <param name="FromTarget">Which place that is, counted from one.</param>
    internal readonly record struct Walk(ReleaseItinerary.Target[] Set, double AlongMetres,
                                         double CrossMetres, int FromTarget);

    /// <summary>
    /// The set as the itinerary wants it: the lead first, then each remaining target nearest to the
    /// stop before it, each hop the ground between the two.
    /// </summary>
    /// <remarks>
    /// <para>The lead first: the bus arrives on its trajectory, so that stop costs nothing, and
    /// <see cref="TargetSet.ElectFarthestLead"/> puts it at an end of the set, so nearest-first walks inward.
    /// A target with no warheads is not a stop, but the lead's place is still where the walk starts.</para>
    ///
    /// <para><c>Plan</c> re-sorts on <see cref="ReleaseItinerary.Target.ReachMetres"/>, so the rank
    /// is emitted descending to reproduce exactly this order.</para>
    /// </remarks>
    /// <param name="hopMetres">
    /// What a hop of this many metres along and across is charged as, in straight-line metres; null charges its
    /// length.
    /// </param>
    /// <param name="slots">A hop's own <see cref="ReleaseItinerary.Target.Slots"/>; null leaves it unset.</param>
    public static Walk Order(IReadOnlyList<Placed>? placed, int lead,
                             Func<double, double, double>? hopMetres = null,
                             Func<double, double, ReleaseItinerary.SlotCurve?>? slots = null,
                             Func<double, double, double>? shift = null)
    {
        if (placed is null || placed.Count == 0) return new Walk([], 0.0, 0.0, 0);

        int first = lead >= 0 && lead < placed.Count ? lead : 0;

        List<int> order = [];
        if (placed[first].Warheads > 0) order.Add(first);

        List<int> rest = [];
        for (int i = 0; i < placed.Count; i++)
        {
            if (i != first && placed[i].Warheads > 0) rest.Add(i);
        }

        // Greedy nearest-first rather than the cheapest of 720 orders, since this runs every frame for
        // every rocket; exact for a chain walked from an end. Ties keep the caller's order.
        double walkAlong = Finite(placed[first].AlongMetres);
        double walkCross = Finite(placed[first].CrossMetres);

        while (rest.Count > 0)
        {
            int nearest = 0;
            double gap = double.PositiveInfinity;

            for (int k = 0; k < rest.Count; k++)
            {
                double d = Gap(placed[rest[k]], walkAlong, walkCross);
                if (d >= gap) continue;

                gap = d;
                nearest = k;
            }

            int pick = rest[nearest];
            rest.RemoveAt(nearest);
            order.Add(pick);

            walkAlong = Finite(placed[pick].AlongMetres);
            walkCross = Finite(placed[pick].CrossMetres);
        }

        double atAlong = Finite(placed[first].AlongMetres);
        double atCross = Finite(placed[first].CrossMetres);
        int atTarget = first + 1;

        // A place the bus never stops at keeps rank zero, which Plan drops before reading its hop.
        ReleaseItinerary.Target[] set = new ReleaseItinerary.Target[placed.Count];
        for (int i = 0; i < set.Length; i++) set[i] = new ReleaseItinerary.Target(0, 0.0, 0.0);

        for (int k = 0; k < order.Count; k++)
        {
            Placed to = placed[order[k]];
            double along = Finite(to.AlongMetres), cross = Finite(to.CrossMetres);

            double length = Math.Sqrt(((along - atAlong) * (along - atAlong))
                                      + ((cross - atCross) * (cross - atCross)));

            set[order[k]] = hopMetres is null
                ? new ReleaseItinerary.Target(to.Warheads, order.Count - k, length)
                : new ReleaseItinerary.Target(to.Warheads, order.Count - k, hopMetres(along - atAlong, cross - atCross),
                                              HopLengthMetres: length,
                                              Slots: slots?.Invoke(along - atAlong, cross - atCross),
                                              ArrivalShiftSeconds: shift?.Invoke(along - atAlong, cross - atCross) ?? 0.0);

            atAlong = along;
            atCross = cross;
            atTarget = order[k] + 1;
        }

        return new Walk(set, atAlong, atCross, atTarget);
    }

    // NaN would make the ordering inconsistent, and Array.Sort may throw on that inside the frame hook.
    private static double Finite(double metres) => double.IsFinite(metres) ? metres : 0.0;

    private static double Gap(in Placed to, double fromAlong, double fromCross)
    {
        double along = Finite(to.AlongMetres) - fromAlong;
        double cross = Finite(to.CrossMetres) - fromCross;

        return Math.Sqrt((along * along) + (cross * cross));
    }
}
