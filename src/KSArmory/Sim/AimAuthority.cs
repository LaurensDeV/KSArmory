using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// How far the aim may be moved for what the bus can afford to fly it with.
///
/// <para><b>An aim the trim cannot reach is worse than a nearer one it can.</b> Flown over 64
/// corrections, a trim that ran to completion landed at 140 m and every other ending landed at 5 to
/// 45 km — so releasing with the correction outstanding is not a slightly worse shot, it is a
/// different order of shot. The loop that chooses the aim has no idea what one costs:
/// <see cref="AimCorrection.MaxMetres"/> is 300 km flat, against a budget that buys 24 km at
/// 3,459 and 113 km at 12,902. It is licensed to walk somewhere the actuator can never follow, and
/// the flown symptom is a demand that exceeds whatever is left of the ceiling on every pass until
/// the budget is gone.</para>
///
/// <para>The exchange rate is a property of the trajectory rather than of the guidance, so there is
/// nothing to tune: two transfers to two aim points, differenced. It runs from 2.48 m/s per
/// kilometre at 3,459 km down to 0.53 at 12,902 — the same trajectory sensitivity that makes a long
/// shot hard to hit is what makes its aim cheap to move.</para>
/// </summary>
internal static class AimAuthority
{
    /// <summary>
    /// How far the aim is displaced to price it.
    ///
    /// <para>Large enough that two Lambert solutions differ by far more than they round by, small
    /// enough to stay inside the linear region — measured linear to three figures over twenty
    /// kilometres, which is further than the correction walks in one pass.</para>
    /// </summary>
    public const double ProbeMetres = 1_000.0;

    /// <summary>
    /// The most the aim may be moved from where the shot is currently going, in metres, for
    /// <paramref name="budgetMetresPerSecond"/> of trim.
    ///
    /// <para>Answers false when the rate cannot be priced — an unusable body, a transfer that will
    /// not solve, a budget of nothing. <b>The caller must then leave the bound alone rather than
    /// treating it as zero</b>: an aim clamped to nothing is a correction switched off, which is
    /// the one outcome nobody asked for.</para>
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
    /// <para>Priced across the aim point's own <em>downrange</em> direction, which is the way a
    /// correction for a shot falling short or long actually moves. The cross-range rate differs and
    /// is not the one that binds: the miss this loop chases is overwhelmingly along the track,
    /// because that is the direction a drag or arrival error displaces an impact in.</para>
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

        // Downrange from the departure point, along the surface. Built from the plane the two
        // points share rather than from a stored heading, so it is right for a shot in any
        // direction and needs nothing carried in.
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

        // Kept on the sphere the aim already sits on. Displacing along the tangent alone lifts the
        // point by the sagitta, which at a kilometre is under a tenth of a millimetre and would not
        // matter -- except that it makes the rate depend on the probe, which is the one thing a
        // measured constant must not do.
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
