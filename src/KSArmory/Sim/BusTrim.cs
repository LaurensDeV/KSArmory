using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Which way a post-boost vehicle is being pushed, as the six directions a thruster set is laid out
/// in. They are the control frame's axes rather than the world's: +X is the nose, +Y is to the
/// right, +Z is down.
/// </summary>
[Flags]
internal enum TrimAxes
{
    None = 0,
    Forward = 1,
    Backward = 2,
    Right = 4,
    Left = 8,
    Down = 16,
    Up = 32,
}

/// <summary>Everything the trim needs to know about the bus this cycle.</summary>
/// <param name="ReferencePositionCci">
/// Where the guidance's own solution says the vehicle was when the engines stopped.
/// </param>
/// <param name="ReferenceVelocityCci">
/// What it needed to be doing there — <see cref="BallisticArc.Solution.RequiredVelocityCci"/>.
///
/// <para>With the position, the trajectory the burn was flown to, which the trim puts the vehicle back on.</para>
/// </param>
/// <param name="SecondsSinceReference">How far along that trajectory the vehicle should be by now.</param>
/// <param name="NoseCci">The control frame's +X in the parent body's inertial frame.</param>
/// <param name="RightCci">Its +Y.</param>
/// <param name="DownCci">Its +Z.</param>
/// <param name="MayFire">
/// Whether the vehicle is allowed to push yet. False while it is still coasting clear of whatever
/// it dropped — see <see cref="SeparationClearance"/>.
///
/// <para>It keeps <em>solving</em> either way, so an error that grew during the wait can be told from
/// one that was always there.</para>
/// </param>
/// <param name="BudgetMetresPerSecond">
/// All the attitude-control propellant the flight may spend on trimming, across every null.
/// Negative is unlimited.
///
/// <para>Not folded into <see cref="MayFire"/>: a clearance wait lifts and a spent tank does not, and
/// withholding fire for good holds the warheads for the rest of the flight.</para>
/// </param>
/// <param name="KeepOutTowardCci">
/// Which way the discarded stage lies, and only while the bus is inside the keep-out. Zero — the
/// default — is no constraint, which is what every caller that has no stack to avoid passes.
///
/// <para><b>An interlock, not a gate</b>: nulling the relative velocity can close the gap a pass began
/// clear of, so each direction toward the stage is withheld for the frame. With every direction
/// withheld the loop <em>waits</em> rather than reporting itself trimmed; <see cref="MaxSeconds"/> and
/// the budget bound the wait.</para>
/// </param>
internal readonly record struct TrimSituation(
    BallisticBody Body,
    double3 PositionCci,
    double3 VelocityCci,
    double3 ReferencePositionCci,
    double3 ReferenceVelocityCci,
    double SecondsSinceReference,
    double3 NoseCci,
    double3 RightCci,
    double3 DownCci,
    bool MayFire = true,
    double BudgetMetresPerSecond = -1.0,
    double3 KeepOutTowardCci = default,

    /// <summary>
    /// The largest single answer worth flying, or NaN for <see cref="BusTrim.MaxMetresPerSecond"/>.
    ///
    /// <para>Nulling a shove is ones of m/s; a deliberate aim correction grows with the trajectory (11.5
    /// to 13.4 at 12,902 km), and only the caller knows which is happening.</para>
    /// </summary>
    double MaxMetresPerSecond = double.NaN,

    /// <summary>
    /// How long one of this bus's thruster pulses lasts, or zero for a bus that only holds its jets
    /// on — <see cref="IcbmConfig.PulseTrim"/>.
    ///
    /// <para>A hold's band is half of <c>acceleration x step</c>; a pulse is the thruster's
    /// <c>MinimumPulseTime</c>, about sixteen times finer, so the trim pulses below its band.</para>
    /// </summary>
    double PulseSeconds = 0.0,

    /// <summary>
    /// Whether a pulse phase that stops closing gives way to holding rather than ending the null —
    /// <see cref="IcbmConfig.StallFallsBackToHolding"/>.
    /// </summary>
    bool StallFallsBackToHolding = false,

    /// <summary>
    /// Whether the frame a hold still owes is taken off what is left to gain before the next
    /// direction is chosen — <see cref="IcbmConfig.TrimCountsTheCommandInFlight"/>.
    /// </summary>
    bool CountsTheCommandInFlight = false,

    /// <summary>
    /// How long the trim may be allowed to fire before it gives up, or NaN for <see cref="BusTrim.MaxSeconds"/> --
    /// longer for a hop whose divert burn needs it (<see cref="IcbmConfig.HopsSizeTheirOwnSlots"/>).
    /// </summary>
    double MaxSeconds = double.NaN);

