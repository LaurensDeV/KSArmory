using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// One craft's ICBM computer: it reads the world, runs <see cref="IcbmProgram"/> and flies the
/// rocket to a place on a map.
///
/// <para>Everything that decides anything is in <c>Sim/</c> and tested there. What is here is the
/// two conversions that cannot be: the world into a <see cref="IcbmState"/>, and the program's
/// answer into writes on somebody else's vehicle.</para>
///
/// <para><b>Both conversions are into the parent body's inertial frame</b>, not the ecliptic. A
/// half-hour ballistic flight carries 54 million kilometres of the planet's own travel through
/// every ecliptic term, and a solve differencing two of them across even a fraction of a step leaks
/// a piece of it. Working in Cci removes the carrier exactly rather than approximately, because it
/// is the frame the engine's own orbital mechanics are written in.
/// See <c>docs/FRAMES-AND-EPOCHS.md</c>.</para>
/// </summary>
internal sealed partial class IcbmComputer
{
    // KSA's own value, so an orbit solved here and one drawn by the game agree.
    private const double GravitationalConstant = 6.6743e-11;

    // How often the impact prediction is re-flown while guidance is consuming it, in *simulated*
    // seconds -- the engines are lit or a post-boost pass has asked for a reading, and WarpPolicy
    // holds the world down through both, so a simulated interval is a real one here.
    private const double PredictIntervalSeconds = 0.5;

    // Every other computer being integrated this frame, refreshed by the roster before any of them
    // steps. Empty when nobody handed one in, which is every path but the roster's.
    private IReadOnlyList<IcbmComputer> _busyElsewhere = [];

    // Simulated seconds since the aim was last read. The reading cadence is gated by _measureDue
    // rather than by PredictIntervalSeconds, so it is not the half-second the interval nominally is
    // and every rate quoted per reading needs it.
    private double _sinceObserve;

    // How often it is re-flown when nothing but the readout wants it, in REAL seconds. Paced by
    // simulated time it runs every frame at warp, and each pass re-flies a whole trajectory at
    // PredictStepSeconds with a terrain lookup per step -- so on a warped coast, which is most of a
    // flight, that is a full re-flight per rocket per frame for a line on an overlay.
    //
    // The same trap IcbmState.PlayerStepSeconds exists for and describes in as many words: a
    // computation budget is paced by the wall clock.
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
    private bool _trimAbandoned;
    private double _departsIn;
    private ReleaseCommand _deploy;
    private bool _awaitingSplit;
    private bool _didSplit;
    private bool _mayTrim = true;
    private bool _saidBudget;


    private bool _saidFloorUnaffordable;
    private bool _saidRefusedStage;
    private bool _saidStructuralLimit;
    private bool _saidLongStep;
    private bool _saidOverLimit;
    private bool _saidHeldControlsDiscarded;

    private double _owedAtSplit = double.NaN;
    private Vehicle? _separatedFrom;
    private readonly List<Vehicle> _wasBeforeSplit = [];
    private readonly List<Vehicle> _afterSplit = [];

    // Everything this vehicle has shed, and the census that finds it. Same difference-of-worlds
    // trick as WhatWasDropped, run at every staging rather than only at the split, because the
    // ascent stages are three of the four vehicles a rocket leaves behind.
    private readonly List<Vehicle> _shed = [];
    private readonly List<Vehicle> _wasBeforeStage = [];
    private readonly List<Vehicle> _afterStage = [];
    private bool _awaitingStage;

    // The craft's engines and parts read either side of every staging, and any part it loses outside
    // one. A part KSA destroys by contact leaves no line anywhere else, and a stage that lit reads
    // exactly like one whose engine was knocked off until the next stage is asked for.
    private static readonly double[] StagingProbeAt = [0.0, 0.25, 1.0, 2.0];
    private const double StagingWindowSeconds = 2.5;
    private readonly Diagnostics.PartWatch _partWatch = new();
    private readonly List<string> _lostParts = [];
    private double _sinceStaging = double.NaN;
    private bool _saidLineSlowed;
    private int _stagingProbe;

    // The session's own settings, as opposed to this installation's. Only the disposal switch is
    // read from it: what a stage costs the frame is a property of the world, not of one shot.
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

    // One per released warhead rather than one per shot. Six traces on one rocket is what
    // separates the walk from the within-rocket landing deviation: they measure 23.6 mm and
    // 21.2 mm and might be one term, and with a single trace per rocket the second can only be
    // asked through the aim-point miss -- which is a good proxy across the track (r = 0.87) and a
    // bad one along it (r = 0.13). docs/ACCURACY-PLAN.md 3ee.
    //
    // It costs a third of the log and well under one RK4 step a frame: the re-fly is the expensive
    // half and it is rationed to one every ten seconds, so 48 concurrent traces in an eight-rocket
    // world do not touch the frame time -- which matters, because frame time is the one covariate
    // the walk actually tracks and an instrument that moved it would be measuring itself.
    private readonly List<WarheadTrace> _traces = [];

    private readonly SalvoProbe _salvoProbe = new();
    private bool _traceWanted;

    // The salvo's kick columns flown through the air, for IcbmConfig.KickThroughTheAir, and the drag they were
    // flown with: a warhead swapped to another drag between releases is a different flight.
    private ReleaseFocus.FlownSensitivity? _flownKick;
    private double _flownKickDragK = double.NaN;

    // The reach on the ground, the landing it is centred on and the rotation that held between the
    // body's frames when it was flown. Body-fixed, because the columns' own axes belong to the
    // instant they departed from and five seconds of Earth's spin is 2.3 km of ground -- which on a
    // ring a few kilometres across is the whole shape.
    private DivertFootprint? _reachFootprint;
    private double3 _reachLandingCcf;
    private doubleQuat _reachCci2Ccf = doubleQuat.Identity;
    private doubleQuat _reachCcf2Cci = doubleQuat.Identity;
    private double _sinceReachWall = double.PositiveInfinity;

    // How often the reach is re-flown, in REAL seconds, for the reason ReadoutIntervalSeconds is:
    // it is a display, and paced by simulated time it re-flies every frame on a warped coast.
    // Seven flights of the predictor a time, against the readout's one -- and the thing it measures
    // decays over hundreds of seconds (1,076 to 886 m per m/s over a 330 s schedule), so five
    // seconds is a third of a per cent stale.
    private const double ReachIntervalSeconds = 5.0;

