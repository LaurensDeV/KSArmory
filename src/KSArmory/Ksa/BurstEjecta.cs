using Brutal.Numerics;
using KSA;
using KSA.Rendering.Particles;

namespace KSArmory;

/// <summary>
/// A nuclear burst's particles: the thrown ground and debris shell where there is no air for a
/// mushroom, and the condensation shell where there is. <see cref="AirlessBurst"/> decides the sizes.
/// <para>Every emitter is one-shot (<c>Burst</c> in its XML) and returns itself to the pool, which is
/// why this holds no state.</para>
/// </summary>
internal static class BurstEjecta
{
    private const string EjectaId = "KSArmoryNuclearEjecta";
    private const string ShellId = "KSArmoryNuclearShell";
    private const string WilsonId = "KSArmoryNuclearWilson";

    // The condensation shell's optical depth through its middle: translucent, as the films show it.
    private const double WilsonDepth = 4.0;

    private static bool _warned;

    /// <summary>
    /// For a burst on a body with no air, called by <see cref="NuclearClouds.Begin"/>. Returns the size
    /// of what it drew, for a camera to frame; empty when nothing was drawn.
    /// </summary>
    public static AirlessBurst.Extent Begin(Celestial body, double3 burstCcf, double chargeKg,
                                            double burstAltitudeMetres)
    {
        if (!Detonation.ParticlesEnabled) return default;

        try
        {
            double kt = MushroomCloud.KilotonsFor(chargeKg);
            double fireball = MushroomCloud.PeakFireballRadius(kt);
            if (!(fireball > 0.0)) return default;

            double gravity = GravityAt(body, burstCcf);

            // At rest in the body's frame: the dust belongs to the ground, not to whatever was hit,
            // and it has to still be over the crater when it lands.
            BubbleOrigin origin = EmitterPool.At(body, burstCcf, double3.Zero);

            double shellRadius = AirlessBurst.ShellRadius(chargeKg);
            double shellSeconds = AirlessBurst.ShellSeconds(chargeKg);
            if (shellRadius > 0.0 && shellSeconds > 0.0)
            {
                Fire(ShellId, body, origin, e =>
                {
                    // One sphere at a point, grown entirely by its ScaleEnvelope: the size IS the
                    // shell's radius and nothing here moves it. Giving it a velocity instead reads
                    // as a blob flying out of the burst rather than as a front leaving it.
                    e.ParticleInfo.Size = new float2((float)shellRadius, (float)shellRadius);
                    e.ParticleInfo.Lifespan = new float2((float)shellSeconds, (float)shellSeconds);
                });
            }

            // Logged whatever happens, so a burst drawing half its effect is told from one never seen.
            string ejecta = $"no ejecta: a {fireball:F0} m fireball does not reach the ground";

            if (AirlessBurst.ThrowsEjecta(chargeKg, burstAltitudeMetres))
            {
                AirlessBurst.Ejecta thrown = AirlessBurst.EjectaAt(chargeKg, gravity);

                if (thrown.Spent)
                {
                    ejecta = $"no ejecta: nothing to throw it with (gravity {gravity:F2} m/s2)";
                }
                else
                {
                    Fire(EjectaId, body, origin, e =>
                    {
                        // Started inside the crater rather than across the fireball: the dome is
                        // drawn by where the dust GOES, and spawning it already spread out gives a
                        // ring instead.
                        e.EmitterSpawnInfo.Radius = (float)(fireball * 0.40);
                        Speed(ref e.ParticleInfo, thrown.SpeedMetresPerSecond);

                        // The throw's own spread: a grain that leaves 25% fast flies 25% longer.
                        e.ParticleInfo.Lifespan = new float2(
                            (float)(thrown.FlightSeconds * (1.0 - AirlessBurst.SpeedSpread)),
                            (float)(thrown.FlightSeconds * (1.0 + AirlessBurst.SpeedSpread)));

                        // Very nearly the size it stays: the envelope is flat, because a grain in
                        // vacuum has no air to entrain and does not swell. The dome widens because
                        // the grains leave at a spread of speeds, not because each one grows.
                        e.ParticleInfo.Size = new float2((float)(thrown.ReachMetres * 0.09),
                                                         (float)(thrown.ReachMetres * 0.16));
                    });

                    ejecta = $"ejecta {thrown.ReachMetres:F0} m at {thrown.SpeedMetresPerSecond:F0} m/s "
                             + $"for {thrown.FlightSeconds:F1} s (gravity {gravity:F2} m/s2)";
                }
            }

            Log.Info($"airless burst: {kt:F2} kt at {burstAltitudeMetres:F0} m over the ground, "
                     + $"shell {shellRadius:F0} m in {shellSeconds:F2} s, " + ejecta);

            return AirlessBurst.ExtentOf(chargeKg, gravity, burstAltitudeMetres);
        }
        catch (Exception e)
        {
            if (!_warned)
            {
                _warned = true;
                Log.Warn($"airless burst failed to draw: {e.Message}");
            }

            return default;
        }
    }

