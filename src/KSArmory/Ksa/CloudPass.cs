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
/// This mod's own full-screen compute pass, run inside KSA's frame.
///
/// <para><b>No renderer was ported to get here and nothing is patched but one prefix.</b> KSA
/// compiles a <c>&lt;Shader&gt;</c> asset out of any mod's folder, <c>ComputePipelineWrapper</c>
/// builds the descriptor sets, and <c>Program.GetRenderer</c> and the clamp samplers are public
/// statics. What was missing was somewhere to dispatch from, and
/// <see cref="CloudPassHook"/> found it.</para>
///
/// <para><b>Where it runs is the whole design.</b> Just before <c>SunbloomRenderer.Render</c> the
/// engine has already put the scene colour into a storage layout and the depth into a sampled one,
/// for its own screen-space volumetric particles — so a mod dispatching there inherits those
/// barriers and needs none of its own. It is also before bloom and before the tonemap composite,
/// which is what lets anything written here bloom and be graded like the rest of the scene.</para>
///
/// <para>Rebuilt when the target changes size, because the descriptor sets name the images.</para>
/// </summary>
internal static class CloudPass
{
    private const string ShaderId = "KSArmoryCloudCompute";

    // The compute shader's workgroup, which has to match KSArmoryCloud.comp's local_size.
    private const int Group = 8;

    private static ComputePipelineWrapper? _pipeline;
    private static int _width;
    private static int _height;
    private static bool _warned;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Push
    {
        public float4x4 InvViewProj;
        public float4 SizeStrength;
        public float4 CentreRadius;
        public float4 UpAge;
        public float4 Shape;       // cap centre, cap radius, cap tube, stem radius
    }

    /// <summary>Whether the pass built and is dispatching.</summary>
    public static bool Available => _pipeline is not null;

    /// <summary>Drops the pipeline, so the next frame builds it again.</summary>
    public static void Release()
    {
        _pipeline = null;
        _width = 0;
        _height = 0;
    }

    /// <summary>
    /// Records the pass into the frame KSA is already building.
    ///
    /// <para>Nothing here may throw: it runs inside the engine's render loop, where an exception is
    /// the game rather than a log line.</para>
    /// </summary>
    public static void Record(CommandBuffer commandBuffer, IViewport viewport, int frameIndex, float tint)
    {
        try
        {
            if (viewport.OffscreenTarget is not { } target) return;
            if (target.ColorImage is not { } colour || target.DepthImage is not { } depth) return;

            int width = (int)target.Extent.Width;
            int height = (int)target.Extent.Height;
            if (width <= 0 || height <= 0) return;

            if (_pipeline is null || width != _width || height != _height)
            {
                if (!Build(colour, depth)) return;

                _width = width;
                _height = height;
            }

            if (Program.GetRenderCamera() is not { } camera) return;
            if (!NuclearClouds.TryNewest(out double3 burstEcl, out double3 up,
                                         out double radius, out double age,
                                         out MushroomCloud.Shape shape)) return;

            // Differenced against the camera in DOUBLE and only then narrowed. The world is a solar
            // system: a float metre cannot hold an ecliptic position, and Ego is a pure translation
            // of Ecl, so the camera sitting at the origin in the shader is exact rather than close.
            double3 centre = burstEcl - camera.PositionEcl;
            if (!Vec.IsFinite(centre)) return;

            Push push = new()
            {
                InvViewProj = camera.VPInv.viewProjection,
                SizeStrength = new float4(width, height, 0f, tint),
                CentreRadius = new float4((float)centre.X, (float)centre.Y, (float)centre.Z,
                                          (float)radius),
                UpAge = new float4((float)up.X, (float)up.Y, (float)up.Z, (float)age),

                // The same shape the pens walk, so the two drawings cannot disagree about where the
                // cloud is -- and so every dimension stays Glasstone's rather than being invented
                // again in GLSL.
                Shape = new float4((float)shape.CapCentre, (float)shape.CapRadius,
                                   (float)shape.CapTube, (float)shape.StemRadius),
            };

            _pipeline!.BindPipeline(commandBuffer, frameIndex, default, default, push);
            commandBuffer.Dispatch((width + Group - 1) / Group,
                                   (height + Group - 1) / Group, 1);
        }
        catch (Exception e)
        {
            Warn($"the pass threw and is standing down: {e.Message}");
            Release();
        }
    }

    private static bool Build(IRenderImage colour, IRenderImage depth)
    {
        if (!ModLibrary.TryGet<ShaderReference>(ShaderId, out var shader) || shader is null)
        {
            Warn($"no shader '{ShaderId}'; the pass will not draw");
            return false;
        }

        Renderer renderer = Program.GetRenderer();

        IRenderImage[] storageTargets = [colour];
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

        _pipeline = new ComputePipelineWrapper(
            storageTargets, depthTargets, default, default, shader,
            default, ranges, renderer.MaxFramesInFlight, renderer,
            "KSArmory.CloudPass", Program.PointClampedSampler, Program.LinearClampedSampler);

        Log.Info($"cloud pass: built against {ShaderId}");
        return true;
    }

    private static void Warn(string what)
    {
        if (_warned) return;

        _warned = true;
        Log.Warn($"cloud pass: {what}");
    }
}
