# Damage decals — how a projected decal works in KSA, and what one would cost here

**A plan and a research record, not a record of built behaviour.** Nothing in this file ships. It
is the mechanism read out of somebody else's working implementation, re-verified against the build
this repository targets, and then turned into what a burn mark on a hull would actually take.

The mechanism comes from **gatOS** (`meow-sci/gatOS`), commit
[`ee97843`](https://github.com/meow-sci/gatOS/commit/ee9784357a65e42975b6248be396a0d061a4d0f3),
*"feat(paint): sticker decals — spray user PNGs onto vehicles, terrain and ground clutter"*, by
Alex Sherwin. It ships as `/sim/paint/stickers`: upload a PNG, point the camera, and the image is
stuck on whatever is under the crosshair — a rocket, a hillside, a rock — and stays there while the
rocket flies and the planet turns. Its own design record is `plans/STICKERS_PLAN.md` in that
repository, which is unusually complete and is the thing to read after this one.

That commit was verified against KSA **2026.8.19.5261**. Everything cited below has been re-checked
against **2026.8.22.5348**, the build in `ksa-assemblies.lock`, and the line numbers are ours.
Every seam survived; several moved by a few dozen lines.

---

## 1. The one window in the frame

`Program.RenderGame` builds the main viewport in one long command buffer. The relevant fact is that
**the scene depth is not sampleable while the scene is being drawn** — inside the opaque dynamic-
rendering scope the depth image is an attachment being written to, and there is no copy of the full
opaque scene's depth (parts *plus* terrain *plus* ground clutter) until that scope ends.

The scope ends, several translucent passes run, and then:

```
KSA/Program.cs:4568   RenderedViewport.OffscreenTarget.ResolveAttachments(commandBuffer2);
KSA/Program.cs:4569   ... underwater ...
KSA/Program.cs:4574   if (GridFlag && DrawUI && GameSettings.ShowMapGrid()) GridPass.Run(...)
```

After `ResolveAttachments` the resolved single-sample `RenderTarget.DepthImage` and `ColorImage` are
both current and neither is bound as an attachment. That is the window, and it is not a gap somebody
noticed — it is where the engine draws its own post-resolve overlay, `GridPass`. A decal pass placed
there is a near-verbatim port of `GridPass.Run` (`KSA/GridPass.cs:427`).

**The seam is `RenderTarget.ResolveAttachments(CommandBuffer)`** (`KSA.Rendering/RenderTarget.cs:315`),
taken with a Harmony **postfix**. Three properties make it the right one:

- It is called **unconditionally** at `:4568`. The method *body* is MSAA-gated and does nothing when
  neither attachment is multisampled — but a postfix fires either way, so the hook works at every
  MSAA setting.
- It is an **instance** method, so `__instance` identifies which target resolved. The main
  viewport's is literally `Program.OffscreenTarget` (`Program.cs:438`, assigned to
  `MainViewport.OffscreenTarget` at `:1477`).
- It is **not behind `DrawUI`**. `GridPass.Run` is, and so is our own simulation step — see
  CLAUDE.md's *Not done* note on `FrameLatch`. A decal drawn from this seam keeps drawing with the
  UI hidden, which is what a screenshot wants.

There are three call sites and the other two must be rejected:

| Call site | Method | How it is told apart |
| --- | --- | --- |
| `Program.cs:4268` | `RenderViewport` — secondary viewports (crew portraits) | `__instance` is that viewport's own target, not `Program.OffscreenTarget`; and `RenderedViewport != MainViewport` |
| `Program.cs:4568` | `RenderGame` — **the one we want** | both identity checks pass, `EditorFlag` is false |
| `Program.cs:4694` | `RenderEditor` — the VAB | resolves the **same** `_offscreenTarget` after setting `_renderedViewportIndex = _mainViewportIndex` (`:4621`), so **both identity checks pass**. `Program.EditorFlag` (`:205`) is the only thing that separates it |