    /// <summary>The condensation cloud, sized by <see cref="MushroomCloud.WilsonRadius"/>.</summary>
    public static void BeginWilson(Celestial body, double3 burstCcf, double chargeKg)
    {
        if (!Detonation.ParticlesEnabled) return;

        try
        {
            double radius = MushroomCloud.WilsonRadius(chargeKg);
            double seconds = MushroomCloud.WilsonSeconds(chargeKg);
            if (!(radius > 0.0) || !(seconds > 0.0)) return;

            BubbleOrigin origin = EmitterPool.At(body, burstCcf, double3.Zero);

            // KSA's volumetric particle takes its density per METRE: the depth to the sun through it is
            // about 0.31 x radius x density, and along the view 1.12 x (Shared.glsl, Volumetric.comp).
            // At the emitter's own opacity a sphere a kilometre across is hundreds deep and draws as black
            // smoke, so the density is set against the radius to keep the shell translucent at any size.
            Fire(WilsonId, body, origin, e =>
            {
                e.ParticleInfo.Size = new float2((float)radius, (float)radius);
                e.ParticleInfo.Lifespan = new float2((float)seconds, (float)seconds);
                e.Opacity = (float)(WilsonDepth / (1.12 * radius));
            });

            Log.Info($"condensation cloud: {radius:F0} m for {seconds:F2} s");
        }
        catch (Exception e)
        {
            if (!_warned)
            {
                _warned = true;
                Log.Warn($"condensation cloud failed to draw: {e.Message}");
            }
        }
    }

    /// <summary>
    /// How long the view is held on this burst: <see cref="ChaseView.LingerSeconds"/> with the same
    /// gravity the ejecta is drawn with.
    /// </summary>
    public static double LingerSeconds(double3 burstEcl, Vehicle? near, double chargeKg, Celestial? known = null)
    {
        try
        {
            if (Detonation.BodyFor(near, known) is { } body)
            {
                double3 burstCcf = (burstEcl - body.GetPositionEcl()).Transform(body.GetCce2Ccf());
                if (Vec.IsFinite(burstCcf))
                {
                    // Above the ground, the measure Begin decides on: the mean sphere is 1.6 km off
                    // on high ground, and the two would disagree about whether anything was thrown.
                    return ChaseView.LingerSeconds(chargeKg, KsaWorld.HasAtmosphere(body),
                                                   GravityAt(body, burstCcf),
                                                   KsaWorld.HeightAboveTerrain(body, burstEcl));
                }
            }
        }
        catch
        {
            // Fall through to the cloud's own duration below.
        }

        // A burst whose body cannot be resolved is held as one in air.
        return ChaseView.LingerSeconds(chargeKg, hasAir: true, 0.0, 0.0);
    }

    // MoveAwayFromCenter multiplies a unit direction by this componentwise, so the three have to
    // agree or the throw comes out as an ellipsoid. The shell has no use for it: that one is a
    // single sphere expanded by its envelope.
    private static void Speed(ref ParticleEmitter<ParticleUpdateData, ParticleRenderData>.ParticleSpawnInfo info,
                              double metresPerSecond)
    {
        float v = (float)metresPerSecond;
        info.Velocity = new float3(v, v, v);
    }

    private static double GravityAt(Celestial body, double3 positionCcf)
    {
        double3 positionEcl = body.GetPositionEcl() + positionCcf.Transform(body.GetCcf2Cce());
        double g = Vec.Len(KsaWorld.GravityAt(body, positionEcl));
        return double.IsFinite(g) ? g : 0.0;
    }

    private static void Fire(string id, Celestial body, BubbleOrigin origin,
                             Action<ParticleEmitter<ParticleUpdateData, ParticleRenderData>> tune)
    {
        if (EmitterPool.Fire(id, body, origin, tune) > 0 || _warned) return;

        _warned = true;
        Log.Warn($"no free emitters for '{id}'; the burst will draw without it");
    }
}
