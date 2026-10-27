namespace KSArmory;

/// <summary>
/// How much explosive a round carries, and what that reaches.
///
/// <para>Every radius is <c>R = Z · W^(1/3)</c> (Hopkinson–Cranz), so doubling a charge multiplies
/// its reach by 1.26, and no profile can give a lethal radius past its blast radius. The scaled
/// distances are a game model pinned to the 57E6's flown numbers.</para>
/// </summary>
public static class Warhead
{
    /// <summary>
    /// Metres per kg^(1/3) at which fragments still kill. Chosen so a 20 kg warhead reaches 20 m,
    /// which is the 57E6's calibrated figure.
    /// </summary>
    public const double LethalScaledDistance = 7.368;

    /// <summary>
    /// The same for the outer radius, where a hit is worth reporting and nothing is destroyed.
    /// Three times the lethal distance, again from the 57E6's calibrated pair.
    /// </summary>
    public const double BlastScaledDistance = 22.104;

    /// <summary>The visible ball, much tighter than either damage radius.</summary>
    public const double FireballScaledDistance = 2.6;

    /// <summary>
    /// Where a part of <see cref="BlastDamage.ReferencePascals"/> strength fails; with
    /// <c>Config.DamageIndividualParts</c> off, everything inside it is destroyed.
    /// </summary>
    public static double LethalRadius(double chargeKg) => Radius(LethalScaledDistance, chargeKg);

    /// <summary>Everything inside this is a near miss worth reporting.</summary>
    public static double BlastRadius(double chargeKg) => Radius(BlastScaledDistance, chargeKg);

    /// <summary>Roughly how big the fireball should look.</summary>
    public static double FireballRadius(double chargeKg) => Radius(FireballScaledDistance, chargeKg);

    private static double Radius(double scaledDistance, double chargeKg)
    {
        if (!double.IsFinite(chargeKg) || chargeKg <= 0.0) return 0.0;

        return scaledDistance * Math.Cbrt(chargeKg);
    }
}
