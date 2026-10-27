using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Whether the ground between two points hides one from the other, against the real height field
/// rather than against the mean sphere.
///
/// <para><see cref="LineOfSight"/> is the cheap half and stays in front of this. Every sample is a
/// height-map fetch, once per contact per scan, so the caller fixes the count and only the band below
/// the highest terrain is sampled.</para>
/// </summary>
public static class TerrainMask
{
    /// <summary>
    /// Samples strictly inside the segment: both ends routinely sit on the ground, and a sample at
    /// either finds the terrain it stands on.
    /// </summary>
    /// <param name="maxTerrainHeight">
    /// The highest terrain on this body (m), used only to skip work.
    /// </param>
    /// <param name="samples">How many height lookups this look may cost. Zero or less asks none.</param>
    /// <param name="clearanceMetres">
    /// How far the terrain must stand above the ray before it counts: near a grazing endpoint the ray is
    /// metres above ground for kilometres, and one coarse sample would blind the sensor.
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
    /// The part of the segment that passes below <paramref name="ceiling"/> from the centre, as
    /// fractions of it; false when none does. One root pair of a quadratic in the fraction.
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
