using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// One part of a craft, as the only three things a blast needs to know about it.
/// </summary>
/// <param name="Index">
/// The caller's own handle on the part. Opaque here, so the sweep never holds an engine object.
/// </param>
/// <param name="PositionEcl">
/// Where the part is, sampled at the step's end like every other body in the sweep — carried
/// back to the burst's instant by the craft's own velocity, which is what
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
/// <param name="Reflection">
/// What the ground under the burst adds to the blast at the part, as a multiple of the free-air
/// overpressure (<see cref="GroundReflection.GainAt"/>): one in free air, which is what the damage law
/// is calibrated in. The law goes as the cube of the distance, so it scales the charge.
/// </param>
/// <param name="MassKg">The part's own dry mass, which sets how much energy breaks it (<see cref="PartToughness"/>).</param>
/// <param name="PresentedAreaM2">
/// The area the part shows a burst, averaged over every direction: a quarter of its box's surface
/// (Cauchy), since a fragment's line to the part is not known to the part.
/// </param>
internal readonly record struct DamageablePart(
    int Index, double3 PositionEcl, double RadiusMetres, double CrashTolerancePascals, double Reflection = 1.0,
    double MassKg = 0.0, double PresentedAreaM2 = 0.0)
{
    /// <summary>The presented area, or a sphere's half-diagonal disc halved where the box was not given.</summary>
    public double AreaM2 => PresentedAreaM2 > 0.0 ? PresentedAreaM2 : 0.5 * Math.PI * RadiusMetres * RadiusMetres;
}

/// <summary>
/// One front's load on one part, as <see cref="BlastDamage.Combine"/> adds it to the others reaching
/// the same part: the dent it alone would be, its real overpressure and the real overpressure that
/// breaks the part, the way it pushes, how long ago it arrived and how long it pushes for.
/// </summary>
internal readonly record struct FrontLoad(double Ratio, double RealPascals, double BreakingPascals,
                                          double3 Direction, double SinceArrival, double PhaseSeconds);

/// <summary>
/// One warhead's burst as the parts around it see it: where it went off, how it was moving and which
/// way it pointed, the craft's own velocity, and the air between.
/// </summary>
/// <param name="SinceSample">How far before the step's end the burst was, negative; parts are carried back by it.</param>
/// <param name="NoseAxis">The warhead's nose, or zero for one with no attitude, whose fragments go every way.</param>
/// <param name="AirKgPerM3">The air at the burst, which slows fragments; zero where there is none.</param>
/// <param name="FuzeLeadEcl">Where the fragments leave from against the burst (<see cref="Fragmentation.FuzeLead"/>).</param>
internal readonly record struct BurstContext(double3 BurstEcl, double SinceSample, double3 PartVelocityEcl,
                                             double3 WarheadVelocityEcl, double3 NoseAxis, double AirKgPerM3,
                                             double3 FuzeLeadEcl = default);

/// <summary>
/// Which parts of a craft a burst breaks.
///
/// <para><b>Nothing here picks a part.</b> Every part is judged on its own distance and the strength
/// the engine derives from its mass and volume.</para>
///
/// <para><b>The reach is one law re-anchored</b>: the radius at which a part's tolerance is reached goes
/// as <c>(W / P)^(1/3)</c>, <see cref="Warhead.LethalRadius"/> with a second cube root on the strength
/// ratio.</para>
/// </summary>
internal static class BlastDamage
{
    /// <summary>
    /// The part strength <see cref="Warhead.LethalScaledDistance"/> is calibrated against, in
    /// pascals.
    ///
    /// <para>A part this strong fails at exactly the lethal radius, so the 57E6's flown calibration
    /// stands. KSA's own <c>BaseStrength</c>, copied because <c>Sim/</c> cannot reference KSA: <b>it has
    /// to move when KSA's does</b>, or every reach moves by the cube root of the change. A derived
    /// tolerance is 9 MPa × clamp(density/330, 0.1, 8), 0.9–72 MPa, so 2.15x to 0.5x of the lethal
    /// radius; a template's authored one is unclamped (Core's engines declare 3 MPa).</para>
    /// </summary>
    public const double ReferencePascals = 9.0e6;

