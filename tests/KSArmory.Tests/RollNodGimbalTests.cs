using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The roll-nod gimbal a targeting pod is hung on, which is a different mechanism from the mast
/// head and not a variation of it.
///
/// <para>What has to hold is one sentence with teeth: <b>the window's rotation, relative to the
/// shell's, is a pure tilt about an axis square to the pod's centreline.</b> The two bodies are one
/// decomposition of one rotation, and a decomposition that does not decompose draws a nose whose
/// ball twists inside its own shroud. Nothing but this can see it — the aim is correct either way,
/// so the picture is right and the model is wrong.</para>
///
/// <para>The rest is the keyhole and the stop, both of which are the mechanism rather than a
/// preference.</para>
/// </summary>
public class RollNodGimbalTests
{
    private static OpticProfile Pod() => new()
    {
        PartId = "test",
        DisplayName = "test",
        Sensor = "test",
        Gimbal = GimbalKind.RollNod,
        BaseMarker = "Body",
        RollMarker = "Roll",
        HeadMarker = "Head",
        HeadPivot = new(0.26669, 0.94282, 0.0),
        EyeForward = 0.223f,
        MaxOffBoresightDeg = 150f,
        KeyholeDeg = 4f,
    };

    private static OpticProfile Mast() => new()
    {
        PartId = "test",
        DisplayName = "test",
        Sensor = "test",
        BaseMarker = "Base",
        HeadMarker = "Head",
        HeadPivot = new(0.63, 0.0, 0.0),
        MinElevationDeg = -20f,
        MaxElevationDeg = 85f,
    };

    // A mount carried somewhere else and turned, which is the case that separates a roll measured
    // in the mount's own frame from one measured in the part's.
    private static MountFrame Tilted(double angleRad)
    {
        doubleQuat turn = doubleQuat.CreateFromAxisAngle(Vec.Unit(new double3(0.3, -0.5, 1.0)),
                                                         angleRad);
        return new MountFrame(new double3(1.7, -0.4, 0.9), turn);
    }

    private static IEnumerable<double3> Aims()
    {
        foreach (double off in new[] { 8.0, 25.0, 60.0, 90.0, 130.0, 149.0 })
        {
            foreach (double roll in new[] { 0.0, 37.0, 90.0, 175.0, -120.0, -60.0 })
            {
                double o = double.DegreesToRadians(off);
                double r = double.DegreesToRadians(roll);

                // Off-boresight `o` from +Y, rolled `r` about it from the mount's normal (+X).
                yield return new double3(Math.Sin(o) * Math.Cos(r),
                                         Math.Cos(o),
                                         Math.Sin(o) * Math.Sin(r));
            }
        }
    }

    [Fact]
    public void TheWindowOnlyEverNodsWithinTheShell()
    {
        OpticProfile p = Pod();

        foreach (MountFrame mount in new[] { MountFrame.Fixed, Tilted(0.9), Tilted(-2.4) })
        {
            foreach (double3 raw in Aims())
            {
                double3 aim = Vec.Unit(mount.Rotation * raw);

                doubleQuat head = OpticGeometry.Rotation(p, mount, aim);
                doubleQuat shell = OpticGeometry.RollPose(p, mount, aim).Rotation;

                // What the window has done that the shell has not.
                doubleQuat nod = doubleQuat.Conjugate(shell) * head;

                // A nod turns about the trunnion, which is square to the centreline the shell
                // rolls about. Any component along that centreline is a twist the mechanism has
                // no bearing for.
                double3 axis = Vec.Unit(new double3(nod.X, nod.Y, nod.Z));
                double along = Vec.Len2(axis) < 0.5 ? 0.0 : Vec.Dot(axis, OpticGeometry.RestDirection);

                Assert.True(Math.Abs(along) < 1e-9,
                            $"the window twists {along:F9} about the roll axis at "
                            + $"aim {aim.X:F3},{aim.Y:F3},{aim.Z:F3}");
            }
        }
    }

    /// <summary>
    /// And the pair still lands the glass where it was told to look. The decomposition above is
    /// satisfiable by two bodies pointing at nothing in particular, so it is only worth having
    /// beside this.
    /// </summary>
    [Fact]
    public void TheGlassEndsUpOnTheAim()
    {
        OpticProfile p = Pod();

        foreach (MountFrame mount in new[] { MountFrame.Fixed, Tilted(1.3) })
        {
            foreach (double3 raw in Aims())
            {
                double3 aim = Vec.Unit(mount.Rotation * raw);
                double3 looked = OpticGeometry.Rotation(p, mount, aim) * OpticGeometry.RestDirection;

                // Seven places: an angle read back through acos amplifies the last bit of a dot
                // product to its square root, so a machine-epsilon dot is 1e-8 of angle.
                Assert.Equal(0.0, Vec.AngleBetween(looked, aim), 7);
            }
        }
    }

