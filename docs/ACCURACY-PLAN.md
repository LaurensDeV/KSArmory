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
| median miss | **30 m** shipped since 3w (**10 m** with a 33 deg floor, 3t) | **6,664 m** | **250 m** on rough ground, **~60 m** at a flat aim (3s) |
| best shot | 52 m | — | **9 m**, group of 0.009-0.479 km |
| p90 | 198 m | 28,652 m | — |
| within a group of six | **5 m** | 6 m | 6 m |
| shape | unimodal, CV 0.58 | **bimodal**, CV 1.08 | corrections mostly finish |

**Overtaken by 2026-09-02.** Two faults found and fixed that day — an arrival floor latching a
budget of zero off the pad (3ah's sibling) and the aim correction pinning itself to its 300 km clamp
off a prediction flown from sea level (3ah) — took the same save, aim and geometry from **4 flights
at 302-310 km and 4 at 0.01-2.15** to **8 of 8 between 0.023 and 0.330 km**. Every number in the
table above is from before them, and the 12,902 km columns should be read as history rather than as
where the shot stands. What is left is decomposed in **3ai**: about half of it is the round
disagreeing with its own predictor, which no correction loop can reach.

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

## 3d. RETRACTED — that measurement was a frozen readout — 2026-08-31

**Everything section 3d claimed was an artefact and none of it is true.** It reported that the aim
loop's observer moves 45x faster than its authority — 78 m of aim against 3,520 m of impact, a
secant of -35.7 — over 3,788 observations. There were **88 observations**, and all of them happened
before launch.

`AimCorrection.Observe` returns at `if (Settled) return;` **before** it writes
`LastAimMoveMetres` / `LastImpactMoveMetres` / `LastImpactAlongAimMetres`, and the log line printed
them regardless. Verified: **3,833 lines, 121 distinct tuples** — twelve per rocket, the rest byte
echoes of a settled loop's last reading. The medians of those echoes reproduce 3d exactly, which is
what made them look like a distribution.

The follow-up that "refuted the frame carry" is worse: `busMoved` updated every cycle while the
impact was frozen, so `impact / bus = 5.43` was a fresh number over a stale one and means nothing.

**Sixth instance of this trap in this repository**, and the first one self-inflicted inside a single
day. A readout that stops updating reads as its last value, which is indistinguishable from a live
one. `AimCorrection` now clears the three fields at the top of every `Observe`, so a cycle that takes
no reading reports none; `AimReadoutTests` pins it and fails against the old code.

### What is actually true, measured properly

**The frame carry is refuted, on far better evidence.** Across 52,819 coast cycles the world rate
swings the bus's own per-cycle travel by 275x — 3,570 m at 1x to 284,098 m above 20x — and the
impact step does not follow: p90 over bus travel goes 0.140 to 0.0011. A carry reads 1.00 and
constant. Through the burn the vehicle accelerates 0 to 7.0 km/s while the step falls the other way:
Rising 5,290 m, PitchProgram 3,820, **ClosedLoop 0 m median, p90 10 m**.

**And it is a drift, not a wander.** Burn, kilometre regime: sign-change rate **0.000**, net move over
path length **1.000** — every step the same direction. Coast: sign changes 0.079, lag-1
autocorrelation **+0.52**. The only white-noise regime is the frozen one, which is the print quantum.

**What the step is proportional to is the miss itself**: p90 |Δ| / miss is **0.026 to 0.081 across
every band from 3 km to 1,000 km**, on both observer populations, n=74,000. Scale-free, once per
solve, one direction. That is a re-solve converging, not an observer that has to be filtered — and at
the operating point it is under 10 m a cycle.

## 3e. The loop reads twice before it has flown once — 2026-08-31

Over 94 coast corrections in `~/shots/warpcoast`, by reading index:

| reading | n | median miss km | ratio to best | median `_response` | worse than best |
| --- | --- | --- | --- | --- | --- |
| 1 | 94 | 4.20 | — | 1.00 | — |
| 2 | 94 | 3.83 | 0.90 | 1.00 | 14% |
| **3** | **90** | **6.94** | **2.24** | **3.13** | **71%** |
| 4 | 75 | 4.94 | 1.78 | 1.01 | 68% |
| 5 | 38 | 0.61 | 0.32 | 1.00 | 26% |

Gap from reading 1 to 2: median **2.03 s**. From 2 to 3: **41.3 s**.

**Reading 2 arrives before anything has been flown.** Post-cutoff the prediction departs from the
vehicle's own state and never reads the aim — the aim reaches the impact only when the trim changes
the bus's velocity, a pass later. So reading 2 re-reads the same number, the loop deadbeats on it a
second time, and the bias ends at about **twice** the error. Reading 3, forty seconds later, duly
reads twice the miss; the secant estimator then reads **3.13** off that manufactured excursion and
divides the next two steps by three.

The guard misses it because `IcbmComputer` gates on `!TrimIsFiring`, and
`TrimIsFiring = Armed && !Done && _mayTrim`. While the keep-out interlock holds the trim off,
`_mayTrim` is false — so the window is open *and* nothing has been flown.

**`AimCorrection.Settled` ends 0 of 96 flights.** The loop's own stopping rules end nothing; the
actuator does.

### Fixed and flown 2026-08-31

`PostBoostAim` arms a reading only once it has seen the trim unsettled since the last one — three
exemptions, each with a reason: the first reading, a trim that gave up, and a bounded fallback for a
demand already inside the settle band.

The mechanism check is deterministic and it passed:

| gap between passes | before | after |
| --- | --- | --- |
| 1 to 2 | **2.03 s** | **19.0 s** |
| 2 to 3 | 41.3 s | 20.8 s |

Evenly spaced, which is what reading off a flown correction looks like. The shot: **0.040, 0.091,
0.092, 0.120, 0.133, 0.450, 0.469, 0.715 km**, median 0.13 — the best long-range group recorded here
— and six of eight ended on `payback` at 33 to 200 m.

**Six of eight is one shot and settles nothing**, against 40 of 96 as the standing rate; the count is
what a paired night has to score, and the failure mode to watch is `payback` converting to `budget`
or `clock` as the extra wait eats the tank.

## 3f. The terminator lever is exhausted at 2,000 km — flown 2026-08-30

96 flights in `~/shots/2026-08-30-1818`, `--paired 'base|aimbudget:AimWithinTrimBudget=true'` at
`06fabec` — so this tree carries the auto-warp interlock and the aim spread, and **none** of 3c, 3d
or 3e, which were verified at 12,902 km and have never been flown here.

Four rockets an arm a shot, the assignment rotated by shot number, so each of the eight seats flew
each arm exactly six of twelve. Read it with `shot-report.py --paired`.

| | base | aimbudget |
| --- | --- | --- |
| flights | 48 | 48 |
| median | 0.10 km | 0.10 km |
| `payback` | 45 | **48** |
| `trim` | 2 | 0 |
| `noimprov` | 1 | 0 |

`aimbudget` is **0.85x [0.53, 1.14] at 97%**, 9 of 12 shots, sign p=0.146, signed-rank p=0.129 —
**unresolved**, and the night rules out harm beyond 14%.

**The finding is the column, not the ratio.** `payback` ends **93 of 96** flights. Every gain of the
preceding week worked by letting more corrections finish, and at this range there are **three
flights** of headroom left in the whole night. `aimbudget` converted all three non-`payback` endings
and could not have shown more than that here, which is why its interval is wide at n=48: a lever
cannot be measured against a quantity that is already spent.

**The seat gradient is gone.** 0.072 to 0.117 km across the eight seats, rank correlation
**rho=-0.09, p=0.357**, against the 175x monotone gradient of 8y. It and the 40-of-96 `payback` rate
were one cause — the auto-warp — and the interlock closed both.

So 2,000 km at a 17.5 degree arrival sits at **100 m against the ~82 m envelope floor**: rung A,
reached, without B1 ever being built. Further correction-loop work at this range is arguing over
18 m, and the next lever is the arrival angle rather than the loop.

## 3g. Shortening the range makes it worse, and the reason is passes — flown 2026-08-31

One validation shot at **418 km** (`mirv:24.849,-80.604`, `SOLVER SCALE 8`, HEAD `6651786`), flown
before committing a night to the range ladder. It flies, and it is far worse:

| | 2,000 km, 96 flights | 418 km, one shot |
| --- | --- | --- |
| miss | **0.10 km** | **0.36 to 3.63 km** |
| `payback` | 93 of 96 | **1 of 6** |
| `trim` | 2 of 96 | **5 of 6** |
| passes | many | **1 or 2** |

**Read from the log, not from a verdict.** The run was killed at 900 s by the operator's own
timeout, with the world at 0.36-0.64x real time on 15 vehicles, so six of the eight flights recorded
an impact and an ending and two did not. The six are complete flights; the group score is not.

Every non-`payback` ending reads *the trim stopped before the next one*, and the flights are 42 to
58 seconds long. **The binding constraint at short range is the number of correction passes, not the
geometry.** 3e's fix makes a pass wait for the trim to fly — measured at 19 s — so a 2,000 km flight
fits many and a 418 km flight fits one. Within-group spread stays ~0.01 km throughout, so this is a
bias and not scatter.

That is the failure mode 3e was committed watching for, and it does not appear at the range 3e was
flown at.

### So the ladder cannot be climbed by shortening the shot

`docs/METRE-LEVEL.md`'s rungs pair each arrival angle with a *shorter* reach, and the accuracy is
credited to the angle. This shot separates them: the short shot has the steeper arrival and lands
**an order of magnitude worse**, because shortening also removes the time the loop needs.

**And steepening at a fixed range is worth much less than the rung table implies.** The lever is
`cot γ`, so against today's 17.5° baseline:

| arrival | `cot γ` | vs 17.5° | from 100 m |
| --- | --- | --- | --- |
| 20° | 2.75 | 0.87 | 87 m |
| 25° | 2.14 | 0.68 | 68 m |
| 32° | 1.60 | 0.50 | 50 m |
| 40° | 1.19 | **0.38** | **38 m** |

A **20 degree floor buys 13%** — inside the [0.53, 1.14] the paired instrument resolved at n=12, so
it is unmeasurable as well as small. This is the same mispricing already in *Ranked highly on
reasoning since refuted*: that entry priced 20° against a 7° baseline that was really 13.6°, and the
baseline is now 17.5°.

**The experiment that is left is the arrival angle at a fixed 2,000 km**, which holds the flight time
that lets the loop finish and moves only `cot γ`. Arms `25|32|40` against base, predicted 68/50/38 m
against 100. If the misses do not fall with `cot γ`, the ladder's premise is wrong at this range and
that is worth knowing for one night.

```bash
KSARMORY_SCENARIO_SAVE="SOLVER SCALE 8" ./tools/shot-batch.sh \
  --aim 10.622,-80.604 \
  --paired 'base|a25:MinArrivalAngleDeg=25|a32:MinArrivalAngleDeg=32|a40:MinArrivalAngleDeg=40' \
  --blocks 12
```

Twelve blocks, four arms of two rockets, about 1.3 hours. **Read the attribution table's `arr deg`
before the ratios**: an unaffordable floor falls back to the cheap arc rather than failing, so an arm
that did not steepen is a null that means nothing about `cot γ`.

## 3h. The arrival angle has an optimum near 26 degrees, not a ladder — flown 2026-08-31

12 shots, 96 flights, `~/shots/2026-08-31-1351`, HEAD `2d0412e` (3c, 3d and 3e all aboard).
`--paired 'base|a25:MinArrivalAngleDeg=25|a32:MinArrivalAngleDeg=32|a40:MinArrivalAngleDeg=40'` at
2,000 km, four arms of two rockets, rotated by shot.

**Every floor was affordable and every flight held it** — base 17.2-18.0 deg, a25 25.9 on 24 of 24,
a32 33.0, a40 41.1. The manipulation is clean, so a null here would have meant something.

| arm | arrival | pooled median | paired ratio | won | sign p | |
| --- | --- | --- | --- | --- | --- | --- |
| base | 17.7° | 0.11 km | — | — | — | |
| **a25** | **25.9°** | **0.08 km** | **0.44x** [0.30, 0.79] | **11 of 12** | **0.006** | **RESOLVED** |
| a32 | 33.0° | 0.11 km | 0.80x [0.59, 1.12] | 7 of 12 | 0.774 | unresolved |
| a40 | 41.1° | 0.13 km | 0.66x [0.39, 1.18] | 9 of 12 | 0.146 | unresolved |

**`cot γ` predicted 0.66 / 0.49 / 0.37, monotone. The night gives 0.44 / 0.80 / 0.66.** a25 beats its
prediction by half again; a32 and a40 miss theirs by two-thirds. There is an **optimum near 26
degrees**, and `docs/METRE-LEVEL.md`'s ladder — which assumes steepening always pays — does not
describe this vehicle at this range.

Steeper is also *erratic* rather than merely flat. Shots worse than base: **a25 1 of 12, a32 5 of 12,
a40 3 of 12.** a25 is the only arm that is consistently better, which is why it is the only one that
resolved.

**Steepening does make corrections finish**, and that is not the whole story either: `payback` ends
24 of 24 on every steep arm against **21 of 24** on base. So the terminator improves monotonically
with angle while the miss does not — another instance of the standing rule that the terminator table
is a diagnosis and not a lever.

### Two predictions this night refuted, one of them mine

**3g's `cot γ` arithmetic was necessary but not sufficient.** It correctly killed the 20 degree floor
as unmeasurable; it wrongly implied 40 degrees would be the best of the three.

**And the payback-floor prediction was wrong.** The floor is `_lastCycleSeconds x 26 m/s`, and 3e took
the cycle from ~2 s to ~19 s, which predicted base degrading from ~100 m to ~500 m. **Base came in at
0.11 km.** Either the cycle is not 19 s at this range or the floor does not bind where the argument
put it; the argument stands unsupported either way and should not be repeated without a measurement
of `_lastCycleSeconds` per range.

**Caveat on the baseline only.** Frame time was **100.8 ms** against last night's 29.8, so base
against last night's 0.10 km is a between-night comparison in a different regime and is worth
nothing. The arm comparisons are within-world and carry no such term, which is the whole reason the
paired design exists.

**Not made the default.** A 25 degree floor costs propellant and reach, which is a trade a player
owns rather than one this mod should make for them.

## 3i. The miss is not the velocity precision, and that retires section 2 — measured 2026-08-31

Mined out of 3h's 96 flights, no new flying. Every flight reports `trimmed to X m/s`, named for its
craft, so the post-boost velocity error is joinable to that flight's own miss.

| | |
| --- | --- |
| post-trim residual | **0.0200 m/s** median, range 0.0060-0.0300 |
| miss | 0.105 km median |
| **rank correlation, residual vs miss** | **rho = -0.109, t = -1.06, n = 96** |

**None.** Across a fivefold spread of residual the miss does not move, and the sign is if anything
backwards. The arithmetic agrees: at this geometry's ~690-1,000 m per m/s, 0.0200 m/s is **14-20 m**
of miss against **110 m** flown — but the correlation is the stronger statement, because it assumes
no sensitivity at all.

**So how accurately the bus reaches its velocity target is not what sets the miss.** The guidance
delivers an arc good to about 17 m and the warheads land 110 m away.

### This retires section 2 before it is built

Section 2 is the sub-frame engine cutoff — `ActiveNozzle.ComputeThrustMod`, the branch
`VehicleCommand` cannot reach, ranked second in this plan and never attempted. It attacks the
**cutoff** residual, which is *upstream of the trim*: the trim already takes it to 0.020 m/s, and
0.020 m/s has no measured influence on where the warheads land. Building it would divide down a term
that is already six times below the binding one and does not correlate with the outcome.

It stays written down for the day the aim is fixed and the residual becomes the floor. It is not the
next thing to build, and the reasoning that ranked it there priced it against `METRE-LEVEL.md`'s
residual columns without ever checking that the residual predicts the miss.

### What is left is the observer

The miss is set by **where the shot is aimed**, not by how precisely it is flown there — so the term
that matters is `ImpactPredictor`'s fidelity, which is the one thing `AimCorrection` cannot see past.
That is this file's own standing rule: *a correction loop can only remove what its observer can see*,
and it is exactly how the drag shortfall hid for so long while the loop reported zero.

`Config.TraceWarhead` is the instrument and it already exists — one warhead followed down beside
`ImpactPredictor` re-flown from where it has got to. **The discriminator is whether the two part
smoothly or in a step**: smooth is a model error carried the whole way down, a step is an event at
release. Different causes, different fixes, and one short batch tells them apart.

## 3j. The miss is the miss the loop agreed to accept — measured 2026-08-31

`ScenarioRunner.BeginBallistic` already sets `_config.TraceWarhead = true`, so every scripted shot
ever flown carries `WarheadTrace`'s decomposition. 3h's 96 flights, mined, no new flying.

| | median | |
| --- | --- | --- |
| walk from the release probe | **4 m** | what the round did that the predictor did not |
| accepted predicted miss at `payback` | **109 m** | what the loop settled for |
| flown miss | **125 m** | |
| payback threshold | **156 m** | `6.0 s x 26.0 m/s` |

**The predictor is right to 4 m — 3.2% of the miss.** It is not the observer that is wrong, and
`ImpactPredictor` is not the next thing to fix. The round goes where the prediction says, and the
prediction is compared against an aim the loop **chose to stop moving**.

`payback` fires when `PredictedMissMetres <= _lastCycleSeconds x HoldingCostsMetresPerSecond`, so what
it accepts is a floor set entirely by those two numbers. The median threshold is **156 m**, which is
`FirstCycleSeconds` exactly — most flights stop on the seed cycle — and the flown miss lands beside
it. Independently, the 418 km shot's `payback` line read *371 m out* and that warhead landed at
**360 m**.

### 3h's retraction of the payback-floor argument was itself wrong

3h recorded the argument as unsupported because base flew 0.11 km where a 19 s cycle predicted
~500 m. **The mechanism was right and the number came from the wrong range.** The 19 s figure is
3e's, measured at 12,902 km; at 2,000 km the cycle is the 6.0 s seed, the floor is 156 m, and base
flew 110-125 m against it. The argument is confirmed, not refuted, and 3h's paragraph is superseded
by this one.

### So the lever is a constant measured once, on one shot

`HoldingCostsMetresPerSecond = 26.0` is derived from a single flight — the ejection kick worth
8.421 km at cutoff and 5.672 km at +106 s, which is 25.9 m/s. It is a real cost and it is **linear in
the floor**: halve it and the loop is allowed to keep correcting to half the miss.

Nothing has ever checked it at another range or another arrival angle, and 3h just moved the arrival
angle by eight degrees for a 0.44x. If the true holding cost at a 26 degree arrival is a third of 26,
the loop is stopping three times too early and the whole 125 m is the constant being wrong.

**That is the next shot**, and it is the first one aimed at a term proven to set the miss rather than
inferred to.

## 3k. The bus comes back and hits the stage it dropped — seen in play 2026-08-31

Reported from watching a flight: `GeoSat FAT_1` running into its spent booster. `ProximityWatch` has
been logging it the whole time, and the line even names the fault —
`closest approach to the spent stack: 2.3 m at +22.2 s, keep-out 15.3 m -- CAME BACK INSIDE THE
KEEP-OUT`. Nothing had ever read it.

Closest approach by arrival angle, over 3h's 96 flights:

| arm | arrival | closest approach | breached the keep-out |
| --- | --- | --- | --- |
| base | 17.7° | **7.5 m**, min **1.8 m** | **19 of 24** |
| a25 | 25.9° | 15.3 m | 4 of 24 |
| a32 | 33.0° | 15.3 m | 1 of 24 |
| a40 | 41.1° | 15.3 m | **0 of 24** |

15.3 m is the sentinel — those arms never came inside at all. 3j's night, where every arm flies the
baseline trajectory, reproduces the base row on all four: ~9 m median, 16 of 20 breaching, and **no
arm worse than another**, so the holding cost is not the cause and neither is anything else varied
since.

**The mechanism is already written down.** `Sim/SeparationClearance.cs` says *the shove is the
separation, so nulling it ends it* — the decoupler's 1.1 m/s is what carries the bus clear, `BusTrim`
sees that shove as error and nulls it, and the bus stops leaving. *Came back* is the instrument
saying exactly that. The steep arms escape it because their trim demand differs, not because
anything about the separation changed.

**It is not currently costing warheads:** 80 of 80 shots report `6 of 6 arrived`, and the misses are
unaffected. So this is a defect with a visible consequence and no measured cost yet — which is
precisely the shape that gets ignored until it destroys a bus.

**Not fixed here, and not diagnosed to a fix.** The obvious move — hold the trim off until the stack
is clear — is what `KeepOutCoversTheClearance` already does, and it is a shipped setting flown at
*87 of 144 flights abandoned*. Whether the keep-out should instead be enforced as a floor the trim
may not cross is untested, and CLAUDE.md's rule applies: ship the diagnostic, not the guess.

## 3l. The holding cost was four times too high, and it was the whole floor — flown 2026-08-31

12 shots, 96 flights, `~/shots/2026-08-31-1634`, HEAD `cc6cc58`. Every arm flies the same 17.7 degree
trajectory; only the payback threshold differs.

| arm | m/s | first-cycle floor | pooled median | paired ratio | won | p | |
| --- | --- | --- | --- | --- | --- | --- | --- |
| base | 26.0 | 156 m | 0.12 km | — | — | — | |
| h13 | 13.0 | 78 m | 0.09 km | 0.53x [0.36, 1.26] | 9 of 12 | 0.146 | unresolved |
| **h6** | **6.5** | **39 m** | **0.03 km** | **0.26x** [0.16, 0.71] | **11 of 12** | **0.006** | **RESOLVED** |
| h3 | 3.25 | 20 m | 0.03 km | 0.33x [0.13, 0.62] | 11 of 12 | 0.006 | RESOLVED |

**120 m to 30 m at 2,000 km**, on a constant, with no change to the trajectory or the guidance. Two
arms resolved independently at p=0.006.

### The optimum is interior and both sides of it are visible

| arm | `payback` | `trim` | `clock` | `noimprov` |
| --- | --- | --- | --- | --- |
| base | 24 | 0 | 0 | 0 |
| h13 | 24 | 0 | 0 | 0 |
| h6 | 22 | 0 | 1 | 1 |
| **h3** | **20** | **3** | 0 | 1 |

Above the optimum the loop stops early and every flight ends on `payback`. Below it the loop keeps
correcting until something else stops it — `h3` loses three flights to the **trim running out**, and
is worse than `h6` despite a floor half the size. That is the cost the constant exists to charge,
appearing exactly where it should, which is what makes 6.5 an answer rather than "smaller is better".

### What this says about the constant

`HoldingCostsMetresPerSecond = 26.0` was derived from **one flight at one geometry** — an ejection
kick worth 8.421 km at cutoff and 5.672 km at +106 s. At 2,000 km and 17.7 degrees it is about four
times too high, and because the floor is linear in it the whole 120 m was that error.

**Not made the default yet, and the reason is the finding itself.** 6.5 is now measured at one
geometry, which is precisely how 26.0 got here. It needs a second range or arrival angle before it
becomes a constant, and 3h's arms are the obvious ones to re-fly it against.

**The principled fix is to stop hardcoding it.** The holding cost is the rate at which the ejection
kick's leverage decays, and `ImpactPredictor` can measure that directly — predict the impact for a
release now against one a second later and difference them. That is self-calibrating at every range
and arrival angle, and it is the same move `Sim/Warhead.cs` makes for blast radii: derive the number
rather than type it.

## 3m. The holding cost is not a constant — measured headlessly 2026-08-31

3l asked whether 26.0 was the wrong *value* or whether the payback rule had the wrong *form*.
`ImpactPredictor` answers it without a game, the same way the original number was taken: difference
the impact of a release now against one 106 s later, and read the decay.
`HoldingCostTests` pins it.

| range | arrival | kick worth | at +106 s | **decay** |
| --- | --- | --- | --- | --- |
| 500 km | 56.4° | 156 m | 69 m | **0.82 m/s** |
| 1,000 km | 36.9° | 188 m | 91 m | 0.91 |
| **2,000 km** | **20.5°** | **339 m** | **191 m** | **1.40** |
| 4,000 km | 10.3° | 1,187 m | 835 m | 3.32 |
| 8,000 km | 4.7° | 7,112 m | 6,006 m | 10.43 |
| 12,900 km | 2.1° | 30,655 m | 28,345 m | **21.79** |

**The answer is the value, and the deeper answer is that it should never have been a constant.**
Applied at 2,000 km, 26.0 overcharges every cycle by about **19x**.

> **The two longest rows are not a geometry this mod flies — corrected 2026-09-01.** The sweep holds
> the release at 400 km and solves for speed, which at 8,000 km and beyond forces a nearly-orbital
> *grazing* arc: 4.7 and 2.1 degrees of arrival. A real intercontinental shot **lofts**, and the
> flown one arrives at **13.5 degrees**, where the vehicle's own probes measure **2.90 m/s** rather
> than the 21.79 this table predicts.
>
> So the "27x span" is partly an artefact of how the sweep was built, and the honest reading is
> narrower and worse for the constant: across the geometries actually flown — 1.33 m/s at 2,000 km
> and 2.90 at 12,900 — **26.0 is nine to twenty times too high everywhere**, not merely at short
> range. The rows up to 4,000 km stand; the last two describe a trajectory family the mod does not
> use.

That explains 3l exactly:

* base, h13, h6 and h3 all overcharge at 2,000 km — 26.0, 13.0, 6.5 and 3.25 against a true 1.40 —
  which is why every step down won.
* `h3` was not better than `h6` because by 3.25 the **trim budget** binds first: 3 of its 24 flights
  ended on `trim`. The holding cost stopped being the constraint before it stopped being wrong.
* 26.0 was never a bad measurement. It was a good measurement of a different shot.

### The fix is to derive it, and the derivation is two predictions

The loop already predicts the impact several times a second. Two more — release now, release a
second later — measure the decay at whatever geometry the vehicle is actually on, and the payback
rule becomes self-calibrating from 500 km to intercontinental. It is the same move `Sim/Warhead.cs`
makes for blast radii: derive the number rather than type it.

**Not built, and deliberately.** `IcbmConfig.HoldingCostMetresPerSecond` is the plumbing and it is
flown; wiring the derivation into it is a behaviour change nothing headless can score, and the rule
is that a fix is unverified until it has been flown. What this section buys is that the flight can
now be aimed at a number with a mechanism behind it rather than at a ladder of guesses.

## 3n. The measured holding cost is the best flown one — flown 2026-08-31

12 shots, 96 flights, `~/shots/2026-08-31-2011`, HEAD `254ddea`.

| arm | holding cost | budget cap | pooled | paired ratio | won | p | |
| --- | --- | --- | --- | --- | --- | --- | --- |
| base | 26.0 | — | 0.11 km | — | — | — | |
| h6 | 6.5 | — | 0.04 km | 0.45x [0.23, 0.68] | 10 of 12 | 0.039 | RESOLVED |
| h3b | 3.25 | yes | 0.03 km | 0.30x [0.13, 0.41] | 11 of 12 | 0.006 | RESOLVED |
| **h1b** | **1.4** | **yes** | **0.02 km** | **0.23x** [0.11, 0.39] | **12 of 12** | **0.000** | **RESOLVED** |

**110 m to 20 m, and 3m's headless number is the winner.** The decay measured off `ImpactPredictor`
at this geometry was **1.40 m/s**; the arm set to exactly that swept all twelve shots. A prediction
made without the game picked the best flown setting.

**And the floor is finally out of the way.** Endings per flight:

| arm | `payback` | `noimprov` | `trim` |
| --- | --- | --- | --- |
| base | 24 | 0 | 0 |
| h6 | 23 | 0 | 1 |
| h3b | 23 | 1 | 0 |
| **h1b** | **16** | **8** | 0 |

At 1.4 a third of the flights stop because the loop **runs out of improvement** rather than because
the rule releases them, which is the first time the correction has been allowed to converge on its
own terms. `noimprov` lands at 0.03 km against `payback`'s 0.04, so those are the good endings.

### The night cannot say whether the budget cap did anything

**A design fault, mine.** `AimWithinTrimBudget` is on in `h3b` and `h1b` and off in `base` and `h6`,
so it is perfectly confounded with the holding cost and no comparison here separates them.

Worse for the stated reason: the cap was added because 3l's two `trim` endings refused a pass over
its 47 m/s ceiling with 946 m/s in the tank. **This night has zero ceiling refusals on any arm,
including the uncapped ones.** So the mechanism the cap was brought in to prevent never occurred, and
its contribution is not merely unmeasured but has no evidence of a route to act through.

The whole gain is attributable to the holding cost until an arm flies **1.4 with no cap**. That is
one arm and it is the next thing to fly.

## 3o. What is left is the trim's per-axis overhead — measured 2026-08-31

Mined from 3n's 96 flights, no flying. Five things, each one closing off a lever.

**1. The flown miss is the accepted miss, on every arm.** `payback` releases at
`cycle x holding cost`, and what the loop agrees to is what lands:

| arm | accepted | threshold | implied cycle | flown |
| --- | --- | --- | --- | --- |
| base | 108 m | 351 m | 13.5 s | 110 m |
| h6 | 40 | 109 | 16.8 s | 40 |
| h3b | 27 | 54 | 16.6 s | 30 |
| h1b | **18** | **24** | **17.1 s** | **20** |

**2. The miss is common mode, not dispersion.** Within-group spread is **0-5 m** against a 20 m group
mean — the six warheads land on top of each other. Every per-warhead term is therefore irrelevant at
this scale: release dispersion, tube cant, the round's sub-step. `docs/METRE-LEVEL.md`'s B5 and B3
are not what is in the way.

**3. The cycle is 16.8 s and 99.3% of it is the trim firing.** 0.0 s before the first burst, 0.0 s
after the last. **There is no dead time to reclaim** — 3e's wait for a flown reading is not a wait,
it is the trim working, so shortening it recovers nothing and reintroduces the double read.

**4. Three axes a pass, one at a time.** Median 3 distinct directions per correction, which
`Sim/BusTrim.cs` fires sequentially on purpose: the stop threshold is half a frame of a thrust only
measurable along the direction being fired.

**5. And the cycle does not shrink as the corrections do** — 17.9 s at pass 1, 16.9 at pass 2, 16.5
at pass 3, while the demand falls by orders of magnitude. So the 16.8 s is **not the delta-v being
delivered**: it is a fixed overhead of about 5.6 s an axis, paid three times, whatever is being
flown.

### So the remaining lever is concurrency, and it is worth about three times

`3 x 5.6 s x 1.4 m/s = 24 m`, which is the floor and therefore the miss. Firing the axes together
rather than in sequence removes two thirds of it — about **7 m** — and the bus has the authority:
all six directions, 4.000 fore and aft and 4.243 in each lateral, off
`tools/model/checkring.py --translation`.

**What stands in the way is the reason the sequence exists**, and it is a real one: the stop
threshold is measurable only along the axis being fired. Firing three at once means stopping each on
its own component of a delta-v that is being changed by the other two. That is a design problem, not
a constant to retune, and it is the first thing in this file for a while that cannot be settled by
picking a better number.

## 3p. The derivation works, and does not beat the number it derives — flown 2026-08-31

12 shots, 96 flights, `~/shots/2026-08-31-2215`, HEAD `0a374e3`. Three arms, each seat flying each
arm exactly four times.

| arm | pooled | paired ratio | won | p | |
| --- | --- | --- | --- | --- | --- |
| base (26.0) | 0.10 km | — | — | — | |
| **derived** | 0.03 km | **0.28x** [0.24, 0.33] | **12 of 12** | 0.000 | RESOLVED |
| h14 (1.4 by hand) | 0.02 km | 0.19x [0.15, 0.31] | 11 of 12 | 0.006 | RESOLVED |

**The measurement reproduces itself in flight.** The vehicle's own probes report a median of
**1.33 m/s** across the night, range 0.27-2.28, against the **1.40** measured headlessly at this
geometry in 3m. Zero refusals in 96 flights.

**And it does not beat the hand-set value.** Paired directly, `derived` against `h14` is **1.62x**
with 4 wins to 7 and sign **p=0.549** — unresolved, and the point estimate favours the constant. The
intervals overlap; what the night establishes is that both crush the shipped 26.0, not that either
beats the other.

### It over-drives the loop, and the terminators say so

| arm | `payback` | `noimprov` | `clock` | `trim` |
| --- | --- | --- | --- | --- |
| base | 32 | 0 | 0 | 0 |
| **derived** | 7 | **21** | **3** | 1 |
| h14 | 18 | 13 | 0 | 1 |

`derived` runs the loop to its own convergence on 21 of 32 flights against `h14`'s 13 — because the
measured cost *falls* as the flight proceeds, to about 0.6 m/s late on, so the floor keeps tightening.
It also picks up the night's only three `clock` endings.

**A hypothesis, tested the same night and retired.** The suspicion was that the payback rule
over-values a cycle, because it credits one with removing the *whole* predicted miss. Measured from
the logs, what a pass actually removes:

| pass | before | after | removed |
| --- | --- | --- | --- |
| 1 | 1,200 m | 300 m | **~80%** |
| 2 | 300 m | 100 m | **~52%** |
| 3 | 100 m | under 50 m | — |

`derived` and `h14` are indistinguishable here — 80/55 against 78/50 — so nothing about the loop's
behaviour differs between them, and the outcome gap is which pass they happen to stop on.

**And correcting the rule would make the miss worse, not better.** Valuing a cycle at 0.6 of the miss
turns `miss <= cycle x cost` into `miss <= cycle x cost / 0.6`, which is a *higher* threshold and an
earlier release. It would buy only the three `clock` endings, which landed at 0.04 km — at the
median. There is nothing here to fix.

### It should still be the default, and the reason is not this range

`h14` wins here by an amount the night cannot resolve, and **1.4 is only right here** — 3m measured
0.82 m/s at 500 km and 21.79 at 12,900. Shipping it would repeat 26.0's mistake with a fresher
number. The derivation is within noise of the best hand-tuned value at the one geometry where a hand
value exists, and it is the only option that is not wrong everywhere else.

That is `docs/WHAT-THE-PLAYER-SETS.md` step 1, flown: **the setting can go.**

## 3q. The arrival-angle night was flown in a regime the holding cost has since removed

Mined from 3h's night, no flying. It was flown at the shipped 26.0, so the first-cycle threshold was
`6.0 s x 26.0 = 156 m` on every arm.

| arm | arrival | passes | threshold | accepted | flown |
| --- | --- | --- | --- | --- | --- |
| base | 17.7° | **2** | 425 m | 119 m | 0.11 km |
| **a25** | 25.9° | **1** | 156 m | **68 m** | **0.08 km** |
| a32 | 33.0° | 1 | 156 m | 110 m | 0.11 km |
| a40 | 41.1° | 1 | 156 m | 124 m | 0.13 km |

**Every steep arm released after one pass.** Their first correction landed under the 156 m threshold,
so `payback` fired immediately and no loop ever ran; `base` took two, and its threshold grew to 425 m
because the second cycle's length replaces the seed. The three steep arms share a threshold exactly
because none of them reached a second cycle.

So a25's **0.44x is the quality of a single correction**, not convergence — the geometry genuinely
helping, since one steep correction beat base's two. And a32 and a40 being worse is one correction
landing further out, not a loop failing.

**Which means the comparison cannot be carried across 3n.** With the derived cost the floor at 2,000
km is about 24 m rather than 156, every arm would run several passes, and an angle whose first
correction is poor may converge to the same place as one whose first correction is good. The
arrival-angle ranking was measured in a regime that no longer exists.

### So step 2 is not "build the search" yet

`docs/WHAT-THE-PLAYER-SETS.md` step 2 is searching the arrival angle, and there is no objective to
search against: `cot γ` and the floor's closed form both say steeper is monotonically better, and the
flown ranking says the optimum is interior at 26 degrees. **Neither model reproduces the
measurement**, and this section says why the measurement may not survive re-flying.

The order that follows: **re-fly the angle ladder with `DeriveHoldingCost` on** — the same
`a25|a32|a40` against base, in the regime the mod will actually ship. That answers the ranking and the
compounding question in one night, and only then is there something to build a search against.

## 3r. The derivation does not hold at long range — flown 2026-09-01

12 shots, 96 flights at **12,902 km**, `~/shots/2026-08-31-2358`, HEAD `254ddea`. Flown to answer one
question before making the derivation the default: does it regress where the constant was thought to
be about right?

| arm | pooled | paired ratio | won | p | |
| --- | --- | --- | --- | --- | --- |
| base (26.0) | **0.25 km** | — | — | — | |
| derived | 0.33 km | **1.59x** [0.73, 2.23] | **3 of 12** | 0.146 | unresolved |

**Unresolved, and pointing the wrong way.** The interval spans 1.0 so harm is not established — but
neither is benefit, and both the point estimate and the win count favour the constant. Against 3n's
**0.28x, 12 of 12** at 2,000 km, the sign has reversed.

**It is not a difference in how the loop ends.** base 8 `noimprov` / 40 `payback`; derived 10 / 37 /
1 `trim`. The distributions are the same shape, so the derivation is not driving the loop somewhere
different — the shots simply land further out.

**And the endings mean the opposite thing at this range.** `noimprov` lands at **0.69 km** against
`payback`'s **0.20**, where at 2,000 km `noimprov` was the *better* ending (0.03 against 0.04). A loop
that runs out of improvement has converged at short range and failed at long.

### So the default does not move

`DeriveHoldingCost` stays off. It is resolved better at 2,000 km, unresolved and possibly worse at
12,900, and shipping it on the strength of the first would be the one-geometry generalisation this
file has spent two days correcting — the same error that put 26.0 in the code, made with a better
method.

**What it is still right about:** no constant is correct at both ranges either. 26.0 is nine to twenty
times the measured cost at both geometries flown. The answer is neither the constant nor this
derivation as it stands, and 3m's corrected table says why the measurement is harder than it looked:
the geometry a long shot actually flies is not the one a naive sweep produces.

### And the roster gradient is back at long range

**rho = +0.50, p = 0.000** — seats 1-3 at 0.056-0.093 km, seats 4-8 at 0.22-0.51. At 2,000 km it is
dead (rho +0.04 to -0.09 over four nights). Whatever the auto-warp interlock closed at short range is
open again here, and it is worth more than the arm being tested: a 9x spread across the roster
against a 1.59x between arms. Nothing has looked at it since 8y.

## 3s. At long range the miss is the predictor, and at short range it is not

Mined from 3r's 96 flights, no flying. This began as an investigation of 3r's roster gradient and
found something larger.

**The gradient is the pads.** The eight rockets stand 0.205 degrees of longitude apart — about 20 km
— so seat 8 launches **143 km** further from the target than seat 1, which is exactly the 12,902 to
13,044 km spread the scenario reports. Arrival angles are identical at 13.5-13.6 degrees and **every
one of the 96 flights had a 33 ms longest step**, so 8y's auto-warp cause is not recurring and the
interlock holds. The gradient is a range gradient wearing a seat's clothes.

**And the accepted miss stops predicting the flown one.** At 2,000 km the loop's accepted miss and
what landed agreed to a tenth on all four arms (3o). Here they do not: seat 5 accepts 51 m and lands
at 376.

`WarheadTrace` says why, and it is the reverse of 3j:

| | 2,000 km | **12,902 km** |
| --- | --- | --- |
| walk from the release probe | **4 m** | **157 m** |
| landing miss | 125 m | 254 m |
| the predictor's share of the miss | **3.2%** | **62%** |

Over 63 traced warheads the walk and the miss correlate at **rho = +0.707, t = +7.81**, and the walk
is **309 m downrange against 3 m cross** — a hundred to one, purely along-track.

**So 3j's conclusion is a short-range one.** There the predictor was right to 4 m and rightly
retired; at intercontinental range it is most of the error, and no amount of work on the correction
loop reaches it — the loop can converge perfectly and the warhead still walks 300 m. That is also
why 3r's derivation could not help here.

### The obvious cause is not the cause

`ImpactPredictor`'s own step is **converged**: integrating the same state at 2.0 s against 0.05 s
moves the impact by 0 to 4 m over flights up to 1,661 s. The fixed 2-second step is not accumulating
along-track phase error.

**What is left is a disagreement between two models of the same fall** — the round, stepped by
`RoundDriver` at frame rate under `Interceptor.MaxFaithfulStep`, against `ImpactPredictor`. They
share `Medium.Drag` by design, so the divergence is somewhere else: the terrain each stops on, the
sub-stepping, or the warp the coast runs under. **Which of the two is wrong is not established**, and
that is the question, not a conclusion.

### The discriminator, run — and my first two attempts measured a round nobody flies

> **Corrected 2026-09-01.** This section first reported that the round's integration walks over a
> kilometre and that **the impact moves with the player's frame rate**. Both were artefacts of the
> fixture, not the mod. Retracted in full below; the tests now set what the game sets.

`PredictorAgreementTests` flies one state through both — `ImpactPredictor` against a real `Slug` on
the same field, the same sphere and no drag. Getting that comparison honest took three attempts, and
each wrong one invented a different fault:

| the fixture | 30 fps | 60 fps |
| --- | --- | --- |
| profile default sub-step, one gravity sample a frame | 1,201 m | 420 m |
| the Mk 21's 1 ms sub-step, still one sample a frame | 1,580 m | 740 m |
| **1 ms sub-step and per-sub-step gravity — as the game runs it** | **51.6 m** | **51.6 m** |

**The reentry vehicle already sub-steps at a millisecond** (`SubStepSeconds = 0.001f`), and
`RoundFields.GravityAt` already re-samples gravity per sub-step. A fixture that omits either hands
the round one gravity sample a frame and holds it across every sub-step — which is a first-order
error that scales with the *frame*, and is where the kilometre and the frame-rate dependence came
from.

**Flown as the game flies it: 51.6 m on a 1,233 s fall, identical at both frame rates.** So:

* **The frame-rate claim is withdrawn.** The impact does not move with the display, and the argument
  built on it — that correcting the predictor to match the round cannot work because there is no
  fixed error — is withdrawn with it.
* **The integrator is a third of the walk, not all of it.** 51.6 m headless against the **157 m**
  measured in flight, so roughly a hundred metres is still unaccounted for and is somewhere the two
  models genuinely differ: drag, the terrain each stops on, or the warp the coast runs under.
* **The one-line Verlet change stays reverted**, and for a better reason than before: at the
  configuration the game actually uses there is no kilometre to remove.

The second test pins the invented fault deliberately — one gravity sample a frame is worth a
kilometre and does move with the display — because that is what `RoundFields.GravityAt` exists to
prevent, and nothing else in the suite said so.

**What is still open** is the ~100 m between 51.6 and 157, and two candidates are now excluded.

**Drag is not it.** Handed one exponential atmosphere and the reentry vehicle's own
`DragK = 1.5e-5`, the two paths land **50.5 m** apart against **51.6 m** with no drag at all. They
share `Medium.Drag` and they apply it the same way.

**Nor is the coarse-versus-accurate height field.** Both sample `accurate: true`, deliberately —
`IcbmComputer.TerrainRadiusAt` says so in as many words, because the round stops where `GroundTest`
says and a coarse sample is a different surface.

**Nor is the frame, on reading.** The predictor transforms `Cci -> Ccf` and asks
`GetTerrainHeightFromDirCcf`; the round builds `Cce` and asks `GetTerrainHeightFromDirCce`. Each uses
the engine variant matching its own frame, both clamp to sea level through the same
`GroundSurface.Height`, and the `_departsIn` un-carry that distinguishes them is **zero once the
engines are off** — which is the whole of the coast this happens in.

**What it looks like instead is the ground under the target.** `shot-report --terrain`:

| | 2,000 km target | 12,902 km target |
| --- | --- | --- |
| ground height spread | **0.0 m** | **836.9 m** |
| residual from a plane fit | 0.0 m rms | **121.4 m rms** |
| walk from the release probe | **4 m** | **157 m** |

The short-range aim sits on ground that is flat to the metre; the intercontinental one is on a
hillside that departs from a plane by about the size of the gap. Two models that stop at slightly
different places on rough terrain stop at different *heights*, and at a 13.5 degree arrival one metre
of height is 4.16 m of ground. It also explains the walk's heavy tail — 10 m at the lower quartile
against 326 at the upper — which a systematic frame error would not produce.

**So "at long range the miss is the predictor" is the wrong reading of 3s.** The confound is the
*target*, not the range: every long shot flown here aims at one rough place and every short one at a
flat place. What 3s established stands — the walk is real and correlates with the miss at rho=+0.707
— but its cause is more likely where it was aimed than how far.

**And the tool said "well conditioned"**, because it scores the *slope* (0.94%, 1.0x flat ground) and
this failure mode is *roughness*. Worth a second line in `shot-report` rather than a footnote here.

### Flown, and it collapses — 2026-09-01

One shot at **-42.0,-179.0**, open Pacific, 12,739 km. Ocean is flat by construction: both paths clamp
to sea level through the same `GroundSurface.Height`.

| | rough target, 12,902 km | **flat ocean, 12,739 km** |
| --- | --- | --- |
| walk from the release probe | **157 m**, quartiles 10 / 326 | **8 m** — 8, 8, 8, 8, 8, 9, 8, 8 |
| miss | 254 m | **22 to 75 m**, one outlier at 584 |

**Twenty times less walk, and dead steady across all eight rockets** where the rough target's swung
by a factor of thirty. The terrain is the cause; the range is not.

**So the long-range accuracy figure was mostly the hillside.** At a flat aim this vehicle lands
around **60 m** at 12,739 km, not the 254 m every night here has reported — and the difference is
where those nights were aimed, because `--aim none` always picks the save's own defended site and
that site is on rough ground.

Two consequences worth carrying:

* **3s's headline is retracted.** "At long range the miss is the predictor" was the confound speaking.
  The walk is real, correlates with the miss at rho=+0.707, and is *terrain* — which the loop cannot
  reach either, so 3s's conclusion about the loop not binding at long range survives; only its cause
  was wrong.
* **Every long-range number in this file is a rough-ground number.** They compare fairly against each
  other, because the aim never moved. They do not describe what this guidance can do.

## 3t. With the floor out of the way the arrival angle is just cot gamma — flown 2026-09-01

12 shots, 96 flights, `~/shots/2026-09-01-1042`, HEAD `6466f9b`. 3h's ladder re-flown with
`DeriveHoldingCost=true` on **every** arm, so the only thing varying is the angle. **This supersedes
3h**, which 3q showed was measured at a 156 m floor no steep arm ever reached a second cycle under.

| arm | arrival | pooled | paired ratio | won | p | |
| --- | --- | --- | --- | --- | --- | --- |
| base | 17.6° | 0.03 km | — | — | — | |
| a25 | 25.9° | 0.02 km | 0.56x [0.32, 0.75] | 10 of 12 | 0.012 | RESOLVED |
| **a32** | **33.0°** | **0.01 km** | **0.44x** [0.19, 0.79] | **12 of 12** | 0.000 | RESOLVED |
| a40 | 41.1° | 0.02 km | 0.43x [0.16, 0.73] | 11 of 12 | 0.006 | RESOLVED |

**All three resolve, and the interior optimum is gone.** 3h had a25 winning and a32 and a40
unresolved and worse; with the floor lowered the ranking is monotone to 33 degrees and flat beyond.

**And it is the textbook number.** Against base's 17.6 degrees:

| arrival | `cot γ` predicts | flew |
| --- | --- | --- |
| 25.9° | 0.65x | 0.56x |
| 33.0° | 0.49x | 0.44x |
| 41.1° | 0.36x | 0.43x |

So once the loop is allowed to converge, the arrival angle does exactly what the geometry says it
should — and 3h's "interior optimum near 26 degrees" was the payback floor, not the trajectory.

### The two levers are not independent, and they compound anyway

The measured holding cost **falls with the arrival angle**: 1.37 m/s at 17.6 degrees against 0.08 to
0.34 at the three steep arms. So a steeper arrival lowers the floor as well as the sensitivity — the
angle acts *partly through* the holding cost, which is why 3h could not separate them.

The effects still stack, because the derivation takes the cost half and `cot γ` delivers the rest:

| | 2,000 km |
| --- | --- |
| shipped: constant 26.0, 17.6 degrees | **110 m** |
| derived cost, 17.6 degrees | **30 m** |
| derived cost, 33 degrees | **10 m** |

**Eleven times better than what ships**, and the terminators say why: a32 ends **24 of 24 on
`noimprov`** and none on `payback`. Every flight runs until the loop stops improving. The floor is
not merely lower, it is gone.

The roster gradient is dead here too — rho = +0.03, p=0.774, every seat at 0.017-0.018 km.

## 3u. At long range the derivation changed nothing the loop controls, and why is unknown

Mined from 3r's 96 flights at 12,902 km, no flying. Asked because 3r's **1.59x** is the only thing
blocking `DeriveHoldingCost` from becoming the default, and 3s showed the loop was never the binding
term at that range.

| arm | threshold | accepted | flown | the gap |
| --- | --- | --- | --- | --- |
| base (26.0) | 546 m | 74 m | 250 m | 176 m |
| derived | **533 m** | **78 m** | 325 m | 247 m |

**Everything the loop controls is the same.** The thresholds agree to 2%, the accepted misses to 5%,
and both arms take three passes at the same point in the coast — 262.6 s after cutoff against 260.8.
The arms differ only in what landed, and that difference lives in the walk, which 3s showed the loop
cannot reach.

**So 3r's 1.59x is not the derivation harming the loop.** It is noise in a term the loop does not
control, and the block on the default is weaker than it looked.

### But the thresholds should not agree, and that is unexplained

`payback` fires at `cycle x cost`. base uses 26.0, so its 546 m implies a **21 s** cycle. The
derivation measured **2.90 m/s** at this geometry, so derived's 533 m implies **184 s** — nine times
longer. Yet both arms show a **20 s wall-clock** gap between passes and correct at the same moment in
the flight, so there is no warp asymmetry to spend the difference on.

Two readings, and the logs cannot separate them:

* the cycle really is nine times longer in **simulated** seconds, and something about the coast
  spends it, or
* **the derived cost is not reaching the rule at long range at all**, and derived was flying the
  constant — which would make 3r a comparison of an arm against itself.

The second would be a bug and would explain 3r entirely.

**Shipped a diagnostic rather than a guess.** The `payback` line now prints its two factors —
`(cycle s x cost m/s)` — not just their product. One short flight then says which, and every log
after it is self-explaining. **The default does not move until it does**: flipping while holding an
unexplained measurement of the thing being flipped is how 26.0 got here.

## 3v. The derivation was reading the hillside, not the arc — found and fixed 2026-09-01

3u shipped a diagnostic printing the payback threshold's two factors instead of their product. One
12,902 km shot read it off, and the answer was neither of the two readings 3u offered:

```
27 m out, under the  517 m another correction would cost (19.9 s x  26.00 m/s)
28 m out, under the   95 m another correction would cost ( 6.0 s x  15.78 m/s)
309 m out, under the  674 m another correction would cost ( 6.0 s x 112.40 m/s)
813 m out, under the  900 m another correction would cost ( 6.0 s x 150.05 m/s)
1102 m out, under the 1169 m another correction would cost ( 6.0 s x 194.82 m/s)
```

**The measurement was returning up to 194.82 m/s** — against a true value near 3 — which sets a
1,169 m threshold and releases the correction 1,102 m out. So 3r's derived arm was not neutral at
long range; it was being fed nonsense on most passes, and the one line reading `26.00` is the
constant standing in where a probe was refused.

### The cause is the terrain, and it is the same confound as 3s

`TryMeasure` differenced two impact predictions **flown against the real height field**. The two
probes land on different relief, so their difference carries the ground's roughness rather than the
decay. Measured across baselines on a target as rough as 12,902 km's:

| baseline | on the reference sphere | on rough ground |
| --- | --- | --- |
| 1 s | 3.62 m/s of spread | 41.56 |
| 10 s | 1.08 | 36.68 |
| 106 s | 1.02 | 28.59 |
| 300 s | 0.69 | 11.70 |

**A longer baseline does not fix it** — the noise is in each probe, not in the interval. On the
reference sphere the same probes hold to about **1 m/s at every baseline**.

### The fix is to stop asking about the ground

The holding cost is a property of the **arc** — how fast the release impulse's leverage decays — and
not of the hillside under the aim. The hillside decides where a round stops; it has no business in
the decay. `HoldingCost.TryMeasure` no longer takes a terrain callback at all, and the baseline is
106 s, which is both steadier and what the shipped constant was originally taken over.

Measured down one coast at 8,000 km, the probe now wanders by **0.65 m/s**, against the 12 to 42 it
showed sampling terrain. `HoldingCostTests.TheMeasurementDoesNotDependOnTheGroundUnderTheAim` holds
it.

**Unflown.** 3n's 2,000 km result stands — that target is flat, so the terrain was doing nothing
there and the numbers it produced were already the arc's. What has to be re-flown is **3r**, whose
verdict was measured against an arm reading a hillside.

## 3w. The derivation is the default — flown 2026-09-01

3v's fix re-flown at 12,902 km, 12 shots, 96 flights, `~/shots/2026-09-01-1445`.

| arm | pooled | paired ratio | won | p | |
| --- | --- | --- | --- | --- | --- |
| base (26.0) | 0.38 km | — | — | — | |
| derived | **0.28 km** | **0.86x** [0.39, 1.15] | 7 of 12 | 0.774 | unresolved |

**Unresolved, and pointing the right way** — against 3r's 1.59x pointing the wrong way. 3r was the
bug: its two arms both sat at a ~540 m threshold, so it compared an arm against itself. Here the
derived floor is **46 m** against base's ~500, and the terminators follow: derived runs to
`noimprov` on 18 flights against base's 7.

Running further does not help much at this range, which 3s already explained — the walk, not the
loop, is what limits an intercontinental shot. So the honest reading is that the derivation is a
**large resolved win where the loop binds and neutral where it does not**.

### So `DeriveHoldingCost` ships on

| | 2,000 km | 12,902 km |
| --- | --- | --- |
| flights | 96 | 96 |
| ratio | **0.28x** [0.24, 0.33] | 0.86x [0.39, 1.15] |
| verdict | **RESOLVED**, 12 of 12, p&lt;0.001 | unresolved |

**110 m to 30 m at 2,000 km for anyone who installs it**, and the argument that closes it is not the
ratio: **no constant is right at either geometry.** 26.0 is nine to twenty times the measured decay
at both, so keeping it means shipping a number known to be wrong everywhere it has been checked, to
avoid a change that is unresolved at one range and resolved at the other.

`HoldingCostMetresPerSecond` stays at zero and stays the override, and a probe that cannot be flown
still falls back to the constant — so a geometry where the measurement fails behaves exactly as it
does today.

## 3x. The cutoff residual is not what the miss is made of — measured 2026-09-01

Item 4 of the plan, run headlessly against the two flown geometries and then against the two nights
themselves. `MissSensitivityTests` reconstructs each arc from three numbers its own flight logs —
cutoff altitude, downrange distance and flight time — and perturbs the cutoff velocity along three
axes.

| | the arc's own sensitivity | the night realises | |
| --- | --- | --- | --- |
| 2,000 km | 772-1,095 m per m/s, median **884** | **36** [97%: 18, 87] | resolved, 8 of 8 crafts positive |
| 12,902 km | **11,636** m per m/s | -115 [97%: -939, 519] | unresolved, 4 of 8 positive |

The flown figure is a **within-craft** least squares of cutoff residual against the flight's mean
miss, bootstrapped over crafts and flights. Within-craft because the eight rockets of a world fly
different arcs at different aim points, and a pooled fit reads that apart as a relationship: pooled
gives rho +0.53 at 2,000 km, and the within-craft pooling gives +0.51, so here the two agree and the
correlation is real. At 12,902 km pooled gives -0.15 and within-craft **-0.02** — nothing.

**So the trim and the aim loop absorb 96% of what the engines leave.** The median 0.26 m/s residual
explains **9 m of a 17 m median miss** at 2,000 km, and at 12,902 km the median 0.14 m/s explains
none of a 301 m one. That is the loop working, not a null result: `dMiss/dV` at cutoff is what the
miss would be if nothing corrected it.

### Item 9 was ranked on the wrong number

"Hand the terminal fraction of the burn to `FlightComputer.Burn`" removes the frame quantum outright,
which is the whole of the cutoff residual. Priced against the arc it is worth 884 m per m/s; priced
against what the nights realise it is worth **36**, so abolishing the residual entirely buys about
**9 m at 2,000 km and nothing measurable at 12,902**. It was ranked ninth on days of work for a term
the loop has already removed. It drops.

The same arithmetic protects the throttle ramp and `HoldDirectionFrames`, which are already shipped
and cost nothing to keep — but nothing further should be spent on the residual.

### The eight rockets of a 2,000 km world do not fly one arc

| cutoff | flight time | reconstructs to |
| --- | --- | --- |
| 117 km | 358 s | 11.1 deg |
| 142 km | 425 s | 17.6 deg |
| 160 km | 486 s | 23.6 deg |
| 181 km | 563 s | 30.8 deg |

Two rockets on each. At 12,902 km all eight agree — 157 km and 1,881 to 1,899 s. So a 2,000 km night
carries a **within-world spread of arrival angle** that nothing has ever accounted for, and by
`cot(gamma)` the shallowest of those four is half again as sensitive as the steepest. A paired
night's variance includes it; a paired night's *comparison* does not, because both arms fly the same
four.

The reconstruction is soft: 200 km of assumed boost travel moves the arrival by about three degrees,
and where the boost ended is not logged. So the four angles are ordered reliably and pinned to about
that.

### And the long arc reconstructs to 7.1 degrees, which nothing can check

Every doc that prices the seven-degree arrival was written before the flown geometry was measured,
and the stale-lines list below says the flown one is 13.6-17.5. **Both may be right**: 17.7 is the
2,000 km baseline, measured off 3h's floored arms; the 12,902 km baseline's arrival has never been
recorded at all, because `IcbmComputer` only printed an arrival angle when a floor was asked for and
could not be met.

That is now fixed, and it is item 3 of the plan.

### The diagnostic: one line per flight, unconditionally

`release summary on <craft>` at INFO, written once at the first release — the instant everything the
correction loop will ever do is over. It carries the cutoff residual, what the trim owed at the split
and still owed **on release**, the arc's own arrival angle, and the aim loop's response, raw
response, plant readings and `worse for` count.

None of those survived a baseline flight before. The response and the plant readings were `DEBUG`
lines among hundreds of per-cycle ones; the release residual only appeared when the trim changed what
it was doing; the arrival angle only when a floor was refused. `tools/shot-report.py` reads the line
per craft and prints it as **what the correction loop left**, so the next night scores on it.

## 3y. The affordable arrival is 67 degrees, and asking for all of it breaks the trim — probed 2026-09-01

One block at 2,000 km, `base|p100:ArrivalPreference=1.0`, `~/shots/2026-09-01-2130`. Flown to pick
the ladder rather than to settle anything: `ArrivalPreference` multiplies the steepest affordable
arrival, and nothing had ever recorded what that number is.

**It is 66-78 degrees**, not the 25-45 the flown `MinArrivalAngleDeg` ladder had made it look. So the
fractions map far steeper than a25/a32/a40 ever went, and a ladder picked on the old assumption would
have put three of its four arms on the baseline.

| rocket | affordable | floor | flew | miss |
| --- | --- | --- | --- | --- |
| FAT 3 | 66.2 deg | 66.2 | 67.3 | 14 m |
| FAT 5 | 66.6 | 66.6 | 67.6 | 18 m |
| FAT 7 | 66.9 | 66.9 | 67.9 | 15 m |
| **FAT** | **77.8** | **77.8** | **78.5** | **5,291 m** |

Base's four flew 16.8-17.1 degrees for 10-63 m.

### The one that could afford the most is the one that failed

`trim owed 2.26 m/s at the split and 3.11 m/s on release (0.83 m/s spent, GAVE UP)` — the trim ran
out and the shot ended on the `trim` terminator, which no other flight that night reached.

The mechanism is in the split of labour and not in the angle. `ArrivalBudget.SteepestAffordableDeg`
prices what the **ascent** can pay for; what a steep arrival then costs the **post-boost trim** is a
different account it says nothing about. So the rocket with the most margin asked for the steepest
arrival, spent the margin getting there, and had nothing left to correct with — 3.11 m/s owed against
0.83 spent. `IcbmConfig.ArrivalPreference`'s own doc comment says a fraction near one leaves no
margin; this is what that looks like.

**Scored the way the harness scores, on the worst warhead of a group, p100 loses outright**: mean
1.33 km against base's 0.039. Three flights of four at 3x better than base is not worth one at 135x
worse, and a mean is the wrong statistic for a distribution with that shape.

### So the ladder is 0.5, 0.65, 0.8

Floors of about 33, 43 and 53 degrees against the affordable 67 — bracketing where the trim starts
running out, and reaching past the 40 degrees that is the steepest anything has flown. Flying
2026-09-01-2148, 12 blocks.

## 3z. The long-range miss is the predictor undersampling the terrain — read out 2026-09-01, **REFUTED headlessly 2026-09-02, see 3ab**

3v established that the walk is the ground and not the range: 157 m of walk and 254 m of miss over
rough relief against **8 m and 22-75 m** on flat ocean at the same range, same code. What it did not
say is *why the correction loop cannot remove it*, since the loop's prediction already flies against
the real height field. Three candidates were read out of the source. **One is the cause; both
refutations found live faults anyway.**

| candidate | verdict |
| --- | --- |
| the prediction and the round sample in different frames (`Ccf` vs `Cce`) | **refuted** — both reduce to `dirCci.Transform(cci2Ccf)`; disagreement 0 m |
| `accurate: true` silently degrading to a modifier-free field | **refuted as a cause here** — never null for a stock body once loading finishes |
| **the predictor samples terrain 826 m apart** | **confirmed** |

### The terminal step is sized for drag, not for ground

`ImpactPredictor` refines its step on **air density** and on nothing else —
`h = Math.Min(h, inAir)` at `ImpactPredictor.cs:133`, one-way and altitude-blind. So the terminal
step is a flat `AtmosphericStepSeconds` of 0.25 s whatever the clearance is.

| arrival | impact speed | ground between terrain samples |
| --- | --- | --- |
| 7.1 deg | 3,330 m/s | **826 m** |
| 16.4 deg | 4,410 m/s | 1,054 m |

About **117 lookups cover the whole final 96 km** of ground track at the shallow arrival.

Against that, `docs/KSA-TERRAIN.md`'s own account of the height field: erosion runs to a **166 m**
wavelength and the tiling detail to 7.4-20.5 m per texel. The predictor's Nyquist is 1,652 m, so
**four of the seven erosion octaves are entirely below it**, carrying about 117 m of aliased
amplitude. Each octave's slope reaches 0.30 against the arc's `tan 7.1 deg` of 0.125 — **terrain can
climb 2.4 times faster than the arc descends**, so the clearance function is genuinely non-monotone
and the below-ground test only finds crossings a sample happens to bracket.

At `cot 7.1 deg` = 8.03 m of ground per metre of height, a 20 m unresolved hump is 160 m of ground
and a 37 m one is 300 m. The flown walk is 157 m and the median miss 301 m. **The magnitudes match
with nothing fitted.**

### It also explains the `noimprov` ending, which nothing else did

`AimCorrection.ResponseFromMetres` is 500 m — the plant response is estimated from aim moves of at
least that, against an 826 m sample grid. Every move lands the sample points on an uncorrelated part
of an aliased field, so the finite difference measures sampling noise rather than plant. **An aliased
observer makes the predicted impact a discontinuous, non-monotone function of the aim**, which is
exactly the condition under which a gradient loop cannot find a better one and gives up.

Same shape as the drag blind spot, for the third time: *a correction loop can only remove what its
observer can see.*

### And the test that ruled this out was itself blind

`PredictorStepTests.TheShippedAirStepIsAlreadyConverged` records the negative — *"the tempting fix is
now ruled out and should stay ruled out"*. Its helper calls
`TryPredict(..., out hit, drag: new ...)`, and the **named `drag:` argument skips
`terrainRadiusAt`**, which defaults to null; `ImpactPredictor.SurfaceUnder` then returns
`body.SurfaceRadius`. So the convergence was established **against a perfect sphere** — the
flat-ocean case that already flies clean at 8 m.

The headless rough ground cannot see it either. `DeorbitShot.RoughGround`'s three terms have
wavelengths of 3,336 km, 308 km and 19.1 km for a total slope near 0.018, seven times shallower than
the arc and monotone-crossing by construction. It reproduces the 800 m of height spread and none of
the roughness that matters.

**This is the first time the blind observer was a test rather than the code**, and it is the reason
the item sat on the refuted list.

### What to do, cheapest first

1. **Confirm it headlessly, before changing anything.** Re-run the convergence test with
   `terrainRadiusAt` actually passed, and add a `RoughGround` variant carrying a 300 m wavelength at
   40 m of amplitude — slope 0.84, which is what erosion actually does. If the shipped 0.25 s step
   then moves the impact by hundreds of metres against a fine reference while the sphere case stays
   sub-metre, it is settled without flying.
2. **Gate the step on clearance rather than on density.** Once inside about 2 km of the ground, size
   `h` so the horizontal advance is 100-150 m: never step further than you can fall. About +120
   lookups per prediction, and the crossing branch already evaluates `SurfaceUnder` **twice at the
   same point** (`ImpactPredictor.cs:140` and `:149`) — caching that gives much of it back. The coast
   is untouched.
3. **Then re-fly the rough-ground long shot.** It is the one geometry where this should be worth
   hundreds of metres.

### Three stale lines this closed, all now corrected

* `CLAUDE.md`: *"The same trap reaches `TerrainRadiusAt`, which samples the height field in the wrong
  orientation"* — true when written in `2119f16`, fixed by `5643caa` two commits later. It pointed
  the whole frame investigation at a closed bug.
* `docs/KSA-TERRAIN.md`: *"`ImpactPredictor` re-samples every integration step, so the prediction sees
  the terrain more finely than the round does."* **Backwards.** The round samples once a frame, about
  55 m of ground track; the predictor every 826 m. It is 15 times coarser.
* `tests/KSArmory.Tests/DeorbitShot.cs`: *"`IcbmComputer`'s `TerrainRadiusAt` does not [clamp to the
  sea]"*. It does, through `SurfaceHeight`.

