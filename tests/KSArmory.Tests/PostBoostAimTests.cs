using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// When the bus corrects its own aim after cutoff, and when it stops and lets the warheads go.
///
/// <para>Every rule here trades one real cost against another, so getting one backwards does not
/// fail loudly — it releases a shot that could have been corrected, or corrects a shot into a worse
/// one. Both look like a working bus.</para>
/// </summary>
public class PostBoostAimTests
{
    private const double Step = 0.5;

    /// <summary>A nose that is not moving, which is the only kind a reading may be taken off.</summary>
    private static readonly double3 Held = new(1, 0, 0);

    /// <summary>
    /// The sequencer with the bus holding still and the tank untouched, so a test says which of the
    /// gates it is about by naming only that one.
    /// </summary>
    private static PostBoostSituation Bus(bool trimSettled, double missMetres,
                                          bool aimHasSettled = false,
                                          double3? directionCci = null,
                                          double spentMetresPerSecond = 0.0)
        => new(trimSettled, directionCci ?? Held, missMetres, aimHasSettled, spentMetresPerSecond);

    /// <summary>
    /// Hold the bus still long enough for the settle gate to open, taking no reading on the way.
    ///
    /// <para>Every rule below this one is about what happens once a reading may be taken, so a test
    /// that did not do this would be measuring <see cref="PostBoostAim.SteadySeconds"/> instead of
    /// the rule it names.</para>
    /// </summary>
    private static void Settle(PostBoostAim aim, double3? directionCci = null)
    {
        for (double t = 0.0; t <= PostBoostAim.SteadySeconds; t += Step)
        {
            aim.Update(Step, Bus(true, double.NaN, directionCci: directionCci));
        }
    }

    /// <summary>Nothing may be read off a vehicle its own thrusters are still moving.</summary>
    [Fact]
    public void NoMeasurementIsTakenWhileTheTrimIsFiring()
    {
        var aim = new PostBoostAim();

        PostBoostAim.Decision d = aim.Update(Step, Bus(trimSettled: false, 50_000.0));

        Assert.False(d.MayMeasure);
        Assert.False(d.MayRelease);
    }

    [Fact]
    public void TheFirstMeasurementIsTakenAsSoonAsTheTrimIsQuiet()
    {
        var aim = new PostBoostAim();

        Settle(aim);
        Assert.True(aim.Update(Step, Bus(true, double.NaN)).MayMeasure);
    }

    /// <summary>
    /// The stopping rule is a payback, not a count. A miss smaller than the leverage another cycle
    /// would spend is one that correcting makes worse.
    /// </summary>
    [Fact]
    public void AShotAlreadyInsideWhatACycleCostsIsReleasedRatherThanCorrected()
    {
        var aim = new PostBoostAim();
        double cannotPayBack =
            0.5 * PostBoostAim.FirstCycleSeconds * PostBoostAim.HoldingCostsMetresPerSecond;

        Settle(aim);

        PostBoostAim.Decision d = aim.Update(Step, Bus(true, cannotPayBack));

        Assert.True(d.MayRelease);
        Assert.Equal(0, aim.Cycles);
        Assert.Contains("another correction would cost", d.Said);
    }

    [Fact]
    public void AMissLargerThanThatIsWorthACycle()
    {
        var aim = new PostBoostAim();
        double paysBack =
            4.0 * PostBoostAim.FirstCycleSeconds * PostBoostAim.HoldingCostsMetresPerSecond;

        Settle(aim);

        PostBoostAim.Decision d = aim.Update(Step, Bus(true, paysBack));

        Assert.True(d.MayMeasure);
        Assert.False(d.MayRelease);
        Assert.Equal(1, aim.Cycles);
    }

    /// <summary>
    /// The correction knows things the sequencer does not — that a cycle made the miss worse, that
    /// the plant is not the one it modelled. When it gives up, so does this.
    /// </summary>
    [Fact]
    public void ItStopsWhenTheCorrectionItselfHasStoppedImproving()
    {
        var aim = new PostBoostAim();
        double large = 100_000.0;

        Settle(aim);

        PostBoostAim.Decision d = aim.Update(Step, Bus(true, large, aimHasSettled: true));

        Assert.True(d.MayRelease);
        Assert.Equal(0, aim.Cycles);
        Assert.Contains("settled", d.Said);
    }

