using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The shape of a nuclear cloud over time, as offsets from the burst.
///
/// <para>Pure geometry: it says where the stem and the cap are at an age, and something else draws
/// them. Neither reachable renderer in KSA has drag, turbulence or a vortex field, so the toroidal
/// roll-up cannot emerge from a simulation and has to be choreographed. This is that
/// choreography.</para>
///
/// <para>Sizes are Glasstone and Dolan, <em>The Effects of Nuclear Weapons</em>; the clock is not.
/// A 0.3 kt cloud takes three and a half minutes to stabilise, which is unwatchable, so the rise is
/// compressed and the <em>ratios</em> are what carry the read. <c>docs/NUCLEAR-EFFECT.md</c> has
/// the laws and the reasoning.</para>
/// </summary>
public static class MushroomCloud
{
    /// <summary>Charge above which a burst grows a cloud at all, in kg of TNT equivalent.</summary>
    public const double ThresholdKg = 1000.0;

    /// <summary>
    /// Seconds the cloud takes to reach its ceiling. The real thing takes minutes: a 0.3 kt cloud
    /// stabilises in about five, so this is a compression of roughly seven times rather than the
    /// fourteen it began at, which is what stopped it reading as shooting upward.
    /// </summary>
    public const double RiseSeconds = 38.0;

    /// <summary>
    /// How large the cloud is <em>drawn</em>, against the size the laws give it.
    ///
    /// <para><b>This is a deliberate lie and the only one in this file.</b> Every dimension here is
    /// Glasstone's and checks out against the one measured low-yield surface burst to within three
    /// per cent, and it still reads as far too large for the burst that made it — because at these
    /// yields it genuinely is. A 0.3 kt fireball is 110 m across under a cap 770 m wide, a ratio of
    /// 1:7 that the test photographs agree with and that nobody watching a game believes.</para>
    ///
    /// <para>The alternative was enlarging the fireball, which is worse: the fireball is the one
    /// number a player can check against a photograph, and it has already been taken to the top of
    /// its own ±25% provenance spread. So the cloud is scaled instead, here, once, with a name — and
    /// <see cref="CloudTop"/> and <see cref="CapRadius"/> keep saying what the laws say, so the
    /// reference tests still mean something.</para>
    /// </summary>
    public const double DrawnScale = 0.65;

    /// <summary>Stabilised height of the cloud top as drawn, which is not what the law says.</summary>
    public static double DrawnCloudTop(double yieldKt) => CloudTop(yieldKt) * DrawnScale;

    /// <summary>And the cap radius as drawn.</summary>
    public static double DrawnCapRadius(double yieldKt) => CapRadius(yieldKt) * DrawnScale;

    /// <summary>And how long it stands there before fading out.</summary>
    public const double StandSeconds = 40.0;

    /// <summary>Total life, after which there is nothing to draw.</summary>
    public const double LifeSeconds = RiseSeconds + StandSeconds;

    /// <summary>Kilotons of TNT equivalent for a charge in kg, which is what a profile carries.</summary>
    public static double KilotonsFor(double chargeKg) => chargeKg / 1.0e6;

    /// <summary>
    /// Fireball radius (m) at its largest. Nuclear, so the 0.4 power rather than the cube root a
    /// chemical charge obeys — which is why this does not agree with
    /// <see cref="Warhead.FireballRadius"/>.
    ///
    /// <para><b>The constant has a ±25% provenance spread and this takes the upper end, on
    /// Glasstone's own authority rather than as a fudge.</b> §2.127 gives breakaway at
    /// <c>33.5 · W^0.4</c> m and says the maximum is about twice it, which is this. The commonly
    /// quoted <c>55 · W^0.4</c> is instead fitted to a single datum, §2.05's 5,700 ft diameter for
    /// 1 Mt; the 1962 edition gave 7,200 ft for the same burst, which is <c>70 · W^0.4</c> and is
    /// what FM 8-9 still quotes. All three are defensible and the spread is real, so the one chosen
    /// is the one that follows from a stated rule instead of from one measurement.</para>
    /// </summary>
    public static double FireballRadius(double yieldKt)
        => yieldKt <= 0.0 ? 0.0 : 67.0 * Math.Pow(yieldKt, 0.4);

    /// <summary>Stabilised height of the cloud top (m).</summary>
    public static double CloudTop(double yieldKt)
        => yieldKt <= 0.0 ? 0.0 : 3000.0 * Math.Cbrt(yieldKt);

