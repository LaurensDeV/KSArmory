namespace KSArmory;

/// <summary>
/// A burst's blast in the air it went off in, by Glasstone and Dolan's rule (§3.68): the <b>efficiency</b>
/// set by the air at the burst, and <b>Sachs' scaling</b> by the air where the pressure is wanted. So a
/// high burst loses blast to radiation where it happens, and what is left still hits the ground as hard
/// as the ground's own air carries it.
///
/// <para>Every entry returns exactly what the sea-level law it wraps returns when the air is sea level,
/// known, to the bit -- <c>SeaLevelPinTests</c> holds the laws and <c>BlastAltitudeTests</c> this.</para>
/// </summary>
internal static class BlastAltitude
{
    // Blast efficiency against the pressure at the burst, as a share of sea level's, interpolated in
    // log η against log P. The first five are G&D's Table 3.68 at 12, 18, 27, 37 and 46 km (US
    // Standard pressures). Past its end the table is extrapolated: X-rays escape the air from about
    // 49 km, a quarter of the energy by 61, and from about 80 km there is no local fireball -- so the
    // last knot is set where Teak, 3.8 Mt at 77 km, is not felt on the ground under it, which it was not.
    private static readonly (double Pressure, double Efficiency)[] Knots =
    [
        (0.1915, 1.0),
        (0.0741, 0.9),
        (0.0182, 0.75),
        (0.00428, 0.55),
        (0.00127, 0.3),
        (1.9e-4, 0.05),
        (1.04e-5, 1.0e-7),
    ];

    /// <summary>
    /// What share of a burst's energy goes into its blast, against a sea-level burst's, for the air it
    /// went off in: one up to about 12 km on Earth, a half at 37, and nothing where there is no air.
    /// A charge too small to be nuclear, or air that could not be read, keeps it all.
    /// </summary>
    public static double Efficiency(double chargeKg, AmbientAir burstAir)
    {
        if (chargeKg < MushroomCloud.ThresholdKg || !burstAir.Known) return 1.0;

        double p = burstAir.PressureRatio;
        if (!(p > 0.0) || !(burstAir.DensityRatio > 0.0)) return 0.0;
        if (p >= Knots[0].Pressure) return 1.0;

        for (int i = 1; i < Knots.Length; i++)
        {
            if (p < Knots[i].Pressure) continue;

            (double pHi, double eHi) = Knots[i - 1];
            (double pLo, double eLo) = Knots[i];
            double t = Math.Log(pHi / p) / Math.Log(pHi / pLo);
            return eHi * Math.Pow(eLo / eHi, t);
        }

        // Below the last knot the blast falls with the air.
        (double pEnd, double eEnd) = Knots[^1];
        return eEnd * p / pEnd;
    }

    /// <summary>The charge (kg) whose sea-level blast this burst's is: its own, times <see cref="Efficiency"/>.</summary>
    public static double BlastChargeKg(double chargeKg, AmbientAir burstAir)
    {
        double efficiency = Efficiency(chargeKg, burstAir);
        return efficiency == 1.0 ? chargeKg : chargeKg * efficiency;
    }

    /// <summary>
    /// The charge (kg) the damage law is judged on: the blast's, with a floor that rises back to the
    /// whole charge where the air is too thin to stop the X-rays within the lethal radius.
    ///
    /// <para>That floor stands in for X-ray fluence, which is what breaks a craft near a burst above the
    /// air, over a wider reach than blast ever had: letting damage go with the blast would make a
    /// burst beside a satellite harmless. So a burst in vacuum is judged exactly as before.</para>
    /// </summary>
    /// <param name="chargeKg">The burst's charge (kg).</param>
    /// <param name="burstAir">The air at the burst.</param>
    /// <param name="xrayOpacity">The body's X-ray opacity against Earth's air (<see cref="BodyTraits.XRayOpacity"/>).</param>
    public static double EquivalentChargeKg(double chargeKg, AmbientAir burstAir, double xrayOpacity = 1.0)
    {
        double efficiency = Efficiency(chargeKg, burstAir);
        if (efficiency == 1.0) return chargeKg;

        return chargeKg * Math.Max(efficiency, XRayReach(chargeKg, burstAir, xrayOpacity));
    }

