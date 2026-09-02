# Blocked on KSA

Things this mod wants and **cannot build**, because the game does not expose what they need. Not a
backlog — `CLAUDE.md`'s *Not done* section is the backlog, and everything in it is this mod's own
work. Everything here waits on RocketWerkz.

Each entry cites what the decompiled corpus says, so a claim can be rechecked after a KSA update
rather than taken on trust. **Recheck this file when the game moves** — the whole point of it is
that some of these will quietly become possible.

Findings are against KSA **2026.9.4.5400**. Paths are relative to
`../ksa-game-assemblies/current/src`.

## Recheck after a KSA update

Tick when rechecked against the new build, then untick for the next one. `tools/ksa-api-diff.sh`
will not surface any of these — none is a signature change, and most are a call that does not
happen rather than a member that moved.

- [x] Secondary viewport gets the planet, atmosphere and lighting passes
- [x] `Camera.NearbyCelestial` is set per camera rather than only for the frame viewport
- [x] Wheel, suspension or steering module exists
- [ ] ~~Partial or component damage exists alongside `DestroyVehicleFromEvent`~~ — **arrived in
  2026.9.4.5400**, see below; the mod has not taken it up
- [x] Per-mod vehicle library path, or a way to register saved craft
- [x] `UncompressedVehicleSave.Load` honours `Character`, making a kitten launchable
- [x] A character attachment's pose survives the frame, so a mod can aim one
- [x] Custom part modules can be registered without patching
- [x] Public accessor for the volumetric trail renderer
- [x] A post-processing or full-screen shader hook a mod can register into
- [x] **A hook between applying the vehicle solvers and snapshotting them** — delete `Ksa/AttitudeHook.cs`'s patch the day this exists
- [x] **A menu-bar hook a mod can register into** — delete `Ksa/Ui/ModMenuEntry.cs` the day this exists
- [x] **`DistanceReference.IsValid()` stops requiring 100 km** — go back to `IsValid()` on the atmosphere and the ocean the day it does

**Twelve of the thirteen rechecked against 2026.9.4.5400 and still blocked; partial damage is the
one that moved** — KSA grew a real part-failure system, and the entry below says what it is and what
taking it up would mean.

The line numbers below are against **2026.8.22.5348** and have not been re-derived: 2026.9.4.5400
replaced the `Viewport` class with `IViewport` / `ViewportBase` / `GameViewport` and moved the list
into `ViewportRegistry`, which moved most of the render path. The *claims* were rechecked against
the new corpus; only the citations are stale. Lighting is still a per-viewport choice —
`IViewport.LightMode` is a `ViewportLightMode` (renamed from `EViewportLightMode`), set to
`Clustered` for the main viewport and `None` for the thumbnail one — but that is the pass a
secondary viewport was already getting some of. `OnFrameCelestials` still resolves one camera through `GetCamera()`
and still calls `_planetRenderer.OnFrame(FrameViewport, ...)`, so the planet, atmosphere and ocean
passes remain the frame viewport's alone and the first two entries stand. `UncompressedVehicleSave.cs`
does not mention `Character` at all; `KittenRenderable` writes an attachment's transform and
submits its draw in consecutive statements; `KSA.Rendering.PostProcessing` is an anti-aliasing
pass and a tone curve.

---

## A menu bar a mod can add to

**Delete the workaround the moment this changes.** `src/KSArmory/Ksa/Ui/ModMenuEntry.cs` exists
only because of what follows, is wanted gone, and is on the recheck list above so it is looked at
every time the game moves.

**Wanted.** An entry in KSA's own menu bar, so the panel opens from where a player expects rather
than from a floating button parked over the flight gauges.

**The engine reason.** `Program` draws the bar inline —
`ImGui.BeginMenu("File")`, `"Universe"`, `"View"`, `"HUD"` — with no event, no registry and no
extension point of any kind. StarMap adds nothing either; its attributes are lifecycle hooks and
none is menu-shaped.

