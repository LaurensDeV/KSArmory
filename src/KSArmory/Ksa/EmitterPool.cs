using Brutal.Numerics;
using KSA;
using KSA.Rendering.Particles;

namespace KSArmory;

/// <summary>
/// Taking an endless emitter set out of the engine's particle pool, pointing it, and giving it back.
/// <para>A set never given back never returns to the pool, and once the pool is dry nothing in the
/// world can spawn particles again.</para>
/// </summary>
internal static class EmitterPool
{
    private static readonly HashSet<string> _unresolved = [];

    /// <summary>
    /// <paramref name="named"/> when some mod declares that emitter, otherwise <paramref name="own"/>: a
    /// pack's typo costs the look, not the effect, and is said once.
    /// </summary>
    public static string Chosen(string? named, string own)
    {
        if (string.IsNullOrEmpty(named)) return own;
        if (Declared(named)) return named;

        if (_unresolved.Add(named)) Log.Warn($"particle emitter '{named}' is not declared by any mod; using '{own}' instead");
        return own;
    }

    /// <summary>Whether some mod declares an emitter by this Id.</summary>
    public static bool Declared(string id)
    {
        try
        {
            return ModLibrary.TryGet<ParticleEmitterReference>(id, out var found) && found is not null;
        }
        catch
        {
            return false;
        }
    }

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

    /// <summary>
    /// Fires a one-shot set and returns how many emitters went; a one-shot set returns itself. Points,
    /// tunes, then registers, in <c>ExplosionSystem.FireStage</c>'s order, which <see cref="Take"/> does not.
    /// </summary>
    public static int Fire(string id, Celestial body, BubbleOrigin origin, Action<ParticleEmitter<ParticleUpdateData, ParticleRenderData>>? tune = null)
    {
        if (!Program.Instance.ParticleSystem.GetAndInitializeEmitters(id, out var handles)
            || handles is null || handles.Count == 0)
        {
            return 0;
        }

        foreach (var handle in handles)
        {
            if (handle.TryGet() is not { } emitter) continue;

            emitter.LocalOffset = float4x4.Identity;
            emitter.Context.Vehicle = null;
            emitter.Context.Part = null;
            emitter.Context.Astronomical = body;
            emitter.Origin = origin;
            tune?.Invoke(emitter);
            body.AddEmitter(handle);
        }

        return handles.Count;
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
        // Kill() is what stops it: RemoveEmitter only drops the handle from the body's list, and
        // UpdateEmitters walks the whole pool, so an Endless emitter spawns on and never returns.
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
