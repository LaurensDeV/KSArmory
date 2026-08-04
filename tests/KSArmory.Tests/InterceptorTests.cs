using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Exercises the round's guidance, seeker and fuse headlessly. These are the parts that are
/// hard to judge by eye in-game: whether the law actually leads a crossing target, and whether
/// the fuse survives the closing speeds involved.
/// </summary>
public class InterceptorTests
{
    private static readonly double3 NoGravity = new(0, 0, 0);

    /// <summary>Stand-in for a KSA Vehicle; the round only ever compares it by reference.</summary>
    private static readonly object TargetHandle = new();

    /// <summary>
    /// Flies an engagement to completion. The target holds a constant course, which is the
    /// case proportional navigation is built for.
    /// </summary>
    private static Result Engage(
        double3 launchPos,
        double3 launchVel,
        double3 targetPos,
        double3 targetVel,
        MunitionProfile munition,
        double3 gravity = default,
        double targetRadius = 5.0,
        double frameDt = 1.0 / 60.0,
        double timeout = 30.0,
        double3 frameVelocityEcl = default)
    {
        var round = new Interceptor(launchPos, launchVel, TargetHandle, tube: 1, platformEcl: default,
                                    frameVelocityEcl: default);
        double t = 0.0;
        double closest = double.MaxValue;

        while (round.State == RoundState.Flying && t < timeout)
        {
            double3 currentTargetPos = targetPos + targetVel * t;
            closest = Math.Min(closest, Vec.Len(currentTargetPos - round.PositionEcl));

            round.Update(frameDt, new TargetState(currentTargetPos, targetVel, targetRadius),
                gravity, frameVelocityEcl, platformEcl: default, munition);
            t += frameDt;
        }

        return new Result(round.State, round.MissDistance, closest, t, round.PositionEcl, round.HasLock);
    }

    private readonly record struct Result(
        RoundState State, double MissDistance, double ClosestApproach,
        double Elapsed, double3 EndPosition, bool HasLock);

    /// <summary>Vacuum-like defaults keep the tests about guidance rather than drag tuning.</summary>
    /// <summary>A round with no drag, so the geometry under test is not muddied by it.</summary>
    private static MunitionProfile Vacuum() => new() { Name = "test", DisplayName = "test", DragK = 0f };

    [Fact]
    public void HeadOnTarget_IsIntercepted()
    {
        MunitionProfile munition = Vacuum();

        // Target 3 km out on +X, closing at 300 m/s. Round fired straight at it.
        var result = Engage(
            launchPos: new double3(0, 0, 0),
            launchVel: new double3(munition.LaunchSpeed, 0, 0),
            targetPos: new double3(3000, 0, 0),
            targetVel: new double3(-300, 0, 0),
            munition);

        Assert.Equal(RoundState.Detonated, result.State);
        Assert.True(result.MissDistance <= munition.FuseRadius + 5.0,
            $"miss distance {result.MissDistance:F1} m exceeded the fuse trigger");
    }

    /// <summary>
    /// The case the brief called out: a target passing by rather than flying at us.
    /// Pure pursuit tail-chases and misses here; proportional navigation must lead.
    /// </summary>
    [Fact]
    public void CrossingTarget_IsLed_AndIntercepted()
    {
        MunitionProfile munition = Vacuum();

        // Target sits 2.5 km dead ahead at launch, then runs across the line of fire at 250 m/s
        // with no closing component at all. Flying straight arrives about a kilometre behind it;
        // see GuidanceDiscriminationTests for the unguided control case.
        var result = Engage(
            launchPos: new double3(0, 0, 0),
            launchVel: new double3(munition.LaunchSpeed, 0, 0),
            targetPos: new double3(2500, 0, 0),
            targetVel: new double3(0, 250, 0),
            munition);

        Assert.Equal(RoundState.Detonated, result.State);
        Assert.True(result.MissDistance <= munition.FuseRadius + 5.0,
            $"miss distance {result.MissDistance:F1} m exceeded the fuse trigger");
    }

