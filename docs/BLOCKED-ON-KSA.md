# Blocked on KSA

Things this mod wants and **cannot build**, because the game does not expose what they need. Not a
backlog — `CLAUDE.md`'s *Not done* section is the backlog, and everything in it is this mod's own
work. Everything here waits on RocketWerkz.

Each entry cites what the decompiled corpus says, so a claim can be rechecked after a KSA update
rather than taken on trust. **Recheck this file when the game moves** — the whole point of it is
that some of these will quietly become possible.

Findings are against KSA **2026.10.25.5707**, and their citations still are; 2026.11.10.5801 has not
been rechecked against them yet, and could not be flown (StarMap 0.5.2 does not start on it). Paths are relative to
`../ksa-game-assemblies/current/src`. A line number is where the member was on the build its entry was
written against, and some have drifted since; search for the member it names.

## Recheck after a KSA update

Tick when rechecked against the new build, then untick for the next one. `tools/ksa-api-diff.sh`
will not surface any of these — none is a signature change, and most are a call that does not
happen rather than a member that moved.

- [x] Secondary viewport gets the planet, atmosphere and lighting passes — **partly, in
  2026.10.7.5541**: terrain and sky, not clouds, ocean or clustered lights; **clouds too in
  2026.10.25.5707** (`ViewportOptionFlags.RenderClouds`); see the entry below
- [x] A secondary viewport's terrain is shadowed by cascades fitted to its own camera, not the main one's
  — **partly, in 2026.10.13.5602**: not shadowed at all unless `PlanetRenderer.ViewportShadows` is set,
  which is the picture the entry below asked for; see it
- [x] `Camera.NearbyCelestial` is set per camera rather than only for the frame viewport — **partly,
  in 2026.10.7.5541**: the field is still the frame camera's, but `Camera.GetEffectiveNearbyCelestial`
  resolves one for any camera, and the planet and atmosphere passes read that
- [x] Wheel, suspension or steering module exists
- [x] Custom part modules can be registered without patching
- [x] Public accessor for the volumetric trail renderer
- [x] **A hook that hands a mod the frame's command buffer** — the day it exists, `Ksa/CloudPassHook.cs`'s
  prefix on `SunbloomRenderer.Render` can go
- [x] **A hook between applying the vehicle solvers and snapshotting them** — delete `Ksa/AttitudeHook.cs`'s patch the day this exists
- [x] **A menu a mod can add a submenu to** — StarMap's Mods menu takes a button only; the `BeginMainMenuBar` append in `Ui.DrawMenuBarEntry` goes the day it takes more
- [x] **`DistanceReference.IsValid()` stops requiring 100 km** — go back to `IsValid()` on the atmosphere and the ocean the day it does
- [ ] **`PhysicsStates.ComputeDrag` is still a body-fixed drag box over the mass, with no lift, no
  Mach term and no restoring torque** — RocketWerkz are working on aerodynamics. The day this
  changes, the gun's lead is leading on yesterday's physics; see the entry below. And
  `IcbmConfig.FlyAnyRange` lets a short shot steer at any angle of attack because nothing here can
  tip a stack over: an aerodynamic moment would make that a way to lose the rocket
- [ ] **`RecomputeImmersion` rotates `MassToGeometryAsmb` into the planet's frame**, or buoyancy
  gets a centre of its own; see *A floating craft's waterline depends on where it is* below
- [ ] **A kitten can walk on a vehicle** — `IsGroundSurfaceFor` accepts something other than a
  static handle; see *Walking on a craft* below
- [ ] **KSA bursts a tank on `RatedPressure`, or gives a liquid a pressure** — the rated pressure and stored
  gas arrived in 2026.10.25.5707 and gas venting is built; for a liquid the mod would rather read the game's
  bar values than its own 0.2 bar leak and 2–4 bar ram prestress. See *A tank's pressure and its walls* below
- [ ] **KSA's destroy runs warm the first time it is called** -- its assemblies compiled ahead
  (ReadyToRun), or a debris split that no longer does first-time work -- **delete
  `JitWarmup.EngineTypes` the day it does**; see the entry below

**Rechecked against 2026.10.25.5707: two entries moved, the rest is still blocked.** Tanks now carry a
rated pressure and hold gas, and a camera window now draws clouds (`ViewportOptionFlags.RenderClouds`,
`Program.cs:1043`), still with no ocean or clustered lights. `PhysicsStates.ComputeDrag` and `DistanceReference`
are unchanged, `IsGroundSurfaceFor` and the buoyancy's
`MassToGeometryAsmb` did not move, `KSA.dll` is still IL-only, the volumetric trail renderer is still reached
by reflection, StarMap did not move, and there is still no hook between applying the vehicle solvers and
snapshotting them. Citations were re-derived only for what this paragraph cites; elsewhere they stand as below.

Earlier builds' recheck notes are in this file's git history. Where an entry gives no build for a
citation, it was written against **2026.8.22.5348** and only its claim, not its line numbers, has been
rechecked since: 2026.9.4.5400 replaced `Viewport` with `IViewport` / `ViewportBase` / `GameViewport` and
moved most of the render path.

