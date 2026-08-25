namespace KSArmory;

/// <summary>
/// Hands one frame's work out once, to whichever of several hooks reaches it first.
///
/// <para>The mod is called from two places the engine does not treat alike: a pass that is skipped
/// entirely under some conditions, and one that is not. Both have to be able to run the step and
/// only one of them may.</para>
///
/// <para><b>The failure this exists to make impossible is the latch never clearing.</b> Running the
/// step twice costs a duplicated frame; failing to release it stops the mod for the rest of the
/// session. So <see cref="EndFrame"/> belongs in a <c>finally</c>, on the hook that cannot be
/// skipped, and ahead of every early return.</para>
/// </summary>
internal sealed class FrameLatch
{
    private bool _taken;

    /// <summary>Whether the frame's work has already been claimed.</summary>
    public bool Claimed => _taken;

    /// <summary>
    /// Whether this caller owns the frame's work. True to the first asker after each
    /// <see cref="EndFrame"/>, and false to everyone after it.
    /// </summary>
    public bool Claim()
    {
        if (_taken) return false;

        _taken = true;
        return true;
    }

    /// <summary>End the frame, whatever happened during it.</summary>
    public void EndFrame() => _taken = false;
}
