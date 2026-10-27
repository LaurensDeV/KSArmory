using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Borrows the player's main view and looks through the optical head, then gives it back.
///
/// <para><b>The main view is the full picture</b>: a camera window draws no clouds, ocean or
/// clustered lights (<c>docs/BLOCKED-ON-KSA.md</c>).</para>
///
/// <para><b>It follows the launcher's own craft, whatever the player was following.</b>
/// <c>FixedController</c> places the camera at <c>following.GetPositionEcl() + CameraOffset</c> in its
/// own pass, so an offset measured from any other craft carries a frame of its motion and the sight
/// shivers.</para>
/// </summary>
internal sealed class SightCamera : IViewPose
{
    private KsaWorld.MainView _saved;

    // The head being looked through, so the pose can be re-resolved inside the engine's viewport
    // pass. Null whenever the view is not held.
    private IOpticalHead? _head;

    // How far the optics are wound in, held for the engine's pass, which cannot reach the settings.
    private double _magnification = 1.0;

    /// <summary>Null: the sight looks where its head points, and the mouse aims the head.</summary>
    public ChaseOrbit? Orbit => null;


    /// <summary>
    /// Where the view goes, asked from inside the engine's own frame pass.
    ///
    /// <para>A pose written from the GUI hook is a frame of the target's angular motion old when
    /// drawn: a couple of pixels at 1x, a third of the picture at 16x under warp.</para>
    /// </summary>
    public bool TryPose(double3 followedEcl, out double3 offsetFromFollowed, out double3 forwardEcl,
                        out double3 upEcl, out double fovDeg)
    {
        offsetFromFollowed = forwardEcl = upEcl = Vec.Zero;
        fovDeg = 0.0;

        if (_head is not { } head || !_saved.Valid) return false;
        if (head.Platform is null || head.OpticPart is null) return false;

        // Against the position the engine has just placed the craft at, not the mod's older sample.
        if (!head.TryOpticViewEclAt(followedEcl, out double3 eye, out forwardEcl)) return false;

        offsetFromFollowed = eye - followedEcl;
        upEcl = head.RollReferenceEcl;
        fovDeg = SightZoom.FovDegreesFor(_saved.FovDeg, _magnification);

        // Caught here: the caller's catch would drop the whole pose for the frame.
        try { ProbeAim(head, eye, forwardEcl, fovDeg); } catch { /* a probe must never cost a frame */ }

        return true;
    }

    // Frames since the last line: a file write every frame in this pass causes the stutter it is
    // looking for. Sampled, with any new worst case let through.
    private int _probeSkipped;

    private double _probeWorst;