    /// <summary>Stabilised cap radius (m).</summary>
    public static double CapRadius(double yieldKt)
        => yieldKt <= 0.0 ? 0.0 : 600.0 * Math.Pow(yieldKt, 0.37);

    /// <summary>
    /// How much bigger a fireball is for sitting on the ground.
    ///
    /// <para>The ground reflects the energy that would have gone downward straight back into the
    /// fireball, so the hemisphere above it grows as though the device were twice the size — and
    /// <see cref="FireballRadius"/> goes as the 0.4 power, hence this. Every burst this mod draws
    /// is at or near a surface, so it applies to all of them.</para>
    /// </summary>
    public static readonly double SurfaceBurstGain = Math.Pow(2.0, 0.4);

    // How far out the blast throws the dust, and how much of that the afterwind takes back, both as
    // fractions of the cap radius. They overlap, so the widest the skirt actually gets is neither of
    // them: about half a cap radius, a third of the way through the rise.
    private const double SurgeReach = 0.70;
    private const double SurgeDrawback = 0.35;

    /// <summary>
    /// Radius the ground skirt has reached (m): dust the blast drives outward along the ground, and
    /// then the afterwind draws back in.
    ///
    /// <para><b>It stays well inside the cap, and that is what separates a land burst from a water
    /// one.</b> The base surge everybody pictures, a dense wall running outward past the cloud's
    /// own width, belongs to an <em>underwater</em> burst, where the column of water falls back and
    /// the spray rolls out over the surface. Nothing on land does that: a surface burst's afterwinds
    /// blow <em>inward</em> along the ground to feed the stem, so dust thrown out by the blast is
    /// pulled back to the axis and lifted. The skirt is a collar round the base of the column, not a
    /// ring beyond the cap.</para>
    ///
    /// <para>Fast out and slow back, because the blast drives the one and nothing sustains it, while
    /// the inflow lasts as long as the column is rising.</para>
    /// </summary>
    public static double SurgeRadius(double yieldKt, double age)
    {
        if (yieldKt <= 0.0 || age <= 0.0) return 0.0;

        double outrush = 1.0 - Math.Exp(-age / (RiseSeconds * 0.10));
        double drawIn = 1.0 - Math.Exp(-age / (RiseSeconds * 0.55));

        return DrawnCapRadius(yieldKt) * ((SurgeReach * outrush) - (SurgeDrawback * drawIn));
    }

    /// <summary>
    /// And how high that collar stands. It keeps climbing while the ring comes back in, because the
    /// inflow drawing the dust inward is the same one lifting it into the stem. But it stays low,
    /// since it has no buoyancy of its own to climb on.
    /// </summary>
    public static double SurgeHeight(double yieldKt, double age)
        => yieldKt <= 0.0 || age <= 0.0
               ? 0.0
               : DrawnCloudTop(yieldKt) * 0.10 * (1.0 - Math.Exp(-age / (RiseSeconds * 0.45)));

    /// <summary>
    /// Whether the pens are laying yet. They are not while the fireball is luminous, because the
    /// volumetric pass draws <em>after</em> the bloom pass and therefore in front of it: smoke laid
    /// over a burning fireball buries the brightest thing the mod can draw inside its own exhaust.
    ///
    /// <para><b>The cloud's clock still starts at the burst, not here.</b> That is the whole
    /// difference between a fireball that rises and turns into a cloud and one that is followed by
    /// a separate cloud. Restarting the clock at the handover puts every pen back on the ground at
    /// the instant the flash dies, so what is drawn is a flash, and then — seconds later — columns
    /// of smoke climbing out of the ground several hundred metres away from where the burst was by
    /// then. Running one clock throughout means the pens are already up at the ball when they start
    /// laying, and the first smoke anybody sees is the ball's own.</para>
    /// </summary>
    public static bool SmokeStarted(double chargeKg, double age)
        => age > FlashSeconds(KilotonsFor(chargeKg));

    /// <summary>Seconds the fireball stays incandescent, after which it is lit smoke.</summary>
    public static double DarkAfter(double yieldKt)
        => yieldKt <= 0.0 ? 0.0 : 3.0 * Math.Pow(yieldKt, 0.4);

