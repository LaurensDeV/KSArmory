---
name: ksa-blender
description: Build a 3D asset for Kitten Space Agency in Blender and get it into the game correctly. Use when modelling a new part, editing an existing one, importing a .blend or .glb somebody authored by hand, baking textures for one, or debugging art that renders wrong in game (sparkle, flicker, a subpart in the wrong place, a part that loads invisible). Covers the export contract KSA actually enforces, the reframe into part space, the bakes that fail silently, and the two defects that are invisible outside the game.
---
 
# Making art for KSA
 
Two ways in, and both end at the same contract:
 
- **Generated** — a headless Blender script builds the part out of primitives.
  `tools/model/pantsir.py` and its modules are this, and the script *is* the model: there is no
  `.blend` to keep in sync.
- **Authored** — somebody models it in the Blender UI and exports a `.glb`. It then has to be
  reframed and rebaked to satisfy the same contract, which is what `tools/model/import-*.py`
  scripts are for.
Neither is more correct. Generated wins when the shape is parametric and has to agree with numbers
in the C#; authored wins when the shape is *observed* — a real vehicle from photographs.
 
**Read `tools/model/README.md` first for the generated pipeline.** This file is the part that is
about KSA rather than about Blender.
 
---
 
## 1. The export contract
 
This is what KSA reads out of a `.glb`, verified against all 44 Core atlases (1054 nodes).
 
### Every rendered subpart is one mesh, at identity, named the same as its node
 
| Rule | Evidence |
| --- | --- |
| **No node transform.** Translation, rotation and scale must all be baked into the vertices. | Of 1054 Core nodes, the only ones carrying a transform are `_ColPrim_*` collider helpers and floating-point dust (1e-16, 3e-07). No rendered subpart has one. |
| **Node name == mesh name.** | Holds for every rendered subpart in Core. |
| **Ship a `_VM` twin of every subpart.** | 419 of Core's 1054 nodes are `*_VM`. It is the editor's preview variant; the mod duplicates the same geometry rather than simplifying it. |
 
A part that loads but renders nothing is nearly always a name mismatch — the `<Mesh Id="...">` in
the XML has to be the name in the atlas, exactly. `tools/validate-parts.py` checks this, and it is
a **silent** failure in game otherwise.
 
```bash
./tools/validate-parts.py                       # ids, texture paths, geometry vs the C#
./tools/meshinfo.py <atlas.glb> <MeshName>      # exact bounds of one mesh
```
 
### Exporter settings, for the authored path
 
```
Format            glTF Binary (.glb)
+Y Up             OFF          <-- the one that matters, see §2
Apply Modifiers   ON
Include           Selected Objects (or the collection)
Data              Mesh: UVs, Normals.  No cameras, no lights, no punctual lights
Compression       off (Draco is not worth debugging against a pre-release loader)
```
 
Then still bake: "+Y Up off" fixes the axes but does **not** clear object transforms. In the UI,
`Object > Apply > All Transforms` on every exported object. In a script, `export_apply=True` plus
`bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)`.
 
If you are importing a `.glb` somebody else exported and it *does* carry node transforms, do not
re-export from Blender to fix it — write a converter that walks the node tree, composes each
chain, and rewrites `POSITION`/`NORMAL` in place. glTF is JSON plus one binary buffer and the
attributes are plain float32 vec3s, so this is fifty lines and it is exact.
 
### Verify the atlas by reading it, not by trusting the exporter
 
`checkmesh.py` checks the *surface*. It does not check the *contract* — node names, node
transforms, which attributes made it out. Those are exactly what fails silently, and the exporter
will happily write a node transform you did not ask for.
 
A GLB is a 12-byte header then length-prefixed chunks, the first of which is the JSON. The check
needs nothing installed:
 
```python
raw = open(path, "rb").read()
off = 12
while off < len(raw):
    clen, ctype = struct.unpack("<II", raw[off:off+8])
    if ctype == 0x4E4F534A:                              # 'JSON'
        js = json.loads(raw[off+8:off+8+clen])
    off += 8 + clen
 
meshes = [m.get("name") for m in js["meshes"]]
for n in js["nodes"]:
    carries = any(k in n for k in ("matrix", "translation", "rotation", "scale"))
    # want: n["name"] == meshes[n["mesh"]]
    #       carries is False, unless the name starts with _ColPrim_
    #       primitives have POSITION, NORMAL, TEXCOORD_0
    #       js.get("extensionsUsed") is empty  (no Draco snuck in)
```
 