That last row is the trap. A decal anchored to a celestial body still resolves in the editor and
would draw over the hangar.

---

## 2. Why a projected box, and not a quad stuck on the hull

gatOS costed four architectures. The table is theirs; the last column is what each means for *us*.

| | A. Flat quad in the opaque scope | B. CPU mesh-conforming decal | **C. Screen-space projected, after resolve** | D. Extend the part shader per instance |
| --- | --- | --- | --- | --- |
| Craft hull | z-fights and clips on curvature; needs a dynamic depth bias KSA does not expose (`Core/Renderer.cs` shares only viewport and scissor) | excellent — clip the part mesh against the box | conforms to anything | excellent, and lit by the real PBR |
| Terrain | clips into bumps; the CPU height field and the GPU tessellation disagree | height-sampled patch, LOD mismatch remains | conforms including tessellation displacement | n/a |
| Ground clutter (rocks) | **impossible** | **impossible** | **works** | n/a |
| Lighting | ours to invent | ours to invent | ours to invent, post-lit | perfect |
| Cost | smallest | clipping code, per-anchor geometry | one cube, one shader, one depth reconstruct | all eight descriptor sets are already taken (`KSA/PartModelRenderer.cs`); deepest coupling available |

The clutter row is what decides it, and it is worth understanding because it is a general fact about
this engine. Ground clutter placement is **entirely on the GPU**: a compute shader writes instance
data into a device-local buffer with no transfer-src usage, and the readback path that exists
(`KSA.Terrain.Physics/ClutterEcotypePhysicalData.ReadbackGroundClutter`) is never constructed in a
shipping build. There is no CPU-addressable transform for a rock. But the clutter pre-pass and draw
both write scene depth — so a decal that reconstructs its receiving surface *from depth* reaches
clutter for free, and every other approach cannot reach it at all.

For damage decals the same argument arrives from a different direction: **we do not know what a
round hit until it has hit it, and we would rather not build geometry per impact.** A projected
decal needs a point and a normal and nothing else. Curvature, panel lines, a fuel tank's taper, the
join between two parts — the box just covers them.

---

## 3. The mechanism

### 3.1 Decal space

A decal is a `[-0.5, 0.5]³` cube centred on the surface point:

- `+x` — width, to the right of the image
- `+y` — the top of the image
- `+z` — the outward surface normal

scaled by `(width, height, depth)` in metres. `depth` is the only unintuitive one: it is how far
*through* the surface the projection reaches, and it is what absorbs every disagreement between
where the CPU thinks the surface is and where the GPU actually drew it. gatOS defaults it to 0.3 m
on a vessel and 1 m on terrain.

The matrix is composed `S · R · T · parent` in KSA's **row-vector** convention (`v * M`), so reading
it left to right is the order the operations happen in.

### 3.2 The draw

One `DrawIndexed(36)` per decal — a unit cube, 8 vertices, 12 triangles — with everything else in
push constants. Nothing is interpolated between the stages: the vertex shader only positions the
box, and the fragment shader works entirely from the depth buffer and the push block.

Pipeline state worth copying exactly:

- **`CullFront`.** The cube is wound counter-clockwise seen from outside — the glTF convention every
  KSA mesh renderer assumes — so culling the *front* faces leaves the far faces, and the box still
  covers its screen footprint when the camera is **inside** it. A near-face draw clips the decal
  away the moment you walk into its box. Same reason KSA draws the planet with `CullFront`.
- **No depth attachment and no depth test.** Occlusion is decided per fragment from the sampled
  scene depth, which is also what lets the decal wrap around geometry the box merely intersects.
- **Single sample**, because this draws after the resolve. The target's own
  `SetupGraphicsPipeline` helper stamps its MSAA state and both attachment formats, so the rendering
  info has to be hand-built: one colour attachment in `Program.Instance.ColorFormat`
  (`R16G16B16A16_SFLOAT`), depth and stencil `Undefined`.
