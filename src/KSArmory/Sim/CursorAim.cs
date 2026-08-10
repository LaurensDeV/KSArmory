using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Turning a mouse cursor into something a drive can be pointed at.
///
/// <para>The conversion itself is one subtraction, and it is the whole reason this type exists:
/// ImGui reports the cursor in <b>screen</b> coordinates spanning every window, while a camera's
/// unprojection divides by <em>its own</em> framebuffer. Feeding one to the other works on a
/// single full-screen viewport and is wrong by the viewport's origin everywhere else — which is
/// invisible until someone opens a second view, and then reads as the gun aiming at a point
/// offset from the cursor by a fixed amount.</para>
///
/// <para>The scale into framebuffer pixels is insurance rather than a correction. In KSA it is
/// exactly one and cannot be otherwise: <c>Viewport.SetSize</c> assigns <c>Size</c> and calls
/// <c>Camera.Resize</c>, which assigns <c>FramebufferSize</c>, in the same statement, and there is
/// no render-scale path. It stays because an unprojection that divides by a framebuffer is
/// entitled to be given framebuffer pixels, and were the two ever to diverge the error would be
/// zero at the top-left corner and grow across the screen — which reads as "close, but not under
/// the pointer".</para>
///
/// <para>No KSA types: the caller supplies the rectangle and unprojects the answer.</para>
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
    /// <para>A direction on its own does not locate anything. The camera and the launcher stand
    /// metres or hundreds of metres apart, so a bearing taken at one is a bearing at the other
    /// only for something infinitely far away. Pointing a drive down the camera's direction is
    /// therefore exact against the sky and wrong by the whole parallax against anything near —
    /// which reads in game as a turret that follows the cursor faithfully above the horizon and
    /// points somewhere else entirely below it, where everything under the pointer is close.</para>
    ///
    /// <para>So both frame-carrying terms arrive here and the subtraction happens in one place,
    /// which is what <c>docs/FRAMES-AND-EPOCHS.md</c> asks of a <c>Sim/</c> entry point.</para>
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
    /// <para>An unprojection at a degenerate projection matrix returns a zero or non-finite
    /// direction, and normalising that yields NaN, which reaches the drive and then the round.
    /// NaN times zero is still NaN, so nothing downstream recovers.</para>
    /// </summary>
    public static bool IsUsableDirection(double3 direction)
        => Vec.IsFinite(direction) && Vec.Len(direction) > 1e-9;

    private static bool IsFinite(float2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);

    /// <summary>
    /// Whether the cursor is far enough from the middle of the view to be a command, and how far.
    ///
    /// <para><b>A dead zone is not a nicety here.</b> Pointing a head that is itself driving the
    /// picture is a feedback loop: the head turns towards the cursor, the view turns with the
    /// head, and the cursor stays off centre — so it keeps turning. Without a rest area a
    /// millimetre of offset is a standing order to drift, and the view never settles.</para>
    ///
    /// <para>The offset is returned unscaled, in pixels, because the caller draws it as well as
    /// acting on it and the two must be the same number or the indicator lies about when the head
    /// will move.</para>
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
    /// <para><b>Measured from the edge of the rest area, not from the middle of the view.</b> From
    /// the middle, a large rest area means the cursor is already far out the instant it leaves the
    /// ring, so the head goes from still to full rate in a pixel — the bigger the rest area, the
    /// worse the jolt, which is the opposite of what a rest area is for. From the edge, the
    /// command always starts at nothing wherever the ring is drawn.</para>
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
