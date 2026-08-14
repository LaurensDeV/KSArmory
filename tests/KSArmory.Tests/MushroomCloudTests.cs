using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The cloud's shape over time. These pin the ratios that make it read as a mushroom rather than
/// as a plume, which is the whole reason the choreography is here instead of in the drawing.
/// </summary>
public class MushroomCloudTests
{
    private const double Kt = 1.0e6;      // kg of TNT equivalent in a kilotonne

    /// <summary>
    /// The sizes are Glasstone's. Checked against the worked table in docs/NUCLEAR-EFFECT.md so a
    /// change to the laws has to be a deliberate one.
    ///
    /// <para>Ten percent, because the reference is not self-consistent to better than that: its
    /// cloud figures come from the cube-root form below ten kilotonnes and from the Fig 2.16
    /// polynomial above, and the two only agree to about a tenth where they overlap. A tighter
    /// tolerance would be pinning one source's rounding rather than the law.</para>
    /// </summary>
    [Theory]
    [InlineData(0.3, 41.0, 2010.0, 390.0)]
    [InlineData(1.5, 79.0, 3430.0, 700.0)]
    [InlineData(10.0, 168.0, 6460.0, 1410.0)]
    [InlineData(50.0, 320.0, 11040.0, 2350.0)]
    public void TheSizeLawsMatchTheReference(double kt, double fireball, double top, double cap)
    {
        // Within a few percent: the reference table is itself rounded.
        Assert.True(Math.Abs(MushroomCloud.FireballRadius(kt) - fireball) < fireball * 0.10,
                    $"fireball {MushroomCloud.FireballRadius(kt):F0} m against {fireball:F0}");
        Assert.True(Math.Abs(MushroomCloud.CloudTop(kt) - top) < top * 0.10,
                    $"cloud top {MushroomCloud.CloudTop(kt):F0} m against {top:F0}");
        Assert.True(Math.Abs(MushroomCloud.CapRadius(kt) - cap) < cap * 0.10,
                    $"cap {MushroomCloud.CapRadius(kt):F0} m against {cap:F0}");
    }

