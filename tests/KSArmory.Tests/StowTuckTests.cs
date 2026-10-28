using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class StowTuckTests
{
    private const double Dt = 0.02;

    private static StowTuck TuckedFully(OpticProfile profile)
    {
        StowTuck stow = new();
        for (int i = 0; i < 500; i++) stow.Step(Dt, parkedAndSettled: true, profile.StowTuckRad, profile.SlewRateRad);
        return stow;
    }

    [Fact]
    public void AParkedLiteningLooksUpAndAftIntoItsNose()
    {
        OpticProfile pod = BuiltIns.Litening;
        double3 rest = OpticGeometry.RestAim(pod, MountFrame.Fixed);
        StowTuck stow = TuckedFully(pod);

        double3 look = OpticGeometry.Pose(pod, MountFrame.Fixed, rest, stow.Rad).Rotation * OpticGeometry.RestDirection;

        Assert.Equal(pod.StowTuckRad, stow.Rad, 9);
        Assert.True(Vec.Dot(look, -MountFrame.Fixed.Forward) > 0.6, $"looks {look}, not aft");
        Assert.True(Vec.Dot(look, -MountFrame.Fixed.Normal) > 0.6, $"looks {look}, not up into the nose");
    }

    [Fact]
    public void TheTuckLeavesTheSlotFacingWhereTheRestAimPutIt()
    {
        OpticProfile pod = BuiltIns.Litening;
        double3 rest = OpticGeometry.RestAim(pod, MountFrame.Fixed);

        Assert.Equal(0.0, OpticGeometry.RollAngleRad(MountFrame.Fixed, rest), 9);
    }

    [Fact]
    public void ItUntucksBeforeTheDriveMayMove()
    {
        OpticProfile pod = BuiltIns.Litening;
        StowTuck stow = TuckedFully(pod);

        int held = 0;
        while (!stow.DriveFree && held < 1000)
        {
            stow.Step(Dt, parkedAndSettled: false, pod.StowTuckRad, pod.SlewRateRad);
            held++;
        }

        double seconds = held * Dt;
        Assert.InRange(seconds, pod.StowTuckDeg / pod.SlewRateDeg - Dt, pod.StowTuckDeg / pod.SlewRateDeg + Dt);
    }

    [Fact]
    public void ItTucksOnlyOnceParkedAndSettled()
    {
        OpticProfile pod = BuiltIns.Litening;
        StowTuck stow = new();
        for (int i = 0; i < 100; i++) stow.Step(Dt, parkedAndSettled: false, pod.StowTuckRad, pod.SlewRateRad);

        Assert.Equal(0.0, stow.Rad);
        Assert.True(stow.DriveFree);
    }

    [Fact]
    public void AHeadWithNoTuckNeverHoldsItsDrive()
    {
        OpticProfile mast = BuiltIns.EoDirector;
        StowTuck stow = TuckedFully(mast);

        Assert.Equal(0.0, stow.Rad);
        Assert.True(stow.DriveFree);
    }

    [Fact]
    public void ZeroTuckLeavesThePoseAsItWas()
    {
        OpticProfile pod = BuiltIns.Litening;
        double3 aim = Vec.Unit(new double3(0.6, 0.5, 0.3));

        Assert.Equal(OpticGeometry.Pose(pod, MountFrame.Fixed, aim).Rotation,
                     OpticGeometry.Pose(pod, MountFrame.Fixed, aim, 0.0).Rotation);
    }
}
