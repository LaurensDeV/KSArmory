using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// What the fire-control chain is doing about one contact, as one value.
///
/// <para>Ordered by how far along the engagement is, so a consumer can compare rather than
/// switch — the drawing wants "at least locked" more often than it wants a specific rung.</para>
/// </summary>
public enum LockPhase
{
    /// <summary>Nothing held. Nothing is drawn.</summary>
    None,

    /// <summary>Held, dwell accumulating. The brackets stand off and close as it builds.</summary>
    Acquiring,

    /// <summary>Dwell reached: this is the contact a weapon would shoot at.</summary>
    Locked,

    /// <summary>Locked and a gate is refusing. Locked geometry, and the reason belongs beside it.</summary>
    Held,

    /// <summary>Nothing in the way. The rung that earns a mark of its own.</summary>
    ClearToFire,
}

/// <summary>
/// The lock cue, as numbers a renderer can use. Geometry and state only — no drawing, no ImGui,
/// no camera, the same division <see cref="Reticle"/> keeps.
///
/// <para>The point of it is that acquisition is <em>shown as motion</em> rather than written down.
/// Brackets that stand off and close over the dwell say "locking, locking, locked" without being
/// read, where text has to be looked at and parsed — and the pilot is looking at the target.</para>
///
/// <para>Every number driving that comes out of the sim: the fraction is real dwell against the
/// sensor's real <c>LockSeconds</c>. So the animation cannot lie about how far along the lock is,
/// which is the failure mode of a cue timed to look good.</para>
/// </summary>
public static class LockCue
{
    /// <summary>
    /// How far the brackets stand off before any dwell, as a multiple of their closed size.
    ///
    /// <para>Matches <see cref="Reticle"/>'s unsettled form, because they are the same idea seen
    /// twice: a thing not yet ready is drawn loose around its target. Sharing the number is what
    /// stops a sight and a HUD disagreeing about what "not yet" looks like.</para>
    /// </summary>
    public const float OpenStandoff = 1.6f;

    /// <summary>
    /// Where the engagement has got to. <paramref name="held"/> is checked after
    /// <paramref name="clearToFire"/> is ruled out, so a system that is both locked and refused
    /// reads as refused rather than as ready.
    /// </summary>
    public static LockPhase Phase(bool hasTrack, bool locked, bool clearToFire, bool held)
    {
        if (!hasTrack) return LockPhase.None;
        if (!locked) return LockPhase.Acquiring;
        if (clearToFire && !held) return LockPhase.ClearToFire;
        return held ? LockPhase.Held : LockPhase.Locked;
    }

    /// <summary>
    /// Dwell as a fraction of what this sensor needs, clamped to 0..1.
    ///
    /// <para>A sensor asking for no dwell is answered 1: it locks on sight, so there is no
    /// acquisition to animate and the brackets should already be closed rather than divide by
    /// zero into a bracket that never arrives.</para>
    /// </summary>
    public static float Acquisition(double heldSeconds, double lockSeconds)
    {
        if (!double.IsFinite(heldSeconds) || !double.IsFinite(lockSeconds)) return 0f;
        if (lockSeconds <= 0.0) return 1f;
        if (heldSeconds <= 0.0) return 0f;

        return (float)Math.Clamp(heldSeconds / lockSeconds, 0.0, 1.0);
    }

    /// <summary>
    /// The bracket's size as a multiple of its closed size: <see cref="OpenStandoff"/> at no
    /// dwell, 1 at full.
    ///
    /// <para>Linear in the dwell on purpose. An eased curve looks better standing still and lies
    /// about the rate: the whole value of the cue is that halfway closed means halfway there, so
    /// a lock that is nearly ready cannot be made to look further off than it is.</para>
    /// </summary>
    public static float Standoff(float acquisition)
    {
        if (!float.IsFinite(acquisition)) return OpenStandoff;

        float t = Math.Clamp(acquisition, 0f, 1f);
        return OpenStandoff + (1f - OpenStandoff) * t;
    }

    /// <summary>
    /// Which way a caret at <paramref name="clampedAt"/> should point, given where the middle of
    /// the view is: outward, along the line from the centre to the clamped edge position.
    ///
    /// <para>For a contact <see cref="Reticle"/> cannot bracket because it is off the glass. The
    /// projection has already clamped it to the edge; this is the only part of that a test can
    /// reach, so it is the part that lives here.</para>
    /// </summary>
    /// <returns>False when the two coincide and no direction exists.</returns>
    public static bool TryCaretDirection(float2 clampedAt, float2 viewCentre, out float2 unit)
    {
        unit = default;

        float dx = clampedAt.X - viewCentre.X;
        float dy = clampedAt.Y - viewCentre.Y;
        if (!float.IsFinite(dx) || !float.IsFinite(dy)) return false;

        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-4f) return false;

        unit = new float2(dx / len, dy / len);
        return true;
    }
}
