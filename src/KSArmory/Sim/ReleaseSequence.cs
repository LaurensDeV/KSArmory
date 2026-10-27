using Brutal.Numerics;

namespace KSArmory;

/// <summary>What the launcher is doing this frame, as the sequencer needs to see it.</summary>
/// <param name="NextTube">Which tube fires next, or -1 when nothing more will be handed out.</param>
/// <param name="TubesLeft">
/// How many rounds could still go, which sets each one's share of the window — and, when it reaches
/// zero, ends the deployment. It has to be the true count for that: a floor of one reads as a
/// magazine that never empties.
/// </param>
/// <param name="NextTubeAxisCci">Where that tube points now, measured this frame.</param>
/// <param name="NoseAxisCci">
/// The mean of the live tube axes now, which each turn is applied to rather than the held command
/// (<see cref="ReleasePointing.TryAimTubeFromHere"/>). Zero where the part tree will not answer.
/// </param>
/// <param name="SweepMetresPerSecond">How fast the vehicle's own turn is sweeping the tubes.</param>
/// <param name="EjectionMetresPerSecond">
/// What the tubes throw a round at: a tube <c>θ</c> off the line throws it <c>2·sin(θ/2)</c> of this
/// sideways. Zero or less prices the cant at nothing, for a store that is dropped.
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
    double3 NoseAxisCci,
    double SweepMetresPerSecond,
    double EjectionMetresPerSecond,
    double SecondsLeftToDeploy,
    double3 HeldDirectionCci,
    double3 HeldRollCci);

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
/// <para>Where the tubes are canted, once <see cref="Begin"/> has latched the axes this turns the vehicle
/// before each release so the tube about to fire lies on the mean (<see cref="ReleasePointing"/>),
/// measured from where the launcher is rather than the held command, since KSA leaves roll free.</para>
///
/// <para>The cant and the sweep are both lateral velocity on the round, so they are summed against
/// one <see cref="LateralBudgetMetresPerSecond"/>. With no axes latched nothing paces the salvo and
/// the magazine empties in consecutive frames, on purpose. <c>docs/ICBM-GUIDANCE.md</c>, "The release
/// gate is one budget, not two thresholds" and the two sections after it.</para>
///
/// <para>It gives up rather than holding warheads: a scattered salvo beats one still aboard when the
/// release altitude closes.</para>
/// </summary>
internal sealed class ReleaseSequence
{
    /// <summary>
    /// How much lateral velocity a release may put on a round, in metres a second at the tube.
    ///
    /// <para>Sweep and cant summed. On a deorbit's ~3,400 m per m/s it is about 170 m of spread. At the
    /// tube rather than the hull, because a hull rate's effect grows with the stack's length.</para>
    /// </summary>
    public const double LateralBudgetMetresPerSecond = 0.05;

    /// <summary>
    /// How long a term has to go without getting better before it is read as this vehicle's floor
    /// rather than as a transient.
    ///
    /// <para>Separates a bus that cannot hold from a stack still settling: even a 20 s exponential
    /// approach still closes by <see cref="SweepClosingMetresPerSecond"/> inside this until it is well
    /// within the budget.</para>
    /// </summary>
    public const double StoppedImprovingSeconds = 3.0;

    /// <summary>
    /// How much lower the sweep must get to count as still coming down.
    ///
    /// <para>A tenth of the budget: a vehicle slowly braking its rotation keeps resetting the clock
    /// above, and noise on a floor does not.</para>
    /// </summary>
    public const double SweepClosingMetresPerSecond = 0.005;

    /// <summary>
    /// How steady the tubes must be before <see cref="Begin"/> will latch their axes, in metres a
    /// second at the tube.
    ///
    /// <para>Ten times the release budget: a slightly-off reference biases every tube equally, and what
    /// it must exclude is the transient right after a split (flown at 0.438 m/s), not the jitter a light
    /// bus never nulls. Gated on the release budget, a vehicle whose sweep floors above it never latches.</para>
    /// </summary>
    public const double SteadyToLatchMetresPerSecond = 0.5;

    /// <summary>
    /// How long to wait for one tube before letting it go anyway.
    ///
    /// <para>A vehicle with no attitude authority would otherwise hold its rounds for ever.</para>
    /// </summary>
    public const double PerTubeTimeoutSeconds = 60.0;

    /// <summary>
    /// Below this much window per remaining warhead, stop turning and just get them away.
    /// </summary>
    public const double NotWorthRepointingBelowSeconds = 5.0;

    /// <summary>
    /// How much further off the line than it started a tube may drift before the turn is read as
    /// unfollowed rather than unfinished.
    ///
    /// <para>The command is a constant rotation, so an error that grows is the vehicle not holding it.
    /// A degree is far above the measurement noise, and an overshoot cannot trip it: it goes past the
    /// line, not back past where the turn began.</para>
    /// </summary>
    public const double NotFollowingDegrees = 1.0;

