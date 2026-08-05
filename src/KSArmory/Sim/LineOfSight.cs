using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Whether a sphere sits between two points — the planet in the way, mostly.
///
/// <para>A marker that says "the system is over there" while the system is on the far side of the
/// world is worse than no marker: it reads as a bearing you could act on. Knowing the view is
/// blocked is what turns it into "over there, and behind the planet".</para>
///
/// <para>The body is treated as its mean sphere, so terrain is not accounted for: a craft in a
/// deep valley is reported visible when a ridge would really hide it, and one just over the
/// horizon flips at the geometric limb rather than at the skyline. That is the right
/// approximation for a marker and the wrong one for a radar, which is why this says nothing about
/// what a sensor can see.</para>
/// </summary>
public static class LineOfSight
{
    /// <summary>
    /// True when the segment from <paramref name="eye"/> to <paramref name="target"/> passes
    /// through the sphere.
    ///
    /// <para>Strictly between the endpoints: a craft standing on the surface has the body's own
    /// sphere touching it, and counting that would make everything on the ground invisible.</para>
    /// </summary>
    public static bool Blocked(double3 eye, double3 target, double3 centre, double radius)
    {
        if (!double.IsFinite(radius) || radius <= 0.0) return false;
        if (!Vec.IsFinite(eye) || !Vec.IsFinite(target) || !Vec.IsFinite(centre)) return false;

        double3 along = target - eye;
        double length2 = Vec.Len2(along);
        if (!double.IsFinite(length2) || length2 < 1e-12) return false;

        // Where along the segment the body's centre is nearest. Outside (0, 1) the sphere is
        // behind the eye or beyond the target, and neither is in the way.
        double t = Vec.Dot(centre - eye, along) / length2;
        if (!double.IsFinite(t) || t <= 0.0 || t >= 1.0) return false;

        return Vec.Len2(eye + along * t - centre) < radius * radius;
    }
}
