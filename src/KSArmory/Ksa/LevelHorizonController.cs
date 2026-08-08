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

    public override void OnFrame(Viewport inViewport, double inDeltaTime)
    {
        double3 forward = Vec.Unit(CameraRotation);
        double3 up = Vec.Unit(UpEcl);

        // Nothing to improve on: no up given, or a view so nearly along it that there is no
        // sideways left to build a basis from. The engine's rule is at least defined there.
        if (Vec.Len2(up) < 0.5 || Vec.Len2(forward) < 0.5 || Math.Abs(Vec.Dot(forward, up)) > 0.9995)
        {
            base.OnFrame(inViewport, inDeltaTime);
            return;
        }

        if (Camera.Following is not { } following) return;

        // The same two writes the engine makes, in the same order, with the up substituted.
        // LookAtRotation orthogonalises the pair itself, so up is a reference rather than a
        // constraint and need not be perpendicular to the view.
        Camera.PositionEcl = following.GetPositionEcl() + CameraOffset;
        Camera.LocalRotation = Camera.LookAtRotation(forward, up);
    }
}
