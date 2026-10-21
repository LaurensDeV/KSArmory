using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// What a nuclear burst irradiates: the prompt flash, the fallout it lays downwind, the debris still
/// aloft, and the belt a burst above the air leaves in a magnetic field. Dose rates in Sv/s, unshielded,
/// as KSA's own gauge reads them. <c>docs/NUCLEAR-RADIATION.md</c> has the sources and the fits.
/// </summary>
internal static class BurstRadiation
{
    public const double SecondsPerHour = 3600.0;

    // DS02 free-in-air kerma at Hiroshima (15 kt): 4.2 Gy at 1.17 km slant, 0.8 at 1.62, 0.15 at 2.09.
    public const double PromptSvM2PerKt = 5.8e6;
    public const double PromptColumnKgPerM2 = 527.0;

    // Hydrodynamic enhancement: puts 1 Mt's 5 Sv at 2.9 km, as Glasstone & Dolan fig. 8.64 has it.
    public const double PromptYieldExponent = 0.075;
    public const double PromptReferenceKt = 15.0;

    // 1 kt of fission products over a square mile reads 2,900 R/h at H+1 (Glasstone & Dolan ch. 9),
    // at 0.0087 Sv/R.
    public const double PlaneSvM2PerSecondPerKt = 2900.0 * 0.0087 * 2.589_988e6 / SecondsPerHour;

    // Fallout gamma through air, from the altitude correction aerial surveys fly with: 0.007 per metre.
    public const double GammaColumnKgPerM2 = 171.0;

    // A point holding what that plane holds, so the cloud and the ground share one calibration: an
    // infinite plane read 1 m over it is Q/2 · E1(1 m / 140 m), and E1 there is 4.372.
    public const double PointSvM2PerSecondPerKt = PlaneSvM2PerSecondPerKt / (0.5 * 4.372);

    public const double LocalShare = 0.6;
    public const double StemShare = 0.3;

    // Glasstone & Dolan's reference wind, 15 mph. KSA has no wind, so this is a stated reference.
    public const double WindMetresPerSecond = 6.7;

    // Fitted so a 1 Mt surface burst's hotline reads about 1,500, 700, 270 and 70 R/h at H+1 at 35, 65, 110
    // and 180 km: within a factor of two of Glasstone & Dolan's idealised pattern.
    public const double SettleMetresPerSecond = 3.0;
    public const double PlumeCoreShare = 0.3;
    public const double CrosswindSpread = 0.04;

    // Before the first minute the fission products' gamma is the prompt dose, not fallout.
    public const double PromptPhaseSeconds = 60.0;

    // Starfish (1.4 Mt, about half fission) is taken to have raised its shell to 4 Sv/h.
    public const double BeltMilliSvPerHourPerFissionKt = 4000.0 / 700.0;
    public const double BeltReferenceL = 1.2;
    public const double BeltLongLifeL = 1.25;
    public const double BeltLongLifeSeconds = 365.25 * 86_400.0;
    public const double BeltShortestLifeSeconds = 5.0 * 86_400.0;

    /// <summary>Fission's share of a yield: a fission weapon to 100 kt, half for a megaton and up.</summary>
    public static double FissionShare(double kt)
    {
        if (!(kt > 100.0)) return 1.0;
        if (kt >= 1000.0) return 0.5;

        return 1.0 - (0.5 * Math.Log10(kt / 100.0));
    }

    /// <summary>The initial radiation, gamma and neutron, delivered over its first minute (Sv).</summary>
    public static double PromptDoseSv(double kt, double slantMetres, double columnKgPerM2)
    {
        if (!(kt > 0.0) || !double.IsFinite(slantMetres)) return 0.0;

        double r = Math.Max(slantMetres, MushroomCloud.FireballRadius(kt));
        double reach = PromptColumnKgPerM2 * Math.Pow(kt / PromptReferenceKt, PromptYieldExponent);

        return PromptSvM2PerKt * kt / (r * r) * Math.Exp(-Math.Max(columnKgPerM2, 0.0) / reach);
    }

