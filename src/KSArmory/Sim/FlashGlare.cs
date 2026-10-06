namespace KSArmory;

/// <summary>
/// How much of a burst's glare reaches a viewer, given where they are looking.
///
/// <para><b>It never reaches nothing.</b> A fireball is twice as bright as the surface of the sun,
/// and light that bright does not stop at the edge of the frame: it scatters in the air between,
/// it veils across the optics, and it lights the whole landscape the viewer <em>is</em> looking at.
/// So turning away dims a burst and cannot abolish it, which is why the falloff lands on a floor
/// rather than on zero.</para>
///
/// <para>Full while the burst is in frame, because a source anywhere on the glass floods it.</para>
/// </summary>
public static class FlashGlare
{
    /// <summary>What the sun delivers at Earth, in W/m²: one sun of light.</summary>
    public const double SolarConstant = 1361.0;

    /// <summary>The share of a low burst's energy that leaves as heat and light.</summary>
    public const double ThermalFraction = 0.35;

    // The thermal pulse is a few of its own peak-times wide, so its peak power is the energy over
    // about that long.
    private const double PulseWidthInPeaks = 3.0;

    /// <summary>
    /// How much light a burst puts in the eye at <paramref name="range"/>, in suns: its thermal
    /// power at the pulse's peak spread over a sphere, against sunlight, and scaled by how bright
    /// the ball is drawn now against its peak. A 0.3 kt burst is about 60 suns at 2.4 km. Taken off
    /// the power rather than off the drawn ball's size, which at the instant of the flash is still
    /// small and put the same burst at 8. In the air it went off in (<see cref="ThermalAltitude"/>):
    /// a stratospheric burst gives more of itself as light, and over a shorter pulse.
    /// </summary>
    public static double Suns(double yieldKt, double range, double glow, double airRatio = 1.0)
    {
        double scale = ThermalAltitude.PulseScale(airRatio);
        double peakSeconds = scale == 1.0
                                 ? MushroomCloud.ThermalMaximumSeconds(yieldKt)
                                 : MushroomCloud.ThermalMaximumSeconds(yieldKt) * scale;
        if (!(peakSeconds > 0.0) || !(range > 1.0) || !(glow > 0.0)) return 0.0;

        double watts = ThermalAltitude.ThermalFraction(airRatio) * yieldKt * 4.184e12 / (PulseWidthInPeaks * peakSeconds);
        double atEye = watts / (4.0 * Math.PI * range * range);

        return atEye / SolarConstant * Math.Clamp(glow / MushroomCloud.PeakGlow, 0.0, 1.0);
    }

    // The light an eye in full night is adapted to, in suns. Real darkness is far less; this is a
    // screen, and nobody watching one is fully dark-adapted.
    private const double NightAdaptation = 0.01;

    /// <summary>
    /// The light the eye is used to, in suns: one by day, a hundredth at night, and the twilight
    /// between eased on the sun's height. A flash is judged against this, which is why the same
    /// burst blinds from much further at night.
    /// </summary>
    public static double AdaptedTo(double sunElevationDeg)
    {
        if (!double.IsFinite(sunElevationDeg)) return 1.0;

        double t = Math.Clamp((sunElevationDeg + 8.0) / 14.0, 0.0, 1.0);
        double day = t * t * (3.0 - (2.0 * t));

        return Math.Pow(10.0, (1.0 - day) * Math.Log10(NightAdaptation));
    }

    /// <summary>
    /// How many tenfold steps above what the eye is used to a flash has to be to white the view
    /// out. The eye answers light on a logarithmic scale, so a burst four times further off is
    /// 0.6 of a step dimmer, not a quarter as bright.
    /// </summary>
    public const double DecadesToWhite = 1.5;

