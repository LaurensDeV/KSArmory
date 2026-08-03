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

## Committing

**Every commit message must be a [Conventional Commit](https://www.conventionalcommits.org/).**
This is not a style preference — semantic-release parses these to decide the next version, so a
message that does not parse silently produces no release and never appears in the changelog.

```
feat(turret): elevate the pods on their trunnions
fix(rounds): anchor bodies to the tube they left, not the orbit position
docs: write an install guide
refactor(sim): split launch geometry out of LauncherPart
```

| Type | Version effect |
| --- | --- |
| `fix`, `perf`, `build`, `revert` | patch |
| `feat` | minor |
| `!` after the type, or a `BREAKING CHANGE:` footer | **major** — see the 1.0.0 note below before using this |
| `docs`, `refactor`, `test`, `chore`, `ci`, `style` | no release (`docs` and `refactor` still reach the changelog) |

Scope is optional but useful; prefer the area touched — `turret`, `rounds`, `radar`, `sim`,
`model`, `ci`. Keep the subject in the imperative and under ~72 characters, and use the body to
say *why* when the reason is not obvious from the diff.

**Pick the type by asking whether a player would notice, not by how much work it was.** The
scope is not consulted: `feat(tools)` on a developer script is still a `feat`, so it bumps the
*mod's* minor version and publishes an archive identical to the previous one but for the
version string. That has already happened twice — 0.1.1, 0.2.0 and 0.3.0 differ only in
`<Version>`, and anyone who upgraded got nothing. Developer tooling is `chore`, `ci`, `test` or
`refactor`. The commit-msg hook warns when a `feat`/`fix`/`perf` commit touches nothing under
`src/AirDefence/`; it only warns, because a packaging fix in `tools/package.sh` genuinely
changes what ships without touching `src/` and no mechanical rule gets that right.

Split unrelated work into separate commits rather than one large one: the changelog is generated
from these, so a commit that does three things describes none of them well.

**This is enforced.** `tools/check-commit-msg.sh` runs both as a local `commit-msg` hook
(`./tools/install-hooks.sh`, using `core.hooksPath` so hooks arrive with a pull) and as a CI job
over every commit in a push or PR. One script drives both, so they cannot drift apart. It skips
merges, reverts, `fixup!`/`squash!` and semantic-release's own `chore(release):` commit.

## Environment

- **KSA install**: `/mnt/c/Program Files/Kitten Space Agency` (Windows game, WSL dev)
- **KSA build these notes were taken against**: `2026.8.3.5117`
- The system `dotnet` is 8.0 and **cannot build this** — the mod targets **net10.0**
  (`error NETSDK1045`). A .NET 10 SDK is installed at `~/.dotnet`.
  **Use `tools/build.sh` / `tools/test.sh`**, which source `tools/env.sh` to fix PATH. Bare
  `dotnet` commands fail. In an interactive shell, `source tools/env.sh` once.

- `Import/` holds the game's assemblies and is **gitignored**. Repopulate with
  `./tools/sync-import.sh`. Nothing builds without it — though the build also finds a game
  install or a `ksa-game-assemblies` checkout on its own, see `Directory.Build.props`.
- **When KSA updates, four things have to move together**, not just `Import/`. See
  [After a KSA update](#after-a-ksa-update); getting it wrong makes CI build the mod against a
  different game from the one you are testing on, silently.
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
./tools/check-boundary.sh                  # Sim/ must not reference KSA types
./tools/package.sh                         # release zip into dist/ -- no symbols, no game DLLs
./tools/deploy.sh                          # build and install into the KSA mods folder
./tools/run.sh                             # build, deploy, launch, show the mod's output
./tools/run.sh --attach                    # follow a game that's already running
./tools/setup-starmap.sh                   # one-off: install StarMap and write its config
./tools/check-assemblies.sh --game         # has the installed game moved past the lock?
./tools/check-ksa-version.sh               # has RocketWerkz published a newer build?
./tools/api-surface.sh                     # record the KSA API this mod binds to
./tools/api-surface.sh --check             # ...and fail if the record is stale
./tools/decompile-assemblies.sh ../ksa-game-assemblies   # refresh the decompiled corpus
./tools/ksa-api-diff.sh ../ksa-game-assemblies           # which KSA changes hit this mod?
./tools/sync-import.sh                     # refresh Import/ -- NOT the whole story after a
                                           #   KSA update; see "After a KSA update" below

source tools/env.sh                        # then bare dotnet works in this shell
cd tools/apidump && dotnet run -- ../../Import members KSA.Vehicle   # inspect the game API
./tools/meshinfo.py "<KSA>/Content/Core/Meshes/CoreStructuralA_MeshAtlas.glb" Tube  # mesh bounds
```

## Layout

**The source is split by whether it touches KSA.** `Sim/` cannot; `Ksa/` does. That is not a
convention to remember — the test project links `Sim/**` wholesale and references no KSA
assembly, so a `using KSA;` under `Sim/` fails the test build. It also means a new file under
`Sim/` is tested the moment it exists, with no build plumbing to add.

| Path | What |
| --- | --- |
| **`src/AirDefence/Sim/`** | **no KSA types, linked into the tests wholesale** |
| `Sim/Arsenal.cs` | **the registry — add a weapon system here** |
| `Sim/LauncherProfile.cs` | one launch platform: part Id, tube geometry, drives |
| `Sim/MunitionProfile.cs` | one round: boost, guidance, fuse, warhead |
| `Sim/SensorProfile.cs` | one sensor: range, cone, threat model |
| `Sim/Config.cs` | the player's settings only — policy and display |
| `Sim/Interceptor.cs` | round physics, proportional navigation, fuse |
| `Sim/Turret.cs` | rate-limited traverse and elevation drives |
| `Sim/FireGeometry.cs` | launch direction and round-body orientation |
| `Sim/Vec.cs`, `Sim/DrawAnchor.cs` | vector helpers, the two-instant draw anchor |
| **`src/AirDefence/Ksa/`** | **everything that binds to the game** |
| `Ksa/AirDefenceMod.cs` | StarMap entry point and frame hooks |
| `Ksa/KsaWorld.cs` | most KSA contact is funnelled here — keep it that way |
| `Ksa/DefenceBattery.cs` | fire control, salvo logic, warhead effects, drives |
| `Ksa/Radar.cs` | cone search, CPA threat model, lock |
| `Ksa/LauncherPart.cs` | finds a registered launcher, resolves tubes and subparts |
| `Ksa/Ui.cs`, `Ksa/Visuals.cs` | ImGui panel, gizmo rendering |
| `src/AirDefence/AirDefence*.xml` | the launcher part — at the mod root, mirroring Core |
| `src/AirDefence/Meshes/`, `Textures/` | generated art; rebuild with `tools/model/build.sh` |
| `src/AirDefence/mod.toml` | serves as both the content-mod and StarMap manifest |
| `tests/AirDefence.Tests/` | links the KSA-free sources and flies engagements headlessly |
| `tools/apidump/` | reflection dumper for the game assemblies |
| `tools/apisurface/` | reads the KSA API this mod binds to out of its own metadata |
| `docs/KSA-API-SURFACE.md` | **generated** — the 115 members an upgrade has to preserve |
| `.claude/skills/upgrade-ksa/` | the whole KSA-update procedure, as a skill |
| `tools/meshinfo.py` | prints mesh bounds from a KSA `.glb` atlas |
| `tools/validate-parts.py` | checks asset Ids, texture paths, and launch geometry vs the mesh |
| `tools/model/` | headless Blender scripts that generate the Pantsir |
| `tools/model/checkmesh.py` | finds zero-UV-area triangles and coplanar faces in a `.glb`; `--compare` diffs two atlases by geometry |
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

**The atlas is not byte-reproducible.** Blender's exporter does not emit triangles in a stable
order, so a rebuild from unchanged sources gives a different file — same positions, normals and
UVs, permuted index buffer. `git status` showing it modified after a build therefore means
nothing. Ask `./tools/model/checkmesh.py <new> --compare <old>`, which compares the surface
rather than the bytes, and **revert the atlas** if it says the geometry is unchanged.

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

## Adding a weapon system

The mod is built around three profile types and a registry, so a new launcher, round or sensor
is **data plus art**, not new logic. Nothing in `Sim/` or `Ksa/` names the Pantsir.

1. **Model it.** Copy `tools/model/pantsir.py`, keep the group/pivot conventions, and export
   into the same atlas. Run `tools/model/checkmesh.py` — it fails the build on the two defects
   that only show up in game.
2. **Declare the part.** A `<SubPart>` per moving assembly plus a `<Part>` in
   `AirDefenceAssets.xml`, and a `<PartGameData>` with its colliders and mass.
3. **Register it.** One `LauncherProfile` in `Sim/Arsenal.cs`, naming the munition and sensor it
   uses, with the geometry `build.sh` prints. Add a `MunitionProfile` too if the round differs.
4. **Nothing else.** `LauncherPart.Find` matches against every registered part Id, and the
   battery selects whichever profile it finds. `ArsenalTests` checks the registry hangs
   together; `validate-parts.py` checks the geometry still matches the mesh.

A launcher that does not train is the same `LauncherProfile` with `TurretMarker` left null —
the drives are skipped and `IsLaid` stays true, so fire control cannot deadlock waiting for
something that will never move. `ArsenalTests.AFixedLauncherIsJustAProfileWithNothingThatMoves`
pins that shape.

**What is deliberately *not* general yet:** one battery per craft (the first launcher found
wins), and `Config` holds a single active profile set, so the panel tunes one system at a time.
Both are straightforward to widen — the profiles are already per-system — but neither is
speculatively built.

## CI and releases

Building needs KSA's own assemblies — `KSA.dll`, `Brutal.Core.Numerics.dll` and friends — and
the tests need them too, for `double3`. They are RocketWerkz's copyrighted files and **must
never be committed here or published anywhere**.

They live instead in the private repository **`LaurensDeV/ksa-game-assemblies`**, checked out by
CI with a **read-only deploy key** held in the `KSA_ASSEMBLIES_KEY` secret. Keeping your own
licensed copy privately is fine; publishing it is not. Only the eight assemblies the projects
actually reference are kept — verified as the minimum that both builds the mod and runs its
tests — and `tools/sync-assemblies.sh` refreshes them after a KSA update, refusing if a csproj
references something it does not know to copy.

`Directory.Build.props` resolves the folder in tiers, first match wins: `KSA_DLL_DIR` (what CI
sets), then `Import/`, then a sibling `ksa-game-assemblies` checkout, then the game install. So
nothing local changed.

### After a KSA update

**You will be told when this happens**, twice over:

- **Before the update is installed** — RocketWerkz publish the current build at
  `http://ksa-master1.rocketwerkz.com:8082/version`, which returns
  `{"Version": "...", "Url": "..."}`. `./tools/check-ksa-version.sh` compares it to the lock,
  and the `ksa-version.yml` workflow does the same daily and **opens an issue** when they
  diverge. It opens an issue rather than failing: nothing is broken when the game moves, there
  is just work to do, and a red cross on an unrelated commit says that badly.
- **After it is installed** — `./tools/build.sh` checks the install against the lock on every
  build, silent unless it has moved. `./tools/check-assemblies.sh --game` asks on demand.

The version check is deliberately *not* wired into `build.sh`: a network call on every build is
slow, breaks offline, and would be the first thing anyone disabled.

That check deliberately looks at the *install*, not at whatever the build resolved: `Import/` is
a copy, so it still matches the lock after a game update and would report all-clear. It also
compares only what the install ships — `StarMap.API.dll` comes from the loader, and would
otherwise report a KSA update every single run.

The assemblies exist in two places that drift apart silently: your `Import/`, and the
private repo CI compiles against. Update one and not the other and CI is building the mod
against a different game from the one you are testing against — which surfaces as behaviour
nobody can reproduce, not as an error.

`ksa-assemblies.lock` records the expected SHA-256 of each referenced assembly plus the game
build. It holds hashes and names only, so it is safe in a public repository. Both CI jobs check
the assemblies against it and fail if they disagree, and `sync-import.sh` reports the same thing
locally the moment you refresh.

The dance, in order — **or just run the `upgrade-ksa` skill, which is this written out with the
reasoning attached**:

```bash
./tools/sync-import.sh                                    # refresh Import/; it reports the drift
./tools/sync-assemblies.sh      ../ksa-game-assemblies    # the mirror's DLLs
./tools/decompile-assemblies.sh ../ksa-game-assemblies    # the mirror's sources
#   set current/KSA_BUILD, commit BOTH together, push there
./tools/ksa-api-diff.sh ../ksa-game-assemblies      # what actually broke — read this
./tools/check-assemblies.sh --update                # record the new digests
./tools/api-surface.sh                              # the surface moves if the fixes did
#   edit the `build` line in ksa-assemblies.lock, commit it here
```

Do the private repo *before* pushing here, or CI fails on the lock it cannot satisfy yet.

**The compiler only finds half of it.** A renamed member is a build error you fix in seconds. A
member that keeps its name and signature and changes its *meaning* — a different reference
frame, different units, a reordered enum — compiles clean and is wrong in flight. This
repository has shipped that bug three times from its own code, and a KSA update can reintroduce
any of them. That is what the decompiled corpus is for, and `ksa-api-diff.sh` narrows it from
650,000 lines to the files defining the 43 types this mod actually uses.

**The mirror is a general KSA SDK, not this mod's dependencies.** It carries all 35 RocketWerkz
first-party assemblies plus the loader and the game-shipped third-party — 44 in total, 12 MB —
so any KSA mod can build against it. `sync-assemblies.sh --subset` restores the old minimal set.
Per-mod pinning still comes from that mod's own `ksa-assemblies.lock`, which covers only what it
references, so the drift check stays exact without the mirror being narrow.

The build number is written down in three places, so update all three: `ksa-assemblies.lock`,
`current/KSA_BUILD` in the private repo, and the **KSA build** line under Environment above. The
lock is the one that is actually enforced; the other two are for humans reading.

CI is split the same way the source is:

- **`tooling` (hosted, always runs)** — everything that does not need the game, which is more
  than it sounds. `checkmesh.py` on the committed atlas catches the two defect classes that are
  invisible outside the game; `check-boundary.sh` guards the Sim/Ksa split textually;
  `palette.py` is re-run and the textures diffed, so hand-edited PNGs are caught before the next
  model build silently reverts them. Plus shellcheck, XML well-formedness and a check that no
  `.dll` is tracked.
- **`build` (hosted)** — the real build, the 74 tests, `validate-parts.py` and the package,
  against the checked-out assemblies. If the secret is absent — a fork — the job skips with a
  notice instead of failing on something a contributor cannot fix.

shellcheck runs at `-S warning`. At the default level it flags every `source tools/env.sh` as
unfollowable, which it is, and CI would fail on nothing.

**Versioning is automatic and commit messages are the input.** semantic-release runs on every
push to `main`, reads the Conventional Commits since the last tag, and cuts the release: version,
`CHANGELOG.md`, the `<Version>` in the csproj (via `tools/set-version.sh`), the tag and the
GitHub Release. **Never edit a version by hand** — it will be overwritten. `feat` is a minor,
`fix`/`perf`/`build`/`revert` a patch, `!` or a `BREAKING CHANGE:` footer a major; `docs`,
`chore`, `ci`, `test`, `style` and `refactor` cut no release. A commit that does not parse is
treated as no release, so a stray `wip` cannot publish anything.

That workflow is in two jobs: the first decides the version and creates the release, the second
builds the archive and attaches it. Both hosted. The release commit carries `[skip ci]` so it
does not retrigger CI.

Three things that will bite:

- **Branch protection on `main`** blocks the release commit unless the token can bypass it.
- **A shallow checkout** makes semantic-release believe every push is a first release — hence
  `fetch-depth: 0`.
- **The first ever release is 1.0.0 unless a tag says otherwise.** semantic-release reads "no
  tags" as "no releases", and the `<Version>` in the csproj has no bearing on it — confirmed by
  dry run, which announced 1.0.0 with the project file saying 0.1.0. The project is pre-1.0, so
  a `v0.1.0` tag has to exist before the first automated run anchors it. Promotion to 1.0.0 is
  then a deliberate `git tag -a v1.0.0`; a `BREAKING CHANGE:` footer would also do it, which is
  worth avoiding until it is meant.

**Releases** are `./tools/package.sh`, locally or from the release workflow — the archive is
identical either way. `./tools/publish-release.sh` does both halves from a machine with KSA:
build, then attach to the release semantic-release created. That is the fallback for when the
assemblies secret is unavailable, and it refuses rather than guessing if the tag or the release
does not exist.

**One archive covers Windows and Linux.** The mod is a portable `net10.0` assembly: no
`RuntimeIdentifier`, no P/Invoke, no Windows-only API. There is nothing to build twice, and
adding a per-platform build would produce two identical files. What *does* differ by platform:

- **Case sensitivity.** A mismatched filename loads on Windows and fails on Linux. The hosted CI
  job runs `validate-parts.py --offline` for exactly this — on Linux, where case matters — and
  it compares against the real directory listing rather than trusting `is_file()`.
- **Where KSA's user directory is.** `Log.cs` tries Documents, `XDG_DATA_HOME`, `~/.local/share`
  and `~/.config` in turn, only ever using one that already exists, and falls back to temp.
  `deploy.sh` searches the same set.
- **The loader.** StarMap ships `StarMap.exe` plus a portable `StarMap.dll`, so `dotnet
  StarMap.dll` is the Linux equivalent. *Not verified here* — this machine is WSL and runs the
  Windows build. Release builds carry **no debug symbols** (`DebugType=none` in the csproj)
and the log starts at `INFO` rather than `DEBUG`, because the developer detail runs to hundreds
of lines per engagement. `Log.Threshold` and the panel's **Verbose log** tick box put it back
without needing a different build, which is what you want from a bug report. The packaging
script refuses to ship a `.pdb` or any DLL that is not ours — that is the last gate before an
archive leaves the machine.

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

**`Sim/` must stay free of KSA types**, and this enforces itself — see the Layout note. When
something KSA-facing turns out to have testable maths inside it, move the maths into `Sim/`
rather than leaving it unverifiable; `FireGeometry` came out of `LauncherPart` exactly that way,
and the launch-angle bug was only testable afterwards.

**Weapon performance lives on profiles, not in `Config`.** `Config` is the *player's* settings:
armed, auto-engage, what to draw. Range, guidance, fuse and launcher geometry belong to a
weapon system and vary per system, so they sit on `SensorProfile`, `MunitionProfile` and
`LauncherProfile`. The panel edits whichever profiles `Config.Select` last pointed at, so live
tuning still works — it just tunes that system rather than the whole mod.

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

**The battery runs on simulated time, never on player time.** StarMap's frame hook hands you
`currentPlayerTime` and a player-time delta, and both are deliberately ignored. Player time is
wall-clock, which is wrong twice over and both were seen in game: it keeps running while the
game is **paused**, so the radar accumulated dwell, matured a firing solution and launched into
a frozen world; and it ignores **timewarp**, so at 10× the world moved ten times further per
frame than the rounds did and tracking fell apart. `KsaWorld.SimTimeSeconds` differenced by
`Sim/SimClock.cs` is the fix, and it is `Universe.GetElapsedSimTime()` plus `Universe.IsPaused()`.

`SimClock` also refuses steps it cannot integrate. `Interceptor` subdivides internally but
clamps at 64 sub-steps, so beyond `Interceptor.MaxFaithfulStep` (0.32 s) a round at 700 m/s
starts stepping over its own fuse radius. Past that — heavy warp, or a load that replaced the
clock — the battery calls `AbandonFlight` and drops what is in the air rather than pretending.
Clamping the delta instead, which is what the old code did with `Math.Min(dt, 0.1)`, silently
discards time and makes the mismatch worse. `SimClockTests` pins both behaviours, and both were
checked by reintroducing the bug.

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
the `LauncherProfile` in `Sim/Arsenal.cs` is pasted from what it prints.
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
