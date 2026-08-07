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

    // ---- The cursor is aimed from the mount, not from the camera ----------
    //
    // A direction has no origin, so pointing a drive down the camera's ray is only right for
    // something infinitely far away. These pin the near case, where it is not.

    /// <summary>
    /// The whole bug in one assertion: the camera stands off the launcher, the cursor is on
    /// ground close by, and the bearing from the mount is nothing like the bearing from the eye.
    /// </summary>
    [Fact]
    public void AimingAtSomethingNearAnswersFromTheMountNotTheCamera()
    {
        // An orbit camera 60 m above a launcher, cursor on ground 40 m to the side at the
        // launcher's own level: pointing *below* the mount on screen, which is the case that
        // was reported and the case where the two origins diverge hardest.
        double3 mount = new(0, 0, 0);
        double3 eye = new(0, 60, 0);
        double3 ground = new(40, 0, 0);

        double3 rayDirection = Vec.Unit(ground - eye);
        double range = Vec.Len(ground - eye);

        Assert.True(CursorAim.TryAimFromMount(eye, rayDirection, range, mount, out double3 aim));

        // It points at the thing under the cursor, which is the entire requirement.
        Assert.Equal(0.0, Vec.Len(aim - Vec.Unit(ground - mount)), 9);

        // And that is a different world from what the camera's own direction would have given.
        double apart = double.RadiansToDegrees(
            Math.Acos(Math.Clamp(Vec.Dot(aim, rayDirection), -1.0, 1.0)));
        Assert.True(apart > 30.0,
            $"the camera's direction and the mount's differ by only {apart:F1} degrees, so this "
            + "geometry cannot tell the two apart and the test is not guarding anything");
    }

    /// <summary>
    /// Why it went unnoticed: against the sky the two answers are the same to well under what a
    /// drive can resolve. Aiming above the launcher was always going to look perfect.
    /// </summary>
    [Fact]
    public void AimingAtTheSkyIsIndistinguishableFromTheCamerasOwnDirection()
    {
        double3 mount = new(0, 0, 0);
        double3 eye = new(-80, 40, 0);
        double3 rayDirection = Vec.Unit(new double3(1, 2, 0));

        Assert.True(CursorAim.TryAimFromMount(eye, rayDirection, 20_000.0, mount, out double3 aim));

        double apart = double.RadiansToDegrees(Math.Acos(Math.Clamp(Vec.Dot(aim, rayDirection), -1, 1)));
        Assert.True(apart < 0.5, $"{apart:F2} degrees apart at 20 km");
    }

    /// <summary>
    /// The invariance that says the subtraction is the only thing this does: put the camera at the
    /// mount and the answer is the ray, whatever range is claimed.
    /// </summary>
    [Theory]
    [InlineData(5.0)]
    [InlineData(1_000.0)]
    [InlineData(20_000.0)]
    public void ACameraAtTheMountJustReturnsTheRay(double range)
    {
        double3 mount = new(1_000, -2_000, 3_000);
        double3 rayDirection = Vec.Unit(new double3(-1, 0.4, 2));

        Assert.True(CursorAim.TryAimFromMount(mount, rayDirection, range, mount, out double3 aim));
        Assert.Equal(0.0, Vec.Len(aim - rayDirection), 9);
    }

    /// <summary>
    /// Moving the whole engagement does not move the answer. Ecl carries 29.8 km/s of ecliptic
    /// motion into every position, so an aim that is not invariant under a common offset is
    /// carrying a frame it has no business carrying.
    /// </summary>
    [Fact]
    public void AddingTheSameOffsetToTheCameraAndTheMountChangesNothing()
    {
        double3 eye = new(-80, 40, 0);
        double3 mount = new(0, 0, 0);
        double3 direction = Vec.Unit(new double3(1, -0.5, 0.2));

        Assert.True(CursorAim.TryAimFromMount(eye, direction, 120.0, mount, out double3 near));

        double3 shift = new(1.4959e11, -2.7e10, 3.3e6);
        Assert.True(CursorAim.TryAimFromMount(eye + shift, direction, 120.0, mount + shift,
                                              out double3 shifted));

        Assert.Equal(0.0, Vec.Len(near - shifted), 6);
    }

    [Fact]
    public void ARangeOrARayThatCannotBeUsedIsRefusedRatherThanReturningNaN()
    {
        double3 eye = new(-80, 40, 0);
        double3 mount = Vec.Zero;
        double3 direction = new(0, 1, 0);

        Assert.False(CursorAim.TryAimFromMount(eye, direction, 0.0, mount, out _));
        Assert.False(CursorAim.TryAimFromMount(eye, direction, -50.0, mount, out _));
        Assert.False(CursorAim.TryAimFromMount(eye, direction, double.NaN, mount, out _));
        Assert.False(CursorAim.TryAimFromMount(eye, Vec.Zero, 100.0, mount, out _));
        Assert.False(CursorAim.TryAimFromMount(new double3(double.NaN, 0, 0), direction, 100.0,
                                               mount, out _));

        // The one degenerate case that is not an input error: the cursor resolved to the mount
        // itself, so there is no bearing to be had.
        Assert.False(CursorAim.TryAimFromMount(eye, Vec.Unit(mount - eye), Vec.Len(mount - eye),
                                               mount, out _));
    }
}