Run it on every export, and read what it prints rather than assuming. The whole failure mode this
guards is a part that loads, resolves, matches — and renders nothing, with nothing in any log.
 
---
 
## 2. Coordinate system
 
The export runs with **`export_yup=False`**, so Blender coordinates *are* the coordinates KSA
reads out of the atlas, and the ones `<Transform>` and `<LocationAsmb>` in the part XML mean.
Leave "+Y Up" on and Blender swaps Y and Z on the way out: the Pantsir exports three metres long
and eight metres wide, and nothing warns you.
 
**Part space** in this mod:
 
- **+X is out of the surface the part attaches to.** It is the part's forward axis in KSA's sense.
  For something that stacks, that is up. For a store hanging under a wing, it points *away from
  the pylon* — downward.
- **+Y is along the host's long axis.** The Pantsir's cab is at +Y; a missile's or a pod's nose
  points +Y.
- **+Z is the host's right.**
The origin sits on the mounting face, so the part rests on its underside and every coordinate in
the script reads as an offset from where it bolts on.
 
**A model authored to a different convention needs one rotation baked in, not an XML transform.**
The mod rewrites the `<Transform>` of every moving subpart each frame, so a rest rotation parked
there is overwritten on the first update. Bake it.
 
### The reframe, as a recipe
 
An authored file is essentially never in part space, and deriving the rotation freehand each time
invites sign errors that survive every geometric check. Do it from two facts:
 
- **+X is the mounting-face normal pointing *away from* the host.** Not into it. A store's pad
  faces up toward its pylon, so the away direction is *down*.
- **+Y is the nose.**
Then take **+Z = +X × +Y**. Never guess the third axis, and check the determinant of the matrix
you built is `+1`. A determinant of `−1` is a mirror: it will pass bounds checks, clearance
checks and a render, and ship the part inside-out.
 
Worked, for a file authored nose-`+X` with the mounting pad on `+Z`:
 
```
part.x = -world.z      # away from the host, i.e. down
part.y =  world.x      # nose
part.z = -world.y      # = +X cross +Y, not a guess
```
 
Translate the mounting face to the origin **first**, then rotate, then
`transform_apply(location=True, rotation=True, scale=True)`. Confirm afterwards that the part's
X extent starts at exactly `0` — that is the mounting face, and it is the cheapest possible check
that you got the whole thing right.
 
### A node transform means the local axes tell you nothing
 
If a mesh still carries a rotation or offset on its object, its local space is wherever it was
imported from, not a considered convention. Do not read the intended part space off it — an
export mesh sitting at `rot Y = 90°` looks authoritative and is not.
 
By §1 the only mesh that ships is one already baked to identity. Until that bake happens, the
only frame that means anything is the *scene* frame the artist is actually working in.
 
### Parts that mate must be reframed together
 
Two parts that bolt to each other have to take the **same rotation**, each about its **own**
mounting face. Reframe one and not the other and they will not line up in game — and because each
is individually valid, nothing anywhere warns you. Do the pair in one sitting, and write the
shared rotation down once so both use the same three lines.
 
---
 
## 3. Shape: proportion first, detail second
 
A part that reads as "blocky" is almost never short of detail. It is the wrong *section*, and no
amount of surface greebling rescues it.
 
Concretely: a store rack modelled as a 300 mm wide, 36 mm thick plate read as a slab. The real
hardware it stood in for — a MAU-12 ejector rack — is **32 × 3 × 6.25 inches**: a narrow, deep
beam, 76 mm wide and 159 mm tall, and only 11.25 inches wide *across its sway braces*. Rebuilding
to that section fixed it in one pass, before a single detail was added.
 
- **Look the dimensions up.** Suspension and launch hardware is published to the inch by the
  people who manufacture it. So are the standards — 14 in and 30 in are the only lug spacings
  that exist. Searching costs less than guessing and then rebuilding.
