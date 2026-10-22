using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using KSA.Rendering.Water.Data;

namespace KSArmory;

internal static partial class KsaWorld
{
    private static ScreenshotCapture? _shots;

    private static bool _lookedForShots;

    private static bool _warnedAboutShots;

    /// <summary>
    /// Asks the game to save a screenshot of its own framebuffer.
    ///
    /// <para>The alternative is <c>tools/screenshot.sh</c>, which grabs the whole primary display
    /// and so refuses unless the game is in front — which it is not, during an unattended run on a
    /// machine somebody is using. This needs no focus, cannot photograph somebody's desktop, and
    /// writes a clean frame: <c>ui</c> and <c>hud</c> are opt-in, so the default is the scene with
    /// no panel over it, which is what anybody judging an effect wants.</para>
    ///
    /// <para>Files land in <c>Documents/exports/screenshots/ksa_&lt;stamp&gt;_&lt;w&gt;x&lt;h&gt;.png</c>.</para>
    ///
    /// <para><b>One private field is the whole obstacle</b>, the same shape as
    /// <see cref="PlumeSmoke"/>: <c>ScreenshotCapture.Request</c> is public and
    /// <c>Program._screenshotCapture</c> is not exposed. The game's own <c>screenshot</c> terminal
    /// command reaches it the same way. Reflected once, and a KSA rename turns this off rather than
    /// breaking anything — nothing but a picture is lost.</para>
    /// </summary>
    public static bool TryRequestScreenshot(int scale = 1, string flags = "")
    {
        try
        {
            if (!_lookedForShots)
            {
                _lookedForShots = true;
                _shots = typeof(Program)
                         .GetField("_screenshotCapture", BindingFlags.NonPublic | BindingFlags.Instance)
                         ?.GetValue(Program.Instance) as ScreenshotCapture;

                if (_shots is null && !_warnedAboutShots)
                {
                    _warnedAboutShots = true;
                    Log.Warn("screenshots: Program._screenshotCapture did not resolve; "
                             + "scripted captures will write nothing");
                }
            }

            if (_shots is null) return false;

            // A capture already running is dropped by the engine with its own warning, so asking
            // again while one is in flight costs nothing and loses only that frame.
            _shots.Request(scale, flags);
            return true;
        }
        catch (Exception e)
        {
            if (!_warnedAboutShots)
            {
                _warnedAboutShots = true;
                Log.Warn($"screenshots: could not ask for one: {e.Message}");
            }
            return false;
        }
    }

    private static bool _lookedForWeather;

    private static object? _transparencies;

    private static bool _warnedAboutWeather;

    // The renderer's accumulated full-resolution result, which it ping-pongs between two images
    // and a flag. Private, so reflected; missing, and the jittered low-resolution pair stands in.
    private static FieldInfo?[]? _settled;

    private static FieldInfo? _writesFlipNext;

    private static readonly string[] SettledNames =
    [
        "_upscaledCloudColorFlipTarget", "_upscaledCloudColorFlopTarget",
        "_upscaledDistanceFlipTarget", "_upscaledDistanceFlopTarget",
    ];

    /// <summary>
    /// KSA's own weather clouds as they were drawn this frame: their light with transmittance in
    /// alpha, and how far each pixel's cloud is from the camera. False when the player has clouds
    /// switched off, which is the one case with no renderer at all.
    ///
    /// <para><b>Needed because the clouds write no depth.</b> They are raymarched into images of
    /// their own and composited into the scene colour, so the depth buffer under a cloud still says
    /// "ground" — and anything drawn afterwards off that depth is drawn in front of every cloud,
    /// whatever is actually between it and the camera.</para>
    ///
    /// <para>One private field away: <c>Program._planetTransparenciesRenderer</c> is the only owner
    /// of the renderer and <c>GetCloudRenderer()</c> is public on it. Reflected once and verified,
    /// and a KSA rename turns this off rather than breaking anything — the burst is then drawn in
    /// front of the clouds. The accumulated images it prefers are
    /// four more private fields and a flag, with the public low-resolution pair behind them.</para>
    ///
    /// <para>Asked every frame rather than held: the renderer is rebuilt when the settings change
    /// and both images when the window is resized, and the accumulated pair it prefers swaps images
    /// every frame.</para>
    /// </summary>
    public static bool TryWeatherClouds(out KSA.Rendering.RenderImage? colour,
                                        out KSA.Rendering.RenderImage? distance)
    {
        colour = null;
        distance = null;

        try
        {
            if (!_lookedForWeather)
            {
                _lookedForWeather = true;
                _transparencies = typeof(Program)
                                  .GetField("_planetTransparenciesRenderer",
                                            BindingFlags.NonPublic | BindingFlags.Instance)
                                  ?.GetValue(Program.Instance);

                if (_transparencies is not PlanetTransparenciesRenderer && !_warnedAboutWeather)
                {
                    _warnedAboutWeather = true;
                    Log.Warn("weather clouds: Program._planetTransparenciesRenderer did not resolve; "
                             + "a burst will be drawn in front of them");
                }
            }

            if (_transparencies is not PlanetTransparenciesRenderer owner) return false;
            if (owner.GetCloudRenderer() is not { } clouds) return false;

            if (TrySettledWeather(clouds, out colour, out distance)) return true;

            colour = clouds.GetLowResolutionCloudColorTarget();
            distance = clouds.GetLowResolutionCloudDistanceTarget();

            return colour is not null && distance is not null;
        }
        catch (Exception e)
        {
            if (!_warnedAboutWeather)
            {
                _warnedAboutWeather = true;
                Log.Warn($"weather clouds: could not read them: {e.Message}");
            }

            return false;
        }
    }

