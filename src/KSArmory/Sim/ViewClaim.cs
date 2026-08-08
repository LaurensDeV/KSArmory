namespace KSArmory;

/// <summary>What a borrower of the player's main view should do with it this frame.</summary>
public enum ViewAction
{
    /// <summary>Not holding it and not wanting it. Touch nothing.</summary>
    Idle,

    /// <summary>Take it: record what it was doing first, then drive it.</summary>
    Take,

    /// <summary>Already holding it and still entitled to. Drive it again.</summary>
    Hold,

    /// <summary>Holding it and no longer entitled to. Put back what was recorded.</summary>
    GiveBack,

    /// <summary>
    /// Something with a stronger claim is driving it. Write nothing, and keep the recording: it is
    /// the only way back to what the player was doing before any of this started.
    /// </summary>
    Yield,

    /// <summary>
    /// Holding it on paper, but the player has taken it back by hand. Forget the recording without
    /// putting the mode back — they chose that.
    ///
    /// <para>The caller must also stop <em>asking</em> for the view. Standing down alone leaves
    /// the setting requesting it, and the next frame is a plain <see cref="Take"/>: the view
    /// blinks back to the borrower and reads as the mod refusing to let go.</para>
    /// </summary>
    StandDown,
}

/// <summary>
/// Who may hold the main view, and what that means for the thing asking.
///
/// <para>The main view is the player's, and the mod borrows it. Two things here want to:
/// <c>Ksa/ChaseCamera.cs</c> rides a round, and <c>Ksa/SightCamera.cs</c> looks through the
/// optical head. Only one can, and getting the hand-back wrong strands the player wherever the
/// last good frame put them — which is why the ladder is here, where a test can reach it, and
/// only the camera writes are in <c>Ksa/</c>.</para>
/// </summary>
public static class ViewClaim
{
    /// <summary>
    /// What the optical head's camera should do this frame.
    ///
    /// <para><b>The order of the rungs is the whole content</b>, and two of them are load-bearing
    /// for reasons that are not visible from the rung itself.</para>
    ///
    /// <para><see cref="ViewAction.StandDown"/> answers first, because restoring over a view the
    /// player has already reclaimed drags them off whatever they turned to look at — a hand-back
    /// fired against somebody else's camera is worse than never having held it.</para>
    ///
    /// <para><see cref="ViewAction.Yield"/> answers before every reason to stop, so a borrower
    /// that is no longer wanted still waits for the stronger claim to finish. Both keep their own
    /// recording of what the view was doing, and they were made in order: restoring the older one
    /// while the newer claim is driving undoes a takeover that happened this frame, and leaves the
    /// stronger claim holding a recording of the weaker one to hand back at the end. The player is
    /// then returned to a borrowed pose that nothing is driving.</para>
    /// </summary>
    /// <param name="wanted">The player has put the optic on this view and the system is theirs.</param>
    /// <param name="resolved">The platform and the optical head were both found this frame.</param>
    /// <param name="outranked">Something with a stronger claim holds it — today, the chase.</param>
    /// <param name="holding">This borrower believes it holds the view.</param>
    /// <param name="stillOurs">The view is still in the mode it was left in, so nobody took it.</param>
    public static ViewAction ForOptic(bool wanted, bool resolved, bool outranked,
                                      bool holding, bool stillOurs)
    {
        if (holding && !stillOurs) return ViewAction.StandDown;
        if (holding && outranked) return ViewAction.Yield;
        if (holding) return wanted && resolved ? ViewAction.Hold : ViewAction.GiveBack;

        // Never take it out from under a stronger claim. Waiting rather than queueing: the chase
        // gives the view back on its own, and the next frame's Take picks it up.
        return wanted && resolved && !outranked ? ViewAction.Take : ViewAction.Idle;
    }

    /// <summary>
    /// Whether the sight's overlay belongs on screen this frame.
    ///
    /// <para><b>Driving a picture and annotating it are the same permission</b>, and separating
    /// them is what puts two reticles on one screen: a sight that yields the main view and keeps
    /// painting sits over a picture showing something else entirely, stacked under the bracket
    /// the stronger claim is drawing.</para>
    ///
    /// <para>A secondary window is never contested — the chase only ever takes the main view — so
    /// there the overlay follows the optic being switched on and nothing else.</para>
    /// </summary>
    /// <param name="opticSelected">The optic is on some window at all.</param>
    /// <param name="onMainView">That window is the one the player flies from.</param>
    /// <param name="holding">The sight believes it holds the main view.</param>
    /// <param name="outranked">Something with a stronger claim is driving it.</param>
    public static bool SightPaints(bool opticSelected, bool onMainView, bool holding, bool outranked)
    {
        if (!opticSelected) return false;
        if (!onMainView) return true;

        return holding && !outranked;
    }
}
