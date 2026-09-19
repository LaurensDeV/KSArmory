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
/// <para>What that costs is one frame: a subpart transform written after the render is drawn on the
/// next one, so every round in the air is a step behind the world it is drawn against. Pressing F2
/// therefore moves a round by one step of its own travel — 4 m at 250 m/s — and then jitters it by
/// the difference between a long frame and a short one, because the display's pacing alternates.</para>
///
/// <para><c>Program.OnFrameCelestials</c> is the last phase before <c>OnPreRender</c> that is not
/// behind that guard, and this is a <b>prefix</b> on it. That is not a detail: <c>OnFrameCelestials</c>
/// resolves the camera-nearby body and updates the planet's shader data for the frame, so a step
/// taken <em>after</em> it prepares the planet against a camera the mod is about to move — which
/// renders the surface as open water. With the UI shown the mod steps at <c>OnDrawUiViewports</c>,
/// which is ahead of it; a prefix is what puts a hidden-UI frame on the same side. Making the two
/// paths identical was the whole point, and a postfix quietly was not.</para>
///
/// <para>It is the private half of the frame loop rather than declared API,
/// which is a rule <see cref="AttitudeHook"/> takes some care not to bend — but the two are not
/// alike in what losing them costs. <b>The fallback here is the behaviour without it.</b> If KSA ever
/// renames the method the patch does not apply, this says so once, and
/// <c>[StarMapAfterOnFrame]</c> goes on running the step exactly as it does today.
/// <c>docs/KSA-FRAME-ORDER.md</c> §7 has the guard and the frame table.</para>
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
            // Private, so by name: there is no way to write this that a compiler would check, which
            // is exactly why the failure has to degrade rather than break.
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

    // A no-op on every frame the GUI pass already claimed, which is every frame with a UI. The
    // latch is what makes that true rather than a condition here: asking whether the UI drew would
    // be a second answer to a question one place already owns.
    private static void BeforeOnFrameCelestials(double deltaTime)
    {
        try
        {
            _step?.Invoke(deltaTime);
        }
        catch (Exception e)
        {
            // Stand down rather than throw again next frame from inside the engine's loop. The
            // frame postfix has the mod's own fault handling, and it is one hook later -- so
            // giving up here is the same degradation a refused patch gives, arrived at differently.
            _step = null;

            if (_complained) return;
            _complained = true;
            Log.Error("the pre-render step failed; the frame postfix takes it from here", e);
        }
    }
}
