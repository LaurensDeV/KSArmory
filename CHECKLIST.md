# Test checklist

## Status: the system works end to end in game

Confirmed against KSA `2026.8.5.5168`: the part loads and renders, the craft launches standalone,
the radar searches and classifies threats, the launcher slews and fires salvos, proportional
navigation intercepts, the proximity fuse detonates, the blast destroys the target, and the
overlay draws correctly on the craft.

Two engagements, a crossing target and a head-on one, both from 9 km, gave four detonations at
**15, 17, 18 and 20 m** and two kills, with no warning or error in the log. All 30 launcher
subparts resolved and `drive=True part=True` held throughout, so the engine accepts the per-frame
subpart transform writes.

Two things those runs do not exercise, so they stay unproven on this build: everything ran at
`sim 1.00x` with every step classified `running`, leaving the timewarp and step-overrun path
untested; and the cannon never left `want=False`, because both engagements sat well outside its
200–4000 m envelope.

**Re-flown after the retarget to KSA `2026.8.19.5261`.** That build renamed `SimTime` to
`UniverseTime`, moved the vehicle solver behind a worker pool, and replaced the physics update task
with `PhysicsBubble` — so the drone spawn path, the solver barrier and the whole simulated clock
were rewritten and needed proving rather than assuming. `scenario.sh head-on` and `overhead` both
pass unattended, and a Pantsir engagement destroyed three drones on the head-on, passing and
overhead geometries, firing both missiles and cannon, with `bubble yes` on every spawned drone —
which is the `AddToBubble` rewrite confirmed rather than inferred. The only log warnings are the two
that describe a launcher honestly: no turret on the rail, no round bodies on the gun-only CIWS.

What that run does **not** cover, so it stays unproven on this build: the optical head's sight at
magnification, the chase camera, the bomb rack, and the editor's **Weapons** category — none of
which a headless scenario reaches. `validate-parts.py` passes against the install, so the tags are
declared; that they still group the parts is unwatched.

**Re-flown after the retarget to KSA `2026.8.22.5348`.** That build removed `Vehicle.PhysicsBubble`
outright: a vehicle in no bubble is now collected by `Universe.PrepareVehicleWorkers` and given one
by `VehicleUpdateTask.IntakeOrphans` before the step it was found on, so the manual `AddToBubble`
the drone spawner did is gone rather than rewritten. The evidence inverts accordingly — a spawned
drone reads **`bubble none`** in the world dump and flies anyway, where the previous build's proof
was `bubble yes`. `scenario.sh head-on` passes unattended, the drone crossing 9 km and being
destroyed by the round; the captured frame shows the plume smoke drawing, which is the reflected
`Program._volumetricTrailRenderer` still binding, and the overlay on the craft rather than beside
it.

Two engine changes in that build survive into flight and were **not** flown, so they are the ones to
watch. Staging became stage-accurate: `SequenceList.ActivateNextSequence` now calls
`Part.ActivateSubtreeInStage(vehicle, sequenceNumber)`, which walks the part's whole subtree and
fires only modules whose own stage matches, where it used to fire every `IActivate` on the listed
part and nothing below it. The mod stages through KSA's own entry point so it inherits this, but a
multi-stage ballistic shot has not been flown against it. And the terrain height field's **seam**
sampling changed — a bicubic tap landing off its cube face is now unfolded and point-fetched
instead of bilinear-blended at a half-texel offset — so a height within two texels of a cube edge is
not the number it was, which reaches the bomb's ground test and the ballistic impact predictor.
`docs/KSA-TERRAIN.md` has the detail.

Unchanged from the note above: the sight at magnification, the chase camera, the bomb rack and the
**Weapons** category stay unwatched, and this retarget flew the rail rather than the Pantsir, so the
turret traverse and pod elevation are unwatched on this build too.

**Retargeted to KSA `2026.9.4.5400`, and flown.** Everything below this paragraph predates it. That build rewrote the viewport subsystem — `Viewport` became `IViewport` / `ViewportBase` /
`GameViewport`, the list became `ViewportRegistry`, and `Viewport.Index` and `IsOffscreen` went — so
every camera path in the mod was retargeted onto it. The suite passes and the mod builds, which
proves neither: the tests link no KSA assembly.

**What was flown on 2026.9.4.5400**, all unattended:

| Scenario | Result |
| --- | --- |
| `head-on` | **PASS** — detonation at 15 m, drone destroyed |
| `overhead` | **PASS** — detonation at 17 m, drone destroyed |
| `mirv` (save `ICBM E2E`, 12,902 km downrange) | **PASS** — 6 of 6 arrived, worst 0.624 km, mean 0.622 km, spread 0.004 km against a 5.0 km bar |
| `passing` | **TIMEOUT, and correctly so** — see below |

The ballistic shot exercised the whole chain and every stage behaved: cutoff at **0 m/s to gain**
with a 0.21 m/s residual, `WarpPolicy` clamping a requested 100x to 10.5x and then 6.3x off the
step it was handed, separation, and `BusTrim` converging once per warhead (0.030, 0.023,
0.017 m/s). The attitude hook held throughout — `before Auto/Custom -> after Auto/Custom`, never
the `Manual/None` that means the write was overwritten — and the pointing deadband sat at
**0.20 deg**, not the 11.40 deg mass-properties pathology. The mod cost **0.62 ms of frame mean**,
3.26 ms worst. **No warning or error in the log for the whole session.**

`passing` times out because the LAU-7 is a **fixed** rail and the profile puts the drone 50 deg off
the tube, past the seeker's 40 deg — `WeaponSystem` refuses and says so. That geometry needs a
launcher that trains. It is the scenario pairing that is wrong, not the mod.

Three things the scenarios could **not** reach, so they stay unverified:

- **The reflected `FixedController` install.** No scenario takes a chase camera — `mirv` parks the
  view on Earth — so `LevelTheHorizon` was never called and neither its success line nor its
  warning appears. This is the one path the retarget changed by reflection. Trigger a chase and
  look for `camera: levelling the horizon on the main view` in the log.
- **The sight at magnification, the turret traverse and pod elevation.** These runs flew the rail
  and the ballistic stack; nothing drove the Pantsir.
- **Debris after a kill.** `DestroyVehicleFromEvent` now sheds debris, and nothing checked what the
  radar then holds.
- **Part damage.** Added after those runs and flown by nothing. See 4.4a — it is the largest
  unverified thing in the mod.

A screenshot wart, not a mod fault: `--shots` reported writing captures that never appeared.
`tools/screenshot.sh` refuses when the game is not the foreground window, which it is not when a
scenario runs unattended.

Four things changed shape and need watching, worst first:

1. **The camera and the sight.** `MainViewportIndex`, `CollectUsableViewports`,
   `TryProjectIntoViewport`, `ViewportFovRad`, `TryLookFromViewport` and both cursor-ray paths now
   index `ViewportRegistry.GameViews` instead of `Program.Viewports`. Viewport *numbers* therefore
   mean a position in a different list, and a persisted `OpticConfig.Viewport` could select a
   different window than it did. Watch: the sight centres its target at 16x, the chase camera, and
   the secondary-viewport picker naming the windows that actually exist.
2. **The levelled horizon is installed by reflection now.** `IGameViewport.FixedController` is
   get-only, so `KsaWorld.LevelTheHorizon` writes `GameViewport`'s backing field. It verifies the
   write took and warns if it did not. Watch for `camera: levelling the horizon on the main view`
   in the log on the first chase, and for either warning beside it. If it failed, the chase
   horizon comes in rolled *and* the sight's aim lags a frame under warp.
3. **A kill now leaves debris.** `Universe.DestroyVehicleFromEvent` calls
   `PartFailure.ShedDebris(vehicle, 12)` before destroying. Debris are craft, so they enter
   `ContactCandidates` and can be detected, tracked and shot at. Nothing in the mod knows about
   them. Watch what the radar holds after a kill, and whether a salvo re-engages wreckage.
4. **Plume colour is per-emitter.** `SubmitEmitter` gained `color`, `densityMultiplier` and
   `lifetimeSeconds`, and the global `DebugTrailColor` is gone. The mod passes Core's
   `DefaultPlumeTrail` values (white, 1, 1200 s), so smoke should look as it did — and a nuclear
   cloud's tint should no longer discolour a booster burning at the same time, which it used to.

Also unflown on this build: everything the previous retarget listed as unwatched, and the turret.

**Retargeted to KSA `2026.9.10.5438` — the managed surface moved, and most of what matters does not
show in a build.** Three things were compile errors: `GameSettings.Graphics.ScreenSpaceParticles`
is gone, `KeyHash` now lives in `Planet.Render.Core.dll` under the same namespace, and the ImGui
bindings grew function-pointer overloads that made a `null` text-input callback ambiguous. The
Numerics rewrite (2026.6 to 2026.9) was read member by member and changes no meaning. What compiles
clean and still has to be flown or looked at:

- [ ] **Burst smoke rises and the tracers hang.** `GravityStrength` is gone from the particle schema
      and the XML deserialiser drops it without a word, so every stage fell at full gravity until
      each was given a `Density`. The values reproduce the old behaviour in sea-level air only:
      higher up the smoke rises less, and below 100 Pa everything falls. Watch a burst at a low
      site and one at altitude.
- [ ] **A warhead goes off as KSA's own explosion**, flash and sound included: `PopSmallExplosion`
      for a cannon shell, `SmallFire` for a missile or the 5"/54, `Explosion_Conflagration` from
      41 kg up and for a nuclear yield. Look at one of each, and grep the newest
      `KittenSpaceAgency.*.log` for `ExplosionSystem:`, which is where a dropped one says so.
      Flown, not watched: `head-on` set off `SmallFire` for the AIM-9J at 43 kPa and
      `gunnery:1,overhead,30,300,1500` for all seven 5"/54 shells at 47 kPa, with every preset
      resolving at load and no `no explosion` in the mod's log. KSA's own log cannot say: under a
      scenario it ends before the first burst.
- [x] **KSA draws its own explosion on every kill and every broken part**, from
      `DestroyVehicleFromEvent` and `PartFailureEvent.Apply`, on top of the warhead's. Kept on
      purpose.
- [x] **Parts break at different ranges.** `CrashTolerancePascals` now comes from collider volume
      against a 9 MPa base at 330 kg/m³, clamped to 0.1–100 MPa, and `BlastDamage.ReferencePascals`
      moved with it to 9 MPa — flown below.
- [ ] **The stack's delta-v may read lower mid-flight.** `SequencePerformanceList.TotalDeltaV` skips
      sequences already passed in flight mode, and `IcbmProgram` judges reach on it.
- [ ] **The rotating-frame fix is in** — `docs/BLOCKED-ON-KSA.md` — so the frame gate that re-flies
      a shot with a rotating-frame probe is now dropping sound shots. Not retired.
- [ ] **Cost, and comparability.** Bubbles now step to mid-step merge horizons, `FxDeformation` runs
      every step for every vehicle, `ExplosionSystem.Update` runs every frame, and KSA's explosion
      volumes share the trail renderer's globals that `PlumeSmoke.Tune` writes. No `SolverLoad` or
      `FrameBudget` number from an earlier build compares, and no shot does either: the new
      Numerics may fuse multiply-adds, so arms flown on 5402 and 5438 differ by more than the arm.

| Scenario | Result |
| --- | --- |
| `head-on`, reference 3 MPa | **PASS** — burst at 17 m, **2 of the drone's parts broken, and no kill** |
| `head-on`, reference 9 MPa | **PASS** — burst at 17 m, 2 broken |
| `head-on`, reference 9 MPa, verbose | **PASS** — burst at 15 m, **6 broken** |

A pass is the harness's — engagement over, no rounds left — and not a kill: 5402 burst at 16 m and
broke 7, fragmenting the drone into 11 vehicles, and none of these reached the fragment guard's 11.

**`ReferencePascals` moved to 9 MPa, and the verbose flight is what settled it.** KSA did not rescale
strength uniformly. Against the tolerances its 5402 log printed, the drone's 20 damageable parts rose
2.09x on the geometric mean — the service module 8.5x (1.5 to 12.7 MPa) and the RCS blocks 3.8-5.4x,
while two tanks weakened to 0.46-0.60x and the engine's authored 3 MPa did not move — so no anchor
reproduces 5402, and 6.3 MPa would match the mean exactly. At 9 MPa a typical part's reach is 1.13x
5402's and the seven parts 5402 broke average 1.04x; at 3 MPa those are 0.78x and 0.72x. 9 MPa is the
nearer, and it is KSA's own constant, which the next update can follow. The burst distance moves the
count more than the anchor does: the weak parts sit 14-16 m from a burst at 15-17 m, right at their
reach. `KSARMORY_SCENARIO_VERBOSE=1` puts every part's gap, reach and tolerance in the mod's log.

Everything else in all three was clean: the three patches installed (`Vehicle.PrepareWorker`,
`Program.OnFrameCelestials`, `Program.OnGameLoaded`), the trail renderer bound, the rail's body and
tube resolved, all four fireball stages registered, and the stamp read `built for KSA
2026.9.10.5438, running 2026.9.10.5438 - reporting on`. KSA's own log has no warning or error, and
ModMenu injected its menu entry. **KSA's log is now per session**,
`Logs/KittenSpaceAgency.<yymmdd-hhmmss>.<pid>.log`, and it records nothing about the part failures.
Its last line comes before the burst, so either this build logs no crash tolerances or the harness's
`taskkill` lost the buffer — the `.abnormal-exit.log` beside it is that kill. Only the rail flew.

**Retargeted to KSA `2026.9.7.5402`, and flown — nothing in the *managed* surface moved.**
RocketWerkz's note for 5402 is one line: *fixed crash for incorrect data stride for thumbnail
rendering*. That fix is in code this corpus cannot see, and the distinction is worth keeping.
Every first-party managed assembly was rebuilt, but the decompiled output is byte-identical to
5400 but for three `AssemblyInfo.cs` version stamps — `KSA.Rendering.Thumbnails` included, which
is where the thumbnail renderer actually lives — and `tools/ksa-api-diff.sh` reports no missing
members and no file defining a type this mod uses touched. The hashes moved because a rebuild
restamps version and MVID, not because the code differs.

The one **native** library rebuilt alongside them is `VulkanEx.dll`, which `Brutal.Vulkan` loads
through `NativeLibrary.Load` and which nothing decompiles; every other native lib in the install
kept its old date. A Vulkan buffer stride is exactly what lives there. So a clean corpus is
*consistent* with the changelog rather than in tension with it: it says the fix landed somewhere
this mod cannot bind to. **The corpus proves the managed API did not move and says nothing about
native code** — worth remembering the next time a changelog and a clean diff disagree.

Nothing in the mod was retargeted, because there was nothing to retarget — the diff is the lock,
the five prose build lines and this paragraph. Core's
XML is unchanged where the mod binds to it: all 416 asset references resolve against the install,
`Radial` still carries `FaceSnapTargetBlacklist` and `NoFaceSnapping` still carries
`FaceSnapBlacklist`, and every Core character Id the mod names still resolves to the element type
it expects.

| Scenario | Result |
| --- | --- |
| `head-on` | **PASS** — detonation at 18 m, drone destroyed |
| `overhead` | **PASS** — detonation at 16 m, drone destroyed |

**No warning or error in either session.** Both runs confirm the bindings that a rebuild could
still have broken even with identical sources, because they rest on layout rather than on
signatures: the Harmony prefix installed (`attitude control hooked into Vehicle.PrepareWorker`),
the reflected trail renderer bound (`volumetric smoke: the trail renderer is reachable`), the
per-frame subpart transform writes accepted (`1 bodies, 1 tubes, tubesResolved=True`), both weapon
packs registered, and both warhead effects placed. The build stamp also reads
`built for KSA 2026.9.7.5402, running 2026.9.7.5402 - reporting on`, so in-game reporting is live
rather than silently off.

Both runs flew the **rail**, so everything the 5400 retarget left unwatched is still unwatched:
the turret traverse and pod elevation, the sight at magnification, the chase camera and its
reflected `FixedController` install, the editor's **Weapons** category, and what the radar holds
after a kill. The four items above are 5400's and are unaffected — 5402 touched none of the code
they describe — so they carry over verbatim rather than being re-opened.

The failure modes worth recognising before starting, and how to tell them apart, are in
`docs/KSA-MODDING-NOTES.md` and `docs/FRAMES-AND-EPOCHS.md`.

Known wart: the **miss distance slider on test targets is nominal, not achieved**. The ballistic
solve is a vacuum solution and KSA models atmosphere, so drones undershoot: a requested 1500 m
pass arrives at roughly 4000 m.

Remaining untested: sections 5 (safety) and 6 (robustness) below.

## 0. The panel, after it was rebuilt around components

Nothing here has been flown. The whole window moved in one evening and every item is a claim
made from reading.

- [ ] **Components** is the first tab and the only one a craft always has. Rows are grouped by
      role, each one folds open, and the position line is still there.
- [ ] A **Camera** row holds everything the Director tab used to. Two directors on one craft are
      two rows that aim independently, and taking the main view on one drops it from the other.
- [ ] A **Launcher** row holds the tally, the reload bar, the mount readout, `live`, Reload,
      Safe all, chase and the bomb sight. A **Gun** row holds its belt and its own `live`.
- [ ] A **Sensor** row holds the lock and the contact. The director's own sensor row says it is
      not the one fire control reads.
- [ ] A **Fire control** row holds auto-engage, FIRE, Reset settings and the mouse
      controls.
- [ ] A second launcher of the same kind says **fitted, not run** rather than showing blanks.
- [ ] The strip above the tabs shows *Clear to fire* / *Holding fire* from **every** tab, plus
      rounds in flight and a word about the clock only when it is the problem.
- [ ] A craft carrying **only a director** opens the window, lists its Camera and Sensor rows,
      and says *no weapons system on this craft* on the rest — **without faulting**. This is the
      case that crashed twice tonight; both were null `_battery` reads on an uncrewed path.
- [ ] *KSArmory settings* holds Display, Sound and the warp hold; *Debug tools* is a window of
      its own beside it. The main panel is the craft list and nothing else.
- [ ] **Tuning** says at the top that it edits every system running that loadout.


## Status: the Pantsir model renders in game

The launcher is a full Pantsir-S1: an 8×8 vehicle 8 m long and 5.6 m tall carrying twelve rounds,
built from `tools/model/pantsir.py` and shipping **its own mesh atlas and textures**.

The vehicle renders correctly and is recognisable, which settles the biggest open question in this
file: **a user mod can ship its own `<MeshAtlas>` and `<PbrMaterial>`, with PNG textures, resolved
relative to the mod root.** The tube markers land on the container mouths, so the generated launch
geometry agrees with the mesh in game and not just in `validate-parts.py`.

Coplanar faces where two boxes abut on a shared plane z-fight, and the whole vehicle then flickers
with white speckle. `box()` inflates every primitive by 8 mm to separate them, and **Blender's
preview render does not reproduce the defect**, so the game is the only place it can be checked.

Still untested:

- **Mass and size** — 30 t and 8 m. Colliders, attachment, pad physics.
- **Round count** — `12/12` in the panel, twelve markers, a full twelve-round salvo.

Guidance, radar, fuse and the draw anchor are unaffected by the model and stay ticked.

---

The order below is deliberate: **riskiest unknowns first**. If something fails, the *If it
fails* line says what it means and where to look.

---

## Where trouble is expected

Ranked by how likely a failure is. Worth reading before you start.

