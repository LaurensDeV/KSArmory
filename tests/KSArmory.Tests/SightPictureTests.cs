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

    // One frame of correction at the controller's rate: 180 deg/s at 60 fps.
    private const double Step = Math.PI / 60.0;

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

    /// <summary>
    /// The fault this exists for, and the only one that matters: a view creeping past its own up
    /// must not have its roll jump. Anything that <em>switches rule</em> at the singularity flips
    /// the picture through half a turn, which in game is the whole view inverting as the head's
    /// elevation crosses zero looking straight down a rocket.
    /// </summary>
    [Fact]
    public void TheRollDoesNotJumpAsTheViewCreepsPastItsOwnUp()
    {
        double3 preferred = new(0, 0, 1);

        // Sweeping through straight down, a quarter of a degree at a time. Starting well outside
        // the cone on purpose: a head arrives at the pole from somewhere, and with no previous
        // answer at all there is nothing to be continuous with -- which the refusal test covers.
        double3 last = Vec.Zero;
        double3 previousUp = Vec.Zero;
        double worst = 0.0;
        double worstRatio = 0.0;

        for (double off = -60.0; off <= 60.0; off += 0.25)
        {
            double a = double.DegreesToRadians(off);
            double3 forward = Vec.Unit(new double3(Math.Sin(a), 0, -Math.Cos(a)));

            Assert.True(SightPicture.TryStableUp(forward, preferred, last, Step, out double3 up));

            if (Vec.Len2(previousUp) > 0.5)
            {
                double rolled = Vec.AngleBetween(previousUp, up);
                worst = Math.Max(worst, rolled);

                // The measured fault was amplification rather than a jump: a quarter degree of aim
                // swinging the roll by tens. Pinning the ratio is what stops a future threshold
                // being set back to where the answer exists but is worthless.
                worstRatio = Math.Max(worstRatio, rolled / double.DegreesToRadians(0.25));
            }

            previousUp = up;
            last = up;
        }

        Assert.True(worst < 0.05,
            $"the roll jumped {double.RadiansToDegrees(worst):F1} deg in one quarter-degree step");

        Assert.True(worstRatio < 2.5,
            $"a degree of aim moved the roll {worstRatio:F1} degrees at worst");
    }

    /// <summary>
    /// With nothing carried there is nothing to correct, so the wanted up is taken outright. That
    /// is what makes the first frame of a levelled view level rather than arbitrary.
    /// </summary>
    [Fact]
    public void WithNothingCarriedThePreferredUpIsTakenOutright()
    {
        double3 forward = new(1, 0, 0);
        double3 preferred = new(0, 0, 1);

        Assert.True(SightPicture.TryStableUp(forward, preferred, Vec.Zero, Step, out double3 up));

        Assert.Equal(0.0, Vec.AngleBetween(up, preferred), 9);
    }

    /// <summary>
    /// And a carried one is pulled towards it a frame at a time rather than snapping, which is
    /// what levelling *is*: a control loop, not a lookup.
    /// </summary>
    [Fact]
    public void ACarriedUpIsCorrectedTowardsThePreferredOverSeveralFrames()
    {
        double3 forward = new(1, 0, 0);
        double3 preferred = new(0, 0, 1);
        double3 up = new(0, 1, 0);          // a quarter turn out

        Assert.True(SightPicture.TryStableUp(forward, preferred, up, Step, out double3 after));

        double moved = Vec.AngleBetween(up, after);
        Assert.True(moved > 0.0 && moved <= Step + 1e-9,
            $"one frame moved the roll {double.RadiansToDegrees(moved):F2} deg");

        // And it gets there, rather than creeping forever.
        for (int frame = 0; frame < 200; frame++)
        {
            SightPicture.TryStableUp(forward, preferred, up, Step, out up);
        }

        Assert.Equal(0.0, Vec.AngleBetween(up, preferred), 6);
    }

    [Fact]
    public void TheAnswerIsAlwaysOrthogonalToTheView()
    {
        double3 forward = Vec.Unit(new double3(1, 0.4, -0.2));

        Assert.True(SightPicture.TryStableUp(forward, new double3(0, 0, 1), Vec.Zero, Step, out double3 up));

        Assert.Equal(0.0, Vec.Dot(up, forward), 9);
        Assert.Equal(1.0, Vec.Len(up), 9);
    }

    /// <summary>
    /// Both unusable is a view along its own up on the very frame it was taken: there is nothing
    /// continuous to be had, because there is nothing to be continuous with.
    /// </summary>
    [Fact]
    public void WithNothingUsableItRefusesRatherThanInventingARoll()
    {
        double3 forward = new(0, 0, 1);

        Assert.False(SightPicture.TryStableUp(forward, forward, Vec.Zero, Step, out _));
        Assert.False(SightPicture.TryStableUp(Vec.Zero, new double3(0, 0, 1), Vec.Zero, Step, out _));
    }

    /// <summary>
    /// The invariant that forbids the whole class, rather than one geometry of it: the answer can
    /// never move further in a frame than it was allowed to.
    ///
    /// <para>Three versions of this flipped the picture, each at a different view angle, because
    /// each chose between two answers and the choice could change from one frame to the next. A
    /// bound on the movement itself cannot be satisfied by a rule that switches — which is the
    /// point of asserting it here rather than asserting the absence of the three faults.</para>
    /// </summary>
    [Fact]
    public void TheAnswerNeverMovesFurtherInOneFrameThanItWasAllowed()
    {
        double3 preferred = new(0, 0, 1);
        double worst = 0.0;

        // Every view angle from along the up to across it, and every carried roll around each,
        // including the ones that sat exactly on the thresholds the earlier versions used.
        for (double off = 0.0; off <= 180.0; off += 1.0)
        {
            double a = double.DegreesToRadians(off);
            double3 forward = Vec.Unit(new double3(Math.Sin(a), 0, Math.Cos(a)));

            for (double roll = 0.0; roll < 360.0; roll += 15.0)
            {
                double r = double.DegreesToRadians(roll);

                // A carried up at that roll about the view, which is the shape one always has.
                if (!SightPicture.TryStableUp(forward, preferred, Vec.Zero, Step, out double3 seed))
                {
                    continue;
                }

                double3 carried = Vec.Unit(doubleQuat.CreateFromAxisAngle(forward, r) * seed);

                if (!SightPicture.TryStableUp(forward, preferred, carried, Step, out double3 up)) continue;

                worst = Math.Max(worst, Vec.AngleBetween(carried, up));
            }
        }

        Assert.True(worst <= Step + 1e-9,
            $"the roll moved {double.RadiansToDegrees(worst):F2} deg against an allowance of "
            + $"{double.RadiansToDegrees(Step):F2}");
    }
}
