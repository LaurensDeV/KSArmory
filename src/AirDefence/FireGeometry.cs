using Brutal.Numerics;

namespace AirDefence;

/// <summary>
/// The geometry of getting a round out of a tube: which way it leaves, and which way its body
/// points once it is out.
///
/// <para>Split out of <see cref="LauncherPart"/> so the test project can link it. Everything
/// here is pure vector maths on values the caller has already resolved, which means the two
/// mistakes this file exists to prevent — launching off the rail, and orienting a body off the
/// wrong velocity — can be caught without the game running.</para>
///
/// <para>Must stay free of KSA types, like <see cref="Interceptor"/>, <see cref="Vec"/> and
/// <see cref="Turret"/>.</para>
/// </summary>
public static class FireGeometry
{
    /// <summary>The model's nose axis. The round mesh is built pointing this way.</summary>
    public static readonly double3 NoseAxis = new(1, 0, 0);

    /// <summary>
    /// Which way a round leaves.
    ///
    /// <para>With a launcher that aims, the answer is simply "along the tube" — the pods have
    /// already been laid on the target, so the tube's own elevation is the loft and the round
    /// emerges pointing where the launcher points.</para>
    ///
    /// <para>The fallback slews onto the target and adds a bias toward the boresight. That is
    /// what a launcher with fixed tubes has to do; applied to one that aims, it sends the round
    /// off at a visibly different angle to the tube it just came out of.</para>
    /// </summary>
    public static double3 LaunchDirection(
        bool alongTube, double3 tubeAxis, double3 launchPos, double3 targetPos,
        double3 boresight, double loft)
    {
        if (alongTube)
        {
            double3 axis = Vec.Unit(tubeAxis);
            if (!axis.Equals(Vec.Zero)) return axis;
        }

        double3 toTarget = Vec.Unit(targetPos - launchPos);
        double3 direction = toTarget.Equals(Vec.Zero) ? boresight : toTarget;
        return Vec.Unit(direction + boresight * loft);
    }

    /// <summary>
    /// Rotation carrying <see cref="NoseAxis"/> onto <paramref name="direction"/>, so a round's
    /// body points the way it is travelling.
    ///
    /// Returns identity for a direction that is zero or already along the nose, and picks an
    /// arbitrary perpendicular axis for one that is exactly reversed — where the cross product
    /// is degenerate and would otherwise normalise to NaN.
    /// </summary>
    public static doubleQuat RotationFromNose(double3 direction)
    {
        double3 forward = Vec.Unit(direction);
        if (forward.Equals(Vec.Zero)) return doubleQuat.Identity;

        double dot = Math.Clamp(Vec.Dot(NoseAxis, forward), -1.0, 1.0);
        if (dot > 0.999999) return doubleQuat.Identity;
        if (dot < -0.999999) return doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), Math.PI);

        return doubleQuat.CreateFromAxisAngle(Vec.Unit(Vec.Cross(NoseAxis, forward)), Math.Acos(dot));
    }
}
