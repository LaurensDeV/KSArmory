---
name: ksa-blender
description: Build a 3D asset for Kitten Space Agency by authoring it in Blender over MCP. Use when modelling a new part, editing an existing one, importing a .glb somebody authored by hand, baking textures for one, or debugging art that renders wrong in game (sparkle, flicker, a subpart in the wrong place, a part that loads invisible). Covers the authoring loop, the export contract KSA actually enforces, and the defects that are invisible outside the game.
---

# Making art for KSA

**New assets are authored in Blender, over MCP, in a live session.** Not written as a headless
Python script, not driven through the Blender CLI. If the MCP connection is down, the answer is to
fix the connection — not to fall back to batch scripting.

The headless generator under `tools/model/` still builds six existing parts and still has to keep
working — §8 says what to know if you touch it. It is **not** the path for anything new.

> ## Stop before you bake
>
> **Ask the human to look at the geometry in Blender, and wait for a yes, before unwrapping,
> baking or exporting.** Say what you have built and what is next, then stop.
>
> Ask them to *look*, do not send them pictures. They have the document open — the model is right
> there, orbitable, at any angle, lit however they like, and their view of it beats any render
> you could hand over. Renders are how *you* see the model, because the protocol has no image
> channel and you are working blind without them. That is your problem, not theirs.
>
> The gate is not politeness. Everything downstream of the unwrap is welded to the geometry:
> bodies sharing an atlas pack together, so changing one body afterwards forces a re-unwrap and a
> re-bake of *all* of them. The AMRAAM paid that twice, once for a joint two primitives shared
> and once for a 2.5 mm recentre — both of which a glance would have caught while they were free.
>
> It is also the last cheap moment to judge the shape. Whether a silhouette reads as the right
> weapon is a matter of taste, and taste is not yours to sign off.

---

## 1. The authoring loop

The Blender Lab MCP addon (`blender.org/lab/mcp-server`) exposes exactly **one** request type:
`execute`. It runs Python inside the running Blender and hands back whatever that printed.

So this is not a toolbox of modelling verbs. It is an interactive Python channel into a session
somebody has open, with a viewport they are watching. That is the whole advantage over the old way,
and it changes how to work:

- **Build a piece, then look at it.** Small steps checked as you go, rather than a whole model
  emitted blind and judged from a render afterwards.
- **Read the scene back.** `execute` returns stdout, so measuring is a `print`: bounds, vertex
  counts, whether a modifier is still on the stack, what a UV island actually covers. Never assume
  a number you could ask for.
- **To see it, render to a file and read the file.** There is no image channel in the protocol.
  `bpy.ops.render.opengl(write_still=True)` to a path, then open the PNG.
- **It is somebody's open document.** Say what you are about to change before changing it, and do
  not silently delete objects they made.

`tools/model/preview-glb.py` renders an *exported* `.glb` from four angles without touching the
live session — the check on what actually left Blender, which is not always what you think you
built.

### When the connection is down

The addon listens on **127.0.0.1:9876** inside Blender's own process, and only once **Start MCP
Server** has been pressed in its sidebar. Two things reliably go wrong:

- **WSL cannot reach Windows loopback.** Across the default NAT boundary `127.0.0.1` is not shared.
  Either run the MCP server on the Windows side so the socket is local to it, or give WSL
  `networkingMode=mirrored` in `.wslconfig` and restart it.
- **Server and addon must be the same implementation.** Blender Lab's addon and the `blender-mcp`
  PyPI package are different projects that happen to share port 9876. Point the wrong one at it and
  the handshake half-succeeds, then blocks ~40 s on a command the addon has never heard of before
  failing the client's health check.

---

## 2. Where the source of truth lives

The generated parts have no `.blend` — the script *is* the model, and anyone can rebuild them from
a clean checkout.

An authored part inverts that. **The `.blend` is the source and it is not in the repository**; what
is committed is the export — the `.glb` and its textures. Two consequences, which are the price of
authoring rather than an argument against it:

