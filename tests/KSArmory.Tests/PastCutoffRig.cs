using Brutal.Numerics;

namespace KSArmory.Tests;

/// <summary>
/// A flight from the pad to where its warhead lands: <see cref="IcbmFlightRig"/> to cutoff, then the split, the
/// clearance, the trim, the post-boost passes, the freeze, the separation kick and the fall, driven through the same
/// <c>Sim/</c> types in the order <c>Ksa/Icbm/IcbmComputer.cs</c> drives them, so a test can assert on the landing.
///
/// <para>It is also the aim correction the computer runs during the burn, as an <see cref="IcbmFlightRig.IAimLoop"/>,
/// because the bias the burn leaves is what the passes after cutoff start from.</para>
///
/// <para>What it does not model, so a result here is about the loop's logic and never about whether KSA will fly
/// it: the bus is a point mass, coasting in vacuum on <see cref="Kepler"/> and pushed by six ideal jets along a
/// control frame that is the modelled nose -- the held line, reached at <see cref="TurnRateDegPerSecond"/> after a
/// commanded turn and otherwise exact, so there is no settling or overshoot; no part tree,
/// so the split is an impulse on a chosen frame and the stack has no size but its radius; the halves pass through
/// each other rather than colliding; one frame of command latency and nothing else of the engine's frame order;
/// no tube offsets, ring or spin, so every warhead leaves the same state and one is flown; no release sequence, so
/// the warheads go on the frame the trim lets them; no terrain; and none of the computer's readouts. Its arrival
/// release (<c>ReleaseAnArrivalTheTrimCannotFly</c>) is flown without the flown prediction it also waits for. A walk is flown either to stops it is given, one warhead
/// each, or to <see cref="PlannedTargets"/>, planned as the computer plans one. The warhead falls through
/// <see cref="ImpactPredictor"/> at a finer step than the probe, not as a <see cref="Slug"/>.</para>
/// </summary>
internal sealed class PastCutoffRig : IcbmFlightRig.IAimLoop
{
    // Ksa/Icbm/IcbmComputer.cs's own cadence and step.
    private const double PredictIntervalSeconds = 0.5;
    private const double PredictStepSeconds = 2.0;

    public required IcbmFlightRig Flight { get; init; }
    public required IcbmProgram Program { get; init; }
    public required MunitionProfile Warhead { get; init; }
    public required Func<double3, double> DensityAt { get; init; }

    private IcbmConfig Config => Program.Config;
    private BallisticBody Body => Flight.Body;

    /// <summary>The frame from cutoff to the gate, where nothing is steered, and the frame once it opens.</summary>
    public double CoastStepSeconds = 1.0;
    public double StepSeconds = 0.05;

    /// <summary>What the decoupler gives the bus, along its nose and away from the stack behind it.</summary>
    public double ShoveMetresPerSecond = 7.0;

    /// <summary>
    /// What both halves are off their solution at the split, along the nose. Positive leaves the stack following
    /// the bus once the trim has nulled the shove: a bus braking nose-retrograde out of orbit owes its solution
    /// back toward the stack behind it. <c>docs/ICBM-OUTSTANDING.md</c> 1.8.
    /// </summary>
    public double SharedErrorAlongNoseMetresPerSecond;

    public double StageRadiusMetres = 5.3;

    /// <summary>
    /// How much of the closing speed the bus comes back with when it reaches the stack's bounding sphere, all of it on
    /// the bus. 0.32 turns a 0.38 m/s closing into the 0.5 m/s knock flown in 1.8.
    /// </summary>
    public double ContactRestitution = 0.32;

    /// <summary>The jets' acceleration, and the frames between a command being written and the bus feeling it.</summary>
    public double ThrusterMetresPerSecond2 = 0.35;
    public int CommandLatencyFrames = 1;

    /// <summary>
    /// The bus's mass fully loaded, which makes the jets push harder by what each warhead takes with it; NaN keeps
    /// <see cref="ThrusterMetresPerSecond2"/> for the whole flight.
    /// </summary>
    public double BusMassKg = double.NaN;
    public double WarheadMassKg = 250.0;
    private int _warheadsGone;

    /// <summary>
    /// What the jets' tank holds at the split, which they then spend and stop on; NaN fires for ever. Needs
    /// <see cref="BusMassKg"/>. A lateral metre a second costs <see cref="LateralCostFactor"/> times an axial one.
    /// </summary>
    public double PropellantKg = double.NaN;
    public double JetExhaustVelocity = 2_906.0;

    /// <summary>
    /// The bus's main engine (N), which flies a hop under <see cref="IcbmConfig.HopsBurnTheEngine"/> along the modelled
    /// nose from the same tank; NaN is a bus with none. Needs <see cref="BusMassKg"/>.
    /// </summary>
    public double EngineThrustNewtons = double.NaN;
    public double EngineExhaustVelocity = 2_996.0;
    private readonly HopBurn _hopBurn = new();
    private double _releasedAt = double.NaN;
    private double _now;
    private bool _engineOn;
    private double _engineSpent;

    private bool EngineHops => Config.HopsBurnTheEngine && HasEngine && Config.HopsSizeTheirOwnSlots;

    private bool HasEngine => double.IsFinite(EngineThrustNewtons) && EngineThrustNewtons > 0.0 && double.IsFinite(BusMassKg);

    private double EngineAcceleration => HasEngine && MassNowKg > 0.0 ? EngineThrustNewtons / MassNowKg : 0.0;
    public double LateralCostFactor = 1.414;
    private double _propellantUsedKg;

    /// <summary>Whether the tank was emptied with the jets still asked to fire.</summary>
    public bool RanDry { get; private set; }

    private bool HasTank => double.IsFinite(PropellantKg) && double.IsFinite(BusMassKg);

    /// <summary>Whether the budget is held under what the tank pays; false lets the jets find the bottom.</summary>
    public bool GuardsTheTank = true;

    /// <summary>What the jets have taken out of the tank (kg).</summary>
    public double PropellantUsedKg => _propellantUsedKg;

    private double MassNowKg
        => (double.IsFinite(BusMassKg) ? BusMassKg : double.NaN) - _warheadsGone * WarheadMassKg - _propellantUsedKg;

    private double JetAcceleration
        => double.IsFinite(BusMassKg) && MassNowKg > 0.0
               ? ThrusterMetresPerSecond2 * BusMassKg / MassNowKg
               : ThrusterMetresPerSecond2;

    // IcbmComputer.TankCeilingMetresPerSecond: read once the bus is alone.
    private double TankCeiling
        => Config.HopsSizeTheirOwnSlots && _didSplit && HasTank && GuardsTheTank
               ? ReleaseLoop.TankCeilingMetresPerSecond(_trim.SpentMetresPerSecond, PropellantKg - _propellantUsedKg,
                                                        MassNowKg, JetExhaustVelocity / LateralCostFactor)
               : double.NaN;

