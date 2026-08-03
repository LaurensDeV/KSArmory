using Brutal.Numerics;
using Xunit;

namespace AirDefence.Tests;

/// <summary>
/// The proximity fuse, and its independence from the seeker.
///
/// <para>From a flight log: <b>30 of 30</b> rounds that expired had lost lock, and 11 of them
/// had passed within 60 m of the target without detonating — the closest at 31 m, against a
/// 22 m fuse radius. Another round detonated at 38 m. The difference was whether the seeker
/// still held the target.</para>
///
/// <para>The cause is geometric and unavoidable: the line of sight to a target swings fastest
/// at the endgame, so it leaves a 55° seeker cone right when the round is closest — exactly
/// when the fuse matters. A real proximity fuse does not ask the seeker's permission, and
/// neither should this one.</para>
/// </summary>
public class FuseTests
{
    /// <summary>
    /// A round with guidance disabled, so the flypast geometry is exactly what the test sets.
    /// With navigation on, proportional navigation steers the round into the target whatever
    /// offset it is given - the first version of these tests did that and "proved" the fuse
    /// worked at 120 m, which was really just the seeker doing its job.
    /// </summary>
    private static MunitionProfile Munition(float navConstant = 0f) => new()
    {
        Name = "test",
        DisplayName = "test",
        NavConstant = navConstant,
        BoostSeconds = 0f,
        BoostAccel = 0f,
        DragK = 0f,
    };

    /// <summary>
    /// Flies a round straight past a stationary target offset laterally by
    /// <paramref name="missDistance"/>, and returns it once it stops flying.
    /// </summary>
    private static Interceptor FlyPast(double missDistance, double speed = 700.0)
    {
        MunitionProfile munition = Munition();

        // Local frame: no orbital motion, so the geometry is easy to reason about.
        double3 start = new(0, 0, 0);
        double3 heading = new(0, 0, 1);

        // 900 m downrange, offset sideways by the miss distance.
        double3 targetPos = new(missDistance, 0, 900);

        var round = new Interceptor(start, heading * speed, target: new object(), tube: 1,
                                    platformEcl: double3.Zero)
        {
            LaunchAnchorPartFrame = double3.Zero,
        };

        var target = new TargetState(targetPos, double3.Zero, Radius: 0.0);

        // Long enough to fly well past, in ordinary frames.
        for (int frame = 0; frame < 300 && round.State == RoundState.Flying; frame++)
        {
            round.Update(1.0 / 60.0, target, gravity: double3.Zero,
                         frameVelocityEcl: double3.Zero, platformEcl: double3.Zero,
                         munition: munition);
        }

        return round;
    }

    [Fact]
    public void ARoundPassingInsideTheFuseRadiusDetonatesEvenAfterTheSeekerLosesIt()
    {
        // 15 m miss against a 22 m fuse radius: unambiguously a kill. The seeker will have
        // dropped it well before this point, because closing on a laterally offset target
        // drives the line of sight towards 90° off the flight path.
        Interceptor round = FlyPast(missDistance: 15.0);

        Assert.Equal(RoundState.Detonated, round.State);
        Assert.True(round.MissDistance <= Munition().FuseRadius,
            $"detonated at {round.MissDistance:F1} m, outside the {Munition().FuseRadius} m fuse radius");
    }

    [Fact]
    public void ARoundPassingOutsideTheFuseRadiusDoesNotDetonate()
    {
        // The other half of the contract. Without this, "always detonate" would pass the test
        // above and the fuse would mean nothing.
        Interceptor round = FlyPast(missDistance: 120.0);

        Assert.NotEqual(RoundState.Detonated, round.State);
    }

    [Fact]
    public void ClosestApproachIsRecordedEvenWhenTheSeekerHasLostTheTarget()
    {
        // The log reports this number on every expiry, and it is how a near miss is told from a
        // clean miss. Tying it to the seeker made it stop updating at the moment it mattered.
        Interceptor round = FlyPast(missDistance: 120.0);

        Assert.True(round.ClosestApproach < 200.0,
            $"closest approach recorded as {round.ClosestApproach:F0} m for a 120 m flypast");
    }

