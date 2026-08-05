using Brutal.Numerics;
using KSA;
using KSA.Rendering.Particles;

namespace KSArmory;

/// <summary>
/// A fireball where a warhead went off, through KSA's own particle system.
///
/// <para>The emitters are authored assets in <c>KSArmoryParticles.xml</c> and fired by Id, the
/// same way the mod's meshes and materials are declared — <c>GetAndInitializeEmitters</c> resolves
/// through <c>ModLibrary</c>, so a mod's emitter is as good as Core's.</para>
///
/// <para>Hosted on the <em>celestial</em>, not on a vehicle. A proximity burst happens in mid-air,
/// and the obvious host — the target — is the thing about to be destroyed. This copies
/// <c>Celestial.TrySpawnGroundImpact</c>, which is the engine's own example of placing an emitter
/// at a point with no vehicle involved.</para>
/// </summary>
internal static class Detonation
{
    /// <summary>The kill: a bright ball with a debris shell inside it.</summary>
    public const string Fireball = "KSArmoryFireball";

    /// <summary>A round that fused and did not kill. Smaller and paler on purpose.</summary>
    public const string Airburst = "KSArmoryAirburst";

    // Emitters come from a fixed pool the whole game shares, so a salvo can run it dry. Reported
    // once rather than per round: a missing effect is cosmetic, and a line per round of a
    // twelve-round salvo would bury the engagement it belongs to.
    private static bool _reportedExhaustion;
    private static bool _reportedFailure;
    private static bool _reportedFirstBurst;

    /// <summary>
    /// Whether the game is drawing particles at all. The whole system returns early when this is
    /// off, so an emitter can resolve, acquire and register and still show nothing.
    /// </summary>
    public static bool ParticlesEnabled
    {
        get
        {
            try { return GameSettings.Current.Graphics.Particles; }
            catch { return true; }
        }
    }

    /// <summary>
    /// Whether an emitter Id resolves. Checked at load and logged, because every link in this
    /// chain fails silently: a missing XML, an Id that does not resolve and an effect placed in
    /// the wrong frame all look identical in game, which is no explosion.
    /// </summary>
    public static bool Resolves(string emitterId)
    {
        try
        {
            return ModLibrary.Get<ParticleEmitterReference>(emitterId) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Shows a burst at a point in Ecl. Silently does nothing if the effect cannot be placed —
    /// this is decoration, and a warhead that killed its target has already done its job.
    /// </summary>
    /// <param name="near">
    /// Any craft close to the burst, used only to find which body to hang the effect on. The
    /// round's own target or the firing platform both do.
    /// </param>
    /// <param name="scale">Multiplies particle size and speed, so a bigger warhead looks bigger.</param>
    public static void Show(string emitterId, double3 burstEcl, Vehicle? near, float scale = 1f)
    {
        if (!Vec.IsFinite(burstEcl) || !float.IsFinite(scale) || scale <= 0f) return;

        try
        {
            if (BodyFor(near) is not { } body) return;

            // Body-fixed, as ground impacts use: the engine then applies the body's rotation to
            // the particles itself rather than leaving them behind in an inertial frame.
            double3 positionCcf = (burstEcl - body.GetPositionEcl()).Transform(body.GetCce2Ccf());
            if (!Vec.IsFinite(positionCcf)) return;

            if (!Program.Instance.ParticleSystem.GetAndInitializeEmitters(emitterId, out var handles))
            {
                if (!_reportedExhaustion)
                {
                    _reportedExhaustion = true;
                    Log.Warn($"no free particle emitters for {emitterId}; effect skipped");
                }
                return;
            }

            BubbleOrigin origin = new()
            {
                Time = Universe.GetElapsedSimTime(),
                Parent = body,
                BubFrame = BubbleFrame.Ccf,
                PositionBub = positionCcf,
                VelocityBub = double3.Zero,
            };

            foreach (ParticleEmitter<ParticleUpdateData, ParticleRenderData>.Handle handle in handles)
            {
                if (handle.TryGet() is not { } emitter) continue;

                // In the body-fixed branch the engine builds the model matrix from the origin
                // alone and ignores LocalOffset, so the burst point is PositionBub and nothing
                // else. Setting a transform here would look like it worked and do nothing.
                emitter.Context.Astronomical = body;
                emitter.Context.Vehicle = null;
                emitter.Context.Part = null;
                emitter.Origin = origin;

                if (Math.Abs(scale - 1f) > 1e-3f)
                {
                    emitter.ParticleInfo.Size *= scale;
                    emitter.ParticleInfo.Velocity *= scale;
                    emitter.EmitterSpawnInfo.Radius *= scale;
                }

                body.AddEmitter(handle);

                // Once, on the first burst of the session. An emitter renders only when it has
                // both a renderer and at least one compute pipeline, and it gets those from the
                // XML's Renderer and Updaters elements -- so a bad name there leaves it acquired,
                // positioned, and invisible, with nothing thrown anywhere.
                if (!_reportedFirstBurst)
                {
                    ParticleEmitter<ParticleUpdateData, ParticleRenderData> e = emitter;
                    Log.Info($"burst {emitterId}: registered={e.IsRegistered} "
                             + $"maxParticles={e.MaximumParticleCount} "
                             + $"particlesEnabled={ParticlesEnabled} "
                             + $"bub={positionCcf.X:F0},{positionCcf.Y:F0},{positionCcf.Z:F0}");
                }
            }

            _reportedFirstBurst = true;
        }
        catch (Exception e)
        {
            // Once, and at warning level. Swallowing this quietly is what turns "the asset never
            // loaded" into "there is no explosion", which is the same symptom as everything else.
            if (!_reportedFailure)
            {
                _reportedFailure = true;
                Log.Warn($"could not show {emitterId}: {e.Message}");
            }
        }
    }

    // The body to hang the effect on: whatever the craft nearest the burst is bound to, falling
    // back to the craft being flown. Both are within a physics bubble of the burst, which is the
    // only accuracy this needs.
    private static Celestial? BodyFor(Vehicle? near)
    {
        if (KsaWorld.IsAlive(near) && near!.Parent is Celestial body) return body;
        if (KsaWorld.ControlledVehicle?.Parent is Celestial fallback) return fallback;

        return null;
    }
}
