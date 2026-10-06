namespace KSArmory;

/// <summary>
/// A body's air as numbers, so nothing downstream reads a body or knows which one it is on. Built on the
/// KSA side from what the body declares (<c>AtmosphereReference.Physical</c>, its mass and radius) and
/// from <see cref="BodyTraits"/> for what it does not.
///
/// <para><b>It mirrors KSA's own model exactly</b>, because the round's drag and the burst must agree
/// about the same air: isothermal, <c>SeaLevel·exp(-h/H)</c> for both pressure and density, clamped to
/// sea level below the mean radius and cut to nothing at the boundary height. So the speed of sound is
/// one number per body, and the column of air above a point is <c>ρ(h)·H</c> with the <b>declared</b>
/// scale height -- which need not be the hydrostatic <c>P/(ρg)</c>: 8.4 km against 8 for KSA's Earth,
/// 25.5 against 150 for its Jupiter.</para>
/// </summary>
/// <param name="SeaLevelPascals">Pressure at the mean radius (Pa).</param>
/// <param name="SeaLevelDensity">Density at the mean radius (kg/m³).</param>
/// <param name="ScaleHeightMetres">The declared scale height (m).</param>
/// <param name="BoundaryMetres">Where KSA stops the air (m over the mean radius).</param>
/// <param name="Gravity">Surface gravity (m/s²).</param>
/// <param name="RadiusMetres">Mean radius (m).</param>
/// <param name="Traits">What the body's <c>Bodies.xml</c> entry says, or the default.</param>
/// <param name="Wet">Whether the body has weather or an ocean, which is where condensation defaults from.</param>
internal readonly record struct BodyAir(
    double SeaLevelPascals,
    double SeaLevelDensity,
    double ScaleHeightMetres,
    double BoundaryMetres,
    double Gravity,
    double RadiusMetres,
    BodyTraits Traits,
    bool Wet = false)
{
    /// <summary>A body with no air at all.</summary>
    public static BodyAir Airless(double gravity, double radiusMetres, BodyTraits traits)
        => new(0.0, 0.0, 0.0, 0.0, gravity, radiusMetres, traits);

    /// <summary>Whether there is any air.</summary>
    public bool HasAir => SeaLevelDensity > 0.0 && SeaLevelPascals > 0.0 && ScaleHeightMetres > 0.0
                          && BoundaryMetres > 0.0;

    /// <summary>Air density (kg/m³) at an altitude over the mean radius, as KSA gives it.</summary>
    public double DensityAt(double altitude) => Profile(SeaLevelDensity, altitude);

    /// <summary>Air pressure (Pa) at an altitude over the mean radius, as KSA gives it.</summary>
    public double PressureAt(double altitude) => Profile(SeaLevelPascals, altitude);

    /// <summary>The air at an altitude, against the fixed references.</summary>
    public AmbientAir AirAt(double altitude)
        => HasAir
               ? new AmbientAir(DensityAt(altitude) / AmbientAir.ReferenceDensityKgPerM3,
                                PressureAt(altitude) / AmbientAir.ReferencePascals)
               : AmbientAir.None;

    /// <summary>The mass of air above a point (kg/m²), in KSA's own profile.</summary>
    public double ColumnAbove(double altitude) => DensityAt(altitude) * ScaleHeightMetres;

    /// <summary>
    /// The altitude where the air above weighs a given column (kg/m²): the height a layer calibrated by
    /// its column sits at on this body. Clamped to the ground and to the boundary.
    /// </summary>
    public double AltitudeOfColumn(double columnKgPerM2)
    {
        if (!HasAir || !(columnKgPerM2 > 0.0)) return 0.0;

        double surface = SeaLevelDensity * ScaleHeightMetres;
        if (columnKgPerM2 >= surface) return 0.0;

        return Math.Min(ScaleHeightMetres * Math.Log(surface / columnKgPerM2), BoundaryMetres);
    }

    /// <summary>The speed of sound (m/s): one number per body, since KSA's air is isothermal.</summary>
    public double SoundMetresPerSecond => HasAir ? Math.Sqrt(Traits.Gamma * SeaLevelPascals / SeaLevelDensity) : 0.0;

    /// <summary>The scale height hydrostatic balance gives this pressure, density and gravity (m).</summary>
    public double HydrostaticScaleHeightMetres
        => HasAir && Gravity > 0.0 ? SeaLevelPascals / (SeaLevelDensity * Gravity) : 0.0;

    /// <summary>Whether a burst raises white condensation here.</summary>
    public bool Condenses => HasAir && (Traits.Condensation ?? Wet);

    private double Profile(double seaLevel, double altitude)
    {
        if (!HasAir || !double.IsFinite(altitude) || altitude >= BoundaryMetres) return 0.0;

        return seaLevel * Math.Exp(-Math.Max(altitude, 0.0) / ScaleHeightMetres);
    }
}
