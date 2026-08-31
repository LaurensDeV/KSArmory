# The accuracy plan, 2026-08-30

**What this is.** One ranked plan, replacing the ranked lists at the end of `EIGHT-ROCKETS.md` and
`METRE-LEVEL.md`, which were written against a state that has since moved and against each other.
Read this first; those two keep their reasoning and their measurements.

**It exists because four investigations landed at once** — the long-range logs, the guidance chain,
the KSA corpus and the backlog itself — and between them they moved the top of the list from "tune a
constant" to "there is a bug, and the engine has a lever nobody used".

## Where it stands, measured

| | 2,000 km | 12,902 km, before | 12,902 km, after |
| --- | --- | --- | --- |
| flights | 96 of 96 scoring | 324 across three nights | 8 per shot, all scoring |
| median miss | **99 m** | **6,664 m** | **150-650 m**, by session |
| best shot | 52 m | — | **9 m**, group of 0.009-0.479 km |
| p90 | 198 m | 28,652 m | — |
| within a group of six | **5 m** | 6 m | 6 m |
| shape | unimodal, CV 0.58 | **bimodal**, CV 1.08 | corrections mostly finish |

Three fixes on 2026-08-30, each verified in flight and each working the same way — by letting more
corrections **finish**. `payback` lands at 88-131 m and every other ending at 3.7 to 10.7 km over 96
flights, so nothing yet has made a *finished* correction more accurate; long range improved because
the share reaching one went from 13 of 48 to 27 of 48 and then higher.

**Two candidate levers were flown and refuted after that.** `MinResponse` costs 9%, not the sevenfold
it looked like from one shot's first reading — with the trim converged the plant's median is **0.91**,
range 0.53-3.17. And the trim is not the constraint: it delivers **99.7%** of what it is asked, 26 of
29 readings converged. The remaining question is why the miss sometimes grows between passes when
each pass is working, and that is not yet measured.

**The burn is equally good at both ranges.** What differs is what the geometry does with it and, at
long range, a bug.

## 1. The long-range bimodality is one rocket's timewarp thrown over the others

**Not a heavy tail — two populations, and the separation is exogenous.** Pooled over 324 flights,
log-miss is 25% at a **60 m** median and 75% at **8.81 km**, with a trough holding 7.4% of the mass
between 0.25 and 2.5 km. A two-component mixture beats one at LRT 245.8 against a bootstrap maximum
of 11.6 under the unimodal null.

**The good long-range mode sits on top of the entire 2,000 km control.** 0.02-0.15 km against
0.04-0.25 km. So there is no long-range accuracy problem: a long shot that avoids one event is as
accurate as a short one.

The event, from `~/shots/interlock-more/006`, 130 milliseconds wide:

```
04:39:19.908  GeoSat FAT    ... longest step of the burn  33 ms
04:39:19.909  warping to within 4:44 of the release point on GeoSat FAT
04:39:20.039  GeoSat FAT 2  ... longest step of the burn 205 ms
04:39:20.039  GeoSat FAT 3  ... 205 ms          (and 4, 5, 6, 7, 8)
```

One rocket finishes its burn, fires KSA's auto-warp, and the seven **still burning** get 205 ms
steps. Their one-frame velocity quantum goes from 0.081 m/s to 1.675 m/s. Misses: 0.68 km for the
one that triggered it, 14-37 km for the rest.

`Ksa/IcbmComputer.cs`'s `CanWarpAhead` asks `!NeedsShortSteps` of **this computer only**, and its own
comment names the consequence: *"WarpPolicy cannot slow the world at all while an auto-warp is
running, so a warp started over the top of one is a warp nothing can rein in."* It is the identical
one-world/several-flights mistake `Sim/WorldSpeed.cs` was written to fix for the speed path, left
unfixed on the auto-warp path.

**The evidence it is causal rather than correlated**, all from the logs:

* Dose-response with no exceptions: 33 ms -> 2.02 km (56% bad); 34-60 -> 9.97 (96%); 61-100 -> 11.45
  (100%); 101-160 -> 20.89 (100%); >160 -> 34.85 (100%). Every one of the 96 control flights is 33 ms.
* In **28 of 28** contaminated shots the flights sort perfectly by cutoff time into a run of 33 ms
  then a run of >33 ms, never interleaved — a world-level switch at one instant, not a property of a
  rocket.
* Which rockets are hit is decided by where the *controlled* craft sits in the cutoff order, which
  is an accident: rho(controlled craft's cutoff rank, flights contaminated) = **-0.89**, and
  rho(flights contaminated, shot median) = **+0.93**.
* Within-shot matched pairs: the >33 ms group is worse in **17 of 17**, median 5.5x, sign p=1.5e-5.

**And the documented 175x seat gradient was this.** Stratified by cutoff rank the effect is flat and
large (7.1x, 5.5x, 7.9x); pooled rank-sum z = -13.6. Seniority was never the variable.

**Flown 2026-08-30: confirmed, and fixed.** The diagnostic went in first and read what the
hypothesis required — `OVER THE TOP OF 6 still needing short steps: GeoSat FAT 3, 4, 5, 6, 7, 8` on
the release-point warp, and `nothing else needs short steps` on the coast warp three seconds later.
The count is not always non-zero; it tracks the mechanism.

With `!NeedsShortSteps` folded over every computer, the same shot on the same save:

| | before | after |
| --- | --- | --- |
| longest burn step | 3 x 33 ms, 4 x 84, 1 x 198 | **8 x 33 ms** |
| misses, km | 4.70, 13.26, 13.67, 32.08, 32.61, 33.66, 34.12, 54.23 | 0.50, 3.24, 3.89, 7.24, 8.54, 9.05, 11.04, 13.46 |
| median | 32.34 km | **8.80 km** |

**What is proven and what is not.** The step distribution is deterministic and conclusive: the
contamination is gone. The *miss* is one shot each way against a session variance of 2.7x, so its
size is not resolved — and it cannot be, by `--paired`: the warp is world-wide, so both arms in one
world would share it. This is the case `SHOT-PROTOCOL.md` says the within-run instrument cannot
reach. What carries the miss claim is the 324-flight forensics that established the step-to-miss
relation in the first place, not this pair.

**The remaining 8.80 km is the second branch**, below, and it is now the largest thing at long
range.

**The second branch needs no warp and stays open.** Even on a clean burn, `owed at the split` is
1.56 m/s at 12,902 km against 0.49 at 2,000, and about half the time that trips the same 20 s
clearance abandonment (82 of 183, median 4.65 km). At 2,000 km the correction finishes 19.5 s after
the split and the bus's closest approach is at 21.6 s — it re-enters *after* release, so the same
physics costs nothing. That is a knife-edge on a 20 s constant, not a margin.

## 2. The engine can cut an engine off between frames, and the mod drives the branch that cannot

`ActiveNozzle.ComputeThrustMod` returns `clamp((ThrustTime - intraStepTime) / dt, 0, 1)` and it is
applied to force, torque, mass rate **and** propellant draw. An engine commanded to burn for less
than a step delivers exactly that fraction of the step's impulse. It is the only sub-substep event
resolution in the vehicle sim and it is the one this mod needs.

The mod never reaches it because `VehicleCommand` drives the manual branch, where
`FlightComputer.ComputeControl` sets `EngineBurnDuration = EngineOn ? PositiveInfinity : 0.0`.
Infinity or zero — hence a whole-frame quantum, which is the term the entire throttle ramp exists to
divide down.

The route in is public and lands inside the window `AttitudeHook` already owns: `FlightComputer.Burn`
(a `BurnTarget?` of public mutable fields) and `FlightComputer.BurnMode`, both copied by
`FlightComputer.CopyFrom` so they survive the double buffer exactly as `AttitudeMode` does. The
engine's own `UpdateBurnTarget` recomputes the duration from the rocket equation every evaluation and
closes the loop on **measured** delta-v — `ReadMeasurements` accumulates `DeltaVelocityCci` per
sub-step, so it is an accelerometer loop rather than a prediction.

**Hand over late and with a small target.** At a 20 m/s remaining target the float ULP in
`BurnTarget`'s `float3` fields is ~2e-6 m/s; handing over 7 km/s puts the cancellation floor at
~8e-4. Either is inside the 0.005 m/s column that gates rungs C and D of `METRE-LEVEL.md`.

Three costs, all verified and none fatal:

* The flight computer takes attitude past ignition (`AttitudeTrackTarget = PositiveDv`), which points
  along remaining delta-v — what `HoldDirectionFrames` approximates — but overrides the frozen command.
* `Vehicle.PrepareWorker` forces `EngineOn = false` while `BurnMode == Auto`, so returning to Manual
  needs a re-ignition.
* `SolveBurnThrottle` throttles down for a short burn on its own, which is the lever the mod ramps by
  hand.

**No second Harmony patch.** Public fields written from the existing prefix.

## 3. The correction loop stops itself, on constants measured at another range

At 2,000 km **93 of 96** corrections ended on `payback`:

```csharp
double nextCycleCosts = _lastCycleSeconds * HoldingCostsMetresPerSecond;   // 6 s x 26 = 156 m
if (now.PredictedMissMetres <= nextCycleCosts) return Finish(...);
```

Median 99 m, p90 198 m — the distribution sits on the threshold. **The shot is not
residual-limited; it is stopping-rule-limited.** The release probe predicts 0.1 km and the warheads
land at 0.099, so the predictor knows the miss and the loop stops anyway.

`HoldingCostsMetresPerSecond = 26` comes from "8.421 km applied at cutoff and 5.672 km at +106 s" —
`(8421-5672)/106 = 25.9` — measured on a **3,459 km** shot. `SteadyWithinDegrees = 2.0` was
calibrated on that same shot. Both scale with the ejection kick's leverage, which is a property of
the trajectory. Applied at 2,000 km the payback rule stops the loop with the whole miss still on the
table.

**The fix is to derive it, not to lower it.** The remaining flight time and the arrival angle are
both known at runtime, and the leverage is what `AimAuthority` already prices.

Two further defects in the same file, both verified:

**`_worseFor` counts cumulatively where its calibration assumes a run.** It resets only on a new
best, so a cycle inside the +/-250 m dead band neither resets nor increments and twelve accumulates
across the whole flight. The doc's justification — *"the worsening patch is five cycles long...
Twelve is twice that patch"* — is a run-length argument, and the measured cliff is right beside it:
3/4/5 -> 15.74/18.99/22.36 km, 6 and above -> 1.15 km. A cumulative counter behaves like a smaller
run threshold, which is the wrong side of that cliff.

**`Resume()` seeds `_response = 1.0`, the floor of the clamp and so the largest step the loop can
take** — on the one cycle carrying the entire cutoff error, before anything has been measured.
Everywhere else seeds `1.0 / Gain` = 4. And the file argues against itself: `Resume` says the
fixed-arrival coast plant moves the impact "about what the aim did", while `Observe` twenty lines
above says that on a latched arrival "the impact moves several times further — at which point a half
is above the stability limit". The arithmetic favours `Observe`: 36 km of miss times the 0.53 m/s
per km aim cost is 19.1 m/s of pass-one demand, against a recorded 19.26.

## 3b. The aim correction never runs, and that is load-bearing — flown 2026-08-30

Instrumenting the loop's own state settled what the terminator table could not.

```
#0   miss 10153.72 km   best 9112.73   resp 4.00   bias 0 -> 300.00 km   worse 0
#1   miss 12281.31 km   best 9112.73   resp 4.00   bias 300 -> 300       worse 1
      ... eleven more, all before launch ...
#12  miss 12235.79 km   best 9112.73   resp 1.00   bias 300 -> 0.00      worse 12   <- Settled
#470 miss     4.57 km   best 9112.73   resp 1.00   bias   0 -> 0         worse 12
```

The first observation lands **33 ms after arming, at `Rising at 0 km`**. A stationary rocket's
ballistic impact is where it stands, so the miss reads as the whole distance to the target.
`AimCorrection` clamps its step to the 300 km reach, twelve such readings trip
`WorseBeforeStopping`, the bias reverts to zero and `Settled` holds for the remaining 460
observations. All eight flights, every long-range shot: `worse for 12`, final bias 0. `Settled` is
also what commits the arrival.

**And the shot landed 8 of 8 at 70-397 m with the loop dead** — the best long-range result recorded
here. So the correction contributes nothing, and every long-range gain today came from elsewhere.

### The obvious fix loses by three orders of magnitude

Gating the loop on the flight phase — no observation before the pitch programme — was flown and put
**every flight at 303-309 km**. The bias ends pinned at its 300 km clamp instead of reverting to
zero, and the shots land 300 km plus their usual miss away. Two things were wrong: the gate does not
even fire, because the first reading is still 12,096 km once `PitchProgram` starts; and **the zero
bias the dead loop reverts to is what was saving the shot**.

Same shape as **7g**, where never freezing the aim was ranked an obvious fix and lost 5.7x. Reverted.

### What the question actually is

Not *when should the loop start observing* but **why is the predicted impact wrong for the whole
ascent, and still 4.57 km out at the last observation of a shot that lands at 198 m**. The loop is
faithful to an observer that is lying to it, and `AimCorrection` cannot be tuned out of that.

`AimCorrection.Response` never leaves {1.00, 4.00} — the two seed values — across 3,837 observations.
Either the plant estimate at `AimCorrection.cs:227` never fires, or it pegs at `MinResponse`. The
instrument does not yet separate those, and it is the next thing to ask.

## 3c. Warping the coast costs 13x, and the trim was not the part that needed protecting — flown 2026-08-30

Twelve shots at 12,902 km alternating `IcbmConfig.WarpTheCoast`, `~/shots/warpcoast`. Alternating
rather than paired: an auto-warp is world-wide, so an arm with it off is still warped by an arm with
it on.

| | warped | not warped |
| --- | --- | --- |
| pooled shot median | **8.25 km** | **0.62 km** |
| flights inside 1 km | 16 of 48 (33%) | **30 of 48 (62%)** |
| adjacent pairs won | 0 of 6 | **6 of 6** |
| geometric mean | | **0.16x** |
| sign test | | **p = 0.031** |

Per pair: 0.43, 0.86, 0.03, 0.11, 0.29, 0.04. Resolved, but p=0.031 is the floor at six pairs — six
of six is the only way to reach it. Two of the six unwarped shots were still bad, so this moves mass
between the two modes rather than abolishing the bad one.

**The manipulation is verified clean**: every warped shot fired exactly two warps and every unwarped
shot none, and all 96 flights burned at **33 ms** either way. So this is not the step contamination
of item 1 — the burn was already protected. What the warp starves is the *aim measurement between
trims*: fewer frames in the seconds the correction has, so fewer passes.

### The fix is not to turn the coast warp off

`IcbmComputer.NeedsShortSteps` covered the burn and the trim. `PostBoostAim.Correcting` — settling
or measuring — is the window that was missing, and adding it is the whole change.

Flown: **0.050 to 1.909 km, median 0.63**, every flight ending on `payback` at 37-150 m predicted.
That is the unwarped arm's accuracy. The window is **17 seconds**, and the shot took **10.7 minutes**
against 10.9-11.4 for the runs either side of it, so nothing was paid for it.

Turning `WarpTheCoast` off outright buys the same accuracy and costs a player the whole
twenty-five-minute fall in real time. This protects seventeen seconds of it.

**The single-rocket claim was wrong, and the flight that was meant to confirm it cannot.** Flown on
`SOLVER SCALE 1`: **0.503 km, PASS, and nought warps** — so one rocket does not keep the fast-forward
either, on this harness.

But the harness is not the player's situation and this shot cannot separate them. Nothing needed
short steps between entering the coast at 10:37:15 and the trim at 10:41:35 — four and a third
minutes with the gate open and no warp offered — so **the fix is not what suppressed it here**. The
scenario asks the world for 8x on its own, which is why the shot still took 10.0 minutes and why
KSA's warp-to-a-time had nothing left to offer.

So what a player at 1x sees is **still unverified**, and the harness cannot answer it: every scripted
shot drives its own world speed. Answering it wants either a scenario that leaves the speed alone or
a hand-flown shot. Until then the honest statement is that the accuracy is measured and the cost to
a player is not.

## 3d. The aim loop's observer moves 45x faster than its own authority — measured 2026-08-31

Recorded every cycle rather than only when the aim has moved 500 m, over 3,788 observations of one
shot at 12,902 km:

| per cycle, median | |
| --- | --- |
| the aim moved | **78 m** |
| the predicted impact moved | **3,520 m** |
| ...of that, along the aim move | 2,803 m |
| ...not along it | 717 m |

**The impact moves forty-five times further than the aim commands it to.** So what the loop observes
between one cycle and the next is overwhelmingly not its own doing, and the secant it computes from
that — `impactAlong / movedBy`, median **-35.7** — is measuring the wander rather than the plant.

### Which invalidates the plant readings, including the ones from an hour earlier

`ResponseFromMetres` requires a 500 m aim move before a reading counts, and **3 of 3,788 cycles
qualify**. The 0.91 median measured over 26 readings is therefore drawn from the 0.08% of cycles that
are atypical — and `Observe` additionally rejects a negative secant, which the typical cycle now
turns out to have. So the loop keeps only the rare positive readings and discards the common
negative ones, which is a selection, not a filter.

### And it explains the three symptoms without any of the mechanisms proposed for them

* The miss "growing" across passes — 4.8 km, 7.7, 11.5 — is wander, not divergence.
* `WorseBeforeStopping` trips because readings bounce by kilometres, not because the loop is walking
  outward.
* The plant estimate is unusable, so `MinResponse` is doing nothing either way — consistent with the
  9% it was measured to cost.

**The fix is not obvious and nothing should be tuned on this yet.** The question it poses is why the
predicted impact is unsettled by kilometres between cycles, and that is a property of
`ImpactPredictor` and the state it is flown from rather than of the correction loop. A loop cannot be
tuned against an observer this noisy; it can only be given a quieter one.

### It is not the vehicle's own motion — flown and refuted

3,520 m over the nominal 0.5 s prediction interval is 7.0 km/s, the bus's orbital speed, which was
close enough to be worth testing as a frame-and-epoch carry. It is not one. Logging how far the state
the prediction *departs from* travelled between readings, over 3,792 observations:

| per cycle, median | |
| --- | --- |
| the departure state moved | **679 m** |
| the predicted impact moved | **3,621 m** |
| ratio | **5.43x**, quartiles 3.16 to 75.2 |

A carry would read 1.00. So the predictor **amplifies** its departure state's motion about fivefold
rather than reporting it, and the interval is ~0.1 s of travel rather than 0.5 s.

That is the shape of a genuine sensitivity rather than a frame error, and it has a candidate: a
coasting bus 679 m further along the same arc should land in the same place, so either the arc is not
the same between readings — the trim, drag, or a state that carries noise — or the predictor's answer
depends on where along the arc it is entered, which would be an integration or terrain-sampling
artefact. `cot(13.8 deg)` is 4.1, and the shallow-arrival amplification of any height disagreement is
the first thing to price.

## 4. Throughput is a setting, and the ladder's gate was mis-read

`App.Run` computes `dtPlayer = min(elapsed, 1f / GameSettings.Current.Simulation.MinTargetFrameRate)`.
The 33.3 ms per frame that `METRE-LEVEL.md` 5b treats as fixed is `MinTargetFrameRate = 30`, a public
mutable field with a 1-10000 UI slider and a TOML key. It appears again in the solver governor's
`_achievedSpeedFraction`, so lowering it raises the step **and** relaxes the deadline.

On 5b's own measurement — 8 rockets, 33 vehicles, 78.7 ms frame, 0.41x sim rate —
`minTargetFrameRate = 10` leaves `dtPlayer` unclamped and gives **1.00x instead of 0.41x: 2.4x for a
config line**, with no change to frame time.

The cost is a coarser step wherever the step matters, and the mod already owns the instrument for
that: `WorldSpeed.ForStep` turns "I want this step" into a speed request, so the burn and trim can
hold today's step by asking for a speed below 1 while ascent and coast run long.

Three more, all public or config:

* `orbitSolvers` defaults to `ProcessorCount / 2`, giving 5 vehicle worker threads of 16 — consistent
  with the 412% of a 1600% budget 5b measured. Boot-time setting.
* The per-vehicle render-data pass runs **four times a frame**: viewports 1, 4 and 5 are constructed
  with `IsOffscreen = true` and it is never cleared, and `UpdateRenderData` culls only on a 1-pixel
  angular test with no frustum cull. Clearing it on the three hidden viewports takes four passes to one.
* Commanding attitude forces a vehicle **off rails** for as long as it is commanded, putting it on the
  sub-stepped full-physics path instead of a closed-form Kepler evaluation. A bus pointed through a
  25-minute coast is integrated the expensive way throughout — a candidate for 5b's unattributed
  ~2.0 ms per vehicle, and cheap to test against `SolverLoad`.

`Profiler.MainThread` is public, and `Program.OnFrame` already tags `UpdateVehicleRenderData` and the
rest. 5b says the missing piece "wants a profiler rather than another guess" — it is there.

## The ranked plan

| # | Do | Cost | Worth |
| --- | --- | --- | --- |
| ~~1~~ | ~~Diagnostic: log what a warp was started over the top of~~ | done | confirmed: 6 others burning |
| ~~2~~ | ~~Fix: fold `!NeedsShortSteps` over every computer~~ | done | 8 of 8 at 33 ms; median 32.34 -> 8.80 km on one pair |
| **2b** | The 20 s clearance knife-edge — the second branch, now the largest at long range | 12 paired shots | 12,902 km: 8.80 km -> ? |
| **3** | **Diagnostic**: log the release residual and `_response` per flight, unconditionally | 0 shots | the budget cannot be closed without it |
| **4** | Measure `dMiss/dV` at both flown geometries in `ErrorBudgetTests` | 0 shots, minutes | settles what the residual is worth; every other item's interpretation depends on it |
| **5** | Derive `HoldingCostsMetresPerSecond` from flight time and arrival angle | 12 paired shots | 2,000 km: **99 -> plausibly 10-30 m** |
| **6** | `_worseFor` as a run counter; headless counterfactual over `RoughGround` first | 0 shots then 12 | long range, if `settled` stops being modal |
| **7** | Seed `Resume()` from the burn's last measured response | 12 paired shots | long range; decomposes the pass-one trim demand |
| **8** | `minTargetFrameRate`, `orbitSolvers`, the three offscreen viewports, coast off-rails | hours | **2.4x or better throughput**, which every row above pays for in shots |
| **9** | Hand the terminal fraction of the burn to `FlightComputer.Burn` | days | removes the frame quantum outright; gates rungs C and D |
| **10** | `AimWithinTrimBudget` to 24 shots, **pre-declared** | 24 shots | 0.85x [0.53, 1.14], the only arm that has never lost |

**1-4 need no flying.** 8 makes everything after it cheaper and should come before 5-7 if a night is
short.

**Score every arm on the terminator table, never the median.** A median cannot see mass moving
between two modes, and the baseline swings 2.7x between sessions.

## Stale lines, ranked by what they close off

1. **`METRE-LEVEL.md` §5: the 200 fps ceiling does not exist.** `Program : App` binds `KSA.App`,
   whose `Run()` has no `FrameLimit` and no sleep. `Core.App.FrameLimit = 200` is in a class KSA never
   subclasses, and `Core.Time.Update()` — the only reader of `Time.FrameLimit` — is called nowhere, so
   `display.fpslimit` does nothing. Vsync is the only limiter. The line reads as a wall that is not there.
2. **`METRE-LEVEL.md` 5b: "throughput is bought by frame time and by nothing else."** See item 4; the
   conclusion that the ladder stops at rung C should be re-derived.
3. **`IcbmProgram.cs` and `CLAUDE.md`: "an engine can only be shut down on a frame boundary."** True of
   the mod's command path, false of the engine — and stating it as an engine constraint closes off item 9.
4. **The seven-degree arrival**, asserted in `ARRIVAL-ANGLE.md`, `KINETIC-FLOOR.md`, `METRE-LEVEL.md`
   and `IcbmConfig.cs`. The flown geometry arrives at 13.6-17.5 degrees. `METRE-LEVEL`'s ladder and
   `KINETIC-FLOOR`'s two columns are priced off the seven.
5. **`KSA-TERRAIN.md`: "there is no raycast, no collider query."** `BoundingVolumeHierarchy.LookupBvhDirection`
   is a public ray query, and there is a Bepu triangle collider on a 2 m grid within 8 m of clearance.
6. **`accurate: true` degrades silently to the coarse answer** when `TerrainModifiersRenderData` is
   null — the loop is bounded by `?.NumModifiers`, so a null runs the body zero times and skips
   erosion, detail and the launch-site levelling decals with no log line. Call
   `SetupModifierRenderData()` when a body is first resolved and assert it in the world dump.
7. **`EIGHT-ROCKETS.md`: "the keep-out interlock is provably dead."** It shipped on, resolved at
   0.49x, p=0.017.
8. **`VehicleCommand.cs`: "KSA exposes no way to set a throttle outright."** True of the manual
   channel; `PlannedBurnThrottle` is solved by the engine in `BurnMode.Auto`.

## Dead — do not spend on these

`TrimCeilingFromBudget` (harmful: 0 of 32 payback against 12, four shots at 54-105x).
`SeparationClearance.TimeoutSeconds` 20 -> 25 (resolved 1 of 16, and `clearance` is 0 of 96 endings now).
Tightening `AimCorrection.SteadyMetres` (worse at every value: 100 -> 2,329 m, 25 -> 2,568).
`MaxResponse` (6/12/24/60 bit-identical) and `ImprovedByMetres` (50/250/1000 identical).
Per-sub-step gravity alone (lost 3/3; only flyable paired with the 1 ms sub-step).
Every tube-cant item — the bus was straightened, all six axes are `(1,0,0)`, and the flown 5 m
within-group spread confirms it against the 233 m cant would give.
Never freezing the aim (lost 5.7x; the freeze is load-bearing).

## Ranked highly on reasoning since refuted — the pattern to watch

`payback` as a lever (it is a selection effect: the rule only fires under ~156 m).
`trim` as the dominant terminator implying `TrimCeilingFromBudget` (attacking the named terminator
cost the good ending).
The 20 degree arrival floor (priced against a 7 degree baseline that was already 13.6).
"The clearance never succeeds" (the absence of a log line measured the logger).
The 24 ms slow-regime screen (29.8 ms gave 0 passes one night and 2 another).

**Five of these five were counts or absences read as mechanisms.** The terminator table is a
diagnosis, not a lever, and an instrument with one output cannot tell a cause from a consequence.