    // Where the camera is sent against where the target is, per frame, under a verbose log. A miss
    // that tracks `crosses` and grows with simulation speed is the aim arriving a frame late: 0.007 deg
    // per unit of speed, deliberately not extrapolated away (CLAUDE.md, "Everything the mod draws is a
    // step behind"). Also proves the in-pass re-solve in OpticalHead.TryOpticViewEclAt is reached.
    private void ProbeAim(IOpticalHead head, double3 eye, double3 forwardEcl, double fovDeg)
    {
        if (Log.Threshold > Log.Level.Debug) return;

        // Whatever the head is following, designation included: a designated patch of ground is
        // never a track.
        double3 targetEcl;
        double3 targetVel;

        if (head.Designation.Kind != AimpointKind.None)
        {
            targetEcl = head.Designation.PositionEcl;
            targetVel = head.Designation.VelocityEcl;

            // Resolved again against this instant: the stored position is a frame older than the
            // eye, and pairing them measures a step of the planet's 29.8 km/s as aiming error.
            if (head.Designation.NeedsResampling
                && KsaWorld.TryGroundAnchorEcl(head.Designation.Handle, head.Designation.Anchor,
                                               out double3 nowEcl, out double3 nowVel))
            {
                targetEcl = nowEcl;
                targetVel = nowVel;
            }
            else if (head.Designation.Handle is Vehicle designated && KsaWorld.IsAlive(designated))
            {
                targetEcl = KsaWorld.PositionEcl(designated);
                targetVel = KsaWorld.VelocityEcl(designated);
            }
        }
        else if (head.LockedTrack is { } track)
        {
            // The drawn position, which the bracket is painted at.
            targetEcl = track.PositionEcl;
            targetVel = track.VelocityEcl;

            if (track.Contact.TryDrawEgo(out double3 ego) && KsaWorld.TryEgoToEcl(ego, out double3 drawn))
            {
                targetEcl = drawn;
            }
        }
        else
        {
            return;
        }

        double3 toTarget = targetEcl - eye;
        double range = Vec.Len(toTarget);
        if (range < 1.0) return;

        double missed = Vec.AngleBetween(forwardEcl, toTarget);

        double dt = KsaWorld.SimStepSeconds;
        double speed = KsaWorld.SimulationSpeed;

        // How far the target crosses the picture in one step, from its velocity relative to the
        // platform; never two ecliptic positions a frame apart, which carry 29.8 km/s.
        double3 relative = head.Platform is { } craft
            ? targetVel - KsaWorld.VelocityEcl(craft)
            : targetVel;

        double3 along = Vec.Unit(toTarget);
        double3 across = relative - along * Vec.Dot(relative, along);
        double crossing = range > 0.0 ? Vec.Len(across) / range * dt : 0.0;

        // One in fifteen, plus anything worse than has been seen: four lines a second at 60 fps.
        bool worst = missed > _probeWorst * 1.25;
        if (worst) _probeWorst = missed;

        if (!worst && ++_probeSkipped < 15) return;
        _probeSkipped = 0;

        double field = Math.Max(fovDeg, 1e-6);

        Log.Debug(() =>
            $"  sight aim: missed {double.RadiansToDegrees(missed):F4} deg "
            + $"({double.RadiansToDegrees(missed) / field * 100.0:F1}% of a {field:F2} deg field) | "
            + $"target crosses {double.RadiansToDegrees(crossing):F4} deg per step | "
            + $"range {range / 1000.0:F2} km, step {dt * 1000.0:F2} ms, sim {speed:F2}x");
    }

    // The craft the view was pointed at, which every offset below is measured from; the panel can
    // move to another system without the view being released.
    private Vehicle? _followed;

    /// <summary>True while this holds the main view, which is what the chase has to outrank.</summary>
    public bool Holding => _saved.Valid;

    // Frames the restore has been attempted and refused, as on leaving flight.
    private int _refusedFrames;

    // Long enough to cover a scene load.
    private const int GiveUpAfterFrames = 180;

    /// <summary>
    /// Lets go of the mode and the follow, for when the scene the recording describes has gone.
    ///
    /// <para>Leaving flight is the case: the new scene sets up its own camera.</para>
    ///
    /// <para><b>The field of view is put back anyway</b>: the camera outlives every scene, and the
    /// player's zoom keys clamp to 15°–120°, so nothing else would recover it.</para>
    /// </summary>
    public void Forget()
    {
        if (!_saved.Valid) return;

        KsaWorld.StopDrivingMainView();
        KsaWorld.TrySetMainViewFov(_saved.FovDeg);

        _saved = default;
        _followed = null;
        _head = null;
        _refusedFrames = 0;
        Log.Info("sight: the scene changed, handing back only the field of view");
    }

    /// <summary>Hands the view back, if it was taken. Safe to call at any time.</summary>
    public void Release()
    {
        if (!_saved.Valid) return;

        // The follow is classified here too: losing the head and switching vessels on one frame
        // reaches this without Apply seeing the takeover.
        //
        // A refused restore keeps the recording and tries again; dropped, the view stays in Fixed
        // with nothing left to restore.
        if (!KsaWorld.TryHandBackMainView(_saved, _followed, out bool mode, out bool follow))
        {
            _refusedFrames++;
            if (_refusedFrames == 1 || _refusedFrames == GiveUpAfterFrames)
            {
                Log.Warn($"sight: could not hand the main view back (mode={mode} follow={follow}), "
                         + (_refusedFrames == 1 ? "will keep trying" : "giving up"));
            }

            if (_refusedFrames < GiveUpAfterFrames) return;
        }

        _saved = default;
        _followed = null;
        _head = null;
        _refusedFrames = 0;
        Log.Info("sight: released the main view");
    }

