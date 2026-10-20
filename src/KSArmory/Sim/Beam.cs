namespace KSArmory;

/// <summary>
/// What a <see cref="BeamProfile"/> does to the part it lands on: the spot's size at a range, what
/// the air leaves of the power, and the share of the part's health that costs per second.
/// </summary>
public static class Beam
{
    /// <summary>
    /// The spot's radius (m) at <paramref name="rangeMetres"/>: the diffraction limit of the aperture
    /// and the pointing jitter, added in quadrature.
    /// </summary>
    public static double SpotRadiusMetres(BeamProfile beam, double rangeMetres)
    {
        double range = Math.Max(rangeMetres, 0.0);
        double diffraction = 1.22 * beam.WavelengthMetres * range / beam.ApertureMetres;
        double jitter = beam.JitterRad * range;

        // A focused beam is never tighter than a centimetre: the burn spreads in the skin.
        return Math.Max(Math.Sqrt((diffraction * diffraction) + (jitter * jitter)), 0.01);
    }

    /// <summary>
    /// What reaches the target, 0 to 1: Beer-Lambert over the path, at the mean of the air density
    /// ratio at its two ends — one where there is no air.
    /// </summary>
    public static double Transmission(BeamProfile beam, double rangeMetres, double densityRatioAtEye,
                                      double densityRatioAtSpot)
    {
        double density = Math.Max(0.0, 0.5 * (densityRatioAtEye + densityRatioAtSpot));
        return Math.Exp(-beam.ExtinctionPerMetre * density * Math.Max(rangeMetres, 0.0));
    }

    /// <summary>The power per area on the spot (W/m²).</summary>
    public static double Irradiance(BeamProfile beam, double rangeMetres, double densityRatioAtEye,
                                    double densityRatioAtSpot)
    {
        double r = SpotRadiusMetres(beam, rangeMetres);
        return beam.PowerWatts * Transmission(beam, rangeMetres, densityRatioAtEye, densityRatioAtSpot)
               / (Math.PI * r * r);
    }

    /// <summary>The share of a part's health the beam burns per second it is held on it.</summary>
    public static double HealthPerSecond(BeamProfile beam, double rangeMetres, double densityRatioAtEye,
                                         double densityRatioAtSpot)
        => Irradiance(beam, rangeMetres, densityRatioAtEye, densityRatioAtSpot) / beam.BurnThroughJoulesPerM2;
}
