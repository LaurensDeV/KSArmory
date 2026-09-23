using Brutal.Numerics;
using KSA;
using KSA.Rendering.Lighting;

namespace KSArmory;

/// <summary>
/// The light a nuclear fireball casts on everything round it.
///
/// <para><b>The light alone; the ball itself is the cloud pass's</b> -- its fire, the glare and
/// the whiteout. A mesh sphere inside the raymarched cloud reads as a hard-edged dark ball once it
/// is under the bloom threshold, and it is placed a step before the camera moves where the cloud is
/// placed at render, so it drifts off the cloud's centre whenever the camera pans.</para>
///
/// <para>Re-submitted every frame: lights are cleared and re-pushed in the same pass.</para>
/// </summary>
internal static class Fireball
{
    // The linear albedo the ball's colour was authored against, which its light is sized from.
    private const float AlbedoLinear = 0.4535f;

    // How far the burst's light reaches, in its own radii: 3.2 km at 0.3 kt.
    private const double RangeInRadii = 60.0;

    private static int _lights;
    private static bool _stoodDown;

    /// <summary>Whether the burst can light the world, or the engine's own spawner has the list.</summary>
    public static bool LightAccepted => LightDebug.Target is null;

    /// <summary>One frame of one fireball's light. <paramref name="glow"/> is its brightness.</summary>
    public static void Draw(double3 centreEcl, double radiusMetres, float3 colour, float glow)
    {
        if (!double.IsFinite(radiusMetres) || radiusMetres <= 0.0) return;
        if (!float.IsFinite(glow) || glow <= 0.0f) return;
        if (!KsaWorld.TryEclToCameraEgo(centreEcl, out double3 centreEgo)) return;

        try
        {
            Illuminate(centreEgo, radiusMetres, colour, glow);
        }
        catch (Exception e)
        {
            Log.Warn($"fireball light refused: {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>Drops the light, for a frame with no fireball in it.</summary>
    public static void Clear()
    {
        if (_lights <= 0) return;

        try
        {
            int from = LightDebug.Lights.Count - _lights;
            if (from >= 0) LightDebug.Lights.RemoveRange(from, _lights);
        }
        catch
        {
            // Somebody else has been at the list. Ours are gone either way.
        }

        _lights = 0;
    }

    // Through LightDebug's list rather than the light system directly, and that is the only route
    // that works: the system clears its lights after the mod's hook and re-pushes this list, so a
    // light created directly is wiped before anything renders.
    private static void Illuminate(double3 centreEgo, double radiusMetres, float3 colour, float glow)
    {
        Clear();

        // Stand down entirely if the engine's own light spawner is live. MovePointLights walks
        // this list against parallel lists only that spawner fills, so an entry of ours is
        // rewritten at best and throws out of the frame loop at worst -- and its target is never
        // cleared once set.
        if (LightDebug.Target is not null)
        {
            if (!_stoodDown)
            {
                _stoodDown = true;
                Log.Warn("fireball light stood down: KSA's own light spawner has the list. Bursts "
                         + "will glow but will not light anything.");
            }

            return;
        }

        // The engine sizes an exhaust light this way: a uniformly emitting sphere puts out its own
        // surface radiance over its whole area, so intensity is that times the area.
        float radiance = AlbedoLinear * glow;
        float intensity = radiance * 4f * MathF.PI * (float)(radiusMetres * radiusMetres);

        // Out to where a burst is watched from, so the land round the watcher is lit by it; the
        // falloff has made it faint by then. No shadows: the terrain takes this light through KSA's
        // forward list, which has none, so a shadow map would cost a cube of the whole landscape a
        // frame for nothing on the ground.
        LightDebug.Lights.Add(Light.CreatePointLight(
            centreEgo, (float)(radiusMetres * RangeInRadii), colour, intensity, ELightFlags.None));

        _lights = 1;
    }
}