    /// <summary>
    /// A cycle that keeps promising an improvement it never delivers still has to end. Warheads
    /// aboard when the release altitude closes are no shot at all.
    /// </summary>
    [Fact]
    public void ItGivesUpRatherThanCorrectingForEver()
    {
        var aim = new PostBoostAim();
        double huge = 1_000_000.0;
        bool released = false;

        for (double t = 0.0; t < PostBoostAim.MaxSeconds * 3.0 && !released; t += Step)
        {
            // Settling and measuring alternate, which is the shape that would otherwise never end.
            released = aim.Update(Step, Bus(trimSettled: true, huge)).MayRelease;
        }

        Assert.True(released);
        Assert.True(aim.Cycles <= PostBoostAim.MaxCycles);
    }

    /// <summary>Once it has released it stays released — a later settle must not restart it.</summary>
    [Fact]
    public void ReleasingIsFinal()
    {
        var aim = new PostBoostAim();
        Settle(aim);
        aim.Update(Step, Bus(true, 1.0, aimHasSettled: true));

        Assert.True(aim.Update(Step, Bus(false, 500_000.0)).MayRelease);
        Assert.False(aim.Update(Step, Bus(true, 500_000.0)).MayMeasure);
    }

    /// <summary>
    /// The correcting flag is what holds the warheads aboard, so it has to go false exactly when
    /// release is allowed. Two names for one state that disagree is a bus that never fires.
    /// </summary>
    [Fact]
    public void CorrectingAndReleasingAreNeverBothTrue()
    {
        var aim = new PostBoostAim();

        for (double t = 0.0; t < PostBoostAim.MaxSeconds * 2.0; t += Step)
        {
            bool release = aim.Update(Step, Bus(t % 2.0 < 1.0, 80_000.0)).MayRelease;
            Assert.NotEqual(release, aim.Correcting);
        }
    }

    // ------------------------------------------------- the nose the reading is taken through

    /// <summary>
    /// The prediction the correction reads adds the ejection kick along the bus's nose, so a nose
    /// that is turning is a moving instrument and nothing may be read off it.
    ///
    /// <para>Measured on the 3,459 km shot: 14-22 m of predicted impact per degree the kick turns
    /// near the nose, and 16.0 km of swing available at a full turn - against 0.17 km the trim can
    /// leave behind at a reading the gate admits.</para>
    /// </summary>
    [Fact]
    public void NoMeasurementIsTakenOffANoseThatIsStillTurning()
    {
        var aim = new PostBoostAim();

        // Well outside the band every step, which is a bus tumbling rather than one settling.
        for (double t = 0.0; t < PostBoostAim.SteadySeconds * 4.0; t += Step)
        {
            double turn = t * 20.0 * Math.PI / 180.0;

            PostBoostAim.Decision d = aim.Update(Step, Bus(
                trimSettled: true, 50_000.0,
                directionCci: new double3(Math.Cos(turn), Math.Sin(turn), 0)));

            Assert.False(d.MayMeasure);
        }

        Assert.Equal(0, aim.Cycles);
    }

    /// <summary>
    /// And it gives up rather than waiting the tumble out. Holding costs
    /// <see cref="PostBoostAim.HoldingCostsMetresPerSecond"/> a second and a bus that will not
    /// settle has nothing to hand over at the end of the wait — a separated one is measured in
    /// flight with a 22.11 deg pointing band and free roll angle.
    /// </summary>
    [Fact]
    public void ABusThatWillNotHoldStillStopsCorrectingRatherThanWaitingItOut()
    {
        var aim = new PostBoostAim();
        double elapsed = 0.0;
        PostBoostAim.Decision d = default;

        for (; elapsed < PostBoostAim.MaxSeconds && !d.MayRelease; elapsed += Step)
        {
            double turn = elapsed * 20.0 * Math.PI / 180.0;

            d = aim.Update(Step, Bus(
                trimSettled: true, 50_000.0,
                directionCci: new double3(Math.Cos(turn), Math.Sin(turn), 0)));
        }

        Assert.True(d.MayRelease, "a tumbling bus held its warheads for the whole backstop");

        // Which rule stopped it is the point: releasing because the readings happened to stop
        // improving is releasing on readings that never meant anything.
        Assert.Contains("holding still", d.Said);
        Assert.True(elapsed <= PostBoostAim.SettlesWithinSeconds + PostBoostAim.SteadySeconds + Step,
                    $"gave up after {elapsed:F1} s, which is more than the "
                    + $"{PostBoostAim.SettlesWithinSeconds:F0} s it is allowed to wait");
        Assert.Equal(0, aim.Cycles);
    }

