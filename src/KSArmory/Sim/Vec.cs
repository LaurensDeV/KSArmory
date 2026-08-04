using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Thin helpers over Brutal's double3 so the guidance code reads like the equations it implements.
/// </summary>
internal static class Vec
{
    public static readonly double3 Zero = new(0, 0, 0);

    public static double Dot(double3 a, double3 b) => double3.Dot(a, b);

    public static double3 Cross(double3 a, double3 b) => double3.Cross(a, b);

    public static double Len(double3 v) => v.Length();

    public static double Len2(double3 v) => v.LengthSquared();

    /// <summary>Unit vector, or zero for a degenerate input. Never returns NaN.</summary>
    public static double3 Unit(double3 v)
    {
        double len = v.Length();
        return len > 1e-12 ? v / len : Zero;
    }

    /// <summary>Component of <paramref name="v"/> parallel to <paramref name="axis"/> removed.</summary>
    public static double3 RejectFrom(double3 v, double3 axis)
    {
        double3 n = Unit(axis);
        return n.Equals(Zero) ? v : v - n * Dot(v, n);
    }

    /// <summary>Rescales <paramref name="v"/> so its length never exceeds <paramref name="max"/>.</summary>
    public static double3 ClampLength(double3 v, double max)
    {
        double len = v.Length();
        return len > max && len > 1e-12 ? v * (max / len) : v;
    }

    public static bool IsFinite(double3 v) =>
        double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

    /// <summary>Angle between two vectors in radians, robust at the 0 and pi endpoints.</summary>
    public static double AngleBetween(double3 a, double3 b)
    {
        double3 ua = Unit(a), ub = Unit(b);
        if (ua.Equals(Zero) || ub.Equals(Zero)) return 0.0;
        return Math.Acos(Math.Clamp(Dot(ua, ub), -1.0, 1.0));
    }

    /// <summary>
    /// Time of closest approach for a point separating as r(t) = r + v*t, clamped to
    /// [0, horizon]. Returns 0 for a stationary relative pair.
    /// </summary>
    public static double TimeOfClosestApproach(double3 r, double3 v, double horizon)
    {
        double vv = Len2(v);
        if (vv < 1e-12) return 0.0;
        double t = -Dot(r, v) / vv;
        return Math.Clamp(t, 0.0, horizon);
    }

    /// <summary>Any unit vector perpendicular to <paramref name="v"/>. Used for drawing cones.</summary>
    public static double3 AnyPerpendicular(double3 v)
    {
        double3 n = Unit(v);
        if (n.Equals(Zero)) return new double3(1, 0, 0);
        double3 seed = Math.Abs(n.X) < 0.9 ? new double3(1, 0, 0) : new double3(0, 1, 0);
        return Unit(Cross(n, seed));
    }
}
