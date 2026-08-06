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
/// <para>Borrowing it is the dangerous part, so the rule is that every exit restores: the round
/// detonating, the battery going away, the player switching it off, and anything throwing.</para>
/// </summary>
internal sealed class ChaseCamera
{
    // Close enough to see the round, far enough that it is not filling the frame.
    private const double Behind = 45.0;
    private const double Above = 9.0;
    private const double Ahead = 250.0;

    private KsaWorld.MainView _saved;
    private IProjectile? _round;

    /// <summary>The round being chased, or null.</summary>
    public IProjectile? Round => _round;

    /// <summary>True while the main view is borrowed.</summary>
    public bool Active => _round is not null;

    /// <summary>Hands the view back, if it was taken. Safe to call at any time.</summary>
    public void Release()
    {
        if (_round is null) return;

        _round = null;
        KsaWorld.RestoreMainView(_saved);
        _saved = default;
        Log.Debug(() => "chase: released the main view");
    }

    /// <summary>
    /// Picks up the newest round in flight and follows it for one frame.
    /// </summary>
    public void Apply(DefenceBattery battery, bool enabled)
    {
        if (!enabled || battery.Platform is null)
        {
            Release();
            return;
        }

        IProjectile? round = Newest(battery);
        if (round is null)
        {
            Release();
            return;
        }

        // Remembered on the frame the view is taken, before anything is written to it.
        if (_round is null)
        {
            _saved = KsaWorld.RememberMainView();
            if (!_saved.Valid) return;

            Log.Debug(() => $"chase: taking the main view for round {round.Tube}");
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

        // A failed write is not a reason to keep the view: the player would be left at whatever
        // the last good frame was, with the round gone.
        if (!KsaWorld.TryLookFromMainViewport(eye, forward, upEcl)) Release();
    }

    // The newest, because that is the one just launched and the one worth watching. A round that
    // has detonated is dropped by the battery, so this naturally moves on.
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
