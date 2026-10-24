namespace KSArmory;

/// <summary>
/// The optical head's magnification, expressed as the field of view it asks a camera for. A factor
/// on the player's own field, so two players with different fields get the same instrument.
///
/// <para>The relation is optical, <c>tan(fov/2) = tan(base/2) / m</c>, not a ratio of angles: a 25°
/// field is 2.10× a 50° one, and what a linear rule would call 16× from 50° is 17.1×.</para>
/// </summary>
public static class SightZoom
{
    /// <summary>
    /// Narrowest field (deg). A crash guard: <c>CreatePerspectiveFieldOfViewReverseZ</c> throws for a
    /// field of zero. Just under the YAL-1's 1 mrad (0.057°), the narrowest optic fitted.
    /// </summary>
    public const double MinFovDeg = 0.05;

    /// <summary>Widest field (deg), the ceiling of the engine's own zoom keys.</summary>
    public const double MaxFovDeg = 120.0;

    /// <summary>What the camera is assumed to be showing when its own field cannot be read.</summary>
    public const double DefaultFovDeg = 50.0;

    public const float MinMagnification = 1f;
    /// <summary>The most any optic may be fitted with; each has its own, <c>OpticProfile.MaxMagnification</c>.</summary>
    public const float MaxMagnification = 1024f;

    /// <summary>The magnifications the panel and the wheel step through.</summary>
    public static ReadOnlySpan<float> Detents => [1f, 2f, 4f, 8f, 16f, 32f, 64f, 128f, 256f, 512f, 1024f];

    /// <summary>
    /// The stops one optic has: every detent up to its maximum, and the maximum itself when it is
    /// not one, so an optic fitted to its real narrowest field ends on it.
    /// </summary>
    public static int StopsFor(float max, Span<float> into)
    {
        const float slack = 1e-3f;
        int n = 0;

        foreach (float detent in Detents)
        {
            if (detent <= max + slack) into[n++] = detent;
        }

        if (n == 0 || Math.Abs(into[n - 1] - max) > slack) into[n++] = Clamp(max);
        return n;
    }

    /// <summary>Clamped to <see cref="MinFovDeg"/>..<see cref="MaxFovDeg"/>.</summary>
    public static double FovDegreesFor(double baseFovDeg, double magnification)
    {
        double half = double.DegreesToRadians(Sane(baseFovDeg)) * 0.5;
        double mag = Clamp(magnification);

        double fov = 2.0 * double.RadiansToDegrees(Math.Atan(Math.Tan(half) / mag));

        return !double.IsFinite(fov) ? Sane(baseFovDeg) : Math.Clamp(fov, MinFovDeg, MaxFovDeg);
    }

    /// <summary>
    /// The inverse of <see cref="FovDegreesFor"/>, for reporting what the camera actually shows, which
    /// differs from what was asked wherever the clamp bit.
    /// </summary>
    public static double MagnificationFor(double baseFovDeg, double fovDeg)
    {
        double half = double.DegreesToRadians(Sane(baseFovDeg)) * 0.5;
        double zoomed = double.DegreesToRadians(Math.Clamp(Sane(fovDeg), MinFovDeg, MaxFovDeg)) * 0.5;

        double mag = Math.Tan(half) / Math.Tan(zoomed);

        return double.IsFinite(mag) && mag > 0.0 ? mag : 1.0;
    }

    public static float Stepped(float magnification, int steps, float max = 16f)
    {
        Span<float> stops = stackalloc float[Detents.Length + 1];
        ReadOnlySpan<float> detents = stops[..StopsFor(max, stops)];
        const float slack = 1e-3f;

        // Counted from the detent already reached in the direction of travel, so a value between two
        // (restored from a save) steps to the neighbour ahead rather than jumping one.
        int from;
        if (steps > 0)
        {
            from = 0;
            for (int i = 0; i < detents.Length; i++)
            {
                if (magnification >= detents[i] - slack) from = i;
            }
        }
        else if (steps < 0)
        {
            from = detents.Length - 1;
            for (int i = detents.Length - 1; i >= 0; i--)
            {
                if (magnification <= detents[i] + slack) from = i;
            }
        }
        else
        {
            from = 0;
            for (int i = 1; i < detents.Length; i++)
            {
                if (Math.Abs(detents[i] - magnification) < Math.Abs(detents[from] - magnification))
                {
                    from = i;
                }
            }
        }

        return detents[Math.Clamp(from + steps, 0, detents.Length - 1)];
    }

    /// <summary>How wide the field is at a range (m).</summary>
    public static double MetresAcrossAt(double fovDeg, double rangeMetres)
    {
        if (!double.IsFinite(rangeMetres) || rangeMetres <= 0.0) return 0.0;

        return 2.0 * rangeMetres * Math.Tan(double.DegreesToRadians(Sane(fovDeg)) * 0.5);
    }

    /// <summary>How large something of a given size looks, in pixels down the viewport; zero when it cannot be sized.</summary>
    public static float ApparentPixels(double metres, double rangeMetres, double fovDeg,
                                       float viewportHeightPx)
    {
        if (!double.IsFinite(metres) || metres <= 0.0) return 0f;
        if (!double.IsFinite(rangeMetres) || rangeMetres <= 0.0) return 0f;
        if (!float.IsFinite(viewportHeightPx) || viewportHeightPx <= 0f) return 0f;

        double fovRad = double.DegreesToRadians(Sane(fovDeg));
        double subtended = 2.0 * Math.Atan(metres / rangeMetres);

        double pixels = subtended / fovRad * viewportHeightPx;

        return double.IsFinite(pixels) ? (float)pixels : 0f;
    }

    public static float Clamp(double magnification)
        => !double.IsFinite(magnification)
            ? MinMagnification
            : (float)Math.Clamp(magnification, MinMagnification, MaxMagnification);

    // Every number here is a ratio against the field, so one bad read is replaced rather than propagated.
    private static double Sane(double fovDeg)
        => double.IsFinite(fovDeg) && fovDeg > 0.0 && fovDeg < 180.0 ? fovDeg : DefaultFovDeg;
}
