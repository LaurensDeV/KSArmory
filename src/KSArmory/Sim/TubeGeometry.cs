using Brutal.Numerics;

namespace KSArmory;

/// <summary>Where a moving assembly sits and how it is turned, in its parent's frame.</summary>
public readonly record struct DrivePose(double3 Position, doubleQuat Rotation);

/// <summary>
/// The launcher's own geometry: where its tubes are, which way they point, and where its moving
/// assemblies sit once the drives have been laid.
///
/// <para>The caller resolves a subpart's position and rotation and passes them in; this decides
/// the geometry. Must stay free of KSA types. This file is what a second launcher rewrites — see
/// <c>docs/MODULARITY.md</c>.</para>
/// </summary>
public static class TubeGeometry
{
    /// <summary>
    /// The turret and search array traverse about the part's X axis; the pods elevate about its Z.
    /// Named rather than repeated as literals so a differently-built launcher changes them in one
    /// place.
    /// </summary>
    public static readonly double3 TraverseAxis = new(1, 0, 0);

    public static readonly double3 ElevationAxis = new(0, 0, 1);

    /// <summary>How far the turret has traversed, as a rotation in the part's frame.</summary>
    public static doubleQuat TurretRotation(double bearingRad)
        => doubleQuat.CreateFromAxisAngle(TraverseAxis, bearingRad);

    /// <summary>
    /// Which way the tubes point in the pods' own frame — the elevation the pods were modelled
    /// at, before any runtime aiming.
    /// </summary>
    public static double3 TubeAxisPodFrame(LauncherProfile profile)
        => new(Math.Sin(profile.PodReferenceElevationRad),
               Math.Cos(profile.PodReferenceElevationRad),
               0.0);

    /// <summary>The same direction carried through the pods' current traverse and elevation.</summary>
    public static double3 TubeAxisPartFrame(LauncherProfile profile, doubleQuat podRotation)
        => Vec.Unit(podRotation * TubeAxisPodFrame(profile));

    /// <summary>
    /// Which way <em>one</em> tube points in the pods' own frame. A tube with no direction of its
    /// own follows the pod axis, which is the parallel-bundle case the model generator emits.
    ///
    /// <para>Out-of-range indices fall back to the pod axis rather than throwing: a tube number
    /// comes from a magazine slot, and firing into empty air beats taking the game down.</para>
    /// </summary>
    public static double3 TubeAxisPodFrame(LauncherProfile profile, int tubeIndex)
    {
        if (tubeIndex < 0 || tubeIndex >= profile.TubeCount) return TubeAxisPodFrame(profile);

        Tube tube = profile.Tubes[tubeIndex];
        if (!tube.HasOwnDirection) return TubeAxisPodFrame(profile);

        double3 own = Vec.Unit(tube.Direction);
        return own.Equals(Vec.Zero) ? TubeAxisPodFrame(profile) : own;
    }

    /// <summary>One tube's direction, carried through the pods' current traverse and elevation.</summary>
    public static double3 TubeAxisPartFrame(LauncherProfile profile, doubleQuat podRotation, int tubeIndex)
        => Vec.Unit(podRotation * TubeAxisPodFrame(profile, tubeIndex));

    /// <summary>
    /// Where one tube's mouth sits in the launcher part's frame, given where the pods currently
    /// are. False for a tube this launcher does not have.
    /// </summary>
    public static bool TryMuzzlePartFrame(LauncherProfile profile, int tubeIndex,
                                          double3 podPosition, doubleQuat podRotation,
                                          out double3 partFrame)
    {
        partFrame = Vec.Zero;
        if (tubeIndex < 0 || tubeIndex >= profile.TubeCount) return false;

        partFrame = podPosition + podRotation * profile.Tubes[tubeIndex].Position;
        return Vec.IsFinite(partFrame);
    }

    /// <summary>
    /// The shortest rotation carrying one direction onto another.
    ///
    /// <para>The optical head points rather than trains, so unlike every other assembly here it
    /// has no axis of its own and takes an arbitrary rotation. Antiparallel is the case worth
    /// handling: the cross product vanishes and any perpendicular axis is equally correct, which
    /// is a half turn about whichever one is picked rather than a NaN.</para>
    /// </summary>
    public static doubleQuat RotationFromTo(double3 from, double3 to)
    {
        double3 a = Vec.Unit(from);
        double3 b = Vec.Unit(to);
        if (!Vec.IsFinite(a) || !Vec.IsFinite(b) || a.Equals(Vec.Zero) || b.Equals(Vec.Zero))
        {
            return doubleQuat.Identity;
        }

        double dot = Math.Clamp(Vec.Dot(a, b), -1.0, 1.0);
        if (dot > 1.0 - 1e-12) return doubleQuat.Identity;
        if (dot < -1.0 + 1e-12)
        {
            return doubleQuat.CreateFromAxisAngle(Vec.AnyPerpendicular(a), Math.PI);
        }

        return doubleQuat.CreateFromAxisAngle(Vec.Unit(Vec.Cross(a, b)), Math.Acos(dot));
    }