    /// <summary>
    /// And how long it is <em>drawn</em> glowing, which parts company with the law at the top of the
    /// dial.
    ///
    /// <para>The cloud's clock is compressed and the flash's is not, so they diverge as the yield
    /// climbs: 340 kt glows for 30.9 s against a 38 s rise, which is a burst still flaring while its
    /// own mushroom forms. Compressing the flash by the same factor is not the alternative — it
    /// works out at a blink nobody sees — so it runs real until it hits the ceiling below.</para>
    ///
    /// <para><b>And that ceiling is <see cref="ClimbUntil"/>, not a number of its own.</b> No smoke
    /// is laid while the ball is luminous, so a flash lasting longer than the pens take to climb
    /// means they are already out on the cap when they lay their first segment, and the column from
    /// the ground up is <em>never drawn at all</em> — the cap arrives disconnected from its own
    /// stem and base. At 0.30 of the rise that happened for everything above about 10 kt, and it is
    /// invisible at the bottom of the dial where the flash is short anyway.</para>
    /// </summary>
    public static double FlashSeconds(double yieldKt)
        => Math.Min(DarkAfter(yieldKt), RiseSeconds * ClimbUntil * 0.6);

    /// <summary>
    /// The cloud at an age, in a frame whose <paramref name="up"/> is the local vertical.
    ///
    /// <para><paramref name="east"/> and <paramref name="north"/> only have to be perpendicular to
    /// up and to each other; which way they actually point does not matter to a shape with an axis
    /// of symmetry, and the caller is spared having to find true north.</para>
    /// </summary>
    public readonly record struct Shape(
        double CapCentre, double CapRadius, double CapTube,
        double StemTop, double StemRadius, double SurgeRadius, double SurgeHeight,
        double Roll, double Fade)
    {
        /// <summary>Nothing left to draw.</summary>
        public bool Spent => Fade <= 0.0;
    }

    /// <summary>
    /// The fireball at an age: how big, what colour, and how hard it is glowing.
    ///
    /// <para><see cref="Glow"/> is a multiplier on the drawn brightness rather than a colour, and it
    /// runs far past one on purpose: it is what carries the burst over the threshold where the
    /// engine's bloom will keep it.</para>
    /// </summary>
    public readonly record struct Flash(double Radius, double3 Colour, double Glow)
    {
        /// <summary>Nothing left to draw.</summary>
        public bool Spent => Glow <= 0.0 || Radius <= 0.0;
    }

    /// <summary>
    /// The fireball for a charge in kg, at an age.
    ///
    /// <para>Its brightness does not scale with yield, which is the surprising part: the surface
    /// temperature of a fireball is much the same whatever the device, so only its size and how
    /// long it lasts change. One ramp therefore serves every setting.</para>
    ///
    /// <para>The colour walks the real progression rather than fading an orange ball out —
    /// blue-white at six or seven thousand kelvin, through yellow and orange into deep red as it
    /// cools, which is the handover to a cloud that is lit rather than glowing.</para>
    /// </summary>
    /// <summary>
    /// Brightness of the fireball at the instant of the burst, as a multiplier on the drawn colour.
    ///
    /// <para>It does not scale with yield, which is the surprising part: fireball surface
    /// temperature is much the same whatever the device, so only size and duration change. One ramp
    /// serves the whole dial.</para>
    /// </summary>
    public const double PeakGlow = 600.0;

    /// <summary>The fireball at its largest, which is what it spends most of the flash at.</summary>
    public static double PeakFireballRadius(double yieldKt)
        => FireballRadius(yieldKt) * SurfaceBurstGain;

    /// <summary>
    /// How long the ball goes on glowing after the luminous phase, as a fraction of the <em>rise</em>
    /// rather than of the flash.
    ///
    /// <para>Measured against the rise on purpose, and it is the same departure
    /// <see cref="DrawnScale"/> is. The luminous phase is real time and the rise is compressed
    /// eightfold, so a fireball that goes dark on its own clock is out before the cloud has done one
    /// part in four hundred of its climb, and what anybody sees is a flash that ends and then a
    /// cloud. Held against the rise instead, the ball is still there — dull, dimming, and
    /// <b>climbing on the same curve the pens do</b> — while the cloud forms around and over it,
    /// which is the fireball becoming the cloud rather than being replaced by one.</para>
    ///
    /// <para>It is an ember, not a second flash. The glow at the end of the luminous phase is
    /// already an order of magnitude under the bloom threshold, so nothing here flares; it is a hot
    /// core showing through the erosion gaps in its own smoke until the cloud swallows it, which is
    /// what Glasstone means by the toroid being "soon hidden by the radioactive cloud and
    /// debris".</para>
    /// </summary>
    public const double EmberFraction = 0.22;

