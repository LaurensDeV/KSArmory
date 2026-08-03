# CLAUDE.md

A point-defence mod for **Kitten Space Agency** (KSA, RocketWerkz) — a Pantsir-S1 with search
radar, proportional-navigation interceptors and a proximity-fused warhead. Twelve rounds, two
pods of six, on an 8×8 chassis the mod generates from a Blender script.

## Read this first

**`docs/KSA-MODDING-NOTES.md` is the distilled result of reverse-engineering the game.** It has
the runtime, the loader contract, the type signatures, the reference frames and the gotchas.
Read it before touching anything KSA-facing — it will save you an hour of decompiling.

KSA has **no official code-modding API**. Everything is community tooling against a pre-release
game, so the API moves between builds.

## Environment

- **KSA install**: `/mnt/c/Program Files/Kitten Space Agency` (Windows game, WSL dev)
- **KSA build these notes were taken against**: `2026.8.3.5117`
- The system `dotnet` is 8.0 and **cannot build this** — the mod targets **net10.0**
  (`error NETSDK1045`). A .NET 10 SDK is installed at `~/.dotnet`.
  **Use `tools/build.sh` / `tools/test.sh`**, which source `tools/env.sh` to fix PATH. Bare
  `dotnet` commands fail. In an interactive shell, `source tools/env.sh` once.

- `Import/` holds the game's assemblies and is **gitignored**. Repopulate with
  `./tools/sync-import.sh`. Nothing builds without it.
- **The game is launchable from WSL** — interop is enabled, so `tools/run.sh` starts
  `StarMap.exe` directly. StarMap lives at `/mnt/c/Users/devoo/StarMap` and reads
  `./StarMapConfig.json` **relative to its own directory**, so it must be launched from there.
- **The mod writes its own log** to `<KSA user dir>/Logs/AirDefence.log`, readable from WSL.
  `Console.WriteLine` only reaches stdout, and KSA's `KittenSpaceAgency.log` is written by its
  internal logger which mods cannot reach — so the mod's own file is the debugging channel.
  KSA's log is still the place to look for mod discovery and asset/XML errors.

## Commands

```bash
./tools/build.sh                           # build the mod (handles the SDK PATH)
./tools/test.sh                            # guidance + fuse tests, no game needed
./tools/validate-parts.py                  # check part XML + launch geometry -- run after editing either
./tools/model/build.sh                     # rebuild the Pantsir mesh and textures (needs Blender)
./tools/deploy.sh                          # build and install into the KSA mods folder
./tools/run.sh                             # build, deploy, launch, show the mod's output
./tools/run.sh --attach                    # follow a game that's already running
./tools/setup-starmap.sh                   # one-off: install StarMap and write its config
./tools/sync-import.sh                     # refresh Import/ after a KSA update

source tools/env.sh                        # then bare dotnet works in this shell
cd tools/apidump && dotnet run -- ../../Import members KSA.Vehicle   # inspect the game API
./tools/meshinfo.py "<KSA>/Content/Core/Meshes/CoreStructuralA_MeshAtlas.glb" Tube  # mesh bounds
```

## Layout

| Path | What |
| --- | --- |
| `src/AirDefence/AirDefenceMod.cs` | StarMap entry point and frame hooks |
| `src/AirDefence/KsaWorld.cs` | most KSA contact is funnelled here — keep it that way |
| `src/AirDefence/DefenceBattery.cs` | fire control, salvo logic, warhead effects |
| `src/AirDefence/Radar.cs` | cone search, CPA threat model, lock |
| `src/AirDefence/LauncherPart.cs` | finds the launcher part, resolves muzzle positions |
| `src/AirDefence/Interceptor.cs` | round physics, proportional navigation, fuse — KSA-free |
| `src/AirDefence/Turret.cs` | rate-limited azimuth drive — KSA-free |
| `src/AirDefence/Vec.cs`, `Config.cs` | vector helpers, tunables — KSA-free |
| `src/AirDefence/Ui.cs`, `Visuals.cs` | ImGui panel, gizmo rendering |
| `src/AirDefence/AirDefence*.xml` | the launcher part — at the mod root, mirroring Core |
| `src/AirDefence/Meshes/`, `Textures/` | generated art; rebuild with `tools/model/build.sh` |
| `src/AirDefence/mod.toml` | serves as both the content-mod and StarMap manifest |
| `tests/AirDefence.Tests/` | links the KSA-free sources and flies engagements headlessly |
| `tools/apidump/` | reflection dumper for the game assemblies |
| `tools/meshinfo.py` | prints mesh bounds from a KSA `.glb` atlas |
| `tools/validate-parts.py` | checks asset Ids, texture paths, and launch geometry vs the mesh |
| `tools/model/` | headless Blender scripts that generate the Pantsir |
| `tools/model/checkmesh.py` | finds zero-UV-area triangles and coplanar faces in a `.glb` |
| `tools/screenshot.sh` | captures the Windows screen; readable from here |