- **Keep the `.blend` somewhere safe.** Losing it means the shipped asset can only ever be edited
  as raw geometry again.
- **A committed asset cannot be regenerated from a clean checkout**, so it has to be checked *at*
  the boundary. `checkmesh.py` and `validate-parts.py` are the only gate on art nobody can rebuild.

---

## 3. The export contract

What KSA reads out of a `.glb`, verified against all 44 Core atlases (1054 nodes).

| Rule | Evidence |
| --- | --- |
| **No node transform on a rendered subpart.** Translation, rotation and scale baked into the vertices. | Of 1054 Core nodes the only ones carrying a transform are `_ColPrim_*` helpers and floating-point dust (1e-16, 3e-07). |
| **Node name == mesh name.** | Holds for every rendered subpart in Core. |
| **A `_VM` twin of every subpart.** | 419 of Core's 1054 nodes. The editor's preview variant; duplicating the geometry is fine at these poly counts. |
| **`_ColPrim_*` carries the collider.** A unit box or cylinder with the volume in its node transform, never rendered, no UVs. | Core ships 58. Read `<Collider>` out of it rather than eyeballing one. |

**Author to this and there is no import step.** The suspension rail satisfied all of it and was
copied in as exported. The targeting pod satisfied none of it and needed
`tools/model/import-litening.py` to bake, reframe, recentre and clock it. The difference is
entirely this table, and it is far cheaper to get right in Blender than to correct afterwards.

A part that loads but renders nothing is nearly always a name mismatch — `<Mesh Id="…">` must be the
atlas name exactly. Silent in game; caught by `./tools/validate-parts.py`.

### Node name == mesh name is not something you can just set

Blender will not tell you when it refuses a name. `ob.data.name = "X"` silently becomes `"X.001"`
if an orphaned mesh already owns `"X"`, and orphans are ordinary: deleting an object leaves its
mesh datablock behind, so rebuilding a body during iteration is enough to produce one. The export
then writes a node called `X` whose mesh is `X.001`, which breaks the contract above.

Two habits close it:

- **Purge before naming, name before exporting.** `bpy.ops.outliner.orphans_purge(do_local_ids=
  True, do_linked_ids=True, do_recursive=True)`, then set `ob.data.name = ob.name`, then export.
- **Read the file back and assert it**, because the only other symptom is a part that renders
  nothing in game. `checkmesh.py` does this now and fails on a mismatch, so running it after every
  export is the check — see §6.

> **A purge is not free.** It deletes every datablock nothing currently points at, which includes
> bake target images between passes and a material you have unassigned for a moment. Give images
> `use_fake_user = True` before you purge, or expect to rebake. This is how the AMRAAM lost its
> materials mid-session.

### Exporter settings

```
Format            glTF Binary (.glb)
+Y Up             OFF          <-- the one that matters, see §4
Apply Modifiers   ON
Include           Selected Objects (or the collection)
Data              Mesh: UVs, Normals.  No cameras, no lights
Compression       off
```

"+Y Up off" fixes the axes but does **not** clear object transforms.
`bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)` over the selection, or
`Object > Apply > All Transforms`.

---

## 4. Coordinate system

Exporting with **+Y Up off** means Blender coordinates *are* what KSA reads, and what `<Transform>`
and `<LocationAsmb>` mean. Leave it on and Blender swaps Y and Z: the Pantsir exports three metres
long and eight wide, and nothing warns you.

**Part space:**

- **+X out of the surface the part attaches to.** Up for something that stacks; for a store under a
  wing it points *away from the pylon*, downward.
- **+Y along the host's long axis.** A missile's or a pod's nose points +Y.
- **+Z the host's right.**

The origin sits on the mounting face, so every coordinate reads as an offset from where it bolts
on. **Model in this frame from the start.** A model authored to another convention needs a rotation
baked into the vertices — it cannot be parked in the XML, because the mod rewrites the
`<Transform>` of every moving subpart each frame and overwrites it on the first update.

