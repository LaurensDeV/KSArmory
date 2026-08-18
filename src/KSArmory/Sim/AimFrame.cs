using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Which way is <em>up</em> for a vehicle told to point somewhere.
///
/// <para>Pointing needs two directions, not one. Where the nose goes leaves the roll about that
/// nose undecided, and something has to decide it — normally the planet, by putting the vehicle's
/// belly toward it.</para>
///
/// <para><b>That rule has no answer when the nose points at the planet or away from it</b>, and
/// worse than no answer: it <em>reverses</em>. Sweep the nose up through the vertical and "belly
/// down" swings through half a turn, because the side the planet is on has changed. A vertical
/// rise sits exactly there for its whole duration. So a rule that re-derives the roll from the
/// aim each frame commands a vehicle that rolls hard for no reason, and no threshold fixes it —
/// the discontinuity is in the rule, not in the arithmetic.</para>
///
/// <para>The reference is therefore <em>carried</em>: each frame's is the previous one squared up
/// against the new aim. Continuous by construction, because it never asks the question again. It is
/// only re-seeded when the aim swings so far that the old reference has become parallel to it,
/// which is a different vehicle attitude rather than a boundary being crossed.</para>
/// </summary>
internal static class AimFrame
{
    /// <summary>
    /// How square to the aim a reference has to stay before it is given up as unusable.
    ///
    /// <para>The cross product loses its <em>direction</em> long before it loses its length, so a
    /// reference within a few degrees of the aim is already noise.</para>
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

        // Carried forward wherever it still means anything, which is almost always.
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
