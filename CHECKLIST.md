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
- [ ] A **Fire control** row holds master arm, auto-engage, FIRE, Reset settings and the mouse
      controls.
- [ ] A second launcher of the same kind says **fitted, not run** rather than showing blanks.
- [ ] The strip above the tabs shows *Clear to fire* / *Holding fire* from **every** tab, plus
      rounds in flight and a word about the clock only when it is the problem.
- [ ] A craft carrying **only a director** opens the window, lists its Camera and Sensor rows,
      and says *no weapons system on this craft* on the rest — **without faulting**. This is the
      case that crashed twice tonight; both were null `_battery` reads on an uncrewed path.
- [ ] *KSArmory settings* holds Display, Sound, the warp hold and Debug. The main panel is the
      craft list and nothing else.
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
| **Medium** | Shooting down a round. Three seams changed at once and none of them is reachable from the test project. | [7.1d](#71d-shooting-down-a-round--never-once-worked-so-nothing-here-has-ever-been-seen) |
| **High** | The ballistic computer has never flown in game. It writes attitude, throttle and staging on a vehicle nobody designed for it. | [12](#12-the-ballistic-computer--never-flown-in-game) |
| **Low** | Guidance and fuse maths — covered by the headless suite, but never against real KSA motion. | [4.3](#43-a-crossing-target-is-intercepted) |
| **Low** | `DestroyVehicleFromEvent` may behave oddly with `Cause = Collision`. | [4.4](#44-the-warhead-kills) |

---

## 0. Before the game

Use the wrapper scripts, not bare `dotnet`. The mod targets **net10.0** and a distro `dotnet` 8
cannot build it — the scripts put the right SDK on PATH for you.

- [x] **0.1** `./tools/sync-import.sh` — Import/ populated, no errors.
- [x] **0.2** `./tools/build.sh` — succeeds.
- [x] **0.3** `./tools/test.sh` — the suite passes.
- [x] **0.4** `./tools/validate-parts.py` — "OK: 26 asset reference(s) resolve".
- [ ] **0.5** `./tools/deploy.sh` — prints an install path containing `KSArmory.dll`,
      `mod.toml`, **two XML files at the root**, `Meshes/KSArmory_MeshAtlas.glb` and three
      PNGs under `Textures/`. An `Assets/` folder left by a previous layout must have been
      deleted by the script — two copies of the part would fight over one Id.
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
(`KittenSpaceAgency.log`, same folder) covers mod discovery and asset loading — that is where
part XML errors would appear.

The in-game console is a nice-to-have: toggle with **`\`** (backslash), `help` lists commands,
`simspeed 1` forces real time. On an AZERTY keyboard the toggle is bound by *physical key
position*, not the printed label, so `\` is likely the key next to left-Shift or beside Enter.
If you cannot find it, ignore it — the log files cover everything on this checklist.

---

## 1. The mod loads

### 1.1 The mod loads

- [x] Launch via **`StarMap.exe`**, not `KSA.exe`.
- [x] `KittenSpaceAgency.log` contains `INFO found mod 'KSArmory'`.
- [x] StarMap prints `Loaded mod: KSArmory from manifest`.
- [x] `Logs/KSArmory.log` contains `loading (mod id: KSArmory)`.
- [ ] Then `ready - 12 tubes, safe.`

Both StarMap hooks fire. `[StarMapAllModsLoaded]` lands about **21 s** after
`[StarMapImmediateLoad]` — it waits for the game to finish loading, so the `ready` line appearing
late is normal, not a hang.

**If it fails:** no `KSArmory.log` at all means StarMap never ran the mod's entry class. Check
`mod.toml`'s `EntryAssembly = "KSArmory"` matches the DLL name, and that StarMapConfig.json
points at the right KSA folder. An exception mentioning TOML means the `assets` array in
`mod.toml` isn't tolerated there; move the part XML into a second mod folder and keep
`mod.toml` StarMap-only.

### 1.2 No XML parse errors

- [x] Nothing in `KittenSpaceAgency.log` about failing to load `KSArmoryAssets.xml` or
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
- [x] Under **Structural**, find **Pantsir-S1 Point Defence System**.

**If it fails:** the `PartGameData` didn't register. Check the `EditorTag Value="Structural"`
and that `mod.toml`'s `assets` paths match where deploy.sh put the files — they are now at the
mod root, **not** under `Assets/`.

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

- [ ] Surface-attaches to the side of a fuel tank.
- [ ] Attaches to the top of a stack via its rear node.
- [ ] Symmetry placement works (2×, 4×).
- [ ] **Stands alone** — a craft consisting of only the launcher builds and launches, with no
      command pod. This is the quickest way to test everything else.

**If it fails:** connector flags. `_adConnectorAft` needs `<Flags>ToSurface</Flags>` in
GameData and a matching `<Connector Id="_adConnectorAft">` with a `<Transform>` in Assets —
the Ids must match exactly between the two files.

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
- [ ] Header shows `Platform: <your craft>` and `Launcher: Pantsir-S1 fitted` in green.
- [ ] Shows `MASTER ARM: SAFE` in green and `Rounds: 12/12`.

**If it fails:** `Launcher: none fitted` while the part *is* on the craft means
`LauncherPart.Find` isn't matching — `Part.Id` may not equal the `PartGameData` Id. Untick
**Require launcher part** to keep testing everything else, and report it.

### 3.1b The turret slews

**The open question is whether KSA honours a runtime transform write at all.** Everything else
here is instrumented so the answer is readable either way.

- [ ] `KSArmory.log` has a `launcher subparts: ...` line naming both subparts. If the turret
      one is missing or oddly named, `Part.ResolveRuntimeId` rewrote it and `TurretMarker` in
      `LauncherPart.cs` needs to match whatever is actually there.
- [ ] The panel shows `Turret: N deg` and not `subpart not found` or `engine refused`.
- [ ] A **cyan line** points out from above the vehicle. That is where the drive *thinks* it is
      aimed — it comes from the mod's own maths, not from the engine.
- [ ] Spawn a test target. The cyan line sweeps round to follow it, taking about a second.
- [ ] **The turret mesh follows the cyan line.** This is the actual test.
- [ ] The twelve tube markers stay on the container mouths as it turns.
- [ ] Turning **Track with turret** off in *Radar → Turret* returns it to facing forward.

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
- [ ] Adjusting *Radar → Range* and *Cone half-angle* resizes it live.

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

**Use the built-in spawner.** *KSArmory settings → Debug → Test targets*: set time-to-pass,
speed and miss distance, then press **Overhead**, **Head-on** or **Passing by**. A drone
appears that far out on exactly that course, so a 60 s / 400 m/s overhead pass spawns 24 km
away and arrives in a minute. Drones are clones of your own craft, so no second craft needed.

Arm the battery *before* the drone arrives — with **Auto engage** on it will handle the rest.

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
- [ ] Lock indicator goes `acquiring...` → `LOCKED` after *Lock time* (0.8 s).

---

## 4. Engagement

Do all of this at `simspeed 1`.

### 4.1 Manual fire

- [ ] Tick **Master arm**. Header turns red, `MASTER ARM: ARMED`.
- [ ] With a lock, press **FIRE**.
- [ ] `Rounds` drops to 11/12; one muzzle dot turns grey.
- [ ] A tracer leaves that tube with a trail behind it.
- [ ] Console logs `[KSArmory] round N away at <target> (X.X km)`.

**If it fails:** "refused: …" in the log tells you which gate stopped it — not armed, no
launcher, empty, or target gone.

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
- [ ] A detonation between *Lethal radius* and *Blast radius* logs `near miss on <name>` and
      the target **survives** (damage is binary — this is expected, not a bug).

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
      (the fuse arms 0.6 s after launch specifically to prevent this).
- [x] **5.3** **Safe all** removes rounds in flight with no detonation, **and disarms**.
      Without the disarm, an armed system holding a lock fires again immediately and the
      button appears to do nothing.
- [x] **5.4** Master arm off means nothing launches, even with a valid lock and auto-engage on.

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
- [ ] **6.5** **Rocket smoke trail** — fire a Sidewinder or a HARM and watch the trail behind it
  while the motor burns, then that it stops laying at burnout and the trail stays put and drifts.
  Nothing on an airless world or above the atmosphere, which is the renderer's own limit. Check a
  CIWS burst does **not** lay one (`TotalBoostSeconds` is zero), and that a salvo beside a standing
  mushroom cloud does not visibly eat the bottom of it — both draw from one 16,384-segment budget
  per body, evicted oldest-first.
- [ ] **6.4** **Platform destroyed** with rounds in flight — the rounds **carry on** rather than
  vanishing, and still detonate and kill. No exception spam. Check the log says
  `<craft> destroyed - N round(s) still in the air`, then `last round down, system forgotten`.
  A command-link round (the Pantsir's 57E6) should coast and expire; a Sidewinder or HARM should
  still hit. **Watch a CIWS burst for this one** — a shell keeps its tracer the whole way, so the
  stream should carry on across the frame its gun is destroyed rather than stopping dead. A missile
  keeps its flame only while boosting and has no body once loose, so past burnout there is nothing
  to see and the log is the only witness. See `docs/CODE-HEALTH.md`.
- [ ] **6.5** **Pin platform** — press *Pin to this vehicle*, switch control elsewhere. The
      pinned craft keeps defending itself.
- [ ] **6.6** **Staging away the launcher** — `Launcher: none fitted` appears, firing refuses.
- [ ] **6.7** **Two launchers** on one craft — should work, still one system of twelve (by design).
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

- [ ] Enable `TimedFuse` on the gun munition and fire at a crossing drone. Shells burst **at** the
      target's predicted position rather than flying past it.
- [ ] The burst is visible. A 0.16 kg shell scales to a 0.2 effect, floored for drawing only —
      whether that reads at all at engagement range is unknown.
- [ ] A burst with the target already dead does not count as a hit. `MissDistance` is infinity when
      nothing is being tracked, and the kill path must not treat that as zero.
- [ ] With `TimedFuse` off, nothing about the cannon changed.

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

### 7.1d Shooting down a round  ← never once worked, so nothing here has ever been seen

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
- [ ] It does **not** explode where it was intercepted. `ShotDown` is not `Detonated`, and a
      fireball there means something is reading the two as one.
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

- [ ] The rail appears in the editor under Structural, and **surface-attaches** to the side of a
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

- [ ] Two rails on one craft: expected to fire **one**. `LauncherOrdinal` is pinned and the roster
      crews one battery per craft. Recorded so it is not mistaken for a bug.

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
- [ ] The log line for the release, and the whole `KSArmory.log`.

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
- [ ] **Teams and IFF** - declare an own team and a hostile one, confirm the track list marks
      F / N / H / ? correctly and that a friendly is not engaged. Name teams so that no team name
      is a substring of another craft's name.
- [ ] **Warp overrun** - warp hard enough to exceed 0.32 s of simulated time per frame and confirm
      the log warns how much time was discarded. That warning is a diagnostic, not a fix.
- [ ] **Stock drones** — Gemini7 / Hunter / Banjo / Polaris / Rocket each spawn and fly.
- [ ] **Battery stays put** — fly a second craft; the battery remains on the launcher and the
      panel says so.
- [ ] **Two launchers on one craft** — still one battery of twelve, no double-firing.
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

**Never flown.** The head is no longer launcher gear: it is a part anything can carry, it finds
its own targets through its own sensor, and it drives the view with no weapon involved. A Pantsir
that has not been given one has no sight at all, which is the intended state and not a fault.

Everything in 7.6 below was flown against a head bolted to the Pantsir's turret. The maths it
proved still holds — the same `PointingDrive`, the same in-phase resolve, the same zoom — but the
thing it was proved on no longer exists, so the items are worth re-running rather than trusted.

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
- [ ] A **Pantsir with no director** reports no sight rather than a broken one.
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

### 7.6d The suspension rail — carriage gear, and whether things mount on it

**Never flown.** Authored, clean under `checkmesh`, and declared with the collider read off its
own `_ColPrim_` node. It has no profile in `Sim/Arsenal.cs` at all: it neither shoots nor sees.

- [ ] `14-inch Suspension Rail` appears under **Weapons** in the editor and surface-attaches to a
      wing or fuselage, hooks downward.
- [ ] It renders and is textured. Its atlas and material are its own; a magenta or untextured rail
      means the Ids, not the mesh.
- [ ] **A pod or a rack can be mounted on it.** This is the item worth the most: it carries no
      `Radial` tag on purpose, because that tag is a `FaceSnapTargetBlacklist` and the blacklist
      beats the `Weapons` whitelist — the same trap that stopped anything being mounted on the
      Pantsir. If nothing will attach to the rail, the gate has moved and
      `docs/KSA-MODDING-NOTES.md` wants correcting.
- [ ] It cannot start a craft, which is right: any `ToSurface` connector bars a part from being a
      root whatever its tags say.
- [ ] The store sits **on the hooks** rather than intersecting the beam. The rail's hooks are at
      14-inch spacing and the pod's lugs are modelled to match, but nothing checks that the two
      line up — the editor places the store wherever the player drops it.

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
asked for again there through `IViewPose`. While the head is settled it is tracking, so the view
is re-solved onto the target's own position at that instant; while it is still slewing the head's
own axis is used, because a target sliding towards the middle is what slewing looks like.

- [x] Paused, at 1×, and at high warp: the cross stays on the target at all three. **Confirmed in
      flight** — both centring faults are closed.
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
- [ ] Master arm, missile count and belt count in the top-left track the panel.
- [ ] **Sight symbology** off leaves the target bracket and takes everything else away.

---

## 12. The ballistic computer — flown, and arriving

Six warheads on the ground **433 m to 1.7 km** from the aim, on a 2,740 km deorbit onto a target
4 km up in the Andes. It got there from 59 km in five flights, and every step was a difference
between what the prediction modelled and what the round actually did:

| | miss |
| --- | --- |
| a prediction flown in vacuum | 59 km |
| + the warhead's drag | 17-20 km |
| + terrain sampled the way the round samples it | 14 km |
| + the 2 m/s the warhead gets off its tube | ~7 km |
| + the burn stopped along the line it is thrusting | **0.4-1.7 km** |

What is left is the **tube cant**: the six tubes sit at 6° in different clock positions, so each
warhead leaves on a slightly different vector and the aim correction can only remove the common
mode. That spread is physical, and closing it means aiming each round separately rather than
correcting one arc.

`docs/ICBM-GUIDANCE.md` has the algorithm and the list of what a test cannot reach; this is the
order to check it in, easiest failure first. Ticks below are what a flight actually showed, with
the number it showed.

**Last flight, target in the Andes at ~4 km, from orbit:** coast at 199 km, 0 m/s left to gain,
engines stopped 1.1 m/s short of the solution, own prediction 9.7 km off (it was 60.4 km before the
predictor was given the real height field), six warheads released on their own, all six down
52.4–53.1 km from the aim on the same bearing.

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

### 12.7 It aims each tube, and lets go of its stack

Separation, the handover and the deployment are flown. No shipped part declares a decoupler, so it
needs a craft built with a stock 3 m decoupler between the launcher and the stack below it.

- [ ] With **Aim each tube before it fires** on and no decoupler fitted: the warheads still all go,
      and the log says the tubes are being turned onto the line, or that it gave up and why.
- [ ] The six land closer together than the ~1,200 m they spread over without it.
- [ ] With it off, behaviour is exactly as before — released as soon as the tubes stop sweeping.
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

What is left is two terms. The ~1 km *spread* is the tube cant, which is what re-pointing is for and
why §12.7a matters. The ~900 m *bias* is that every round landed beyond its own release probe —
`docs/MIRV-NEXT.md` item 2.

**The trim itself is flown and working.**

- [x] The panel and log show `trimming N m/s on the tail` with `thrusters measured at N m/s2`
      beside it. Measured **0.9-2.2 m/s2**, so KSA's translation flags do reach the bus's nozzles.
- [x] It settles rather than hunting: `trimming 1.23 m/s` → `trimmed to 0.010 m/s` in 1.8 s.
- [x] Nothing leaves the bus until it has. Split at `00:05:59.352`, first `round 1 away` at
      `00:06:02.002`.
- [x] Timewarp is held down through the trim as well as the burn.

**What is new since that flight, and unflown.** The trim now waits to coast clear of the spent stack
before firing, because nulling the decoupler's shove *is* nulling the separation — last flight the
bus trimmed 130 ms after the split at 12 m and then sat against the booster.

- [ ] After the split the log reads `waiting to clear the spent stack, N m of 50`, then
      `clear of the spent stack at N m after N s`, and only then the trim.
- [ ] **Write down the standoff and how long it took.** 50 m and the 90 s cap are both guesses; one
      flight replaces them with a number.
- [ ] The bus visibly drifts off the booster rather than sitting on it, and the release window still
      has room for the wait — the sequencer must not start timing out on `SecondsLeftToDeploy`.
- [ ] A shot with no decoupler fitted skips the wait entirely and trims within a second or two.
- [ ] The view follows onto the separated craft, and only when it was watching the stack.
      `KsaWorld.GoTo` no longer asks the engine to rebuild a part tree nobody changed, which is what
      it was refusing; expect `view moved to <name>` rather than `could not go to <name>`.
- [ ] If the flags reach nothing, the log says `nothing left aboard moves the bus` with the residual
      and the warheads go anyway. Warheads still aboard when the release altitude closes is the one
      failure this must not have.
- [ ] With **Trim the bus before releasing** off, behaviour is as it was: released as soon as the
      tubes stop sweeping, with the shove still in.

**Never yet exercised:** whether the bus's ~183 kg of MMH/NTO lasts. Nothing has spent it.

### 12.7a Turn re-pointing back on and read one line

The ~1 km spread in the flown group **is** the tube cant, so this is now the largest thing left to
win. It is off by default because it made the bus hunt; the sequencer has not been made to work, it
has been made to say which way it is failing. One flight decides where the fix goes.

- [ ] Tick **Aim each tube before it fires** and fly the same shot. Read the deploy lines.
- [ ] **Which of these two appears is the whole result:**
      `tube 1 is not following the turn, X deg off the line against Y when it started` — the bus
      accepted the command and is being pushed off it, so the fix is on the craft (more RCS) or is a
      documented limit; or `tube 1 has stopped closing on the line` — nothing is moving at all, so it
      is authority, or the attitude write is not reaching the bus at all.
- [ ] If it says *stopped closing* with no movement whatever, check `AttitudeHook.Hold` is landing on
      the bus rather than on the discarded stack — the handover re-homes `Craft`, but that was read
      rather than observed.
- [ ] The salvo takes about one tube's timeout, not three minutes, whatever else goes wrong.
- [ ] Every release logs which tube went, how far off the line and how fast the tubes were sweeping
      — including the ones that worked. Six impacts are only diagnosable against six release states.
- [ ] If the bus holds a *steady* offset it cannot improve on, write the number down: that is the
      evidence for making `AlignedDegrees` a convergence test rather than an absolute one, which is
      the one gate change worth making and cannot be justified without it.

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

### 12.6 It gives the vehicle back

- [ ] **Abort** stops the engines and returns attitude control. Flying by hand works immediately
      afterwards.
- [ ] Disarming mid-flight does the same.
- [ ] Destroying the craft mid-flight does not throw, and nothing in the log complains afterwards.

### 12.7 Timewarp

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

## Reporting back

Most useful, in order:

1. `Logs/KSArmory.log` — the whole file. Especially `ERROR` lines with stack traces.
   `Logs/KittenSpaceAgency.log` too if the part or XML is misbehaving.
2. Which checklist item failed and what you saw instead.
3. A screenshot for anything visual (2.2 especially).
4. For guidance misses: the fuse trigger ranges off the `detonated` lines and the closest
   approaches off the `expired` ones, plus whether it lagged behind or overshot.
