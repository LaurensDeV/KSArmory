using Brutal.Numerics;

namespace KSArmory;

/// <summary>What the launcher is doing this frame, as the sequencer needs to see it.</summary>
/// <param name="NextTube">Which tube fires next, or -1 when nothing more will be handed out.</param>
/// <param name="TubesLeft">How many rounds could still go, which sets each one's share of the window.</param>
/// <param name="NextTubeAxisCci">Where that tube points now, measured this frame.</param>
/// <param name="SweepMetresPerSecond">How fast the vehicle's own turn is sweeping the tubes.</param>
/// <param name="EjectionMetresPerSecond">
/// What the tubes throw a round at, which is what turns a cant into the currency a release is
/// decided in: a tube <c>θ</c> off the line throws its round <c>2·sin(θ/2)</c> of this away from
/// where it was aimed.
///
/// <para>Carried rather than assumed, because it belongs to the munition and two canted launchers
/// need not agree about it. Zero or less prices the cant at nothing and spends only the sweep,
/// which is the honest answer for a store that is dropped rather than thrown.</para>
/// </param>
/// <param name="SecondsLeftToDeploy">
/// How long the release window has left. NaN when unknown, which is treated as "plenty" — a
/// sequencer that stops re-pointing because it cannot see the clock would never re-point at all.
/// </param>
internal readonly record struct ReleaseSituation(
    bool ReadyToDeploy,
    int NextTube,
    int TubesLeft,
    double3 NextTubeAxisCci,
    double SweepMetresPerSecond,
    double EjectionMetresPerSecond,
    double SecondsLeftToDeploy,
    double3 HeldDirectionCci,
    double3 HeldRollCci);

/// <summary>What to hold and whether to let one go.</summary>
internal readonly record struct ReleaseCommand(
    double3 DirectionCci,
    double3 RollCci,
    bool ReleaseNow,
    int Tube,
    double OffLineDegrees,
    string Said);

/// <summary>
/// Letting a magazine go along one line.
///
/// <para>The bus holds one attitude through its coast and the tubes are canted off it, so warheads
/// released from that attitude leave on six different vectors and scatter. Once <see cref="Begin"/>
/// has latched the axes this turns the bus by one cant before each release so the tube about to fire
/// lies on the mean — see <see cref="ReleasePointing"/> — waits for it to settle, releases, and moves
/// on. One at a time, because each tube wants a different attitude.</para>
///
/// <para><b>What decides a release is one number.</b> Both things held against it — how far off the
/// line the tube is, and how fast the vehicle is sweeping it — are lateral velocity put on the
/// round, so they are added and spent against <see cref="LateralBudgetMetresPerSecond"/> rather than
/// tested separately. Two independent thresholds compare nothing: a tube on the line while the
/// vehicle sweeps is a <em>smaller</em> error than one canted while it is still, and a pair of gates
/// refuses the first and accepts the second.</para>
///
/// <para><b>Where the budget cannot be met, the best the vehicle will give is taken.</b> A separated
/// bus that cannot null its residual rate has a sweep no waiting improves, so the only term left is
/// the pointing and the release goes at the pointing's own best — see
/// <see cref="StoppedImprovingSeconds"/>. A vehicle that is still settling keeps improving one term
/// or the other and is never offered that, which is what stops a rigid stack releasing mid-swing.</para>
///
/// <para><b>Nothing paces a salvo it is not re-pointing.</b> With no axes latched every tube wants
/// the same attitude, the only gate left is that the vehicle is steady, and a magazine empties in
/// consecutive frames. That is the intent rather than an oversight: warheads off one release state
/// share a time of flight and land together, where a paced salvo gives each of them a different one
/// — and on a bus falling toward its release altitude that differential is a larger error than
/// anything the pause would buy. <c>docs/ICBM-GUIDANCE.md</c> has the flown numbers.</para>
///
/// <para><b>It gives up rather than holding warheads.</b> A bus that cannot point, one that will not
/// settle, or one running out of window releases on the nominal line anyway: a scattered salvo beats
/// one still aboard when the release altitude closes, because the shot is already paid for.</para>
/// </summary>
internal sealed class ReleaseSequence
{
    /// <summary>
    /// How much lateral velocity a release may put on a round, in metres a second at the tube.
    ///
    /// <para>The whole of it: the vehicle's sweep and the tube's cant are the same quantity and are
    /// added before the comparison. On a deorbit's ~3,400 m per m/s it is about 170 m of spread, and
    /// it is what the flown salvos were released inside.</para>
    ///
    /// <para><b>At the tube, not at the hull.</b> A round on top of a long stack is tens of metres
    /// from the centre of mass, so one degree a second there is half a metre a second at the tube.
    /// This is the quantity that actually reaches the round and it does not care how long the
    /// vehicle is.</para>
    /// </summary>
    public const double LateralBudgetMetresPerSecond = 0.05;