    public double MaxSecondsAfterCutoff = 3_000.0;

    /// <summary>How fast the modelled nose swings onto a new held line; flown, 90 deg took about 3 s.</summary>
    public double TurnRateDegPerSecond = 30.0;

    /// <summary>Where the air first turns an arriving RV, which is what <see cref="IcbmConfig.PointRvsAtEntry"/> points at.</summary>
    public double EntryAltitudeMetres = 130_000.0;

    /// <summary>The modelled nose against the released warhead's airflow at entry, one per release; NaN where unknown.</summary>
    public List<double> NoseFromEntryDeg { get; } = [];

    /// <summary>Whether any release left while a turn was still holding it.</summary>
    public bool ReleasedTurning { get; private set; }

    /// <summary>
    /// What the burn left the bus off its cutoff, along local up: the rig cuts off on the bit, and a flown burn does
    /// not. <c>docs/ICBM-OUTSTANDING.md</c> 1.20.
    /// </summary>
    public double CutoffErrorUpMetresPerSecond;

    /// <summary>The committed arrival at cutoff, in seconds from it; NaN until the burn has cut off.</summary>
    public double CoastSeconds { get; private set; } = double.NaN;

    /// <summary>
    /// Further places to walk to after the lead, one warhead each, in the order flown; empty releases at the lead alone.
    /// The hand-over between them is <c>IcbmComputer.HandOverTo</c>'s.
    /// </summary>
    public List<double3> WalkAimsAtEpoch { get; } = [];

    /// <summary>What each stop of a walk came to, the lead first.</summary>
    internal readonly record struct Stop(double LandedMetres, double DivertMetresPerSecond, double SpentMetresPerSecond,
                                         double BeforeArrivalSeconds = double.NaN, double EngineMetresPerSecond = 0.0);

    public List<Stop> Stops { get; } = [];

    /// <summary>
    /// A set the rig plans its own walk over, as <c>IcbmComputer</c> does: the reach flown every few seconds of
    /// coast, the stops and their order from <see cref="ReachDisplay"/> and <see cref="ReleaseWalker"/>, and each hop
    /// asked of the trim before it is flown. <see cref="Fly"/> is given the lead. Empty flies <see cref="WalkAimsAtEpoch"/>.
    /// </summary>
    public List<(double3 AimAtEpoch, int Warheads)> PlannedTargets { get; } = [];

    public int PlannedLead;

    /// <summary>The plan the walker took, for a test to read; default until one is taken.</summary>
    public ReleaseWalk Plan => _walker.Walk;

    /// <summary>Whether a hop was refused and the walk ended where the bus was.</summary>
    public bool Curtailed => _walker.Curtailed;

    // Ksa/Icbm/IcbmComputer.cs's reach cadence.
    private const double ReachIntervalSeconds = 5.0;

    private readonly ReleaseWalker _walker = new();
    private double _hopCeilingMetresPerSecond = double.NaN;
    private double _hopBurnSeconds;

    /// <summary>Whether a hop's slot and clocks follow the jets under the flag; false keeps the budget alone.</summary>
    public bool SlotsFollowTheJets = true;

    private double HopAcceleration
        => Config.HopsSizeTheirOwnSlots && SlotsFollowTheJets ? ThrusterMetresPerSecond2 : double.NaN;
    private DivertFootprint? _footprint;
    private List<DivertFootprint>? _later;
    private double3 _landingNowCci;
    private double _landingAt = double.NaN;
    private double _sinceReach = double.PositiveInfinity;
    private ReachDisplay? _reach;

    private readonly AimCorrection _aim = new();
    private readonly BusTrim _trim = new();
    private readonly PostBoostAim _postBoost = new();
    private readonly ProximityWatch _proximity = new();

    private bool _resumedForCoast;
    private bool _measureDue;
    private double _freshMiss = double.NaN;
    private double _sincePredict;
    private double _holdingCost = double.NaN;
    private int _holdingCostForPass = -1;
    private double _trimFloor = double.NaN;
    private int _trimFloorForPass = -1;
    private bool _mayTrim;
    private bool _trimAbandoned;
    private bool _releasedTheArrival;
    private bool _postBoostSaid;
    private string _postBoostSaidWhy = "";

    private bool _separated;
    private bool _splitPending;
    private bool _awaitingSplit;
    private bool _didSplit;
    private double _sinceSplit;
    private double3 _keepOutTowardCci;
    private double _owedAtSplit = double.NaN;
    private double _heldOffSeconds;

    private double3 _busPositionCci;
    private double3 _busVelocityCci;
    private double3 _stackPositionCci;
    private double3 _stackVelocityCci;
    private bool _haveStack;

    private IcbmCommand _command;

    // IcbmComputer.Turn.cs's twin: the modelled nose, the turn, and the line it latched.
    private double3 _attitudeCci;
    private readonly BusTurn _turn = new();
    private int _turnLeg;
    private double3 _entryLineCci;
    private bool _reachDueAfterTurn;
    private double3 _trueAimCci;

    /// <summary>What one flight came to.</summary>
    internal readonly record struct Outcome(
        bool CutOff,
        bool Released,
        double LandedMetres,
        double ProbeMissMetres,
        double SecondsFromSplitToRelease,
        int PostBoostPasses,
        string PostBoostEnded,
        ClosestApproach Closest,
        double OwedAtSplitMetresPerSecond,
        double TrimSpentMetresPerSecond,
        double AimBiasMetres,
        bool Abandoned,
        double SecondsHeldOffTheStack,
        string Said);

    public static PastCutoffRig For(IcbmFlightRig flight, IcbmProgram program, MunitionProfile warhead,
                                    Func<double3, double> densityAt)
    {
        PastCutoffRig rig = new() { Flight = flight, Program = program, Warhead = warhead, DensityAt = densityAt };
        flight.AimLoop = rig;
        flight.Warhead = warhead;
        flight.ReleaseLaunchSpeed = warhead.LaunchSpeed;
        return rig;
    }

    /// <summary>Fly to cutoff, then on to the landing.</summary>
    public Outcome Fly(double3 aimAtEpoch, double burnStepSeconds = 0.02, double maxBurnSeconds = 6_000.0)
    {
        IcbmFlightRig.Flight burn = Flight.Fly(Program, aimAtEpoch, burnStepSeconds, maxBurnSeconds);

        if (!burn.Reached)
        {
            return new Outcome(false, false, double.NaN, double.NaN, double.NaN, 0, "", default, double.NaN, 0.0,
                               BiasMetres, false, 0.0, $"never cut off: {burn.FinalPhase} '{burn.Hold}'");
        }

        return Coast(aimAtEpoch, burn.CutoffSeconds);
    }

