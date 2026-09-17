using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where each warhead of one salvo is aimed, as a pattern on the ground around the designation.
///
/// <para>Every warhead this mod has released has been aimed at the <em>same</em> point:
/// <see cref="ReleaseFocus"/> exists to land each round where the tubes' mean would land, so a
/// group converges rather than spreading. Flown, six 1.80 m reentry vehicles arrive a median
/// <b>8.7 mm</b> apart — they interpenetrate, and rounds do not collide with one another, so they
/// pass through each other and burst as one.</para>
///
/// <para><b>What this is for is measurement before it is realism.</b> A kick commanded to move
/// every warhead to one place can never be checked: each is asked for the same thing, and any error
/// appears as dispersion mixed with every other term. Commanded to a <em>known</em> pattern, the
/// kick becomes an actuator with a delivered-against-asked residual — and it is the largest single
/// term in the within-rocket miss, 44.5% of its variance (<c>docs/ACCURACY-PLAN.md</c> 3ef).</para>
///
/// <para><b>It is not a MIRV footprint and cannot become one.</b>
/// <see cref="ReleaseFocus.MaxMissKickMetresPerSecond"/> caps the separation kick at 10 mm/s, which
/// over a 345 s flight is about <b>3.5 m</b> of ground — two body lengths, against a 706 m fireball
/// and a 2 km lethal radius on the charge this vehicle carries. Real per-target spread needs the
/// bus to manoeuvre between releases, and that cap exists precisely so a separation cannot become a
/// burn.</para>
/// </summary>
public static class WarheadFootprint
{
    /// <summary>
    /// Where warhead <paramref name="index"/> of <paramref name="count"/> is aimed: the designation
    /// itself when <paramref name="metres"/> is zero, which is every flight before this one.
    /// </summary>
    /// <remarks>
    /// A ring rather than a grid, because the tubes are already a ring and the kick that moves a
    /// warhead off its mouth is the same solve either way — and a ring has no warhead at the centre
    /// to be indistinguishable from a group that is not spread at all.
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
    /// A position offset at release lands where a velocity of about <c>offset / T</c> does, so the
    /// ask grows as the radius and shrinks as the flight lengthens. Worth reporting rather than
    /// discovering: over the cap the kick is refused and the warhead flies the group's aim, which
    /// looks from the outside exactly like the setting doing nothing.
    /// </remarks>
    public static bool WithinTheCap(double metres, double flightSeconds)
        => !(metres > 0.0)
           || (flightSeconds > 0.0 && metres / flightSeconds <= ReleaseFocus.MaxMissKickMetresPerSecond);

    /// <summary>The widest footprint the cap allows on a flight of this length.</summary>
    public static double WidestAt(double flightSeconds)
        => flightSeconds > 0.0 ? ReleaseFocus.MaxMissKickMetresPerSecond * flightSeconds : 0.0;
}