    /// <summary>
    /// A warhead is being followed and has not reported yet. The trace reports from a poll one
    /// frame after the round stops flying, so a harness that ends the run on the last impact ends
    /// it before the report — see <see cref="ScenarioRunner"/>.
    /// </summary>
    public bool TraceOutstanding => _traceWanted && _traces.Any(t => t.Watching);
    private MunitionProfile? _tracedWarhead;
    private bool _saidTraceStranded;

    // Cached rather than converted at each call site: a method group becomes a delegate by
    // allocating one, and the trace builds its Setup on every frame of a four-hundred-second fall.
    private Func<double3, double>? _terrainRadius;
    private Func<double3, double>? _densityRatio;

    // Often enough to see an oscillation, rare enough not to fill the log with a burn's worth.
    private const double ProbeIntervalSeconds = 0.5;

    private double _throttleAchieved = 1.0;

    public Vehicle Craft { get; private set; }

    public IcbmConfig Config { get; }

    public IcbmProgram Program { get; }

    /// <summary>
    /// Where it has been told to put the warheads. Nothing happens until this is set.
    ///
    /// <para>The lead of <see cref="Targets"/>, which the set decides rather than the order the
    /// player clicked in. Everything downstream — the aim, the overlay, the trace, the scoring —
    /// reads this and nothing else about the list, so a list of one target is the whole of a
    /// single-target shot.</para>
    /// </summary>
    public AimSite Target => _targets.Primary;

    /// <summary>Everywhere the warheads are going, in the order they were chosen.</summary>
    public IReadOnlyList<TargetSet.Entry> Targets => _targets.Entries;

    /// <summary>
    /// The ground this bus can still put a warhead on, as the overlay draws it, the cursor is
    /// refused against and the panel reads it.
    ///
    /// <para>Derived once a frame rather than per reader: three surfaces ask the same question and
    /// a cursor refused where the ring shows room reads as the tool being broken.</para>
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

    /// <summary>The last command issued, which is what every readout on the panel is describing.</summary>
    public IcbmCommand Command { get; private set; }

    /// <summary>
    /// Where the vehicle would land, flown from <see cref="PredictsFromCutoff"/>'s departure. Null
    /// when it would not.
    /// </summary>
    public ImpactPredictor.Impact? PredictedImpact { get; private set; }

    /// <summary>
    /// The prediction departs from the burn's planned cutoff rather than from the vehicle as it is,
    /// so it says where the burn as planned lands, not where the rocket would fall if cut off now.
    /// </summary>
    public bool PredictsFromCutoff { get; private set; }

    /// <summary>How far the predicted impact is from the aim point, along the ground.</summary>
    public double PredictedMissMetres { get; private set; } = double.NaN;

    /// <summary>
    /// How many warheads have left, which is when the vehicle stops being the shot.
    ///
    /// <para>Everything this computer predicts is about the craft it is flying. Once a warhead is
    /// away it is on its own arc and the bus's is no longer an answer to anything — so a readout
    /// that goes on quoting it is describing a vehicle nobody is aiming any more.</para>
    /// </summary>
    public int WarheadsAway { get; private set; }

    /// <summary>
    /// Whether every warhead the bus started with has gone, so nothing is left to correct for.
    ///
    /// <para>False before the first release, because a salvo that has not started is not one that
    /// has finished.</para>
    ///
    /// <para><b>On a walk it means the walk is over, not that a warhead has left.</b> The magazine
    /// count cannot say: a walk that dropped a stop it could not pay for keeps its warheads aboard,
    /// so <see cref="WarheadsAway"/> never reaches the salvo's size and the trim would go on solving
    /// and firing at a bus with nothing left to release.</para>
    /// </summary>
    public bool SalvoFinished => _walker.Done || (_salvoSize > 0 && WarheadsAway >= _salvoSize);

    private int _salvoSize;

    /// <summary>
    /// The warhead aboard, or null for a vehicle carrying nothing that lets go. What the overlay
    /// sizes its aim ring from, so the circle on the ground is what one of these actually reaches.
    /// </summary>
    public MunitionProfile? Munition => _warhead;

    /// <summary>
    /// How far the aim has been moved to make the flown arc arrive, in metres.
    ///
    /// <para>Worth reading beside the predicted miss rather than on its own: the correction is
    /// clamped, so the pair says whether a miss is one the loop has not finished removing or one it
    /// has run out of room to remove. At the clamp they stop being independent.</para>
    /// </summary>
    public double AimBiasMetres => Vec.Len(_aim.BiasCci);

    /// <summary>The body the flight is around, as the guidance sees it.</summary>
    public BallisticBody Body { get; private set; }

    /// <summary>Height over the mean sphere, for readouts that mean nothing on the ground.</summary>
    public double AltitudeMetres { get; private set; }

    /// <summary>How far off the plane the vehicle is flying in the target sits, in degrees.</summary>
    public double OffPlaneDegrees { get; private set; }

    /// <summary>Roughly what turning the orbit that far would cost on its own.</summary>
    public double PlaneChangeCost { get; private set; }

    /// <summary>
    /// Seconds until the warheads arrive, from now.
    ///
    /// <para>Taken from the flown prediction wherever there is one, and from the plan only before
    /// there is. The two disagree while the burn is still running — the plan assumes it finishes,
    /// the prediction assumes it stops now — and the plan is the honest answer to "when will this
    /// arrive" right up until the engines quit.</para>
    /// </summary>
    public double SecondsToArrival
        => Program.IsBurning || Program.Phase == IcbmPhase.Holding
               ? Program.SecondsToArrival
               : ArrivalFromTheLastPrediction();

    // Aged by the time since the prediction was made, which is what makes it a countdown rather
    // than a snapshot. ImpactPredictor answers "this many seconds from the state it was given", and
    // that state is up to PredictIntervalSeconds old -- so read raw it sits still between solves and
    // freezes at whatever it last said if solving stops, which is a timer that never reaches zero.
    private double ArrivalFromTheLastPrediction()
    {
        return _arrivalLeft > 0.0 ? _arrivalLeft : double.NaN;
    }

    /// <summary>
    /// Warheads are down there somewhere and have not landed yet.
    ///
    /// <para>True across the craft's own death, which is the point of it: a bus has no heat shield
    /// and its warheads do, so it breaks up on reentry five to twenty seconds before they arrive.
    /// Anything that describes where they are going has to outlive it — the same rule the mod
    /// already applies to a fired round, which does not belong to its launcher any more.</para>
    /// </summary>
    public bool SalvoStillArriving => _salvoAway && double.IsFinite(SecondsToArrival);

