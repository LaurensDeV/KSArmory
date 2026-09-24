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
    ///
    /// <para>It is deliberately <em>not</em> monotone — see the double pulse below — so what is
    /// guarded here is the collapse rather than the direction: the first pulse is the brightest
    /// point of the whole flash, and by halfway through the luminous phase there is a fifth of it
    /// left. A linear fade from the peak sits at half there and fails.</para>
    /// </summary>
    [Fact]
    public void TheFlashIsBrightestAtTheStartAndCollapses()
    {
        double dark = MushroomCloud.DarkAfter(0.3);

        double at0 = MushroomCloud.FlashAt(0.3 * Kt, 0.0).Glow;
        double atHalf = MushroomCloud.FlashAt(0.3 * Kt, dark * 0.5).Glow;

        for (double age = 0.0; age <= dark; age += dark / 400.0)
        {
            Assert.True(MushroomCloud.FlashAt(0.3 * Kt, age).Glow <= at0 + 1e-9,
                        $"something outshone the first pulse at {age:F4} s");
        }

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
    /// It grows as t^0.4 over about a second and a half at 340 kt, reaching 90% of its largest by
    /// about three times its second maximum (Glasstone 1962, Fig 2.51) -- not in a tenth of a
    /// second, which read as a ball popping in rather than a burst expanding.
    /// </summary>
    [Fact]
    public void TheFireballGrowsOverItsOwnPulseRatherThanAppearing()
    {
        double peak = MushroomCloud.PeakFireballRadius(340.0);
        double tMax = MushroomCloud.ThermalMaximumSeconds(340.0);

        Assert.True(MushroomCloud.FlashAt(340.0 * Kt, 0.1).Radius < peak * 0.6,
                    "a tenth of a second in it is still well short of its largest");
        Assert.True(MushroomCloud.FlashAt(340.0 * Kt, 3.0 * tMax).Radius > peak * 0.85,
                    "and near it by three times the second maximum");
        Assert.True(MushroomCloud.FlashAt(0.3 * Kt, 0.0).Radius > 0.0, "and the first frame has a ball");
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
        double capR = MushroomCloud.DrawnCapRadius(0.3);

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
    /// The ball goes out rather than being deleted. It ends while the smoke is taking over from it,
    /// which is the single instant the eye is watching for continuity, so it has to be dark by then.
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

        // And then goes out: only ever dimmer through the ember, and as good as dark on the last
        // frame it is drawn, so its removal is not a cut. BurstContinuityTests holds every frame.
        double previous = double.MaxValue;
        double lastGlow = 0.0;
        for (double age = flash; age < flash + MushroomCloud.EmberSeconds; age += 1.0 / 60.0)
        {
            MushroomCloud.Flash f = MushroomCloud.FlashAt(0.3 * Kt, age);
            if (f.Spent) break;

            Assert.True(f.Glow <= previous, $"at {age:F2} s the ember brightens, {previous:F2} -> {f.Glow:F2}");
            previous = f.Glow;
            lastGlow = f.Glow;
        }

        Assert.True(lastGlow < 0.01 * MushroomCloud.EmberGlow,
                    $"the ember is removed still glowing at {lastGlow:F2}");

        // And nowhere near a second flash.
        Assert.True(MushroomCloud.FlashAt(0.3 * Kt, flash * 1.05).Glow
                    < MushroomCloud.FlashAt(0.3 * Kt, 0.0).Glow * 0.1);

        Assert.True(MushroomCloud.FlashAt(0.3 * Kt, flash + MushroomCloud.EmberSeconds + 0.01).Spent,
                    "and be gone at the end of it");

        // And it climbs the whole time it is there, rather than hanging where it burst while the
        // cloud leaves without it. It climbs ON THE CAP, because it is the cap.
        double first = MushroomCloud.At(0.3 * Kt, flash).CapCentre;
        double last = MushroomCloud.At(0.3 * Kt, flash + MushroomCloud.EmberSeconds).CapCentre;

        Assert.True(last > first * 2.0,
                    $"the ember should still be lifting: {first:F0} m to {last:F0} m");
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
    /// The ball glows as long as the law says up to 20 kt, and longer still past it: a 340 kt ball
    /// that went dark at 3.4 s, as a leftover ceiling had it, is the burst reading as weak.
    /// </summary>
    [Fact]
    public void TheGlowRunsItsRealLengthAndLengthensWithYield()
    {
        Assert.Equal(MushroomCloud.DarkAfter(20.0), MushroomCloud.FlashSeconds(20.0), 6);
        Assert.True(MushroomCloud.FlashSeconds(20.0) > 9.0, "about ten seconds at 20 kt, as Glasstone has it");
        Assert.True(MushroomCloud.FlashSeconds(340.0) > MushroomCloud.FlashSeconds(20.0));
        Assert.True(MushroomCloud.FlashSeconds(340.0) <= MushroomCloud.LongestGlowSeconds);
    }

    /// <summary>
    /// White-yellow for the heat pulse, which has given out 80% of its energy by ten times its
    /// second maximum -- 5.4 s at 340 kt -- and orange only after that. At 20 kt the same pulse is
    /// over by 1.6 s, so it is orange by three.
    /// </summary>
    [Fact]
    public void TheBallStaysWhiteHotForItsHeatPulse()
    {
        double3 big = MushroomCloud.FlashAt(340.0 * Kt, 3.0).Colour;
        double3 small = MushroomCloud.FlashAt(20.0 * Kt, 3.0).Colour;

        Assert.True(big.Z > 0.5 && big.Y > 0.8, $"340 kt at 3 s should be white-yellow, not {big}");
        Assert.True(small.Z < big.Z, "and a smaller ball cools sooner");
        Assert.True(MushroomCloud.FlashAt(340.0 * Kt, 3.0).Glow > MushroomCloud.BurnGlow,
                    "still burning at full heat, not dimming from its peak");
    }

    /// <summary>
    /// The condensation shell is a blast-clock thing: 1 to 2 s after a 20 kt burst and gone about a
    /// second later (Glasstone §2.49), and longer for a bigger blast by the cube root -- not tied
    /// to how long the ball glows, which would stand it up for tens of seconds.
    /// </summary>
    [Fact]
    public void TheCondensationShellKeepsTheBlastsClock()
    {
        Assert.Equal(MushroomCloud.WilsonAt20KtSeconds, MushroomCloud.WilsonSeconds(20.0 * Kt), 6);
        Assert.Equal(Math.Cbrt(17.0), MushroomCloud.WilsonSeconds(340.0 * Kt) / MushroomCloud.WilsonSeconds(20.0 * Kt), 6);
        Assert.True(MushroomCloud.WilsonSeconds(20.0 * Kt) < MushroomCloud.FlashSeconds(20.0));
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
    /// A lit ball that has climbed is inside the cloud it became, not out in the open.
    ///
    /// <para><b>This replaces a height limit whose premise is gone.</b> The ball used to lift on a
    /// law of its own and stop, because it was an emissive sphere drawn <em>over</em> the smoke:
    /// for as long as it was lit it was the brightest thing in frame, and a lit ball climbing
    /// hundreds of metres read as a flare ascending rather than as a burst dying. It is drawn as a
    /// mesh now and the cloud is a raymarch that clips itself against the scene depth and
    /// multiplies what is behind it by its own transmittance — so the ball is <em>inside</em> the
    /// smoke, and a height limit that held it under its own cloud was keeping the two apart for a
    /// reason that had expired.</para>
    ///
    /// <para>What has to hold instead is that the cloud is actually around it by the time it is up
    /// there. Stated in the ball's own radii so it holds at every yield: the absolute height varies
    /// fifteen-fold across the range and the proportion does not.</para>
    /// </summary>
    [Theory]
    [InlineData(0.3)]
    [InlineData(1.0)]
    [InlineData(20.0)]
    [InlineData(300.0)]
    public void ALitBallThatHasClimbedIsInsideTheCloudItBecame(double yieldKt)
    {
        double charge = yieldKt * 1.0e6;
        double peak = MushroomCloud.PeakFireballRadius(yieldKt);

        for (double t = 0.0; t < 60.0; t += 0.05)
        {
            MushroomCloud.Flash flash = MushroomCloud.FlashAt(charge, t);
            if (flash.Spent) break;

            MushroomCloud.Shape shape = MushroomCloud.At(charge, t);

            // Below a couple of its own radii it is a fireball sitting on the ground and there is
            // no cloud yet, which is right: the cap is made of this.
            if (shape.CapCentre < peak * 2.0) continue;

            Assert.True(shape.CapRadius > flash.Radius,
                        $"at {yieldKt:F1} kt the ball is lit {shape.CapCentre:F0} m up "
                        + $"({shape.CapCentre / peak:F1} of its own radius) and is {flash.Radius:F0} m "
                        + $"across inside a {shape.CapRadius:F0} m cap -- it is out in the open");
        }
    }

    /// <summary>
    /// The ball and the cap are one object, which is what makes the above the right question.
    /// </summary>
    [Theory]
    [InlineData(0.3)]
    [InlineData(20.0)]
    public void TheBallSitsWhereTheCapDoes(double yieldKt)
    {
        double charge = yieldKt * 1.0e6;

        for (double t = 0.1; t < 20.0; t += 0.1)
        {
            MushroomCloud.Shape shape = MushroomCloud.At(charge, t);

            Assert.True(shape.CapCentre >= 0.0);
            Assert.True(shape.CapCentre <= MushroomCloud.DrawnCloudTop(yieldKt),
                        $"the cap centre left the cloud at {t:F1} s");
        }
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

    [Theory]
    [InlineData(0.3)]
    [InlineData(30.0)]
    [InlineData(1000.0)]
    public void TheThermalPulseHasTwoMaximaWithARealMinimumBetweenThem(double kt)
    {
        // The signature. A bhangmeter identifies a nuclear test from orbit on this curve alone,
        // and nothing else in nature produces it: an intensely bright, very short first pulse, a
        // minimum as the shock front goes opaque to the radiation behind it, then a second maximum
        // that is dimmer and lasts an order of magnitude longer.
        double charge = kt * 1.0e6;

        double tMin = MushroomCloud.PulseMinimumSeconds(kt);
        double tPeak = MushroomCloud.PulsePeakSeconds(kt);

        Assert.True(tPeak > tMin, $"the second maximum at {tPeak:F4} s is not after the minimum");

        double first = MushroomCloud.FlashAt(charge, 0.0).Glow;
        double second = MushroomCloud.FlashAt(charge, tPeak).Glow;

        // The minimum is INTERIOR and has to be found rather than read off tMin: the composite
        // goes on falling past the shock front's own time while the opening behind it is still
        // small, so where the two cross is later than either.
        double dipAt = MushroomCloud.PulseTroughSeconds(charge);
        double dip = MushroomCloud.FlashAt(charge, dipAt).Glow;

        Assert.True(dipAt > tMin, $"the trough at {dipAt:F4} s is not past the shock front's {tMin:F4} s");

        Assert.True(dipAt > 0.0 && dipAt < tPeak,
                    $"the minimum landed on an endpoint at {dipAt:F4} s, so there are not two maxima");
        Assert.True(dip < first * 0.5, $"no dip: {dip:F1} against a first pulse of {first:F1}");
        Assert.True(second > dip * 1.5, $"no second maximum: {second:F1} against a dip of {dip:F1}");
    }

    [Fact]
    public void TheBallNeverGoesDarkInTheMinimum()
    {
        // The dip is the shock front hiding a fireball that is still there and still growing, not
        // the fireball going out. Under the ember floor it would drop below the bloom threshold and
        // revert to being drawn as geometry.
        double dip = MushroomCloud.FlashAt(0.3e6, MushroomCloud.PulseTroughSeconds(0.3e6)).Glow;

        Assert.True(dip >= MushroomCloud.EmberGlow, $"the ball went to {dip:F1}");
    }

    [Fact]
    public void ThePulseIsSlowedOnlyWhereItCouldNotBeSeen()
    {
        // A floor rather than a multiplier: at a megatonne the second maximum falls at 0.87 s on
        // its own, and a stretch there would put it at fourteen seconds.
        Assert.Equal(1.0, MushroomCloud.DrawnPulseStretch(1000.0), 6);
        Assert.True(MushroomCloud.DrawnPulseStretch(0.3) > 5.0);

        foreach (double kt in new[] { 0.3, 3.0, 30.0, 1000.0 })
        {
            Assert.True(MushroomCloud.PulsePeakSeconds(kt) >= MushroomCloud.LegibleSecondPeak - 1e-9,
                        $"{kt} kt peaks at {MushroomCloud.PulsePeakSeconds(kt):F3} s, too fast to see");
        }
    }

    [Fact]
    public void TheBangIsHeardUnalteredAtItsAnchor()
    {
        Assert.Equal(1.0, MushroomCloud.BangPitch(MushroomCloud.BangAnchorKt), 9);
    }

    [Fact]
    public void EightTimesTheYieldIsTwiceTheRumbleWhileTheLawHolds()
    {
        // The cube root: every time in the sound goes as W^(1/3), so eight times the yield plays
        // back at half the pitch -- twice as long and an octave down -- as long as neither end of
        // the pair has reached the floor.
        double low = MushroomCloud.BangAnchorKt;
        double high = low * 8.0;

        Assert.True(MushroomCloud.BangPitch(high) > MushroomCloud.BangPitchFloor);
        Assert.Equal(0.5, MushroomCloud.BangPitch(high) / MushroomCloud.BangPitch(low), 9);
    }

    [Fact]
    public void TheBangNeverRisesAboveTheSampleAndNeverFallsThroughTheFloor()
    {
        double last = double.MaxValue;

        foreach (double kt in new[] { 0.001, 0.01, 0.3, 1.0, 3.0, 20.0, 300.0, 10000.0 })
        {
            double pitch = MushroomCloud.BangPitch(kt);

            Assert.InRange(pitch, MushroomCloud.BangPitchFloor, 1.0);
            Assert.True(pitch <= last, $"a bigger burst was pitched higher at {kt} kt");

            last = pitch;
        }
    }

    [Fact]
    public void TheWarheadSoundsBiggerThanTheBomb()
    {
        // The two nuclear charges the arsenal ships, which were the same file before this.
        Assert.True(MushroomCloud.BangPitch(20.0) < MushroomCloud.BangPitch(0.3) * 0.5);
    }

    /// <summary>
    /// The anvil. Under the tropopause the law is untouched; past it the cap spreads rather than
    /// climbs, so a big burst reads wider than it is tall — the one cue that says its yield.
    /// </summary>
    [Theory]
    [InlineData(0.3)]
    [InlineData(20.0)]
    [InlineData(45.0)]
    public void UnderTheTropopauseTheLawIsUntouched(double yieldKt)
    {
        Assert.Equal(1.0, MushroomCloud.CapSquash(MushroomCloud.DrawnCloudTop(yieldKt)));
    }

    [Fact]
    public void ABigBurstSpreadsWiderThanItIsTall()
    {
        MushroomCloud.Shape shape = MushroomCloud.At(340.0 * Kt, MushroomCloud.RiseSeconds);
        double crown = shape.CapCentre + (2.1 * shape.CapTube);

        Assert.True(2.0 * shape.CapRadius > crown,
                    $"340 kt: {2.0 * shape.CapRadius:F0} m across under a {crown:F0} m crown");
    }

    [Fact]
    public void TheCapWidensWithYieldPastTheTropopause()
    {
        double previous = 0.0;
        for (double kt = 20.0; kt <= 340.0; kt += 20.0)
        {
            MushroomCloud.Shape shape = MushroomCloud.At(kt * Kt, MushroomCloud.RiseSeconds);
            double aspect = shape.CapRadius / (shape.CapCentre + (2.1 * shape.CapTube));

            Assert.True(aspect >= previous - 1e-9, $"the cap got narrower for its height at {kt} kt");
            previous = aspect;
        }
    }

    [Fact]
    public void TheBendHasNoKink()
    {
        double tropopause = MushroomCloud.TropopauseMetres * MushroomCloud.DrawnScale;
        double below = MushroomCloud.Stratified(tropopause - 1.0);
        double above = MushroomCloud.Stratified(tropopause + 1.0);

        Assert.Equal(2.0, above - below, 3);
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(60.0)]
    [InlineData(340.0)]
    public void TheBoundHoldsTheWholeCloud(double yieldKt)
    {
        for (double t = 0.5; t < MushroomCloud.LifeSeconds; t += 0.5)
        {
            double bound = MushroomCloud.DrawnBound(yieldKt, t);
            MushroomCloud.Shape shape = MushroomCloud.At(yieldKt * Kt, t);
            double reach = Math.Sqrt((shape.CapCentre * shape.CapCentre)
                                     + Math.Pow(shape.CapRadius + shape.CapTube, 2.0));

            Assert.True(reach < bound, $"{yieldKt} kt at {t:F1} s reaches {reach:F0} m of {bound:F0}");
        }
    }

    /// <summary>
    /// The heat inside the young cloud has to outlast the ball, or the cloud round it is clean smoke
    /// the moment the ball is dark -- and has to be gone by the end of the rise, or the cap glows while
    /// it stands.
    /// </summary>
    [Theory]
    [InlineData(0.3)]
    [InlineData(20.0)]
    [InlineData(340.0)]
    public void TheCloudStaysHotPastTheBallAndIsColdByTheStand(double kt)
    {
        double dark = MushroomCloud.FlashSeconds(kt);
        double ballOut = dark + MushroomCloud.EmberSeconds;

        Assert.Equal(0.0, MushroomCloud.Incandescence(kt * Kt, 0.0));
        Assert.True(MushroomCloud.Incandescence(kt * Kt, dark) > 0.9, "full heat as the ball goes dark");
        Assert.True(MushroomCloud.Incandescence(kt * Kt, ballOut + 0.5) > 0.1,
                    $"{kt} kt: cold at {ballOut + 0.5:F1} s, while the ball has only just gone");
        Assert.Equal(0.0, MushroomCloud.Incandescence(kt * Kt, MushroomCloud.RiseSeconds));
    }

    [Fact]
    public void TheCloudCoolsWithoutRekindling()
    {
        double dark = MushroomCloud.FlashSeconds(0.3);
        double last = double.PositiveInfinity;

        for (double age = dark; age < MushroomCloud.RiseSeconds; age += 0.05)
        {
            double heat = MushroomCloud.Incandescence(0.3 * Kt, age);
            Assert.True(heat <= last + 1e-12, $"heat rose at {age:F2} s");
            last = heat;
        }
    }

    /// <summary>
    /// The blast front is strong and then sonic, and its speed has no step where the one law hands
    /// over to the other: a jump there is the ring lurching outward on screen.
    /// </summary>
    [Theory]
    [InlineData(0.3)]
    [InlineData(20.0)]
    [InlineData(340.0)]
    public void TheBlastFrontSlowsToSoundWithoutAJerk(double kt)
    {
        const double dt = 0.001;
        double lastSpeed = double.PositiveInfinity;

        for (double age = 0.02; age < 20.0; age += 0.01)
        {
            double speed = (MushroomCloud.ShockRadius(kt, age + dt) - MushroomCloud.ShockRadius(kt, age)) / dt;
            Assert.True(speed > 330.0, $"{kt} kt: {speed:F0} m/s at {age:F2} s, under sound");
            Assert.True(speed <= lastSpeed * 1.001, $"{kt} kt: sped up at {age:F2} s");
            lastSpeed = speed;
        }
    }

    [Fact]
    public void TheBlastFrontOutrunsTheCloud()
    {
        // Two seconds in, the front is far past the cap: the ring is what is fast in the first
        // seconds, and a front on the rise's clock would still be under it.
        MushroomCloud.Shape at2 = MushroomCloud.At(0.3 * Kt, 2.0);
        Assert.True(at2.Shock > 1.5 * (at2.CapRadius + at2.CapTube), $"front {at2.Shock:F0} m, cap {at2.CapRadius:F0} m");
        Assert.Equal(0.0, MushroomCloud.ShockRadius(0.3, 0.0));
    }

    /// <summary>
    /// The cloud ages the way a real one comes apart: the cap spreads, the stem narrows away first,
    /// and the whole thins before it fades out -- standing where it burst.
    /// </summary>
    [Fact]
    public void TheStandingCloudSpreadsAndItsStemGoesFirst()
    {
        MushroomCloud.Shape risen = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds + 1.0);
        MushroomCloud.Shape old = MushroomCloud.At(0.3 * Kt, MushroomCloud.LifeSeconds - MushroomCloud.FadeOutSeconds);

        Assert.True(old.CapRadius > 1.4 * risen.CapRadius, $"cap {risen.CapRadius:F0} -> {old.CapRadius:F0} m");
        Assert.True(old.StemRadius < 0.5 * risen.StemRadius, $"stem {risen.StemRadius:F0} -> {old.StemRadius:F0} m");
        Assert.True(old.Fade < risen.Fade && old.Fade > 0.5, $"fade {risen.Fade:F2} -> {old.Fade:F2}");
        Assert.True(MushroomCloud.At(0.3 * Kt, MushroomCloud.LifeSeconds - 1.0).Fade < 0.01);
        Assert.True(MushroomCloud.LifeSeconds >= 240.0, "a cloud that is gone in a minute and a half");
    }

    /// <summary>
    /// The front arrives where its own law puts it: a dent or a puff timed off this lands as the
    /// front passes the part, not at the flash -- 2.1 s late at 800 m from 0.3 kt.
    /// </summary>
    [Theory]
    [InlineData(0.3, 800.0)]
    [InlineData(20.0, 3000.0)]
    [InlineData(340.0, 10000.0)]
    [InlineData(2.0e-5, 25.0)]
    public void TheFrontArrivesWhereItsLawPutsIt(double kt, double distance)
    {
        double arrives = MushroomCloud.ShockArrivalSeconds(kt, distance);

        Assert.Equal(distance, MushroomCloud.ShockRadius(kt, arrives), 3);
        Assert.True(MushroomCloud.ShockRadius(kt, arrives * 0.99) < distance);
    }

    /// <summary>
    /// Warheads landing together are one burst; a bomb dropped on the same spot twenty seconds
    /// later is a second one, with its own flash and front.
    /// </summary>
    [Fact]
    public void ABombOnAStandingCloudIsASecondBurst()
    {
        const double charge = 3.0e5;

        Assert.True(MushroomCloud.IsTheSameBurst(0.0, 0.0, charge * 2.0));
        Assert.True(MushroomCloud.IsTheSameBurst(0.009, 0.02, charge * 6.0));
        Assert.False(MushroomCloud.IsTheSameBurst(0.0, 16.0, charge * 2.0));
        Assert.False(MushroomCloud.IsTheSameBurst(
            MushroomCloud.PeakFireballRadius(MushroomCloud.KilotonsFor(charge * 2.0)) * 1.1, 0.0, charge * 2.0));
    }

    /// <summary>
    /// The cloud is what the burst threw, so no part of it can be outside the blast front -- across or
    /// up. Unscaled, a 340 kt cap is 2 km of lit cloud round a front 400 m out, and its top is 3 km
    /// above the front at 20 s, because the rise is drawn eightfold fast.
    /// </summary>
    [Theory]
    [InlineData(0.3)]
    [InlineData(20.0)]
    [InlineData(340.0)]
    public void NoPartOfTheCloudOutrunsTheBlastFront(double kt)
    {
        foreach (double age in new[] { 0.01, 0.08, 0.3, 1.0, 3.0, 10.0, 20.0, 30.0, 60.0 })
        {
            MushroomCloud.Shape shape = MushroomCloud.At(kt * 1.0e6, age);

            double across = shape.CapRadius + shape.CapTube;
            double up = shape.CapCentre + (MushroomCloud.CrownInTubes * shape.CapTube);
            double farthest = Math.Sqrt((across * across) + (up * up));

            Assert.True(farthest <= shape.Shock * 1.000001,
                        $"{kt} kt at {age} s: cloud reaches {farthest:F0} m, front {shape.Shock:F0} m");
            Assert.True(shape.SurgeRadius <= shape.Shock * 1.000001);
        }
    }
}
