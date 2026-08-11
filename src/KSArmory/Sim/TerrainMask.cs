using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Whether the ground between two points hides one from the other, against the real height field
/// rather than against the mean sphere.
///
/// <para><see cref="LineOfSight"/> is the cheap half and stays in front of this: a sphere
/// containing the terrain cannot produce a false negative, so anything it rejects is genuinely
/// hidden and never reaches here. What is left is the band where the segment passes close enough
/// to the surface for a ridge to matter, and that is the only part sampled.</para>
///
/// <para>The cost is the whole design constraint. Every sample is a height-map fetch, and a sensor
/// runs this once per contact per scan — so the count is fixed and given by the caller, the
/// interval sampled is narrowed first by closed-form geometry, and a segment that never comes
/// within the body's own highest terrain costs nothing at all.</para>
/// </summary>
public static class TerrainMask
{
    /// <summary>
    /// True when terrain stands between the two points.
    ///
    /// <para>Samples strictly inside the segment. Both ends routinely sit on the ground — a
    /// launcher on a hillside, a target on a pad — and a sample taken at either would find the
    /// terrain that the endpoint is standing on and call everything hidden.</para>
    /// </summary>
    /// <param name="maxTerrainHeight">
    /// The highest terrain on this body (m). Only a bound, and only used to skip work: a segment
    /// that stays above it cannot be blocked, whatever the height field says.
    /// </param>
    /// <param name="samples">How many height lookups this look may cost. Zero or less asks none.</param>
    /// <param name="clearanceMetres">
    /// How far the terrain must stand above the ray before it counts. Absorbs the height field's
    /// own coarseness near a grazing endpoint, where the ray is metres above ground for
    /// kilometres and a single optimistic sample would blind the sensor.
    /// </param>
    public static bool Blocked(double3 eye, double3 target, double3 centre, double meanRadius,
                               double maxTerrainHeight, int samples, double clearanceMetres,
                               ITerrainHeights heights)
    {
        if (heights is null || samples <= 0) return false;
        if (!Vec.IsFinite(eye) || !Vec.IsFinite(target) || !Vec.IsFinite(centre)) return false;
        if (!double.IsFinite(meanRadius) || meanRadius <= 0.0) return false;

        double ceiling = meanRadius + Math.Max(0.0, double.IsFinite(maxTerrainHeight) ? maxTerrainHeight : 0.0);
        double clearance = Math.Max(0.0, double.IsFinite(clearanceMetres) ? clearanceMetres : 0.0);

        if (!TryBandBelow(eye, target, centre, ceiling, out double from, out double to)) return false;

        double3 along = target - eye;

        // Interior points of the band, evenly spaced and both ends left out.
        for (int i = 1; i <= samples; i++)
        {
            double t = from + (to - from) * i / (samples + 1);

            double3 radial = eye + along * t - centre;
            double radius = Vec.Len(radial);
            if (!(radius > 0.0)) continue;

            if (!heights.TryHeight(radial / radius, out double height)) continue;
            if (!double.IsFinite(height)) continue;

            if (radius + clearance <= meanRadius + height) return true;
        }

        return false;
    }

    /// <summary>
    /// The part of the segment that passes below <paramref name="ceiling"/> from the centre, as a
    /// fraction of it. False when none of it does.
    ///
    /// <para>Closed form, because it is what makes the sampling affordable: the distance from a
    /// centre to a point on a segment is a quadratic in how far along it is, so the interval
    /// where that distance is small enough for terrain to reach is exactly one root pair. Against
    /// an aircraft the band is a fraction of a long segment, and against something in orbit there
    /// is no band at all.</para>
    /// </summary>
    public static bool TryBandBelow(double3 eye, double3 target, double3 centre, double ceiling,
                                    out double from, out double to)
    {
        from = to = 0.0;

        if (!Vec.IsFinite(eye) || !Vec.IsFinite(target) || !Vec.IsFinite(centre)) return false;
        if (!double.IsFinite(ceiling) || ceiling <= 0.0) return false;

        double3 along = target - eye;
        double a = Vec.Len2(along);
        if (!double.IsFinite(a) || a < 1e-12) return false;

        double3 offset = eye - centre;
        double b = 2.0 * Vec.Dot(offset, along);
        double c = Vec.Len2(offset) - ceiling * ceiling;

        double discriminant = b * b - 4.0 * a * c;
        if (!double.IsFinite(discriminant) || discriminant <= 0.0) return false;

        double root = Math.Sqrt(discriminant);
        double lo = (-b - root) / (2.0 * a);
        double hi = (-b + root) / (2.0 * a);

        from = Math.Max(0.0, Math.Min(lo, hi));
        to = Math.Min(1.0, Math.Max(lo, hi));

        return to > from;
    }
}
