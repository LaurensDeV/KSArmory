using Brutal.Numerics;
using KSA;

namespace AirDefence;

/// <summary>
/// Spawns a drone on a timed pass over the battery, so the system can be exercised without
/// building a second craft and flying it into position by hand.
///
/// Drones fly one of KSA's stock craft by default, so the thing being shot at is recognisably
/// a separate vessel rather than another copy of the launcher. They are placed on a course
/// computed backwards from the moment of closest approach, so "30 seconds out at 300 m/s"
/// means exactly that.
/// </summary>
internal static class TestTarget
{
    private static int _counter;

    /// <summary>
    /// Compass bearing the drones come in on, relative to an arbitrary local axis. Fixed rather
    /// than random so repeated runs are comparable.
    /// </summary>
    private const double AzimuthRadians = 0.0;

    /// <summary>How the drone is aimed relative to the battery.</summary>
    public enum Profile
    {
        /// <summary>Flies straight at the battery. The easy case.</summary>
        HeadOn,

        /// <summary>Crosses overhead at <c>missDistance</c>. The case ProNav exists for.</summary>
        Overhead,

        /// <summary>Passes off to one side without ever closing. Tests the CPA threat model.</summary>
        PassingBy,
    }

    /// <summary>
    /// Creates the drone. Returns null and logs if anything in the spawn chain fails - this is
    /// a testing aid, so it must never take the game down with it.
    /// </summary>
    /// <param name="platform">The vehicle carrying the battery.</param>
    /// <param name="secondsToClosestApproach">Flight time from spawn to the pass.</param>
    /// <param name="speed">Drone speed relative to the platform (m/s).</param>
    /// <param name="missDistance">How close it passes (m). Ignored for <see cref="Profile.HeadOn"/>.</param>
    /// <param name="craftName">Stock craft to fly, e.g. "Gemini7". Null clones the platform.</param>
    public static Vehicle? Spawn(
        Vehicle platform,
        Profile profile,
        double secondsToClosestApproach,
        double speed,
        double missDistance,
        string? craftName = null)
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

            // Build the approach in world space. `heading` is the direction the drone travels.
            double3 east = Vec.AnyPerpendicular(up);
            double3 north = Vec.Cross(up, east);

            // Spawn by elevation angle rather than flying a level track. A level pass computed
            // from range alone starts the drone at ~9 degrees elevation, which is both outside
            // the radar cone (measured off local up) and deep in dense air, where a blunt
            // 185 kg craft sheds its speed in seconds. Starting high fixes both.
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

            // Direction from the battery to the spawn point: elevation above the horizon,
            // azimuth around it.
            double elev = double.DegreesToRadians(elevationDeg);
            double3 azimuth = east * Math.Cos(AzimuthRadians) + north * Math.Sin(AzimuthRadians);
            double3 spawnDir = up * Math.Sin(elev) + azimuth * Math.Cos(elev);

            double3 spawnEcl = originEcl + Vec.Unit(spawnDir) * spawnRange;

            // Aim it at the pass point; |closestPoint - spawn| is close enough to spawnRange
            // that the requested speed still lands the timing.
            double3 heading = Vec.Unit(closestPointEcl - spawnEcl);

