using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Where the pointer meets the ground. The case worth pinning is the hill seen side-on: the mean
/// sphere is hit behind it, and a pick that starts from there lands on the terrain beyond.
/// </summary>
public class TerrainRayTests
{
    private const double Radius = 6371000.0;
    private const double Ceiling = 16000.0;

    private static readonly double3 Centre = new(0, 0, 0);

    /// <summary>Flat ground everywhere except a hill of a given height over a band of longitude.</summary>
    private sealed class Hill(double fromDeg, double toDeg, double height) : ITerrainHeights
    {
        public int Asked { get; private set; }

        public bool TryHeight(double3 dirFromCentre, out double metres)
        {
            Asked++;

            double deg = double.RadiansToDegrees(Math.Atan2(dirFromCentre.Y, dirFromCentre.X));
            metres = deg >= fromDeg && deg <= toDeg ? height : 0.0;

            return true;
        }
    }

    private static readonly Hill Flat = new(10.0, 11.0, 0.0);

    // A point on the sphere at this longitude and altitude, in the equatorial plane. 0.01 deg is 1.11 km.
    private static double3 At(double longitudeDeg, double altitude)
    {
        double a = double.DegreesToRadians(longitudeDeg);

        return new double3(Math.Cos(a), Math.Sin(a), 0) * (Radius + altitude);
    }

    private static double LongitudeDeg(double3 point) => double.RadiansToDegrees(Math.Atan2(point.Y, point.X));

    [Fact]
    public void AHillInFrontIsHit_NotTheGroundBehindIt()
    {
        // From 100 m up, at ground 10 km away, with a 300 m hill 3.3 to 4.4 km out standing in the way.
        double3 eye = At(0.0, 100.0);
        double3 behind = At(0.09, 0.0);

        Assert.True(TerrainRay.TryFirstHit(eye, behind - eye, 50_000.0, Centre, Radius, Ceiling,
                                           new Hill(0.03, 0.04, 300.0), out double range));

        double3 hit = eye + (Vec.Unit(behind - eye) * range);
        Assert.InRange(LongitudeDeg(hit), 0.0299, 0.0301);
    }

    [Fact]
    public void FlatGroundIsHitWhereTheRayMeetsIt()
    {
        double3 eye = At(0.0, 100.0);
        double3 ground = At(0.05, 0.0);

        Assert.True(TerrainRay.TryFirstHit(eye, ground - eye, 50_000.0, Centre, Radius, Ceiling, Flat,
                                           out double range));

        Assert.Equal(Vec.Len(ground - eye), range, 1.0);
    }

    [Fact]
    public void ARayClearingTheCrestFindsTheGroundBeyond()
    {
        double3 eye = At(0.0, 1000.0);
        double3 ground = At(0.09, 0.0);

        Assert.True(TerrainRay.TryFirstHit(eye, ground - eye, 50_000.0, Centre, Radius, Ceiling,
                                           new Hill(0.03, 0.04, 300.0), out double range));

        Assert.Equal(Vec.Len(ground - eye), range, 1.0);
    }

    [Fact]
    public void ARayAboveTheHorizonMeetsNothing()
    {
        double3 eye = At(0.0, 100.0);
        double3 rising = At(0.5, 5000.0);

        Assert.False(TerrainRay.TryFirstHit(eye, rising - eye, 200_000.0, Centre, Radius, Ceiling, Flat, out _));
    }

    [Fact]
    public void AnEyeInsideAHillNamesNothing()
    {
        double3 eye = At(0.035, 100.0);
        double3 ground = At(0.09, 0.0);

        Assert.False(TerrainRay.TryFirstHit(eye, ground - eye, 50_000.0, Centre, Radius, Ceiling,
                                            new Hill(0.03, 0.04, 300.0), out _));
    }

    [Fact]
    public void AnOrdinaryPickCostsAFewHundredLookupsAtMost()
    {
        // A shallow ray over flat ground is the expensive case: it is barely above the ground for
        // most of its length, and a step fixed by that alone would take thousands.
        var flat = new Hill(10.0, 11.0, 0.0);
        double3 eye = At(0.0, 100.0);
        double3 ground = At(0.09, 0.0);

        Assert.True(TerrainRay.TryFirstHit(eye, ground - eye, 50_000.0, Centre, Radius, Ceiling, flat, out _));
        Assert.True(flat.Asked < 400, $"a 10 km pick asked {flat.Asked} heights");
    }
}
