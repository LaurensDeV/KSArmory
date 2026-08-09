# Blocked on KSA

Things this mod wants and **cannot build**, because the game does not expose what they need. Not a
backlog — `CLAUDE.md`'s *Not done* section is the backlog, and everything in it is this mod's own
work. Everything here waits on RocketWerkz.

Each entry cites what the decompiled corpus says, so a claim can be rechecked after a KSA update
rather than taken on trust. **Recheck this file when the game moves** — the whole point of it is
that some of these will quietly become possible.

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
- [x] `UncompressedVehicleSave.Load` honours `Character`, making a kitten launchable
- [x] A character attachment's pose survives the frame, so a mod can aim one
- [x] Custom part modules can be registered without patching
- [x] Public accessor for the volumetric trail renderer
- [x] A post-processing or full-screen shader hook a mod can register into
- [x] **A menu-bar hook a mod can register into** — delete `Ksa/Ui/ModMenuEntry.cs` the day this exists

All eleven rechecked against 2026.8.5.5168 and still blocked. The line numbers below are against
that build's render path, in which `Program._offscreenTarget` is a `RenderTarget`; none of the
structure these entries depend on differs from the build before it. `UncompressedVehicleSave.cs`
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

**Why.** Secondary viewports go through `Program.RenderViewport` (`KSA/KSA/Program.cs:3967-4070`),
a much shorter path than the main one. The loop that calls it ends at `Program.cs:4113`, and every
pass that makes a planet look like a planet is after that line and only ever sees the main
viewport: the planet renderer, the light and shadow passes, the ocean, and
`_planetTransparenciesRenderer.Render` (`:4254`) — the sole call site of the atmosphere and cloud
compute passes anywhere in the game.

Two details explain the exact image:

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

**Why it is blocked.** KSA exposes only `Universe.DestroyVehicleFromEvent`. There is no component
or partial damage model to drive.

**Consequence in the mod.** Kills are binary. `LethalRadius` destroys, and between lethal and
`BlastRadius` the mod logs a near miss and the target survives. The fuse radii are gameplay numbers
rather than physical ones for exactly this reason — a realistic lethal envelope for a 20 kg
continuous-rod warhead would read as the round doing nothing at all.

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

## A launchable kitten

**Wanted.** A `KittenEva` on the pad, so the on-foot weapon can be tested without flying a capsule
up and climbing out of it every time.

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
only path that builds one, and it sets `Program.ControlledVehicle`, so a weapon system mounts on it
with `RequireLauncherPart` off. Core's medium capsule carries the doors
(`CoreCommandA_Subpart_MediumCapsuleCrewDoorA`/`B`).

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

## Aiming a character attachment

**Wanted.** The kitten's shoulder gun to point where the mouse points, the way the launcher's
turret and optical head do.

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

**Consequence in the mod.** The gun is a fixed ornament. `Config.MouseAim` still aims the *weapon
system* at the cursor and rounds leave along that direction, so the weapon works; only the model
does not turn.

---

## Custom part modules

**Wanted.** Rounds as real part-based vehicles, with the engine integrating them.

**Why it is blocked.** Registering a custom module type means getting it into engine-internal
update lists, which is not reachable without Harmony patching the engine.

**Consequence in the mod.** Rounds are simulated by the mod and drawn as subparts whose transforms
are written each frame. That is not purely a loss — it gives sub-frame accuracy for free and cannot
corrupt a save — but it is a workaround, not a choice made freely.

---

## Plume trails on mod-simulated rounds

**Wanted.** Smoke trails behind the missiles, using the game's own volumetric trail renderer
rather than the mod's gizmo tracers.

**Why it is blocked.** The XML tag is real — `<PlumeTrail Id="DefaultPlumeTrail"/>` inside a
`<ReactionPlume>` — but the emitter only produces anything when
`current.State.DutyCycle > 0f && flag` (`KSA/KSA/Vehicle.cs:5216`), where `DutyCycle` is
accumulated by a **burning rocket core**. The mod's rounds have no motor, no propellant and no
staging, and a real motor would apply real thrust to the launcher, since the round bodies are its
subparts.

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

**Outstanding with RocketWerkz.** blackrack (KSA graphics programmer) suggested the XML tag;
whether an emitter can be submitted directly is unanswered.

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

That is fine here, because `SystemConfig.OpticViewport` can be the **main** viewport — and should
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

## Where a structure's surface is

**Wanted.** A cursor over the launch pad to resolve to the top of the pad, so a craft set down
there lands on it rather than through it — and so pointing at the pad's corner is not answered
with the ground beside it.

**Why it is blocked.** The pad is a `LandmarkReference` (`KSA/KSA/LandmarkReference.cs`), which is
a location with an `IsLaunchPad` flag and nothing else: no bounds, no mesh, no collider, no height.
Whatever draws it is not reachable from it.

Terrain itself is fine — `Celestial.GetTerrainHeightFromDirCcf` is public and accurate, and the
cursor already uses it. It is only things standing *on* the terrain that cannot be asked where
their surfaces are.

So `KsaWorld.LaunchPadHeight` adds a flat 8 m within 40 m of a pad landmark. That is a guess at
one pad's height over a circle that does not match its shape, which is why the corner of a large
pad answers with the ground: the corner is outside the circle, and even inside it the height is
assumed rather than measured.

**What would unblock it.** A raycast against static geometry, or bounds on the landmark. The
engine raycasts elsewhere — `Part.RayCastEgo` is public and goes per-triangle against the real
mesh via `Ray.RaycastWatertight` (`KSA/KSA/Part.cs:1943`) — so the machinery exists; it is
reaching a landmark's geometry that has no route.

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
render pass, and **no StarMap hook carries a command buffer** — the five attributes are all plain
method postfixes. Patching a private render method with Harmony would work and is not worth it: a
private method sits outside the API surface `ksa-api-diff.sh` checks, so it breaks silently on a
KSA update that passes every other gate, and an exception inside the render pass takes the game
down with nothing pointing at the mod.

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
