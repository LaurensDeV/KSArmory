using Brutal.Numerics;
using KSA;
using KSA.Rendering.Particles;

namespace KSArmory;

/// <summary>
/// The flash at the cannon's muzzles: one endless emitter per battery, held open while the gun is
/// firing and handed back the moment it stops.
///
/// <para>Per battery rather than per round, which is the whole design. A CIWS cycles at 75 rounds
/// a second; taking a burst emitter from the pool that often would drain it within a second and
/// leave nothing anywhere in the world able to spawn particles again. A gun firing is one
/// continuous event, so it gets one continuous emitter.</para>
///
/// <para>Anchored to the barrel cluster's centre rather than to whichever barrel just fired. The
/// six muzzles sit within 10 cm of each other, so the difference is invisible, and averaging them
/// keeps the flash on the cluster axis as the gun elevates instead of hopping between barrels.</para>
///
/// <para>The tracers are <em>not</em> here, and cannot be. A muzzle-anchored emitter has no way to
/// throw particles down the bore: the engine assigns <c>EmitterVelocity</c> only for a
/// vehicle-parented emitter, so <c>InheritVelocity</c> has nothing to inherit from a celestial
/// parent, and directional spawning is built about a fixed axis of the body frame rather than the
/// turret's. See <see cref="TracerTrail"/>, which follows the shells instead.</para>
///
/// <para>Emitters come from a pool, so every one taken must be returned — <see cref="ReleaseAll"/>
/// is not tidiness, it is the reason this class holds any state at all. Same contract as
/// <see cref="MotorPlume"/>.</para>
/// </summary>
internal sealed class MuzzleFlash
{
    private const string FlashId = "KSArmoryMuzzleFlash";

    private sealed class Live
    {
        public required Celestial Body;
        public required List<ParticleEmitter<ParticleUpdateData, ParticleRenderData>.Handle> Flash;
    }

    private readonly Dictionary<IEffectSource, Live> _firing = [];
    private readonly List<IEffectSource> _stopped = [];

    private static bool _warned;

    /// <summary>Starts, moves and ends the flash for every battery whose cannon are firing.</summary>
    public void Update(IEffectSource battery)
    {
        bool wanted = battery.PlumesEnabled
                      && battery.GunsFiring
                      && battery.Platform is not null
                      && battery.TryGunFlashEcl(out _, out _);

        if (!wanted)
        {
            Release(battery);
            return;
        }

        Follow(battery);
    }

    /// <summary>
    /// Hands back the emitters of any battery the roster has forgotten.
    ///
    /// <para>A craft destroyed mid-burst never reaches <see cref="Update"/> again, so without this
    /// its emitter is held for the rest of the session and the pool bleeds one per kill.</para>
    /// </summary>
    public void Sweep(WeaponSystems roster)
    {
        foreach (IEffectSource battery in _firing.Keys)
        {
            bool present = false;
            foreach (WeaponSystems.Entry e in roster.All)
            {
                if (ReferenceEquals(e.Battery, battery)) { present = true; break; }
            }
            if (!present) _stopped.Add(battery);
        }

        foreach (IEffectSource battery in _stopped) Release(battery);
        _stopped.Clear();
    }

    /// <summary>Hands every emitter back. Safe at any time.</summary>
    public void ReleaseAll()
    {
        foreach (Live live in _firing.Values) Give(live);
        _firing.Clear();
    }

    private void Follow(IEffectSource battery)
    {
        if (battery.Platform is not { } platform) return;
        if (!battery.TryGunFlashEcl(out double3 ecl, out _)) return;

        if (!_firing.TryGetValue(battery, out Live? live))
        {
            if (Acquire(platform) is not { } fresh) return;

            live = fresh;
            _firing[battery] = live;
        }

        double3 centre = live.Body.GetPositionEcl();
        double3 positionCcf = (ecl - centre).Transform(live.Body.GetCce2Ccf());
        if (!Vec.IsFinite(positionCcf)) return;

        var at = new BubbleOrigin
        {
            Time = Universe.GetElapsedTime(),
            Parent = live.Body,
            BubFrame = BubbleFrame.Ccf,
            PositionBub = positionCcf,

            // Zero for the flash, and InheritVelocity is off to match: gas leaves the barrel and
            // stays where the air is.
            VelocityBub = double3.Zero,
        };

        Point(live.Flash, at);
    }

    private static void Point(List<ParticleEmitter<ParticleUpdateData, ParticleRenderData>.Handle> handles,
                              BubbleOrigin origin)
    {
        foreach (var handle in handles)
        {
            if (handle.TryGet() is not { } emitter) continue;

            emitter.Origin = origin;
        }
    }

    private static Live? Acquire(Vehicle platform)
    {
        try
        {
            if (platform.Parent is not Celestial body) return null;

            if (Take(FlashId, body) is not { } flash) return null;

            return new Live { Body = body, Flash = flash };
        }
        catch (Exception e)
        {
            if (!_warned)
            {
                _warned = true;
                Log.Warn($"muzzle flash failed to start: {e.Message}");
            }
            return null;
        }
    }

    private static List<ParticleEmitter<ParticleUpdateData, ParticleRenderData>.Handle>? Take(
        string id, Celestial body)
    {
        if (!Program.Instance.ParticleSystem.GetAndInitializeEmitters(id, out var handles)
            || handles is null || handles.Count == 0)
        {
            if (!_warned)
            {
                _warned = true;
                Log.Warn($"no free emitters for '{id}'; the cannon will fire without it");
            }
            return null;
        }

        foreach (var handle in handles)
        {
            if (handle.TryGet() is not { } emitter) continue;

            emitter.Context.Astronomical = body;
            emitter.Context.Vehicle = null;
            emitter.Context.Part = null;
            body.AddEmitter(handle);
        }

        return [.. handles];
    }

    private void Release(IEffectSource battery)
    {
        if (!_firing.Remove(battery, out Live? live)) return;

        Give(live);
    }

    private static void Give(Live live)
    {
        // Kill() first, and it is what actually stops it. Celestial.RemoveEmitter only drops the
        // handle from that body's list; ParticleSystem.UpdateEmitters walks the whole pool, so a
        // removed emitter keeps being updated. An Endless one never completes its own simulation,
        // so it spawns for the rest of the session and is never returned to the pool -- which is
        // seen as particles frozen where the emitter last was, and eventually as nothing in the
        // world being able to spawn any.
        foreach (var handle in live.Flash)
        {
            try
            {
                if (handle.TryGet() is { } emitter) emitter.Kill();
                live.Body.RemoveEmitter(handle);
            }
            catch { /* A body torn down mid-frame has already taken its emitters with it. */ }
        }
    }
}
