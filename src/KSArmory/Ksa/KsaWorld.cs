using System.IO;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using KSA.Rendering.Water.Data;

namespace KSArmory;

/// <summary>
/// Every direct touch of KSA's internals lives here. KSA is pre-release and its API moves,
/// so keeping the surface in one file means a game update breaks one place, not ten.
///
/// All positions and velocities are in the ecliptic frame (Ecl): inertial, metres, and the
/// frame both <see cref="Vehicle.GetPositionEcl"/> and gizmo rendering agree on.
/// </summary>
internal static class KsaWorld
{
    /// <summary>The vehicle the player is currently flying, or null in menus.</summary>
    public static Vehicle? ControlledVehicle => Program.ControlledVehicle;

    public static bool InFlight => Program.ControlledVehicle is { IsDisposed: false };

    /// <summary>
    /// The simulated seconds KSA's last step actually advanced the world by.
    ///
    /// <para>This — not the player-time delta StarMap hands the frame hook, and not a
    /// difference of clock samples — is what the battery steps on. A paused game reports zero
    /// and a warped one reports the real span, and because it is the step the engine applied
    /// rather than one measured around it, it cannot be a step out of phase with the world.
    /// See <see cref="SimClock"/> for why that distinction is worth tens of metres.</para>
    /// </summary>
    public static double SimStepSeconds => Universe.GetLastSimStep().DeltaTime;

    // Pure, and in Sim/ so it can be tested. See StepGate.
    private static readonly StepGate<SimTime> _stepGate = new();

    /// <summary>
    /// The simulated seconds to integrate now, or zero if the engine has applied no new step
    /// since the last call. <b>Consuming</b> — call once per update and use the result.
    ///
    /// <para><see cref="SimStepSeconds"/> reports the <em>last</em> step, not one since you last
    /// asked, so asking twice without the engine stepping returns it twice. Integrating it twice
    /// adds motion the world never made, and it compounds because it lands in
    /// <c>PositionEcl</c>.</para>
    /// </summary>
    public static double ConsumeSimStep()
    {
        SimStep step = Universe.GetLastSimStep();
        return _stepGate.Consume(step.NextTime, step.DeltaTime);
    }

    /// <summary>Forgets which step was last integrated. For unload and scene changes.</summary>
    public static void ResetSimStepTracking() => _stepGate.Reset();

    /// <summary>True while the simulation is stopped. KSA defines this as speed exactly zero.</summary>
    public static bool IsPaused => Universe.IsPaused();

    /// <summary>Current timewarp factor; 1.0 is real time, 0.0 is paused. Display only.</summary>
    public static double SimulationSpeed => Universe.SimulationSpeed;

    /// <summary>
    /// Slowest speed worth offering. Below this KSA names the speed "paused" — its SimSpeed
    /// constructor calls anything under 1e-4 paused — and a world that runs while every label
    /// says it is stopped is worse than one that will not go slower.
    ///
    /// <para>It is only the *name*: Universe.IsPaused() tests the speed against exactly zero, so
    /// the world really would keep running. That mismatch is the trap, not a limit.</para>
    /// </summary>
    public const double SlowestSimSpeed = 0.001;

    /// <summary>
    /// Sets the world's simulation speed, including values slower than the in-game controls
    /// reach. KSA's own roller works in tenths, so 0.1x is as slow as it will go; nothing in
    /// the engine enforces that.
    ///
    /// <para><c>SetSimulationSpeed</c> only rejects speeds above <c>SimSpeed.MaxSpeed</c> — there
    /// is no floor — and it assigns the field directly rather than queuing an input event, so a
    /// value set here holds until something else changes it.</para>
    ///
    /// <para>Everything this mod does is already keyed to simulated time, so a slow world needs
    /// no special handling: <see cref="SimTimeSeconds"/> simply advances slowly and the battery,
    /// the drives and the rounds all scale with it.</para>
    /// </summary>
    /// <returns>False if the value was not finite or not positive; the speed is left alone.</returns>
    public static bool SetSimulationSpeed(double speed)
    {
        if (!double.IsFinite(speed) || speed <= 0.0) return false;

        Universe.SetSimulationSpeed(new SimSpeed(Math.Max(speed, SlowestSimSpeed)));
        return true;
    }

    /// <summary>True once the vehicle has been destroyed or unloaded out from under us.</summary>
    public static bool IsAlive(Vehicle? v) => v is { IsDisposed: false };

