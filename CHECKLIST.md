# Test checklist

## Status — 2026-08-02: the system works end to end in-game

Confirmed working: the part loads and renders from Core's meshes, launches standalone, radar
searches and classifies threats, the launcher slews and fires salvos, proportional navigation
**intercepts at 22–23 m**, the proximity fuse detonates, the blast destroys the target, and the
overlay draws correctly on the craft.

Bugs found and fixed along the way, all worth knowing about (see
`docs/KSA-MODDING-NOTES.md`):

| Bug | Cause |
| --- | --- |
| Radar saw nothing | `Program.VehiclesInFrame` reads empty from a StarMap hook |
| Drones spawned at the solar barycentre | `CreateVehicle` needs `parent.Children.Add` + `AddToTask` |
| Nothing ever rendered | gizmos submitted after the frame's render, wiped by the next reset |
| Rounds flew 84 km, lock broken instantly | absolute Ecl velocity used as airspeed and heading |
| Overlay drawn 500 m off the craft | Ecl positions differenced one frame apart, at 29.8 km/s |
| Detonations killed nothing | blast compared end-of-frame burst against frame-start positions |

Known wart: the **miss distance slider on test targets is nominal, not achieved**. The ballistic
solve is a vacuum solution and KSA models atmosphere, so drones undershoot — a requested 1500 m
pass arrives at roughly 4000 m.

Remaining untested: sections 5 (safety) and 6 (robustness) below.

## Status — the Pantsir model renders in-game

The launcher is no longer a 0.9 m tube bundle borrowing Core's meshes. It is a full Pantsir-S1:
an 8×8 vehicle 8 m long and 5.6 m tall, **12 rounds instead of 6**, built from
`tools/model/pantsir.py` and shipping **its own mesh atlas and textures**.

**Confirmed 2026-08-02.** The vehicle renders correctly and is recognisable. That settles the
biggest open question in this file: **a user mod can ship its own `<MeshAtlas>` and
`<PbrMaterial>`, with PNG textures, resolved relative to the mod root.** The tube markers land
on the container mouths, so the generated launch geometry agrees with the mesh in-game and not
just in `validate-parts.py`.

One defect found and fixed: the whole vehicle **flickered with white speckle** — z-fighting
between coplanar faces where boxes abutted on a shared plane. `box()` now inflates every
primitive by 8 mm. **Blender's preview render does not reproduce this**, so it is game-only.
Needs a re-check after redeploying.

Still untested after the model change:

- **Mass and size** — 30 t and 8 m, versus 185 kg and 0.9 m. Colliders, attachment, pad physics.
- **Round count** — `12/12` in the panel, twelve markers, a full twelve-round salvo.

Everything about guidance, radar, fuse and the draw anchor is untouched and stays ticked.

---

The order below is deliberate: **riskiest unknowns first**. If something fails, the *If it
fails* line says what it means and where to look.

---

## Where I expect trouble

Ranked by how likely I think a failure is. Worth reading before you start.