    /// <summary>
    /// The same budget, asked of the sweep alone.
    ///
    /// <para>What a caller deciding when the tube axes may be latched has to ask: the reference is
    /// the attitude the aim correction converged against, so it can only be measured on a vehicle
    /// that has stopped turning. The cant is not part of that question — at the nominal attitude
    /// every tube is canted, which is the whole reason the sequence exists.</para>
    /// </summary>
    public const double SteadyMetresPerSecond = LateralBudgetMetresPerSecond;

    /// <summary>
    /// How long a term has to go without getting better before it is read as this vehicle's floor
    /// rather than as a transient.
    ///
    /// <para>This is what separates a bus that cannot hold from a stack that has not finished
    /// settling, and no single frame can tell them apart. A settling vehicle improves one term or
    /// the other every few tenths of a second — an exponential approach on a twenty-second time
    /// constant is still closing by <see cref="SweepClosingMetresPerSecond"/> inside three seconds
    /// until it is well within the budget — so patience costs it nothing and buys the
    /// distinction.</para>
    /// </summary>
    public const double StoppedImprovingSeconds = 3.0;

    /// <summary>
    /// How much lower the sweep must get to count as still coming down.
    ///
    /// <para>A tenth of the budget: small enough that a vehicle slowly braking its own rotation
    /// keeps resetting the clock above — that is the vehicle which must not be released mid-settle —
    /// and large enough that noise on a floor does not.</para>
    /// </summary>
    public const double SweepClosingMetresPerSecond = 0.005;

    /// <summary>
    /// How long to wait for one tube before letting it go anyway.
    ///
    /// <para>A vehicle with no attitude authority would otherwise hold its rounds for ever, which is
    /// the worse failure of the two.</para>
    /// </summary>
    public const double PerTubeTimeoutSeconds = 60.0;

    /// <summary>
    /// Below this much window per remaining warhead, stop turning and just get them away.
    ///
    /// <para>Spread on the ground is a worse shot; rounds still aboard when the release altitude
    /// closes are no shot at all.</para>
    /// </summary>
    public const double NotWorthRepointingBelowSeconds = 5.0;

    /// <summary>
    /// How much further off the line than it started a tube may drift before the turn is read as
    /// unfollowed rather than unfinished.
    ///
    /// <para>The command is a constant — one rotation of the frozen coast attitude, rebuilt from the
    /// same latched axis every frame — so an error that <em>grows</em> is the vehicle failing to hold
    /// what it was given, and no amount of patience fixes that. A degree is a sixth of the bus's cant
    /// and far above the noise on the measurement, and an overshoot cannot trip it: an overshoot goes
    /// past the line, not back past where the turn began.</para>
    /// </summary>
    public const double NotFollowingDegrees = 1.0;

