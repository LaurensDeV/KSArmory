using Brutal.Numerics;
using KSA;

namespace KSArmory;

internal sealed partial class IcbmComputer
{
    /// <summary>
    /// A map window is open on this craft's targets, so the reach is priced even with one placed.
    /// Called every frame it is open; it decays rather than clearing on use, because the window
    /// draws in the UI pass and this is read in the step.
    /// </summary>
    public void NoteBeingEdited() => _editedFor = EditedFrames;

    private bool BeingEdited => _editedFor > 0;

    private const int EditedFrames = 4;

    private int _editedFor;

    /// <summary>
    /// How far this stack could still throw a warhead, as <see cref="BoosterReach"/> bounds it — a
    /// ceiling. Zero unless a map is open on this craft, because it is a bisection of arc solves.
    /// </summary>
    public double BoosterCeilingMetres { get; private set; }

    private double _sinceCeiling = double.PositiveInfinity;

    private const double CeilingIntervalSeconds = 1.0;

    private void RefreshCeiling(in IcbmState state)
    {
        if (!BeingEdited)
        {
            BoosterCeilingMetres = 0.0;
            _sinceCeiling = double.PositiveInfinity;
            return;
        }

        _sinceCeiling += state.PlayerStepSeconds;
        if (_sinceCeiling < CeilingIntervalSeconds) return;

        _sinceCeiling = 0.0;

        BoosterCeilingMetres = BoosterReach.CeilingMetres(state.Body, state.PositionCci,
                                                          state.VelocityCci,
                                                          state.Booster.DeltaVRemaining,
                                                          Config.MinArrivalAngleDeg);
    }

    private void RefreshReach(in IcbmState state)
    {
        if (_editedFor > 0) _editedFor--;

        RefreshCeiling(state);

        bool coasting = Program.Phase == IcbmPhase.Coast;

        // The coast's length latched at cutoff, never what is left: the countdown stops holding a
        // hop just as the first release is due. Before the `wanted` gate, or it latches late and short.
        double coast = _walker.CoastForPlanning(Program.CommittedArrivalFromNow, coasting);

        bool wanted = Config.ShowDivertReach
                      && !SalvoIsOver
                      && Program.Phase != IcbmPhase.NoSolution
                      && Parent is not null
                      && _warhead is not null
                      && _targets.Count > 0
                      // Seven predictor flights per interval, spent only where targets are being placed.
                      && (Config.DesignateByClicking || _targets.Count > 1 || BeingEdited);

        if (!wanted)
        {
            _reachFootprint = null;
            _reachLater = null;
            _sinceReachWall = double.PositiveInfinity;
            _reachDueAfterTurn = false;
            Reach = ReachDisplay.None(WhyNotWanted(), _targets.Count);
            return;
        }

        if (coasting)
        {
            _sinceReachWall += state.PlayerStepSeconds;

            // Once more as soon as a turn has settled, so the walk is priced on the axes it will be flown on.
            if (_sinceReachWall >= ReachIntervalSeconds || _reachDueAfterTurn)
            {
                _reachDueAfterTurn = false;
                FlyTheReach(state);
            }
        }
        else
        {
            // Nothing flown before cutoff: the columns would depart from the projected cutoff, which
            // before the vehicle has flown is the pad. Pinned, both axes are the release epoch anyway.
            _sinceReachWall = double.PositiveInfinity;
            ReachAtTheEpoch(state);
        }

        double tankKg = double.NaN, busKg = double.NaN;
        bool tankByStop = Config.PriceTheTankStopByStop && TryReadTheTank(out tankKg, out busKg);

        Reach = ReachDisplay.For(_reachFootprint, PlacedTargets(),
                                 new ReleaseItinerary.Bus(Config.ReleaseBeforeArrivalSeconds,
                                                          coast, WarheadsAboard,
                                                          FromCutoff: Config.WalkStartsAtCutoff,
                                                          ReserveAfterFirstStopMetresPerSecond:
                                                              Config.SpendTheSplitReserveAfterTheFirstStop
                                                                  ? ReleaseLoop.SplitNullReserveMetresPerSecond
                                                                  : 0.0,
                                                          HopsFlyTheirOwnCeiling: Config.HopsFlyTheirOwnCeiling,
                                                          TurnSeconds: Program.TurnsAtTheSplit
                                                                           ? Config.TurnAllowanceSeconds
                                                                           : 0.0,
                                                          BudgetMetresPerSecond: Config.HopsSizeTheirOwnSlots
                                                              ? ReleaseLoop.PlanningBudget(
                                                                    Config.TrimBudgetMetresPerSecond,
                                                                    tankByStop ? double.NaN : TankCeilingMetresPerSecond,
                                                                    Config.SpendTheSplitReserveAfterTheFirstStop
                                                                        ? ReleaseLoop.SplitNullReserveMetresPerSecond
                                                                        : 0.0)
                                                              : PostBoostAim.MaxTrimMetresPerSecond,
                                                          HopAccelerationMetresPerSecond2: HopsBurnTheEngine
                                                              ? _release!.Profile.HopEngineAccelerationMetresPerSecond2
                                                              : HopAcceleration,
                                                          FreeArrival: Config.HopsFreeTheirArrival,
                                                          TankKg: tankByStop ? tankKg : double.NaN,
                                                          MassKg: busKg,
                                                          TankExhaustVelocity: HopsBurnTheEngine
                                                              ? _release!.Profile.HopEngineExhaustVelocity
                                                              : TankExhaustVelocity,
                                                          StoreMassKg: ShedStoreMassKg,
                                                          HopTurnSeconds: HopsBurnTheEngine ? HopBurn.TurnsSeconds : 0.0,
                                                          PassesMetresPerSecondPerStop: HopsBurnTheEngine
                                                              ? ReleaseItinerary.EnginePassesMetresPerSecond
                                                              : 0.0),
                                 Program.Phase, SalvoIsOver, TargetSet.MaxTargets,
                                 Warhead.LethalRadius(_warhead!.ChargeKg), _targets.LeadIndex,
                                 coasting ? ReachHold.Unflown : ReachHold.EpochUnmeasured,
                                 Config.PriceEachHopAtItsSlot, Config.PriceHopsAtWhatTheTrimPays,
                                 Config.PriceSlotsOnTheGateChord, _reachLater, HopsBurnTheEngine,
                                 Config.FreeHopsCountTheArrivalTheyMove);

        // Replaced only by a plan with stops: a frame whose footprint did not come down would take the
        // walk away near the release gate and shut ReadyToDeploy mid-walk.
        bool mayReplan = ReleaseLoop.MayReplan(Program.TurnsAtTheSplit, _didSplit, Reach.Flown.Stops, _walker.Walk.Stops,
                                               Config.WalkShrinksAfterTheSplit);
        if (Reach.Flown.Stops > 0 && mayReplan && _walker.Plan(Reach.Flown)) SayTheWalkIfItChanged();
    }

