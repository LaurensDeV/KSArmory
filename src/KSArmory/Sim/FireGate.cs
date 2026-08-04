namespace KSArmory;

/// <summary>Whether the launcher is pointing where it is about to shoot.</summary>
public static class FireGate
{
    /// <summary>
    /// A launcher with no training gear is always laid — there is nothing that could be pointing
    /// the wrong way. One whose drive the engine has refused is frozen wherever it stopped, and is
    /// not: firing then ejects rounds along a stale tube transform, which guidance recovers from
    /// well enough that nothing but the drawn facing line shows it happened.
    /// </summary>
    /// <param name="aiming">Slewing onto a track, rather than stowed or driven from the panel.</param>
    /// <param name="trains">The profile declares gear that has to be aimed before firing.</param>
    /// <param name="drivesAccepted">The engine still accepts the traverse and elevation writes.</param>
    /// <param name="assembliesResolved">Those subparts were found on the vehicle.</param>
    /// <param name="settled">The drives have held the commanded bearing for the settle time.</param>
    public static bool IsLaid(bool aiming, bool trains, bool drivesAccepted,
                              bool assembliesResolved, bool settled)
        => !aiming || !trains || (drivesAccepted && assembliesResolved && settled);
}
