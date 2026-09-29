using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The sign of a carry between the burst instant and the step-end sample. Every scored miss, drawn
/// burst and ground anchor goes through one of these two, and a sign slipped at any one of them is
/// a step of the planet's ~30 km/s -- hundreds of metres -- in the wrong direction.
/// </summary>
public class InFrameTests
{
    private static readonly double3 Travel = new(29_800.0, 0.0, 0.0);
    private const double Since = -0.016;

    [Fact]
    public void ABurstIsBehindTheSampleAlongTheTravel()
    {
        // Samples arrive at the step's end, so what the world held at the burst is further back
        // along its own motion, and the burst's ground has moved on ahead of it by the sample.
        double3 atBurst = InFrame.AtBurst(Vec.Zero, Travel, Since);
        Assert.Equal(-476.8, atBurst.X, 6);

        double3 atSample = InFrame.AtSample(Vec.Zero, Travel, Since);
        Assert.Equal(476.8, atSample.X, 6);
    }

    [Fact]
    public void TheTwoCarriesUndoEachOther()
    {
        double3 p = new(1.0e11, -3.0e10, 42.0);
        double3 there = InFrame.AtSample(InFrame.AtBurst(p, Travel, Since), Travel, Since);
        Assert.True(Vec.Len(there - p) < 1e-3);
    }

    [Fact]
    public void TheGroundAnchorIsTheSampleCarry()
        => Assert.Equal(InFrame.AtSample(Vec.Zero, Travel, Since),
                        BlastSweep.GroundAtSample(Vec.Zero, Travel, Since));
}
