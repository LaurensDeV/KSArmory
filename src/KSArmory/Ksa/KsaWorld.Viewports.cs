using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using KSA.Rendering.Water.Data;

namespace KSArmory;

internal static partial class KsaWorld
{
    // The views this mod may drive, in the order its viewport numbers index. GameViews rather than
    // Views because only a game viewport has a camera and controllers -- which also keeps the
    // part-thumbnail renderer out, since it is not one.
    private static ReadOnlySpan<IGameViewport> GameViewports
    {
        get
        {
            try { return ViewportRegistry.GameViews; }
            catch { return default; }
        }
    }

    // One of the game's camera windows, by the index the panel names it with. The registry's
    // order is not promised, so an index means something only for as long as the span it was read
    // from -- which is one frame. Every caller resolves afresh.
    private static bool TryViewport(int index, out IGameViewport viewport)
    {
        viewport = null!;

        ReadOnlySpan<IGameViewport> viewports = GameViewports;
        if (index < 0 || index >= viewports.Length) return false;

        viewport = viewports[index];
        return viewport is not null;
    }

    // ViewportBase.OptionFlags has a protected setter. Clearing HasUi on a window the mod draws
    // itself is what stops KSA wrapping the same picture in its own window; releasing the window
    // puts the defaults back (GameViewport.ResetToDefaults), so nothing here has to restore it.
    private static readonly MethodInfo? OptionFlagsSetter =
        typeof(ViewportBase).GetProperty(nameof(ViewportBase.OptionFlags))?.GetSetMethod(nonPublic: true);

