using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The layer of air a burst high over the atmosphere lights: only from high enough, green for a moment
/// and red for minutes, brighter under a bigger or nearer burst.
/// </summary>
public class XRayGlowTests
{
    [Fact]
    public void OnlyABurstOverTheAirLightsTheLayer()
    {
        Assert.False(XRayGlow.Lights(0.01));
        Assert.False(XRayGlow.Lights(XRayGlow.AirRatio));
        Assert.True(XRayGlow.Lights(1.0e-7));
        Assert.True(XRayGlow.Lights(0.0));
    }

    [Fact]
    public void ItLightsInASecondAndFadesOnTheRedLinesLife()
    {
        const double kt = 1400.0;
        const double over = 320_000.0;

        Assert.Equal(0.0, XRayGlow.Strength(kt, over, 0.0));
        double lit = XRayGlow.Strength(kt, over, XRayGlow.RiseSeconds);
        Assert.True(lit > 0.0);

        double later = XRayGlow.Strength(kt, over, XRayGlow.RiseSeconds + XRayGlow.RedSeconds);
        Assert.Equal(lit / Math.E, later, 9);

        Assert.Equal(0.0, XRayGlow.Strength(kt, over, XRayGlow.LifeSeconds));
        Assert.True(XRayGlow.Strength(kt, over, XRayGlow.LifeSeconds - 61.0) > 0.0);
    }

    [Fact]
    public void ItIsGreenFirstAndRedAfter()
    {
        Assert.Equal(1.0, XRayGlow.GreenShare(0.0));
        Assert.True(XRayGlow.GreenShare(30.0) < 0.001);
    }

    [Fact]
    public void ABiggerOrNearerBurstLightsItHarderUpToACeiling()
    {
        double far = XRayGlow.Strength(1000.0, 300_000.0, 2.0);
        Assert.True(XRayGlow.Strength(1000.0, 150_000.0, 2.0) > far);
        Assert.True(XRayGlow.Strength(4000.0, 300_000.0, 2.0) > far);
        Assert.True(XRayGlow.Strength(1.0e6, 10_000.0, 1.0) <= XRayGlow.MostNits);
    }
}

/// <summary>Where the X-ray-heated layer sits, by the air above it rather than by a height.</summary>
public class XRayLayerTests
{
    private static readonly BodyAir Earth = new(101_325, 1.225, 8_000, 167_000, 9.81, 6.371e6, BodyTraits.Default);

    [Fact]
    public void InEarthsAirItIsGlasstonesEightyTwoKilometresAndTwentyThick()
    {
        Assert.Equal(82_000.0, XRayGlow.LayerAltitude(Earth), 3);
        Assert.Equal(20_000.0, XRayGlow.LayerThickness(Earth), 9);
    }

    /// <summary>
    /// Hydrogen stops X-rays far less per kilogram, so they run deeper before they are stopped -- and
    /// the aurora's border, keyed on the air's mass alone, does not follow them down.
    /// </summary>
    [Fact]
    public void AnAirThatStopsXRaysLessPutsTheLayerDeeperAndApartFromTheAurora()
    {
        BodyAir hydrogen = Earth with { Traits = new BodyTraits(XRayOpacity: 0.01) };

        Assert.True(XRayGlow.LayerAltitude(hydrogen) < XRayGlow.LayerAltitude(Earth) - 30_000.0);
        Assert.True(Aurora.BottomAltitude(Earth) > XRayGlow.LayerAltitude(Earth) + 15_000.0);
    }
}
