using System.Runtime.InteropServices;
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
internal sealed class IcbmComputer
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

    // The departure velocity the last prediction was flown from, and the one the aim was last read
    // against, so the impact's wander can be priced against the only thing that can cause it.
    private double3 _lastPredictedFromVelCci = new(double.NaN, double.NaN, double.NaN);
    private double3 _lastObservedVelCci = new(double.NaN, double.NaN, double.NaN);

    // Simulated seconds since the aim was last read. The reading cadence is gated by _measureDue
    // rather than by PredictIntervalSeconds, so it is not the half-second the interval nominally is
    // and every rate quoted per reading needs it.
    private double _sinceObserve;

    // How often it is re-flown when nothing but the readout wants it, in REAL seconds. Paced by
    // simulated time it runs every frame at warp, and each pass re-flies a whole trajectory at
    // PredictStepSeconds with a terrain lookup per step -- so on a warped coast, which is most of a
    // flight, this was a full re-flight per rocket per frame for a line on an overlay.
    //
    // The same trap IcbmState.PlayerStepSeconds exists for and describes in as many words: a
    // computation budget is paced by the wall clock. The planner was given that; this was not.
    private const double ReadoutIntervalSeconds = 0.5;

    // Coarse enough to be cheap over half an hour, fine enough to land in the right place.
    private const double PredictStepSeconds = 2.0;

    private readonly List<double3> _path = [];
    private double _sincePredict = double.PositiveInfinity;
    private double _sincePredictWall = double.PositiveInfinity;
    private bool _driving;
    private double3 _rollReference;
    private readonly AimCorrection _aim = new();
    private readonly ReleaseSequence _sequence = new();
    private readonly BusTrim _trim = new();
    private readonly ProximityWatch _proximity = new();
    private double3 _keepOutTowardCci;
    private bool _saidProximity;
    private bool _saidCleared;
    private readonly PostBoostAim _postBoost = new();
    private bool _postBoostSaid;
    private bool _measureDue;
    private double _freshMiss = double.NaN;
    private double _holdingCost = double.NaN;
    private int _holdingCostForPass = -1;
    private bool _resumedForCoast;
    private bool _trimAbandoned;
    private double _departsIn;
    private ReleaseCommand _deploy;
    private readonly double3[] _tubeAxes = new double3[64];
    private bool _separated;
    private bool _awaitingSplit;
    private bool _didSplit;
    private bool _mayTrim = true;
    private bool _saidBudget;

    // What the current and previous post-boost passes asked the trim for, which is what separates a
    // wind-up from a correction the geometry genuinely needs.
    private double _demandThisPass = double.NaN;
    private double _demandLastPass = double.NaN;
    private bool _saidRunaway;

    // Whether the aim's affordable reach has been reported. Once per flight: it is a fact about the
    // trajectory rather than an event, and it moves slowly.
    private bool _saidAimReach;
    private bool _saidFloorUnaffordable;
    private bool _saidRefusedStage;
    private bool _saidStructuralLimit;
    private bool _saidLongStep;
    private bool _saidOverLimit;

    private double _owedAtSplit = double.NaN;
    private Vehicle? _separatedFrom;
    private readonly List<Vehicle> _wasBeforeSplit = [];
    private readonly List<ShedCandidate> _shedCandidates = [];
    private double3 _coastProbePosCci = double3.Zero;
    private double3 _coastProbeVelCci = double3.Zero;
    private bool _coastProbeHasState;
    private readonly List<Vehicle> _afterSplit = [];

    // Everything this vehicle has shed, and the census that finds it. Same difference-of-worlds
    // trick as WhatWasDropped, run at every staging rather than only at the split, because the
    // ascent stages are three of the four vehicles a rocket leaves behind.
    private readonly List<Vehicle> _shed = [];
    private readonly List<Vehicle> _wasBeforeStage = [];
    private readonly List<Vehicle> _afterStage = [];
    private bool _awaitingStage;

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
    private double3 _releaseOffsetCci;
    private double3 _releaseKickCci;
    private bool _releaseMeasured;
    private double _tubeSpinSpeed;
    private bool _warpIsOurs;
    private IcbmPhase _reported = IcbmPhase.Idle;
    private double _sinceProbe;
    private double _sinceThrottleProbe;
    private double3 _lastCommanded;

    private readonly WarheadTrace _trace = new();
    private bool _traceWanted;
    private bool _tracedThisShot;

    // Cached rather than converted at each call site: a method group becomes a delegate by
    // allocating one, and the trace builds its Setup on every frame of a four-hundred-second fall.
    private Func<double3, double>? _terrainRadius;
    private Func<double3, double>? _densityRatio;

    // Often enough to see an oscillation, rare enough not to fill the log with a burn's worth.
    private const double ProbeIntervalSeconds = 0.5;

    // How much of the remaining wait each warp leaves for the next one, and the most it will leave.
    // A fraction rather than a constant because the span is what decides how fast KSA warps: a
    // fixed margin off a ninety-minute hold is still approached at thousands of times speed.
    private const double MarginFraction = 0.2;

    private const double MaxMarginSeconds = 900.0;
    private double _throttleAchieved = 1.0;

    public Vehicle Craft { get; private set; }

    public IcbmConfig Config { get; }

    public IcbmProgram Program { get; }

    /// <summary>Where it has been told to put the warheads. Nothing happens until this is set.</summary>
    public AimSite Target { get; private set; } = AimSite.None;

    /// <summary>The last command issued, which is what every readout on the panel is describing.</summary>
    public IcbmCommand Command { get; private set; }

    /// <summary>Where the vehicle would land if everything stopped now. Null when it would not.</summary>
    public ImpactPredictor.Impact? PredictedImpact { get; private set; }

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
    /// </summary>
    public bool SalvoFinished => _salvoSize > 0 && WarheadsAway >= _salvoSize;

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
    private bool _saidClearOnce;

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
    // It used to have to cover the clearance wait as well, because the trim solved a fresh transfer
    // to the corrected aim and the two loops drove each other. The trim reads no aim at all now, so
    // that coupling is gone at the source - and a correction frozen across a long wait is its own
    // fault, since what it absorbs is what the fall loses to drag and terrain and that changes as
    // the release point descends.
    public bool TrimIsFiring => _trim.Armed && !_trim.Done && _mayTrim;

    public Celestial? Parent { get; private set; }

    public IcbmComputer(Vehicle craft, IcbmConfig config, Config session)
    {
        Craft = craft;
        Config = config;
        _session = session;
        Program = new IcbmProgram(config);
    }

    public void Designate(AimSite site)
    {
        Target = site;
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
        _saidRunaway = false;
        _saidAimReach = false;
        _demandThisPass = double.NaN;
        _demandLastPass = double.NaN;
        _saidFloorUnaffordable = false;
        WarheadsAway = 0;
        _salvoSize = 0;
        _owedAtSplit = double.NaN;
        _rollReference = Vec.Zero;
        PredictedImpact = null;
        PredictedMissMetres = double.NaN;

        // A new aim point is a new shot, and the trace's walk is measured against an aim that has
        // just moved. Whatever is still in the air from the last one is dropped rather than scored
        // against the wrong target.
        _tracedThisShot = false;
        _trace.Forget();

        Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} designated {site.Describe()}");
    }

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

        Program.Reset();
        _releasedTheArrival = false;
        _trim.Reset();
        _postBoost.Reset();
        _postBoostSaid = false;
        _measureDue = false;
        _freshMiss = double.NaN;
        _holdingCost = double.NaN;
        _holdingCostForPass = -1;
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
        _saidRunaway = false;
        _saidAimReach = false;
        _demandThisPass = double.NaN;
        _demandLastPass = double.NaN;
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
        if (!KsaWorld.IsAlive(Craft)) return;

        _busyElsewhere = busyElsewhere ?? [];
        _traceWanted = traceWarhead;

        // What the prediction is of. The bus cuts off above the air; the warheads it drops fly all
        // the way down through it, and they are the things that have to arrive.
        _warhead = release?.Munition;

        // Read every frame, not inside DriveTrim: that returns early whenever the trim is off or
        // the phase has moved past deployment, which leaves a stale count behind and puts the
        // arrival readout back to counting down to an impact nothing was going to make.
        // Whether anything is already flying, which is the only thing that actually changes when a
        // warhead leaves. Neither Ammo nor TubesReadyToFire does: a warhead goes through the
        // deployment path rather than the magazine's fire path, so both still read six with the
        // salvo long gone -- which left this readout counting down to the *bus's* own impact, about
        // half a minute after the warheads it dropped.
        _salvoAway = release is IRoundsInFlight flying && flying.Rounds.Count > 0;

        // Run down on the world's own clock. Everything else the readout could be aged by stops
        // when this computer stops predicting; the step does not.
        if (double.IsFinite(_arrivalLeft)) _arrivalLeft -= simStep;
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
            return;
        }

        IcbmState state = Sample(playerStep, out bool usable);

        // After Sample, which is what writes Parent, Body and the aim in this frame's coordinates,
        // and before Release below - so a warhead let go this frame is picked up with its clock at
        // zero rather than a frame in.
        StepTrace(simStep);

        if (!usable)
        {
            Command = Program.Update(simStep, state);
            return;
        }

        bool wasBurning = Program.IsBurning;
        Command = Program.Update(simStep, state);
        ReportLongStep(wasBurning, simStep, state);

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

        // And starts again the moment the engines do stop, because the thing that made the two
        // halves fight is the burn: with the trajectory fixed, the arc follows the aim and the trim
        // flies the difference. Once only, so a coast pass that genuinely settles stays settled.
        if (!Program.IsBurning && !_resumedForCoast)
        {
            _resumedForCoast = true;
            _aim.Resume();
        }

        Predict(simStep, state);

        // Read before anything is written this frame. KSA replaces the whole flight computer from
        // its worker every frame, so this is what survived of last frame's command — and comparing
        // it with what is read straight after writing is the only way to tell a write that never
        // lands from one the engine reverts.
        FlightComputerAttitudeMode wasMode = Craft.FlightComputer.AttitudeMode;
        FlightComputerAttitudeTrackTarget wasTrack = Craft.FlightComputer.AttitudeTrackTarget;

        // At cutoff rather than at the first release, which on a nominal shot is the same frame:
        // the launcher has the whole coast to settle, and every kilogram of spent stack is mass its
        // own thrusters would otherwise have to turn between releases.
        //
        // Both of these run before anything decides whether a warhead may go, and the ordering is
        // the point. The decoupler's shove is about a metre a second and it arrives after the last
        // thing that could compensate for it; letting a round go on the same frame the split is
        // asked for sends one warhead on the attached stack's solution and the rest on the shoved
        // bus's. Measured in flight as a 163 m outlier inside a 3.6 km group.
        if (Command.ReadyToDeploy) SeparateOnce(release);

        DriveTrim(simStep, state, release);

        // Attitude is driven for every phase that is doing something, not only while an engine is
        // lit. A hold can be an hour long and the vehicle is pointed at the burn for all of it; and
        // after cutoff the bus has to keep the line it was cut off on for the warheads to leave
        // along. Both were left free before, which is a vehicle drifting when it should be settled.
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
            AttitudeHook.Hold(Craft, _deploy.DirectionCci, _deploy.RollCci);
            aimed = AttitudeHook.Installed;
            if (aimed) _driving = true;
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
            _throttleAchieved = VehicleCommand.DriveThrottle(Craft, Command.Throttle);

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

            _sinceThrottleProbe += playerStep;
            if (Log.Threshold <= Log.Level.Debug && _sinceThrottleProbe >= ProbeIntervalSeconds)
            {
                _sinceThrottleProbe = 0.0;
                BoosterPerformance booster = Program.LastBooster;

                Log.Debug($"throttle: asked {Command.Throttle:F3}, achieved {_throttleAchieved:F3}"
                          + $" | full-throttle {booster.AccelerationNow / 9.80665:F2} g, "
                          + $"load {load.PeakGLoad:F2} of {load.MaxGLoad:F1} g"
                          + $" (thrust {booster.ThrustNewtons / 1000.0:F0} kN, "
                          + $"mass {booster.TotalMassKg / 1000.0:F1} t)");
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

                    VehicleCommand.Stage(Craft);
                }
                else if (!_saidRefusedStage)
                {
                    // Said once, because the refusal is otherwise completely silent -- and on the
                    // pad it is the launch not happening rather than a stage going unspent.
                    _saidRefusedStage = true;
                    Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} wants a stage and will "
                             + "not fire one that separates its own launcher; stage it by hand");
                }
            }
        }
        else if (_driving)
        {
            VehicleCommand.SetEngine(Craft, running: false);
            _throttleAchieved = VehicleCommand.DriveThrottle(Craft, 1.0);
        }

        if (Config.AutoRelease && _deploy.ReleaseNow) Release(release);

        CarryOurWarp();
        CarryTheView();
    }

    /// <summary>
    /// Hand the wait to KSA's own warp-to-a-time. Pressed, never automatic.
    ///
    /// <para>Warping is an action rather than a setting, and taking the player's time control
    /// because a target happened to be designated is not a thing a weapon gets to do. They may have
    /// set a tenth speed to watch something.</para>
    ///
    /// <para>One press covers the whole wait, in hops. That is not tidiness — it is the only way
    /// the handover can work. KSA scales its warp rate to the <em>span</em> it is asked to cover, so
    /// a single jump to the end of a ninety-minute hold arrives doing thousands of times normal
    /// speed, where the last minute of it passes in under two frames and there is nowhere to hand
    /// over. Each hop leaves a margin, and the next one covers a shorter span and so runs gentler,
    /// until the approach is slow enough to be caught.</para>
    /// </summary>
    public bool TryWarpToWindow() => TryWarpAhead("the burn window");

    /// <summary>
    /// The same, for the long fall after the engines stop.
    ///
    /// <para>Stops a settling margin short of the release rather than at it — see
    /// <see cref="IcbmProgram.SteadyBeforeReleaseSeconds"/>, which is where the number and the
    /// measurement behind it live.</para>
    /// </summary>
    public bool TryWarpTheCoast() => TryWarpAhead("the release point");

    private bool TryWarpAhead(string what)
    {
        if (!CanWarpAhead) return false;

        double wait = SecondsToTheNextThingThatMatters;
        double margin = Math.Clamp(wait * MarginFraction, IcbmProgram.WarpHoldLeadSeconds, MaxMarginSeconds);

        if (!KsaWorld.TryAutoWarpTo(wait, margin)) return false;

        _warpIsOurs = true;
        Log.Info($"warping to within {IcbmProgram.Clock(margin)} of {what} on "
                 + $"{KsaWorld.DisplayName(Craft)}, {IcbmProgram.Clock(wait)} to go{OverTheTopOf()}");
        return true;
    }

    // Whether any OTHER flight is mid-burn or mid-trim. The list is empty when nobody handed one
    // in, so a computer driven outside IcbmComputers asks about itself alone.
    private bool AnythingElseNeedsShortSteps
    {
        get
        {
            for (int i = 0; i < _busyElsewhere.Count; i++)
            {
                if (!ReferenceEquals(_busyElsewhere[i], this)) return true;
            }

            return false;
        }
    }

    // What else in the world was still being integrated when this warp started. An auto-warp is
    // world-wide and WarpPolicy cannot rein one in, so every name here is a flight that is about to
    // be stepped at whatever rate KSA picked for somebody else's coast.
    private string OverTheTopOf()
    {
        int others = 0;

        for (int i = 0; i < _busyElsewhere.Count; i++)
        {
            if (!ReferenceEquals(_busyElsewhere[i], this)) others++;
        }

        if (others == 0) return " -- nothing else needs short steps";

        List<string> names = [];

        for (int i = 0; i < _busyElsewhere.Count; i++)
        {
            if (!ReferenceEquals(_busyElsewhere[i], this))
            {
                names.Add(KsaWorld.DisplayName(_busyElsewhere[i].Craft));
            }
        }

        return $" -- OVER THE TOP OF {others} still needing short steps: {string.Join(", ", names)}";
    }

    /// <summary>Whether the window is far enough away for warping to it to be worth offering.</summary>
    public bool CanWarpToWindow => Program.Phase == IcbmPhase.Holding && CanWarpAhead;

    /// <summary>Whether the coast has enough left in it to be worth warping.</summary>
    public bool CanWarpTheCoast => Program.Phase == IcbmPhase.Coast && CanWarpAhead;

    // Only for the craft being flown, only out to a margin short of what is coming, and never while
    // ANY flight in the world is being integrated -- NeedsShortSteps covers the burn and the trim,
    // and WarpPolicy cannot slow the world at all while an auto-warp is running, so a warp started
    // over the top of one is a warp nothing can rein in.
    //
    // Every flight, not this one. There is one world and one clock: a computer that checks only
    // itself hands the clock away while seven other rockets are still burning, and their longest
    // burn step goes 33 ms to 205 ms with their one-frame velocity quantum 0.081 to 1.675 m/s.
    // Same rule and same reason as WorldSpeed.Slowest, which the speed path already obeys.
    private bool CanWarpAhead
        => !KsaWorld.IsAutoWarpActive
        && !NeedsShortSteps
        && !AnythingElseNeedsShortSteps
        && ReferenceEquals(Craft, KsaWorld.ControlledVehicle)
        && double.IsFinite(SecondsToTheNextThingThatMatters)
        && SecondsToTheNextThingThatMatters > IcbmProgram.WarpHoldLeadSeconds * 2.0;

    // How far off the next thing this computer has to be awake for is, or NaN when there is nothing
    // to wait for. Two waits, and they are one problem: a departure window in orbit, and the release
    // point at the end of a ballistic coast. Both are a known instant minutes or hours away with
    // nothing to do until then, which is what KSA's warp-to-a-time is for.
    private double SecondsToTheNextThingThatMatters
        => Program.Phase switch
        {
            IcbmPhase.Holding => Command.SecondsToBurn,
            IcbmPhase.Coast => SecondsToReleaseApproach,
            _ => double.NaN,
        };

    /// <summary>
    /// How long until the warheads are due to leave, or NaN when nothing is waiting for that.
    ///
    /// <para>What a coast is actually counting down to. The arrival is minutes later and is a
    /// different question — and a shot that fell short holds its warheads for ever, which is why
    /// this is absent rather than large there.</para>
    ///
    /// <para>The <em>time</em> gate only. A release also waits for the deploy altitude on the way
    /// up, and where that is the binding one the hold line says so.</para>
    /// </summary>
    public double SecondsToRelease
    {
        get
        {
            if (Command.ShortfallMetresPerSecond > 0.0) return double.NaN;

            double toArrival = Program.CommittedArrivalFromNow;
            if (!double.IsFinite(toArrival)) return double.NaN;

            return toArrival - Config.ReleaseBeforeArrivalSeconds;
        }
    }

    /// <summary>
    /// When the world has to be back at normal speed, which is earlier than the release itself.
    ///
    /// <para>The aim correction is still converging out here and at a hundred times its steps are
    /// seconds long — see <see cref="IcbmProgram.SteadyBeforeReleaseSeconds"/>.</para>
    /// </summary>
    public double SecondsToReleaseApproach
    {
        get
        {
            double toRelease = SecondsToRelease;

            return double.IsFinite(toRelease)
                       ? toRelease - IcbmProgram.SteadyBeforeReleaseSeconds
                       : double.NaN;
        }
    }

    // Carries a warp this computer started through to whatever it was aimed at, and ends it if the
    // shot stops wanting one. Only ever a warp it started: one the player started is theirs.
    private void CarryOurWarp()
    {
        if (Config.WarpTheCoast && !_warpIsOurs && CanWarpTheCoast) TryWarpTheCoast();

        if (!_warpIsOurs) return;

        if (!double.IsFinite(SecondsToTheNextThingThatMatters))
        {
            if (KsaWorld.IsAutoWarpActive)
            {
                Log.Info($"stopping the warp on {KsaWorld.DisplayName(Craft)}, "
                         + "there is nothing left to wait for");
                KsaWorld.StopAutoWarp();
            }

            _warpIsOurs = false;
            return;
        }

        // Still running: leave it alone. It stops itself at the margin, which is the whole reason
        // for asking it to stop short rather than braking the world by hand.
        if (KsaWorld.IsAutoWarpActive) return;

        // A hop finished. Close the remaining gap with another, shorter and therefore slower one.
        if (CanWarpAhead && TryWarpAhead("what is next")) return;

        _warpIsOurs = false;
    }

    // Hand the wait to KSA's own warp-to-a-time. Only while holding, only for the craft being
    // flown, and only out to a margin short of the burn - the last minute belongs to WarpPolicy,
    // which cannot slow the world down at all while an auto-warp is running.

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

        Craft = craft;

        Log.Info($"ICBM computer followed its weapon from {KsaWorld.DisplayName(left)} "
                 + $"onto {KsaWorld.DisplayName(craft)}");

        // And take the player with it, but only if they were watching the thing that just split.
        // Somebody flying an aircraft on the other side of the planet did not ask to be moved.
        //
        // Staged rather than done here, and no longer because the engine refuses it - GoTo stopped
        // rebuilding derived data, which was the thing it refused. What is left is ordering: a
        // handover is decided during the panel's own pass, and taking the player's camera in the
        // middle of it moves the craft out from under a panel that has already read which one it
        // is showing.
        if (KsaWorld.IsWatching(left)) _viewWanted = craft;

        // Held for the whole coast, to measure a distance from. The stack is alive rather than
        // destroyed, so this is not the reference CLAUDE.md's rule about dead vehicles is about,
        // and the lifetime is one flight either way: Rehome and the stand-down both clear it.
        _separatedFrom = left;

        // Said again on the other side of the handover. The clearance state is reported once, and
        // before this it was always reported from the half the computer is about to leave -- so the
        // reading the trim actually runs on has never appeared in a log.
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

    // Whether the next sequence would fire the joint the launcher hangs on -- that one, not any
    // later. A launcher that can separate at all is not a reason to refuse every stage: a
    // multi-stage stack carrying a bus has a decoupler under it from the moment it is built, and
    // treating that as "the next stage drops my rounds" strands it with a dead first stage.
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

    // How near the airframe's limit is worth a line in the log.
    private const double OverLimitWarnFraction = 0.85;

    private static bool StagingWouldDropTheLauncher(IManualFire? weapon)
        => weapon is { NextStageSeparatesIt: true };

    // Once, and only where the part tree offers a joint to let go of. Nothing shipped declares one,
    // so this does nothing until a craft is built with a decoupler under its launcher.
    private void SeparateOnce(IManualFire? weapon)
    {
        if (_separated || weapon is null) return;

        if (!weapon.CanSeparate)
        {
            _separated = true;
            return;
        }

        // Latched before the call, not after: the split lands a frame later and the module does not
        // de-duplicate, so a second request queues a second decouple.
        _separated = true;

        if (weapon.Separate())
        {
            _awaitingSplit = true;
            _didSplit = true;
            _wasBeforeSplit.Clear();
            KsaWorld.CollectVehicles(_wasBeforeSplit);

            Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} separating the launcher "
                     + "from the stack before deploying");
        }
    }

    // What came off at the last staging, by the same difference WhatWasDropped uses. Run one frame
    // late because the stage is deferred through the engine's input buffer -- a census taken on the
    // frame the command was issued sees the world before it.
    private void CollectShedStages()
    {
        if (!_awaitingStage) return;

        _awaitingStage = false;

        _afterStage.Clear();
        KsaWorld.CollectVehicles(_afterStage);

        for (int i = 0; i < _afterStage.Count; i++)
        {
            Vehicle other = _afterStage[i];

            if (ReferenceEquals(other, Craft)
                || _wasBeforeStage.Contains(other)
                || _shed.Contains(other))
            {
                continue;
            }

            // Bounded the same way WhatWasDropped is, and for the same reason. The window between
            // asking for a stage and the engine reporting it done is not one frame, so a world
            // flying eight rockets on one profile stages them inside it and this census sees THEIR
            // stages as new. Unbounded it adopted other rockets' buses at 39.9 and 79.9 km, six
            // minutes before those buses released. A decoupler parts two halves at about a metre a
            // second, so anything this computer actually let go of is metres away and nothing else
            // in the world is. docs/ACCURACY-PLAN.md 3ap, 3as.
            double3 between = KsaWorld.PositionEcl(other) - KsaWorld.PositionEcl(Craft);
            double apart = Vec.IsFinite(between) ? Vec.Len(between) : double.NaN;

            if (!(apart <= ShedStage.MaxMetres))
            {
                Log.Debug($"{KsaWorld.DisplayName(Craft)} ICBM: not adopting "
                          + $"{KsaWorld.DisplayName(other)} as a shed stage, "
                          + (double.IsFinite(apart) ? $"{apart / 1000.0:F1} km away" : "unreadable"));
                continue;
            }

            _shed.Add(other);
        }

        _wasBeforeStage.Clear();
    }

    // Spent stages cost frame time for the whole coast while they fall, and frame time is the only
    // thing that buys simulation rate. Off unless asked for: it destroys things in the player's
    // world. Sim/StageDisposal.cs holds the rules, including that the half the clearance is still
    // reading is never taken.
    private void DisposeShedStages()
    {
        for (int i = _shed.Count - 1; i >= 0; i--)
        {
            Vehicle stage = _shed[i];

            if (!KsaWorld.IsAlive(stage))
            {
                _shed.RemoveAt(i);
                continue;
            }

            // Never the craft being flown, whatever the census decided: destroying it clears
            // ControlledVehicle and strands the player in a scene that carries on without them.
            if (ReferenceEquals(stage, Craft)
                || ReferenceEquals(stage, KsaWorld.ControlledVehicle))
            {
                continue;
            }

            double3 between = KsaWorld.PositionEcl(stage) - KsaWorld.PositionEcl(Craft);
            double apart = Vec.IsFinite(between) ? Vec.Len(between) : double.NaN;

            if (!StageDisposal.MayDispose(_session.DisposeSpentStages,
                                          ReferenceEquals(stage, _separatedFrom), apart))
            {
                continue;
            }

            Log.Info($"{KsaWorld.DisplayName(Craft)}: taking the spent stage "
                     + $"{KsaWorld.DisplayName(stage)} out of the world at {apart / 1000.0:F1} km, "
                     + "so it stops costing frame time while it falls");

            KsaWorld.WaitForVehicleSolvers();
            KsaWorld.Destroy(stage, 0.0f);
            _shed.RemoveAt(i);
        }
    }

    // The half of the stack this vehicle let go of, found by difference: the decoupler makes a
    // vehicle that did not exist a frame ago, and everything else in the world did.
    //
    // Rehome captures it too, but only when the computer follows its weapon onto the *other* half.
    // The ordinary case is the bus keeping both the launcher and the computer, where Rehome never
    // runs -- so without this the distance is unreadable on every flight and SeparationClearance
    // falls back to a blind clock, which is the trim being authorised while the stack is still
    // metres away.
    private Vehicle? WhatWasDropped()
    {
        _afterSplit.Clear();
        KsaWorld.CollectVehicles(_afterSplit);

        _shedCandidates.Clear();

        for (int i = 0; i < _afterSplit.Count; i++)
        {
            Vehicle other = _afterSplit[i];
            if (ReferenceEquals(other, Craft) || _wasBeforeSplit.Contains(other)) continue;

            double3 between = KsaWorld.PositionEcl(other) - KsaWorld.PositionEcl(Craft);
            if (!Vec.IsFinite(between)) continue;

            _shedCandidates.Add(new ShedCandidate(i, Vec.Len(between)));
        }

        ShedChoice choice = ShedStage.Choose(CollectionsMarshal.AsSpan(_shedCandidates));

        if (choice.Verdict != ShedVerdict.Take)
        {
            Log.Info($"split on {KsaWorld.DisplayName(Craft)}: no stack adopted -- {choice.Why}");
        }

        _wasBeforeSplit.Clear();
        return choice.Verdict == ShedVerdict.Take ? _afterSplit[choice.Index] : null;
    }

    // The world half of SeparationClearance: how far apart the two actually are. Both positions
    // come from the same instant, so the ecliptic motion they each carry cancels in the difference
    // - see docs/FRAMES-AND-EPOCHS.md. NaN rather than zero when the stack cannot be read, because
    // the two mean opposite things there.
    private Clearance Clear(double simStep)
    {
        _sinceSplit += simStep;

        double apart = double.NaN;
        double radius = double.NaN;

        if (_separatedFrom is { } stack && KsaWorld.IsAlive(stack))
        {
            double3 between = KsaWorld.PositionEcl(stack) - KsaWorld.PositionEcl(Craft);
            if (Vec.IsFinite(between)) apart = Vec.Len(between);

            // The stage's own bounding sphere, which is what the coarse contact test a released
            // store would be scored against actually uses.
            try { radius = stack.MeanRadius; }
            catch { radius = double.NaN; }
        }

        if (!_saidClearOnce)
        {
            _saidClearOnce = true;
            Log.Info($"clearance on {KsaWorld.DisplayName(Craft)}: "
                     + $"stack {(_separatedFrom is null ? "null" : KsaWorld.DisplayName(_separatedFrom))}, "
                     + $"alive {KsaWorld.IsAlive(_separatedFrom)}, "
                     + $"apart {apart:F1} m, radius {radius:F1} m");
        }

        // Measured off the same pair of samples the gate is about to decide on, so the two cannot
        // report different distances about one frame.
        _proximity.Update(simStep, apart, radius);

        // What the trim's interlock is asked, recorded here because this is the one place the
        // separation is measured -- a second derivation could report a different distance about the
        // same frame. In Cci, because that is the frame the trim's own axes are in.
        _keepOutTowardCci = double3.Zero;

        if (double.IsFinite(apart) && apart < ProximityWatch.KeepOutFor(radius)
            && _separatedFrom is { } near && KsaWorld.IsAlive(near) && Parent is { } parent)
        {
            double3 towardEcl = KsaWorld.PositionEcl(near) - KsaWorld.PositionEcl(Craft);

            if (Vec.IsFinite(towardEcl) && !towardEcl.Equals(double3.Zero))
            {
                // A difference of two Ecl positions is already Cce, so this is the same one-rotation
                // conversion every other Cci quantity in this file takes.
                _keepOutTowardCci = Vec.Unit(towardEcl).Transform(parent.GetCce2Cci());
            }
        }

        // An unreadable stack falls back to the clock rather than to "clear": a part tree
        // mid-rebuild reads as no distance at all, and treating that as clearance is exactly the
        // case this exists to prevent -- and it is asked fresh every pass, never remembered.
        return SeparationClearance.Check(apart, radius, _sinceSplit);
    }

    // One line per change of state, which is all any of this is worth while nothing is happening
    // on screen. The detail rides along with it rather than driving it.
    //
    // Compared with its numbers collapsed, because the sentence carries them and a reading is not a
    // state: "trimming 3.71 m/s on the tail" against "trimming 3.70 m/s on the tail" is the same
    // thing happening. Comparing whole sentences wrote a line every frame -- 21,000 from one coast,
    // which is the log's own weight on the frame the coast step is measured in. What still separates
    // is the words, so a direction change, a stall or a hand-back all still say so.
    private void Say(string what, string detail = "")
    {
        string shape = WithoutNumbers(what);
        if (shape == _trimShape) return;

        // Two fields, because they are two different things: the shape is what decides whether this
        // is a new state, and the sentence is what anybody reads. Keeping only the shape puts
        // "trimming # m/s on the back" on the panel, which is the comparison key with its numbers
        // already thrown away.
        _trimShape = shape;
        _saidTrim = what;
        Log.Info($"trimming the bus on {KsaWorld.DisplayName(Craft)}: {what}{detail}");
    }

    // Every run of digits down to one mark, so two readings of one state compare equal.
    private static string WithoutNumbers(string said)
    {
        Span<char> shape = stackalloc char[said.Length + 1];
        int n = 0;
        bool number = false;

        foreach (char c in said)
        {
            if (char.IsAsciiDigit(c) || c == '.')
            {
                number = true;
                continue;
            }

            if (number)
            {
                shape[n++] = '#';
                number = false;
            }

            shape[n++] = c;
        }

        if (number) shape[n++] = '#';

        return new string(shape[..n]);
    }

    // Put the bus back on its solution with its own thrusters, before anything leaves it. All the
    // deciding is in BusTrim; what is here is the same two conversions as everywhere else in this
    // file - the world into a situation and the answer into writes on somebody else's vehicle -
    // plus the one thing only this side can know, which is whether the split has actually landed.
    private void DriveTrim(double simStep, in IcbmState state, IManualFire? weapon)
    {
        // Nothing left to put on a solution. ReadyToDeploy stays true after the last warhead goes,
        // so without this the trim goes on solving and firing at an empty bus: measured at 12,902 km
        // as 8.24 m/s nulled and 19.26 more asked for after `0 left`, a fifth of the whole budget
        // spent on nobody -- and spent manoeuvring six metres from the spent stack, which is the
        // manoeuvre the clearance had just refused on safety grounds.
        if (!Config.TrimBeforeRelease || !Command.ReadyToDeploy || SalvoFinished)
        {
            if (_trim.Firing != TrimAxes.None) VehicleCommand.DriveTranslation(Craft, TrimAxes.None);
            return;
        }

        // The error this exists to remove arrives *with* the split, and the split is deferred
        // through the engine's input buffer. Asking the joint again is the split itself rather than
        // a timer: the decoupler that was there is the one that just came apart, so the question
        // stops answering yes the moment it has. A launcher that never had one is past this
        // already, because SeparateOnce never set the flag.
        if (_awaitingSplit)
        {
            if (weapon is { CanSeparate: true }) return;
            _awaitingSplit = false;

            // Not `??=`. Rehome captures the vehicle the computer came *from*, and a decoupler
            // disposes the pre-split vehicle to make two new ones -- so that capture is a corpse,
            // IsAlive says so, and the clearance test reads no distance at all. Prefer whichever
            // half is actually alive.
            // Read before WhatWasDropped, which clears the census it counts.
            int before = _wasBeforeSplit.Count;

            if (!KsaWorld.IsAlive(_separatedFrom)) _separatedFrom = WhatWasDropped();

            // Said once, because two guesses at why the distance reads as unknown have both been
            // wrong and the next step is a measurement rather than a third. Everything the
            // clearance test depends on, at the one instant it is decided.
            _afterSplit.Clear();
            KsaWorld.CollectVehicles(_afterSplit);

            Log.Info($"split on {KsaWorld.DisplayName(Craft)}: "
                     + $"{(_separatedFrom is null ? "no stack captured" : KsaWorld.DisplayName(_separatedFrom))}, "
                     + $"alive {KsaWorld.IsAlive(_separatedFrom)}, "
                     + $"{before} vehicles before and {_afterSplit.Count} after");
        }

        // Armed at the split rather than at clearance, and held rather than skipped. It keeps
        // solving through the whole wait, so what the bus owes its solution is on record from the
        // moment the decoupler fired — which is the only thing that separates an error the
        // separation caused from one that grew while the vehicle coasted clear of it.
        Clearance clearance = _didSplit ? Clear(simStep) : new Clearance(true, false, "");

        // Given up on rather than waited out: the stack is readable and still too close, so there
        // is no manoeuvre to make here that does not fly into it. Release proceeds untrimmed.
        //
        // It also caps the correction loop, which is not what it is for and is load-bearing anyway:
        // the requirement balloons between passes -- 0.02 m/s trimmed, 12.63 asked next -- and this
        // is what stops the bus flying that number. Handing the safety question to the keep-out
        // interlock lifts the cap and flies it: 5.39 km median against 3.04. `docs/MIRV-NEXT.md`
        // item 8h.
        PostCutoffSequence.Plan plan = PostCutoffSequence.Decide(
            clearance.IsClear, clearance.Abandoned, _postBoost.Cycles,
            Config.TrimBudgetMetresPerSecond, _trim.SpentMetresPerSecond,
            Config.TrimCeilingFromBudget, Config.KeepOutCoversTheClearance);

        if (plan.Abandon)
        {
            _trimAbandoned = true;
            if (_trim.Firing != TrimAxes.None) VehicleCommand.DriveTranslation(Craft, TrimAxes.None);
            Say(clearance.Said, "");
            return;
        }

        // Bounded across the flight, not just per run. Each release re-arms the trim, and with the
        // warheads held until the arrival is close there is a long coast for it to keep finding
        // small corrections in -- which spends the tanks before the corrections that matter.
        //
        // Handed to the trim rather than enforced here, and for two reasons. The figure it keeps is
        // cumulative across every null, so a second total kept out here counts each finished run
        // again for every run that follows. And the stop has to *end* the trim: expressed as
        // withholding fire it never lifts, because only firing spends the tank -- and the warheads
        // do not leave until the trim is done.
        double budget = Config.TrimBudgetMetresPerSecond;

        if (!_saidBudget && !_trim.WithinBudget(budget))
        {
            _saidBudget = true;
            Log.Info($"trim: budget of {budget:F0} m/s spent; the warheads go on the aim as it is");
        }

        // Said here rather than left to the trim's own line, which is the only other thing that
        // reads this sentence and drops it: Say prints `trim.Said` alone once _mayTrim is true, so
        // the success branch's text is produced on exactly the frame it can no longer be logged on.
        // Its absence from 94 flights was read as the gate never opening -- `docs/MIRV-NEXT.md`
        // item 8w. A gate whose only observable is its failures is not an instrument.
        if (!_saidCleared && clearance.IsClear && _didSplit && clearance.Said.Length > 0)
        {
            _saidCleared = true;
            Log.Info($"clearance on {KsaWorld.DisplayName(Craft)}: {clearance.Said}");
        }

        _mayTrim = plan.MayTrim;

        _trim.Begin();

        double3 nose = Vec.Zero;
        double3 right = Vec.Zero;
        double3 down = Vec.Zero;

        // A frame that will not resolve is handed in as zeroes rather than skipped, because the
        // trim's own budget is what has to bound a part tree that never comes back - and it can
        // only run that budget down if it is being stepped.
        if (Parent is { } parent)
        {
            KsaWorld.TryControlFrameCci(Craft, parent, out nose, out right, out down);
        }

        // The trajectory the guidance flew to, which is the pair the arc and the cutoff position
        // make, and how far along it the vehicle should be by now.
        double3 referenceVelocity = Program.Arc?.RequiredVelocityCci ?? Vec.Zero;

        TrimCommand trim = _trim.Update(simStep, new TrimSituation(
            state.Body, state.PositionCci, state.VelocityCci,
            Program.ReferencePositionCci, referenceVelocity, Program.SecondsSinceReference,
            nose, right, down, _mayTrim, budget, _keepOutTowardCci,
            plan.CeilingMetresPerSecond));

        VehicleCommand.DriveTranslation(Craft, trim.Fire);

        // The post-boost passes. With the thrusters quiet and the nose steady the correction gets a
        // clean look at where the bus is actually going; moving the aim and re-solving the arc from
        // here gives the trim something new to null onto, and the trim is the only thing left
        // aboard that can still move the impact. A trim that has given up ends it, because further
        // passes have no actuator.
        //
        // The same call ReleaseImpulseCci() feeds the prediction, so what the sequencer watches for
        // steadiness is the term that actually moves the reading rather than a proxy for it.
        ReleaseAnArrivalTheTrimCannotFly(trim);

        // What this pass is asking for, kept so the next one can be compared against it. Size alone
        // cannot separate a large correction the geometry needs from a loop winding itself up.
        if (double.IsFinite(trim.ToGainMetresPerSecond) && trim.ToGainMetresPerSecond > 0.0)
        {
            _demandThisPass = trim.ToGainMetresPerSecond;
        }

        int passesBefore = _postBoost.Cycles;

        MeasureHoldingCost();

        PostBoostAim.Decision pass = _postBoost.Update(simStep, new PostBoostSituation(
            TrimSettled: _trim.Done,
            ReleaseDirectionCci: ReleaseImpulseCci(),
            PredictedMissMetres: _freshMiss,
            AimHasSettled: _aim.Settled,
            TrimGaveUp: _trim.GaveUp,
            TrimSpentMetresPerSecond: _trim.SpentMetresPerSecond,
            HoldingCostMetresPerSecond: double.IsFinite(_holdingCost)
                                            ? _holdingCost
                                            : Config.HoldingCostMetresPerSecond));

        if (pass.MayMeasure) _measureDue = true;

        // A demand that has grown half again since the last pass is the correction and the trim
        // driving each other rather than the shot needing more. Only checked where the ceiling has
        // been widened to the budget: the constant is its own guard otherwise, and this is what
        // makes widening it defensible rather than a licence to spend the tank on a wind-up.
        if (Config.TrimCeilingFromBudget && !_saidRunaway
            && PostCutoffSequence.IsRunaway(_demandThisPass, _demandLastPass))
        {
            _saidRunaway = true;
            Log.Info($"post-boost on {KsaWorld.DisplayName(Craft)}: the demand grew from "
                     + $"{_demandLastPass:F2} to {_demandThisPass:F2} m/s across passes, which is a "
                     + "wind-up rather than a larger shot; keeping the best aim found");
            _aim.Freeze();
        }

        if (_postBoost.Cycles > passesBefore)
        {
            _demandLastPass = _demandThisPass;
            // Consumed, so the next decision waits for a reading taken after this correction has
            // actually been flown rather than re-reading the one that prompted it.
            _freshMiss = double.NaN;
            Program.CorrectCoastArc();
            _trim.Resume();
            Say($"post-boost: {pass.Said}", "");
        }

        // The reason it stopped, which Say above cannot report: that fires on a cycle being taken,
        // and finishing is precisely the decision that takes none. It is the line that says how
        // much of the miss was still on the table and why it was left there -- the largest term in
        // where the warheads land, and until now the only one never written down.
        if (pass.MayRelease && !_postBoostSaid)
        {
            _postBoostSaid = true;

            // Named, because this is the line that says which rule ended the correction and that
            // is the largest single term in where the warheads land -- a loop that finished landed
            // at 140 m and every other ending at 5 to 45 km. Unattributed it can only be read once
            // per shot and then spread across every rocket in the world, which reports eight
            // flights of one craft's outcome: `docs/MIRV-NEXT.md` 8z's n=40 was six shots.
            //
            // Log.Info rather than Say: Say dedupes on the sentence's shape and drives the panel's
            // trim line, and this is neither a trim state nor one that repeats.
            Log.Info($"post-boost on {KsaWorld.DisplayName(Craft)}: {pass.Said}");

            // Keep the best aim the passes found, not the last one they tried. AimCorrection reverts
            // to its own best when *it* decides to stop -- but the sequencer above stops it for
            // reasons the loop knows nothing about, and on those the bias is left wherever the final
            // pass put it. The miss is not monotonic in the aim, so that is routinely worse: flown at
            // 12,902 km, a run read 2.1 km at pass 2, 6.0 at pass 3, 4.5 at pass 4 and released on
            // the 4.5. Freeze is the existing "stop and keep the best" and costs nothing when the
            // loop had already settled.
            _aim.Freeze();
        }

        if (!double.IsFinite(_owedAtSplit) && double.IsFinite(trim.ToGainMetresPerSecond))
        {
            _owedAtSplit = trim.ToGainMetresPerSecond;
        }

        // Deliberately NOT dropped when the trim reports done. A post-boost pass calls
        // _trim.Resume(), so passes keep arriving afterwards -- and those are the large ones. With
        // the reference gone they read "waiting to clear the spent stack, which cannot be read" and
        // fall through to SeparationClearance's 20 s clock, so the dangerous passes were exactly
        // the ones flying blind. This is not the reverted clearance latch: that cached a stale
        // ANSWER, and this keeps the QUESTION askable.

        // Said once per change. A trim that stalls looks exactly like one that has finished, and
        // the difference between them is kilometres on the ground.
        if (trim.Said.Length == 0) return;

        // Two audiences, one state. The panel gets a sentence it can fit; the log gets the numbers
        // that diagnose it, which are long enough to run off the edge of a narrow window.
        Say(_mayTrim ? trim.Said : clearance.Said + "; " + trim.Said,
            (trim.Acceleration > 0.0 ? $" (thrusters measured at {trim.Acceleration:F3} m/s2)" : "")
            + Grew()
            + Arrivals());
    }

    // How often the coast is written down, in simulated seconds.
    private const double CoastProbeSeconds = 10.0;

    private double _sinceCoastProbe;
    private double _coastProbeMiss = double.NaN;

    // The 168 seconds between cutoff and the first release, which is the one window in a flight that
    // nothing instruments. Shot 006 of 2026-09-02-1508 put all eight rockets 75-99 km out, and the
    // whole divergence happened in here: healthy through cutoff, healthy for 65 s of coast, then the
    // predicted impact walks off at 252-342 m/s of simulated time in a straight line. The phase line
    // fires on change and the warhead trace starts at release, so the ramp was only ever visible as
    // a DEBUG stream nobody reads.
    //
    // Rate as well as position, because the discriminator is what KIND of ramp it is: a constant
    // drift is the bus's own state moving, a growing one is the prediction diverging from a state
    // that is not.
    private void ProbeTheCoast(double simStep)
    {
        if (Program.Phase != IcbmPhase.Coast || _salvoAway)
        {
            _sinceCoastProbe = 0.0;
            _coastProbeMiss = double.NaN;
            _coastProbeHasState = false;
            return;
        }

        _sinceCoastProbe += simStep;
        if (_sinceCoastProbe < CoastProbeSeconds) return;

        double interval = _sinceCoastProbe;
        _sinceCoastProbe = 0.0;

        if (PredictedImpact is not { } hit) return;

        double miss = PredictedMissMetres;
        double rate = double.IsFinite(_coastProbeMiss) && interval > 0.0
                          ? (miss - _coastProbeMiss) / interval
                          : double.NaN;
        _coastProbeMiss = miss;

        if (Parent is not { } parent) return;

        doubleQuat cce2Cci = parent.GetCce2Cci();
        double3 positionCci = (KsaWorld.PositionEcl(Craft) - parent.GetPositionEcl()).Transform(cce2Cci);
        double3 velocityCci = (KsaWorld.VelocityEcl(Craft) - parent.GetVelocityEcl()).Transform(cce2Cci);

        // Zero above ~209 km, measured over 6,181 samples with no exception -- so this column is a
        // guard rather than a reading. Every failure path in KsaWorld.MediumDensityRatioAt returns
        // sea-level air, which here would bend the predicted arc down and land it short; nothing
        // else in the flight would say so. docs/ACCURACY-PLAN.md 3an.
        double density = DensityRatioAt(positionCci);

        // And where the impact is walking to, not just how far. A miss that grows is one number; a
        // miss that grows because the impact is marching along the track is a different fault from
        // one that grows because it is sliding across it.
        string lands = "";

        try
        {
            double3 cce = hit.GroundFixedPointCci.Transform(parent.GetCci2Cce());
            lands = $", lands {parent.GetLatitudeFromCce(cce):F3},{parent.GetLongitudeFromCce(cce):F3}";

            // The miss is an angle between two points and either of them can be what moves. The
            // aim is built from a latitude and longitude the operator set and never changes, so
            // reading it back through the same frames has one right answer -- and a readback that
            // drifts is the conversion rather than the trajectory. Every computer in the world
            // shares those frames, which is the only thing so far that would explain eight
            // predictions moving in one frame. docs/ACCURACY-PLAN.md item 17.
            double3 aimCce = _trueAimCci.Transform(parent.GetCci2Cce());
            double aimLat = parent.GetLatitudeFromCce(aimCce);
            double aimLon = parent.GetLongitudeFromCce(aimCce);

            lands += $", aim reads {aimLat:F3},{aimLon:F3}"
                     + $" (set {Target.LatitudeDeg:F3},{Target.LongitudeDeg:F3})";
        }
        catch
        {
            // A frame the engine will not convert says nothing about the flight; the rest of the
            // line is still worth having.
        }

        // What the coast is doing that gravity does not account for. A ballistic coast is an exact
        // function of one state, so propagating the previous probe's state forward under gravity
        // alone and differencing gives the non-gravitational part directly -- and that is the only
        // way to see it: the walk is about half a metre a second and is invisible in a printed
        // speed. Log it as a VECTOR in a radial/along/cross basis -- a magnitude alone read as
        // along-track once and the mechanism is cross-track. docs/ACCURACY-PLAN.md 3as.
        double pushMps = double.NaN;
        double pushRadial = 0.0, pushAlong = 0.0, pushCross = 0.0;

        if (_coastProbeHasState
            && Kepler.TryCoast(KsaWorld.BodyMu(parent), _coastProbePosCci, _coastProbeVelCci, interval,
                               out _, out double3 coastedVelCci))
        {
            double3 slip = velocityCci - coastedVelCci;
            if (Vec.IsFinite(slip))
            {
                pushMps = Vec.Len(slip);

                // The basis the walk is actually levered through. Cross-track is the one that
                // moves an impact without moving the conic -- a normal impulse does no work and
                // leaves |h| alone -- so a push that shows up here and not in the energy is the
                // signature to look for.
                double3 radial = Vec.Unit(positionCci);
                double3 cross = Vec.Unit(Vec.Cross(positionCci, velocityCci));
                double3 along = Vec.Cross(cross, radial);

                pushRadial = Vec.Dot(slip, radial);
                pushAlong = Vec.Dot(slip, along);
                pushCross = Vec.Dot(slip, cross);
            }
        }

        _coastProbePosCci = positionCci;
        _coastProbeVelCci = velocityCci;
        _coastProbeHasState = true;

        // Whether the state being read belongs to this frame. A clock that parts from the engine's
        // is world-level by construction, and no other reading in this mod would see it.
        double stateEpoch = KsaWorld.StateEpochSeconds(Craft);
        double clockGap = double.IsFinite(stateEpoch) ? KsaWorld.SimClockSeconds - stateEpoch : double.NaN;

        // Whether anything is perturbing the bus while it waits, and how long the correction has
        // gone without a reading. A coast is an exact function of one state, so a predicted impact
        // that walks means that state is moving, and a perturbation far too small to see as a
        // speed is tens of kilometres of impact -- cross-track, where the lever is the orbit
        // radius over the angular momentum rather than this arc's 0.4-2.9 km per m/s along it. The rationing is the other half: one reading taken 975 s after the last is a
        // full-size correction nothing has verified. docs/ACCURACY-PLAN.md item 17.
        // On rails the engine propagates this vehicle as an exact conic and the step cannot matter;
        // off rails it integrates, and the truncation scales with a step the nominal warp figure
        // does not show. A prediction that starts walking on a coast is either that transition or
        // the governor behind it, and neither is visible anywhere else. docs/ACCURACY-PLAN.md 17.
        bool? rails = KsaWorld.OnRails(Craft);

        string loop = $", {(rails is null ? "rails unknown" : rails.Value ? "on rails" : "off rails")}"
                      + (KsaWorld.ForcedOffRails ? " (forced)" : "")
                      + $", trim {(TrimIsFiring ? "firing" : _trim.Done ? "done" : "idle")}"
                      + $", {_sinceObserve:F0} s since the aim last read"
                      + (_measureDue ? ", reading due" : "");

        Log.Info($"coast probe on {KsaWorld.DisplayName(Craft)}: "
                 + $"{AltitudeMetres / 1000.0:F1} km, {Vec.Len(velocityCci):F1} m/s, "
                 + $"r_dot {Vec.Dot(velocityCci, Vec.Unit(positionCci)):+0.0;-0.0} m/s, "
                 + $"density {density:E2}, "
                 + $"predicted miss {miss / 1000.0:F2} km"
                 + (double.IsFinite(rate) ? $" moving {rate:+0.0;-0.0} m/s" : "")
                 + $", arrives in {hit.Seconds:F0} s, "
                 + $"committed {Program.CommittedArrivalFromNow:F0} s"
                 + lands
                 + $", state {clockGap:+0.000;-0.000} s behind"
                 + (double.IsFinite(pushMps)
                        ? $", off-gravity {pushMps:F4} m/s"
                          // Three sections, not two: a component that lands on negative zero takes
                          // the sign from the value and the body from the POSITIVE section, which
                          // prints "-+0.0000" and breaks anything reading the column back.
                          + $" (r {pushRadial:+0.0000;-0.0000;0.0000}"
                          + $", a {pushAlong:+0.0000;-0.0000;0.0000}"
                          + $", c {pushCross:+0.0000;-0.0000;0.0000})"
                        : "")
                 + $", plan {KsaWorld.FlightPlanMarginSeconds(Craft):F1} s"
                 + $", bubble {KsaWorld.BubbleVehicleCount(Craft)}"
                 + loop
                 + $", release in {IcbmProgram.Clock(SecondsToReleaseApproach)}");
    }

    // The one state the latch cannot get itself out of. The arrival is pinned during the burn and
    // both branches that unpin it live there, so after cutoff it stands whatever the trajectory
    // does -- and what the trim is asked for is RequiredVelocity(arrival) - v, worth about 2.35 m/s
    // per second the arrival is out. BusTrim's ceiling is crossed at 4.3 s, and past it the trim
    // refuses before its first pulse and the warheads go out untrimmed: flown once in twelve shots,
    // all eight rockets 75-99 km out on burns nothing was wrong with.
    //
    // Asked only of a state that is ALREADY LOST -- the trim over its ceiling, having spent nothing
    // -- rather than on a threshold of its own. A guard that fires on a number has to be right about
    // the number; this one fires where the alternative is a certain 90 km, so being wrong about it
    // costs a re-solve.
    //
    // Once. A second release would be the cycle the latch exists to prevent, and IcbmProgram
    // re-latches nothing after cutoff, so this cannot become a loop.
    private void ReleaseAnArrivalTheTrimCannotFly(in TrimCommand trim)
    {
        if (_releasedTheArrival || !_trim.GaveUp) return;
        if (_trim.SpentMetresPerSecond > 0.0) return;
        if (!(trim.ToGainMetresPerSecond > BusTrim.MaxMetresPerSecond)) return;

        double committed = Program.CommittedArrivalFromNow;
        double flown = PredictedImpact?.Seconds ?? double.NaN;

        if (!double.IsFinite(committed) || !double.IsFinite(flown)) return;

        _releasedTheArrival = true;

        if (!Program.ReleaseArrival()) return;

        _trim.Begin();

        Log.Info($"arrival released on {KsaWorld.DisplayName(Craft)}: the trim was asked for "
                 + $"{trim.ToGainMetresPerSecond:F1} m/s against a {BusTrim.MaxMetresPerSecond:F0} "
                 + $"ceiling and had spent nothing, solving to an arrival {committed:F0} s away "
                 + $"where the flown prediction says {flown:F0}. Giving the arrival up and taking "
                 + "the cheapest arc again.");
    }

    private bool _releasedTheArrival;

    // Which arrival the trim is solving to, beside when the flown prediction says the warheads
    // actually get there. What the trim nulls is RequiredVelocity(arrival) - v, and that required
    // velocity moves about 2.35 m/s for every second the arrival is out at 12,902 km -- so
    // BusTrim.MaxMetresPerSecond is crossed at 4.3 s of disagreement, and a trim asking for tens is
    // a handful of seconds long before it is anything wrong with the vehicle.
    //
    // Printed unconditionally, and that is the point. It used to fire only once the demand was
    // already over the ceiling, so it could report the disagreement's tail and never its
    // distribution -- a 96-flight night read as 1 s on eleven shots and 26 s on one, which was the
    // logger describing its own trigger rather than the fault. Anything under the ceiling was
    // invisible.
    //
    // The two are not the same quantity and need not match: the arrival is when a vacuum transfer
    // reaches the aim point, the prediction is when a warhead with drag reaches the ground. Their
    // gap is the measurement, not an error on its face.
    private string Arrivals()
    {
        double committed = Program.CommittedArrivalFromNow;
        double flown = PredictedImpact?.Seconds ?? double.NaN;

        if (!double.IsFinite(committed)) return " [no committed arrival]";

        return double.IsFinite(flown)
            ? $" [solving to an arrival {committed:F0} s away; the flown prediction says {flown:F0} s]"
            : $" [solving to an arrival {committed:F0} s away; nothing predicted]";
    }

    // What coasting clear cost, said only when it cost something. The same number at the split and
    // at the release means the wait was free and the error came off the decoupler; a number that
    // has grown means something moved the vehicle or the aim while it waited, which is a different
    // fault with a different fix.
    private string Grew()
    {
        double atRelease = _trim.AtReleaseMetresPerSecond;

        if (!double.IsFinite(atRelease) || !double.IsFinite(_owedAtSplit)) return "";
        if (Math.Abs(atRelease - _owedAtSplit) < 0.05) return "";

        return $" [owed {_owedAtSplit:F2} m/s at the split, {atRelease:F2} after "
               + $"{_sinceSplit:F0} s of clearing]";
    }

    /// <summary>Let one warhead go at the aim point, if there is one to let go and it is ready.</summary>
    public bool Release(IManualFire? weapon)
    {
        if (weapon is null || !weapon.ReadyToFire) return false;
        if (TargetEcl() is not { } targetEcl) return false;

        bool away = weapon.FireAt(targetEcl);

        if (away)
        {
            // Captured on the first one away, because it is the only instant the magazine's loaded
            // count is still readable: it reloads a few seconds after the salvo, which is exactly
            // what left the coast warp dead for weeks. WarheadsAway only ever increases, so the two
            // together are a monotonic "the salvo is finished".
            if (WarheadsAway == 0)
            {
                _salvoSize = 1 + weapon.TubesReadyToFire;
                SayWhatTheLoopLeft();
                SayWhatTheGroundUnderTheAimIsLike();
            }

            WarheadsAway++;
            ProbeRelease();
            BeginTrace(weapon);
        }

        return away;
    }

    // Everything the correction loop will ever do is over by the first release, and until now none
    // of it survived the flight at INFO: the response and the plant readings were DEBUG lines
    // buried in hundreds of per-cycle ones, the release residual only appeared when the trim
    // changed what it was doing, and the arrival angle was printed only when a floor was asked for
    // and could not be met -- so a baseline shot never recorded the one number cot(gamma) says
    // dominates its precision.
    private void SayWhatTheLoopLeft()
    {
        double arrival = Program.Arc?.ArrivalAngleDeg ?? double.NaN;

        Log.Info($"release summary on {KsaWorld.DisplayName(Craft)}: "
                 + $"cut off {Program.ResidualAtCutoff:F3} m/s short, "
                 + $"trim owed {Rate(_owedAtSplit)} at the split and "
                 + $"{Rate(_trim.AtReleaseMetresPerSecond)} on release "
                 + $"({Rate(_trim.SpentMetresPerSecond)} spent"
                 + (_trim.GaveUp ? ", GAVE UP" : _trim.Done ? ", done" : ", still running") + "), "
                 + (double.IsFinite(arrival) ? $"arriving at {arrival:F1} deg" : "")
                 + FloorSaid()
                 + (double.IsFinite(arrival) ? ", " : "")
                 + $"aim response {_aim.Response:F2} (raw {_aim.LastRawResponse:F2}) off "
                 + $"{_aim.PlantMeasurements} plant reading(s), "
                 + $"bias {Vec.Len(_aim.BiasCci) / 1000.0:F1} km, "
                 + $"best {_aim.BestMissMetres / 1000.0:F2} km, worse for {_aim.WorseFor}");
    }

    // What the ground the warheads are about to cross actually does, once per flight, beside the
    // release summary.
    //
    // The headless fixture is faithful to KSA's declared erosion spectrum and *undamped*; the game
    // scales every octave by the biome weight, a gradient-falloff power and `1 - |dot|`, and
    // docs/KSA-TERRAIN.md says of that product only that it is unmeasured. This is the measurement.
    // What matters is the amplitude surviving *below a kilometre of wavelength*, because that is
    // the band a round crossing a kilometre of ground per frame cannot resolve --
    // docs/ACCURACY-PLAN.md 3ae.
    private void SayWhatTheGroundUnderTheAimIsLike()
    {
        if (Parent is not { } parent) return;

        try
        {
            double3 up = Vec.Unit(_trueAimCci);
            double3 back = Program.CutoffPositionCci - _trueAimCci;
            double3 along = Vec.Unit(back - up * Vec.Dot(back, up));

            if (!Vec.IsFinite(along) || Vec.Len2(along) < 0.5) return;

            const int Half = 100;
            const double Spacing = 25.0;
            const int Window = 40;

            double[] height = new double[2 * Half + 1];

            for (int i = 0; i < height.Length; i++)
            {
                height[i] = TerrainRadiusAt(_trueAimCci + along * ((i - Half) * Spacing))
                            - parent.MeanRadius;
            }

            double lo = double.MaxValue, hi = double.MinValue;
            double fineLo = double.MaxValue, fineHi = double.MinValue, sumSquares = 0.0;
            int counted = 0;

            for (int i = 0; i < height.Length; i++)
            {
                lo = Math.Min(lo, height[i]);
                hi = Math.Max(hi, height[i]);

                // High-pass by subtracting a one-kilometre boxcar, which leaves exactly what a
                // sample grid coarser than that steps over.
                int from = i - Window / 2, to = i + Window / 2;
                if (from < 0 || to >= height.Length) continue;

                double mean = 0.0;
                for (int k = from; k <= to; k++) mean += height[k];
                mean /= to - from + 1;

                double residual = height[i] - mean;

                fineLo = Math.Min(fineLo, residual);
                fineHi = Math.Max(fineHi, residual);
                sumSquares += residual * residual;
                counted++;
            }

            if (counted == 0) return;

            Log.Info($"ground under the aim on {KsaWorld.DisplayName(Craft)}: "
                     + $"{height.Length} samples over "
                     + $"{(height.Length - 1) * Spacing / 1000.0:F1} km of the approach, "
                     + $"swing {hi - lo:F1} m, below a 1 km wavelength "
                     + $"{fineHi - fineLo:F1} m peak-to-peak and "
                     + $"{Math.Sqrt(sumSquares / counted):F1} m rms");
        }
        catch (Exception e)
        {
            Log.Warn($"could not profile the ground under the aim: {e.Message}");
        }
    }

    // What bounded the search, and what the fraction was applied to. Silent for a shot that asked
    // for nothing, which is what ships -- and both numbers or neither, because a floor without its
    // multiplicand cannot be read back into the preference that produced it.
    private string FloorSaid()
    {
        double floor = Program.ArrivalFloorDeg;
        double from = Program.ArrivalFloorFromDeg;

        if (!double.IsFinite(floor)) return "";

        return double.IsFinite(from)
            ? $" against a {floor:F1} deg floor, {Config.ArrivalPreference:P0} of the {from:F1} "
              + "deg the tanks could afford"
            : $" against a {floor:F1} deg floor";
    }

    // Most of a flight is spent before either trim number exists, and "NaN m/s" in a summary reads
    // as a fault rather than as a measurement nothing has taken yet.
    private static string Rate(double metresPerSecond)
        => double.IsFinite(metresPerSecond) ? $"{metresPerSecond:F2} m/s" : "nothing";

    // One warhead per designation, and it is the first away. A salvo leaves inside a tenth of a
    // second and lands in a group tens of metres wide, so any of the six answers the question and
    // six traces answer it six times.
    //
    // The round is reached by casting past IManualFire on purpose: a launcher owes a ballistic
    // computer the ability to shoot and nothing else, and widening that role for a diagnostic would
    // put rounds in front of everything else that takes it.
    private void BeginTrace(IManualFire weapon)
    {
        if (!_traceWanted || _tracedThisShot) return;
        if (TraceSetup() is not { } setup) return;
        if (weapon is not IRoundsInFlight inFlight) return;

        // The round just fired is the one just appended - nothing runs between FireAt and here.
        if (inFlight.Rounds is not { Count: > 0 } rounds) return;

        _tracedThisShot = true;
        _trace.Begin(rounds[^1], setup);
    }

    private void StepTrace(double simStep)
    {
        if (!_traceWanted) { _trace.Forget(); return; }
        if (!_trace.Watching) return;
        if (TraceSetup() is not { } setup) return;

        _trace.Update(simStep, setup);
    }

    private WarheadTrace.Setup? TraceSetup()
    {
        if (Parent is not { } parent) return null;
        if (_warhead is not { } warhead) return null;

        return new WarheadTrace.Setup(parent, Body, warhead, _trueAimCci, PredictStepSeconds,
                                      _terrainRadius ??= TerrainRadiusAt,
                                      _densityRatio ??= DensityRatioAt);
    }

    // What the prediction says about the state the warhead is actually leaving on, beside where the
    // round then lands. The phase line cannot answer this: it is printed before the frame's Predict
    // and while the engines are still lit, so it carries a prediction of the *solved* cutoff arc.
    // Only these two numbers isolate what the prediction and the round still disagree about, which
    // is the difference every remaining metre of the miss lives in.
    //
    // At INFO, and it earns it: this fires once per warhead released rather than per frame, and a
    // diagnostic nobody has switched on is one that is never there in the salvo that needed it.
    private void ProbeRelease()
    {
        if (Parent is not { } parent) return;
        if (_warhead is not { } warhead) return;

        try
        {
            doubleQuat cce2Cci = parent.GetCce2Cci();
            double3 positionCci = (KsaWorld.PositionEcl(Craft) - parent.GetPositionEcl()).Transform(cce2Cci)
                                  + ReleaseOffsetCci();
            double3 velocityCci = (KsaWorld.VelocityEcl(Craft) - parent.GetVelocityEcl()).Transform(cce2Cci)
                                  + ReleaseImpulseCci();

            if (!ImpactPredictor.TryPredict(Body, positionCci, velocityCci, PredictStepSeconds,
                                            ImpactPredictor.DefaultMaxSeconds,
                                            out ImpactPredictor.Impact hit, TerrainRadiusAt, null,
                                            new ImpactPredictor.Drag(DensityRatioAt, warhead)))
            {
                Log.Info("release probe: no impact predicted from the release state");
                return;
            }

            double3 cce = hit.GroundFixedPointCci.Transform(parent.GetCci2Cce());
            double miss = Body.SurfaceRadius * Vec.AngleBetween(hit.GroundFixedPointCci, _trueAimCci);

            // The angle this reports has to match the one the release line reports for the tube.
            // The prediction throws the warhead along the direction the vehicle was commanded to
            // hold; the round actually leaves along its tube. If those disagree, two metres a
            // second is being applied in the wrong direction, and radially that is 3.4 km per m/s.
            double3 impulse = ReleaseImpulseCci();
            string thrown = impulse.Equals(Vec.Zero)
                ? ""
                : $", {(_releaseMeasured ? "measured" : "assumed")} thrown "
                  + $"{Vec.AngleBetween(impulse, velocityCci) * 180.0 / Math.PI:F0} deg from the "
                  + $"platform's track, {Vec.Len(ReleaseOffsetCci()):F1} m off the orbit position";

            // The warheads' own ETA, latched here and never rewritten. This probe is flown from the
            // state a warhead actually left in, so it is the one honest arrival time there is --
            // and after this instant Predict is flying the *bus*, which coasts on to its own impact
            // about half a minute later. Letting that overwrite the readout is what made a correct
            // countdown reach zero as the warheads landed and then jump back to twenty seconds.
            _arrivalLeft = hit.Seconds;
            _salvoAway = true;

            Log.Info($"release probe: predicted from the release state -> "
                      + $"{parent.GetLatitudeFromCce(cce):F3},{parent.GetLongitudeFromCce(cce):F3}, "
                      + $"{miss / 1000.0:F1} km from the target, {hit.Seconds:F0} s of flight{thrown}");
        }
        catch
        {
            // A probe that throws inside the frame hook is worse than one that says nothing.
        }
    }

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

    // Where the mod thinks the arc lands, as a place rather than a distance. A distance says the
    // solution is wrong; the place says which way, and short-versus-sideways are different faults.
    private string PredictedImpactSaid()
    {
        if (!double.IsFinite(PredictedMissMetres) || PredictedImpact is not { } hit) return "";
        if (Parent is not { } parent) return "";

        try
        {
            // The prediction is un-carried to its own epoch, so it is a place on the ground in the
            // same terms the aim point is - which is what makes the two comparable at all.
            double3 cce = hit.GroundFixedPointCci.Transform(parent.GetCci2Cce());

            return $", own prediction {PredictedMissMetres / 1000.0:F1} km off "
                   + $"(lands {parent.GetLatitudeFromCce(cce):F3},{parent.GetLongitudeFromCce(cce):F3})";
        }
        catch
        {
            return $", own prediction {PredictedMissMetres / 1000.0:F1} km off";
        }
    }

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
    public double3? TargetEcl()
    {
        if (Parent is null || !Target.IsSet) return null;
        return SurfacePointEcl(Parent, Target.LatitudeDeg, Target.LongitudeDeg);
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

        return new IcbmState(Body, positionCci, velocityCci, aimCci, hasAim, booster, density,
                             Craft.IsAnyEnginePropellantAvailable(), _throttleAchieved, playerStep,
                             _aim.IsSteady, StackDeltaV(), StructuralLimitGee());
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
    // is why a multi-stage rocket no longer reports itself unreachable while sitting on the pad
    // with the range to spare. NaN when it cannot be read, which puts the single-stage estimate
    // back rather than claiming a stack has nothing.
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
            double3 nowCci = _departsIn > 0.0 ? Body.CarryCci(pointCci, -_departsIn) : pointCci;
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

    // What the launcher should be holding, and whether a round may go. The sequencer turns the
    // vehicle so the tube about to fire lies on the line the aim correction assumed - which for a
    // launcher whose tubes are not canted is the line it is already on, and costs nothing.
    private ReleaseCommand DriveDeployment(double simStep, IManualFire? weapon, in IcbmState state)
    {
        double3 held = Command.ThrustDirectionCci;
        double3 roll = _rollReference;

        // The trim is a precondition of being ready rather than a step inside the sequence, and
        // that is what keeps the sequencer's reference honest: it latches the tube axes on the
        // first frame the launcher is both ready and settled, and a reference latched before the
        // decoupler's shove has been taken back out describes a line no warhead will leave on.
        bool trimming = Config.TrimBeforeRelease && Command.ReadyToDeploy && !_trimAbandoned
                        && (!_trim.Done || _postBoost.Correcting);

        if (weapon is null || !Command.ReadyToDeploy || trimming)
        {
            return new ReleaseCommand(held, roll, false, -1, 0.0, "");
        }

        int next = weapon.NextTube;

        // One line per flight, whether or not anything went wrong, said the frame the magazine
        // empties -- which is the last moment the bus manoeuvres near what it dropped, and so the
        // moment the minimum is final. It is a measurement rather than a gate: the 2026-08-25
        // collision was inferred from a thrashing trim rather than observed, and a shot that grazes
        // the stack and survives leaves no other trace.
        if (!_saidProximity && _didSplit && next < 0 && weapon.TubesReadyToFire == 0)
        {
            _saidProximity = true;
            Log.Info($"{KsaWorld.DisplayName(Craft)}: {_proximity.Closest.Said}");
        }

        // Latched once the launcher is ready to deploy and the split's transient has died down —
        // not once it is steady enough to release, which is a far tighter number and one a light
        // bus may never reach.
        if (!_sequence.Begun && Config.RepointBetweenReleases
            && !(_tubeSpinSpeed > ReleaseSequence.SteadyToLatchMetresPerSecond))
        {
            int found = weapon.TubeAxesEcl(_tubeAxes);
            if (found > 0 && Parent is { } parent)
            {
                doubleQuat cce2Cci = parent.GetCce2Cci();
                for (int i = 0; i < found; i++) _tubeAxes[i] = _tubeAxes[i].Transform(cce2Cci);

                if (_sequence.Begin(_tubeAxes.AsSpan(0, found)))
                {
                    Log.Info($"aiming each of {found} tube(s) before it fires");
                }
            }
        }

        double3 nextAxis = Vec.Zero;
        double3 noseAxis = Vec.Zero;

        if (next >= 0 && Parent is { } body)
        {
            int live = weapon.TubeAxesEcl(_tubeAxes);

            if (live > next)
            {
                doubleQuat cce2Cci = body.GetCce2Cci();
                for (int i = 0; i < live; i++) _tubeAxes[i] = _tubeAxes[i].Transform(cce2Cci);

                nextAxis = _tubeAxes[next];

                // The launcher's own axis, read the same way the reference was: the cants cancel in
                // the mean. It is what the turn is applied to, so it has to be measured now rather
                // than taken from the attitude the vehicle was asked for.
                noseAxis = ReleasePointing.ReferenceAxis(_tubeAxes.AsSpan(0, live));
            }
        }

        // How long the release window has left, from the descent rather than from the arrival: it
        // closes when the launcher falls through the deploy altitude, not when the rounds land.
        double descent = -Vec.Dot(state.VelocityCci, state.UpCci);
        double window = descent > 0.0
                            ? (AltitudeMetres - Config.DeployAltitudeMetres) / descent
                            : double.NaN;

        return _sequence.Update(simStep, new ReleaseSituation(
            // The honest count, not a floor of one. The share-of-the-window division guards zero
            // itself, and the sequencer has to see the magazine reach empty -- that is what ends
            // the deployment, and a launcher reloads a few seconds later.
            ReadyToDeploy: true, NextTube: next, TubesLeft: weapon.TubesReadyToFire,
            NextTubeAxisCci: nextAxis, NoseAxisCci: noseAxis, SweepMetresPerSecond: _tubeSpinSpeed,

            // Off the munition rather than assumed: it is what turns a tube's cant into the lateral
            // velocity the release is budgeted in, and it belongs to the round rather than to the
            // sequencer. A launcher carrying nothing prices a cant at nothing, which is right —
            // there is no round to throw off the line.
            EjectionMetresPerSecond: _warhead?.LaunchSpeed ?? 0.0,
            SecondsLeftToDeploy: window, HeldDirectionCci: held, HeldRollCci: roll));
    }

    // Where a released round starts, as a difference from where the craft is.
    //
    // A difference rather than a state, because the prediction is taken from two different places -
    // the solved cutoff while burning, the live state while coasting - and the tube's offset from
    // the craft applies to both. It carries all three things a prediction taken from the orbit
    // state gets wrong: the tube mouth is metres away, the lever arm is sweeping, and the round is
    // thrown along the tube rather than along whatever attitude was commanded.
    private void MeasureRelease(IManualFire? weapon)
    {
        _releaseMeasured = false;
        _releaseOffsetCci = Vec.Zero;
        _releaseKickCci = Vec.Zero;
        _tubeSpinSpeed = 0.0;

        if (weapon is null || Parent is not { } parent) return;

        try
        {
            if (!weapon.TryMeanReleaseStateEcl(out double3 positionEcl, out double3 velocityEcl,
                                               out double spinSpeed))
            {
                return;
            }

            _tubeSpinSpeed = spinSpeed;

            doubleQuat cce2Cci = parent.GetCce2Cci();
            double3 offset = (positionEcl - KsaWorld.PositionEcl(Craft)).Transform(cce2Cci);
            double3 kick = (velocityEcl - KsaWorld.VelocityEcl(Craft)).Transform(cce2Cci);

            if (!Vec.IsFinite(offset) || !Vec.IsFinite(kick)) return;

            _releaseOffsetCci = offset;
            _releaseKickCci = kick;
            _releaseMeasured = true;
        }
        catch
        {
            // A launcher whose tubes will not resolve falls back to the commanded attitude below.
        }
    }

    // The fallback when the tubes cannot be resolved: the munition's ejection speed along the
    // direction the vehicle was told to hold. Wrong by however far the vehicle settled off that
    // command, which is why it is second choice rather than the rule.
    // Once a pass rather than once a solve: it is four impact predictions, the solve runs several
    // times a second, and the answer is a property of the trajectory, which moves over minutes.
    // A refusal leaves the previous measurement standing.
    private void MeasureHoldingCost()
    {
        if (!Config.DeriveHoldingCost) { _holdingCost = double.NaN; return; }
        if (Parent is not { } parent || _warhead is not { } warhead) return;
        if (_postBoost.Cycles == _holdingCostForPass) return;

        _holdingCostForPass = _postBoost.Cycles;

        try
        {
            doubleQuat cce2Cci = parent.GetCce2Cci();
            double3 positionCci = (KsaWorld.PositionEcl(Craft) - parent.GetPositionEcl()).Transform(cce2Cci);
            double3 velocityCci = (KsaWorld.VelocityEcl(Craft) - parent.GetVelocityEcl()).Transform(cce2Cci);

            if (HoldingCost.TryMeasure(Body, positionCci, velocityCci, ReleaseImpulseCci(),
                                       PredictStepSeconds, out double measured,
                                       new ImpactPredictor.Drag(_densityRatio ??= DensityRatioAt, warhead)))
            {
                if (!double.IsFinite(_holdingCost) || Math.Abs(measured - _holdingCost) > 0.05)
                {
                    Log.Debug($"holding cost on {KsaWorld.DisplayName(Craft)}: {measured:F2} m/s "
                              + $"measured, against the {PostBoostAim.HoldingCostsMetresPerSecond:F0} "
                              + "the constant assumes");
                }

                _holdingCost = measured;
            }
        }
        catch (Exception e)
        {
            Log.Debug($"holding cost could not be measured: {e.Message}");
        }
    }

    private double3 ReleaseImpulseCci()
    {
        // Once the sequence is turning the vehicle, the line every round leaves on is the latched
        // reference and nothing else. The *live* mean of the tube axes swings by a full cant as
        // each tube is brought onto that line, so predicting with it describes a round nobody is
        // about to release - and feeds the aim correction a target that moves six times a salvo.
        //
        // It is also simply the right number: a re-pointed tube throws the whole LaunchSpeed along
        // the reference, not its cosine.
        if (_sequence.Begun && _warhead is { LaunchSpeed: > 0f } aimed)
        {
            return _sequence.ReferenceCci * aimed.LaunchSpeed;
        }

        if (_releaseMeasured) return _releaseKickCci;
        if (_warhead is not { LaunchSpeed: > 0f } warhead) return Vec.Zero;

        double3 nose = Command.ThrustDirectionCci;
        return nose.Equals(Vec.Zero) || !Vec.IsFinite(nose) ? Vec.Zero : Vec.Unit(nose) * warhead.LaunchSpeed;
    }

    private double3 ReleaseOffsetCci() => _releaseMeasured ? _releaseOffsetCci : Vec.Zero;

    // How thick the air is at a point on the arc. The same field the round's own drag is read from,
    // so the prediction and the round cannot disagree about the atmosphere they are flying through.
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

    private double DensityRatioAt(double3 pointCci)
    {
        if (Parent is not { } parent) return 0.0;

        try
        {
            double3 positionEcl = pointCci.Transform(parent.GetCci2Cce()) + parent.GetPositionEcl();
            double density = KsaWorld.MediumDensityRatioAt(parent, positionEcl);
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

    private void Predict(double simStep, in IcbmState state)
    {
        // Two clocks, because this reading has two consumers with different needs. Guidance reads
        // it while the engines are lit and when a post-boost pass asks, and there it has to keep
        // step with the world. Everything else is a readout, and a readout paced by simulated time
        // costs a whole re-flown trajectory per frame once the coast is warped.
        _sincePredict += simStep;
        _sinceObserve += simStep;
        _sincePredictWall += state.PlayerStepSeconds;

        bool guidanceWants = Program.IsBurning || _measureDue;

        if (guidanceWants
                ? _sincePredict < PredictIntervalSeconds
                : _sincePredictWall < ReadoutIntervalSeconds)
        {
            return;
        }

        _sincePredict = 0.0;
        _sincePredictWall = 0.0;

        // While the engines are running, predict from where the arc *departs* rather than from
        // where the vehicle is. The current state is mid-burn and describes a trajectory nobody
        // intends to fly, so a correction driven by it never sees the shot being aimed - which
        // leaves the aim uncorrected for the whole burn, and by the coast the arc is fixed and the
        // warheads are already going.
        bool fromCutoff = Program.IsBurning && Program.Arc is not null;

        double3 fromCci = fromCutoff ? Program.CutoffPositionCci : state.PositionCci;
        double3 alongCci = fromCutoff ? Program.Arc!.Value.RequiredVelocityCci : state.VelocityCci;

        // How far in the future the predicted arc departs. Zero once the engines are off, and the
        // rest of the burn while they are running.
        double departsIn = fromCutoff && double.IsFinite(Command.SecondsToCutoff)
                         ? Math.Max(0.0, Command.SecondsToCutoff)
                         : 0.0;

        // Held in a field because the terrain callback runs inside the prediction and needs it too.
        _departsIn = departsIn;

        fromCci += ReleaseOffsetCci();
        alongCci += ReleaseImpulseCci();

        // What the predictor is actually a function of. On a coast it is an exact function of this
        // pair, so any wander in its answer is a wander in here -- 0.4 to 2.9 km per m/s along track
        // on this arc, and far more across it. Differencing positions cannot see that: on a coast
        // they move by v*dt whatever is wrong, which is why the earlier probe could only ever report
        // the bus's speed. docs/ACCURACY-PLAN.md 3as.
        _lastPredictedFromVelCci = alongCci;

        // Predicted with the warhead's drag rather than in vacuum. On a shallow deorbit arrival a
        // vacuum arc lands tens of kilometres beyond anything that actually flies it, and the aim
        // correction reads its own drag-free prediction - so it converges, reports zero, and the
        // rounds go on falling short. Measured at 54.6 km.
        ImpactPredictor.Drag? air =
            _warhead is { } warhead ? new ImpactPredictor.Drag(DensityRatioAt, warhead) : null;

        if (ImpactPredictor.TryPredict(Body, fromCci, alongCci, PredictStepSeconds,
                                       ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit,
                                       TerrainRadiusAt, _path, air))
        {
            // The predictor un-carries its impact by its own flight time, which puts the ground
            // point in the body-fixed frame of the instant the arc *departs*. Mid-burn that instant
            // is the cutoff, seconds away, while the target is known in the frame of now - so
            // comparing them measures the planet's turn over the rest of the burn and calls it miss.
            //
            // It is not a small term and it is not a bias: it shrinks to nothing as cutoff arrives,
            // so the correction chases a ruler moving at ~400 m/s against a target moving at 465.
            // Flown headless at 2,000 km, a shot needing no correction at all was put 191.6 km wrong
            // by nulling it; at 3,459 km, 37.1 km against 0.4 km once both are in one epoch.
            hit = hit with
            {
                GroundFixedPointCci = departsIn > 0.0
                                    ? Body.CarryCci(hit.GroundFixedPointCci, -departsIn)
                                    : hit.GroundFixedPointCci,
            };

            PredictedImpact = hit;

            // Restarted, not aged. Ageing it by the interval since the last prediction freezes the
            // readout the moment predicting stops, which is what left a timer holding at twenty or
            // thirty seconds while the warheads landed. This is run down by the simulated step in
            // Update instead, so it keeps counting for as long as the world does.
            if (!_salvoAway) _arrivalLeft = hit.Seconds;

            // Measured against the *target*, not against the biased aim: the bias is the correction
            // being applied, so scoring it against itself would report a perfect shot however far
            // the rounds actually land from the place the player picked.
            PredictedMissMetres = state.HasAim
                ? Body.SurfaceRadius * Vec.AngleBetween(hit.GroundFixedPointCci, _trueAimCci)
                : double.NaN;

            // Not while the trim is firing, and this is the whole reason the two can coexist. The
            // correction's only observer is this prediction, and the trim is actively moving the
            // vehicle it is taken from - so the bias absorbs a displacement the trim then reads as
            // a larger error and burns harder at. Same shape as the release sequence's latched
            // reference, and it runs away rather than merely drifting: flown, a shot 0.1 km off at
            // cutoff wound up by a factor of ten every ten cycles to 139 m/s of commanded trim.
            // What the correction is being told and what it has done about it, per cycle. A bias
            // that ends at its limit says nothing about how it got there - walked, jumped, or
            // pushed back and forth - and those want different fixes.
            Log.Debug($"aim: bias {AimBiasMetres / 1000.0:F1} km, predicted miss "
                      + $"{PredictedMissMetres / 1000.0:F1} km, from "
                      + $"{(fromCutoff ? "the solved cutoff" : "the live state")}, "
                      + $"kick {Vec.Len(ReleaseImpulseCci()):F2} m/s");

            // During the burn every cycle is a measurement. After it they are rationed: the
            // correction's only observer is this prediction, so one taken between the aim moving
            // and the trim having flown the new arc reads its own unspent correction as error and
            // steps again on top of it.
            // And not from inside the air. Mid-burn the prediction departs from the projected
            // cutoff, which before the vehicle has flown is the pad -- so the arc is flown with drag
            // from sea level and lands thousands of kilometres short of a target nothing is wrong
            // with. AimCorrection.DepartureIsWorthObserving has the flown numbers.
            if (Config.CorrectAim && state.HasAim && !TrimIsFiring
                && AimCorrection.DepartureIsWorthObserving(DensityRatioAt(fromCci))
                && (Program.IsBurning || _measureDue))
            {
                PriceTheAim(state);

                double biasWas = Vec.Len(_aim.BiasCci);

                // How far the state the prediction DEPARTS FROM travelled since the last reading. The impact moves 3,520 m
                // a cycle against an aim move of 78, and 3,520 m over the 0.5 s prediction interval
                // is 7.0 km/s -- the bus's own speed. If the two match, the impact being differenced
                // is carrying the vehicle's motion rather than reporting where it will land, which
                // is the frame-and-epoch fault docs/FRAMES-AND-EPOCHS.md exists for.
                double departureVel = Vec.IsFinite(_lastObservedVelCci)
                                          ? Vec.Len(_lastPredictedFromVelCci - _lastObservedVelCci)
                                          : double.NaN;
                _lastObservedVelCci = _lastPredictedFromVelCci;

                double sinceLast = _sinceObserve;
                _sinceObserve = 0.0;

                _aim.Observe(hit.GroundFixedPointCci, _trueAimCci);

                // The loop's own state, which nothing else reports. A demand that grows pass over
                // pass and a step sized by a response stuck at the clamp's floor are the same
                // reading, and without this they are indistinguishable from a shot that simply
                // wants a large correction.
                Log.Debug($"aim loop on {KsaWorld.DisplayName(Craft)}: "
                          + $"{PredictedMissMetres / 1000.0:F2} km out, best "
                          + $"{_aim.BestMissMetres / 1000.0:F2}, response {_aim.Response:F2}, "
                          + $"bias {biasWas / 1000.0:F1} -> {Vec.Len(_aim.BiasCci) / 1000.0:F1} km, "
                          + $"worse for {_aim.WorseFor}, "
                          + $"{_aim.PlantMeasurements} plant reading(s), raw {_aim.LastRawResponse:F2}"
                          // What the trim had actually delivered when the impact was read. A plant
                          // of 0.14 means the impact followed a seventh of the aim move, and the two
                          // causes want opposite fixes: a trim that converged and an impact that
                          // still moved a seventh is the trajectory, and the step is too small; a
                          // trim that flew a seventh of what it was asked is the actuator, and a
                          // larger step makes it worse.
                          + $" | departure vel {departureVel:F4} m/s over {sinceLast:F2} s"
                          + $" | aim moved {_aim.LastAimMoveMetres:F0} m, impact moved "
                          + $"{_aim.LastImpactMoveMetres:F0} m of which "
                          + $"{_aim.LastImpactAlongAimMetres:F0} along it"
                          + $" | trim owes {_trim.ToGainMetresPerSecond:F2} of "
                          + $"{_trim.SpentMetresPerSecond:F2} spent"
                          + (_trim.GaveUp ? ", GAVE UP" : _trim.Done ? ", done" : ", running"));

                if (!Program.IsBurning)
                {
                    _measureDue = false;
                    _freshMiss = PredictedMissMetres;
                }
            }
        }

        else
        {
            PredictedImpact = null;
            PredictedMissMetres = double.NaN;
        }
    }

    // How far the correction may walk the aim before the trim can no longer fly it there.
    //
    // Only after cutoff. While the engines are lit the actuator is the burn, which re-solves to
    // whatever the aim says and costs propellant rather than accuracy; the trim's budget is the
    // limit only once the burn is the thing that has ended.
    private void PriceTheAim(in IcbmState state)
    {
        if (!Config.AimWithinTrimBudget || Program.IsBurning)
        {
            _aim.AffordableMetres = double.PositiveInfinity;
            return;
        }

        double left = Math.Max(0.0, Config.TrimBudgetMetresPerSecond - _trim.SpentMetresPerSecond);

        // Left standing rather than clamped when the trajectory will not price: a bound of zero is
        // the correction switched off, which is the one outcome nobody asked for.
        if (!AimAuthority.TryMetresFor(state.Body, state.PositionCci, _trueAimCci,
                                       Program.CommittedArrivalFromNow, left, out double reach))
        {
            _aim.AffordableMetres = double.PositiveInfinity;

            if (!_saidAimReach)
            {
                _saidAimReach = true;
                Log.Info($"aim reach on {KsaWorld.DisplayName(Craft)}: the trajectory would not "
                         + "price, so the aim keeps its full range");
            }

            return;
        }

        _aim.AffordableMetres = reach;

        // Said once, and only when it BINDS. A bound wider than AimCorrection.MaxMetres changes
        // nothing, and a setting that cannot be seen to have done anything is one whose flown
        // result means nothing either way -- which is the trap this file's own history keeps
        // falling into. The number is what the budget buys at this trajectory's exchange rate.
        if (!_saidAimReach && reach < AimCorrection.MaxMetres)
        {
            _saidAimReach = true;
            Log.Info($"aim reach on {KsaWorld.DisplayName(Craft)}: {left:F0} m/s of trim buys "
                     + $"{reach / 1000.0:F0} km of aim, against the {AimCorrection.MaxMetres / 1000.0:F0} km "
                     + "the correction may otherwise walk");
        }
    }
}