- **Measure the part you are mating to before you design the mate.** Take the lug positions,
  pad size and body diameter off the neighbour's actual mesh. A dimension measured is a dimension
  that cannot drift, and it will often confirm the standard for you.
- **Bevel every edge**, 0.8–2.5 mm by part size. Flat-shaded primitives read as untextured boxes
  until their edges catch a highlight. It is the cheapest realism available and costs only
  geometry — but see §6, because it also multiplies the vertex count the exporter emits.
- **Round the ends.** A cylinder capping a beam reads as machined where a flat face reads as a
  placeholder. It also satisfies §4b for free, since a cylinder's side is parallel to nothing.
---
 
## 4. The two defects you cannot see outside the game
 
Both look identical in game — flickering white speckle crawling over the part — and **Blender's
preview render reproduces neither**, because the preview material has no normal map wired in. So
*diagnose with the checker, not by eye*: the symptom points at z-fighting whether the cause is
coplanar faces or degenerate UVs, and inflating geometry that is already fine fixes neither.
 
```bash
./tools/model/checkmesh.py <atlas.glb>                    # exits non-zero on either defect
./tools/model/checkmesh.py <new.glb> --compare <old.glb>  # geometry diff, not a byte diff
```
 
### 4a. Every face needs UV *area*
 
The obvious way to use a palette atlas — collapse a face's loops onto one swatch centre — gives
the face a **zero UV derivative**. A renderer deriving a tangent frame from that gets a
zero-length tangent, `normalize()` on it is NaN, and NaN survives being multiplied by zero, so a
flat normal map cannot rescue it. Project each face into a small patch instead
(`project_to_swatch()`).
 
On the authored path a real unwrap gives you this for free, but check it rather than assume:
sum the shoelace area of each face's UV loop and require it non-zero. A smart-project with a
sensible island margin passes; a hand-tweaked layout with a collapsed island does not.
 
### 4b. Never let two primitives share a face plane
 
Laying out by naming shared edges — "the deck's underside is at the same X as the hull's top" —
reads naturally and is wrong. The depth buffer then picks a winner per pixel per frame.
 
`box()` grows every box by a skin **plus a per-box, per-axis jitter**, and the second part is the
one that gets missed: a uniform skin separates faces pointing *at* each other and does nothing for
two boxes whose outer faces both sit on the same constant.
 
**`cyl()` gets no jitter at all**, so coaxial primitives need clearance by hand — and radius alone
will not save a coaxial pair, because their side facets stay parallel. Use a **different facet
count**, or a `cone()`, whose sides are parallel to nothing.
 
Joints: sink a cone's base *inside* the body it leaves rather than butting them at a plane. Two
coaxial sections meeting exactly at a plane put an end cap on an end cap.
 
> **The jitter runs off one seed, so moving a `box()` call reshuffles every box after it.** Adding
> or removing one is enough, and the damage lands somewhere else entirely. To change which group a
> primitive belongs to without disturbing anything, set `_group` around the existing call rather
> than moving the call. **Build new modules last.**
 
**Off the generated path there is no jitter at all.** Building in the UI or through `bmesh`, every
stacked pair needs an explicit interference fit: a primitive must *sink into* its neighbour by a
few millimetres, never rest on its face. A pad resting exactly on a beam's top plane earns both
defects at once — coplanar, and touching with zero interference. Pick odd numbers (0.011, not
0.010) so a later round-number tweak cannot silently restore the shared plane.
 
### Coplanar is only a defect where the faces overlap
 
A naive check — group faces by plane, flag any plane holding faces from two objects — drowns in
false positives. Mirror twins share their X planes. A row of identical bolts shares its cap
planes. None of those can z-fight, because they are disjoint *within* the plane.
 
Build an orthonormal basis in the plane and test 2D bounds before flagging:
 
```python
a = Vector((1,0,0)) if abs(n.x) < 0.9 else Vector((0,1,0))
u = n.cross(a).normalized()
v = n.cross(u)
# project each face's verts to (w.dot(u), w.dot(v)), then a plain AABB overlap
```
 
Without this the checker reported seven "defects" on a part that had none. A checker that cries
wolf gets switched off, which is strictly worse than having no checker.
 
---
 
## 5. Materials and textures
 