### The refutation that found something else: the terrain mask has no bound

`Celestial.UpdateApproxTerrainAltitudes()` runs from the **constructor**, and
`Universe.SetupRenderData()` — which populates `TerrainModifiersRenderData` — runs 79 lines later in
`Program`. The modifier loop is bounded by `?.NumModifiers`, so a null runs it zero times with no log
line. **`MaxTerrainHeightApprox` is therefore a modifier-free maximum**, missing Earth's declared
1000 m of erosion, 1500 m of dunes and detail out to 1900 m.

`KsaWorld.cs:374` hands that number to `TerrainMask.Blocked` as the sphere containing all terrain,
and CLAUDE.md justifies the whole cheap-before-exact ordering on *"a sphere containing the terrain
cannot produce a false negative"*. **It is not a sphere containing the terrain.** Nothing about the
ballistic shot, and a real false-negative source in the radar horizon mask.

Calling `SetupModifierRenderData()` does not fix it — `UpdateApproxTerrainAltitudes` is private, has
no public re-run, and the render data is already populated by the time any mod code runs. The fix is
mod-side: pad the bound by the modifier amplitude budget, or stop using that number.

## 3aa. Half the affordable arrival is the setting; four fifths of it is a resolved loss — flown 2026-09-01

12 shots, 96 flights, `base|p50|p65|p80` at 2,000 km, `~/shots/2026-09-01-2148`. Frame time 22.4 ms,
5 correction passes at the median shot.