---

## A tank's pressure and its walls

**Arrived in 2026.10.25.5707, and taken up.** `AsmbTankTemplate` derives a `RatedPressure` from the
wall material's yield stress over a safety factor of two and thin-wall hoop and dome-crown formulas
(`AsmbTankTemplate.cs`, `TankGeometry.PressurePerWallStress`), and `Tank.RatedPressure` carries it
(`Tank.cs:80`). `Tank.CanHold` (`:788`) takes a gas only into a rated tank, as a single-reactant mix, and a
gas mole's `ContainerPressure` and `GetPressure(mass)` (`Mole.cs:15`, `:85`) give its pressure. Liquids are
still unpressurised, and KSA itself bursts nothing on `RatedPressure` yet. `Ksa/Leaks.cs`'s write path still
mirrors `Vehicle.DepleteConsumables`, and since this build a holed gas tank vents by that pressure
(`TankLeak.GasFlowKgPerSecond`).

**What the mod does with it** (`Sim/TankWall.cs`, flown 2026-10-30). A dart or a fragment holes a tank only
with more energy than shearing a plug its own size out of what is left of the wall there; a dart that stops
dents it (`Sim/WallCraters.cs`), so hits on one spot add up, and a burst holes it regardless. A holed gas
tank bursts where its pressure beats the hoop stress a through-crack of the opening's radius stands, read
off the wall's thickness and yield against `Tank.RatedPressure`, with one aluminium-like toughness since KSA
declares none. Fragments still make no holes, so only a shell's opening can burst a tank.

**What is still a guess.** A liquid has no pressure in KSA, so `Sim/TankLeak.cs` holds every liquid tank at
its own 0.2 bar, low enough that the depth of a hole and the pull the craft feels still decide a leak, and a
liquid tank going up is still a flat one fire in five (`PartFire.FlashoverChance`). A part's toughness
against a hit stays its mass to the two thirds (`Sim/PartToughness.cs`); the wall decides only whether a hit
gets through. KSA itself bursts nothing on `RatedPressure`.

**Waiting on KSA: a liquid tank's pressure.** Because a liquid has none in KSA, the mod invents two: the
leak's 0.2 bar above, and a flight tank's 2–4 bar of operating pressure for the planned hydrodynamic ram's
prestress (`docs/HYDRODYNAMIC-RAM.md`) — the load in the skin that lets a round's push through the liquid
split a tank. **We would rather model the actual bar values the game gives**: the pressure a tank is run at,
and how it changes as the tank drains or is topped up. Both of the mod's numbers go the day KSA has them.

## The first kill of a session pays for KSA's destroy running cold

**What it costs:** the frame a session's first craft is destroyed. On the Mk 42, 2026.9.22.5482,
`Universe.DestroyVehicleFromEvent` took **28 ms** the first time and 4-11 ms after, nearly all of it
`PartFailure.ShedDebris` building up to twelve debris pieces as vehicles. The mod's own detonation on
the same frame was 12.8 ms cold.

**What the mod does about it.** `Ksa/JitWarmup.cs` compiles the path on a thread of its own at load:
this mod's assembly, which takes its detonation to 2.6 ms, and `EngineTypes`, the engine's half,
which takes the destroy to 23. The rest is not compilation -- with all of `KSA.dll` compiled it stayed
at **21.9 ms** -- so it is first-time work inside the debris split that only the engine can do earlier
or cheaper. The frame went from 44-45 ms to 33-34, flown twice with `gunnery:2`.

**Re-measured on 2026.10.25.5707, still worth keeping.** The engine has been built with the JIT optimiser
on since 2026.10.19.5638, but `KSA.dll` is still IL-only. Flown once each with `gunnery:2`, `fire` worst on
the first kill against the second: **31.1 ms against 5.6** with `EngineTypes`, **46.8 against 5.2** without.

**When to delete `EngineTypes`.** When KSA ships its assemblies compiled ahead, or its first destroy
costs what a later one does. The check: comment out the `EngineTypes` loop, fly
`KSARMORY_SCENARIO_VERBOSE=1 ./tools/scenario.sh gunnery:2`, and read the `fire` worst on the first
kill's `mod frame:` line against the second kill's. Within a few milliseconds of each other, the list
is buying nothing and goes. The mod's own assembly stays warmed either way: that half is this mod's
code, and no KSA change touches it.

## Aerodynamics are being worked on, and the gun's lead is a copy of today's

**Not blocked — the opposite.** Nothing here is missing. It is recorded because the author has it
from RocketWerkz that aerodynamics are being worked on (2026-09-14), and this mod copies the
current model by hand in a place no tool will flag when it changes.