/// <summary>What to fire and whether the warheads may go.</summary>
/// <param name="Acceleration">
/// What the thrusters were measured doing (m/s²), zero before anything is observed: a residual near
/// <c>acceleration x step</c> is a timing limit, not a control error.
/// </param>
/// <param name="Pulse">
/// Whether this frame's command is a pulse rather than a hold; the directions are commanded alike.
/// </param>
internal readonly record struct TrimCommand(
    TrimAxes Fire,
    bool Done,
    double ToGainMetresPerSecond,
    double Acceleration,
    string Said,
    bool Pulse = false);

/// <summary>
/// The post-boost vehicle's own velocity trim: what its thrusters and its own tank are for.
///
/// <para>After cutoff the decoupler's shove (about a metre a second on a six-tonne bus) and the cutoff
/// residual move the bus off its arc. The trim carries the flown trajectory forward, subtracts the
/// vehicle's velocity and thrusts along the difference.</para>
///
/// <para><b>Against the flown solution, never a fresh one</b>: a re-solved transfer depends on when it
/// is asked and goes stale as the bus coasts. <b>One direction at a time</b>, so every number is
/// observed along the direction fired and one that moves nothing is struck off. <b>It gives up rather
/// than holding warheads.</b> <c>docs/ICBM-GUIDANCE.md</c>, "The split costs a metre a second".</para>
/// </summary>
internal sealed class BusTrim
{
    /// <summary>
    /// Close enough to stop, in metres per second.
    ///
    /// <para>The floor under the post-boost correction, binding over the frame's 0.005 m/s quantum at
    /// 16 ms: 7-8 m of miss (<c>docs/ACCURACY-PLAN.md</c> 3cu).</para>
    /// </summary>
    public const double SettledMetresPerSecond = 0.02;

    /// <summary>
    /// What a direction must still owe to be worth firing, at a measured acceleration and step.
    /// <see cref="PostBoostAim"/> prices its floor off the same expression, so the two cannot
    /// disagree about what the trim can resolve.
    /// </summary>
    public static double StopBand(double acceleration, double step)
        => Math.Max(SettledMetresPerSecond,
                    acceleration > 0.0 && double.IsFinite(acceleration) && step > 0.0 && double.IsFinite(step)
                        ? 0.5 * acceleration * step
                        : 0.0);

    /// <summary>
    /// The same threshold for a bus that pulses, which stops inside <see cref="PulseFloorPulses"/>
    /// pulses rather than inside a frame's worth.
    ///
    /// <para><see cref="PostBoostAim"/> prices its release floor off the same call, so pulsing lowers it;
    /// pulses coarser than the band cannot raise it.</para>
    /// </summary>
    public static double StopBand(double acceleration, double step, double pulseSeconds)
    {
        double band = StopBand(acceleration, step);

        if (!(pulseSeconds > 0.0) || !double.IsFinite(pulseSeconds) || !(acceleration > 0.0)) return band;

        return Math.Min(band, PulseFloorPulses * acceleration * pulseSeconds);
    }

    /// <summary>
    /// How long after arming before it may call itself finished.
    ///
    /// <para>The split is deferred through the engine's input buffer, so at first the state solved against
    /// is the one <em>before</em> the shove.</para>
    /// </summary>
    public const double SettleSeconds = 1.5;

    /// <summary>
    /// The most this will accept as a trim, in metres per second.
    ///
    /// <para>A separation transient is about a metre a second; an answer in the tens is the solve asked the
    /// wrong question. Checked every cycle, since the aim correction and the trim can wind each other up.</para>
    /// </summary>
    public const double MaxMetresPerSecond = 10.0;

    /// <summary>
    /// The most one pass will fly, given what the loop above it allows.
    /// </summary>
    /// <remarks>
    /// <b>One expression for two callers</b>: <see cref="Update"/> refuses a demand over it, and a release
    /// walk must know it before committing to a hop. Never less than <see cref="MaxMetresPerSecond"/>;
    /// a spent budget is <see cref="WithinBudget"/>'s.
    /// </remarks>
    /// <param name="askedMetresPerSecond">
    /// <see cref="PostCutoffSequence.CeilingFor"/>, or NaN where nothing above has an opinion.
    /// </param>
    public static double CeilingFor(double askedMetresPerSecond)
        => askedMetresPerSecond > 0.0 && double.IsFinite(askedMetresPerSecond)
               ? Math.Max(askedMetresPerSecond, MaxMetresPerSecond)
               : MaxMetresPerSecond;

