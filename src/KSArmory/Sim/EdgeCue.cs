namespace KSArmory;

/// <summary>
/// Where an indicator for something off the screen sits on its edge.
///
/// <para>Taken from the bearing against the camera's own right and up rather than from a
/// projection, because a projection maps a point behind the camera to the opposite side of the
/// screen and the arrow then points exactly the wrong way.</para>
/// </summary>
public static class EdgeCue
{
    /// <summary>
    /// The offset from the screen's centre, in pixels with Y growing downward, of the point on a
    /// <paramref name="width"/> by <paramref name="height"/> screen inset by
    /// <paramref name="margin"/> that lies along the bearing (<paramref name="right"/>,
    /// <paramref name="up"/>). False for a bearing that is not finite or is dead along the axis.
    /// </summary>
    public static bool TryOffset(double right, double up, double width, double height, double margin,
                                 out double dx, out double dy)
    {
        dx = 0.0;
        dy = 0.0;
        if (!double.IsFinite(right) || !double.IsFinite(up)) return false;
        if (Math.Abs(right) < 1e-9 && Math.Abs(up) < 1e-9) return false;

        double len = Math.Sqrt((right * right) + (up * up));
        double ux = right / len, uy = -up / len;

        double halfW = (width * 0.5) - margin, halfH = (height * 0.5) - margin;
        double scale = Math.Min(Math.Abs(ux) > 1e-9 ? halfW / Math.Abs(ux) : double.MaxValue,
                                Math.Abs(uy) > 1e-9 ? halfH / Math.Abs(uy) : double.MaxValue);

        dx = ux * scale;
        dy = uy * scale;
        return true;
    }
}
