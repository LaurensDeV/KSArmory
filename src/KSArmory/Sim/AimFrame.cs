using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Which way is <em>up</em> for a vehicle told to point somewhere.
///
/// <para>"Belly toward the planet" has no answer with the nose vertical and reverses through half a turn
/// as the nose sweeps past it, which a vertical rise does for its whole duration. So the reference is
/// <em>carried</em>: each frame's is the previous one squared up against the new aim, and re-seeded only
/// once the aim has swung parallel to it.</para>
/// </summary>
internal static class AimFrame
{
    /// <summary>
    /// How square to the aim a reference has to stay before it is given up: the cross product loses its
    /// direction long before its length, so a reference within a few degrees of the aim is noise.
    /// </summary>
    public const double MinimumSeparation = 0.05;

    /// <param name="previousCci">Last frame's reference. Zero on the first frame.</param>
    /// <param name="aimCci">Where the nose is going now.</param>
    /// <param name="preferredCci">What to clock to when starting fresh — the planet, below.</param>
    /// <param name="fallbackCci">
    /// Something square to the aim when the preferred reference is not. Downrange during a vertical
    /// rise, which is horizontal by construction and therefore exactly what is wanted.
    /// </param>
    public static double3 Advance(double3 previousCci, double3 aimCci,
                                  double3 preferredCci, double3 fallbackCci)
    {
        double3 aim = Vec.Unit(aimCci);
        if (aim.Equals(Vec.Zero)) return previousCci;

        double3 carried = Flatten(previousCci, aim);
        if (!carried.Equals(Vec.Zero)) return carried;

        double3 preferred = Flatten(preferredCci, aim);
        if (!preferred.Equals(Vec.Zero)) return preferred;

        double3 fallback = Flatten(fallbackCci, aim);
        if (!fallback.Equals(Vec.Zero)) return fallback;

        return Vec.AnyPerpendicular(aim);
    }

    // The part of a reference that is square to the aim, or nothing if there is not enough of it.
    private static double3 Flatten(double3 reference, double3 aim)
    {
        double3 unit = Vec.Unit(reference);
        if (unit.Equals(Vec.Zero)) return Vec.Zero;

        double3 across = unit - aim * Vec.Dot(unit, aim);
        return Vec.Len(across) < MinimumSeparation ? Vec.Zero : Vec.Unit(across);
    }
}
