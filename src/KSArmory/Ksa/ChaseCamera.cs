using Brutal.Numerics;
using KSArmory.Sim;

namespace KSArmory;

/// <summary>
/// Rides the main view behind a round in flight, and gives it back when the round is gone.
///
/// <para>The main view rather than a second one, because a secondary viewport draws a starfield
/// over a featureless grey ball — every pass that makes a planet look like a planet runs only for
/// the frame viewport. See <c>docs/BLOCKED-ON-KSA.md</c>.</para>
///
/// <para>Borrowing it is the dangerous part, so every exit hands it back: the round ending, the
/// battery going away, the switch going off, or a write failing.</para>
/// </summary>
internal sealed class ChaseCamera
{
    // Close enough to see the round, far enough that it is not filling the frame.
    private const double Behind = 45.0;
    private const double Above = 9.0;
    private const double Ahead = 250.0;

    private KsaWorld.MainView _saved;
    private IProjectile? _round;

    // A restore in progress. The follow cannot be re-attached until the viewport has actually
    // left Fixed mode, which happens on its next frame rather than when the mode is set.
    private KsaWorld.MainView _restoring;

    /// <summary>The round being chased, or null.</summary>
    public IProjectile? Round => _round;

    /// <summary>Hands the view back, if it was taken. Safe to call at any time.</summary>
    public void Release()
    {
        if (_round is null) return;

        _round = null;
        KsaWorld.BeginRestoreMainView(_saved);

        // Finished over the following frames, not now.
        _restoring = _saved;
        _saved = default;
        Log.Info("chase: released the main view");
    }

    /// <summary>
    /// Runs every frame, whether or not the chase is on, so a restore always completes.
    /// </summary>
    public void Tick()
    {
        if (!_restoring.Valid) return;

        if (KsaWorld.TryFinishRestore(_restoring)) _restoring = default;
    }

    // Lets go without touching the view, for when the player has already taken it.
    private void StandDown()
    {
        _round = null;
        _saved = default;
        Log.Info("chase: the view was taken over by hand, standing down");
    }

    /// <summary>Follows one round for one frame.</summary>
    public void Apply(DefenceBattery battery, bool enabled)
    {
        if (!enabled || battery.Platform is null)
        {
            Release();
            return;
        }

        // The player wins. Changing the camera mode by hand while this holds the view is a
        // decision, so it stands down rather than dragging the view back every frame -- and
        // fighting over the mode is what put the camera into Fixed while still following, which
        // takes the game down inside FixedController.
        if (_round is not null && !KsaWorld.MainViewIsFixed())
        {
            StandDown();
            return;
        }

        // Something attached a follow while this holds the view in Fixed mode. That pair is fatal
        // on the next frame, and it can arrive from anywhere in the game, so the view goes back
        // immediately rather than being held for one more frame.
        if (_round is not null && KsaWorld.MainViewIsFollowing())
        {
            Log.Info("chase: the view acquired a follow, giving it back");
            Release();
            return;
        }

        IProjectile? round = Current(battery);
        if (round is null)
        {
            Release();
            return;
        }

        // Read before the first write: setting Fixed clears the follow, so a reading taken
        // afterwards describes the borrowed state.
        if (_round is null)
        {
            _saved = KsaWorld.RememberMainView();
            if (!_saved.Valid) return;

            Log.Info($"chase: taking the main view for round {round.Tube}");
        }

        _round = round;

        double3 at = battery.DrawnRoundEcl(round);

        // Away from the planet, which is the direction gravity is not. A zero here (deep space)
        // is handled by TryPose falling back to any perpendicular rather than rolling the view.
        double3 up = -Vec.Unit(KsaWorld.GravityAt(battery.Platform, at));

        if (!ChaseView.TryPose(at, round.VelocityLocal, up, Behind, Above, Ahead,
                               out double3 eye, out double3 forward, out double3 upEcl))
        {
            return;
        }

        // A refused write must not leave the view held: the player would be stranded wherever the
        // last good frame put them.
        if (!KsaWorld.TryLookFromMainViewport(eye, forward, upEcl)) Release();
    }

    // The round already being ridden while it still flies, otherwise the newest in the air.
    // Re-picking every frame swaps between rounds of one salvo twice a second, which is
    // unwatchable and was the first thing anyone said about it.
    private IProjectile? Current(DefenceBattery battery)
    {
        if (_round is { State: RoundState.Flying } held && battery.Rounds.Contains(held)) return held;

        return Newest(battery);
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
