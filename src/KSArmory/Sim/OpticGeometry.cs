using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where the base a director is bolted to has ended up, in the part's frame.
///
/// <para><b>Read, never reconstructed.</b> Whatever moved the mount — a traverse, a hinge, an arm,
/// something not built yet — has already written its transform by the time a head is driven, so
/// the head asks where its base <em>is</em> rather than recomputing it from that mover's own
/// angles. A head that reconstructed the pose would have to know what kind of thing carried it,
/// and would work for exactly the one kind somebody taught it.</para>
///
/// <para><see cref="Fixed"/> is a director bolted to a hull: the base sits at the part origin and
/// never moves, which is what every question below reduces to when nothing turns underneath.</para>
/// </summary>
public readonly record struct MountFrame(double3 Position, doubleQuat Rotation)
{
    /// <summary>A mount that never moves — the part origin, unrotated.</summary>
    public static readonly MountFrame Fixed = new(Vec.Zero, doubleQuat.Identity);

    /// <summary>The surface's outward normal, which elevation and roll are both measured from.</summary>
    public double3 Normal => Rotation * OpticGeometry.MountNormal;

    /// <summary>
    /// Where the mount faces: the base's own forward, which for a pod is the centreline its outer
    /// gimbal rolls about and the axis its keyhole sits on.
    /// </summary>
    public double3 Forward => Rotation * OpticGeometry.RestDirection;

    /// <summary>A point on the mount, in the part's frame.</summary>
    public double3 ToPart(double3 onMount) => Position + Rotation * onMount;
}

/// <summary>
/// Where an optical head sits and how far it may look, measured against the base it is bolted to.
///
/// <para>Every question here is asked in the <see cref="MountFrame"/>'s terms, so a director on a
/// hull and one riding a turret are the same problem with a different mount. <see
/// cref="MountFrame.Fixed"/> is the hull case and reduces all of it to the constants.</para>
/// </summary>
public static class OpticGeometry
{
    /// <summary>
    /// The mount's own "up" before anything turns it: out of the surface the base is bolted to.
    /// Elevation is measured against it, so a director on a deck reads level at zero and a
    /// director on a hull's side reads level along that side — which is what the limits are
    /// quoted in. <see cref="MountFrame.Normal"/> is this carried onto where the base actually is.
    /// </summary>
    public static readonly double3 MountNormal = new(1, 0, 0);

    /// <summary>
    /// Where a head's mesh looks when it is not turned: the part's <c>+Y</c>, which is the face
    /// the window is modelled on. Every rotation written to the head is the shortest one carrying
    /// this onto the aim, so a model with its lens anywhere else arrives pointing sideways.
    /// </summary>
    public static readonly double3 RestDirection = new(0, 1, 0);

    /// <summary>The head's pose in the part's frame, given its mount and where it is looking.</summary>
    /// <remarks>
    /// The mount carries the <em>position</em> and nothing else. A head's rotation is written
    /// absolutely in the part's frame, so turning the base under a ball already pointed at
    /// something must not turn the ball with it — the aim is where it is looking, not an offset
    /// from its mount. Only the roll reference comes from the mount, through
    /// <see cref="RollReference"/>.
    /// </remarks>
    public static DrivePose Pose(OpticProfile profile, MountFrame mount, double3 aimPartFrame)
        => new(mount.ToPart(profile.HeadPivot), Rotation(profile, mount, aimPartFrame));

    /// <summary>The head's pose on a mount that never moves.</summary>
    public static DrivePose Pose(OpticProfile profile, double3 aimPartFrame)
        => Pose(profile, MountFrame.Fixed, aimPartFrame);

