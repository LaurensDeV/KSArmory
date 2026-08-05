# Blocked on KSA

Things this mod wants and **cannot build**, because the game does not expose what they need. Not a
backlog — `CLAUDE.md`'s *Not done* section is the backlog, and everything in it is ours to do.
Everything here waits on RocketWerkz.

Each entry says what was actually read in the decompiled corpus, so a claim can be rechecked after
a KSA update rather than taken on trust. **Recheck this file when the game moves** — the whole
point of it is that some of these will quietly become possible.

Findings are against KSA **2026.8.5.5168**. Paths are relative to
`../ksa-game-assemblies/current/src`.

## Recheck after a KSA update

Tick when rechecked against the new build, then untick for the next one. `tools/ksa-api-diff.sh`
will not surface any of these — none is a signature change, and most are a call that does not
happen rather than a member that moved.

- [x] Secondary viewport gets the planet, atmosphere and lighting passes
- [x] `Camera.NearbyCelestial` is set per camera rather than only for the frame viewport
- [x] Wheel, suspension or steering module exists
- [x] Partial or component damage exists alongside `DestroyVehicleFromEvent`
- [x] Per-mod vehicle library path, or a way to register saved craft
- [ ] `UncompressedVehicleSave.Load` honours `Character`, making a kitten launchable
- [ ] A character attachment's pose survives the frame, so a mod can aim one
- [x] Custom part modules can be registered without patching
- [x] Public accessor for the volumetric trail renderer

All seven rechecked against 2026.8.5.5168 and still blocked. The render path was refactored in
that build — `Program._offscreenTarget` is now a `RenderTarget` and every line number below
moved — but none of the structure any of these depend on changed.

---

## Secondary viewport: no sky, clouds, atmosphere or terrain

**What we want.** The launcher's electro-optical head drives a second camera window, so the sight
can be watched while flying something else. The head, the tracking, the reticule and the camera
write all work; the *picture* is wrong.

**What happens.** A secondary viewport shows a raw starfield above a hard horizon and a
featureless grey ball, where the main view at the same position shows sky, clouds and terrain.

**Why.** Secondary viewports go through `Program.RenderViewport` (`KSA/KSA/Program.cs:3967-4070`),
a much shorter path than the main one. The loop that calls it ends at `Program.cs:4113`, and every
pass that makes a planet look like a planet is after that line and only ever sees the main
viewport: the planet renderer, the light and shadow passes, the ocean, and
`_planetTransparenciesRenderer.Render` (`:4254`) — the sole call site of the atmosphere and cloud
compute passes anywhere in the game.

Two details worth keeping, because they explain the exact image:

- The starfield is drawn because stars *are* in the reduced path (`Program.cs:4009-4015`).
- The grey ball is not terrain. It is `StaticCelestial.RenderSphere` → `DistantSphereRenderer`, a
  sphere scaled to `MeanRadius` with no heightfield. It appears because
  `Camera.NearbyCelestial` is only ever assigned inside `OnFrameCelestials`
  (`Program.cs:2316-2345`), which runs for the frame viewport — always the main one. The check
  that suppresses the planet you are standing on
  compares `camera.NearbyCelestial == orbiter`, and a secondary camera's is permanently `null`, so
  it never matches. The same `null` zeroes that viewport's lighting data.

**Why a mod cannot fix it.** `PlanetTransparenciesRenderer`, `OceanRenderer` and
`OverallBloomRenderer` are constructed holding `Program._offscreenTarget`
(`Program.cs:1061, 1076, 1085`), which *is* `MainViewport.OffscreenTarget` — the same object
(`Program.cs:1385`). The `Viewport` they accept per call only selects a shader dynamic offset;
the image they write into was baked into their descriptor sets at construction. Redirecting them
means rebuilding Vulkan resources, which is not something a mod can do sensibly even with Harmony.

**What would unblock it**, roughly easiest first:

1. Assign `Camera.NearbyCelestial` per camera rather than only for the frame viewport. On its own
   this removes the wrong grey sphere. Cheap.
2. Array the atmosphere and cloud LUTs by viewport as well as by planet. There is already a
   pattern for this in the codebase — `SunbloomRenderer`'s buffers are sized by
   `Program.ViewportCount`.
3. Let the transparency, ocean and bloom renderers take their render target per call, or hold one
   set per viewport.
4. Run the planet, atmosphere and lighting passes inside the per-viewport loop.

