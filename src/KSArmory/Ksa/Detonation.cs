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
    // Two of each. The Volumetric renderer is KSA's screen-space particle renderer, and its draw
    // commands are only issued when GameSettings.Graphics.ScreenSpaceParticles is on -- which
    // defaults to off. A volumetric emitter on a default install resolves, acquires, registers,
    // spawns, ages and draws nothing at all, with no error anywhere.
    private const string FireballVolumetric = "KSArmoryFireball";
    private const string FireballSolid = "KSArmoryFireballSolid";
    private const string AirburstVolumetric = "KSArmoryAirburst";
    private const string AirburstSolid = "KSArmoryAirburstSolid";

    /// <summary>The kill: a bright ball with fire, fragments and smoke.</summary>
    public static string Fireball => SoftParticles ? FireballVolumetric : FireballSolid;

    /// <summary>A round that fused and did not kill. Smaller and paler on purpose.</summary>
    public static string Airburst => SoftParticles ? AirburstVolumetric : AirburstSolid;

    /// <summary>
    /// Whether the volumetric renderer will actually draw. Off by default in KSA, and the
    /// difference between smoke that looks like smoke and smoke that looks like a heap of balls.
    /// </summary>
    public static bool SoftParticles
    {
        get
        {
            try { return GameSettings.Current.Graphics.ScreenSpaceParticles; }
            catch { return false; }
        }
    }

    // Reported once per distinct reason rather than per round: a twelve-round salvo would
    // otherwise bury the engagement it belongs to. Keyed by emitter Id, not a single flag,
    // because the variant changes with a graphics setting and the new one has told nobody
    // anything yet.
    private static readonly HashSet<string> _describedBursts = [];

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
        string why = TryShow(emitterId, burstEcl, near, scale);
        if (why.Length == 0) return;

        // A silent refusal is indistinguishable from a wrong frame, a disabled renderer and a
        // warhead that never went off: all four are "no explosion". Say which, once per reason.
        if (_reported.Add(why)) Log.Warn($"no burst ({emitterId}): {why}");
    }

    private static readonly HashSet<string> _reported = [];

    // Empty on success, otherwise why not.
    private static string TryShow(string emitterId, double3 burstEcl, Vehicle? near, float scale)
    {
        if (!Vec.IsFinite(burstEcl)) return "burst position is not finite";
        if (!float.IsFinite(scale) || scale <= 0f) return $"bad scale {scale}";

        try
        {
            if (BodyFor(near) is not { } body)
            {
                return "no celestial to hang it on";
            }

            double3 positionCcf = (burstEcl - body.GetPositionEcl()).Transform(body.GetCce2Ccf());
            if (!Vec.IsFinite(positionCcf)) return "burst position does not convert to body-fixed";

            if (!Program.Instance.ParticleSystem.GetAndInitializeEmitters(emitterId, out var handles))
            {
                return "no free emitters in the pool";
            }

            if (handles is null || handles.Count == 0) return "the emitter resolved to no emitters";

            BubbleOrigin origin = new()
            {
                Time = Universe.GetElapsedSimTime(),
                Parent = body,
                BubFrame = BubbleFrame.Ccf,
                PositionBub = positionCcf,
                VelocityBub = double3.Zero,
            };

            int placed = 0;
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
                placed++;

                // Once per session. An emitter renders only when it holds both a renderer and a
                // compute pipeline, both from the XML's Renderer and Updaters elements, so a bad
                // name leaves it acquired, positioned and invisible with nothing thrown.
                if (!_describedBursts.Contains(emitterId))
                {
                    ParticleEmitter<ParticleUpdateData, ParticleRenderData> e = emitter;
                    Log.Info($"burst {emitterId} stage {placed}: registered={e.IsRegistered} "
                             + $"max={e.MaximumParticleCount} spawn={e.SpawnRate} "
                             + $"life={e.ParticleInfo.Lifespan.X:F1}-{e.ParticleInfo.Lifespan.Y:F1}s "
                             + $"size={e.ParticleInfo.Size.X:F2}-{e.ParticleInfo.Size.Y:F2}");
                }
            }

            if (_describedBursts.Add(emitterId))
            {
                Log.Info($"burst {emitterId}: {placed} of {handles.Count} stage(s) placed at "
                         + $"{positionCcf.X:F0},{positionCcf.Y:F0},{positionCcf.Z:F0} on {body.Id}");
            }

            return placed == 0 ? "every handle came back empty" : string.Empty;
        }
        catch (Exception e)
        {
            return $"{e.GetType().Name}: {e.Message}";
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
