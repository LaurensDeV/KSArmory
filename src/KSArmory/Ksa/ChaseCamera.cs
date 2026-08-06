using Brutal.Numerics;
using KSArmory.Sim;

namespace KSArmory;

/// <summary>
/// Rides the main view behind a round in flight, holds on the burst, and gives the view back.
///
/// <para>The main view rather than a second one, because a secondary viewport draws a starfield
/// over a featureless grey ball — every pass that makes a planet look like a planet runs only for
/// the frame viewport. See <c>docs/BLOCKED-ON-KSA.md</c>, which also has why the camera keeps
/// following its craft throughout.</para>
/// </summary>
internal sealed class ChaseCamera
{
    // Close enough to see the round, far enough that it is not filling the frame.
    private const double Behind = 45.0;
    private const double Above = 9.0;
    private const double Ahead = 250.0;

    // How long to keep looking at a burst. Cutting away the instant it goes off shows the one
    // moment worth watching for no frames at all.
    private const double LingerSeconds = 3.0;

    private KsaWorld.MainView _saved;
    private IProjectile? _round;

    // The last pose, which becomes the pose to hold once the round is gone.
    private double3 _holdOffset;
    private double3 _holdForward;
    private double3 _holdUp;
    private double _holding;

    // Set when the player takes the view back, cleared only once the sky is empty. Without it a
    // stand-down is undone by the next round of the same salvo, which reads as the camera being
    // stuck and fighting back.
    private bool _standingDown;

    /// <summary>The round being chased, or null.</summary>
    public IProjectile? Round => _round;

    /// <summary>Hands the view back, if it was taken. Safe to call at any time.</summary>
    public void Release()
    {
        _holding = 0.0;
        _round = null;

        // Keyed on holding the view, not on having a round: the hold after a burst has no round
        // and is exactly when the view still has to be given back.
        if (!_saved.Valid) return;

        KsaWorld.BeginRestoreMainView(_saved);
        _saved = default;
        Log.Info("chase: released the main view");
    }

    /// <summary>Follows one round for one frame.</summary>
    public void Apply(DefenceBattery battery, bool enabled, double dtPlayer)
    {
        if (!enabled || battery.Platform is null)
        {
            _standingDown = false;
            Release();
            return;
        }

        // Still watching where the last one went off. Checked before the stand-down, because the
        // hold is what precedes it.
        if (_holding > 0.0)
        {
            _holding -= Math.Max(0.0, dtPlayer);

            if (!KsaWorld.TryLookFromMainViewport(_holdOffset, _holdForward, _holdUp)) Release();
            else if (_holding <= 0.0) Release();

            return;
        }

        if (_standingDown)
        {
            if (!AnythingFlying(battery)) _standingDown = false;
            return;
        }

        // The player taking the view back is a decision, not a fault.
        if (_round is not null && !KsaWorld.MainViewIsFixed())
        {
            _round = null;
            _holding = 0.0;
            _saved = default;   // dropped, not restored: the view is already theirs
            _standingDown = true;
            Log.Info("chase: the view was taken over by hand, standing down");
            return;
        }

        IProjectile? round = Current(battery);
        if (round is null)
        {
            // The one being watched has gone off. Hold the last pose on the burst, then stand
            // down for the rest of the engagement rather than cutting to a sibling missile --
            // what just happened is the part worth seeing.
            if (_round is not null)
            {
                _round = null;
                _holding = LingerSeconds;
                _standingDown = true;
                Log.Info("chase: holding on the burst");
                return;
            }

            Release();
            return;
        }

        if (_round is null)
        {
            _saved = KsaWorld.RememberMainView();
            if (!_saved.Valid) return;

            Log.Info($"chase: taking the main view for round {round.Tube}");
        }

        _round = round;

        // Everything platform-relative. OffsetFromPlatform is the round's position measured from
        // the same craft the controller adds the camera offset to, so no absolute position and no
        // sampling instant enters into it.
        double3 relative = round.OffsetFromPlatform;
        double3 up = -Vec.Unit(KsaWorld.GravityAt(battery.Platform, round.PositionEcl));

        if (!ChaseView.TryPose(relative, round.VelocityLocal, up, Behind, Above, Ahead,
                               out double3 eye, out double3 forward, out double3 upEcl))
        {
            return;
        }

        MeasureSlip(battery);

        _holdOffset = eye;
        _holdForward = forward;
        _holdUp = upEcl;

        // A refused write must not leave the view held: the player would be stranded wherever the
        // last good frame put them.
        if (!KsaWorld.TryLookFromMainViewport(eye, forward, upEcl)) Release();
    }

    // The round already being ridden, while it still flies. Once it stops, null: the caller holds
    // the pose it last had rather than recomputing one from a detonated round, whose position has
    // just jumped to the burst point and whose velocity describes nothing.
    private IProjectile? Current(DefenceBattery battery)
    {
        if (_round is { } held)
        {
            if (held.State == RoundState.Flying && battery.Rounds.Contains(held)) return held;

            return null;
        }

        return Newest(battery);
    }

    // Diagnostic, not correction. The camera is placed against the analytic position and the body
    // is drawn against the physics one; if that difference moves frame to frame, the round
    // shivers no matter what the camera does. Logged as a range so a wobble is distinguishable
    // from a constant offset, which is the whole question.
    private double3 _lastSlip;
    private double _slipMin = double.MaxValue;
    private double _slipMax;
    private double _slipStep;
    private int _slipFrames;

    private void MeasureSlip(DefenceBattery battery)
    {
        if (!KsaWorld.TryDrawSlip(battery.Platform, out double3 slip)) return;

        double magnitude = Vec.Len(slip);
        _slipMin = Math.Min(_slipMin, magnitude);
        _slipMax = Math.Max(_slipMax, magnitude);

        if (_slipFrames > 0) _slipStep = Math.Max(_slipStep, Vec.Len(slip - _lastSlip));

        _lastSlip = slip;

        if (++_slipFrames < 60) return;

        Log.Info($"chase: draw slip {_slipMin:F2}-{_slipMax:F2} m, "
                 + $"worst frame-to-frame change {_slipStep:F3} m");

        _slipFrames = 0;
        _slipMin = double.MaxValue;
        _slipMax = 0.0;
        _slipStep = 0.0;
    }

    private static bool AnythingFlying(DefenceBattery battery)
    {
        IReadOnlyList<IProjectile> rounds = battery.Rounds;

        for (int i = 0; i < rounds.Count; i++)
        {
            if (rounds[i].State == RoundState.Flying) return true;
        }

        return false;
    }

    private static IProjectile? Newest(DefenceBattery battery)
    {
        IReadOnlyList<IProjectile> rounds = battery.Rounds;

        for (int i = rounds.Count - 1; i >= 0; i--)
        {
            if (rounds[i].State == RoundState.Flying) return rounds[i];
        }

        return null;
    }
}