| arm | floor | flew | owed on release | miss median | worst |
| --- | --- | --- | --- | --- | --- |
| base | — | 16.9 deg | 2.63 m/s | 29.5 m | 269 m |
| **p50** | 33.3 | 34.2 | 2.56 | **13.5 m** | 183 m |
| p65 | 43.3 | 44.4 | 2.60 | 17.0 m | 234 m |
| p80 | 53.2 | 54.4 | **4.19** | 132 m | **16,883 m** |

| arm | ratio | interval | sign p | rank p | verdict |
| --- | --- | --- | --- | --- | --- |
| p50 | **0.48x** | [0.26, 1.12] | 0.146 | **0.021** | **WIN** |
| p65 | 0.59x | [0.32, 5.60] | 0.388 | 0.850 | unresolved, open |
| p80 | **5.55x** | [3.51, 70.02] | 0.006 | 0.001 | **LOSS** |

**p50 clears the protocol's bar** — rank p=0.021 against ALPHA 0.0294, ratio below one — and it is
worth stating that the distribution-free interval still reaches 1.12. At n=12 that interval's
coverage is discrete and conservative, so it is wider than the exact test; the two are not in
conflict, but the honest summary is *a win by the stated rule with an interval that admits no
effect*. A second night at 25 an arm would settle it.

