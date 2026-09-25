using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Where a burst above the atmosphere lights an aurora: both ends of its own field line, on a dipole
/// along the spin axis.
/// </summary>
public class AuroraTests(ITestOutputHelper output)
{
    private const double Radius = 6_371_000.0;
    private static readonly double3 Axis = new(0, 0, 1);

    private static double3 At(double latitudeDeg, double altitude)
    {
        double lat = double.DegreesToRadians(latitudeDeg);
        return new double3(Math.Cos(lat), 0, Math.Sin(lat)) * (Radius + altitude);
    }

    private static double LatitudeDeg(double3 unit) => double.RadiansToDegrees(Math.Asin(unit.Z));

    /// <summary>
    /// Teak, 77 km over Johnston Island at 17° N, lit an aurora over Samoa at about 14° S: the far end of
    /// its field line, some 3,000 km away.
    /// </summary>
    [Fact]
    public void TeakLitTheSkyOverSamoa()
    {
        Span<double3> feet = stackalloc double3[2];
        int count = Aurora.Footpoints(At(16.7, 77_000.0), Axis, Radius, feet);
        Assert.Equal(2, count);

        double near = LatitudeDeg(feet[0]);
        double far = LatitudeDeg(feet[1]);
        double apart = Vec.AngleBetween(feet[0], feet[1]) * Radius;
        output.WriteLine($"feet at {near:F1} and {far:F1} deg, {apart / 1000.0:F0} km apart");

        Assert.InRange(near, 15.0, 18.0);
        Assert.InRange(far, -18.0, -15.0);
        Assert.InRange(apart, 3_000_000.0, 4_200_000.0);
    }

    [Fact]
    public void TheTwoEndsAreMirroredAcrossTheMagneticEquatorOnTheBurstsMeridian()
    {
        Span<double3> feet = stackalloc double3[2];
        double3 burst = new double3(0.6, 0.5, 0.3) * (Radius + 400_000.0);
        Assert.Equal(2, Aurora.Footpoints(burst, Axis, Radius, feet));

        Assert.Equal(feet[0].Z, -feet[1].Z, 9);
        Assert.Equal(Math.Atan2(burst.Y, burst.X), Math.Atan2(feet[0].Y, feet[0].X), 9);
        Assert.Equal(Math.Atan2(burst.Y, burst.X), Math.Atan2(feet[1].Y, feet[1].X), 9);
        Assert.True(feet[0].Z > 0.0, "the end in the burst's own hemisphere comes first");
    }

    [Fact]
    public void ABurstOnTheEquatorUnderTheAuroraReachesNoAirWithIt()
    {
        Span<double3> feet = stackalloc double3[2];
        Assert.Equal(0, Aurora.Footpoints(At(0.0, 90_000.0), Axis, Radius, feet));

        // And from 400 km its line comes down about twelve degrees either side.
        Assert.Equal(2, Aurora.Footpoints(At(0.0, 400_000.0), Axis, Radius, feet));
        Assert.InRange(LatitudeDeg(feet[0]), 11.0, 13.5);
    }

    [Fact]
    public void AnArcRunsMagneticEastWest()
    {
        double3 foot = Vec.Unit(At(20.0, 0.0));
        double3 east = Aurora.EastAt(foot, Axis);

        Assert.Equal(0.0, Vec.Dot(east, foot), 9);
        Assert.Equal(0.0, Vec.Dot(east, Axis), 9);
    }

    [Fact]
    public void ItArrivesInSecondsAndFadesOverMinutes()
    {
        Assert.Equal(0.0, Aurora.Strength(1000.0, 0.0));
        double lit = Aurora.Strength(1000.0, Aurora.ArrivesSeconds);
        Assert.Equal(Aurora.PeakNits, lit, 9);
        Assert.True(Aurora.Strength(1000.0, 300.0) < lit);
        Assert.Equal(0.0, Aurora.Strength(1000.0, Aurora.LifeSeconds));
        Assert.True(Aurora.Strength(4000.0, 10.0) > Aurora.Strength(1000.0, 10.0));
    }
}
