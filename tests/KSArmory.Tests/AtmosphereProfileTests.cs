using Xunit;

namespace KSArmory.Tests;

public class AtmosphereProfileTests
{
    // KSA's Earth as declared in 2026.11.10.5801: US Standard Atmosphere 1976 to 86 km, one atmosphere at
    // the surface, air's gas constant, the mean radius and its own G·M/r².
    private const double Radius = 6.371e6;
    private const double Gravity = 9.798;
    private const double AirGasConstant = 287.05;

    private static readonly double[] Km = [0, 11.019, 20.063, 32.162, 47.35, 51.413, 71.802, 86];
    private static readonly double[] Kelvin = [288.15, 216.65, 216.65, 228.65, 270.65, 270.65, 214.65, 186.87];

    private static AtmosphereProfile Earth()
        => new(Radius, Gravity, AirGasConstant, 101_325, Km.Select(k => k * 1000.0).ToArray(), Kelvin);

    [Theory]
    [InlineData(0.0, 101_325, 1.2250)]
    [InlineData(11_019, 22_632, 0.36392)]
    [InlineData(20_063, 5_474.9, 0.088035)]
    [InlineData(32_162, 868.02, 0.013225)]
    public void TheLayersReproduceTheStandardAtmosphereAtItsBreakpoints(double metres, double pascals, double density)
    {
        (double p, _, double rho) = Earth().At(metres);

        Assert.InRange(p / pascals, 0.995, 1.005);
        Assert.InRange(rho / density, 0.995, 1.005);
    }

    [Fact]
    public void TemperatureIsLinearInGeopotentialAndHeldAboveTheLastPoint()
    {
        AtmosphereProfile earth = Earth();

        Assert.Equal(216.65, earth.At(15_000).Kelvin, 9);
        Assert.Equal(186.87, earth.At(120_000).Kelvin, 9);
        Assert.InRange(earth.At(5_000).Kelvin, 255.0, 256.0);
    }

    [Fact]
    public void OnePointIsIsothermalWithTheHydrostaticScaleHeightInGeopotential()
    {
        AtmosphereProfile flat = new(Radius, Gravity, AirGasConstant, 101_325, [0.0], [250.0]);
        double scaleHeight = AirGasConstant * 250.0 / Gravity;
        double h = 40_000.0;
        double geopotential = Radius * h / (Radius + h);

        Assert.Equal(Math.Exp(-geopotential / scaleHeight), flat.At(h).Pascals / 101_325, 12);
    }

    [Fact]
    public void ALayeredBodyAirInvertsItsOwnColumnAndDensity()
    {
        BodyAir air = BodyAir.Layered(Earth(), 140_000, Gravity, Radius, BodyTraits.Default, wet: true);

        foreach (double h in (double[])[1_000, 15_000, 48_000, 90_000])
        {
            Assert.Equal(h, air.AltitudeOfColumn(air.ColumnAbove(h)), 3);
            Assert.Equal(h, air.AltitudeOfDensity(air.DensityAt(h)), 3);
        }
    }

    [Fact]
    public void ALayeredBodyAirClampsAtTheGroundAndStopsAtItsBoundary()
    {
        BodyAir air = BodyAir.Layered(Earth(), 140_000, Gravity, Radius, BodyTraits.Default, wet: true);

        Assert.Equal(air.SeaLevelDensity, air.DensityAt(-300), 12);
        Assert.Equal(0.0, air.DensityAt(140_000));
        Assert.InRange(air.ScaleHeightMetres, 8_400, 8_500);
        Assert.InRange(air.SoundMetresPerSecond, 340.0, 340.5);
    }

    [Fact]
    public void TheColumnIsThePressureOverGravityNotTheSurfaceScaleHeight()
    {
        BodyAir air = BodyAir.Layered(Earth(), 140_000, Gravity, Radius, BodyTraits.Default, wet: true);

        // Above the tropopause the air is colder than at the surface, so ρ·H₀ overstates the column.
        Assert.Equal(air.PressureAt(20_000) / Gravity, air.ColumnAbove(20_000), 9);
        Assert.True(air.ColumnAbove(20_000) < air.DensityAt(20_000) * air.ScaleHeightMetres);
    }
}
