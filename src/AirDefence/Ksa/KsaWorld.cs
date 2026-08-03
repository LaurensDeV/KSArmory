using Brutal.Numerics;
using KSA;

namespace AirDefence;

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

    /// <summary>True while the simulation is stopped. KSA defines this as speed exactly zero.</summary>
    public static bool IsPaused => Universe.IsPaused();

    /// <summary>Current timewarp factor; 1.0 is real time, 0.0 is paused. Display only.</summary>
    public static double SimulationSpeed => Universe.SimulationSpeed;

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
    /// Destroys a vehicle, attributing it to collision damage. Must be called from the main
    /// thread and never while iterating engine-owned vehicle collections.
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

    public static void DrawLineEcl(double3 startEcl, double3 endEcl, float4 colour)
    {
        if (Program.GizmosRenderer is null) return;
        if (!TryEclToEgo(startEcl, out double3 a)) return;
        if (!TryEclToEgo(endEcl, out double3 b)) return;
        Program.GizmosRenderer.DrawLine(a, b, colour);
    }
}
