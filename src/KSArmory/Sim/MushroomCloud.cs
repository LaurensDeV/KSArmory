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

    /// <summary>Seconds the cloud takes to reach its ceiling. The real thing takes minutes.</summary>
    public const double RiseSeconds = 22.0;

    /// <summary>And how long it stands there before fading out.</summary>
    public const double StandSeconds = 40.0;

    /// <summary>Total life, after which there is nothing to draw.</summary>
    public const double LifeSeconds = RiseSeconds + StandSeconds;

    /// <summary>Kilotons of TNT equivalent for a charge in kg, which is what a profile carries.</summary>
    public static double KilotonsFor(double chargeKg) => chargeKg / 1.0e6;

    /// <summary>
    /// Fireball radius (m). Nuclear, so the 0.4 power rather than the cube root a chemical charge
    /// obeys — which is why this does not agree with <see cref="Warhead.FireballRadius"/>.
    /// </summary>
    public static double FireballRadius(double yieldKt)
        => yieldKt <= 0.0 ? 0.0 : 55.0 * Math.Pow(yieldKt, 0.4);

    /// <summary>Stabilised height of the cloud top (m).</summary>
    public static double CloudTop(double yieldKt)
        => yieldKt <= 0.0 ? 0.0 : 3000.0 * Math.Cbrt(yieldKt);

    /// <summary>Stabilised cap radius (m).</summary>
    public static double CapRadius(double yieldKt)
        => yieldKt <= 0.0 ? 0.0 : 600.0 * Math.Pow(yieldKt, 0.37);

    /// <summary>Seconds the fireball stays incandescent, after which it is lit smoke.</summary>
    public static double DarkAfter(double yieldKt)
        => yieldKt <= 0.0 ? 0.0 : 3.0 * Math.Pow(yieldKt, 0.4);

    /// <summary>
    /// The cloud at an age, in a frame whose <paramref name="up"/> is the local vertical.
    ///
    /// <para><paramref name="east"/> and <paramref name="north"/> only have to be perpendicular to
    /// up and to each other; which way they actually point does not matter to a shape with an axis
    /// of symmetry, and the caller is spared having to find true north.</para>
    /// </summary>
    public readonly record struct Shape(
        double CapCentre, double CapRadius, double CapTube,
        double StemTop, double StemRadius, double Roll, double Fade)
    {
        /// <summary>Nothing left to draw.</summary>
        public bool Spent => Fade <= 0.0;
    }

    /// <summary>Where the cloud is at <paramref name="age"/> seconds, for a charge in kg.</summary>
    public static Shape At(double chargeKg, double age)
    {
        double kt = KilotonsFor(chargeKg);
        if (kt <= 0.0 || age < 0.0 || age >= LifeSeconds) return default;

        double top = CloudTop(kt);
        double capR = CapRadius(kt);

        // Fast then asymptotic, which is what a buoyant parcel does as it entrains cooler air and
        // loses the density difference driving it. A linear rise reads as a lift rather than a
        // detonation.
        double rise = 1.0 - Math.Exp(-3.0 * Math.Min(age, RiseSeconds) / RiseSeconds);

        // The cap centre sits at three quarters of the top, because the cap has thickness: its base
        // is at half the cloud top and its crown is the top itself.
        double capCentre = top * 0.75 * rise;

        // The stem is not fireball. It is dirt lifted by afterwinds, so it starts later and climbs
        // at a fraction of the cap's rate, and it never catches up -- which is the cheapest cue
        // separating a mushroom from a plume.
        double stemAge = Math.Max(0.0, age - (RiseSeconds * 0.08));
        double stemRise = 1.0 - Math.Exp(-3.0 * Math.Min(stemAge, RiseSeconds) / RiseSeconds);
        double stemTop = Math.Min(capCentre, top * 0.55 * stemRise);

        // The cap widens as it rises and keeps widening after it stops, which is the lateral spread
        // that turns a ball into an anvil.
        double spread = 0.55 + (0.45 * Math.Min(1.0, age / RiseSeconds));

        // A slight twist and no more. The emitters drawing this are pens that keep everywhere they
        // have been, so a roll of any size draws a helix rather than a rolling cap -- eight of them
        // being a spiral staircase. The rollover has to come from the *path* shape below.
        double roll = 0.30 * (1.0 - Math.Exp(-2.0 * age / RiseSeconds));

        return new Shape(
            CapCentre: capCentre,
            CapRadius: capR * spread,
            CapTube: capR * 0.55,
            StemTop: stemTop,
            StemRadius: capR * 0.5 * 0.6,
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
    public static double3 CapPoint(in Shape shape, int index, int count, double progress,
                                   double shell, double3 up, double3 east, double3 north)
    {
        if (count <= 0) return Vec.Zero;

        double p = Math.Clamp(progress, 0.0, 1.0);

        // Up the axis first, arriving at the cap's height about two thirds of the way through.
        //
        // Inner strokes finish higher: a cap is a dome rather than a lid, and a ring of strokes all
        // levelling at one height draws a plate. A low-yield cap is about as tall as it is wide, so
        // the crown is a large part of the silhouette rather than a detail.
        double crown = 1.0 + ((1.0 - shell) * 0.55);
        double climb = shape.CapCentre * crown * Math.Min(1.0, p / 0.65);

        // Then out. Nothing until the climb is well under way, or the cloud is a cone from the
        // ground up rather than a column with a head on it.
        double out2 = shape.CapRadius * shell * Smooth(p, 0.45, 1.0);

        // And under at the very end, which is the lip of the cap and the whole silhouette.
        double droop = shape.CapTube * 1.35 * Smooth(p, 0.78, 1.0);

        double turn = (2.0 * Math.PI * index / count) + (shape.Roll * p);

        return (up * (climb - droop))
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
