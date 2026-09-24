# KSA modding notes

Everything here comes out of the shipped assemblies of **KSA build 2026.9.10.5438**, read with
`tools/apidump`, or out of the StarMap sources, and was rechecked against **2026.9.22.5482**
wherever that build changed a type this mod uses. KSA is pre-release and unofficially moddable:
none of this is documented by RocketWerkz, and **it will drift between game builds**. Re-run
the dumper rather than trusting this file after an update.

## The stack

| Thing | Value |
| --- | --- |
| Language / runtime | C#, **.NET 10** (the game ships `mscordaccore_..._10.0.*`) |
| Engine | **Brutal** — RocketWerkz's in-house engine (Vulkan renderer, FMOD audio, GLFW windowing) |
| Physics | **BepuPhysics v2** |
| Game assembly | `KSA.dll` (~1100 public types in the `KSA` namespace) |
| UI | Dear ImGui via `Brutal.ImGui.dll`, namespace `Brutal.ImGuiApi` |
| Maths | `Brutal.Core.Numerics` — `double3`, `float3`, `float4`, `doubleQuat`, System.Numerics-style statics |
| Code mod loader | **StarMap** (community) — <https://github.com/StarMapLoader/StarMap> |
| Patching | Harmony (`Lib.Harmony` 2.4.2) |

There is **no official code-modding API**. Part/asset mods are the supported path (XML + GLB +
DDS); everything else goes through StarMap.

## StarMap

Mods live in `Documents/My Games/Kitten Space Agency/mods/<ModName>/` or
`<install>/Content/<ModName>/`. The game must be launched via `StarMap.exe`, not `KSA.exe`.

`mod.toml` beside the DLL:

```toml
name = "KSArmory"

[StarMap]
EntryAssembly = "KSArmory"     # StarMap loads "<EntryAssembly>.dll"
```

StarMap loads that assembly and instantiates **the first type carrying `[StarMapMod]`** — the
class name is irrelevant. It then dispatches to attributed methods. Signatures are validated,
and a mismatch means the hook is silently skipped:

| Attribute | Required signature | When |
| --- | --- | --- |
| `[StarMapImmediateLoad]` | `void M(KSA.Mod mod)` | as this mod finishes loading |
| `[StarMapBeforeMain]` | `void M()` | before the game's main |
| `[StarMapAllModsLoaded]` | `void M()` | after every mod has loaded — do Harmony patching here |
| `[StarMapAfterOnFrame]` | `void M(double currentPlayerTime, double dtPlayer)` | postfix on `OnFrame`, **the per-frame tick** |
| `[StarMapBeforeGui]` / `[StarMapAfterGui]` | `void M(double dt)` | around ImGui rendering |
| `[StarMapUnload]` | `void M()` | teardown |

Mods are loaded into separate `AssemblyLoadContext`s, so dependencies do not collide.

`Console.WriteLine` reaches the KSA console.

### Porting to a different loader

The bound surface is small — six attributes and a `KSA.Mod` parameter, all in one entry class, so
a different loader is a new entry point calling the same methods rather than a rewrite. What does
not port automatically is **when the hooks fire**, and that is load-bearing:

| Requirement | Why |
| --- | --- |
| A hook **between the gizmo reset and the render** | `GizmosRenderer.ResetInstances()` runs near the top of `OnFrame`. Anything submitted outside that window is cleared before it is drawn. `[StarMapAfterGui]` sits there; a hook that only fires after the render draws nothing at all. |
| A hook that can run **on the main thread mid-frame** | Destroying a vehicle mutates a list KSA's solver jobs enumerate on workers, so the barrier in `KsaWorld.WaitForVehicleSolvers` has to be taken from somewhere the scheduler can be joined. |
| Simulated, not player, time available | Everything steps on `Universe.GetLastSimStep()`. A loader that only offers a wall-clock delta is not sufficient — see `SimClock`. |

The first is the dangerous one: a loader without a pre-render hook leaves the mod **compiling,
loading and silently drawing nothing**, with no error anywhere. The fallback there is
Harmony-patching `Program.OnDrawUiViewports` directly, which is what StarMap does for the mod.

**Only the code half needs a loader.** `mod.toml`'s `assets` array, the part XML, meshes and
textures are KSA's own content system: without any loader the part still appears in the editor and
renders, it simply does nothing. A package manager — a CKAN equivalent — sits above all of this
and distributes the archive; it neither loads code nor changes any of the above.

## Key types

### Entry points into world state — `KSA.Program : App`

All static:

```csharp
public static Vehicle? ControlledVehicle { get; set; }     // the vessel the player flies
public static ReadOnlySpan<Vehicle> VehiclesInFrame { get; }  // every loaded vehicle
public static GizmosRenderer GizmosRenderer;              // field; debug line/sphere drawing
public static IGameViewport MainViewport { get; }
public static Camera GetMainCamera();
public static Program Instance { get; }
```

### `KSA.Vehicle : Astronomical` — a vessel

```csharp
double3 GetPositionEcl();  double3 GetVelocityEcl();      // ecliptic, inertial, metres
double3 GetVelocityCce();  double3 GetVelocityCci();      // other frames
IParentBody Parent { get; }                               // body it is bound to
PartTree Parts { get; set; }
float TotalMass { get; }  double MeanRadius { get; }
bool IsDisposed { get; private set; }                     // check before touching a stored ref
string Id { get; }                                        // via IObjectId
doubleQuat Body2Cce { get; set; }
Span<Vehicle> NearbyVehicles { get; }                     // same physics bubble only
static Vehicle CreateVehicle(CelestialSystem, VehicleTemplate, IParentBody, string id);
Vehicle? Split(Part.Connector, double impulse, out PoseChange, string? id = null);
void Teleport(Orbit, doubleQuat?, double3?);
```

### Viewports — an interface and a registry, not a list

**`KSA.Viewport` no longer exists.** As of 2026.9.4.5400 it is split three ways, and a mod binds to
the interfaces rather than to a class:

```csharp
interface IViewport      // Id, ShaderSlot, Type, State, Visible, Mode, Size, Position, GetCamera()
interface IGameViewport : IViewport   // BaseCamera, MapCamera, the five controllers,
                                      //   GetActiveController(), SetCameraMode(), NextCameraMode()
abstract class ViewportBase : IViewport
class GameViewport : ViewportBase, IGameViewport
class PartThumbnailViewport : ViewportBase          // not an IGameViewport
```

`Program.MainViewport` is an `IGameViewport`. What replaced the rest:

| Was | Now |
| --- | --- |
| `Program.Viewports` (a `List<Viewport>`) | `ViewportRegistry.Views` / `.GameViews`, both `ReadOnlySpan` |
| `Viewport.Index` | `IViewport.Id` (a `ViewportId`), and `ShaderSlot` for the render path |
| `Viewport.IsOffscreen` | gone — the thumbnail viewport is simply not an `IGameViewport` |
| `EViewportLightMode` | `ViewportLightMode` |
| `Viewport.FixedController` (public **field**) | `IGameViewport.FixedController`, **get-only** |