**p80 is a settled loss** and needs no hedging: 1 of 12, both tests, interval entirely above one, and
a worst shot 309 times the baseline.

### The mechanism is visible, and it is the trim rather than the arc

`owed on release` runs **2.63 / 2.56 / 2.60 / 4.19** m/s. Flat to 44 degrees, then a jump at 54. And
p80 is the only arm producing the `trim` terminator — **3 of 24 flights, median 14.06 km** — where
base, p50 and p65 produce none at all.

That is 3y's single failed rocket reproduced at n=24, and it says
`ArrivalBudget.SteepestAffordableDeg` is answering the wrong question: it prices what the **ascent**
can pay for, and the affordable angle is ~66.6 degrees on every arm while what actually binds is
somewhere between 44 and 54.

**What binds is not the trim's budget, which is what this section assumed** — 3ag prices that
headlessly and the authority *grows* with the angle. It is the last floor for which a long transfer
still exists from where the burn leaves the vehicle.

**It is only visible because the release summary shipped the day before.** Without it p80 is a
mysterious 5.55x with no mechanism attached, and the natural next move would have been another night
at another fraction rather than a look at the trim.

### The terminator table, which says the same thing from the other side

| arm | noimprov | payback | clock | trim |
| --- | --- | --- | --- | --- |
| base | 13 | 9 | 2 | 0 |
| p50 | **24** | 0 | 0 | 0 |
| p65 | **24** | 0 | 0 | 0 |
| p80 | 18 | 0 | 3 | **3** |

A steeper arrival moves every flight onto `noimprov` — the loop runs to exhaustion instead of being
cut off by the payback rule, which is what a smaller miss looks like from inside. p80 breaks that and
is the only arm that does.

### So the shipped default should be `ArrivalPreference = 0.5`

Not flown as a default yet, and that is the gate: this night compared it against zero **as an arm**,
which is the same build and the same world. What has not been flown is 0.5 at the long geometry,
where the arrival is 7 degrees and `cot(gamma)` says the lever is worth far more.

**Do not go past 0.65.** The night rules out 0.8 outright and 0.65 is already bimodal — per-shot
ratios of 9.61 and 6.82 beside 0.13.

### Two tool faults this night exposed, both fixed

* **The verdict label took `min(sign, rank)` against 0.05**, where the interval beside it is built at
  `ALPHA = 0.0294`. Two chances at a looser threshold. It now reads the rank test at `ALPHA`, which
  is what the code's own comment already said to do. No past verdict in `MIRV-NEXT.md` changes sign
  under it, but an arm at sign 0.04 and rank 0.20 would have read `RESOLVED`.
* **`what the correction loop left` printed one merged row in paired mode**, keyed on the batch's arm
  column — which in a paired night is one value for the whole world. It is now keyed on `within`,
  which is per flight, and split by arm. The table above is that fix's first output.

## 3ab. The predictor does not alias KSA's terrain, and the criterion 3z used was the wrong one — headless 2026-09-02

3z asked for exactly this before anything was changed: *"Confirm it headlessly, before changing
anything."* Confirmed is not what happened.

`PredictorStepTests` now passes `terrainRadiusAt` — the omission that made the original negative
blind — and scores the shipped 250 ms step against a 2 ms one **on the same surface**, so what it
measures is the step rather than the ground.

| surface | shipped step vs a 2 ms one |
| --- | --- |
| mean sphere (the old, blind negative) | **0.00 m** |
| `RoughGround`, the existing fixture | 0.4 m |
| one octave, 40 m over 300 m — 3z's own suggestion, slope 0.84 | **0.0 m** |
| **KSA's erosion spectrum, all seven octaves, undamped** | **0.13 m** |

The sampling is exactly what 3z read: **781.9 m** between terrain lookups at the 7 degree arrival
against a predicted 826, and 9.1 m at the reference step. The fixture is genuinely rough — 94.6 m of
swing across 3 km of track. Both land on the same point.

### Slope is not the criterion; amplitude against the arc's drop per sample is

3z's argument was that each erosion octave carries a slope up to 0.30 against the arc's `tan 7.1` of
0.125, so *"terrain can climb 2.4 times faster than the arc descends"*. Swept, slope turns out to
carry no signal at all:

| octave | slope | cost |
| --- | --- | --- |
| 100 m over 800 m | 0.79 | **576.3 m** |
| 40 m over 300 m | 0.84 | **0.0 m** |
| 250 m over 1,600 m | 0.98 | 0.3 m |
| 600 m over 3,200 m | 1.18 | 0.0 m |

Four slopes within 50% of each other spanning nothing to 576 m. **What decides it is whether a
feature can hide between two samples and still be tall enough to matter**, which needs both:

* **amplitude above the arc's drop across one sample interval** — about 101 m at this arrival, since
  the arc descends `tan 7 deg` over 782 m; and
* **wavelength below twice the sample spacing**, about 1.56 km, or the feature is resolved anyway.

A short octave is steep locally and returns to its own mean two or three times within one step, so
it is never stepped over. That is why 40 m over 300 m — steeper than the case that costs 576 m —
costs nothing.

### KSA has no octave in that corner, and it is not close

`EarthErosion` is seven octaves, lacunarity 2, gain 0.5, from 10.6 km at 500 m of amplitude down to
166 m at 7.8 m (`docs/KSA-TERRAIN.md`). Sorting them against the two conditions:

| octave | wavelength | amplitude | under 1.56 km? | over 101 m? |
| --- | --- | --- | --- | --- |
| 2 | 2,655 m | 125 m | no | yes |
| 3 | **1,327 m** | **62.5 m** | yes | **no** |
| 4 | 664 m | 31.2 m | yes | no |
| 5 | 332 m | 15.6 m | yes | no |

Every octave short enough to alias is far too small, and the only one tall enough is resolved. 3z
summed the sub-Nyquist amplitudes to 117 m and compared *that* to the threshold, but they sit at
four different wavelengths and phases and do not stack into one feature — which is why the spectrum
tested whole costs 0.13 m rather than the hundreds of metres the sum would suggest.

**The bisection is the reason the mechanism is so hard to trigger**, and 3z did not account for it.
`ImpactPredictor` does not accept the first sample below ground: it halves the step and retries from
the *previous* state until the answer is within `CrossingToleranceMetres`, so a coarse step that
overshoots into a hillside still resolves the first crossing to a quarter of a metre. The only
unrecoverable case is an arc that clears a peak entirely and comes down beyond it, which is what the
two conditions above describe.

### So item 1b is dropped, and the long-range miss is unexplained again

Gating the step on clearance would buy **0.13 m** at best and cost about 120 lookups per prediction.
Not worth building.

What this does not do is explain the 301 m median at 12,902 km, or 3v's finding that rough ground
costs 157 m of walk against 8 m on flat ocean. **That correlation stands and its mechanism is now
open** — it is the ground, and it is not the predictor's step through it. The `noimprov` ending 3z
attributed to an aliased observer needs another explanation too.

The next candidate is the one 3z displaced rather than closed: the *round's* own arrival, not the
prediction of it. `ProbeGapTests` prices the round's integrator on flat ground; nothing has priced
it over relief, and unlike the predictor the round has no bisection — `ContactSweep` and the ground
test run once a frame at about 55 m of ground track, with whatever the frame happened to be.

**Three legs, and all three are load-bearing.** The sphere leg says the rig is sound; KSA's spectrum
is the finding; and the 100 m over 800 m leg is what stops this being a second blind negative. 3z
exists because the test before it was established against a surface with nothing to miss, so a null
here would be worth nothing unless the same rig demonstrably still sees a real effect. It does:
576.3 m.

## 3ac. The round and its probe strike different hills, and terrain multiplies 30 m into 5 km — headless 2026-09-02

3ab exonerated the predictor and left 3v's finding — 157 m of walk over rough ground against 8 m on
flat ocean — without a mechanism. This is the other integrator, measured the same way.

`ProbeGapTests` already flew "with relief"; what it lacked was relief with **features**.
`RoughGround`'s shortest term is 19 km across, so it carries height and nothing a round can be
caught out by. `DeorbitShot.ErodedGroundKsaSpectrum` puts KSA's own seven erosion octaves on it and
is faithful to what the game declares: per-octave slope **0.296** against `KSA-TERRAIN.md`'s "up to
0.30", amplitudes 500 m down to 7.8 m, wavelengths 10.6 km down to 166 m, 992 m in total against a
declared 1000.

| surface | the round, against its own probe |
| --- | --- |
| mean sphere | −29 m |
| `RoughGround` | −30 m |
| **KSA's erosion spectrum** | **−5,143 m** |

### It is not integration, and it is not chaos

**Not integration**: −5,281 m at a 25 ms frame, −5,282 at 50 ms, −5,233 at 130 ms. Flat across a
fivefold change in step, where an integration error is first order in it.

**Not chaos**: nudging the release by ±6 cm/s — far below anything guidance controls — moves the gap
by **11 m** on 5,280. So the round and the probe are not stopping on different features at random;
they disagree the same way every time, which is a bias and therefore in principle removable.

**And not the four terms the file already prices.** Removed one at a time they sum to −215 m; removed
together they are worth **+2,648 m**, leaving **−2,496 m unaccounted**. On the two smooth surfaces
the same decomposition closes to within 8 m. Strong non-additivity with a large residual is the
signature of a term nobody has named, not of the named ones interacting.

### Why this is the shape that matters

`AimCorrection`'s only observer is `ImpactPredictor`. 3ab says the predictor reads terrain correctly
to a tenth of a metre. So a stable disagreement between where the round stops and where the
predictor says it stops is **exactly what the loop cannot remove**: it converges the prediction onto
the target and the round lands the bias away from it.

Third time in this file, after the drag blind spot and the back-dated observer: *a correction loop
can only remove what its observer can see.*

### The cause: neither side is wrong, and the terrain multiplies the difference by 170

Asked directly which of them stops where the ground is not, the answer is **neither**:

| | stopped over terrain of | its own error against the surface there |
| --- | --- | --- |
| the round | **+764.8 m** | 1.8 m |
| the probe | **-1.5 m** | 0.3 m |

Both stop correctly. They stop on **different features** — the round clips a hill, the probe clears
it and runs on into a valley 5.1 km further downrange. A 766 m difference in the height struck, at
`cot 7 deg` of 8.14, is 6.2 km of ground against the 5.1 km measured.

So there is no misreading to fix. The round and its probe fly trajectories that differ by about
**30 m** — that is the whole gap on smooth ground, and 23 m of it is symplectic Euler. Over
non-monotone terrain that 30 m decides *which feature is struck first*, and the answer changes by
kilometres. **A gain of roughly 170.**

This reconciles the two facts that looked contradictory. It is stable under a 6 cm/s nudge because
a hill is either clipped or not and centimetres do not change that; and it is wildly non-additive
under the decomposition because removing any one term can flip the choice. Both are threshold
behaviour, not error accumulation.

**It also explains `MIRV-NEXT` item -1** — seven headless improvements that scored well on smooth
ground and lost in flight. Smooth ground shows the 30 m honestly and hides the multiplier entirely.

### What follows, and what is not established

The lever is not accuracy in either integrator separately: it is **agreement** between them, since
the loop steers the round using the probe's answer. Closing the 30 m closes the flip probability
with it. That is the opposite of the usual framing, where the round's own integration error is
priced against a converged reference.

**The magnitude is a worst case.** These octaves are undamped, and in the game each is scaled by the
biome weight, a gradient-falloff power and `1 - |dot|` — so real ground is some fraction of this,
and the flip is correspondingly rarer. The flown median at 12,902 km is 301 m, not 5 km, and 3v's
rough-vs-flat contrast is 157 m against 8. Direction and ordering match; the scale factor between
this fixture and flown ground is unmeasured, which is item 1f.

**And this is one geometry.** Whether a flip happens at all depends on there being a hill at the
crossing; the 170 is this shot's gain, not a constant.

## 3ad. Converging the round does not close the terrain gap — headless 2026-09-02

3ac's conclusion was that the lever is *agreement* between the round and its probe rather than
either one's accuracy, and that closing their 30 m trajectory difference should close the flip with
it. Flown headlessly against the sub-step, it does not.

| the round's sub-step | mean sphere | `RoughGround` | **KSA erosion** |
| --- | --- | --- | --- |
| as shipped | -29 m | -30 m | **-5,282 m** |
| 1.00 ms | -29 m | -30 m | -5,282 m |
| 0.50 ms | -14 m | -14 m | -5,271 m |
| **0.25 ms (converged)** | **-6 m** | **-7 m** | **-5,263 m** |

On smooth ground the sub-step is the whole story and converging it removes four fifths of the gap,
which is what `GravityIsAlreadyShippedAndTheSubStepIsTheWholeOfWhatIsLeft` has always pinned. Over
erosion the same change is worth **19 m of 5,282**, and the round strikes the same hill throughout —
764.8 m as shipped, 759.4 m converged.

**So the flip is not decided by the round's integration**, and 1g is refuted before it was built.
This matters beyond the item: it means the round's own integration error, which is what
`KINETIC-FLOOR.md` and most of `ProbeGapTests` price, is not the term that survives contact with
terrain.

### What is still open, stated precisely

The four differences `ProbeGapTests` already prices — ground held for a frame, air held for a frame,
symplectic Euler, per-sub-step gravity — remove **2,648 m** of the 5,143 when taken together and
leave **2,495 m**. Converging the sub-step further does not touch it. So there is a difference
between the round's stopping rule and the predictor's that none of them names.

The candidate the code suggests, unverified: the round's ground test answers with a **sphere** —
one centre and one radius — which `ContactSweep` then tests the whole step against, where
`ImpactPredictor` tests only its step's endpoint radius and bisects. A swept test against a sphere
sized on a hilltop stops on that hilltop for the rest of the step, wherever the round has since
moved. That is a difference in *kind* rather than resolution, which fits a residual that does not
shrink with the step.

**Not established, and worth stating**: 3ac's clearance instrument was withdrawn. It reconstructed
each path point's time by interpolating linearly over the point index, and `ImpactPredictor`'s steps
are anything but uniform — seconds while coasting, 2 ms in air, halving again through the crossing —
so the body-fixed un-carry it did was wrong for every point but the last. Whether the probe's own
path ever passes under the ground before it lands is therefore **unmeasured**, not answered.

