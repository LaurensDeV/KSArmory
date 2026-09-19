using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// That a reading comes off a flown correction, not off the one that produced it.
///
/// <para>Measured across 94 coast corrections: a second reading arrives a median of 2.03 s after the
/// first, before the trim has flown anything, and the loop deadbeats on the same error twice. The
/// third reading, 41 s later, then reads 2.24x the best and the plant secant reads 3.13 off that
/// overshoot.</para>
/// </summary>
public class PostBoostFlownReadingTests
{
    private const double Step = 0.5;

    private static PostBoostSituation Situation(bool trimSettled, double missMetres,
                                                bool decideOnTheReading = false)
        => new(TrimSettled: trimSettled,
               ReleaseDirectionCci: new Brutal.Numerics.double3(1.0, 0.0, 0.0),
               PredictedMissMetres: missMetres,
               AimHasSettled: false,
               TrimSpentMetresPerSecond: 0.0,
               DecideOnTheReading: decideOnTheReading);

    // Advances until the first reading is taken and stops there, so the fallback clock starts from
    // a known instant. Running on past it spends FlownWithinSeconds inside the fixture instead.
    private static void UpToTheFirstReading(PostBoostAim aim, double missMetres)
    {
        for (int i = 0; i < 200; i++)
        {
            if (aim.Update(Step, Situation(true, missMetres)).MayMeasure) return;
        }

        Assert.Fail("the fixture never got its first reading");
    }

    private static int ReadingsWhile(PostBoostAim aim, bool trimSettled, double missMetres,
                                     double seconds)
    {
        int taken = 0;

        for (double t = 0.0; t < seconds; t += Step)
        {
            if (aim.Update(Step, Situation(trimSettled, missMetres)).MayMeasure) taken++;
        }

        return taken;
    }

    [Fact]
    public void TheFirstReadingIsTakenWithoutWaitingForAnything()
    {
        PostBoostAim aim = new();

        Assert.True(ReadingsWhile(aim, trimSettled: true, missMetres: 4_200.0, seconds: 40.0) > 0,
                    "the cutoff solution's own reading must not wait for a flight that precedes it");
    }

    [Fact]
    public void ASecondReadingWaitsForTheTrimToHaveFlownTheFirst()
    {
        PostBoostAim aim = new();
        UpToTheFirstReading(aim, 4_200.0);

        // The trim never reports itself working, so nothing has been flown. Well inside
        // FlownWithinSeconds, so the pass must still be waiting.
        int again = ReadingsWhile(aim, trimSettled: true, missMetres: 4_200.0,
                                  seconds: PostBoostAim.FlownWithinSeconds - Step * 3.0);

        Assert.Equal(0, again);
    }

    [Fact]
    public void AndIsTakenOnceTheTrimHasBeenSeenWorking()
    {
        PostBoostAim aim = new();
        UpToTheFirstReading(aim, 4_200.0);

        // The trim fires, then settles again -- one flown correction.
        for (int i = 0; i < 6; i++) aim.Update(Step, Situation(false, 4_200.0));

        Assert.True(ReadingsWhile(aim, trimSettled: true, missMetres: 3_100.0, seconds: 40.0) > 0,
                    "a correction that has been flown must be readable");
    }

    [Fact]
    public void AndIsTakenAnywayIfTheTrimNeverReportsWorking()
    {
        // The bounded fallback: an interlock holding the trim off, or a demand already inside its
        // settle band, must not stall the correction at one pass for ever.
        PostBoostAim aim = new();
        UpToTheFirstReading(aim, 4_200.0);

        int eventually = ReadingsWhile(aim, trimSettled: true, missMetres: 4_200.0,
                                       seconds: PostBoostAim.FlownWithinSeconds + 20.0);

        Assert.True(eventually > 0, "waiting for a flight that never comes must not stall the loop");
    }

    /// <summary>
    /// The frames the engine actually delivers after a pass: the trim flies, it settles on a frame
    /// the prediction has not reached yet, and the reading arrives on the next. That reading is the
    /// one a flown correction earned, and it must be decided on when it arrives rather than
    /// FlownWithinSeconds later. <c>docs/ACCURACY-PLAN.md</c> 3cr.
    /// </summary>
    [Fact]
    public void AFrameWithNoReadingLeavesTheFlightForTheReadingThatFollows()
    {
        PostBoostAim aim = new();
        UpToTheFirstReading(aim, 4_200.0);
        int passes = aim.Cycles;

        for (int i = 0; i < 6; i++) aim.Update(Step, Situation(false, double.NaN, decideOnTheReading: true));
        aim.Update(Step, Situation(true, double.NaN, decideOnTheReading: true));

        PostBoostAim.Decision decided = aim.Update(Step, Situation(true, 3_100.0, decideOnTheReading: true));

        Assert.True(aim.Cycles > passes,
                    "the reading after a flown correction was held instead of decided on");
        Assert.True(decided.MayMeasure, "a pass that improves on the best should start another");
    }

    /// <summary>
    /// Off, the same frames leave the reading waiting, which is what ships until the switch has
    /// flown. A switch that changes the default path is not off.
    /// </summary>
    [Fact]
    public void OffTheFrameWithNoReadingStillSpendsTheFlight()
    {
        PostBoostAim aim = new();
        UpToTheFirstReading(aim, 4_200.0);
        int passes = aim.Cycles;

        for (int i = 0; i < 6; i++) aim.Update(Step, Situation(false, double.NaN));
        aim.Update(Step, Situation(true, double.NaN));

        int decided = ReadingsWhile(aim, trimSettled: true, missMetres: 3_100.0,
                                    seconds: PostBoostAim.FlownWithinSeconds - Step * 3.0);

        Assert.Equal(0, decided);
        Assert.Equal(passes, aim.Cycles);
    }

    /// <summary>
    /// The fallback has the same race. A reading let through once FlownWithinSeconds has run out
    /// must not have that clock restarted by a frame with no number in it, or it waits it out again.
    /// </summary>
    [Fact]
    public void AFrameWithNoReadingDoesNotRestartTheFallbackClock()
    {
        PostBoostAim aim = new();
        UpToTheFirstReading(aim, 4_200.0);
        int passes = aim.Cycles;

        // The trim never reports working, and no reading arrives until the fallback has run out.
        for (double t = 0.0; t < PostBoostAim.FlownWithinSeconds + Step; t += Step)
        {
            aim.Update(Step, Situation(true, double.NaN, decideOnTheReading: true));
        }

        aim.Update(Step, Situation(true, 3_100.0, decideOnTheReading: true));

        Assert.True(aim.Cycles > passes,
                    "a reading arriving after the fallback ran out was held for another one");
    }
}