```xml
<PbrMaterial Id="KSArmory_Material">
  <Diffuse      Path="Textures/KSArmory_Diffuse.png" Category="Vessel" />
  <Normal       Path="Textures/KSArmory_Normal.png"  Category="Vessel" />
  <AoRoughMetal Path="Textures/KSArmory_PBR.png"     Category="Vessel" />
</PbrMaterial>
```
 
- **PNG is fine.** KSA loads PNG and `.ktx2` and mixes them in one material
  (`CharacterAssets.xml` does). No `toktx`, no encoder.
- **`AoRoughMetal` is R = ambient occlusion, G = roughness, B = metalness** — the glTF ORM
  convention. Not a guess: Core's `default_pbr.png` is `(255, 180, 0)` and
  `EmptyAoRoughMetallic.png` is `(255, 255, 0)`, i.e. unoccluded, rough, non-metal.
- **A part may declare several materials and several atlases.** Core does. So an authored asset
  with its own baked maps sits alongside the palette material rather than having to join it.
- **Paths are relative and it is undocumented what to.** Keep the asset XML at the mod root so
  "relative to the mod root" and "relative to the XML" are the same directory, and the question
  never has to be answered.
- **One export mesh, one material.** Even though a part *may* declare several, the shipped
  subpart wants a single material over a single unwrap — that is what the bake produces. Keep the
  many-material version as the high-poly source and collapse on the way out.
### The palette trick, for generated parts
 
`tools/model/palette.py` writes a 4x4 grid of flat swatches into one 512x512 atlas, and every
primitive puts all its UVs inside one swatch. Material by name, no unwrap, no bake, no seams.
128 px cells mean mipmapping never bleeds one swatch into its neighbour.
 
It is the right default for parametric geometry and the wrong one for anything that wants
observed detail — that wants a real unwrap and baked maps, which is §6.
 
---
 
## 6. Baking
 
Only the authored path needs this; the palette trick exists precisely to avoid it. Four things go
wrong, all of them silent, all of them costing a re-bake if you notice late.
 
Throughout: work on **copies** of the artist's objects with **copies** of their materials, in a
scratch collection. The bake rewires materials and the export mesh is a dead end — joined,
single-material, and reframed out of the frame the artist is still working in.
 
### 6a. `image.save()` destroys the buffer
 
```python
im.filepath_raw = path
im.save()            # <-- rebinds the datablock to the file it just wrote
```
 
The image's `source` flips `GENERATED` → `FILE`, and from that moment `im.pixels` reads back *the
file*, not the bake. If the write was wrong, the bake is gone and there is nothing left to
re-save. Three maps were written black this way and the originals were unrecoverable.
 
Check `im.source` after any save. Better, do not use it — a PNG is a signature and three chunks,
so writing one by hand is fifteen lines, is exact, and cannot touch the datablock:
 
```python
def chunk(t, d):
    return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t+d) & 0xffffffff)
 
raw = bytearray()
for y in range(h-1, -1, -1):               # Blender rows are bottom-up, PNG is top-down
    raw.append(0)                           # filter: none
    raw += rgba[y*w*4:(y+1)*w*4]
png = (b"\x89PNG\r\n\x1a\n"
       + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))   # 8-bit RGBA
       + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
       + chunk(b"IEND", b""))
```
 
The conversion is `int(v*255 + 0.5)` and it is correct for **both** colour spaces with no branch:
for an 8-bit image `pixels` returns what is *stored*, which is already sRGB-encoded for an sRGB
image and raw for a Non-Color one. Encode nothing yourself.
 
**Verify the pixel reader before trusting it.** `im.pixels[:]` is slow and correct.
`im.pixels.foreach_get()` into an `array('f', ...)` returned all zeros here and raised nothing,
and three blank maps were written before anybody noticed. Whatever accessor you use, push a known
value through it first.
 
### 6b. The DIFFUSE pass excludes metals
 
Blender's diffuse bake is `base_color * (1 - metallic)`. A material at metallic 1.0 bakes to
**pure black**; a mid-metal one bakes to a fraction of its albedo. Bare-alloy parts vanish.
 
There is also **no `METALLIC` bake type at all**.
 