    /// <summary>
    /// The roll is measured against the mounting face, so a pod under a wing reads zero looking
    /// straight down — which is where it spends its life and where the shell should sit still.
    ///
    /// <para>The angle's sign is a handedness convention and worth nothing on its own. What is
    /// worth pinning is that the shell's <em>rotation</em> agrees with it: it must carry the
    /// mounting face's own direction onto the plane the nod happens in, or the reported angle and
    /// the drawn nose are two different numbers.</para>
    /// </summary>
    [Fact]
    public void TheShellRollsOntoThePlaneTheNodHappensIn()
    {
        OpticProfile p = Pod();

        Assert.Equal(0.0, OpticGeometry.RollAngleRad(MountFrame.Fixed, OpticGeometry.MountNormal), 9);

        foreach (MountFrame mount in new[] { MountFrame.Fixed, Tilted(0.7) })
        {
            foreach (double3 raw in Aims())
            {
                double3 aim = Vec.Unit(mount.Rotation * raw);

                double3 rolled = OpticGeometry.RollPose(p, mount, aim).Rotation
                                 * OpticGeometry.MountNormal;

                Assert.Equal(0.0, Vec.AngleBetween(rolled, Vec.RejectFrom(aim, mount.Forward)), 7);
            }
        }
    }

    /// <summary>
    /// The nod is never negative, which is what lets an asymmetric aperture work at all: the pod's
    /// recession is cut into one side of the shroud, and the roll is what puts that side on the
    /// target. A gimbal that nodded both ways would look out through the closed side half the time.
    /// </summary>
    [Fact]
    public void TheNodOnlyEverGoesOneWay()
    {
        OpticProfile p = Pod();

        foreach (double3 raw in Aims())
        {
            double3 aim = Vec.Unit(raw);

            doubleQuat shell = OpticGeometry.RollPose(p, MountFrame.Fixed, aim).Rotation;

            // The aim, seen from inside the shell. It must lie on the shell's own +X side --
            // the side the recession was clocked onto by the importer.
            double3 inShell = doubleQuat.Conjugate(shell) * aim;

            Assert.True(inShell.X > -1e-9,
                        $"the nod went to the closed side ({inShell.X:F6}) at "
                        + $"aim {aim.X:F3},{aim.Y:F3},{aim.Z:F3}");
        }
    }

    /// <summary>
    /// The hand controls drive one axis each: the nod tilts the ball and leaves the shell where it
    /// is.
    ///
    /// <para>A bearing-and-elevation pair cannot do that on this gimbal. The roll such a pair
    /// implies depends on <em>both</em> numbers, so tilting the sight turns the whole nose with it
    /// — one control moving two bodies, which is the thing an operator cannot work with.</para>
    /// </summary>
    [Fact]
    public void TheNodControlLeavesTheShellAlone()
    {
        OpticProfile p = Pod();

        foreach (MountFrame mount in new[] { MountFrame.Fixed, Tilted(1.1) })
        {
            foreach (double roll in new[] { -180.0, -75.0, 0.0, 40.0, 179.0 })
            {
                double? shell = null;

                foreach (double nod in new[] { 4.0, 30.0, 90.0, 120.0, 150.0 })
                {
                    double3 aim = OpticGeometry.ManualAim(p, mount, roll, nod);

                    // The nod reads back exactly, and the roll does not move with it.
                    Assert.Equal(nod, double.RadiansToDegrees(
                        OpticGeometry.OffBoresightRad(mount, aim)), 6);

                    double at = double.RadiansToDegrees(OpticGeometry.RollAngleRad(mount, aim));
                    shell ??= at;

                    // Wrapped, because a roll is an angle on a circle: atan2 answers +180 and
                    // -180 for one direction, and half a turn apart is the same shell pose.
                    double moved = (at - shell.Value + 540.0) % 360.0 - 180.0;

                    Assert.Equal(0.0, moved, 6);
                }
            }
        }
    }

    /// <summary>
    /// And every position on the sliders is reachable, so the travel clamp never moves a
    /// hand-driven command — which is what kept the controls and the ball disagreeing.
    /// </summary>
    [Fact]
    public void NothingTheHandControlsCanNameIsOutsideTheTravel()
    {
        OpticProfile p = Pod();
        var (first, second) = OpticGeometry.ManualRanges(p);

        for (double roll = first.Min; roll <= first.Max; roll += 15.0)
        {
            for (double nod = second.Min; nod <= second.Max; nod += 5.0)
            {
                double3 aim = OpticGeometry.ManualAim(p, MountFrame.Fixed, roll, nod);

                Assert.Equal(0.0, Vec.AngleBetween(aim, OpticGeometry.ClampToTravel(p, aim)), 7);
            }
        }
    }

