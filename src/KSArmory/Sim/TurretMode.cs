namespace KSArmory;

/// <summary>What the traverse ring was laid on this frame, in the order the drives try them.</summary>
public enum TurretRung
{
    Spin,
    Manual,
    Cursor,
    Designated,
    Stowed,
    Tracking,
}

/// <summary>
/// The claims a rung makes on the ring. The fire ladder holds the missiles on either, so each is
/// decided from this frame's rung alone: a claim carried from an earlier one stops auto-engage
/// launching, with nothing on the ring to show why.
/// </summary>
public static class TurretMode
{
    /// <param name="leadSolved">The cannon's lead solved this frame; meaningful only while tracking.</param>
    public static (bool OnGunLead, bool OnCursor) Claims(TurretRung rung, bool leadSolved) => rung switch
    {
        TurretRung.Cursor => (false, true),
        TurretRung.Tracking => (leadSolved, false),
        _ => (false, false),
    };
}
