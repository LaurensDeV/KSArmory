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
    /// <para>Needs no focus, unlike <c>tools/screenshot.sh</c>; <c>ui</c> and <c>hud</c> are opt-in.
    /// Files land in <c>Documents/exports/screenshots/ksa_&lt;stamp&gt;_&lt;w&gt;x&lt;h&gt;.png</c>.</para>
    ///
    /// <para><c>Program._screenshotCapture</c> is private, reflected once as the game's own
    /// <c>screenshot</c> command reaches it; a rename turns this off.</para>
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

            // A request during a capture is dropped by the engine with its own warning.
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

    /// <summary>
    /// KSA's own weather clouds as they were drawn this frame: their light with transmittance in
    /// alpha, and how far each pixel's cloud is from the camera. False when the player has clouds
    /// switched off, which is the one case with no renderer at all.
    ///
    /// <para>Needed because the clouds write no depth. Reached through the private
    /// <c>Program._planetTransparenciesRenderer</c>, reflected and verified; a rename draws the burst
    /// in front of the clouds. Asked every frame: the renderer and its images are rebuilt and swapped.</para>
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

    // The accumulated pair, which holds still where the jittered low-resolution pair crawls. The
    // engine flips the flag after writing, so this frame's is the one it no longer names. The getters
    // throw while the main view has no cloud resources, and the low-resolution pair then stands in.
    private static bool TrySettledWeather(KSA.Atmosphere.Rendering.CloudRenderer clouds,
                                          out KSA.Rendering.RenderImage? colour,
                                          out KSA.Rendering.RenderImage? distance)
    {
        try
        {
            bool flip = clouds.IsUpscaledHistoryInFlip();

            colour = flip ? clouds.GetUpscaledCloudColorFlipTarget() : clouds.GetUpscaledCloudColorFlopTarget();
            distance = flip ? clouds.GetUpscaledDistanceFlipTarget() : clouds.GetUpscaledDistanceFlopTarget();
        }
        catch (InvalidOperationException)
        {
            colour = null;
            distance = null;
        }

        return colour is not null && distance is not null;
    }

    /// <summary>
    /// The buffers KSA shades its ground by its weather from, for one body: the layers' fixed data and
    /// their per-frame placement, and the object holding them, which changes only when the renderer
    /// rebuilds its planets.
    ///
    /// <para>The buffers, not KSA's descriptor set, which is declared for fragment shaders and reads
    /// NaN bound to compute. Both private, reflected; KSA's no-weather stand-in carries no layers.</para>
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
