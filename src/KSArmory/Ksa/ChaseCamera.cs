using Brutal.Numerics;
using KSA;
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
    // The stand-off at range and at arrival; the camera closes between them as the round
    // converges.
    private const double Behind = 26.0;
    private const double Above = 6.0;
    private const double Ahead = 120.0;

    private const double BehindAtImpact = 7.0;
    private const double AboveAtImpact = 1.6;

    // Closing runs on time to impact, normalised against the time left when the chase began, so
    // it starts easing the moment the view is taken.
    // Zero, so the camera is still moving when the round goes off.
    private const double CloseUntil = 0.0;

    // What the flight had left when the view was taken. The whole curve is measured against it.
    private double _flightAtTake;

    // The stand-off actually in force, kept so it can be held through a frame with no closing
    // solution rather than snapping back to the full distance.
    private double _behind = Behind;
    private double _above = Above;

    // How long the view takes to travel from where the player had it onto the round.
    private const double TransitionSeconds = 1.2;

    // Progress along that: 0 at the player's pose, 1 riding the round. Starts finished, so a chase
    // that could not read a starting pose simply cuts, as it always did.
    private double _blend = 1.0;

    // The pose being eased out of -- where the view was, and a point on what it was looking at.
    // Both held as separations from the craft so they keep up with it: the ecliptic is inertial
    // and the craft crosses it at ~29.8 km/s, so a point stored in it falls half a kilometre
    // behind every frame and the transition would start from open space.
    private double3 _fromOffset;
    private double3 _fromLookOffset;

    // The axis KSA's FixedController builds its basis around. A followable that is not a Vehicle
    // or a Celestial gets the Identity reference frame -- its declared CameraReferenceFrame is not
    // read at all -- so for RoundFollowable it is ecliptic +Z, which is a different direction from
    // local "up" at every site but one. See docs/KSA-CAMERAS.md.
    private static readonly double3 EngineAxis = new(0, 0, 1);

    // A missile leaves almost vertically, so "behind it" at first is under the vehicle.
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

    // Rounds that have already had their turn. Waiting for the sky to empty instead never fires:
    // a salvo's second missile outlives the target its first one killed.
    private readonly List<IProjectile> _passedOver = [];

    /// <summary>The round being chased, or null.</summary>
    public IProjectile? Round => _round;

    /// <summary>
    /// True while this holds the main view, including the hold on a burst after the round is
    /// gone. Read by <see cref="SightCamera"/>, which yields to it and stops painting: watching a
    /// round arrive is worth more than the sight for the seconds it lasts, and the sight resumes
    /// on its own afterwards.
    /// </summary>
    public bool HoldsMainView => _saved.Valid;

    /// <summary>
    /// Flight left as a fraction of what was left when the view was taken: one on the first frame,
    /// zero at impact, NaN with no closing solution. Shared with the overlay so the brackets grow
    /// on the same curve.
    /// </summary>
    public double Closing { get; private set; } = double.NaN;

    /// <summary>Hands the view back, if it was taken. Safe to call at any time.</summary>
    public void Release()
    {
        _holding = 0.0;
        _round = null;

        // All three belong to the engagement that has just ended. The blend left finished would
        // cut straight to the round next time instead of travelling onto it; the flight time is
        // what the whole stand-off curve is measured against, so carrying it over calibrates the
        // next chase against an engagement it has nothing to do with -- the second chase of a
        // session opens at about 11 m instead of the 26 it is meant to.
        _blend = 1.0;
        _flightAtTake = 0.0;
        _behind = Behind;
        _above = Above;

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
    /// <param name="dtPlayer">Wall clock, for how long a burst is lingered on. A viewing duration.</param>
    /// <param name="dtSim">
    /// The simulated step. <b>The transition runs on this, not on player time.</b> It is a camera
    /// move whose whole job is to arrive on a round, so it has to advance at the rate the round
    /// does: on player time it runs at full speed through the panel's slow-motion buttons and on
    /// through a pause, sliding the view across a world that is not moving. Same rule, and the
    /// same reason, as fire control — see CLAUDE.md.
    /// </param>
    public void Apply(IRoundsInFlight battery, bool enabled, double dtPlayer, double dtSim)
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
            // Only if the view is already on this craft: otherwise a site elsewhere takes the
            // camera off whatever is being watched.
            if (!KsaWorld.MainViewFollows(battery.Platform)) return;

            _saved = KsaWorld.RememberMainView();
            if (!_saved.Valid)
            {
                // Said, not swallowed. A silent return here is indistinguishable from the chase
                // being switched off, and looks like rounds simply stopping being followed.
                Log.Warn("chase: cannot read the main view, not taking it");
                return;
            }

            // Read where the player had the view BEFORE anything is attached to the round.
            // Camera.SetFollow sets PositionEcl to the followed object plus 2.5 mean radii, and
            // a round's mean radius is one metre -- so the instant the follow is swapped the
            // camera is 2.5 m from the missile. Reading afterwards gives that, not the player's
            // pose, and the transition then eases from its own destination: no travel at all,
            // just the aim swinging from a point already at the round. That is the whole of
            // "teleported very hard forward".
            bool hasPose = KsaWorld.TryMainCameraPose(out double3 wasEcl, out double3 wasForward);

            _followed.Track(round);

            if (!KsaWorld.TryFollowOnMainViewport(_followed))
            {
                Log.Warn("chase: the view refused to follow the round");
                _saved = default;
                return;
            }

            if (hasPose)
            {
                // Undo the jump SetFollow just made. This frame's view matrix was built in the
                // viewport pass, which is over, and the controller does not pick up the offset
                // until the next frame -- so without this one frame renders from beside the
                // missile before the transition has begun.
                KsaWorld.TryPlaceMainCamera(wasEcl);

                // At the target's distance, because that is what the player is looking at and
                // what the chase ends up looking past the round at. Put at the *round's* distance
                // instead it sits a hundred metres away in mid-air, while the point the chase
                // aims for is kilometres off in much the same direction — so the aim swings
                // through tens of degrees getting from one to the other, measured at 43 degrees
                // of sweep peaking at 87 deg/s with the round off screen for half the transition.
                // Two points at the same depth barely move apart at all.
                double depth = round.TargetRef is Vehicle craft && KsaWorld.IsAlive(craft)
                               ? Vec.Len(KsaWorld.PositionEcl(craft) - wasEcl)
                               : Math.Max(Vec.Len(round.PositionEcl - wasEcl), Ahead);

                _fromOffset = wasEcl - battery.PlatformEcl;
                _fromLookOffset = (wasEcl + Vec.Unit(wasForward) * depth) - battery.PlatformEcl;
                _blend = 0.0;
            }
            else
            {
                _blend = 1.0;
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
        double toGo = TimeToTarget(round);

        if (_flightAtTake <= 0.0 || !double.IsFinite(_flightAtTake)) _flightAtTake = toGo;

        Closing = _flightAtTake > 0.0 ? Math.Clamp(toGo / _flightAtTake, 0.0, 1.0) : double.NaN;

        // A target that dies mid-flight takes the closing solution with it, and StandOff answers
        // the full distance for a range that is not finite -- which throws the camera from ~12 m
        // back to 26 in one frame, about 900 m/s. A salvo whose first round kills the target while
        // a later one is being chased is the ordinary case, not a corner. Holding the last good
        // stand-off leaves the view where it was, which is what a camera watching a round with
        // nothing left to chase should do.
        if (double.IsFinite(toGo))
        {
            _behind = ChaseView.StandOff(toGo, _flightAtTake, CloseUntil, Behind, BehindAtImpact);
            _above = ChaseView.StandOff(toGo, _flightAtTake, CloseUntil, Above, AboveAtImpact);
        }

        double behind = _behind;
        double above = _above;

        ReportClosing(round, toGo, behind);

        if (!ChaseView.TryPose(Vec.Zero, round.VelocityLocal, up, EngineAxis, behind, above, Ahead,
                               out double3 eye, out double3 forward, out double3 upEcl))
        {
            return;
        }

        // The eye is relative to the round, so its height over the launcher is the two together.
        // Lifting rather than refusing: a view from slightly the wrong place beats none.
        double overLauncher = Vec.Dot(round.OffsetFromPlatform + eye, up);
        if (overLauncher < -FloorBelowLauncher) eye += up * (-FloorBelowLauncher - overLauncher);

        // Travelling onto that pose rather than cutting to it. Only the position really moves:
        // the player is looking at the target and the chase looks along a round flying at it, so
        // the two aim points are close and the view barely turns. Both ends are rebuilt from this
        // frame's samples, so the pair describes one instant however far along it is.
        if (_blend < 1.0)
        {
            _blend = Math.Min(1.0, _blend + (Math.Max(0.0, dtSim) / TransitionSeconds));

            if (ChaseView.TryBlend(battery.PlatformEcl + _fromOffset,
                                   battery.PlatformEcl + _fromLookOffset,
                                   round.PositionEcl + eye, round.PositionEcl + eye + forward * Ahead,
                                   EngineAxis, _blend,
                                   out double3 blendedEcl, out double3 blendedForward))
            {
                eye = blendedEcl - round.PositionEcl;
                forward = blendedForward;
            }
            else
            {
                _blend = 1.0;
            }
        }

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
    private IProjectile? Current(IRoundsInFlight battery)
    {
        if (_round is { } held)
        {
            if (held.State == RoundState.Flying && battery.Rounds.Contains(held)) return held;

            return null;
        }

        return Newest(battery);
    }

    // What the closing curve is being fed: the stand-off is visible, the input is not.
    private int _rangeFrames;

    private void ReportClosing(IProjectile round, double toGo, double behind)
    {
        if (++_rangeFrames < 30) return;

        _rangeFrames = 0;
        Log.Info($"chase: {(double.IsNaN(toGo) ? "no closing solution" : $"{toGo:F1} s to go")}"
                 + $" of {_flightAtTake:F1}, stand-off {behind:F1} m");
    }

    // Against the target where it is NOW, not the aimpoint: an aimpoint holds an absolute
    // ecliptic position, and the world leaves it behind at ~29.8 km/s, so the range to it grows
    // while the round closes. NaN when nothing is being chased.
    private static double TimeToTarget(IProjectile round)
    {
        if (round.TargetRef is not Vehicle target || !KsaWorld.IsAlive(target)) return double.NaN;

        double3 toTarget = KsaWorld.PositionEcl(target) - round.PositionEcl;
        double range = Vec.Len(toTarget);
        if (range < 1e-6) return 0.0;

        // Closing speed along the line of sight. Differenced here rather than taken from either
        // velocity alone: both carry the ecliptic's ~29.8 km/s, and it cancels only in the
        // subtraction.
        double closing = Vec.Dot(round.VelocityEcl - KsaWorld.VelocityEcl(target),
                                 toTarget / range);

        // Opening, or barely closing: nothing to count down to.
        return closing > 1.0 ? range / closing : double.NaN;
    }

    // Everything in the air right now has had its chance. The next launch has not.
    private void PassOverEverythingFlying(IRoundsInFlight battery)
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

    private IProjectile? Newest(IRoundsInFlight battery)
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