---

## 5. Materials and textures

```xml
<PbrMaterial Id="…">
  <Diffuse      Path="Textures/….png" Category="Vessel" />
  <Normal       Path="Textures/….png" Category="Vessel" />
  <AoRoughMetal Path="Textures/….png" Category="Vessel" />
</PbrMaterial>
```

- **PNG is fine.** KSA loads PNG and `.ktx2` and mixes them in one material. No encoder needed.
- **`AoRoughMetal` is R = occlusion, G = roughness, B = metalness** — the glTF ORM convention. Not a
  guess: Core's `default_pbr.png` is `(255, 180, 0)`.
- **Several atlases and several materials per mod are fine.** Core does it.
- **Keep the asset XML at the mod root.** `<MeshAtlas Path="…">` is relative and it is undocumented
  whether it resolves against the mod root or the XML's own directory; at the root both readings
  agree. Asset *folders* below it can be reorganised freely — only moving the XML reopens that.

**One material per part, not one per body.** The pod shipped three 2048² sets for three bodies —
nine files, 8.7 MB — where one unwrap across all three is three files and one material. Unwrap the
whole part into a single atlas before baking.

### Unwrapping several bodies into one atlas

Select them all, edit them together, and let Blender pack them as one set: `smart_project`, then
`average_islands_scale` so texel density is uniform across bodies, then `pack_islands`. Ask
`uv.select_overlap` afterwards and count the selected faces per object — zero, or two islands are
sharing texels and one will bake over the other.

The AMRAAM's rail and round pack to 95% coverage of a 1024² this way, which is the argument
against a set per body.

### Baking, and the four traps in it

Build the material out of **object-space coordinates** rather than painting in UV space. A
`ColorRamp` on constant interpolation, driven by the object's own X, gives every band on a missile
at once and does not care where the islands landed — so a repack cannot smear the markings. Its
one trap is the mirror of that strength: translating the mesh afterwards moves the pattern
relative to the geometry, so a recentre means a rebake.

Then, in order:

1. **`DIFFUSE` with `pass_filter={'COLOR'}`** — no lighting baked in.
2. **`ROUGHNESS`** and **`AO`** into scratch images. AO at ~64 samples; the rest need one.
3. **Metalness has no bake pass.** Route whatever feeds Principled's Metallic into an `Emission`
   shader, point the material output at it, bake `EMIT`, then put the BSDF back.
4. **Compose ORM yourself**: R = AO, G = roughness, B = metalness. Fill the background with
   `(1.0, 0.5, 0.0)` and copy in only where roughness is non-zero, so unbaked texels are not
   fully occluded black.

The traps, all of which fail quietly:

- **`bpy.ops.object.bake(use_clear=…)` overrides `scene.render.bake.use_clear`.** Baking a second
  object into the shared image wipes the first one's islands unless you pass `use_clear=(i == 0)`
  on the operator itself. The scene setting is ignored when the operator is called from Python.
- **Bake each body with the others hidden** (`hide_render`). Ray-traced passes see the whole
  scene, and bodies sitting in their own export frames intersect each other — the AMRAAM's round
  passes straight through its rail's pylon at the origin. A round is not occluded by a rail it
  has left, either.
- **`is` does not work on Blender RNA wrappers.** `bpy.data.materials[…].node_tree.nodes["…"]`
  returns a fresh Python object each access, so `link.to_node is bsdf` is `False` while the link
  plainly exists. Compare with `==`, or by name. This silently baked an all-zero metalness map.
- **Check the result, do not assume it.** `min`, `mean` and `max` over the pixels costs one line
  and is the difference between a bad map and a bad map you shipped.

---

## 6. The defects that are invisible outside the game

Both read as flickering white speckle crawling over the part, and **Blender's preview reproduces
neither**, because the preview material has no normal map wired in. Diagnose with the checker, not
by eye: the symptom is identical whichever cause it is.

