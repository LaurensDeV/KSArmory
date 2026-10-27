using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The launcher's ejector bias pushes a store clear of the rack but must not turn it: fed to the attitude
/// it leaves 50 deg off the rack at the rack's 1.2 (a bias on a unit axis), which in vacuum nothing corrects.
/// </summary>
public class ReleaseAttitudeTests
{
    private static readonly double3 Tube = new(0, 1, 0);
    private static readonly double3 Boresight = new(1, 0, 0);   // square to the tube, as a rack is

    [Fact]
    public void TheEjectorBiasTiltsTheDirectionAStoreIsPushed()
    {
        double3 pushed = FireGeometry.LaunchDirection(
            alongTube: true, Tube, double3.Zero, double3.Zero, Boresight, loft: 0.0,
            ejectAway: 1.2);

        double off = double.RadiansToDegrees(Vec.AngleBetween(pushed, Tube));

        Assert.True(off > 45.0,
                    $"the ejector should push a store well off its rack, pushed only {off:F1} deg");
        Assert.Equal(Math.Atan(1.2), Vec.AngleBetween(pushed, Tube), 6);
    }

    /// <summary>Same launcher, same bias: the push follows it and the attitude does not.</summary>

    [Fact]
    public void ButAStorePointsAlongItsRack()
    {
        double3 pushed = FireGeometry.LaunchDirection(
            alongTube: true, Tube, double3.Zero, double3.Zero, Boresight, loft: 0.0,
            ejectAway: 1.2);
        double3 heading = Vec.Unit(Tube);           // what WeaponSystem hands the round

        Assert.Equal(0.0, Vec.AngleBetween(heading, Tube), 9);
        Assert.True(Vec.AngleBetween(pushed, heading) > 0.5,
                    "the test is vacuous unless the two genuinely differ");
    }

    /// <summary>
    /// With no air there is nothing to correct it, so whatever it is released pointing at is what
    /// it keeps — which is why the attitude has to be right at the instant it leaves.
    /// </summary>
    [Fact]
    public void InVacuumTheReleaseAttitudeIsPermanent()
    {
        double3 fast = new(0, 0, 900);              // nothing like the release heading

        doubleQuat released = TubeGeometry.ReleaseAttitudeEcl(Tube, doubleQuat.Identity, doubleQuat.Identity);
        doubleQuat drawn = BodyAttitude.Turn(released, fast, mediumDensityRatio: 0.0, seconds: 60.0);

        Assert.Equal(0.0, Vec.AngleBetween(drawn * FireGeometry.NoseAxis, Tube), 9);
    }
}
