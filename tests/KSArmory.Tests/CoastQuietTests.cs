using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The coast hold's window. Both bounds here were absent when QuietCoast first flew, and their
/// absence is the whole of the 89x it cost on worlds that were already healthy — so most of these
/// fail against that version rather than merely describing this one.
/// </summary>
public class CoastQuietTests
{
    private static CoastQuietState Holding(
        bool enabled = true, bool coasting = true, bool burning = false, bool trimming = false,
        bool salvoAway = false, bool correctionFinished = true, bool inReleaseApproach = false,
        double errorDeg = 0.1)
        => new(enabled, coasting, burning, trimming, salvoAway, correctionFinished,
               inReleaseApproach, errorDeg);

    [Fact]
    public void ItLetsGoOnceTheBusIsPointedAndEverythingElseIsDone()
    {
        var quiet = new CoastQuiet();

        Assert.True(quiet.Update(Holding(), quietDeg: 0.5, reacquireDeg: 2.0));
    }

    [Fact]
    public void ItStaysUnderCommandUntilTheCorrectionHasFinished()
    {
        var quiet = new CoastQuiet();

        // The trim resolves onto the vehicle's own control axes and takes the attitude to BE the
        // release line. Drifting between passes thrusts along stale axes, which is the loop that
        // ended on the clock in 55 of 56 flights.
        Assert.False(quiet.Update(Holding(correctionFinished: false),
                                  quietDeg: 0.5, reacquireDeg: 2.0));
    }

    [Fact]
    public void ItTakesTheLineBackBeforeTheReleaseApproach()
    {
        var quiet = new CoastQuiet();

        // Steady is not pointed: the release sequence latches on a bus that has stopped moving,
        // whether or not anything is holding it on the committed line.
        Assert.False(quiet.Update(Holding(inReleaseApproach: true),
                                  quietDeg: 0.5, reacquireDeg: 2.0));
    }

    [Fact]
    public void GoingQuietAndThenApproachingReleaseTakesTheLineBack()
    {
        var quiet = new CoastQuiet();

        Assert.True(quiet.Update(Holding(), quietDeg: 0.5, reacquireDeg: 2.0));
        Assert.False(quiet.Update(Holding(inReleaseApproach: true),
                                  quietDeg: 0.5, reacquireDeg: 2.0));
        Assert.False(quiet.IsQuiet);
    }

    [Theory]
    [InlineData(false, true, false, false, false, true)]   // not asked for
    [InlineData(true, false, false, false, false, true)]   // not coasting
    [InlineData(true, true, true, false, false, true)]     // burning
    [InlineData(true, true, false, true, false, true)]     // trimming
    [InlineData(true, true, false, false, true, true)]     // warheads already away
    [InlineData(true, true, false, false, false, false)]   // correction still running
    public void EveryPhaseThatNeedsTheAttitudeKeepsIt(
        bool enabled, bool coasting, bool burning, bool trimming, bool salvoAway,
        bool correctionFinished)
    {
        var quiet = new CoastQuiet();

        Assert.False(quiet.Update(
            Holding(enabled: enabled, coasting: coasting, burning: burning, trimming: trimming,
                    salvoAway: salvoAway, correctionFinished: correctionFinished),
            quietDeg: 0.5, reacquireDeg: 2.0));
    }

    [Fact]
    public void TheBandIsHysteretic()
    {
        var quiet = new CoastQuiet();

        // Outside the letting-go band to begin with, so it keeps pointing.
        Assert.False(quiet.Update(Holding(errorDeg: 1.0), quietDeg: 0.5, reacquireDeg: 2.0));

        // Inside it, so it lets go.
        Assert.True(quiet.Update(Holding(errorDeg: 0.4), quietDeg: 0.5, reacquireDeg: 2.0));

        // Drifted past the letting-go band but not past the taking-back one: still quiet, which is
        // what stops a settled bus re-commanding every time the error crosses a threshold.
        Assert.True(quiet.Update(Holding(errorDeg: 1.0), quietDeg: 0.5, reacquireDeg: 2.0));

        // Past the taking-back band.
        Assert.False(quiet.Update(Holding(errorDeg: 2.5), quietDeg: 0.5, reacquireDeg: 2.0));

        // And it has to earn the quiet again through the tighter band, not the wider one.
        Assert.False(quiet.Update(Holding(errorDeg: 1.0), quietDeg: 0.5, reacquireDeg: 2.0));
    }

    [Fact]
    public void AnUnreadablePointingErrorKeepsTheAttitude()
    {
        var quiet = new CoastQuiet();

        Assert.False(quiet.Update(Holding(errorDeg: double.NaN),
                                  quietDeg: 0.5, reacquireDeg: 2.0));
    }

    [Fact]
    public void AnAbsentReleaseMarginReadsAsApproachingRatherThanAsPlentyOfTime()
    {
        // A shot with no committed arrival has no margin, and reading that as "not yet" would keep
        // a bus quiet through its own deployment.
        Assert.True(CoastQuiet.InReleaseApproach(double.NaN, 60.0));
        Assert.True(CoastQuiet.InReleaseApproach(double.PositiveInfinity, 60.0));
    }

    [Fact]
    public void TheMarginIsWhatSeparatesApproachingFromNot()
    {
        Assert.True(CoastQuiet.InReleaseApproach(59.0, 60.0));
        Assert.True(CoastQuiet.InReleaseApproach(60.0, 60.0));
        Assert.False(CoastQuiet.InReleaseApproach(61.0, 60.0));
    }

    [Fact]
    public void ResetForgetsTheLatch()
    {
        var quiet = new CoastQuiet();

        Assert.True(quiet.Update(Holding(), quietDeg: 0.5, reacquireDeg: 2.0));
        quiet.Reset();
        Assert.False(quiet.IsQuiet);

        // Having forgotten, it has to come inside the tighter band again rather than the wider one.
        Assert.False(quiet.Update(Holding(errorDeg: 1.0), quietDeg: 0.5, reacquireDeg: 2.0));
    }
}
