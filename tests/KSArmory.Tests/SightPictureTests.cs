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
    /// The whole point of the reference: <em>every</em> point sits on the horizontal plane through
    /// the eye, however far above or below it the head is looking.
    /// </summary>
    [Fact]
    public void EveryPointOfTheReferenceSitsAtZeroElevation()
    {
        double3 eye = new(0, 0, 0);
        Span<double3> arc = stackalloc double3[9];

        foreach (double3 forward in new[]
                 {
                     new double3(1, 0, 0),          // level
                     new double3(1, 0, 1),          // climbing 45
                     new double3(1, 0, -0.6),       // depressed
                     new double3(0.2, 0.9, 0.35),   // off both axes
                 })
        {
            int n = SightPicture.ReferenceArc(eye, forward, Up, 0.7, 30000.0, arc);
            Assert.Equal(arc.Length, n);

            for (int i = 0; i < n; i++)
            {
                Assert.Equal(0.0, SightPicture.ElevationRad(arc[i] - eye, Up), 9);
            }
        }
    }

    /// <summary>
    /// An arc, not a chord. Level places lie on a circle around the eye, so a straight line
    /// between two widely separated ones dips below level in the middle — by 3.4 km over a
    /// 40° half-span at 30 km, which draws a horizon sagging through the picture.
    /// </summary>
    [Fact]
    public void ThePointsFollowTheLevelCircleRatherThanCuttingAcrossIt()
    {
        Span<double3> arc = stackalloc double3[9];
        int n = SightPicture.ReferenceArc(Vec.Zero, new double3(1, 0, 0), Up, 0.7, 30000.0, arc);

        for (int i = 0; i < n; i++) Assert.Equal(30000.0, Vec.Len(arc[i]), 6);

        // What the two-point form would have drawn: the midpoint of the chord between the ends.
        double3 chordMiddle = (arc[0] + arc[^1]) * 0.5;

        Assert.True(30000.0 - Vec.Len(chordMiddle) > 3000.0,
            "the chord should sag kilometres below the circle, or this test proves nothing");
        Assert.True(Vec.Len(arc[n / 2]) - Vec.Len(chordMiddle) > 3000.0,
            "and the arc's own middle should not sag with it");
    }

    /// <summary>
    /// The points straddle where the head is looking rather than all landing on one side, which is
    /// what makes the drawn line cross the middle of the picture.
    /// </summary>
    [Fact]
    public void TheReferenceStraddlesTheLookDirection()
    {
        double3 eye = new(0, 0, 0);
        double3 forward = new(1, 0, 0.3);

        Span<double3> arc = stackalloc double3[9];
        int n = SightPicture.ReferenceArc(eye, forward, Up, 0.7, 30000.0, arc);
        Assert.Equal(9, n);

        // Flattened, the look direction lies between the two ends: each is the same angle off it
        // and they fall on opposite sides.
        double3 flat = Vec.Unit(new double3(forward.X, forward.Y, 0));
        double3 toLeft = Vec.Unit(arc[0] - eye);
        double3 toRight = Vec.Unit(arc[^1] - eye);

        Assert.Equal(Vec.AngleBetween(flat, toLeft), Vec.AngleBetween(flat, toRight), 9);

        // Opposite sides of it, asserted without naming which side is which: the caller draws a
        // line between the two and does not care, and pinning a handedness here would be pinning
        // the test's own convention rather than anything the sight depends on.
        double sideOfLeft = Vec.Dot(Vec.Cross(toLeft, flat), Up);
        double sideOfRight = Vec.Dot(Vec.Cross(toRight, flat), Up);

        Assert.True(Math.Abs(sideOfLeft) > 1e-6 && Math.Abs(sideOfRight) > 1e-6);
        Assert.True(sideOfLeft * sideOfRight < 0.0,
            $"both ends fell on one side of the look direction ({sideOfLeft:F6}, {sideOfRight:F6})");

        // In order across the picture, so consecutive points can be joined without sorting. Which
        // way round is the caller's business — only that it does not double back.
        double previous = Vec.Dot(Vec.Cross(Vec.Unit(arc[0] - eye), flat), Up);
        double second = Vec.Dot(Vec.Cross(Vec.Unit(arc[1] - eye), flat), Up);
        double sense = Math.Sign(second - previous);

        for (int i = 1; i < n; i++)
        {
            double here = Vec.Dot(Vec.Cross(Vec.Unit(arc[i] - eye), flat), Up);
            Assert.True(Math.Sign(here - previous) == sense,
                $"point {i} doubled back on point {i - 1}");
            previous = here;
        }
    }

    /// <summary>
    /// The span is the caller's, because it has to match the field of view: a fixed one puts both
    /// ends far outside a magnified picture, where they are behind the camera at any elevation and
    /// the reference vanishes exactly when it is needed.
    /// </summary>
    [Fact]
    public void TheSpanNarrowsWithTheFieldOfView()
    {
        Span<double3> wide = stackalloc double3[9];
        Span<double3> narrow = stackalloc double3[9];

        SightPicture.ReferenceArc(Vec.Zero, new double3(1, 0, 0), Up, 0.7, 30000.0, wide);
        SightPicture.ReferenceArc(Vec.Zero, new double3(1, 0, 0), Up, 0.03, 30000.0, narrow);

        double wideSpan = Vec.AngleBetween(wide[0], wide[^1]);
        double narrowSpan = Vec.AngleBetween(narrow[0], narrow[^1]);

        Assert.Equal(1.4, wideSpan, 6);
        Assert.Equal(0.06, narrowSpan, 6);
        Assert.True(narrowSpan < wideSpan);
    }

    /// <summary>
    /// Looking along the site's own vertical, the horizontal plane projects to a point and there
    /// is no line. Refused rather than answered with a degenerate set, which would draw a stroke
    /// across the picture in an arbitrary direction.
    /// </summary>
    [Fact]
    public void LookingStraightUpHasNoReferenceLine()
    {
        Span<double3> arc = stackalloc double3[9];

        Assert.Equal(0, SightPicture.ReferenceArc(Vec.Zero, Up, Up, 0.7, 30000.0, arc));
        Assert.Equal(0, SightPicture.ReferenceArc(Vec.Zero, new double3(0, 0, -1), Up, 0.7, 30000.0, arc));
    }

    [Fact]
    public void TheReferenceRefusesInputItCannotUse()
    {
        Span<double3> arc = stackalloc double3[9];

        Assert.Equal(0, SightPicture.ReferenceArc(Vec.Zero, new double3(1, 0, 0), Up, 0.7, 0.0, arc));
        Assert.Equal(0, SightPicture.ReferenceArc(Vec.Zero, Vec.Zero, Up, 0.7, 30000.0, arc));
        Assert.Equal(0, SightPicture.ReferenceArc(new double3(double.NaN, 0, 0), new double3(1, 0, 0),
                                                  Up, 0.7, 30000.0, arc));
        Assert.Equal(0, SightPicture.ReferenceArc(Vec.Zero, new double3(1, 0, 0), Up, 0.7, 30000.0,
                                                  stackalloc double3[1]));
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
