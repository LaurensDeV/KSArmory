using Brutal.Numerics;

namespace KSArmory;

/// <summary>Why a bus has a region on the ground to draw, or has not.</summary>
internal enum ReachHold
{
    /// <summary>There is a region, and it is <see cref="ReachDisplay.SemiMajorMetres"/> across.</summary>
    Drawn,

    /// <summary>
    /// The booster has no trajectory to the place it is aimed at, so there is no landing for a
    /// divert reach to be measured around. The bus's reach is the wrong question here: the
    /// booster's is.
    /// </summary>
    NoShot,

    /// <summary>
    /// Before the burn is over the reach is the release epoch's and nothing else
    /// (<see cref="DivertFootprint.TryAtTheEpoch"/>), and this gate is outside the band that was
    /// measured — <see cref="DivertFootprint.EpochLeastSeconds"/> to
    /// <see cref="DivertFootprint.EpochMostSeconds"/>. Outside it the estimate stops being a floor.
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
    /// Nothing is editing the list, so nothing is flown. Seven flights of the predictor every few
    /// seconds is not a thing to spend on a rocket nobody is clicking at, so the reach is priced
    /// while a coast has designate mode on or a second target already placed — and this is the only
    /// hold with a way out the player can take.
    /// </summary>
    NotAsked,
}

/// <summary>What a click where the cursor is would do, as far as the reach is concerned.</summary>
internal enum ReachVerdict
{
    /// <summary>
    /// The click starts the shot over rather than adding to it, so the reach bounds nothing. The
    /// booster's own reach is a different and far dearer question and is not asked here.
    /// </summary>
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
/// The bus's reach as something to draw, to refuse a click against and to read off the panel.
///
/// <para>One place decides all three, because they are one question asked by three surfaces and
/// they must not disagree: a cursor refused where the ring says there is room reads as the tool
/// being broken. <see cref="DivertFootprint"/> is the ellipse per metre a second and
/// <see cref="ReleaseItinerary"/> is what is left to spend; this is the two multiplied together
/// and everything that follows from the product.</para>
///
/// <para><b>Pinned, and there is no choice about it.</b> The flight latches the arrival at
/// closed-loop handover, so <see cref="DivertFootprint.ArrivalClock.Free"/> would draw a region 2
/// to 12 times too long along the track. <c>docs/MIRV-TARGETS.md</c>.</para>
///
/// <para><b>And pinned, it can be drawn before the burn as well as after it.</b> Free-clock the long
/// axis is <c>450 · cot γ</c> and belongs to the arc actually flown; pinned both axes collapse onto
/// the cross-track reach, which is the release epoch alone. So a flight that has not flown gets
/// <see cref="DivertFootprint.TryAtTheEpoch"/> and a coasting bus gets the columns it has actually
/// flown — one region, two ways of knowing it, and <see cref="DivertFootprint.FromTheRealState"/>
/// says which.</para>
///
/// <para><b>The reach bounds an <em>add</em>, never a designation.</b> Target 1 is the missile's
/// reach — a trajectory search per candidate point, which is <c>IcbmReach</c> asked along bearings
/// and is not built — so a click that starts the shot over is let through and only a click that
/// adds to the set is tested.</para>
///
/// <para><b>The ring is around the stop the next hop leaves from, not around the landing.</b> The
/// itinerary charges a hop between <em>consecutive</em> stops, so a ring around the landing accepts
/// two targets on opposite edges of it — twice its radius apart, 20 m/s against a 10 m/s ring — and
/// the release loop then refuses the pair it was told it could have. <see cref="NextHopAlongMetres"/>
/// is the centre, and it is the landing only while the lead is the last stop.</para>
/// </summary>
/// <param name="Footprint">
/// The ellipse per metre a second. Meaningless unless <see cref="Hold"/> is
/// <see cref="ReachHold.Drawn"/>.
/// </param>
/// <param name="LeftMetresPerSecond">
/// What the trim budget still has after the set already chosen is paid for.
/// </param>
/// <param name="HopMetresPerSecond">
/// What <em>one</em> hop may spend, which is what sizes the ring and bounds the refusal — the lower
/// of what is left and <see cref="BusTrim.MaxMetresPerSecond"/>, the most one trim solve will fly.
/// <b>Drawing the budget's whole reach and shrinking it when phase 3 raises that ceiling is the
/// wrong order</b>: it would promise ground the release loop cannot get to.
/// </param>
/// <param name="BuysMetres">
/// What the whole budget buys on the ground, read off <see cref="ReleaseItinerary.LeftBuysMetres"/>
/// at the last stop's reach. Zero where no warhead is assigned anywhere, because then there are no
/// stops. Larger than the ring, and the two answer different questions: the ring is one hop, this is
/// every hop that is left.
/// </param>
/// <param name="RoomForMore">
/// How many further targets the budget reaches at <paramref name="SpacingMetres"/>, never negative.
/// </param>
/// <param name="SpacingMetres">
/// The closest two targets are allowed to be, which is the warhead's lethal radius — overlapping
/// blast is the player's business and overlapping kill is not. <c>docs/MIRV-TARGETS.md</c>.
/// </param>
/// <param name="NextHopAlongMetres">
/// Downrange from the landing to the stop the next hop leaves from, which is where the ring is
/// centred and what a click is measured against. Zero while the lead is the last stop, which is
/// every set of one.
/// </param>
/// <param name="NextHopCrossMetres"><inheritdoc cref="NextHopAlongMetres"/></param>
/// <param name="NextHopFromTarget">
/// Which of the placed targets that stop is, counted as the player sees them, or <c>0</c> for the
/// landing itself — so the panel can name it rather than saying "from where the warheads land"
/// about a point several kilometres from there.
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
    /// <summary>One target as the ground sees it: where it sits, and how many warheads it takes.</summary>
    /// <param name="AlongMetres">Downrange from where the bus's warheads would land now.</param>
    /// <param name="CrossMetres">Across the track from the same place.</param>
    internal readonly record struct Placed(double AlongMetres, double CrossMetres, int Warheads);

