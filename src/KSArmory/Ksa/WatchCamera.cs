using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Holds the main view pointed at one craft, from wherever the player is, without handing it the
/// controls.
///
/// <para>The camera is rewritten every frame from the GUI hook. That is not belt and braces: each
/// viewport runs a controller that rewrites its camera from whatever mode it is in, so a single
/// write is gone before it renders — which is why <c>Camera.LookAt</c> on its own does nothing
/// while the camera is following anything. The optical head has driven a viewport this way since
/// it existed; this is the same mechanism aimed at the main one.</para>
///
/// <para>The view sits behind the craft being flown and looks past it at the target, so the
/// answer to "where is it" includes "relative to me". Pure rotation would lose that: a view
/// spun off its own craft shows a patch of sky with no sense of which way anything is.</para>
/// </summary>
internal sealed class WatchCamera
{
    // How far back from the anchor to sit, in anchor radii, and a floor for it so a kitten does
    // not put the camera inside its own helmet.
    private const double StandOff = 9.0;
    private const double MinStandOff = 12.0;

    private Vehicle? _target;
    private CameraMode _restoreMode = CameraMode.Fixed;
    private bool _holding;

    /// <summary>The craft being watched, or null.</summary>
    public Vehicle? Target => _target;

    public bool IsWatching => _holding && KsaWorld.IsAlive(_target);

    /// <summary>Starts watching a craft, or stops if it is already the one being watched.</summary>
    public void Toggle(Vehicle? vehicle)
    {
        if (ReferenceEquals(vehicle, _target)) { Release(); return; }

        _target = vehicle;
    }

    /// <summary>Gives the view back to whatever mode it was in.</summary>
    public void Release()
    {
        _target = null;
        if (!_holding) return;

        _holding = false;
        KsaWorld.RestoreMainCameraMode(_restoreMode);
    }

    /// <summary>
    /// Writes the view for this frame. Call from the GUI hook, after the engine's own controller.
    /// </summary>
    public void Apply(double dt)
    {
        if (_target is null) return;

        if (!KsaWorld.IsAlive(_target))
        {
            Release();
            return;
        }

        // Anchored to what the player is flying, not to the camera's own position: in Fixed mode
        // nothing moves the camera, so anchoring it to itself would leave it hanging in space
        // while the craft flew away from it.
        Vehicle anchor = KsaWorld.ControlledVehicle ?? _target;
        double3 anchorEcl = KsaWorld.PositionEcl(anchor);
        double3 targetEcl = KsaWorld.PositionEcl(_target);

        double3 toTarget = targetEcl - anchorEcl;
        if (Vec.Len2(toTarget) < 1e-6)
        {
            // Watching the craft you are flying. Nothing to point at, so leave the view alone.
            Release();
            return;
        }

        double3 forward = Vec.Unit(toTarget);
        double back = Math.Max(KsaWorld.MeanRadius(anchor) * StandOff, MinStandOff);
        double3 eye = anchorEcl - forward * back;

        if (!_holding)
        {
            _restoreMode = KsaWorld.MainCameraMode();
            _holding = true;
        }

        KsaWorld.TryLookFromMainViewport(eye, forward, KsaWorld.LocalUp(anchor), dt);
    }
}
