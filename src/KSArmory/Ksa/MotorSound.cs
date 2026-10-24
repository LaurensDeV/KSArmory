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

    // Core's engine loop: it resolves on every install.
    private const string DefaultMotorId = "DefaultEngineSoundBehavior";

    // Sounds.xml maps Throttle 0 to 0.1x volume and 0.5x pitch, so left unset the loop plays a tenth
    // as loud and an octave down. A rocket motor has one setting.
    private static readonly KeyHash ThrottleHash = KeyHash.Make("Throttle");
    private const float FullThrottle = 1f;

    // Update runs once per system, so without an owner each system's sweep would cut every other
    // system's channels as orphans.
    private readonly Dictionary<IProjectile, (IRoundsInFlight Owner, IChannel Channel)> _burning = [];
    private readonly List<IProjectile> _finished = [];

    private static bool _warnedMissing;

    /// <summary>Starts, moves and stops the motor of every round this system has in the air.</summary>
    public void Update(IRoundsInFlight system)
    {
        if (!_config.MotorSound || system.Platform is not { } platform)
        {
            SilenceOwnedBy(system);
            return;
        }

        Camera? camera = SoundChannels.Listener();
        if (camera is null) return;

        double3 platformEgo = camera.GetPositionEgo(platform);
        double3 platformVelEgo = camera.GetVelocityEgo(platform);
        double pressure = SoundChannels.PressureAt(camera);

        foreach (IProjectile round in system.Rounds)
        {
            if (Burning(round)) Follow(round, system, platformEgo, platformVelEgo, pressure);
            else Silence(round);
        }

        // A round that left the system's list without burning out - reaped, or the whole salvo
        // abandoned - never gets a Silence call above, so its channel would play forever.
        foreach (KeyValuePair<IProjectile, (IRoundsInFlight Owner, IChannel Channel)> kv in _burning)
        {
            if (!ReferenceEquals(kv.Value.Owner, system)) continue;
            if (!system.Rounds.Contains(kv.Key)) _finished.Add(kv.Key);
        }

        foreach (IProjectile round in _finished) Silence(round);
        _finished.Clear();
    }

    /// <summary>Cuts every channel. Safe at any time.</summary>
    public void StopAll()
    {
        foreach ((_, IChannel channel) in _burning.Values) SoundChannels.Cut(channel);
        _burning.Clear();
    }

    /// <summary>Cuts the channels of any system the roster has forgotten.</summary>
    public void Sweep(WeaponSystems roster)
    {
        foreach (KeyValuePair<IProjectile, (IRoundsInFlight Owner, IChannel Channel)> kv in _burning)
        {
            if (!roster.Knows(kv.Value.Owner)) _finished.Add(kv.Key);
        }

        foreach (IProjectile round in _finished) Silence(round);
        _finished.Clear();
    }

    private void SilenceOwnedBy(IRoundsInFlight system)
    {
        foreach (KeyValuePair<IProjectile, (IRoundsInFlight Owner, IChannel Channel)> kv in _burning)
        {
            if (ReferenceEquals(kv.Value.Owner, system)) _finished.Add(kv.Key);
        }

        foreach (IProjectile round in _finished) Silence(round);
        _finished.Clear();
    }

    // The round's own profile, never the system's: cannon shells share the round list and carry
    // a munition whose BoostSeconds is zero.
    private static bool Burning(IProjectile round)
        => round.State == RoundState.Flying
           && round.Munition.TotalBoostSeconds > 0f
           && round.MotorBurning;

    private void Follow(IProjectile round, IRoundsInFlight system,
                        double3 platformEgo, double3 platformVelEgo, double pressure)
    {
        // From the round's offset from its platform, which is epoch-clean; an absolute Ecl position
        // converted on its own is a step of 29.8 km/s out.
        double3 posEgo = platformEgo + round.OffsetFromPlatform;
        double3 velEgo = platformVelEgo + round.VelocityLocal;

        if (!Vec.IsFinite(posEgo) || !Vec.IsFinite(velEgo)) return;

        var spatial = new SpatialAudio(posEgo, velEgo, pressure);

        if (_burning.TryGetValue(round, out (IRoundsInFlight Owner, IChannel Channel) held))
        {
            if (SoundChannels.TryMove(held.Channel, spatial)) return;

            _burning.Remove(round);
        }

        if (Start(spatial, _config) is { } started) _burning[round] = (system, started);
    }

    private void Silence(IProjectile round)
    {
        if (!_burning.Remove(round, out (IRoundsInFlight Owner, IChannel Channel) held)) return;

        SoundChannels.Cut(held.Channel);
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
}