    /// <summary>
    /// Where one barrel's muzzle sits in the launcher part's frame, given where the cannon
    /// currently are. False for a barrel this launcher does not have.
    /// </summary>
    public static bool TryGunMuzzlePartFrame(LauncherProfile profile, int barrelIndex,
                                             double3 gunPosition, doubleQuat gunRotation,
                                             out double3 partFrame)
    {
        partFrame = Vec.Zero;
        if (barrelIndex < 0 || barrelIndex >= profile.GunMuzzles.Length) return false;

        partFrame = gunPosition + gunRotation * profile.GunMuzzles[barrelIndex];
        return Vec.IsFinite(partFrame);
    }

    /// <summary>
    /// Which way the barrels point in the cannon's own frame: the elevation they were modelled
    /// at, exactly as <see cref="TubeAxisPodFrame(LauncherProfile)"/> does for the tubes.
    /// </summary>
    public static double3 GunAxisGunFrame(LauncherProfile profile)
    {
        double reference = profile.GunReferenceElevationRad;
        return new double3(Math.Sin(reference), Math.Cos(reference), 0.0);
    }

    /// <summary>Which way the barrels point in the launcher part's frame.</summary>
    public static double3 GunAxisPartFrame(LauncherProfile profile, doubleQuat gunRotation)
        => Vec.Unit(gunRotation * GunAxisGunFrame(profile));

    /// <summary>
    /// Where a round's <em>centre</em> sits when seated. The body mesh is modelled about its
    /// centre, so half a body length back from the mouth puts the nose at the mouth.
    /// </summary>
    public static bool TrySeatedPartFrame(LauncherProfile profile, int tubeIndex,
                                          double3 podPosition, doubleQuat podRotation,
                                          double bodyLength, out double3 seated)
    {
        seated = Vec.Zero;
        if (!TryMuzzlePartFrame(profile, tubeIndex, podPosition, podRotation, out double3 muzzle)) return false;

        // This tube's own axis, not the pod's: a splayed round has to back into the tube it is
        // actually in, or it seats itself through the side of a neighbouring one.
        double3 axis = TubeAxisPartFrame(profile, podRotation, tubeIndex);
        if (axis.Equals(Vec.Zero)) return false;

        seated = muzzle - axis * (bodyLength * 0.5);
        return Vec.IsFinite(seated);
    }

    /// <summary>
    /// Where the pods sit and how they are turned, for a given aim.
    ///
    /// <para>Subparts do not nest in KSA, so the pods are a sibling of the turret and the two
    /// rotations are composed here rather than inherited. The trunnion is offset from the traverse
    /// axis, so the pods' <em>position</em> moves as the turret swings; leaving it fixed would spin
    /// them on the spot.</para>
    ///
    /// <para>Rotating about +Z by <c>a</c> takes elevation <c>e</c> to <c>e - a</c>, so reaching
    /// <paramref name="elevationRad"/> from the modelled pose is a rotation of
    /// <c>reference - elevation</c>, applied before the traverse.</para>
    /// </summary>
    public static DrivePose PodPose(LauncherProfile profile, double bearingRad, double elevationRad)
        => ElevatingPose(profile, profile.PodPivotFromTurret,
                         profile.PodReferenceElevationRad, bearingRad, elevationRad);

    /// <summary>
    /// Where the cannon sit and how they are pitched. Same drive as the pods on a different
    /// trunnion, so a launcher with both gets one implementation rather than two.
    /// </summary>
    public static DrivePose GunPose(LauncherProfile profile, double bearingRad, double elevationRad)
        => ElevatingPose(profile, profile.GunPivotFromTurret,
                         profile.GunReferenceElevationRad, bearingRad, elevationRad);