    /// <summary>Seconds of that ember, which is the same for every yield because the rise is.</summary>
    public static double EmberSeconds => RiseSeconds * EmberFraction;

    /// <summary>
    /// Glow the ember holds, and the value it is cut at.
    ///
    /// <para><b>Both sit above the bloom threshold, and that is the whole point.</b> An emissive
    /// sphere clears that threshold and the bloom pass spreads it into glare, so what anybody sees
    /// is light with no discernible edge; under it the pass discards the pixel and the same sphere
    /// is drawn as ordinary shaded geometry — which is to say, as a ball. That is why the flash has
    /// never looked like one and a dim ember immediately did. Brightness here is not a preference,
    /// it is the difference between drawing light and drawing a mesh.</para>
    ///
    /// <para>The threshold is about 24 for the deep red the ember cools to, so these are 1.7 and 1.1
    /// times it: bright enough to stop being geometry, twenty-five times under the flash itself, and
    /// nowhere near a second flash.</para>
    /// </summary>
    public const double EmberGlow = 40.0;

    /// <inheritdoc cref="EmberGlow"/>
    public const double EmberFloor = 26.0;

    /// <summary>
    /// Glow the ball settles to once the thermal pulse is over, and burns at until it is an ember.
    /// Chosen to meet <see cref="EmberGlow"/> exactly at the end of the luminous phase, so the three
    /// stages join without a step.
    /// </summary>
    public const double BurnGlow = 200.0;

    /// <summary>
    /// How far the ball contracts across the luminous phase, before the ember shrink takes over.
    /// Gentle: it is the incandescent region cooling inward, not the fireball getting smaller.
    /// </summary>
    public const double LuminousShrink = 0.25;

    /// <summary>
    /// How far the ball shrinks over the ember, as a fraction of its own radius.
    ///
    /// <para>It recedes into the cloud rather than fading where it stands. The smoke is growing
    /// around it the whole time, so a ball that keeps its size stays proud of its own cloud and
    /// reads as an object sitting in it; one that shrinks is swallowed, which is what Glasstone
    /// means by the toroid being soon hidden by the cloud and debris. It also means the cut at the
    /// end removes something small and faint instead of something ball-sized.</para>
    /// </summary>
    public const double EmberShrink = 0.85;

    public static Flash FlashAt(double chargeKg, double age)
    {
        double kt = KilotonsFor(chargeKg);
        if (kt <= 0.0 || age < 0.0) return default;

        double dark = FlashSeconds(kt);
        if (age >= dark + EmberSeconds) return default;

        double t = Math.Min(1.0, age / dark);
        double ember = age <= dark ? 0.0 : (age - dark) / EmberSeconds;

        // Full size in a tenth of the luminous phase, then contracting: gently while it burns, hard
        // once it is an ember. The ramp on the way up is only so the ball does not appear at full
        // size in one frame -- the real expansion is over before anyone can resolve it.
        //
        // <b>What contracts is the incandescent region, not the fireball.</b> The hot air mass keeps
        // growing the whole time; its outer skin cools below visible emission first, so the part of
        // it that glows shrinks inward while the part of it that exists does not. That is why the
        // ball can shrink without contradicting a law that says a fireball only ever grows, and it
        // is what lets it recede into its own smoke instead of being switched off inside it.
        double radius = FireballRadius(kt) * SurfaceBurstGain
                        * Math.Min(1.0, 0.60 + (0.40 * Math.Sqrt(t / 0.10)))
                        * (1.0 - (LuminousShrink * t))
                        * (1.0 - (EmberShrink * ember));

        // Blue-white, then yellow, then orange, then deep red.
        double3 colour = t < 0.35
                             ? Lerp(new double3(1.0, 0.97, 0.92), new double3(1.0, 0.78, 0.35), t / 0.35)
                             : Lerp(new double3(1.0, 0.78, 0.35), new double3(0.75, 0.16, 0.05),
                                    (t - 0.35) / 0.65);

        // Three stages, each taking over from the one before by being the brightest of them.
        //
        // The PULSE is enormous and off a cliff: the thermal pulse is essentially over in a tenth of
        // the time the ball stays visible, and a linear fade there reads as a lamp turned down. Its
        // peak is anchored rather than chosen -- a 6,000-7,000 K blackbody radiates sigma*T^4 =
        // 1.4e8 W/m^2 against the sun's photosphere at 6.3e7, so a fireball is twice as bright as
        // the surface of the sun, which is why looking at one blinds people miles away.
        //
        // The BURN is what the cliff lands on, and without it the ball is dark within a fifth of the
        // time it is drawn: a flash, and then a long dim nothing. It is still plainly a fireball,
        // rising and contracting, and it is the stage that makes the burst read as burning rather
        // than as having gone off.
        //
        // The EMBER is the floor under both, and stays over the bloom threshold so the ball never
        // reverts to being drawn as geometry. See EmberGlow.
        // Each stage runs in its own phase rather than all three competing: t clamps at 1, so a burn
        // term left in the maximum past the luminous phase holds its final value forever and the
        // ember can never darken under it. They join without a step because the burn is sized to
        // arrive at exactly EmberGlow when t reaches 1.
        double glow = age <= dark
                          ? Math.Max(PeakGlow * Math.Exp(-4.5 * t),
                                     BurnGlow * Math.Exp(-Math.Log(BurnGlow / EmberGlow) * t))
                          : EmberGlow + ((EmberFloor - EmberGlow) * ember);

        return new Flash(radius, colour, glow);
    }