    /// <summary>
    /// A nose drifting slowly enough to stay inside the band is steady, whatever the frame rate —
    /// the gate is an angle across a window and not a per-frame rate, so the frame pacing cannot
    /// decide the answer. KSA's step alternates 8.33 / 25.0 ms on a 120 Hz screen at a nominal 60.
    /// </summary>
    [Theory]
    [InlineData(0.00833)]
    [InlineData(0.025)]
    [InlineData(0.5)]
    public void TheSettleGateReadsTheSameWhateverTheFrameItIsSampledOn(double step)
    {
        var aim = new PostBoostAim();

        // Half the band per window: inside it, and moving the whole time.
        double ratePerSecond = 0.5 * PostBoostAim.SteadyWithinDegrees / PostBoostAim.SteadySeconds;

        for (double t = 0.0; t < PostBoostAim.SteadySeconds * 2.0; t += step)
        {
            double turn = t * ratePerSecond * Math.PI / 180.0;

            aim.Update(step, Bus(trimSettled: true, double.NaN,
                                 directionCci: new double3(Math.Cos(turn), Math.Sin(turn), 0)));
        }

        Assert.True(aim.Steady, $"a drift of {ratePerSecond:F2} deg/s read as unsteady at a "
                                + $"{step * 1000.0:F1} ms step");
    }

    /// <summary>
    /// The differencing is the measurement, so it has to be invariant under a rotation applied to
    /// every sample — the same test <c>docs/FRAMES-AND-EPOCHS.md</c> asks of anything that
    /// subtracts two frame-carrying terms.
    /// </summary>
    [Fact]
    public void WhatCountsAsSteadyDoesNotDependOnTheFrameTheDirectionsAreExpressedIn()
    {
        doubleQuat turned = doubleQuat.CreateFromAxisAngle(Vec.Unit(new double3(0.3, -0.8, 0.5)), 1.1);

        double rate = 0.5 * PostBoostAim.SteadyWithinDegrees / PostBoostAim.SteadySeconds;

        var plain = new PostBoostAim();
        var rotated = new PostBoostAim();
        bool everSteady = false;

        for (double t = 0.0; t < PostBoostAim.SteadySeconds * 6.0; t += Step)
        {
            double a = t * rate * Math.PI / 180.0;
            double3 nose = new(Math.Cos(a), Math.Sin(a), 0);

            plain.Update(Step, Bus(true, double.NaN, directionCci: nose));
            rotated.Update(Step, Bus(true, double.NaN, directionCci: turned * nose));

            Assert.Equal(plain.Steady, rotated.Steady);
            everSteady |= plain.Steady;
        }

        // Otherwise two sequencers that both never settle would agree about nothing.
        Assert.True(everSteady, "neither read steady, so the agreement proved nothing");
    }

    /// <summary>A bus with no modelled kick has no turning instrument, so nothing is waited on.</summary>
    [Fact]
    public void ALauncherThatThrowsNothingIsSteadyRatherThanNeverReady()
    {
        var aim = new PostBoostAim();

        Assert.True(aim.Update(Step, Bus(true, double.NaN, directionCci: Vec.Zero)).MayMeasure);
    }

    // ------------------------------------------------- the best, and the tank