    /// <summary>
    /// An assembly that elevates about a trunnion offset from the traverse axis, then rides the
    /// turret round. Because the trunnion is offset, the position moves with the traverse and has
    /// to be rewritten too.
    /// </summary>
    public static DrivePose ElevatingPose(LauncherProfile profile, double3 pivotFromTurret,
                                          double referenceElevationRad,
                                          double bearingRad, double elevationRad)
    {
        doubleQuat traverse = TurretRotation(bearingRad);
        doubleQuat elevate = doubleQuat.CreateFromAxisAngle(
            ElevationAxis, referenceElevationRad - elevationRad);

        return new DrivePose(profile.TurretPivot + traverse * pivotFromTurret, traverse * elevate);
    }

    /// <summary>
    /// Where the search array sits and how far round it has turned.
    ///
    /// Both rotations are about the traverse axis — the turret's bearing and the array's own spin —
    /// so composing them is adding the angles. The position still moves, because the turntable
    /// sits well aft of the turret's axis and swings with it.
    /// </summary>
    public static DrivePose RadarPose(LauncherProfile profile, double bearingRad, double spinRad)
    {
        doubleQuat traverse = TurretRotation(bearingRad);

        return new DrivePose(profile.TurretPivot + traverse * profile.RadarPivotFromTurret,
                             TurretRotation(bearingRad + spinRad));
    }

    /// <summary>
    /// The direction a sensor's boresight names, in the launcher part's own frame. False for
    /// <see cref="BoresightMode.LocalUp"/>, which depends on where the parent body is and so is not
    /// a part-frame direction at all — the caller resolves that one.
    /// </summary>
    public static bool TryBoresightPartFrame(LauncherProfile profile, BoresightMode mode,
                                             double bearingRad, double elevationRad,
                                             out double3 partFrame)
    {
        switch (mode)
        {
            case BoresightMode.PartForward:
                partFrame = TraverseAxis;
                return true;

            case BoresightMode.TurretAxis:
                // Tube zero: a splayed bundle has no single axis to speak of.
                partFrame = TubeAxisPartFrame(profile, PodPose(profile, bearingRad, elevationRad).Rotation, 0);
                return !partFrame.Equals(Vec.Zero);

            default:
                partFrame = Vec.Zero;
                return false;
        }
    }

    /// <summary>
    /// Muzzle of one tube on a ring about the boresight, in Ecl. Fallback for a launcher with no
    /// pods subpart to read a transform off: the ring is built from an arbitrary perpendicular, so
    /// it is the right size but rotated by an arbitrary angle off the real tubes.
    /// </summary>
    public static double3 MuzzleRingEcl(LauncherProfile profile, double3 originEcl,
                                        double3 boresight, int tubeIndex)
    {
        double3 u = Vec.AnyPerpendicular(boresight);
        double3 w = Vec.Cross(boresight, u);

        double angle = tubeIndex * (Math.Tau / profile.TubeCount);
        double3 ring = (u * Math.Cos(angle) + w * Math.Sin(angle)) * profile.TubeRingRadius;

        return originEcl + boresight * profile.MuzzleForwardOffset + ring;
    }

    /// <summary>
    /// Where a round in flight belongs in the launcher part's frame: its tube anchor plus travel
    /// <em>since</em> launch. Not the absolute platform-relative offset — that is measured from the
    /// platform's analytic orbit position, while a subpart is placed against the vehicle's physics
    /// origin, and the two differ by metres on a landed craft.
    /// </summary>
    public static double3 BodyPositionPartFrame(double3 anchorPartFrame, double3 travelEcl,
                                                doubleQuat ecl2Asmb, doubleQuat asmb2Part)
        => anchorPartFrame + asmb2Part * (ecl2Asmb * travelEcl);

    /// <summary>
    /// Which way a round in flight points, in the launcher part's frame.
    /// <paramref name="directionEcl"/> must be the round's <em>local</em> velocity: Ecl velocity
    /// carries ~29.8 km/s of orbital motion and would point every round the same way.
    /// </summary>
    public static doubleQuat BodyRotationPartFrame(double3 directionEcl,
                                                   doubleQuat ecl2Asmb, doubleQuat asmb2Part)
        => FireGeometry.RotationFromNose(asmb2Part * (ecl2Asmb * directionEcl));

    /// <summary>
    /// Per-axis scale for a fin set. X is along the body, so length is untouched and Y and Z carry
    /// the span. Stowed is a small fraction rather than zero, which would be singular.
    /// </summary>
    public static double3 FinScale(MunitionProfile munition, double deployment)
    {
        double span = munition.FinStowedScale
                      + (1.0 - munition.FinStowedScale) * Math.Clamp(deployment, 0.0, 1.0);

        return new double3(1.0, span, span);
    }
}
