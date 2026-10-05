using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using KSA.Rendering;
using RenderCore;

namespace KSArmory;

/// <summary>
/// What a director's sensor does to its camera window's picture: <c>Shaders/KSArmorySensor.comp</c>,
/// recorded from <see cref="CloudPassHook"/> before bloom, so the grey is bloomed and tonemapped
/// with the scene. Only for a window a head is driving in a mode other than colour.
///
/// <para><b>Nothing here may throw.</b> It runs inside the engine's render loop.</para>
/// </summary>
internal static class SensorPass
{
    private const string ShaderId = "KSArmorySensorCompute";
    private const int Group = 8;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Push
    {
        public float4 ModeTime;
    }

    private sealed class View
    {
        public required IRenderImage Target;
        public required int Width, Height;
        public required RenderImage Mean;
        public ComputePipelineWrapper? Pipeline;
    }

    // Keyed on the viewport object, because the UI names a window by its index and the renderer
    // hands over the viewport itself.
    private static readonly Dictionary<IViewport, SensorMode> _wanted = [];
    private static readonly Dictionary<int, View> _views = [];
    private static bool _warned;

    /// <summary>Forgets last frame's modes; each driven window states its own again.</summary>
    public static void BeginFrame() => _wanted.Clear();

    /// <summary>Asks for one camera window to be drawn in a sensor mode this frame.</summary>
    public static void Want(int viewportIndex, SensorMode mode)
    {
        if (mode == SensorMode.Colour) return;
        if (KsaWorld.TryGameViewport(viewportIndex, out IViewport viewport)) _wanted[viewport] = mode;
    }

    public static void Release() => _views.Clear();

    public static void Record(CommandBuffer commandBuffer, IViewport viewport)
    {
        try
        {
            if (!_wanted.TryGetValue(viewport, out SensorMode mode)) return;
            if (viewport.OffscreenTarget is not { } target) return;
            if (target.ColorImage is not { } colour || target.DepthImage is not { } depth) return;

            int width = (int)target.Extent.Width;
            int height = (int)target.Extent.Height;
            if (width <= 0 || height <= 0) return;

            View? view = ViewFor(viewport, colour, depth, width, height);
            if (view?.Pipeline is null) return;

            Span<VkImageMemoryBarrier2> one = stackalloc VkImageMemoryBarrier2[1];
            BarrierBatch toStorage = new(one);
            toStorage.Add(view.Mean, ImageBarrierInfo.Presets.StorageReadWriteC);
            toStorage.SubmitAndFlush(commandBuffer);

            float seconds = (float)(Environment.TickCount64 % 100_000L) * 0.001f;
            Push push = new() { ModeTime = new float4((float)mode, seconds, 0f, 0f) };

            Hazard(commandBuffer);
            view.Pipeline.BindPipeline(commandBuffer, viewport.ShaderSlot, default, default, push);
            commandBuffer.Dispatch(1, 1, 1);

            Hazard(commandBuffer);
            push.ModeTime.Z = 1f;
            view.Pipeline.BindPipeline(commandBuffer, viewport.ShaderSlot, default, default, push);
            commandBuffer.Dispatch((width + Group - 1) / Group, (height + Group - 1) / Group, 1);

            Hazard(commandBuffer);
        }
        catch (Exception e)
        {
            Warn($"stood down ({e.GetType().Name}: {e.Message})");
            _wanted.Clear();
        }
    }

    // Rebuilt on a resize. The size is compared as well as the image, because a resize can rebuild
    // the image inside the same object, and a set still naming the old one loses the device.
    private static View? ViewFor(IViewport viewport, IRenderImage colour, IRenderImage depth,
                                 int width, int height)
    {
        if (_views.TryGetValue(viewport.ShaderSlot, out View? view) && ReferenceEquals(view.Target, colour)
            && view.Width == width && view.Height == height)
        {
            return view;
        }

        if (!ModLibrary.TryGet<ShaderReference>(ShaderId, out var shader) || shader is null)
        {
            Warn($"no shader '{ShaderId}'; camera windows stay in colour");
            return null;
        }

        Renderer renderer = Program.GetRenderer();
        RenderImage mean = RenderImage.CreateColorStorage(renderer, "KSArmory Sensor Mean",
                                                          new VkExtent2D(1, 1), VkFormat.R32SFloat);

        IRenderImage[] storageTargets = [colour, mean];
        IRenderImage[] depthTargets = [depth];
        VkPushConstantRange[] ranges =
        [
            new VkPushConstantRange
            {
                Offset = (ByteSize32)0,
                Size = (ByteSize32)Marshal.SizeOf<Push>(),
                StageFlags = VkShaderStageFlags.ComputeBit,
            },
        ];

        view = new View
        {
            Target = colour,
            Width = width,
            Height = height,
            Mean = mean,
            Pipeline = new ComputePipelineWrapper(
                storageTargets, depthTargets, default, default, shader,
                default, ranges, renderer.MaxFramesInFlight, renderer,
                "KSArmory.Sensor", Program.PointClampedSampler, Program.LinearClampedSampler),
        };

        _views[viewport.ShaderSlot] = view;
        return view;
    }

    private static void Hazard(CommandBuffer commandBuffer)
    {
        Span<VkMemoryBarrier2> one = stackalloc VkMemoryBarrier2[1];
        BarrierBatch batch = new(one, default, default);

        VkMemoryBarrier2 barrier = new()
        {
            SrcStageMask = VkPipelineStageFlags2.ComputeShaderBit,
            SrcAccessMask = VkAccessFlags2.ShaderWriteBit,
            DstStageMask = VkPipelineStageFlags2.AllCommandsBit,
            DstAccessMask = VkAccessFlags2.ShaderReadBit | VkAccessFlags2.ShaderWriteBit,
        };

        batch.Add(in barrier);
        batch.SubmitAndFlush(commandBuffer);
    }

    private static void Warn(string what)
    {
        if (_warned) return;

        _warned = true;
        Log.Warn($"sensor pass: {what}");
    }
}
