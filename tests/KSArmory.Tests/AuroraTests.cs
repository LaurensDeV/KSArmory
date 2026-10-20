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

    // KSA's Earth air, which puts the curtains' border at 100 km.
    private static readonly BodyAir Earth = new(101_325, 1.225, 8_000, 167_000, 9.81, Radius, BodyTraits.Default);
    private static readonly double Foot = Aurora.BottomAltitude(Earth);
    private static readonly MagneticDipole Centred = new(Vec.Zero, Axis, 3.1e-5, Radius);

    [Fact]
    public void TheBorderSitsAtAHundredKilometresInEarthsAirAndHigherInADeeperOne()
    {
        Assert.Equal(100_000.0, Foot, 3);
        Assert.Equal(505_000.0, Aurora.TopAltitude(Earth), 3);

        BodyAir deeper = Earth with { ScaleHeightMetres = 18_000, SeaLevelDensity = 65.0, SeaLevelPascals = 92 * 101_325, BoundaryMetres = 500_000 };
        Assert.True(Aurora.BottomAltitude(deeper) > 250_000.0);
    }

    private static double3 At(double latitudeDeg, double altitude)
    {
        double lat = double.DegreesToRadians(latitudeDeg);
        return new double3(Math.Cos(lat), 0, Math.Sin(lat)) * (Radius + altitude);
    }

    private static double LatitudeDeg(double3 at) => double.RadiansToDegrees(Math.Asin(Vec.Unit(at).Z));

    /// <summary>
    /// Teak, 77 km over Johnston Island at 17° N, lit an aurora over Samoa at about 14° S: the far end of
    /// its field line, some 3,000 km away.
    /// </summary>
    [Fact]
    public void TeakLitTheSkyOverSamoa()
    {
        Span<double3> feet = stackalloc double3[2];
        int count = Centred.Footpoints(At(16.7, 77_000.0), Radius + Foot, feet);
        Assert.Equal(2, count);

        double near = LatitudeDeg(feet[0]);
        double far = LatitudeDeg(feet[1]);
        double apart = Vec.AngleBetween(feet[0], feet[1]) * Radius;
        Assert.Equal(Radius + Foot, Vec.Len(feet[0]), 3);
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
        Assert.Equal(2, Centred.Footpoints(burst, Radius + Foot, feet));

        Assert.Equal(feet[0].Z, -feet[1].Z, 9);
        Assert.Equal(Math.Atan2(burst.Y, burst.X), Math.Atan2(feet[0].Y, feet[0].X), 9);
        Assert.Equal(Math.Atan2(burst.Y, burst.X), Math.Atan2(feet[1].Y, feet[1].X), 9);
        Assert.True(feet[0].Z > 0.0, "the end in the burst's own hemisphere comes first");
    }

    [Fact]
    public void ABurstOnTheEquatorUnderTheAuroraReachesNoAirWithIt()
    {
        Span<double3> feet = stackalloc double3[2];
        Assert.Equal(0, Centred.Footpoints(At(0.0, 90_000.0), Radius + Foot, feet));

        // And from 400 km its line comes down about twelve degrees either side.
        Assert.Equal(2, Centred.Footpoints(At(0.0, 400_000.0), Radius + Foot, feet));
        Assert.InRange(LatitudeDeg(feet[0]), 11.0, 13.5);
    }

    [Fact]
    public void AnArcRunsMagneticEastWest()
    {
        double3 foot = At(20.0, 0.0);
        double3 east = Centred.EastAt(foot);

        Assert.Equal(0.0, Vec.Dot(east, Vec.Unit(foot)), 9);
        Assert.Equal(0.0, Vec.Dot(east, Axis), 9);
    }

    /// <summary>
    /// KSA's Earth: a dipole toward 80.7° N 72.7° W with its centre 570 km off. Its feet still lie on the
    /// burst's own field line and on the border's sphere about the planet, and the offset moves them.
    /// </summary>
    [Fact]
    public void OnATiltedOffCentreFieldTheFeetStayOnTheBurstsLineAndTheBorder()
    {
        double lat = double.DegreesToRadians(80.7), lon = double.DegreesToRadians(-72.7);
        MagneticDipole ksa = new(new double3(-446_000, 290_000, 204_000),
                                 new double3(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat)),
                                 3.0e-5, Radius);
        double3 burst = At(16.7, 400_000.0);

        Span<double3> feet = stackalloc double3[2];
        Span<double3> centred = stackalloc double3[2];
        Assert.Equal(2, ksa.Footpoints(burst, Radius + Foot, feet));
        Assert.Equal(2, Centred.Footpoints(burst, Radius + Foot, centred));

        foreach (double3 f in feet)
        {
            Assert.Equal(Radius + Foot, Vec.Len(f), 0);
            Assert.Equal(ksa.L(burst), ksa.L(f), 6);
        }

        double moved = Vec.Len(feet[1] - centred[1]);
        output.WriteLine($"L {ksa.L(burst):F3}; the far foot moved {moved / 1000.0:F0} km by the tilt and offset");
        Assert.True(moved > 100_000.0);
    }

    [Fact]
    public void ABodyWithNoFieldHasNoFeet()
    {
        Span<double3> feet = stackalloc double3[2];
        Assert.Equal(0, MagneticDipole.None.Footpoints(At(16.7, 400_000.0), Radius + Foot, feet));
    }

    [Fact]
    public void ItArrivesInSecondsAndFadesOverMinutes()
    {
        Assert.Equal(0.0, Aurora.Strength(1000.0, 0.0));
        double lit = Aurora.Strength(1000.0, Aurora.ArrivesSeconds);
        Assert.Equal(Aurora.PeakBrightness, lit, 9);
        Assert.True(Aurora.Strength(1000.0, 300.0) < lit);
        Assert.Equal(0.0, Aurora.Strength(1000.0, Aurora.LifeSeconds));
        Assert.True(Aurora.Strength(4000.0, 10.0) > Aurora.Strength(1000.0, 10.0));
    }
}
