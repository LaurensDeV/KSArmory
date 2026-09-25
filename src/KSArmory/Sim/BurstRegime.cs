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

    // Condensation needs the moist air under the tropopause, which on Earth is about 11 km, 0.25 of sea
    // level's density; by 16 km, 0.13, the air is dry. Keyed on density so a wet body's tropopause lands
    // where its air thins to the same.
    private const double WetAbove = 0.25;
    private const double DryBelow = 0.13;

    /// <summary>
    /// How dry the air at the burst is for condensation, from none to one: one on a body that raises
    /// none at all, and from 11 to 16 km on Earth, above which Tightrope's ball raised no white cap and
    /// no Wilson cloud. It removes condensation and never the ground's own dust.
    /// </summary>
    public static double Dryness(BodyAir body, double burstAltitude)
    {
        if (!body.Condenses) return 1.0;

        double air = body.AirAt(burstAltitude).DensityRatio;
        if (!(air < WetAbove)) return 0.0;
        if (!(air > DryBelow)) return 1.0;

        double x = Math.Log(WetAbove / air) / Math.Log(WetAbove / DryBelow);
        return x * x * (3.0 - (2.0 * x));
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