- **`BlendColorAlphaOver`** and `ReverseZDepthStencil.NoDepthTest`, both from KSA's own presets.

Three descriptor sets, and the order is baked into the GLSL:

| Set | What | Note |
| --- | --- | --- |
| 0 | `GlobalShaderBindings.DescriptorSet` | the game-wide camera/lighting/celestial/vessel UBO block, bound with `GlobalShaderBindings.DynamicOffset(MainViewport.Index)` |
| 1 | ours: one `CombinedImageSampler` of the resolved `DepthImage`, `DepthReadOnlyOptimal`, `Program.PointClampedSampler` | |
| 2 | `Program.Instance.BindlessTextures.DescriptorSet` | `UpdateAfterBind \| PartiallyBound`, which is why a shader may index a slot the game never touches. The GLSL `#define SET_TEXTURE 2` must precede the include |

### 3.3 The push block, and the 3×4 packing

112 bytes — six `vec4` plus four scalars — inside the 128 B Vulkan minimum, `Vertex | Fragment`.

The six `vec4`s are `DecalToEgo` and `EgoToDecal` as 3×4. The packing is the clever part: KSA
matrices are row-vector, so component `i` of `p * M` is the dot of `(p, 1)` with **column** `i`.
Each `vec4` is therefore one *column*, which makes the shader's
`vec3(dot(d2e0,v), dot(d2e1,v), dot(d2e2,v))` reproduce `float3.Transform(pos, DecalToEgo)` exactly.
The useful consequence is free: `vec3(d2e0.z, d2e1.z, d2e2.z)` is *row 2* of the row-vector matrix,
which is the decal's `+z` axis in ego — the facing reference, without a seventh `vec4`.

Because 112 B is exactly full, gatOS signals debug draw by setting the texture id to `0xFFFFFFFF`,
a value a 1024-slot bindless table can never hand out.

### 3.4 The fragment shader

This is the whole idea, in about thirty lines:

1. `texelFetch` the resolved depth at `gl_FragCoord.xy`. Screen-sized and single-sample, so exactly
   one texel per fragment.
2. Reconstruct the ego-space point: `ndc = 2 * fragCoord / size - 1` on **both** axes with **no Y
   flip** — the projection already carries it — then `inverseProjection * vec4(ndc, z, 1)`,
   perspective divide, then `inverseView`, which is rotation-only, so undoing it lands in ego. This
   is the same arithmetic as `Camera.ScreenToEgoNearPlane`.
3. Take `normalize(cross(dFdx(p), dFdy(p)))` for the receiving surface's normal — **before any
   discard**. Derivatives are only defined in uniform control flow, and a neighbour that has already
   been discarded makes this undefined rather than merely noisy.
4. `if (z <= 0.0) discard;` — the depth is **reverse-Z**, so 0 is the far plane *and* what untouched
   background reads as. A decal has nothing to stick to there.
5. Project into decal space and reject outside the box:
   `if (!all(lessThanEqual(abs(pDec), vec3(0.5)))) discard;`. The negated form is deliberate — a NaN
   coordinate from a degenerate reconstruction discards too, where `if (any(greaterThan(...)))`
   would sail through.
6. Orient the derived normal towards the decal (the winding the derivatives produce is arbitrary),
   then `if (!(facing >= cutoff)) discard;` — negated again, same reason. A projected decal stretches
   without bound at grazing angles; a cosine cutoff of 0.2 with a `smoothstep` fade over the next 0.2
   is what hides it.
7. Sample the image through the bindless table: `SAMPLE_TEXTURE(texId, 0, pDec.xy * vec2(1,-1) + 0.5)`.
   The V flip is because PNG row 0 is the top.
8. Shade: one sun term plus an ambient floor.
   `global.lighting.sunPosition.xyz` is the sun's **ego** position and `sunColor` the star's light
   colour; `planetColor` is the nearby atmospheric body's lit colour and is **zero** for an airless
   body or a camera in shadow, so the small additive constant is what keeps a night-side decal from
   going black. Both live in the set-0 UBO (`Content/Core/Shaders/Common/Global.glsl:35-46`).

