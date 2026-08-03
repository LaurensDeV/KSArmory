using Brutal.Numerics;
using Xunit;

namespace AirDefence.Tests;

/// <summary>
/// Guards the overlay-anchoring invariant, which has now been broken twice in two different
/// ways and is invisible to every other test — it only shows up as an overlay drawn beside the
/// craft, which nothing but a human looking at the screen has ever reported.
///
/// The rule: <see cref="DrawAnchor.Ego"/> is sampled <b>this frame</b>, <see cref="DrawAnchor.Ecl"/>
/// is the reference the geometry was measured against <b>one update earlier</b>. Using one
/// instant for both leaves a frame of ecliptic motion — ~500 m at 60 fps near Earth — between
/// the overlay and the craft.
/// </summary>
public class DrawAnchorTests
{
    /// <summary>Earth's ecliptic motion over one frame at 60 fps: 29800 / 60.</summary>
    private static readonly double3 FrameDrift = new(497, 0, 0);

    /// <summary>
    /// The case that matters: geometry measured against the platform's *older* position must
    /// land exactly on the platform's *current* render position.
    ///
    /// This is what the cone apex, the tube markers and the launch point all rely on.
    /// </summary>
    [Fact]
    public void GeometryAtTheOldReference_MapsOntoTheCurrentRenderPosition()
    {
        // Platform was here when the battery ran; it has since moved a frame's worth.
        double3 platformThen = new(1.475e11, 0, 0);
        double3 platformNowEgo = new(12, -3, 45);   // wherever the camera puts it now

        var anchor = new DrawAnchor(platformNowEgo, platformThen);

        // A point recorded at the platform's own position must draw on the platform.
        double3 mapped = anchor.ToEgo(platformThen);

        Assert.True(Vec.Len(mapped - platformNowEgo) < 1e-9,
            $"geometry at the anchor's own reference landed {Vec.Len(mapped - platformNowEgo):F1} m away");
    }

    /// <summary>
    /// The regression proper: collapsing the two instants into one reintroduces the drift.
    /// A "tidier" anchor built from a single sample must measurably fail this.
    /// </summary>
    [Fact]
    public void UsingOneInstantForBoth_LeavesAFrameOfDrift()
    {
        double3 platformThen = new(1.475e11, 0, 0);
        double3 platformNow = platformThen + FrameDrift;

        // Correct: Ego from the current position, Ecl from the geometry's epoch.
        double3 egoNow = new(0, 0, 0);              // camera-relative, platform at the origin
        var correct = new DrawAnchor(egoNow, platformThen);

        // Wrong: Ego derived from the stale reference, so the drift never cancels. This is
        // exactly the shape of both shipped regressions.
        var collapsed = new DrawAnchor(egoNow + (platformThen - platformNow), platformThen);

        double3 geometryAtPlatform = platformThen;

        double correctError = Vec.Len(correct.ToEgo(geometryAtPlatform) - egoNow);
        double collapsedError = Vec.Len(collapsed.ToEgo(geometryAtPlatform) - egoNow);

        Assert.True(correctError < 1e-9);
        Assert.True(collapsedError > 400.0,
            "the collapsed form should be visibly wrong - if it is not, this test no longer guards anything");
    }

    /// <summary>Offsets from the reference must be preserved exactly, whatever the epochs.</summary>
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(2500, 0, 0)]
    [InlineData(-1500, 800, 12000)]
    public void OffsetsFromTheReference_ArePreserved(double ox, double oy, double oz)
    {
        double3 platformThen = new(1.475e11, 1.2e10, -3.5e6);
        double3 egoNow = new(-8, 44, 2);

        var anchor = new DrawAnchor(egoNow, platformThen);

        double3 offset = new(ox, oy, oz);
        double3 mapped = anchor.ToEgo(platformThen + offset);

        Assert.True(Vec.Len(mapped - (egoNow + offset)) < 1e-6,
            "an offset from the reference must appear as the same offset from the anchor");
    }

    /// <summary>
    /// The mapping must stay exact at ecliptic magnitudes — ~1.5e11 m — where a careless
    /// formulation would lose metres to floating-point cancellation.
    /// </summary>
    [Fact]
    public void MappingIsExact_AtEclipticMagnitudes()
    {
        double3 platformThen = new(1.4750e11, 1.3650e11, -3.586e6);
        double3 egoNow = new(196.1, 801.1, -630.5);

        var anchor = new DrawAnchor(egoNow, platformThen);

        // One metre north of the reference should be one metre north of the anchor.
        double3 mapped = anchor.ToEgo(platformThen + new double3(0, 1, 0));

        Assert.True(Vec.Len(mapped - (egoNow + new double3(0, 1, 0))) < 1e-3,
            "precision lost differencing at 1e11 m");
    }

    [Fact]
    public void NonFiniteInputs_AreRejected()
    {
        Assert.False(new DrawAnchor(new double3(double.NaN, 0, 0), default).IsValid);
        Assert.False(new DrawAnchor(default, new double3(0, double.PositiveInfinity, 0)).IsValid);
        Assert.True(new DrawAnchor(new double3(1, 2, 3), new double3(4, 5, 6)).IsValid);
    }
}
