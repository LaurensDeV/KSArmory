using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Flak: a shell that bursts at a set time of flight rather than only on proximity.
/// </summary>
public class TimedFuseTests
{
    private static MunitionProfile Shell(bool timed) => new()
    {
        Name = "TestShell",
        DisplayName = "test shell",
        LaunchSpeed = 1_000f,
        FuseRadius = 5f,
        FuseArmSeconds = 0f,
        MaxFlightSeconds = 30f,
        DragK = 0f,
        NeutralDensityRatio = 0f,
        ChargeKg = 0.16f,
        TimedFuse = timed,
    };

    private static Slug Fired(double3 velocity, double fuseSeconds)
        => new(Vec.Zero, velocity, null, -1, Vec.Zero, Vec.Zero)
        { Munition = Shell(timed: true), FuseSeconds = fuseSeconds };

    [Fact]
    public void ItBurstsAtTheTimeItWasSet()
    {
        Slug slug = Fired(new double3(1_000, 0, 0), 2.0);

        for (int i = 0; i < 40 && slug.State == RoundState.Flying; i++)
        {
            slug.Update(0.1, null, Vec.Zero, Vec.Zero, Vec.Zero, Shell(true));
        }

        Assert.Equal(RoundState.Detonated, slug.State);

        // Where it should be after exactly two seconds at 1 km/s, not at the end of whichever
        // sub-step happened to cross the fuse time.
        Assert.Equal(2_000.0, Vec.Len(slug.PositionEcl), 1.0);
    }

    [Fact]
    public void ZeroMeansNoTimedFuse()
    {
        // The default. Everything already flying must be unaffected by this field existing.
        Slug slug = Fired(new double3(1_000, 0, 0), 0.0);

        for (int i = 0; i < 50; i++) slug.Update(0.1, null, Vec.Zero, Vec.Zero, Vec.Zero, Shell(false));

        // Five seconds of flight and no burst. It expires on MaxFlightSeconds like any other
        // shell, which is a different test.
        Assert.Equal(RoundState.Flying, slug.State);
    }

    [Fact]
    public void ABurstWithNothingNearIsNotAHit()
    {
        // MissDistance decides lethality. Zero here would read as a direct hit on a target that
        // was never there.
        Slug slug = Fired(new double3(1_000, 0, 0), 1.0);

        while (slug.State == RoundState.Flying) slug.Update(0.1, null, Vec.Zero, Vec.Zero, Vec.Zero, Shell(true));

        Assert.Equal(RoundState.Detonated, slug.State);
        Assert.True(double.IsPositiveInfinity(slug.MissDistance), $"miss was {slug.MissDistance}");
    }

    [Fact]
    public void TheMissDistanceIsMeasuredWhereTheTargetIsAtTheBurst()
    {
        // Not where it was when the trigger was pulled. A crosser moves a long way in two seconds,
        // and measuring against the stale position would call every burst a hit.
        var start = new double3(2_000, 0, 0);
        var velocity = new double3(0, 300, 0);
        Slug slug = Fired(new double3(1_000, 0, 0), 2.0);

        // Resampled every frame, the way the battery feeds it. One fixed snapshot would park the
        // target where the shell was aimed and the proximity fuse would take it first.
        for (double t = 0.0; slug.State == RoundState.Flying; t += 0.1)
        {
            // Advanced to the END of the frame being handed over: the engine's sample is
            // end-of-frame and the round back-dates it. Passing the start instead reads as one
            // frame of target motion, which at 300 m/s is 30 m of error that looks like a bug in
            // the fuse. See docs/FRAMES-AND-EPOCHS.md.
            var target = new TargetState(start + velocity * (t + 0.1), velocity, 5.0);
            slug.Update(0.1, target, Vec.Zero, Vec.Zero, Vec.Zero, Shell(true));
        }

        Assert.Equal(RoundState.Detonated, slug.State);

        // The shell reaches x=2000 at t=2; the target has moved 600 m along y by then.
        Assert.Equal(600.0, slug.MissDistance, 20.0);
    }

    [Fact]
    public void ProximityStillWins()
    {
        // A shell fused for later that meets something on the way must not sail through it
        // waiting for the clock.
        var target = new TargetState(new double3(500, 0, 0), Vec.Zero, 5.0);
        Slug slug = Fired(new double3(1_000, 0, 0), 5.0);

        while (slug.State == RoundState.Flying)
        {
            slug.Update(0.05, target, Vec.Zero, Vec.Zero, Vec.Zero, Shell(true));
        }

        Assert.Equal(RoundState.Detonated, slug.State);
        Assert.True(slug.Age < 1.0, $"burst at {slug.Age:F2} s, so it flew past the target");
        Assert.True(slug.MissDistance <= 10.0, $"miss was {slug.MissDistance}");
    }

    [Fact]
    public void TheBurstIsBackDatedIntoTheFrameLikeAnyOther()
    {
        // The engine's world sample is end-of-frame, so a detonation mid-frame reports a negative
        // offset. Getting this wrong puts the fireball a frame of ecliptic motion away.
        Slug slug = Fired(new double3(1_000, 0, 0), 0.05);

        slug.Update(0.1, null, Vec.Zero, Vec.Zero, Vec.Zero, Shell(true));

        Assert.Equal(RoundState.Detonated, slug.State);
        Assert.True(slug.DetonationElapsedInFrame < 0.0,
                    $"expected a negative back-date, got {slug.DetonationElapsedInFrame}");
    }

    [Fact]
    public void TheFuseTimeComesFromTheSolveThatAimed()
    {
        // One number for both. A fuse set from a separately derived flight time would burst
        // somewhere the gun was not pointing.
        bool solved = BallisticLead.TrySolve(
            Vec.Zero, Vec.Zero,
            new double3(3_000, 0, 0), new double3(0, 200, 0),
            1_000.0, Vec.Zero, out double3 aim, out double flightTime);

        Assert.True(solved);
        Assert.True(flightTime > 0.0);

        // A round flying for exactly that long at muzzle speed reaches the point aimed at.
        Assert.Equal(Vec.Len(aim), flightTime * 1_000.0, 1.0);
    }

    [Fact]
    public void ATimedBurstSaysItWasTimed()
    {
        // The only way anyone can tell the setting did anything: a burst looks identical either
        // way, and at a kilometre it is not a judgement a player can make by eye.
        Slug slug = Fired(new double3(1_000, 0, 0), 1.0);

        while (slug.State == RoundState.Flying) slug.Update(0.1, null, Vec.Zero, Vec.Zero, Vec.Zero, Shell(true));

        Assert.True(slug.BurstOnTime);
    }

    [Fact]
    public void AProximityBurstDoesNot()
    {
        var target = new TargetState(new double3(500, 0, 0), Vec.Zero, 5.0);
        Slug slug = Fired(new double3(1_000, 0, 0), 5.0);

        while (slug.State == RoundState.Flying)
        {
            slug.Update(0.05, target, Vec.Zero, Vec.Zero, Vec.Zero, Shell(true));
        }

        Assert.False(slug.BurstOnTime);
    }
}
