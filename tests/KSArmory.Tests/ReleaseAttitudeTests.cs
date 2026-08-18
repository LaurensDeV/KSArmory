using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Which way a store points as it leaves is not which way it is pushed.
///
/// <para><see cref="MunitionProfile"/> has no say in this: the ejector bias belongs to the
/// launcher, and it exists so a store clears the rack before gravity takes it. Feeding that bias
/// to the round's <em>attitude</em> as well draws it leaving at an angle to the rack that is still
/// holding it — 50 degrees at the bomb rack's own 1.2, because the bias is added to a unit axis
/// and the boresight is square to the tube.</para>
///
/// <para>In air it is invisible, because the airflow straightens the store within a second. In
/// vacuum nothing corrects it and it is permanent.</para>
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

    /// <summary>
    /// And the attitude does not follow it. This is the pairing the release code has to keep apart:
    /// same launcher, same bias, two different answers.
    /// </summary>
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

        double3 drawn = BodyAttitude.Heading(fast, Tube, mediumDensityRatio: 0.0);

        Assert.Equal(0.0, Vec.AngleBetween(drawn, Tube), 9);
    }
}
