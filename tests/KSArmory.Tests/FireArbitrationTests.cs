using Brutal.Numerics;
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
    // The Pantsir's envelopes: the cannon reach 4 km, the missiles start at 1.2 km.
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
    /// What this arbitration exists for: with the ring laid on the gun's lead, a missile leaving
    /// along the tube departs ~18 degrees off the target. Proportional navigation recovers, so
    /// nothing on screen shows it happening.
    /// </summary>
    [Fact]
    public void MissilesHoldWhileTheRingIsOnTheGunLead()
        => Assert.False(FireGate.MissilesMayFire(ringIsElsewhere: true, launchAlongTube: true));

    [Fact]
    public void MissilesFireWhenTheRingIsOnTheTarget()
        => Assert.True(FireGate.MissilesMayFire(ringIsElsewhere: false, launchAlongTube: true));

    /// <summary>
    /// A launcher that does not release along the tube is unaffected by where the ring points, so
    /// holding its missiles would cost a shot for nothing.
    /// </summary>
    [Fact]
    public void ALauncherThatDoesNotFireAlongTheTubeIsUnaffected()
        => Assert.True(FireGate.MissilesMayFire(ringIsElsewhere: true, launchAlongTube: false));

    /// <summary>
    /// A failed ballistic solve leaves the ring on the target, which is exactly what the missiles
    /// want — so the gate asks where the turret actually points, not whether the guns are in
    /// range. Getting this wrong holds missiles for an engagement the cannon never took over.
    /// </summary>
    [Fact]
    public void AFailedLeadSolveDoesNotHoldTheMissiles()
        => Assert.True(FireGate.MissilesMayFire(ringIsElsewhere: false, launchAlongTube: true));

    // ---- What a fixed launcher can be pointed at ------------------------

    // The AIM-9J's gimbal limit, which is the only thing deciding what a rail can shoot at.
    private static readonly double Fov = double.DegreesToRadians(40.0);

    private static bool CanGuide(double offAxisDeg, GuidanceMode guidance = GuidanceMode.Seeker,
                                 bool operatorHeld = false)
    {
        double a = double.DegreesToRadians(offAxisDeg);

        return FireGate.CanGuideOntoAimpoint(guidance, operatorHeld, Fov,
                                             launchDirection: new double3(1, 0, 0),
                                             toAimpoint: new double3(Math.Cos(a), Math.Sin(a), 0) * 6000.0);
    }

    /// <summary>
    /// A seeker round leaving a fixed tube can only be sent where its seeker can already see.
    ///
    /// <para>Outside the gimbal limit it never steers, so its flight path never changes, so it
    /// never comes back inside — the round simply flies away for its whole life. Nothing about
    /// that looks like a failure on screen: the launch is normal and the miss is silent, which is
    /// why the shot is refused at the launcher instead.</para>
    /// </summary>
    [Theory]
    [InlineData(0.0, true)]
    [InlineData(39.0, true)]
    [InlineData(41.0, false)]
    [InlineData(90.0, false)]
    [InlineData(150.0, false)]
    public void ASeekerRoundIsOnlySentWhereItsSeekerCanSee(double offAxisDeg, bool expected)
        => Assert.Equal(expected, CanGuide(offAxisDeg));

    /// <summary>
    /// A command-linked round has no such limit: the launcher steers it, so where it leaves says
    /// nothing about whether it can turn. The Pantsir fires every round this way, and applying the
    /// gate to it would ground the weapon that has been working all along.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(90.0)]
    [InlineData(179.0)]
    public void ACommandLinkedRoundHasNoSuchLimit(double offAxisDeg)
        => Assert.True(CanGuide(offAxisDeg, GuidanceMode.CommandLink));

    /// <summary>
    /// Nor does a place the operator designated, whatever the round carries. The seeker limit is
    /// about a round finding its own target; a designation is held for it, so there is nothing to
    /// lose sight of. Without this a rail can only shoot where it already points — which, bolted
    /// to a stack, is along the stack and nowhere anyone would aim.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(95.0)]
    [InlineData(116.0)]
    public void NorDoesAPlaceTheOperatorIsHolding(double offAxisDeg)
        => Assert.True(CanGuide(offAxisDeg, GuidanceMode.Seeker, operatorHeld: true));

    /// <summary>A degenerate direction is a refusal, not a NaN comparison that quietly passes.</summary>
    [Fact]
    public void ADegenerateDirectionIsRefused()
    {
        Assert.False(FireGate.CanGuideOntoAimpoint(GuidanceMode.Seeker, false, Fov, Vec.Zero, new double3(1, 0, 0)));
        Assert.False(FireGate.CanGuideOntoAimpoint(GuidanceMode.Seeker, false, Fov, new double3(1, 0, 0), Vec.Zero));
        Assert.False(FireGate.CanGuideOntoAimpoint(GuidanceMode.Seeker, false, Fov, new double3(1, 0, 0),
                                                   new double3(double.NaN, 0, 0)));
    }
}
