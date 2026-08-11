using Brutal.Numerics;
using KSA;

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
    // that could not read a starting pose simply cuts.
    private double _blend = 1.0;

    // The transition's own clock. The engine's step beats with the display's frame pacing, and a
    // cosmetic ease is the one consumer entitled to even that out -- see Sim/SmoothedStep.cs.
    private readonly SmoothedStep _blendStep = new();

    // Where the camera meant to be, and where the engine actually had it, last frame. Logged per
    // frame through a transition, to show whether the eye is advancing evenly and along which
    // axis it is not.
    private double3 _probeWantEcl;
    private double3 _probeHadEcl;
    private double3 _probeTravel;
    private bool _probing;

    // The pose being eased out of -- where the view was, and a point on what it was looking at.
    // Both held as separations from the craft so they keep up with it: the ecliptic is inertial
    // and the craft crosses it at ~29.8 km/s, so a point stored in it falls half a kilometre
    // behind every frame and the transition would start from open space.
    private double3 _fromOffset;
    private double3 _fromLookOffset;

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

    // The field to fly at. The sight's own base while it is holding underneath this, and otherwise
    // whatever the view was showing when this took it -- which is the player's, since nothing else
    // had touched it. Either way the answer is "the field the player chose", never the sight's
    // magnified one: the chase outranks the sight and would otherwise inherit its picture.
    private double Field(double unzoomedFovDeg)
        => unzoomedFovDeg > 0.0 ? unzoomedFovDeg
         : _saved.Valid ? _saved.FovDeg
         : SightZoom.DefaultFovDeg;

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

        _followed.Track(null, null);
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
    /// <param name="unzoomedFovDeg">
    /// The field the player's own view was set to, or zero if nothing has changed it. Required,
    /// because the sight magnifies by up to 16× and <em>yields</em> the view rather than releasing
    /// it — so a chase that inherits the picture untouched flies the whole transition down a
    /// three-degree straw. Stated every frame rather than set once, for the same reason the sight
    /// states its own: the player's zoom keys clamp at 15° and would otherwise wrench it back
    /// mid-flight.
    /// </param>
    public void Apply(IRoundsInFlight battery, bool enabled, double dtPlayer, double dtSim,
                      bool freezeTransition, double unzoomedFovDeg)
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

            if (!KsaWorld.TryLookFromMainViewport(_holdOffset, _holdForward, _holdUp, Field(unzoomedFovDeg))) Release();
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
            // just the aim swinging from a point already at the round.
            bool hasPose = KsaWorld.TryMainCameraPose(out double3 wasEcl, out double3 wasForward);

            _followed.Track(round, battery.Platform);

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
                // aims for is kilometres off in much the same direction, so the aim swings
                // through tens of degrees getting from one to the other: 43 degrees of sweep
                // peaking at 87 deg/s, with the round off screen for half the transition. Two
                // points at the same depth barely move apart at all.
                double depth = round.TargetRef is Vehicle craft && KsaWorld.IsAlive(craft)
                               ? Vec.Len(KsaWorld.PositionEcl(craft) - wasEcl)
                               : Math.Max(Vec.Len(round.PositionEcl - wasEcl), Ahead);

                _fromOffset = wasEcl - battery.PlatformEcl;
                _fromLookOffset = (wasEcl + Vec.Unit(wasForward) * depth) - battery.PlatformEcl;
                _blend = 0.0;
                _blendStep.Reset();
                _probing = false;
            }
            else
            {
                _blend = 1.0;
            }

            Log.Info($"chase: taking the main view for round {round.Tube}");
        }

        _round = round;
        _followed.Track(round, battery.Platform);

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

        ReportClosing(toGo, behind);

        if (!ChaseView.TryPose(Vec.Zero, round.VelocityLocal, up, up, behind, above, Ahead,
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
        if (_blend < 1.0 && freezeTransition)
        {
            // Held where the transition started, so the only thing moving is the world. See
            // Config.FreezeChaseTransition for what this is separating.
            double3 fromRoundHeld = _fromOffset - round.OffsetFromPlatform;

            if (ChaseView.TryBlend(fromRoundHeld, _fromLookOffset - round.OffsetFromPlatform,
                                   fromRoundHeld, _fromLookOffset - round.OffsetFromPlatform,
                                   up, 0.0,
                                   out double3 heldOffset, out double3 heldForward))
            {
                eye = heldOffset;
                forward = heldForward;

                ProbeBlend(round, eye, up, dtSim);
            }
        }
        else if (_blend < 1.0)
        {
            _blend = Math.Min(1.0, _blend + (_blendStep.Next(dtSim) / TransitionSeconds));

            // Offsets from the round, never a pair of ecliptic positions. PlatformEcl is sampled
            // before the round is stepped and round.PositionEcl after it, so differencing the two
            // ends in the ecliptic carries one whole step of the planet's motion -- 715 m on a
            // 24 ms frame against 286 m on a 9 ms one. That difference beats against the display's
            // frame pacing and swings the camera +-270 m vertically every frame.
            // OffsetFromPlatform is the round measured against the same frame's platform sample,
            // which is the pairing that cancels it; TryBlend is a lerp of points, so running it in
            // this translated frame is the same answer.
            double3 fromRound = _fromOffset - round.OffsetFromPlatform;
            double3 fromLookRound = _fromLookOffset - round.OffsetFromPlatform;

            if (ChaseView.TryBlend(fromRound, fromLookRound,
                                   eye, eye + forward * Ahead,
                                   up, _blend,
                                   out double3 blendedOffset, out double3 blendedForward))
            {
                eye = blendedOffset;
                forward = blendedForward;

                ProbeBlend(round, eye, up, dtSim);
            }
            else
            {
                _blend = 1.0;
            }
        }

        _holdOffset = eye;
        _holdForward = forward;
        _holdUp = up;

        // A refused write must not leave the view held: the player would be stranded wherever the
        // last good frame put them.
        if (!KsaWorld.TryLookFromMainViewport(eye, forward, up, Field(unzoomedFovDeg))) Release();
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

    private void ReportClosing(double toGo, double behind)
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

    // What the eye did this frame, split along the local vertical, measured as an offset from the
    // round so the planet's motion is not in it. Debug-only and only while a transition runs.
    private void ProbeBlend(IProjectile round, double3 eye, double3 up, double dtSim)
    {
        if (Log.Threshold > Log.Level.Debug) return;

        // What the engine currently has, in the same frame: its position is live off the round, so
        // this is the offset it is actually using -- the one the mod wrote last frame.
        double3 cameraEcl = KsaWorld.CameraPositionEcl();
        double3 had = cameraEcl - round.PositionEcl;

        // The camera's own travel through the world, which is what crosses terrain cells. Taken
        // against the round so the planet's motion is not in it, then put back.
        double3 cameraStep = _probing ? (had - _probeHadEcl) + round.TravelSinceLaunch - _probeTravel
                                      : Vec.Zero;

        if (_probing)
        {
            double3 wantStep = eye - _probeWantEcl;
            double3 hadStep = had - _probeHadEcl;

            // Where the camera is over the ground and how fast it is crossing it. Terrain is
            // re-tiled on a lattice whose ground cell scales with the body's radius, so the same
            // travel crosses 3.7 times as many cells on the Moon as on Earth -- and low and fast
            // is when a transition does it.
            string overGround = "n/a";
            if (GroundTest.Shared.TryGround(cameraEcl, out double3 centreEcl, out double surface))
            {
                double3 outward = Vec.Unit(cameraEcl - centreEcl);
                double altitude = Vec.Len(cameraEcl - centreEcl) - surface;

                double3 across = cameraStep - outward * Vec.Dot(cameraStep, outward);
                double speed = dtSim > 0.0 ? Vec.Len(across) / dtSim : 0.0;

                overGround = $"{altitude:F0} m up, {speed:F0} m/s across";
            }

            Log.Debug($"  blend {_blend:F3} step {dtSim * 1000.0:F2} ms | "
                     + $"want {Vec.Len(wantStep):F3} m (up {Vec.Dot(wantStep, up):F3}) | "
                     + $"had {Vec.Len(hadStep):F3} m (up {Vec.Dot(hadStep, up):F3}) | "
                     + $"{overGround}");
        }

        _probeWantEcl = eye;
        _probeHadEcl = had;
        _probeTravel = round.TravelSinceLaunch;
        _probing = true;
    }
}
