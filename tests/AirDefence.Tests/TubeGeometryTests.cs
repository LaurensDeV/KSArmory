using Brutal.Numerics;
using Xunit;

namespace AirDefence.Tests;

/// <summary>
/// The launcher's own geometry — tube positions, tube direction, and where the moving assemblies
/// sit once the drives are laid.
///
/// <para>All of this used to sit behind a <c>Part</c> argument it only read two properties off, so
/// none of it could be tested. It is also precisely what a second launcher rewrites, which is why
/// it is pinned here first. See <c>docs/MODULARITY.md</c>.</para>
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
        TubeOffsets =
        [
            new(3.0, 1.0,  0.5),
            new(3.0, 1.0, -0.5),
            new(2.0, 2.0,  0.5),
            new(2.0, 2.0, -0.5),
        ],
        TurretPivot = new(0.0, -1.5, 0.0),
        PodPivotFromTurret = new(2.5, -0.5, 0.0),
        RadarPivotFromTurret = new(4.0, -1.0, 0.0),
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
        AssertClose(podPosition + profile.TubeOffsets[0], muzzle, "tube 0");
    }

    [Fact]
    public void TubesRideThePodsRotation()
    {
        LauncherProfile profile = TestLauncher();

        // Half a turn about the traverse axis (+X) maps (x, y, z) -> (x, -y, -z).
        doubleQuat halfTurn = doubleQuat.CreateFromAxisAngle(TubeGeometry.TraverseAxis, Math.PI);

        Assert.True(TubeGeometry.TryMuzzlePartFrame(profile, 0, Vec.Zero, halfTurn, out double3 muzzle));

        double3 o = profile.TubeOffsets[0];
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
    /// <b>The wrecking-ball case.</b> Subparts do not nest in KSA, so the pods are a sibling of
    /// the turret rather than a child. Their <em>position</em> therefore has to be rewritten as the
    /// turret traverses; leaving it alone spins them on the spot while the turret rotates out from
    /// under them.
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
            asmb2Part: doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), -0.3));

        AssertClose(anchor, placed, "body at zero travel");
    }

    /// <summary>
    /// The anchor is already in the part frame and must not be rotated; only the travel is
    /// converted. Rotating both is how a round ends up leaving from the wrong side of the vehicle.
    /// </summary>
    [Fact]
    public void OnlyTheTravelIsRotatedIntoThePartFrame()
    {
        // The anchor is deliberately OFF the rotation axis. With it on the axis the rotation is a
        // no-op for that term, so a version that wrongly rotates the anchor too gives the same
        // answer and the test proves nothing - which is the mistake the zigzag regression test
        // made and the reason this file says so out loud.
        double3 anchor = new(1, 2, 0);
        double3 travel = new(0, 10, 0);

        // A quarter turn about +X maps +Y onto +Z.
        doubleQuat quarter = doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), Math.PI / 2);

        double3 placed = TubeGeometry.BodyPositionPartFrame(anchor, travel, quarter, doubleQuat.Identity);

        AssertClose(new double3(1, 2, 10), placed, "body with rotated travel");
    }

    [Fact]
    public void TravelAccumulatesLinearlyFromTheAnchor()
    {
        double3 anchor = new(5, 5, 5);
        double3 travel = new(100, 0, 0);

        double3 once = TubeGeometry.BodyPositionPartFrame(anchor, travel, doubleQuat.Identity, doubleQuat.Identity);
        double3 twice = TubeGeometry.BodyPositionPartFrame(anchor, travel * 2.0, doubleQuat.Identity, doubleQuat.Identity);

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
}
