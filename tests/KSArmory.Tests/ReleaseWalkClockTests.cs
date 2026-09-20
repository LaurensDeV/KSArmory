using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What the coast a set is planned against has to mean, and what it costs to get it wrong.
///
/// <para><b>The flight this exists for.</b> Two targets, 3 warheads each, flown against
/// <c>SOLVER SCALE 8</c>: the plan was made and printed — <c>2 stops, 20.3 m/s of 60</c> — and then
/// nothing of the loop ran. All six warheads went to the lead, on all eight rockets, and the log
/// held not one <c>re-aiming at target</c>, <c>released n of m</c> or <c>walk complete</c> line.
/// </para>
/// </summary>
public class ReleaseWalkClockTests(ITestOutputHelper Out)
{
    private static double Gate => new IcbmConfig().ReleaseBeforeArrivalSeconds;

    private static double Hop => ReleaseItinerary.MedianHopSeconds;

    private static double[] Reach => [410.0];

    // Two targets 4 km apart, three warheads each, the bus aimed at entry 0.
    private static ReleaseItinerary.Target[] TwoTargets()
        => [new(3, 8000.0, 0.0), new(3, 4000.0, 4000.0)];

    private static ReleaseWalk PlanAt(double coastSeconds)
        => ReleaseLoop.Plan(TwoTargets(),
                            new ReleaseItinerary.Bus(Gate, coastSeconds, Warheads: 6),
                            Reach, lead: 0);

    /// <summary>
    /// <b>The bug, as arithmetic.</b> <see cref="ReleaseItinerary.CoastFits"/> asks whether
    /// <c>CoastSeconds - GateSeconds</c> holds another hop, so a coast that is the time <em>left</em>
    /// rather than the coast's own length cuts the set to one stop at
    /// <c>Gate + Hop</c> — which is exactly <see cref="ReleaseItinerary.FirstBeforeArrivalSeconds"/>,
    /// the instant the first release is due.
    /// </summary>
    [Fact]
    public void ACountdownCutsTheWalkAtTheInstantItShouldStart()
    {
        double due = PlanAt(1315.0).Itinerary.FirstBeforeArrivalSeconds;

        Assert.Equal(Gate + Hop, due, 6);

        // One frame before the release is due, and one frame after. Nothing about the trajectory
        // changed between them: only the clock ran down.
        ReleaseWalk justBefore = PlanAt(due + 0.05);
        ReleaseWalk justAfter = PlanAt(due - 0.05);

        Assert.True(justBefore.Walks, "the walk is still a walk while the release is not yet due");
        Assert.False(justAfter.Walks, "and a tenth of a second later it is not");
        Assert.Equal(ReleaseWalkHold.OneStop, justAfter.Hold);

        Out.WriteLine($"first release due at {due:F1} s to go; "
                      + $"{due + 0.05:F2} -> {justBefore.Stops} stops, "
                      + $"{due - 0.05:F2} -> {justAfter.Stops}");
    }

    /// <summary>
    /// <b>The property the flight needs.</b> Planned against the coast's own length — what it was at
    /// cutoff — the count is settled, and no amount of the clock running down moves it.
    /// </summary>
    /// <remarks>
    /// <see cref="ReleaseItinerary.Bus.CoastSeconds"/> says it is measured "from the earliest release
    /// the flight allows", so it is a property of the trajectory rather than a countdown, and
    /// <see cref="ReleaseWalker.CoastForPlanning"/> is what holds it to that.
    /// </remarks>
    [Fact]
    public void ACoastLatchedAtCutoffSettlesTheCountForTheWholeFlight()
    {
        ReleaseWalker walker = new();
        int atCutoff = 0;

        // A coast of 1,315 s run down past the first release, as the flight runs it down.
        for (double toArrival = 1315.0; toArrival > 300.0; toArrival -= 7.5)
        {
            double coast = walker.CoastForPlanning(toArrival, coasting: true);
            ReleaseWalk walk = PlanAt(coast);

            if (atCutoff == 0) atCutoff = walk.Stops;

            Assert.Equal(1315.0, coast, 6);
            Assert.Equal(atCutoff, walk.Stops);
            Assert.True(walk.Walks);
        }

        Assert.Equal(2, atCutoff);
        Out.WriteLine($"{atCutoff} stops, held across the whole coast");
    }

    /// <summary>Nothing is latched before there is a coast to latch, and a reset forgets it.</summary>
    [Fact]
    public void TheCoastIsUnknownBeforeCutoffAndForgottenOnANewShot()
    {
        ReleaseWalker walker = new();

        // Before cutoff there is no coast, and CoastFits is then unbounded rather than one.
        Assert.True(double.IsNaN(walker.CoastForPlanning(1315.0, coasting: false)));
        Assert.True(double.IsNaN(walker.CoastForPlanning(double.NaN, coasting: true)));
        Assert.True(double.IsNaN(walker.CoastForPlanning(0.0, coasting: true)));

        Assert.Equal(900.0, walker.CoastForPlanning(900.0, coasting: true), 6);
        Assert.Equal(900.0, walker.CoastForPlanning(120.0, coasting: true), 6);

        walker.Reset();
        Assert.Equal(120.0, walker.CoastForPlanning(120.0, coasting: true), 6);
    }

    /// <summary>
    /// <b>The other half of what nothing pinned.</b> A two-stop walk bounds the sequencer to the
    /// stop's quota and advances a stop when that quota is met — which is what the flight showed it
    /// was not doing, six warheads leaving consecutively for one target.
    /// </summary>
    [Fact]
    public void ATwoStopWalkBoundsTheMagazineAndAdvances()
    {
        ReleaseWalker walker = new();
        walker.Plan(PlanAt(1315.0));

        Assert.True(walker.Walking);
        Assert.Equal(2, walker.Walk.Stops);

        // The whole magazine is six; the first stop may have three of them and no more.
        Assert.Equal(3, walker.TubesLeft(6));
        Assert.Equal(0, walker.TargetIndex(99));

        for (int n = 0; n < 3; n++)
        {
            Assert.True(walker.Step.ReleaseHere, $"warhead {n + 1} of the first stop");
            walker.WarheadAway();
        }

        Assert.Equal(0, walker.TubesLeft(6));
        Assert.False(walker.Step.ReleaseHere);
        Assert.True(walker.Step.Handover);
        Assert.Equal(1, walker.Step.NextTarget);

        walker.Advance();

        Assert.Equal(1, walker.Stop);
        Assert.Equal(1, walker.TargetIndex(99));
        Assert.Equal(3, walker.TubesLeft(3));

        for (int n = 0; n < 3; n++) walker.WarheadAway();

        Assert.True(walker.Step.Finished);
        Assert.False(walker.Step.Handover);

        walker.Finish();
        Assert.True(walker.Done);
        Assert.Equal(0, walker.TubesLeft(6));
    }
}