    // On a change of shape, numbers collapsed but the stop count: the cost moves with every re-fly,
    // and a walk that stops being one releases everything at the lead.
    private void SayTheWalkIfItChanged()
    {
        string now = _walker.Walk.Say();
        string shape = $"{_walker.Walk.Stops} {WithoutNumbers(now)}";
        if (shape == _saidWalk) return;

        _saidWalk = shape;
        Log.Info($"walk on {KsaWorld.DisplayName(Craft)}: {now}");
    }

    private string _saidWalk = "";

    // In the order a reader wants it: what has ended the shot first, then what cannot be answered,
    // and "nobody asked" last, because it is the one with a way out.
    private ReachHold WhyNotWanted()
        => SalvoIsOver ? ReachHold.SalvoAway
         : Program.Phase == IcbmPhase.NoSolution ? ReachHold.NoShot
         : Parent is null || _warhead is null || _targets.Count == 0 ? ReachHold.Unflown
         : ReachHold.NotAsked;

    // Before cutoff: the release epoch alone, in a frame at the landing carried to arrival, the
    // epoch the flown columns' frame belongs to, so ReachDisplay's carries mean the same under both.
    private void ReachAtTheEpoch(in IcbmState state)
    {
        _reachFootprint = null;
        _reachLater = null;

        if (Parent is not { } parent || !Target.IsSet || Target.BodyName != parent.Id) return;

        double3 landingCcf = parent.GetDirCcfFromLatLon(Target.LatitudeDeg, Target.LongitudeDeg)
                             * parent.MeanRadius;

        double seconds = Config.ReleaseBeforeArrivalSeconds;
        double3 arrivalCci = Body.CarryCci(landingCcf.Transform(parent.GetCcf2Cci()), seconds);

        // Pinned, the footprint is a disc and only its plane is read; PerpendicularTo keeps a frame
        // for a target under the vehicle or opposite it, where the chord has no horizontal part.
        double3 alongCci = Vec.PerpendicularTo(Vec.Unit(arrivalCci), arrivalCci - state.PositionCci);

        if (!ArrivalFrame.TryAt(arrivalCci, alongCci, out ArrivalFrame frame)) return;
        if (!DivertFootprint.TryAtTheEpoch(frame, seconds, out DivertFootprint footprint)) return;

        _reachFootprint = footprint;
        _reachCci2Ccf = parent.GetCci2Ccf();
        _reachCcf2Cci = parent.GetCcf2Cci();
        _reachLandingCcf = landingCcf;
    }