## 3ae. The terrain gap has a floor refinement cannot reach, and the fixture is undamped — headless 2026-09-02

3ad left 2,495 m that the four priced differences do not remove. Two more things are now measured
about it, and together they say to stop spending here until the game is asked a question.

### The predictor is exact, so the round is the one stopping early

Refined to **0.46 m of ground track** — 0.1 ms, against a shortest erosion octave of 166 m — the
predictor's impact does not move at all:

| its step | ground track per sample | from the shipped 250 ms answer |
| --- | --- | --- |
| 50 ms | 232 m | 0.0 m |
| 2 ms | 9.3 m | 0.1 m |
| **0.1 ms** | **0.46 m** | **0.0 m** |

It is not missing features at any resolution. So the round is stopping on something the predictor
correctly clears, and the disagreement is the round's.

### The cheapest round that closes it is 10x the shipped cost, and closes half

| the round's sub-step | ground held for the frame | ground re-sampled per slice |
| --- | --- | --- |
| shipped | -5,282 m | -5,280 m |
| 2.50 ms | -5,267 m | -5,303 m |
| 1.00 ms | -5,282 m | -5,280 m |
| **0.50 ms** | -5,271 m | **-2,500 m** |
| 0.25 ms | -5,263 m | -2,495 m |

Neither lever does anything alone at any setting; together they need a **0.5 ms sub-step**, ten times
the shipped 5 ms, and they halve the gap rather than closing it. **2.5 km survives every refinement
tried.**

### Why a floor is the expected shape

The round and its probe land 30 m apart on smooth ground, 14 m apart with a 0.5 ms sub-step. Over
this terrain a graze is decided by **metres of clearance**, so a divergence of 14 m still flips which
feature is struck, and the flip is worth kilometres whatever produced the 14 m. Accuracy in either
integrator does not converge the *pair* fast enough to stop flipping — which is the same conclusion
3ad reached from the other side, now with the curve behind it.

**So over sufficiently rough ground the miss is bounded below by grazing sensitivity rather than by
guidance**, and that is a term `KINETIC-FLOOR.md` does not carry.

### And "sufficiently rough" is exactly what is unmeasured

`ErodedGroundKsaSpectrum` is faithful to KSA's declared spectrum and **undamped**. The game scales
every octave by the biome weight, a gradient-falloff power of the angle between texture and surface
normals, and `1 - |dot|` of the same pair — and `KSA-TERRAIN.md` says of that product, in as many
words, **"The product is unmeasured here; only the geometry is"**, adding that it is near zero over
flat ground.

Against flown evidence the fixture is far too rough: 3v measured 157 m of walk over rough ground and
8 m on flat ocean, where this fixture gives 5,143 m. Roughly **thirty times** overstated, and the
relationship is threshold-driven rather than proportional, so it cannot simply be scaled.

**Nothing here justifies a code change yet.** Ten times the sub-step cost for half a gap, on a
fixture thirty times too rough, is not a trade anything has earned. The gating measurement is the
damping product, and it needs the game rather than the rig: sample the real height field along a
flown reentry track and read the amplitude that actually survives below a kilometre of wavelength.

### One stale line this closed

`Sim/IGroundTest.cs` justified holding the ground sphere for a whole frame on the round covering
"the few metres of ground track a falling round covers in one frame". `Sim/Slug.cs` says, forty
lines from the call that does it, that "a re-entering round covers a kilometre a frame at ordinary
speeds and more under warp" — which is why the air is re-read per sub-step and the ground is not.
Both cannot be true. Corrected, because it closes off exactly this investigation for the next
reader.

## 3af. The terrain mechanism is real and KSA's ground never triggers it — flown 2026-09-02

3ab through 3ae built a mechanism headlessly and left one number unmeasured, which
`KSA-TERRAIN.md` had flagged as unmeasured too: what fraction of the declared erosion spectrum
survives the biome weight and the two angle terms. `IcbmComputer` now samples the **real height
field** once per flight, beside the release summary — 201 taps at 25 m along 5 km of the approach
through the aim, high-passed with a one-kilometre boxcar.

```
ground under the aim on GeoSat FAT_1: 201 samples over 5.0 km of the approach,
  swing 18.8 m, below a 1 km wavelength 3.6 m peak-to-peak and 0.6 m rms
```

| | the undamped fixture | flown ground |
| --- | --- | --- |
| swing across a few km | 94.6 m | **18.8 m** |
| amplitude below a 1 km wavelength | 62.5 m in the largest octave alone | **3.6 m peak-to-peak, 0.6 m rms** |

3ae's sweep put the threshold for a feature flip at an amplitude **above the arc's drop across one
sample interval, about 100 m**. Flown ground carries 3.6 m in that band — **a factor of 28 below the
level at which the mechanism does anything at all**, and the fixture overstates it by roughly a
hundred rather than the thirty 3ae guessed from 3v.

**So the whole line 3ab-3ae describes is real, correct, and never fires.** The round and its probe
do strike different hills over ground rough enough, and KSA's is not. Nothing in 3ad or 3ae should
be built: not the 0.5 ms sub-step, not the per-slice ground sample, not a clearance-gated predictor
step. `ProbeGapTests`' erosion column stays as the bound it establishes, not as a target.

**And the flight agrees.** The same shot landed 6 of 6 within **17 m**, worst to best 0.017 to
0.016 km, on the geometry 3v flagged as rough.

**One flight settles nothing about a median, and this pair says so.** The identical save, aim and
code landed **0.624 km** two hours earlier and **0.017 km** here — a factor of **37** with nothing
changed but the diagnostic, which only reads. `SHOT-PROTOCOL.md` documents run-to-run scatter at 1.7x
either side of a median; this is twenty times that, and it is the bimodality section 1 describes
rather than noise. Neither number is the shot's accuracy. **Nothing above should be read as a
measurement of where this geometry lands** — 3af measures the *ground*, which is a property of the
place and needs one flight, not the miss, which needs a night.

### The arrival is 12.9 degrees, and that closes stale line 4

The same release summary carries the number the plan has wanted since 3x:

```
release summary: cut off 0.343 m/s short, ... arriving at 12.9 deg,
  aim response 1.00 (raw 0.97) off 5 plant reading(s), bias 0.7 km, best 0.03 km, worse for 0
```

**12.9 degrees, not the 7.1 that 3x reconstructed** and not the seven asserted in
`ARRIVAL-ANGLE.md`, `KINETIC-FLOOR.md`, `METRE-LEVEL.md` and `IcbmConfig.cs`. `cot` of it is
**4.37 rather than 8.03**, so every term priced off the seven — the terrain gain, `KINETIC-FLOOR`'s
two columns, `METRE-LEVEL`'s ladder — is about **half** what those files claim, at this range as
well as at 2,000 km. The seven-degree arrival now has no flown geometry behind it at all.

That also halves 3ae's amplification independently of the damping, and the two compound: the
mechanism needed a gain of 8 and 100 m of relief, and has 4.4 and 3.6 m.

## 3ag. A steep arrival is cheap for the trim, and what it runs into is a wall — headless 2026-09-02

Item 5c asked what a steep arrival costs the **post-boost trim**, on the standing reading from 3y
and 3aa: `ArrivalBudget.SteepestAffordableDeg` prices what the **ascent** can pay and answers 67
degrees, p80 flew 54, the trim gave up, and the arm lost 5.55x. The natural mechanism is that a
steep arrival is dear for the bus to correct on. `TrimAffordableArrivalTests` prices it, and it is
the other way round.

### The exchange rate is the transfer time, and the angle only reaches it through that

`AimAuthority.TryRate` takes the transfer time as a free parameter, so one departure and one aim
priced at a range of times is the controlled experiment. 12,902 km from 600 km:

| flight s | arrival | m/s per km | 1/t | ratio |
| --- | --- | --- | --- | --- |
| 900 | −32.7 deg | 0.917 | 1.111 | 0.83 |
| 1,800 | −0.4 | 0.532 | 0.556 | 0.96 |
| 3,000 | 20.8 | 0.434 | 0.333 | 1.30 |
| 4,500 | 30.8 | 0.398 | 0.222 | 1.79 |

**The arrival steepens and the rate falls, monotonically, at both ranges** — so the angle cannot be
what makes an aim dear. It is about `1/t` at short transfers and falls slower than `1/t` at long
ones, which is the whole of the relation: moving the aim a kilometre downrange in a fixed time costs
about a kilometre per flight time of velocity.

### So a floor never spends the trim's authority. It buys it

The same call `ArrivalBudget` makes, swept finely from a 900 km post-boost departure — the state the
trim actually pays from, rather than the pad the budget is priced at:

| floor | arrival | flight s | cost m/s | m/s per km | km of aim per 60 m/s |
| --- | --- | --- | --- | --- | --- |
| 0 | 3.2 deg | 1,986 | 304 | 0.493 | 121.7 |
| 15 | 15.0 | 2,621 | 1,719 | 0.438 | 136.9 |
| 25 | 25.0 | 3,602 | 3,173 | 0.400 | 149.8 |
| **35** | **35.0** | **6,835** | 4,955 | **0.362** | **165.9** |
| 40 | — | **no arc** | — | — | — |

A binding floor is satisfied with a **longer** transfer every time, so the trim's authority grows
with the angle — 122 km at a graze to 166 km at 35 degrees. `AFloorIsBoughtWithALongerTransferUntilNoArcSatisfiesIt`
asserts that it never shortens, and fails against the opposite.

### What ends the table is the arc ceasing to exist, and that is the mechanism

Past 35 degrees `BallisticArc.TryCheapest` returns false from that departure: not dear, **absent**.
3,459 km walls at 65 rather than 35, so it is a property of the geometry and not a constant.

`ArrivalBudget` sees a wall too — `Cost` is infinity where the arc will not solve, so the bisection
stops at it — but it sees the one at the state it is **called** from, which is early in the burn
with the whole stack still aboard. The floor is latched there, once, by design. What the vehicle
then has to satisfy is the wall at the state the burn **leaves** it in, and the two are not the same
number.

That is 3y and 3aa's mechanism restated, and it predicts what they measured rather than
accommodating it. At 12,902 km a preference of 0.5 latches 0.5 x 67 = **33.5 degrees**, just inside
a wall this reading puts at 35-40; 0.8 latches **53.6**, well past it. A floor past the wall is not
flown shallower — the search falls back to whichever short steep arc still solves, and the flown
sweep shows exactly that: flight time rising to 4,463 s at a 40 degree floor and then **collapsing
to 1,256 s** at 67, with the rate doubling from 0.443 to 1.001 m/s per km and the aim authority
halving from 136 km to 60.

**So `owed on release` jumping 2.63/2.56/2.60 to 4.19 is not a steep arrival being expensive. It is
a short one**, taken because the long one was unreachable.

### What this changes, and what it does not

* **5c is answered and its premise was wrong.** There is nothing to add to `ArrivalBudget` about
  what the trim pays; the trim is better off the steeper it gets. Do not build a trim-budget cap.
* **The lever it leaves is the latch instant, not the fraction.** The floor is priced from a state
  the vehicle has left by the time it has to fly it. Whether re-checking the latched floor against
  the post-boost state is worth anything is unflown, and it is now the cheapest thing 5c can become.
* **It supports 5d rather than warning against it.** At long range, 0.5 moves the shot *down* the
  rate curve — 0.527 to 0.451 m/s per km on the flown sweep, aim authority 114 km to 133.
* **Nothing here is a miss.** No shot is flown, no aim correction runs, and the rig's departures are
  circular states chosen to bracket the flown ones. What it establishes is a price and a wall, both
  properties of the geometry.

### One stale reading this closed

3aa's "what actually binds is somewhere between 44 and 54 degrees" is right about where the loss
appears and wrong about what is binding. It is not the trim's budget: it is the last floor for which
a long transfer still exists from the post-boost state.

## 3ah. The aim correction pins itself to the 300 km clamp on the pad — flown 2026-09-02

Half the flights at 12,902 km land **300 to 310 km** out and the other half under **2.2 km**. It is
not a heavy tail and it is not the arrival angle: it is a clean bimodality with a mechanism, and the
whole of it is visible in one shot's logs.

| rocket | arm | trim | spent | plant readings | response raw | bias | landed |
| --- | --- | --- | --- | --- | --- | --- | --- |
| FAT 6 | base | **GAVE UP** | 3.60 | 14 | **0.00** | **300.0 km** | 310.42 km |
| FAT 4 | base | **GAVE UP** | 3.28 | 15 | **0.00** | **300.0** | 306.90 |
| FAT 3 | p50 | **GAVE UP** | 2.63 | 13 | **0.07** | **300.0** | 303.74 |
| FAT 5 | p50 | **GAVE UP** | 2.62 | 15 | **0.00** | **300.0** | 302.77 |
| FAT 2 | base | done | 27.78 | 56 | 0.98 | 7.0 | **0.01** |
| FAT 8 | base | done | 26.70 | 54 | 1.31 | 1.3 | **0.17** |
| FAT 7 | p50 | done | 39.35 | 52 | 1.08 | 18.8 | **0.09** |
| FAT | p50 | done | 54.67 | 12 | 0.65 | 78.8 | 2.15 |

**It cuts across both arms**, four and four, so nothing about the arrival preference causes it.

### The bias is set on the launch pad, off a miss that is the unburnt velocity

```
13:03:24.655  aim loop on GeoSat FAT 6: 3126.59 km out, best 3096.64, response 4.00,
              bias 0.0 -> 300.0 km, worse for 0, 0 plant reading(s), raw NaN
```

At 13:03:24 every rocket reads `Rising at 0 km, 6284 m/s to gain`. Cutoff is at **13:07:04-13:07:22**,
nearly four minutes later. So the first cycle sees a 3,126 km miss — which is not an aim error, it is
the entire burn not having happened — and `BiasCci - error / _response` with `_response` at its seed
of `1/Gain` = 4.0 asks for 781 km, which `ClampLength` takes to `AimCorrection.MaxMetres` = **300 km
exactly**. Zero plant readings, `raw NaN`: the loop is acting on no measurement at all.

**Every rocket does this**, good and bad alike, so the slam is not the discriminator. What separates
them is whether the loop can climb back out.

### What decides it is whether the trim will fly that aim

The four that failed are exactly the four whose trim ended:

```
trimming the bus on GeoSat FAT 3_1: more than the 57 m/s this pass may spend
                    ... 4_1: more than the 57 m/s ...
                    ... 5_1: more than the 57 m/s ...
                    ... 6_1: more than the 56 m/s ...
```

A 300 km aim move costs `300 x 0.5` = about 150 m/s at this range's exchange rate (3ag), against a
ceiling of 56-57. The trim refuses, the impact therefore does not move, the measured plant response
comes back **0.00**, and a loop dividing by nothing has no way to walk the bias back — `bias 300.0 ->
300.0` for the whole descent, 13-15 readings, while the real miss decays 3,126 -> 208 km on its own as
the burn finishes. The four that succeeded had a trim that flew it, 26-55 m/s spent, 52-56 readings,
a plant measuring ~1.0, and a bias back down to 1.3-18.8 km.

**So the 300 km clamp is the symptom and the trim refusal is the gate.** Both are downstream of a
bias that should never have been set.

### This is the same shape as the arrival-floor latch, in the same flight

Both take their first reading **from the pad**, where the honest answer is "the burn has not
happened", and both keep it. The arrival floor latched a budget of zero because zero is finite; the
aim correction latched a 300 km bias because a 3,126 km shortfall looks like a miss. Neither loop is
wrong about its arithmetic and both are asking before there is anything to ask about.

### What follows, ranked

* **Item 10 is not a marginal 0.85x, it is the fix for this** — or half of it.
  `IcbmConfig.AimWithinTrimBudget` clamps `Reach` to what the trim can pay for, and its own doc
  comment describes this symptom exactly. **The caveat is real**: at cutoff the budget is untouched,
  so the bound is `60 / 0.5` = about 120 km, and flying 120 km still costs the entire 60 m/s. It
  should convert a pinned 300 km into a spent-but-moving 120, not into a small number.
* ~~**The cheaper fix is upstream: do not set a bias from a state that has not burnt yet.**~~
  **Built and flown 2026-09-02, and it is the whole fault.** The miss is not "velocity still to gain"
  as this bullet first guessed — the prediction already departs from the *solved cutoff*, and the
  logs say so. It is that before the vehicle has flown, `BurnoutGuidance` projects that cutoff from a
  standing start and lifts anything underground back to the surface, so the arc is flown **with drag
  from sea level**. Headless, one transfer solved once and flown from five altitudes: **1,522 km** of
  reported miss at sea level, 13.4 km at 40 km, **0.2 km at 74 km**, 0.0 above — same aim, same vacuum
  solution. `AimCorrection.DepartureIsWorthObserving` refuses a departure in air denser than
  `Medium.NoticeableDensity`, which lands the gate at ~74 km without being tuned to.

  | flown, same save and aim | before | after |
  | --- | --- | --- |
  | pad slams to the 300 km clamp | 8 | **0** |
  | biases pinned at 300 km | 4 | **0** |
  | trim refusals | 4 | **0** |
  | trims reading `done` | 4 of 8 | **8 of 8** |
  | worst flight | **310.42 km** | **0.330 km** |
  | group | 4 at 302-310 km, 4 at 0.01-2.15 | **all 8 at 0.023-0.330** |

  **The honest cost**: three of the four flights that were already good drifted 13 to 160 m worse
  (0.01 -> 0.023, 0.17 -> 0.199, 0.09 -> 0.250) and the fourth improved from 2.15 km to 0.036. That is
  the risk this change carried — it removes about four minutes of correction cycles from every flight
  — and at one shot it is far inside the session scatter `SHOT-PROTOCOL.md` documents, so it is a
  thing to watch on the next night rather than a measured loss.
* **Nothing about the arrival angle can be measured at this geometry until one of them lands.** Half
  the flights carrying a ~300 km per-flight error, unshared between arms, swamps a lever worth
  kilometres — which is why the 5d night was called off after one shot rather than flown for 2.5 hours.

## 3ai. With the guidance faults gone, half the miss is the round disagreeing with its own probe — flown 2026-09-02

The shot that verified 3ah's fix is the first at this geometry with nothing large wrong with it, so
it is the first that can say what is left. Eight rockets, all releasing cleanly, `done` on every
trim, biases of 0.1-2.0 km:

| rocket | arrival | probe says | flown | gap | ground under the aim, below 1 km |
| --- | --- | --- | --- | --- | --- |
| FAT | 31.8 deg | 2 m | 36 m | **34** | 15.7 m p-p |
| FAT 2 | 17.6 | 24 | 23 | **−1** | 21.9 |
| FAT 3 | 32.0 | 7 | 226 | **219** | 76.3 |
| FAT 4 | 17.7 | 4 | 33 | **29** | 55.8 |
| FAT 5 | 32.0 | 12 | 330 | **318** | 26.0 |
| FAT 6 | 17.7 | 156 | 221 | **65** | 69.0 |
| FAT 7 | 32.1 | 22 | 250 | **228** | 39.6 |
| FAT 8 | 17.7 | 49 | 199 | **150** | 15.1 |

The probe is `ImpactPredictor` re-flown from the state the round actually left on, so **this gap is a
miss `AimCorrection` structurally cannot remove** — its only observer is that same predictor. Median
**150 m** against flown misses of 23-330, so it is roughly half of what is left.

Both columns are measured against each rocket's **own** aimpoint. `AimSpread` puts the eight 12 to
72 km apart, so a comparison against the group's point would read tens of kilometres; these read
metres, which is the check that they are the same reference.

### The arrival angle does not cause it, and that was the obvious reading

Rank correlation of the gap with the arrival angle is **+0.90** across those eight, and with the
local ground's roughness only **+0.17**. Steep median 228 m against shallow 65. That is a strong
enough signal at n=8 to act on, and it is wrong.

`WhetherTheProbeGapGrowsWithTheArrivalAngle` holds the release position and speed still and rotates
only the flight-path angle, so no two rows differ in energy, in where they start, or in the ground
they cross:

| arrived | mean sphere | with relief | with KSA erosion |
| --- | --- | --- | --- |
| 7.3 deg | −12 m | −14 m | 44 m |
| 10.5 | −4 | −4 | 29 |
| 17.6 | −1 | 1 | 17 |
| 24.8 | −1 | −4 | 28 |
| 31.7 | −1 | −1 | **−798** |
| 39.7 | −0 | −0 | 14 |
| 49.7 | −0 | −1 | 13 |

**Flat at zero, and if anything shrinking as the approach steepens.** The one −798 m is the
"different features" coin toss 3ac describes, on a surface 3af showed is far rougher than KSA's, and
it is an outlier rather than a trend. So the flown +0.90 is a coincidence of eight rockets, and the
arrival angle keeps the whole of what `ARRIVAL-ANGLE.md` claims for it.