    /// <summary>
    /// The field the view was showing before this took it, which is what a magnification is
    /// measured against. Zero while the view is not held.
    /// </summary>
    public double BaseFovDeg => _saved.Valid ? _saved.FovDeg : 0.0;

    /// <summary>Points the main view through the optical head for one frame.</summary>
    /// <param name="wanted">The optic is switched to the main view on the system being shown.</param>
    /// <param name="outranked">Something with a stronger claim holds the view — the chase.</param>
    /// <param name="magnification">How far the head's optics are wound in.</param>
    /// <returns>
    /// <see cref="ViewAction.StandDown"/> asks the caller to switch the setting off, or the next frame
    /// takes the view straight back.
    /// </returns>
    public ViewAction Apply(IOpticalHead head, bool wanted, bool outranked, double magnification)
    {
        double3 eye = Vec.Zero;
        double3 forward = Vec.Zero;

        bool resolved = head.Platform is not null
                        && head.OpticPart is not null
                        && head.TryOpticViewEcl(out eye, out forward);

        ViewAction action = ViewClaim.ForOptic(wanted, resolved, outranked, Holding,
                                               StillOurs(outranked));

        switch (action)
        {
            case ViewAction.StandDown:
                StandDown();
                return action;

            case ViewAction.GiveBack:
                Release();
                return action;

            case ViewAction.Yield:
                // Held on paper, driven by the chase. The recording stays: the chase's own is of
                // this pose and hands the view back here when it finishes.
                return action;

            case ViewAction.Take when !Take(head):
                return ViewAction.Idle;

            case ViewAction.Idle:
                return action;
        }

        // The panel moved to another craft without the view being released: re-point before the
        // offset below is written, since it is measured from the craft followed.
        if (!ReferenceEquals(_followed, head.Platform) && !Follow(head.Platform)) return action;

        // The separation, both terms from one sample of the followed craft, so it carries no instant.
        double3 offsetFromCraft = eye - head.PlatformEcl;

        // A refused write must not leave the view held. The values written are the controller's
        // fallback; the pose source puts the aim in phase with the frame.
        _head = head;
        _magnification = magnification;

        // The field is part of the pose, answered inside the engine's viewport pass, so not written here.
        if (!KsaWorld.TryLookFromMainViewport(offsetFromCraft, forward, head.RollReferenceEcl,
                                              SightZoom.FovDegreesFor(_saved.FovDeg, magnification),
                                              this))
        {
            Release();
        }

        return action;
    }

    // What the engine reports about the view, against what this pointed it at; the rule is ViewClaim's.
    private bool StillOurs(bool outranked)
        => ViewClaim.StillOurs(KsaWorld.MainViewIsFixed(),
                               KsaWorld.MainViewFollows(_followed), outranked);

    private void StandDown()
    {
        bool modeIsOurs = KsaWorld.StandDownMainView(_saved, _followed);

        _saved = default;
        _followed = null;
        _head = null;
        _refusedFrames = 0;

        Log.Info($"sight: the view was taken over by hand ({(modeIsOurs ? "vessel" : "camera mode")}"
                 + "), standing down");
    }

    // Records what the view was doing, then points it at the launcher's craft; both or neither.
    private bool Take(IOpticalHead head)
    {
        if (head.Platform is null) return false;

        _saved = KsaWorld.RememberMainView();
        if (!_saved.Valid)
        {
            // Logged: silent, it looks like the optic being switched off.
            Log.Warn("sight: cannot read the main view, not taking it");
            return false;
        }

        if (!Follow(head.Platform))
        {
            _saved = default;
            return false;
        }

        return true;
    }

    // Points the view at a craft and records which one, so offsets are measured from what the engine applies them to.
    private bool Follow(Vehicle? platform)
    {
        if (platform is null) return false;

        if (!KsaWorld.TryFollowOnMainViewport(platform))
        {
            Log.Warn("sight: the view refused to follow the launcher");
            return false;
        }

        _followed = platform;
        Log.Info($"sight: the main view is looking through {KsaWorld.DisplayName(platform)}'s optical head");
        return true;
    }
}
