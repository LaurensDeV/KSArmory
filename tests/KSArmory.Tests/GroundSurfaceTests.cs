using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Which surface a falling round meets. A height field answers with terrain and nothing else, so
/// under an ocean it reports the seabed — and a contact-fused warhead taking that for the surface
/// falls through the waterline and bursts on the bottom, which is a detonation nobody can see and
/// is indistinguishable in play from a warhead that failed.
/// </summary>
public class GroundSurfaceTests
{
    /// <summary>Over sea, the sea is the surface — not the seabed a kilometre under it.</summary>
    [Fact]
    public void OverSeaARoundMeetsTheWaterline()
    {
        Assert.Equal(0.0, GroundSurface.Height(terrainHeight: -1200.0, seaLevel: 0.0, hasSea: true));
    }

    /// <summary>Over land the terrain is higher and nothing about dry ground changes.</summary>
    [Fact]
    public void OverLandTheTerrainStillWins()
    {
        Assert.Equal(2400.0, GroundSurface.Height(terrainHeight: 2400.0, seaLevel: 0.0, hasSea: true));
    }

    /// <summary>A body with no ocean is untouched, whatever number comes with it.</summary>
    [Fact]
    public void ABodyWithNoSeaIsUnaffected()
    {
        Assert.Equal(-800.0, GroundSurface.Height(terrainHeight: -800.0, seaLevel: 5000.0, hasSea: false));
    }

    /// <summary>An unreadable waterline must not swallow a perfectly good terrain height.</summary>
    [Fact]
    public void AnUnreadableSeaLevelLeavesTheTerrainAlone()
    {
        Assert.Equal(120.0, GroundSurface.Height(120.0, double.NaN, hasSea: true));
        Assert.True(double.IsNaN(GroundSurface.Height(double.NaN, 0.0, hasSea: true)));
    }
}
