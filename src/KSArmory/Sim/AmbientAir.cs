namespace KSArmory;

/// <summary>
/// The air at a point, as its density and pressure against fixed physical references -- 1.225 kg/m³
/// and 101,325 Pa -- rather than against the sea level of whichever body it is over. That is what lets a
/// threshold calibrated in Earth's air hold anywhere: air of a given density is the same air on any body.
///
/// <para>Both are carried because a body's air need not be Earth's: the two ratios differ wherever its
/// temperature does, and the speed of sound is <c>√(γP/ρ)</c>.</para>
///
/// <para><see cref="Known"/> is false where the air could not be read. It is then treated as sea level,
/// which is what every read defaulted to before, but a caller that must not guess can tell.</para>
/// </summary>
internal readonly record struct AmbientAir(double DensityRatio, double PressureRatio, bool Known = true)
{
    /// <summary>The density the ratio is measured against (kg/m³): Earth's sea level, as a unit.</summary>
    public const double ReferenceDensityKgPerM3 = Medium.ReferenceDensityKgPerM3;

    /// <summary>The pressure the ratio is measured against (Pa).</summary>
    public const double ReferencePascals = BlastWave.SeaLevelPascals;

    /// <summary>Earth's sea-level air, which every law in this mod was written for.</summary>
    public static AmbientAir SeaLevel => new(1.0, 1.0);

    /// <summary>No air at all.</summary>
    public static AmbientAir None => new(0.0, 0.0);

    /// <summary>Air that could not be read, standing in as sea level.</summary>
    public static AmbientAir Unknown => new(1.0, 1.0, Known: false);

    /// <summary>Exactly sea level, known -- where every law must return what it always did.</summary>
    public bool IsSeaLevel => Known && DensityRatio == 1.0 && PressureRatio == 1.0;

    /// <summary>Whether there is any air to carry a wave.</summary>
    public bool HasAir => DensityRatio > 0.0 && PressureRatio > 0.0;

    /// <summary>The ambient pressure (Pa).</summary>
    public double Pascals => PressureRatio * ReferencePascals;

    /// <summary>The speed of sound (m/s), for a ratio of specific heats; zero with no air.</summary>
    public double SoundMetresPerSecond(double gamma)
        => HasAir ? Math.Sqrt(gamma * Pascals / (DensityRatio * ReferenceDensityKgPerM3)) : 0.0;
}