    [Fact]
    public void ARoundWhoseSeekerDroppedTheTargetEarlyStillFuses()
    {
        // The case the head-on flypast above cannot show. There, the line of sight only leaves
        // the seeker cone in the last few metres, by which point the fuse has already fired, so
        // the fuse never actually depends on the lock.
        //
        // A crossing target is different: it starts wide of the flight path and the seeker
        // drops it immediately, long before the two converge. The round then coasts through the
        // target and, with the fuse living inside the lock check, sails on to expiry. That is a
        // hit scored as a miss - and every expired round in the flight log had lost lock.
        MunitionProfile munition = Munition();

        double3 roundVelocity = new(0, 0, 700);
        var round = new Interceptor(double3.Zero, roundVelocity, new object(), 1, double3.Zero);

        // 60 degrees off the flight path at the start, outside the 55 degree seeker cone, but
        // closing so that the two meet a second downrange.
        double3 targetStart = new(1200, 0, 700);
        double3 targetVelocity = new(-1200, 0, 0);

        double t = 0.0;
        const double dt = 1.0 / 60.0;
        for (int frame = 0; frame < 200 && round.State == RoundState.Flying; frame++)
        {
            var target = new TargetState(targetStart + targetVelocity * t, targetVelocity, Radius: 0.0);
            round.Update(dt, target, double3.Zero, double3.Zero, double3.Zero, munition);
            t += dt;
        }

        Assert.Equal(RoundState.Detonated, round.State);
    }

    [Fact]
    public void TravelStaysConsistentWithPositionThroughDetonation()
    {
        // Everything drawn comes from TravelSinceLaunch; the fuse and the blast work from
        // PositionEcl. They must describe the same round, on every frame including the last.
        //
        // In a stationary frame the two are the same quantity, so the invariant is exact:
        // accumulated travel equals actual displacement. That makes this the one place the
        // detonation path can be checked without knowing tCa.
        //
        // It was not true. The detonation branch advanced PositionEcl to the closest-approach
        // point and returned before the travel was accumulated, leaving the round drawn short of
        // where it went off by localVelocity * tCa. tCa is bounded by the sub-step, which is
        // dt / ceil(dt / SubStep) and therefore changes with the frame time - so the error moved
        // whenever the simulation speed changed, which is exactly how it was reported.
        MunitionProfile munition = Munition();

        double3 start = new(0, 0, 0);
        var round = new Interceptor(start, new double3(0, 0, 700), new object(), 1, double3.Zero);
        var target = new TargetState(new double3(10, 0, 900), double3.Zero, Radius: 0.0);

        for (int frame = 0; frame < 300 && round.State == RoundState.Flying; frame++)
        {
            round.Update(1.0 / 60.0, target, gravity: double3.Zero,
                         frameVelocityEcl: double3.Zero, platformEcl: double3.Zero,
                         munition: munition);
        }

        Assert.Equal(RoundState.Detonated, round.State);

        double drift = Vec.Len(round.TravelSinceLaunch - (round.PositionEcl - start));
        Assert.True(drift < 0.001,
            $"drawn travel and actual position disagree by {drift:F2} m at detonation");
    }

    [Fact]
    public void TheFuseDoesNotFireBeforeItIsArmed()
    {
        // A round still in its tube must not detonate on the launcher.
        MunitionProfile munition = Munition();
        var round = new Interceptor(double3.Zero, new double3(0, 0, 60), new object(), 1, double3.Zero);

        // Target sitting essentially on top of the round, well inside the fuse radius.
        var target = new TargetState(new double3(0, 0, 5), double3.Zero, Radius: 0.0);

        round.Update(munition.FuseArmSeconds * 0.5, target, double3.Zero, double3.Zero,
                     double3.Zero, munition);

        Assert.Equal(RoundState.Flying, round.State);
    }
}
