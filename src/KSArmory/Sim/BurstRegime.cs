namespace KSArmory;

/// <summary>
/// Which of a burst's behaviours the air it went off in switches on, each keyed on the physics that
/// decides it rather than on one density: pressure for the blast, the fireball against the scale height
/// for the mushroom, and the column above for whether X-rays escape. On Earth each reproduces the
/// threshold it replaces; on another body each moves by what actually differs there.
/// </summary>
internal static class BurstRegime
{
    /// <summary>The scale height the mushroom's threshold was set on (m): Earth's, as KSA declares it.</summary>
    public const double ReferenceScaleHeightMetres = 8_000.0;

    /// <summary>Whether the burst makes a blast at all.</summary>
    public static bool Blasts(double chargeKg, AmbientAir burstAir) => BlastAltitude.Efficiency(chargeKg, burstAir) > 0.0;

    /// <summary>
    /// Whether the burst stands as a mushroom rather than rising as a ball. A mushroom needs a fireball
    /// small beside the scale height, so that buoyancy has a stratified column to rise through; the ball
    /// grows as the cube root of the thinning air, so the density where it stops is
    /// <see cref="MushroomCloud.ThinAirRatio"/> times the cube of Earth's scale height over the body's.
    /// G&amp;D see no mushroom above about 30 km whatever the yield, so the yield is calibrated out.
    /// </summary>
    public static bool StandsAsMushroom(AmbientAir burstAir, double scaleHeightMetres)
    {
        if (!burstAir.Known) return true;

        double h = scaleHeightMetres > 0.0 ? scaleHeightMetres : ReferenceScaleHeightMetres;
        double ratio = ReferenceScaleHeightMetres / h;
        double threshold = ratio == 1.0 ? MushroomCloud.ThinAirRatio : MushroomCloud.ThinAirRatio * ratio * ratio * ratio;

        return !(burstAir.DensityRatio < threshold);
    }

    /// <summary>
    /// Whether the burst is above enough air for its X-rays to escape to the layer rather than being
    /// stopped round the ball: the column above it lighter than the one <see cref="XRayGlow.AirRatio"/>
    /// has under Earth's scale height, over the body's opacity.
    /// </summary>
    public static bool XRaysEscape(BodyAir body, double burstAltitude)
    {
        if (!body.HasAir) return true;

        double threshold = XRayGlow.AirRatio * AmbientAir.ReferenceDensityKgPerM3 * ReferenceScaleHeightMetres
                           / Math.Max(body.Traits.XRayOpacity, 1.0e-6);
        return body.ColumnAbove(burstAltitude) < threshold;
    }
}
