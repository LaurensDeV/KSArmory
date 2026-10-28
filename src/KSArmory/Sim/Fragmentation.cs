using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A warhead's fragments as they leave it: how many, how heavy, how fast, and the band they fly in.
/// </summary>
/// <param name="Count">Fragments thrown, zero for a warhead that throws none.</param>
/// <param name="MassKg">One fragment's mass.</param>
/// <param name="SpeedMetresPerSecond">How fast each leaves the warhead, in the warhead's own frame.</param>
/// <param name="PolarDeg">The middle of the band, from the warhead's nose: 90 is straight out of its side.</param>
/// <param name="HalfWidthDeg">Half the band's width either side of <paramref name="PolarDeg"/>.</param>
internal readonly record struct FragmentSpray(int Count, double MassKg, double SpeedMetresPerSecond,
                                              double PolarDeg, double HalfWidthDeg)
{
    public bool Throws => Count > 0 && MassKg > 0.0 && SpeedMetresPerSecond > 0.0;

    public static FragmentSpray Of(MunitionProfile munition)
    {
        ArgumentNullException.ThrowIfNull(munition);

        return new FragmentSpray(munition.FragmentCount, munition.FragmentMassGrams / 1000.0,
                                 munition.FragmentSpeed, munition.SprayAngleDeg, munition.SprayHalfWidthDeg);
    }
}

/// <summary>What one burst's fragments do to one part: how many are expected to hit it, and how hard.</summary>
internal readonly record struct FragmentLoad(double ExpectedHits, double ImpactSpeed)
{
    public static FragmentLoad None => new(0.0, 0.0);

    /// <summary>The energy one fragment of <paramref name="massKg"/> arrives with.</summary>
    public double JoulesPerHit(double massKg) => 0.5 * massKg * ImpactSpeed * ImpactSpeed;
}

/// <summary>
/// Where a warhead's fragments go, and how many reach a part.
///
/// <para><b>The band is thrown in the warhead's frame and arrives in the target's</b>, so the warhead's
/// own velocity relative to the target tilts it forward and thins it: a part the static band would
/// miss can be in the moving one, and the reverse. A part outside the band takes nothing however
/// close it is, which is most of why a missile's single-shot kill probability is not one.</para>
///
/// <para>Density falls as the square of the distance, and the count on a part is Poisson about the
/// expected number: <c>docs/DAMAGE-PLAN.md</c> has the derivation.</para>
/// </summary>
internal static class Fragmentation
{
    // A steel fragment's drag, a tumbling chunk's.
    private const double FragmentDragCoefficient = 1.2;

    private const double SteelKgPerM3 = 7850.0;

    /// <summary>
    /// How many of a spray's fragments are expected to hit a part, and how fast they arrive.
    ///
    /// <para><paramref name="relativeVelocity"/> is the warhead's velocity less the part's, which
    /// a caller differences from both; <paramref name="noseAxis"/> is the warhead's own nose, or
    /// zero for one with no orientation, whose fragments go every way alike.</para>
    /// </summary>
    public static FragmentLoad OnPart(FragmentSpray spray, double3 burst, double3 noseAxis, double3 relativeVelocity,
                                      double3 partCentre, double presentedAreaM2, double airKgPerM3)
    {
        if (!spray.Throws || !(presentedAreaM2 > 0.0)) return FragmentLoad.None;

        double3 offset = partCentre - burst;
        double range = Vec.Len(offset);
        if (!(range > 1.0e-6) || !double.IsFinite(range)) return FragmentLoad.None;

        double3 toPart = offset / range;
        double v = spray.SpeedMetresPerSecond;
        double3 w = Vec.IsFinite(relativeVelocity) ? relativeVelocity : Vec.Zero;

        // The static speed along the line to the part: |s·d - w| = v.
        double along = Vec.Dot(toPart, w);
        double disc = (along * along) - Vec.Dot(w, w) + (v * v);
        if (!(disc >= 0.0)) return FragmentLoad.None;

        double s = along + Math.Sqrt(disc);
        if (!(s > 0.0)) return FragmentLoad.None;

        double3 staticDirection = ((toPart * s) - w) / v;

        double solidAngle;
        double axisLength = Vec.Len(noseAxis);
        if (axisLength > 0.0 && double.IsFinite(axisLength))
        {
            double polar = Math.Acos(Math.Clamp(Vec.Dot(staticDirection, noseAxis / axisLength), -1.0, 1.0));
            double low = DegToRad(Math.Max(spray.PolarDeg - spray.HalfWidthDeg, 0.0));
            double high = DegToRad(Math.Min(spray.PolarDeg + spray.HalfWidthDeg, 180.0));
            if (polar < low || polar > high) return FragmentLoad.None;

            solidAngle = 2.0 * Math.PI * (Math.Cos(low) - Math.Cos(high));
        }
        else
        {
            solidAngle = 4.0 * Math.PI;
        }

        if (!(solidAngle > 0.0)) return FragmentLoad.None;

        // The static sphere of velocities seen from the target: dΩ_static/dΩ_target = s² / (v · (s - d·w)).
        double cosine = (s - along) / v;
        if (!(cosine > 1.0e-6)) return FragmentLoad.None;
        double jacobian = s * s / (v * v * cosine);

        double expected = spray.Count / solidAngle * jacobian * presentedAreaM2 / (range * range);

        // No part can take more than half the band, however close it stands.
        expected = Math.Min(expected, spray.Count * 0.5);

        return new FragmentLoad(expected, ImpactSpeed(spray.MassKg, s, range, airKgPerM3));
    }