    public double3 Apply(double3 aimNowCci) => _aim.Apply(aimNowCci);

    public bool IsSteady => _aim.IsSteady;

    private double BiasMetres => Vec.Len(_aim.BiasCci);

    // IcbmComputer.Update's freeze, resume and Predict during the burn, where a reading departs from the solved
    // cutoff and is carried back into the frame of now.
    public void AfterUpdate(IcbmProgram program, in IcbmCommand command, double3 aimNowCci, double stepSeconds)
    {
        _command = command;
        _trueAimCci = aimNowCci;
        FreezeOrResume();

        _sincePredict += stepSeconds;
        if (_sincePredict < PredictIntervalSeconds) return;
        _sincePredict = 0.0;

        if (!program.IsBurning || program.Arc is not { } arc) return;

        double3 fromCci = program.CutoffPositionCci;
        double departsIn = double.IsFinite(command.SecondsToCutoff) ? Math.Max(0.0, command.SecondsToCutoff) : 0.0;

        double3 alongCci = arc.RequiredVelocityCci;
        if (!TryPredict(fromCci, alongCci + PredictedReleaseImpulseCci(fromCci, alongCci), out ImpactPredictor.Impact hit)) return;

        double3 landed = departsIn > 0.0 ? Body.CarryCci(hit.GroundFixedPointCci, -departsIn) : hit.GroundFixedPointCci;

        bool solidsHold = Config.AimWaitsForTheSolids && program.Phase == IcbmPhase.PitchProgram && !program.IsShortShot
                          && Flight.StageIndex < Flight.Stages.Count && Flight.Stages[Flight.StageIndex].Solid
                          && Flight.Stages[Flight.StageIndex].PropellantKg > 0.0;

        if (Config.CorrectAim && AimCorrection.DepartureIsWorthObserving(DensityAt(fromCci)) && !solidsHold)
        {
            _aim.Observe(landed, aimNowCci);
        }
    }

    private void FreezeOrResume()
    {
        if (Program.IsBurning && double.IsFinite(Program.CommittedArrivalFromNow)) _aim.Freeze();

        if (Program.ResumesTheAim && !_resumedForCoast)
        {
            _resumedForCoast = true;
            _aim.Resume();
        }
    }

    private double3 ReleaseImpulseCci()
    {
        double3 nose = _attitudeCci.Equals(Vec.Zero) ? _command.ThrustDirectionCci : _attitudeCci;
        return nose.Equals(Vec.Zero) || !Vec.IsFinite(nose) ? Vec.Zero : Vec.Unit(nose) * Warhead.LaunchSpeed;
    }

    // IcbmComputer.PredictedReleaseImpulseCci: until the turn is done, the kick is thrown along the entry line.
    private double3 PredictedReleaseImpulseCci(double3 releasePositionCci, double3 releaseVelocityCci)
    {
        double3 impulse = ReleaseImpulseCci();
        if (impulse.Equals(Vec.Zero) || !Program.TurnsAtTheSplit || _turnLeg == 3) return impulse;

        double3 line = _entryLineCci;
        if (line.Equals(Vec.Zero) && !TryEntryAirflow(releasePositionCci, releaseVelocityCci, out line)) return impulse;

        return Vec.RotationFromTo(Vec.Unit(impulse), Vec.Unit(line)) * impulse;
    }

    private bool TryEntryAirflow(double3 positionCci, double3 velocityCci, out double3 airflowCci)
        => EntryDirection.TryAt(Body, positionCci, velocityCci, Body.SurfaceRadius + EntryAltitudeMetres,
                                out airflowCci, out _);

    // IcbmComputer.TurnIsDue and DriveTurn, for the entry turn only.
    private void DriveTurn(double step, in IcbmState state, bool released)
    {
        if (!Program.TurnsAtTheSplit || _turnLeg == 3) return;

        if (_turnLeg == 0)
        {
            bool due = _didSplit && !_awaitingSplit && _command.ReadyToDeploy && !released && _postBoost.Cycles == 0
                       && (_trim.Done || _trimAbandoned || !Config.TrimBeforeRelease);
            if (!due) return;

            if (!TryEntryAirflow(state.PositionCci, state.VelocityCci, out double3 airflow))
            {
                _turnLeg = 3;
                return;
            }

            _turnLeg = 1;
            _entryLineCci = Vec.Unit(airflow);
            _turn.Begin(_entryLineCci, Config.TurnCapSeconds);
            Program.CoastLineCci = _entryLineCci;
            _measureDue = false;
            _freshMiss = double.NaN;
            return;
        }

        BusTurn.Stage stage = _turn.Update(step, _attitudeCci);
        if (stage == BusTurn.Stage.Turning) return;
        if (stage == BusTurn.Stage.Capped) Program.CoastLineCci = _turn.ReachedCci;
        _turnLeg = 3;
        _reachDueAfterTurn = PlannedTargets.Count > 0;
    }

    // The nose swings onto the held line at a fixed rate, and holds it exactly once there.
    private void Slew(double seconds)
    {
        double3 want = Vec.Unit(_command.ThrustDirectionCci);
        if (want.Equals(Vec.Zero)) return;
        if (_attitudeCci.Equals(Vec.Zero))
        {
            _attitudeCci = want;
            return;
        }

        double angle = Vec.AngleBetween(_attitudeCci, want);
        double most = double.DegreesToRadians(TurnRateDegPerSecond) * seconds;
        if (angle <= most)
        {
            _attitudeCci = want;
            return;
        }

        double3 axis = Vec.Unit(Vec.Cross(_attitudeCci, want));
        if (axis.Equals(Vec.Zero)) axis = Vec.PerpendicularTo(_attitudeCci, new double3(0, 0, 1));
        _attitudeCci = Vec.Unit(doubleQuat.CreateFromAxisAngle(axis, most) * _attitudeCci);
    }

    private bool TryPredict(double3 fromCci, double3 alongCci, out ImpactPredictor.Impact hit)
        => ImpactPredictor.TryPredict(Body, fromCci, alongCci, PredictStepSeconds, ImpactPredictor.DefaultMaxSeconds,
                                      out hit, null, null, new ImpactPredictor.Drag(DensityAt, Warhead),
                                      stopOnTheSurface: Config.PredictionStopsOnTheSurface,
                                      stopOnTheTerrain: Config.PredictionStopsOnTheTerrain);

    private double GroundMetres(double3 a, double3 b) => Body.SurfaceRadius * Vec.AngleBetween(a, b);

