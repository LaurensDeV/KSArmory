using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The crop arithmetic the ground-track map is drawn against.
///
/// <para>This is KSA's own formula restated, because reading it back from the engine would mean
/// calling a method that consumes a drag and a wheel notch. So what these tests are really for is
/// catching the day KSA changes it: a mark placed with a stale crop sits somewhere plausible and
/// wrong, which is the failure a screenshot does not show.</para>
/// </summary>
public class MapViewportTests
{
    private static readonly float2 Area = new(800f, 400f);

    [Fact]
    public void UnzoomedShowsTheWholeBody()
    {
        MapViewport.FromPanZoom(Area, float2.Zero, 1f, out float2 min, out float2 max);

        Assert.Equal(0f, min.X, 5);
        Assert.Equal(0f, min.Y, 5);
        Assert.Equal(1f, max.X, 5);
        Assert.Equal(1f, max.Y, 5);
    }

    [Fact]
    public void ZoomingWithoutPanningKeepsTheTopLeftCorner()
    {
        MapViewport.FromPanZoom(Area, float2.Zero, 2f, out float2 min, out float2 max);

        Assert.Equal(0f, min.X, 5);
        Assert.Equal(0f, min.Y, 5);
        Assert.Equal(0.5f, max.X, 5);
        Assert.Equal(0.5f, max.Y, 5);
    }

    [Fact]
    public void PanningToTheMiddleCentresTheCrop()
    {
        // Pan runs from area-scaled to zero, so half of that is the centred view.
        float2 pan = new(-Area.X * 0.5f, -Area.Y * 0.5f);

        MapViewport.FromPanZoom(Area, pan, 2f, out float2 min, out float2 max);

        Assert.Equal(0.25f, min.X, 5);
        Assert.Equal(0.25f, min.Y, 5);
        Assert.Equal(0.75f, max.X, 5);
        Assert.Equal(0.75f, max.Y, 5);
    }

    [Fact]
    public void ADegenerateZoomShowsEverythingRatherThanNothing()
    {
        MapViewport.FromPanZoom(Area, float2.Zero, 0f, out float2 min, out float2 max);

        Assert.Equal(float2.Zero, min);
        Assert.Equal(float2.One, max);
    }

