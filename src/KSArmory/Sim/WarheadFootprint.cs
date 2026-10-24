using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where each warhead of one salvo is aimed, as a pattern on the ground around the designation.
///
/// <para>Unset, every warhead is aimed at the same point. A known pattern is for measurement: it
/// gives the kick a delivered-against-asked residual, and the kick is 44.5% of the within-rocket
/// landing variance (<c>docs/ACCURACY-PLAN.md</c> 3ef).</para>
///
/// <para><b>Not a MIRV footprint</b>: <see cref="ReleaseFocus.MaxMissKickMetresPerSecond"/> caps the
/// kick at 10 mm/s, about 3.5 m of ground over a 345 s flight.</para>
/// </summary>
public static class WarheadFootprint
{
    /// <summary>
    /// Where warhead <paramref name="index"/> of <paramref name="count"/> is aimed: the designation
    /// itself when <paramref name="metres"/> is zero, which is what ships.
    /// </summary>
    /// <remarks>
    /// A ring rather than a grid: a ring has no warhead at the centre to be mistaken for an unspread group.
    /// </remarks>
    public static double3 AimFor(double3 aimCci, double3 axisCci, double metres, int index, int count)
    {
        if (!(metres > 0.0) || count < 2 || index < 0 || index >= count) return aimCci;
        if (!Vec.IsFinite(aimCci) || !Vec.IsFinite(axisCci)) return aimCci;

        MapFrame? frame = MapFrame.TryAt(Vec.Zero, aimCci, axisCci);
        if (frame is not { } at) return aimCci;

        double angle = 2.0 * Math.PI * index / count;

        // Along the ground, so the pattern is a pattern on the surface rather than a chord through
        // it. At these radii the two differ by nanometres; at a footprint's radii they would not.
        double3 offset = at.East * (Math.Cos(angle) * metres) + at.North * (Math.Sin(angle) * metres);

        return Vec.Unit(aimCci + offset) * Vec.Len(aimCci);
    }

    /// <summary>
    /// The separation kick a footprint of this radius asks for, against what the cap allows.
    /// </summary>
    /// <remarks>
    /// The ask is about <c>offset / T</c>. Over the cap the kick is refused and the warhead flies the
    /// group's aim, which looks exactly like the setting doing nothing.
    /// </remarks>
    public static bool WithinTheCap(double metres, double flightSeconds)
        => !(metres > 0.0)
           || (flightSeconds > 0.0 && metres / flightSeconds <= ReleaseFocus.MaxMissKickMetresPerSecond);

    /// <summary>The widest footprint the cap allows on a flight of this length.</summary>
    public static double WidestAt(double flightSeconds)
        => flightSeconds > 0.0 ? ReleaseFocus.MaxMissKickMetresPerSecond * flightSeconds : 0.0;
}
