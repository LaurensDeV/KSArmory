using System.Reflection;
using HarmonyLib;
using KSA;

namespace KSArmory;

/// <summary>
/// A pre-render step for frames with no UI, because StarMap has no hook that is both.
///
/// <para>StarMap offers three per-frame hooks and that is the whole list: <c>BeforeGui</c> and
/// <c>AfterGui</c> are prefix and postfix on <c>OnDrawUiFrame</c> and <c>OnDrawUiViewports</c>,
/// which <c>Program.OnFrame</c> wraps in <c>if (DrawUI)</c> — and <c>DrawUI</c> is a player keybind,
/// F2. The third, <c>AfterOnFrame</c>, is a postfix on <c>OnFrame</c> itself and lands <em>after</em>
/// the render. So with the UI hidden the mod can step either before the render or at all, and
/// <see cref="FrameLatch"/> alone can only choose the second.</para>
///
/// <para>Stepped after the render, every round is drawn a step behind the world: 4 m at 250 m/s,
/// jittering with the display's alternating frame pacing.</para>
///
/// <para>A <b>prefix</b> on <c>Program.OnFrameCelestials</c>, the last phase before
/// <c>OnPreRender</c> outside that guard. It resolves the camera-nearby body and updates the planet's
/// shader data, so a step taken after it renders the surface as open water.</para>
///
/// <para>The method is private. If KSA renames it the patch does not apply, this says so once, and
/// <c>[StarMapAfterOnFrame]</c> runs the step as it would without it. <c>docs/KSA-FRAME-ORDER.md</c>
/// §7 has the guard and the frame table.</para>
///
/// <para><b>Nothing here may throw.</b> It runs inside the engine's frame loop.</para>
/// </summary>
internal static class PreRenderHook
{
    private const string HarmonyId = "com.kesslersystems.ksarmory.prerender";

    // The mod's own step. Static because Harmony patches a method, not an instance, and there is
    // one mod. Null while unloaded, which is what stops a stale patch reaching a dead mod.
    private static Action<double>? _step;

    private static Harmony? _harmony;
    private static bool _complained;

    /// <summary>Whether the step runs before the render on a frame that draws no UI.</summary>
    public static bool Installed { get; private set; }

    /// <summary>Why it is not installed. Empty when it is, or when nothing has tried.</summary>
    public static string Trouble { get; private set; } = "";

    public static void Install(Action<double> step)
    {
        _step = step;

        if (Installed) return;

        try
        {
            // Private, so by name: no compiler checks it, which is why the failure has to degrade.
            MethodInfo? target = AccessTools.Method(typeof(Program), "OnFrameCelestials",
                                                    [typeof(double)]);

            if (target is null)
            {
                Trouble = "KSA has no Program.OnFrameCelestials(double) to hook";
                Log.Warn($"stepping after the render on frames with no UI: {Trouble}");
                return;
            }

            MethodInfo prefix = typeof(PreRenderHook).GetMethod(
                nameof(BeforeOnFrameCelestials), BindingFlags.NonPublic | BindingFlags.Static)!;

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, prefix: new HarmonyMethod(prefix));

            Installed = true;
            Trouble = "";
            Log.Info("hidden-UI frames step before the render, via Program.OnFrameCelestials");
        }
        catch (Exception e)
        {
            Trouble = e.Message;
            Log.Warn($"could not hook the pre-render step; frames with no UI will be drawn one "
                     + $"frame late ({e.Message})");
        }
    }

    public static void Remove()
    {
        _step = null;

        try
        {
            _harmony?.UnpatchAll(HarmonyId);
        }
        catch (Exception e)
        {
            Log.Warn($"could not remove the pre-render hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    // A no-op on every frame the GUI pass already claimed; the latch decides, not a DrawUI test here.
    private static void BeforeOnFrameCelestials(double deltaTime)
    {
        try
        {
            _step?.Invoke(deltaTime);
        }
        catch (Exception e)
        {
            // Stand down rather than throw again next frame; the frame postfix steps it one hook later.
            _step = null;

            if (_complained) return;
            _complained = true;
            Log.Error("the pre-render step failed; the frame postfix takes it from here", e);
        }
    }
}
