# KSA cameras, viewports and the render path

What the engine does with cameras, read out of the decompiled sources rather than inferred from
this mod's behaviour. Every claim carries a `file:line` into `../ksa-game-assemblies/current/src`.

**Read this before writing to a camera.** Fixed mode *requires* something to follow: Fixed with a
non-null `Following` is the working combination, and a crash in that state is an unset
`CameraRotation` (§4.5, trap 1) rather than an illegal pairing. Neither this mod's code nor its
docs are evidence about the engine — only the corpus is.

Nine citations are verified against the source, spanning both halves — the per-viewport render loop,
`NearbyCelestial` assignment, `ClampCamera`, `SetFieldOfView`, `GetFrame2Ecl`,
`OrbitController.OnFrame`'s early return, the unguarded `OrbitView` dereference, `MeanRadius` as a
divisor, and `changeControl`. The rest are unverified, which is why they are cited: check before
relying on one.

---

# Part 1 — modes, controllers, and following something

## KSA cameras and controllers — what a mod needs to know

Grounded **only** in the decompiled engine at
`../ksa-game-assemblies/current/src`.
All citations are relative to that root. Nothing here is taken from any mod's own code or docs.
Build: **2026.8.22.5348**. Line numbers move on every KSA update, so a citation that does not land
on what it claims means this file is behind the corpus, not that the corpus is wrong.

---

### 1. Frame order — where a mod's write lands

`KSA/KSA/App.cs:35-54` is the whole game loop: `OnFrame(totalSeconds, dtPlayer)` in a `while`,
**with no try/catch anywhere**. An exception thrown from a controller propagates out of `App.Run`
and kills the process. `dtPlayer` is clamped to `1/GameSettings.Current.Simulation.MinTargetFrameRate`
(`App.cs:40-41`).

`Program.OnFrame` (`KSA/KSA/Program.cs:2022`) in order:

| # | Step | Ref |
|---|---|---|
| 1 | `PrepareFrame` — orbit/vehicle solvers applied, `Glfw.PollEvents()` | `Program.cs:1956-2020` |
| 2 | `OnFrameViewports(dtPlayer)` — **every controller writes its camera here** | `Program.cs:2046`, `2353-2391` |
| 3 | `OnDrawUiFrame` / `OnDrawUiViewports` (StarMap Gui hooks wrap these) | `Program.cs:2050-2051` |
| 4 | `OnFrameController` (cursor mode), `Cursor.UpdateInputRay(GetCamera())` | `Program.cs:2098-2101` |
| 5 | `OnPreRender` → `Render` → `PostRender` | `Program.cs:2106-2114` |
| 6 | *(StarMap postfix)* `[StarMapAfterOnFrame]` mod hooks | `StarMap.Core/StarMap.Core.Patches/ProgramPatcher.cs:40-49` |

`ProgramPatcher.AfterOnFrame` is a **Harmony postfix on `Program.OnFrame`**, so a mod's frame hook
runs *after* this frame's controller pass *and after rendering*. Consequences:

- A write to `Camera.PositionEcl` / `Camera.LocalRotation` from a frame hook is **never rendered on
  the frame it is made**. It is consumed (or discarded) at step 2 of the *next* frame.
- The right place to write is therefore the controller's own input fields (`FixedController.CameraOffset`
  etc.), which the controller reads next frame — not the camera's pose, which the controller rewrites.

`OnFrameViewports` (`Program.cs:2353-2391`) calls `viewport.OnFrame((float)dtPlayer)` for **every**
viewport, visible or not (the `Visible` test at `:2371` is *after* the call). The dt is player time,
cast through `float`, and keeps ticking while the simulation is paused.

`Viewport.OnFrame` (`KSA/KSA/Viewport.cs:139-144`) is exactly three calls:
`GetActiveController().OnFrame(this, dt)` → `GetCamera().OnFrame(dt)` → `ViewportAudio(dt)`.

`Camera.OnFrame` (`Camera.cs:490-500`) calls `ClampCamera()` **first**, then builds the view/VP
matrices from `Ego2View`. So the clamp is applied on top of whatever the controller just wrote.

---

### 2. Viewports

`Program.ViewportCount = 4` (`Program.cs:238`), built at `Program.cs:857-871`:

| Index | Role | State at boot |
|---|---|---|
| 0 | `MainViewport` | `Visible = true`, no own render target (shares `_offscreenTarget`) |
| 1 | `ThumbnailViewport` | `IsOffscreen = true`, `ShouldRenderGizmos = false`, 512-ish square |
| 2, 3 | spare | `Visible = false`, own 500×500 render targets |

So a mod has **at most two spare viewports** for a picture-in-picture camera, and `DockingPort`
competes for the same two (`KSA/KSA/DockingPort.cs:179-196` scans for the first viewport that is
neither `Visible` nor `IsOffscreen`). `ViewportCount` is a public static field but changing it after
boot creates nothing.

Public and writable from a mod: `Viewport.Mode`, `.Visible`, `.IsOffscreen`, `.ShouldRenderGizmos`,
`.NewSize`, `.AllowResize`, `.BaseCamera`, `.MapCamera`, and all five controllers
(`Viewport.cs:14-51`). `Program.Viewports` is a `public static readonly List<Viewport>`
(`Program.cs:240`).

- `Viewport.GetCamera()` returns `MapCamera` **only** in `Map` mode, `BaseCamera` otherwise
  (`Viewport.cs:326-333`). The four non-Map controllers are all constructed against `BaseCamera`;
  `MapController` alone gets `MapCamera` (`Viewport.cs:111-115`). So `FixedController.Camera == BaseCamera`.
- `Viewport.GetActiveController()` is a switch that **throws `ArgumentOutOfRangeException` on the
  default arm** (`Viewport.cs:313-324`). Writing a bogus `CameraMode` into the public `Mode` field
  crashes the game every frame thereafter.
- Non-main viewports get an ImGui window of their own automatically, named `ViewportName`
  (`Viewport.DrawImGui`, `Viewport.cs:233-292`, called from `Program.cs:2766-2778`). The window's
  close button writes `Visible` by ref; nothing resets the camera mode when it closes — `DockingPort`
  detects the close by polling `Viewport.Visible` (`DockingPort.cs:170-173`).
- In `Fixed` mode the viewport's menu bar and hover rectangle are suppressed (`Viewport.cs:284`).

#### Is a mode change immediate?

`Viewport.SetCameraMode` (`Viewport.cs:335-345`) is **synchronous and immediate** for the *field*:
it calls `OnSwitchOff(newMode)` on the outgoing controller, assigns `Mode`, calls `OnSwitchOn(oldMode)`
on the incoming one, and clears held vehicle input. It is a no-op if the mode is unchanged.

The **camera pose** does not change until the next `Viewport.OnFrame`, because only `OnFrame`
positions the camera. Two of the `OnSwitchOn` handlers do move the camera themselves
(`MapController.cs:552-561` restores the last map pose; `IVAController.cs:187-194` sets `LocalRotation`),
the other three do not.

`Program.SetCameraMode(mode)` (`Program.cs:2721-2723`) targets `HoveredViewport`, not the main one.
`Viewport.NextCameraMode()` cycles Orbit→Free→IVA→Orbit only; from `Map` or `Fixed` it returns false
and does nothing (`Viewport.cs:347-363`). **A viewport in `Fixed` mode can only be taken out of it by
an explicit `SetCameraMode` call** — the player's camera-mode key cannot.

Writing `Viewport.Mode` **directly** bypasses `OnSwitchOff`/`OnSwitchOn` entirely. That is the route
past `FixedController`'s `TimedAlert`, but it also skips `MapController`'s `Camera.NoRotation`
bookkeeping (`MapController.cs:535`, `:573`) and `IVAController`'s seat/`LastFollowing` setup.

---

### 3. `KSA.Camera` — the object every controller writes

`KSA/KSA/Camera.cs`, extends `KSA.Transform3D` (`Transform3D.cs`).

**The rendered orientation is `LocalRotation`, full stop.** `Ego2View => LocalRotation.Inverse()`
(`Camera.cs:79`) is the only thing `Camera.OnFrame` feeds into the view matrix (`Camera.cs:493-498`).
But `GetForwardEcl()` / `GetRightEcl()` / `GetUpEcl()` are built from `WorldRotation`
(`Camera.cs:171-184`), which is `Parent.WorldRotation * LocalRotation` when `Parent != null` and
`!NoRotation` (`Camera.cs:133-154`). **These two disagree if you ever set `Camera.Parent`.**
The engine never sets `Camera.Parent` anywhere — do not start.

Position:

- `PositionEcl` getter/setter is overridden (`Camera.cs:110-131`): when `Following != null` it is
  `Following.GetPositionEcl() + PositionCce`, and the setter stores the difference.
- `PositionCce` (`Camera.cs:81-108`) round-trips `LocalPosition` through
  `Following.GetBodyFixed2Ecl()` unless `NoRotation`. So **the camera's stored offset is always in the
  followed object's body-fixed axes and always rotates with it** — there is no flag that turns that off
  except `NoRotation`.
- `TidalLocking` (`Camera.cs:160`, set at `:612`) is stored, saved (`:824`) and shown in a menu
  (`Program.cs:3288`) but **is not read by any code that affects the camera pose** — grep across the
  whole tree finds no consumer. It appears vestigial in this build.
- `Translate` is `LocalPosition += translation` in body-fixed axes when following (`Camera.cs:186-196`).

`ClampCamera()` (`Camera.cs:636-650`) runs at the top of every `Camera.OnFrame`, for every viewport,
whenever `Program.Editor == null`. If altitude ≤ 0.5 m it **overwrites `PositionEcl`** onto the
surface. Two traps in it:
- It reads `Program.GetNearbyCelestial()` and `Program.GetCurrentAltitudeKm()`, both of which resolve
  `Program.GetCamera()` = **`FrameViewport`'s** camera (`Program.cs:4555-4558`, `2349-2360`), not
  `this`. A secondary viewport's camera is therefore clamped against the *main* camera's altitude.
- `Program.GetCurrentAltitudeKm` calls `positionCce.Normalized()` (`Program.cs:2357`) — see §8, that
  throws on a zero vector.

`camera.NearbyCelestial` and `CurrentAltitudeKm` are only ever computed for the `FrameViewport`
camera (`Program.OnFrameCelestials`, `Program.cs:2316-2347`), so on a secondary viewport they are
stale or null.

`SetFollow(target, tidalLocking, changeControl = true, alert = true)` (`Camera.cs:605-621`):
- immediately jumps the camera to `target.GetPositionEcl() + (float)target.MeanRadius * 2.5 * forward`.
  Note the `(double)(float)` narrowing of `MeanRadius` at `:609`.
- `changeControl: true` (the default) sets `Program.ControlledVehicle = target as Vehicle` — i.e.
  **following a non-Vehicle with the default deselects the player's craft**. Pass `changeControl: false`.
- `Unfollow(changeControl = true)` preserves `PositionEcl` and clears `_tidalLocking` (`Camera.cs:623-634`).

FOV: `SetFieldOfView` / `ChangeFieldOfView` / `SetOrthographic` / `SetOrthoHalfHeight`
(`Camera.cs:420-446`), clamped 15–120° (`Camera.cs:37-39`, `:462`). Applied once at viewport creation
from `GameSettings` (`Program.cs:593`, `GameSettings.cs:2346-2348`) — **not overwritten per frame**, so
a mod's FOV sticks. `Camera.OnKey` (`Camera.cs:854-872`) is dispatched *before* the active controller
(`Program.cs:1567`), so the player's FOV keys work in every mode including `Fixed`.

`Camera.SerializeSave` / `DeserializeSave` (`Camera.cs:815-847`) store `Following.Id` and resolve it
through `Universe.CurrentSystem.Get(id)`, which returns `Astronomical`. **A mod-supplied `IFollowable`
will not survive a save/load** — `Following` comes back null.

---

### 4. One section per `CameraMode`

`CameraMode` has exactly five values: `Orbit, Free, Map, IVA, Fixed` (`KSA/KSA/CameraMode.cs:5-17`),
each with an `[XmlEnum]` name. There is no sixth, and no `Chase` mode — `Chase` is a
`CameraReferenceFrame`, not a mode (`CameraReferenceFrame.cs:3-12`: `Surface, Orbit, Parent, Poles,
Stars, Chase, Editor`).