    /// <summary>
    /// The outer roll gimbal's pose: the shell a <see cref="GimbalKind.RollNod"/> head nods
    /// inside, turned about the mount's own centreline and nothing else.
    ///
    /// <para>It shares <see cref="OpticProfile.HeadPivot"/> with the head, because they turn on
    /// the same bearing — which is what makes the window stay flush in the shell at every nod.</para>
    /// </summary>
    public static DrivePose RollPose(OpticProfile profile, MountFrame mount, double3 aimPartFrame)
        => new(mount.ToPart(profile.HeadPivot),
               doubleQuat.CreateFromAxisAngle(mount.Forward, RollAngleRad(mount, aimPartFrame))
               * mount.Rotation);

    /// <summary>
    /// How far the outer gimbal has rolled (rad), measured about the mount's centreline from its
    /// normal — so a pod under a wing reads zero looking straight down.
    ///
    /// <para>Zero along the centreline itself, which is the keyhole: there is no nod plane there,
    /// so there is no roll angle to report. <see cref="OpticProfile.KeyholeDeg"/> is what keeps a
    /// commanded aim out of that cone; this only has to not produce a NaN when something else
    /// puts it there.</para>
    /// </summary>
    public static double RollAngleRad(MountFrame mount, double3 aimPartFrame)
    {
        double3 aim = Vec.Unit(aimPartFrame);
        if (Vec.Len2(aim) < 0.5) return 0.0;

        double3 axis = mount.Forward;
        double3 plane = Vec.RejectFrom(aim, axis);
        if (Vec.Len2(plane) < 1e-12) return 0.0;

        plane = Vec.Unit(plane);
        double3 reference = mount.Normal;

        return Math.Atan2(Vec.Dot(Vec.Cross(reference, plane), axis), Vec.Dot(reference, plane));
    }

    /// <summary>
    /// Which direction the head's own up is kept nearest, which is the whole difference between
    /// the two gimbals.
    ///
    /// <para>A mast head leans its up towards the surface it is bolted to, so a ball on a deck
    /// stays upright. A roll-nod head has no such freedom: its nod plane contains the pod's
    /// centreline by construction, so the head's up lies in that plane on the far side from where
    /// it started — which is what makes the nose a pure roll followed by a pure nod, and is why
    /// <see cref="RollPose"/> and <see cref="Rotation"/> agree about where the shell is.</para>
    /// </summary>
    public static double3 RollReference(OpticProfile profile, MountFrame mount)
        => profile.Gimbal == GimbalKind.RollNod ? -mount.Forward : mount.Normal;

    /// <summary>Where a head parks with nothing to look at.</summary>
    /// <remarks>
    /// <para>Along the host for a mast head, and out of the mounting face for a roll-nod one — a
    /// pod stows looking down, because along the host is exactly its keyhole.</para>
    ///
    /// <para>The mast head's is the part's own <see cref="RestDirection"/> rather than
    /// <see cref="MountFrame.Forward"/>, and the two differ on the one head that rides a traverse:
    /// the mount form would park the Pantsir's director along the <em>turret's</em> forward instead
    /// of the vehicle's. That may well be the better resting place, but it is a different
    /// behaviour and not one this decides.</para>
    /// </remarks>
    public static double3 RestAim(OpticProfile profile, MountFrame mount)
        => profile.Gimbal == GimbalKind.RollNod ? mount.Normal : RestDirection;

    /// <summary>
    /// Where the two hand controls end, in the head's own gimbal's terms.
    ///
    /// <para>Between them they can only name directions the head can actually reach, which is the
    /// point: the travel clamp then never moves a hand-driven command, so the sliders and the ball
    /// agree. Ends taken from the elevation band or from the nod's own stops, never from the other
    /// gimbal's vocabulary.</para>
    /// </summary>
    public static ((float Min, float Max) First, (float Min, float Max) Second) ManualRanges(
        OpticProfile profile)
        => profile.Gimbal == GimbalKind.RollNod
            ? ((-180f, 180f), (profile.KeyholeDeg, profile.MaxOffBoresightDeg))
            : ((-180f, 180f), (profile.MinElevationDeg, profile.MaxElevationDeg));