    // A walk whose hops find their own arrival is priced on the reach that leaves it free.
    private DivertFootprint.ArrivalClock ReachClock
        => Config.HopsFreeTheirArrival ? DivertFootprint.ArrivalClock.Free : DivertFootprint.ArrivalClock.Pinned;

    private void FlyTheReach(in IcbmState state)
    {
        _sinceReachWall = 0.0;

        if (Parent is not { } parent || _warhead is not { } warhead) return;

        try
        {
            double3 positionCci = state.PositionCci + ReleaseOffsetCci();
            double3 velocityCci = state.VelocityCci + ReleaseImpulseCci();

            // Not from inside the air, as for the aim correction: a column flown from there prices a
            // divert off a landing nothing was going to make.
            if (!AimCorrection.DepartureIsWorthObserving(DensityRatioAt(positionCci)))
            {
                _reachFootprint = null;
                _reachLater = null;
                return;
            }

            // A sphere through the ground under the aim: each column is two landings metres apart, and
            // the terrain's texture across them is not the arc's sensitivity.
            double groundRadius = Target.IsSet && Target.BodyName == parent.Id
                                      ? TerrainRadiusAt(_trueAimCci)
                                      : Body.SurfaceRadius;

            long started = System.Diagnostics.Stopwatch.GetTimestamp();

            ReleaseFocus.Air air = new(new ImpactPredictor.Drag(DensityRatioAt, warhead), PredictStepSeconds,
                                       groundRadius, Config.PredictionStopsOnTheSurface,
                                       Config.PredictionStopsOnTheTerrain);
            ReleaseFocus.FlownSensitivity? flown = ReleaseFocus.FlownSensitivity.TryFly(Body, positionCci, velocityCci, air);

            double ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            if (flown is null
                || !DivertFootprint.TryFrom(Body, flown, ReachClock, fromTheRealState: true,
                                            out DivertFootprint footprint))
            {
                _reachFootprint = null;
                _reachLater = null;
                Log.Debug($"reach on {KsaWorld.DisplayName(Craft)}: not priced -- a column did not come "
                          + $"down ({ms:F2} ms)");
                return;
            }

            _reachLater = null;
            if (Config.PriceHopsAtWhatTheTrimPays
                && KsaWorld.TryControlFrameCci(Craft, parent, out double3 nose, out double3 right, out double3 down)
                && DivertFootprint.TryAxisPrice(Body, flown, nose, right, down, out DivertFootprint.AxisPrice axes,
                                                ReachClock))
            {
                footprint = footprint with { Axes = axes };

                if (Config.HopsFreeTheirArrival && Config.FreeHopsMeasureTheirSlots)
                {
                    _reachLater = LaterReach(positionCci, velocityCci, flown.FlightSeconds, air, nose, right, down);
                }
            }

            _reachFootprint = footprint;

            // Kept with the columns: read against a later frame's rotation, the region turns by
            // however far the planet has moved since.
            _reachCci2Ccf = parent.GetCci2Ccf();
            _reachCcf2Cci = parent.GetCcf2Cci();
            _reachLandingCcf = Body.CarryCci(flown.ArrivedCci, -flown.FlightSeconds)
                                   .Transform(_reachCci2Ccf);

            Log.Debug($"reach on {KsaWorld.DisplayName(Craft)}: "
                      + $"{footprint.SemiMajorMetresPerMetrePerSecond:F0} x "
                      + $"{footprint.SemiMinorMetresPerMetrePerSecond:F0} m per m/s {footprint.Clock.ToString().ToLowerInvariant()}, "
                      + $"flown in {ms:F2} ms for a {flown.FlightSeconds:F0} s fall"
                      + (footprint.Axes.Known ? $", the trim paying up to {footprint.WorstAxisFactor():F2}x a divert's length" : ""));
        }
        catch (Exception e)
        {
            _reachFootprint = null;
            _reachLater = null;
            Log.Warn($"reach on {KsaWorld.DisplayName(Craft)}: not priced -- {e.Message}");
        }
    }

