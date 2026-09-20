using Brutal.Numerics;

namespace KSArmory;

/// <summary>Why a bus has a region on the ground to draw, or has not.</summary>
internal enum ReachHold
{
    /// <summary>There is a region, and it is <see cref="ReachDisplay.SemiMajorMetres"/> across.</summary>
    Drawn,

    /// <summary>
    /// The burn is not over. The footprint's long axis is <c>450 · cot γ</c> and γ belongs to the
    /// arc actually flown, which the pad does not know — measured out by 1.04x, 0.51x and 0.59x at
    /// three flown geometries.
    /// </summary>
    NotCoasting,

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
/// <para><b>The reach bounds an <em>add</em>, never a designation.</b> Target 1 is the missile's
/// reach — a trajectory search per candidate point, which is <c>IcbmReach</c> asked along bearings
/// and is not built — so a click that starts the shot over is let through and only a click that
/// adds to a coasting bus is tested.</para>
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
internal readonly record struct ReachDisplay(ReachHold Hold,
                                             DivertFootprint Footprint,
                                             double LeftMetresPerSecond,
                                             double HopMetresPerSecond,
                                             double BuysMetres,
                                             int Targets,
                                             int RoomForMore,
                                             double SpacingMetres)
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
    public static ReachDisplay For(DivertFootprint? footprint, IReadOnlyList<Placed>? placed,
                                   in ReleaseItinerary.Bus bus, IcbmPhase phase, bool salvoAway,
                                   int maxTargets, double spacingMetres)
    {
        int targets = placed?.Count ?? 0;

        if (salvoAway) return None(ReachHold.SalvoAway, targets);
        if (phase != IcbmPhase.Coast) return None(ReachHold.NotCoasting, targets);
        if (footprint is not { } reach) return None(ReachHold.Unflown, targets);

        // One number for the whole coast rather than one per slot, and the smaller axis of the two.
        // The itinerary then prices every hop at the reach the ring is drawn at, so the readout and
        // the picture cannot disagree -- and pinned the two axes agree to within 3% anyway.
        double perMetrePerSecond = reach.SemiMinorMetresPerMetrePerSecond;
        double[] atEachSlot = [perMetrePerSecond];

        ReleaseItinerary itinerary = ReleaseItinerary.Plan(Order(placed), bus, atEachSlot);

        double left = itinerary.LeftMetresPerSecond;
        double hop = Math.Min(left, BusTrim.MaxMetresPerSecond);
        int room = Math.Max(0, ReleaseItinerary.TargetsWithin(spacingMetres, atEachSlot, bus) - targets);

        ReachDisplay display = new(ReachHold.Drawn, reach, left, hop, itinerary.LeftBuysMetres,
                                   targets, room, spacingMetres);

        if (targets >= maxTargets) return display with { Hold = ReachHold.Full };
        if (!(hop > 0.0)) return display with { Hold = ReachHold.Spent };

        return display;
    }

    /// <summary>Whether there is a region on the ground at all.</summary>
    public bool HasRegion => Hold == ReachHold.Drawn;

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

        // Against one hop's ceiling, which is what the ring is drawn at: a refusal measured on the
        // whole budget would refuse ground inside the outline and take clicks outside it.
        return Footprint.Reaches(alongMetres, crossMetres, HopMetresPerSecond)
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
                ReachHold.NotCoasting => "Divert reach: not until the burn is over -- its shape belongs to "
                                         + "the arc actually flown",
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

        return $"Divert reach: {Distance.Say(SemiMinorMetres)} from where the warheads land -- {more}";
    }

    /// <summary>What it is spending, on the line under <see cref="Say"/>.</summary>
    public string SayBudget()
    {
        if (!HasRegion) return "";

        string buys = BuysMetres > 0.0
                          ? $", which moves a landing {Distance.Say(BuysMetres)} in all"
                          : ", and no warhead is assigned anywhere yet";

        return $"{LeftMetresPerSecond:F1} m/s of trim left{buys}; one hop may spend "
               + $"{HopMetresPerSecond:F1}";
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

    // The set as the itinerary wants it: farthest from the landing first, each hop the ground
    // between it and the stop before. Plan sorts by reach and keeps ties in this order, so the
    // hops it charges are the ones measured here.
    //
    // A target holding no warheads is left out rather than given a hop of zero: Plan drops it
    // anyway, and leaving it in would measure the next hop from a stop the bus never makes.
    private static ReleaseItinerary.Target[] Order(IReadOnlyList<Placed>? placed)
    {
        if (placed is null || placed.Count == 0) return [];

        List<int> order = [];
        for (int i = 0; i < placed.Count; i++) if (placed[i].Warheads > 0) order.Add(i);
        if (order.Count == 0) return [];

        order.Sort((a, b) =>
        {
            int byReach = Reach(placed[b]).CompareTo(Reach(placed[a]));
            return byReach != 0 ? byReach : a.CompareTo(b);
        });

        ReleaseItinerary.Target[] set = new ReleaseItinerary.Target[order.Count];

        for (int k = 0; k < order.Count; k++)
        {
            Placed at = placed[order[k]];
            double hop = k == 0 ? 0.0 : Between(placed[order[k - 1]], at);

            set[k] = new ReleaseItinerary.Target(at.Warheads, Reach(at), hop);
        }

        return set;
    }

    // NaN compares equal to nothing, so a place that could not be resolved makes the comparison
    // inconsistent and Sort is entitled to throw on that -- inside the frame hook, where an
    // exception is the game. Ranked last, so it never claims the slot with the most leverage.
    private static double Reach(in Placed at)
    {
        double metres = Math.Sqrt((at.AlongMetres * at.AlongMetres) + (at.CrossMetres * at.CrossMetres));

        return double.IsFinite(metres) ? metres : 0.0;
    }

    private static double Between(in Placed from, in Placed to)
    {
        double along = to.AlongMetres - from.AlongMetres;
        double cross = to.CrossMetres - from.CrossMetres;

        return Math.Sqrt((along * along) + (cross * cross));
    }
}