    /// <summary>
    /// The direction the two hand controls name.
    ///
    /// <para><b>Each gimbal is driven in its own terms, and that is not cosmetic.</b> A mast head
    /// takes a bearing and an elevation about its mounting face. A roll-nod head has neither: ask
    /// it for "bearing 180, elevation 17" and the direction that describes is 163° off the pod's
    /// centreline, past a 150° stop — so the clamp moves it and the ball ends up somewhere the
    /// controls do not say. Naming the roll and the nod instead makes every position on the
    /// sliders reachable, and makes them read the same as
    /// <see cref="RollAngleRad"/> and <see cref="OffBoresightRad"/> report.</para>
    /// </summary>
    public static double3 ManualAim(OpticProfile profile, MountFrame mount,
                                    double firstDeg, double secondDeg)
    {
        if (profile.Gimbal == GimbalKind.RollNod)
        {
            double roll = double.DegreesToRadians(firstDeg);
            double nod = double.DegreesToRadians(secondDeg);

            // The nod plane at that roll: the mount's normal turned about its centreline. Built
            // about Cross(Forward, Normal) so the angle reads back through RollAngleRad unchanged
            // — the two are one convention, and a sign flip here is a control that runs backwards.
            double3 across = mount.Normal * Math.Cos(roll)
                             + Vec.Cross(mount.Forward, mount.Normal) * Math.Sin(roll);

            return Vec.Unit(mount.Forward * Math.Cos(nod) + across * Math.Sin(nod));
        }

        // A mast head, in the part's own axes rather than the mount's. Unchanged: the two are the
        // same for a director bolted to a hull, and differ only on the one that rides a traverse —
        // where this would swing the hand controls by the turret's bearing.
        double bearing = double.DegreesToRadians(firstDeg);
        double elevation = double.DegreesToRadians(secondDeg);

        double flat = Math.Cos(elevation);

        return new double3(Math.Sin(elevation), flat * Math.Cos(bearing), flat * Math.Sin(bearing));
    }

    /// <summary>
    /// The head's rotation: <see cref="RestDirection"/> onto the aim, rolled so the ball's own up
    /// stays as near <see cref="RollReference"/> as it can.
    ///
    /// <para><b>The roll is why this is not a shortest-arc rotation.</b> Looking dead astern puts
    /// the aim exactly opposite the rest direction, where the shortest arc has no axis at all and
    /// any perpendicular is equally correct — so the one picked flips as the aim creeps past that
    /// point, and the head snaps through half a turn on the spot. Choosing the roll explicitly
    /// removes the choice, and with it the flip.</para>
    ///
    /// <para>It is also what makes a roll-nod nose a roll-nod nose. With the reference on the far
    /// side of the mount's centreline, this rotation is exactly <see cref="RollPose"/>'s roll
    /// followed by a tilt about an axis square to that centreline — the outer gimbal and the inner
    /// one, from one expression. Referencing the mounting face instead, as a mast head does, gives
    /// a head that arrives at the same bearing having twisted about the line of sight on the way,
    /// which no such nose can do.</para>
    ///
    /// <para>Degenerate only looking straight along the reference itself, where there is no roll
    /// to prefer. The travel limits stop short of it.</para>
    /// </summary>
    public static doubleQuat Rotation(OpticProfile profile, MountFrame mount, double3 aimPartFrame)
        => Rotation(RollReference(profile, mount), aimPartFrame);

    /// <summary>The rotation of a head whose up leans towards the mounting face — a mast head.</summary>
    public static doubleQuat Rotation(MountFrame mount, double3 aimPartFrame)
        => Rotation(mount.Normal, aimPartFrame);