### The "opposite sign" reading was wrong, and it was an artefact of this section's own arithmetic

**Withdrawn 2026-09-02, found independently by three of the four investigations in 3aj.** The gap
column above is `|round − aim| − |probe − aim|` — a difference of two **magnitudes**. The probe lands
within 2-49 m of the aim on all eight, so that quantity is very nearly the *magnitude* of the
round-to-probe walk whatever its direction: it is non-negative by construction and **its sign carries
no information**. `ProbeGapTests` reports a **signed downrange displacement**. Comparing the two and
reading "opposite sign, five times the size" compared a magnitude with a vector.

The comparable flown quantity is the log's own signed walk, which is **mixed**: −220, −145, −62, −32,
+31, +41, +191, +284 m — median **−0.5 m**, mean +11, and the fixture's −29 m sits inside that
scatter. **There was never a sign flip to explain.** What is real is a magnitude gap of about 30x,
and 3aj has its cause.

### And 3af's ground measurement does not generalise

3af sampled the ground under **one** aimpoint, read `3.6 m peak-to-peak below a 1 km wavelength`, and
concluded KSA's ground is a factor of 28 below anything that could matter. Eight aimpoints 12-72 km
apart read **15.1 to 76.3 m** — four to twenty-one times more, and the largest is within striking
distance of 3ae's ~100 m threshold rather than far below it.

That does **not** rescue the terrain hypothesis: roughness does not predict the gap here (+0.17), and
the controlled sweep above is flat. What it retires is the specific claim that KSA's ground is
uniformly too smooth to matter, which was one place's ground read as every place's.

### What to do next, and what not to

* **Do not build anything on the arrival angle causing this.** It does not, and the sweep is the
  reason.
* **The open question is what flight has that the fixture does not**, with the sign as the handle.
  Candidates, none measured: the real height field's interpolation and quantisation against
  `TerrainRadiusAt`'s sampling of it, the coast's variable frame against the fixture's fixed one, and
  the round being stepped through `RoundDriver` inside the game loop rather than a tight test loop.
* **It bounds what guidance work is worth.** Half the remaining miss is downstream of the aim, so a
  perfect correction loop buys at most half of 23-330 m at this geometry.

## 3aj. The whole probe gap is one wrong number in the last frame — four investigations, 2026-09-02

3ai left the probe-to-round gap unexplained and blamed the instrument. Four parallel investigations
settled it, and they agree on the mechanism from four different directions.

### It is not accumulated error. It appears at the stop

**The round's walk from its release probe is 1-2 m for the entire 300 s flight, down to 5-6 km
altitude, and then jumps to 31-284 m at the stop.** Everything upstream — the integrators, the frame
jitter, the coast — is worth single metres.

### The two sides read the same surface. They read it at different places

`WarheadTrace.Surfaces` hands the same direction to the round's `GroundTest.Shared` and to the
computer's `TerrainRadiusAt` at every landing point. All eight flown rockets:

```
the round stopped on 6375272.7 m, the prediction flies to 6375272.7 m (+0.0 m apart)   x8
```

So the surface function is identical — the terrain-disagreement hypothesis 3ac-3af spent four
sections on is dead at this geometry. What differs is **where each asks**.

The round stops 13.4 to 173.5 m off that surface, and **that height times `cot γ` is the whole walk**:
right sign 8 of 8, ratios 1.42-3.06 against `cot γ` of 1.60 and 3.13 with the residual being local
slope, and one lane fits it at **r = 0.991, slope 1.025**. The walk is near-pure downrange — cross
2-18 m against down 31-284 — which is what a height error does and a lateral error does not.

### The suspect: the lookup is differenced against a frame-newer body

`Sim/Slug.cs` samples the ground once per frame at its **pre-step** position; `Ksa/GroundTest.cs`
builds the direction as `Unit(positionEcl − nearest.GetPositionEcl())`, and that centre is a celestial
sample **one applied step ahead**. The lookup therefore lands `bodyVelocityEcl · dt` away — at the
flown 18-33 ms and 30,190 m/s that is **536-1,005 m of chord**, of which the tangential part displaces
the sample. The round's own within-frame ground track is only 115-157 m, so the epoch term would be
4-6x larger.

**`WeaponSystem.cs` already carries the comment naming it**, beside two neighbouring lookups that do
apply the correction and one that does not:

> *"The sample is still one applied step ahead of the pre-step round, and the correction for that is
> to put the body back by bodyVelocityEcl*dt … which is what AirDensityIntoFrame does below and this
> does not."*

`AirDensityIntoFrame` and `GroundCentreDriftIntoFrame` back-date; the terrain **radius** never did.

### Why no fixture could have caught it

`tests/KSArmory.Tests/DeorbitShot.cs`'s `OneFrame` hands the ground test the centre **at the round's
own instant** — deliberately, with a comment saying it can only be paired one way — and `Relief` sets
its centre to zero and is carrier-blind. `ProbeGapTests` never constructs a `Carrier` at all. The rig
does not model the shipped pairing; it models the correct one. Its own header already said a rig whose
planet sits at the origin is *"not bad at seeing them, incapable"*.

That also explains a standing puzzle: `CarriedFrameTests.TheImpactDoesNotMoveWhenThePlanetDoes`
records an unexplained 207.87 m / 590.83 m carrier residual its doc says "has not been run to ground".
It is `OneFrame` not passing `GroundCentreDriftAt`. Measured on a corrected rig: 0.0 m as the game
pairs it, 213.4 m as `OneFrame` does, 4,081.3 m with neither.

And a second reason the fixture reads small: `DeorbitShot.RoughGround` — which 3ai called realistic —
carries **0.3 m** peak-to-peak below a kilometre where the eight flown aimpoints carry **15.1-76.3 m**.
The rig's round accordingly stops within 0.3 m of it.

### What is measured and what is still inferred

**Measured**: the walk's flat-then-jump shape; the +0.0 m surface agreement; the 13.4-173.5 m stopping
heights and their `cot γ` fit; the signed walk being mixed; zero skipped steps, zero overruns, lag
−0.4 to −0.9 ms over a whole flight; and headlessly, that correct pairing is exactly carrier-invariant
where the shipped pairing moves the impact 22-712 m across slope and frame length.

**Inferred, and this is the open question**: that the epoch displacement dominates the round's own
within-frame ground track. The flown log carries only their **sum**. The arithmetic favours it — 173.5 m
of height over 50 m of ground track needs a slope of 3.5, where adding ~700 m of displacement puts the
implied slopes at 0.03-0.23, inside the 0.018-0.107 the `ground under the aim` sampler measures — and
the 33.3 ms frames carry the larger errors while the 32-degree group has *less* within-frame track and
*more* error, which is the wrong ordering for the ground-track term and the right one for the epoch
term. None of that is a measurement.

### The diagnostic, shipped rather than the fix

`Slug.GroundSampledAtEcl` and `GroundSampledOverSeconds` record where and over what frame the round
read the ground; `WarheadTrace.GroundSample` prints the height field at that point and at the same
point back-dated by `bodyVelocityEcl · dt`. The difference is the epoch term **on its own, on KSA's
real terrain**. It costs two lookups on the landing frame, is off with the rest of the trace, and rides
the next flight at no extra cost.

**The fix is one expression and it is deliberately not applied yet**: pass the back-dated position to
`Ground.TryGround`, the same correction three neighbouring lookups already make. Two previous phase
corrections of this exact shape were flown and **lost** — `docs/KSA-FRAME-ORDER.md` section 5 — so the
diagnostic reads first. Those two were a field integrated over 400 s and a wind; this is a value read
once, at the instant it decides where the round stops, which is a different case but not an argument.

**If it holds it is worth about half the remaining miss at this geometry**, which after 3ah is
23-330 m.

## 3ak. The night, and three things it got wrong — flown 2026-09-02, corrected the same evening

**Read the correction first.** Three of this section's conclusions were withdrawn within hours by the
investigations in 3al. In order of how badly they mislead:

1. **The range is 6,269 km, not 12,902.** `--aim 26.485S,68.148W` is a **6,269 km** shot — the mod's
   own log says `aimed at scenario aim point (6241 km downrange)`. The historic 12,902 km nights used
   `aim none`, the save's own target, which is a **different geometry**. The 5d row said to fly
   "12,902 km" with that aim and it was wrong; this night therefore compared a 6,269 km result against
   12,902 km history. **Every range figure below is mislabelled**, and so is 3ag's prediction, which
   priced the exchange rate at 12,902 km for a shot that flew 6,269.
2. **`ArrivalPreference = 0.5` may not lose at all.** The verdict below is confounded with timewarp:
   p50's rockets land later, by which time the harness has asked for 8x, so the arm and the frame
   length are entangled. At matched frame length p50 reads **0.25x**, not 1.91x — 3al.
3. **The "1 s vs 26 s" arrival table is the logger describing its own trigger.** The clause is emitted
   only once the trim demand is already over its ceiling, and only when the two disagree by a whole
   second. Healthy shots run it on 2 of 3,030 trim lines; shot 006 on 16 of 16. **The rate is the
   discriminator, not the count**, and everything under the ceiling was invisible.

What survives unqualified: the shot-006 chain (2.35 m/s of trim demand per second of arrival error,
ceiling crossed at 4.3 s), the seat gradient, and the epoch measurement — whose sign was also wrong,
see 3al.

## 3ak (as written). The night: 0.5 does not travel, and the epoch fix is not justified

12 paired shots, 96 flights, `base|p50:ArrivalPreference=0.5` at 12,902 km on `SOLVER SCALE 8`,
`~/shots/2026-09-02-1508`, frame 23.4 ms, 5 correction passes at the median shot. The first night at
this geometry with 3ah's two fixes in, and it carried the 3aj diagnostic for free.

### Item 5d: p50 does not win here, and the point estimate is against it

| arm | flights | median | arc | floor | afforded | owed m/s |
| --- | --- | --- | --- | --- | --- | --- |
| base | 48 | **0.19 km** | 17.7 deg | — | — | 2.60 |
| p50 | 48 | **0.35 km** | 32.0 | 31.9 | 63.8 | 2.63 |

**p50 vs base: 1.91x [0.49, 3.79] at 97%, won 3 of 12, sign p=0.146, signed-rank p=0.151 —
`unresolved` by the protocol's rule, and pointing the wrong way.** Per shot: 5.62, 0.49, 5.99, 1.40,
3.79, 1.06, 2.24, 0.35, 3.75, 1.63, 3.60, 0.26.

**So 3aa's 0.48x win at 2,000 km does not travel.** The same setting that halved the miss at the short
geometry roughly doubles it at the long one, and the interval does not exclude either. That is a
result about *range*, not about the setting, and it retires the assumption in 5d that the lever should
be worth more where `cot γ` is larger.

**And it refutes 3ag's prediction outright.** 3ag priced the aim's exchange rate at this range and
predicted 0.5 would move it *down* — 0.527 to 0.451 m/s per km, authority 114 to 133 km — and
concluded the night should therefore help. It did not. The exchange-rate reading stands as arithmetic;
what was wrong was assuming it was the term that decides the miss.

**The terminator table says the same thing from the other side**, and inverts 3aa's reading:

| arm | clock | noimprov | payback | trim |
| --- | --- | --- | --- | --- |
| base | 9 | 9 | **26** | 4 |
| p50 | 5 | **28** | 11 | 4 |

At 2,000 km a steeper arrival moved every flight onto `noimprov` and that was *the shape of a smaller
miss*. Here p50 does the same thing and lands twice as far out, while base's `payback` — the ending
3f called a selection effect — is the one attached to the good shots. **`noimprov` is not a proxy for
accuracy**, and any future arm scored on that table alone would have read this night backwards.

### The seat gradient is still there

rho = +0.23, p = 0.023 across 96 flights, medians by seat 0.393 / 0.019 / 0.190 / 0.066 / 0.932 /
0.193 / 0.349 / 0.191 km. Smaller than the 175x of section 1 — which was the warp contamination — but
not zero, and unexplained.

### The arrival latch can drift, and it is worth 90 km

One shot of twelve (006) failed with **all eight** rockets at 75-99 km. The burns were clean — 8 of 8
at 33 ms, cutoff residuals 0.096-0.530 m/s, arrival angles normal, 6 of 6 warheads released by every
bus. What separates it is one line:

```
solving to an arrival 420 s away; the flown prediction says 402 s
```

| shots | worst arrival disagreement | occurrences |
| --- | --- | --- |
| 001-005, 007-012 | **1 s** | 1-2 each |
| **006** | **26 s** | **16** |

The committed arrival drifted 26 s from the trajectory being flown; the trim was then asked for
**55-128 m/s** against a per-pass ceiling in the tens, gave up on all eight buses, and the warheads
went out uncorrected. The `trim` terminator's median is **92.41 km** against 0.15-0.41 for every other
ending. Perfectly bimodal, world-level, and it hit both arms equally — four flights each — which is
why the paired instrument still reads. **This is now the largest single item at this geometry** and it
has no explanation: nothing in that shot's setup differs from the eleven that were fine.

### Item 12: the epoch term is implicated and the fix is NOT justified

The 3aj diagnostic across **95 warheads**:

| | median | range |
| --- | --- | --- |
| stop-height error | \|61.3\| m | −1227.3 to +479.1 |
| epoch term | \|81.0\| m | −607.3 to +640.9 |
| frame at the stop | — | 18.2 to **266.7** ms |
| body moved in it | — | 548 to **8,051** m |

* **Magnitudes track**: `rho = +0.519` on `|epoch|` vs `|stop|`, n=95. The epoch displacement predicts
  how large the stopping-height error is, strongly and with no ambiguity about significance.
* **The signs do not**: `rho = −0.408` signed, and the median `stop/epoch` ratio is **−0.60**. A
  straight pass-through — the round holds a radius sampled where the ground is H metres different, so
  it stops H metres off — predicts **+1**. The data says −0.6.

**So the one-expression back-date is not justified, and this is exactly what the diagnostic was for.**
Something implicates the epoch displacement in the *size* of the error while the naive correction has
the wrong sign, so applying it could as easily double the error as remove it — which is what happened
to the two phase corrections `docs/KSA-FRAME-ORDER.md` section 5 records as flown and lost. Item 12
stays open, and the next step is to find why the ratio is −0.6 rather than to ship the fix.

**One caveat on the instrument itself**: `radiusAt` is evaluated at the *landing* frame against the
round's *previous* position, so it is not identically the radius the round held. Whether that accounts
for the sign is unknown and is the first thing to check.

**And a second reading the diagnostic gave away for free**: frames at the stop run to **266.7 ms** and
the body moves up to **8 km** within one. That is warp during the terminal descent, and it is not
what `WarpPolicy` is supposed to allow while rounds are in the air.

## 3al. Four investigations, and most of 3ak was the instrument — 2026-09-02

Four parallel lanes were put on 3ak's open items. They converged, and between them they withdrew
three of 3ak's conclusions and one of 3aj's. **Every fault found this round was in something that
measures, not in the guidance.**

### The range was never 12,902 km

`--aim 26.485S,68.148W` is a **6,269 km** shot — `aimed at scenario aim point (6241 km downrange)`,
the mod's own line, computed as the great circle from the craft to the aim. The 12,902 km nights used
`aim none`. Item 5d told an operator to fly "12,902 km" with an aim that is half that, so the night
compared one geometry against another's history — and **3ag's prediction was priced at 12,902 km for
a shot that flew 6,269**, which is why it failed. Nothing about the physics is implicated.

### The trim's arrival readout only fires once the trim is already lost

`IcbmComputer.Arrivals` was gated on `trim.ToGainMetresPerSecond > BusTrim.MaxMetresPerSecond`, so it
could report the disagreement's tail and never its distribution. 3ak's "1 s on eleven shots, 26 s on
one" is the logger describing its own trigger: healthy shots emit it on **2 of 3,030** trim lines,
shot 006 on **16 of 16**. The rate is the discriminator; the count is an artefact. Now printed
unconditionally.

**What survives about shot 006**, and it is a complete chain: the committed arrival is a velocity
command at **2.35 m/s per second of error**, so `BusTrim`'s 10 m/s ceiling is crossed at **4.3 s**.
Per craft the flown `owed ~= 4.9 x delta` and the miss follows. The burn was healthy to cutoff and for
65 s of coast; the divergence starts silently mid-coast as a **linear ramp of 252-342 m/s of simulated
time**, staggered across the eight craft, visible only in a DEBUG stream nobody reads. Whether the
ramp is the bus's state or the predictor's answer is **not established** — the 168 s between cutoff
and split is a logging blind spot, and a per-craft coast probe is the next diagnostic.

### The mod was fighting its own timewarp, and it corrupted the instrument

`BallisticScenario` asks for 8x once a salvo is away; `WarpPolicy` read that as a competing writer and
yielded. `_yielded` clears only on an empty sky, which eight staggered rockets never give, so **one
spurious yield stood the policy down for the whole flight** — 9 shots of 12.

* frame at the stop: **18-33 ms** in the 3 shots that held, **117-267 ms** in the 9 that did not
* frame length vs stopping-height error: **rho = +0.661**, surviving controls for terrain (+0.660),
  arrival angle (+0.562) and arrival speed (+0.556)
* **0 of 44** base traces stopped in a frame over 60 ms; **32 of 43** p50 traces did

**This is what item 5d actually measured.** Base's four rockets land first at short frames; p50's land
88-174 s later, by which time the harness has asked for 8x. A long frame multiplies a seat's own bias
by **5.8x**. At matched frame length p50 reads **0.25x** rather than 1.91x, and where p50 did land in
a short frame its bias matches base's at the same seat. 3ak's verdict is withdrawn.

### The seat gradient is a fixed per-aimpoint terrain bias

`AimSpread` puts each rocket on its own 12 km of hillside, so the eight are **not eight draws from one
distribution**. Each seat's stopping-height error is a property of its ground, repeatable **to 1-2 m
across 12 shots over three hours**, 44 of 44 traces sharing their seat's sign (p = 5.7e-14):
−16.7, −7.6, −42.4, +29.3, −110.0, +62.0, +61.3, +50.0 m.

Fully mediated: seat → stop-height **+0.680**, stop-height → miss **+0.892**, seat → miss controlling
for stop-height **+0.084, p = 0.586**. It is *not* the logged roughness (rho = −0.030), which is why
3ak's terrain hypothesis as posed was refuted while the underlying idea was right. At a flat aim the
stop-height error is **0.0 m in all 56 traces** and the seat effect vanishes — Friedman p = 0.38
against 1.4e-4.

**Consequence for every future night: `--paired` survives this only because the arm rotates across
seats. Any statistic pooling seats within one arm is reading terrain.**

### The epoch diagnostic had the sign backwards, and the fix was justified all along

3aj applied `sampledAt − V*dt` where `AirDensityIntoFrame` and `GroundCentreDriftIntoFrame` both carry
`+V*dt`, so it modelled the fault **doubled**. 3ak's −0.60 is half a reciprocal pair whose centre is
−1; on unwarped warheads the relationship is **47 of 47 inverted, Spearman −0.877, Theil-Sen −1.121**.
The pooled +0.519 magnitude correlation was a two-cluster artefact of the frame regime.

So `Slug` now asks the ground at the round's own epoch through the existing drift seam. Headless, on
the eroded spectrum through the real `RoundDriver`: median stopping-height error **21.3 → 8.7 m** at
20 ms and **30.9 → 11.9 m** at 29 ms.

### Flown, and both fixes hold

One shot, same save and aim, with the warp funnel and the ground back-date in:

| | before | after |
| --- | --- | --- |
| group | 8 of 8 at 23-330 m | **8 of 8 at 13-108 m** |
| frame at the stop | 18-33 ms base, **117-267 ms** p50 | **21.7-41.4 ms, all eight** |
| stopping-height error | −16.7, −7.6, −42.4, +29.3, **−110.0**, +62.0, +61.3, +50.0 m | **−0.3, −12.3, +0.0, +4.5, +26.5, +11.7, −7.5, +40.9** |
| median absolute | ~46 m | **~10 m** |

No rocket landed on a long frame, which is the warp funnel; and the per-seat biases that had been
stable to the metre across twelve shots collapsed, which is the back-date. **One shot settles a
direction, not a median** — `SHOT-PROTOCOL.md`'s scatter still applies and the paired night is what
sizes it.

**And over-correcting is worse than either.** The trace's counterfactual column, applied a second time
on top of the fix, reads −208.5 m where the round held −12.3. It now points backwards instead, at what
the old lookup would have held, which is the comparison worth having.

### One real ordering instability, not the cause of anything here

`IcbmComputers.Update` iterates a dictionary whose order `Follow`'s remove-and-insert scrambles at
staging — measured as three distinct orderings within one shot. Nothing correlates with it, and every
cross-rocket quantity is already collected before the loop that uses it. Recorded so the next person
does not have to find it twice.

