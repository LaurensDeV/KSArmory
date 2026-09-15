using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The cannon you can hear: one looping spatialised channel per battery, held while the gun fires,
/// and a gunshot for every round from a gun slow enough to be heard shot by shot.
///
/// <para>Per battery rather than per round for the loop, and that is not an optimisation. A Phalanx
/// cycles at 4500 rounds a minute; the shots arrive at 75 Hz, which is inside the range the ear
/// reads as <em>pitch</em> rather than as rhythm, so what a listener hears is one buzz and not
/// seventy-five bangs. Playing a one-shot per round would model the wrong thing and ask FMOD for 75
/// voices a second to do it. A five-inch gun at forty rounds a minute is the opposite case, which is
/// why the gunshot is a sound of its own and only a profile naming one gets it.</para>
///
/// <para>The engine loops the sample itself, declared in <c>KSArmorySounds.xml</c>. Restarting it
/// from here would be checked once a frame, and one frame at 60 fps is a whole round missing from
/// the cycle.</para>
///
/// <para>The shared recording is retuned toward each gun's rate, within limits: pitch moves cycle and
/// timbre together, and past about a quarter either way the result is not a slower gun, it is the
/// same gun played wrong. A gun that names its own recording is played as recorded, and a gun with a
/// gunshot and no loop of its own has no loop at all: it is heard shot by shot, not as a buzz.</para>
/// </summary>
internal sealed class GunSound(Config config)
{
    private readonly Config _config = config;

    private const string CannonId = "KSArmoryCannon";

    private readonly Dictionary<IEffectSource, IChannel> _firing = [];
    private readonly Dictionary<IEffectSource, int> _shotsHeard = [];
    private readonly List<IEffectSource> _stopped = [];
    private readonly HashSet<string> _warned = [];

    /// <summary>Starts, moves and stops the gun of every battery that is firing, and plays each round's gunshot.</summary>
    public void Update(IEffectSource battery)
    {
        // Ahead of the firing gate: the last round of a burst is fired on the frame the cannon stop.
        PlayGunshot(battery);

        if (!_config.CannonSound || !battery.GunsFiring || battery.Platform is not { } platform
            || (battery.Profile.GunSoundId is null && battery.Profile.GunshotSoundId is not null))
        {
            Silence(battery);
            return;
        }

        if (!TrySpatial(platform, out SpatialAudio spatial)) return;

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

        if (Start(spatial, battery) is { } started) _firing[battery] = started;
    }

    /// <summary>
    /// Cuts the channel of any battery the roster has forgotten. A craft destroyed mid-burst never
    /// reaches <see cref="Update"/> again, and its channel would play for the rest of the session.
    /// </summary>
    public void Sweep(WeaponSystems roster)
    {
        foreach (IEffectSource battery in _firing.Keys)
        {
            if (!roster.Knows(battery)) _stopped.Add(battery);
        }
        foreach (IEffectSource battery in _shotsHeard.Keys)
        {
            if (!roster.Knows(battery) && !_stopped.Contains(battery)) _stopped.Add(battery);
        }

        foreach (IEffectSource battery in _stopped)
        {
            Silence(battery);
            _shotsHeard.Remove(battery);
        }
        _stopped.Clear();
    }

    /// <summary>Cuts every channel. Safe at any time.</summary>
    public void StopAll()
    {
        foreach (IChannel channel in _firing.Values) Cut(channel);
        _firing.Clear();
        _shotsHeard.Clear();
    }

    private void Silence(IEffectSource battery)
    {
        if (!_firing.Remove(battery, out IChannel? channel)) return;

        Cut(channel);
    }

    private void PlayGunshot(IEffectSource battery)
    {
        int fired = battery.GunShotsFired;

        // A battery first heard now owes nothing for the rounds it fired before anybody was listening.
        if (!_shotsHeard.TryGetValue(battery, out int heard))
        {
            _shotsHeard[battery] = fired;
            return;
        }
        if (fired == heard) return;
        _shotsHeard[battery] = fired;

        if (!_config.CannonSound || battery.Profile.GunshotSoundId is not { } id
            || battery.Platform is not { } platform
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

    private IChannel? Start(SpatialAudio spatial, IEffectSource battery)
    {
        string id = battery.Profile.GunSoundId ?? CannonId;
        try
        {
            if (ModLibrary.Get<SoundBehavior>(id) is not { } sound)
            {
                WarnOnce(id, $"cannon sound '{id}' does not resolve; the gun will be silent");
                return null;
            }

            sound.Play(spatial, _config.CannonVolume, out IChannel? channel);

            if (channel is not null && battery.Profile.GunSoundId is null
                && _config.CannonReferenceRpm > 0f && battery.Profile.GunRoundsPerMinute > 0f)
            {
                // Clamped, because the sample is a recording of one real gun rather than a
                // synthesised pulse train. Pitch moves the cycle and the timbre together, so a
                // large shift does not give a slower gun, it gives the same gun played wrong.
                // Within a quarter either way it still reads as a cannon of a different rate.
                channel.PitchMultiplier = Math.Clamp(
                    battery.Profile.GunRoundsPerMinute / _config.CannonReferenceRpm, 0.8f, 1.25f);
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

        Camera? camera = SafeAudioCamera();
        if (camera is null) return false;

        // The craft's own position, not the muzzle's. They differ by the couple of metres from the
        // mount to the barrel tips, which is far inside what a listener could place, and taking it
        // from the camera keeps this on the same footing the engine spatialises everything else on.
        double3 posEgo = camera.GetPositionEgo(platform);
        double3 velEgo = camera.GetVelocityEgo(platform);
        if (!Vec.IsFinite(posEgo) || !Vec.IsFinite(velEgo)) return false;

        spatial = new SpatialAudio(posEgo, velEgo, SafePressure(camera));
        return true;
    }

    private void WarnOnce(string id, string message)
    {
        if (_warned.Add(id)) Log.Warn(message);
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
