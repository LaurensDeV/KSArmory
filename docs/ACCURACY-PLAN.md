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
| median miss | **99 m** (**10 m** at a derived cost and a 33 deg floor, 3t) | **6,664 m** | **150-650 m**, by session |
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

**What is left, unmeasured:** the two sample the same field through *different frames*. The
predictor takes `Vec.Unit(nowCci).Transform(parent.GetCci2Ccf())` and un-carries the point by
`_departsIn` first; the round asks `GetTerrainHeightFromDirCce`. A frame or epoch mismatch between
them is this project's most-repeated fault, `docs/FRAMES-AND-EPOCHS.md` exists for it, and at a 13.5
degree arrival one metre of height is 4.16 m of ground — so the documented spread between surfaces
(mean 4.21 m of height, 44 m at the 99th) is **17 m and 183 m of ground**, which brackets the ~100 m
outstanding and has the same heavy-tailed shape as the flown walk's 10 / 326 quartiles.

That is a suspicion with the right magnitude and the right distribution, and it is **not** a
measurement. The cut is to ask both for the surface radius under one point and difference them.

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
The 20 degree arrival floor (priced against a 7 degree baseline that was already 13.6; the
baseline is now 17.5, so it buys 13% -- 3g). A 25 degree floor is a different matter and is the
one resolved win here -- 0.44x, 3h.
Steepening past ~26 degrees (33 and 41 deg both flew, both unresolved, both erratic; the optimum
is interior -- 3h).
Shortening the range to steepen the arrival (418 km lands 0.36-3.63 km against 2,000 km's 0.10 --
the short flight cannot fit the passes; 3g).
"The clearance never succeeds" (the absence of a log line measured the logger).
The 24 ms slow-regime screen (29.8 ms gave 0 passes one night and 2 another).

**Five of these five were counts or absences read as mechanisms.** The terminator table is a
diagnosis, not a lever, and an instrument with one output cannot tell a cause from a consequence.
