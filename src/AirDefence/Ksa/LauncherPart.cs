using Brutal.Numerics;
using KSA;

namespace AirDefence;

/// <summary>
/// Locates the launcher part on a vehicle and works out where its tubes are in the world.
///
/// The part itself (AirDefenceAssets.xml) is inert geometry - KSA sees a lump of
/// structure with mass and a collider. This class is the bridge: it finds that part on the
/// vehicle, and the battery mounts to it.
/// </summary>
internal static class LauncherPart
{
    /// <summary>Scale applied to a round that is not in the air. Small rather than zero: a
    /// zero-scaled transform is singular, and nothing good comes of handing one to a renderer.</summary>
    private static readonly double3 Hidden = new(1e-3, 1e-3, 1e-3);
    private static readonly double3 Shown = new(1.0, 1.0, 1.0);

    /// <summary>
    /// Marker for the round subparts, one per tube, flown by the mod. Unlike the moving
    /// assemblies this is a property of the *round*, so it comes off the munition profile.
    /// </summary>
    private const string MissileMarkerFallback = "Missile";

    /// <summary>
    /// Finds a launcher on a vehicle, or null if it carries none.
    ///
    /// <para>Matches against every launcher in <see cref="Arsenal"/> rather than one hardcoded
    /// Id, so adding a weapon system is a registry entry and needs no change here. Returns the
    /// first match: several launchers on one craft still give one battery, and sharing ammo
    /// between them would be a different feature.</para>
    /// </summary>
    public static (Part Part, LauncherProfile Profile)? Find(Vehicle vehicle)
    {
        try
        {
            ReadOnlySpan<Part> parts = vehicle.Parts.Parts;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] is { } part && Arsenal.LauncherForPart(part.Id) is { } profile)
                {
                    return (part, profile);
                }
            }
        }
        catch
        {
            // Part tree can be mid-rebuild during staging or docking.
        }
        return null;
    }

    public static bool IsMounted(Vehicle? vehicle) => vehicle is not null && Find(vehicle) is not null;

    /// <summary>
    /// The turret subpart of a launcher, or null if it cannot be found.
    ///
    /// KSA models subparts as <see cref="Part"/> objects in their own right, each with its own
    /// settable <c>Asmb2ParentAsmb</c> — so the turret can be slewed without splitting the
    /// launcher into two separate parts joined by a node.
    /// </summary>
    public static Part? FindTurret(Part launcher, LauncherProfile profile)
        => FindSubPart(launcher, profile.TurretMarker);

    /// <summary>The missile pods, which elevate on the turret's trunnions.</summary>
    public static Part? FindPods(Part launcher, LauncherProfile profile)
        => FindSubPart(launcher, profile.PodsMarker);

    /// <summary>The search array, which turns continuously.</summary>
    public static Part? FindRadar(Part launcher, LauncherProfile profile)
        => FindSubPart(launcher, profile.RadarMarker);

    /// <summary>
    /// Collects the round subparts, in declaration order, so tube N maps to the same body every
    /// time. There is one per tube, which is what lets a whole salvo be in the air at once.
    /// </summary>
    public static void FindMissiles(Part launcher, MunitionProfile munition, List<Part> into)
    {
        into.Clear();
        try
        {
            ReadOnlySpan<Part> subParts = launcher.SubParts;
            for (int i = 0; i < subParts.Length; i++)
            {
                if (subParts[i] is { } sub && sub.Id is { } id
                    && id.Contains(munition.BodyMarker ?? MissileMarkerFallback,
                                   StringComparison.OrdinalIgnoreCase))
                {
                    into.Add(sub);
                }
            }
        }
        catch
        {
            into.Clear();
        }
    }

    /// <summary>
    /// Where a tube's mouth is, in the launcher part's own frame, given where the pods are
    /// currently aimed.
    ///
    /// <see cref="MuzzleEcl"/> builds a ring about the boresight instead, which was a fair
    /// approximation while the launcher was a fixed bundle of tubes pointing up. It is not one
    /// now: the pods traverse and elevate, so the real mouths can be metres from that ring, and
    /// rounds appeared to leave from wherever the ring happened to be rather than from a tube.
    /// </summary>
    public static bool TryGetTubeMuzzlePartFrame(Part pods, LauncherProfile profile, int tubeIndex, out double3 partFrame)
    {
        partFrame = Vec.Zero;
        if (tubeIndex < 0 || tubeIndex >= profile.TubeCount) return false;

        try
        {
            partFrame = pods.PositionParentAsmb + pods.Asmb2ParentAsmb * profile.TubeOffsets[tubeIndex];
            return Vec.IsFinite(partFrame);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Which way the tubes point, in Ecl.
    ///
    /// In the pods' own frame the tubes lie at the elevation they were modelled at; the pods'
    /// transform then carries that through the launcher's current elevation and traverse.
    /// </summary>
    public static bool TryGetTubeAxisEcl(Vehicle platform, Part launcher, Part pods, LauncherProfile profile, out double3 axisEcl)
    {
        axisEcl = Vec.Zero;
        try
        {
            // Direction the tubes point in the pods' own frame, at the elevation they were
            // modelled at; the pods' transform then carries it through the current aim.
            double3 tubeAxisPodFrame = new(Math.Sin(profile.PodReferenceElevationRad),
                                           Math.Cos(profile.PodReferenceElevationRad), 0.0);
            double3 inPart = pods.Asmb2ParentAsmb * tubeAxisPodFrame;
            double3 inVehicle = launcher.Asmb2VehicleAsmb * inPart;
            axisEcl = Vec.Unit(platform.Asmb2Ego * inVehicle);
            return Vec.IsFinite(axisEcl) && !axisEcl.Equals(Vec.Zero);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The same point in Ecl, for the round the simulation actually flies.</summary>
    public static bool TryGetTubeMuzzleEcl(
        Vehicle platform, Part launcher, Part pods, LauncherProfile profile, int tubeIndex,
        double3 platformEcl, out double3 ecl)
    {
        ecl = Vec.Zero;
        if (!TryGetTubeMuzzlePartFrame(pods, profile, tubeIndex, out double3 partFrame)) return false;

        try
        {
            double3 inVehicle = launcher.PositionVehicleAsmb + launcher.Asmb2VehicleAsmb * partFrame;
            ecl = platformEcl + platform.Asmb2Ego * inVehicle;
            return Vec.IsFinite(ecl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Shrinks a round out of sight. Used for tubes that are loaded or already spent.</summary>
    public static void HideMissile(Part missile)
    {
        try
        {
            if (missile.Scale.Equals(Hidden)) return;      // already stowed; skip the cache reset
            missile.Scale = Hidden;
            missile.ResetCachedPosMatrixValues();
        }
        catch
        {
            // Nothing to do; a round that will not hide is cosmetic, not fatal.
        }
    }

    /// <summary>
    /// Puts a round body where its simulated round actually is, pointing the way it is going.
    ///
    /// <para><paramref name="offsetEcl"/> and <paramref name="directionEcl"/> come straight off
    /// the <see cref="Interceptor"/>: a platform-relative offset and an airspeed vector, both in
    /// Ecl. Two rotations take them into the subpart's frame — the vehicle's attitude, then the
    /// launcher part's own mounting — because <c>PositionParentAsmb</c> is measured in the
    /// parent part's frame, not the vehicle's.</para>
    ///
    /// <para>The mesh is modelled nose-along-+X, so the orientation is whatever rotation carries
    /// +X onto the flight direction.</para>
    /// </summary>
    public static bool TryPlaceMissile(
        Vehicle platform, Part launcher, Part missile,
        double3 launchAnchorPartFrame, double3 travelEcl, double3 directionEcl)
    {
        try
        {
            doubleQuat ecl2Asmb = doubleQuat.Conjugate(platform.Asmb2Ego);
            doubleQuat asmb2Part = doubleQuat.Conjugate(launcher.Asmb2VehicleAsmb);

            // Anchor to the tube it came out of and add how far it has travelled *since*.
            // Converting the round's absolute platform offset instead measures from the
            // platform's analytic orbit position, while a subpart is placed against the
            // vehicle's physics origin - and those two are metres apart on a landed craft.
            double3 position = launchAnchorPartFrame + asmb2Part * (ecl2Asmb * travelEcl);
            if (!Vec.IsFinite(position)) return false;

            doubleQuat rotation = FireGeometry.RotationFromNose(asmb2Part * (ecl2Asmb * directionEcl));

            missile.PositionParentAsmb = position;
            missile.PositionParentAsmbSafe = position;
            missile.Asmb2ParentAsmb = rotation;
            missile.Asmb2ParentAsmbSafe = rotation;
            missile.Scale = Shown;
            missile.ResetCachedPosMatrixValues();
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"round body: could not place ({e.GetType().Name}: {e.Message})");
            return false;
        }
    }


    private static Part? FindSubPart(Part launcher, string? marker)
    {
        if (string.IsNullOrEmpty(marker)) return null;

        try
        {
            ReadOnlySpan<Part> subParts = launcher.SubParts;
            for (int i = 0; i < subParts.Length; i++)
            {
                if (subParts[i] is { } sub && sub.Id is { } id
                    && id.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return sub;
                }
            }
        }
        catch
        {
            // Subpart list can be mid-rebuild during staging or docking.
        }
        return null;
    }

    /// <summary>Lists a launcher's subpart Ids, so a failed match can be diagnosed from the log.</summary>
    public static string DescribeSubParts(Part launcher)
    {
        try
        {
            ReadOnlySpan<Part> subParts = launcher.SubParts;
            if (subParts.Length == 0) return "(none)";

            var names = new List<string>(subParts.Length);
            for (int i = 0; i < subParts.Length; i++) names.Add(subParts[i]?.Id ?? "?");
            return string.Join(", ", names);
        }
        catch (Exception e)
        {
            return $"(unreadable: {e.GetType().Name})";
        }
    }

    /// <summary>
    /// Points the turret at <paramref name="bearingRad"/> about the part's X axis.
    ///
    /// <para>Writing the quaternion is not enough on its own — <see cref="Part"/> caches the
    /// matrices derived from it (<c>_matrixAsmb2Parent</c> and friends), so without
    /// <c>ResetCachedPosMatrixValues</c> the new orientation would be stored and then
    /// ignored.</para>
    ///
    /// <para>Both the plain and the <c>Safe</c> property are written. The naming suggests one is
    /// a snapshot for the render or physics thread to read without tearing, and it is cheaper to
    /// set both than to guess wrong and be left wondering why nothing moved.</para>
    /// </summary>
    /// <returns>False if KSA rejected the write; the caller should stop trying and say so.</returns>
    public static bool TryApplyTurretBearing(Part turret, double bearingRad)
    {
        try
        {
            doubleQuat rotation = doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), bearingRad);

            turret.Asmb2ParentAsmb = rotation;
            turret.Asmb2ParentAsmbSafe = rotation;
            turret.ResetCachedPosMatrixValues();
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"turret: could not write orientation ({e.GetType().Name}: {e.Message})");
            return false;
        }
    }

    /// <summary>
    /// Traverses and elevates the missile pods.
    ///
    /// <para>The pods are a <em>sibling</em> of the turret in the subpart list, not a child of
    /// it — KSA's asset XML places every SubPart against the Part, and there is no nesting. So
    /// the two rotations have to be composed here rather than inherited: the pods elevate about
    /// their own trunnion, and that whole assembly is then swung round by the turret's
    /// bearing.</para>
    ///
    /// <para>Because the pivot is offset from the turret's axis, the pods' <em>position</em>
    /// changes as the turret traverses. <c>PositionParentAsmb</c> is settable too, so that gets
    /// written each frame as well; leaving it alone would spin the pods on the spot while the
    /// turret they are supposed to sit on rotates away from underneath them.</para>
    /// </summary>
    public static bool TryApplyPodAim(Part pods, LauncherProfile profile, double bearingRad, double elevationRad)
    {
        try
        {
            doubleQuat traverse = doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), bearingRad);

            // Rotating about +Z by `a` takes elevation `e` to `e - a`, so reaching `elevation`
            // from the modelled pose is a rotation of (reference - elevation).
            doubleQuat elevate = doubleQuat.CreateFromAxisAngle(
                new double3(0, 0, 1), profile.PodReferenceElevationRad - elevationRad);

            // Elevation first, in the pods' own frame; then the turret's traverse.
            doubleQuat rotation = traverse * elevate;
            double3 position = profile.TurretPivot + traverse * profile.PodPivotFromTurret;

            pods.Asmb2ParentAsmb = rotation;
            pods.Asmb2ParentAsmbSafe = rotation;
            pods.PositionParentAsmb = position;
            pods.PositionParentAsmbSafe = position;
            pods.ResetCachedPosMatrixValues();
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"pods: could not write aim ({e.GetType().Name}: {e.Message})");
            return false;
        }
    }

    /// <summary>
    /// Turns the search array to <paramref name="spinRad"/> while it rides the turret.
    ///
    /// Both rotations are about the part's X axis — the turret's traverse and the array's own
    /// spin — so composing them is just adding the angles. The position still has to be
    /// rewritten, because the turntable sits well aft of the turret's axis and swings with it.
    /// </summary>
    public static bool TryApplyRadarSpin(Part radar, LauncherProfile profile, double turretBearingRad, double spinRad)
    {
        try
        {
            var axis = new double3(1, 0, 0);
            doubleQuat rotation = doubleQuat.CreateFromAxisAngle(axis, turretBearingRad + spinRad);
            doubleQuat traverse = doubleQuat.CreateFromAxisAngle(axis, turretBearingRad);
            double3 position = profile.TurretPivot + traverse * profile.RadarPivotFromTurret;

            radar.Asmb2ParentAsmb = rotation;
            radar.Asmb2ParentAsmbSafe = rotation;
            radar.PositionParentAsmb = position;
            radar.PositionParentAsmbSafe = position;
            radar.ResetCachedPosMatrixValues();
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"search array: could not write spin ({e.GetType().Name}: {e.Message})");
            return false;
        }
    }

    /// <summary>Rotates a direction in the part's frame back out into Ecl.</summary>
    public static bool TryDirectionFromPartFrame(Vehicle vehicle, double3 partFrame, out double3 directionEcl)
    {
        directionEcl = Vec.Zero;
        try
        {
            directionEcl = vehicle.Asmb2Ego * partFrame;
            return Vec.IsFinite(directionEcl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Rotates a world direction into the part's own frame, so it can be turned into a bearing.
    ///
    /// <c>Asmb2Ego</c> is the vehicle's assembly-to-render rotation, and Ego is a pure
    /// translation of Ecl — so for a *direction* the two frames are identical and this is exact.
    /// </summary>
    public static bool TryDirectionToPartFrame(Vehicle vehicle, double3 directionEcl, out double3 partFrame)
    {
        partFrame = Vec.Zero;
        try
        {
            partFrame = doubleQuat.Conjugate(vehicle.Asmb2Ego) * directionEcl;
            return Vec.IsFinite(partFrame);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Position of the launcher part in the ecliptic frame.
    ///
    /// Goes via the render frame deliberately: KSA offers <c>Part.PositionEgo</c> as a
    /// purpose-built helper, and Ego is a pure translation of Ecl, so a round trip through it
    /// is exact and avoids hand-rolling the assembly-to-world transform chain.
    ///
    /// Falls back to the vehicle origin when there is no camera (loading screens), which costs
    /// at most a couple of metres on a kilometre-scale engagement.
    /// </summary>
    public static double3 ResolveOriginEcl(Vehicle vehicle, Part? launcher)
    {
        double3 vehicleEcl = KsaWorld.PositionEcl(vehicle);
        if (launcher is null) return vehicleEcl;

        try
        {
            Camera camera = Program.GetMainCamera();
            if (camera is null) return vehicleEcl;

            double3 vehicleEgo = camera.EclToEgo(vehicleEcl);
            double4x4 asmb2Ego = vehicle.GetMatrixAsmb2Ego(vehicleEgo);
            double3 partEgo = launcher.PositionEgo(ref asmb2Ego);

            double3 partEcl = camera.EgoToEcl(partEgo);
            return Vec.IsFinite(partEcl) ? partEcl : vehicleEcl;
        }
        catch
        {
            return vehicleEcl;
        }
    }

    /// <summary>
    /// Muzzle position of one tube, in Ecl.
    ///
    /// The hexagon is laid out in world space about the boresight rather than being read out
    /// of the part's own rotation. The battery always points its rounds along the boresight,
    /// so building the ring around that axis keeps the tubes and the departing rounds
    /// consistent with each other.
    /// </summary>
    public static double3 MuzzleEcl(LauncherProfile profile, double3 originEcl, double3 boresight, int tubeIndex)
    {
        double3 u = Vec.AnyPerpendicular(boresight);
        double3 w = Vec.Cross(boresight, u);

        double angle = tubeIndex * (Math.Tau / profile.TubeCount);
        double3 ring = (u * Math.Cos(angle) + w * Math.Sin(angle)) * profile.TubeRingRadius;

        return originEcl + boresight * profile.MuzzleForwardOffset + ring;
    }

    /// <summary>
    /// Muzzle of each tube in the render frame, derived from the part's own transform.
    ///
    /// <see cref="MuzzleEcl"/> builds its ring from an arbitrary perpendicular of the boresight,
    /// which has no relation to how the part is actually mounted — the markers land on a ring of
    /// the right size, rotated by a random angle off the real tubes. Going through the part's
    /// assembly transform puts them on the tubes themselves.
    /// </summary>
    /// <returns>False if the transform is unavailable; the caller should skip drawing.</returns>
    public static bool TryGetTubeMuzzlesEgo(Vehicle platform, Part pods, LauncherProfile profile, double3 platformEgo, Span<double3> into)
    {
        if (into.Length < profile.TubeCount) return false;

        try
        {
            double4x4 asmb2Ego = platform.GetMatrixAsmb2Ego(platformEgo);

            for (int i = 0; i < profile.TubeCount; i++)
            {
                // Pod-local -> vehicle assembly -> render frame. The first hop carries the
                // launcher's current traverse and elevation, so these ride the tubes.
                double3 vehicleAsmb = pods.PositionVehicleAsmbOffset(profile.TubeOffsets[i]);
                double3 ego = vehicleAsmb.Transform(asmb2Ego);

                if (!Vec.IsFinite(ego)) return false;
                into[i] = ego;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
