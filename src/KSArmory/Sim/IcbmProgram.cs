using Brutal.Numerics;

namespace KSArmory;

/// <summary>Where a flight has got to. The phases run in order and never run backwards.</summary>
internal enum IcbmPhase
{
    Idle,
    Rising,
    PitchProgram,

    /// <summary>Coasting on purpose, because the cheapest moment to burn has not arrived.</summary>
    Holding,

    ClosedLoop,
    Coast,
    NoSolution,
}

/// <summary>Whether the shot can be made at all, which is three different answers.</summary>
internal enum IcbmReach
{
    Unknown,
    Reachable,

    /// <summary>A trajectory exists and the tanks cannot fly it.</summary>
    ShortOfPropellant,

    /// <summary>No arc reaches the target from anywhere on this orbit.</summary>
    NoTrajectory,
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
    double ThrottleAchieved = 1.0,

    /// <summary>
    /// Real seconds in this frame, as opposed to simulated ones.
    ///
    /// <para>Planning is a computation budget rather than physics, so it is paced by the wall
    /// clock. Paced by simulated time it runs once a frame at high warp — five simulated seconds
    /// being two milliseconds of real time — and a search costing most of a frame then costs
    /// every frame.</para>
    /// </summary>
    double PlayerStepSeconds = 0.0)
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
    string Hold,
    IcbmReach Reach,
    double SecondsToArrival,
    double SecondsToBurn,
    double ShortfallMetresPerSecond);

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
    /// <summary>
    /// The longest step a guided burn survives.
    ///
    /// <para>An engine can only be shut down on a frame boundary, so the velocity left at cutoff is
    /// whatever the last step added — <c>acceleration x step x throttle</c>. At the 170-second steps
    /// high timewarp hands out that is kilometres per second, and the shot lands on another
    /// continent. So a burn is something warp has to be held down for, exactly as rounds in the air
    /// are — see <see cref="WarpPolicy"/>, and this is the number that asks for it.</para>
    ///
    /// <para><b>Deliberately no tighter than a round's.</b> Asking for more is asking the world to
    /// run slower than anything else in the mod needs, and the policy answering that request is a
    /// control loop against a shared actuator: from a thousand times speed the first thing it
    /// computes is a speed of nearly zero, which pauses the game and then abandons the burn for not
    /// being able to run slow enough. The accuracy bought is not worth what it costs — a third of a
    /// second of step is a few hundred metres at the far end, and cancelling the shot is all of
    /// it.</para>
    /// </summary>
    public const double MaxFaithfulStep = 0.3;

    /// <summary>How often the trajectory is re-solved. Everything between is the countdown.</summary>
    public const double SolveIntervalSeconds = 0.25;

    /// <summary>Inside this much of cutoff, solve every step. It is thirty frames and it decides the shot.</summary>
    public const double SolveEveryStepWithin = 0.75;

    /// <summary>
    /// How often the departure time is searched while holding, in <em>real</em> seconds.
    ///
    /// <para>Deliberately slow, and deliberately not on simulated time. One search is a few dozen
    /// trajectory solves and costs a good part of a frame; at a thousand times speed a simulated
    /// interval of any sensible size elapses every frame, so the search would run every frame and
    /// halve the frame rate exactly when the world is moving fastest. Nothing about a coast changes
    /// fast enough to want it more often than this, and the countdown in between is arithmetic.
    /// </para>
    /// </summary>
    public const double WindowIntervalSeconds = 5.0;

    /// <summary>
    /// How much waiting has to save, in metres per second, before it is worth doing.
    ///
    /// <para>Absolute rather than proportional, because the thing being traded away is <em>time</em>
    /// and a fraction says nothing about how much. Ninety metres a second is a fifth off a cheap
    /// deorbit and is not worth spending an hour and a half in orbit to collect; the cases that
    /// genuinely need waiting save kilometres a second, because leaving now means reversing the
    /// whole orbital velocity.</para>
    ///
    /// <para>The margin also stops the computer dithering. The cheapest departure drifts by seconds
    /// between searches, and a proportional test near its own threshold flips on that noise.</para>
    /// </summary>
    public const double WaitMustSaveMetresPerSecond = 1000.0;

    /// <summary>
    /// Assumed half-burn lead, for a vehicle whose engines are not running.
    ///
    /// <para>A finite burn has to start before the instant an impulsive one would, or it finishes
    /// late. That lead is half the burn duration — which cannot be known while coasting, because
    /// KSA reports the performance of engines that are <em>running</em> and none are.</para>
    /// </summary>
    public const double AssumedBurnLeadSeconds = 20.0;

    /// <summary>Warp is held this long before the window opens, not only during the burn itself.</summary>
    public const double WarpHoldLeadSeconds = 60.0;

    /// <summary>Long enough for the stack to settle before the next stage is considered.</summary>
    public const double StageCooldownSeconds = 1.5;

    /// <summary>
    /// How little must be left before the rising-again backstop may end a burn.
    ///
    /// <para>That backstop exists for a solve that never converges, and it has to be held to
    /// <em>nearly finished</em> or it becomes the thing that ruins a shot rather than the thing
    /// that saves one. Velocity still to gain is noisy near the end, so a loose threshold lets one
    /// upward tick cut the engines with tens of metres a second unspent — and at a thousand-odd
    /// metres of range per metre a second, forty of those is fifty kilometres short, on an
    /// otherwise perfect trajectory.</para>
    /// </summary>
    public const double BackstopBelow = 2.0;

    /// <summary>Propellant unavailable for this long, with a stage already asked for, ends the burn.</summary>
    public const double DrySecondsBeforeGivingUp = 4.0;

    /// <summary>
    /// How much full-throttle burn is left when the throttle starts coming back.
    ///
    /// <para>An engine can only be shut down on a frame boundary, so the velocity error left at
    /// cutoff is whatever the last frame added. Coming back to a fraction of thrust for the last
    /// moment divides that error by the same fraction, and costs a fraction of a second of
    /// burn.</para>
    ///
    /// <para>Nothing depends on the vehicle honouring it. A stack whose motors cannot be throttled
    /// at all simply gets the error it would have had, because the cutoff test is written against
    /// the throttle that was <em>achieved</em>: an ignored command makes the threshold
    /// conservative rather than wrong.</para>
    /// </summary>
    public const double ThrottleDownSeconds = 2.0;

    /// <summary>The least thrust worth commanding. Below this, engines misbehave and so does the maths.</summary>
    public const double MinCommandedThrottle = 0.03;

    /// <summary>
    /// Below this much velocity still to gain, the direction it points in stops meaning anything.
    ///
    /// <para>Velocity-to-be-gained is a <em>difference</em>, so as it closes on zero its direction
    /// is the difference of two nearly equal vectors and swings wildly — measured at 161 degrees
    /// between one sample and the next, right at cutoff. Steering to that spins the vehicle at the
    /// exact moment it should be holding still for its warheads to leave along the line it was cut
    /// off on. So the last direction that meant something is held instead.</para>
    /// </summary>
    public const double HoldDirectionBelow = 5.0;

    // Every shot goes the direct way round. BallisticArc can fly the arc over the far side, and it
    // is not offered: that arc is a near-complete orbit, so it costs orbital-grade delta-v rather
    // than ballistic, and a switch for it would silently turn every shot into one that falls short.
    // The solver keeps the second family because a solver told there is only one fails at the
    // boundary between them.
    private const bool LongWay = false;

    private double _cutoffSeed;
    private double _flightSeed = double.NaN;
    private double _sinceSolve = double.PositiveInfinity;
    private double _countdown = double.PositiveInfinity;
    private double _toGain;
    private double3 _thrustDirCci;
    private double _stageCooldown;
    private double _drySeconds;
    private double _sinceLaunch;
    private double _lastStep;
    private double _throttle = 1.0;
    private double _lowestToGain = double.PositiveInfinity;
    private bool _fellShort;
    private double _arrivalFromLaunch = double.NaN;
    private string _reachHold = "";

    private double _sinceWindow = double.PositiveInfinity;
    private double _windowWait = double.NaN;
    private double _windowCost;
    private double3 _windowDirection;
    private double _shortfall;
    private double _closestOffPlane = double.NaN;

    public IcbmConfig Config { get; }

    public IcbmPhase Phase { get; private set; } = IcbmPhase.Idle;

    /// <summary>Whether this shot can be made, and if not, which way it cannot.</summary>
    public IcbmReach Reach { get; private set; } = IcbmReach.Unknown;

    /// <summary>The arc the last solve was flying to. Null until guidance has found one.</summary>
    public BallisticArc.Solution? Arc { get; private set; }

    /// <summary>Which way downrange is, refreshed while the pitch programme runs.</summary>
    public double3 DownrangeCci { get; private set; }

    public double SecondsSinceLaunch => _sinceLaunch;

    /// <summary>Velocity still to gain at the last solve. Zero once the burn is over.</summary>
    public double VelocityToGain => _toGain;

    /// <summary>
    /// What was still to gain the instant the engines stopped — the number that says whether a
    /// shot's error is the burn or the aim. NaN until a burn has ended.
    /// </summary>
    public double ResidualAtCutoff { get; private set; } = double.NaN;

    /// <summary>
    /// The closest the target ever comes to the plane being flown in, in degrees, or NaN before
    /// anything has looked. A floor well above zero is an inclination this orbit does not have.
    /// </summary>
    public double ClosestOffPlaneDegrees
        => double.IsFinite(_closestOffPlane) ? _closestOffPlane * 180.0 / Math.PI : double.NaN;

    /// <summary>Seconds until the burn should start, or zero once it has. NaN when unknown.</summary>
    public double SecondsToBurn => Phase == IcbmPhase.Holding ? Math.Max(_windowWait, 0.0)
                                 : IsBurning ? 0.0
                                 : double.NaN;

    /// <summary>Whether an engine is being commanded, which is when the step has to stay short.</summary>
    public bool IsBurning => Phase is IcbmPhase.Rising or IcbmPhase.PitchProgram or IcbmPhase.ClosedLoop;

    /// <summary>
    /// Whether the world has to be kept slow. The burn itself, and the last minute before it —
    /// a window is no use if one warped frame steps clean over it.
    /// </summary>
    public bool NeedsShortSteps
        => IsBurning
        || (Phase == IcbmPhase.Holding && double.IsFinite(_windowWait) && _windowWait <= WarpHoldLeadSeconds);

    /// <summary>
    /// How long until the warheads arrive, from now. NaN once the burn is over, where the flown
    /// prediction is both available and better.
    /// </summary>
    public double SecondsToArrival
    {
        get
        {
            if (Arc is not { } arc) return double.NaN;

            if (Phase == IcbmPhase.Holding && double.IsFinite(_windowWait))
            {
                return _windowWait + AssumedBurnLeadSeconds * 2.0 + arc.FlightSeconds;
            }

            if (IsBurning) return Math.Max(_countdown, 0.0) + arc.FlightSeconds;

            return double.NaN;
        }
    }

    public IcbmProgram(IcbmConfig config) => Config = config;

    /// <summary>Back to the pad. The one way a flight can be un-flown.</summary>
    public void Reset()
    {
        Phase = IcbmPhase.Idle;
        Reach = IcbmReach.Unknown;
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
        _sinceLaunch = 0.0;
        _lastStep = 0.0;
        _throttle = 1.0;
        _lowestToGain = double.PositiveInfinity;
        _fellShort = false;
        ResidualAtCutoff = double.NaN;
        _arrivalFromLaunch = double.NaN;
        _reachHold = "";
        _sinceWindow = double.PositiveInfinity;
        _windowWait = double.NaN;
        _windowCost = 0.0;
        _windowDirection = Vec.Zero;
        _shortfall = 0.0;
        _closestOffPlane = double.NaN;
    }

    public IcbmCommand Update(double stepSeconds, in IcbmState state)
    {
        double step = double.IsFinite(stepSeconds) && stepSeconds > 0.0 ? stepSeconds : 0.0;
        if (step > 0.0) _lastStep = step;

        _stageCooldown = Math.Max(0.0, _stageCooldown - step);
        _sinceSolve += step;
        _sinceWindow += state.PlayerStepSeconds > 0.0 ? state.PlayerStepSeconds : step;
        if (double.IsFinite(_windowWait)) _windowWait -= step;
        if (Phase is not (IcbmPhase.Idle or IcbmPhase.NoSolution)) _sinceLaunch += step;

        if (!Config.Armed) return Idle(state, "not armed");
        if (!state.HasAim) return Idle(state, "no target designated");
        if (!state.Body.IsUsable) return Idle(state, "no parent body");

        if (Phase == IcbmPhase.Coast) return Coasting(state);

        // Picked up wherever it happens to be, rather than always from a pad. On the ground it
        // needs the launch sequence; in thick air it needs the schedule; above the air the only
        // question left is *when*, which is what holding is for.
        if (Phase is IcbmPhase.Idle or IcbmPhase.NoSolution) Phase = PickUpFrom(state);

        if (Phase == IcbmPhase.Holding) return Hold(state);

        Resolve(state);

        if (Arc is null)
        {
            Phase = IcbmPhase.NoSolution;
            Reach = IcbmReach.NoTrajectory;
            return Idle(state, _reachHold);
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
            ResidualAtCutoff = _toGain;
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

    // Which phase this vehicle belongs in, given what it is doing rather than what it did.
    private IcbmPhase PickUpFrom(in IcbmState state)
    {
        bool onTheGround = state.Altitude < Config.TurnStartMetres
                        && Vec.Len(state.AirflowCci) < AscentProfile.VerticalRiseSpeed;

        if (onTheGround)
        {
            _sinceLaunch = 0.0;
            return IcbmPhase.Rising;
        }

        // Still enough air to bend the vehicle: fly the schedule until the loads come off. A
        // pick-up here is an ascent already under way, and the schedule plus the angle-of-attack
        // limiter is the same answer for both.
        if (state.DynamicPressurePa > Config.HandoverPressurePa) return IcbmPhase.PitchProgram;

        return IcbmPhase.Holding;
    }

    // Coasting on purpose. Nothing here commands an engine; the whole job is deciding when to.
    private IcbmCommand Hold(in IcbmState state)
    {
        if (_sinceWindow >= WindowIntervalSeconds || !double.IsFinite(_windowWait))
        {
            _sinceWindow = 0.0;

            if (BurnWindow.TryFind(state.Body, state.PositionCci, state.VelocityCci, state.AimNowCci,
                                   out BurnWindow.Window window, Config.Loft))
            {
                // Waiting is a fallback, not an optimisation. A weapon whose whole point is
                // arriving is not worth holding in orbit for ninety metres a second — but leaving
                // now can also be flatly impossible, or cost the entire orbital velocity, and that
                // is what this is for.
                bool worthWaiting = !double.IsFinite(window.CostIfLeavingNow)
                                 || window.Saving >= WaitMustSaveMetresPerSecond;

                _windowWait = worthWaiting ? window.WaitSeconds : 0.0;
                _windowCost = window.Cost;
                _windowDirection = window.BurnDirectionCci;
                _closestOffPlane = window.ClosestOffPlaneRadians;
                Arc = window.Arc;
                _flightSeed = window.Arc.CheapestFlightSeconds;
                AssessReach(state, window.Cost);
            }
            else
            {
                Phase = IcbmPhase.NoSolution;
                Reach = IcbmReach.NoTrajectory;
                return Idle(state, "no trajectory reaches that target from this orbit");
            }
        }

        double lead = state.Booster.CanThrust
                          ? 0.5 * state.Booster.SecondsToGain(_windowCost)
                          : AssumedBurnLeadSeconds;

        if (!double.IsFinite(lead)) lead = AssumedBurnLeadSeconds;

        if (_windowWait <= lead)
        {
            Phase = IcbmPhase.ClosedLoop;

            // Solve before steering, not after. The closed loop opens by asking whether the burn is
            // already finished, and the velocity still to gain is zero until something has worked
            // it out — so handing straight over cuts the engines off before they light.
            _sinceSolve = double.PositiveInfinity;
            Resolve(state);

            return Arc is null ? Idle(state, _reachHold) : ClosedLoop(state);
        }

        // Pointed where the burn will be, so the vehicle is already settled when the window opens.
        double3 facing = _windowDirection.Equals(Vec.Zero) ? Vec.Unit(state.VelocityCci) : _windowDirection;

        return new IcbmCommand(IcbmPhase.Holding, facing, 0.0, EngineOn: false, RequestStage: false,
                               VelocityToGain: _windowCost, SecondsToCutoff: double.NaN,
                               ReadyToDeploy: false,
                               Hold: $"holding for the burn window, {Clock(_windowWait)} away",
                               Reach: Reach, SecondsToArrival: SecondsToArrival,
                               SecondsToBurn: Math.Max(_windowWait, 0.0),
                               ShortfallMetresPerSecond: _shortfall);
    }

    private void Resolve(in IcbmState state)
    {
        bool burning = IsBurning;
        if (burning && _lastStep > 0.0) _countdown -= _lastStep * Math.Clamp(state.ThrottleAchieved, 0.0, 1.0);

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
                                      Config.Loft, LongWay, _cutoffSeed, _flightSeed, arrivalFromNow))
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

        if (_toGain > HoldDirectionBelow || _thrustDirCci.Equals(Vec.Zero))
        {
            _thrustDirCci = command.ThrustDirectionCci;
        }
        AssessReach(state, command.VelocityToGain);

        if (Phase is IcbmPhase.Rising or IcbmPhase.PitchProgram)
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
        else if (double.IsFinite(_arrivalFromLaunch) && !command.HeldTheArrival)
        {
            // The arrival that was latched turned out not to be solvable — a pinned transfer angle
            // can walk onto the one geometry Lambert cannot answer. Give it up rather than asking
            // for it again every cycle and taking the fallback every time; following the cheapest
            // arc is what this did before commitment and it works.
            _arrivalFromLaunch = double.NaN;
        }
    }

    // Whether the tanks can pay for the shot the solver found. KSA reports the mass of the whole
    // vehicle's propellant but the exhaust velocity of the engines actually running, so this is a
    // single-stage figure over a multi-stage load - which understates a staged vehicle, because
    // staging throws dry mass away. Understating is the right way round to be wrong: it calls a
    // marginal shot unreachable rather than flying one that is not.
    private void AssessReach(in IcbmState state, double required)
    {
        double available = state.Booster.DeltaVRemaining;

        if (!(available > 0.0))
        {
            Reach = IcbmReach.Unknown;
            _shortfall = 0.0;
            return;
        }

        _shortfall = Math.Max(0.0, required - available);
        Reach = _shortfall > 0.0 ? IcbmReach.ShortOfPropellant : IcbmReach.Reachable;
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
            // Recorded here rather than in Coasting, which clears it. What was left when the
            // engines stopped is the whole story of a shot that lands short on an otherwise
            // perfect trajectory, and reporting a zero says every burn closed perfectly - which is
            // exactly what a burn that ended forty metres a second early also says.
            ResidualAtCutoff = _toGain;
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
        return _lowestToGain < BackstopBelow && _toGain > _lowestToGain + Math.Max(oneStep, 1.0);
    }

    private IcbmCommand Coasting(in IcbmState state)
    {
        double shortBy = _fellShort ? _toGain : 0.0;
        Phase = IcbmPhase.Coast;
        _toGain = 0.0;
        if (_fellShort) Reach = IcbmReach.ShortOfPropellant;

        bool ready = !_fellShort && state.Altitude >= Config.DeployAltitudeMetres;

        // A burn that ended because the tanks did is not the same as one that ended because the
        // shot was complete, and the two are indistinguishable from every other number on the
        // panel. The warheads are held back as well as the message changing: releasing them on a
        // trajectory known to fall short spreads them across whatever is under the short fall.
        string hold = _fellShort
            ? $"burn ended {shortBy:F0} m/s short of the solution"
            : ready ? "coasting, warheads may be released" : "coasting to release altitude";

        // The line it was cut off on, not the airflow. The warheads leave along it, and a bus that
        // swings to prograde the moment the engines stop throws them off the solution it just spent
        // the whole burn arriving at.
        double3 held = _thrustDirCci.Equals(Vec.Zero) ? Vec.Unit(state.AirflowCci) : _thrustDirCci;

        return new IcbmCommand(IcbmPhase.Coast, held, 0.0, EngineOn: false,
                               RequestStage: false, VelocityToGain: shortBy, SecondsToCutoff: 0.0,
                               ReadyToDeploy: ready, Hold: hold, Reach: Reach,
                               SecondsToArrival: double.NaN, SecondsToBurn: double.NaN,
                               ShortfallMetresPerSecond: _fellShort ? shortBy : _shortfall);
    }

    private IcbmCommand Idle(in IcbmState state, string why)
        => new(Phase == IcbmPhase.NoSolution ? IcbmPhase.NoSolution : IcbmPhase.Idle,
               state.UpCci, 0.0, EngineOn: false, RequestStage: false,
               VelocityToGain: 0.0, SecondsToCutoff: 0.0, ReadyToDeploy: false, Hold: why,
               Reach: Reach, SecondsToArrival: double.NaN, SecondsToBurn: double.NaN,
               ShortfallMetresPerSecond: _shortfall);

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
                               _toGain, Math.Max(_countdown, 0.0), ReadyToDeploy: false, Hold: hold,
                               Reach: Reach, SecondsToArrival: SecondsToArrival, SecondsToBurn: 0.0,
                               ShortfallMetresPerSecond: _shortfall);
    }

    /// <summary>A duration a person can read at a glance, which "4271 s" is not.</summary>
    public static string Clock(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0.0) return "--:--";

        int whole = (int)Math.Round(seconds);
        int hours = whole / 3600;
        int minutes = whole % 3600 / 60;

        return hours > 0
            ? $"{hours}:{minutes:00}:{whole % 60:00}"
            : $"{minutes}:{whole % 60:00}";
    }
}
