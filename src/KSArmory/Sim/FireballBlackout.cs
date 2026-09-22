using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The air a nuclear fireball ionised, which a radar cannot see through until it cools.
///
/// <para>A fireball is gas hot enough to be a plasma, and the shock-heated air round it is not far
/// behind: free electrons absorb radio energy, so a radar beam crossing the region is lost and a
/// set inside it sees nothing at all. Glasstone calls it fireball blackout. It is a sphere that
/// rides up with the ball, as the ball becomes the cap, and it shrinks away as the air cools.</para>
///
/// <para>Radio only. An optical or infrared sensor is not blinded by electrons; what blinds one is
/// the flash, which is a different effect.</para>
/// </summary>
public static class FireballBlackout
{
    /// <summary>
    /// How far round the ball the air is ionised enough to absorb a radar beam, in peak fireball
    /// radii: the ball itself and the shock-heated shell just outside it.
    /// </summary>
    public const double ReachInFireballs = 1.5;

    /// <summary>
    /// How long a megatonne low burst blacks out the air round it. An order of magnitude from the
    /// literature, which puts a low megatonne burst's fireball blackout at about a minute; chosen
    /// rather than measured, and it scales as the cube root, like the fireball's own cooling.
    /// </summary>
    public const double MegatonneSeconds = 60.0;

    // The last share of the blackout over which the region shrinks away rather than switching off:
    // the air cools from its edge in.
    private const double ClearingShare = 0.3;

    /// <summary>How long a burst of this charge blacks out radar for, in seconds.</summary>
    public static double Seconds(double chargeKg)
        => chargeKg < MushroomCloud.ThresholdKg
               ? 0.0
               : MegatonneSeconds * Math.Cbrt(MushroomCloud.KilotonsFor(chargeKg) / 1000.0);

    /// <summary>
    /// The ionised region's radius at an age, zero once it has cleared. Full from the burst, and
    /// shrinking linearly through the last <see cref="ClearingShare"/> of its life.
    /// </summary>
    public static double Radius(double chargeKg, double ageSeconds)
    {
        double life = Seconds(chargeKg);
        if (!(life > 0.0) || ageSeconds < 0.0 || ageSeconds >= life) return 0.0;

        double full = ReachInFireballs * MushroomCloud.PeakFireballRadius(MushroomCloud.KilotonsFor(chargeKg));
        double clearingFrom = life * (1.0 - ClearingShare);

        return ageSeconds <= clearingFrom
                   ? full
                   : full * (life - ageSeconds) / (life - clearingFrom);
    }

    /// <summary>
    /// Whether a radar at <paramref name="radar"/> loses a contact at <paramref name="contact"/> to
    /// a region: the beam crosses it, or either end is inside it.
    /// </summary>
    public static bool Blocks(double3 radar, double3 contact, double3 centre, double radius)
    {
        if (!(radius > 0.0)) return false;

        double r2 = radius * radius;
        return Vec.Len2(radar - centre) < r2
               || Vec.Len2(contact - centre) < r2
               || LineOfSight.Blocked(radar, contact, centre, radius);
    }
}
