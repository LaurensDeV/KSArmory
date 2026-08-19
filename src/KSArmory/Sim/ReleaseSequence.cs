using Brutal.Numerics;

namespace KSArmory;

/// <summary>What the launcher is doing this frame, as the sequencer needs to see it.</summary>
/// <param name="NextTube">Which tube fires next, or -1 when nothing more will be handed out.</param>
/// <param name="TubesLeft">How many rounds could still go, which sets each one's share of the window.</param>
/// <param name="NextTubeAxisCci">Where that tube points now, measured this frame.</param>
/// <param name="SweepMetresPerSecond">How fast the vehicle's own turn is sweeping the tubes.</param>
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
    /// How near the line a tube must be before its warhead goes.
    ///
    /// <para>Half a degree admits <c>2·sin(0.5°)</c> = 0.017 m/s of lateral velocity, which on a
    /// deorbit's ~3,400 m per m/s is about 59 m — a factor of three under what the sweep gate below
    /// already accepts, so the pointing is not the binding term. Tightening it buys nothing until
    /// that one comes down with it.</para>
    /// </summary>
    public const double AlignedDegrees = 0.5;

    /// <summary>
    /// How fast the tubes may still be sweeping when a warhead goes, in metres a second at the
    /// tube rather than degrees a second at the hull.
    ///
    /// <para>The hull's rate is the wrong measure: a round on top of a long stack is tens of metres
    /// from the centre of mass, so one degree a second there is half a metre a second at the tube.
    /// This is the quantity that actually reaches the round and it does not care how long the
    /// vehicle is.</para>
    /// </summary>
    public const double SteadyMetresPerSecond = 0.05;

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

    /// <summary>Whether the tube axes have been latched and the sequence is running.</summary>
    public bool Begun { get; private set; }

    /// <summary>The line every warhead is being sent along, latched with the axes.</summary>
    public double3 ReferenceCci { get; private set; }

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

        bool onLine = !turning || (measured && offDegrees <= AlignedDegrees);
        bool steady = !(now.SweepMetresPerSecond > SteadyMetresPerSecond);
        bool late = _waiting >= deadline;

        // One tube's whole clock spent not settling is the evidence, and it is not worth gathering
        // six times over: every later tube pays the same wait for the same answer, and each of those
        // waits puts the next warhead on a different release state and a different time of flight.
        // What the gate holds out for is bounded by the sweep; what the waiting costs is not.
        if (late && !steady) _wontSettle = true;

        bool go = (onLine && (steady || _wontSettle)) || late;

        // Releasing off the line means the turn failed, and the remaining tubes go on the mean axis
        // rather than each spending its own clock discovering the same thing.
        if (go && !onLine) _gaveUp = true;

        string said = abandoned.Length > 0
                          ? abandoned
                          : Say(now, go, onLine, steady, late, measured, offDegrees);

        return new ReleaseCommand(direction, roll, go, now.NextTube, offDegrees, said);
    }

    private void ForgetTheTurn()
    {
        _startedOff = double.NaN;
        _bestOff = double.PositiveInfinity;
        _sinceClosing = 0.0;
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
    // produced them, and those are gone by the time anything lands.
    private string Say(in ReleaseSituation now, bool go, bool onLine, bool steady, bool late,
                       bool measured, double offDegrees)
    {
        int tube = now.NextTube + 1;
        string line = measured ? $"{offDegrees:F1} deg off the line and " : "";
        string sweeping = $"tubes sweeping {now.SweepMetresPerSecond:F3} m/s";

        if (!go)
        {
            if (onLine) return $"settling, {sweeping}";

            return measured
                       ? $"turning onto tube {tube}, {offDegrees:F1} deg to go"
                       : $"turning onto tube {tube} blind, its axis will not resolve";
        }

        if (!onLine)
        {
            return $"releasing tube {tube} off the line, {offDegrees:F1} deg out - the rest will go "
                   + "on the mean axis";
        }

        if (steady) return $"releasing tube {tube}, {line}{sweeping}";

        return late
                   ? $"releasing tube {tube} late, {line}{sweeping} - the warheads will scatter"
                   : $"releasing tube {tube} without settling, {line}{sweeping} - this vehicle has "
                     + "already shown it will not";
    }
}
