using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// How far off the plane the vehicle is already flying in a target sits, and what that costs.
///
/// <para>The one thing about an orbital shot that is invisible from the numbers on the panel. A
/// deorbit onto the ground track costs a hundred metres a second; the same weapon aimed at a place
/// thirty degrees out of plane costs four kilometres a second, and nothing in "3703 m/s to gain"
/// says which of those is happening or that the fix is a different orbit rather than a bigger
/// tank.</para>
///
/// <para>Waiting does not help either, which is why this is worth saying rather than solving. The
/// burn window search looks across one revolution, and a plane is not something a revolution
/// changes — only the planet turning underneath does, over many of them.</para>
/// </summary>
internal static class OrbitPlane
{
    /// <summary>Beyond this a shot is a plane change with a delivery attached, not a delivery.</summary>
    public const double NotableDegrees = 5.0;

    /// <summary>
    /// The angle between the target and the plane the vehicle is orbiting in, in radians.
    ///
    /// <para>Signed away: what matters is how far off it is, not which side.</para>
    /// </summary>
    public static double OffPlaneRadians(double3 positionCci, double3 velocityCci, double3 targetCci)
    {
        double3 normal = Vec.Unit(Vec.Cross(positionCci, velocityCci));
        double3 target = Vec.Unit(targetCci);

        if (normal.Equals(Vec.Zero) || target.Equals(Vec.Zero)) return 0.0;

        return Math.Asin(Math.Clamp(Math.Abs(Vec.Dot(normal, target)), 0.0, 1.0));
    }

    /// <summary>
    /// Roughly what turning the orbit that far costs, as a single impulse.
    ///
    /// <para>The textbook <c>2 v sin(theta/2)</c>. It is an estimate and it is not what guidance
    /// solves — the real burn does the turn and the deorbit together and so costs less — but it is
    /// the right order of magnitude and it is what makes the number on the panel explicable.</para>
    /// </summary>
    public static double PlaneChangeCost(double speed, double offPlaneRadians)
        => 2.0 * speed * Math.Sin(Math.Clamp(offPlaneRadians, 0.0, Math.PI) * 0.5);
}