    /// <summary>Nothing to draw, and the reason.</summary>
    public static ReachDisplay None(ReachHold hold, int targets)
        => new(hold, default, 0.0, 0.0, 0.0, targets, 0, 0.0);

    /// <summary>
    /// What the ground, the cursor and the panel are all reading.
    /// </summary>
    /// <param name="footprint">
    /// The pinned ellipse flown from the bus's own state, or null where no set of columns came down.
    /// </param>
    /// <param name="placed">The targets already chosen, in the order the player chose them.</param>
    /// <param name="spacingMetres">
    /// The floor on how close two targets may be, which prices <see cref="RoomForMore"/>.
    /// </param>
    /// <param name="lead">
    /// Which of <paramref name="placed"/> the booster is flying to. It is the stop the walk starts
    /// from and the only one that costs no hop, so the itinerary is ordered around it rather than
    /// around the landing — <c>ReleaseLoop</c> refuses a plan whose first stop is not the lead.
    /// </param>
    /// <param name="unpriced">
    /// What to say when no footprint came in. The caller knows which of the two ways of knowing one
    /// it was asking for, and they fail for different reasons.
    /// </param>
    public static ReachDisplay For(DivertFootprint? footprint, IReadOnlyList<Placed>? placed,
                                   in ReleaseItinerary.Bus bus, IcbmPhase phase, bool salvoAway,
                                   int maxTargets, double spacingMetres, int lead,
                                   ReachHold unpriced)
    {
        int targets = placed?.Count ?? 0;

        if (salvoAway) return None(ReachHold.SalvoAway, targets);
        if (phase == IcbmPhase.NoSolution) return None(ReachHold.NoShot, targets);
        if (footprint is not { } reach) return None(unpriced, targets);

        // One number for the whole coast rather than one per slot, and the smaller axis of the two.
        // The itinerary then prices every hop at the reach the ring is drawn at, so the readout and
        // the picture cannot disagree -- and pinned the two axes agree to within 3% anyway.
        double perMetrePerSecond = reach.SemiMinorMetresPerMetrePerSecond;
        double[] atEachSlot = [perMetrePerSecond];

        Walk walk = Order(placed, lead);
        ReleaseItinerary itinerary = ReleaseItinerary.Plan(walk.Set, bus, atEachSlot);

        // Planned off the same ordered set the ring is drawn from, rather than built again beside
        // it: a walk flown from a second construction can disagree with the picture about which
        // hops are being bought, and the picture is the one a player has used.
        ReleaseWalk flown = ReleaseLoop.Plan(walk.Set, bus, atEachSlot, lead);

        double left = itinerary.LeftMetresPerSecond;
        double hop = Math.Min(left, BusTrim.MaxMetresPerSecond);
        int room = Math.Max(0, ReleaseItinerary.TargetsWithin(spacingMetres, atEachSlot, bus) - targets);

        ReachDisplay display = new(ReachHold.Drawn, reach, left, hop, itinerary.LeftBuysMetres,
                                   targets, room, spacingMetres,
                                   walk.AlongMetres, walk.CrossMetres, walk.FromTarget)
        {
            Flown = flown,
        };

        if (targets >= maxTargets) return display with { Hold = ReachHold.Full };
        if (!(hop > 0.0)) return display with { Hold = ReachHold.Spent };

        return display;
    }

