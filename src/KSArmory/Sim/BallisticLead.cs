using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where an unguided round has to be aimed to arrive where a moving target will be.
///
/// <para>A missile does not need this: it steers, so pointing the launcher at the target is
/// enough and the round closes the rest. A shell cannot, so everything the engagement needs has
/// to be in the barrel's direction at the instant it leaves — the target's motion during the
/// flight, and the drop over the same interval.</para>
///
/// <para>Neither term is small at cannon ranges. A 300 m/s target crosses ~1.4 km during a 4 km
/// shot, and the round falls ~100 m over the same 4.5 s.</para>
/// </summary>
public static class BallisticLead
{
    // Time of flight depends on where the target will be, which depends on time of flight, so it
    // is solved by iterating to a fixed point.
    //
    // Run to a tolerance rather than a fixed pass count. The iteration contracts by roughly the
    // ratio of target speed to muzzle speed, so a fixed four passes is only enough while that
    // ratio is small -- which was true of the aircraft it was calibrated on and false of the
    // thing point defence exists for. Intercepting a missile at 576 m/s with a 956 m/s shell
    // leaves ~13% of the error after four passes, tens of metres of lead, and it amplifies the
    // frame-to-frame variation in the target's position instead of settling it.
    private const double ToleranceSeconds = 1e-7;

    // Bounded so a ratio near or above 1 cannot spin: past that the round never arrives and the
    // solve has no answer to converge on.
    private const int MaxPasses = 32;

    /// <summary>
    /// The point to aim at, in the same frame as the inputs. False if there is no solution —
    /// no muzzle speed, or a target so fast the round can never arrive.
    /// </summary>
    /// <remarks>
    /// Takes both velocities and differences them here rather than accepting a relative one.
    /// The subtraction carries the whole frame contract — both terms hold the planet's ~29.8 km/s
    /// around its star, the round is launched with the shooter's share already in it, and leading
    /// on the common part throws the aim point a hundred kilometres wide. Accepting the difference
    /// would leave that subtraction at a call site in <c>Ksa/</c>, which no test can reach.
    ///
    /// <para>This is the convention for the whole of <c>Sim/</c>: an entry point takes both
    /// frame-carrying terms and differences them itself. See docs/FRAMES-AND-EPOCHS.md.</para>
    /// </remarks>
    /// <param name="gravityEcl">Acceleration acting on the round, not on the shooter.</param>
    public static bool TrySolve(double3 shooterPos, double3 shooterVelocity,
                                double3 targetPos, double3 targetVelocity,
                                double muzzleSpeed, double3 gravityEcl, out double3 aimPoint)
        => TrySolve(shooterPos, shooterVelocity, targetPos, targetVelocity, muzzleSpeed, gravityEcl,
                    out aimPoint, out _);

    /// <summary>
    /// The same solve, also reporting the time of flight it converged on.
    ///
    /// <para>Handed out rather than recomputed by the caller: a timed fuse set from a second,
    /// separately derived number would burst somewhere the gun was not aiming.</para>
    /// </summary>
    public static bool TrySolve(double3 shooterPos, double3 shooterVelocity,
                                double3 targetPos, double3 targetVelocity,
                                double muzzleSpeed, double3 gravityEcl, out double3 aimPoint,
                                out double flightTimeSeconds)
    {
        aimPoint = targetPos;
        flightTimeSeconds = 0.0;
        if (!(muzzleSpeed > 0.0) || !double.IsFinite(muzzleSpeed)) return false;
        if (!Vec.IsFinite(shooterPos) || !Vec.IsFinite(targetPos)
            || !Vec.IsFinite(shooterVelocity) || !Vec.IsFinite(targetVelocity))
        {
            return false;
        }

        double3 targetVelocityRelative = targetVelocity - shooterVelocity;

        double flightTime = Vec.Len(targetPos - shooterPos) / muzzleSpeed;

        bool converged = false;

        for (int i = 0; i < MaxPasses; i++)
        {
            double3 predicted = targetPos + targetVelocityRelative * flightTime;
            double range = Vec.Len(predicted - shooterPos);
            double next = range / muzzleSpeed;

            if (!double.IsFinite(next)) return false;

            double moved = Math.Abs(next - flightTime);
            flightTime = next;

            if (moved <= ToleranceSeconds) { converged = true; break; }
        }

        // A solve that ran out of passes has not found an intercept, it has been walking away from
        // one: the target is outrunning the round. Reporting the last iterate would be an aim point
        // presented with the same confidence as a real one.
        if (!converged) return false;

        // Aim above the intercept by exactly what the round will fall, so the two cancel on the
        // way. Sign matters: gravity points down, so subtracting raises the aim.
        double3 intercept = targetPos + targetVelocityRelative * flightTime;
        aimPoint = intercept - gravityEcl * (0.5 * flightTime * flightTime);
        flightTimeSeconds = flightTime;

        return Vec.IsFinite(aimPoint);
    }
}
