using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class CloudCoverTests
{
    private const double R = 6_371_000.0;
    private const double Bottom = R + 2_000.0;
    private const double Top = R + 8_700.0;

    private static CloudLayer Layer(CoverageCube cube, doubleQuat? toCloud = null) =>
        new(Bottom, Top, toCloud ?? doubleQuat.Identity, cube);

    private static readonly double3 OnTheGround = new(R + 10.0, 0, 0);

    [Fact]
    public void ABc4BlockDecodesItsEightValuePaletteAndItsSixValueOne()
    {
        // r0 > r1: index 2 is 6/7 r0 + 1/7 r1. Texel (1,0) carries index 2, the rest index 0.
        byte[] eight = [210, 0, 0b0001_0000, 0, 0, 0, 0, 0];
        Assert.Equal(210, CoverageCube.Bc4(eight, 0, 0));
        Assert.Equal(180, CoverageCube.Bc4(eight, 1, 0));

        // r0 <= r1: indices 6 and 7 are 0 and 255. Texel (0,0) index 6, texel (1,0) index 7.
        byte[] six = [10, 20, 0b0011_1110, 0, 0, 0, 0, 0];
        Assert.Equal(0, CoverageCube.Bc4(six, 0, 0));
        Assert.Equal(255, CoverageCube.Bc4(six, 1, 0));
    }

    [Fact]
    public void EachAxisLandsInTheMiddleOfItsOwnFaceInVulkansOrder()
    {
        double3[] axes = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];

        for (int i = 0; i < 6; i++)
        {
            Assert.True(CoverageCube.Face(axes[i], out int face, out double s, out double t));
            Assert.Equal(i, face);
            Assert.Equal(0.5, s, 9);
            Assert.Equal(0.5, t, 9);
        }
    }

    [Fact]
    public void ABeamFiredUpIntoFullCoverStopsAtTheLayersBase()
    {
        Assert.True(CloudCover.TryEntry(OnTheGround, new double3(1, 0, 0), 300_000.0,
                                        [Layer(CoverageCube.Uniform(8, 255))], out double at));

        Assert.InRange(at, Bottom - OnTheGround.X - 3.0, Bottom - OnTheGround.X + 3.0);
    }

    [Fact]
    public void ClearSkyAndBrokenCloudLetItThrough()
    {
        Assert.False(CloudCover.TryEntry(OnTheGround, new double3(1, 0, 0), 300_000.0,
                                         [Layer(CoverageCube.Uniform(8, 0))], out _));
        Assert.False(CloudCover.TryEntry(OnTheGround, new double3(1, 0, 0), 300_000.0,
                                         [Layer(CoverageCube.Uniform(8, (byte)(0.7 * 255)))], out _));
    }

    [Fact]
    public void ACloudBeyondTheBeamsReachDoesNotStopIt()
    {
        Assert.False(CloudCover.TryEntry(OnTheGround, new double3(1, 0, 0), 1_000.0,
                                         [Layer(CoverageCube.Uniform(8, 255))], out _));
    }

    [Fact]
    public void LessCoverIsLowerCloudSoABeamAboveItsTopPasses()
    {
        // 90% cover tops out at 90% of the band; a level beam a kilometre above that is clear.
        double cloudTop = Bottom + (0.9 * (Top - Bottom));
        double3 above = new(cloudTop + 1_000.0, 0, 0);
        CloudLayer layer = Layer(CoverageCube.Uniform(8, (byte)(0.9 * 255)));

        Assert.False(CloudCover.TryEntry(above, new double3(0, 1, 0), 20_000.0, [layer], out _));
        Assert.True(CloudCover.TryEntry(new double3(cloudTop - 1_000.0, 0, 0), new double3(0, 1, 0), 20_000.0,
                                        [layer], out double inside));
        Assert.Equal(0.0, inside);
    }

    [Fact]
    public void TheCoverageIsReadInTheLayersOwnTurnedFrame()
    {
        // Cloud only on the cube's -X face; turned half a round about Z, that is over the body's +X.
        CoverageCube westOnly = CoverageCube.PerFace(8, [0, 255, 0, 0, 0, 0]);
        doubleQuat halfTurn = doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), Math.PI);

        Assert.False(CloudCover.TryEntry(OnTheGround, new double3(1, 0, 0), 300_000.0, [Layer(westOnly)], out _));
        Assert.True(CloudCover.TryEntry(OnTheGround, new double3(1, 0, 0), 300_000.0,
                                        [Layer(westOnly, halfTurn)], out _));
    }

    [Fact]
    public void TheNearerOfTwoLayersStopsTheBeam()
    {
        CloudLayer low = Layer(CoverageCube.Uniform(8, 255));
        CloudLayer high = new(R + 11_000.0, R + 12_200.0, doubleQuat.Identity, CoverageCube.Uniform(8, 255));

        Assert.True(CloudCover.TryEntry(OnTheGround, new double3(1, 0, 0), 300_000.0, [high, low], out double at));
        Assert.InRange(at, Bottom - OnTheGround.X - 3.0, Bottom - OnTheGround.X + 3.0);
    }

    [Fact]
    public void ABeamFiredDownFromAboveMeetsTheCloudTopsNotTheBase()
    {
        double3 high = new(R + 20_000.0, 0, 0);

        Assert.True(CloudCover.TryEntry(high, new double3(-1, 0, 0), 300_000.0,
                                        [Layer(CoverageCube.Uniform(8, 255))], out double at));
        Assert.InRange(at, high.X - Top - 3.0, high.X - Top + 3.0);
    }
}
