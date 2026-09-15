using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Where a burst is drawn from: the round's offset from the platform at the instant it went off.
///
/// <para>A round bursts part-way through a frame, and its offset is still refreshed after the step
/// against the frame-end platform sample. On that one frame the offset carries the platform's motion
/// across the rest of the frame, and a burst drawn from it jumps off the shell along the ecliptic.</para>
/// </summary>
public class BurstOffsetTests
{
    private static readonly double3 PlatformStart = new(1.4959e11, 0, 0);
    private static readonly double3 OrbitalVelocity = new(0, 29_800, 0);

    private static MunitionProfile TimedShell() => new()
    {
        Name = "BurstOffsetShell",
        DisplayName = "burst offset shell",
        LaunchSpeed = 800f,
        DragK = 0f,
        NeutralDensityRatio = 0f,
        MaxFlightSeconds = 30f,
        FuseRadius = 0f,
        FuseArmSeconds = 0f,
        TimedFuse = true,
        ChargeKg = 3.3f,
    };

    [Theory]
    [InlineData(0.1, 0.95)]     // bursts half-way through a frame
    [InlineData(1.0 / 60.0, 2.004)]
    [InlineData(0.05, 1.21)]
    public void TheOffsetAtTheBurstIsTheRoundAgainstThePlatformAtThatInstant(double dt, double fuseSeconds)
    {
        MunitionProfile shell = TimedShell();
        double3 platform = PlatformStart;
        double3 up = new(1, 0, 0);

        var slug = new Slug(platform + up, OrbitalVelocity + (up * shell.LaunchSpeed), null, -1, platform, OrbitalVelocity)
        {
            Munition = shell,
            FuseSeconds = fuseSeconds,
        };

        while (slug.State == RoundState.Flying)
        {
            // The engine's phase: the sample has already advanced by the step this update is given.
            platform += OrbitalVelocity * dt;
            slug.Update(dt, null, Vec.Zero, OrbitalVelocity, platform, shell, 0.0);
        }

        double elapsed = slug.DetonationElapsedInFrame;
        double3 platformAtBurst = platform + (OrbitalVelocity * elapsed);
        double3 truth = slug.PositionEcl - platformAtBurst;

        Assert.Equal(RoundState.Detonated, slug.State);
        Assert.True(elapsed < -1e-4, $"burst {elapsed * 1000.0:F2} ms into the frame; this needs one mid-frame");

        // The round's own offset is off by the platform's travel over the rest of the frame.
        Assert.True(Vec.Len(slug.OffsetFromPlatform - truth) > 100.0,
                    $"the round's offset is only {Vec.Len(slug.OffsetFromPlatform - truth):F1} m out");

        double3 atBurst = DrawAnchor.OffsetAtBurst(slug.OffsetFromPlatform, OrbitalVelocity, elapsed);
        Assert.True(Vec.Len(atBurst - truth) < 0.01, $"drawn {Vec.Len(atBurst - truth):F3} m from the burst");
    }
}
