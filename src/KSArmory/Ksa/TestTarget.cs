using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Spawns a drone on a timed pass over the system. The course is computed backwards from the
/// moment of closest approach, so "30 seconds out at 300 m/s" means exactly that.
/// </summary>
internal static class TestTarget
{
    private static int _counter;

    // Compass bearing the drones come in on, relative to an arbitrary local axis. Fixed rather than
    // random so repeated runs are comparable.
    private const double AzimuthRadians = 0.0;

    /// <summary>The level direction from a platform that every drone is spawned along.</summary>
    public static double3 ApproachBearing(Vehicle platform)
    {
        double3 up = KsaWorld.LocalUp(platform);
        double3 east = Vec.AnyPerpendicular(up);
        double3 north = Vec.Cross(up, east);

        return (east * Math.Cos(AzimuthRadians)) + (north * Math.Sin(AzimuthRadians));
    }

    /// <summary>How the drone is aimed relative to the system.</summary>
    public enum Profile
    {
        /// <summary>Flies straight at the system. The easy case.</summary>
        HeadOn,

        /// <summary>Crosses overhead at <c>missDistance</c>. The case ProNav exists for.</summary>
        Overhead,

        /// <summary>Passes off to one side without ever closing. Tests the CPA threat model.</summary>
        PassingBy,
    }

