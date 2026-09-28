using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// How bright a thing looks to a seeker, and how strongly that seeker would lock onto it.
///
/// <para>Heat is radiant intensity in kW/sr, the unit a flare's specification is written in. Radar
/// is <see cref="RadarSignature.CrossSectionFor"/>'s m², which overstates every craft by the same
/// factor, so a decoy's radar peak is stated on that same scale rather than as a real cloud's.</para>
/// </summary>
public static class Signature
{
    /// <summary>A craft with its engines out: warm skin and whatever idles, in kW/sr.</summary>
    public const double AirframeKwPerSr = 1.0;

    /// <summary>
    /// Plume intensity per kilonewton of thrust actually being made, in kW/sr. A fighter's ~75 kN at
    /// military power reads 3 kW/sr, the open literature's order for a tail aspect.
    /// </summary>
    public const double PlumeKwPerSrPerKilonewton = 0.04;

    /// <summary>A round coasting after burnout, in kW/sr — a hot nose and a cooling nozzle.</summary>
    public const double CoastingRoundKwPerSr = 0.3;

    /// <summary>A round whose motor is burning, in kW/sr.</summary>
    public const double BurningRoundKwPerSr = 5.0;

    /// <summary>A craft's heat, from the thrust its engines are making.</summary>
    public static double HeatOfCraft(double thrustNewtons, double throttle)
    {
        double making = double.IsFinite(thrustNewtons) && double.IsFinite(throttle)
                            ? Math.Max(0.0, thrustNewtons) * Math.Clamp(throttle, 0.0, 1.0)
                            : 0.0;

        return AirframeKwPerSr + (making / 1000.0 * PlumeKwPerSrPerKilonewton);
    }

    /// <summary>A round's heat, which is whether its motor is burning.</summary>
    public static double HeatOfRound(bool burning) => burning ? BurningRoundKwPerSr : CoastingRoundKwPerSr;

    /// <summary>
    /// How strongly a seeker would lock onto something of this signature at this range and this far off
    /// its boresight. Zero outside the field of view.
    ///
    /// <para>Only a ratio is ever taken of it, so the units are the signature's over range normalised to
    /// a kilometre: heat falls as the square of range, a radar return as the fourth power, because the
    /// radar's own pulse has to get there as well. The boresight term favours what is centred in the
    /// field, which is what a seeker's tracking loop does with two sources in it.</para>
    /// </summary>
    public static double LockStrength(SeekerBand band, double signature, double rangeMetres,
                                      double offBoresightRad, double fieldRad)
    {
        if (band == SeekerBand.None) return 0.0;
        if (!(signature > 0.0) || !(rangeMetres > 0.0) || !(fieldRad > 0.0)) return 0.0;
        if (!double.IsFinite(offBoresightRad) || offBoresightRad > fieldRad) return 0.0;

        double km = rangeMetres / 1000.0;
        double spread = km * km;
        if (band == SeekerBand.Radar) spread *= spread;

        double u = offBoresightRad / fieldRad;
        return signature / spread * (1.0 - (u * u));
    }

    /// <summary>The closing speed of <paramref name="velocityEcl"/> on a seeker along the line to it.</summary>
    public static double ClosingSpeed(double3 seekerPosEcl, double3 seekerVelEcl, double3 positionEcl,
                                      double3 velocityEcl)
    {
        double3 line = positionEcl - seekerPosEcl;
        double range = Vec.Len(line);
        return range > 0.0 ? -Vec.Dot(velocityEcl - seekerVelEcl, line) / range : 0.0;
    }
}
