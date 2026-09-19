using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A contact's acceleration from its velocity history, which a burst's kick cannot throw.
/// </summary>
public class AccelerationEstimateTests
{
    private const double Frame = 1.0 / 60.0;

    // Earth's motion round the Sun, so a velocity that carries it is still differenced to the truth.
    private static readonly double3 Orbital = new(0, 29_800, 0);
    private static readonly double3 Coasting = new(-8.0, 3.0, -9.80665);

    private static AccelerationEstimate Flown(double seconds, double3 acceleration, double3 start)
    {
        var estimate = new AccelerationEstimate();
        double3 velocity = start;
        for (double t = 0.0; t <= seconds; t += Frame)
        {
            estimate.Add(velocity, Frame);
            velocity += acceleration * Frame;
        }

        return estimate;
    }

    [Fact]
    public void ASteadyAccelerationIsReadExactly()
    {
        AccelerationEstimate estimate = Flown(1.0, Coasting, Orbital + new double3(250, 0, 0));

        Assert.True(estimate.TryEstimate(out double3 a));
        Assert.True(Vec.Len(a - Coasting) < 1e-6, $"read {a}");
    }

    [Fact]
    public void AKickFromABurstIsOutvoted()
    {
        var estimate = new AccelerationEstimate();
        double3 velocity = Orbital + new double3(250, 0, 0);
        var kick = new double3(0, 0, 10.0);   // a burst shoves the craft up 10 m/s in one frame

        for (int i = 0; i < 60; i++)
        {
            estimate.Add(velocity, Frame);
            velocity += Coasting * Frame;
            if (i == 45) velocity += kick;
        }

        Assert.True(estimate.TryEstimate(out double3 a));

        // The same samples averaged carry the kick across the whole window.
        double meanError = Vec.Len(kick) / AccelerationEstimate.WindowSeconds;
        Assert.True(meanError > 10.0, "a kick this size has to be one a mean could not hide");
        Assert.True(Vec.Len(a - Coasting) < 1e-6, $"the kick moved the estimate to {a}");
    }

    [Fact]
    public void ADecelerationThatBuildsIsFollowedWithinTheWindow()
    {
        var estimate = new AccelerationEstimate();
        double3 velocity = Orbital + new double3(250, 0, 0);
        var braking = new double3(-20.0, 0, -9.80665);

        for (double t = 0.0; t < 2.0; t += Frame)
        {
            estimate.Add(velocity, Frame);
            velocity += (t < 1.0 ? Coasting : braking) * Frame;
        }

        Assert.True(estimate.TryEstimate(out double3 a));
        Assert.True(Vec.Len(a - braking) < 1e-6, $"still reading {a} a second after the change");
    }

    /// <summary>
    /// The history lags a drag that is still changing, and used as the reading that lag put every
    /// first shell 16 m behind its drone. An accelerometer that agrees within the limit is kept.
    /// </summary>
    [Fact]
    public void AnAccelerometerThatAgreesIsBelievedOverTheLaggingHistory()
    {
        var estimate = new AccelerationEstimate();
        double3 velocity = Orbital + new double3(250, 0, 0);
        double3 now = Coasting;

        for (double t = 0.0; t < 1.0; t += Frame)
        {
            now = Coasting + new double3(-1.2 * t, 0, 0);   // drag easing off at a drone's rate
            estimate.Add(velocity, Frame);
            velocity += now * Frame;
        }

        Assert.True(estimate.TryEstimate(out double3 history));
        Assert.True(Vec.Len(history - now) > 0.1, "this needs a history that lags the accelerometer");

        Assert.Equal(now, estimate.Believe(now));
    }

    [Fact]
    public void AnAccelerometerThatDisagreesWithHowTheVelocityChangedIsOverruled()
    {
        AccelerationEstimate estimate = Flown(1.0, Coasting, Orbital);
        var tumbling = Coasting + new double3(0, 0, 7.0);

        Assert.True(Vec.Len(estimate.Believe(tumbling) - Coasting) < 1e-6);
    }

    [Fact]
    public void WithoutHistoryTheAccelerometerIsAllThereIs()
    {
        var estimate = new AccelerationEstimate();
        var reading = new double3(0, 0, 30.0);

        Assert.Equal(reading, estimate.Believe(reading));
    }

    [Fact]
    public void TooFewSamplesGiveNoEstimate()
    {
        var estimate = new AccelerationEstimate();
        for (int i = 0; i < AccelerationEstimate.MinSamples; i++) estimate.Add(Orbital, Frame);

        // MinSamples velocities make one fewer difference.
        Assert.False(estimate.TryEstimate(out _));
    }

    [Fact]
    public void AStoppedClockAddsNothing()
    {
        AccelerationEstimate estimate = Flown(1.0, Coasting, Orbital);
        estimate.Add(Orbital * 2.0, 0.0);   // a paused frame with a nonsense velocity

        Assert.True(estimate.TryEstimate(out double3 a));
        Assert.True(Vec.Len(a - Coasting) < 1e-6, $"read {a}");
    }
}
