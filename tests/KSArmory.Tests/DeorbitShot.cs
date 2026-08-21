using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The 3,459 km near-orbital shot every ballistic budget is measured on, and the one flight model
/// they all fly it with.
///
/// <para>Lifted out of the suites that share it so that two budgets cannot disagree about what the
/// shot <em>is</em> — the planet, the air, the warhead and the arc are one definition here rather
/// than a constant block copied per file.</para>
///
/// <para><b>The planet sits at the origin and does not move.</b> That is the one case where a frame
/// carrier is identically zero, so nothing measured through this rig can see an epoch fault in a
/// term differenced against a body sample. <c>docs/FRAMES-AND-EPOCHS.md</c> has why, and
/// <c>AirSampleEpochTests</c> is where that convention is pinned instead.</para>
///
/// <para>It does not <em>accelerate</em> either, which is a second thing the game does and this does
/// not — hence <c>bodyAccelCci</c>, the one switch here that puts a real-world term back rather than
/// taking one of the round's own approximations away.</para>
/// </summary>
internal static class DeorbitShot
{
    public const double Mu = 3.986004418e14;
    public const double R = 6_371_000.0;
    public const double ScaleHeight = 8_000.0;
    public const double EarthSpin = 7.2921159e-5;

    /// <summary>How far downrange the aim point sits, along the track from the pickup.</summary>
    public const double RangeMetres = 3_459_000.0;

    /// <summary>Where the shot is picked up, which is what the flown scenario resumes into.</summary>
    public const double PickupAltitude = 200_000.0;

