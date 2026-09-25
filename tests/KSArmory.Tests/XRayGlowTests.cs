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
