using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>Finding what the pointer is over.</summary>
public class PickingTests
{
    private const double R = 6_000_000.0;
    private static readonly double3 Centre = new(0, 0, 0);

    /// <summary>The near face, not the far one: you point at the side of a planet you can see.</summary>
    [Fact]
    public void ARayAtASphereHitsTheNearSide()
    {
        double3 eye = new(R * 3, 0, 0);

        Assert.True(Picking.TryHitSphere(eye, new double3(-1, 0, 0), Centre, R, out double3 hit));
        Assert.Equal(R, hit.X, 3);
    }

    [Fact]
    public void ARayPastTheSphereMisses()
    {
        double3 eye = new(R * 3, 0, 0);

        Assert.False(Picking.TryHitSphere(eye, new double3(0, 1, 0), Centre, R, out _));
    }

    /// <summary>Pointing away from the planet is a miss, not a hit behind you.</summary>
    [Fact]
    public void ASphereBehindTheRayIsNotHit()
    {
        double3 eye = new(R * 3, 0, 0);

        Assert.False(Picking.TryHitSphere(eye, new double3(1, 0, 0), Centre, R, out _));
    }

    /// <summary>Inside it — a camera below the mean radius — the exit is the only hit ahead.</summary>
    [Fact]
    public void FromInsideTheExitIsTheHit()
    {
        Assert.True(Picking.TryHitSphere(Centre, new double3(1, 0, 0), Centre, R, out double3 hit));
        Assert.Equal(R, hit.X, 3);
    }

    [Fact]
    public void DegenerateInputMissesRatherThanGuessing()
    {
        double3 eye = new(R * 3, 0, 0);

        Assert.False(Picking.TryHitSphere(eye, default, Centre, R, out _));
        Assert.False(Picking.TryHitSphere(eye, new double3(-1, 0, 0), Centre, 0.0, out _));
        Assert.False(Picking.TryHitSphere(eye, new double3(-1, 0, 0), Centre, double.NaN, out _));
    }

    /// <summary>
    /// Nearest, not first. Two craft close together would otherwise be picked by list order,
    /// which is the order they were built in and means nothing to whoever is pointing at one.
    /// </summary>
    [Fact]
    public void TheNearestScreenPositionWins()
    {
        List<float2> positions = [new(100, 100), new(105, 100), new(400, 400)];

        Assert.Equal(1, Picking.NearestOnScreen(positions, new float2(106, 100), 40f));
        Assert.Equal(0, Picking.NearestOnScreen(positions, new float2(96, 100), 40f));
        Assert.Equal(2, Picking.NearestOnScreen(positions, new float2(402, 402), 40f));
    }

    [Fact]
    public void NothingWithinReachIsNoPick()
    {
        List<float2> positions = [new(100, 100)];

        Assert.Equal(-1, Picking.NearestOnScreen(positions, new float2(400, 400), 40f));
        Assert.Equal(-1, Picking.NearestOnScreen([], new float2(0, 0), 40f));
    }

    /// <summary>
    /// Each item reaches as far as it looks. A flat radius makes a big vessel exactly as hard to
    /// hit as a drone — the pointer has to find its centre either way, so most of what is plainly
    /// under the cursor is not clickable.
    /// </summary>
    [Fact]
    public void EachItemIsPickedWithinItsOwnReach()
    {
        List<float2> positions = [new(100, 100), new(400, 400)];
        List<float> radii = [80f, 10f];

        // 60 px from the big one is inside it; the same distance from the small one is not.
        Assert.Equal(0, Picking.NearestWithin(positions, radii, new float2(160, 100)));
        Assert.Equal(-1, Picking.NearestWithin(positions, radii, new float2(460, 400)));
        Assert.Equal(1, Picking.NearestWithin(positions, radii, new float2(405, 400)));
    }

    /// <summary>
    /// Overlapping reaches go to whichever centre is nearer, not to whichever is listed first —
    /// list order is build order and means nothing to whoever is pointing.
    /// </summary>
    [Fact]
    public void OverlappingReachesGoToTheNearerCentre()
    {
        List<float2> positions = [new(100, 100), new(140, 100)];
        List<float> radii = [100f, 100f];

        Assert.Equal(1, Picking.NearestWithin(positions, radii, new float2(130, 100)));
        Assert.Equal(0, Picking.NearestWithin(positions, radii, new float2(110, 100)));
    }

    [Fact]
    public void MismatchedOrEmptyListsPickNothing()
    {
        Assert.Equal(-1, Picking.NearestWithin([], [], new float2(0, 0)));
        Assert.Equal(-1, Picking.NearestWithin([new float2(0, 0)], [], new float2(0, 0)));
    }
}
