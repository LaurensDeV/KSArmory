using Brutal.Numerics;
using KSA;

namespace KSArmory;

// A commanded turn of the bus after its split, and what it costs. docs/RV-POINTING.md.
internal sealed partial class IcbmComputer
{
    private readonly BusTurn _turn = new();

    // 0 not begun, 1 turning off the line, 2 turning back, 3 done. Only the turn test turns back.
    private int _turnLeg;
    private double _turnLogIn;
    private double _turnMassBefore = double.NaN;
    private double _turnOwedBefore = double.NaN;
    private double _lastOwed = double.NaN;

    private bool Turning => _turn.Holds;

    // A turn has just settled and the walk has not yet been re-priced on the new axes.
    private bool _reachDueAfterTurn;

    private void ResetTurn()
    {
        _turn.Reset();
        _turnLeg = 0;
        _turnLogIn = 0.0;
        _lastOwed = double.NaN;
        _entryLineCci = Vec.Zero;
        _reachDueAfterTurn = false;
        Program.CoastLineCci = Vec.Zero;
    }

    // After the split's shove is nulled -- or the null is given up on, or there is no trim -- and before the
    // first post-boost pass, so every pass and every release sees the line the bus ends on.
    private bool TurnIsDue
        => _didSplit && !_awaitingSplit && Command.ReadyToDeploy
           && !Program.IsShortShot && !Program.ReleasesAtCutoff && WarheadsAway == 0
           && _postBoost.Cycles == 0
           && (_trim.Done || _trimAbandoned || !Config.TrimBeforeRelease);

    private void DriveTurn(double simStep, in IcbmState state)
    {
        bool test = Config.TurnTestDegrees != 0.0;
        if (!test && !Program.TurnsAtTheSplit) return;

        if (_turnLeg == 0)
        {
            if (!TurnIsDue) return;

            double3 line = Program.CutoffLineCci;
            if (line.Equals(Vec.Zero)) return;

            if (test)
            {
                double3 axis = Vec.Unit(Vec.Cross(line, state.VelocityCci));
                if (axis.Equals(Vec.Zero)) axis = Vec.Unit(Vec.Cross(line, state.UpCci));

                StartTurn(1, Rotate(line, axis, double.DegreesToRadians(Config.TurnTestDegrees)), state);
                return;
            }

            // A split that came too late to turn before the release instant: a coast shorter than the gate and the allowance.
            bool found = TryEntryAirflow(state, out double3 airflow, out double altitude);
            string keeps = Program.CommittedArrivalFromNow < Program.ReleaseGate
                               ? "the coast left no room to turn before the release"
                               : !found ? "its conic has no entry to point at"
                               : "";

            if (keeps.Length > 0)
            {
                _turnLeg = 3;
                Log.Info($"turn on {KsaWorld.DisplayName(Craft)}: keeps the cutoff line -- {keeps}");
                return;
            }

            _entryLineCci = Vec.Unit(airflow);
            Log.Info($"turn on {KsaWorld.DisplayName(Craft)}: onto the airflow at {altitude / 1000.0:F0} km, "
                     + $"{Degrees(line, airflow):F1} deg from the cutoff line");
            StartTurn(1, airflow, state);
            return;
        }

        if (_turnLeg >= 3) return;

        double3 nose = Vec.Zero;
        if (Parent is { } parent) KsaWorld.TryControlFrameCci(Craft, parent, out nose, out _, out _);

        BusTurn.Stage stage = _turn.Update(simStep, nose);

        _turnLogIn -= simStep;
        if (_turnLogIn <= 0.0 && stage == BusTurn.Stage.Turning)
        {
            _turnLogIn = 1.0;
            Log.Info($"turn {_turnLeg} on {KsaWorld.DisplayName(Craft)}: {_turn.ElapsedSeconds:F1} s, "
                     + $"{_turn.OffDegrees:F2} deg off, {_turn.RateDegPerSecond:F2} deg/s, {Mass():F2} kg, "
                     + $"stage {StageDistance():F1} m, owed {_lastOwed:F3} m/s");
        }

        if (stage == BusTurn.Stage.Turning) return;

        if (stage == BusTurn.Stage.Capped) Program.CoastLineCci = _turn.ReachedCci;

        Log.Info($"turn {_turnLeg} on {KsaWorld.DisplayName(Craft)}: {(stage == BusTurn.Stage.Settled ? "settled" : "capped")} "
                 + $"after {_turn.ElapsedSeconds:F1} s from {_turn.StartDegrees:F1} deg, first on the line at "
                 + $"{_turn.FirstOnLineSeconds:F1} s, {_turn.OffDegrees:F2} deg off at the end; "
                 + $"spent {_turnMassBefore - Mass():F3} kg, owed {_turnOwedBefore:F3} -> {_lastOwed:F3} m/s, "
                 + $"stage {StageDistance():F1} m");

        if (_turnLeg == 1 && test)
        {
            // Back onto the cutoff line, so the shot flies on as it would have and is still scored.
            StartTurn(2, Program.CutoffLineCci, state);
            Program.CoastLineCci = Vec.Zero;
        }
        else
        {
            _turnLeg = 3;
            _reachDueAfterTurn = !test;
        }
    }

