using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The sight's world-space geometry: where the horizontal reference lies and how far the head is
/// looking above it.
///
/// <para>The reference line is two <em>points</em> rather than a screen-space line, and the test
/// that matters is that both sit at the same elevation whatever the head is doing. A line laid
/// flat across the screen passes any test taken with the camera level, which is the one pose it
/// happens to be right at.</para>
/// </summary>
public class SightPictureTests
{
    private static readonly double3 Up = new(0, 0, 1);

    [Fact]
    public void ElevationIsZeroAlongTheHorizontalAndAQuarterTurnStraightUp()
    {
        Assert.Equal(0.0, SightPicture.ElevationRad(new double3(1, 0, 0), Up), 9);
        Assert.Equal(Math.PI / 2, SightPicture.ElevationRad(Up, Up), 9);
        Assert.Equal(-Math.PI / 2, SightPicture.ElevationRad(new double3(0, 0, -1), Up), 9);
    }

    [Fact]
    public void ElevationIsSignedAndIndependentOfHowLongTheVectorsAre()
    {
        double3 climbing = new(1, 0, 1);

        Assert.Equal(Math.PI / 4, SightPicture.ElevationRad(climbing, Up), 9);
        Assert.Equal(Math.PI / 4, SightPicture.ElevationRad(climbing * 1e6, Up * 7.0), 9);
        Assert.Equal(-Math.PI / 4, SightPicture.ElevationRad(new double3(1, 0, -1), Up), 9);
    }

    /// <summary>
    /// The whole point of the reference: both ends sit on the horizontal plane through the eye,
    /// however far above or below it the head is looking.
    /// </summary>
    [Fact]
    public void BothEndsOfTheReferenceSitAtZeroElevation()
    {
        double3 eye = new(0, 0, 0);

        foreach (double3 forward in new[]
                 {
                     new double3(1, 0, 0),          // level
                     new double3(1, 0, 1),          // climbing 45
                     new double3(1, 0, -0.6),       // depressed
                     new double3(0.2, 0.9, 0.35),   // off both axes
                 })
        {
            Assert.True(SightPicture.TryReferenceLine(eye, forward, Up, 0.7, 30000.0,
                                                      out double3 left, out double3 right));

            Assert.Equal(0.0, SightPicture.ElevationRad(left - eye, Up), 9);
            Assert.Equal(0.0, SightPicture.ElevationRad(right - eye, Up), 9);
        }
    }

    /// <summary>
    /// The two ends straddle where the head is looking rather than both landing on one side,
    /// which is what makes the drawn line cross the middle of the picture.
    /// </summary>
    [Fact]
    public void TheReferenceStraddlesTheLookDirection()
    {
        double3 eye = new(0, 0, 0);
        double3 forward = new(1, 0, 0.3);

        Assert.True(SightPicture.TryReferenceLine(eye, forward, Up, 0.7, 30000.0,
                                                  out double3 left, out double3 right));

        // Flattened, the look direction lies between the two ends: each is the same angle off it
        // and they fall on opposite sides.
        double3 flat = Vec.Unit(new double3(forward.X, forward.Y, 0));
        double3 toLeft = Vec.Unit(left - eye);
        double3 toRight = Vec.Unit(right - eye);

        Assert.Equal(Vec.AngleBetween(flat, toLeft), Vec.AngleBetween(flat, toRight), 9);

        // Opposite sides of it, asserted without naming which side is which: the caller draws a
        // line between the two and does not care, and pinning a handedness here would be pinning
        // the test's own convention rather than anything the sight depends on.
        double sideOfLeft = Vec.Dot(Vec.Cross(toLeft, flat), Up);
        double sideOfRight = Vec.Dot(Vec.Cross(toRight, flat), Up);

        Assert.True(Math.Abs(sideOfLeft) > 1e-6 && Math.Abs(sideOfRight) > 1e-6);
        Assert.True(sideOfLeft * sideOfRight < 0.0,
            $"both ends fell on one side of the look direction ({sideOfLeft:F6}, {sideOfRight:F6})");
    }

    /// <summary>
    /// Looking along the site's own vertical, the horizontal plane projects to a point and there
    /// is no line. Refused rather than answered with a degenerate pair, which would draw a stroke
    /// across the picture in an arbitrary direction.
    /// </summary>
    [Fact]
    public void LookingStraightUpHasNoReferenceLine()
    {
        Assert.False(SightPicture.TryReferenceLine(Vec.Zero, Up, Up, 0.7, 30000.0, out _, out _));
        Assert.False(SightPicture.TryReferenceLine(Vec.Zero, new double3(0, 0, -1), Up, 0.7, 30000.0,
                                                   out _, out _));
    }

    [Fact]
    public void TheReferenceRefusesInputItCannotUse()
    {
        Assert.False(SightPicture.TryReferenceLine(Vec.Zero, new double3(1, 0, 0), Up, 0.7, 0.0,
                                                   out _, out _));
        Assert.False(SightPicture.TryReferenceLine(Vec.Zero, Vec.Zero, Up, 0.7, 30000.0, out _, out _));
        Assert.False(SightPicture.TryReferenceLine(new double3(double.NaN, 0, 0), new double3(1, 0, 0),
                                                   Up, 0.7, 30000.0, out _, out _));
    }

    [Fact]
    public void PointingIsAUnitVectorFromOnePlaceToAnother()
    {
        Assert.True(SightPicture.TryPointing(new float2(100f, 100f), new float2(100f, 40f),
                                             out float2 towards));

        Assert.Equal(0f, towards.X, 5);
        Assert.Equal(-1f, towards.Y, 5);
    }

    [Fact]
    public void PointingRefusesWhenThereIsNoDirectionToGive()
    {
        Assert.False(SightPicture.TryPointing(new float2(10f, 10f), new float2(10f, 10f), out _));
        Assert.False(SightPicture.TryPointing(new float2(float.NaN, 0f), new float2(10f, 10f), out _));
    }
}