    private Outcome Coast(double3 aimAtEpoch, double cutoffSeconds)
    {
        _busPositionCci = Flight.PositionCci;
        _busVelocityCci = Flight.VelocityCci + Vec.Unit(Flight.PositionCci) * CutoffErrorUpMetresPerSecond;
        CoastSeconds = Program.CommittedArrivalFromNow;

        Queue<TrimCommand> inFlight = new();
        double t = cutoffSeconds;
        int stops = 1 + WalkAimsAtEpoch.Count;
        int stop = 0;
        double spentAtLastStop = 0.0;
        if (stops > 1) Program.ReleaseGateSeconds = ReleaseItinerary.BeforeArrivalSeconds(0, stops, Program.ReleaseGate, ReleaseItinerary.MedianHopSeconds);
        double splitAt = double.NaN;
        double h = 0.0;

        // Each frame is handed the step it follows, as the computer is: the cutoff frame was the burn's last.
        while (t - cutoffSeconds < MaxSecondsAfterCutoff)
        {
            double3 aimNow = Body.CarryCci(aimAtEpoch, t);
            _trueAimCci = aimNow;
            _now = t;

            IcbmState state = new(Body, _busPositionCci, _busVelocityCci, _aim.Apply(aimNow), HasAim: true,
                                  Flight.Performance(), DensityAt(_busPositionCci), PropellantAvailable: false,
                                  PlayerStepSeconds: h, AimIsSteady: _aim.IsSteady,
                                  DensityRatioAt: DensityAt, Warhead: Warhead, ReleaseImpulseCci: ReleaseImpulseCci());

            _command = Program.Update(h, state);
            FreezeOrResume();
            Predict(h, state);

            if (PlannedTargets.Count > 0) PlanTheWalk(h, state, t);

            bool atCutoff = Program.ReleasesAtCutoff;
            bool burnOver = Program.Phase == IcbmPhase.Coast && !atCutoff && Program.IsShortShot;
            if ((_command.ReadyToDeploy && !atCutoff) || burnOver) SeparateOnce();

            DriveTurn(h, state, Stops.Count > 0);
            DriveHop(h, state);
            TrimCommand trim = _hopBurn.Holds ? default : DriveTrim(h, state);
            if (_didSplit && double.IsNaN(splitAt)) splitAt = t;
            bool held = PostCutoffSequence.TrimHoldsTheRelease(Config.TrimBeforeRelease, _command.ReadyToDeploy,
                                                               _trimAbandoned, atCutoff, _trim.Done,
                                                               _postBoost.Correcting)
                        || _turn.Holds || _reachDueAfterTurn || _hopBurn.Holds;

            if (Config.AutoRelease && _command.ReadyToDeploy && !held)
            {
                Outcome released = Release(t, double.IsNaN(splitAt) ? double.NaN : t - splitAt);
                double spent = _trim.SpentMetresPerSecond;
                _releasedAt = t;
                Stops.Add(new Stop(released.LandedMetres, spent - spentAtLastStop, spent, Program.CommittedArrivalFromNow,
                                   _engineSpent));
                _engineSpent = 0.0;
                _warheadsGone += PlannedTargets.Count > 0 && _walker.Walking ? _walker.Step.Warheads : 1;
                spentAtLastStop = spent;

                if (PlannedTargets.Count > 0)
                {
                    if (!_walker.Walking) return released;

                    for (int w = _walker.Step.Warheads; w > 0; w--) _walker.WarheadAway();
                    ReleaseStep step = _walker.Step;

                    if (!step.Handover)
                    {
                        _walker.Finish();
                        return released;
                    }

                    double ceiling = ReleaseLoop.HopCeilingFor(Config.HopsFlyTheirOwnCeiling,
                                                               step.NextHopMetresPerSecond,
                                                               _trim.ToGainMetresPerSecond,
                                                               step.NextPassMetresPerSecond);

                    if (ReleaseLoop.CanFlyTheHop(step.NextHopMetresPerSecond, _trim.ToGainMetresPerSecond,
                                                 double.IsFinite(ceiling)
                                                     ? ceiling
                                                     : PostCutoffSequence.CeilingFor(0, Budget, _trim.SpentMetresPerSecond),
                                                 _trim.SpentMetresPerSecond, Budget, step.NextPassMetresPerSecond)
                        != HopHold.WillFly)
                    {
                        _walker.StopHere();
                        return released;
                    }

                    _walker.Advance();
                    aimAtEpoch = PlannedTargets[step.NextTarget].AimAtEpoch;
                    HandOverToPlanned();
                    _hopCeilingMetresPerSecond = ceiling;
                }
                else
                {
                    if (++stop >= stops) return released;

                    aimAtEpoch = WalkAimsAtEpoch[stop - 1];
                    HandOver(stop, stops);
                }
            }

            double next = _command.ReadyToDeploy || _didSplit ? StepSeconds : CoastStepSeconds;

            inFlight.Enqueue(trim);
            TrimCommand applied = inFlight.Count > CommandLatencyFrames ? inFlight.Dequeue() : default;

            if (_splitPending)
            {
                _splitPending = false;
                double3 nose = _attitudeCci.Equals(Vec.Zero) ? Vec.Unit(_command.ThrustDirectionCci) : _attitudeCci;
                _stackPositionCci = _busPositionCci;
                _stackVelocityCci = _busVelocityCci + nose * SharedErrorAlongNoseMetresPerSecond;
                _busVelocityCci += nose * (SharedErrorAlongNoseMetresPerSecond + ShoveMetresPerSecond);
                _haveStack = true;
            }

            Slew(next);
            Advance(ref _busPositionCci, ref _busVelocityCci, next);
            if (_haveStack) Advance(ref _stackPositionCci, ref _stackVelocityCci, next);
            if (_haveStack) Knock();

            _busVelocityCci += Push(applied, next) + Burn(next);
            t += next;
            h = next;
        }

        return new Outcome(true, false, double.NaN, double.NaN, double.NaN, _postBoost.Cycles, _postBoostSaidWhy,
                           _proximity.Closest, _owedAtSplit, _trim.SpentMetresPerSecond, BiasMetres, _trimAbandoned,
                           _heldOffSeconds, $"no release within {MaxSecondsAfterCutoff:F0} s of cutoff: {_command.Hold}");
    }

    // IcbmComputer.HandOverTo, less the release sequence and the salvo probe, which this rig does not model.
    private void HandOver(int stop, int stops)
    {
        Program.ReleaseGateSeconds = ReleaseItinerary.BeforeArrivalSeconds(stop, stops, Config.ReleaseBeforeArrivalSeconds,
                                                                          ReleaseItinerary.MedianHopSeconds);
        _aim.Retarget(Config.CarryAimBiasAcrossHops);
        Program.CorrectCoastArc();
        _trim.Resume();
        _postBoost.Reset();
        _trimAbandoned = false;
        _postBoostSaid = false;
        _measureDue = false;
        _freshMiss = double.NaN;
        _holdingCost = double.NaN;
        _holdingCostForPass = -1;
        _trimFloor = double.NaN;
        _trimFloorForPass = -1;
    }