    /// <summary>
    /// Appends every vehicle the game currently has loaded into <paramref name="into"/>.
    ///
    /// Reads <c>Universe.CurrentSystem.All</c> rather than <c>Program.VehiclesInFrame</c>.
    /// The latter is a per-frame scratch buffer refilled by <c>RefreshVehiclesInFrame()</c>
    /// at a point in the tick that does not line up with a Harmony postfix on OnFrame - it
    /// reads back empty from there, which silently blinds the radar. The system's collection
    /// is the authoritative list and is valid whenever we are called.
    ///
    /// Copies immediately: the result must not be held across frames, and we must not be
    /// iterating engine state while destroying vehicles.
    /// </summary>
    public static void CollectVehicles(List<Vehicle> into)
    {
        into.Clear();

        try
        {
            if (Universe.CurrentSystem is { } system)
            {
                ReadOnlySpan<Astronomical> all = system.All.AsSpan();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] is Vehicle { IsDisposed: false } v) into.Add(v);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"vehicle enumeration failed: {e.Message}");
        }

        // Belt and braces: if the system collection ever comes back empty, fall back to the
        // per-frame buffer rather than going blind.
        if (into.Count != 0) return;

        try
        {
            ReadOnlySpan<Vehicle> inFrame = Program.VehiclesInFrame;
            for (int i = 0; i < inFrame.Length; i++)
            {
                if (inFrame[i] is { IsDisposed: false } v) into.Add(v);
            }
        }
        catch
        {
            // Nothing more to try.
        }
    }

    public static double3 PositionEcl(Vehicle v) => v.GetPositionEcl();

    public static double3 VelocityEcl(Vehicle v) => v.GetVelocityEcl();

    /// <summary>
    /// Whether a celestial body sits between two points — the planet in the way.
    ///
    /// <para>Every body in the system, not just the one being orbited: a marker hidden behind a
    /// moon is as unusable as one hidden behind the world under it.</para>
    /// </summary>
    /// <param name="blockedBy">The first body found in the way, or empty.</param>
    public static bool IsOccluded(double3 eyeEcl, double3 targetEcl, out string blockedBy)
        => IsOccluded(eyeEcl, targetEcl, 0.0, out blockedBy);

    /// <inheritdoc cref="IsOccluded(double3, double3, out string)"/>
    /// <param name="terrainMargin">
    /// Metres to inflate every body by, so a contact skimming the limb counts as hidden.
    /// </param>
    public static bool IsOccluded(double3 eyeEcl, double3 targetEcl, double terrainMargin,
                                  out string blockedBy)
    {
        blockedBy = string.Empty;
        try
        {
            if (Universe.CurrentSystem is not { } system) return false;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not Celestial body) continue;
                if (!LineOfSight.BlockedByTerrain(eyeEcl, targetEcl, body.GetPositionEcl(),
                                                  body.MeanRadius, terrainMargin))
                {
                    continue;
                }

                blockedBy = body.Id ?? string.Empty;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Anchors an ecliptic position to the body under it, as an offset in that body's own frame.
    ///
    /// <para>The only description of a place that does not move. An ecliptic coordinate is left
    /// behind at the body's ~29.8 km/s the instant it is written down, and a round sent to one
    /// reads that whole frame velocity as closing speed.</para>
    /// </summary>
    public static bool TryAnchorToGround(double3 pointEcl, out object? body, out double3 anchor)
    {
        body = null;
        anchor = Vec.Zero;

        try
        {
            if (Universe.CurrentSystem is not { } system) return false;

            Celestial? nearest = null;
            double nearestRange = double.MaxValue;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not Celestial candidate) continue;

                double range = Vec.Len(pointEcl - candidate.GetPositionEcl()) - candidate.MeanRadius;
                if (range >= nearestRange) continue;

                nearest = candidate;
                nearestRange = range;
            }

            if (nearest is null) return false;

            body = nearest;
            anchor = doubleQuat.Conjugate(nearest.GetBodyFixed2Ecl()) * (pointEcl - nearest.GetPositionEcl());
            return Vec.IsFinite(anchor);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Turns a ground anchor back into a live position and the velocity of the ground there.
    ///
    /// <para>The velocity carries the body's orbital motion <em>and</em> its spin. The spin term
    /// is not decoration: 465 m/s at Earth's equator is 4.6 km of miss over a ten-second flight,
    /// which would read as the round simply being inaccurate.</para>
    ///
    /// <para>Spin is about body-fixed <c>+Z</c>. Read out of the engine, not assumed:
    /// <c>IParentBody.GetAngularVelocityCci</c> returns <c>(0, 0, GetAngularVelocity())</c> and
    /// <c>Celestial.GetCcf2Cci</c> turns about <c>double3.UnitZ</c>.</para>
    /// </summary>
    public static bool TryGroundAnchorEcl(object? body, double3 anchor,
                                          out double3 positionEcl, out double3 velocityEcl)
    {
        positionEcl = Vec.Zero;
        velocityEcl = Vec.Zero;

        if (body is not Celestial celestial) return false;

        try
        {
            doubleQuat spin = celestial.GetBodyFixed2Ecl();
            double3 offset = spin * anchor;

            positionEcl = celestial.GetPositionEcl() + offset;

            // Orbital motion, plus the surface sweeping under it. omega x r, with omega taken
            // about the spin axis the body's own frame defines.
            double3 axis = Vec.Unit(spin * new double3(0, 0, 1));
            velocityEcl = celestial.GetVelocityEcl()
                          + Vec.Cross(axis * celestial.GetAngularVelocity(), offset);

            return Vec.IsFinite(positionEcl) && Vec.IsFinite(velocityEcl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where the cursor's ray meets a celestial surface, as a place a craft can be put.
    ///
    /// <para>Nearest body hit, not the one being orbited: pointing at a moon on the horizon should
    /// mean the moon. The mean sphere, so a mountain is not accounted for — the engine's own
    /// placement settles the craft onto the real terrain, and this only has to say where.</para>
    /// </summary>
    public static bool TryCursorGroundPoint(out double3 groundEcl,
                                            out double latitudeDeg, out double longitudeDeg,
                                            out string bodyName)
    {
        groundEcl = default;
        latitudeDeg = 0.0;
        longitudeDeg = 0.0;
        bodyName = string.Empty;

        try
        {
            if (!TryCursorRayEcl(out double3 eye, out double3 direction)) return false;
            if (Universe.CurrentSystem is not { } system) return false;
            Celestial? nearest = null;
            double3 nearestHit = default;
            double nearestRange = double.MaxValue;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not Celestial body) continue;

                double3 centre = body.GetPositionEcl();
                if (!Picking.TryHitSphere(eye, direction, centre, body.MeanRadius, out double3 hit))
                {
                    continue;
                }

                double range = Vec.Len2(hit - eye);
                if (range >= nearestRange) continue;

                nearest = body;
                nearestHit = hit;
                nearestRange = range;
            }

            if (nearest is null) return false;

            // The mean sphere is not the surface. A ray at a mountain -- or at a launch pad --
            // meets the real surface well before the sphere, so the answer taken from that first
            // hit lands past where the pointer is. Re-intersect against the height under the
            // answer until it stops moving; three passes is plenty short of a cliff edge.
            //
            // The height goes into the *radius*, never added to the point afterwards. Raising a
            // hit radially moves it off the ray, and a point off the ray is not under the cursor:
            // that error is zero at ground level and grows with every metre of elevation, which
            // is what made the marker drift furthest over the pad.
            double3 centreEcl = nearest.GetPositionEcl();
            for (int pass = 0; pass < 3; pass++)
            {
                double3 dirCce = Vec.Unit(nearestHit - centreEcl);
                if (!Vec.IsFinite(dirCce) || Vec.Len(dirCce) < 0.5) break;

                double height = nearest.GetTerrainHeightFromDirCce(dirCce, accurate: true);
                if (!double.IsFinite(height)) break;

                double radius = nearest.MeanRadius + height + LaunchPadHeight(nearest, dirCce);
                if (!Picking.TryHitSphere(eye, direction, centreEcl, radius, out double3 refined))
                {
                    // Grazing: the raised surface is missed where the mean sphere was caught.
                    // The last good answer is closer than none.
                    break;
                }

                nearestHit = refined;
            }

            double3 cce = nearestHit - centreEcl;
            groundEcl = nearestHit;
            latitudeDeg = nearest.GetLatitudeFromCce(cce);
            longitudeDeg = nearest.GetLongitudeFromCce(cce);
            bodyName = nearest.Id ?? string.Empty;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Mirrors Vehicle.GetInitialKinematicStateForLocation, which is private and is what actually
    // places the craft: within 40 m of a launch-pad landmark it stands 8 m up, on the pad. These
    // numbers are the engine's, not ours -- if they move, the marker and the landing part company.
    private static double LaunchPadHeight(Celestial body, double3 dirCce)
    {
        try
        {
            if (body.BodyTemplate is not { } template) return 0.0;

            double3 dirCcf = dirCce.Transform(body.GetCce2Ccf());

            foreach (LocationReference location in template.Locations)
            {
                if (location is not LandmarkReference { IsLaunchPad: true } pad) continue;
                if (Vec.Len(pad.ForwardCcf - dirCcf) * body.MeanRadius < 40.0) return 8.0;
            }

            return 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>
    /// Sets a craft down at a latitude and longitude on the body it is nearest.
    ///
    /// <para><c>Vehicle.TeleportToLocation</c> does the work, and doing it this way rather than by
    /// writing a position is what makes the craft arrive upright and resting: it builds the
    /// kinematic state from the craft's own bounding box, so the hull ends up on the ground rather
    /// than the origin.</para>
    /// </summary>
    public static bool TryPlaceOnSurface(Vehicle craft, string bodyName,
                                         double latitudeDeg, double longitudeDeg)
    {
        if (!IsAlive(craft)) return false;
        if (!double.IsFinite(latitudeDeg) || !double.IsFinite(longitudeDeg)) return false;

        try
        {
            if (Universe.CurrentSystem is not { } system) return false;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not Celestial body) continue;
                if (body.Id != bodyName) continue;

                craft.TeleportToLocation(body, latitudeDeg, longitudeDeg);
                return true;
            }

            return false;
        }
        catch (Exception e)
        {
            Log.Warn($"could not place {DisplayName(craft)}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// How large something at <paramref name="atEcl"/> appears on screen, in pixels.
    ///
    /// <para>Measured by projecting a point one radius to the camera's right rather than by
    /// reconstructing the field of view: the projection already knows the lens, and asking it
    /// twice cannot disagree with itself.</para>
    /// </summary>
    public static bool TryApparentRadiusPixels(double3 atEcl, double metres, out float pixels)
    {
        pixels = 0f;
        if (!double.IsFinite(metres) || metres <= 0.0) return false;

        try
        {
            if (Program.GetMainCamera() is not { } camera) return false;

            double3 right = camera.GetRightEcl();
            if (!Vec.IsFinite(right) || Vec.Len(right) < 0.5) return false;

            if (!TryProjectAhead(atEcl, out float2 centre)) return false;
            if (!TryProjectAhead(atEcl + Vec.Unit(right) * metres, out float2 edge)) return false;

            float dx = edge.X - centre.X, dy = edge.Y - centre.Y;
            pixels = MathF.Sqrt(dx * dx + dy * dy);
            return float.IsFinite(pixels);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where a craft stands: the surface point under it, and its latitude and longitude.
    ///
    /// <para>Placing something at a craft has to use the craft's own position rather than the
    /// ground the cursor ray reaches. A ray through a vehicle's middle carries on and meets the
    /// ground <em>behind</em> it, so aiming at a craft and using the ray puts the answer a
    /// vehicle-height's worth of parallax past it.</para>
    /// </summary>
    public static bool TryCraftSurfacePoint(Vehicle craft, out double3 groundEcl,
                                            out double latitudeDeg, out double longitudeDeg,
                                            out string bodyName)
    {
        groundEcl = default;
        latitudeDeg = 0.0;
        longitudeDeg = 0.0;
        bodyName = string.Empty;

        if (!IsAlive(craft)) return false;

        try
        {
            if (craft.Parent is not Celestial body) return false;

            double3 cce = PositionEcl(craft) - body.GetPositionEcl();
            if (!Vec.IsFinite(cce) || Vec.Len(cce) < 1.0) return false;

            groundEcl = body.GetSurfacePositionEclFromCce(cce);
            latitudeDeg = body.GetLatitudeFromCce(cce);
            longitudeDeg = body.GetLongitudeFromCce(cce);
            bodyName = body.Id ?? string.Empty;
            return Vec.IsFinite(groundEcl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Where a save keeps its files, or false if it has no folder on disk.</summary>
    public static bool TrySaveFolder(string saveId, out string folder)
    {
        folder = string.Empty;
        if (string.IsNullOrWhiteSpace(saveId)) return false;

        try
        {
            string path = Path.Combine(GameSaves.SaveFolderPath, saveId);
            if (!Directory.Exists(path)) return false;

            folder = path;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// When the open save was last written, as ticks, or 0 if there is none.
    ///
    /// <para>The only way to notice the player saving without patching the engine: StarMap has no
    /// save hook, so the file's own timestamp is the event. Watching it is what lets the mod write
    /// its settings <em>when the game writes</em> rather than continuously — and a continuous
    /// write is what makes reloading a save unable to restore anything, because the file has
    /// already been brought up to date with the session.</para>
    /// </summary>
    public static long CurrentSaveStamp()
    {
        try
        {
            if (GameSaves.Selected?.Id is not { Length: > 0 } id) return 0;

            string folder = Path.Combine(GameSaves.SaveFolderPath, id);
            string universe = Path.Combine(folder, "universe.xml");

            if (File.Exists(universe)) return File.GetLastWriteTimeUtc(universe).Ticks;
            return Directory.Exists(folder) ? Directory.GetLastWriteTimeUtc(folder).Ticks : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// The Ecl position that renders at a given Ego position.
    ///
    /// <para>The inverse of the conversion everything else does, and it exists for one case:
    /// handing a point to a system that takes Ecl — the particle emitters — when what is known is
    /// where something is <em>drawn</em>. A vehicle's drawn position and its
    /// <c>GetPositionEcl</c> are not the same place.</para>
    /// </summary>
    public static bool TryEgoToEcl(double3 ego, out double3 ecl)
    {
        ecl = default;
        try
        {
            if (Program.GetMainCamera() is not { } camera) return false;

            ecl = camera.EgoToEcl(ego);
            return Vec.IsFinite(ecl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Which save the player is in, for scoping anything the mod writes down.
    ///
    /// <para>KSA's own save format cannot be extended — <c>UniverseData</c> is a fixed
    /// XML-mapped class with no room for a mod — and StarMap has no save or load hook. So this is
    /// the next best thing: the save's Id, used to key the mod's own file.</para>
    ///
    /// <para><c>GameSaves.Selected</c> is set when a save is picked in the browser, which is what
    /// happens immediately before loading one. It is not a guaranteed "currently loaded" pointer,
    /// and it is empty on a sandbox that was never loaded from a save — hence the caller's
    /// fallback bucket rather than an assumption.</para>
    /// </summary>
    public static string CurrentSaveId()
    {
        try
        {
            return GameSaves.Selected?.Id ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Rough size of a vehicle, used to scale hit and blast checks.</summary>
    public static double MeanRadius(Vehicle v)
    {
        double r = v.MeanRadius;
        return double.IsFinite(r) && r > 0.0 ? r : 5.0;
    }

    public static string DisplayName(Vehicle v)
    {
        try { return string.IsNullOrEmpty(v.Id) ? "unnamed" : v.Id; }
        catch { return "unnamed"; }
    }

    /// <summary>
    /// Local "up" at the platform: the radial-out direction from whatever body it is bound to.
    /// This is the natural boresight for a defence site. Falls back to the platform's velocity
    /// direction, and finally to +Z, so the caller always gets a usable unit vector.
    /// </summary>
    public static double3 LocalUp(Vehicle platform)
    {
        try
        {
            if (platform.Parent is IPosition parent)
            {
                double3 radial = platform.GetPositionEcl() - parent.GetPositionEcl();
                double3 up = Vec.Unit(radial);
                if (!up.Equals(Vec.Zero)) return up;
            }
        }
        catch
        {
            // Parent can be null or mid-transition during scene changes; fall through.
        }

        double3 alongTrack = Vec.Unit(platform.GetVelocityEcl());
        return alongTrack.Equals(Vec.Zero) ? new double3(0, 0, 1) : alongTrack;
    }

    /// <summary>
    /// Gravitational acceleration at <paramref name="positionEcl"/> from the platform's parent body,
    /// in Ecl. Returns zero if the parent or its gravity parameter is unavailable.
    /// </summary>
    public static double3 GravityAt(Vehicle platform, double3 positionEcl)
    {
        try
        {
            if (platform.Parent is not IPosition parent) return Vec.Zero;

            double mu = platform.Parent.Mu;
            if (mu <= 0.0) return Vec.Zero;

            double3 toBody = parent.GetPositionEcl() - positionEcl;
            double dist2 = Vec.Len2(toBody);
            if (dist2 < 1.0) return Vec.Zero;

            return Vec.Unit(toBody) * (mu / dist2);
        }
        catch
        {
            return Vec.Zero;
        }
    }

    /// <summary>
    /// Density of whatever the round is flying through, as a multiple of the parent body's
    /// sea-level air density.
    ///
    /// <para>1.0 at sea level, 0.0 in vacuum and above the atmosphere, and roughly 840 below the
    /// waterline. A <em>ratio</em> rather than an absolute density so a munition's drag
    /// coefficient keeps meaning what it did when it was tuned; one scale covers air and water, so
    /// a torpedo simply carries a much smaller <see cref="MunitionProfile.DragK"/>.</para>
    ///
    /// <para>Falls back to 1.0, not 0.0, when the atmosphere cannot be read: a round that keeps
    /// its tuned drag is a far less confusing failure than one that silently loses all of it and
    /// flies several times further.</para>
    /// </summary>
    public static double MediumDensityRatioAt(Vehicle platform, double3 positionEcl)
    {
        try
        {
            if (platform.Parent is not IPosition parent) return 1.0;
            if (platform.Parent is not Celestial body) return 1.0;

            AtmosphereReference? atmosphere = body.GetAtmosphereReference();
            if (atmosphere?.Physical is not { } air || !air.IsValid()) return 0.0;

            double seaLevel = air.SeaLevelDensity;
            if (!(seaLevel > 0.0)) return 0.0;

            // Altitude above the mean surface, the same measure KSA's own physics uses.
            double altitude = Vec.Len(positionEcl - parent.GetPositionEcl()) - body.MeanRadius;

            // Below the waterline the medium is the ocean, which is ~840x sea-level air. The
            // ratio is therefore not bounded above by 1.
            OceanReference? ocean = body.GetOceanReference();
            if (ocean is { } sea && sea.IsValid() && altitude < sea.Level)
            {
                double water = sea.Density / seaLevel;
                return double.IsFinite(water) && water > 0.0 ? water : 1.0;
            }

            if (altitude < 0.0) altitude = 0.0;
            if (altitude >= air.Height) return 0.0;

            double ratio = air.GetAtmosphericDensityAtAltitude(altitude) / seaLevel;
            return double.IsFinite(ratio) && ratio >= 0.0 ? ratio : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>
    /// Blocks until KSA's vehicle solver jobs have finished the step they are working on.
    ///
    /// <para><b>Required before destroying a vehicle from a mod hook.</b> Disposing a vehicle
    /// removes it from the update task's <c>_vehicleStates</c>, which is the list
    /// <c>VehicleUpdateTask.DoWorkAndStageResults</c> enumerates on a worker thread — the dispose
    /// surfaces as <c>InvalidOperationException: Collection was modified</c> inside the
    /// engine.</para>
    ///
    /// <para>No mod hook sits in the safe window. <c>PrepareFrame</c> takes this barrier at
    /// <c>Program.cs:1984</c> and re-dispatches the jobs at <c>:2020</c>, while the GUI hook fires
    /// at <c>:2068</c> and the frame hook later still — so moving the call between hooks cannot
    /// help. Taking the barrier costs a stall only on frames where something dies, and those jobs
    /// had to finish before the next <c>PrepareFrame</c> anyway.</para>
    /// </summary>
    public static void WaitForVehicleSolvers()
    {
        try
        {
            JobSystems.VehicleSolvers?.Wait();
        }
        catch (Exception e)
        {
            // Falling through to the destroy leaves the race in place; taking the frame down is
            // worse.
            Log.Warn($"could not join the vehicle solvers before a kill ({e.GetType().Name})");
        }
    }

    /// <summary>
    /// Destroys a vehicle, attributing it to collision damage. Must be called from the main
    /// thread, and only after <see cref="WaitForVehicleSolvers"/> — see there for why.
    /// </summary>
    public static void Destroy(Vehicle v, float blastSeverity)
    {
        if (!IsAlive(v)) return;
        try
        {
            var evt = new VehicleDestructionEvent
            {
                Cause = VehicleDestructionCause.Collision,
                PeakGLoad = blastSeverity,
                PeakDynamicPressure = blastSeverity,
            };
            Universe.DestroyVehicleFromEvent(v, evt);
        }
        catch (Exception e)
        {
            Log.Warn($"failed to destroy {DisplayName(v)}: {e.Message}");
        }
    }

    // ---- Rendering ------------------------------------------------------

    // The overlay is drawn relative to an anchor vehicle rather than by converting absolute
    // positions. See BeginDraw for why.
    private static DrawAnchor _anchor;
    private static bool _anchored;

    /// <summary>
    /// Establishes the frame for this draw pass. Call once before any Draw*Ecl call.
    ///
    /// <para>Naive conversion — <c>camera.EclToEgo(v.GetPositionEcl())</c> — puts the overlay in
    /// the wrong place. <see cref="Vehicle.GetPositionEcl"/> returns a value computed in
    /// <c>UpdatePerFrameData</c> from <c>Orbit.StateVectors</c>, i.e. the analytic on-rails
    /// position. A landed craft is held where physics puts it, not where its degenerate Kepler
    /// orbit says, and the two differ by enough to be obvious on screen.</para>
    ///
    /// <para>KSA renders vehicles via <c>camera.GetPositionEgo(vehicle)</c>, which returns
    /// <c>-PositionCce</c> for the followed craft and uses <c>KinematicStates.PositionPhys</c>
    /// for others in the same bubble — the physics position in both cases. Anchoring to that and
    /// adding Ecl offsets (exact, since Ego is a pure translation of Ecl) puts our overlay
    /// exactly where the game draws the craft.</para>
    /// </summary>
    /// <param name="anchorEcl">
    /// The anchor's Ecl position **captured at the same instant as everything else being
    /// drawn** — not re-read here. Ecliptic positions near Earth sweep past at ~29.8 km/s, so
    /// a reference taken one frame later than the geometry it is differenced against is about
    /// 500 m stale at 60 fps, and the whole overlay lands that far from the craft.
    /// </param>
    public static bool BeginDraw(Vehicle anchor, double3 anchorEcl)
    {
        _anchored = false;
        if (!IsAlive(anchor)) return false;

        try
        {
            // The camera the frame will actually be rendered with, not the main viewport's.
            Camera camera = Program.GetRenderCamera() ?? Program.GetMainCamera();
            if (camera is null) return false;

            // See DrawAnchor for why these are sampled at different instants, and why
            // collapsing them into one puts the whole overlay beside the craft.
            //
            // EclToEgo rather than GetPositionEgo: the latter picks a different branch depending
            // on what the camera follows, so the anchor shifts basis mid-engagement when the
            // player switches view. EclToEgo is a pure translation and behaves identically
            // whatever the camera is doing.
            // GetPositionEgo, not EclToEgo. Its branching on what the camera follows is the
            // engine answering correctly per case — exact for the followed craft, physics-based
            // for others in its bubble — and it is the same call KSA renders vehicles with.
            // EclToEgo instead measures against camera.PositionEcl, which only agrees with the
            // rendered scene when the followed craft's analytic and physics positions coincide.
            // That holds for a landed launcher and fails once the camera follows something in
            // flight, which is when anything drawn to a vehicle stops lining up.
            _anchor = new DrawAnchor(camera.GetPositionEgo(anchor), anchorEcl);

            if (!_anchor.IsValid) return false;


            _anchored = true;
            return true;
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// The anchor's position in the render frame, straight from the engine. Drawing here uses
    /// none of our own arithmetic, so it isolates "is the anchor right" from "is the Ecl offset
    /// maths right".
    /// </summary>
    public static double3 AnchorEgo => _anchor.Ego;

    public static bool HasAnchor => _anchored;

    /// <summary>Converts an Ecl position into the anchored Ego frame.</summary>
    public static bool TryEclToEgo(double3 posEcl, out double3 posEgo)
    {
        if (_anchored)
        {
            posEgo = _anchor.ToEgo(posEcl);
            return true;
        }

        posEgo = Vec.Zero;
        return false;
    }

    /// <summary>
    /// Ego position of a vehicle, straight from the engine, so track markers sit on the craft
    /// rather than on its analytic orbit position.
    /// </summary>
    /// <summary>
    /// Ego position of a vehicle. Uses the anchored conversion for the same reason
    /// <see cref="BeginDraw"/> does — <c>GetPositionEgo</c> would place this marker on a
    /// different basis to the rest of the overlay whenever the camera follows something else.
    /// </summary>
    public static bool TryVehicleEgo(Vehicle v, out double3 posEgo)
    {
        posEgo = Vec.Zero;
        if (!IsAlive(v)) return false;

        // Ask the engine, for the same reason BeginDraw does: this is where the vehicle is
        // actually being drawn. Deriving it from GetPositionEcl gives the analytic on-rails
        // position instead, and lines drawn to it visibly miss the craft.
        try
        {
            Camera camera = Program.GetRenderCamera() ?? Program.GetMainCamera();
            if (camera is not null)
            {
                posEgo = camera.GetPositionEgo(v);
                if (Vec.IsFinite(posEgo)) return true;
            }
        }
        catch
        {
            // Fall back to the anchored conversion.
        }

        return TryEclToEgo(PositionEcl(v), out posEgo);
    }

    public static void DrawSphereEgo(double3 positionEgo, float radiusMetres, float4 colour)
    {
        Program.GizmosRenderer?.DrawSphere(positionEgo, radiusMetres, colour);
    }

    public static void DrawLineEgo(double3 startEgo, double3 endEgo, float4 colour)
    {
        Program.GizmosRenderer?.DrawLine(startEgo, endEgo, colour);
    }

    public static void DrawSphereEcl(double3 positionEcl, float radiusMetres, float4 colour)
    {
        if (Program.GizmosRenderer is null) return;
        if (!TryEclToEgo(positionEcl, out double3 ego)) return;
        Program.GizmosRenderer.DrawSphere(ego, radiusMetres, colour);
    }

    /// <summary>How many camera views the game currently has open.</summary>
    public static int ViewportCount
    {
        get
        {
            try { return Program.Viewports?.Count ?? 0; }
            catch { return 0; }
        }
    }

    /// <summary>
    /// Indices of the camera windows a player can actually see.
    ///
    /// <para>KSA keeps viewports of its own that are never shown — the thumbnail renderer is
    /// one — and they are indistinguishable from real windows by index alone. Driving one looks
    /// exactly like the feature not working.</para>
    /// </summary>
    public static void CollectUsableViewports(List<int> into)
    {
        into.Clear();
        try
        {
            if (Program.Viewports is not { } viewports) return;

            // Index 0 is the view the player flies from; taking it is a different feature.
            for (int i = 1; i < viewports.Count; i++)
            {
                if (viewports[i] is { Visible: true, IsOffscreen: false }) into.Add(i);
            }
        }
        catch
        {
            into.Clear();
        }
    }

    /// <summary>
    /// Where a point in Ecl lands on screen inside one viewport, in absolute screen pixels.
    ///
    /// <para>The camera projects into its own framebuffer, so the result is viewport-local and
    /// has to be offset by where that window sits — otherwise an overlay drawn from it lands on
    /// the main view instead.</para>
    ///
    /// <para>False when the point is behind the camera or outside the window, which is the
    /// caller's cue to draw nothing rather than to clamp it to an edge.</para>
    /// </summary>
    public static bool TryProjectIntoViewport(int index, double3 pointEcl, out float2 screen,
                                              out int width, out int height)
    {
        screen = default;
        width = height = 0;
        try
        {
            if (Program.Viewports is not { } viewports) return false;
            if (index < 0 || index >= viewports.Count) return false;

            Viewport viewport = viewports[index];
            Camera camera = viewport.Mode == CameraMode.Fixed ? viewport.BaseCamera
                                                              : viewport.GetCamera();
            if (camera is null) return false;

            width = viewport.Width;
            height = viewport.Height;
            if (width <= 0 || height <= 0) return false;

            float2 local = camera.EclToScreen(pointEcl, ignoreBehind: false);
            if (!float.IsFinite(local.X) || !float.IsFinite(local.Y)) return false;
            if (local.X < 0f || local.Y < 0f || local.X > width || local.Y > height) return false;

            screen = new float2(viewport.Position.X + local.X, viewport.Position.Y + local.Y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Vertical field of view of a viewport's camera (rad), for scaling an overlay.</summary>
    public static double ViewportFovRad(int index)
    {
        try
        {
            Camera camera = Program.Viewports[index].GetCamera();
            // GetFieldOfView reports degrees; everything here works in radians.
            return camera is null ? 1.0 : double.DegreesToRadians(camera.GetFieldOfView());
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>
    /// Reports how the cursor is being turned into a ray, and how far the answer lands from the
    /// pointer once projected back. The round trip is the measurement that matters: it is the
    /// error actually on screen, whatever the intermediate conventions turn out to be.
    /// </summary>
    public static string DescribeCursorRay(double3 solvedEcl)
    {
        try
        {
            float2 cursor = ImGui.GetMousePos();
            ImGuiViewportPtr main = ImGui.GetMainViewport();

            string chosen = "none";
            for (int i = 0; i < Program.Viewports.Count; i++)
            {
                Viewport v = Program.Viewports[i];
                if (!v.Visible || v.IsOffscreen) continue;
                if (!CursorAim.TryToViewport(cursor, v.Position, v.Width, v.Height, out float2 local))
                {
                    continue;
                }

                Camera? c = v.GetCamera();
                chosen = $"vp{i} pos={v.Position.X:F0},{v.Position.Y:F0} "
                         + $"size={v.Width}x{v.Height} fb={c?.FramebufferSize.X}x{c?.FramebufferSize.Y} "
                         + $"local={local.X:F0},{local.Y:F0}";
                break;
            }

            string back = TryProjectAhead(solvedEcl, out float2 screen)
                              ? $"back={screen.X:F0},{screen.Y:F0} "
                                + $"err={screen.X - cursor.X:F0},{screen.Y - cursor.Y:F0}"
                              : "back=offscreen";

            return $"cursor={cursor.X:F0},{cursor.Y:F0} "
                   + $"mainvp={main.Pos.X:F0},{main.Pos.Y:F0} {main.Size.X:F0}x{main.Size.Y:F0} "
                   + $"{chosen} {back}";
        }
        catch (Exception e)
        {
            return $"(describe failed: {e.Message})";
        }
    }

    public static bool TryCursorRayEcl(out double3 originEcl, out double3 directionEcl)
    {
        originEcl = default;
        directionEcl = default;
        try
        {
            float2 cursor = ImGui.GetMousePos();

            for (int i = 0; i < Program.Viewports.Count; i++)
            {
                Viewport v = Program.Viewports[i];
                if (!v.Visible || v.IsOffscreen) continue;

                if (v.GetCamera() is not { } camera) continue;

                // Framebuffer pixels, not viewport pixels: ScreenToEgoRay divides by the camera's
                // own framebuffer, and a render or display scale makes those different sizes.
                if (!CursorAim.TryToFramebuffer(cursor, v.Position, v.Width, v.Height,
                                                camera.FramebufferSize.X, camera.FramebufferSize.Y,
                                                out float2 local))
                {
                    continue;
                }

                double3 direction = camera.ScreenToEgoRay(local).Direction;
                if (!CursorAim.IsUsableDirection(direction)) continue;

                originEcl = camera.EgoToEcl(Vec.Zero);
                directionEcl = Vec.Unit(direction);
                return Vec.IsFinite(originEcl);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where the cursor points, as a bearing from <paramref name="mountEcl"/>.
    ///
    /// <para>Takes a mount because a bearing without an origin is not an aim. The camera stands
    /// well away from any launcher on screen, so its direction and the launcher's coincide only
    /// for something at infinity: against sky they agree, and against ground a few tens of metres
    /// off they disagree by tens of degrees. There is deliberately no direction-only form of this
    /// next to it — the two are indistinguishable wherever anyone tests them first.</para>
    ///
    /// <para>The ray and the range are solved once a frame and shared, so several batteries on
    /// mouse aim cost one terrain query between them rather than one each.</para>
    /// </summary>
    public static bool TryCursorAimEcl(double3 mountEcl, out double3 directionEcl)
    {
        directionEcl = default;
        SolveCursorAim();

        return _cursorAimValid
               && CursorAim.TryAimFromMount(_cursorAimOrigin, _cursorAimDirection, _cursorAimRange,
                                            mountEcl, out directionEcl);
    }

    /// <summary>
    /// Drops the frame's cached cursor solve. Called once where the simulation is stepped.
    /// </summary>
    public static void BeginFrame() => _cursorAimSolved = false;

    // Taken as the range when the ray meets no body: far enough that the camera-to-launcher
    // parallax is under what the drives can resolve, at 100 m of offset about 0.3 degrees.
    private const double CursorSkyRange = 20_000.0;

    private static bool _cursorAimSolved;
    private static bool _cursorAimValid;
    private static double3 _cursorAimOrigin;
    private static double3 _cursorAimDirection;
    private static double _cursorAimRange;

    private static void SolveCursorAim()
    {
        if (_cursorAimSolved) return;

        _cursorAimSolved = true;
        _cursorAimValid = false;

        if (!TryCursorRayEcl(out double3 eye, out double3 direction)) return;

        // The terrain-refined hit, not the mean sphere: the sphere is sea level, so over a pad it
        // sits below the ground and puts the aim point past what the pointer is actually on.
        double range = TryCursorGroundPoint(out double3 ground, out _, out _, out _)
                           ? Vec.Len(ground - eye)
                           : CursorSkyRange;

        if (!double.IsFinite(range) || range <= 0.0) range = CursorSkyRange;

        _cursorAimOrigin = eye;
        _cursorAimDirection = direction;
        _cursorAimRange = range;
        _cursorAimValid = true;
    }

    /// <summary>The character this mod declares, whose kitten carries the shoulder cannon.</summary>
    public const string ArmedCharacterId = "KSArmoryArmedKitten";



    /// <summary>
    /// <summary>Ids of the armed character's declarations, and whether the game resolved each.</summary>
    ///
    /// <para>The chain fails silently at every link. An unresolved attachment is skipped inside
    /// <c>CharacterAvatar</c>'s null check, and a glTF that will not load is skipped by the same
    /// one — no warning, no error, just a kitten with no gun. Asking each link separately is the
    /// only way to tell which one gave way.</para>
    public static void CollectArmedChain(List<(string What, string Id, bool Resolved)> into)
    {
        into.Clear();
        into.Add(("character", ArmedCharacterId, Resolves<CharacterReference>(ArmedCharacterId)));
        into.Add(("attachment", ArmedAttachmentId,
                  Resolves<CharacterAttachmentReference>(ArmedAttachmentId)));
        into.Add(("mesh", ArmedGltfId, Resolves<Gltf2Reference>(ArmedGltfId)));
    }

    /// <summary>The attachment declaring the gun, and the glTF it draws.</summary>
    public const string ArmedAttachmentId = "KSArmoryKittenGunAttachment";
    public const string ArmedGltfId = "KSArmoryKittenGunGlb";

    /// <summary>
    /// The character a vehicle is wearing, or null when it is not a kitten.
    ///
    /// <para>The one fact that separates "the gun is not rendering" from "this kitten was never
    /// armed": a KittenEva takes its character in its constructor, so one that was walking before
    /// the roster changed still reports the body it was born with.</para>
    /// </summary>
    public static string? CharacterOf(Vehicle? vehicle)
    {
        try
        {
            return vehicle is KittenEva kitten ? kitten.Character?.Id : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool Resolves<T>(string id) where T : IKeyed
    {
        try
        {
            return ModLibrary.Get<T>(id) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Projects a world point onto the main viewport, culling anything behind the camera.
    ///
    /// <para>Distinct from <see cref="TryProjectIntoViewport"/>, which passes
    /// <c>ignoreBehind: false</c>. That is right for the gunner's sight, whose head is pointed at
    /// its target and so cannot be looking away from it, and wrong for a marker over an arbitrary
    /// craft: <c>EgoToScreen</c> only tests the point against the camera's forward when asked, so
    /// without it a site *behind* you draws a bracket in front of you.</para>
    /// </summary>
    public static bool TryProjectAhead(double3 pointEcl, out float2 screen)
    {
        screen = default;
        try
        {
            if (Program.MainViewport is not { } viewport) return false;
            if (viewport.GetCamera() is not { } camera) return false;
            if (viewport.Width <= 0 || viewport.Height <= 0) return false;

            float2 local = camera.EclToScreen(pointEcl, ignoreBehind: true);
            if (!float.IsFinite(local.X) || !float.IsFinite(local.Y)) return false;
            if (local.X < 0f || local.Y < 0f || local.X > viewport.Width || local.Y > viewport.Height)
            {
                return false;
            }

            screen = new float2(viewport.Position.X + local.X, viewport.Position.Y + local.Y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where a world point sits on screen, and whether it is actually in view.
    ///
    /// <para>For a point that is off-screen or behind, <paramref name="screen"/> comes back
    /// clamped to the edge of the viewport in the direction of the target, so a caller can draw
    /// an indicator pointing at something it cannot see. That is the difference from
    /// <see cref="TryProjectAhead"/>, which simply refuses.</para>
    ///
    /// <para>A point behind the camera has to be handled by hand: a projection matrix maps it to
    /// the *opposite* side of the screen, so an arrow built from it points exactly the wrong way.
    /// The camera basis is public, so the bearing is taken from the direction to the target
    /// against the camera's own right and up, which is well defined for any point that is not
    /// exactly on the axis.</para>
    /// </summary>
    public static bool TryProjectOrClamp(double3 pointEcl, out float2 screen, out bool inView)
    {
        screen = default;
        inView = false;
        try
        {
            if (Program.MainViewport is not { } viewport) return false;
            if (viewport.GetCamera() is not { } camera) return false;

            int w = viewport.Width, h = viewport.Height;
            if (w <= 0 || h <= 0) return false;

            double3 toTarget = pointEcl - camera.EgoToEcl(Vec.Zero);
            bool ahead = Vec.Dot(toTarget, camera.GetForwardEcl()) > 0.0;

            if (ahead)
            {
                float2 local = camera.EclToScreen(pointEcl, ignoreBehind: true);
                if (float.IsFinite(local.X) && float.IsFinite(local.Y)
                    && local.X >= 0f && local.Y >= 0f && local.X <= w && local.Y <= h)
                {
                    inView = true;
                    screen = new float2(viewport.Position.X + local.X, viewport.Position.Y + local.Y);
                    return true;
                }
            }

            // Off-screen or behind: bearing from the camera basis, then out to the edge.
            double right = Vec.Dot(toTarget, camera.GetRightEcl());
            double up = Vec.Dot(toTarget, camera.GetUpEcl());
            if (!double.IsFinite(right) || !double.IsFinite(up)) return false;
            if (Math.Abs(right) < 1e-9 && Math.Abs(up) < 1e-9) return false;

            // Screen Y grows downward, so the camera's up is negated.
            double len = Math.Sqrt(right * right + up * up);
            double dx = right / len, dy = -up / len;

            double halfW = w * 0.5 - EdgeMargin, halfH = h * 0.5 - EdgeMargin;
            double scale = Math.Min(Math.Abs(dx) > 1e-9 ? halfW / Math.Abs(dx) : double.MaxValue,
                                    Math.Abs(dy) > 1e-9 ? halfH / Math.Abs(dy) : double.MaxValue);

            screen = new float2((float)(viewport.Position.X + w * 0.5 + dx * scale),
                                (float)(viewport.Position.Y + h * 0.5 + dy * scale));
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Keeps an edge indicator clear of the very border, where it would be half off-screen.
    private const float EdgeMargin = 28f;

    /// <summary>Where the camera is, in Ecl. Zero if there is none.</summary>
    public static double3 CameraPositionEcl()
    {
        try
        {
            return Program.GetMainCamera() is { } camera ? camera.EgoToEcl(Vec.Zero) : Vec.Zero;
        }
        catch
        {
            return Vec.Zero;
        }
    }



    /// <summary>
    /// Points the camera at a craft and takes control of it.
    ///
    /// <para>The same four steps KSA's own "Control from here" runs, in the same order — follow,
    /// control, match the zoom, then let the vehicle rebuild its derived data. Doing fewer of
    /// them leaves the camera watching one craft while the controls drive another, or the view
    /// snapped to a zoom that belonged to the last vehicle.</para>
    /// </summary>
    /// <returns>False if the craft is gone, or the engine refused any part of it.</returns>
    public static bool GoTo(Vehicle? vehicle)
    {
        if (!IsAlive(vehicle)) return false;

        try
        {
            Camera? camera = Program.GetMainCamera();
            if (camera is null) return false;

            camera.SetFollow(vehicle!, tidalLocking: true);
            Program.ControlledVehicle = vehicle;
            Program.MainViewport.OrbitController.DistancePower = vehicle!.OrbitView.DistancePower;
            vehicle.UpdateAfterPartTreeModification();
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not go to {DisplayName(vehicle!)}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Flattens a craft's part tree into what <see cref="WeaponSurvey"/> takes.
    ///
    /// <para>Position and orientation are read in the vehicle's assembly frame, which is the
    /// frame the parts were placed in, so what comes back is where the player put them. Only the
    /// top-level parts: a subpart is a piece of one part's own articulation, not a component
    /// somebody bolted on.</para>
    /// </summary>
    public static void SurveyParts(Vehicle? vehicle, List<SurveyedPart> into)
    {
        into.Clear();
        if (!IsAlive(vehicle)) return;

        try
        {
            ReadOnlySpan<Part> parts = vehicle!.Parts.Parts;
            for (int i = 0; i < parts.Length; i++)
            {
                Part part = parts[i];
                into.Add(new SurveyedPart(part.Id, part.PositionVehicleAsmb, part.Asmb2VehicleAsmb));
            }
        }
        catch
        {
            // A tree being rebuilt underneath us during staging or docking. Next frame will see
            // the finished one; reporting a half-built craft would be worse than reporting none.
            into.Clear();
        }
    }

    /// <summary>A short description of one open view, for the panel's picker.</summary>
    public static string DescribeViewport(int index)
    {
        try
        {
            Viewport v = Program.Viewports[index];
            return $"Camera {index} ({v.Width}x{v.Height})";
        }
        catch
        {
            return $"#{index}";
        }
    }

    /// <summary>
    /// Reads the main view's orbit camera: the basis it is looking along, and both copies of the
    /// angles behind it.
    ///
    /// <para>The angles live in two places and only one is writable.
    /// <c>Camera.Following.OrbitView</c> holds the stored pair, which is what a mouse drag moves;
    /// <c>OrbitController</c> holds an <em>output</em>, resprung towards it every frame. Writing
    /// the controller's lasts one frame and fights the spring, which looks exactly like jitter.
    /// Read both: the controller's built the camera being measured, so that is what the geometry
    /// solves against, and the view's is what a write has to land on.</para>
    /// </summary>
    public static bool TryReadMainOrbit(out OrbitAim.Reading reading)
    {
        reading = default;
        try
        {
            if (Program.MainViewport is not { } viewport) return false;
            if (viewport.OrbitController is not { } orbit) return false;
            if (Program.GetMainCamera() is not { } camera) return false;
            if (camera.Following?.OrbitView is not { } view) return false;

            reading = new OrbitAim.Reading(camera.GetForwardEcl(), camera.GetRightEcl(),
                                           orbit.Azimuth, orbit.Elevation,
                                           view.Azimuth, view.Elevation);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Moves the main orbit camera to a pair of angles.</summary>
    public static bool TryWriteMainOrbit(double azimuth, double elevation)
    {
        if (!double.IsFinite(azimuth) || !double.IsFinite(elevation)) return false;

        try
        {
            if (Program.GetMainCamera()?.Following?.OrbitView is not { } view) return false;

            view.Azimuth = azimuth;

            // The game clamps it on every path that writes it, so a value past the pole would be
            // one this camera can never report back and the write would look refused forever.
            view.Elevation = Math.Clamp(elevation, -Math.PI / 2.0, Math.PI / 2.0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Puts one viewport's camera at a point in Ecl, looking along a direction.
    ///
    /// <para>Must be written every frame. Each viewport runs a controller that rewrites its
    /// camera from whatever mode it is in, so this holds only for as long as it keeps being
    /// reapplied — and only if it runs after that controller. The GUI hook does, which is why
    /// the call sits there.</para>
    ///
    /// <para>KSA opens views itself; <c>AddViewport</c> is private, so a mod cannot make one. It
    /// can drive one the player has opened, which is the difference between borrowing a window
    /// and stealing the main camera.</para>
    /// </summary>
    /// <summary>
    /// Whether the main view is still in the mode a borrower left it in.
    ///
    /// <para>False means the player has taken it back, which is a decision rather than a fault:
    /// whoever holds it should let go rather than fight, because trading writes over the camera
    /// mode is what puts it into Fixed while still following.</para>
    /// </summary>
    public static bool MainViewIsFixed()
    {
        try
        {
            return Program.MainViewport?.Mode == CameraMode.Fixed;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the main view is currently following this craft.
    ///
    /// <para>Asked before anything borrows the view: a battery on the far side of the world taking
    /// the camera off whatever the player is watching is a hijack, however good the shot.</para>
    /// </summary>
    public static bool MainViewFollows(Vehicle? craft)
    {
        if (craft is null) return false;

        try
        {
            return ReferenceEquals(Program.MainViewport?.GetCamera()?.Following, craft);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Index of the main viewport, for the projection helpers that take one.</summary>
    public static int MainViewportIndex
    {
        get
        {
            try
            {
                return Program.MainViewport?.Index ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>What the main view was doing before something borrowed it.</summary>
    public readonly record struct MainView(IFollowable? Following, CameraMode Mode, bool Valid);

    /// <summary>
    /// Records the main view so it can be handed back.
    ///
    /// <para>Taken before the first write, not after. Setting Fixed clears the follow, so a
    /// reading taken afterwards describes the borrowed state and restoring from it leaves the
    /// player at a fixed point in space with no way home.</para>
    /// </summary>
    public static MainView RememberMainView()
    {
        try
        {
            if (Program.MainViewport is not { } viewport) return default;

            return new MainView(viewport.GetCamera()?.Following, viewport.Mode, true);
        }
        catch (Exception e)
        {
            Log.Warn($"could not read the main view: {e.Message}");
            return default;
        }
    }

    /// <summary>
    /// Points the main view from a place, using Fixed mode as it is meant to be used.
    ///
    /// <para>The camera keeps following whatever it followed. <c>FixedController.OnFrame</c> puts
    /// it at <c>following.GetPositionEcl() + CameraOffset</c> looking along <c>CameraRotation</c>,
    /// so those two fields are the whole interface — and the offset is measured from the followed
    /// craft, not from the world.</para>
    ///
    /// <para><c>CameraRotation</c> must be non-zero before the mode is set. The controller crosses
    /// it with the frame's up and normalises, so a zero vector divides by zero — which is the
    /// entire reason this mode has a reputation for crashing.</para>
    /// </summary>
    /// <summary>
    /// Puts back whatever the view was following before a mod borrowed it.
    ///
    /// <para>Separate from the mode: following something of the mod's own has to be undone even
    /// when the mode never changed, and leaving a camera pointed at an object the mod is about to
    /// forget is how a view ends up stuck on a round that no longer exists.</para>
    /// </summary>
    public static bool RestoreFollow(MainView saved)
    {
        if (!saved.Valid || saved.Following is null) return false;

        try
        {
            if (Program.MainViewport?.GetCamera() is not { } camera) return false;

            camera.SetFollow(saved.Following, tidalLocking: true, changeControl: false, alert: false);
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not restore what the view was following: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Points the main camera at something of the mod's own, so the engine resolves its position
    /// in its own frame pass rather than the mod handing over one sampled somewhere else.
    /// </summary>
    /// <returns>False if the camera could not be reached; the view is then untouched.</returns>
    public static bool TryFollowOnMainViewport(IFollowable target)
    {
        try
        {
            if (Program.MainViewport?.GetCamera() is not { } camera) return false;

            // changeControl false, or Program.ControlledVehicle becomes `target as Vehicle`, which
            // for anything that is not a vehicle is null -- the player loses their craft.
            camera.SetFollow(target, tidalLocking: false, changeControl: false, alert: false);
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not follow: {e.Message}");
            return false;
        }
    }

    /// <param name="offsetFromFollowed">
    /// Where the camera goes <em>relative to the craft the view is following</em>, not an absolute
    /// position. The controller adds it to <c>following.GetPositionEcl()</c> later in the frame,
    /// so an offset derived from that position here is measured against a different instant from
    /// the one it is applied to — which is a frame of the platform's motion, every frame, and
    /// reads as the thing being watched shivering.
    /// </param>
    public static bool TryLookFromMainViewport(double3 offsetFromFollowed, double3 forwardEcl,
                                               double3 upEcl)
    {
        if (!Vec.IsFinite(offsetFromFollowed) || !Vec.IsFinite(forwardEcl)) return false;
        if (Vec.Len2(forwardEcl) < 1e-12) return false;

        try
        {
            if (Program.MainViewport is not { } viewport) return false;
            if (viewport.FixedController is not { } controller) return false;
            if (viewport.GetCamera()?.Following is null) return false;

            // The reference frame is Identity for anything that is not a Vehicle, so the axis the
            // controller crosses against is ecliptic +Z. A view along it divides by zero.
            if (Math.Abs(Vec.Dot(Vec.Unit(forwardEcl), new double3(0, 0, 1))) > 0.999) return false;

            // Set before the mode, every time. A frame drawn in Fixed with a zero rotation is the
            // crash, and setting the mode first leaves exactly that gap.
            controller.CameraRotation = Vec.Unit(forwardEcl);
            controller.CameraOffset = offsetFromFollowed;

            if (viewport.Mode != CameraMode.Fixed) viewport.SetCameraMode(CameraMode.Fixed);

            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not drive the main view: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Puts the main view back in the mode it was found in.
    ///
    /// <para>Only the mode. The follow was never taken away, so there is nothing to re-attach and
    /// no window in which the camera follows in Fixed mode with no rotation set.</para>
    /// </summary>
    public static bool BeginRestoreMainView(MainView saved)
    {
        if (!saved.Valid) return false;

        try
        {
            if (Program.MainViewport is not { } viewport) return false;

            if (viewport.Mode != saved.Mode) viewport.SetCameraMode(saved.Mode);
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not restore the main view: {e.Message}");
            return false;
        }
    }

    public static bool TryLookFromViewport(int index, double3 eyeEcl, double3 forwardEcl,
                                           double3 upEcl, double dt)
    {
        if (!Vec.IsFinite(eyeEcl) || !Vec.IsFinite(forwardEcl) || !Vec.IsFinite(upEcl)) return false;
        if (Vec.Len2(forwardEcl) < 1e-12) return false;

        try
        {
            if (Program.Viewports is not { } viewports) return false;
            if (index < 0 || index >= viewports.Count) return false;

            Viewport viewport = viewports[index];

            // A view in Map mode renders the map scene — starfield, orbits, planets as discs —
            // and GetCamera() hands back the *map* camera, so moving it puts the map somewhere
            // else rather than showing the world from here. Fixed is the mode that draws the
            // scene from wherever its camera happens to be, which is the whole point.
            // Unfollow before Fixed for the same reason as the main view: FixedController
            // divides by zero on its own default CameraRotation whenever the camera it drives is
            // following something. This viewport's camera normally follows nothing, which is why
            // the optical head never met it -- but a player can set one to follow a craft.
            if (viewport.Mode != CameraMode.Fixed)
            {
                try { viewport.GetCamera()?.Unfollow(changeControl: false); } catch { }
                viewport.SetCameraMode(CameraMode.Fixed);
            }

            Camera camera = viewport.BaseCamera;
            if (camera is null) return false;

            // Position and orientation together: setting one without the other leaves a frame
            // drawn from the old place looking the new way.
            camera.LookAt(eyeEcl, eyeEcl + Vec.Unit(forwardEcl) * 1000.0, upEcl);

            // Moving a camera does not tell it where it now is. The sky, the atmosphere and the
            // terrain LOD are all shaded from this context, which the engine derives per camera
            // for its own controller — so a camera the mod has moved keeps whatever its
            // controller last worked out and renders the sky from there. Copied from the main
            // view, which is metres away and has it right.
            if (Program.GetMainCamera() is { } reference)
            {
                camera.NearbyCelestial = reference.NearbyCelestial;
                camera.CurrentAltitudeKm = reference.CurrentAltitudeKm;
                camera.DistanceToNearbyCelestialKm = reference.DistanceToNearbyCelestialKm;
                camera.DistanceToNearbyCelestialSurfaceMeanKm =
                    reference.DistanceToNearbyCelestialSurfaceMeanKm;
                camera.NearbyCelestialTerrainHeight = reference.NearbyCelestialTerrainHeight;
            }

            // Setting the fields is not enough on its own: the sky and atmosphere are shaded from
            // data the engine uploads per viewport, which by this point in the frame already holds
            // where the camera *was*. These recompute and re-upload it for the new position.
            if (Program.Instance is { } program)
            {
                program.UpdateShaderData(dt, viewport);
                program.SetCameraUbo(viewport);
            }

            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"camera: could not take view {index} ({e.GetType().Name}: {e.Message})");
            return false;
        }
    }

    /// <summary>
    /// A ring lying flat about <paramref name="normalEcl"/>, in metres.
    ///
    /// <para>Drawn from line segments rather than through <c>GizmosRenderer.DrawCircle</c>, which
    /// builds a full circle from twelve of them — a dodecagon, and plainly one at any size worth
    /// looking at.</para>
    ///
    /// <para>For marking a place on the ground. A sphere large enough to read as "this craft" is
    /// by construction large enough to hide it.</para>
    /// </summary>
    /// <param name="drape">
    /// Follow the terrain under each segment. A ring holds one radius, which is flat in space: on
    /// a slope half of it ends up underground and the rest hangs in the air.
    /// </param>
    public static void DrawCircleEcl(double3 centreEcl, double3 normalEcl, double radius,
                                     float4 colour, int segments = 64, bool drape = true,
                                     double clearance = 0.5)
    {
        if (Program.GizmosRenderer is null) return;
        if (!Vec.IsFinite(centreEcl) || !(radius > 0.0)) return;

        double3 up = Vec.Unit(normalEcl);
        if (Vec.Len2(up) < 0.5) return;

        // Any two axes square to the normal. Which two does not matter for a circle.
        double3 seed = Math.Abs(up.X) < 0.9 ? new double3(1, 0, 0) : new double3(0, 1, 0);
        double3 a = Vec.Unit(Vec.Cross(up, seed)) * radius;
        double3 b = Vec.Unit(Vec.Cross(up, a)) * radius;

        int steps = Math.Clamp(segments, 8, 256);
        double3 previous = OnGround(centreEcl + a, drape, clearance);

        for (int i = 1; i <= steps; i++)
        {
            double angle = Math.Tau * i / steps;
            double3 next = OnGround(centreEcl + (a * Math.Cos(angle)) + (b * Math.Sin(angle)),
                                    drape, clearance);

            DrawLineEcl(previous, next, colour);
            previous = next;
        }
    }

    // Lifted clear of the surface by a little: a line exactly on the terrain z-fights with it and
    // disappears in patches, which looks worse than being slightly above it.
    private static double3 OnGround(double3 atEcl, bool drape, double clearance)
    {
        if (!drape || !TrySnapToGround(atEcl, out double3 ground)) return atEcl;

        return ground + Vec.Unit(ground - NearestBodyCentre(ground)) * clearance;
    }

    private static double3 NearestBodyCentre(double3 nearEcl)
    {
        try
        {
            if (Universe.CurrentSystem is not { } system) return Vec.Zero;

            double3 centre = Vec.Zero;
            double best = double.MaxValue;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not Celestial body) continue;

                double3 at = body.GetPositionEcl();
                double distance = Vec.Len(nearEcl - at);
                if (distance >= best) continue;

                best = distance;
                centre = at;
            }

            return centre;
        }
        catch
        {
            return Vec.Zero;
        }
    }

    /// <summary>
    /// Puts a point on the ground beneath it: same direction from the body's centre, radius taken
    /// from the terrain there.
    ///
    /// <para>What makes a ring drawn on a slope follow the slope. A ring at one radius is flat in
    /// space, so on anything but level ground half of it is buried and the other half floats.</para>
    /// </summary>
    public static bool TrySnapToGround(double3 nearEcl, out double3 onGroundEcl)
    {
        onGroundEcl = nearEcl;

        try
        {
            if (Universe.CurrentSystem is not { } system) return false;

            Celestial? nearest = null;
            double best = double.MaxValue;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not Celestial body) continue;

                double distance = Vec.Len(nearEcl - body.GetPositionEcl());
                if (distance >= best) continue;

                best = distance;
                nearest = body;
            }

            if (nearest is null) return false;

            double3 centre = nearest.GetPositionEcl();
            double3 dirCce = Vec.Unit(nearEcl - centre);
            if (Vec.Len2(dirCce) < 0.5) return false;

            double height = nearest.GetTerrainHeightFromDirCce(dirCce, accurate: true);
            if (!double.IsFinite(height)) return false;

            onGroundEcl = centre + dirCce * (nearest.MeanRadius + height);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// A torus: a ring of solid spheres, draped onto the terrain.
    ///
    /// <para>Spheres because they are the only solid thing the gizmo renderer draws — <c>Render</c>
    /// is <c>RenderSpheres</c> then <c>RenderLines</c>, with no filled polygon anywhere. Enough of
    /// them around the ring, each wider than the gap to the next, and the result reads as a tube
    /// rather than as beads.</para>
    /// </summary>
    /// <param name="tubeRadius">Thickness of the ring itself.</param>
    public static void DrawTorusEcl(double3 centreEcl, double3 normalEcl, double ringRadius,
                                    double tubeRadius, float4 colour, bool drape = true)
    {
        if (Program.GizmosRenderer is null) return;
        if (!Vec.IsFinite(centreEcl) || !(ringRadius > 0.0) || !(tubeRadius > 0.0)) return;

        double3 up = Vec.Unit(normalEcl);
        if (Vec.Len2(up) < 0.5) return;

        double3 seed = Math.Abs(up.X) < 0.9 ? new double3(1, 0, 0) : new double3(0, 1, 0);
        double3 a = Vec.Unit(Vec.Cross(up, seed));
        double3 b = Vec.Unit(Vec.Cross(up, a));

        // Spaced closer together than they are wide, or it beads. Bounded so a large ring cannot
        // ask for thousands of spheres.
        int steps = (int)Math.Clamp(Math.Ceiling(Math.Tau * ringRadius / tubeRadius), 16, 160);

        for (int i = 0; i < steps; i++)
        {
            double angle = Math.Tau * i / steps;
            double3 at = centreEcl + ((a * Math.Cos(angle)) + (b * Math.Sin(angle))) * ringRadius;

            // Each bead sits on the ground under it, so the ring follows a slope instead of
            // burying one side and floating the other.
            if (drape && TrySnapToGround(at, out double3 ground)) at = ground;

            if (TryEclToEgo(at, out double3 ego))
            {
                Program.GizmosRenderer.DrawSphere(ego, (float)tubeRadius, colour);
            }
        }
    }

    public static void DrawLineEcl(double3 startEcl, double3 endEcl, float4 colour)
    {
        if (Program.GizmosRenderer is null) return;
        if (!TryEclToEgo(startEcl, out double3 a)) return;
        if (!TryEclToEgo(endEcl, out double3 b)) return;
        Program.GizmosRenderer.DrawLine(a, b, colour);
    }
}