    /// <summary>
    /// How far up the cloud is, as a fraction of its ceiling, at an age.
    ///
    /// <para>Overshoots by about a tenth and settles back, the way a thermal does in a stratified
    /// atmosphere. The real cloud completes roughly a third of one buoyancy oscillation before it
    /// stabilises, so one overshoot and one settle is the whole of it -- more would ring.</para>
    /// </summary>
    public static double Rise(double age)
    {
        if (age <= 0.0) return 0.0;

        const double Damping = 0.60;

        double peak = Math.PI / (0.85 * RiseSeconds);
        double natural = peak / Math.Sqrt(1.0 - (Damping * Damping));
        double decay = Math.Exp(-Damping * natural * age);

        return 1.0 - (decay * (Math.Cos(peak * age)
                               + (Damping * natural / peak * Math.Sin(peak * age))));
    }

    private static double3 Lerp(double3 a, double3 b, double t)
    {
        double f = Math.Clamp(t, 0.0, 1.0);
        return a + ((b - a) * f);
    }

    /// <summary>Where the cloud is at <paramref name="age"/> seconds, for a charge in kg.</summary>
    public static Shape At(double chargeKg, double age)
    {
        double kt = KilotonsFor(chargeKg);
        if (kt <= 0.0 || age < 0.0 || age >= LifeSeconds) return default;

        double top = DrawnCloudTop(kt);
        double capR = DrawnCapRadius(kt);

        // Underdamped, not a lag. A buoyant parcel accelerates while the density difference drives
        // it, decelerates as entrainment kills that difference, overshoots its neutral level and
        // settles back -- a second-order step response, not a first-order one. The difference is
        // visible: a lag leaves at maximum speed and never overshoots, which reads as a lift on a
        // rope rather than as something thrown up by a detonation.
        //
        // It also keeps the cloud moving well past the rise, which is most of the answer to
        // everything stopping at once.
        double rise = Rise(age);

        // The cap centre sits at three quarters of the top, because the cap has thickness: its base
        // is at half the cloud top and its crown is the top itself.
        double capCentre = top * 0.75 * rise;

        // The cap widens as it rises, and is done widening before a pen reaches the widest point of
        // its own stroke -- which is the whole of it, because a pen crosses the equator once and
        // then tucks under, so the width it finds there is the width the cap keeps. Widening after
        // that is drawn by nothing: it moves the silhouette the pens have already passed. Spread out
        // over the full rise it left the cap 19% narrower than every other number here says it is.
        double spread = 0.55 + (0.57 * Math.Min(1.0, age / (RiseSeconds * SpreadBy)));
        double capRadius = capR * spread;

        // The stem's top is the cap's underside, always, and it is never anywhere else.
        //
        // A stem is dirt the afterwinds lift, so the temptation is to raise it on its own clock and
        // let it lag the cap. That is the air-burst picture: for a burst high enough that its
        // fireball never touches the ground, a dust column really does climb separately and join the
        // cloud later. A surface burst has no such moment -- the dust is already inside the fireball
        // when it lifts, so the column is continuous from the first instant and the only thing that
        // develops is how clearly it reads as narrower than the cap.
        //
        // Drawn on its own clock instead, it is a free-standing column with clear air above it and a
        // tip climbing toward an empty sky, which is the named tell of an amateur mushroom.
        double underside = capCentre - (capRadius * Oblate * 0.7);
        double climb = (capCentre + (capRadius * Oblate))
                       * Math.Sqrt(Math.Min(1.0, Progress(age) / ClimbUntil));
        double stemTop = Math.Max(0.0, Math.Min(climb, underside));

        // A slight twist and no more. The emitters drawing this are pens that keep everywhere they
        // have been, so a roll of any size draws a helix rather than a rolling cap -- eight of them
        // being a spiral staircase. The rollover has to come from the *path* shape below.
        double roll = 0.30 * (1.0 - Math.Exp(-2.0 * age / RiseSeconds));

        return new Shape(
            CapCentre: capCentre,
            CapRadius: capRadius,
            CapTube: capR * 0.45,
            StemTop: stemTop,
            StemRadius: capR * 0.5,
            SurgeRadius: SurgeRadius(kt, age),
            SurgeHeight: SurgeHeight(kt, age),
            Roll: roll,
            Fade: Fade(age));
    }

