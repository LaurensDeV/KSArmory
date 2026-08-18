using Brutal.Numerics;

namespace KSArmory;

/// <summary>Where a flight has got to. The phases run in order and never run backwards.</summary>
internal enum IcbmPhase
{
    Idle,
    Rising,
    PitchProgram,
    ClosedLoop,
    Coast,
    NoSolution,
}

/// <summary>
/// Everything the program needs to know about the world this cycle, sampled by whoever owns the
/// game.
/// </summary>
internal readonly record struct IcbmState(
    BallisticBody Body,
    double3 PositionCci,
    double3 VelocityCci,
    double3 AimNowCci,
    bool HasAim,
    BoosterPerformance Booster,
    double AirDensityRatio,
    bool PropellantAvailable,
    double ThrottleAchieved = 1.0)
{
    public double Altitude => Body.AltitudeOf(PositionCci);

    /// <summary>Motion relative to the turning ground, which is what the air is doing.</summary>
    public double3 AirflowCci => VelocityCci - Body.GroundVelocityCci(PositionCci);

    public double3 UpCci => Vec.Unit(PositionCci);

    public double DynamicPressurePa
        => AscentProfile.DynamicPressure(AirDensityRatio, Vec.Len(AirflowCci));
}

/// <summary>What to do about it, this instant.</summary>
internal readonly record struct IcbmCommand(
    IcbmPhase Phase,
    double3 ThrustDirectionCci,
    double Throttle,
    bool EngineOn,
    bool RequestStage,
    double VelocityToGain,
    double SecondsToCutoff,
    bool ReadyToDeploy,
    string Hold);

/// <summary>
/// The flight, from the pad to warhead release: a schedule while there is air, closed-loop
/// guidance once there is not, and a cutoff that ends the powered flight where the fall begins.
///
/// <para>It flies a rocket it has never seen. Nothing here knows how many stages the stack has,
/// what its engines are or what it weighs — <see cref="BoosterPerformance"/> is re-read every
/// cycle, so staging is not an event to be handled but a change in four numbers, and a vehicle
/// assembled by somebody else is not a special case.</para>
///
/// <para><b>Stepped every frame; solved a few times a second.</b> The two rates are different on
/// purpose. Re-solving the trajectory is hundreds of transfer solutions and does not need doing at
/// frame rate — but the <em>cutoff</em> does, because it is the one instant in the flight where
/// being a tenth of a second late costs kilometres at the far end. So the solve sets a countdown
/// and the frame runs it down.</para>
///
/// <para><b>Why the phases cannot run backwards.</b> Every gate here is on a quantity that is noisy
/// at exactly the moment it is being tested — dynamic pressure at handover, velocity still to gain
/// at cutoff. A machine that can fall back a phase will, repeatedly, and each round trip relights
/// an engine that had finished. So the transitions are one-way and a shot that goes wrong is
/// abandoned rather than retried, which is also the honest thing to show the player.</para>
/// </summary>
internal sealed class IcbmProgram
{
    /// <summary>How often the trajectory is re-solved. Everything between is the countdown.</summary>
    public const double SolveIntervalSeconds = 0.25;

    /// <summary>Inside this much of cutoff, solve every step. It is thirty frames and it decides the shot.</summary>
    public const double SolveEveryStepWithin = 0.75;

    /// <summary>Long enough for the stack to settle before the next stage is considered.</summary>
    public const double StageCooldownSeconds = 1.5;

    // Every shot goes the direct way round. BallisticArc can fly the arc over the far side, and it
    // is not offered: that arc is a near-complete orbit, so it costs orbital-grade delta-v rather
    // than ballistic, and a switch for it would silently turn every shot into one that falls short.
    // The solver keeps the second family because a solver told there is only one fails at the
    // boundary between them.
    private const bool LongWay = false;

    /// <summary>Propellant unavailable for this long, with a stage already asked for, ends the burn.</summary>
    public const double DrySecondsBeforeGivingUp = 4.0;

    /// <summary>
    /// How much full-throttle burn is left when the throttle starts coming back.
    ///
    /// <para>An engine can only be shut down on a frame boundary, so the velocity error left at
    /// cutoff is whatever the last frame added — a couple of metres a second on a light upper
    /// stage, which is a kilometre and more at the far end of the arc. Coming back to a fraction of
    /// thrust for the last moment divides that error by the same fraction, and costs a fraction of
    /// a second of burn.</para>
    ///
    /// <para>Nothing depends on the vehicle honouring it. A stack whose motors cannot be throttled
    /// at all simply gets the error it would have had, because the cutoff test is written against
    /// the throttle that was <em>commanded</em>: an ignored command makes the threshold
    /// conservative rather than wrong.</para>
    /// </summary>
    public const double ThrottleDownSeconds = 2.0;

