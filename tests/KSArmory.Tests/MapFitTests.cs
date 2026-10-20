using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Fitting an equirectangular picture into whatever shape a window has been dragged to.
///
/// <para>The ratio is not a preference. The crop a map shows is one over the zoom across and the
/// same down — a square in UV, two-to-one on the ground — so a picture drawn into any other shape
/// is stretched, and stretched by an amount that changes as the window is resized.</para>
/// </summary>
public class MapFitTests
{
    private const float Aspect = 0.5f;
    private const float Least = 80f;

    [Fact]
    public void AWideShortBoxIsLimitedByItsHeight()
    {
        // The reported fault: filling the width here squashes a 2:1 picture into 6:1.
        float2 got = MapViewport.Fit(new float2(1200f, 200f), Aspect, Least, out float inset);

        Assert.Equal(400f, got.X, 3);
        Assert.Equal(200f, got.Y, 3);
        Assert.Equal(400f, inset, 3);
    }

    [Fact]
    public void ATallNarrowBoxIsLimitedByItsWidth()
    {
        float2 got = MapViewport.Fit(new float2(600f, 900f), Aspect, Least, out float inset);

        Assert.Equal(600f, got.X, 3);
        Assert.Equal(300f, got.Y, 3);
        Assert.Equal(0f, inset, 3);
    }

    [Fact]
    public void AnExactlyShapedBoxIsFilled()
    {
        float2 got = MapViewport.Fit(new float2(800f, 400f), Aspect, Least, out float inset);

        Assert.Equal(800f, got.X, 3);
        Assert.Equal(400f, got.Y, 3);
        Assert.Equal(0f, inset, 3);
    }

    [Theory]
    [InlineData(1200f, 200f)]
    [InlineData(600f, 900f)]
    [InlineData(800f, 400f)]
    [InlineData(37f, 13f)]
    public void TheShapeIsAlwaysTheMapsShape(float wide, float tall)
    {
        float2 got = MapViewport.Fit(new float2(wide, tall), Aspect, Least, out _);

        Assert.Equal(Aspect, got.Y / got.X, 4);
    }

    [Fact]
    public void AWindowDraggedShutStillGivesARectangle()
    {
        float2 got = MapViewport.Fit(new float2(0f, -40f), Aspect, Least, out _);

        Assert.True(got.X >= Least);
        Assert.True(got.Y > 0f);
    }
}
