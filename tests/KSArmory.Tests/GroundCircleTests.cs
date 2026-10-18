using System;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The ring of ground a blast radius covers, which a map has to draw as the shape it actually is.
/// </summary>
public class GroundCircleTests
{
    private const double Earth = 6_371_000.0;

    private static double Haversine(double aLat, double aLon, double bLat, double bLon)
    {
        double dLat = double.DegreesToRadians(bLat - aLat);
        double dLon = double.DegreesToRadians(bLon - aLon);
        double s = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
                   + (Math.Cos(double.DegreesToRadians(aLat))
                      * Math.Cos(double.DegreesToRadians(bLat))
                      * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));

        return 2 * Earth * Math.Asin(Math.Min(1.0, Math.Sqrt(s)));
    }

    [Fact]
    public void NoDistanceIsTheSamePlace()
    {
        GroundCircle.At(-24.0, -62.0, 0.0, Earth, 1.0, out double lat, out double lon);

        Assert.Equal(-24.0, lat, 9);
        Assert.Equal(-62.0, lon, 9);
    }

    [Fact]
    public void DueNorthMovesLatitudeAlone()
    {
        GroundCircle.At(0.0, 0.0, 111_195.0, Earth, 0.0, out double lat, out double lon);

        Assert.Equal(1.0, lat, 2);
        Assert.Equal(0.0, lon, 6);
    }

    [Fact]
    public void AtTheEquatorDueEastMovesLongitudeAlone()
    {
        GroundCircle.At(0.0, 0.0, 111_195.0, Earth, Math.PI / 2, out double lat, out double lon);

        Assert.Equal(0.0, lat, 6);
        Assert.Equal(1.0, lon, 2);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-24.0)]
    [InlineData(60.0)]
    [InlineData(84.0)]
    public void EveryPointOfTheRingIsTheSameDistanceAway(double latitude)
    {
        const double Radius = 40_000.0;

        for (int i = 0; i < 24; i++)
        {
            double bearing = 2 * Math.PI * i / 24;

            GroundCircle.At(latitude, 12.0, Radius, Earth, bearing,
                            out double lat, out double lon);

            double away = Haversine(latitude, 12.0, lat, lon);
            Assert.True(Math.Abs(away - Radius) < 1.0,
                        $"bearing {bearing:F2} came out {away:F1} m, not {Radius:F1}");
        }
    }

    [Fact]
    public void AwayFromTheEquatorTheRingIsWiderInLongitudeThanInLatitude()
    {
        // The whole reason this is not a screen-space circle.
        const double Radius = 100_000.0;

        GroundCircle.At(60.0, 0.0, Radius, Earth, 0.0, out double north, out _);
        GroundCircle.At(60.0, 0.0, Radius, Earth, Math.PI / 2, out _, out double east);

        Assert.True(Math.Abs(east - 0.0) > Math.Abs(north - 60.0) * 1.5);
    }

    [Fact]
    public void TheHeightBoundMatchesWalkingDueNorth()
    {
        GroundCircle.At(0.0, 0.0, 50_000.0, Earth, 0.0, out double lat, out _);

        Assert.Equal(lat, GroundCircle.HeightDeg(50_000.0, Earth), 6);
    }
}
