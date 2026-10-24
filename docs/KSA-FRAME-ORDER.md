# KSA's frame order, and what instant a sample belongs to

What the engine does with time, read out of the decompiled sources rather than inferred from this
mod's behaviour. Every claim carries a `file:line` into `../ksa-game-assemblies/current/src`.
Written against build 2026.8.22.5348; the citations in §1–§4, §6–§9 were re-pointed at
**2026.10.19.5638**, and where that build changed the code the text says so. `PhysicsBubble` was
restructured in between, so its citations name the method as well as the lines. Line numbers move on
every KSA update, so a citation that does not land on what it claims means this file is behind the
corpus, not that the corpus is wrong.

**Read this beside `docs/FRAMES-AND-EPOCHS.md`, not instead of it.** That file has the rules and
the four failure shapes; this one is the evidence under them. Every epoch rule in this mod was
settled by internal consistency plus a flight — a form that agrees with itself and lands rounds on
target — never by knowing what the engine does. This file closes that gap.

---

## The short answer

| What the mod assumes | Verdict | Where |
| --- | --- | --- |
| Samples read from the mod's hook are the **end of the step just applied** | **Confirmed** | §3, §4 |
| `Universe.GetLastSimStep()` is the interval those samples moved across | **Confirmed** | §2 |
| The mod's update is a postfix on `OnDrawUiViewports`, after the pass that built the frame's matrices | **Confirmed** | §1 |

**All three survive.** But confirming them measures one gap in the mod's own arithmetic:

> **`WeaponSystem.UpdateRounds` reads its force samples at the round's pre-step position against a
> body sample one applied step ahead of it** — the same ~0.9 km at 1× and ~3.9 km at 8× that the
> air-density lookup was wrong by before it was corrected. **Moving the sample point to close it was
> flown twice, in two forms, and both lost; what ships moves the body instead. §5 has the
> mechanism, the flights, and which terms are dated now.**

Four further things were not known, and none is urgent:

- **The step a mod is handed was sized from the previous frame's wall clock**, so a
  simulation-speed change cannot show up in it for two frames (§2). That is the mechanism behind
  `WarpPolicy.SettleSteps`, which was derived from a measurement.
- **`GetElapsedTime()` is not monotonic.** Loading a save rewrites the clock outright; the mod
  resets `StepGate` on that path through `Ksa/WorldReloadHook.cs` (§6).
- **The epoch need not be assumed at all** — `Orbit.StateVectors.StateTime` is public, and it is
  the sample's own timestamp (§8).
- **KSA advances its own particles by the step that has *not* run yet** (§7), which is not the one
  everything else moved by.

---

## 1. One frame, in order

`App.Run` is a bare `while` around `OnFrame(totalSeconds, dtPlayer)` with **no try/catch**
(`KSA/KSA/App.cs:39-74`); `dtPlayer` is wall-clock, clamped to
`1/GameSettings.Current.Simulation.MinTargetFrameRate` (`App.cs:52-53`). There is one simulation
step per frame and its length comes from that clamped `dtPlayer` — which is why there is no
interpolation alpha anywhere (§8).

`Program.OnFrame` (`Program.cs:2372-2468`), with the StarMap hook points and this mod's
`OnFrameCelestials` prefix marked:

| # | Phase | Ref |
| --- | --- | --- |
| 1 | **`PrepareFrame`** — expanded below. **Every position in the world is advanced here and nowhere else.** | `Program.cs:2377` |
| 2 | `GaugeCanvas` / `BurnCanvas` on-frame | `Program.cs:2384-2392` |
| 3 | `GizmosRenderer.ResetInstances()` — anything submitted before this is discarded | `Program.cs:2394` |
| 4 | `PrepareImGui`, `OnFrameEditor` | `Program.cs:2395-2396` |
| 5 | **`OnFrameViewports`** — every controller writes its camera, then `Camera.OnFrame` builds this frame's view and view-projection matrices | `Program.cs:2397` |
| 6 | `OnDrawUiFrame` — **`[StarMapBeforeGui]` is a prefix here** | `Program.cs:2401` |
| 7 | `OnDrawUiViewports` — **`[StarMapAfterGui]` is a postfix here; this is where this mod simulates and draws** | `Program.cs:2402` |
| 8 | `OnDrawUiThreadSafe`, `DrawFps`, `OnDrawUiConsole`, `ImGui.Render()` | `Program.cs:2404-2420` |
| 9 | `OnFrameLaunchMenu`, `OnFrameHoveredOrbiters`, gauges | `Program.cs:2426-2444` |
| 10 | `OnFrameController`, `LightSystem.OnFrame` | `Program.cs:2448-2450` |
| 11 | `OnFrameCelestials` — camera-nearby body, altitude, terrain height. **Moves nothing.** `Ksa/PreRenderHook.cs` prefixes it | `Program.cs:2454`, body at `:2833-2864` |
| 12 | `OnPreRender` → `Render` → `PostRender` | `Program.cs:2457-2464` |
| 13 | `FrameNumber++` | `Program.cs:2466` |
| 14 | *(StarMap postfix)* **`[StarMapAfterOnFrame]`** — after the render | `StarMap.Core/StarMap.Core.Patches/ProgramPatcher.cs:40-49` |

