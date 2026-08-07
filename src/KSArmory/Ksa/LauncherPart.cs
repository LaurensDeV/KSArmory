using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Locates the launcher part on a vehicle and works out where its tubes are in the world.
///
/// The part itself (KSArmoryAssets.xml) is inert geometry - KSA sees a lump of
/// structure with mass and a collider. This class is the bridge: it finds that part on the
/// vehicle, and the battery mounts to it.
/// </summary>
internal static class LauncherPart
{
    // Scale applied to a round that is not in the air. Small rather than zero: a zero-scaled
    // transform is singular, and nothing good comes of handing one to a renderer.
    private static readonly double3 Hidden = new(1e-3, 1e-3, 1e-3);
    private static readonly double3 Shown = new(1.0, 1.0, 1.0);

    // Marker for the round subparts, one per tube, flown by the mod. Unlike the moving assemblies
    // this is a property of the *round*, so it comes off the munition profile.
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
    /// Every launcher on a vehicle, in part order, appended to <paramref name="into"/>. Part order
    /// rather than the <see cref="Part"/> reference is what a battery keys on: KSA rebuilds the
    /// part tree during staging and docking, and the ordinal survives that.
    /// </summary>
    public static void FindAll(Vehicle vehicle, List<(Part Part, LauncherProfile Profile)> into)
    {
        into.Clear();
        try
        {
            ReadOnlySpan<Part> parts = vehicle.Parts.Parts;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] is { } part && Arsenal.LauncherForPart(part.Id) is { } profile)
                {
                    into.Add((part, profile));
                }
            }
        }
        catch
        {
            // Part tree can be mid-rebuild during staging or docking.
        }
    }

    /// <summary>The nth launcher on a vehicle, or null once that many are no longer fitted.</summary>
    public static (Part Part, LauncherProfile Profile)? FindNth(Vehicle vehicle, int ordinal,
                                                                List<(Part, LauncherProfile)> scratch)
    {
        if (ordinal < 0) return null;

        FindAll(vehicle, scratch);
        return ordinal < scratch.Count ? scratch[ordinal] : null;
    }

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

    /// <summary>The cannon, which pitch on their own trunnion.</summary>
    public static Part? FindGuns(Part launcher, LauncherProfile profile)
        => FindSubPart(launcher, profile.GunsMarker);

    /// <summary>The optical head, which points wherever the battery is looking.</summary>
    public static Part? FindOptic(Part launcher, LauncherProfile profile)
        => FindSubPart(launcher, profile.OpticMarker);

    /// <summary>
    /// Collects the round subparts, in declaration order, so tube N maps to the same body every
    /// time. There is one per tube, which is what lets a whole salvo be in the air at once.
    /// </summary>
    /// <summary>Collects this round's fin subparts, in tube order. Empty if it has none.</summary>
    public static void FindFins(Part launcher, MunitionProfile munition, List<Part> into)
    {
        into.Clear();
        if (munition.FinMarker is not { } marker) return;

        try
        {
            ReadOnlySpan<Part> subParts = launcher.SubParts;
            for (int i = 0; i < subParts.Length; i++)
            {
                if (subParts[i] is { } sub && sub.Id is { } id
                    && id.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    into.Add(sub);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"fin subparts: {e.Message}");
        }
    }

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
    public static bool TryGetTubeMuzzlePartFrame(Part? pods, LauncherProfile profile, int tubeIndex, out double3 partFrame)
    {
        partFrame = Vec.Zero;
        try
        {
            return TubeGeometry.TryMuzzlePartFrame(profile, tubeIndex,
                                                   PodOffset(pods), PodRotation(pods),
                                                   out partFrame);
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
    // A launcher declaring no pods keeps its tubes in the part's own frame, so an absent assembly
    // contributes an identity rather than being a failure. Callers still have to separate that
    // from pods that were *declared* and not found -- see DefenceBattery.TubesResolved.
    private static doubleQuat PodRotation(Part? pods) => pods?.Asmb2ParentAsmb ?? doubleQuat.Identity;

    private static double3 PodOffset(Part? pods) => pods?.PositionParentAsmb ?? Vec.Zero;

    /// <summary>Direction the tubes point, in the launcher part's own frame.</summary>
    public static bool TryGetTubeAxisPartFrame(Part? pods, LauncherProfile profile, int tubeIndex, out double3 axis)
    {
        axis = Vec.Zero;
        try
        {
            axis = TubeGeometry.TubeAxisPartFrame(profile, PodRotation(pods), tubeIndex);
            return Vec.IsFinite(axis) && !axis.Equals(Vec.Zero);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where a round's <em>centre</em> sits when seated in its tube, in the part frame.
    ///
    /// <para>The body mesh is modelled about its centre, so placing it at the tube mouth leaves
    /// half of it sticking out. Backing off half a body length puts the nose at the mouth and
    /// the rest inside — which is where a loaded round belongs, and gives a launch something to
    /// emerge from.</para>
    /// </summary>
    public static bool TryGetSeatedPartFrame(Part? pods, LauncherProfile profile, int tubeIndex,
                                             double bodyLength, out double3 seated)
    {
        seated = Vec.Zero;
        try
        {
            return TubeGeometry.TrySeatedPartFrame(profile, tubeIndex,
                                                   PodOffset(pods), PodRotation(pods),
                                                   bodyLength, out seated);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Places a loaded round in its tube, at rest, with its fins stowed.</summary>
    public static bool TrySeatMissile(Part? pods, LauncherProfile profile, Part missile, Part? fins,
                                      int tubeIndex, MunitionProfile munition)
    {
        try
        {
            if (!TryGetSeatedPartFrame(pods, profile, tubeIndex, munition.BodyLength, out double3 seated)) return false;
            if (!TryGetTubeAxisPartFrame(pods, profile, tubeIndex, out double3 axis)) return false;

            doubleQuat rotation = FireGeometry.RotationFromNose(axis);

            missile.PositionParentAsmb = seated;
            missile.PositionParentAsmbSafe = seated;
            missile.Asmb2ParentAsmb = rotation;
            missile.Asmb2ParentAsmbSafe = rotation;
            missile.Scale = Shown;
            missile.ResetCachedPosMatrixValues();

            // Stowed: flat against the casing, so the round clears the bore.
            if (fins is not null) TryPlaceFins(fins, seated, rotation, 0.0, munition);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetTubeAxisEcl(Vehicle platform, Part launcher, Part? pods, LauncherProfile profile,
                                         int tubeIndex, out double3 axisEcl)
    {
        axisEcl = Vec.Zero;
        try
        {
            // Direction THIS tube points in the pods' own frame - its own if it declares one,
            // otherwise the elevation the pods were modelled at. The pods' transform then carries
            // it through the current aim.
            double3 inPart = PodRotation(pods) * TubeGeometry.TubeAxisPodFrame(profile, tubeIndex);
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
    /// <summary>
    /// Where a round's body is actually <em>drawn</em>, in Ecl.
    ///
    /// <para>Not <c>PlatformEcl + OffsetFromPlatform</c>, which is the same round measured from
    /// the platform's <em>analytic</em> orbit position. A body is placed against the vehicle's
    /// physics origin instead, and on a landed craft those are metres apart - so anything that has
    /// to sit visually on the round, rather than merely near it, has to be built the way the body
    /// is. See CLAUDE.md on anchoring to the tube.</para>
    /// </summary>
    public static bool TryGetBodyEcl(Vehicle platform, Part launcher,
                                     double3 launchAnchorPartFrame, double3 travelEcl,
                                     double3 platformEcl, out double3 ecl)
    {
        ecl = Vec.Zero;

        try
        {
            doubleQuat ecl2Asmb = doubleQuat.Conjugate(platform.Asmb2Ego);
            doubleQuat asmb2Part = doubleQuat.Conjugate(launcher.Asmb2VehicleAsmb);

            double3 partFrame = TubeGeometry.BodyPositionPartFrame(launchAnchorPartFrame, travelEcl,
                                                                   ecl2Asmb, asmb2Part);
            if (!Vec.IsFinite(partFrame)) return false;

            double3 inVehicle = launcher.PositionVehicleAsmb + (launcher.Asmb2VehicleAsmb * partFrame);
            ecl = platformEcl + (platform.Asmb2Ego * (inVehicle - platform.CenterOfMassAsmb));
            return Vec.IsFinite(ecl);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetTubeMuzzleEcl(
        Vehicle platform, Part launcher, Part? pods, LauncherProfile profile, int tubeIndex,
        double3 platformEcl, out double3 ecl)
    {
        ecl = Vec.Zero;
        if (!TryGetTubeMuzzlePartFrame(pods, profile, tubeIndex, out double3 partFrame)) return false;

        try
        {
            // Measured from the centre of mass, because that is what platformEcl is:
            // GetPositionEcl returns the centre of mass while PositionVehicleAsmb is from the
            // assembly origin, and adding one to the other is out by the whole offset.
            double3 inVehicle = launcher.PositionVehicleAsmb + launcher.Asmb2VehicleAsmb * partFrame;
            ecl = platformEcl + platform.Asmb2Ego * (inVehicle - platform.CenterOfMassAsmb);
            return Vec.IsFinite(ecl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where one cannon barrel's muzzle is in Ecl, and which way it points. Both come off the
    /// cannon subpart's live transform, so they follow the traverse and elevation the drives
    /// wrote this frame rather than the pose the mesh was modelled in.
    /// </summary>
    public static bool TryGetGunMuzzleEcl(
        Vehicle platform, Part launcher, Part guns, LauncherProfile profile, int barrelIndex,
        double3 platformEcl, out double3 ecl, out double3 axisEcl)
    {
        ecl = axisEcl = Vec.Zero;
        try
        {
            if (!TubeGeometry.TryGunMuzzlePartFrame(profile, barrelIndex, guns.PositionParentAsmb,
                                                    guns.Asmb2ParentAsmb, out double3 partFrame))
            {
                return false;
            }

            // Same centre-of-mass correction as the tubes: platformEcl is the centre of mass,
            // PositionVehicleAsmb is from the assembly origin.
            double3 inVehicle = launcher.PositionVehicleAsmb + launcher.Asmb2VehicleAsmb * partFrame;
            ecl = platformEcl + platform.Asmb2Ego * (inVehicle - platform.CenterOfMassAsmb);

            double3 axisPart = TubeGeometry.GunAxisPartFrame(profile, guns.Asmb2ParentAsmb);
            axisEcl = Vec.Unit(platform.Asmb2Ego * (launcher.Asmb2VehicleAsmb * axisPart));

            return Vec.IsFinite(ecl) && Vec.IsFinite(axisEcl) && !axisEcl.Equals(Vec.Zero);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Places a fin set on its round. Same position and rotation as the body, which shares its
    /// origin; the span is carried entirely by a radial scale.
    /// </summary>
    public static bool TryPlaceFins(Part fins, double3 position, doubleQuat rotation,
                                    double deployment, MunitionProfile munition)
    {
        try
        {
            // X is along the body, so length is untouched; Y and Z carry the span.
            double3 scale = TubeGeometry.FinScale(munition, deployment);
            if (!Vec.IsFinite(position) || !Vec.IsFinite(scale)) return false;

            fins.PositionParentAsmb = position;
            fins.PositionParentAsmbSafe = position;
            fins.Asmb2ParentAsmb = rotation;
            fins.Asmb2ParentAsmbSafe = rotation;
            fins.Scale = scale;
            fins.ResetCachedPosMatrixValues();
            return true;
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
        => TryPlaceMissile(platform, launcher, missile, launchAnchorPartFrame, travelEcl,
                           directionEcl, out _, out _);

    /// <summary>
    /// As above, and reports the transform it used so a fin set can be hung on the same one -
    /// the two meshes share an origin, so they must share a placement exactly or the fins swim.
    /// </summary>
    public static bool TryPlaceMissile(
        Vehicle platform, Part launcher, Part missile,
        double3 launchAnchorPartFrame, double3 travelEcl, double3 directionEcl,
        out double3 position, out doubleQuat rotation)
    {
        position = Vec.Zero;
        rotation = doubleQuat.Identity;

        try
        {
            doubleQuat ecl2Asmb = doubleQuat.Conjugate(platform.Asmb2Ego);
            doubleQuat asmb2Part = doubleQuat.Conjugate(launcher.Asmb2VehicleAsmb);

            // asmb2Part is currently identity - the launcher is mounted unrotated relative to the
            // vehicle assembly - but PositionParentAsmb is the assembly frame, so the conversion
            // is kept explicit rather than relying on that holding.
            position = TubeGeometry.BodyPositionPartFrame(launchAnchorPartFrame, travelEcl,
                                                          ecl2Asmb, asmb2Part);
            if (!Vec.IsFinite(position)) return false;

            rotation = TubeGeometry.BodyRotationPartFrame(directionEcl, ecl2Asmb, asmb2Part);

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
    /// <para><see cref="Part"/> caches the matrices derived from the quaternion, so without
    /// <c>ResetCachedPosMatrixValues</c> the new orientation is stored and ignored. Both the plain
    /// and <c>Safe</c> properties are written — one appears to be a snapshot for another thread,
    /// and setting both is cheaper than guessing wrong.</para>
    /// </summary>
    /// <returns>False if KSA rejected the write; the caller should stop trying and say so.</returns>
    public static bool TryApplyTurretBearing(Part turret, double bearingRad)
    {
        try
        {
            doubleQuat rotation = TubeGeometry.TurretRotation(bearingRad);

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
    /// Traverses and elevates the missile pods. Subparts do not nest in KSA, so the pods are a
    /// sibling of the turret and both the composed rotation and the position have to be written
    /// each frame — see <see cref="TubeGeometry.PodPose"/>.
    /// </summary>
    /// <summary>Pitches the cannon and carries them round with the turret.</summary>
    public static bool TryApplyGunAim(Part guns, LauncherProfile profile, double bearingRad, double elevationRad)
    {
        try
        {
            DrivePose pose = TubeGeometry.GunPose(profile, bearingRad, elevationRad);

            guns.Asmb2ParentAsmb = pose.Rotation;
            guns.Asmb2ParentAsmbSafe = pose.Rotation;
            guns.PositionParentAsmb = pose.Position;
            guns.PositionParentAsmbSafe = pose.Position;
            guns.ResetCachedPosMatrixValues();
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"guns: could not write aim ({e.GetType().Name}: {e.Message})");
            return false;
        }
    }

    public static bool TryApplyPodAim(Part pods, LauncherProfile profile, double bearingRad, double elevationRad)
    {
        try
        {
            DrivePose pose = TubeGeometry.PodPose(profile, bearingRad, elevationRad);
            (double3 position, doubleQuat rotation) = (pose.Position, pose.Rotation);

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

    /// <summary>Turns the search array to <paramref name="spinRad"/> while it rides the turret.</summary>
    public static bool TryApplyRadarSpin(Part radar, LauncherProfile profile, double turretBearingRad, double spinRad)
    {
        try
        {
            DrivePose pose = TubeGeometry.RadarPose(profile, turretBearingRad, spinRad);
            (double3 position, doubleQuat rotation) = (pose.Position, pose.Rotation);

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

    /// <summary>
    /// Points the optical head along a direction given in the launcher part's frame. Unlike the
    /// drives this is not an angle about an axis: the head has two degrees of freedom and takes
    /// whatever rotation carries its lens onto the aim.
    /// </summary>
    public static bool TryApplyOpticAim(Part optic, LauncherProfile profile,
                                        double turretBearingRad, double3 aimPartFrame)
    {
        try
        {
            DrivePose pose = TubeGeometry.OpticPose(profile, turretBearingRad, aimPartFrame);
            (double3 position, doubleQuat rotation) = (pose.Position, pose.Rotation);

            optic.Asmb2ParentAsmb = rotation;
            optic.Asmb2ParentAsmbSafe = rotation;
            optic.PositionParentAsmb = position;
            optic.PositionParentAsmbSafe = position;
            optic.ResetCachedPosMatrixValues();
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"optical head: could not write aim ({e.GetType().Name}: {e.Message})");
            return false;
        }
    }

    /// <summary>
    /// Where the optical head's eye sits in Ecl, and which way it is looking. Both come off the
    /// head's own aim rather than the turret's, so the view follows the sight and not the tubes.
    /// </summary>
    public static bool TryGetOpticViewEcl(Vehicle platform, Part launcher, LauncherProfile profile,
                                          double turretBearingRad, double3 aimPartFrame,
                                          double3 platformEcl,
                                          out double3 eyeEcl, out double3 forwardEcl)
    {
        eyeEcl = forwardEcl = Vec.Zero;
        try
        {
            DrivePose pose = TubeGeometry.OpticPose(profile, turretBearingRad, aimPartFrame);

            // Ahead of the ball's centre, along the way it is looking, or the view starts inside
            // the head's own mesh.
            double3 eyePartFrame = pose.Position
                                   + Vec.Unit(aimPartFrame) * profile.OpticEyeForward;

            // Same centre-of-mass correction as the tubes: platformEcl is the centre of mass and
            // PositionVehicleAsmb is from the assembly origin.
            double3 inVehicle = launcher.PositionVehicleAsmb + launcher.Asmb2VehicleAsmb * eyePartFrame;
            eyeEcl = platformEcl + platform.Asmb2Ego * (inVehicle - platform.CenterOfMassAsmb);

            return TryLauncherDirectionEcl(platform, launcher, aimPartFrame, out forwardEcl)
                   && Vec.IsFinite(eyeEcl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Rotates a direction in the <em>launcher part's</em> frame out into Ecl, through the part's
    /// mounting and then the vehicle's attitude. Distinct from
    /// The inverse of <see cref="TryDirectionToPartFrame"/>. Both apply the launcher's own
    /// mounting, and there is deliberately no variant that stops at the vehicle assembly frame:
    /// the two frames differ by the part's rotation, which is nothing on a surface mount and a
    /// half turn on a stack one, and anything reading a launcher direction through the wrong one
    /// points backwards without saying so.
    /// </summary>
    public static bool TryLauncherDirectionEcl(Vehicle platform, Part launcher, double3 partFrame,
                                               out double3 directionEcl)
    {
        directionEcl = Vec.Zero;
        try
        {
            double3 inVehicle = launcher.Asmb2VehicleAsmb * partFrame;
            directionEcl = Vec.Unit(platform.Asmb2Ego * inVehicle);
            return Vec.IsFinite(directionEcl) && !directionEcl.Equals(Vec.Zero);
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
    /// <param name="launcher">
    /// The launcher, so its own mounting within the vehicle is undone as well.
    ///
    /// <para>Without it this stops at the vehicle frame and hands <see cref="Turret.Track"/> a
    /// direction in the wrong axes — and that drive's axes are the launcher <em>part</em>'s. The
    /// reverse conversion, <see cref="TryLauncherDirectionEcl"/>, has always applied the mounting,
    /// so aim and boresight disagreed by exactly the part's rotation.</para>
    ///
    /// <para>Invisible while every launcher surface-attached unrotated. A stack-mounted CIWS
    /// carries a connector rotation of a half turn, which aimed its gun 180 degrees out — the
    /// failure docs/AUDIT-2026-08.md predicted and said to fix before a second mount was
    /// modelled. Null is tolerated and means the vehicle frame, which is what a caller with no
    /// launcher resolved yet can honestly ask for.</para>
    /// </param>
    public static bool TryDirectionToPartFrame(Vehicle vehicle, Part? launcher,
                                               double3 directionEcl, out double3 partFrame)
    {
        partFrame = Vec.Zero;
        try
        {
            double3 inVehicle = doubleQuat.Conjugate(vehicle.Asmb2Ego) * directionEcl;

            partFrame = launcher is null
                            ? inVehicle
                            : doubleQuat.Conjugate(launcher.Asmb2VehicleAsmb) * inVehicle;

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
        => TubeGeometry.MuzzleRingEcl(profile, originEcl, boresight, tubeIndex);

    /// <summary>
    /// Muzzle of each tube in the render frame, from the part's own transform — so unlike
    /// <see cref="MuzzleEcl"/> these land on the actual tubes.
    /// </summary>
    /// <param name="carrier">
    /// The assembly the tubes ride: the pods, or the launcher part itself when the profile
    /// declares none. Unlike the seating path this works straight in vehicle-assembly
    /// coordinates, so an absent assembly cannot be stood in for with an identity - there has to
    /// be a real part to measure the offset from.
    /// </param>
    /// <returns>False if the transform is unavailable; the caller should skip drawing.</returns>
    public static bool TryGetTubeMuzzlesEgo(Vehicle platform, Part? carrier, LauncherProfile profile, double3 platformEgo, Span<double3> into)
    {
        if (into.Length < profile.TubeCount || carrier is null) return false;

        try
        {
            double4x4 asmb2Ego = platform.GetMatrixAsmb2Ego(platformEgo);

            for (int i = 0; i < profile.TubeCount; i++)
            {
                // Pod-local -> vehicle assembly -> render frame. The first hop carries the
                // launcher's current traverse and elevation, so these ride the tubes.
                double3 vehicleAsmb = carrier.PositionVehicleAsmbOffset(profile.Tubes[i].Position);
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
