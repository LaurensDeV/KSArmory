using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// A body's air as the burst reads it, over bodies made up for the purpose -- none of them named, since
/// nothing that reads a BodyAir may know which planet it is on.
/// </summary>
public class BodyAirTests(ITestOutputHelper output)
{
    // KSA's own Earth, Mars and Venus numbers, as declared, and bodies no system has yet.
    private static readonly BodyAir EarthLike = new(101_325, 1.225, 8_000, 167_000, 9.81, 6.371e6,
                                                    new BodyTraits(FieldTesla: 3.1e-5, Airglow: Airglow.OxygenNitrogen));
    private static readonly BodyAir ThinCold = new(0.006 * 101_325, 0.02, 11_000, 120_000, 3.71, 3.39e6,
                                                   new BodyTraits(Gamma: 1.29));
    private static readonly BodyAir ThickHot = new(92 * 101_325, 65.0, 18_000, 300_000, 8.87, 6.05e6,
                                                   new BodyTraits(Gamma: 1.29));
    private static readonly BodyAir Airless = BodyAir.Airless(1.62, 1.737e6, BodyTraits.Default);

    [Fact]
    public void EarthsSeaLevelIsTheReference()
    {
        AmbientAir air = EarthLike.AirAt(0.0);

        Assert.True(air.IsSeaLevel);
        Assert.Equal(340.3, EarthLike.SoundMetresPerSecond, 1);
    }

    [Fact]
    public void TheAirFollowsKsasProfileAndStopsAtItsBoundary()
    {
        Assert.Equal(1.225 * Math.Exp(-37.0 / 8.0), EarthLike.DensityAt(37_000), 12);
        Assert.Equal(1.225, EarthLike.DensityAt(-500), 12);
        Assert.Equal(0.0, EarthLike.DensityAt(167_000));
        Assert.Equal(0.0, EarthLike.PressureAt(400_000));
    }

    /// <summary>
    /// A thin cold atmosphere is not Earth's air at another height: its pressure and density ratios
    /// differ, so its sound runs slower -- which reading density as pressure would miss.
    /// </summary>
    [Fact]
    public void AColdThinAirCarriesSoundSlowerThanItsDensityImplies()
    {
        AmbientAir surface = ThinCold.AirAt(0.0);
        output.WriteLine($"thin cold: density {surface.DensityRatio:F4}, pressure {surface.PressureRatio:F4}, "
                         + $"sound {ThinCold.SoundMetresPerSecond:F0} m/s");

        Assert.NotEqual(surface.DensityRatio, surface.PressureRatio, 3);
        Assert.InRange(ThinCold.SoundMetresPerSecond, 180, 240);
        Assert.InRange(ThickHot.SoundMetresPerSecond, 400, 480);
    }

    [Fact]
    public void AnAirlessBodyHasNoAirAnywhere()
    {
        Assert.False(Airless.HasAir);
        Assert.Equal(AmbientAir.None, Airless.AirAt(0.0));
        Assert.Equal(0.0, Airless.SoundMetresPerSecond);
        Assert.False(Airless.Condenses);
    }

    /// <summary>
    /// A layer calibrated by the air above it lands at whatever height a body puts that column: the same
    /// column sits far higher in a thick atmosphere.
    /// </summary>
    [Fact]
    public void ALayerKeyedOnItsColumnFindsItsOwnHeightOnEachBody()
    {
        double column = EarthLike.ColumnAbove(82_000);
        Assert.Equal(82_000, EarthLike.AltitudeOfColumn(column), 3);

        double onThin = ThinCold.AltitudeOfColumn(column);
        double onThick = ThickHot.AltitudeOfColumn(column);
        output.WriteLine($"Earth's 82 km column sits at {onThin / 1000:F0} km on the thin body, {onThick / 1000:F0} km on the thick");

        Assert.True(onThick > 82_000 && onThin < onThick);
        Assert.Equal(0.0, EarthLike.AltitudeOfColumn(1e9));
    }

    [Fact]
    public void TheDeclaredScaleHeightNeedNotBeHydrostatic()
    {
        Assert.InRange(EarthLike.HydrostaticScaleHeightMetres, 8_400, 8_460);
    }

    [Fact]
    public void CondensationFollowsTheEntryThenTheWater()
    {
        Assert.True((EarthLike with { Traits = new BodyTraits(Condensation: true) }).Condenses);
        Assert.False((EarthLike with { Traits = new BodyTraits(Condensation: false), Wet = true }).Condenses);
        Assert.True((EarthLike with { Traits = BodyTraits.Default, Wet = true }).Condenses);
        Assert.False((EarthLike with { Traits = BodyTraits.Default, Wet = false }).Condenses);
    }
}
