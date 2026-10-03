using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A guided missile stops on the ground: one fired downhill used to fly through the hill still
/// steering, because only an unguided round ever asked where the ground was.
/// </summary>
public class MissileGroundTests
{
    private const double PlanetRadius = 6_000_000.0;

    private sealed class Ball : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = PlanetRadius;
            return true;
        }
    }

    private static MunitionProfile Missile() => new()
    {
        Name = "missile",
        DisplayName = "missile",
        DragK = 0f,
        Guidance = GuidanceMode.Seeker,
        SeparationSeconds = 0.2f,
        FuseRadius = 16f,
        FuseArmSeconds = 0.5f,
        MaxFlightSeconds = 30f,
    };

    private static Interceptor Fly(double startAltitude, double3 velocity, IGroundTest? ground)
    {
        MunitionProfile munition = Missile();
        var round = new Interceptor(new double3(0, 0, PlanetRadius + startAltitude), velocity, new object(),
                                    tube: 0, platformEcl: default, frameVelocityEcl: default)
        {
            Munition = munition,
            Ground = ground,
        };

        for (int i = 0; i < 600 && round.State == RoundState.Flying; i++)
        {
            round.Update(1.0 / 60.0, null, Vec.Zero, frameVelocityEcl: default, platformEcl: default, munition);
        }

        return round;
    }

    [Fact]
    public void AMissileDivingIntoTheGroundGoesOffOnIt()
    {
        Interceptor round = Fly(300.0, new double3(250, 0, -150), new Ball());

        Assert.Equal(RoundState.Detonated, round.State);
        Assert.True(round.HitGround);
        Assert.Equal(PlanetRadius, Vec.Len(round.PositionEcl), 1.0);

        // Not a fuse range: zero would read as a direct hit on whatever it was aimed at.
        Assert.True(double.IsPositiveInfinity(round.MissDistance));
    }

    [Fact]
    public void AMissileLeavingATubeUnderTheHeightFieldDoesNotBurstOnTheRail()
    {
        Interceptor round = Fly(-0.5, new double3(250, 0, 100), new Ball());

        Assert.False(round.HitGround);
    }

    [Fact]
    public void WithNoGroundSuppliedItFliesThrough()
    {
        Interceptor round = Fly(300.0, new double3(250, 0, -150), ground: null);

        Assert.False(round.HitGround);
        Assert.True(Vec.Len(round.PositionEcl) < PlanetRadius);
    }
}
