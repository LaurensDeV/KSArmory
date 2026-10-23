using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>An ejector's push laid along its arms' stroke, and the arms drawn with the store.</summary>
public class EjectorTests
{
    private const double Speed = 6.0;
    private const double Stroke = 0.10;
    private static readonly double3 Down = new(0, 0, -1);

    // No air, no pull, the motor not yet lit: the push is the only thing moving it.
    private static Interceptor Ejected() =>
        new(Vec.Zero, Vec.Zero, null, 1, Vec.Zero, Vec.Zero)
        {
            Munition = BuiltIns.Missile120C,
            ReleaseHeadingEcl = new double3(1, 0, 0),
            LaunchFrameVelocityLocal = Down * Speed,
            MotorDelaySeconds = 0.5,
            EjectAccelerationEcl = Down * Ejector.Acceleration(Speed, Stroke),
            EjectSeconds = Ejector.StrokeSeconds(Speed, Stroke),
        };

    // The step is uneven so one sub-step straddles the end of the stroke. The travel is good to a
    // sub-step's half at the end speed, 1 cm here: the integrator moves on the velocity a step ends with.
    [Fact]
    public void TheStoreLeavesAtTheEjectionSpeedHavingTravelledTheStroke()
    {
        Interceptor round = Ejected();
        double strokeSeconds = Ejector.StrokeSeconds(Speed, Stroke);

        double t = 0.0;
        while (t < strokeSeconds + 0.013)
        {
            round.Update(0.007, null, Vec.Zero, Vec.Zero, Vec.Zero, BuiltIns.Missile120C, 0.0);
            t += 0.007;
        }

        Assert.Equal(Speed, -round.VelocityEcl.Z, 3);
        double travelledAtEnd = -round.TravelSinceLaunch.Z - (Speed * (t - strokeSeconds));
        Assert.InRange(travelledAtEnd, Stroke - 0.002, Stroke + 0.012);
    }

    [Theory]
    [InlineData(0.005)]
    [InlineData(0.015)]
    [InlineData(0.030)]
    public void TheArmsMoveWithTheStoreAlongTheStroke(double t)
    {
        double store = 0.5 * Ejector.Acceleration(Speed, Stroke) * t * t;

        Assert.Equal(store, Ejector.ArmOffset(t, Speed, Stroke, 0.8), 9);
    }

    [Fact]
    public void TheArmsHoldAtFullTravelThenComeBack()
    {
        double strokeSeconds = Ejector.StrokeSeconds(Speed, Stroke);

        Assert.Equal(Stroke, Ejector.ArmOffset(strokeSeconds + 0.1, Speed, Stroke, 0.8), 9);
        Assert.Equal(0.0, Ejector.ArmOffset(strokeSeconds + Ejector.HoldSeconds + 0.8, Speed, Stroke, 0.8), 9);
        Assert.Equal(0.0, Ejector.ArmOffset(-0.1, Speed, Stroke, 0.8), 9);
    }

    [Fact]
    public void AStepStraddlingTheEndOfTheStrokeCountsOnlyItsShareInside()
    {
        Assert.Equal(0.5, Ejector.ShareOfStep(0.02, 0.02, 0.03), 9);
        Assert.Equal(1.0, Ejector.ShareOfStep(0.0, 0.01, 0.03), 9);
        Assert.Equal(0.0, Ejector.ShareOfStep(0.04, 0.01, 0.03), 9);
        Assert.Equal(0.0, Ejector.ShareOfStep(0.0, 0.01, 0.0), 9);
    }
}
