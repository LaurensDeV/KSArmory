using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The rocket motor you can hear: one spatialised channel per round, moved every frame while it
/// burns and stopped at burnout.
///
/// <para>Driven directly rather than through <see cref="IAudio"/>. That interface exists so the
/// engine can drive an object's own audio during its update, which a <see cref="Vehicle"/> needs
/// and a mod-simulated round does not have: these rounds are already stepped from the frame hook,
/// so the channel is positioned in the same pass that moved them and cannot lag by a frame.</para>
///
/// <para>Sounds come from <see cref="ModLibrary"/> by Id, the same lookup
/// <see cref="Detonation"/> uses for particle emitters, so a mod's own <c>&lt;SoundFile&gt;</c>
/// and Core's resolve identically.</para>
/// </summary>
internal sealed class MotorSound(Config config)
{
    private readonly Config _config = config;

    // Core's engine loop. Ours until the mod ships its own sample; it resolves on every install,
    // which a mod-supplied Id does not until the asset is actually there.
    private const string DefaultMotorId = "DefaultEngineSoundBehavior";

    // Core's engine loop is driven by a Throttle parameter, and everything about how it sounds
    // hangs off it: Sounds.xml maps throttle 0 to 0.1x volume AND 0.5x pitch. Left unset it plays
    // at a tenth of its level, an octave down. A rocket motor has one setting, so this is pinned
    // wide open rather than being anything the round decides.
    private static readonly KeyHash ThrottleHash = KeyHash.Make("Throttle");
    private const float FullThrottle = 1f;

    private readonly Dictionary<IProjectile, IChannel> _burning = [];
    private readonly List<IProjectile> _finished = [];

    private static bool _warnedMissing;

    /// <summary>Starts, moves and stops the motor of every round this battery has in the air.</summary>
    public void Update(IRoundsInFlight battery)
    {
        if (!_config.MotorSound || battery.Platform is not { } platform)
        {
            StopAll();
            return;
        }

        Camera? camera = SafeAudioCamera();
        if (camera is null) return;

        double3 platformEgo = camera.GetPositionEgo(platform);
        double3 platformVelEgo = camera.GetVelocityEgo(platform);
        double pressure = SafePressure(camera);

        foreach (IProjectile round in battery.Rounds)
        {
            if (Burning(round)) Follow(round, platformEgo, platformVelEgo, pressure);
            else Silence(round);
        }

        // A round that left the battery's list without burning out - reaped, or the whole salvo
        // abandoned - never gets a Silence call above, so its channel would play forever.
        foreach (IProjectile round in _burning.Keys)
        {
            if (!battery.Rounds.Contains(round)) _finished.Add(round);
        }

        foreach (IProjectile round in _finished) Silence(round);
        _finished.Clear();
    }

    /// <summary>Cuts every channel. Safe at any time.</summary>
    public void StopAll()
    {
        foreach (IChannel channel in _burning.Values) Cut(channel);
        _burning.Clear();
    }

    // The round's own profile, never the battery's. Cannon shells share the battery's round list
    // and carry a different munition: measured against the missile's burn they would each get a
    // rocket motor for two seconds, and a burst is twelve of them a second. A shell has no motor,
    // which its own BoostSeconds of zero already says.
    private static bool Burning(IProjectile round)
        => round.State == RoundState.Flying
           && round.Munition.BoostSeconds > 0f
           && round.Age <= round.Munition.BoostSeconds;

    private void Follow(IProjectile round, double3 platformEgo, double3 platformVelEgo, double pressure)
    {
        // Built from the round's offset from its platform, never from an absolute Ecl position
        // converted on its own. The offset is the one quantity the mod already keeps epoch-clean,
        // and at 29.8 km/s the alternative is audibly in the wrong place.
        double3 posEgo = platformEgo + round.OffsetFromPlatform;
        double3 velEgo = platformVelEgo + round.VelocityLocal;

        if (!Vec.IsFinite(posEgo) || !Vec.IsFinite(velEgo)) return;

        var spatial = new SpatialAudio(posEgo, velEgo, pressure);

        if (_burning.TryGetValue(round, out IChannel? channel))
        {
            try
            {
                if (channel.IsPlaying()) { channel.SetSpatialAudio(spatial); return; }
            }
            catch
            {
                // Fall through and try to start a fresh one.
            }

            _burning.Remove(round);
        }

        if (Start(spatial, _config) is { } started) _burning[round] = started;
    }

    private void Silence(IProjectile round)
    {
        if (!_burning.Remove(round, out IChannel? channel)) return;

        Cut(channel);
    }

    private static IChannel? Start(SpatialAudio spatial, Config config)
    {
        try
        {
            if (ModLibrary.Get<SoundBehavior>(config.MotorSoundId ?? DefaultMotorId) is not { } sound)
            {
                if (!_warnedMissing)
                {
                    _warnedMissing = true;
                    Log.Warn($"motor sound '{config.MotorSoundId ?? DefaultMotorId}' does not resolve; "
                             + "rounds will be silent");
                }
                return null;
            }

            sound.Play(spatial, config.MotorVolume, out IChannel? channel);

            if (channel is not null)
            {
                channel.SetParameter(ThrottleHash, FullThrottle);
                channel.ApplyParameters();
            }

            return channel;
        }
        catch (Exception e)
        {
            if (!_warnedMissing)
            {
                _warnedMissing = true;
                Log.Warn($"motor sound failed to start: {e.Message}");
            }
            return null;
        }
    }

    private static void Cut(IChannel channel)
    {
        try { channel.Stop(); }
        catch { /* Already gone, which is the state we wanted. */ }
    }

    private static Camera? SafeAudioCamera()
    {
        try { return GameAudio.GetAudioCamera(); }
        catch { return null; }
    }

    private static double SafePressure(Camera camera)
    {
        // Listener's pressure, not the round's: it is what decides how much of the sound survives
        // the trip, and in vacuum that is none of it.
        try { return PhysicalAtmosphereReference.GetAtmosphericPressure(camera); }
        catch { return 1.0; }
    }
}
