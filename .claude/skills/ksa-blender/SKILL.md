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

---

## 6. The defects that are invisible outside the game

Both read as flickering white speckle crawling over the part, and **Blender's preview reproduces
neither**, because the preview material has no normal map wired in. Diagnose with the checker, not
by eye: the symptom is identical whichever cause it is.

```bash
./tools/model/checkmesh.py <atlas.glb>                    # exits non-zero
./tools/model/checkmesh.py <new.glb> --compare <old.glb>  # geometry diff, not a byte diff
```

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

### Clearance

```bash
./tools/model/checkswept.py     # adrift, standing proud, passing through, in travel
```

It sweeps **named axes**, so a freely-pointing head has none and its travel has to be solved
separately — sweep the moving body's own vertices against whatever it could strike. And it only
sweeps vehicles listed in `vehicles()`: a body set nobody named is not swept, and the tool still
prints "clear".

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
2. **Unwrap the whole part into one atlas** and bake Diffuse / Normal / ORM (§5).
3. **Export** with +Y Up off, all transforms applied, moving bodies recentred on their pivots.
4. **`checkmesh.py --near-max 0`** — zero-UV-area triangles and exact coplanar overlaps.
5. **`preview-glb.py`** — look at what actually left Blender.
6. **Declare it**: a `<SubPart>` per moving body, a `<Part>`, and a `<PartGameData>` with the collider
   read off `_ColPrim_`, mass and editor tags.
7. **Register it** in `Sim/Arsenal.cs` — and there are **two** registries. Missing from `Components`,
   a part loads, resolves, matches, and is completely invisible with nothing in any log.
8. **`validate-parts.py`**, then **`./tools/check-all.sh`**.
9. **Fly it.** The suite cannot see what KSA does with the transforms. Record it in `CHECKLIST.md`,
   and do not call a behaviour fix fixed until it has been seen in game.