**What the ecosystem does instead**, and why it is unpleasant. MrJeranimo's **ModMenu** Harmony
*transpiles* `Program.DrawMenuBar`, scans its IL for an `ImGui.EndMenu()` call and splices in its
own `BeginMenu("Mods")`. Mods opt in with a `[ModMenuEntry]` attribute, which ModMenu matches by
`GetType().Name` alone — so a mod copies the attribute rather than referencing anything, which is
all `Ksa/Ui/ModMenuEntry.cs` is. It costs no dependency, but the whole arrangement stands on
rewriting the IL of a game method in a pre-release build. `DrawMenuBar` being public buys nothing
here: calling it draws the bar, it does not contribute to one.

The mod also appends to the bar directly with `ImGui.BeginMainMenuBar()`, which works because
ImGui's menu bar is immediate-mode. It must be called *before* KSA's GUI pass: from
`[StarMapAfterGui]` the bar has already been ended for the frame and the call returns false. That
is still ImGui behaviour rather than a supported hook, which is why the floating button stays.

**What would unblock it.** Any public means of contributing a menu: an event on `Program`, a list
of callbacks, or an asset type. Then both the copied attribute and the `BeginMainMenuBar` append
go, and the panel opens from a menu the game itself put there.

## Full-screen post-processing shaders

**Wanted.** A gunner's sight that actually looks through optics — desaturation, a vignette, grain,
thermal false-colour. Also the whole class of damage and blast effects that live on the framebuffer
rather than in the world.

**The engine reason.** `KSA.Rendering.PostProcessing` contains `Cmaa2Renderer` and
`HableFilmicToneCurve` and nothing else: an anti-aliasing pass and a tone curve, both wired
directly into `Program`. There is no chain to append to, no asset type for a shader, and nothing
in the namespace a mod can register with. Searching the corpus for `GlobalPostShader` or any
equivalent returns nothing, so the extension point does not exist to be called.

**It is nonetheless being done, by patching.** AMPW's **ShaderExtensions** adds `<ShaderEx>`,
`<PostProcessingShader>`, `<GlobalPostShader>` and `<ImGuiShader>` asset types to KSA, built on
tsholmes' **KittenExtensions**. His **KSAGEffects** then declares a fragment shader with

```xml
<GlobalPostShader Id="GEffectFrag" Path="Shaders/GEffectShader.frag" RenderPassId="256" SubpassId="0">
  <GEffectBuffer Id="GEffectBuffer" Size="1" />
</GlobalPostShader>
```

and the shader reads the framebuffer as a genuine Vulkan **subpass input**
(`layout(set = 1, binding = 0, input_attachment_index = 0) uniform subpassInput Source`), with a
uniform buffer the mod fills each frame.

**So this is blocked on KSA only in the sense that doing it natively is impossible.** Taking it
would mean depending on two third-party framework mods, which is a different decision from a
missing engine feature — this mod currently requires only StarMap. Recorded here so the trade is a
deliberate one.

## Secondary viewport: no sky, clouds, atmosphere or terrain

**Wanted.** The launcher's electro-optical head drives a second camera window, so the sight can be
watched while flying something else. The head, the tracking, the reticule and the camera write all
work; the *picture* is wrong.

**What happens.** A secondary viewport shows a raw starfield above a hard horizon and a
featureless grey ball, where the main view at the same position shows sky, clouds and terrain.

**Why.** Secondary viewports go through `Program.RenderViewport` (`KSA/KSA/Program.cs:4168-4283`),
a much shorter path than the main one. The loop that calls it ends at `Program.cs:4339`, and every
pass that makes a planet look like a planet is after that line and only ever sees the main
viewport: the planet renderer, the light and shadow passes, the ocean, and
`_planetTransparenciesRenderer.Render` (`:4522`) — the sole call site of the atmosphere and cloud
compute passes anywhere in the game.

Two details explain the exact image:

- The starfield is drawn because stars *are* in the reduced path (`Program.cs:4211-4220`).
- The grey ball is not terrain. It is `StaticCelestial.RenderSphere` → `DistantSphereRenderer`, a
  sphere scaled to `MeanRadius` with no heightfield. It appears because
  `Camera.NearbyCelestial` is only ever assigned inside `OnFrameCelestials`
  (`Program.cs:2480-2510`), which runs for the frame viewport — always the main one. The check
  that suppresses the planet you are standing on
  compares `camera.NearbyCelestial == orbiter` (`StaticCelestialDistanceRendering.cs:416`), and a
  secondary camera's is permanently `null`, so it never matches. The same `null` zeroes that
  viewport's lighting data.