    /// <summary>
    /// What fraction of the bus's own best acceleration a direction must still be producing to count
    /// as connected to something.
    ///
    /// <para>Low: a live thruster reads its full authority and a dead one noise about zero.</para>
    /// </summary>
    public const double AliveFraction = 0.25;

    /// <summary>How long one direction may fire without moving its own component before it is struck off.</summary>
    public const double DirectionStallSeconds = 4.0;

    /// <summary>
    /// How long the whole loop may run without closing before it gives up.
    ///
    /// <para>Longer than <see cref="DirectionStallSeconds"/>, or a bus with no lateral authority gives up
    /// before the dead direction is struck off.</para>
    /// </summary>
    public const double StallSeconds = 10.0;

    /// <summary>What counts as having moved the number.</summary>
    public const double ProgressMetresPerSecond = 0.01;

    /// <summary>
    /// How many of the bus's own pulses wide the floor a pulse phase stops inside is.
    ///
    /// <para>Three, so it stops where one more could overshoot: 0.0017 m/s on the shipped bus against a
    /// hold's 0.02. <c>docs/ACCURACY-PLAN.md</c> 3cu.</para>
    /// </summary>
    public const double PulseFloorPulses = 3.0;

    /// <summary>
    /// The largest error a pulse phase may be entered on, at the threshold a hold stops at.
    ///
    /// <para>The hold's stop rule read as a length: a hold ends with all three components inside the
    /// band, at most <c>√3</c> bands. It refuses an error standing on an axis the loop cannot fire, which
    /// keying on the largest <em>available</em> component would pulse past. <c>docs/ACCURACY-PLAN.md</c> 3cu.</para>
    /// </summary>
    public static double PulseEntry(double band) => Math.Sqrt(3.0) * band;

    /// <summary>
    /// How long one null may spend pulsing before it finishes as a hold would.
    ///
    /// <para>A converging phase needs at most 14.7 s on the shipped bus. What it bounds is a reference
    /// drifting faster than pulsing's 0.0037 m/s² can null while <see cref="Stalled"/> still reads
    /// progress.</para>
    /// </summary>
    public const double PulseSecondsPerNull = 20.0;

    /// <summary>
    /// The whole budget. Past this the warheads go untrimmed.
    ///
    /// <para>Generous: the release window is the real clock, and this is the backstop for a limit cycle.</para>
    /// </summary>
    public const double MaxSeconds = 120.0;

    /// <summary>
    /// The longest step the trim can be integrated across without leaving velocity on the bus.
    ///
    /// <para><b>The trim's precision is linear in the step</b> (<see cref="StopBand"/>): measured headlessly,
    /// 0.118 m/s left at 33 ms, 0.245 at 66, 0.420 at 108, 0.750 at 200.</para>
    ///
    /// <para><b>Recorded, not demanded.</b> Asked of <see cref="WarpPolicy"/>, the world would not run
    /// slow enough and the burns were abandoned, landing 182 to 316 km short. Nothing may turn this into a
    /// demand that can lose a burn.</para>
    /// </summary>
    public const double MaxFaithfulStep = 0.066;

    /// <summary>
    /// How much of each measurement is taken. Low, because the quantity is a difference of two
    /// velocities one frame apart and the frame pacing beats.
    /// </summary>
    public const double AccelerationGain = 0.25;

    private bool _armed;
    private bool _done;
    private bool _gaveUp;
    private double _since;
    private double _accel;
    private double _spent;
    private double3 _velocityPrev;
    private bool _havePrev;
    private int _firingFor;
    private TrimAxes _fire;
    private TrimAxes _dead;

    // Whether the pulse phase has already given way to holding on this null. One-shot: a second stall
    // is the hold's own and ends the null, so this cannot become a wait that never ends.
    private bool _gaveWayToHolding;

    // Whether the last command was a pulse: an acceleration measured across one reads a fraction of
    // the truth.
    private bool _pulsedLast;

    // The frame before that: a command reaches the engine's worker one frame after it is written.
    private bool _pulsedBefore;

    // How long this null has spent pulsing, against PulseSecondsPerNull.
    private double _pulsingFor;

    // What this null's pulses asked for and what arrived, to tell a drifting reference from pulses
    // that do not deliver (docs/ACCURACY-PLAN.md 3ey). Per pulse that felt like one, not per command:
    // the engine grants one pulse per its own allowance however often it is asked.
    private double _pulseSeconds;
    private double _pulseAsked;
    private double _pulseGot;
    private int _pulses;
    private int _pulsesFelt;

    // Which way the chosen direction pushes and what was measured along it: a dead thruster from a
    // live one losing a race.
    private double3 _pushDirCci = Vec.Zero;

