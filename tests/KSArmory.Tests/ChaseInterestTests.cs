using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class ChaseInterestTests
{
    private const double Frame = 1.0 / 60.0;

    // Where a site on Earth is, and how fast the ecliptic carries it — added to both sides of every
    // countdown, which must not notice.
    private static readonly double3 Far = new(1.5e11, 6.371e6, 0);
    private static readonly double3 Carrier = new(29_800 * 0.6, 29_800 * 0.8, 0);

    [Fact]
    public void TheCountdownIsRangeOverClosingSpeed()
    {
        double toGo = ChaseInterest.TimeToGo(Vec.Zero, new double3(700, 0, 0),
                                             new double3(7_000, 0, 0), new double3(-300, 0, 0));

        Assert.Equal(7.0, toGo, 1e-9);
    }

    [Fact]
    public void ARoundThatHasGonePastHasNothingToCountDownTo()
    {
        double toGo = ChaseInterest.TimeToGo(new double3(500, 0, 0), new double3(700, 0, 0),
                                             Vec.Zero, Vec.Zero);

        Assert.True(double.IsNaN(toGo), $"counting down {toGo:F1} s to something behind it");
    }

    /// <summary>
    /// Both positions and both velocities carry the ecliptic's ~29.8 km/s, and it cancels only in
    /// the subtraction. Adding it to both sides must not move the answer.
    /// </summary>
    [Fact]
    public void SharedMotionDoesNotReachTheCountdown()
    {
        var round = new double3(0, 0, 1_000);
        var roundVelocity = new double3(600, 0, -50);
        var target = new double3(6_000, 2_000, 1_000);
        var targetVelocity = new double3(-200, 100, 0);

        double still = ChaseInterest.TimeToGo(round, roundVelocity, target, targetVelocity);
        double carried = ChaseInterest.TimeToGo(round + Far, roundVelocity + Carrier,
                                                target + Far, targetVelocity + Carrier);

        Assert.True(double.IsFinite(still), "the case is not closing");
        Assert.Equal(still, carried, 1e-6);
    }

    [Fact]
    public void ARoundClosingOnSomethingIsNeverGivenUpOn()
    {
        var interest = new ChaseInterest();

        for (int frame = 0; frame < 60 * 60; frame++)
        {
            Assert.False(interest.Lost(stoppedByGround: false, timeToGo: 5.0, Frame));
        }
    }

    [Fact]
    public void ARoundGoingNowhereIsLetGoOfAfterTheGrace()
    {
        var interest = new ChaseInterest();
        int frames = 0;
        bool lost = false;

        while (!lost && frames < 60 * 60)
        {
            lost = interest.Lost(stoppedByGround: false, timeToGo: double.NaN, Frame);
            frames++;
        }

        Assert.True(lost, "never let go");
        Assert.InRange(frames * Frame, ChaseInterest.GraceSeconds - (0.5 * Frame),
                       ChaseInterest.GraceSeconds + (1.5 * Frame));
    }

    /// <summary>
    /// The grace is simulated time, because the camera is still riding the round. On wall clock a
    /// pause would cut away from a frozen world.
    /// </summary>
    [Fact]
    public void APausedWorldIsNeverLetGoOf()
    {
        var interest = new ChaseInterest();

        for (int frame = 0; frame < 60 * 60; frame++)
        {
            Assert.False(interest.Lost(stoppedByGround: false, timeToGo: double.NaN, 0.0));
        }
    }

    [Fact]
    public void ClosingAgainStartsTheGraceOver()
    {
        var interest = new ChaseInterest();
        int nearlyTheGrace = (int)(ChaseInterest.GraceSeconds / Frame) - 5;

        for (int frame = 0; frame < nearlyTheGrace; frame++) interest.Lost(false, double.NaN, Frame);

        interest.Lost(stoppedByGround: false, timeToGo: 3.0, Frame);

        for (int frame = 0; frame < nearlyTheGrace; frame++)
        {
            Assert.False(interest.Lost(false, double.NaN, Frame),
                         $"let go {frame} frames after it closed again");
        }
    }

    /// <summary>
    /// A bomb dropped on nothing ends by arriving, and the arrival is what the chase is for.
    /// </summary>
    [Fact]
    public void AStoreTheGroundStopsIsWatchedAllTheWayDown()
    {
        var interest = new ChaseInterest();

        for (int frame = 0; frame < 60 * 60; frame++)
        {
            Assert.False(interest.Lost(stoppedByGround: true, timeToGo: double.NaN, Frame));
        }
    }

    /// <summary>
    /// The case it is for, flown: a missile that goes forty metres past its target and flies on.
    /// It is ridden all the way in, and let go of the grace after it passes rather than when
    /// <c>MaxFlightSeconds</c> reaps it.
    /// </summary>
    [Fact]
    public void AMissileThatGoesPastItsTargetIsLetGoOfShortlyAfter()
    {
        var interest = new ChaseInterest();

        double3 round = Far;
        double3 roundVelocity = Carrier + new double3(700, 0, 0);
        double3 target = Far + new double3(3_000, 40, 0);
        double3 targetVelocity = Carrier;

        double passed = double.NaN;
        double letGo = double.NaN;

        for (int frame = 1; frame <= 60 * 20 && double.IsNaN(letGo); frame++)
        {
            round += roundVelocity * Frame;
            target += targetVelocity * Frame;

            double toGo = ChaseInterest.TimeToGo(round, roundVelocity, target, targetVelocity);
            if (double.IsNaN(passed) && !double.IsFinite(toGo)) passed = frame * Frame;

            if (interest.Lost(stoppedByGround: false, toGo, Frame)) letGo = frame * Frame;
        }

        Assert.False(double.IsNaN(letGo), "never let go of a missile that had gone past");

        // Closest approach is 3000 / 700 = 4.29 s in.
        Assert.InRange(passed, 4.25, 4.32);
        Assert.InRange(letGo - passed, ChaseInterest.GraceSeconds - (1.5 * Frame),
                       ChaseInterest.GraceSeconds + (1.5 * Frame));
    }
}
