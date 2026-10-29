using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.VulkanApi;
using KSA;
using KSA.Rendering.Planet;

namespace KSArmory;

/// <summary>
/// This mod's own planet map: an equirectangular picture of the body below, rendered by KSA's own
/// exporter into a texture nothing else owns.
///
/// <para><b>Why not KSA's.</b> Its ground-track window renders one of these already, but it comes
/// with that window — its chrome, its menu, its flags, and only for the craft being flown
/// (<c>Program</c> drives it off <c>ControlledVehicle</c>). A targeting map wants its own
/// controls around it, so it wants its own picture.</para>
///
/// <para><b>What makes it cheap.</b> <see cref="PlanetMapExporter"/>'s constructor is public and
/// so are <c>Program.GetRenderer</c> and <c>GetPlanetRenderer</c>, so building one is ordinary
/// code. The only thing a mod cannot reach on its own is a command buffer at an instant where a
/// render pass may start — and <see cref="PlanetMapHook"/> takes that from the engine, which
/// hands one over to drive its own exporters at the same moment.</para>
///
/// <para>It is a second planet render per frame while the window is open, which is why it is
/// built on demand and disposed the moment nothing is looking at it.</para>
/// </summary>
internal sealed class PlanetMap : IDisposable
{
    private PlanetMapExporter? _exporter;
    private bool _broken;
    private bool _drew;

    /// <summary>Whether there is a picture to draw.</summary>
    public bool Ready => _exporter is { Enabled: true };

    /// <summary>Why there is not. Empty when there is, or when nothing has asked.</summary>
    public string Trouble { get; private set; } = "";

    /// <summary>
    /// Build the exporter, once. A failure is latched: this is Vulkan resource creation, and
    /// retrying it every frame would be a new way to fail every frame.
    /// </summary>
    public bool TryOpen()
    {
        if (_exporter is not null) return true;
        if (_broken) return false;

        try
        {
            // Exactly the sequence KSA's own ground-track window uses. Not SetViewportResolution: it
            // raises a rebuild flag only the exporter's own OnDrawUI acts on, and left pending it
            // crashes the game on the first frame that draws.
            PlanetMapExporter made = new(Program.GetRenderer(), Program.GetPlanetRenderer());
            made.ToggleEnabled();

            if (!made.Enabled)
            {
                made.Dispose();
                Fail("KSA's map exporter would not switch on");
                return false;
            }

            _exporter = made;
            Trouble = "";
            Log.Info("planet map: own exporter built");
            return true;
        }
        catch (Exception e)
        {
            Fail(e.Message);
            return false;
        }
    }

    /// <summary>The part of the body the picture covers, in UV.</summary>
    public void SetCrop(float2 min, float2 max)
    {
        if (_exporter is not { } exporter) return;

        exporter.SetViewportMin(min);
        exporter.SetViewportMax(max);
    }

    /// <summary>Flat lighting or the real terminator. Flat is the targeting view.</summary>
    public void SetLit(bool lit) => _exporter?.SetMapLightingEnabled(lit);

    /// <summary>
    /// Render it, from the one instant the engine renders its own. Never called from anywhere
    /// else: outside that command buffer a render pass may not begin.
    /// </summary>
    public void Render(CommandBuffer commandBuffer, Celestial body, IViewport viewport,
                       int frameIndex)
    {
        if (_exporter is not { Enabled: true } exporter) return;

        try
        {
            Camera camera = viewport.GetCamera();
            PlanetRenderer planets = Program.GetPlanetRenderer();

            PlanetViewportData.PushConstData data = new()
            {
                MeshMatrix = float4x4.Identity,
                TextureMatrix = planets.GetRenderTextureMatrix(camera),
            };

            exporter.OnFrame(body, data);
            exporter.Render(commandBuffer, body, viewport, frameIndex);
        }
        catch (Exception e)
        {
            // Inside the engine's command buffer, so this may not escape; latched so it does not repeat.
            Fail(e.Message);
            Close();
        }
    }

    /// <summary>The picture, for <c>ImGui.Image</c>.</summary>
    public bool TryTexture(out ImTextureRef texture)
    {
        texture = default;

        if (_exporter is not { Enabled: true } exporter) return false;

        ImTextureRef[] ids = exporter.PlanetMapImGuiIds;
        if (ids is null || ids.Length == 0) return false;

        texture = ids[0];

        if (!_drew)
        {
            _drew = true;
            Log.Info($"planet map: drawing our own picture ({ids.Length} layer(s))");
        }

        return true;
    }

    /// <summary>Give the textures back. A second planet render is not worth keeping unseen.</summary>
    public void Close()
    {
        if (_exporter is not { } exporter) return;

        _exporter = null;
        _drew = false;

        try
        {
            exporter.Dispose();
        }
        catch (Exception e)
        {
            Log.Warn($"planet map: the exporter would not dispose ({e.Message})");
        }
    }

    public void Dispose() => Close();

    private void Fail(string why)
    {
        _broken = true;
        Trouble = why;
        Log.Warn($"planet map: no picture of our own ({why})");
    }
}