Controller base: `KSA/KSA/Controller.cs:8-117` — `Camera` is a public field, and the virtual surface is
`OnFrame(Viewport, double)`, `OnDrawUi`, `OnSwitchOn/Off`, `GetCursorMode`, and the input handlers.

---

#### 4.1 Orbit — `OrbitController` (`KSA/KSA/OrbitController.cs`)

**For:** the default third-person orbit around a followed object.

**`OnFrame` (`:468-639`)**: reads `Camera.Following`; **returns immediately if it is null** (`:470-475`).
Otherwise it reads `following.OrbitView` (azimuth, elevation, distance power, offset part), applies
gamepad right-stick directly to `orbitView` (`:480-485`), spring-smooths its own `Azimuth`/`Elevation`/
`DistancePower` copies toward the `OrbitView` values (`:585-587`), builds `frame2Ecl` from
`OrbitView.ReferenceFrame` (`:590`, `GetFrame2Ecl` at `:214-315`), and writes, every frame:

```
Camera.NoRotation  = false                                 (:617 / :625)
Camera.LocalRotation = LookAtRotation(dir, up)             (:618 / :626)
Camera.PositionCce   = offset - back * distance            (:619 / :627)
```

**Intended external interface:** the followed object's `OrbitView`
(`KSA/KSA/OrbitView.cs:5-11`) — `ReferenceFrame`, `Azimuth`, `Elevation`, `DistancePower`,
`OffsetPart`. Those are the fields the mouse and keyboard write; the controller's own
`Azimuth/Elevation/DistancePower` are smoothed *outputs* and get overwritten. `AnimateFocusChange`
(`:75`) turns the next focus change into a lerp; `SetPoseChange` (`:683-688`) exists for a
frame/origin shift. `Program.GetOrbitController()` (`Program.cs:573`) returns the `FrameViewport`'s.

**Traps:**
- `following.OrbitView.OffsetPart` at `:477` is an **unguarded dereference of `OrbitView`**. A mod
  `IFollowable` that returns null from `OrbitView` NREs here on the first frame.
- `MeanRadius` is a divisor at `:513` (editor path) and `:580` (normal path):
  `_lastFocusedRadius / meanRadius`. A `MeanRadius` of 0 produces `Infinity` → `DistancePower` becomes
  `Infinity`/`NaN` → the camera position becomes `NaN` and everything drawn disappears. It is also the
  distance scale at `:593` (`DistancePower * meanRadius`), so `MeanRadius == 0` puts the camera exactly
  on the object.
- `OnSwitchOn` (`:690-700`) **forces `SetFollow(Universe.WorldSun, false)`** when nothing is followed.
  That call defaults `changeControl: true`, so it also clears `Program.ControlledVehicle`.
- `GetCarousel2Cce` (`:317-333`, used by `CameraReferenceFrame.Parent`) dereferences
  `celestial.Orbit.Parent` — NRE if `Orbit` is null. It does guard the degenerate cross-product case
  by returning `Identity` (`:325-328`), unlike `FixedController`'s copy.
- `AlertCameraReference` (`:209-212`) indexes `_modeAlertTitle[(int)frame]`; an out-of-range
  `CameraReferenceFrame` is an `IndexOutOfRangeException`.

**Overwritten every frame:** `Camera.LocalRotation`, `Camera.PositionCce` (hence `PositionEcl`),
`Camera.NoRotation`. A mod write to any of these in `Orbit` mode is lost.

**The useful exception:** `Orbit` mode with `Camera.Unfollow()` — `OnFrame` returns at `:471-475`
without touching anything, `OnCursorPos`/`OnScroll` no-op because `Camera.Following?.OrbitView` is
null (`:347`, `:362`, `:387`). That is a **fully mod-driven camera with no controller writes at all**,
and it avoids every `FixedController` trap. Caveats: `Camera.ClampCamera` still runs, and with
`Following == null` the physics-bubble branch of `Camera.GetPositionEgo` (`Camera.cs:237-243`) is
skipped, so nearby vehicles draw at their *analytic* position rather than their physics one.

---

#### 4.2 Free — `FlyController` (`KSA/KSA/FlyController.cs`)

**For:** the WASD noclip camera. Also doubles as the vehicle editor's fly camera
(`IsEditing => Program.Editor != null`, `:100`; `OnFrameEditor` at `:742-825`).

**`OnFrame` (`:653-735`)**, non-editor path:
- position is written **only when a movement key is held or the gamepad stick is off centre**
  (`:661-712`) — `Camera.Translate(dir * dt * GetCurrentSpeed())` then `ClampCamera()`.
- `_frame2Ecl` is set from `Camera.Following` **only if it is a `Celestial`** (`:721-731`), else Identity.
- **rotation is written unconditionally, every frame**: `Camera.LocalRotation = _frame2Ecl * _offsetEcl`
  (`:734`).

**Intended external interface:** `Speed`, `FastSpeed`, `RollSpeed`, `SpeedMultiplier` (`:92-98`),
`SetSpeed(value, DistanceUnit)` (`:133-152`), `lookTgt` and `lookSharpness` (public fields, `:40-42`)
for a decaying look impulse, and **`CacheOffset()` (`:870-882`)** — the only public way to make a
rotation stick: set `Camera.LocalRotation`, then call `CacheOffset()`, which back-solves the private
`_offsetEcl` so the next `OnFrame` reproduces what you wrote. `Program.GetFlyController()` at
`Program.cs:578`.

**Traps:**
- A `Camera.LocalRotation` write without `CacheOffset()` is silently reverted on the next frame.
- `GetFrame2Ecl` (`:~560-651`) **throws by design** for invalid combinations:
  `NotImplementedException` for `Parent` on a vehicle (`:639`), `InvalidOperationException` for
  `Poles` on a vehicle (`:647`) and for `Orbit`/`Chase` on a celestial (`:601`). It is not reached from
  `OnFrame` (which only ever passes `Surface`, `:724`/`:875`), but it is public-adjacent behaviour worth
  knowing.
- Its private `ClampCamera` (`:845-868`) is *not* `Camera.ClampCamera`; it uses a 2 m floor and pushes
  the camera out of the sun's mesh. It runs only on movement.
- `OnSwitchOn` (`:898-909`) calls `CacheOffset()` (or `CacheEditorLook()`), so entering Free mode
  preserves the current aim.

**Overwritten every frame:** `Camera.LocalRotation`. Position is preserved unless the player moves.

---

#### 4.3 Map — `MapController` (`KSA/KSA/MapController.cs`)

**For:** the orrery view. Uses `Viewport.MapCamera`, a **different `Camera` object** from every other
mode (`Viewport.cs:113`, `:326-333`).

**`OnFrame` (`:124-289`)**: bails to `Program.SetCameraMode(CameraMode.Free)` if nothing is followed
(`:126-130`) — note that targets the **hovered** viewport, not this one. Otherwise it applies drag/keys
to its **own** `OrbitView` field (`:43`, not the followed object's) and `Scope`, then writes every
frame:

```
Camera.PositionEcl   = Lerp(AnimationStartPosition, AnimationEndPosition, AnimationProgress)   (:281)
Camera.LocalRotation = LookAtRotation(dir, up)                                                 (:282)
```

**Intended external interface:** `Scope` (metres, floored at `Following.MeanRadius` at `:269`),
`PositionOffsetFollowing` (`:35`), the controller's own `OrbitView` (`:43`), `Inverted`,
`PreviouslyControlledVehicle`, `AnimateFocusChange` + `AnimationStartPosition`, `VehicleControlToggle` /
`ToggleVehicleControl()` (`:108-122`). `SetDefaults()` (`:74-106`) computes a sane `Scope` from the
target's radius and SOI. `Program.GetMapController()` at `Program.cs:583`.

**Traps:**
- `OnSwitchOn` sets `Camera.NoRotation = true` (`:535`) and `OnSwitchOff` sets it false (`:573`).
  Setting `Viewport.Mode` directly instead of calling `SetCameraMode` strands `NoRotation` — and with
  `NoRotation` true, `PositionCce` and `WorldRotation` stop consulting the followed body / parent
  (`Camera.cs:85`, `:97`, `:137`, `:145`).
- `OnSwitchOff` unconditionally sets `Program.ControlledVehicle = PreviouslyControlledVehicle` and
  `IsControlledVehicleActive = true` (`:569-570`) — leaving map mode hands control back whether the
  mod wanted it or not.
- `MeanRadius` and `GetPositionEcl` on `Camera.Following` are dereferenced unguarded at `:269`, `:272`.

**Overwritten every frame:** `MapCamera.PositionEcl`, `MapCamera.LocalRotation`.

---

#### 4.4 IVA — `IVAController` (`KSA/KSA/IVAController.cs`)

**For:** sitting in an `IVASeat` module on the followed vehicle and looking around with the mouse.

**`OnFrame` (`:27-118`)**:
1. Bails via `Program.HoveredViewport.NextCameraMode()` if `Camera.Following` is not a `Vehicle`
   (`:29-33`) **or if it is not the same vehicle as `LastFollowing`** (`:34-38`).
2. Position, **every frame** (`:40-41`):
   ```
   posAsmb = Seat.Parent.PositionVehicleAsmbOffset(Seat.PositionAsmb)
   Camera.PositionEcl = vehicle.GetPositionEcl() + vehicle.PosAsmbToBody(posAsmb).Transform(vehicle.Body2Cce)
   ```
   `PositionVehicleAsmbOffset` is `offset.Transform(MatrixAsmb2VehicleAsmb)` (`KSA/KSA/Part.cs:1056-1059`);
   `PosAsmbToBody` is `posAsmb - CenterOfMassAsmb` (`Vehicle.cs:1198-1201`). So the seat point is taken
   into the vehicle assembly frame, re-referenced to the centre of mass, and rotated into Ecl by
   `Body2Cce`. **`Vehicle.GetPositionEcl()` is the vehicle's centre of mass in Ecl.**
3. Rotation from smoothed cursor deltas (3-sample box filter, `:46-58`), scaled by
   `GameSettings.Current.Input.LookSensitivity` (`:67`), rotated about the camera's own up/right
   (`:71`, `:76`), then **clamped to the seat's forward hemisphere** (`:81-94`, `Dot < 0` is pushed back
   to 90°) and to ±~64° of the seat up axis (`:95-108`, the 0.9 dot limit). Written at `:111-112`.
   Suppressed on the frame after a switch (`SwitchThisFrame`, `:42`, `:117`).

**Intended external interface:** `Seat` (public field, `:21`) — assign it to any `IVASeat` on the
vehicle; the built-in seat-cycling key does exactly that (`:120-148`). `LastFollowing` (`:23`) and
`SwitchThisFrame` (`:25`) are public but are internal bookkeeping. There is **no `Program.GetIVAController()`** —
reach it as `viewport.IVAController` (`Viewport.cs:48`).

**`IVASeat` (`KSA/KSA/IVASeat.cs`)** is a `Module<IVASeat>` created from an `<IVASeat>` element in a
part template, with `<Position>`, `<ForwardAxis>` (default +X) and `<UpAxis>` (default −Z)
(`IVASeat.cs:9-27`, `:53-69`). **A mod can declare an `IVASeat` on its own part XML** and IVA mode will
find it — `vehicle.Parts.Modules.Get<IVASeat>()` (`IVAController.cs:128`, `:164`).

**Traps:**
- `OnSwitchOn` (`:155-198`) bails via `Program.HoveredViewport.NextCameraMode()` if the followed
  object is not a `Vehicle` (`:157-160`) or has **no `IVASeat` modules** (`:164-168`). Note it always
  acts on `Program.HoveredViewport`, **not on the viewport being switched** — putting a *secondary*
  viewport into IVA on a seatless craft cycles the *main* viewport's mode instead, and leaves the
  secondary stuck in IVA doing nothing.
- `Seat` is dereferenced unguarded at `:40`. It is only ever assigned in `OnSwitchOn`/`OnKey`. Reaching
  `:40` with `Seat == null` requires `Camera.Following == LastFollowing` while `Seat` is null — which
  the normal path cannot produce, but a mod writing the public `LastFollowing` field can.
- Uses `Cursor.ScreenPosition` (`:39`), which is main-window screen coordinates, not viewport-relative.
- `GetCursorMode()` returns `Disabled` unless Alt is held or a window is open (`:204-207`) — entering
  IVA captures the mouse.

