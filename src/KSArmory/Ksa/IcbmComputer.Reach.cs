using Brutal.Numerics;
using KSA;

namespace KSArmory;

internal sealed partial class IcbmComputer
{
    // The reach the overlay draws, the cursor is refused against and the panel reads -- re-flown
    // rarely and derived every frame, because the set of targets it is priced against changes on a
    // click and a readout a frame behind that reads as the click having done nothing.
    private void RefreshReach(in IcbmState state)
    {
        bool coasting = Program.Phase == IcbmPhase.Coast;

        // The coast's own LENGTH, latched at cutoff, never what is left of it: CoastFits asks
        // whether `coast - gate` holds another hop, so the countdown stops holding one at
        // `gate + hop`, which is the instant the first release is due -- and a two-stop set is cut
        // to one on that very frame. Taken before the `wanted` gate below, because latching it late
        // latches a smaller number.
        double coast = _walker.CoastForPlanning(Program.CommittedArrivalFromNow, coasting);

        bool wanted = Config.ShowDivertReach
                      && !SalvoIsOver
                      && Program.Phase != IcbmPhase.NoSolution
                      && Parent is not null
                      && _warhead is not null
                      && _targets.Count > 0
                      // The reach exists to place and to check targets, and neither is happening on a
                      // bus nobody is clicking at with a set of one. Seven predictor flights per
                      // rocket per interval is not a thing to spend on a rocket nobody is editing.
                      && (Config.DesignateByClicking || _targets.Count > 1);

        if (!wanted)
        {
            _reachFootprint = null;
            _sinceReachWall = double.PositiveInfinity;
            Reach = ReachDisplay.None(WhyNotWanted(), _targets.Count);
            return;
        }

        if (coasting)
        {
            _sinceReachWall += state.PlayerStepSeconds;

            if (_sinceReachWall >= ReachIntervalSeconds) FlyTheReach(state);
        }
        else
        {
            // Nothing is flown before the burn is over, and that is the decision rather than a
            // saving: the columns would have to depart from the guidance's projected cutoff, which
            // before the vehicle has flown is the pad. Pinned there is nothing in the footprint the
            // arc could tell us anyway -- both axes are the release epoch.
            _sinceReachWall = double.PositiveInfinity;
            ReachAtTheEpoch(state);
        }

        Reach = ReachDisplay.For(_reachFootprint, PlacedTargets(),
                                 new ReleaseItinerary.Bus(Config.ReleaseBeforeArrivalSeconds,
                                                          coast, WarheadsAboard,
                                                          FromCutoff: Config.WalkStartsAtCutoff),
                                 Program.Phase, SalvoIsOver, TargetSet.MaxTargets,
                                 Warhead.LethalRadius(_warhead!.ChargeKg), _targets.LeadIndex,
                                 coasting ? ReachHold.Unflown : ReachHold.EpochUnmeasured);

        // Held rather than re-read, and only replaced by a plan that has stops. A frame whose
        // footprint did not come down would otherwise take the walk away on the approach to the
        // release gate and put the gate back to the setting, which shuts ReadyToDeploy mid-walk.
        // The walker refuses everything once the first warhead has gone, so the plan behind the bus
        // cannot be re-ordered under it either.
        if (Reach.Flown.Stops > 0 && _walker.Plan(Reach.Flown)) SayTheWalkIfItChanged();
    }