## 3D model pipeline (Blender, headless)

Blender **5.2** is installed at
`/mnt/c/Program Files/Blender Foundation/Blender 5.2/blender.exe` and is driven entirely from
scripts — no viewport work. See `tools/model/README.md`; run `tools/model/smoketest.py` first
after any toolchain change.

```bash
BL="/mnt/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
"$BL" --background --python "$(wslpath -w tools/model/smoketest.py)" -- 'C:\Windows\Temp\out.png'
```

**This loop is verified working**: build geometry → render a PNG → read the PNG here → adjust →
repeat, with `./tools/meshinfo.py` checking exported GLB bounds. Model work is therefore
*visually iterable* rather than blind — use it the same way the diagnostic dump was used for the
simulation bugs. **The Pantsir was built this way**, over about six render-and-adjust rounds.

`tools/model/README.md` has the full pipeline, the coordinate system and five traps that have
each already cost time. Three worth repeating here:

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

**Neither shows up in Blender's preview render**, so a clean preview proves nothing.
`./tools/model/checkmesh.py <atlas.glb>` catches both and exits non-zero — run it after any
model change. Both defects look identical in game (flickering white speckle), so *diagnose with
the checker, not by eye*: the first attempt at this was spent inflating geometry that was
already fine, because the symptom pointed at z-fighting and the real cause was the UVs.

### Runtime part transforms work — the turret traverses and the pods elevate

**Confirmed in-game: writing a subpart's transform each frame moves it.** That was the last big
"the API allows it, does the engine agree" unknown, and the answer is yes.

**Subparts are `Part` objects in their own right.** `Part.SubParts` is a `ReadOnlySpan<Part>`,
each with settable `Asmb2ParentAsmb` *and* `PositionParentAsmb` — so the launcher stays a single
part in the editor and still articulates.

What it depends on, all worth not rediscovering:

- **`ResetCachedPosMatrixValues()` must be called after the write.** `Part` caches
  `_matrixAsmb2Parent` and friends; without the reset the new value is stored and ignored.
- **A subpart rotates about its own mesh origin.** The turret and pod meshes are exported
  recentred on their pivots (`TURRET_PIVOT`, `POD_PIVOT` in `pantsir.py`) and put back with
  `<Position>` in the XML. Without that, a subpart swings round the chassis like a wrecking ball.
- **SubParts do not nest.** The asset XML places every `<SubPart>` against the `<Part>`, so the
  pods and the search array are *siblings* of the turret, not children. The mod composes the
  rotations itself and rewrites their `PositionParentAsmb` every frame — otherwise they would
  turn on the spot while the turret rotated out from under them.
- **The pods are modelled at their working elevation, not flat**, and runtime elevation is
  applied as a rotation *away* from that reference. A refused write then leaves the vehicle in
  a pose that looks right rather than with its tubes through the tracking radar.
- **`Vehicle.Asmb2Ego` doubles as world→part orientation.** Ego is a pure translation of Ecl, so
  for a *direction* the two agree exactly, and its conjugate turns a world bearing into the part
  frame the drives work in.

Tube offsets are emitted **pod-local**, so the launch markers ride both axes for free — and
`Visuals.DrawLoadedTubes` must resolve them through `PodsPart`, **not** `TurretPart`. Passing
the turret compiles, looks right, and is very nearly right: the markers still follow the
traverse and simply refuse to go up and down with the pods.

**The pods will not depress into the vehicle's own bodywork.** `Turret.DepressionFloorAt`
raises the elevation floor across a forward arc, where the pods would otherwise swing down
through the APU box behind the cab. It eases in across the arc edge, and is enforced against
the *current* bearing in `Update` so traversing into the forward sector lifts the pods on the
way round rather than on arrival. A flat depression limit everywhere would be simpler and
worse — off the beam the pods can legitimately come down to level, which is the shot against
anything skimming the horizon.

