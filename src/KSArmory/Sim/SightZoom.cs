namespace KSArmory;

/// <summary>
/// The optical head's magnification, expressed as the field of view it asks a camera for.
///
/// <para>Magnification rather than a field of view, because a field of view is only meaningful
/// against whatever the player had set: someone playing at 90° and someone at 50° asking for "20°"
/// get two different instruments. A factor is the same instrument on both, which is what a
/// magnification means on a real sight.</para>
///
/// <para>The relation is the optical one, not a ratio of angles. Halving an angle does not double
/// what it magnifies — a 25° field is 2.06× a 50° one, and the error grows without bound as the
/// field narrows: at what a linear rule would call 16× the true factor is 20.7×.</para>
/// </summary>
public static class SightZoom
{
    /// <summary>
    /// Narrowest field the camera may be asked for (deg).
    ///
    /// <para>The engine throws rather than clamping — <c>CreatePerspectiveFieldOfViewReverseZ</c>
    /// rejects a field of zero or more than half a turn, out of a mod's frame hook — so this is a
    /// hard limit and not a preference. A degree is far below any magnification offered here and
    /// well clear of the throw.</para>
    /// </summary>
    public const double MinFovDeg = 1.0;

    /// <summary>Widest field the camera may be asked for (deg), which is the engine's own ceiling.</summary>
    public const double MaxFovDeg = 120.0;

    /// <summary>What the camera is assumed to be showing when its own field cannot be read.</summary>
    public const double DefaultFovDeg = 50.0;

    public const float MinMagnification = 1f;
    public const float MaxMagnification = 24f;

    /// <summary>
    /// The magnifications the panel and the wheel step through.
    ///
    /// <para>Detents rather than a continuous slider: a gunner's sight has fixed optical stops, and
    /// a factor arrived at by dragging is a number nobody can return to.</para>
    /// </summary>
    public static ReadOnlySpan<float> Detents => [1f, 2f, 4f, 8f, 16f];

    /// <summary>
    /// The field of view (deg) that magnifies <paramref name="baseFovDeg"/> by
    /// <paramref name="magnification"/>, clamped to what the camera will accept.
    /// </summary>
    public static double FovDegreesFor(double baseFovDeg, double magnification)
    {
        double half = double.DegreesToRadians(Sane(baseFovDeg)) * 0.5;
        double mag = Clamp(magnification);

        double fov = 2.0 * double.RadiansToDegrees(Math.Atan(Math.Tan(half) / mag));

        return !double.IsFinite(fov) ? Sane(baseFovDeg) : Math.Clamp(fov, MinFovDeg, MaxFovDeg);
    }

    /// <summary>
    /// What magnification a field of view amounts to against the unzoomed one. The inverse of
    /// <see cref="FovDegreesFor"/>, for reporting what the camera is actually showing rather than
    /// what it was asked for — the two differ wherever the clamp bit.
    /// </summary>
    public static double MagnificationFor(double baseFovDeg, double fovDeg)
    {
        double half = double.DegreesToRadians(Sane(baseFovDeg)) * 0.5;
        double zoomed = double.DegreesToRadians(Math.Clamp(Sane(fovDeg), MinFovDeg, MaxFovDeg)) * 0.5;

        double mag = Math.Tan(half) / Math.Tan(zoomed);

        return double.IsFinite(mag) && mag > 0.0 ? mag : 1.0;
    }

    /// <summary>
    /// The next detent up or down from where the sight currently is.
    ///
    /// <para>Answers from the nearest detent rather than from the exact value, so a magnification
    /// restored from a save or left over from an older detent table still steps sensibly instead of
    /// sticking at one end.</para>
    /// </summary>
    public static float Stepped(float magnification, int steps)
    {
        ReadOnlySpan<float> detents = Detents;
        const float slack = 1e-3f;

        // Counted from the detent the value has already reached in the direction of travel, so a
        // magnification landing between two — restored from a save, or left over from an older
        // table — steps to the neighbour ahead of it rather than jumping the one it is inside.
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

    /// <summary>How wide the field is at a range (m), which is what a scale bar measures.</summary>
    public static double MetresAcrossAt(double fovDeg, double rangeMetres)
    {
        if (!double.IsFinite(rangeMetres) || rangeMetres <= 0.0) return 0.0;

        return 2.0 * rangeMetres * Math.Tan(double.DegreesToRadians(Sane(fovDeg)) * 0.5);
    }

    /// <summary>
    /// How large something of a given size looks, in pixels down the viewport.
    ///
    /// <para>Used to size the pipper to what the round actually covers, so the ring encloses what
    /// the shell reaches rather than being an icon of fixed size. Zero when it cannot be sized —
    /// the caller then draws its floor.</para>
    /// </summary>
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

    // A field the camera could not have been showing is replaced rather than propagated: every
    // number here is a ratio against it, so one bad read would otherwise put the sight at a
    // magnification nothing on screen agrees with.
    private static double Sane(double fovDeg)
        => double.IsFinite(fovDeg) && fovDeg > 0.0 && fovDeg < 180.0 ? fovDeg : DefaultFovDeg;
}
