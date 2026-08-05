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
/// <para>And the viewport's pixels are not necessarily the camera's. The unprojection divides by
/// <c>FramebufferSize</c>, which a render scale or a display scale can make a different size from
/// the window it is shown in — so the cursor has to be scaled into framebuffer pixels as well as
/// offset into the viewport. Getting only the offset right leaves an error that is zero at the
/// top-left corner and grows across the screen, which reads as "close, but not under the
/// pointer".</para>
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
    /// Whether an aim direction is usable — finite and long enough to normalise.
    ///
    /// <para>An unprojection at a degenerate projection matrix returns a zero or non-finite
    /// direction, and normalising that yields NaN, which reaches the drive and then the round.
    /// NaN times zero is still NaN, so nothing downstream recovers.</para>
    /// </summary>
    public static bool IsUsableDirection(double3 direction)
        => Vec.IsFinite(direction) && Vec.Len(direction) > 1e-9;

    private static bool IsFinite(float2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
}