    /// <summary>
    /// How much closer to the line counts as the turn getting somewhere, and how long it may go
    /// without doing so.
    ///
    /// <para>A quarter of a degree in ten seconds is 0.025 deg/s. A turn that slow needs four minutes
    /// for one cant, which is past <see cref="PerTubeTimeoutSeconds"/> anyway — so nothing that could
    /// have finished is given up on, and a bus that is simply not moving is found in ten seconds
    /// rather than sixty.</para>
    ///
    /// <para>Measured in degrees rather than in the budget's currency on purpose: whether the vehicle
    /// is following a rotation it was commanded is a question about the turn, not about what the
    /// release costs the round.</para>
    /// </summary>
    public const double ClosingDegrees = 0.25;

    /// <inheritdoc cref="ClosingDegrees"/>
    public const double NoProgressSeconds = 10.0;

    private double3[] _axes = [];
    private double _waiting;
    private int _tube = -1;
    private bool _gaveUp;
    private bool _wontSettle;
    private double _startedOff = double.NaN;
    private double _bestOff = double.PositiveInfinity;
    private double _sinceClosing;
    private double _bestSweep = double.PositiveInfinity;
    private double _sinceSweepFell;

    /// <summary>Whether the tube axes have been latched and the sequence is running.</summary>
    public bool Begun { get; private set; }

    /// <summary>The line every warhead is being sent along, latched with the axes.</summary>
    public double3 ReferenceCci { get; private set; }

    /// <summary>
    /// What a tube that far off the line throws its round sideways at, in metres a second — the
    /// currency a release is decided in.
    /// </summary>
    public static double LateralFromCant(double offDegrees, double ejectionMetresPerSecond)
        => ejectionMetresPerSecond > 0.0
               ? 2.0 * ejectionMetresPerSecond * Math.Sin(0.5 * offDegrees * Math.PI / 180.0)
               : 0.0;

    /// <summary>
    /// Latch the tube axes and the line they average to.
    ///
    /// <para>Called on the frame the vehicle is first both ready and settled — which is the
    /// attitude the aim correction converged against, and therefore the one the reference has to be
    /// measured at. Everything after is an offset from it.</para>
    /// </summary>
    public bool Begin(ReadOnlySpan<double3> tubeAxesCci)
    {
        if (Begun) return true;
        if (tubeAxesCci.Length == 0) return false;

        double3[] axes = new double3[tubeAxesCci.Length];

        for (int i = 0; i < tubeAxesCci.Length; i++)
        {
            if (!Vec.IsFinite(tubeAxesCci[i])) return false;
            axes[i] = Vec.Unit(tubeAxesCci[i]);
            if (axes[i].Equals(Vec.Zero)) return false;
        }

        double3 reference = ReleasePointing.ReferenceAxis(axes);
        if (!Vec.IsFinite(reference) || reference.Equals(Vec.Zero)) return false;

        _axes = axes;
        ReferenceCci = reference;
        Begun = true;
        return true;
    }

    public void Reset()
    {
        _axes = [];
        ReferenceCci = Vec.Zero;
        Begun = false;
        _waiting = 0.0;
        _tube = -1;
        _gaveUp = false;
        _wontSettle = false;
        ForgetTheTurn();
    }