**Overwritten every frame:** `Camera.PositionEcl` and `Camera.LocalRotation` (the latter unless
`SwitchThisFrame`).

**Usable outside an IVA seat?** No. The mode is defined entirely in terms of `Seat` and it hard-requires
`Following is Vehicle` with at least one `IVASeat` module. To place a first-person camera at an
arbitrary point, use `Fixed` and reproduce the two lines at `IVAController.cs:40-41` yourself.

---

#### 4.5 Fixed — `FixedController` (`KSA/KSA/FixedController.cs`, 131 lines)

**For:** a camera pinned to a point on a followed object, aimed along a fixed direction. The engine's
only user is the docking-port camera (`KSA/KSA/DockingPort.cs:179-196`).

**`OnFrame` (`:18-35`)** — the whole thing:

```csharp
IFollowable following = Camera.Following;
if (following != null) {                                              // :20-22
    OrbitView   orbitView = following.OrbitView;                      // :23
    doubleQuat  frame2Ecl = GetFrame2Ecl(following, orbitView.ReferenceFrame);   // :24
    double3     up        = double3.UnitZ.Transform(frame2Ecl);       // :25
    double3     right     = double3.Cross(CameraRotation, up).Normalized();      // :27  <-- throws
    double3     upEcl     = double3.Cross(right, CameraRotation).Normalized();   // :29  <-- throws
    Camera.PositionEcl    = following.GetPositionEcl() + CameraOffset; // :31-32
    Camera.LocalRotation  = Camera.LookAtRotation(CameraRotation, upEcl);        // :30, :33
}
```

**Interface — two public fields, both `double3` (`:9`, `:11`):**

| Field | Meaning | Units / frame |
|---|---|---|
| `CameraOffset` | added to `Following.GetPositionEcl()` | **metres, Ecl axes** — not body axes |
| `CameraRotation` | the camera's **forward direction** | a **direction vector in Ecl**. Not Euler angles, not a quaternion. |

Confirmed by the engine's own use (`DockingPort.cs:189-191`):
```csharp
CameraOffset   = vehicle.PosAsmbToBody(Connector.PositionVehicleAsmb).Transform(vehicle.Body2Cce);
CameraRotation = new double3(1,0,0).Transform(Connector.Asmb2VehicleAsmb.Concatenate(vehicle.Asmb2Cce));
```
i.e. offset = part position relative to the vehicle's centre of mass, rotated into Ecl; rotation = the
connector's local +X axis expressed in Ecl. `LookAtRotation` normalises the forward internally
(`Camera.cs:200`), so `CameraRotation` need not be unit length — but it must not be zero.

**Roll** is not settable. It is fixed by `frame2Ecl`'s +Z axis (`:25`), which comes from
`Following.OrbitView.ReferenceFrame` — a field the mod does not own and the player can change.

**The engine never writes `CameraOffset` or `CameraRotation`.** `DockingPort` sets them once and never
updates them, so the stock docking camera's aim is frozen in Ecl and does *not* track the vehicle's
rotation. To stay attached to a rotating body a mod must rewrite both **every frame**, from a
`[StarMapAfterOnFrame]` hook (which lands one frame early — see §1).

**Traps — this is the sharp one:**

1. **`CameraRotation` defaults to `double3.Zero`.** `Cross(zero, up)` is zero,
   `.Normalized()` → `Double3Ex.Normalized` (`KSA/KSA/Double3Ex.cs:18-21`) → `double3.Normalize`
   (`Brutal.Core.Numerics/Brutal.Numerics/double3.cs:722-727`), which **throws `DivideByZeroException`
   on a zero-length vector**. It does not produce NaN. With no try/catch in the frame loop (§1) this is
   a hard crash on the first frame after `SetCameraMode(Fixed)` with a non-null `Following`. Set
   `CameraRotation` **before** setting the mode.
2. **Same crash if `CameraRotation` is parallel to `frame2Ecl`'s +Z axis** — the cross at `:27` is again
   zero. With `ReferenceFrame == Stars` (or a `WreckageMarker`, see below) that axis is Ecl +Z, so
   aiming straight up or straight down in the ecliptic crashes. With `Surface` it is the local vertical,
   so aiming at the zenith or nadir crashes.
3. `:29` and `Camera.LookAtRotation` (`Camera.cs:200-202`) call `Normalize` again — same failure mode.
4. **`GetFrame2Ecl` (`:37-87`) unwraps nullables without checking**, unlike `OrbitController`'s copy:
   - `:45` `vehicle.GetEnu2Cce().Value` — `ComputeEnu2Cce` returns null when the vehicle's CCE position
     is near zero or exactly on the parent's rotation axis (`Vehicle.cs:2950`, `:2955-2974`).
   - `:49-50` `vehicle.GetLvlh2Cce().Value` — `ComputeLvlh2Cce` returns null when CCE velocity or
     position is near zero, or when they are parallel (`Vehicle.cs:2927`, `:2932-2946`).
     A **landed** craft with ~0 CCE velocity is exactly this case.
     Either is an `InvalidOperationException` ("Nullable object must have a value").
     `OrbitView.ReferenceFrame` is auto-set from the vehicle's region — `Surface` on the ground,
     `Orbit` in low orbit, `Parent` in high orbit (`VehicleRegionEx.cs:38-67`, applied at
     `Vehicle.cs:2899-2902`) — so a mod does not control which branch is taken.
   - `:53` `Parent` → `GetCarousel2Cce` → `celestial.Orbit.Parent.GetCci2Cce()` (`:91`), NRE if `Orbit`
     is null.
5. **`GetFrame2Ecl` has no `WreckageMarker` branch** (`OrbitController.cs:275-309` does). When a
   followed vehicle is destroyed the engine silently swaps `Camera.Following` to a `WreckageMarker`
   for every viewport (`Universe.cs:1722-1732`, `Camera.FollowWreckage` at `Camera.cs:598-603`), whose
   `ReferenceFrame` is forced to `Surface` or `Orbit` (`WreckageMarker.cs:53`). `FixedController` falls
   into the `else` arm and returns `Identity` (`:84`) — so the roll reference jumps to Ecl +Z the
   instant the target dies, which can turn trap 2 on without the mod doing anything.
6. `CameraOffset` and `CameraRotation` **persist across mode switches** — `OnSwitchOn` (`:127-130`) only
   raises a `TimedAlert`, and `OnSwitchOff` is not overridden. So a viewport previously used by
   `DockingPort` carries stale values into a mod's use of it.
7. `Camera.ClampCamera` (§3) still runs afterwards and can move the camera off `CameraOffset`.

**Overwritten every frame:** `Camera.PositionEcl` and `Camera.LocalRotation`, but only as a pure
function of `CameraOffset`, `CameraRotation` and the followed object — which is exactly what makes this
the intended mode for a mod-driven camera.

All input handlers return false (`:107-125`), so `Fixed` swallows nothing; `GetCursorMode()` falls
through to the base `Normal` (`Controller.cs:74-77`).

There is **no `Program.GetFixedController()`** — `Program` exposes accessors only for Orbit, Fly and Map
(`Program.cs:573-586`). Reach it as `viewport.FixedController` (`Viewport.cs:50`), which is the pattern
`DockingPort` uses.

---

#### 4.6 Not camera controllers

- **`GimbalController`** (`KSA/KSA/GimbalController.cs:8`) is
  `ModuleStateful<GimbalController, GimbalControllerState, ...>` — **engine thrust-vectoring**, nothing
  to do with cameras. Same for `EngineController`, `ThrusterController`. `Gimbal`, `GimbalAxis`,
  `GimbalReference`, `GimbalState` are all nozzle geometry.
- **`RenderCore.Cameras.Camera` / `RenderCore.Input.Controllers.{CameraController,FlyController,OrbitController}`**
  (`Planet.Render.Core/RenderCore.Cameras/Camera.cs`,
  `Planet.Render.Core/RenderCore.Input.Controllers/*.cs`) are a separate float-precision demo camera
  system. **Nothing in `KSA/` references the `RenderCore.Cameras` namespace** — do not confuse them
  with `KSA.Camera`.
- `BiomeSoundController` (`KSA/KSA/BiomeSoundController.cs`) is audio; it only *reads* a camera.

---

### 5. Use case B — kitten / first-person POV

**Can a camera be attached to a character or a bone? Not through any public API.**

- The only first-person mechanism in the engine is `IVAController` + `IVASeat`, and that is a
  **part module on a vehicle**, not a character attachment (`IVASeat.cs:7`, `:53-69`).
- `CharacterAvatar` (`KSA/KSA/CharacterAvatar.cs:11`) is a pure render description — core mesh, fur,
  attachments, expressions, animations, personality. **It has no camera, no head transform, no look
  direction, and no reference to `Camera` at all.** The identifier `Camera` does not occur in the file.
- Bone transforms exist: `AnimatedRenderable.GetBoneTransform(int)` and `GetBonePosition(int)`
  (`KSA/KSA/AnimatedRenderable.cs:214-231`), returning `Skeleton.WorldTransformList[i] * Transform`.
  But `Transform` is set to a **camera-relative (Ego) `float4x4`** during rendering
  (`KittenRenderable.cs:184`, from `KittenEva.UpdateRenderData` → `Vehicle.GetWorldMatrix(camera)`,
  `Vehicle.cs:3472-3483`). So a bone matrix is float precision, relative to *last* frame's camera, and
  only valid after rendering — the wrong side of the frame from where a camera must be positioned.
- **There is no public path from a `KittenEva` to its skeleton anyway.**
  `KittenEva._renderable` is private (`KittenEva.cs:10`) and `KittenRenderable._characterAvatar` is
  private with no accessor (`KittenRenderable.cs:11`). `KittenEva.Character` is public but is a
  `CharacterReference` (asset data), not the live avatar.

**Is there a head/look direction the engine maintains?** Only cosmetic eye tracking.
`CatEyeAnim` (`KSA/KSA/CatEyeAnim.cs`) has a `LookTargetMode { Camera, Random }` (`:10-16`), a
`LookTarget` (`:18`) and `MaxLookAtAngle = 30°` (`KittenRenderable.cs:103`) — it aims the *eyeballs* at
the camera or randomly. Nothing maintains a head or view direction for the character.

**What the engine does maintain, and a mod can read:** `KittenEva.PrepareWorker`
(`KittenEva.cs:108-116`) copies the **main camera's** basis into
`CharacterControlInputs.{CameraForwardCce, CameraRightCce, CameraUpCce}` every physics step — that is
the camera driving the kitten's movement frame, not the other way round.
`KittenEva.CharacterControlInputs` and `LocomotionState` are public getters (`:18-20`).

**What actually works for a kitten POV:**
- `KittenEva : Vehicle` (`KittenEva.cs:8`), so it is an `IFollowable` and every mode accepts it.
- `Fixed` mode following the kitten, with `CameraOffset` recomputed each frame the way
  `IVAController.cs:40-41` does it — an eye-height offset in the kitten's body frame:
  `offsetBody.Transform(kitten.Body2Cce)` — and `CameraRotation` from `kitten.Body2Cce`.
  `Vehicle.GetBodyFixed2Ecl()` is `Body2Cce` (`Vehicle.cs:1221-1224`); `Asmb2Cce`/`Body2Ego`/`Asmb2Ego`
  are all the same quaternion (`Vehicle.cs:460-494`).
- Or declaring an `<IVASeat>` on a mod part and using real IVA mode — but that requires the seat to be
  on a vehicle's part tree, and `IVAController` refuses a vehicle with no seats
  (`IVAController.cs:164-168`).

`KittenEva.UpdateHighlight` (`:199-226`) is worth noting: kitten hover-picking is disabled unless
`inViewport.Mode == CameraMode.Orbit`.

---

### 6. Use case C — rocket / projectile chase

#### `IFollowable`

```csharp
public interface IFollowable : IObjectId, IPosition, IVelocity, IOrientation, IRadius
{
    OrbitView OrbitView { get; }
}
```
`KSA/KSA/IFollowable.cs:3-6`. The full member list a mod must supply:

| From | Members | Ref |
|---|---|---|
| `IObjectId` | `string Id`, `KeyHash Hash`, `string Class`, `IsMoon()`, `IsStar()`, `HasOrbit()` | `IObjectId.cs:3-16` |
| `IPosition` | `GetPositionEcl()`, `GetPositionEclFromCce(double3)`, `GetPositionCceFromEcl(double3)` | `IPosition.cs:5-13` |
| `IVelocity` | `GetVelocityEcl()` | `IVelocity.cs:5-8` |
| `IOrientation` | `bool ShowAxes {get;set;}`, `GetBodyFixed2Ecl()`, `GetBodyRates()`, `DrawAxes(Viewport)` | `IOrientation.cs:5-14` |
| `IRadius` | `double MeanRadius` | `IRadius.cs:3-6` |
| `IFollowable` | `OrbitView OrbitView` | `IFollowable.cs:5` |

#### Implementers

| Type | Ref | `GetPositionEcl()` returns |
|---|---|---|
| `Astronomical` (abstract) | `Astronomical.cs:12`, `:306` | abstract |
| `Vehicle` | `Vehicle.cs:27`, `:871-873` | `_positionEcl`, cached |
| `Celestial` | `Celestial.cs:23` | analytic orbit position |
| `StellarBody` | `StellarBody.cs:12` | — (has no `Orbit` at all; not an `IOrbiter`) |
| `VehicleEditingSpace` | `VehicleEditingSpace.cs:8` | editor-space origin |
| `WreckageMarker` | `WreckageMarker.cs:6` | see below |

**`Vehicle.GetPositionEcl()` returns the ANALYTIC position, not the physics one.** It is
`_positionEcl`, assigned in `Vehicle.UpdatePerFrameData` (`Vehicle.cs:2449-2455`) as
`Parent.GetPositionEcl() + Orbit.StateVectors.PositionCci.Transform(cci2Cce)` — pure orbit solution.
The physics/drawn position lives in `KinematicStates.PositionPhys` in a bubble frame, and only appears
in `Camera.GetPositionEgo` (`Camera.cs:237-243`), which uses the physics path **only** when the camera
follows a vehicle and the queried object shares its `BubbleLeader`. Note `Camera.GetPositionEgo`
returns exactly `-PositionCce` for the followed object itself (`Camera.cs:233-236`), which is what hides
the analytic/physics difference for whatever you are following.

**`WreckageMarker` is the proof that a mod can implement `IFollowable`.** It is a plain public sealed
class (`WreckageMarker.cs:6-123`) that is *not* an `Astronomical`, is never registered in a
`CelestialSystem`, and is handed straight to `Camera.SetFollow` by `Camera.FollowWreckage`
(`Camera.cs:598-603`). The engine swaps to it automatically when a followed vehicle is destroyed
(`Universe.cs:1722-1732`). Its `GetPositionEcl` (`:85-93`) recomputes from either a body-fixed
coordinate or an orbit sample each call, and `GetVelocityEcl` (`:105-108`) just returns the parent's.
Every consumer of `Camera.Following` in the tree is interface-typed or uses `as`/`is` — nothing
downcasts to `Astronomical` unguarded (the call sites are `Astronomical.cs:458-461`,
`Program.cs:3377-3378`, `LocationMusicPlayer.cs:30-45`, `PhysicalAtmosphereReference.cs:61-64`,
`KSA.Rendering.Lighting/CascadedShadowSystem.cs:303-319`, `Universe.cs:1952-1962`).

#### Requirements on a mod's `IFollowable`, from the call sites

- **`OrbitView` must be non-null** — `OrbitController.cs:477` dereferences it unguarded, and
  `FixedController.cs:23` does too. Return a stable instance, not a new one per call.
- **`MeanRadius` must be > 0 and finite.** It is a divisor at `OrbitController.cs:513`/`:580`, a
  multiplier at `:593` and `MapController.cs:269`/`:96`, and is narrowed through `float` at
  `Camera.cs:609`.
- `GetPositionEcl()` is called several times per frame from different places — make it cheap and
  **consistent within a frame** (`Camera.cs:116`, `:124`, `FixedController.cs:31`,
  `MapController.cs:272`, `CascadedShadowSystem.cs:313`).
- `GetBodyFixed2Ecl()` is applied to `Camera.LocalPosition` on **every** `PositionCce` get/set
  (`Camera.cs:90`, `:102`) — return `doubleQuat.Identity` if you do not want the camera offset to
  rotate with your object.
- Call `SetFollow(target, tidalLocking, changeControl: false, alert: false)` — the default
  `changeControl: true` sets `Program.ControlledVehicle = target as Vehicle` = null (`Camera.cs:619`).
- **You own the lifetime.** Nothing cleans up `Camera.Following` when a mod object dies; the engine's
  own cleanup (`Universe.cs:1722-1732`) is keyed on `Vehicle` destruction only. Call
  `Camera.Unfollow(false)` or re-`SetFollow` before dropping the reference.
- It will not survive a save/load (`Camera.DeserializeSave`, `Camera.cs:830-847`).

#### `CameraOffset` vs `CameraReferenceFrame` / `GetFrame2Ecl` — how they compose

They are **independent and do not compose**:

- `CameraOffset` is added directly to `Following.GetPositionEcl()` in Ecl axes
  (`FixedController.cs:31`). `GetFrame2Ecl` has no effect on position.
- `GetFrame2Ecl(focused, referenceFrame)` produces one quaternion whose **+Z axis alone** is used, as
  the roll reference for the look-at (`FixedController.cs:24-25`, `:29`). Nothing else from it is used.
- The reference frame comes from `Following.OrbitView.ReferenceFrame` — a field on the *followed
  object*, shared with `OrbitController` and rewritten by the vehicle's region logic
  (`Vehicle.cs:2899-2902`). For a mod-supplied `IFollowable` the mod controls it; for a `Vehicle` it
  does not.
- Frames available in `FixedController.GetFrame2Ecl` (`:37-87`): for a `Vehicle` —
  `Surface` (ENU), `Orbit` (LVLH flipped 180° about X), `Parent` (carousel), `Stars` (Identity),
  `Chase` (`Body2Cce` flipped 180° about X). For a `Celestial` — `Surface` (CCF), `Parent`, `Poles`
  (CCI), `Stars`. Everything else → Identity.
  `OrbitController.GetFrame2Ecl` (`OrbitController.cs:214-315`) has the same list plus `Editor`,
  `WreckageMarker` support, and `HasValue` guards; `FlyController.GetFrame2Ecl` throws for several
  combinations.
- `CameraReferenceFrame.IsValidFor` (`CameraReferenceFrameEx.cs:5-25`): `Orbit` and `Chase` are
  vehicles-only, `Poles` is non-vehicles-only, `Surface`/`Parent`/`Stars` are always valid, `Editor` is
  never "valid" (it is forced in the editor at `OrbitController.cs:519`).

#### Recommended shape for a projectile chase camera

Two viable approaches, both grounded:

1. **Mod `IFollowable` + `Orbit` mode.** Implement `IFollowable` over the projectile, `SetFollow(…,
   changeControl: false)`, give it an `OrbitView` with `ReferenceFrame = Stars` and a sensible
   `DistancePower`. The player keeps mouse orbit/zoom for free. Requires a non-zero `MeanRadius`.
2. **`Fixed` mode on a spare viewport** (indices 2–3), following the launch platform or the projectile,
   with `CameraOffset` and `CameraRotation` rewritten each frame. Full control, none of the orbit
   smoothing — but every trap in §4.5 applies.

A third option avoids the controllers entirely: **`Orbit` mode with `Camera.Unfollow()`** (see §4.1),
where the controller returns before touching anything and the mod writes `Camera.PositionEcl` and
`Camera.LocalRotation` itself. `Camera.ClampCamera` is then the only engine write, and the analytic /
physics offset caveat applies.

---

### 7. Crash inventory

Ordered by how easily a mod trips it. Reminder: **there is no try/catch in the frame loop**
(`App.cs:53`), so all of these terminate the process.

| # | Where | Cause | Exception |
|---|---|---|---|
| 1 | `FixedController.cs:27` | `CameraRotation` left at `double3.Zero` | `DivideByZeroException` via `Double3Ex.cs:20` → `double3.cs:722-727` |
| 2 | `FixedController.cs:27`, `:29` | `CameraRotation` parallel to `frame2Ecl`'s +Z (zenith/nadir, or Ecl +Z under `Stars`) | `DivideByZeroException` |
| 3 | `Camera.cs:200-202` (`LookAtRotation`) | zero forward, or forward parallel to the supplied up | `DivideByZeroException` |
| 4 | `FixedController.cs:45` | `Surface` frame on a vehicle at a parent's centre or exactly on its rotation axis | `InvalidOperationException` (`Nullable.Value`) |
| 5 | `FixedController.cs:49-50` | `Orbit` frame on a vehicle with ~zero CCE velocity (landed) or radial ∥ velocity | `InvalidOperationException` |
| 6 | `Viewport.cs:322` | a `CameraMode` value outside 0–4 written into the public `Mode` field | `ArgumentOutOfRangeException`, every frame |
| 7 | `OrbitController.cs:477`, `FixedController.cs:23` | a mod `IFollowable` returning null `OrbitView` | `NullReferenceException` |
| 8 | `OrbitController.cs:513`, `:580` | `MeanRadius == 0` on the followed object | no throw — `Infinity`/`NaN` camera, everything vanishes |
| 9 | `FixedController.cs:91`, `OrbitController.cs:319` | `CameraReferenceFrame.Parent` on something whose `Orbit` is null | `NullReferenceException` |
| 10 | `IVAController.cs:40` | `Seat == null` while `Following == LastFollowing` (reachable only by writing the public `LastFollowing`) | `NullReferenceException` |
| 11 | `Program.cs:2357` (`GetCurrentAltitudeKm`, reached from `Camera.ClampCamera`) | camera exactly at the nearby celestial's centre | `DivideByZeroException` |
| 12 | `FlyController.cs:601`, `:639`, `:647` | invalid frame/target combination passed to its `GetFrame2Ecl` | `InvalidOperationException` / `NotImplementedException` |
| 13 | `OrbitController.cs:211` | out-of-range `CameraReferenceFrame` reaching `AlertCameraReference` | `IndexOutOfRangeException` |
| 14 | `Camera.cs:486` (`UpdateProjection`) | a non-invertible projection (ortho half-height and aspect are guarded, so this needs `AspectRatio` = 0/NaN, i.e. a zero-height framebuffer) | `Exception("Tried to assign non-invertible projection matrix")` |
| 15 | `Program.cs:3285` (**View → Add Camera**) | a **fourth** camera window. No mod needed — see below. | `InvalidOperationException` ("Sequence contains no matching element") |

**#15 needs no mod at all, and a player can reach it in three clicks.** The guard and the search
disagree about the offscreen thumbnail viewport:

```csharp
if (VisibleViewportCount < ViewportCount && ImGui.MenuItem("Add Camera"u8, ""u8))
{
    Viewports.First((Viewport v) => !v.Visible && !v.IsOffscreen).Visible = true;
}
```

`ViewportCount` is a fixed 4 (`Program.cs:238`) and one of the four is `ThumbnailViewport`, with
`IsOffscreen = true` (`Program.cs:864`). `VisibleViewportCount` counts `v.Visible` only
(`Program.cs:435`), so the hidden thumbnail viewport still counts as *room left* — but the search
excludes it. With the main view plus two added cameras there are three visible, `3 < 4` offers the
menu item, and the only remaining viewport is the offscreen one, so `First` matches nothing and
throws. There is no try/catch in the frame loop, so the process dies.

**So the usable ceiling is three windows: the main view and two others.** `DockingPort.cs:181`
does the same search and gets it right by testing both flags. The fix upstream is a matching
guard — `Count(v => v.Visible || v.IsOffscreen)`, or `FirstOrDefault` with a null check.

The stack is `Enumerable.First → Program.DrawMenuBar → OnDrawUiThreadSafe → App.Run`. ModMenu's
Harmony transpile of `DrawMenuBar` appears in it as `DrawMenuBar_Patch1` and is not the cause: the
throwing statement is KSA's own inside the View menu, and the splice only inserts a
`BeginMenu`/`EndMenu` pair, touching nothing in the viewport list. **This is a reason to prefer the
main-view optic** (`Ksa/SightCamera.cs`) over asking a player to open windows they have very few of.

Non-crashing misbehaviour worth the same attention:

- **A mod cannot set the camera's roll in `Fixed` mode — but it can replace the controller.**
  `FixedController.OnFrame` derives up as `normalize(a − (a·f)f)`, where `a` is
  `UnitZ.Transform(GetFrame2Ecl(following, …))`. `GetFrame2Ecl` dispatches on the followed
  object's **runtime type** first: a `Vehicle` gets local vertical under `Surface`/`Orbit`, a
  `Celestial` gets its spin axis or pole, and **anything else falls to `Identity` with the
  reference frame not read at all** — so for a mod's own `IFollowable` the axis is ecliptic +Z and
  the horizon is level only where the ecliptic pole, the local vertical and the view happen to be
  coplanar. Roll rate under a sweep is `ω̇·cot θ` with θ the angle from that axis, so a view
  passing near the pole rolls hard — up to 1094 °/s across a sweep of site orientations. Nothing on
  `Camera`, `OrbitView`, `CameraOffset`, `CameraRotation`, `TidalLocking` or `Parent` overrides it,
  and `Camera.LocalRotation` written directly is overwritten by the controller in the viewport pass
  before any mod hook runs.

  The seam that does work: `Viewport.FixedController` is a **public, writable field**,
  `FixedController` is public and unsealed with a public constructor, and `OnFrame` is virtual.
  A subclass assigned into that field supplies its own up. `Ksa/LevelHorizonController.cs` is
  that, and it is the only unclaimed extension point this mod stands on.

- **A viewport in `Fixed` mode cannot be recovered by the player from the keyboard or the mouse.**
  `FixedController` reads no input of any kind — no `Input.`, no key, no mouse — so a camera a mod
  is driving ignores every reflex a player has. `Shift+C` does not help either: it routes through
  `Viewport.NextCameraMode` (`Viewport.cs:347-363`), whose switch covers Orbit, Free and IVA and
  whose `default` returns `false`, so the key falls through to `FixedController.OnKey` and is
  dropped. The only routes out are the **View menu**, which calls `SetCameraMode` outright
  (`Program.cs:3271-3281`), and whatever the mod itself offers. **Anything that takes the main
  view must say so and offer its own way back** — `Ksa/SightCamera.cs` releases from the panel,
  and the panel names the View menu because no reflex works.

- `Camera.ClampCamera` on a secondary viewport uses the **main** viewport's altitude
  (`Camera.cs:636-650` vs `Program.cs:4555-4558`, `:2349-2360`) — it can teleport a secondary camera
  to a planet surface for no reason visible in that viewport.
- `IVAController.OnSwitchOn`/`OnFrame` and `MapController.OnFrame` bail by cycling
  `Program.HoveredViewport` / `Program.SetCameraMode`, i.e. **a different viewport from the one that
  failed** (`IVAController.cs:31`, `:36`, `:159`, `:167`; `MapController.cs:128`).
- `MapController.OnSwitchOff` unconditionally restores `Program.ControlledVehicle` and
  `IsControlledVehicleActive = true` (`MapController.cs:569-570`).
- `Camera.SetFollow` with the default `changeControl: true` nulls `Program.ControlledVehicle` for any
  non-`Vehicle` target (`Camera.cs:617-620`).
- Setting `Camera.Parent` desynchronises the view matrix (`LocalRotation`, `Camera.cs:79`) from
  `GetForwardEcl()` (`WorldRotation`, `Camera.cs:133-154`).

---

### 8. Unresolved

Not established from the source, and what would settle each:

1. **Whether `TidalLocking` has any effect.** It is written, saved and displayed, and no reader in the
   corpus changes behaviour on it (`Camera.cs:160`, `:612`, `:849-852`; consumers at `Program.cs:3288`,
   `DockingEvent.cs:23`, `Universe.cs:2237`, `MapController.cs:254` all just pass it through). Settle by
   toggling it in game and watching whether a landed craft's camera stops following the surface.
2. **Whether a `Celestial` can have a null `Orbit`.** `Celestial.Orbit` is `{get;set;}`
   (`Celestial.cs:71`) and `IOrbiter.Orbit` is declared non-nullable (`IOrbiter.cs:16`). If it can be
   null, `GetCarousel2Cce` NREs. Settle by dumping `Orbit` for every body in `Universe.CurrentSystem`.
3. **Whether `Vehicle.GetPositionEcl()` and the drawn position ever disagree by enough to matter for a
   camera.** `GetPositionEcl` is the analytic value (`Vehicle.cs:2449-2455`) and `Camera.GetPositionEgo`
   has a physics branch (`Camera.cs:237-243`); how far apart `PositionPhys`-in-bubble and the analytic
   state can drift is untraced. Settle by logging both for a landed craft.
4. **Whether `KittenEva` instances actually carry `IVASeat` modules** in shipped content. The class
   supports parts (`KittenEva.cs:47-56`) but the part templates are game content XML, which is not in
   the decompiled corpus. Settle by inspecting `Content/Core/**` in the install.
5. **Whether writing `Viewport.Visible = true` on viewports 2/3 is enough**, or whether something else
   (a render target rebuild, `NewSize`) must happen first. `DockingPort.cs:185-186` sets `Visible` and
   `NewSize` together and the rebuild happens in `OnFrameViewports` (`Program.cs:2275-2282`); whether
   the first frame renders correctly is unconfirmed. Settle by trying it.
6. **What happens to a `Fixed` viewport when the game is paused or time-warping.** `Viewport.OnFrame`
   gets `dtPlayer` (`Program.cs:2284`) and `FixedController.OnFrame` does not use `dt` at all, so it
   should keep tracking — but `Following.GetPositionEcl()` is only refreshed when the solvers run
   (`Program.cs:1911-1912`). Unverified in flight.
7. **`Camera.NoRotation` semantics for `Fixed`.** Nothing sets `BaseCamera.NoRotation = true` except
   `MapController` on `MapCamera` (`MapController.cs:535`), and `OrbitController` clears it every frame
   (`OrbitController.cs:617`, `:625`). Whether a mod would ever want it true in `Fixed` mode is not
   determinable from the source.
8. **Whether the StarMap loader offers any hook earlier than the `Program.OnFrame` postfix.** The
   loader's whole surface is the four attributes in `StarMap.API/StarMap.API/` (`BeforeGui`, `AfterGui`,
   `AfterOnFrame`, plus lifecycle) and their patches in `ProgramPatcher.cs`. `BeforeGui` is a prefix on
   `OnDrawUiFrame`, which is **after** `OnFrameViewports` (`Program.cs:1989` vs `:1993`), so it is no
   earlier for this purpose. A Harmony prefix on `Viewport.OnFrame` from the mod itself would be, but
   that is a patch, not an API.

---

# Part 2 — viewports, the render path, and lifecycle

## KSA render path, viewports and render targets — from the decompiled engine

Source: `../ksa-game-assemblies/current/src`, KSA build in `current/KSA_BUILD`.
All citations are `path/relative/to/src:line`. Nothing here comes from the KSArmory mod's own code or docs.

---

### 0. Shape of the frame

`Program.OnFrame(currentPlayerTime, dtPlayer)` (`KSA/KSA/Program.cs:2022`) in order:

| Step | Line |
| --- | --- |
| `OnFrameViewports(dtPlayer)` — per-viewport controller + camera + audio | `Program.cs:2046` |
| `OnDrawUiFrame` / `OnDrawUiViewports` (ImGui) | `Program.cs:2050`, `2051` |
| `OnFrameController(dtPlayer)` | `Program.cs:2098` |
| `LightSystem.OnFrame(FrameViewport, dtPlayer)` | `Program.cs:2100` |
| `Cursor.UpdateInputRay(GetCamera())` | `Program.cs:2101` |
| `OnFrameCelestials(dtPlayer)` | `Program.cs:2103` |
| `OnPreRender(dtPlayer)` | `Program.cs:2106` |
| `Render(dtPlayer, …)` → `UpdateShaderData` per viewport, `UpdateRenderingResources`, then `RenderGame` or `RenderEditor` | `Program.cs:2195-2226` |
| `PostRender(dtPlayer)` | `Program.cs:2114` |

`RenderGame` (`Program.cs:4206`) is the whole GPU frame. It renders **secondary** viewports first, in a loop
that calls the short path, then falls through into a long inline block for the main viewport:

```
for (int i = 1; i < ViewportCount; i++) {
    Viewport viewport = Viewports[i];
    if (viewport.Index != _mainViewportIndex && viewport.Visible)
        RenderViewport(commandBuffer2, viewport, resourceFrameIndex);   // Program.cs:4227-4234
}
_renderedViewportIndex = _mainViewportIndex;                            // Program.cs:4114
… ~200 lines of main-viewport-only passes, inline, never factored into a function …
```

`RenderViewport` is `private` (`Program.cs:3967`). `RenderGame` is `private unsafe` (`Program.cs:4085`).

---

### A. Why a secondary viewport shows stars, a hard horizon and a grey ball

**That picture is structural, not a state bug.** It is two separate facts:

#### A.1 What `RenderViewport` actually runs (`Program.cs:3967-4070`)

Complete list, in order:

| Pass | Line |
| --- | --- |
| `SuperMeshRenderSystem.UseShadows = false; UseLightPrePass = false` | `3970-3971` |
| `LaunchPadRenderer.Draw` | `3973` |
| `KittenEva` vehicles `UpdateRenderData` | `3982` |
| `_milkyWayRenderer.RenderMilkyWay` (if `ShowMilkyWay`) | `4013` |
| `_starStarTechnique.Render` (if `GameSettings.ShowStars()`) | `4015` |
| `StaticCelestialDistanceRendering.Run` (point-sprite bodies) | `4017` |
| `StaticCelestial.RenderSphere` for every `StaticCelestial` in the system | `4024` |
| `GenericMeshRenderer.WriteCommands` | `4027` |
| `PartModelRenderer.ColorData.WriteCommands` | `4028` |
| `SuperMeshRenderSystem.RenderMainPass` | `4030` |
| `_sunbloomRenderer.Render` | `4040` |
| `SingleToMultisamplePass.Run` (if MSAA) | `4044` |
| `SuperMeshRenderSystem.RenderTranslucencyPass` | `4051` |
| `OrbitLinePass.Run` (if `DrawUI`) | `4054` |
| `GizmoPass.Run` | `4056` |
| `RenderAaPreparePasses` + `RenderFinalComposite` into `viewport.MainTarget` | `4059-4069` |

That is: **stars, distant-body sprites, distant-body spheres, vehicle meshes, orbit lines, part gizmos.**
Nothing else.

#### A.2 Every pass that only ever runs for the main viewport

All of these are inline in `RenderGame` **after** `_renderedViewportIndex = _mainViewportIndex`
(`Program.cs:4114`), i.e. strictly outside the per-viewport loop at `4106-4113`:

| Pass | Line |
| --- | --- |
| `PrePassRenderer.Render` (depth/normal pre-pass) | `4133` |
| `AmbientOcclusionRenderer.Render` | `4136` |
| `LightSystem.RenderShadowPass` | `4138` |
| `LightSystem.DispatchComputePasses` | `4141` |
| `_planetRenderer.GenerateMeshData` | `4143` |
| `_planetRenderer.UpdateUvOffsets` | `4144` |
| `_planetRenderer.OnFrameGroundClutter` (hardcoded `MainViewport`) | `4145` |
| `_planetRenderer.UpdateGroundClutter` | `4146` |
| `SunShadowSystem.TerrainShadowMap` + `_planetRenderer.RenderSunShadow` | `4147-4149` |
| `_cascadedShadowSystem` cascades + `_planetRenderer.RenderShadow` | `4150-4167` |
| `_planetTransparenciesRenderer.UpdateRingMeshes` | `4168` |
| `PartSelectedRenderer.ClearColorImage` / `.Run` | `4170`, `4272` |
| `RaytracingRenderer.RunRaytracePass` (IVA only) | `4174` |
| `_clutterPrePass` + `_planetRenderer.RenderDepthPrePass` | `4182-4184` |
| `_sunRenderer.Draw()` | `4206` |
| `_planetTransparenciesRenderer.RenderRingMeshes` | `4219` |
| **`_planetRenderer.Render` — the terrain** | `4223` |
| `_volumetricExhaustRenderer.RenderDebugWireFrame` / `.Render` | `4227`, `4262` |
| **`GizmosRenderer.Render` — the debug line/sphere gizmos** | `4228` |
| `ParticleSystem.UpdateParticles` / `WriteCommandsColorOpaque` / `…Translucent` | `4234`, `4235`, `4288` |
| `_planetRenderer.ProcessExporter` | `4245` |
| **`_oceanRenderer.Render` / `.RenderUnderwater`** | `4247`, `4298` |
| `_transparenciesMsaaResolve` | `4250`, `4263` |
| **`_planetTransparenciesRenderer.Render` — atmosphere + clouds** | `4254` |
| `_overallBloomRenderer.Render` | `4270` |
| `PartModelGlass.Shared.WriteCommandsColor` | `4281` |
| `GridPass.Run` | `4302` |
| `GaugeCanvas.CanvasesRender` | `4306` |
| `ImGauge.PrepareRender` | `4310` |
| `_screenshotCapture.OnRenderGameCapture` / `OnRenderGameSwapchainGrab` | `4313`, `4319` |

