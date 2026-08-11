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
    /// <see cref="MountFrame.Normal"/>.
    /// </remarks>
    public static DrivePose Pose(OpticProfile profile, MountFrame mount, double3 aimPartFrame)
        => new(mount.ToPart(profile.HeadPivot), Rotation(mount, aimPartFrame));

    /// <summary>The head's pose on a mount that never moves.</summary>
    public static DrivePose Pose(OpticProfile profile, double3 aimPartFrame)
        => Pose(profile, MountFrame.Fixed, aimPartFrame);

    /// <summary>
    /// The head's rotation: <see cref="RestDirection"/> onto the aim, rolled so the ball's own up
    /// stays as near <see cref="MountNormal"/> as it can.
    ///
    /// <para><b>The roll is why this is not a shortest-arc rotation.</b> Looking dead astern puts
    /// the aim exactly opposite the rest direction, where the shortest arc has no axis at all and
    /// any perpendicular is equally correct — so the one picked flips as the aim creeps past that
    /// point, and the head snaps through half a turn on the spot. Choosing the roll explicitly
    /// removes the choice, and with it the flip.</para>
    ///
    /// <para>Degenerate only looking straight along the mount's own normal, where there is no roll
    /// to prefer. The travel limits stop short of it.</para>
    /// </summary>
    public static doubleQuat Rotation(MountFrame mount, double3 aimPartFrame)
    {
        double3 aim = Vec.Unit(aimPartFrame);
        if (Vec.Len2(aim) < 0.5) return doubleQuat.Identity;

        doubleQuat swing = TubeGeometry.RotationFromTo(RestDirection, aim);

        // The reference is where the mounting face actually points now; the ball's own up is its
        // mesh axis, which the swing has already carried. Rotating the second by the mount as well
        // would roll the head by the traverse on top of the roll it was given.
        double3 wanted = Vec.RejectFrom(mount.Normal, aim);
        double3 have = Vec.RejectFrom(swing * MountNormal, aim);

        // Looking along the normal: nothing to roll about, so whatever the swing chose stands.
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
    /// </summary>
    public static double3 ClampToTravel(OpticProfile profile, MountFrame mount, double3 aimPartFrame)
    {
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
}
