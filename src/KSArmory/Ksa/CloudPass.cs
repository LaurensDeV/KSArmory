using System.Buffers.Binary;
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
/// <para>Held per viewport and rebuilt when its target changes, because the descriptor sets name
/// the images. The clouds are marched into a layer with a dither that moves every frame, and a
/// resolve blends that with last frame's result reprojected onto this one: grain is what limits
/// the march, and last frame's samples are the only ones that cost nothing.</para>
/// </summary>
internal static class CloudPass
{
    private const string ShaderId = "KSArmoryCloudCompute";
    private const string ResolveShaderId = "KSArmoryCloudResolveCompute";

    // The compute shader's workgroup, which has to match KSArmoryCloud.comp's local_size.
    private const int Group = 8;

    // KSA's own GPU profiler rather than a query pool of this mod's. A compute dispatch is
    // asynchronous, so timing it from C# measures the recording and not the work; the engine
    // already writes timestamps around every region tagged this way, and its profiler window then
    // lists this pass beside the passes it has to be afforded against.
    private static readonly ProfilerTag GpuTag = new("KSArmory Cloud"u8);

    private static ComputePipelineWrapper? _pipeline;

    // Everything one viewport's clouds are drawn with. The pass is recorded once per viewport a
    // frame, and a camera window has its own target, its own size and its own history -- sharing
    // one set drew a window's clouds into the main view's image whenever the two were one size.
    private sealed class View
    {
        public required IRenderImage Target;
        public required int Width;
        public required int Height;

        // The clouds this frame, which the resolve blends with the history and composites.
        public required RenderImage LayerColour;
        public required RenderImage LayerDistance;

        // Last frame's result and this one's, swapping: the resolve samples one and writes the
        // other, and a pipeline each way names them.
        public required RenderImage[] History;
        public readonly ComputePipelineWrapper?[] Resolve = new ComputePipelineWrapper?[2];
        public int Parity;

        // One cloud pipeline per weather-cloud pair its descriptor sets were built against, null
        // for the stand-ins bound when there are none. The renderer's accumulated pair alternates
        // between two sets of images frame by frame, so two are held rather than one rebuilt.
        public readonly List<(RenderImage? Colour, RenderImage? Distance, ComputePipelineWrapper Pipeline)>
            Clouds = [];

        // What reprojects the history: the view it was drawn from and where its reference cloud
        // stood against the camera then. Only good for the frame straight after.
        public long Frames;
        public long ResolvedAt = long.MinValue;
        public float4x4 LastViewProjection;
        public int LastSerial;
        public double3 LastCentre;
    }

    private static readonly Dictionary<int, View> _views = [];
    private const int MostPipelines = 2;