```bash
./tools/model/checkmesh.py <atlas.glb>                    # exits non-zero
./tools/model/checkmesh.py <a.glb> <b.glb> --near-max 0   # several at once; each is reported
./tools/model/checkmesh.py <new.glb> --compare <old.glb>  # geometry diff, not a byte diff
```

It also checks the **node/mesh name pairing** from §3, which is the one contract violation with no
visible symptom outside the game.

**Zero UV area.** A face whose loops share one UV has a zero UV derivative, so the tangent is
zero-length, `normalize()` gives NaN, and NaN survives being multiplied by zero — a flat normal map
cannot rescue it. Watch for collapsed faces from a UV sphere's poles, from booleans and from
decimation: 144 of the pod's triangles were degenerate in 3D *and* UV, and simply had to go.

**Coplanar faces.** Two surfaces on one plane make the depth buffer pick a winner per pixel per
frame. In authored work this is a modelling habit rather than a script bug: sink one surface into
the other rather than butting them, and give a cap a different facet count from the tube it caps.

> **`--near-max` is for authored meshes.** The default near-coplanar band is calibrated to the
> generator's 8 mm modelling skin, so on a hand-built model it reports every deliberate panel step
> and shell wall — 451 on the pod, none of them mistakes. `--near-max 0` silences that advisory
> while leaving zero-UV-area and *exact* coplanar overlaps checked in full, because neither of those
> is ever deliberate. Record the residual somewhere a flight will settle it.

---

## 7. Articulation, and the part around it

Writing a subpart's transform each frame moves it. Confirmed in game.

- **`ResetCachedPosMatrixValues()` after the write**, or `Part` serves its cached matrix and the new
  value is silently ignored.
- **A subpart rotates about its own mesh origin.** So export a moving body *recentred on its pivot*
  and put the offset back with `<Position>`. Three copies of that pivot then exist — the model, the
  XML and `Sim/Arsenal.cs` — and `validate-parts.py` is what stops them drifting.
- **Bodies turning on one bearing share one pivot.** The pod's shell and ball do, which is what lets
  the ball sweep its travel without leaving the shroud.
- **SubParts do not nest.** Every `<SubPart>` is placed against the `<Part>`; the mod composes the
  rotations itself.
- **Model a body at its working pose**, and apply motion as a rotation away from that reference. A
  refused write then leaves the part looking right rather than inside-out.

**A shipped part's subpart list is append-only.** KSA pairs a saved part with its definition
positionally, so removing a `<SubPart>` throws `IndexOutOfRangeException` from inside
`Popup.DrawAll` and **terminates the game** on every save holding that part. Adding and renaming are
free.

### Three editor gates that fail silently

- **`IsAllowedAsRootPart` rejects a part if *any* connector is `ToSurface`/`FromSurface`.** A vehicle
  roots; a store rides. Pick one.
- **A `<Connector>` with no `<Flags>` is a node connector.** `ToSurface` is the opt-in for radial.
- **Core's `Radial` tag is a `FaceSnapTargetBlacklist`, and the blacklist beats the whitelist** — so
  nothing can be mounted *on* a part carrying it. Right for a store, wrong for anything meant to
  carry one.

`docs/KSA-MODDING-NOTES.md` has all three with the decompiled evidence.

**A round's mesh must be *centred* on its origin, not merely modelled about it.** Fire control
seats a round half a `MunitionProfile.BodyLength` back from the tube mouth, so it takes the mesh
origin for the body's centre. The AMRAAM's ogive ended 2.5 mm further forward than its base ended
aft, which put the seated round that far off where it fires from. Invisible, and checked now by
`validate-parts.py`.

### Clearance

```bash
./tools/model/checkswept.py     # adrift, standing proud, passing through, in travel
```

It sweeps **named axes**, so a freely-pointing head has none and its travel has to be solved
separately — sweep the moving body's own vertices against whatever it could strike. And it only
sweeps vehicles listed in `vehicles()`: a body set nobody named is not swept, and the tool still
prints "clear".