    /// <summary>The least thrust worth commanding. Below this, engines misbehave and so does the maths.</summary>
    public const double MinCommandedThrottle = 0.03;

    private double _cutoffSeed;
    private double _flightSeed = double.NaN;
    private double _sinceSolve = double.PositiveInfinity;
    private double _countdown = double.PositiveInfinity;
    private double _toGain;
    private double3 _thrustDirCci;
    private double _stageCooldown;
    private double _drySeconds;
    private double _lastStep;
    private double _throttle = 1.0;
    private double _lowestToGain = double.PositiveInfinity;
    private bool _fellShort;
    private double _arrivalFromLaunch = double.NaN;
    private double _sinceLaunch;
    private string _reachHold = "";

    public IcbmConfig Config { get; }

    public IcbmPhase Phase { get; private set; } = IcbmPhase.Idle;

    /// <summary>The arc the last solve was flying to. Null until guidance has found one.</summary>
    public BallisticArc.Solution? Arc { get; private set; }

    /// <summary>Which way downrange is, refreshed while the pitch programme runs.</summary>
    public double3 DownrangeCci { get; private set; }

    public double SecondsSinceLaunch => _sinceLaunch;

    /// <summary>Velocity still to gain at the last solve. Zero once the burn is over.</summary>
    public double VelocityToGain => _toGain;

    public IcbmProgram(IcbmConfig config) => Config = config;

    /// <summary>Back to the pad. The one way a flight can be un-flown.</summary>
    public void Reset()
    {
        Phase = IcbmPhase.Idle;
        Arc = null;
        DownrangeCci = Vec.Zero;
        _cutoffSeed = 0.0;
        _flightSeed = double.NaN;
        _sinceSolve = double.PositiveInfinity;
        _countdown = double.PositiveInfinity;
        _toGain = 0.0;
        _thrustDirCci = Vec.Zero;
        _stageCooldown = 0.0;
        _drySeconds = 0.0;
        _lastStep = 0.0;
        _throttle = 1.0;
        _lowestToGain = double.PositiveInfinity;
        _fellShort = false;
        _arrivalFromLaunch = double.NaN;
        _sinceLaunch = 0.0;
        _reachHold = "";
    }

    public IcbmCommand Update(double stepSeconds, in IcbmState state)
    {
        double step = double.IsFinite(stepSeconds) && stepSeconds > 0.0 ? stepSeconds : 0.0;
        if (step > 0.0) _lastStep = step;

        _stageCooldown = Math.Max(0.0, _stageCooldown - step);
        _sinceSolve += step;
        if (Phase is not (IcbmPhase.Idle or IcbmPhase.NoSolution)) _sinceLaunch += step;

        if (!Config.Armed) return Idle(state, "not armed");
        if (!state.HasAim) return Idle(state, "no target designated");
        if (!state.Body.IsUsable) return Idle(state, "no parent body");

        if (Phase == IcbmPhase.Coast) return Coasting(state);

        Resolve(state, step);

        if (Phase is IcbmPhase.Idle or IcbmPhase.NoSolution)
        {
            if (Arc is null)
            {
                Phase = IcbmPhase.NoSolution;
                return Idle(state, _reachHold);
            }
            return Liftoff(state);
        }

        // No thrust counts as dry, and is not treated as an immediate failure. On the pad it is
        // simply the state before ignition, and an engine takes a moment to come up — so a burn is
        // only abandoned once nothing has been available for long enough that a stage would have
        // arrived by now.
        bool dry = !state.PropellantAvailable || !state.Booster.CanThrust;
        if (step > 0.0) _drySeconds = dry ? _drySeconds + step : 0.0;

        if (_drySeconds > DrySecondsBeforeGivingUp)
        {
            _fellShort = _toGain > BurnoutGuidance.CutoffMetresPerSecond;
            Phase = IcbmPhase.Coast;
            return Coasting(state);
        }

        return Phase switch
        {
            IcbmPhase.Rising => Rising(state),
            IcbmPhase.PitchProgram => PitchProgram(state),
            IcbmPhase.ClosedLoop => ClosedLoop(state),
            _ => Coasting(state),
        };
    }

