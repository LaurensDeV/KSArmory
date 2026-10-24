using System;
using System.Reflection;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace KSArmory;

/// <summary>
/// A patch that lets the mod draw on KSA's ground-track map rather than render a planet of its own.
///
/// <para>The target is <c>public override</c>, so <see cref="PinTheSignature"/> makes a KSA change
/// to it a build error. The prefix only measures: the image's rect is locals in
/// <c>DrawContent</c>, but the content region is fixed at <c>Begin</c> and the map drawn at the
/// cursor, so reading both on the way in gives that rect.</para>
///
/// <para>Nothing here may throw: it runs in the engine's GUI pass, so a fault is latched and costs
/// only the marks.</para>
/// </summary>
internal static class GroundTrackHook
{
    private const string HarmonyId = "com.kesslersystems.ksarmory.groundtrack";

    /// <summary>Whether the mod can draw on the ground-track map.</summary>
    public static bool Installed { get; private set; }

    /// <summary>Why it cannot. Empty when it can, or when nothing has tried.</summary>
    public static string Trouble { get; private set; } = "";

    private static Harmony? _harmony;
    private static Action<GroundTrackWindow, float2, float2, float2, float2>? _draw;
    private static bool _complained;

    private static float2 _at;
    private static float2 _size;
    private static bool _measured;

    // KSA's own crop, captured as it computes it. The window hands this to its map exporter and
    // draws its grid from it, so taking it rather than deriving it is what makes a mark and the
    // picture under it incapable of disagreeing.
    private static GenericImGuiViewport? _cropOwner;
    private static float2 _cropMin;
    private static float2 _cropMax;
    private static bool _warnedCrop;
    private const int ConfirmUpTo = 8;
    private static int _confirmed;
    private static float _lastConfirmed = float.NaN;

