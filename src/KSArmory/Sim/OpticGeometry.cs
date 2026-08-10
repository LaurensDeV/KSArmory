using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where a standalone optical head sits and how far it may look.
///
/// <para>Simpler than <see cref="TubeGeometry.OpticPose"/>, and that is the point: a head on a
/// launcher rides a traverse and has to compose with it, while a director is bolted straight to
/// the hull. Its pivot is a constant in the part's frame and nothing turns underneath it.</para>
/// </summary>
public static class OpticGeometry
{
    /// <summary>
    /// The part's own "up": out of the surface it is bolted to. Elevation is measured against it,
    /// so a director on a deck reads level at zero and a director on a hull's side reads level
    /// along that side — which is what the limits are quoted in.
    /// </summary>
    public static readonly double3 MountNormal = new(1, 0, 0);

    /// <summary>
    /// Where a head's mesh looks when it is not turned: the part's <c>+Y</c>, which is the face
    /// the window is modelled on. Every rotation written to the head is the shortest one carrying
    /// this onto the aim, so a model with its lens anywhere else arrives pointing sideways.
    /// </summary>
    public static readonly double3 RestDirection = new(0, 1, 0);

    /// <summary>The head's pose in the part's frame, given where it is looking.</summary>
    public static DrivePose Pose(OpticProfile profile, double3 aimPartFrame)
        => new(profile.HeadPivot, Rotation(aimPartFrame));

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
    public static doubleQuat Rotation(double3 aimPartFrame)
    {
        double3 aim = Vec.Unit(aimPartFrame);
        if (Vec.Len2(aim) < 0.5) return doubleQuat.Identity;

        doubleQuat swing = TubeGeometry.RotationFromTo(RestDirection, aim);

        double3 wanted = Vec.RejectFrom(MountNormal, aim);
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

    /// <summary>
    /// Where the eye sits in the part's frame.
    ///
    /// <para>Along the aim, so it slides up and down the line of sight rather than off it. The
    /// bearing a head is commanded is measured from the <em>pivot</em> for that reason: the eye's
    /// offset has no perpendicular part to miss by.</para>
    /// </summary>
    public static double3 EyePartFrame(OpticProfile profile, double3 aimPartFrame)
    {
        double3 aim = Vec.Unit(aimPartFrame);

        return Vec.Len2(aim) < 0.5
            ? profile.HeadPivot
            : profile.HeadPivot + aim * profile.EyeForward;
    }

    /// <summary>
    /// How far above the mounting plane a direction points (rad), positive away from the surface.
    /// </summary>
    public static double ElevationRad(double3 aimPartFrame)
    {
        double3 aim = Vec.Unit(aimPartFrame);
        if (Vec.Len2(aim) < 0.5) return 0.0;

        return Math.Asin(Math.Clamp(Vec.Dot(aim, MountNormal), -1.0, 1.0));
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
    /// </summary>
    public static double3 ClampToTravel(OpticProfile profile, double3 aimPartFrame)
    {
        double3 aim = Vec.Unit(aimPartFrame);
        if (Vec.Len2(aim) < 0.5) return aimPartFrame;

        double elevation = ElevationRad(aim);
        double min = float.DegreesToRadians(profile.MinElevationDeg);
        double max = float.DegreesToRadians(profile.MaxElevationDeg);

        double wanted = Math.Clamp(elevation, Math.Min(min, max), Math.Max(min, max));
        if (Math.Abs(wanted - elevation) < 1e-9) return aim;

        // The bearing, as a unit vector in the mounting plane. Zero length means the command was
        // along the normal itself, which has no bearing to keep.
        double3 across = Vec.RejectFrom(aim, MountNormal);
        if (Vec.Len2(across) < 1e-12) return aim;

        return Vec.Unit(Vec.Unit(across) * Math.Cos(wanted) + MountNormal * Math.Sin(wanted));
    }
}
