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
/// <para>The pair is a <em>trajectory</em>, and it is the one the whole burn was flown to arrive
/// at. Everything the trim does is putting the vehicle back on it.</para>
/// </param>
/// <param name="SecondsSinceReference">How far along that trajectory the vehicle should be by now.</param>
/// <param name="NoseCci">The control frame's +X in the parent body's inertial frame.</param>
/// <param name="RightCci">Its +Y.</param>
/// <param name="DownCci">Its +Z.</param>
/// <param name="MayFire">
/// Whether the vehicle is allowed to push yet. False while it is still coasting clear of whatever
/// it dropped — see <see cref="SeparationClearance"/>.
///
/// <para>It keeps <em>solving</em> either way, and that is the point: what the bus owes its
/// solution is the one number that says whether the wait is harmless, and a loop that only starts
/// looking when it is allowed to act cannot tell an error that was always there from one that grew
/// while it waited.</para>
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
    bool MayFire = true);

/// <summary>What to fire and whether the warheads may go.</summary>
/// <param name="Acceleration">
/// What the thrusters were measured doing, in metres per second squared, or zero before anything
/// has been observed. It is what sets the floor under the residual, so it is reported rather than
/// kept: one frame of firing is <c>acceleration x step</c>, and a residual near that is a timing
/// limit rather than a control error.
/// </param>
internal readonly record struct TrimCommand(
    TrimAxes Fire,
    bool Done,
    double ToGainMetresPerSecond,
    double Acceleration,
    string Said);

/// <summary>
/// The post-boost vehicle's own velocity trim: what its thrusters and its own tank are for.
///
/// <para>The main burn is exact at the instant it ends, and then two things move the bus off the
/// solution it arrived at. The decoupler that lets the launcher off the spent stack is the larger
/// of them — a stock 3 m decoupler declares 7 kN against a six-tonne bus, which is about a metre a
/// second, and it arrives <em>after</em> the last thing that could compensate for it. The other is
/// whatever the cutoff left, which is a frame of the upper stage's thrust. Both are velocity
/// errors on an otherwise perfect arc, and at this trajectory's ~3,400 m of miss per m/s left
/// radially, a metre a second is kilometres.</para>
///
/// <para><b>It is the same loop as the burn, against a different actuator.</b> Take the trajectory
/// the guidance flew to, carry it forward to now, subtract what the vehicle is actually doing, and
/// thrust along the difference until less than one frame of firing is left.</para>
///
/// <para><b>Against the flown solution, never a fresh one.</b> Re-solving a transfer from wherever
/// the bus happens to be looks equivalent and is not: a transfer is parameterised by an arrival
/// time and an aim point, and on a deorbit it demands about twenty metres a second more for every
/// second the arrival is out — so the answer depends on <em>when</em> it is asked, and goes stale
/// while the vehicle coasts clear of what it dropped. Propagating the cutoff state has none of
/// that. A shove of a metre a second reads as a metre a second however long the trim waits.</para>
///
/// <para><b>One direction at a time, and it is not tidiness.</b> The stop threshold is half a
/// frame's worth of thrust, and that number is only knowable along the direction actually being
/// fired: a bus's lateral authority is whatever its nozzle layout happened to give it, which for
/// four clusters arranged for roll and pitch may be nothing at all. Firing one direction means
/// every number in the loop was observed rather than assumed, and a direction that turns out to
/// move nothing is struck off and the next one tried — so a vehicle with only an axial pair still
/// gets the whole axial error out, which is nearly all of it.</para>
///
/// <para><b>It gives up rather than holding warheads</b>, exactly as <see cref="ReleaseSequence"/>
/// does. A bus with no thrusters, or one that has run its tank dry, releases on the trajectory it
/// has: an untrimmed salvo is a worse shot, and one still aboard when the release altitude closes
/// is no shot at all.</para>
/// </summary>
internal sealed class BusTrim
{
    /// <summary>
    /// Close enough to stop, in metres per second.
    ///
    /// <para>Two centimetres a second is about 68 m of miss at this trajectory's radial
    /// sensitivity, which is comfortably under the best a shot has flown without any of this. Below
    /// it the residual stops being what the miss is made of and there is nothing to buy.</para>
    /// </summary>
    public const double SettledMetresPerSecond = 0.02;

