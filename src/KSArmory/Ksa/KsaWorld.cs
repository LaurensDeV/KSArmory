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
            if (viewport.Mode != CameraMode.Fixed) viewport.SetCameraMode(CameraMode.Fixed);

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

    public static void DrawLineEcl(double3 startEcl, double3 endEcl, float4 colour)
    {
        if (Program.GizmosRenderer is null) return;
        if (!TryEclToEgo(startEcl, out double3 a)) return;
        if (!TryEclToEgo(endEcl, out double3 b)) return;
        Program.GizmosRenderer.DrawLine(a, b, colour);
    }
}