    // The direction in force across an interval is the one chosen two frames before its end.
    private double3 _pushDirLast = Vec.Zero;
    private double3 _pushDirBefore = Vec.Zero;

    // How squarely delivered pulses landed on their direction: the delivery reading alone is a
    // magnitude. docs/ACCURACY-PLAN.md 3fd.
    private double _pulseAlong;
    private double _pushed;
    private double _bestAccel;
    private TrimAxes _watching;
    private double _watchedFrom;
    private double _watchingFor;
    private double _toGain = double.NaN;

    // The debt as a vector, so a caller can say what it is made of. docs/ACCURACY-PLAN.md 3fe.
    private double3 _toGainCci;
    private bool _watched;
    private double _atRelease = double.NaN;
    private double _lowest = double.PositiveInfinity;
    private double _sinceProgress;
    private string _said = "";

    /// <summary>Whether it has been told to start. Nothing is commanded until it has.</summary>
    public bool Armed => _armed;

    /// <summary>Whether the warheads may go — either trimmed, or given up on.</summary>
    public bool Done => _done;

    /// <summary>Whether it stopped because it could not do the job rather than because it had.</summary>
    public bool GaveUp => _gaveUp;

    /// <summary>Velocity still to trim off, or NaN before anything has been solved.</summary>
    public double ToGainMetresPerSecond => _toGain;

    /// <summary>The same debt as a vector, for a caller that has to say which way it points.</summary>
    public double3 ToGainCci => _toGainCci;

    /// <summary>
    /// What it owed the moment it was first allowed to push, or NaN if it was never held.
    ///
    /// <para>Beside <see cref="ToGainMetresPerSecond"/> at the split: unchanged, the wait was harmless.</para>
    /// </summary>
    public double AtReleaseMetresPerSecond => _atRelease;

    /// <summary>What the thrusters were measured doing, or zero before they have been fired.</summary>
    public double Acceleration => _accel;

    /// <summary>
    /// What the thrusters have taken out of the tank since the trim was first armed, in metres per
    /// second.
    ///
    /// <para>Across every null, since a tank does not refill on <see cref="Resume"/>; what
    /// <see cref="PostBoostAim.MaxTrimMetresPerSecond"/> budgets against. Off the measured acceleration,
    /// not a declared thrust.</para>
    /// </summary>
    public double SpentMetresPerSecond => _spent;

    /// <summary>
    /// Whether a budget for the whole flight still leaves room to trim. Negative is unlimited.
    ///
    /// <para>Asked of the trim: <see cref="SpentMetresPerSecond"/> is cumulative, and a caller's own
    /// running total of it double-counts.</para>
    /// </summary>
    public bool WithinBudget(double metresPerSecond)
        => metresPerSecond < 0.0 || _spent < metresPerSecond;

    /// <summary>What it is doing or waiting for, for the one line the log prints per change.</summary>
    public string Said => _said;

    /// <summary>Which directions are being fired this cycle. None once it is finished.</summary>
    public TrimAxes Firing => _fire;

    /// <summary>
    /// Start trimming. Called once the split has landed, because the error it exists to remove
    /// arrives with the split.
    /// </summary>
    public void Begin()
    {
        if (_armed) return;
        _armed = true;
        _since = 0.0;
    }

    /// <summary>
    /// Re-arm onto a fresh reference, keeping what has been learned about the vehicle.
    ///
    /// <para>The measured acceleration is the bus's and is kept; what records how <em>this</em> null went
    /// (stalls, dead axes, the low-water mark) is cleared.</para>
    /// </summary>
    public void Resume()
    {
        _armed = true;
        ForgetThisNull();
    }

    public void Reset()
    {
        _armed = false;
        ForgetThisNull();
        _accel = 0.0;
        _spent = 0.0;
        _velocityPrev = Vec.Zero;
        _havePrev = false;
        _toGain = double.NaN;
        _toGainCci = Vec.Zero;
        _atRelease = double.NaN;
        _said = "";
    }

    // What records how one null went. Resume keeps the rest, which is about the bus.
    private void ForgetThisNull()
    {
        _done = false;
        _gaveUp = false;
        _since = 0.0;
        _firingFor = 0;
        _fire = TrimAxes.None;
        _gaveWayToHolding = false;
        _pulsedLast = false;
        _pulsedBefore = false;
        _pulsingFor = 0.0;
        _pulsesFelt = 0;
        _pulseAsked = 0.0;
        _pulseGot = 0.0;
        _pulses = 0;
        _dead = TrimAxes.None;
        _pushDirCci = Vec.Zero;
        _pushDirLast = Vec.Zero;
        _pushDirBefore = Vec.Zero;
        _pulseAlong = 0.0;
        _pushed = 0.0;
        _watching = TrimAxes.None;
        _watchedFrom = double.NaN;
        _watchingFor = 0.0;
        _watched = false;
        _lowest = double.PositiveInfinity;
        _sinceProgress = 0.0;
    }

