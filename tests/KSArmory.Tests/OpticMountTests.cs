using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A director whose base moves — riding a turret's traverse, a hinge, an arm, anything.
///
/// <para>The whole design is that the head reads where its base <em>is</em> rather than working it
/// out from whatever moved it. So these pin the two halves of that: a mount carries the head's
/// <em>position</em> and the surface it measures against, and it must <em>not</em> carry the head's
/// aim — a ball already pointed at something does not swing when the thing under it turns. Getting
/// the second wrong is invisible on a hull, where the mount is the identity, and wrong by the full
/// traverse angle the moment anything rides a turret.</para>
/// </summary>
public class OpticMountTests
{
    private static OpticProfile Director() => new()
    {
        PartId = "test",
        DisplayName = "test",
        Sensor = "test",
        BaseMarker = "Base",
        HeadMarker = "Head",
        HeadPivot = new(0.63, 0.0, 0.0),
        EyeForward = 0.30f,
        MinElevationDeg = -20f,
        MaxElevationDeg = 85f,
    };

    // A traverse: about the part's +X, which is the mount's own normal, at an offset from the axis
    // so turning actually carries the base somewhere.
    private static MountFrame Traversed(double bearingRad, double3 offsetFromAxis)
    {
        doubleQuat turn = doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), bearingRad);
        return new MountFrame(turn * offsetFromAxis, turn);
    }

    private static void AssertClose(double3 expected, double3 actual, double tol = 1e-9)
    {
        Assert.True(Vec.Len(expected - actual) < tol,
                    $"expected {expected.X:F6},{expected.Y:F6},{expected.Z:F6} "
                    + $"got {actual.X:F6},{actual.Y:F6},{actual.Z:F6}");
    }

    [Fact]
    public void AFixedMountIsExactlyTheHullCase()
    {
        OpticProfile p = Director();
        double3 aim = Vec.Unit(new double3(0.4, 1.0, 0.2));

        Assert.Equal(OpticGeometry.Pose(p, aim).Position,
                     OpticGeometry.Pose(p, MountFrame.Fixed, aim).Position);
        Assert.Equal(OpticGeometry.Pose(p, aim).Rotation,
                     OpticGeometry.Pose(p, MountFrame.Fixed, aim).Rotation);
        Assert.Equal(OpticGeometry.EyePartFrame(p, aim),
                     OpticGeometry.EyePartFrame(p, MountFrame.Fixed, aim));
        Assert.Equal(OpticGeometry.ClampToTravel(p, aim),
                     OpticGeometry.ClampToTravel(p, MountFrame.Fixed, aim));
    }

    [Fact]
    public void TheMountCarriesTheHeadsPivotRoundWithIt()
    {
        OpticProfile p = Director();
        double3 offset = new(0.0, 1.05, 0.44);          // off the traverse axis, so it swings

        // A quarter turn about +X takes +Y to +Z and +Z to -Y.
        DrivePose pose = OpticGeometry.Pose(p, Traversed(Math.PI / 2, offset), new double3(0, 1, 0));

        AssertClose(new double3(0.63, -0.44, 1.05), pose.Position);
    }

    [Fact]
    public void APivotOnTheTraverseAxisDoesNotMoveWhenTheMountTurns()
    {
        OpticProfile p = Director();
        double3 onAxis = new(0.0, 0.0, 0.0);

        double3 rest = OpticGeometry.Pose(p, Traversed(0.0, onAxis), new double3(0, 1, 0)).Position;

        foreach (double bearing in new[] { 0.3, 1.4, Math.PI, 5.9 })
        {
            AssertClose(rest, OpticGeometry.Pose(p, Traversed(bearing, onAxis),
                                                 new double3(0, 1, 0)).Position);
        }
    }

    /// <summary>
    /// The one that separates this design from composing the mover's angle in. A head is told where
    /// to look in the part's frame, so turning its base must leave the aim alone — otherwise the
    /// picture swings by the traverse every time the turret moves and the operator fights it.
    /// </summary>
    [Fact]
    public void TurningTheMountDoesNotTurnWhereTheHeadLooks()
    {
        double3 aim = Vec.Unit(new double3(0.3, 1.0, 0.0));
        double3 offset = new(0.0, 1.05, 0.44);

        double3 rest = OpticGeometry.Rotation(MountFrame.Fixed, aim) * OpticGeometry.RestDirection;

        foreach (double bearing in new[] { 0.0, 0.7, 2.5, 4.8 })
        {
            double3 looked = OpticGeometry.Rotation(Traversed(bearing, offset), aim)
                             * OpticGeometry.RestDirection;

            AssertClose(aim, looked, 1e-9);
            AssertClose(rest, looked, 1e-9);
        }
    }

    [Fact]
    public void TheEyeStaysOnTheAimAheadOfTheCarriedPivot()
    {
        OpticProfile p = Director();
        double3 aim = Vec.Unit(new double3(0.2, 1.0, 0.1));
        MountFrame mount = Traversed(1.1, new double3(0.0, 1.05, 0.44));

        double3 pivot = OpticGeometry.Pose(p, mount, aim).Position;
        double3 eye = OpticGeometry.EyePartFrame(p, mount, aim);

        // Along the aim, by exactly the forward offset: the eye slides up the line of sight and
        // contributes nothing perpendicular to it, whatever the mount has done.
        AssertClose(pivot + aim * p.EyeForward, eye);
        Assert.Equal(p.EyeForward, Vec.Len(eye - pivot), 9);
    }

    /// <summary>
    /// Elevation is measured off the surface the base is bolted to, so tilting the mount tilts what
    /// counts as level. A hinge is the case this exists for: the same aim reads as a different
    /// elevation once the thing carrying the director has folded over.
    /// </summary>
    [Fact]
    public void ElevationIsMeasuredAgainstTheMountsOwnSurface()
    {
        double3 aim = new(1, 0, 0);                     // straight up in the part's frame

        Assert.Equal(Math.PI / 2, OpticGeometry.ElevationRad(MountFrame.Fixed, aim), 9);

        // Fold the mount 90 degrees about +Z: its normal goes from +X to +Y, so an aim along +X is
        // now level rather than overhead.
        MountFrame folded = new(Vec.Zero, doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), Math.PI / 2));

        Assert.Equal(0.0, OpticGeometry.ElevationRad(folded, aim), 9);
        Assert.Equal(Math.PI / 2, OpticGeometry.ElevationRad(folded, new double3(0, 1, 0)), 9);
    }

    [Fact]
    public void TheTravelLimitsFollowTheMountRatherThanThePart()
    {
        OpticProfile p = Director();                    // floor at -20 deg off the mount's plane

        MountFrame folded = new(Vec.Zero, doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), Math.PI / 2));

        // Steeply down the part's -X, but off the axis so there is a bearing to preserve — dead
        // along a mount's normal is the one case the clamp deliberately passes through, having no
        // bearing to keep. Against a fixed mount this is ~79 deg below the floor and gets clamped;
        // against the folded one it is 11 deg up and legal, so it survives untouched.
        double3 down = Vec.Unit(new double3(-1, 0.2, 0));

        Assert.Equal(float.DegreesToRadians(p.MinElevationDeg),
                     OpticGeometry.ElevationRad(OpticGeometry.ClampToTravel(p, down)), 9);

        AssertClose(down, OpticGeometry.ClampToTravel(p, folded, down));
    }

    /// <summary>
    /// The roll reference is the mount's normal, so a head on a turret keeps its own up square to
    /// the turret roof rather than to the hull. Pinned by rolling the mount about the aim itself,
    /// which changes nothing about where the head points and everything about which way is up.
    /// </summary>
    [Fact]
    public void TheRollReferenceComesFromTheMount()
    {
        double3 aim = new(0, 1, 0);                     // along the rest direction

        doubleQuat upright = OpticGeometry.Rotation(MountFrame.Fixed, aim);
        double3 uprightUp = upright * OpticGeometry.MountNormal;

        // Roll the mount half a turn about the aim: its normal flips, so the head's own up must
        // follow it rather than staying with the part.
        MountFrame rolled = new(Vec.Zero, doubleQuat.CreateFromAxisAngle(aim, Math.PI));
        double3 rolledUp = OpticGeometry.Rotation(rolled, aim) * OpticGeometry.MountNormal;

        AssertClose(-uprightUp, rolledUp, 1e-9);

        // ...and the aim is untouched by that roll, which is what makes it a roll.
        AssertClose(aim, OpticGeometry.Rotation(rolled, aim) * OpticGeometry.RestDirection);
    }
}
