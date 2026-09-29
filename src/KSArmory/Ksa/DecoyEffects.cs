using Brutal.Numerics;
using KSA;
using KSA.Rendering.Particles;

namespace KSArmory;

/// <summary>
/// What a decoy looks like: a flare's white-hot core and the thick white smoke it trails, and chaff's
/// faint grey puff where it stopped in the air.
///
/// <para>A flare holds a pooled emitter for as long as it burns, so every one taken is handed back the
/// frame it is spent — <see cref="MotorPlume"/> has the reason. At most <see cref="MaxLit"/> burn at
/// once; past that a flare still lays its smoke. Chaff is one fire-and-forget burst, which returns
/// itself.</para>
/// </summary>
internal sealed class DecoyEffects
{
    private const string FlareId = "KSArmoryFlare";
    private const string ChaffId = "KSArmoryChaffPuff";

    /// <summary>Flare cores lit at once. A 30-cartridge dump is the case that would otherwise drain the pool.</summary>
    public const int MaxLit = 12;

    // Seconds after ejection a chaff puff appears: it has stopped in the air by then.
    private const double ChaffShowsAfter = 0.08;

    private sealed class Lit
    {
        public required Celestial Body;
        public required List<ParticleEmitter<ParticleUpdateData, ParticleRenderData>.Handle> Handles;
    }

    private readonly Dictionary<Decoy, Lit> _lit = [];
    private readonly Dictionary<Decoy, PlumeSmoke.Strand> _smoking = [];
    private readonly HashSet<Decoy> _puffed = [];
    private readonly HashSet<Decoy> _live = [];
    private readonly List<Decoy> _gone = [];
    private readonly Config _config;

    private static bool _warned;

    public DecoyEffects(Config config) => _config = config;

    /// <summary>Moves every flare's light and smoke to where it now is, and puffs any fresh chaff.</summary>
    public void Update(IReadOnlyList<Decoy> decoys)
    {
        _live.Clear();
        for (int i = 0; i < decoys.Count; i++)
        {
            Decoy d = decoys[i];
            if (d.Spent || d.Body is not Celestial body) continue;
            _live.Add(d);

            double3 positionCcf = (d.PositionEcl - body.GetPositionEcl()).Transform(body.GetCce2Ccf());
            if (!Vec.IsFinite(positionCcf)) continue;

            if (d.Profile.Kind == DecoyKind.Flare) Burn(d, body, positionCcf);
            else if (d.Age >= ChaffShowsAfter && _puffed.Add(d)) Puff(body, positionCcf);
        }

        _gone.Clear();
        foreach (Decoy d in _lit.Keys) if (!_live.Contains(d)) _gone.Add(d);
        foreach (Decoy d in _smoking.Keys) if (!_live.Contains(d)) _gone.Add(d);
        foreach (Decoy d in _gone)
        {
            Release(d);
            _smoking.Remove(d);
        }

        _puffed.RemoveWhere(d => !_live.Contains(d));
    }

    /// <summary>Hands every emitter back. Safe at any time.</summary>
    public void ReleaseAll()
    {
        _gone.Clear();
        _gone.AddRange(_lit.Keys);
        foreach (Decoy d in _gone) Release(d);
        _smoking.Clear();
        _puffed.Clear();
    }

    private void Burn(Decoy d, Celestial body, double3 positionCcf)
    {
        double share = d.Profile.PeakSignature > 0f ? d.Signature / d.Profile.PeakSignature : 0.0;

        if (Detonation.ParticlesEnabled && (_lit.ContainsKey(d) || _lit.Count < MaxLit))
        {
            if (!_lit.TryGetValue(d, out Lit? lit) && Acquire(body) is { } fresh) _lit[d] = lit = fresh;

            if (lit is not null)
            {
                var origin = EmitterPool.At(body, positionCcf, double3.Zero);

                // Shrinks as it burns down rather than winking out at the end.
                float size = (float)(0.25 + (0.55 * share));
                foreach (var handle in lit.Handles)
                {
                    if (handle.TryGet() is not { } emitter) continue;
                    emitter.Origin = origin;
                    emitter.ParticleInfo.Size = new float2(size * 0.6f, size);
                }
            }
        }

        // The magnesium oxide it throws off, which lingers long after the flare is out.
        if (!_config.MotorSmoke || !PlumeSmoke.Available) return;

        if (!_smoking.TryGetValue(d, out PlumeSmoke.Strand? strand)) _smoking[d] = strand = new PlumeSmoke.Strand();
        PlumeSmoke.Lay(strand, body, positionCcf, 0.3f, (float)(1.5 + (2.5 * _config.MotorSmokeWidth)));
    }

    private static void Puff(Celestial body, double3 positionCcf)
    {
        if (!Detonation.ParticlesEnabled) return;

        try
        {
            if (!Program.Instance.ParticleSystem.GetAndInitializeEmitters(ChaffId, out var handles)
                || handles is null || handles.Count == 0)
            {
                Warn($"no free emitters for '{ChaffId}'; chaff will bloom unseen");
                return;
            }

            var origin = new BubbleOrigin
            {
                Time = Universe.GetElapsedTime(),
                Parent = body,
                BubFrame = BubbleFrame.Ccf,
                PositionBub = positionCcf,
                VelocityBub = double3.Zero,
            };

            foreach (var handle in handles)
            {
                if (handle.TryGet() is not { } emitter) continue;

                emitter.LocalOffset = float4x4.Identity;
                emitter.Context.Vehicle = null;
                emitter.Context.Part = null;
                emitter.Context.Astronomical = body;
                emitter.Origin = origin;
                body.AddEmitter(handle);
            }

            Log.Debug(() => $"  chaff puff drawn from {handles.Count} emitter(s)");
        }
        catch (Exception e)
        {
            Warn($"chaff could not be drawn: {e.Message}");
        }
    }

    private static Lit? Acquire(Celestial body)
    {
        try
        {
            if (EmitterPool.Take(FlareId, body) is not { } handles)
            {
                Warn($"no free emitters for '{FlareId}'; flares will burn unseen");
                return null;
            }

            return new Lit { Body = body, Handles = handles };
        }
        catch (Exception e)
        {
            Warn($"a flare could not be lit: {e.Message}");
            return null;
        }
    }

    private void Release(Decoy d)
    {
        if (_lit.Remove(d, out Lit? lit)) EmitterPool.Give(lit.Body, lit.Handles);
    }

    private static void Warn(string message)
    {
        if (_warned) return;

        _warned = true;
        Log.Warn(message);
    }
}
