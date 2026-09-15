using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where a ray first meets the ground: the first place it goes from above the height field to below
/// it, walked out from the eye.
///
/// <para>Not the mean sphere's hit refined by the height under that answer. From a low eye looking
/// along a slope the sphere is hit behind the hill, every refinement samples the ground back there,
/// and a pick lands on the terrain beyond what the pointer is on.</para>
///
/// <para>Each step is as long as the ray stands above the ground where it is, so it strides over open
/// country and shortens as it closes on a slope — terrain rising no faster than <see cref="MaxSlope"/>
/// cannot get above it in between. Only the part of the ray below the highest terrain is walked.</para>
/// </summary>
public static class TerrainRay
{
    // Steeper than one in one is a cliff face.
    private const double MaxSlope = 1.0;

    // Never shorter than this fraction of the range walked, so a ray grazing flat ground does not
    // crawl: a ridge narrower than half a percent of its range, 50 m at 10 km, is not something a
    // pointer can be put on anyway.
    private const double MinStepFraction = 0.005;

    private const double MinStepMetres = 1.0;
    private const double MaxStepMetres = 1000.0;
    private const int MaxSamples = 512;
    private const double ToleranceMetres = 0.5;

    /// <summary>
    /// How far along the ray the ground is first met, or false when it is not met within
    /// <paramref name="maxRange"/> — sky, or an eye inside a hill, which is pointing at nothing it can
    /// name.
    /// </summary>
    /// <param name="maxTerrainHeight">
    /// The highest terrain on the body (m). A bound, used only to skip the part of the ray above it.
    /// </param>
    public static bool TryFirstHit(double3 origin, double3 direction, double maxRange, double3 centre,
                                   double meanRadius, double maxTerrainHeight, ITerrainHeights heights,
                                   out double range)
    {
        range = double.NaN;

        if (heights is null || !Vec.IsFinite(origin) || !Vec.IsFinite(direction) || !Vec.IsFinite(centre))
        {
            return false;
        }

        if (!double.IsFinite(maxRange) || maxRange <= 0.0 || !double.IsFinite(meanRadius) || meanRadius <= 0.0)
        {
            return false;
        }

        double3 dir = Vec.Unit(direction);
        if (!Vec.IsFinite(dir) || Vec.Len2(dir) < 0.5) return false;

        double top = double.IsFinite(maxTerrainHeight) ? Math.Max(0.0, maxTerrainHeight) : 0.0;
        if (!TerrainMask.TryBandBelow(origin, origin + (dir * maxRange), centre, meanRadius + top,
                                      out double from, out double to))
        {
            return false;
        }

        double t = from * maxRange;
        double end = to * maxRange;
        double clearAt = double.NaN;

        for (int i = 0; i < MaxSamples && t <= end; i++)
        {
            double floor = Math.Max(MinStepMetres, t * MinStepFraction);

            if (!TryClearance(origin, dir, t, centre, meanRadius, heights, out double clearance))
            {
                t += floor;
                continue;
            }

            if (clearance <= 0.0)
            {
                if (double.IsNaN(clearAt)) return false;

                range = Refine(origin, dir, clearAt, t, centre, meanRadius, heights);
                return true;
            }

            clearAt = t;
            t += Math.Clamp(Math.Max(clearance / MaxSlope, floor), MinStepMetres, MaxStepMetres);
        }

        return false;
    }

    // Halves the step that went from above the ground to below it until the crossing is known to
    // within the tolerance.
    private static double Refine(double3 origin, double3 dir, double above, double below, double3 centre,
                                 double meanRadius, ITerrainHeights heights)
    {
        for (int i = 0; i < 40 && below - above > ToleranceMetres; i++)
        {
            double mid = 0.5 * (above + below);

            if (TryClearance(origin, dir, mid, centre, meanRadius, heights, out double clearance) && clearance > 0.0)
            {
                above = mid;
            }
            else
            {
                below = mid;
            }
        }

        return 0.5 * (above + below);
    }

    // How far the point this far along the ray stands above the ground under it.
    private static bool TryClearance(double3 origin, double3 dir, double t, double3 centre, double meanRadius,
                                     ITerrainHeights heights, out double clearance)
    {
        clearance = 0.0;

        double3 radial = origin + (dir * t) - centre;
        double radius = Vec.Len(radial);
        if (!(radius > 0.0)) return false;

        if (!heights.TryHeight(radial / radius, out double height) || !double.IsFinite(height)) return false;

        clearance = radius - (meanRadius + height);
        return true;
    }
}