Both have the same fix — bake through emission. Rewire each source material's output to an
Emission shader and bake `EMIT`:
 
- **albedo** — emission colour = the Principled `Base Color`
- **metalness** — emission colour = `(m, m, m)` from the `Metallic` scalar
Roughness has a real bake type and needs no trick. Compose ORM afterwards: R from the AO bake,
G from roughness, B from metalness.
 
### 6c. AO bakes black against coincident geometry
 
By bake time three things sit exactly where the target sits: the high-poly source, the `_VM` twin
(§1), and the `_ColPrim_*` box. Every occlusion ray leaves the surface and hits one of them
immediately, and the map comes back black — mean 0.026, and it looks like a bake that "just
failed".
 
Hide every renderable object except the bake target, bake, restore. AO is the one map that must
be a **self**-bake rather than selected-to-active.
 
### 6d. A flat normal map is the correct answer
 
If the low-poly *is* the high-poly — joined, not decimated — the baked normal map is uniform
`(0.5, 0.5, 1.0)`, and that is right. Do not chase it, do not raise the ray distance, do not
conclude the cage is wrong. Verify numerically (R, G mean ≈ 0.502, B ≈ 1.0) and move on.
 
Ship it anyway, because it is the *presence* of a normal map that makes §4a fatal: with no normal
map there is no tangent frame to turn into NaN. Shipping the flat map keeps the failure mode live
and therefore keeps the UV-area check honest.
 
### Interior faces bake AO = 0, and that is §4b's doing
 
The interference fits §4b demands mean a primitive-built part is substantially *inside itself* —
arm ends buried in the beam, the beam buried under the shoe. Those faces are fully occluded, bake
to black, and leave whole UV islands empty. On the rack that was 44% of surface area.
 
It is not a bug and it costs nothing, because none of it is ever visible. **Do not "fix" it by
pulling the primitives apart** — that reintroduces exactly the defect §4b exists to prevent.
 
### Sanity numbers
 
Read the maps back **off disk** before believing them, and compare per channel against the source
materials:
 
| Map | Expect |
| --- | --- |
| Diffuse | max = the sRGB encoding of the brightest `Base Color` — linear 0.34 reads back as 0.62 |
| Normal | R, G mean ≈ 0.502; B ≈ 1.0 |
| ORM | R = AO across 0…1; G = roughness, capped at the highest source roughness; B = metalness |
 
A map whose max is 0 is not a dark map, it is a failed one.
 
---
 
## 7. Articulation
 
Writing a subpart's transform each frame moves it. Confirmed in game. What it depends on:
 
- **`Part.SubParts` are `Part` objects in their own right**, with settable `Asmb2ParentAsmb` and
  `PositionParentAsmb` — so the part stays one part in the editor and still articulates.
- **`ResetCachedPosMatrixValues()` must be called after the write.** `Part` caches
  `_matrixAsmb2Parent`; without the reset the new value is stored and ignored.
- **A subpart rotates about its own mesh origin.** So a moving body's mesh is exported *recentred
  on its pivot* and put back with `<Position>` in the XML. Without that it swings around the part
  like a wrecking ball. Three copies of that pivot then exist — the model script, `Sim/Arsenal.cs`
  and the XML — and `validate-parts.py` is what stops them drifting.
- **SubParts do not nest.** Every `<SubPart>` is placed against the `<Part>`, so a turret's pods
  are *siblings* of the turret, not children. The mod composes the rotations itself and rewrites
  each body's `PositionParentAsmb` every frame.
- **Model a body at its working pose, not flat**, and apply runtime motion as a rotation away from
  that reference. A refused write then leaves the part looking right rather than inside-out.
- **A part with no moving bodies gets no `<SubPart>` at all** — one mesh, one node, done. Do not
  invent an articulation story for a bracket.
### A shipped part's subpart list is append-only
 
KSA pairs a saved part with its current definition **positionally**, bounding the loop by the save
and indexing the definition. Removing a `<SubPart>` throws `IndexOutOfRangeException` from inside
`Popup.DrawAll` and **terminates the game** on every save holding that part.
 
Adding and renaming are free. Leave a stub to hold the count, or repair saves with
`tools/repair-saves.py`.
 
---
 
