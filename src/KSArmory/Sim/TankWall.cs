namespace KSArmory;

/// <summary>
/// A tank's wall as KSA declares it — thickness, the material's yield strength and the pressure KSA rates
/// it for — and what gets through it or bursts it. The default is no wall known, which stops nothing.
/// </summary>
internal readonly record struct TankWall(double ThicknessMetres, double YieldPascals, double RatedPascals)
{
    /// <summary>
    /// Plane-stress fracture toughness over yield, squared over pi, since a tank's wall is thin sheet:
    /// 58.0 MPa√m over 524 MPa for 1.5 mm 2014-T6 at 77 K, transverse, the weaker way (Orange 1967,
    /// NASA TN D-4017, tables I and II). Every Core tank is 2014; KSA declares no toughness, so every
    /// material is taken as it.
    /// </summary>
    public const double CrackLengthMetres = 0.0039;

    /// <summary>A fragment taken as a steel sphere, for the size of the hole it would punch.</summary>
    public const double FragmentDensityKgPerM3 = 7850.0;

    public bool Known => ThicknessMetres > 0.0 && YieldPascals > 0.0;

    /// <summary>Work to shear a plug of this diameter out of the wall, at a shear strength of yield over √3.</summary>
    public double PlugJoules(double diameterMetres) => PlugJoules(diameterMetres, ThicknessMetres);

    /// <summary>The same through only <paramref name="thicknessMetres"/> of it, as is left under a crater.</summary>
    public double PlugJoules(double diameterMetres, double thicknessMetres) =>
        0.5 * Math.PI * diameterMetres * thicknessMetres * thicknessMetres * YieldPascals / Math.Sqrt(3.0);

    /// <summary>Whether a hit carrying this much energy through this diameter gets through; always, with no wall known.</summary>
    public bool Perforates(double joules, double diameterMetres) => Perforates(joules, diameterMetres, ThicknessMetres);

    /// <summary>The same against only <paramref name="thicknessMetres"/> of wall.</summary>
    public bool Perforates(double joules, double diameterMetres, double thicknessMetres) =>
        !Known || !(diameterMetres > 0.0) || joules > PlugJoules(diameterMetres, thicknessMetres);

    /// <summary>
    /// How deep a hit that does not get through goes: the thickness whose plug its energy would just
    /// shear, since the plug's work grows as the square of the thickness.
    /// </summary>
    public double DentMetres(double joules, double diameterMetres)
    {
        if (!Known || !(diameterMetres > 0.0) || !(joules > 0.0)) return 0.0;
        return Math.Sqrt(joules / PlugJoules(diameterMetres, 1.0));
    }

    /// <summary>
    /// The pressure difference the wall holds with a hole of this radius through it: the hoop stress
    /// against a through-crack's fracture stress. Twice the rating with no hole, since KSA rates at half
    /// the yield; zero where KSA gives no rating.
    /// </summary>
    public double HoldsPascals(double holeRadiusMetres)
    {
        if (!(RatedPascals > 0.0)) return 0.0;
        if (!(holeRadiusMetres > CrackLengthMetres)) return 2.0 * RatedPascals;

        return 2.0 * RatedPascals * Math.Sqrt(CrackLengthMetres / holeRadiusMetres);
    }

    /// <summary>Whether a holed tank at this pressure, against this outside, bursts rather than vents.</summary>
    public bool Bursts(double insidePascals, double outsidePascals, double holeRadiusMetres) =>
        RatedPascals > 0.0 && insidePascals - outsidePascals > HoldsPascals(holeRadiusMetres);

    /// <summary>
    /// The wall as <see cref="Config.DamageScale"/> has it: a tenth as strong at ten times the damage, so
    /// what gets through it and what it holds follow the slider that sets what a hit costs.
    /// </summary>
    public TankWall Scaled(double damageScale) => damageScale > 0.0
        ? this with { YieldPascals = YieldPascals / damageScale, RatedPascals = RatedPascals / damageScale }
        : this;

    public static double FragmentDiameterMetres(double massKg) =>
        massKg > 0.0 ? Math.Cbrt(6.0 * massKg / (Math.PI * FragmentDensityKgPerM3)) : 0.0;
}