    [Fact]
    public void AZeroSizedAreaShowsEverythingRatherThanNothing()
    {
        MapViewport.FromPanZoom(float2.Zero, float2.Zero, 1f, out float2 min, out float2 max);

        Assert.Equal(float2.Zero, min);
        Assert.Equal(float2.One, max);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.5f, 0.5f)]
    [InlineData(-180.0, 90.0, 0.0f, 0.0f)]
    [InlineData(180.0, -90.0, 0.0f, 1.0f)]
    [InlineData(-90.0, 45.0, 0.25f, 0.25f)]
    [InlineData(90.0, -45.0, 0.75f, 0.75f)]
    public void LatitudeAndLongitudeLandWhereTheGridSaysTheyShould(
        double lon, double lat, float u, float v)
    {
        float2 uv = MapViewport.ToUv(lon, lat);

        Assert.Equal(u, uv.X, 5);
        Assert.Equal(v, uv.Y, 5);
    }

    [Fact]
    public void ALongitudePastTheDatelineWrapsRatherThanRunningOffTheMap()
    {
        Assert.Equal(MapViewport.ToUv(-170.0, 0.0).X, MapViewport.ToUv(190.0, 0.0).X, 5);
        Assert.Equal(MapViewport.ToUv(170.0, 0.0).X, MapViewport.ToUv(-190.0, 0.0).X, 5);
    }

    [Fact]
    public void TheDatelineIsTheLeftEdge()
    {
        // KSA's wrap brings +180 round to -180, so a mark exactly on the dateline draws at the
        // left edge and one a fraction west of it at the right. Anything drawn as a line across
        // that meridian -- a ground track, a reach ellipse -- jumps the full width of the map and
        // has to be split rather than drawn through.
        Assert.Equal(0f, MapViewport.ToUv(180.0, 0.0).X, 5);
        Assert.Equal(0f, MapViewport.ToUv(-180.0, 0.0).X, 5);
        Assert.True(MapViewport.ToUv(179.9, 0.0).X > 0.999f);
    }

    [Fact]
    public void ALatitudePastAPoleIsHeldAtItRatherThanWrappingOverTheTop()
    {
        // Wrapping would put it on the opposite meridian, half a world from where it was asked for.
        Assert.Equal(MapViewport.ToUv(0.0, 90.0), MapViewport.ToUv(0.0, 120.0));
        Assert.Equal(MapViewport.ToUv(0.0, -90.0), MapViewport.ToUv(0.0, -120.0));
    }

    [Fact]
    public void TwoCropsThatDifferByLessThanAPixelAreTheSame()
    {
        // What the live check against KSA's own crop is allowed to shrug off. A ten-thousandth of
        // UV is a fifth of a pixel on the widest texture the exporter renders.
        float2 min = new(0.25f, 0.25f);
        float2 max = new(0.75f, 0.75f);

        Assert.True(MapViewport.Same(min, max, min, max));
        Assert.True(MapViewport.Same(min, max, new float2(0.25005f, 0.25f), max));
        Assert.False(MapViewport.Same(min, max, new float2(0.26f, 0.25f), max));
        Assert.False(MapViewport.Same(min, max, min, new float2(0.75f, 0.74f)));
    }

    [Fact]
    public void TheMiddleOfAnUncroppedMapIsNullIsland()
    {
        Assert.True(MapViewport.TryPlace(float2.Zero, new float2(360f, 180f), float2.Zero,
                                         float2.One, new float2(180f, 90f),
                                         out double lon, out double lat));

        Assert.Equal(0.0, lon, 6);
        Assert.Equal(0.0, lat, 6);
    }

    [Fact]
    public void TheCornersOfAnUncroppedMapAreTheCornersOfTheWorld()
    {
        float2 size = new(360f, 180f);
        float2 origin = new(40f, 25f);

        Assert.True(MapViewport.TryPlace(origin, size, float2.Zero, float2.One, origin,
                                         out double lon, out double lat));
        Assert.Equal(-180.0, lon, 6);
        Assert.Equal(90.0, lat, 6);

        Assert.True(MapViewport.TryPlace(origin, size, float2.Zero, float2.One, origin + size,
                                         out lon, out lat));
        Assert.Equal(180.0, lon, 6);
        Assert.Equal(-90.0, lat, 6);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(-62.0, -24.0)]
    [InlineData(151.2, -33.9)]
    [InlineData(-90.0, 45.0)]
    public void APlaceSurvivesBeingDrawnAndThenClicked(double lon, double lat)
    {
        // The round trip a targeting map is: a place projected onto the picture, and the pixel it
        // landed on read back. It has to come home to the metre, because what comes back is a
        // designation -- see MapViewport.TryPlace on why this is all in double.
        RoundTrip(lon, lat, float2.Zero, float2.One);
    }

    [Fact]
    public void APlaceSurvivesTheRoundTripZoomedIn()
    {
        // Zoom 1 cannot fail this: the crop is 0..1 and the lerp is the identity. What this adds
        // is a crop that is neither, around a place inside it.
        MapViewport.FromPanZoom(Size, new float2(-1500f, -700f), 2.5f,
                                out float2 min, out float2 max);

        float2 middle = (min + max) * 0.5f;
        double lon = (middle.X * 360.0) - 180.0;
        double lat = 90.0 - (middle.Y * 180.0);

        RoundTrip(lon, lat, min, max);
    }

    private static readonly float2 Origin = new(25f, 178f);
    private static readonly float2 Size = new(1392f, 672f);

    private static void RoundTrip(double lon, double lat, float2 min, float2 max)
    {
        float2 uv = MapViewport.ToUv(lon, lat);
        Assert.True(MapViewport.Shows(uv, min, max), "the fixture put the place off the crop");

        float2 pixel = new(
            Origin.X + ((uv.X - min.X) / (max.X - min.X) * Size.X),
            Origin.Y + ((uv.Y - min.Y) / (max.Y - min.Y) * Size.Y));

        Assert.True(MapViewport.TryPlace(Origin, Size, min, max, pixel,
                                         out double back, out double backLat));

        Assert.Equal(lon, back, 3);
        Assert.Equal(lat, backLat, 3);
    }

    [Fact]
    public void AClickOffTheImageIsNotAPlace()
    {
        Assert.False(MapViewport.TryPlace(Origin, Size, float2.Zero, float2.One,
                                          new float2(Origin.X - 1f, Origin.Y + 10f), out _, out _));
        Assert.False(MapViewport.TryPlace(Origin, Size, float2.Zero, float2.One,
                                          new float2(Origin.X + 10f, Origin.Y + Size.Y + 1f),
                                          out _, out _));
    }

    [Fact]
    public void AMarkOutsideTheCropIsNotShown()
    {
        MapViewport.FromPanZoom(Area, float2.Zero, 2f, out float2 min, out float2 max);

        Assert.True(MapViewport.Shows(MapViewport.ToUv(-90.0, 45.0), min, max));
        Assert.False(MapViewport.Shows(MapViewport.ToUv(90.0, -45.0), min, max));
    }
}
