namespace KSArmory;

/// <summary>
/// An armour plate's layer — a material and a thickness — and what a round does against it: Lambert's
/// ballistic limit (BRL-MR-3960 p. 70), the Lambert–Jonas residual speed with Lambert's own estimates of its
/// constants, Rosenberg's glance, and how deep a round that stops goes. A plate is not a tank: nothing here
/// bursts or rams. The default is no layer, which stops nothing.
/// </summary>
internal readonly record struct ArmourLayer(ArmourMaterial? Material, double ThicknessMetres)
{
    /// <summary>A round that states no length is taken this many calibres long; a guess, not a measurement.</summary>
    public const double UnstatedLengthInCalibres = 4.0;

    /// <summary>
    /// The calibres of steel-equivalent armour an exploding shell's casing holes on bursting against it: the
    /// 20 mm M56 HEI's 12.7 mm of rolled armour at 104 m, as Wikipedia's M61 Vulcan page gives it.
    /// </summary>
    public const double ShellHolesCalibres = 0.635;

    private const double RolledArmourCoefficient = 4000.0;

    // Lambert's p grows with z and runs away for thick plates, which the source warns of; held inside its fit.
    private const double MostExponent = 8.0;

    public bool Known => Material is { LambertCoefficient: > 0f, DensityKgPerM3: > 0f } && ThicknessMetres > 0.0;

    /// <summary>
    /// The speed a round must beat to get through <paramref name="thicknessMetres"/> of this layer at an
    /// obliquity whose cosine is <paramref name="cosObliquity"/>; a wall a damage scale times as strong takes
    /// its root times the speed.
    /// </summary>
    public double BallisticLimit(double massKg, double diameterMetres, double lengthOverDiameter, double cosObliquity,
                                 double thicknessMetres, double damageScale = 1.0)
    {
        if (!Known || !(massKg > 0.0) || !(diameterMetres > 0.0) || !(thicknessMetres > 0.0)) return 0.0;

        double d = diameterMetres * 100.0, m = massKg * 1000.0;
        double z = Z(thicknessMetres, diameterMetres, cosObliquity);
        double a = Material!.LambertCoefficient / (damageScale > 0.0 ? Math.Sqrt(damageScale) : 1.0);

        return a * Math.Pow(Math.Max(lengthOverDiameter, 0.1), 0.15) * Math.Sqrt(F(z) * d * d * d / m);
    }

    /// <summary>
    /// What a round faster than the ballistic limit leaves with: <c>A(v^p − v_L^p)^(1/p)</c>, with
    /// <c>A = M/(M + M′/3)</c> for the plug's mass <c>M′</c> and <c>p = 2 + z/3</c> (BRL-MR-3960 eq. 30–31).
    /// Nothing for one that does not get through.
    /// </summary>
    public double ResidualSpeed(double speed, double massKg, double diameterMetres, double lengthOverDiameter,
                                double cosObliquity, double thicknessMetres, double damageScale = 1.0)
    {
        double limit = BallisticLimit(massKg, diameterMetres, lengthOverDiameter, cosObliquity, thicknessMetres, damageScale);
        if (!(speed > limit)) return 0.0;
        if (!Known) return speed;

        double d = diameterMetres * 100.0, m = massKg * 1000.0;
        double z = Z(thicknessMetres, diameterMetres, cosObliquity);
        double plug = Material!.DensityKgPerM3 / 1000.0 * Math.PI * d * d * d * z / 4.0;
        double carried = m / (m + (plug / 3.0));
        double p = Math.Min(2.0 + (z / 3.0), MostExponent);

        return carried * Math.Pow(Math.Pow(speed, p) - Math.Pow(limit, p), 1.0 / p);
    }

    /// <summary>
    /// Whether a round that does not get through glances off: Rosenberg's
    /// <c>tan²θ > (ρ_p v²/H)·(v + U)/(v − U)</c>, with the round's penetration speed <c>U</c> taken
    /// hydrodynamically, <c>v/(1 + √(ρ_t/ρ_p))</c>.
    /// </summary>
    public bool Glances(double speed, double cosObliquity, double roundKgPerM3)
    {
        if (!Known || !(Material!.ResistancePascals > 0f) || !(roundKgPerM3 > 0.0) || !(speed > 0.0)) return false;
        if (!(cosObliquity > 0.0) || cosObliquity >= 1.0) return false;

        double u = speed / (1.0 + Math.Sqrt(Material.DensityKgPerM3 / roundKgPerM3));
        double critical = roundKgPerM3 * speed * speed / Material.ResistancePascals * (speed + u) / (speed - u);
        double tan = Math.Sqrt((1.0 / (cosObliquity * cosObliquity)) - 1.0);

        return tan * tan > critical;
    }

    /// <summary>
    /// How deep a round that does not get through goes, measured square to the plate: the thickness whose
    /// ballistic limit is its speed.
    /// </summary>
    public double DentMetres(double speed, double massKg, double diameterMetres, double lengthOverDiameter,
                             double cosObliquity, double damageScale = 1.0)
    {
        if (!Known || !(speed > 0.0)) return 0.0;

        double low = 0.0, high = ThicknessMetres;
        for (int i = 0; i < 50; i++)
        {
            double mid = 0.5 * (low + high);
            if (BallisticLimit(massKg, diameterMetres, lengthOverDiameter, cosObliquity, mid, damageScale) < speed) low = mid;
            else high = mid;
        }

        return low;
    }

    /// <summary>Whether an exploding shell bursting on this layer holes it, by its steel-equivalent thickness.</summary>
    public bool HoledByShellBurst(double calibreMetres) =>
        !Known || ThicknessMetres * Material!.LambertCoefficient / RolledArmourCoefficient <= ShellHolesCalibres * calibreMetres;

    /// <summary>
    /// A round's length in calibres: its body's where it has one drawn, since only then is
    /// <see cref="MunitionProfile.BodyLength"/> its own rather than the default's.
    /// </summary>
    public static double LengthOverDiameter(MunitionProfile munition)
    {
        double calibre = munition.CalibreMm / 1000.0;
        bool drawn = !string.IsNullOrEmpty(munition.BodyMarker) || !string.IsNullOrEmpty(munition.BodyModel);
        return drawn && calibre > 0.0 && munition.BodyLength > 0f ? munition.BodyLength / calibre : UnstatedLengthInCalibres;
    }

    public static double RoundDensity(double massKg, double diameterMetres, double lengthOverDiameter)
    {
        double volume = 0.25 * Math.PI * diameterMetres * diameterMetres * diameterMetres * Math.Max(lengthOverDiameter, 0.1);
        return volume > 0.0 ? massKg / volume : 0.0;
    }

    private static double Z(double thicknessMetres, double diameterMetres, double cosObliquity)
    {
        double cos = Math.Clamp(cosObliquity, 0.05, 1.0);
        return thicknessMetres / diameterMetres * Math.Pow(1.0 / cos, 0.75);
    }

    private static double F(double z) => z + Math.Exp(-z) - 1.0;
}