    private void StartTurn(int leg, double3 targetCci, in IcbmState state)
    {
        _turnLeg = leg;
        _turnLogIn = 0.0;
        _turnMassBefore = Mass();
        _turnOwedBefore = _lastOwed;
        _turn.Begin(targetCci, Config.TurnCapSeconds);
        Program.CoastLineCci = Vec.Unit(targetCci);

        // A reading taken before the turn would be consumed by the first pass after it.
        _measureDue = false;
        _freshMiss = double.NaN;

        Log.Info($"turn {leg} on {KsaWorld.DisplayName(Craft)}: commanded, release and post-boost held, "
                 + $"{Mass():F2} kg aboard, stage {StageDistance():F1} m, owed {_lastOwed:F3} m/s");
    }

    // Where the air first turns an arriving RV and the airflow there, on the conic the bus is on now: every point of
    // one coast gives the same entry, so the live state stands in for a release probe that does not exist yet.
    private bool TryEntryAirflow(in IcbmState state, out double3 airflowCci, out double altitude)
        => TryEntryAirflow(state.Body, state.PositionCci, state.VelocityCci, out airflowCci, out altitude);

    private bool TryEntryAirflow(in BallisticBody body, double3 positionCci, double3 velocityCci,
                                 out double3 airflowCci, out double altitude)
    {
        airflowCci = Vec.Zero;
        altitude = double.NaN;
        if (Parent is not { } parent) return false;

        BodyAir air = KsaWorld.BodyAirOf(parent);
        double r = Vec.Len(positionCci);
        double v2 = Vec.Len2(velocityCci);

        // The entry speed sets the altitude and the altitude the speed; two rounds settle it to metres.
        double speed = Math.Sqrt(v2);
        for (int i = 0; i < 2; i++)
        {
            altitude = EntryDirection.AuthorityAltitude(air, speed);
            if (!double.IsFinite(altitude)) return false;
            double re = body.SurfaceRadius + altitude;
            speed = Math.Sqrt(Math.Max(0.0, v2 + (2.0 * body.Mu * ((1.0 / re) - (1.0 / r)))));
        }

        return EntryDirection.TryAt(body, positionCci, velocityCci, body.SurfaceRadius + altitude,
                                    out airflowCci, out _);
    }

    // The kick a release would give, turned onto the line the bus will leave on while it has not turned there yet --
    // so the burn and the passes are aimed for the warheads' real departure, and the turn moves the prediction by
    // the difference between two estimates of one line rather than by the whole swing of the kick.
    private double3 PredictedReleaseImpulseCci(in BallisticBody body, double3 releasePositionCci, double3 releaseVelocityCci)
    {
        double3 impulse = ReleaseImpulseCci();
        if (impulse.Equals(Vec.Zero) || !Program.TurnsAtTheSplit || Config.TurnTestDegrees != 0.0 || _turnLeg == 3)
            return impulse;

        double3 line = _entryLineCci;
        if (line.Equals(Vec.Zero) && !TryEntryAirflow(body, releasePositionCci, releaseVelocityCci, out line, out _))
            return impulse;

        double3 nose = Vec.Zero;
        if (Parent is { } parent) KsaWorld.TryControlFrameCci(Craft, parent, out nose, out _, out _);
        if (nose.Equals(Vec.Zero)) nose = Command.ThrustDirectionCci;
        if (nose.Equals(Vec.Zero)) return impulse;

        return Vec.RotationFromTo(Vec.Unit(nose), Vec.Unit(line)) * impulse;
    }

    // The nose against where the warhead is going now and the airflow it will meet, for every flight and either arm.
    private void SayReleaseAttitude(double3 positionCci, double3 velocityCci)
    {
        if (Parent is not { } parent) return;
        if (!KsaWorld.TryControlFrameCci(Craft, parent, out double3 nose, out _, out _) || nose.Equals(Vec.Zero)) return;

        string entry = TryEntryAirflow(Body, positionCci, velocityCci, out double3 airflow, out double altitude)
                           ? $", {Degrees(nose, airflow):F1} deg from the airflow it meets at {altitude / 1000.0:F0} km"
                           : ", no entry to compare with";
        Log.Info($"release attitude on {KsaWorld.DisplayName(Craft)}: nose {Degrees(nose, velocityCci):F1} deg "
                 + $"from its own velocity{entry}");
    }

    // The line the bus turned onto, zero when it holds the cutoff line.
    private double3 _entryLineCci;

    private static double Degrees(double3 a, double3 b)
        => double.RadiansToDegrees(Math.Acos(Math.Clamp(Vec.Dot(Vec.Unit(a), Vec.Unit(b)), -1.0, 1.0)));

    private double Mass() => KsaWorld.IsAlive(Craft) ? KsaWorld.MassKg(Craft) : double.NaN;

    private double StageDistance()
        => _separatedFrom is { } stack && KsaWorld.IsAlive(stack) && KsaWorld.IsAlive(Craft)
               ? Vec.Len(KsaWorld.PositionEcl(stack) - KsaWorld.PositionEcl(Craft))
               : double.NaN;

    private static double3 Rotate(double3 v, double3 unitAxis, double radians)
        => (v * Math.Cos(radians)) + (Vec.Cross(unitAxis, v) * Math.Sin(radians))
           + (unitAxis * (Vec.Dot(unitAxis, v) * (1.0 - Math.Cos(radians))));
}
