using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// One craft's ICBM computer: it reads the world, runs <see cref="IcbmProgram"/> and flies the
/// rocket to a place on a map.
///
/// <para>The deciding is in <c>Sim/</c>; this is the world into a <see cref="IcbmState"/> and the
/// answer into writes on somebody else's vehicle — both in the parent's inertial frame, because a
/// half-hour flight carries 54,000 km of the planet's travel through every ecliptic term
/// (<c>docs/FRAMES-AND-EPOCHS.md</c>).</para>
/// </summary>
internal sealed partial class IcbmComputer
{
    // KSA's own value, so an orbit solved here and one drawn by the game agree.
    private const double GravitationalConstant = 6.6743e-11;

    // Simulated seconds, while guidance consumes it; WarpPolicy holds the world down then anyway.
    private const double PredictIntervalSeconds = 0.5;

    // Every computer being integrated this frame, from the roster; empty off the roster's path.
    private IReadOnlyList<IcbmComputer> _busyElsewhere = [];

    private bool _roundsInFlight;

    // Readings are gated by _measureDue, not PredictIntervalSeconds, so a rate per reading needs this.
    private double _sinceObserve;

    // Real seconds for the readout alone: paced by simulated time a warped coast re-flies the whole
    // trajectory every frame, per rocket.
    private const double ReadoutIntervalSeconds = 0.5;

    // Coarse enough to be cheap over half an hour, fine enough to land in the right place.
    private const double PredictStepSeconds = 2.0;

    private readonly List<double3> _path = [];
    private bool _driving;
    private double3 _rollReference;
    private readonly AimCorrection _aim = new();
    private readonly ReleaseSequence _sequence = new();
    private readonly BusTrim _trim = new();
    private readonly ProximityWatch _proximity = new();
    private double3 _keepOutTowardCci;
    private bool _saidProximity;
    private bool _saidStalled;
    private bool _saidCleared;
    private readonly PostBoostAim _postBoost = new();
    private bool _postBoostSaid;
    private bool _measureDue;
    private double _freshMiss = double.NaN;
    private double _holdingCost = double.NaN;
    private int _holdingCostForPass = -1;
    private double _trimFloor = double.NaN;
    private int _trimFloorForPass = -1;
    private bool _resumedForCoast;
    private bool _aimResetForTheOutrun;
    private bool _aimRebasedForTheSolids;
    private bool _trimAbandoned;
    private double _departsIn;
    private double _coastedToRelease;
    private ReleaseCommand _deploy;
    private bool _awaitingSplit;
    private bool _didSplit;
    private bool _mayTrim = true;
    private bool _saidBudget;

    private bool _saidFloorUnaffordable;
    private bool _saidRefusedStage;
    private bool _movedRelease;
    private bool _pointingForTheBurn;
    private double _pointLeadSeconds = HoldPointing.MaxLeadSeconds;
    private IManualFire? _release;
    private bool _saidStructuralLimit;
    private bool _saidLongStep;
    private bool _saidOverLimit;
    private bool _saidHeldControlsDiscarded;

    private double _owedAtSplit = double.NaN;

    // Kept after the trim reports done: post-boost passes resume it, and without the stack they fall
    // to SeparationClearance's blind clock.
    private Vehicle? _separatedFrom;
    private readonly List<Vehicle> _wasBeforeSplit = [];
    private readonly List<Vehicle> _afterSplit = [];

    // Everything shed, found by difference as WhatWasDropped does, at every staging: the ascent
    // stages are three of the four vehicles a rocket leaves behind.
    private readonly List<Vehicle> _shed = [];
    private readonly List<Vehicle> _wasBeforeStage = [];
    private readonly List<Vehicle> _afterStage = [];
    private bool _awaitingStage;

    // Engines and parts read either side of every staging: a part KSA destroys by contact leaves no
    // line anywhere else.
    private static readonly double[] StagingProbeAt = [0.0, 0.25, 1.0, 2.0];
    private const double StagingWindowSeconds = 2.5;
    private const double StagingGapSeconds = 0.6;
    private Vehicle? _gapStage;
    private double _gapOpening = double.NaN;
    private readonly Diagnostics.PartWatch _partWatch = new();
    private readonly List<string> _lostParts = [];
    private double _sinceStaging = double.NaN;
    private bool _saidLineSlowed;
    private int _stagingProbe;

    // The session's settings; only the stage-disposal switch is read.
    private readonly Config _session;
    private double _sinceSplit;
    private string _trimShape = "";
    private string _saidTrim = "";

    private Vehicle? _viewWanted;
    private string _saidLast = "";
    private double3 _trueAimCci;
    private MunitionProfile? _warhead;
    private bool _releaseMeasured;
    private bool _warpIsOurs;
    private IcbmPhase _reported = IcbmPhase.Idle;
    private double _sinceProbe;
    private double _sinceThrottleProbe;
    private double3 _lastCommanded;

    // One per released warhead, to separate the walk from the within-rocket deviation
    // (docs/ACCURACY-PLAN.md 3ee). The re-fly is rationed to one every ten seconds, so 48 traces
    // do not move the frame time the walk tracks.
    private readonly List<WarheadTrace> _traces = [];

    private readonly SalvoProbe _salvoProbe = new();
    private bool _traceWanted;

    // The salvo's kick columns flown through the air, for IcbmConfig.KickThroughTheAir, and the drag they were
    // flown with: a warhead swapped to another drag between releases is a different flight.
    private ReleaseFocus.FlownSensitivity? _flownKick;
    private double _flownKickDragK = double.NaN;

    // Body-fixed with the rotation of the instant it was flown: five seconds of Earth's spin is
    // 2.3 km of ground, the whole shape of a ring a few kilometres across.
    private DivertFootprint? _reachFootprint;
    private List<DivertFootprint>? _reachLater;
    private double3 _reachLandingCcf;
    private doubleQuat _reachCci2Ccf = doubleQuat.Identity;
    private doubleQuat _reachCcf2Cci = doubleQuat.Identity;
    private double _sinceReachWall = double.PositiveInfinity;

    // Real seconds, as ReadoutIntervalSeconds. Seven predictor flights a time, for a reach that
    // decays over hundreds of seconds (1,076 to 886 m per m/s over 330 s).
    private const double ReachIntervalSeconds = 5.0;

    /// <summary>
    /// A warhead is being followed and has not reported yet. The trace reports from a poll one
    /// frame after the round stops flying, so a harness that ends the run on the last impact ends
    /// it before the report — see <see cref="ScenarioRunner"/>.
    /// </summary>
    public bool TraceOutstanding => _traceWanted && _traces.Any(t => t.Watching);
    private MunitionProfile? _tracedWarhead;
    private bool _saidTraceStranded;

    // Cached: a method group allocates a delegate, and the trace builds its Setup every frame.
    private Func<double3, double>? _terrainRadius;
    private Func<double3, double>? _densityRatio;

    // Often enough to see an oscillation, rare enough not to fill the log with a burn's worth.
    private const double ProbeIntervalSeconds = 0.5;

    private double _throttleAchieved = 1.0;

    public Vehicle Craft { get; private set; }

    public IcbmConfig Config { get; }

    public IcbmProgram Program { get; }

    /// <summary>
    /// The lead of <see cref="Targets"/>, which everything downstream reads and nothing else about
    /// the list. Nothing happens until it is set.
    /// </summary>
    public AimSite Target => _targets.Primary;

    public IReadOnlyList<TargetSet.Entry> Targets => _targets.Entries;

    /// <summary>
    /// The ground this bus can still put a warhead on, derived once a frame so the overlay, the
    /// cursor and the panel cannot disagree.
    /// </summary>
    public ReachDisplay Reach { get; private set; } = ReachDisplay.None(ReachHold.Unflown, 0);