## 8. Clearance, which a render cannot answer
 
A render only shows the poses it was asked for. Defects hide at the others.
 
```bash
./tools/model/checkswept.py          # sweeps the drives; needs neither Blender nor the game
```
 
It answers four questions nothing else can:
 
1. does every piece reach the chassis, or has something come adrift?
2. does each articulated body actually touch what it hangs off?
3. does any cover stand proud of what it covers?
4. does any assembly pass through another anywhere in its travel?
Three things to know:
 
- **It sweeps every articulated vehicle, and a new one has to be added to `vehicles()` by hand.**
  A body set nobody named is simply not swept, and the tool still prints "clear".
- **It sweeps *named axes*.** A freely-pointing head has none, so its travel has to be solved in
  the model script instead — sweep the moving body's own vertices against whatever it could
  strike, and print the limit for the C# to be pasted from.
- **Splitting a body out of an assembly is what makes a clash visible at all.** A body cannot
  intersect itself, so a stub buried inside another housing is invisible until it becomes a body
  in its own right. **Re-run this after adding a body to an existing assembly.**
Two geometric facts worth using rather than rediscovering:
 
- **Bearing cancels for any pair that both ride the same traverse.** One rigid motion applied to
  both, so it is a one-degree-of-freedom problem in elevation alone.
- **Elevation turns about +Z, so a gap in Z holds at every pose.** Separating two turret-riding
  bodies in X or Y only works at the elevation you checked.
**A fixed part is not exempt, it is just not the thing being swept.** Bolting new volume onto an
articulated vehicle means the *vehicle's* sweep is now stale, because the part plus whatever it
carries was not there when it last ran. Re-run it.
 
### Checking a fixed part against its neighbour
 
For a part that mates rather than moves, the useful check is a distance-to-neighbour pass, and it
is a dozen lines: for every vertex, the signed gap to the neighbour's surface. Expect a small
**negative** number where an interference fit is intended (§4b) and a small **positive** one where
a pad is meant to bear. What you are hunting is the third case — a few millimetres of unintended
penetration, or a bracket floating clear of the thing it supposedly bolts to.
 
---
 
## 9. Running Blender
 
It is the **Windows** binary, driven headlessly from WSL.
 
```bash
BL="/mnt/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
"$BL" --background --python "$(wslpath -w tools/model/smoketest.py)" -- 'C:\Windows\Temp\out.png'
```
 
- `--python` needs `wslpath -w`, and outputs want `C:\...` paths.
- **Blender 5.2 has no `BLENDER_EEVEE_NEXT`** — use `BLENDER_EEVEE`.
- Run `tools/model/smoketest.py` first after any toolchain change.
- Previews land in `/mnt/c/Windows/Temp/airdefence-model`, readable from here — so the loop is
  *build geometry → render a PNG → read the PNG → adjust*, which is visually iterable rather than
  blind.
### Driving a live session instead
 
When the work is already open in Blender on the artist's machine, you can drive that session
rather than a headless one. Three things differ, and all three bite on the first call.
 
**Every `bpy.ops` call needs a context override.** The addon executes with no active object and no
area, so `mode_set`, `join`, `transform_apply`, `uv.smart_project` and `export_scene.gltf` all
fail with `poll() Context missing active object`:
 
```python
win  = bpy.context.window_manager.windows[0]
scr  = win.screen
area = next(a for a in scr.areas if a.type == 'VIEW_3D')
rgn  = next(r for r in area.regions if r.type == 'WINDOW')
with bpy.context.temp_override(window=win, screen=scr, area=area, region=rgn):
    ...
```
 
**`matrix_world` is stale until the depsgraph runs.** Set `parent` and read
`parent.matrix_world.inverted()` in the same breath and you get the identity back, which silently
offsets every child by the parent's location. It moved a whole part by `(-0.217, 0, 0.392)` and
every bound it reported afterwards looked perfectly plausible. Call
`bpy.context.view_layer.update()` before reading, or build the matrix yourself from
`Matrix.Translation(parent.location)`.
 
**Screenshots of the window can come back black.** Do not debug that; render to a temp file
instead and read the file. The build → render → look → adjust loop is the whole value of driving a
live session, so get it working before doing anything else.
 
