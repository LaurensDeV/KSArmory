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
    /// <para>Clamped here and not merely by the caller: <c>SetFieldOfView</c> does not clamp, and
    /// <c>UpdateProjection</c> throws <c>ArgumentOutOfRangeException</c> for a field of zero or
    /// more than half a turn — out of the frame hook, which takes the mod down with it.</para>
    ///
    /// <para>Has to be rewritten every frame it is wanted. The player's zoom keys route through
    /// <c>ChangeFieldOfView</c>, which clamps to 15°–120°, so a single keypress throws away
    /// anything narrower than 15° and there is no notification that it happened.</para>
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
    /// <para>The base camera, not <c>GetMainCamera()</c>: that answers with the <em>map</em> camera
    /// in map mode, which sits millions of metres out. The base camera is the one Fixed mode drives
    /// and therefore the one being taken over, and in map mode it still holds the last pose the
    /// player had in the world.</para>
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
    /// <para>For undoing the jump <c>Camera.SetFollow</c> makes: it sets
    /// <c>PositionEcl = target.GetPositionEcl() + 2.5 * MeanRadius * forward</c> before switching
    /// what is followed, so attaching the view to a round puts the camera 2.5 m from it. The
    /// frame's view matrix is built in the viewport pass, which is over by the time a mod's hook
    /// runs, so that jump is rendered — and the controller does not take over until the next
    /// frame.</para>
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
    /// <para>Both, because the two come apart: the camera is left on whatever it was following when
    /// control moves elsewhere. Anything that wants to move the view politely has to ask about
    /// both, or it will drag somebody off a craft they are deliberately looking at.</para>
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
    /// <para>A power rather than a distance: the engine scales it by the followed object's mean
    /// radius, so one number frames a ground vehicle and a rocket alike. Written straight onto the
    /// controller, which is what <see cref="GoTo"/> does when it carries a craft's own saved
    /// zoom across.</para>
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
    /// <para>Three steps: follow, control, match the zoom. Doing fewer leaves the camera watching
    /// one craft while the controls drive another, or the view snapped to a zoom that belonged to
    /// the last vehicle.</para>
    ///
    /// <para><b>It deliberately does not rebuild the vehicle's derived data.</b> Nothing here
    /// modifies a part tree, and the engine does not do it either when it switches which vehicle is
    /// followed and controlled — <c>Camera.SetFollow</c> sets <c>ControlledVehicle</c> and stops.
    /// The rebuild reaches the shapes registry, which the vehicle worker holds for its whole run, and
    /// this mod's hooks land inside that run whenever the worker has not finished, so rebuilding here
    /// throws on a handover after a decoupler split.
    /// <c>TestTarget</c>, which has to build a vehicle, waits the run out instead.</para>
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
    /// <para><b>For a scenario nobody is watching, and it is a frame-rate lever rather than a
    /// framing one.</b> KSA puts the orbit camera at <c>DistancePower x MeanRadius</c>
    /// (<c>OrbitController.cs:593</c>), so following a rocket of some tens of metres sits the view
    /// about a hundred metres off the ground with the terrain, the ocean and the atmosphere all
    /// rendering at full detail. Following the body it is launching from puts the same power
    /// thousands of kilometres out, where none of that costs anything.</para>
    ///
    /// <para><paramref name="power"/> is written onto the body's own <c>OrbitView</c> rather than
    /// onto the controller, because the controller springs toward the followed object's view every
    /// frame — a value set on the controller alone is interpolated away within a second.</para>
    ///
    /// <para><c>changeControl: false</c> is the load-bearing argument. The camera moves and
    /// <see cref="Program.ControlledVehicle"/> does not, so the rocket keeps taking throttle and
    /// staging while nothing looks at it.</para>
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
    /// <para>Asked before anything borrows the view: a system on the far side of the world taking
    /// the camera off whatever the player is watching is a hijack, however good the shot.</para>
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

    /// <summary>Index of the main viewport, for the projection helpers that take one.</summary>
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
    /// <param name="FovDeg">
    /// The field of view it was set to. Carried because the sight magnifies, and a borrower that
    /// hands back everything except the zoom leaves the player at 3° with no control that reaches
    /// it — their own zoom keys clamp at 15° and cannot widen past it.
    /// </param>
    public readonly record struct MainView(IFollowable? Following, CameraMode Mode, double FovDeg,
                                           bool Valid);

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
    /// <para>A destroyed craft is not, and it is the ordinary case rather than a corner: the thing
    /// a borrower was watching is often the thing that just blew up. The engine has already
    /// answered the same question for itself by pointing the view at the wreckage, so the right
    /// move is to leave that alone — restoring over it puts the camera on a disposed vehicle and
    /// holds it there.</para>
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
    /// Hands the main view back in the two halves it was taken in, and says whether each half
    /// arrived. Every borrower goes through here, so a refusal cannot mean two different things in
    /// two files.
    ///
    /// <para>Order is the contract: what the view follows is read before anything is written,
    /// because the restore is about to change it and "did the player move this themselves" has no
    /// answer afterwards.</para>
    ///
    /// <para>A follow that cannot be given back is <b>not</b> a refusal. A destroyed craft is the
    /// ordinary way a chase or an engagement ends, and a follow the player has taken is theirs to
    /// keep — neither has anything to retry, so counting either would leave a caller retrying
    /// something that is never coming back.</para>
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
    /// Gives back whichever half of the main view the player did not take, for a borrower whose
    /// view has been taken over by hand. Every borrower stands down through here, so none of them
    /// can leave behind something the others would have put back.
    ///
    /// <para>Whichever of the mode and the follow the player changed is theirs and stays; the other
    /// is the mod's leavings and goes back. Leaving both strands them — a vessel switch leaves Fixed
    /// standing, and Fixed is a mode no input can leave — and restoring both drags them off the very
    /// thing they just chose. The field is handed back either way, because neither half restores it
    /// and no zoom key reaches it.</para>
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

    // The one name rather than a signature that the camera stands on, and the one thing
    // api-surface.sh cannot check for it. GameViewport.FixedController is an auto-property whose
    // setter is protected, behind IGameViewport's get-only one, so the backing field is the only
    // way in -- same shape, and the same warn-and-degrade, as PlumeSmoke's reflected renderer.
    //
    // Null here is not a fault: the engine's own controller stays, which costs the levelled
    // horizon and puts the sight's aim a frame behind again. Both are worse pictures, not crashes.
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

    // Puts LevelHorizonController on the main viewport, once, and answers with whichever
    // controller is now in place. Null only if the viewport has no controller at all.
    //
    // If the swap itself fails the engine's own is returned and everything carries on with the
    // roll it always had -- this is an extension point nobody promised, so it has to be allowed
    // to stop working.
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

            // The write is the whole point, and a silent no-op would read exactly like a working
            // swap: everything downstream still finds *a* controller.
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
    /// <para>The camera keeps following whatever it followed. <c>FixedController.OnFrame</c> puts
    /// it at <c>following.GetPositionEcl() + CameraOffset</c> looking along <c>CameraRotation</c>,
    /// so those two fields are the whole interface — and the offset is measured from the followed
    /// craft, not from the world.</para>
    ///
    /// <para><c>CameraRotation</c> must be non-zero before the mode is set. The controller crosses
    /// it with the frame's up and normalises, so a zero vector divides by zero — which is the
    /// entire reason this mode has a reputation for crashing.</para>
    /// </summary>
    /// <param name="offsetFromFollowed">
    /// Where the camera goes <em>relative to the craft the view is following</em>, not an absolute
    /// position. The controller adds it to <c>following.GetPositionEcl()</c> later in the frame,
    /// so an offset derived from that position here is measured against a different instant from
    /// the one it is applied to — which is a frame of the platform's motion, every frame, and
    /// reads as the thing being watched shivering.
    /// </param>
    /// <param name="fovDeg">
    /// The field this borrower wants. Required, and deliberately not optional: a borrower that
    /// says nothing about the field inherits whatever the last one left behind, and the sight
    /// leaves 3°. Making it part of driving the view is what stops that being possible.
    /// </param>
    /// <param name="pose">
    /// Somewhere the controller may ask again, inside the engine's own viewport pass. Null is the
    /// right answer for anything that does not need to be in phase with the frame, and passing it
    /// every call is what takes the previous borrower's source back off the controller.
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

            // Only when the engine is deriving up for itself, which it does whenever the level
            // controller could not be installed. Its axis is then ecliptic +Z, and a view along
            // that divides by zero. LevelHorizonController has no such direction: it falls back
            // for a view along the up it was given rather than refusing, so the chase is never
            // dropped mid-flight.
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

            // With the pose and not after it. A borrower supplying a pose source restates the
            // field in phase every frame, so this is the take-over frame's value and the fallback
            // if that source ever refuses.
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
    /// <para>Forgets the up that was being supplied and the source being asked. The controller
    /// stays installed for the session — nothing but a mod puts a viewport in Fixed mode, so there
    /// is nothing to disturb — but with no up it behaves exactly as KSA's own does, rather than
    /// holding one from an engagement that is over.</para>
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
    /// <para>Switching vessels is the case: it changes what the camera follows and leaves the mode
    /// alone, so the follow is the player's choice and the Fixed mode is still the mod's leavings —
    /// and Fixed is a mode no input can leave, because <c>FixedController</c> reads none and
    /// <c>Viewport.NextCameraMode</c> has no case for it. Only the mode comes back.</para>
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