The known artifact is one pixel wide: at a depth discontinuity the derivatives straddle two surfaces
and the normal is nonsense. The NaN-safe tests eat it.

### 3.5 The depth descriptor ring

The depth image view changes on resize and on an MSAA change, so the descriptor is rewritten every
frame from the live `DepthImage.ImageView`. One set per frame in flight, indexed by
`Program.Instance.ResourceFrameIndex % MaxFramesInFlight`: the set for slot *i* is only rewritten
once the engine has already waited on slot *i*'s fence, so no in-flight command buffer can be
reading it.

The barriers are `GridPass`'s, verbatim: `DepthImage` to `ImageBarrierInfo.Presets.DepthSampledReadF`,
`ColorImage` to `ColorAttachmentReadWrite` with `inForceBarrier: true`. Depth is **left** in
`DepthSampledReadF`, exactly as `GridPass` leaves it — the engine's tracked-state barriers tolerate
that for the rest of the frame and next frame's `ClearDepthImages` barriers from the tracked state.

---

## 4. Anchors — the part that is our problem too

Ego space is camera-relative and the planet turns, so **nothing derived is cached across frames**.
What is *stored* is frame-independent, and what is *computed* is rebuilt every frame in `double`,
with only the final 3×4 packed to `float` for the push constant. Inverting the packed float matrix
instead would lose the surface point to cancellation at kilometre distances.

Two anchor kinds:

**Vessel.** Sub-part `InstanceId`, a position and a normal in that sub-part's local frame, and a
roll. Composition is `S · R · T · part.MatrixAsmb2Ego(vehicle.GetMatrixAsmb2Ego(camera))`, which
walks the whole sub-part parent chain **including `Part.Scale`** — a decal is stuck to the rendered
surface, so unlike a billboard it wants the scale. The decal follows staging animations, gimbals and
anything else that moves a sub-part, with no per-frame bookkeeping at all.

The reference for "up" defaults to the part's own `+Y` projected onto the tangent plane, so a decal
on the side of a stack reads upright along the stack; when `+Y` *is* the normal — a decal on a nose
cone's tip — that projection vanishes and `+X` stands in. **This is the third time this repository
has met that shape**, after `Vec.PerpendicularTo` and `OpticGeometry`: a reference direction that
degenerates when it lines up with the axis, and a named fallback rather than a shortest arc.

**Body.** Body id, latitude, longitude, heading. `+z` is the local radial and `+y` is rotated from
north by the heading, so it rides the planet's spin for free. The ego position is composed as *body
ego position + the body-fixed offset rotated into ecliptic axes* — **never an absolute ecliptic
point**, which is our own rule about the draw anchor, the round bodies and the remembered
anti-radiation emission, arrived at independently. The terrain height is sampled with
`accurate: false`, four texel taps, the same call the physics hot path uses; the GPU adds
tessellation the CPU never sees, and the box's depth absorbs it.

Both re-resolve their target from the live system **every frame** by id, which is what makes a decal
survive a bubble switch, a floating-origin shift, a scene reload and time warp. A despawned vessel or
a missing image makes a decal **dormant, not deleted** — it comes back if the target does.

---

## 5. Textures — and the shortcut we get that gatOS did not

gatOS takes arbitrary user PNGs at runtime, so it needs the whole path: decode with
`TextureLoader.LoadFromMemory` forcing four channels, wrap in a `TextureAsset` with a non-empty
`FilePath` (the `SimpleVkTexture` constructor throws otherwise), upload through a staging pool with
`fillMipChain`, claim a bindless slot with `BindlessTextureLibrary.AddTexture(imageView)`, and retire
the image through a deferred queue that waits out every frame in flight before destroying it. The
decoded CPU texture is neither `IDisposable` nor finalized, so its `Destroy()` must be called or the
native buffer leaks for the life of the process.