Steps 6 and 7 are inside `if (DrawUI)` (`Program.cs:2399-2403`) and step 8's `OnDrawUiThreadSafe`
inside a second one (`:2404-2407`); `DrawFps` and `OnDrawUiConsole` are not guarded. That guard is
the whole of §7.

`GameViewport.OnFrame` is three calls — active controller, then `Camera.OnFrame`, then audio
(`GameViewport.cs:124-132`); `Camera.OnFrame` clamps the camera to the ground, builds `_vp.view` /
`_vp.viewProjection` and extracts the frustum planes (`Camera.cs:479-489`). So by the time the mod's hook runs at step 7, **this frame's
camera matrices are already built** and a pose written now is consumed at step 5 of the *next*
frame. That is the assumption `Ksa/LevelHorizonController.cs` and `IViewPose` exist to work around,
and it is confirmed.

### `PrepareFrame` — where the world moves

`Program.cs:2275-2370`, in order:

| Line | What |
| --- | --- |
| `2283` | `_screenshotCapture.OnPrepareFrame(...)` — **can set `Program.DrawUI = false`** (§7) |
| `2284` | `ExplosionFlashSystem.Update` by the step that has *not* run yet (§7) |
| `2286-2295` | wait for the performance, orbit, vehicle and cloth jobs queued **last** frame |
| `2298` | `Universe.ApplyOrbitSolvers()` — each `Celestial` takes its worker's new state vectors |
| **`2301`** | **`Universe.ApplyVehicleSolvers()`** — physics results applied, `_lastSimStep` advanced, `CurrentSystem.UpdatePerFrameData()` |
| `2304` | `Universe.ApplyClothSolvers()` |
| `2313-2315` | `Glfw.PollEvents()`, then `InputEvents.ApplyInputEvents()` |
| `2323` | `RefreshVehiclesInFrame()` |
| `2349` | `Universe.ProcessAutoWarp(dtPlayer)` |
| **`2350`** | **`SimStep jobSimStep = Universe.GetJobSimStep(dtPlayer)`** — sizes the *next* step |
| `2351-2355` | `ExecuteNextClothSolvers`, `ExecuteNextVehicleSolvers`, `ExecuteNextOrbitSolvers` — queue the workers for it; `_nextSimStep = simStep` |
| `2356` | `Network.Tick()` |
| `2357-2368` | two early returns: window closing, and a font rebuild (§7) |

`Vehicle.PrepareWorker` — which `Ksa/AttitudeHook.cs` patches — is reached from
`ExecuteNextVehicleSolvers` (`Universe.cs:2147`, called at `Program.cs:2354`), i.e. *inside*
`PrepareFrame` and before any mod hook of any kind runs. That is why an attitude command written from a StarMap hook is
overwritten and one written from the prefix is not.

### A mod may destroy a vehicle from its hooks. It may not split one.

The same ordering decides this, and the two look identical until you try the second.

Every mod hook runs **after** `ExecuteNextVehicleSolvers` has queued the next worker run and
**before** the `ApplyVehicleSolvers` that will apply its results — which is the *next* frame's. So
a hook sits between a run being staged and its results being applied:

```
frame N   PrepareFrame:  wait -> ApplyVehicleSolvers -> ... -> ExecuteNextVehicleSolvers
frame N   mod hooks                                              <-- results staged, unapplied
frame N+1 PrepareFrame:  wait -> ApplyVehicleSolvers             <-- they land here
```

`KsaWorld.WaitForVehicleSolvers` joins that run, which is what stops the collection being mutated
underneath it. It does **not** apply the results. Destroying a vehicle from there is safe because a
destroyed vehicle's staged results have nowhere to land. **Splitting one is not**: the fragments
survive, and results computed against the whole craft are applied to them on the next frame.

Measured, from a warhead breaking seven parts off a 21-part drone: the drone fragmented into 11
vehicles and every subsequent frame threw

```
System.ArgumentOutOfRangeException: Index was out of range.
   at KSA.FlightComputer.UpdateTvcParams(FlightComputerOutput&)
   at KSA.PhysicsBubble.FullPhysicsPreStep(Double&)
```

for ever — `VehicleConfig.Gimbals` indexed against a state list belonging to a craft that no longer
exists. Nothing recovers, and the errors are on screen.

Re-running `Universe.ApplyVehicleSolvers()` from the hook is not the fix, though it is public: it
also advances `_lastSimStep` and calls `UpdatePerFrameData`, so calling it out of band moves the
clock every timing rule in this file rests on.

**The fix is to queue rather than apply.** `PartFailure.Detect` does not call
`PartFailureEvent.Apply` either — it writes `VehicleUpdateState.PartFailureEvent`, and
`ApplyRenderEventsToVehicles` applies it *immediately after* the staged results, inside the same
`ApplyResultsToMainThread`. `KsaWorld.TryQueuePartFailure` writes that same field, so the mod's
part failures run on the engine's own path at the engine's own instant. The field is public
because that is the protocol; only the `VehicleUpdateState` holding it is private, and it is read
by a verified reflection with a fallback to destroying whole craft.