    [Fact]
    public void TheCommandIsHeldOutOfTheKeyhole()
    {
        OpticProfile p = Pod();

        foreach (double off in new[] { 0.1, 1.0, 3.9 })
        {
            double o = double.DegreesToRadians(off);
            double3 aim = new(Math.Sin(o), Math.Cos(o), 0.0);

            double3 clamped = OpticGeometry.ClampToTravel(p, aim);

            Assert.Equal(float.DegreesToRadians(p.KeyholeDeg),
                         OpticGeometry.OffBoresightRad(MountFrame.Fixed, clamped), 9);

            // ...and it comes out on the same side, so a head held off the axis has not been
            // thrown to an arbitrary bearing on the way.
            Assert.True(clamped.X > 0.0 && Math.Abs(clamped.Z) < 1e-12);
        }
    }

    [Fact]
    public void TheNodStopsAtTheGimbalsOwnLimit()
    {
        OpticProfile p = Pod();

        // Straight aft-and-down, well past the stop.
        double3 clamped = OpticGeometry.ClampToTravel(p, Vec.Unit(new double3(0.5, -1.0, 0.0)));

        Assert.Equal(float.DegreesToRadians(p.MaxOffBoresightDeg),
                     OpticGeometry.OffBoresightRad(MountFrame.Fixed, clamped), 9);

        // The roll plane is kept: it stopped short in the same plane it was sent to, rather than
        // turning round to the nearest legal bearing.
        Assert.True(clamped.X > 0.0 && Math.Abs(clamped.Z) < 1e-12);
    }

    [Fact]
    public void EverythingInsideTheTravelIsLeftAlone()
    {
        OpticProfile p = Pod();

        foreach (double3 raw in Aims())
        {
            double3 aim = Vec.Unit(raw);

            Assert.Equal(0.0, Vec.AngleBetween(aim, OpticGeometry.ClampToTravel(p, aim)), 7);
        }
    }

    /// <summary>
    /// A pod parks looking out of its own mounting face, because dead ahead is its keyhole. The
    /// mast head keeps the old rest direction, which is along the host.
    /// </summary>
    [Fact]
    public void APodStowsLookingDownAndAMastHeadAlongTheHost()
    {
        Assert.Equal(0.0, Vec.AngleBetween(OpticGeometry.RestAim(Pod(), MountFrame.Fixed),
                                           OpticGeometry.MountNormal), 9);

        Assert.Equal(0.0, Vec.AngleBetween(OpticGeometry.RestAim(Mast(), MountFrame.Fixed),
                                           OpticGeometry.RestDirection), 9);
    }

    /// <summary>
    /// The travel limits are not shared vocabulary, and the case that shows it is the one a pod is
    /// for: looking very nearly straight out of the mounting face, which is straight down under a
    /// wing. A roll-nod head reaches it; a mast head's ceiling stops 5 degrees short, because a
    /// ball on a mast has nothing to roll about up there.
    /// </summary>
    [Fact]
    public void TheElevationBandDoesNotApplyToAPod()
    {
        // 89 degrees off the centreline: 89 degrees of elevation over the mounting face.
        double o = double.DegreesToRadians(89.0);
        double3 aim = new(Math.Sin(o), Math.Cos(o), 0.0);

        Assert.Equal(0.0, Vec.AngleBetween(aim, OpticGeometry.ClampToTravel(Pod(), aim)), 9);

        Assert.Equal(float.DegreesToRadians(Mast().MaxElevationDeg),
                     OpticGeometry.ElevationRad(OpticGeometry.ClampToTravel(Mast(), aim)), 9);
    }

    /// <summary>
    /// The mast head's roll is unchanged by any of this, which is what says the branch was added
    /// rather than the old behaviour rewritten.
    /// </summary>
    [Fact]
    public void AMastHeadStillLeansItsUpTowardsItsMountingFace()
    {
        OpticProfile p = Mast();

        foreach (double3 raw in Aims())
        {
            double3 aim = Vec.Unit(raw);

            // Straight along the mounting face's own normal there is no roll to prefer, and the
            // travel stops short of it -- see MaxElevationDeg.
            if (Math.Abs(Vec.Dot(aim, OpticGeometry.MountNormal)) > 0.999) continue;

            double3 up = OpticGeometry.Rotation(p, MountFrame.Fixed, aim) * OpticGeometry.MountNormal;

            Assert.True(Vec.Dot(up, Vec.Unit(Vec.RejectFrom(OpticGeometry.MountNormal, aim))) > 0.999);
        }
    }
}
