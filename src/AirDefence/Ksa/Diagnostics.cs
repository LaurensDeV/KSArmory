using Brutal.Numerics;
using KSA;

namespace AirDefence;

/// <summary>
/// Dumps the battery's view of the world to the log.
///
/// When nothing appears on screen there are two candidate explanations - the mod is not seeing
/// the world, or it is seeing it and failing to draw. This prints enough of both sides to tell
/// which, including the reason each nearby vehicle was rejected by the radar.
/// </summary>
internal static class Diagnostics
{
    private static readonly List<Vehicle> Scratch = [];

    private static double _nextDumpAt;

    /// <summary>Emit a dump every <paramref name="intervalSeconds"/> while enabled.</summary>
    public static void Tick(DefenceBattery battery, Config config, double clock, double intervalSeconds)
    {
        if (clock < _nextDumpAt) return;
        _nextDumpAt = clock + intervalSeconds;
        Dump(battery, config);
    }

    public static void ResetTimer() => _nextDumpAt = 0.0;

    public static void Dump(DefenceBattery battery, Config config)
    {
        try
        {
            Log.Debug("---- diagnostic dump ----");
            DumpPlatform(battery);
            DumpRendering(battery);
            DumpVehicles(battery, config);
            DumpRadar(battery);
            Log.Debug("---- end dump ----");
        }
        catch (Exception e)
        {
            Log.Error("diagnostic dump failed", e);
        }
    }

    private static void DumpPlatform(DefenceBattery battery)
    {
        if (battery.Platform is not { } platform)
        {
            Log.Debug("platform: NONE (no controlled vehicle)");
            return;
        }

        double3 pos = KsaWorld.PositionEcl(platform);
        double3 vel = KsaWorld.VelocityEcl(platform);

        Log.Debug($"platform: '{KsaWorld.DisplayName(platform)}' launcher={(battery.Launcher is null ? "none" : "fitted")} " +
                 $"operational={battery.IsOperational} ammo={battery.Ammo}");
        Log.Debug($"  posEcl  = {Fmt(pos)}  |pos| = {Vec.Len(pos):E3}");
        Log.Debug($"  velEcl  = {Fmt(vel)}  speed = {Vec.Len(vel):F1} m/s");
        Log.Debug($"  bore    = {Fmt(battery.Boresight)}  (local up)");
        Log.Debug($"  mount   = {Fmt(battery.MountEcl)}  offset from hull = {Vec.Len(battery.MountEcl - pos):F2} m");

        try
        {
            if (platform.Parent is { } parent)
            {
                string parentName = parent is IObjectId oid ? oid.Id : parent.GetType().Name;
                double alt = parent is IPosition pp ? Vec.Len(pos - pp.GetPositionEcl()) : double.NaN;
                Log.Debug($"  parent  = {parentName}  distance from centre = {alt / 1000.0:F1} km");
            }
            else
            {
                Log.Warn("  parent  = NULL (boresight falls back to velocity direction)");
            }
        }
        catch (Exception e)
        {
            Log.Warn($"  parent lookup threw: {e.GetType().Name}");
        }
    }

    /// <summary>
    /// Checks the pieces the gizmo overlay depends on. If the renderer or camera is missing,
    /// nothing we submit can ever appear.
    /// </summary>
    private static void DumpRendering(DefenceBattery battery)
    {
        bool hasRenderer = Program.GizmosRenderer is not null;
        Log.Debug($"render: GizmosRenderer={(hasRenderer ? "ok" : "NULL")}");

        Camera? camera = null;
        try { camera = Program.GetMainCamera(); }
        catch (Exception e) { Log.Warn($"  GetMainCamera threw {e.GetType().Name}"); }

        if (camera is null)
        {
            Log.Warn("  camera = NULL -- Ecl->Ego conversion cannot run, nothing will draw");
            return;
        }

        // The two ways of locating the craft in the render frame. GetPositionEgo is what KSA
        // itself draws with (physics position); EclToEgo(GetPositionEcl()) uses the analytic
        // on-rails position. The gap between them is exactly how far the overlay sits from the
        // craft, so it says whether the anchor is working and how big the error is.
        if (battery.Platform is { } plat)
        {
            try
            {
                double3 engineEgo = camera.GetPositionEgo(plat);
                double3 naiveEgo = camera.EclToEgo(plat.GetPositionEcl());
                double gap = Vec.Len(engineEgo - naiveEgo);

                Log.Debug($"  platformEgo(engine) = {Fmt(engineEgo)}");
                Log.Debug($"  platformEgo(naive)  = {Fmt(naiveEgo)}");
                Log.Debug($"  anchor error        = {gap:F2} m");
                Log.Debug($"  renderCam==mainCam  = {ReferenceEquals(Program.GetRenderCamera(), Program.GetMainCamera())}");

                // GetPositionEgo only takes its exact, physics-based path when the camera is
                // following this vehicle (or one sharing its bubble). Otherwise it falls back
                // to the analytic position and the anchor buys us nothing.
                object? following = camera.Following;
                string followName = following switch
                {
                    null => "NOTHING",
                    Vehicle fv => $"vehicle '{KsaWorld.DisplayName(fv)}'",
                    IObjectId oid => $"{following.GetType().Name} '{oid.Id}'",
                    _ => following.GetType().Name,
                };
                Log.Debug($"  camera following    = {followName}  (is our platform: {ReferenceEquals(following, plat)})");
                Log.Debug($"  bubbleLeader        = {(plat.BubbleLeader is null ? "null" : KsaWorld.DisplayName(plat.BubbleLeader))}");
            }
            catch (Exception e)
            {
                Log.Warn($"  anchor comparison threw {e.GetType().Name}");
            }
        }

        try
        {
            double3 camEcl = camera.PositionEcl;
            double3 mountEgo = camera.EclToEgo(battery.MountEcl);

            // Ego is camera-relative, so this length is the distance from the eye to the
            // launcher. Wildly large means the frames are not lining up.
            Log.Debug($"  camPosEcl = {Fmt(camEcl)}");
            Log.Debug($"  mountEgo  = {Fmt(mountEgo)}  |ego| = {Vec.Len(mountEgo):F1} m from camera");

            if (Vec.Len(mountEgo) > 1e7)
            {
                Log.Warn("  mount is >10000 km from the camera in Ego -- frame mismatch, gizmos will be off-screen");
            }
        }
        catch (Exception e)
        {
            Log.Warn($"  Ego conversion threw {e.GetType().Name}");
        }
    }

