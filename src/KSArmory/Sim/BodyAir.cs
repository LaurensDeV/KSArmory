namespace KSArmory;

/// <summary>
/// A body's air as numbers, so nothing downstream reads a body or knows which one it is on. Built on the
/// KSA side from what the body declares and from <see cref="BodyTraits"/> for what it does not.
///
/// <para><b>It mirrors KSA's own model exactly</b>, because the round's drag and the burst must agree
/// about the same air: <see cref="Layers"/> is KSA's layered profile, clamped to sea level below the mean
/// radius and cut to nothing at the boundary height, and the column above a point is hydrostatic,
/// <c>P(h)/g</c>. Without layers the air is isothermal, <c>SeaLevel·exp(-h/H)</c>, which is what the
/// rigs fly. The speed of sound is the surface's, one number per body.</para>
/// </summary>
/// <param name="SeaLevelPascals">Pressure at the mean radius (Pa).</param>
/// <param name="SeaLevelDensity">Density at the mean radius (kg/m³).</param>
/// <param name="ScaleHeightMetres">The scale height at the surface (m); hydrostatic for a layered profile.</param>
/// <param name="BoundaryMetres">Where KSA stops the air (m over the mean radius).</param>
/// <param name="Gravity">Surface gravity (m/s²).</param>
/// <param name="RadiusMetres">Mean radius (m).</param>
/// <param name="Traits">What the body's <c>Bodies.xml</c> entry says, or the default.</param>
/// <param name="Wet">Whether the body has weather or an ocean, which is where condensation defaults from.</param>
/// <param name="Layers">KSA's layered profile, or null for the isothermal one.</param>
internal readonly record struct BodyAir(
    double SeaLevelPascals,
    double SeaLevelDensity,
    double ScaleHeightMetres,
    double BoundaryMetres,
    double Gravity,
    double RadiusMetres,
    BodyTraits Traits,
    bool Wet = false,
    AtmosphereProfile? Layers = null)
{
    public static BodyAir Airless(double gravity, double radiusMetres, BodyTraits traits)
        => new(0.0, 0.0, 0.0, 0.0, gravity, radiusMetres, traits);

    public static BodyAir Layered(AtmosphereProfile layers, double boundaryMetres, double gravity,
                                  double radiusMetres, BodyTraits traits, bool wet)
    {
        (double pascals, _, double density) = layers.At(0.0);
        double scaleHeight = density > 0.0 && gravity > 0.0 ? pascals / (density * gravity) : 0.0;
        return new BodyAir(pascals, density, scaleHeight, boundaryMetres, gravity, radiusMetres, traits, wet, layers);
    }

    public bool HasAir => SeaLevelDensity > 0.0 && SeaLevelPascals > 0.0 && ScaleHeightMetres > 0.0
                          && BoundaryMetres > 0.0;

    /// <summary>Air density (kg/m³) at an altitude over the mean radius, as KSA gives it.</summary>
    public double DensityAt(double altitude) => Profile(altitude, density: true);

    /// <summary>Air pressure (Pa) at an altitude over the mean radius, as KSA gives it.</summary>
    public double PressureAt(double altitude) => Profile(altitude, density: false);

    /// <summary>The air at an altitude, against the fixed references.</summary>
    public AmbientAir AirAt(double altitude)
        => HasAir
               ? new AmbientAir(DensityAt(altitude) / AmbientAir.ReferenceDensityKgPerM3,
                                PressureAt(altitude) / AmbientAir.ReferencePascals)
               : AmbientAir.None;

    /// <summary>The mass of air above a point (kg/m²), in KSA's own profile.</summary>
    public double ColumnAbove(double altitude)
        => Layers is null ? DensityAt(altitude) * ScaleHeightMetres
                          : Gravity > 0.0 ? PressureAt(altitude) / Gravity : 0.0;

    /// <summary>
    /// The altitude where the air above weighs a given column (kg/m²): the height a layer calibrated by
    /// its column sits at on this body. Clamped to the ground and to the boundary.
    /// </summary>
    public double AltitudeOfColumn(double columnKgPerM2)
    {
        if (!HasAir || !(columnKgPerM2 > 0.0)) return 0.0;

        double surface = ColumnAbove(0.0);
        if (columnKgPerM2 >= surface) return 0.0;
        if (Layers is null) return Math.Min(ScaleHeightMetres * Math.Log(surface / columnKgPerM2), BoundaryMetres);

        BodyAir self = this;
        return Descend(h => self.ColumnAbove(h) > columnKgPerM2);
    }

    /// <summary>
    /// The altitude where the air thins to a density (kg/m³), clamped to the ground and to the boundary.
    /// </summary>
    public double AltitudeOfDensity(double densityKgPerM3)
    {
        if (!HasAir || !(densityKgPerM3 > 0.0) || densityKgPerM3 >= SeaLevelDensity) return 0.0;
        if (Layers is null) return Math.Min(ScaleHeightMetres * Math.Log(SeaLevelDensity / densityKgPerM3), BoundaryMetres);

        BodyAir self = this;
        return Descend(h => self.DensityAt(h) > densityKgPerM3);
    }

    /// <summary>The speed of sound (m/s): one number per body, since KSA's air is isothermal.</summary>
    public double SoundMetresPerSecond => HasAir ? Math.Sqrt(Traits.Gamma * SeaLevelPascals / SeaLevelDensity) : 0.0;

    /// <summary>The scale height hydrostatic balance gives this pressure, density and gravity (m).</summary>
    public double HydrostaticScaleHeightMetres
        => HasAir && Gravity > 0.0 ? SeaLevelPascals / (SeaLevelDensity * Gravity) : 0.0;

    /// <summary>Whether a burst raises white condensation here.</summary>
    public bool Condenses => HasAir && (Traits.Condensation ?? Wet);

    private double Profile(double altitude, bool density)
    {
        if (!HasAir || !double.IsFinite(altitude) || altitude >= BoundaryMetres) return 0.0;

        double h = Math.Max(altitude, 0.0);
        if (Layers is { } layers)
        {
            (double pascals, _, double rho) = layers.At(h);
            return density ? rho : pascals;
        }

        return (density ? SeaLevelDensity : SeaLevelPascals) * Math.Exp(-h / ScaleHeightMetres);
    }

    // The highest altitude still below the condition's edge, by bisection: every profile thins upwards.
    private double Descend(Func<double, bool> below)
    {
        double low = 0.0, high = BoundaryMetres;
        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5 * (low + high);
            if (below(mid)) low = mid;
            else high = mid;
        }

        return low;
    }
}
