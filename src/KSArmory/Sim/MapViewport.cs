using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The visible crop of an equirectangular planet map, as normalised UV against the whole body.
///
/// <para>KSA's ground-track window owns the pan and zoom and hands the crop to its map exporter,
/// computing it inside <c>GenericImGuiViewport.CreateDraggableZoomableViewport</c> — which also
/// consumes a drag and a wheel notch, so nothing may call it a second time in a frame to read the
/// answer back. The pan and zoom themselves are public, so this is the arithmetic between them
/// restated, and <c>MapViewportTests</c> is what keeps it the same arithmetic.</para>
/// </summary>
internal static class MapViewport
{
    /// <summary>
    /// The crop a pan and zoom leave, in UV. The whole body when the zoom or the area is
    /// degenerate, which is the answer that shows everything rather than nothing.
    /// </summary>
    public static void FromPanZoom(float2 area, float2 pan, float zoom,
                                   out float2 min, out float2 max)
    {
        if (!(zoom > 0f) || !(area.X > 0f) || !(area.Y > 0f))
        {
            min = float2.Zero;
            max = float2.One;
            return;
        }

        float2 scaled = area * zoom;
        min = float2.Clamp(-pan / scaled, float2.Zero, float2.One);
        max = float2.Clamp((area - pan) / scaled, float2.Zero, float2.One);
    }

    /// <summary>Where a longitude and latitude sit in the whole body's UV, before any crop.</summary>
    /// <remarks>
    /// The wrap and the clamp are KSA's: a longitude is brought into -180..180 the long way so a
    /// negative one lands in range, and a latitude past a pole is held at it rather than wrapping
    /// over the top onto the opposite meridian.
    /// </remarks>
    public static float2 ToUv(double longitudeDeg, double latitudeDeg)
    {
        double lon = ((longitudeDeg + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
        double lat = latitudeDeg < -90.0 ? -90.0 : latitudeDeg > 90.0 ? 90.0 : latitudeDeg;

        return new float2((float)((lon + 180.0) / 360.0), (float)((90.0 - lat) / 180.0));
    }

    /// <summary>
    /// Whether two crops agree to within a UV the map could not tell apart — a ten-thousandth,
    /// which on a 2048-wide texture is a fifth of a pixel.
    /// </summary>
    public static bool Same(float2 aMin, float2 aMax, float2 bMin, float2 bMax)
    {
        const float Tolerance = 1e-4f;

        return System.Math.Abs(aMin.X - bMin.X) < Tolerance
               && System.Math.Abs(aMin.Y - bMin.Y) < Tolerance
               && System.Math.Abs(aMax.X - bMax.X) < Tolerance
               && System.Math.Abs(aMax.Y - bMax.Y) < Tolerance;
    }

    /// <summary>Whether a point's UV is inside the crop, so a mark off the edge is not drawn.</summary>
    public static bool Shows(float2 uv, float2 min, float2 max)
        => uv.X >= min.X && uv.X <= max.X && uv.Y >= min.Y && uv.Y <= max.Y;
}