    public ReleaseCommand Update(double stepSeconds, in ReleaseSituation now)
    {
        ReleaseCommand held = new(now.HeldDirectionCci, now.HeldRollCci, false, now.NextTube, 0.0, "");

        if (!now.ReadyToDeploy || now.NextTube < 0)
        {
            _waiting = 0.0;
            return held;
        }

        // A different tube means the last one went. Its clock starts now, not when the salvo did.
        if (now.NextTube != _tube)
        {
            _tube = now.NextTube;
            _waiting = 0.0;
            ForgetTheTurn();
        }
        else if (stepSeconds > 0.0)
        {
            _waiting += stepSeconds;
        }

        double share = now.TubesLeft > 0 && double.IsFinite(now.SecondsLeftToDeploy)
                           ? now.SecondsLeftToDeploy / now.TubesLeft
                           : double.PositiveInfinity;

        double deadline = Math.Min(PerTubeTimeoutSeconds, share);

        bool turning = Begun
                       && !_gaveUp
                       && now.NextTube >= 0 && now.NextTube < _axes.Length
                       && deadline >= NotWorthRepointingBelowSeconds;

        double3 direction = now.HeldDirectionCci;
        double3 roll = now.HeldRollCci;

        if (turning
            && !ReleasePointing.TryAimTube(_axes[now.NextTube], ReferenceCci,
                                       now.HeldDirectionCci, now.HeldRollCci,
                                       out direction, out roll))
        {
            turning = false;
            direction = now.HeldDirectionCci;
            roll = now.HeldRollCci;
        }

        // An unresolvable axis is not an axis on the line. The angle between a degenerate direction
        // and the reference is zero, so reading it as a measurement releases every warhead the
        // instant the launcher's part tree stops answering.
        bool measured = Begun && Vec.IsFinite(now.NextTubeAxisCci)
                        && !Vec.Unit(now.NextTubeAxisCci).Equals(Vec.Zero);

        double offDegrees = measured
                                ? ReleasePointing.OffReferenceRadians(now.NextTubeAxisCci, ReferenceCci)
                                  * 180.0 / Math.PI
                                : 0.0;

        string abandoned = "";

        if (turning)
        {
            Record(stepSeconds, measured, offDegrees);
            abandoned = WhyTheTurnIsNotWorking(now.NextTube, measured, offDegrees);

            if (abandoned.Length > 0)
            {
                _gaveUp = true;
                turning = false;
                direction = now.HeldDirectionCci;
                roll = now.HeldRollCci;
            }
        }

        // A launcher that is not being re-pointed is canted by design and no waiting changes that,
        // so only a turn in progress spends any of the budget on its pointing.
        bool blind = turning && !measured;
        double cant = turning && measured
                          ? LateralFromCant(offDegrees, now.EjectionMetresPerSecond)
                          : 0.0;
        double lateral = now.SweepMetresPerSecond + cant;

        RecordSweep(stepSeconds, now.SweepMetresPerSecond);

        // Two questions, and both have to answer yes: has nothing better than this sweep turned up
        // in a while, and is the vehicle at that floor now rather than at the top of a swing? A
        // vehicle still oscillating is on the line and still at different instants, so it satisfies
        // both only once it has stopped.
        bool sweepIsAFloor = !(now.SweepMetresPerSecond > _bestSweep + LateralBudgetMetresPerSecond)
                             && (_wontSettle || _sinceSweepFell >= StoppedImprovingSeconds);

        bool pointed = !blind && !(cant > LateralBudgetMetresPerSecond);
        bool withinBudget = !blind && !(lateral > LateralBudgetMetresPerSecond);

        bool turnGotSomewhere = turning && measured && !double.IsNaN(_startedOff)
                                && _bestOff <= _startedOff - ClosingDegrees;

        // The pointing has nothing left to give: it has passed its own best, or it has stopped
        // finding a better one. Nothing left to give is not the same as good — where the sweep is a
        // floor above the budget the pointing is the only term still moving, and this is the moment
        // it is worth the most.
        bool pointingIsDone = !turning
                              || (turnGotSomewhere
                                  && (offDegrees > _bestOff + ClosingDegrees
                                      || _sinceClosing >= StoppedImprovingSeconds));

        bool late = _waiting >= deadline;

        // One tube's whole clock spent above the budget on a sweep that will not come down is the
        // evidence, and it is not worth gathering six times over: every later tube pays the same
        // wait for the same answer, and each of those waits puts the next warhead on a different
        // release state and a different time of flight.
        if ((sweepIsAFloor || late) && now.SweepMetresPerSecond > LateralBudgetMetresPerSecond)
        {
            _wontSettle = true;
        }

        bool best = sweepIsAFloor && pointingIsDone;
        bool go = withinBudget || best || late;

        // Releasing off the line because the clock ran out means the turn failed, and the rest go on
        // the mean axis rather than each spending its own clock discovering the same thing. Releasing
        // at the pointing's own best is the turn working, however far off the line that best is.
        if (go && !pointed && !pointingIsDone) _gaveUp = true;

        string said = abandoned.Length > 0
                          ? abandoned
                          : Say(now, go, withinBudget, best, pointed, blind, measured, offDegrees,
                                lateral);

        return new ReleaseCommand(direction, roll, go, now.NextTube, offDegrees, said);
    }