            // Solve the ballistic problem rather than just aiming the velocity: without this the
            // drone falls ~18 km over a 60 s run and buries itself in the terrain long before
            // arriving. For displacement d over time t under gravity g,
            //     d = v0*t + 0.5*g*t^2   =>   v0 = d/t - 0.5*g*t
            // which is an upward bias, since g points down. Inheriting the platform's velocity
            // also carries the site's rotation, leaving only ~60 m of centripetal drift over a
            // minute - close enough for a test target.
            //
            // This is a *vacuum* solution and KSA models atmosphere, so the drone sheds speed on
            // the way down and lands short of
            // the aim point, by a few km on a shallow pass. Steeper profiles (HeadOn) are much
            // less affected, and the miss is harmless for testing: the drone still flies a real
            // trajectory through the radar's volume.
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
                Universe.GetElapsedSimTime(),
                posCci,
                velCci,
                new byte4(255, 80, 80, 255));

            Log.Debug($"  orbit: pe = {orbit.Periapsis / 1000.0:F1} km, ap = {orbit.Apoapsis / 1000.0:F1} km, " +
                     $"ecc = {orbit.Eccentricity:F4}");

            DroneBlueprint blueprint = BuildDroneParts(platform, craftName);

            string id = $"AD Test Drone {++_counter}";
            Vehicle drone = CreateDroneVehicle(blueprint, system, platform, parent, id, orbit);

            // Constructing the Vehicle is not enough to put it in the world. KSA's own runtime
            // spawn path (Vehicle.Split) attaches it to the parent's orbiter tree and to a
            // physics update task; without the first, CelestialSystem.UpdatePerFrameData never
            // walks it, so its cached Ecl position stays at the frame origin and it neither
            // moves nor can be seen. Without the second it is never simulated.
            parent.Children.Add(drone);

            if (platform.UpdateTask is { } task)
            {
                drone.AddToTask(task);
            }
            else
            {
                Log.Warn("test target: platform has no update task, drone will not be simulated");
            }

            // Work out how big it is.
            //
            // Vehicle.MeanRadius is BoundingSphereRadiusBody, which only ever gets set by
            // UpdateCollisionGeometry, which is private and reachable only through this call.
            // A vehicle assembled here and never handed through it keeps a radius of zero - and
            // the camera scales its zoom by MeanRadius, so a spawned drone could not be zoomed
            // in or out at all. It also feeds the flight plan's impact clearance margin.
            //
            // KSA's own runtime spawn path reaches this the same way, after the part tree is
            // attached; ours has to say so explicitly.
            try
            {
                // Rebuild the part tree's derived data, then the vehicle's, in that order: the
                // second reads the SubstanceStores and inert mass properties the first rebuilds.
                //
                // Between them they are what a tree assembled from a save has never been through.
                // UpdateAfterPartTreeModification reaches UpdateCollisionGeometry, which is
                // private and is the only thing that sets the bounding sphere - and the camera
                // scales its zoom by MeanRadius, so without it a drone has a radius of zero.
                //
                // Neither has anything to do with whether the drone can be *controlled*. That was
                // the first theory and measurement disproved it: RecomputeAllDerivedData rebuilds
                // substance stores, static mass, motor stacks and seats, and never touches
                // Controls. The real cause is a class, not a cache - see CreateDroneVehicle.
                drone.Parts.RecomputeAllDerivedData();
                drone.UpdateAfterPartTreeModification();
            }
            catch (Exception e)
            {
                Log.Warn($"test target: could not compute drone bounds ({e.GetType().Name}); "
                         + "the camera will not zoom on it");
            }

            // What the camera needs, measured rather than assumed.
            //
            // Zoom scales by MeanRadius and is stored per craft in OrbitView.DistancePower. A
            // spawned drone that cannot be zoomed has one of the two wrong, and guessing which
            // has already cost a build.
            try
            {
                double radius = drone.MeanRadius;
                var view = drone.OrbitView;
                // INFO, not DEBUG: it fires once per spawn, and it is the number that decides
                // whether the camera can frame the thing at all.
                Log.Info($"  drone     {Describe(drone)}");

                // Against craft that already zoom correctly. A number on its own says nothing;
                // the same number beside a working one says everything, and the vehicles already
                // in the scene are exactly that reference.
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

            // Populate the per-frame cache now rather than waiting a frame, so the read-back
            // below reports the real position.
            drone.UpdatePerFrameData();

            // Where did it actually end up? If this disagrees with the intended spawn range,
            // the orbit did not take.
            try
            {
                double actualRangeKm = Vec.Len(KsaWorld.PositionEcl(drone) - originEcl) / 1000.0;
                Log.Debug($"  placed at {actualRangeKm:F1} km from the battery (intended {Vec.Len(spawnEcl - originEcl) / 1000.0:F1} km)");
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

    /// <summary>Stock craft that ship with the game, usable as drones.</summary>
    public static readonly string[] StockCraft = ["Gemini7", "Hunter", "Banjo", "Polaris", "Rocket"];

    /// <summary>
    /// Builds the drone's parts, preferring one of KSA's own craft.
    ///
    /// Cloning the launcher platform works and is always structurally valid, but it means
    /// shooting air-defence sites at air-defence sites, which is both silly to watch and
    /// actively confusing — a drone carrying a launcher looks like it should be fighting back,
    /// and being a clone it shares the guards that protect the player's craft. A stock vessel is
    /// an obviously separate thing. Falls back to the clone if the library craft will not load.
    /// </summary>
    /// <summary>
    /// Everything about a vehicle that plausibly bears on whether the camera can frame it.
    ///
    /// <para>Written as one function used for both the spawned drone and the craft already in
    /// the scene, so the two are described identically and the difference is the only thing that
    /// stands out. A stock Hunter zooms and a clone of it does not, with the same MeanRadius, so
    /// whatever is responsible is one of the other fields.</para>
    /// </summary>
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
                   + $"bubble {(v.BubbleLeader is null ? "none" : "yes")} "
                   + $"task {(v.UpdateTask is null ? "none" : "yes")} "
                   + $"controllable {v.IsControllable} "
                   + $"hasControlModule {HasControlModule(v)} controls {ControlCount(v)}";
        }
        catch (Exception e)
        {
            return $"'{KsaWorld.DisplayName(v)}': could not describe ({e.GetType().Name})";
        }
    }

    /// <summary>Whether the tree carries a Control module at all, listed or not.</summary>
    private static string HasControlModule(Vehicle v)
    {
        try { return v.Parts?.Modules.HasAny<Control>().ToString() ?? "?"; } catch { return "?"; }
    }

    /// <summary>
    /// Control modules the tree has, which is exactly what Vehicle.IsControllable tests.
    ///
    /// <para>Zero modules and zero controls means the save's parts arrived without their modules
    /// at all. Modules present but no controls means they exist and the hot-path list was never
    /// built. Those need different fixes, and the difference is invisible from IsControllable
    /// alone - which is why the first attempt at this rebuilt the list and changed nothing.</para>
    /// </summary>
    private static int ControlCount(Vehicle v)
    {
        try { return v.Parts?.Controls.NumModules ?? -1; } catch { return -1; }
    }

    /// <summary>
    /// A drone's part tree, plus the character it belongs to if the save names one.
    ///
    /// <para>The character is empty for craft. It is the only thing that distinguishes a kitten
    /// save from a vehicle save, and it cannot be recovered from the part tree.</para>
    /// </summary>
    private readonly record struct DroneBlueprint(PartTree Parts, string Character);

    /// <summary>
    /// Builds the drone in whichever class KSA itself would have used for this save.
    ///
    /// <para><b>Kittens are not craft.</b> <c>KittenEva</c> is a <c>Vehicle</c> subclass that
    /// overrides <c>IsControllable</c> to a constant true, and the stock Hunter, Banjo and
    /// Polaris are all instances of it. Measured in game, they carry <em>no control module at
    /// all</em> — which is exactly what the base <c>Vehicle.IsControllable</c> requires. So a
    /// Hunter rebuilt through <c>Vehicle.CreateVehicle</c> is a plain vehicle wearing a kitten's
    /// part tree: it matches the stock one in every measurable respect — same radius, same half
    /// extents, same zoom power, same part count, same zero control modules — and can never be
    /// controlled or zoomed, because the property that would have said otherwise belongs to a
    /// class it is not an instance of.</para>
    ///
    /// <para>That is why rebuilding the part tree's caches changed nothing, twice. There was
    /// never anything wrong with the part tree.</para>
    ///
    /// <para>KSA chooses between the two the same way, on whether the save names a character —
    /// see <c>VehicleTemplate</c>, which branches on <c>Character != null</c>.</para>
    /// </summary>
    private static Vehicle CreateDroneVehicle(
        DroneBlueprint blueprint, CelestialSystem system, Vehicle platform,
        IParentBody parent, string id, Orbit orbit)
    {
        if (!string.IsNullOrEmpty(blueprint.Character))
        {
            try
            {
                return new KittenEva(system, blueprint.Character, platform.Body2Cce,
                                     bodyRates: new double3(0, 0, 0), parent, id,
                                     blueprint.Parts.Root, orbit);
            }
            catch (Exception e)
            {
                // The id is resolved through ModLibrary, so a renamed or unloaded character
                // throws here. A plain vehicle is still a perfectly good thing to shoot at; it
                // just cannot be flown, which is what the warning is for.
                Log.Warn($"test target: '{blueprint.Character}' is not a loadable character "
                         + $"({e.GetType().Name}); spawning a plain vehicle, which cannot be flown");
            }
        }

        return Vehicle.CreateVehicle(system, platform.Body2Cce, bodyRates: new double3(0, 0, 0),
                                     parent, id, blueprint.Parts.Root, orbit);
    }

    private static DroneBlueprint BuildDroneParts(Vehicle platform, string? craftName)
    {
        if (!string.IsNullOrEmpty(craftName))
        {
            try
            {
                // Both are genuinely nullable in KSA - FindSave returns VehicleSave? and Load
                // returns PartTree? - so declaring them non-null was the warning, not the
                // checks below, which were already right.
                VehicleSave? save = DefaultVehicleSaves.FindSave(craftName);
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

    /// <summary>
    /// Converts an ecliptic state into the parent body's inertial frame.
    ///
    /// Cce is the parent-centred *ecliptic* frame, so it differs from Ecl only by the body's
    /// own position and velocity. Cci is parent-centred *inertial*, a fixed rotation away from
    /// Cce - both are non-rotating, so the same quaternion carries position and velocity.
    /// </summary>
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