    /// <summary>
    /// How much closer to the line counts as the turn getting somewhere, and how long it may go
    /// without doing so.
    ///
    /// <para>0.025 deg/s: a turn that slow could not finish inside <see cref="PerTubeTimeoutSeconds"/>
    /// anyway, so a bus that is not moving is found in ten seconds rather than sixty.</para>
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
    private bool _hadRounds;

    /// <summary>Whether the tube axes have been latched and the sequence is running.</summary>
    public bool Begun { get; private set; }

    /// <summary>
    /// Whether the magazine it was given has been let go. It does not fill again.
    ///
    /// <para>Latched until <see cref="Reset"/>, because the launcher reloads and a bus deploys once.</para>
    /// </summary>
    public bool Emptied { get; private set; }

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
    /// <para>Called on the frame the vehicle is first both ready and settled: the attitude the aim
    /// correction converged against.</para>
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
        Emptied = false;
        _hadRounds = false;
        _waiting = 0.0;
        _tube = -1;
        _gaveUp = false;
        _wontSettle = false;
        ForgetTheTurn();
    }

    public ReleaseCommand Update(double stepSeconds, in ReleaseSituation now)
    {
        ReleaseCommand held = new(now.HeldDirectionCci, now.HeldRollCci, false, now.NextTube, 0.0, "");

        // Watched rather than counted: a reload is indistinguishable from a fuller magazine.
        if (now.TubesLeft > 0) _hadRounds = true;
        else if (_hadRounds) Emptied = true;

        if (Emptied)
        {
            _waiting = 0.0;
            return new ReleaseCommand(now.HeldDirectionCci, now.HeldRollCci, false, -1, 0.0, "");
        }

        if (!now.ReadyToDeploy || now.NextTube < 0)
        {
            _waiting = 0.0;
            return held;
        }

        // A different tube means the last one went.
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

        // An unresolvable axis is not an axis on the line. The angle between a degenerate direction
        // and the reference is zero, so reading it as a measurement releases every warhead the
        // instant the launcher's part tree stops answering.
        bool measured = Begun && Vec.IsFinite(now.NextTubeAxisCci)
                        && !Vec.Unit(now.NextTubeAxisCci).Equals(Vec.Zero);

        double offDegrees = measured
                                ? ReleasePointing.OffReferenceRadians(now.NextTubeAxisCci, ReferenceCci)
                                  * 180.0 / Math.PI
                                : 0.0;

        double3 direction = now.HeldDirectionCci;
        double3 roll = now.HeldRollCci;

        // The latched form is only the fallback for a part tree that will not answer: it leaves the
        // vehicle's own pointing error and roll in the answer.
        bool fromHere = measured && Vec.IsFinite(now.NoseAxisCci)
                        && !Vec.Unit(now.NoseAxisCci).Equals(Vec.Zero);

        if (turning)
        {
            bool aimed = fromHere
                             ? ReleasePointing.TryAimTubeFromHere(
                                   now.NextTubeAxisCci, now.NoseAxisCci, ReferenceCci,
                                   now.HeldRollCci, out direction, out roll)
                             : ReleasePointing.TryAimTube(
                                   _axes[now.NextTube], ReferenceCci,
                                   now.HeldDirectionCci, now.HeldRollCci,
                                   out direction, out roll);

            if (!aimed)
            {
                turning = false;
                direction = now.HeldDirectionCci;
                roll = now.HeldRollCci;
            }
        }

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

        // Both: nothing better has turned up in a while, and the vehicle is at that floor now rather
        // than at the top of a swing.
        bool sweepIsAFloor = !(now.SweepMetresPerSecond > _bestSweep + LateralBudgetMetresPerSecond)
                             && (_wontSettle || _sinceSweepFell >= StoppedImprovingSeconds);

        bool pointed = !blind && !(cant > LateralBudgetMetresPerSecond);
        bool withinBudget = !blind && !(lateral > LateralBudgetMetresPerSecond);

        bool turnGotSomewhere = turning && measured && !double.IsNaN(_startedOff)
                                && _bestOff <= _startedOff - ClosingDegrees;

        // The pointing has passed its own best or stopped improving; not the same as good, but with the
        // sweep floored above the budget this is the moment it is worth the most.
        bool pointingIsDone = !turning
                              || (turnGotSomewhere
                                  && (offDegrees > _bestOff + ClosingDegrees
                                      || _sinceClosing >= StoppedImprovingSeconds));

        bool late = _waiting >= deadline;

        // Learnt once rather than per tube: each later wait would put the next warhead on a different
        // release state and time of flight.
        if ((sweepIsAFloor || late) && now.SweepMetresPerSecond > LateralBudgetMetresPerSecond)
        {
            _wontSettle = true;
        }

        bool best = sweepIsAFloor && pointingIsDone;
        bool go = withinBudget || best || late;

        // Off the line because the clock ran out is a failed turn, and the rest go on the mean axis;
        // released at the pointing's own best is the turn working.
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

    // Why this turn will not finish, or empty while it still might.
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

    // A release says so even when nothing went wrong: impact points are only diagnosable against the
    // release states that produced them.
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