    /// <summary>
    /// How long after arming before it may call itself finished.
    ///
    /// <para>The split is deferred through the engine's input buffer and the bus's orbit is
    /// recomputed after it lands, so for the first moment the state being solved against is the one
    /// <em>before</em> the shove. Declaring the trim complete there is declaring it complete on a
    /// problem that has not arrived yet, and nothing afterwards looks again.</para>
    /// </summary>
    public const double SettleSeconds = 1.5;

    /// <summary>
    /// The most this will accept as a trim, in metres per second.
    ///
    /// <para>A separation transient is about a metre a second and a cutoff residual is a fraction
    /// of one. An answer in the tens is not a bus that has been shoved off its arc — it is the solve
    /// being asked the wrong question, and thrusting at it makes the shot worse rather than better
    /// while looking exactly like work.</para>
    ///
    /// <para>Checked every cycle rather than once at the start, because the way it went wrong in
    /// flight was a runaway rather than a bad first answer: the aim correction and the trim drove
    /// the same vehicle through the same prediction, and the pair wound each other up by a factor
    /// of ten every ten cycles.</para>
    /// </summary>
    public const double MaxMetresPerSecond = 10.0;

    /// <summary>How long one direction may fire without moving its own component before it is struck off.</summary>
    public const double DirectionStallSeconds = 4.0;

    /// <summary>
    /// How long the whole loop may run without closing before it gives up.
    ///
    /// <para><b>Longer than <see cref="DirectionStallSeconds"/>, and it has to be.</b> A bus with no
    /// lateral authority spends the first stretch pushing at nothing, so a loop that gave up on the
    /// total over the same span would give up before the direction that does not work has been
    /// struck off — leaving the axial error, which is nearly all of it, untouched.</para>
    /// </summary>
    public const double StallSeconds = 10.0;

    /// <summary>What counts as having moved the number.</summary>
    public const double ProgressMetresPerSecond = 0.01;

    /// <summary>
    /// The whole budget. Past this the warheads go untrimmed.
    ///
    /// <para>Generous, because the release window is the real clock and the sequencer already
    /// watches it — this is only the backstop for a loop that is neither converging nor stalling,
    /// which is a limit cycle around a quantum too coarse to land inside the band.</para>
    /// </summary>
    public const double MaxSeconds = 120.0;

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
    private double3 _velocityPrev;
    private bool _havePrev;
    private int _firingFor;
    private TrimAxes _fire;
    private TrimAxes _dead;
    private TrimAxes _watching;
    private double _watchedFrom;
    private double _watchingFor;
    private double _toGain = double.NaN;
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

    /// <summary>
    /// What it owed the moment it was first allowed to push, or NaN if it was never held.
    ///
    /// <para>Beside <see cref="ToGainMetresPerSecond"/> at the split this is the whole diagnosis of
    /// a wait that costs something: the same number twice means the wait was harmless and the error
    /// came from the separation, and a number that has grown means something moved the vehicle or
    /// the aim while it coasted.</para>
    /// </summary>
    public double AtReleaseMetresPerSecond => _atRelease;

    /// <summary>What the thrusters were measured doing, or zero before they have been fired.</summary>
    public double Acceleration => _accel;

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

    public void Reset()
    {
        _armed = false;
        _done = false;
        _gaveUp = false;
        _since = 0.0;
        _accel = 0.0;
        _velocityPrev = Vec.Zero;
        _havePrev = false;
        _firingFor = 0;
        _fire = TrimAxes.None;
        _dead = TrimAxes.None;
        _watching = TrimAxes.None;
        _watchedFrom = double.NaN;
        _watchingFor = 0.0;
        _toGain = double.NaN;
        _watched = false;
        _atRelease = double.NaN;
        _lowest = double.PositiveInfinity;
        _sinceProgress = 0.0;
        _said = "";
    }

