using Brutal.Numerics;
using KSA;
using KSA.Rendering.Particles;

namespace KSArmory;

/// <summary>
/// The nuclear burst on a body with no air: thrown ground, and the debris shell over it.
///
/// <para>What <see cref="NuclearClouds"/> draws needs an atmosphere twice over — a mushroom is
/// buoyant, and KSA raymarches the trail volume only for an <c>AtmosphericBody</c>. So on the Moon
/// the cloud was laid and never drawn. This is what stands in for it, through the particle system,
/// which writes its commands in the main render path and so draws anywhere.</para>
///
/// <para><b>Both emitters are one-shot.</b> Their XML declares <c>Burst</c>, so each spawns its
/// particles once and hands itself back to the pool when they die. That is the whole reason this
/// class holds no state: an <c>Endless</c> emitter would have to be tracked and killed, which is
/// what <see cref="MuzzleFlash"/> exists to do.</para>
///
/// <para><see cref="AirlessBurst"/> is the arithmetic and this is the plumbing. Nothing here
/// decides a size.</para>
/// </summary>
internal static class BurstEjecta
{
    private const string EjectaId = "KSArmoryNuclearEjecta";
    private const string ShellId = "KSArmoryNuclearShell";

    private static bool _warned;

    /// <summary>
    /// Throws the ground and lights the shell, for a burst on a body with no air.
    ///
    /// <para>Called by <see cref="NuclearClouds.Begin"/> rather than from the weapon, so a burst
    /// has one entry point and no call site has to know which kind of body it is over.</para>
    /// </summary>
    public static void Begin(Celestial body, double3 burstCcf, double chargeKg, double burstAltitudeMetres)
    {
        if (!Detonation.ParticlesEnabled) return;

        try
        {
            double kt = MushroomCloud.KilotonsFor(chargeKg);
            double fireball = MushroomCloud.PeakFireballRadius(kt);
            if (!(fireball > 0.0)) return;

            BubbleOrigin origin = new()
            {
                Time = Universe.GetElapsedTime(),
                Parent = body,
                BubFrame = BubbleFrame.Ccf,
                PositionBub = burstCcf,

                // At rest in the body's frame: the dust belongs to the ground, not to whatever was
                // hit, and it has to still be over the crater when it lands.
                VelocityBub = double3.Zero,
            };

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

            // Said whatever happens, including when nothing is thrown. Reporting only the case
            // that drew something is what made a burst drawing half its effect look like a burst
            // the mod had never seen: the shell fired, the dust did not, and the log was silent.
            string ejecta = $"no ejecta: a {fireball:F0} m fireball does not reach the ground";

            if (AirlessBurst.ThrowsEjecta(chargeKg, burstAltitudeMetres))
            {
                double gravity = GravityAt(body, burstCcf);
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

                        // Long enough for the arc to land. Cut short, the dust vanishes at the top
                        // of its climb, which reads as it being deleted rather than falling.
                        e.ParticleInfo.Lifespan = new float2((float)(thrown.FlightSeconds * 0.7),
                                                             (float)thrown.FlightSeconds);

                        // The envelope swells each puff 3.4x over its flight, so this is where it
                        // starts rather than how big the dust ends up.
                        e.ParticleInfo.Size = new float2((float)(thrown.ReachMetres * 0.06),
                                                         (float)(thrown.ReachMetres * 0.12));
                    });

                    ejecta = $"ejecta {thrown.ReachMetres:F0} m at {thrown.SpeedMetresPerSecond:F0} m/s "
                             + $"for {thrown.FlightSeconds:F1} s (gravity {gravity:F2} m/s2)";
                }
            }

            Log.Info($"airless burst: {kt:F2} kt at {burstAltitudeMetres:F0} m over the ground, "
                     + $"shell {shellRadius:F0} m in {shellSeconds:F2} s, " + ejecta);
        }
        catch (Exception e)
        {
            if (!_warned)
            {
                _warned = true;
                Log.Warn($"airless burst failed to draw: {e.Message}");
            }
        }
    }

    /// <summary>
    /// How long the view is held on this burst, resolved against the body it happened over.
    ///
    /// <para>This half reads the world and <see cref="ChaseView.LingerSeconds"/> decides, through
    /// the same gravity the ejecta is drawn with — so the camera is held for exactly as long as
    /// there is something in the picture.</para>
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
                    return ChaseView.LingerSeconds(chargeKg, KsaWorld.HasAtmosphere(body),
                                                   GravityAt(body, burstCcf),
                                                   Vec.Len(burstCcf) - body.MeanRadius);
                }
            }
        }
        catch
        {
            // Fall through to the cloud's own duration below.
        }

        // What held the view before any of this existed, for a burst whose body cannot be resolved.
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
        if (!Program.Instance.ParticleSystem.GetAndInitializeEmitters(id, out var handles)
            || handles is null || handles.Count == 0)
        {
            if (!_warned)
            {
                _warned = true;
                Log.Warn($"no free emitters for '{id}'; the burst will draw without it");
            }
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
            tune(emitter);
            body.AddEmitter(handle);
        }
    }
}