Also: **leave the artist's file unsaved and say so.** Everything above is reversible until
somebody saves. Exported `.glb` and `.png` files are on disk regardless of whether the `.blend`
ever is, which is the right default — but it also means the authoring objects exist only in
memory, and that is worth saying out loud rather than assuming.
 
### The atlas is not byte-reproducible
 
Blender's exporter does not emit triangles in a stable order, so a rebuild from unchanged sources
gives a different file — same positions, normals and UVs, permuted index buffer. **`git status`
showing it modified after a build therefore means nothing.** Ask
`./tools/model/checkmesh.py <new> --compare <old>`, which compares the surface rather than the
bytes, and **revert the atlas** if it says the geometry is unchanged.
 
### Watch the atlas size
 
Bevels (§3) plus UV seams split far more vertices than the mesh's own count suggests, and the
`_VM` twin doubles whatever comes out. A 7.7k-face bracket exported at 2.1 MB. That is survivable
for one part and not for twenty. If an atlas looks fat, the first lever is bevel segments — 2 → 1
roughly halves it at almost no visual cost — and the second is facet counts on small cylinders.
 
---
 
## 10. Checklist for a new asset
 
1. **Get the proportions right first** (§3) — real dimensions, and measurements taken off whatever
   it mates to. Detail after, never instead.
2. **Model it.** A module beside the existing ones, called **last** from `pantsir.py`'s `main()`
   (box jitter, §4b). Or author it and write an importer.
3. **`checkmesh.py`** — zero-UV-area triangles and coplanar faces. Make sure the coplanar test
   includes the in-plane overlap check, or it will bury you in false positives.
4. **`checkswept.py`** — adrift, standing proud, passing through. Add the vehicle to `vehicles()`
   if it articulates about named axes; solve the travel in the model script if it does not. If the
   part rides an existing vehicle, re-run *that* vehicle's sweep too.
5. **Bake, if authored** (§6) — albedo and metalness through emission, AO with everything else
   hidden, then read all three maps back off disk and check them against the source materials.
6. **Reframe** (§2) — mounting face to the origin, then the rotation, then apply transforms.
   Confirm the X extent starts at 0. Do any mating part in the same sitting.
7. **Export**, then **verify the atlas by parsing it** (§1) — node names, absent node transforms,
   attributes, no Draco.
8. **Declare it** — a `<SubPart>` per moving body plus a `<Part>` in the asset XML, and a
   `<PartGameData>` with colliders, mass and editor tags.
9. **Register it** in `Sim/Arsenal.cs`, and remember there are **two** registries: `Launchers`
   (or `Optics`) says what it does, `Components` is what `WeaponSurvey` reads to recognise the
   craft as a weapons system at all. Missing from the second, it loads, resolves, matches — and is
   completely invisible, with nothing in any log.
10. **`validate-parts.py`** — ids, texture paths, and the geometry against the mesh. Teach it the
    new part's block; a number duplicated across a boundary with nothing checking it is the shape
    that has bitten this repo twice.
11. **`./tools/check-all.sh`** — everything CI runs.
12. **Fly it.** The suite cannot see what KSA does with the transforms. Record it in
    `CHECKLIST.md`, and do not describe a behaviour fix as fixed until it has been seen in game.
### Three editor gates that fail silently
 
Nothing logs when any of these is wrong; the part is simply absent or greyed out.
 
- **`IsAllowedAsRootPart` rejects a part if *any* connector is `ToSurface` or `FromSurface`.** So
  it is one or the other: a vehicle roots, a store rides.
- **A `<Connector>` with no `<Flags>` is a node connector.** `ToSurface` is the opt-in for radial.
- **Core's `Radial` tag stops anything being mounted *on* the part carrying it**, because the
  editor's face-snap target blacklist beats its whitelist. Right on a store; on a launcher it
  means no director can be fitted to it.
The third one is the trap for anything whose whole job is to carry something else — a rack, a
pylon, an adapter. It is the natural tag to reach for, because the part *is* radially attached.
It describes what may be mounted **on** it, not how it mounts. A rack tagged `Radial` accepts
nothing, forever, in silence.
 
`docs/KSA-MODDING-NOTES.md` has all three with the decompiled evidence.