Same run, same seven parts, after the change: zero exceptions.

### Nor may it stage one

The worker `ExecuteNextVehicleSolvers` queues is *running* while the hooks run, and nearly everything
it reads is a snapshot. One thing is not. For the controlled craft, while the engine-control gauge or
the staging list is open, `PhysicsBubble.RunVehiclePostWorkInner` calls
`Parts.PerformanceSequences.RecomputeForFlight` (`PhysicsBubble.cs:811-814`), which walks the live
`PartTree.SequenceList` — and `SequenceList.ActivateNextSequence` rebuilds it. A stage fired from a hook
races that walk. Seen once, on shot 4 of `2026-09-17-stepclock`, with the gauge open:

```
Update task failed for vehicle(s): GeoSat FAT
System.NullReferenceException: Object reference not set to an instance of an object.
   at KSA.SequencePerformanceList.Recompute()
   at KSA.SequencePerformanceList.RecomputeForFlight(Single ambientPressure)
   at KSA.PhysicsBubble.RunVehiclePostWorkInner(VehicleUpdateState vehicleState)
```

Only the controlled craft, only with one of those two panels open, and only when the stage lands on
the walk — which is why none of the 35 sessions logged before it showed one. `AttitudeHook.Stage`
queues the stage for `Vehicle.PrepareWorker`, before the worker is queued, which is where the attitude
is written for the other reason above.

---

## 2. `GetLastSimStep()` and `GetElapsedTime()`

```csharp
public readonly struct SimStep
{
    public required UniverseTime PreviousTime { get; init; }
    public required UniverseTime NextTime    { get; init; }
    public required double       DeltaTime   { get; init; }
}
```
`KSA/KSA/SimStep.cs`.

```csharp
public static SimStep     GetLastSimStep() => _lastSimStep;              // Universe.cs:2370-2373
public static SimStep     GetNextSimStep() => _nextSimStep;              // Universe.cs:2376-2379
public static double GetElapsedSeconds() => GetElapsedTime().Seconds();  // Universe.cs:2382-2385
public static UniverseTime GetElapsedTime() => _lastSimStep.NextTime;    // Universe.cs:2388-2391
```

**They are two fields of one struct, so they cannot disagree.** `GetElapsedTime()` is the end of
the interval `GetLastSimStep().DeltaTime` describes. `_lastSimStep` is written in exactly two
places: `ApplyVehicleSolvers` (`Universe.cs:1946`) and `DeserializeSave` (`Universe.cs:2445-2451`,
§6).

**The steps are contiguous — no gaps, no overlaps.** `GetJobSimStep` builds every step from the
end of the last one:

```csharp
public static SimStep GetJobSimStep(double dtPlayer)
{
    double num = _achievedSpeedFraction * GetSimulationSpeed();
    UniverseTime nextTime = _lastSimStep.NextTime;
    double num2 = dtPlayer * num;
    double num3 = double.Max(_maxStepSimSeconds, dtPlayer);    // since 5602: at most twice the last step
    if (num2 > num3) { num2 = num3; _achievedSpeedFraction = num2 / (dtPlayer * GetSimulationSpeed()); }
    return new SimStep { PreviousTime = nextTime, NextTime = nextTime + num2, DeltaTime = num2 };
}
```
`Universe.cs:2612-2630`, condensed; the cap is set in `ApplyVehicleSolvers` (`Universe.cs:1939-1944`)
and `docs/KSA-MODDING-NOTES.md` has what it does to a warp change. So `PreviousTime(k) == NextTime(k−1)` by construction. **But `NextTime` is
`DeltaTime` rounded to the nanosecond** — `UniverseTime + double` goes through `Math.Round` — and every
celestial is placed on `NextTime` (`CelestialUpdateTask.Prepare`), so it is the difference of the
`NextTime`s, not the sum of the `DeltaTime`s, that reproduces the clock the planets are on. The two
differ by up to half a nanosecond a frame, which is 15 µm against 29.8 km/s; taken as the longer of the
two every frame it is 0.125 ns ahead on average and a ~20 mm walk over a warped coast
(`docs/ACCURACY-PLAN.md` 3el). `Universe.GetAchivedSpeedFraction()`
(`Universe.cs:2305-2308`, and yes, that is the spelling) is public if the factor is ever wanted.

**The step is sized from the *previous* frame's wall clock, and that is the one genuinely new
fact here.** The step reported at frame *k* was built at frame *k−1* from *k−1*'s `dtPlayer`,
`_achievedSpeedFraction` and simulation speed, queued as `_nextSimStep` (`Universe.cs:2134`), and
promoted to `_lastSimStep` at frame *k* (`Universe.cs:1946`). Two consequences:

- **A simulation-speed change takes two frames to appear in the step.** Written during the mod's
  hook at frame *k*: read back from `Universe.SimulationSpeed` immediately at *k+1* (its setter
  writes `_simulationSpeed` synchronously, `Universe.cs:128-139`, `:2277-2285`), first used by
  `GetJobSimStep` at *k+1*, first
  *applied* at *k+2*. `WarpPolicy`'s observe-then-settle sequence — clear `_awaitingWrite` when the
  speed reads back, then skip `SettleSteps` further steps — lands on or just after that boundary,
  which is why the constant measured out at 1.