**What is copied.** `PhysicsStates.ComputeDrag` and `BoundingBoxCdA.ComputeCdA`, as of
2026.9.7.5402: drag = `(ComputeCdA(airflow in the body frame) + 0.1 × TotalSurfaceArea) × ½ρv²`,
against the mass, then an implicit damping factor `1 / (1 + F·dt / (m·v))` that is negligible at
these speeds and is not copied. The box is `VehicleProperties.AerodynamicCdABody`: each face's area
weighted 0.3 on +X, 1.0 on −X and 1.2 on the sides, by how squarely the air meets it. There is **no
Mach term, no lift and no restoring torque** — only a rate-damping one, `0.1 × TotalSurfaceArea`
against the body rates, which a lead on the centre of mass does not need. `Sim/DragShape.cs` is the copy and
`KsaWorld.DragShapeOf` reads the terms off a craft; `Sim/BallisticLead.cs` flies a target on it.

**How it was confirmed, so the check can be repeated.** `scenario.sh gunnery:1,overhead,30,300,1500`
logs every half second a drone's slowing times its mass over `v² × density ratio × area`. On today's
engine that held at **0.6125 — ½ × 1.225 — to four places** while the drag area changed by 20%, and
with `,20` appended (a 20°/s tumble) the area predicted half a second ahead matched the engine's
within 0.1 m². If either stops holding after an update, the copy is out of date.

**What else the lead assumes, which aerodynamics would also break.**
- Whatever accelerates a target besides gravity and this drag is its **engine**, held fixed to the
  body and scaled up as its mass burns away (`DragShape.ExhaustVelocity`, `PropellantMass`). Lift
  would be read as thrust: the right size at the shot, then wrong, because lift follows the airflow
  and the airspeed rather than the body and the mass. Flown with `,0,burn` appended: holding the
  thrust put every burst 19.5 m and further under a burning drone, and burning the mass away put
  the first shell on it.
- A tumbling craft keeps its **body rates** for the whole of a shell's flight. True while nothing
  aerodynamic torques it; an aerodynamic moment that weathervanes a craft into the wind would turn
  it away from the predicted attitude within seconds.
- A **round's** drag is `Medium.Drag`, a coefficient on airspeed squared with no Mach term. That is
  this mod's own model rather than the engine's, and a real shell's drag changes a lot through the
  speed of sound.

**What to do when it lands**, cheapest first: re-run the two logged checks above; if the new drag is
still a function of the body, the airflow and the mass, extend `DragShape` to it; if it gains lift or
a moment, carry that in the same place, since the body's attitude is already being flown. Holding
the measured acceleration is the fallback, and it is what a target without a shape still gets.
Closed-loop spotting would stop the gun depending on the copy at all, and `docs/CIWS-REALISM.md` §2
says what it would take.

**And it is not a real fire-control system either.** The lead reads the target's mass, fuel, drag box
and body rates straight out of the engine — nothing a radar could measure. It hits because it knows
the game's true state under the game's simple physics. A realistic director would estimate all of
it from the track and miss more, especially against a target that manoeuvres.

---

## A menu bar a mod can add to