    /// <summary>The same, given the direction the head's own up is to be kept nearest.</summary>
    public static doubleQuat Rotation(double3 rollReference, double3 aimPartFrame)
    {
        double3 aim = Vec.Unit(aimPartFrame);
        if (Vec.Len2(aim) < 0.5) return doubleQuat.Identity;

        doubleQuat swing = TubeGeometry.RotationFromTo(RestDirection, aim);

        // The reference is where the mounting face actually points now; the ball's own up is its
        // mesh axis, which the swing has already carried. Rotating the second by the mount as well
        // would roll the head by the traverse on top of the roll it was given.
        double3 wanted = Vec.RejectFrom(rollReference, aim);
        double3 have = Vec.RejectFrom(swing * MountNormal, aim);

        // Looking along the reference: nothing to roll about, so whatever the swing chose stands.
        if (Vec.Len2(wanted) < 1e-12 || Vec.Len2(have) < 1e-12) return swing;

        wanted = Vec.Unit(wanted);
        have = Vec.Unit(have);

        // About the aim, by a signed angle, rather than the shortest arc between the two. Both lie
        // in the plane across the aim, so the shortest arc is *usually* about the aim -- but at
        // exactly half a turn it has no axis and picks a perpendicular, which tips the aim itself
        // off target. Naming the axis is the difference between a roll and a wrecked bearing.
        double angle = Math.Atan2(Vec.Dot(Vec.Cross(have, wanted), aim), Vec.Dot(have, wanted));

        return doubleQuat.CreateFromAxisAngle(aim, angle) * swing;
    }

    /// <summary>The head's rotation on a mount that never moves.</summary>
    public static doubleQuat Rotation(double3 aimPartFrame)
        => Rotation(MountFrame.Fixed, aimPartFrame);

    /// <summary>
    /// Where the eye sits in the part's frame.
    ///
    /// <para>Along the aim, so it slides up and down the line of sight rather than off it. The
    /// bearing a head is commanded is measured from the <em>pivot</em> for that reason: the eye's
    /// offset has no perpendicular part to miss by.</para>
    /// </summary>
    public static double3 EyePartFrame(OpticProfile profile, MountFrame mount, double3 aimPartFrame)
    {
        double3 aim = Vec.Unit(aimPartFrame);
        double3 pivot = mount.ToPart(profile.HeadPivot);

        // The forward offset is along the aim, which is already a part-frame direction — so it is
        // added after the mount, not carried through it.
        return Vec.Len2(aim) < 0.5 ? pivot : pivot + aim * profile.EyeForward;
    }

    /// <summary>Where the eye sits on a mount that never moves.</summary>
    public static double3 EyePartFrame(OpticProfile profile, double3 aimPartFrame)
        => EyePartFrame(profile, MountFrame.Fixed, aimPartFrame);

    /// <summary>
    /// How far above the mounting plane a direction points (rad), positive away from the surface.
    /// </summary>
    public static double ElevationRad(MountFrame mount, double3 aimPartFrame)
    {
        double3 aim = Vec.Unit(aimPartFrame);
        if (Vec.Len2(aim) < 0.5) return 0.0;

        return Math.Asin(Math.Clamp(Vec.Dot(aim, mount.Normal), -1.0, 1.0));
    }

    /// <summary>Elevation above a mounting plane that never moves.</summary>
    public static double ElevationRad(double3 aimPartFrame)
        => ElevationRad(MountFrame.Fixed, aimPartFrame);

    /// <summary>
    /// How far off the mount's own centreline a direction points (rad). Zero is dead ahead, which
    /// for a roll-nod head is its keyhole, and a straight angle is dead astern.
    /// </summary>
    public static double OffBoresightRad(MountFrame mount, double3 aimPartFrame)
    {
        double3 aim = Vec.Unit(aimPartFrame);
        if (Vec.Len2(aim) < 0.5) return 0.0;

        return Math.Acos(Math.Clamp(Vec.Dot(aim, mount.Forward), -1.0, 1.0));
    }