    /// <summary>How long the flash takes to deliver most of itself (s).</summary>
    public static double PromptSeconds(double kt) => Math.Clamp(Math.Pow(Math.Max(kt, 0.0), 0.2), 0.3, 10.0);

    /// <summary>That dose as a rate at an age (Sv/s).</summary>
    public static double PromptRate(double doseSv, double kt, double ageSeconds)
    {
        if (!(ageSeconds >= 0.0) || !(doseSv > 0.0)) return 0.0;

        double tau = PromptSeconds(kt);
        return doseSv / tau * Math.Exp(-ageSeconds / tau);
    }

    /// <summary>Way–Wigner: fission products against their H+1 rate, held at the first minute's.</summary>
    public static double Decay(double ageSeconds)
        => Math.Pow(Math.Max(ageSeconds, PromptPhaseSeconds) / SecondsPerHour, -1.2);

    /// <summary>What reaches a point <paramref name="columnKgPerM2"/> of air from a gamma source.</summary>
    public static double GammaThrough(double columnKgPerM2)
        => Math.Exp(-Math.Max(columnKgPerM2, 0.0) / GammaColumnKgPerM2);

    /// <summary>
    /// One burst's fallout: its fission yield, how much of it comes down locally, and the plume it lands
    /// in. A plume with no wind is the stem's disc alone, which is a burst on a body with no air.
    /// </summary>
    public readonly record struct Plume(
        double FissionKt, double Local, double Wind, double MeanFallSeconds, double StemSigma, double CapRadius)
    {
        public static Plume For(double kt, double coupling, bool hasAir)
        {
            double fission = kt * FissionShare(kt);
            double local = LocalShare * Math.Clamp(coupling, 0.0, 1.0);
            double fall = Math.Max(MushroomCloud.CloudTop(kt) / SettleMetresPerSecond, PromptPhaseSeconds);

            return new Plume(fission, local, hasAir ? WindMetresPerSecond : 0.0, fall,
                             3.0 * MushroomCloud.FireballRadius(kt), MushroomCloud.CapRadius(kt));
        }

        public bool Lays => FissionKt > 0.0 && Local > 0.0;

        private double StemPart => Wind > 0.0 ? StemShare : 1.0;

        /// <summary>The H+1 rate 1 m over the ground at a place downwind and across (Sv/s).</summary>
        public double UnitRate(double downwind, double across)
        {
            if (!Lays) return 0.0;

            double total = PlaneSvM2PerSecondPerKt * FissionKt * Local;
            double s2 = StemSigma * StemSigma;
            double stem = StemPart * Math.Exp(-((downwind * downwind) + (across * across)) / (2.0 * s2))
                          / (2.0 * Math.PI * s2);

            double plume = 0.0;
            if (Wind > 0.0 && downwind > 0.0)
            {
                // Fall times spread exponentially, carried downwind at the wind's speed.
                double mean = Wind * MeanFallSeconds;
                double along = Math.Exp(-downwind / mean) / mean;
                double sigma = (PlumeCoreShare * CapRadius) + (CrosswindSpread * downwind);
                plume = (1.0 - StemPart) * along
                        * Math.Exp(-(across * across) / (2.0 * sigma * sigma)) / (Math.Sqrt(2.0 * Math.PI) * sigma);
            }

            return total * (stem + plume);
        }

        /// <summary>When what lands at a place downwind gets there (s).</summary>
        public double ArrivalSeconds(double downwind)
            => Wind > 0.0 && downwind > 0.0 ? Math.Max(downwind / Wind, PromptPhaseSeconds) : PromptPhaseSeconds;

        /// <summary>How much of the fallout has landed anywhere by an age, as a share of all of it.</summary>
        public double Landed(double ageSeconds)
        {
            if (!(ageSeconds > 0.0)) return 0.0;

            double stem = StemPart * Ramp.Smooth(0.0, PromptPhaseSeconds, ageSeconds);
            if (!(Wind > 0.0)) return stem;

            return stem + ((1.0 - StemPart) * (1.0 - Math.Exp(-ageSeconds / MeanFallSeconds)));
        }

        /// <summary>The rate 1 m over the ground at a place and an age (Sv/s).</summary>
        public double GroundRate(double downwind, double across, double ageSeconds)
        {
            double arrive = ArrivalSeconds(downwind);
            double on = Ramp.Smooth(0.8 * arrive, (1.2 * arrive) + PromptPhaseSeconds, ageSeconds);

            return on > 0.0 ? UnitRate(downwind, across) * Decay(ageSeconds) * on : 0.0;
        }

        /// <summary>The fission products still aloft, in kt of fission.</summary>
        public double AloftKt(double ageSeconds) => FissionKt * (1.0 - (Local * Landed(ageSeconds)));

        /// <summary>Where the debris cloud's centre is, downwind and up, from the ground under the burst.</summary>
        public (double Downwind, double Up, double Radius) Cloud(double kt, double ageSeconds)
        {
            double rise = Math.Clamp(ageSeconds / MushroomCloud.RiseSeconds, 0.0, 1.0);
            double drift = Wind * Math.Max(0.0, ageSeconds - MushroomCloud.RiseSeconds);
            double radius = Math.Max(MushroomCloud.FireballRadius(kt), CapRadius * rise) + (CrosswindSpread * drift);

            return (drift, 0.8 * MushroomCloud.CloudTop(kt) * rise, radius);
        }
    }