    public TrimCommand Update(double stepSeconds, in TrimSituation now)
    {
        double step = double.IsFinite(stepSeconds) && stepSeconds > 0.0 ? stepSeconds : 0.0;

        // Neither wipes what it last said: a finished trim is read for its residual and for why
        // it stopped, and both are gone the moment the frame after it overwrites them.
        if (!_armed || _done) return new TrimCommand(TrimAxes.None, _done, _toGain, _accel, _said);

        if (now.MayFire) _since += step;

        // Measured before this cycle's command is chosen, so it describes the interval the last was in force
        // for; proper acceleration, the velocity change less what gravity did.
        Measure(step, in now);

        // Without the trajectory the burn was flown to there is nothing to trim towards: a refusal, not a wait.
        if (now.ReferenceVelocityCci.Equals(Vec.Zero) || !Vec.IsFinite(now.ReferenceVelocityCci))
        {
            return Finish(gaveUp: true, "no cutoff solution to trim against");
        }

        if (_since >= (now.MaxSeconds > 0.0 && double.IsFinite(now.MaxSeconds) ? now.MaxSeconds : MaxSeconds))
        {
            return Finish(gaveUp: true, Left("the trim ran out of time"));
        }

        // Finished rather than held: a spent tank never lifts, and an untrimmed salvo beats one still aboard.
        if (!WithinBudget(now.BudgetMetresPerSecond))
        {
            return Finish(gaveUp: true, Left("the trim has spent its budget"));
        }

        // Transient right after a split, while the part tree and the orbit are rebuilt; the budget bounds the wait.
        if (!UsableFrame(in now)) return Command(TrimAxes.None, "waiting for the bus's control frame");

        if (!TrySolve(in now, out double3 toGainCci))
        {
            return Command(TrimAxes.None, "waiting for the cutoff trajectory to propagate");
        }

        // A command reaches the engine's worker the frame after it is written, so last frame's hold still owes a
        // frame: chosen without it the overshoot leaves the band on the far side, 4-18 m/s a pass at 88 ms steps.
        if (now.CountsTheCommandInFlight && _fire != TrimAxes.None && !_pulsedLast
            && !_pushDirLast.Equals(Vec.Zero) && _accel > 0.0 && double.IsFinite(_accel))
        {
            toGainCci -= Vec.Unit(_pushDirLast) * (_accel * step);
        }

        _toGain = Vec.Len(toGainCci);
        _toGainCci = toGainCci;

        // Watching rather than working: no clock runs and no budget is spent on a wait the trim does not control.
        if (!now.MayFire)
        {
            _watched = true;
            return Command(TrimAxes.None, $"holding, {_toGain:F2} m/s off the solution");
        }

        // Separates an error already there at the split from one that grew during the wait.
        if (_watched && !double.IsFinite(_atRelease))
        {
            _atRelease = _toGain;
        }

        // Stop when less than one more frame of firing would remove: a band below that nothing can reach.
        double band = StopBand(_accel, step);

        // Off the same call the release floor is priced from; the band itself for a bus that does not pulse.
        _pulseSeconds = now.PulseSeconds;
        double stop = StopBand(_accel, step, now.PulseSeconds);
        double fine = stop < band ? stop : 0.0;

        double ceiling = CeilingFor(now.MaxMetresPerSecond);

        if (_toGain > ceiling)
        {
            return Finish(gaveUp: true, Left($"more than the {ceiling:F0} m/s this pass may spend"));
        }

        // Whether this null may work below the band: inside PulseEntry, and the phase still closing.
        bool refine = fine > 0.0
                      && !_gaveWayToHolding
                      && _toGain <= PulseEntry(band)
                      && _pulsingFor < PulseSecondsPerNull;

        // Chosen against the threshold the loop is working to, or a refused phase taps side jets with 2.54 m/s
        // on the tail.
        TrimAxes pick = Choose(in now, toGainCci, refine ? stop : band,
                               out double component, out bool withheld);

        // A component outside the band is a hold's work: one frame of jets removes what a hundred pulses would.
        bool pulse = refine && pick != TrimAxes.None && component <= band;

        // Held off by the interlock, answered before the stall clock: a trim not allowed to push has neither
        // settled nor stalled. MaxSeconds and the budget bound the wait.
        if (pick == TrimAxes.None && withheld)
        {
            return Command(TrimAxes.None,
                           $"holding off the spent stack, {_toGain:F2} m/s off the solution");
        }

        // With a fixed arrival a lateral error the bus cannot null grows a fresh axial requirement every cycle,
        // so a loop that stops helping is stopped. A pulse phase is judged at its own scale.
        if (Stalled(step, pulse ? 0.25 * fine : ProgressMetresPerSecond))
        {
            // A hold has about 150 times a pulse phase's authority, and StallSeconds is shorter than
            // PulseSecondsPerNull, so a stalled phase falls back to holding here (ACCURACY-PLAN 3fd), on a fresh clock.
            if (now.StallFallsBackToHolding && _pulsingFor > 0.0 && !_gaveWayToHolding)
            {
                _gaveWayToHolding = true;
                Restart();

                return Command(TrimAxes.None,
                               $"the pulse phase stopped closing at {_toGain:F3} m/s -- holding instead");
            }

            return Finish(gaveUp: true, Left("the trim stopped closing" + PulseDelivery()));
        }

        // Per direction rather than on the total, which can sit outside the band as a length with every axis inside.
        if (pick == TrimAxes.None)
        {
            if (_since < SettleSeconds) return Command(TrimAxes.None, "settling before the first release");

            // Success or defeat by why nothing was picked: a direction is struck off only after firing without moving
            // its component.
            return _dead == TrimAxes.None
                       ? Finish(gaveUp: false, $"trimmed to {_toGain:F3} m/s")
                       : Finish(gaveUp: true,
                                Left($"nothing left aboard moves the bus (struck off: {Struck(_dead)})"));
        }

        // Not while pulsing: a null that pulses from its first frame never measures, so every axis would read dead.
        if (!pulse) Watch(step, pick, component);

        if (pulse)
        {
            _pulsingFor += step;
            _pulses++;
        }

        _fire = pick;
        _firingFor++;

        return Command(pick,
                       pulse
                           ? $"pulsing {_toGain:F3} m/s off {Name(pick)}"
                           : $"trimming {_toGain:F2} m/s on {Name(pick)}",
                       pulse);
    }

