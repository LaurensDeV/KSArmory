using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class SpotSearchTests
{
    private static readonly object Jet = new();
    private static readonly object Mast = new();
    private static readonly object Drone = new();
    private static readonly double3 Eye = new(0, 0, 0);

    private static LaserSpot Spot(object by, int code, double x, double vx = 0.0)
        => new(by, code, new double3(x, 0, 0), new double3(vx, 0, 0), 0.0);

    private static bool Choose(IReadOnlyList<LaserSpot> spots, out double3 at, double dt = 0.0,
                               Func<double3, bool>? canSee = null)
        => SpotSearch.TryChoose(spots, 1688, Jet, Eye, 20_000.0, dt, canSee ?? (_ => true), out at);

    [Fact]
    public void ItLocksOntoTheNearestSpotCarryingItsCode()
    {
        Assert.True(Choose([Spot(Mast, 1688, 9_000), Spot(Drone, 1688, 4_000)], out double3 at));
        Assert.Equal(4_000.0, at.X);
    }

    [Fact]
    public void ASpotOnAnotherCodeIsNotItsTarget()
    {
        Assert.False(Choose([Spot(Mast, 1511, 4_000)], out _));
    }

    [Fact]
    public void ItDoesNotChaseTheSpotItsOwnCraftIsMaking()
    {
        Assert.False(Choose([Spot(Jet, 1688, 4_000)], out _));
    }

    [Fact]
    public void ASpotBeyondTheSetsRangeOrOutOfViewIsNotFound()
    {
        Assert.False(Choose([Spot(Mast, 1688, 25_000)], out _));
        Assert.False(Choose([Spot(Mast, 1688, 4_000)], out _, canSee: _ => false));
    }

    [Fact]
    public void TheNearestTheHeadCanSeeWinsOverANearerOneBehindAHill()
    {
        Assert.True(Choose([Spot(Mast, 1688, 4_000), Spot(Drone, 1688, 9_000)], out double3 at,
                           canSee: p => p.X > 5_000));
        Assert.Equal(9_000.0, at.X);
    }

    [Fact]
    public void ASpotIsCarriedOverTheStepByTheVelocityOfWhatItLiesOn()
    {
        // Ground moving with the planet at 29.8 km/s, stated a 20 ms step ago: held still it would
        // be 596 m behind where it is.
        Assert.True(Choose([Spot(Mast, 1688, 4_000, vx: 29_800)], out double3 at, dt: 0.02));
        Assert.Equal(4_596.0, at.X, 6);
    }
}
