using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The geometry of getting a round out of a tube: which way it leaves, and which way its body
/// points once it is out.
///
/// <para>Out of <c>LauncherPart</c> so launching off the rail and orienting a body off the wrong velocity
/// are caught without the game running.</para>
/// </summary>
public static class FireGeometry
{
    /// <summary>The model's nose axis. The round mesh is built pointing this way.</summary>
    public static readonly double3 NoseAxis = new(1, 0, 0);

    /// <summary>What an ejector adds to a round's velocity: its speed out of the mounting face.</summary>
    public static double3 Ejection(double3 mountNormal, double ejectSpeed)
        => ejectSpeed > 0.0 ? Vec.Unit(mountNormal) * ejectSpeed : Vec.Zero;

    /// <summary>
    /// Which way a round leaves.
    ///
    /// <para>Along the tube for a launcher that aims, since the pods are already laid. The fallback, for
    /// fixed tubes, slews onto the target with a bias toward the boresight.</para>
    /// </summary>
    /// <param name="ejectAway">
    /// Pushes a rail-launched round outward along the boresight; a container-launched round gets zero.
    /// </param>
    public static double3 LaunchDirection(
        bool alongTube, double3 tubeAxis, double3 launchPos, double3 targetPos,
        double3 boresight, double loft, double ejectAway = 0.0)
    {
        if (alongTube)
        {
            double3 axis = Vec.Unit(tubeAxis);
            if (!axis.Equals(Vec.Zero))
            {
                if (ejectAway <= 0.0) return axis;

                double3 pushed = Vec.Unit(axis + Vec.Unit(boresight) * ejectAway);
                return pushed.Equals(Vec.Zero) ? axis : pushed;
            }
        }

        double3 toTarget = Vec.Unit(targetPos - launchPos);
        double3 direction = toTarget.Equals(Vec.Zero) ? boresight : toTarget;
        return Vec.Unit(direction + boresight * loft);
    }

    /// <summary>
    /// The velocity a tube already has because the platform carrying it is turning.
    ///
    /// <para>Both positions are differenced here rather than handed in as a lever arm, so the ecliptic
    /// motion they share cancels where a test reaches it (<c>SpinVelocityIsUnchangedByTheFramesOwnMotion</c>,
    /// <c>docs/FRAMES-AND-EPOCHS.md</c>). The answer is in the inputs' frame.</para>
    /// </summary>
    /// <param name="angularVelocity">The platform's rotation rate, rad/s.</param>
    /// <param name="tubePosition">Where the round leaves.</param>
    /// <param name="centreOfMass">The point the platform turns about, not its origin.</param>
    public static double3 SpinVelocity(double3 angularVelocity, double3 tubePosition,
                                       double3 centreOfMass)
    {
        if (!Vec.IsFinite(angularVelocity) || !Vec.IsFinite(tubePosition)
            || !Vec.IsFinite(centreOfMass))
        {
            return Vec.Zero;
        }

        double3 spin = Vec.Cross(angularVelocity, tubePosition - centreOfMass);
        return Vec.IsFinite(spin) ? spin : Vec.Zero;
    }

    /// <summary>
    /// The arm a point on a craft swings on as the craft turns: from its centre of mass, in the craft's
    /// own assembly frame, turned into the world's.
    ///
    /// <para>No position enters it, so it pairs with any sample this frame. KSA's craft position already
    /// <em>is</em> the centre of mass, a part placed at it plus <c>PositionVehicleAsmb − CenterOfMassAsmb</c>
    /// (<c>Vehicle.GetMatrixAsmb2Ego</c>); adding <c>CenterOfMassAsmb</c> again counts the offset twice.</para>
    /// </summary>
    public static double3 LeverArm(doubleQuat asmb2World, double3 pointVehicleAsmb, double3 centreOfMassAsmb)
    {
        double3 arm = asmb2World * (pointVehicleAsmb - centreOfMassAsmb);
        return Vec.IsFinite(arm) ? arm : Vec.Zero;
    }

    /// <summary>
    /// The velocity a point on a turning craft already has, taken from the craft's own frame.
    ///
    /// <para>The engine's rule for a part split off a turning vehicle:
    /// <c>BodyRates × (PositionVehicleAsmb − CenterOfMassAsmb)</c>, turned into the world.</para>
    /// </summary>
    /// <param name="angularVelocity">The craft's rotation in the world's frame, rad/s.</param>
    public static double3 SpinVelocityAt(double3 angularVelocity, doubleQuat asmb2World,
                                         double3 pointVehicleAsmb, double3 centreOfMassAsmb)
    {
        if (!Vec.IsFinite(angularVelocity)) return Vec.Zero;

        double3 spin = Vec.Cross(angularVelocity, LeverArm(asmb2World, pointVehicleAsmb, centreOfMassAsmb));
        return Vec.IsFinite(spin) ? spin : Vec.Zero;
    }

    /// <summary>
    /// Rotation carrying <see cref="NoseAxis"/> onto <paramref name="direction"/>, so a round's
    /// body points the way it is travelling.
    /// </summary>
    public static doubleQuat RotationFromNose(double3 direction)
        => Vec.RotationFromTo(NoseAxis, direction);
}