**Note the architecture already supports per-viewport where it was intended to** — `_lightingData`
is indexed by `viewport.Index`, and the sunbloom buffers by viewport count. This is unfinished
rather than impossible.

**Workaround in the mod.** Take over the *main* viewport instead. Fully public API, full render
quality, and it borrows the player's view while active.

---

## Wheels, suspension and steering

**What we want.** The Pantsir sits on an 8×8 chassis. The wheels should turn, steer and carry the
vehicle; ideally it should drive.

**Why it is blocked.** KSA has no wheel, suspension, steering or landing-gear module of any kind.
Searching the whole decompiled corpus for such a type returns nothing, and no Core part declares
one. There is no ground-vehicle physics to attach to.

**What would unblock it.** A wheel or suspension module with a declarable part interface. Until
then the wheels are geometry and the vehicle is placed rather than driven.

---

## Partial damage

**What we want.** A round that lands close enough to hurt but not destroy should degrade the
target — knock out a sensor, break a part.

**Why it is blocked.** KSA exposes only `Universe.DestroyVehicleFromEvent`. There is no component
or partial damage model to drive.

**Consequence in the mod.** Kills are binary. `LethalRadius` destroys, and between lethal and
`BlastRadius` the mod logs a near miss and the target survives. The fuse radii are gameplay numbers
rather than physical ones for exactly this reason — a realistic lethal envelope for a 20 kg
continuous-rod warhead would read as the round doing nothing at all.

---

## A self-contained test scenario

**What we want.** One click that places a launcher, a target and a scenario, so the mod can be
tried without the player assembling anything.

**Why it is blocked.** `LoadVehicleFromLibrary` in a system XML resolves through
`DefaultVehicleSaves`, whose `SaveFolderPath` is **hardcoded** to `Content/Core/defaultvehicles`
under the game install. It is not per-mod and not writable without elevation.

**What would unblock it.** A per-mod vehicle library path, or any way for a mod to register saved
craft with the loader.

**Workaround in the mod.** `tools/install-testcraft.sh` writes a craft into the *user's* vehicle
folder, which is writable, and `TestTarget` spawns drones on demand from the panel.

---

## A launchable kitten

**What we want.** A `KittenEva` on the pad, so the on-foot weapon can be tested without flying a
capsule up and climbing out of it every time.

**Why it is blocked.** The same hardcoded folder as above, reached from a second direction. A
vehicle save becomes a kitten rather than a craft purely because `VehicleSaveData` carries a
`Character` attribute — `VehicleTemplate.CreateInto` branches on it and calls
`KittenEva.CreateKittenEva`. But only `VehicleTemplate` reads that attribute, and
`VehicleTemplate.OnDataLoad` sources its save from `DefaultVehicleSaves.FindSave`. Core's own
kittens live there: `Content/Core/defaultvehicles/Hunter/vehicle.xml` is four lines of XML with
`Character="HunterKitten"`.

The user's vehicle folder is loaded by an entirely different path. `UncompressedVehicleSave.Load`
builds a `PartTree` and returns it, and **never looks at `Character`** — so no craft a mod or a
player can write will ever become a `KittenEva`, whatever its XML says.

The failure is silent and looks like a mod bug. The craft loads as a plain `Vehicle` wearing
`KittenBackPackPart`, whose `<SubPart Id="KittenBackPackSubPart"/>` is declared empty — no model,
no mesh, no material, because a kitten's body is drawn by `KittenRenderable` and that only exists
on a `KittenEva`. So it spawns, is controllable, and is **invisible**. The only clue is KSA's own
log: a working craft logs `finished loading vehicle … part count 1`, and this one stops at
`started loading vehicle`.

**What would unblock it.** `UncompressedVehicleSave.Load` honouring `Character`, or a per-mod
vehicle library path — the same fix as the entry above.

**Workaround in the mod.** EVA a kitten out of a crewed capsule: `EVADoor.CreateKittenEva` is the
only path that builds one, and it sets `Program.ControlledVehicle`, so the battery mounts on it
with `RequireLauncherPart` off. Core's medium capsule carries the doors
(`CoreCommandA_Subpart_MediumCapsuleCrewDoorA`/`B`).

---

## Driving the main camera means unfollowing first

**Not blocked** — recorded because it is a crash, in engine code, with nothing in the message
pointing at the cause.

`FixedController.OnFrame` runs only when the camera it drives is following something, and then
does:

```csharp
double3 cameraRotation = CameraRotation;                       // public field, defaults to zero
double3 vector2 = double3.Cross(cameraRotation, vector).Normalized();
```

