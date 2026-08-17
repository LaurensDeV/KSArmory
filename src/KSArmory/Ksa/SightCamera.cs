using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Borrows the player's main view and looks through the optical head, then gives it back.
///
/// <para><b>The main view rather than a second one, because a secondary viewport draws no planet.</b>
/// Every pass that makes one look like a planet — the planet renderer, the light and shadow
/// passes, the ocean, the atmosphere and cloud compute — runs only for the frame viewport, so a
/// sight on a secondary window shows a starfield over a featureless grey ball. That is the
/// engine's own limit, and taking the main view is the workaround `docs/BLOCKED-ON-KSA.md`
/// names.</para>
///
/// <para><b>It follows the launcher's own craft, whatever the player was following.</b>
/// <c>FixedController</c> places the camera at <c>following.GetPositionEcl() + CameraOffset</c>
/// during its own frame pass, so the offset is applied at a later instant than the one it was
/// computed at. Measured from the launcher's craft that costs nothing, because the eye is derived
/// from the same sample; measured from anything else it carries a frame of that craft's motion
/// every frame, which at 29.8 km/s reads as the sight shivering. Same reason
/// <see cref="ChaseCamera"/> follows the round it is riding.</para>
/// </summary>
internal sealed class SightCamera : IViewPose
{
    private KsaWorld.MainView _saved;

    // The head being looked through, kept only so the pose can be re-resolved inside the engine's
    // viewport pass. Null whenever the view is not held, which is what stops that pass reaching
    // into a system the panel has moved off.
    private IOpticalHead? _head;

    // How far the optics are wound in, from the last frame the panel was read. Held for the same
    // reason as the head: the pose is answered inside the engine's pass, which has no route to the
    // system's own settings.
    private double _magnification = 1.0;


    /// <summary>
    /// Where the view goes, asked from inside the engine's own frame pass.
    ///
    /// <para>The pose written from the GUI hook is a frame old by the time it is drawn, and that
    /// frame is one frame of the <em>target's</em> angular motion — a couple of pixels at 1x, and a
    /// third of the picture at 16x under warp, because it scales with simulation speed. Resolving
    /// it again here costs a part-transform read and removes the whole term.</para>
    /// </summary>
    public bool TryPose(double3 followedEcl, out double3 offsetFromFollowed, out double3 forwardEcl,
                        out double3 upEcl, out double fovDeg)
    {
        offsetFromFollowed = forwardEcl = upEcl = Vec.Zero;
        fovDeg = 0.0;

        if (_head is not { } head || !_saved.Valid) return false;
        if (head.Platform is null || head.OpticPart is null) return false;

        // Against the position the engine has just placed the craft at, not the mod's own sample.
        // Pairing the eye with an older platform sample would put a frame of the planet's motion
        // back into the separation, which is the same fault in a different term.
        if (!head.TryOpticViewEclAt(followedEcl, out double3 eye, out forwardEcl)) return false;

        offsetFromFollowed = eye - followedEcl;
        upEcl = head.RollReferenceEcl;
        fovDeg = SightZoom.FovDegreesFor(_saved.FovDeg, _magnification);

        // Diagnostic only, and inside the engine's frame pass -- so it is caught here rather than
        // relying on the caller's catch, which would drop the whole pose for that frame and turn a
        // logging fault into a visible one.
        try { ProbeAim(head, eye, forwardEcl, fovDeg); } catch { /* a probe must never cost a frame */ }

        return true;
    }

    // Where the camera is being sent against where the target actually is, per frame, under a
    // verbose log.
    //
    // The question it settles: re-resolving the pose here refreshes the *part's* transform, but the
    // head's aim relative to that part was integrated a frame ago in the GUI hook, against wherever
    // the target was then. So a residual of one frame of the target's angular motion should survive
    // the fix in `fix(sight): aim the camera in phase with the frame it is drawn in`, and it should
    // scale with simulation speed.
    //
    // `missed` is what the geometry actually gives; `crosses` is how far the target moves across
    // the picture in one step. A miss that tracks the crossing, and grows with simulation speed,
    // says the aim is arriving a frame late and the fix belongs in how it is carried to this pass.
    // A miss that stays put while the crossing grows says it is something else entirely.
    //
    // What it found: the miss is linear in simulation speed -- 0.0142 deg at 2x, 0.0302 at 4x,
    // 0.0607 at 10x, a flat 0.007 deg per unit of speed. That is the frame-late term, and the fix
    // is the in-pass re-solve in OpticalHead.TryOpticViewEclAt rather than any extrapolation of the
    // drive: leading the aim by the drive's own last turn was tried and measured far worse, 0.35
    // deg at 10x against a target crossing 0.0037. Left in because the same measurement is what
    // proves the re-solve is reached, and it silently was not for a designation.
    //
    // Reported as a fraction of the field because that is what "off target" means to whoever is
    // looking through it: 0.3 degrees is nothing at 60 degrees and a tenth of the picture at 3.
    // Frames since the last line. A probe inside the engine's frame pass that writes a file every
    // frame is a probe that causes the stutter it is looking for -- 4,000 synchronous writes in one
    // session, in the loop that positions the camera. Sampled instead, with any new worst case let
    // through so a spike is never missed between samples.
    private int _probeSkipped;
    private double _probeWorst;

