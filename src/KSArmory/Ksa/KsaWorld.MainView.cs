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
    /// <summary>The main view's own field of view (deg), or the engine's default if unreadable.</summary>
    public static double MainViewFovDeg()
    {
        try
        {
            if (Program.MainViewport?.GetCamera() is not { } camera) return SightZoom.DefaultFovDeg;

            double degrees = double.RadiansToDegrees(camera.GetFieldOfView());

            return double.IsFinite(degrees) && degrees > 0.0 && degrees < 180.0
                ? degrees
                : SightZoom.DefaultFovDeg;
        }
        catch
        {
            return SightZoom.DefaultFovDeg;
        }
    }

    /// <summary>
    /// Narrows or widens the main view.
    ///
    /// <para>Clamped here: <c>SetFieldOfView</c> does not clamp, and <c>UpdateProjection</c> throws
    /// for zero or over half a turn. Rewritten every frame it is wanted, since the zoom keys'
    /// <c>ChangeFieldOfView</c> clamps to 15°–120°.</para>
    /// </summary>
    public static bool TrySetMainViewFov(double degrees)
    {
        if (!double.IsFinite(degrees)) return false;

        try
        {
            if (Program.MainViewport?.GetCamera() is not { } camera) return false;

            float wanted = (float)Math.Clamp(degrees, SightZoom.MinFovDeg, SightZoom.MaxFovDeg);
            if (Math.Abs(double.RadiansToDegrees(camera.GetFieldOfView()) - wanted) < 1e-3) return true;

            camera.SetFieldOfView(wanted);
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not set the field of view: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Where the main view's scene camera is and what it is looking along, both this frame.
    ///
    /// <para>The base camera, which Fixed mode drives; <c>GetMainCamera()</c> is the map camera in
    /// map mode.</para>
    /// </summary>
    public static bool TryMainCameraPose(out double3 positionEcl, out double3 forwardEcl)
    {
        positionEcl = forwardEcl = Vec.Zero;

        try
        {
            if (Program.MainViewport?.BaseCamera is not { } camera) return false;

            positionEcl = camera.EgoToEcl(Vec.Zero);
            forwardEcl = camera.GetForwardEcl();

            return Vec.IsFinite(positionEcl) && Vec.Len2(forwardEcl) > 1e-12;
        }
        catch (Exception e)
        {
            Log.Warn($"could not read the main camera pose: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Puts the main view's camera at a place, now, without waiting for a controller pass.
    ///
    /// <para>Undoes <c>Camera.SetFollow</c>'s jump to 2.5 mean radii from the target, which would
    /// otherwise be rendered: the controller takes over only next frame.</para>
    /// </summary>
    public static bool TryPlaceMainCamera(double3 positionEcl)
    {
        if (!Vec.IsFinite(positionEcl)) return false;

        try
        {
            if (Program.MainViewport?.BaseCamera is not { } camera) return false;

            camera.PositionEcl = positionEcl;
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not place the main camera: {e.Message}");
            return false;
        }
    }

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
    /// Whether the player is watching this craft — flying it, or pointing the camera at it.
    ///
    /// <para>Both, because the camera stays on what it followed when control moves elsewhere.</para>
    /// </summary>
    public static bool IsWatching(Vehicle? vehicle)
    {
        if (!IsAlive(vehicle)) return false;
        if (ReferenceEquals(ControlledVehicle, vehicle)) return true;

        try
        {
            return Program.GetMainCamera() is { } camera && ReferenceEquals(camera.Following, vehicle);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// How far back the orbit camera sits, as the power KSA stores it in.
    ///
    /// <para>The engine scales it by the followed object's mean radius.</para>
    /// </summary>
    public static bool SetOrbitZoomPower(double power)
    {
        try
        {
            if (!double.IsFinite(power)) return false;

            Program.MainViewport.OrbitController.DistancePower = power;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Points the camera at a craft and takes control of it.
    ///
    /// <para>Follow, control and zoom. No rebuild of derived data, as <c>Camera.SetFollow</c> does
    /// none: the vehicle worker holds the shapes registry, and rebuilding here throws after a split.</para>
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
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not go to {DisplayName(vehicle!)}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Park the camera out at a celestial, without taking control away from whatever is flying.
    ///
    /// <para>A frame-rate lever for an unwatched scenario: the orbit camera sits at
    /// <c>DistancePower x MeanRadius</c>, so following the body puts it thousands of km out. The
    /// power goes on the body's <c>OrbitView</c>, which the controller springs toward, and
    /// <c>changeControl: false</c> keeps the rocket flying.</para>
    /// </summary>
    public static bool WatchFrom(Celestial? body, double power)
    {
        if (body is null || !double.IsFinite(power) || power <= 0.0) return false;

        try
        {
            Camera? camera = Program.GetMainCamera();
            if (camera is null) return false;

            body.OrbitView.DistancePower = power;
            camera.SetFollow(body, tidalLocking: false, changeControl: false, alert: false);
            Program.MainViewport.OrbitController.DistancePower = power;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the main view's orbit camera: the basis it is looking along, and both copies of the
    /// angles behind it.
    ///
    /// <para><c>Following.OrbitView</c> is the writable pair; <c>OrbitController</c>'s is an output
    /// resprung toward it each frame, which built the camera being measured.</para>
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
    /// Whether the main view is still in the mode a borrower left it in.
    ///
    /// <para>False is the player taking it back, to be let go rather than fought.</para>
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
    /// <para>Asked before anything borrows the view.</para>
    /// </summary>
    public static bool MainViewFollows(IFollowable? target)
    {
        if (target is null) return false;

        try
        {
            return ReferenceEquals(Program.MainViewport?.GetCamera()?.Following, target);
        }
        catch
        {
            return false;
        }
    }

    public static int MainViewportIndex
    {
        get
        {
            try
            {
                // A viewport carries a ViewportId rather than its position in a list, and the
                // registry's order is not promised to put the main one first. Asked by identity.
                ReadOnlySpan<IGameViewport> viewports = GameViewports;
                IGameViewport? main = Program.MainViewport;

                for (int i = 0; i < viewports.Length; i++)
                {
                    if (ReferenceEquals(viewports[i], main)) return i;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>What the main view was doing before something borrowed it.</summary>
    /// <param name="FovDeg">Carried because the zoom keys cannot widen past a 15° clamp.</param>
    public readonly record struct MainView(IFollowable? Following, CameraMode Mode, double FovDeg,
                                           bool Valid);

    /// <summary>
    /// Records the main view so it can be handed back.
    ///
    /// <para>Before the first write: setting Fixed clears the follow.</para>
    /// </summary>
    public static MainView RememberMainView()
    {
        try
        {
            if (Program.MainViewport is not { } viewport) return default;

            return new MainView(viewport.GetCamera()?.Following, viewport.Mode, MainViewFovDeg(), true);
        }
        catch (Exception e)
        {
            Log.Warn($"could not read the main view: {e.Message}");
            return default;
        }
    }

    /// <summary>
    /// Whether something the view was following is still there to go back to.
    ///
    /// <para>A destroyed craft is not, the ordinary case: the engine has already pointed the view at
    /// the wreckage, and restoring over it holds the camera on a disposed vehicle.</para>
    /// </summary>
    public static bool CanFollow(IFollowable? target) => target switch
    {
        null => false,
        Vehicle v => !v.IsDisposed,
        _ => true,
    };

    /// <summary>
    /// Puts back whatever the view was following before a mod borrowed it.
    ///
    /// <para>Separate from the mode, which may never have changed.</para>
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
    /// Hands the main view back in the two halves it was taken in. The follow is read before
    /// anything is written; a follow that cannot be given back is not a refusal, as nothing is left
    /// to retry.
    /// </summary>
    /// <returns>False if either half was refused, and the caller still holds the view.</returns>
    public static bool TryHandBackMainView(MainView saved, IFollowable? followed,
                                           out bool mode, out bool follow)
    {
        bool followIsOurs = MainViewFollows(followed);

        mode = BeginRestoreMainView(saved);
        follow = !followIsOurs || !CanFollow(saved.Following) || RestoreFollow(saved);

        return mode && follow;
    }

    /// <summary>
    /// Gives back whichever half of the main view the player did not take: Fixed is a mode no input
    /// can leave. The field is handed back either way.
    /// </summary>
    /// <returns>True if the mode was still the mod's, which is what a vessel switch looks like.</returns>
    public static bool StandDownMainView(MainView saved, IFollowable? followed)
    {
        if (!saved.Valid) return false;

        bool modeIsOurs = MainViewIsFixed();
        bool followIsOurs = MainViewFollows(followed);

        StopDrivingMainView();
        TrySetMainViewFov(saved.FovDeg);

        if (followIsOurs && CanFollow(saved.Following)) RestoreFollow(saved);
        if (modeIsOurs) RestoreMainViewMode(saved);

        return modeIsOurs;
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

    // The engine's own controller, kept so it can be put back. Static because there is one main
    // viewport and the swap outlives any single borrower of it.
    private static FixedController? _stockFixedController;

    // A name api-surface.sh cannot check: the setter is protected, so the backing field is the only
    // way in. Null keeps the engine's own controller, a worse picture rather than a crash.
    private static FieldInfo? _fixedControllerField;

    private static bool _lookedForFixedControllerField;

    private static FieldInfo? FixedControllerField(IGameViewport viewport)
    {
        if (_lookedForFixedControllerField) return _fixedControllerField;
        _lookedForFixedControllerField = true;

        _fixedControllerField = viewport.GetType().GetField(
            "<FixedController>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

        if (_fixedControllerField is null)
        {
            Log.Warn("camera: no FixedController backing field on "
                     + $"{viewport.GetType().Name} - keeping KSA's own controller, so the horizon "
                     + "is not levelled and the sight's aim lags a frame. If the property has a "
                     + "public setter again, use it and delete this.");
        }

        return _fixedControllerField;
    }

    // Once; a failed swap returns the engine's own, since nobody promised this extension point.
    private static FixedController? LevelTheHorizon(IGameViewport viewport)
    {
        if (viewport.FixedController is LevelHorizonController already) return already;

        try
        {
            if (viewport.BaseCamera is not { } camera) return viewport.FixedController;
            if (FixedControllerField(viewport) is not { } field) return viewport.FixedController;

            var level = new LevelHorizonController(camera);

            _stockFixedController ??= viewport.FixedController;
            field.SetValue(viewport, level);

            // Checked: a silent no-op reads like a working swap.
            if (!ReferenceEquals(viewport.FixedController, level))
            {
                Log.Warn("camera: the FixedController write did not take - keeping KSA's own");
                _stockFixedController = null;
                return viewport.FixedController;
            }

            Log.Info("camera: levelling the horizon on the main view");

            return level;
        }
        catch (Exception e)
        {
            Log.Warn($"could not level the horizon, using KSA's own controller: {e.Message}");
            return viewport.FixedController;
        }
    }

    /// <summary>Puts KSA's own fixed controller back, if it was ever swapped out.</summary>
    public static void RestoreStockController()
    {
        if (_stockFixedController is not { } stock) return;

        try
        {
            if (Program.MainViewport is { } viewport
                && FixedControllerField(viewport) is { } field)
            {
                field.SetValue(viewport, stock);
            }
            Log.Info("camera: gave the fixed controller back");
        }
        catch (Exception e)
        {
            Log.Warn($"could not restore the fixed controller: {e.Message}");
        }
        finally
        {
            _stockFixedController = null;
        }
    }

    /// <summary>
    /// Points the main view from a place, using Fixed mode as it is meant to be used.
    ///
    /// <para><c>FixedController.OnFrame</c> places it at <c>following.GetPositionEcl() +
    /// CameraOffset</c> along <c>CameraRotation</c>, which must be non-zero before the mode is set.</para>
    /// </summary>
    /// <param name="offsetFromFollowed">
    /// A pure separation from the followed craft, from the same sample, or the view shivers by a
    /// frame of its motion.
    /// </param>
    /// <param name="fovDeg">Required, so no borrower inherits the last one's field.</param>
    /// <param name="pose">
    /// Asked again inside the engine's viewport pass; null when phase does not matter. Passed every
    /// call, so the previous borrower's source comes off.
    /// </param>
    public static bool TryLookFromMainViewport(double3 offsetFromFollowed, double3 forwardEcl,
                                               double3 upEcl, double fovDeg, IViewPose? pose = null)
    {
        if (!Vec.IsFinite(offsetFromFollowed) || !Vec.IsFinite(forwardEcl)) return false;
        if (Vec.Len2(forwardEcl) < 1e-12) return false;

        try
        {
            if (Program.MainViewport is not { } viewport) return false;
            if (viewport.GetCamera()?.Following is null) return false;

            FixedController? controller = LevelTheHorizon(viewport);
            if (controller is null) return false;

            // Only under the engine's own controller, whose up is ecliptic +Z.
            if (controller is not LevelHorizonController
                && Math.Abs(Vec.Dot(Vec.Unit(forwardEcl), new double3(0, 0, 1))) > 0.999)
            {
                return false;
            }

            // Set before the mode, every time. A frame drawn in Fixed with a zero rotation is the
            // crash, and setting the mode first leaves exactly that gap.
            controller.CameraRotation = Vec.Unit(forwardEcl);
            controller.CameraOffset = offsetFromFollowed;

            if (controller is LevelHorizonController level)
            {
                level.UpEcl = upEcl;

                // Every call, including with null. A borrower that stops driving must not leave
                // its source installed, or the controller goes on asking a sight that has let go.
                level.Pose = pose;
            }

            // The take-over frame's field, and the fallback if the pose source refuses.
            TrySetMainViewFov(fovDeg);

            if (viewport.Mode != CameraMode.Fixed) viewport.SetCameraMode(CameraMode.Fixed);

            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not drive the main view: {e.Message}");
            return false;
        }
    }

    /// <summary>A deliberate cut of a view the mod is driving, which the roll probe must not call a jump.</summary>
    public static void CutMainView()
    {
        try
        {
            if (Program.MainViewport?.FixedController is LevelHorizonController level) level.ExpectCut();
        }
        catch (Exception e)
        {
            Log.Warn($"could not mark a cut of the main view: {e.Message}");
        }
    }

    /// <summary>
    /// Takes the mod off the controller without touching the mode, the follow or the field.
    ///
    /// <para>The controller stays installed; with no up it behaves as KSA's own.</para>
    /// </summary>
    public static void StopDrivingMainView()
    {
        try
        {
            if (Program.MainViewport?.FixedController is LevelHorizonController level)
            {
                level.UpEcl = Vec.Zero;
                level.Pose = null;
                level.Forget();
            }
        }
        catch (Exception e)
        {
            Log.Warn($"could not stop driving the main view: {e.Message}");
        }
    }

    public static bool BeginRestoreMainView(MainView saved)
    {
        if (!saved.Valid) return false;

        try
        {
            if (Program.MainViewport is not { } viewport) return false;

            StopDrivingMainView();

            // Before the mode, so a frame drawn during the handover is drawn at the player's own
            // field rather than at the sight's.
            TrySetMainViewFov(saved.FovDeg);

            if (viewport.Mode != saved.Mode) viewport.SetCameraMode(saved.Mode);
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not restore the main view: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Puts back only the camera mode, for a hand-back where the rest of the view is no longer the
    /// mod's to undo.
    ///
    /// <para>A vessel switch: the follow is the player's, the Fixed mode still the mod's.</para>
    /// </summary>
    public static bool RestoreMainViewMode(MainView saved)
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
            Log.Warn($"could not restore the main view's mode: {e.Message}");
            return false;
        }
    }
}