So: **no terrain, no atmosphere, no clouds, no ocean, no light pre-pass, no shadows (terrain or
cascaded), no ambient occlusion, no particles, no exhaust, no bloom (other than sun bloom), no
`GizmosRenderer` lines/spheres, no gauges, no screenshot capture** in a secondary viewport.

Note in particular for a mod that draws diagnostics with `GizmosRenderer.DrawLine`/`DrawSphere`:
`GizmosRenderer.Render` (`KSA/KSA/GizmosRenderer.cs:337`) is called only at `Program.cs:4228`
(game) and `Program.cs:4386` (editor). Those primitives are invisible in a secondary viewport.
`GizmoPass` (`Program.cs:4056`) is a different subsystem — the part-manipulation gizmos in
`GenericGizmo` — and it does run per viewport.

#### A.3 Where the grey ball comes from

`StaticCelestialDistanceRendering.UpdateRenderData(viewport, frameIndex)` runs per visible viewport
(`Program.cs:3906`). Inside it (`KSA/KSA/StaticCelestialDistanceRendering.cs:349-374`):

```
if (!(astronomical is IOrbiter orbiter) || camera.NearbyCelestial == orbiter || …) continue;   // :352
…
if (num4 > 2.0 && staticCelestial != null)
    staticCelestial.RenderSphereThisFrame[viewport.Index] = true;                              // :372-373
```

The body you are standing on is excluded **only** by `camera.NearbyCelestial == orbiter`. A secondary
camera's `NearbyCelestial` is always `null` (see A.4), so the home planet is not excluded and is flagged
for `RenderSphere`. `StaticCelestial.RenderSphere` (`KSA/KSA/StaticCelestial.cs:33-39`) then invokes
`DistantSphereRenderer.Render` (`KSA/KSA/DistantSphereRenderer.cs:72`), which draws:

- a `ModLibrary.Get<MeshReference>("Sphere")` (`StaticCelestial.cs:22`)
- scaled to `_celestial.MeanRadius` — the **mean** radius, no heightfield (`DistantSphereRenderer.cs:77`)
- textured from a streamed low-res diffuse/normal cubemap (`DistantSphereRenderer.cs:96-97`)
- with `VkCullModeFlags.BackBit` and depth test+write (`DistantSphereRenderer.cs:117`)

That is the "featureless grey ball", and its limb against the star pass is the "hard horizon". Because
the sphere is at *mean* radius, a camera on high terrain sees the sphere below it; a camera below mean
radius is inside the sphere and back-face culling removes it entirely, leaving pure starfield.

#### A.4 `NearbyCelestial` is per-camera state that only the frame viewport's camera ever gets

`Camera.NearbyCelestial` is a plain `{ get; set; }` (`KSA/KSA/Camera.cs:66`). The **only** places it is
assigned in the whole engine are `Program.OnFrameCelestials`:

```
private void OnFrameCelestials(double deltaTime) {
    Camera camera = GetCamera();                       // Program.cs:2318
    …
    camera.NearbyCelestial = null;                     // Program.cs:2326  (no billboarded body)
    camera.NearbyCelestial = astronomical as Celestial; // Program.cs:2329
    …
    camera.NearbyCelestial = null;                     // Program.cs:2345  (>80 000 km from surface)
}
```

`GetCamera()` is `FrameViewport.GetCamera()` (`Program.cs:558-561`), and `FrameViewport` is
`Viewports[_frameViewportIndex]` (`Program.cs:445`). At the moment `OnFrameCelestials` runs
(`Program.cs:2047`), `_frameViewportIndex` has been reset to `_mainViewportIndex` by the previous frame's
`UpdateRenderingResources` (`Program.cs:3849` and again `3864`), and `OnDrawUiViewports` saves and
restores it around its own loop (`Program.cs:2767`, `2777`). `_mainViewportIndex` is assigned exactly once,
to `0`, at boot (`Program.cs:860`) and never again.

So: **`NearbyCelestial` is set on exactly one camera per frame — `Viewports[0]`'s active camera — and is
`null` on every other camera for the entire session.** Note also `Viewport.GetCamera()` returns `MapCamera`
in `CameraMode.Map` and `BaseCamera` otherwise (`Viewport.cs:333-339`), so even on the main viewport only
the *currently active* camera of the two gets it.

Everything gated on `GetRenderCamera().NearbyCelestial != null && …IsBillboarded()` — the planet render
(`Program.cs:4220-4224`), the ocean (`4242-4248`, `4295-4299`) — would therefore be skipped for a secondary
camera even if the call sites were inside the loop.

#### A.5 Other per-camera state a secondary camera never gets

| State | Set where | Consequence |
| --- | --- | --- |
| `NearbyCelestial` | `Program.cs:2326-2345`, frame viewport only | above |
| `DistanceToNearbyCelestialKm`, `…SurfaceMeanKm`, `NearbyCelestialTerrainHeight`, `CurrentAltitudeKm` | `Program.cs:2332-2341`, same block | stay 0 |
| `_sunPositionEgo`, `_sunRenderer.UpdateShaderData` | `Program.cs:2507-2510`, guarded `viewport.Index == _mainViewportIndex` | no sun disc |
| `_lightingData[i].PlanetColor/PlanetRadius/AtmosphereLutLayer/OcclusionColor` | `UpdatePlanetShaderData(nearbyCelestial, viewport)` `Program.cs:2610-2646`; with `null` it takes the `else` branch and writes `PlanetColor = float4.Zero` (`2645`) | unlit by planet bounce |
| `BiomeSoundController` | constructed only for `index == 0` (`Viewport.cs:113-116`) | no environment audio |
| IVA audio blend | early-returns for `Index != 0` (`Viewport.cs:151-154`) | — |
| `SunShadowSystem.UpdateUniforms` / `_cascadedShadowSystem.UpdateUniforms` | `Program.cs:3865-3866`, against `RenderedViewport` which is main at that point | shadow maps are main-camera-fitted |

What a secondary camera **does** get: `_cameraData`, `_lightingData` (sun position/flare),
`_celestialData` and `_vesselData` are filled per viewport by `UpdateShaderData(dt, viewport)`, called for
every viewport at `Program.cs:2151-2155`. `GlobalShaderBindings` reserves a per-viewport slice of the
uniform buffer and `DynamicOffset(viewportIndex)` selects it (`KSA/KSA/GlobalShaderBindings.cs:57-66`).
So the *camera matrices* are correct in a secondary viewport; the *scene passes* are missing.

#### A.6 Could a mod make a secondary viewport draw a planet?

**No, not through the public API, and not cleanly even with Harmony.** Three independent blockers:

1. **The calls are hardcoded and unreachable.** `_planetRenderer`, `_oceanRenderer`,
   `_planetTransparenciesRenderer`, `_cascadedShadowSystem`, `_clutterPrePass`, `PrePassRenderer` are
   private instance/static fields, and `RenderGame`/`RenderViewport` are private methods. `Program`
   exposes `GetPlanetRenderer()` (`Program.cs:509`) and `GetOceanRenderer()` (`Program.cs:514`) publicly,
   but there is no hook that hands a mod a `CommandBuffer` mid-frame (see B.3).

2. **The renderers are bound to a single render target at construction.** `PlanetRenderer` takes an
   `IRenderPassInfo` in its constructor (`KSA/KSA/PlanetRenderer.cs:414`) and is built with
   `_offscreenTarget` (`Program.cs:1054`); so are `_planetTransparenciesRenderer` (`1059`),
   `_volumetricTrailRenderer` (`1067`), `_oceanRenderer` (`1071`), `_overallBloomRenderer` (`1080`),
   `GizmosRenderer` (`1000`), `_volumetricExhaustRenderer` (`996`), `_transparenciesMsaaResolve` (`991`).
   `_offscreenTarget` is the **main viewport's** target: `MainViewport.OffscreenTarget = _offscreenTarget`
   (`Program.cs:1385`), sized to `MainViewport.Size` (`Program.cs:1370`). `RenderTechnique.CreatePipeline`
   bakes that pass info into the pipeline (`KSA/RenderCore/RenderTechnique.cs:110-114`). Calling them with
   a secondary `Viewport` would still write into the main offscreen image.
   (Formats do happen to match — `Viewport.BuildRenderTarget` uses `Program.Instance.ColorFormat`,
   `_renderer.DepthFormat` and `GameSettings.GetSampleCount()`, `Viewport.cs:186-193` — so the obstacle is
   resource identity and extent, not pipeline compatibility.)

3. **The supporting resources are singletons sized to the main viewport / swapchain.**
   `PrePassRenderer`, `AmbientOcclusionRenderer`, `_clutterPrePass` (`Program.cs:1379`),
   `GizmoPass.ColorImage`/`DepthImage` (built at `renderer.Extent`, `KSA/KSA/GizmoPass.cs:70-72`),
   the atmosphere LUTs, the terrain and cascade shadow maps. There is exactly one set, fitted to the main
   camera each frame.

Repointing `_mainViewportIndex` at a secondary viewport does not work either: `RenderGame` composites
with a hardcoded index 0 (`RenderFinalComposite(commandBuffer2, 0, resourceFrameIndex)`,
`Program.cs:4316`), and `_compositeRenderer[0]` is wired to `_offscreenTarget.ColorImage`
(`Program.cs:1090-1092`).

**The one thing that would change what a secondary viewport shows, cheaply:** the grey ball is drawn
because `camera.NearbyCelestial != orbiter` (`StaticCelestialDistanceRendering.cs:352`). `NearbyCelestial`
is a public settable property (`Camera.cs:66`). Assigning the home body to the secondary camera each frame
suppresses the distant sphere — leaving a plain starfield with vehicles in it, no terrain. Nothing in the
`RenderViewport` path would then draw the planet at all. It also changes what `Camera.ClampCamera` does
(see D.4) and what `BiomeSoundController`/`PhysicalAtmosphereReference` compute, so it is not free.

---

### B. Render targets, render passes and shaders

#### B.1 Render targets — a mod can create them

`KSA.Rendering.RenderTarget` is `public sealed`, `IDisposable`, `IRenderPassInfo`, with a public
constructor `RenderTarget(IVulkanContext, string, VkExtent2D, VkFormat colorFormat, VkFormat depthFormat,
int mipLevels = 1, VkSampleCountFlags = _1Bit, bool preserveMultisampleAttachments = true)`
(`KSA/KSA.Rendering/RenderTarget.cs:8`, `:87`). Colour images are created with
`SampledBit | StorageBit | ColorAttachmentBit` (`RenderTarget.cs:110`), so they can be sampled and
written by compute. `ColorImage`, `DepthImage`, `BeginRendering`, `EndRendering`, `ResolveAttachments`,
`Rebuild`, `SetupGraphicsPipeline` are all public (`RenderTarget.cs:36-48`, `173-427`).

`Program.GetRenderer()` (`Program.cs:504`) and `Program.GetRendererContext()` (`Program.cs:4580`) are
public statics; `RendererContext` carries `Renderer`, `IRenderPassInfo`, `FrameCount`
(`KSA/KSA/RendererContext.cs:6-14`). `Program.OffscreenTarget` (`Program.cs:409`) and
`Program.MainPass` (`Program.cs:407`) are public statics too, so a mod can *read* the main colour image.

An ImGui-displayable texture is available the same way `Viewport` does it:
`ImGuiBackend.Vulkan.AddTexture(sampler, colorImage.ImageView)` returning an `ImTextureRef`
(`Viewport.cs:207-212`), drawn with `ImGui.ImageWithBg` (`Viewport.cs:262`).

#### B.2 Shaders — compiled from GLSL source at runtime, not precompiled SPIR-V

