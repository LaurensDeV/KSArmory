using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// One of a body's cloud layers: the shell it fills, the turn from the body's own frame into the frame
/// its coverage map is drawn in, and the map.
/// </summary>
public readonly record struct CloudLayer(double BottomRadius, double TopRadius, doubleQuat ToCloud, CoverageCube Coverage);

/// <summary>
/// Where a ray first enters cloud thick enough to stop a beam, read off the coverage maps KSA draws its
/// clouds from. Only their kilometre-scale shapes: the puffs inside a field are KSA's GPU noise, which
/// nothing here reproduces, so the stop can sit a few hundred metres off a drawn cloud's edge
/// (docs/BEAM-RENDERING.md).
/// </summary>
public static class CloudCover
{
    /// <summary>
    /// Coverage at which a layer counts as cloud a beam cannot cross: overcast, never broken cloud, whose
    /// gaps the map is too coarse to place. On Earth broken cumulus reads 0.7 and a solid deck 0.8-0.9.
    /// </summary>
    public const float Opaque = 0.8f;

    // Fine against a texel of ~5 km on Earth's coverage map; the edge is then refined by halving.
    private const double StepMetres = 250.0;
    private const int MostSteps = 4000;
    private const int Refinements = 7;

    /// <summary>
    /// Whether the ray from <paramref name="origin"/> along <paramref name="direction"/>, both in the
    /// body's frame about its centre, meets cloud within <paramref name="reach"/>, and how far along.
    /// </summary>
    public static bool TryEntry(double3 origin, double3 direction, double reach, ReadOnlySpan<CloudLayer> layers,
                                out double distance)
    {
        distance = double.PositiveInfinity;
        double3 d = Vec.Unit(direction);
        if (!Vec.IsFinite(origin) || !Vec.IsFinite(d) || !(reach > 0.0)) return false;

        Span<double> spans = stackalloc double[4];
        foreach (CloudLayer layer in layers)
        {
            int count = Stretches(origin, d, layer.BottomRadius, layer.TopRadius, Math.Min(reach, distance), spans);

            for (int k = 0; k < count; k += 2)
            {
                if (TryMarch(origin, d, layer, spans[k], spans[k + 1], out double at) && at < distance) distance = at;
            }
        }

        return double.IsFinite(distance);
    }

    /// <summary>
    /// Whether this point is in cloud: coverage over <see cref="Opaque"/>, and below a top that rises
    /// with it, since KSA's thicker cover is its taller cloud.
    /// </summary>
    public static bool InCloud(double3 point, CloudLayer layer)
    {
        double r = Vec.Len(point);
        if (r < layer.BottomRadius || r > layer.TopRadius) return false;

        float cover = layer.Coverage.Sample(layer.ToCloud * point);
        if (cover < Opaque) return false;

        return r <= layer.BottomRadius + (cover * (layer.TopRadius - layer.BottomRadius));
    }

    private static bool TryMarch(double3 origin, double3 d, CloudLayer layer, double from, double to, out double at)
    {
        at = double.PositiveInfinity;
        double length = to - from;
        if (!(length > 0.0)) return false;

        int steps = Math.Clamp((int)Math.Ceiling(length / StepMetres), 1, MostSteps);
        double step = length / steps;

        if (InCloud(origin + (d * from), layer))
        {
            at = from;
            return true;
        }

        for (int i = 1; i <= steps; i++)
        {
            double t = from + (i * step);
            if (!InCloud(origin + (d * t), layer)) continue;

            double clear = t - step, cloud = t;
            for (int j = 0; j < Refinements; j++)
            {
                double mid = 0.5 * (clear + cloud);
                if (InCloud(origin + (d * mid), layer)) cloud = mid;
                else clear = mid;
            }

            at = cloud;
            return true;
        }

        return false;
    }

    // The stretches of the ray in [0, reach] that lie inside the shell between the two radii, as pairs.
    private static int Stretches(double3 o, double3 d, double bottom, double top, double reach, Span<double> into)
    {
        if (!Roots(o, d, top, out double t1, out double t2)) return 0;

        int count = 0;
        if (Roots(o, d, bottom, out double u1, out double u2))
        {
            count = Add(into, count, t1, u1, reach);
            count = Add(into, count, u2, t2, reach);
        }
        else
        {
            count = Add(into, count, t1, t2, reach);
        }

        return count;
    }

    private static int Add(Span<double> into, int count, double a, double b, double reach)
    {
        a = Math.Max(a, 0.0);
        b = Math.Min(b, reach);
        if (b <= a) return count;

        into[count] = a;
        into[count + 1] = b;
        return count + 2;
    }

    // Where |o + t d| = radius, nearer first; false if the line misses the sphere.
    private static bool Roots(double3 o, double3 d, double radius, out double near, out double far)
    {
        double b = Vec.Dot(o, d);
        double c = Vec.Len2(o) - (radius * radius);
        double disc = (b * b) - c;

        near = far = 0.0;
        if (!(disc > 0.0)) return false;

        double root = Math.Sqrt(disc);
        near = -b - root;
        far = -b + root;
        return true;
    }
}
