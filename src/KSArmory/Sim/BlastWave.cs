namespace KSArmory;

/// <summary>
/// The blast wave as the air carries it, in pascals: how hard the front hits, how long it pushes,
/// and the wind behind it.
///
/// <para><b>Not the damage law.</b> <see cref="BlastDamage"/> is calibrated against KSA's part
/// strengths, which are crash tolerances of megapascals, so its "overpressure" says whether a part
/// breaks and nothing about what the air is doing. This is Kinney and Graham's fit for a TNT charge
/// in free air, which is what a craft being pushed and a view being shaken are about.</para>
///
/// <para>The charge is doubled by default, for a burst at the ground, as
/// <see cref="MushroomCloud.ShockRadius"/> doubles its energy: the ground reflects the half that went
/// down. A caller that knows better passes <see cref="GroundReflection.GainAt"/> -- one in free air,
/// more in an air burst's Mach stem. Everything scales with the
/// ambient pressure, which is Sachs' scaling held at the same scaled distance, so a burst in thin
/// air pushes less and one where there is none pushes nothing.</para>
/// </summary>
internal static class BlastWave
{
    public const double SeaLevelPascals = 101_325.0;

    // Sound in sea-level air, which the speed of the air behind a front is a multiple of.
    public const double SoundMetresPerSecond = 340.0;

    public const double SurfaceReflection = 2.0;

    // The wind behind a front decays as (1 - t/T)² e^(-t/T) over the positive phase, whose integral
    // is 1 - 2/e of its peak times the phase.
    private static readonly double WindImpulseShare = 1.0 - (2.0 / Math.E);

    /// <summary>Distance over the cube root of the charge, in m/kg^(1/3), as the fit reads it.</summary>
    public static double ScaledDistance(double chargeKg, double distanceMetres,
                                        double reflection = SurfaceReflection)
    {
        return distanceMetres / Math.Cbrt(reflection * chargeKg);
    }

    /// <summary>Peak overpressure the front carries at <paramref name="distanceMetres"/>, in pascals.</summary>
    public static double PeakOverpressurePascals(double chargeKg, double distanceMetres,
                                                 double ambientPascals = SeaLevelPascals,
                                                 double reflection = SurfaceReflection)
    {
        if (!(chargeKg > 0.0) || !(distanceMetres > 0.0) || !(ambientPascals > 0.0)) return 0.0;

        double z = ScaledDistance(chargeKg, distanceMetres, reflection);
        double ratio = 808.0 * (1.0 + Sq(z / 4.5))
                       / Math.Sqrt((1.0 + Sq(z / 0.048)) * (1.0 + Sq(z / 0.32)) * (1.0 + Sq(z / 1.35)));

        return ratio * ambientPascals;
    }

    /// <summary>How long the front pushes before the pressure behind it falls below the air's, in seconds.</summary>
    public static double PositivePhaseSeconds(double chargeKg, double distanceMetres,
                                              double reflection = SurfaceReflection)
    {
        if (!(chargeKg > 0.0) || !(distanceMetres > 0.0)) return 0.0;

        double z = ScaledDistance(chargeKg, distanceMetres, reflection);
        double msPerCubeRoot = 980.0 * (1.0 + Math.Pow(z / 0.54, 10.0))
                               / ((1.0 + Math.Pow(z / 0.02, 3.0)) * (1.0 + Math.Pow(z / 0.74, 6.0))
                                  * Math.Sqrt(1.0 + Sq(z / 6.9)));

        return msPerCubeRoot * Math.Cbrt(reflection * chargeKg) / 1000.0;
    }

    /// <summary>
    /// Peak dynamic pressure, ½ρu² of the wind behind a front of <paramref name="overpressurePascals"/>
    /// (Rankine–Hugoniot for air). This is what pushes a body along; the overpressure itself wraps
    /// round a closed shape and mostly cancels.
    /// </summary>
    public static double PeakWindPascals(double overpressurePascals, double ambientPascals = SeaLevelPascals)
    {
        if (!(overpressurePascals > 0.0) || !(ambientPascals > 0.0)) return 0.0;

        return 2.5 * overpressurePascals * overpressurePascals / ((7.0 * ambientPascals) + overpressurePascals);
    }

    /// <summary>
    /// How fast the air behind a front of <paramref name="overpressurePascals"/> is moving
    /// (Rankine–Hugoniot for air): the fastest the wind can carry anything, since a body it has
    /// brought up to its own speed feels no more of it.
    /// </summary>
    public static double WindSpeed(double overpressurePascals, double ambientPascals = SeaLevelPascals)
    {
        if (!(overpressurePascals > 0.0) || !(ambientPascals > 0.0)) return 0.0;

        double ratio = overpressurePascals / ambientPascals;
        return SoundMetresPerSecond * (5.0 / 7.0) * ratio / Math.Sqrt(1.0 + (6.0 / 7.0 * ratio));
    }

    /// <summary>
    /// The wind's push over the whole positive phase, in pascal-seconds: times a drag coefficient
    /// and an area it is the impulse a body takes along the blast.
    /// </summary>
    public static double WindImpulse(double chargeKg, double distanceMetres, double ambientPascals = SeaLevelPascals,
                                     double reflection = SurfaceReflection)
    {
        double peak = PeakWindPascals(PeakOverpressurePascals(chargeKg, distanceMetres, ambientPascals, reflection),
                                      ambientPascals);

        return peak * PositivePhaseSeconds(chargeKg, distanceMetres, reflection) * WindImpulseShare;
    }

    /// <summary>
    /// The overpressure behind a front of <paramref name="incidentPascals"/> meeting a rigid wall
    /// head-on (Rankine–Hugoniot for air): twice the incident for a weak front, rising toward eight
    /// times for a strong one. Two equal fronts meeting head-on meet as though at a wall, because
    /// the plane between them is one.
    /// </summary>
    public static double ReflectedPascals(double incidentPascals, double ambientPascals = SeaLevelPascals)
    {
        if (!(incidentPascals > 0.0) || !(ambientPascals > 0.0)) return 0.0;

        return 2.0 * incidentPascals * ((7.0 * ambientPascals) + (4.0 * incidentPascals))
               / ((7.0 * ambientPascals) + incidentPascals);
    }

    /// <summary>
    /// What is left of a front's overpressure <paramref name="sinceArrival"/> seconds after it
    /// passed, as a share of its peak: Friedlander's (1 - t/T) e^(-t/T) over the positive phase, and
    /// nothing after it.
    /// </summary>
    public static double Remaining(double sinceArrival, double positivePhaseSeconds)
    {
        if (!(sinceArrival > 0.0)) return 1.0;
        if (!(positivePhaseSeconds > 0.0) || sinceArrival >= positivePhaseSeconds) return 0.0;

        double tau = sinceArrival / positivePhaseSeconds;
        return (1.0 - tau) * Math.Exp(-tau);
    }

    private static double Sq(double x) => x * x;
}