That last one is the one that bites: installing a custom `FixedController` was ordinary field
assignment and is now impossible through the public surface. `GameViewport.FixedController` is an
auto-property with a `protected` setter, so the backing field is the only way in — which is what
`KsaWorld.LevelTheHorizon` does, and why it warns and falls back rather than assuming it worked.

Secondary viewports are also **leased** now rather than simply existing:
`ViewportRegistry.TryOpenSecondaryViewport` / `TryClaimSecondaryViewport(IViewportOwner)` hand one
out, `ReleaseSecondaryViewport` gives it back, and `AvailableSecondaryCount` says how many are free.

### `KSA.Universe` — statics

```csharp
static CelestialSystem? CurrentSystem { get; private set; }
static void DestroyVehicle(Vehicle, CrewDisposition = EndMission);
static void DestroyVehicleFromEvent(Vehicle, VehicleDestructionEvent);   // how you kill something; KSA adds its own explosion
static UniverseTime GetElapsedTime();
```

`UniverseTime` is the engine's clock type — an `Int128` of **nanoseconds**, not a `double` of
seconds, so `Seconds()` is a conversion rather than a field read. It has no NaN and no infinity:
`new UniverseTime(double.NaN)` **throws**, and arithmetic saturates at `MinValue`/`MaxValue`
instead of overflowing. Anything carrying a "no time yet" sentinel needs its own flag.

`VehicleDestructionEvent { VehicleDestructionCause Cause; float PeakGLoad; float PeakDynamicPressure; }`
with `Cause ∈ { GroundImpact, OceanImpact, Collision, ExcessiveGForce, AerodynamicForces, HydrodynamicForces }`.

There is **no partial-damage API** — destruction is binary.

### Spawning a vehicle at runtime

`Vehicle.CreateVehicle(...)` alone is **not enough** — it constructs the object but does not put
it in the world. The vehicle registers into `CurrentSystem.All` (so it shows up in enumerations)
yet stays at the frame origin, never moves, and is invisible. Copy what `Vehicle.Split` does:

```csharp
Orbit orbit  = Orbit.CreateFromStateCci(parent, Universe.GetElapsedTime(), posCci, velCci, colour);
Vehicle v    = Vehicle.CreateVehicle(system, body2Cce, bodyRates, parent, id, rootPart, orbit);
parent.Children.Add(v);        // orbiter tree -- without this UpdatePerFrameData never runs
v.AddToBubble(platform.PhysicsBubble);   // physics bubble -- without this it is never simulated
v.UpdatePerFrameData();        // optional: populate the cache now instead of next frame
```

Why the first matters: `CelestialSystem.UpdatePerFrameData()` walks `_all.OfType<IParentBody>()`
and calls `UpdatePerFrameDataTree()` on each **non-orbiter**, i.e. it descends the parent→children
tree. A vehicle that is not in `parent.Children` is never visited, so `Vehicle.UpdatePerFrameData`
never runs, and `GetPositionEcl()` keeps returning its default `_positionEcl` of zero. The symptom
is a vehicle apparently sitting at the solar system barycentre.

`IParentBody.Mu` is a plain property — no need to hunt for the gravitational parameter.

### Per-frame buffers are not readable from a StarMap hook

`Program.VehiclesInFrame` reads back **empty** from `[StarMapAfterOnFrame]`. It is a `FrameSpan`
refilled by `RefreshVehiclesInFrame()` at a point in the tick that does not line up with a Harmony
postfix on `OnFrame`. Enumerate `Universe.CurrentSystem.All` and filter to `Vehicle` instead —
that collection is authoritative and always valid.

Gizmo submission from the same hook does not work either. KSA's whole frame runs inside `OnFrame`:
`GizmosRenderer.ResetInstances()` near the top, then the UI, then the render. A postfix on
`OnFrame` therefore lands *after* the render, and what it submits is cleared by the next frame's
reset before it is ever drawn. `[StarMapAfterGui]` is a postfix on `OnDrawUiViewports`, which sits
between the reset and the render, and is the hook to submit from.

### Reference frames

Suffixes on positions and velocities mean the frame:

| Suffix | Meaning |
| --- | --- |
| `Ecl` | ecliptic, inertial, metres — the common frame, use this for cross-vehicle maths |
| `Cce` / `Cci` / `Ccf` | centred on the current celestial body (equatorial / inertial / fixed) |
| `Bub` / `Phys` | physics-bubble local, used by the Bepu step |
| `Asmb` / `Body` | vehicle assembly and body frames |
| `Ego` | **camera-relative render frame** |

`Ego` is a **pure translation** of `Ecl` — `Camera.EclToEgo(p)` is literally `p - camera.PositionEcl`,
with no rotation. That means you can do all your maths in `Ecl` and convert once at draw time.

Absolute `Ecl` coordinates run to ~1e11 m; `double` still resolves ~20 µm there, so differencing
two world positions is safe.

#### Ecl is absolute

Near Earth, ecliptic **position** sweeps past at ~29.8 km/s and ecliptic **velocity** is dominated
by that same solar orbit. Anything that treats an Ecl value as local is wrong, and the failures
look nothing alike:

| Mistake | What it looks like |
| --- | --- |
| `VelocityEcl` used as airspeed and as a heading | Drag sees Mach 87 and the seeker compares line-of-sight against Earth's orbital vector, so a missile flies 84 km in a straight line, drag-limited to ~1.1 km/s, with seeker lock broken instantly |
| Distance and speed measured against the absolute frame | Telemetry reads "flew 650 km, speed 29 km/s" |
| An Ecl position captured during the frame update differenced against one re-read at draw time | The two are one frame apart, and 29800/60 = 497 m, so the whole gizmo overlay draws ~500 m from the craft |

Rules that follow:

- **Relative quantities are safe.** `targetVel - missileVel` is frame-independent; use those freely.
- **Absolute velocity is never a heading or an airspeed.** Subtract a local frame velocity first —
  the platform's is the natural choice, since it carries the body's orbital *and* rotational motion.
- **Never difference Ecl positions captured at different instants.** Capture one reference at the
  same moment as everything else and difference against that.

`EngagementIsUnchanged_WhenCarriedByAFastMovingFrame` pins the first two mistakes: an engagement
offset by 29.8 km/s must produce an identical result.

#### Drawing gizmos on a craft

Use `camera.GetPositionEgo(vehicle)` as an anchor and add Ecl offsets to it. Do **not** use
`camera.EclToEgo(vehicle.GetPositionEcl())` as an anchor for geometry captured at another time.

Part-relative geometry goes through the part's own transform rather than being rebuilt from a
boresight and an arbitrary perpendicular — the latter gives a correctly-sized ring at a random
rotation:

```csharp
double4x4 asmb2Ego = platform.GetMatrixAsmb2Ego(anchorEgo);
double3 vehicleAsmb = part.PositionVehicleAsmbOffset(localOffset);
double3 ego         = vehicleAsmb.Transform(asmb2Ego);
```

Compiling against `Vehicle`'s transform/mass signatures needs a reference to `BepuUtilities.dll`
(for `Symmetric3x3`).

### Drawing — `KSA.GizmosRenderer`

```csharp
void DrawSphere(double3 positionEgo, float radiusMetres, float4 colour);
void DrawLine(double3 startEgo, double3 endEgo, float4 colour);
```

