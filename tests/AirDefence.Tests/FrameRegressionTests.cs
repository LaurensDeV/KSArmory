using Brutal.Numerics;
using Xunit;

namespace AirDefence.Tests;

/// <summary>
/// One test per bug that actually shipped and had to be found in-game.
///
/// Every one of these came from the same mistake: an ecliptic value used as if it were local.
/// Near Earth, Ecl position sweeps past at ~29.8 km/s and Ecl velocity is dominated by that same
/// solar orbit, so the errors are hundreds of metres to tens of kilometres — big enough to look
/// like a completely different bug each time. They are cheap to assert and expensive to rediscover.
/// </summary>
public class FrameRegressionTests
{
    private static readonly object TargetHandle = new();
    private static readonly double3 NoGravity = new(0, 0, 0);

    /// <summary>Roughly Earth's orbital velocity — the magnitude that caused every one of these.</summary>
    private static readonly double3 SolarFrame = new(29800, 0, 0);

    private static MunitionProfile Vacuum() =>
        new() { Name = "test", DisplayName = "test", DragK = 0f };

    private static Interceptor Round(double3 velocity, double3 platformEcl = default) =>
        new(new double3(0, 0, 0), velocity, TargetHandle, tube: 1, platformEcl);

    /// <summary>
    /// Shipped bug: rounds were drawn ~500 m from where they were, with trails smeared across
    /// kilometres, because their absolute Ecl position was differenced against an anchor
    /// captured a frame earlier. The offset the renderer uses must not depend on frame velocity.
    /// </summary>
    [Fact]
    public void OffsetFromPlatform_IsUnaffectedByFrameVelocity()
    {
        MunitionProfile munition = Vacuum();
        const double dt = 1.0 / 60.0;

        var still = Round(new double3(100, 0, 0));
        var carried = Round(new double3(100, 0, 0) + SolarFrame);

        var target = new TargetState(new double3(5000, 0, 0), new double3(0, 0, 0), 5.0);
        var carriedTarget = new TargetState(new double3(5000, 0, 0), SolarFrame, 5.0);

        // The platform is re-read every update in the game, so it advances with the frame.
        // Holding it still here would be a fiction, and would make the invariant untestable.
        //
        // It advances *before* the update, not after: the mod's frame hook is a postfix, so KSA
        // has already stepped the world when it runs and GetPositionEcl returns the platform at
        // the end of that step. Advancing after was a fiction of its own - it handed over the
        // start-of-step position, which made an extrapolation inside Update look correct and
        // hid a frame of ecliptic motion leaking into every drawn round.
        double3 platform = new(0, 0, 0);

        for (int i = 0; i < 10; i++)
        {
            platform += SolarFrame * dt;
            still.Update(dt, target, NoGravity, frameVelocityEcl: default, platformEcl: default, munition);
            carried.Update(dt, carriedTarget, NoGravity, SolarFrame, platform, munition);
        }

        double drift = Vec.Len(still.OffsetFromPlatform - carried.OffsetFromPlatform);
        Assert.True(drift < 1.0,
            $"platform-relative offset moved {drift:F1} m when the frame was carried at 29.8 km/s");
    }

    /// <summary>
    /// Same bug, trail edition: 32 points over 1.6 s of absolute positions would be spread over
    /// ~48 km of the planet's motion. Consecutive trail points must stay a plausible flight
    /// distance apart.
    /// </summary>
    [Fact]
    public void TrailPoints_DoNotSmearWithFrameVelocity()
    {
        MunitionProfile munition = Vacuum();
        const double dt = 1.0 / 60.0;

        var round = Round(new double3(200, 0, 0) + SolarFrame);
        var target = new TargetState(new double3(20000, 0, 0), SolarFrame, 5.0);

        double3 platform = new(0, 0, 0);
        for (int i = 0; i < 120; i++)
        {
            round.Update(dt, target, NoGravity, SolarFrame, platform, munition);
            platform += SolarFrame * dt;
        }

        Assert.True(round.TrailOffsets.Count > 2, "expected a trail to have been recorded");

        for (int i = 1; i < round.TrailOffsets.Count; i++)
        {
            double gap = Vec.Len(round.TrailOffsets[i] - round.TrailOffsets[i - 1]);

            // Trail interval is 0.05 s; even a very fast round covers well under a kilometre.
            Assert.True(gap < 1000.0,
                $"trail points {i - 1}->{i} are {gap / 1000.0:F1} km apart - frame motion is leaking in");
        }
    }

    /// <summary>
    /// Shipped bug: detonations at 22 m killed nothing, because the blast compared the round's
    /// end-of-frame position against world positions sampled at the frame start. The elapsed
    /// value that lets the caller reconcile those must be a real offset inside the step.
    /// </summary>
    [Fact]
    public void DetonationElapsed_LiesWithinTheUpdateStep()
    {
        MunitionProfile munition = Vacuum();
        const double dt = 1.0 / 30.0;

        var round = Round(new double3(500, 0, 0) + SolarFrame);

        // Target close enough ahead that the fuse trips during this step.
        var target = new TargetState(new double3(20, 0, 0), SolarFrame, 1.0);
        munition.FuseArmSeconds = 0f;

        round.Update(dt, target, NoGravity, SolarFrame, platformEcl: default, munition);

        Assert.Equal(RoundState.Detonated, round.State);
        Assert.InRange(round.DetonationElapsedInFrame, 0.0, dt);
    }

