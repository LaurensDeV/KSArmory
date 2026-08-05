# KSA modding notes

Everything here was read out of the shipped assemblies of **KSA build 2026.8.5.5168** with
`tools/apidump`, or out of the StarMap sources. KSA is pre-release and unofficially moddable:
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
| Patching | Harmony (`Lib.Harmony` 2.4.1) |

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
class name is irrelevant, despite what some docs say. It then dispatches to attributed methods.
Signatures are validated, and a mismatch means the hook is silently skipped:

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
loading and silently drawing nothing**, with no error anywhere. If that happens, the fallback is
Harmony-patching `Program.OnDrawUiViewports` directly, which is what StarMap does on our behalf.

**Only the code half needs a loader.** `mod.toml`'s `assets` array, the part XML, meshes and
textures are KSA's own content system: without any loader the part still appears in the editor and
renders, it simply does nothing. A package manager — a CKAN equivalent — sits above all of this
and distributes the archive; it neither loads code nor changes any of the above.

## Key types

### Entry points into world state — `KSA.Program : App`

All static:

```csharp
public static Vehicle ControlledVehicle;                  // field; the vessel the player flies
public static ReadOnlySpan<Vehicle> VehiclesInFrame { get; }  // every loaded vehicle
public static GizmosRenderer GizmosRenderer;              // field; debug line/sphere drawing
public static Viewport MainViewport { get; }
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
bool IsDisposed { get; set; }                             // check before touching a stored ref
string Id { get; }                                        // via IObjectId
doubleQuat Body2Cce { get; set; }
Span<Vehicle> NearbyVehicles { get; }                     // same physics bubble only
static Vehicle CreateVehicle(CelestialSystem, VehicleTemplate, IParentBody, string id);
Vehicle Split(Part.Connector, double impulse, ref PoseChange, string id);
void Teleport(Orbit, doubleQuat?, double3?);
```

### `KSA.Universe` — statics

```csharp
static CelestialSystem CurrentSystem { get; set; }
static void DestroyVehicle(Vehicle);
static void DestroyVehicleFromEvent(Vehicle, VehicleDestructionEvent);   // how you kill something
static SimTime GetElapsedSimTime();
```

`VehicleDestructionEvent { VehicleDestructionCause Cause; float PeakGLoad; float PeakDynamicPressure; }`
with `Cause ∈ { GroundImpact, OceanImpact, Collision, ExcessiveGForce, AerodynamicForces, HydrodynamicForces }`.

There is **no partial-damage API** — destruction is binary.

### Spawning a vehicle at runtime

`Vehicle.CreateVehicle(...)` alone is **not enough** — it constructs the object but does not put
it in the world. The vehicle registers into `CurrentSystem.All` (so it shows up in enumerations)
yet stays at the frame origin, never moves, and is invisible. Copy what `Vehicle.Split` does:

```csharp
Orbit orbit  = Orbit.CreateFromStateCci(parent, Universe.GetElapsedSimTime(), posCci, velCci, colour);
Vehicle v    = Vehicle.CreateVehicle(system, body2Cce, bodyRates, parent, id, rootPart, orbit);
parent.Children.Add(v);        // orbiter tree -- without this UpdatePerFrameData never runs
v.AddToTask(platform.UpdateTask);   // physics task -- without this it is never simulated
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

Gizmo submission from the same hook *does* work: `ResetInstances()` runs early in `OnFrame` and
`GizmosRenderer.Render` later in the render pass, so a postfix lands between them.

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

#### Ecl is absolute — three bugs came from forgetting that

Near Earth, ecliptic **position** sweeps past at ~29.8 km/s and ecliptic **velocity** is dominated
by that same solar orbit. Anything that treats an Ecl value as local is wrong, and the failures
look nothing alike:

| Symptom | Cause |
| --- | --- |
| Missiles flew 84 km in a straight line, drag-limited to ~1.1 km/s, seeker lock broken instantly | Used `VelocityEcl` as airspeed and as a heading. Drag saw Mach 87; the seeker compared line-of-sight against Earth's orbital vector |
| Telemetry read "flew 650 km, speed 29 km/s" | Measured distance and speed against the absolute frame |
| Whole gizmo overlay drawn ~500 m from the craft | Differenced an Ecl position captured during the frame update against one re-read at draw time — one frame apart, and 29800/60 = 497 m |

Rules that follow:

- **Relative quantities are safe.** `targetVel - missileVel` is frame-independent; use those freely.
- **Absolute velocity is never a heading or an airspeed.** Subtract a local frame velocity first —
  the platform's is the natural choice, since it carries the body's orbital *and* rotational motion.
- **Never difference Ecl positions captured at different instants.** Capture one reference at the
  same moment as everything else and difference against that.

The regression test `EngagementIsUnchanged_WhenCarriedByAFastMovingFrame` pins the first two: an
engagement offset by 29.8 km/s must produce an identical result.

#### Drawing gizmos on a craft

Use `camera.GetPositionEgo(vehicle)` as an anchor and add Ecl offsets to it. Do **not** use
`camera.EclToEgo(vehicle.GetPositionEcl())` as an anchor for geometry captured at another time.

Part-relative geometry should go through the part's own transform rather than being rebuilt from
a boresight and an arbitrary perpendicular — the latter gives a correctly-sized ring at a random
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

### Placing a craft on the ground — `Vehicle.TeleportToLocation`

`TeleportToLocation(Celestial, latDeg, lonDeg)` is public and is how the game moves a vessel. It
builds the kinematic state from the craft's own bounding box and queues it through
`InputEvents.TeleportInputBuffer`, so the hull arrives upright and resting on the terrain. Writing
a position instead puts the craft's *origin* at the point and leaves the rest wherever that falls.

Because it is a buffered engine event that rebuilds the vehicle's orbit and velocity, it is a
once-per-action call. Do not drive it per frame to make a craft follow the cursor.

**It silently adds 8 m within 40 m of a launch-pad landmark** — `GetInitialKinematicStateForLocation`
calls a private `GetLaunchPadHeightAtDirCcf`, so a craft placed at the pad stands *on* it. Anything
drawing a preview marker has to add the same, or the marker sits inside the structure the craft
will end up on. `KsaWorld.LaunchPadHeight` mirrors it, reading `Celestial.BodyTemplate.Locations`
for a `LandmarkReference { IsLaunchPad: true }` — all public. **Those two numbers are the engine's
and are copied**, so `ksa-api-diff.sh` will not notice if they move: the method holding them is
private and is not in the API surface.

### Aiming the player's camera — `OrbitView`, not `OrbitController`

Writing `Camera.LocalRotation` does nothing lasting: every viewport runs a controller that rebuilds
its camera each frame. `ViewportBase.SetCameraMode(CameraMode.Fixed)` does hold, and is how a
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
them. Simulating behaviour in a `[StarMapAfterOnFrame]` hook avoids all of that — which is the
approach this repo takes.

## Authoring a part with no new art

**Asset Ids resolve in one global library across mods** (`SerializedId` / `ILibraryData`, with
an `_isReference` flag for entries that are pure references). Core loads before user mods, so a
mod's XML can instance Core's subparts and materials by Id — **no mesh atlas, no textures, no
Blender**.

**Confirmed in-game.** A mod's `<SubPart InstanceOf="CoreStructuralA_Subpart_TubeA">` renders
with Core's material, shipping no art at all. This repo's launcher was built entirely that way
before it became a Pantsir, and it is still the right answer for anything that can be assembled
out of Core's kit. `tools/validate-parts.py` checks every reference resolves.

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
- **Meshes whose name starts with `_` are skipped**, which is a free convention for helper
  geometry you want in the source file but not in the game.

A consequence worth stating separately: because node transforms are not read, **an atlas is a
library of bodies in their own local frames**, and placement lives entirely in the part XML's
`<Transform><Position>`. That is what makes a pose reconstructible outside the game — see
`tools/model/checkswept.py`.
- **`AoRoughMetal` is R=occlusion, G=roughness, B=metalness** (glTF ORM). Core's own
  `Textures/default_pbr.png` is `(255, 180, 0)` and `EmptyAoRoughMetallic.png` is
  `(255, 255, 0)`: unoccluded, rough, non-metal. *An earlier revision of these notes had this
  backwards.*
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
`CoreFairingA_Subpart_FairingNoseCone{Half,1,2}WA` (0.5 / 1 / 2 m nosecones). Materials follow
`<Category>_Material`, e.g. `CoreStructuralA_Material`.

**Not solved**: rendering a mesh at an arbitrary runtime position (for mod-simulated objects
rather than parts on a vehicle). `KSA.StaticMeshRenderable` has a public constructor and a
`Transform` field, but needs `IMeshRenderer<InstanceData>` instances owned by the engine's
render systems.

## Threading

`[StarMapAfterOnFrame]` runs on the main thread (Harmony postfix on `Program.OnFrame`), so
gizmo submission and `Universe.DestroyVehicleFromEvent` are safe from there. Vehicle physics
itself runs on worker threads via `VehicleUpdateTask`; do not mutate vehicle state from those.

Never destroy vehicles while iterating `Program.VehiclesInFrame` — copy to a list first.

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
