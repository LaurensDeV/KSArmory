using Brutal.Numerics;
using KSA;
using KSA.Rendering.Particles;

namespace KSArmory;

/// <summary>
/// The dust and grit a blast front throws off a part as it strikes: a burst of it on the face
/// toward the burst, streaming away along the blast, heavier the harder the part was hit.
///
/// <para>Billboards with the mod's own smoke sprite, the renderer the gun's smoke is drawn with and
/// is seen to work; the volumetric one drew the condensation shell as a blocky puff a quarter its
/// size. The stream is a force along the blast rather than a spawn velocity: a planet-anchored
/// emitter inherits no velocity, and its directional spawn is about the body's own axes. The force
/// is in the body-fixed frame the emitter works in, and the drag settles it to a drift.</para>
/// </summary>
internal static class BlastPuff
{
    private const string PuffId = "KSArmoryBlastPuff";

    // Loads under this leave nothing to see: the engine dents from half its tolerance, and dust
    // thrown by a fifth of it is already faint.
    private const double FaintestRatio = 0.2;

    private static bool _warned;

    /// <summary>
    /// One puff off one part: <paramref name="faceAsmb"/> and <paramref name="pushAsmb"/> in the
    /// craft's assembly frame, <paramref name="acrossMetres"/> the part's half-diagonal.
    /// </summary>
    public static void Throw(Vehicle craft, double3 faceAsmb, double3 pushAsmb, double acrossMetres,
                             double pressureRatio)
    {
        if (!Detonation.ParticlesEnabled || !(pressureRatio >= FaintestRatio)) return;

        try
        {
            if (Detonation.BodyFor(craft) is not { } body) return;

            double3 faceEcl = KsaWorld.VehicleAsmbToEcl(craft, faceAsmb);
            double3 pushEcl = craft.Asmb2Ego * pushAsmb;

            double3 faceCcf = (faceEcl - body.GetPositionEcl()).Transform(body.GetCce2Ccf());
            double3 pushCcf = Vec.Unit(pushEcl.Transform(body.GetCce2Ccf()));
            if (!Vec.IsFinite(faceCcf) || !Vec.IsFinite(pushCcf)) return;

            // Past the part's own tolerance a load is breaking things on anything unprotected, so
            // the dust stops growing there.
            float strength = (float)Math.Clamp(pressureRatio, FaintestRatio, 1.5);
            float across = (float)Math.Max(acrossMetres, 0.5);

            BubbleOrigin origin = new()
            {
                Time = Universe.GetElapsedTime(),
                Parent = body,
                BubFrame = BubbleFrame.Ccf,
                PositionBub = faceCcf,
                VelocityBub = double3.Zero,
            };

            Fire(body, origin, e =>
            {
                e.EmitterSpawnInfo.Radius = across * 0.6f;
                e.ParticleInfo.Size = new float2(across * 0.4f, across * 1.2f) * (0.6f + (0.4f * strength));
                e.ParticleInfo.Velocity = new float3(3f, 3f, 3f) * strength;
                e.Force = float3.Pack(pushCcf * (40.0 * strength));
                e.Opacity = Math.Clamp(0.3f + (0.4f * strength), 0.3f, 0.85f);
            });
        }
        catch (Exception e)
        {
            if (_warned) return;

            _warned = true;
            Log.Warn($"blast dust could not be thrown: {e.Message}");
        }
    }

    // The order KSA's own ExplosionSystem.FireStage uses: point it, tune it, then register it.
    private static void Fire(Celestial body, BubbleOrigin origin,
                             Action<ParticleEmitter<ParticleUpdateData, ParticleRenderData>> tune)
    {
        if (!Program.Instance.ParticleSystem.GetAndInitializeEmitters(PuffId, out var handles)
            || handles is null || handles.Count == 0)
        {
            if (!_warned)
            {
                _warned = true;
                Log.Warn($"no free emitters for '{PuffId}'; a front will strike without dust");
            }

            return;
        }

        foreach (var handle in handles)
        {
            if (handle.TryGet() is not { } emitter) continue;

            emitter.LocalOffset = float4x4.Identity;
            emitter.Context.Vehicle = null;
            emitter.Context.Part = null;
            emitter.Context.Astronomical = body;
            emitter.Origin = origin;
            tune(emitter);
            body.AddEmitter(handle);
        }
    }
}
