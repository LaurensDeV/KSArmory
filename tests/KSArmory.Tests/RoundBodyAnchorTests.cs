using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Guards the reference point a round's *body* is placed against.
///
/// <para>A round records <c>OffsetFromPlatform</c> relative to the platform's analytic orbit
/// position. A subpart is placed relative to the vehicle's physics origin. Those two differ —
/// that is the whole reason <see cref="DrawAnchor"/> exists — so positioning a body from the
/// absolute offset puts a round several metres from its tube, inside the search radar.</para>
///
/// <para>A body is therefore anchored to the tube it left, plus only the travel *since* launch:
/// a difference between two positions in the same frame, which carries none of that discrepancy.
/// These tests hold that property.</para>
/// </summary>
public class RoundBodyAnchorTests
{
    private static MunitionProfile Vacuum() =>
        new() { Name = "test", DisplayName = "test", DragK = 0f, BoostSeconds = 0f, GravityCompensation = 0f };

    private static Interceptor Launch(double3 platformEcl, double3 launchDirection)
    {
        return new Interceptor(
            positionEcl: platformEcl + new double3(0, 0, 5),
            velocityEcl: launchDirection * 60.0,
            target: new object(),
            tube: 1,
            platformEcl: platformEcl,
            frameVelocityEcl: default) { Munition = BuiltIns.Missile57E6 };
    }

    [Fact]
    public void TravelSinceLaunch_StartsAtZero()
    {
        var round = Launch(new double3(1.5e11, 0, 0), new double3(0, 0, 1));
        Assert.Equal(0.0, Vec.Len(round.TravelSinceLaunch), 12);
    }

    [Fact]
    public void TravelSinceLaunch_DoesNotDependOnWhereThePlatformIs()
    {
        // Two identical engagements whose platforms sit at wildly different absolute positions -
        // which is what "analytic orbit position" versus "physics origin" amounts to. The
        // *travel* must come out identical; the raw offsets must not.
        var munition = Vacuum();
        double3 near = new(0, 0, 0);
        double3 far = new(1.496e11, -2.7e10, 3.3e9);

        var a = Launch(near, new double3(0, 0, 1));
        var b = Launch(far, new double3(0, 0, 1));

        for (int step = 0; step < 60; step++)
        {
            a.Update(1.0 / 60.0, null, Vec.Zero, Vec.Zero, near, munition);
            b.Update(1.0 / 60.0, null, Vec.Zero, Vec.Zero, far, munition);
        }

        Assert.True(Vec.Len(a.TravelSinceLaunch) > 50.0, "the round should have gone somewhere");
        Assert.Equal(0.0, Vec.Len(a.TravelSinceLaunch - b.TravelSinceLaunch), 6);
    }

    [Fact]
    public void TravelSinceLaunch_ExcludesTheLaunchStandoff()
    {
        // The round starts 5 m off the platform. Anchoring a body to its tube and adding travel
        // must not add that 5 m again - doing so is the same class of double-count that put the
        // bodies inside the radar.
        var round = Launch(Vec.Zero, new double3(0, 0, 1));

        Assert.Equal(5.0, Vec.Len(round.LaunchOffset), 9);
        Assert.Equal(0.0, Vec.Len(round.TravelSinceLaunch), 12);
    }

    [Fact]
    public void VelocityLocal_ExcludesTheFrameVelocity()
    {
        // Orienting a body off VelocityEcl points every round along the platform's ~29.8 km/s
        // of ecliptic motion, i.e. all of them the same way regardless of where they are going.
        var munition = Vacuum();
        double3 frame = new(0, 29_800, 0);
        double3 platform = new(1.496e11, 0, 0);

        var round = new Interceptor(
            positionEcl: platform,
            velocityEcl: frame + new double3(0, 0, 60),      // straight up, through the frame
            target: new object(),
            tube: 1,
            platformEcl: platform,
            frameVelocityEcl: frame) { Munition = BuiltIns.Missile57E6 };

        // Before the first update, which is an instant the round is genuinely drawn at: Fire runs
        // after the round update, so SyncRoundBodies reaches a round that has never been
        // integrated. Unseeded, VelocityLocal degenerates to VelocityEcl and the body points along
        // the planet's orbit.
        Assert.True(Vec.AngleBetween(new double3(0, 0, 1), round.VelocityLocal) < 0.05,
            "a freshly launched round does not know its frame yet - its body will be drawn sideways");

        round.Update(1.0 / 60.0, null, Vec.Zero, frame, platform, munition);

        // The airspeed vector is what the body points along, and it is nearly straight up.
        Assert.True(Vec.AngleBetween(new double3(0, 0, 1), round.VelocityLocal) < 0.05);

        // The absolute velocity is dominated by the frame and points nowhere near it.
        Assert.True(Vec.AngleBetween(new double3(0, 0, 1), round.VelocityEcl) > 1.5);
    }

    [Fact]
    public void RoundsFromDifferentTubesKeepTheirOwnIdentity()
    {
        // Bodies are matched to rounds by tube number, so a round must carry the tube it left.
        var first = new Interceptor(Vec.Zero, new double3(0, 0, 60), new object(), 1, Vec.Zero, Vec.Zero)
        { Munition = BuiltIns.Missile57E6,
            LaunchAnchorPartFrame = new double3(5.5, 0.1, 1.3),
        };
        var second = new Interceptor(Vec.Zero, new double3(0, 0, 60), new object(), 7, Vec.Zero, Vec.Zero)
        { Munition = BuiltIns.Missile57E6,
            LaunchAnchorPartFrame = new double3(5.3, 0.2, -1.1),
        };

        Assert.Equal(1, first.Tube);
        Assert.Equal(7, second.Tube);
        Assert.NotEqual(first.LaunchAnchorPartFrame, second.LaunchAnchorPartFrame);
    }
}
