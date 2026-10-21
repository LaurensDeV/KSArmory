using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using KSA.Rendering.Water.Data;

namespace KSArmory;

internal static partial class KsaWorld
{
    /// <summary>
    /// How large something at <paramref name="atEcl"/> appears on screen, in pixels.
    ///
    /// <para>Measured by projecting a point one radius to the camera's right rather than by
    /// reconstructing the field of view: the projection already knows the lens, and asking it
    /// twice cannot disagree with itself.</para>
    /// </summary>
    public static bool TryApparentRadiusPixels(double3 atEcl, double metres, out float pixels)
    {
        pixels = 0f;
        if (!double.IsFinite(metres) || metres <= 0.0) return false;

        try
        {
            if (Program.GetMainCamera() is not { } camera) return false;

            double3 right = camera.GetRightEcl();
            if (!Vec.IsFinite(right) || Vec.Len(right) < 0.5) return false;

            if (!TryProjectAhead(atEcl, out float2 centre)) return false;
            if (!TryProjectAhead(atEcl + Vec.Unit(right) * metres, out float2 edge)) return false;

            float dx = edge.X - centre.X, dy = edge.Y - centre.Y;
            pixels = MathF.Sqrt(dx * dx + dy * dy);
            return float.IsFinite(pixels);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Projects a world point onto the main viewport, culling anything behind the camera.
    ///
    /// <para>Distinct from <see cref="TryProjectIntoViewport"/>, which passes
    /// <c>ignoreBehind: false</c>. That is right for the gunner's sight, whose head is pointed at
    /// its target and so cannot be looking away from it, and wrong for a marker over an arbitrary
    /// craft: <c>EgoToScreen</c> only tests the point against the camera's forward when asked, so
    /// without it a site *behind* the camera draws a bracket in front of it.</para>
    /// </summary>
    public static bool TryProjectAhead(double3 pointEcl, out float2 screen)
    {
        screen = default;
        try
        {
            if (Program.MainViewport is not { } viewport) return false;
            if (viewport.GetCamera() is not { } camera) return false;
            if (viewport.Width <= 0 || viewport.Height <= 0) return false;

            float2 local = camera.EclToScreen(pointEcl, ignoreBehind: true);
            if (!float.IsFinite(local.X) || !float.IsFinite(local.Y)) return false;
            if (local.X < 0f || local.Y < 0f || local.X > viewport.Width || local.Y > viewport.Height)
            {
                return false;
            }

            screen = new float2(viewport.Position.X + local.X, viewport.Position.Y + local.Y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// An Ego point on the main viewport, off screen or not; false only behind the camera. For a
    /// line drawn through the world, which ImGui clips to the screen itself.
    /// </summary>
    public static bool TryProjectEgo(double3 posEgo, out float2 screen)
    {
        screen = default;
        try
        {
            if (Program.MainViewport is not { } viewport) return false;
            if (viewport.GetCamera() is not { } camera) return false;

            float2 local = camera.EgoToScreen(posEgo, ignoreBehind: true);
            if (!float.IsFinite(local.X) || !float.IsFinite(local.Y)) return false;

            screen = new float2(viewport.Position.X + local.X, viewport.Position.Y + local.Y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where a world point sits on screen, and whether it is actually in view.
    ///
    /// <para>For a point that is off-screen or behind, <paramref name="screen"/> comes back
    /// clamped to the edge of the viewport in the direction of the target, so a caller can draw
    /// an indicator pointing at something it cannot see. That is the difference from
    /// <see cref="TryProjectAhead"/>, which simply refuses.</para>
    ///
    /// <para>A point behind the camera has to be handled by hand: a projection matrix maps it to
    /// the *opposite* side of the screen, so an arrow built from it points exactly the wrong way.
    /// The camera basis is public, so the bearing is taken from the direction to the target
    /// against the camera's own right and up, which is well defined for any point that is not
    /// exactly on the axis.</para>
    /// </summary>
    public static bool TryProjectOrClamp(double3 pointEcl, out float2 screen, out bool inView)
    {
        screen = default;
        inView = false;
        try
        {
            if (Program.MainViewport is not { } viewport) return false;
            if (viewport.GetCamera() is not { } camera) return false;

            if (viewport.Width <= 0 || viewport.Height <= 0) return false;

            double3 toTarget = pointEcl - camera.EgoToEcl(Vec.Zero);
            bool ahead = Vec.Dot(toTarget, camera.GetForwardEcl()) > 0.0;
            float2 local = ahead ? camera.EclToScreen(pointEcl, ignoreBehind: true) : default;

            return OnScreenOrEdge(viewport, camera, toTarget, ahead, local, out screen, out inView);
        }
        catch
        {
            return false;
        }
    }

    // Keeps an edge indicator clear of the very border, where it would be half off-screen.
    private const float EdgeMargin = 28f;

    /// <summary>
    /// Where a world point lands on one camera window, <em>including off the edge of it</em>.
    ///
    /// <para>The window is named rather than assumed. A sight painting the main view's projection
    /// onto a secondary window puts its whole picture in the wrong place at the wrong scale, and
    /// looks from outside exactly like a camera that is not being driven.</para>
    ///
    /// <para>For a shape whose ends are outside the picture but whose middle is inside — the
    /// sight's horizontal reference is the case, and it is the whole shape. Rejecting a point for
    /// being off-screen throws the line away exactly when it spans the view, which is always: the
    /// reference reaches past both edges by design, and at 3° of field it reaches a long way past.
    /// The draw list clips what it is given, so out-of-bounds coordinates are the caller's
    /// friend.</para>
    ///
    /// <para>A point <em>behind</em> the camera is still refused, because a projection maps it to
    /// the opposite side of the screen and a line drawn to it runs the wrong way.</para>
    /// </summary>
    public static bool TryProjectUnbounded(int index, double3 pointEcl, out float2 screen)
    {
        screen = default;
        try
        {
            if (!TryViewport(index, out IGameViewport viewport)) return false;
            if (viewport.GetCamera() is not { } camera) return false;

            // ignoreBehind answers NaN rather than a mirrored point, which is the refusal.
            float2 local = camera.EclToScreen(pointEcl, ignoreBehind: true);
            if (!float.IsFinite(local.X) || !float.IsFinite(local.Y)) return false;

            screen = new float2(viewport.Position.X + local.X, viewport.Position.Y + local.Y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The same projection, for a position already in the render frame.
    ///
    /// <para>For anything drawn <em>over</em> a craft rather than beside it. A craft's analytic
    /// position and the place its mesh is drawn differ by metres on the ground, which is noise
    /// against a 22 m fuse and is the entire budget for a bracket: at 3° of field a metre is
    /// several pixels, so a sight fed the analytic position sits visibly off the target it is
    /// supposed to be on. <see cref="TryVehicleEgo"/> is where the drawn position comes from.</para>
    /// </summary>
    public static bool TryProjectEgoOrClamp(int index, double3 posEgo, out float2 screen,
                                            out bool inView)
    {
        screen = default;
        inView = false;
        try
        {
            if (!TryViewport(index, out IGameViewport viewport)) return false;
            if (viewport.GetCamera() is not { } camera) return false;

            int w = viewport.Width, h = viewport.Height;
            if (w <= 0 || h <= 0) return false;

            // Ego is measured from the camera that sampled it, and everything upstream samples
            // against the main one -- so it is a *position* for that camera alone. Handing it to
            // another window's camera displaces the bracket by however far the two cameras are
            // apart, which is a few degrees at a kilometre and unbounded when the player pulls
            // their view back. Re-based through Ecl, which is the frame both agree in.
            if (!ReferenceEquals(camera, Program.GetMainCamera()))
            {
                if (Program.GetMainCamera() is not { } sampled) return false;

                posEgo = camera.EclToEgo(sampled.EgoToEcl(posEgo));
                if (!Vec.IsFinite(posEgo)) return false;
            }

            // Ego is camera-relative, so the separation to the target *is* the position. The
            // basis is read in Ecl because Ego is a pure translation of it and the two agree
            // exactly for a direction.
            bool ahead = Vec.Dot(posEgo, camera.GetForwardEcl()) > 0.0;
            float2 local = ahead ? camera.EgoToScreen(posEgo, ignoreBehind: true) : default;

            return OnScreenOrEdge(viewport, camera, posEgo, ahead, local, out screen, out inView);
        }
        catch
        {
            return false;
        }
    }

    // Off-screen or behind, the edge position comes from the bearing against the camera basis
    // rather than the projection, which throws a point behind the camera to the opposite side.
    private static bool OnScreenOrEdge(IGameViewport viewport, Camera camera, double3 fromCamera,
                                       bool ahead, float2 local, out float2 screen, out bool inView)
    {
        int w = viewport.Width, h = viewport.Height;
        inView = false;

        if (ahead && float.IsFinite(local.X) && float.IsFinite(local.Y)
            && local.X >= 0f && local.Y >= 0f && local.X <= w && local.Y <= h)
        {
            inView = true;
            screen = new float2(viewport.Position.X + local.X, viewport.Position.Y + local.Y);
            return true;
        }

        screen = default;
        double right = Vec.Dot(fromCamera, camera.GetRightEcl());
        double up = Vec.Dot(fromCamera, camera.GetUpEcl());
        if (!EdgeCue.TryOffset(right, up, w, h, EdgeMargin, out double dx, out double dy)) return false;

        screen = new float2((float)(viewport.Position.X + w * 0.5 + dx),
                            (float)(viewport.Position.Y + h * 0.5 + dy));
        return true;
    }
}