**A part with nothing that moves gets nothing from `checkswept.py`.** Its bodies still share
planes, though, and `checkmesh.py` reads one mesh at a time — so the check that matters there is
the cross-body plane pass inside `validate-parts.py`, which places each body as the part XML does
and looks for faces two *different* subparts share. It reads `<Rotation>` as well as `<Position>`,
which it has to: a round seated on a rail is placed with a quarter turn onto the tube axis, and a
pass using position alone lays the body across the launcher and finds nothing.

**And `_ColPrim_` is a source of numbers, not something KSA reads out of your `.glb`.** The engine
takes the collider from `<Collider>` in the GameData XML. Authoring the node is Core's convention
and a convenient place to measure from; declaring the box from the mesh bounds is equally valid,
and is what the AMRAAM rail does.

---

## 8. The generated parts that already exist

Six parts — the Pantsir, the CIWS, the Mk 82 and B61 racks, the LAU-7 rail and the EO director — are
built by `tools/model/pantsir.py` and its modules into one atlas sharing one palette material.
**Keep it working; do not extend it.**

If you do have to touch it:

- `./tools/model/build.sh` rebuilds the atlas and textures. Blender is a **Windows** binary, so
  `--python` needs `wslpath -w` and outputs want `C:\…` paths. Blender 5.2 has no
  `BLENDER_EEVEE_NEXT` — use `BLENDER_EEVEE`.
- **`box()` inflates every primitive by a skin plus a per-box jitter**, which is what keeps faces off
  each other's planes. The jitter runs off one seed, **so inserting or moving a `box()` call
  reshuffles every box after it** and the damage lands somewhere unrelated. Append new groups last,
  and set `_group` around an existing call rather than moving the call.
- `cyl()` gets no jitter, so coaxial primitives need a different facet count or a `cone()`.
- The palette trick — every face UV'd to one flat swatch — is why there is no unwrap step. Right for
  parametric geometry, wrong for anything wanting observed detail, which is the whole reason new
  work is authored.
- **The atlas is not byte-reproducible.** The exporter does not order triangles stably, so `git
  status` showing it modified means nothing. Ask `checkmesh.py --compare` and revert if the geometry
  is unchanged.

---

## 9. Checklist for a new asset

1. **Model it in Blender over MCP**, in part space (§4), to the export contract (§3).
2. **Ask the human to look at it in Blender, and wait.** The geometry is signed off or it is not
   finished. Nothing below this line is cheap to redo — see *Stop before you bake* above.
3. **Unwrap the whole part into one atlas** and bake Diffuse / Normal / ORM (§5).
4. **Export** with +Y Up off, all transforms applied, orphans purged, moving bodies recentred on
   their pivots and rounds centred on their own.
5. **`checkmesh.py --near-max 0`** — node/mesh names, zero-UV-area triangles, coplanar overlaps.
6. **`preview-glb.py`** — look at what actually left Blender. It renders the file *naively*, node
   transforms and all, so a multi-body atlas shows each body in its own frame: a rail along +Y and
   its round along +X will cross each other, and that is correct rather than a fault.
7. **Declare it**: a `<SubPart>` per moving body, a `<Part>`, and a `<PartGameData>` with the collider
   read off `_ColPrim_` or off the mesh bounds, mass and editor tags.
8. **Register it** in `Sim/Arsenal.cs` — and there are **two** registries. Missing from `Components`,
   a part loads, resolves, matches, and is completely invisible with nothing in any log.
9. **`validate-parts.py`**, then **`./tools/check-all.sh`**.
10. **Deploy it, and look at what you are replacing first.** `./tools/deploy.sh` overwrites the
    installed mod wholesale, so a build from a worktree quietly removes whatever the last build
    had that yours does not. Check the branch and the mods folder before installing over them.
11. **Fly it.** The suite cannot see what KSA does with the transforms. Record it in `CHECKLIST.md`,
    and do not call a behaviour fix fixed until it has been seen in game.
