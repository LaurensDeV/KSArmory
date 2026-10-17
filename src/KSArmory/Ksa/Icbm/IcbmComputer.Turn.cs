using Brutal.Numerics;
using KSA;

namespace KSArmory;

// A commanded turn of the bus after its split, and what it costs. docs/RV-POINTING.md.
internal sealed partial class IcbmComputer
{
    private readonly BusTurn _turn = new();

    // 0 not begun, 1 turning off the line, 2 turning back, 3 done. Only the turn test uses legs 1 and 2.
    private int _turnLeg;
    private double _turnLogIn;
    private double _turnMassBefore = double.NaN;
    private double _turnOwedBefore = double.NaN;
    private double _lastOwed = double.NaN;

    private bool Turning => _turn.Holds;

    private void ResetTurn()
    {
        _turn.Reset();
        _turnLeg = 0;
        _turnLogIn = 0.0;
        _lastOwed = double.NaN;
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
        if (!(Config.TurnTestDegrees != 0.0)) return;

        if (_turnLeg == 0)
        {
            if (!TurnIsDue) return;

            double3 line = Program.CutoffLineCci;
            if (line.Equals(Vec.Zero)) return;

            double3 axis = Vec.Unit(Vec.Cross(line, state.VelocityCci));
            if (axis.Equals(Vec.Zero)) axis = Vec.Unit(Vec.Cross(line, state.UpCci));

            StartTurn(1, Rotate(line, axis, double.DegreesToRadians(Config.TurnTestDegrees)), state);
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

        if (_turnLeg == 1)
        {
            // Back onto the cutoff line, so the shot flies on as it would have and is still scored.
            StartTurn(2, Program.CutoffLineCci, state);
            Program.CoastLineCci = Vec.Zero;
        }
        else
        {
            _turnLeg = 3;
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

    private double Mass() => KsaWorld.IsAlive(Craft) ? KsaWorld.MassKg(Craft) : double.NaN;

    private double StageDistance()
        => _separatedFrom is { } stack && KsaWorld.IsAlive(stack) && KsaWorld.IsAlive(Craft)
               ? Vec.Len(KsaWorld.PositionEcl(stack) - KsaWorld.PositionEcl(Craft))
               : double.NaN;

    private static double3 Rotate(double3 v, double3 unitAxis, double radians)
        => (v * Math.Cos(radians)) + (Vec.Cross(unitAxis, v) * Math.Sin(radians))
           + (unitAxis * (Vec.Dot(unitAxis, v) * (1.0 - Math.Cos(radians))));
}
