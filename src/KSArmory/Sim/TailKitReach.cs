using Brutal.Numerics;

namespace KSArmory;

/// <summary>Why a store already in the air has no reach to report.</summary>
internal enum TailKitHold
{
    /// <summary>
    /// Nothing could be read where the store is. First so a default <see cref="TailKitReach"/> carries it:
    /// a zero-valued <c>Known</c> would read as covering a designation right on top of the store.
    /// </summary>
    Unreadable,

    /// <summary>There is a region, and it is <see cref="TailKitReach.RadiusMetres"/> across.</summary>
    Known,

    /// <summary>The store has no kit: no region at all, not a small one.</summary>
    Unguided,

    /// <summary>
    /// There is no landing to move: the store is still climbing, or falls for longer than
    /// <see cref="BombSight.MaxSteps"/> can follow.
    /// </summary>
    NoLanding,
}

/// <summary>
/// How far a store already falling can still move its landing, and where that landing is.
///
/// <para>Flown, not solved: <c>½·a·t²</c> is no bound at any constant fraction, since a lateral push
/// settles against drag; the shipped kit delivers 0.46 of <c>a·t²</c> at a 21 s fall and 0.18 at 95 s.</para>
///
/// <para>It decides as well as reports (<see cref="StoreRetarget.Reaches"/>), so it has to stay a floor.
/// CLAUDE.md, "Design decisions worth not re-litigating", has the rest.</para>
/// </summary>
/// <param name="Hold">Whether there is a region, and why there is not.</param>
/// <param name="SecondsToGo">The fall still to come, flown.</param>
/// <param name="ImpactEcl">Where it comes down with nothing steering it, which the region is centred on.</param>
/// <param name="RadiusMetres">How far that landing can still be moved.</param>
internal readonly record struct TailKitReach(
    TailKitHold Hold, double SecondsToGo, double3 ImpactEcl, double RadiusMetres)
{
    // A multiple of the kinematic ceiling, enough to saturate the law (above a·t²/N). Not more: at four a
    // 20 km release was sent 70 km away, the fall outran BombSight.MaxSteps and no region was reported.
    private const double ProbeSaturation = 2.0;

    // A saturated kit sweeps past what it could settle on: across ten release geometries the extreme ran
    // up to 1.24x what settled within 25 m. TailKitReachTests fails if a geometry comes in under this.
    private const double SettlingMargin = 0.75;

    /// <summary>Whether there is a region at all.</summary>
    public bool Known => Hold == TailKitHold.Known;

    /// <summary>How far the aim sits from where the store would come down untouched.</summary>
    public double MissFrom(double3 aimEcl) => Vec.Len(aimEcl - ImpactEcl);

    /// <summary>Whether the kit can be expected to arrive at a place.</summary>
    public bool Covers(double3 aimEcl) => Known && MissFrom(aimEcl) <= RadiusMetres;

    /// <summary>How far outside the region a place sits. Zero when it is inside.</summary>
    public double ShortfallFrom(double3 aimEcl)
        => Known ? Math.Max(0.0, MissFrom(aimEcl) - RadiusMetres) : double.NaN;

    /// <summary>
    /// One of the three flights, so a caller can spread them over as many frames.
    ///
    /// <para>One solve measured about 7 ms in flight, which as a single lump lands on one frame.</para>
    /// </summary>
    /// <param name="stage">0 for the landing, 1 along the ground track, 2 across it.</param>
    /// <param name="carried">
    /// What the previous stages found, which stages 1 and 2 depart from — its landing put back on
    /// this instant's ground, since as an ecliptic point it is left behind by the planet's motion.
    /// </param>
    public static TailKitReach FlyStage(int stage, TailKitReach carried,
                                        double3 roundPositionEcl, double3 velocityOverGround,
                                        double3 groundVelocityEcl, double3 groundAccelerationEcl,
                                        double3 bodyVelocityEcl,
                                        Func<double3, double3> groundVelocityAt,
                                        MunitionProfile munition,
                                        Func<double3, double3> gravityAt,
                                        Func<double3, double> densityAt,
                                        IGroundTest? ground,
                                        double stepSeconds,
                                        List<double3> scratch,
                                        ref double alongMetres, ref double acrossMetres,
                                        double startAge = 0.0)
    {
        ArgumentNullException.ThrowIfNull(munition);
        ArgumentNullException.ThrowIfNull(gravityAt);

        if (!munition.SteersItsFall)
        {
            alongMetres = 0.0;
            acrossMetres = 0.0;
            return new(TailKitHold.Unguided, 0.0, Vec.Zero, 0.0);
        }

        double3 impact = carried.ImpactEcl;
        double seconds = carried.SecondsToGo;

        if (stage == 0)
        {
            // The clock the probes are sized from, so a failure here loses the whole answer.
            if (!Drop(null, out impact, out seconds))
            {
                alongMetres = 0.0;
                acrossMetres = 0.0;
                return new(TailKitHold.NoLanding, 0.0, Vec.Zero, 0.0);
            }
        }
        else if (!(seconds > 0.0))
        {
            // Nothing to depart from yet: stage 0 has not answered since the last reset.
            return carried;
        }

        if (!Axes(impact, velocityOverGround, gravityAt, out double3 up, out double3 track))
        {
            return new(TailKitHold.Unreadable, 0.0, Vec.Zero, 0.0);
        }

        double ceiling = ProbeSaturation * 0.5 * munition.MaxLateralAccel * seconds * seconds;

        if (stage == 1) alongMetres = Probe(track);
        else if (stage == 2) acrossMetres = Probe(Vec.Unit(Vec.Cross(up, track)));

        // A probe that did not land is unknown, not zero, and the other axis cannot stand in for it:
        // which one is narrower swaps with release speed.
        double radius = alongMetres > 0.0 && acrossMetres > 0.0
                            ? SettlingMargin * Math.Min(alongMetres, acrossMetres)
                            : 0.0;
        return radius > 0.0
                   ? new(TailKitHold.Known, seconds, impact, radius)
                   : new(TailKitHold.Unreadable, seconds, impact, 0.0);

        // Aimed on the surface, since the law solves the fall to the aim's own level, and about the
        // body's centre from the ground test: about the ecliptic origin it is a sphere round the Sun.
        double Probe(double3 direction)
        {
            if (ground is null
                || !ground.TryGround(impact, out double3 centre, out double surfaceRadius))
            {
                return 0.0;
            }

            double3 out2 = (impact - centre) + (direction * ceiling);
            double3 at = centre + (Vec.Unit(out2) * surfaceRadius);

            return Drop(at, out double3 moved, out _) ? Vec.Len(moved - impact) : 0.0;
        }

        bool Drop(double3? steerAt, out double3 impactEcl, out double fallSeconds)
        {
            // The cache follows one trajectory, and the three flights end in different places.
            (ground as CoarseGroundTest)?.Reset();

            bool landed = BombSight.TryPredict(roundPositionEcl, velocityOverGround, groundVelocityEcl,
                                               groundAccelerationEcl, bodyVelocityEcl, groundVelocityAt,
                                               munition, gravityAt, densityAt, ground, stepSeconds,
                                               scratch, out impactEcl, steerAt, startAge);

            fallSeconds = Math.Max(0, scratch.Count - 1) * stepSeconds;
            return landed;
        }
    }

    // A store with no horizontal motion has no track, and any horizontal direction will do.
    private static bool Axes(double3 impact, double3 velocityOverGround,
                             Func<double3, double3> gravityAt, out double3 up, out double3 track)
    {
        up = Vec.Unit(-gravityAt(impact));
        track = Vec.Zero;

        if (Vec.Len2(up) < 0.5) return false;

        track = Vec.Unit(Vec.RejectFrom(velocityOverGround, up));
        if (Vec.Len2(track) < 0.5) track = Vec.AnyPerpendicular(up);

        return true;
    }

    /// <summary>
    /// Flies the store to the ground three times: once untouched, and once at full authority along
    /// each of the ground track and across it.
    ///
    /// <para>The footprint is an ellipse, and which axis is narrower swaps with release (along-track wins at
    /// 2 km and 250 m/s, loses at 9 km), so the narrower is taken. Rate-limited by the caller.</para>
    /// </summary>
    /// <param name="roundPositionEcl">Where the store is now.</param>
    /// <param name="velocityOverGround">What it is doing, against the ground beneath it.</param>
    /// <param name="scratch">A path buffer, reused across the three flights.</param>
    /// <inheritdoc cref="BombSight.TryPredict"/>
    public static TailKitReach Fly(double3 roundPositionEcl, double3 velocityOverGround,
                                   double3 groundVelocityEcl, double3 groundAccelerationEcl,
                                   double3 bodyVelocityEcl,
                                   Func<double3, double3> groundVelocityAt,
                                   MunitionProfile munition,
                                   Func<double3, double3> gravityAt,
                                   Func<double3, double> densityAt,
                                   IGroundTest? ground,
                                   double stepSeconds,
                                   List<double3> scratch,
                                   double startAge = 0.0)
    {
        ArgumentNullException.ThrowIfNull(munition);
        ArgumentNullException.ThrowIfNull(scratch);

        double along = 0.0, across = 0.0;
        TailKitReach reach = new(TailKitHold.Unreadable, 0.0, Vec.Zero, 0.0);

        for (int stage = 0; stage <= 2; stage++)
        {
            reach = FlyStage(stage, reach, roundPositionEcl, velocityOverGround, groundVelocityEcl,
                             groundAccelerationEcl, bodyVelocityEcl, groundVelocityAt, munition,
                             gravityAt, densityAt, ground, stepSeconds, scratch,
                             ref along, ref across, startAge);
        }

        // All at one instant, so an unknown region carries no landing for a later stage to start from.
        return reach.Hold == TailKitHold.Unreadable ? new(TailKitHold.Unreadable, 0.0, Vec.Zero, 0.0) : reach;
    }

    /// <summary>What to tell an operator about a place, in one clause.</summary>
    public string Describe(double3 aimEcl)
        => Hold switch
        {
            TailKitHold.Known when Covers(aimEcl)
                => $"inside the kit's reach ({Distance.Say(MissFrom(aimEcl))} to walk, "
                   + $"{Distance.Say(RadiusMetres)} of authority over {SecondsToGo:F0} s of fall)",
            TailKitHold.Known
                => $"{Distance.Say(ShortfallFrom(aimEcl))} beyond the kit's reach "
                   + $"({Distance.Say(RadiusMetres)} of authority over {SecondsToGo:F0} s of fall)",
            TailKitHold.Unguided => "unguided - it lands where it was thrown",
            TailKitHold.NoLanding => "no landing to move yet - the kit takes hold once it is coming down",
            _ => "nothing readable where the store is",
        };
}
