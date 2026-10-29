using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Holes in one skin close enough to tear into each other, taken as one opening: their whole area, at its
/// area-weighted middle. A burst of hits then leaks as one jet rather than a bundle of threads.
///
/// <para>Two holes join when they are on the same piece of skin and nearer than <see cref="ReachInRadii"/>
/// times their radii together, or <see cref="LeastReachMetres"/>, whichever is wider; joining chains, so a
/// row of hits is one tear. The area is kept, so the flow through the opening is what the holes let through
/// between them.</para>
/// </summary>
internal static class HoleMerge
{
    /// <summary>How far apart, in their radii summed, two holes still tear into one.</summary>
    public const double ReachInRadii = 2.0;

    /// <summary>The least reach, so two 20 mm holes a hand-width apart are one.</summary>
    public const double LeastReachMetres = 0.15;

    /// <summary>
    /// For each hole the index of the first hole of its group, in the order given: a hole joining a group
    /// later never changes which hole leads it. <paramref name="skin"/> says which piece of skin each is in.
    /// </summary>
    public static int[] Groups(IReadOnlyList<double3> at, IReadOnlyList<double> radius, IReadOnlyList<int> skin)
    {
        int n = at.Count;
        int[] parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (skin[i] != skin[j]) continue;

                double reach = Math.Max(ReachInRadii * (radius[i] + radius[j]), LeastReachMetres);
                if (Vec.Len(at[i] - at[j]) > reach) continue;

                int a = Root(parent, i), b = Root(parent, j);
                if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
            }
        }

        int[] lead = new int[n];
        for (int i = 0; i < n; i++) lead[i] = Root(parent, i);
        return lead;
    }

    /// <summary>One group's opening: the radius of its whole area, at its area-weighted middle.</summary>
    public static (double3 At, double Radius) Opening(IReadOnlyList<double3> at, IReadOnlyList<double> radius,
                                                      int[] lead, int group)
    {
        double area = 0.0;
        double3 sum = Vec.Zero;
        for (int i = 0; i < at.Count; i++)
        {
            if (lead[i] != group) continue;

            double a = radius[i] * radius[i];
            area += a;
            sum += at[i] * a;
        }

        return area > 0.0 ? (sum / area, Math.Sqrt(area)) : (at[group], radius[group]);
    }

    private static int Root(int[] parent, int i)
    {
        while (parent[i] != i) i = parent[i] = parent[parent[i]];
        return i;
    }
}
