using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The map's local frame and the square hung off it.
///
/// <para>The load-bearing one is <see cref="TheFrameCarriesNoEclipticMotion"/>. Everything here is
/// a <em>difference</em> against an anchor, and the whole point of that is that the ecliptic
/// motion both terms share subtracts out — a map built from absolute positions slides off the
/// ground at 29.8 km/s. It is the same rule the draw anchor and the round bodies obey.</para>
/// </summary>
public class TerrainMapTests
{
    // A body a bit like Earth: 6371 km, spinning about a tilted axis so nothing here can pass by
    // accidentally agreeing with the ecliptic pole.
    private static readonly double3 Centre = new(1.2e11, -3.4e10, 5.0e9);
    private static readonly double3 Axis = Vec.Unit(new double3(0.0, 0.3979, 0.9174));
    private const double Radius = 6.371e6;

    private static double3 Surface(double3 direction) => Centre + Vec.Unit(direction) * Radius;

    private static MapFrame At(double3 direction)
    {
        MapFrame? frame = MapFrame.TryAt(Centre, Surface(direction), Axis);
        Assert.NotNull(frame);
        return frame.Value;
    }

    [Fact]
    public void TheTriadIsOrthonormalAndRightHanded()
    {
        foreach (double3 where in new[] { new double3(1, 0, 0), new double3(0.3, 0.5, 0.8),
                                          new double3(-0.7, 0.2, -0.1) })
        {
            MapFrame f = At(where);

            Assert.Equal(1.0, Vec.Len(f.Up), 9);
            Assert.Equal(1.0, Vec.Len(f.East), 9);
            Assert.Equal(1.0, Vec.Len(f.North), 9);

            Assert.Equal(0.0, Vec.Dot(f.Up, f.East), 9);
            Assert.Equal(0.0, Vec.Dot(f.Up, f.North), 9);
            Assert.Equal(0.0, Vec.Dot(f.East, f.North), 9);

            // east x north == up, which is what makes north-up draw the world the right way round
            // rather than mirrored.
            Assert.Equal(0.0, Vec.Len(Vec.Cross(f.East, f.North) - f.Up), 9);
        }
    }

    /// <summary>
    /// North points towards the rotation axis, not the ecliptic pole. On a body with Earth's tilt
    /// those differ by 23°, which is a map wrong by that much everywhere.
    /// </summary>
    [Fact]
    public void NorthIsTheBodysOwnAxis()
    {
        MapFrame f = At(new double3(1, 0, 0));

        Assert.True(Vec.Dot(f.North, Axis) > 0.0);
        Assert.Equal(0.0, Vec.Dot(f.East, Axis), 9);
    }

    [Fact]
    public void ThereIsNoBearingAtThePoles()
    {
        Assert.Null(MapFrame.TryAt(Centre, Centre + Axis * Radius, Axis));
        Assert.Null(MapFrame.TryAt(Centre, Centre - Axis * Radius, Axis));
    }

    [Fact]
    public void TheAnchorIsTheOriginAndOffsetsReadBack()
    {
        MapFrame f = At(new double3(0.3, 0.5, 0.8));
        double3 anchor = Centre + f.Up * Radius;

        // Four places, not six. The anchor is 1.2e11 m from the ecliptic origin, where a double's
        // last bit is about 1.2e-5 m -- so a micron is below what the coordinates can represent,
        // and a tenth of a millimetre is already far finer than a terrain map can mean.
        Assert.Equal(0.0, Vec.Len(f.ToLocal(anchor)), 4);

        // 400 m east and 250 m north of the anchor, read back as exactly that.
        double3 point = anchor + f.East * 400.0 + f.North * 250.0 + f.Up * 30.0;
        double3 local = f.ToLocal(point);

        Assert.Equal(400.0, local.X, 4);
        Assert.Equal(250.0, local.Y, 4);
        Assert.Equal(30.0, local.Z, 4);
    }

    /// <summary>
    /// <b>The frame contract.</b> Move the body, the anchor and the point together and the local
    /// coordinates must not budge — because that shared displacement is the ecliptic motion every
    /// sample carries, and a map that did not cancel it would slide off the ground.
    /// </summary>
    [Fact]
    public void TheFrameCarriesNoEclipticMotion()
    {
        double3 where = Vec.Unit(new double3(0.3, 0.5, 0.8));
        double3 anchor = Centre + where * Radius;
        double3 point = anchor + Vec.Cross(Axis, where) * 700.0;

        double3 before = At(where).ToLocal(point);

        // One frame at 29.8 km/s, which is what the planet actually moves between samples.
        double3 carried = new(29_800.0 / 60.0, 0.0, 0.0);

        MapFrame? moved = MapFrame.TryAt(Centre + carried, anchor + carried, Axis);
        Assert.NotNull(moved);

        double3 after = moved.Value.ToLocal(point + carried);

        Assert.Equal(0.0, Vec.Len(after - before), 4);
    }

