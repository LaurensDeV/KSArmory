using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// What a round entering a tank's liquid does to the tank: the energy the liquid takes off it, whether
/// the liquid's push cracks the wall at the hole, and the pressure the cavity it opens raises. Fitted to
/// NASA TM X-52063 and argued in <c>docs/HYDRODYNAMIC-RAM.md</c>.
/// </summary>
internal static class HydroRam
{
    /// <summary>
    /// The speed above which a 5.56 mm steel sphere cracked a water-filled 0.794 mm 2014-T6 tank loaded to
    /// its yield: 4,800 ft/s, NASA TM X-52063 table I. Every other term scales from this one.
    /// </summary>
    public const double ReferenceSpeed = 1463.0;

    public const double ReferenceWallMetres = 0.000794;
    public const double ReferenceDiameterMetres = 0.00556;

    // Table I: the same sphere needed 5,800 ft/s through a wall twice as thick.
    public const double WallExponent = 0.27;

    // Larger rounds than the tests', by Bless's V²D/W; smaller, table I's 3.2 mm spheres, which cracked
    // nothing at 2,188 m/s where the 5.56 mm broke the wall at 1,006.
    public const double LargerDiameterExponent = 0.5;
    public const double SmallerDiameterExponent = 1.4;

    // Table I, 7075-T6: unloaded walls held to 6,100 ft/s where loaded ones cracked at 3,300.
    public const double UnloadedSpeedFactor = 1.85;

    // Table I: liquid nitrogen, 0.81 of water's density, needed 2% more speed.
    public const double LiquidDensityExponent = 0.1;

    /// <summary>The reference sphere's mass over its frontal area, kg/m²: a heavier round cracks a wall slower.</summary>
    public const double ReferenceSectionalDensity = 28.8;

    // Table I: tungsten spheres cracked 2014 at 0.60 and 0.74 of steel's speed; nothing tested denser.
    public const double SectionalDensityExponent = 0.75;
    public const double DensestSpeedFactor = 0.6;

    /// <summary>
    /// A flight tank's pressure, since KSA gives a liquid none: what loads the skin before the round arrives.
    /// The leak keeps its own 0.2 bar (<see cref="TankLeak.UllagePascals"/>).
    /// </summary>
    public const double OperatingPascals = 3.0e5;

    /// <summary>
    /// How wide a round's hole in the far wall is, in calibres: it has tumbled in the liquid and leaves
    /// broadside, its length across. Three is a guess at a shell's or a dart's length, not a measurement.
    /// </summary>
    public const double TumbledCalibres = 3.0;

    /// <summary>A round's drag in liquid: between a nose-on dart's 0.3 and a tumbling one's 1.</summary>
    public const double DragCoefficient = 0.6;

    /// <summary>The share of the energy left in the liquid that opens the cavity, rather than heating or stirring it.</summary>
    public const double CavityShare = 0.7;

    public const double AluminiumModulusPascals = 72.0e9;
    private const double Gamma = 1.4;

    /// <summary>The speed above which a round of this diameter and mass cracks this wall at its hole.</summary>
    /// <param name="loadedShare">The wall's hoop stress before the round, as a share of its yield.</param>
    public static double CrackingSpeed(double wallMetres, double diameterMetres, double massKg, double loadedShare,
                                       double liquidKgPerM3, double damageScale = 1.0)
    {
        if (!(wallMetres > 0.0) || !(diameterMetres > 0.0) || !(massKg > 0.0)) return double.PositiveInfinity;

        double size = diameterMetres >= ReferenceDiameterMetres
            ? Math.Pow(ReferenceDiameterMetres / diameterMetres, LargerDiameterExponent)
            : Math.Pow(ReferenceDiameterMetres / diameterMetres, SmallerDiameterExponent);
        double sectional = massKg / (0.25 * Math.PI * diameterMetres * diameterMetres);
        double heavy = Math.Clamp(Math.Pow(sectional / ReferenceSectionalDensity, -SectionalDensityExponent),
                                  DensestSpeedFactor, 1.0);
        double load = 1.0 + ((UnloadedSpeedFactor - 1.0) * (1.0 - Math.Clamp(loadedShare, 0.0, 1.0)));
        double liquid = Math.Pow(Math.Max(liquidKgPerM3, 1.0) / 1000.0, -LiquidDensityExponent);

        // The load scales as the speed squared, so a wall a scale times as strong needs its root times the speed.
        double strength = damageScale > 0.0 ? 1.0 / Math.Sqrt(damageScale) : 1.0;

        return ReferenceSpeed * Math.Pow(wallMetres / ReferenceWallMetres, WallExponent) * size * heavy * load
               * liquid * strength;
    }