- **`GetLastSimStep().DeltaTime` is not a measurement of anything the mod can time around itself.**
  It is the interval the world was integrated across, decided a frame before the mod sees it. It
  cannot be a phase out from the samples, because the samples *are* its endpoint.

`Universe.IsPaused()` is `_simulationSpeed == 0.0` (`Universe.cs:1791-1794`) — a statement about
the setting, read the instant it is written, while `_lastSimStep` still carries the step queued
before it. That is the one-frame skew `FRAMES-AND-EPOCHS.md` warns about, confirmed: **gate on the
applied step, never on the flag.**

---

## 3. What instant a `Celestial`'s position is

**The end of the step just applied, i.e. `Universe.GetElapsedTime()`.** This is the most
load-bearing assumption in the mod and the source settles it outright.

The orbit worker is handed a `SimStep` at `ExecuteNextOrbitSolvers` (`Universe.cs:2097-2111`, via
`CelestialUpdateTask.Prepare` at `Universe.cs:2105`) and evaluates the orbit at that step's
**`NextTime`**:

```csharp
private void DoWorkAndStageResults()
{
    if (!(_simStep.DeltaTime <= 0.0))
    {
        UniverseTime nextTime = _simStep.NextTime;
        Orbit orbit = _readOnlyCelestial.Orbit;
        NewStateVectors = orbit.GetStateVectorsAt(nextTime);
        ...
    }
}
```
`KSA/KSA/CelestialUpdateTask.cs:52-62`.

Those results are taken at `ApplyOrbitSolvers` (`Universe.cs:1897-1912` →
`Celestial.UpdateFromTaskResults`, `Celestial.cs:1497-1515`), and the cached ecliptic position is
rebuilt from them a few lines later in `Celestial.UpdatePerFrameData`
(`Celestial.cs:592-607`), which is what `GetPositionEcl()` / `GetVelocityEcl()` return
(`Celestial.cs:386-407`). The step whose `NextTime` that was is the one promoted to `_lastSimStep`
at `Universe.cs:1946`. **So `celestial.GetPositionEcl()` read from the mod's hook is the body's
position at `Universe.GetElapsedTime()`, exactly.**

Two details worth having:

- **A zero-length step does not move a celestial and does not need to.** `DeltaTime <= 0` skips the
  evaluation entirely (`CelestialUpdateTask.cs:54`), leaving the old state vectors — and
  `NextTime == PreviousTime` for such a step, so the old vectors are still stamped at the current
  elapsed time. Paused, the sample and the clock still agree.
- **The whole system shares one epoch**, because it is walked in one call — see §4.

---

## 4. Same question for `Vehicle` — and it does not differ

**Identical, and by construction rather than by coincidence: celestials and vehicles are advanced
in the same call.**

`ApplyVehicleSolvers` ends with three lines (`Universe.cs:1946-1948`):

```csharp
_lastSimStep = _nextSimStep;
_lastPlayerDeltaTime = _nextPlayerDeltaTime;
CurrentSystem.UpdatePerFrameData();
```

`CelestialSystem.UpdatePerFrameData` (`CelestialSystem.cs:281-292`) finds each root — an
`IParentBody` that is not an `IOrbiter` — and calls `UpdatePerFrameDataTree()`, which is
parent-first and covers **every celestial and every vehicle in the system**
(`IParentBody.cs:114-128`). Vehicles are the leaf case at `IParentBody.cs:125`.

`Vehicle.UpdatePerFrameData` (`Vehicle.cs:2714-2741`) then does the same composition a celestial
does:

```csharp
doubleQuat cci2Cce = Parent.GetCci2Cce();
_positionCce = Orbit.StateVectors.PositionCci.Transform(cci2Cce);
_velocityCce = Orbit.StateVectors.VelocityCci.Transform(cci2Cce);
_positionEcl = Parent.GetPositionEcl() + _positionCce;
_velocityEcl = Parent.GetVelocityEcl() + _velocityCce;
```

and `GetPositionEcl()` / `GetVelocityEcl()` return those fields (`Vehicle.cs:1031-1040`). Because the
walk is parent-before-child, the parent's `_positionEcl` is already this frame's when a child adds
it. **One epoch for the whole system, and it is `GetElapsedTime()`.**

The state vectors behind it come from the physics worker, which integrates from `SimStep.PreviousTime`
to `SimStep.NextTime` — `PhysicsBubble.RunStep` is `StepFrameTo(SimStep.NextTime)`
(`PhysicsBubble.cs:1158-1163`, `:703-727`), and the full-physics, surface and freefall paths each
step to their window's `NextTime` (`PhysicsBubble.cs:1405-1445`). Results reach the `Vehicle` through
`PhysicsBubble.ApplyResultsToVehicles` (`PhysicsBubble.cs:934-962`) →
`Vehicle.UpdateFromTaskResultsUnsynchronized` (`Vehicle.cs:2510-2613`), which writes both
`_kinematicStates` (`:2516`) and `Orbit.UpdatePosition(...)` (`:2543`).