**Why a mod cannot fix it.** `PlanetTransparenciesRenderer`, `OceanRenderer` and
`OverallBloomRenderer` are constructed holding `Program._offscreenTarget`
(`Program.cs:1128, 1143, 1152`), which *is* `MainViewport.OffscreenTarget` — the same object
(`Program.cs:1477`). The `Viewport` they accept per call only selects a shader dynamic offset;
the image they write into was baked into their descriptor sets at construction. Redirecting them
means rebuilding Vulkan resources, which is not something a mod can do sensibly even with Harmony.

**What would unblock it**, roughly easiest first:

1. Assign `Camera.NearbyCelestial` per camera rather than only for the frame viewport. On its own
   this removes the wrong grey sphere. Cheap.
2. Array the atmosphere and cloud LUTs by viewport as well as by planet. There is already a
   pattern for this in the codebase — `SunbloomRenderer`'s buffers are sized by the viewport
   count, which is `ViewportRegistry.MAX_VIEWPORTS` and a viewport's own `ShaderSlot` since
   2026.9.4.5400.
3. Let the transparency, ocean and bloom renderers take their render target per call, or hold one
   set per viewport.
4. Run the planet, atmosphere and lighting passes inside the per-viewport loop.

**The architecture already supports per-viewport wherever it was designed to** — `_lightingData`
is indexed by `viewport.Index`, and the sunbloom buffers by viewport count. This is unfinished
rather than impossible.

**Workaround in the mod, and it is the shipped default.** `Ksa/SightCamera.cs` takes over the
*main* viewport instead — fully public API, full render quality, borrowed while the optic is
selected and handed straight back when it is not. The panel still offers a secondary window, for
watching a site while flying something else, and warns there that the picture is wrong. Whichever
of these entries unblocks first, that option stops needing the warning.

---

## Wheels, suspension and steering

**Wanted.** The Pantsir sits on an 8×8 chassis. The wheels should turn, steer and carry the
vehicle; ideally it should drive.

**Why it is blocked.** KSA has no wheel, suspension, steering or landing-gear module of any kind.
Searching the whole decompiled corpus for such a type returns nothing, and no Core part declares
one. There is no ground-vehicle physics to attach to.

**What would unblock it.** A wheel or suspension module with a declarable part interface. Until
then the wheels are geometry and the vehicle is placed rather than driven.

---

## Partial damage

**Wanted.** A round that lands close enough to hurt but not destroy should degrade the target —
knock out a sensor, break a part.

**No longer blocked, as of 2026.9.4.5400.** KSA grew a structural failure model:
`PartStructuralLimits` derives a crash tolerance in pascals from a part's mass and volume
(`PartTemplate.CrashTolerance` overrides it), `PartFailure.Detect` accumulates contact pressure and
fails individual parts, and `PartFailureEvent` — **public, with public `FailedParts`,
`DestroyWholeVehicle` and `Apply(Vehicle)`** — isolates a failed part, sheds debris and splits the
remainder into fragments, promoting the largest controllable one if the player was flying it.

So a mod can destroy **one part** rather than a whole craft: build a `PartFailureEvent` naming the
parts and `Apply` it. `PartFailure.IsolateAndDestroy` itself is `internal`, so `Apply` is the door.

**Not taken up, and it is a design decision rather than a port.** *Kills are binary* in `CLAUDE.md`
is justified by this entry, so changing it means deciding what a near miss should do — which part a
blast picks, whether a launcher that loses its radar should keep firing, and what
`WeaponSystems` does when a craft it has pinned splits into fragments it did not choose. The
roster already follows a part across a decoupler split (`Sim/PlatformHandover.cs`), which is the
half of it that exists.

**Consequence in the mod today, unchanged.** Kills are binary. `LethalRadius` destroys, and between
lethal and `BlastRadius` the mod logs a near miss and the target survives. The fuse radii are
gameplay numbers rather than physical ones for exactly this reason — a realistic lethal envelope
for a 20 kg continuous-rod warhead would read as the round doing nothing at all.

**And it already reaches the mod without anything being taken up:**
`Universe.DestroyVehicleFromEvent` now calls `PartFailure.ShedDebris(vehicle, 12)` before
destroying, so a craft this mod kills leaves debris vehicles behind. Those are craft, so they enter
`ContactCandidates` and can be seen, tracked and shot at. Flown behaviour unverified — see
`CHECKLIST.md`.

