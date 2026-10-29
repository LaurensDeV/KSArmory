using System;
using System.Reflection;
using Brutal.VulkanApi;
using HarmonyLib;
using KSA;

namespace KSArmory;

/// <summary>
/// One of the places this mod patches the game: the instant a planet map may be rendered.
///
/// <para>An exporter needs a command buffer, the body below, a viewport and the frame's resource
/// index, and all four only exist together inside the engine's render. <c>Program</c> runs its own
/// exporters in one tagged region — <c>UpdatePlanetMaps</c>, between the barriers that make the
/// offscreen target readable — and <see cref="PlanetRenderer.ProcessExporter"/> is the public
/// method it calls there. A postfix on it renders this mod's map in the same breath as the game's,
/// which is the only way to be sure a render pass may begin -- and paints the paintball splats there,
/// after the opaque scene and before anything translucent (<see cref="CloudPass.RecordEarly"/>).</para>
///
/// <para>The target is <c>public</c>, so <see cref="PinTheSignature"/> keeps it in this assembly's
/// metadata and a KSA change to it is a build error rather than a map that silently stops
/// updating.</para>
///
/// <para><b>Nothing here may throw.</b> It runs inside the engine's command buffer; the render is
/// wrapped and latched by <see cref="PlanetMap"/>, so a fault costs the picture rather than the
/// frame. The engine's own call site is skipped while the nearby body is billboarded, and then the
/// picture holds its last frame.</para>
/// </summary>
internal static class PlanetMapHook
{
    private const string HarmonyId = "com.kesslersystems.ksarmory.planetmap";

    /// <summary>Whether this mod can render a planet map of its own.</summary>
    public static bool Installed { get; private set; }

    /// <summary>Why it cannot. Empty when it can, or when nothing has tried.</summary>
    public static string Trouble { get; private set; } = "";

    private static Harmony? _harmony;
    private static Func<PlanetMap?>? _map;

    public static void Install(Func<PlanetMap?> map)
    {
        _map = map;

        if (Installed) return;

        try
        {
            MethodInfo? target = AccessTools.Method(
                typeof(PlanetRenderer), nameof(PlanetRenderer.ProcessExporter),
                [typeof(CommandBuffer), typeof(Celestial), typeof(IViewport), typeof(int)]);

            if (target is null)
            {
                Trouble = "KSA has no PlanetRenderer.ProcessExporter to render alongside";
                Log.Warn($"no planet map of our own: {Trouble}");
                return;
            }

            MethodInfo postfix = typeof(PlanetMapHook).GetMethod(
                nameof(AfterProcessExporter), BindingFlags.NonPublic | BindingFlags.Static)!;

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, postfix: new HarmonyMethod(postfix));

            Installed = true;
            Trouble = "";
            Log.Info("own planet map renders with KSA's, via PlanetRenderer.ProcessExporter");
        }
        catch (Exception e)
        {
            Trouble = e.Message;
            Log.Warn($"could not hook the planet map render; there will be no map of our own "
                     + $"({e.Message})");
        }
    }

    public static void Remove()
    {
        _map = null;

        try
        {
            _harmony?.UnpatchAll(HarmonyId);
        }
        catch (Exception e)
        {
            Log.Warn($"could not remove the planet map hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    private static void AfterProcessExporter(CommandBuffer commandBuffer, Celestial nearbyCelestial,
                                             IViewport viewport, int frameIndex)
    {
        CloudPass.RecordEarly(commandBuffer, viewport, frameIndex);

        if (_map?.Invoke() is not { Ready: true } map) return;
        if (nearbyCelestial is null) return;

        map.Render(commandBuffer, nearbyCelestial, viewport, frameIndex);
    }

    // Never called: puts the patched member in this assembly's metadata, so a signature change is a build error.
    private static void PinTheSignature(PlanetRenderer planets, CommandBuffer commandBuffer,
                                        Celestial body, IViewport viewport, int frameIndex)
        => planets.ProcessExporter(commandBuffer, body, viewport, frameIndex);
}
