using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class RoundSpinTests
{
    private static readonly RoundSpin Spin = BuiltIns.MirvBus.ReleaseSpin!;

    [Fact]
    public void NothingRollsBeforeRelease()
    {
        Assert.Equal(0.0, Spin.RollRadAt(0.0));
        Assert.Equal(0.0, Spin.RollRadAt(-1.0));
    }

    [Fact]
    public void TheMotorsLightOnlyOnceTheRoundIsClearOfTheBus()
    {
        Assert.Equal(0.0, Spin.RollRadAt(Spin.IgnitionSeconds));
        Assert.False(Spin.Burning(Spin.IgnitionSeconds - 0.01));
        Assert.True(Spin.Burning(Spin.IgnitionSeconds + 0.01));
        Assert.False(Spin.Burning(Spin.IgnitionSeconds + Spin.SpinUpSeconds));
        Assert.True(Spin.IgnitionSeconds * BuiltIns.ReentryVehicleMk21.LaunchSpeed >= 1.0);
    }

    [Fact]
    public void TheRollIsContinuousWhereTheMotorsBurnOut()
    {
        double t = Spin.IgnitionSeconds + Spin.SpinUpSeconds;
        Assert.Equal(Spin.RollRadAt(t - 1e-9), Spin.RollRadAt(t + 1e-9), 6);
    }

    [Fact]
    public void TheRateClimbsAcrossTheBurnThenHolds()
    {
        double rate = double.DegreesToRadians(Spin.RateDegPerSecond);
        double early = (Spin.RollRadAt(Spin.IgnitionSeconds + 0.11) - Spin.RollRadAt(Spin.IgnitionSeconds + 0.10)) / 0.01;
        double late = (Spin.RollRadAt(30.0) - Spin.RollRadAt(29.0)) / 1.0;

        Assert.True(Math.Abs(early) < Math.Abs(rate) * 0.5, $"{early} rad/s a tenth of a second in");
        Assert.Equal(rate, late, 9);
    }

    [Fact]
    public void ARollTurnsTheBodyAboutItsOwnNoseByTheRightHandRule()
    {
        doubleQuat attitude = doubleQuat.CreateFromAxisAngle(Vec.Unit(new double3(0.3, -1.0, 0.4)), 1.1);
        doubleQuat rolled = BodyAttitude.Roll(attitude, Math.PI / 2.0);

        double3 nose = attitude * FireGeometry.NoseAxis;
        Assert.True(Vec.Len(rolled * FireGeometry.NoseAxis - nose) < 1e-12);

        // A positive quarter turn about +X carries the mesh's +Y onto where its +Z was.
        double3 wasZ = attitude * new double3(0, 0, 1);
        Assert.True(Vec.Len(rolled * new double3(0, 1, 0) - wasZ) < 1e-12);
    }

    [Fact]
    public void TheNozzlesSpinTheBodyTheWayItsRateSaysAndPushItClearOfTheBus()
    {
        double3 torque = Vec.Zero, force = Vec.Zero;
        foreach (SpinNozzle n in Spin.Nozzles)
        {
            double3 thrust = -Vec.Unit(n.ExhaustBody);
            force += thrust;
            torque += Vec.Cross(n.MouthBody, thrust);
        }

        Assert.Equal(Math.Sign(Spin.RateDegPerSecond), Math.Sign(torque.X));
        Assert.True(Math.Abs(torque.Y) < 1e-4 && Math.Abs(torque.Z) < 1e-4, $"torque {torque}");
        Assert.True(Math.Abs(force.Y) < 1e-4 && Math.Abs(force.Z) < 1e-4, $"force {force}");
        Assert.True(force.X > 0.0, "the jets push the RV forward, away from the bus behind it");
    }

    [Fact]
    public void OnlyTheBusWhoseRvsCarrySpinMotorsSpinsThem()
    {
        foreach (LauncherProfile launcher in Catalogue.Launchers)
            Assert.True((launcher.ReleaseSpin is not null) == ReferenceEquals(launcher, BuiltIns.MirvBus), launcher.DisplayName);
    }
}
