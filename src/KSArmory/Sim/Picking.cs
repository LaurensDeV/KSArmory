using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Working out what the pointer is over: a sphere the cursor ray meets, and the nearest thing to
/// the cursor on screen.
/// </summary>
public static class Picking
{
    /// <summary>
    /// Where a ray first meets a sphere, or false if it misses or the sphere is behind it.
    ///
    /// <para>The near root, not the far one: standing outside a planet and pointing at it, the
    /// answer wanted is the surface facing you, not the far side.</para>
    /// </summary>
    public static bool TryHitSphere(double3 origin, double3 direction,
                                    double3 centre, double radius, out double3 hit)
    {
        hit = default;

        if (!double.IsFinite(radius) || radius <= 0.0) return false;
        if (!Vec.IsFinite(origin) || !Vec.IsFinite(direction) || !Vec.IsFinite(centre)) return false;

        double3 d = Vec.Unit(direction);
        if (Vec.Len(d) < 0.5) return false;

        double3 toCentre = centre - origin;
        double along = Vec.Dot(toCentre, d);
        double gap2 = Vec.Len2(toCentre) - along * along;
        double radius2 = radius * radius;
        if (gap2 > radius2) return false;

        double half = Math.Sqrt(Math.Max(radius2 - gap2, 0.0));

        // Inside the sphere the near root is behind us, so the exit is the only hit ahead.
        double t = along - half;
        if (t <= 0.0) t = along + half;
        if (!double.IsFinite(t) || t <= 0.0) return false;

        hit = origin + d * t;
        return true;
    }

    /// <summary>
    /// The index of the screen position nearest the cursor within <paramref name="radius"/>
    /// pixels, or -1.
    ///
    /// <para>Nearest rather than first: two craft close together on screen would otherwise be
    /// picked by list order, which is the order they were built in and means nothing to the
    /// person pointing at one of them.</para>
    /// </summary>
    public static int NearestOnScreen(IReadOnlyList<float2> positions, float2 cursor, float radius)
    {
        int best = -1;
        float bestDistance2 = radius * radius;

        for (int i = 0; i < positions.Count; i++)
        {
            float dx = positions[i].X - cursor.X;
            float dy = positions[i].Y - cursor.Y;
            float distance2 = dx * dx + dy * dy;

            if (distance2 > bestDistance2) continue;

            bestDistance2 = distance2;
            best = i;
        }

        return best;
    }
}
