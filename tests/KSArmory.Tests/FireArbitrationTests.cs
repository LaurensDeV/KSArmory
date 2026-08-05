using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Which weapon owns an engagement, and what that costs the other one.
///
/// <para>The turret has one bearing. Inside the envelope overlap the cannon want it on a
/// ballistic lead and the missiles want it on the target, and only one of them can have it —
/// so "both are in range" is a fire-control decision rather than a happy accident.</para>
/// </summary>
public class FireArbitrationTests
{
    // The shipped Pantsir envelopes: the cannon reach 4 km, the missiles start at 1.2 km.
    private const double GunMin = 200.0;
    private const double GunMax = 4000.0;

    private static bool GunsHaveIt(double range, bool enabled = true, bool belt = true)
        => FireGate.GunsHaveTheEngagement(hasCannon: true, gunsEnabled: enabled,
                                          beltHasRounds: belt, range: range,
                                          gunMinRange: GunMin, gunMaxRange: GunMax);

    [Theory]
    [InlineData(200.0)]    // exactly the near edge
    [InlineData(2600.0)]   // mid overlap
    [InlineData(4000.0)]   // exactly the far edge
    public void TheCannonOwnAnEngagementInsideTheirEnvelope(double range)
        => Assert.True(GunsHaveIt(range));

    [Theory]
    [InlineData(199.0)]
    [InlineData(4001.0)]
    [InlineData(9000.0)]
    public void AndNotOneOutsideIt(double range)
        => Assert.False(GunsHaveIt(range));

    [Fact]
    public void ACannonSwitchedOffOwnsNothing()
        => Assert.False(GunsHaveIt(2600.0, enabled: false));

    [Fact]
    public void AnEmptyBeltOwnsNothing()
        => Assert.False(GunsHaveIt(2600.0, belt: false));

    [Fact]
    public void ALauncherWithNoCannonOwnsNothing()
        => Assert.False(FireGate.GunsHaveTheEngagement(
            hasCannon: false, gunsEnabled: true, beltHasRounds: true,
            range: 2600.0, gunMinRange: GunMin, gunMaxRange: GunMax));

    /// <summary>
    /// The defect this arbitration exists for: the ring is laid on the gun's lead, the missile
    /// leaves along the tube, and so it leaves ~18 degrees off the target. Proportional
    /// navigation recovers, which is why nothing measured it for so long.
    /// </summary>
    [Fact]
    public void MissilesHoldWhileTheRingIsOnTheGunLead()
        => Assert.False(FireGate.MissilesMayFire(ringIsOnGunLead: true, launchAlongTube: true));

    [Fact]
    public void MissilesFireWhenTheRingIsOnTheTarget()
        => Assert.True(FireGate.MissilesMayFire(ringIsOnGunLead: false, launchAlongTube: true));

    /// <summary>
    /// A launcher that does not release along the tube is unaffected by where the ring points, so
    /// holding its missiles would cost a shot for nothing.
    /// </summary>
    [Fact]
    public void ALauncherThatDoesNotFireAlongTheTubeIsUnaffected()
        => Assert.True(FireGate.MissilesMayFire(ringIsOnGunLead: true, launchAlongTube: false));

    /// <summary>
    /// A failed ballistic solve leaves the ring on the target, which is exactly what the missiles
    /// want — so the gate asks where the turret actually points, not whether the guns are in
    /// range. Getting this wrong holds missiles for an engagement the cannon never took over.
    /// </summary>
    [Fact]
    public void AFailedLeadSolveDoesNotHoldTheMissiles()
        => Assert.True(FireGate.MissilesMayFire(ringIsOnGunLead: false, launchAlongTube: true));
}
