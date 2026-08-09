using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The sight's optics. The one thing worth pinning is that magnification is not a ratio of
/// angles: halving the field does not double what it magnifies, and the gap widens without bound
/// as the field narrows. A linear rule would label the narrowest stop 16× when it is 20.7×.
/// </summary>
public class SightZoomTests
{
    [Fact]
    public void UnityMagnification_LeavesTheFieldAlone()
    {
        Assert.Equal(50.0, SightZoom.FovDegreesFor(50.0, 1.0), 6);
        Assert.Equal(90.0, SightZoom.FovDegreesFor(90.0, 1.0), 6);
    }

    /// <summary>
    /// The relation is <c>tan(fov/2) = tan(base/2) / m</c>, so the tangent halves and the angle
    /// does not. A sight built on angles would ask for 25° here and be showing 1.94×.
    /// </summary>
    [Fact]
    public void MagnificationIsOptical_NotAHalvingOfTheAngle()
    {
        double fov = SightZoom.FovDegreesFor(50.0, 2.0);

        Assert.True(fov > 25.0, $"2x of 50 deg came out at {fov:F3} deg, which is the linear answer");
        Assert.Equal(26.25, fov, 2);
    }

    [Fact]
    public void MagnificationRoundTripsThroughTheFieldItAsksFor()
    {
        foreach (double magnification in new[] { 1.0, 2.0, 4.0, 8.0, 16.0 })
        {
            double fov = SightZoom.FovDegreesFor(50.0, magnification);

            Assert.Equal(magnification, SightZoom.MagnificationFor(50.0, fov), 3);
        }
    }

    /// <summary>
    /// The engine throws rather than clamping — a field of zero or more than half a turn raises
    /// out of the frame hook — so the clamp here is a crash guard and not a preference.
    /// </summary>
    [Fact]
    public void TheFieldStaysInsideWhatTheEngineWillAccept()
    {
        foreach (double magnification in new[] { -5.0, 0.0, 1e9, double.NaN, double.PositiveInfinity })
        {
            double fov = SightZoom.FovDegreesFor(50.0, magnification);

            Assert.InRange(fov, SightZoom.MinFovDeg, SightZoom.MaxFovDeg);
        }
    }

    [Fact]
    public void AnUnreadableBaseFieldFallsBackRatherThanPropagating()
    {
        foreach (double baseFov in new[] { 0.0, -10.0, 180.0, 400.0, double.NaN })
        {
            Assert.Equal(SightZoom.DefaultFovDeg, SightZoom.FovDegreesFor(baseFov, 1.0), 6);
        }
    }

    [Fact]
    public void SteppingWalksTheDetentsAndStopsAtBothEnds()
    {
        Assert.Equal(2f, SightZoom.Stepped(1f, 1));
        Assert.Equal(4f, SightZoom.Stepped(2f, 1));
        Assert.Equal(1f, SightZoom.Stepped(1f, -1));
        Assert.Equal(16f, SightZoom.Stepped(16f, 1));
        Assert.Equal(8f, SightZoom.Stepped(16f, -1));
    }

    /// <summary>
    /// A value between two detents — restored from a save, or left over from an older table —
    /// steps to the neighbour on the side it is going, rather than sticking on the nearest.
    /// </summary>
    [Fact]
    public void AValueOffTheDetentsStillStepsBothWays()
    {
        Assert.Equal(4f, SightZoom.Stepped(3f, 1));
        Assert.Equal(2f, SightZoom.Stepped(3f, -1));
    }

    [Fact]
    public void ApparentSizeGrowsAsTheFieldNarrowsAndAsTheTargetCloses()
    {
        float wide = SightZoom.ApparentPixels(20.0, 4000.0, 50.0, 1080f);
        float narrow = SightZoom.ApparentPixels(20.0, 4000.0, 3.0, 1080f);
        float closer = SightZoom.ApparentPixels(20.0, 2000.0, 50.0, 1080f);

        Assert.True(narrow > wide * 10.0, $"narrowing the field to 3 deg gave {narrow:F1} px against {wide:F1}");
        Assert.True(closer > wide * 1.9, $"halving the range gave {closer:F1} px against {wide:F1}");
    }

    [Fact]
    public void ApparentSizeIsZeroWhereItCannotBeSized()
    {
        Assert.Equal(0f, SightZoom.ApparentPixels(0.0, 4000.0, 50.0, 1080f));
        Assert.Equal(0f, SightZoom.ApparentPixels(20.0, 0.0, 50.0, 1080f));
        Assert.Equal(0f, SightZoom.ApparentPixels(20.0, 4000.0, 50.0, 0f));
        Assert.Equal(0f, SightZoom.ApparentPixels(double.NaN, 4000.0, 50.0, 1080f));
    }

    [Fact]
    public void TheFieldIsWiderAtRangeThanNear()
    {
        Assert.Equal(2.0 * 1000.0 * Math.Tan(double.DegreesToRadians(25.0)),
                     SightZoom.MetresAcrossAt(50.0, 1000.0), 6);
        Assert.Equal(0.0, SightZoom.MetresAcrossAt(50.0, -1.0));
    }
}
