# Model pipeline

The launcher's art is **generated, not authored**. `pantsir.py` builds the whole Pantsir-S1 out
of primitives in headless Blender, `palette.py` writes the textures, and `build.sh` runs both
and installs the results into the mod. Nothing here needs the Blender UI, and there is no
`.blend` file to keep in sync — the script *is* the model.

```bash
./tools/model/build.sh              # textures, mesh, previews, install
./tools/model/build.sh --previews   # renders only; leaves the committed atlas alone
```

Outputs land in `src/AirDefence/`:

| File | What |
| --- | --- |
| `Meshes/AirDefence_MeshAtlas.glb` | `AirDefence_Subpart_{Chassis,Turret}` and their `_VM` previews |
| `Textures/AirDefence_{Diffuse,PBR,Normal}.png` | the palette |
| `tools/model/muzzles.json` | launch geometry, checked against `LauncherPart.cs` |

Previews (`preview_{3q,rear3q,side,front,top}.png`) go to `C:\Windows\Temp\airdefence-model`,
readable from WSL at `/mnt/c/Windows/Temp/airdefence-model`.

## The loop

Edit `pantsir.py`, run `build.sh`, **read the PNGs**, adjust, repeat. This is verified working
end to end — the model in the repo was built that way, not guessed at. `./tools/meshinfo.py`
on the exported GLB gives exact bounds, so "is the vehicle 3 m or 4 m wide" is a fact rather
than an opinion.

## Coordinate system

Part space, which is also glTF file space — the export runs with `export_yup=False`, so
Blender coordinates *are* what KSA reads out of the atlas and what `<Transform>` and
`<LocationAsmb>` in the part XML mean.

- **+X is up.** It is the part's forward axis in KSA's sense, and the battery's boresight.
- **+Y is the direction the vehicle drives.** The cab is at +Y.
- **+Z is the vehicle's right.**

The origin sits on the ground between the wheels, so every height in the script reads as a
height and the part rests on its underside when mounted.

Geometry is baked into the mesh at its final position: object transforms are applied and the
XML places both subparts at identity. Core's subparts are centred at the origin because they
are reusable pieces; ours are bespoke, so there is nothing to gain from an extra transform to
get wrong.

## Texturing without unwrapping

`palette.py` writes a 4×4 grid of flat swatches into a 512×512 atlas. Every primitive is
created with a swatch name and gets **all** its UVs written to that swatch's centre. So the
material is chosen per-primitive by name, there are no seams, no bakes, and no unwrap step.
128 px cells mean mipmapping never bleeds one swatch into its neighbour.

`AirDefence_PBR.png` is **R = ambient occlusion, G = roughness, B = metalness**. That is not a
guess: KSA's own `Content/Core/Textures/default_pbr.png` is `(255, 180, 0)` and
`EmptyAoRoughMetallic.png` is `(255, 255, 0)` — unoccluded, rough, non-metal — matching the
`<AoRoughMetal>` element name and the glTF ORM convention.

**KSA loads PNG for material slots**, so no `.ktx2` encoder is needed. `CharacterAssets.xml`
mixes `.ktx2` and `.png` in the same `<PbrMaterial>`. This is why `toktx` never had to be
installed.

## Launch geometry is generated too

The tube muzzle positions exist in two places: here, where the containers are placed, and in
`LauncherPart.cs`, which draws markers on them and spawns rounds from them. `build.sh` writes
`muzzles.json` and **`tools/validate-parts.py` fails if the two disagree** — this repo has
already been bitten twice by geometry duplicated across a boundary with nothing checking it.

After changing anything about the pods, rerun `build.sh` and paste the C# block it prints. If
the tube count changes, `Config.TubeCount` has to change with it.

## Every face needs UV *area* — this is the one that cost the most

The obvious way to use a palette atlas is to collapse all of a face's loops onto the swatch
centre. Same flat colour, no seams, trivial code. **It also makes the whole vehicle crawl with
flickering white speckle in game.**

A face whose loops share one UV has a zero UV derivative. A renderer building a tangent frame
from that gets a zero-length tangent; `normalize()` on it is NaN, and NaN survives being
multiplied by zero — so even a *flat* normal map cannot rescue the shading. The result is
garbage normals, per pixel, changing as the camera moves.

