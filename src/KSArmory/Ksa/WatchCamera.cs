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
/// <para>It stops of its own accord on arrival. Holding the view would mean holding it against
/// the player, and the on-screen brackets already answer "where is it" continuously.</para>
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

    private Vehicle? _target;
    private double _elapsed;

    /// <summary>The craft being turned towards, or null once it has arrived.</summary>
    public Vehicle? Target => _target;

    /// <summary>Starts turning towards a craft, restarting the turn if it is already the one.</summary>
    public void Watch(Vehicle? vehicle)
    {
        _target = vehicle;
        _elapsed = 0.0;
    }

    public void Release()
    {
        _target = null;
        _elapsed = 0.0;
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

        if (!KsaWorld.TryNudgeMainCameraTowards(Vec.Unit(toTarget), Rate, dtPlayer, ArrivedRad,
                                               out bool arrived))
        {
            // No orbit controller to drive -- a map or fixed view, say. Nothing to chase.
            Release();
            return;
        }

        if (arrived) Release();
    }
}