    // The largest remaining component of the directions not struck off.
    /// <param name="band">
    /// The stop threshold. A component already inside it is one more frame of firing away from
    /// being made worse, so it is not a candidate — which is also what makes "nothing to push"
    /// distinguishable from "nothing that pushes".
    /// </param>
    /// <param name="withheld">
    /// True when a direction worth firing was refused by the keep-out interlock. It is what
    /// separates "nothing left to push" from "not allowed to push it", and those two have opposite
    /// answers: the first is the trim finishing, the second is the trim waiting.
    /// </param>
    private TrimAxes Choose(in TrimSituation now, double3 toGainCci, double band,
                            out double component, out bool withheld)
    {
        double3 nose = Vec.Unit(now.NoseCci);
        double3 right = Vec.Unit(now.RightCci);
        double3 down = Vec.Unit(now.DownCci);

        // Copied out because `now` is an `in` parameter and the local function below closes over it.
        double3 toward = KeepOutDirection(in now);

        double along = Vec.Dot(toGainCci, nose);
        double across = Vec.Dot(toGainCci, right);
        double under = Vec.Dot(toGainCci, down);

        TrimAxes best = TrimAxes.None;
        double bestSize = band;
        double3 bestPush = Vec.Zero;
        bool refused = false;

        Consider(along >= 0.0 ? TrimAxes.Forward : TrimAxes.Backward, Math.Abs(along),
                 along >= 0.0 ? nose : -nose);
        Consider(across >= 0.0 ? TrimAxes.Right : TrimAxes.Left, Math.Abs(across),
                 across >= 0.0 ? right : -right);
        Consider(under >= 0.0 ? TrimAxes.Down : TrimAxes.Up, Math.Abs(under),
                 under >= 0.0 ? down : -down);

        component = best == TrimAxes.None ? 0.0 : bestSize;
        _pushDirCci = bestPush;
        withheld = refused;
        return best;

        void Consider(TrimAxes direction, double size, double3 pushes)
        {
            if ((_dead & direction) != TrimAxes.None) return;
            if (!(size > bestSize)) return;

            // Withheld rather than struck off. The axis is healthy and the geometry is temporary,
            // so nothing about it is remembered past this frame.
            if (Vec.Dot(pushes, toward) > 0.0)
            {
                refused = true;
                return;
            }

            best = direction;
            bestSize = size;
            bestPush = pushes;
        }
    }