    private void Resolve(in IcbmState state, double step)
    {
        bool burning = Phase is IcbmPhase.Rising or IcbmPhase.PitchProgram or IcbmPhase.ClosedLoop;
        if (burning && step > 0.0) _countdown -= step * Math.Clamp(state.ThrottleAchieved, 0.0, 1.0);

        bool due = _sinceSolve >= SolveIntervalSeconds
                || _countdown <= SolveEveryStepWithin
                || Arc is null;

        if (!due) return;

        _sinceSolve = 0.0;

        double arrivalFromNow = double.IsFinite(_arrivalFromLaunch)
                              ? _arrivalFromLaunch - _sinceLaunch
                              : double.NaN;

        if (!BurnoutGuidance.TrySteer(state.Body, state.PositionCci, state.VelocityCci, state.AimNowCci,
                                      state.Booster, out BurnoutGuidance.Command command,
                                      Config.Loft, LongWay, _cutoffSeed, _flightSeed,
                                      arrivalFromNow))
        {
            // A flight already under way keeps flying its schedule: the geometry a solve needs can
            // be momentarily out of reach on the way up, and abandoning a shot for it would throw
            // away a launch that is going perfectly well.
            if (Arc is null) _reachHold = WhyNot(state);
            return;
        }

        Arc = command.Arc;
        _cutoffSeed = command.SecondsToCutoff;
        _flightSeed = command.Arc.CheapestFlightSeconds;
        _countdown = command.SecondsToCutoff;
        _toGain = command.VelocityToGain;
        _lowestToGain = Math.Min(_lowestToGain, _toGain);
        _thrustDirCci = command.ThrustDirectionCci;

        if (Phase is IcbmPhase.Idle or IcbmPhase.NoSolution or IcbmPhase.PitchProgram)
        {
            DownrangeCci = AscentProfile.Downrange(state.UpCci, command.Arc.RequiredVelocityCci,
                                                   state.Body.GroundVelocityCci(state.PositionCci));
        }

        // The moment closed-loop guidance takes the vehicle, the arrival is nailed down. Before
        // that the cheapest shot is the right thing to follow, because the state is changing far too
        // much for any arrival time chosen on the pad to still be the cheapest one.
        if (Phase == IcbmPhase.ClosedLoop && !double.IsFinite(_arrivalFromLaunch))
        {
            _arrivalFromLaunch = _sinceLaunch + command.SecondsToCutoff + command.Arc.FlightSeconds;
        }
    }

    // Two different failures read identically from outside, and only one of them is the player's
    // to fix. A target beyond any trajectory needs a different target; a target this stack cannot
    // reach needs a bigger rocket.
    private string WhyNot(in IcbmState state)
    {
        if (!BallisticArc.TryCheapest(state.Body, state.PositionCci, state.VelocityCci, state.AimNowCci,
                                      out BallisticArc.Solution reachable, Config.Loft, LongWay))
        {
            return "no trajectory reaches that target";
        }

        if (!state.Booster.CanThrust) return "no engine running";

        double needed = Vec.Len(reachable.VelocityToGain(state.VelocityCci));
        double have = state.Booster.DeltaVRemaining;
        return $"not enough in the tanks: needs {needed / 1000.0:F1} km/s, has {have / 1000.0:F1} km/s";
    }

    private IcbmCommand Liftoff(in IcbmState state)
    {
        Phase = IcbmPhase.Rising;
        _sinceLaunch = 0.0;
        return Fly(IcbmPhase.Rising, state.UpCci, state, "lifting off");
    }

    private IcbmCommand Rising(in IcbmState state)
    {
        bool clear = state.Altitude >= AscentProfile.VerticalRiseMetres
                  || Vec.Len(state.AirflowCci) >= AscentProfile.VerticalRiseSpeed;

        if (clear) Phase = IcbmPhase.PitchProgram;

        return Fly(Phase, state.UpCci, state, "vertical rise");
    }

    private IcbmCommand PitchProgram(in IcbmState state)
    {
        if (state.DynamicPressurePa <= Config.HandoverPressurePa
            && state.Altitude >= Config.TurnStartMetres)
        {
            Phase = IcbmPhase.ClosedLoop;
            return ClosedLoop(state);
        }

        double pitch = AscentProfile.PitchDegreesAt(state.Altitude, Config.TurnStartMetres, Config.TurnEndMetres);
        double3 wanted = AscentProfile.Aim(state.UpCci, DownrangeCci, pitch);

        return Fly(IcbmPhase.PitchProgram, Limit(wanted, state), state, $"pitch programme, {pitch:F0} deg");
    }

    private IcbmCommand ClosedLoop(in IcbmState state)
    {
        if (ShouldCutOff(state))
        {
            Phase = IcbmPhase.Coast;
            return Coasting(state);
        }

        _throttle = ThrottleDownSeconds > 0.0 && _countdown < ThrottleDownSeconds
                  ? Math.Clamp(_countdown / ThrottleDownSeconds, MinCommandedThrottle, 1.0)
                  : 1.0;

        double3 wanted = _thrustDirCci.Equals(Vec.Zero) ? Vec.Unit(state.VelocityCci) : _thrustDirCci;

        return Fly(IcbmPhase.ClosedLoop, Limit(wanted, state), state, "guiding to cutoff");
    }