**CLAUDE.md's line about `PrepareFrame` advancing vehicle positions before the viewport pass is
confirmed** — `ApplyVehicleSolvers` (`Program.cs:2301`, inside `PrepareFrame`) runs ahead of
`OnFrameViewports` (`Program.cs:2397`). The
inference drawn from it in `Ksa/RoundFollowable.cs` — that following the launching craft would add
a frame-newer platform position to an offset built against the older one — follows.

**The analytic and physics positions of a vehicle share an epoch; they differ in *frame*, not in
time.** `_kinematicStates` and `_positionEcl` are both written during the same
`ApplyVehicleSolvers`. So the metres of disagreement the verbose world dump prints per craft are a
bubble-origin difference and never a timing one, and no amount of re-phasing will close them.

---

## 5. The phase the force samples are at, and the two corrections that lost

> **Flown, and it lost.** The correction below was applied to all three force samples — gravity, the
> air's motion and the once-a-frame density — as `GravityIntoFrame`/`GroundVelocityIntoFrame` called
> with `-dt`. Three flights: **2.80, 3.50, 3.74 km** of miss against a six-flight baseline of
> 0.65-2.07 km (mean 1.39). No overlap. Reverted.
>
> The epoch analysis below is confirmed from source and is not what failed. What failed is the
> assumption that closing the gap by moving the *sample point* is therefore right. For **density**
> it is — the same correction is worth 4.4 km there, because altitude is a scalar and the error is
> genuinely in it. For **gravity** it is not: shifting where the field is read translates the whole
> field, so the round is pulled toward a centre displaced from the one the ground test uses, and
> over a 400-second arc that integrates into more error than it removes.
>
> So the rule is narrower than it looks. **A phase correction is right for a term read *at* a
> position and wrong for a term that *is* a field about a body.** The one remaining honest form —
> re-reading gravity at the round's own sub-step position against an unshifted centre — was flown
> as its own change and also lost: `docs/MIRV-NEXT.md` item 2d, priced headlessly at 284 m and
> flown at a 2.03 km mean against 1.04 without it. **Do not retry either without a mechanism.**
>
> **What ships today is neither of them, and it shares a name with the first.** `GravityIntoFrame`
> now passes `_bodyVelocityEcl * secondsIntoFrame` as a **body offset** — it moves the centre the
> field is about to the round's own instant and leaves the sample point alone. Expanded, the round's
> pull is exactly `−μ(ρ + v_cci·h/2)/|·|³`: the ecliptic carrier that the mid-step read adds is
> cancelled term for term by the body's back-date, so it **equals** the Cci mid-point gravity
> `ImpactPredictor` evaluates. Derived and checked against KSA's rail code in
> `docs/ACCURACY-PLAN.md` 3ec. A reader arriving at `WeaponSystem.cs` and finding the name above
> should not read this warning as covering it.

**What `UpdateRounds` reads now.** The per-sub-step callbacks are all dated by moving the body:
`GravityIntoFrame`, `AirDensityIntoFrame`, `AirVelocityIntoFrame` (through `GroundVelocityIntoFrame`,
`docs/ACCURACY-PLAN.md` 3el) and the ground queries. Of the once-a-frame reads, gravity aims at the
body half a step back (`GravityAtRound`, `PullOnRound` with `-0.5 * dt`), the air's velocity is read
with the body put back a whole step, and only the density `MediumAtRound(round.PositionEcl)` is
undated. The rest of this section is the analysis as it stood before those changes; its code
snippets show the old reads.

**`WeaponSystem.UpdateRounds` differences the round's pre-step position against a celestial sample
one applied step ahead of it, exactly as the air-density lookup did until it was corrected.** The
comment there records that and points here; nothing should be changed on the strength of this
section, because both corrections it suggests have now been flown and both lost. What follows is
the mechanism, the form that closes the gap, and why the experiment recorded in that comment tested
something else again.

### Which instant each side is at

At frame *k*, the mod consumes `DeltaTime(k)` — the step **just applied** — and integrates its
rounds across it (`KsaWorld.ConsumeSimStep`). So, from §2's contiguity:

| | epoch |
| --- | --- |
| the round **before** its step | `NextTime(k−1)` |
| the round **after** its step | `NextTime(k)` |
| every celestial and vehicle sample | `NextTime(k)` |

The world sample matches the round's **post**-step instant, not its pre-step one. The rest of the
mod already behaves as if it knows this and is right to: `Interceptor` and `Slug` back-date target
samples by `elapsedInFrame - frameSeconds`, which is `−frameSeconds` at the first sub-step;
`KSArmoryMod.AddAirborne` carries a round forward by one whole step before publishing it as a
contact. Both put a pair at one instant, and both agree the sample is one step ahead of the
pre-step round.

### The three calls that do not

`WeaponSystem.UpdateRounds` samples its force terms at `round.PositionEcl` — the pre-step
position — and each of the three lookups differences it against `body.GetPositionEcl()`:

```csharp
double3 toBody   = body.GetPositionEcl() - positionEcl;              // KsaWorld.GravityAt
double3 fromCentre = positionEcl - body.GetPositionEcl();            // KsaWorld.GroundVelocityAt
double  altitude = Vec.Len(positionEcl - body.GetPositionEcl()) - …; // KsaWorld.MediumDensityRatioAt
```

The round side is at `NextTime(k−1)`, the body side at
`NextTime(k)`, so each separation is short by `bodyVelocityEcl * dt` — **the same
~0.9 km at 1× and ~3.9 km at 8× that the air lookup was wrong by**, in the same direction, for the
same reason.

The per-sub-step density callback is the exception. `WeaponSystem.AirDensityIntoFrame` is

```csharp
MediumAtRound(positionEcl - (_bodyVelocityEcl * secondsIntoFrame))
```

and `Slug` and `Interceptor` both pass `elapsed - dt`, so at the first sub-step the round is moved
forward by a full `bodyVelocityEcl * dt` and the pair land on one instant. **That correction is
right, and it is the one the other three are missing.** It reaches a round only through the
callback — one with `AirDensityAt` unset uses the uncorrected once-a-frame `mediumDensity`, which
reads a missile low over a site near sea level as under the water.

### Why the experiment in the comment does not settle it

The comment records a real flight: adding a carry made things much worse — the rounds diverged from
their own prediction by 2 km and the salvo's common miss went 782 m → 2,667 m. But what was carried
was `round.VelocityEcl * step`, `AddAirborne`'s carry. **That is a different correction.** It moves
the round by its *own* ecliptic velocity, which is the planet's ~29.8 km/s **plus** the round's own
several km/s, and it evaluates the force at the **end** of the step rather than the start — a change
of integration scheme on top of a change of phase.

The phase-only correction moves the **body** back instead, by `_bodyVelocityEcl * dt` and nothing
else, leaving the force where an explicit integrator wants it — at the start of the step. In
`UpdateRounds` that is one term added to three call sites:

```csharp
double3 atRoundEpoch = round.PositionEcl + _bodyVelocityEcl * dt;
double3 gravity      = GravityAtRound(atRoundEpoch);
double  mediumDensity = MediumAtRound(atRoundEpoch);
double3 airVelocity  = GroundVelocityAtRound(atRoundEpoch);
```

### What it is worth, and what it is not

**Measured, and on the two terms still uncorrected it is worth less than nothing** — the blockquote
at the head of this section is that flight. The term-by-term argument is kept because it is the one
a retry has to beat:

- **Ground velocity: negligible.** The offset enters only through `spin × fromCentre`, and
  `7.3e-5 rad/s × 4 km` is 0.3 m/s against airspeeds of kilometres per second.
- **Density: already fixed** where it mattered, and worth 0.9–3.9 km when it was not — because
  altitude is a *radial* reading against an 8 km scale height, where the offset does not cancel.
- **Gravity: the argument cuts both ways, and the flight cut against it.** A constant offset of the
  field's centre is a *translation*, and the impact test differences against the same body sample,
  so much of it may be common-mode and cancel. What does not obviously cancel is the radial part,
  which changes `mu/dist²` by about 0.02% at low altitude — and displacing the centre away from the
  one the ground test uses costs more over a 400-second arc than that is worth.

That mixture is exactly why this was a measurement rather than an argument. Score any retry against
the target rather than against the prediction — a correction loop can only remove what its observer
can see, which is how the density bug survived a converged aim correction for as long as it did.

---

## 6. The one place the clock is not continuous

`Universe.DeserializeSave` — loading a save — rewrites the step outright
(`Universe.cs:2444-2451`):

```csharp
UniverseTime universeTime = universeData.GameTime;
_lastSimStep = new SimStep { PreviousTime = universeTime, NextTime = universeTime, DeltaTime = 0.0 };
_nextSimStep = _lastSimStep;
```

then re-evaluates every celestial at the new elapsed time. So:

- **`GetElapsedTime()` is not monotonic.** It can jump forward by years or backward by any amount.
- **The jump announces itself**: `DeltaTime == 0` while `NextTime` differs from the previous
  frame's. No step `GetJobSimStep` produces can look like that, because it always starts at
  `_lastSimStep.NextTime`.
- **This mod resets on it.** `Ksa/WorldReloadHook.cs` postfixes `Program.OnGameLoaded`, called
  right after `DeserializeSave`, and `KSArmoryMod.ForgetTheWorld` calls
  `KsaWorld.ResetSimStepTracking()`, so `StepGate` does not carry the old save's clock across. Without
  it a backward jump is harmless — `StepGate.Consume` takes the span only within a nanosecond of the
  reported step or longer, so a negative span is ignored — and a forward jump hands the mod an
  enormous span for `SimClock.Classify` to reject as `Skipped`.

---

## 7. When the mod's hook does not run at all