---

## A self-contained test scenario

**Wanted.** One click that places a launcher, a target and a scenario, so the mod can be tried
without the player assembling anything.

**Why it is blocked.** `LoadVehicleFromLibrary` in a system XML resolves through
`DefaultVehicleSaves`, whose `SaveFolderPath` is **hardcoded** to `Content/Core/defaultvehicles`
under the game install. It is not per-mod and not writable without elevation.

**What would unblock it.** A per-mod vehicle library path, or any way for a mod to register saved
craft with the loader.

**Workaround in the mod.** `tools/install-testcraft.sh` writes a craft into the *user's* vehicle
folder, which is writable, and `TestTarget` spawns drones on demand from the panel.

---

## Driving the main camera: set CameraRotation, do not unfollow

**Not blocked** — recorded because getting it wrong is a crash, in engine code, with nothing in
the message pointing at the cause, and because the obvious reading of it is backwards.

`FixedController.OnFrame` runs only when the camera it drives is following something, and then:

```csharp
double3 cameraRotation = CameraRotation;                       // public field, defaults to zero
double3 vector2 = double3.Cross(cameraRotation, vector).Normalized();
...
Camera.PositionEcl = following.GetPositionEcl() + CameraOffset;
```

So Fixed mode is **"follow this, but sit at an offset from it and look along a direction the
caller supplies"** — `CameraOffset` and `CameraRotation` are the entire interface, and the offset
is measured from the followed craft rather than from the world. A camera in Fixed mode *should* be
following something.

It divides by zero only because `CameraRotation` defaults to the zero vector, and crossing that
with anything and normalising is a division by zero length. **Set `CameraRotation` before setting
the mode**, and keep it set every frame.

The tempting misreading is that Fixed and following are an illegal pair, and that the fix is
`Camera.Unfollow(changeControl: false)` first. That does stop the crash, and it is wrong: the
camera then has nothing to be offset from, the view has to be restored by re-attaching a follow,
and *any* other thing in the game that attaches one — a jump-to-vehicle key, a scene teardown —
puts the camera back into the fatal pair with the rotation still zero.

`KsaWorld.TryLookFromMainViewport` does it the supported way. `TryLookFromViewport` unfollows,
because a secondary viewport's camera genuinely follows nothing and has nothing to offset from.

There is a second way to divide by zero here, and it is easier to hit: the controller crosses
`CameraRotation` with the reference frame's **+Z**, so a view pointing *along* that axis — the
local zenith under `Surface` — fails the same way. A round launched vertically points exactly
there. `docs/KSA-CAMERAS.md` has the full account of this and every other controller.

## Commanding a vehicle's attitude from a mod hook

**Solved by patching, like the post-processing entry above — recorded so the trade stays a
deliberate one, and so it can be given up the day KSA offers a hook.**

**Wanted.** To point somebody else's rocket, which is the whole of `Ksa/VehicleCommand.cs` and
everything the ballistic computer does with it.

**The engine reason.** `FlightComputer` is double-buffered across the frame, and every hook StarMap
offers lands on the wrong side of it. In `Program.OnFrame`:

```
2012   Universe.ApplyVehicleSolvers()          // vehicle.FlightComputer.CopyFrom(worker result)
2047   Universe.ExecuteNextVehicleSolvers()    // PrepareWorker snapshots vehicle.FlightComputer
2096   OnDrawUiViewports()                     // [StarMapBeforeGui] / [StarMapAfterGui]
```

A write made at 2096 is not in the snapshot taken at 2047, so the result applied at 2012 of the
*next* frame — computed from that snapshot — overwrites it. Then 2047 snapshots the overwritten
value. `[StarMapAfterOnFrame]` is later still. There is no hook between 2012 and 2047, which is the
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
`current.State.DutyCycle > 0f && flag` (`KSA/KSA/Vehicle.cs:5298`), where `DutyCycle` is
accumulated by a **burning rocket core**. The mod's rounds have no motor, no propellant and no
staging, and a real motor would apply real thrust to the launcher, since the round bodies are its
subparts.

