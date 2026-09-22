using KSArmory;
using Xunit;

namespace KSArmory.Tests;

public class FlashGlareTests
{
    private const double HalfField = 25.0;

    [Fact]
    public void ABurstInFrameGivesTheWholeOfIt()
    {
        Assert.Equal(1.0, FlashGlare.Reaching(0.0, HalfField));
        Assert.Equal(1.0, FlashGlare.Reaching(HalfField, HalfField));
    }

    [Fact]
    public void ABurstBehindTheViewerStillReachesTheFloor()
    {
        // The whole point: turning away dims a fireball and cannot abolish it.
        Assert.Equal(FlashGlare.ScatteredFloor, FlashGlare.Reaching(180.0, HalfField), 6);
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(25.0)]
    [InlineData(60.0)]
    public void ItFallsOffWithoutEverGoingBackUp(double halfField)
    {
        double last = double.MaxValue;

        for (double deg = 0.0; deg <= 180.0; deg += 1.0)
        {
            double now = FlashGlare.Reaching(deg, halfField);

            Assert.InRange(now, FlashGlare.ScatteredFloor, 1.0);
            Assert.True(now <= last + 1e-12, $"glare rose at {deg} deg off a {halfField} deg half-field");

            last = now;
        }
    }

    [Fact]
    public void ANarrowFieldStillTakesThePeripheryGradually()
    {
        // A cut at the frame edge would switch the flash on and off as a burst drifted across it,
        // which is a worse artefact than the one this exists to fix. Sighted at three degrees, a
        // burst ten degrees off must still be glaring rather than at the floor.
        double justOutside = FlashGlare.Reaching(10.0, 1.5);

        Assert.True(justOutside > FlashGlare.ScatteredFloor * 2.0,
                    $"a burst just off a narrow frame fell to {justOutside:F3}");
        Assert.True(justOutside < 1.0);
    }

    [Fact]
    public void AnUnreadableViewGivesTheWholeFlashRatherThanNone()
    {
        Assert.Equal(1.0, FlashGlare.Reaching(double.NaN, HalfField));
        Assert.Equal(1.0, FlashGlare.Reaching(30.0, double.NaN));
    }
}