    /// <summary>
    /// How near this warhead has to go off to break a part of that strength.
    ///
    /// <para>Bounded above by <see cref="Warhead.BlastRadius"/>, the radius the panel, the overlay and
    /// the near-miss line describe the weapon by.</para>
    /// </summary>
    public static double FailureRadius(double chargeKg, double crashTolerancePascals)
    {
        double lethal = Warhead.LethalRadius(chargeKg);
        if (lethal <= 0.0) return 0.0;

        // An unreadable tolerance is the reference part's.
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
    /// A load in real pascals beside the real overpressure that breaks the same part, both at sea
    /// level: what <see cref="Combine"/> adds up. The failure radius scales with the cube
    /// root of the charge, so the breaking overpressure is the part's, whatever the burst.
    /// </summary>
    public static (double RealPascals, double BreakingPascals) RealLoad(double chargeKg, double crashTolerancePascals,
                                                                        double gap)
    {
        return (BlastWave.PeakOverpressurePascals(chargeKg, gap),
                BlastWave.PeakOverpressurePascals(chargeKg, FailureRadius(chargeKg, crashTolerancePascals)));
    }

    /// <summary>
    /// How much of a part one burst uses up: its real overpressure over the real overpressure that
    /// breaks the part, so one or more exactly where <see cref="FailureRadius"/> says it breaks and a
    /// fraction beyond it, falling as the real wave does rather than as the cube. Nothing past
    /// <see cref="Warhead.BlastRadius"/>. The units <see cref="Combine"/> adds fronts in.
    /// </summary>
    public static double Share(double chargeKg, double crashTolerancePascals, double gap)
    {
        if (!(gap <= Warhead.BlastRadius(chargeKg))) return 0.0;
        if (gap <= FailureRadius(chargeKg, crashTolerancePascals)) return Math.Max(1.0, Real(chargeKg, crashTolerancePascals, gap));

        return Math.Min(Real(chargeKg, crashTolerancePascals, gap), 1.0);

        static double Real(double charge, double tolerance, double gap)
        {
            (double real, double breaking) = RealLoad(charge, tolerance, gap);
            return breaking > 0.0 && double.IsFinite(real) ? real / breaking : 0.0;
        }
    }

    /// <summary>
    /// What meeting adds to fronts reaching one part together, over each counted alone at its
    /// flash: the most head-on pair's reflection off a wall, against what breaks the part. Never
    /// negative, because what a front did at its arrival is not undone by the next one arriving.
    /// </summary>
    public static double MeetingShare(ReadOnlySpan<FrontLoad> loads)
    {
        double extra = Reflected(loads, out double breaking);
        return breaking > 0.0 ? extra / breaking : 0.0;
    }

    /// <summary>
    /// What several fronts reaching one part come to, at the instant the last of them arrives: each
    /// earlier one for what is left of it by then (<see cref="BlastWave.Remaining"/>), and the most
    /// head-on pair meeting as at a wall (<see cref="BlastWave.ReflectedPascals"/>) rather than
    /// simply adding. <c>Share</c> is the total against what breaks the part, so one or more breaks
    /// it; <c>Ratio</c> is the dent to hand the engine, bent as <see cref="DentRatio"/> bends one,
    /// and never less than any front's own at its arrival. A single front is its own load.
    ///
    /// <para>The reflection is counted once, for the pair it is largest for, and off the weaker of
    /// the two: fronts converging from several sides do not each meet every other at a wall.</para>
    /// </summary>
    public static (double Ratio, double Share) Combine(ReadOnlySpan<FrontLoad> loads)
    {
        if (loads.Length == 0) return (0.0, 0.0);

        double breaking = 0.0, strongest = 0.0, sum = 0.0;
        foreach (FrontLoad load in loads)
        {
            breaking = Math.Max(breaking, load.BreakingPascals);
            strongest = Math.Max(strongest, load.Ratio);
            sum += Math.Max(load.RealPascals, 0.0) * BlastWave.Remaining(load.SinceArrival, load.PhaseSeconds);
        }

        if (!(breaking > 0.0)) return (strongest, 0.0);
        if (loads.Length == 1) return (loads[0].Ratio, sum / breaking);

        double share = (sum + Reflected(loads, out _)) / breaking;
        double ratio = Math.Pow(share, Math.Log(EngineDentShare) / Math.Log(YieldShare));
        return (Math.Max(ratio, strongest), share);
    }

    // The largest head-on pair's reflection over the two simply adding, in pascals, beside the
    // real overpressure that breaks the part.
    private static double Reflected(ReadOnlySpan<FrontLoad> loads, out double breaking)
    {
        breaking = 0.0;
        foreach (FrontLoad load in loads) breaking = Math.Max(breaking, load.BreakingPascals);

        double extra = 0.0;
        for (int i = 0; i < loads.Length; i++)
        {
            for (int j = i + 1; j < loads.Length; j++)
            {
                double headOn = -Vec.Dot(Vec.Unit(loads[i].Direction), Vec.Unit(loads[j].Direction));
                if (!(headOn > 0.0)) continue;

                double weaker = Math.Min(
                    loads[i].RealPascals * BlastWave.Remaining(loads[i].SinceArrival, loads[i].PhaseSeconds),
                    loads[j].RealPascals * BlastWave.Remaining(loads[j].SinceArrival, loads[j].PhaseSeconds));
                if (!(weaker > 0.0)) continue;

                extra = Math.Max(extra, headOn * (BlastWave.ReflectedPascals(weaker) - (2.0 * weaker)));
            }
        }

        return extra;
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
            double ratio = DentRatio(munition.ChargeKg * part.Reflection, part.CrashTolerancePascals, gap);

            if (ratio > 0.0) into.Add((part.Index, ratio, gap));
        }
    }

