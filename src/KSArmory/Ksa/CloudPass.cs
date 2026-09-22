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
        public float4 FireSun;       // fireball radius and glow, the whiteout, the scorch
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

    /// <summary>
    /// Whether a build was attempted and did not produce a pipeline.
    ///
    /// <para>Not the same question as <see cref="Available"/> being false, and reading it as the
    /// same is what made a deliberate control run report a shader that would not compile: a pass
    /// switched off is never asked to build, so it has no pipeline for a reason that is not a
    /// fault. Only a build that ran and failed sets this.</para>
    /// </summary>
    public static bool BuildFailed { get; private set; }

    /// <summary>Drops the pipeline, so the next frame builds it again.</summary>
    public static void Release()
    {
        _pipeline = null;
        BuildFailed = false;
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

            // THE GROUND FIRST, because the clouds composite over what is already in the image and
            // a mark is under the column rather than in front of it.
            //
            // One dispatch each. They are the cheap kind -- a scorch-only push leaves the shader
            // before the march, measured at 0.04 ms a frame against the column's 2 -- and they run
            // for the rest of the session, which is why NuclearClouds bounds the list rather than
            // this loop.
            int marks = 0;

            using (commandBuffer.TagRegion(GpuTag))
            {
                for (int i = 0; i < NuclearClouds.ScorchCount; i++)
                {
                    if (!NuclearClouds.TryScorch(i, out double3 markEcl, out double markRadius,
                                                 out double3 markWind)) continue;

                    double3 markCentre = markEcl - camera.PositionEcl;
                    if (!Vec.IsFinite(markCentre)) continue;

                    float2 markOct = OctahedralPack(markWind);

                    Push burn = new()
                    {
                        InvViewProj = camera.VPInv.viewProjection,
                        // The wind in the same two floats the column's lean takes it in, and the
                        // strength beside it left at zero -- which is what says there is no cloud
                        // on this dispatch. What fell out of a cloud landed downwind of it, so the
                        // mark needs the same vector the lean does and takes it the same way.
                        AgeStrengthWind = new float4(0f, 0f, markOct.X, markOct.Y),
                        // The radius here is the bounding sphere the MARCH uses, and there is no
                        // march: zero is what tells the shader this dispatch is the ground alone.
                        CentreRadius = new float4((float)markCentre.X, (float)markCentre.Y,
                                                  (float)markCentre.Z, 0f),
                        FireSun = new float4(0f, 0f, 0f, (float)markRadius),
                        Shape = float4.Zero,
                    };

                    if (marks > 0) Hazard(commandBuffer);

                    _pipeline!.BindPipeline(commandBuffer, viewport.ShaderSlot, default, default, burn);
                    commandBuffer.Dispatch((width + Group - 1) / Group,
                                           (height + Group - 1) / Group, 1);
                    marks++;
                }
            }

            // A flash with no cloud under it still has to be written, so one dispatch happens for
            // the whiteout alone: a burst on an airless body grows no column and blinds a viewer
            // all the same.
            if (_order.Count == 0)
            {
                if (BurstFlash.Whiteout <= 0f) return;

                using (commandBuffer.TagRegion(GpuTag))
                {
                    if (marks > 0) Hazard(commandBuffer);

                    Push flashOnly = new()
                    {
                        InvViewProj = camera.VPInv.viewProjection,
                        AgeStrengthWind = float4.Zero,
                        CentreRadius = float4.Zero,
                        FireSun = new float4(0f, 0f, BurstFlash.Whiteout, 0f),
                        Shape = float4.Zero,
                    };

                    _pipeline!.BindPipeline(commandBuffer, viewport.ShaderSlot, default, default,
                                            flashOnly);
                    commandBuffer.Dispatch((width + Group - 1) / Group,
                                           (height + Group - 1) / Group, 1);
                }

                return;
            }
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

                    float2 windOct = OctahedralPack(Vec.Unit(downwind));

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
                        // Neither the cloud's up nor the direction to the sun is in here: the
                        // shader derives both from the burst, the planet and the star, all of which
                        // it already has. That freed four floats in a block with nothing spare --
                        // the fireball's radius and glow, which let a burst light the cloud it is
                        // inside, and the whiteout it leaves on the view.
                        //
                        // The whiteout goes on ONE dispatch. The pass runs once per standing cloud
                        // and each would otherwise lay its own white over the last.
                        //
                        // No scorch here: the ground a burst burned outlives the column over it,
                        // so it is its own dispatch below and a cloud never draws one.
                        FireSun = new float4((float)flash.Radius, (float)flash.Glow,
                                             n == 0 ? BurstFlash.Whiteout : 0f, 0f),

                        // The same shape MushroomCloud carries, so every dimension stays
                        // Glasstone's rather than being invented again in GLSL.
                        Shape = new float4((float)shape.CapCentre, (float)shape.CapRadius,
                                           (float)shape.CapTube, (float)shape.StemRadius),
                    };

                    // Between dispatches, because every one of them reads the scene image and
                    // writes it back: without this the second cloud races the first wherever the
                    // two overlap on screen and one of the writes is simply lost. KSA's own
                    // BarrierBatch, so this stands on public API like the rest of the pass.
                    if (n > 0 || marks > 0) Hazard(commandBuffer);

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

            // AFTER Release, which clears it. A throw out of the build is a failure to build, and
            // KSA compiles the shader at load -- so a GLSL fault arrives here and nowhere else,
            // and reading it as a pass nobody switched on is the same silence by a third route.
            BuildFailed = true;
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
            BuildFailed = true;
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

        BuildFailed = false;
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
