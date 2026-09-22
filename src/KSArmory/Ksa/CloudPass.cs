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

    // KSA's own GPU profiler rather than a query pool of this mod's. A compute dispatch is
    // asynchronous, so timing it from C# measures the recording and not the work; the engine
    // already writes timestamps around every region tagged this way, and its profiler window then
    // lists this pass beside the passes it has to be afforded against.
    private static readonly ProfilerTag GpuTag = new("KSArmory Cloud"u8);

    private static ComputePipelineWrapper? _pipeline;
    private static int _width;
    private static int _height;
    private static bool _warned;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Push
    {
        public float4x4 InvViewProj;
        public float4 AgeStrengthWind;   // age, strength, and the downwind packed into two floats
        public float4 CentreRadius;
        public float4 FireSun;       // fireball radius and glow, then the sun octahedral
        public float4 Shape;        // cap centre, cap radius, cap tube, stem radius
    }

    // A unit vector in two floats. Both directions here are unit, and packing them is what keeps
    // the whole push constant at 128 bytes -- Vulkan's guaranteed minimum, and KSA's own volumetric
    // shader takes four, so there is no evidence of headroom to borrow.
    private static float2 OctahedralPack(double3 unit)
    {
        double sum = Math.Abs(unit.X) + Math.Abs(unit.Y) + Math.Abs(unit.Z);
        if (!(sum > 0.0)) return new float2(0f, 0f);

        double x = unit.X / sum;
        double y = unit.Y / sum;

        if (unit.Z < 0.0)
        {
            double fx = (1.0 - Math.Abs(y)) * (x >= 0.0 ? 1.0 : -1.0);
            double fy = (1.0 - Math.Abs(x)) * (y >= 0.0 ? 1.0 : -1.0);
            x = fx;
            y = fy;
        }

        return new float2((float)x, (float)y);
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
            // Far to near. Each dispatch composites its cloud OVER whatever is already in the
            // image, so the last one drawn ends up in front -- which is only right if the nearest
            // goes last.
            _order.Clear();
            for (int i = 0; i < NuclearClouds.Count && _order.Count < MaxClouds; i++)
            {
                if (!NuclearClouds.TryAt(i, out double3 at, out _, out _, out _, out _, out _, out _)) continue;

                _order.Add((i, Vec.Len2(at - camera.PositionEcl)));
            }

            if (_order.Count == 0) return;
            _order.Sort(static (a, b) => b.DistanceSq.CompareTo(a.DistanceSq));

            using (commandBuffer.TagRegion(GpuTag))
            {
                for (int n = 0; n < _order.Count; n++)
                {
                    if (!NuclearClouds.TryAt(_order[n].Index, out double3 burstEcl, out double3 up,
                                             out double radius, out double age,
                                             out MushroomCloud.Shape shape,
                                             out double3 downwind,
                                             out MushroomCloud.Flash flash)) continue;

                    // Differenced against the camera in DOUBLE and only then narrowed. The world is
                    // a solar system: a float metre cannot hold an ecliptic position, and Ego is a
                    // pure translation of Ecl, so the camera sitting at the origin in the shader is
                    // exact rather than close.
                    double3 centre = burstEcl - camera.PositionEcl;
                    if (!Vec.IsFinite(centre)) continue;

                    // Which way the light comes from, at the cloud rather than at the camera: over a
                    // kilometre of cloud the difference is nothing, and asking at the burst is what
                    // makes it right when the camera is somewhere else entirely. Straight up if the
                    // star cannot be found, which is noon and is at least a lighting direction
                    // rather than a black volume.
                    double3 sun = KsaWorld.TryStarPositionEcl(out double3 starEcl)
                                      ? Vec.Unit(starEcl - burstEcl)
                                      : up;

                    float2 windOct = OctahedralPack(Vec.Unit(downwind));
                    float2 sunOct = OctahedralPack(Vec.IsFinite(sun) ? sun : up);

                    Push push = new()
                    {
                        InvViewProj = camera.VPInv.viewProjection,
                        // The target's own size is not in here: the shader asks imageSize() for it,
                        // which freed the two floats the wind needed. The block is at Vulkan's
                        // guaranteed 128 bytes and there was nowhere else to take them from.
                        //
                        // The strength carries the cloud's own fade. It holds at one through the
                        // rise and half the stand and then squares away to nothing, so a cloud
                        // dissolves instead of being switched off when its shape expires.
                        AgeStrengthWind = new float4((float)age, tint * (float)shape.Fade,
                                                     windOct.X, windOct.Y),
                        CentreRadius = new float4((float)centre.X, (float)centre.Y, (float)centre.Z,
                                                  (float)radius),
                        // The cloud's own up is NOT in here: the shader derives it from the burst
                        // and the planet, both of which it already has. That freed the two floats
                        // the fireball needed -- its radius and how hard it is glowing, which is
                        // what lets a burst light the cloud it is sitting inside.
                        FireSun = new float4((float)flash.Radius, (float)flash.Glow,
                                             sunOct.X, sunOct.Y),

                        // The same shape MushroomCloud carries, so every dimension stays
                        // Glasstone's rather than being invented again in GLSL.
                        Shape = new float4((float)shape.CapCentre, (float)shape.CapRadius,
                                           (float)shape.CapTube, (float)shape.StemRadius),
                    };

                    // Between dispatches, because every one of them reads the scene image and
                    // writes it back: without this the second cloud races the first wherever the
                    // two overlap on screen and one of the writes is simply lost. KSA's own
                    // BarrierBatch, so this stands on public API like the rest of the pass.
                    if (n > 0) Hazard(commandBuffer);

                    // The VIEWPORT's slot, never the frame index. That argument picks the dynamic
                    // offset into the global set, which is where global.lighting lives: a frame
                    // index there reads a different viewport's planet, sun and radii on every frame
                    // in flight, and anything lit from that block flickers at frame rate.
                    _pipeline!.BindPipeline(commandBuffer, viewport.ShaderSlot, default, default, push);
                    commandBuffer.Dispatch((width + Group - 1) / Group,
                                           (height + Group - 1) / Group, 1);
                }
            }
        }
        catch (Exception e)
        {
            Warn($"the pass threw and is standing down: {e.Message}");
            Release();
        }
    }

    // The most clouds drawn at once. A bus carries six warheads and each makes one; past that is a
    // world nobody has, and every extra is a full-screen dispatch however little of it survives the
    // bounding sphere.
    private const int MaxClouds = 8;

    private static readonly List<(int Index, double DistanceSq)> _order = [];

    // One compute-write to compute-read barrier, so a dispatch sees what the one before it wrote.
    private static void Hazard(CommandBuffer commandBuffer)
    {
        Span<VkMemoryBarrier2> one = stackalloc VkMemoryBarrier2[1];
        BarrierBatch batch = new(one, default, default);

        VkMemoryBarrier2 barrier = new()
        {
            SrcStageMask = VkPipelineStageFlags2.ComputeShaderBit,
            SrcAccessMask = VkAccessFlags2.ShaderWriteBit,
            DstStageMask = VkPipelineStageFlags2.ComputeShaderBit,
            DstAccessMask = VkAccessFlags2.ShaderReadBit | VkAccessFlags2.ShaderWriteBit,
        };

        batch.Add(ref barrier);
        batch.SubmitAndFlush(commandBuffer);
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

        // The engine's aerial-perspective LUTs, as this pass's own samplers -- which is how Core's
        // consumers take them too. The transmittance LUT is not among them because it is already in
        // the global set the wrapper binds at 0, and the scalars the call wants -- planet position,
        // sun position, radii, the layer -- are in that set's lighting block. So there is no
        // uniform buffer here and nothing of this mod's to keep in step with the engine.
        AtmosphereRenderer air = Program.PlanetAtmosphereRenderer;
        IRenderImage[] aerial =
        [
            air.AerialPerspectiveRange,
            air.AerialPerspectiveColorRgbTransmittanceR,
            air.AerialPerspectiveTransmittanceGb,
        ];
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
            storageTargets, depthTargets, aerial, default, shader,
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
