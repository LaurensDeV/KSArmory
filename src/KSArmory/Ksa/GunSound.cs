using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The cannon you can hear: one looping spatialised channel per battery, held while the gun fires.
///
/// <para>Per battery rather than per round, and that is not an optimisation. A Phalanx cycles at
/// 4500 rounds a minute; the reports arrive at 75 Hz, which is inside the range the ear reads as
/// <em>pitch</em> rather than as rhythm, so what a listener hears is one buzz and not seventy-five
/// bangs. Playing a one-shot per round would model the wrong thing and ask FMOD for 75 voices a
/// second to do it.</para>
///
/// <para>The engine loops the sample itself, declared in <c>KSArmorySounds.xml</c>. Restarting it
/// from here would be checked once a frame, and one frame at 60 fps is a whole round missing from
/// the cycle.</para>
///
/// <para>Pitch carries the fire rate, within limits. One sample serves every cannon in the mod,
/// tuned by the ratio of the gun's rate to the rate the recording was made at, but clamped: pitch
/// moves cycle and timbre together, and past about a quarter either way the result is not a slower
/// gun, it is the same gun played wrong.</para>
/// </summary>
internal sealed class GunSound(Config config)
{
    private readonly Config _config = config;

    private const string CannonId = "KSArmoryCannon";

    private readonly Dictionary<DefenceBattery, IChannel> _firing = [];
    private readonly List<DefenceBattery> _stopped = [];

    private static bool _warnedMissing;

    /// <summary>Starts, moves and stops the gun of every battery that is firing.</summary>
    public void Update(DefenceBattery battery)
    {
        if (!_config.CannonSound || !battery.GunsFiring || battery.Platform is not { } platform)
        {
            Silence(battery);
            return;
        }

        Camera? camera = SafeAudioCamera();
        if (camera is null) return;

        // The craft's own position, not the muzzle's. They differ by the couple of metres from the
        // mount to the barrel tips, which is far inside what a listener could place, and taking it
        // from the camera keeps this on the same footing the engine spatialises everything else on.
        double3 posEgo = camera.GetPositionEgo(platform);
        double3 velEgo = camera.GetVelocityEgo(platform);
        if (!Vec.IsFinite(posEgo) || !Vec.IsFinite(velEgo)) return;

        var spatial = new SpatialAudio(posEgo, velEgo, SafePressure(camera));

        if (_firing.TryGetValue(battery, out IChannel? channel))
        {
            try
            {
                if (channel.IsPlaying()) { channel.SetSpatialAudio(spatial); return; }
            }
            catch
            {
                // Fall through and try a fresh one.
            }

            _firing.Remove(battery);
        }

        if (Start(spatial, battery, _config) is { } started) _firing[battery] = started;
    }

    /// <summary>
    /// Cuts the channel of any battery the roster has forgotten. A craft destroyed mid-burst never
    /// reaches <see cref="Update"/> again, and its channel would play for the rest of the session.
    /// </summary>
    public void Sweep(BatteryRoster roster)
    {
        foreach (DefenceBattery battery in _firing.Keys)
        {
            bool present = false;
            foreach (BatteryRoster.Entry e in roster.All)
            {
                if (ReferenceEquals(e.Battery, battery)) { present = true; break; }
            }
            if (!present) _stopped.Add(battery);
        }

        foreach (DefenceBattery battery in _stopped) Silence(battery);
        _stopped.Clear();
    }

    /// <summary>Cuts every channel. Safe at any time.</summary>
    public void StopAll()
    {
        foreach (IChannel channel in _firing.Values) Cut(channel);
        _firing.Clear();
    }

    private void Silence(DefenceBattery battery)
    {
        if (!_firing.Remove(battery, out IChannel? channel)) return;

        Cut(channel);
    }

    private static IChannel? Start(SpatialAudio spatial, DefenceBattery battery, Config config)
    {
        try
        {
            if (ModLibrary.Get<SoundBehavior>(CannonId) is not { } sound)
            {
                if (!_warnedMissing)
                {
                    _warnedMissing = true;
                    Log.Warn($"cannon sound '{CannonId}' does not resolve; the gun will be silent");
                }
                return null;
            }

            sound.Play(spatial, config.CannonVolume, out IChannel? channel);

            if (channel is not null && config.CannonReferenceRpm > 0f
                && battery.Profile.GunRoundsPerMinute > 0f)
            {
                // Clamped, because the sample is a recording of one real gun rather than a
                // synthesised pulse train. Pitch moves the cycle and the timbre together, so a
                // large shift does not give a slower gun, it gives the same gun played wrong.
                // Within a quarter either way it still reads as a cannon of a different rate.
                channel.PitchMultiplier = Math.Clamp(
                    battery.Profile.GunRoundsPerMinute / config.CannonReferenceRpm, 0.8f, 1.25f);
                channel.ApplyParameters();
            }

            return channel;
        }
        catch (Exception e)
        {
            if (!_warnedMissing)
            {
                _warnedMissing = true;
                Log.Warn($"cannon sound failed to start: {e.Message}");
            }
            return null;
        }
    }

    private static void Cut(IChannel channel)
    {
        try { channel.Stop(); }
        catch { /* A channel the engine has already reclaimed is already stopped. */ }
    }

    private static Camera? SafeAudioCamera()
    {
        try { return GameAudio.GetAudioCamera(); }
        catch { return null; }
    }

    private static double SafePressure(Camera camera)
    {
        // Listener's pressure, not the gun's: it is what decides how much of the sound survives
        // the trip, and in vacuum that is none of it.
        try { return PhysicalAtmosphereReference.GetAtmosphericPressure(camera); }
        catch { return 1.0; }
    }
}