`OnDrawUiFrame` and `OnDrawUiViewports` are both inside `if (DrawUI)` (`Program.cs:2399-2403`), so
**a `[StarMapBeforeGui]` or `[StarMapAfterGui]` method is not called at all when `Program.DrawUI`
is false** — a postfix on an uncalled method never runs. `ScreenshotCapture.OnPrepareFrame` sets it
false for a hi-res capture without the HUD (`ScreenshotCapture.cs:228-232`, restored at `:425` and
`:450`), and the player's `ToggleUi` key (F2) toggles it (`Program.cs:1930-1934`). `PrepareFrame` runs
regardless (`Program.cs:2377`, and it is where the capture arms itself), so **the world still advances across
the skipped frame**.

There is a second route: `PrepareFrame` returns `Exit` on a window close (`Program.cs:2357-2361`)
or a font rebuild (`:2362-2368`), and `OnFrame` returns immediately (`Program.cs:2377-2382`). Both
of those returns are **after** `ApplyVehicleSolvers` and after `ExecuteNext*Solvers`, so again: the
step was applied and the mod never saw it. The font-rebuild case is recoverable and the game
carries on.

That is what `KsaWorld.ConsumeSimStep` handing `StepGate` the *span* between step boundaries — not
the last `DeltaTime` alone — is for, and it is the right shape: the step boundaries are contiguous
(§2), so the span across any number of missed frames is exact.

