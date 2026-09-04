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
    /// rather than read from there because nothing under <c>Sim/</c> may reference KSA.</para>
    ///
    /// <para>It is worth knowing where the extremes land. The engine clamps a tolerance to
    /// 0.1–20 MPa, so the flimsiest part reaches 3.11x the lethal radius and the densest 0.53x —
    /// and the weak end is capped at the blast radius anyway, which for the 57E6 is 3x. So the
    /// outer radius stays the honest limit of the weapon and nothing outside it is touched.</para>
    /// </summary>
    public const double ReferencePascals = 3.0e6;

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
