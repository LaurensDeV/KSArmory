using Brutal.Numerics;
using KSA;
using KSA.Rendering.Particles;

namespace KSArmory;

/// <summary>
/// Taking an endless emitter set out of the engine's particle pool, pointing it, and giving it back.
///
/// <para>Leaf functions rather than a base class: the effects holding these differ in what they key
/// on, how many sets one holds and when they let go, and agree only on these three steps. The third
/// is the one that matters — a set never given back is never returned to the pool, and once the pool
/// is dry nothing in the world can spawn particles again.</para>
/// </summary>
internal static class EmitterPool
{
    /// <summary>
    /// A set of emitters for <paramref name="id"/>, attached to <paramref name="body"/>, or null when
    /// the pool has none free. Exceptions are the caller's, so each effect can say what it lost.
    /// </summary>
    public static List<ParticleEmitter<ParticleUpdateData, ParticleRenderData>.Handle>? Take(
        string id, Celestial body)
    {
        if (!Program.Instance.ParticleSystem.GetAndInitializeEmitters(id, out var handles)
            || handles is null || handles.Count == 0)
        {
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

    /// <summary>Where a set of emitters spawns from, at a point in <paramref name="body"/>'s rotating frame.</summary>
    public static BubbleOrigin At(Celestial body, double3 positionCcf, double3 velocityCcf) => new()
    {
        Time = Universe.GetElapsedTime(),
        Parent = body,
        BubFrame = BubbleFrame.Ccf,
        PositionBub = positionCcf,
        VelocityBub = velocityCcf,
    };

    /// <summary>Moves every emitter of a set to <paramref name="origin"/>.</summary>
    public static void Point(List<ParticleEmitter<ParticleUpdateData, ParticleRenderData>.Handle> handles,
                             BubbleOrigin origin)
    {
        foreach (var handle in handles)
        {
            if (handle.TryGet() is not { } emitter) continue;

            emitter.Origin = origin;
        }
    }

    /// <summary>Stops a set and hands it back to the pool. Safe on emitters the engine has already reclaimed.</summary>
    public static void Give(Celestial body, List<ParticleEmitter<ParticleUpdateData, ParticleRenderData>.Handle> handles)
    {
        // Kill() first, and it is what actually stops it. Celestial.RemoveEmitter only drops the
        // handle from that body's list; ParticleSystem.UpdateEmitters walks the whole pool, so a
        // removed emitter keeps being updated. An Endless one never completes its own simulation,
        // so it spawns for the rest of the session and is never returned to the pool -- which is
        // seen as particles frozen where the emitter last was, and eventually as nothing in the
        // world being able to spawn any.
        foreach (var handle in handles)
        {
            try
            {
                if (handle.TryGet() is { } emitter) emitter.Kill();
                body.RemoveEmitter(handle);
            }
            catch { /* An emitter the engine has already reclaimed, or a body torn down mid-frame, has already stopped it. */ }
        }
    }
}