**We need almost none of that.** Our decals are art we ship, not art a player uploads. And a texture
declared in `KSArmoryAssets.xml` already carries a bindless handle:

```
KSA/TextureReference.cs:70    public int BindlessHandle { get; private set; }
KSA/TextureReference.cs:149   BindlessHandle = Program.Instance.BindlessTextures.AddTexture(ImageView);
```

set at load, and the game itself pushes those handles into its own shaders
(`KSA/CharacterRenderResources.cs:60-67`). So a scorch atlas declared beside the mesh atlases is
`ModLibrary.Get<TextureReference>("KSArmoryDecals").BindlessHandle` — one `uint` for the push block,
no decode, no staging submit, no retire queue, no eviction policy, and nothing to free.

That deletes the single largest chunk of gatOS's implementation and the one risk item they flagged
as unvalidated (an out-of-band staging submit during a frame). It also means **one atlas, many
decals**: a decal picks its patch with a UV rectangle in the push block rather than a texture id, so
holes, scorches, cracks and craters are sub-rectangles of one image.

---

## 6. Lifecycle and idle cost

The discipline is the same one this repository already applies to `WarpPolicy`, `TerrainMapScan` and
the effect emitters, and it is worth copying wholesale:

- The **Harmony patch is installed on the 0 → 1 live edge and removed on 1 → 0**. With nothing
  placed there is no patch, so the per-frame cost of the whole feature is one emptiness branch.
- The **pipeline, cube and descriptor ring track registry emptiness rather than liveness**, because
  rebuilding them costs a device-wide `WaitIdle`, two shader compiles and a blocking staging submit
  — and a decal goes dormant on every bubble switch. A dormant entry keeps them.
- The **postfix may not throw.** It runs inside the engine's frame loop, where an exception is the
  game rather than a log line. gatOS catches, logs **once**, latches a fault flag, and lets the next
  tick unpatch — a patched method cannot unpatch itself from inside.
- Teardown waits for the device to go idle before destroying anything.

Shaders are compiled at runtime from strings via `ShaderModuleUtils.FromString`. Two details bite:
`#include` resolves relative to the **directory of the debug name**, so the debug name has to be a
real path next to KSA's own shaders — found through a shipped asset (`ModLibrary.Get<ShaderReference>("GridFrag").ModPath`)
rather than hard-coded — and it **must be NUL-terminated**, because the include resolver reads it as
a C string.

---

## 7. What a KSArmory damage decal would be

### 7.1 The hit point is already computed, and already thrown away

`Ksa/HullTest.cs:59` calls `Part.RayCastEgo` and discards six out-parameters:

```csharp
if (!parts[i].RayCastEgo(in asmb2Round, ray, out double near, out double far,
                         out _, out _, out _, out _, out _, out _))
```

The full signature at our build (`KSA/Part.cs:2306`) is:

```csharp
public bool RayCastEgo(ref readonly double4x4 matrixVehicleAsmb2Ego, Ray ray,
    out double minDistance, out double maxDistance,
    out double3 nearIntersectionPositionAsmb, out double3 nearIntersectionNormalAsmb,
    out double3 farIntersectionPositionAsmb, out double3 farIntersectionNormalAsmb,
    out Part? closestSubPart, out Part? farthestSubPart)
```

`nearIntersectionPositionAsmb` and `nearIntersectionNormalAsmb` are in the local frame of
`closestSubPart` — **not** of the part the call was made on, because `RayCastEgoSubPart` inverts that
sub-part's own matrix to produce them. That is exactly the anchor a decal needs, in exactly the frame
it needs to be stored in, and a shell impact already pays for it. The normal is the mesh normal of
the triangle's first vertex, flat rather than barycentric — fine for orienting a box a decimetre
deep, and it never touches the shading, which the fragment shader derives from depth.

So a shell that strikes a hull yields a complete decal anchor with **no new raycast**: sub-part
`InstanceId`, position, normal, and a roll of zero.