    /// <summary>
    /// A sampled direction is the one a height field is asked along, so it has to point at the cell
    /// rather than near it. At the anchor that is exactly up.
    /// </summary>
    [Fact]
    public void SampleDirectionsPointAtTheirCells()
    {
        MapFrame f = At(new double3(0.3, 0.5, 0.8));

        Assert.Equal(0.0, Vec.AngleBetween(f.DirectionAt(0.0, 0.0), f.Up), 9);

        foreach ((double east, double north) in new[] { (1000.0, 0.0), (0.0, -800.0), (600.0, 600.0) })
        {
            double3 dir = f.DirectionAt(east, north);

            Assert.Equal(1.0, Vec.Len(dir), 9);

            // The direction hits the sphere where the offset says, to within the curvature over a
            // kilometre — which is what makes a flat grid legitimate at this span.
            double3 local = f.ToLocal(Centre + dir * Radius);

            Assert.Equal(east, local.X, 1);
            Assert.Equal(north, local.Y, 1);
        }
    }

    [Fact]
    public void NorthIsUpTheScreen()
    {
        // Centre of the square.
        Assert.Equal(0.5f, TerrainMap.ToUnitSquare(new double3(0, 0, 0), 2000.0).X, 5);
        Assert.Equal(0.5f, TerrainMap.ToUnitSquare(new double3(0, 0, 0), 2000.0).Y, 5);

        // North of the anchor is *up*, so its Y is smaller.
        Assert.True(TerrainMap.ToUnitSquare(new double3(0, 500, 0), 2000.0).Y < 0.5f);

        // East is to the right.
        Assert.True(TerrainMap.ToUnitSquare(new double3(500, 0, 0), 2000.0).X > 0.5f);
    }

    [Fact]
    public void OffMapContactsComeBackOnTheEdgeTheyLeft()
    {
        Assert.Null(TerrainMap.EdgeToward(new float2(0.5f, 0.5f)));

        foreach (float2 far in new[] { new float2(3f, 0.5f), new float2(0.5f, -2f),
                                       new float2(-4f, -4f) })
        {
            float2? edge = TerrainMap.EdgeToward(far);
            Assert.NotNull(edge);
            Assert.True(TerrainMap.OnMap(edge.Value));

            // On the border, and in the direction the contact actually lies.
            float2 e = edge.Value;
            Assert.True(Math.Abs(Math.Max(Math.Abs(e.X - 0.5f), Math.Abs(e.Y - 0.5f)) - 0.5f) < 1e-5f);
            Assert.True((e.X - 0.5f) * (far.X - 0.5f) >= 0f);
            Assert.True((e.Y - 0.5f) * (far.Y - 0.5f) >= 0f);
        }
    }

    [Fact]
    public void CellsCoverTheSquareAndAreCentred()
    {
        const int cells = 64;
        const double span = 2000.0;

        double first = TerrainMap.CellOffset(0, cells, span);
        double last = TerrainMap.CellOffset(cells - 1, cells, span);

        // Half a cell in from each edge, and symmetric about the anchor.
        Assert.Equal(-span / 2 + span / cells / 2, first, 9);
        Assert.Equal(-first, last, 9);
    }

    [Fact]
    public void FlatGroundShadesFlatAndASlopeDoesNot()
    {
        double flat = TerrainMap.Relief(100.0, 100.0, 100.0, 100.0, 30.0);
        Assert.Equal(TerrainMap.Relief(0.0, 0.0, 0.0, 0.0, 30.0), flat, 9);

        // Lit from the north-west, so the pair has to straddle *that* line. A slope falling
        // equally east and north lies square to it and shades the same either way round, which is
        // correct and proves nothing -- east against west is the pair that separates.
        double toward = TerrainMap.Relief(90.0, 110.0, 100.0, 100.0, 30.0);
        double away = TerrainMap.Relief(110.0, 90.0, 100.0, 100.0, 30.0);

        Assert.True(Math.Abs(toward - away) > 0.2, $"slopes shade the same: {toward:F3} vs {away:F3}");
        Assert.InRange(toward, 0.0, 1.0);
        Assert.InRange(away, 0.0, 1.0);
    }

