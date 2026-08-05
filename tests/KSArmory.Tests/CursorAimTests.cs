using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Turning a cursor into an aim direction.
///
/// <para>The one interesting case is the coordinate space. ImGui reports the cursor across every
/// window; a camera's unprojection divides by <em>its own</em> framebuffer. On a single
/// full-screen viewport those are the same numbers, so the mistake is invisible until a second
/// view is open — and then reads as the gun aiming at a fixed offset from the cursor rather than
/// as an error.</para>
/// </summary>
public class CursorAimTests
{
    [Fact]
    public void ACursorOverAFullScreenViewportNeedsNoAdjustment()
    {
        Assert.True(CursorAim.TryToViewport(new float2(640, 360), new float2(0, 0),
                                            1280, 720, out float2 local));

        Assert.Equal(640f, local.X);
        Assert.Equal(360f, local.Y);
    }

    /// <summary>
    /// The case a single-viewport test cannot see: the same cursor over an offset viewport is a
    /// different point in that viewport's own coordinates.
    /// </summary>
    [Fact]
    public void ACursorOverAnOffsetViewportIsMeasuredFromItsOwnCorner()
    {
        Assert.True(CursorAim.TryToViewport(new float2(900, 500), new float2(800, 400),
                                            640, 360, out float2 local));

        Assert.Equal(100f, local.X);
        Assert.Equal(100f, local.Y);
    }

    [Theory]
    [InlineData(700, 500)]    // left of it
    [InlineData(900, 300)]    // above it
    [InlineData(1500, 500)]   // right of it
    [InlineData(900, 800)]    // below it
    public void ACursorOutsideTheViewportIsRejected(float x, float y)
        => Assert.False(CursorAim.TryToViewport(new float2(x, y), new float2(800, 400),
                                                640, 360, out _));

    /// <summary>
    /// The far edge is exclusive: a cursor at exactly the width unprojects to NDC +1, which is
    /// the frustum boundary rather than a point inside it.
    /// </summary>
    [Fact]
    public void TheFarEdgeIsOutside()
    {
        Assert.False(CursorAim.TryToViewport(new float2(1440, 500), new float2(800, 400),
                                             640, 360, out _));
        Assert.True(CursorAim.TryToViewport(new float2(1439, 500), new float2(800, 400),
                                            640, 360, out _));
    }

    [Fact]
    public void TheNearEdgeIsInside()
        => Assert.True(CursorAim.TryToViewport(new float2(800, 400), new float2(800, 400),
                                               640, 360, out _));

    [Theory]
    [InlineData(0, 360)]
    [InlineData(640, 0)]
    [InlineData(-1, 360)]
    public void AViewportWithNoAreaHasNothingToAimInto(int width, int height)
        => Assert.False(CursorAim.TryToViewport(new float2(10, 10), new float2(0, 0),
                                                width, height, out _));

    [Fact]
    public void ANonFiniteCursorIsRejected()
    {
        Assert.False(CursorAim.TryToViewport(new float2(float.NaN, 10), new float2(0, 0),
                                             1280, 720, out _));
        Assert.False(CursorAim.TryToViewport(new float2(10, float.PositiveInfinity), new float2(0, 0),
                                             1280, 720, out _));
    }

    /// <summary>
    /// A degenerate projection unprojects to zero or NaN, and normalising that reaches the drive
    /// and then the round. NaN times zero is still NaN, so nothing downstream recovers from it.
    /// </summary>
    [Theory]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(double.NaN, 0.0, 1.0)]
    [InlineData(0.0, double.PositiveInfinity, 1.0)]
    public void AnUnusableDirectionIsRejected(double x, double y, double z)
        => Assert.False(CursorAim.IsUsableDirection(new double3(x, y, z)));

    [Fact]
    public void AUsableDirectionIsAccepted()
        => Assert.True(CursorAim.IsUsableDirection(new double3(0, 0, 1)));

    /// <summary>
    /// The camera unprojects against its framebuffer, which a render or display scale makes a
    /// different size from the window. Getting only the viewport offset right leaves an error
    /// that is zero at the top-left and grows across the screen — "close, but not under the
    /// pointer", which is exactly how it looked.
    /// </summary>
    [Fact]
    public void TheCursorIsScaledIntoFramebufferPixels()
    {
        // A 800x600 viewport rendered at 1600x1200: the centre must stay the centre, and the
        // far corner must reach the far corner of the framebuffer rather than its midpoint.
        Assert.True(CursorAim.TryToFramebuffer(new float2(400, 300), new float2(0, 0),
                                               800, 600, 1600, 1200, out float2 centre));
        Assert.Equal(800f, centre.X, 3);
        Assert.Equal(600f, centre.Y, 3);

        Assert.True(CursorAim.TryToFramebuffer(new float2(799, 599), new float2(0, 0),
                                               800, 600, 1600, 1200, out float2 corner));
        Assert.Equal(1598f, corner.X, 3);
        Assert.Equal(1198f, corner.Y, 3);
    }

    /// <summary>The offset still has to happen; the scale is on top of it, not instead.</summary>
    [Fact]
    public void TheViewportOffsetStillApplies()
    {
        Assert.True(CursorAim.TryToFramebuffer(new float2(1000, 400), new float2(600, 100),
                                               800, 600, 800, 600, out float2 local));
        Assert.Equal(400f, local.X, 3);
        Assert.Equal(300f, local.Y, 3);
    }

    [Fact]
    public void ADegenerateFramebufferIsRefused()
    {
        Assert.False(CursorAim.TryToFramebuffer(new float2(10, 10), new float2(0, 0),
                                                800, 600, 0, 600, out _));
        Assert.False(CursorAim.TryToFramebuffer(new float2(10, 10), new float2(0, 0),
                                                800, 600, 800, -1, out _));

        // Outside the viewport is still outside it, whatever the framebuffer is.
        Assert.False(CursorAim.TryToFramebuffer(new float2(900, 10), new float2(0, 0),
                                                800, 600, 1600, 1200, out _));
    }
}
