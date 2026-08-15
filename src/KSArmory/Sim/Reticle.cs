using Brutal.Numerics;

namespace KSArmory;

/// <summary>One straight stroke of the sight, in screen pixels.</summary>
public readonly record struct ReticleStroke(float2 A, float2 B);

/// <summary>
/// The gunner's sight, as strokes on a screen.
///
/// <para>Geometry only — no drawing, no ImGui, no camera. The layout is the part worth being sure
/// of: brackets that close as the head settles, and a gap at the centre so the target is never
/// covered by the thing pointing at it.</para>
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

    /// <summary>
    /// Half-width of the small fixed bracket (px), shared by the sight and the on-screen system
    /// markers so the two cannot drift apart.
    ///
    /// <para>Constant on purpose: this is an icon, not a bounding box. Sized to the target's
    /// apparent width instead, a box four widths across fills most of the screen the moment
    /// anything gets close — which is exactly when the sight is being looked through. It is below
    /// <see cref="CrossBelow"/>, so the cross and the ladder drop out and what is left is the
    /// eight corner strokes.</para>
    /// </summary>
    public const float IconHalfSize = 11f;

    /// <summary>
    /// Below this half-size the box holds corner brackets alone. The cross and the ladder are
    /// fractions of the box, so under it they collide with the brackets and with each other.
    /// </summary>
    public const float CrossBelow = 20f;

    /// <summary>
    /// Lays out the sight around a point.
    ///
    /// <para><paramref name="settled"/> draws the closed form — brackets tight against the target
    /// and the cross ticks stepped in. Unsettled they stand off, so a head still slewing looks
    /// like one still slewing rather than like a lock.</para>
    /// </summary>
    /// <returns>How many strokes were written.</returns>
    /// <param name="ladder">
    /// Draw the ranging ladder. It is for judging range by eye off the target's apparent size, so
    /// it earns nothing anywhere the range is already written down.
    /// </param>
    public static int Build(float2 centre, float halfSize, bool settled, Span<ReticleStroke> into,
                            bool ladder = true)
        => Build(centre, halfSize, settled ? 1f : LockCue.OpenStandoff, settled, into, ladder);

    /// <summary>
    /// The same sight with the stand-off given continuously rather than as settled or not, so a
    /// bracket can <em>close</em> over an acquisition instead of snapping between two sizes.
    ///
    /// <para><paramref name="standoff"/> multiplies the box: 1 is the closed form,
    /// <see cref="LockCue.OpenStandoff"/> is the open one, and the bool overload is exactly its
    /// two ends. <paramref name="settled"/> still chooses the closed <em>detailing</em> — the
    /// stepped-in cross and the ladder — because those are about having arrived rather than about
    /// how far the corners have come.</para>
    /// </summary>
    public static int Build(float2 centre, float halfSize, float standoff, bool settled,
                            Span<ReticleStroke> into, bool ladder = true)
    {
        if (into.Length < MaxStrokes) return 0;
        if (!float.IsFinite(centre.X) || !float.IsFinite(centre.Y) || !(halfSize > 0f)) return 0;
        if (!float.IsFinite(standoff) || !(standoff > 0f)) return 0;

        float box = halfSize * standoff;
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
        //
        // Omitted on a small box. Everything here is a fraction of the box, so at the floor the
        // ticks end 1.2 px short of the brackets -- less than the stroke width -- and twelve lines
        // merge into a blob that reads as a rendering fault rather than a sight.
        if (box < CrossBelow) return n;

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
        if (settled && ladder)
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