    /// <summary>
    /// The stem starts later and climbs slower, so it never reaches the cap. A stem that keeps up
    /// draws a column with a ball on it, which is a plume.
    /// </summary>
    [Fact]
    public void TheStemLagsTheCapAndNeverCatchesIt()
    {
        for (double age = 0.5; age < MushroomCloud.RiseSeconds; age += 0.5)
        {
            MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, age);
            Assert.True(s.StemTop < s.CapCentre,
                        $"at {age:F1} s the stem reached {s.StemTop:F0} m against a cap at {s.CapCentre:F0} m");
        }
    }

    /// <summary>The cap is twice the stem's width, which is Glasstone's ratio below 20 kt.</summary>
    [Fact]
    public void TheCapIsWiderThanTheStem()
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);
        Assert.True(s.CapRadius > s.StemRadius * 2.0,
                    $"cap {s.CapRadius:F0} m against stem {s.StemRadius:F0} m");
    }

    /// <summary>
    /// And it is exactly Glasstone's half, not a fraction of it. A thinner stem is the difference
    /// between a mushroom and a lollipop: the cap has nothing to sit on and reads as a blob on a
    /// stick, however right the cap itself is.
    /// </summary>
    [Fact]
    public void TheStemIsHalfTheCapRadius()
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);
        // Against the drawn cap rather than the law's: the ratio is Glasstone's, the size is not.
        Assert.Equal(MushroomCloud.DrawnCapRadius(0.3) * 0.5, s.StemRadius, 3);
    }

    /// <summary>
    /// No smoke exists while the fireball is still burning, and that is not a detail of timing.
    ///
    /// <para>Every pen starts at the burst point, so laying them from t=0 groups them into one ball
    /// several hundred metres across on the first frame — and the volumetric pass runs *after* the
    /// bloom pass, so that ball is drawn in front of the brightest thing the mod can produce. The
    /// flash is buried inside its own smoke and the burst reads as nothing happening.</para>
    /// </summary>
    [Fact]
    public void TheSmokeWaitsForTheFireballToGoDark()
    {
        double charge = 0.3 * Kt;
        double flash = MushroomCloud.FlashSeconds(0.3);

        for (double age = 0.0; age < flash; age += flash / 20.0)
        {
            Assert.False(MushroomCloud.FlashAt(charge, age).Spent, $"the flash is out at {age:F2} s");
            Assert.False(MushroomCloud.SmokeStarted(charge, age),
                         $"smoke is being laid at {age:F2} s, over a fireball that is still burning");
        }

        Assert.True(MushroomCloud.SmokeStarted(charge, flash + 0.01),
                    "and starts the moment the luminous phase is over");

        // The ember that follows deliberately overlaps it, which is not the case above: what has to
        // stay out from under the smoke is the flash, and by here the ball is orders of magnitude
        // under the bloom threshold and the same width as the smoke taking over from it.
        Assert.False(MushroomCloud.FlashAt(charge, flash + 0.01).Spent);
    }

    /// <summary>
    /// It accelerates, overshoots its ceiling and settles back, rather than easing into it. That is
    /// what a buoyant parcel does in a stratified atmosphere, and it is the difference between
    /// something thrown up by a detonation and something lifted on a rope.
    /// </summary>
    [Fact]
    public void ItOvershootsItsCeilingAndSettles()
    {
        double peak = 0.0;
        double peakAt = 0.0;

        for (double age = 0.0; age < MushroomCloud.LifeSeconds; age += 0.1)
        {
            double h = MushroomCloud.Rise(age);
            if (h > peak) { peak = h; peakAt = age; }
        }

        Assert.True(peak > 1.02, $"it should overshoot, peaked at {peak:F3}");
        Assert.True(peak < 1.25, $"but not bounce, peaked at {peak:F3}");
        Assert.True(peakAt > MushroomCloud.RiseSeconds * 0.6,
                    "and the apex should be late in the rise, not at the start");

        // Still moving well after the rise, which is what stops the whole cloud freezing at once.
        double settling = MushroomCloud.Rise(MushroomCloud.RiseSeconds * 1.3);
        Assert.True(Math.Abs(settling - 1.0) > 0.01, "it should still be settling after the rise");

        // And it does not start at full speed, the way a first-order lag does.
        Assert.True(MushroomCloud.Rise(0.5) < 0.06, "it should accelerate rather than leap");
    }

    /// <summary>
    /// The roll decays to a stop: entrained air cools the toroid and kills the circulation as it
    /// nears its ceiling. A cap still spinning at the end reads as a special effect.
    /// </summary>
    [Fact]
    public void TheToroidalRollSlowsAsItReachesTheCeiling()
    {
        double a = MushroomCloud.At(0.3 * Kt, 2.0).Roll;
        double b = MushroomCloud.At(0.3 * Kt, 6.0).Roll;
        double c = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds).Roll;

        // Rates, not differences: the two intervals are different lengths, and comparing raw
        // deltas across them says more about the sampling than about the roll.
        double early = (b - a) / 4.0;
        double late = (c - b) / (MushroomCloud.RiseSeconds - 6.0);

        Assert.True(early > late, $"the roll should be slowing: {early:F4} then {late:F4} rad/s");
    }

    /// <summary>Nothing is drawn before it exists or after it has gone.</summary>
    [Fact]
    public void ItIsSpentOutsideItsLife()
    {
        Assert.True(MushroomCloud.At(0.3 * Kt, -1.0).Spent);
        Assert.True(MushroomCloud.At(0.3 * Kt, MushroomCloud.LifeSeconds + 1.0).Spent);
        Assert.False(MushroomCloud.At(0.3 * Kt, 1.0).Spent);
    }

    /// <summary>
    /// The flash is brightest at the instant of the burst and is essentially over long before the
    /// ball stops being visible. A slow fade reads as a lamp being turned down.
    /// </summary>
    [Fact]
    public void TheFlashIsBrightestAtTheStartAndCollapses()
    {
        double dark = MushroomCloud.DarkAfter(0.3);

        double at0 = MushroomCloud.FlashAt(0.3 * Kt, 0.0).Glow;
        double atTenth = MushroomCloud.FlashAt(0.3 * Kt, dark * 0.1).Glow;
        double atHalf = MushroomCloud.FlashAt(0.3 * Kt, dark * 0.5).Glow;

        Assert.True(at0 > atTenth && atTenth > atHalf, "it should be falling throughout");
        Assert.True(atHalf < at0 * 0.2, "and be a fifth of its peak by halfway");
        Assert.True(MushroomCloud.FlashAt(0.3 * Kt, dark + MushroomCloud.EmberSeconds + 0.1).Spent,
                    "and gone once the ember it leaves has gone out");
    }

    /// <summary>
    /// Bright enough to bloom, which is a threshold rather than a preference: under it the engine
    /// discards the pixel from the bloom pass and the burst is merely a pale ball.
    /// </summary>
    [Fact]
    public void TheFlashIsBrightEnoughToBloom()
    {
        MushroomCloud.Flash f = MushroomCloud.FlashAt(0.3 * Kt, 0.0);

        // The same Rec.709 luminance the bloom pass tests, times the albedo the shader samples.
        double lum = (0.2126 * f.Colour.X) + (0.7152 * f.Colour.Y) + (0.0722 * f.Colour.Z);
        double luminance = 0.4535 * lum * f.Glow;

        // By a wide margin, not merely over the line. Clearing the threshold nine times over draws a
        // bright lamp; a fireball's surface is twice the radiance of the sun's, and how far past the
        // threshold it sits is what decides how far the flare spreads — which at these yields is the
        // whole read, because the ball itself is 45 m and a couple of pixels.
        Assert.True(luminance > 50.0 * 3.0,
                    $"peak luminance {luminance:F0} is only {luminance / 3.0:F0}x the bloom "
                    + "threshold, which reads as a lamp rather than as a detonation");
    }

    /// <summary>
    /// It is at full size almost immediately. The growth is real but it is over in under a fifth of
    /// a second, so a ramp stretched across a quarter of the luminous phase means most of what
    /// anyone actually sees is an undersized ball.
    /// </summary>
    [Fact]
    public void TheFireballIsAtFullSizeAlmostImmediately()
    {
        double flash = MushroomCloud.FlashSeconds(0.3);
        double peak = MushroomCloud.PeakFireballRadius(0.3);

        // Within the contraction it has already begun by then, and no less.
        Assert.True(MushroomCloud.FlashAt(0.3 * Kt, flash * 0.15).Radius > peak * 0.95,
                    "it should be at full size a sixth of the way through the flash");
        Assert.True(MushroomCloud.FlashAt(0.3 * Kt, 0.0).Radius > peak * 0.5,
                    "and it does not start from nothing");
    }

    /// <summary>
    /// It cools rather than fading: white-hot, then orange, then deep red. The blue channel falling
    /// away fastest is what makes that read as temperature instead of as a colour wash.
    /// </summary>
    [Fact]
    public void TheFireballCoolsFromWhiteThroughOrangeToRed()
    {
        double dark = MushroomCloud.DarkAfter(0.3);

        double3 hot = MushroomCloud.FlashAt(0.3 * Kt, 0.0).Colour;
        double3 cool = MushroomCloud.FlashAt(0.3 * Kt, dark * 0.9).Colour;

        Assert.True(hot.Z > 0.8, "it should start near white");
        Assert.True(cool.Z < 0.2, "and end deep red");
        Assert.True(cool.X > cool.Y && cool.Y > cool.Z, "red over green over blue, all the way down");
    }

    /// <summary>
    /// The ball contracts as it cools, gently while it burns and hard once it is an ember.
    ///
    /// <para><b>What contracts is the incandescent region, not the fireball.</b> The hot air mass
    /// keeps growing the whole time; its outer skin cools below visible emission first, so the part
    /// that glows shrinks inward while the part that exists does not. That is how this squares with
    /// a law saying a fireball only ever grows, and it is what lets the ball recede into its own
    /// smoke rather than being switched off inside it.</para>
    /// </summary>
    [Fact]
    public void TheGlowingBallContractsAsItCools()
    {
        double flash = MushroomCloud.FlashSeconds(0.3);
        double peak = MushroomCloud.PeakFireballRadius(0.3);

        double early = MushroomCloud.FlashAt(0.3 * Kt, flash * 0.3).Radius;
        double late = MushroomCloud.FlashAt(0.3 * Kt, flash * 0.9).Radius;
        double out_ = MushroomCloud.FlashAt(0.3 * Kt, flash + (MushroomCloud.EmberSeconds * 0.9)).Radius;

        Assert.True(late < early, $"it should contract while burning: {early:F0} m to {late:F0} m");
        Assert.True(late > peak * 0.7, "but only gently -- it is still a fireball, not a spark");
        Assert.True(out_ < late * 0.4, $"and then hard, into the cloud: {late:F0} m to {out_:F0} m");

        // Bigger than the free-air law throughout, because it is sitting on the ground: the
        // reflected energy grows the upper hemisphere as though the device were twice the size.
        Assert.True(Math.Abs(MushroomCloud.SurfaceBurstGain - 1.32) < 0.01,
                    $"the surface gain should be 2^0.4, not {MushroomCloud.SurfaceBurstGain:F3}");
    }

    /// <summary>
    /// The ground skirt stays inside the cap and is drawn back in, which is what separates a land
    /// burst from a water one.
    ///
    /// <para>A dense ring rolling outward past the cloud's own width is the base surge, and the base
    /// surge is spray thrown off a collapsing column of <em>water</em>. On land the afterwinds blow
    /// inward along the ground to feed the stem, so the dust the blast threw out is pulled back to
    /// the axis and lifted. Draw it running outward and every viewer reads the burst as happening at
    /// sea.</para>
    /// </summary>
    [Fact]
    public void TheGroundSkirtStaysInsideTheCapAndIsDrawnBackIn()
    {
        double capR = MushroomCloud.CapRadius(0.3);

        double peak = 0.0;
        double peakAt = 0.0;

        for (double age = 0.0; age < MushroomCloud.LifeSeconds; age += 0.1)
        {
            double r = MushroomCloud.SurgeRadius(0.3, age);
            if (r > peak) { peak = r; peakAt = age; }
        }

        Assert.True(peak < capR * 0.75,
                    $"the skirt reached {peak:F0} m against a {capR:F0} m cap, which is a water burst");
        Assert.True(peakAt < MushroomCloud.RiseSeconds * 0.5,
                    $"the blast drives it and nothing sustains it, so it should be widest early, "
                    + $"not at {peakAt:F1} s");

        double settled = MushroomCloud.SurgeRadius(0.3, MushroomCloud.LifeSeconds);
        Assert.True(settled < peak * 0.8,
                    $"it should be drawn back in: {peak:F0} m at {peakAt:F1} s, "
                    + $"still {settled:F0} m at the end");
        Assert.True(settled > 0.0, "but not vanish -- it settles as the collar round the stem");

        // And it stays low. A skirt that climbs is a second stem.
        Assert.True(MushroomCloud.SurgeHeight(0.3, MushroomCloud.RiseSeconds)
                    < MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds).StemTop * 0.5,
                    "the skirt should stay well under the stem it stands beside");
    }

    /// <summary>
    /// The skirt starts as one ball of dust on the burst, blooms outward into a ring, and then stays
    /// a ring for the rest of its life.
    ///
    /// <para>The engine merges pens sitting within 0.4 of an expanded radius of their centroid and
    /// draws one ball of <c>cbrt(count)</c> radii instead, so a ring survives only while its radius
    /// exceeds <c>0.5 · cbrt(count) · tube</c>. Being under that on the way out is wanted; crossing
    /// back over it later is not, because each break discards the trail the merged chain was
    /// holding.</para>
    /// </summary>
    [Fact]
    public void TheGroundSkirtBloomsFromOneBallAndStaysARing()
    {
        double handover = MushroomCloud.TubeAtHandover(0.3);

        double Threshold(in MushroomCloud.Shape s, double age)
            => 0.5 * Math.Cbrt(MushroomCloud.SurgeStrands)
                   * MushroomCloud.SurgeTube(s, MushroomCloud.Progress(age), handover);

        // Its tube starts at the handover width like everything else, so the skirt is part of the
        // one ball the burst hands over as rather than a ring of its own arriving beside it. That is
        // TubeAtHandover's property to hold, not this one's.
        Assert.Equal(handover, MushroomCloud.SurgeTube(MushroomCloud.At(0.3 * Kt, 0.0), 0.0, handover), 6);

        for (double age = 3.0; age < MushroomCloud.LifeSeconds; age += 0.5)
        {
            MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, age);
            Assert.True(s.SurgeRadius > Threshold(s, age),
                        $"at {age:F1} s the skirt is back inside the merge radius and will flicker");
        }
    }

    /// <summary>
    /// The smoke takes over at the fireball's own width, whatever the yield and however many pens
    /// are bunched on the burst point.
    ///
    /// <para>This is the whole of the handover. The pens all start at the burst, where the engine
    /// merges them into one ball of <c>cbrt(count)</c> radii; laid at the cap's full tube that ball
    /// is eight times the fireball and it arrives in a single frame, so the burst does not become
    /// the cloud, it is replaced by one that is then judged far too big for the flash that preceded
    /// it.</para>
    /// </summary>
    [Theory]
    [InlineData(0.3)]
    [InlineData(50.0)]
    [InlineData(340.0)]
    public void TheSmokeTakesOverAtTheFireballsWidth(double kt)
    {
        // One radius, not one divided by the pen count. The pens are coincident on the axis while
        // they climb and the raymarcher takes the deeper of two overlapping capsules rather than
        // summing them, so any number of them at radius r render as one tube of radius r.
        Assert.Equal(MushroomCloud.PeakFireballRadius(kt), MushroomCloud.TubeAtHandover(kt), 6);
    }

    /// <summary>
    /// And swells to full before the pens begin to spread, because the coverage rule keeping the rim
    /// closed is written against the full tube. A thin pen out on the rim is a rope.
    /// </summary>
    [Fact]
    public void ThePensReachFullWidthBeforeTheySpread()
    {
        Assert.Equal(0.0, MushroomCloud.TubeGrowth(0.0), 6);

        // 0.45 is where CapPoint starts moving a pen off the axis.
        Assert.Equal(1.0, MushroomCloud.TubeGrowth(0.45), 6);

        double last = -1.0;
        for (double p = 0.0; p <= 1.0; p += 0.01)
        {
            double now = MushroomCloud.TubeGrowth(p);
            Assert.True(now >= last, $"the tube should only ever grow, and shrank at {p:F2}");
            last = now;
        }
    }

    /// <summary>
    /// The ball goes out rather than being deleted. It is removed on the one frame the smoke is
    /// taking over from it, which is the single instant the eye is watching for continuity, so a cut
    /// there is worth more than the ember costs.
    /// </summary>
    [Fact]
    public void TheFireballDimsOutRatherThanVanishing()
    {
        double flash = MushroomCloud.FlashSeconds(0.3);

        // Still there once the luminous phase is over, and still the size and colour it ended at.
        MushroomCloud.Flash dark = MushroomCloud.FlashAt(0.3 * Kt, flash * 1.05);
        MushroomCloud.Flash end = MushroomCloud.FlashAt(0.3 * Kt, flash * 0.999);

        Assert.False(dark.Spent, "it should still be drawn as the smoke takes over");
        Assert.True(dark.Radius < end.Radius,
                    "and shrink into the cloud rather than hang in it at full size");

        // Above the bloom threshold for the whole ember, and that is not a preference. Over it the
        // pass spreads the sphere into glare and what is drawn is light; under it the pixel is
        // discarded and the same sphere is drawn as ordinary shaded geometry, which reads as a ball.
        for (double age = flash; age < flash + MushroomCloud.EmberSeconds; age += 0.1)
        {
            MushroomCloud.Flash f = MushroomCloud.FlashAt(0.3 * Kt, age);
            double lum = (0.2126 * f.Colour.X) + (0.7152 * f.Colour.Y) + (0.0722 * f.Colour.Z);

            Assert.True(0.4535 * lum * f.Glow > 3.0,
                        $"at {age:F1} s the ember is at {0.4535 * lum * f.Glow:F1} against a bloom "
                        + "threshold of 3, so it stops being light and becomes a shaded ball");
        }

        // And nowhere near a second flash.
        Assert.True(MushroomCloud.FlashAt(0.3 * Kt, flash * 1.05).Glow
                    < MushroomCloud.FlashAt(0.3 * Kt, 0.0).Glow * 0.1);

        Assert.True(MushroomCloud.FlashAt(0.3 * Kt, flash + MushroomCloud.EmberSeconds + 0.01).Spent,
                    "and be gone at the end of it");

        // And it climbs the whole time it is there, on the same curve the pens do, rather than
        // hanging where it burst while the cloud leaves without it.
        double first = MushroomCloud.AxisHeight(
            MushroomCloud.At(0.3 * Kt, flash), MushroomCloud.Progress(flash), 1.0);
        double last = MushroomCloud.AxisHeight(
            MushroomCloud.At(0.3 * Kt, flash + MushroomCloud.EmberSeconds),
            MushroomCloud.Progress(flash + MushroomCloud.EmberSeconds), 1.0);

        Assert.True(last > first * 3.0,
                    $"the ember should ride the cloud up: {first:F0} m to {last:F0} m");
    }

    /// <summary>
    /// The flash never outlasts the cloud it made. The luminous phase is real time and the rise is
    /// compressed, so the two diverge as the dial climbs: at the top of the B61's range the law
    /// gives 30.9 s of glow against a 22 s rise, which is a burst still flaring after its own
    /// mushroom has finished forming.
    /// </summary>
    [Theory]
    [InlineData(0.3)]
    [InlineData(50.0)]
    [InlineData(340.0)]      // the top of the dial
    public void TheFlashNeverOutlastsTheRise(double kt)
    {
        Assert.True(MushroomCloud.FlashSeconds(kt) < MushroomCloud.RiseSeconds * 0.5,
                    $"{kt} kt glows for {MushroomCloud.FlashSeconds(kt):F1} s against a "
                    + $"{MushroomCloud.RiseSeconds:F0} s rise");

        // And the law is left alone: it is Glasstone's, and the holding back belongs to the clock.
        Assert.True(MushroomCloud.FlashSeconds(kt) <= MushroomCloud.DarkAfter(kt));
    }

    /// <summary>
    /// The flash always ends while the pens are still climbing the axis, at every yield on the dial.
    ///
    /// <para><b>This is the rule that keeps the cloud in one piece.</b> No smoke is laid while the
    /// ball is luminous, so a flash outlasting the climb means the pens are already out on the cap
    /// when they lay their first segment — and the column from the ground up is never drawn at all.
    /// The cap then arrives disconnected from its own stem and base, which is invisible at the
    /// bottom of the dial where the flash is short anyway, and unmissable at the top.</para>
    /// </summary>
    [Theory]
    [InlineData(0.3)]
    [InlineData(1.5)]
    [InlineData(10.0)]
    [InlineData(50.0)]
    [InlineData(340.0)]
    public void TheFlashEndsWhileThePensAreStillClimbing(double kt)
    {
        double climbEnds = MushroomCloud.RiseSeconds * MushroomCloud.ClimbUntil;

        Assert.True(MushroomCloud.FlashSeconds(kt) < climbEnds,
                    $"{kt} kt flashes for {MushroomCloud.FlashSeconds(kt):F1} s against a climb that "
                    + $"is over at {climbEnds:F1} s, so the cloud's lower body is never drawn");
    }

    /// <summary>
    /// The cap reaches its full width, walked against a shape that grows the way the real one does.
    ///
    /// <para>A pen crosses the equator — the widest point of its own stroke — once, at about 0.63,
    /// and then tucks under. So the width it finds *there* is the width the cap keeps, and any
    /// widening the shape does afterwards is drawn by nothing. Every other test here walks a frozen
    /// shape and cannot see that: this one steps the age.</para>
    /// </summary>
    [Fact]
    public void TheCapReachesItsFullWidthWhileThePensCanStillFindIt()
    {
        double3 up = new(0, 0, 1), east = new(1, 0, 0), north = new(0, 1, 0);
        double Radial(double3 v) => Math.Sqrt((v.X * v.X) + (v.Y * v.Y));

        double widest = 0.0;
        for (double age = 0.0; age <= MushroomCloud.RiseSeconds; age += 0.05)
        {
            MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, age);
            widest = Math.Max(widest, Radial(MushroomCloud.CapPoint(
                s, 0, 18, MushroomCloud.Progress(age), 1.0, up, east, north)));
        }

        double full = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds).CapRadius;

        Assert.True(widest > full * 0.95,
                    $"the cap only ever reaches {widest:F0} m against the {full:F0} m it is sized "
                    + "for, because the pens pass its widest point before it has finished growing");
    }

    /// <summary>Small yields are untouched by that ceiling — they are watchable as they are.</summary>
    [Fact]
    public void TheShippedYieldFlashesForItsRealDuration()
    {
        double kt = MushroomCloud.KilotonsFor(Arsenal.NukeB61.ChargeKg);
        Assert.Equal(MushroomCloud.DarkAfter(kt), MushroomCloud.FlashSeconds(kt), 6);
    }

    /// <summary>A conventional charge grows no cloud at all, however the caller is feeling.</summary>
    [Fact]
    public void AChemicalChargeHasNoCloud()
    {
        Assert.True(Arsenal.BombMk82.ChargeKg < MushroomCloud.ThresholdKg);
        Assert.True(Arsenal.NukeB61.ChargeKg > MushroomCloud.ThresholdKg);
    }

    /// <summary>
    /// The pens ring the axis, evenly spread, and the stroke reaches the cap's full radius on the
    /// way round rather than ending there: the last stretch is the lip coming back in underneath.
    /// </summary>
    [Fact]
    public void TheCapPointsRingTheAxis()
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);
        double3 up = new(0, 0, 1), east = new(1, 0, 0), north = new(0, 1, 0);
        double Radial(double3 v) => Math.Sqrt((v.X * v.X) + (v.Y * v.Y));

        const int Count = 8;
        var seen = new List<double3>();

        // The cap radius is a bound rather than an average: the lobe only ever pulls a pen in, so
        // the widest bearing reaches it and no bearing passes it.
        double widest = 0.0;
        for (int i = 0; i < Count; i++)
        {
            for (double p = 0.0; p <= 1.0; p += 0.005)
            {
                widest = Math.Max(widest,
                                  Radial(MushroomCloud.CapPoint(s, i, Count, p, 1.0, up, east, north)));
            }
        }

        Assert.True(widest <= s.CapRadius * 1.0001,
                    $"the stroke reaches {widest:F1} m outside a {s.CapRadius:F1} m cap");
        Assert.True(widest > s.CapRadius * (1.0 - MushroomCloud.CapLobeDepth),
                    $"and only {widest:F1} m of it, so the cap never reaches its own radius");

        for (int i = 0; i < Count; i++)
        {
            double3 at = MushroomCloud.CapPoint(s, i, Count, 1.0, 1.0, up, east, north);

            foreach (double3 other in seen)
            {
                Assert.True(Vec.Len(at - other) > s.CapRadius * 0.2, "cap emitters should not bunch");
            }

            seen.Add(at);
        }
    }

    /// <summary>
    /// The cap is at least as tall as it is wide, and that is Glasstone rather than taste: a base at
    /// half the cloud top under a crown at the cloud top is 1004 m of cap over a 769 m width at
    /// 0.3 kt. Drawn flatter it is a lampshade, and it was: a plate at one height with a lip hanging
    /// off its edge and a small ball perched on the axis above it, which is a shade and a finial.
    /// </summary>
    [Fact]
    public void TheCapIsTallerThanItIsWide()
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);
        double3 up = new(0, 0, 1), east = new(1, 0, 0), north = new(0, 1, 0);
        double Radial(double3 v) => Math.Sqrt((v.X * v.X) + (v.Y * v.Y));

        double low = double.MaxValue, high = double.MinValue, wide = 0.0;

        // Every ring, since the silhouette is the outermost of all of them.
        foreach (double shell in new[] { 1.0, 0.62, 0.18 })
        {
            for (double p = MushroomCloud.ClimbUntil; p <= 1.0; p += 0.005)
            {
                double3 at = MushroomCloud.CapPoint(s, 0, 8, p, shell, up, east, north);
                low = Math.Min(low, at.Z);
                high = Math.Max(high, at.Z);
                wide = Math.Max(wide, Radial(at));
            }
        }

        Assert.True(high - low >= 2.0 * wide,
                    $"the cap is {high - low:F0} m tall against {2.0 * wide:F0} m wide, which is a lid");
    }

    /// <summary>
    /// A pen climbs before it spreads, but only briefly, and the difference is the whole read.
    ///
    /// <para>Flaring from the ground draws a cone. Climbing for half the stroke and flaring after
    /// draws something worse: a pillar that extends, and then a mushroom that appears on the end of
    /// it, because the pens keep every position they have held and the climb <em>is</em> a column.
    /// The cap has to be opening while the thing is still rising, which means the climb is a short
    /// opening move and the stroke is mostly cap.</para>
    /// </summary>
    [Fact]
    public void APenClimbsBeforeItFlares()
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);
        double3 up = new(0, 0, 1), east = new(1, 0, 0), north = new(0, 1, 0);
        double Radial(double3 v) => Math.Sqrt((v.X * v.X) + (v.Y * v.Y));

        double3 early = MushroomCloud.CapPoint(s, 0, 8, MushroomCloud.ClimbUntil * 0.6, 1.0,
                                               up, east, north);

        Assert.True(early.Z > s.CapCentre * 0.35, "it should be well up the axis while it climbs");
        Assert.True(Radial(early) < s.CapRadius * 0.05, "and barely off it");

        // And the climb is a small part of the stroke, not most of it.
        Assert.True(MushroomCloud.ClimbUntil < 0.25,
                    $"the pens climb for {MushroomCloud.ClimbUntil:P0} of the stroke, which draws a "
                    + "pillar that a mushroom then appears on top of");

        // Which means the cap is already opening while the cloud is still well short of its ceiling.
        double opening = Radial(MushroomCloud.CapPoint(s, 0, 8, 0.35, 1.0, up, east, north));
        Assert.True(opening > s.CapRadius * 0.1,
                    $"a third of the way through the rise the cap has only opened to {opening:F0} m");
    }

    /// <summary>
    /// The lip curls back down. That overhang is the mushroom's silhouette, and without it the
    /// shape is a tree.
    /// </summary>
    [Fact]
    public void TheCapLipCurlsUnder()
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);
        double3 up = new(0, 0, 1), east = new(1, 0, 0), north = new(0, 1, 0);

        double highest = MushroomCloud.CapPoint(s, 0, 8, 0.78, 1.0, up, east, north).Z;
        double lip = MushroomCloud.CapPoint(s, 0, 8, 1.0, 1.0, up, east, north).Z;

        Assert.True(lip < highest, $"the lip at {lip:F0} m should hang below the crown at {highest:F0} m");
    }

    /// <summary>
    /// A ring of pens has to close into a surface, and that is arithmetic rather than taste: a
    /// capsule of radius R is solid only within 0.55 R, so pens spaced further apart than 1.1 R
    /// leave clear air between them and the cloud reads as ropes.
    ///
    /// <para>This is the rule the first two attempts broke — eight cap pens 300 m apart with 80 m
    /// tubes, then four stem pens at 144 m with 94 m tubes, which is where the pillars came
    /// from.</para>
    /// </summary>
    [Theory]
    [InlineData(18, 0.65)]      // the rim, at CapExpanded
    public void ARingOfPensCloses(int count, double tubeFraction)
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);

        double tube = s.CapTube * tubeFraction;

        // Against the circle the pens actually walk, which is what NuclearClouds.Ring hands them:
        // they stay inside the silhouette by their own radius, so measuring at the cap's full width
        // overstates the pitch by a third.
        double pitch = MushroomCloud.RingPitch(s with { CapRadius = MushroomCloud.PathRadius(s, tube) },
                                               count);

        Assert.True(pitch <= 1.1 * tube,
                    $"{count} pens give a {pitch:F0} m pitch against a {tube:F0} m tube; "
                    + $"needs {1.1 * tube:F0} m or less, or it reads as strands");
    }

    /// <summary>
    /// The fireball has lifted off the ground before the smoke takes over, and the smoke takes over
    /// where the fireball <em>is</em> rather than where the burst was.
    ///
    /// <para>This is the difference between a burst that turns into a cloud and one that is followed
    /// by a separate cloud. Restarting the cloud's clock at the handover puts every pen back on the
    /// ground at the instant the flash dies, and what that draws is a flash, and then, seconds
    /// later, columns of smoke climbing out of the ground underneath where the ball had got to. One
    /// clock from the burst is what keeps them the same object.</para>
    /// </summary>
    [Fact]
    public void TheFireballLiftsOffAndTheSmokeTakesOverWhereItIs()
    {
        double charge = 0.3 * Kt;
        double flash = MushroomCloud.FlashSeconds(0.3);

        MushroomCloud.Shape at = MushroomCloud.At(charge, flash);
        double lift = MushroomCloud.AxisHeight(at, MushroomCloud.Progress(flash), 1.0);

        // Off the ground by more than its own radius, or it has not visibly left at all.
        Assert.True(lift > MushroomCloud.PeakFireballRadius(0.3),
                    $"the ball is still only {lift:F0} m up when the smoke takes over, against a "
                    + $"{MushroomCloud.PeakFireballRadius(0.3):F0} m radius");

        // And the first thing the pens lay is up there with it, not back down at the burst.
        double3 up = new(0, 0, 1), east = new(1, 0, 0), north = new(0, 1, 0);
        double first = MushroomCloud.CapPoint(at, 0, 18, MushroomCloud.Progress(flash), 1.0,
                                              up, east, north).Z;

        Assert.True(Math.Abs(first - lift) < MushroomCloud.PeakFireballRadius(0.3),
                    $"the smoke starts at {first:F0} m against a ball at {lift:F0} m, so the cloud "
                    + "is a separate object growing out of the ground");

        // The stem is what reaches back down to the ground, and it lags on purpose.
        Assert.True(at.StemTop < lift, "the stem should be dragged up behind the ball, not lead it");
    }

    /// <summary>
    /// A lobed ring still closes, which is the whole limit on how far out of round anything may go.
    ///
    /// <para>Pulling one pen in and leaving its neighbour out separates the two <em>radially</em>, on
    /// top of the pitch already between them. Coverage is written against the straight-line gap, so
    /// the depth that reads as pleasantly lumpy and the depth at which the cap comes apart into
    /// ropes are only a little way apart, and the difference is not visible in any preview.</para>
    /// </summary>
    [Fact]
    public void ALobedRingStillCloses()
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);
        double3 up = new(0, 0, 1), east = new(1, 0, 0), north = new(0, 1, 0);

        const int Count = 18;
        double tube = s.CapTube * 0.65;
        MushroomCloud.Shape walked = s with { CapRadius = MushroomCloud.PathRadius(s, tube) };

        // Along the whole stroke, not only at the equator: the pens are furthest apart where the
        // ring is widest, but the lobe moves that point around.
        for (double p = MushroomCloud.ClimbUntil; p <= 1.0; p += 0.01)
        {
            for (int i = 0; i < Count; i++)
            {
                double3 a = MushroomCloud.CapPoint(walked, i, Count, p, 1.0, up, east, north);
                double3 b = MushroomCloud.CapPoint(walked, (i + 1) % Count, Count, p, 1.0,
                                                   up, east, north);

                Assert.True(Vec.Len(a - b) <= 1.1 * tube,
                            $"pens {i} and {i + 1} are {Vec.Len(a - b):F0} m apart at p={p:F2}, "
                            + $"against a {tube:F0} m tube; needs {1.1 * tube:F0} m or the cap "
                            + "comes apart into ropes");
            }
        }
    }

    /// <summary>
    /// A cap is a dome about as tall as it is wide at these yields, not a plate. The flat anvil
    /// everyone pictures is megaton-scale and mostly later-time spreading.
    /// </summary>
    [Fact]
    public void TheCapIsADomeRatherThanALid()
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);
        double3 up = new(0, 0, 1), east = new(1, 0, 0), north = new(0, 1, 0);

        double crown = MushroomCloud.CapPoint(s, 0, 8, 1.0, 0.55, up, east, north).Z;
        double rim = MushroomCloud.CapPoint(s, 0, 8, 1.0, 1.0, up, east, north).Z;

        Assert.True(crown > rim, $"the crown at {crown:F0} m should stand above the rim at {rim:F0} m");
    }

    /// <summary>
    /// The cap is flatter than a hemisphere. A dome of radius R over a rim of radius R puts its
    /// apex exactly R above that rim, and a real cap is oblate, so the crown standing off by more
    /// than the cap's own radius is a spire rather than a dome.
    ///
    /// <para>This is the arrowhead, and it is the defect the first attempt shipped: a 382 m rim at
    /// 1332 m with the inner shell 400 m above it. Every stroke inside the rim is narrower, so a gap
    /// leaves a narrow shape standing over a wide one with clear air in its neck, and the whole
    /// cloud reads as an arrow rather than as a mushroom.</para>
    /// </summary>
    [Fact]
    public void TheCapIsFlatterThanAHemisphere()
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);
        double3 up = new(0, 0, 1), east = new(1, 0, 0), north = new(0, 1, 0);

        double crown = MushroomCloud.CapPoint(s, 0, 8, 1.0, 0.18, up, east, north).Z;
        double rim = MushroomCloud.CapPoint(s, 0, 8, 1.0, 1.0, up, east, north).Z;

        Assert.True(crown - rim < s.CapRadius,
                    $"the crown stands {crown - rim:F0} m over a {s.CapRadius:F0} m rim, which is "
                    + "steeper than a hemisphere and reads as an arrowhead");
    }

    /// <summary>
    /// The pens must not wind round the axis. They are trails that keep every position they have
    /// held, so a full turn draws a helix and a ring of them is a spiral staircase rather than a
    /// cloud. This is the shape that shipped once.
    /// </summary>
    [Fact]
    public void APenDoesNotWindAroundTheAxis()
    {
        for (double age = 0.0; age < MushroomCloud.RiseSeconds; age += 0.25)
        {
            MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, age);
            Assert.True(Math.Abs(s.Roll) < 0.6,
                        $"at {age:F1} s the roll is {s.Roll:F2} rad, which starts to wind");
        }
    }
}
