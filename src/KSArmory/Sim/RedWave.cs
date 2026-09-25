namespace KSArmory;

/// <summary>
/// The red wave a burst high in the air sends out through the thermosphere: Teak's, 3.8 Mt at 77 km, was a
/// red sphere 965 km across six minutes later, seen from Hawaii. A shell running out at about 1.35 km/s
/// with oxygen's 630 nm red trailing about 150 km behind its front -- the speed times the ~110 s the
/// excited atom lives -- glowing only where the air is thin enough that the red is not quenched first.
///
/// <para>That is a density rather than a height (<see cref="RedSurvivesBelow"/>, about 150 km on Earth),
/// and anywhere above the air counts, so on another body it glows where that body's air is as thin.</para>
/// </summary>
internal static class RedWave
{
    /// <summary>How fast the front runs out (m/s): 965 km across at six minutes.</summary>
    public const double SpeedMetresPerSecond = 1_350.0;

    /// <summary>How far the red trails behind the front (m).</summary>
    public const double TrailMetres = 150_000.0;

    /// <summary>How long it is drawn at all (s), and the time it fades on.</summary>
    public const double LifeSeconds = 600.0;

    /// <inheritdoc cref="LifeSeconds"/>
    public const double FadeSeconds = 240.0;

    /// <summary>The air at the burst, against sea level, at or under which it sends one: about 60 km on Earth.</summary>
    public const double MostAirRatio = 2.5e-4;

    /// <summary>The density, against sea level, under which 630 nm red is not quenched: about 150 km on Earth.</summary>
    public const double RedSurvivesBelow = 5.0e-9;

    /// <summary>Its brightness at its brightest, for Teak's yield (nits); faint, a night sight.</summary>
    public const double PeakNits = 0.05;

    /// <summary>The yield <see cref="PeakNits"/> is for (kt).</summary>
    public const double ReferenceKt = 3_800.0;

    /// <summary>Whether a burst in air this thin sends one: some air to heat, and not much of it.</summary>
    public static bool Lights(double airRatio) => double.IsFinite(airRatio) && airRatio > 0.0 && airRatio <= MostAirRatio;

    /// <summary>How far the front has run (m).</summary>
    public static double Radius(double age) => age > 0.0 ? SpeedMetresPerSecond * age : 0.0;

    /// <summary>How bright it is (nits), by yield, rising over its first seconds and fading over minutes.</summary>
    public static double Nits(double yieldKt, double age)
    {
        if (!(yieldKt > 0.0) || !(age > 0.0) || age >= LifeSeconds) return 0.0;

        double scale = Math.Min(Math.Sqrt(yieldKt / ReferenceKt), 1.5);
        double rising = Smooth(age / 20.0);
        double ending = 1.0 - Smooth((age - (LifeSeconds - 60.0)) / 60.0);
        return PeakNits * scale * rising * Math.Exp(-age / FadeSeconds) * ending;
    }

    /// <summary>
    /// The altitude (m) over which the red survives on a body: where its air thins to
    /// <see cref="RedSurvivesBelow"/>, or its air's top.
    /// </summary>
    public static double RedAltitude(BodyAir air)
    {
        if (!air.HasAir) return 0.0;

        double ratio = air.SeaLevelDensity / (RedSurvivesBelow * AmbientAir.ReferenceDensityKgPerM3);
        return ratio > 1.0 ? Math.Min(air.ScaleHeightMetres * Math.Log(ratio), air.BoundaryMetres) : 0.0;
    }

    private static double Smooth(double x)
    {
        double t = Math.Clamp(x, 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }
}
