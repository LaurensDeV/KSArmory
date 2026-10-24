using System.Reflection;
using Brutal.VulkanApi;
using HarmonyLib;
using KSA;

namespace KSArmory;

/// <summary>
/// Where this mod's full-screen pass is recorded into KSA's frame: a prefix on the public
/// <c>SunbloomRenderer.Render</c>, rather than an IL transpiler over <c>Program.RenderGame</c> that
/// reads locals by slot index and breaks silently on a reorder.
/// <para>Just before it KSA puts the scene colour in a storage layout and the depth in a sampled one,
/// and bloom and the tonemap are still to come. Nothing in the prefix may throw: it runs inside the
/// render loop, so <see cref="CloudPass"/> catches its own and stands down.</para>
/// </summary>
internal static class CloudPassHook
{
    private const string HarmonyId = "com.laurens.ksarmory.rendering";

    private static Harmony? _harmony;
    private static Func<float>? _tint;

    public static bool Installed { get; private set; }

    // Never called: puts the patched method in docs/KSA-API-SURFACE.md, so a signature change is a build error.
    public static void PinTheSignature()
    {
        SunbloomRenderer? never = null;
        never?.Render(default, null!, 0);
    }

    /// <summary><paramref name="tint"/> is asked once a frame for how hard to draw the cloud; zero skips it.</summary>
    public static void Install(Func<float> tint)
    {
        _tint = tint;

        if (Installed) return;

        try
        {
            MethodInfo? target = typeof(SunbloomRenderer).GetMethod(
                nameof(SunbloomRenderer.Render), [typeof(CommandBuffer), typeof(IViewport), typeof(int)]);

            if (target is null)
            {
                Log.Warn("KSA has no SunbloomRenderer.Render(CommandBuffer, IViewport, int) to hook; "
                         + "this mod's own shader pass will not draw");
                return;
            }

            MethodInfo prefix = typeof(CloudPassHook).GetMethod(
                nameof(BeforeSunbloom), BindingFlags.NonPublic | BindingFlags.Static)!;

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, prefix: new HarmonyMethod(prefix));

            Installed = true;
            Log.Info("the mod's own shader pass runs before bloom, via SunbloomRenderer.Render");
        }
        catch (Exception e)
        {
            Log.Warn($"could not hook the shader pass: {e.Message}; it will not draw");
        }
    }

    /// <summary>Unhooks it, and drops the pipeline with it.</summary>
    public static void Remove()
    {
        try { _harmony?.UnpatchAll(HarmonyId); }
        catch { /* Going away anyway. */ }

        CloudPass.Release();
        SensorPass.Release();
        _harmony = null;
        Installed = false;
    }

    private static void BeforeSunbloom(CommandBuffer commandBuffer, IViewport viewport, int frameIndex)
    {
        // Always: at zero the pass skips only the cloud, and the rings, holes and tracers it paints
        // have no other way onto the screen.
        float tint = _tint?.Invoke() ?? 0f;
        CloudPass.Record(commandBuffer, viewport, frameIndex, Math.Max(0f, tint));

        // Last and whatever the cloud pass is set to: a sensor sees the burst too, and its look is
        // the director's setting rather than a display one.
        SensorPass.Record(commandBuffer, viewport);
    }
}
