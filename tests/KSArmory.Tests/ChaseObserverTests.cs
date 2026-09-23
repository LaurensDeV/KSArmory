using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

public class ChaseObserverTests(ITestOutputHelper output)
{
    private static readonly double3 Up = new(0.0, 0.0, 1.0);

    [Fact]
    public void OnlyACloudOverAirIsHandedToAnObserver()
    {
        Assert.True(ChaseView.HandsToAnObserver(0.3e6, hasAir: true));
        Assert.False(ChaseView.HandsToAnObserver(0.3e6, hasAir: false));
        Assert.False(ChaseView.HandsToAnObserver(MushroomCloud.ThresholdKg - 1.0, hasAir: true));
    }

    /// <summary>
    /// Back along the chase's line of sight, so the cut is the same view from further off: a chase
    /// looking east and down at the round is watched from west of the landing.
    /// </summary>
    [Fact]
    public void TheObserverStandsBackAlongTheChasesBearing()
    {
        double3 lookingEastAndDown = new(1.0, 0.2, -0.8);
        double3 offset = ChaseView.ObserverOffset(Up, lookingEastAndDown, new double3(-50.0, 30.0, 80.0), 2000.0, 14.0);

        Assert.Equal(2000.0, Vec.Len(offset), 6);
        Assert.True(offset.X < -1900.0);
        Assert.Equal(0.2, offset.Y / offset.X, 6);
        Assert.Equal(14.0, double.RadiansToDegrees(Math.Asin(offset.Z / 2000.0)), 6);

        // Looking straight down there is no bearing, and the chase's side decides.
        double3 straightDown = ChaseView.ObserverOffset(Up, new double3(0.01, 0.0, -1.0),
                                                        new double3(0.0, -40.0, 90.0), 2000.0, 14.0);
        Assert.True(straightDown.Y < -1900.0);
    }

    [Fact]
    public void ThePairIsFramedWhenItFitsAndTheKeptOneWhenItDoesNot()
    {
        double3 keep = new(1.0, 0.0, 0.0);
        double3 near = new(Math.Cos(0.1745), 0.0, Math.Sin(0.1745));
        (double3 both, double fov) = ChaseView.Frame(keep, near, 3.0, 50.0);
        Assert.Equal(10.0 / 0.7, fov, 1);
        Assert.Equal(5.0, double.RadiansToDegrees(Vec.AngleBetween(both, keep)), 1);

        double3 far = new(Math.Cos(1.4), 0.0, Math.Sin(1.4));
        (double3 kept, double wide) = ChaseView.Frame(keep, far, 3.0, 50.0);
        Assert.Equal(50.0, wide);
        Assert.True(double.RadiansToDegrees(Vec.AngleBetween(kept, keep)) < 0.5 * wide);

        (_, double floored) = ChaseView.Frame(keep, keep, 8.0, 50.0);
        Assert.Equal(8.0, floored);
    }

    [Fact]
    public void TheCloudRisesFromTheFireballToItsTop()
    {
        const double b61 = 0.3e6;
        double atFlash = ChaseView.CloudHeightNow(b61, 0.0);
        double risen = ChaseView.CloudHeightNow(b61, MushroomCloud.RiseSeconds);

        Assert.True(atFlash >= 2.0 * Warhead.FireballRadius(b61) - 1e-9);
        Assert.True(risen > 2.0 * atFlash);
        Assert.InRange(risen, 0.8 * MushroomCloud.DrawnCloudTop(0.3), 1.6 * MushroomCloud.DrawnCloudTop(0.3));
        Assert.InRange(ChaseView.ObserverDistanceMetres(b61), 2000.0, 3000.0);

        foreach (double kt in new[] { 0.3, 10.0, 340.0 })
        {
            double d = ChaseView.ObserverDistanceMetres(kt * 1e6);
            output.WriteLine($"{kt} kt: observer {d:F0} m; cloud at 0/5/15/38 s "
                             + $"{ChaseView.CloudHeightNow(kt * 1e6, 0):F0}/{ChaseView.CloudHeightNow(kt * 1e6, 5):F0}/"
                             + $"{ChaseView.CloudHeightNow(kt * 1e6, 15):F0}/{ChaseView.CloudHeightNow(kt * 1e6, 38):F0} m, "
                             + $"fireball fov {ChaseView.FovToFit(2.0 * Warhead.FireballRadius(kt * 1e6), d):F1} deg");
        }
    }
}