    /// <summary>Null, logged, if anything in the spawn chain fails: a testing aid must not throw.</summary>
    /// <param name="platform">The vehicle carrying the system.</param>
    /// <param name="secondsToClosestApproach">Flight time from spawn to the pass.</param>
    /// <param name="speed">Drone speed relative to the platform (m/s).</param>
    /// <param name="missDistance">How close it passes (m). Ignored for <see cref="Profile.HeadOn"/>.</param>
    /// <param name="craftName">Stock craft to fly, e.g. "Gemini7". Null clones the platform.</param>
    /// <param name="bodyRates">How fast it leaves turning, in its own axes (rad/s).</param>
    /// <param name="bearing">The level direction to come in along, or zero for <see cref="ApproachBearing"/>.</param>
    public static Vehicle? Spawn(
        Vehicle platform,
        Profile profile,
        double secondsToClosestApproach,
        double speed,
        double missDistance,
        string? craftName = null,
        double3 bodyRates = default,
        double3 bearing = default)
    {
        try
        {
            if (Universe.CurrentSystem is not { } system)
            {
                Log.Warn("test target: no current system");
                return null;
            }

            if (platform.Parent is not { } parent)
            {
                Log.Warn("test target: platform has no parent body");
                return null;
            }

            double3 originEcl = KsaWorld.PositionEcl(platform);
            double3 originVel = KsaWorld.VelocityEcl(platform);
            double3 up = KsaWorld.LocalUp(platform);

            double3 azimuth = Vec.Len2(bearing) > 0.0 ? Vec.Unit(bearing) : ApproachBearing(platform);
            double3 north = Vec.Cross(up, azimuth);

            // By elevation angle, not a level track: a level pass starts at ~9 degrees elevation,
            // outside a radar cone measured off local up and deep in air that sheds its speed.
            double elevationDeg = profile switch
            {
                Profile.HeadOn => 75.0,     // steep dive onto the site
                Profile.PassingBy => 40.0,  // shallow, off to one side
                _ => 55.0,                  // Overhead: crosses well above
            };

            double3 closestPointEcl = profile switch
            {
                Profile.HeadOn => originEcl,
                Profile.PassingBy => originEcl + north * missDistance + up * (missDistance * 0.5),
                _ => originEcl + up * missDistance,
            };

            double t = secondsToClosestApproach;
            double spawnRange = speed * t;

            double elev = double.DegreesToRadians(elevationDeg);
            double3 spawnDir = up * Math.Sin(elev) + azimuth * Math.Cos(elev);

            double3 spawnEcl = originEcl + Vec.Unit(spawnDir) * spawnRange;

            // Aim it at the pass point; |closestPoint - spawn| is close enough to spawnRange
            // that the requested speed still lands the timing.
            double3 heading = Vec.Unit(closestPointEcl - spawnEcl);

            // v0 = d/t - g*t/2, or the drone falls ~18 km over a 60 s run. A vacuum solution, so
            // in air it arrives short, a few km on a shallow pass, still through the radar volume.
            double3 gravity = KsaWorld.GravityAt(platform, spawnEcl);
            double3 spawnVelEcl = originVel + heading * speed - gravity * (0.5 * t);

            if (!ToParentInertial(parent, spawnEcl, spawnVelEcl, out double3 posCci, out double3 velCci))
            {
                Log.Warn("test target: could not convert spawn state to the parent frame");
                return null;
            }

            // Placement goes through Orbit.StateVectors, so a bad orbit silently drops the
            // vehicle at the frame origin instead of erroring. Log the inputs.
            Log.Debug($"  spawn posCci = {posCci.X:E3},{posCci.Y:E3},{posCci.Z:E3}  |r| = {Vec.Len(posCci) / 1000.0:F1} km");
            Log.Debug($"  spawn velCci = {velCci.X:E3},{velCci.Y:E3},{velCci.Z:E3}  |v| = {Vec.Len(velCci):F1} m/s");
            Log.Debug($"  parent Mu    = {parent.Mu:E4}");

            if (parent.Mu <= 0.0)
            {
                Log.Warn("test target: parent Mu is zero, orbit would be degenerate");
                return null;
            }

            Orbit orbit = Orbit.CreateFromStateCci(
                parent,
                Universe.GetElapsedTime(),
                posCci,
                velCci,
                new byte4(255, 80, 80, 255));

            Log.Debug($"  orbit: pe = {orbit.Periapsis / 1000.0:F1} km, ap = {orbit.Apoapsis / 1000.0:F1} km, " +
                     $"ecc = {orbit.Eccentricity:F4}");

            // Building a vehicle mutates the shapes registry, which the vehicle worker holds for its
            // whole run and a hook of this mod can land inside; racing it builds half a vehicle.
            using ShapesUnlock shapes = ConstraintSim.UnlockShapesBlocking();

            DroneBlueprint blueprint = BuildDroneParts(platform, craftName);

            string id = $"AD Test Drone {++_counter}";
            Vehicle drone = CreateDroneVehicle(blueprint, system, platform, parent, id, orbit, bodyRates);

            // Without this CelestialSystem.UpdatePerFrameData never walks it and it sits at the
            // frame origin. No bubble is needed: AssignOrphanVehicles gives it one on the next step.
            parent.Children.Add(drone);

            // MeanRadius stays zero until UpdateAfterPartTreeModification recomputes it, and the
            // camera's zoom scales by it.
            try
            {
                // In this order: the second reads the stores and mass properties the first rebuilds.
                drone.Parts.RecomputeAllDerivedData();
                drone.UpdateAfterPartTreeModification();
            }
            catch (Exception e)
            {
                Log.Warn($"test target: could not compute drone bounds ({e.GetType().Name}); "
                         + "the camera will not zoom on it");
            }

            // Zoom scales by MeanRadius and is stored per craft in OrbitView.DistancePower, so a
            // drone that cannot be zoomed has one of the two wrong.
            try
            {
                double radius = drone.MeanRadius;
                var view = drone.OrbitView;
                Log.Info($"  drone     {Describe(drone)}");

                // Beside craft that already zoom correctly, as the reference.
                var scratch = new List<Vehicle>();
                KsaWorld.CollectVehicles(scratch);
                foreach (Vehicle other in scratch)
                {
                    if (ReferenceEquals(other, drone)) continue;
                    Log.Info($"  reference {Describe(other)}");
                }
            }
            catch (Exception e)
            {
                Log.Warn($"  could not read drone camera data: {e.GetType().Name}");
            }

            // Now rather than next frame, so the read-back below reports the real position.
            drone.UpdatePerFrameData();

            // Disagreeing with the intended range means the orbit did not take.
            try
            {
                double actualRangeKm = Vec.Len(KsaWorld.PositionEcl(drone) - originEcl) / 1000.0;
                Log.Debug($"  placed at {actualRangeKm:F1} km from the system (intended {Vec.Len(spawnEcl - originEcl) / 1000.0:F1} km)");
            }
            catch (Exception e)
            {
                Log.Warn($"  could not read back drone position: {e.GetType().Name}");
            }

            double spawnRangeKm = Vec.Len(spawnEcl - originEcl) / 1000.0;
            Log.Info(
                $"spawned '{id}' - {profile}, {speed:F0} m/s, CPA in {t:F0}s " +
                $"at {(profile == Profile.HeadOn ? 0 : missDistance):F0} m, " +
                $"spawn range {spawnRangeKm:F1} km");

            return drone;
        }
        catch (Exception e)
        {
            Log.Error("test target spawn failed", e);
            return null;
        }
    }

