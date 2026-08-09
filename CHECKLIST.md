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

The failure modes worth recognising before starting, and how to tell them apart, are in
`docs/KSA-MODDING-NOTES.md` and `docs/FRAMES-AND-EPOCHS.md`.

Known wart: the **miss distance slider on test targets is nominal, not achieved**. The ballistic
solve is a vacuum solution and KSA models atmosphere, so drones undershoot: a requested 1500 m
pass arrives at roughly 4000 m.

Remaining untested: sections 5 (safety) and 6 (robustness) below.

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
- [ ] **6.2b** **Camera switching mid-engagement** — fire a salvo, then switch the camera to the
      drone and back. The cone, track markers and tracers must stay locked to the craft and to
      each other. *(`GetPositionEgo` takes a different branch depending on what the camera
      follows, so a mismatch there shifts the whole overlay and makes hits look like misses. The
      log is the arbiter — miss distances stay ~22 m either way.)*
- [ ] **6.3** **Target dies mid-flight** — destroy the target another way while rounds chase it.
      They should lose lock and expire, not throw.
- [ ] **6.4** **Platform destroyed** with rounds in flight — no exception spam.
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
- [ ] A save made with the mod active still loads with the mod **removed** — or fails cleanly.

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
untested. They are under *Tuning → Radar*.

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
- [x] **Timewarp during flight.** Fire control steps on `Universe.GetElapsedSimTime()` via
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

### 7.6 The gunner's sight — symbology, zoom and the two reticules

Never flown. The maths is covered by `SightZoomTests` and `SightPictureTests`, and the maths is
not what is in doubt: every item below is a question about whether KSA honours a write or draws
what the mod thinks it drew.

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

## Reporting back

Most useful, in order:

1. `Logs/KSArmory.log` — the whole file. Especially `ERROR` lines with stack traces.
   `Logs/KittenSpaceAgency.log` too if the part or XML is misbehaving.
2. Which checklist item failed and what you saw instead.
3. A screenshot for anything visual (2.2 especially).
4. For guidance misses: the `miss distance` values, plus whether it lagged behind or overshot.
