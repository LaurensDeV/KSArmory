using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The one place that decides what the reach looks like, what a click on it does and what the panel
/// says about it — asked off the same flown columns <c>DivertFootprintTests</c> prices the ellipse
/// from.
///
/// <para>The rule under most of these is that the outline, the refusal and the readout are three
/// views of one answer. A cursor refused inside the ring, or taken outside it, is the failure this
/// type exists to make impossible.</para>
/// </summary>
public class ReachDisplayTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    // The flown release at 6,179 km: ~/shots/2026-09-17-threearm shot 001, GeoSat FAT_1 round 1.
    private static readonly double3 FlownPositionCci = new(3_835_254.4, -5_998_331.6, -1_302_454.7);
    private static readonly double3 FlownVelocityCci = new(279.7753, 2844.5295, -3967.0558);

    private static double DensityAt(double3 p)
    {
        double altitude = Math.Max(0.0, Vec.Len(p) - R);
        return altitude >= 167_410.0 ? 0.0 : Math.Exp(-altitude / 8_000.0);
    }

    private static DivertFootprint Pinned()
    {
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;
        ReleaseFocus.Air air = new(new ImpactPredictor.Drag(DensityAt, warhead), 2.0, R, true);

        ReleaseFocus.FlownSensitivity columns =
            ReleaseFocus.FlownSensitivity.TryFly(Earth, FlownPositionCci, FlownVelocityCci, air)
            ?? throw new InvalidOperationException("the columns did not come down");

        Assert.True(DivertFootprint.TryFrom(Earth, columns, DivertFootprint.ArrivalClock.Pinned,
                                            fromTheRealState: true, out DivertFootprint footprint));
        return footprint;
    }

    // A bus part-way down the 6,179 km coast, with the shipped gate and budget.
    private static ReleaseItinerary.Bus Bus(int warheads = 6)
        => new(new IcbmConfig().ReleaseBeforeArrivalSeconds, 1315.0, warheads);

    private const double LethalMetres = 2_000.0;

    private static ReachDisplay Display(IReadOnlyList<ReachDisplay.Placed>? placed,
                                        IcbmPhase phase = IcbmPhase.Coast, bool salvoAway = false,
                                        DivertFootprint? footprint = null, int warheads = 6,
                                        int lead = 0)
        => ReachDisplay.For(footprint ?? Pinned(), placed, Bus(warheads), phase, salvoAway,
                            TargetSet.MaxTargets, LethalMetres, lead, ReachHold.Unflown);

    private static ReachDisplay.Placed Lead => new(0.0, 0.0, 6);

    // The lead holding half the bus, for the fixtures that need a second target to be a stop at all.
    // The lead is flown FIRST and takes its warheads first, so a lead holding all six leaves nothing
    // for anywhere else and the hop is never bought.
    private static ReachDisplay.Placed HalfTheBus => new(0.0, 0.0, 3);

    // The same reach before the burn is over, which reads nothing off a state at all: the frame is
    // the flown one's so the two are comparable, and only the axes are the epoch's.
    private static DivertFootprint Epoch()
    {
        Assert.True(DivertFootprint.TryAtTheEpoch(Pinned().Frame,
                                                  new IcbmConfig().ReleaseBeforeArrivalSeconds,
                                                  out DivertFootprint footprint));
        return footprint;
    }

    /// <summary>
    /// The region and the refusal are one answer: a point on the outline is the last one a click is
    /// taken at.
    /// </summary>
    /// <remarks>
    /// This is the whole contract between what is drawn and what is accepted. Sized on the budget
    /// rather than on one hop's ceiling, the ring would be four times too big and every click in the
    /// outer three-quarters of it would be refused with the outline saying otherwise.
    /// </remarks>
    [Fact]
    public void TheOutlineIsExactlyWhereTheRefusalStarts()
    {
        ReachDisplay reach = Display([Lead]);

        Out.WriteLine($"ring {reach.SemiMajorMetres / 1000.0:F2} x {reach.SemiMinorMetres / 1000.0:F2} km "
                      + $"at {reach.HopMetresPerSecond:F1} m/s a hop, "
                      + $"{reach.LeftMetresPerSecond:F1} m/s of budget left");

        // Along the minor axis, where the ellipse's own edge is SemiMinorMetres by construction.
        double edge = reach.SemiMinorMetres;

        Assert.Equal(ReachVerdict.Adds, reach.Verdict(TargetClick.Add, true, 0.0, edge * 0.99));
        Assert.Equal(ReachVerdict.OutsideReach, reach.Verdict(TargetClick.Add, true, 0.0, edge * 1.01));
    }

    /// <summary>
    /// The ring is one hop, not the whole budget — which is what the release loop can actually fly.
    /// </summary>
    [Fact]
    public void OneHopIsWhatTheRingIsDrawnAt()
    {
        ReachDisplay reach = Display([Lead]);

        Assert.Equal(BusTrim.MaxMetresPerSecond, reach.HopMetresPerSecond, 9);
        Assert.True(reach.LeftMetresPerSecond > BusTrim.MaxMetresPerSecond);

        // And the budget's own reach is the larger number the readout quotes beside it.
        Assert.True(reach.BuysMetres > reach.SemiMinorMetres);
    }

    /// <summary>
    /// A click that starts the shot over is never refused on the bus's reach. Target 1 is the
    /// booster's question, and it is not asked here.
    /// </summary>
    /// <remarks>
    /// Getting this the other way round makes the designator unusable on the pad: with nothing
    /// priced there is no region, so every first designation would be refused with "reach not known"
    /// and nothing would ever be aimed at anything.
    /// </remarks>
    [Fact]
    public void ADesignationIsNeverBoundedByTheBusesReach()
    {
        ReachDisplay nothingPriced = ReachDisplay.None(ReachHold.EpochUnmeasured, 0);

        Assert.False(nothingPriced.HasRegion);

        // A thousand kilometres out, which no bus reaches.
        Assert.Equal(ReachVerdict.Designates,
                     nothingPriced.Verdict(TargetClick.Designate, true, 1_000_000.0, 0.0));
    }

    /// <summary>
    /// Every phase but the one with no trajectory has a region, and <see cref="TargetEdit.ClickDoes"/>
    /// agrees with it at each.
    /// </summary>
    /// <remarks>
    /// The two have to agree, or a click reads as an add in one and as a designation in the other.
    /// Before cutoff the footprint handed in is the release epoch's rather than the flown columns —
    /// pinned, both axes are the epoch, so there is nothing in it the arc could have told us.
    /// </remarks>
    [Fact]
    public void BothSidesOfCutoffHaveARegionAndTheClickAgrees()
    {
        DivertFootprint beforeTheBurn = Epoch();

        foreach (IcbmPhase phase in Enum.GetValues<IcbmPhase>())
        {
            bool coasting = phase == IcbmPhase.Coast;
            ReachDisplay reach = Display([Lead], phase, footprint: coasting ? null : beforeTheBurn);

            Assert.Equal(phase != IcbmPhase.NoSolution, reach.HasRegion);
            Assert.Equal(phase == IcbmPhase.NoSolution ? TargetClick.Designate : TargetClick.Add,
                         TargetEdit.ClickDoes(1, phase, reach.HasRegion));
        }
    }

    /// <summary>
    /// A shot with no trajectory has no landing to draw a reach around, and the panel says which
    /// question is the wrong one.
    /// </summary>
    [Fact]
    public void NoTrajectoryIsTheBoostersProblemRatherThanTheBuses()
    {
        ReachDisplay reach = Display([Lead], IcbmPhase.NoSolution);

        Assert.Equal(ReachHold.NoShot, reach.Hold);
        Assert.False(reach.HasRegion);
    }

    /// <summary>An offset the world could not resolve is not the middle of the region.</summary>
    /// <remarks>
    /// The caller has one way to say it could not answer, and zero is not it: zero is the landing
    /// itself, which is the most reachable point there is. A false accept here puts a target
    /// somewhere nobody pointed at.
    /// </remarks>
    [Fact]
    public void AnUnreadableOffsetIsRefusedRatherThanTakenAsTheCentre()
    {
        ReachDisplay reach = Display([Lead]);

        Assert.Equal(ReachVerdict.Unknown, reach.Verdict(TargetClick.Add, true, double.NaN, double.NaN));
        Assert.Equal(ReachVerdict.Adds, reach.Verdict(TargetClick.Add, true, 0.0, 0.0));
    }

    /// <summary>Another world is refused whatever the reach says, and before anything else.</summary>
    [Fact]
    public void AnotherWorldIsRefusedEvenForADesignation()
    {
        Assert.Equal(ReachVerdict.OffBody, Display([Lead]).Verdict(TargetClick.Designate, false, 0.0, 0.0));
        Assert.Equal(ReachVerdict.OffBody, Display([]).Verdict(TargetClick.Add, false, 0.0, 0.0));
    }

    /// <summary>Six is all a bus goes to, and the region stops being drawn at it.</summary>
    [Fact]
    public void AFullSetHasNoRegionAndTakesNoClick()
    {
        List<ReachDisplay.Placed> six = [Lead];
        for (int i = 1; i < TargetSet.MaxTargets; i++) six.Add(new ReachDisplay.Placed(i * 3_000.0, 0.0, 0));

        ReachDisplay reach = Display(six);

        Assert.Equal(ReachHold.Full, reach.Hold);
        Assert.False(reach.HasRegion);
        Assert.Equal(ReachVerdict.Full, reach.Verdict(TargetClick.Add, true, 0.0, 0.0));
    }

    /// <summary>
    /// A target holding no warheads costs nothing, because the bus never goes there.
    /// </summary>
    /// <remarks>
    /// A target added by clicking the world starts with none, so the alternative is a ring that
    /// shrinks the instant a place is named and grows back when it is given a warhead — which reads
    /// as the display being unstable rather than as the bus being committed.
    /// </remarks>
    [Fact]
    public void APlaceWithNoWarheadsSpendsNothing()
    {
        ReachDisplay alone = Display([Lead]);
        ReachDisplay withAnEmptyOne = Display([Lead, new ReachDisplay.Placed(40_000.0, 0.0, 0)]);

        Assert.Equal(alone.LeftMetresPerSecond, withAnEmptyOne.LeftMetresPerSecond, 9);
        Assert.Equal(alone.SemiMajorMetres, withAnEmptyOne.SemiMajorMetres, 6);

        // And it is not a stop the next hop is measured from either. The bus goes the 6 km from the
        // lead out to the far target whether or not an unassigned place sits half way along -- left
        // in, the hop would be charged as two of 3 km.
        ReachDisplay twoReal = Display([HalfTheBus, new ReachDisplay.Placed(6_000.0, 0.0, 3)]);
        ReachDisplay throughAnEmptyOne = Display([HalfTheBus, new ReachDisplay.Placed(6_000.0, 0.0, 3),
                                                  new ReachDisplay.Placed(3_000.0, 0.0, 0)]);

        Out.WriteLine($"left: {twoReal.LeftMetresPerSecond:F3} with two, "
                      + $"{throughAnEmptyOne.LeftMetresPerSecond:F3} with an unassigned place between");

        Assert.Equal(twoReal.LeftMetresPerSecond, throughAnEmptyOne.LeftMetresPerSecond, 9);
    }

    /// <summary>And one that does hold warheads spends the ground between it and the stop before.</summary>
    [Fact]
    public void ASecondTargetCostsWhatItsHopMoves()
    {
        ReachDisplay alone = Display([HalfTheBus]);
        ReachDisplay near = Display([HalfTheBus, new ReachDisplay.Placed(3_000.0, 0.0, 3)]);
        ReachDisplay far = Display([HalfTheBus, new ReachDisplay.Placed(30_000.0, 0.0, 3)]);

        Out.WriteLine($"left: {alone.LeftMetresPerSecond:F2} alone, {near.LeftMetresPerSecond:F2} at 3 km, "
                      + $"{far.LeftMetresPerSecond:F2} at 30 km");

        Assert.True(near.LeftMetresPerSecond < alone.LeftMetresPerSecond);
        Assert.True(far.LeftMetresPerSecond < near.LeftMetresPerSecond);

        // The room left for further targets goes the same way.
        Assert.True(far.RoomForMore <= near.RoomForMore);
        Assert.True(near.RoomForMore <= alone.RoomForMore);
    }

    /// <summary>
    /// The ring's axes are carried back from the arrival, because a landing is read where the ground
    /// is now and the columns are resolved where it will be a whole fall later.
    /// </summary>
    /// <remarks>
    /// Over the 340 s this state falls for, Earth turns 1.4° — about 2.5% of the ring's own width
    /// laid across it. Dropping the carry leaves the ellipse rotated by exactly that, which looks
    /// almost right and refuses the wrong clicks at its ends.
    /// </remarks>
    [Fact]
    public void TheRingsAxesAreCarriedBackFromTheArrival()
    {
        ReachDisplay reach = Display([Lead]);

        Assert.True(reach.TryAxes(Earth, out double3 major, out double3 minor));

        double3 uncarried = (reach.Footprint.Frame.Downrange * Math.Cos(reach.Footprint.OrientationRad)
                             + reach.Footprint.Frame.Cross * Math.Sin(reach.Footprint.OrientationRad))
                            * reach.SemiMajorMetres;

        double turned = Vec.AngleBetween(major, uncarried) * 180.0 / Math.PI;

        Out.WriteLine($"the ring's long axis is carried {turned:F3} deg back over "
                      + $"{reach.Footprint.FlightSeconds:F0} s of fall");

        Assert.InRange(turned, 0.5, 5.0);
        Assert.Equal(reach.SemiMajorMetres, Vec.Len(major), 3);
        Assert.Equal(reach.SemiMinorMetres, Vec.Len(minor), 3);

        // Square to each other, so the ellipse is one rather than a sheared pair of directions.
        Assert.InRange(Vec.AngleBetween(major, minor) * 180.0 / Math.PI, 89.9, 90.1);
    }

    /// <summary>
    /// A point taken off the ring's own edge reads back as exactly one hop's worth of cost, so the
    /// shape drawn and the offsets measured are the same ellipse.
    /// </summary>
    [Fact]
    public void APointOnTheEdgeCostsExactlyOneHop()
    {
        ReachDisplay reach = Display([Lead]);

        Assert.True(reach.TryAxes(Earth, out double3 major, out double3 minor));

        foreach ((double3 axis, string which) in new[] { (major, "major"), (minor, "minor") })
        {
            Assert.True(reach.TryOffsets(Earth, axis, out double along, out double cross));

            double cost = reach.Footprint.CostMetresPerSecond(along, cross);
            Out.WriteLine($"the {which} axis's end costs {cost:F4} m/s against a "
                          + $"{reach.HopMetresPerSecond:F1} m/s hop");

            Assert.Equal(reach.HopMetresPerSecond, cost, 6);
            Assert.Equal(ReachVerdict.Adds, reach.Verdict(TargetClick.Add, true, along * 0.99, cross * 0.99));
            Assert.Equal(ReachVerdict.OutsideReach,
                         reach.Verdict(TargetClick.Add, true, along * 1.01, cross * 1.01));
        }
    }

    /// <summary>Every refusal says something, and nothing a click is taken at does.</summary>
    [Fact]
    public void EveryRefusalNamesItselfAndNothingElseDoes()
    {
        foreach (ReachVerdict verdict in Enum.GetValues<ReachVerdict>())
        {
            string said = ReachDisplay.CursorSays(verdict);

            Assert.Equal(ReachDisplay.Takes(verdict), said.Length == 0);
        }
    }

    /// <summary>The panel says why there is nothing to draw, for every reason there can be.</summary>
    [Fact]
    public void ThePanelHasALineForEveryHold()
    {
        foreach (ReachHold hold in Enum.GetValues<ReachHold>())
        {
            string said = ReachDisplay.None(hold, 3).Say();

            Out.WriteLine($"{hold}: {said}");
            Assert.NotEmpty(said);
        }

        Assert.NotEmpty(Display([Lead]).Say());
        Assert.NotEmpty(Display([Lead]).SayBudget());
    }

    /// <summary>A salvo that has gone has nothing left to divert, whatever the coast still allows.</summary>
    [Fact]
    public void AwayBeatsEveryOtherReason()
    {
        ReachDisplay reach = Display([Lead], IcbmPhase.Coast, salvoAway: true);

        Assert.Equal(ReachHold.SalvoAway, reach.Hold);
        Assert.False(reach.HasRegion);
    }

    // ------------------------------------------------- the hop is measured where it is charged

    /// <summary>
    /// <b>The check and the charge are one question.</b> Two targets on opposite edges of the ring
    /// are twice its radius apart, and the second is refused.
    /// </summary>
    /// <remarks>
    /// <para>Measured from the <em>landing</em> both clicks are inside the ring and both were taken —
    /// while <see cref="ReleaseItinerary"/> charges the hop between consecutive stops, so the pair
    /// costs about 20 m/s against a 10 m/s ring and <c>ReleaseLoop</c> then refuses the whole walk.
    /// The test states the old measurement beside the new one, so it cannot pass against the form it
    /// exists to detect.</para>
    /// </remarks>
    [Fact]
    public void ASecondTargetIsMeasuredFromTheStopTheHopLeavesFrom()
    {
        ReachDisplay one = Display([HalfTheBus]);

        double edge = one.SemiMinorMetres * 0.99;

        // The first click, out at the edge: taken, and the ring then moves onto it.
        Assert.Equal(ReachVerdict.Adds, one.Verdict(TargetClick.Add, true, 0.0, edge));

        ReachDisplay two = Display([HalfTheBus, new ReachDisplay.Placed(0.0, edge, 3)]);

        Assert.Equal(edge, two.NextHopCrossMetres, 3);
        Assert.Equal(2, two.NextHopFromTarget);

        // The opposite edge is one ring radius from the landing and two from the stop the bus is on.
        Assert.Equal(ReachVerdict.OutsideReach, two.Verdict(TargetClick.Add, true, 0.0, -edge));

        // And that is exactly what the old form got wrong: from the landing it is inside the ring.
        Assert.True(two.Footprint.Reaches(0.0, -edge, two.HopMetresPerSecond),
                    "measured from the landing the click is inside the ring, so this test proves nothing");

        // A click the same distance from the stop the bus is on is taken, so the rule is the hop and
        // not a shrunken region.
        Assert.Equal(ReachVerdict.Adds, two.Verdict(TargetClick.Add, true, 0.0, edge * 1.99));
    }

    /// <summary>And the ring is drawn around that same stop, so nothing inside it is refused.</summary>
    [Fact]
    public void TheRingIsDrawnAroundTheStopTheClickIsMeasuredFrom()
    {
        ReachDisplay two = Display([HalfTheBus, new ReachDisplay.Placed(4_000.0, 1_000.0, 3)]);

        Assert.True(two.TryCentreOffset(Earth, out double3 centreCci));
        Assert.True(two.TryOffsets(Earth, centreCci, out double along, out double cross));

        Out.WriteLine($"the ring's centre reads back at {along:F1} m along, {cross:F1} across");

        Assert.Equal(two.NextHopAlongMetres, along, 3);
        Assert.Equal(two.NextHopCrossMetres, cross, 3);
    }

    /// <summary>
    /// The walk starts at the lead and names the caller's own indices, which is exactly what
    /// <c>ReleaseLoop</c> requires of a plan it is asked to fly.
    /// </summary>
    /// <remarks>
    /// <para>It refuses outright a plan whose first stop is not the lead — <b>which was every real
    /// set</b>, because the ordering was farthest-from-the-landing first and the lead sits <em>at</em>
    /// the landing, so it sorted last and the bus would have walked out to the far end and back.</para>
    ///
    /// <para>And a stop that named a position in some re-ordered array rather than in the list the
    /// lead indexes would aim the walk at the wrong entry, silently.</para>
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TheWalkStartsAtTheLeadAndNamesTheCallersOwnEntries(int lead)
    {
        List<ReachDisplay.Placed> placed =
        [
            new(0.0, 0.0, 2), new(4_000.0, 0.0, 2), new(0.0, 3_000.0, 2),
        ];

        ReachDisplay.Walk walk = ReachDisplay.Order(placed, lead);
        ReleaseItinerary plan = ReleaseItinerary.Plan(walk.Set, Bus(), [355.0]);

        Out.WriteLine($"lead {lead}: stops " + string.Join(" -> ", plan.Stops.Select(s => s.Target)));

        Assert.Equal(placed.Count, walk.Set.Length);
        Assert.Equal(placed.Count, plan.Count);
        Assert.Equal(lead, plan.Stops[0].Target);
        Assert.Equal(0.0, plan.Stops[0].HopMetresPerSecond, 9);

        // Every entry is visited exactly once, so no warhead is planned onto a place twice.
        Assert.Equal([0, 1, 2], plan.Stops.Select(s => s.Target).Order());

        // And the walk leaves the bus at the last stop it made.
        Assert.Equal(plan.Stops[^1].Target + 1, walk.FromTarget);
    }

    // ------------------------------------------------- a set of one is untouched

    /// <summary>
    /// <b>Nothing R1 added can move a single-target flight.</b> With one place on the list the walk
    /// is that place, the ring is around the landing, and the itinerary is the one stop it has always
    /// been — whatever the lead index, the phase or the way the footprint was priced.
    /// </summary>
    /// <remarks>
    /// Structural rather than by inspection: the flight reads <c>TargetSet.Primary</c> and
    /// <c>ReleasePlan</c> and nothing else about the list, and the reach display reaches neither. What
    /// is pinned here is the other half — that the display cannot make a set of one look like a walk.
    /// </remarks>
    [Fact]
    public void ASetOfOneIsTheShotItHasAlwaysBeen()
    {
        DivertFootprint epoch = Epoch();

        foreach (IcbmPhase phase in Enum.GetValues<IcbmPhase>())
        foreach (bool flownColumns in new[] { true, false })
        {
            ReachDisplay reach = Display([Lead], phase, footprint: flownColumns ? null : epoch);

            if (!reach.HasRegion) continue;

            // The ring is on the landing, because the lead is the only stop the bus makes.
            Assert.Equal(0.0, reach.NextHopAlongMetres, 9);
            Assert.Equal(0.0, reach.NextHopCrossMetres, 9);
            Assert.Equal(1, reach.NextHopFromTarget);

            // And no hop has been charged against the budget: the whole spend is the one a
            // single-target flight already makes.
            Assert.Equal(ReleaseItinerary.SingleTargetMetresPerSecond,
                         PostBoostAim.MaxTrimMetresPerSecond - reach.LeftMetresPerSecond, 9);
        }
    }

    /// <summary>Columns that never came down leave no region and no false one.</summary>
    [Fact]
    public void NoColumnsMeansNoRegion()
    {
        ReachDisplay reach = ReachDisplay.For(null, [Lead], Bus(), IcbmPhase.Coast, salvoAway: false,
                                              TargetSet.MaxTargets, LethalMetres, lead: 0,
                                              ReachHold.Unflown);

        Assert.Equal(ReachHold.Unflown, reach.Hold);
        Assert.Equal(0.0, reach.SemiMajorMetres);
        Assert.Equal(ReachVerdict.Unknown, reach.Verdict(TargetClick.Add, true, 0.0, 0.0));
    }
}