`ShaderReference.Compile()` calls `ShaderModuleUtils.FromFile(renderer.Device, ModPath, out Stage,
Options)` using `Brutal.ShaderCApi.CompileOptions` (`KSA/KSA/ShaderReference.cs:80-113`). It resolves
`#include`s through a callback and records the include paths for hot reload
(`ShaderReference.cs:105-107`, `133-160`). Stage is inferred from the file extension
(`ShaderReference.cs:48`). There is a `CompileVariantWithCustomOptions(CompileOptions)` public method
(`ShaderReference.cs:114`). `Brutal.ShaderC.dll` ships with the game (`current/dll/Brutal.ShaderC.dll`).

`ShaderReference` is a first-class asset type: `[XmlElement("Shader", typeof(ShaderReference))]` in the
asset bundle schema (`KSA/KSA/AssetBundle.cs:21`), alongside `[XmlElement("GaugeShader", …)]` (`:22`).
It derives from `FileReference` and resolves `ModPath` against the owning mod
(`ShaderReference.cs:44-49`, `:88-92`). **A content mod can therefore ship its own `.vert`/`.frag`/`.comp`
and have the engine compile it at load**, retrievable with `ModLibrary.Get<ShaderReference>("<Id>")` —
that is exactly how `StaticCelestial` gets `DistantSphereVert`/`DistantSphereFrag`
(`StaticCelestial.cs:18-21`) and how `ThumbnailRenderer` gets `ThumbnailMeshVert`/`ThumbnailMeshFrag`
(`KSA/KSA.Rendering.Thumbnails/ThumbnailRenderer.cs:63-64`).

There is a hot-reloader (`KSA/KSA.AssetReloader/ShaderReloader.cs`) and
`Program.DisableHotShaders()` (`Program.cs:4605`).

`RenderTechnique` is `public abstract` with a constructor taking `(name, Renderer, IRenderPassInfo,
Span<ShaderReference>, Span<VkSpecializationInfo>)` and protected `CreatePipeline` helpers
(`KSA/RenderCore/RenderTechnique.cs:13`, `:64-70`). Subclassing it from a mod is possible; the only
abstract members are `MakeVertexInput()` and `OnRebuildFrameResources()` (`RenderTechnique.cs:135-139`).

#### B.3 Adding a render pass — there is no hook

StarMap's entire surface is five method attributes plus load/unload
(`StarMap.API/StarMap.API/*.cs`):

| Attribute | Signature | Patched at |
| --- | --- | --- |
| `StarMapBeforeGui` | `void(double)` | Prefix on `Program.OnDrawUiFrame` (`StarMap.Core/StarMap.Core.Patches/ProgramPatcher.cs:17-19`) |
| `StarMapAfterGui` | `void(double)` | Postfix on `Program.OnDrawUiViewports` (`ProgramPatcher.cs:28-30`) |
| `StarMapAfterOnFrame` | `void(double, double)` | Postfix on `Program.OnFrame` (`ProgramPatcher.cs:39-41`) |
| `StarMapBeforeMain`, `StarMapAllModsLoaded`, `StarMapImmediateLoad(Mod)`, `StarMapUnload` | load-time | — |

**None of them carries a `CommandBuffer`, a `Viewport`, a `RenderTarget` or a frame index.**
`StarMapAfterOnFrame` fires after `PostRender` has already submitted and presented the frame
(`Program.cs:2058-2059`). So a mod cannot inject a pass into KSA's frame graph through the loader API.

StarMap.Core itself uses Harmony (`using HarmonyLib;`, `ProgramPatcher.cs:3`), so `0Harmony` is present at
runtime, and a mod could in principle patch `Program.RenderViewport` or `Program.RenderGame` directly.
That is unsupported and fragile — both are private and their bodies change between builds — but it is the
only route to a mid-frame `CommandBuffer`.

#### B.4 The supported alternative: render your own frame, out of band

`ThumbnailCreator` is a complete worked example of a mod-shaped offscreen render using only public API
(`KSA/KSA.Rendering/ThumbnailCreator.cs`):

- create a `VkCommandPool` from `renderer.Device.CreateCommandPool` (`:35-40`)
- drive a `Viewport`'s camera by hand and call `Program.Instance.UpdateShaderData(dt, viewport)` and
  `Program.Instance.UpdateRenderingResources(0)` — **both public** (`:69-71`,
  `Program.cs:2468`, `Program.cs:3846`)
- build a pipeline from `ModLibrary.Get<ShaderReference>` modules (`ThumbnailRenderer.cs:63-76`)
- allocate a command buffer, record, `SubmitOneTimeRenderCommand`, wait on a fence (`:96-113`)
- render into an image created with `ColorAttachmentBit | SampledBit | TransferSrc/DstBit` (`:167`)

It also renders through `GlobalShaderBindings.DescriptorSetLayout` + `DynamicOffset(viewport.Index)`
(`ThumbnailRenderer.cs:52-56`), i.e. a mod's own pipeline can consume KSA's per-viewport camera UBO.

#### B.5 Verdict for a thermal/FLIR channel

- **A full-screen colour grade of the existing main view: possible in principle but with no clean hook.**
  `Program.OffscreenTarget.ColorImage` is public and `SampledBit|StorageBit`, and a mod can compile its own
  compute/fragment shader and build a pipeline. What it cannot do is get a command buffer at the right
  point in the frame without Harmony-patching a private method, and it cannot make its result land in the
  composite — `RenderFinalComposite` reads `_compositeRenderer[viewportIndex]` / `_cmaa2Renderers[…]`,
  both private arrays (`Program.cs:4074-4083`).
- **A separate thermal *viewport*: the camera can be positioned and displayed, but there is nothing to
  grade.** A secondary viewport renders only stars, sprites, distant spheres and vehicle meshes (A.1). A
  thermal render of that is a thermal render of a starfield.
- **A mod-owned offscreen render with its own shaders and its own ImGui window: fully supported**
  (B.1, B.2, B.4) — but the mod must supply the geometry and materials itself, because it cannot invoke
  KSA's terrain/atmosphere/ocean renderers into its own target (A.6, blocker 2).

---

### C. Viewport lifecycle

#### C.1 Creation — all at boot, fixed count

```
public static int ViewportCount = 4;                                       // Program.cs:238
public static readonly List<Viewport> Viewports = new List<Viewport>();    // Program.cs:240
```

`using (Loading.Task("Build Viewports"))` (`Program.cs:857-871`):

| Index | Created as | Size | Own render targets |
| --- | --- | --- | --- |
| 0 | `_mainViewportIndex = 0` (`:860`) | swapchain extent | **no** — `buildRenderTarget: false` (`:861`); later `MainViewport.OffscreenTarget = _offscreenTarget` (`:1385`), `MainTarget` stays null and it composites straight to the swapchain |
| 1 | `_thumbnailViewportIndex = 1` (`:862`), `IsOffscreen = true`, `ShouldRenderGizmos = false` (`:864-865`) | `ThumbnailRenderer.SIZE` | yes |
| 2, 3 | loop `for (num4 = 2; num4 < ViewportCount; num4++)` (`:866-869`) | 500×500 | yes |

`AddViewport` (`Program.cs:583-604`) is `private`. Each viewport gets **two** cameras — `BaseCamera` and
`MapCamera` — plus five controllers: `FlyController`, `OrbitController`, `MapController`, `IVAController`,
`FixedController` (`Viewport.cs:99-118`).

**So there are exactly two viewports available to a mod: indices 2 and 3.** `VisibleViewportCount`
(`Program.cs:435`) counts `Visible`, and the "Add Camera" menu item picks
`Viewports.First(v => !v.Visible && !v.IsOffscreen)` (`Program.cs:3283-3286`). `DockingPort` does the same
scan (`KSA/KSA/DockingPort.cs:178-192`).

`ViewportCount` is a public mutable static, but raising it after boot is not viable: `_sunflareData`
(`Program.cs:984`), `_compositeRenderer` (`:1011`), `_cmaa2Renderers` (`:1032`), `_cameraData`,
`_lightingData`, `_celestialData`, `_vesselData` (`:1085-1088`), `StaticCelestial.RenderSphereThisFrame`
(`StaticCelestial.cs:15`) and the `GlobalShaderBindings` uniform buffer
(`GlobalShaderBindings.cs:91`) are all sized from it at initialisation.

#### C.2 Destruction

`Viewports` is never cleared and no viewport is destroyed at runtime. `Viewport.Dispose` is called only
from `Program.Dispose` at shutdown (`Program.cs:1246-1249`), and it only frees render targets when
`_ownsRenderTargets` (`Viewport.cs:124-137`) — which is set by `BuildRenderTarget()` (`Viewport.cs:184`),
so true for 1/2/3 and false for 0.

"Closing" a viewport is `Visible = false`. `Viewport.DrawImGui` binds `ref Visible` to
`ImGui.Begin(_viewportName, ref Visible, flags)` (`Viewport.cs:252`), so the window's X button clears it.
`DockingPort.ResetDockingViewport` shows the intended teardown: `Visible = false`,
`SetCameraMode(CameraMode.Orbit)`, `AllowResize = true` (`DockingPort.cs:132-141`), called from
`DockingPort.Dispose` (`:129`).

`RebuildViewport(viewport)` (`Program.cs:4427-4443`) does `Device.WaitIdle()`, resizes the viewport's
targets, and rebuilds `OrbitLinePass`, `_sunbloomRenderer`, `_compositeRenderer[index]`,
`_cmaa2Renderers[index]`, `GizmoPass`, `SingleToMultisamplePass`. It is driven from `OnFrameViewports`
when `NewSize` differs or the sample count changed (`Program.cs:2273-2281`).

#### C.3 `MainViewport` vs `Viewports`

`MainViewport => Viewports[_mainViewportIndex]` (`Program.cs:437`) — **it is element 0 of the list**, not a
separate object. Same for `HoveredViewport` (`:439`), `RenderedViewport` (`:441`), `ThumbnailViewport`
(`:443`), `FrameViewport` (`:445`). `_mainViewportIndex` is private and only ever `0` (`Program.cs:860`).

#### C.4 What happens to a camera a mod has moved

| Event | Effect | Cite |
| --- | --- | --- |
| **Vehicle destroyed** (`Universe.DestroyVehicleFromEvent`) | loops **all** viewports; any `BaseCamera` following the vehicle is switched to `FollowWreckage(vehicle, cause)`; any `MapCamera` following it is re-followed onto `vehicle.Parent` | `KSA/KSA/Universe.cs:1722-1731` |
| `FollowWreckage` | creates a `WreckageMarker` and calls `SetFollow(marker, tidalLocking: false)` — which also sets `Program.ControlledVehicle = target as Vehicle` (null here) because `changeControl` defaults true | `Camera.cs:598-604`, `:610-627` |
| **Save loaded** (`Universe.DeserializeSave`) | loops all viewports, `BaseCamera.Unfollow()` and `MapCamera.Unfollow()` | `Universe.cs:2154-2159` |
| `Unfollow(changeControl: true)` | clears `_following`, `_wreckageMarker`, `_tidalLocking`, and sets `Program.ControlledVehicle = null` | `Camera.cs:629-639` |
| **Camera mode change** | `SetCameraMode` calls `OnSwitchOff`/`OnSwitchOn` on the controllers and `Program.ControlledVehicle?.ClearHeldPlayerInput()` | `Viewport.cs:341-352` |
| **Quit** | `Program.Dispose` → `viewport.Dispose()` | `Program.cs:1246-1249` |

A camera position written directly is overwritten every frame by the active controller:
`Viewport.OnFrame` runs `GetActiveController().OnFrame(this, dt)` then `GetCamera().OnFrame(dt)`
(`Viewport.cs:139-144`). `FixedController.OnFrame` recomputes `PositionEcl` and `LocalRotation` from
`Camera.Following`, `CameraOffset` and `CameraRotation` every frame (`KSA/KSA/FixedController.cs:19-34`)
and does nothing at all if `Following` is null. `CameraMode.Fixed` is the mode a mod-driven camera wants;
note it is deliberately excluded from `NextCameraMode()`'s cycle, which is Orbit→Free→IVA→Orbit
(`Viewport.cs:354-368`), so the player cannot rotate out of it with the camera key.

#### C.5 Camera state in the save

