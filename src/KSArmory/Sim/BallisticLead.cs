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
    // Time of flight depends on where the target will be, which depends on time of flight. Four
    // passes settle it to well inside a metre at these ranges; it converges geometrically because
    // the target moves far slower than the round.
    private const int Passes = 4;

    /// <summary>
    /// The point to aim at, in the same frame as the inputs. False if there is no solution —
    /// no muzzle speed, or a target so fast the round can never arrive.
    /// </summary>
    /// <param name="targetVelocityRelative">
    /// The target's velocity <b>relative to the shooter</b>, never its absolute ecliptic velocity.
    /// Both carry the planet's ~29.8 km/s around its star, and that motion is shared by the round:
    /// leading on it throws the aim point over a hundred kilometres wide.
    /// </param>
    /// <param name="gravityEcl">Acceleration acting on the round, not on the shooter.</param>
    public static bool TrySolve(double3 shooterPos, double3 targetPos,
                                double3 targetVelocityRelative,
                                double muzzleSpeed, double3 gravityEcl, out double3 aimPoint)
    {
        aimPoint = targetPos;
        if (!(muzzleSpeed > 0.0) || !double.IsFinite(muzzleSpeed)) return false;
        if (!Vec.IsFinite(shooterPos) || !Vec.IsFinite(targetPos) || !Vec.IsFinite(targetVelocityRelative))
        {
            return false;
        }

        double flightTime = Vec.Len(targetPos - shooterPos) / muzzleSpeed;

        for (int i = 0; i < Passes; i++)
        {
            double3 predicted = targetPos + targetVelocityRelative * flightTime;
            double range = Vec.Len(predicted - shooterPos);
            flightTime = range / muzzleSpeed;

            if (!double.IsFinite(flightTime)) return false;
        }

        // Aim above the intercept by exactly what the round will fall, so the two cancel on the
        // way. Sign matters: gravity points down, so subtracting raises the aim.
        double3 intercept = targetPos + targetVelocityRelative * flightTime;
        aimPoint = intercept - gravityEcl * (0.5 * flightTime * flightTime);

        return Vec.IsFinite(aimPoint);
    }

    /// <summary>Flight time to a stationary point, for readouts and for arming decisions.</summary>
    public static double FlightTime(double range, double muzzleSpeed)
        => muzzleSpeed > 0.0 ? range / muzzleSpeed : double.PositiveInfinity;
}
