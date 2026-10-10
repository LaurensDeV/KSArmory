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
/// control frame that is exactly the held line, so there is no attitude, no turn and no settling; no part tree,
/// so the split is an impulse on a chosen frame and the stack has no size but its radius; the halves pass through
/// each other rather than colliding; one frame of command latency and nothing else of the engine's frame order;
/// no tube offsets, ring or spin, so every warhead leaves the same state and one is flown; no release sequence, so
/// the warheads go on the frame the trim lets them; no terrain; and none of the computer's readouts, its
/// arrival release (<c>ReleaseAnArrivalTheTrimCannotFly</c>) or its multi-target walk. The warhead falls through
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

    public double MaxSecondsAfterCutoff = 3_000.0;

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

        if (!TryPredict(fromCci, arc.RequiredVelocityCci + ReleaseImpulseCci(), out ImpactPredictor.Impact hit)) return;

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
        double3 nose = _command.ThrustDirectionCci;
        return nose.Equals(Vec.Zero) || !Vec.IsFinite(nose) ? Vec.Zero : Vec.Unit(nose) * Warhead.LaunchSpeed;
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
        _busVelocityCci = Flight.VelocityCci;

        Queue<TrimCommand> inFlight = new();
        double t = cutoffSeconds;
        double splitAt = double.NaN;
        double h = 0.0;

        // Each frame is handed the step it follows, as the computer is: the cutoff frame was the burn's last.
        while (t - cutoffSeconds < MaxSecondsAfterCutoff)
        {
            double3 aimNow = Body.CarryCci(aimAtEpoch, t);
            _trueAimCci = aimNow;

            IcbmState state = new(Body, _busPositionCci, _busVelocityCci, _aim.Apply(aimNow), HasAim: true,
                                  Flight.Performance(), DensityAt(_busPositionCci), PropellantAvailable: false,
                                  PlayerStepSeconds: h, AimIsSteady: _aim.IsSteady,
                                  DensityRatioAt: DensityAt, Warhead: Warhead, ReleaseImpulseCci: ReleaseImpulseCci());

            _command = Program.Update(h, state);
            FreezeOrResume();
            Predict(h, state);

            bool atCutoff = Program.ReleasesAtCutoff;
            bool burnOver = Program.Phase == IcbmPhase.Coast && !atCutoff && Program.IsShortShot;
            if ((_command.ReadyToDeploy && !atCutoff) || burnOver) SeparateOnce();

            TrimCommand trim = DriveTrim(h, state);
            if (_didSplit && double.IsNaN(splitAt)) splitAt = t;
            bool held = PostCutoffSequence.TrimHoldsTheRelease(Config.TrimBeforeRelease, _command.ReadyToDeploy,
                                                               _trimAbandoned, atCutoff, _trim.Done,
                                                               _postBoost.Correcting);

            if (Config.AutoRelease && _command.ReadyToDeploy && !held)
            {
                return Release(t, double.IsNaN(splitAt) ? double.NaN : t - splitAt);
            }

            double next = _command.ReadyToDeploy || _didSplit ? StepSeconds : CoastStepSeconds;

            inFlight.Enqueue(trim);
            TrimCommand applied = inFlight.Count > CommandLatencyFrames ? inFlight.Dequeue() : default;

            if (_splitPending)
            {
                _splitPending = false;
                double3 nose = Vec.Unit(_command.ThrustDirectionCci);
                _stackPositionCci = _busPositionCci;
                _stackVelocityCci = _busVelocityCci + nose * SharedErrorAlongNoseMetresPerSecond;
                _busVelocityCci += nose * (SharedErrorAlongNoseMetresPerSecond + ShoveMetresPerSecond);
                _haveStack = true;
            }

            Advance(ref _busPositionCci, ref _busVelocityCci, next);
            if (_haveStack) Advance(ref _stackPositionCci, ref _stackVelocityCci, next);
            if (_haveStack) Knock();

            _busVelocityCci += Push(applied, next);
            t += next;
            h = next;
        }

        return new Outcome(true, false, double.NaN, double.NaN, double.NaN, _postBoost.Cycles, _postBoostSaidWhy,
                           _proximity.Closest, _owedAtSplit, _trim.SpentMetresPerSecond, BiasMetres, _trimAbandoned,
                           _heldOffSeconds, $"no release within {MaxSecondsAfterCutoff:F0} s of cutoff: {_command.Hold}");
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
        return direction * (ThrusterMetresPerSecond2 * burn);
    }

    // The held line, rolled to keep down toward the planet: a point mass has whatever attitude it is asked for.
    private void Frame(out double3 nose, out double3 right, out double3 down)
    {
        nose = Vec.Unit(_command.ThrustDirectionCci);
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
        if (!TryPredict(fromCci, state.VelocityCci + ReleaseImpulseCci(), out ImpactPredictor.Impact hit)) return;

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
            || (Config.StackWaitsOnlyForTheSplit && _postBoost.Cycles > 0))
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

    // IcbmComputer.DriveTrim, less its logging.
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
            Config.TrimBudgetMetresPerSecond, _trim.SpentMetresPerSecond, Config.KeepOutCoversTheClearance);

        if (plan.Abandon)
        {
            _trimAbandoned = true;
            return default;
        }

        _mayTrim = plan.MayTrim;
        _trim.Begin();

        Frame(out double3 nose, out double3 right, out double3 down);

        TrimCommand trim = _trim.Update(step, new TrimSituation(
            state.Body, state.PositionCci, state.VelocityCci,
            Program.ReferencePositionCci, Program.Arc?.RequiredVelocityCci ?? Vec.Zero, Program.SecondsSinceReference,
            nose, right, down, _mayTrim, Config.TrimBudgetMetresPerSecond, _keepOutTowardCci,
            plan.CeilingMetresPerSecond,
            Config.PulseTrim ? Config.PulseSeconds : 0.0,
            Config.StallFallsBackToHolding,
            Config.TrimCountsTheCommandInFlight));

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
            TrimFloorMetres: _trimFloor));

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