    /// <summary>
    /// Passes stop when they stop beating the best seen — and the bar they have to beat it by
    /// scales, so the loop follows a converging shot all the way down.
    ///
    /// <para><b>This is what a fixed 250 m bar cost.</b> On the flown readings a fixed bar banks
    /// 400 m and stops three passes later, because nothing after it improves by a quarter of a
    /// kilometre — while the same readings go on to 100 m. At the shipped ejection kick a pass
    /// costs about 13 m of leverage at two seconds, so those passes were worth taking and the rule
    /// could not see them.</para>
    /// </summary>
    [Fact]
    public void PassesThatStopBeatingTheBestEndIt()
    {
        var aim = new PostBoostAim();
        Settle(aim);

        double[] flown = [3300, 2000, 1200, 700, 400, 300, 500, 200, 400, 100, 500, 300, 200];

        int read = 0;
        bool released = false;

        foreach (double miss in flown)
        {
            read++;
            released = aim.Update(Step, Bus(true, miss)).MayRelease;
            if (released) break;
        }

        Assert.True(released, "the wander after convergence never stopped it");
        Assert.True(aim.Cycles < PostBoostAim.MaxCycles);

        // The whole point: it banked the 100 m the readings reached, not the 400 m a quarter-
        // kilometre bar stops at.
        Assert.Equal(100.0, aim.BestMissMetres);

        Assert.True(read <= 13, $"it took {read} readings to stop, against 10 that improved plus "
                                + $"{PostBoostAim.PassesWithoutImprovement} that did not");
    }

    /// <summary>
    /// And the counter is failures to improve rather than worsenings, which is the whole difference
    /// from <see cref="AimCorrection.WorseBeforeStopping"/>. A reading that oscillates inside the
    /// band is never <em>worse</em> than the best by enough to count, so that rule alone never
    /// trips.
    /// </summary>
    [Fact]
    public void ReadingsThatOscillateInsideTheBandStillStopIt()
    {
        var aim = new PostBoostAim();
        Settle(aim);

        // Inside AimCorrection.ImprovedBy(1000) either way, so never better and never worse by
        // enough to count. The amplitude has to be read against the bar rather than written down:
        // a wander of hundreds is a real convergence at this scale, which is the neighbouring test.
        double[] wander = [1_000, 1_040, 960, 1_030, 970, 1_000, 1_040, 960, 1_030, 970,
                           1_000, 1_040, 960, 1_030, 970, 1_000, 1_040, 960, 1_030, 970];

        int read = 0;
        bool released = false;

        foreach (double miss in wander)
        {
            read++;
            released = aim.Update(Step, Bus(true, miss)).MayRelease;
            if (released) break;
        }

        Assert.True(released, "a reading that never worsens by enough to count never stopped it");
        Assert.True(read <= PostBoostAim.PassesWithoutImprovement + 1,
                    $"it took {read} readings against {AimCorrection.WorseBeforeStopping} "
                    + "the worsening counter would have needed and never reached");
    }

    /// <summary>
    /// The passes are what the correction costs in propellant, and a bus that arrives at the
    /// release dry cannot null the separation impulse — 1.1 m/s of decoupler shove takes the
    /// predicted impact from 0.7 km to 4.5 km on this arc. Measured in flight: 1,943 frames with
    /// thrusters firing against 24 settled, about 36 m/s, on a bus carrying 70-90.
    /// </summary>
    [Fact]
    public void ItStopsOnceThePassesHaveSpentTheBudget()
    {
        var aim = new PostBoostAim();
        Settle(aim);

        PostBoostAim.Decision d = aim.Update(Step, Bus(
            trimSettled: true, 500_000.0,
            spentMetresPerSecond: PostBoostAim.MaxTrimMetresPerSecond));

        Assert.True(d.MayRelease, "half a megametre of miss bought passes past the tank");
        Assert.Equal(0, aim.Cycles);
        Assert.Contains("budget", d.Said);
    }

    /// <summary>
    /// And the budget leaves a reserve worth having: at least one null at the largest trim
    /// <see cref="BusTrim.MaxMetresPerSecond"/> will accept, on the smallest bus the shipped rack
    /// is flown on.
    /// </summary>
    [Fact]
    public void TheBudgetLeavesEnoughToNullASeparation()
    {
        const double smallestTankMetresPerSecond = 70.0;

        Assert.True(
            smallestTankMetresPerSecond - PostBoostAim.MaxTrimMetresPerSecond
                >= BusTrim.MaxMetresPerSecond,
            $"a correction spending {PostBoostAim.MaxTrimMetresPerSecond:F0} m/s of a "
            + $"{smallestTankMetresPerSecond:F0} m/s tank cannot afford a "
            + $"{BusTrim.MaxMetresPerSecond:F0} m/s null afterwards");
    }
}
