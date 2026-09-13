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

    /// <summary>
    /// The host's long axis, which is where a rail points and the direction every tube is
    /// declared along. Perpendicular to <see cref="TraverseAxis"/>, and that is the whole
    /// distinction <see cref="BoresightMode.PartForward"/> and
    /// <see cref="BoresightMode.MountNormal"/> exist to keep apart.
    /// </summary>
    public static readonly double3 ForwardAxis = new(0, 1, 0);

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
    /// Where one tube's mouth sits from the mean of all of them, in the launcher part's frame.
    ///
    /// <para>The mean is what a release prediction flies, so this is the whole of what one round
    /// starts from that the prediction does not. Built in the part frame on purpose: turning it
    /// into the world is a rotation and carries none of the ecliptic's motion, where differencing
    /// two world positions would.</para>
    /// </summary>
    public static bool TryOffsetFromMeanPartFrame(LauncherProfile profile, int tubeIndex,
                                                  double3 podPosition, doubleQuat podRotation,
                                                  out double3 offset)
    {
        offset = Vec.Zero;
        if (!TryMuzzlePartFrame(profile, tubeIndex, podPosition, podRotation, out double3 mine)) return false;

        double3 sum = Vec.Zero;
        for (int tube = 0; tube < profile.TubeCount; tube++)
        {
            if (!TryMuzzlePartFrame(profile, tube, podPosition, podRotation, out double3 mouth)) return false;
            sum += mouth;
        }

        offset = mine - sum / profile.TubeCount;
        return Vec.IsFinite(offset);
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
    /// How far apart two muzzles can sit and still share one flash (m).
    ///
    /// <para>Comfortably above the spacing inside a barrel cluster and comfortably below the gap
    /// between two of them: a Phalanx's six sit within 0.2 m of each other, and a Pantsir's four
    /// are in two sponsons 3.5 m apart.</para>
    /// </summary>
    public const double GunFlashClusterMetres = 0.6;

    /// <summary>
    /// Groups muzzles that are close enough to share one flash, and returns how many groups.
    ///
    /// <para><b>Averaging every muzzle into one point is wrong for anything but a single cluster.</b>
    /// A rotary cannon's barrels sit within a hand's breadth, so their mean is on the gun. A mount
    /// with a sponson either side has its mean <em>between</em> them — on the vehicle's centreline,
    /// where there is no gun and nothing is firing.</para>
    ///
    /// <para>Single-link: a muzzle joins a group if it is within
    /// <see cref="GunFlashClusterMetres"/> of any muzzle already in it, so a row of barrels stays
    /// one group however long it is.</para>
    /// </summary>
    /// <param name="into">Filled with each muzzle's group index, in the muzzles' own order.</param>
    public static int ClusterMuzzles(ReadOnlySpan<double3> muzzles, double radius, Span<int> into)
    {
        int n = Math.Min(muzzles.Length, into.Length);
        if (n <= 0) return 0;

        for (int i = 0; i < n; i++) into[i] = -1;

        int groups = 0;
        for (int i = 0; i < n; i++)
        {
            if (into[i] >= 0) continue;

            into[i] = groups;

            // Grow the group until nothing else is within reach of anything already in it.
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int a = 0; a < n; a++)
                {
                    if (into[a] != groups) continue;

                    for (int b = 0; b < n; b++)
                    {
                        if (into[b] >= 0) continue;
                        if (Vec.Len(muzzles[b] - muzzles[a]) > radius) continue;

                        into[b] = groups;
                        grew = true;
                    }
                }
            }

            groups++;
        }

        return groups;
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
    /// A carried director's base, riding the traverse and nothing else.
    ///
    /// <para>The launcher's whole involvement with a director. The head above it is aimed by its
    /// own drive against this pose, read back off the engine rather than passed along — so the
    /// turret never learns it is carrying a sight, and the sight never learns it is on a
    /// turret.</para>
    /// </summary>
    public static DrivePose OpticBasePose(LauncherProfile profile, double bearingRad)
    {
        doubleQuat traverse = TurretRotation(bearingRad);

        return new DrivePose(profile.TurretPivot + traverse * profile.OpticBaseFromTurret,
                             traverse);
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
            // +Y, the axis a tube is declared along. Not TraverseAxis: that is +X, the mounting
            // face's normal, and a seeker given it searches square to the rail carrying it.
            case BoresightMode.PartForward:
                partFrame = ForwardAxis;
                return true;

            case BoresightMode.MountNormal:
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
                                                doubleQuat ecl2Asmb, doubleQuat asmb2Part,
                                                doubleQuat sinceLaunchAsmb)
        => CarryAnchor(anchorPartFrame, sinceLaunchAsmb, asmb2Part)
           + asmb2Part * (ecl2Asmb * travelEcl);

    /// <summary>
    /// The launch anchor as it stands now, after the craft has turned underneath it.
    ///
    /// <para><b>The anchor is a point in the world, written down in the part's frame.</b> The
    /// travel term is converted through the craft's <em>current</em> attitude every frame and so
    /// stays put; the anchor was not, so it rode the craft. Rolling the launcher then swung every
    /// round already in flight about the craft's own centre — on a stack that lever arm is the
    /// whole distance from the tube to the centre of mass, which is metres, not millimetres.</para>
    ///
    /// <para><paramref name="sinceLaunchAsmb"/> is <c>Conjugate(attitude now) * attitude at
    /// launch</c>: identity while the craft holds still, so a launcher that never turns is
    /// untouched by this and every round fired before it behaves exactly as it did.</para>
    /// </summary>
    public static double3 CarryAnchor(double3 anchorPartFrame, doubleQuat sinceLaunchAsmb,
                                      doubleQuat asmb2Part)
    {
        if (!IsRotation(sinceLaunchAsmb)) return anchorPartFrame;

        double3 carried = asmb2Part * (sinceLaunchAsmb * (doubleQuat.Conjugate(asmb2Part) * anchorPartFrame));
        return Vec.IsFinite(carried) ? carried : anchorPartFrame;
    }

    // A quaternion that is not unit length has never been set -- a round from before the field
    // existed -- so whatever it was going to carry stands as it is rather than being multiplied by
    // nonsense.
    private static bool IsRotation(doubleQuat q)
    {
        double norm = (q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z) + (q.W * q.W);
        return double.IsFinite(norm) && Math.Abs(norm - 1.0) <= 1e-6;
    }

    /// <summary>
    /// The attitude a round leaves at, body to ecliptic: as it sat in its tube, at the launcher's
    /// own roll.
    ///
    /// <para><b>A nose direction does not decide a rotation, and the leftover is the roll.</b>
    /// Swinging the mesh's nose onto the flight direction by the shortest arc leaves that roll to
    /// whatever the arc happens to give, which changes as the round noses over: 51° of roll over a
    /// 5 km drop, at 1–5°/s. Nothing aerodynamic is involved either way — <see cref="FinMixer"/> is
    /// drawn only. So the attitude lives <em>in the ecliptic</em>, starts here, and
    /// <see cref="BodyAttitude.Turn"/> carries it a frame's turn at a time, which adds no roll about
    /// the nose. Building it in the part frame instead glues the roll to the launching craft:
    /// measured at a degree of roll per degree the craft turns, on rounds that had already
    /// gone.</para>
    /// </summary>
    /// <param name="releaseHeadingEcl">What it left along, captured at launch.</param>
    /// <param name="launchAttitude">The platform's attitude when it left, as Asmb2Ecl. Unset falls
    /// back to the shortest arc onto the heading, which still points the nose correctly.</param>
    public static doubleQuat ReleaseAttitudeEcl(double3 releaseHeadingEcl, doubleQuat launchAttitude,
                                                doubleQuat asmb2Part)
    {
        if (!Vec.IsFinite(releaseHeadingEcl) || Vec.Len2(releaseHeadingEcl) < 1e-9) return doubleQuat.Identity;
        if (!IsRotation(launchAttitude)) return FireGeometry.RotationFromNose(releaseHeadingEcl);

        doubleQuat ecl2PartAtLaunch = asmb2Part * doubleQuat.Conjugate(launchAttitude);
        doubleQuat seated = FireGeometry.RotationFromNose(ecl2PartAtLaunch * releaseHeadingEcl);
        return doubleQuat.Conjugate(ecl2PartAtLaunch) * seated;
    }

    /// <summary>A body's ecliptic attitude, as the rotation its subpart is written with.</summary>
    public static doubleQuat BodyRotationPartFrame(doubleQuat attitudeEcl, doubleQuat ecl2Asmb,
                                                   doubleQuat asmb2Part)
        => asmb2Part * ecl2Asmb * attitudeEcl;

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