`UniverseData.Create()` serialises **only `Program.MainViewport.BaseCamera`**
(`KSA/KSA/UniverseData.cs:43-49`). `Camera.SerializeSave` writes `Program.MainViewport.Mode`,
`LocalPosition`, `LocalRotation`, the followed object's Id, tidal locking, and the map controller's
inverted flag / previously-controlled vehicle (`Camera.cs:815-829`). `CameraData` is a field of
`UniverseData` (`UniverseData.cs:14`), which is the fixed XML-mapped save class.

**Consequences for a mod:** a mod that moves viewport 2/3's cameras changes nothing in the save. A mod
that moves `MainViewport.BaseCamera` or changes `MainViewport.Mode` **is** persisted, and
`Camera.DeserializeSave` will restore that mode and follow target on load (`Camera.cs:831-849`).

---

### D. Assorted

#### D.1 Free-flying and fixed cameras the game already has

`CameraMode` is Orbit / Free / Map / IVA / Fixed, selected via `Viewport.GetActiveController()`
(`Viewport.cs:320-331`).

- **Free** = `FlyController`, which reads `Program.GetNearbyCelestial()` for its speed scaling
  (`KSA/KSA/FlyController.cs:859`) — a global, not per-viewport, so a secondary free camera scales its
  speed off the *main* camera's nearby body.
- **Fixed** = `FixedController` — position `Following.GetPositionEcl() + CameraOffset`, orientation from
  `CameraRotation` plus the followed object's `OrbitView.ReferenceFrame`
  (`FixedController.cs:19-34`, `:36-84`). This is the pattern the docking-port camera uses
  (`DockingPort.cs:180-192`) and is the natural one for a mod-driven optical head.
- **Orbit zoom is camera distance, not FOV**: `OrbitView.DistancePower` (`KSA/KSA/OrbitController.cs:381-411`).
  **`OrbitView` belongs to the followed object, not to the viewport** — `IFollowable.OrbitView`
  (`KSA/KSA/IFollowable.cs:5`), `Astronomical.OrbitView` (`KSA/KSA/Astronomical.cs:60`, `:106`). Two viewports
  in Orbit mode following the same craft therefore **share** azimuth, elevation and zoom.

#### D.2 FOV, near and far planes

- Near plane `0.1f`, far plane `1.4959786E+11f` (1 AU), ortho far `50000f` — all `private const`
  with public read-only accessors (`Camera.cs:30-38`, `:63-67`). **Not settable.** Reverse-Z projection
  (`Camera.UpdateProjection`, `Camera.cs:473-487`).
- FOV: `_fovRadians`, default `0.87266463f` = 50° (`Camera.cs:53`). `SetFieldOfView(float fovDegrees)`
  (`Camera.cs:420-424`) is **unclamped**; `ChangeFieldOfView(float)` clamps to `[15, 120]`
  (`Camera.cs:458-465`, constants at `:40-42`). `GetFieldOfView()` returns **radians** (`Camera.cs:782`).
  `GameSettings.ApplyTo(Camera)` calls `SetFieldOfView(FieldOfView)` (`KSA/KSA/GameSettings.cs:2346-2348`),
  and `AddViewport` applies it to both cameras at creation (`Program.cs:587`, `:591`).
- `SetOrthographic(bool)` / `SetOrthoHalfHeight(float)` are public (`Camera.cs:426-445`).

#### D.3 Screenshots

`ScreenshotCapture` (`KSA/KSA/ScreenshotCapture.cs`), registered as a terminal command
`Screenshot(int, string)` (`Program.cs:1391`). Flags `ui`, `hud`, `warm=N` (`ScreenshotCapture.cs:105-136`).
Two modes: a swapchain grab (`ui`, includes ImGui) or a supersampled offscreen capture, scale clamped to
`[1, 8]` and further reduced against `MaxImageDimension2D`/`MaxFramebuffer*` limits and free device memory
(`:141-180`). Files go to `Documents/exports/screenshots` (`:78`). It hooks the frame at
`_screenshotCapture.OnRenderGameCapture` (`Program.cs:4313`) and `OnRenderGameSwapchainGrab`
(`Program.cs:4319`) — **main viewport only**; there is no per-viewport capture. It drives
`Program.CompositeViewportSizeOverride` (`ScreenshotCapture.cs:323`, cleared at `:210`, `:409`, `:436`),
which `Program.SetViewport` honours (`Program.cs:3950`) — a public static `int2?` a mod could set and
thereby corrupt every viewport's scissor rect.

#### D.4 Crash and corruption surfaces a mod can reach

1. **`Camera.ClampCamera` reads the *frame* viewport's state, not its own.** It runs from
   `Camera.OnFrame` (`Camera.cs:492`) for **every** camera, but tests
   `Program.GetNearbyCelestial()` and `Program.GetCurrentAltitudeKm()`
   (`Camera.cs:636-651`), both of which resolve through `FrameViewport` (`Program.cs:558`, `4555-4558`,
   `2350-2360`). During `OnFrameViewports` the frame index is the main viewport, so when the **main**
   camera is under 0.5 m altitude, **every** camera — including a mod's secondary one — is teleported to
   `surfacePositionEclFromDirCce(mainCameraDirection) + dir * 0.5`. A mod-driven camera on a landed craft
   is exactly the case that triggers this.
2. **`Camera.SetFieldOfView` throws for out-of-range values.**
   `ReverseDepthBufferUtils.CreatePerspectiveFieldOfViewReverseZ` throws `ArgumentOutOfRangeException`
   when `fieldOfView <= 0 || >= π` (`Planet.Render.Core/…/ReverseDepthBufferUtils.cs:8-13`), reached
   from `UpdateProjection` (`Camera.cs:481`). `SetFieldOfView` does not clamp (`Camera.cs:420-424`), so
   `SetFieldOfView(0)` or `SetFieldOfView(180)` throws out of a mod's frame hook.
3. **Zero-height viewport ⇒ non-invertible projection.** `Camera.Resize` computes
   `AspectRatio = X / Y` with no guard (`Camera.cs:467-471`); `UpdateProjection` then fails
   `float4x4.Invert` and throws `Exception("Tried to assign non-invertible projection matrix")`
   (`Camera.cs:483-486`). `Viewport.SetSize` calls `Resize` on both cameras (`Viewport.cs:314-320`), and
   `RebuildViewport` only guards against `NewSize == int2.Zero` — **both** components zero
   (`Program.cs:4429`). A `NewSize` of `(500, 0)` passes the guard.
4. **`Universe.DestroyVehicleFromEvent` will steal a mod's camera.** It iterates every viewport and
   re-follows any camera pointed at the destroyed vehicle (`Universe.cs:1722-1731`), and `SetFollow`
   defaults `changeControl: true`, which writes `Program.ControlledVehicle` (`Camera.cs:610-626`).
5. **Making the offscreen thumbnail viewport visible burns a full render.** `RenderGame`'s loop tests
   only `viewport.Index != _mainViewportIndex && viewport.Visible` (`Program.cs:4109`), while
   `Viewport.DrawImGui` early-returns on `IsOffscreen` (`Viewport.cs:235-240`). Setting
   `ThumbnailViewport.Visible = true` renders it every frame and never shows it.
6. **`GizmoPass` writes into a swapchain-sized shared image for every viewport.** `GizmoPass.ColorImage`
   and `DepthImage` are built at `renderer.Extent` (`GizmoPass.cs:64-84`) and are a single static pair
   (`GizmoPass.cs:15-17`), reused across viewports; `Program.SetViewport` sets the Vulkan viewport to
   `RenderedViewport.Size` (`Program.cs:3947-3966`). Not a mod-reachable crash, but it means gizmo-pass
   contents are not isolated per viewport.

#### D.5 ImGui overlays over a secondary viewport are supported

`Viewport.DrawImGui` records `Position = ImGui.GetCursorScreenPos()` and `Size` before drawing the image
(`Viewport.cs:255-262`), and both are public fields (`Viewport.cs:30-34`). Inside the window it calls
`Universe.OnDrawUi(this)` (`Viewport.cs:282`, → `Universe.cs:1598-1601`) and
`GetCamera().NearbyCelestial?.DrawUiNearby(...)` (`Viewport.cs:283` — a no-op for a secondary viewport,
per A.4). StarMap's `StarMapAfterGui` is a **postfix** on `OnDrawUiViewports` (`ProgramPatcher.cs:28-30`),
so a mod draws after all viewport windows exist and can use their `Position`/`Size` to overlay them.
`OnDrawUiViewports` also sets `_frameViewportIndex` to each viewport while calling `DrawImGui` and
restores it afterwards (`Program.cs:2766-2778`) — so `Program.GetCamera()` inside a viewport's own UI
refers to that viewport, but inside a StarMap `AfterGui` hook it is back to the main viewport.

#### D.6 Cursor rays

`Cursor.UpdateInputRay(GetCamera())` uses the frame (main) camera once per frame (`Program.cs:2045`).
Per-viewport picking must use `viewport.GetCamera().ScreenToEgoRay(...)` directly
(`Camera.cs:734-752`), which divides by that camera's own `FramebufferSize`
(`Camera.ScreenToEgoNearPlane`, `Camera.cs:706-732`). `Camera.FramebufferSize` is set by
`Camera.Resize` from `Viewport.SetSize` (`Camera.cs:467-471`, `Viewport.cs:314-320`), i.e. it equals
`Viewport.Size`, and `Viewport.Position` is the viewport window's top-left in screen space
(`Viewport.cs:255`).

---

### Unresolved

- **Whether the secondary path's atmosphere-LUT barriers imply any sky contribution.** `RenderViewport`
  transitions `PlanetAtmosphereRenderer.AmbientTarget`, `SkyColorRgbTransmittanceR` and
  `SkyTransmittanceGb` (`Program.cs:3991-3993`, `4034-4036`) and runs `_sunbloomRenderer.Render`
  (`:4040`), which samples them. Whether any visible sky tint results, or only the sun-flare/bloom effect,
  needs the shader source (`SunbloomRenderer`'s GLSL) or an in-game look. The `.vert`/`.frag` files are
  game content and are not in the decompiled `src` tree.
- **Whether `Brutal.ShaderC` compiles at load or caches SPIR-V to disk.** `ShaderReference.Compile` calls
  `ShaderModuleUtils.FromFile` (`ShaderReference.cs:105`); whether `FromFile` has a pipeline/SPIR-V cache
  is untraced in `Brutal.Render.Common`/`Brutal.ShaderC`. The observable fact — that GLSL source ships
  with the game and is compiled by shaderc at runtime — holds either way. Reading
  `Brutal.Render.Common/.../ShaderModuleUtils.cs` would settle it.
- **Whether a mod's `<Shader>` element actually survives `ModLibrary.LoadAll()` from a non-Core mod.**
  The XML element exists (`AssetBundle.cs:21`) and `ShaderReference` resolves `ModPath` against its owning
  `Mod` (`ShaderReference.cs:88-92`). `ModLibrary.LoadAll` / `DoLoad` ordering is untraced, so whether
  shaders from a late-loading mod are compiled before pipelines that might reference them is unconfirmed.
  Testing it in game, or reading `KSA/KSA/ModLibrary.cs` load ordering, would settle it.
- **Whether `0Harmony.dll` is redistributed with StarMap or expected beside it.** `StarMap.Core` uses
  `HarmonyLib` (`ProgramPatcher.cs:3`) but no Harmony assembly is in the mirrored game/loader DLL set
  (`current/dll/`). Checking the actual StarMap install directory would settle it.
- **Exact pixel threshold at which the distant sphere replaces the sprite for the home body.**
  `num4 > 2.0` flags the sphere and `num4 > 6.0` suppresses the sprite
  (`StaticCelestialDistanceRendering.cs:371-375`); `SpriteSphereBlend` governs the crossfade
  (`:381`, `DistantSphereRenderer.cs:80`), and its body is unread.
- **What `RenderedViewport` is during `UpdateRenderingResources`'s `SunShadowSystem.UpdateUniforms` /
  `_cascadedShadowSystem.UpdateUniforms` calls (`Program.cs:3865-3866`).** By inspection it is the main
  viewport (`RenderGame` restores `_renderedViewportIndex = _mainViewportIndex` at `:4114` and
  `UpdateRenderingResources` runs before `RenderGame`), but `_renderedViewportIndex` is also written by
  `RenderViewport` (`:3969`) and the very first frame is untraced. It does not change any conclusion.
