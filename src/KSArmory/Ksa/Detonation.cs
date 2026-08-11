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
    // because the variant changes with a graphics setting and each variant is worth describing
    // on its own.
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
    /// The bang. One-shot and untracked, unlike a motor: a burst is over before anything could
    /// want to move or stop it.
    /// </summary>
    public static void Bang(double3 burstEcl, Vehicle? near, float scale, Config config)
    {
        if (!config.BurstSound) return;

        try
        {
            if (near is null) return;
            if (ModLibrary.Get<SoundBehavior>(config.BurstSoundId ?? DefaultBurstId) is not { } sound)
            {
                if (!_warnedNoBang)
                {
                    _warnedNoBang = true;
                    Log.Warn($"burst sound '{config.BurstSoundId ?? DefaultBurstId}' does not resolve");
                }
                return;
            }

            Camera camera = GameAudio.GetAudioCamera();

            // Relative to something the camera can locate, then offset: an Ecl point on its own
            // has nothing to convert against.
            double3 posEgo = camera.GetPositionEgo(near) + (burstEcl - KsaWorld.PositionEcl(near));
            double3 velEgo = camera.GetVelocityEgo(near);

            if (!Vec.IsFinite(posEgo)) return;

            var spatial = new SpatialAudio(posEgo, velEgo,
                                           PhysicalAtmosphereReference.GetAtmosphericPressure(camera));

            // Louder for a bigger warhead, on the same scale the fireball is drawn at, so a 30 mm
            // shell cannot sound like a 20 kg missile.
            sound.Play(spatial, Math.Clamp(config.BurstVolume * scale, 0.05f, 1f), out IChannel? channel);

            // And deeper. A big charge puts its energy at low frequencies, so pitch falls as the
            // warhead grows: a shell cracks, a missile thumps.
            if (channel is not null)
            {
                channel.PitchMultiplier = Math.Clamp(1.18f - (0.32f * scale), 0.8f, 1.2f);
            }
        }
        catch (Exception e)
        {
            if (!_warnedNoBang)
            {
                _warnedNoBang = true;
                Log.Warn($"burst sound failed: {e.Message}");
            }
        }
    }

    // The mod's own, synthesised by tools/sounds.py. Core ships no explosion at all - its whole
    // sound library is nine entries - and the nearest thing, a decoupler's separation charge, is
    // a metal detach that reads as dropping a sheet of steel however it is pitched.
    private const string DefaultBurstId = "KSArmoryBurst";
    private static bool _warnedNoBang;

    /// <summary>
    /// Shows a burst at a point in Ecl. Silently does nothing if the effect cannot be placed:
    /// this is decoration, and a warhead that killed its target has already done its job.
    /// </summary>
    /// <param name="near">
    /// Any craft close to the burst, used only to find which body to hang the effect on. The
    /// round's own target or the firing platform both do.
    /// </param>
    /// <param name="scale">Multiplies particle size and speed, so a bigger warhead looks bigger.</param>
    public static void Show(string emitterId, double3 burstEcl, Vehicle? near, float scale = 1f)
    {
        // A large charge is drawn as several bursts spread through the ball rather than one burst
        // with everything about it multiplied.
        //
        // Scaling one emitter does not add a single particle: the authored cloud has the count it
        // has, so at 25x each particle is 25x across and the result is a handful of enormous blobs
        // with gaps between them. What reads as a big explosion is *more* particles filling a
        // volume, and the only way to get more from a fixed emitter is more emitters.
        //
        // Each stays at a size that still looks like a particle, and the count makes up the volume.
        float per = Math.Min(scale, ModerateScale);
        int count = scale <= ModerateScale
                        ? 1
                        : Math.Clamp((int)Math.Round(Math.Pow(scale / per, 2.0)), 2, MaxBursts);

        if (count > 1)
        {
            ShowSpread(emitterId, burstEcl, near, per, count, ReferenceFireballMetres * scale);
            return;
        }

        string why = TryShow(emitterId, burstEcl, near, scale);
        if (why.Length == 0) return;

        // A silent refusal is indistinguishable from a wrong frame, a disabled renderer and a
        // warhead that never went off: all four are "no explosion". Say which, once per reason.
        if (_reported.Add(why)) Log.Warn($"no burst ({emitterId}): {why}");
    }

    // Largest a single burst is drawn at before the answer is more of them instead.
    private const float ModerateScale = 6f;

    // Most emitters one burst may take. They come from a shared pool, so a warhead that grabs
    // dozens starves every other effect in the world -- the trade MotorPlume already names.
    private const int MaxBursts = 12;

    // The authored emitter's own fireball radius, which is what one unit of scale is.
    private static readonly double ReferenceFireballMetres =
        Warhead.FireballRadius(Warhead.ReferenceChargeKg);

    // Spread through the ball on a golden-angle spiral: deterministic, so the same burst looks the
    // same twice, and even, which a random scatter is not at these counts -- clumps and a bare
    // patch read as a mistake rather than as an explosion.
    private static void ShowSpread(string emitterId, double3 burstEcl, Vehicle? near,
                                   float scale, int count, double radius)
    {
        string first = "";

        for (int i = 0; i < count; i++)
        {
            double3 at = burstEcl;

            if (i > 0)
            {
                double t = (i + 0.5) / count;
                double z = 1.0 - (2.0 * t);
                double ring = Math.Sqrt(Math.Max(0.0, 1.0 - (z * z)));
                double phi = i * 2.399963;                       // golden angle, radians

                // Nearer the middle than the skin, so the ball has a dense core and a soft edge
                // instead of reading as a hollow shell.
                double out2 = radius * 0.55 * Math.Cbrt(t);

                at += new double3(ring * Math.Cos(phi), ring * Math.Sin(phi), z) * out2;
            }

            string why = TryShow(emitterId, at, near, scale);
            if (why.Length > 0 && first.Length == 0) first = why;
        }

        if (first.Length > 0 && _reported.Add(first)) Log.Warn($"no burst ({emitterId}): {first}");
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
                Time = Universe.GetElapsedTime(),
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
                    // Size and spawn radius take the whole factor -- that is what makes a big
                    // warhead look big. Velocity deliberately does not.
                    //
                    // Driving all three together is what made a large charge read as broken rather
                    // than as large: at 25x the particles leave faster than the eye can follow and
                    // the ball is gone before it forms. It is also the wrong physics. Fireball
                    // radius goes as the cube root of yield, but a larger fireball takes longer to
                    // grow, so its expansion speed rises far more slowly than its size. Big and
                    // slow is what an explosion looks like from far enough away to survive it.
                    emitter.ParticleInfo.Size *= scale;
                    emitter.ParticleInfo.Velocity *= MathF.Cbrt(scale);
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
    /// <summary>Which body to hang an effect on, given something near it.</summary>
    internal static Celestial? BodyFor(Vehicle? near)
    {
        if (KsaWorld.IsAlive(near) && near!.Parent is Celestial body) return body;
        if (KsaWorld.ControlledVehicle?.Parent is Celestial fallback) return fallback;

        return null;
    }
}