    public TrimCommand Update(double stepSeconds, in TrimSituation now)
    {
        double step = double.IsFinite(stepSeconds) && stepSeconds > 0.0 ? stepSeconds : 0.0;

        // Neither wipes what it last said: a finished trim is read for its residual and for why
        // it stopped, and both are gone the moment the frame after it overwrites them.
        if (!_armed || _done) return new TrimCommand(TrimAxes.None, _done, _toGain, _accel, _said);

        if (now.MayFire) _since += step;

        // Measured before this cycle's command is chosen, so what it describes is the interval the
        // last one was in force for. Proper acceleration: the velocity change less what gravity did
        // over the same interval, which is the only part a thruster is responsible for.
        Measure(step, in now);

        // Nothing to trim *towards*. Without the trajectory the burn was flown to there is no way
        // to tell a vehicle that has been pushed off its solution from one whose solution was
        // always something else, so this is a refusal rather than a wait.
        if (now.ReferenceVelocityCci.Equals(Vec.Zero) || !Vec.IsFinite(now.ReferenceVelocityCci))
        {
            return Finish(gaveUp: true, "no cutoff solution to trim against");
        }

        if (_since >= MaxSeconds)
        {
            return Finish(gaveUp: true, Left("the trim ran out of time"));
        }

        // Both of these are transient right after a split, which is precisely when the trim starts:
        // the part tree is being rebuilt and the new craft's orbit has not been recomputed. Waiting
        // is what changes nothing, and the budget above is what stops it waiting for ever.
        if (!UsableFrame(in now)) return Command(TrimAxes.None, "waiting for the bus's control frame");

        if (!TrySolve(in now, out double3 toGainCci))
        {
            return Command(TrimAxes.None, "waiting for the cutoff trajectory to propagate");
        }

        _toGain = Vec.Len(toGainCci);

        // Watching rather than working. None of the clocks run and nothing is judged: a trim that
        // has not been allowed to push has neither settled nor stalled, and starting its budget
        // here would spend it on a wait it does not control.
        if (!now.MayFire)
        {
            _watched = true;
            return Command(TrimAxes.None, $"holding, {_toGain:F2} m/s off the solution");
        }

        // The first thing it is allowed to say, and the one that separates an error that was
        // already there at the split from one that grew during the wait.
        if (_watched && !double.IsFinite(_atRelease))
        {
            _atRelease = _toGain;
        }

        // The same rule the main burn cuts off on: stop when less than one more frame of firing
        // would remove. A threshold below what a frame adds is a threshold nothing can reach, and
        // the loop hunts round it for as long as it is allowed to.
        double quantum = _accel > 0.0 ? 0.5 * _accel * step : 0.0;
        double band = Math.Max(SettledMetresPerSecond, quantum);

        if (_toGain <= band && _since >= SettleSeconds)
        {
            return Finish(gaveUp: false, $"trimmed to {_toGain:F3} m/s");
        }

        if (_toGain > MaxMetresPerSecond)
        {
            return Finish(gaveUp: true, Left("more than a separation could have cost"));
        }

        // A loop that has stopped helping is not the same as one that has stopped firing, and it is
        // the more expensive of the two: with a fixed arrival, a lateral error the bus cannot null
        // grows a fresh axial requirement every cycle, so the axial jets go on chasing it for as
        // long as they are allowed to. That burns the tank for nothing and leaves the shot no
        // better than when the last real progress was made.
        if (Stalled(step)) return Finish(gaveUp: true, Left("the trim stopped closing"));

        TrimAxes pick = Choose(in now, toGainCci, band, out double component);

        if (pick == TrimAxes.None)
        {
            // Everything worth pushing is on a direction that moves nothing — unless the whole
            // remainder is already inside the band, which is the settle window still running.
            return _toGain <= band
                       ? Command(TrimAxes.None, "settling before the first release")
                       : Finish(gaveUp: true, Left("nothing left aboard moves the bus"));
        }

        Watch(step, pick, component);

        _fire = pick;
        _firingFor++;

        return Command(pick, $"trimming {_toGain:F2} m/s on {Name(pick)}");
    }

    // Which direction to push, out of the ones that have not been struck off. The largest remaining
    // component, because it is the one that most of the miss is made of and the one whose stop
    // threshold the measurement will be good for.
    /// <param name="band">
    /// The stop threshold. A component already inside it is one more frame of firing away from
    /// being made worse, so it is not a candidate — which is also what makes "nothing to push"
    /// distinguishable from "nothing that pushes".
    /// </param>
    private TrimAxes Choose(in TrimSituation now, double3 toGainCci, double band, out double component)
    {
        double along = Vec.Dot(toGainCci, Vec.Unit(now.NoseCci));
        double across = Vec.Dot(toGainCci, Vec.Unit(now.RightCci));
        double under = Vec.Dot(toGainCci, Vec.Unit(now.DownCci));

        TrimAxes best = TrimAxes.None;
        double bestSize = band;

        Consider(along >= 0.0 ? TrimAxes.Forward : TrimAxes.Backward, Math.Abs(along));
        Consider(across >= 0.0 ? TrimAxes.Right : TrimAxes.Left, Math.Abs(across));
        Consider(under >= 0.0 ? TrimAxes.Down : TrimAxes.Up, Math.Abs(under));

        component = best == TrimAxes.None ? 0.0 : bestSize;
        return best;

        void Consider(TrimAxes direction, double size)
        {
            if ((_dead & direction) != TrimAxes.None) return;
            if (!(size > bestSize)) return;

            best = direction;
            bestSize = size;
        }
    }