    private void ForgetTheTurn()
    {
        _startedOff = double.NaN;
        _bestOff = double.PositiveInfinity;
        _sinceClosing = 0.0;
        _bestSweep = double.PositiveInfinity;
        _sinceSweepFell = 0.0;
    }

    private void Record(double stepSeconds, bool measured, double offDegrees)
    {
        if (measured)
        {
            if (double.IsNaN(_startedOff)) _startedOff = offDegrees;

            if (offDegrees < _bestOff - ClosingDegrees)
            {
                _bestOff = offDegrees;
                _sinceClosing = 0.0;
                return;
            }
        }

        if (stepSeconds > 0.0) _sinceClosing += stepSeconds;
    }

    private void RecordSweep(double stepSeconds, double sweepMetresPerSecond)
    {
        if (sweepMetresPerSecond < _bestSweep - SweepClosingMetresPerSecond)
        {
            _bestSweep = sweepMetresPerSecond;
            _sinceSweepFell = 0.0;
            return;
        }

        if (stepSeconds > 0.0) _sinceSweepFell += stepSeconds;
    }

    // Why this turn will not finish, or nothing while it still might. The command is one constant
    // rotation of a frozen attitude, so an error that grows or stops shrinking is the vehicle rather
    // than the arithmetic, and waiting out the rest of the clock costs the salvo its time on target.
    private string WhyTheTurnIsNotWorking(int tube, bool measured, double offDegrees)
    {
        if (measured && !double.IsNaN(_startedOff) && offDegrees > _startedOff + NotFollowingDegrees)
        {
            return $"tube {tube + 1} is not following the turn, {offDegrees:F1} deg off the line "
                   + $"against {_startedOff:F1} when it started - the rest will go on the mean axis";
        }

        if (_sinceClosing < NoProgressSeconds) return "";

        return double.IsFinite(_bestOff)
                   ? $"tube {tube + 1} has stopped closing on the line, {_bestOff:F1} deg after "
                     + $"{NoProgressSeconds:F0} s - the rest will go on the mean axis"
                   : $"tube {tube + 1} axis will not resolve, {NoProgressSeconds:F0} s blind - the "
                     + "rest will go on the mean axis";
    }

    // What this frame did, as the per-tube record a salvo leaves behind. A release says so even when
    // nothing went wrong: six impact points are only diagnosable against the six release states that
    // produced them, and those are gone by the time anything lands. What was spent is quoted against
    // the budget, because the number alone does not say which side of it the release fell.
    private string Say(in ReleaseSituation now, bool go, bool withinBudget, bool best, bool pointed,
                       bool blind, bool measured, double offDegrees, double lateral)
    {
        int tube = now.NextTube + 1;
        string line = measured ? $"{offDegrees:F1} deg off the line and " : "";
        string sweeping = $"tubes sweeping {now.SweepMetresPerSecond:F3} m/s";
        string spent = $"{lateral:F3} m/s at the tube against {LateralBudgetMetresPerSecond:F3} wanted";

        if (!go)
        {
            if (blind) return $"turning onto tube {tube} blind, its axis will not resolve";
            if (!pointed) return $"turning onto tube {tube}, {offDegrees:F1} deg to go";

            return $"settling, {sweeping} - {spent}";
        }

        if (withinBudget) return $"releasing tube {tube}, {line}{sweeping}";

        if (best) return $"releasing tube {tube} on the best it will give, {line}{sweeping} - {spent}";

        if (!pointed)
        {
            return $"releasing tube {tube} off the line, {offDegrees:F1} deg out - the rest will go "
                   + "on the mean axis";
        }

        return $"releasing tube {tube} late, {line}{sweeping} - {spent}, the warheads will scatter";
    }
}