    private void ProbeAim(IOpticalHead head, double3 eye, double3 forwardEcl, double fovDeg)
    {
        if (Log.Threshold > Log.Level.Debug) return;

        // Whatever the head is actually following, which is the designation when there is one and
        // the set's own pick otherwise. Asking only for a track measured nothing at all while a
        // designation was driving -- and a designated patch of ground is never a track, so the
        // probe was silent in exactly the case it was wanted for.
        double3 targetEcl;
        double3 targetVel;

        if (head.Designation.Kind != AimpointKind.None)
        {
            targetEcl = head.Designation.PositionEcl;
            targetVel = head.Designation.VelocityEcl;

            // Resolved again, here, against this instant. The stored position was resampled in the
            // GUI hook a frame ago, and the eye below comes from the engine's own fresh sample --
            // so comparing them measures one step of the planet's 29.8 km/s and calls it an aiming
            // error. 383 m at 439 km reads as 0.05 deg, and it spikes on a long frame, which is
            // exactly what a real fault would look like. An instrument that pairs two epochs is
            // measuring itself; see docs/FRAMES-AND-EPOCHS.md.
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
            // The drawn position, which is what the bracket is painted at -- so this compares the
            // two things actually on screen rather than the camera against a simulated position no
            // pixel corresponds to.
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

        // How far the target crosses the picture in one step, from its velocity *relative to the
        // platform* and within this one frame.
        //
        // Never by differencing two ecliptic positions a frame apart, which is what this did first:
        // that carries 29.8 km/s of the planet's own motion, 655 m across a 22 ms step, which at
        // 8 km reads as 4.7 degrees of target movement that never happened. It printed exactly
        // that. See docs/FRAMES-AND-EPOCHS.md -- the rule is the same one the rounds and the
        // overlay already obey, and a diagnostic is not exempt from it.
        double3 relative = head.Platform is { } craft
            ? targetVel - KsaWorld.VelocityEcl(craft)
            : targetVel;

        double3 along = Vec.Unit(toTarget);
        double3 across = relative - along * Vec.Dot(relative, along);
        double crossing = range > 0.0 ? Vec.Len(across) / range * dt : 0.0;

        // One in fifteen, plus anything worse than has been seen. At 60 fps that is four lines a
        // second, which is enough to see a pattern and few enough to cost nothing.
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

    // The craft the view was pointed at, which is what every offset written below is measured
    // from. Held because the panel can move to another system without the view being released,
    // and an offset for one launcher applied to another craft's position places the eye wherever
    // the two happen to be apart -- which is not a small error between two sites on one world.
    private Vehicle? _followed;

    /// <summary>True while this holds the main view, which is what the chase has to outrank.</summary>
    public bool Holding => _saved.Valid;

    // Frames the restore has been attempted and refused. A scene change is the case: leaving
    // flight is exactly when the viewport may not be readable, and it is also the one moment the
    // view has to be handed back.
    private int _refusedFrames;

    // Long enough to cover a scene load, short enough that a viewport which is never coming back
    // does not leave this trying for the session.
    private const int GiveUpAfterFrames = 180;

    /// <summary>
    /// Lets go of the mode and the follow, for when the scene the recording describes has gone.
    ///
    /// <para>Leaving flight is the case. The recording names a camera mode and a craft to follow
    /// that belonged to the flight scene, and restoring them once the editor is up writes a dead
    /// scene's camera onto the live one — which is a view the player cannot account for and did
    /// not ask for. The new scene sets up its own camera, so neither is worth handing back; the
    /// same reason <see cref="ChaseCamera"/> drops its own recording when the player takes the
    /// view outright.</para>
    ///
    /// <para><b>The field of view is not the scene's, and is put back anyway.</b> It is a
    /// preference on a camera object that outlives every scene, so nothing resets it and no later
    /// borrower will ever hand it back — the next one records the magnified field as the player's
    /// own and magnifies again from there. The player cannot recover it either: their zoom keys
    /// clamp to 15°–120°, so the field they had is not reachable from any control they have. This
    /// is what keeps a wrong answer about the scene merely wrong instead of unrecoverable.</para>
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

        // The follow is classified here as well as at the stand-down rung, because this path is
        // reachable without Apply ever having seen the takeover: losing the head and switching
        // vessels on the same frame arrives here instead, and restoring then puts the player back
        // on the craft they just left. The mode needs no such test — a mode taken by hand stands
        // the sight down before it can reach this.
        //
        // A refused restore keeps the recording and tries again. Dropping it on the first attempt
        // is what strands the player: the view stays in Fixed mode at the optic's pose and field,
        // and the only description of what it was doing has been thrown away. Nothing is
        // recoverable after that, in any scene.
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
    /// What was done, so the caller can act on it. <see cref="ViewAction.StandDown"/> is the one
    /// that matters: releasing alone is not enough, because the setting still asks for the view
    /// and the next frame would take it straight back.
    /// </returns>
    public ViewAction Apply(IOpticalHead head, bool wanted, bool outranked, double magnification)
    {
        // Declared up front rather than in the condition: the resolve short-circuits, and the
        // drive below has to be reachable on the one path where it succeeded.
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
                // Held on paper, driven by the chase. The recording stays: it is the only route
                // back to what the player was doing, and the chase's own recording is of this
                // pose, so it hands the view straight back here when it finishes.
                return action;

            case ViewAction.Take when !Take(head):
                return ViewAction.Idle;

            case ViewAction.Idle:
                return action;
        }

        // The panel moved to a system on another craft without the view ever being released. The
        // recording still describes what the player was doing, so this re-points rather than
        // releasing -- but it has to happen before the offset below is written, because that
        // offset means nothing measured from the craft the view followed a moment ago.
        if (!ReferenceEquals(_followed, head.Platform) && !Follow(head.Platform)) return action;

        // The separation, not either position. Both terms come from this frame's one sample of the
        // craft the view now follows, so the difference carries no instant and the engine may
        // apply it whenever in its pass it likes.
        double3 offsetFromCraft = eye - head.PlatformEcl;

        // A refused write must not leave the view held: the player would be stranded wherever the
        // last good frame put them, in a mode they never chose.
        // Handing itself over as the pose source is what puts the aim in phase with the frame; the
        // values written here are the fallback the controller keeps if that ever refuses.
        _head = head;
        _magnification = magnification;

        // The field is not written here. It is part of the pose now, answered inside the engine's
        // viewport pass along with everything else, so a write from this hook would be a stale
        // copy of the same number one frame early.
        if (!KsaWorld.TryLookFromMainViewport(offsetFromCraft, forward, head.RollReferenceEcl,
                                              SightZoom.FovDegreesFor(_saved.FovDeg, magnification),
                                              this))
        {
            Release();
        }

        return action;
    }

    // What the engine reports about the view, against what this pointed it at. The rule is in
    // ViewClaim; only the two readings are here.
    private bool StillOurs(bool outranked)
        => ViewClaim.StillOurs(KsaWorld.MainViewIsFixed(),
                               KsaWorld.MainViewFollows(_followed), outranked);

    // Gives back whichever half of the view the player did not take for themselves.
    //
    // Symmetric on purpose. Whichever of the mode and the follow they changed is their decision and
    // is left alone; the other is still the mod's leavings and is put back. Leaving both would
    // strand them -- a vessel switch leaves Fixed standing, and Fixed is a mode no input can
    // leave -- and restoring both would drag them off the very thing they just chose.
    private void StandDown()
    {
        bool modeIsOurs = KsaWorld.MainViewIsFixed();
        bool followIsOurs = KsaWorld.MainViewFollows(_followed);

        KsaWorld.StopDrivingMainView();

        // Always. The field is the one thing neither half of a takeover restores and no zoom key
        // can return to, so it is handed back whoever took the view and however.
        KsaWorld.TrySetMainViewFov(_saved.FovDeg);

        if (followIsOurs && KsaWorld.CanFollow(_saved.Following)) KsaWorld.RestoreFollow(_saved);
        if (modeIsOurs) KsaWorld.RestoreMainViewMode(_saved);

        _saved = default;
        _followed = null;
        _head = null;
        _refusedFrames = 0;

        Log.Info($"sight: the view was taken over by hand ({(modeIsOurs ? "vessel" : "camera mode")}"
                 + "), standing down");
    }

    // Records what the view was doing, then points it at the launcher's craft. Both have to
    // succeed or neither counts: a follow swapped without a saved state has nothing to go back to.
    private bool Take(IOpticalHead head)
    {
        if (head.Platform is null) return false;

        _saved = KsaWorld.RememberMainView();
        if (!_saved.Valid)
        {
            // Said, not swallowed. A silent return is indistinguishable from the optic being
            // switched off, and looks like the sight simply never opening.
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

    // Points the view at a craft and records which one, so the offsets written against it are
    // known to be measured from the same place the engine will apply them to.
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