    /// <summary>
    /// Zooming <em>in</em> makes the square smaller. The two run opposite ways, so a control wired
    /// straight to the span works backwards — which is the whole reason the sign lives in
    /// <see cref="TerrainMap.Zoom"/> rather than at the button.
    /// </summary>
    [Fact]
    public void ZoomingInNarrowsTheSquare()
    {
        Assert.True(TerrainMap.Zoom(2000f, +1) < 2000f, "zooming in must show less ground, not more");
        Assert.True(TerrainMap.Zoom(2000f, -1) > 2000f, "zooming out must show more ground");
    }

    /// <summary>
    /// Heading is clockwise from north, which is what a compass reads and what the arrow is drawn
    /// against. Getting the sense backwards is a map that points the wrong way and looks fine.
    /// </summary>
    [Fact]
    public void HeadingIsClockwiseFromNorth()
    {
        // Local is (east, north, up).
        Assert.Equal(0.0, TerrainMap.HeadingDeg(new double3(0, 100, 0))!.Value, 6);
        Assert.Equal(90.0, TerrainMap.HeadingDeg(new double3(100, 0, 0))!.Value, 6);
        Assert.Equal(180.0, TerrainMap.HeadingDeg(new double3(0, -100, 0))!.Value, 6);
        Assert.Equal(270.0, TerrainMap.HeadingDeg(new double3(-100, 0, 0))!.Value, 6);

        // North-east is 45, and it is reported in [0, 360) rather than signed.
        Assert.Equal(45.0, TerrainMap.HeadingDeg(new double3(70, 70, 0))!.Value, 6);
        Assert.Equal(315.0, TerrainMap.HeadingDeg(new double3(-70, 70, 0))!.Value, 6);
    }

    /// <summary>
    /// Climbing is not a heading. A craft going straight up has no direction over the ground, and
    /// an arrow built from it would spin on the spot.
    /// </summary>
    [Fact]
    public void ThereIsNoHeadingWithoutGroundSpeed()
    {
        Assert.Null(TerrainMap.HeadingDeg(new double3(0, 0, 250)));
        Assert.Null(TerrainMap.HeadingDeg(new double3(0, 0, 0)));
        Assert.Null(TerrainMap.HeadingDeg(new double3(0.1, 0.1, 0)));

        // And the vertical part never leaks into the speed over the ground.
        Assert.Equal(100.0, TerrainMap.GroundSpeed(new double3(60, 80, 900)), 9);
    }

    [Fact]
    public void ADirectionTakesNoAnchorWithIt()
    {
        MapFrame f = At(new double3(0.3, 0.5, 0.8));

        // 200 m/s due east, as a velocity. Through ToLocal it would come back as a point on the
        // far side of the planet; as a direction it is what it says.
        double3 local = f.ToLocalDirection(f.East * 200.0);

        Assert.Equal(200.0, local.X, 6);
        Assert.Equal(0.0, local.Y, 6);
        Assert.Equal(0.0, local.Z, 6);

        Assert.Equal(90.0, TerrainMap.HeadingDeg(local)!.Value, 6);
    }

    [Fact]
    public void ZoomStepsThroughTheDetentsAndStopsAtTheEnds()
    {
        ReadOnlySpan<float> spans = TerrainMap.Spans;

        // spans[0] is the narrowest, so it is as far in as the map goes.
        Assert.Equal(spans[0], TerrainMap.Zoom(spans[0], +1));
        Assert.Equal(spans[^1], TerrainMap.Zoom(spans[^1], -1));
        Assert.Equal(spans[0], TerrainMap.Zoom(spans[1], +1));

        // A value between detents snaps to the nearest before stepping, so a stale saved span
        // cannot strand the control.
        Assert.Equal(spans[2], TerrainMap.Zoom(1900f, 0));
    }

    /// <summary>
    /// The refresh distance is what keeps the cost off the frame. A span that re-sampled every
    /// metre would be the per-frame cost this design exists to avoid.
    /// </summary>
    [Fact]
    public void TheGridSurvivesSmallMovements()
    {
        Assert.True(TerrainMap.RefreshDistance(2000.0) >= 100.0);
        Assert.True(TerrainMap.RefreshDistance(500.0) >= 10.0);
    }
}