    /// <summary>
    /// Whether the far wall cracks as a round leaves at <paramref name="leavingSpeed"/>: tumbled, so broadside,
    /// through a wall the cavity has loaded to <paramref name="loadedShare"/> of its yield. Nothing where the
    /// round has too little left to leave.
    /// </summary>
    public static bool ExitCracks(TankWall wall, double massKg, double diameterMetres, double leavingSpeed,
                                  double loadedShare, double liquidKgPerM3, double damageScale = 1.0)
    {
        double broadside = TumbledCalibres * diameterMetres;
        if (!wall.Known || !wall.Perforates(0.5 * massKg * leavingSpeed * leavingSpeed, broadside)) return false;

        return leavingSpeed > CrackingSpeed(wall.ThicknessMetres, broadside, massKg, loadedShare, liquidKgPerM3,
                                            damageScale);
    }

    /// <summary>The share of a round's energy a path this long through liquid takes off it.</summary>
    public static double DepositedShare(double massKg, double diameterMetres, double liquidKgPerM3, double pathMetres)
    {
        if (!(massKg > 0.0) || !(pathMetres > 0.0) || !(liquidKgPerM3 > 0.0)) return 0.0;

        double beta = liquidKgPerM3 * DragCoefficient * 0.25 * Math.PI * diameterMetres * diameterMetres / (2.0 * massKg);
        return 1.0 - Math.Exp(-2.0 * beta * pathMetres);
    }

    /// <summary>
    /// The pressure rise a cavity opened by <paramref name="cavityJoules"/> raises in a tank: its energy shared
    /// between squeezing the ullage gas and stretching the wall, so a full tank has only its wall to give.
    /// </summary>
    /// <param name="ullageAbsolutePascals">The gas over the liquid, absolute.</param>
    public static double CavityPascals(double cavityJoules, double tankM3, double ullageM3, double ullageAbsolutePascals,
                                       double radiusMetres, double wallMetres)
    {
        if (!(cavityJoules > 0.0) || !(tankM3 > 0.0) || !(radiusMetres > 0.0) || !(wallMetres > 0.0)) return 0.0;

        // The liquid is taken as stiff: the wall's give, 2R(1 − ν/2)/(tE), is the larger by far for a thin tank.
        double give = 2.0 * radiusMetres * (1.0 - (0.33 / 2.0)) / (wallMetres * AluminiumModulusPascals);
        double wall(double rise) => 0.5 * rise * rise * tankM3 * give;

        if (!(ullageM3 > 1.0e-6) || !(ullageAbsolutePascals > 0.0)) return Math.Sqrt(2.0 * cavityJoules / (tankM3 * give));

        double p0 = ullageAbsolutePascals, v0 = ullageM3;
        double gas(double ratio) => p0 * v0 / (Gamma - 1.0) * (Math.Pow(ratio, Gamma - 1.0) - 1.0);
        double rise(double ratio) => p0 * (Math.Pow(ratio, Gamma) - 1.0);

        double low = 1.0, high = 1.0e4;
        if (gas(high) + wall(rise(high)) < cavityJoules) return rise(high);
        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5 * (low + high);
            if (gas(mid) + wall(rise(mid)) < cavityJoules) low = mid;
            else high = mid;
        }

        return rise(0.5 * (low + high));
    }

    /// <summary>
    /// How much of a straight path through a tank runs under its liquid, marched in a frame where
    /// <paramref name="up"/> points away from the liquid and the surface stands at <paramref name="surface"/>
    /// along it. Floating, the liquid is everywhere at its share <paramref name="floatingFill"/>.
    /// </summary>
    public static double WetPath(Func<double3, bool> inTank, double3 entry, double3 direction, double3 up,
                                 double surface, bool floating, double floatingFill, double longestMetres,
                                 double stepMetres = 0.02)
    {
        if (!(longestMetres > 0.0) || !(stepMetres > 0.0)) return 0.0;

        double3 along = Vec.Unit(direction);
        if (Vec.Len2(along) < 0.5) return 0.0;

        double wet = 0.0;
        bool entered = false;
        for (double s = 0.5 * stepMetres; s < longestMetres; s += stepMetres)
        {
            double3 at = entry + (along * s);
            if (!inTank(at))
            {
                if (entered) break;
                continue;
            }

            entered = true;
            if (floating) wet += stepMetres * Math.Clamp(floatingFill, 0.0, 1.0);
            else if (Vec.Dot(at, up) <= surface) wet += stepMetres;
        }

        return wet;
    }
}
