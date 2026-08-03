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

            PartTree clone = BuildDroneParts(platform, craftName);

            string id = $"AD Test Drone {++_counter}";
            Vehicle drone = Vehicle.CreateVehicle(
                system,
                platform.Body2Cce,
                bodyRates: new double3(0, 0, 0),
                parent,
                id,
                clone.Root,
                orbit);

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
    private static PartTree BuildDroneParts(Vehicle platform, string? craftName)
    {
        if (!string.IsNullOrEmpty(craftName))
        {
            try
            {
                VehicleSave save = DefaultVehicleSaves.FindSave(craftName);
                if (save is not null)
                {
                    PartTree tree = save.Load(Program.MainViewport);
                    if (tree is not null) return tree;
                }
                Log.Warn($"test target: stock craft '{craftName}' not found, cloning the platform instead");
            }
            catch (Exception e)
            {
                Log.Warn($"test target: could not load '{craftName}' ({e.GetType().Name}), cloning the platform instead");
            }
        }

        return platform.Parts.DeepCopy();
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
