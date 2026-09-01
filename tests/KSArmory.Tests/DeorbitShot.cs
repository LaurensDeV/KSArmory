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
    /// <para>Both the round and the prediction of it clamp here — <c>Ksa/GroundTest.cs</c> directly
    /// and <c>Ksa/IcbmComputer.cs</c>'s <c>TerrainRadiusAt</c> through its <c>SurfaceHeight</c> — so
    /// this is the surface each of them stops on rather than a difference between them.
    /// <c>docs/KSA-TERRAIN.md</c> has what the height field reports under an ocean.</para>
    /// </summary>
    public static double RoughGroundAtSea(double3 bodyFixedCci)
        => R + GroundSurface.Height(RoughGround(bodyFixedCci) - R, seaLevel: 0.0, hasSea: true);

    /// <summary>The mean sphere, as the thing a round asks where the ground is.</summary>
    public sealed class Ball : IGroundTest
    {
        /// <summary>
        /// Where this test believes the body's centre is. Zero is the truth here, because the rig's
        /// planet sits at the origin — it is settable so a caller can ask what a body sample taken
        /// at the wrong instant costs, which is a term the game has and this world otherwise cannot
        /// express at all.
        /// </summary>
        public double3 CentreEcl;

        /// <summary>
        /// Where the carrier has taken the body by this frame, kept apart from
        /// <see cref="CentreEcl"/> so the two compose: one is a deliberate mis-placement under
        /// test, the other is the world moving, and either overwriting the other silently deletes
        /// the case being measured.
        /// </summary>
        public double3 CarriedCentreEcl;

        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = CentreEcl + CarriedCentreEcl;
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
    /// How this flight differs from the round the game flies — and <b>nothing</b> is the default.
    ///
    /// <para>Both this and the game go through <see cref="RoundDriver"/>, so the shipped
    /// configuration is what asking for nothing gives. That is the point of the default: a budget
    /// is differenced against its baseline, so a baseline that is wrong misprices every other term
    /// rather than only its own.</para>
    ///
    /// <para><b>Two of these price something the game cannot do at all</b> and are still emulated
    /// by slicing, because there is no lookup to attach: <see cref="Slug"/> takes the air's own
    /// motion as one value per frame and samples the ground once before its sub-step loop. They
    /// are worth -2 m and 0-22 m respectively, which is why nothing has ever been built to let the
    /// game do them.</para>
    /// </summary>
    public readonly record struct Refresh
    {
        /// <summary>
        /// Hold gravity at the frame's first sample rather than re-reading it at the round's own
        /// position each sub-step. <b>Pricing only</b> — see
        /// <see cref="BeforeGravityPerSubStep"/>.
        /// </summary>
        public bool HoldGravity { get; init; }

        /// <summary>Re-evaluate the air's own velocity per slice. The game cannot; see above.</summary>
        public bool AirMotion { get; init; }

        /// <summary>Re-sample the ground per slice. The game cannot; see above.</summary>
        public bool Ground { get; init; }

        /// <summary>
        /// Integrate at this sub-step rather than the profile's own. Zero leaves it alone.
        ///
        /// <para>Applied as <see cref="MunitionProfile.SubStepSeconds"/> on a copy of the round's
        /// profile — the shipped mechanism, and the one <c>arm/substep</c> uses — rather than by
        /// handing the round shorter frames. Those are not the same thing: a shorter frame also
        /// re-samples the ground and the air's motion, so the old form priced three changes and
        /// called them one.</para>
        /// </summary>
        public double StepSeconds { get; init; }

        /// <summary>The round exactly as the game flies it, which is the default.</summary>
        public static Refresh AsFlown => default;

        /// <summary>
        /// The round before the per-sub-step gravity landed, kept only so a budget can show what
        /// that change was worth. Nothing in the shipped tree flies this way.
        /// </summary>
        public static Refresh BeforeGravityPerSubStep => new() { HoldGravity = true };

        /// <summary>Whether anything here needs the frame cut up, which only the two do.</summary>
        internal bool NeedsSlicing => AirMotion || Ground;
    }

    /// <summary>
    /// The body's own motion through the ecliptic, which this world otherwise does not have.
    ///
    /// <para>KSA carries a planet at ~29.8 km/s and integrates rounds in <c>Ecl</c>, so every
    /// quantity a round is differenced against carries that speed and only cancels when the two
    /// terms belong to the <em>same instant</em>. A rig whose planet sits at the origin has that
    /// term identically zero, which makes it blind to the whole class of fault by construction —
    /// not bad at seeing them, incapable.</para>
    ///
    /// <para>A constant velocity is enough: the carrier is a pure translation, so it changes no
    /// physics and a correctly paired flight must land in the same body-fixed place with it as
    /// without. That invariance is the test, and it catches a mis-paired instant whether or not
    /// anybody thought to model that particular one.</para>
    /// </summary>
    public readonly record struct Carrier(double3 MetresPerSecond)
    {
        /// <summary>Earth's own ecliptic speed, along a direction square to nothing in particular.</summary>
        public static Carrier Earthlike => new(new double3(29_800.0, 0.0, 0.0));

        /// <summary>A planet at the origin, which is what every budget here is taken against.</summary>
        public static Carrier Still => default;

        /// <summary>Where the body's centre is at a stated instant of the flight.</summary>
        public double3 At(double seconds) => MetresPerSecond * seconds;
    }

    /// <summary>The round's profile, with a finer sub-step if one was asked for.</summary>
    private static MunitionProfile MunitionFor(Refresh refresh)
        => refresh.StepSeconds > 0.0
               ? MunitionVariant.Of(Warhead, m => m.SubStepSeconds = (float)refresh.StepSeconds)
               : Warhead;

    /// <summary>The round as the game flies it: sub-stepped, air re-read per sub-step, ground sphere.</summary>
    /// <param name="dt">The frame the round is handed, which is what the world is warped to.</param>
    /// <param name="refresh">Which frame-level inputs to re-read per sub-step instead of holding.</param>
    /// <param name="ground">Where the ground is. Null is the mean sphere.</param>
    /// <param name="bodyAccelCci"><inheritdoc cref="FlyTheRoundAsWarped" path="/param[@name='bodyAccelCci']"/></param>
    /// <param name="gravityCentreCci">
    /// Where the round is pulled toward, if not the origin.
    ///
    /// <para>The game reads a round's gravity at its pre-step position against a celestial sample
    /// from the frame's end, so the pull centre sits <c>bodyVelocity × dt</c> away — 516 m at
    /// 29.8 km/s on a 17 ms frame. This world's planet does not move, so that displacement is
    /// identically zero here and has to be asked for.</para>
    /// </param>
    public static (double3 GroundFixed, double Seconds) FlyTheRound(double3 fromCci, double3 velocityCci,
                                                                   double dt,
                                                                   Refresh refresh = default,
                                                                   IGroundTest? ground = null,
                                                                   double3 bodyAccelCci = default,
                                                                   double3 gravityCentreCci = default,
                                                                   Carrier carrier = default)
    {
        BallisticBody body = Earth;

        // Once, because RoundDriver assigns it every frame: a null here is not "leave it
        // alone", it is a round nothing stops, and it flies through the planet.
        IGroundTest surface = ground ?? new Ball();

        // Started where the carrier has the body at t=0, so the round's stored coordinates carry
        // the ecliptic term the game's do. Its velocity carries it too: a body's own motion is
        // shared by everything riding it.
        Slug round = new(fromCci + carrier.At(0.0), velocityCci + carrier.MetresPerSecond, null, 1,
                         fromCci + carrier.At(0.0), Vec.Zero)
        {
            Munition = MunitionFor(refresh),
            Ground = surface,
        };

        double elapsed = 0.0;

        for (int i = 0; i < (int)(20_000.0 / dt) && round.State == RoundState.Flying; i++)
        {
            elapsed = OneFrame(body, round, dt, fromCci, refresh, surface, elapsed, bodyAccelCci,
                               gravityCentreCci, carrier);
        }

        return Arrived(body, round, elapsed, carrier);
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
                                   double3 bodyAccelCci = default,
                                   double3 gravityCentreCci = default,
                                   Carrier carrier = default)
    {
        // Two instants, and keeping them apart is the whole of what this models. The frame's
        // celestial sample is taken at its end - docs/KSA-FRAME-ORDER.md section 5 - and the
        // lookups below back-date off it by the seconds-into-frame the round hands over, which
        // lands each one on the round's own instant. Anything read once for the whole frame is
        // read where the round is when the frame starts, which is a different place.
        double3 sampledCentre = carrier.At(elapsed + dt);
        double3 roundCentre = carrier.At(elapsed);

        // The ground gets no time argument, so it can only be paired one way: where the round is.
        if (ground is Ball ball) ball.CarriedCentreEcl = roundCentre;

        // Only the two the game has no lookup for. Everything else is expressed as configuration
        // the game could itself be given, so the rig and the tree cannot hold different opinions.
        int n = refresh.NeedsSlicing ? Math.Max(1, (int)Math.Ceiling(dt / Interceptor.SubStep)) : 1;

        // About a stated centre rather than about the origin, so a caller can put the pull centre
        // where a body sample from the wrong instant would put it. Zero is the honest answer and
        // every existing caller takes it.
        //
        // Less the body's own acceleration, because the round is integrated about a centre that is
        // itself falling and the prediction of it is not. Subtracting it is exact rather than
        // approximate: the solar tide across a planet's radius is 0.009% of the term.
        double3 heldGravity = body.GravityCci(round.PositionEcl - roundCentre - gravityCentreCci)
                              - bodyAccelCci;
        double heldDensity = DensityAt(round.PositionEcl - roundCentre);

        // The air rides the body, so its motion carries the body's own. Leaving that off gives the
        // round the whole carrier as a headwind, which is not a pairing error but a different
        // planet: measured at 98 s of flight time and 550 km of impact.
        double3 heldAir = body.GroundVelocityCci(round.PositionEcl - roundCentre)
                          + carrier.MetresPerSecond;

        if (ground is Relief relief)
        {
            relief.HoldForTheFrame = !refresh.Ground;
            relief.BeginFrame();
        }

        // What the game attaches, spelled the same way. A held field is a lookup that is absent,
        // which is why HoldGravity passes null rather than a lookup that returns a constant.
        RoundFields fields = new(
            refresh.HoldGravity
                ? null
                : (pos, into) => body.GravityCci(pos - Centre(into) - gravityCentreCci) - bodyAccelCci,
            (pos, into) => DensityAt(pos - Centre(into)),
            ground);

        // The body where it was when the round was there, rather than where the frame's sample
        // found it. `into` is negative: it is how far back from the sample the round has got to.
        double3 Centre(double into) => sampledCentre + carrier.MetresPerSecond * into;

        for (int k = 0; k < n && round.State == RoundState.Flying; k++)
        {
            double3 air = refresh.AirMotion
                              ? body.GroundVelocityCci(round.PositionEcl - carrier.At(elapsed))
                                + carrier.MetresPerSecond
                              : heldAir;

            if (ground is Relief r) r.Seconds = elapsed;

            RoundDriver.Fly(round, dt / n, null, heldGravity, air, fromCci + carrier.At(elapsed),
                            round.Munition, heldDensity, fields);
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
                                                                 double framesIssued,
                                                                 Carrier carrier = default)
    {
        Assert.NotEqual(RoundState.Flying, round.State);

        double seconds = framesIssued + Math.Min(0.0, round.DetonationElapsedInFrame);

        // Out of the ecliptic first, then out of the body's spin. Both at the impact's own instant.
        return (body.UncarryCci(round.PositionEcl - carrier.At(seconds), seconds), seconds);
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

        // Once, because RoundDriver assigns it every frame: a null here is not "leave it
        // alone", it is a round nothing stops, and it flies through the planet.
        IGroundTest surface = ground ?? new Ball();

        Slug round = new(fromCci, velocityCci, null, 1, fromCci, Vec.Zero)
        {
            Munition = MunitionFor(refresh),
            Ground = surface,
        };

        double elapsed = 0.0;
        double dt = NominalFrame;

        while (round.State == RoundState.Flying && elapsed < 20_000.0)
        {
            elapsed = OneFrame(body, round, dt, fromCci, refresh, surface, elapsed, bodyAccelCci);

            // What the mod asks the world for on the next frame, capped by the speed the scenario
            // runner asks for once the salvo is away.
            dt = Math.Clamp(round.FaithfulStepSeconds, NominalFrame, warp * NominalFrame);
        }

        return Arrived(body, round, elapsed);
    }

    /// <summary>What one flight under <see cref="WarpPolicy"/> did, beside where it landed.</summary>
    /// <param name="GroundFixed">Where it came down, body-fixed.</param>
    /// <param name="Seconds">How long it took.</param>
    /// <param name="MeanStep">
    /// The mean simulated step across the vacuum coast, which is the number the round's
    /// disagreement with its own probe is nearly linear in.
    /// </param>
    /// <param name="HeldSpeed">The speed the policy settled the coast at, or the request if it never acted.</param>
    /// <param name="HeldAt">
/// When it first acted <em>during the coast</em>, in seconds of flight. NaN if it never did — the
/// entry pulls the world to real time on every flight, so a hold from there on is not one.
/// </param>
    public readonly record struct WarpedFlight(double3 GroundFixed, double Seconds, double MeanStep,
                                               double HeldSpeed, double HeldAt);

    /// <summary>
    /// The round flown against the speed <see cref="WarpPolicy"/> actually settles on, driven by a
    /// stream of wall-clock frame times rather than by an assumed constant step.
    ///
    /// <para><see cref="FlyTheRoundAsWarped"/> assumes the coast runs at the requested warp for its
    /// whole length, which is the one thing about it that is not true: the policy only acts once a
    /// frame overruns, and it never gives the speed back while a round is in the air. So the step a
    /// shot is flown at depends on <em>whether</em> a frame overran and <em>when</em> — which is
    /// what this reproduces and that one cannot.</para>
    ///
    /// <para>The order is the mod's own: decide on the step just applied, then integrate across it
    /// clamped by what the round can survive (<c>Ksa/KSArmoryMod.cs</c>).</para>
    /// </summary>
    /// <param name="requestedWarp">The speed the scenario asks for once the salvo is away.</param>
    /// <param name="wallFrameSeconds">How long the next frame takes, given the flight time so far.</param>
    /// <param name="ground">Where the ground is. Null is the mean sphere.</param>
    public static WarpedFlight FlyTheRoundUnderTheWarpPolicy(double3 fromCci, double3 velocityCci,
                                                             double requestedWarp,
                                                             Func<double, double> wallFrameSeconds,
                                                             IGroundTest? ground = null,
                                                             MunitionProfile? warhead = null)
    {
        BallisticBody body = Earth;

        IGroundTest surface = ground ?? new Ball();

        Slug round = new(fromCci, velocityCci, null, 1, fromCci, Vec.Zero)
        {
            Munition = warhead ?? Warhead,
            Ground = surface,
        };

        WarpPolicy policy = new();
        double speed = requestedWarp;
        double elapsed = 0.0;
        double heldAt = double.NaN;
        double heldSpeed = requestedWarp;

        double coastSteps = 0.0;
        int coastFrames = 0;

        while (round.State == RoundState.Flying && elapsed < 20_000.0)
        {
            double dtSim = speed * wallFrameSeconds(elapsed);

            // The coast is what the frame reaches: once there is air the round asks for a step of
            // its own and the world is pulled to real time whatever it was doing, so a hold from
            // there on says nothing about the coast.
            bool coasting = round.FaithfulStepSeconds >= Warhead.PreferredStep;

            // The world's own speed is decided on the step just applied, before anything integrates
            // across it -- so a frame that overruns is flown at the old speed and only the next one
            // is slower.
            WarpDecision d = policy.Decide(dtSim, speed, true, true, round.FaithfulStepSeconds);
            if (d.Action is WarpAction.Slow or WarpAction.Restore)
            {
                if (d.Action == WarpAction.Slow && coasting && double.IsNaN(heldAt))
                {
                    heldAt = elapsed;
                    heldSpeed = d.Speed;
                }

                speed = d.Speed;
            }

            double step = Math.Min(dtSim, Warhead.MaxFaithfulStepSeconds);

            if (coasting)
            {
                coastSteps += step;
                coastFrames++;
            }

            elapsed = OneFrame(body, round, step, fromCci, Refresh.AsFlown, surface, elapsed);
        }

        (double3 landed, double seconds) = Arrived(body, round, elapsed);
        return new WarpedFlight(landed, seconds, coastSteps / Math.Max(1, coastFrames), heldSpeed, heldAt);
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
