using Brutal.Numerics;

namespace KSArmory;

internal static class Vec
{
    public static readonly double3 Zero = new(0, 0, 0);

    public static double Dot(double3 a, double3 b) => double3.Dot(a, b);

    public static double3 Cross(double3 a, double3 b) => double3.Cross(a, b);

    public static double Len(double3 v) => v.Length();

    public static double Len2(double3 v) => v.LengthSquared();

    /// <summary>Unit vector, or zero for a zero-length or NaN input; an infinite component still gives NaN.</summary>
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

    public static double3 ClampLength(double3 v, double max)
    {
        double len = v.Length();
        return len > max && len > 1e-12 ? v * (max / len) : v;
    }

    public static bool IsFinite(double3 v) =>
        double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

    /// <summary>
    /// Angle between two vectors in radians, exact at the 0 and pi endpoints as well as between.
    /// </summary>
    /// <remarks>
    /// <c>atan2(|a x b|, a.b)</c> rather than <c>acos(a.b)</c>: acos has a floor of
    /// <c>sqrt(2*eps)</c> = 2.1e-8 rad, 0.134 m across Earth's radius, larger than a ballistic group's miss.
    /// </remarks>
    public static double AngleBetween(double3 a, double3 b)
    {
        double3 ua = Unit(a), ub = Unit(b);
        if (ua.Equals(Zero) || ub.Equals(Zero)) return 0.0;
        return Math.Atan2(Len(Cross(ua, ub)), Dot(ua, ub));
    }

    /// <summary>
    /// <paramref name="from"/> turned toward <paramref name="to"/> by at most
    /// <paramref name="maxRadians"/>, as a unit vector. Exactly opposite, it turns about any
    /// perpendicular.
    /// </summary>
    public static double3 TurnToward(double3 from, double3 to, double maxRadians)
    {
        double3 a = Unit(from), b = Unit(to);
        if (a.Equals(Zero)) return b;
        if (b.Equals(Zero)) return a;

        double angle = AngleBetween(a, b);
        if (angle <= maxRadians) return b;

        double3 cross = Cross(a, b);
        double3 axis = Len2(cross) > 1e-24 ? Unit(cross) : AnyPerpendicular(a);
        return Unit(doubleQuat.CreateFromAxisAngle(axis, Math.Max(maxRadians, 0.0)) * a);
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

    public static double3 Lerp(double3 a, double3 b, double t) => a + ((b - a) * t);

    /// <summary>Any unit vector perpendicular to <paramref name="v"/>, seeded from a fixed ecliptic axis.</summary>
    public static double3 AnyPerpendicular(double3 v)
    {
        double3 n = Unit(v);
        if (n.Equals(Zero)) return new double3(1, 0, 0);
        double3 seed = Math.Abs(n.X) < 0.9 ? new double3(1, 0, 0) : new double3(0, 1, 0);
        return Unit(Cross(n, seed));
    }

    /// <summary>
    /// The shortest rotation carrying one direction onto another.
    ///
    /// <para>Antiparallel turns half a circle about <see cref="AnyPerpendicular"/>. The 1e-6 band is
    /// that wide because near antiparallel the cross product's direction is cancellation noise, and a
    /// randomly directed half turn flickers frame to frame.</para>
    /// </summary>
    public static doubleQuat RotationFromTo(double3 from, double3 to)
    {
        double3 a = Unit(from);
        double3 b = Unit(to);

        if (!IsFinite(a) || !IsFinite(b) || a.Equals(Zero) || b.Equals(Zero))
        {
            return doubleQuat.Identity;
        }

        double dot = Math.Clamp(Dot(a, b), -1.0, 1.0);

        if (dot > 0.999999) return doubleQuat.Identity;
        if (dot < -0.999999) return doubleQuat.CreateFromAxisAngle(AnyPerpendicular(a), Math.PI);

        return doubleQuat.CreateFromAxisAngle(Unit(Cross(a, b)), Math.Acos(dot));
    }

    /// <summary>
    /// A unit vector perpendicular to <paramref name="v"/>, clocked to <paramref name="reference"/>.
    ///
    /// <para>For a direction sweeping through world space, <see cref="AnyPerpendicular"/> turns with
    /// it and jumps when its seed swaps at |X| = 0.9; a reference turning with the craft holds still.</para>
    /// </summary>
    public static double3 PerpendicularTo(double3 v, double3 reference)
    {
        double3 n = Unit(v);
        if (n.Equals(Zero) || !IsFinite(reference)) return AnyPerpendicular(v);

        double3 flattened = reference - n * Dot(reference, n);
        return Len2(flattened) < 1e-12 ? AnyPerpendicular(v) : Unit(flattened);
    }
}
