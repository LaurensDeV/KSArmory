using Xunit;

namespace KSArmory.Tests;

public class BeamTests
{
    private static readonly BeamProfile Coil = new();

    // 12 km up, where the ABL orbited; ISA's density ratio there.
    private const double TwelveKm = 0.254;

    [Fact]
    public void AMegawattBeamKillsAt100KmFrom12KmUpInTheQuotedThreeToFiveSeconds()
    {
        double seconds = 1.0 / Beam.HealthPerSecond(Coil, 100_000.0, TwelveKm, TwelveKm);

        Assert.InRange(seconds, 3.0, 5.0);
    }

    [Fact]
    public void FarOutTheSpotGrowsWithRangeSoTheBurnFallsAsItsSquare()
    {
        double near = Beam.HealthPerSecond(Coil, 100_000.0, 0.0, 0.0);
        double far = Beam.HealthPerSecond(Coil, 200_000.0, 0.0, 0.0);

        Assert.Equal(2.0, Beam.SpotRadiusMetres(Coil, 200_000.0) / Beam.SpotRadiusMetres(Coil, 100_000.0), 6);
        Assert.Equal(4.0, near / far, 6);
    }

    [Fact]
    public void WithNoAirEverythingArrivesAndThickAirTakesMore()
    {
        Assert.Equal(1.0, Beam.Transmission(Coil, 300_000.0, 0.0, 0.0));
        Assert.True(Beam.Transmission(Coil, 50_000.0, 1.0, 1.0)
                    < Beam.Transmission(Coil, 50_000.0, TwelveKm, TwelveKm));
    }

    [Fact]
    public void AtPointBlankTheSpotStopsAtACentimetreRatherThanFocusingToNothing()
    {
        Assert.Equal(0.01, Beam.SpotRadiusMetres(Coil, 0.0));
        Assert.True(double.IsFinite(Beam.HealthPerSecond(Coil, 0.0, 1.0, 1.0)));
    }
}
