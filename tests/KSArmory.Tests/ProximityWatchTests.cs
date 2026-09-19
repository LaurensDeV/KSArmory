using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What the bus's closest approach to its own spent stack was.
///
/// <para>The thing under test is a minimum over a whole coast, so every case here is about a
/// reading that is <em>not</em> the last one: a watch that reports where the two ended up rather
/// than where they were nearest would have said nothing about the 2026-08-25 collision, which
/// happened seconds before the warheads left and left the pair drifting apart again.</para>
/// </summary>
public class ProximityWatchTests
{
    private const double StageRadius = 8.0;
    private const double KeepOut = StageRadius + SeparationClearance.ClearOfTheSphereMetres;

    /// <summary>A watch nobody fed makes no claim, which is not the same as a claim of zero.</summary>
    [Fact]
    public void AWatchWithNoReadingsSaysSoRatherThanReportingZero()
    {
        ClosestApproach c = new ProximityWatch().Closest;

        Assert.False(c.Known);
        Assert.False(c.Breached);
        Assert.Contains("never read", c.Said);
    }

    /// <summary>
    /// The point of the whole class: it keeps the nearest approach, not the newest one. A pair that
    /// closes to inside the keep-out and drifts apart again is exactly the flight that has to be
    /// reported, and it is indistinguishable from a clean one at the end.
    /// </summary>
    [Fact]
    public void ItKeepsTheNearestApproachRatherThanTheLatest()
    {
        var watch = new ProximityWatch();

        foreach (double apart in new[] { 60.0, 30.0, 4.0, 25.0, 90.0, 400.0 })
        {
            watch.Update(1.0, apart, StageRadius);
        }

        ClosestApproach c = watch.Closest;

        Assert.True(c.Known);
        Assert.Equal(4.0, c.MetresApart, 6);
        Assert.Equal(3.0, c.AtSeconds, 6);
        Assert.True(c.Breached);
        Assert.Contains("INSIDE THE KEEP-OUT", c.Said);
    }

    /// <summary>And it says when, because a graze during the trim and one during the release are
    /// different faults with different fixes.</summary>
    [Fact]
    public void ItSaysWhenTheClosestApproachHappened()
    {
        var watch = new ProximityWatch();

        watch.Update(0.5, 100.0, StageRadius);
        watch.Update(0.5, 12.0, StageRadius);   // +1.0 s
        watch.Update(0.5, 80.0, StageRadius);

        Assert.Equal(1.0, watch.Closest.AtSeconds, 6);
    }

    /// <summary>
    /// An unreadable frame is not a distance of zero. A part tree mid-rebuild answers with nothing,
    /// and every flight has such frames right after the split — reading them as contact would
    /// report a collision on all of them.
    /// </summary>
    [Fact]
    public void AnUnreadableFrameContributesNoReadingAndStillAdvancesTheClock()
    {
        var watch = new ProximityWatch();

        watch.Update(1.0, double.NaN, double.NaN);
        watch.Update(1.0, double.NaN, double.NaN);

        Assert.False(watch.Closest.Known);

        watch.Update(1.0, 50.0, StageRadius);

        Assert.True(watch.Closest.Known);
        Assert.Equal(50.0, watch.Closest.MetresApart, 6);

        // The two blind frames still happened, so the reading is stamped at +3 s rather than +1.
        Assert.Equal(3.0, watch.Closest.AtSeconds, 6);
    }

    /// <summary>
    /// The keep-out comes from the stage the same way the clearance gate's does. Two derivations
    /// would drift apart, and then the watch would report safe about a distance the gate refused.
    /// </summary>
    [Theory]
    [InlineData(2.0)]
    [InlineData(40.0)]
    public void TheKeepOutIsTheSameDistanceTheClearanceGateWaitsFor(double radius)
    {
        double keepOut = ProximityWatch.KeepOutFor(radius);

        Assert.True(SeparationClearance.Check(keepOut, radius, 1.0).IsClear);
        Assert.False(SeparationClearance.Check(keepOut - 0.5, radius, 1.0).IsClear);
    }

    /// <summary>An unreadable stage still gets a keep-out, so a breach is still detectable.</summary>
    [Fact]
    public void AStageWhoseSizeCannotBeReadStillHasAKeepOut()
    {
        var watch = new ProximityWatch();
        watch.Update(1.0, SeparationClearance.FallbackMetres + 5.0, double.NaN);
        watch.Update(1.0, SeparationClearance.FallbackMetres - 1.0, double.NaN);

        Assert.True(watch.Closest.Breached);
        Assert.Equal(SeparationClearance.FallbackMetres, watch.Closest.KeepOutMetres, 6);
    }

    /// <summary>
    /// A pair that never parted has not breached a keep-out — it has failed to separate, which is
    /// a different fault and has to read as one.
    ///
    /// <para>This is the whole reason the watch arms. Every split begins with the halves adjacent,
    /// so a minimum taken from the first frame is the split: across 94 recorded flights it read
    /// <c>2.0 m at +0.0 s -- INSIDE THE KEEP-OUT</c>, identically, on every one. An instrument
    /// with a single possible output cannot detect anything.</para>
    /// </summary>
    [Fact]
    public void APairThatNeverPartedIsNotABreach()
    {
        var watch = new ProximityWatch();

        // What every flight actually looks like from the split onward when it never gets clear.
        foreach (double apart in new[] { 2.0, 3.5, 6.0, 9.0, 11.0, 13.0 })
        {
            watch.Update(1.0, apart, StageRadius);
        }

        ClosestApproach c = watch.Closest;

        Assert.False(c.Opened);
        Assert.False(c.Breached);
        Assert.False(c.Known);
        Assert.Contains("never opened past", c.Said);
        Assert.DoesNotContain("CAME BACK", c.Said);
    }

    /// <summary>And once it has parted, coming back is caught — which is the event it exists for.</summary>
    [Fact]
    public void OnceTheyHavePartedComingBackIsCaught()
    {
        var watch = new ProximityWatch();

        watch.Update(1.0, KeepOut + 20.0, StageRadius);
        watch.Update(1.0, 3.0, StageRadius);

        ClosestApproach c = watch.Closest;

        Assert.True(c.Opened);
        Assert.True(c.Breached);
        Assert.Equal(3.0, c.MetresApart, 6);
        Assert.Contains("CAME BACK INSIDE THE KEEP-OUT", c.Said);
    }

    /// <summary>A pair that never came near reports the number and does not shout.</summary>
    [Fact]
    public void AFlightThatStayedClearReportsItsMarginWithoutComplaint()
    {
        var watch = new ProximityWatch();
        watch.Update(1.0, KeepOut + 5.0, StageRadius);

        Assert.True(watch.Closest.Known);
        Assert.False(watch.Closest.Breached);
        Assert.DoesNotContain("INSIDE", watch.Closest.Said);
    }

    /// <summary>A new split is a new pair, so nothing carries over from the last one.</summary>
    [Fact]
    public void ResetForgetsTheLastSplit()
    {
        var watch = new ProximityWatch();
        watch.Update(1.0, KeepOut + 5.0, StageRadius);
        watch.Update(1.0, 2.0, StageRadius);
        Assert.True(watch.Closest.Breached);

        watch.Reset();

        Assert.False(watch.Closest.Known);
        Assert.False(watch.Closest.Breached);
    }
}