    /// <summary>
    /// The nearest direction the head can actually look, given its travel.
    ///
    /// <para>The floor is not a preference. A director's window stands further off its pivot than
    /// its mast stops short of it, so a head pointed straight down puts its lens through its own
    /// mount — no arrangement of a ball on a mast sees past what holds it up. Clamping keeps the
    /// bearing and moves only the elevation, so a head told to look below the floor still turns
    /// the right way and stops at the lowest thing it can see.</para>
    ///
    /// <para>Falls back to the command unchanged when the aim is along the mount's own normal:
    /// straight up has no bearing to preserve, and inventing one would swing the head to an
    /// arbitrary compass point rather than leaving it where it is.</para>
    ///
    /// <para>A roll-nod head has neither an elevation floor nor a ceiling, and clamping it as if
    /// it did would be wrong in both directions at once: 360° of roll makes every bearing the
    /// same bearing, so what bounds it is the nod alone. See <see cref="ClampOffBoresight"/>.</para>
    /// </summary>
    public static double3 ClampToTravel(OpticProfile profile, MountFrame mount, double3 aimPartFrame)
    {
        if (profile.Gimbal == GimbalKind.RollNod)
        {
            return ClampOffBoresight(profile, mount, aimPartFrame);
        }

        double3 aim = Vec.Unit(aimPartFrame);
        if (Vec.Len2(aim) < 0.5) return aimPartFrame;

        double3 normal = mount.Normal;

        double elevation = ElevationRad(mount, aim);
        double min = float.DegreesToRadians(profile.MinElevationDeg);
        double max = float.DegreesToRadians(profile.MaxElevationDeg);

        double wanted = Math.Clamp(elevation, Math.Min(min, max), Math.Max(min, max));
        if (Math.Abs(wanted - elevation) < 1e-9) return aim;

        // The bearing, as a unit vector in the mounting plane. Zero length means the command was
        // along the normal itself, which has no bearing to keep.
        double3 across = Vec.RejectFrom(aim, normal);
        if (Vec.Len2(across) < 1e-12) return aim;

        return Vec.Unit(Vec.Unit(across) * Math.Cos(wanted) + normal * Math.Sin(wanted));
    }

    /// <summary>The travel limits of a head on a mount that never moves.</summary>
    public static double3 ClampToTravel(OpticProfile profile, double3 aimPartFrame)
        => ClampToTravel(profile, MountFrame.Fixed, aimPartFrame);

    /// <summary>
    /// A roll-nod head's travel: an annulus about the mount's centreline, bounded outward by
    /// <see cref="OpticProfile.MaxOffBoresightDeg"/> and inward by <see cref="OpticProfile.KeyholeDeg"/>.
    ///
    /// <para>Both ends are the mechanism rather than a preference. Outward it is the gimbal's own
    /// stop, or the nose's aperture where that is tighter; inward, the roll angle is undefined on
    /// the axis and the rate needed to hold a target near it grows without bound, so a command
    /// allowed in there is a nose that spins.</para>
    ///
    /// <para>The roll plane is kept and only the nod is moved, so a head told to look somewhere it
    /// cannot still turns the right way round and stops at the nearest thing it can see — the same
    /// rule the elevating clamp follows, about the other axis. On the axis itself there is no plane
    /// to keep, so the command stands: the drive is already looking dead ahead, and inventing a
    /// roll would throw the nose to an arbitrary one.</para>
    /// </summary>
    public static double3 ClampOffBoresight(OpticProfile profile, MountFrame mount,
                                            double3 aimPartFrame)
    {
        double3 aim = Vec.Unit(aimPartFrame);
        if (Vec.Len2(aim) < 0.5) return aimPartFrame;

        double3 axis = mount.Forward;

        double off = OffBoresightRad(mount, aim);
        double keyhole = Math.Max(0.0, float.DegreesToRadians(profile.KeyholeDeg));
        double reach = Math.Clamp(float.DegreesToRadians(profile.MaxOffBoresightDeg), keyhole, Math.PI);

        double wanted = Math.Clamp(off, keyhole, reach);
        if (Math.Abs(wanted - off) < 1e-9) return aim;

        double3 across = Vec.RejectFrom(aim, axis);
        if (Vec.Len2(across) < 1e-12) return aim;

        return Vec.Unit(Vec.Unit(across) * Math.Sin(wanted) + axis * Math.Cos(wanted));
    }
}
