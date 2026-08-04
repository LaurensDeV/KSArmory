using Brutal.Numerics;

namespace KSArmory;

/// <summary>One straight stroke of the sight, in screen pixels.</summary>
public readonly record struct ReticleStroke(float2 A, float2 B);

/// <summary>
/// The gunner's sight, as strokes on a screen.
///
/// <para>Geometry only — no drawing, no ImGui, no camera. The layout is the part worth being sure
/// of: brackets that close as the head settles, a gap at the centre so the target is never
/// covered by the thing pointing at it, and ticks whose spread reads as range.</para>
///
/// <para>Modelled on the Pantsir's optical channel: corner brackets rather than a full box, a
/// broken cross rather than a solid one, and a scale that grows as the target closes.</para>
/// </summary>
public static class Reticle
{
    /// <summary>Most strokes <see cref="Build"/> can produce, so callers can size a buffer.</summary>
    public const int MaxStrokes = 20;

    /// <summary>
    /// Smallest box worth drawing (px). An aircraft at engagement range subtends only a few
    /// pixels at a normal field of view, so without a floor the sight collapses to a dot exactly
    /// when it is most needed.
    /// </summary>
    public const float MinBoxHalfSize = 10f;

    /// <summary>Half-width of the bracket box, in pixels, for a target of this angular size.</summary>
    public static float BoxHalfSize(double angularSizeRad, double verticalFovRad, int screenHeight)
    {
        if (!(verticalFovRad > 0.0) || screenHeight <= 0) return 24f;
        if (!double.IsFinite(angularSizeRad) || angularSizeRad <= 0.0) return MinBoxHalfSize;

        // Several target widths across. The brackets have to sit clear of the target so it stays
        // visible between them, and a box merely the size of the target reads as a blob.
        double pixels = angularSizeRad / verticalFovRad * screenHeight * 4.0;
        return (float)Math.Clamp(pixels, MinBoxHalfSize, screenHeight * 0.4);
    }

    /// <summary>
    /// Lays out the sight around a point.
    ///
    /// <para><paramref name="settled"/> draws the closed form — brackets tight against the target
    /// and the cross ticks stepped in. Unsettled they stand off, so a head still slewing looks
    /// like one still slewing rather than like a lock.</para>
    /// </summary>
    /// <returns>How many strokes were written.</returns>
    public static int Build(float2 centre, float halfSize, bool settled, Span<ReticleStroke> into)
    {
        if (into.Length < MaxStrokes) return 0;
        if (!float.IsFinite(centre.X) || !float.IsFinite(centre.Y) || !(halfSize > 0f)) return 0;

        float box = settled ? halfSize : halfSize * 1.6f;
        float arm = box * 0.38f;                 // how far each corner bracket runs
        int n = 0;

        // Corner brackets. Two strokes each, meeting at the corner.
        for (int corner = 0; corner < 4; corner++)
        {
            float sx = (corner & 1) == 0 ? -1f : 1f;
            float sy = (corner & 2) == 0 ? -1f : 1f;

            float2 at = new(centre.X + sx * box, centre.Y + sy * box);
            into[n++] = new ReticleStroke(at, new float2(at.X - sx * arm, at.Y));
            into[n++] = new ReticleStroke(at, new float2(at.X, at.Y - sy * arm));
        }

        // A broken cross: four ticks pointing inward, stopping short of the middle. A solid one
        // hides the target at exactly the moment it matters.
        float gap = settled ? box * 0.28f : box * 0.45f;
        float tick = box * 0.22f;

        into[n++] = new ReticleStroke(new float2(centre.X - gap - tick, centre.Y),
                                      new float2(centre.X - gap, centre.Y));
        into[n++] = new ReticleStroke(new float2(centre.X + gap, centre.Y),
                                      new float2(centre.X + gap + tick, centre.Y));
        into[n++] = new ReticleStroke(new float2(centre.X, centre.Y - gap - tick),
                                      new float2(centre.X, centre.Y - gap));
        into[n++] = new ReticleStroke(new float2(centre.X, centre.Y + gap),
                                      new float2(centre.X, centre.Y + gap + tick));

        // Ranging ladder down the left of the box, only once the sight has settled — before that
        // there is nothing to range against.
        if (settled)
        {
            for (int step = 1; step <= 3; step++)
            {
                float y = centre.Y - box + step * (2f * box / 4f);
                float len = step == 2 ? box * 0.16f : box * 0.10f;
                into[n++] = new ReticleStroke(new float2(centre.X - box - len, y),
                                              new float2(centre.X - box, y));
            }
        }

        return n;
    }
}
