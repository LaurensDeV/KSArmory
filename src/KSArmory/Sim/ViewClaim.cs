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
    /// The player has taken it back by hand: give back only the half they did not take. The caller
    /// must also stop asking for the view, or the next frame is a plain <see cref="Take"/>.
    /// </summary>
    StandDown,
}

/// <summary>
/// Who may hold the player's main view — <c>Ksa/ChaseCamera.cs</c> or <c>Ksa/SightCamera.cs</c> — as a
/// ladder a test can reach; only the camera writes are in <c>Ksa/</c>.
/// </summary>
public static class ViewClaim
{
    /// <summary>
    /// What the optical head's camera should do this frame. The order of the rungs is the content:
    /// <see cref="ViewAction.StandDown"/> first, so nothing is restored over a view the player took back;
    /// <see cref="ViewAction.Yield"/> before every reason to stop, because the two recordings were made in
    /// order and restoring the older one under the chase leaves it handing back a borrowed pose.
    /// </summary>
    /// <param name="wanted">The player has put the optic on this view and the system is theirs.</param>
    /// <param name="resolved">The platform and the optical head were both found this frame.</param>
    /// <param name="outranked">Something with a stronger claim holds it — today, the chase.</param>
    /// <param name="holding">This borrower believes it holds the view.</param>
    /// <param name="stillOurs"><see cref="StillOurs"/>.</param>
    public static ViewAction ForOptic(bool wanted, bool resolved, bool outranked,
                                      bool holding, bool stillOurs)
    {
        if (holding && !stillOurs) return ViewAction.StandDown;
        if (holding && outranked) return ViewAction.Yield;
        if (holding) return wanted && resolved ? ViewAction.Hold : ViewAction.GiveBack;

        // Waiting rather than queueing: the chase gives the view back, and the next frame's Take picks it up.
        return wanted && resolved && !outranked ? ViewAction.Take : ViewAction.Idle;
    }

    /// <summary>
    /// Whether the view a borrower believes it holds is still the one it took. Both halves: the
    /// camera-mode menu changes only the mode, and switching vessels only the follow.
    /// </summary>
    /// <param name="followsWhatWePointedAt">
    /// By identity with what this borrower pointed it at, so a borrower re-pointing itself is not
    /// mistaken for the player moving it.
    /// </param>
    /// <param name="outranked">
    /// A stronger claim inside the mod is driving, so the follow is not evidence: it is the mod's.
    /// </param>
    public static bool StillOurs(bool inTakenMode, bool followsWhatWePointedAt, bool outranked)
        => inTakenMode && (outranked || followsWhatWePointedAt);

    /// <summary>
    /// Whether the sight's overlay belongs on screen this frame. Driving the main view and painting on
    /// it are one permission; a secondary window is never contested, since the chase takes only the main view.
    /// </summary>
    /// <param name="opticSelected">The optic is on some window at all.</param>
    public static bool SightPaints(bool opticSelected, bool onMainView, bool holding, bool outranked)
    {
        if (!opticSelected) return false;
        if (!onMainView) return true;

        return holding && !outranked;
    }
}