    // IcbmComputer.TrimBudgetMetresPerSecond: the split's reserve once a walk's first stop has let go.
    private double Budget
        => ReleaseLoop.CappedByTheTank(
               ReleaseLoop.BudgetWithReserve(Config.TrimBudgetMetresPerSecond,
                                             ReleaseLoop.ReserveReleased(Config.SpendTheSplitReserveAfterTheFirstStop,
                                                                         _walker.FirstStopAway)),
               TankCeiling);

    private int WarheadsAboard
    {
        get
        {
            int n = 0;
            foreach ((double3 _, int w) in PlannedTargets) n += w;
            return n;
        }
    }

    // IcbmComputer.RefreshReach and FlyTheReach, less the overlay: the plan is all the rig reads.
    private void PlanTheWalk(double step, in IcbmState state, double t)
    {
        bool coasting = Program.Phase == IcbmPhase.Coast;
        double coast = _walker.CoastForPlanning(Program.CommittedArrivalFromNow, coasting);

        if (coasting)
        {
            _sinceReach += step;
            if (_sinceReach >= ReachIntervalSeconds || _reachDueAfterTurn)
            {
                _reachDueAfterTurn = false;
                FlyTheReach(state, t);
            }
        }

        List<ReachDisplay.Placed> placed = [];
        foreach ((double3 aim, int warheads) in PlannedTargets)
        {
            double along = 0.0, cross = 0.0;
            bool known = _reach is { } r && double.IsFinite(_landingAt)
                         && r.TryOffsets(Body, Body.CarryCci(aim, t) - Body.CarryCci(_landingNowCci, t - _landingAt),
                                         out along, out cross);
            placed.Add(known ? new ReachDisplay.Placed(along, cross, warheads) : new ReachDisplay.Placed(0.0, 0.0, 0));
        }

        bool tankByStop = Config.PriceTheTankStopByStop && double.IsFinite(TankCeiling);
        ReachDisplay reach = ReachDisplay.For(
            _footprint, placed,
            new ReleaseItinerary.Bus(Config.ReleaseBeforeArrivalSeconds, coast, WarheadsAboard,
                                     FromCutoff: Config.WalkStartsAtCutoff,
                                     ReserveAfterFirstStopMetresPerSecond: Config.SpendTheSplitReserveAfterTheFirstStop
                                                                               ? ReleaseLoop.SplitNullReserveMetresPerSecond
                                                                               : 0.0,
                                     HopsFlyTheirOwnCeiling: Config.HopsFlyTheirOwnCeiling,
                                     TurnSeconds: Program.TurnsAtTheSplit ? Config.TurnAllowanceSeconds : 0.0,
                                     BudgetMetresPerSecond: Config.HopsSizeTheirOwnSlots
                                         ? ReleaseLoop.PlanningBudget(Config.TrimBudgetMetresPerSecond,
                                                                      tankByStop ? double.NaN : TankCeiling,
                                                                      Config.SpendTheSplitReserveAfterTheFirstStop
                                                                          ? ReleaseLoop.SplitNullReserveMetresPerSecond
                                                                          : 0.0)
                                         : PostBoostAim.MaxTrimMetresPerSecond,
                                     HopAccelerationMetresPerSecond2: EngineHops ? EngineAcceleration : HopAcceleration,
                                     FreeArrival: Config.HopsFreeTheirArrival,
                                     TankKg: tankByStop ? PropellantKg - _propellantUsedKg : double.NaN,
                                     MassKg: MassNowKg,
                                     TankExhaustVelocity: EngineHops ? EngineExhaustVelocity : JetExhaustVelocity / LateralCostFactor,
                                     StoreMassKg: WarheadMassKg,
                                     HopTurnSeconds: EngineHops ? HopBurn.TurnsSeconds : 0.0),
            Program.Phase, false, TargetSet.MaxTargets, KSArmory.Warhead.LethalRadius(Warhead.ChargeKg), PlannedLead,
            ReachHold.Unflown, Config.PriceEachHopAtItsSlot, Config.PriceHopsAtWhatTheTrimPays,
            Config.PriceSlotsOnTheGateChord, _later, EngineHops);
        _reach = reach;

        bool mayReplan = ReleaseLoop.MayReplan(Program.TurnsAtTheSplit, _didSplit, reach.Flown.Stops, _walker.Walk.Stops,
                                               Config.WalkShrinksAfterTheSplit);
        if (reach.Flown.Stops > 0 && mayReplan) _walker.Plan(reach.Flown);
        Program.ReleaseGateSeconds = _walker.GateOverrideSeconds(Config, Math.Max(PlannedTargets.Count, 1));
    }

    private void FlyTheReach(in IcbmState state, double t)
    {
        _sinceReach = 0.0;

        double3 positionCci = state.PositionCci;
        double3 velocityCci = state.VelocityCci + ReleaseImpulseCci();
        _later = null;
        if (!AimCorrection.DepartureIsWorthObserving(DensityAt(positionCci)))
        {
            _footprint = null;
            return;
        }

        ReleaseFocus.Air air = new(new ImpactPredictor.Drag(DensityAt, Warhead), PredictStepSeconds, Body.SurfaceRadius,
                                   Config.PredictionStopsOnTheSurface, Config.PredictionStopsOnTheTerrain);
        ReleaseFocus.FlownSensitivity? flown = ReleaseFocus.FlownSensitivity.TryFly(Body, positionCci, velocityCci, air);

        DivertFootprint.ArrivalClock clock = Config.HopsFreeTheirArrival ? DivertFootprint.ArrivalClock.Free
                                                                         : DivertFootprint.ArrivalClock.Pinned;
        if (flown is null || !DivertFootprint.TryFrom(Body, flown, clock, fromTheRealState: true,
                                                       out DivertFootprint footprint))
        {
            _footprint = null;
            return;
        }

        Frame(out double3 nose, out double3 right, out double3 down);
        if (Config.PriceHopsAtWhatTheTrimPays
            && DivertFootprint.TryAxisPrice(Body, flown, nose, right, down, out DivertFootprint.AxisPrice axes, clock))
        {
            footprint = footprint with { Axes = axes };

            if (Config.HopsFreeTheirArrival && Config.FreeHopsMeasureTheirSlots)
            {
                // IcbmComputer.LaterReach.
                List<DivertFootprint> later = [];
                foreach (double before in DivertFootprint.LaterEpochsBeforeArrival(flown.FlightSeconds,
                                                                                   Config.ReleaseBeforeArrivalSeconds))
                {
                    if (!DivertFootprint.TryFreeLater(Body, positionCci, velocityCci, flown.FlightSeconds, before, air,
                                                      nose, right, down, out DivertFootprint atEpoch))
                    {
                        later.Clear();
                        break;
                    }

                    later.Add(atEpoch);
                }

                _later = later.Count > 0 ? later : null;
            }
        }

        _footprint = footprint;
        _landingNowCci = Body.CarryCci(flown.ArrivedCci, -flown.FlightSeconds);
        _landingAt = t;
    }