That gate is the `isActive` argument rather than a property of the renderer, so a caller passing its
own never meets it. `VolumetricTrailRenderer.SubmitEmitter` is `public` and `PlumeTrailEmitterState`
is a public class, so `Ksa/PlumeSmoke.cs` holds one cursor per round and submits it each frame — no
nozzle, no propellant, no thrust. The single obstacle is that `Program._volumetricTrailRenderer` is
a private field with no accessor, where its sibling exhaust renderer got one, so that one field is
reflected and everything after it is an ordinary call inside `docs/KSA-API-SURFACE.md`.

The emitter follows a moved subpart correctly either way: its position comes from
`state.FxExhaustLocationVehicleAsmb = FxLocationAsmb.Transform(matrix)` where `matrix` is
`Parent.MatrixAsmb2VehicleAsmb` (`KSA/KSA/RocketNozzle.cs:209`) — exactly the matrix this mod
already writes each frame.

**What would unblock it properly**, and let the reflection go. A public accessor for the trail
renderer, of the shape `Program.VolumetricExhaustRenderer` already has.

**Outstanding with RocketWerkz.** blackrack (KSA graphics programmer) suggested the XML tag.

## A thermal or FLIR channel on the optical head

**Wanted.** The optical head showing a heat picture rather than a daylight one — engines and
exhaust bright against a cold sky, the way a real electro-optical turret is used at night.

**Split it in two, because only one half is blocked.**

*The look* — false colour, white-hot palette, grain, edge lift — is a screen-space remap of the
image that is already there, and it is reachable today through AMPW's ShaderExtensions
`<GlobalPostShader>`; see the post-processing entry above for what that costs. It is what most
games ship and call thermal.

*The physics* — a hot engine bright against cold sky **because it is hot** — is not. A post-process
subpass reads the composited colour image and has no idea what any pixel was, so anything dark in
daylight stays dark in "thermal". Real thermal needs per-object temperature reaching the shader,
which means a replacement material shader or a second render target. ShaderExtensions' `<ShaderEx>`
adds bindings to *existing* fragment shaders and is the only plausible route; whether it can carry
per-object data that KSA never computes is **unverified**.

**A weapons mod can cheat well here.** The hot things in these scenes are the ones this mod already
simulates: burning motors and fireballs, both of which it knows the position and intensity of.
False colour underneath, with genuine bright sources composited at the motor and burst positions,
covers most of what makes a FLIR picture readable here without any per-object temperature at all.

**Post-processing is full-screen only, and that scope works here.** `<PostProcessingShader>` and
`<GlobalPostShader>` differ only in whether they run before or after ImGui; both are full-screen,
and ShaderExtensions' README is explicit that "the shaders only target the main window, any other
windows are ignored". There is no per-viewport post-process.

That is fine here, because `OpticConfig.Viewport` can be the **main** viewport — and should
be anyway, since a secondary one draws no planet, terrain or atmosphere (see the entry above). With
the optic on the main view, full-screen *is* the right scope: while you are looking through the
sight, the main view is the sight.

Two constraints that come with it:

- **A post shader cannot be turned off.** It runs every frame at its stage forever. The documented
  answer is to pass an amount through a uniform and return the source colour unchanged at zero,
  which is a one-line early-out and costs a full-screen pass of nothing.
- **RenderPassId is a bare integer with no registry.** Ordering is only defined for unique
  renderpass/subpass combinations, and two mods that pick the same number get undefined execution
  order rather than an error. AMPW's g-effects already occupies 254, 255 and 256.

**What would unblock the physics half.** A public render-target or material/shader hook. Failing that, nothing:
of the effects a weapons mod normally spends shaders on, this is the only one that applies here.
Explosions and smoke already go through KSA's XML particle system, tracers are `GizmosRenderer`
lines, and there are no scorch decals to draw because kills are binary.

**Not blocking the optical head itself.** Main-viewport takeover, HUD symbology and zoom all sit on
`Ksa/Sight.cs` painting over the existing camera, and none of them need a shader.

## Configuring a part in the vehicle editor

**Wanted.** What a fuel tank gets: a section in the editor's part inspector with the part's own
settings, saved with the craft. For a launcher that would be a loadout — which round a rail
carries, how many, what the fuse does.

**Why it is blocked.** Three layers, and the third is the one that decides it.