    /// <summary>Which of <see cref="Targets"/> the flight is aimed at, which is <see cref="Target"/>'s.</summary>
    public int LeadTarget => _targets.LeadIndex;

    private readonly TargetSet _targets = new();

    /// <summary>
    /// How many warheads the target list is planned against.
    ///
    /// <para>The most the magazine has ever reported loaded, until a salvo names its own size.
    /// Nothing in the flight reads it: it bounds what the panel may assign.</para>
    /// </summary>
    public int WarheadsAboard => TargetEdit.WarheadsAboard(_warheadsLoaded, _salvoSize);

    private int _warheadsLoaded;

    public IcbmCommand Command { get; private set; }

    /// <summary>Flown from <see cref="PredictsFromCutoff"/>'s departure; null when it would not land.</summary>
    public ImpactPredictor.Impact? PredictedImpact { get; private set; }

    /// <summary>
    /// The prediction departs from the burn's planned cutoff rather than from the vehicle as it is,
    /// so it says where the burn as planned lands, not where the rocket would fall if cut off now.
    /// </summary>
    public bool PredictsFromCutoff { get; private set; }

    /// <summary>From the target, not the biased aim, along the ground.</summary>
    public double PredictedMissMetres { get; private set; } = double.NaN;

    /// <summary>Once one is away, the bus's own prediction is no longer the shot's.</summary>
    public int WarheadsAway { get; private set; }

    /// <summary>
    /// Nothing is left to correct for: false before the first release, and on a walk true when the
    /// walk is over, since a dropped stop keeps its warheads aboard.
    /// </summary>
    public bool SalvoFinished => _walker.Done || (_salvoSize > 0 && WarheadsAway >= _salvoSize);

    private int _salvoSize;

    /// <summary>The warhead aboard, or null for a vehicle carrying nothing that lets go.</summary>
    public MunitionProfile? Munition => _warhead;

    /// <summary>
    /// How far the aim has been moved to make the flown arc arrive, in metres. Read beside the
    /// predicted miss: the correction is clamped.
    /// </summary>
    public double AimBiasMetres => Vec.Len(_aim.BiasCci);

    public BallisticBody Body { get; private set; }

    /// <summary>Over the mean sphere.</summary>
    public double AltitudeMetres { get; private set; }

    public double OffPlaneDegrees { get; private set; }

    /// <summary>Roughly what turning the orbit by <see cref="OffPlaneDegrees"/> would cost alone.</summary>
    public double PlaneChangeCost { get; private set; }

    /// <summary>
    /// Seconds until the warheads arrive: the plan while holding or burning, since it assumes the
    /// burn finishes, and the flown prediction after.
    /// </summary>
    public double SecondsToArrival
        => Program.IsBurning || Program.Phase == IcbmPhase.Holding
               ? Program.SecondsToArrival
               : ArrivalFromTheLastPrediction();

    // _arrivalLeft is run down by the simulated step in Update, so it counts between solves and
    // after predicting stops.
    private double ArrivalFromTheLastPrediction()
    {
        return _arrivalLeft > 0.0 ? _arrivalLeft : double.NaN;
    }

    /// <summary>
    /// Warheads have not landed yet. True across the craft's death: a bus breaks up on reentry five
    /// to twenty seconds before its warheads arrive.
    /// </summary>
    public bool SalvoStillArriving => _salvoAway && double.IsFinite(SecondsToArrival);

    /// <summary>The salvo has gone and arrived. False before a shot as well as during one.</summary>
    public bool SalvoHasLanded => _salvoAway && !double.IsFinite(SecondsToArrival);

    private double _arrivalLeft = double.NaN;

    /// <summary>
    /// During the coast <see cref="SecondsToArrival"/> is when a warhead released this instant would
    /// land, early by however long the clearance and the trim take.
    /// </summary>
    public bool ArrivalIsIfReleasedNow => !Program.IsBurning && Program.Phase != IcbmPhase.Holding;

    // After the salvo Predict would fly the bus with a modelled kick: a warhead that does not exist.
    private bool _salvoAway;

    // Holding an empty bus's attitude would fire its thrusters for the rest of the coast.
    private bool _letGoAfterSalvo;
    private bool _saidClearOnce;

    /// <summary>Holds no walk for a set of one — see <see cref="ReleaseWalker.Walking"/>.</summary>
    public ReleaseWalker Walk => _walker;

    private readonly ReleaseWalker _walker = new();

    private double _spentAtStopStart;

    // The ceiling the hop just handed over to was priced at, for the pass before the correction's first.
    private double _hopCeilingMetresPerSecond = double.NaN;
    private double _hopBurnSeconds;

    // HopsResolveAfterTheirBurn: rounds flown since the hand-over, and the spend the round started at; -1 when no hop's
    // arc is still being solved again.
    private int _hopRound = -1;
    private double _spentAtHopRound;

    // What the bus's jets push it at, for sizing a hop's slot and clocks; NaN with the flag off.
    private double HopAcceleration
        => Config.HopsSizeTheirOwnSlots ? _release?.Profile.TrimAccelerationMetresPerSecond2 ?? double.NaN : double.NaN;

    private readonly List<(IProjectile Round, int Target)> _sentTo = [];

    private readonly List<(int Target, string Site, int Warheads)> _wentTo = [];

    // Over, not merely begun: between a walk's stops the bus is still aimed, corrected and trimmed.
    private bool SalvoIsOver => _salvoAway && !_walker.Walking;

    /// <summary>NaN while nothing is trimming.</summary>
    public double TrimToGainMetresPerSecond => _trim.Armed ? _trim.ToGainMetresPerSecond : double.NaN;

    /// <summary>
    /// The last thing said about the trim or the clearance before it — not <see cref="BusTrim.Said"/>,
    /// which is blank through the wait before the trim is armed.
    /// </summary>
    public string TrimSaid => _saidTrim;

    /// <summary>
    /// Owed at the decoupler, or NaN if it never split. Grown by release, something moved the
    /// vehicle or the aim while it coasted clear.
    /// </summary>
    public double TrimOwedAtSplitMetresPerSecond => _owedAtSplit;

    /// <summary>What it still owed when first allowed to push.</summary>
    public double TrimOwedOnReleaseMetresPerSecond => _trim.AtReleaseMetresPerSecond;

    /// <summary>
    /// This frame's hold and release. <see cref="ReleaseCommand.OffLineDegrees"/> is only meaningful
    /// on the frame a round leaves.
    /// </summary>
    public ReleaseCommand Deployment => _deploy;

    /// <summary>
    /// The burn, the trim and the aim measurement between them keep the world slow: each stops on
    /// a frame boundary, and at 12,902 km warping the coast through the passes shot 8.25 km against 0.62.
    /// Bounded by <see cref="PostBoostAim.MaxSeconds"/>, about two minutes of the coast.
    /// </summary>
    public bool NeedsShortSteps => Program.NeedsShortSteps || TrimIsFiring || PostBoostIsCorrecting;

    // Waits for ReadyToDeploy: the loop starts out Settling and would read as correcting through a
    // hold and the coast.
    private bool PostBoostIsCorrecting => Command.ReadyToDeploy && _postBoost.Correcting;

    /// <summary>For the warp log.</summary>
    public string ShortStepsBecause
        => (Program.NeedsShortSteps ? $" {Program.Phase}" : "")
           + (TrimIsFiring ? " trim" : "")
           + (PostBoostIsCorrecting ? " post-boost" : "");