    // IcbmComputer.DriveHop: the hop on the engine, the arc solved again as it burns and once more when it stops.
    private void DriveHop(double step, in IcbmState state)
    {
        if (_hopBurn.Now is HopBurn.Stage.Idle or HopBurn.Stage.Done)
        {
            _engineOn = false;
            return;
        }

        double3 toGain = Vec.Zero;
        if (Program.Arc is { } arc
            && Kepler.TryCoast(Body.Mu, Program.ReferencePositionCci, arc.RequiredVelocityCci, Program.SecondsSinceReference,
                               out _, out double3 required))
        {
            toGain = required - state.VelocityCci;
        }

        // The rig's warheads are not objects: the last one has gone at its kick since it left.
        double released = double.IsFinite(_releasedAt) ? (_now - _releasedAt) * Warhead.LaunchSpeed : double.NaN;
        HopBurn.Decision d = _hopBurn.Update(step, toGain, _attitudeCci, EngineAcceleration, released);
        _engineOn = d.EngineOn;
        if (!d.LineCci.Equals(Vec.Zero)) Program.CoastLineCci = d.LineCci;
        if (d.ResolveNow) ResolveTheHop();

        if (_hopBurn.Now != HopBurn.Stage.Done) return;

        if (_hopBurn.Burnt) ResolveTheHop();
        _trim.Resume();
        _spentAtHopRound = _trim.SpentMetresPerSecond;
    }

    private void ResolveTheHop()
    {
        if (Config.HopsFreeTheirArrival) Program.FreeTheHopArrival();
        else Program.CorrectCoastArc();
    }

    // The engine along the modelled nose, from the same tank as the nozzles.
    private double3 Burn(double seconds)
    {
        if (!_engineOn || !HasEngine) return Vec.Zero;

        if (HasTank && _propellantUsedKg >= PropellantKg)
        {
            RanDry = true;
            return Vec.Zero;
        }

        double dv = EngineAcceleration * seconds;
        _engineSpent += dv;
        if (HasTank) _propellantUsedKg += EngineThrustNewtons / EngineExhaustVelocity * seconds;

        Frame(out double3 nose, out _, out _);
        return nose * dv;
    }

    // IcbmComputer.HandOverTo, for a walk the rig planned: the walker sets the gate.
    private void HandOverToPlanned()
    {
        _hopBurnSeconds = ReleaseLoop.HopBurnSeconds(_walker.Step.HopMetresPerSecond, HopAcceleration);
        _hopRound = Config.HopsResolveAfterTheirBurn ? 0 : -1;
        _spentAtHopRound = _trim.SpentMetresPerSecond;
        _aim.Retarget(Config.CarryAimBiasAcrossHops);
        if (Config.HopsFreeTheirArrival) Program.FreeTheHopArrival();
        else Program.CorrectCoastArc();
        if (Config.HopsBurnTheEngine && HasEngine)
        {
            _hopBurn.Begin(!Program.CoastLineCci.Equals(Vec.Zero) ? Program.CoastLineCci : _entryLineCci);
        }
        _trim.Resume();
        _postBoost.Reset();
        _trimAbandoned = false;
        _postBoostSaid = false;
        _measureDue = false;
        _freshMiss = double.NaN;
        _holdingCost = double.NaN;
        _holdingCostForPass = -1;
        _trimFloor = double.NaN;
        _trimFloorForPass = -1;
        Program.ReleaseGateSeconds = _walker.GateOverrideSeconds(Config, Math.Max(PlannedTargets.Count, 1));
    }

    private void Knock()
    {
        double3 fromStack = _busPositionCci - _stackPositionCci;
        double apart = Vec.Len(fromStack);
        if (!(apart < StageRadiusMetres) || apart <= 0.0) return;

        double3 normal = fromStack / apart;
        double closing = -Vec.Dot(_busVelocityCci - _stackVelocityCci, normal);
        if (closing <= 0.0) return;

        _busVelocityCci += normal * ((1.0 + ContactRestitution) * closing);
        Contacts++;
    }

    /// <summary>How many times the bus reached the stack's bounding sphere.</summary>
    public int Contacts { get; private set; }

    private void Advance(ref double3 positionCci, ref double3 velocityCci, double seconds)
    {
        if (Kepler.TryCoast(Body.Mu, positionCci, velocityCci, seconds, out double3 p, out double3 v))
        {
            positionCci = p;
            velocityCci = v;
        }
    }

    private double3 Push(in TrimCommand command, double seconds)
    {
        if (command.Fire == TrimAxes.None) return Vec.Zero;

        Frame(out double3 nose, out double3 right, out double3 down);
        double3 direction = Vec.Zero;
        if (command.Fire.HasFlag(TrimAxes.Forward)) direction += nose;
        if (command.Fire.HasFlag(TrimAxes.Backward)) direction -= nose;
        if (command.Fire.HasFlag(TrimAxes.Right)) direction += right;
        if (command.Fire.HasFlag(TrimAxes.Left)) direction -= right;
        if (command.Fire.HasFlag(TrimAxes.Down)) direction += down;
        if (command.Fire.HasFlag(TrimAxes.Up)) direction -= down;

        double burn = command.Pulse && Config.PulseTrim ? Config.PulseSeconds : seconds;
        double dv = JetAcceleration * burn;

        if (HasTank)
        {
            if (_propellantUsedKg >= PropellantKg)
            {
                RanDry = true;
                return Vec.Zero;
            }

            int axial = (command.Fire.HasFlag(TrimAxes.Forward) ? 1 : 0) + (command.Fire.HasFlag(TrimAxes.Backward) ? 1 : 0);
            int lateral = (command.Fire.HasFlag(TrimAxes.Right) ? 1 : 0) + (command.Fire.HasFlag(TrimAxes.Left) ? 1 : 0)
                          + (command.Fire.HasFlag(TrimAxes.Down) ? 1 : 0) + (command.Fire.HasFlag(TrimAxes.Up) ? 1 : 0);
            _propellantUsedKg += MassNowKg * dv * (axial + lateral * LateralCostFactor) / JetExhaustVelocity;
        }

        return direction * dv;
    }

    // The modelled nose, rolled to keep down toward the planet.
    private void Frame(out double3 nose, out double3 right, out double3 down)
    {
        nose = _attitudeCci.Equals(Vec.Zero) ? Vec.Unit(_command.ThrustDirectionCci) : _attitudeCci;
        double3 toward = -Vec.Unit(_busPositionCci);
        down = Vec.Unit(Vec.RejectFrom(toward, nose));
        if (down.Equals(Vec.Zero)) down = Vec.PerpendicularTo(nose, new double3(0, 0, 1));
        right = Vec.Cross(down, nose);
    }

