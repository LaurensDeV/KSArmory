using Brutal.Numerics;

namespace KSArmory;

/// <summary>Why a store already in the air has no reach to report.</summary>
internal enum TailKitHold
{
    /// <summary>
    /// Nothing could be read where the store is. First so that it is what a default
    /// <see cref="TailKitReach"/> carries: a zero-valued <c>Known</c> would report a store with no
    /// authority left as covering a designation sitting right on top of it, which is the one wrong
    /// answer that reads as a working instrument.
    /// </summary>
    Unreadable,

    /// <summary>There is a region, and it is <see cref="TailKitReach.RadiusMetres"/> across.</summary>
    Known,

    /// <summary>
    /// The store has no kit. It lands where it was thrown, so there is no region to move a landing
    /// inside — not a small one.
    /// </summary>
    Unguided,

    /// <summary>
    /// There is no landing to move. The store is still going up off a climbing rack, or it is
    /// falling for longer than <see cref="BombSight.MaxSteps"/> can follow — the same horizon that
    /// leaves a reentry vehicle released minutes above its target with no pipper.
    /// </summary>
    NoLanding,
}

/// <summary>
/// How far a store already falling can still move its landing, and where that landing is.
///
/// <para><b>This exists to be shown.</b> A kit's authority collapses as the ground comes up, so
/// designating after release works, works partly, or does nothing — with nothing on screen to say
/// which. A player who cannot see the region reads all three as the weapon being broken, which is
/// what sent them clicking at the ground over and over. Same argument <see cref="ReachDisplay"/>
/// makes for the bus, and the same answer: one region, drawn, reported and read off the panel, so
/// the three cannot disagree.</para>
///
/// <para><b>Flown, not solved</b> — and here that is a finding rather than a preference. The
/// obvious closed form is the kinematic <c>½·a·t²</c>, and it is not a bound at any constant
/// fraction: a lateral push does not accumulate against drag, it settles at the drift where fin
/// authority and lateral drag balance, so displacement stops growing as <c>t²</c> and grows as
/// <c>t</c>. Flown against the shipped kit the share of <c>a·t²</c> actually delivered runs 0.46 at
/// a 21 s fall, 0.27 at 54 s and 0.18 at 95 s, so a constant tuned to look right at a few kilometres
/// promises three times what the kit can fly from twenty. <see cref="Fly"/> steps the same
/// <see cref="Slug"/> through the same <see cref="TailKit"/> law the store is obeying, which is
/// what <see cref="BombSight"/> does for the pipper and for the same reason.</para>
///
/// <para><b>It reports; it does not refuse.</b> A designation outside the region is still taken —
/// see <c>WeaponSystem.Designate</c>. The store has nothing better to do than steer at it, landing
/// nearer is strictly better than holding an aim the operator has just replaced, and a refusal here
/// is indistinguishable from the bug this exists to fix. That is the opposite of the bus, where a
/// hop the trim cannot pay for strands warheads; the difference is that a bomb is already falling,
/// so there is no budget left to overspend.</para>
/// </summary>
/// <param name="Hold">Whether there is a region, and why there is not.</param>
/// <param name="SecondsToGo">The fall still to come, flown.</param>
/// <param name="ImpactEcl">Where it comes down with nothing steering it, which the region is centred on.</param>
/// <param name="RadiusMetres">How far that landing can still be moved.</param>
internal readonly record struct TailKitReach(
    TailKitHold Hold, double SecondsToGo, double3 ImpactEcl, double RadiusMetres)
{
    // How far past the region a probe is aimed, as a multiple of the kinematic ceiling. The probe
    // saturates the guidance law rather than being arrived at: a kit sent somewhere it cannot
    // reach pushes at full lateral authority all the way down, and where that puts it is the edge
    // of the region. The law saturates above a·t²/N, so two is three times what it takes.
    //
    // And not more, because the aim is flown to: at four, a store released from 20 km was sent at
    // a point 70 km away, which stretched the fall past BombSight.MaxSteps — the probe never
    // landed and a store with a perfectly good landing reported no region at all.
    private const double ProbeSaturation = 2.0;

    // The share of the swept extreme a designation can be settled on. The probe measures where a
    // saturated kit puts the landing, and arriving somewhere is not stopping there: a kit sent
    // near the edge is still pushing when the ground arrives, so it sweeps past what it could
    // have reached. Flown across ten release geometries the extreme ran up to 1.24 times what the
    // kit could settle within 25 m of, all of the overshoot on the cross-track probe of a moving
    // release; three quarters clears the worst with room, and TailKitReachTests fails if a
    // geometry ever comes in under it.
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
    /// <para>They are independent — the probes need only the landing and the time to it, which the
    /// ballistic flight hands over — so doing all three in one frame buys nothing and spends the
    /// lot at once. Measured in flight, one solve is about 7 ms: amortised that is 0.33 ms a frame
    /// and invisible, but as a single lump every <c>StoreReach.SolveIntervalSeconds</c> it lands on
    /// top of whatever the game's own frame costs.</para>
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
            // The landing, and with it the clock the probes are sized from. A failure here is the
            // whole answer gone rather than one axis of it.
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

        double radius = alongMetres > 0.0 && acrossMetres > 0.0
                            ? SettlingMargin * Math.Min(alongMetres, acrossMetres)
                            : 0.0;

        // Known only once both axes have been flown at least once. Until then the landing is
        // already worth having, but a region is not: the narrower axis is the whole point.
        return radius > 0.0
                   ? new(TailKitHold.Known, seconds, impact, radius)
                   : new(TailKitHold.Unreadable, seconds, impact, 0.0);

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
            (ground as CoarseGroundTest)?.Reset();

            bool landed = BombSight.TryPredict(roundPositionEcl, velocityOverGround, groundVelocityEcl,
                                               groundAccelerationEcl, bodyVelocityEcl, groundVelocityAt,
                                               munition, gravityAt, densityAt, ground, stepSeconds,
                                               scratch, out impactEcl, steerAt, startAge);

            fallSeconds = Math.Max(0, scratch.Count - 1) * stepSeconds;
            return landed;
        }
    }

    // Up at the landing, and the ground track the probes are measured along and across. A store
    // released with no horizontal motion has no track, and any horizontal direction is then as good
    // as any other.
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
    /// <para><b>Three flights, and the two probes are why it is not one.</b> The footprint is not a
    /// circle: moving the landing across the track is a direct sideways push, while moving it along
    /// the track is had by lengthening or shortening the fall, and which of the two is larger
    /// depends on how fast the store was released — flown, along-track beats cross-track at a
    /// 2 km release at 250 m/s and loses at 9 km. The narrower of the two is taken, so the circle
    /// drawn is inscribed in the real footprint rather than circumscribing it.</para>
    ///
    /// <para>Rate-limited by the caller, exactly as the pipper is: this is a readout, and a store's
    /// fall does not change fast enough to need one per frame.</para>
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
        ArgumentNullException.ThrowIfNull(gravityAt);

        if (!munition.SteersItsFall) return new(TailKitHold.Unguided, 0.0, Vec.Zero, 0.0);

        if (!Drop(null, out double3 impact, out double seconds))
        {
            return new(TailKitHold.NoLanding, 0.0, Vec.Zero, 0.0);
        }

        double3 up = Vec.Unit(-gravityAt(impact));
        if (Vec.Len2(up) < 0.5) return new(TailKitHold.Unreadable, 0.0, Vec.Zero, 0.0);

        // The ground track, which is what the two probes are measured along and across. A store
        // released with no horizontal motion at all has none, and any horizontal direction is then
        // as good as any other.
        double3 track = Vec.Unit(Vec.RejectFrom(velocityOverGround, up));
        if (Vec.Len2(track) < 0.5) track = Vec.AnyPerpendicular(up);

        double ceiling = ProbeSaturation * 0.5 * munition.MaxLateralAccel * seconds * seconds;
        double along = Probe(track);
        double across = Probe(Vec.Unit(Vec.Cross(up, track)));

        // A probe that did not land measures nothing, and nothing is not zero: the ballistic flight
        // above landed, so there is a landing here to move. What is missing is one of the two axes
        // the circle is inscribed in, and the remaining one cannot stand in for it — the narrower
        // axis is the whole point, and which of the two it is swaps with release speed. So the
        // region is unknown rather than absent, and rather than guessed at.
        double radius = along > 0.0 && across > 0.0 ? SettlingMargin * Math.Min(along, across) : 0.0;

        return radius > 0.0
                   ? new(TailKitHold.Known, seconds, impact, radius)
                   : new(TailKitHold.Unreadable, 0.0, Vec.Zero, 0.0);

        // How far full authority along one direction moves the landing. The probe is put back onto
        // the surface rather than left out in space: the law solves the fall to the aim's own
        // level, so an aim kilometres up shortens the flight it is steering.
        //
        // Around the BODY's centre, which the ground test is asked for rather than assumed. Built
        // about the ecliptic origin instead it is a sphere centred on the Sun, and the probe is
        // then aimed ~1.5e11 m away -- which is invisible in any rig that puts its planet at the
        // origin, and is every rig that has no reason not to.
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
            // Per flight, not per solve. The cache exists to skip lookups down one trajectory, and
            // these three deliberately end up in different places -- so the second and third would
            // otherwise start against a surface sampled along the first.
            (ground as CoarseGroundTest)?.Reset();

            bool landed = BombSight.TryPredict(roundPositionEcl, velocityOverGround, groundVelocityEcl,
                                               groundAccelerationEcl, bodyVelocityEcl, groundVelocityAt,
                                               munition, gravityAt, densityAt, ground, stepSeconds,
                                               scratch, out impactEcl, steerAt, startAge);

            fallSeconds = Math.Max(0, scratch.Count - 1) * stepSeconds;
            return landed;
        }
    }

    /// <summary>What to tell an operator about a place, in one clause.</summary>
    public string Describe(double3 aimEcl)
        => Hold switch
        {
            TailKitHold.Known when Covers(aimEcl)
                => $"inside the kit's reach ({Distance.Say(MissFrom(aimEcl))} to walk, "
                   + $"{Distance.Say(RadiusMetres)} of authority over {SecondsToGo:F0} s of fall)",
            TailKitHold.Known
                => $"{Distance.Say(ShortfallFrom(aimEcl))} beyond the kit's reach - it will steer "
                   + $"at it and fall short ({Distance.Say(RadiusMetres)} of authority over "
                   + $"{SecondsToGo:F0} s of fall)",
            TailKitHold.Unguided => "unguided - it lands where it was thrown",
            TailKitHold.NoLanding => "no landing to move yet - the kit takes hold once it is coming down",
            _ => "nothing readable where the store is",
        };
}