    /// <summary>
    /// Every part of one craft this burst breaks, appended to <paramref name="failed"/> as the
    /// indices they were handed over with.
    ///
    /// <para><paramref name="sinceSample"/> and <paramref name="velocityEcl"/> carry the step-end positions
    /// back to the burst, as the craft sweep does; per-part velocity would add centimetres.</para>
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

            if (gap <= FailureRadius(munition.ChargeKg * part.Reflection, part.CrashTolerancePascals))
            {
                failed.Add(part.Index);
            }
        }
    }

    /// <summary>
    /// Every part of one craft a warhead costs something, with what it costs: the blast's
    /// <see cref="Share"/>, held under what the part's area intercepts of the charge, and the fragments
    /// that hit it, drawn from <paramref name="dice"/>.
    ///
    /// <para><b>The ceiling is what stops a burst breaking every part it is near</b>: overpressure says
    /// a fairing and a 20 t tank fail at the same distance, where the energy reaching each is set by its
    /// size and what breaks it by its mass. A nuclear charge is judged on the pressure alone, which is
    /// what its reach is calibrated on.</para>
    ///
    /// <para>Every part draws its own fragments, so parts shadowing each other are each hit as though
    /// the other were not there.</para>
    /// </summary>
    public static void Costs(in BurstContext burst, ReadOnlySpan<DamageablePart> parts, MunitionProfile munition,
                             Random dice, List<(int Index, double Share)> into, List<(int Index, int Hits)>? hits = null)
    {
        ArgumentNullException.ThrowIfNull(munition);
        ArgumentNullException.ThrowIfNull(dice);
        ArgumentNullException.ThrowIfNull(into);

        FragmentSpray spray = FragmentSpray.Of(munition);
        bool capped = munition.ChargeKg < MushroomCloud.ThresholdKg;

        for (int i = 0; i < parts.Length; i++)
        {
            DamageablePart part = parts[i];

            double3 centre = InFrame.AtBurst(part.PositionEcl, burst.PartVelocityEcl, burst.SinceSample);
            double gap = Vec.Len(centre - burst.BurstEcl) - part.RadiusMetres;
            double charge = munition.ChargeKg * part.Reflection;
            double joules = PartToughness.Joules(part.MassKg, part.CrashTolerancePascals);

            double cost = Share(charge, part.CrashTolerancePascals, gap);
            if (capped)
            {
                cost = Math.Min(cost, PartToughness.BlastShareCeiling(
                    charge, Vec.Len(centre - burst.BurstEcl), part.AreaM2, joules));
            }

            FragmentLoad load = Fragmentation.OnPart(spray, burst.BurstEcl + burst.FuzeLeadEcl, burst.NoseAxis,
                                                     burst.WarheadVelocityEcl - burst.PartVelocityEcl,
                                                     centre, part.AreaM2, burst.AirKgPerM3);
            int struck = Fragmentation.Hits(load.ExpectedHits, dice);
            cost += PartToughness.FragmentShare(struck, load.JoulesPerHit(spray.MassKg), joules);
            if (struck > 0) hits?.Add((part.Index, struck));

            if (cost > 0.0) into.Add((part.Index, cost));
        }
    }

    /// <summary>
    /// Every part of one craft this burst reaches, with its <see cref="Share"/>: what
    /// <see cref="Sweep"/> breaks is exactly what comes out at one or more.
    /// </summary>
    public static void Shares(double3 burstEcl, double sinceSample, double3 velocityEcl,
                              ReadOnlySpan<DamageablePart> parts, MunitionProfile munition,
                              List<(int Index, double Share)> into)
    {
        ArgumentNullException.ThrowIfNull(munition);
        ArgumentNullException.ThrowIfNull(into);

        for (int i = 0; i < parts.Length; i++)
        {
            DamageablePart part = parts[i];

            double gap = BlastSweep.SurfaceGap(part.PositionEcl, velocityEcl, sinceSample,
                                               burstEcl, part.RadiusMetres);
            double share = Share(munition.ChargeKg * part.Reflection, part.CrashTolerancePascals, gap);

            if (share > 0.0) into.Add((part.Index, share));
        }
    }
}
