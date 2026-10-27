using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The motor pushes along the velocity gained since release, not the velocity the round has: a
/// rail-launched round inherits its craft's whole velocity, which points wherever the craft was going.
/// </summary>
public class BoostAxisTests
{
    private static MunitionProfile Boosting => Catalogue.MunitionNamed("AGM88");

    private const double Step = 0.05;

    // Where a round has got to after a stated number of steps, measured against what it inherited.
    // Counted in steps rather than accumulated seconds: `t += 0.05` runs one iteration too many as
    // often as not, and that extra step is a whole step of the platform's velocity in the answer.
    private static double3 TravelAfter(double3 platformVelocity, double3 rail, int steps)
    {
        MunitionProfile munition = Boosting;
        double3 frame = Vec.Zero;
        double3 launchVelocity = platformVelocity + (Vec.Unit(rail) * munition.LaunchSpeed);

        var round = new Interceptor(Vec.Zero, launchVelocity, null, 1, Vec.Zero, frame)
        {
            Munition = munition,
            ReleaseHeadingEcl = Vec.Unit(rail),
            LaunchFrameVelocityLocal = platformVelocity - frame,
        };

        // No target, no gravity, no air: the motor is the only thing acting, so what is left is
        // the direction it pushed.
        for (int i = 0; i < steps; i++)
        {
            round.Update(Step, null, Vec.Zero, frame, Vec.Zero, munition, 0.0);
        }

        return round.TravelSinceLaunch - (platformVelocity * (steps * Step));
    }

    /// <summary>
    /// A launcher that has turned round still travels the way it came, so thrust along the total
    /// velocity drives the round away from what the rail points at.
    /// </summary>
    [Fact]
    public void AMotorPushesAlongTheRailAndNotAlongTheCraftsTrack()
    {
        var track = new double3(500, 0, 0);
        var rail = new double3(-1, 0, 0);          // turned round: the rail faces back down the track

        double3 travel = TravelAfter(track, rail, steps: 60);

        Assert.True(Vec.Dot(Vec.Unit(travel), Vec.Unit(rail)) > 0.99,
                    $"boosted {Vec.AngleBetween(travel, rail) * 180.0 / Math.PI:F0} deg off the rail");
    }

    /// <summary>
    /// And the case that reads as the rounds diving: a craft descending after a turn drags the
    /// boost down with it when the motor follows the total velocity.
    /// </summary>
    [Fact]
    public void ADescendingCraftDoesNotAimTheMotorAtTheGround()
    {
        var descending = new double3(300, 0, -200);
        var rail = new double3(-1, 0, 0);

        double3 travel = TravelAfter(descending, rail, steps: 60);

        Assert.True(travel.Z > -1.0, $"the boost drove {-travel.Z:F0} m downwards off a level rail");
    }

    /// <summary>
    /// A launcher standing still has gained exactly what it has, so the two axes agree for every
    /// ground system.
    /// </summary>
    [Fact]
    public void AStationaryLauncherIsUnchanged()
    {
        var rail = new double3(0, 0, 1);

        double3 travel = TravelAfter(Vec.Zero, rail, steps: 60);

        Assert.True(Vec.Dot(Vec.Unit(travel), Vec.Unit(rail)) > 0.999);
    }

    /// <summary>
    /// Both terms are the frame's own samples, so the ecliptic's ~29.8 km/s cancels in the
    /// subtraction and must not move the round relative to its rail.
    /// </summary>
    [Fact]
    public void SharedMotionDoesNotReachTheAxis()
    {
        var rail = new double3(-1, 0, 0);
        var platform = new double3(500, 0, 0);

        const int steps = 40;

        double3 alone = TravelAfter(platform, rail, steps);

        MunitionProfile munition = Boosting;
        var carried = new double3(0, 29_800, 0);
        double3 launchVelocity = platform + carried + (Vec.Unit(rail) * munition.LaunchSpeed);

        var round = new Interceptor(Vec.Zero, launchVelocity, null, 1, Vec.Zero, carried)
        {
            Munition = munition,
            ReleaseHeadingEcl = Vec.Unit(rail),
            LaunchFrameVelocityLocal = platform,
        };

        for (int i = 0; i < steps; i++)
        {
            round.Update(Step, null, Vec.Zero, carried, Vec.Zero, munition, 0.0);
        }

        double3 moved = round.TravelSinceLaunch - ((platform + carried) * (steps * Step));

        Assert.True(Vec.Len(moved - alone) < 1.0,
                    $"the shared {Vec.Len(carried):F0} m/s moved the boost by {Vec.Len(moved - alone):F1} m");
    }
}
