# tools/model

The headless generator and the checkers over what it exports. `pantsir.py` builds the Pantsir, the
LAU-7 rail and the EO director into one atlas sharing one palette material. Keep it working; do not
extend it — new art is authored in Blender over MCP, per `.claude/skills/ksa-blender/SKILL.md`.
`README.md` beside this has the full pipeline and the coordinate system; this file is the rules.

The CIWS is not among them any more: it is authored, into an atlas of its own. The generator still
draws its old boxes' jitter, so the director built after them keeps its planes.

The nuclear rack is not among them: all three of its bodies are authored, into one atlas sharing
one unwrap. It stopped instancing the generated beam when the B61-12 turned out to hang from
30-inch lugs where that beam's hooks are 14 inches apart — a MAU-12 carries both spacings, so the
authored rack carries both spacings.

Blender **5.2** is installed at
`/mnt/c/Program Files/Blender Foundation/Blender 5.2/blender.exe` and is driven entirely from
scripts — no viewport work. See `tools/model/README.md`; run `tools/model/smoketest.py` first
after any toolchain change.

```bash
BL="/mnt/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
"$BL" --background --python "$(wslpath -w tools/model/smoketest.py)" -- 'C:\Windows\Temp\out.png'
```

**The loop is**: build geometry → render a PNG → read the PNG here → adjust → repeat, with
`./tools/meshinfo.py` checking exported GLB bounds. Model work is therefore *visually iterable*
rather than blind, and the Pantsir was built through it.

`tools/model/README.md` has the full pipeline and the coordinate system. The traps:

- Blender is a **Windows** binary, so `--python` needs `wslpath -w` and outputs want `C:\...`.
- Blender 5.2 has no `BLENDER_EEVEE_NEXT` — use `BLENDER_EEVEE`.
- **Every face needs UV area.** Collapsing a face's loops onto one swatch centre — the obvious
  way to use a palette atlas — gives a zero UV derivative, hence a zero-length tangent, hence
  `normalize()` → NaN, hence garbage shading. `NaN * 0` is still NaN, so a flat normal map does
  not save it. The vehicle sparkles. `project_to_swatch()` gives each face a small projected
  patch instead.
- **Never let two primitives share a face plane.** Coplanar faces z-fight. `box()` inflates
  every box by a skin *plus a per-box jitter* — a uniform skin only separates faces pointing at
  each other, and does nothing for two boxes whose outer faces both sit on the same constant.
  `cyl()` does not inflate at all, and radius alone will not save a coaxial pair: use a
  different facet count or a `cone()`.
- **The jitter runs off one seed, so moving a `box()` call reshuffles every box after it.** Adding
  or removing one is enough, and the damage lands somewhere else entirely — pushing two faces in
  an unrelated assembly onto the same plane. To change which group a primitive belongs to without
  disturbing anything, set `_group` around the existing call rather than moving the call.

**The atlas is not byte-reproducible.** Blender's exporter does not emit triangles in a stable
order, so a rebuild from unchanged sources gives a different file — same positions, normals and
UVs, permuted index buffer. `git status` showing it modified after a build therefore means
nothing. Ask `./tools/model/checkmesh.py <new> --compare <old>`, which compares the surface
rather than the bytes, and **revert the atlas** if it says the geometry is unchanged.

- **Two bodies can share a plane, and `checkmesh.py` alone will not see it.** It analyses one
  mesh at a time, so a turntable resting exactly on the cap of its mast z-fights like any other
  coincident pair and reports clean — worse when the pair spins, because the fight then rotates.
  The cross-body pass lives in `validate-parts.py`, because the atlas carries **no node
  transforms** and only the part XML knows where each body sits. It reads the subpart's
  `<Rotation>` as well as its `<Position>`, and has to: a round seated on a rail is placed with a
  quarter turn carrying its nose onto the tube axis, so a pass using position alone lays the body
  *across* the launcher and finds nothing, because nothing is in contact.
- **A render only shows the poses it was asked for.** Geometry defects hide at the other ones:
  pods that pass through the gun sponsons at the twelve o'clock positions, tubes through the APU
  box at bearing 50°. `tools/model/checkswept.py` sweeps the drives and reports the metres one
  assembly would have to move to leave another. It needs neither Blender nor the game — the atlas
  is a library of bodies in their own local frames, so any pose is reconstructible from it plus
  `muzzles.json`.
- **It sweeps every articulated vehicle, and a new one has to be added to `vehicles()` by hand.**
  A body set it does not name is simply not swept, and the tool still prints "clear" — a vehicle
  with a traverse and an elevating head can have no coverage at all. A tool that reads the first
  entry looks correct until there is a second, which is the shape of the launcher registry and
  the travel reader too. **When a weapon system stops being the only one, check what still
  assumes it is.**
- **A piece can come adrift and every other check still passes.** The mesh is clean, the pivots
  agree, nothing intersects — the part simply stops touching what carried it and hangs in the
  air. `checkswept.py` requires every primitive of the assembled vehicle to reach the chassis
  through overlap. Per-*body* connectivity is the wrong test: the cannon are legitimately two
  islands that never touch each other, and the fins are twelve.
- **A cover must not stand proud of what it covers.** A cap a few millimetres wider than its tube
  catches the light as a rim all the way round, and one with fewer facets makes that rim visibly
  polygonal. It is far too small to see in a preview. `checkswept.py` flags a *short* coaxial
  primitive slightly wider than the long one it caps — short is what separates a mistake from a
  design, because a booster stage is legitimately fatter than its sustainer.
- **Bearing cancels for any pair of bodies that both ride the turret.** The traverse is one rigid
  motion applied to the whole group, so pods-vs-guns, pods-vs-turret and guns-vs-turret are
  one-degree-of-freedom problems in elevation alone. That is a fact about the chain, not a
  sampling shortcut, and it is what keeps the sweep cheap.
- **Elevation turns about +Z, so a gap in Z holds at every pose.** Separating two turret-riding
  bodies in Z is the only separation that survives both drives; separating them in X or Y only
  works at the elevation you checked.

**Neither shows up in Blender's preview render**, so a clean preview proves nothing.
`./tools/model/checkmesh.py <atlas.glb>` catches both and exits non-zero — run it after any
model change. Both defects look identical in game (flickering white speckle), so *diagnose with
the checker, not by eye*: the symptom points at z-fighting whether the cause is coplanar faces
or degenerate UVs, and inflating geometry that is already fine fixes neither.