### 7.2 The three decal sources, in order of how well the mechanism fits

| Source | Anchor | Notes |
| --- | --- | --- |
| **Shell strike** (`Slug` through `HullTest`) | vessel, from the discarded out-params above | the clean case; a small hole or spall mark, one per strike |
| **Proximity burst** (`Interceptor`, `Warhead`) | vessel, but there is **no hull point** — a proximity fuse never touches | see below |
| **Ground burst / crater** (bomb, reentry vehicle, `BlastSweep` over terrain) | body, lat/lon | we already compute the impact point in body-fixed coordinates for the ballistic path; `CoarseGroundTest` and `Ksa/GroundTest.cs` are the same height field gatOS marches |

The proximity case is the interesting one, and the projected-decal architecture answers it in a way a
quad cannot. A burst has a **centre and a radius**, not a surface point. Put the box at the burst,
size it to the lethal radius, orient `+z` along the vector from the burst to the craft's centre, and
the projection scorches *whatever geometry happens to be inside that volume* — near side only, by the
normal cutoff. No hull query, no per-part decision, and a burst between two parts marks both. That is
the same property that lets gatOS paint a rock: the decal does not need to know what it landed on.

The obvious cost is that it also marks the far craft if two are inside the box; the box's depth
bounds that, and so does not sizing it beyond the blast.

### 7.3 Where the code would live

The `Sim/` and `Ksa/` split falls out cleanly, and most of the interesting content is on the `Sim/`
side where it is testable headlessly:

| `Sim/` | |
| --- | --- |
| `DecalMark.cs` | one mark: anchor kind, target id, sub-part instance id, position, normal, roll, size, atlas rect, age |
| `DecalGeometry.cs` | the decal basis and the `S · R · T` composition — **the tangent-reference degeneracy and its fallback**, which is the thing worth a test |
| `DecalAgeing.cs` | how a mark fades and when it is evicted; the oldest-out cap |
| `DecalBudget.cs` | how many marks one impact is worth, and the per-craft ceiling |

| `Ksa/` | |
| --- | --- |
| `Decals.cs` | the registry: resolve targets by id each frame, compose, publish an immutable array |
| `DecalPass.cs` | pipeline, cube, depth descriptor ring, `RecordPass` |
| `DecalRenderPatch.cs` | the `ResolveAttachments` postfix and its three identity checks |

The anchor arithmetic being in `Sim/` matters more here than usual, because it is the half that is
wrong in ways nothing visible catches — a decal composed against the wrong instant sits a few metres
off the hull and reads as "the art is slightly wrong".

### 7.4 The frame rule this has to obey

`docs/FRAMES-AND-EPOCHS.md` applies to a decal exactly as it applies to a round body, and for the
same reason. A vessel decal is `part matrix this frame` and carries no ecliptic term at all, so it
is safe by construction. A **body** decal is `camera.GetPositionEgo(body) + bodyFixedOffset`, both
from this frame — never a stored ecliptic point, which is left behind by ~30 km per second of the
planet's own travel. And the pass runs inside the engine's own frame recording, *after* the mod's
step, so the matrices composed on the game thread and the depth they are projected against belong to
the same frame. Composing them anywhere else reopens the question.

---

## 8. What it costs us

**It would be the second place this mod patches the game.** CLAUDE.md currently says
`Ksa/AttitudeHook.cs` is *"the one place this mod patches the game"*, and that sentence is
load-bearing — it is why the rule about not patching has a single, arguable exception rather than a
growing list. A decal pass makes it two, on a target with a materially different risk profile:
`AttitudeHook` prefixes one `public virtual` method whose signature is tracked in
`docs/KSA-API-SURFACE.md`, where a decal pass binds to a dozen render internals at once. That is a
decision to take deliberately, not a detail of the implementation.

The rest, roughly in order of how likely it is to bite:

1. **Render-internals churn is High.** `RenderTarget.{ResolveAttachments,DepthImage,ColorImage,Extent}`,
   `Program.{OffscreenTarget,RenderedViewport,MainViewport,PointClampedSampler,SetViewport,ResourceFrameIndex,EditorFlag,ColorFormat}`,
   `GlobalShaderBindings.{DescriptorSet,DescriptorSetLayout,DynamicOffset}`, `BarrierBatch`,
   `ImageBarrierInfo.Presets`, `BindlessTextureLibrary`, and the `Global.glsl` UBO layout. gatOS
   notes that revision 5154 deleted the previous offscreen API wholesale. Everything listed survived
   5261 → 5348 unchanged, which is one data point and not a trend. The mitigation is the one we
   already use: bind the patched signature into our own metadata so
   `docs/KSA-API-SURFACE.md` tracks it and a change is a build error, plus a fault latch so a
   surprise is one log line and a feature that turns itself off.
2. **It draws over anything translucent that is in front of it** — a plume, a visor, an atmosphere —
   because it runs after those passes. Rare at decal scale and cosmetic; worth knowing before it is
   reported as a bug.
3. **Main viewport only.** Secondary viewports resolve their own targets with their own cameras, and
   the matrices were composed against the main camera. No terrain or clutter renders there anyway.
4. **The lighting is an approximation**, not the PBR the hull is shaded with. A decal on a hull in
   shadow will not match perfectly. gatOS's v2 idea is the good one: copy the colour image before the
   pass and modulate by the scene's own luminance, so shadow and sun direction come from the pixel.
5. **No aerial perspective and no MSAA on the decal's own edges.** A distance cull makes the first
   moot; the second is invisible at decal scale.
6. **Cost scales with screen coverage, not with count.** A decal is a full-screen-ish quad's worth of
   fragment work when you stand inside its box. A hundred small marks on a hull at 50 m are cheap; a
   crater box a kilometre across that the camera is inside is not.

---

## 9. If it is built, build it in this order

Each stage is worth committing on its own, and each proves something the next one assumes.

1. **The debug box.** Registry, patch, pipeline, cube, depth descriptor ring, and a fragment shader
   that outputs a magenta checker in decal space and samples nothing. This proves the seam, the
   reverse-Z reconstruction, the NDC convention and the box rejection with no art involved. gatOS
   kept theirs as a shipped debug flag, and the reason is good: it is the only way to tell "the
   matrices are wrong" from "the texture is wrong" in a live session.
2. **A body anchor.** Hard-code a lat/lon, draw the checker on the ground, drive around it. This is
   where the frame rule gets tested, and it is testable without firing anything.
3. **The atlas.** Declare it, push `BindlessHandle` and a UV rect, replace the checker.
4. **A vessel anchor from a real shell strike**, taking the out-params `HullTest` already discards.
5. **The burst box**, which needs nothing new but a different way of sizing and orienting the volume.
6. **Ageing, the per-craft cap, and the settings control** — one panel control, per CLAUDE.md's
   ownership table this is session-wide display state and belongs in the settings window beside the
   world overlay switch.

Everything before stage 4 is verifiable without a weapon, which is the useful property: the risky
half of this feature can be flown and confirmed before any of it touches fire control.

---

## 10. What this mechanism will never do

- **Damage the craft.** A decal is paint. KSA exposes no partial-damage model — CLAUDE.md's *Kills
  are binary* stands, and a hull with forty scorch marks on it is as alive as one with none.
- **Persist.** The marks are runtime state, like tracks and rounds in flight. They could be written
  into `saves/<save>/KSArmory/` the way system settings are, but nothing about the mechanism requires
  it and a reload starting clean is defensible.
- **Show in the VAB.** Deliberately — `EditorFlag` rejects it, and a decal on a part in the hangar is
  a different feature with a different anchor.
- **Look right at a grazing angle.** The cutoff hides the stretch by fading the decal out; there is
  no angle at which a projected decal is correct edge-on. If a logo has to survive that, it is
  architecture B, and B is a much larger piece of work.
