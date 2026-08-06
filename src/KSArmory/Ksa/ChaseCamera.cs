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
    // The stand-off at range, and the one at the moment of arrival. The camera closes between
    // them as the round converges: a fixed distance makes a missile appear to hang still, because
    // everything in frame scales together.
    private const double Behind = 26.0;
    private const double Above = 6.0;
    private const double Ahead = 120.0;

    private const double BehindAtImpact = 7.0;
    private const double AboveAtImpact = 1.6;

    // Where the closing starts and where it has finished.
    private const double CloseFrom = 1_500.0;
    private const double CloseTo = 60.0;

    // How far a round must get from the tube before the view is taken. A missile leaves almost
    // vertically, so "behind it" for the first moment is underneath the vehicle that fired it.
    private const double ClearOfLauncher = 80.0;

    // And a floor, for anything that gets past that: the eye never sits below the launcher by
    // more than this, whatever the flight path says.
    private const double FloorBelowLauncher = 2.0;

    // How long to keep looking at a burst. Cutting away the instant it goes off shows the one
    // moment worth watching for no frames at all.
    private const double LingerSeconds = 3.0;

    private readonly RoundFollowable _followed = new();

    private KsaWorld.MainView _saved;
    private IProjectile? _round;

    // The last pose, which becomes the pose to hold once the round is gone.
    private double3 _holdOffset;
    private double3 _holdForward;
    private double3 _holdUp;
    private double _holding;

    // Rounds that have already had their turn: the siblings still in the air when the view was
    // given up. Waiting for the sky to empty instead does not work -- a salvo's second missile
    // outlives the target its first one killed and flies to its full life, so the sky is never
    // empty and nothing is ever followed again.
    private readonly List<IProjectile> _passedOver = [];

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

        _followed.Track(null);
        KsaWorld.BeginRestoreMainView(_saved);
        KsaWorld.RestoreFollow(_saved);
        _saved = default;
        Log.Info("chase: released the main view");
    }

    /// <summary>Follows one round for one frame.</summary>
    public void Apply(DefenceBattery battery, bool enabled, double dtPlayer)
    {
        if (!enabled || battery.Platform is null)
        {
            _passedOver.Clear();
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

        // Anything still in the air from the last engagement that has since stopped can be
        // forgotten; the list only has to outlive the rounds it names.
        _passedOver.RemoveAll(r => r.State != RoundState.Flying);

        // The player taking the view back is a decision, not a fault.
        if (_round is not null && !KsaWorld.MainViewIsFixed())
        {
            _round = null;
            _holding = 0.0;
            _saved = default;   // dropped, not restored: the view is already theirs
            PassOverEverythingFlying(battery);
            Log.Info("chase: the view was taken over by hand, standing down");
            return;
        }

        IProjectile? round = Current(battery);
        if (round is null)
        {
            // The one being watched has gone off. Hold the last pose on the burst, then stand
            // down for the rest of the engagement rather than cutting to a sibling missile --
            // what just happened is the part worth seeing.
            if (_round is { } spent)
            {
                // Anchored to the platform, so the view stays on the burst rather than being left
                // behind by the ecliptic.
                _followed.HoldAgainst(battery.Platform, spent);

                _round = null;
                _holding = LingerSeconds;
                PassOverEverythingFlying(battery);
                Log.Info("chase: holding on the burst");
                return;
            }

            Release();
            return;
        }

        if (_round is null)
        {
            _saved = KsaWorld.RememberMainView();
            if (!_saved.Valid)
            {
                // Said, not swallowed. A silent return here is indistinguishable from the chase
                // being switched off, and looks like rounds simply stopping being followed.
                Log.Warn("chase: cannot read the main view, not taking it");
                return;
            }

            _followed.Track(round);

            if (!KsaWorld.TryFollowOnMainViewport(_followed))
            {
                Log.Warn("chase: the view refused to follow the round");
                _saved = default;
                return;
            }

            Log.Info($"chase: taking the main view for round {round.Tube}");
        }

        _round = round;
        _followed.Track(round);

        // Measured from the round, because the round is what the camera follows: the engine adds
        // this to whatever position the round reports during its own frame pass, so nothing here
        // is sampled at one instant and applied at another.
        double3 up = -Vec.Unit(KsaWorld.GravityAt(battery.Platform, round.PositionEcl));

        // Closing in as it arrives, which is what conveys the speed.
        double range = RangeToTarget(round);
        double behind = ChaseView.StandOff(range, CloseFrom, CloseTo, Behind, BehindAtImpact);
        double above = ChaseView.StandOff(range, CloseFrom, CloseTo, Above, AboveAtImpact);

        ReportClosing(round, range, behind);

        if (!ChaseView.TryPose(Vec.Zero, round.VelocityLocal, up, behind, above, Ahead,
                               out double3 eye, out double3 forward, out double3 upEcl))
        {
            return;
        }

        // The eye is relative to the round, so its height over the launcher is the two together.
        // Lifting rather than refusing: a view from slightly the wrong place beats none.
        double overLauncher = Vec.Dot(round.OffsetFromPlatform + eye, up);
        if (overLauncher < -FloorBelowLauncher) eye += up * (-FloorBelowLauncher - overLauncher);

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

    // What the closing curve is actually being fed. The stand-off is the visible thing, and the
    // range is the input nobody can see: an aimpoint that is not resampled reports a distance to
    // where the target was at launch, which drives the curve from the wrong number.
    private int _rangeFrames;

    private void ReportClosing(IProjectile round, double range, double behind)
    {
        if (++_rangeFrames < 30) return;

        _rangeFrames = 0;
        Log.Info($"chase: range {(double.IsNaN(range) ? "none" : $"{range:F0} m")}, "
                 + $"stand-off {behind:F1} m, aimpoint {round.Aimpoint.Kind}");
    }

    // How far the round still has to go, or NaN when it is not aimed at anything -- an unguided
    // shell has nothing to converge on and keeps the full stand-off.
    private static double RangeToTarget(IProjectile round)
    {
        double3 at = round.Aimpoint.PositionEcl;
        if (!Vec.IsFinite(at) || Vec.Len2(at) < 1e-6) return double.NaN;

        return Vec.Len(at - round.PositionEcl);
    }

    // Everything in the air right now has had its chance. The next launch has not.
    private void PassOverEverythingFlying(DefenceBattery battery)
    {
        IReadOnlyList<IProjectile> rounds = battery.Rounds;

        for (int i = 0; i < rounds.Count; i++)
        {
            if (rounds[i].State == RoundState.Flying && !_passedOver.Contains(rounds[i]))
            {
                _passedOver.Add(rounds[i]);
            }
        }
    }

    private IProjectile? Newest(DefenceBattery battery)
    {
        IReadOnlyList<IProjectile> rounds = battery.Rounds;

        for (int i = rounds.Count - 1; i >= 0; i--)
        {
            IProjectile round = rounds[i];

            if (round.State != RoundState.Flying) continue;
            if (_passedOver.Contains(round)) continue;

            // Still in the launcher's lap. Not skipped for good -- it is picked up as soon as it
            // is clear, which is a beat later.
            if (Vec.Len(round.TravelSinceLaunch) < ClearOfLauncher) continue;

            return round;
        }

        return null;
    }
}