    public static void Install(Action<GroundTrackWindow, float2, float2, float2, float2> draw)
    {
        _draw = draw;

        if (Installed) return;

        try
        {
            MethodInfo? target = AccessTools.Method(typeof(GroundTrackWindow),
                                                    nameof(GroundTrackWindow.DrawContent),
                                                    [typeof(IViewport)]);

            if (target is null)
            {
                Trouble = "KSA has no GroundTrackWindow.DrawContent(IViewport) to draw on";
                Log.Warn($"no marks on the ground-track map: {Trouble}");
                return;
            }

            MethodInfo? crop = AccessTools.Method(
                typeof(GenericImGuiViewport),
                nameof(GenericImGuiViewport.CreateDraggableZoomableViewport));

            if (crop is null)
            {
                Trouble = "KSA has no GenericImGuiViewport.CreateDraggableZoomableViewport to read "
                          + "the map's crop from";
                Log.Warn($"no marks on the ground-track map: {Trouble}");
                return;
            }

            MethodInfo prefix = typeof(GroundTrackHook).GetMethod(
                nameof(BeforeDrawContent), BindingFlags.NonPublic | BindingFlags.Static)!;
            MethodInfo postfix = typeof(GroundTrackHook).GetMethod(
                nameof(AfterDrawContent), BindingFlags.NonPublic | BindingFlags.Static)!;
            MethodInfo cropPostfix = typeof(GroundTrackHook).GetMethod(
                nameof(AfterCrop), BindingFlags.NonPublic | BindingFlags.Static)!;

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, prefix: new HarmonyMethod(prefix),
                                   postfix: new HarmonyMethod(postfix));
            _harmony.Patch(crop, postfix: new HarmonyMethod(cropPostfix));

            Installed = true;
            Trouble = "";
            Log.Info("drawing on KSA's ground-track map, via GroundTrackWindow.DrawContent");
        }
        catch (Exception e)
        {
            Trouble = e.Message;
            Log.Warn($"could not hook the ground-track map; it draws without this mod's "
                     + $"marks ({e.Message})");
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
            Log.Warn($"could not remove the ground-track hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
        _measured = false;
        _cropOwner = null;
    }

    // The rect the map is about to occupy. Taken here because DrawContent keeps it in locals.
    private static void BeforeDrawContent()
    {
        try
        {
            _size = ImGui.GetContentRegionAvail();
            _at = ImGui.GetCursorScreenPos();
            _measured = _size.X > 0f && _size.Y > 0f;
        }
        catch
        {
            _measured = false;
        }
    }

    private static void AfterDrawContent(GroundTrackWindow __instance)
    {
        if (!_measured) return;
        _measured = false;

        // DrawContent returns early on a hidden window and on a craft with no body under it,
        // having drawn no map -- so the rect measured on the way in describes nothing.
        if (!__instance.IsShown) return;

        if (_draw is not { } draw) return;

        // Windows draw one at a time, so the crop recorded last belongs to this one -- checked
        // rather than assumed, because the same viewport type is used elsewhere in the game.
        if (!ReferenceEquals(_cropOwner, __instance.Viewport)) return;

        try
        {
            draw(__instance, _at, _size, _cropMin, _cropMax);
        }
        catch (Exception e)
        {
            if (_complained) return;

            _complained = true;
            Log.Error("drawing on the ground-track map failed; it draws without the marks "
                      + "from here", e);
        }
    }

    /// <summary>
    /// Put a place in the middle of the map at a zoom. Taken on the next frame that draws, because
    /// the pan is in pixels of the image and only the draw knows how big that is.
    /// </summary>
    public static void CentreOn(double latitudeDeg, double longitudeDeg, double zoom)
    {
        _centreOn = MapViewport.ToUv(longitudeDeg, latitudeDeg);
        _centreZoom = (float)Math.Clamp(zoom, 1.0, 5000.0);
        _centreWanted = true;
    }

    private static float2 _centreOn;
    private static float _centreZoom = 1f;
    private static bool _centreWanted;

    private static void AfterCrop(GenericImGuiViewport __instance,
                                  float2 imGuiArea, ref float2 viewportMin, ref float2 viewportMax)
    {
        _cropOwner = __instance;
        _cropMin = viewportMin;
        _cropMax = viewportMax;


        Check(__instance, imGuiArea, viewportMin, viewportMax);

        // After Check: written before it, the comparison reads this frame's pan against last
        // frame's crop and warns about a disagreement the mod caused.
        if (!_centreWanted) return;

        _centreWanted = false;

        // The crop is one over the zoom wide, so its corner is half of that below the middle.
        float half = 0.5f / _centreZoom;
        KsaWorld.SetMapView(__instance, _centreZoom,
                            new float2(-(_centreOn.X - half) * imGuiArea.X * _centreZoom,
                                       -(_centreOn.Y - half) * imGuiArea.Y * _centreZoom));
    }

    private static void Check(GenericImGuiViewport __instance, float2 imGuiArea,
                              float2 viewportMin, float2 viewportMax)
    {
        if (_warnedCrop) return;

        // The same crop worked out from the pan and zoom, which are public. Checked against the
        // engine's own answer once a session rather than trusted: this is the arithmetic a KSA
        // change would silently move, and a mark placed with a stale crop sits somewhere plausible.
        MapViewport.FromPanZoom(imGuiArea, __instance.GetPan(), __instance.GetZoom(),
                                out float2 min, out float2 max);

        if (MapViewport.Same(min, max, viewportMin, viewportMax))
        {
            // Once per distinct zoom, capped: at zoom 1 the crop is 0..1 whatever the arithmetic
            // does, so a pass at one zoom proves nothing.
            float zoom = __instance.GetZoom();

            if (_confirmed >= ConfirmUpTo || Math.Abs(zoom - _lastConfirmed) < 0.01f) return;

            _confirmed++;
            _lastConfirmed = zoom;
            Log.Debug($"ground-track map: the crop matches MapViewport at zoom {zoom:F2} "
                      + $"({viewportMin}..{viewportMax})");
            return;
        }

        _warnedCrop = true;
        Log.Warn($"the ground-track map's crop no longer matches MapViewport: KSA says "
                 + $"{viewportMin}..{viewportMax}, this mod works out {min}..{max}. The marks use "
                 + "KSA's, so they stay right; MapViewport's fallback does not.");
    }

    // Never called. They exist so the patched members are in this assembly's metadata, which is
    // what makes a KSA signature change a build error. See the class remarks.
    private static void PinTheSignature(GroundTrackWindow window, IViewport viewport,
                                        GenericImGuiViewport crop, float2 area)
    {
        window.DrawContent(viewport);
        crop.CreateDraggableZoomableViewport(area, out _, out _);
    }
}
