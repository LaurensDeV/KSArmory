namespace KSArmory;

/// <summary>
/// A burst's heat and light by the air it went off in, each a multiple of the sea-level law and exactly
/// one at sea level: when its thermal pulse peaks, what share of its energy leaves as heat and light,
/// how long the ball goes on glowing, and whether the pulse is still double.
///
/// <para>Keyed on the density against the fixed reference (<see cref="AmbientAir"/>), so Glasstone and
/// Dolan's altitudes -- given here as the US Standard densities they correspond to -- hold on any body.
/// Air that is exactly zero is the vacuum case, which the flash answers on its own.</para>
/// </summary>
internal static class ThermalAltitude
{
    // G&D §7.88: the second maximum at 0.0417 W^0.44 s below 15,000 ft, and 0.038 W^0.44 (ρ/ρ₀)^0.36
    // above, which holds to 100,000 ft. Blended over a decade of air rather than switched, and held at
    // its limit rather than extrapolated.
    private const double LowLawBelow = 0.63;
    private const double HighLawFrom = 0.063;
    private const double HighLawLimit = 0.0147;
    private const double HighLawRatio = 0.038 / 0.0417;
    private const double HighLawExponent = 0.36;

    /// <summary>When the thermal pulse peaks, as a multiple of its time at sea level.</summary>
    public static double PulseScale(double airRatio)
    {
        if (!double.IsFinite(airRatio) || airRatio >= LowLawBelow) return 1.0;

        double high = HighLawRatio * Math.Pow(Math.Max(airRatio, HighLawLimit), HighLawExponent);
        double s = LogStep(LowLawBelow, HighLawFrom, Math.Max(airRatio, 0.0));
        return 1.0 + ((high - 1.0) * s);
    }

    // G&D §7.90: a third of the energy low down, three fifths through the stratosphere where the ball is
    // large and slow to cool, a quarter once X-rays escape the air round it, a few percent above it.
    private static readonly (double Air, double Fraction)[] Fractions =
    [
        (0.254, 0.35),
        (0.0147, 0.6),
        (0.00103, 0.6),
        (2.0e-4, 0.25),
        (1.7e-5, 0.25),
        (1.2e-5, 0.03),
    ];

    /// <summary>
    /// The share of the burst's energy that leaves as heat and light: <see cref="FlashGlare.ThermalFraction"/>
    /// at sea level.
    /// </summary>
    public static double ThermalFraction(double airRatio)
    {
        if (!double.IsFinite(airRatio) || airRatio >= Fractions[0].Air) return FlashGlare.ThermalFraction;
        if (!(airRatio > Fractions[^1].Air)) return Fractions[^1].Fraction;

        for (int i = 1; i < Fractions.Length; i++)
        {
            if (airRatio < Fractions[i].Air) continue;

            (double hiAir, double hi) = Fractions[i - 1];
            (double loAir, double lo) = Fractions[i];
            double t = Math.Log(hiAir / airRatio) / Math.Log(hiAir / loAir);
            return hi + ((lo - hi) * t);
        }

        return Fractions[^1].Fraction;
    }

    // How long the ball glows against sea level's: as the fourth root of the air to Orange's (3.8 Mt at
    // 43 km, about 18 s), and faster above it to Teak's (3.8 Mt at 77 km, about 2.5 s).
    private const double OrangeAir = 2.1e-3;
    private const double BelowOrangeExponent = 0.44;

    /// <summary>How long the ball goes on glowing, as a multiple of its time at sea level.</summary>
    public static double GlowScale(double airRatio)
    {
        if (!double.IsFinite(airRatio) || airRatio >= 1.0) return 1.0;
        if (!(airRatio > 0.0)) return 0.0;

        double orange = Math.Pow(OrangeAir, 0.25);
        return airRatio >= OrangeAir
                   ? Math.Pow(airRatio, 0.25)
                   : orange * Math.Pow(airRatio / OrangeAir, BelowOrangeExponent);
    }

    // The pulse's minimum fades from 100,000 to 130,000 ft (G&D §7.89): thin air is transparent to the
    // radiation the shock front hides lower down, so the two maxima merge into one.
    private const double DoubleBelow = 0.0147;
    private const double SingleAbove = 0.0035;

    /// <summary>How far the double pulse has merged into one, from nothing at sea level to one.</summary>
    public static double SinglePulse(double airRatio)
    {
        if (!double.IsFinite(airRatio) || airRatio >= DoubleBelow) return 0.0;

        return LogStep(DoubleBelow, SingleAbove, Math.Max(airRatio, 0.0));
    }

    // A smoothstep from `from` to `to` in log air, falling as the air thins.
    private static double LogStep(double from, double to, double air)
    {
        if (!(air > 0.0)) return 1.0;

        double x = Math.Clamp(Math.Log(from / air) / Math.Log(from / to), 0.0, 1.0);
        return x * x * (3.0 - (2.0 * x));
    }
}