    // Cutting off is a timing problem, not a threshold one. An engine can only be shut down on a
    // frame boundary, and a light upper stage at ten gravities changes its velocity by more in one
    // frame than any sensible tolerance allows - so waiting for the velocity still to gain to fall
    // below a fixed number waits for something that cannot happen. It overshoots instead, turns
    // round to brake, overshoots the other way, and burns the stage dry hunting.
    //
    // So: stop when less than half a frame of burning is left, which puts the cutoff at the frame
    // boundary nearest the ideal instant and leaves the residual symmetric. The rising-again test
    // behind it is the backstop for a solve that never converges at all.
    private bool ShouldCutOff(in IcbmState state)
    {
        if (_toGain <= BurnoutGuidance.CutoffMetresPerSecond) return true;

        // Against the throttle the vehicle actually has, never the one that was asked for. A stack
        // whose motors do not throttle, or one still ramping down, would otherwise be cut off on a
        // prediction of how much velocity the last frame adds that is several times too small.
        double achieved = Math.Clamp(state.ThrottleAchieved, 0.0, 1.0);
        if (_countdown <= 0.5 * _lastStep * Math.Max(achieved, 1e-3)) return true;

        double oneStep = state.Booster.AccelerationNow * _lastStep;
        return _lowestToGain < 50.0 && _toGain > _lowestToGain + Math.Max(oneStep, 1.0);
    }

    private IcbmCommand Coasting(in IcbmState state)
    {
        double short_ = _fellShort ? _toGain : 0.0;
        Phase = IcbmPhase.Coast;
        _toGain = 0.0;

        bool ready = !_fellShort && state.Altitude >= Config.DeployAltitudeMetres;

        // A burn that ended because the tanks did is not the same as one that ended because the
        // shot was complete, and the two are indistinguishable from every other number on the
        // panel. The warheads are held back as well as the message changing: releasing them on a
        // trajectory known to fall short spreads them across whatever is under the short fall.
        string hold = _fellShort
            ? $"burn ended {short_:F0} m/s short of the solution"
            : ready ? "coasting, warheads may be released" : "coasting to release altitude";

        return new IcbmCommand(IcbmPhase.Coast, Vec.Unit(state.AirflowCci), 0.0, EngineOn: false,
                               RequestStage: false, VelocityToGain: short_, SecondsToCutoff: 0.0,
                               ReadyToDeploy: ready, Hold: hold);
    }

    private IcbmCommand Idle(in IcbmState state, string why)
        => new(Phase == IcbmPhase.NoSolution ? IcbmPhase.NoSolution : IcbmPhase.Idle,
               state.UpCci, 0.0, EngineOn: false, RequestStage: false,
               VelocityToGain: 0.0, SecondsToCutoff: 0.0, ReadyToDeploy: false, Hold: why);

    // Two things bound what may be commanded, and both exist to stop guidance flying the stack
    // into something. The airflow limit protects the vehicle; the horizon floor protects against a
    // handover on an airless body, where there is no dynamic pressure to wait for and the closed
    // loop would otherwise take over at treetop height already pointing downhill.
    private double3 Limit(double3 wanted, in IcbmState state)
    {
        double3 held = AscentProfile.HoldIntoTheAirflow(wanted, state.AirflowCci, state.DynamicPressurePa,
                                                        Config.MaxAngleOfAttackDeg);

        if (state.Altitude >= Config.TurnEndMetres) return held;

        double3 up = state.UpCci;
        if (Vec.Dot(held, up) >= 0.0) return held;

        double3 horizontal = Vec.Unit(Vec.RejectFrom(held, up));
        return horizontal.Equals(Vec.Zero) ? up : horizontal;
    }

    private IcbmCommand Fly(IcbmPhase phase, double3 direction, in IcbmState state, string hold)
    {
        bool stage = Config.AutoStage && _stageCooldown <= 0.0
                  && (!state.PropellantAvailable || !state.Booster.CanThrust);
        // The dry timer deliberately keeps running across a stage request. Clearing it here means
        // a stack with nothing left to stage asks again every cooldown for ever, and a flight that
        // is over never ends.
        if (stage) _stageCooldown = StageCooldownSeconds;

        if (phase != IcbmPhase.ClosedLoop) _throttle = 1.0;

        return new IcbmCommand(phase, direction, _throttle, EngineOn: true, stage,
                               _toGain, Math.Max(_countdown, 0.0), ReadyToDeploy: false, Hold: hold);
    }
}