`Visuals` draws a cyan line along where the drives think they point. It stays: it is what
separates "the maths is wrong" from "the engine ignored the write", and it cost a restart to
learn that distinction matters.

Four moving pieces now: chassis (fixed), turret (traverses), pods (traverse + elevate), and the
**search array**, a double-sided hexagonal wedge that turns continuously off the clock rather
than off the track — it is a search set, so it never stops and never aims. Its two hex faces are
clocked 30° apart because hexagons rotated alike put their flats on the same planes, and the
faces lean toward each other far enough for those planes to overlap.

Still fixed: **boresight is local "up"**, not the launcher's facing — the radar sweeps a
hemisphere regardless of where the tubes are aimed, and the spinning array is cosmetic.

## Design decisions worth not re-litigating

**Rounds are simulated by this mod, not by KSA's vehicle physics.** They are drawn with
`GizmosRenderer` rather than being real part-based vehicles. This was deliberate: spawning real
vehicles needs a part template (GLB model, XML schema, and registering a module type into
engine-internal update lists that would require Harmony patching), and steering them means
writing kinematics from a worker-thread update. Self-simulating gives sub-frame accuracy for
free and cannot corrupt a save. The cost is that rounds look like tracer spheres with trails.
Swapping in real part-based missiles later means replacing `Visuals.DrawRounds` and the
integration in `Interceptor`, nothing else.

**Everything is computed in the ecliptic (`Ecl`) frame** and converted to camera-relative `Ego`
only at draw time. `Ego` is a pure translation of `Ecl`, so this is exact — see the notes.

**Threat classification uses closest point of approach, not closing speed.** That is what makes
targets *passing by* engageable and not just ones flying straight at the battery. This was an
explicit requirement.

**`Interceptor.cs`, `Vec.cs`, `Config.cs`, `DrawAnchor.cs`, `Turret.cs` and `FireGeometry.cs`
must stay free of KSA types.** The test project links those files directly so guidance,
anchoring, the turret drive and launch geometry can be exercised on Linux with no game present.
Adding a `using KSA;` to any of them breaks the tests. When something KSA-facing turns out to
have testable maths inside it, split the maths out rather than leaving it untestable —
`FireGeometry` came out of `LauncherPart` exactly that way.

**Rounds are drawn as real subparts, anchored to the tube they left.** Twelve `Missile`
subparts, scaled to nothing until fired, with their transform written each frame. Two rules,
both learned the hard way:

- **Anchor to the tube, add only the travel *since* launch.** `OffsetFromPlatform` is measured
  from the platform's *analytic* orbit position; a subpart is placed against the vehicle's
  *physics* origin. Those differ by metres on a landed craft — the same distinction
  `DrawAnchor` exists to preserve — and using the absolute offset put every round inside the
  search radar. `Interceptor.TravelSinceLaunch` is a difference between two positions in one
  frame, so it carries none of that.
- **Orient off `VelocityLocal`, never `VelocityEcl`.** The latter carries ~29.8 km/s of ecliptic
  motion and points every round the same way.

`RoundBodyAnchorTests` and `FireGeometryTests` hold both, and both were checked by
reintroducing the bug and watching them fail.

**A fully self-contained scenario is not possible from a mod.** `LoadVehicleFromLibrary` in a
system XML resolves through `DefaultVehicleSaves`, whose `SaveFolderPath` is **hardcoded** to
`Content/Core/defaultvehicles` under the game install — not per-mod, and not writable without
elevation. So a one-click "everything placed and ready" scenario would mean writing into
Program Files. Instead: `tools/install-testcraft.sh` writes a craft into the *user's* vehicle
folder (which is writable), and `TestTarget` spawns drones on demand from the panel.

**The battery mounts to the craft carrying the launcher part, and stays there.** It does not
follow the player's control. It used to, from before the part existed, and that meant taking the
target's seat re-homed the battery onto the target — which then could not be shot at, because
the kill path refuses to destroy its own platform. Four confirmed 22 m hits looked like misses.
`PinPlatform` is now only an override for choosing between multiple launcher-equipped craft.

