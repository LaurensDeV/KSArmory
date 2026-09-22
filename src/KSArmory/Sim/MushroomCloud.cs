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
    /// stabilises in about five, so this is a compression of roughly seven times. Twice that much
    /// compression reads as the cloud shooting upward rather than rising.
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

    /// <summary>
    /// How much wider the cap is drawn than <see cref="CapRadius"/> makes it.
    ///
    /// <para><b>The law is narrower than the photographs.</b> <c>600·W^0.37</c> over
    /// <c>3000·∛W</c> is 0.38 as wide as it is tall at a third of a kilotonne and only 0.52 at a
    /// megatonne — a column, at every yield. Castle Bravo's cloud was about 100 km across against
    /// 40 km tall, which is wider than tall, and Ivy Mike's wider still. The fit and the pictures
    /// disagree, and the silhouette is the whole of what makes a mushroom read as one.</para>
    ///
    /// <para>So this is a drawing choice and is named as one, the same way <see cref="DrawnScale"/>
    /// is. <see cref="CapRadius"/> keeps saying what the law says, so anything measured against the
    /// law still means something.</para>
    /// </summary>
    public const double DrawnCapWidening = 1.9;

    /// <summary>And the cap radius as drawn.</summary>
    public static double DrawnCapRadius(double yieldKt)
        => CapRadius(yieldKt) * DrawnScale * DrawnCapWidening;

    /// <summary>
    /// How far downwind the cloud has sailed, as a multiple of its own drawn cap radius by the end
    /// of its life.
    ///
    /// <para><b>A drawing choice, and named as one</b> like <see cref="DrawnScale"/>. Wind aloft is
    /// twenty to forty metres a second and a real cloud rises for five minutes, so a proportionally
    /// honest drift is five or six kilometres — four cloud widths, which carries the column out of
    /// any frame that also holds the ground it burned. One cap radius puts the stem's foot just
    /// outside its own crater, which is the thing a photograph shows and the thing this exists
    /// for.</para>
    /// </summary>
    public const double DriftInCapRadii = 1.0;

    /// <summary>And how long it stands there before fading out.</summary>
    public const double StandSeconds = 40.0;

    /// <summary>Total life, after which there is nothing to draw.</summary>
    public const double LifeSeconds = RiseSeconds + StandSeconds;

    /// <summary>
    /// How far downwind the whole column has been carried (m).
    ///
    /// <para><b>Nothing until the rise is over</b>, because until then the stem is rooted: it is
    /// being fed from the ground, and what the wind does to a rooted column is tilt it, which is
    /// the lean and the veer the shader already draws. A cloud sails once it stops being fed —
    /// which is why an old photograph shows a cap far downwind with no stem under it at all.</para>
    /// </summary>
    public static double DriftMetres(double yieldKt, double age)
    {
        if (yieldKt <= 0.0 || age <= RiseSeconds || StandSeconds <= 0.0) return 0.0;

        double sailing = Math.Min(age, LifeSeconds) - RiseSeconds;

        return DrawnCapRadius(yieldKt) * DriftInCapRadii * (sailing / StandSeconds);
    }

    /// <summary>
    /// The yield KSA's own big bang is heard unaltered at.
    ///
    /// <para><b>An anchor, and a choice.</b> Nothing says what yield Core's <c>ExplosionBig</c> was
    /// authored for, so it is pinned to the one burst anybody has listened to it on — the B61's
    /// third of a kilotonne. The <em>ratio</em> between two yields is the law's; where the scale
    /// sits is this.</para>
    /// </summary>
    public const double BangAnchorKt = 0.3;

    /// <summary>
    /// The lowest a bang is pitched, which is where a sample stops being a sound.
    ///
    /// <para>At this the ten-second echo KSA's bang carries runs thirty, which is the rumble
    /// observers report from tens of kilometres; below it the crack turns to mud before the tail
    /// gets any longer worth hearing. The law reaches it at about seven kilotonnes, so the Mk 21's
    /// twenty sits on it.</para>
    /// </summary>
    public const double BangPitchFloor = 0.35;

    /// <summary>
    /// How a burst is heard against the bang KSA ships: slower and deeper by the same factor.
    ///
    /// <para><b>The cube root, like everything else about a burst.</b> A blast wave's duration
    /// scales with the linear size of the source, which is Hopkinson–Cranz again, so every time in
    /// the sound goes as <c>W^(1/3)</c> and playing it back at <c>W^(-1/3)</c> lengthens the crack,
    /// the report and the echo together. Eight times the yield is twice the rumble and an octave
    /// down — which is what makes a twenty-kilotonne warhead sound unlike a rocket going off, where
    /// before the two were the same file.</para>
    ///
    /// <para>Never above one: the sample is already a big explosion, and a smaller burst than the
    /// anchor played faster is a firework rather than a smaller bang.</para>
    /// </summary>
    public static double BangPitch(double yieldKt)
        => yieldKt <= 0.0 || !double.IsFinite(yieldKt)
               ? 1.0
               : Math.Clamp(Math.Cbrt(BangAnchorKt / yieldKt), BangPitchFloor, 1.0);

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
    /// The condensation cloud — the Wilson cloud — as a radius and how long it lasts.
    ///
    /// <para>The shock front leaves a rarefaction behind it, and the air it has just expanded cools
    /// below its own dew point: a white shell of droplets appears around the burst, engulfs the
    /// fireball, and then evaporates again as the pressure recovers. It is the reason a photograph
    /// of the first second looks like a white dome rather than a ball of fire.</para>
    ///
    /// <para><b>It needs humid air, so it belongs to the atmospheric branch alone</b> — there is
    /// nothing to condense on an airless body, which is also where this mod already forks.</para>
    ///
    /// <para>Sized in fireball radii and timed against the luminous phase rather than given laws of
    /// their own. The published scaling for it is thin and strongly dependent on humidity, and two
    /// more fitted constants here would be inventing precision — what is defensible is that the
    /// shell is a few fireball radii across and gone about as quickly as the flash.</para>
    /// </summary>
    public const double WilsonInFireballs = 3.4;

    /// <summary>How long that shell stands, as a multiple of the luminous phase.</summary>
    public const double WilsonOfFlash = 1.35;

    /// <summary>The condensation shell's radius, or zero for a charge too small to make one.</summary>
    public static double WilsonRadius(double chargeKg)
        => chargeKg < ThresholdKg
               ? 0.0
               : WilsonInFireballs * PeakFireballRadius(KilotonsFor(chargeKg));

    /// <summary>And how long it lasts before the pressure recovers and it evaporates.</summary>
    public static double WilsonSeconds(double chargeKg)
        => chargeKg < ThresholdKg
               ? 0.0
               : WilsonOfFlash * FlashSeconds(KilotonsFor(chargeKg));

    /// <summary>
    /// The widest the base surge gets, and how long it takes to get there.
    ///
    /// <para><b>Sampled off <see cref="SurgeRadius"/> rather than solved for.</b> That expression is
    /// a difference of two exponentials whose peak has a closed form nobody would recognise a year
    /// from now, and writing it down separately is the shape that drifts: change either rate and
    /// the peak silently stops being the peak. Two hundred samples over the rise, once per burst.
    /// </para>
    /// </summary>
    public static (double Radius, double AtAge) PeakSurge(double yieldKt)
    {
        double best = 0.0;
        double at = 0.0;

        for (int i = 1; i <= 200; i++)
        {
            double age = RiseSeconds * i / 200.0;
            double r = SurgeRadius(yieldKt, age);
            if (r <= best) continue;

            best = r;
            at = age;
        }

        return (best, at);
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

    private static double Smoothstep(double edge0, double edge1, double x)
    {
        if (!(edge1 > edge0)) return x >= edge1 ? 1.0 : 0.0;

        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);

        return t * t * (3.0 - (2.0 * t));
    }

    /// <summary>
    /// How long the second maximum of the thermal pulse is held out to, so it can be seen.
    ///
    /// <para><b>A drawing choice, and a floor rather than a multiplier.</b> At the B61's third of
    /// a kilotonne the whole double pulse is over in 25 ms — a frame and a half — so the one
    /// signature that separates a nuclear burst from a large explosion would be drawn and never
    /// witnessed. At a megatonne the second maximum falls at 0.87 s on its own and this does
    /// nothing at all, which is what makes it a floor: the law is left alone wherever the law is
    /// already legible.</para>
    /// </summary>
    public const double LegibleSecondPeak = 0.35;

    // Glasstone and Dolan, as powers of yield in kilotonnes: the minimum of the thermal pulse and
    // its second maximum. 10 ms and 157 ms at 20 kt; 52 ms and 871 ms at a megatonne.
    private const double PulseMinimumCoefficient = 0.0025;
    private const double PulsePeakCoefficient = 0.0417;
    private const double PulseExponent = 0.44;

    /// <summary>How much the pulse is slowed so it can be seen. One at high yields.</summary>
    public static double DrawnPulseStretch(double yieldKt)
    {
        if (yieldKt <= 0.0) return 1.0;

        double peak = PulsePeakCoefficient * Math.Pow(yieldKt, PulseExponent);

        return peak > 0.0 ? Math.Max(1.0, LegibleSecondPeak / peak) : 1.0;
    }

    /// <summary>
    /// When the shock front goes opaque to the radiation behind it, as drawn (s).
    ///
    /// <para><b>Not where the drawn curve is dimmest</b>, which is <see cref="PulseTroughSeconds"/>
    /// and is later: the first pulse is still dying here and the burn behind it has barely begun
    /// to show, so where the two cross is past both. The fireball is still there and still growing
    /// throughout — what happens is that for a moment one cannot see it.</para>
    /// </summary>
    public static double PulseMinimumSeconds(double yieldKt)
        => yieldKt <= 0.0
               ? 0.0
               : PulseMinimumCoefficient * Math.Pow(yieldKt, PulseExponent) * DrawnPulseStretch(yieldKt);

    /// <summary>
    /// When the drawn flash is actually at its dimmest (s) — the bottom of the double pulse.
    ///
    /// <para>Found by walking the curve rather than solved, because it is where a decaying
    /// exponential crosses a smoothstep and there is no closed form. Cheap and asked rarely: by
    /// the harness, to photograph the trough, and by the tests that assert there is one.</para>
    /// </summary>
    public static double PulseTroughSeconds(double chargeKg)
    {
        double peak = PulsePeakSeconds(KilotonsFor(chargeKg));
        if (!(peak > 0.0)) return 0.0;

        double dimmest = double.MaxValue;
        double at = 0.0;

        for (int i = 0; i <= TroughSamples; i++)
        {
            double age = peak * i / TroughSamples;
            double glow = FlashAt(chargeKg, age).Glow;

            if (glow >= dimmest) continue;

            dimmest = glow;
            at = age;
        }

        return at;
    }

    // Enough to land the trough within a frame at 60 fps over any drawn pulse.
    private const int TroughSamples = 256;

    /// <summary>
    /// When the second maximum falls, as drawn (s). The bigger of the two in everything but peak
    /// power: it lasts an order of magnitude longer and carries about 99% of the thermal energy,
    /// which is what actually burns and blinds at range.
    /// </summary>
    public static double PulsePeakSeconds(double yieldKt)
        => yieldKt <= 0.0
               ? 0.0
               : PulsePeakCoefficient * Math.Pow(yieldKt, PulseExponent) * DrawnPulseStretch(yieldKt);

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
    /// Brightness of the fireball at the instant of the burst, as a multiplier on the drawn colour.
    ///
    /// <para>It does not scale with yield, which is the surprising part: fireball surface
    /// temperature is much the same whatever the device, so only size and duration change. One ramp
    /// serves the whole dial.</para>
    /// </summary>
    public const double PeakGlow = 600.0;

    /// <summary>
    /// How fast the first pulse decays, in multiples of the time to the minimum.
    ///
    /// <para>Under one, so the pulse is well down before the minimum it is falling toward — which
    /// is what leaves a minimum there at all rather than a shoulder.</para>
    /// </summary>
    public const double PulseDecayInMinima = 0.45;

    /// <summary>
    /// What still gets out while the shock front is opaque, as a share of the second maximum.
    ///
    /// <para><b>The minimum is not the fireball going out.</b> The front is opaque to the radiation
    /// behind it and is itself radiating, just cooler — so a floor rather than a gap. Without one
    /// the drawn glow collapsed to about 5 against an ember floor of 40, which puts the ball under
    /// the bloom threshold and reverts it to being drawn as geometry for a fifth of a second.
    /// A fifth to a quarter of the second maximum is the shape Glasstone's curves have.</para>
    /// </summary>
    public const double ShockFrontShare = 0.25;

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
    public const double EmberFraction = 0.09;


    /// <summary>
    /// Seconds of that ember, which is the same for every yield because the rise is.
    ///
    /// <para><b>Bounded by how far the ball climbs while it is still lit.</b> The emissive sphere
    /// draws over the smoke rather than through it, so an ember that outlasts the lift-off is not a
    /// hot core glimpsed inside a cloud — it is a bright ball climbing in front of one, which reads
    /// as a flare going up rather than a fireball dying. At 0.22 it stayed lit through eight of its
    /// own radii of climb. <c>TheBallGoesDarkBeforeItClimbsOutOfItself</c> holds the limit.</para>
    /// </summary>
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
        // THE PULSE has a hard deadline of its own rather than a fraction of the luminous phase:
        // it is shock-front radiation, and it is over long before the ball is.
        double tMin = PulseMinimumSeconds(kt);
        double pulse = tMin > 0.0
                           ? PeakGlow * Math.Exp(-age / (tMin * PulseDecayInMinima))
                           : 0.0;

        // ...AND THE BURN IS HELD OUT UNTIL THE SHOCK FRONT LETS IT THROUGH, which is the whole of
        // the double flash. Between the pulse dying and this opening there is a real minimum -- the
        // front is opaque to the radiation behind it, so the fireball is still there, still
        // growing, and for a moment cannot be seen. Nothing else in nature does that, and a
        // bhangmeter identifies a nuclear test from orbit on this curve alone.
        //
        // It reaches one at the second maximum and the term under it is unchanged from there on,
        // so the burn and the ember below it are reached unchanged.
        double opening = ShockFrontShare
                         + ((1.0 - ShockFrontShare) * Smoothstep(tMin, PulsePeakSeconds(kt), age));

        double glow = age <= dark
                          ? Math.Max(pulse,
                                     BurnGlow * Math.Exp(-Math.Log(BurnGlow / EmberGlow) * t) * opening)
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

        double t = age / RiseSeconds;

        if (t >= 1.0)
        {
            // One overshoot and one settle. A bump that is zero at t = 1 and peaks a quarter of a
            // rise later, so it joins the track without a step: there is nothing to see at the
            // handover between the two halves of this function.
            double past = (t - 1.0) / OvershootAt;
            return 1.0 + (OvershootBy * past * Math.Exp(1.0 - past));
        }

        for (int i = 1; i < RiseAt.Length; i++)
        {
            if (t > RiseAt[i]) continue;

            double span = RiseAt[i] - RiseAt[i - 1];
            double into = span > 0.0 ? (t - RiseAt[i - 1]) / span : 0.0;

            return RiseTrack[i - 1] + (into * (RiseTrack[i] - RiseTrack[i - 1]));
        }

        return 1.0;
    }

    // Teapot Wasp's cloud top, tracked by theodolite (WT-1152, Project 9.4), normalised against its
    // own rise. The measurement rather than a fit, because no closed form matches both ends: every
    // one that starts like sqrt(t) arrives late, and every one that arrives on time starts too
    // fast. sqrt(t) itself is within a per cent at a tenth of the rise and 10 per cent low at
    // eight tenths.
    //
    // A step response accelerating from rest is what a buoyant parcel does and is not what was
    // measured at this scale: it reaches a twelfth of its climb where the real cloud is a third up,
    // then arrives early and sits at its ceiling from 0.6 onward.
    private static readonly double[] RiseAt = [0.0, 0.10, 0.30, 0.50, 0.60, 0.80, 1.00];
    private static readonly double[] RiseTrack = [0.0, 0.308, 0.509, 0.771, 0.840, 0.991, 1.000];

    // Ruth and Post were both tracked peaking and subsiding a few per cent, so the overshoot is
    // real and small -- not the tenth the old step response gave, which it then never came back
    // from.
    private const double OvershootBy = 0.04;
    private const double OvershootAt = 0.25;

    private static double3 Lerp(double3 a, double3 b, double t)
    {
        double f = Math.Clamp(t, 0.0, 1.0);
        return a + ((b - a) * f);
    }

    /// <summary>
    /// The stem's radius, as a fraction of the cap's.
    ///
    /// <para>A photographed mushroom's cap is five or six times the width of the column under it;
    /// at a half the cap is only twice the stem and the silhouette stops reading as a mushroom at
    /// all, which is the single strongest shape cue the drawing has.</para>
    /// </summary>
    public const double StemOfCap = 0.22;

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
        double underside = StemCeiling(capCentre, capRadius);
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
            StemRadius: capR * StemOfCap,
            SurgeRadius: SurgeRadius(kt, age),
            SurgeHeight: SurgeHeight(kt, age),
            Roll: roll,
            Fade: Fade(age));
    }

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

    /// <summary>
    /// How high the stem's head may reach: the cap's underside, so the column ends inside the cap
    /// rather than poking out of the top of it.
    ///
    /// <para>Exposed because the renderer needs the same number to know how far up its own column a
    /// pen has got, and a second copy drifts silently: one that omits the <see cref="Oblate"/>
    /// factor puts the ratio above one on every frame, which pins the stem's flare at its head
    /// value and looks like nothing in particular.</para>
    /// </summary>
    public static double StemCeiling(double capCentre, double capRadius)
        => capCentre - (capRadius * Oblate * 0.7);

    /// <summary>How far along its stroke a pen is at this age, in [0, 1].</summary>
    public static double Progress(double age) => Math.Clamp(age / RiseSeconds, 0.0, 1.0);

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
