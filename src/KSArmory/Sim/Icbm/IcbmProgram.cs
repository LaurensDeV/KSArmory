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
    double StructuralLimitGee = 0.0,

    /// <summary>
    /// Whether the running stage stops when told to. False for a solid motor, which burns to the end
    /// once lit; true for anything the caller cannot tell, which is every engine KSA throttles.
    /// </summary>
    bool RunningStageCanStop = true,

    /// <summary>The least throttle the running engines hold, or zero if the caller cannot tell.</summary>
    double MinThrottle = 0.0,

    /// <summary>Which way the engines are actually pushing, or zero if the caller cannot tell.</summary>
    double3 ThrustAxisCci = default,

    /// <summary>
    /// What the running stage alone has left, or NaN if unknown. <see cref="Booster"/>'s figure puts the
    /// whole vehicle's propellant behind the running engines, which for a solid with liquid stages
    /// above it is several times what the grain can give.
    /// </summary>
    double RunningStageDeltaV = double.NaN,

    /// <summary>The air at a point, as a ratio to the reference sea level, or null if the caller cannot say.</summary>
    Func<double3, double>? DensityRatioAt = null,

    /// <summary>What the bus carries, whose drag an arc is flown with; null if the caller cannot say.</summary>
    MunitionProfile? Warhead = null,

    /// <summary>Where a warhead leaves from, against the bus's own position; zero if the caller cannot say.</summary>
    double3 ReleaseOffsetCci = default,

    /// <summary>What a warhead leaves with on top of the bus's velocity -- its tube's throw; zero if the caller cannot say.</summary>
    double3 ReleaseImpulseCci = default,

    /// <summary>Something is running and every running engine is a solid, which ignores the throttle; false if the caller cannot say.</summary>
    bool OnlySolidsRunning = false,

    /// <summary>
    /// The load the engine judges the airframe on now, KSA's smoothed acceleration in standard gravities; NaN if the caller
    /// cannot say.
    /// </summary>
    double LoadGee = double.NaN)
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
    double ShortfallMetresPerSecond,

    /// <summary>
    /// The engine is being switched on and off to average a throttle below its floor, so an off frame holds the lever at
    /// <see cref="Throttle"/> rather than winding it up for a relight.
    /// </summary>
    bool Pulsing = false);

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
internal sealed partial class IcbmProgram
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

    /// <summary>
    /// Seconds of burn the pitch programme leaves for the closed loop under
    /// <see cref="IcbmConfig.FlyAnyRange"/>: once less than this much burning is left, the throttle
    /// comes down in proportion, so a short shot reaches thin air with something still to gain
    /// rather than kilometres a second too much. Flown at 418 km; <c>docs/SHORT-RANGE.md</c> Step 2.
    /// A long shot never gets near it before handover.
    /// </summary>
    public const double AscentReserveSeconds = 15.0;

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

    /// <summary>
    /// How long the last seconds' throttle-down may run before the program says it has stalled. A closing one lasts
    /// 10-15 s; a liquid stack stood still over a 25 km target held one for twelve minutes. <c>docs/SHORT-RANGE.md</c>.
    /// </summary>
    public const double StalledRampSeconds = 30.0;

    /// <summary>
    /// Seconds in the throttle-down before <see cref="IcbmConfig.ShortShotPushesThroughAStall"/> acts. A closing one
    /// lasts 10-15 s, so most flights never reach it.
    /// </summary>
    public const double PushesThroughAfterSeconds = 10.0;

    /// <summary>The steering freeze while pushing through, in m/s: drag keeps pushing across a frozen line.</summary>
    public const double PushesThroughHoldBelow = 1.0;

    private const double PushFilterSeconds = 0.5;

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

    /// <summary>
    /// The affordable arrival the latched floor was a fraction of, kept as it read at the instant of
    /// latching.
    ///
    /// <para><see cref="SteepestAffordableArrivalDeg"/> goes on moving after that, so reading it
    /// afterwards says what the stack could afford by then rather than what the fraction was applied
    /// to — and a night flown at several fractions cannot be read at all without the multiplicand
    /// each arm actually got.</para>
    /// </summary>
    public double ArrivalFloorFromDeg { get; private set; } = double.NaN;

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

    // What the running stage cannot be stopped from adding, as of the last solve, and whether the
    // arc was lofted to need exactly that.
    private double _unavoidable;
    private double _secondsInTheRamp;
    private double3 _lastToGainCci;
    private double3 _pushCci;
    private bool _absorbing;

    // Whether the closed loop took over at the top of a climb still inside the air.
    private bool _handedOverInTheAir;

    // Whether this flight has been flown as a short shot at all: lofted to absorb, or handed over in
    // the air. Nothing that only a short shot needs may reach one that never was.
    private bool _shortShot;

    private bool _releasesAtCutoff;

    // Whether a short shot's engine is off, coasting out of the air before its last burn.
    private bool _paused;

    // Set by the closed loop for the frame it is building: the engine stays off until the vehicle
    // has turned to the burn.
    private bool _waitingForAttitude;

    // Whether the line is still the one carried in from before the closed loop took over or the engine
    // last stopped. A slowed line turns from where it is, so it has to start from a fresh solve.
    private bool _lineCarriedOver = true;
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

    /// <summary>What the shot still needs, in m/s, as of the last solve; NaN before one.</summary>
    public double NeedMetresPerSecond { get; private set; } = double.NaN;

    /// <summary>What the stack has left against it, in m/s; NaN when unknown.</summary>
    public double HaveMetresPerSecond { get; private set; } = double.NaN;

    /// <summary>
    /// Whether <see cref="HaveMetresPerSecond"/> is the engine's figure for the whole stack rather than the running
    /// stage's exhaust velocity over all the propellant, which reads a staged rocket low.
    /// </summary>
    public bool HaveIsTheWholeStack { get; private set; }

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
    /// Whether this flight has been flown as a short shot: lofted to absorb an unstoppable stage, or
    /// handed over inside the air. Only <see cref="IcbmConfig.FlyAnyRange"/> makes one.
    /// </summary>
    public bool IsShortShot => _shortShot;

    /// <summary>Whether this flight cut off in the air as a short shot, and so releases at cutoff.</summary>
    public bool ReleasesAtCutoff => _releasesAtCutoff;

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

    /// <summary>Seconds of full-throttle burn left on the cutoff countdown; infinite when none is running.</summary>
    public double Countdown => _countdown;

    /// <summary>Below this velocity to gain the steering direction is held, as of the last solve.</summary>
    public double HoldDirectionBelowNow { get; private set; } = double.NaN;

    /// <summary>Whether the last solve turned the line at a bounded rate (<see cref="IcbmConfig.ShortShotSlowsLineSeconds"/>).</summary>
    public bool LineSlowed { get; private set; }

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

    /// <summary>Velocity to gain on the first solve with the engines burning, in m/s; NaN before one.</summary>
    public double ToGainAtIgnition { get; private set; } = double.NaN;

    /// <summary>
    /// The thrust's delta-v over the burn, in m/s. Less <see cref="ToGainAtIgnition"/> it is what the ascent lost to
    /// gravity, drag and steering, which a reach drawn before launch has to allow for.
    /// </summary>
    public double ThrustSpentMetresPerSecond { get; private set; }

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
    /// Whether the aim correction frozen by the burn may start again: once the engines are off, or under
    /// <see cref="IcbmConfig.AimResumesAtCutoff"/> only once they have cut off, so a hold for a burn window,
    /// with nothing settled to resume, does not spend it.
    /// </summary>
    public bool ResumesTheAim => Config.AimResumesAtCutoff ? Phase == IcbmPhase.Coast : !IsBurning;

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

    /// <summary>
    /// Give up the committed arrival, so the next solve takes the cheapest arc again.
    ///
    /// <para><b>For the one state the latch cannot get itself out of.</b> The arrival is pinned
    /// during the burn and the two branches that can unpin it both live there, so after cutoff it
    /// stands whatever the trajectory does. What the trim is then asked for is
    /// <c>RequiredVelocity(arrival) − v</c>, which moves about <b>2.35 m/s for every second the
    /// arrival is out</b> — so <see cref="BusTrim.MaxMetresPerSecond"/> is crossed at 4.3 s and the
    /// trim gives up on a bus it could otherwise have flown. Flown once in twelve shots: all eight
    /// rockets 75-99 km out on clean burns, every trim refusing before its first pulse.</para>
    ///
    /// <para><b>Why this does not reintroduce the runaway it was latched against.</b> That failure
    /// is a loft re-applied to the cheapest arc from the vehicle's current state, which converges on
    /// the arc it is already flying and walks the answer outward every cycle — measured at 162 km.
    /// It needs thrust. After cutoff the trajectory is fixed but for the trim's own metre a second,
    /// so re-solving is idempotent: the answer does not move. The latch's purpose expires exactly
    /// where the burn does.</para>
    ///
    /// <para>The caller decides when, and it should be a state that is already lost rather than a
    /// threshold — <c>Ksa/Icbm/IcbmComputer.cs</c> asks only once the trim has refused over its ceiling.
    /// </para>
    /// </summary>
    public bool ReleaseArrival()
    {
        if (!double.IsFinite(_arrivalFromLaunch)) return false;

        _arrivalFromLaunch = double.NaN;
        return true;
    }

    public IcbmProgram(IcbmConfig config) => Config = config;

    /// <summary>Back to the pad. The one way a flight can be un-flown.</summary>
    public void Reset()
    {
        Phase = IcbmPhase.Idle;
        Reach = IcbmReach.Unknown;
        NeedMetresPerSecond = double.NaN;
        HaveMetresPerSecond = double.NaN;
        HaveIsTheWholeStack = false;
        Arc = null;
        ReferencePositionCci = Vec.Zero;
        SecondsSinceReference = 0.0;
        _resolveCoastArc = false;
        DownrangeCci = Vec.Zero;
        _cutoffSeed = 0.0;
        _flightSeed = double.NaN;
        _sinceSolve = double.PositiveInfinity;
        _countdown = double.PositiveInfinity;
        HoldDirectionBelowNow = double.NaN;
        LineSlowed = false;
        _dragEast = 0.0;
        _dragNorth = 0.0;
        _dragEngaged = false;
        DragMissMetres = double.NaN;
        _lineCarriedOver = true;
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
        _unavoidable = 0.0;
        _absorbing = false;
        _handedOverInTheAir = false;
        _shortShot = false;
        _releasesAtCutoff = false;
        _paused = false;
        _fellShort = false;
        _arrivalFloorUnaffordable = false;
        ResidualAtCutoff = double.NaN;
        ResidualVectorCci = Vec.Zero;
        AccelerationAtCutoff = double.NaN;
        StepAtCutoff = double.NaN;
        ThrottleAtCutoff = double.NaN;
        LongestStepWhileBurning = 0.0;
        ToGainAtIgnition = double.NaN;
        ThrustSpentMetresPerSecond = 0.0;
        _arrivalFromLaunch = double.NaN;
        _secondsInTheRamp = 0.0;
        _lastToGainCci = default;
        _pushCci = default;
        _reachHold = "";
        _reachIfNoArc = IcbmReach.NoTrajectory;
        _sinceWindow = double.PositiveInfinity;
        _sinceArrivalBudget = double.PositiveInfinity;
        SteepestAffordableArrivalDeg = double.NaN;
        ArrivalFloorDeg = double.NaN;
        ArrivalFloorFromDeg = double.NaN;
        _windowWait = double.NaN;
        _windowCost = 0.0;
        _windowDirection = Vec.Zero;
        _shortfall = 0.0;
        _closestOffPlane = double.NaN;
        ReleaseGateSeconds = double.NaN;
    }

    /// <summary>
    /// When the warheads may first go, in seconds before arrival, overriding
    /// <see cref="IcbmConfig.ReleaseBeforeArrivalSeconds"/>. NaN — the default — is no override.
    /// </summary>
    /// <remarks>
    /// <b>Only a walk of several targets writes it.</b> <see cref="ReleaseItinerary"/> counts its
    /// releases back from the gate so the <em>last</em> one lands on it, which means the first has
    /// to happen <c>(N-1) x 65 s</c> earlier — and a walk of one returns the gate itself, so a
    /// single-target flight leaves this NaN and reads the setting live as it always has.
    /// </remarks>
    public double ReleaseGateSeconds { get; set; } = double.NaN;

    /// <summary>
    /// The gate in force, which is the setting unless a walk has asked for an earlier one.
    ///
    /// <para>One expression, because everything that counts down to a release has to reach the same
    /// answer: the coast's own gate, the readout, and the warp the correction needs held down
    /// before it.</para>
    /// </summary>
    public double ReleaseGate
        => double.IsFinite(ReleaseGateSeconds) ? ReleaseGateSeconds
                                               : Config.ReleaseBeforeArrivalSeconds;

    public IcbmCommand Update(double stepSeconds, in IcbmState state)
    {
        double step = double.IsFinite(stepSeconds) && stepSeconds > 0.0 ? stepSeconds : 0.0;
        if (step > 0.0) _lastStep = step;
        if (step > 0.0 && IsBurning) LongestStepWhileBurning = Math.Max(LongestStepWhileBurning, step);
        if (step > 0.0 && IsBurning && state.Booster.AccelerationNow > 0.0)
        {
            ThrustSpentMetresPerSecond += state.Booster.AccelerationNow * Math.Clamp(state.ThrottleAchieved, 0.0, 1.0) * step;
        }

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

        // A solid that has just delivered the arc it was matched to leaves a residual the bus trim
        // can fly; lighting the next stage to add it would put a full-sized motor to work in the air
        // for a few metres a second, through an airflow limit that stops it pointing anywhere useful.
        if (Config.FlyAnyRange && dry && _absorbing && _toGain <= BusTrim.MaxMetresPerSecond)
        {
            _releasesAtCutoff = state.AirDensityRatio >= Medium.NoticeableDensity || StaysUnderTheReleaseAltitude(state);
            ResidualAtCutoff = _toGain;
            ResidualVectorCci = _toGainVectorCci;
            AccelerationAtCutoff = state.Booster.AccelerationNow;
            StepAtCutoff = _lastStep;
            ThrottleAtCutoff = state.ThrottleAchieved;
            Phase = IcbmPhase.Coast;
            return Coasting(state);
        }

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
        bool stage = Config.AutoStage && _stageCooldown <= 0.0 && (unlit || SolidsUnderWeight(state))
                     && (_thrustSeen || !_everLit);

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

        // The stage after the solids lights at whatever the lever was left at, and it comes down at about
        // 0.7/s: lit at full, a 30 m/s margin is a 0.3 s burn at 2 m/s a frame.
        if (state.OnlySolidsRunning
            && ((Config.SolidsLeaveMetresPerSecond > 0.0 && _absorbing) || DropsSolidsUnderWeight(state)))
        {
            _throttle = MinCommandedThrottle;
        }

        if (phase == IcbmPhase.PitchProgram && !Config.FlyAnyRange) _throttle = HoldBackTheAscent(_throttle, state);

        (bool pulsing, bool pulsedOff) = PulseBelowTheFloor(state);
        _pulsedOff = pulsedOff;

        bool waiting = _waitingForAttitude && phase == IcbmPhase.ClosedLoop;
        _waitingForAttitude = false;

        // Waiting for the vehicle to come round, the engine stays lit at the least it will burn: a
        // stack that steers by gimballing its engines has only reaction control without them, and
        // flown at 200 km one tumbled into the sea with the engine held off until it was pointed.
        // Only the coast out of the air is a real shutdown.
        if (waiting && !_paused)
        {
            waiting = false;
            _throttle = MinCommandedThrottle;
        }

        double commanded = pulsing && !waiting ? state.MinThrottle : _throttle;

        return new IcbmCommand(phase, direction, commanded, EngineOn: !waiting && !pulsedOff, stage,
                               _toGain, Math.Max(_countdown, 0.0), ReadyToDeploy: false, Hold: hold,
                               Reach: Reach, SecondsToArrival: SecondsToArrival, SecondsToBurn: 0.0,
                               ShortfallMetresPerSecond: _shortfall, Pulsing: pulsing && !waiting);
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