    /// <summary>
    /// Which craft a ground-track window belongs to. The window holds it privately and nothing
    /// else names it, so this is reflection; a refusal means the marks are not drawn, which is the
    /// honest failure for an overlay.
    /// </summary>
    public static bool TryGroundTrackCraft(GroundTrackWindow window,
                                           [NotNullWhen(true)] out Vehicle? craft)
    {
        craft = null;

        try
        {
            craft = typeof(GroundTrackWindow)
                    .GetField("_vehicle", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(window) as Vehicle;

            return craft is not null && IsAlive(craft);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// KSA's ground-track window for a craft, opened or closed. The property builds and disposes
    /// the window, so this is the whole of it.
    /// </summary>
    public static bool TryShowGroundTrack(Vehicle craft, bool show)
    {
        try
        {
            craft.ShowGroundTrack = show;
            return craft.ShowGroundTrack == show;
        }
        catch (System.Exception e)
        {
            Log.Warn($"could not change the ground track window: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// A map viewport's zoom and pan, written straight onto the fields KSA drags and zooms. For
    /// the one caller that already holds the viewport; <see cref="TrySetGroundTrackView"/> is the
    /// form that finds it from a craft.
    /// </summary>
    public static void SetMapView(GenericImGuiViewport view, float zoom, float2 pan)
    {
        try
        {
            const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
            Type type = typeof(GenericImGuiViewport);

            type.GetField("_zoom", Private)?.SetValue(view, zoom);
            type.GetField("_pan", Private)?.SetValue(view, pan);
        }
        catch
        {
            // Driving the map is a test's business; a refusal costs the view, not the frame.
        }
    }

    /// <summary>
    /// Where that window is looking: its zoom, and its pan in pixels. Both are private fields of
    /// the viewport KSA drags and zooms, so this is reflection, and it reports rather than throws
    /// when a field moves — driving the map is a test's business, and a clear refusal is worth
    /// more there than an exception inside the GUI pass.
    /// </summary>
    /// <remarks>
    /// Nothing a player does comes through here. KSA's own wheel and drag write the same fields
    /// from inside <c>CreateDraggableZoomableViewport</c>, which also re-clamps them, so a value
    /// set here is a starting point rather than a setting.
    /// </remarks>
    public static bool TrySetGroundTrackView(Vehicle craft, double? zoom, double? panX,
                                             double? panY, out string trouble)
    {
        trouble = "";

        try
        {
            const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

            if (typeof(Vehicle).GetField("_groundTrackWindow", Private)?.GetValue(craft)
                is not GroundTrackWindow window)
            {
                trouble = "that craft has no ground track window open";
                return false;
            }

            GenericImGuiViewport view = window.Viewport;
            FieldInfo? zoomField = typeof(GenericImGuiViewport).GetField("_zoom", Private);
            FieldInfo? panField = typeof(GenericImGuiViewport).GetField("_pan", Private);

            if (zoomField is null || panField is null)
            {
                trouble = "KSA's map viewport no longer keeps its zoom and pan where this expects";
                return false;
            }

            if (zoom is { } z) zoomField.SetValue(view, (float)z);

            if (panX.HasValue || panY.HasValue)
            {
                float2 was = view.GetPan();
                panField.SetValue(view,
                                  new float2((float)(panX ?? was.X), (float)(panY ?? was.Y)));
            }

            return true;
        }
        catch (System.Exception e)
        {
            trouble = e.Message;
            return false;
        }
    }

    /// <summary>
    /// Takes KSA's own window and orbit lines off a camera viewport, leaving it rendering, so the
    /// mod can show the picture in a window of its own. False when that cannot be done, and KSA's
    /// window stays.
    /// </summary>
    public static bool TryHideViewportWindow(int index)
    {
        try
        {
            if (OptionFlagsSetter is null) return false;
            if (!TryViewport(index, out IGameViewport viewport)) return false;
            if (viewport.Type != ViewportType.Secondary || !viewport.Visible) return false;

            // Orbit lines too: a sight looking past the Moon otherwise shows its orbit across the picture.
            const ViewportOptionFlags taken = ViewportOptionFlags.HasUi | ViewportOptionFlags.RenderOrbitLines;

            ViewportOptionFlags flags = viewport.OptionFlags;
            if ((flags & taken) == 0) return true;

            OptionFlagsSetter.Invoke(viewport, [flags & ~taken]);
            return (viewport.OptionFlags & taken) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>A camera window's rendered picture as an ImGui texture, and the size it renders at.</summary>
    public static bool TryViewportTexture(int index, out ImTextureRef texture, out float2 size)
    {
        texture = default;
        size = default;
        try
        {
            if (!TryViewport(index, out IGameViewport viewport)) return false;
            texture = viewport.ImGuiTexture;
            size = new float2(viewport.Width, viewport.Height);
            return size.X > 0f && size.Y > 0f;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Says where a camera window's picture is on the desktop, as KSA's own window would have:
    /// the sight and the projections read it back through <see cref="TryViewportPicture"/>.
    /// </summary>
    public static void PlaceViewportPicture(int index, float2 pos, uint imGuiId, int2 wantedSize)
    {
        try
        {
            if (!TryViewport(index, out IGameViewport viewport)) return;

            viewport.SetPosition(pos);
            viewport.ImGuiId = imGuiId;

            if (wantedSize.X >= 64 && wantedSize.Y >= 64
                && (wantedSize.X != viewport.Width || wantedSize.Y != viewport.Height))
            {
                viewport.RequestResize(wantedSize);
            }
        }
        catch { /* a window that cannot be placed draws where it last was */ }
    }

    /// <summary>Hands a camera window back to KSA, as its own close button does.</summary>
    public static void CloseCameraWindow(int index)
    {
        try
        {
            if (TryViewport(index, out IGameViewport viewport)) ViewportRegistry.ReleaseSecondaryViewport(viewport);
        }
        catch { /* already gone */ }
    }

    /// <summary>A camera window by the panel's index, for a renderer hook keyed on the viewport.</summary>
    public static bool TryGameViewport(int index, out IViewport viewport)
    {
        viewport = null!;
        try
        {
            if (!TryViewport(index, out IGameViewport found)) return false;
            viewport = found;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where one camera window's picture sits on the desktop, and which ImGui platform window it
    /// is currently part of.
    ///
    /// <para><paramref name="imGuiId"/> is what separates a secondary view docked inside the game
    /// window from the same view dragged onto another monitor: ImGui gives a torn-off window a
    /// platform viewport of its own, and a draw list belonging to the wrong one renders on the
    /// wrong screen. KSA records it in <c>DrawImGui</c>, so it is a frame old at worst and zero
    /// until that window has been drawn once.</para>
    /// </summary>
    public static bool TryViewportPicture(int index, out float2 pos, out float2 size,
                                          out uint imGuiId)
    {
        pos = default;
        size = default;
        imGuiId = 0u;

        try
        {
            if (!TryViewport(index, out IGameViewport viewport)) return false;

            int w = viewport.Width, h = viewport.Height;
            if (w <= 0 || h <= 0) return false;

            pos = viewport.Position;
            size = new float2(w, h);
            imGuiId = viewport.ImGuiId;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Names a camera window after whatever is driving it.
    ///
    /// <para>A window on another monitor is identified by its title and nothing else, and "Camera
    /// 3" says nothing about which of four it is. Written only when it differs: the name is the
    /// ImGui window's identity, so rewriting it every frame would reset its position and drag it
    /// back out of the monitor the player put it on. KSA restores the default name when the
    /// viewport is released, so there is nothing to put back.</para>
    /// </summary>
    public static void NameViewport(int index, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            if (!TryViewport(index, out IGameViewport viewport)) return;
            if (viewport.Name == name) return;

            viewport.SetName(name);
        }
        catch { /* a window that will not be renamed is still a window */ }
    }

    /// <summary>
    /// Whether an index still names a camera window a player can see.
    ///
    /// <para>The window has an X on it, and closing it hands the lease back — after which the
    /// index either names a viewport nobody is showing or, once the registry has been rebuilt,
    /// somebody else's. Driving one costs nothing visible and looks exactly like the head being
    /// broken, which is the failure this whole row is prone to.</para>
    /// </summary>
    public static bool IsUsableCameraWindow(int index)
    {
        try
        {
            return TryViewport(index, out IGameViewport viewport)
                && viewport is { Visible: true, Type: ViewportType.Secondary };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads back where a camera window is actually looking from and along.
    ///
    /// <para>For the one question a screenshot cannot answer: whether anything moved the camera
    /// between the mod writing it and the engine drawing with it. Nothing else in the mod reads a
    /// camera it drives — the aim is written and trusted — so a fight over the pose would show up
    /// only as a scene that will not sit still.</para>
    /// </summary>
    public static bool TryReadViewportPose(int index, out double3 eyeEcl, out double3 forwardEcl,
                                           out double3 upEcl)
    {
        eyeEcl = forwardEcl = upEcl = Vec.Zero;

        try
        {
            if (!TryViewport(index, out IGameViewport viewport)) return false;
            if (viewport.GetCamera() is not { } camera) return false;

            eyeEcl = camera.PositionEcl;
            forwardEcl = camera.GetForwardEcl();
            upEcl = camera.GetUpEcl();

            return Vec.IsFinite(eyeEcl) && Vec.IsFinite(forwardEcl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>How many spare camera windows the game will still hand out.</summary>
    public static int SpareCameraWindows
    {
        get
        {
            try { return ViewportRegistry.AvailableSecondaryCount; }
            catch { return 0; }
        }
    }

    /// <summary>
    /// Opens one of the game's spare camera windows and returns the index that names it.
    ///
    /// <para>KSA builds four at startup and leases them out; this is the same call behind its own
    /// <c>View &gt; Add Camera</c>. The lease is what stops two owners driving one window, and it
    /// is given back by KSA itself when the player closes the window — so nothing here has to
    /// track one.</para>
    ///
    /// <para>The window ImGui puts it in can be dragged out of the game onto another monitor:
    /// KSA turns on ImGui's platform viewports, and the window it opens sets no flag against
    /// it.</para>
    /// </summary>
    public static bool TryOpenCameraWindow(out int index)
    {
        index = -1;

        try
        {
            if (!ViewportRegistry.TryOpenSecondaryViewport(out IGameViewport? opened)) return false;

            opened.SetVisible(true);

            // The index is a position in the registry's span, so it is read back rather than
            // remembered -- opening one is exactly the event that can move the others.
            ReadOnlySpan<IGameViewport> viewports = GameViewports;
            for (int i = 0; i < viewports.Length; i++)
            {
                if (ReferenceEquals(viewports[i], opened))
                {
                    index = i;
                    return true;
                }
            }

            return false;
        }
        catch (Exception e)
        {
            Log.Warn($"camera: could not open a window ({e.GetType().Name}: {e.Message})");
            return false;
        }
    }

    /// <summary>How many camera views the game currently has open.</summary>
    public static int ViewportCount
    {
        get
        {
            try { return GameViewports.Length; }
            catch { return 0; }
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
            ReadOnlySpan<IGameViewport> viewports = GameViewports;
            if (index < 0 || index >= viewports.Length) return false;

            IGameViewport viewport = viewports[index];
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
    /// <remarks>
    /// <c>GetFieldOfView</c> hands back <c>_fovRadians</c> unconverted while
    /// <c>SetFieldOfView</c> takes degrees. Reading one and writing the other without noticing is
    /// a factor of 57.3, which is why the two directions are wrapped here rather than called
    /// directly.
    /// </remarks>
    public static double ViewportFovRad(int index)
    {
        try
        {
            Camera camera = GameViewports[index].GetCamera();
            return camera is null ? 1.0 : camera.GetFieldOfView();
        }
        catch
        {
            return 1.0;
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
    /// <para>The window is one KSA built at startup and hands out on request — see
    /// <see cref="TryOpenCameraWindow"/>. A mod still cannot <em>make</em> a viewport, which is
    /// the difference between borrowing a window and stealing the main camera.</para>
    /// </summary>
    /// <param name="fovDeg">The field of view to draw with, clamped here as the main view's is.</param>
    public static bool TryLookFromViewport(int index, double3 eyeEcl, double3 forwardEcl,
                                           double3 upEcl, double fovDeg, double dt)
    {
        if (!Vec.IsFinite(eyeEcl) || !Vec.IsFinite(forwardEcl) || !Vec.IsFinite(upEcl)) return false;
        if (Vec.Len2(forwardEcl) < 1e-12) return false;

        try
        {
            ReadOnlySpan<IGameViewport> viewports = GameViewports;
            if (index < 0 || index >= viewports.Length) return false;

            IGameViewport viewport = viewports[index];

            // A view in Map mode renders the map scene — starfield, orbits, planets as discs —
            // and GetCamera() hands back the *map* camera, so moving it puts the map somewhere
            // else rather than showing the world from here. Fixed is the mode that draws the
            // scene from wherever its camera happens to be, which is the whole point.
            //
            // Unfollow before Fixed for the same reason as the main view: FixedController divides
            // by zero on its own default CameraRotation whenever the camera it drives is following
            // something. Every frame rather than only when the mode changes, because a follow can
            // arrive at any time and this window is a likely place for one: KSA's vessel-next and
            // vessel-previous act on the *hovered* viewport, so the cursor resting over the sight
            // is enough to attach one. Once attached, the controller places the camera at
            // following + CameraOffset — an offset this path never writes, because it aims with
            // LookAt — which parks the view inside whichever craft was switched to.
            try
            {
                if (viewport.GetCamera() is { Following: not null } followed)
                {
                    followed.Unfollow(changeControl: false);
                }
            }
            catch { /* a view that cannot be unfollowed is still worth trying to aim */ }

            if (viewport.Mode != CameraMode.Fixed) viewport.SetCameraMode(CameraMode.Fixed);

            Camera camera = viewport.BaseCamera;
            if (camera is null) return false;

            // Position and orientation together: setting one without the other leaves a frame
            // drawn from the old place looking the new way.
            camera.LookAt(eyeEcl, eyeEcl + Vec.Unit(forwardEcl) * 1000.0, upEcl);

            // SetFieldOfView does not clamp, and the projection throws outside (0, 180).
            float wanted = (float)Math.Clamp(double.IsFinite(fovDeg) ? fovDeg : SightZoom.DefaultFovDeg,
                                             SightZoom.MinFovDeg, SightZoom.MaxFovDeg);
            if (Math.Abs(double.RadiansToDegrees(camera.GetFieldOfView()) - wanted) >= 1e-3)
            {
                camera.SetFieldOfView(wanted);
            }

            SetNearbyContext(camera);

            // LookAt sets the rotation alone; the view matrix the render, the culling and every
            // projection read is rebuilt in Camera.OnFrame, which ran in the viewport pass before
            // this. Without it the window draws with last frame's aim: one frame of the line of
            // sight's swing, a fixed angle that judders with the frame time and grows with the zoom.
            camera.OnFrame(dt);

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

    // The engine derives this context for the main camera alone (Program.OnFrameCelestials), and
    // a window's terrain LOD, sky and grey-ball suppression all read it. Derived from the window's
    // own camera rather than copied from the main one: the two can be hundreds of kilometres
    // apart, and the copied altitude picks a planet mesh LOD for where the player is.
    private static void SetNearbyContext(Camera camera)
    {
        Celestial? body = Program.FindNearbyCelestial(camera);

        if (body is null || !body.IsBillboarded())
        {
            camera.NearbyCelestial = null;
            return;
        }

        double distanceKm = camera.DistanceTo(body.GetPositionEcl()) * 0.001;
        double surfaceKm = distanceKm - body.MeanRadius * 0.001;

        // The engine's own cut-off for a nearby body, so a window and the main view agree on it.
        camera.NearbyCelestial = surfaceKm > 80000.0 ? null : body;
        camera.DistanceToNearbyCelestialKm = distanceKm;
        camera.DistanceToNearbyCelestialSurfaceMeanKm = surfaceKm;
        camera.NearbyCelestialTerrainHeight = body.GetTerrainHeight(camera) * 0.001;
        camera.CurrentAltitudeKm = Program.GetCurrentAltitudeKm(camera);
    }

    /// <summary>The body a viewport's camera thinks it is near, and its altitude, for the bridge.</summary>
    public static string DescribeViewportContext(int index)
    {
        try
        {
            if (!TryViewport(index, out IGameViewport viewport)) return "no viewport";
            if (viewport.GetCamera() is not { } camera) return "no camera";

            return camera.NearbyCelestial is { } body
                ? $"{body.Id} at {camera.CurrentAltitudeKm:F2} km"
                : "none";
        }
        catch (Exception e)
        {
            return e.GetType().Name;
        }
    }

    /// <summary>
    /// Gives a camera window back the engine's default field of view, once nothing is driving it.
    ///
    /// <para>The window is the player's again through View &gt; Add Camera, and its own zoom keys
    /// clamp to 15°, so a director let go at x16 would otherwise leave it at 3° with no way out.</para>
    /// </summary>
    public static void ResetViewportFov(int index)
    {
        try
        {
            if (!TryViewport(index, out IGameViewport viewport)) return;
            if (viewport.Type != ViewportType.Secondary) return;

            viewport.BaseCamera?.SetFieldOfView((float)SightZoom.DefaultFovDeg);
        }
        catch { /* a window that cannot be reset is still a window */ }
    }
}