**The draw anchor uses two different instants on purpose.** `DrawAnchor.Ego` is sampled this
frame; `DrawAnchor.Ecl` is the platform position the geometry was measured against, one update
earlier. The difference between them *is* the frame's ecliptic motion (~500 m at 60 fps), and
differencing against the older reference is what cancels it. **Collapsing them into one sample
looks like a tidy-up and puts the entire overlay beside the craft.** That has now happened
twice. `DrawAnchorTests` fails if it happens again — read `DrawAnchor.cs` before touching it.

**Kills are binary.** KSA exposes no partial-damage model, only
`Universe.DestroyVehicleFromEvent`. `LethalRadius` destroys; between lethal and `BlastRadius`
the mod logs a near miss and the target survives.

**The launcher ships its own art, and the asset XML lives at the mod root.** It used to
instance Core's meshes by Id and ship nothing — that worked, and is still the right answer for
a part that can be assembled from Core's kit, but a Pantsir cannot. The mod now carries
`Meshes/AirDefence_MeshAtlas.glb` and three PNGs, declared with `<MeshAtlas>` and
`<PbrMaterial>` exactly as Core does.

The XML sits at `src/AirDefence/*.xml` rather than in an `Assets/` subfolder **on purpose**.
`<MeshAtlas Path="Meshes/…">` is relative, and it is not documented whether it resolves against
the mod root or against the XML's own directory. With the XML at the root those are the same
directory, so the question never has to be answered. Moving it back into a subfolder reopens a
silent-failure mode.

`Textures` are **PNG, not `.ktx2`** — KSA loads both, and `CharacterAssets.xml` mixes them in
one material. No `toktx` needed.

Run `./tools/validate-parts.py` after touching any of it: a bad Id or path is a *silent*
in-game failure. It now also checks mesh Ids against the atlas and texture paths against disk.

**The part is inert; the behaviour is in C#.** KSA sees structure with mass and a collider.
`LauncherPart.Find` looks for it on the vehicle and the battery mounts there. This sidesteps
registering a custom module type into the engine's internal update lists, which is not
reachable without patching.

**Launch and slew geometry live in the Blender script, not in the C#.**
`tools/model/pantsir.py` places the containers and writes `muzzles.json`;
`LauncherPart.TubeOffsetsPodFrame`, `MuzzleForwardOffset`, `TubeRingRadius`, `PodPivotFromTurret`,
`TurretPivotInPart` and `PodReferenceElevationRad` are all pasted from what it prints.
`validate-parts.py` **fails if any of them disagree** — this is the third piece of geometry in
the repo duplicated across a boundary, and the first two both drifted. Change the pods, rerun
`tools/model/build.sh`, paste the block. If the tube count changes, `Config.TubeCount` changes
with it.

**The battery will not fire while the launcher is slewing.** `DefenceBattery.IsLaid` requires
both axes on target for `TurretSettleSeconds` first. Before that gate existed it launched the
instant it had a lock, out of tubes still pointing somewhere else — guidance recovered and the
intercepts still landed, so nothing measured it and only watching it caught it. `IsLaid` returns
true whenever nothing is driving the turret, so it can never deadlock fire control.

**The class is `DefenceBattery`, not `Battery`.** `KSA.Battery` already exists as the game's
electrical battery, and these files have `using KSA;`.

## Testing

**Nothing has been verified in-game.** `CHECKLIST.md` is the manual test plan, ordered by risk;
update its tick-boxes and the risk table as items are confirmed or disproved.

`tests/AirDefence.Tests` flies whole engagements headlessly. `GuidanceDiscriminationTests` is
load-bearing: it asserts that the crossing-target scenario **misses** with the nav constant
turned off. Without it, a hit test can silently pass on a geometry that never needed a lead.
Keep that guard if you change the test geometry.

## Not done

- Whether round bodies survive at long range is unproven. They are subparts of a vehicle they
  fly kilometres away from, so the engine may cull or clamp them; the gizmo tracers stay on as
  a fallback and `DefenceBattery.RoundBodiesWork` turns the whole thing off if a write is
  refused.
- The guns do not move. They are fixed in the turret mesh, so they traverse but never elevate.
- Radar boresight is local "up" regardless of where the launcher is aimed, so the search volume
  does not follow the turret.
- The model has no normal or occlusion detail — flat palette swatches only. Faceted lighting is
  the whole look, which suits KSA's art style, but it is a floor not a ceiling.
- Rounds do not collide with terrain or structures, only their designated target.
- Radar has no line-of-sight or occlusion check.
- No save/load persistence of battery state; settings reset each session.