    /// <summary>
    /// A stock craft set down on the ground at a latitude and longitude, standing still -- a target
    /// to shoot at rather than one flying past. Built above <paramref name="platform"/> and then
    /// teleported, because the launch menu's own placement is what puts a craft on the surface.
    /// </summary>
    public static Vehicle? SpawnParked(Vehicle platform, string craftName, string id, Celestial body,
                                       double latitudeDeg, double longitudeDeg)
    {
        try
        {
            if (Universe.CurrentSystem is not { } system || platform.Parent is not { } parent) return null;

            double3 up = KsaWorld.LocalUp(platform);
            double3 spawnEcl = KsaWorld.PositionEcl(platform) + up * 500.0;
            if (!ToParentInertial(parent, spawnEcl, KsaWorld.VelocityEcl(platform), out double3 posCci, out double3 velCci))
            {
                return null;
            }

            Orbit orbit = Orbit.CreateFromStateCci(parent, Universe.GetElapsedTime(), posCci, velCci,
                                                   new byte4(255, 80, 80, 255));

            using ShapesUnlock shapes = ConstraintSim.UnlockShapesBlocking();
            DroneBlueprint blueprint = BuildDroneParts(platform, craftName);
            Vehicle craft = Vehicle.CreateVehicle(system, platform.Body2Cce, bodyRates: default,
                                                  parent, id, blueprint.Parts.Root, orbit);
            parent.Children.Add(craft);
            craft.Parts.RecomputeAllDerivedData();
            craft.UpdateAfterPartTreeModification();
            craft.UpdatePerFrameData();
            craft.TeleportToLocation(body, latitudeDeg, longitudeDeg);

            Log.Info($"parked '{id}' ({craftName}) at {latitudeDeg:F4}, {longitudeDeg:F4} on {body.Id}");
            return craft;
        }
        catch (Exception e)
        {
            Log.Error("parked target spawn failed", e);
            return null;
        }
    }

    /// <summary>Stock craft that ship with the game, usable as drones.</summary>
    public static readonly string[] StockCraft = ["Gemini7", "Hunter", "Banjo", "Polaris", "Rocket"];

    // One function for the drone and the reference craft, so only their difference stands out.
    private static string Describe(Vehicle v)
    {
        try
        {
            string extents = "?";
            try
            {
                float3 e = v.BoundingBoxHalfExtentsAsmb;
                extents = $"{e.X:F2}x{e.Y:F2}x{e.Z:F2}";
            }
            catch { }

            return $"'{KsaWorld.DisplayName(v)}': radius {v.MeanRadius:F2} "
                   + $"halfExtents {extents} "
                   + $"zoomPow {v.OrbitView?.DistancePower ?? double.NaN:F2} "
                   + $"parts {v.Parts?.Count ?? -1} "
                   + $"bubbleLeader {(v.BubbleLeader is null ? "none" : "yes")} "
                   + $"bubble {(v.HasPhysicsBubble ? "yes" : "none")} "
                   + $"controllable {v.IsControllable} "
                   + $"hasControlModule {HasControlModule(v)} controls {ControlCount(v)}";
        }
        catch (Exception e)
        {
            return $"'{KsaWorld.DisplayName(v)}': could not describe ({e.GetType().Name})";
        }
    }

