using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// One test per way an ecliptic value can be used as if it were local.
///
/// Near Earth, Ecl position sweeps past at ~29.8 km/s and Ecl velocity is dominated by that same
/// solar orbit, so the errors are hundreds of metres to tens of kilometres — big enough that each
/// one looks like a completely different fault. They are cheap to assert and expensive to
/// rediscover.
/// </summary>
public class FrameRegressionTests
{
    private static readonly object TargetHandle = new();
    private static readonly double3 NoGravity = new(0, 0, 0);

    /// <summary>Roughly Earth's orbital velocity — the magnitude every one of these multiplies.</summary>
    private static readonly double3 SolarFrame = new(29800, 0, 0);

    private static MunitionProfile Vacuum() =>
        new() { Name = "test", DisplayName = "test", DragK = 0f };

    private static Interceptor Round(double3 velocity, double3 platformEcl = default,
                                     double3 frameVelocityEcl = default) =>
        new(new double3(0, 0, 0), velocity, TargetHandle, tube: 1, platformEcl, frameVelocityEcl);

    /// <summary>
    /// An absolute Ecl position differenced against an anchor captured a frame earlier draws a
    /// round ~500 m from where it is, with its trail smeared across kilometres. The offset the
    /// renderer uses must not depend on frame velocity.
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
        double3 platform = new(0, 0, 0);

        for (int i = 0; i < 10; i++)
        {
            // Advanced BEFORE the update that uses it: the platform sample arriving at update k has
            // already moved by v * dt(k) - the step that same update is given, not the previous
            // one. Advancing it afterwards encodes the opposite phase, which a constant step cannot
            // tell apart from this one. See OffsetPhaseTests for the measurement.
            platform += SolarFrame * dt;

            still.Update(dt, target, NoGravity, frameVelocityEcl: default, platformEcl: default, munition);
            carried.Update(dt, carriedTarget, NoGravity, SolarFrame, platform, munition);
        }

        double drift = Vec.Len(still.OffsetFromPlatform - carried.OffsetFromPlatform);
        Assert.True(drift < 1.0,
            $"platform-relative offset moved {drift:F1} m when the frame was carried at 29.8 km/s");
    }

    /// <summary>
    /// The trail, for the same reason: 32 points over 1.6 s of absolute positions spread over
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
    /// A blast that compares the round's end-of-frame position against world positions sampled at
    /// the frame start kills nothing at 22 m. The elapsed value that lets the caller reconcile the
    /// two must be a real offset inside the step.
    /// </summary>
    [Fact]
    public void DetonationElapsed_LiesWithinTheUpdateStep()
    {
        MunitionProfile munition = Vacuum();
        const double dt = 1.0 / 30.0;

        var round = Round(new double3(500, 0, 0) + SolarFrame);

        // Target close enough ahead that the fuse trips during this step — expressed the way KSA
        // actually hands it over.
        //
        // Vehicle Ecl state is refreshed once per frame at the top of OnFrame, to the state at the
        // END of the step the round is about to be integrated across, so a sample means "where the
        // target will be at the end of this step", not "where it is now". Interceptor back-dates
        // it by the step to line it up with the round's own epoch.
        //
        // The + SolarFrame * dt is the convention, not padding to make a test pass. Writing the
        // sample as a start-of-step position leaves every line of sight carrying a whole frame of
        // the planet's 29.8 km/s, and re-creates that inside the test.
        var target = new TargetState(new double3(20, 0, 0) + SolarFrame * dt, SolarFrame, 1.0);
        munition.FuseArmSeconds = 0f;

        round.Update(dt, target, NoGravity, SolarFrame, platformEcl: default, munition);

        Assert.Equal(RoundState.Detonated, round.State);

        // Negative, and that is the point: the value is measured against the world sample, which
        // is the END of the step - Universe refreshes vehicle Ecl state at the top of OnFrame to
        // GetLastSimStep().NextTime. A burst can only happen at or before that instant, so the
        // caller advances the world BACKWARD by this much.
        //
        // What must hold is that it names an instant inside the step just integrated.
        Assert.InRange(round.DetonationElapsedInFrame, -dt, 0.0);
    }

    /// <summary>
    /// A seeker that compares line-of-sight against absolute Ecl velocity, which near Earth points
    /// along the planet's orbit, breaks lock on the first step of every shot.
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
    /// Drag applied to absolute Ecl speed has a round "flying" at 29.8 km/s see Mach 87 of it and
    /// scrubs it to ~1.1 km/s in 22 s. Airspeed must be local.
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
    /// Telemetry must describe the round, not the planet: measured absolutely, distance flown
    /// comes out at 650 km for a 22 s flight, which is Earth moving around the Sun.
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
