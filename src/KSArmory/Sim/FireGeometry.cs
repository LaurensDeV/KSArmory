using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The geometry of getting a round out of a tube: which way it leaves, and which way its body
/// points once it is out.
///
/// <para>Pure vector maths on values the caller has already resolved, which is what lets the two
/// mistakes it guards — launching off the rail, and orienting a body off the wrong velocity — be
/// caught without the game running.</para>
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
    /// <param name="ejectAway">
    /// Pushes a tube-launched round off its rail, along the boresight. A rail has no walls holding
    /// the round in, so it separates outward as well as forward; a container-launched round gets
    /// zero and leaves exactly along its tube.
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
    /// <para>A launcher one metre off the spin axis of a rolling craft is *moving*, and a store
    /// released from it keeps that velocity — which is how a spun bus fans its warheads apart.
    /// Adding only the platform's linear velocity drops every round out as though the craft were
    /// dead still, however fast it is rotating.</para>
    ///
    /// <para>Both positions are taken separately and differenced here rather than being handed a
    /// pre-computed lever arm: the subtraction carries the whole frame contract, and doing it at a
    /// call site no test reaches is what <c>docs/FRAMES-AND-EPOCHS.md</c> warns about. It is a
    /// difference of two points in one frame, so the ecliptic motion both carry cancels exactly —
    /// which is what <c>SpinVelocityIsUnchangedByTheFramesOwnMotion</c> pins.</para>
    ///
    /// <para>Every term is in the same frame; the answer comes back in it too.</para>
    /// </summary>
    /// <param name="angularVelocity">The platform's rotation rate, rad/s.</param>
    /// <param name="tubePosition">Where the round leaves.</param>
    /// <param name="centreOfMass">The point the platform actually turns about, not its origin —
    /// those differ by metres on a real stack, and the lever arm is measured from the pivot.</param>
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
    /// Rotation carrying <see cref="NoseAxis"/> onto <paramref name="direction"/>, so a round's
    /// body points the way it is travelling.
    /// </summary>
    public static doubleQuat RotationFromNose(double3 direction)
        => Vec.RotationFromTo(NoseAxis, direction);
}