    private static string HasControlModule(Vehicle v)
    {
        try { return v.Parts?.Modules.HasAny<Control>().ToString() ?? "?"; } catch { return "?"; }
    }

    // What Vehicle.IsControllable tests. Beside HasControlModule it tells parts arriving without
    // modules from modules whose hot-path list was never built, which IsControllable alone cannot.
    private static int ControlCount(Vehicle v)
    {
        try { return v.Parts?.Controls.NumModules ?? -1; } catch { return -1; }
    }

    // The character is empty for craft; it is all that tells a kitten save from a vehicle save.
    private readonly record struct DroneBlueprint(PartTree Parts, string Character);

    // In the class KSA would use: VehicleTemplate makes a KittenEva when the save names a character.
    // The stock Hunter, Banjo and Polaris carry no control module, and only KittenEva's
    // IsControllable override makes them controllable; built as a plain Vehicle they never are.
    private static Vehicle CreateDroneVehicle(
        DroneBlueprint blueprint, CelestialSystem system, Vehicle platform,
        IParentBody parent, string id, Orbit orbit, double3 bodyRates)
    {
        if (!string.IsNullOrEmpty(blueprint.Character))
        {
            try
            {
                return new KittenEva(system, blueprint.Character, platform.Body2Cce,
                                     bodyRates: bodyRates, parent, id,
                                     blueprint.Parts.Root, orbit);
            }
            catch (Exception e)
            {
                // A renamed or unloaded character throws: the id is resolved through ModLibrary.
                Log.Warn($"test target: '{blueprint.Character}' is not a loadable character "
                         + $"({e.GetType().Name}); spawning a plain vehicle, which cannot be flown");
            }
        }

        return Vehicle.CreateVehicle(system, platform.Body2Cce, bodyRates: bodyRates,
                                     parent, id, blueprint.Parts.Root, orbit);
    }

    // A clone of the platform is the fallback only: it carries a launcher and shares the guards
    // that protect the player's craft.
    private static DroneBlueprint BuildDroneParts(Vehicle platform, string? craftName)
    {
        if (!string.IsNullOrEmpty(craftName))
        {
            try
            {
                VehicleSave? save = DefaultVehicleSaves.FindSave(craftName) ?? LibrarySave(craftName);
                if (save is not null)
                {
                    PartTree? tree = save.Load(Program.MainViewport);
                    if (tree is not null) return new DroneBlueprint(tree, save.VehicleSaveData.Character);
                }
                Log.Warn($"test target: stock craft '{craftName}' not found, cloning the platform instead");
            }
            catch (Exception e)
            {
                Log.Warn($"test target: could not load '{craftName}' ({e.GetType().Name}), cloning the platform instead");
            }
        }

        return new DroneBlueprint(platform.Parts.DeepCopy(), string.Empty);
    }

    // A design from the player's own library, read fresh so one written while the game runs is found.
    private static VehicleSave? LibrarySave(string name)
    {
        VehicleSaves.Refresh();
        foreach (VehicleSave save in VehicleSaves.AsSpan())
        {
            if (save.Id == name) return save;
        }
        return null;
    }

    // Cce differs from Ecl by the body's position and velocity; Cci is a fixed rotation of Cce, both
    // non-rotating, so one quaternion carries position and velocity.
    private static bool ToParentInertial(
        IParentBody parent, double3 posEcl, double3 velEcl, out double3 posCci, out double3 velCci)
    {
        posCci = default;
        velCci = default;

        if (parent is not IPosition parentPos || parent is not IVelocity parentVel) return false;

        double3 posCce = posEcl - parentPos.GetPositionEcl();
        double3 velCce = velEcl - parentVel.GetVelocityEcl();

        doubleQuat cce2Cci = parent.GetCce2Cci();
        posCci = cce2Cci * posCce;
        velCci = cce2Cci * velCce;

        return Vec.IsFinite(posCci) && Vec.IsFinite(velCci);
    }
}