    /// <summary>
    /// Lists every loaded vehicle with the numbers the radar filters on, and says which filter
    /// rejected it. This is the fastest way to see why the track list is empty.
    /// </summary>
    private static void DumpVehicles(DefenceBattery battery, Config config)
    {
        KsaWorld.CollectVehicles(Scratch);

        int inFrame;
        try { inFrame = Program.VehiclesInFrame.Length; } catch { inFrame = -1; }

        Log.Debug($"vehicles: {Scratch.Count} from CurrentSystem.All  (Program.VehiclesInFrame reports {inFrame})");

        if (battery.Platform is not { } platform) return;

        double3 origin = KsaWorld.PositionEcl(platform);
        double3 originVel = KsaWorld.VelocityEcl(platform);
        double coneCos = Math.Cos(config.Sensor.ConeHalfAngleRad);

        foreach (Vehicle v in Scratch)
        {
            string name = KsaWorld.DisplayName(v);

            if (ReferenceEquals(v, platform)) { Log.Debug($"  '{name}': self"); continue; }

            double3 r = KsaWorld.PositionEcl(v) - origin;
            double3 rel = KsaWorld.VelocityEcl(v) - originVel;
            double range = Vec.Len(r);
            double relSpeed = Vec.Len(rel);
            double cos = Vec.Dot(Vec.Unit(r), battery.Boresight);
            double offAxisDeg = double.RadiansToDegrees(Math.Acos(Math.Clamp(cos, -1.0, 1.0)));

            double tCa = Vec.TimeOfClosestApproach(r, rel, config.Sensor.ThreatHorizonSeconds);
            double cpa = Vec.Len(r + rel * tCa);

            string verdict =
                config.ProtectControlledVehicle && ReferenceEquals(v, KsaWorld.ControlledVehicle) ? "SKIP: is controlled vehicle"
                : range > config.Sensor.Range ? $"REJECT: out of range ({range / 1000.0:F1} > {config.Sensor.Range / 1000.0:F1} km)"
                : cos < coneCos ? $"REJECT: outside cone ({offAxisDeg:F0} deg > {config.Sensor.ConeDeg:F0})"
                : relSpeed < config.Sensor.MinTargetSpeed ? $"REJECT: too slow ({relSpeed:F1} < {config.Sensor.MinTargetSpeed:F0} m/s)"
                : cpa <= config.Sensor.ThreatRadius || range <= config.Sensor.ThreatRadius ? "TRACK: threat"
                : $"TRACK: not a threat (cpa {cpa:F0} m > {config.Sensor.ThreatRadius:F0})";

            Log.Debug($"  '{name}': range {range / 1000.0:F2} km, off-axis {offAxisDeg:F0} deg, " +
                     $"rel speed {relSpeed:F0} m/s, cpa {cpa:F0} m in {tCa:F0}s -> {verdict}");
        }
    }

    private static void DumpRadar(DefenceBattery battery)
    {
        Log.Debug($"radar: {battery.Radar.Tracks.Count} track(s), " +
                 $"locked={(battery.Radar.Locked is null ? "none" : KsaWorld.DisplayName(battery.Radar.Locked.Vehicle))}, " +
                 $"firingSolution={battery.Radar.HasFiringSolution}, roundsInFlight={battery.Rounds.Count}");

        foreach (Interceptor round in battery.Rounds)
        {
            Log.Debug($"  round {round.Tube}: age {round.Age:F1}s, speed {round.Speed:F0} m/s, lock={round.HasLock}");
        }
    }

    private static string Fmt(double3 v) => $"({v.X:E3}, {v.Y:E3}, {v.Z:E3})";
}