    private void SeparateOnce()
    {
        if (_separated) return;
        _separated = true;
        _splitPending = true;
        _awaitingSplit = true;
        _didSplit = true;
    }

    // IcbmComputer.Predict after cutoff: from the bus as it is, and read only when a pass asked for it and the
    // correction it made has been flown.
    private void Predict(double step, in IcbmState state)
    {
        _sincePredict += step;
        if (_sincePredict < PredictIntervalSeconds) return;
        _sincePredict = 0.0;

        if (!_measureDue) return;

        double3 fromCci = state.PositionCci;
        if (!TryPredict(fromCci, state.VelocityCci + PredictedReleaseImpulseCci(fromCci, state.VelocityCci),
                        out ImpactPredictor.Impact hit))
        {
            return;
        }

        bool trimIsFiring = _trim.Armed && !_trim.Done && _mayTrim;

        if (Config.CorrectAim && !trimIsFiring && AimCorrection.DepartureIsWorthObserving(DensityAt(fromCci))
            && _trim.Done)
        {
            _aim.Observe(hit.GroundFixedPointCci, _trueAimCci);
            _measureDue = false;
            _freshMiss = GroundMetres(hit.GroundFixedPointCci, _trueAimCci);
        }
    }

    // IcbmComputer.Clear, against the stack the rig left at the split.
    private Clearance Clear(double step)
    {
        _sinceSplit += step;

        double3 between = _stackPositionCci - _busPositionCci;
        double apart = _haveStack ? Vec.Len(between) : double.NaN;

        _proximity.Update(step, apart, StageRadiusMetres);

        _keepOutTowardCci = double.IsFinite(apart) && apart < ProximityWatch.KeepOutFor(StageRadiusMetres)
                            && !between.Equals(Vec.Zero)
                                ? Vec.Unit(between)
                                : Vec.Zero;

        return SeparationClearance.Check(apart, StageRadiusMetres, _sinceSplit, ForTheTrimMetres());
    }

    private double ForTheTrimMetres()
    {
        if (!Config.TrimWaitsOutTheStack || !_haveStack || !Vec.IsFinite(_trim.ToGainCci)
            || (Config.StackWaitsOnlyForTheSplit && (_postBoost.Cycles > 0 || _warheadsGone > 0)))
        {
            return 0.0;
        }

        double toArrival = Program.CommittedArrivalFromNow;
        double secondsToRelease = _command.ShortfallMetresPerSecond > 0.0 || !double.IsFinite(toArrival)
                                      ? double.NaN
                                      : toArrival - Program.ReleaseGate;
        double toRelease = Math.Max(_postBoost.SecondsLeft, double.IsFinite(secondsToRelease) ? secondsToRelease : 0.0);

        return SeparationClearance.ForTheTrimMetres(ProximityWatch.KeepOutFor(StageRadiusMetres),
                                                    _busPositionCci - _stackPositionCci,
                                                    _busVelocityCci - _stackVelocityCci, _trim.ToGainCci, toRelease);
    }

    private int _hopRound = -1;
    private double _spentAtHopRound;

    // IcbmComputer.ResolvedTheHopAgain, less its line.
    private bool ResolvedTheHopAgain()
    {
        if (_hopRound < 0 || !_trim.Done || _postBoost.Cycles > 0) return false;

        double spent = _trim.SpentMetresPerSecond - _spentAtHopRound;
        if (!ReleaseLoop.ResolvesTheHopAgain(_hopRound, spent))
        {
            _hopRound = -1;
            return false;
        }

        _hopRound++;
        _spentAtHopRound = _trim.SpentMetresPerSecond;
        if (Config.HopsFreeTheirArrival) Program.FreeTheHopArrival();
        else Program.CorrectCoastArc();
        _trim.Resume();
        _measureDue = false;
        _freshMiss = double.NaN;
        return true;
    }

    // IcbmComputer.DriveTrim, less its logging.
    // IcbmComputer.ReleaseAnArrivalTheTrimCannotFly, less its wait for a flown prediction.
    private void ReleaseAnArrivalTheTrimCannotFly(in TrimCommand trim, in IcbmState state)
    {
        if (_releasedTheArrival || !_trim.GaveUp || _trim.SpentMetresPerSecond > 0.0) return;
        if (!(trim.ToGainMetresPerSecond > BusTrim.MaxMetresPerSecond)) return;
        if (!double.IsFinite(Program.CommittedArrivalFromNow)) return;

        _releasedTheArrival = true;

        if (!Config.RefusedTrimTakesTheCheapestArrival)
        {
            if (Program.ReleaseArrival()) _trim.Begin();
            return;
        }

        if (Program.TryCommitTheCheapestArrival(state)) _trim.Resume();
    }