    /// <summary>
    /// What an eye makes of a flash, in [0, 1]: nothing when it adds nothing to the light it is
    /// used to, and full at <see cref="DecadesToWhite"/> tenfold steps over it.
    /// </summary>
    public static double Level(double suns, double adaptedSuns)
    {
        if (!(suns > 0.0) || !(adaptedSuns > 0.0)) return 0.0;

        return Math.Clamp(Math.Log10(1.0 + (suns / adaptedSuns)) / DecadesToWhite, 0.0, 1.0);
    }

    /// <summary>
    /// The e-folding time of the flash's violet, on the drawn clock -- stretched like the double
    /// flash, so it is still there as the whiteout clears.
    /// </summary>
    public const double VioletSeconds = 0.8;

    /// <summary>
    /// How violet the flash is at an age, in [0, 1]: all of it at the burst, warming to the ball's
    /// own yellow as it cools.
    ///
    /// <para><b>Seen from a distance the whole blinded view is lavender</b>, whitest round the burst
    /// and deeper toward the edges: the burst's gamma rays set the air round it fluorescing in
    /// nitrogen's blue-violet lines, and the fireball is hottest, bluest, then. A warm veil from the
    /// first frame is the tell against every film of a test shot from far off.</para>
    /// </summary>
    public static double VioletShare(double ageSeconds)
        => !(ageSeconds >= 0.0) ? 0.0 : Math.Exp(-ageSeconds / VioletSeconds);

    // How fast a whiteout clears, as the time constant of its recovery: by day and in full night.
    private const double DayRecoverySeconds = 0.28;
    private const double NightRecoverySeconds = 2.2;

    /// <summary>
    /// The time constant the whiteout clears on, for an eye adapted to <paramref name="adaptedSuns"/>.
    ///
    /// <para><b>A dark-adapted eye recovers far more slowly</b>, which is why flash blindness from a
    /// night burst lasts seconds to minutes where a daylight one is gone in a second: the pigment the
    /// flash bleached is what the eye had been saving up to see in the dark. Eased on the log of the
    /// adaptation, as <see cref="AdaptedTo"/> is built. Seconds rather than minutes at night: it is a
    /// game, and a view that stays white for a minute is a player who cannot see what hit them.</para>
    /// </summary>
    public static double RecoverySeconds(double adaptedSuns)
    {
        if (!(adaptedSuns > 0.0)) return DayRecoverySeconds;

        double night = Math.Clamp(Math.Log10(adaptedSuns) / Math.Log10(NightAdaptation), 0.0, 1.0);
        return DayRecoverySeconds * Math.Pow(NightRecoverySeconds / DayRecoverySeconds, night);
    }

    /// <summary>What still reaches a viewer with the burst directly behind them.</summary>
    public const double ScatteredFloor = 0.12;

    /// <summary>
    /// How far past the edge of the field the glare takes to fall to that floor, in degrees.
    ///
    /// <para>Wide on purpose. A source a few degrees outside the frame is still in the lens, and a
    /// cut at the frame edge would make the flash switch on and off as a burst drifted across
    /// it — which is a worse artefact than the one this exists to fix.</para>
    /// </summary>
    public const double FalloffDeg = 70.0;

    /// <summary>
    /// The fraction of the glare that reaches the eye, between <see cref="ScatteredFloor"/> and 1.
    /// </summary>
    /// <param name="offAxisDeg">Angle between where the viewer is looking and the burst.</param>
    /// <param name="halfFieldDeg">Half the viewer's field of view.</param>
    public static double Reaching(double offAxisDeg, double halfFieldDeg)
    {
        // Anything unreadable gives the whole of it. A flash that should not have fired is a
        // surprise somebody reports; one that did not fire is a feature silently missing.
        if (!double.IsFinite(offAxisDeg) || !double.IsFinite(halfFieldDeg)) return 1.0;

        double edge = Math.Max(halfFieldDeg, 1.0);
        if (offAxisDeg <= edge) return 1.0;

        double t = Math.Clamp((offAxisDeg - edge) / FalloffDeg, 0.0, 1.0);

        return 1.0 - ((1.0 - ScatteredFloor) * (t * t * (3.0 - (2.0 * t))));
    }
}
