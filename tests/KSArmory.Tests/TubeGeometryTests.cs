using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The launcher's own geometry — tube positions, tube direction, and where the moving assemblies
/// sit once the drives are laid. This is what a second launcher rewrites, so it is pinned closely.
/// </summary>
public class TubeGeometryTests
{
    private const double Tol = 1e-9;

    /// <summary>A launcher whose numbers are round enough to assert against by hand.</summary>
    private static LauncherProfile TestLauncher(double referenceElevationRad = Math.PI / 4) => new()
    {
        PartId = "Test_Prefab_Launcher",
        DisplayName = "test launcher",
        Munition = "57E6",
        Sensor = "1RS1",
        TurretMarker = "Turret",
        PodsMarker = "Pods",
        RadarMarker = "Radar",
        Tubes =
        [
            new(3.0, 1.0,  0.5),
            new(3.0, 1.0, -0.5),
            new(2.0, 2.0,  0.5),
            new(2.0, 2.0, -0.5),
        ],
        TurretPivot = new(0.0, -1.5, 0.0),
        PodPivotFromTurret = new(2.5, -0.5, 0.0),
        RadarPivotFromTurret = new(4.0, -1.0, 0.0),
        GunsMarker = "Guns",
        GunPivotFromTurret = new(2.7, 0.1, 0.0),
        GunReferenceElevationRad = 0.38397,
        PodReferenceElevationRad = referenceElevationRad,
        MuzzleForwardOffset = 5.0,
        TubeRingRadius = 1.2,
    };

    private static void AssertClose(double3 expected, double3 actual, string what)
    {
        Assert.True(Vec.Len(expected - actual) < 1e-9,
            $"{what}: expected ({expected.X:F6},{expected.Y:F6},{expected.Z:F6}) " +
            $"but got ({actual.X:F6},{actual.Y:F6},{actual.Z:F6})");
    }

    // ---- Tube positions ------------------------------------------------

    [Fact]
    public void AnUnrotatedPodPutsATubeWhereTheProfileSaysItIs()
    {
        LauncherProfile profile = TestLauncher();
        double3 podPosition = new(1, 2, 3);

        Assert.True(TubeGeometry.TryMuzzlePartFrame(profile, 0, podPosition, doubleQuat.Identity, out double3 muzzle));
        AssertClose(podPosition + profile.Tubes[0].Position, muzzle, "tube 0");
    }