## 3am. The clean night: 0.5 wins, resolved — flown 2026-09-02, read 2026-09-03

12 paired shots, 96 flights at **6,269 km** on `SOLVER SCALE 8`, `~/shots/2026-09-02-2131`, frame
23.9 ms. The first night on a harness that is not corrupting itself: 3al's warp funnel, the ground
back-date, the arrival-floor fix and the pad-aim fix all in.

### Item 5d, answered

| arm | flights | median | arrives at |
| --- | --- | --- | --- |
| base | 48 | 0.04 km | 17.7 deg |
| p50 | 48 | **0.02 km** | 32.0 |

```
p50 vs base: 0.69x [0.17, 0.88] at 97%
   won 11 of 12 paired shots, sign p=0.006, signed-rank p=0.009   RESOLVED
   per shot: 0.59 0.47 1.82 0.12 0.88 0.17 0.83 0.20 0.80 0.98 0.80 0.09
```

**Clears the bar on both tests with the interval entirely below one**, which no arrival-angle arm has
managed before. Against 3ak's **1.91x the wrong way** on the same command, same save, same aim — that
was the 8x warp landing p50's rockets on 240 ms frames, and fixing the harness turned a spurious loss
into a real win. `ArrivalPreference = 0.5` is now resolved at **two** geometries: 0.48x at 2,000 km
(3aa) and 0.69x here.

### The seat gradient is gone

```
rank correlation seat vs miss: rho=-0.04, p=0.683   no gradient at this n
```

Against **+0.23, p=0.023** the night before, with seat medians collapsing from 19-932 m to 21-146.
That is the ground back-date confirmed from a direction it was not fitted to: the per-aimpoint
terrain biases 3al measured as repeatable to 1-2 m are **removed**, not averaged over.

### Where the shot stands

| | 2026-09-02 morning | this night |
| --- | --- | --- |
| median | 6,664 m | **30 m** |
| p90 | 28,652 m | **112 m** |
| best | — | **5 m** |
| shape | bimodal, 75% at 8.81 km | unimodal, plus one rare event |

88 clean flights across 11 worlds. The 12th is below.

### And one thing is now the whole remaining problem

| ending | n | median |
| --- | --- | --- |
| clock | 21 | 0.03 km |
| noimprov | 34 | 0.02 |
| payback | 33 | 0.04 |
| **trim** | **8** | **94.27 km** |

One world in twelve, all eight rockets, and it is three thousand times every other ending. **It is now
the entire difference between a 30 m weapon and an unreliable one.**

### The drift, instrumented at last — and the guard is not the answer

The coast probe caught it. The onset is **sharp**, at ~505 km on the way *up* with the bus still
climbing at +632 m/s, and the miss had been *improving* right up to it:

```
502.8 km  r_dot +669.3  miss  0.40 km  rate   -0.7   <- converging
509.4 km  r_dot +632.5  miss  2.15 km  rate +172.3   <- break
515.6 km  r_dot +595.6  miss  5.82 km  rate +366.2
521.4 km  r_dot +558.6  miss 11.51 km  rate +565.2
```

It **accelerates** rather than ramping linearly, which 3ak's reconstruction could not see. Attitude
and control are healthy throughout — all eight holding to 0.001 deg with normal rates.

**The signature says which side is moving.** The committed arrival counts down at exactly 10 s per
10 s, as a fixed instant must; the flown prediction counts down at about **11 s per 10 s**. So the
predictor increasingly believes the round will arrive **sooner and shorter** — the shape of a
trajectory losing energy, which the bus is not.

**And the arrival guard fired on all eight and did not save the shot** — 85-102 km anyway. That is a
result, not a wasted change: it eliminates the latch as the cause, which was the leading candidate,
and it costs nothing on a healthy flight (0 firings in 88).

**Next, and it is a diagnostic rather than a fix.** The leading suspect is the density the predictor
is handed at altitude — `IcbmComputer.DensityRatioAt` feeding `ImpactPredictor.Drag`. A spuriously
non-zero density at 500 km produces exactly this: sooner, shorter, and worsening as the predicted path
bends further. **Unverified**, and the way to settle it is to log what that lookup returns through the
coast rather than to change anything.

### The default is justified and does not ship yet

`ArrivalPreference = 0.5` has now won at two geometries and lost at none. Setting it as the default
was tried and backed out: it changes the arrival angle every headless fixture flies, and eight tests
encode measured constants at the geometry they currently get — `ArrivalDebtTests`'s 2.48 m/s per
kilometre among them. Re-recording those under the same names would file different facts. **Each
fixture should state the geometry it means rather than inherit it**, and the default waits on that.

## 3an. The air is exonerated, and the terminator is the aim being driven — flown 2026-09-03

Ten paired blocks at 26.485S,68.148W on the instrumented build, to point item 15's density probe at
the `trim` terminator. 80 flights, 150 of them carrying a coast trace.

### The answer to item 15 is no, and it is worth as much as a yes

| | samples above 300 km | non-zero | max density |
| --- | --- | --- | --- |
| healthy flights | 4,172 | **0** | 0.00E+00 |
| divergent flights | 2,009 | **0** | 0.00E+00 |

**`DensityRatioAt` returns exactly zero through every one of the 52 divergences.** The hypothesis was
that `KsaWorld.MediumDensityRatioAt` was taking one of its five `return 1.0` failure paths and handing
the predictor sea-level air at half a megametre, which bends the predicted arc down and reads as
arriving sooner and landing shorter — the flown signature exactly. It is not happening. The drag model
and the atmosphere lookup are both cleared, and **item 15 is closed by refutation.**

The control is as tight as it could be: on healthy flights the reading is 3.0E-9 at 157 km and a hard
zero from 209 km up, *including at 501.5 and 504.5 km* — the precise band the onset sits in. There is
no altitude at which healthy and divergent flights read differently, because neither reads anything.

### What the terminator actually is

The onset is sharp and the flights are **accurate before it**: median miss over the 52 divergent
flights, at the last sample before the break, is **0.06 km**. These are sixty-metre shots that become
ninety-kilometre ones.

| onset altitude | flights |
| --- | --- |
| 25-50 km | 5 |
| 475-550 km | 35 |
| 800-900 km | 12 |

One flight's coast, which is the shape of all of them:

| altitude | predicted miss | rate | impact latitude | arrives - committed |
| --- | --- | --- | --- | --- |
| 509.8 km | 3.90 km | -0.1 m/s | -26.556 | 3 s |
| 516.1 km | 5.54 km | **+163.7 m/s** | -26.533 | 2 s |
| 532.5 km | 15.51 km | +340.3 m/s | -26.430 | 0 s |
| 564.9 km | 50.07 km | +305.4 m/s | -26.111 | 8 s |

Three things follow, and the third is the item.

**The arrival latch is not it either.** `arrives - committed` runs 0-8 s across the whole divergence,
against the 26 s that 3ak measured and ranked as item 13. Whatever that shot was, it is not what these
52 are.

**The impact walks rather than scattering.** Monotonic in latitude, 0.45 degrees over the divergence,
at a near-constant ~300 m/s of miss per second. A quantity moving at a constant rate is being *driven*,
not diverging. That column exists because a scalar miss cannot tell a march from a scatter, and this is
the measurement it was added for.

**The aim is what is being driven, and the trim then refuses to pay.** At release:

```
aim loop:        95.71 km out, best 95.79, response 1.00, bias 3.1 -> 94.1 km, worse for 0
release summary: trim owed 123.88 m/s at the split and 122.03 m/s on release
                 (0.00 m/s spent, GAVE UP), arriving at 31.8 deg
```

The bias is at **94.1 km** against the 3.1 it held all coast, the resulting trim demand is **~124 m/s**
— an order of magnitude past a bus's authority — and the trim spends **nothing** and gives up, so the
warheads leave on the walked aim. That is the `trim` terminator, and its 90.95 km median is the walked
bias arriving.

`best 95.79` against `95.71 out` is the part that says this is a fault rather than a large correction:
the loop believes 95 km is the best reading it has ever taken, on a flight that was at 0.06 km minutes
earlier. **`AimCorrection`'s keep-the-best-and-revert safety cannot fire, because what it is holding is
already the runaway.** Either the best was reset under it, or the observation moved so far that the
earlier reading is no longer comparable.

### Two things this night could not settle, both named rather than guessed

**The single cycle.** The bus emitted exactly **one** aim-loop line in the whole night, already showing
`3.1 -> 94.1`. So the bias arrives at 94 km in one cycle, where the coast probe shows the predicted
impact walking over ~100 s. Those are different shapes and they are logged on differently-named craft,
so whether they are one fault or two is **not established**.

**The names collide across a split, and that is load-bearing for every per-craft diagnostic.** The
rockets are `GeoSat FAT`, `GeoSat FAT 2` ... `GeoSat FAT 8`; split products appear as `GeoSat FAT_1`,
`GeoSat FAT 2_1` and so on. But the disposal line reads `GeoSat FAT: taking the spent stage
GeoSat FAT 4_1 out of the world at 59.8 km` — rocket 1's computer naming a stage that by the suffix
rule belongs to rocket 4. One of the two readings is wrong, and until it is known which, **a per-craft
trace across a separation cannot be trusted to follow one rocket.** Nothing in the mod keys on the
display name, so this is an instrument fault rather than a flight one — which is exactly why it has to
be fixed before the next night rather than after.

### The night's other numbers

`ArrivalPreference = 0.5` read **0.66x [0.38, 1.05], 7 of 10, signed-rank p=0.084** — unresolved at ten
blocks, and consistent with 3am's resolved 0.69x at twelve. Nothing here revises 3am; it is the same
effect at less power. The seat gradient stayed dead: **rho=-0.07, p=0.517**, seat medians 0.021-0.215 km.

Endings: `clock` 12 at 0.02 km, `noimprov` 23 at 0.03, `payback` 21 at 0.04, **`trim` 24 at 90.95**.
The terminator is still the entire difference between this weapon and a reliable one.

## 3ao. The split census was adopting other rockets' stages — found 2026-09-03, unflown

Item 18 was ranked as an instrument fault: a per-craft trace could not be trusted across a
separation, which blocked reading item 17. It is not an instrument fault. It is a live one.

### What it was

`WhatWasDropped` finds the shed stage **by difference** — the world is counted when this computer
asks for separation, counted again when the engine reports it done, and anything new in between is a
candidate. The window is not one frame; it is however long the decouple takes through the engine's
input buffer. A world flying eight rockets on one profile stages them within moments of each other,
so that window catches *their* stages too.

The tie-break was nearest-of-them at **any distance**, and the night's logs show what that adopts:

```
GeoSat FAT 2: taking the spent stage GeoSat FAT 3_1_1 out of the world at 20 km
GeoSat FAT 2: taking the spent stage GeoSat FAT 4_1_1 out of the world at 40 km
```

KSA names a split product by appending `_N` to its parent's Id, so those are demonstrably rocket 3's
and rocket 4's stages, adopted and destroyed by rocket 2's computer.

**What it costs is the separation gate.** `_separatedFrom` is what `Clear()` measures, so a computer
holding a foreign stage reads tens of kilometres of separation, `SeparationClearance` passes at once,
and the trim is authorised while this vehicle's own stack is still alongside — the exact failure
`docs/MIRV-NEXT.md` 8y and 8z rank as the accuracy.

### The fix, and why it is shaped like `PlatformHandover`

A decoupler parts two halves at about a metre a second, so a stack dropped a few frames ago is metres
away and nothing else in the world is. `Sim/ShedStage.cs` bounds the candidate at 10 km and
**refuses when more than one is inside it**, rather than taking the nearer — which is the rule
`PlatformHandover` already draws for a part, for the same reason and in almost the same words.
Refusing costs a clearance that reads unknown and falls back to its clock; choosing wrong reports a
stack that is already clear.

`ShedStageTests` fails on 3 of 6 against the old census — the two adoption distances and the
ambiguous pair — and passes on the three that are invariants either way.

**Unflown.** Nothing here has been in the air, and the connection to the `trim` terminator is a
hypothesis rather than a finding.

### What 17 already narrows to

The single aim-loop line the diverging bus emitted carries its own interval:

```
bias 3.1 -> 94.1 km, 95.71 km out, best 95.79, 4 plant reading(s)
departure vel 7322.8427 m/s over 974.83 s
```

**974.83 seconds since the previous reading.** The correction is rationed after the burn — one
observation between the aim moving and the trim having flown the new arc would read its own unspent
correction as error — and on this coast the ration came due once, at the end. So a full-size 91 km
bias was applied off a single unverified reading, which is also why `best` equals the current miss.

The other half is why the prediction walked 50 km during a coast at all. On a coast it is an exact
function of one state. **The 91.5 km per m/s quoted here was wrong by two orders and is corrected
in 3as** — this arc's along-track amplification is 0.38 to 2.93 km per m/s, which is what
`METRE-LEVEL.md`'s own `dMiss/dV` table says (415 m per m/s at 30 deg). The walk is not along track. The coast probe now reports whether the trim is
firing and how long since the correction last read, which is what separates a bus being perturbed
from a predictor drifting on its own.

### And the frame question from 3an is closed as no fault

3an flagged that a predicted impact sat ~31 km from the nominal aim while reporting a 3.9 km miss.
`AimSpread` is the whole of it: `SpacingInLethalRadii = 6.0` puts the eight aim points ~12 km apart
along a line spanning ~84 km, and the traced rocket was aimed at `-26.556,-68.496` rather than at the
nominal `-26.485,-68.148`. Its prediction landed `-26.561,-68.462` — 3.4 km away, against the 3.90 km
reported. The `lands` column is in the frame it claims.

## 3ap. The terminator is a world-level event, and three of 3ao's claims were wrong — flown 2026-09-03

Ten paired blocks, `base|p0:ArrivalPreference=0.0`, on the census change and the new probe columns.
The pairing is inverted because 0.5 is now the default; the arm *composition* is identical to 3an's
night — 40 rockets at 0.0 and 40 at 0.5, both times — so the two nights are a controlled before and
after on everything except the code.

### The default is confirmed, decisively, from the other side

**0 wins of 10, sign p=0.002, signed-rank p=0.002 — RESOLVED.** Per-shot ratios 1.02 to 7.61, every
one above 1. Asking for a shallower arrival than the tanks can afford is worse at every block, which
is 3am's result approached from the opposite direction and is the strongest arrival-angle reading
this project has.

### What 3ao got wrong

**One. The fix was inert.** `WhatWasDropped` was bounded; the clearance measured **2 m** on both
nights, median and max, 160 splits each. `_separatedFrom` was already correct, because the census is
only consulted when the earlier capture is dead. Zero refusals fired.

**Two. The disposal lines are a different census.** The 20 and 40 km adoptions quoted in 3ao come
from `CollectShedStages`, which adds **every** new vehicle to `_shed` with no distance test at all —
not from `WhatWasDropped`. Far adoptions were 1,632 before and 1,662 after: unchanged, as they must
be. That census is still unbounded, and it is worse than 3ao described: rocket 1's computer was
observed trying to dispose **rockets 3's and 5's buses** at 39.9 and 79.9 km, twice each, six minutes
before those buses released their warheads. The destroy did not take — all 80 flights produced
endings — but nothing in the design prevented it.

`StageDisposal.ClearOfTheCraftMetres` states the safety argument for its one-kilometre margin as
"the census identifies a stage as new in the world **and nearest to the craft**". That is true of
`WhatWasDropped` and false of `CollectShedStages`, which is the census that actually feeds disposal.

**Three. The unit is the world, not the flight.** 3an reported "24 of 80 flights, 30%". The
terminator is all-or-nothing per world — 16 `GAVE UP` lines or zero, never between:

| night | worlds affected |
| --- | --- |
| 3an's | **3 of 10** (001, 004, 007) |
| this one | **1 of 10** (005) |

So the apparent 24 -> 8 improvement is **3 worlds against 1, p about 0.58** — noise, and consistent
with the original "one shot in twelve". Treating rockets in one world as independent overstated both
the rate and the significance. Nothing here is evidence that the census change helped, and the
inertness above says it could not have.

### What the night did establish, and it reframes 17 completely

Every rocket in an affected world diverges **at the same instant**, at unrelated altitudes:

| time | craft | altitude | miss rate |
| --- | --- | --- | --- |
| 10:34:20.321 | GeoSat FAT 4 | 802.6 km | +147.0 m/s |
| 10:34:20.342 | GeoSat FAT 2 | 803.7 km | +145.9 m/s |
| 10:34:20.867 | GeoSat FAT | 480.9 km | +29.4 m/s |
| 10:34:20.867 | GeoSat FAT 7 | 481.7 km | +281.1 m/s |

All eight inside 0.55 s, four sharing one frame, across two altitude groups 320 km apart. **The
onset-altitude clustering in 3an was an artefact of pooling across worlds** — there is no altitude
threshold, and the ~505 km figure that motivated item 15's density hypothesis was never a real
feature.

The aim biases are stable at 3.7/3.7/1.7/3.8/3.8/4.0/1.6/3.8 km across the break and stay stable; the
**predicted misses** are what start moving, together, at 10:34:19.75. So the prediction moves first
and the aim loop follows it — the loop is still doing its job on a bad observation.

No warp change, no overrun and no frame spike is logged at that instant: the world had been held at
5.4x since 10:33:40 and the next change is at 10:36:20. Whatever is shared is **not** anything the
mod currently records.

**So 17 is not a per-rocket guidance fault.** It is one world-level disturbance that moves every
prediction at once, and the next step is to find what is common to eight computers at one instant —
the parent body's sample, the epoch, or something in the engine the mod does not log.

## 3aq. Three hypotheses flown and none survives, but the coast premise is wrong — 2026-09-03

Three subagents read the decompiled corpus for anything that could move every vehicle's prediction
at one instant. They produced three candidates and excluded a great deal; a solver-load ladder then
flew 2 blocks each at 8, 16 and 32 rockets to test them.

### What the ladder settled

**H3, `PhysicsBubble._forceOffRails`, is refuted.** A `public static bool` read every sub-step whose
only writer in the whole game is a debug checkbox. The coast probe now reads it: `(forced)` appears
**zero** times in 9,466 probes. It has never been set.

**H1, the engine's speed governor, is real and is not the cause.** `Universe._achievedSpeedFraction`
scales the world's step and snaps down in one frame when the vehicle solver overruns. Measured for
the first time, and it is strongly dose-dependent:

| vehicles | frames the world ran slower than asked |
| --- | --- |
| 10-19 | 1.22% |
| 30-39 | 3.57% |
| 50-59 | **55.88%** |
| 60-69 | **65.16%** |

Lowest fraction seen 0.260. **But the failure does not track it.** At 8 rockets, with the governor
holding 1.22% of frames, both worlds threw the ~90 km event; at 32 rockets, with it holding 65% of
frames, the trim endings missed by **4.96 km**. More governing does not produce the failure.

**H2, an off-rails integration error scaling with the step, is not supported either.** Off rails is
necessary but nowhere near sufficient — **65 of 65** divergent flights went off rails, and so did
**124 of 126** healthy ones. And its central prediction fails on the sign: over 24 worlds, the ones
that threw the event averaged **1.64x** achieved warp against **1.97x** for those that did not, and
the two worst ran slowest of all. A step-scaling error should be worse at high warp. It is not.

### What the ladder did establish, and it is a design fault regardless

**A coast IS being integrated, and this file said it was not.** `PhysicsBubble.cs:1085` puts a
vehicle off rails whenever `AnyActuatorCommanded` or `AnyActuatorActive`. The mod drives attitude
through the whole coast to hold the line the warheads leave along — so it commands actuators, so the
bus leaves exact Kepler propagation and is integrated with velocity Verlet at whatever step the warp
gives it. Measured: **17-20% of all coast probes report off rails.**

`CLAUDE.md` justifies not holding timewarp during the coast on the premise that "a coast is not
being integrated by anything". That premise is false, and it is false *because of something the mod
itself does*. Whether it is the cause of the terminator is unproven — the warp correlation above
argues against it — but the premise cannot stand as written.

### Method note

Two markers were tried and both contaminated a correlation before the third worked: "worst coast
miss > 40 km" flags every world, because a warhead entering atmosphere legitimately predicts a large
miss; and a bare `GAVE UP` count conflates the ~90 km event at 8 rockets with a benign trim ending at
32, where the same terminator fires on a 4.96 km miss. **The terminator's name is not the failure.**
Any future scoring has to require the ending *and* the magnitude.

## 3ar. The terminator is the coast being integrated instead of propagated — flown 2026-09-04

Ten worlds on the shipped build, 80 flights, with `22fec05`'s off-gravity probe read for the first
time. Two worlds threw the terminator and both carry the same signature. It is not subtle.

### Three regimes had to come out of the instrument first

Each of them alone reads at or above the ~0.03 m/s the walk needs, on every flight, diverging or
not — so any one left in makes the column report itself:

