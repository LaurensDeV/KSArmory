using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The aurora a burst above the atmosphere makes where the planet's field brings its charged debris back
/// down into the air: near the burst, and at the other end of the same field line in the other hemisphere.
/// Teak, 3.8 Mt at 77 km over Johnston Island in 1958, lit an aurora over Samoa 3,200 km away. The field
/// is KSA's own (<see cref="MagneticDipole"/>), and the look is KSA's own aurora's, ported to a patch
/// (<c>Shaders/KSArmoryCloud.comp</c>, <c>DrawAurora</c>).
/// </summary>
internal static class Aurora
{
    /// <summary>
    /// The air, against sea level, above which a burst's debris is free to run along the field rather
    /// than being stopped in the fireball: about 70 km, which takes in Teak's 77.
    /// </summary>
    public const double AirRatio = 1.0e-4;

    /// <summary>Whether a burst in air this thin makes an aurora.</summary>
    public static bool Lights(double airRatio) => double.IsFinite(airRatio) && airRatio < AirRatio;

    /// <summary>
    /// The mass of air above the curtain's sharp lower border (kg/m²), where the debris's electrons are
    /// stopped. Calibrated on KSA's Earth to put the border at 100 km, and a column rather than a height
    /// so it sits where each body's air puts it. The green of oxygen peaks a little above it, and in a
    /// bright display nitrogen fringes it pink just below.
    /// </summary>
    public static readonly double FootColumnKgPerM2 = 1.225 * 8_000.0 * Math.Exp(-100.0 / 8.0);

    /// <summary>
    /// How far the curtain reaches over its border, in the body's scale heights: KSA's own aurora's shell,
    /// 95 to 500 km in Earth's air.
    /// </summary>
    public const double ReachScaleHeights = 405.0 / 8.0;

    /// <summary>The curtain's lower border over a body's mean sphere (m).</summary>
    public static double BottomAltitude(BodyAir air) => air.AltitudeOfColumn(FootColumnKgPerM2);

    /// <summary>Where the curtain is gone above (m): the red of oxygen peaks well above the border and fades through this.</summary>
    public static double TopAltitude(BodyAir air) => BottomAltitude(air) + (ReachScaleHeights * air.ScaleHeightMetres);

    /// <summary>How long it glows at all (s), and the e-folding time it fades on.</summary>
    public const double LifeSeconds = 900.0;

    /// <inheritdoc cref="LifeSeconds"/>
    public const double FadeSeconds = 240.0;

    /// <summary>How long after the burst it lights: the particles are seconds along the field line.</summary>
    public const double ArrivesSeconds = 3.0;

    /// <summary>Its brightness at a megatonne, as KSA's <c>AuroraReference.Brightness</c>: one is KSA's own aurora.</summary>
    public const double PeakBrightness = 1.0;

    /// <summary>
    /// How bright the curtain is at an age, for a yield in kilotonnes, against KSA's own aurora: arriving
    /// over its first seconds, fading over minutes, and gone at <see cref="LifeSeconds"/>. As the fourth
    /// root of the yield, so 50 Mt is under three times a natural display rather than seven.
    /// </summary>
    public static double Strength(double yieldKt, double age)
    {
        if (!(yieldKt > 0.0) || !(age >= 0.0) || age >= LifeSeconds) return 0.0;

        double arriving = Math.Clamp(age / ArrivesSeconds, 0.0, 1.0);
        double fading = Math.Exp(-Math.Max(age - ArrivesSeconds, 0.0) / FadeSeconds);
        double ending = 1.0 - Math.Clamp((age - (LifeSeconds - 60.0)) / 60.0, 0.0, 1.0);

        return PeakBrightness * Math.Pow(yieldKt / 1000.0, 0.25) * arriving * fading * ending;
    }
}
