using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The engine's aerodynamic loads as a table the lead flies a target on.
/// </summary>
public class AeroTableTests
{
    private static DragShape Drone(doubleQuat attitude, double3 rates = default) => new(
        Positive: new double3(0.3 * 8.19, 1.2 * 34.5, 1.2 * 33.9),
        Negative: new double3(1.0 * 8.19, 1.2 * 34.5, 1.2 * 33.9),
        Skin: 0.1 * 2.0 * (8.19 + 34.5 + 33.9),
        Body2Ecl: attitude,
        BodyRates: rates,
        Mass: 1000.0,
        ReferenceDensity: 1.225,
        ExhaustVelocity: 0.0,
        PropellantMass: 0.0);

    private static AeroTable Filled(Func<double3, double3> forcePerQ)
    {
        var table = new AeroTable(3);
        for (int i = 0; i < table.Count; i++) table.Set(i, forcePerQ(table.Direction(i)));
        table.Seal();
        return table;
    }

    [Fact]
    public void TheIcosphereIsUnitDirectionsWithNoneRepeated()
    {
        double3[] points = AeroTable.Icosphere(3).Points;

        Assert.Equal(642, points.Length);
        Assert.All(points, p => Assert.Equal(1.0, Vec.Len(p), 9));
        for (int i = 0; i < points.Length; i++)
        {
            for (int j = i + 1; j < points.Length; j++) Assert.True(Vec.Dot(points[i], points[j]) < 0.999);
        }
    }

    [Fact]
    public void ATableIsReadOnlyOnceEveryDirectionIsFilled()
    {
        var table = new AeroTable(1);
        for (int i = 0; i < table.Count - 1; i++) table.Set(i, new double3(1, 0, 0));
        table.Seal();

        Assert.False(table.IsComplete);
        Assert.Equal(Vec.Zero, table.ForcePerQ(new double3(1, 0, 0)));
    }

    // The table filled with the box's own drag must fly as the box does: a sign or a frame slipped in
    // the table's path is a drag pointing anywhere but against the airflow.
    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0.0)]
    [InlineData(30.0, 10.0, 0.0, 0.0)]
    [InlineData(-70.0, 45.0, 2.0, 0.6)]
    [InlineData(120.0, -20.0, 3.0, 0.7)]
    public void ATableOfTheBoxsOwnDragFliesAsTheBoxDoes(double yawDeg, double pitchDeg, double seconds, double rate)
    {
        doubleQuat attitude = doubleQuat.CreateFromYawPitchRoll(double.DegreesToRadians(yawDeg),
                                                                 double.DegreesToRadians(pitchDeg), 0.3);
        DragShape box = Drone(attitude, new double3(0.1, rate, -0.2));

        // The box's force per q in its own frame, for air flowing along flowBody past it.
        AeroTable table = Filled(flowBody =>
        {
            double3 d = -flowBody;
            double area = (Math.Max(d.X, 0) * box.Positive.X) + (Math.Max(d.Y, 0) * box.Positive.Y)
                          + (Math.Max(d.Z, 0) * box.Positive.Z) + (Math.Max(-d.X, 0) * box.Negative.X)
                          + (Math.Max(-d.Y, 0) * box.Negative.Y) + (Math.Max(-d.Z, 0) * box.Negative.Z) + box.Skin;
            return flowBody * area;
        });
        DragShape aero = box with { Aero = table };

        foreach (double3 air in new[] { new double3(200, 0, 0), new double3(-50, 180, 40), new double3(10, -20, -230) })
        {
            double3 expected = box.DragAcceleration(air, 0.4, seconds);
            double3 got = aero.DragAcceleration(air, 0.4, seconds);

            Assert.True(Vec.Len(got - expected) < 0.05 * Vec.Len(expected),
                        $"air {air}: table {got} against box {expected}");
        }
    }

    [Fact]
    public void EveryGridPointIsWeightedOnlyOnTheSamplesAroundIt()
    {
        // A force equal to the direction: interpolated between neighbours it stays within a hair of unit
        // length everywhere, which a grid point left unweighted (zero) or weighted on far samples is not.
        var table = new AeroTable(3);
        for (int i = 0; i < table.Count; i++) table.Set(i, table.Direction(i));
        table.Seal();

        for (double lat = -90; lat <= 90; lat += 7.3)
        {
            for (double lon = 0; lon < 360; lon += 11.1)
            {
                double la = double.DegreesToRadians(lat), lo = double.DegreesToRadians(lon);
                var d = new double3(Math.Cos(la) * Math.Cos(lo), Math.Cos(la) * Math.Sin(lo), Math.Sin(la));
                double3 f = table.ForcePerQ(d);
                Assert.True(Vec.Dot(f, d) > 0.98, $"at {lat:F1},{lon:F1}: {f}");
            }
        }
    }

    [Fact]
    public void LiftInTheTablePushesSquareToTheAirflow()
    {
        // A wing's worth of lift: 20 m² of force per q along the body's +Z, whatever the airflow.
        DragShape aero = Drone(doubleQuat.Identity) with { Aero = Filled(_ => new double3(0, 0, 20)) };

        double3 a = aero.DragAcceleration(new double3(200, 0, 0), 1.0);

        double q = 0.5 * 1.225 * 200 * 200;
        Assert.Equal(0.0, a.X, 6);
        Assert.Equal(q * 20 / 1000.0, a.Z, 3);
    }
}