| excluded | reads | what it is |
| --- | --- | --- |
| each craft's **first** probe | 0.019-0.040 m/s, to 2.42 on a bus | the engine re-fitting the conic when thrust stops |
| any sample with **density** | **230-242 m/s** | a reentering body. Drag, measured correctly |
| **trim** anything but idle | 0.5-4.0 m/s | the bus's own commanded push, leaking in after it stops |

What is left is the pre-split coast, which is where the walk happens.

### Two populations, and nothing between them

| shot | on rails n / max | off rails n / mean / max | % off |
| --- | --- | --- | --- |
| 001-006, 008, 009 | ~750 / **0.0004-0.0010** | **1-11** / 0.0001-0.0003 / 0.0004 | **0-1%** |
| **007** | 229 / 0.0002 | **523** / **2.3810** / 4.1245 | **70%** |
| **010** | 228 / 0.0002 | **524** / **2.3810** / 4.1096 | **70%** |

A divergent world's *on-rails* samples are indistinguishable from a healthy world's. What differs is
that the bus spends **70% of its coast off rails against 1%**, and accumulates ~2.4 m/s per probe of
non-gravitational velocity while it does. The two worlds agree to six figures on the mean
(2.381004, 2.381031) and to four on the median (2.2720, 2.2726), across independent runs — this is
deterministic, not scatter.

The walk on the same samples is +256 to +386 m/s, which is 3an's +163 to +340 signature.
**The mechanism stated here first was along-track and is wrong — see 3as.** It is cross-track, and
the arithmetic that appeared to support it used an amplification 180x too large.

### The onset is one sample, not a ramp

One craft, one line each, 007:

```
00:59:32   840.0 km   miss 3.86 km   rate  +0.4   off-gravity 0.0000   on rails
00:59:34   857.1 km   miss 4.24 km   rate +38.5   off-gravity 1.3195   off rails
00:59:35   873.7 km   miss 8.15 km   rate +386.9  off-gravity 4.1245   off rails
```

Forty-six seconds of flat coast at 0.0000, then **the rails transition and the divergence onset are
the same probe.**

### What this settles

**3aq's H2 was refuted on a test that could not see it.** Off rails was dismissed because 65 of 65
divergent *and* 124 of 126 healthy flights went off rails — a binary per-flight test. The
discriminator is the **fraction of the coast**: 70% against 1%. This is the sixth entry for the
list at the end of this file, and the same shape as the other five — a count read as a mechanism.

**And the premise was already known to be wrong.** 3aq found that the mod drives attitude through
the coast, which commands actuators, which takes the bus off rails; it recorded that as a design
fault and could not connect it to the terminator. This connects it.

### What it does not settle, and two candidates already refuted

**What puts them off rails at that instant is open**, and it is now the whole question — much
narrower than 3ap's "what is common to eight computers at one instant".

- **Not the warp.** No speed change is logged at the onset and frame time is flat across it:
  10.81 / 10.89 / 11.31 ms mean over the three 10 s windows spanning it.
- **Not the attitude error crossing the pointing band.** First crossing of 0.15 deg is
  `00:59:40.8`, **six seconds after** the vehicle is already off rails, and the earlier on-rails
  window reached 0.1349 deg max against the onset window's 0.0979 mean. Comparable either side.

The next probe should log `AnyActuatorCommanded` and `AnyActuatorActive` off `PhysicsBubble`
directly rather than inferring the command from the error.

### The night's other numbers

80 flights, median miss **0.02 km**, spread **0.00 km**, arrival **32.0 deg**, ground well
conditioned (-0.30% downrange slope). Endings: `noimprov` 28 at 0.02 km, `payback` 21 at 0.01,
`clock` 15 at 0.02, **`trim` 16 at 84.04** — all sixteen from the two divergent worlds, at 8 per
world, which is 3ap's all-or-nothing rule holding for a third night.

So 64 of 80 warheads land inside 20 m and 16 land at 84 km. The weapon is a 20 m weapon with a
mode, and the mode now has a mechanism.

**Item 18 was also observed live**: `GeoSat FAT`'s computer disposing rockets 2, 3, 4 and 5's stages
at 00:56:28 of shot 007. `CollectShedStages` is still unbounded.

## 3as. The push is real, it is cross-track, and 3ar's arithmetic was wrong — read 2026-09-05

Three readings against 3ar, each of which changes something.

### The push is a real force, and the probe is sound

| ruled out | by |
| --- | --- |
| **integrator truncation** | `PhysicsStates.ComputeTimestep` caps the off-rails sub-step at **2.0 s**; nothing else binds in vacuum. Integrated on this orbit the 10 s error at h=2.0 s is **3.0e-5 m/s**, and 1e-8 at the step actually flown. Measured is 2.38 — five to eight orders out. Empirically too: the world dropped 5.3x to 1.0x mid-coast and the push carried on the same curve, changing 6% |
| **a different force model off rails** | `ComputeDerivatives` applies the closest parent's `mu*(-rhat)/r^2` minus the same at the bubble origin. No J2, no third body, no SRP. Drag, buoyancy and Coriolis sit behind `InPhysicsRadius` ~ R+210 km, and the bus is at 950-1160 km |
| **a probe artefact** | same craft, same frames, same code: on-rails samples read <= 0.0006 m/s |

What is left in `ComputeDerivatives` is `ActiveNozzle` thrust. 2.38 m/s per 10 s is **0.238 m/s²**
against this bus's own logged **0.539 m/s²** of RCS translational authority — 31-76% of it. **Off
rails and thrusting are the same event**, which is why the rails flag reads as the discriminator.

### The six-figure agreement is a smooth function of state, not a coincidence

007 and 010 are the same scenario twice — same craft names, same 95 probes, trajectories matching to
0.3 km. The push runs 3.54 m/s at 973 km, 1.65 at apogee, 3.51 at 946 km: U-shaped, no scatter. A
mean over a near-identical sweep of a smooth curve is second-order insensitive to the small state
offset. It is evidence the push is a **function of orbital state**, not noise.

### It is cross-track, and 3ar's along-track arithmetic was out by 180x

**`91.5 km per m/s` is wrong for this arc**, and it had propagated into three places in this file
and three comments in `IcbmComputer.cs`. Fitting the conic to the log's own on-rails samples gives
a = 4682 km, e = 0.611, and an along-track amplification of **2.93 km per m/s at 227 km, 2.04 at the
onset, 1.15 at apogee, 0.38 at the end**. That is what `METRE-LEVEL.md`'s own `dMiss/dV` table has
said all along — 415 m per m/s at 30 deg — and 91.5 is 16 to 220 times outside the whole table.

At 91.5, 2.4 m/s per probe would be 220 km per probe. Observed is 1.2.

And the conic barely changes: the in-plane impact anomaly wanders **±3.9 km and returns to
+0.17 km**, `a` moves 4681.92 to 4683.43 km, `e` 0.61058 to 0.60837. Taking the push normal to the
plane instead, `sum push*(R*r/h)*sin(theta)` is **95.2 km** against the observed **83.7 km** of
latitude walk — ratio 0.88, per-probe shape matching, and r = 0.94 across both worlds. A normal
impulse does no work and does not change `|h|`, which is exactly why the energy and angular momentum
stayed put.

**Logging the push as a magnitude is what sent 3ar to the wrong mechanism.** It has to be a vector
in a radial / along / cross basis.

### What the fix is

- **Not "keep it on rails" and not holding timewarp.** Rails is the symptom: KSA goes off rails
  *because* the flight computer is commanding actuators, which is the mod's own continuous attitude
  hold through the coast — the design fault 3aq found and filed. Stop driving attitude once the
  release line is held, the actuators go quiet, and the engine puts the bus back on rails. That is
  what the eight healthy worlds are doing.
- **A second, independent fault:** the imbalance is ~88% lateral, so **a rotation command's nozzle
  set is not summing to zero force**. `tools/model/checkring.py --translation` reads six-axis
  translation authority off the XML; the missing gate is whether a *rotation* command's enrolled set
  has zero net force. Without it, any attitude hold in coast is a thruster.
- **The engine already computes the answer**: `KinematicMeasurements.DeltaVelocityCci` in
  `IntegrateVelocityVerlet` is the non-gravitational delta-v, and `Disturbances.ForceBody` is the
  thrust.

### And the world-load discriminator stands, independent of any of this

3ar's chain is measured from vehicle counts and disposal lines rather than from the probe, so it
survives every correction above: **12-20 vehicles at warp against 9, splitting 10 of 10 worlds with
no overlap.** That is item 18, and it is the root-cause candidate rather than a latent tidiness
fault.

**The disposal count is NOT part of that discriminator, and 3ar said it was.** On the night it read
160 in the two divergent worlds against exactly 167 in the eight healthy ones, which looked like
seven stages going undisposed. Re-flown 2026-09-05 it reads **158, 167, 158 on three healthy
worlds** — straddling the 160 that was supposed to mark a divergent one. It is a count of disposal
*lines*, most of which are duplicate attempts by computers that adopted the same stage, so it moves
with frame timing. **The vehicle count is the signal; the disposal count was a coincidence of one
night.**

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
| ~~3~~ | ~~**Diagnostic**: log the release residual and `_response` per flight~~ | done | `release summary`, read by `shot-report.py`; 3x |
| ~~4~~ | ~~Measure `dMiss/dV` at both flown geometries~~ | done | **the residual is worth 36 m per m/s, not 884** — 3x |
| ~~5~~ | ~~Derive `HoldingCostsMetresPerSecond`~~ | done | 2,000 km: **110 -> 30 m**, 0.28x; 3l-3w |
| ~~5b~~ | ~~Fly `ArrivalPreference` at 0.5/0.65/0.8~~ | done | **0.5 wins, 0.48x, 29.5 -> 13.5 m; 0.8 is a settled loss** — 3aa |
| ~~5d~~ | ~~Re-fly `ArrivalPreference = 0.5` on a clean harness~~ | done | **0.69x [0.17, 0.88], 11 wins of 12, rank p=0.009 — RESOLVED, and 3ak's 1.91x was the harness** — 3am |
| ~~5c~~ | ~~Price a steep arrival against the **trim's** budget, not the ascent's~~ | done | **refuted: the trim's authority *grows* with the angle, 122 km to 166 km — what ends it is the arc ceasing to exist** — 3ag |
| **5e** | Re-check the latched arrival floor against the state the burn **leaves** the vehicle in, not the one it is priced from | 0 shots then 12 | 3ag: 0.5 latches 33.5 deg against a wall at 35-40, and 0.8 latches 53.6 — what 5c became |
| ~~1a~~ | ~~Confirm 3z headlessly~~ | done | **refuted: 0.13 m over KSA's own erosion spectrum** — 3ab |
| ~~1b~~ | ~~Gate `ImpactPredictor`'s step on clearance, not density~~ | — | **dropped** — worth 0.13 m, costs ~120 lookups a prediction (3ab) |
| ~~1d~~ | ~~Price the round's arrival over relief~~ | done | **−5,143 m against its own probe over KSA's erosion, stable to 11 m** — 3ac |
| ~~1e~~ | ~~Name the unaccounted term in 3ac~~ | done | **neither side misreads: they strike different features, and 30 m of trajectory difference becomes 5 km** — 3ac |
| ~~1g~~ | ~~Close the round-probe trajectory gap by converging the round~~ | done | **refuted: worth 19 m of 5,282 over erosion, same hill struck** — 3ad |
| ~~1h~~ | ~~Price the stopping rules' difference in kind~~ | done | **the predictor is exact to 0.46 m; the round stops early, and 2.5 km survives every refinement** — 3ae |
| ~~1f~~ | ~~Measure the damping product against the game~~ | done | **3.6 m below a 1 km wavelength against a ~100 m threshold — the mechanism never fires** — 3af |
| **1i** | Give `ImpactPredictor.pathCci` a companion list of **times**, then re-ask whether the probe's path passes under the ground | 0 shots | 3ad withdrew that measurement; the un-carry needs real per-point times |
| **1c** | Pad or replace `MaxTerrainHeightApprox` at `KsaWorld.cs:374` | 0 shots | the radar mask's containing sphere is not one (3z) |
| **6** | `_worseFor` as a run counter; headless counterfactual over `RoughGround` first | 0 shots then 12 | long range, if `settled` stops being modal |
| **7** | Seed `Resume()` from the burn's last measured response | 12 paired shots | long range; decomposes the pass-one trim demand |
| **8** | `minTargetFrameRate`, `orbitSolvers`, the three offscreen viewports, coast off-rails | hours | **2.4x or better throughput**, which every row above pays for in shots |
| ~~9~~ | ~~Hand the terminal fraction of the burn to `FlightComputer.Burn`~~ | days | **dropped** — abolishing the residual entirely buys ~9 m at 2,000 km and nothing at 12,902 (3x) |
| **13** | **Why the committed arrival drifts 26 s** — one shot in twelve, all eight rockets, 75-99 km, `trim` median 92.41 km | 0 shots then 12 | 3ak: the largest single item at this geometry, and unexplained — **but 3an bounds the drift to 0-8 s across all 52 divergences, so it is not what the `trim` terminator is; see 17** |
| ~~12~~ | ~~Why the epoch sign runs at −0.6~~ | done | **the diagnostic had the sign backwards; the fix was justified and is applied** — 3al |
| ~~2b'~~ | ~~Re-sample the ground per sub-step in the terminal phase~~ | done | **refuted headlessly: 0-2 m on smooth ground, and chaotic rather than convergent on rough (−2,781 m at 22 ms, −7 at 33, −2 at 50). Re-sampling changes which feature the round stops on; it does not converge** |
| ~~15~~ | ~~Log what `DensityRatioAt` returns through the coast~~ | done | **refuted: 0 of 2,009 samples non-zero through 52 divergences — the air and the drag model are cleared** — 3an |
| ~~17~~ | ~~**Why the aim bias walks to 94 km**~~ | done | **the coast is being integrated instead of propagated: the bus spends 70% of the coast off rails against 1% healthy, and accumulates ~2.4 m/s per probe of non-gravitational velocity, which at 91.5 km per m/s is the walk. Replicated in two independent worlds to six figures** — 3ar |
| **18** | **Bound `CollectShedStages` — now the root-cause candidate, not a tidiness fault.** **12-20 vehicles at warp against 9, splitting 10 of 10 with no overlap.** (The disposal count that looked like part of this is not — three healthy worlds read 158/167/158 on 2026-09-05.) That load is what keeps the buses off rails once they trip | 0 shots | 3ar, 3as |
| ~~16~~ | ~~Make each headless fixture state its own arrival geometry~~ | done | **`ArrivalPreference = 0.5` ships as the default; 15 cases across 7 classes now state their geometry through `FixtureGeometry`, 1,854 pass** — 3ao |
| ~~14~~ | ~~A per-craft coast probe~~ | done | **caught the failure: sharp onset at 505 km, accelerating, and the guard proven not to be the cause** — 3am |
| **20** | **Stop driving attitude through the coast.** The hold is what commands the actuators, and the thrust is a real 0.238 m/s² against the bus's 0.539 of authority. Release the hold once the line is held; the engine puts it back on rails | 0 shots then 10 | 3as: ~88% of it is lateral |
| **21** | **Gate a rotation command's nozzle set to zero net force.** `checkring.py --translation` reads six-axis translation authority; nothing checks that a *rotation* set does not translate | 0 shots | 3as |
| **19** | **What puts the bus off rails mid-coast.** Answered in outline: the mod's own continuous attitude hold commands actuators, and off-rails and thrusting are the same event. **`AnyActuatorCommanded`/`AnyActuatorActive` are NOT reachable** — they hang off `Vehicle._threadWorkerUpdateState`, which is private. Log `FlightPlan.ExpiryGameTime` and `BubbleVehicleCount`, both public, and the push as a **vector** | 0 shots then 10 | 3ar, 3as |
| **10** | `AimWithinTrimBudget` to 24 shots, **pre-declared**. 3ah re-ranked it to the top and then the item 11 fix removed the fault it was for, so it is back to being a tuning question — re-rank it once a night has run on the fixed build | 24 shots | 0.85x [0.53, 1.14], the only arm that has never lost |
| ~~11~~ | ~~Do not set an aim bias from a state that has not burnt yet~~ | done | **flown: 8 of 8 within 0.33 km against a worst of 310.42, and every terminator cleared** — 3ah |

**5d is ready to fly, and this is the command.** `--plan-only` clean on 2026-09-02 against
`SOLVER SCALE 8` and HEAD; the arm is a setting rather than a branch, so there is nothing to build
and nothing to check a ref for.

```bash
KSARMORY_SCENARIO_SAVE="SOLVER SCALE 8" ./tools/shot-batch.sh \
    --paired 'base|p50:ArrivalPreference=0.5' --blocks 12 --aim 26.485S,68.148W
./tools/shot-report.py --paired ~/shots/<night>
```

About two and a half hours, so it is a night rather than a session. Swap the aim for a flat one if
the question is the lever rather than the hard target — 7g is the account of what that target does
to a night, and 3ag is why 0.5 is expected to help here rather than hurt.

**8 makes everything after it cheaper** and should come before 5b-7 if a night is short.

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
4. ~~**The seven-degree arrival**~~ — **settled 2026-09-02, and corrected the same day.** The
   2,000 km geometry arrives at 13.6-17.5 degrees and the **12,902 km** one at **12.9**, logged from
   the release summary rather than reconstructed. 3x's 7.1 was a reconstruction and nothing flies it.
   `cot` is 4.37 rather than 8.03 — 3af. The four files that asserted the seven now say what is
   flown: `ARRIVAL-ANGLE`'s floor is labelled a floor rather than a flown figure, `KINETIC-FLOOR`'s
   two columns carry the 0.54x correction on their height-driven terms, `METRE-LEVEL`'s "below twenty
   degrees the residual is irrelevant" is re-priced at the 15-degree rung the mod is actually on
   (29%, not 2%), and `IcbmConfig` states the flown range outright. **The parametric tables were left
   alone**: they are arithmetic at the angle each states and were never the wrong part.
5. **`KSA-TERRAIN.md`: "there is no raycast, no collider query."** `BoundingVolumeHierarchy.LookupBvhDirection`
   is a public ray query, and there is a Bepu triangle collider on a 2 m grid within 8 m of clearance.
6. ~~**`accurate: true` degrades silently**~~ — **read out, and both halves were wrong.** The
   mechanism is real: the modifier loop is bounded by `?.NumModifiers` and a null runs it zero times
   with no log line. But it does not degrade to the *coarse* answer — the base term stays bicubic
   under `accurate: true` — and it is **unreachable in stock content**, because every body with a
   `<Height>` also has a `<MeshCollection>` and is populated once at startup. `SetupModifierRenderData()`
   would be a no-op. Its one live consequence is `MaxTerrainHeightApprox`, computed in the `Celestial`
   constructor before that population, which is why the terrain mask's containing sphere is not a
   bound — 3z.
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
The 20 degree arrival floor (priced against a 7 degree baseline that was already 13.6; the
baseline is now 17.5, so it buys 13% -- 3g). A 25 degree floor is a different matter and is the
one resolved win here -- 0.44x, 3h.
Steepening past ~26 degrees (33 and 41 deg both flew, both unresolved, both erratic; the optimum
is interior -- 3h).
Shortening the range to steepen the arrival (418 km lands 0.36-3.63 km against 2,000 km's 0.10 --
the short flight cannot fit the passes; 3g).
"The clearance never succeeds" (the absence of a log line measured the logger).
The 24 ms slow-regime screen (29.8 ms gave 0 passes one night and 2 another).

The arrival angle driving the probe-to-round gap (rank correlation **+0.90** across eight flights,
steep median 228 m against shallow 65 -- and a controlled sweep holding the release still and
rotating only the flight-path angle is **flat at zero** from 7 to 50 degrees. Eight is enough to
produce a convincing rank correlation from nothing; 3ai).

"A steep arrival is dear for the trim to correct on" (3aa's own mechanism, and the premise of 5c
for a day. The rate is set by the transfer time, so steepening makes the aim *cheaper* to move --
122 km of authority at a graze against 166 at 33 degrees. The reading that looked like a price was a
wall: past some floor the long arc does not exist and a short steep one is flown instead -- 3ag).

"Off rails is not the cause" (65 of 65 divergent flights went off rails and so did 124 of 126
healthy ones -- a binary per-flight test, where the discriminator is the **fraction of the
coast**: 70% against 1%, and the whole mechanism. 3aq refuted it, 3ar found it).

**Six of these six were counts or absences read as mechanisms.** The terminator table is a
diagnosis, not a lever, and an instrument with one output cannot tell a cause from a consequence.

**And one entry sat on this list because the test that put it here was blind.** "Refining the
predictor's integration step" was ruled out by `PredictorStepTests`, which measured convergence with
no terrain passed in — over a mean sphere, which is the one surface a step cannot undersample. 3z is
the reading. The lesson is narrower than the five above and worth stating on its own: **a recorded
negative is only as good as what its instrument was pointed at, and this file should name that for
every entry it carries.**
