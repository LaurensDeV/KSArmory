using Brutal.Numerics;
using KSA;
using KSA.Rendering.Particles;

namespace KSArmory;

/// <summary>
/// The black smoke a gun shell leaves where it burst in the air, hanging there after the flash is
/// gone. A sky dotted with them is what naval anti-aircraft fire looks like.
///
/// <para>Sized off the charge by its cube root, as the blast is, from the 5"/54's 3.3 kg: a 30 mm
/// shell leaves a puff a third the size that is gone in a few seconds. Anchored to the body the
/// burst was over and at rest in its frame, so it stays where the shell went off.</para>
/// </summary>
internal static class FlakPuff
{
    private const string PuffId = "KSArmoryFlakPuff";

    // The charge the emitter's own numbers are for.
    private const double ReferenceChargeKg = 3.3;

    // A burst this near the ground raises dirt rather than hanging smoke, which KSA's explosion already draws.
    private const double LowestMetres = 5.0;

    // Under this there is no air to hold smoke, and KSA itself counts none below it.
    private const double ThinnestPascals = 100.0;

    private static bool _warned;

    /// <summary>
    /// A puff where a shell of <paramref name="chargeKg"/> burst, <paramref name="overGroundMetres"/>
    /// up, or infinity where the ground could not be read.
    /// </summary>
    public static void Throw(double3 burstEcl, double chargeKg, double overGroundMetres, Vehicle? near,
                             Celestial? known)
    {
        if (!Detonation.ParticlesEnabled || !(chargeKg > 0.0) || !(overGroundMetres > LowestMetres)) return;

        try
        {
            if (Detonation.BodyFor(near, known) is not { } body) return;
            if (!(KsaWorld.AtmosphericPressureAt(body, burstEcl) > ThinnestPascals)) return;

            double3 burstCcf = (burstEcl - body.GetPositionEcl()).Transform(body.GetCce2Ccf());
            if (!Vec.IsFinite(burstCcf)) return;

            float scale = (float)Math.Cbrt(chargeKg / ReferenceChargeKg);

            BubbleOrigin origin = new()
            {
                Time = Universe.GetElapsedTime(),
                Parent = body,
                BubFrame = BubbleFrame.Ccf,
                PositionBub = burstCcf,
                VelocityBub = double3.Zero,
            };

            if (!Program.Instance.ParticleSystem.GetAndInitializeEmitters(PuffId, out var handles)
                || handles is null || handles.Count == 0)
            {
                Warn($"no free emitters for '{PuffId}'; a shell will burst without smoke");
                return;
            }

            // The order KSA's own ExplosionSystem.FireStage uses: point it, tune it, then register it.
            foreach (var handle in handles)
            {
                if (handle.TryGet() is not { } emitter) continue;

                emitter.LocalOffset = float4x4.Identity;
                emitter.Context.Vehicle = null;
                emitter.Context.Part = null;
                emitter.Context.Astronomical = body;
                emitter.Origin = origin;

                emitter.EmitterSpawnInfo.Radius = 2.5f * scale;
                emitter.ParticleInfo.Size *= scale;
                emitter.ParticleInfo.Velocity *= scale;
                emitter.ParticleInfo.Lifespan = new float2(Math.Max(18f * scale, 3f), Math.Max(28f * scale, 5f));

                body.AddEmitter(handle);
            }

            Log.Debug(() => $"shell smoke: {chargeKg:G3} kg, {overGroundMetres:F0} m up, {12f * scale:F1} m across");
        }
        catch (Exception e)
        {
            Warn($"shell smoke could not be thrown: {e.Message}");
        }
    }

    private static void Warn(string message)
    {
        if (_warned) return;

        _warned = true;
        Log.Warn(message);
    }
}
