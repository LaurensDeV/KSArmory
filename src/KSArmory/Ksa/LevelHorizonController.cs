using Brutal.Numerics;
using KSA;
using KSArmory.Sim;

namespace KSArmory;

/// <summary>
/// KSA's fixed camera controller, with the one thing it does not offer: an up vector.
///
/// <para><b>Why this exists.</b> <c>FixedController</c> derives the camera's up by crossing the
/// view direction with the camera reference frame's +Z, and <c>GetFrame2Ecl</c> dispatches on the
/// followed object's <em>type</em> — a followable that is not a <c>Vehicle</c>, a <c>Celestial</c>
/// or an editing space gets the Identity frame and its declared <c>CameraReferenceFrame</c> is not
/// read at all. <see cref="RoundFollowable"/> is one of those, so the axis is ecliptic +Z and the
/// horizon arrives rolled by the site's angle from the ecliptic pole — 60° of roll at a site 60°
/// off it, snapping to that the instant the view is taken. Nothing on <c>Camera</c>,
/// <c>OrbitView</c> or the controller offers a way to say otherwise.</para>
///
/// <para><b>Why not follow the launching craft instead</b>, whose frame would give local vertical:
/// <c>PrepareFrame</c> advances every vehicle's position before the viewport pass, while a round's
/// is integrated after it. The engine would add a frame-newer platform position to an offset
/// measured against the older one, and at 29.8 km/s that is ~500 m of camera displacement per
/// frame. Avoiding exactly that is what <see cref="RoundFollowable"/> is for.</para>
///
/// <para><b>What this depends on, and what happens when it stops being true.</b>
/// <c>Viewport.FixedController</c> is a public writable field, <c>FixedController</c> is public and
/// unsealed with a public constructor, and <c>OnFrame</c> is virtual — so this is ordinary
/// subclassing rather than patching. It is still an extension point nobody promised: it is bound
/// through <c>docs/KSA-API-SURFACE.md</c>, so a signature change is caught by
/// <c>tools/ksa-api-diff.sh</c>, and if this class ever cannot be installed the engine's own
/// controller stays in place and the roll comes back. Nothing else breaks.</para>
/// </summary>
internal sealed class LevelHorizonController(Camera camera) : FixedController(camera)
{
    /// <summary>
    /// Which way is up, in Ecl. Zero hands the frame back to the engine's own rule, so a caller
    /// that has no opinion gets exactly the stock behaviour.
    /// </summary>
    public double3 UpEcl;

    /// <summary>
    /// Somewhere to ask where the view should be, at the instant the answer is used. Null keeps
    /// the fields, which is what everything but the sight wants.
    /// </summary>
    public IViewPose? Pose;

    // The up actually used last frame, already orthogonal to that frame's view. What keeps the
    // roll continuous where the wanted up is no use.
    private double3 _lastUp;

    /// <summary>
    /// Drop everything carried from frame to frame, for a view that is being handed back.
    ///
    /// <para>The controller stays installed for the session, so without this a borrower taking the
    /// view again resumes a roll from the last engagement and the probe reports the take itself as
    /// a jump — the one warning that must not cry wolf.</para>
    /// </summary>
    public void Forget()
    {
        _lastUp = Vec.Zero;
        _probed = false;
        _grossReported = 0;
    }

    // How fast a levelled picture rights itself (rad/s). See the correction in OnFrame.
    private const double LevelRateRad = Math.PI;

    public override void OnFrame(Viewport inViewport, double inDeltaTime)
    {
        AskThePoseSource();

        double3 forward = Vec.Unit(CameraRotation);

        // The previous frame's answer is carried through the singularity rather than handing back
        // to the engine's rule there. Switching rule is what flips the picture: a view along its
        // own up has no roll, so the two conventions disagree by half a turn, and creeping past
        // that point swaps between them. See SightPicture.TryStableUp.
        // No up given at all is the caller saying it has no opinion, which is KSA's own rule and
        // not a singular case to carry through. Forgetting the last one matters: keeping it would
        // resume a stale roll the moment stabilising is switched back on.
        if (Vec.Len2(Vec.Unit(UpEcl)) < 0.5)
        {
            Forget();
            base.OnFrame(inViewport, inDeltaTime);
            return;
        }

        // How fast the wanted up may pull the carried one. Fast enough that levelling looks
        // immediate on any ordinary slew, slow enough that it cannot snap: at 180 deg/s a frame
        // moves it three degrees, which is below what anyone sees as a jump.
        double step = LevelRateRad * Math.Clamp(inDeltaTime, 0.0, 0.1);

        if (!SightPicture.TryStableUp(forward, UpEcl, _lastUp, step, out double3 up))
        {
            base.OnFrame(inViewport, inDeltaTime);
            return;
        }

        Probe(forward, up);
        _lastUp = up;

        if (Camera.Following is not { } following) return;

        // The same two writes the engine makes, in the same order, with the up substituted.
        // LookAtRotation orthogonalises the pair itself, so up is a reference rather than a
        // constraint and need not be perpendicular to the view.
        Camera.PositionEcl = following.GetPositionEcl() + CameraOffset;
        Camera.LocalRotation = Camera.LookAtRotation(forward, up);
    }

