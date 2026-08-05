using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Nudges the main view round until it is looking at a chosen craft, then lets go.
///
/// <para>It drives KSA's own orbit camera by setting the azimuth and elevation its controller
/// already reads, rather than taking the view. That matters three times over: the player keeps
/// full control throughout and can drag out of it at any moment, no camera mode changes — so the
/// interface stays up and <c>FixedController</c> is never handed a following camera to divide by
/// zero on — and easing is a matter of moving two numbers, so the view swings round instead of
/// cutting.</para>
///
/// <para>It stops of its own accord on arrival, and the moment the player moves the view
/// themselves. Holding on would mean fighting them for a camera they never gave up: the stored
/// angles are exactly what was last written unless something else moved them, so any difference
/// is somebody else's and the turn is over.</para>
/// </summary>
internal sealed class WatchCamera
{
    // Fraction of the remaining angle covered per second. About a second to swing most of the way
    // round, which reads as deliberate rather than either instant or sluggish.
    private const double Rate = 4.0;

    // Close enough that another frame of nudging would not be visible.
    private const double ArrivedRad = 0.004;

    // Gives up rather than chasing forever if the angles will not converge -- a target directly
    // overhead, or a craft moving faster than the view can follow.
    private const double Timeout = 4.0;

    // How far the stored angles may differ from what was written before it counts as the player
    // having taken the view. Tight: a write lands exactly, so anything else is a real input.
    private const double HandoverRad = 1e-6;

    private Vehicle? _target;
    private double _elapsed;

    // What was last written, to notice anything else moving it.
    private double _wroteAzimuth;
    private double _wroteElevation;
    private bool _hasWritten;

    /// <summary>The craft being turned towards, or null once it has arrived.</summary>
    public Vehicle? Target => _target;

    /// <summary>Starts turning towards a craft, restarting the turn if it is already the one.</summary>
    public void Watch(Vehicle? vehicle)
    {
        _target = vehicle;
        _elapsed = 0.0;
        _hasWritten = false;
    }

    public void Release()
    {
        _target = null;
        _elapsed = 0.0;
        _hasWritten = false;
    }

    /// <summary>Moves the view one frame's worth towards the target.</summary>
    public void Apply(double dtPlayer)
    {
        if (_target is null) return;

        if (!KsaWorld.IsAlive(_target))
        {
            Release();
            return;
        }

        if (double.IsFinite(dtPlayer) && dtPlayer > 0.0) _elapsed += dtPlayer;
        if (_elapsed > Timeout)
        {
            Release();
            return;
        }

        // Where the view would have to look. Measured from the camera rather than from the craft
        // being flown: it is the camera that is being turned, and the two are metres apart.
        double3 toTarget = KsaWorld.PositionEcl(_target) - KsaWorld.CameraPositionEcl();
        if (Vec.Len2(toTarget) < 1e-6)
        {
            Release();
            return;
        }

        // No orbit camera to drive -- a map or fixed view, say. Nothing to chase.
        if (!KsaWorld.TryReadMainOrbit(out OrbitAim.Reading view))
        {
            Release();
            return;
        }

        // The player moved the view. It is theirs; stand down rather than dragging it back.
        if (_hasWritten
            && !OrbitAim.SameAim(view.StoredAzimuth, view.StoredElevation,
                                 _wroteAzimuth, _wroteElevation, HandoverRad))
        {
            Release();
            return;
        }

        // Solved against the shown angles, because those are what built the basis being measured.
        if (!OrbitAim.TrySolve(view.Forward, view.Right, view.ShownAzimuth, view.ShownElevation,
                               Vec.Unit(toTarget), out double toAzimuth, out double toElevation))
        {
            Release();
            return;
        }

        double azimuth = view.StoredAzimuth;
        double elevation = view.StoredElevation;
        OrbitAim.Ease(ref azimuth, ref elevation, toAzimuth, toElevation, Rate, dtPlayer);

        if (!KsaWorld.TryWriteMainOrbit(azimuth, elevation))
        {
            Release();
            return;
        }

        _wroteAzimuth = azimuth;
        _wroteElevation = Math.Clamp(elevation, -Math.PI / 2.0, Math.PI / 2.0);
        _hasWritten = true;

        // Arrival is what the player sees, so measure the camera rather than what it chases.
        if (OrbitAim.Arrived(view.ShownAzimuth, view.ShownElevation,
                             toAzimuth, toElevation, ArrivedRad))
        {
            Release();
        }
    }
}
