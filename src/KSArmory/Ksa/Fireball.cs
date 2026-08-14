using Brutal.Numerics;
using KSA;
using KSA.Rendering.Lighting;

namespace KSArmory;

/// <summary>
/// The nuclear flash: one emissive sphere, and a light that puts it on everything nearby.
///
/// <para>A sphere rather than particles because what is wanted is one coherent ball, and a cloud of
/// spheres is a cloud however bright each one is. It is drawn through the engine's generic mesh
/// renderer, whose shader adds <c>Color.a</c> as an unclamped self-lit term after the scene
/// lighting — so the alpha is an emissive multiplier rather than an opacity, and there is no
/// ceiling on it.</para>
///
/// <para><b>Brightness has one number that matters.</b> The bloom pass keeps a tap only if its
/// Rec.709 luminance clears 3, and the default albedo the sphere samples costs a factor of 0.4535
/// on the way. <see cref="BloomingEmissive"/> solves for the rest, so a colour can be chosen for
/// its hue and the multiplier that makes it glow follows.</para>
///
/// <para>Everything is re-submitted every frame: the renderer clears its instances after each one,
/// and lights are cleared and re-pushed in the same pass.</para>
/// </summary>
internal static class Fireball
{
    // Core's default albedo is 0.698 sRGB, and the shader gamma-decodes it before the emissive
    // multiply, so every authored colour arrives dimmed by this.
    private const float AlbedoLinear = 0.4535f;

    // Below this Rec.709 luminance a tap is discarded by the threshold bloom and nothing glows.
    private const float BloomLuminance = 3f;

    private static MeshReference? _sphere;
    private static GenericMeshRenderer.PerDrawData _textures;
    private static bool _tried;
    private static bool _usable;
    private static int _lights;
    private static bool _stoodDown;

    /// <summary>Whether the burst can light the world, or the engine's own spawner has the list.</summary>
    public static bool LightAccepted => LightDebug.Target is null;

    /// <summary>The emissive multiplier that just puts <paramref name="colour"/> at the bloom threshold.</summary>
    public static float BloomingEmissive(float3 colour)
    {
        float lum = (0.2126f * colour.X) + (0.7152f * colour.Y) + (0.0722f * colour.Z);
        return lum <= 0f ? 0f : BloomLuminance / (AlbedoLinear * lum);
    }

    /// <summary>
    /// One frame of one fireball, with the light it casts.
    ///
    /// <para><paramref name="emissive"/> is unclamped and is what decides whether it blooms.</para>
    /// </summary>
    public static void Draw(double3 centreEcl, double radiusMetres, float3 colour, float emissive)
    {
        if (!Resolve()) return;
        if (!double.IsFinite(radiusMetres) || radiusMetres <= 0.0) return;
        if (!float.IsFinite(emissive) || emissive <= 0.0f) return;
        if (!KsaWorld.TryEclToEgo(centreEcl, out double3 centreEgo)) return;

        // Ego rather than ecliptic, and not by preference: the matrix is packed to float32, where
        // an ecliptic position has a 16 km quantum. Ego is camera-relative and stays small.
        var instance = new GenericMeshRenderer.InstanceData
        {
            ModelMatrix = float4x4.Pack(double4x4.CreateScale(radiusMetres)
                                        * double4x4.CreateTranslation(centreEgo)),
            Color = new float4(colour.X, colour.Y, colour.Z, emissive),
        };

        try
        {
            GenericMeshRenderer.AddInstance(_sphere!, in instance, in _textures,
                                            Program.MainViewport,
                                            Program.Instance.ResourceFrameIndex);

            Illuminate(centreEgo, radiusMetres, colour, emissive);
        }
        catch (Exception e)
        {
            _usable = false;
            Log.Warn($"fireball refused, standing down: {e.GetType().Name}: {e.Message}");
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

    // A real light, so the launcher and anything near it are lit by the burst rather than merely
    // standing next to a bright ball.
    //
    // Through LightDebug's list rather than the light system directly, and that is the only route
    // that works: the system clears its lights after the mod's hook and re-pushes this list, so a
    // light created directly is wiped before anything renders.
    private static void Illuminate(double3 centreEgo, double radiusMetres, float3 colour,
                                   float emissive)
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
        float radiance = AlbedoLinear * emissive;
        float intensity = radiance * 4f * MathF.PI * (float)(radiusMetres * radiusMetres);

        LightDebug.Lights.Add(Light.CreatePointLight(
            centreEgo, (float)(radiusMetres * 30.0), colour, intensity, ELightFlags.None));

        _lights = 1;
    }

    // Resolved once, because ModLibrary throws rather than answering null and this runs in a frame
    // hook.
    private static bool Resolve()
    {
        if (_tried) return _usable;
        _tried = true;

        try
        {
            // Core's unit sphere: two metres across about the origin, so the scale is the radius.
            // It is also one of the few Core meshes declared interleaved, which AddInstance
            // requires and dereferences without checking.
            _sphere = ModLibrary.Get<MeshReference>("Sphere");

            if (_sphere.DeviceMeshesInterleaved is null)
            {
                Log.Warn("fireball: Core's 'Sphere' is not interleaved");
                return false;
            }

            _textures = new GenericMeshRenderer.PerDrawData
            {
                AlbedoTextureIndex = ModLibrary.Get<TextureReference>("DefaultAlbedo").BindlessHandle,
                NormalTextureIndex = ModLibrary.Get<TextureReference>("DefaultNormalMap").BindlessHandle,
                PbrTextureIndex = ModLibrary.Get<TextureReference>("DefaultPbrMap").BindlessHandle,
            };

            _usable = true;
            Log.Info("fireball: the mesh renderer is reachable");
        }
        catch (Exception e)
        {
            Log.Warn($"fireball unavailable: {e.GetType().Name}: {e.Message}");
        }

        return _usable;
    }
}
