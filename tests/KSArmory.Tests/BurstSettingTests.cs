using KSArmory;
using Xunit;

namespace KSArmory.Tests;

public class BurstSettingTests
{
    private const double Ball = 55.0;

    [Fact]
    public void ABodyWithNoSeaIsAlwaysLand()
    {
        Assert.Equal(BurstSetting.Land, BurstSettings.Classify(-500.0, -800.0, 0.0, false, Ball));
    }

    [Fact]
    public void GroundAboveTheSeaIsLandWhereverTheBurstIs()
    {
        Assert.Equal(BurstSetting.Land, BurstSettings.Classify(12.0, 10.0, 0.0, true, Ball));
        Assert.Equal(BurstSetting.Land, BurstSettings.Classify(2000.0, 10.0, 0.0, true, Ball));
    }

    [Fact]
    public void AStoreStoppedAtTheWaterlineIsASurfaceBurst()
    {
        // The ground test stops a store at the sea's surface, so this is the case play produces.
        Assert.Equal(BurstSetting.WaterSurface, BurstSettings.Classify(0.0, -3000.0, 0.0, true, Ball));
    }

    [Fact]
    public void ABurstWhoseFireballBreaksTheSurfaceIsStillASurfaceBurst()
    {
        Assert.Equal(BurstSetting.WaterSurface, BurstSettings.Classify(-Ball * 0.5, -3000.0, 0.0, true, Ball));
    }

    [Fact]
    public void TheSeabedFourKilometresDownIsUnderwater()
    {
        // A pad on the floor of the South Atlantic: the store bursts under 4,162 m of water.
        Assert.Equal(BurstSetting.Underwater, BurstSettings.Classify(-4162.0, -4162.0, 0.0, true, Ball));
    }

    [Fact]
    public void AnUnreadableHeightIsLandWhichIsWhatEveryBurstWasBefore()
    {
        Assert.Equal(BurstSetting.Land, BurstSettings.Classify(0.0, double.NaN, 0.0, true, Ball));
        Assert.Equal(BurstSetting.Land, BurstSettings.Classify(double.NaN, -3000.0, 0.0, true, Ball));
    }
}