    /// <summary>
    /// The walk this set amounts to — what the flight actually flies, planned off the same ordered
    /// set the ring is drawn from.
    ///
    /// <para>Holds no stops where nothing was priced, which is every <see cref="None"/>. A caller
    /// that is flying one has to latch it rather than re-reading it: once the first warhead is away
    /// the display stops being drawn, and the plan behind the bus must not change anyway.</para>
    /// </summary>
    public ReleaseWalk Flown { get; init; }

    /// <summary>Whether there is a region on the ground at all.</summary>
    public bool HasRegion => Hold == ReachHold.Drawn;

    /// <summary>
    /// Whether the ring sits on the landing, which it does while the lead is the only stop the bus
    /// makes — every set of one, whichever entry the lead is.
    /// </summary>
    public bool CentredOnTheLanding => NextHopAlongMetres == 0.0 && NextHopCrossMetres == 0.0;

    /// <summary>How far the ring reaches along its long axis, in metres.</summary>
    public double SemiMajorMetres
        => HasRegion ? Footprint.SemiMajorMetresPerMetrePerSecond * HopMetresPerSecond : 0.0;

    /// <summary>The same across it.</summary>
    public double SemiMinorMetres
        => HasRegion ? Footprint.SemiMinorMetresPerMetrePerSecond * HopMetresPerSecond : 0.0;

    /// <summary>
    /// What a click at a ground offset from the landing would do.
    /// </summary>
    /// <param name="click">
    /// What the list and the phase already say the click is. A designation is let through whatever
    /// the reach says, because the reach being drawn is about the bus and a designation is about the
    /// booster.
    /// </param>
    /// <param name="onTheBody">Whether the point is on the world the arc is flown around.</param>
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