    /// <summary>
    /// Where one of <paramref name="count"/> cap emitters is, at <paramref name="progress"/> along
    /// its stroke.
    ///
    /// <para><b>These are pens, not points.</b> What draws them keeps every position they have held
    /// for twenty minutes, so this is the shape of a <em>stroke</em> and the cloud is the surface
    /// those strokes sweep. Each one climbs the axis, flares outward at the top and curls under —
    /// a meridian of the cap — and the ring of them is a surface of revolution.</para>
    ///
    /// <para>Which is also the vortex ring's own circulation: up the middle, out over the top, down
    /// and tucked under at the rim. Drawing the path the smoke would take is what produces the
    /// rollover, since neither renderer has a vortex field to produce it for us.</para>
    ///
    /// <para><paramref name="shell"/> scales the flare, so a ring inside the rim fills the dome the
    /// outer ring leaves hollow. It costs nothing to draw: overlapping smoke takes the deeper of
    /// the two rather than adding them, so an inner ring cannot make the cloud denser than one
    /// capsule already is.</para>
    /// </summary>
    /// <summary>
    /// How far up its stroke a pen is still climbing the axis. Past this it is walking the cap, so
    /// it is also where the cap's own shape starts being measurable — and the deadline the flash has
    /// to end by, see <see cref="FlashSeconds"/>.
    /// </summary>
    public const double ClimbUntil = 0.15;

    /// <summary>
    /// How much of the rise the cap takes to reach its full width, as a fraction. Short of the
    /// 0.63 at which a pen crosses the equator, because that is the one instant the cap's width is
    /// decided.
    /// </summary>
    public const double SpreadBy = 0.50;

    // The cap is taller than it is wide, which is the opposite of the anvil everyone pictures and
    // is what Glasstone's own two numbers say at these yields: a base at half the cloud top and a
    // crown at the cloud top is 1004 m of cap over a 769 m width for a 0.3 kt burst. Drawn round
    // instead, it reads as a lampshade -- flat on top, widest along its lower edge.
    private const double Oblate = 1.15;

    // And how far past the equator the lip carries on before the stroke ends, which is the overhang.
    private static readonly double Tuck = 60.0 * Math.PI / 180.0;

    /// <summary>How far the cap's radius wanders with bearing, as a fraction of it.</summary>
    public const double CapLobeDepth = 0.25;

    /// <summary>And the skirt's, which has more room because its pens sit closer together.</summary>
    public const double SkirtLobeDepth = 0.20;

    /// <summary>
    /// A bearing-dependent scale in <c>[1 − depth, 1]</c>, for pulling a ring out of round.
    ///
    /// <para>Nothing in a cloud is a surface of revolution, and the eye knows it without being able
    /// to say why: a shape that is *exactly* the same from every bearing is the tell that separates
    /// something generated from something photographed. Two harmonics rather than one, because a
    /// single one draws a trefoil and reads as deliberate.</para>
    ///
    /// <para>It only ever pulls <em>in</em>, so the cap radius stays the bound it is everywhere else
    /// rather than becoming an average. And <see cref="CapLobeDepth"/> is limited by coverage rather
    /// than by taste: a lobe separates neighbouring pens radially on top of the pitch already
    /// between them, and past what the tube can bridge the ring opens and the cap reads as ropes.
    /// <c>ALobedRingStillCloses</c> is where that limit is held.</para>
    /// </summary>
    public static double Lobe(double turn, double depth)
    {
        double wave = (0.6 * Math.Cos(3.0 * turn)) + (0.4 * Math.Sin((5.0 * turn) + 1.1));

        return 1.0 - (depth * (1.0 - wave) * 0.5);
    }

