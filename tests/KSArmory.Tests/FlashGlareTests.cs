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

    /// <summary>
    /// The eye answers light on a log scale, so a burst four times further off is a step dimmer,
    /// not a sixteenth as bright. Linear, 0.3 kt whited the view at 2.4 km and did nothing at 10.
    /// </summary>
    [Fact]
    public void FourTimesFurtherOffStillBlindsHalfAsMuch()
    {
        double near = FlashGlare.Level(FlashGlare.Suns(0.3, 2_400.0, MushroomCloud.PeakGlow), 1.0);
        double far = FlashGlare.Level(FlashGlare.Suns(0.3, 9_600.0, MushroomCloud.PeakGlow), 1.0);

        Assert.Equal(1.0, near);
        Assert.InRange(far, 0.3, 0.7);
    }

    /// <summary>
    /// A third of a kilotonne's energy as heat within a few hundredths of a second, over a sphere
    /// 2.4 km across: tens of suns. Reckoned off the drawn ball's size instead, it was eight.
    /// </summary>
    [Fact]
    public void AtThePulseASmallBurstIsTensOfSunsAtTheWatchDistance()
    {
        Assert.InRange(FlashGlare.Suns(0.3, 2_400.0, MushroomCloud.PeakGlow), 30.0, 120.0);
    }

    [Fact]
    public void TheSameFlashBlindsMoreAtNight()
    {
        double suns = FlashGlare.Suns(0.3, 20_000.0, MushroomCloud.PeakGlow);

        Assert.True(FlashGlare.AdaptedTo(-20.0) < FlashGlare.AdaptedTo(30.0) * 0.02);
        Assert.True(FlashGlare.Level(suns, FlashGlare.AdaptedTo(-20.0))
                    > FlashGlare.Level(suns, FlashGlare.AdaptedTo(30.0)) + 0.4);
    }

    [Fact]
    public void NothingAddedIsNothingSeen()
    {
        Assert.Equal(0.0, FlashGlare.Level(0.0, 1.0));
        Assert.Equal(0.0, FlashGlare.Suns(0.3, 2_400.0, 0.0));
    }

    [Fact]
    public void ANightEyeRecoversMoreSlowlyThanADayEye()
    {
        double day = FlashGlare.RecoverySeconds(FlashGlare.AdaptedTo(40.0));
        double dusk = FlashGlare.RecoverySeconds(FlashGlare.AdaptedTo(0.0));
        double night = FlashGlare.RecoverySeconds(FlashGlare.AdaptedTo(-30.0));

        Assert.Equal(0.28, day, 6);
        Assert.True(day < dusk && dusk < night, $"{day} {dusk} {night}");
        Assert.True(night > 5.0 * day, $"night {night:F2} s against day {day:F2} s");
    }

    [Fact]
    public void TheFlashStartsVioletAndWarmsAsItCools()
    {
        Assert.Equal(1.0, FlashGlare.VioletShare(0.0), 9);
        Assert.True(FlashGlare.VioletShare(0.3) > 0.6, "violet still while the whiteout clears");
        Assert.True(FlashGlare.VioletShare(3.0) < 0.05, "warm by the time the ball is an ember");
        Assert.Equal(0.0, FlashGlare.VioletShare(double.NaN));

        for (double t = 0.0; t < 5.0; t += 0.1)
        {
            Assert.True(FlashGlare.VioletShare(t + 0.1) < FlashGlare.VioletShare(t));
        }
    }
}