    // The camera's roll and where it points, per frame, under a verbose log.
    //
    // A snap is a jump between two consecutive frames, which is the one thing nobody watching can
    // measure and nothing else records: by the time it is seen it is over, and every quantity that
    // decides it has already moved on. Logged only when something actually jumps, so a session
    // produces a handful of lines at the moments that matter rather than one per frame.
    private double3 _probeForward;
    private double3 _probeUp;
    private double3 _probeWanted;
    private bool _probed;
    private int _grossReported;

    // A single frame cannot roll this far honestly: the head is rate-limited and the camera is
    // built from it. Past this it is a fault, and a fault worth a line in *any* log -- a
    // diagnostic that only records when someone remembered to switch it on records nothing on
    // the run that mattered, which is what happened the first time this was asked for.
    private const double GrossRollJump = 0.35;

    private void Probe(double3 forward, double3 up)
    {
        if (_probed)
        {
            double turned = Vec.AngleBetween(_probeForward, forward);
            double rolled = Vec.AngleBetween(_probeUp, up);

            // A rate-limited head cannot move far in a frame, so anything past a few degrees is a
            // discontinuity rather than motion. The two are reported together because which of
            // them jumped says which half is at fault: the aim, or the roll built on it.
            bool gross = rolled > GrossRollJump || turned > GrossRollJump;

            if (gross || rolled > 0.09 || turned > 0.09)
            {
                string line =
                    $"sight roll: turned {double.RadiansToDegrees(turned):F2} deg, "
                    + $"rolled {double.RadiansToDegrees(rolled):F2} deg | "
                    + $"fwd {forward.X:F3},{forward.Y:F3},{forward.Z:F3} "
                    + $"up {up.X:F3},{up.Y:F3},{up.Z:F3} "
                    + $"wanted {_probeWanted.X:F3},{_probeWanted.Y:F3},{_probeWanted.Z:F3} "
                    + $"| dot(fwd,wanted) {Vec.Dot(forward, Vec.Unit(UpEcl)):F5}";

                // Rate-limited rather than gated on a switch, so a gross jump is always on record
                // and a run of them is still one line rather than sixty a second.
                if (gross && _grossReported < 12)
                {
                    _grossReported++;
                    Log.Warn(line);
                }
                else if (Log.Threshold <= Log.Level.Debug)
                {
                    Log.Debug(() => line);
                }
            }
        }

        _probeForward = forward;
        _probeUp = up;
        _probeWanted = Vec.Unit(UpEcl);
        _probed = true;
    }

    // Re-reads the pose here, which is the only moment in the frame that is in phase with the
    // matrices the scene is drawn through.
    //
    // Everything else the mod does runs from the GUI hook, a postfix on OnDrawUiViewports -- after
    // this pass. A camera aimed from there is consumed on the *next* frame, so the view is drawn
    // along a direction solved one frame ago while the target is drawn where it is now. The gap is
    // one frame of the target's angular motion, and it scales with simulation speed: invisible
    // paused, a couple of pixels at 1x, and a third of the picture at 16x on a close target.
    //
    // Refusals and faults leave the fields alone, so the worst case is the pose the GUI hook
    // wrote. This runs inside the engine's own loop, which is the whole point and also why
    // nothing here may throw.
    private void AskThePoseSource()
    {
        if (Pose is not { } source) return;
        if (Camera.Following is not { } following) return;

        try
        {
            if (!source.TryPose(following.GetPositionEcl(), out double3 offset,
                                out double3 forward, out double3 up, out double fovDeg))
            {
                return;
            }

            if (!Vec.IsFinite(offset) || Vec.Len2(forward) < 0.5) return;

            CameraOffset = offset;
            CameraRotation = forward;
            UpEcl = up;

            // Clamped rather than refused. The engine's own setter does not clamp and the
            // projection throws outside (0, pi) -- from in here, which is the engine's loop.
            KsaWorld.TrySetMainViewFov(fovDeg);
        }
        catch
        {
            // Keep whatever was written a frame ago rather than dropping the view.
        }
    }
}

/// <summary>
/// Where the view should be, asked at the instant the engine uses the answer rather than a frame
/// before it.
///
/// <para>The one thing that cannot be done from a GUI hook, because all three of StarMap's hooks
/// run after the viewport pass has already built the frame's matrices.</para>
/// </summary>
internal interface IViewPose
{
    /// <param name="followedEcl">
    /// Where the followed craft is <em>now</em>, from the engine. The offset must be measured
    /// against this and nothing else: a separation taken from a sample of the same craft made in
    /// the GUI hook carries a frame of its motion, which is the fault this interface exists to
    /// remove rather than a second copy of it.
    /// </param>
    /// <param name="fovDeg">
    /// The field this borrower wants, every frame and without exception. Part of the pose rather
    /// than something set on the side, because a borrower that says nothing about the field
    /// inherits whatever the last one left — and the sight leaves 3°, so a chase that inherits it
    /// flies down a straw. Answering the field it wants is the only way not to have an opinion.
    /// </param>
    bool TryPose(double3 followedEcl, out double3 offsetFromFollowed, out double3 forwardEcl,
                 out double3 upEcl, out double fovDeg);
}
