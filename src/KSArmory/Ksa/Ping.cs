using KSA;

namespace KSArmory;

/// <summary>
/// A short-lived marker on one craft, so a system picked out of a list can be found in the world.
///
/// <para>Drawn by this mod rather than set through <c>Part.Highlighted</c>. The engine rewrites
/// its own highlight state every frame from selection, staging and resource flow, so a value
/// written from a mod hook lands and is gone before it is read — the same shape as the character
/// attachment transform in docs/BLOCKED-ON-KSA.md. A gizmo is ours and simply obeys.</para>
///
/// <para>It decays on <em>player</em> time, not simulated time. It is an answer to "which one is
/// it", so it should fade while the game is paused and not stretch under timewarp.</para>
/// </summary>
internal sealed class Ping
{
    /// <summary>How long a mark lasts. Long enough to look up from the panel and find it.</summary>
    public const double Seconds = 6.0;

    private Vehicle? _target;
    private double _left;

    /// <summary>Marks a craft, replacing any previous mark.</summary>
    public void Mark(Vehicle? vehicle)
    {
        _target = vehicle;
        _left = vehicle is null ? 0.0 : Seconds;
    }

    public void Clear() => Mark(null);

    /// <summary>
    /// Advances the mark and reports the craft to draw, or null.
    ///
    /// <para>How far through its life it is comes back too, so the marker can shrink or fade
    /// rather than vanishing between one frame and the next.</para>
    /// </summary>
    public Vehicle? Tick(double dtPlayer, out double fraction)
    {
        fraction = 0.0;
        if (_target is null) return null;

        if (!KsaWorld.IsAlive(_target))
        {
            Clear();
            return null;
        }

        if (double.IsFinite(dtPlayer) && dtPlayer > 0.0) _left -= dtPlayer;
        if (_left <= 0.0)
        {
            Clear();
            return null;
        }

        fraction = _left / Seconds;
        return _target;
    }
}
