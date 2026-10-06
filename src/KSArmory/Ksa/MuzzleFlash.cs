using Brutal.Numerics;
using KSA;
using KSA.Rendering.Particles;

namespace KSArmory;

/// <summary>
/// The flash at the cannon's muzzles: one endless emitter per barrel cluster, held open while the
/// gun is firing and for a moment after each round, then handed back.
///
/// <para>Per system rather than per round, which is the whole design. A CIWS cycles at 75 rounds
/// a second; taking a burst emitter from the pool that often would drain it within a second and
/// leave nothing anywhere in the world able to spawn particles again. A gun firing is one
/// continuous event, so it gets one continuous emitter.</para>
///
/// <para>The moment after each round is for a gun too slow to hold a burst open. A one-round burst
/// opens and closes inside a single step, so by the time anything asks whether the gun is firing it
/// already is not, and a flash gated on that alone never appears.</para>
///
/// <para>One per cluster rather than one per mount: a Pantsir's sponsons are 3.9 m apart and their
/// mean is on the hull between them, where no barrel is. Both flash at the same instant, so they
/// cannot share a set. Within a cluster it is anchored to the mean of its muzzles rather than to
/// whichever barrel just fired, which keeps the flash on the cluster axis as the gun elevates
/// instead of hopping between barrels.</para>
///
/// <para>The tracers are <em>not</em> here, and cannot be. A muzzle-anchored emitter has no way to
/// throw particles down the bore: the engine assigns <c>EmitterVelocity</c> only for a
/// vehicle-parented emitter, so <c>InheritVelocity</c> has nothing to inherit from a celestial
/// parent, and directional spawning is built about a fixed axis of the body frame rather than the
/// turret's. They are <see cref="ShellTracers"/>, drawn in the shader pass.</para>
///
/// <para>Emitters come from a pool, so every one taken must be returned — <see cref="ReleaseAll"/>
/// is not tidiness, it is the reason this class holds any state at all. Same contract as
/// <see cref="MotorPlume"/>.</para>
/// </summary>
internal sealed class MuzzleFlash
{
    private const string FlashId = "KSArmoryMuzzleFlash";

    // Simulated time, so a paused world holds the flash rather than letting it expire unseen.
    private const double ShotFlashSeconds = 0.12;

    private sealed class Live
    {
        public required Celestial Body;
        // One set of handles per barrel cluster. A rotary cannon has one; a mount with a
        // sponson either side has two, and they fire together.
        public required List<List<ParticleEmitter<ParticleUpdateData, ParticleRenderData>.Handle>> Clusters;
    }

    private readonly Dictionary<IEffectSource, Live> _firing = [];
    private readonly List<IEffectSource> _stopped = [];

    private static bool _warned;

    /// <summary>Starts, moves and ends the flash for every system whose cannon are firing.</summary>
    public void Update(IEffectSource system)
    {
        bool wanted = system.PlumesEnabled
                      && (system.GunsFiring || system.GunSecondsSinceShot < ShotFlashSeconds)
                      && system.Platform is not null
                      && system.HasGunFlash();

        if (!wanted)
        {
            Release(system);
            return;
        }

        Follow(system);
    }

    /// <summary>
    /// Hands back the emitters of any system the roster has forgotten.
    ///
    /// <para>A craft destroyed mid-burst never reaches <see cref="Update"/> again, so without this
    /// its emitter is held for the rest of the session and the pool bleeds one per kill.</para>
    /// </summary>
    public void Sweep(WeaponSystems roster)
    {
        foreach (IEffectSource system in _firing.Keys)
        {
            if (!roster.Knows(system)) _stopped.Add(system);
        }

        foreach (IEffectSource system in _stopped) Release(system);
        _stopped.Clear();
    }

    /// <summary>Hands every emitter back. Safe at any time.</summary>
    public void ReleaseAll()
    {
        foreach (Live live in _firing.Values) Give(live);
        _firing.Clear();
    }

    private void Follow(IEffectSource system)
    {
        if (system.Platform is not { } platform) return;

        Span<double3> points = stackalloc double3[MaxClusters];
        int count = system.GunFlashPointsEcl(points);
        if (count <= 0) return;

        if (!_firing.TryGetValue(system, out Live? live))
        {
            if (Acquire(platform, count) is not { } fresh) return;

            live = fresh;
            _firing[system] = live;
        }

        double3 centre = live.Body.GetPositionEcl();
        doubleQuat cce2Ccf = live.Body.GetCce2Ccf();

        for (int i = 0; i < count && i < live.Clusters.Count; i++)
        {
            double3 positionCcf = (points[i] - centre).Transform(cce2Ccf);
            if (!Vec.IsFinite(positionCcf)) continue;

            // Zero velocity, and InheritVelocity is off to match: gas leaves the barrel and stays
            // where the air is.
            EmitterPool.Point(live.Clusters[i], EmitterPool.At(live.Body, positionCcf, double3.Zero));
        }
    }

    // More barrel clusters than any mount is going to have. A cap rather than a list so the
    // per-frame path allocates nothing.
    private const int MaxClusters = 8;

    private static Live? Acquire(Vehicle platform, int clusters)
    {
        try
        {
            if (platform.Parent is not Celestial body) return null;

            // One emitter set per cluster, taken up front. A mount with two sponsons flashes at
            // both at once, so they cannot share a set.
            var sets = new List<List<ParticleEmitter<ParticleUpdateData, ParticleRenderData>.Handle>>();
            for (int i = 0; i < clusters; i++)
            {
                if (EmitterPool.Take(FlashId, body) is not { } set)
                {
                    if (!_warned)
                    {
                        _warned = true;
                        Log.Warn($"no free emitters for '{FlashId}'; the cannon will fire without it");
                    }

                    // Give back whatever was taken, or they leak for the session.
                    foreach (var taken in sets) EmitterPool.Give(body, taken);
                    return null;
                }

                sets.Add(set);
            }

            if (sets.Count == 0) return null;

            return new Live { Body = body, Clusters = sets };
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

    private void Release(IEffectSource system)
    {
        if (!_firing.Remove(system, out Live? live)) return;

        Give(live);
    }

    private static void Give(Live live)
    {
        foreach (var set in live.Clusters) EmitterPool.Give(live.Body, set);
    }
}
