using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A standalone optical head's geometry. The clamp is the part worth pinning: it is not a
/// preference but a fact about the model — a ball on a mast cannot see past what holds it up —
/// and the way it clamps matters as much as that it does. A head told to look below the floor has
/// to keep pointing the right way and stop at the lowest thing it can see, not swing somewhere
/// else on the way.
/// </summary>
public class OpticGeometryTests
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

    // The mount's normal, which elevation is measured against.
    private static readonly double3 Up = new(1, 0, 0);

    [Fact]
    public void ElevationIsZeroAcrossTheMountAndAQuarterTurnOffIt()
    {
        Assert.Equal(0.0, OpticGeometry.ElevationRad(new double3(0, 1, 0)), 9);
        Assert.Equal(0.0, OpticGeometry.ElevationRad(new double3(0, 0, -1)), 9);
        Assert.Equal(Math.PI / 2, OpticGeometry.ElevationRad(Up), 9);
        Assert.Equal(-Math.PI / 2, OpticGeometry.ElevationRad(new double3(-1, 0, 0)), 9);
    }

    [Fact]
    public void TheEyeSitsAlongTheAimAndSoHasNoPerpendicularPart()
    {
        OpticProfile p = Director();
        double3 aim = Vec.Unit(new double3(0.3, 1.0, -0.4));

        double3 eye = OpticGeometry.EyePartFrame(p, aim);
        double3 offset = eye - p.HeadPivot;

        Assert.Equal(p.EyeForward, Vec.Len(offset), 6);
        Assert.Equal(0.0, Vec.Len(Vec.RejectFrom(offset, aim)), 9);
    }

    [Fact]
    public void AnAimInsideTheTravelIsLeftAlone()
    {
        OpticProfile p = Director();

        foreach (double3 aim in new[]
                 {
                     new double3(0, 1, 0),            // level
                     new double3(0.3, 1, 0),          // climbing
                     new double3(-0.2, 1, 0.4),       // depressed, but not past the floor
                 })
        {
            double3 clamped = OpticGeometry.ClampToTravel(p, aim);

            Assert.Equal(OpticGeometry.ElevationRad(aim), OpticGeometry.ElevationRad(clamped), 9);
        }
    }

    [Fact]
    public void LookingBelowTheFloorStopsAtIt()
    {
        OpticProfile p = Director();
        double3 clamped = OpticGeometry.ClampToTravel(p, new double3(-1, 0.2, 0));

        Assert.Equal(float.DegreesToRadians(p.MinElevationDeg),
                     OpticGeometry.ElevationRad(clamped), 6);
    }

    [Fact]
    public void LookingAboveTheCeilingStopsAtIt()
    {
        OpticProfile p = Director();
        double3 clamped = OpticGeometry.ClampToTravel(p, new double3(1, 0.05, 0));

        Assert.Equal(float.DegreesToRadians(p.MaxElevationDeg),
                     OpticGeometry.ElevationRad(clamped), 6);
    }

    /// <summary>
    /// The half that a naive clamp gets wrong: keeping the bearing. A head told to look down and
    /// to the left must still be looking left afterwards, or it swings across the picture on its
    /// way to the floor.
    /// </summary>
    [Fact]
    public void ClampingKeepsTheBearingAndMovesOnlyTheElevation()
    {
        OpticProfile p = Director();
        double3 aim = Vec.Unit(new double3(-1.0, 0.6, -0.8));

        double3 clamped = OpticGeometry.ClampToTravel(p, aim);

        double3 wanted = Vec.Unit(Vec.RejectFrom(aim, Up));
        double3 got = Vec.Unit(Vec.RejectFrom(clamped, Up));

        Assert.Equal(0.0, Vec.AngleBetween(wanted, got), 9);
        Assert.Equal(1.0, Vec.Len(clamped), 9);
    }

    /// <summary>
    /// Straight up has no bearing to keep, so inventing one would swing the head to an arbitrary
    /// compass point. It is left where it was told to go instead — and it is inside the travel
    /// only if the ceiling allows, which at 85 degrees it does not.
    /// </summary>
    [Fact]
    public void StraightUpIsLeftAloneRatherThanGivenAnArbitraryBearing()
    {
        OpticProfile p = Director();

        Assert.Equal(Math.PI / 2, OpticGeometry.ElevationRad(OpticGeometry.ClampToTravel(p, Up)), 9);
    }

    [Fact]
    public void TheHeadTurnsFromItsRestDirectionOntoTheAim()
    {
        OpticProfile p = Director();
        double3 aim = Vec.Unit(new double3(0.4, 0.8, -0.3));

        DrivePose pose = OpticGeometry.Pose(p, aim);

        Assert.Equal(p.HeadPivot.X, pose.Position.X, 9);
        Assert.Equal(0.0, Vec.AngleBetween(pose.Rotation * TubeGeometry.OpticRestDirection, aim), 9);
    }

    [Fact]
    public void ItRefusesNothingAndInventsNothingFromAnUnusableAim()
    {
        OpticProfile p = Director();

        Assert.Equal(p.HeadPivot.X, OpticGeometry.EyePartFrame(p, Vec.Zero).X, 9);
        Assert.Equal(0.0, OpticGeometry.ElevationRad(Vec.Zero), 9);
    }
}