    [Fact]
    public void TubesRideThePodsRotation()
    {
        LauncherProfile profile = TestLauncher();

        // Half a turn about the traverse axis (+X) maps (x, y, z) -> (x, -y, -z).
        doubleQuat halfTurn = doubleQuat.CreateFromAxisAngle(TubeGeometry.TraverseAxis, Math.PI);

        Assert.True(TubeGeometry.TryMuzzlePartFrame(profile, 0, Vec.Zero, halfTurn, out double3 muzzle));

        double3 o = profile.Tubes[0].Position;
        AssertClose(new double3(o.X, -o.Y, -o.Z), muzzle, "tube 0 after a half turn");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void ATubeThisLauncherDoesNotHaveIsRefused(int tubeIndex)
    {
        LauncherProfile profile = TestLauncher();

        Assert.False(TubeGeometry.TryMuzzlePartFrame(profile, tubeIndex, Vec.Zero, doubleQuat.Identity, out _));
        Assert.False(TubeGeometry.TrySeatedPartFrame(profile, tubeIndex, Vec.Zero, doubleQuat.Identity, 3.0, out _));
    }

    // ---- Tube direction ------------------------------------------------

    /// <summary>
    /// The elevation convention, stated exactly. X is the traverse axis, so it carries the
    /// vertical component: level tubes point along +Y and vertical ones along +X.
    /// </summary>
    [Theory]
    [InlineData(0.0, 0.0, 1.0)]                      // level
    [InlineData(Math.PI / 2, 1.0, 0.0)]              // straight up
    [InlineData(Math.PI / 6, 0.5, 0.8660254037844387)]
    public void TheTubeAxisReadsTheElevationItWasBuiltAt(double elevation, double x, double y)
    {
        LauncherProfile profile = TestLauncher(elevation);

        AssertClose(new double3(x, y, 0), TubeGeometry.TubeAxisPodFrame(profile), "tube axis in the pod frame");
    }

    /// <summary>
    /// Laying the pods to an elevation must actually produce that elevation. This is the whole
    /// point of the reference-elevation convention — the pods are modelled at their working angle
    /// and runtime elevation is applied as a rotation <em>away</em> from it, so a sign error here
    /// puts the tubes through the tracking radar rather than at the sky.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.95993)]
    [InlineData(Math.PI / 2)]
    public void ElevatingToAnAngleLeavesTheTubesAtThatAngle(double commanded)
    {
        LauncherProfile profile = TestLauncher(0.95993);   // the Pantsir's 55 degrees

        DrivePose pose = TubeGeometry.PodPose(profile, bearingRad: 0.0, elevationRad: commanded);
        double3 axis = TubeGeometry.TubeAxisPartFrame(profile, pose.Rotation);

        AssertClose(new double3(Math.Sin(commanded), Math.Cos(commanded), 0), axis,
                    $"tube axis at {commanded:F5} rad");
    }

    [Fact]
    public void ElevatingToTheModelledAngleIsANoOp()
    {
        // A launcher commanded to the pose it was modelled at must not move at all - that is what
        // makes a refused transform write leave the vehicle looking right rather than broken.
        LauncherProfile profile = TestLauncher(0.95993);

        DrivePose pose = TubeGeometry.PodPose(profile, 0.0, profile.PodReferenceElevationRad);

        AssertClose(TubeGeometry.TubeAxisPodFrame(profile),
                    TubeGeometry.TubeAxisPartFrame(profile, pose.Rotation),
                    "tube axis at the reference elevation");
    }

    [Fact]
    public void TheTubeAxisIsAlwaysAUnitVector()
    {
        LauncherProfile profile = TestLauncher(0.7);

        for (double bearing = -Math.PI; bearing <= Math.PI; bearing += 0.37)
        {
            for (double elevation = 0.0; elevation <= Math.PI / 2; elevation += 0.19)
            {
                DrivePose pose = TubeGeometry.PodPose(profile, bearing, elevation);
                double length = Vec.Len(TubeGeometry.TubeAxisPartFrame(profile, pose.Rotation));

                Assert.True(Math.Abs(length - 1.0) < Tol,
                    $"tube axis at bearing {bearing:F2} elevation {elevation:F2} had length {length:F9}");
            }
        }
    }

    // ---- Per-tube direction --------------------------------------------

    /// <summary>
    /// A tube with no direction of its own follows the pods. That is the parallel-bundle case, what
    /// the model generator emits, and what every tube on the Pantsir does.
    /// </summary>
    [Fact]
    public void ATubeWithNoDirectionOfItsOwnFollowsThePods()
    {
        LauncherProfile profile = TestLauncher(0.95993);

        for (int tube = 0; tube < profile.TubeCount; tube++)
        {
            Assert.False(profile.Tubes[tube].HasOwnDirection);
            AssertClose(TubeGeometry.TubeAxisPodFrame(profile),
                        TubeGeometry.TubeAxisPodFrame(profile, tube),
                        $"tube {tube} without its own direction");
        }
    }

    /// <summary>
    /// Each tube carries its own direction, which is what makes a splayed bundle — a VLS with
    /// divergence, an MLRS — expressible at all.
    /// </summary>
    [Fact]
    public void ASplayedBundleGivesEachTubeItsOwnDirection()
    {
        var splayed = new LauncherProfile
        {
            PartId = "Test_Prefab_Splayed",
            DisplayName = "splayed rack",
            Munition = "57E6",
            Sensor = "1RS1",
            PodsMarker = "Pods",
            Tubes =
            [
                new(new double3(1, 0,  0.5), new double3(0, 1,  1)),   // canted one way
                new(new double3(1, 0, -0.5), new double3(0, 1, -1)),   // and the other
                new(1, 0, 0),                                          // straight up the middle
            ],
            PodReferenceElevationRad = Math.PI / 2,
        };

        double3 left = TubeGeometry.TubeAxisPodFrame(splayed, 0);
        double3 right = TubeGeometry.TubeAxisPodFrame(splayed, 1);
        double3 centre = TubeGeometry.TubeAxisPodFrame(splayed, 2);

        // Each canted tube is a unit vector along its own declared direction.
        AssertClose(Vec.Unit(new double3(0, 1, 1)), left, "left tube");
        AssertClose(Vec.Unit(new double3(0, 1, -1)), right, "right tube");

        // The undirected one still follows the pod reference elevation.
        AssertClose(new double3(1, 0, 0), centre, "centre tube");

        Assert.True(Vec.Len(left - right) > 0.1, "the splayed tubes point the same way");
    }

    [Fact]
    public void ASplayedTubesDirectionRidesThePodsThroughTraverse()
    {
        var splayed = new LauncherProfile
        {
            PartId = "Test_Prefab_Splayed",
            DisplayName = "splayed rack",
            Munition = "57E6",
            Sensor = "1RS1",
            Tubes = [new(new double3(1, 0, 0), new double3(0, 1, 0))],
            PodReferenceElevationRad = 0.5,
        };

        // Half a turn about the traverse axis takes the declared +Y direction onto -Y.
        doubleQuat halfTurn = doubleQuat.CreateFromAxisAngle(TubeGeometry.TraverseAxis, Math.PI);

        AssertClose(new double3(0, -1, 0),
                    TubeGeometry.TubeAxisPartFrame(splayed, halfTurn, 0),
                    "splayed tube after a half turn");
    }

    /// <summary>
    /// A declared direction need not be normalised — a profile is hand-authored, and requiring unit
    /// vectors of whoever writes one is an invitation to a subtly scaled launch.
    /// </summary>
    [Fact]
    public void ADeclaredDirectionIsNormalised()
    {
        var profile = new LauncherProfile
        {
            PartId = "Test_Prefab_Long",
            DisplayName = "unnormalised",
            Munition = "57E6",
            Sensor = "1RS1",
            Tubes = [new(new double3(0, 0, 0), new double3(0, 900, 0))],
            PodReferenceElevationRad = 0.3,
        };

        Assert.Equal(1.0, Vec.Len(TubeGeometry.TubeAxisPodFrame(profile, 0)), 9);
    }

    /// <summary>
    /// A degenerate or out-of-range direction falls back to the pod axis rather than throwing or
    /// producing a zero vector. A tube number comes from a magazine slot, and a launcher firing
    /// into empty air is a better failure than one that takes the game down.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void AnOutOfRangeTubeFallsBackToThePodAxis(int tubeIndex)
    {
        LauncherProfile profile = TestLauncher(0.6);

        AssertClose(TubeGeometry.TubeAxisPodFrame(profile),
                    TubeGeometry.TubeAxisPodFrame(profile, tubeIndex),
                    $"tube {tubeIndex}");
    }

    [Fact]
    public void ASplayedRoundSeatsAlongItsOwnTubeNotThePods()
    {
        // Two tubes at the same mouth position, pointing opposite ways. If seating used the pod
        // axis the two would land in the same place; using each tube's own axis puts them on
        // opposite sides of the mouth.
        var splayed = new LauncherProfile
        {
            PartId = "Test_Prefab_Splayed",
            DisplayName = "splayed rack",
            Munition = "57E6",
            Sensor = "1RS1",
            Tubes =
            [
                new(new double3(2, 0, 0), new double3(0, 1, 0)),
                new(new double3(2, 0, 0), new double3(0, -1, 0)),
            ],
            PodReferenceElevationRad = Math.PI / 2,
        };

        Assert.True(TubeGeometry.TrySeatedPartFrame(splayed, 0, Vec.Zero, doubleQuat.Identity, 4.0, out double3 a));
        Assert.True(TubeGeometry.TrySeatedPartFrame(splayed, 1, Vec.Zero, doubleQuat.Identity, 4.0, out double3 b));

        AssertClose(new double3(2, -2, 0), a, "tube 0 seated");
        AssertClose(new double3(2, 2, 0), b, "tube 1 seated");
    }

    // ---- Seating -------------------------------------------------------

    /// <summary>
    /// A seated round sits half a body back from the mouth, along the tube. The mesh is modelled
    /// about its centre, so seating it at the mouth leaves half of it sticking out of the tube.
    /// </summary>
    [Fact]
    public void ASeatedRoundSitsHalfABodyBackAlongTheTube()
    {
        LauncherProfile profile = TestLauncher(0.0);   // level tubes: the axis is +Y
        const double bodyLength = 3.0;

        Assert.True(TubeGeometry.TryMuzzlePartFrame(profile, 0, Vec.Zero, doubleQuat.Identity, out double3 muzzle));
        Assert.True(TubeGeometry.TrySeatedPartFrame(profile, 0, Vec.Zero, doubleQuat.Identity, bodyLength, out double3 seated));

        AssertClose(muzzle - new double3(0, bodyLength / 2.0, 0), seated, "seated position");
        Assert.Equal(bodyLength / 2.0, Vec.Len(muzzle - seated), 9);
    }

    [Fact]
    public void SeatingIsBehindTheMouthAtEveryAim()
    {
        LauncherProfile profile = TestLauncher(0.95993);
        const double bodyLength = 3.1;

        for (double bearing = -Math.PI; bearing <= Math.PI; bearing += 0.51)
        {
            DrivePose pose = TubeGeometry.PodPose(profile, bearing, 0.4);

            Assert.True(TubeGeometry.TryMuzzlePartFrame(profile, 1, pose.Position, pose.Rotation, out double3 muzzle));
            Assert.True(TubeGeometry.TrySeatedPartFrame(profile, 1, pose.Position, pose.Rotation, bodyLength, out double3 seated));

            // Exactly half a body back, and on the far side of the mouth from where it will fly.
            Assert.Equal(bodyLength / 2.0, Vec.Len(muzzle - seated), 9);

            double3 axis = TubeGeometry.TubeAxisPartFrame(profile, pose.Rotation);
            Assert.True(Vec.Dot(muzzle - seated, axis) > 0.0,
                "the round is seated on the wrong side of the mouth - it would fly backwards out of the tube");
        }
    }

    // ---- The drives ----------------------------------------------------

    /// <summary>
    /// Subparts do not nest in KSA, so the pods are a sibling of the turret and their
    /// <em>position</em> has to be rewritten as it traverses — otherwise they spin on the spot
    /// while the turret rotates out from under them.
    /// </summary>
    [Fact]
    public void TraversingMovesThePodsAndNotJustTheirRotation()
    {
        LauncherProfile profile = TestLauncher();

        DrivePose forward = TubeGeometry.PodPose(profile, 0.0, 0.5);
        DrivePose beam = TubeGeometry.PodPose(profile, Math.PI / 2, 0.5);

        Assert.True(Vec.Len(forward.Position - beam.Position) > 0.1,
            "the pods did not move when the turret traversed - they are pivoting on the spot");
    }

    [Fact]
    public void ThePodsHangOffTheTurretPivotByTheTrunnionOffset()
    {
        LauncherProfile profile = TestLauncher();

        DrivePose pose = TubeGeometry.PodPose(profile, bearingRad: 0.0, elevationRad: 0.3);
        AssertClose(profile.TurretPivot + profile.PodPivotFromTurret, pose.Position, "pods at zero bearing");

        // Half a turn about +X flips the offset's Y and Z but leaves the pivot alone.
        DrivePose reversed = TubeGeometry.PodPose(profile, Math.PI, 0.3);
        double3 p = profile.PodPivotFromTurret;
        AssertClose(profile.TurretPivot + new double3(p.X, -p.Y, -p.Z), reversed.Position, "pods reversed");
    }

    [Fact]
    public void ThePodsElevationDoesNotMoveTheirPivot()
    {
        // Elevation happens about the trunnion, so the trunnion itself must stay put.
        LauncherProfile profile = TestLauncher();

        DrivePose low = TubeGeometry.PodPose(profile, 1.1, 0.0);
        DrivePose high = TubeGeometry.PodPose(profile, 1.1, Math.PI / 2);

        AssertClose(low.Position, high.Position, "pod pivot across the elevation range");
    }

    /// <summary>
    /// The search array turns off the clock and rides the turret. Its spin must not shift it, and
    /// the turret's traverse must.
    /// </summary>
    [Fact]
    public void TheSearchArraySpinsWithoutMovingButStillRidesTheTurret()
    {
        LauncherProfile profile = TestLauncher();

        DrivePose still = TubeGeometry.RadarPose(profile, bearingRad: 0.6, spinRad: 0.0);
        DrivePose spun = TubeGeometry.RadarPose(profile, bearingRad: 0.6, spinRad: 2.4);
        AssertClose(still.Position, spun.Position, "search array position across its own spin");
        Assert.NotEqual(still.Rotation, spun.Rotation);

        DrivePose traversed = TubeGeometry.RadarPose(profile, bearingRad: 0.6 + Math.PI / 2, spinRad: 0.0);
        Assert.True(Vec.Len(still.Position - traversed.Position) > 0.1,
            "the search array did not move when the turret traversed");
    }

    [Fact]
    public void TheSearchArraysSpinComposesOntoTheTurretsBearing()
    {
        LauncherProfile profile = TestLauncher();

        // Both rotations are about the same axis, so the angles simply add.
        DrivePose split = TubeGeometry.RadarPose(profile, bearingRad: 0.4, spinRad: 0.9);
        doubleQuat combined = TubeGeometry.TurretRotation(1.3);

        double3 probe = new(0.3, 0.8, -0.5);
        AssertClose(combined * probe, split.Rotation * probe, "composed search-array rotation");
    }

    // ---- Boresight modes -----------------------------------------------

    /// <summary>
    /// Local "up" is not a part-frame direction — it depends on where the parent body is — so it
    /// must be refused rather than guessed at. A mode that silently fell back to +X would leave a
    /// ground site searching whichever way the truck happened to be parked.
    /// </summary>
    [Fact]
    public void LocalUpIsNotAPartFrameDirectionAndIsRefused()
    {
        LauncherProfile profile = TestLauncher(0.6);

        Assert.False(TubeGeometry.TryBoresightPartFrame(
            profile, BoresightMode.LocalUp, 0.3, 0.4, out double3 partFrame));
        Assert.Equal(Vec.Zero, partFrame);
    }

    [Fact]
    public void PartForwardIsTheLongAxisAndIgnoresTheDrives()
    {
        LauncherProfile profile = TestLauncher(0.6);

        Assert.True(TubeGeometry.TryBoresightPartFrame(
            profile, BoresightMode.PartForward, bearingRad: 2.1, elevationRad: 0.2, out double3 a));
        Assert.True(TubeGeometry.TryBoresightPartFrame(
            profile, BoresightMode.PartForward, bearingRad: -0.7, elevationRad: 1.3, out double3 b));

        AssertClose(TubeGeometry.ForwardAxis, a, "part-forward boresight");
        AssertClose(a, b, "part-forward boresight across two different aims");
    }

    /// <summary>
    /// The other half of the pair, and the axis <see cref="BoresightMode.PartForward"/> used to
    /// return. A sight that looks away from its mount wants this one; a seeker never does.
    /// </summary>
    [Fact]
    public void MountNormalIsTheMountingFaceAndIgnoresTheDrives()
    {
        LauncherProfile profile = TestLauncher(0.6);

        Assert.True(TubeGeometry.TryBoresightPartFrame(
            profile, BoresightMode.MountNormal, bearingRad: 2.1, elevationRad: 0.2, out double3 a));
        Assert.True(TubeGeometry.TryBoresightPartFrame(
            profile, BoresightMode.MountNormal, bearingRad: -0.7, elevationRad: 1.3, out double3 b));

        AssertClose(TubeGeometry.TraverseAxis, a, "mount-normal boresight");
        AssertClose(a, b, "mount-normal boresight across two different aims");
    }

    /// <summary>
    /// Slaved to the launcher: the cone has to move when the drives do, which is the entire
    /// difference between this mode and <see cref="BoresightMode.PartForward"/>.
    /// </summary>
    [Fact]
    public void TurretAxisFollowsTheDrivesWherePartForwardDoesNot()
    {
        LauncherProfile profile = TestLauncher(0.95993);

        Assert.True(TubeGeometry.TryBoresightPartFrame(
            profile, BoresightMode.TurretAxis, bearingRad: 0.0, elevationRad: 0.2, out double3 low));
        Assert.True(TubeGeometry.TryBoresightPartFrame(
            profile, BoresightMode.TurretAxis, bearingRad: 1.4, elevationRad: 1.2, out double3 high));

        Assert.True(Vec.Len(low - high) > 0.1, "the turret-slaved boresight did not move with the drives");

        // And it is genuinely the tube axis, not something adjacent to it.
        DrivePose pose = TubeGeometry.PodPose(profile, 0.0, 0.2);
        AssertClose(TubeGeometry.TubeAxisPartFrame(profile, pose.Rotation, 0), low, "turret-axis boresight");
    }

    [Fact]
    public void EveryPartRelativeBoresightIsAUnitVector()
    {
        LauncherProfile profile = TestLauncher(0.8);

        foreach (BoresightMode mode in new[] { BoresightMode.PartForward, BoresightMode.TurretAxis })
        {
            for (double bearing = -Math.PI; bearing <= Math.PI; bearing += 0.63)
            {
                Assert.True(TubeGeometry.TryBoresightPartFrame(profile, mode, bearing, 0.5, out double3 dir));
                Assert.Equal(1.0, Vec.Len(dir), 9);
            }
        }
    }

    // ---- The cannon ------------------------------------------------------

    /// <summary>
    /// The cannon use the same drive as the pods on a different trunnion, so everything the pods
    /// are pinned for has to hold for them: elevating to an angle produces that angle, the
    /// trunnion rides the traverse, and elevation alone does not move it.
    /// </summary>
    [Fact]
    public void TheCannonElevateOnTheirOwnTrunnion()
    {
        LauncherProfile profile = TestLauncher();

        DrivePose forward = TubeGeometry.GunPose(profile, bearingRad: 0.0, elevationRad: 0.3);
        AssertClose(profile.TurretPivot + profile.GunPivotFromTurret, forward.Position,
                    "cannon at zero bearing");

        // Half a turn about the traverse axis flips the offset's Y and Z, not the pivot.
        DrivePose reversed = TubeGeometry.GunPose(profile, Math.PI, 0.3);
        double3 g = profile.GunPivotFromTurret;
        AssertClose(profile.TurretPivot + new double3(g.X, -g.Y, -g.Z), reversed.Position,
                    "cannon reversed");

        // Elevation happens about the trunnion, so the trunnion itself stays put.
        DrivePose high = TubeGeometry.GunPose(profile, 0.0, Math.PI / 2);
        AssertClose(forward.Position, high.Position, "cannon pivot across the elevation range");
    }

    /// <summary>
    /// The cannon and the pods are laid on one solution but sit at different modelled angles, so
    /// the same commanded elevation must produce the same barrel angle from either reference.
    /// </summary>
    [Fact]
    public void TheCannonAndThePodsReachTheSameCommandedElevation()
    {
        LauncherProfile profile = TestLauncher(0.95993);

        foreach (double commanded in new[] { 0.0, 0.4, 1.1, Math.PI / 2 })
        {
            DrivePose pods = TubeGeometry.PodPose(profile, 0.0, commanded);
            DrivePose guns = TubeGeometry.GunPose(profile, 0.0, commanded);

            // Each reference axis carried through its own drive lands at the commanded angle.
            double3 podAxis = Vec.Unit(pods.Rotation * TubeGeometry.TubeAxisPodFrame(profile));
            double3 gunAxis = Vec.Unit(guns.Rotation * new double3(
                Math.Sin(profile.GunReferenceElevationRad),
                Math.Cos(profile.GunReferenceElevationRad), 0.0));

            AssertClose(new double3(Math.Sin(commanded), Math.Cos(commanded), 0), podAxis,
                        $"pod axis at {commanded:F3}");
            AssertClose(new double3(Math.Sin(commanded), Math.Cos(commanded), 0), gunAxis,
                        $"gun axis at {commanded:F3}");
        }
    }

    [Fact]
    public void ALauncherWithNoCannonSaysSo()
    {
        var noGuns = new LauncherProfile
        {
            PartId = "Test_Prefab_NoGuns", DisplayName = "no guns",
            Munition = "57E6", Sensor = "1RS1",
            Tubes = [new(1, 0, 0)],
        };

        Assert.Null(noGuns.GunsMarker);
    }

    // ---- The muzzle ring fallback --------------------------------------

    [Fact]
    public void TheMuzzleRingSitsOnTheBoresightAtTheRightRadius()
    {
        LauncherProfile profile = TestLauncher();
        double3 origin = new(100, 200, 300);
        double3 boresight = new(0, 0, 1);

        for (int tube = 0; tube < profile.TubeCount; tube++)
        {
            double3 muzzle = TubeGeometry.MuzzleRingEcl(profile, origin, boresight, tube);
            double3 fromOrigin = muzzle - origin;

            Assert.Equal(profile.MuzzleForwardOffset, Vec.Dot(fromOrigin, boresight), 9);
            Assert.Equal(profile.TubeRingRadius, Vec.Len(Vec.RejectFrom(fromOrigin, boresight)), 9);
        }
    }

    [Fact]
    public void TheMuzzleRingGivesEveryTubeItsOwnPlace()
    {
        LauncherProfile profile = TestLauncher();
        double3 boresight = Vec.Unit(new double3(1, 1, 1));

        var seen = new List<double3>();
        for (int tube = 0; tube < profile.TubeCount; tube++)
        {
            seen.Add(TubeGeometry.MuzzleRingEcl(profile, Vec.Zero, boresight, tube));
        }

        for (int i = 0; i < seen.Count; i++)
        {
            for (int j = i + 1; j < seen.Count; j++)
            {
                Assert.True(Vec.Len(seen[i] - seen[j]) > 1e-6, $"tubes {i} and {j} share a muzzle position");
            }
        }
    }

    // ---- Round bodies in flight ----------------------------------------

    /// <summary>
    /// A round that has not moved is exactly at its anchor. Any offset here appears in game as the
    /// round starting somewhere other than its tube.
    /// </summary>
    [Fact]
    public void ARoundAtZeroTravelIsExactlyAtItsTube()
    {
        double3 anchor = new(2.9, 1.8, 1.3);

        double3 placed = TubeGeometry.BodyPositionPartFrame(
            anchor, travelEcl: Vec.Zero,
            ecl2Asmb: doubleQuat.CreateFromAxisAngle(new double3(0, 1, 0), 0.8),
            asmb2Part: doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), -0.3),
            sinceLaunchAsmb: doubleQuat.Identity);