    private TrimCommand DriveTrim(double step, in IcbmState state)
    {
        if (!Config.TrimBeforeRelease || !_command.ReadyToDeploy || Program.ReleasesAtCutoff) return default;

        if (_awaitingSplit)
        {
            if (_splitPending) return default;
            _awaitingSplit = false;
        }

        Clearance clearance = _didSplit ? Clear(step) : new Clearance(true, false, "");

        PostCutoffSequence.Plan plan = PostCutoffSequence.Decide(
            clearance.IsClear, clearance.Abandoned, _postBoost.Cycles,
            Budget, _trim.SpentMetresPerSecond, Config.KeepOutCoversTheClearance, _hopCeilingMetresPerSecond);

        if (plan.Abandon)
        {
            _trimAbandoned = true;
            return default;
        }

        _mayTrim = plan.MayTrim && !_turn.Holds;
        _trim.Begin();

        Frame(out double3 nose, out double3 right, out double3 down);

        TrimCommand trim = _trim.Update(step, new TrimSituation(
            state.Body, state.PositionCci, state.VelocityCci,
            Program.ReferencePositionCci, Program.Arc?.RequiredVelocityCci ?? Vec.Zero, Program.SecondsSinceReference,
            nose, right, down, _mayTrim, Budget, _keepOutTowardCci,
            plan.CeilingMetresPerSecond,
            Config.PulseTrim ? Config.PulseSeconds : 0.0,
            Config.StallFallsBackToHolding,
            Config.TrimCountsTheCommandInFlight,
            MaxSeconds: Config.HopsSizeTheirOwnSlots ? BusTrim.MaxSeconds + _hopBurnSeconds : double.NaN));

        ReleaseAnArrivalTheTrimCannotFly(trim, state);

        if (_turn.Holds) return trim;
        if (ResolvedTheHopAgain()) return trim;

        int passesBefore = _postBoost.Cycles;

        MeasureHoldingCost(state);
        MeasureTrimFloor(step, state);

        PostBoostAim.Decision pass = _postBoost.Update(step, new PostBoostSituation(
            TrimSettled: _trim.Done,
            ReleaseDirectionCci: ReleaseImpulseCci(),
            PredictedMissMetres: _freshMiss,
            AimHasSettled: _aim.Settled,
            TrimGaveUp: _trim.GaveUp,
            TrimSpentMetresPerSecond: _trim.SpentMetresPerSecond,
            HoldingCostMetresPerSecond: double.IsFinite(_holdingCost) ? _holdingCost : Config.HoldingCostMetresPerSecond,
            DecideOnTheReading: Config.DecideOnTheReading,
            ReleaseInsideTheTrimFloor: Config.ReleaseInsideTheTrimFloor,
            TrimFloorMetres: _trimFloor,
            BudgetMetresPerSecond: Config.HopsSizeTheirOwnSlots ? ReleaseLoop.PlanningBudget(Budget)
                                                                : PostBoostAim.MaxTrimMetresPerSecond,
            MaxSeconds: Config.HopsSizeTheirOwnSlots ? PostBoostAim.MaxSeconds + _hopBurnSeconds : double.NaN));

        if (pass.MayMeasure) _measureDue = true;

        if (_postBoost.Cycles > passesBefore)
        {
            _freshMiss = double.NaN;
            Program.CorrectCoastArc();
            _trim.Resume();
        }

        if (pass.MayRelease && !_postBoostSaid)
        {
            _postBoostSaid = true;
            _postBoostSaidWhy = pass.Said;
            _aim.Freeze();
        }

        if (_mayTrim && !trim.Done && trim.Fire == TrimAxes.None && !_keepOutTowardCci.Equals(Vec.Zero))
        {
            _heldOffSeconds += step;
        }

        if (!double.IsFinite(_owedAtSplit) && double.IsFinite(trim.ToGainMetresPerSecond))
        {
            _owedAtSplit = trim.ToGainMetresPerSecond;
        }

        return trim;
    }

    private void MeasureHoldingCost(in IcbmState state)
    {
        if (!Config.DeriveHoldingCost) { _holdingCost = double.NaN; return; }
        if (_postBoost.Cycles == _holdingCostForPass) return;
        _holdingCostForPass = _postBoost.Cycles;

        if (HoldingCost.TryMeasure(Body, state.PositionCci, state.VelocityCci, ReleaseImpulseCci(),
                                   PredictStepSeconds, out double measured,
                                   new ImpactPredictor.Drag(DensityAt, Warhead),
                                   stopOnTheSurface: Config.PredictionStopsOnTheSurface,
                                   stopOnTheTerrain: Config.PredictionStopsOnTheTerrain))
        {
            _holdingCost = measured;
        }
    }

    private void MeasureTrimFloor(double step, in IcbmState state)
    {
        if (!Config.ReleaseInsideTheTrimFloor) { _trimFloor = double.NaN; return; }
        if (_postBoost.Cycles == _trimFloorForPass) return;
        _trimFloorForPass = _postBoost.Cycles;

        double band = BusTrim.StopBand(_trim.Acceleration, step, Config.PulseTrim ? Config.PulseSeconds : 0.0);

        _trimFloor = AimAuthority.TryRate(state.Body, state.PositionCci, _trueAimCci,
                                          Program.CommittedArrivalFromNow, out double perMetre)
                     && perMetre > 0.0
                         ? band / perMetre
                         : double.NaN;
    }

    // IcbmComputer.Release with its own probe and KickAtSeparation, for a warhead leaving from the bus's own
    // state with no tube offset and no spin. The fall is flown finer than the probe so the two are not one call.
    private Outcome Release(double t, double secondsFromSplit)
    {
        double3 fromCci = _busPositionCci;
        double3 alongCci = _busVelocityCci + ReleaseImpulseCci();
        ImpactPredictor.Drag drag = new(DensityAt, Warhead);

        ReleasedTurning |= _turn.Holds;
        NoseFromEntryDeg.Add(TryEntryAirflow(fromCci, alongCci, out double3 entryAirflow) && !_attitudeCci.Equals(Vec.Zero)
                                 ? double.RadiansToDegrees(Vec.AngleBetween(_attitudeCci, entryAirflow))
                                 : double.NaN);

        double probeMiss = double.NaN;
        double3 kickCci = Vec.Zero;

        if (TryPredict(fromCci, alongCci, out ImpactPredictor.Impact probe))
        {
            probeMiss = GroundMetres(probe.GroundFixedPointCci, _trueAimCci);

            ReleaseFocus.ProbeMiss? miss = Config.CancelProbeMissAtSeparation
                ? new ReleaseFocus.ProbeMiss(probe.GroundFixedPointCci, _trueAimCci)
                : null;

            ReleaseFocus.FlownSensitivity? throughTheAir = Config.KickThroughTheAir && miss is not null
                ? ReleaseFocus.FlownSensitivity.TryFly(Body, fromCci, alongCci,
                                                       new ReleaseFocus.Air(drag, PredictStepSeconds,
                                                                            Vec.Len(probe.PointCci),
                                                                            Config.PredictionStopsOnTheSurface,
                                                                            Config.PredictionStopsOnTheTerrain))
                : null;

            ReleaseFocus.Separation kick = ReleaseFocus.Kick(Body, fromCci, alongCci, probe.Seconds, Vec.Zero,
                                                             Vec.Zero, focusRing: false,
                                                             Config.CancelSpinAtSeparation, miss, throughTheAir,
                                                             MissKickCap);
            kickCci = kick.KickCci;
        }

        double landed = ImpactPredictor.TryPredict(Body, fromCci, alongCci + kickCci, 0.5,
                                                   ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit,
                                                   null, null, drag, atmosphericStepSeconds: 0.02,
                                                   stopOnTheSurface: true)
                            ? GroundMetres(hit.GroundFixedPointCci, _trueAimCci)
                            : double.NaN;

        return new Outcome(true, true, landed, probeMiss, secondsFromSplit, _postBoost.Cycles, _postBoostSaidWhy,
                           _proximity.Closest, _owedAtSplit, _trim.SpentMetresPerSecond, BiasMetres, _trimAbandoned,
                           _heldOffSeconds, _trim.Said);
    }

    private double MissKickCap
        => Config.ShortShotMissKickMetresPerSecond > 0.0
           && (Program.ReleasesAtCutoff || (Config.ShortShotKickCapAfterTheTrim && Program.IsShortShot))
               ? Config.ShortShotMissKickMetresPerSecond
               : Config.LongShotMissKickMetresPerSecond > 0.0 && !Program.IsShortShot
                   ? Config.LongShotMissKickMetresPerSecond
                   : ReleaseFocus.MaxMissKickMetresPerSecond;
}
