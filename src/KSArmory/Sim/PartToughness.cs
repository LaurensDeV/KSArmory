namespace KSArmory;

/// <summary>
/// How much energy a part can take before it breaks, and what share of that a burst, a fragment or a
/// shell delivers.
///
/// <para><b>A part's size is its toughness</b>, where KSA's crash tolerance reads only its density: the
/// energy goes as its mass to the two thirds, the skin a hit has to get through, so a 20 t tank takes
/// many hits that break a 10 kg fairing. The tolerance moves it by its cube root, the law the blast
/// reach already obeys. Calibration is in <c>docs/DAMAGE-PLAN.md</c>.</para>
/// </summary>
internal static class PartToughness
{
    /// <summary>What breaks a part of <see cref="ReferenceMassKg"/> and the engine's base strength, in joules.</summary>
    public const double ReferenceJoules = 1.5e5;

    public const double ReferenceMassKg = 1000.0;

    // A part with no mass the engine will tell is taken as the reference.
    private const double LightestMassKg = 1.0;

    public const double TntJoulesPerKg = 4.184e6;

    /// <summary>
    /// The share of a charge's energy, over the solid angle a part fills, that goes into breaking it:
    /// the rest heats the air and the products.
    /// </summary>
    public const double BlastCoupling = 0.2;

    /// <summary>The share of a shell's kinetic energy left in the part it strikes; the rest leaves with it.</summary>
    public const double KineticCoupling = 0.5;

    /// <summary>
    /// The share of a fragment's energy that counts against the part: about half a hull's presented
    /// area is over nothing a fragment can disable.
    /// </summary>
    public const double FragmentCoupling = 0.5;

    /// <summary>
    /// How much of a round in the air is as tough as a part of its mass: its warhead, motor and seeker
    /// fill most of it, and one fragment through any of them is a kill.
    /// </summary>
    public const double RoundVulnerability = 0.15;

    /// <summary>The energy that breaks this part.</summary>
    public static double Joules(double massKg, double crashTolerancePascals)
    {
        double mass = double.IsFinite(massKg) && massKg > 0.0 ? Math.Max(massKg, LightestMassKg) : ReferenceMassKg;
        double tolerance = double.IsFinite(crashTolerancePascals) && crashTolerancePascals > 0.0
            ? crashTolerancePascals
            : BlastDamage.ReferencePascals;

        return ReferenceJoules * Math.Pow(mass / ReferenceMassKg, 2.0 / 3.0)
                               * Math.Cbrt(tolerance / BlastDamage.ReferencePascals);
    }

    /// <summary>
    /// The most a burst can cost a part, from the energy the part's presented area intercepts:
    /// <c>η·E·A / 4πr²</c>, and never more than the half of the charge facing it.
    /// </summary>
    public static double BlastShareCeiling(double chargeKg, double centreRangeMetres, double presentedAreaM2,
                                           double partJoules)
    {
        if (!(chargeKg > 0.0) || !(presentedAreaM2 > 0.0) || !(partJoules > 0.0)) return 0.0;

        double range = Math.Max(centreRangeMetres, 1.0e-3);
        double intercepted = Math.Min(presentedAreaM2 / (4.0 * Math.PI * range * range), 0.5);

        return BlastCoupling * chargeKg * TntJoulesPerKg * intercepted / partJoules;
    }

    /// <summary>What a struck part pays for a shell of <paramref name="massKg"/> arriving at <paramref name="speed"/>.</summary>
    public static double KineticShare(double massKg, double speed, double partJoules)
    {
        if (!(massKg > 0.0) || !(partJoules > 0.0) || !double.IsFinite(speed)) return 0.0;

        return KineticCoupling * 0.5 * massKg * speed * speed / partJoules;
    }

    /// <summary>What <paramref name="hits"/> fragments of <paramref name="joulesPerHit"/> cost a part.</summary>
    public static double FragmentShare(int hits, double joulesPerHit, double partJoules)
    {
        if (hits <= 0 || !(joulesPerHit > 0.0) || !(partJoules > 0.0)) return 0.0;

        return FragmentCoupling * hits * joulesPerHit / partJoules;
    }
}