        AssertClose(anchor, placed, "body at zero travel");
    }

    /// <summary>
    /// The anchor is already in the part frame and must not be rotated; only the travel is
    /// converted. Rotating both is how a round ends up leaving from the wrong side of the vehicle.
    /// </summary>
    [Fact]
    public void OnlyTheTravelIsRotatedIntoThePartFrame()
    {
        // The anchor is off the rotation axis on purpose: on the axis, rotating it is a no-op, so
        // a version that wrongly rotates the anchor too would give the same answer.
        double3 anchor = new(1, 2, 0);
        double3 travel = new(0, 10, 0);

        // A quarter turn about +X maps +Y onto +Z.
        doubleQuat quarter = doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), Math.PI / 2);

        double3 placed = TubeGeometry.BodyPositionPartFrame(anchor, travel, quarter, doubleQuat.Identity, doubleQuat.Identity);

        AssertClose(new double3(1, 2, 10), placed, "body with rotated travel");
    }

    [Fact]
    public void TravelAccumulatesLinearlyFromTheAnchor()
    {
        double3 anchor = new(5, 5, 5);
        double3 travel = new(100, 0, 0);

        double3 once = TubeGeometry.BodyPositionPartFrame(anchor, travel, doubleQuat.Identity, doubleQuat.Identity, doubleQuat.Identity);
        double3 twice = TubeGeometry.BodyPositionPartFrame(anchor, travel * 2.0, doubleQuat.Identity, doubleQuat.Identity, doubleQuat.Identity);

        AssertClose(once - anchor, (twice - anchor) * 0.5, "travel scaling");
    }

    [Fact]
    public void ABodyPointsAlongTheDirectionItIsGiven()
    {
        double3 direction = new(0, 0, 400);

        doubleQuat rotation = TubeGeometry.BodyRotationPartFrame(direction, doubleQuat.Identity, doubleQuat.Identity);

        // The mesh is built nose-along +X, so the rotation must carry +X onto the flight direction.
        AssertClose(Vec.Unit(direction), Vec.Unit(rotation * FireGeometry.NoseAxis), "body nose");
    }

    [Fact]
    public void ABodysHeadingIsConvertedThroughBothFrames()
    {
        // A direction expressed in Ecl has to come back through the vehicle's attitude and the
        // launcher's own mounting before it means anything to a subpart transform.
        doubleQuat ecl2Asmb = doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), Math.PI / 2);
        doubleQuat asmb2Part = doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), Math.PI / 2);

        double3 directionEcl = new(300, 0, 0);

        doubleQuat rotation = TubeGeometry.BodyRotationPartFrame(directionEcl, ecl2Asmb, asmb2Part);
        double3 expected = Vec.Unit(asmb2Part * (ecl2Asmb * directionEcl));

        AssertClose(expected, Vec.Unit(rotation * FireGeometry.NoseAxis), "body nose through both frames");
    }

    // ---- Fins ----------------------------------------------------------

    [Fact]
    public void FinsGrowFromStowedToFullSpanWithoutChangingLength()
    {
        var munition = new MunitionProfile { Name = "test", DisplayName = "test", FinStowedScale = 0.06f };

        double3 stowed = TubeGeometry.FinScale(munition, 0.0);
        double3 open = TubeGeometry.FinScale(munition, 1.0);

        // X is along the body, so the round never changes length.
        Assert.Equal(1.0, stowed.X, 9);
        Assert.Equal(1.0, open.X, 9);

        Assert.Equal(munition.FinStowedScale, stowed.Y, 6);
        Assert.Equal(munition.FinStowedScale, stowed.Z, 6);
        Assert.Equal(1.0, open.Y, 9);
        Assert.Equal(1.0, open.Z, 9);
    }

    [Theory]
    [InlineData(-5.0)]
    [InlineData(2.0)]
    [InlineData(1e30)]
    public void FinDeploymentIsClampedRatherThanTrusted(double deployment)
    {
        var munition = new MunitionProfile { Name = "test", DisplayName = "test", FinStowedScale = 0.06f };

        double3 scale = TubeGeometry.FinScale(munition, deployment);

        // A negative span would invert the mesh and a runaway one would explode it.
        Assert.True(double.IsFinite(scale.Y) && scale.Y >= munition.FinStowedScale - 1e-9 && scale.Y <= 1.0,
            $"deployment {deployment} produced a span of {scale.Y}");
    }

    /// <summary>
    /// A non-finite deployment is <em>not</em> laundered into a valid span, because
    /// <c>Math.Clamp(NaN, ..)</c> is NaN. That makes the caller's finite check load-bearing rather
    /// than defensive: <c>LauncherPart.TryPlaceFins</c> refuses the write, so the fins hold their
    /// last good transform instead of being handed a singular one.
    ///
    /// <para>Pinned rather than fixed here on purpose. Clamping NaN to stowed would silently change
    /// what the engine is handed in a case no test can reach, and this repository does not ship
    /// behaviour changes it cannot verify in flight.</para>
    /// </summary>
    [Fact]
    public void ANonFiniteDeploymentStaysNonFiniteSoTheCallerRejectsIt()
    {
        var munition = new MunitionProfile { Name = "test", DisplayName = "test", FinStowedScale = 0.06f };

        Assert.False(Vec.IsFinite(TubeGeometry.FinScale(munition, double.NaN)));
    }

    // ---- The anchor is a world point, not a part-frame one -----------------

    /// <summary>
    /// A craft that rolls after firing must not drag the round with it.
    ///
    /// <para>The anchor is where the tube <em>was</em>, written down in the launcher's frame. The
    /// travel term is re-converted through the craft's current attitude every frame and so stays
    /// put; the anchor was not, so a rolling launcher swung every round already in flight about its
    /// own centre. The lever arm is the whole distance from tube to centre of mass — metres on a
    /// stack, and plainly visible in orbit.</para>
    ///
    /// <para>Checked where it matters: back out in the world, which is what a player sees.</para>
    /// </summary>
    [Fact]
    public void RollingTheCraftDoesNotMoveARoundAlreadyInFlight()
    {
        double3 anchor = new(1.73, 0.96, 0.0);
        double3 travelEcl = new(400.0, -60.0, 12.0);

        // Deliberately NOT identity. With identity the composition below is the same whichever way
        // round its operands go, so the test would pass against a reversed quaternion order and
        // prove nothing about the one thing it exists to check.
        doubleQuat atLaunch = doubleQuat.CreateFromAxisAngle(Vec.Unit(new double3(0.3, -0.7, 0.5)), 1.1);

        double3 WorldOf(doubleQuat attitudeNow)
        {
            doubleQuat ecl2Asmb = doubleQuat.Conjugate(attitudeNow);
            double3 partFrame = TubeGeometry.BodyPositionPartFrame(
                anchor, travelEcl, ecl2Asmb, doubleQuat.Identity,
                doubleQuat.Concatenate(atLaunch, ecl2Asmb));
            return attitudeNow * partFrame;         // back into the world the player watches
        }

        double3 still = WorldOf(atLaunch);
        foreach (double roll in new[] { 0.4, 1.6, 3.14159, 5.0 })
        {
            double3 rolled = WorldOf(doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), roll));
            Assert.True(Vec.Len(still - rolled) < 1e-9,
                $"rolling {roll:F2} rad moved the round {Vec.Len(still - rolled):F3} m");
        }
    }

    /// <summary>A launcher that never turns is untouched by any of this.</summary>
    [Fact]
    public void AnUnturnedCraftLeavesTheAnchorExactlyWhereItWas()
    {
        double3 anchor = new(2.9, 1.8, 1.3);
        doubleQuat asmb2Part = doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), 0.7);
        AssertClose(anchor, TubeGeometry.CarryAnchor(anchor, doubleQuat.Identity, asmb2Part),
                    "identity carry");
    }

    /// <summary>An attitude that was never recorded must leave the anchor exactly as it is.</summary>
    [Fact]
    public void AnUnsetAttitudeLeavesTheAnchorAlone()
    {
        double3 anchor = new(2.9, 1.8, 1.3);
        AssertClose(anchor, TubeGeometry.CarryAnchor(anchor, default, doubleQuat.Identity),
                    "unset quaternion");
    }
}
