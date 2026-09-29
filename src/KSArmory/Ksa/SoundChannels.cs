using KSA;

namespace KSArmory;

/// <summary>
/// The listener and the channel calls every held sound makes, each guarded so a sound that cannot be
/// placed or stopped costs a silent frame rather than the frame hook.
/// </summary>
internal static class SoundChannels
{
    /// <summary>The camera the engine hears from, or null when it cannot say.</summary>
    public static Camera? Listener()
    {
        try { return GameAudio.GetAudioCamera(); }
        catch { return null; }
    }

    /// <summary>The pressure at the listener, which is what decides how much of a sound survives the trip.</summary>
    public static double PressureAt(Camera listener)
    {
        // The listener's, not the source's: in vacuum at the ear nothing is heard however loud it is.
        try { return PhysicalAtmosphereReference.GetAtmosphericPressure(listener); }
        catch { return 1.0; }
    }

    /// <summary>
    /// Moves a held channel if it is still playing. False means it has stopped or broken, and the
    /// caller should drop it and start another.
    /// </summary>
    public static bool TryMove(IChannel channel, SpatialAudio spatial)
    {
        try
        {
            if (!channel.IsPlaying()) return false;

            channel.SetSpatialAudio(spatial);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Stops a channel. Safe on one the engine has already reclaimed.</summary>
    public static void Cut(IChannel channel)
    {
        try { channel.Stop(); }
        catch { /* A channel the engine has already reclaimed is already stopped. */ }
    }
}
