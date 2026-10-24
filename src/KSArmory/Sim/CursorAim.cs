using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Turning a mouse cursor into something a drive can be pointed at.
///
/// <para>ImGui reports the cursor in <b>screen</b> coordinates spanning every window, while a camera's
/// unprojection divides by <em>its own</em> framebuffer, so the cursor is offset into the viewport.</para>
///
/// <para>The scale into framebuffer pixels is one in KSA today (<c>GameViewport.SetSize</c> resizes its
/// cameras to the viewport's size) and kept because the unprojection is owed framebuffer pixels.</para>
/// </summary>
public static class CursorAim
{
    /// <summary>
    /// The cursor in the viewport's own coordinates, or false when it is not over the viewport.
    /// </summary>
    /// <param name="cursorScreen">Cursor position as ImGui reports it, across all windows.</param>
    /// <param name="viewportOrigin">Top-left of the viewport in the same screen coordinates.</param>
    /// <param name="width">Viewport width in pixels.</param>
    /// <param name="height">Viewport height in pixels.</param>
    /// <param name="local">Cursor relative to the viewport's top-left.</param>
    public static bool TryToViewport(float2 cursorScreen, float2 viewportOrigin,
                                     int width, int height, out float2 local)
    {
        local = default;

        if (width <= 0 || height <= 0) return false;
        if (!IsFinite(cursorScreen) || !IsFinite(viewportOrigin)) return false;

        float x = cursorScreen.X - viewportOrigin.X;
        float y = cursorScreen.Y - viewportOrigin.Y;

        // Exclusive at the far edge: a cursor at exactly width unprojects to NDC +1, which is the
        // frustum boundary rather than a point inside it.
        if (x < 0f || y < 0f || x >= width || y >= height) return false;

        local = new float2(x, y);
        return true;
    }

    /// <summary>
    /// The cursor in the camera's framebuffer coordinates: offset into the viewport, then scaled
    /// from viewport pixels into framebuffer pixels.
    /// </summary>
    /// <param name="framebufferWidth">Width the camera unprojects against, which is what matters.</param>
    /// <param name="framebufferHeight">Height the camera unprojects against.</param>
    public static bool TryToFramebuffer(float2 cursorScreen, float2 viewportOrigin,
                                        int width, int height,
                                        int framebufferWidth, int framebufferHeight,
                                        out float2 local)
    {
        local = default;

        if (framebufferWidth <= 0 || framebufferHeight <= 0) return false;
        if (!TryToViewport(cursorScreen, viewportOrigin, width, height, out float2 inViewport))
        {
            return false;
        }

        local = new float2(inViewport.X * framebufferWidth / width,
                           inViewport.Y * framebufferHeight / height);
        return true;
    }

    /// <summary>
    /// Where the cursor points, seen from the <em>mount</em> rather than from the camera.
    ///
    /// <para>The camera's bearing is the mount's only for something infinitely far away, so the ray is
    /// resolved to a point first; both frame-carrying terms are differenced here
    /// (<c>docs/FRAMES-AND-EPOCHS.md</c>).</para>
    /// </summary>
    /// <param name="rangeMetres">How far along the ray the thing under the cursor is taken to be.</param>
    public static bool TryAimFromMount(double3 rayOriginEcl, double3 rayDirectionEcl,
                                       double rangeMetres, double3 mountEcl,
                                       out double3 directionEcl)
    {
        directionEcl = default;

        if (!Vec.IsFinite(rayOriginEcl) || !Vec.IsFinite(mountEcl)) return false;
        if (!IsUsableDirection(rayDirectionEcl)) return false;
        if (!double.IsFinite(rangeMetres) || rangeMetres <= 0.0) return false;

        double3 aimed = rayOriginEcl + Vec.Unit(rayDirectionEcl) * rangeMetres - mountEcl;
        if (!IsUsableDirection(aimed)) return false;

        directionEcl = Vec.Unit(aimed);
        return true;
    }

    /// <summary>
    /// Whether an aim direction is usable — finite and long enough to normalise.
    ///
    /// <para>A degenerate projection unprojects to zero or NaN, and nothing downstream recovers from a
    /// NaN aim.</para>
    /// </summary>
    public static bool IsUsableDirection(double3 direction)
        => Vec.IsFinite(direction) && Vec.Len(direction) > 1e-9;

    private static bool IsFinite(float2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);

    /// <summary>
    /// Whether the cursor is far enough from the middle of the view to be a command, and how far.
    ///
    /// <para>A head driving the picture it is pointed from is a feedback loop: the cursor stays off
    /// centre as the view turns, so without a rest area the view never settles. The offset is returned
    /// in pixels because the caller both draws and acts on it.</para>
    /// </summary>
    public static bool OutsideDeadZone(float2 cursor, float2 centre, float deadZonePx,
                                       out float2 fromCentre)
    {
        fromCentre = default;

        if (!float.IsFinite(cursor.X) || !float.IsFinite(cursor.Y)) return false;
        if (!float.IsFinite(centre.X) || !float.IsFinite(centre.Y)) return false;

        float dx = cursor.X - centre.X;
        float dy = cursor.Y - centre.Y;

        fromCentre = new float2(dx, dy);

        float radius = float.IsFinite(deadZonePx) ? Math.Max(0f, deadZonePx) : 0f;

        return MathF.Sqrt(dx * dx + dy * dy) > radius;
    }

    /// <summary>
    /// How hard the cursor is commanding: nothing at the edge of the rest area, everything
    /// <paramref name="fullAtPx"/> beyond it.
    ///
    /// <para>Measured from the edge of the rest area, so the command starts at nothing however large the
    /// ring is.</para>
    /// </summary>
    public static double CommandStrength(float2 fromCentre, float deadZonePx, float fullAtPx)
    {
        if (!float.IsFinite(fromCentre.X) || !float.IsFinite(fromCentre.Y)) return 0.0;

        double distance = Math.Sqrt((double)fromCentre.X * fromCentre.X
                                    + (double)fromCentre.Y * fromCentre.Y);

        double rest = float.IsFinite(deadZonePx) ? Math.Max(0.0, deadZonePx) : 0.0;
        double span = float.IsFinite(fullAtPx) ? Math.Max(1.0, fullAtPx) : 1.0;

        return Math.Clamp((distance - rest) / span, 0.0, 1.0);
    }
}
