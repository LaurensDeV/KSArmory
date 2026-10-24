using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// What a metre of aim movement costs the trim, and so how far a budget can move the aim. The trim
/// floor (<c>Ksa/Icbm/IcbmComputer.Trim.cs</c>) is its stop band over this rate.
///
/// <para>A property of the trajectory, not the guidance: two transfers to two aim points, differenced.
/// 2.48 m/s per km at 3,459 km down to 0.53 at 12,902 (<c>docs/MIRV-NEXT.md</c>, "What an aim move
/// costs").</para>
/// </summary>
internal static class AimAuthority
{
    /// <summary>
    /// How far the aim is displaced to price it: well above the solves' rounding, and inside the linear
    /// region, measured linear to 0.07% over twenty kilometres.
    /// </summary>
    public const double ProbeMetres = 1_000.0;

    /// <summary>
    /// The most the aim may be moved from where the shot is currently going, in metres, for
    /// <paramref name="budgetMetresPerSecond"/> of trim.
    ///
    /// <para>False when the rate cannot be priced or the budget is nothing. <b>A caller must then leave
    /// the bound alone rather than read it as zero</b>, which would switch the correction off.</para>
    /// </summary>
    public static bool TryMetresFor(BallisticBody body, double3 fromCci, double3 aimNowCci,
                                    double flightSeconds, double budgetMetresPerSecond,
                                    out double metres, bool longWay = false)
    {
        metres = 0.0;

        if (!(budgetMetresPerSecond > 0.0)) return false;
        if (!TryRate(body, fromCci, aimNowCci, flightSeconds, out double perMetre, longWay)) return false;

        metres = budgetMetresPerSecond / perMetre;
        return double.IsFinite(metres) && metres > 0.0;
    }

    /// <summary>
    /// What one metre of aim movement costs the trim, in metres a second.
    ///
    /// <para>Priced downrange, the way a drag or arrival error displaces an impact; the cross-range rate
    /// differs.</para>
    /// </summary>
    public static bool TryRate(BallisticBody body, double3 fromCci, double3 aimNowCci,
                               double flightSeconds, out double metresPerSecondPerMetre,
                               bool longWay = false)
    {
        metresPerSecondPerMetre = 0.0;

        if (!body.IsUsable) return false;
        if (!Vec.IsFinite(fromCci) || !Vec.IsFinite(aimNowCci)) return false;

        double radius = Vec.Len(aimNowCci);
        if (!(radius > 0.0)) return false;

        double3 up = aimNowCci / radius;
        double3 across = Vec.Cross(fromCci, aimNowCci);

        if (Vec.Len(across) <= 0.0) return false;

        double3 downrange = Vec.Unit(Vec.Cross(across, up));

        if (!Vec.IsFinite(downrange) || downrange.Equals(Vec.Zero)) return false;

        if (!BallisticArc.TrySolve(body, fromCci, aimNowCci, flightSeconds,
                                   out BallisticArc.Solution here, longWay))
        {
            return false;
        }

        // Kept on the aim's sphere: along the tangent alone the sagitta makes the rate depend on the probe.
        double3 moved = Vec.Unit(aimNowCci + downrange * ProbeMetres) * radius;

        if (!BallisticArc.TrySolve(body, fromCci, moved, flightSeconds,
                                   out BallisticArc.Solution there, longWay))
        {
            return false;
        }

        double cost = Vec.Len(there.RequiredVelocityCci - here.RequiredVelocityCci);

        if (!double.IsFinite(cost) || cost <= 0.0) return false;

        metresPerSecondPerMetre = cost / ProbeMetres;
        return true;
    }
}