**Partly unblocked by StarMap 0.5, and taken up.** StarMap's `IModMenuService.AddModMenuItem(label,
onClick)` puts an item under a **Mods** menu StarMap draws itself, and `KSArmoryMod`'s constructor takes
it: **Mods > KSArmory** toggles the panel. So the panel is always reachable, and the floating button and
the copied ModMenu attribute are gone. Not yet seen in game: StarMap 0.5.2 does not start on
2026.11.10.5801.

**What is still missing.** The item is a button. The built-in families' switches need a submenu, so
`Ui.DrawMenuBarEntry` still appends a top-level **KSArmory** menu with `ImGui.BeginMainMenuBar()`,
called *before* KSA's GUI pass because after it the bar has been ended for the frame. That works
because ImGui's menu bar is immediate-mode, not because anything supports it.

**What would unblock it.** A menu item that can hold children, from StarMap or from `Program`. Then the
append goes and the switches move under Mods > KSArmory.

## Full-screen passes inside KSA's frame

**Solved by patching — recorded so the trade stays a deliberate one.** KSA has no post-processing chain
a mod can join (`KSA.Rendering.PostProcessing` is `Cmaa2Renderer` and `HableFilmicToneCurve`, wired into
`Program`) and no StarMap hook carries a command buffer. `Ksa/CloudPassHook.cs` prefixes the public
`SunbloomRenderer.Render`, which hands over the command buffer while the scene colour is
storage-writable, the depth is sampled and bloom and the tonemap are still to come; every compute pass
the mod has — cloud, shock, tracers, rings, holes, leaks, beams, the sensor look, the flash — records
there. A patch that does not apply leaves those effects undrawn and nothing else.

**What would unblock it properly**, and let the patch go: any hook that hands a mod the frame's command
buffer at a stated point in the render. AMPW's ShaderExtensions (`<GlobalPostShader>`, on tsholmes'
KittenExtensions) is the community's other route, and taking it would mean two framework dependencies.

## A camera window: no ocean, clutter, trails or clustered lights

**Wanted.** A director's camera window drawing the same picture as the main view, so a site can be
watched while flying something else.

**What a window draws now.** Since 2026.10.7.5541 a secondary viewport runs the planet renderer and the
sky for its own slot, with its own LOD off its own camera (`PlanetViewportData.OnFrame`), and
`Camera.GetEffectiveNearbyCelestial` keeps the grey distant sphere out of it; 2026.10.19.5638 added a
terrain depth pre-pass and the radiation belts; 2026.10.25.5707 the clouds
(`ViewportOptionFlags.RenderClouds`, `CloudRenderer`'s `ViewportResources`), seen in a director's window on
5707. Since 2026.10.13.5602 its
terrain is drawn **unshadowed** unless `PlanetRenderer.ViewportShadows` is set (default false), where it
was shadowed through cascades fitted to the main camera and moved whenever that camera did; the shadow
maps are still built for the main view alone.

**What it still does not.** The ocean, ground clutter, volumetric trails and the clustered light pre-pass
stay in the main-only half of `RenderGame`, and detail textures stream round the main camera alone
(`PlanetRenderer.OnFrame` is called for the frame viewport), so a window looking far from the player may
see coarser ground. `Ksa/SightCamera.cs` borrowing the main view is still the full picture.

**Don't build a second renderer for it.** One was built and flown before 5541: a second
`PlanetRenderer` bound to a window's target drew no terrain and made the main view's flicker, because
the terrain mesh belongs to the `Celestial` (`PlanetRenderer.GetMesh`, `GenerateMeshData` writes
`cubeMesh.Mesh`'s own buffers) and was regenerated every frame for one camera.

**What would unblock the rest.** The ocean, clutter and lighting passes run per viewport as the planet and
cloud passes now are, and shadow cascades fitted per viewport.

## Commanding a vehicle's attitude from a mod hook

**Solved by patching, like the full-screen passes entry above — recorded so the trade stays a
deliberate one, and so it can be given up the day KSA offers a hook.**

**Wanted.** To point somebody else's rocket, which is the whole of `Ksa/VehicleCommand.cs` and
everything the ballistic computer does with it.

**The engine reason.** `FlightComputer` is double-buffered across the frame, and every hook StarMap
offers lands on the wrong side of it. In `Program.OnFrame`:

```
2165   Universe.ApplyVehicleSolvers()          // vehicle.FlightComputer.CopyFrom(worker result)
2211   Universe.ExecuteNextVehicleSolvers()    // PrepareWorker snapshots vehicle.FlightComputer
2259   OnDrawUiViewports()                     // [StarMapBeforeGui] / [StarMapAfterGui]
```

A write made at 2259 is not in the snapshot taken at 2211, so the result applied at 2165 of the
*next* frame — computed from that snapshot — overwrites it. Then 2211 snapshots the overwritten
value. `[StarMapAfterOnFrame]` is later still. There is no hook between 2165 and 2211, which is the
only window where a write survives.

Confirmed in flight rather than inferred. A probe reading the flight computer either side of the
write reports, every frame without exception:

```
aimed=True dir=set | before Manual/None -> after Auto/Custom | error <0,0,0> rates <0,0,0>
```

The write lands and is gone by the next frame, and the engine's own error angles stay at zero
because it is not tracking anything. `FlightComputer.CopyFrom` does copy `AttitudeMode`,
`AttitudeTrackTarget` and `CustomAttitudeTarget`, so the round trip is not lossy — the write is
simply on the wrong side of it.

**What is done instead.** `Ksa/AttitudeHook.cs` puts a Harmony prefix on `Vehicle.PrepareWorker`,
the one thing inside that window a mod can reach. cairn5's **PoweredGuidance** does the same, and
its own comment gives the same reason: the prefix runs "right before the sim snapshots the flight
computer".

**Why this is a much weaker thing than the ModMenu transpile above.** That one rewrites the IL of a
game method. This one patches a **`public virtual`** method — declared API, not an implementation
detail. `AttitudeHook.PinTheSignature` is never called and exists only to emit a reference to it, so
it appears in `docs/KSA-API-SURFACE.md` and a signature change in KSA is a build error here rather
than a silent break. Harmony ships with StarMap, so nothing is asked of a player. The one real cost
is that a prefix runs inside the engine's frame loop, where an exception is the game rather than a
log line — so it is wrapped, stands down on the first failure, and says so once.

**What would unblock it properly**, and let the patch be deleted. Any hook between applying the
solver results and taking the next snapshot: a `[StarMapBeforeVehicleSolvers]` would do it, and so
would a public `Vehicle.SetAttitudeCommand(...)` writing into the pending snapshot rather than into
the live object.

## Custom part modules

**Wanted.** Rounds as real part-based vehicles, with the engine integrating them.

**Why it is blocked.** Registering a custom module type means getting it into engine-internal
update lists, which is not reachable without Harmony patching the engine.

**Consequence in the mod.** Rounds are simulated by the mod and drawn as subparts whose transforms
are written each frame. That is not purely a loss — it gives sub-frame accuracy for free and cannot
corrupt a save — but it is a workaround, not a choice made freely.

---

## Plume trails on mod-simulated rounds

**Solved by reflecting one private field — recorded so the trade stays a deliberate one, and so it
can be given up the day KSA exposes an accessor.**

**Wanted.** Smoke trails behind the missiles, using the game's own volumetric trail renderer
rather than the mod's gizmo tracers.

**The declarative route is closed.** The XML tag is real — `<PlumeTrail Id="DefaultPlumeTrail"/>`
inside a `<ReactionPlume>` — but the emitter only produces anything when
`current.State.DutyCycle > 0f && current.State.ExhaustVelocity > 0f` (`KSA/KSA/Vehicle.cs:5691`), where `DutyCycle` is
accumulated by a **burning rocket core**. The mod's rounds have no motor, no propellant and no
staging, and a real motor would apply real thrust to the launcher, since the round bodies are its
subparts.

That gate is the `isFlowing` argument rather than a property of the renderer, so a caller passing its
own never meets it. `VolumetricTrailRenderer.SubmitEmitter` is `public` and `PlumeTrailEmitterState`
is a public class, so `Ksa/PlumeSmoke.cs` holds one cursor per round and submits it each frame — no
nozzle, no propellant, no thrust. The single obstacle is that `Program._volumetricTrailRenderer` is
a private field with no accessor, where its sibling exhaust renderer got one, so that one field is
reflected and everything after it is an ordinary call inside `docs/KSA-API-SURFACE.md`.

The emitter follows a moved subpart correctly either way: its position comes from
`state.FxExhaustLocationVehicleAsmb = FxLocationAsmb.Transform(matrix)` where `matrix` is
`Parent.MatrixAsmb2VehicleAsmb` (`KSA/KSA/RocketNozzle.cs:282`) — exactly the matrix this mod
already writes each frame.

**What would unblock it properly**, and let the reflection go. A public accessor for the trail
renderer, of the shape `Program.VolumetricExhaustRenderer` already has.

**Outstanding with RocketWerkz.** blackrack (KSA graphics programmer) suggested the XML tag.

## A thermal picture that knows what is hot

**Wanted.** A director's thermal channel showing a hot engine bright against a cold sky **because it is
hot**.

**What the mod does instead.** `Shaders/KSArmorySensor.comp` makes white-hot and black-hot off the scene's
brightness with the sky read cold — a look, not a temperature. A screen-space pass reads the composited
colour and has no idea what any pixel was, so anything dark in daylight stays dark in thermal. The hot
things that matter here are ones the mod simulates — motors, fireballs, flares — and their positions and
intensities are known, so compositing them as bright sources is open to it without any engine change.

**What would unblock the physics.** A per-object temperature reaching a shader: a material hook or a second
render target. KSA computes no such temperature today.

## Configuring a part in the vehicle editor

**Wanted.** What a fuel tank gets: a section in the editor's part inspector with the part's own
settings, saved with the craft. For a launcher that would be a loadout — which round a rail
carries, how many, what the fuse does.

**Why it is blocked.** Three layers, and the third is the one that decides it.

- **The inspector has no extension point.** Its sections are written out longhand against concrete
  module types — `part.SubtreeModules.Get<Tank>()` then a Propellant block
  (`KSA/KSA/VehicleEditor.cs:6572`), the same shape for decouplers and the rest. There is no
  registry, no per-module draw callback, nothing keyed on a mod.
- **A mod cannot register a module type** to be drawn for in the first place. See *Custom part
  modules* above.
- **A saved craft has nowhere to put it.** `PartTree` (`KSA/KSA/PartTree.cs`) keeps a typed
  `ModuleStateful<…>.StateList` field per module type the engine knows. **Since 5638 it carries a
  `ModuleList Modules` and a `ModuleStateList States` keyed on the module's `Type`**, which is open to any
  type — but both ends that feed it are closed. `ModuleList.CreateModules` (`KSA/KSA/ModuleList.cs:115`) is
  a fixed list of `X.CreateComponents` calls; the XML element map for `PartInstance.Components` is built
  in `XmlHelper`'s static constructor from `Assembly.GetExecutingAssembly()` alone (`XmlHelper.cs:21`), and
  the part-file and universe-save serializers are built from it before any mod loads
  (`XmlHelper.cs:43`, `GameSaves.cs:135`). A save entry with no matching module is dropped with a warning
  (`PartTree.cs:376`); an unknown element is dropped silently. `PartInstance` has no free-form field.
  So per-part mod state rides a vehicle file only by patching: a postfix on `ModuleList.CreateModules`
  adding the module, its `SaveData` type appended to `XmlHelper.AttributeOverrides` before the craft-file
  serializer is built (`VehicleSaves.cs:181`, which may or may not be after StarMap's earliest hook —
  unchecked) and the universe one replaced, and a postfix on `VehicleEditor.DrawPartUi` for the window.

**What is not blocked, and why it is still not built.** The editor itself is readable:
`Program.Editor` is a public static (`KSA/KSA/Program.cs:228`) and `VehicleEditor.Selected`,
`Highlighted` and `EditingPart` are public `Part?` (`VehicleEditor.cs:553-559`). So the mod could
detect the editor, see the selected part and draw its own window beside KSA's.

It would be a window whose settings cannot be saved with the craft, which is worse than no window:
a loadout chosen in the editor and silently gone on load is a bug report waiting to happen. The
mod's own store keys per *craft* by display name, and there is no per-part key that survives the
round trip.

It also has nothing to configure yet. Weapon *performance* lives on shared profiles, so a per-part
control there would be the wrong scope, and per-installation *policy* — auto-engage, IFF —
is a flight decision the panel already covers and already persists. The day a rail can carry more
than one kind of round, that is an editor decision and this becomes worth solving.

**What would unblock it.** An extensible per-part blob in the vehicle save that survives a
round trip, or a registerable module type — either one makes the rest follow. It would also turn the planned family of armour
plates (`docs/ARMOUR-PLAN.md`) — a part per thickness — into one plate with a thickness setting. The
editor's scale gizmo is the one per-part setting KSA does save (`Part.Scale`, written as the part's
`Transform`, 0.5x to 2x), and it is uniform: a plate scaled to 2x is twice as thick as well as four times the
area, so it cannot stand in for a thickness.

## Filtering the part picker by mod or manufacturer

**Wanted.** A shared **Weapons** category that several weapon mods can put parts into, with a way
to narrow it to one maker — "Kessler Armory Systems" — so a player running three of them can find
this mod's nine parts among thirty.

The category half is already possible and is built: `EditorTag` is a string-wrapping record struct
rather than an enum, `EditorTagDefinition.OnDataLoad` calls `VehicleEditor.RegisterTag`, and the
picker draws a row for every registered tag not flagged `NotaCategory`. Core's own
`Content/Core/CoreEditorTagsGameData.xml` says so in a comment addressed to modders — this mod
registers **Weapons** and **Sensors**. It is the *filter within* a category that has nowhere to go.

**Why it is blocked.** Three things are missing and none has a workaround:

- **No manufacturer, vendor or author field** anywhere on `PartTemplate` or `PartGameData`
  (`KSA/KSA/PartTemplate.cs`). The only text a part carries is `DisplayName` and its `Id`.
- **No search box.** The picker's only input is a *diameter* combo, keyed on the selected tag
  (`KSA/KSA/VehicleEditor.cs:272`). The grid filter is the selected tag, whether the template is a
  subpart, and whether it is hidden — nothing that a mod or a maker could key on (`:316`).
- **The owning mod is known and never used.** `SerializedId.Mod` is set on every asset at load
  (`KSA/KSA/SerializedId.cs`), so the data exists — but `VehicleEditor` never reads it. The
  category state is a `private EditorTag _selectedTag` on the private nested `PartWindow` (`:52`),
  so a mod cannot drive the selection either.

The only mechanism available is *more tags*: a second `EditorTagDef` per maker, giving a
"Kessler Armory Systems" row beside "Weapons". That is not built, deliberately. Everything it would
contain is already reachable from the two rows this mod registers, so it is a third category
earning nothing; it starts earning the day a second weapons mod ships and a player has both.

**What would unblock it.** A manufacturer or maker attribute on `PartGameData` that the picker
filters on, or a search box over `DisplayName`, or simply exposing the owning mod the engine is
already tracking.

## Where a structure's surface is

**Wanted.** A cursor over the launch pad to resolve to the top of the pad, so a craft set down
there lands on it rather than through it — and so pointing at the pad's corner is not answered
with the ground beside it.

**Mostly unblocked in 2026.8.22.5348, and the mod has not taken it up.** A `LandmarkReference`
(`KSA/KSA/LandmarkReference.cs`) now carries a `StaticObjectId`, and `LocationReference` resolves it
through a public `GetStaticObject()` to a public `StaticObject` with declared `GroundOffset`,
`SurfaceHeight` and `FootprintRadius`, its own models, and a Bepu compound built from real
colliders. Core's `CoreLaunchPadA_Prefab_LaunchPadA` is 0.2 m + 1.5537 m over a 108.3 m circle, and
`Vehicle.GetInitialKinematicStateForLocation` stands a craft on exactly those numbers.

Terrain itself was never the problem — `Celestial.GetTerrainHeightFromDirCcf` is public and
accurate, and the cursor already uses it. Earth's height field is also decal-levelled to a fixed
altitude within 275 m of each launch site, so the ground the pad stands on is flat before the pad
is added.

**What is left.** The footprint is a *radius*, so a square pad's corner still answers with the
ground beside it, and the height it gives is one number for the whole disc rather than the surface
under a particular point. Nothing here has been built into the mod: `KsaWorld` resolves the cursor
against terrain alone, because a pad modelled as a thicker planet swung the bearing from the mount
through 168° between two adjacent pixels at the pad's edge.

**Placing a craft already stands it on the pad.** `Vehicle.TeleportToLocation`, which Move craft with
mouse uses, goes through `GetInitialKinematicStateForLocation` (`Vehicle.cs:4322`), and that adds
`GroundOffset + SurfaceHeight` inside an `IsLaunchPad` landmark's `FootprintRadius` (the private
`GetLaunchPadHeightAtDirCcf`, `Vehicle.cs:4296`). What is wrong is the marker and the cursor, which say the
terrain.

**Not blocked on KSA, and not taken up.** No engine raycast reaches a structure from afar: nothing calls
Bepu's `Simulation.RayCast`, and the pad's collider exists only while a vehicle is within 300 m of it, in
bubble coordinates (`ConstraintSim.cs:585`). But the meshes are public: `StaticObject.Template.Models[i].Mesh`
is a `MeshReference` whose `PositionCompare` (`MeshReference.cs:55`) is the de-indexed triangle list
`Ray.RaycastWatertight` takes, as `Part.RayCastEgo` uses it. The pose is the landmark's
(`LocationReference.GetAxesCcf`: assembly X up, Y east, Z north) at its terrain height plus `GroundOffset`,
and placed objects are found by walking `BodyTemplate.Locations` as the engine does. So the cursor can stop on
the pad: a bounding-sphere reject per landmark, then the mesh test; or the flat top disc alone, which is the
model placement already uses.

## A bright light far away, on craft and structures

**Wanted.** A fireball's point light on every surface it reaches, as the terrain has it.

**What stops it.** Every light goes to two places (`ClusteredLightSystem.CreateLightInstance`,
`KSA/KSA.Rendering.Lighting/ClusteredLightSystem.cs:894`). Terrain takes it through the forward
loop (`Lighting/PunctualLighting.glsl` `SampleForwardLights`), which has no cut. Craft parts, static
objects such as the launch pad, and every other pre-pass mesh take it only through
`Lighting/LightPrePass.comp`, whose first test is

```glsl
if (subgroupAll(dotNL * rangeAtt < ATT_EPSILON))   // 1e-7, rangeAtt = falloff / d^2
    continue;
