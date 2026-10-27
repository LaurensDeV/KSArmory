using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The cannon you can hear: one looping spatialised channel per system while the gun fires, and a
/// gunshot per round from a gun slow enough to be heard shot by shot.
///
/// <para>The loop is per system because 4500 rounds a minute is 75 Hz, which the ear hears as
/// pitch, not rhythm. The engine loops the sample itself (<c>KSArmorySounds.xml</c>): restarted
/// from here, once a frame, a 60 fps frame is a whole round missing from the cycle.</para>
///
/// <para>A gun that names a gunshot and no loop has no loop at all.</para>
/// </summary>
internal sealed class GunSound(Config config)
{
    private readonly Config _config = config;

    private const string CannonId = "KSArmoryCannon";

    private readonly Dictionary<IEffectSource, IChannel> _firing = [];
    private readonly Dictionary<IEffectSource, int> _shotsHeard = [];
    private readonly List<IEffectSource> _stopped = [];
    private readonly HashSet<string> _warned = [];

    public void Update(IEffectSource system)
    {
        // Ahead of the firing gate: the last round of a burst is fired on the frame the cannon stop.
        PlayGunshot(system);

        if (!_config.CannonSound || !system.GunsFiring || system.Platform is not { } platform
            || (system.Profile.GunSoundId is null && system.Profile.GunshotSoundId is not null))
        {
            Silence(system);
            return;
        }

        if (!TrySpatial(platform, out SpatialAudio spatial)) return;

        if (_firing.TryGetValue(system, out IChannel? channel))
        {
            if (SoundChannels.TryMove(channel, spatial)) return;

            _firing.Remove(system);
        }

        if (Start(spatial, system) is { } started) _firing[system] = started;
    }

    /// <summary>
    /// Cuts the channel of any system the roster has forgotten. A craft destroyed mid-burst never
    /// reaches <see cref="Update"/> again, and its channel would play for the rest of the session.
    /// </summary>
    public void Sweep(WeaponSystems roster)
    {
        foreach (IEffectSource system in _firing.Keys)
        {
            if (!roster.Knows(system)) _stopped.Add(system);
        }
        foreach (IEffectSource system in _shotsHeard.Keys)
        {
            if (!roster.Knows(system) && !_stopped.Contains(system)) _stopped.Add(system);
        }

        foreach (IEffectSource system in _stopped)
        {
            Silence(system);
            _shotsHeard.Remove(system);
        }
        _stopped.Clear();
    }

    /// <summary>Cuts every channel. Safe at any time.</summary>
    public void StopAll()
    {
        foreach (IChannel channel in _firing.Values) SoundChannels.Cut(channel);
        _firing.Clear();
        _shotsHeard.Clear();
    }

    private void Silence(IEffectSource system)
    {
        if (!_firing.Remove(system, out IChannel? channel)) return;

        SoundChannels.Cut(channel);
    }

    private void PlayGunshot(IEffectSource system)
    {
        int fired = system.GunShotsFired;

        // A system first heard now owes nothing for the rounds it fired before anybody was listening.
        if (!_shotsHeard.TryGetValue(system, out int heard))
        {
            _shotsHeard[system] = fired;
            return;
        }
        if (fired == heard) return;
        _shotsHeard[system] = fired;

        if (!_config.CannonSound || system.Profile.GunshotSoundId is not { } id
            || system.Platform is not { } platform
            || !TrySpatial(platform, out SpatialAudio spatial))
        {
            return;
        }

        // One gunshot however many rounds left this frame: a gun slow enough to have gunshots never
        // fires two in one, and a faster gun is heard through its loop instead.
        try
        {
            if (ModLibrary.Get<SoundBehavior>(id) is not { } sound)
            {
                WarnOnce(id, $"gunshot sound '{id}' does not resolve; the rounds will be silent");
                return;
            }

            sound.Play(spatial, _config.CannonVolume, out IChannel? _);
        }
        catch (Exception e)
        {
            WarnOnce(id, $"gunshot sound failed: {e.Message}");
        }
    }

    private IChannel? Start(SpatialAudio spatial, IEffectSource system)
    {
        string id = system.Profile.GunSoundId ?? CannonId;
        try
        {
            if (ModLibrary.Get<SoundBehavior>(id) is not { } sound)
            {
                WarnOnce(id, $"cannon sound '{id}' does not resolve; the gun will be silent");
                return null;
            }

            sound.Play(spatial, _config.CannonVolume, out IChannel? channel);

            if (channel is not null && system.Profile.GunSoundId is null
                && _config.CannonReferenceRpm > 0f && system.Profile.GunRoundsPerMinute > 0f)
            {
                // Pitch moves the cycle and the timbre together; past a quarter either way the
                // recording reads as the same gun played wrong rather than a different rate.
                channel.PitchMultiplier = Math.Clamp(
                    system.Profile.GunRoundsPerMinute / _config.CannonReferenceRpm, 0.8f, 1.25f);
                channel.ApplyParameters();
            }

            return channel;
        }
        catch (Exception e)
        {
            WarnOnce(id, $"cannon sound failed to start: {e.Message}");
            return null;
        }
    }

    private static bool TrySpatial(Vehicle platform, out SpatialAudio spatial)
    {
        spatial = default!;

        Camera? camera = SoundChannels.Listener();
        if (camera is null) return false;

        // The craft's position, not the muzzle's: metres apart, which no listener can place, and
        // read off the camera as the engine spatialises everything else.
        double3 posEgo = camera.GetPositionEgo(platform);
        double3 velEgo = camera.GetVelocityEgo(platform);
        if (!Vec.IsFinite(posEgo) || !Vec.IsFinite(velEgo)) return false;

        spatial = new SpatialAudio(posEgo, velEgo, SoundChannels.PressureAt(camera));
        return true;
    }

    private void WarnOnce(string id, string message)
    {
        if (_warned.Add(id)) Log.Warn(message);
    }
}