| Risk | What might break | Covered by |
| --- | --- | --- |
| ~~High~~ | ~~The mod's own `<MeshAtlas>` / `<PbrMaterial>` paths don't resolve~~ — **settled, it renders.** | [2.2](#22-the-part-renders) |
| **Medium** | 30 t and 8 m of part on a stack: colliders, editor attachment, pad physics. | [2.3](#23-the-part-attaches), [2.4](#24-the-part-behaves-physically) |
| **Medium** | `mod.toml` serving as both content manifest and StarMap manifest. Plausible, untested. | [1.1](#11-the-mod-loads) |
| **Medium** | `Program.VehiclesInFrame` may not contain the loaded vehicles, so radar sees nothing. | [3.3](#33-radar-sees-a-target) |
| **Medium** | Boresight is local "up" derived from the parent body; if `Vehicle.Parent` misbehaves the cone points somewhere daft. | [3.2](#32-the-search-cone-is-drawn) |
| **Medium** | Shooting down a round. Three seams changed at once and none of them is reachable from the test project. | [7.1g](#71g-shooting-down-a-round---never-once-worked-so-nothing-here-has-ever-been-seen) |
| **High** | The viewport rework on `2026.9.4.5400`. Every camera path was retargeted and none has been flown. | [3.1b](#31b-the-turret-slews), status note above |
| **Medium** | The levelled horizon is now installed by writing a private backing field. It says in the log whether it took. | status note above |
| **Medium** | A kill sheds debris, which are craft the radar can see and shoot at. | [4.4](#44-the-warhead-kills) |
| **Medium** | Individual parts break instead of whole craft. Flown once against a drone; the ordering fault it exposed is fixed, and a long craft losing one end is still unwatched. | [4.4a](#44a-individual-parts-break--never-flown-the-whole-feature-is-unverified) |
| **Medium** | A fragmenting craft costs frame time nobody has measured: one craft becomes several, each simulated. | [4.4a](#44a-individual-parts-break--never-flown-the-whole-feature-is-unverified) |
| **Medium** | A launcher or director part destroyed outright now retires its roster entry after 120 fruitless searches. The bound has never been reached in flight. | [4.4a](#44a-individual-parts-break--never-flown-the-whole-feature-is-unverified) |
| **Medium** | The ballistic computer writes attitude, throttle and staging on a vehicle nobody designed for it. Flown and arriving; what is unwatched is each change since. | [12](#12-the-ballistic-computer--flown-and-arriving) |
| **Low** | Guidance and fuse maths — covered by the headless suite, but never against real KSA motion. | [4.3](#43-a-crossing-target-is-intercepted) |
| **Low** | `DestroyVehicleFromEvent` may behave oddly with `Cause = Collision`. | [4.4](#44-the-warhead-kills) |

---

## 0. Before the game

Use the wrapper scripts, not bare `dotnet`. The mod targets **net10.0** and a distro `dotnet` 8
cannot build it — the scripts put the right SDK on PATH for you.

- [x] **0.1** `./tools/sync-import.sh` — Import/ populated, no errors.
- [x] **0.2** `./tools/build.sh` — succeeds.
- [x] **0.3** `./tools/test.sh` — the suite passes.
- [x] **0.4** `./tools/validate-parts.py` — ends "OK: N asset reference(s) resolve" with no
      errors above it.
- [ ] **0.5** `./tools/deploy.sh` — prints an install path containing `KSArmory.dll`,
      `mod.toml`, **the `KSArmory*.xml` files at the root**, `KSArmory/Weapons.xml`, and the
      `Meshes/`, `Textures/` and `Sounds/` folders. An `Assets/` folder left by a previous layout
      must have been deleted by the script — two copies of the part would fight over one Id.
- [ ] **0.6** Re-run `./tools/deploy.sh` — it must say it **registered the mod in
      manifest.toml**.
- [ ] **0.7** `./tools/setup-starmap.sh` — installs StarMap and writes `StarMapConfig.json`.
      One-off; skip if already done.

> Dropping the folder into `mods/` is **not** enough. KSA discovers mods through
> `Documents/My Games/Kitten Space Agency/manifest.toml`, and StarMap walks the same list —
> without an `[[mods]] id = "KSArmory"` entry, nothing loads.

> Running `dotnet` directly gives `error NETSDK1045: The current .NET SDK does not support
> targeting .NET 10.0`. Either use the scripts, or `source tools/env.sh` once per shell.

### Launching and reading output

Launch from the terminal — build, deploy and start in one go:

```bash
./tools/run.sh              # build, deploy, launch, show the mod's output
./tools/run.sh --verbose    # ...and KSA's own log spam as well
./tools/run.sh --no-build   # launch what is already deployed
./tools/run.sh --attach     # don't launch; follow a game that's already running
```

The default filters out KSA's several hundred startup DEBUG lines and shows only the mod's
output, StarMap's load messages, and anything that looks like a failure.

**You do not need the in-game console.** The mod writes its own log next to KSA's:

```bash
./tools/run.sh --attach                  # finds the log and follows it
tail -F "$(./tools/ksa-user-dir.sh)/Logs/KSArmory.log"
```

It is truncated at each launch, so it always shows the current session. KSA's own log
(the newest `KittenSpaceAgency.*.log`, same folder) covers mod discovery and asset loading — that is where
part XML errors would appear.

The in-game console is a nice-to-have: toggle with **`\`** (backslash), `help` lists commands,
`simspeed 1` forces real time. On an AZERTY keyboard the toggle is bound by *physical key
position*, not the printed label, so `\` is likely the key next to left-Shift or beside Enter.
If you cannot find it, ignore it — the log files cover everything on this checklist.

---

## 1. The mod loads

### 1.1 The mod loads

- [x] Launch via **`StarMap.exe`**, not `KSA.exe`.
- [x] `KittenSpaceAgency.*.log` contains `INFO found mod 'KSArmory'`.
- [x] StarMap prints `Loaded mod: KSArmory from manifest`.
- [x] `Logs/KSArmory.log` contains `loading (mod id: KSArmory)`.
- [ ] Then `ready - <every registered launcher>, safe.`

Both StarMap hooks fire. `[StarMapAllModsLoaded]` lands about **21 s** after
`[StarMapImmediateLoad]` — it waits for the game to finish loading, so the `ready` line appearing
late is normal, not a hang.

**If it fails:** no `KSArmory.log` at all means StarMap never ran the mod's entry class. Check
`mod.toml`'s `EntryAssembly = "KSArmory"` matches the DLL name, and that StarMapConfig.json
points at the right KSA folder. An exception mentioning TOML means the `assets` array in
`mod.toml` isn't tolerated there; move the part XML into a second mod folder and keep
`mod.toml` StarMap-only.

### 1.2 No XML parse errors

- [x] Nothing in `KittenSpaceAgency.*.log` about failing to load `KSArmoryAssets.xml` or
      `KSArmoryGameData.xml`.

Re-check after any XML edit.

**If it fails:** the schema differs from Core's. Compare against
`Content/Core/CoreStructuralAAssets.xml`, which the mod's XML is modelled on.

### 1.3 A weapon pack registers its own weapons

**Confirmed against KSA `2026.8.19.5261`**, with `KSArmory-example-mod` installed beside the mod.

- [x] StarMap prints `Loaded mod: ExampleMod from manifest`, and KSA `found mod 'ExampleMod'`.
- [x] `KSArmory.log` reads `pack 'ExampleMod': 3 registered`, with no fault lines.
- [x] The pack's launcher appears in the `ready -` roster beside the compiled-in ones.
- [x] The audit is silent — the pack's part Id resolves and both its markers match one subpart.
- [x] Its part renders correctly in the editor, right way up.
- [ ] Release a bomb from it, and check the sight ring against where it lands.
- [ ] The panel drives it exactly as it drives a compiled-in launcher.

**Two bugs only the flight could find**, both invisible to the suite and to every offline gate:

- `ModLibrary.Has<T>` and `TryGet<T>` dispatch through a branch chain with no `PartTemplate` case
  and fall through to `false`, so the audit called **every** part in the game undeclared. Only
  `Get<T>` reaches `AllParts`, and it reports a miss by throwing.
- The pack's mesh was exported a quarter turn out, so its bomb hung across the hull instead of
  along it. Blender's glTF importer always converts Y-up to Z-up, so a script that *round-trips* an
  atlas needs `+Y Up` **on** — the opposite of the generator, which builds in Blender coordinates
  and writes them raw. `checkmesh.py` passes a rotated body, because UV area and coplanarity are
  both fine; comparing bounding boxes against the source is what catches it.

---

## 2. The part

### 2.1 The part appears in the editor

- [x] Open the vehicle editor.
- [x] Under **Weapons**, find **Pantsir-S1 Point Defence System**.

**If it fails:** the `PartGameData` didn't register. Check the `EditorTag Value="Weapons"` — and
that the `<EditorTagDef>` declaring it is still there, since a tag nothing defines groups nothing —
and that `mod.toml`'s `assets` paths match where deploy.sh put the files. They are at the mod root,
**not** under `Assets/`.

### 2.2 The part renders

**The highest-risk item in the whole list.** The mod carries its own mesh atlas and textures, so
this is the only thing that exercises the loader path resolving them relative to the mod root.

- [x] The part is a **green 8×8 military truck**: four axles, a cab at the front, and a turret
      at the back carrying two pods of six missile tubes elevated to about 55°.
- [x] Two thin gun barrels point forward from the turret, above the cab roof line.
- [x] A large pale grey panel (the search radar) stands at the very back, leaning aft.
- [x] A pale grey array faces forward at the front of the turret, with a small dome beside it.
- [x] Colours are right: olive green body, near-black tyres, grey radar faces, dark glazing.
- [ ] **No flickering speckle** on flat surfaces — cab roof, hood, deck, turret sides. Coplanar
      faces z-fight; `box()` inflates every primitive by 8 mm to prevent it, and Blender's
      preview render does not reproduce the defect, so the game is the only place to check.
- [ ] Rough size: **8 m long, 3 m wide, 5.6 m to the tube mouths.**

**If it fails, the symptom tells you which half broke:**

| What you see | What it means |
| --- | --- |
| Nothing at all, part missing | `<MeshAtlas Path>` did not resolve. Try `Assets/Meshes/…` — the path may be relative to the mod root rather than to the XML. |
| Correct shape, **magenta** patches | UVs landed on an unpainted palette cell. Rerun `tools/model/build.sh`. |
| Correct shape, flat white / black / untextured | `<PbrMaterial>` paths did not resolve, or `.png` is not accepted for these slots after all. Fall back to `.ktx2` (needs `toktx`, not installed). |
| Correct shape, wrong proportions — short and wide | The glTF Y-up conversion got applied somewhere. `export_yup=False` in `pantsir.py` is the knob. |
| Shape right, lying on its side or buried | The part origin or the connector transform is wrong, not the mesh. |

Take a screenshot (`./tools/screenshot.sh`) either way — the preview renders in
`/mnt/c/Windows/Temp/airdefence-model` show exactly what it is supposed to look like, so the
two can be compared directly.

### 2.3 The part attaches

It stacks rather than surface-attaching. `_adConnectorAft` is a node connector with no
`<Flags>`, because `IsAllowedAsRootPart` rejects a part any of whose connectors is `ToSurface`
— so a vehicle roots and a store rides, and this is a truck.

- [ ] Attaches to the top of a 3 m stack via its node.
- [ ] Symmetry placement works (2×, 4×).
- [ ] **Stands alone** — a craft consisting of only the launcher builds and launches, with no
      command pod. This is the quickest way to test everything else.
- [ ] A director or another store can be mounted **on** it. It carries `NoFaceSnapping` and
      deliberately not Core's `Radial`, which is a face-snap target blacklist that beats the
      `Weapons` whitelist.

**If it fails:** the connector Ids must match exactly between GameData and Assets, and the
Assets one needs a `<Transform>`.

### 2.4 The part behaves physically

- [ ] Craft mass increases by **~30 t** per launcher (it is a whole vehicle, not a pod).
- [ ] It doesn't clip weirdly or fall through the launchpad. The hull collider starts at the
      ground plane, so it should rest on its wheels rather than sink to the axles.
- [ ] Craft launches without the part exploding or detaching.
- [ ] Thirty tonnes on a small stack does not fold the craft in half on the pad.

---

## 3. Panel and detection

### 3.1 The panel appears

- [x] In flight, a **KSArmory** window is visible.
- [x] Closing it leaves a small **KSArmory** button that reopens it.
- [ ] The **Components** tab lists the Pantsir's launcher, gun, sensor and fire-control rows.
- [ ] The strip above the tabs shows auto-engage off and a full twelve-round tally.

**If it fails:** `no weapons system on this craft` while the part *is* on it means
`LauncherPart.Find` isn't matching — `Part.Id` may not equal the `PartGameData` Id, or the part
is registered as a launcher and missing from `Arsenal.Components`, which is silent everywhere
else.

### 3.1b The turret slews

**The open question is whether KSA honours a runtime transform write at all.** Everything else
here is instrumented so the answer is readable either way.

- [ ] `KSArmory.log` has a `launcher subparts: ...` line naming both subparts. If the turret
      one is missing or oddly named, `Part.ResolveRuntimeId` rewrote it and the profile's
      `TurretMarker` needs to match whatever is actually there.
- [ ] The panel shows `Turret: N deg` and not `subpart not found` or `engine refused`.
- [ ] A **cyan line** points out from above the vehicle. That is where the drive *thinks* it is
      aimed — it comes from the mod's own maths, not from the engine.
- [ ] Spawn a test target. The cyan line sweeps round to follow it, taking about a second.
- [ ] **The turret mesh follows the cyan line.** This is the actual test.
- [ ] The twelve tube markers stay on the container mouths as it turns.
- [ ] Turning **Track with turret** off on the launcher's *Components* row returns it to facing
      forward.

**If the line moves but the mesh does not:** the slew maths is right and the engine is ignoring
`Asmb2ParentAsmb`. Next thing to try is splitting the turret into its own `<Part>` joined by a
node, rather than a subpart.

**If the turret swings round the chassis instead of spinning in place:** the mesh recentring and
the XML `<Position>` disagree, or KSA composes child transforms rotate-then-translate rather
than translate-then-rotate. Compare `TURRET_PIVOT` in `pantsir.py` against the `<Position>` on
`KSArmory_Launcher_Turret`.

### 3.2 The search cone is drawn

- [x] A blue wireframe cone extends from the launcher.
- [x] It points **away from the planet** (straight up when you're on the pad).
- [ ] **Twelve** dots at the tube mouths: **green** = loaded. They must sit *on* the container
      mouths, not in a ring floating beside them — that is what `validate-parts.py`'s launch
      geometry check is guarding, but only the game proves it.
- [ ] Adjusting *Tuning → Radar*'s **Range** and **Cone half-angle** resizes it live.

**If it fails:** cone pointing sideways or through the ground means `KsaWorld.LocalUp` is
picking a bad parent body. Cone missing entirely means gizmo rendering isn't reaching the
screen — check `Program.GizmosRenderer` is non-null at that point.

### 3.3 Radar sees a target

**Skip the editor.** `./tools/install-testcraft.sh` writes a ready-to-fly craft ("AA Defence
Site") into your vehicle saves — a single Pantsir-S1, which is its own command source.

To fly it: **File → Launch Existing Vehicle**, pick **AA Defence Site** from the *Vehicle Save*
dropdown, then **Launch Vehicle**. Saves are only re-scanned at startup, so relaunch the game
after installing the craft.

> If **Launch Existing Vehicle** is **greyed out**, KSA found no user vehicle saves at all —
> the craft file did not load. If the menu item is live but the craft is missing from the
> dropdown, the folder was read but that save was rejected.

**Use the built-in spawner.** *Debug tools → Test targets*: set time-to-pass, speed and miss
distance, then press **Overhead**, **Head-on** or **Passing by**. A drone appears that far out on
exactly that course, so a 60 s / 400 m/s overhead pass spawns 24 km away and arrives in a minute.
Drones are clones of your own craft, so no second craft needed.

Arm the battery *before* the drone arrives — with **Auto engage** on it will handle the rest.

- [ ] Spawn a drone while the world is running, several times in a row. The spawner now waits for the
      vehicle worker to release the shapes registry before building the drone. Racing it built half a
      vehicle, logged `test target spawn failed: … shapes registry cannot be mutated while the vehicle
      update is stepping`, and the game died on a `NullReferenceException` a fifth of a second later.
      Expect at most a one-frame stall on the button press, and never that line.

Doing it by hand instead: launch a craft, leave it flying, launch a second with the launcher
and switch control. Two craft parked on the pad won't work — nothing is moving, and the radar
filters out anything below *Min target speed* (default 15 m/s).

- [ ] With another vessel within range and moving, it appears under **Tracks**.
- [ ] Its range, CPA and time-to-CPA update sensibly.
- [ ] Orange marker sphere drawn at its position.
- [ ] Moving out of the cone or beyond range drops it from the list.

**If it fails:** empty track list with a vessel clearly nearby means `Program.VehiclesInFrame`
isn't returning loaded vehicles. Try widening *Cone half-angle* to 180 and *Range* to max
first — that rules out geometry before blaming the API.

### 3.4 Threat classification

The distinction that matters: **passing by** should engage, not just **incoming**.

- [ ] A vessel heading roughly at you: marked as a threat (red), `CPA` small.
- [ ] A vessel crossing nearby but not aimed at you: **also** marked a threat, if its CPA is
      under *Threat radius*.
- [ ] A vessel heading away: shown as a track but **not** a threat.
- [ ] Lock indicator goes `acquiring...` → `LOCKED` after *Lock time* (1.5 s).

---

## 4. Engagement

Do all of this at `simspeed 1`.

### 4.1 Manual fire

- [ ] With a lock and nothing else touched, press **FIRE**. There is no arming step.
- [ ] `Rounds` drops to 11/12; one muzzle dot turns grey.
- [ ] A tracer leaves that tube with a trail behind it.
- [ ] Console logs `[KSArmory] round N away at <target> (X.X km)`.

**If it fails:** "refused: …" in the log tells you which gate stopped it — no launcher,
empty, or target gone.

- [ ] On a Pantsir, the **Weapons** button is on the header strip, and the window lists
      `Pantsir-S1: Missiles` and `Pantsir-S1: Cannon` as two rows. Select the cannon: the header reads
      `showing Pantsir-S1 (1): Cannon`, and FIRE with nothing detected fires a burst. The line beside
      it reads `Auto-engage held: nothing detected -- trigger is clear`, not `Holding fire`.
- [ ] Back on the missiles, with no threat locked, shift-click a craft the radar can see and press
      FIRE: a round leaves for *that* craft. The line beside the trigger does not say `Holding fire`
      about it, even if it is not closing.
- [ ] Shift-click a craft beyond the radar's reach: the line reads `Holding fire: <name> is not on
      the radar`, and FIRE refuses with the same words.
- [ ] With auto-engage on, shift-click a craft that is not a threat and inside the guns' reach: the
      guns do **not** open fire on it by themselves.

### 4.1a A low shot from a site near sea level, at 1x

- [ ] From a Pantsir parked near the coast, fire at a target on the ground or skimming it, at 1x.
      The rounds keep flying after the motor burns out and reach the target. The fault this checks
      reads in the log as `expired after 30.0s - ... flew 0.8 km, final speed 19 m/s`: a round
      flown through water because its altitude was read a frame of the planet's travel low.

### 4.2 The round guides

- [ ] The tracer visibly **turns** toward the target rather than flying straight.
- [ ] A red line connects round to target (the seeker line).
- [ ] It accelerates for the first ~2 s (boost), then coasts.

### 4.3 A crossing target is intercepted

This is the headline behaviour and the thing the headless tests prove in isolation.

- [ ] Against a target crossing your position, the round **leads** it — aims at where the
      target is going, not where it is.
- [ ] It detonates near the target.
- [ ] Console logs `round N detonated with the target at X m` with X under ~25 m.

      That number is the range at which the **fuse** fired, bounded by the round's own fuse radius
      plus the target's `MeanRadius` — never how far it missed by. A proximity-fused round reports
      its own envelope on every good shot, so a burst at exactly the trigger is the weapon working.
      What a bad shot looks like is `expired`, with the closest approach on the same line.

**If it fails:** consistently missing behind means the lead isn't working; raise *Nav constant*
and *Max lateral g* and see if it improves. Report the trigger ranges — the headless tests pass,
so a real-world failure points at frame timing or the target-state sampling, not the maths.

### 4.4 The warhead kills

- [ ] Target vessel is destroyed on a close detonation.
- [ ] Console logs `destroyed <name>`.
- [ ] A detonation between *Lethal radius* and *Blast radius* leaves the target flying, and either
      logs `near miss on <name>` or `N part(s) broken off <name>`.

#### 4.4a Individual parts break — **flown once against a drone; most of it still unverified**

Flown on `2026.9.7.5402` with `scenario.sh head-on`: a Sidewinder burst 16 m from a 21-part drone
broke **7 parts**, KSA logged each one against its own crash tolerance, and the craft fragmented
into 11 vehicles. Zero exceptions and zero engine errors.

That run is also where the ordering fault was found and fixed: applying `PartFailureEvent` from a
mod hook threw `ArgumentOutOfRangeException` out of `FlightComputer.UpdateTvcParams` every frame
afterwards, for every fragment. The event is queued for the engine now — `docs/KSA-FRAME-ORDER.md`.
**Watch for that exception returning**; it is the failure shape this whole design avoids.

What that run did *not* touch is everything below.

`Settings -> Break individual parts, not whole craft`, on by default. Every part is judged on its
own distance and the strength KSA derives for it, and losing half a craft's parts at once still
destroys it. Untick it to get the old binary kill back and compare.

- [x] Parts break rather than the craft dying, and KSA logs each against its crash tolerance.
- [ ] A burst against one end of a **long multi-part craft** breaks parts near the burst and leaves
      the far end flying. The drone was small enough that the burst reached most of it.
- [ ] The remainder is a real craft: it still has a name, still appears in the panel's craft list,
      and the radar still holds it.
- [ ] A burst that engulfs a **small** craft destroys it outright rather than shedding fragments —
      that is `TrippedTheFragmentGuard`, and it is what keeps a drone kill a kill.
- [ ] KSA logs `Part '<id>' destroyed - exceeded its crash tolerance of ...` per part. If the mod
      says parts broke and KSA says nothing, `PartFailureEvent.Apply` refused them.
- [x] No engine exception on the frames after a craft fragments. This is the one that failed first.
- [ ] Frame time after a craft fragments. One craft becomes several and every fragment is
      simulated — about 2 ms each, `docs/METRE-LEVEL.md` §5b. This is what the toggle is for.
- [ ] Shoot the **launcher part** off a live craft. The system should go loose, fly its rounds and
      retire — log `lost its launcher part and nothing else carries one`. It must not sit there
      searching, and the panel must not keep offering a system that cannot fire.
- [ ] Same for a **director**: log `has been destroyed and nothing else carries one`.
- [ ] With the setting **off**, a lethal burst destroys the whole craft exactly as it used to.

### 4.5 Salvo and auto-engage

- [ ] Tick **Auto engage**, present a threat, and let it work unattended.
- [ ] It fires *Rounds per target* rounds (default 2), spaced by *Salvo spacing* (0.45 s).
- [ ] It does **not** dump all twelve at one target.
- [ ] After twelve rounds, `Rounds: 0/12` and a reload progress bar appears.
- [ ] After *Reload time* (12 s), back to 12/12 and `launcher reloaded`.

### 4.6 Manual designation

- [ ] With several tracks, press **designate** on a non-priority one.
- [ ] Lock switches to it and stays there.
- [ ] **Clear designation** returns to automatic priority.

---

## 5. Safety

Do these deliberately — a failure here is the kind that ruins a save.

- [x] **5.1** With **Never target the vehicle I'm flying** ticked, it never locks or fires on
      your own craft.
- [x] **5.2** A round fired at a close target does not destroy your own launcher platform
      (the launcher is in neither its own contact nor its own blast sweep, and the proximity fuse
      arms late on top of that).
- [ ] **5.3** **Safe all** removes rounds in flight with no detonation, **and turns auto-engage
      off**. Without that, a guarding system holding a lock fires again immediately and the
      button appears to do nothing.
- [ ] **5.4** With auto-engage off nothing launches on its own, however good the lock, and
      **FIRE** still does.

---

## 6. Robustness

Where latent bugs are most likely.

- [ ] **6.1** **Timewarp** — raise sim speed with rounds in flight. Nothing should NaN, crash,
      or spam the log. Rounds behaving oddly under warp is acceptable; a crash isn't.
- [ ] **6.2** **Scene change** — go flight → editor → flight. Panel recovers, no exceptions.

      **Reported broken**: with the optical head driving the main view, switching to the vehicle
      editor leaves the view in a bad state. The log settled it — the restore did *not* fail, it
      succeeded: `sight: released the main view` with no warning. So the mod was writing a camera
      mode and a followed craft belonging to the flight scene onto the editor, which had already
      loaded. It now **forgets** rather than restores when it is no longer in flight, on the
      grounds that the new scene brings its own camera and a dead scene's is not worth handing
      back. Restoring is still what happens when the optic is switched off *in* flight, which is
      the case the recording is actually for.
- [ ] **6.2b** **Camera switching mid-engagement** — fire a salvo, then switch the camera to the
      drone and back. The cone, track markers and tracers must stay locked to the craft and to
      each other. *(`GetPositionEgo` takes a different branch depending on what the camera
      follows, so a mismatch there shifts the whole overlay and makes hits look like misses. The
      log is the arbiter — the fuse trigger ranges stay ~22 m either way.)*
- [ ] **6.3** **Target dies mid-flight** — destroy the target another way while rounds chase it.
      They should lose lock and expire, not throw.
- [ ] **6.3b** **Rocket smoke trail** — fire a Sidewinder or a HARM and watch the trail behind it
  while the motor burns, then that it stops laying at burnout and the trail stays put and drifts.
  Nothing on an airless world or above the atmosphere, which is the renderer's own limit. Check a
  CIWS burst does **not** lay one (`TotalBoostSeconds` is zero). The mushroom cloud no longer
  competes for the same budget — it left the trail renderer for its own raymarch — so a salvo
  beside a standing cloud can only evict other motor trails.
- [ ] **6.4** **Platform destroyed** with rounds in flight — the rounds **carry on** rather than
  vanishing, and still detonate and kill. No exception spam. Check the log says
  `<craft> destroyed - N round(s) still in the air`, then `last round down, system forgotten`.
  A command-link round (the Pantsir's 57E6) should coast and expire; a Sidewinder or HARM should
  still hit. **Watch a CIWS burst for this one** — a shell keeps its tracer the whole way, so the
  stream should carry on across the frame its gun is destroyed rather than stopping dead. A missile
  keeps its flame only while boosting and has no body once loose, so past burnout there is nothing
  to see and the log is the only witness. See `docs/CODE-HEALTH.md`.
- [ ] **6.5** **Switching control away** — a system is pinned to the craft carrying its launcher
      when it is crewed and nothing moves it after, so taking control of something else leaves it
      defending itself. There is no button: pinning is not the operator's.
- [ ] **6.6** **Staging away the launcher** — the craft reports no weapons system, firing refuses.
- [ ] **6.7** **Two launchers** on one craft — **two** weapons, each with its own magazine, arm
      switch and rounds in the air. The header's selector chooses which one the panel and the
      trigger are pointed at.
- [ ] **6.8** **Long session** — leave auto-engage on for a while. No unbounded log growth, no
      frame-rate decay.
- [ ] **6.9** **Fault handling** — if anything throws, the console shows
      `[KSArmory] ERROR … (n/10)`. After 10 it disables itself rather than spamming. If you
      see this, **copy those lines** — they are the most useful thing to report.

---

## 7. Gaps — never exercised at all

Paths that have never once run. These are not "probably fine".

### 7.1 Save / load with the part fitted  ← highest risk

- [x] Build a craft with the Pantsir, save the game, quit to menu, reload. Craft intact, part present.
- [ ] Save *while rounds are in flight*, reload. No exception; rounds simply gone is fine.
- [x] A save made with the mod active still loads with the mod **removed** — **it does not, and
      it does not fail cleanly.** `PartInstance.GetTemplate` calls `ModLibrary.Get<PartTemplate>`,
      which throws `NullReferenceException` for an Id nothing declares. Same family as removing a
      subpart. Proved by removing the Mk 82 rack: the three instances across two saves had to be
      lifted out of `universe.xml` by hand first.

**Why it matters:** the part goes into the save's part tree. If KSA cannot resolve
`KSArmory_Prefab_Launcher6` on load, the craft — or the whole save — may fail.

Saving with rounds in flight, and loading with the mod removed, are the two still untried.

### 7.1b Timed airburst (flak)

Untested in game. `MunitionProfile.TimedFuse` makes the cannon fuse each shell for the
flight time of the lead solution it was aimed with; the proximity fuse still fires first if
something arrives early.

**The 5"/54 is the first weapon to ship with it on** (7.1d3), so this path now runs whether or not
anybody sets out to test it — where before it was a field nothing enabled. Everything below is
therefore about the Mk 42 unless the box says otherwise, and the last box has become the
regression test rather than a curiosity.

- [ ] Fire the Mk 42 at a crossing drone. Shells burst **at** the target's predicted position
      rather than flying past it.
- [ ] The burst is visible. The Mk 42's 3.3 kg charge is twenty times the 20 mm's, so this is the
      one case where the drawn effect should read on its own without the floor — if it does not,
      the floor is hiding something rather than helping.
- [ ] A burst with the target already dead does not count as a hit. `MissDistance` is infinity when
      nothing is being tracked, and the kill path must not treat that as zero.
- [ ] The CIWS is unchanged. Its 20 mm leaves `TimedFuse` off, so the whole flak path must still be
      inert for it: same burst behaviour, same kills, nothing bursting early in the air.

### 7.1c Horizon masking

The controls for this were unreachable until now — `HorizonMasking` and the limb margin had no
panel control and no profile set them — so every box below has been untestable rather than
untested. They are under a system's *Tuning → Radar*.

- [ ] Put a drone on the far side of the planet. It does **not** appear on the scope, and the
      panel says `N behind the horizon` rather than showing an empty list with no explanation.
- [ ] A drone overhead and a neighbour on the same pad are both still seen. If short-range
      contacts vanish, the mount is being treated as sitting at mean radius.
- [ ] Raise **Limb margin** and watch low contacts drop out at shorter range.
- [ ] With **Horizon masking** off, everything is visible again exactly as before.

### 7.1c2 Terrain masking, and what it costs

Ships at zero samples, which is the mean sphere alone. **The point of flying this is the frame
time**, not the behaviour: the behaviour is covered by `TerrainMaskTests` against a synthetic
ridge, and the cost is the thing no test can answer.

- [ ] Note the frame time with **Terrain samples** at 0. Raise it to 16, then 64, with a dozen
      contacts up. Write down all three. That measurement is the whole reason this is a number
      and not a switch, and `SensorProfile.TerrainSamples`' default should be set from it.
- [ ] A drone low behind a ridge disappears from the scope; the same drone climbing reappears.
      If it never disappears, the samples are not reaching the ridge — try more of them, since
      they are spread over the whole band that passes under the body's highest terrain.
- [ ] A launcher on a slope still sees along its own ground. If it goes blind at close range,
      **Terrain clearance** is too low and the height map is finding the hill the site stands on.
- [ ] Contacts high above the ground cost nothing: raising the sample count with everything at
      altitude does not move the frame time. That is `TryBandBelow` doing its job, and if the cost
      *does* move, it is not.
- [ ] Nothing throws over a body with no height map, and over a moon.

### 7.1g Shooting down a round  ← never once worked, so nothing here has ever been seen

Rounds have been visible to radar and engageable by fire control from the start, and could not be
hit by anything: a round is not a `Vehicle`, so the shell's contact list never held one, the
missile's target sample refused one, and the kill path had no way to reach one. What changed is
`IProjectile.ShootDown` and the three seams that now find their way to it. **All of it is
unverified in flight** — `RoundInterceptTests` covers the state machine and the shell geometry,
and nothing under `Sim/` can reach the wiring.

- [ ] Two sites, opposite teams, one firing at the other. The defender's **cannon** shells reach
      the incoming round and the log says `intercepted <name> at <n> m`. Before this, the same
      engagement fired the whole belt and the missile arrived regardless.
- [ ] The intercepted round disappears — from the scope, from the world, and its body with it. Its
      own launcher says `round N was shot down after <t>s`.
- [ ] It does **not** explode where it was intercepted. `ShotDown` is not `Detonated`, and an
      explosion there means something is reading the two as one.
- [ ] The defender's **missiles** can do it too, by proximity rather than contact. Needs a target
      further out than the 1.2 km minimum range — inside that the cannon is the only answer, which
      is what `holding fire: target out of reach` says when it happens.
- [ ] Nothing shoots down its own salvo. Every launcher filters its own rounds out before the radar
      sees them, but a *second* launcher on the same craft is a separate system with its own list.
- [ ] Frame time with a full CIWS burst up against a salvo of incoming rounds. The designated-target
      path is one extra sweep per shell, which should be nothing — but that is a prediction.
- [ ] Two defenders engaging one missile, at different simulation speeds and under warp. Both
      should agree on whether it died; if they disagree, the airborne sample is not being read at
      one instant and `docs/FRAMES-AND-EPOCHS.md` says why that matters.

### 7.1c3 Telling targets apart — size, Doppler and clutter

All three ship at zero, which is the behaviour every earlier section was flown against. The
arithmetic is in `RadarSignatureTests`; what a flight adds is whether the numbers land anywhere
useful against real craft, whose `MeanRadius` is a bounding half-diagonal rather than a skin.

- [ ] Read the **Reference RCS** line with it at zero: the set reaches the same distance whatever
      it looks at, and the track list matches what earlier sections recorded.
- [ ] Set it to roughly a drone's own cross-section and confirm a drone is still detected at about
      the profile range. Wildly short means `MeanRadius` is much larger than assumed and the
      reference needs to be too.
- [ ] With it set, fire a round and watch a *second* system see it. A round should appear at a
      fraction of the range a craft does, not at the same range and not never.
- [ ] **Doppler notch** to 40 m/s, then fly a drone across the site rather than at it. It drops off
      the scope near the beam and comes back either side. That loss is the feature.
- [ ] Confirm the same drone flown straight at the site is unaffected.
- [ ] **Clutter floor** to a few hundred metres: low contacts vanish, high ones do not. Then put it
      back to zero, because the Pantsir exists to kill things down there.
- [ ] All three back at zero: the track list is exactly what section 3.3 recorded.

### 7.1d The Sidewinder rail

**`./tools/scenario.sh head-on` flies this unattended and kills the target.** The log shows the
LAU-7 rail crewed, armed with 1 round, target away, round away, and KSA reporting
`Vehicle 'AD Test Drone 1' destroyed by Collision (50.0 g)`. So the fixed-launcher path crews,
arms, fires and kills.

What a log cannot answer is appearance: the screenshot lands after the kill, so it never shows the
round leaving the rail. The unticked boxes below are those.

A *fixed* launcher — no turret, no pods, one round, no reload — takes paths the Pantsir never
does, so a green suite says little about it.

- [ ] The rail appears in the editor under **Weapons**, and **surface-attaches** to the side of a
      stack. It is not a command source, so a craft made only of a rail will not fly — expected.
- [ ] The AIM-9J is **visible on the rail** before firing. A tube launcher hides its rounds inside
      the containers; a rail cannot, so `TubeVisual.Loaded` is exercised here and nowhere else.
- [ ] The round sits along the rail, fins in an X straddling it. A roll that puts one fin through
      the rail means the seating rotation is not the shortest one `RotationFromTo` promises.
- [ ] It fires. `Trains` is false, so `IsLaid` is true from the start and nothing waits for drives
      that do not exist — the failure to look for is fire control deadlocking rather than missing.
- [ ] The round leaves **along the rail** and is clear of the craft before it turns.
      `LaunchAlongTube` is true and the round coasts for `MunitionProfile.SeparationSeconds`
      before steering, so the arc it then flies is bounded by its own lateral g. Watch for a
      round departing *through* the craft carrying it.
- [ ] The search volume follows the craft's attitude, not local up. `BoresightSource` is
      `PartForward` — pitch the craft over and the cone must go with it.
- [ ] After the one round is gone the launcher stays empty. `ReloadSeconds` is zero.

- [ ] **A Pantsir and a rail in the same world at once.** Each keeps its own search cone, round and
      envelope in the panel and in the overlay. Profiles belong to the system for exactly this
      case: a session-scoped profile shows up here as one site drawing the other's numbers, and
      nowhere else.

- [ ] Two rails on one craft: **two** weapons. The roster keys on the craft and the launcher's
      ordinal, so each has its own magazine and arm switch, and the header's selector chooses which
      one the trigger is pointed at. Dropping one must not refill the other.

### 7.1d2 The AMRAAM rail — it loads and lays, and nothing past that is confirmed

**It reached the world.** From `KSArmory.log`, first session after it shipped:

```
ready - Pantsir-S1, LAU-7 Sidewinder rail, LAU-128 AMRAAM rail, Mk 15 Phalanx, ...
LAU-128 AMRAAM rail tracking AA Defence Site
LAU-128 AMRAAM rail: turret on AA Defence Site -- driving at 1.8 km
holding fire: auto-engage is off
```

So the part loads, is recognised by the survey, gets crewed, acquires a target and lays on it, and
fire control gates it for the right reason. That covers registration and the whole acquisition
path — which is most of what a *new profile* can get wrong, and none of what new *art* can.

**Everything below is still unverified**, because a log says nothing about appearance and this is
the first part in the mod whose art was authored in Blender rather than generated — so the export
contract is being trusted rather than demonstrated. Nothing has been seen to fire, either.

It is mechanically the LAU-7 (7.1d), so that section's list applies whole and is not repeated. What
is genuinely new:

- [ ] The part appears in the editor under **Weapons** and surface-attaches. Its collider is
      hand-declared from the mesh bounds rather than read off a `_ColPrim_` node, so a part that
      cannot be placed or that snaps oddly points here first.
- [ ] The round and the rail **render textured**. They share one material across two subparts,
      which nothing else in the mod does — an untextured or black body means the atlas, the
      material Id or a texture path, and `validate-parts.py` cannot see a path that resolves to
      the wrong image.
- [ ] No **speckle or sparkle** anywhere on either body, at any range, and specifically where the
      hanger lugs sit between the shoe cheeks. The cross-body pass now honours `<Rotation>` and
      reports this part clean, which it could not have done before — the round is seated with a
      quarter turn, and until that landed the pass was comparing a body lying across the launcher
      rather than along it. So this is checked rather than assumed; what a checker cannot see is
      whether KSA's renderer agrees.
- [ ] The round sits **nose-forward on the rail**, its tip level with the rail's forward fairing
      and its tail fins just aft of the beam. The mesh is centred on its own origin and the seat
      offset assumes it: a round half a body length out of place means that assumption broke.
- [ ] The seated round does not **jump** when the mod takes over on the first frame. The XML seat
      and what `TrySeatMissile` computes are the same numbers by construction, and
      `validate-parts.py` now checks that, but only against the committed files.
- [ ] It reaches. The envelope is 105 km on paper and the round is boost-only, so a long shot
      should arrive slow and turn badly — the failure to look for is a round that holds speed
      like a sustainer, which would mean `DragK` is wrong rather than the guidance.
- [ ] **A LAU-7 and a LAU-128 on separate craft in one world.** Two fixed launchers with different
      rounds and different seekers is the case the per-system profiles exist for, and it has never
      been run.

### 7.1d3 The 5"/54 Mk 42 mount — it loads and crews; nothing it does has been seen

**Confirmed from the log**, first version: it registers (`ready - ... 5"/54 Mk 42`), is crewed, and
resolves its turret and cannon by name, with no asset or XML error in KSA's own log. One shell was
fired and fell through the planet, reaching 725 km/s — which is what `HitsTerrain` on its profile now
stops, and that is unflown too.

Everything below is Mallikas's second version — a barrel that recoils, a shell body and a painted texture set — and none of it has been flown. The headless gates are green:
`checkmesh.py` clean, `validate-parts.py` holding the trunnion, the barrel, the muzzle and the shell to
the mesh and the XML, and the suite.

**The model**

- [ ] It renders painted rather than white or magenta, and the shell body carries its olive and yellow.
- [ ] It sits upright on a 3 m node with the barrel forward. The model was rotated into part space by
      a map baked into the vertices, and a frame error there is the mount on its side, not something subtle.
- [x] On a 2 m or a 3 m tank its base sits on the tank's end, not inside it. **Seen in game**; the
      CIWS, fixed the same way, not looked at. Both
      nodes were unsized, so KSA mated them on the tank's nested `Internal` node: saved at 1.92 m on a
      4 m tank rather than 2.00. Only a mount attached afresh moves; a saved one keeps its position.
- [ ] Moving a Mk 42 on its tank with the craft mover leaves the tank whole. Reported breaking it, and
      probably not the overlap: parts of one craft never collide. KSA breaks a part whose contact
      pressure beats its crash tolerance, and a near-empty tank's is the 0.9 MPa floor, which 61.4 t
      settling at half a metre a second on 3.1 m² already reaches. If it still breaks, `crash
      tolerance` in KSA's own log says which part and whether this is it.
- [ ] The barrel stays in the cannon through a full traverse and from -15 to +85. It rides the
      cannon's trunnion, so it should never part from the breech; if it does, the barrel's `<Position>`
      and the trunnion disagree.
- [ ] Nothing checks the barrel against the gun house roof at high elevation: `checkswept.py` sweeps
      only the vehicles named in `vehicles()`, and this is not one.

**Recoil**

- [ ] Each shot runs the barrel back and eases it home in about two thirds of a second. The numbers
      are set by eye; if it reads as a twitch or a slide, `GunRecoilMetres`, `GunRecoilSeconds` and
      `GunReturnSeconds` are the three to move.
- [ ] Pausing freezes a barrel mid-recoil rather than finishing it: recoil runs on simulated time.

**Shell bodies**

- [x] A shell in flight is drawn as the shell. **Seen in game.**
- [ ] It flies nose first and carries its olive and yellow; easiest from the chase camera, since at
      807.7 m/s it covers thirteen metres a frame.
- [ ] A shell drawn as a body has no streak line and no glowing tracer on it. Those are for a shell
      with nothing else on screen, and were drawn on top of the body when it was first seen.
- [ ] A shell that bursts or lands takes its body with it; nothing is left hanging in the air.
- [ ] With more than ten in the air the rest draw as streaks and tracers, and the log says so once.
- [ ] The shell is 78 mm across where a real five-inch shell is 127 mm. Worth a look against the bore
      before asking Mallikas whether that was meant.
- [ ] The Pantsir's missiles and the CIWS still draw as before, streaks and tracers included. Tube
      bodies are now searched only on a launcher with tubes, so a CIWS session should also stop
      opening with `no round bodies`.

**Accuracy against a target that is not flying straight**

- [ ] Long shots hit a test drone. Flown before this, the miss matched half a gravity drop times the
      flight time squared at every range — 38 m at 2.8 s, 255 m at 7.3 s, 470 m at 8.7 s — because
      the lead predicted the drone along its velocity and an unpowered drone slows and falls. The
      lead now adds the target's own acceleration, read off the engine with gravity put back.
- [ ] After each timed burst, `lead check` splits the burst's offset along the drone's track, up and
      right, beside what a lead assuming it held its velocity would have missed by. With the fix
      working the first is small while the second is large. If the two still agree, the acceleration
      never reached the lead.
- [ ] A craft in steady level flight is led as it was: it is held up, so its acceleration reads near
      zero. If shots against one start missing, the engine's `AccelerationBody` does not mean what
      `KsaWorld.AccelerationEcl` assumes.
- [ ] A distant craft on rails may carry a stale measured acceleration. Worth one engagement.
- [ ] Timed bursts land on the drone at range, not short of it. The lead timed the fuse as distance
      over muzzle speed, and the air slows the shell: flown, the misses grew with flight time, 16 m at
      2.9 s to 201 m at 6.5 s, and headlessly the same lead bursts 1.6 km short at 15.7 km. The lead now
      flies the shell through `Medium.Drag` and the body's own pull, and headlessly bursts within a
      metre on Earth, the Moon, Mars and a 250 km asteroid (`FlownLeadTests`). `lead check` along the
      track should now be metres, not hundreds.
      **Flown 2026-09-14** with `scenario.sh gunnery` (4 drones passing 3 km off at 250 m/s): inside
      ~4 km, 5–11 m; further out the burst lands *behind* the drone, 152 m at 8.4 s, 77 m at 8.0 s,
      26 m at 7.3 s. That is the target's prediction, not the shell's: a velocity-only lead would have
      been 294 m ahead, so holding the drone's current deceleration for the whole flight overshoots a
      drag that eases as it slows. A persistent −5 m along track and +3 m right remain at short range.
      **Flown again** with the target's drag flown as drag (`BallisticLead.Flight`), same scenario:
      median 11.4 m against 17.4, every drone with a burst inside the lethal radius against one, and
      the first shell at 6.3 km and 8.3 s a contact hit where it had been 152 m behind. Along track
      the bursts are now 2–9 m.
- [ ] A burst that breaks parts off a target no longer throws the next leads high. It did: the
      engine's `AccelerationBody` is an accelerometer — the step's change of velocity less gravity,
      over the step, impulses included — so it spikes on a blast, and the lead flew the spike, 36–40 m
      high on the next shells and 154–232 m on a fragment tumbling away. Reading the median change of
      velocity over half a second *instead* **lost, flown**: median 16.0 m against 11.4, because it
      lags — every first shell 16 m behind its drone at 8.3 s — and the ±25 m vertical misses after a
      hit stayed, so those were never accelerometer spikes. The radar now keeps the accelerometer and
      overrules it with the history only when they disagree by more than 3 m/s²
      (`AccelerationEstimate.Believe`). **Flown once**, same scenario: PASS, every drone killed by
      its first shell on contact at 6.3 km and 8.3 s. The first shell killing means no shot was
      fired after a hit, so the case the check exists for was not exercised.
- [ ] Most shells go at something already dead, **by choice**. 47 of 66 in one run: about four of
      every five fired at a target are still in the air when the first kills it, and the gun then
      engages the pieces the kill split it into. Those come from `PartFailure.IsolateAndDestroy`,
      which splits at joints and marks nothing, so only the small pieces `ShedDebris` makes carry
      `IsDebris` — and those are no longer threats. Kept as a barrage, as the real mount fires, with
      599 rounds to spend; a shoot-look-shoot cap and not engaging uncontrollable pieces were both
      considered and declined.
- [x] A burst is drawn where the shell was when it went off, not beside it. **Seen in game.** Reported from play as
      airbursts jumping to one side of the target: a round's offset on the frame it bursts pairs its
      mid-frame position with the frame-end platform, so the burst carried the platform's ecliptic
      motion over the rest of that frame — 377 m at 60 fps, always the same way
      (`BurstOffsetTests`, `DrawAnchor.OffsetAtBurst`). Missile bursts go through the same call and
      should have stopped jumping too; not yet looked at.
- [ ] Long shots are biased high and behind. `scenario.sh gunnery:3,passing,55,300,4000`: from 13.2 km
      (18 s) the bursts run −12 m along the drone's track and +15 m up, peaking at −41 m and +62 m
      around 12 km and closing to −6/+16 m by 8 km; median 43 m. A velocity-only lead would have been
      700 m and 1.2 km out. The lead is predicting about half a metre a second squared more fall and
      slowing than the drone has.
      **Measured since** (`gunnery:2,overhead,30,300,1500`, the drone's own motion logged twice a
      second): no lift and no push, and a drag coefficient that drifts — 1.09e-3 to 0.82e-3 over the
      pass — with the accelerometer and the velocity agreeing within 3%. The barrel is within 0.06 mrad
      of the lead at every shot, so it is not the gun still laying. The engine's drag has no Mach or
      speed term: it is a drag box fixed to the body (`BoundingBoxCdA`) plus a skin term, and the
      drone holds its attitude while its path bends, so the air meets a different area — 65.8 m²
      falling to 52.9 while slowing over that area held at 0.6125 to four places. The lead now flies
      the target's drag box against its turning airflow (`Sim/DragShape.cs`). A fit of the coefficient
      against airspeed was flown first and fixed the 9 km pass but not the 12.6 km one, because it was
      the wrong law. Flown overhead at 300 m/s, three drones each: at 9 km the first shell burst on
      every drone at 7 km; at 12.6 km, where the first five had burst 35–49 m off, the first shell
      burst on every drone at 10 km. Then three drones each, first shell on every one: passing
      (250 m/s, 3 km off, 6.3 km), head-on (6 km), overhead from 15 km (12.4 km), overhead at 150 m/s
      (9.3 km), and tumbling at 20°/s (7 km), where the area predicted half a second ahead matched
      the engine's within 0.1 m². With the engine lit the lead first held the thrust, and every burst
      went 19.5 m and further under the drone as it burned mass away; carrying the mass flow off the
      engines' exhaust velocity put the first shell on every drone at 8.1 km. Incoming rounds now
      report their pull and drag, unflown against a gun. The whole model is a copy of today's
      engine drag, which RocketWerkz are reworking — `docs/BLOCKED-ON-KSA.md`.
- [ ] The Phalanx and the Pantsir's cannon go through the same flown lead. Their shells are short-lived,
      so the change is small, but it is unflown: a CIWS against a crossing drone should hit as before.
- [ ] Frame time with a Mk 42 tracking is not visibly worse. A solve flies the shell a few hundred steps
      per pass and starts from the last frame's answer; it has not been measured in a frame.

**Aiming at the ground**

- [ ] Mouse-aim the Mk 42 at flat ground 2, 8 and 15 km out and fire. The shells come down under the
      cursor, not short of it, and the barrel visibly elevates further as the cursor moves out. Laid
      along the line of sight it had fired level and landed within a kilometre or two whatever the
      range. Headless, the flown lay lands within 8 m out to 15 km on a round Earth
      (`GroundLayTests`).
- [ ] Over sky, or over a craft, mouse aim still lays along the line of sight: nothing about the air
      fight changed.
- [x] Designate ground well past the Mk 42's reach, past 23.7 km, and fire. The shell lands as far out
      along that line as it can get rather than a kilometre or two away. Before, on 2026.9.10.5438: 23.3 km
      was laid to land, and 24.2 km fell back to `driving at 24.2 km`, the line of sight, and came down
      short. Headless, a place 35.5 km out is thrown 23.67 km, where the best elevation reaches 23.68 km
      (`GroundLayTests`). **Confirmed on 2026.9.10.5438** with `gunnery:1,ground,,,35000`: `driving a gun
      laid to its longest reach, 23.7 km, 11.3 km short of it at 35.0 km`, and the shell came down 82.2 s
      later 11,289 m short of the place, at 23.7 km, with no exception in KSA's log.
- [ ] The same by hand, with the mouse as well as a designation. The header strip says `Aim point beyond
      reach` and `the gun reaches` names the number, and the designation mark says it under the range.
- [x] Designate ground just inside the longest reach, 22 km out, and fire. The log says `a gun laid to
      land`, not `beyond reach`, and the shell comes down on the place. **Confirmed on 2026.9.10.5438**
      with `gunnery:1,ground,,,22000` on the range-table drag, second-order shells and the centre carried:
      `driving a gun laid to land 22.0 km out`, 64.9 s in the air, down 8.1 m from the place and 7.6 m
      short, where holding the centre still costs about 29 m at that flight time. The log's `burst moved
      119.7 m to where the round is drawn` was the burst carried to the end of its step with the planet's
      29.8 km/s, which is correct: over nine logged bursts that line is 29.8 km/s times how far into the
      step each one struck, and it now reports only what is left after that carry. The lay used to turn the barrel
      along its miss, which near the longest reach moves a lobbed shell's path mostly along itself: on a
      shell with almost no drag nothing past 55 km of a 64.8 km reach could be laid. Headless now, 97% of
      the longest reach lands inside the lethal radius on the range table's drag (22.97 km, 6.9 m) and on
      almost none (`GroundLayTests`). **Flown twice on 2026.9.10.5438 on the almost-airless drag** with
      `gunnery:1,ground,,,60000`: laid to land, 92.4 s in the air, and down 79.8 m from the place, 19.9 m
      long, both times. The lay held the planet's centre still against the gun while the ground carried
      the gun round it, which headlessly on a world spinning like Earth is g·V·t³/6R: 1 m at 15 km, 7 m at
      30, 25 m at 45 and 79–83 m at 60. With the centre carried, the same 60 km shot lands 3.3 and 5.5 m
      off headlessly (`GroundLayTests`).
- [x] **On Mars, where the air is 0.0135 of the reference.** The gun's longest reach there is
      **174.1 km**, and `gunnery:1,ground,,,200000` — the invocation in CLAUDE.md's own usage
      block — asks for a shot past it: the lay says `driving a gun laid to its longest reach`
      and throws the shell as far as it goes, landing 25.5 km short of a point it never claimed
      to cover. That example cannot pass by construction, which is worth knowing before reading
      its FAIL as a regression.
- [ ] **Inside that reach the residual grows with flight time, and at 150 km it is outside the
      bar.** `gunnery:1,ground,,,150000` lands **25.3 m** from the place, 25.0 m long, after
      **221.3 s** — against an 11 m lethal radius, so it scores FAIL. The longest Earth shot ever
      flown here is 92 s, and `g·V·t³/6R` on Mars at that flight time is about 459 m, so 25 m is
      roughly 5% of the term the carried centre removes. Not diagnosed further; what it is not is
      new — flown paired with the two fixes below reverted, the same shot lands **25.1 m** (24.9 m
      long, same 221.3 s), so the airless-air and ground-test fixes are no-ops on Mars exactly as
      their code paths predict: Mars has an atmosphere and no ocean, and a shell is always well
      inside a mean radius of the surface.

- [ ] Fire the Mk 42 at 47° with nothing to aim at. The shell comes down about 23.7 km out, as the real
      gun's range table has it, rather than 65 km. `DragCoefficient` is fitted to that: headlessly 23.69 km at
      47.25° in 83.6 s, and 16.1 km straight up against the real 14.8, which one constant cannot match.
- [x] Re-fly the passing drones on the range-table drag. Every first-shell number in 7.1d3 was flown on
      the almost-airless drag, and a lead now has longer to be wrong in. **Confirmed on 2026.9.10.5438**
      with `gunnery`: 4 of 4 drones had a burst inside the 11 m lethal radius, the first on each 6.0–6.2 km
      out after about 10 s of flight and all 5 at 0.0 m, from 28 shells, with no exception in KSA's log.
- [ ] The same overhead and far out (`gunnery:3,overhead,...`). A shell to 15.7 km now takes 36–43 s
      rather than 20–23, and the envelope's far edge straight up is near the shell's ceiling.
- [ ] Gun shells step second order (`Slug.SecondOrder`). Headlessly at 60 fps a shell 15.7 km out bursts
      1.3 m from its target against 3.8 m first order (`FlownLeadTests`); a CIWS and a Pantsir burst
      should look and score as before.
- [x] The Phalanx still reaches 1486 m and a burst still kills a crossing drone. Its 20 mm round's drag is
      now its mass, calibre and coefficient, 44 times the constant it had: headlessly it takes 2.1 s to
      get there and arrives at under half its muzzle speed, and it lives 2.5 s rather than 2. **Confirmed
      on 2026.9.10.5438** with `KSARMORY_SCENARIO_SAVE="CIWS" gunnery:3,passing,15,200,500`: 3 of 3
      crossing drones had a burst inside the 2.7 m lethal radius, 7 bursts at 0.0 m from 1,550 shells, and
      no exception in KSA's log.
- [x] The Phalanx settles on a head-on drone and fires. It did not: its 20 mm lead stalled 0.1 to 0.15 m
      short of the solver's tolerance on most close geometries, and seeded from last frame's answer failed
      on the frame after every one it solved, so the gun swung 7 degrees between the lead and the target
      and never settled -- 4 shells and no hit against two drones. **Confirmed 2026-09-19** with
      `KSARMORY_SCENARIO_SAVE="CIWS" gunnery:2,head-on,20,300,1`: 9 bursts at 0.0 m, both drones destroyed,
      and one lay jump per drone, onto the lead as it came into reach. No exception in KSA's log. The
      5"/54 on the same solver, `scenario.sh gunnery`: 4 of 4 drones, 5 bursts at 0.0 m beyond 6 km.
- [x] A burst stops when there is nothing left to put it on -- the target gone, or the gun swung off the
      lay. **Confirmed** in the same run: no round left the gun after either kill, where a burst used to
      run on while the mount turned back to rest.
- [x] The pieces a burst breaks off are not engaged. They were: the CIWS chased the pieces of one drone
      for 9 s and about 480 shells. **Confirmed** in the same run: `nothing detected` the moment each drone
      was destroyed, with 7 and 5 pieces still flying.
- [x] The Pantsir's 30 mm reaches 4 km and still hits: 30 times its old drag, about 8.4 s to get there
      at under 300 m/s, and it lives 9 s rather than 6. **Confirmed on 2026.9.10.5438** with
      `KSARMORY_SCENARIO_SAVE="KABOOM" gunnery:2,passing,12,200,1500`: 2 of 2 crossing drones had a burst
      inside the 4 m lethal radius, 4 bursts at 0.0 m about 2 km out from 96 shells, no missile fired, and
      unspent shells expired at 9.0 s after 4.2 km at 261 m/s. No exception in KSA's log.
- [x] A shell over a body with thin air flies further than over Earth. Drag is measured against Earth's
      sea-level air over every body, where it was each body's own sea level. **Confirmed on
      2026.9.10.5438** from Mars, with `KSARMORY_SCENARIO_SYSTEM=Sol KSARMORY_SCENARIO_SITE=Mars,15,-160
      gunnery:1,ground,,,200000`: 2,097 m up the mount read the air as 0.0135 of the reference, which is
      Mars's 0.02 kg/m³ over 1.225 at that height, where each body's own sea level would have read 0.83.
      The lay reached 90.3 km and the shell came down about 86.4 km out after 114.5 s, where Earth's
      reach is 23.7 km and the old reading predicts 31 km. No exception in KSA's log.
- [ ] Fire a shell high with nothing to aim at, on Earth and from Mars. It flies until it lands and never
      vanishes in mid-air: a path that can only end on the ground runs no clock. With a 30 s life, on
      2026.9.10.5438, shells were removed 23.3 km out while still doing 766 m/s; with a two-minute one the
      clock was the gun's reach from Mars, where the shell above landed at 114.5 s of 120, and a 45° shell
      would have been ended about 40 km up. The 312 km shot below flew 307.7 s and came down on the ground;
      a steep one has not been flown.
- [x] From Mars the gun reaches past its two-minute clock. Headlessly on a Mars-sized world the longest reach
      is 173.3 km in 310 s, and a place at 156 km is laid to land 0.2 m from it (`ReachAlongTheGroundTests`);
      following each shell for its 120 s life, the lay stopped at 90.3 km. **Confirmed on 2026.9.10.5438**
      with the shot below: laid to its longest reach, 174.1 km, down after 307.7 s, no exception in KSA's log.
- [x] Beyond reach on a small body the shell lands on the reach it names. From Mars, laid to 90.3 km of a
      place 199.7 km out along the straight line, it came down 3.9 km short of that: the point was 1,457 m
      under the ground, and a shell laid through it meets the ground first. The reach is searched over the
      ground now. Headlessly a place 312 km out is thrown 173.4 km and lands 0.1 m from the place named,
      where along the straight line the point was 3,525 m under and the shell landed 5,063 m from it
      (`ReachAlongTheGroundTests`). **Confirmed on 2026.9.10.5438** with `KSARMORY_SCENARIO_SYSTEM=Sol
      KSARMORY_SCENARIO_SITE=Mars,15,-160 gunnery:1,ground,,,312000`: `driving a gun laid to its longest
      reach, 174.1 km`, and the shell came down 136,997.5 m from the place, which on a sphere of the mount's
      radius is 174.08 km from the mount. On Earth `gunnery:1,ground,,,35000` still lays to 23.7 km and lands
      11,291.7 m short, against 11,289.0 before, and `scenario.sh drop` still lands 0 m from the ring. The
      first search took 215 ms of one frame from Mars, against 69 ms along the straight line with a
      two-minute horizon, and 76 ms on Earth; what it costs the frames after has not been measured.
- [x] Shift-click a craft parked about 10 km out and fire. The log says `driving a gun laid on it 9.7 km
      out` rather than `driving at 9.7 km`, and the shell arrives at the craft. **Confirmed by hand on
      2026.9.10.5438**: laid on it 9.7 km out from a mountainside, struck on contact 11.8 s after it
      left, one part broken off each of twelve pieces. Before the change the same shot drew
      `driving at 9.7 km` and came down on the ground about 8.8 s out, roughly halfway.
      `gunnery:1,craft,,,10000` on BIG BOOM flies it unattended.
- [ ] The burst line of a shell fired at a designated craft says how far from the aim point it went
      off. It did not on the shot above, and why is not known: the shell is handed the craft as its
      target and its aim point, which is what that line reads.
- [ ] Chase a shell onto a hillside. It stops short well before it lands. On a 9.9 km ground shot
      from the mountainside it stopped 29 ms before and held on the burst from 4 m: the countdown
      is the fall to the ground straight below, and the slope ahead is met first.
- [x] `scenario.sh gunnery:3,ground,,,8000` designates the ground 8 km out and passes with the median
      landing inside the 11 m lethal radius. Flown: 4.0–4.8 m short at 8 km and 3.4 m short at 15 km.
      Laid from the mount rather than the muzzle the 8 km shells landed 25 m long, and before the lay
      allowed for the ground turning they were 15 m long at 8 km and 33 m at 15 km.
      `gunnery:1,overhead,30,300,1500` still bursts 0.0 m from the drone.

**Striking what is close**

- [x] A shell strikes a craft nearer than its fuze's 363 m arming distance rather than flying through
      it. Flown on the "BIG BOOM" save with `gunnery:3,craft`: the tank stack 180 m out was struck by
      the first shell at 182 m after 0.22 s, detonated on contact, and destroyed. Before the fix every
      shell in that save passed through and came down on the ground behind.
- [ ] Shoot at a craft beside the mount by hand. It is struck; the mount itself never is.

**Firing and sound**

- [ ] One press is one shell, with one flash and one gunshot, and auto-engage fires at
      40 rpm. The first flight of this version fired a 20-round burst from one press — a minute of
      firing, with the flash and a machine-gun loop held open the whole time.
- [ ] The flash shows at all. A one-round burst closes inside the step it opens, so the flash is held
      for 0.12 s after each shot rather than for as long as a burst is open. If nothing appears, that
      hold is too short for the emitter to spawn anything.
- [ ] No machine-gun rattle. A gun with a gunshot and no loop of its own gets no loop, rather than the
      Phalanx's.
- [ ] A shell's burst is KSA's `SmallFire`, and sounds like it. The shell carries no burst sound of its
      own any more.
- [ ] If a sound is silent, grep `KSArmory.log` for `does not resolve`.
- [ ] With shells committed and the chase camera riding one, the lock cue's `salvo committed` sits
      under its bracket and the chase's range beside the target, not on top of each other. Both
      were written to the right of the target at one height.
- [ ] The CIWS and the Pantsir sound and flash as they did. Neither names a sound, so both still get
      the shared recording retuned to their rate, and their bursts hold the flash open as before.

**Still true from the first version**

- [ ] The colliders are declared at the modelled pose, so a raised barrel collides where it is not.
- [ ] The mass is the real mount's, **61.4 tonnes**, three times the first version's guess. A 3 m
      stack under it may now sag or break where it held before.
- [ ] It engages at range: `MaxRange` 15.7 km against the CIWS's 1.5, shells 36–43 seconds out at
      the far end, so the track has to survive far longer than any gun here has needed.
- [ ] Twenty shell bodies, not ten: at 40 rpm and 43 s of flight nearly thirty can be in the air, and
      the ones past twenty draw as tracers. The ten added came after the first ten, so a save holding
      the first version still loads.

### 7.1f Releasing a bomb

**Reported not working in flight**, with no detail yet, so nothing here is a diagnosis.

The Mk 82 rack this was first reported against now ships in `KSArmory-example-mod` rather than
here, which changes nothing about the fault: every piece of the release path — `Slug`,
`Ksa/GroundTest.cs`, `Ksa/BombSightOverlay.cs` — is still this mod's, and the **B61 rack** exercises
all of it. Run it against that. Doing it against the example pack as well answers a second question
for free, which is whether a registered weapon behaves like a compiled-in one.

Two things shipped together and either could be it: `feat(rounds): drop a bomb` and
`feat(rounds): show where a bomb would land`. They fail in different places — one is a round that
never leaves or never arrives, the other is a ring in the wrong place over a round that is fine.

What to record next time, in this order, because each answers a different half:

- [ ] Does the rack **release** at all? The trigger fires without a lock and auto-engage refuses
      it outright and says why, so the panel's *Holding fire* line is the first thing to read.
- [ ] Does the bomb **fall away from the aircraft**, nose-down, rather than sideways or through it?
- [ ] Does it **burst on the ground** rather than passing through? `HitsTerrain` is set for this
      round and the Mk 21 reentry vehicle, so it is nearly the only thing exercising
      `Ksa/GroundTest.cs`.
- [ ] Does the **ring** sit where it lands? A ring in the wrong place with a bomb that arrives
      correctly is the sight; a bomb that goes nowhere near the ring is the round.
- [x] The ring allows for the planet's turn. `BombSight` flies the fall against where the ground has
      carried the release round the centre, reads the terrain where the round is in that turning frame,
      and puts the ring on the ground that will be under the landing: headlessly a 5 km drop at 250 m/s
      over the equator and 150 m ridges lands 0.6–0.7 m from the ring, where holding the planet still put
      it 36.5 m off heading east and 16.4 m heading north, and reading the terrain at the carried point
      26.7 and 190.6 m (`BombSightSpinTests`). **Confirmed on 2026.9.10.5438** with
      `./tools/scenario.sh drop`: released at 1001 m, down 36.3 s later 0 m from the ring (E 0, N 0) and
      0 m from a flight off the release state, with no exception in KSA's log. Reading the terrain at the
      carried point, the same scenario had landed 16 m off, almost all of it height. That release is
      near vertical over KSC and a kilometre up, so the turn it exercises is small: a long level drop at
      the equator has not been flown.
- [ ] Designate a point, fly level, and release with the ring on it. The bomb lands within tens of
      metres of it, with the `detonated on the ground, N m from the aim point` line to say how far.
      Hundreds of metres off is the tail kit; `./tools/scenario.sh drop` flies the same release
      unattended and says which of the sight, the release and the fall the miss belongs to.
- [ ] The log line for the release, and the whole `KSArmory.log`.
- [x] The **pipper goes out when the store is released** — the arc from the craft and the ring
      under it, both. It answers "where would one released now land", and once the rack is empty
      there is none: `TryNextReleaseEcl` refused to fall back to tube 0, which had left the sight
      drawn for the rest of the flight. **Confirmed in game on 2026.9.10.5438.**

### 7.1f2 Designating after the store has gone

**The main path is confirmed in game on 2026.9.10.5438** — a store retargeted mid-fall changes
course and arrives, and both rings and the line under the trigger read correctly. What is still
unflown is everything below the first four boxes: the refusals, the horizon and what it costs.

- [x] With a store falling, shift-click the ground. `KSArmory.log` reads
      `round 1 now steering at <place> - inside the kit's reach (... m to walk, ... m of authority
      over ... s of fall)`, and the store visibly changes course.
- [x] It arrives at the new place rather than the old one, with the
      `detonated on the ground, N m from the aim point` line to say how far.
- [ ] Shift-click **again**, somewhere else, while it is still falling. It goes to the second place:
      nothing latches the first.
- [x] The two rings are drawn on the ground — the landing, and the blue-white circle around it that
      is how far it can still be walked. The circle **shrinks** as it falls, fast.
- [x] Click well outside the circle: `./tools/scenario.sh drop:6000,30,guided,,,20@9000`. **Flown
      on 2026.9.10.5438** — warned before anything happened (`7.00 km beyond the kit's reach - it
      will steer at it and fall short`), steered anyway, and landed short rather than ignoring the
      click.

      Scored on whether the ring told the truth rather than on arriving, because a target the kit
      cannot reach can never pass a 30 m bar and a run that can only fail teaches everyone to skip
      this file. `PASS the reach held as a floor: walked 2684 m of the 1985 m the ring claimed
      (1.35x)`. **That is the only in-game evidence for `TailKitReach.SettlingMargin`**, which is
      otherwise measured against a smooth sphere with no terrain — and it is the direction that
      matters: a ring promising a walk the kit cannot fly is the one way the instrument is worse
      than none. Reproduces at 2,705 m on earlier code, so the margin is not noise.
- [x] The line under the trigger reads `Store in the air: N s to go, can still be walked ...`, and
      agrees with the ring. The two are one answer, so a disagreement is a bug rather than a rounding.
- [ ] Clearing the designation does **not** turn the falling store back into an unguided one.
- [ ] A release above about 15 km draws no circle at all and says nothing false — the probes are
      bounded by the pipper's own `BombSight.MaxSteps` horizon.
- [x] **Warp, flown across the range on 2026.9.10.5438.** Identical releases every time — 6,001 m,
      275 m/s, 48 deg above the horizon, ejected 4.0 m/s at 50 deg to the rack — so what differs is
      the warp and nothing else.

      | run | before the fixes | after |
      | --- | --- | --- |
      | `,,auto` | taken out of the world 1.9 s after release, at 8x | **0 m from the ring** |
      | `,,100` | — | **0 m** |
      | `,,400` | 2,776 m off (8 s allowance) | **0 m** (30 s) |
      | `,,800` | still falling at the 180 s budget | **0 m** |

      Three separate faults, and the range is what separated them: KSA refusing a speed change
      during its own warp-to-a-time (deletion), the policy calibrating a frame *rate* off the 3.2 s
      hitch a speed change costs (world stuck at 1x), and the integration clamp discarding whatever
      part of a frame the store could not take (the misses). Only the first was the reported bug.

- [x] **What it costs a frame**, read off `FrameBudget` under `KSARMORY_SCENARIO_VERBOSE=1` while a
      store fell. The rings were **1.96 ms mean** of a 16.7 ms budget — and the flights were not the
      problem: `store reach` (the draw) was 1.52 of it, re-draping 96 terrain lookups every frame
      for a ring that does not move, against `reach solve` at 0.28. Draping once per solve and
      spreading the three flights over three ticks: **0.33 ms mean, worst lump 7.7 → 2-4.5 ms.**
      About 2% of a frame.

- [ ] **By hand, raising warp through KSA's own control** rather than by a scripted jump. The
      scenario sets a speed in one call, which is a harsher input than stepping through warp levels,
      and the hitch it produces is what `MaxPlausibleFrameSeconds` exists for. Stepping up may never
      produce one — untested either way.
- [ ] And a store's **body** is still drawn if `AbandonFlight` ever does fire, not just its tracer.
      That call hides every body on the launcher when nothing survives, which over a survivor would
      hide the survivor. Unreachable from the scenarios now that nothing abandons.
- [x] The burst effects sit where the store landed: **1.5 m at 400x, 31.6 m at 800x**, scaling with
      how far into the frame the burst falls and bounded by the in-air clamp — so tens of metres,
      inside a 490 m lethal radius. Measured by the `burst drawn ... from the ground it reached`
      line, added because a cloud kilometres from the mark looked like an effects fault and was the
      landing being wrong.

### 7.1f3 How long a nuclear burst is watched

**Flown on 2026.9.10.5438.** The chase held a flat 3 s on every burst, which over a cloud that
rises for 38 s and stands for 40 showed about a twenty-fifth of it and took the camera away
mid-event. It is now sized by the charge, the same way `ChaseView.StopShortMetres` already sizes
the stand-off.

- [x] `./tools/scenario.sh drop:6000,30,guided` logs `holding on the burst for 38 s` and then
      `released the main view`. The hand-back had never appeared in a run before: the harness waited
      6 s after the burst and closed the game a sixth of the way into the hold, so the part most
      worth checking never happened. That wait is sized off the hold now.
- [x] `./tools/scenario.sh drop:6000,30,guided,20` — the launching craft destroyed 20 s into the
      fall. The store still landed **0 m from the ring**, the hold ran its full 38 s with no craft in
      existence, and the view came back. The burst is anchored to the ground rather than to the
      launcher (`RoundFollowable.HoldAgainst`), which is what makes that work.
- [ ] **Right-drag and the wheel during the hold.** Wired and unexercised: an unattended run has no
      mouse, so only a person can say whether looking around a mushroom cloud feels right. Without
      it a 38 s hold is a frozen stare, which is why the two changes ship together.
- [ ] A conventional burst still holds 3 s — a cannon putting one round into a drone must not take
      the view away for the next one.

### 7.1f4 The cloud drawn by this mod's own shader, and the burst where there is no air

**Flown on 2026.9.10.5438.** The column is no longer walked with smoke pens: `Ksa/CloudPass.cs`
dispatches a compute shader inside KSA's own frame, prefixed onto `SunbloomRenderer.Render`. That
is the fifth place this mod patches the game and the first in the renderer. On a body with no air
there is no column at all, and `Ksa/BurstEjecta.cs` throws ground instead.

- [x] A B61 burst on Earth grows a raymarched column, lit by KSA's own atmosphere — no uniform
      buffer and no vendored shader code, because the global descriptor set already carried the
      LUTs and `global.lighting`.
- [x] It costs less than the pens it replaced: about **3 ms** against 6–8, read off KSA's own GPU
      profiler through `Ksa/CloudPassCost.cs`, from the pinned pose `Ksa/CloudWatch.cs` sets.
      A `noshader` control run is what says the number is the pass and not the scene.
- [x] **Luna.** `KSARMORY_SCENARIO_SITE=Luna,0,0 KSARMORY_SCENARIO_SYSTEM=Sol` with
      `KSARMORY_SCENARIO_CLOUDS=1`: the store lands in 5.2 s, the dust dome draws, and four
      captures land at 0.10/0.30/0.60/0.95 of a **16.4 s** watch that no longer drifts.
- [x] **And at a yield that is not the B61's.** `KSARMORY_SCENARIO_TWOCLOUDS=100` sets the second
      burst off at a hundred times the store's charge, so one run carries both. Flown on Luna: a
      0.3 kt dome 218 m across beside a 30 kt one at 1378 m, which is the 6.3x that `W^0.4` asks
      for over a hundredfold yield. The drawing had only ever been looked at at 0.3 kt; the
      arithmetic was already tested from 0.3 to 1000.
- [x] Three bugs that only an airless body could show, each flown before and after — a body with no
      atmosphere reading as Earth sea-level air, rounds on the Moon ground-tested against Earth,
      and the two halves of one burst disagreeing about whether it threw anything. The Earth drop
      still lands 0 m from the ring, which is what says none of it reached the scored path.

Not exercised, and the first two are the ones to believe least:

- [x] **The base surge and the fade, which were modelled and undrawn.** `MushroomCloud` computes
      `SurgeRadius`, `SurgeHeight` and `Fade` every frame, and the fade folds into the strength
      float already being sent: at 72 s the sky shows through a dissolving cap rather than a solid
      silhouette. **The surge is drawn by the raymarch now**, as the stem's foot flaring to three
      and a half times its width at the ground. It was a particle collar for a while and that was
      taken out: KSA draws particles after its weather clouds and never tests them against them,
      so the collar sat on top of a cloud deck the column had correctly gone behind — and at
      300 kt its kilometre-wide volumes came out black. `SurgeRadius` and `SurgeHeight` are still
      computed and tested, and drive nothing drawn; they are the obvious thing to drive the foot
      with, since the push constant already carries the age and the cap radius they are fractions
      of.

- [x] **`StemTop` — looked at and deliberately not done.** The shader stops the stem at the cap's
      CENTRE rather than at `StemTop`, which is `min(climb, StemCeiling(capCentre, capRadius))`.
      Both terms it needs are in the push constant, so it is derivable without the uniform buffer
      that was supposed to gate it — but `StemCeiling` is `capCentre - capRadius * Oblate * 0.7`
      with `Oblate` private, so drawing it means copying two geometry constants into GLSL. That is
      the duplication CLAUDE.md warns drifts, and what it buys is the stem ending at the cap's
      underside instead of its centre: both inside the cap, and the taper already widens into the
      underside there. Not worth a constant in two places.

- [x] **More than one cloud, and coincident bursts that are one.** The pass draws every standing
      cloud, one dispatch each, far to near so the nearest composites last, with a compute-write to
      compute-read barrier between them — `KSA.Rendering.BarrierBatch`, which is public, so this
      needs no uniform buffer and no reflection. An earlier reading that no barrier existed came
      from grepping `CommandBuffer` for one; it lives on its own type.

      And a burst landing inside a standing cloud's own fireball is **added to that cloud** rather
      than starting another. A bus's six warheads land about 9 mm apart: six dispatches would march
      the same pixels six times for six coincident clouds at six times the density, where six 20 kt
      bursts at one point is one 120 kt burst.

      Flown on 2026.9.10.5438 with `KSARMORY_SCENARIO_TWOCLOUDS=1`: two bursts 1 km apart both draw
      and composite, 5.3 ms for one against 8.6 for two; at 34.4 m the second merges — `inside its
      72 m fireball, so it is that one -- now 0.60 kt`.

- [x] **An atmosphere that is not Earth's.** Flown on Mars, where the air is 0.011–0.0135 of the
      reference: the column draws with the right shape, and the sky, horizon and terrain around it
      are lit by Mars's own LUTs rather than by Earth's. The thin atmosphere is no longer the
      untried case.
- [x] **The night side is dark, and the sun's elevation is logged beside every capture.** The sun
      term is the cloud's own optical depth toward the sun, which is self-shadowing: nothing asked
      whether the planet was in the way, so a column over pitch-black Martian ground stood there
      brightly and directionally lit. Flown at both ends on 2026.9.10.5438 — sun 27.4 deg up, the
      daylight cloud unchanged; sun 48.3 deg below, dark.

      **Done geometrically rather than through the transmittance LUT, and that is the finding.**
      `GetAerialPerspectiveForObjectInAtmosphere` hands back a `sunToObjectTransmittance` that is
      this and a sunset reddening too, and Core's own `Clouds/RaymarchCloud.comp` lights its clouds
      with exactly it. Fed the radii reachable from the global lighting block it comes back near
      zero **in broad daylight with the sun measured at 27 degrees up**, which took the cloud to a
      black silhouette at every gain tried. Core passes `topRadius`, `bottomRadius` and
      `planetRadius` out of its atmosphere UBO, which this shader does not bind; something in that
      parameterisation disagrees and was not worth being wrong about. The geometric gate needs no
      radii, no UBO and no LUT, and is identically 1 wherever the sun is up, so it cannot cost the
      daylight case anything.

- [x] **The sun's own colour and whether it has set, from the engine.** `global.lighting` carries
      an `occlusionColor`, documented in Core's `Global.glsl` as "the transmittance of the sun color
      through the atmosphere", and every one of Core's mesh shaders multiplies its sun by exactly
      it. So the terminator and the sunset reddening are one term and need no radii, no atmosphere
      UBO and no LUT call — which is what two earlier attempts went looking for.
      `GetAerialPerspectiveForObjectInAtmosphere`'s `sunToObjectTransmittance` is the same quantity
      per-object and better still, and remains unusable here: fed the radii reachable from the
      global block it returns near zero in broad daylight with the sun 27 degrees up. Flown at both
      ends on 2026.9.10.5438 — daylight unchanged, Mars at 48 deg below the horizon dark.

- [x] **What a burst does besides standing there: the flash, the wave, the bang.** All flown on
      2026.9.10.5438 through `KSARMORY_SCENARIO_CLOUDS=1`.

      The **whiteout** is written by the compute shader, not by an ImGui overlay, and that is the
      finding rather than a preference: KSA wraps its entire UI pass in `if (DrawUI)`, and both F2
      and `ScreenshotCapture` clear it — so painted there it vanished from every screenshot the
      harness took, and would have vanished for any player with the HUD hidden. It is driven by the
      fireball's **rise** rather than its brightness, with recovery always running: taken off the
      brightness it held for the ball's whole 1.9 s burn, which is a stuck screen rather than an
      eye adjusting. `Ksa/BurstFlash.cs` is the model; nothing in it draws.

      **It is white only where it clips.** A veil of one colour is what every pixel overexposed
      looks like; a real glare is dimmer off the source, stops clipping there, and shows the light
      itself — a fireball of a few thousand kelvin yellowed by kilometres of air. So the shader
      falls it off with the angle from the burst and warms it both there and as the flash dies. The
      levels are measured rather than assumed: bands written through KSA's whole post chain came out
      0.25 → 130, 1.0 → 240 and everything from 1.25 → 255, where the shader had claimed one was a
      mid grey. At that claim's level of 9 the glare was seven times the clip and every corner read
      255; flown at the measured one, 255 over the burst and (232, 212, 176) in the corners.

      **And a halo round the ball that lasts as long as it burns.** The whiteout is an eye
      overwhelmed, so it follows the rise and recovers in about a second — while the ball is still
      orange in frame. Light scattering round a bright source lasts exactly as long as the source is
      bright, so `BurstFlash.Glare` is its apparent brightness held at level, in its own colour, and
      the shader lays it on the core only. Flown at 0.3 kt: a warm glow round the cap at 3.8 s,
      glow 32, and none at 11.4 s, when the ball has gone out.

      **There is no blast wave, and the burst carries no `<ExplosionVolume>` at all.** KSA's trail
      renderer, which draws explosion volumes, raymarches into the same low-resolution images the
      weather clouds do, and those are what this pass reads as weather — so the burst's own fireball
      and shock volumes occluded the cloud they sat in, in low-resolution blocks at the fireball's
      edge. The shell's patchy tail was also the white streaks across the sky at 3.8 s. Flown with
      both taken out: the blocks and the streaks gone, at 1.3 s and 3.8 s.

      The **bang arrives late**, at 343 m/s from the burst, anchored to the ground it happened over
      rather than to the launching craft or to a bare ecliptic point. Silent where there is no air,
      which is the one case a sound of this kind must get right.

- [x] **Three things that separate a drawn cloud from a photographed one**, flown the same day.

      **Shear.** The wind above the cloud used to be one vector, so the column leaned in a single
      flat plane the whole way up. Real wind veers as well as strengthens — the Ekman spiral near
      the ground, the thermal wind above it, twenty to sixty degrees over a column this tall — so
      the lean turns with height and the cloud corkscrews. Free: the axis is the local vertical and
      the downwind is already perpendicular to it.

      **Fallout streamers**, hung off the cap's own field rather than off a plane under it. Hung
      off a plane they *detach*: erosion cuts the cap's real underside well inside the torus's
      analytic one, so the strands begin in clear air with a visible gap over them, and the parts
      of the rim the noise has eaten go on shedding from nothing. Reported from looking at it, and
      fixed by measuring the drop from the cap's own middle so the two overlap. 1.49 to 2.06 ms.

      **The ground burns**, out to `Warhead.LethalRadius` — the radius the panel, the overlay and
      the blast sweep already describe the weapon by, so the stain is not a fourth number. Screen
      space off the depth the march already clips against, so it follows the terrain with nothing
      placed in the world; desaturated before it is darkened, because a multiply leaves grass green
      and merely dimmer, which reads as shadow. 2.06 to 2.08 ms: almost every pixel leaves on one
      comparison.

      **It lasts exactly as long as the pass is dispatched, which is as long as the cloud stands —
      about 78 s.** A mark that outlives the cloud is a decal on the hillside, which is
      `docs/DAMAGE-DECALS.md` and is not built.

      **Both closed the same day, and without a decal.** A mark is no longer the cloud's business:
      `NuclearClouds` keeps a third body-fixed list that outlives both the column and the fireball,
      and `CloudPass` sends it ahead of the clouds so a column composites over ground already
      burned. The shader takes either half alone, so an airless burst — which grows no column at
      all — burns ground for the first time.

      It costs a full-screen dispatch per mark for the rest of the session, which is why the list
      is bounded at **four** where the clouds are at eight: a cloud expires and that list drains
      itself, and this one never does. The oldest is dropped. Marks merge on the rule the clouds
      merge on, so a bus's six warheads make one stain rather than six mixes of the same ash onto
      the same pixels.

      Flown three ways. On Earth the cloud count goes 1 to 0 at 78 s with the mark still at 1, and
      the capture past the column's whole life shows burned ground under an empty sky. With
      `TWOCLOUDS=1` two marks stand a kilometre apart, each with its own edge and neither
      compounding into the other — `TWOCLOUDS=100` does **not** test that, because a 30 kt burst's
      lethal radius is 2.3 km and swallows the 0.3 kt one 1 km away, which is the merge working.
      On **Luna** the pass goes from 0.04 to 0.13 ms and a mark stands on ground no column ever
      covered.

      **In vacuum the mark is how far the radiation reaches, not a blast law.**
      `Warhead.LethalRadius` is overpressure from a shock a vacuum does not carry, and borrowed there
      it drew a 220 m burst under a 493 m mark with `CloudWatch` standing inside it.
      `AirlessBurst.ScorchRadius` is the inverse square of an unabsorbed pulse instead — the square
      root of the yield rather than the cube root — at a fluence that is chosen, not measured,
      because nobody has burned regolith this way. And no plume: nothing lifted anything and no wind
      would carry it. Flown on Luna: 264 m, a round patch just past the 218 m of thrown ground.

      **The bound has never been reached.** `MaxScorches` is twelve and the oldest is dropped; the
      harness produces at most two, and nothing has ever watched a mark vanish. That path is
      reasoned only.

- [x] **All three re-flown at a yield and on a body neither was written against**, same day.

      `KSARMORY_SCENARIO_TWOCLOUDS=100` puts a 0.3 kt burst beside a 30 kt one in one frame, which
      is the check the shear and the streamers most needed: `VeerRadians` is a fixed angle and
      `FalloutReach` a fraction of the radius, so neither had been seen at a cloud of another size.
      Both read correctly at both. Two clouds cost **2.50 ms** against 2.08 for one, so the barrier
      path is sub-linear — and the two scorch marks **merge into one patch of ash rather than
      compounding toward black**, which the desaturate-then-darken form was reasoned to do and had
      never been looked at.

      On Luna the pass falls to **0.04 ms** over 296 frames: the flash-only dispatch returns at the
      cloud guard before reaching the scorch block, the ejecta still arc and land, and nothing the
      airless path does was disturbed by a block added ahead of it in `main()`.

- [x] **Warp and pause.** Flown on 2026.9.10.5438, and what took so long was two instruments
      rather than any doubt about the behaviour.

      The capture ages were **wall clock** and the burst is on simulated time, so a warped run
      photographed a different cloud from the one it was being compared with; they are the burst's
      own age now, which is the same number at 1x and the right one everywhere else. And the linger
      ended on wall clock, so a warped run sat out eighty seconds after photographing everything.
      `KSARMORY_SCENARIO_CLOUDWARP=<n>` then holds a speed through the linger, which the drop's own
      warp argument cannot: that one is handed back the instant the store lands, deliberately,
      because the hand-back should not be watched at speed.

      **Zero needed a call of its own.** `KsaWorld.SetSimulationSpeed` refuses anything at or below
      zero and clamps to `SlowestSimSpeed`, so asking for a pause as a speed left the world at 1x
      while the run reported it had asked for one — flown, and it is why `SetPaused` exists. A
      caller wanting a slow world and one wanting a stopped world want different things.

      At **20x** the captures land at the same seven cloud ages as the 1x run, sun elevations
      agreeing to a tenth of a degree, in 4.2 s of wall clock against 84. **Paused**, the burst
      holds at 0.0 s across 86 s of wall clock with the cloud and the mark both still standing and
      the pass still drawing.

- [x] **KSA's weather clouds hide the burst behind them.** They write no depth — they are
      raymarched into images of their own and composited into the scene colour — so the depth under
      a cloud says "ground", and everything this pass drew off it was drawn in front of every cloud:
      the column over a deck it was behind, and at 300 kt the ground mark as a flat grey disc across
      the tops of the clouds. `CloudPass` now binds the cloud renderer's own low-resolution colour
      and distance, reached through one verified reflected field, and treats each pixel's cloud as a
      thin sheet: what lies behind it is seen through its transmittance, and a mark is dimmed by it.
      With clouds switched off there is no renderer, stand-ins are bound, and a flag says so.

      **The images are not only weather.** Explosion volumes and exhaust trails are raymarched into
      them too, which is right for a trail in front of the cloud and wrong for anything the burst
      itself adds — see the blast-wave note above.

      Flown at 300 kt from above a deck: the grey disc gone from the cloud tops, and the stem running
      down behind the deck while the cap stands above it. **And from under one**, at
      `KSARMORY_SCENARIO_WATCHELEV=2`: the cap shows only through the deck's holes, and the stem
      stands below its base.

      **Read off the renderer's accumulated result, not its low-resolution pass.** The low-resolution
      images are sampled at a different sub-pixel every frame, so an edge cut from them crawls with
      the world paused: three stills at `KSARMORY_SCENARIO_STILLAT=25` differed along the whole
      outline of a hole the cap showed through, by up to 122 of 255. The accumulated full-resolution
      pair — four private fields and a flag, since it ping-pongs — has the same layout and the same
      distance encoding, and the outline is gone: what differs between stills is scattered specks at
      the level of KSA's own clouds elsewhere in the sky. One pipeline per image of the pair, so the
      swap is not a rebuild a frame.

- [ ] **The old note, kept because it is what the shipped scenarios still cannot do.** Reasoned, not flown. `NuclearClouds.Update` is driven from `StepOnce` on
      the simulated step, so a pause hands it nothing and warp hands it a large one — and the shape
      and the flash are both pure functions of age, so a cloud that skips its life in one step just
      expires. Nothing in the pass reads a clock at all.

      **The shipped scenarios cannot exercise it**, which is why it stays open: `speeds=` only runs
      in the engagement phase, and the drop's own warp argument is given back by `WarpPolicy` when
      the store lands, before the linger starts — flown at `drop:10,0,dumb,,50` and the captures
      came back at 1x ages. Driving a speed through the linger would contradict a decision already
      in `CaptureBurst`: the capture ages are wall clock, so a run warping through them photographs
      the wrong ones.
- [x] **An instrument that cried wolf on every control run.** `CloudPassCost` printed
      `pass FAILED TO BUILD` whenever there was no pipeline — and a pass switched off by `noshader`
      is never *asked* to build, so the deliberate control reported a shader that would not
      compile. That is the confusion the line was added to remove, back again with the sign
      flipped, and worse: a control is the run that is supposed to report nothing. `BuildFailed` is
      now set only by a build that ran and failed. Flown: the control reads `pass off`.

- [x] **The anvil.** Past the drawn tropopause — the standard 11 km at `DrawnScale`, reached at
      ~49 kt — the cap is braked to `MushroomCloud.StratospherePenetration` of the rise the law asks
      for, fitted to Castle Bravo, and the height it does not make goes into width at constant
      volume. Flown at 300 kt: 16.3 km across under a 10.2 km top, where the law drew a column. The
      fallout curtain is measured in the cap's own thickness, so the anvil sheds a fringe rather
      than the wall a curtain sized off the whole cloud hung from its rim.

- [ ] **Another GPU.** One machine, one vendor. A compute dispatch into someone else's frame is
      exactly the kind of thing that is driver-specific, and a failed patch or a shader that will
      not compile draws no cloud rather than crashing — which is the intended degradation and has
      never been provoked.
- [x] **How strong the aerial perspective should be — and it is not a judgement after all.**
      Flown as an A/B at the pinned pose, same age, haze on against haze off: with it on the cloud
      is cooler and lower in contrast and the stem's foot is visibly bluer than the dirt it is made
      of. That reads as over-applied, and it is not.

      Settled by reading the engine's own volumetric consumer rather than by eye.
      `Clouds/RaymarchCloud.comp:525` is
      `cloudColor.rgb * eyeToCloudTransmittance + inscatter * atmosphereInscatterWeight`, which is
      this pass's expression term for term — and its weight, `1.0 - cloudColor.a`, is the same
      quantity as ours despite reading like the opposite. **Core's `a` is transmittance, not
      opacity**: `:561` composites two layers by *multiplying* alphas and `:438` raymarches only
      while `a < 1.0`. So `1 - a` is coverage, which is what we weight by.

      The amount is therefore the engine's own answer applied the way the engine applies it, and a
      scale factor here would be a fudge fitted at the one distance anybody has looked from. What
      the A/B actually shows is what 2.4 km of the densest air there is does to a near-white
      object, which is more than it does to the dark terrain beside it.
- [ ] **A craft set down on Luna sinks and its tip explodes**, seen in play. `TryPlaceOnSurface` is a
      thin wrapper over KSA's own `Vehicle.TeleportToLocation` and the craft reads 7 m **above** the
      ground when placed, so this is the engine's vehicle-vs-terrain handling or the blast taking a
      rocket standing 7 m from a nuclear detonation. Not diagnosed; the drop passes either way.

### 7.1e Drag, and what a round does once it leaves the air

Never deliberately tested. A flight log reads:

```
round 2 expired after 30.0s - closest 617 m, flew 33.0 km, final speed 1010 m/s, lock=False
```

A 57E6 peaks near 1290 m/s, so that round shed under 300 m/s in thirty seconds and travelled 65 %
past the system's 20 km engagement envelope. Both look wrong and neither is:
`KsaWorld.MediumDensityRatioAt` returns **zero above `air.Height`**, which is correct because there
is no atmosphere to resist anything, and the envelope decides when the battery *commits* rather
than how far a round may fly. `MaxFlightSeconds` is what ends it.

So this is here to be confirmed, not to be fixed:

- [ ] A low, flat shot inside the atmosphere **does** bleed speed. If a sea-level round also
      arrives at 1010 m/s, the density lookup is not finding the body and every round is coasting.
- [ ] A steep or high shot coasts, and the flight log's final speed is close to its peak.
- [ ] Somewhere in between, the final speed lands in between. Two points cannot distinguish
      "drag works" from "drag is all-or-nothing".
- [ ] `lock=False` on a long shot is the command uplink breaking when the target leaves the
      sensor volume, which is the documented behaviour — not the round losing its own seeker.
      Check the panel says the launcher lost the track at about the same moment.

**Why it matters:** a round that quietly loses all its drag flies several times too far and still
looks plausible in the log. `MediumDensityRatioAt` falls back to **1.0** rather than 0.0 when the
atmosphere cannot be read, precisely so that failure is a round that stops short rather than one
that sails on — but nothing has ever exercised the branch.

### 7.2 More than one target at a time

- [ ] Spawn three drones close together. Radar lists all three, ranked by time-to-CPA.
- [ ] It commits `RoundsPerTarget` to the top threat, then moves to the next — not all twelve at one.
- [ ] Killing the lock target promotes the next one without a stall.

**Why:** every engagement so far has been one drone. Track prioritisation, round attribution and
salvo allocation have literally never run against a contested list.

**The arithmetic is covered.** `ThreatModelTests` ranks contested lists, checks non-threats
never outrank threats, and walks twelve tubes across three targets to prove the first does not
take the whole magazine. It is headless because the logic lives in `Sim/ThreatModel.cs` rather
than in `Ksa/Radar.cs` and `Ksa/WeaponSystem.cs`.

It does **not** retire these boxes. The tests prove the maths; they say nothing about KSA
handing over the vehicles expected, tracks surviving a rebuild, or the lock promoting cleanly
when a target dies mid-engagement. Run them still — expect fewer surprises.

### 7.3 Blast catching a third party

- [ ] Put two drones close together, kill one, confirm the other reports `near miss` or dies too.

**Why:** the splash path (as opposed to the intended-target path) has never destroyed anything.

### 7.4 Unproven

Closed: the pod frame rings and spine, the search array's turntable, the gun sponsons tied back to
their cheeks, the tube covers and the raised optical head all render correctly. `checkswept.py`
guards that class of defect, so a regression is caught before a build rather than by eye.

Still open below.

- [ ] **Pods clear the bodywork off the bow** - traverse into the forward sector at low elevation
      and confirm the tubes lift rather than passing through the APU box. The depression floor
      holds its full height across the sector it protects; only flight proves the arc is wide
      enough.
- [ ] **A refused drive holds fire** - cannot be forced on demand, since it needs KSA to reject a
      transform write. If it ever happens the panel says which assembly froze and whether the
      launcher is holding fire; report the line rather than trying to reproduce it.
- [ ] **Teams and IFF** - declare two teams under **KSArmory settings → Teams**, put the launcher on
      one with its flag, and confirm the track list marks F / N / H / ? correctly and that a
      friendly is not engaged. The flag is what places a craft; only something with nothing of this
      mod's fitted falls back to its name, so for those name teams such that no team name is a
      substring of another craft's name.
- [ ] **Two mounts on one team do not shoot each other's rounds.** A Phalanx and a Pantsir, both
      flags on the same team, both auto-engaging, one firing. Neither may engage the other's shells,
      and neither track list may show them as `?`. **This is the case that only the rounds reach**:
      two craft standing on the same ground are under `MinTargetSpeed` and never enter each other's
      track lists at all, so nothing before the first shot exercises the classification. Check the
      Radar tab draws them **X** rather than a triangle.
- [ ] **Removing a team** under **KSArmory settings → Teams** moves every craft on it to **No team**
      in the switcher, and each one's **Teams and IFF** tab reads `Own team: none`. Declaring the
      team again does not bring back the allied or neutral ticks it had.
- [ ] **Warp overrun** - warp hard enough to exceed 0.32 s of simulated time per frame and confirm
      the log warns how much time was discarded. That warning is a diagnostic, not a fix.
- [ ] **Stock drones** — Gemini7 / Hunter / Banjo / Polaris / Rocket each spawn and fly.
- [ ] **Battery stays put** — fly a second craft; the battery remains on the launcher and the
      panel says so.
- [ ] **Two launchers on one craft** — two weapons, each with its own magazine, and the selector
      switching between them without either refilling.
- [ ] **Reload cycle** — let it run dry and auto-reload rather than reloading by hand.
- [ ] **Manual designation** — designate a non-priority track and confirm it is engaged.

### 7.5 Known-missing behaviour, worth confirming is *survivable*

- [ ] A round fired at a low target passes through terrain rather than hitting it. Expected —
      rounds only test against their target — but confirm it does not throw.
- [x] **Timewarp during flight.** Fire control steps on `Universe.GetElapsedTime()` via
      `Sim/SimClock.cs`, never on StarMap's *player-time* delta: player time is wall-clock, so it
      runs through a pause and stays at 1× under warp, which breaks tracking and lets a paused
      system fire.

### 7.5b Timewarp and pause, on simulated time

- [x] Pause mid-engagement: rounds hold position, no launches, no dwell accrued. Resume and the
      engagement continues rather than jumping.
- [x] 2×–10× warp with rounds in flight: they still guide and still intercept.
- [x] Above ~20× (0.32 s of sim time in one frame): the panel says *rounds stand down*, the
      rounds vanish, and nothing throws. Dropping them is intended, not a failure.
- [x] Warp up and back down repeatedly: the battery recovers each time and re-acquires.
- [x] Load a save while rounds are in flight: they are abandoned, not flown into the new world.

### 7.6b The EO director — the sight as a part of its own

The head is no longer launcher gear: it is a part anything can carry, it finds its own targets
through its own sensor, and it drives the view with no weapon involved. A Pantsir that has not been
given one has no sight at all, which is the intended state and not a fault.

Everything in 7.6 below was flown against a head bolted to the Pantsir's turret. That head is
still there — `PantsirDirector` is a second `OpticProfile` on the launcher's own part Id, riding
the traverse — but what carries it is now a mount frame read off the engine rather than the
launcher's angle, so the items are worth re-running rather than trusted.

**Flown and working.** The faults found along the way, all fixed and confirmed: the panel not
listing a camera-only craft and dropping the selection when it was managed; the horizon drawn
against the mount normal rather than local vertical; the head sweeping through its own mast
between two legal bearings; and the picture flipping, which took three attempts because the first
two moved a threshold rather than removing it — the roll is now corrected towards vertical at a
limited rate instead of being chosen, and cannot move more than a few degrees in a frame.

Levelling is off by default: the roll is rigid with the head, so the picture rolls with the craft
and looking sideways stays sideways. **Level the horizon** is the opt-in.

- [ ] `EO Director` appears under **Sensors** in the editor and surface-attaches.
- [ ] A craft carrying **only** a director and a command source tracks a target and shows the
      bracket. That is the whole point of the split and nothing before could do it.
- [ ] Its **Camera** row under *Components* says **Director view**, and *off / main view*
      drives the picture.
- [ ] **Track with the director** off parks the head; **Aim by hand** drives it from the sliders.
- [ ] The elevation slider stops at **−20°**. Below that the window would pass through its own
      mast, which is a fact about the model rather than a preference.
- [ ] On an unarmed craft: bracket, horizontal reference, edge cue and zoom, and **no** arm state,
      ammo or gun pipper. On a craft with a Pantsir *and* a director, all of it.
- [ ] A launcher carrying **no director** — a rail, or the CIWS — reports no sight rather than a
      broken one. The Pantsir cannot show this: its turret roof carries one.
- [ ] Two directors on one craft are two heads, each pointed independently.
- [x] **Mouse aim.** The ring holds the head inside it and follows outside it, the speed builds
      from the ring's edge rather than from the middle of the view, and resting the cursor leaves
      the head where it is rather than parking it.
- [ ] Known and not yet built: a head's settings are **not persisted**, so magnification and
      viewport reset on reload.

### 7.6c The LITENING pod — the same sight on a roll-nod gimbal

**Never flown.** Nothing below has been seen in game. The maths is pinned by
`RollNodGimbalTests`, the geometry by `validate-parts.py` against the mesh, and the mesh by
`checkmesh.py` — but none of them can see what KSA does with three subparts on one pivot, and this
is also the **first authored asset** in the mod, so its atlas, its three materials and its baked
maps are all unproven paths.

- [ ] `LITENING Targeting Pod` appears under **Sensors** in the editor and surface-attaches to a
      wing or fuselage. It cannot root a craft, which is right for a store.
- [ ] **It renders at all.** A second `<MeshAtlas>` and three `<PbrMaterial>`s alongside the
      palette one is new; a mesh Id that does not resolve is a *silent* failure. An untextured or
      magenta pod means the material Ids, not the geometry.
- [ ] It reads as a Litening: 2.2 m long, the ball nearly as fat as the body, the lugs on top.
- [ ] **The nose rolls and the ball nods.** Watch it while the head tracks a crossing target: the
      shroud and its cheeks sweep round the centreline, the ball tilts within them, and the ball
      never parts company with the shroud. That last is the one thing the tests cannot see.
- [ ] **The recession faces the way the sight looks.** If the pod is looking out through the
      *closed* side of the shroud, `CLOCK_DEG` in `import-litening.py` is half a turn out.
- [ ] The **Camera** row says `roll-nod gimbal`, with a roll and a nod that move as it tracks.
- [ ] Looking dead ahead it says **in the keyhole** and holds ~4° off the centreline rather than
      spinning the nose. This is the alt-az-at-zenith singularity and is expected, not a fault.
- [ ] Looking aft and down it says **at the nod stop** at 150°, and the ball is still inside the
      shroud there — the shell clears the sightline to 158°, so it should have 8° in hand.
- [ ] **Derotation.** Roll the aircraft, or track a target right round the pod: the picture keeps
      the airframe at the top rather than turning with the nose.
- [ ] The pod stows looking **out of its mounting face** — straight down under a wing — rather
      than dead ahead, because dead ahead is its keyhole.
- [ ] **Shimmer at range.** `checkmesh` reports 451 near-coplanar pairs on the authored mesh, at
      gaps of 0.3–4 mm and up to 75 cm² — panel steps, decals and the shroud's shell wall. There
      are no *exact* coplanar overlaps, and KSA's reverse-Z depth buffer should hold sub-millimetre
      gaps apart, so this is expected to be fine. **Look at the pod from a few hundred metres
      anyway**; if its panels crawl, the gaps want opening up in Blender rather than in the import.
- [ ] A craft carrying a pod *and* an EO director runs both, each on its own gimbal, and the
      panel describes each in its own terms.
- [ ] Not modelled: the airframe masks nothing, so the pod can look up into the wing it hangs
      from. The sensor cone points out of the mounting face, which keeps it off that direction
      without forbidding it.

### 7.6d The suspension rail — carriage gear the builder cannot place

**Hidden rather than deleted.** A bare rail carries nothing and is registered as no launcher, so
placing one would do nothing at all — but the asset is shared with the stores that ship, and a part
removed outright stops every saved craft carrying it from loading. It therefore carries
`EditorTag Value="Hidden"`, which `PartTemplate.IsHidden` reads and `VehicleEditor` checks in the
part list, the diameter filter and the root-part test. It has no profile in `Sim/Arsenal.cs` at
all: it neither shoots nor sees.

- [ ] It is **absent** from the editor's part list, and a saved craft carrying one still loads.
- [ ] It renders and is textured on a craft that has one. Its atlas and material are its own; a
      magenta or untextured rail means the Ids, not the mesh.

### 7.6e The terrain map

**Never flown.** The frame maths is pinned by `TerrainMapTests`, but nothing has sampled a real
height field — so the two numbers that matter most, what the ground looks like and what a scan
costs, are both unmeasured.

- [ ] **Map** on a director's row opens a window; the button tints while it is open.
- [ ] The square shows recognisable relief — a hill reads as a hill. Flat, banded or noise means
      the height field is answering differently from how `TerrainMask` uses it.
- [ ] **The scan cost.** The legend prints `scan N ms`. That is the number
      `SensorProfile.TerrainSamples` has never had, so **write it down**: at 64×64 it is 4096
      lookups. If it is tens of milliseconds the cell count wants dropping; if it is under one,
      terrain masking is far cheaper than assumed and that is worth knowing on its own.
- [ ] Moving the craft a short way does **not** re-scan; moving a tenth of the span does. Watch the
      log with **Verbose** on — a line per frame means the cache is not working.
- [ ] Zoom: **+ shows less ground**, - shows more. Steps 500 m to 10 km, and the range rings stay
      honest against known distances.
- [ ] **The heading arrow points where the craft is going over the ground**, and the legend agrees
      with it: heading clockwise from north, ground speed, and climbing or descending. Fly a known
      compass direction and check the arrow matches the world rather than being mirrored or 90°
      out. Hovering shows no arrow, which is right — there is no heading without ground speed.
- [ ] In orbit the arrow should read as orbital motion over the surface, not 29.8 km/s of the
      planet's own travel. A heading that never changes wherever you point means the ecliptic
      velocity is leaking in.
- [ ] North is up and matches the world. A map rotated by ~23° means the axis is being read as the
      ecliptic pole somewhere.
- [ ] Contacts sit where they are. An off-map contact shows as a triangle on the rim it left, not
      dropped and not clamped into the corner.
- [ ] The blue line runs from the craft to where the sight meets the ground, and tracks with the
      pod.
- [ ] Over ocean or unstreamed terrain: cells go **dark**, and the legend counts them. Flat grey at
      0 m would mean an unreadable field is being read as sea level.
- [ ] At a pole: it says there is no bearing rather than drawing a rose pointing anywhere.
- [ ] Not built: no structures, no ground clutter, and no memory — a contact the pod stops seeing
      leaves the map.

### 7.6 The gunner's sight — symbology, zoom and the two reticules

**Flown once. Zoom works and hands the view back; two faults found, both addressed and neither
re-flown.**

- The overlay was a full-screen ImGui window submitted after the panel, so it drew **over** the
  panel. It is now on `ImGui.GetBackgroundDrawList()`, which renders beneath every window and is
  what the game uses for its own main-viewport overlays.
- At 16× the bracket sat **off the target**, which was also jittering up and down. The sight
  projected `track.PositionEcl` — the analytic position — while the craft is drawn at the physics
  one. `KsaWorld.TryVehicleEgo` documents that exact mistake ("lines drawn to it visibly miss the
  craft"); at 3° of field the gap is tens of pixels rather than the noise it is at 50°. The
  bracket and the pipper now both come off the drawn position.

  **The jitter is a hypothesis riding on the same change**, not a separate fix: the analytic
  sample is the mod's and one step old, the camera is placed by the engine this frame, and the
  display's 8.33/25.0 ms pacing makes that difference alternate. If it still jitters after this,
  the cause is elsewhere and the measurement to take is the bracket's screen position per frame.

The maths is covered by `SightZoomTests` and `SightPictureTests`, and the maths is not what is in
doubt: every item below is a question about whether KSA honours a write or draws what the mod
thinks it drew.

- [x] The zoom narrows the picture through the detents, and the readout agrees.
- [x] Switching the optic off puts the field back.
Second flight: the overlay is under the panel and the bracket is closer, and two more faults.

- The reference **vanished whenever the head moved or elevated**. Its two ends were placed a fixed
  ±40° off the look direction and both had to project inside the viewport or the line was dropped.
  At 3° of field they are most of a right angle outside it, and at 50° they sit right on the
  horizontal edge — which is why it survived only while the head was still. The span now tracks
  the camera's own field, the projection keeps out-of-bounds coordinates and lets the draw list
  clip, and only a point *behind* the camera is dropped.
- It is an **arc rather than two ends** for a reason that only appears once the span is wide:
  level places lie on a circle, and a straight chord across ±40° sags 3.4 km below level at 30 km.
- The bracket was still **off centre at 16×**. Not the bracket this time — the head is *commanded*
  at the target's analytic position, so the camera boresights a few metres off where the craft is
  drawn. It now takes the drawn position, the same as the bracket.

Third flight settled the centring fault by measuring it at two ranges, which is the only thing
that could have: the boresight cross sat far above the target at **0.72 km** and almost on it at
**9.03 km**. A fixed distance subtending a shrinking angle, so the cause is geometric and not
screen-space — and the direction is right too, because the displacement is mostly the 4.10 m
*up* the traverse axis, which is why the cross is above rather than beside.

The optical head was commanded a bearing measured from the launcher part's origin while the head
itself stands 4.14 m away from it, so it was laid *parallel* to the right bearing and displaced
off it. `WeaponSystem.AimOriginEcl` already carries that whole diagnosis in a comment — for the
tube drives, which were fixed for it. The optic never was. Predicted 9.9% of the vertical field at
0.72 km against 0.8% at 9.03 km, a ratio of 12.5.

**What this retires:** the framebuffer-versus-viewport-pixels theory, which predicted an offset
that is a constant fraction of the screen at every range. It is not that, and the algebra said so
too — a scale error there would displace the *bracket* and leave the cross correct.

- [x] The overlay stays **under** the panel.
- [x] The reference survives the head slewing and elevating, and only disappears looking straight
      up. **Confirmed in flight.**
- [ ] The target sits at **screen centre** at 16× once the head has settled — check it at **both**
      long and short range, because only the short one could ever have shown this.
**Then a second fault underneath it, separated by the one experiment that could:** with the
simulation **paused** the cross sits exactly on the target, and the offset grows with simulation
speed. Geometry does not care about time, so that residue is a lag, and pausing is what proved the
parallax fix had landed.

The mod's whole update and draw is a postfix on `OnDrawUiViewports`, which runs *after* the
viewport pass that builds the frame's matrices. So a camera aimed from there is consumed on the
**next** frame: the view is drawn along a direction solved one frame ago while the target is drawn
where it is now. One frame of the target's angular motion, times the simulation speed — 1.8% of
the field at 1× for a 42 m/s target at 0.65 km, and 30% of it at 16×, which is what was seen.

`LevelHorizonController.OnFrame` is the only mod code that runs *inside* that pass, so the pose is
asked for again there through `IViewPose`. While the head is settled on what it follows — a
designation, or the set's pick with tracking on — the view is re-solved onto the target's own
position at that instant; while it is still slewing, or held by the mouse or the sliders, the
head's own axis is used, because a target sliding towards the middle is what slewing looks like.

- [x] Paused, at 1×, and at high warp: the cross stays on the target at all three. **Confirmed in
      flight** — both centring faults are closed.
- [ ] **Mouse aim** with a contact tracked: drag the head off it and let the cursor come to rest
      inside the ring. The picture stays where the head is rather than jumping back onto the
      contact, and the contact's bracket reads `MOUSE AIM`.
- [x] Start a chase transition **from 16×**. It flies at the player's own field, not down a
      three-degree straw, and the magnification comes back when the chase stands down.
      **Confirmed in flight.**
- [ ] It holds still. Still unconfirmed either way, and now separable: with both systematic offsets
      gone, anything left moving is the epoch question rather than either of these.
- [ ] Known and not yet fixed: `PointingDrive.OnTarget` is a fixed 1° window, which at 16× is a
      third of the vertical field. The brackets close and `SLEWING` clears while the head can
      still be visibly off, so "settled" is not evidence of anything at magnification.

**Zoom is the one with a crash behind it.** `Camera.SetFieldOfView` does not clamp and
`UpdateProjection` throws for a field of zero or more than half a turn, out of the frame hook.
`SightZoom.MinFovDeg` guards it; that guard has never been reached in game.

- [ ] Put the optical head on the **main view** and step the magnification 1× → 16×. The picture
      narrows each time. If it snaps back to something wide within a frame, the mod's write is
      losing to KSA's own and the field is being reset rather than kept.
- [ ] At 16× the readout says `x16` and a field of about 3°. If it says 3° and looks unzoomed, the
      write is being ignored; if it says 172° the radians-versus-degrees conversion has come back.
- [ ] Press the game's own zoom keys while magnified. Expected: the picture jumps to 15° for at
      most a frame and the mod puts it back. A permanent jump means the per-frame rewrite is not
      running.
- [ ] Switch the optic **off**. The field returns to whatever it was before — not to 50°, and not
      left at 3°. Left narrow is the failure that strands a player: their own keys clamp at 15°
      and cannot widen past it.
- [ ] Take the view back through KSA's **View → Orbit Camera**. Same again: the field comes back
      with it. This is the `StandDown` path rather than the release one, and they restore
      separately.
- [ ] Let the chase camera take the view mid-salvo while the sight is magnified, then let it
      finish. The sight's zoom is still there afterwards.

**The two reticules.** Only visible on a target inside the cannon's 200–4000 m envelope, which is
the same band section 7.1b needs — fly one engagement and check both.

- [ ] Inside the band, a **second ring** appears away from the target bracket, with a line joining
      them. That gap is the lead. Outside the band there is one reticule and no line.
- [ ] The ring sits where the shells actually go. Fire and watch: tracers should pass through it.
- [ ] The status block says `GUN HAS THE RING` while it does, and `MSL HAS THE RING` otherwise.
      Missiles are held in the first state by `FireGate.MissilesMayFire`, so the two must agree.
- [ ] The ring grows as the target closes. It is sized to what the shell covers, not to an icon.

**Symbology.**

- [ ] The horizontal reference crosses the picture and **tilts** as the head elevates, rather than
      lying flat across the screen. Flat means it is being drawn in screen space, which is right
      only at the one pose anyone checks first.
- [ ] Looking straight up, the reference disappears rather than drawing a stroke in an arbitrary
      direction.
- [ ] Slew onto a target hard enough to lose it off the edge at high magnification: a **chevron**
      appears at that edge pointing after it, with the range beside it. Behind the camera counts —
      the chevron must point backwards correctly, not at its mirror image.
- [ ] Missile count and belt count in the top-left track the panel.
- [ ] **Sight symbology** off leaves the target bracket and takes everything else away.

### 7.6f The chase camera — the turn onto the target, and letting go

`ChaseViewTests` and `ChaseInterestTests` hold the arithmetic; none of this has been flown.

- [ ] Take a chase from the orbit camera with the craft in the middle of the screen. The view turns
      onto the missile at the take — the one cut left — and then, as the eye travels in behind it,
      turns onto the target, arriving on it as the transition ends. Nothing whips round in the
      last few frames.
- [ ] Take one through the sight. The turn is small: the sight was already on the target and the
      missile leaves towards it.
- [ ] Fire two at one drone and chase the second. When the first kills it the chase stays about
      two seconds — the burst should be ahead of the round, in frame — then hands the view back.
      The log says `chase: <tube> has nothing left to arrive at, handing the view back`. It must
      not cut to another missile.
- [ ] A missile that misses: the view comes back about two seconds after it passes the target, not
      when it expires.
- [ ] Pause inside those two seconds. The chase stays until the world runs again.
- [ ] Drop a B61 on nothing. The chase rides it all the way down and holds on the burst.
- [ ] Drop a B61 designated on the ground below, from a hover and from level flight. The chase closes
      in steadily for the whole fall: no lunge in the first seconds, and no pull back out before the
      burst.
- [ ] Drop a B61 onto a point almost straight below the craft, so the chase looks nearly straight
      down. The bomb never appears to turn half a round as the view passes over the point, and the
      log has no `chase: the eye swung ... deg round the round in one frame` warning.
- [ ] Drop a B61 and lose the craft that dropped it before the bomb lands. The log says
      `pinned platform lost` but no `chase: released the main view` until after the burst: the chase
      rides the fall to the ground (the bomb itself cannot be drawn once its craft is gone), holds on
      the burst, and then leaves the view over it where it can be orbited, rather than off in space.
      The explosion and the cloud still appear, with no `no explosion ... no celestial to hang it on`
      warning.
- [ ] Right-drag while a chase rides a bomb. The camera swings round it the way the orbit camera
      does — drag down to look from above — with the bomb staying where it was on screen and the
      cursor hidden while dragging. Let go: it stays a moment, then eases back behind the bomb in
      about a second. The wheel moves in and out and eases back the same way.
- [ ] Pause while a chase rides a round, right-drag and let go: the view stays where it was put for
      as long as the game is paused, and eases back behind the round only once it runs again. Pause
      during the hold on a burst: the view stays on the burst until the game runs, rather than
      being handed back three seconds later. Through slow motion both still take their usual
      second or three.
- [ ] End a drag with the cursor over the panel. The view still eases back rather than staying
      turned. End one over a craft: no part window opens. A plain right-click on a part, with no
      drag, still opens its window.
- [ ] Drag the view under a bomb falling onto a designated point. The eye stops short of the
      ground rather than going into it.
- [ ] A chased round that expires while still closing hands the view straight back, logging
      `chase: the round expired, nothing to hold on`, rather than holding three seconds on nothing.
- [x] Switch vessels with `[` or `]` while a chase rides a round, and again while it holds on the
      burst. The view is back in the orbit camera on the craft switched to and the mouse rotates
      it; the log says `chase: the view was taken over by hand (vessel), standing down`. A view
      that will not rotate is still in Fixed, and the mode was not put back.
      **Confirmed in flight.**
- [ ] Take the view back through **View → Orbit Camera** during a chase. The camera orbits the
      launching craft rather than the spot the round was at, and the log says `(camera mode)`.
- [x] Chase a shell low over the ground at 1x. The view holds steady rather than flipping up and
      down several metres a frame. **Measured in flight on 2026.9.10.5438** with
      `KSARMORY_SCENARIO_CHASE=1 KSARMORY_SCENARIO_SPEEDS=0.05,0.1,0.25,1 ./tools/scenario.sh gunnery:1,ground,,,12000`:
      203 frames at 1x jumped by up to 60 m while the camera was under about 175 m, then none with
      it down to 10 m.
- [ ] The same by eye at 0.1x and 0.25x, chasing a Mk 42 shell at BIG BOOM's tank stack on the
      machine that showed it at 34 fps. The scenario measured the camera; nobody has watched the fix.
- [ ] Chase a Mk 42 shell onto the ground, and again onto a craft. About 60 m before it arrives the
      camera stops where it is, turns after the shell as it flies on, and holds on the burst from
      there, logging `chase: stopping short of where <shell> arrives, to watch it go in`. The view
      never ends up inside the explosion.
- [ ] Chase a B61 onto the ground. The camera stops about a kilometre short and watches the burst
      from there.
- [ ] The shell is chased from its own lengths away: it fills about as much of the picture as a
      Pantsir missile does, rather than being a speck 26 m ahead, and the closing log line's
      stand-off agrees.
- [x] Chase a Mk 42 shell on a long shot, 60 km out with `gunnery:1,ground,,,60000`. The shell stays in
      the picture the whole way down, and the log says once how far from the camera the mount went under
      a pixel and that it is being drawn anyway. Before, on 2026.9.10.5438, it vanished part-way while the
      mod went on placing it 3.7 m ahead of the camera: `Vehicle.UpdateRenderData` draws none of a craft
      under a pixel across, and the shell's body is one of the mount's parts. **Confirmed on
      2026.9.10.5438** with the hook: `NewRocket_1 is 1.00 px across 20.1 km from the camera ... drawing it
      anyway`, and screenshots 12 s and 58 s later show the shell's body ahead of the camera, at 1440p.

### 7.6g The switcher — the panel's list, one row per craft

`GuardStateTests` and `IffTests` hold the rules; none of this has been flown.

- [ ] Craft sit under their team's name in its colour, in the declared order, with "No team" last.
- [ ] Clicking a name flies it, and the craft being flown is tinted. Right-clicking a name turns
      the view to it and pins its label without taking the seat.
- [ ] The shield is grey when nothing is guarding, amber when some weapons are, green when all
      are. One click from grey or amber turns auto-engage on for every weapon on the craft; one
      click from green turns it off. The craft's own window agrees afterwards, FIRE works in every
      state, and a craft carrying only stores has no shield.
- [ ] The camera icon chases that craft's rounds once it is flown or its window is open.
- [ ] Clicking the flag opens a menu: the declared teams in their colours with the current one
      ticked, then **No team**, then a **New team** box. Picking one moves the row to that team's
      group at once, tints the flag and closes the menu.
- [ ] Typing a name in **New team** and pressing Enter creates the team, puts the craft on it and
      closes the menu. With no teams declared the box has the cursor as soon as the menu opens.
- [ ] Right-clicking the flag steps to the next declared team, wrapping and never through none, so
      with two teams it switches between them.
- [ ] "..." opens the craft's full window.
- [ ] The icons draw cleanly at the UI scale in use: nothing clipped, nothing off-centre.

### 7.6h Shift-click locking onto a craft

Unflown. The pick is the hull the cursor ray meets, then a craft's centre within 24 px.

- [ ] Shift-click a craft close enough to fill a good part of the screen, **near its edge** rather
      than its middle, wherever KSA outlines it in orange. The log says
      `lock: <craft> is under the pointer` and then `tracking <craft>`, never
      `tracking Earth <lat>, <lon>`.
- [ ] Shift-click a distant craft only a few pixels across. It still locks, through the centre
      grace, with no `is under the pointer` line.
- [ ] Shift-click open ground beside a craft. It designates the ground.
- [ ] Shift-click a craft standing behind a ridge from the camera. The ridge wins: ground, not
      the hidden craft.
- [x] Shift-click a hill seen side-on from low down. It designates the hill under the pointer, not
      the terrain behind it. **Confirmed by hand on 2026.9.10.5438.**
- [ ] The same pick sets a craft down with **Move craft with the mouse**, sets off a burst with the
      burst tool and aims click-to-shoot. Set a craft down on a hillside, and on a plateau well above
      sea level seen at a shallow angle: it lands under the pointer on the first click, where before
      it took several clicks walking in towards the spot.

---

## 12. The ballistic computer — flown, and landing inside a hundred metres

**Six warheads within 20–90 m of the aim**, on a 3,460 km deorbit from a 207 km pick-up. Measured
2026-08-24 as a 6-against-6 interleaved batch, median **0.05 km**, group spread 0.02 km.

It got there from 2.88 km in one day, and every step was a term the round carried and its own
predictor did not:

| | miss | what it was |
| --- | --- | --- |
| where the day started | 2.88 km | |
| + the body's own fall | 0.72 km | a round felt its parent body and nothing else while KSA carried that body along its orbit |
| + gravity aimed mid-frame | 0.45 km | the pull centre sat a whole frame of the body's travel away |
| + gravity aimed per sub-step | **0.05 km** | the rest of the same term |

Each was flown against an interleaved baseline: ratios 0.25, 0.58 and 0.11, all with p under 0.01
and none overlapping. `docs/MIRV-NEXT.md` item 2 is the account.

**The tube cant is no longer what is left.** The tubes were straightened, and the salvo spread is
now 0.02 km — the six warheads land closer to each other than any of them lands to the aim.

**What is left, in order.** The release probe's own miss, about 50 m, which is what the aim
correction leaves behind; the round's own integrator at 5 ms, which a screen priced at tens of
metres and which is now below what one shot resolves; and everything in
`docs/KINETIC-FLOOR.md`, whose irreducible floor at this 7.1° arrival is about 5 m.

**And accuracy has stopped buying anything.** The Mk 21's lethal radius is 2,000 m, so at 50 m every
warhead is 40× inside it. Further work here is craft rather than capability.

Fit a KSArmory weapon to any rocket — the MIRV bus is the one it is for — and open
**Ballistic** on that craft's window.

### 12.1 It knows where it is

- [x] The tab says `flying about <body>` with the right body.
- [x] **Designate by clicking the world** on: a ring follows the cursor over the ground, and
      vanishes over the sky. Clicking sets a latitude and longitude that match what KSA's own
      readouts say for that place.
- [ ] The ring greys out over a body that is not the one being flown around.
- [ ] With the tool off, world clicks do nothing to the target. A click on the panel never does,
      either way.
- [ ] Typing coordinates and pressing **Designate those coordinates** works independently of it.
- [ ] With no target: `Holding: no target designated`, and nothing lights.
- [ ] With a target and the computer disarmed: `Holding: not armed`. **The vehicle is still
      yours** — attitude, throttle and staging all respond to the keyboard.

### 12.2 It solves a shot

- [x] Armed, on the pad, with a target a few thousand kilometres away: an apogee and a flight time
      appear, and both are plausible (hundreds of kilometres, tens of minutes).
- [ ] A target on the far side of the planet says `not enough in the tanks` with two numbers, or
      solves — either is fine, a wrong-looking apogee is not.
- [ ] The **Loft** slider moves the apogee and the flight time together, and 1.00 is the lowest
      *To gain* of any setting.
- [ ] The trajectory is drawn in the world as an arc, with a ring on the aim point.

**Steepest arrival — nothing here has been flown.** It is off at zero, so leaving it alone is the
behaviour every tick above was taken against.

- [ ] At zero the line under it says `off`, and the arc's own arrival angle is printed beside it.
- [ ] Raising it to 15–20 deg raises *To gain* and the arc's arrival angle together, and the
      printed achieved angle reaches the minimum rather than stopping short of it.
- [ ] It beats **Loft**: with the minimum at 15, dragging Loft from 0.6 to 1.8 never drops the
      achieved arrival below 15. That is the whole defect it was built for — off, the same 556 km
      shot arrives at 33.9 deg at loft 1.0 and 6.2 at loft 1.8.
- [ ] A minimum nothing can reach says `NO ARC ARRIVES AT n DEG OR STEEPER` and names the steepest
      it found, rather than reading as an unreachable target.
- [ ] Flown at 15–20 deg, the group is tighter than the 433 m – 1.7 km above. **This is the point
      of the whole control** and is the one line here worth a flight.

### 12.3 It flies

**This is the one to watch closely.** The likeliest failure is the attitude convention: a wrong one
is a rocket holding a perfectly steady attitude in the wrong direction.

- [ ] It lifts off vertically and holds vertical for the first few hundred metres.
- [ ] It pitches over **toward the target**, not away from it and not sideways.
- [ ] The nose stays near the airflow through max Q. The vehicle should not be visibly flying
      across its own slipstream at any point below 40 km.
- [x] It stages when a stage runs out, once, without repeatedly firing sequences.
- [x] `Phase` runs Rising → PitchProgram → ClosedLoop → Coast and never goes backwards.
- [x] *To gain* falls steadily to zero. It must not stall in the single digits and sit there.
- [x] The engines stop. If they hunt — thrusting, reversing, thrusting again — say so: that is the
      cutoff-timing path and it is the one that took the longest to get right headlessly. Flown:
      stopped 1.1 m/s short, no hunting, once the cutoff was timed to the frame boundary.

### 12.4 It arrives

- [x] *Predicted impact* converges on the target as the burn ends, and reads under a kilometre at
      cutoff. Flown at **0.1 km**, and the six warheads landed 433 m to 1.7 km from the aim.
- [ ] The drawn arc's far end sits on the ring.
- [x] The warheads release on their own during the coast, one at a time, above the release
      altitude — and they go **at the target**, not straight ahead.
- [ ] With **Release warheads automatically** off, nothing leaves until the button is pressed.
- [ ] A shot deliberately short of propellant says `burn ended N m/s short of the solution` and
      **holds its warheads**.

### 12.5 It picks up from anywhere

The phase machine no longer assumes a pad. Each of these should join at the right point rather than
trying to fly a vertical rise.

- [x] Arm it **in orbit** with a target ahead on the ground track: it goes straight to a deorbit
      burn, not a vertical rise.
- [ ] Arm it with a target the craft has just **passed over**: it says
      `holding for the burn window, H:MM:SS away` and does **not** burn. Warp through the wait —
      the mod should let you, then slow the world down as the window approaches.
- [ ] Arm it **halfway up an ascent** already under way: it takes over without pitching back to
      vertical.
- [ ] Arm it on something already **on a ballistic arc**: it corrects rather than starting over.
- [x] `IMPACT IN` counts down and keeps counting through the burn, the cutoff and the coast.
- [ ] The mark on the target stays on screen, and points from the edge when it is out of view.
- [ ] A target the stack cannot afford reads `TARGET UNREACHABLE` with a shortfall in m/s.

### 12.7 It lets go of its stack

Separation, the handover and the deployment are flown. No shipped part declares a decoupler, so it
needs a craft built with a stock 3 m decoupler between the launcher and the stack below it.

**Aim each tube before it fires** is off and stays off — flown and lost, see §12.7a — so the rest
of this section is the default path.

- [ ] A vehicle that cannot point releases anyway after a minute and says so, rather than holding
      warheads until the release altitude closes.
- [x] **With a decoupler fitted**, the launcher separates at cutoff, once, and the log names both
      craft. Flown twice: `separating the launcher from the stack before deploying`, then
      `launcher decoupled onto Rocket_1 as launcher 1, 12 m away - 6 round(s) aboard, 0 in flight`.
- [x] The weapon follows onto the separated craft carrying its magazine, its rounds in flight, its
      arm state, its teams and its IFF policy. Six aboard after the handover, not refilled.
- [x] The ballistic computer follows with it and keeps deploying — all six released.
- [ ] The spent stack is left in `Manual/None` with its engine off, and is not still being pointed.
- [ ] The spent stack drifts clear rather than staying alongside the bus.

**Accuracy, flown 20 August.** With the trim in and tube re-pointing off, six warheads landed
**431 m, 537 m, 607 m, 1.1 km, 1.2 km and 1.4 km** — against 3,100-4,100 m before the trim existed.
All six left within 67 ms and landed within 32 ms of each other, off a cutoff the mod's own
prediction called `0.0 km off`.

What is left is two terms. The ~1 km *spread* is the tube cant, which re-pointing was for and could
not remove — §12.7a. The ~900 m *bias* is that every round landed beyond its own release probe —
`docs/MIRV-NEXT.md` item 2.

**The trim itself is flown and working.**

- [x] The panel and log show `trimming N m/s on the tail` with `thrusters measured at N m/s2`
      beside it. Measured **0.9-2.2 m/s2**, so KSA's translation flags do reach the bus's nozzles.
- [x] It settles rather than hunting: `trimming 1.23 m/s` → `trimmed to 0.010 m/s` in 1.8 s.
- [x] Nothing leaves the bus until it has. Split at `00:05:59.352`, first `round 1 away` at
      `00:06:02.002`.
- [x] Timewarp is held down through the trim as well as the burn.

**What is new since those flights, and unflown.** The trim no longer re-solves a transfer; it
carries the guidance's own cutoff state forward and subtracts what the vehicle is doing, so its
answer no longer depends on when it runs. The clearance wait is sized off the discarded stage's own
bounding sphere and capped at 20 s rather than 90, because holding a salvo back was measured to cost
kilometres.

- [ ] After the split: `waiting to clear the spent stack, N m of M` where **M now comes from the
      stage** rather than being 50, then either `clear of the spent stack at N m` or
      `going ahead ... after 20 s`.
- [ ] **The two numbers in the annotation should now agree**: `owed X m/s at the split, Y after N s
      of clearing`. X and Y within a few per cent of each other is the whole point of the change.
      They read 0.21 → 228.97 before it.
- [ ] The trim runs and ends `trimmed to 0.0N m/s` rather than
      `more than a separation could have cost`.
- [ ] Release follows within a few seconds of cutoff, not a minute and a half. The release probe
      should read close to what the cutoff line said (`own prediction 0.1 km off`), not kilometres
      more — that gap is item 2b and is the thing this shortening is working around.
- [ ] Write down the standoff it settled for and whether it timed out. Twenty seconds at the flown
      0.26 m/s of decoupler shove is about five metres, so expect a timeout and a small number;
      the question is whether that is visibly a problem.
- [ ] The bus still visibly comes off the booster rather than sitting on it.
- [ ] Impacts. Anything worse than the 431 m - 1.4 km best means the shortening did not buy what it
      was meant to.

**Never yet exercised:** whether the bus's ~183 kg of MMH/NTO lasts. Nothing has spent it.

### 12.7a Turn re-pointing — flown, and it lost

**Settled, and there is nothing to fly here.** Flown on a separated bus with the sequencer on and
the axes latching properly: commanding six degrees off the held line made the vehicle *hunt*, the
six tubes releasing at 5.2, 2.1, 8.2, 12.8, 14.1 and 11.7 degrees off it — against the six degrees
of cant the turn exists to remove. The sweep never came under the gate, every release was a
timeout, and the salvo took three minutes, against 1.7–0.3 km on the same shot without it. So
`RepointBetweenReleases` stays **off**, and the ~1 km spread is the cant.

Reopening it means a different bus — finer RCS, more inertia, or thrusters placed for translation
— which is a craft design change rather than a mod one. `docs/MIRV-NEXT.md` §5 and §5a carry the
numbers and the engine reason.

### 12.7b A director that rides away on a split

Unflown, and **the case cannot be built from shipped parts alone** — nothing that separates carries
a director. To construct it: root a stack on something that stacks, put a decoupler in it, surface-
attach an **EO director** to the tank above the decoupler, and put a command part above so the upper
half stays a live craft after the split.

- [ ] Before staging, give the head something to lose: a distinctive magnification, tracking on, and
      a shift-clicked designation.
- [ ] Stage the decoupler. Expect **one** director in the panel afterwards, on the upper craft, still
      at that magnification and still watching what it was told to. The bug being fixed looks like
      *two* — the second parked at default zoom watching nothing.
- [ ] The log names both craft, as the weapon roster's handover already does.
- [ ] With **two** directors on the separating half: both follow, and their ordinals stay in part
      order. This is the path the handover's ambiguity rule was changed to open, and it has no
      in-game evidence at all.
- [ ] Control: split a stack whose director stays on the *lower* half. Nothing should move and
      nothing should log.
- [ ] A Pantsir on a decoupler exercises both rosters at once, since its roof director shares the
      launcher's part Id. They search independently and must agree — a disagreement shows as the
      sight and the weapon reporting different craft.

### 12.7c Shell labelling and the overrun warning

Both unflown.

- [ ] Fire the CIWS at something and confirm all four events — shot down, expired, arrived,
      detonated — read `shell from barrel N` and never a negative number.
- [ ] Confirm missiles still read `round 1..12`. The tube path is unchanged but shares the call.
- [ ] Load a scene and confirm the ~48 s first frame produces **no** warning about rounds lagging,
      with an empty sky. It should appear under **Verbose log** only.
- [ ] Then warp hard with a salvo in the air and confirm the warning still fires on the *first*
      such frame. That is what the per-kind rate limit exists to protect: with one shared counter
      the load frame spends the slot and the real overrun is silent.

**And the site shot two of them down.** The Pantsir at the target detected a warhead at 20 km, fired
two interceptors, killed it at 11 m, re-laid on the next at 4.1 km and killed that at 15 m. The two
it picked were the most accurate of the salvo, because flying accurately means flying at the
defended point.

### 12.7d The aim correction is allowed to walk past its own best

Unflown, and headless only. The predicted miss is not a monotonic function of the aim, so
`AimCorrection.WorseBeforeStopping` is 12 rather than 3 and the loop crosses a patch of cycles that
make it worse before it improves again. Headless at 7,645 km that is 15.74 km of flown miss down to
1.15; 2,000, 3,459 and 5,000 km are unchanged. Read it off the `aim: bias N km, predicted miss N km`
lines under **Verbose log**.

- [ ] Fly a long shallow shot and watch those lines. The predicted miss **rising for a few seconds
      and then falling again** is the loop crossing the hump, not a failure.
- [ ] The bias stops moving before cutoff and the arrival commits. A loop still walking when
      `IcbmProgram.LatchArrivalWithinSeconds` runs out is frozen wherever it happens to be, which is
      the one thing more patience can cost.
- [ ] Impacts at the flown 3,459 km geometry are no worse than the 431 m - 1.4 km group. Nothing
      about that range changed headlessly, so anything that did move is the extra patience.

### 12.7e The bus's pointing band — three changes, flown as one arm

The separated bus held a **9.63 degree** pointing band and it was almost all one artefact. KSA
widens `AngleDeadband` to what one control period of the minimum thruster impulse can produce — a
stability guard, so the tracker is not asked to settle inside its own quantum — but it takes a max
against the standing value and nothing lowers it. At the frame the bus becomes its own vehicle its
mass properties resolve and the rate bit momentarily reads **55 deg/s**; `0.2 x 55 = 11.04`, and
that one frame set the guard for the entire deployment.

Measured, same aim point, arms interleaved:

| | base | with all three |
| --- | --- | --- |
| `AngleDeadband` | 11.40° | **0.20°** |
| pitch/yaw rate bit | 0.393 °/s | **0.027 °/s** |
| **pointing band, separated bus** | **9.63°** | **0.37°** |
| band while attached | 0.70° | 0.31° |

- [ ] **The band holds at ~0.37°.** Read `Rocket_N control:` under **Verbose log**. One reading of
      a few hundred degrees at the separation frame is the transient and is expected; a *second*
      one, or a band that stays wide afterwards, means the profile assignment is not landing.
- [ ] **No chatter.** A deadband below what the thrusters can settle inside is a limit cycle rather
      than precision. `0.2 x rate bit` is 0.005° against a 0.20° floor, so the guard is not binding
      — but watch the bus for buzzing, and watch RCS propellant to the last release.
- [ ] **The ascent is unharmed.** `SetAttitudeProfile` also sets `RateLimit`, and Strict's is
      30 deg/s against Balanced's 5. The commanded direction is still limited by the ascent
      profile's angle-of-attack limiter, so this should only make small corrections quicker.
      Anything that looks like a snap or a slew off the pitch programme is this.
- [ ] **The trim still converges.** Nozzles at ~394 N give 0.25 m/s², so a direction moves 1.0 m/s
      inside the 4 s it has to prove itself. Watch for `nothing left aboard moves the bus` or
      `the trim stopped closing`, either of which means the cut went too far. First flight trimmed
      to 0.032 m/s against a baseline 0.010, which is worth watching over a batch rather than one
      shot.
- [ ] **Warheads leave on the line.** `warhead away from tube N, X deg off the salvo's line` — the
      first flight read 0.00 for all six. That number is the band converted into what it was
      costing.

### 12.7f The bus's divert reach — nothing here has been flown

Headless only. `ReachDisplayTests` pins that the outline and the refusal are the same ellipse and
that the axes are carried back from the arrival, but no flight has drawn one — and the two things
only a flight can settle are whether the region lands on the right ground and what it costs the
frame.

Fly a MIRV shot, and once the burn is over and the bus is coasting:

- [ ] With **Designate by clicking the world** on, an outline appears on the ground around where the
      warheads are going, a few kilometres across. It sits *around* the aim ring rather than beside
      it — a region drawn a planet's turn out of date lands hundreds of kilometres downrange.
- [ ] The cursor ring is the designation colour inside it and grey outside, with **outside reach**
      beside the cursor. A click outside does nothing at all: no new row on the panel, no line in
      the log.
- [ ] A click inside adds a target. Its ring is drawn dimmer than the lead's, numbered to match the
      panel's list, and it does not move when the planet turns under it.
- [ ] The panel's `Divert reach:` line agrees with the picture — the metres it quotes are about the
      radius of the outline, not four times it.
- [ ] **The frame cost.** `icbmdraw` in the mod-frame line was 11.1 ms of a 15.1 ms frame before
      this; the worst frame is the number to read, because only one ring is re-draped per frame and
      a spike would mean that rule is not holding. And `reach on <craft>` in a verbose log carries
      what one pricing flight took.
- [ ] Turning **Show what the bus can still divert to** off stops both: no outline, and no
      `reach on <craft>` lines.

### 12.6 It gives the vehicle back

- [ ] **Abort** stops the engines and returns attitude control. Flying by hand works immediately
      afterwards.
- [ ] Disarming mid-flight does the same.
- [ ] Destroying the craft mid-flight does not throw, and nothing in the log complains afterwards.

### 12.8 Timewarp

A burn now asks `WarpPolicy` to hold the world down, the same way rounds in the air do. This is the
section that proves it, and it is the failure that produced a 3,255 km miss before it existed.

- [ ] Arm a shot and wind the timewarp up hard. The mod should hold it down and log
      `timewarp held at Nx`. It must not sit at 1000x while the engine burns.
- [ ] Move the speed yourself while it is held: the mod stands down and logs
      `timewarp not held`, rather than fighting you for the control frame by frame.
- [ ] If a slowdown is refused outright, the burn is **abandoned** and the log says why. Check the
      vehicle is handed back rather than left pointing at a target it can no longer reach.
- [ ] After cutoff the hold is released — the coast is not integrated by anything, so warping
      through it is fine and should be allowed.
- [ ] `Config.LimitWarpInFlight` off restores the old behaviour. Expect a large miss; that is the
      point of the setting, not a bug.

---

## 13. The Mk 15's authored art

The CIWS moved off the generator onto an atlas of its own, `Meshes/KSArmory_Ciws.glb`. Same part Id,
same three subparts; the pivots, muzzles and colliders moved with the geometry.

- [x] It loads, crews, traverses, elevates and fires from all six barrels — flown on
      2026-09-19, and judged by eye to look right.
- [ ] A save holding the old CIWS loads with the new art and nothing in either log complains. The
      subpart Ids did not change, so it should.
- [ ] Nothing on the head passes through the cheeks from −25° to +85°, the FLIR included. That was
      swept in Blender, not by `checkswept.py`, which cannot sweep an authored mesh.
- [ ] Tracers leave from the muzzle clamp, not from inside the barrels or ahead of them.
- [ ] No speckle or flicker on the truss plates at range — they are the thinnest geometry in the
      atlas and the likeliest place for a mip to find the background.

---

## Reporting back

Most useful, in order:

1. `Logs/KSArmory.log` — the whole file. Especially `ERROR` lines with stack traces.
   the newest `Logs/KittenSpaceAgency.*.log` too if the part or XML is misbehaving.
2. Which checklist item failed and what you saw instead.
3. A screenshot for anything visual (2.2 especially).
4. For guidance misses: the fuse trigger ranges off the `detonated` lines and the closest
   approaches off the `expired` ones, plus whether it lagged behind or overshot.