```

before the light's intensity is multiplied in. So a light is dropped from those surfaces beyond
3.16 km times the square root of `dotNL`, however bright it is. That is right for a lamp and wrong
for a 50 kt fireball, which lit the ground round the pad at night and left the pad and the rocket
on it black -- and, at 2.1 km, lit the pad in a pattern of rectangles, one per subgroup that voted
it out. No light flag keeps a light out of either path.

**What the mod does instead.** `Shaders/KSArmoryFireLight.comp` reads what the pre-pass wrote
(`ClusteredLightSystem._diffuseIrradianceImage`, private) and, on each pre-pass pixel the eye sees
whose irradiance holds less than half the fireball's, adds the forward loop's diffuse term. Since
2026.10.25.5707 terrain and ground clutter are drawn into the pre-pass too (`PrePassRenderer.Render`)
while still lighting themselves forward (`Planet.frag`, `GroundClutter/Solid.frag`), so they are told
apart by the normal's w: both write exactly one there (`PlanetDepth.frag`, `GroundClutter/SolidDepth.frag`),
where a part or structure writes its filtered roughness (`MeshNormalIndirect.frag`,
`StaticObjectNormalIndirect.frag`). A structure texel exactly fully rough is then left as KSA draws it. The
pre-pass keeps a surface's normal and not its colour, so the albedo is recovered from the pixel: in
sunlight it shows `albedo / pi x sunColor x occlusionColor x N.L`, `StaticObject.frag`'s direct term,
and the albedo divides out. Where the sun does not reach the pixel is black and says nothing: a surface
lying on the ground, the pad and its apron, takes the planet's colour map there, which is what a
terrain-sampled apron is textured from, and anything else the planet's `meanDiffuseLuminosity`. The
ground showing beside it on screen was tried and copied the trees there across the apron. A fixed 0.28 made the pad glow many times
brighter than the grass under a 50 Mt ball at 100 km; recovered, the apron's grass matches the
terrain's and only the concrete, which is brighter, reads brighter. The rectangles at 2.1 km fade
rather than vanish. 0.04 ms a
frame while a fireball glows. Without the field the fill is off and the pad is dark again.

**What would unblock it.** `intensity` in that early-out, which is one float load the loop makes three
lines later anyway.

## A body's magnetic field and what its air is made of

**Wanted.** Every body's magnetic field, and its air's composition, for the aurora and the debris a
high burst leaves (the field holds it) and for the colour its excited air glows.

**What stops it.** KSA declares the field and not the composition. `AstronomicalTemplate.MagnetosphereReference`
(`KSA/KSA/MagnetosphereReference.cs`) is an eccentric, tilted dipole: a pole latitude and longitude, an
offset of the centre, the equatorial field in µT, radiation belts and an aurora band in `L`, with
`ToMag`/`FromMag` between the body's fixed frame and the dipole's. Core gives Earth a 30 µT dipole toward
80.7° N 72.7° W centred about 570 km off, and several other bodies one too. KSA draws its own aurora from it
(`AuroraRenderer`). An atmosphere is still a sea-level pressure, a density, a scale height and a boundary,
isothermal, and nothing else.

**What the mod does instead.** `KSArmory/Bodies.xml` in any mod, keyed by body Id (`Sim/BodyReader.cs`),
carries the field, the airglow, whether water condenses, γ and whether there is a surface; KSA's own
bodies ship with entries, and a body with none gets no field and a neutral glow. The atmosphere's
isothermal model is mirrored exactly in `Sim/BodyAir.cs`, so if KSA ever gives air a temperature profile
every altitude law has to follow it.

**What would unblock it.** A composition on the body template. The field is read now:
`KsaWorld.DipoleOf` takes the template's dipole, tilt and offset included, ahead of `Bodies.xml`, so the
aurora, the debris held along the field and the belt a burst fills all follow KSA's own field. The file's
field is only for a body KSA declares none on.

## `IsValid()` on a distance is an astronomical-scale check

**This one is not a missing feature — it is a predicate that does not mean what it is named**,
which is worse, because the code reads correctly and is silently wrong.

**Wanted.** To ask KSA whether a body's atmosphere and ocean are usable, with KSA's own
`IsValid()`, rather than second-guessing it.

**The engine reason.**

```csharp
// KSA.DistanceReference
public override bool IsValid()
{
    return !double.IsNaN(_value) && Math.Abs(_value) > 100000.0;
}
```

A distance is "valid" only above **100 km**. That is a sane check for the orbital distances the
type mostly carries and nonsense for anything planetary-surface sized — and two of the references
this mod needs are exactly that:

| Reference | Earth's value | `IsValid()` |
| --- | --- | --- |
| `PhysicalAtmosphereReference.ScaleHeight` | 8 km | **false** |
| `OceanReference.Level` | 0 m | **false** |
| `OceanReference.TransparencyDepth` | 100 m | **false** |

Both composites fold those in — `PhysicalAtmosphereReference.IsValid()` is
`ScaleHeight.IsValid() && SeaLevelDensity.IsValid() && SeaLevelPressure.IsValid()`, and the
ocean's is the same shape — so **`air.IsValid()` is false for every realistic atmosphere and
`sea.IsValid()` is false wherever there is water.** `DensityReference` and `PressureReference` are
fine; they test `_value > 0`. It is the distance that is wrong.

**What it cost here.** `KsaWorld.MediumDensityRatioAt` gated on `air.IsValid()` and so reported
**vacuum at ground level, on Earth, always**. Nothing in the mod has ever had atmospheric drag,
and a released store never weathervaned, because `BodyAttitude` needs `q = rho*v^2` over 4 before
the airflow has any authority and `rho` was pinned at zero. Confirmed in flight: a B61 at 81 m
and descending, over an atmosphere reporting a correct 1.225 kg/m3 sea level, an 8 km scale
height and a 167 km top — with `valid=False`. `Ksa/GroundTest.cs` had it too, so `hasSea` was
always false and a round fell through the waterline to burst on the seabed.

**The workaround, in both places.** Do not call `IsValid()`. Check the terms actually divided by
— `SeaLevelDensity > 0` and `ScaleHeight.InMeters() > 0` — and use a **null** ocean reference as
the discriminator for a body with no water, which is what `Astronomical.GetOceanReference`
returning `BodyTemplate.OceanReference` already means.

**What would unblock it.** `DistanceReference.IsValid()` dropping the 100 km floor, or the
atmosphere and ocean composites testing their own fields directly rather than delegating a
surface-scale distance to an astronomical-scale predicate.

## A floating craft's waterline depends on where it is

`PhysicsEnvironment.RecomputeImmersion` finds the centre of the sphere it floats a craft on as
`(positionCcf + double3.Unpack(props.MassToGeometryAsmb)).Length()`: a planet-frame position plus
an assembly-frame offset that is never rotated. So wherever the centre of mass is off the bounding
box's centre, how high a craft floats changes with where on the planet it is and which way it
points. Flown with a 134 m hull whose centre of mass sat 6.7 m under the box's centre: the sphere's
centre landed 3.87 m above the centre of mass rather than 6.7, and the keel rode 2 m above the sea
instead of 4.6 m below it. Putting the centre of mass on the box's centre removes it, and is the
workaround until the offset is rotated (`Transform(Asmb2Ccf)`) or buoyancy stops being a sphere.
`docs/KSA-MODDING-NOTES.md` *A vehicle in the sea* has the rest of the model.

## Walking on a craft

A kitten's walk mode needs ground, and ground is a static contact only
(`ConstraintSim.IsGroundSurfaceFor(VehicleUpdateState, StaticHandle)`, `ConstraintSim.cs:169-185`):
terrain, terrain blocks, clutter and launch pads. On a vehicle's deck a kitten stands on the
colliders but is airborne to its locomotion — no walking, no jumping, and no frame that carries it
with the craft. What works today is a `<Grab>` rail from the ground to an `EVADoor`
(`docs/KSA-MODDING-NOTES.md`). Unblocked by vehicle contacts counting as ground, with the kitten
servoing its speed against the deck rather than the planet (`KittenServoPrecomp.cs:73-82`).

Nor is there anything that pushes in water: a rocket nozzle reads atmospheric pressure alone and
thrusts underwater as at sea level (`Rocket.cs:181`, `:208`), and no propeller or propellant-free
engine exists, so a boat is a mod writing velocity every frame.