    // Which way the stack lies, or zero for no constraint: the test is a strict inequality, so zero costs
    // nothing and a push exactly square to the stage is allowed.
    private static double3 KeepOutDirection(in TrimSituation now)
    {
        double3 toward = now.KeepOutTowardCci;

        return Vec.IsFinite(toward) && !toward.Equals(Vec.Zero) ? Vec.Unit(toward) : Vec.Zero;
    }

    // Give the progress watch a fresh run. Only for a change of regime: a loop that restarts its own
    // clock while nothing else changed cannot stall at all.
    private void Restart()
    {
        _lowest = _toGain;
        _sinceProgress = 0.0;
    }

    // Against the lowest ever reached: a bang-bang loop overshoots by a quantum and comes back.
    private bool Stalled(double step, double progress)
    {
        if (_toGain <= _lowest - progress)
        {
            _lowest = _toGain;
            _sinceProgress = 0.0;
            return false;
        }

        _sinceProgress += step;
        return _sinceProgress >= StallSeconds;
    }

    // Whether the direction being fired is doing anything, against the best acceleration this bus has
    // shown; before anything is measured nothing is struck off.
    private bool Alive()
        => _bestAccel > 0.0 && _pushed > AliveFraction * _bestAccel;

    // Struck off rather than given up on: a bus with only an axial pair still has the axial error to remove.
    private void Watch(double step, TrimAxes pick, double component)
    {
        if (pick != _watching)
        {
            _watching = pick;
            _watchedFrom = component;
            _watchingFor = 0.0;
            return;
        }

        _watchingFor += step;

        if (component <= _watchedFrom - ProgressMetresPerSecond)
        {
            _watchedFrom = component;
            _watchingFor = 0.0;
            return;
        }

        if (_watchingFor < DirectionStallSeconds) return;

        // A component not falling is a dead thruster or a live one losing a race to a moving reference; striking
        // off the second abandons a working axis (2.02 m/s left on one, measured). Proper acceleration along the
        // direction carries none of the reference's motion, so it tells them apart.
        if (Alive())
        {
            // Losing a race: whether it is winnable is the overall stall rule's question.
            _watchedFrom = component;
            _watchingFor = 0.0;
            return;
        }

        // Narrowing the search is progress, so the overall clock starts again.
        _dead |= pick;
        _sinceProgress = 0.0;
        _watching = TrimAxes.None;
        _watchedFrom = double.NaN;
        _watchingFor = 0.0;
    }

    // What a give-up costs, in the units the miss is made of.
    private string Left(string why)
        => double.IsFinite(_toGain) ? $"{why}, {_toGain:F2} m/s left on the bus" : $"{why}, nothing solved";

    // Three axes that are actually a frame: a part tree mid-rebuild gives back zeroes.
    private static bool UsableFrame(in TrimSituation now)
        => Vec.IsFinite(now.NoseCci) && Vec.IsFinite(now.RightCci) && Vec.IsFinite(now.DownCci)
        && !Vec.Unit(now.NoseCci).Equals(Vec.Zero)
        && !Vec.Unit(now.RightCci).Equals(Vec.Zero)
        && !Vec.Unit(now.DownCci).Equals(Vec.Zero);

    // What the bus owes the trajectory the guidance flew to, propagated, rather than a fresh transfer, whose
    // demand moves about 20 m/s per second of arrival slip on a deorbit and so depends on when it is asked.
    // Velocity and not position: a shove's displacement is tens of metres, its velocity kilometres of miss.
    private static bool TrySolve(in TrimSituation now, out double3 toGainCci)
    {
        toGainCci = Vec.Zero;

        if (!now.Body.IsUsable) return false;
        if (!Vec.IsFinite(now.VelocityCci)) return false;
        if (!Vec.IsFinite(now.ReferencePositionCci) || !Vec.IsFinite(now.ReferenceVelocityCci)) return false;
        if (now.ReferenceVelocityCci.Equals(Vec.Zero)) return false;
        if (!(now.SecondsSinceReference >= 0.0)) return false;

        if (!Kepler.TryCoast(now.Body.Mu, now.ReferencePositionCci, now.ReferenceVelocityCci,
                             now.SecondsSinceReference, out _, out double3 shouldBeDoing))
        {
            return false;
        }

        toGainCci = shouldBeDoing - now.VelocityCci;
        return Vec.IsFinite(toGainCci);
    }

