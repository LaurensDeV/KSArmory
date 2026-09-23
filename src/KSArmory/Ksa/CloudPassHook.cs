using System.Reflection;
using Brutal.VulkanApi;
using HarmonyLib;
using KSA;

namespace KSArmory;

/// <summary>
/// Where this mod's own full-screen pass is recorded into KSA's frame — the fifth place the mod
/// patches the game, and the first inside the renderer.
///
/// <para><b>An ordinary prefix on a public method, which is the point.</b> The published way to get
/// a shader into KSA's frame is an IL transpiler over <c>Program.RenderGame</c> that matches a
/// <c>BeginRenderPass</c> call and reads locals by slot index; that breaks on a recompile that
/// merely reorders them, silently. <c>SunbloomRenderer.Render(CommandBuffer, IViewport, int)</c> is
/// public and hands over all three arguments, so there is nothing to match and nothing to
/// index.</para>
///
/// <para><b>And it is the right instant as well as the safe one.</b> Immediately before it, KSA
/// puts the scene colour into a storage layout and the depth into a sampled one — its own barriers,
/// for its own screen-space volumetric particles — and bloom and the tonemap composite are both
/// still to come. A pass here inherits the barriers, blooms, and is graded with the scene.</para>
///
/// <para><see cref="PinTheSignature"/> is never called and exists so the patched method appears in
/// this assembly's metadata: <c>docs/KSA-API-SURFACE.md</c> then tracks it and a KSA signature
/// change is a build error rather than an effect that quietly stops drawing.</para>
///
/// <para><b>Nothing in the prefix may throw.</b> It runs inside the engine's render loop, where an
/// exception is the game rather than a log line — so <see cref="CloudPass"/> catches its own and
/// stands down.</para>
/// </summary>
internal static class CloudPassHook
{
    private const string HarmonyId = "com.laurens.ksarmory.rendering";

    private static Harmony? _harmony;
    private static Func<float>? _tint;

    /// <summary>Whether the pass is wired into the frame.</summary>
    public static bool Installed { get; private set; }

    /// <summary>
    /// Puts <c>SunbloomRenderer.Render</c> in this assembly's metadata. Never called.
    /// </summary>
    public static void PinTheSignature()
    {
        SunbloomRenderer? never = null;
        never?.Render(default, null!, 0);
    }

    /// <summary>
    /// Hooks the pass in. <paramref name="tint"/> is asked once a frame for how hard to draw, and
    /// zero is the pass doing nothing — which is what the switch being off looks like.
    /// </summary>
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
        _harmony = null;
        Installed = false;
    }

    private static void BeforeSunbloom(CommandBuffer commandBuffer, IViewport viewport, int frameIndex)
    {
        float tint = _tint?.Invoke() ?? 0f;
        if (tint <= 0f) return;

        CloudPass.Record(commandBuffer, viewport, frameIndex, tint);
    }
}
