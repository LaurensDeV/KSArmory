using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using KSA.Atmosphere.Rendering;
using KSA.Rendering;
using KSA.Rendering.Lighting;
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
    private const string ShockShaderId = "KSArmoryShockCompute";
    private const string FireLightShaderId = "KSArmoryFireLightCompute";

    // KSArmoryFireLight.comp's workgroup.
    private const int FireLightGroupX = 16;
    private const int FireLightGroupY = 8;

    // The albedo the fill assumes where the planet's cannot be read: KSA's own default, 0.5 to the 2.2.
    private const double FireLightAlbedo = 0.218;

    // The compute shader's workgroup, which has to match KSArmoryCloud.comp's local_size.
    private const int Group = 8;

    // KSA's own GPU profiler rather than a query pool of this mod's. A compute dispatch is
    // asynchronous, so timing it from C# measures the recording and not the work; the engine
    // already writes timestamps around every region tagged this way, and its profiler window then
    // lists this pass beside the passes it has to be afforded against.
    private static readonly ProfilerTag GpuTag = new("KSArmory Cloud"u8);

    // Each stage inside that region, so what the pass costs can be split by what costs it. Nested
    // rather than beside it: the pass's own total is the outer regions, and a stage tagged with the
    // outer tag is counted twice.
    private static readonly ProfilerTag MarksTag = new("KSArmory Cloud: marks"u8);
    private static readonly ProfilerTag MarchTag = new("KSArmory Cloud: march"u8);
    private static readonly ProfilerTag ResolveTag = new("KSArmory Cloud: resolve"u8);
    private static readonly ProfilerTag FrontsTag = new("KSArmory Cloud: fronts"u8);
    private static readonly ProfilerTag FireLightTag = new("KSArmory Cloud: fire light"u8);
    private static readonly ProfilerTag FlashTag = new("KSArmory Cloud: flash"u8);
    private static readonly ProfilerTag GlowTag = new("KSArmory Cloud: glow"u8);
    private static readonly ProfilerTag AuroraTag = new("KSArmory Cloud: aurora"u8);
    private static readonly ProfilerTag DebrisTag = new("KSArmory Cloud: debris"u8);
    private static readonly ProfilerTag RedWaveTag = new("KSArmory Cloud: red wave"u8);

    private static readonly List<(float Kind, double Brightness, Push Push)> _sky = [];

    /// <summary>How many sky dispatches the last frame asked for, and how many it drew.</summary>
    public static int SkyWanted { get; private set; }

    /// <inheritdoc cref="SkyWanted"/>
    public static int SkyDrawn { get; private set; }

    private static void KeepTheNewestOfEachKind()
    {
        _skyKinds.Clear();
        foreach ((float kind, _, _) in _sky) _skyKinds.Add(kind);

        SkyDispatch.Choose(_skyKinds, _keep);

        _kept.Clear();
        foreach (int i in _keep) _kept.Add(_sky[i]);
        _sky.Clear();
        _sky.AddRange(_kept);
    }

    private static readonly List<float> _skyKinds = [];
    private static readonly List<int> _keep = [];
    private static readonly List<(float Kind, double Brightness, Push Push)> _kept = [];

    // One kind's share of the frame's sky, in its own profiler region so `cost` splits the three.
    private static void DrawSky(CommandBuffer commandBuffer, IViewport viewport, Camera camera, int width, int height,
                                float kind, ProfilerTag tag, ref int marks)
    {
        bool any = false;
        foreach ((float k, _, _) in _sky) any |= k == kind;
        if (!any) return;

        using (commandBuffer.TagRegion(GpuTag))
        using (commandBuffer.TagRegion(tag))
        {
            foreach ((float k, _, Push push) in _sky)
            {
                if (k != kind) continue;

                if (marks > 0) Hazard(commandBuffer);

                BindCloud(commandBuffer, viewport, camera, push);
                commandBuffer.Dispatch((width + Group - 1) / Group, (height + Group - 1) / Group, 1);
                marks++;
            }
        }
    }

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

        // The scene as it was before the blast front bends it: a pass may not read a neighbour of
        // the pixel it writes, and the bend reads nothing else.
        public required RenderImage SceneCopy;
        public ComputePipelineWrapper? Shock;
        public ComputePipelineWrapper? FireLight;
        public VkImageView FireLightDepth;
        public VkImageView FireLightNormal;
        public VkImageView FireLightIrradiance;
        public readonly ComputePipelineWrapper?[] Resolve = new ComputePipelineWrapper?[2];
        public int Parity;

        // One cloud pipeline per weather-cloud pair its descriptor sets were built against, null
        // for the stand-ins bound when there are none. The renderer's accumulated pair alternates
        // between two sets of images frame by frame, so two are held rather than one rebuilt.
        public readonly List<(RenderImage? Colour, RenderImage? Distance, object? Shadows, ComputePipelineWrapper Pipeline)>
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
        _graveyard.Add((view.SceneCopy, _recorded + GraveFrames));
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
            SceneCopy = RenderImage.CreateColorStorage(renderer, "KSArmory Scene Copy", extent,
                                                       VkFormat.R16G16B16A16SFloat),
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

            // The weather's shadow data for the body the camera is near. A pipeline's set names the
            // buffers, so one built against another body's -- or against a planet list the renderer
            // has since rebuilt -- is not reused.
            if (Program.GetRenderCamera() is not { NearbyCelestial: { } near }) return;
            if (!KsaWorld.TryWeatherShadowBuffers(near, out VkBuffer shadowFixed, out VkBuffer shadowFrame,
                                                  out object? shadows)) return;

            _pipeline = null;
            foreach ((RenderImage? c, RenderImage? d, object? s, ComputePipelineWrapper p) in view.Clouds)
            {
                if (ReferenceEquals(c, weatherColour) && ReferenceEquals(d, weatherDistance)
                    && ReferenceEquals(s, shadows)) _pipeline = p;
            }

            if (_pipeline is null)
            {
                if (!Build(colour, depth, view, weatherColour, weatherDistance, shadowFixed, shadowFrame)) return;

                if (view.Clouds.Count >= MostPipelines) view.Clouds.RemoveAt(0);
                view.Clouds.Add((weatherColour, weatherDistance, shadows, _pipeline!));
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

            // With no body near there is no weather to shade by and nothing on the ground to mark.
            if (Program.GetRenderCamera() is not { NearbyCelestial: not null } camera) return;
            // Far to near. Each dispatch composites its cloud OVER whatever is already in the
            // image, so the last one drawn ends up in front -- which is only right if the nearest
            // goes last.
            _order.Clear();
            for (int i = 0; i < NuclearClouds.Count && _order.Count < MaxClouds; i++)
            {
                // Air too thin for a mushroom leaves a shell of glowing debris, drawn below as light.
                if (NuclearClouds.IsThin(i)) continue;
                if (!NuclearClouds.TryAt(i, out double3 at, out _, out _, out _, out _, out _, out _, out _, out _)) continue;

                _order.Add((i, Vec.Len2(at - camera.PositionEcl)));
            }

            // THE FIREBALL'S LIGHT on what KSA's pre-pass dropped it from, before anything is drawn
            // over those surfaces.
            FireLight(commandBuffer, viewport, camera, view, depth);

            // THE GROUND FIRST, because the clouds composite over what is already in the image and
            // a mark is under the column rather than in front of it.
            //
            // One dispatch each. They are the cheap kind -- a scorch-only push leaves the shader
            // before the march, measured at 0.04 ms a frame against the column's 2 -- and they run
            // for the rest of the session, which is why NuclearClouds bounds the list rather than
            // this loop.
            int marks = 0;

            using (commandBuffer.TagRegion(GpuTag))
            using (commandBuffer.TagRegion(MarksTag))
            {
                for (int i = 0; i < NuclearClouds.ScorchCount; i++)
                {
                    if (!NuclearClouds.TryScorch(i, out double3 markEcl, out double markRadius,
                                                 out double3 markWind, out double markOverSea,
                                                 out bool markAirless, out double markCoupling)) continue;

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
                        // And whether KSA's weather clouds are bound, in the next float along,
                        // whether there was air to carry a plume downwind, in the one after, and how
                        // much of a surface burst it was, which is how much fallout it dropped.
                        Shape = new float4((float)markOverSea, weather ? 1f : 0f,
                                           markAirless ? 1f : 0f, (float)markCoupling),
                    };

                    if (marks > 0) Hazard(commandBuffer);

                    BindCloud(commandBuffer, viewport, camera, burn);
                    commandBuffer.Dispatch(tile.GroupsX, tile.GroupsY, 1);
                    marks++;
                }
            }

            // THE SKY a high burst lights: the X-ray-heated layer, the aurora at each end of the field line
            // and a thin-air burst's debris shell, each a full-screen dispatch with a negative bound. Every
            // one is gathered first and up to SkyDispatch.MostOf of each kind drawn, newest first, since a bus bursting over several
            // targets asks for a dozen or more and each is a pass over the whole screen. They add light, so
            // the order they are drawn in is nobody's business.
            _sky.Clear();

            for (int i = 0; i < NuclearClouds.GlowCount; i++)
            {
                // Drawn against the planet the camera is near, which is the only one the shader has:
                // a glow over another body would be drawn as a shell round this one.
                if (!NuclearClouds.TryGlow(i, out double3 glowEcl, out double nits, out double green,
                                           out object? over)) continue;
                if (!ReferenceEquals(over, camera.NearbyCelestial)) continue;

                double3 lit = glowEcl - camera.PositionEcl;
                if (!Vec.IsFinite(lit)) continue;

                // Its size is the layer's, in the fireball's first two floats, and its brightness the bound's:
                // the layer where this body's air stops the X-rays, not a height.
                BodyAir glowAir = KsaWorld.BodyAirOf(camera.NearbyCelestial);
                _sky.Add((SkyDispatch.Glow, nits, new Push
                {
                    InvViewProj = camera.VPInv.viewProjection,
                    CentreRadius = new float4((float)lit.X, (float)lit.Y, (float)lit.Z, -(float)nits),
                    FireSun = new float4((float)XRayGlow.LayerAltitude(glowAir), (float)XRayGlow.LayerThickness(glowAir),
                                         SkyDispatch.Glow, 0f),
                    Shape = new float4((float)green, 0f, 0f, 0f),
                }));
            }

            for (int i = 0; i < NuclearClouds.AuroraCount; i++)
            {
                if (!NuclearClouds.TryAurora(i, out double3 footEcl, out double3 eastEcl, out double sinLatitude,
                                             out double nits, out double age, out object? over)) continue;
                if (!ReferenceEquals(over, camera.NearbyCelestial)) continue;

                double3 foot = footEcl - camera.PositionEcl;
                if (!Vec.IsFinite(foot)) continue;

                _sky.Add((SkyDispatch.Aurora, nits, new Push
                {
                    InvViewProj = camera.VPInv.viewProjection,
                    CentreRadius = new float4((float)foot.X, (float)foot.Y, (float)foot.Z, -(float)nits),
                    FireSun = new float4((float)Aurora.BottomAltitude(KsaWorld.BodyAirOf(camera.NearbyCelestial)),
                                         (float)Aurora.TopAltitude(KsaWorld.BodyAirOf(camera.NearbyCelestial)),
                                         SkyDispatch.Aurora,
                                         (float)sinLatitude),
                    Shape = new float4((float)eastEcl.X, (float)eastEcl.Y, (float)eastEcl.Z, (float)age),
                }));
            }

            for (int i = 0; i < NuclearClouds.Count; i++)
            {
                if (!NuclearClouds.TryDebris(i, out double3 shellEcl, out double3 fieldEcl, out double shellRadius,
                                             out DebrisShell.Look look, out object? over,
                                             out double clipAltitude)) continue;
                if (!ReferenceEquals(over, camera.NearbyCelestial)) continue;

                double3 shell = shellEcl - camera.PositionEcl;
                if (!Vec.IsFinite(shell)) continue;

                _sky.Add((SkyDispatch.Debris, look.Radiance, new Push
                {
                    InvViewProj = camera.VPInv.viewProjection,
                    AgeStrengthWind = new float4((float)look.Colour.X, (float)look.Colour.Y, (float)look.Colour.Z,
                                                 (float)look.Fill),
                    CentreRadius = new float4((float)shell.X, (float)shell.Y, (float)shell.Z, -(float)look.Radiance),
                    FireSun = new float4((float)shellRadius, (float)look.Elongation, SkyDispatch.Debris,
                                         (float)clipAltitude),
                    Shape = new float4((float)fieldEcl.X, (float)fieldEcl.Y, (float)fieldEcl.Z,
                                       (float)NuclearClouds.AgeOf(i)),
                }));
            }

            for (int i = 0; i < NuclearClouds.WaveCount; i++)
            {
                if (!NuclearClouds.TryWave(i, out double3 waveEcl, out double waveRadius, out double nits,
                                           out object? over)) continue;
                if (!ReferenceEquals(over, camera.NearbyCelestial)) continue;

                double3 from = waveEcl - camera.PositionEcl;
                if (!Vec.IsFinite(from)) continue;

                _sky.Add((SkyDispatch.RedWave, nits, new Push
                {
                    InvViewProj = camera.VPInv.viewProjection,
                    CentreRadius = new float4((float)from.X, (float)from.Y, (float)from.Z, -(float)nits),
                    FireSun = new float4((float)waveRadius, (float)RedWave.TrailMetres, SkyDispatch.RedWave,
                                         (float)RedWave.RedAltitude(KsaWorld.BodyAirOf(camera.NearbyCelestial))),
                }));
            }

            SkyWanted = _sky.Count;
            KeepTheNewestOfEachKind();

            SkyDrawn = _sky.Count;
            DrawSky(commandBuffer, viewport, camera, width, height, SkyDispatch.Glow, GlowTag, ref marks);
            DrawSky(commandBuffer, viewport, camera, width, height, SkyDispatch.Aurora, AuroraTag, ref marks);
            DrawSky(commandBuffer, viewport, camera, width, height, SkyDispatch.Debris, DebrisTag, ref marks);
            DrawSky(commandBuffer, viewport, camera, width, height, SkyDispatch.RedWave, RedWaveTag, ref marks);

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
                        //
                        // The heat shares its float with how much of a surface burst this was and
                        // how much of a stem it raised: MushroomCloud.PackHeat.
                        FireSun = new float4((float)flash.Radius, (float)flash.Glow,
                                             MushroomCloud.PackHeat(heat, shape.Coupling, shape.StemShare),
                                             KSArmory.CloudFlags.Pack(water, weather, first: drawn == 0, shape.Shock,
                                                                      NuclearClouds.DrynessAt(_order[n].Index))),

                        // The same shape MushroomCloud carries, so every dimension stays
                        // Glasstone's rather than being invented again in GLSL.
                        // The stem's radius carries how far up the column reaches in its fraction:
                        // MushroomCloud.PackStem.
                        Shape = new float4((float)shape.CapCentre, (float)shape.CapRadius,
                                           (float)shape.CapTube,
                                           MushroomCloud.PackStem(shape.StemRadius, shape.ColumnTop)),
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
                    // On the coarse grid one invocation stands for a square of pixels, so the dispatch
                    // covers the screen at that grid's size.
                    using (commandBuffer.TagRegion(MarchTag))
                    {
                        int scale = Math.Max((int)ShaderTunables.ValueOf(ShaderTunables.MarchScale, 1.0), 1);
                        int across = (width + scale - 1) / scale;
                        int down = (height + scale - 1) / scale;

                        BindCloud(commandBuffer, viewport, camera, push);
                        commandBuffer.Dispatch((across + Group - 1) / Group, (down + Group - 1) / Group, 1);
                    }

                    drawn++;

                    // The nearest, drawn last: what the history is reprojected against.
                    reference = NuclearClouds.SerialAt(_order[n].Index);
                    referenceCentre = centre;
                }

                if (drawn > 0)
                {
                    using (commandBuffer.TagRegion(ResolveTag))
                    {
                        Resolve(commandBuffer, viewport, camera, view, reference, referenceCentre);
                    }
                }

                Shock(commandBuffer, viewport, camera, view, depth);
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
        history.Add(view.History[from], ImageBarrierInfo.Presets.StorageReadWriteC);
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

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ShockPush
    {
        public float4x4 InvViewProj;
        public float4 CentreRadius;        // the burst, camera-relative, and how far the front has got
        public float4 CentreUvStrength;    // the burst on screen, how hard it bends, its thickness
        public float4 UpMode;              // the vertical at the burst, one plus its height long, and 0 to copy or 1 to bend
        public float4 Shake;               // the picture thrown across and up, its roll, and 1 while shaking
    }

    // How hard a front bends the light, in screen widths per unit of the shell's angular thickness,
    // at full strength. Its strength is its reach against the warhead's lethal radius, so it fades
    // as the overpressure does and is gone by about thirty lethal radii.
    //
    // Fifteen times what air does. A 20 kt front 3 km out deflects light by about 0.03 degrees, a
    // pixel, which is why the real one is only seen on high-speed film.
    private const float ShockBend = 4.5f;
    private const double ShockFaintest = 0.03;

    // How thick the front looks, as a share of how far it has got, and never less than this.
    private const double ShockShellShare = 0.03;
    private const double ShockShellFloorMetres = 3.0;

    // How many shell widths either side of the front the shader bends: KSArmoryShock.comp's cutoff.
    private const double ShockBandWidths = 3.0;

    // THE BLAST FRONTS, as a bend in the light behind each: the scene copied, then written back from
    // the copy where a ray grazes a front's shell. One copy and one bend per front strong enough to
    // see, each bending what the one before left, so where two cross their bends add; nothing once
    // they have gone. The first pass also moves the whole picture while BlastShake says a front has
    // just passed the eye.
    private static void Shock(CommandBuffer commandBuffer, IViewport viewport, Camera camera, View view,
                              IRenderImage depth)
    {
        _fronts.Clear();

        float4x4 viewProjection = camera.MVP.viewProjection;
        double4x4 vp = double4x4.Unpack(in viewProjection);

        for (int i = 0; i < NuclearClouds.Count && _fronts.Count < MaxClouds; i++)
        {
            if (!NuclearClouds.TryFront(i, out double3 burstEcl, out double3 up, out double front,
                                        out double chargeKg, out double overGround)) continue;

            double strength = Math.Clamp(Warhead.LethalRadius(chargeKg) / front, 0.0, 1.0);
            if (strength <= ShockFaintest) continue;

            double3 c = burstEcl - camera.PositionEcl;
            double width = Math.Max(front * ShockShellShare, ShockShellFloorMetres);

            // A front that has swept past the camera bends nothing: from inside the sphere every ray
            // passes the burst nearer than the camera is, so none grazes the shell -- which is the
            // only place the shader bends, three widths either side of it.
            if (Vec.Len(c) < front - (ShockBandWidths * width)) continue;

            double w = (c.X * vp.M14) + (c.Y * vp.M24) + (c.Z * vp.M34) + vp.M44;
            if (!(w > 0.0)) continue;

            // Row-vector, as the camera's own projection is.
            double x = ((c.X * vp.M11) + (c.Y * vp.M21) + (c.Z * vp.M31) + vp.M41) / w;
            double y = ((c.X * vp.M12) + (c.Y * vp.M22) + (c.Z * vp.M32) + vp.M42) / w;
            if (!double.IsFinite(x) || !double.IsFinite(y)) continue;

            _fronts.Add(new ShockPush
            {
                InvViewProj = camera.VPInv.viewProjection,
                CentreRadius = new float4((float)c.X, (float)c.Y, (float)c.Z, (float)front),
                CentreUvStrength = new float4((float)((x * 0.5) + 0.5), (float)((y * 0.5) + 0.5),
                                              ShockBend * (float)strength,
                                              (float)width),
                // Unit up, lengthened by the burst's height over the ground: KSArmoryShock.comp.
                UpMode = new float4((float)(up.X * (1.0 + overGround)), (float)(up.Y * (1.0 + overGround)),
                                    (float)(up.Z * (1.0 + overGround)), 0f),
            });
        }

        (double shakeX, double shakeY, double shakeRoll) = BlastShake.Offset;
        bool shaking = BlastShake.Shaking;
        if (_fronts.Count == 0 && !shaking) return;

        // Shaking with no front to draw is one pass that only moves the picture.
        if (_fronts.Count == 0) _fronts.Add(new ShockPush { InvViewProj = camera.VPInv.viewProjection });

        if (view.Shock is null && !BuildShock(view, depth)) return;

        using (commandBuffer.TagRegion(FrontsTag))
        {
            for (int n = 0; n < _fronts.Count; n++)
            {
                ShockPush push = _fronts[n];
                push.Shake = shaking && n == 0 ? new float4((float)shakeX, (float)shakeY, (float)shakeRoll, 1f) : default;

                Hazard(commandBuffer);

                Span<VkImageMemoryBarrier2> one = stackalloc VkImageMemoryBarrier2[1];
                BarrierBatch copy = new(one);
                copy.Add(view.SceneCopy, ImageBarrierInfo.Presets.StorageReadWriteC);
                copy.SubmitAndFlush(commandBuffer);

                view.Shock!.BindPipeline(commandBuffer, viewport.ShaderSlot, default, default, push);
                commandBuffer.Dispatch((view.Width + Group - 1) / Group, (view.Height + Group - 1) / Group, 1);

                Hazard(commandBuffer);

                push.UpMode.W = 1f;
                view.Shock.BindPipeline(commandBuffer, viewport.ShaderSlot, default, default, push);
                commandBuffer.Dispatch((view.Width + Group - 1) / Group, (view.Height + Group - 1) / Group, 1);
            }
        }
    }

    private static readonly List<ShockPush> _fronts = [];

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct FireLightPush
    {
        public float4x4 InvViewProj;
        public float4 LightRange;          // the light as KSA was handed it, camera-relative, and its range
        public float4 Radiant;             // its colour times its intensity, and the planet's mean albedo
        public float4 Cce2Ccf;             // the planet's rotation, world to its own frame, as a quaternion
        public float4 GroundMap;           // its colour map and a sampler, bindless; negative for none
    }

    // THE FIREBALL'S LIGHT where KSA's light pre-pass dropped it: KSArmoryFireLight.comp. The main
    // view alone, because that is the one view the pre-pass runs for; nothing when its images cannot
    // be reached, which leaves the craft as dark as KSA left them.
    private static void FireLight(CommandBuffer commandBuffer, IViewport viewport, Camera camera, View view,
                                  IRenderImage depth)
    {
        if (!Fireball.TryPushed(out double3 lightEgo, out float range, out float3 radiant)) return;
        if (!ReferenceEquals(viewport, Program.MainViewport)) return;
        if (Program.Instance?.PrePassRenderer is not { OpaqueHasValidDepth: true } prePass) return;
        if (prePass.OpaquePrePassData.Target is not { } target) return;
        if (target.ColorImage is not { } normal || target.DepthImage is not { } prePassDepth) return;
        if (target.Extent.Width != view.Width || target.Extent.Height != view.Height) return;
        if (!TryPrePassIrradiance(out RenderImage? irradiance)) return;

        if (view.FireLight is null
            || !view.FireLightDepth.Equals(prePassDepth.ImageView)
            || !view.FireLightNormal.Equals(normal.ImageView)
            || !view.FireLightIrradiance.Equals(irradiance!.ImageView))
        {
            if (!BuildFireLight(view, depth, prePassDepth, normal, irradiance!)) return;
        }

        FireLightPush push = new()
        {
            InvViewProj = camera.VPInv.viewProjection,
            LightRange = new float4((float)lightEgo.X, (float)lightEgo.Y, (float)lightEgo.Z, range),
            Radiant = new float4(radiant.X, radiant.Y, radiant.Z, (float)MeanAlbedo(camera.NearbyCelestial)),
            Cce2Ccf = Cce2CcfQuaternion(camera.NearbyCelestial),
            GroundMap = GroundMapFor(camera.NearbyCelestial),
        };

        using (commandBuffer.TagRegion(GpuTag))
        using (commandBuffer.TagRegion(FireLightTag))
        {
            // From the fragment read KSA left it in to a compute read, through KSA's own tracked
            // state, so the engine barriers it back next frame from wherever this left it.
            Span<VkImageMemoryBarrier2> one = stackalloc VkImageMemoryBarrier2[1];
            BarrierBatch toSample = new(one);
            toSample.Add(irradiance!, ImageBarrierInfo.Presets.SampledReadC);
            toSample.SubmitAndFlush(commandBuffer);

            Span<VkDescriptorSet> sets = stackalloc VkDescriptorSet[1];
            sets[0] = Program.Instance.TextureSystem.DescriptorSet;
            view.FireLight!.BindPipeline(commandBuffer, viewport.ShaderSlot, sets, default, push);
            commandBuffer.Dispatch((view.Width + FireLightGroupX - 1) / FireLightGroupX,
                                   (view.Height + FireLightGroupY - 1) / FireLightGroupY, 1);
            Hazard(commandBuffer);
        }
    }

    // The pre-pass keeps a surface's normal and not its colour, so the fill takes the albedo the
    // terrain round it averages -- KSA's own meanDiffuseLuminosity, which is what Planet.frag scales the
    // ground's colour to -- and the pixel's own hue. A fixed number instead was the terrain's several
    // times over, and the pad glowed beside the grass.
    private static double MeanAlbedo(Celestial? body)
    {
        try
        {
            float? mean = body?.BodyTemplate.ScatteringReference?.MeanDiffuseLuminosity is { } reference
                              ? (float)reference
                              : null;
            return mean is { } m && float.IsFinite(m) && m > 0f ? Math.Pow(m, 2.2) : FireLightAlbedo;
        }
        catch
        {
            return FireLightAlbedo;
        }
    }

    // The planet's rotation as the shader applies it, built from what Transform does to each axis
    // rather than from the type's own layout, so a convention nobody has checked cannot turn it inside
    // out. Identity where there is no body.
    private static float4 Cce2CcfQuaternion(Celestial? body)
    {
        if (body is null) return new float4(0f, 0f, 0f, 1f);

        double3 x = new double3(1, 0, 0).Transform(body.GetCce2Ccf());
        double3 y = new double3(0, 1, 0).Transform(body.GetCce2Ccf());
        double3 z = new double3(0, 0, 1).Transform(body.GetCce2Ccf());
        double m00 = x.X, m10 = x.Y, m20 = x.Z, m01 = y.X, m11 = y.Y, m21 = y.Z, m02 = z.X, m12 = z.Y, m22 = z.Z;

        double trace = m00 + m11 + m22;
        double qw, qx, qy, qz;
        if (trace > 0.0)
        {
            double s = Math.Sqrt(trace + 1.0) * 2.0;
            (qw, qx, qy, qz) = (0.25 * s, (m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s);
        }
        else if (m00 > m11 && m00 > m22)
        {
            double s = Math.Sqrt(1.0 + m00 - m11 - m22) * 2.0;
            (qw, qx, qy, qz) = ((m21 - m12) / s, 0.25 * s, (m01 + m10) / s, (m02 + m20) / s);
        }
        else if (m11 > m22)
        {
            double s = Math.Sqrt(1.0 + m11 - m00 - m22) * 2.0;
            (qw, qx, qy, qz) = ((m02 - m20) / s, (m01 + m10) / s, 0.25 * s, (m12 + m21) / s);
        }
        else
        {
            double s = Math.Sqrt(1.0 + m22 - m00 - m11) * 2.0;
            (qw, qx, qy, qz) = ((m10 - m01) / s, (m02 + m20) / s, (m12 + m21) / s, 0.25 * s);
        }

        return new float4((float)qx, (float)qy, (float)qz, (float)qw);
    }

    // The planet's colour cube map and a sampler, as the bindless handles the shader indexes; negative
    // where the body has none, which leaves ground at the planet's mean albedo.
    private static float4 GroundMapFor(Celestial? body)
    {
        try
        {
            if (body?.BodyTemplate.DiffuseReference?.Get() is { } map)
            {
                return new float4(map.BindlessHandle, Program.Instance.TextureSystem.SamplerClampHandle, 0f, 0f);
            }
        }
        catch
        {
            // No map to read; the mean stands in.
        }

        return new float4(-1f, -1f, 0f, 0f);
    }

    private static FieldInfo? _irradianceField;
    private static bool _irradianceMissing;

    // What KSA's light pre-pass wrote, which is the only way to know which pixels it dropped the light
    // from: its vote runs per subgroup among whichever lanes reach that light in their own lists, so it
    // cannot be repeated. A private field, and without it the fill is off rather than guessed.
    private static bool TryPrePassIrradiance(out RenderImage? image)
    {
        image = null;
        if (_irradianceMissing) return false;

        try
        {
            if (Program.LightSystem is not ClusteredLightSystem lights) return false;
            _irradianceField ??= typeof(ClusteredLightSystem).GetField(
                "_diffuseIrradianceImage", BindingFlags.NonPublic | BindingFlags.Instance);
            image = _irradianceField?.GetValue(lights) as RenderImage;
        }
        catch
        {
            image = null;
        }

        if (image is null)
        {
            _irradianceMissing = true;
            Warn("KSA's light pre-pass result could not be read; a fireball will not light craft beyond 3 km");
        }

        return image is not null;
    }

    private static bool BuildFireLight(View view, IRenderImage depth, RenderImage prePassDepth, RenderImage normal,
                                       RenderImage irradiance)
    {
        view.FireLight = null;
        if (!ModLibrary.TryGet<ShaderReference>(FireLightShaderId, out var shader) || shader is null)
        {
            Warn($"no shader '{FireLightShaderId}'; a fireball will not light craft beyond 3 km");
            return false;
        }

        Renderer renderer = Program.GetRenderer();

        IRenderImage[] storageTargets = [view.Target];
        IRenderImage[] depthTargets = [depth, prePassDepth];
        VkImageView[] readOnly = [normal.ImageView, irradiance.ImageView];
        VkPushConstantRange[] ranges =
        [
            new VkPushConstantRange
            {
                Offset = (ByteSize32)0,
                Size = (ByteSize32)Marshal.SizeOf<FireLightPush>(),
                StageFlags = VkShaderStageFlags.ComputeBit,
            },
        ];

        VkDescriptorSetLayout[] external = [Program.Instance.TextureSystem.Layout];
        view.FireLight = new ComputePipelineWrapper(
            storageTargets, depthTargets, default, default, shader,
            external, ranges, renderer.MaxFramesInFlight, renderer,
            "KSArmory.FireLight", Program.PointClampedSampler, Program.LinearClampedSampler,
            colorSamplerLinearViewsReadOnlyLayout: readOnly);
        view.FireLightDepth = prePassDepth.ImageView;
        view.FireLightNormal = normal.ImageView;
        view.FireLightIrradiance = irradiance.ImageView;

        return true;
    }

    private static bool BuildShock(View view, IRenderImage depth)
    {
        if (!ModLibrary.TryGet<ShaderReference>(ShockShaderId, out var shader) || shader is null)
        {
            Warn($"no shader '{ShockShaderId}'; the blast front will not bend the light");
            return false;
        }

        Renderer renderer = Program.GetRenderer();

        IRenderImage[] storageTargets = [view.Target, view.SceneCopy];
        IRenderImage[] depthTargets = [depth];
        VkPushConstantRange[] ranges =
        [
            new VkPushConstantRange
            {
                Offset = (ByteSize32)0,
                Size = (ByteSize32)Marshal.SizeOf<ShockPush>(),
                StageFlags = VkShaderStageFlags.ComputeBit,
            },
        ];

        view.Shock = new ComputePipelineWrapper(
            storageTargets, depthTargets, default, default, shader,
            default, ranges, renderer.MaxFramesInFlight, renderer,
            "KSArmory.Shock", Program.PointClampedSampler, Program.LinearClampedSampler);

        return true;
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
        double ballRadius = 0.0;
        if (NuclearClouds.TryBall(BurstFlash.SourceIndex, out double3 burstEcl, out double radius))
        {
            double3 centre = burstEcl - camera.PositionEcl;
            if (Vec.IsFinite(centre))
            {
                source = centre;
                ballRadius = radius;
            }
        }

        using (commandBuffer.TagRegion(GpuTag))
        using (commandBuffer.TagRegion(FlashTag))
        {
            if (hazard) Hazard(commandBuffer);

            Push flash = new()
            {
                InvViewProj = camera.VPInv.viewProjection,
                // The ball's radius, so the shader can ask how much of it the eye can see, and
                // whether it is a thin-air burst's debris shell, which the halo must not paint over.
                AgeStrengthWind = new float4((float)ballRadius,
                                             NuclearClouds.BallIsThin(BurstFlash.SourceIndex) ? 1f : 0f, 0f, 0f),
                CentreRadius = new float4((float)source.X, (float)source.Y, (float)source.Z, 0f),
                // The halo's level, how violet the flash still is, and the halo's colour ride in
                // floats a flash has no other use for.
                FireSun = new float4(BurstFlash.Glare, BurstFlash.Violet, BurstFlash.Whiteout, 0f),
                Shape = new float4(BurstFlash.GlareColour.X, BurstFlash.GlareColour.Y,
                                   BurstFlash.GlareColour.Z, BurstFlash.HaloViolet),
            };

            BindCloud(commandBuffer, viewport, camera, flash);
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
                              RenderImage? weatherColour, RenderImage? weatherDistance,
                              VkBuffer shadowFixed, VkBuffer shadowFrame)
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

        // KSA's bindless textures at set 2, which the weather's coverage maps are read out of; the
        // builder numbers external sets from 2, and KSA declares this one for compute as well.
        VkDescriptorSetLayout[] external = [Program.Instance.TextureSystem.Layout];

        // And the weather's shadow data in this pass's own set, after everything above: bindings 9
        // and 10 in the builder's order, the second advanced a slice per frame in flight.
        VkBuffer[] shadowData = [shadowFixed];
        VkBuffer[] shadowPerFrame = [shadowFrame];
        ByteSize[] shadowSlice = [CloudShadowRenderData.DynamicUboStride];

        using Specialization tuned = new();
        _pipeline = new ComputePipelineWrapper(
            storageTargets, depthTargets, aerial, default, shader,
            external, ranges, renderer.MaxFramesInFlight, renderer,
            "KSArmory.CloudPass", Program.PointClampedSampler, Program.LinearClampedSampler,
            specializationInfo: tuned.Info,
            uniformBuffers: shadowData,
            uniformDynamicBuffers: shadowPerFrame,
            uniformDynamicBufferRanges: shadowSlice);

        BuildFailed = false;
        Log.Info($"cloud pass: built against {ShaderId}, module {shader.Shader?.VkHandle ?? 0:X}");
        return true;
    }

    // Binds the cloud pipeline with the bindless textures, and its own set at this frame's slice of
    // the weather's per-frame shadow buffer -- the first dynamic offset after the global set's, since
    // offsets are taken in set order -- which is how KSA's own passes read it.
    private static void BindCloud(CommandBuffer commandBuffer, IViewport viewport, Camera camera, Push push)
    {
        Span<VkDescriptorSet> sets = stackalloc VkDescriptorSet[1];
        sets[0] = Program.Instance.TextureSystem.DescriptorSet;

        Span<ByteSize32> offsets = stackalloc ByteSize32[1];
        offsets[0] = Program.Instance.ResourceFrameIndex * CloudShadowRenderData.DynamicUboStride;

        _pipeline!.BindPipeline(commandBuffer, viewport.ShaderSlot, sets, offsets, push);
    }

    // The resolve one way round: reading History[from], writing History[1 - from].
    private static bool BuildResolve(View view, int from)
    {
        if (!ModLibrary.TryGet<ShaderReference>(ResolveShaderId, out var shader) || shader is null)
        {
            Warn($"no shader '{ResolveShaderId}'; the clouds will not draw");
            BuildFailed = true;
            return false;
        }

        Renderer renderer = Program.GetRenderer();

        // Last frame's history is read as STORAGE and filtered by hand, not sampled: the images are
        // made with CreateColorStorage, which asks for storage usage alone, and sampling an image
        // without the sampled usage is undefined -- flown, the history came back as garbage over a
        // hard-edged block of the screen, a dark grainy rectangle on the young cloud.
        IRenderImage[] storageTargets =
            [view.Target, view.LayerColour, view.LayerDistance, view.History[1 - from], view.History[from]];
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
            storageTargets, default, default, default, shader,
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