    // Pulses asked for against what arrived, empty for a null that never pulsed: near one, the phase was
    // chasing its reference; far below, the pulses are not arriving.
    private string PulseDelivery()
        => _pulses == 0
               ? ""
               : $" ({_pulses} pulses commanded, {_pulsesFelt} fired"
                 + (_pulsesFelt > 0 && _pulseAsked > 0.0
                        ? $", asking {_pulseAsked * 1000.0:F3} mm/s and getting {_pulseGot * 1000.0:F3}, "
                          + $"{_pulseGot / _pulseAsked:F2}x, "
                          + $"{_pulseAlong / _pulsesFelt:F2} of it along the direction asked)"
                        : " -- none of them delivered)");

    // Only across an interval one command was in force for the whole of: a change lands a frame late.
    private void Measure(double step, in TrimSituation now)
    {
        if (step <= 0.0 || !Vec.IsFinite(now.VelocityCci))
        {
            _havePrev = false;
            return;
        }

        if (_havePrev && _fire != TrimAxes.None && _pulsedLast && _pulsedBefore)
        {
            // What the pulse delivered, kept out of _accel like every pulsed frame.
            double3 pulled = now.Body.GravityCci(now.PositionCci);
            double got = Vec.Len(((now.VelocityCci - _velocityPrev) / step) - pulled) * step;

            // A frame the engine granted nothing on is neither asked nor got: a tenth of a pulse is the floor.
            if (double.IsFinite(got) && got > 0.1 * _accel * _pulseSeconds)
            {
                _pulseGot += got;
                _pulseAsked += _accel * _pulseSeconds;
                _pulsesFelt++;

                if (!_pushDirBefore.Equals(Vec.Zero) && got > 0.0)
                {
                    double along = Vec.Dot(((now.VelocityCci - _velocityPrev) / step) - pulled,
                                           Vec.Unit(_pushDirBefore)) * step / got;
                    if (double.IsFinite(along)) _pulseAlong += along;
                }
            }
        }

        if (_havePrev && _fire != TrimAxes.None && _firingFor >= 2 && !_pulsedLast && !_pulsedBefore)
        {
            double3 gravity = now.Body.GravityCci(now.PositionCci);
            double3 proper = (now.VelocityCci - _velocityPrev) / step - gravity;
            double measured = Vec.Len(proper);

            if (double.IsFinite(measured) && measured > 0.0)
            {
                _accel = _accel > 0.0 ? _accel + (measured - _accel) * AccelerationGain : measured;
                _bestAccel = Math.Max(_bestAccel, _accel);
            }

            // Along the direction pushed, which carries none of the reference's motion.
            if (!_pushDirCci.Equals(Vec.Zero))
            {
                double along = Vec.Dot(proper, Vec.Unit(_pushDirCci));
                if (double.IsFinite(along)) _pushed = _pushed + (along - _pushed) * AccelerationGain;
            }
        }

        // Charged across the interval the command was in force for, burn good or wasted. A pulse is charged at one
        // per frame, the most the engine delivers and about seven times what it does.
        if (_fire != TrimAxes.None) _spent += _accel * (_pulsedLast ? now.PulseSeconds : step);

        _velocityPrev = now.VelocityCci;
        _havePrev = true;
    }

    private TrimCommand Finish(bool gaveUp, string said)
    {
        _done = true;
        _gaveUp = gaveUp;
        _fire = TrimAxes.None;
        _firingFor = 0;
        _said = said;

        return new TrimCommand(TrimAxes.None, Done: true, _toGain, _accel, said);
    }

    private TrimCommand Command(TrimAxes fire, string said = "", bool pulse = false)
    {
        if (fire == TrimAxes.None) _firingFor = 0;
        _fire = fire;
        _said = said;
        _pushDirBefore = _pushDirLast;
        _pushDirLast = _pushDirCci;

        _pulsedBefore = _pulsedLast;
        _pulsedLast = pulse && fire != TrimAxes.None;

        return new TrimCommand(fire, _done, _toGain, _accel, said, _pulsedLast);
    }

    // Which directions were struck off: a bus that cannot push that way against a solution outrunning it.
    private static string Struck(TrimAxes dead)
    {
        List<string> named = [];
        foreach (TrimAxes d in new[] { TrimAxes.Forward, TrimAxes.Backward, TrimAxes.Right,
                                       TrimAxes.Left, TrimAxes.Down, TrimAxes.Up })
        {
            if ((dead & d) != TrimAxes.None) named.Add(Name(d));
        }

        return named.Count > 0 ? string.Join(", ", named) : "nothing";
    }

    private static string Name(TrimAxes direction) => direction switch
    {
        TrimAxes.Forward => "the nose",
        TrimAxes.Backward => "the tail",
        TrimAxes.Right => "starboard",
        TrimAxes.Left => "port",
        TrimAxes.Down => "the belly",
        TrimAxes.Up => "the back",
        _ => "nothing",
    };
}