    public static double3 CapPoint(in Shape shape, int index, int count, double progress,
                                   double shell, double3 up, double3 east, double3 north)
    {
        if (count <= 0) return Vec.Zero;

        double p = Math.Clamp(progress, 0.0, 1.0);
        double radius = shape.CapRadius * shell;

        // Up the axis, over the top, down the outside, and tucked under. That is the vortex ring's
        // own circulation, and walking it is what fills a *volume* with one stroke: the cap is the
        // body of revolution the meridian sweeps, not a plate at one height.
        //
        // The body is a spheroid, which is Glasstone's cap rather than a choice. A cap whose base
        // is half the cloud top and whose crown is the cloud top, at the cap radius, is within a
        // few percent of a sphere of that radius centred at three quarters of the top -- so the
        // shape follows from the two numbers already in the table.
        double climb = AxisHeight(shape, p, shell);

        // From the pole round to past the equator. Stopping at the equator leaves a cap with a flat
        // underside; carrying on brings the lip back in under it, which is the overhang the whole
        // silhouette rests on.
        double phi = (Math.PI / 2.0) - (Smooth(p, ClimbUntil, 1.0) * ((Math.PI / 2.0) + Tuck));

        double turn = (2.0 * Math.PI * index / count) + (shape.Roll * p);

        double out2 = radius * Math.Cos(phi) * Lobe(turn, CapLobeDepth);
        double swept = shape.CapCentre + (radius * Oblate * Math.Sin(phi));

        return (up * Math.Min(climb, swept))
               + (east * (Math.Cos(turn) * out2))
               + (north * (Math.Sin(turn) * out2));
    }

