using Brutal.GlfwApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// KSA's fixed camera controller, with the one thing it does not offer: an up vector.
///
/// <para><c>FixedController</c> derives up from the camera reference frame's +Z, and
/// <c>GetFrame2Ecl</c> gives any followable that is not a <c>Vehicle</c>, a <c>Celestial</c> or an
/// editing space the Identity frame. <see cref="RoundFollowable"/> is one, so the horizon arrives
/// rolled by the site's angle from the ecliptic pole. Following the launching craft instead pairs a
/// frame-newer platform position with an older offset: ~500 m a frame.</para>
///
/// <para>Subclassing is ordinary, but installing it writes <c>GameViewport</c>'s backing field by
/// reflection, the one part <c>docs/KSA-API-SURFACE.md</c> cannot cover. If that fails, KSA's own
/// controller stays: no levelled horizon, and no <see cref="Pose"/>, so the sight's aim is written a
/// frame early.</para>
/// </summary>
internal sealed class LevelHorizonController(Camera camera) : FixedController(camera)
{
    /// <summary>Which way is up, in Ecl. Zero hands the frame back to the engine's own rule.</summary>
    public double3 UpEcl;

    /// <summary>Asked where the view should be, at the instant the answer is used. Null keeps the fields.</summary>
    public IViewPose? Pose;

    // The up used last frame, orthogonal to that frame's view: keeps the roll continuous where the
    // wanted up is singular.
    private double3 _lastUp;

    /// <summary>
    /// Drop everything carried from frame to frame, for a view that is being handed back.
    ///
    /// <para>The controller stays installed for the session, so otherwise the next borrower resumes a
    /// stale roll and the probe reports the take as a jump.</para>
    /// </summary>
    public void Forget()
    {
        _lastUp = Vec.Zero;
        _probed = false;
        _grossReported = 0;
    }

    /// <summary>
    /// Tell the probe the next frame is a cut, so it does not report the jump as a fault. The roll
    /// carried between frames is kept: a cut that looks along the same bearing wants it.
    /// </summary>
    public void ExpectCut() => _probed = false;

    // How fast a levelled picture rights itself (rad/s). See the correction in OnFrame.
    private const double LevelRateRad = Math.PI;

    // Asked of the pose source, so only a borrower whose view can be looked around takes the input.
    private ChaseOrbit? Orbit => Pose?.Orbit;

    public override bool OnMouseButton(GlfwWindow window, GlfwMouseButton button, GlfwButtonAction action,
                                       GlfwModifier mods)
    {
        if (Orbit is not { } orbit || button != GlfwMouseButton.Number2)
        {
            return base.OnMouseButton(window, button, action, mods);
        }

        // The modifiers KSA's orbit camera starts a drag on, so a chord it leaves alone stays alone.
        if (action == GlfwButtonAction.Press && (mods == 0 || (mods & GlfwModifier.Shift) != 0)) orbit.Press();
        else if (action == GlfwButtonAction.Release) orbit.Release();

        return true;
    }

    public override bool OnCursorPos(GlfwWindow window, double2 pos)
    {
        if (Orbit is not { } orbit) return base.OnCursorPos(window, pos);

        orbit.Move(pos.X, pos.Y);
        return orbit.Dragging;
    }

    public override bool OnScroll(GlfwWindow window, double2 offset)
    {
        if (Orbit is not { } orbit) return base.OnScroll(window, offset);

        orbit.Scroll(offset.Y);
        return true;
    }

    // Load-bearing: the engine asks the active controller this before a right-release opens the
    // window of whatever part is under the cursor, so a drag ending over a craft opens nothing.
    public override bool IsMouseDrag() => Orbit?.Dragging == true || base.IsMouseDrag();

    // Hidden and unbounded while dragging, as the orbit camera's is, so a drag does not stop at the
    // edge of the screen.
    public override GlfwCursorMode GetCursorMode()
        => Orbit?.Dragging == true ? GlfwCursorMode.Disabled : base.GetCursorMode();

    public override void OnFrame(IViewport inViewport, double inDeltaTime)
    {
        AskThePoseSource();

        double3 forward = Vec.Unit(CameraRotation);

        // The previous frame's answer is carried through the singularity rather than handing back to
        // the engine's rule, which disagrees by half a turn there. See SightPicture.TryStableUp.
        // No up at all is KSA's own rule, and forgets the carried one so it is not resumed stale.
        if (Vec.Len2(Vec.Unit(UpEcl)) < 0.5)
        {
            Forget();
            base.OnFrame(inViewport, inDeltaTime);
            return;
        }

        // How fast the wanted up may pull the carried one: 180 deg/s, three degrees a frame at 60 fps.
        double step = LevelRateRad * Math.Clamp(inDeltaTime, 0.0, 0.1);

        if (!SightPicture.TryStableUp(forward, UpEcl, _lastUp, step, out double3 up))
        {
            base.OnFrame(inViewport, inDeltaTime);
            return;
        }

        Probe(forward, up);
        _lastUp = up;

        if (Camera.Following is not { } following) return;

        // The engine's two writes, in its order, with the up substituted. LookAtRotation
        // orthogonalises the pair, so up need not be perpendicular to the view.
        Camera.PositionEcl = following.GetPositionEcl() + CameraOffset;
        Camera.LocalRotation = Camera.LookAtRotation(forward, up);
    }

    // The camera's roll and where it points, logged only when something jumps between two frames.
    private double3 _probeForward;
    private double3 _probeUp;
    private double3 _probeWanted;
    private bool _probed;
    private int _grossReported;

    // A rate-limited head cannot roll this far in a frame, so past this it is a fault, logged in
    // any log rather than only a verbose one.
    private const double GrossRollJump = 0.35;

    private void Probe(double3 forward, double3 up)
    {
        if (_probed)
        {
            double turned = Vec.AngleBetween(_probeForward, forward);
            double rolled = Vec.AngleBetween(_probeUp, up);

            // Aim and roll reported together: which jumped says which half is at fault.
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

                // Rate-limited, so a run of jumps is one line rather than sixty a second.
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

    // Re-reads the pose here, the only moment in phase with the matrices the scene is drawn through:
    // from the GUI hook, after this pass, the aim is a frame of the target's angular motion late.
    // Refusals and faults leave the fields alone. Inside the engine's loop, so nothing may throw.
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
        }
    }
}

/// <summary>
/// Where the view should be, asked at the instant the engine uses the answer rather than a frame
/// before it.
///
/// <para>StarMap's per-frame hooks all run after the viewport pass has built the frame's matrices.</para>
/// </summary>
internal interface IViewPose
{
    /// <param name="followedEcl">
    /// Where the followed craft is now, from the engine. The offset must be measured against this:
    /// a GUI-hook sample of the same craft carries a frame of its motion.
    /// </param>
    /// <param name="fovDeg">
    /// The field this borrower wants, every frame: one that says nothing inherits the last one's,
    /// which from the sight can be 3°.
    /// </param>
    bool TryPose(double3 followedEcl, out double3 offsetFromFollowed, out double3 forwardEcl,
                 out double3 upEcl, out double fovDeg);

    /// <summary>
    /// The player looking around this view, or null. Non-null hands the right button and the wheel
    /// to it rather than to the engine.
    /// </summary>
    ChaseOrbit? Orbit { get; }
}