    [Fact]
    public void ClimbingTarget_IsIntercepted_UnderGravity()
    {
        MunitionProfile munition = Vacuum();

        // 9.8 m/s^2 pulling down -Z, target climbing away. Guidance must not be dragged short.
        var result = Engage(
            launchPos: new double3(0, 0, 0),
            launchVel: new double3(0, 0, munition.LaunchSpeed),
            targetPos: new double3(600, 300, 2500),
            targetVel: new double3(-120, -60, 90),
            munition,
            gravity: new double3(0, 0, -9.80665));

        Assert.Equal(RoundState.Detonated, result.State);
        Assert.True(result.MissDistance <= munition.FuseRadius + 5.0,
            $"miss distance {result.MissDistance:F1} m exceeded the fuse trigger");
    }

    /// <summary>
    /// At 2 km/s of closing speed a 1/30 s frame covers ~67 m, far more than the fuse radius.
    /// Sub-stepping plus the closest-approach fuse must still trigger rather than tunnel through.
    /// </summary>
    [Fact]
    public void VeryHighClosingSpeed_DoesNotTunnelThroughTheFuse()
    {
        MunitionProfile munition = Vacuum();

        var result = Engage(
            launchPos: new double3(0, 0, 0),
            launchVel: new double3(600, 0, 0),
            targetPos: new double3(8000, 0, 0),
            targetVel: new double3(-1500, 0, 0),
            munition,
            frameDt: 1.0 / 30.0);

        Assert.Equal(RoundState.Detonated, result.State);
        Assert.True(result.MissDistance <= munition.FuseRadius + 5.0,
            $"miss distance {result.MissDistance:F1} m exceeded the fuse trigger");
    }

    /// <summary>
    /// Regression: the round must behave identically when the whole engagement is carried
    /// along by a fast-moving frame.
    ///
    /// Ecliptic velocities near Earth are dominated by ~29.8 km/s of solar orbit. Treating that
    /// as the round's own airspeed and heading made drag see Mach 87, broke the seeker lock on
    /// the first step (the line of sight is nowhere near Earth's orbital vector), and sent the
    /// round coasting 84 km in a straight line. Positions were unaffected because those are
    /// differences — only the places using absolute velocity as a heading were wrong.
    /// </summary>
    [Fact]
    public void EngagementIsUnchanged_WhenCarriedByAFastMovingFrame()
    {
        MunitionProfile munition = Vacuum();

        // Earth's orbital velocity, roughly. Everything is offset by it: the round, the target,
        // and the declared frame. The relative geometry is identical.
        double3 frame = new(29800, 0, 0);

        var stationary = Engage(
            launchPos: new double3(0, 0, 0),
            launchVel: new double3(munition.LaunchSpeed, 0, 0),
            targetPos: new double3(2500, 0, 0),
            targetVel: new double3(0, 250, 0),
            munition);

        var carried = Engage(
            launchPos: new double3(0, 0, 0),
            launchVel: new double3(munition.LaunchSpeed, 0, 0) + frame,
            targetPos: new double3(2500, 0, 0),
            targetVel: new double3(0, 250, 0) + frame,
            munition,
            frameVelocityEcl: frame);

        Assert.Equal(RoundState.Detonated, carried.State);
        Assert.Equal(stationary.State, carried.State);
        Assert.True(Math.Abs(carried.MissDistance - stationary.MissDistance) < 1.0,
            $"miss distance changed with the frame: {stationary.MissDistance:F1} m vs {carried.MissDistance:F1} m");
    }

    [Fact]
    public void Fuse_StaysSafe_UntilArmed()
    {
        // Target sitting right on top of the launcher: without the arming delay this would
        // detonate on the first step and take the platform with it.
        MunitionProfile munition = Vacuum();
        munition.FuseArmSeconds = 1.0f;

        var round = new Interceptor(new double3(0, 0, 0), new double3(50, 0, 0), TargetHandle, 1, default, default);
        var target = new TargetState(new double3(5, 0, 0), new double3(50, 0, 0), 1.0);

        round.Update(0.1, target, NoGravity, frameVelocityEcl: default, platformEcl: default, munition);

        Assert.Equal(RoundState.Flying, round.State);
        Assert.True(round.Age < munition.FuseArmSeconds);
    }

