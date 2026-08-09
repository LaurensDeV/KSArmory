using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A bomb has nothing to arrive at but the ground. Every other round in the arsenal is aimed
/// upwards and passes through terrain, which is cheap and invisible; this is the one that cannot.
/// </summary>
public class GroundImpactTests
{
    private const double Dt = 1.0 / 60.0;
    private const double PlanetRadius = 6_371_000.0;

    private static readonly double3 Centre = new(0, 0, 0);

    /// <summary>A spherical planet, which is what one frame of ground track looks like anyway.</summary>
    private sealed class Ball(double radius) : IGroundTest
    {
        public int Samples;

        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            Samples++;
            centreEcl = Centre;
            surfaceRadius = radius;
            return true;
        }
    }

    private static MunitionProfile Bomb(bool hitsTerrain = true) => new()
    {
        Name = "TESTBOMB",
        DisplayName = "test bomb",
        Guidance = GuidanceMode.None,
        LaunchSpeed = 0f,
        BoostSeconds = 0f,
        BoostAccel = 0f,
        MaxFlightSeconds = 60f,
        DragK = 0f,
        FuseRadius = 0f,
        ChargeKg = 250f,
        HitsTerrain = hitsTerrain,
    };

    // Released from rest 500 m up, with gravity pointing at the planet's centre.
    private static Slug Dropped(IGroundTest? ground, MunitionProfile munition, double altitude = 500.0)
    {
        double3 start = new(PlanetRadius + altitude, 0, 0);

        return new Slug(start, Vec.Zero, null, 1, start, Vec.Zero)
        {
            Munition = munition,
            Ground = ground,
        };
    }

    private static void Fall(Slug bomb, int frames = 4000)
    {
        for (int i = 0; i < frames && bomb.State == RoundState.Flying; i++)
        {
            double3 gravity = Vec.Unit(Centre - bomb.PositionEcl) * 9.81;
            bomb.Update(Dt, null, gravity, Vec.Zero, Vec.Zero, bomb.Munition);
        }
    }

    /// <summary>
    /// The whole feature. Dropped from 500 m it falls, meets the ground and bursts there — not at
    /// MaxFlightSeconds, and not somewhere under the surface.
    /// </summary>
    [Fact]
    public void ABombDroppedFromRestBurstsOnTheGround()
    {
        Slug bomb = Dropped(new Ball(PlanetRadius), Bomb());

        Fall(bomb);

        Assert.Equal(RoundState.Detonated, bomb.State);
        Assert.True(bomb.HitGround);

        // On the surface, not through it. A sub-step at impact speed is ~0.5 m, so anything much
        // larger than that means the crossing was not backed up to.
        double altitude = Vec.Len(bomb.PositionEcl - Centre) - PlanetRadius;
        Assert.True(Math.Abs(altitude) < 0.5, $"burst at {altitude:F2} m, expected the surface");
    }

    /// <summary>
    /// Without the flag the terrain is not there at all, which is how every other round behaves and
    /// what keeps a 150-shell burst from paying for a terrain sample each. Without this the test
    /// above passes against a round that detonates on anything.
    /// </summary>
    [Fact]
    public void ARoundThatDoesNotHitTerrainFallsStraightThrough()
    {
        Ball ground = new(PlanetRadius);
        Slug shell = Dropped(ground, Bomb(hitsTerrain: false));

        Fall(shell);

        Assert.Equal(RoundState.Expired, shell.State);
        Assert.False(shell.HitGround);
        Assert.Equal(0, ground.Samples);
    }

    /// <summary>
    /// One terrain sample a frame, whatever the sub-step count. That is the reason the seam answers
    /// with a centre and a radius rather than an altitude — an altitude would have to be re-read
    /// per sub-step to mean anything, and the sample is the expensive half.
    /// </summary>
    [Fact]
    public void TheGroundIsSampledOncePerFrameNotPerSubStep()
    {
        Ball ground = new(PlanetRadius);
        Slug bomb = Dropped(ground, Bomb());

        int frames = 0;
        while (bomb.State == RoundState.Flying && frames < 4000)
        {
            frames++;
            double3 gravity = Vec.Unit(Centre - bomb.PositionEcl) * 9.81;
            bomb.Update(Dt, null, gravity, Vec.Zero, Vec.Zero, bomb.Munition);
        }

        Assert.True(Interceptor.SubStep < Dt, "a frame must span several sub-steps for this to bite");
        Assert.Equal(frames, ground.Samples);
    }

    /// <summary>
    /// The burst instant is inside the step it happened in, negative because the world sample is
    /// end-of-frame. Detonate back-dates the world by it to place the blast, so a value outside
    /// that range puts the explosion in the wrong place by a whole frame of the planet's motion.
    /// </summary>
    [Fact]
    public void TheBurstInstantSitsInsideTheStepThatCausedIt()
    {
        Slug bomb = Dropped(new Ball(PlanetRadius), Bomb());

        Fall(bomb);

        Assert.Equal(RoundState.Detonated, bomb.State);
        Assert.InRange(bomb.DetonationElapsedInFrame, -Dt, 0.0);
    }

    /// <summary>
    /// A ground test that will not answer leaves the round flying rather than bursting it in mid
    /// air. Same rule as the hull test: what cannot be established is not a hit.
    /// </summary>
    [Fact]
    public void NoAnswerIsNotAnImpact()
    {
        Slug bomb = Dropped(null, Bomb());

        Fall(bomb);

        Assert.Equal(RoundState.Expired, bomb.State);
        Assert.False(bomb.HitGround);
    }
}