    // Against the lowest ever reached rather than the last cycle's, because the number wanders: a
    // bang-bang loop overshoots by a quantum and comes back, so "worse than last time" is the
    // ordinary state of a loop that is working.
    private bool Stalled(double step)
    {
        if (_toGain <= _lowest - ProgressMetresPerSecond)
        {
            _lowest = _toGain;
            _sinceProgress = 0.0;
            return false;
        }

        _sinceProgress += step;
        return _sinceProgress >= StallSeconds;
    }

    // A direction that fires for long enough without moving its own component is not connected to
    // anything, and the loop has to find that out rather than assume a layout. Struck off rather
    // than given up on: an axial pair is the one every thruster set has, and a bus with only that
    // still has nearly all of the error to remove.
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

        // Narrowing the search is progress of its own kind, so the overall clock starts again: what
        // the loop does next is a different thing from what it has just found does not work.
        _dead |= pick;
        _sinceProgress = 0.0;
        _watching = TrimAxes.None;
        _watchedFrom = double.NaN;
        _watchingFor = 0.0;
    }

    // What a give-up costs, said in the units the miss is made of. A reason on its own does not
    // tell anyone whether the salvo about to go is a good one, and that is the whole question.
    private string Left(string why)
        => double.IsFinite(_toGain) ? $"{why}, {_toGain:F2} m/s left on the bus" : $"{why}, nothing solved";

    // Three axes that are actually a frame. A part tree mid-rebuild gives back zeroes, and firing
    // on the direction those pick out is firing on nothing at all.
    private static bool UsableFrame(in TrimSituation now)
        => Vec.IsFinite(now.NoseCci) && Vec.IsFinite(now.RightCci) && Vec.IsFinite(now.DownCci)
        && !Vec.Unit(now.NoseCci).Equals(Vec.Zero)
        && !Vec.Unit(now.RightCci).Equals(Vec.Zero)
        && !Vec.Unit(now.DownCci).Equals(Vec.Zero);

    // What the bus owes the trajectory the guidance actually flew to, rather than a fresh transfer
    // solved from where it happens to be.
    //
    // Re-solving looks equivalent and is not. A transfer is parameterised by an arrival time and an
    // aim point, and on a deorbit the velocity it demands moves about twenty metres a second for
    // every second the arrival is out - so a trim built on one is only as good as those two
    // parameters are fresh, and its answer depends on *when* it is asked. Measured in flight at
    // 229 m/s after a wait that cost the vehicle nothing at all.
    //
    // Propagating the guidance's own cutoff state has none of that. It is the same conic the
    // vehicle is supposed to be on, so a shove of a metre a second reads as a metre a second an
    // hour later, and the answer does not care how long the trim took to get asked.
    //
    // It corrects velocity and not position, which is the right half: a shove displaces the vehicle
    // by the shove times the delay - tens of metres over a minute - where the velocity it leaves
    // behind is worth kilometres of miss per metre a second.
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

    // Only across an interval the thrusters were in force for the whole of. A command written this
    // frame is copied into the engine's worker on the next one, so the first interval after a
    // change is a mixture and the estimate it gives is somewhere between the two.
    private void Measure(double step, in TrimSituation now)
    {
        if (step <= 0.0 || !Vec.IsFinite(now.VelocityCci))
        {
            _havePrev = false;
            return;
        }

        if (_havePrev && _fire != TrimAxes.None && _firingFor >= 2)
        {
            double3 gravity = now.Body.GravityCci(now.PositionCci);
            double3 proper = (now.VelocityCci - _velocityPrev) / step - gravity;
            double measured = Vec.Len(proper);

            if (double.IsFinite(measured) && measured > 0.0)
            {
                _accel = _accel > 0.0 ? _accel + (measured - _accel) * AccelerationGain : measured;
            }
        }

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

    private TrimCommand Command(TrimAxes fire, string said = "")
    {
        if (fire == TrimAxes.None) _firingFor = 0;
        _fire = fire;
        _said = said;

        return new TrimCommand(fire, _done, _toGain, _accel, said);
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