        // Measured from the stop the hop LEAVES FROM, which is the whole of what the itinerary
        // charges. Measured from the landing instead, two targets on opposite edges of the ring are
        // each accepted and the pair then costs twice the ring -- 20 m/s against a 10 m/s ring.
        //
        // Against one hop's ceiling, which is what the ring is drawn at: a refusal measured on the
        // whole budget would refuse ground inside the outline and take clicks outside it.
        return Footprint.Reaches(alongMetres - NextHopAlongMetres, crossMetres - NextHopCrossMetres,
                                 HopMetresPerSecond)
                   ? ReachVerdict.Adds
                   : ReachVerdict.OutsideReach;
    }

    /// <summary>
    /// The ring's two semi-axes, as displacements in the body-fixed frame the landing is read in.
    /// </summary>
    /// <remarks>
    /// The columns are carried to the arrival, so the frame they are resolved in stands where the
    /// ground will be a whole flight from now. Drawn at the landing as it is <em>now</em> they have
    /// to come back: half an hour of a planet's spin is 830 km, which would lay the ring on
    /// somebody else's continent.
    /// </remarks>
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

    /// <summary>
    /// A ground offset from the landing, as the along and across the ellipse is measured in.
    /// </summary>
    /// <param name="offsetCci">
    /// The separation between two ground-fixed points, both read in the frame of the instant the
    /// columns were flown from — never two inertial positions, which differ by the planet's travel.
    /// </param>
    public bool TryOffsets(BallisticBody body, double3 offsetCci, out double alongMetres,
                           out double crossMetres)
    {
        alongMetres = 0.0;
        crossMetres = 0.0;
        if (!HasRegion || !Vec.IsFinite(offsetCci)) return false;

        double3 resolved = Footprint.Frame.Resolve(body.CarryCci(offsetCci, Footprint.FlightSeconds));

        alongMetres = resolved.Y;
        crossMetres = resolved.Z;

        return double.IsFinite(alongMetres) && double.IsFinite(crossMetres);
    }

    /// <summary>What the panel says about the reach, in one line.</summary>
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

        // Named, because the ring is around the last stop rather than around the landing as soon as
        // there is more than one: "from where the warheads land" about a ring several kilometres
        // from there is the readout and the picture disagreeing.
        string from = CentredOnTheLanding ? "from where the warheads land"
                                          : $"from target {NextHopFromTarget}";

        return $"Divert reach: {Distance.Say(SemiMinorMetres)} {from} -- {more}";
    }

    /// <summary>What it is spending, on the line under <see cref="Say"/>.</summary>
    public string SayBudget()
    {
        if (!HasRegion) return "";

        string buys = BuysMetres > 0.0
                          ? $", which moves a landing {Distance.Say(BuysMetres)} in all"
                          : ", and no warhead is assigned anywhere yet";

        // An estimate says so. Before the burn the reach is the release epoch's alone and the coast
        // is not known at all, so how many of the set actually fit is settled at cutoff and not here.
        string how = Footprint.FromTheRealState
                         ? ""
                         : " -- estimated from the release epoch until the burn is over, and how many "
                           + "of them the coast fits is known then too";

        return $"{LeftMetresPerSecond:F1} m/s of trim left{buys}; one hop may spend "
               + $"{HopMetresPerSecond:F1}{how}";
    }

    /// <summary>What to print beside the cursor, or empty where the click needs no explaining.</summary>
    public static string CursorSays(ReachVerdict verdict)
        => verdict switch
        {
            ReachVerdict.OutsideReach => "outside reach",
            ReachVerdict.OffBody => "another world",
            ReachVerdict.Full => "six targets already",
            ReachVerdict.Unknown => "reach not known",
            _ => "",
        };

    /// <summary>Whether a click here would be taken.</summary>
    public static bool Takes(ReachVerdict verdict)
        => verdict is ReachVerdict.Designates or ReachVerdict.Adds;

    /// <summary>The walk a set amounts to, and where its last stop leaves the bus.</summary>
    /// <param name="Set">
    /// One entry per place given, <b>in the caller's own order</b> — so
    /// <see cref="ReleaseItinerary.Stop.Target"/> indexes back into the same list the lead does,
    /// which is what <c>ReleaseLoop</c> requires of a plan it is asked to fly.
    /// </param>
    /// <param name="AlongMetres">Where the walk leaves the bus, as a ground offset from the landing.</param>
    /// <param name="CrossMetres"><inheritdoc cref="AlongMetres"/></param>
    /// <param name="FromTarget">Which place that is, counted as the player sees them.</param>
    internal readonly record struct Walk(ReleaseItinerary.Target[] Set, double AlongMetres,
                                         double CrossMetres, int FromTarget);

    /// <summary>
    /// The set as the itinerary wants it: the lead first, then the rest in the order they were
    /// chosen, each hop the ground between it and the stop before.
    /// </summary>
    /// <remarks>
    /// <para><b>The lead first, not the farthest from the landing.</b> The bus arrives on the lead's
    /// trajectory, so that stop costs nothing and every other hop is measured from it — and
    /// <c>ReleaseLoop</c> refuses outright a plan whose first stop is not the lead, because flown as
    /// ordered from anywhere else the bus walks out to the far end and back, twice the ground the
    /// itinerary charges. Farthest first is still the rule the <em>set</em> obeys:
    /// <see cref="TargetSet.ElectFarthestLead"/> puts the lead at an end of it, so walking it in the
    /// chosen order walks inward.</para>
    ///
    /// <para><b>The order the panel, the cursor and the flight all read.</b> Phase 3 plans its walk
    /// from this rather than from a second construction, or the ring drawn and the walk flown
    /// disagree about which hops are being bought.</para>
    ///
    /// <para>A target holding no warheads is not a stop — <see cref="ReleaseItinerary.Plan"/> drops
    /// it — but the <em>lead's</em> position is still where the walk starts even when it takes none,
    /// because the bus arrives there whatever it drops.</para>
    ///
    /// <para><c>Plan</c> re-sorts on <see cref="ReleaseItinerary.Target.ReachMetres"/>, so the rank
    /// is emitted descending to reproduce exactly this order.</para>
    /// </remarks>
    public static Walk Order(IReadOnlyList<Placed>? placed, int lead)
    {
        if (placed is null || placed.Count == 0) return new Walk([], 0.0, 0.0, 0);

        int first = lead >= 0 && lead < placed.Count ? lead : 0;

        List<int> order = [];
        if (placed[first].Warheads > 0) order.Add(first);

        for (int i = 0; i < placed.Count; i++)
        {
            if (i != first && placed[i].Warheads > 0) order.Add(i);
        }

        // Where the bus is before any hop is bought. The lead's place even when nothing leaves
        // there, because that is the trajectory the booster flew.
        double atAlong = Finite(placed[first].AlongMetres);
        double atCross = Finite(placed[first].CrossMetres);
        int atTarget = first + 1;

        // One entry per place, so a stop names the caller's own index. A place the bus never stops
        // at keeps rank zero and whatever hop, both of which Plan drops before reading either.
        ReleaseItinerary.Target[] set = new ReleaseItinerary.Target[placed.Count];
        for (int i = 0; i < set.Length; i++) set[i] = new ReleaseItinerary.Target(0, 0.0, 0.0);

        for (int k = 0; k < order.Count; k++)
        {
            Placed to = placed[order[k]];
            double along = Finite(to.AlongMetres), cross = Finite(to.CrossMetres);

            double hop = Math.Sqrt(((along - atAlong) * (along - atAlong))
                                   + ((cross - atCross) * (cross - atCross)));

            set[order[k]] = new ReleaseItinerary.Target(to.Warheads, order.Count - k, hop);

            atAlong = along;
            atCross = cross;
            atTarget = order[k] + 1;
        }

        return new Walk(set, atAlong, atCross, atTarget);
    }

    // A place the world could not resolve is taken as the landing rather than carried as NaN: NaN
    // compares equal to nothing, which makes a hop's price meaningless and an ordering inconsistent
    // -- and Array.Sort is entitled to throw on that, inside the frame hook where an exception is
    // the game.
    private static double Finite(double metres) => double.IsFinite(metres) ? metres : 0.0;
}
