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
    /// stabilises in about five, so this is a compression of about five times. Players read
    /// anything much faster as the cloud shooting upward rather than rising.
    /// </summary>
    public const double RiseSeconds = 57.0;

    /// <summary>
    /// How large the cloud is <em>drawn</em>, against the size the laws give it: at one, the laws as
    /// they are.
    ///
    /// <para>Every dimension here is Glasstone's and checks out against the one measured low-yield
    /// surface burst to within three per cent. A 0.3 kt fireball is 110 m across under a cap 770 m
    /// wide, 1:7, which the test photographs agree with. A named factor rather than none so the
    /// drawing can depart from the laws in one place, while <see cref="CloudTop"/> and
    /// <see cref="CapRadius"/> keep saying what the laws say.</para>
    /// </summary>
    public const double DrawnScale = 1.0;

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

    /// <summary>
    /// And at Tsar Bomba's 50 Mt: narrower than Bravo's widening makes it, because a cloud that
    /// punches further into the stratosphere spreads less. See <see cref="TsarPenetration"/>.
    /// </summary>
    public const double TsarCapWidening = 0.6;

    /// <summary>The widening for a yield, between the two shots as <see cref="PenetrationFor"/> is.</summary>
    public static double CapWideningFor(double yieldKt)
        => DrawnCapWidening + ((TsarCapWidening - DrawnCapWidening) * PastBravo(yieldKt));

    // How far from Bravo's 15 Mt to Tsar's 50 a yield is, log-linear and clamped at both ends.
    private static double PastBravo(double yieldKt)
        => Math.Clamp(Math.Log(Math.Max(yieldKt, 1e-9) / 15_000.0) / Math.Log(50_000.0 / 15_000.0), 0.0, 1.0);

    /// <summary>And the cap radius as drawn.</summary>
    public static double DrawnCapRadius(double yieldKt)
        => CapRadius(yieldKt) * DrawnScale * CapWideningFor(yieldKt);

    /// <summary>
    /// The tropopause, at the law's scale: the standard atmosphere's 11 km. Earth's, the way the
    /// bang's speed of sound is, because this mod carries no temperature profile for any body.
    /// </summary>
    public const double TropopauseMetres = 11000.0;

    /// <summary>
    /// How much of the rise the law asks for past the tropopause a cloud actually makes.
    ///
    /// <para>The stratosphere is stable, so a cloud reaching it is braked and spreads sideways
    /// instead: the anvil. Fitted to Castle Bravo, whose cloud topped out near 40 km under a 16.5 km
    /// tropical tropopause where <see cref="CloudTop"/> asks 74 km. The rest of the cap's volume goes
    /// into its width, which is what makes a yield readable from the silhouette.</para>
    /// </summary>
    public const double StratospherePenetration = 0.41;

    /// <summary>
    /// And at the top of the dial: ten minutes after Tsar Bomba its cloud stood 64 to 67 km high and
    /// about 95 km across, which Bravo's share alone draws at 52 km. The largest clouds punch further
    /// into the stratosphere than one share describes. Chosen, with <see cref="TsarCapWidening"/>, so
    /// the cloud as drawn is that tall and that wide at that age.
    /// </summary>
    public const double TsarPenetration = 0.65;

    /// <summary>
    /// The share for a yield: Bravo's up to its 15 Mt, Tsar's from its 50, and log-linear between.
    /// Two measured clouds and nothing else, so nothing past either end is extrapolated.
    /// </summary>
    public static double PenetrationFor(double yieldKt)
        => StratospherePenetration + ((TsarPenetration - StratospherePenetration) * PastBravo(yieldKt));

    // The width of the bend onto that slope, as a share of the tropopause, so the cap does not
    // visibly kink as it passes through.
    private const double TropopauseKnee = 0.15;

    /// <summary>
    /// A height the laws give, bent to what a stratified atmosphere lets the cloud reach. Unchanged
    /// under the drawn tropopause, and past it the slope eases from one onto
    /// <see cref="PenetrationFor"/> the yield.
    /// </summary>
    public static double Stratified(double drawnHeight, double yieldKt = 0.0)
    {
        double tropopause = TropopauseMetres * DrawnScale;
        double over = drawnHeight - tropopause;
        if (over <= 0.0) return drawnHeight;

        double knee = TropopauseKnee * tropopause;
        double share = PenetrationFor(yieldKt);
        return tropopause + (share * over)
               + ((1.0 - share) * knee * (1.0 - Math.Exp(-over / knee)));
    }

    /// <summary>
    /// How much thinner the cap is than the law draws it, for a cloud whose top the law puts at
    /// <paramref name="drawnTop"/>: one under the tropopause, less past it. The cap's base is at
    /// half the top and its crown at the top, so this is how much of that span survives the bend.
    /// </summary>
    public static double CapSquash(double drawnTop, double yieldKt = 0.0)
    {
        if (drawnTop <= 0.0) return 1.0;

        double lawSpan = drawnTop * 0.5;
        return Math.Clamp((Stratified(drawnTop, yieldKt) - Stratified(lawSpan, yieldKt)) / lawSpan, 0.05, 1.0);
    }

    /// <summary>How high the drawn cloud stands once it has stopped rising, tropopause and all.</summary>
    public static double DrawnStandingTop(double yieldKt) => Stratified(DrawnCloudTop(yieldKt), yieldKt);

    /// <summary>
    /// The highest the drawn crown gets over the cloud's life (m), from the ground under the burst:
    /// what a player sees, where <see cref="DrawnStandingTop"/> is the law it is built from. The two
    /// part by a few kilometres in the stratosphere, where the cap is a thin anvil round its centre.
    /// </summary>
    public static double TallestDrawn(double yieldKt, double burstHeight = 0.0, double airRatio = 1.0)
    {
        if (_tallest.Kt == yieldKt && _tallest.Height == burstHeight && _tallest.Air == airRatio) return _tallest.Metres;

        double life = LifeFor(yieldKt);
        double tallest = 0.0;

        for (int i = 1; i <= 24; i++)
        {
            Shape shape = At(yieldKt * 1.0e6, life * i / 25.0, burstHeight, airRatio);
            tallest = Math.Max(tallest, shape.CapCentre + (CrownInTubes * shape.CapTube));
        }

        _tallest = (yieldKt, burstHeight, airRatio, tallest);
        return tallest;
    }

    // The panel asks every frame for the dial it has not moved.
    [ThreadStatic]
    private static (double Kt, double Height, double Air, double Metres) _tallest;

    /// <summary>And how wide its cap is then, anvil and all.</summary>
    public static double DrawnCapAcross(double yieldKt)
        => 2.0 * DrawnCapRadius(yieldKt) / Math.Sqrt(CapSquash(DrawnCloudTop(yieldKt), yieldKt));

    /// <summary>
    /// The height of burst above which a burst leaves no significant local fallout (m): 180·W^0.4
    /// feet (Glasstone and Dolan). Below it the fireball reaches the ground and draws it up into the
    /// cloud; above it the cloud is the bomb's own debris and air. It sits at 0.82 of
    /// <see cref="FireballRadius"/>, so it is also where the ball stops touching the ground.
    /// </summary>
    public static double FalloutSafeHeight(double yieldKt)
        => yieldKt <= 0.0 ? 0.0 : 54.9 * Math.Pow(yieldKt, 0.4);

    /// <summary>
    /// How much of a surface burst this is, in [0, 1], from its height above the ground: one on the
    /// ground and up to half <see cref="FalloutSafeHeight"/>, none from all of it. What decides
    /// the dirt in the cloud, the fallout under it, and how much the ground adds to the fireball.
    /// </summary>
    public static double GroundCoupling(double yieldKt, double burstHeight)
    {
        double safe = FalloutSafeHeight(yieldKt);
        if (!(safe > 0.0) || !(burstHeight > 0.0)) return 1.0;

        return 1.0 - Smoothstep(0.5 * safe, safe, burstHeight);
    }

    /// <summary>
    /// How wide the column of dust under an air burst is drawn, as a share of a surface burst's stem.
    ///
    /// <para>An air burst still raises one: the afterwinds lift the ground under it into a column
    /// that climbs to the cloud. It is thinner than the dirt a surface burst throws up, and above a
    /// few fallout-safe heights the winds at the ground are too weak to raise one at all. Nagasaki
    /// at 2.7 of those heights has a column, and the Tumbler-Snapper air drops at about 5 still
    /// raised one that never reached the cloud; the fade to nothing between 3 and 7 is drawn, not
    /// measured.</para>
    /// </summary>
    public static double StemShare(double yieldKt, double burstHeight)
    {
        double safe = FalloutSafeHeight(yieldKt);
        if (!(safe > 0.0) || !(burstHeight > 0.0)) return 1.0;

        double airborne = Smoothstep(0.5 * safe, safe, burstHeight);
        double fading = Smoothstep(3.0 * safe, 7.0 * safe, burstHeight);
        return (1.0 - ((1.0 - AirStemShare) * airborne)) * (1.0 - fading);
    }

    // When the front reaches the ground under an air burst: a bisection, and the same answer for every
    // shape of one cloud, so the last is kept. Per thread, since tests draw shapes in parallel.
    private static double GroundArrival(double yieldKt, double burstHeight, BlastFront? front)
    {
        if (_arrival.Kt == yieldKt && _arrival.Height == burstHeight && _arrival.Front == front) return _arrival.Seconds;

        double seconds = front is { } f ? f.ArrivalSeconds(burstHeight) : ShockArrivalSeconds(yieldKt, burstHeight);
        _arrival = (yieldKt, burstHeight, front, seconds);
        return seconds;
    }

    [ThreadStatic]
    private static (double Kt, double Height, BlastFront? Front, double Seconds) _arrival;

    // How far an air burst's column has formed: none until the front comes down to the ground.
    private static double ColumnFormed(double yieldKt, double burstHeight, double age, BlastFront? front)
    {
        if (!(burstHeight > 0.0)) return 1.0;

        double reaches = GroundArrival(yieldKt, burstHeight, front);
        return Smoothstep(reaches, reaches + ColumnFormsSeconds, age);
    }

    /// <summary>
    /// How far up to the cap's underside an air burst's column of dust climbs, as a share: all the
    /// way from a burst low enough to be drawing the ground up into its fireball, and half from four
    /// fallout-safe heights, where the photographs of the higher air drops show a column rising under
    /// a cloud it never reaches. The share is drawn, not measured.
    /// </summary>
    public static double ColumnReach(double yieldKt, double burstHeight)
    {
        double safe = FalloutSafeHeight(yieldKt);
        if (!(safe > 0.0) || !(burstHeight > 0.0)) return 1.0;

        return 1.0 - (0.5 * Smoothstep(1.5 * safe, 4.0 * safe, burstHeight));
    }

    /// <summary>How long an air burst's column takes to climb to its height, once the front is at the ground.</summary>
    public static double ColumnClimbSeconds => RiseSeconds * 0.5;

    /// <summary>
    /// The stem's radius and how far up the cap the column reaches in one float: whole metres, and
    /// the share in the fraction. The shader decodes it as <see cref="UnpackStem"/> does.
    /// </summary>
    public static float PackStem(double stemRadius, double columnTop)
    {
        double metres = Math.Floor(Math.Clamp(double.IsFinite(stemRadius) ? stemRadius : 0.0, 0.0, 1.0e6));
        double share = Math.Clamp(double.IsFinite(columnTop) ? columnTop : 1.0, 0.0, 0.99);
        return (float)(metres + share);
    }

    /// <summary>What <see cref="PackStem"/> packed; a share at its top is the whole way.</summary>
    public static (double StemRadius, double ColumnTop) UnpackStem(float packed)
    {
        double metres = Math.Floor(packed);
        double share = packed - metres;
        return (metres, share >= ColumnJoins ? 1.0 : share);
    }

    /// <summary>A column share at or over this joins the cap.</summary>
    public const double ColumnJoins = 0.985;

    /// <summary>An air burst's dust column against a surface burst's stem, where it is fully formed.</summary>
    public const double AirStemShare = 0.45;

    /// <summary>
    /// The heat, the ground coupling and the stem share in one float, for a push constant with no
    /// room for three: six bits each above a fraction. Six and not eight, because a float's
    /// resolution falls as its size grows -- at two bytes the heat was left to a sixty-fourth, at
    /// twelve bits it keeps a thousandth. The shader decodes it exactly as <see cref="UnpackHeat"/>.
    /// </summary>
    public static float PackHeat(double heat, double coupling, double stemShare)
    {
        double h = Math.Clamp(double.IsFinite(heat) ? heat : 0.0, 0.0, 0.99);
        double c = Math.Round(Math.Clamp(double.IsFinite(coupling) ? coupling : 1.0, 0.0, 1.0) * PackLevels);
        double s = Math.Round(Math.Clamp(double.IsFinite(stemShare) ? stemShare : 1.0, 0.0, 1.0) * PackLevels);
        return (float)((2.0 * (c + ((PackLevels + 1.0) * s))) + h);
    }

    /// <summary>What <see cref="PackHeat"/> packed.</summary>
    public static (double Heat, double Coupling, double StemShare) UnpackHeat(float packed)
    {
        double q = Math.Floor(packed / 2.0);
        return (packed - (2.0 * q), (q % (PackLevels + 1.0)) / PackLevels,
                Math.Floor(q / (PackLevels + 1.0)) / PackLevels);
    }

    // The steps the coupling and the stem share are packed in.
    private const double PackLevels = 63.0;

    /// <summary>
    /// The sphere about the burst that holds the whole drawn cloud at an age, lean and billows
    /// included. The top alone does under the tropopause; an anvil is wider than it is tall, and
    /// reaches further sideways than up.
    ///
    /// <para>Per age rather than once for the life, because the cap goes on spreading through the
    /// stand: a sphere big enough for the end of it spreads the march's 48 steps over half as much
    /// again while the cloud is still rising, which is grain for nothing.</para>
    /// </summary>
    public static double DrawnBound(double yieldKt, double age, double burstHeight = 0.0)
        => GrownBound(RisenBound(yieldKt, burstHeight), At(yieldKt * 1.0e6, age, burstHeight), age);

    /// <summary>
    /// That sphere as the risen cloud has it: at the overshoot's peak, which is as big as the RISING
    /// shape ever gets. Fixed from then on, which is what the lean is measured against -- reckoned
    /// against the grown bound, the same height reads as lower down the column and the lean weakens
    /// by a quarter over the stand.
    /// </summary>
    public static double RisenBound(double yieldKt, double burstHeight = 0.0, double airRatio = 1.0)
    {
        double top = DrawnCloudTop(yieldKt);
        if (top <= 0.0) return 0.0;

        if (IsThin(airRatio))
        {
            Shape ball = At(yieldKt * 1.0e6, LifeFor(yieldKt) * 0.99, burstHeight, airRatio);
            return (ball.CapCentre + (3.0 * ball.CapTube)) * BoundMargin;
        }

        return Math.Max(top, ReachOf(At(yieldKt * 1.0e6, RiseSeconds * (1.0 + OvershootAt), burstHeight))
                             * BoundMargin);
    }

    /// <summary>
    /// The air, against sea level, under which a burst grows no mushroom: about 30 km on Earth. There
    /// is too little air for a buoyant column, and the fireball's debris climbs and spreads as one
    /// glowing ball (Glasstone and Dolan, 2.130-2.143).
    /// </summary>
    public const double ThinAirRatio = 0.01;

    /// <summary>Whether air this thin grows a ball of debris rather than a mushroom.</summary>
    public static bool IsThin(double airRatio) => double.IsFinite(airRatio) && airRatio < ThinAirRatio;

    /// <summary>
    /// How much bigger a fireball grows in thinner air: as the inverse cube root of the density
    /// (Glasstone and Dolan, 2.130), since it swells until it has swept up a given mass of air.
    /// Bounded where Teak's stopped: 3.8 Mt at 77 km was 29 km across at 3.5 s, eight times its size in
    /// dense air, in air a cube root would have grown it 35 times.
    /// </summary>
    public static double ThinAirGrowth(double airRatio)
        => double.IsFinite(airRatio) && airRatio > 0.0 ? Math.Clamp(Math.Cbrt(1.0 / airRatio), 1.0, MostThinAirGrowth)
                                                       : MostThinAirGrowth;

    /// <summary>The most thin air grows a fireball by.</summary>
    public const double MostThinAirGrowth = 8.0;

    /// <summary>
    /// How many of its own radii a thin-air fireball's debris climbs over the rise, and how many times
    /// its size it swells to over its life: it rises ballistically and spreads with nothing to hold it.
    /// Drawn, not measured.
    /// </summary>
    public const double ThinBallClimb = 4.0;

    /// <summary><inheritdoc cref="ThinBallClimb"/></summary>
    public const double ThinBallSwell = 3.0;

    /// <summary>
    /// The risen bound grown to hold the stand's spreading, sheared cap. Only the shape at the age
    /// and the age itself, so the shader, which is pushed the risen bound and the shape, reckons the
    /// same sphere for its march.
    /// </summary>
    public static double GrownBound(double risen, Shape shape, double age)
        => Math.Max(risen, ReachOf(shape) * (1.0 + (AgedShear * Aged(age))) * BoundMargin);

    /// <summary>What the bound carries over the cloud's own reach, for the billows past it.</summary>
    public const double BoundMargin = 1.15;

    private static double ReachOf(Shape shape)
        => Math.Sqrt((shape.CapCentre * shape.CapCentre) + Math.Pow(shape.CapRadius + shape.CapTube, 2.0));

    /// <summary>
    /// And how long it stands once it has risen, fading out over the last of it.
    ///
    /// <para>A real cloud lasts tens of minutes, spreading and shearing into a long plume; drawn
    /// shorter it is gone before anybody has finished looking at it. Eight minutes, on the same
    /// compressed clock as the rise, is most of a real one's recognisable life. Over it the cap
    /// spreads and thins and the stem narrows away first, which is the order a real cloud
    /// comes apart in -- rooted where it burst, by decision (docs/NUCLEAR-NEXT.md item 3).</para>
    ///
    /// <para><b>It costs the pass for as long as it is on screen</b>, about three and a half
    /// milliseconds at the watching pose.</para>
    /// </summary>
    public const double StandSeconds = 480.0;

    /// <summary>How far the cap spreads over the stand, as a share of its width when it stops rising.</summary>
    public const double AgedSpread = 0.6;

    /// <summary>How much of the stem's width is gone by the end of the stand.</summary>
    public const double AgedStemLoss = 0.65;

    /// <summary>
    /// How much longer downwind the cloud's upper part is drawn by the end of its stand: a share of
    /// its own reach on that side. A real cloud drifts off as a plume; this one stands over the
    /// ground it burned by decision, so it shears instead -- the upwind edge and the foot unmoved.
    /// The shader stretches the shape by this (<c>AgedShear</c> there), and the bound grows with it.
    /// </summary>
    public const double AgedShear = 1.2;

    /// <summary>How much thinner the whole cloud is by the time it starts to fade out.</summary>
    public const double AgedThinning = 0.15;

    /// <summary>The last of the stand, over which it fades out entirely.</summary>
    public const double FadeOutSeconds = 90.0;

    /// <summary>Total life, after which there is nothing to draw.</summary>
    public const double LifeSeconds = RiseSeconds + StandSeconds;

    /// <summary>
    /// A yield's life, which is <see cref="LifeSeconds"/> and longer only for a cloud its own blast
    /// front holds in past the rise. The front runs at the speed of sound in real time and the rise
    /// is compressed, so at 50 Mt the cloud is still growing into the front minutes after the rise
    /// is over -- and a fixed life faded it out the moment it reached its full height.
    /// </summary>
    public static double LifeFor(double yieldKt) => LifeSeconds + HeldByFront(yieldKt);

    /// <summary>
    /// How long past the rise the front takes to reach the settled cloud's farthest point. Nothing up
    /// to about a megatonne, where the cloud is inside the front by the end of the rise.
    /// </summary>
    public static double HeldByFront(double yieldKt)
    {
        if (yieldKt <= 0.0) return 0.0;
        if (_held.TryGetValue(yieldKt, out double known)) return known;

        // The last moment the drawn cloud is still outside the front, scanned over the stand: the cap
        // spreads and shears as the front runs on, so neither end alone says when it stops binding.
        double last = RiseSeconds;
        for (double age = RiseSeconds; age <= RiseSeconds + StandSeconds; age += HeldScanSeconds)
        {
            if (DrawnReach(CapAt(yieldKt, age, 0.0)) > ShockRadius(yieldKt, age)) last = age;
        }

        double held = last - RiseSeconds;
        if (_held.Count > 64) _held.Clear();
        _held[yieldKt] = held;
        return held;
    }

    private const double HeldScanSeconds = 5.0;

    // Per yield, since every shape asks for its life: a hundred cap shapes a solve.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<double, double> _held = new();

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

    // Bursts closer together in time than this are one event, as bangs are: a bus's warheads are
    // released together and land within a frame or two of each other.
    public const double SameBurstSeconds = 0.5;

    /// <summary>
    /// Whether a burst <paramref name="gapMetres"/> from a standing cloud is that cloud's own event
    /// rather than a burst of its own: inside the fireball the two make together, and while the
    /// standing one is still going off. A bomb dropped on a cloud already standing is a second
    /// explosion, with its own flash and its own front.
    /// </summary>
    public static bool IsTheSameBurst(double gapMetres, double standingAgeSeconds, double combinedChargeKg)
    {
        if (!(standingAgeSeconds <= SameBurstSeconds)) return false;

        double reach = PeakFireballRadius(KilotonsFor(combinedChargeKg));
        return gapMetres <= reach;
    }

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
    /// <see cref="FireballRadius"/> goes as the 0.4 power, hence this. It applies as far as the
    /// burst is coupled to the ground (<see cref="GroundCoupling"/>): wholly on it, not at all in
    /// free air.</para>
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
    /// ring beyond the cap. What a land burst does have out there is thinner and faster: the dust the
    /// blast front itself lifts as it passes, which is <see cref="ShockRadius"/>'s.</para>
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

    // Sea-level air and its sound speed, for the blast wave. Earth's, because the ring is drawn only
    // where there is air and dust, and the difference between two thick atmospheres is not visible
    // in a front that leaves the frame in seconds.
    private const double AirKgPerM3 = 1.225;
    private const double SoundMetresPerSecond = BlastWave.SoundMetresPerSecond;
    private const double JoulesPerKiloton = 4.184e12;

    // The Sedov front is let go at this Mach number, and relaxes to sound speed after it.
    private const double StrongShockMach = 1.2;

    /// <summary>
    /// How far the blast wave has run along the ground (m): the front that lifts the ring of dust.
    ///
    /// <para><b>Real time, not the rise's clock.</b> The ring is the one thing in the first seconds
    /// that is plainly <em>fast</em>, racing out past a cloud that has barely started to climb, and
    /// compressed fivefold like the rise it would crawl. It is Sedov–Taylor while the shock is
    /// strong — the energy doubled, because the ground reflects the half going down — and then runs
    /// on at sound speed, eased from the Mach number it was let go at so the speed has no step. A
    /// real front stays slightly supersonic further out than this, so it arrives a little late.</para>
    /// </summary>
    public static double ShockRadius(double yieldKt, double age)
    {
        return ShockRadiusAt(yieldKt, age);
    }

    /// <summary>
    /// When the blast front reaches <paramref name="distanceMetres"/> from the burst: the inverse of
    /// <see cref="ShockRadius"/>, which only ever grows. Bisected rather than solved, because the
    /// law is Sedov and then an eased sonic run with no closed inverse across the join.
    /// </summary>
    public static double ShockArrivalSeconds(double yieldKt, double distanceMetres)
    {
        return ShockArrivalIn(yieldKt, distanceMetres, AirKgPerM3, SoundMetresPerSecond, BlastWave.SurfaceReflection);
    }

    private static double ShockRadiusAt(double yieldKt, double age)
        => ShockRadiusIn(yieldKt, age, AirKgPerM3, SoundMetresPerSecond, BlastWave.SurfaceReflection);

    /// <summary>
    /// <see cref="ShockRadius"/> in any air: its density (kg/m³), its speed of sound (m/s), and the
    /// energy's multiple for the ground, two for a burst on it and one in free air.
    /// </summary>
    internal static double ShockRadiusIn(double yieldKt, double age, double densityKgPerM3,
                                         double soundMetresPerSecond, double reflection)
    {
        if (yieldKt <= 0.0 || age <= 0.0 || !(densityKgPerM3 > 0.0) || !(soundMetresPerSecond > 0.0)) return 0.0;

        double k = 1.03 * Math.Pow(reflection * yieldKt * JoulesPerKiloton / densityKgPerM3, 0.2);

        // Where the Sedov speed, 0.4 R/t, falls to the release Mach number.
        double released = Math.Pow(0.4 * k / (StrongShockMach * soundMetresPerSecond), 1.0 / 0.6);
        if (age <= released) return k * Math.Pow(age, 0.4);

        double since = age - released;
        double excess = (StrongShockMach - 1.0) * soundMetresPerSecond;

        return (k * Math.Pow(released, 0.4)) + (soundMetresPerSecond * since)
               + (excess * released * (1.0 - Math.Exp(-since / released)));
    }

    /// <summary>The inverse of <see cref="ShockRadiusIn"/>, bisected as <see cref="ShockArrivalSeconds"/> is.</summary>
    internal static double ShockArrivalIn(double yieldKt, double distanceMetres, double densityKgPerM3,
                                          double soundMetresPerSecond, double reflection)
    {
        if (yieldKt <= 0.0 || !(distanceMetres > 0.0)) return 0.0;
        if (!(densityKgPerM3 > 0.0) || !(soundMetresPerSecond > 0.0)) return double.PositiveInfinity;

        double late = 1.0;
        while (ShockRadiusIn(yieldKt, late, densityKgPerM3, soundMetresPerSecond, reflection) < distanceMetres
               && late < 3600.0) late *= 2.0;

        double early = 0.0;
        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5 * (early + late);
            if (ShockRadiusIn(yieldKt, mid, densityKgPerM3, soundMetresPerSecond, reflection) < distanceMetres) early = mid;
            else late = mid;
        }

        return late;
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
    /// <para>Sized in fireball radii, because the published scaling for its size is thin and
    /// strongly dependent on humidity. Timed on the blast's clock, which is the rarefaction's.</para>
    ///
    /// <para><b>Nothing draws it</b>: KSA's volumetric particle lit it as dark smoke at every size, so
    /// this is the arithmetic for a shell the cloud pass would draw -- docs/NUCLEAR-EFFECT.md.</para>
    /// </summary>
    public const double WilsonInFireballs = 3.4;

    /// <summary>
    /// How long that shell lasts at 20 kt: it formed 1 to 2 s after the burst and was gone "within
    /// another second or so" (Glasstone §2.49). Blast times scale as the cube root of the yield.
    /// </summary>
    public const double WilsonAt20KtSeconds = 3.0;

    /// <summary>The condensation shell's radius, or zero for a charge too small to make one.</summary>
    public static double WilsonRadius(double chargeKg)
        => chargeKg < ThresholdKg
               ? 0.0
               : WilsonInFireballs * PeakFireballRadius(KilotonsFor(chargeKg));

    /// <summary>And how long it lasts before the pressure recovers and it evaporates.</summary>
    public static double WilsonSeconds(double chargeKg)
        => chargeKg < ThresholdKg
               ? 0.0
               : WilsonAt20KtSeconds * Math.Cbrt(KilotonsFor(chargeKg) / 20.0);

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
    /// Seconds the fireball stays incandescent, after which it is lit smoke. Not a formula
    /// Glasstone states: it fits his anchors, 10 s at 20 kt and 37 to 60 s at a megatonne.
    /// </summary>
    public static double DarkAfter(double yieldKt)
        => yieldKt <= 0.0 ? 0.0 : 3.0 * Math.Pow(yieldKt, 0.4);

    /// <summary>
    /// And how long it is <em>drawn</em> glowing, which parts company with the law at the top of the
    /// dial.
    ///
    /// <para>The cloud's clock is compressed and the flash's is not, so they diverge as the yield
    /// climbs: 340 kt glows for 30.9 s against a 57 s rise, which is a ball still burning after its
    /// own mushroom has formed. Compressing the flash by the same factor is not the alternative — it
    /// works out at a blink — so it runs real until <see cref="LongestGlowSeconds"/>.</para>
    /// </summary>
    public static double FlashSeconds(double yieldKt)
        => Math.Min(DarkAfter(yieldKt), LongestGlowSeconds);

    /// <summary>
    /// <see cref="FlashSeconds(double)"/> in the air the burst went off in (<see cref="ThermalAltitude.GlowScale"/>):
    /// Orange's ball glowed about 18 s and Teak's 2.5. Uncapped where the air is too thin for a column,
    /// since the cap only keeps a ball from outliving its own mushroom; and no air at all is the vacuum's
    /// flash, <see cref="VacuumFlashSeconds"/>.
    /// </summary>
    public static double FlashSeconds(double yieldKt, double airRatio)
    {
        if (double.IsFinite(airRatio) && airRatio == 0.0) return VacuumFlashSeconds(yieldKt);

        double scale = ThermalAltitude.GlowScale(airRatio);
        double real = scale == 1.0 ? DarkAfter(yieldKt) : DarkAfter(yieldKt) * scale;
        return IsThin(airRatio) ? real : Math.Min(real, LongestGlowSeconds);
    }

    /// <summary>
    /// How long a burst with no air round it is seen: the device's own vapour, flashing and gone, since
    /// there is nothing for its X-rays to heat into a fireball. The pulse's own fall, held to half a
    /// second so it is seen at all.
    /// </summary>
    public static double VacuumFlashSeconds(double yieldKt)
        => yieldKt <= 0.0 ? 0.0 : Math.Max(6.0 * PulseDecayInMinima * PulseMinimumSeconds(yieldKt), LeastVacuumFlashSeconds);

    /// <summary>The shortest a vacuum flash is drawn (s).</summary>
    public const double LeastVacuumFlashSeconds = 0.5;

    /// <summary>
    /// The longest a ball is drawn glowing. 20 kt keeps its real 9.9 s; above that the glow still
    /// lengthens with yield where the law has it past a third of the rise, which is when the cap has
    /// formed round the ball on the compressed clock.
    /// </summary>
    public const double LongestGlowSeconds = 14.0;

    /// <summary>
    /// How long the ball stays white-hot: the heat pulse, which has given out 80% of its energy by
    /// ten times its second maximum (Glasstone §7.85) -- 1.6 s at 20 kt, 5.4 s at 340 kt. Never
    /// before the drawn second maximum, which is slowed where it would be too quick to see, and
    /// never past half the glow, so the cooling has room.
    /// </summary>
    public static double WhiteHotSeconds(double yieldKt)
    {
        if (yieldKt <= 0.0) return 0.0;

        double pulse = Math.Max(10.0 * ThermalMaximumSeconds(yieldKt), 1.2 * PulsePeakSeconds(yieldKt));
        return Math.Min(pulse, 0.5 * FlashSeconds(yieldKt));
    }

    /// <summary>
    /// How long the ball takes to grow, as R ~ t^0.4 (Taylor): 90% of its largest by about three
    /// times its second maximum, 1.6 s at 340 kt. Floored so the smallest ball does not arrive at
    /// full size within a frame.
    /// </summary>
    public static double GrowthSeconds(double yieldKt)
        => yieldKt <= 0.0 ? 0.0 : Math.Max(4.0 * ThermalMaximumSeconds(yieldKt), 0.2);

    /// <summary><see cref="GrowthSeconds(double)"/> on the pulse of the air the burst went off in.</summary>
    public static double GrowthSeconds(double yieldKt, double airRatio)
    {
        double scale = ThermalAltitude.PulseScale(airRatio);
        return scale == 1.0 ? GrowthSeconds(yieldKt) : GrowthSeconds(yieldKt) * scale;
    }

    /// <summary>
    /// <see cref="WhiteHotSeconds(double)"/> in the air the burst went off in: the heat pulse on its own
    /// clock there, and still never past half the glow.
    /// </summary>
    public static double WhiteHotSeconds(double yieldKt, double airRatio)
    {
        if (yieldKt <= 0.0) return 0.0;

        double scale = ThermalAltitude.PulseScale(airRatio);
        double dark = FlashSeconds(yieldKt, airRatio);
        if (scale == 1.0 && dark == FlashSeconds(yieldKt)) return WhiteHotSeconds(yieldKt);

        double pulse = Math.Max(10.0 * ThermalMaximumSeconds(yieldKt), 1.2 * PulsePeakSeconds(yieldKt)) * scale;
        return Math.Min(pulse, 0.5 * dark);
    }

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

    /// <summary>When the thermal pulse peaks by Glasstone's law, without the stretch it is drawn at.</summary>
    public static double ThermalMaximumSeconds(double yieldKt)
        => yieldKt <= 0.0 ? 0.0 : PulsePeakCoefficient * Math.Pow(yieldKt, PulseExponent);

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
    /// <para><see cref="Shock"/> is the blast front along the ground. <see cref="StemRadius"/> is a
    /// surface burst's stem, which the dust ring is sized by; the column actually drawn is
    /// <see cref="StemShare"/> of it, and <see cref="Coupling"/> is <see cref="GroundCoupling"/>.
    /// <see cref="ColumnTop"/> is how far up to the cap's centre the column reaches: one joins it.</para>
    public readonly record struct Shape(
        double CapCentre, double CapRadius, double CapTube,
        double StemTop, double StemRadius, double SurgeRadius, double SurgeHeight,
        double Roll, double Fade, double Shock = 0.0, double Coupling = 1.0, double StemShare = 1.0,
        double ColumnTop = 1.0)
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
    /// the drawn glow collapses to about 5 for a fifth of a second, a flicker at the brightest
    /// moment of the burst. A fifth to a quarter of the second maximum is the shape Glasstone's
    /// curves have.</para>
    /// </summary>
    public const double ShockFrontShare = 0.25;

    /// <summary>The fireball at its largest, which is what it spends most of the flash at.</summary>
    public static double PeakFireballRadius(double yieldKt, double burstHeight = 0.0)
        => FireballRadius(yieldKt) * GroundGain(yieldKt, burstHeight);

    // The surface gain as far as the ground is under the ball.
    private static double GroundGain(double yieldKt, double burstHeight)
        => 1.0 + ((SurfaceBurstGain - 1.0) * GroundCoupling(yieldKt, burstHeight));

    /// <summary>
    /// How long the ball goes on glowing after the luminous phase, as a fraction of the <em>rise</em>
    /// rather than of the flash.
    ///
    /// <para>Measured against the rise on purpose, and it is the same departure
    /// <see cref="DrawnScale"/> is. The luminous phase is real time and the rise is compressed
    /// fivefold, so a fireball that goes dark on its own clock is out before the cloud has done one
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
    /// Glow the ember starts from, at the end of the luminous phase. It cools from here to nothing
    /// over <see cref="EmberSeconds"/>, easing out, so the ball goes out rather than being removed
    /// while still glowing.
    /// </summary>
    public const double EmberGlow = 40.0;

    /// <summary>
    /// Glow the ball settles to once the heat pulse is over, and cools from until it is an ember.
    /// It meets <see cref="EmberGlow"/> exactly at the end of the luminous phase, so the stages join
    /// without a step.
    /// </summary>
    public const double BurnGlow = 200.0;

    /// <summary>
    /// Glow through the heat pulse, falling to <see cref="BurnGlow"/> as it ends: a ball near
    /// 7,700 degrees for seconds (Glasstone §2.125) rather than one that dims as soon as it peaks.
    /// </summary>
    public const double WhiteHotGlow = 320.0;

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
    public static Flash FlashAt(double chargeKg, double age, double burstHeight = 0.0, double airRatio = 1.0)
    {
        double kt = KilotonsFor(chargeKg);
        if (kt <= 0.0 || age < 0.0) return default;
        if (double.IsFinite(airRatio) && airRatio == 0.0) return VacuumFlashAt(kt, age);

        // The heat pulse and the glow by the air (ThermalAltitude); each exactly one at sea level.
        double pulseScale = ThermalAltitude.PulseScale(airRatio);
        double dark = FlashSeconds(kt, airRatio);
        if (age >= dark + EmberSeconds) return default;

        double t = Math.Min(1.0, age / dark);
        double ember = age <= dark ? 0.0 : (age - dark) / EmberSeconds;
        double whiteHot = WhiteHotSeconds(kt, airRatio);

        // Growing as t^0.4 to its largest, then contracting: gently while it burns, hard once it is
        // an ember. From a tenth of its size, so the first frame has a ball in it.
        //
        // <b>What contracts is the incandescent region, not the fireball.</b> The hot air mass keeps
        // growing the whole time; its outer skin cools below visible emission first, so the part of
        // it that glows shrinks inward while the part of it that exists does not. That is why the
        // ball can shrink without contradicting a law that says a fireball only ever grows, and it
        // is what lets it recede into its own smoke instead of being switched off inside it.
        double radius = FireballRadius(kt) * GroundGain(kt, burstHeight) * ThinAirGrowth(airRatio)
                        * Math.Clamp(Math.Pow(age / GrowthSeconds(kt, airRatio), 0.4), 0.10, 1.0)
                        * (1.0 - (LuminousShrink * t))
                        * (1.0 - (EmberShrink * ember));

        // Blue-white to white-yellow through the heat pulse, then orange, then deep red.
        double cooling = age <= whiteHot ? 0.0 : Math.Min(1.0, (age - whiteHot) / (dark - whiteHot));
        double3 colour = age <= whiteHot
                             ? Lerp(new double3(1.0, 0.97, 0.92), new double3(1.0, 0.88, 0.60), age / whiteHot)
                             : cooling < 0.4
                                 ? Lerp(new double3(1.0, 0.88, 0.60), new double3(1.0, 0.62, 0.25), cooling / 0.4)
                                 : Lerp(new double3(1.0, 0.62, 0.25), new double3(0.75, 0.16, 0.05),
                                        (cooling - 0.4) / 0.6);

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
        // The EMBER is the last of the heat, cooling to nothing as the cloud swallows it.
        // Each stage runs in its own phase rather than all three competing: t clamps at 1, so a burn
        // term left in the maximum past the luminous phase holds its final value forever and the
        // ember can never darken under it. They join without a step because the burn is sized to
        // arrive at exactly EmberGlow when t reaches 1.
        // THE PULSE has a hard deadline of its own rather than a fraction of the luminous phase:
        // it is shock-front radiation, and it is over long before the ball is.
        double tMin = PulseMinimumSeconds(kt) * pulseScale;
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
                         + ((1.0 - ShockFrontShare) * Smoothstep(tMin, PulsePeakSeconds(kt) * pulseScale, age));

        // In thin air there is no front to hide the ball, and the two maxima merge into one.
        double single = ThermalAltitude.SinglePulse(airRatio);
        if (single > 0.0) opening += (1.0 - opening) * single;

        double burn = age <= whiteHot
                          ? WhiteHotGlow * Math.Pow(BurnGlow / WhiteHotGlow, age / whiteHot)
                          : BurnGlow * Math.Pow(EmberGlow / BurnGlow, cooling);

        double glow = age <= dark
                          ? Math.Max(pulse, burn * opening)
                          : EmberGlow * (1.0 - Smoothstep(0.0, 1.0, ember));

        return new Flash(radius, colour, glow);
    }

    // The vacuum's flash: the device's vapour, white-blue and at the pulse's peak brightness, growing to a
    // dense-air ball's size and gone within the flash, with no burn and no ember to follow.
    private static Flash VacuumFlashAt(double kt, double age)
    {
        double seconds = VacuumFlashSeconds(kt);
        if (age >= seconds) return default;

        double radius = FireballRadius(kt) * Math.Clamp(Math.Pow(age / GrowthSeconds(kt), 0.4), 0.10, 1.0);
        double glow = PeakGlow * Math.Exp(-6.0 * age / seconds) * (1.0 - Smoothstep(0.8 * seconds, seconds, age));
        return new Flash(radius, new double3(0.85, 0.9, 1.0), glow);
    }

    /// <summary>
    /// The e-folding time of the heat left inside the young cloud once the ball has gone dark.
    ///
    /// <para>Rise-compressed like <see cref="EmberSeconds"/>, and for the same reason: the real glow
    /// through a tower shot's dust lasts a few seconds of a climb that takes minutes, and on the
    /// drawn clock that would be over before the cloud has formed round it. Three seconds leaves a
    /// glow about a sixth as hot six seconds after the ball is dark and gone by ten, which is the
    /// "first ten seconds" every film of one shows.</para>
    /// </summary>
    public const double CoolingSeconds = 3.0;

    /// <summary>
    /// How hot the inside of the young cloud still is, in [0, 1]: what glows orange through the
    /// gaps in its own smoke, and what turns over as a ring of fire when the ball hollows.
    ///
    /// <para><b>It outlasts the ball, and that is the point.</b> The ball is the incandescent
    /// region, which shrinks inward as its skin cools (<see cref="LuminousShrink"/>); the hot gas
    /// under the skin is the cloud's own core, and it is still glowing when the dust has closed
    /// over the surface. Drawn as the ball alone, the glow ends when the ball does and the cloud
    /// round it is clean smoke from then on.</para>
    ///
    /// <para>Rises as the white-hot pulse ends rather than jumping: before that the ball itself is
    /// the fire, and pockets showing through it would break up the one clean shape the first
    /// seconds have.</para>
    /// </summary>
    public static double Incandescence(double chargeKg, double age)
    {
        double kt = KilotonsFor(chargeKg);
        if (kt <= 0.0 || age <= 0.0) return 0.0;

        double dark = FlashSeconds(kt);
        double whiteHot = WhiteHotSeconds(kt);
        double warming = Smoothstep(whiteHot * 0.3, whiteHot, age);
        double cooling = Math.Exp(-Math.Max(age - dark, 0.0) / CoolingSeconds);
        double heat = warming * cooling;

        // Cut rather than left as a tail nobody can see, so the shader's term is exactly zero.
        return heat < 0.02 ? 0.0 : heat;
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
    public const double StemOfCap = 0.19;

    /// <summary>Where the cloud is at <paramref name="age"/> seconds, for a charge in kg.</summary>
    /// <param name="burstHeight">
    /// How far above the ground it went off (m). Every height in the shape is from the ground under
    /// the burst, so the stem, the skirt and the dust ring stay on the ground and only the cap starts
    /// up at the burst.
    /// </param>
    // The cap before anything holds it inside its blast front, with what the stem is measured from.
    private readonly record struct Upright(double Top, double CapR, double Hob, double CapCentre, double CapRadius,
                                           double CapTube, double Spread, double Squash, double Widen, double Aged);

    private static Upright CapAt(double kt, double age, double burstHeight)
    {
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
        //
        // Past the tropopause the stratosphere brakes it, and the height it does not make goes into
        // width: the cap thins by the squash and widens by its square root, which keeps its volume.
        //
        // An air burst starts its climb at the height it went off rather than at the ground, and has
        // that much less of it to make: the cap still settles where the laws put it, measured from
        // the ground. A burst at or above that height still climbs a little on its own buoyancy.
        double hob = Math.Max(double.IsFinite(burstHeight) ? burstHeight : 0.0, 0.0);
        double settles = Stratified(top * 0.75, kt);
        double climbShare = settles > 0.0 ? Math.Max(1.0 - (hob / settles), MinClimbShare) : 1.0;
        double capCentre = hob + (Stratified(top * 0.75 * rise, kt) * climbShare);
        double squash = CapSquash(top * rise, kt);

        // The cap widens as it rises, and is done widening before a pen reaches the widest point of
        // its own stroke -- which is the whole of it, because a pen crosses the equator once and
        // then tucks under, so the width it finds there is the width the cap keeps. Widening after
        // that is drawn by nothing: it moves the silhouette the pens have already passed. Spread out
        // over the full rise it left the cap 19% narrower than every other number here says it is.
        double spread = 0.55 + (0.57 * Math.Min(1.0, age / (RiseSeconds * SpreadBy)));
        double aged = Aged(age);
        double widen = Widening(age);
        double capRadius = capR * spread * widen / Math.Sqrt(squash) * (1.0 + (AgedSpread * aged));

        double capTube = capR * 0.45 * widen * squash / Math.Sqrt(1.0 + (AgedSpread * aged));

        return new Upright(top, capR, hob, capCentre, capRadius, capTube, spread, squash, widen, aged);
    }

    // How far the cloud as the shader draws it reaches from the burst: sheared downwind over the stand,
    // leaned, and with billows standing proud of the tube.
    private static double DrawnReach(Upright cap, double scale = 1.0)
    {
        double rise = (cap.CapCentre - cap.Hob) * scale;
        double height = rise + ((CrownInTubes + BillowReach) * cap.CapTube * scale);
        double across = ((cap.CapRadius + ((1.0 + BillowReach) * cap.CapTube)) * scale * (1.0 + (AgedShear * cap.Aged)))
                        + (MostLean * (cap.Hob + rise));
        return Math.Sqrt((across * across) + (height * height));
    }

    // The scale about the burst that brings the drawn cloud inside the front. Not one ratio: the lean
    // is measured from the ground, so an air burst's part of it does not shrink with the rest.
    private static double InsideFront(Upright cap, double front)
    {
        if (DrawnReach(cap) <= front) return 1.0;

        double low = 0.0;
        double high = 1.0;
        for (int i = 0; i < 30; i++)
        {
            double mid = 0.5 * (low + high);
            if (DrawnReach(cap, mid) <= front) low = mid;
            else high = mid;
        }

        return low;
    }

    public static Shape At(double chargeKg, double age, double burstHeight = 0.0, double airRatio = 1.0)
        => At(chargeKg, age, burstHeight, airRatio, null);

    /// <summary>
    /// <see cref="At(double, double, double, double)"/> against a burst's own <paramref name="front"/>, which
    /// sets where the ring of dust is, how far the cloud is held inside it, and when an air burst's
    /// column starts; null is a surface burst's in sea-level air.
    /// </summary>
    internal static Shape At(double chargeKg, double age, double burstHeight, double airRatio, BlastFront? front)
    {
        double kt = KilotonsFor(chargeKg);
        if (kt <= 0.0 || age < 0.0 || age >= LifeFor(kt)) return default;
        if (IsThin(airRatio)) return ThinBall(kt, age, burstHeight, airRatio);

        Upright cap = CapAt(kt, age, burstHeight);
        double capR = cap.CapR;
        double hob = cap.Hob;
        double capCentre = cap.CapCentre;
        double capRadius = cap.CapRadius;
        double capTube = cap.CapTube;
        double spread = cap.Spread;
        double squash = cap.Squash;
        double widen = cap.Widen;
        double aged = cap.Aged;

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
        // Measured on the cap's height rather than its width, which an anvil has far more of.
        double capHeight = capR * spread * squash;
        double underside = StemCeiling(capCentre, capHeight);
        double climb = (capCentre + (capHeight * Oblate))
                       * Math.Sqrt(Math.Min(1.0, Progress(age) / ClimbUntil));
        double stemTop = Math.Max(0.0, Math.Min(climb, underside));

        // A slight twist and no more. The emitters drawing this are pens that keep everywhere they
        // have been, so a roll of any size draws a helix rather than a rolling cap -- eight of them
        // being a spiral staircase. The rollover has to come from the *path* shape below.
        double roll = 0.30 * (1.0 - Math.Exp(-2.0 * age / RiseSeconds));

        // Nothing the burst throws can be outside its own blast front, and the cloud is drawn ahead
        // of it twice over: the cloud is born a third of its final width, and the rise runs fivefold
        // fast while the front runs at the speed of sound. So while the front is inside the cloud's
        // farthest point, the whole cloud is scaled about the burst to fit it.
        //
        // About the burst, which for an air burst is up where the cap starts. And against the cloud
        // as the shader draws it rather than the upright shape: sheared downwind over the stand,
        // leaned, and with billows standing proud of the tube. A megatonne-class cloud is still
        // inside its front minutes in, and measured upright its downwind edge ran out ahead of it.
        double shock = front is { } f ? f.Radius(age) : ShockRadius(kt, age);
        double inside = InsideFront(cap, shock);

        // The front along the ground, which is what lifts the ring of dust: nothing until it has
        // come down the burst height, and then the circle where the sphere meets the ground.
        double onGround = shock > hob ? Math.Sqrt((shock * shock) - (hob * hob)) : 0.0;

        return new Shape(
            CapCentre: hob + ((capCentre - hob) * inside),
            CapRadius: capRadius * inside,
            CapTube: capTube * inside,
            StemTop: stemTop <= hob ? stemTop * inside : hob + ((stemTop - hob) * inside),
            StemRadius: capR * StemOfCap * widen * (1.0 - (AgedStemLoss * aged)) * inside,
            SurgeRadius: SurgeRadius(kt, age) * inside,
            SurgeHeight: SurgeHeight(kt, age) * inside,
            Roll: roll,
            Shock: onGround,
            Fade: Fade(age, kt),
            Coupling: GroundCoupling(kt, hob),
            StemShare: StemShare(kt, hob) * AsFarAsAirborne(GroundCoupling(kt, hob), ColumnFormed(kt, hob, age, front)),
            ColumnTop: AsFarAsAirborne(GroundCoupling(kt, hob), ColumnTopAt(kt, hob, age, underside, capCentre, front)));
    }

    // What only an air burst does -- a column that waits for the front and climbs -- as far as this one
    // is not on the ground: a store stops a metre over the terrain, and a burst drawing the ground into
    // its fireball is a surface burst whose column is there from the first instant.
    private static double AsFarAsAirborne(double coupling, double airborne)
        => airborne + ((1.0 - airborne) * Math.Clamp(coupling, 0.0, 1.0));

    // An air burst's column as a share of the cap's height: nothing until the front is at the ground,
    // then climbing to its reach of the cap's underside. A surface burst's always joins.
    private static double ColumnTopAt(double yieldKt, double burstHeight, double age, double underside,
                                      double capCentre, BlastFront? front)
    {
        if (!(burstHeight > 0.0) || !(capCentre > 0.0)) return 1.0;

        double reaches = GroundArrival(yieldKt, burstHeight, front);
        double climbed = Smoothstep(reaches, reaches + ColumnClimbSeconds, age);
        double reach = ColumnReach(yieldKt, burstHeight);
        if (reach >= 1.0 && climbed >= 1.0) return 1.0;

        return Math.Clamp(underside * reach * climbed / capCentre, 0.0, 1.0);
    }

    // A burst in air too thin for a column: its debris as one ball, climbing and swelling from the
    // fireball's own size, clean -- nothing on the ground is touched -- and fading as it spreads.
    private static Shape ThinBall(double kt, double age, double burstHeight, double airRatio)
    {
        double hob = Math.Max(double.IsFinite(burstHeight) ? burstHeight : 0.0, 0.0);
        double ball = FireballRadius(kt) * ThinAirGrowth(airRatio);
        double t = Math.Clamp(age / LifeFor(kt), 0.0, 1.0);

        double climb = ThinBallClimb * ball * Math.Sqrt(Progress(age));
        double tube = ball * (1.0 + ((ThinBallSwell - 1.0) * Math.Sqrt(t))) * Widening(age);

        return new Shape(
            CapCentre: hob + climb,
            CapRadius: tube,
            CapTube: tube,
            StemTop: 0.0,
            StemRadius: 0.0,
            SurgeRadius: 0.0,
            SurgeHeight: 0.0,
            Roll: 0.30 * (1.0 - Math.Exp(-2.0 * age / RiseSeconds)),
            Fade: Fade(age, kt),
            Shock: 0.0,
            Coupling: 0.0,
            StemShare: 0.0,
            ColumnTop: 1.0);
    }

    /// <summary>
    /// The least of its settled height a cloud still climbs, as a share, however high it went off.
    /// </summary>
    public const double MinClimbShare = 0.1;

    /// <summary>
    /// How far the shader's lean can carry a point, as a share of its height: 0.095 of the bound times
    /// the height's share of it to the 1.4, which never passes 0.095 of the height itself.
    /// </summary>
    public const double MostLean = 0.095;

    /// <summary>How far the shader's billows can stand proud of the cap's tube, in tube radii.</summary>
    public const double BillowReach = 0.75;

    /// <summary>
    /// How long an air burst's column of dust takes to rise once the front has reached the ground and
    /// the afterwinds begin: nothing before it, since there is no wind at the ground to lift anything.
    /// </summary>
    public static double ColumnFormsSeconds => RiseSeconds * 0.1;

    /// <summary>
    /// How far up its stroke a pen is still climbing the axis. Past this it is walking the cap, so
    /// it is also where the cap's own shape starts being measurable.
    /// </summary>
    public const double ClimbUntil = 0.15;

    /// <summary>
    /// How far above the cap's centre the drawn cloud reaches, in cap-tube radii: the shader's
    /// veil over the crown ends there.
    /// </summary>
    public const double CrownInTubes = 1.5;

    /// <summary>
    /// How much of the rise the cap takes to reach its full width, as a fraction. Short of the
    /// 0.63 at which a pen crosses the equator, because that is the one instant the cap's width is
    /// decided.
    /// </summary>
    public const double SpreadBy = 0.50;

    /// <summary>
    /// The share of its width the whole cloud -- cap, roll and stem -- is born with, before it widens
    /// on the rise's clock. Born at full width, the only thing holding the cloud in is the blast
    /// front, and a 0.3 kt cloud is two thirds of its final width two seconds in: an expansion far
    /// faster than the climb. With this it is 29%.
    /// </summary>
    public const double BornWidth = 0.35;

    /// <summary>How much of the rise the cloud takes to finish widening from <see cref="BornWidth"/>.</summary>
    public const double WidenBy = 0.50;

    /// <summary>
    /// The factor on the cloud's width at an age: <see cref="BornWidth"/> easing out to one, fast
    /// at first and slowing, as the climb does.
    /// </summary>
    public static double Widening(double age)
    {
        double x = Math.Clamp(age / (RiseSeconds * WidenBy), 0.0, 1.0);
        return BornWidth + ((1.0 - BornWidth) * (1.0 - ((1.0 - x) * (1.0 - x))));
    }

    // Under the tropopause the cap is taller than it is wide, which is the opposite of the anvil
    // everyone pictures and is what Glasstone's own two numbers say at these yields: a base at half the cloud top and a
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
    public static double StemCeiling(double capCentre, double capHeight)
        => capCentre - (capHeight * Oblate * 0.7);

    /// <summary>How far along its stroke a pen is at this age, in [0, 1].</summary>
    public static double Progress(double age) => Math.Clamp(age / RiseSeconds, 0.0, 1.0);

    /// <summary>How far through its stand the cloud is, eased, in [0, 1]: zero while it rises.</summary>
    public static double Aged(double age)
    {
        double t = Math.Clamp((age - RiseSeconds) / StandSeconds, 0.0, 1.0);
        return t * (2.0 - t);
    }

    // Thinning slowly through the stand, and then out over its last minute. Squared at the end so it
    // thins slowly at first and then goes, which is how a cloud disperses rather than how a light
    // switches off.
    private static double Fade(double age, double yieldKt)
    {
        double thinned = 1.0 - (AgedThinning * Aged(age));
        double outAt = LifeFor(yieldKt) - FadeOutSeconds;
        if (age <= outAt) return thinned;

        double left = 1.0 - Math.Clamp((age - outAt) / FadeOutSeconds, 0.0, 1.0);
        return thinned * left * left;
    }
}