    // Every epoch or none: a curve with a gap in it would read the gap as the worst case's fall.
    private List<DivertFootprint>? LaterReach(double3 positionCci, double3 velocityCci, double secondsToGo,
                                              in ReleaseFocus.Air air, double3 nose, double3 right, double3 down)
    {
        List<DivertFootprint> later = [];
        foreach (double before in DivertFootprint.LaterEpochsBeforeArrival(secondsToGo, Config.ReleaseBeforeArrivalSeconds))
        {
            if (!DivertFootprint.TryFreeLater(Body, positionCci, velocityCci, secondsToGo, before, air, nose, right, down,
                                              out DivertFootprint footprint))
            {
                return null;
            }

            later.Add(footprint);
        }

        return later.Count > 0 ? later : null;
    }

    // One entry per target, always: Stop.Target and LeadIndex index this list. A place that will not
    // resolve is kept with no warheads, which Plan drops.
    private List<ReachDisplay.Placed> PlacedTargets()
    {
        _placed.Clear();

        foreach (TargetSet.Entry entry in _targets.Entries)
        {
            bool known = TryReachOffsets(entry.Site, out double along, out double cross);
            _placed.Add(new ReachDisplay.Placed(along, cross, known ? entry.Warheads : 0));
        }

        return _placed;
    }

    private readonly List<ReachDisplay.Placed> _placed = [];

    /// <summary>
    /// Where a place sits on the reach ellipse, in metres along and across. On the mean sphere,
    /// because the height lands in the component nothing reads.
    /// </summary>
    public bool TryReachOffsets(AimSite site, out double alongMetres, out double crossMetres)
    {
        alongMetres = 0.0;
        crossMetres = 0.0;

        if (Parent is not { } parent || !site.IsSet || site.BodyName != parent.Id) return false;

        double3 pointCcf = parent.GetDirCcfFromLatLon(site.LatitudeDeg, site.LongitudeDeg)
                           * parent.MeanRadius;

        return TryOffsetsFromCcf(pointCcf, out alongMetres, out crossMetres);
    }

    /// <summary>The same for a point picked off the ground rather than named by coordinates.</summary>
    public bool TryReachOffsets(double3 groundEcl, out double alongMetres, out double crossMetres)
    {
        alongMetres = 0.0;
        crossMetres = 0.0;

        if (Parent is not { } parent) return false;

        return TryOffsetsFromCcf((groundEcl - parent.GetPositionEcl()).Transform(parent.GetCce2Ccf()),
                                 out alongMetres, out crossMetres);
    }

    // Differenced body-fixed and only then rotated into the frame the columns departed from.
    // Differenced in the ecliptic instead, the two terms carry the planet's own ~29.8 km/s.
    private bool TryOffsetsFromCcf(double3 pointCcf, out double alongMetres, out double crossMetres)
    {
        alongMetres = 0.0;
        crossMetres = 0.0;

        if (!Reach.HasFootprint) return false;

        return Reach.TryOffsets(Body, (pointCcf - _reachLandingCcf).Transform(_reachCcf2Cci),
                                out alongMetres, out crossMetres);
    }

    /// <summary>
    /// Where the next hop leaves from, which is what the reach is drawn around — the landing while
    /// the lead is the last stop, and the last stop itself once there is more than one.
    /// </summary>
    public double3? ReachCentreEcl()
    {
        if (Parent is not { } parent || !Reach.HasRegion) return null;

        double3 centreCcf = _reachLandingCcf;

        if (Reach.TryCentreOffset(Body, out double3 offsetCci))
        {
            centreCcf += offsetCci.Transform(_reachCci2Ccf);
        }

        return centreCcf.Transform(parent.GetCcf2Cce()) + parent.GetPositionEcl();
    }

    /// <summary>The reach ellipse's two semi-axes as ecliptic displacements, in metres.</summary>
    public bool TryReachAxesEcl(out double3 majorEcl, out double3 minorEcl)
    {
        majorEcl = Vec.Zero;
        minorEcl = Vec.Zero;

        if (Parent is not { } parent
            || !Reach.TryAxes(Body, out double3 majorCci, out double3 minorCci))
        {
            return false;
        }

        doubleQuat ccf2Cce = parent.GetCcf2Cce();

        majorEcl = majorCci.Transform(_reachCci2Ccf).Transform(ccf2Cce);
        minorEcl = minorCci.Transform(_reachCci2Ccf).Transform(ccf2Cce);

        return Vec.IsFinite(majorEcl) && Vec.IsFinite(minorEcl);
    }
}
