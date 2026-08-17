using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The sight's layout. Worth pinning because it is drawn over a camera view and cannot be
/// inspected anywhere else: a stroke off by a sign lands on the far side of the target, and a
/// box that never closes reads as a sight that never settles.
/// </summary>
public class ReticleTests
{
    private static readonly float2 Centre = new(400, 300);

    private static ReticleStroke[] Build(float half, bool settled, out int count)
    {
        var strokes = new ReticleStroke[Reticle.MaxStrokes];
        count = Reticle.Build(Centre, half, settled, strokes);
        return strokes;
    }

    /// <summary>The middle must stay clear, or the sight hides the thing it is pointing at.</summary>
    [Fact]
    public void NothingIsDrawnThroughTheCentre()
    {
        foreach (bool settled in new[] { true, false })
        {
            ReticleStroke[] strokes = Build(40f, settled, out int count);

            for (int i = 0; i < count; i++)
            {
                Assert.True(DistanceToSegment(Centre, strokes[i].A, strokes[i].B) > 4f,
                            $"stroke {i} crosses the aim point when settled={settled}");
            }
        }
    }

    [Fact]
    public void TheBracketsCloseInWhenTheHeadSettles()
    {
        ReticleStroke[] slewing = Build(40f, settled: false, out int slewCount);
        ReticleStroke[] settled = Build(40f, settled: true, out int settledCount);

        Assert.True(Spread(slewing, slewCount) > Spread(settled, settledCount),
                    "a settled sight must draw tighter than one still slewing");
    }

    [Fact]
    public void TheRangingLadderOnlyAppearsOnceSettled()
    {
        Build(40f, settled: false, out int slewing);
        Build(40f, settled: true, out int settled);

        Assert.True(settled > slewing, "the ladder is the difference between the two states");
    }

    [Fact]
    public void EveryStrokeStaysWithinTheBufferItWasGiven()
    {
        ReticleStroke[] strokes = Build(40f, settled: true, out int count);

        Assert.True(count <= Reticle.MaxStrokes);
        Assert.True(count > 0);
        for (int i = 0; i < count; i++)
        {
            Assert.True(float.IsFinite(strokes[i].A.X) && float.IsFinite(strokes[i].B.Y));
        }
    }

    [Fact]
    public void JunkGeometryDrawsNothingRatherThanScatteringStrokes()
    {
        var strokes = new ReticleStroke[Reticle.MaxStrokes];

        Assert.Equal(0, Reticle.Build(Centre, 0f, true, strokes));
        Assert.Equal(0, Reticle.Build(Centre, -5f, true, strokes));
        Assert.Equal(0, Reticle.Build(new float2(float.NaN, 0), 40f, true, strokes));
        Assert.Equal(0, Reticle.Build(Centre, 40f, true, new ReticleStroke[4]));
    }

    /// <summary>
    /// The sight's mark is an icon, so it stays the size it is however close the target gets. A
    /// box sized to the target's apparent width grows without bound on a boresighted view, where
    /// the target is always centred and always closing, and ends up covering the picture it is
    /// drawn on.
    /// </summary>
    [Fact]
    public void TheSightsMarkIsAFixedIconRatherThanABoundingBox()
    {
        // Small enough that the cross and the ladder drop out, leaving the eight corner strokes
        // that make it read as the same mark the on-screen system brackets use.
        Assert.True(Reticle.IconHalfSize < Reticle.CrossBelow);

        int count = Reticle.Build(Centre, Reticle.IconHalfSize, settled: true,
                                  new ReticleStroke[Reticle.MaxStrokes]);
        Assert.Equal(8, count);
    }

    private static float Spread(ReticleStroke[] strokes, int count)
    {
        float worst = 0f;
        for (int i = 0; i < count; i++)
        {
            worst = Math.Max(worst, Math.Abs(strokes[i].A.X - Centre.X));
            worst = Math.Max(worst, Math.Abs(strokes[i].B.X - Centre.X));
        }
        return worst;
    }

    private static float DistanceToSegment(float2 p, float2 a, float2 b)
    {
        float2 ab = new(b.X - a.X, b.Y - a.Y);
        float lengthSquared = ab.X * ab.X + ab.Y * ab.Y;
        if (lengthSquared < 1e-9f) return Distance(p, a);

        float t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lengthSquared, 0f, 1f);
        return Distance(p, new float2(a.X + ab.X * t, a.Y + ab.Y * t));
    }

    private static float Distance(float2 p, float2 q)
        => MathF.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));

    [Fact]
    public void ASmallBoxIsCornersOnly()
    {
        // Every part of the sight is a fraction of the box, so at the floor the cross ends about a
        // pixel short of the brackets -- less than the stroke width -- and the whole thing merges
        // into a blob that reads as a rendering fault.
        Span<ReticleStroke> strokes = new ReticleStroke[Reticle.MaxStrokes];

        int count = Reticle.Build(new float2(100f, 100f), Reticle.MinBoxHalfSize, settled: true,
                                  strokes);

        Assert.Equal(8, count);
    }

    [Fact]
    public void ALargeBoxKeepsTheCross()
    {
        Span<ReticleStroke> strokes = new ReticleStroke[Reticle.MaxStrokes];

        int count = Reticle.Build(new float2(100f, 100f), 120f, settled: true, strokes);

        Assert.True(count > 8, $"the cross went missing at a usable size: {count} strokes");
    }

    [Fact]
    public void NothingIsDrawnOutsideTheBox()
    {
        // Corner arms run inward and cross ticks stop short, so every stroke stays within the box
        // it was asked for. A stroke outside it would sit over the target rather than around it.
        Span<ReticleStroke> strokes = new ReticleStroke[Reticle.MaxStrokes];
        const float half = 120f;
        var centre = new float2(500f, 400f);

        int count = Reticle.Build(centre, half, settled: true, strokes, ladder: false);

        for (int i = 0; i < count; i++)
        {
            foreach (float2 p in new[] { strokes[i].A, strokes[i].B })
            {
                Assert.True(Math.Abs(p.X - centre.X) <= half + 0.01f, $"stroke {i} escaped in X");
                Assert.True(Math.Abs(p.Y - centre.Y) <= half + 0.01f, $"stroke {i} escaped in Y");
            }
        }
    }

    /// <summary>
    /// A closing speed that rounds to nothing prints as nothing.
    ///
    /// <para>A hair either side of zero formats as "-0" and "0", and a target holding station
    /// crosses that boundary every frame — so the only thing moving on the glass is a minus sign
    /// appearing and disappearing.</para>
    /// </summary>
    [Theory]
    [InlineData(-0.4, "0 m/s")]
    [InlineData(-0.0, "0 m/s")]
    [InlineData(0.4, "0 m/s")]
    [InlineData(0.0, "0 m/s")]
    public void AClosingSpeedThatRoundsToNothingHasNoSign(double closing, string expected)
    {
        Assert.EndsWith(expected, Reticle.RangeAndClosing(800.0, closing), StringComparison.Ordinal);
    }

    /// <summary>A real closing speed keeps its sign, which is what says opening from closing.</summary>
    [Theory]
    [InlineData(-140.0, "-140 m/s")]
    [InlineData(140.0, "140 m/s")]
    public void ARealClosingSpeedKeepsItsSign(double closing, string expected)
    {
        Assert.EndsWith(expected, Reticle.RangeAndClosing(800.0, closing), StringComparison.Ordinal);
    }

    [Fact]
    public void RangeIsInKilometresToTwoPlaces()
    {
        Assert.StartsWith("0.80 km", Reticle.RangeAndClosing(800.0, 0.0), StringComparison.Ordinal);
    }
}
