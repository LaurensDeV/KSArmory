using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class SearchHeadTests
{
    private static readonly SearchSetProfile Irst = new()
    {
        PartId = "test", DisplayName = "test", Sensor = "Irst", HeadMarker = "Head",
        HeadTravelDeg = 60f, ScanHalfWidthDeg = 45f, ScanPeriodSeconds = 6f, ScanNodDeg = 8f,
    };

    private static double OffAxisDeg(double3 d) => double.RadiansToDegrees(Vec.AngleBetween(OpticGeometry.RestDirection, d));

    [Fact]
    public void WithAContactInsideItsTravelTheHeadLooksStraightAtIt()
    {
        var toContact = new double3(0.3, 1.0, -0.4);

        double3 command = SearchHead.Command(Irst, toContact, 0.0);

        Assert.True(Vec.AngleBetween(command, toContact) < 1e-9);
    }

    [Fact]
    public void AContactBeyondItsTravelIsHeldAtTheEdgeOnTheSameBearing()
    {
        var behindAndAbove = new double3(1.0, -0.2, 0.0);

        double3 command = SearchHead.Command(Irst, behindAndAbove, 0.0);

        Assert.Equal(60.0, OffAxisDeg(command), 6);
        Assert.True(command.X > 0.0);
        Assert.Equal(0.0, command.Z, 9);
    }

    [Fact]
    public void WithNothingHeldItSweepsItsWholeWidthEachPeriodAndNeverLeavesItsTravel()
    {
        double widest = 0.0;
        double left = 0.0, right = 0.0;

        for (double t = 0.0; t < 6.0; t += 0.01)
        {
            double3 c = SearchHead.Command(Irst, null, t);
            Assert.Equal(1.0, Vec.Len(c), 9);
            widest = Math.Max(widest, OffAxisDeg(c));
            left = Math.Min(left, c.Z);
            right = Math.Max(right, c.Z);
        }

        Assert.InRange(widest, 44.0, 60.0);
        Assert.True(left < -0.6 && right > 0.6, $"swept from {left:F2} to {right:F2}");
    }

    [Fact]
    public void ItNodsAtTwiceTheSweepRateSoItTracesAFigureOfEightNotATiltedLine()
    {
        // Level at both ends of the sweep and at its middle; at full nod a quarter of the way between.
        foreach (double t in new[] { 0.0, 1.5, 3.0, 4.5 })
        {
            Assert.Equal(0.0, SearchHead.Scan(Irst, t).X, 9);
        }

        Assert.Equal(Math.Sin(double.DegreesToRadians(8.0)), SearchHead.Scan(Irst, 0.75).X, 9);
        Assert.Equal(-Math.Sin(double.DegreesToRadians(8.0)), SearchHead.Scan(Irst, 2.25).X, 9);
    }

    [Fact]
    public void TheSweepRepeatsEveryPeriodAndCrossesTheMiddleHalfwayThrough()
    {
        for (double t = 0.0; t < 6.0; t += 0.37)
        {
            Assert.True(Vec.AngleBetween(SearchHead.Scan(Irst, t), SearchHead.Scan(Irst, t + 6.0)) < 1e-9);
        }

        Assert.Equal(0.0, SearchHead.Scan(Irst, 3.0).Z, 9);
        Assert.True(SearchHead.Scan(Irst, 1.5).Z > 0.6);
        Assert.True(SearchHead.Scan(Irst, 4.5).Z < -0.6);
    }

    [Fact]
    public void SetsSeededFromDifferentPartsDoNotSweepInStep()
    {
        double a = SearchHead.PhaseSeconds(Irst, 1);
        double b = SearchHead.PhaseSeconds(Irst, 2);

        Assert.InRange(a, 0.0, 6.0);
        Assert.InRange(b, 0.0, 6.0);
        Assert.True(Math.Abs(a - b) > 0.3, $"{a:F2} s and {b:F2} s");
    }

    [Fact]
    public void ADirectionStraightBehindStillComesBackInsideTheTravel()
    {
        double3 command = SearchHead.ClampToTravel(new double3(0, -1, 0), 60.0);

        Assert.Equal(60.0, OffAxisDeg(command), 6);
    }
}