    // One line whenever the plan changes SHAPE, walking or not. A walk that stops being one is the
    // line that matters: it leaves the flight releasing everything at the lead, and without this it
    // does so under a log full of lines announcing the walk it no longer is.
    //
    // Compared with its numbers collapsed, like the trim's: the reach is re-flown every few seconds
    // and the cost moves with it, so comparing whole sentences writes a line a frame.
    private void SayTheWalkIfItChanged()
    {
        string now = _walker.Walk.Say();
        string shape = WithoutNumbers(now);
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

    // The reach before the burn is over: the release epoch alone, centred on the place the booster
    // is flying to. The frame is built at the landing carried to arrival, which is the epoch the
    // flown columns' own frame belongs to -- so the carries in ReachDisplay mean the same thing
    // under both, and a disc is the same disc either way.
    private void ReachAtTheEpoch(in IcbmState state)
    {
        _reachFootprint = null;

        if (Parent is not { } parent || !Target.IsSet || Target.BodyName != parent.Id) return;

        double3 landingCcf = parent.GetDirCcfFromLatLon(Target.LatitudeDeg, Target.LongitudeDeg)
                             * parent.MeanRadius;

        double seconds = Config.ReleaseBeforeArrivalSeconds;
        double3 arrivalCci = Body.CarryCci(landingCcf.Transform(parent.GetCcf2Cci()), seconds);

        // Downrange is the shot's own direction, and pinned it does not matter: the footprint is a
        // disc and only the plane it lies in is read. Through PerpendicularTo so that a target under
        // the vehicle or opposite it -- where the chord has no horizontal part at all -- still has a
        // frame, rather than the reach vanishing at one bearing.
        double3 alongCci = Vec.PerpendicularTo(Vec.Unit(arrivalCci), arrivalCci - state.PositionCci);

        if (!ArrivalFrame.TryAt(arrivalCci, alongCci, out ArrivalFrame frame)) return;
        if (!DivertFootprint.TryAtTheEpoch(frame, seconds, out DivertFootprint footprint)) return;

        _reachFootprint = footprint;
        _reachCci2Ccf = parent.GetCci2Ccf();
        _reachCcf2Cci = parent.GetCcf2Cci();
        _reachLandingCcf = landingCcf;
    }

    private void FlyTheReach(in IcbmState state)
    {
        _sinceReachWall = 0.0;

        if (Parent is not { } parent || _warhead is not { } warhead) return;

        try
        {
            double3 positionCci = state.PositionCci + ReleaseOffsetCci();
            double3 velocityCci = state.VelocityCci + ReleaseImpulseCci();

            // Not from inside the air, for the reason the aim correction is not: a column flown from
            // in there ploughs through the whole atmosphere and prices a divert off a landing nothing
            // was going to make. The same question of the same model.
            if (!AimCorrection.DepartureIsWorthObserving(DensityRatioAt(positionCci)))
            {
                _reachFootprint = null;
                return;
            }

            // A sphere through the ground under the aim, which is what the columns want: each is two
            // landings a few metres apart, and the terrain's texture across them is not the arc's
            // sensitivity. Flown against the height field it would cost a lookup per step per column.
            double groundRadius = Target.IsSet && Target.BodyName == parent.Id
                                      ? TerrainRadiusAt(_trueAimCci)
                                      : Body.SurfaceRadius;

            long started = System.Diagnostics.Stopwatch.GetTimestamp();

            ReleaseFocus.FlownSensitivity? flown = ReleaseFocus.FlownSensitivity.TryFly(
                Body, positionCci, velocityCci,
                new ReleaseFocus.Air(new ImpactPredictor.Drag(DensityRatioAt, warhead), PredictStepSeconds,
                                     groundRadius, Config.PredictionStopsOnTheSurface,
                                     Config.PredictionStopsOnTheTerrain));

            double ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            if (flown is null
                || !DivertFootprint.TryFrom(Body, flown, DivertFootprint.ArrivalClock.Pinned,
                                            fromTheRealState: true, out DivertFootprint footprint))
            {
                _reachFootprint = null;
                Log.Debug($"reach on {KsaWorld.DisplayName(Craft)}: not priced -- a column did not come "
                          + $"down ({ms:F2} ms)");
                return;
            }

            _reachFootprint = footprint;

            // Both rotations sampled here and kept, not asked for again: the columns' axes and the
            // landing belong to this instant, and reading them against a later frame's rotation
            // turns the whole region by however far the planet has moved since.
            _reachCci2Ccf = parent.GetCci2Ccf();
            _reachCcf2Cci = parent.GetCcf2Cci();
            _reachLandingCcf = Body.CarryCci(flown.ArrivedCci, -flown.FlightSeconds)
                                   .Transform(_reachCci2Ccf);

            Log.Debug($"reach on {KsaWorld.DisplayName(Craft)}: "
                      + $"{footprint.SemiMajorMetresPerMetrePerSecond:F0} x "
                      + $"{footprint.SemiMinorMetresPerMetrePerSecond:F0} m per m/s pinned, "
                      + $"flown in {ms:F2} ms for a {flown.FlightSeconds:F0} s fall");
        }
        catch (Exception e)
        {
            // Inside the frame hook, where an exception is the game rather than a log line.
            _reachFootprint = null;
            Log.Warn($"reach on {KsaWorld.DisplayName(Craft)}: not priced -- {e.Message}");
        }
    }

    // The set as the ground sees it, which is what prices the itinerary.
    //
    // ONE ENTRY PER TARGET, always. ReleaseItinerary.Stop.Target indexes back into this list and
    // TargetSet.LeadIndex indexes the entries, so a list that skipped anything would aim the bus at
    // somebody else's target with nothing saying so. A place the world cannot resolve is therefore
    // kept, with no warheads -- which is what stops it becoming a free stop at the landing, since
    // Plan drops a target nothing leaves at.
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

    /// <summary>Where a place on this world sits on the reach ellipse, in metres along and across.</summary>
    /// <remarks>
    /// On the mean sphere rather than on the terrain, which is what makes it cheap enough to ask of
    /// every target every frame: the answer is resolved along and across the track and the height
    /// goes into the third component, which nothing reads. An accurate height lookup here would buy
    /// a number that is then thrown away.
    /// </remarks>
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
