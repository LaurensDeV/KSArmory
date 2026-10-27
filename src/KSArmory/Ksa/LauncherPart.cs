using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Locates the launcher part on a vehicle and works out where its tubes are in the world.
///
/// The part itself is inert to KSA, structure with mass and a collider; the system mounts to it.
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
    /// <para>Matches against every launcher in <see cref="Catalogue"/>, stopping at the first; a
    /// system resolves its own launcher through <see cref="FindNth"/>.</para>
    /// </summary>
    public static (Part Part, LauncherProfile Profile)? Find(Vehicle vehicle)
    {
        try
        {
            ReadOnlySpan<Part> parts = vehicle.Parts.Parts;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] is { } part && Catalogue.LauncherForPart(part.Id) is { } profile)
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
    /// Every launcher on a vehicle, in part order, appended to <paramref name="into"/>. A system holds
    /// its part by reference once it has found it (<see cref="WeaponSystem.HeldPart"/>): KSA moves
    /// parts whole through a split and a dock, and the ordinal is what renumbers.
    /// </summary>
    public static void FindAll(Vehicle vehicle, List<(Part Part, LauncherProfile Profile)> into)
    {
        into.Clear();
        try
        {
            ReadOnlySpan<Part> parts = vehicle.Parts.Parts;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] is { } part && Catalogue.LauncherForPart(part.Id) is { } profile)
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

    /// <summary>
    /// What a launcher's settings are filed under: its <paramref name="ordinal"/>, moved past any switched-off
    /// family's launchers before <paramref name="part"/> on the craft (<see cref="SettingsOrdinal"/>).
    /// </summary>
    public static int FiledOrdinal(Vehicle vehicle, Part part, int ordinal)
    {
        if (Catalogue.DisabledParts.Count == 0) return ordinal;

        try
        {
            ReadOnlySpan<Part> parts = vehicle.Parts.Parts;
            List<string> ids = new(parts.Length);
            int at = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                if (ReferenceEquals(parts[i], part)) at = i;
                ids.Add(parts[i]?.Id ?? "");
            }

            return at < 0 ? ordinal : SettingsOrdinal.Of(ordinal, ids, at, Catalogue.IsDisabledLauncher);
        }
        catch
        {
            return ordinal;
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

    /// <summary>A barrel that recoils inside the cannon. Null for a launcher with none.</summary>
    public static Part? FindBarrel(Part launcher, LauncherProfile profile)
        => FindSubPart(launcher, profile.GunBarrelMarker);

    /// <summary>A carried director's base, which rides the traverse. Null for a launcher with none.</summary>
    public static Part? FindOpticBase(Part launcher, LauncherProfile profile)
        => FindSubPart(launcher, profile.OpticBaseMarker);

    /// <summary>The cannon actuator's cylinder, rod and feed. Null for each the launcher has none of.</summary>
    public static Part? FindGunCylinder(Part launcher, LauncherProfile profile)
        => FindSubPart(launcher, profile.GunCylinderMarker);
    public static Part? FindGunRod(Part launcher, LauncherProfile profile)
        => FindSubPart(launcher, profile.GunRodMarker);
    public static Part? FindGunFeed(Part launcher, LauncherProfile profile)
        => FindSubPart(launcher, profile.GunFeedMarker);
    public static Part? FindGunRotor(Part launcher, LauncherProfile profile)
        => FindSubPart(launcher, profile.GunRotorMarker);

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
    /// <see cref="MuzzleEcl"/> builds a ring about the boresight instead, which coincides with the
    /// mouths only on a fixed bundle of tubes.
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
    // from pods that were *declared* and not found -- see WeaponSystem.TubesResolved.
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
    /// <para>The body mesh is modelled about its centre, so half a body length back puts the nose
    /// at the mouth.</para>
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

            // Stowed: flat against the casing, so the round clears the bore. A hinged set has no
            // stowed state and four blades rather than one, so the caller places those itself —
            // scaling blade zero to nothing here would leave one missing on the rack.
            if (fins is not null && munition.FinsPerRound == 0)
                TryPlaceFins(fins, seated, rotation, 0.0, munition);
            return true;
        }
        catch (Exception e)
        {
            // Logged once per launcher: silent, it reads as a round that is not there.
            if (_seatFailure.Add(profile.PartId))
                Log.Warn($"seating tube {tubeIndex} of {profile.PartId} failed: {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    // Launchers already complained about, so a per-frame failure is not a per-frame log.
    private static readonly HashSet<string> _seatFailure = [];

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

    /// <summary>
    /// Where a round's body is actually <em>drawn</em>, in Ecl.
    ///
    /// <para>Not <c>PlatformEcl + OffsetFromPlatform</c>, which is measured from the platform's
    /// analytic position; a body is placed against the physics origin, metres apart on a landed
    /// craft. See CLAUDE.md on anchoring to the tube.</para>
    /// </summary>
    public static bool TryGetBodyEcl(Vehicle platform, Part launcher,
                                     double3 launchAnchorPartFrame, double3 travelEcl,
                                     double3 platformEcl, doubleQuat launchAttitude,
                                     out double3 ecl)
    {
        ecl = Vec.Zero;

        try
        {
            doubleQuat ecl2Asmb = doubleQuat.Conjugate(platform.Asmb2Ego);
            doubleQuat asmb2Part = doubleQuat.Conjugate(launcher.Asmb2VehicleAsmb);

            double3 partFrame = TubeGeometry.BodyPositionPartFrame(
                launchAnchorPartFrame, travelEcl, ecl2Asmb, asmb2Part,
                doubleQuat.Concatenate(launchAttitude, ecl2Asmb));
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

    /// <summary>
    /// Where one tube's mouth sits from the mean of all of them, turned into Ecl.
    ///
    /// <para>No position enters it — only the part frame and two rotations — so it pairs with any
    /// sample of the craft taken this frame, where differencing two mouths' world positions would
    /// carry whatever separates the instants they were read at.</para>
    /// </summary>
    public static bool TryGetTubeOffsetFromMeanEcl(Vehicle platform, Part launcher, Part? pods,
                                                   LauncherProfile profile, int tubeIndex,
                                                   out double3 offsetEcl)
    {
        offsetEcl = Vec.Zero;

        try
        {
            if (!TubeGeometry.TryOffsetFromMeanPartFrame(profile, tubeIndex, PodOffset(pods),
                                                         PodRotation(pods), out double3 inPart))
            {
                return false;
            }

            offsetEcl = platform.Asmb2Ego * (launcher.Asmb2VehicleAsmb * inPart);
            return Vec.IsFinite(offsetEcl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The same point as <see cref="TryGetTubeMuzzlePartFrame"/> in Ecl, for the round the
    /// simulation actually flies.
    /// </summary>
    public static bool TryGetTubeMuzzleEcl(
        Vehicle platform, Part launcher, Part? pods, LauncherProfile profile, int tubeIndex,
        double3 platformEcl, out double3 ecl)
    {
        ecl = Vec.Zero;
        if (!TryGetTubeMuzzleVehicleAsmb(launcher, pods, profile, tubeIndex, out double3 inVehicle)) return false;

        try
        {
            // platformEcl is the centre of mass; PositionVehicleAsmb is from the assembly origin.
            ecl = platformEcl + platform.Asmb2Ego * (inVehicle - platform.CenterOfMassAsmb);
            return Vec.IsFinite(ecl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where one tube's mouth sits in the vehicle's own assembly frame, which every placement of it in
    /// the world is turned and offset from.
    /// </summary>
    public static bool TryGetTubeMuzzleVehicleAsmb(Part launcher, Part? pods, LauncherProfile profile,
                                                   int tubeIndex, out double3 vehicleAsmb)
    {
        vehicleAsmb = Vec.Zero;
        if (!TryGetTubeMuzzlePartFrame(pods, profile, tubeIndex, out double3 partFrame)) return false;

        try
        {
            vehicleAsmb = launcher.PositionVehicleAsmb + launcher.Asmb2VehicleAsmb * partFrame;
            return Vec.IsFinite(vehicleAsmb);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// How fast one tube's mouth moves because the craft is turning, and the arm it swings on — off
    /// the assembly frame, as <see cref="FireGeometry.SpinVelocityAt"/> takes them.
    /// </summary>
    public static bool TryGetTubeSpinEcl(Vehicle platform, Part launcher, Part? pods, LauncherProfile profile,
                                         int tubeIndex, double3 angularVelocityEcl,
                                         out double3 spinEcl, out double3 armEcl)
    {
        spinEcl = armEcl = Vec.Zero;
        if (!TryGetTubeMuzzleVehicleAsmb(launcher, pods, profile, tubeIndex, out double3 mouth)) return false;

        try
        {
            doubleQuat asmb2Ecl = platform.Asmb2Ego;
            double3 centreOfMass = platform.CenterOfMassAsmb;

            armEcl = FireGeometry.LeverArm(asmb2Ecl, mouth, centreOfMass);
            spinEcl = FireGeometry.SpinVelocityAt(angularVelocityEcl, asmb2Ecl, mouth, centreOfMass);
            return Vec.IsFinite(armEcl) && Vec.IsFinite(spinEcl);
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

    /// <summary>
    /// Places one hinged blade of a cruciform set on its round.
    ///
    /// <para>The blade's mesh is recentred on its hinge, so its origin rides the body axis at the
    /// hinge station and its rotation is the hinge. Composed in that order: deflect about the
    /// blade's own radial axis first, then roll it into position, then carry both through the
    /// body's attitude. Rolling first would deflect it about somebody else's hinge.</para>
    /// </summary>
    public static bool TryPlaceFin(Part fin, double3 bodyPos, doubleQuat bodyRot,
                                   double hingeStationPartFrame, double rollRad, double deflectRad)
    {
        try
        {
            doubleQuat local = doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), rollRad)
                               * doubleQuat.CreateFromAxisAngle(new double3(0, 1, 0), deflectRad);
            doubleQuat rotation = bodyRot * local;
            double3 position = bodyPos + bodyRot * new double3(hingeStationPartFrame, 0, 0);

            if (!Vec.IsFinite(position)) return false;

            fin.PositionParentAsmb = position;
            fin.PositionParentAsmbSafe = position;
            fin.Asmb2ParentAsmb = rotation;
            fin.Asmb2ParentAsmbSafe = rotation;
            fin.Scale = Shown;
            fin.ResetCachedPosMatrixValues();
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
    /// Puts a round body where its simulated round actually is, at the attitude it is drawn at.
    ///
    /// <para><paramref name="travelEcl"/> comes straight off the round and
    /// <paramref name="attitudeEcl"/> off <see cref="BodyAttitude.Turn"/>, both in Ecl. Two
    /// rotations take them into the subpart's frame — the vehicle's attitude, then the launcher
    /// part's own mounting — because <c>PositionParentAsmb</c> is measured in the parent part's
    /// frame, not the vehicle's.</para>
    /// </summary>
    public static bool TryPlaceMissile(
        Vehicle platform, Part launcher, Part missile,
        double3 launchAnchorPartFrame, double3 travelEcl, doubleQuat attitudeEcl,
        doubleQuat launchAttitude)
        => TryPlaceMissile(platform, launcher, missile, launchAnchorPartFrame, travelEcl,
                           attitudeEcl, launchAttitude, out _, out _);

    /// <summary>
    /// As above, and reports the transform it used so a fin set can be hung on the same one -
    /// the two meshes share an origin, so they must share a placement exactly or the fins swim.
    /// </summary>
    public static bool TryPlaceMissile(
        Vehicle platform, Part launcher, Part missile,
        double3 launchAnchorPartFrame, double3 travelEcl, doubleQuat attitudeEcl,
        doubleQuat launchAttitude,
        out double3 position, out doubleQuat rotation)
    {
        position = Vec.Zero;
        rotation = doubleQuat.Identity;

        try
        {
            doubleQuat ecl2Asmb = doubleQuat.Conjugate(platform.Asmb2Ego);
            doubleQuat asmb2Part = doubleQuat.Conjugate(launcher.Asmb2VehicleAsmb);

            // asmb2Part is the launcher's own mounting -- nothing on a surface mount and a half turn
            // on a stack one -- and PositionParentAsmb is measured in the parent part's frame.
            //
            // How far the craft has turned since this round left. The anchor is a world point
            // written in the part's frame, so it has to be carried back through that.
            doubleQuat sinceLaunch = doubleQuat.Concatenate(launchAttitude, ecl2Asmb);

            position = TubeGeometry.BodyPositionPartFrame(launchAnchorPartFrame, travelEcl,
                                                          ecl2Asmb, asmb2Part, sinceLaunch);
            if (!Vec.IsFinite(position)) return false;

            rotation = TubeGeometry.BodyRotationPartFrame(attitudeEcl, ecl2Asmb, asmb2Part);

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


    /// <summary>
    /// The mesh a declared <c>&lt;SubPart&gt;</c> template is drawn with, by its Id, with no part of
    /// it on any craft. Null for an Id nothing declares.
    /// </summary>
    public static PartModel? ModelOfTemplate(string? templateId)
    {
        if (string.IsNullOrEmpty(templateId)) return null;

        try
        {
            return ModelOf(ModLibrary.Get<PartTemplate>(templateId)?.Components);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The mesh a part is drawn with. Shared by every part of its template and kept by the engine for
    /// the session, so it outlives the craft the part was on.
    /// </summary>
    public static PartModel? ModelOf(Part? part)
    {
        try
        {
            return ModelOf(part?.Template?.Components);
        }
        catch
        {
            // A part mid-teardown has nothing to lend.
            return null;
        }
    }

    private static PartModel? ModelOf(List<ModuleBase.TemplateDataBase>? components)
    {
        if (components is null) return null;

        foreach (ModuleBase.TemplateDataBase component in components)
        {
            if (component is PartModelModule.Template template) return PartModel.Get(template);
        }

        return null;
    }

    /// <summary>The attitude a round leaves this launcher at. See <see cref="TubeGeometry.ReleaseAttitudeEcl"/>.</summary>
    public static doubleQuat ReleaseAttitudeEcl(Part launcher, double3 releaseHeadingEcl, doubleQuat launchAttitude)
    {
        try
        {
            return TubeGeometry.ReleaseAttitudeEcl(releaseHeadingEcl, launchAttitude,
                                                   doubleQuat.Conjugate(launcher.Asmb2VehicleAsmb));
        }
        catch
        {
            return FireGeometry.RotationFromNose(releaseHeadingEcl);
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

    /// <summary>An ejector's arms, every subpart its marker matches. Empty for a launcher with none.</summary>
    public static void FindEjectorArms(Part launcher, LauncherProfile profile, List<Part> into)
    {
        into.Clear();
        if (string.IsNullOrEmpty(profile.EjectorArmMarker)) return;

        try
        {
            ReadOnlySpan<Part> subParts = launcher.SubParts;
            for (int i = 0; i < subParts.Length; i++)
            {
                if (subParts[i] is { } sub && sub.Id is { } id
                    && id.Contains(profile.EjectorArmMarker, StringComparison.OrdinalIgnoreCase))
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
    /// <para><see cref="Part"/> caches its matrices, so without <c>ResetCachedPosMatrixValues</c>
    /// the new orientation is ignored. Both the plain and <c>Safe</c> properties are written, since
    /// which one a render thread snapshots is not known.</para>
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

    /// <summary>Carries the barrel with the cannon and runs it back along the bore.</summary>
    public static bool TryApplyBarrelAim(Part barrel, LauncherProfile profile, double bearingRad,
                                         double elevationRad, double recoilMetres)
    {
        try
        {
            DrivePose pose = TubeGeometry.BarrelPose(profile, bearingRad, elevationRad, recoilMetres);

            barrel.Asmb2ParentAsmb = pose.Rotation;
            barrel.Asmb2ParentAsmbSafe = pose.Rotation;
            barrel.PositionParentAsmb = pose.Position;
            barrel.PositionParentAsmbSafe = pose.Position;
            barrel.ResetCachedPosMatrixValues();
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"barrel: could not write recoil ({e.GetType().Name}: {e.Message})");
            return false;
        }
    }

    /// <summary>
    /// Traverses and elevates the missile pods. Subparts do not nest in KSA, so the pods are a
    /// sibling of the turret and both the composed rotation and the position have to be written
    /// each frame — see <see cref="TubeGeometry.PodPose"/>.
    /// </summary>
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

    /// <summary>Writes a pose the caller has already solved. False, logged once, if the engine refused it.</summary>
    public static bool TryApplyPose(Part part, DrivePose pose, string what)
    {
        try
        {
            part.Asmb2ParentAsmb = pose.Rotation;
            part.Asmb2ParentAsmbSafe = pose.Rotation;
            part.PositionParentAsmb = pose.Position;
            part.PositionParentAsmbSafe = pose.Position;
            part.ResetCachedPosMatrixValues();
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"{what}: could not write pose ({e.GetType().Name}: {e.Message})");
            return false;
        }
    }

    /// <summary>Carries a director's base round with the traverse. Cosmetic to the launcher.</summary>
    public static bool TryApplyOpticBase(Part opticBase, LauncherProfile profile, double turretBearingRad)
    {
        try
        {
            DrivePose pose = TubeGeometry.OpticBasePose(profile, turretBearingRad);
            (double3 position, doubleQuat rotation) = (pose.Position, pose.Rotation);

            opticBase.Asmb2ParentAsmb = rotation;
            opticBase.Asmb2ParentAsmbSafe = rotation;
            opticBase.PositionParentAsmb = position;
            opticBase.PositionParentAsmbSafe = position;
            opticBase.ResetCachedPosMatrixValues();
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"optic base: could not write pose ({e.GetType().Name}: {e.Message})");
            return false;
        }
    }

    /// <summary>
    /// Rotates a direction in the <em>launcher part's</em> frame out into Ecl, through the part's
    /// mounting and then the vehicle's attitude. The inverse of
    /// <see cref="TryDirectionToPartFrame"/>; deliberately no variant stops at the vehicle frame,
    /// which differs by a half turn on a stack mount.
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
    /// <para><see cref="Turret.Track"/> works in the launcher part's axes, which differ from the
    /// vehicle's by a half turn on a stack mount. Null means the vehicle frame.</para>
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
    /// A point given in the launcher part's own frame, in Ecl.
    ///
    /// <para>Same centre-of-mass correction as the tubes: <paramref name="platformEcl"/> is the
    /// centre of mass and <c>PositionVehicleAsmb</c> is measured from the assembly origin.</para>
    /// </summary>
    public static bool TryPartPointEcl(Vehicle platform, Part launcher, double3 partFrame,
                                       double3 platformEcl, out double3 ecl)
    {
        ecl = Vec.Zero;

        try
        {
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
    /// Position of the launcher part in the ecliptic frame.
    ///
    /// Via the render frame, through <c>Part.PositionEgo</c>: Ego is a pure translation of Ecl.
    /// Falls back to the vehicle origin when there is no camera.
    /// </summary>
    public static double3 ResolveOriginEcl(Vehicle vehicle, Part? launcher)
    {
        double3 vehicleEcl = KsaWorld.PositionEcl(vehicle);
        if (launcher is null) return vehicleEcl;

        try
        {
            Camera camera = Program.GetMainCamera();
            if (camera is null) return vehicleEcl;

            // Where the craft is drawn: a craft in the followed craft's physics bubble is drawn at a
            // physics position metres from the analytic one, and the bearing is taken at something
            // picked off the screen. As the engine's own GetMatrixAsmb2Ego(Camera) overload does.
            double3 vehicleEgo = camera.GetPositionEgo(vehicle);
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
    /// A ring laid out about the boresight rather than read off the part's rotation.
    /// </summary>
    public static double3 MuzzleEcl(LauncherProfile profile, double3 originEcl, double3 boresight, int tubeIndex)
        => TubeGeometry.MuzzleRingEcl(profile, originEcl, boresight, tubeIndex);

    /// <summary>
    /// Muzzle of each tube in the render frame, from the part's own transform — so unlike
    /// <see cref="MuzzleEcl"/> these land on the actual tubes.
    /// </summary>
    /// <param name="carrier">
    /// The assembly the tubes ride: the pods, or the launcher part itself when the profile declares
    /// none. Works in vehicle-assembly coordinates, so it needs a real part, not an identity.
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
