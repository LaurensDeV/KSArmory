namespace KSArmory;

/// <summary>
/// The layer of air a burst high above the atmosphere lights up.
///
/// <para>Above about 80 km a burst's X-rays are not stopped in metres to make a fireball: they run
/// down until the air is thick enough to absorb them, and heat a layer at 70-90 km -- the X-ray
/// "pancake" -- hundreds of kilometres across (Glasstone and Dolan, 2.130-2.143). It glows. Starfish
/// Prime, 1.4 Mt at 400 km, turned the sky over Hawaii red for minutes, 1,300 km away.</para>
///
/// <para>The red is atomic oxygen's 630 nm line, which lives about two minutes; its 557.7 nm green
/// lives under a second, so the first moments are green-white and the rest is red. The brightness is
/// drawn, not measured: a peak under the burst that falls as the inverse square of the distance from
/// it, and grows with the yield.</para>
/// </summary>
internal static class XRayGlow
{
    /// <summary>The air, against sea level, above which a burst lights the layer rather than growing a fireball of its own.</summary>
    public const double AirRatio = 1.0e-5;

    /// <summary>
    /// The mass of air above the layer (kg/m²): where a burst's X-rays have gone deep enough to be
    /// stopped. Calibrated on KSA's Earth to put the layer at 82 km (Glasstone and Dolan §7.91's
    /// 270,000 ft), and a column rather than a height so the layer sits where each body's air puts it.
    /// </summary>
    public static readonly double LayerColumnKgPerM2 = 1.225 * 8_000.0 * Math.Exp(-82.0 / 8.0);

    /// <summary>How thick the layer is, in the body's scale heights: 20 km in Earth's air.</summary>
    public const double LayerScaleHeights = 2.5;

    /// <summary>
    /// Where the layer is over a body's mean sphere (m): under the calibrated column, deeper in an air
    /// that stops X-rays less per kilogram (<see cref="BodyTraits.XRayOpacity"/>).
    /// </summary>
    public static double LayerAltitude(BodyAir air)
        => air.AltitudeOfColumn(LayerColumnKgPerM2 / Math.Max(air.Traits.XRayOpacity, 1.0e-6));

    /// <summary>How thick the layer is over a body (m).</summary>
    public static double LayerThickness(BodyAir air) => LayerScaleHeights * air.ScaleHeightMetres;

    /// <summary>How long the layer glows at all (s); it fades on <see cref="RedSeconds"/> long before.</summary>
    public const double LifeSeconds = 600.0;

    /// <summary>The e-folding time of the red, about the 630 nm line's own life.</summary>
    public const double RedSeconds = 110.0;

    /// <summary>The e-folding time of the green that is there first.</summary>
    public const double GreenSeconds = 3.0;

    /// <summary>How long the layer takes to light, from the burst.</summary>
    public const double RiseSeconds = 1.0;

    /// <summary>
    /// The glow's radiance straight under the burst, looking through the whole layer, at a megatonne
    /// 100 km over it. Vivid against a night sky and a faint pink on the horizon by day, where the
    /// line of sight runs furthest through the layer: an aurora of any strength is thousands of times
    /// fainter than the day sky, which is why Starfish's was a night's. Ten times this turns the whole
    /// day sky over the burst pink.
    /// </summary>
    public const double PeakNits = 0.3;

    /// <summary>The most any glow may be, so a burst just over the layer does not white the sky out.</summary>
    public const double MostNits = 0.5;

    /// <summary>A glow fainter than this at its brightest is not worth a full-screen dispatch for ten minutes.</summary>
    public const double FaintestNits = 0.002;

    /// <summary>Whether a burst in air this thin lights the layer.</summary>
    public static bool Lights(double airRatio) => double.IsFinite(airRatio) && airRatio < AirRatio;

    /// <summary>
    /// The glow's peak radiance under the burst at an age, for a yield in kilotonnes and the burst's
    /// height over the layer (m): the X-ray energy over the square of the distance it travelled, rising
    /// over its first second and fading on the red line's life. Nothing past <see cref="LifeSeconds"/>.
    /// </summary>
    public static double Strength(double yieldKt, double overLayerMetres, double age)
    {
        if (!(yieldKt > 0.0) || !(age >= 0.0) || age >= LifeSeconds) return 0.0;

        double over = Math.Max(overLayerMetres, 10_000.0);
        double peak = PeakNits * (yieldKt / 1000.0) * Math.Pow(100_000.0 / over, 2.0);
        double rising = Math.Clamp(age / RiseSeconds, 0.0, 1.0);
        double fading = Math.Exp(-Math.Max(age - RiseSeconds, 0.0) / RedSeconds);
        double ending = 1.0 - Math.Clamp((age - (LifeSeconds - 60.0)) / 60.0, 0.0, 1.0);

        return Math.Min(peak, MostNits) * rising * fading * ending;
    }

    /// <summary>How much of the glow is still the green line, in [0, 1].</summary>
    public static double GreenShare(double age)
        => double.IsFinite(age) && age >= 0.0 ? Math.Exp(-age / GreenSeconds) : 0.0;
}
