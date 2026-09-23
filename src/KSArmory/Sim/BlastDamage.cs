using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// One part of a craft, as the only three things a blast needs to know about it.
/// </summary>
/// <param name="Index">
/// The caller's own handle on the part. Opaque here, so the sweep never holds an engine object.
/// </param>
/// <param name="PositionEcl">
/// Where the part is, sampled at the frame start like every other body in the sweep — carried
/// forward to the burst's instant by the craft's own velocity, which is what
/// <see cref="BlastSweep.SurfaceGap"/> exists to do.
/// </param>
/// <param name="RadiusMetres">
/// The part's own half-diagonal, so a burst against the skin of a long tank is measured from the
/// skin rather than from the point halfway down it.
/// </param>
/// <param name="CrashTolerancePascals">
/// What the engine says it takes to break this part. Derived from its mass and volume unless the
/// part template overrides it, so a profile never has to say anything about damage.
/// </param>
internal readonly record struct DamageablePart(
    int Index, double3 PositionEcl, double RadiusMetres, double CrashTolerancePascals);

/// <summary>
/// Which parts of a craft a burst breaks.
///
/// <para><b>Nothing here picks a part.</b> Every part is judged on its own distance and its own
/// strength, so a burst against a rocket's tail takes the engines and leaves the payload, and a
/// fragile radome goes at a range that would not scratch a tank. That is the whole answer to
/// "which part does a blast choose", and it needs no gameplay number of its own: the strength is
/// the engine's, derived from the part's mass and volume.</para>
///
/// <para><b>The reach is one law re-anchored, not a second damage model.</b> Cube-root scaling
/// says a given overpressure is felt at a fixed <em>scaled</em> distance, and near the burst
/// pressure falls as the cube of it — so the radius at which a part's own tolerance is reached
/// goes as <c>(W / P)^(1/3)</c>. That is <see cref="Warhead.LethalRadius"/> with a second cube
/// root on the strength ratio, which is why a warhead twice the size and a part half the strength
/// buy exactly the same 1.26x of reach.</para>
/// </summary>
internal static class BlastDamage
{
    /// <summary>
    /// The part strength <see cref="Warhead.LethalScaledDistance"/> is calibrated against, in
    /// pascals.
    ///
    /// <para>A part this strong fails at exactly the lethal radius, so the mod's flown 57E6
    /// numbers still mean what they meant: the calibration is unchanged and this only says which
    /// part it was calibrated <em>on</em>. The value is KSA's own <c>BaseStrength</c> — the
    /// tolerance the engine derives for a part at its reference density — and it is written here
    /// rather than read from there because nothing under <c>Sim/</c> may reference KSA. <b>It has
    /// to move when KSA's does</b>, or every warhead's reach against every part moves by the cube
    /// root of the change.</para>
    ///
    /// <para>It is worth knowing where the extremes land. The engine clamps a tolerance to
    /// 0.1–100 MPa, so the flimsiest part reaches 4.48x the lethal radius and the densest 0.45x —
    /// and the weak end is capped at the blast radius anyway, which for the 57E6 is 3x. So the
    /// outer radius stays the honest limit of the weapon and nothing outside it is touched.</para>
    /// </summary>
    public const double ReferencePascals = 9.0e6;

    /// <summary>
    /// How near this warhead has to go off to break a part of that strength.
    ///
    /// <para>Bounded above by <see cref="Warhead.BlastRadius"/>, which is the radius the weapon
    /// is described by everywhere else — the panel, the overlay and the near-miss line. A damage
    /// rule that reached past it would make all three lie.</para>
    /// </summary>
    public static double FailureRadius(double chargeKg, double crashTolerancePascals)
    {
        double lethal = Warhead.LethalRadius(chargeKg);
        if (lethal <= 0.0) return 0.0;

        // A tolerance the engine could not answer for is treated as the reference part rather
        // than as either invulnerable or made of paper.
        double tolerance = double.IsFinite(crashTolerancePascals) && crashTolerancePascals > 0.0
            ? crashTolerancePascals
            : ReferencePascals;

        double reach = lethal * Math.Cbrt(ReferencePascals / tolerance);

        return Math.Min(reach, Warhead.BlastRadius(chargeKg));
    }

    /// <summary>
    /// The overpressure at a part over what it can take, from the law <see cref="FailureRadius"/>
    /// is: exactly one where the part fails, falling as the cube of the distance past it. Nothing
    /// past <see cref="Warhead.BlastRadius"/>, which is where the weapon is described as reaching.
    /// </summary>
    public static double PressureRatio(double chargeKg, double crashTolerancePascals, double gap)
    {
        double lethal = Warhead.LethalRadius(chargeKg);
        if (lethal <= 0.0 || !(gap < Warhead.BlastRadius(chargeKg))) return 0.0;

        double tolerance = double.IsFinite(crashTolerancePascals) && crashTolerancePascals > 0.0
            ? crashTolerancePascals
            : ReferencePascals;

        double scaled = lethal / Math.Max(gap, 1.0e-3);
        return ReferencePascals / tolerance * scaled * scaled * scaled;
    }