**What this mod does about it.** `Sim/FrameLatch.cs` hands the frame's work to whichever hook
reaches it first, so the simulation never stops. That alone leaves the step landing at #14, after
the render, so everything written into the world — subpart transforms, effects — is drawn on the
following frame; at 250 m/s that is 4 m of a round's own travel, and it alternates with the
display's frame pacing rather than sitting still. `Ksa/PreRenderHook.cs` closes it with a **prefix**
on **`OnFrameCelestials`** (#11), the last phase before `OnPreRender` that is not behind the
`DrawUI` guard. Every candidate is private, so the patch resolves by name and **degrades to #14 if
it ever fails**, which is the behaviour without it.

**A prefix and not a postfix, and that is not a detail.** #11 resolves the camera-nearby body and
updates the planet's shader data for the frame, so a step taken after it prepares the planet
against a camera the mod is about to move — reported from play as the ground rendering as open
water on pressing F2 during a chase. With the UI drawn the mod steps at #7, which is ahead of #11;
a prefix is what puts a hidden-UI frame on the same side of it. **The hook exists to make the two
paths identical, and a postfix quietly did not.**

Two gaps it does not close, both covered by the #14 fallback: the early return at `:2421-2425` is ahead
of #9, so a frame taking it reaches none of #9–#12; and `PrepareFrame` returning `Exit` skips
everything from #2 on.

**`Program.FrameNumber` (`Program.cs:302`) is not a reliable skipped-frame detector.** It is
incremented at `Program.cs:2466`, past three early returns (`:2381`, `:2425`, `:2462`), so it counts
frames that reached the end of the render rather than frames that were begun. It does increment on
a `DrawUI == false` frame, so it catches the screenshot case and not the `PrepareFrame`-exit one.
The step boundary is the better question in both.

**One asymmetry worth knowing if a mod places particles:** KSA advances its own particle system by
`Universe.GetNextSimStep().DeltaTime` (`Program.cs:2540`, `:5041`) — the step that has *not* run yet —
while every position in the frame moved by the step that has. Those differ whenever the frame time
or the simulation speed changes. This mod's emitters are placed at positions it computes itself, so
nothing depends on it today.

---

## 8. What a mod can read instead of guessing

**Yes — the engine stamps the sample.** `StateVectors` carries its own time:

```csharp
public struct StateVectors
{
    public readonly UniverseTime StateTime;
    public readonly double3 PositionCci;
    public readonly double3 VelocityCci;
    ...
}
```
`KSA/KSA/StateVectors.cs:6-17`.

The chain to it is public the whole way: `Vehicle.Orbit` (`Vehicle.cs:375`, its first patched conic's orbit),
`Celestial.Orbit` (`Celestial.cs:64`), `IOrbiter.Orbit` (`IOrbiter.cs:16`), and
`Orbit.StateVectors` as `public ref readonly` (`Orbit.cs:1158`). So

```csharp
vehicle.Orbit.StateVectors.StateTime == Universe.GetElapsedTime()
```

is a **checkable** statement rather than an assumption, per craft, per frame — and it is the
assertion a diagnostic should make rather than a comment restating §3 and §4. Under full physics
the orbit is rebuilt from the physics state at that state's own time
(`Orbit.CreateFromStateCci` at the state's time, `PhysicsBubble.cs:2586-2602`), so the stamp stays honest on the path where the analytic orbit is
being regenerated every step.

The rest of what is reachable:

| Want | Read | Ref |
| --- | --- | --- |
| the epoch of the world's samples | `Universe.GetElapsedTime()` | `Universe.cs:2388-2391` |
| the interval they moved across | `Universe.GetLastSimStep()` | `Universe.cs:2370-2373` |
| the step now in the workers, applied next frame | `Universe.GetNextSimStep()` | `Universe.cs:2376-2379` |
| the epoch of one specific body | `x.Orbit.StateVectors.StateTime` | `Orbit.cs:1158`, `StateVectors.cs:8` |
| how much of the requested speed the solver is keeping up with | `Universe.GetAchivedSpeedFraction()` | `Universe.cs:2305-2308` |
| a frame counter | `Program.FrameNumber` — with §7's caveat | `Program.cs:302`, `:2466` |

**There is no interpolation alpha, and there is nothing to interpolate.** The simulation step is
derived from the frame's own `dtPlayer` (`Universe.cs:2614-2616`), so there is exactly one step per
frame and the rendered state *is* the integrated state — `Camera.GetPositionEgo`
(`Camera.cs:236-249`) differences stored positions and blends nothing. A fixed-timestep engine
would need an alpha; this one does not have the problem.

**`Vehicle.KinematicStates` carries no timestamp** (`KinematicStates.cs:8-20`, public accessor at
`Vehicle.cs:567`). Its epoch has to come from §4 — which is fine, because it is the same one.

---

## 9. The three assumptions, one by one

**1. Samples read during the mod's hook are the end of the step just applied.** **Confirmed.**
Celestials are evaluated at `_simStep.NextTime` (`CelestialUpdateTask.cs:54-58`); vehicles are
integrated to `SimStep.NextTime` (`PhysicsBubble.RunStep`, `PhysicsBubble.cs:1158-1163`); both are composed into `_positionEcl`
by one tree walk at `Universe.cs:1948`, immediately after `_lastSimStep` is advanced at `:1946`; the
mod's hook is several phases later at `Program.cs:2402`; and nothing between them moves anything
(`Program.cs:2302-2401`, and `OnFrameCelestials` at `:2833-2864` only reads) except
`InputEvents.ApplyInputEvents` (`:2315`), which applies queued teleports, docking and new vehicles
(`InputEvents.cs:1107-1133`) — so a craft placed or created that frame is the exception.

So the world sample is exactly one applied step ahead of a round's pre-step position, and
`elapsedInFrame - frameSeconds` is the correct back-date — for targets, for platforms, and for the
air-density lookup — provided `frameSeconds` is the step the mod actually consumed rather than the
last `DeltaTime`. `KsaWorld.ConsumeSimStep` returns the former (§7).

**It is also what §5 turns on**: gravity, air velocity and the once-a-frame density are sampled
with no back-date at all, so they are the three places in the mod where this assumption is recorded
in a comment and deliberately not applied in the arithmetic.

**2. `GetLastSimStep()` reports the step just finished applying.** **Confirmed**, and it is
stronger than that: `GetElapsedTime()` *is* that step's `NextTime` (`Universe.cs:2388-2391`), so the
interval and the epoch are the same object and cannot drift apart. Steps are contiguous
(`Universe.cs:2615`), so the mod's `StepGate` deduplicating on `NextTime` is deduplicating on a key
the engine guarantees is unique per step and ordered.

**3. The mod's update is a postfix on `OnDrawUiViewports`, after the viewport pass.**
**Confirmed.** `ProgramPatcher.AfterOnDrawUi` is `[HarmonyPatch("OnDrawUiViewports")]
[HarmonyPostfix]` (`ProgramPatcher.cs:29-38`); `OnFrameViewports` — which runs every controller and
then `Camera.OnFrame` to build the matrices — is at `Program.cs:2397`, five lines earlier
(`GameViewport.cs:124-132`, `Camera.cs:479-489`). The gizmo reset at `Program.cs:2394` is before it and
the render at `:2457-2463` is after it, which is why the mod draws from this hook and not from
`[StarMapAfterOnFrame]`.

---

## 10. What this does not settle

- **§5 is settled negative, not resolved.** The source says the phase is one applied step wide;
  two flights say that closing it costs more than it removes, and neither says what the phase is
  worth on its own. Nothing in this repository can answer that — the suite would agree with
  whichever form it was written against, because a constant step cannot see a phase error at all.
- **The save-load reset is keyed on the load, not on the clock.** `WorldReloadHook` resets
  `StepGate` on every `Program.OnGameLoaded`; what `StepGate` itself does across the rewrite, were
  the hook not to apply, is §6's reasoning and has not been flown.
- **`_achievedSpeedFraction` is a feedback loop on solver load** (`Universe.cs:1914-1938`), smoothed
  with a 0.9/0.1 filter and ratcheting downward instantly. It means the step can shrink without
  either the frame time or the simulation speed moving. Nothing in this mod reads it; `WarpPolicy`
  calibrating off the step it was handed rather than off a frame rate is the right shape regardless.
- **Sub-frame ordering inside the physics worker is out of scope here.** What matters to a mod is
  that the worker's output is stamped at `SimStep.NextTime`, and that is §4.
- **This is one build.** `tools/ksa-api-diff.sh` cannot settle any of it: what moves here is
  meaning rather than signature, so it compiles clean and is wrong in flight. `Program`,
  `Universe`, `Vehicle` and `Celestial` are all types the mod binds to, so their files do land in
  its changed-file list — reading them is the whole of the check. Re-read §2 and §4 after a KSA
  update the way `docs/BLOCKED-ON-KSA.md` is re-read.