    /// <summary>
    /// Where a proximity fuse matched to its warhead fires, against where the round passed nearest its
    /// target: earlier along the pass, by as much as puts the target on the middle of the moving band.
    /// Zero where the band points back, since nothing fires after the nearest point.
    ///
    /// <para>A real fuze looks forward and delays so its fragments arrive where the target is; one
    /// firing at the nearest point throws the band ahead of a target it passes fast. Taken along the
    /// nose, so a pass at an angle to it is approximate.</para>
    /// </summary>
    public static double3 FuzeLead(FragmentSpray spray, double3 warheadVelocity, double3 targetVelocity,
                                   double missMetres)
    {
        double3 w = warheadVelocity - targetVelocity;
        double closing = Vec.Len(w);
        if (!spray.Throws || !(closing > 0.0) || !double.IsFinite(closing) || !(missMetres > 0.0)) return Vec.Zero;

        double polar = DegToRad(spray.PolarDeg);
        double v = spray.SpeedMetresPerSecond;
        double forward = (v * Math.Cos(polar)) + closing;
        if (!(forward > 0.0)) return Vec.Zero;

        double tan = v * Math.Sin(polar) / forward;
        if (!(tan > 0.0)) return Vec.Zero;

        return w / closing * -(missMetres / tan);
    }

    /// <summary>
    /// A fragment's speed after <paramref name="rangeMetres"/> of air: <c>v·e^(-r/L)</c> with
    /// <c>L = 2m / (ρ·Cd·A)</c> for a steel cube. Unchanged where there is no air.
    /// </summary>
    public static double ImpactSpeed(double massKg, double speed, double rangeMetres, double airKgPerM3)
    {
        if (!(airKgPerM3 > 0.0) || !(massKg > 0.0)) return speed;

        double side = Math.Cbrt(massKg / SteelKgPerM3);
        double area = 1.5 * side * side;
        double decayLength = 2.0 * massKg / (airKgPerM3 * FragmentDragCoefficient * area);

        return speed * Math.Exp(-rangeMetres / decayLength);
    }

    /// <summary>
    /// A Poisson draw about <paramref name="expected"/>, by inversion below 30 and a rounded normal
    /// above, where the two agree to well under a hit.
    /// </summary>
    public static int Hits(double expected, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!(expected > 0.0) || !double.IsFinite(expected)) return 0;

        if (expected >= 30.0)
        {
            double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
            double normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return Math.Max(0, (int)Math.Round(expected + (normal * Math.Sqrt(expected))));
        }

        double limit = Math.Exp(-expected), product = random.NextDouble();
        int count = 0;
        while (product > limit)
        {
            count++;
            product *= random.NextDouble();
        }

        return count;
    }

    private static double DegToRad(double degrees) => degrees * Math.PI / 180.0;
}