    [Fact]
    public void Seeker_BreaksLock_WhenTargetLeavesItsFieldOfView()
    {
        MunitionProfile munition = Vacuum();
        munition.Guidance = GuidanceMode.Seeker;   // the default round is command-linked
        munition.SeekerFovDeg = 20f;

        // Round flying +X; target sits hard abeam on +Y, well outside a 20 degree seeker.
        var round = new Interceptor(new double3(0, 0, 0), new double3(500, 0, 0), TargetHandle, 1, default, default);
        var target = new TargetState(new double3(0, 2000, 0), new double3(0, 0, 0), 5.0);

        round.Update(1.0 / 60.0, target, NoGravity, frameVelocityEcl: default, platformEcl: default, munition);

        Assert.False(round.HasLock);
        Assert.Equal(RoundState.Flying, round.State);
    }

    [Fact]
    public void LostTarget_LeavesRoundCoasting_ThenExpires()
    {
        MunitionProfile munition = Vacuum();
        munition.MaxFlightSeconds = 3f;

        var round = new Interceptor(new double3(0, 0, 0), new double3(200, 0, 0), TargetHandle, 1, default, default);

        double t = 0.0;
        while (round.State == RoundState.Flying && t < 10.0)
        {
            round.Update(1.0 / 60.0, target: null, NoGravity, frameVelocityEcl: default, platformEcl: default, munition);
            t += 1.0 / 60.0;
        }

        Assert.Equal(RoundState.Expired, round.State);
        Assert.False(round.HasLock);
        Assert.True(Vec.IsFinite(round.PositionEcl));
    }

    [Fact]
    public void UnreachableTarget_ExpiresCleanly_WithoutNaN()
    {
        MunitionProfile munition = Vacuum();
        munition.MaxFlightSeconds = 5f;

        // Target running away far faster than the round can ever fly.
        var result = Engage(
            launchPos: new double3(0, 0, 0),
            launchVel: new double3(100, 0, 0),
            targetPos: new double3(5000, 0, 0),
            targetVel: new double3(9000, 0, 0),
            munition,
            timeout: 20.0);

        Assert.NotEqual(RoundState.Detonated, result.State);
        Assert.True(Vec.IsFinite(result.EndPosition));
    }

    /// <summary>
    /// The command must be a pure lateral pull: an airframe cannot add thrust along its own
    /// flight path, and letting the law do so would quietly turn it into a speed cheat.
    /// </summary>
    [Fact]
    public void GuidanceCommand_IsPerpendicularToFlightPath_AndWithinTheGLimit()
    {
        MunitionProfile munition = Vacuum();

        double3 missileVelocity = new(500, 0, 0);
        double3 r = new(2000, 800, 0);
        double3 v = new(-300, 220, 0);

        double3 command = Interceptor.GuidanceAccel(r, v, missileVelocity, NoGravity, munition);

        Assert.True(Math.Abs(Vec.Dot(command, Vec.Unit(missileVelocity))) < 1e-6,
            "command had an axial component");
        Assert.True(Vec.Len(command) <= munition.MaxLateralAccel + 1e-6,
            $"command {Vec.Len(command):F1} m/s^2 exceeded the {munition.MaxLateralAccel:F1} limit");
    }

    /// <summary>
    /// A target on a pure collision course produces no line-of-sight rotation, so a correct
    /// implementation should command essentially nothing. This is the classic PN sanity check.
    /// </summary>
    [Fact]
    public void GuidanceCommand_IsNearZero_OnACollisionCourse()
    {
        MunitionProfile munition = Vacuum();

        double3 missileVelocity = new(500, 0, 0);
        double3 r = new(2000, 0, 0);
        double3 v = new(-300, 0, 0); // closing straight down the line of sight

        double3 command = Interceptor.GuidanceAccel(r, v, missileVelocity, NoGravity, munition);

        Assert.True(Vec.Len(command) < 1e-9,
            $"expected no correction on a collision course, got {Vec.Len(command):E2} m/s^2");
    }
}