    /// <summary>
    /// How far the burst's X-rays carry against its lethal radius, as a share in [0, 1]: nothing where
    /// the air stops them within a tenth of it, everything where they run the whole of it. The column
    /// they cross is the one the X-ray layer sits under (<see cref="XRayGlow.LayerColumnKgPerM2"/>).
    /// </summary>
    public static double XRayReach(double chargeKg, AmbientAir burstAir, double xrayOpacity = 1.0)
    {
        double density = burstAir.DensityRatio * AmbientAir.ReferenceDensityKgPerM3;
        if (!(density > 0.0)) return 1.0;

        double path = XRayGlow.LayerColumnKgPerM2 / (density * Math.Max(xrayOpacity, 1.0e-6));
        double lethal = Warhead.LethalRadius(chargeKg);
        if (!(lethal > 0.0)) return 1.0;

        double x = Math.Clamp(Math.Log10(path / lethal) + 1.0, 0.0, 1.0);
        return x * x * (3.0 - (2.0 * x));
    }

    /// <summary>
    /// The peak overpressure (Pa) at a distance from the burst: the blast's charge from the air at the
    /// burst, in the air at the target by Sachs -- distance scaled by the cube root of its pressure,
    /// and the pressure by the pressure. Nothing where the target has no air to carry it.
    /// </summary>
    public static double PeakPascals(double chargeKg, AmbientAir burstAir, AmbientAir targetAir,
                                     double distanceMetres, double reflection = BlastWave.SurfaceReflection)
    {
        if (!targetAir.HasAir) return 0.0;

        double charge = BlastChargeKg(chargeKg, burstAir);
        double sachs = Math.Cbrt(targetAir.PressureRatio);

        return BlastWave.PeakOverpressurePascals(charge, distanceMetres * sachs, targetAir.Pascals, reflection);
    }

    /// <summary>
    /// How long that front pushes (s), by Sachs: the sea-level phase at the scaled distance, stretched by
    /// the inverse cube root of the target's pressure and by sea level's sound speed over its own.
    /// </summary>
    public static double PositivePhaseSeconds(double chargeKg, AmbientAir burstAir, AmbientAir targetAir,
                                              double targetSoundMetresPerSecond, double distanceMetres,
                                              double reflection = BlastWave.SurfaceReflection)
    {
        if (!targetAir.HasAir || !(targetSoundMetresPerSecond > 0.0)) return 0.0;

        double charge = BlastChargeKg(chargeKg, burstAir);
        double sachs = Math.Cbrt(targetAir.PressureRatio);
        double seaLevel = BlastWave.PositivePhaseSeconds(charge, distanceMetres * sachs, reflection);

        return seaLevel / sachs * (BlastWave.SoundMetresPerSecond / targetSoundMetresPerSecond);
    }

    /// <summary>
    /// How far the front has run (m), in the burst's air: Sedov on the blast's energy and the air's
    /// density, then the air's own sound speed -- which is Sachs' scaling of the sea-level front.
    /// <paramref name="reflection"/> is the energy's multiple for the ground under it: two on it, one
    /// in free air.
    /// </summary>
    public static double FrontRadius(double chargeKg, double age, AmbientAir burstAir,
                                     double soundMetresPerSecond, double reflection = BlastWave.SurfaceReflection)
    {
        double kt = MushroomCloud.KilotonsFor(BlastChargeKg(chargeKg, burstAir));
        double density = burstAir.DensityRatio * AmbientAir.ReferenceDensityKgPerM3;

        return MushroomCloud.ShockRadiusIn(kt, age, density, soundMetresPerSecond, reflection);
    }

    /// <summary>When that front reaches a distance (s); never, where the burst's air carries none.</summary>
    public static double FrontArrivalSeconds(double chargeKg, double distanceMetres, AmbientAir burstAir,
                                             double soundMetresPerSecond,
                                             double reflection = BlastWave.SurfaceReflection)
    {
        double kt = MushroomCloud.KilotonsFor(BlastChargeKg(chargeKg, burstAir));
        if (!(kt > 0.0)) return double.PositiveInfinity;

        double density = burstAir.DensityRatio * AmbientAir.ReferenceDensityKgPerM3;
        return MushroomCloud.ShockArrivalIn(kt, distanceMetres, density, soundMetresPerSecond, reflection);
    }
}