    // A hold drifts until the turn onto its burn is due, and goes back to drifting if the window moves away: pointed on
    // the bus's jets the stack empties them in minutes at 1x. HoldPointing has why.
    private bool Drifting(in IcbmState state)
    {
        double toIgnition = Program.SecondsToIgnitionInHold;
        if (!Config.HoldDriftsUntilIgnition || !double.IsFinite(toIgnition))
        {
            _pointingForTheBurn = false;
            return false;
        }

        _pointLeadSeconds = PointLeadSeconds(state);
        bool was = _pointingForTheBurn;
        _pointingForTheBurn = HoldPointing.Points(toIgnition, _pointLeadSeconds, was);

        if (_pointingForTheBurn != was)
        {
            Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)}: "
                     + (_pointingForTheBurn ? "turning onto the burn" : "drifting again, the window moved")
                     + $", {toIgnition:F0} s before ignition against a {_pointLeadSeconds:F0} s lead{BusJetsSaid()}");
        }

        return !_pointingForTheBurn;
    }

    private double PointLeadSeconds(in IcbmState state)
    {
        if (Parent is not { } parent || Command.ThrustDirectionCci.Equals(Vec.Zero)
            || !KsaWorld.TryControlFrameCci(Craft, parent, out double3 nose, out _, out _)
            || !KsaWorld.TryRcsAuthority(Craft, out double accel, out double rateLimit))
        {
            return HoldPointing.MaxLeadSeconds;
        }

        return HoldPointing.LeadSeconds(Vec.AngleBetween(nose, Command.ThrustDirectionCci), accel, rateLimit);
    }

    /// <summary>Stages listed after the launcher's release, for the pad's warning.</summary>
    public int StagesAfterTheRelease => _release?.StagesAfterTheRelease ?? 0;

    public bool RefusedAStage => _saidRefusedStage;

    public bool TrimIsFiring => _trim.Armed && !_trim.Done && _mayTrim;

    public Celestial? Parent { get; private set; }

    public IcbmComputer(Vehicle craft, IcbmConfig config, Config session)
    {
        Craft = craft;
        Config = config;
        _session = session;
        Program = new IcbmProgram(config);
    }

    /// <summary>
    /// Aim at one place and start the shot over; <see cref="AddTarget"/> is the edit.
    /// <c>AimSite.None</c> clears the whole list.
    /// </summary>
    public void Designate(AimSite site)
    {
        _targets.SetOnly(site, WarheadsAboard);
        ForgetTheFlight();
        _reported = IcbmPhase.Idle;
        _aim.Reset();
        _sequence.Reset();
        WarheadsAway = 0;
        _salvoSize = 0;
        _rollReference = Vec.Zero;
        PredictedImpact = null;
        PredictedMissMetres = double.NaN;
        _salvoAway = false;
        _letGoAfterSalvo = false;

        // Traces still in the air are dropped rather than scored against the wrong target.
        _tracedWarhead = null;
        _saidTraceStranded = false;
        foreach (WarheadTrace trace in _traces) trace.Forget();
        _traces.Clear();
        _salvoProbe.Forget();
        _flownKick = null;
        _walker.Reset();
        _spentAtStopStart = 0.0;
        _sentTo.Clear();
        _watchedRelease = 0;
        _wentTo.Clear();
        _saidWalk = "";

        Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} designated {site.Describe()}");
    }

    /// <summary>What a save keeps of this computer: its settings and its targets, never its flight.</summary>
    public BallisticSettings Setup => BallisticSettings.From(Config, _targets.Entries, _targets.LeadIndex);

    /// <summary>
    /// Put back a setup read from a save, disarmed. Only on a computer that is flying nothing, since
    /// replacing the targets under a shot aims it at places it was not flown for.
    /// </summary>
    public bool Restore(BallisticSettings setup)
    {
        if (Config.Armed || Program.Phase != IcbmPhase.Idle) return false;

        setup.ApplyTo(Config, _targets);
        Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)}: settings and "
                 + $"{_targets.Count} target(s) restored from the save, disarmed");
        return true;
    }

    /// <summary>Add a place for the warheads to go, and touch nothing that belongs to the flight.</summary>
    public bool AddTarget(AimSite site)
    {
        if (!_targets.TryAdd(site))
        {
            Log.Warn($"ICBM computer on {KsaWorld.DisplayName(Craft)}: {site.Describe()} refused - "
                     + $"a bus goes to at most {TargetSet.MaxTargets} places");
            return false;
        }

        string moved = ElectLead();

        Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)}: target {_targets.Count} is "
                 + $"{site.Describe()}{moved}; it gets no warheads until it is given some, and a "
                 + "target nothing leaves at is not a stop");
        return true;
    }

    // The farthest target leads, while the aim is free: the walk starts from the lead, and from
    // anywhere but an end of the set the bus goes out and back at about twice the itinerary's price.
    private string ElectLead()
    {
        if (!TargetEdit.LeadMayMove(Program.Phase, double.IsFinite(Program.CommittedArrivalFromNow)))
        {
            return "";
        }

        if (Parent is not { } parent) return "";

        double3 fromEcl = KsaWorld.PositionEcl(Craft);
        double[] ranges = new double[_targets.Count];

        for (int i = 0; i < _targets.Count; i++)
        {
            AimSite site = _targets.Entries[i].Site;

            ranges[i] = site.IsSet && site.BodyName == parent.Id
                            ? Vec.Len(SurfacePointEcl(parent, site.LatitudeDeg, site.LongitudeDeg) - fromEcl)
                            : double.NaN;
        }

        return _targets.ElectFarthestLead(ranges)
                   ? $" and is the farthest, so the booster flies to it"
                   : "";
    }

    /// <summary>
    /// Its warheads go back to the spares. Never once a walk is committed: its stops index this list.
    /// </summary>
    public bool RemoveTarget(int index)
    {
        if (!MayRemoveTarget(index)) return false;

        string what = _targets.Entries[index].Site.Describe();
        if (!_targets.RemoveAt(index)) return false;

        Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)}: {what} is no longer a target");
        return true;
    }

    public bool MayRemoveTarget(int index)
        => !(_walker.Committed && _walker.Walk.Walks)
           && TargetEdit.MayRemove(index, _targets.Count, _targets.LeadIndex);

    /// <summary>Bounded by what the others have taken.</summary>
    public void SetTargetWarheads(int index, int warheads)
        => _targets.SetWarheads(index, warheads, WarheadsAboard);

    /// <summary>Taking them from the lead; refused rather than clamped when there are not enough.</summary>
    public bool ShiftTargetWarheads(int index, int delta)
        => _targets.Shift(index, delta, WarheadsAboard);

    public void BalanceTargets() => _targets.Balance(WarheadsAboard);

    public string DescribeTargets() => _targets.Describe(WarheadsAboard);

    /// <summary>Forget the flight and hand the vehicle back.</summary>
    public void Abort(string why)
    {
        if (_warpIsOurs)
        {
            KsaWorld.StopAutoWarp();
            _warpIsOurs = false;
        }

        AttitudeHook.Release(Craft);

        if (_driving)
        {
            VehicleCommand.SetEngine(Craft, running: false);
            VehicleCommand.ReleaseAttitude(Craft);
            _driving = false;
        }

        // Outside that block: the thruster flags are held keys, and one left down translates the bus.
        VehicleCommand.DriveTranslation(Craft, TrimAxes.None);
        AttitudeHook.PulseMode(Craft, pulsing: false);

        ForgetTheFlight();
        Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} stood down: {why}");
    }

    // What a new aim and a stand-down both forget. Designate also forgets the aim, the salvo and
    // the walk; Abort keeps what has already flown.
    private void ForgetTheFlight()
    {
        Program.Reset();
        _releasedTheArrival = false;
        _trim.Reset();
        _postBoost.Reset();
        _postBoostSaid = false;
        _measureDue = false;
        _freshMiss = double.NaN;
        _holdingCost = double.NaN;
        _holdingCostForPass = -1;
        _trimFloor = double.NaN;
        _trimFloorForPass = -1;
        _resumedForCoast = false;
        _aimResetForTheOutrun = false;
        _aimRebasedForTheSolids = false;
        _trimAbandoned = false;
        _trimShape = "";
        ResetTurn();
        _saidTrim = "";
        _separatedFrom = null;
        _didSplit = false;
        _proximity.Reset();
        _saidProximity = false;
        _saidCleared = false;
        _keepOutTowardCci = double3.Zero;
        _sinceSplit = 0.0;
        _mayTrim = true;
        _saidBudget = false;
        _saidFloorUnaffordable = false;
        _owedAtSplit = double.NaN;
        _hopCeilingMetresPerSecond = double.NaN;
        _hopBurnSeconds = 0.0;
        _hopRound = -1;
        ResetTheHop();
    }

    /// <param name="release">Null for a vehicle carrying nothing that lets go, which flies the arc regardless.</param>
    /// <param name="traceWarhead">Follow released warheads down with <see cref="WarheadTrace"/>; measurement only.</param>
    public void Update(double simStep, double playerStep, IManualFire? release,
                       bool traceWarhead = false,
                       IReadOnlyList<IcbmComputer>? busyElsewhere = null,
                       bool roundsInFlight = false)
    {
        _release = release;

        // On a steep arrival the bus breaks up before its warheads arrive, and the trace needs only
        // the planet and the aim.
        if (!KsaWorld.IsAlive(Craft))
        {
            // Frozen, anything holding on for the salvo to arrive would hold for ever.
            if (double.IsFinite(_arrivalLeft)) _arrivalLeft -= simStep;

            _reachFootprint = null;
            _reachLater = null;
            Reach = ReachDisplay.None(ReachHold.SalvoAway, _targets.Count);

            StepTraceLoose(simStep);
            return;
        }

        _busyElsewhere = busyElsewhere ?? [];
        _roundsInFlight = roundsInFlight;
        _traceWanted = traceWarhead;

        // Before the round is read, so the prediction and the warheads share one profile.
        if (Config.WarheadSubStepMs > 0.0 && release is { } stepped
            && Math.Abs(stepped.Munition.SubStep - Config.WarheadSubStepMs / 1000.0) > 1e-9)
        {
            stepped.FlyRoundsAs(stepped.Munition.AtSubStep(Config.WarheadSubStepMs / 1000.0));
            Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)}: warheads integrate at "
                     + $"{stepped.Munition.SubStep * 1000.0:F3} ms, {stepped.Munition.MaxSubSteps} sub-steps "
                     + "to a frame at most");
        }

        _warhead = release?.Munition;

        // A latch, read every frame: rounds in the air first say a warhead has left (Ammo and
        // TubesReadyToFire do not), and unlatched it would clear when the last one landed.
        _salvoAway |= release is IRoundsInFlight flying && flying.Rounds.Count > 0;

        // The largest reading: one taken mid-reload or with no weapon resolved would say empty.
        if (release is { } rack) _warheadsLoaded = Math.Max(_warheadsLoaded, rack.TubesReadyToFire);

        if (double.IsFinite(_arrivalLeft)) _arrivalLeft -= simStep;
        _salvoProbe.Advance(simStep);
        MeasureRelease(release);

        if (!Config.Armed)
        {
            // An edge, not a state: releasing every frame would take the vehicle from a player
            // flying it by hand.
            if (_driving) Abort("disarmed");
            IcbmState idle = Sample(playerStep, out _);
            StepTrace(simStep);
            Command = Program.Update(simStep, idle);

            // On the pad too, where targets are placed; before the burn the reach costs no flying.
            RefreshReach(idle);
            return;
        }

        IcbmState state = Sample(playerStep, out bool usable);
        StepStagingProbe(simStep, state);

        // After Sample writes this frame's Parent, Body and aim, and before a release, so a warhead
        // let go this frame starts its trace at zero.
        StepTrace(simStep);

        if (!usable)
        {
            Command = Program.Update(simStep, state);
            return;
        }

        // A walk moves its first release earlier so its last lands on the setting; NaN leaves the
        // program reading the setting live.
        Program.ReleaseGateSeconds = _walker.GateOverrideSeconds(Config, _targets.Count);

        bool wasBurning = Program.IsBurning;
        Command = Program.Update(simStep, state);
        ReportLongStep(wasBurning, simStep, state);
        WatchTheCutoffTail(wasBurning, simStep, state);

        if (!_saidStalled && Command.Hold.StartsWith("cutoff stalled", StringComparison.Ordinal))
        {
            _saidStalled = true;
            Log.Info($"{KsaWorld.DisplayName(Craft)} ICBM: {Command.Hold}");
        }

        CollectShedStages();
        DisposeShedStages();

        // An arrival this stack cannot buy, flown shallower anyway; logged once a flight, since an
        // unattended shot has no panel.
        if (Program.ArrivalFloorUnaffordable && !_saidFloorUnaffordable)
        {
            _saidFloorUnaffordable = true;
            double got = Program.Arc?.ArrivalAngleDeg ?? double.NaN;

            Log.Info($"{KsaWorld.DisplayName(Craft)} ICBM: cannot afford the "
                     + $"{Config.MinArrivalAngleDeg:F0} deg arrival asked for; flying "
                     + (double.IsFinite(got) ? $"{got:F1} deg instead" : "the shallowest it can")
                     + " -- it will arrive, less precisely");
        }

        ProbeTheCoast(simStep);

        // One line per phase change: every gate returns quietly, and the panel shows only now.
        if (Command.Phase != _reported)
        {
            _reported = Command.Phase;
            Log.Info($"{KsaWorld.DisplayName(Craft)} ICBM: {Command.Phase} at "
                     + $"{AltitudeMetres / 1000.0:F0} km, {Command.VelocityToGain:F0} m/s to gain, "
                     + $"burn in {IcbmProgram.Clock(Command.SecondsToBurn)}, "
                     + $"{(ArrivalIsIfReleasedNow ? "impact if released now in" : "impact in")} "
                     + $"{IcbmProgram.Clock(SecondsToArrival)}, "
                     + $"target {OffPlaneDegrees:F1} deg off plane ({PlaneChangeCost:F0} m/s), "
                     + $"reach {Command.Reach}{BusJetsSaid()}"
                     + (double.IsFinite(Program.ResidualAtCutoff)
                            ? $", cut off {Program.ResidualAtCutoff:F2} m/s short{ResidualSaid()}"
                            : "")
                     // Near zero, the solution is self-consistent and a miss happened afterwards.
                     + PredictedImpactSaid()
                     + $" :: {Command.Hold}");
        }

        // Read off a schedule that was adding to the shot, which the closed loop is about to stop flying.
        if (Program.SolidsWentOutInTheLoop && !_aimRebasedForTheSolids)
        {
            _aimRebasedForTheSolids = true;
            Log.Info($"{KsaWorld.DisplayName(Craft)} ICBM: the solids burned out in the closed loop; the aim keeps its "
                     + $"{Distance.Say(Vec.Len(_aim.BiasCci))} of bias and forgets its best");
            _aim.Rebase();
        }

        if (Program.OutranTheStack && !_aimResetForTheOutrun)
        {
            _aimResetForTheOutrun = true;
            Log.Info($"{KsaWorld.DisplayName(Craft)} ICBM: the schedule outran the stack; the aim correction starts again "
                     + $"from a bias of {Distance.Say(Vec.Len(_aim.BiasCci))}");
            _aim.Reset();
        }

        // The aim freezes once the arrival is committed: one problem solved in two halves.
        if (Program.IsBurning && double.IsFinite(Program.CommittedArrivalFromNow)) _aim.Freeze(Config.FreezeKeepsABetterRecentAim);

        // And resumes once after cutoff, when the arc follows the aim and the trim flies the difference.
        if (Program.ResumesTheAim && !_resumedForCoast)
        {
            _resumedForCoast = true;
            _aim.Resume();
        }

        Predict(simStep, state);
        RefreshReach(state);

        // Before this frame's writes: KSA replaces the flight computer from its worker every frame, so
        // this against a read after writing tells a write that never lands from one reverted.
        FlightComputerAttitudeMode wasMode = Craft.FlightComputer.AttitudeMode;
        FlightComputerAttitudeTrackTarget wasTrack = Craft.FlightComputer.AttitudeTrackTarget;

        // Separates when the release gate opens, or a short shot when the burn ends; before anything
        // decides a release, since the shove (~1 m/s) on a release frame made a 163 m outlier. A shot
        // releasing at cutoff never separates: flown at 300 km, separating cost 2.2 km against 10.
        bool atCutoff = Program.ReleasesAtCutoff;
        bool burnOver = Program.Phase == IcbmPhase.Coast && !atCutoff
                        && Program.IsShortShot;
        if ((Command.ReadyToDeploy && !atCutoff) || burnOver) SeparateOnce(release);

        DriveTurn(simStep, state);
        DriveHop(simStep, state);
        WatchTheReleased(simStep);
        if (_hopBurn.Holds)
        {
            if (_trim.Firing != TrimAxes.None) VehicleCommand.DriveTranslation(Craft, TrimAxes.None);
        }
        else
        {
            DriveTrim(simStep, state, release);
        }

        // Attitude is driven in every active phase but a drifting hold, not only with an engine lit.
        bool aimed = false;

        bool drifting = Drifting(state);

        if (Command.Phase is not (IcbmPhase.Idle or IcbmPhase.NoSolution) && !drifting)
        {
            // On the nominal line, not the sequencer's: stepping by a cant would re-flatten it each time.
            _rollReference = AimFrame.Advance(_rollReference, Command.ThrustDirectionCci,
                                              -Vec.Unit(state.PositionCci), RollFallback(state));

            _deploy = DriveDeployment(simStep, release, state);

            // Once per change of state, the first clause; the whole sentence writes sixty lines a turn.
            string stage = _deploy.Said.Length > 0 ? _deploy.Said.Split(',')[0] : "";

            if (stage != _saidLast)
            {
                _saidLast = stage;
                if (_deploy.Said.Length > 0) Log.Info($"deploying: {_deploy.Said}");
            }

            // Through the hook: a write from this pass is discarded before anything reads it.
            if (SalvoIsOver)
            {
                if (!_letGoAfterSalvo)
                {
                    _letGoAfterSalvo = true;
                    AttitudeHook.Release(Craft);
                    if (_driving)
                    {
                        VehicleCommand.ReleaseAttitude(Craft);
                        _driving = false;
                    }
                    VehicleCommand.DriveTranslation(Craft, TrimAxes.None);
                    AttitudeHook.PulseMode(Craft, pulsing: false);
                    Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)}: the salvo is away; the bus is let go");
                }
            }
            else
            {
                // A Strict deadband held through a wait empties the bus's jets in minutes, flown.
                AttitudeHook.Hold(Craft, _deploy.DirectionCci, _deploy.RollCci,
                                  loosely: Config.HoldPointsLoosely && Program.Phase == IcbmPhase.Holding);
                aimed = AttitudeHook.Installed;
                if (aimed) _driving = true;
            }
        }
        else
        {
            AttitudeHook.Release(Craft);
            if (drifting)
            {
                AttitudeHook.Drift(Craft);
                _driving = false;
            }
            else
            {
                AttitudeHook.StopDrifting(Craft);
            }
        }

        // The direction handed to the hook, which the release sequence turns off the nominal by a cant.
        ProbeAttitude(playerStep, aimed ? _deploy.DirectionCci : Command.ThrustDirectionCci,
                      wasMode, wasTrack, aimed);

        if (Command.EngineOn)
        {
            _throttleAchieved = VehicleCommand.DriveThrottle(Craft, Command.Throttle, Config.ThrottleThroughTheKeyboardClear);

            // A held control the engine drops reads exactly like a throttle on its way down, until the
            // airframe comes apart. The gap is past the servo's own tolerance, so a settled throttle is quiet.
            if (!_saidHeldControlsDiscarded && Math.Abs(_throttleAchieved - Math.Max(Command.Throttle, Craft.GetMinThrottle())) > 0.05
                && KsaWorld.DiscardsHeldControls(Craft, out string discarded))
            {
                _saidHeldControlsDiscarded = true;
                Log.Warn($"{KsaWorld.DisplayName(Craft)}: KSA is discarding the throttle this computer holds -- "
                         + $"{discarded}, so it stays at {_throttleAchieved:F3} against {Command.Throttle:F3}, "
                         + "and the trim's thrusters are dropped the same way");
            }

            StructuralLoad load = Craft.StructuralLoad;

            // KSA destroys a vehicle the moment this fraction reaches one; nothing else logs how near.
            if (!_saidOverLimit && load.GLoadFraction >= OverLimitWarnFraction)
            {
                _saidOverLimit = true;
                Log.Info($"{KsaWorld.DisplayName(Craft)} is pulling {load.PeakGLoad:F1} g of its "
                         + $"{load.MaxGLoad:F1} g limit at {_throttleAchieved:F2} throttle");
            }

            // Every frame of a short shot's last two seconds, which the half-second probe cannot resolve.
            if (Log.Threshold <= Log.Level.Debug && Program.Phase == IcbmPhase.ClosedLoop
                && double.IsFinite(Program.DragMissMetres) && Program.Countdown < 2.0)
            {
                Log.Debug($"cutoff approach on {KsaWorld.DisplayName(Craft)}: countdown {Program.Countdown:F3} s, "
                          + $"to gain {Program.VelocityToGain:F2} m/s, achieved {_throttleAchieved:F3}, "
                          + $"line {(Program.LineSlowed ? "slowed" : "followed")}, drag offset {Program.DragOffsetMetres:F0} m, "
                          + $"drag-flown miss {Program.DragMissMetres:F0} m, alt {state.Altitude / 1000.0:F2} km, climbing "
                          + $"{Vec.Dot(state.AirflowCci, state.UpCci):F1} m/s, arrival committed in {Program.CommittedArrivalFromNow:F1} s");
            }

            _sinceThrottleProbe += playerStep;
            if (Log.Threshold <= Log.Level.Debug && _sinceThrottleProbe >= ProbeIntervalSeconds)
            {
                _sinceThrottleProbe = 0.0;
                BoosterPerformance booster = Program.LastBooster;

                Log.Debug($"throttle: asked {Command.Throttle:F3}, achieved {_throttleAchieved:F3}"
                          + $" | full-throttle {booster.AccelerationNow / 9.80665:F2} g, "
                          + $"load {load.PeakGLoad:F2} of {load.MaxGLoad:F1} g"
                          + $" (thrust {booster.ThrustNewtons / 1000.0:F0} kN, "
                          + $"mass {booster.TotalMassKg / 1000.0:F1} t) | stage dv {RunningStageDeltaV():F0} m/s, "
                          + $"{(KsaWorld.RunningEnginesCanStop(Craft) ? "can stop" : "solid")} | "
                          + $"to gain {Program.VelocityToGain:F2} m/s, countdown {Program.Countdown:F3} s, "
                          + $"held below {Program.HoldDirectionBelowNow:F2} m/s"
                          + (Program.LineSlowed ? ", line slowed" : "")
                          + (double.IsFinite(Program.DragMissMetres)
                                 ? $", drag offset {Program.DragOffsetMetres:F0} m, drag-flown miss {Program.DragMissMetres:F0} m"
                                 : "")
                          + $" | {Program.Phase}, alt {state.Altitude / 1000.0:F2} km, climbing "
                          + $"{Vec.Dot(state.AirflowCci, state.UpCci):F1} m/s, airspeed {Vec.Len(state.AirflowCci):F0} m/s, "
                          + $"q {state.DynamicPressurePa:F0} Pa");
            }
            if (Program.LineSlowed && !_saidLineSlowed)
            {
                _saidLineSlowed = true;
                Log.Info($"{KsaWorld.DisplayName(Craft)} ICBM: slowing the thrust line with "
                         + $"{Program.VelocityToGain:F1} m/s to gain, turning {Diagnostics.SpinDegPerSec(Craft):F1} deg/s");
            }

            VehicleCommand.SetEngine(Craft, running: true);

            // Never past the launcher: a shot that ran dry would otherwise drop its rounds.
            if (Command.RequestStage)
            {
                if (!StagingWouldDropTheLauncher(release))
                {
                    Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} staging: {Command.Hold}");

                    // Before the call: the stage lands frames later, and the difference names what came off.
                    _wasBeforeStage.Clear();
                    KsaWorld.CollectVehicles(_wasBeforeStage);
                    _awaitingStage = true;

                    Log.Info($"staging probe on {KsaWorld.DisplayName(Craft)}: before -- "
                             + $"{Diagnostics.DescribeEngines(Craft)}, turning "
                             + $"{Diagnostics.SpinDegPerSec(Craft):F1} deg/s");
                    _sinceStaging = 0.0;
                    _stagingProbe = 0;
                    _gapStage = null;

                    AttitudeHook.Stage(Craft);
                }
                else if (Config.MovesAnEarlyRelease && !_movedRelease && release is { } early)
                {
                    // Once: a move the list refuses would otherwise be asked again every second and a half.
                    _movedRelease = true;
                    string craft = KsaWorld.DisplayName(Craft);
                    AttitudeHook.StageAfter(Craft, () =>
                    {
                        bool moved = early.MoveReleaseLast(out string why);
                        Log.Info($"ICBM computer on {craft} wants a stage that would drop its launcher: {why}"
                                 + (moved ? "; staging on" : "; it will not fire it -- stage it by hand"));
                        _saidRefusedStage = !moved;
                        return moved;
                    });
                }
                else if (!_saidRefusedStage && !Config.MovesAnEarlyRelease)
                {
                    // Otherwise silent, and on the pad it is the launch not happening.
                    _saidRefusedStage = true;
                    Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} wants a stage and will "
                             + "not fire one that separates its own launcher; stage it by hand -- "
                             + Diagnostics.DescribeEngines(Craft));
                }
            }
        }
        else if (_driving)
        {
            VehicleCommand.SetEngine(Craft, running: _engineLit);
            _throttleAchieved = VehicleCommand.DriveThrottle(Craft, Command.Pulsing || Command.HoldsLever ? Command.Throttle : 1.0,
                                                             Config.ThrottleThroughTheKeyboardClear);
        }

        if (Config.AutoRelease && _deploy.ReleaseNow && Release(release) && ReleasesTogether)
        {
            while (release is { ReadyToFire: true } && Release(release)) { }
        }

        // After the release, so a stop whose last warhead went this frame hands over this frame.
        StepTheWalk(release);

        CarryOurWarp();
        CarryTheView();
    }

    /// <summary>
    /// Follow the weapon onto the craft that now carries it, after a decoupler split the stack. The
    /// flight continues: a fresh computer would re-enter at <see cref="IcbmPhase.Holding"/> and never
    /// reach the <c>Coast</c> that sets <c>ReadyToDeploy</c>.
    /// </summary>
    public void Rehome(Vehicle craft)
    {
        if (!KsaWorld.IsAlive(craft) || ReferenceEquals(craft, Craft)) return;

        Vehicle left = Craft;

        // The hook is keyed on the vehicle, so otherwise the spent stack is held for the session.
        AttitudeHook.Release(left);

        if (_driving)
        {
            VehicleCommand.SetEngine(left, running: false);
            VehicleCommand.ReleaseAttitude(left);
        }

        // Unconditionally: the trim may already have commanded the half left behind, and a thruster
        // flag is a held key.
        VehicleCommand.DriveTranslation(left, TrimAxes.None);
        AttitudeHook.PulseMode(left, pulsing: false);

        Craft = craft;

        Log.Info($"ICBM computer followed its weapon from {KsaWorld.DisplayName(left)} "
                 + $"onto {KsaWorld.DisplayName(craft)}");

        // Only if the player was watching what split; deferred, because a handover is decided during
        // the panel's pass and moving the view then pulls the craft from under it.
        if (KsaWorld.IsWatching(left)) _viewWanted = craft;

        // A live stack, held for the coast to measure from; ForgetTheFlight clears it.
        _separatedFrom = left;

        // The first clearance report came from the half being left.
        _saidClearOnce = false;
    }

    // Deferred out of Rehome, which runs in the panel's pass (Ui calls IcbmComputers.Sync).
    private void CarryTheView()
    {
        if (_viewWanted is not { } craft) return;
        _viewWanted = null;

        if (!KsaWorld.IsAlive(craft)) return;

        if (KsaWorld.GoTo(craft))
        {
            Log.Info($"view moved to {KsaWorld.DisplayName(craft)}, which is where the warheads are");
        }
    }

    // A cutoff resolves to one frame, so a long step is accel x step of velocity nobody asked for;
    // the rounds' overrun report is at Debug with nothing in the air.
    private void ReportLongStep(bool burning, double simStep, in IcbmState state)
    {
        if (!burning || _saidLongStep) return;
        if (!double.IsFinite(simStep) || simStep <= IcbmProgram.MaxFaithfulStep) return;

        _saidLongStep = true;

        Log.Warn($"{KsaWorld.DisplayName(Craft)} burn flown across a {simStep * 1000.0:F0} ms step, "
                 + $"over the {IcbmProgram.MaxFaithfulStep * 1000.0:F0} ms a cutoff can resolve -- "
                 + $"about {state.Booster.AccelerationNow * simStep:F0} m/s in that one frame");
    }

    // Frames read after the cutoff command. One frame of command latency is two frames of reading.
    private const int CutoffTailFrames = 4;

    private int _cutoffTailFrame = -1;
    private double3 _tailVelocityCci;
    private double3 _tailPositionCci;
    private double3 _tailLineCci;

    // What the engine still pushed after the cutoff was commanded, frame by frame, along the line the burn ended on:
    // an extra frame of thrust here is the long-range split debt's overshoot (ACCURACY-PLAN 3fp).
    private void WatchTheCutoffTail(bool wasBurning, double simStep, in IcbmState state)
    {
        if (Program.IsBurning)
        {
            _cutoffTailFrame = 0;
            _tailVelocityCci = state.VelocityCci;
            _tailPositionCci = state.PositionCci;
            _tailLineCci = Command.ThrustDirectionCci;
            return;
        }

        if (_cutoffTailFrame < 0 || _cutoffTailFrame >= CutoffTailFrames || Program.Phase != IcbmPhase.Coast) return;
        if (!(simStep > 0.0) || _tailLineCci.Equals(Vec.Zero)) return;

        double3 gravity = state.Body.GravityCci((state.PositionCci + _tailPositionCci) * 0.5);
        double3 felt = state.VelocityCci - _tailVelocityCci - gravity * simStep;
        double along = Vec.Dot(felt, Vec.Unit(_tailLineCci));
        double floorFrame = Program.AccelerationAtCutoff * simStep * Program.ThrottleAtCutoff;

        Log.Info($"cutoff tail on {KsaWorld.DisplayName(Craft)}: frame {_cutoffTailFrame}"
                 + $"{(wasBurning ? " (the command's)" : "")}, {simStep * 1000.0:F1} ms, "
                 + $"{along:+0.000;-0.000;0.000} m/s along the line and {Vec.Len(felt - Vec.Unit(_tailLineCci) * along):F3} off it, "
                 + $"one frame at the cutoff throttle is {floorFrame:F3}, engine reports {state.ThrottleAchieved:F3}");

        _cutoffTailFrame++;
        _tailVelocityCci = state.VelocityCci;
        _tailPositionCci = state.PositionCci;
    }

    // How near the airframe's limit is worth a line in the log.
    private const double OverLimitWarnFraction = 0.85;

    // Tells a swinging command from a vehicle that cannot hold a steady one, which look alike and want
    // opposite fixes. Through the coast too, where the inertia has collapsed with the stack.
    private void ProbeAttitude(double playerStep, double3 commandedCci,
                               FlightComputerAttitudeMode wasMode,
                               FlightComputerAttitudeTrackTarget wasTrack, bool aimed)
    {
        if (Command.Phase is IcbmPhase.Idle or IcbmPhase.NoSolution) return;
        if (Log.Threshold > Log.Level.Debug) return;

        _sinceProbe += playerStep;
        if (_sinceProbe < ProbeIntervalSeconds) return;
        _sinceProbe = 0.0;

        double3 wanted = Vec.Unit(commandedCci);
        double slew = _lastCommanded.Equals(Vec.Zero) || wanted.Equals(Vec.Zero)
                          ? 0.0
                          : Vec.AngleBetween(_lastCommanded, wanted) * 180.0 / Math.PI;
        _lastCommanded = wanted;

        FlightComputer computer = Craft.FlightComputer;

        Log.Debug($"{KsaWorld.DisplayName(Craft)} attitude: aimed={aimed} "
                  + $"dir={(wanted.Equals(Vec.Zero) ? "ZERO" : "set")} "
                  + $"slew {slew:F1} deg | before {wasMode}/{wasTrack} "
                  + $"-> after {computer.AttitudeMode}/{computer.AttitudeTrackTarget} | "
                  + $"error {computer.ErrorAngles} rates {computer.ErrorRates}");

        ProbeControlLimits(computer);
    }

    // KSA's RCS tracker crawls at half a rate bit inside 0.5*AngleDeadband + AngleTurnaround and
    // latches there; dropping a stack widens that by the mass ratio (docs/MIRV-NEXT.md item 5).
    private void ProbeControlLimits(FlightComputer computer)
    {
        double band = 0.5 * computer.AngleDeadband
                      + Math.Max(computer.AngleTurnaround.Y, computer.AngleTurnaround.Z);

        Log.Debug($"{KsaWorld.DisplayName(Craft)} control: "
                  + $"{computer.ActiveControlSystem.X}/{computer.ActiveControlSystem.Y}/"
                  + $"{computer.ActiveControlSystem.Z}, roll {computer.RollMode}, "
                  + $"control part {(Craft.ControlPart is null ? "NONE" : "held")} | "
                  + $"deadband {Degrees(computer.AngleDeadband):F2} deg, turnaround "
                  + $"{Degrees(computer.AngleTurnaround.Y):F2}/{Degrees(computer.AngleTurnaround.Z):F2} deg, "
                  + $"rate bit {Degrees(computer.RateBit.Y):F3}/{Degrees(computer.RateBit.Z):F3} deg/s | "
                  + $"pointing band {Degrees(band):F2} deg");
    }

    private static double Degrees(double radians) => radians * 180.0 / Math.PI;

    // What the bus's own jets have left: the turn onto a burn is flown on them, and so is the trim after the split.
    private string BusJetsSaid()
    {
        if (LauncherPart.Find(Craft) is not { } launcher || !KsaWorld.TryTankOf(launcher.Part, out Tank? tank)
            || !KsaWorld.TryTankMassKg(tank, out double kg))
        {
            return "";
        }
        return $", bus jets {kg:F1} kg";
    }

    // A roll reference through a vertical rise: downrange, or before there is one the velocity.
    private double3 RollFallback(in IcbmState state)
        => Program.DownrangeCci.Equals(Vec.Zero) ? state.VelocityCci : Program.DownrangeCci;

    /// <summary>For drawing; empty until a prediction has run.</summary>
    public void PathEcl(List<double3> into)
    {
        into.Clear();
        if (Parent is null) return;

        doubleQuat cci2Cce = Parent.GetCci2Cce();
        double3 centre = Parent.GetPositionEcl();

        for (int i = 0; i < _path.Count; i++) into.Add(_path[i].Transform(cci2Cce) + centre);
    }

    /// <summary>On the real ground, now. Null when nothing is designated.</summary>
    public double3? TargetEcl() => SiteEcl(Target);

    public double3? SiteEcl(AimSite site)
    {
        if (Parent is not { } parent || !site.IsSet) return null;
        return SurfacePointEcl(parent, site.LatitudeDeg, site.LongitudeDeg);
    }

    private IcbmState Sample(double playerStep, out bool usable)
    {
        usable = false;

        Parent = KsaWorld.ParentBody(Craft);
        if (Parent is null) return default;

        double mu = Parent.Mass * GravitationalConstant;

        // The spin axis is exactly +Z in Cci: KSA builds Ccf from Cci by a rotation about UnitZ alone.
        Body = new BallisticBody(mu, Parent.MeanRadius, new double3(0, 0, 1), Parent.GetAngularVelocity());

        doubleQuat cce2Cci = Parent.GetCce2Cci();
        double3 positionCci = (KsaWorld.PositionEcl(Craft) - Parent.GetPositionEcl()).Transform(cce2Cci);
        double3 velocityCci = (KsaWorld.VelocityEcl(Craft) - Parent.GetVelocityEcl()).Transform(cce2Cci);

        double3 aimCci = default;
        bool hasAim = false;

        if (Target.IsSet && Target.BodyName == Parent.Id)
        {
            _trueAimCci = (SurfacePointEcl(Parent, Target.LatitudeDeg, Target.LongitudeDeg)
                           - Parent.GetPositionEcl()).Transform(cce2Cci);

            // Plus what the flown prediction says the arc loses: the solver is exact for a point in
            // vacuum, and the round stops on the ground through the air.
            aimCci = _aim.Apply(_trueAimCci);
            hasAim = true;
        }

        FlightComputer computer = Craft.FlightComputer;
        ActiveEnginePerformance engines = computer.ActiveEnginePerformanceMax;

        BoosterPerformance booster = new(engines.Thrust, engines.MassFlowRate,
                                         Craft.TotalMass, Craft.PropellantMass);

        double density = KsaWorld.MediumDensityRatioAt(Parent, KsaWorld.PositionEcl(Craft));

        usable = Body.IsUsable;
        AltitudeMetres = Body.AltitudeOf(positionCci);

        if (hasAim)
        {
            double off = OrbitPlane.OffPlaneRadians(positionCci, velocityCci, aimCci);
            OffPlaneDegrees = off * 180.0 / Math.PI;
            PlaneChangeCost = OrbitPlane.PlaneChangeCost(Vec.Len(velocityCci), off);
        }
        else
        {
            OffPlaneDegrees = 0.0;
            PlaneChangeCost = 0.0;
        }

        double3 noseCci = KsaWorld.TryControlFrameCci(Craft, Parent, out double3 nose, out _, out _) ? nose : default;

        return new IcbmState(Body, positionCci, velocityCci, aimCci, hasAim, booster, density,
                             Craft.IsAnyEnginePropellantAvailable(),
                             KsaWorld.OnlySolidsRunning(Craft) ? 1.0 : _throttleAchieved, playerStep,
                             _aim.IsSteady, StackDeltaV(), StructuralLimitGee(),
                             KsaWorld.RunningEnginesCanStop(Craft), engines.MinThrottle, noseCci,
                             RunningStageDeltaV(), _densityRatio ??= DensityRatioAt, _warhead,
                             ReleaseOffsetCci(), ReleaseImpulseCci(), KsaWorld.OnlySolidsRunning(Craft),
                             AirframeLimitGee > 0.0 ? Craft.StructuralLoad.PeakGLoad : double.NaN);
    }

    /// <summary>What the engine destroys this airframe at, in g, or zero if it has not said.</summary>
    public double AirframeLimitGee
    {
        get
        {
            double limit = Craft.StructuralLoad.MaxGLoad;
            return double.IsFinite(limit) && limit > 0.0 ? limit : 0.0;
        }
    }

    // Zero outside a physics bubble, where the struct was never filled in: absent, not unlimited.
    private double StructuralLimitGee()
    {
        double limit = AirframeLimitGee;
        if (limit <= 0.0) return 0.0;

        if (!_saidStructuralLimit)
        {
            _saidStructuralLimit = true;
            string asked = Config.MaxAccelerationGee > 0.0f
                ? $" or the {Config.MaxAccelerationGee:F1} g asked for, whichever is less"
                : "";

            Log.Info($"{KsaWorld.DisplayName(Craft)} airframe is destroyed at {limit:F1} g; "
                     + $"holding it to {limit * IcbmProgram.StructuralMarginFraction:F1} g{asked}");
        }

        return limit;
    }

    // While reported solids burn, a long shot's arc is held by what they must deliver, not by the aim: read there at
    // 0.07-0.85 the loop walked its bias 15 km. KSA reports a stage's delta-v for the controlled craft alone.
    private bool AimWaitsForTheSolids(in IcbmState state)
        => Config.AimWaitsForTheSolids && Program.Phase == IcbmPhase.PitchProgram && !Program.IsShortShot
           && !state.RunningStageCanStop && state.RunningStageDeltaV > 0.0;

    private double RunningStageDeltaV()
    {
        try
        {
            float stage = Craft.Parts.PerformanceSequences.FindActiveSequenceDeltaV();
            return float.IsFinite(stage) && stage >= 0.0f ? stage : double.NaN;
        }
        catch
        {
            return double.NaN;
        }
    }

    // KSA's staging display, the only figure that accounts for staging. NaN puts the single-stage
    // estimate back rather than claiming the stack has nothing.
    private double StackDeltaV()
    {
        try
        {
            float total = Craft.Parts.PerformanceSequences.TotalDeltaV;
            return total > 0.0f && float.IsFinite(total) ? total : double.NaN;
        }
        catch (Exception e)
        {
            Log.Error("could not read the stack's delta-v", e);
            return double.NaN;
        }
    }

    // The round stops on terrain, and a shallow deorbit covers about twelve km of ground per km of
    // height. The point is in the prediction's epoch, mid-burn the cutoff, so it is carried to now.
    private double TerrainRadiusAt(double3 pointCci)
    {
        if (Parent is not { } parent) return Body.SurfaceRadius;

        try
        {
            // Only while burning: _departsIn is set at ~2 Hz and read stale at a cutoff release the
            // probe stood on ground turned away, 1.13 m low at 300 km.
            double carry = (Program.IsBurning ? _departsIn : 0.0) + _coastedToRelease;
            double3 nowCci = carry > 0.0 ? Body.CarryCci(pointCci, -carry) : pointCci;
            double3 dirCcf = Vec.Unit(nowCci).Transform(parent.GetCci2Ccf());
            if (!Vec.IsFinite(dirCcf) || dirCcf.Equals(Vec.Zero)) return Body.SurfaceRadius;

            // Accurate, as GroundTest is: a coarse sample is a different height field. ImpactPredictor
            // asks only near the surface.
            double height = SurfaceHeight(parent, parent.GetTerrainHeightFromDirCcf(dirCcf, accurate: true));
            return double.IsFinite(height) ? parent.MeanRadius + height : Body.SurfaceRadius;
        }
        catch
        {
            return Body.SurfaceRadius;
        }
    }

    // On a deorbit 1 m/s left along track is ~1.8 km of miss and radially ~3.4 km. One frame of
    // burning is accel x step x throttle, and a residual near it is timing, not guidance.
    private string ResidualSaid()
    {
        double3 leftover = Program.ResidualVectorCci;
        if (leftover.Equals(Vec.Zero) || !Vec.IsFinite(leftover)) return "";

        double3 up = Vec.Unit(Program.CutoffPositionCci);
        double3 along = Vec.Unit(Vec.Cross(Vec.Cross(up, Program.Arc?.RequiredVelocityCci ?? up), up));

        if (along.Equals(Vec.Zero)) return "";

        double radial = Vec.Dot(leftover, up);
        double track = Vec.Dot(leftover, along);
        double cross = Vec.Len(leftover - up * radial - along * track);

        // At the achieved throttle and at full, which a ramp that never arrives makes equal; both
        // factors, since a long frame and an absurd acceleration want opposite fixes.
        double full = Program.AccelerationAtCutoff * Program.StepAtCutoff;
        double achieved = Program.ThrottleAtCutoff;

        return $" ({track:F2} along, {radial:F2} radial, {cross:F2} cross"
               + $"; one frame is {full * (double.IsFinite(achieved) ? achieved : 1.0):F3} m/s at "
               + $"{achieved:P0} throttle, {full:F2} at full"
               + $" = {Program.AccelerationAtCutoff:F1} m/s2 x {Program.StepAtCutoff * 1000.0:F0} ms"
               + $"; longest step of the burn {Program.LongestStepWhileBurning * 1000.0:F0} ms)";
    }

    // The round's own clamp: the height field reports the seabed, 3,776 m deep on average, which on
    // a seven-degree arrival is about 35 km of ground the prediction would agree with.
    private static double SurfaceHeight(Celestial body, double terrainHeight)
    {
        try
        {
            return body.GetOceanReference() is { } sea && sea.Density > 0.0
                       ? GroundSurface.Height(terrainHeight, sea.Level, hasSea: true)
                       : terrainHeight;
        }
        catch
        {
            return terrainHeight;
        }
    }

    // The air alone: the medium reads ocean anywhere under the mean sphere, and under 204 m of ground
    // the air step read 837x the air there (docs/ACCURACY-PLAN.md 3ep).
    private double DensityRatioAt(double3 pointCci)
    {
        if (Parent is not { } parent) return 0.0;

        try
        {
            double3 positionEcl = pointCci.Transform(parent.GetCci2Cce()) + parent.GetPositionEcl();
            double density = KsaWorld.AirDensityRatioAt(parent, positionEcl);
            return double.IsFinite(density) && density > 0.0 ? density : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    // On the real ground: the solve is a transfer between two points, so a raised target is hit by
    // aiming where it stands.
    private static double3 SurfacePointEcl(Celestial body, double latitudeDeg, double longitudeDeg)
    {
        double3 dirCcf = body.GetDirCcfFromLatLon(latitudeDeg, longitudeDeg);
        double height = SurfaceHeight(body, body.GetTerrainHeightFromDirCcf(dirCcf, accurate: true));
        return dirCcf.Transform(body.GetCcf2Cce()) * (body.MeanRadius + height) + body.GetPositionEcl();
    }
}
