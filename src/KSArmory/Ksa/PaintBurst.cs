using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// A paintball round going off: a burst of the shooter's paint where it burst, in place of the explosion,
/// sized off the charge by its cube root from a 20 mm round's, and at rest in the frame of the body under it.
/// </summary>
internal static class PaintBurst
{
    private const string BurstId = "KSArmoryPaintBurst";

    // The charge the emitter's own numbers are for.
    private const double ReferenceChargeKg = 0.011;

    private static bool _warned;

    public static void Throw(double3 burstEcl, double chargeKg, float hue, Vehicle? near, Celestial? known)
    {
        if (!Detonation.ParticlesEnabled) return;

        try
        {
            if (Detonation.BodyFor(near, known) is not { } body) return;

            double3 burstCcf = (burstEcl - body.GetPositionEcl()).Transform(body.GetCce2Ccf());
            if (!Vec.IsFinite(burstCcf)) return;

            // A nuclear charge's cube root over a shell's is about three hundred, which is the point.
            float scale = (float)Math.Clamp(Math.Cbrt(Math.Max(chargeKg, ReferenceChargeKg) / ReferenceChargeKg), 1.0, 300.0);
            (float r, float g, float b) = Paintball.Colour(hue);

            int fired = EmitterPool.Fire(BurstId, body, EmitterPool.At(body, burstCcf, double3.Zero), e =>
            {
                e.EmitterSpawnInfo.Radius *= scale;
                e.ParticleInfo.Size *= scale;
                e.ParticleInfo.Velocity *= scale;
                e.ParticleInfo.Color = new float4(r, g, b, 1f);
            });

            if (fired == 0) Warn($"no free emitters for '{BurstId}'; a paint burst will not be seen");
        }
        catch (Exception e)
        {
            Warn($"a paint burst could not be thrown: {e.Message}");
        }
    }

    private static void Warn(string message)
    {
        if (_warned) return;

        _warned = true;
        Log.Warn(message);
    }
}
