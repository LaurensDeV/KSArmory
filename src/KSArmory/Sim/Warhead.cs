namespace KSArmory;

/// <summary>
/// How much explosive a round carries, and what that reaches.
///
/// <para>Everything scales as the <b>cube root</b> of the charge — the Hopkinson–Cranz law, which
/// says the distance at which a given blast effect is felt goes as <c>R = Z · W^(1/3)</c>. That
/// is the one fact worth encoding: doubling a warhead does not double its reach, it multiplies it
/// by 1.26. Written as three independent radii instead, a profile can quietly describe a warhead
/// whose lethal radius exceeds its blast radius.</para>
///
/// <para>The scaled distances below are a game model, not a munitions table. They are pinned to
/// the 57E6's existing numbers so the flight behaviour that has been tested stays as it was, and
/// a new round now gets consistent numbers from one figure rather than three guesses.</para>
/// </summary>
public static class Warhead
{
    /// <summary>
    /// Metres per kg^(1/3) at which fragments still kill. Chosen so a 20 kg warhead reaches 20 m,
    /// which is what the 57E6 was flown and tested with.
    /// </summary>
    public const double LethalScaledDistance = 7.368;

    /// <summary>
    /// The same for the outer radius, where a hit is worth reporting and nothing is destroyed.
    /// Three times the lethal distance, again from the 57E6's tested pair.
    /// </summary>
    public const double BlastScaledDistance = 22.104;

    /// <summary>
    /// The visible ball. Much tighter than either damage radius: an explosion that filled its own
    /// blast radius would be a 60 m sphere for a missile, which reads as a bug rather than as a
    /// warhead.
    /// </summary>
    public const double FireballScaledDistance = 2.6;

    /// <summary>The charge the particle emitters are authored for, in kg.</summary>
    public const double ReferenceChargeKg = 20.0;

    /// <summary>Everything inside this is destroyed.</summary>
    public static double LethalRadius(double chargeKg) => Radius(LethalScaledDistance, chargeKg);

    /// <summary>Everything inside this is a near miss worth reporting.</summary>
    public static double BlastRadius(double chargeKg) => Radius(BlastScaledDistance, chargeKg);

    /// <summary>Roughly how big the fireball should look.</summary>
    public static double FireballRadius(double chargeKg) => Radius(FireballScaledDistance, chargeKg);

    /// <summary>
    /// Smallest an effect is drawn at, whatever the charge.
    ///
    /// <para>The cube root is right for reach and wrong for visibility: a 0.16 kg cannon shell
    /// scales to 0.2, which turns the authored burst into 5 cm particles — perfectly proportionate
    /// and invisible at any range anyone watches from. An effect nobody can see is the same as no
    /// effect, and this is decoration, so it gets a floor. The damage radii do not.</para>
    /// </summary>
    public const double MinimumEffectScale = 0.45;

    /// <summary>
    /// What to multiply the authored effect by so it reads as this charge. Cube root again, so a
    /// warhead a thousand times bigger looks ten times bigger rather than a thousand — floored,
    /// so a small one still looks like something.
    /// </summary>
    public static double EffectScale(double chargeKg)
    {
        if (!double.IsFinite(chargeKg) || chargeKg <= 0.0) return 0.0;

        return Math.Max(Math.Cbrt(chargeKg / ReferenceChargeKg), MinimumEffectScale);
    }

    private static double Radius(double scaledDistance, double chargeKg)
    {
        if (!double.IsFinite(chargeKg) || chargeKg <= 0.0) return 0.0;

        return scaledDistance * Math.Cbrt(chargeKg);
    }
}