`project_to_swatch()` box-projects each face into a small patch around the swatch centre
instead. The patch is one flat colour, so orientation and scale are cosmetically irrelevant;
they only need to be non-degenerate. `UV_PER_METRE` and `SWATCH_REACH` keep the largest face on
the model well inside its cell.

**Blender's preview render does not reproduce this** — the preview material has no normal map
wired in, so the previews look perfect while the part sparkles in KSA. Run the checker:

```bash
./tools/model/checkmesh.py src/AirDefence/Meshes/AirDefence_MeshAtlas.glb
```

It reports zero-UV-area triangles and coplanar face pairs, and exits non-zero on either. It is
the only way to catch both classes of defect without restarting the game.

## Never let two primitives share a face plane

Laying a model out by naming shared edges — "the deck's underside is at the same X as the hull's
top" — reads naturally and is **wrong**. Two coplanar faces make the depth buffer pick a winner
per pixel per frame, and in game the whole vehicle crawls with white speckles. Blender's own
preview render does *not* show it, so the previews here will look clean while the part flickers
in KSA.

`box()` grows every box by `SKIN` plus a **per-box, per-axis jitter**. Both parts matter, and
the second is the one that is easy to miss:

- A uniform skin fixes faces that point **at each other**. It pushes them past one another.
- It does nothing for faces pointing the **same way**. Two boxes whose outer surfaces both sit
  on `DECK_X` move together under a uniform skin and stay exactly coplanar. Only a *different*
  inflation per box separates those. This accounted for most of the conflicts on this model.

**Cylinders do not go through `box()`**, so anything built with `cyl()` needs its clearance by
hand — and radius alone is not enough. Two coaxial cylinders have parallel side faces a couple
of millimetres apart, which fights even though nothing is coplanar. Use a different facet count,
or a `cone()`, whose sides are parallel to nothing. Growing the radius instead ran the tube
covers into their own neighbours.

Verify, don't tune by eye: `checkmesh.py` reports coplanar overlaps with their true intersection
area, plus parallel faces within a few millimetres. `build.sh` runs it and fails on either.

## The atlas rebuilds to a different file every time

`build.sh` is **not** byte-reproducible, and this is not a bug to chase. Blender's glTF
exporter does not emit triangles in a stable order, so rebuilding from unchanged sources
produces a GLB with identical positions, normals and UVs and a **permuted index buffer** —
a few hundred differing bytes, same size, same geometry.

So `git status` showing the atlas as modified after a build means nothing on its own, and a
byte comparison cannot answer the question you actually have. Ask it properly:

```bash
./tools/model/checkmesh.py <new.glb> --compare <old.glb>
```

which compares the surface itself — every triangle canonicalised and sorted — and tells you
whether an edit to `pantsir.py` moved anything. If it reports *same geometry*, **revert the
atlas** rather than committing several hundred bytes of noise into a binary file.

This was diagnosed by rebuilding three times and getting three hashes; only the turret differed,
and only in `INDICES`. Do not re-derive it.

## Four more traps, all already hit

- **Blender is a Windows binary.** A WSL path passed to `--python` gets mangled. Always
  `wslpath -w` the script path, and give Blender Windows paths (`C:\...`) for output.
- **Blender 5.2 renamed the render engine back.** `BLENDER_EEVEE_NEXT` does not exist; use
  `BLENDER_EEVEE`.
- **The add-primitive operators already make a UV layer.** Calling `uv_layers.new()` produces
  a *second* one and leaves the generated one first — which is the one the glTF exporter writes
  as `TEXCOORD_0`. The model comes out with the whole atlas smeared over every face:
  candy-striped tubes, magenta wheels.
- **`TRACK_TO` aligns its up axis to world Z**, and this part's up is world X, so every preview
  came out rolled ninety degrees. `look_at()` builds the camera matrix directly instead.

## Smoke test

`smoketest.py` builds two primitives, renders a PNG and exports a GLB. Run it first after any
Blender or WSL change — it proves the toolchain before you debug a model against a broken one.

```bash
BL="/mnt/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
"$BL" --background --python "$(wslpath -w tools/model/smoketest.py)" -- 'C:\Windows\Temp\out.png'
```

See `docs/KSA-MODDING-NOTES.md` for the part XML that consumes all of this.
