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
    /// The cap is several times the width of the column under it, which is the strongest shape cue
    /// the drawing has. A photographed mushroom runs about five or six to one; at the half this
    /// used to hold it was barely two, and the silhouette stopped reading as a mushroom at all.
    ///
    /// <para>That half was a pen-era compromise rather than a measurement: a stem drawn as a bundle
    /// of capsules needs width to read as anything, and a raymarched one does not. Still bounded at
    /// the far end, because the concern it was guarding against is real — a stem thin enough to be a
    /// stick gives a lollipop, a cap with nothing under it.</para>
    /// </summary>
    [Fact]
    public void TheCapIsSeveralTimesTheWidthOfItsStem()
    {
        MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds);

        Assert.True(s.StemRadius > 0.0);
        Assert.InRange(s.CapRadius / s.StemRadius, 4.0, 6.5);
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

        // And it climbs the whole time it is there, rather than hanging where it burst while the
        // cloud leaves without it. On its own law rather than the pens', because the cloud's climb
        // carries a lit ball out of its own smoke -- see MushroomCloud.EmberHeight.
        double first = MushroomCloud.EmberHeight(0.3 * Kt, flash);
        double last = MushroomCloud.EmberHeight(0.3 * Kt, flash + MushroomCloud.EmberSeconds);

        Assert.True(last > first * 2.0,
                    $"the ember should still be lifting: {first:F0} m to {last:F0} m");

        // ...and stop, which is the half that stops it reading as a flare.
        Assert.Equal(MushroomCloud.EmberLiftRadii * MushroomCloud.PeakFireballRadius(0.3), last, 3);
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
        Assert.True(BuiltIns.Missile57E6.ChargeKg < MushroomCloud.ThresholdKg);
        Assert.True(Arsenal.NukeB61.ChargeKg > MushroomCloud.ThresholdKg);
    }

    /// <summary>
    /// The fireball goes dark while it is still near where it burst.
    ///
    /// <para>A real one lifts off and is hidden by its own cloud within a couple of diameters. This
    /// one is an emissive sphere drawn over the smoke rather than inside it, so for as long as it is
    /// lit it is the brightest thing in the frame — and a lit ball climbing hundreds of metres reads
    /// as a flare ascending, not as a burst dying.</para>
    ///
    /// <para>Measured in its own radii so it holds at every yield, which is the only way to state it:
    /// the absolute height varies fifteen-fold across the range, the proportion does not.</para>
    /// </summary>
    [Theory]
    [InlineData(0.3)]
    [InlineData(1.0)]
    [InlineData(20.0)]
    [InlineData(300.0)]
    public void TheBallGoesDarkBeforeItClimbsOutOfItself(double yieldKt)
    {
        double charge = yieldKt * 1.0e6;

        double lit = 0.0;
        for (double t = 0.0; t < 60.0; t += 0.05)
        {
            if (MushroomCloud.FlashAt(charge, t).Spent) break;

            lit = MushroomCloud.EmberHeight(charge, t);
        }

        double peak = MushroomCloud.PeakFireballRadius(yieldKt);
        Assert.True(lit < peak * 4.5,
            $"at {yieldKt:F1} kt the ball is still lit {lit:F0} m up, {lit / peak:F1} of its own "
            + $"{peak:F0} m radius; past 4.5 it reads as a flare going up rather than a burst dying");
    }

    /// <summary>
    /// The pens must not wind round the axis. They are trails that keep every position they have
    /// held, so a full turn draws a helix and a ring of them is a spiral staircase rather than a
    /// cloud.
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

    /// <summary>
    /// The climb is Teapot Wasp's tracked cloud top, not a curve that merely looks like one. The
    /// old step response had done a twelfth of its rise where the real cloud had done a third —
    /// and the start is the part that matters, because the fireball rides it through the handover.
    /// </summary>
    [Theory]
    [InlineData(0.10, 0.308)]
    [InlineData(0.30, 0.509)]
    [InlineData(0.50, 0.771)]
    [InlineData(0.60, 0.840)]
    [InlineData(0.80, 0.991)]
    [InlineData(1.00, 1.000)]
    public void TheClimbFollowsTheTrackedCloud(double fraction, double height)
        => Assert.Equal(height, MushroomCloud.Rise(fraction * MushroomCloud.RiseSeconds), 3);

    /// <summary>It only ever goes up, and it is never behind where it started.</summary>
    [Fact]
    public void TheClimbNeverRunsBackwardsWhileItRises()
    {
        double last = 0.0;

        for (double t = 0.0; t <= 1.0; t += 0.01)
        {
            double now = MushroomCloud.Rise(t * MushroomCloud.RiseSeconds);
            Assert.True(now >= last - 1e-9, $"the cloud sank at {t:F2} of its rise");
            last = now;
        }
    }

    /// <summary>
    /// It overshoots a few per cent and settles, which is what Ruth and Post were tracked doing.
    /// Joined to the track without a step, so nothing shows at the moment the two halves meet.
    /// </summary>
    [Fact]
    public void TheClimbOvershootsAndComesBack()
    {
        double ceiling = MushroomCloud.Rise(MushroomCloud.RiseSeconds);
        double peak = MushroomCloud.Rise(1.25 * MushroomCloud.RiseSeconds);
        double later = MushroomCloud.Rise(4.0 * MushroomCloud.RiseSeconds);

        Assert.Equal(1.0, ceiling, 6);
        Assert.InRange(peak, 1.02, 1.06);
        Assert.InRange(later, 1.0, 1.01);

        // No step where the track hands over to the overshoot.
        Assert.Equal(MushroomCloud.Rise(0.999 * MushroomCloud.RiseSeconds),
                     MushroomCloud.Rise(1.001 * MushroomCloud.RiseSeconds), 2);
    }
}
