namespace KSArmory;

/// <summary>
/// A high-energy laser an optical head fires along its own line of sight: what it puts out, through
/// what aperture, and how long its load of chemicals lasts. An <see cref="OpticProfile"/> without one only
/// designates.
/// </summary>
public sealed class BeamProfile
{
    public float PowerWatts = 1.0e6f;

    public float WavelengthMetres = 1.315e-6f;

    /// <summary>The telescope the beam leaves through, which sets how tight it can be focused.</summary>
    public float ApertureMetres = 1.5f;

    /// <summary>How far the pointing wanders (rad), which smears the spot beyond diffraction.</summary>
    public float JitterRad = 0.2e-6f;

    /// <summary>
    /// Energy per area that burns a skin through (J/m²), so one part's whole health. 6 kJ/cm² puts a
    /// 1 MW beam's kill at 100 km from 12 km up in the 3-5 s the ABL's dwell is quoted at.
    /// </summary>
    public float BurnThroughJoulesPerM2 = 6.0e7f;

    /// <summary>What the air takes out per metre at sea-level density, scaled by the density on the path.</summary>
    public float ExtinctionPerMetre = 2.0e-5f;

    /// <summary>
    /// Seconds of lasing a full load of chemicals gives: about twenty 5 s shots. A COIL burns chlorine,
    /// hydrogen peroxide and iodine to make its light, so what runs out is those, not a battery.
    /// </summary>
    public float ChemicalSeconds = 100f;
}