    public static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), EarthSpin);

    public static MunitionProfile Warhead => Arsenal.ReentryVehicleMk21;

    public static double DensityAt(double3 pointCci)
        => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    /// <summary>Ground distance between two places on the mean sphere.</summary>
    public static double GroundMetres(double3 a, double3 b) => R * Vec.AngleBetween(a, b);

    /// <summary>
    /// Surface radius under a body-fixed point, with relief on it.
    ///
    /// <para><b>The mean sphere is the one thing a correction loop must not be measured against.</b>
    /// <see cref="AimCorrection"/>'s only observer is <see cref="ImpactPredictor"/>, so a smooth
    /// planet makes that observer noiseless and every extra cycle free averaging of a clean signal —
    /// which is the shape of change this rig keeps scoring as a large win and flight keeps refusing.
    /// The three terms are the continental relief, ranges, and the height field's own interpolation
    /// disagreement; <c>docs/KSA-TERRAIN.md</c> has the measured figures and the 0.2985 m quantum
    /// they are rounded onto.</para>
    /// </summary>
    public static double RoughGround(double3 bodyFixedCci)
    {
        double3 u = Vec.Unit(bodyFixedCci);

        double height = 800.0 * Math.Sin(u.X * 12.0) * Math.Cos(u.Y * 9.0)
                      + 150.0 * Math.Sin(u.Y * 130.0 + 1.7) * Math.Cos(u.Z * 110.0)
                      +  40.0 * Math.Sin(u.X * 2100.0 + 0.4) * Math.Sin(u.Y * 1900.0 + 2.1);

        return R + Math.Round(height / HeightQuantumMetres) * HeightQuantumMetres;
    }

    /// <summary>`R16_UNORM` over the 19,561 m range the height field declares.</summary>
    public const double HeightQuantumMetres = 0.2985;

    /// <summary>
    /// The same relief with the sea filled in, which is the surface a round actually stops on.
    ///
    /// <para><c>Ksa/GroundTest.cs</c> passes the height field through <see cref="GroundSurface"/>
    /// and <c>Ksa/IcbmComputer.cs</c>'s <c>TerrainRadiusAt</c> does not, so the round and the
    /// prediction of it read two different surfaces wherever the terrain is under water.
    /// <c>docs/KSA-TERRAIN.md</c> has the measurement.</para>
    /// </summary>
    public static double RoughGroundAtSea(double3 bodyFixedCci)
        => R + GroundSurface.Height(RoughGround(bodyFixedCci) - R, seaLevel: 0.0, hasSea: true);

    /// <summary>The mean sphere, as the thing a round asks where the ground is.</summary>
    public sealed class Ball : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = R;
            return true;
        }
    }

    /// <summary>
    /// The shot: picked up at near-orbital speed 200 km up, aimed 3,459 km downrange.
    /// </summary>
    public static BallisticArc.Solution Shot(out double3 from, out double3 target)
    {
        from = new double3(R + PickupAltitude, 0, 0);
        target = new double3(R * Math.Cos(RangeMetres / R), R * Math.Sin(RangeMetres / R), 0);

        double3 circular = new(0, Math.Sqrt(Mu / (R + PickupAltitude)), 0);
        Assert.True(BallisticArc.TryCheapest(Earth, from, circular, target, out BallisticArc.Solution s));
        return s;
    }

    /// <summary>Where a warhead released from this state comes down, as a place on the ground.</summary>
    /// <param name="terrainRadiusAt">The surface to stop on. Null is the mean sphere.</param>
    public static double3 Land(double3 fromCci, double3 velocityCci,
                               Func<double3, double>? terrainRadiusAt = null)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, fromCci, velocityCci, 1.0, 20_000.0,
                                               out ImpactPredictor.Impact hit, terrainRadiusAt, null,
                                               new ImpactPredictor.Drag(DensityAt, Warhead)));
        return hit.GroundFixedPointCci;
    }

    /// <summary>
    /// <see cref="RoughGround"/> as the thing a round asks where the ground is, which is not the
    /// question <see cref="ImpactPredictor"/> asks of the same relief.
    ///
    /// <para>Three of the round's own approximations live here rather than in the terrain: the
    /// answer is a <b>sphere</b> — one centre and one radius for a whole frame — it is taken at the
    /// round's position at the <em>top</em> of that frame while the round crosses it, and it is
    /// clamped to the waterline. Each is a switch because each is a term, and the predictor has
    /// none of them.</para>
    /// </summary>
    public sealed class Relief : IGroundTest
    {
        private bool _held;
        private double3 _centre;
        private double _radius;

        /// <summary>
        /// Flight time so far. The engine's <c>Cce</c> entry point applies the planet's current
        /// phase for free; here the un-carry is by hand, and skipping it reads the ground the shot
        /// started over rather than the ground under the round.
        /// </summary>
        public double Seconds { get; set; }

        /// <summary>Clamp to the waterline, as <c>Ksa/GroundTest.cs</c> does and the predictor does not.</summary>
        public bool Waterline { get; set; }

        /// <summary>
        /// The surface under a body-fixed point. Null is <see cref="RoughGround"/>.
        ///
        /// <para>Whatever it is, the round reads it through the three approximations above and
        /// <see cref="ImpactPredictor"/> reads it directly — which is the only way to hand both
        /// sides one surface and still have the difference be the round's.</para>
        /// </summary>
        public Func<double3, double>? Surface { get; init; }

        /// <summary>Hold one answer for a whole frame, which is what <see cref="Slug"/> asks for.</summary>
        public bool HoldForTheFrame { get; set; } = true;

        /// <summary>Terrain lookups taken. The cost every re-sampling proposal is traded against.</summary>
        public int Sampled { get; private set; }

        /// <summary>A new frame, so a held answer is stale.</summary>
        public void BeginFrame() => _held = false;

        /// <inheritdoc />
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            if (!_held || !HoldForTheFrame)
            {
                Sampled++;
                double3 bodyFixed = Earth.UncarryCci(positionEcl, Seconds);

                _centre = Vec.Zero;
                _radius = Surface is { } surface
                        ? surface(bodyFixed)
                        : Waterline ? RoughGroundAtSea(bodyFixed) : RoughGround(bodyFixed);
                _held = true;
            }

            centreEcl = _centre;
            surfaceRadius = _radius;
            return true;
        }
    }

    /// <summary>
    /// Which of the round's frame-level inputs are re-read per sub-step rather than held for the
    /// whole frame, and how finely it integrates while that happens.
    ///
    /// <para>None of it is what the game does: <c>WeaponSystem</c> samples gravity and the air's
    /// motion once, at the round's position at the top of the frame, and <see cref="Slug"/> holds
    /// both — and the ground — across every 5 ms sub-step inside it.</para>
    ///
    /// <para>Re-reading the ground is only a term over real relief. Against <see cref="Ball"/> the
    /// answer cannot change, which is why every budget taken on the mean sphere reports it as
    /// nothing.</para>
    /// </summary>
    /// <param name="Gravity">Re-evaluate gravity at the round's own position each sub-step.</param>
    /// <param name="AirMotion">Re-evaluate the air's own velocity each sub-step.</param>
    public readonly record struct Refresh(bool Gravity, bool AirMotion)
    {
        /// <summary>Nothing re-read: the round exactly as the game flies it.</summary>
        public static Refresh AsFlown => new(false, false);

        /// <summary>Re-sample the ground each sub-step. Needs a <see cref="Relief"/> to mean anything.</summary>
        public bool Ground { get; init; }

        /// <summary>
        /// Integrate at this step rather than the round's own 5 ms, by handing it <c>Update</c>s
        /// that short — <c>steps = ceil(dt / SubStep)</c> bottoms out at one, so a shorter frame
        /// <em>is</em> a shorter sub-step. Zero leaves it alone.
        /// </summary>
        public double StepSeconds { get; init; }

        public bool Any => Gravity || AirMotion || Ground || StepSeconds > 0.0;

        /// <summary>How long each <c>Update</c> the frame is divided into may be.</summary>
        internal double Slice => StepSeconds > 0.0 ? StepSeconds : Interceptor.SubStep;
    }

    /// <summary>The round as the game flies it: sub-stepped, air re-read per sub-step, ground sphere.</summary>
    /// <param name="dt">The frame the round is handed, which is what the world is warped to.</param>
    /// <param name="refresh">Which frame-level inputs to re-read per sub-step instead of holding.</param>
    /// <param name="ground">Where the ground is. Null is the mean sphere.</param>
    /// <param name="bodyAccelCci"><inheritdoc cref="FlyTheRoundAsWarped" path="/param[@name='bodyAccelCci']"/></param>
    public static (double3 GroundFixed, double Seconds) FlyTheRound(double3 fromCci, double3 velocityCci,
                                                                   double dt,
                                                                   Refresh refresh = default,
                                                                   IGroundTest? ground = null,
                                                                   double3 bodyAccelCci = default)
    {
        BallisticBody body = Earth;

        Slug round = new(fromCci, velocityCci, null, 1, fromCci, Vec.Zero)
        {
            Munition = Warhead,
            Ground = ground ?? new Ball(),
            AirDensityAt = (pos, _) => DensityAt(pos),
        };

        double elapsed = 0.0;

        for (int i = 0; i < (int)(20_000.0 / dt) && round.State == RoundState.Flying; i++)
        {
            elapsed = OneFrame(body, round, dt, fromCci, refresh, ground, elapsed, bodyAccelCci);
        }

        return Arrived(body, round, elapsed);
    }

    /// <summary>
    /// One frame of the world handed to the round, divided into as many <c>Update</c>s as the
    /// switches ask for.
    ///
    /// <para>Everything not being re-read is held at the sample taken here, at the top of the
    /// frame — which is what leaves the difference between two runs being one named term rather
    /// than all of them at once.</para>
    /// </summary>
    private static double OneFrame(BallisticBody body, Slug round, double dt, double3 fromCci,
                                   Refresh refresh, IGroundTest? ground, double elapsed,
                                   double3 bodyAccelCci = default)
    {
        int n = refresh.Any ? Math.Max(1, (int)Math.Ceiling(dt / refresh.Slice)) : 1;

        double3 heldGravity = body.GravityCci(round.PositionEcl);
        double3 heldAir = body.GroundVelocityCci(round.PositionEcl);

        if (ground is Relief relief)
        {
            relief.HoldForTheFrame = !refresh.Ground;
            relief.BeginFrame();
        }

        for (int k = 0; k < n && round.State == RoundState.Flying; k++)
        {
            // Less the body's own acceleration, because the round is integrated about a centre that
            // is itself falling and the prediction of it is not. Subtracting it here is exact
            // rather than approximate: the solar tide across a planet's radius is 0.009% of the
            // term, so the field really is uniform over everything a round can reach.
            double3 gravity = (refresh.Gravity ? body.GravityCci(round.PositionEcl) : heldGravity)
                              - bodyAccelCci;
            double3 air = refresh.AirMotion ? body.GroundVelocityCci(round.PositionEcl) : heldAir;

            if (ground is Relief r) r.Seconds = elapsed;

            round.Update(dt / n, null, gravity, air, fromCci, Warhead, DensityAt(round.PositionEcl));
            elapsed += dt / n;
        }

        return elapsed;
    }

    /// <summary>
    /// Where the round stopped, as a place on the ground, and how long it really took to get there.
    ///
    /// <para>The flight time is the frames issued <em>less</em> the part of the last one the round
    /// did not need: a round stops on a sub-step, so counting whole frames overshoots by up to one
    /// of them. Un-carrying by that turns the overshoot into ground — 465 m a second at the equator,
    /// which on a 320 ms frame is enough to read as guidance error.</para>
    /// </summary>
    private static (double3 GroundFixed, double Seconds) Arrived(BallisticBody body, Slug round,
                                                                 double framesIssued)
    {
        Assert.NotEqual(RoundState.Flying, round.State);

        double seconds = framesIssued + Math.Min(0.0, round.DetonationElapsedInFrame);
        return (body.UncarryCci(round.PositionEcl, seconds), seconds);
    }

    /// <summary>
    /// The speed <c>Ksa/BallisticScenario.cs</c> asks for once the salvo is away, which is what
    /// sets the frame the coast is flown at. <c>WarpPolicy</c> then slows it for the entry.
    /// </summary>
    public const double ScenarioWarp = 8.0;

    /// <summary>
    /// The frame a warp factor multiplies. Measured from flight rather than assumed: a traced coast
    /// ran a median 198.5 ms at the scenario's 8x, so the unwarped frame under that load is nearer
    /// 25 ms than the 16.7 ms sixty frames a second would give. The rig's own gap is linear in this
    /// at about 4.2 m per millisecond, so assuming 60 fps understates it by a third.
    /// </summary>
    public const double NominalFrame = 0.025;

    /// <summary>
    /// The round at the step the world is actually held to: coarse through the vacuum coast, fine
    /// once there is air.
    ///
    /// <para>That is what <c>WarpPolicy</c> asks for through <c>IProjectile.FaithfulStepSeconds</c>,
    /// and it is not the same as either constant step — the coast runs at whatever warp the player
    /// (or the scenario) asked for, and the entry pulls it back to <see cref="Medium.FaithfulStepInAir"/>.
    /// </para>
    /// </summary>
    /// <param name="warp">The simulation speed held during the coast.</param>
    /// <param name="refresh">Which frame-level inputs to re-read per sub-step instead of holding.</param>
    /// <param name="ground">Where the ground is. Null is the mean sphere.</param>
    /// <param name="bodyAccelCci">
    /// The parent body's own acceleration about whatever it orbits — the one force a round in
    /// <c>Ecl</c> feels and <see cref="ImpactPredictor"/> in <c>Cci</c> cannot. Zero is this rig's
    /// usual planet at the origin. <c>ModelInputAgreementTests</c> prices it.
    /// </param>
    public static (double3 GroundFixed, double Seconds) FlyTheRoundAsWarped(
        double3 fromCci, double3 velocityCci, double warp, Refresh refresh = default,
        IGroundTest? ground = null, double3 bodyAccelCci = default)
    {
        BallisticBody body = Earth;

        Slug round = new(fromCci, velocityCci, null, 1, fromCci, Vec.Zero)
        {
            Munition = Warhead,
            Ground = ground ?? new Ball(),
            AirDensityAt = (pos, _) => DensityAt(pos),
        };

        double elapsed = 0.0;
        double dt = NominalFrame;

        while (round.State == RoundState.Flying && elapsed < 20_000.0)
        {
            elapsed = OneFrame(body, round, dt, fromCci, refresh, ground, elapsed, bodyAccelCci);

            // What the mod asks the world for on the next frame, capped by the speed the scenario
            // runner asks for once the salvo is away.
            dt = Math.Clamp(round.FaithfulStepSeconds, NominalFrame, warp * NominalFrame);
        }

        return Arrived(body, round, elapsed);
    }

    /// <summary>The widest gap between any two of a group's impacts, on the ground.</summary>
    public static double Spread(IReadOnlyList<double3> landed)
    {
        double worst = 0.0;
        for (int a = 0; a < landed.Count; a++)
        {
            for (int b = a + 1; b < landed.Count; b++)
            {
                worst = Math.Max(worst, GroundMetres(landed[a], landed[b]));
            }
        }

        return worst;
    }

    /// <summary>How far the group's own centre sits from where it was aimed.</summary>
    public static double CommonBias(IReadOnlyList<double3> landed, double3 target)
    {
        double3 sum = Vec.Zero;
        foreach (double3 p in landed) sum += p;

        return GroundMetres(Vec.Unit(sum) * R, target);
    }
}
