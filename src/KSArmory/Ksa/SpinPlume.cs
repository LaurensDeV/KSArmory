using Brutal.Numerics;
using KSA;
using KSA.Rendering.Particles;

namespace KSArmory;

/// <summary>
/// The jets of a released round's spin motors: one pooled emitter per nozzle while
/// <see cref="RoundSpin.Burning"/>, placed through the body's drawn attitude so the jets turn with it.
///
/// <para>Keyed and returned as <see cref="MotorPlume"/> is, for the same reason: an emitter never given
/// back is lost to the pool for the session.</para>
/// </summary>
internal sealed class SpinPlume
{
    private const string PlumeId = "KSArmorySpinPlume";

    // How fast the jet's particles leave the mouth, m/s. The emitter is launched with it, so it is what
    // points a jet along the nozzle rather than leaving a ball of gas at the mouth.
    private const double JetSpeed = 25.0;

    private sealed class Live
    {
        public required IEffectSource Owner;
        public required Celestial Body;
        public required List<ParticleEmitter<ParticleUpdateData, ParticleRenderData>.Handle> Handles;
    }

    private readonly Dictionary<(IProjectile Round, int Nozzle), Live> _burning = [];
    private readonly List<(IProjectile, int)> _finished = [];

    private static bool _warned;

    public void Update(IEffectSource system)
    {
        if (system.Profile.ReleaseSpin is { } spin && system.PlumesEnabled && system.EffectBody is not null)
        {
            foreach (IProjectile round in system.Rounds)
            {
                bool burning = round.State == RoundState.Flying && spin.Burning(round.Age);
                for (int n = 0; n < spin.Nozzles.Length; n++)
                {
                    if (burning) Follow(round, n, spin.Nozzles[n], system);
                    else _finished.Add((round, n));
                }
            }
        }

        foreach (KeyValuePair<(IProjectile Round, int Nozzle), Live> kv in _burning)
        {
            if (!ReferenceEquals(kv.Value.Owner, system)) continue;
            if (system.Profile.ReleaseSpin is null || !system.PlumesEnabled || !system.Rounds.Contains(kv.Key.Round))
            {
                _finished.Add(kv.Key);
            }
        }

        foreach ((IProjectile, int) key in _finished) Release(key);
        _finished.Clear();
    }

    /// <summary>Hands back the emitters of any system the roster has forgotten.</summary>
    public void Sweep(WeaponSystems roster)
    {
        foreach (KeyValuePair<(IProjectile Round, int Nozzle), Live> kv in _burning)
        {
            if (!roster.Knows(kv.Value.Owner)) _finished.Add(kv.Key);
        }

        foreach ((IProjectile, int) key in _finished) Release(key);
        _finished.Clear();
    }

    /// <summary>Hands every emitter back. Safe at any time.</summary>
    public void ReleaseAll()
    {
        foreach (Live live in _burning.Values) EmitterPool.Give(live.Body, live.Handles);
        _burning.Clear();
    }

    private void Follow(IProjectile round, int index, SpinNozzle nozzle, IEffectSource system)
    {
        if (!system.TryRoundEffectEcl(round, out double3 bodyEcl)
            || !system.TryRoundAttitudeEcl(round, out doubleQuat attitudeEcl))
        {
            return;
        }

        if (!_burning.TryGetValue((round, index), out Live? live))
        {
            if (Acquire(system.EffectBody) is not { } fresh) return;

            fresh.Owner = system;
            live = fresh;
            _burning[(round, index)] = live;
        }

        double3 mouthEcl = bodyEcl + (attitudeEcl * nozzle.MouthBody);
        double3 jet = round.VelocityLocal + (Vec.Unit(attitudeEcl * nozzle.ExhaustBody) * JetSpeed);

        doubleQuat toCcf = live.Body.GetCce2Ccf();
        double3 positionCcf = (mouthEcl - live.Body.GetPositionEcl()).Transform(toCcf);
        double3 velocityCcf = jet.Transform(toCcf);
        if (!Vec.IsFinite(positionCcf)) return;

        EmitterPool.Point(live.Handles,
                          EmitterPool.At(live.Body, positionCcf, Vec.IsFinite(velocityCcf) ? velocityCcf : double3.Zero));
    }

    private static Live? Acquire(Celestial? body)
    {
        try
        {
            if (body is null) return null;

            if (EmitterPool.Take(PlumeId, body) is not { } handles)
            {
                if (!_warned)
                {
                    _warned = true;
                    Log.Warn($"no free emitters for '{PlumeId}'; spin motors will burn without a jet");
                }
                return null;
            }

            return new Live { Owner = null!, Body = body, Handles = handles };
        }
        catch (Exception e)
        {
            if (!_warned)
            {
                _warned = true;
                Log.Warn($"spin plume failed to start: {e.Message}");
            }
            return null;
        }
    }

    private void Release((IProjectile, int) key)
    {
        if (!_burning.Remove(key, out Live? live)) return;

        EmitterPool.Give(live.Body, live.Handles);
    }
}