    // Images dropped while a frame in flight may still read them, disposed once none can.
    private static readonly List<(RenderImage Image, long DueAt)> _graveyard = [];
    private static long _recorded;
    private const long GraveFrames = 16;

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
        foreach (View view in _views.Values) Bury(view);
        _views.Clear();
        BuildFailed = false;
        LastMarkTile = 1.0;
    }

    private static void Bury(View view)
    {
        _graveyard.Add((view.LayerColour, _recorded + GraveFrames));
        _graveyard.Add((view.LayerDistance, _recorded + GraveFrames));
        _graveyard.Add((view.History[0], _recorded + GraveFrames));
        _graveyard.Add((view.History[1], _recorded + GraveFrames));
    }

    private static void DisposeTheDue()
    {
        for (int i = _graveyard.Count - 1; i >= 0; i--)
        {
            if (_graveyard[i].DueAt > _recorded) continue;

            try { _graveyard[i].Image.Dispose(); }
            catch { /* Already gone with the device. */ }

            _graveyard.RemoveAt(i);
        }
    }

    // The viewport's set, made or remade when its target is a different image or size.
    private static View ViewFor(IViewport viewport, IRenderImage colour, int width, int height)
    {
        if (_views.TryGetValue(viewport.ShaderSlot, out View? view)
            && ReferenceEquals(view.Target, colour) && view.Width == width && view.Height == height)
        {
            return view;
        }

        if (view is not null) Bury(view);

        Renderer renderer = Program.GetRenderer();
        VkExtent2D extent = new(width, height);

        view = new View
        {
            Target = colour,
            Width = width,
            Height = height,
            LayerColour = RenderImage.CreateColorStorage(renderer, "KSArmory Cloud Layer", extent,
                                                         VkFormat.R16G16B16A16SFloat),
            LayerDistance = RenderImage.CreateColorStorage(renderer, "KSArmory Cloud Layer Distance",
                                                           extent, VkFormat.R32SFloat),
            History =
            [
                RenderImage.CreateColorStorage(renderer, "KSArmory Cloud History A", extent,
                                               VkFormat.R16G16B16A16SFloat),
                RenderImage.CreateColorStorage(renderer, "KSArmory Cloud History B", extent,
                                               VkFormat.R16G16B16A16SFloat),
            ],
        };

        _views[viewport.ShaderSlot] = view;
        return view;
    }

    // The tunables' generation the pipelines were built with. A change is a rebuild, which is a few
    // milliseconds with the modules already compiled.
    private static int _tunedGeneration;

    // ShaderTunables as Vulkan's specialization info, over unmanaged memory freed once the pipeline
    // exists -- the wrapper reads it only while building. Written through Marshal rather than
    // pointers, so the mod needs no unsafe code for it.
    private sealed class Specialization : IDisposable
    {
        private readonly IntPtr _entries;
        private readonly IntPtr _data;

        public VkSpecializationInfo? Info { get; }

        public Specialization()
        {
            (ShaderTunables.Tunable Tunable, double Value)[] set = [.. ShaderTunables.Overridden()];
            if (set.Length == 0) return;

            int entrySize = Marshal.SizeOf<VkSpecializationMapEntry>();
            byte[] entries = new byte[entrySize * set.Length];
            byte[] data = new byte[4 * set.Length];

            for (int k = 0; k < set.Length; k++)
            {
                VkSpecializationMapEntry entry = new()
                {
                    ConstantID = set[k].Tunable.ConstantId,
                    Offset = (ByteSize32)(4 * k),
                    Size = (ByteSize64)4L,
                };
                MemoryMarshal.Write(entries.AsSpan(k * entrySize), in entry);

                if (set[k].Tunable.Integer)
                    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4 * k), (int)set[k].Value);
                else
                    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4 * k), (float)set[k].Value);
            }

            _entries = Marshal.AllocHGlobal(entries.Length);
            _data = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(entries, 0, _entries, entries.Length);
            Marshal.Copy(data, 0, _data, data.Length);

            byte[] raw = new byte[Marshal.SizeOf<VkSpecializationInfo>()];
            int count = set.Length;
            ByteSize64 size = (ByteSize64)(long)data.Length;
            IntPtr entriesAt = _entries;
            IntPtr dataAt = _data;
            MemoryMarshal.Write(raw.AsSpan(Offset(nameof(VkSpecializationInfo.MapEntryCount))), in count);
            MemoryMarshal.Write(raw.AsSpan(Offset(nameof(VkSpecializationInfo.MapEntries))), in entriesAt);
            MemoryMarshal.Write(raw.AsSpan(Offset(nameof(VkSpecializationInfo.DataSize))), in size);
            MemoryMarshal.Write(raw.AsSpan(Offset(nameof(VkSpecializationInfo.Data))), in dataAt);

            Info = MemoryMarshal.Read<VkSpecializationInfo>(raw);
        }

        private static int Offset(string field) => (int)Marshal.OffsetOf<VkSpecializationInfo>(field);

        public void Dispose()
        {
            if (_entries != IntPtr.Zero) Marshal.FreeHGlobal(_entries);
            if (_data != IntPtr.Zero) Marshal.FreeHGlobal(_data);
        }
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

            _recorded++;
            DisposeTheDue();

            if (ShaderTunables.Generation != _tunedGeneration)
            {
                Release();
                _tunedGeneration = ShaderTunables.Generation;
            }

            View view = ViewFor(viewport, colour, width, height);
            view.Frames++;

            // The weather is the main view's: KSA renders its clouds for that one alone, and their
            // images laid over a camera window would hide its burst behind somebody else's sky.
            RenderImage? weatherColour = null;
            RenderImage? weatherDistance = null;
            bool weather = ReferenceEquals(viewport, Program.MainViewport)
                           && KsaWorld.TryWeatherClouds(out weatherColour, out weatherDistance);
            if (!weather) weatherColour = weatherDistance = null;

            _pipeline = null;
            foreach ((RenderImage? c, RenderImage? d, ComputePipelineWrapper p) in view.Clouds)
            {
                if (ReferenceEquals(c, weatherColour) && ReferenceEquals(d, weatherDistance)) _pipeline = p;
            }

            if (_pipeline is null)
            {
                if (!Build(colour, depth, view, weatherColour, weatherDistance)) return;

                if (view.Clouds.Count >= MostPipelines) view.Clouds.RemoveAt(0);
                view.Clouds.Add((weatherColour, weatherDistance, _pipeline!));
            }

            // Into a layout a compute shader may sample, through KSA's own tracked state, so the
            // engine barriers them back out next frame from wherever this left them.
            if (weatherColour is not null && weatherDistance is not null)
            {
                Span<VkImageMemoryBarrier2> two = stackalloc VkImageMemoryBarrier2[2];
                BarrierBatch toSample = new(two);
                toSample.Add(weatherColour, ImageBarrierInfo.Presets.SampledReadC);
                toSample.Add(weatherDistance, ImageBarrierInfo.Presets.SampledReadC);
                toSample.SubmitAndFlush(commandBuffer);
            }

            if (Program.GetRenderCamera() is not { } camera) return;
            // Far to near. Each dispatch composites its cloud OVER whatever is already in the
            // image, so the last one drawn ends up in front -- which is only right if the nearest
            // goes last.
            _order.Clear();
            for (int i = 0; i < NuclearClouds.Count && _order.Count < MaxClouds; i++)
            {
                if (!NuclearClouds.TryAt(i, out double3 at, out _, out _, out _, out _, out _, out _, out _, out _)) continue;

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
                                                 out double3 markWind, out double markOverSea,
                                                 out bool markAirless)) continue;

                    double3 markCentre = markEcl - camera.PositionEcl;
                    if (!Vec.IsFinite(markCentre)) continue;

                    float2 markOct = OctahedralPack(markWind);

                    // ONLY THE PART OF THE SCREEN THE MARK IS ON. A mark is permanent, so a
                    // full-screen dispatch each is a cost that never goes away and is the whole
                    // reason NuclearClouds bounds how many may stand. Its own footprint is a few
                    // per cent of that.
                    Tile tile = TileFor(camera, markCentre, markWind, markRadius,
                                        markAirless ? 1.0 : ScorchScreenReach, width, height);
                    if (tile.Empty) continue;

                    RecordTile(tile, width, height);

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
                        // The tile's origin rides in the two floats a mark has no use for: it
                        // has no fireball, so the radius and the glow are free.
                        FireSun = new float4(tile.OriginX, tile.OriginY, 0f, (float)markRadius),
                        // How far the patch's centre stands above the sea, in the one float of the
                        // cloud's shape a mark does not use. Always set: zero would read as a mark
                        // standing on the waterline and blank everything below its own centre.
                        // And whether KSA's weather clouds are bound, in the next float along, and
                        // whether there was air to carry a plume downwind, in the one after.
                        Shape = new float4((float)markOverSea, weather ? 1f : 0f,
                                           markAirless ? 1f : 0f, 0f),
                    };

                    if (marks > 0) Hazard(commandBuffer);

                    _pipeline!.BindPipeline(commandBuffer, viewport.ShaderSlot, default, default, burn);
                    commandBuffer.Dispatch(tile.GroupsX, tile.GroupsY, 1);
                    marks++;
                }
            }

            if (_order.Count == 0)
            {
                Flash(commandBuffer, viewport, camera, width, height, marks > 0);
                return;
            }

            _order.Sort(static (a, b) => b.DistanceSq.CompareTo(a.DistanceSq));

            // Written into the layer rather than the scene, so the resolve can blend it with last
            // frame's. The layer's own images go to a storage layout here, every frame, through
            // KSA's tracked state.
            Span<VkImageMemoryBarrier2> layerBarriers = stackalloc VkImageMemoryBarrier2[2];
            BarrierBatch toLayer = new(layerBarriers);
            toLayer.Add(view.LayerColour, ImageBarrierInfo.Presets.StorageReadWriteC);
            toLayer.Add(view.LayerDistance, ImageBarrierInfo.Presets.StorageReadWriteC);
            toLayer.SubmitAndFlush(commandBuffer);

            int drawn = 0;
            int reference = 0;
            double3 referenceCentre = default;

            using (commandBuffer.TagRegion(GpuTag))
            {
                for (int n = 0; n < _order.Count; n++)
                {
                    if (!NuclearClouds.TryAt(_order[n].Index, out double3 burstEcl, out double3 up,
                                             out double radius, out double age,
                                             out MushroomCloud.Shape shape,
                                             out double3 downwind,
                                             out MushroomCloud.Flash flash,
                                             out double heat,
                                             out bool water)) continue;

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
                        // it already has. That freed the floats for the fireball's radius and glow,
                        // which let a burst light the cloud it is inside. The third is the heat left
                        // in the cloud's core, which outlasts the ball: the whiteout is a dispatch
                        // of its own, after every cloud, so the float is free here.
                        //
                        // No scorch here: the ground a burst burned outlives the column over it,
                        // so it is its own dispatch below and a cloud never draws one. The fourth
                        // float is therefore free on this dispatch, and carries two flags -- see
                        // CloudFlags.
                        FireSun = new float4((float)flash.Radius, (float)flash.Glow,
                                             (float)heat,
                                             CloudFlags(water, weather, first: drawn == 0, shape.Shock)),

                        // The same shape MushroomCloud carries, so every dimension stays
                        // Glasstone's rather than being invented again in GLSL.
                        Shape = new float4((float)shape.CapCentre, (float)shape.CapRadius,
                                           (float)shape.CapTube, (float)shape.StemRadius),
                    };

                    // Between dispatches, because every one of them reads the scene image and
                    // writes it back: without this the second cloud races the first wherever the
                    // two overlap on screen and one of the writes is simply lost. KSA's own
                    // BarrierBatch, so this stands on public API like the rest of the pass.
                    if (drawn > 0 || marks > 0) Hazard(commandBuffer);

                    // The VIEWPORT's slot, never the frame index. That argument picks the dynamic
                    // offset into the global set, which is where global.lighting lives: a frame
                    // index there reads a different viewport's planet, sun and radii on every frame
                    // in flight, and anything lit from that block flickers at frame rate.
                    _pipeline!.BindPipeline(commandBuffer, viewport.ShaderSlot, default, default, push);
                    commandBuffer.Dispatch((width + Group - 1) / Group,
                                           (height + Group - 1) / Group, 1);
                    drawn++;

                    // The nearest, drawn last: what the history is reprojected against.
                    reference = NuclearClouds.SerialAt(_order[n].Index);
                    referenceCentre = centre;
                }

                if (drawn > 0) Resolve(commandBuffer, viewport, camera, view, reference, referenceCentre);
            }

            Flash(commandBuffer, viewport, camera, width, height, hazard: true);
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

    /// <summary>
    /// What share of the screen the last mark dispatched over, or 1 when none has.
    ///
    /// <para>Read out beside the pass's milliseconds rather than logged once, because it is a
    /// property of where the camera is standing: the first mark of a run is dispatched on the frame
    /// the bomb bursts, with the camera still down at the impact and inside the mark's own box, so
    /// a number said once says 100% about a saving that is real everywhere else.</para>
    /// </summary>
    public static double LastMarkTile { get; private set; } = 1.0;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ResolvePush
    {
        public float4x4 InvViewProj;
        public float4x4 Reproject;     // zero when there is no history to reproject
    }

    // The layer blended with last frame's result where that result is now, and composited. The
    // history is only reprojected from the frame straight before, drawn around the same cloud: a
    // gap or a different reference leaves it pointing at the wrong place, and the current layer
    // alone is then the answer, grain and all, for a frame.
    private static void Resolve(CommandBuffer commandBuffer, IViewport viewport, Camera camera, View view,
                                int reference, double3 referenceCentre)
    {
        int from = view.Parity;
        int to = 1 - from;

        if (view.Resolve[from] is null && !BuildResolve(view, from)) return;

        bool continues = view.ResolvedAt == view.Frames - 1 && view.LastSerial == reference
                         && reference != 0;

        Hazard(commandBuffer);

        Span<VkImageMemoryBarrier2> two = stackalloc VkImageMemoryBarrier2[2];
        BarrierBatch history = new(two);
        history.Add(view.History[from], ImageBarrierInfo.Presets.SampledReadC);
        history.Add(view.History[to], ImageBarrierInfo.Presets.StorageReadWriteC);
        history.SubmitAndFlush(commandBuffer);

        ResolvePush push = new()
        {
            InvViewProj = camera.VPInv.viewProjection,
            Reproject = continues
                            ? Reprojection(view.LastViewProjection, view.LastCentre - referenceCentre)
                            : default,
        };

        view.Resolve[from]!.BindPipeline(commandBuffer, viewport.ShaderSlot, default, default, push);
        commandBuffer.Dispatch((view.Width + Group - 1) / Group, (view.Height + Group - 1) / Group, 1);

        view.Parity = to;
        view.ResolvedAt = view.Frames;
        view.LastViewProjection = camera.MVP.viewProjection;
        view.LastSerial = reference;
        view.LastCentre = referenceCentre;
    }

    // Last frame's view-projection, applied after moving a point by how far the cloud's centre has
    // shifted against the camera since: a point on the cloud keeps its place against the burst, and
    // the burst rides the planet at 30 km/s. Row-vector, as the camera's own EgoToClipDouble is:
    // translating first adds the shift times the first three rows to the fourth.
    private static float4x4 Reprojection(float4x4 lastViewProjection, double3 shift)
    {
        double4x4 m = double4x4.Unpack(in lastViewProjection);

        m.M41 += (shift.X * m.M11) + (shift.Y * m.M21) + (shift.Z * m.M31);
        m.M42 += (shift.X * m.M12) + (shift.Y * m.M22) + (shift.Z * m.M32);
        m.M43 += (shift.X * m.M13) + (shift.Y * m.M23) + (shift.Z * m.M33);
        m.M44 += (shift.X * m.M14) + (shift.Y * m.M24) + (shift.Z * m.M34);

        return float4x4.Pack(in m);
    }

    // THE WHITEOUT, LAST: it is glare in the eye rather than a thing in the world, so it veils
    // the clouds too, and one dispatch carries it however many are standing. It is centred on the
    // burst driving it, which rides in the cloud centre's three floats with the radius left at
    // zero -- the shader's sign that there is no cloud on this dispatch.
    private static void Flash(CommandBuffer commandBuffer, IViewport viewport, Camera camera,
                              int width, int height, bool hazard)
    {
        if (BurstFlash.Whiteout <= 0f && BurstFlash.Glare <= 0f) return;

        // Zero when the source cannot be resolved, which the shader reads as a glare with no
        // centre: one colour everywhere, the warm one.
        double3 source = double3.Zero;
        if (NuclearClouds.TryBurning(BurstFlash.SourceIndex, out double3 burstEcl, out _))
        {
            double3 centre = burstEcl - camera.PositionEcl;
            if (Vec.IsFinite(centre)) source = centre;
        }

        using (commandBuffer.TagRegion(GpuTag))
        {
            if (hazard) Hazard(commandBuffer);

            Push flash = new()
            {
                InvViewProj = camera.VPInv.viewProjection,
                AgeStrengthWind = float4.Zero,
                CentreRadius = new float4((float)source.X, (float)source.Y, (float)source.Z, 0f),
                // The halo's level, how violet the flash still is, and the halo's colour ride in
                // floats a flash has no other use for.
                FireSun = new float4(BurstFlash.Glare, BurstFlash.Violet, BurstFlash.Whiteout, 0f),
                Shape = new float4(BurstFlash.GlareColour.X, BurstFlash.GlareColour.Y,
                                   BurstFlash.GlareColour.Z, BurstFlash.HaloViolet),
            };

            _pipeline!.BindPipeline(commandBuffer, viewport.ShaderSlot, default, default, flash);
            commandBuffer.Dispatch((width + Group - 1) / Group, (height + Group - 1) / Group, 1);
        }
    }

    private static void RecordTile(Tile tile, int width, int height)
    {
        double whole = (double)((width + Group - 1) / Group) * ((height + Group - 1) / Group);

        LastMarkTile = whole > 0.0
                           ? Math.Clamp((double)tile.GroupsX * tile.GroupsY / whole, 0.0, 1.0)
                           : 1.0;
    }

    // The block of workgroups a mark's own footprint covers, and where it starts.
    private readonly record struct Tile(int OriginX, int OriginY, int GroupsX, int GroupsY)
    {
        public bool Empty => GroupsX <= 0 || GroupsY <= 0;
    }

    // Where on the screen a mark can possibly reach, as whole workgroups.
    //
    // ALONG THE WIND rather than a cube about the centre. The plume runs one way, so a cube sized
    // to its reach is three times too big in the other five directions -- and the cost of that is
    // not a few unused workgroups: its corners end up behind a camera watching from two and a half
    // kilometres, which takes the whole-screen fallback every time. Measured at 100.0% of the
    // screen before this and 55.1% after.
    //
    // Anything behind the camera takes the whole screen. A corner with a clip w at or under zero
    // has no screen position at all, and projecting it anyway folds the box inside out -- which
    // crops a mark the viewer is standing in, exactly when it fills the frame.
    private static Tile TileFor(Camera camera, double3 centreEgo, double3 downwind, double radius,
                                double reachInRadii, int width, int height)
    {
        Tile whole = new(0, 0, (width + Group - 1) / Group, (height + Group - 1) / Group);

        double reach = radius * reachInRadii;
        double across = radius * ScorchScreenWidth;
        if (!(reach > 0.0) || !Vec.IsFinite(downwind)) return whole;

        double3 along = Vec.Unit(downwind);
        if (Vec.Len2(along) < 0.5) return whole;

        double3 side = Vec.Unit(Vec.AnyPerpendicular(along));
        double3 other = Vec.Unit(Vec.Cross(along, side));

        double lowX = double.MaxValue, lowY = double.MaxValue;
        double highX = double.MinValue, highY = double.MinValue;

        for (int corner = 0; corner < 8; corner++)
        {
            // Upwind only by the patch's ragged rim; downwind by the plume's whole run.
            double3 at = centreEgo
                         + (along * ((corner & 1) == 0 ? -radius * ScorchScreenBehind : reach))
                         + (side * ((corner & 2) == 0 ? -across : across))
                         + (other * ((corner & 4) == 0 ? -across : across));

            double4 clip = camera.EgoToClipDouble(at);
            if (!(clip.W > 1.0e-6) || !double.IsFinite(clip.W)) return whole;

            double x = ((clip.X / clip.W * 0.5) + 0.5) * width;
            double y = ((clip.Y / clip.W * 0.5) + 0.5) * height;
            if (!double.IsFinite(x) || !double.IsFinite(y)) return whole;

            lowX = Math.Min(lowX, x);
            highX = Math.Max(highX, x);
            lowY = Math.Min(lowY, y);
            highY = Math.Max(highY, y);
        }

        int x0 = Math.Clamp((int)Math.Floor(lowX) / Group, 0, whole.GroupsX);
        int y0 = Math.Clamp((int)Math.Floor(lowY) / Group, 0, whole.GroupsY);
        int x1 = Math.Clamp(((int)Math.Ceiling(highX) + Group - 1) / Group, 0, whole.GroupsX);
        int y1 = Math.Clamp(((int)Math.Ceiling(highY) + Group - 1) / Group, 0, whole.GroupsY);

        return new Tile(x0 * Group, y0 * Group, x1 - x0, y1 - y0);
    }

    // How far a mark reaches downwind, across and upwind, in patch radii. All three are the
    // SHADER's constants restated -- the plume's run with its wandering tip, its widest half-width
    // with its wandering edge and soft cut, and the patch's ragged rim -- and if any grows past what
    // is here the mark is cropped at a straight edge partway along itself, which reads as terrain
    // rather than as a fault. ScorchFootprintTests is the only thing that compares the two sides.
    private const double ScorchScreenReach = 3.3;
    private const double ScorchScreenWidth = 1.8;
    private const double ScorchScreenBehind = 1.2;

    // The fourth fireball float on a cloud's dispatch: 1 if the column is spray, 2 if there are no
    // weather clouds to respect and 4 if it is the frame's first cloud, which clears the layer; and
    // eight times the blast front's radius in whole metres, which the ring of dust it lifts is drawn
    // from -- summed and NEGATED, because a positive value there is what makes the shader read a
    // dispatch as a ground mark. Exact as a float to 2,000 km, which the front never reaches while
    // the cloud stands. KSArmoryCloud.comp decodes exactly this.
    private static float CloudFlags(bool water, bool weather, bool first, double shockMetres)
    {
        double shock = Math.Clamp(Math.Round(shockMetres), 0.0, MostShockMetres);
        return -(float)((water ? 1.0 : 0.0) + (weather ? 0.0 : 2.0) + (first ? 4.0 : 0.0) + (8.0 * shock));
    }

    private const double MostShockMetres = 2.0e6;

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

    private static bool Build(IRenderImage colour, IRenderImage depth, View view,
                              RenderImage? weatherColour, RenderImage? weatherDistance)
    {
        if (!ModLibrary.TryGet<ShaderReference>(ShaderId, out var shader) || shader is null)
        {
            Warn($"no shader '{ShaderId}'; the pass will not draw");
            BuildFailed = true;
            return false;
        }

        Renderer renderer = Program.GetRenderer();

        IRenderImage[] storageTargets = [colour, view.LayerColour, view.LayerDistance];
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

            // KSA's weather clouds, so the burst is drawn behind the ones in front of it. With
            // clouds switched off there is nothing to bind and a descriptor cannot be left empty,
            // so the range LUT stands in and CloudFlags tells the shader not to read it.
            (IRenderImage?)weatherColour ?? air.AerialPerspectiveRange,
            (IRenderImage?)weatherDistance ?? air.AerialPerspectiveRange,
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

        using Specialization tuned = new();
        _pipeline = new ComputePipelineWrapper(
            storageTargets, depthTargets, aerial, default, shader,
            default, ranges, renderer.MaxFramesInFlight, renderer,
            "KSArmory.CloudPass", Program.PointClampedSampler, Program.LinearClampedSampler,
            specializationInfo: tuned.Info);

        BuildFailed = false;
        Log.Info($"cloud pass: built against {ShaderId}");
        return true;
    }

    // The resolve one way round: sampling History[from], writing History[1 - from].
    private static bool BuildResolve(View view, int from)
    {
        if (!ModLibrary.TryGet<ShaderReference>(ResolveShaderId, out var shader) || shader is null)
        {
            Warn($"no shader '{ResolveShaderId}'; the clouds will not draw");
            BuildFailed = true;
            return false;
        }

        Renderer renderer = Program.GetRenderer();

        IRenderImage[] storageTargets =
            [view.Target, view.LayerColour, view.LayerDistance, view.History[1 - from]];
        IRenderImage[] sampled = [view.History[from]];
        VkPushConstantRange[] ranges =
        [
            new VkPushConstantRange
            {
                Offset = (ByteSize32)0,
                Size = (ByteSize32)Marshal.SizeOf<ResolvePush>(),
                StageFlags = VkShaderStageFlags.ComputeBit,
            },
        ];

        using Specialization tuned = new();
        view.Resolve[from] = new ComputePipelineWrapper(
            storageTargets, default, sampled, default, shader,
            default, ranges, renderer.MaxFramesInFlight, renderer,
            "KSArmory.CloudResolve", Program.PointClampedSampler, Program.LinearClampedSampler,
            specializationInfo: tuned.Info);

        return true;
    }

    private static void Warn(string what)
    {
        if (_warned) return;

        _warned = true;
        Log.Warn($"cloud pass: {what}");
    }
}
