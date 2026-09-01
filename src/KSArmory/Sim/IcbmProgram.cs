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

    /// <summary>Arcs reach it and none of them arrives steeply enough to satisfy the floor.</summary>
    TooShallow,
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
    double PlayerStepSeconds = 0.0,

    /// <summary>
    /// Whether whatever is correcting the aim has stopped moving it.
    ///
    /// <para>True for a caller that corrects nothing, which is every test and every vehicle whose
    /// aim is simply where it was pointed.</para>
    /// </summary>
    bool AimIsSteady = true,

    /// <summary>
    /// What the whole stack has left, across every stage it has not yet flown, or NaN if unknown.
    ///
    /// <para>The engine works this out for its own staging display and it is the only figure that
    /// accounts for throwing dry mass away. Without it the reach has to be judged on the running
    /// stage's exhaust velocity over the whole vehicle's propellant, which understates a staged
    /// rocket badly enough to call an ordinary ICBM unreachable on the pad.</para>
    /// </summary>
    double StackDeltaV = double.NaN,

    /// <summary>
    /// The acceleration the airframe is destroyed at, in standard gravities, or zero if the engine
    /// has not said.
    ///
    /// <para>KSA works it out from the vehicle's own bounding sphere — <c>max(5, 50 x 5/radius)</c>
    /// — so a long stack is held to a fraction of what a stubby one survives, and it is not a
    /// number an operator can be expected to know about somebody else's rocket. Zero is the reading
    /// being <em>absent</em>, which is not the same as there being no limit.</para>
    /// </summary>
    double StructuralLimitGee = 0.0)
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
    /// <para><b>Deliberately a round third of a second, which is a round's 0.32 to within a
    /// rounding.</b> Asking for materially less is asking the world to
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
    /// How often the steepest affordable arrival is re-searched, in <em>real</em> seconds.
    ///
    /// <para>Slower than the departure window, and for the same reason twice over: it is a bisection
    /// of trajectory solves, and it answers a question an operator reads rather than one the flight
    /// depends on.</para>
    /// </summary>
    public const double ArrivalBudgetIntervalSeconds = 10.0;

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

    /// <summary>
    /// How long before the release the world has to be back at normal speed.
    ///
    /// <para>Not tidiness, and not the release instant: the post-boost aim correction converges
    /// across the coast, and at a hundred times normal speed its steps are seconds long. Measured
    /// on a kept shot as a release probe's own miss going from <b>50 m to 520</b> when the gate was
    /// opened from 45 seconds to 20 — the walk did not move, the correction did. So a coast is
    /// worth warping right up to here and no further.</para>
    /// </summary>
    public const double SteadyBeforeReleaseSeconds = 45.0;

    /// <summary>
    /// The longest the arrival is left free while the aim is still moving.
    ///
    /// <para>Left free the arc follows the aim, which is the plant the correction converges
    /// against. But the latch exists for a reason — a lofted shot chases its own arc outward
    /// without it — so this bounds how long that reason is suspended.</para>
    /// </summary>
    public const double LatchArrivalWithinSeconds = 20.0;

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
    /// How much of the airframe's own limit the stack is flown at.
    ///
    /// <para>The engine destroys a vehicle when its load reaches that limit, and the load it tests
    /// is a lag of the real one with a time constant of the bounding sphere over 200 — a fraction
    /// of a second. So flying at the number itself is flying on the boundary, and the margin is the
    /// room a transient has to live in. The throttle is a servo moving at 0.7 a second, which is
    /// where transients come from.</para>
    /// </summary>
    public const double StructuralMarginFraction = 0.9;

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

    /// <summary>
    /// The same limit as frames of the burn <em>actually happening</em>, which is what it really is.
    ///
    /// <para>Five metres a second is about ten frames of a full-throttle stack, and
    /// <see cref="ThrottleDownSeconds"/> makes a frame an order of magnitude smaller — so a fixed
    /// number holds the direction seconds before cutoff instead of frames before it, and everything
    /// the required velocity does in between is left square to a line nothing can still thrust
    /// along. Anywhere from ten to eighty frames measures the same, so this is the middle of a
    /// plateau rather than a tuned value.</para>
    /// </summary>
    public const double HoldDirectionFrames = 20.0;

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

    // Whether the arc it is holding settled for a shallower arrival than was asked for, because
    // nothing steeper was affordable. Reported rather than refused -- and it describes the arc
    // currently held rather than latching, because a stack too heavy to afford an arrival at
    // lift-off can afford it once it is light, and a latch goes on calling the shot compromised
    // after it has stopped being.
    public bool ArrivalFloorUnaffordable => _arrivalFloorUnaffordable;

    /// <summary>
    /// The steepest arrival this stack could pay for from where it is, in degrees, or NaN before
    /// anything has been able to look.
    ///
    /// <para>What bounds the arrival-angle control, so an operator sees the ceiling instead of
    /// discovering it after the shot falls short. Re-searched on a slow <em>real</em>-time cadence
    /// for the same reason the departure window is — it is a handful of trajectory solves, which is
    /// far too dear per frame, and nothing about it changes quickly.</para>
    /// </summary>
    public double SteepestAffordableArrivalDeg { get; private set; } = double.NaN;

    /// <summary>
    /// The arrival floor this flight is actually holding to, whether asked for or worked out.
    ///
    /// <para><b>Latched, and that is the whole of why it is a field.</b> The steepest affordable
    /// arrival moves through a flight — the stack lightens, the geometry turns — and a floor that
    /// followed it would re-open the search every cycle against a different bound. That is the shape
    /// <c>docs/ARRIVAL-ANGLE.md</c> refuses for <see cref="IcbmConfig.Loft"/>: a predicate is
    /// idempotent where a multiplier is not, and a bound that walks unlatches the shot it is meant to
    /// pin.</para>
    /// </summary>
    public double ArrivalFloorDeg { get; private set; } = double.NaN;

    private double _sinceArrivalBudget = double.PositiveInfinity;

    private bool _arrivalFloorUnaffordable;

    // Whether the stage now lit has ever pushed. See the staging test in Fly.
    private bool _thrustSeen;

    // Whether anything aboard has ever pushed, which -- unlike _thrustSeen -- survives a stage
    // request. It is what separates lighting the first engine from throwing away a spent one.
    private bool _everLit;
    private double _drySeconds;
    private double _sinceLaunch;
    private double _sinceCutoff;
    private double _sinceClosedLoop;
    private double _lastStep;
    private bool _resolveCoastArc;
    private double _throttle = 1.0;
    private double _lowestToGain = double.PositiveInfinity;
    private bool _fellShort;
    private double _arrivalFromLaunch = double.NaN;
    private string _reachHold = "";
    private IcbmReach _reachIfNoArc = IcbmReach.NoTrajectory;

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

    /// <summary>What the stack could do at the last sample, for a readout to show beside a limit.</summary>
    public BoosterPerformance LastBooster { get; private set; }

    /// <summary>
    /// Where the last solve expects the engines to stop.
    ///
    /// <para>Paired with <see cref="BallisticArc.Solution.RequiredVelocityCci"/> it is the state the
    /// arc departs from — which is the only state worth predicting from during a burn. The
    /// vehicle's current one is mid-ascent and describes a trajectory nobody intends to fly.</para>
    /// </summary>
    public double3 CutoffPositionCci { get; private set; }

    /// <summary>
    /// Where the arc the trim nulls onto departs from, and how long ago that was.
    ///
    /// <para>The cutoff state while the shot is the one the burn solved. It moves to the vehicle's
    /// own position each time the coast arc is re-solved, because a transfer solved from where the
    /// bus <em>is</em> is the one it can actually fly — nulling onto a velocity required at a point
    /// the vehicle has since left spends propellant reproducing an error.</para>
    /// </summary>
    public double3 ReferencePositionCci { get; private set; }

    /// <inheritdoc cref="ReferencePositionCci"/>
    public double SecondsSinceReference { get; private set; }

    /// <summary>Which way downrange is, refreshed while the pitch programme runs.</summary>
    public double3 DownrangeCci { get; private set; }

    public double SecondsSinceLaunch => _sinceLaunch;

    /// <summary>
    /// How long the engines have been out, which is how far the coast has carried the vehicle from
    /// the state <see cref="Arc"/> and <see cref="CutoffPositionCci"/> describe.
    ///
    /// <para>That pair is a trajectory rather than a moment, so anything correcting the vehicle
    /// back onto it needs to know how far along it should be by now. Zero until the burn ends.</para>
    /// </summary>
    public double SecondsSinceCutoff => _sinceCutoff;

    /// <summary>Velocity still to gain at the last solve. Zero once the burn is over.</summary>
    public double VelocityToGain => _toGain;

    /// <summary>
    /// What was still to gain the instant the engines stopped — the number that says whether a
    /// shot's error is the burn or the aim. NaN until a burn has ended.
    /// </summary>
    public double ResidualAtCutoff { get; private set; } = double.NaN;

    /// <summary>
    /// The same residual as a vector, latched at cutoff, and it is the more useful of the two.
    ///
    /// <para>What a metre a second left over costs depends entirely on which way it points: on a
    /// deorbit, along the track it is about 1.8 km of miss and radially about 3.4 km. A magnitude
    /// cannot tell those apart, so a residual reported as a number alone leaves the miss it implies
    /// uncertain by a factor of two.</para>
    /// </summary>
    public double3 ResidualVectorCci { get; private set; }

    private double3 _toGainVectorCci;

    /// <summary>What the stack could do at cutoff, latched with the residual to explain it.</summary>
    public double AccelerationAtCutoff { get; private set; } = double.NaN;

    /// <summary>The step the cutoff landed on, which sets the floor under the residual.</summary>
    public double StepAtCutoff { get; private set; } = double.NaN;

    /// <summary>
    /// The longest step the burn was ever flown across.
    ///
    /// <para>Latched because a cutoff is only as good as the frame it lands on, and one long frame
    /// anywhere in the burn is worth <c>accel x step</c> of velocity nobody asked for. The step at
    /// cutoff alone cannot show it: a burn that ate a minute in the middle and then ran dry ends on
    /// an ordinary frame and reports an ordinary one.</para>
    /// </summary>
    public double LongestStepWhileBurning { get; private set; }

    /// <summary>
    /// The throttle the stack actually had when the engines stopped.
    ///
    /// <para>The floor under the residual is <c>acceleration x step x throttle</c>, so this is the
    /// third of the three and the only one anything can change. A ramp that is commanded and never
    /// arrives leaves the other two multiplied by one, and looks identical from outside to a ramp
    /// that was never asked for.</para>
    /// </summary>
    public double ThrottleAtCutoff { get; private set; } = double.NaN;

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

    /// <summary>
    /// The arrival the shot was committed to, as seconds from now. NaN before commitment.
    ///
    /// <para>Deliberately not <see cref="SecondsToArrival"/>, which stops answering once the burn
    /// is over because the flown prediction is better by then. This one is the <em>parameter</em>
    /// the arc was solved against rather than an estimate of when anything lands, and it is what a
    /// correction made after cutoff has to re-solve to: asking for the cheapest arrival instead
    /// gets back the trajectory the vehicle is already on, however far off the shot that is.</para>
    /// </summary>
    public double CommittedArrivalFromNow
        => double.IsFinite(_arrivalFromLaunch) ? _arrivalFromLaunch - _sinceLaunch : double.NaN;

    public IcbmProgram(IcbmConfig config) => Config = config;

    /// <summary>Back to the pad. The one way a flight can be un-flown.</summary>
    public void Reset()
    {
        Phase = IcbmPhase.Idle;
        Reach = IcbmReach.Unknown;
        Arc = null;
        ReferencePositionCci = Vec.Zero;
        SecondsSinceReference = 0.0;
        _resolveCoastArc = false;
        DownrangeCci = Vec.Zero;
        _cutoffSeed = 0.0;
        _flightSeed = double.NaN;
        _sinceSolve = double.PositiveInfinity;
        _countdown = double.PositiveInfinity;
        _toGain = 0.0;
        _thrustDirCci = Vec.Zero;
        _stageCooldown = 0.0;
        _thrustSeen = false;
        _everLit = false;
        _drySeconds = 0.0;
        _sinceLaunch = 0.0;
        _sinceCutoff = 0.0;
        _sinceClosedLoop = 0.0;
        _lastStep = 0.0;
        _throttle = 1.0;
        _lowestToGain = double.PositiveInfinity;
        _fellShort = false;
        _arrivalFloorUnaffordable = false;
        ResidualAtCutoff = double.NaN;
        ResidualVectorCci = Vec.Zero;
        AccelerationAtCutoff = double.NaN;
        StepAtCutoff = double.NaN;
        ThrottleAtCutoff = double.NaN;
        LongestStepWhileBurning = 0.0;
        _arrivalFromLaunch = double.NaN;
        _reachHold = "";
        _reachIfNoArc = IcbmReach.NoTrajectory;
        _sinceWindow = double.PositiveInfinity;
        _sinceArrivalBudget = double.PositiveInfinity;
        SteepestAffordableArrivalDeg = double.NaN;
        ArrivalFloorDeg = double.NaN;
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
        if (step > 0.0 && IsBurning) LongestStepWhileBurning = Math.Max(LongestStepWhileBurning, step);

        _stageCooldown = Math.Max(0.0, _stageCooldown - step);
        _sinceSolve += step;
        _sinceWindow += state.PlayerStepSeconds > 0.0 ? state.PlayerStepSeconds : step;
        _sinceArrivalBudget += state.PlayerStepSeconds > 0.0 ? state.PlayerStepSeconds : step;
        if (double.IsFinite(_windowWait)) _windowWait -= step;
        if (Phase is not (IcbmPhase.Idle or IcbmPhase.NoSolution)) _sinceLaunch += step;
        if (Phase == IcbmPhase.Coast) _sinceCutoff += step;
        if (Phase == IcbmPhase.Coast) SecondsSinceReference += step;
        if (Phase == IcbmPhase.ClosedLoop) _sinceClosedLoop += step;

        if (!Config.Armed) return Idle(state, "not armed");
        if (!state.HasAim) return Idle(state, "no target designated");
        if (!state.Body.IsUsable) return Idle(state, "no parent body");

        RefreshArrivalBudget(state);

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
            Reach = _reachIfNoArc;
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
            ResidualVectorCci = _toGainVectorCci;
            AccelerationAtCutoff = state.Booster.AccelerationNow;
            StepAtCutoff = _lastStep;
            ThrottleAtCutoff = state.ThrottleAchieved;
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

    // What the operator may ask for, refreshed rarely. The stack's whole delta-v where the engine
    // reports it, because that is the figure that accounts for throwing dry mass away -- the running
    // stage's alone understates a staged rocket badly enough to cap the control at a few degrees on
    // a vehicle that could fly thirty.
    private void RefreshArrivalBudget(in IcbmState state)
    {
        if (_sinceArrivalBudget < ArrivalBudgetIntervalSeconds) return;

        _sinceArrivalBudget = 0.0;

        double available = state.StackDeltaV > 0.0 && double.IsFinite(state.StackDeltaV)
                               ? state.StackDeltaV
                               : state.Booster.DeltaVRemaining;

        SteepestAffordableArrivalDeg = ArrivalBudget.SteepestAffordableDeg(
            state.Body, state.PositionCci, state.VelocityCci, state.AimNowCci, available, Config.Loft);

        LatchArrivalFloor();
    }

    // Once, the first time the budget answers. Preference zero leaves the operator's own number
    // alone, which is what ships; above zero the floor is that fraction of what the tanks can pay
    // for, and never below what was asked for outright.
    private void LatchArrivalFloor()
    {
        if (double.IsFinite(ArrivalFloorDeg)) return;

        if (!(Config.ArrivalPreference > 0.0) || !double.IsFinite(SteepestAffordableArrivalDeg))
        {
            return;
        }

        double wanted = Config.ArrivalPreference * SteepestAffordableArrivalDeg;

        ArrivalFloorDeg = Math.Max(Config.MinArrivalAngleDeg, wanted);
    }

    // What the search is bounded by: the latched floor, or the operator's own number.
    private double FloorDeg => double.IsFinite(ArrivalFloorDeg)
                                   ? ArrivalFloorDeg
                                   : Config.MinArrivalAngleDeg;

    /// <summary>
    /// Whether a vehicle is still sitting on the ground rather than already flying.
    ///
    /// <para>The test the phase machine picks a vehicle up by, and public because anything deciding
    /// whether a shot can be <em>started</em> from here has to ask the same question. Two of them
    /// would drift, and the failure is silent: a launch sequence entered for a vehicle that is
    /// already airborne flies a pitch programme from wherever it happens to be.</para>
    /// </summary>
    public static bool IsOnTheGround(double altitudeMetres, double airspeedMetresPerSecond,
                                     double turnStartMetres)
        => altitudeMetres < turnStartMetres
        && airspeedMetresPerSecond < AscentProfile.VerticalRiseSpeed;

    // Which phase this vehicle belongs in, given what it is doing rather than what it did.
    private IcbmPhase PickUpFrom(in IcbmState state)
    {
        if (IsOnTheGround(state.Altitude, Vec.Len(state.AirflowCci), Config.TurnStartMetres))
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
                                   out BurnWindow.Window window, Config.Loft, FloorDeg))
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
                (Reach, string why) = NoWindow(state);
                return Idle(state, why);
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

    // Never looser than HoldDirectionBelow, so a stack burning at full thrust holds where it
    // always did and only a throttled-down one holds later.
    private double HoldDirectionThreshold(in IcbmState state)
    {
        double frame = state.Booster.AccelerationNow * _lastStep
                     * Math.Clamp(state.ThrottleAchieved, 0.0, 1.0);

        return frame > 0.0 ? Math.Min(HoldDirectionBelow, HoldDirectionFrames * frame)
                           : HoldDirectionBelow;
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

        bool steered = BurnoutGuidance.TrySteer(
            state.Body, state.PositionCci, state.VelocityCci, state.AimNowCci, state.Booster,
            out BurnoutGuidance.Command command, Config.Loft, LongWay, _cutoffSeed, _flightSeed,
            arrivalFromNow, FloorDeg);

        // A floor is what to aim for, not a reason to fly nowhere. A stack that cannot afford the
        // arrival asked for still has a target, and the shallow arc it can afford is worth far more
        // than a refusal: the same rule as a shot short of the propellant, which is flown and
        // reported. What the operator loses is precision, and the readout says which angle it got.
        if (steered)
        {
            _arrivalFloorUnaffordable = false;
        }
        else if (FloorDeg > 0.0)
        {
            steered = BurnoutGuidance.TrySteer(
                state.Body, state.PositionCci, state.VelocityCci, state.AimNowCci, state.Booster,
                out command, Config.Loft, LongWay, _cutoffSeed, _flightSeed, arrivalFromNow);

            if (steered) _arrivalFloorUnaffordable = true;
        }

        if (!steered)
        {
            // A flight already under way keeps flying its schedule: the geometry a solve needs can
            // be momentarily out of reach on the way up, and abandoning a shot for it would throw
            // away a launch that is going perfectly well.
            if (Arc is null) (_reachIfNoArc, _reachHold) = WhyNot(state);
            return;
        }

        Arc = command.Arc;
        CutoffPositionCci = command.CutoffPositionCci;
        ReferencePositionCci = command.CutoffPositionCci;
        SecondsSinceReference = 0.0;
        _cutoffSeed = command.SecondsToCutoff;
        _flightSeed = command.Arc.CheapestFlightSeconds;
        _toGain = command.VelocityToGain;
        _toGainVectorCci = command.ToGainVectorCci;
        _lowestToGain = Math.Min(_lowestToGain, _toGain);

        double holdBelow = HoldDirectionThreshold(state);

        if (_toGain > holdBelow || _thrustDirCci.Equals(Vec.Zero))
        {
            _thrustDirCci = command.ThrustDirectionCci;
            _countdown = command.SecondsToCutoff;
        }
        else
        {
            // Steering is frozen, so thrust is no longer parallel to what is left to gain, and the
            // solver's countdown - the time to gain the whole *length* of it - overshoots. Only the
            // component along the line actually being thrust can still be removed; burning past it
            // grows the residual again, which is what the backstop was catching a whole metre a
            // second late.
            double along = Vec.Dot(command.ToGainVectorCci, _thrustDirCci);
            double seconds = state.Booster.SecondsToGain(Math.Max(along, 0.0));
            _countdown = double.IsFinite(seconds) ? seconds : 0.0;
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
        // Committed once the aim has stopped moving, or once the window runs out.
        //
        // Both loops are solving the same shot. Latching the arrival first makes the aim correction
        // solve against a pinned parameter: moving the aim then forces a different trajectory to
        // arrive at the same *instant*, which on a shallow arrival moves the impact several times
        // further than the aim moved and puts the correction above its stability limit. Left free,
        // the arc simply follows the aim and the same loop converges in a handful of cycles.
        //
        // Bounded, because the reason for latching at all is real: the cheapest arc from the
        // vehicle's current state converges on the arc it is already flying, so a loft above one
        // walks the answer outward every cycle and the shot chases a trajectory running away from
        // it — 162 km, measured. The window is what stops that being unbounded.
        if (Phase == IcbmPhase.ClosedLoop && !double.IsFinite(_arrivalFromLaunch)
            && (state.AimIsSteady || _sinceClosedLoop >= LatchArrivalWithinSeconds))
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

    // Whether the tanks can pay for the shot the solver found.
    //
    // Prefers the engine's own per-stage total, which is the only figure that accounts for staging
    // throwing dry mass away. Falling back to the running stage's exhaust velocity over the whole
    // vehicle's propellant understates a staged rocket badly -- enough to call an ordinary ICBM
    // unreachable while it sits on the pad with the range to spare. Understating is at least the
    // right way round to be wrong, which is why it remains the fallback.
    private void AssessReach(in IcbmState state, double required)
    {
        double available = state.StackDeltaV > 0.0 && double.IsFinite(state.StackDeltaV)
                               ? state.StackDeltaV
                               : state.Booster.DeltaVRemaining;

        if (!(available > 0.0))
        {
            Reach = IcbmReach.Unknown;
            _shortfall = 0.0;
            return;
        }

        _shortfall = Math.Max(0.0, required - available);
        Reach = _shortfall > 0.0 ? IcbmReach.ShortOfPropellant : IcbmReach.Reachable;
    }

    // Why there is no arc, as the banner to show and the line to print under it. A target beyond
    // any trajectory needs a different target; one no arc arrives steeply enough at needs the floor
    // lowered; one this stack cannot reach needs a bigger rocket.
    private (IcbmReach Reach, string Why) WhyNot(in IcbmState state)
    {
        if (!BallisticArc.TryCheapest(state.Body, state.PositionCci, state.VelocityCci, state.AimNowCci,
                                      out BallisticArc.Solution reachable, Config.Loft, LongWay,
                                      double.NaN, FloorDeg))
        {
            // Asked again with the floor off, because "there is no trajectory" and "there is no
            // trajectory that arrives that steeply" are the same silence from outside and only one
            // of them is about a setting the operator can move.
            if (FloorDeg > 0.0
                && BallisticArc.TryCheapest(state.Body, state.PositionCci, state.VelocityCci,
                                            state.AimNowCci, out BallisticArc.Solution shallow,
                                            Config.Loft, LongWay))
            {
                return (IcbmReach.TooShallow,
                        $"nothing arrives at {FloorDeg:F0} deg or steeper from here; "
                        + $"the cheapest arc arrives at {shallow.ArrivalAngleDeg:F0} deg");
            }

            return (IcbmReach.NoTrajectory, "no trajectory reaches that target");
        }

        // Everything past here keeps NoTrajectory, which is not literally true of either — the
        // detail is in the line beside it, and the reach is what the red banner reads. The floor
        // above is the one exception because it is the only one a control on this panel fixes.
        if (!state.Booster.CanThrust) return (IcbmReach.NoTrajectory, "no engine running");

        double needed = Vec.Len(reachable.VelocityToGain(state.VelocityCci));
        double have = state.Booster.DeltaVRemaining;
        return (IcbmReach.NoTrajectory,
                $"not enough in the tanks: needs {needed / 1000.0:F1} km/s, has {have / 1000.0:F1} km/s");
    }

    // The same question for the orbital case, where the search is over departures as well as
    // flight times: a floor that no window satisfies is not the same as a target the orbit cannot
    // reach, and the unconstrained search already measures the steepest arrival it saw.
    private (IcbmReach Reach, string Why) NoWindow(in IcbmState state)
    {
        if (FloorDeg > 0.0
            && BurnWindow.TryFind(state.Body, state.PositionCci, state.VelocityCci, state.AimNowCci,
                                  out BurnWindow.Window any, Config.Loft))
        {
            return (IcbmReach.TooShallow,
                    $"no window arrives at {FloorDeg:F0} deg or steeper; "
                    + $"the steepest one found arrives at {any.SteepestArrivalDeg:F0} deg");
        }

        return (IcbmReach.NoTrajectory, "no trajectory reaches that target from this orbit");
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
            ResidualVectorCci = _toGainVectorCci;
            AccelerationAtCutoff = state.Booster.AccelerationNow;
            StepAtCutoff = _lastStep;
            ThrottleAtCutoff = state.ThrottleAchieved;
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

    /// <summary>
    /// Ask for the arc to be re-solved from where the bus is now, to the aim it has now.
    ///
    /// <para>What turns the aim correction from a readout back into a lever after the engines stop.
    /// The warheads coast along whatever arc the bus is on, so moving the aim alone changes nothing
    /// — re-solving the transfer to the corrected point gives the trim something to null onto, and
    /// the trim is the only thing left aboard that can still move the impact.</para>
    ///
    /// <para>The arrival stays where the burn committed it. Solving to a fixed instant is what makes
    /// the plant a plain one: the aim moves, the arc follows it, and the impact moves by about as
    /// much again.</para>
    /// </summary>
    public void CorrectCoastArc() => _resolveCoastArc = true;

    private void ResolveCoastArc(in IcbmState state)
    {
        double remaining = CommittedArrivalFromNow;
        if (!double.IsFinite(remaining)) return;

        // From the vehicle's own position, not from the cutoff state. The bus has coasted since, and
        // a velocity required at a point it has left is one the trim would spend propellant flying
        // back to.
        if (!BallisticArc.TrySolve(state.Body, state.PositionCci, state.AimNowCci, remaining,
                                   out BallisticArc.Solution corrected, LongWay))
        {
            return;
        }

        Arc = corrected;
        ReferencePositionCci = state.PositionCci;
        SecondsSinceReference = 0.0;
    }

    private IcbmCommand Coasting(in IcbmState state)
    {
        if (_resolveCoastArc)
        {
            _resolveCoastArc = false;
            ResolveCoastArc(state);
        }

        double shortBy = _fellShort ? _toGain : 0.0;
        Phase = IcbmPhase.Coast;
        _toGain = 0.0;
        if (_fellShort) Reach = IcbmReach.ShortOfPropellant;

        // Late enough that the separation kick has little flight left to grow in, and the trim has
        // had the whole coast to converge. The altitude stays as the floor under it: it is what
        // stops a release inside the air, which the time alone would allow on a short shot.
        double toArrival = CommittedArrivalFromNow;
        bool closeEnough = Config.ReleaseBeforeArrivalSeconds <= 0.0
                           || !double.IsFinite(toArrival)
                           || toArrival <= Config.ReleaseBeforeArrivalSeconds;

        // The altitude is a floor on the way *up* only. Applied on the descent it shuts the gate
        // exactly when the release is meant to happen -- the vehicle drops back through it on the
        // way to the target -- and the warheads ride the bus into the atmosphere still aboard.
        bool climbing = Vec.Dot(state.VelocityCci, state.UpCci) > 0.0;
        bool highEnough = !climbing || state.Altitude >= Config.DeployAltitudeMetres;

        bool ready = !_fellShort && highEnough && closeEnough;

        // A burn that ended because the tanks did is not the same as one that ended because the
        // shot was complete, and the two are indistinguishable from every other number on the
        // panel. The warheads are held back as well as the message changing: releasing them on a
        // trajectory known to fall short spreads them across whatever is under the short fall.
        string hold = _fellShort
            ? !_everLit ? NothingEverLit()
            : $"burn ended {shortBy:F0} m/s short of the solution"
            : ready ? "coasting, warheads may be released"
            : closeEnough ? "coasting to release altitude"

            // Counted to the release rather than to the arrival. The arrival is already the
            // headline above this line, and it is not the thing being waited for: a coast ends
            // when the warheads go, minutes earlier.
            : $"holding the warheads, release in {Clock(toArrival - Config.ReleaseBeforeArrivalSeconds)}";

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

    // A shot that never lit reads exactly like one that burned out early -- the whole velocity is
    // still to gain either way -- and the two want completely different things done about them.
    private string NothingEverLit()
        => Config.AutoStage
               ? "the engines never lit: nothing the next sequence activated produced thrust"
               : "the engines never lit: automatic staging is off, so nothing fired one";

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

    /// <summary>
    /// The acceleration the stack is flown at, in standard gravities, or zero for no limit at all.
    ///
    /// <para>The tighter of two numbers that say different things. The operator's is about this
    /// shot — a stack they know is fragile, or one they want flown gently. The airframe's is what
    /// the engine will actually destroy it at, and it applies whether or not anybody typed
    /// anything: a computer that flies a rocket it was told nothing about cannot ask its operator
    /// for a limit only the engine knows.</para>
    /// </summary>
    public double AccelerationCapGee(in IcbmState state)
    {
        double airframe = state.StructuralLimitGee > 0.0
                              ? state.StructuralLimitGee * StructuralMarginFraction
                              : 0.0;

        double asked = Config.MaxAccelerationGee;

        if (asked <= 0.0) return airframe;
        if (airframe <= 0.0) return asked;

        return Math.Min(asked, airframe);
    }

    // The throttle held down to whatever keeps the stack inside that limit. A light upper stage on
    // a full-sized motor is the case: eighteen times its own weight in thrust is nothing unusual
    // once the boosters are gone, and flying it wide open tears the vehicle apart. Reads the
    // engine's own reported acceleration, so a stack that cannot throttle gets the number it would
    // have had.
    private double ThrottleUnderAccelerationCap(double wanted, in IcbmState state)
    {
        double capGee = AccelerationCapGee(state);
        if (capGee <= 0.0) return wanted;

        double full = state.Booster.AccelerationNow;
        if (full <= 0.0 || !double.IsFinite(full)) return wanted;

        // Against standard gravity rather than the local field: it is a structural limit on the
        // vehicle, and the number written on an airframe is in standard gravities.
        double cap = capGee * 9.80665;
        if (full <= cap) return wanted;

        return Math.Clamp(Math.Min(wanted, cap / full), 0.0, 1.0);
    }

    private IcbmCommand Fly(IcbmPhase phase, double3 direction, in IcbmState state, string hold)
    {
        LastBooster = state.Booster;

        if (state.PropellantAvailable && state.Booster.CanThrust) _thrustSeen = _everLit = true;

        // Nothing to burn with, which is two situations that read identically. KSA reports no
        // thrust and no propellant for an engine the sequence list has not activated, so a rocket
        // standing on its pad gives exactly a spent stage's reading -- and firing the next sequence
        // is the only thing that moves either of them on. Before anything has ever pushed that
        // sequence is the ignition; afterwards it is the stage below being thrown away.
        bool unlit = !state.PropellantAvailable || !state.Booster.CanThrust;

        // Ignition may be asked for again on the cooldown, because the sequence a player put first
        // is not necessarily the one with an engine in it. Throwing a stage away may not: the stack
        // below is gone and whatever is now lit has to prove itself first, or a stage that takes a
        // moment to come up is discarded on the very next cooldown. The dry timer bounds both, so
        // neither can walk down the sequence list for ever.
        bool stage = Config.AutoStage && _stageCooldown <= 0.0 && unlit && (_thrustSeen || !_everLit);

        // The dry timer deliberately keeps running across a stage request. Clearing it here means
        // a stack with nothing left to stage asks again every cooldown for ever, and a flight that
        // is over never ends.
        if (stage)
        {
            _stageCooldown = StageCooldownSeconds;
            _thrustSeen = false;
        }

        if (phase != IcbmPhase.ClosedLoop) _throttle = 1.0;

        _throttle = ThrottleUnderAccelerationCap(_throttle, state);

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
