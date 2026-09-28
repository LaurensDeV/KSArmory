using System.Reflection;
using HarmonyLib;
using KSA;

namespace KSArmory;

/// <summary>
/// Draws the bodies of rounds whose launcher has been destroyed.
///
/// <para>A round's body is a subpart of its launcher, and a destroyed craft's parts are released with it.
/// The mesh a part is drawn with is not: <c>PartModel</c> is shared by every part of its template and
/// outlives any craft, and <c>PartModel.AddInstance</c> draws it at any matrix with the part shader,
/// lighting and shadows — which is how the editor's part thumbnails are drawn with no craft at all. So a
/// loose round keeps its model and is added as an instance of it each frame.</para>
///
/// <para>A prefix on <c>PartModelRenderer.UpdateRenderData</c>, which runs once per visible viewport after
/// every craft has queued its parts and before the queue is uploaded. <c>public static</c>, so
/// <see cref="PinTheSignature"/> puts it in <c>docs/KSA-API-SURFACE.md</c>; a patch that does not apply
/// leaves loose rounds without bodies, which is what they had before. <b>Nothing here may throw</b>: it
/// runs inside the engine's render preparation.</para>
/// </summary>
internal static class LooseBodyDrawHook
{
    private const string HarmonyId = "com.kesslersystems.ksarmory.loosebodies";

    private static Action<IViewport, int>? _draw;
    private static Harmony? _harmony;
    private static bool _complained;

    public static bool Installed { get; private set; }

    public static void Install(Action<IViewport, int> draw)
    {
        _draw = draw;

        if (Installed) return;

        try
        {
            MethodInfo? target = typeof(PartModelRenderer).GetMethod(
                nameof(PartModelRenderer.UpdateRenderData), [typeof(IViewport), typeof(int)]);

            if (target is null)
            {
                Log.Warn("KSA has no PartModelRenderer.UpdateRenderData(IViewport, int) to hook; a round "
                         + "whose launcher is destroyed flies on without its body");
                return;
            }

            MethodInfo prefix = typeof(LooseBodyDrawHook).GetMethod(
                nameof(BeforeUpdateRenderData), BindingFlags.NonPublic | BindingFlags.Static)!;

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, prefix: new HarmonyMethod(prefix));

            Installed = true;
            Log.Info("rounds whose launcher is destroyed keep their bodies, via PartModelRenderer.UpdateRenderData");
        }
        catch (Exception e)
        {
            Log.Warn($"could not hook the loose-round draw; such rounds fly on without their bodies ({e.Message})");
        }
    }

    public static void Remove()
    {
        _draw = null;

        try
        {
            _harmony?.UnpatchAll(HarmonyId);
        }
        catch (Exception e)
        {
            Log.Warn($"could not remove the loose-round draw hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    // Parameter names are Harmony's contract with the original: viewport and frameIndex.
    private static void BeforeUpdateRenderData(IViewport viewport, int frameIndex)
    {
        try
        {
            _draw?.Invoke(viewport, frameIndex);
        }
        catch (Exception e)
        {
            // Stand down rather than throw again next frame from inside the render preparation.
            _draw = null;

            if (_complained) return;
            _complained = true;
            Log.Error("the loose-round draw hook failed; such rounds fly on without their bodies", e);
        }
    }

    // Never called. Puts the patched method in this assembly's metadata, so a KSA change to it is a
    // build error rather than a patch that silently stops applying.
    private static void PinTheSignature(IViewport viewport, int frameIndex)
        => PartModelRenderer.UpdateRenderData(viewport, frameIndex);
}