- **The inspector has no extension point.** Its sections are written out longhand against concrete
  module types — `part.SubtreeModules.Get<Tank>()` then a Propellant block
  (`KSA/KSA/VehicleEditor.cs:6455-6458`), the same shape for decouplers and the rest. There is no
  registry, no per-module draw callback, nothing keyed on a mod.
- **A mod cannot register a module type** to be drawn for in the first place. See *Custom part
  modules* above.
- **A saved craft has nowhere to put it.** `PartTree` (`KSA/KSA/PartTree.cs:39-71`) is a fixed set
  of typed `ModuleStateful<…>.StateList` fields, one per module type the engine knows. It is the
  same closed shape as `UniverseData`, so per-part mod state cannot ride the vehicle file.

**What is not blocked, and why it is still not built.** The editor itself is readable:
`Program.Editor` is a public static (`KSA/KSA/Program.cs:207`) and `VehicleEditor.Selected`,
`Highlighted` and `EditingPart` are public `Part?` (`VehicleEditor.cs:549-555`). So the mod could
detect the editor, see the selected part and draw its own window beside KSA's.

It would be a window whose settings cannot be saved with the craft, which is worse than no window:
a loadout chosen in the editor and silently gone on load is a bug report waiting to happen. The
mod's own store keys per *craft* by display name, and there is no per-part key that survives the
round trip.

It also has nothing to configure yet. Weapon *performance* lives on shared profiles, so a per-part
control there would be the wrong scope, and per-installation *policy* — armed, auto-engage, IFF —
is a flight decision the panel already covers and already persists. The day a rail can carry more
than one kind of round, that is an editor decision and this becomes worth solving.

**What would unblock it.** An extensible per-part blob in the vehicle save that survives a
round trip, or a registerable module type — either one makes the rest follow.

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
  (`KSA/KSA/VehicleEditor.cs:257`). The grid filter is the selected tag, whether the template is a
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

**What would unblock the rest.** A raycast against static geometry. The pieces exist —
`StaticObject.CollisionShape` is a public `TypedIndex` and `ConstraintSim.UnlockShapes()` hands out
the Bepu `Shapes` registry — but nothing here has tried it, and the engine's own per-triangle path
is `Part.RayCastEgo` against `Ray.RaycastWatertight` (`KSA/KSA/Part.cs:2306`, `:2363`), which takes
a `Part` and not a landmark.

## Drawing a shape the gizmo renderer does not have

**Wanted.** A solid torus on the ground under a craft being placed, and in general any shape worth
looking at drawn at a world position — a marker, a volume, a footprint — without attaching it to a
vehicle.

**Why it is blocked.** `GizmosRenderer` draws two things. `Render` is `RenderSpheres` then
`RenderLines`, and `GizmoType` has exactly `Sphere`, `Line`, `Num`. Everything else it offers is
built from those: `DrawCircle` is twelve line segments, `DrawWireBox` and `DrawCylinderSides` are
line loops. There is no filled polygon in it at all.

The engine can *generate* the geometry — `ProcGenMeshLibrary.GenerateTorus` writes positions,
indices, normals and UVs, alongside sphere, cube and plane generators. What it cannot do from a mod
is *draw* it: arbitrary geometry has to be uploaded as a `SimpleVkMesh` and submitted inside a
render pass, and **no StarMap hook carries a command buffer** — its seven method attributes are
plain prefixes and postfixes on methods that take none. Patching a private render method with
Harmony would work and is not worth it: a private method sits outside the API surface
`ksa-api-diff.sh` checks, so it breaks silently on a KSA update that passes every other gate, and
an exception inside the render pass takes the game down with nothing pointing at the mod.

**What is possible today, and what it costs.** Real geometry reaches the screen through the asset
pipeline: a mesh in `Meshes/KSArmory_MeshAtlas.glb`, declared as a `<SubPart>`, positioned by
writing its transform each frame — which is exactly how the round bodies work, out to a measured
79.5 km from the launcher. So a real torus is available, at the cost of art, XML, transform code,
and living on a vehicle's part tree, which is awkward for a marker that should exist whether or not
a launcher does.

Meanwhile `KsaWorld.DrawTorusEcl` rings solid spheres closely enough to read as a tube, and drapes
each one onto the terrain under it.

**What would unblock it.** A gizmo primitive with a filled surface, or any hook that hands a mod a
command buffer.

---

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