    /// <summary>
    /// The rate from debris spread through a ball, at a distance from its centre (Sv/s): the inverse
    /// square outside, three times the rim's at the centre.
    /// </summary>
    public static double CloudRate(double aloftKt, double ageSeconds, double distance, double radius, double column)
    {
        if (!(aloftKt > 0.0) || !(radius > 0.0)) return 0.0;

        double onset = Ramp.Smooth(0.5 * PromptPhaseSeconds, PromptPhaseSeconds, ageSeconds);
        double spread = 4.0 * Math.PI * ((distance * distance) + (radius * radius / 3.0));

        return PointSvM2PerSecondPerKt * aloftKt * Decay(ageSeconds) * onset / spread * GammaThrough(column);
    }

    /// <summary>The peak of the shell a burst above the air fills, for KSA's belt (mSv/h).</summary>
    public static double BeltPeakMilliSvPerHour(double fissionKt, double l)
        => fissionKt > 0.0 && l > 1.0 ? BeltMilliSvPerHourPerFissionKt * fissionKt * Math.Pow(BeltReferenceL / l, 4.0)
                                      : 0.0;

    public static double BeltWidth(double l) => 0.15 * l;

    /// <summary>How long that shell holds its electrons: a year deep in the field, days further out (s).</summary>
    public static double BeltLifeSeconds(double l)
        => Math.Max(BeltLongLifeSeconds * Math.Exp(-Math.Max(0.0, l - BeltLongLifeL) / 0.15), BeltShortestLifeSeconds);

    /// <summary>
    /// The air between two points, both measured from the body's centre (kg/m²). Sampled, because a slant
    /// path through an exponential atmosphere has no closed form.
    /// </summary>
    public static double Column(BodyAir air, double3 a, double3 b, int samples = 16)
    {
        if (!air.HasAir) return 0.0;

        double length = Vec.Len(b - a);
        if (!(length > 0.0)) return 0.0;

        double sum = 0.0;
        for (int i = 0; i < samples; i++)
        {
            double3 p = a + ((b - a) * ((i + 0.5) / samples));
            sum += air.DensityAt(Vec.Len(p) - air.RadiusMetres);
        }

        return sum * length / samples;
    }
}