    /// <summary>
    /// Shipped bug: the seeker compared line-of-sight against absolute Ecl velocity, which near
    /// Earth points along the planet's orbit. Lock broke on the very first step of every shot.
    /// </summary>
    [Fact]
    public void Seeker_KeepsLock_WhenTheFrameIsFastMoving()
    {
        MunitionProfile munition = Vacuum();

        // Round flying +X locally; target dead ahead. Trivially inside any sane seeker cone.
        var round = Round(new double3(400, 0, 0) + SolarFrame);
        var target = new TargetState(new double3(3000, 0, 0), SolarFrame, 5.0);

        round.Update(1.0 / 60.0, target, NoGravity, SolarFrame, platformEcl: default, munition);

        Assert.True(round.HasLock,
            "seeker broke lock immediately - it is measuring against absolute velocity again");
    }

    /// <summary>
    /// Shipped bug: drag was applied to absolute Ecl speed, so a round "flying" at 29.8 km/s saw
    /// Mach 87 of it and was scrubbed down to ~1.1 km/s in 22 s. Airspeed must be local.
    /// </summary>
    [Fact]
    public void Drag_ActsOnAirspeed_NotAbsoluteSpeed()
    {
        var munition = new MunitionProfile { Name = "test", DisplayName = "test", DragK = 4.0e-5f, BoostSeconds = 0f, BoostAccel = 0f };
        const double dt = 1.0 / 60.0;

        var round = Round(new double3(600, 0, 0) + SolarFrame);
        var target = new TargetState(new double3(50000, 0, 0), SolarFrame, 5.0);

        double3 platform = new(0, 0, 0);
        for (int i = 0; i < 60; i++)
        {
            round.Update(dt, target, NoGravity, SolarFrame, platform, munition);
            platform += SolarFrame * dt;
        }

        // One second of drag on a 600 m/s airspeed is a mild loss. Applied to 29.8 km/s it is
        // catastrophic, and the round would be down to a few hundred m/s.
        Assert.InRange(round.Speed, 550.0, 600.0);
    }

    /// <summary>
    /// Reported telemetry must describe the round, not the planet. Distance flown was once
    /// reporting 650 km for a 22 s flight, which is simply Earth moving around the Sun.
    /// </summary>
    [Fact]
    public void DistanceFlown_MeasuresLocalMotion()
    {
        MunitionProfile munition = Vacuum();
        munition.BoostSeconds = 0f;
        munition.BoostAccel = 0f;

        const double dt = 1.0 / 60.0;
        var round = Round(new double3(300, 0, 0) + SolarFrame);
        var target = new TargetState(new double3(50000, 0, 0), SolarFrame, 5.0);

        double3 platform = new(0, 0, 0);
        for (int i = 0; i < 60; i++)
        {
            round.Update(dt, target, NoGravity, SolarFrame, platform, munition);
            platform += SolarFrame * dt;
        }

        // One second at 300 m/s local.
        Assert.InRange(round.DistanceFlown, 280.0, 320.0);
    }

    /// <summary>
    /// Guidance must never emit a non-finite command, whatever it is handed. A NaN here
    /// propagates into the position and the round vanishes to an unrenderable coordinate.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 0)]          // co-located with the target
    [InlineData(1e-9, 0, 0)]       // essentially co-located
    [InlineData(1e7, 1e7, 1e7)]    // absurdly distant
    public void Guidance_NeverProducesNonFiniteCommands(double rx, double ry, double rz)
    {
        MunitionProfile munition = Vacuum();

        double3 command = Interceptor.GuidanceAccel(
            new double3(rx, ry, rz),
            new double3(-300, 40, 0),
            new double3(500, 0, 0),
            NoGravity,
            munition);

        Assert.True(Vec.IsFinite(command), $"guidance returned {command} for r=({rx},{ry},{rz})");
        Assert.True(Vec.Len(command) <= munition.MaxLateralAccel + 1e-6);
    }

    /// <summary>A round with no target must coast and expire cleanly, never NaN.</summary>
    [Fact]
    public void LostTarget_InAFastFrame_StaysFinite()
    {
        MunitionProfile munition = Vacuum();
        munition.MaxFlightSeconds = 2f;

        var round = Round(new double3(300, 0, 0) + SolarFrame);

        while (round.State == RoundState.Flying)
        {
            round.Update(1.0 / 60.0, target: null, NoGravity, SolarFrame, platformEcl: default, munition);
        }

        Assert.Equal(RoundState.Expired, round.State);
        Assert.True(Vec.IsFinite(round.PositionEcl));
        Assert.True(Vec.IsFinite(round.OffsetFromPlatform));
    }
}
