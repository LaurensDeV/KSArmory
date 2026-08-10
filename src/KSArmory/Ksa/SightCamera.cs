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

    // Whether the picture is held level, from the last frame the panel was read.
    private bool _stabilise = true;

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
        // Zero is the contract for "no opinion", which hands the roll back to KSA's own rule.
        upEcl = _stabilise ? head.Boresight : Vec.Zero;
        fovDeg = SightZoom.FovDegreesFor(_saved.FovDeg, _magnification);

        return true;
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
    /// Lets go without writing anything, for when the scene the recording describes has gone.
    ///
    /// <para>Leaving flight is the case. The recording names a camera mode and a craft to follow
    /// that belonged to the flight scene, and restoring them once the editor is up writes a dead
    /// scene's camera onto the live one — which is a view the player cannot account for and did
    /// not ask for. The new scene sets up its own camera, so there is nothing here worth handing
    /// back; the same reason <see cref="ChaseCamera"/> drops its own recording when the player
    /// takes the view outright.</para>
    /// </summary>
    public void Forget()
    {
        if (!_saved.Valid) return;

        _saved = default;
        _followed = null;
        _head = null;
        _refusedFrames = 0;
        Log.Info("sight: the scene changed, letting go of the main view without restoring it");
    }

    /// <summary>Hands the view back, if it was taken. Safe to call at any time.</summary>
    public void Release()
    {
        if (!_saved.Valid) return;

        // A refused restore keeps the recording and tries again. Dropping it on the first attempt
        // is what strands the player: the view stays in Fixed mode at the optic's pose and field,
        // and the only description of what it was doing has been thrown away. Nothing is
        // recoverable after that, in any scene.
        bool mode = KsaWorld.BeginRestoreMainView(_saved);
        bool follow = _saved.Following is null || KsaWorld.RestoreFollow(_saved);

        if (!mode || !follow)
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
    public ViewAction Apply(IOpticalHead head, bool wanted, bool outranked, double magnification,
                            bool stabilise)
    {
        // Declared up front rather than in the condition: the resolve short-circuits, and the
        // drive below has to be reachable on the one path where it succeeded.
        double3 eye = Vec.Zero;
        double3 forward = Vec.Zero;

        bool resolved = head.Platform is not null
                        && head.OpticPart is not null
                        && head.TryOpticViewEcl(out eye, out forward);

        ViewAction action = ViewClaim.ForOptic(wanted, resolved, outranked, Holding,
                                               KsaWorld.MainViewIsFixed());

        switch (action)
        {
            case ViewAction.StandDown:
                // The mode is the player's and stays as they set it. The *follow* and the field of
                // view are the mod's to change and are put back, or they are left orbiting a
                // launcher they never chose to look at, through a three-degree straw.
                KsaWorld.TrySetMainViewFov(_saved.FovDeg);
                KsaWorld.RestoreFollow(_saved);
                _saved = default;
                _followed = null;
                _head = null;
                Log.Info("sight: the view was taken over by hand, standing down");
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
        _stabilise = stabilise;

        // The field is not written here. It is part of the pose now, answered inside the engine's
        // viewport pass along with everything else, so a write from this hook would be a stale
        // copy of the same number one frame early.
        if (!KsaWorld.TryLookFromMainViewport(offsetFromCraft, forward,
                                              _stabilise ? head.Boresight : Vec.Zero,
                                              SightZoom.FovDegreesFor(_saved.FovDeg, magnification),
                                              this))
        {
            Release();
        }

        return action;
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