Reachable as `Program.GizmosRenderer`. This is the cheapest way to render anything custom —
no asset pipeline, no shaders.

### Particle effects — authored emitters, fired by Id

Emitters are **assets**, declared in XML exactly like meshes and materials, and Core's
`Content/Core/ParticleEmitterAssets.xml` is the only documentation the format has. Copy from it.
`<SpawnMode>Burst</SpawnMode>` is an explosion; `<ParticleEmitters>` nests, so one Id can fire
several emitters together.

```csharp
Program.Instance.ParticleSystem.GetAndInitializeEmitters(id, out var handles);   // ModLibrary.Get
foreach (var h in handles) { var e = h.TryGet(); ...; body.AddEmitter(h); }
```

`GetAndInitializeEmitters` resolves through `ModLibrary`, so **a mod's own emitter Id works as well
as Core's** — no editing Core, no borrowing its assets.

Worth knowing:

- **Host it on a `Celestial`, not on a vehicle,** for anything in mid-air. `Vehicle.AddEmitter` and
  `Celestial.AddEmitter` are both public, and the obvious host for a warhead — the target — is the
  thing about to be destroyed. `Celestial.TrySpawnGroundImpact` is the engine's own worked example
  of placing an emitter at a point with no vehicle.
- **With `BubbleFrame.Ccf` the position is `Origin.PositionBub` and `LocalOffset` is ignored** —
  the engine sets `GpuLocalOffset` to identity on that branch and builds the model matrix from the
  origin alone. Setting a transform there looks like it worked and does nothing. Convert Ecl to
  that frame with `(pointEcl - body.GetPositionEcl()).Transform(body.GetCce2Ccf())`.
- **Colour is HDR.** Core's `ThrusterSparks` runs at `(15, 11, 6)`. Values at or below 1 read as
  flat paint; the bloom is what makes a fireball look like one.
