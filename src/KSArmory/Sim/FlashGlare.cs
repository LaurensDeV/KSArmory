namespace KSArmory;

/// <summary>
/// How much of a burst's glare reaches a viewer, given where they are looking.
///
/// <para><b>It never reaches nothing.</b> A fireball is twice as bright as the surface of the sun,
/// and light that bright does not stop at the edge of the frame: it scatters in the air between,
/// it veils across the optics, and it lights the whole landscape the viewer <em>is</em> looking at.
/// So turning away dims a burst and cannot abolish it, which is why the falloff lands on a floor
/// rather than on zero.</para>
///
/// <para>Full while the burst is in frame, because a source anywhere on the glass floods it.</para>
/// </summary>
public static class FlashGlare
{
    /// <summary>What still reaches a viewer with the burst directly behind them.</summary>
    public const double ScatteredFloor = 0.12;

    /// <summary>
    /// How far past the edge of the field the glare takes to fall to that floor, in degrees.
    ///
    /// <para>Wide on purpose. A source a few degrees outside the frame is still in the lens, and a
    /// cut at the frame edge would make the flash switch on and off as a burst drifted across
    /// it — which is a worse artefact than the one this exists to fix.</para>
    /// </summary>
    public const double FalloffDeg = 70.0;

    /// <summary>
    /// The fraction of the glare that reaches the eye, between <see cref="ScatteredFloor"/> and 1.
    /// </summary>
    /// <param name="offAxisDeg">Angle between where the viewer is looking and the burst.</param>
    /// <param name="halfFieldDeg">Half the viewer's field of view.</param>
    public static double Reaching(double offAxisDeg, double halfFieldDeg)
    {
        // Anything unreadable gives the whole of it, which is what happened before this existed.
        // A flash that should not have fired is a surprise; one that did not fire is a feature
        // silently missing, and of the two the first is the one somebody reports.
        if (!double.IsFinite(offAxisDeg) || !double.IsFinite(halfFieldDeg)) return 1.0;

        double edge = Math.Max(halfFieldDeg, 1.0);
        if (offAxisDeg <= edge) return 1.0;

        double t = Math.Clamp((offAxisDeg - edge) / FalloffDeg, 0.0, 1.0);

        return 1.0 - ((1.0 - ScatteredFloor) * (t * t * (3.0 - (2.0 * t))));
    }
}
