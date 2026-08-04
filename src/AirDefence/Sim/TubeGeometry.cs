using Brutal.Numerics;

namespace AirDefence;

/// <summary>Where a moving assembly sits and how it is turned, in its parent's frame.</summary>
public readonly record struct DrivePose(double3 Position, doubleQuat Rotation);

/// <summary>
/// The launcher's own geometry: where its tubes are, which way they point, and where its moving
/// assemblies sit once the drives have been laid.
///
/// <para>Split out of <see cref="LauncherPart"/> for the same reason <see cref="FireGeometry"/>
/// was — every function here was pure maths trapped behind a <c>Part</c> argument it only read two
/// properties off, which made the whole tube chain untestable. The caller resolves those two
/// properties and passes them in; this decides the geometry.</para>
///
/// <para>Must stay free of KSA types. See <c>docs/MODULARITY.md</c> for why this matters more than
/// it looks: this file is what a second launcher rewrites.</para>
/// </summary>
public static class TubeGeometry
{
    /// <summary>
    /// The turret and the search array both traverse about the part's X axis, and the pods
    /// elevate about its Z. Named rather than repeated as literals: a launcher built to different
    /// conventions changes these, and finding every <c>new double3(1, 0, 0)</c> by eye is how a
    /// traverse ends up composed against an elevation.
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
    /// Which way <em>one</em> tube points in the pods' own frame.
    ///
    /// <para>A tube with no direction of its own follows the pod axis, which is the parallel-bundle
    /// case and what the model generator emits. A tube that declares one uses it — that is what
    /// lets a splayed bundle, a VLS with divergence or an MLRS be expressed at all.</para>
    ///
    /// <para>Out-of-range indices fall back to the pod axis rather than throwing: a tube number is
    /// derived from a magazine slot, and a launcher that fires into empty air is a better failure
    /// than one that takes the game down.</para>
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
    /// Where a round's <em>centre</em> sits when seated in its tube.
    ///
    /// <para>The body mesh is modelled about its centre, so placing it at the mouth leaves half of
    /// it sticking out. Backing off half a body length puts the nose at the mouth and the rest
    /// inside, which is where a loaded round belongs and gives a launch something to emerge
    /// from.</para>
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
    /// <para>The pods are a <em>sibling</em> of the turret in KSA's subpart list, not a child of
    /// it, so the two rotations are composed here rather than inherited. And because the trunnion
    /// is offset from the traverse axis, the pods' <em>position</em> moves as the turret swings —
    /// leaving it alone spins them on the spot while the turret rotates out from under them.</para>
    ///
    /// <para>Rotating about +Z by <c>a</c> takes elevation <c>e</c> to <c>e - a</c>, so reaching
    /// <paramref name="elevationRad"/> from the modelled pose is a rotation of
    /// <c>reference - elevation</c>. Elevation applies first, in the pods' own frame; the turret's
    /// traverse then carries the whole assembly round.</para>
    /// </summary>
    public static DrivePose PodPose(LauncherProfile profile, double bearingRad, double elevationRad)
    {
        doubleQuat traverse = TurretRotation(bearingRad);
        doubleQuat elevate = doubleQuat.CreateFromAxisAngle(
            ElevationAxis, profile.PodReferenceElevationRad - elevationRad);

        return new DrivePose(profile.TurretPivot + traverse * profile.PodPivotFromTurret,
                             traverse * elevate);
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
    /// The direction a sensor's boresight names, in the launcher part's own frame.
    ///
    /// <para>False for <see cref="BoresightMode.LocalUp"/>, which is not a part-frame direction at
    /// all — it depends on where the parent body is, so the caller resolves it. Returning false
    /// rather than a guess keeps that distinction explicit: a mode that silently fell back to +X
    /// would leave a ground site searching whichever way the truck happened to be parked.</para>
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
                // Tube zero: the tubes are what the launcher is laid on, and a splayed bundle has
                // no single axis to speak of anyway.
                partFrame = TubeAxisPartFrame(profile, PodPose(profile, bearingRad, elevationRad).Rotation, 0);
                return !partFrame.Equals(Vec.Zero);

            default:
                partFrame = Vec.Zero;
                return false;
        }
    }

    /// <summary>
    /// Muzzle of one tube laid out on a ring about the boresight, in Ecl.
    ///
    /// <para><b>Fallback only</b>, for a launcher with no pods subpart to read a real transform
    /// off. The ring is built from an arbitrary perpendicular of the boresight, so it has no
    /// relation to how the part is actually mounted — the positions land on a ring of the right
    /// size, rotated by an arbitrary angle off the real tubes.</para>
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
    /// Where a round in flight belongs in the launcher part's frame.
    ///
    /// <para>Anchored to the tube it left plus how far it has flown <em>since</em>. The absolute
    /// platform-relative offset must not be used: it is measured from the platform's analytic
    /// orbit position, while a subpart is placed against the vehicle's physics origin, and those
    /// differ by metres on a landed craft.</para>
    /// </summary>
    public static double3 BodyPositionPartFrame(double3 anchorPartFrame, double3 travelEcl,
                                                doubleQuat ecl2Asmb, doubleQuat asmb2Part)
        => anchorPartFrame + asmb2Part * (ecl2Asmb * travelEcl);

    /// <summary>
    /// Which way a round in flight points, in the launcher part's frame.
    ///
    /// <para><paramref name="directionEcl"/> must be the round's <em>local</em> velocity. Ecl
    /// velocity carries ~29.8 km/s of the planet's orbital motion and would point every round the
    /// same way.</para>
    /// </summary>
    public static doubleQuat BodyRotationPartFrame(double3 directionEcl,
                                                   doubleQuat ecl2Asmb, doubleQuat asmb2Part)
        => FireGeometry.RotationFromNose(asmb2Part * (ecl2Asmb * directionEcl));

    /// <summary>
    /// Per-axis scale for a fin set at a given deployment.
    ///
    /// X is along the body, so length is untouched and the span is carried entirely by Y and Z.
    /// Stowed is a small fraction rather than zero: the fins have to clear the bore, and a
    /// zero-scaled transform is singular.
    /// </summary>
    public static double3 FinScale(MunitionProfile munition, double deployment)
    {
        double span = munition.FinStowedScale
                      + (1.0 - munition.FinStowedScale) * Math.Clamp(deployment, 0.0, 1.0);

        return new double3(1.0, span, span);
    }
}