- **`Renderer` decides whether smoke reads as smoke.** `SimpleColor` draws each particle as a solid
  mesh, so a cloud of them is a heap of balls whatever the colour. `Volumetric` with a low
  `<Opacity>` (Core's own uses 0.05) accumulates instead — individual particles stop being visible
  and what is left is the density where they overlap.
- **Nest child emitters inline, not by Id.** Core's `Debug_SphericalBurst` composes with
  `<ParticleEmitters Id="Billboard"/>`, but that form from a mod throws *"Invalid renderer type"* —
  the hardcoded message `ParticleSystem` uses when an emitter in the tree has no renderer, i.e. the
  by-Id child does not resolve back to its definition. Inline `<ParticleEmitters>` blocks are the
  form every emitter Core uses in play, and they work.
- **Nothing gates a renderer but `GameSettings.Graphics.Particles`**, and that stops the whole
  system: an emitter resolves, acquires and registers and still draws nothing while it is off.
- **`Billboard` is the soft renderer for a sprite.** It is an alpha-blended camera-facing quad
  (`BillboardParticleFrag`, `BlendColorAlpha`, no cull) sampling a `<MaterialId>`. With a
  soft-edged sprite a cloud of them cannot read as a ball, however many overlap. Use
  `<Mesh Id="Plane"/>`, and `ParticleColor`'s W is its alpha.
- **`Density` is buoyancy, and a stage without one falls at full local gravity** — about 20 m in
  two seconds. It is an attribute on the `<ParticleEmitter>` or `<ParticleEmitters>` element, and
  `ParticleEmitter.ApplyAtmosphereResponse` scales gravity by `1 - airDensity / Density`, clamped
  to ±1: matching the air floats, lighter rises, and Core's metal debris says 7800. **Below 100 Pa
  it counts no air**, so in vacuum every stage falls at full gravity whatever it says. `Drag`
  beside it decays velocity as `exp(-Drag x dt)`, scaled by air density over 1.225
  (`SimpleMovement.comp`).
- **The pool is finite and shared.** `EmitterPool.Get` returns false when not enough emitters are
  free, so a salvo can starve it. Handle the false — an effect is decoration.

`SimpleColor` needs no `MaterialId`; `Pbr` does, and that would be a Core asset Id to keep in step.

### Placing a craft on the ground — `Vehicle.TeleportToLocation`

`TeleportToLocation(Celestial, latDeg, lonDeg)` is public and is how the game moves a vessel. It
builds the kinematic state from the craft's own bounding box and queues it through
`InputEvents.TeleportInputBuffer`, so the hull arrives upright and resting on the terrain. Writing
a position instead puts the craft's *origin* at the point and leaves the rest wherever that falls.

Because it is a buffered engine event that rebuilds the vehicle's orbit and velocity, it is a
once-per-action call. Do not drive it per frame to make a craft follow the cursor.

**It silently stands the craft on a pad** — `GetInitialKinematicStateForLocation` calls a private
`GetLaunchPadHeightAtDirCcf`, which walks `Celestial.BodyTemplate.Locations` for a
`LandmarkReference { IsLaunchPad: true }` and adds that landmark's static object's
`GroundOffset + SurfaceHeight` within its `FootprintRadius`. The numbers are declared data rather
than constants — Core's `CoreLaunchPadA_Prefab_LaunchPadA` is 0.2 m + 1.5537 m over a 108.3 m
circle — and `LocationReference.GetStaticObject()` is public, so anything drawing a preview marker
can read the same figures instead of copying them.

### Aiming the player's camera — `OrbitView`, not `OrbitController`

Writing `Camera.LocalRotation` does nothing lasting: every viewport runs a controller that rebuilds
its camera each frame. `IGameViewport.SetCameraMode(CameraMode.Fixed)` does hold, and is how a
*secondary* viewport is driven — but on the main one it takes the view off the player and hides the
interface, and `FixedController.OnFrame` divides by zero if the camera is following anything, so
`Unfollow(changeControl: false)` has to come first.

To turn the player's own view, move the orbit angles the controller is already reading. They exist
in two places and **only one is writable**:

| | |
| --- | --- |
| `Camera.Following.OrbitView.Azimuth` / `.Elevation` | the stored angles — **write these**; a mouse drag moves the same fields |
| `OrbitController.Azimuth` / `.Elevation` | an **output**, resprung towards the stored pair every frame (`SpringInterpDriven`, 0.12 s) |

Writing the controller's pair survives one frame and then fights the spring, which on screen is
jitter rather than motion. Read them, though: they are what built the camera basis this frame, so
they are the angles to solve against. Elevation is clamped to ±π/2 by the game and should be
clamped on write too.

The frame the angles are measured in is private (`GetFrame2Ecl`), but it need not be — the
controller builds the camera's basis out of it:

```csharp
horizontal = frameX rotated about frameZ by Azimuth;
right      = normalize(cross(horizontal, frameZ));   // == Camera.GetRightEcl()
forward    = horizontal rotated about right by Elevation;
```

so undoing the elevation about the camera's right recovers the horizontal, and `cross(right,
horizontal)` recovers the frame's vertical. That is everything aiming needs. `Sim/OrbitAim.cs` has
the inversion, with the construction above reproduced in `OrbitAimTests` as a round trip.

### ImGui — `Brutal.ImGuiApi.ImGui`

`ImString` has an implicit conversion from `string`, so plain literals work.

```csharp
bool Begin(ImString name, ref bool pOpen, ImGuiWindowFlags flags = None);
bool Begin(ImString name, ImGuiWindowFlags flags = None);
void Text(ImString);  void TextDisabled(ImString);  void TextColored(in float4 col, ImString);
bool Button(ImString label, in float2? size = null);
bool Checkbox(ImString label, ref bool v);
bool SliderFloat(ImString label, ref float v, float min, float max, ...);
bool SliderInt(ImString label, ref int v, int min, int max, ...);
bool TreeNode(ImString label);  void TreePop();
void ProgressBar(float fraction, in float2? size = null, ImString overlay = default);
void Separator();  void SameLine(float offsetFromStartX = 0, float spacing = -1);  void End();
```

## Parts and the module system

Parts are declared in XML under `<install>/Content/Core/*.xml`, paired as
`<Name>Assets.xml` (meshes, textures) and `<Name>GameData.xml` (simulation). Models are GLB;
textures are `.ktx2` (or `.png`, or `.dds`) with a packed **ORM** map — see below.

The simulation model is unusually physical — engines are real combustion chambers and De Laval
nozzles rather than thrust curves:

```xml
<PartGameData Id="CorePropulsionA_Prefab_EngineA2" DisplayName="LR91 Sea">
  <EditorTag Value="Engines" />
  <Diameter M="1"/>
  <RocketEngineController Id="LR91-AJ-3">
    <RocketReference Id="Engine" SubPartId="..." />
  </RocketEngineController>
  <Combustor Id="GasGeneratorChamber">
    <Reaction Id="Hydrolox"><MixtureRatio>5.5</MixtureRatio></Reaction>
    <MaxPressure Bar="49" />
  </Combustor>
  <SolidSphereMass><Mass Kg="1500" /><Radius M="0.25" /></SolidSphereMass>
  <Collider Id="Collider1"><Cylinder .../><Sphere .../></Collider>
</PartGameData>
```

Each XML node maps to a C# pair:

- `FooReference : SerializedId, ILibraryData` — the deserialised template, fields typed as
  `RadianReference`, `BoolReference`, `TransformReference`, …
- `Foo : ModuleStateful<...>` — the runtime module, with
  `static void UpdateModules(ref ModuleUpdateContext)`,
  `static void CreateComponents(Part, PartTemplate, PartInstance)` and `CreateStates(...)`.

`KSA.Gimbal` / `KSA.GimbalReference` is the smallest complete example to copy.

**Registering a new module type with the engine's hot-path updater is not solved here.** The
registration lists are internal, so a genuinely new part module needs Harmony patching into
them. Simulating the behaviour from a StarMap hook instead avoids all of that, and is what this
repo does — from `[StarMapAfterGui]` rather than the frame hook, because a postfix on `OnFrame`
lands *after* the render it was meant to feed. See `docs/FRAMES-AND-EPOCHS.md`.

### What lets a part start a craft, and what lets one be bolted to

Three separate gates in `VehicleEditor`, none of which fails loudly. A part that trips one is
simply greyed out or skipped, with nothing in any log.

**Starting a craft** — `IsAllowedAsRootPart`, reached from `editor.IsEmpty && !IsAllowedAsRootPart(part)`,
which is what greys a part out when the editor is empty:

```csharp
if (EditorTag.MatchAny(part.EditorTags, _rootPartWhitelist))
{
    if (part.Connectors.Count == 0) return false;
    foreach (var connector in part.Connectors)
        if (IsSet(connector.Flags, 4) || IsSet(connector.Flags, 2))   // FromSurface | ToSurface
            return false;
    return true;
}
return false;
```

So it needs a tag whose `EditorTagDef` carries `RootPartWhitelist` — `Capsules`, `Engines` and
`Interstage` are the built-ins, and a mod's own tag can set it — **and** at least one connector,
**and** not one single `ToSurface` or `FromSurface` connector among them. The last is absolute
and beats the tag: **a radially-attached part can never be a vehicle root.** Choose one.

**Being bolted to** — `HandleSnapping` skips any candidate where
`!faceSnapTargetWhitelist || faceSnapTargetBlacklist`. **The blacklist wins**, so Core's `Radial`
tag (`FaceSnapTargetBlacklist`) silently cancels a whitelisted tag on the same part and nothing
can be mounted on it.

**Two different routes onto a surface**, and the one taken changes the orientation:

| Route | Taken when | Aligns |
| --- | --- | --- |
| `ToSurface` connector | the part has one | the **connector's −X** to the surface normal |
| face snapping | it has none, and no `FaceSnapBlacklist` tag | the **part's −Z** (`alignDirectionPartAsmb ?? (0,0,-1)`) |

A `ToSurface` connector *suppresses* face snapping — `HandleConnectorConnections` runs first and
the loop after it returns early. So dropping the flag does not stop a part attaching, it switches
it to the other route and a different axis: for a hull modelled with +X up, that lays it on its
side. `NoFaceSnapping` (Core's tag, `FaceSnapBlacklist`) is what turns the second route off.

`Diameter` plus a `DiameterFilterlist` tag is what makes a part appear under a given stack size.

### Removing a subpart breaks every save holding that part, and kills the process

**A saved part is paired with its current definition by *position*, and the loop is bounded by
the save while it indexes the definition** — `KSA.PartTree.Deserialize`:

```csharp
for (int i = 0; i < nextNode2.SubPartInstances?.Count; i++)
{
    Part part2 = nextNode.SubParts[i];                     // the definition, as it is now
    PartInstance partInstance2 = nextNode2.SubPartInstances[i];   // the save
```

So the three edits are not symmetric:

| Edit | Result |
| --- | --- |
| **add** a subpart | fine — the loop stops at the save's shorter count, and the new one starts unconfigured |
| **rename** a subpart | fine — `InstanceOf` is written into the save and never read back |
| **remove** a subpart | `IndexOutOfRangeException` on load, **every time**, for every save holding it |

And it is not survivable. `UncompressedSave.Load` runs from `Popup.DrawAll` inside
`OnDrawUiFrame`, and nothing between there and `Program.Main` catches it, so the game does not
refuse the save — it terminates. There is no version field, no name match and no warning; the
same positional pairing means a **reorder** silently applies one subpart's saved state to
another.

Craft files in the vehicle library are unaffected: they store no `<SubPartRef>` at all.

**So a shipped part's subpart list is append-only.** Before removing one, either accept that
existing saves die, or leave the `<SubPart>` declared as an inert stub to hold the count.
`tools/repair-saves.py` drops the surplus entries from saves written before a removal, which is
the fix for a mod still in development; it reads the current definitions out of the asset XML, so
it needs no record of what changed.

### A mod's character ends up in nearly every save, and cannot be taken back out

A roster is dressed from every `<Character>` registered, a mod's included — `KittenRosterData`
draws from `ModLibrary.AllCharacters` — so about a quarter of the kittens in any save made while a
mod declaring one was installed wear it. The save records the Id on each `<Kitten>`, and on a
`<Vehicle>` for one out on EVA, and `KittenEva.CreateKittenFromSaveData` resolves it through
`ModLibrary.Get`, which throws on an Id nothing declares — inside the same uncaught load as above.
So removing a character terminates the game on nearly every save, not just ones holding a part.
`tools/repair-saves.py` re-dresses those kittens in Core's own characters.

### Mods load in manifest order, and Core leads only if the game wrote the manifest

`ModLibrary.LoadAll` loads each mod's asset XML in the order the local `manifest.toml` lists them,
and a `<Character>` resolves Core's `CharacterCore` **while its file loads**
(`CharacterReference.OnDataLoad`). `PrepareManifest` copies the game's `Content/manifest.toml`,
Core first, only when there is no local manifest at all; one that exists without Core gets it
**appended**. A manager that writes a fresh manifest listing only the mods it installed — Borea
0.1.0 does — therefore loads every mod before Core, and a mod declaring a character throws
`CharacterCoreReference is null for 'CharacterCore'` and takes the game down. `mod.toml` has no
dependency or ordering field, so a mod cannot ask to load after Core. A sound's channel and an
exhaust template are resolved later, once every mod has loaded, and survive the reorder.

## Authoring a part with no new art

**Asset Ids resolve in one global library across mods** (`SerializedId` / `ILibraryData`, with
an `_isReference` flag for entries that are pure references). Core loads before user mods, so a
mod's XML can instance Core's subparts and materials by Id — **no mesh atlas, no textures, no
Blender**.

**Confirmed in-game.** A mod's `<SubPart InstanceOf="CoreStructuralA_Subpart_TubeA">` renders
with Core's material, shipping no art at all. That is the right answer for anything that can be
assembled out of Core's kit. `tools/validate-parts.py` checks every reference resolves.

## Shipping your own art

When the part cannot be assembled out of Core's kit, mirror Core's own file exactly:

```xml
<Assets>
  <MeshAtlas Path="Meshes/MyMod_MeshAtlas.glb" />

  <PbrMaterial Id="MyMod_Material">
    <Diffuse      Path="Textures/MyMod_Diffuse.png" Category="Vessel" />
    <Normal       Path="Textures/MyMod_Normal.png"  Category="Vessel" />
    <AoRoughMetal Path="Textures/MyMod_PBR.png"     Category="Vessel" />
  </PbrMaterial>

  <SubPart Id="MyMod_Subpart_Thing">
    <PartModel Id="MyMod_Subpart_Thing_Model">
      <Mesh Id="MyMod_Subpart_Thing" />          <!-- a mesh name inside the atlas -->
      <Material Id="MyMod_Material" />
    </PartModel>
    <MeshView><Mesh Id="MyMod_Subpart_Thing_VM" /></MeshView>
  </SubPart>

  <Part Id="MyMod_Prefab_Thing">
    <SubPart Id="MyMod_Thing_1" InstanceOf="MyMod_Subpart_Thing" />
  </Part>
</Assets>
```

- **`.png` works** for every material slot — no `.ktx2` encoder needed.
  `CharacterAssets.xml` mixes `.ktx2` and `.png` inside a single `<PbrMaterial>`.

### How the atlas loader reads a `.glb`

From `MeshAtlasFileReference.cs`. None of this is documented anywhere, and three of the four are
silent failures.

- **Mesh Ids are a single global namespace**, shared with Core and with every other loaded mod.
  On a collision the loader keeps whoever registered first and **yours simply never loads** — no
  error, no log line. Prefixing every mesh with the mod's own name is not tidiness, it is the
  only thing standing between your turret and someone else's.
- **The Id is the glTF *mesh* name, not the node name.** Renaming the object in Blender without
  renaming its mesh data changes nothing; renaming the mesh data breaks the XML.
- **The node graph is never walked.** The loader takes mesh data and ignores the scene hierarchy,
  so a parent transform, an armature or a nested empty contributes nothing. Geometry has to be
  *baked* into the mesh — that is a requirement, not a style preference.
- **Every mesh in the file is registered**, including helper geometry. The `_` prefix that skips a
  mesh belongs to `KSA.GlbImport`'s bundlers — the authoring tool that writes asset XML from a
  `.glb` — and `MeshAtlasFileReference` does not honour it, so a helper mesh in a shipped atlas
  takes a global Id like any other.

A consequence worth stating separately: because node transforms are not read, **an atlas is a
library of bodies in their own local frames**, and placement lives entirely in the part XML's
`<Transform><Position>`. That is what makes a pose reconstructible outside the game — see
`tools/model/checkswept.py`.
- **`AoRoughMetal` is R=occlusion, G=roughness, B=metalness** (glTF ORM). Core's own
  `Textures/default_pbr.png` is `(255, 180, 0)` and `EmptyAoRoughMetallic.png` is
  `(255, 255, 0)`: unoccluded, rough, non-metal.
- A slot can reference a `<Texture Id>` from `DefaultAssets.xml` instead of a path —
  `<Normal Id="EmptyNormal"/>`.
- **Put the Assets XML at the mod root**, next to `Meshes/` and `Textures/`. Whether relative
  paths resolve against the mod root or the XML's directory is undocumented; at the root the
  two are the same and the question is moot.
- **Bake positions into the mesh** if the part is bespoke — apply transforms, export at
  identity, and place the subpart with no `<Transform>`. Core's meshes are origin-centred
  because they are reusable pieces, which is a different problem.
- **glTF file axes are the game's part axes.** Export with Blender's Y-up conversion *off*, or
  the model arrives with Y and Z swapped.

`tools/model/` in this repo does all of the above from a script; its README has the details.

A mod is a folder with `mod.toml`. The same folder serves as both a content mod and a StarMap
code mod:

```toml
name = "KSArmory"
assets = [ "MyModAssets.xml", "MyModGameData.xml" ]

[StarMap]
EntryAssembly = "KSArmory"
```

**Assets XML** — appearance and layout. `<Part>` places `<SubPart>` instances:

```xml
<Part Id="MyMod_Prefab_Thing">
  <SubPart Id="MyMod_Base" InstanceOf="CoreStructuralA_Subpart_MountingNodeHalfWA" />
  <SubPart Id="MyMod_Tube1" InstanceOf="CoreStructuralA_Subpart_TubeA">
    <Transform>
      <Position X="0.522" Y="0.26" Z="0" />
      <Rotation Z="3.14159" />          <!-- RADIANS, not degrees -->
      <Scale X="0.5" Y="0.5" Z="0.5" />
    </Transform>
  </SubPart>
  <Connector Id="_myConnector">
    <Transform><Position X="-0.095" /><Rotation Z="3.14159" /></Transform>
  </Connector>
</Part>
```

**GameData XML** — simulation. Minimal is genuinely minimal; `CoreStructuralA_Prefab_StrutA` is
nothing but an `EditorTag`. Mass and colliders are optional and derived from geometry if absent.

```xml
<PartGameData Id="MyMod_Prefab_Thing" DisplayName="Shown in the editor">
  <EditorTag Value="Structural" />
  <EditorTag Value="Radial" />        <!-- allows surface attachment -->
  <Diameter M="0.5" />                <!-- 0.5 / 1 / 2 / 3 / 4 -->
  <Connector Id="_myConnector"><Flags>ToSurface</Flags></Connector>
  <SolidSphereMass><Mass Kg="185" /><Radius M="0.30" /></SolidSphereMass>
  <Collider Id="MyCollider">
    <Cylinder Id="C1">
      <LocationAsmb X="0.522" Y="0" Z="0" />
      <Collider2Asmb X="0" Y="0" Z="1.5708" />   <!-- cylinders are Y-axis; rotate onto X -->
      <LengthY M="0.854" /><Radius M="0.32" />
    </Cylinder>
  </Collider>
</PartGameData>
```

Conventions worth knowing:

- **X is the part's forward axis.** Engine exhausts point down −X.
- **Rotations are radians.** `3.14159` = 180°, `1.5708` = 90°.
- **Colliders**: `Cylinder` is Y-aligned by default, `Collider2Asmb` rotates it. Also `Box`
  (`LengthX/Y/Z`) and `Sphere` (`Radius`).
- **Connector flags**: `ToSurface` / `FromSurface` for surface attachment, `Internal` to hide,
  `Capabilities` for `BulkFluid`, `DecouplerJoint`, etc.
- **EditorTag values in use**: `Structural`, `Radial`, `Coupling`, `Engines`, `Fuel Tanks`,
  `Booster`, `Capsules`, `Cargo`, `Electrical`, `Interstage`, `Landing`, `Lights`, `Passage`,
  `RCS`, `Hidden`, `NoFaceSnapping`.
- Sub-meshes named `*_VM` are the editor preview ("mesh view") variants.

Measure atlas meshes before laying anything out — glTF stores per-accessor min/max, so bounds
come for free:

```bash
./tools/meshinfo.py "$KSA/Content/Core/Meshes/CoreStructuralA_MeshAtlas.glb" Tube
```

Handy Core subparts: `CoreStructuralA_Subpart_TubeA` (0.854 × 0.101 tube),
`..._MountingNodeHalfWA` / `_1WA` / `_2WA` (mounting discs), `..._Endcap*`, `..._TrussA`,
`CoreFairingA_Subpart_NoseconeBase{Half,1,2,3}MSkinA` (0.5 / 1 / 2 / 3 m nosecone bases, with
`Nosecone{Ballistic,Blunt}{1,2}MSkinA` for the cones themselves). Materials follow
`<Category>_Material`, e.g. `CoreStructuralA_Material`.

**Not solved**: rendering a mesh at an arbitrary runtime position (for mod-simulated objects
rather than parts on a vehicle). `KSA.StaticMeshRenderable` has a public constructor and a
`Transform` field, but needs `IMeshRenderer<InstanceData>` instances owned by the engine's
render systems.

## Character attachments

Nothing in this mod ships one any more. These are the engine's rules, and they cost an
afternoon each to find.

### A character attachment is authored in centimetres, a part in metres
 The kitten is drawn
through `CharacterAvatar.Core.Scale = 0.01`, and `GetBoneTransform` returns a bone matrix that
already carries it — so a mesh exported in metres arrives a hundred times too small. Core's own
attachments measure 80.6 glTF units (helmet) and 48.3 (MMU); a mesh at metre scale renders a
hundredth of that and is buried in the fur: it loads, registers, draws every frame, is
invisible, and puts nothing in any log.

**And the scale must be baked into the vertices.** `StaticMeshRenderable.Draw` writes one instance
transform per asset and never reads the glTF's node transforms — `GltfPbrAssetRef.SceneGraph` is
assigned and never read anywhere in the engine. A scale left on the Blender object is silently
discarded. A generator has to apply the scale to the object and then bake it into the
vertices.

**An attachment's axes are composed in a different order from the body's.** The body gets
`RotX(-90) * RotZ(-90)` applied *after* the scale (`KittenRenderable:106`); an attachment gets
`RotZ(-90) * RotX(-90)` applied *before* the bone matrix (`:354`). So a mesh that is the right
size can still arrive rotated, and the `<Rotation>` in the attachment XML is where that is
corrected.

**Keep an attachment to one mesh with one primitive.** `GltfPbrSystem` aliases the index buffer
across primitives and then disposes it (`:102` against `:112`), so the second primitive frees a
list the first still points at. One mesh is the only shape that is not walking on freed memory.

**Nothing else in that pipeline fails quietly.** A bad material Id, a missing bone, a null material
slot and a failed asset load all throw, and `AssetManager.GetOrLoad` rethrows rather than
swallowing. The only silent no-draws are `Visible == false` and a glTF with no mesh primitives. So
an attachment that is present but unseen is a *geometry* problem — wrong units, wrong winding, or
wrapped around the camera — not a materials or registration one.

## Sound: reachable, and shipped the same way art is

A mod can make a noise, on every axis that matters.

**The API is public and imperative.** `KSA.GameAudio` exposes `PlaySound(SoundEvent, SpatialAudio,
out IChannel?, IAudio? parent, float volume, bool startPaused)` as a static, plus `Register(IAudio)`
so a mod object can be driven by the engine's own `UpdateAudio` pass. `CreateFmodSound` takes a
`SoundFileReference` directly. Underneath is **FMOD**, via `Brutal.Fmod`.

**`SpatialAudio` is in Ego, and carries velocity and pressure.** Its constructor is
`(double3 posEgo, double3 velEgo, double atmosphericPressure)`. So it wants the same frame the mod
already converts to for drawing — `KsaWorld.TryEclToEgo` — and *needs* the velocity, which makes
Doppler the engine's job rather than the mod's. Pressure is a parameter, so thinning air is
modelled.

**A mod can ship its own audio, by relative path, exactly like a mesh atlas.** `Core/Sounds.xml`
declares `<SoundFile Path="Sounds/EngineDefault.wav">` alongside `<SpatialSoundData>`,
`<SoundGroup>` and `<SoundBehavior>`, and the files are plain `.wav` and `.ogg` under
`Core/Sounds/`. That is the same shape as `<MeshAtlas Path="Meshes/…">`, which works from a user
mod — so the loader contract is known-good.

**The declarative route is for parts only.** Core hangs engine noise off
`<SoundEvent Action="On" SoundId="DefaultEngineSoundBehavior" />` inside `<PartGameData>`, driven
by the part's engine module. A self-simulated round is not a part with an engine, so that path is
closed and `GameAudio.PlaySound` is the one to use.

## Explosions — KSA's own, from a point with no vehicle

`ExplosionSystem.SpawnPreset(id, in ExplosionContext)` fires a declared `<Explosion>` with its
flash, sound, volumes and emitters. Core ships `PopSmallExplosion`, `MetalBurst`, `SmallFire` and
`Explosion_Conflagration` in `Content/Core/ExplosionAssets.xml`; `Ksa/Detonation.cs` is the worked
example.

- **Leave `Vehicle` null and set `Anchor`**: a `BubbleOrigin` on a `Celestial`, `BubbleFrame.Ccf`,
  with the body-fixed point as `PositionBub`. With no vehicle the anchor is used as given, and a
  stage whose anchor's parent is not a `Celestial` is dropped.
- **Size is `IntensityJ / 5e10`, clamped to 0.05–100, as its cube root.** A 66 kg warhead is 0.0055
  and goes off at the floor like everything else conventional, so pick the preset for size.
- **`AmbientPressurePa` gates the stages.** Debris has air and vacuum variants split at 500 Pa, and
  `PopSmallExplosion`'s smoke needs 5 kPa. Zero is vacuum.
- **Main thread only**, or the request is dropped with a warning. Above 1200x warp the emitters and
  volumes are skipped, and the flash and the sound still play.
- **A volume needs an atmosphere, clouds and upscaling**, or its `FallbackEmitterId` fires instead.
  The flash takes one of eight point-light slots shared with the engine.
- **`DestroyVehicleFromEvent` and `PartFailureEvent.Apply` spawn their own**, routed by propellant
  mass, so a mod's kill gets an explosion whether or not it asks for one.
- **A missing Id warns in KSA's log, rate-limited, and nowhere else.** Ask
  `ModLibrary.TryGet<ExplosionReference>` at load.

## Particle emitters can follow something that moves

A one-shot emitter pinned to a body-fixed point is the simple case, but the emitter is not limited
to that. `emitter.Context` has `Astronomical`, `Vehicle` **and** `Part` fields, and
`emitter.Origin` is a `BubbleOrigin` carrying `BubFrame`, `PositionBub` **and `VelocityBub`** —
so a continuously-spawning emitter re-anchored each frame is expressible. `SpawnRate`,
`MaximumParticleCount` and `ParticleInfo.Lifespan` are all writable.

**The trap to expect is spacing, not attachment.** A round at 840 m/s covers 14 m per frame at
60 fps, so an emitter spawning once per frame at the round's current position draws a dotted line
of puffs rather than a plume.

`VelocityBub` does **not** smear the spawn and cannot be made to — see the last section of this
file for why. What works is moving the emitter itself: the particles are left behind at the
positions it occupied, and the frame's travel becomes the streak rather than a gap in one.
`Ksa/TracerTrail.cs` and `Ksa/MotorPlume.cs` are both that shape, and they pay for it with one
pooled emitter per moving thing — which is why the tracer only decorates a few shells at a time.

## Threading

`[StarMapAfterOnFrame]` runs on the main thread (Harmony postfix on `Program.OnFrame`), so
gizmo submission and `Universe.DestroyVehicleFromEvent` are safe from there. Vehicle physics
itself runs on worker threads via `VehicleUpdateTask`; do not mutate vehicle state from those.

Never destroy vehicles while iterating `Program.VehiclesInFrame` — copy to a list first.

## Held controls are cleared on the controlled vehicle while the UI has the keyboard

`Vehicle.ProcessInput` sets bits — `EngineFlags.ThrottleUp`/`ThrottleDown`, `ThrusterCommandFlags` —
that persist until released, and `Vehicle.PrepareWorker` moves them into the vehicle's manual inputs.
Its first act is:

```csharp
if (Program.ControlledVehicle == this && (!Program.IsControlledVehicleActive
    || ImGui.GetIO().WantCaptureKeyboard || Universe.GetSimulationSpeed() > 30.0))
{
    ClearHeldPlayerInput();   // thruster flags, sprint, grab, engine flags
}
```

So anything written through the keyboard channel is discarded on the craft being flown whenever an
ImGui window has the keyboard, and every other vehicle keeps it. A Harmony prefix on `PrepareWorker`
does not escape it: the clear runs inside the body, after the prefix.

`VersionInfo.CheckOnLaunch` runs at every launch whatever `[system] checkForUpdates` says — that setting
only gates the menu's "Check for Update" — and a newer server version raises `UpdateAvailablePopup`, a
console modal that stays until a button is clicked. Popups sit on `Popup`'s private static `Popups`
list, `Popup.AnyOpen` is public, and setting a popup's public `Active` to false is how its own buttons
close it.

## Dents are the engine's, and a mod can make one

KSA deforms part meshes itself (`FxDeformation`, `KSA.Deformation.DentField`): up to ten dents a
part, set by the Impact Dent Quality graphics setting, merged when they land on one another, and
saved with the craft. Its collision code reports them through **`FxDeformation.ReportContact(part,
centreOfMassAsmb, pointBody, normalBody, impulsePerArea, area)`**, which is public and static:

- The point is `pointBody + centreOfMassAsmb`, so passing a zero centre of mass takes the point in
  the vehicle's assembly frame. The normal is the push, into the part, in that same frame.
- `impulsePerArea / 0.01` is the pressure (`PartStructuralLimits.AccumulatedPressure`), compared with
  the full part's `CrashTolerancePascals`: nothing under half of it, and a depth of 0.099 of the
  part's scale radius per unit of the ratio, capped at 0.15.
- The footprint's radius is `2.29 · sqrt(area / π)`, clamped to 0.314–0.709 of that scale radius.
- It returns silently if the Impact Dents setting or `FxDeformation.Shared.Enabled` is off. It
  enqueues under a lock and the vehicle's own module update drains the queue, so it can be called
  from a mod hook; `FxDeformation.Shared.TotalReported` counts what it accepted.

`KsaWorld.ReportBlastDents` is the worked example. The mesh editor's **Add test dent** is the other
way in, `FxDeformation.DebugImpact`, and places one at random.

## A compute pass can read KSA's weather shadows, but not through KSA's set

`Program.GetCloudShadowsRenderer().GetDescriptorSet(body)` is the set the terrain, the ocean and static
objects shade by the weather through (`Clouds/CloudShadows.glsl`), and binding it to a compute pass
compiles, validates nowhere visible and reads **garbage**: its three bindings are declared with
`StageFlags = FragmentBit` alone, so from a compute shader every float came back NaN and the layer
count as a random integer. A set cannot be bound to a pipeline whose layout differs from it in stage
flags, so there is no widening it from outside.

What works is taking its two buffers, not its set — `CloudShadowRenderData._staticShadowDataBuffer`
and `_dynamicShadowDataBuffer`, private, off the renderer's private `PlanetToCloudShadowData` keyed by
`Celestial.Hash`, or `_noShadowData` for a body with none — and handing them to
`ComputePipelineWrapper` as `uniformBuffers` and `uniformDynamicBuffers`, which it declares for
compute in its own set 1, after the images. The dynamic one takes
`ResourceFrameIndex * CloudShadowRenderData.DynamicUboStride`, passed as the first external dynamic
offset because offsets are consumed in set order. The coverage textures are in KSA's bindless set,
`Program.Instance.TextureSystem`, which **is** declared for compute and binds as an external set.
`KsaWorld.TryWeatherShadowBuffers` and `CloudPass.Build` are the worked example; `CloudShadows.glsl`
itself uses derivatives, so the lookup is rewritten at a fixed mip.

## KSA's weather distance is one number for every layer, and the nearest of its neighbours

`CloudRenderer`'s distance image (`GetLowResolutionCloudDistanceTarget`, and the upscaled pair it is
accumulated into) holds **one** distance per pixel, in kilometres, for all the layers together:
`RaymarchCloud.comp` marches them front to back and blends each into the last with
`cloudDistance = mix(previous, current, currentOpacity / (previousOpacity + currentOpacity))`. The
colour's alpha is likewise the layers' transmittances multiplied. So over Earth, where cumulus from
2 km and cirrus from 11.0 to 12.2 km both lie along a downward ray, the distance lands **between**
them, by how opaque each is. Anything standing between the two layers cannot be placed against it:
treated as one sheet at that distance, a mushroom cap poking through the cirrus read as behind the
whole opaque deck wherever there was cirrus, and dropped out in holes.

Then the upscaler (`Upscaling/UpscalingFunctions.glsl`) writes each full-resolution pixel the
**minimum** distance over the 3x3 low-resolution texels round it, and a texel is up to four pixels
(`SetUpscalingMultipliers`, 2x1 to 4x4). A wisp of the near layer therefore pulls a square round
itself forward, which draws as boxes.

What recovers it: the layers' own radii are in the weather-shadow buffers above (`bottomRadius`,
`middleRadius`, the top being symmetric), so each stretch of the ray **inside** a layer's slab can be
placed exactly -- between its bottom and top spheres, not at its middle's crossing, which a ray
skimming the top half never makes. The blended distance then says only how the opacity **divides**
between the two stretches it falls between, measured between their facing edges (a deck seen from
above averages at its tops). Read as the farthest within five pixels, it undoes the minimum.

**And a deck the eye is inside is thickest at the eye.** The stretch then starts at the camera and
near the horizon runs hundreds of kilometres, so its opacity spread evenly along it put most of a fog
bank behind a burst 95 km off, which showed through fog that had hidden the ground. For fog thinning
exponentially the opacity-weighted mean distance is its scale, so that stretch builds up as
`exp(-t / averaged distance)` instead. `PlaceWeather` in `Shaders/KSArmoryCloud.comp` is the worked
example.

## Re-running the research

```bash
./tools/sync-import.sh                                   # refresh Import/ from the game
cd tools/apidump
dotnet run -- ../../Import types                         # every public type
dotnet run -- ../../Import grep Vehicle                  # find types by name
dotnet run -- ../../Import members KSA.Vehicle           # fields, properties, methods
```

For method bodies, `ilspycmd` works but needs a .NET 10 runtime and chokes on `--list-types`
for `KSA.dll`; use `-t <TypeName>` for a single type instead:

```bash
dotnet tool install --global ilspycmd
ilspycmd -t KSA.Camera Import/KSA.dll
```

## Sources

- StarMap loader — <https://github.com/StarMapLoader/StarMap>
- StarMap example mods — <https://github.com/StarMapLoader/StarMap-ExampleMods>
- Community modding wiki — <https://modding.kittenspaceagency.wiki/>
- Official wiki, part modding — <https://kittenspaceagency.wiki.gg/wiki/Help:Modding>
- SpaceDock (KSA mods) — <https://spacedock.info/ksa>
- Forums — <https://forums.ahwoo.com/>

## A body's primary is `IParentBody`, and testing for `Celestial` compiles and answers nothing

`Celestial.Parent` is declared as **`KSA.IParentBody`**, not as `Celestial`. So this:

```csharp
if (body.Parent is not Celestial primary) return Vec.Zero;   // wrong, and silent
```

compiles, reads like a null check, and returns zero for every body in the game. The interface is
where the useful members are anyway — `Mu`, `Mass`, `SphereOfInfluence`, `BodyTemplate`, and it
implements `IPosition`, so the position comes off it too:

```csharp
if (body.Parent is not { } primary) return Vec.Zero;         // IParentBody
double mu = primary.Mu;
double3 toPrimary = primary.GetPositionEcl() - body.GetPositionEcl();
```

**The failure mode is silence, and no gate in this repository can catch it.** The test project
references no KSA assembly by design, so nothing under `Ksa/` is reachable by a unit test; the build
is clean, `check-boundary.sh` is happy, and the API surface does not move because `Celestial` is
already bound. It cost a batch of shots comparing two identical builds, and the only tell was a
diagnostic log line that did not appear.

The general form is worth carrying: **a KSA property's declared type is usually the interface**, and
a pattern-match against the concrete class is a runtime `false` wearing a compile-time tick.

## A BCL property can be missing at runtime, and the assembly is not the reason

`HttpResponseMessage.StatusCode` throws `MissingMethodException` in game:

```
Method not found: 'System.Net.HttpStatusCode System.Net.Http.HttpResponseMessage.get_StatusCode()'
```

Everything about that is checkable, and all of it checks out. The runtime loads
`C:\Program Files\Kitten Space Agency\System.Net.Http.dll` (10.0.0.0) — the mod logs which
assembly it gets — and that file decompiles to `public HttpStatusCode StatusCode`, present and
untrimmed. No second copy is deployed beside the mod, and the game ships
`System.Net.Primitives.dll` too.

So the assembly is right and the member is there. What is left is **type identity**: the exception
names the full signature including its return type, which is what a mismatch on `HttpStatusCode`
between the compile-time reference and StarMap's load context would look like.

**Read it by reflection.** `type.GetProperty("StatusCode").GetValue(response)` asks the object what
it actually has, and works. `Ksa/FeedbackClient.cs` does exactly that and logs the assembly it
found, so if a future build fixes this the log will say so.

The general shape: a BCL member whose **return type comes from a different BCL assembly** is the
one at risk. Nothing about the call site looks dangerous, it compiles against the reference
assemblies without complaint, and it fails only in game.

## Particles

### An endless emitter must be Kill()ed, not just removed from its parent

`Celestial.RemoveEmitter` removes the handle from that body's list and **nothing else**.
`ParticleSystem.UpdateEmitters` walks the whole pool, so a removed emitter is still updated, still
spawns, and still draws. An `Endless` emitter never completes its own simulation, so
`TryUnregisterEmitter` never fires and it is never returned to the pool: it emits for the rest of
the session from wherever its origin was left.

`ParticleEmitter.Kill()` is the stop. It forces spawning complete, after which the engine ages the
last particles out, unregisters and calls `ResetEmitter`, which is what makes the slot acquirable
again. So the release path is `Kill()` **then** `RemoveEmitter`, in that order.

Skipping the `Kill()` shows in game as particles frozen in mid-air along the path the emitter was
following, and as a gun keeping a small fire burning on its muzzles after it stops shooting. The
third consequence of the same fault is invisible until it is fatal: the pool bleeds one emitter
per effect, and eventually nothing in the world can spawn particles at all.

### An emitter cannot throw particles in a direction of your choosing

`ParticleEmitter.EmitterVelocity` is assigned **only** on the vehicle-parented path in
`ParticleEmitter.UpdateUniforms`, and only when `EmitterRelative` is set. A mod emitter parented to
a `Celestial` therefore has `EmitterVelocity == float3.Zero` forever: `InheritVelocity` has nothing
to inherit, and `BubbleOrigin.VelocityBub` never reaches spawning. Particles launched from such an
emitter stay where they were born.

Directional spawn logic is no substitute. The cone is built about a fixed axis of the emitter's
frame, and for a body-fixed bubble that axis is a compass bearing rather than anything that follows
a turret.

So an effect that has to *travel* is built by moving the emitter, one per moving thing, with the
origin rewritten each frame. `Ksa/MotorPlume.cs` and `Ksa/TracerTrail.cs` are both that shape. The
cost model follows from it: an emitter per object, out of a shared pool, so anything spawning tens
of objects a second has to cap how many are decorated.