| Risk | What might break | Covered by |
| --- | --- | --- |
| ~~High~~ | ~~The mod's own `<MeshAtlas>` / `<PbrMaterial>` paths don't resolve~~ — **settled, it renders.** | [2.2](#22-the-part-renders) |
| **Medium** | 30 t and 8 m of part where there used to be 185 kg and 0.9 m: colliders, editor attachment, pad physics. | [2.3](#23-the-part-attaches), [2.4](#24-the-part-behaves-physically) |
| **Medium** | `mod.toml` serving as both content manifest and StarMap manifest. Plausible, untested. | [1.1](#11-the-mod-loads) |
| **Medium** | `Program.VehiclesInFrame` may not contain what I assume, so radar sees nothing. | [3.3](#33-radar-sees-a-target) |
| **Medium** | Boresight is local "up" derived from the parent body; if `Vehicle.Parent` misbehaves the cone points somewhere daft. | [3.2](#32-the-search-cone-is-drawn) |
| **Low** | Guidance and fuse maths — covered by 13 headless tests, but never against real KSA motion. | [4.3](#43-a-crossing-target-is-intercepted) |
| **Low** | `DestroyVehicleFromEvent` may behave oddly with `Cause = Collision`. | [4.4](#44-the-warhead-kills) |

---

## 0. Before the game

Use the wrapper scripts, not bare `dotnet`. The mod targets **net10.0** and your system SDK is
8.x, which cannot build it — the scripts put the right SDK on PATH for you.

- [x] **0.1** `./tools/sync-import.sh` — Import/ populated, no errors.
- [x] **0.2** `./tools/build.sh` — succeeds.
- [x] **0.3** `./tools/test.sh` — 13 passed.
- [x] **0.4** `./tools/validate-parts.py` — "OK: 26 asset reference(s) resolve".
- [ ] **0.5** `./tools/deploy.sh` — prints an install path containing `AirDefence.dll`,
      `mod.toml`, **two XML files at the root**, `Meshes/AirDefence_MeshAtlas.glb` and three
      PNGs under `Textures/`. If an `Assets/` folder is still there from an older deploy, the
      script should have deleted it — two copies of the part would fight over one Id.
- [ ] **0.6** Re-run `./tools/deploy.sh` — it must now say it **registered the mod in
      manifest.toml**. Your first run predates that step, so the entry is missing.
- [ ] **0.7** `./tools/setup-starmap.sh` — installs StarMap and writes `StarMapConfig.json`.
      One-off; skip if already done.

> Dropping the folder into `mods/` is **not** enough. KSA discovers mods through
> `Documents/My Games/Kitten Space Agency/manifest.toml`, and StarMap walks the same list —
> without an `[[mods]] id = "AirDefence"` entry, nothing loads.

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
tail -F "/mnt/c/Users/devoo/Documents/My Games/Kitten Space Agency/Logs/AirDefence.log"
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
- [x] `KittenSpaceAgency.log` contains `INFO found mod 'AirDefence'`.
- [x] StarMap prints `Loaded mod: AirDefence from manifest`.
- [x] `Logs/AirDefence.log` contains `loading (mod id: AirDefence)`.
- [ ] Then `ready - 12 tubes, safe.` (was 6 before the Pantsir model landed)

**Passed 2026-08-02.** Both StarMap hooks fire. Note `[StarMapAllModsLoaded]` lands about
**21 s** after `[StarMapImmediateLoad]` — it waits for the game to finish loading, so the
`ready` line appearing late is normal, not a hang.

**If it fails:** no `AirDefence.log` at all means StarMap never ran our entry class. Check
`mod.toml`'s `EntryAssembly = "AirDefence"` matches the DLL name, and that StarMapConfig.json
points at the right KSA folder. An exception mentioning TOML means the `assets` array in
`mod.toml` isn't tolerated there; move the part XML into a second mod folder and keep
`mod.toml` StarMap-only.

### 1.2 No XML parse errors

- [x] Nothing in `KittenSpaceAgency.log` about failing to load `AirDefenceAssets.xml` or
      `AirDefenceGameData.xml`.

**Passed 2026-08-02.** Re-check after any XML edit.

**If it fails:** the schema differs from what I inferred from Core's files. Compare against
`Content/Core/CoreStructuralAAssets.xml`, which is the file I modelled it on.

---

## 2. The part

### 2.1 The part appears in the editor

- [x] Open the vehicle editor.
- [x] Under **Structural**, find **Pantsir-S1 Point Defence System**. (Renamed from "AA-6
      Point Defence Launcher".)

**If it fails:** the `PartGameData` didn't register. Check the `EditorTag Value="Structural"`
and that `mod.toml`'s `assets` paths match where deploy.sh put the files — they are now at the
mod root, **not** under `Assets/`.

### 2.2 The part renders

**This is the highest-risk item in the whole list, and it is untested again.** The mod used to
ship no art at all and borrow Core's meshes by Id, which worked. It now carries its own mesh
atlas and textures, which is a different loader path and has never been exercised.

- [x] The part is a **green 8×8 military truck**: four axles, a cab at the front, and a turret
      at the back carrying two pods of six missile tubes elevated to about 55°.
- [x] Two thin gun barrels point forward from the turret, above the cab roof line.
- [x] A large pale grey panel (the search radar) stands at the very back, leaning aft.
- [x] A pale grey array faces forward at the front of the turret, with a small dome beside it.
- [x] Colours are right: olive green body, near-black tyres, grey radar faces, dark glazing.
- [ ] **No flickering speckle** on flat surfaces — cab roof, hood, deck, turret sides. That was
      z-fighting from coplanar faces; `box()` now inflates every primitive by 8 mm. Blender's
      preview render does not reproduce it, so the game is the only place it can be checked.
- [ ] Rough size: **8 m long, 3 m wide, 5.6 m to the tube mouths.** It is a big part now — it
      used to be 0.9 m across.

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

- [ ] Craft mass increases by **~30 t** per launcher (it is a whole vehicle now, not a pod).
- [ ] It doesn't clip weirdly or fall through the launchpad. The hull collider starts at the
      ground plane, so it should rest on its wheels rather than sink to the axles.
- [ ] Craft launches without the part exploding or detaching.
- [ ] Thirty tonnes on a small stack does not fold the craft in half on the pad.

---

## 3. Panel and detection

### 3.1 The panel appears

- [x] In flight, an **Air Defence** window is visible.
- [x] Closing it leaves a small **Air Defence** button that reopens it.
- [ ] Header shows `Platform: <your craft>` and `Launcher: Pantsir-S1 fitted` in green.
- [ ] Shows `MASTER ARM: SAFE` in green and `Rounds: 12/12`.

**If it fails:** `Launcher: none fitted` while the part *is* on the craft means
`LauncherPart.Find` isn't matching — `Part.Id` may not equal the `PartGameData` Id. Untick
**Require launcher part** to keep testing everything else, and tell me.

### 3.1b The turret slews

**The open question is whether KSA honours a runtime transform write at all.** Everything else
here is instrumented so the answer is readable either way.

- [ ] `AirDefence.log` has a `launcher subparts: ...` line naming both subparts. If the turret
      one is missing or oddly named, `Part.ResolveRuntimeId` rewrote it and `TurretMarker` in
      `LauncherPart.cs` needs to match whatever is actually there.
- [ ] The panel shows `Turret: N deg` and not `subpart not found` or `engine refused`.
- [ ] A **cyan line** points out from above the vehicle. That is where the drive *thinks* it is
      aimed — it comes from our own maths, not from the engine.
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
`AirDefence_Launcher_Turret`.

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

**Use the built-in spawner.** The panel has a **Test targets** section: set time-to-pass,
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
first — that rules out geometry before we blame the API.

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
- [ ] `Rounds` drops to 5/6; one muzzle dot turns grey.
- [ ] A tracer leaves that tube with a trail behind it.
- [ ] Console logs `[AirDefence] round N away at <target> (X.X km)`.

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
- [ ] Console logs `round N detonated, miss distance X m` with X under ~25 m.

**If it fails:** consistently missing behind means the lead isn't working; raise *Nav constant*
and *Max lateral g* and see if it improves. Report the miss distances — the headless tests pass,
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
- [ ] After 6 rounds, `Rounds: 0/6` and a reload progress bar appears.
- [ ] After *Reload time* (12 s), back to 6/6 and `launcher reloaded`.

### 4.6 Manual designation

- [ ] With several tracks, press **designate** on a non-priority one.
- [ ] Lock switches to it and stays there.
- [ ] **Clear designation** returns to automatic priority.

---

## 5. Safety

Do these deliberately — a failure here is the kind that ruins a save.

- [ ] **5.1** With **Never target the vehicle I'm flying** ticked, it never locks or fires on
      your own craft.
- [ ] **5.2** A round fired at a close target does not destroy your own launcher platform
      (the fuse arms 0.6 s after launch specifically to prevent this).
- [ ] **5.3** **Safe all** removes rounds in flight with no detonation.
- [ ] **5.4** Master arm off means nothing launches, even with a valid lock and auto-engage on.

---

## 6. Robustness

Where I'd expect latent bugs.

- [ ] **6.1** **Timewarp** — raise sim speed with rounds in flight. Nothing should NaN, crash,
      or spam the log. Rounds behaving oddly under warp is acceptable; a crash isn't.
- [ ] **6.2** **Scene change** — go flight → editor → flight. Panel recovers, no exceptions.
- [ ] **6.2b** **Camera switching mid-engagement** — fire a salvo, then switch the camera to the
      drone and back. The cone, track markers and tracers must stay locked to the craft and to
      each other. *(Regression: `GetPositionEgo` takes a different branch depending on what the
      camera follows, which used to shift the whole overlay and make hits look like misses. The
      log is the arbiter — miss distances stay ~22 m either way.)*
- [ ] **6.3** **Target dies mid-flight** — destroy the target another way while rounds chase it.
      They should lose lock and expire, not throw.
- [ ] **6.4** **Platform destroyed** with rounds in flight — no exception spam.
- [ ] **6.5** **Pin platform** — press *Pin to this vehicle*, switch control elsewhere. The
      pinned craft keeps defending itself.
- [ ] **6.6** **Staging away the launcher** — `Launcher: none fitted` appears, firing refuses.
- [ ] **6.7** **Two launchers** on one craft — should work, still one battery of 6 (by design).
- [ ] **6.8** **Long session** — leave auto-engage on for a while. No unbounded log growth, no
      frame-rate decay.
- [ ] **6.9** **Fault handling** — if anything throws, the console shows
      `[AirDefence] ERROR … (n/10)`. After 10 it disables itself rather than spamming. If you
      see this, **copy those lines** — that's the most useful thing you can send me.

---

## 7. Gaps — never exercised at all

Added after the system was working. These are not "probably fine", they are "never once run".

### 7.1 Save / load with the part fitted  ← highest risk

- [ ] Build a craft with the Pantsir, save the game, quit to menu, reload. Craft intact, part present.
- [ ] Save *while rounds are in flight*, reload. No exception; rounds simply gone is fine.
- [ ] A save made with the mod active still loads with the mod **removed** — or fails cleanly.

**Why it matters:** the part goes into the save's part tree. If KSA cannot resolve
`AirDefence_Prefab_Launcher6` on load, the craft — or the whole save — may fail. Nobody has
tried this once, and it is the only failure here that could cost you work.

### 7.2 More than one target at a time

- [ ] Spawn three drones close together. Radar lists all three, ranked by time-to-CPA.
- [ ] It commits `RoundsPerTarget` to the top threat, then moves to the next — not all twelve at one.
- [ ] Killing the lock target promotes the next one without a stall.

**Why:** every engagement so far has been one drone. Track prioritisation, round attribution and
salvo allocation have literally never run against a contested list.

**The arithmetic is now covered.** `ThreatModelTests` ranks contested lists, checks non-threats
never outrank threats, and walks twelve tubes across three targets to prove the first does not
take the whole magazine — 19 tests, headless. That became possible by moving the logic out of
`Ksa/Radar.cs` and `Ksa/DefenceBattery.cs` into `Sim/ThreatModel.cs`.

It does **not** retire these boxes. The tests prove the maths; they say nothing about KSA
handing us the vehicles we expect, tracks surviving a rebuild, or the lock promoting cleanly
when a target dies mid-engagement. Run them still — expect fewer surprises.

### 7.3 Blast catching a third party

- [ ] Put two drones close together, kill one, confirm the other reports `near miss` or dies too.

**Why:** the splash path (as opposed to the intended-target path) has never destroyed anything.

### 7.4 Recently changed, unproven

- [ ] **Stock drones** — Gemini7 / Hunter / Banjo / Polaris / Rocket each spawn and fly.
- [ ] **Battery stays put** — fly a second craft; the battery remains on the launcher and the
      panel says so.
- [ ] **Two launchers on one craft** — still one battery of twelve, no double-firing.
- [ ] **Reload cycle** — let it run dry and auto-reload rather than reloading by hand.
- [ ] **Manual designation** — designate a non-priority track and confirm it is engaged.

### 7.5 Known-missing behaviour, worth confirming is *survivable*

- [ ] A round fired at a low target passes through terrain rather than hitting it. Expected —
      rounds only test against their target — but confirm it does not throw.
- [ ] Timewarp during flight: rounds may behave oddly; a crash would not be acceptable.

---

## Reporting back

Most useful, in order:

1. `Logs/AirDefence.log` — the whole file. Especially `ERROR` lines with stack traces.
   `Logs/KittenSpaceAgency.log` too if the part or XML is misbehaving.
2. Which checklist item failed and what you saw instead.
3. A screenshot for anything visual (2.2 especially).
4. For guidance misses: the `miss distance` values, plus whether it lagged behind or overshot.