A cross product with the zero vector is the zero vector, and normalising that divides by a zero
length. So putting a viewport into `CameraMode.Fixed` while its camera still follows a craft takes
the game down on the *next* frame with `DivideByZeroException` inside
`KSA.Program.OnFrameViewports` — nowhere near the mod that set the mode.

`Camera.Unfollow(changeControl: false)` before the switch avoids it and keeps control of the
vehicle. `KsaWorld.TryLookFromMainViewport` and `TryLookFromViewport` both do this, so no caller
has to remember.

The optical head never met it because the secondary viewport it borrows follows nothing. A player
who sets a secondary view to follow a craft and then enables the optic view on it would have,
which is why the guard is in both.

---

## Aiming a character attachment

**What we want.** The kitten's shoulder gun to point where the mouse points, the way the
launcher's turret and optical head do.

**Why it is blocked.** Not visibility — `CharacterAvatar.Attachments` and its
`CosmeticAttachments` list are both public, and the mesh is a `StaticMeshRenderable` with a
settable `Transform`. The problem is reaching it and, more fundamentally, *when*.

`KittenEva._renderable` is private and `KittenRenderable._characterAvatar` is private inside it,
so the avatar is unreachable without reflection. And reflection would not help, because
`KittenRenderable.UpdateRenderData` writes the transform and submits the draw in consecutive
statements:

```csharp
cosmeticAttachment.Mesh.Transform = cosmeticAttachment.Transform * (float4x7 * boneTransform3);
cosmeticAttachment.Mesh.Draw();
```

That runs inside `Vehicle.UpdateRenderData`, called from `Program.cs:3884` during render — after
the GUI hook a mod gets. Any transform a mod writes is overwritten in the same frame it is read.
This is the difference from the launcher, where the mod writes `Part.SubParts` transforms that
the engine reads later in its own pass.

Nor is there a part to fall back on: a kitten's only part is Core's `KittenBackPackPart`, whose
`KittenBackPackSubPart` is declared with no model at all.

**What would unblock it.** A settable pose on the attachment that survives the frame — an
`ExtraTransform` the renderer composes rather than overwrites — or a public accessor for the
avatar plus a hook between `UpdateRenderData` and the render submit.

**Consequence in the mod.** The gun is a fixed ornament. `Config.MouseAim` still aims the
*battery* at the cursor and rounds leave along that direction, so the weapon works; only the
model does not turn.

---

## Custom part modules

**What we want.** Rounds as real part-based vehicles, with the engine integrating them.

**Why it is blocked.** Registering a custom module type means getting it into engine-internal
update lists, which is not reachable without Harmony patching the engine.

**Consequence in the mod.** Rounds are simulated by the mod and drawn as subparts whose transforms
are written each frame. That is not purely a loss — it gives sub-frame accuracy for free and cannot
corrupt a save — but it is a workaround, not a choice made freely.

---

## Plume trails on mod-simulated rounds

**What we want.** Smoke trails behind the missiles, using the game's own volumetric trail renderer
rather than the mod's gizmo tracers.

**Why it is blocked.** The XML tag is real — `<PlumeTrail Id="DefaultPlumeTrail"/>` inside a
`<ReactionPlume>` — but the emitter only produces anything when
`current.State.DutyCycle > 0f && flag` (`KSA/KSA/Vehicle.cs:5216`), where `DutyCycle` is
accumulated by a **burning rocket core**. Our rounds have no motor, no propellant and no staging,
and a real motor would apply real thrust to the launcher, since the round bodies are its subparts.

The encouraging half: the emitter's position comes from
`state.FxExhaustLocationVehicleAsmb = FxLocationAsmb.Transform(matrix)` where `matrix` is
`Parent.MatrixAsmb2VehicleAsmb` (`KSA/KSA/RocketNozzle.cs:205`) — exactly the matrix this mod
already writes each frame. The plume machinery tracks a moved subpart correctly. Only the ignition
gate is in the way.

**What would unblock it.** A public accessor for the trail renderer.
`VolumetricTrailRenderer.SubmitEmitter` is already `public` and `PlumeTrailEmitterState` is a
public class, so a mod could hold one emitter per round and submit it each frame — no nozzle, no
propellant, no thrust. The only obstacle is that `Program._volumetricTrailRenderer` is a private
field with no accessor, so reaching it today needs reflection.

**Raised with:** blackrack (KSA graphics programmer) suggested the XML tag; the follow-up question
about submitting an emitter directly is outstanding.
