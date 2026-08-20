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

    /// <summary>Nothing may be read off a vehicle its own thrusters are still moving.</summary>
    [Fact]
    public void NoMeasurementIsTakenWhileTheTrimIsFiring()
    {
        var aim = new PostBoostAim();

        PostBoostAim.Decision d = aim.Update(Step, trimSettled: false, 50_000.0, aimHasSettled: false);

        Assert.False(d.MayMeasure);
        Assert.False(d.MayRelease);
    }

    [Fact]
    public void TheFirstMeasurementIsTakenAsSoonAsTheTrimIsQuiet()
    {
        var aim = new PostBoostAim();

        Assert.True(aim.Update(Step, true, double.NaN, false).MayMeasure);
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

        PostBoostAim.Decision d = aim.Update(Step, true, cannotPayBack, false);

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

        PostBoostAim.Decision d = aim.Update(Step, true, paysBack, false);

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

        PostBoostAim.Decision d = aim.Update(Step, true, large, aimHasSettled: true);

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
            released = aim.Update(Step, trimSettled: true, huge, false).MayRelease;
        }

        Assert.True(released);
        Assert.True(aim.Cycles <= PostBoostAim.MaxCycles);
    }

    /// <summary>Once it has released it stays released — a later settle must not restart it.</summary>
    [Fact]
    public void ReleasingIsFinal()
    {
        var aim = new PostBoostAim();
        aim.Update(Step, true, 1.0, aimHasSettled: true);

        Assert.True(aim.Update(Step, false, 500_000.0, false).MayRelease);
        Assert.False(aim.Update(Step, true, 500_000.0, false).MayMeasure);
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
            bool release = aim.Update(Step, t % 2.0 < 1.0, 80_000.0, false).MayRelease;
            Assert.NotEqual(release, aim.Correcting);
        }
    }
}