    /// <summary>
    /// The salvo has gone and everything in it has arrived, so there is nothing left on its way to
    /// the aim point.
    ///
    /// <para>False before a shot as well as during one — an aim point that has not been fired at
    /// yet is exactly what a mark is for. It is only once the warheads are down that the mark has
    /// no referent.</para>
    /// </summary>
    public bool SalvoHasLanded => _salvoAway && !double.IsFinite(SecondsToArrival);

    private double _arrivalLeft = double.NaN;

    /// <summary>
    /// Whether the arrival time above is a forecast rather than a measurement.
    ///
    /// <para>It always is, and the distinction is worth drawing because the number reads like a
    /// countdown. During the coast it is <em>when a warhead released this instant would land</em>,
    /// and release actually waits for the bus to clear the stack and for the trim to finish — so it
    /// runs early by however long that takes. The mod says "if the engines stopped now" about the
    /// same kind of number elsewhere, and this is the same kind of number.</para>
    /// </summary>
    public bool ArrivalIsIfReleasedNow => !Program.IsBurning && Program.Phase != IcbmPhase.Holding;

    // Nothing left to release means nothing this bus does predicts an impact. Predict keeps flying
    // its live state with a modelled kick on it, which after the salvo describes a warhead that does
    // not exist -- the bus follows a similar arc and never goes off. Seen as a readout counting down
    // to an impact nothing was going to make, and as a predicted miss climbing past 500 km once the
    // real warheads were long down.
    private bool _salvoAway;

    // Handed back once the last warhead is away: nothing aboard is going anywhere, and holding an empty
    // bus's attitude fires its thrusters for the rest of the coast for nothing.
    private bool _letGoAfterSalvo;
    private bool _saidClearOnce;

    /// <summary>
    /// The walk this bus is on, and where in it. Holds no walk for every set of one — see
    /// <see cref="ReleaseWalker.Walking"/>.
    /// </summary>
    public ReleaseWalker Walk => _walker;

    private readonly ReleaseWalker _walker = new();

    // What the trim had spent when the current stop began, so a stop's own divert is a difference
    // rather than the flight's running total.
    private double _spentAtStopStart;

    // Which target each released warhead was sent to, by reference, for a harness scoring a walk.
    // At most one entry per warhead aboard, cleared with the flight.
    private readonly List<(IProjectile Round, int Target)> _sentTo = [];

    // What the walk actually delivered, for the line at the end of it.
    private readonly List<(int Target, string Site, int Warheads)> _wentTo = [];

    // The salvo is over rather than merely begun. Between a walk's stops rounds are in the air and
    // the bus is still being aimed, corrected and trimmed for the next one, so the latch that ends a
    // single-target flight is exactly the wrong question -- it would quiet the coast, stop the reach
    // being re-flown, and leave the operator looking at a bus that has stopped working.
    private bool SalvoIsOver => _salvoAway && !_walker.Walking;

    /// <summary>How far off its solution the bus still is, or NaN while nothing is trimming it.</summary>
    public double TrimToGainMetresPerSecond => _trim.Armed ? _trim.ToGainMetresPerSecond : double.NaN;

    /// <summary>
    /// What the trim is doing, or the residual it settled or gave up at. Empty before there is
    /// anything to say.
    ///
    /// <para>Not <see cref="BusTrim.Said"/>: most of the wait happens before the trim is even armed,
    /// while the bus coasts clear of the stack it dropped, and a readout that stays blank through it
    /// is indistinguishable from one that has stopped working. This is the last thing said about
    /// either, and it is the sentence rather than the shape the log de-duplicates on.</para>
    /// </summary>
    public string TrimSaid => _saidTrim;

    /// <summary>
    /// What the bus owed its solution the moment the decoupler fired, or NaN if it never split.
    ///
    /// <para>Beside <see cref="TrimOwedOnReleaseMetresPerSecond"/> this is the whole diagnosis of a
    /// wait that costs something: the same number twice means the wait was harmless and the error
    /// came off the separation, and a number that has grown means something moved the vehicle or
    /// the aim while it coasted clear.</para>
    /// </summary>
    public double TrimOwedAtSplitMetresPerSecond => _owedAtSplit;

    /// <summary>The other half of that pair — what it still owed when it was first allowed to push.</summary>
    public double TrimOwedOnReleaseMetresPerSecond => _trim.AtReleaseMetresPerSecond;

    /// <summary>
    /// What the launcher is being told to hold this frame, and whether a warhead may go.
    ///
    /// <para>Read for <see cref="ReleaseCommand.OffLineDegrees"/> on the frame a round leaves: how
    /// far off the salvo's own line that tube was pointing is what says whether re-pointing worked,
    /// and it is gone by the next frame, when the sequencer has moved on to the next tube.</para>
    /// </summary>
    public ReleaseCommand Deployment => _deploy;

    /// <summary>
    /// Whether the world has to be kept slow: the burn, the trim, and the aim measurement between
    /// them.
    ///
    /// <para>The trim stops on a frame boundary exactly as the burn does, so the velocity it leaves
    /// behind is what one step of its thrusters adds. At a tenth of a second that is centimetres a
    /// second and the whole point of doing it; at the steps high timewarp hands out it is metres a
    /// second, which is worse than never having trimmed at all.</para>
    ///
    /// <para><b>And the measurement between trims costs as much as the trims do.</b> Flown at
    /// 12,902 km, twelve shots alternating the coast warp on and off with every burn stepped at
    /// 33 ms either way: the warped arm's shot median is <b>8.25 km</b> against <b>0.62</b>, it wins
    /// none of the six adjacent pairs, and the share of flights inside a kilometre goes 33% to 62%.
    /// The burn is not what the warp was hurting — the correction gets fewer passes because there
    /// are fewer frames in the seconds it has.</para>
    ///
    /// <para>Bounded by <see cref="PostBoostAim.MaxSeconds"/>, so this protects about two minutes of
    /// a twenty-five minute coast and the rest still warps. Turning the coast warp off outright buys
    /// the same accuracy and costs the player the whole fall in real time.</para>
    /// </summary>
    public bool NeedsShortSteps => Program.NeedsShortSteps || TrimIsFiring || _postBoost.Correcting;