    // The pair the renderer wrote this frame. Its low-resolution images are sampled at a different
    // sub-pixel every frame and accumulated into these, so an edge cut from the low-resolution pair
    // crawls while the world is paused, and one cut from these holds still. PerformUpscaling flips
    // the flag after writing, so the image just written is the one the flag no longer names.
    private static bool TrySettledWeather(object clouds, out KSA.Rendering.RenderImage? colour,
                                          out KSA.Rendering.RenderImage? distance)
    {
        colour = null;
        distance = null;

        if (_settled is null)
        {
            Type type = clouds.GetType();
            const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

            _settled = [.. SettledNames.Select(n => type.GetField(n, Private))];
            _writesFlipNext = type.GetField("writeHistoryToFlip", Private);

            if (_settled.Any(f => f?.FieldType != typeof(KSA.Rendering.RenderImage))
                || _writesFlipNext?.FieldType != typeof(bool))
            {
                Log.Warn("weather clouds: the renderer's accumulated images did not resolve; "
                         + "reading its jittered low-resolution ones, whose edges crawl");
                _settled = [];
            }
        }

        if (_settled.Length != 4 || _writesFlipNext is null) return false;

        bool flipIsNext = (bool)_writesFlipNext.GetValue(clouds)!;
        int written = flipIsNext ? 1 : 0;

        colour = _settled[written]!.GetValue(clouds) as KSA.Rendering.RenderImage;
        distance = _settled[2 + written]!.GetValue(clouds) as KSA.Rendering.RenderImage;

        return colour is not null && distance is not null;
    }

    /// <summary>
    /// The buffers KSA shades its ground by its weather from, for one body: the layers' fixed data and
    /// their per-frame placement, and the object holding them, which changes only when the renderer
    /// rebuilds its planets.
    ///
    /// <para><b>The buffers, not KSA's descriptor set.</b> That set's layout is declared for fragment
    /// shaders alone, and bound to a compute pass it reads garbage -- every float a NaN, flown. So the
    /// cloud pass takes the two buffers into its own set, which the pipeline builder declares for
    /// compute. Both are private, hence the reflection; the stand-in KSA uses for a body with no
    /// weather, or with clouds off, stands in here too, and carries no layers.</para>
    /// </summary>
    public static bool TryWeatherShadowBuffers(Celestial body, out Brutal.VulkanApi.VkBuffer staticBuffer,
                                               out Brutal.VulkanApi.VkBuffer frameBuffer, out object? source)
    {
        staticBuffer = default;
        frameBuffer = default;
        source = null;

        try
        {
            var renderer = Program.GetCloudShadowsRenderer();
            const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

            _shadowPlanets ??= renderer.GetType().GetField("PlanetToCloudShadowData", Private);
            _shadowNone ??= renderer.GetType().GetField("_noShadowData", Private);

            object? data = null;
            if (GameSettings.ShowClouds()
                && _shadowPlanets?.GetValue(renderer) is System.Collections.IDictionary planets
                && planets.Contains(body.Hash))
            {
                data = planets[body.Hash];
            }

            data ??= _shadowNone?.GetValue(renderer);
            if (data is null) return Unshadowed("the renderer's shadow data did not resolve");

            Type type = data.GetType();
            _shadowStatic ??= type.GetField("_staticShadowDataBuffer", Private);
            _shadowFrame ??= type.GetField("_dynamicShadowDataBuffer", Private);

            if (_shadowStatic?.GetValue(data) is not Brutal.VulkanApi.Abstractions.BufferEx fixedPart
                || _shadowFrame?.GetValue(data) is not Brutal.VulkanApi.Abstractions.BufferEx movingPart)
            {
                return Unshadowed("its buffers did not resolve");
            }

            staticBuffer = fixedPart.VkBuffer;
            frameBuffer = movingPart.VkBuffer;
            source = data;
            return true;
        }
        catch (Exception e)
        {
            return Unshadowed(e.Message);
        }
    }

    private static FieldInfo? _shadowPlanets;

    private static FieldInfo? _shadowNone;

    private static FieldInfo? _shadowStatic;

    private static FieldInfo? _shadowFrame;

    private static bool _warnedAboutShadows;

    private static bool Unshadowed(string why)
    {
        if (!_warnedAboutShadows)
        {
            _warnedAboutShadows = true;
            Log.Warn($"weather shadows: {why}; the burst is lit as though under a clear sky");
        }

        return false;
    }
}
