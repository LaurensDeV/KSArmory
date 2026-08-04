namespace KSArmory;

/// <summary>One articulated assembly the mod writes a transform to each frame.</summary>
public enum DriveChannel
{
    Turret,
    Pods,
    Guns,
    Radar,
    Optic,
}

/// <summary>
/// Which drives the engine is still accepting writes for.
///
/// <para>Writing every frame to an API the engine ignores fills the log and buries the one message
/// that matters, so the first refusal on a channel latches that channel off for the session. The
/// channels do not share a latch: a refused search-array spin is cosmetic and says nothing about
/// whether the tubes can still be laid.</para>
/// </summary>
public struct DriveStatus
{
    private int _refused;

    /// <summary>True while the engine is still accepting writes for this channel.</summary>
    public readonly bool Works(DriveChannel channel) => (_refused & Bit(channel)) == 0;

    /// <summary>Latches a channel off. True if this was its first refusal, so callers log once.</summary>
    public bool Refuse(DriveChannel channel)
    {
        if (!Works(channel)) return false;
        _refused |= Bit(channel);
        return true;
    }

    /// <summary>True once any channel has been refused.</summary>
    public readonly bool AnyRefused => _refused != 0;

    /// <summary>True while both channels that aim the tubes are still accepted.</summary>
    public readonly bool AimingAccepted => Works(DriveChannel.Turret) && Works(DriveChannel.Pods);

    public void Clear() => _refused = 0;

    private static int Bit(DriveChannel channel) => 1 << (int)channel;
}