    // The span the aim correction has to sit out, which is only while thrusters are actually
    // moving the vehicle its observer reads.
    //
    // Not the clearance wait: the trim reads no aim, so the two loops cannot drive each other
    // there - and a correction frozen across a long wait is its own fault, since what it absorbs
    // is what the fall loses to drag and terrain and that changes as the release point descends.
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
    /// Aim at one place, and start the shot over.
    ///
    /// <para>A new aim point is a new flight, which is why everything below it is reset — and why
    /// this is the wrong call once a bus is coasting. <see cref="AddTarget"/> is the edit;
    /// <c>AimSite.None</c> here clears the whole list.</para>
    /// </summary>
    public void Designate(AimSite site)
    {
        _targets.SetOnly(site, WarheadsAboard);
        Program.Reset();
        _releasedTheArrival = false;
        _reported = IcbmPhase.Idle;
        _aim.Reset();
        _sequence.Reset();
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
        _trimAbandoned = false;
        _trimShape = "";
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
        WarheadsAway = 0;
        _salvoSize = 0;
        _owedAtSplit = double.NaN;
        _rollReference = Vec.Zero;
        PredictedImpact = null;
        PredictedMissMetres = double.NaN;
        _salvoAway = false;
        _letGoAfterSalvo = false;

        // A new aim point is a new shot, and the trace's walk is measured against an aim that has
        // just moved. Whatever is still in the air from the last one is dropped rather than scored
        // against the wrong target.
        _tracedWarhead = null;
        _saidTraceStranded = false;
        foreach (WarheadTrace trace in _traces) trace.Forget();
        _traces.Clear();
        _salvoProbe.Forget();
        _flownKick = null;
        _walker.Reset();
        _spentAtStopStart = 0.0;
        _sentTo.Clear();
        _wentTo.Clear();
        _saidWalk = "";

        Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} designated {site.Describe()}");
    }

    /// <summary>
    /// Add a place for the warheads to go, and touch nothing else.
    ///
    /// <para>Everything <see cref="Designate"/> resets belongs to the flight, and a bus half an hour
    /// into its coast has no way back from that — so an edit to the list is a different call rather
    /// than a guarded version of the same one.</para>
    /// </summary>
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

    // Aim the booster at whichever target is farthest downrange, while the aim is still free, and
    // return what to append to a log line.
    //
    // The lead is what the release schedule walks from and it has to be an END of the set: aimed
    // anywhere else the bus goes out and back and spends about twice what the itinerary charges.
    // Only before the arrival is committed -- after that the arc is pinned to an instant chosen for
    // somewhere else, and after cutoff there is no engine left to move it with.
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

    /// <summary>Drop a place the warheads were going to, which gives its warheads back to the spares.</summary>
    /// <remarks>
    /// Never once a walk is under way. Its stops name indexes into this list, so removing an entry
    /// sends the bus to whichever place slides into the gap — and the walk is committed by the first
    /// warhead leaving, which is also the point past which the list is a record of where they went.
    /// </remarks>
    public bool RemoveTarget(int index)
    {
        if (!MayRemoveTarget(index)) return false;

        string what = _targets.Entries[index].Site.Describe();
        if (!_targets.RemoveAt(index)) return false;

        Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)}: {what} is no longer a target");
        return true;
    }

    /// <summary>Whether that entry may go, which is what the panel draws its button on.</summary>
    public bool MayRemoveTarget(int index)
        => !(_walker.Committed && _walker.Walk.Walks)
           && TargetEdit.MayRemove(index, _targets.Count, _targets.LeadIndex);

    /// <summary>Give one target a share of the warheads, bounded by what the others have taken.</summary>
    public void SetTargetWarheads(int index, int warheads)
        => _targets.SetWarheads(index, warheads, WarheadsAboard);

    /// <summary>Spread every warhead over the targets chosen, evenly.</summary>
    public void BalanceTargets() => _targets.Balance(WarheadsAboard);

    /// <summary>What the list amounts to, in the one line the panel prints.</summary>
    public string DescribeTargets() => _targets.Describe(WarheadsAboard);

    /// <summary>Forget the target and the flight, and hand the vehicle back.</summary>
    public void Abort(string why)
    {
        // A warp asked for on this shot's behalf outlives the shot otherwise, and the player is
        // left fast-forwarding towards a burn that is no longer going to happen.
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

        // Outside that block, and before the reset that forgets what was being fired: the thruster
        // flags are held keys, so a stood-down computer that leaves one down hands the player a bus
        // translating on its own with nothing on screen saying why.
        VehicleCommand.DriveTranslation(Craft, TrimAxes.None);
        AttitudeHook.PulseMode(Craft, pulsing: false);

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
        _trimAbandoned = false;
        _trimShape = "";
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
        Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} stood down: {why}");
    }

    /// <param name="release">
    /// The weapon aboard, as the one thing this needs of it: something that can be told to shoot at
    /// a place. Null for a vehicle carrying nothing that lets go, which flies the arc regardless.
    /// </param>
    /// <param name="traceWarhead">
    /// Follow one released warhead down and write the comparison to the log.
    /// <see cref="WarheadTrace"/> — measurement only, and off unless somebody asked for it.
    /// </param>
    public void Update(double simStep, double playerStep, IManualFire? release,
                       bool traceWarhead = false,
                       IReadOnlyList<IcbmComputer>? busyElsewhere = null)
    {
        // A warhead outlives the bus that let it go, and on a steep arrival the bus does not last:
        // it breaks up on reentry BEFORE its own warheads arrive -- measured 10:43:26 against
        // impacts at 10:43:38-49. A trace that stops with its craft therefore loses exactly the
        // flights on the steepest arc, which on a paired night is one whole arm. Everything the
        // trace still needs is the planet and the aim, and neither of those is the vehicle.
        if (!KsaWorld.IsAlive(Craft))
        {
            // The countdown keeps running, because the warheads do. Frozen here it never reaches
            // zero, and anything holding on for the salvo to arrive would hold for ever.
            if (double.IsFinite(_arrivalLeft)) _arrivalLeft -= simStep;

            // The reach is not one of the things that outlive the bus: it describes where the bus
            // could still send a warhead, and there is no bus.
            _reachFootprint = null;
            Reach = ReachDisplay.None(ReachHold.SalvoAway, _targets.Count);

            StepTraceLoose(simStep);
            return;
        }

        _busyElsewhere = busyElsewhere ?? [];
        _traceWanted = traceWarhead;

        // What the prediction is of. The bus cuts off above the air; the warheads it drops fly all
        // the way down through it, and they are the things that have to arrive.
        // Before the round is read, so the prediction and the warheads it lets go are the same profile.
        if (Config.WarheadDragFromItsShape && release is { } launcher
            && launcher.Munition.Name == Arsenal.ReentryVehicleMk21.Name && !launcher.Munition.DragFromShape)
        {
            launcher.FlyRoundsAs(Arsenal.Mk21WithDragFromShape(launcher.Munition));
            Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)}: warheads drag from their shape, "
                     + $"k {launcher.Munition.AppliedDragK:E3}");
        }

        // After the drag swap and not instead of it: each takes the round as it stands, so the two compose
        // and a night may fly either alone. Guarded on the value rather than on a flag because the swap is
        // idempotent only while the step already matches.
        if (Config.WarheadSubStepMs > 0.0 && release is { } stepped
            && Math.Abs(stepped.Munition.SubStep - Config.WarheadSubStepMs / 1000.0) > 1e-9)
        {
            stepped.FlyRoundsAs(Arsenal.RoundAtSubStep(stepped.Munition, Config.WarheadSubStepMs / 1000.0));
            Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)}: warheads integrate at "
                     + $"{stepped.Munition.SubStep * 1000.0:F3} ms, {stepped.Munition.MaxSubSteps} sub-steps "
                     + "to a frame at most");
        }

        _warhead = release?.Munition;

        // Read every frame, not inside DriveTrim: that returns early whenever the trim is off or
        // the phase has moved past deployment, which leaves a stale count behind and puts the
        // arrival readout back to counting down to an impact nothing was going to make.
        // A LATCH. Rounds being in the air is what first says a warhead has left -- neither Ammo nor
        // TubesReadyToFire does, because a warhead goes through the deployment path rather than the
        // magazine's fire path and both still read six with the salvo long gone -- but "rounds are
        // in the air" and "the salvo has gone" are only the same thing while the warheads are
        // flying. Recomputed each frame it would go false again the moment the last one landed,
        // letting Predict resume writing the arrival and bringing the readout back counting down to
        // the BUS's own impact, about half a minute later.
        //
        // Every reader wants the latched meaning: the coast probe stops predicting once the salvo
        // is gone.
        _salvoAway |= release is IRoundsInFlight flying && flying.Rounds.Count > 0;

        // The largest reading rather than the latest: a frame with no weapon resolved, or one taken
        // mid-reload, would otherwise tell the panel the bus is empty. Only the target list reads it.
        if (release is { } rack) _warheadsLoaded = Math.Max(_warheadsLoaded, rack.TubesReadyToFire);

        // Run down on the world's own clock. Everything else the readout could be aged by stops
        // when this computer stops predicting; the step does not.
        if (double.IsFinite(_arrivalLeft)) _arrivalLeft -= simStep;
        _salvoProbe.Advance(simStep);
        MeasureRelease(release);

        if (!Config.Armed)
        {
            // Standing down has to be an edge rather than a state. Writing "manual" every frame
            // would take the vehicle away from a player who is flying it by hand, on a computer
            // that is switched off.
            if (_driving) Abort("disarmed");
            IcbmState idle = Sample(playerStep, out _);
            StepTrace(simStep);
            Command = Program.Update(simStep, idle);

            // On the pad too, because that is where the targets are placed: the reach before the
            // burn is the release epoch's and costs no flying, so an unarmed computer can price it
            // as readily as a coasting one.
            RefreshReach(idle);
            return;
        }

        IcbmState state = Sample(playerStep, out bool usable);
        StepStagingProbe(simStep);

        // After Sample, which is what writes Parent, Body and the aim in this frame's coordinates,
        // and before Release below - so a warhead let go this frame is picked up with its clock at
        // zero rather than a frame in.
        StepTrace(simStep);

        if (!usable)
        {
            Command = Program.Update(simStep, state);
            return;
        }

        // NaN unless a walk is running, and then when its first release is due -- gate + (N-1) x 65 s,
        // so the LAST release lands on the setting, or the start of the coast with WalkStartsAtCutoff.
        // A walk of one is NaN, which leaves the program reading the setting live as it always has.
        Program.ReleaseGateSeconds = _walker.GateOverrideSeconds(Config.ReleaseBeforeArrivalSeconds);

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

        // The operator asked for an arrival this stack cannot buy, and the shot is being flown
        // shallower anyway. Said rather than left to be inferred from two angles differing on the
        // panel -- and said here because an unattended shot has no panel at all, so the log is the
        // only place it can be read afterwards. Once a flight: it clears with the flight.
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

        // One line per phase change. Every gate in the program returns quietly, so a flight that
        // goes wrong leaves nothing behind saying which of them it went wrong at - and the panel
        // only shows the state it is in now, not the order it got there.
        if (Command.Phase != _reported)
        {
            _reported = Command.Phase;
            Log.Info($"{KsaWorld.DisplayName(Craft)} ICBM: {Command.Phase} at "
                     + $"{AltitudeMetres / 1000.0:F0} km, {Command.VelocityToGain:F0} m/s to gain, "
                     + $"burn in {IcbmProgram.Clock(Command.SecondsToBurn)}, "
                     + $"{(ArrivalIsIfReleasedNow ? "impact if released now in" : "impact in")} "
                     + $"{IcbmProgram.Clock(SecondsToArrival)}, "
                     + $"target {OffPlaneDegrees:F1} deg off plane ({PlaneChangeCost:F0} m/s), "
                     + $"reach {Command.Reach}"
                     + (double.IsFinite(Program.ResidualAtCutoff)
                            ? $", cut off {Program.ResidualAtCutoff:F2} m/s short{ResidualSaid()}"
                            : "")
                     // The mod's own prediction against its own aim. Near zero means the solution
                     // is self-consistent and whatever missed happened to the round afterwards;
                     // large means the arc never pointed at the target and the burn flying it
                     // perfectly was never going to help.
                     + PredictedImpactSaid()
                     + $" :: {Command.Hold}");
        }

        // The aim stops moving when the arrival stops being free. They are one problem solved in
        // two halves, and the second half is only solvable once the first has finished.
        if (Program.IsBurning && double.IsFinite(Program.CommittedArrivalFromNow)) _aim.Freeze();

        // And starts again once the engines stop, because the thing that made the two halves fight is
        // the burn: with the trajectory fixed, the arc follows the aim and the trim flies the
        // difference. Once only, so a coast pass that genuinely settles stays settled.
        if (Program.ResumesTheAim && !_resumedForCoast)
        {
            _resumedForCoast = true;
            _aim.Resume();
        }

        Predict(simStep, state);
        RefreshReach(state);

        // Read before anything is written this frame. KSA replaces the whole flight computer from
        // its worker every frame, so this is what survived of last frame's command — and comparing
        // it with what is read straight after writing is the only way to tell a write that never
        // lands from one the engine reverts.
        FlightComputerAttitudeMode wasMode = Craft.FlightComputer.AttitudeMode;
        FlightComputerAttitudeTrackTarget wasTrack = Craft.FlightComputer.AttitudeTrackTarget;

        // When the release gate opens, which on a long shot is minutes after cutoff -- so the bus
        // holds attitude for the whole coast with the empty stack on. A short shot drops it when the
        // burn ends instead: there the stack also coasts through air.
        //
        // Both of these run before anything decides whether a warhead may go, and the ordering is
        // the point. The decoupler's shove is about a metre a second and it arrives after the last
        // thing that could compensate for it; letting a round go on the same frame the split is
        // asked for sends one warhead on the attached stack's solution and the rest on the shoved
        // bus's. Measured in flight as a 163 m outlier inside a 3.6 km group.
        //
        // A shot that releases at cutoff does not separate at all: its warheads leave the bus while it
        // is still on the stack, which is heavier and steadier than a bus just shoved off it, with no
        // trim to null the shove and no clearance to wait for. Flown at 300 km, separating first put
        // two warheads at 2.2 km and the four released after the split at 10.
        bool atCutoff = Program.ReleasesAtCutoff;
        bool burnOver = Program.Phase == IcbmPhase.Coast && !atCutoff
                        && Program.IsShortShot;
        if ((Command.ReadyToDeploy && !atCutoff) || burnOver) SeparateOnce(release);

        DriveTrim(simStep, state, release);

        // Attitude is driven for every phase that is doing something, not only while an engine is
        // lit. A hold can be an hour long and the vehicle is pointed at the burn for all of it; and
        // after cutoff the bus has to keep the line it was cut off on for the warheads to leave
        // along. Left free, the vehicle drifts when it should be settled.
        bool aimed = false;

        if (Command.Phase is not (IcbmPhase.Idle or IcbmPhase.NoSolution))
        {
            // Advanced on the *nominal* line, not on the sequencer's offset one. The carried
            // reference is about continuity, and a direction that steps by a cant six times would
            // re-flatten it six times for nothing.
            _rollReference = AimFrame.Advance(_rollReference, Command.ThrustDirectionCci,
                                              -Vec.Unit(state.PositionCci), RollFallback(state));

            _deploy = DriveDeployment(simStep, release, state);

            // Once per change of *state*, not per change of the number in it: the angle counts down
            // by a tenth of a degree a frame, and deduplicating on the whole sentence writes sixty
            // lines a turn. What is worth a line is that it started turning, started settling, or
            // gave up - and a sequence that stalls still looks exactly like one that has finished
            // if none of it is said at all.
            string stage = _deploy.Said.Length > 0 ? _deploy.Said.Split(',')[0] : "";

            if (stage != _saidLast)
            {
                _saidLast = stage;
                if (_deploy.Said.Length > 0) Log.Info($"deploying: {_deploy.Said}");
            }

            // Handed to the hook rather than written here. A write from this pass is discarded
            // before anything reads it - see AttitudeHook.
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
                AttitudeHook.Hold(Craft, _deploy.DirectionCci, _deploy.RollCci);
                aimed = AttitudeHook.Installed;
                if (aimed) _driving = true;
            }
        }
        else
        {
            AttitudeHook.Release(Craft);
        }

        // The direction actually handed to the hook, not the nominal one: the release sequence turns
        // the vehicle off that line by a cant, and a probe reading the nominal cannot see it happen.
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

            // The only thing that explains a rocket which came apart. KSA destroys a vehicle the
            // moment this fraction reaches one, and nothing else in the log says how near it got --
            // a throttle that is on its way down but has not arrived reads exactly like one that
            // never moved.
            if (!_saidOverLimit && load.GLoadFraction >= OverLimitWarnFraction)
            {
                _saidOverLimit = true;
                Log.Info($"{KsaWorld.DisplayName(Craft)} is pulling {load.PeakGLoad:F1} g of its "
                         + $"{load.MaxGLoad:F1} g limit at {_throttleAchieved:F2} throttle");
            }

            // Every frame of a short shot's last two seconds, because the throttle probe's half-second
            // cannot show what moves the cutoff: the drag offset freezing, the line being slowed, and what is
            // left across the line once it is.
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
                                 : ""));
            }
            if (Program.LineSlowed && !_saidLineSlowed)
            {
                _saidLineSlowed = true;
                Log.Info($"{KsaWorld.DisplayName(Craft)} ICBM: slowing the thrust line with "
                         + $"{Program.VelocityToGain:F1} m/s to gain, turning {Diagnostics.SpinDegPerSec(Craft):F1} deg/s");
            }

            VehicleCommand.SetEngine(Craft, running: true);

            // Never past the launcher. A stage runs dry with the engines still commanded on and
            // the program asks for the next sequence every second and a half; if the joint holding
            // the launcher is the next thing in that list, a shot that fell short drops its rounds
            // instead of holding them.
            if (Command.RequestStage)
            {
                if (!StagingWouldDropTheLauncher(release))
                {
                    Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} staging: {Command.Hold}");

                    // Before the call, because the stage lands a frame later through the engine's
                    // input buffer and the difference is what identifies what came off.
                    _wasBeforeStage.Clear();
                    KsaWorld.CollectVehicles(_wasBeforeStage);
                    _awaitingStage = true;

                    Log.Info($"staging probe on {KsaWorld.DisplayName(Craft)}: before -- "
                             + $"{Diagnostics.DescribeEngines(Craft)}, turning "
                             + $"{Diagnostics.SpinDegPerSec(Craft):F1} deg/s");
                    _sinceStaging = 0.0;
                    _stagingProbe = 0;

                    AttitudeHook.Stage(Craft);
                }
                else if (!_saidRefusedStage)
                {
                    // Said once, because the refusal is otherwise completely silent -- and on the
                    // pad it is the launch not happening rather than a stage going unspent.
                    _saidRefusedStage = true;
                    Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} wants a stage and will "
                             + "not fire one that separates its own launcher; stage it by hand -- "
                             + Diagnostics.DescribeEngines(Craft));
                }
            }
        }
        else if (_driving)
        {
            VehicleCommand.SetEngine(Craft, running: false);
            _throttleAchieved = VehicleCommand.DriveThrottle(Craft, 1.0, Config.ThrottleThroughTheKeyboardClear);
        }

        if (Config.AutoRelease && _deploy.ReleaseNow && Release(release) && ReleasesTogether)
        {
            while (release is { ReadyToFire: true } && Release(release)) { }
        }

        // After the release, so a stop whose last warhead went this frame hands over on this frame
        // rather than spending one more holding an aim nothing is left for.
        StepTheWalk(release);

        CarryOurWarp();
        CarryTheView();
    }

    /// <summary>
    /// Follow the weapon onto the craft that now carries it, after a decoupler split the stack.
    ///
    /// <para>The flight continues. The phase, the held cutoff line, the aim bias, the roll
    /// reference and the target are all about the shot rather than about the hull, and they come
    /// across by staying where they are — which is the argument for rehoming rather than building a
    /// fresh computer. A fresh one re-enters the phase machine at
    /// <see cref="IcbmPhase.Holding"/>, and only <c>Coast</c> ever sets <c>ReadyToDeploy</c>, so it
    /// would never release a warhead at all.</para>
    /// </summary>
    public void Rehome(Vehicle craft)
    {
        if (!KsaWorld.IsAlive(craft) || ReferenceEquals(craft, Craft)) return;

        Vehicle left = Craft;

        // Before anything else: the hook is keyed on the vehicle and is only ever cleared here, so
        // without this the spent stack is held on the cutoff line for the rest of the session.
        AttitudeHook.Release(left);

        // And hand it back the way a player expects to find it. Only what this mod switched on.
        if (_driving)
        {
            VehicleCommand.SetEngine(left, running: false);
            VehicleCommand.ReleaseAttitude(left);
        }

        // Unconditionally, and not with the rest. The rehome lands a frame after the split, so the
        // trim can already have commanded the half being left behind - and a thruster flag is a
        // held key, so nothing would ever let go of it again.
        VehicleCommand.DriveTranslation(left, TrimAxes.None);
        AttitudeHook.PulseMode(left, pulsing: false);

        Craft = craft;

        Log.Info($"ICBM computer followed its weapon from {KsaWorld.DisplayName(left)} "
                 + $"onto {KsaWorld.DisplayName(craft)}");

        // And take the player with it, but only if they were watching the thing that just split.
        // Somebody flying an aircraft on the other side of the planet did not ask to be moved.
        //
        // Staged rather than done here, for ordering: a handover is decided during the panel's own
        // pass, and taking the player's camera in the middle of it moves the craft out from under a
        // panel that has already read which one it is showing.
        if (KsaWorld.IsWatching(left)) _viewWanted = craft;

        // Held for the whole coast, to measure a distance from. The stack is alive rather than
        // destroyed, so this is not the reference CLAUDE.md's rule about dead vehicles is about,
        // and the lifetime is one flight either way: Rehome and the stand-down both clear it.
        _separatedFrom = left;

        // Said again on the other side of the handover. The clearance state is reported once, and
        // that first report comes from the half the computer is about to leave -- so without this
        // the reading the trim actually runs on never appears in a log.
        _saidClearOnce = false;
    }

    // Deferred out of Rehome, which runs inside the engine's update pass.
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

    // A guided burn cannot resolve its cutoff finer than one frame, so a long step is not a slow
    // frame -- it is accel x step of velocity nobody asked for, and the shot is decided by it. The
    // rounds' own overrun report drops to Debug when nothing is in the air, and a boost always is,
    // so this is the only thing that says it happened.
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

    // What the flight computer makes of the attitude it is being given, which is the only way to
    // tell a command that is swinging from a vehicle that cannot hold a steady one. Both look like
    // tumbling from outside, and they want opposite fixes.
    //
    // It runs through the coast as well as the burn, because that is where the release sequence
    // lives and the vehicle it is asking to turn is a different one: the spent stack is gone, so
    // the inertia has collapsed and every limit below has moved with it.
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

    // The engine's own attitude limits, read back after its worker has written them. They are what
    // says whether a small command is asked for at all: KSA's RCS tracker crawls at half a rate bit
    // anywhere inside 0.5*AngleDeadband + AngleTurnaround and latches its deadband there, and
    // AngleTurnaround is at least ten seconds of one rate bit - which is a minimum thruster pulse
    // divided by the vehicle's inertia, so dropping a spent stack widens the whole band by the mass
    // ratio. docs/MIRV-NEXT.md item 5 has the law and the citations.
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

    // Something square to the vertical for the roll to clock to when the planet cannot supply one,
    // which is the whole of a vertical rise. Downrange is horizontal by construction; before there
    // is one, the way the vehicle is already moving will do.
    private double3 RollFallback(in IcbmState state)
        => Program.DownrangeCci.Equals(Vec.Zero) ? state.VelocityCci : Program.DownrangeCci;

    /// <summary>The trajectory, in the ecliptic, for drawing. Empty until a prediction has run.</summary>
    public void PathEcl(List<double3> into)
    {
        into.Clear();
        if (Parent is null) return;

        doubleQuat cci2Cce = Parent.GetCci2Cce();
        double3 centre = Parent.GetPositionEcl();

        for (int i = 0; i < _path.Count; i++) into.Add(_path[i].Transform(cci2Cce) + centre);
    }

    /// <summary>Where the aim point is right now, in the ecliptic. Null when nothing is designated.</summary>
    public double3? TargetEcl() => SiteEcl(Target);

    /// <summary>The same for any other place in the list.</summary>
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

        // The spin axis is exactly +Z in a body's own Cci: KSA builds Ccf from Cci by rotating
        // about UnitZ and nothing else, so there is no obliquity term to carry here. It is the
        // *ecliptic* that sees the tilt.
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

            // Aimed at the target plus whatever the flown prediction says the arc is losing. The
            // solver is exact for a *point* in vacuum; the round stops where the ground actually
            // is, and on a shallow arrival over rising terrain that is tens of kilometres short of
            // a summit. Correcting the aim is the only thing that closes it, because there is
            // nothing wrong with the trajectory - it arrives exactly where it was asked to.
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
                             ReleaseOffsetCci(), ReleaseImpulseCci(), KsaWorld.OnlySolidsRunning(Craft));
    }

    /// <summary>What the engine will destroy this airframe at, in standard gravities, or zero if it
    /// has not said. Read-only: the panel reports it, because there is nothing to set.</summary>
    public double AirframeLimitGee
    {
        get
        {
            double limit = Craft.StructuralLoad.MaxGLoad;
            return double.IsFinite(limit) && limit > 0.0 ? limit : 0.0;
        }
    }

    // What the engine will destroy this airframe at, which it works out from the vehicle's own
    // bounding sphere and reports beside the load it is actually seeing. Zero outside a physics
    // bubble, where the struct has never been filled in -- absent rather than unlimited, which is
    // why the program treats the two differently.
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

    // What the engine says the whole stack has left, across the stages it has not yet flown.
    //
    // The only figure that accounts for staging: it is what KSA's own staging display reads, and it
    // is what keeps a multi-stage rocket from reporting itself unreachable while sitting on the pad
    // with the range to spare. NaN when it cannot be read, which puts the single-stage estimate
    // back rather than claiming a stack has nothing.
    // The running stage alone, off the same staging display. NaN when it cannot be read.
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

    // Where the ground actually is under a point on the arc. Without this the prediction flies
    // down to the mean sphere while the round it is predicting stops on terrain, and on a shallow
    // deorbit that gap is enormous: the arc covers about twelve kilometres of ground per kilometre
    // of height near the end, so a target four kilometres up - which is most of the Andes - puts
    // the prediction fifty kilometres past where anything actually lands.
    //
    // The point arrives un-carried to the prediction's own epoch, which mid-burn is the cutoff and
    // not now - so it is brought back the rest of the way before being read in the body-fixed frame
    // this frame has. Skipping that samples the height field a whole burn's worth of rotation away,
    // which is tens of kilometres of the wrong ground on the arrival the correction then reads.
    private double TerrainRadiusAt(double3 pointCci)
    {
        if (Parent is not { } parent) return Body.SurfaceRadius;

        try
        {
            // Only while burning: the aim cycle sets _departsIn at ~2 Hz and a short shot releases at cutoff,
            // before it is cleared, so read stale the probe stood on ground turned away: 1.13 m low at 300 km.
            double carry = Program.IsBurning ? _departsIn : 0.0;
            double3 nowCci = carry > 0.0 ? Body.CarryCci(pointCci, -carry) : pointCci;
            double3 dirCcf = Vec.Unit(nowCci).Transform(parent.GetCci2Ccf());
            if (!Vec.IsFinite(dirCcf) || dirCcf.Equals(Vec.Zero)) return Body.SurfaceRadius;

            // Accurate, because GroundTest is accurate and the round stops where *it* says. A
            // coarse sample is a different height field, and on a shallow arrival every metre of
            // disagreement is about eleven metres of ground. Affordable because ImpactPredictor
            // only asks near the surface.
            double height = SurfaceHeight(parent, parent.GetTerrainHeightFromDirCcf(dirCcf, accurate: true));
            return double.IsFinite(height) ? parent.MeanRadius + height : Body.SurfaceRadius;
        }
        catch
        {
            return Body.SurfaceRadius;
        }
    }

    // Which way the leftover points, because that is what decides what it costs: on a deorbit a
    // metre a second left along the track is about 1.8 km of miss and the same metre left radially
    // is about 3.4 km. The acceleration and step come with it because together they are the floor -
    // one frame of burning is accel x step x throttle, and a residual near that is a timing limit
    // rather than a guidance error, which wants a completely different fix.
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

        // The frame quantum at the throttle the stack actually had, beside the same quantum at
        // full thrust. A commanded ramp that never arrives makes those two equal, and is otherwise
        // indistinguishable from never having asked for one.
        //
        // Both factors are printed, not only their product: a quantum of kilometres a second is a
        // long frame or an absurd acceleration, those want opposite fixes, and one number cannot
        // say which. The longest step of the whole burn comes with them because the cutoff frame
        // is not where a stolen minute shows up.
        double full = Program.AccelerationAtCutoff * Program.StepAtCutoff;
        double achieved = Program.ThrottleAtCutoff;

        return $" ({track:F2} along, {radial:F2} radial, {cross:F2} cross"
               + $"; one frame is {full * (double.IsFinite(achieved) ? achieved : 1.0):F3} m/s at "
               + $"{achieved:P0} throttle, {full:F2} at full"
               + $" = {Program.AccelerationAtCutoff:F1} m/s2 x {Program.StepAtCutoff * 1000.0:F0} ms"
               + $"; longest step of the burn {Program.LongestStepWhileBurning * 1000.0:F0} ms)";
    }

    // A warhead does not leave on the bus's velocity. Each is ejected along its own tube at the
    // munition's LaunchSpeed, and a bus's tube cants cancel in the mean, so what survives is the
    // whole of it along the nose.
    //
    // On a deorbit that nose is held retrograde - it is the attitude the braking burn ended on - so
    // the ejection *slows* every warhead and they all fall short together. Predicting the bus's arc
    // rather than the round's leaves that invisible to the aim correction, and this trajectory
    // moves about two kilometres per metre per second.

    // The same clamp the round's own ground test applies. A height field answers with terrain, so
    // over an ocean it reports the seabed - and 71% of Earth is below its waterline at a mean depth
    // of 3,776 m, which on a seven-degree arrival is about 35 km of ground. Without it the aim is
    // placed on the bottom and the prediction agrees, so the correction converges and reports zero
    // while the warheads splash short: the same blindness as a drag-free predictor.
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

    // How thick the air is at a point on the arc: the field the round's own drag is read from, less the
    // ocean. A warhead stops at the waterline and never flies through water, but the medium reads ocean
    // anywhere under the mean sphere, land above it or not -- and under 204 m of ground the air step's
    // stages reach there, read 837x the air, and throw the prediction off the planet. ACCURACY-PLAN.md 3ep.
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

    // The aim point sits on the real ground rather than on the mean sphere, and that is not a
    // refinement. The whole solve is a transfer between two *points*, so a target standing five
    // kilometres up is hit by aiming at where it stands - no terrain model anywhere else in the
    // guidance, and no correction to apply afterwards.
    private static double3 SurfacePointEcl(Celestial body, double latitudeDeg, double longitudeDeg)
    {
        double3 dirCcf = body.GetDirCcfFromLatLon(latitudeDeg, longitudeDeg);
        double height = SurfaceHeight(body, body.GetTerrainHeightFromDirCcf(dirCcf, accurate: true));
        return dirCcf.Transform(body.GetCcf2Cce()) * (body.MeanRadius + height) + body.GetPositionEcl();
    }
}