    // Hermite ease between two thresholds, zero below and one above.
    private static double Smooth(double x, double from, double to)
    {
        if (to <= from) return x >= to ? 1.0 : 0.0;

        double t = Math.Clamp((x - from) / (to - from), 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }

    /// <summary>Where the stem's head sits, as an offset from the burst.</summary>
    public static double3 StemPoint(in Shape shape, double3 up) => up * shape.StemTop;

    /// <summary>How far along its stroke a pen is at this age, in [0, 1].</summary>
    public static double Progress(double age) => Math.Clamp(age / RiseSeconds, 0.0, 1.0);

    /// <summary>
    /// How high up the axis a stroke has climbed, before it starts walking the cap.
    ///
    /// <para><b>This is also where the fireball is.</b> The ball rises and the pens ride it up, which
    /// is the difference between a burst that turns into a cloud and one that is replaced by a
    /// separate cloud growing out of the ground beneath it. A fireball pinned to the burst point
    /// cannot become anything: it can only go out and let something else take over.</para>
    /// </summary>
    /// <remarks>
    /// The square root is the classical thermal result, <c>z ∝ √t</c>, and it matters at exactly one
    /// instant: the flash runs on real time while the cloud's clock is compressed, so a climb that
    /// is linear in progress has barely left the ground by the time the smoke takes over, and the
    /// slower the rise is made the worse that gets. Rising fast and then easing also *is* what a
    /// buoyant thermal does — the measured cloud-top tracks go as √t, not as a ramp.
    /// </remarks>
    public static double AxisHeight(in Shape shape, double progress, double shell)
        => (shape.CapCentre + (shape.CapRadius * shell * Oblate))
           * Math.Sqrt(Math.Min(1.0, Math.Clamp(progress, 0.0, 1.0) / ClimbUntil));

    /// <summary>
    /// The circle the outer pens walk, which is inside the silhouette by their own tube radius.
    ///
    /// <para>Smoke is drawn as a capsule about the path, so a pen walking the cap radius puts the
    /// visible edge a whole tube outside it and the cloud comes out half again too wide.</para>
    /// </summary>
    public static double PathRadius(in Shape shape, double tubeRadius)
        => Math.Max(shape.CapRadius * 0.25, shape.CapRadius - tubeRadius);

    /// <summary>
    /// Spacing between neighbouring pens on a ring of <paramref name="count"/>, at the cap's full
    /// width. What makes a ring of tubes read as one surface is this against the tube radius: the
    /// solid core of a capsule reaches 0.55 of its radius, so pens further apart than 1.1 radii
    /// leave gaps between them and the cloud reads as ropes.
    /// </summary>
    public static double RingPitch(in Shape shape, int count)
        => count <= 0 ? 0.0 : 2.0 * Math.PI * shape.CapRadius / count;

    /// <summary>
    /// Tube radius the pens are laid at when the smoke takes over, which is simply the fireball's,
    /// so that what appears is the fireball it is taking over from.
    ///
    /// <para><b>This is what makes the cloud grow out of the explosion rather than replace it.</b>
    /// Every pen starts on the burst point, and at the cap's own tube what arrives there in a single
    /// frame is 224 m across against a 90 m fireball. That does not read as an explosion becoming a
    /// cloud; it reads as a small flash, and then a large cloud, and the cloud is judged against the
    /// flash and found far too big for it.</para>
    ///
    /// <para><b>One radius, not one divided by anything.</b> The pens are coincident on the axis
    /// while they climb, and the raymarcher takes the deeper of two overlapping capsules rather than
    /// summing them, so any number of coincident tubes of radius <c>r</c> render as one tube of
    /// radius <c>r</c>. Dividing by <c>cbrt(count)</c> — as would be right if the engine were
    /// merging them into a single fat ball — lays a 14 m thread where a 45 m ball belongs, and a
    /// thread that climbs is a pillar rising out of the ground. The engine's merge does exist, and
    /// never runs on these pens: <c>docs/NUCLEAR-EFFECT.md</c> has the frame ordering.</para>
    /// </summary>
    public static double TubeAtHandover(double yieldKt) => PeakFireballRadius(yieldKt);

    /// <summary>
    /// How far the pens have swollen from that toward their full width, at
    /// <paramref name="progress"/> along the stroke.
    ///
    /// <para>Full <em>before</em> they begin to spread, because the coverage rule keeping the rim
    /// closed is written against the full tube and a thin pen out on the rim is a rope. What it buys
    /// on the way there is a column thin at its base and fat at its head, which is the silhouette
    /// wanted anyway.</para>
    /// </summary>
    public static double TubeGrowth(double progress) => Smooth(progress, 0.0, 0.40);

    /// <summary>
    /// Pens on the skirt's ring. It lives beside <see cref="SurgeTube"/> because the tube is derived
    /// from it: split across a boundary, the two drift and the collar opens into spokes.
    /// </summary>
    public const int SurgeStrands = 18;

    // How fat the collar is while its ring is still too small for the pitch to decide, as a
    // fraction of the cap's own tube.
    private const double SurgeFloorTube = 0.45;

    /// <summary>
    /// Tube radius the skirt's pens need to close into a surface at the radius the ring has
    /// reached.
    ///
    /// <para>The pitch grows with the ring, so a tube chosen for the settled collar opens into
    /// spokes on the way out. The floor is what gives it body while the ring is small, and it is
    /// deliberately narrow enough at the start that the pens are still merged: the skirt begins as
    /// one ball of dust sitting on the burst and blooms outward into a ring, which is the order the
    /// real thing happens in.</para>
    ///
    /// <para>That floor grows in from <paramref name="handoverTube"/> for the same reason the cap's
    /// does — see <see cref="TubeAtHandover"/>. A skirt at its full floor on the first frame is a
    /// 205 m ball of dust arriving where a 45 m fireball just was, which is the same discontinuity
    /// in miniature.</para>
    /// </summary>
    public static double SurgeTube(in Shape shape, double progress, double handoverTube)
    {
        double floor = handoverTube
                       + (((shape.CapTube * SurgeFloorTube) - handoverTube) * TubeGrowth(progress));

        return Math.Max(floor, 2.0 * Math.PI * shape.SurgeRadius / SurgeStrands);
    }

    // Full while it rises and stands, then out. Squared so it thins slowly at first and then goes,
    // which is how a cloud disperses rather than how a light switches off.
    private static double Fade(double age)
    {
        if (age <= RiseSeconds + (StandSeconds * 0.5)) return 1.0;

        double t = (age - RiseSeconds - (StandSeconds * 0.5)) / (StandSeconds * 0.5);
        double left = 1.0 - Math.Clamp(t, 0.0, 1.0);
        return left * left;
    }
}
