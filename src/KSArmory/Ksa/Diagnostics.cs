using Brutal.Numerics;
using KSA;

namespace KSArmory;

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

    // Next dump due, per system. Every system feeds this its own clock, which starts at zero when
    // that system is crewed, so one shared deadline is always tripped by whichever was crewed
    // first and pushed out past every other system's clock before any of them reach it. The others
    // then never dump, and nothing says so. The log is the mod's only debugging channel, and the
    // system worth dumping is rarely the oldest one.
    private static readonly Dictionary<IWeaponSystemView, double> NextDumpAt = [];

    /// <summary>Emit a dump every <paramref name="intervalSeconds"/> while enabled.</summary>
    public static void Tick(IWeaponSystemView battery, SystemConfig policy,
                            double clock, double intervalSeconds)
    {
        SampleRadialMotion(battery);
        SampleStep();

        if (NextDumpAt.TryGetValue(battery, out double due) && clock < due) return;

        NextDumpAt[battery] = clock + intervalSeconds;
        Dump(battery, policy);
    }

    // How far the platform's analytic position moves radially between frames, worst case since the
    // last dump. A craft the engine has put on rails is exactly static and reads zero; one still
    // live in the physics solver rests on its contacts and bobs along the contact normal, which is
    // the local vertical. Everything anchored to the platform inherits that -- including a round,
    // and so the camera riding it.
    private static readonly Dictionary<IWeaponSystemView, (double Last, double Worst)> Radial = [];

    // The simulated step's spread since the last dump. The chase transition advances its blend by
    // dt/TransitionSeconds each frame, so an uneven step advances the camera unevenly *along the
    // blend path* -- which for a target overhead on an airless body is very nearly straight up.
    private static double _stepMin = double.MaxValue;
    private static double _stepMax;
    private static double _stepWorstJump;
    private static double _stepLast;
    private static int _stepSamples;

    private static void SampleStep()
    {
        double step = KsaWorld.SimStepSeconds;
        if (!double.IsFinite(step) || step <= 0.0) return;

        if (_stepLast > 0.0) _stepWorstJump = Math.Max(_stepWorstJump, Math.Abs(step - _stepLast));

        _stepLast = step;
        _stepMin = Math.Min(_stepMin, step);
        _stepMax = Math.Max(_stepMax, step);
        _stepSamples++;
    }

    private static void SampleRadialMotion(IWeaponSystemView battery)
    {
        if (battery.Platform is not { } platform) return;

        try
        {
            if (platform.Parent is not IPosition parent) return;

            double radius = Vec.Len(KsaWorld.PositionEcl(platform) - parent.GetPositionEcl());
            if (!double.IsFinite(radius)) return;

            Radial.TryGetValue(battery, out (double Last, double Worst) seen);

            double moved = seen.Last > 0.0 ? Math.Abs(radius - seen.Last) : 0.0;
            Radial[battery] = (radius, Math.Max(seen.Worst, moved));
        }
        catch
        {
            // The parent chain is rebuilt during staging and SOI changes.
        }
    }

    /// <summary>Makes every system dump on its next tick.</summary>
    public static void ResetTimer() => NextDumpAt.Clear();

    /// <summary>Forgets a system, so its entry does not outlive the craft it was crewed on.</summary>
    public static void Forget(IWeaponSystemView battery)
    {
        NextDumpAt.Remove(battery);
        Radial.Remove(battery);
    }

    public static void Dump(IWeaponSystemView battery, SystemConfig policy)
    {
        try
        {
            Log.Debug("---- diagnostic dump ----");
            DumpPlatform(battery);
            DumpRendering(battery);
            DumpVehicles(battery, policy);
            DumpRadar(battery);
            DumpAttitudeControl(battery);
            Log.Debug("---- end dump ----");
        }
        catch (Exception e)
        {
            Log.Error("diagnostic dump failed", e);
        }
    }

    private static void DumpPlatform(IWeaponSystemView battery)
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
        Log.Debug($"  bore    = {Fmt(battery.Boresight)}  ({battery.Sensor.BoresightSource})");
        Log.Debug($"  mount   = {Fmt(battery.MountEcl)}  offset from hull = {Vec.Len(battery.MountEcl - pos):F2} m");

        // Whether the engine has parked this craft or is still solving it. On an airless body it
        // can barely ever be parked: PhysicsStates forces MotionlessTime to zero every step
        // without an atmosphere, so the one-second gate onto rails is unreachable and only Bepu's
        // 255-step sleeper is left, with no drag to get it there.
        try
        {
            (double _, double worst) = Radial.TryGetValue(battery, out (double, double) seen)
                                           ? seen
                                           : (0.0, 0.0);

            if (_stepSamples > 0)
            {
                double spread = _stepMax - _stepMin;

                // Samples, not frames: this is fed once per system per frame, so two crewed
                // systems double it. And the dump interval is simulated time, so at 0.01x the
                // window is a hundred times longer in wall clock and can span a speed change,
                // which makes the spread meaningless. The line only compares at one speed.
                Log.Debug($"  step    = {_stepMin * 1000.0:F2}..{_stepMax * 1000.0:F2} ms over "
                         + $"{_stepSamples} samples, spread {spread * 1000.0:F2} ms "
                         + $"({(_stepMax > 0.0 ? spread / _stepMax * 100.0 : 0.0):F1}%), "
                         + $"worst jump {_stepWorstJump * 1000.0:F2} ms");
            }

            _stepMin = double.MaxValue;
            _stepMax = 0.0;
            _stepWorstJump = 0.0;
            _stepSamples = 0;

            Log.Debug($"  physics = {platform.Situation}, onRails={platform.Situation.IsOnRails()}, "
                     + $"worst radial move between frames {worst * 1000.0:F3} mm");

            Radial[battery] = (Radial.TryGetValue(battery, out (double Last, double _) k) ? k.Last : 0.0, 0.0);
        }
        catch
        {
            Log.Warn("  physics = unavailable");
        }

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

    // Checks the pieces the gizmo overlay depends on. If the renderer or camera is missing, nothing
    // submitted can ever appear.
    private static void DumpRendering(IWeaponSystemView battery)
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
                // to the analytic position and the anchor buys nothing.
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

    // Lists every loaded vehicle with the numbers the radar filters on, and says which filter
    // rejected it. This is the fastest way to see why the track list is empty.
    private static void DumpVehicles(IWeaponSystemView battery, SystemConfig policy)
    {
        KsaWorld.CollectVehicles(Scratch);

        int inFrame;
        try { inFrame = Program.VehiclesInFrame.Length; } catch { inFrame = -1; }

        Log.Debug($"vehicles: {Scratch.Count} from CurrentSystem.All  (Program.VehiclesInFrame reports {inFrame})");

        if (battery.Platform is not { } platform) return;

        double3 origin = KsaWorld.PositionEcl(platform);
        double3 originVel = KsaWorld.VelocityEcl(platform);
        double coneCos = Math.Cos(battery.Sensor.ConeHalfAngleRad);

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

            double tCa = Vec.TimeOfClosestApproach(r, rel, battery.Sensor.ThreatHorizonSeconds);
            double cpa = Vec.Len(r + rel * tCa);

            string verdict =
                policy.ProtectControlledVehicle && ReferenceEquals(v, KsaWorld.ControlledVehicle) ? "SKIP: is controlled vehicle"
                : range > battery.Sensor.Range ? $"REJECT: out of range ({range / 1000.0:F1} > {battery.Sensor.Range / 1000.0:F1} km)"
                : cos < coneCos ? $"REJECT: outside cone ({offAxisDeg:F0} deg > {battery.Sensor.ConeDeg:F0})"
                : relSpeed < battery.Sensor.MinTargetSpeed ? $"REJECT: too slow ({relSpeed:F1} < {battery.Sensor.MinTargetSpeed:F0} m/s)"
                : cpa <= battery.Sensor.ThreatRadius || range <= battery.Sensor.ThreatRadius ? "TRACK: threat"
                : $"TRACK: not a threat (cpa {cpa:F0} m > {battery.Sensor.ThreatRadius:F0})";

            // How far the analytic position sits from where the craft is drawn. Rounds are fused
            // against the first and struck against the second, so this is the error budget of a
            // contact fuse: noise against a bounding sphere, and the whole answer against a hull.
            string slip = "n/a";
            if (KsaWorld.HasAnchor && KsaWorld.TryVehicleEgo(v, out double3 drawnEgo))
            {
                slip = $"{Vec.Len(drawnEgo - (KsaWorld.AnchorEgo + r)):F2} m";
            }

            Log.Debug($"  '{name}': range {range / 1000.0:F2} km, off-axis {offAxisDeg:F0} deg, " +
                     $"rel speed {relSpeed:F0} m/s, cpa {cpa:F0} m in {tCa:F0}s, " +
                     $"analytic-vs-drawn {slip} -> {verdict}");
        }
    }

    private static void DumpRadar(IWeaponSystemView battery)
    {
        Log.Debug($"radar: {battery.Radar.Tracks.Count} track(s), " +
                 $"maskedByTerrain={battery.Radar.MaskedByTerrain}, " +
                 $"locked={(battery.Radar.Locked is null ? "none" : battery.Radar.Locked.Contact.DisplayName)}, " +
                 $"firingSolution={battery.Radar.HasFiringSolution}, roundsInFlight={battery.Rounds.Count}");

        foreach (IProjectile round in battery.Rounds)
        {
            Log.Debug($"  {RoundLabel.For(round.Tube)}: age {round.Age:F1}s, speed {round.Speed:F0} m/s, lock={round.HasLock}");
        }
    }

    // What KSA's attitude tracker has to work with on the craft being flown, and the arm that
    // decides which nozzles it may work with at all.
    //
    // ThrusterController enrols a nozzle in a rotation axis on dot(thrust, normalize(axis x r))
    // over 0.1, where r runs from the *vehicle's* centre of mass -- not the part's. So a ring
    // sized to be a couple about its own part stops being one the moment that part is a stage on
    // something bigger, and past a long enough arm the nozzles meant to steer fall under the
    // threshold and are dropped in favour of whatever fires across it. tools/model/checkring.py
    // cannot see this: it measures against the part's declared mass seat, which is the separated
    // case. Nothing else reports the stacked one, which is why this is here.
    private static void DumpAttitudeControl(IWeaponSystemView battery)
    {
        if (battery.Platform is not { } craft) return;
        if (craft.FlightComputer is not { } computer) return;

        double band = 0.5 * computer.AngleDeadband
                      + Math.Max(computer.AngleTurnaround.Y, computer.AngleTurnaround.Z);

        // Ground-relative speed, because that is what KSA builds the Up frame's second direction
        // from -- and a frame built on a number this small is built on noise.
        double3 hereEcl = KsaWorld.PositionEcl(craft);
        double3 ground = KsaWorld.GroundVelocityAt(craft, hereEcl);
        Log.Debug($"  ground-relative speed "
                  + $"{Vec.Len(KsaWorld.VelocityEcl(craft) - ground):F3} m/s");

        Log.Debug($"attitude: {computer.AttitudeMode}/{computer.AttitudeTrackTarget}, "
                  + $"roll {computer.RollMode}, control part "
                  + $"{(craft.ControlPart is null ? "NONE" : "held")}");
        // The decisive pair: what the tracker thinks is wrong, and how fast it thinks the target is
        // running away. A vehicle standing vertical with "up" selected should read nothing on both.
        Log.Debug($"  error angles {Deg(computer.ErrorAngles.X):F2}/{Deg(computer.ErrorAngles.Y):F2}/"
                  + $"{Deg(computer.ErrorAngles.Z):F2} deg, error rates "
                  + $"{Deg(computer.ErrorRates.X):F3}/{Deg(computer.ErrorRates.Y):F3}/"
                  + $"{Deg(computer.ErrorRates.Z):F3} deg/s");

        Log.Debug($"  deadband {Deg(computer.AngleDeadband):F2} deg, turnaround "
                  + $"{Deg(computer.AngleTurnaround.Y):F2}/{Deg(computer.AngleTurnaround.Z):F2} deg, "
                  + $"rate bit {Deg(computer.RateBit.Y):F3}/{Deg(computer.RateBit.Z):F3} deg/s, "
                  + $"pointing band {Deg(band):F2} deg");

        double3 com = craft.CenterOfMassAsmb;

        ReadOnlySpan<Part> parts = craft.Parts.Parts;

        for (int p = 0; p < parts.Length; p++)
        {
            Part part = parts[p];
            ReadOnlySpan<Part> subParts = part.SubParts;
            double3 sum = Vec.Zero;
            int nozzles = 0;

            for (int i = 0; i < subParts.Length; i++)
            {
                if (!subParts[i].Id.Contains("Rcs", StringComparison.OrdinalIgnoreCase)) continue;
                sum += subParts[i].PositionVehicleAsmb;
                nozzles++;
            }

            if (nozzles == 0) continue;

            double3 ring = sum / nozzles;
            double3 arm = ring - com;
            Log.Debug($"  ring on {part.Id}: {nozzles} nozzle(s), centre {Fmt(ring)}, "
                      + $"vehicle CoM {Fmt(com)}, arm {Vec.Len(arm):F2} m");
        }
    }

    // A wobble is a shape, not a magnitude, and the periodic dump is far too slow to see one. This
    // samples the craft's own body rates every frame it is asked, so the trace says whether what is
    // happening is one lurch, a decaying ring, or a sustained oscillation -- and at what rate,
    // which is what separates a structural mode from a control loop chasing itself.
    //
    // Rate-limited by movement rather than by a clock: it prints only while something is actually
    // turning, so a craft sitting still costs a comparison and writes nothing.
    private static double _quietFor;

    public static void SampleBodyRates(Vehicle craft, double dt)
    {
        if (Log.Threshold > Log.Level.Debug) return;

        try
        {
            double3 rates = craft.BodyRates;
            double spin = Vec.Len(rates) * 180.0 / Math.PI;

            if (spin < 0.5)
            {
                _quietFor += dt;
                return;
            }

            // One line saying it started, then the trace, so the onset is findable in the log.
            if (_quietFor > 0.5)
            {
                Log.Debug($"---- {DisplayNameOf(craft)} started turning ----");
            }

            _quietFor = 0.0;

            double3 accel = craft.AngularAccelerationBody;
            Log.Debug($"rates {Deg(rates.X):+0.00;-0.00}/{Deg(rates.Y):+0.00;-0.00}/"
                      + $"{Deg(rates.Z):+0.00;-0.00} deg/s  "
                      + $"angular accel {Deg(accel.X):+0.0;-0.0}/{Deg(accel.Y):+0.0;-0.0}/"
                      + $"{Deg(accel.Z):+0.0;-0.0} deg/s^2  contact={craft.Situation.HasAnyContact()}");
        }
        catch (Exception e)
        {
            Log.Error("body-rate probe failed", e);
        }
    }

    private static string DisplayNameOf(Vehicle craft) => KsaWorld.DisplayName(craft);

    private static double Deg(double radians) => radians * 180.0 / Math.PI;

    private static string Fmt(double3 v) => $"({v.X:E3}, {v.Y:E3}, {v.Z:E3})";
}