    // Skin yields at about a fifth of the overpressure that tears it: a parked aircraft is lightly
    // damaged by about one psi and destroyed by about five (Glasstone, ch. 5).
    public const double YieldShare = 0.2;

    // The engine dents a part from half its tolerance, so half is where the load reaches YieldShare.
    private const double EngineDentShare = 0.5;

    /// <summary>
    /// The load to hand the engine as a dent: <see cref="PressureRatio"/> inside the radius where
    /// the part fails, and outside it the real blast wave's fall-off, bent so the engine's
    /// threshold lands where the real overpressure is <see cref="YieldShare"/> of what broke the
    /// part: 2.6x the failure radius for a strong part and 3.4x for one of the reference strength,
    /// which takes it out to the blast radius, where the cube law alone reached that threshold 1.26x
    /// out.
    /// </summary>
    public static double DentRatio(double chargeKg, double crashTolerancePascals, double gap)
    {
        double failure = FailureRadius(chargeKg, crashTolerancePascals);
        if (!(failure > 0.0) || !(gap < Warhead.BlastRadius(chargeKg))) return 0.0;
        if (gap <= failure) return PressureRatio(chargeKg, crashTolerancePascals, gap);

        double atFailure = BlastWave.PeakOverpressurePascals(chargeKg, failure);
        if (!(atFailure > 0.0)) return 0.0;

        double share = BlastWave.PeakOverpressurePascals(chargeKg, gap) / atFailure;
        return Math.Pow(share, Math.Log(EngineDentShare) / Math.Log(YieldShare));
    }

    /// <summary>
    /// Every part of one craft this burst loads without breaking, with how hard and how far from
    /// the burst its skin is — what it would dent, and when the front gets there. A part in
    /// <paramref name="failed"/> is breaking off, and is left out.
    ///
    /// <para><b>Whether a load dents is the engine's to say</b>, as it is for a collision: this
    /// hands over every load inside the blast radius, and the engine's own threshold and depth
    /// law decide what, if anything, it leaves.</para>
    /// </summary>
    public static void Loads(double3 burstEcl, double sinceSample, double3 velocityEcl,
                             ReadOnlySpan<DamageablePart> parts, MunitionProfile munition,
                             IReadOnlyCollection<int>? failed,
                             List<(int Index, double PressureRatio, double GapMetres)> into)
    {
        ArgumentNullException.ThrowIfNull(munition);
        ArgumentNullException.ThrowIfNull(into);

        for (int i = 0; i < parts.Length; i++)
        {
            DamageablePart part = parts[i];
            if (failed is not null && failed.Contains(part.Index)) continue;

            double gap = BlastSweep.SurfaceGap(part.PositionEcl, velocityEcl, sinceSample,
                                               burstEcl, part.RadiusMetres);
            double ratio = DentRatio(munition.ChargeKg, part.CrashTolerancePascals, gap);

            if (ratio > 0.0) into.Add((part.Index, ratio, gap));
        }
    }

    /// <summary>
    /// Every part of one craft this burst breaks, appended to <paramref name="failed"/> as the
    /// indices they were handed over with.
    ///
    /// <para><paramref name="sinceSample"/> and <paramref name="velocityEcl"/> pair the parts with
    /// the burst the same way the craft sweep pairs a whole vehicle: the positions were taken
    /// before the round finished its step. Per part rather than per craft would be more exact by
    /// the craft's rotation over one step, which is centimetres, and there is no per-part velocity
    /// to read anyway.</para>
    /// </summary>
    public static void Sweep(double3 burstEcl, double sinceSample, double3 velocityEcl,
                             ReadOnlySpan<DamageablePart> parts, MunitionProfile munition,
                             List<int> failed)
    {
        ArgumentNullException.ThrowIfNull(munition);
        ArgumentNullException.ThrowIfNull(failed);

        for (int i = 0; i < parts.Length; i++)
        {
            DamageablePart part = parts[i];

            double gap = BlastSweep.SurfaceGap(part.PositionEcl, velocityEcl, sinceSample,
                                               burstEcl, part.RadiusMetres);

            if (gap <= FailureRadius(munition.ChargeKg, part.CrashTolerancePascals))
            {
                failed.Add(part.Index);
            }
        }
    }
}
