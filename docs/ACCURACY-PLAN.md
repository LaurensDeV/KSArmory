# The accuracy plan, 2026-08-30

**What this is.** One ranked plan, replacing the ranked lists at the end of `EIGHT-ROCKETS.md` and
`METRE-LEVEL.md`, which were written against a state that has since moved and against each other.
Read this first; those two keep their reasoning and their measurements.

**It exists because four investigations landed at once** — the long-range logs, the guidance chain,
the KSA corpus and the backlog itself — and between them they moved the top of the list from "tune a
constant" to "there is a bug, and the engine has a lever nobody used".

## Where it stands after 2026-09-08 — read this first

**The one-line version: the shot is 15 m, and it is two errors of roughly equal size — 10.5 m made
during the fall, which follows the ground, and 7 m made before release, which does not. The aim
correction is finished work and is neither of them.**

Written at the end of a session that produced no improvement in metres and closed several routes.
The four entries to read are **3bz**, **3ca**, **3cf** and **3ce**, and **3cg** is the night they
point at, prepared; 1-4 below are 2026-09-06 and still stand.

**A. The aim loop is converged and is not the limiter (3bz).** Traced over a whole coast on eight
rockets it walks the bias down monotonically — the path it covers equals its net change on every
flight — and settles at 1.4-12.4 m by its own prediction, against an observer exact to 0.46 m. The
landing correlates **+0.07** with that prediction and **+0.93** with the ground under that seat.
Items 24, 25 and 20b are all tuning of this loop and cannot pay.

**B. The miss decomposes, and the halves are different terms (3cf).** 15.0 m total: **10.5 m made
during the fall**, pure downrange at 1.0 m cross-range, correlating with seat roughness at
**+0.74, p = 0.046**; and **7.0 m made before release**, correlating **+0.12, p = 0.793** — nothing.
The second sits exactly on the aim loop's converged residual. **Two budgets, and a metre needs both
under a metre.**

**C. Two levers flown, both null, and one of the nulls is not real.** The improvement band read
0.88x [0.84, 1.10] and the arrival angle 0.96x [0.66, 1.13]. But the angle acts only on the walk,
which is 70% of the miss, so scoring it on the *total* diluted 0.71x to about 0.80x — 3cd could not
have resolved it either way. **Item 29 re-flies it scored on the walk**, which is 3cc's design
finally executable.

**D. A shipped mechanism does not operate (3ca).** `AimCorrection`'s ratchet is arithmetically dead
below 250 m — banking a better aim would take a negative miss — so `Freeze()` ships whichever aim
was current when the miss first fell under 250 m and discards up to **153 m** of walking after it.
Pinned by `AimRatchetTests`. Off by default and unflown at 0.88x, so it is a documented mechanism
not working rather than a known cost.

**E. Three instrument faults, all now fixed, all of which had produced wrong published results.**
A one-block paired run confounds arm with seat and cannot compare arms at all (3by) — two of this
session's own results died on it. The warhead trace was being *stranded* rather than sampled, 4 of 8
finishing, and did not name its craft (3ce): coverage is now 91%, and 3cd's walk figure is void.
The report's terrain check read one aim point of eight, the smoothest, and called it well
conditioned (3cb).

### Read this before the 2026-09-06 header below

## Where it stood after 2026-09-06 — read this before the table below

**The one-line version: the shot is 17 m on a healthy world, and about a fifth of worlds are not
healthy — but that fifth is plausibly this harness rather than the weapon.**

**1. The instrument was deaf and now is not.** A seat is a fixed point on the ground, and seat 3
lands on a hillside: 76-108 m against seat 1's 6-12, reproducibly, across seven nights and many
builds. Arms alternate down the roster, so within one shot the two of them sat on *different
ground* — a deterministic alternation the interval read as scatter, which is why it never shrank
with n. Dividing each seat's level out takes a 14-block night from 9% power at x0.60 to **90%**, and
it re-reads `2026-09-03-1544` — a night filed as answering nothing — as a clear x2.36 loss. `--paired`
also now splits the two modes and tests the count with Fisher, which is what SHOT-PROTOCOL.md has
prescribed since 8s and nothing computed.

**2. Four of the plan's own top rows died on measurement.** The `clock` terminator is the *best*
ending at 15 m, not a cut-off loop at 1.92 km — the pooled figure was another arm's broken flights
wearing the label (3bc). 5e is refuted: the exit reaches steeper than the latch can afford, and the
arrival ceiling is the trim's debt instead (3be). Item 8's "2.4x for a config line" is bought
directly out of that same trim precision (4b). Item 21 was already built and already green (4c).

**3. QuietCoast is settled enough to stop flying.** Harmless on healthy worlds, twice measured. On a
divergent one it does not change *whether* a world is lost, only how badly — 85.84 km to 50.35 —
which is 4 of 5 divergent worlds in favour (3bf, 3bh, 3bi). Item 20's stated mechanism is wrong,
though: a bus that verifiably stopped commanding attitude stayed off rails at an unchanged rate, and
the two actuator flags now read `neither` on every off-rails probe of both arms (3bg).

**4. The catastrophic mode is a missed staging census, not the pad spacing.** Divergence is
**inherited from the ascent** — 100% of divergent flights are already bubble-merged at their first
coast probe, 100% of healthy ones never merge at all — and what keeps that bubble alive is debris
left in the world: **6 of 10 worlds carrying debris into the coast diverged, 0 of 55 without,
p=2.5e-6**. The trigger is staging synchrony, **182 ms spread divergent against 5 ms healthy**, one
rocket missing the single census pass that would have had a neighbour dispose its stack. **3bn**.

Widening the pad spacing is *not* indicated: the rockets never come within the 4.194 km split
radius, and the eight-rocket worlds come closer while diverging less. A lone rocket did fly 20 of 20
clean at **10 m median, 30 m worst** — so **metre-level resumes from 10 m rather than 17** — but
against a clean eight-rocket subset that difference is **p=0.145 and not significant** (3bn corrects
3bl).

**5. And the divergence tracks the target, not the code.** 0 of 24 shots at 2,000 km, 8-20% at
6,269 km, with the change falling on the day the target moved rather than on any commit. The bubble
envelope grows with the coast, so the long shot has time to reach a neighbouring pad and the short
one does not. **A player firing one ICBM has no neighbour**, so the catastrophic mode is plausibly a
property of the eight-rocket throughput harness. One cheap test settles it — `SOLVER SCALE 1`, same
target — and a wider `--spacing` then takes it out of every future night. **3bj**, and it is the next
thing to do.

**What is actually in the way of metre-level**, once that is cleared: the healthy mode is ~17 m at a
32 degree arrival, rung C wants ~5 m at 45-60, and what stops the angle is the trim's debt — 2.6 m/s
owed at 44 degrees against 4.19 at 54. That is **5f**, and it is the first thing after the harness.

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

## 3at. The stage census fires once, a frame too early, and adopts the neighbours — 2026-09-05

Bounding `CollectShedStages` at 10 km was flown and **regressed the world**: peak vehicles 24 -> 57,
disposal lines 167 -> 32. Reverted in `4e9b207`. Chasing why gave the real diagnosis.

### Almost nothing a computer disposes of is its own

Counted by name, since KSA suffixes a split product `_N` onto its parent's Id:

| | own stage | another rocket's |
| --- | --- | --- |
| 2026-09-04, four worlds | **0** | 668 |
| 2026-09-05, twelve worlds | **15** | 635 |

**About 2%**, and the minimum distance to any disposed stage is 19.2 km — the pad spacing in this
save. So the bound removed 98% of the disposal work, which is exactly what the flight showed.

### Why: one chance, on a one-frame assumption

`CollectShedStages()` runs at `IcbmComputer.cs:515`, near the top of `Update`. `_awaitingStage` is
set at `:684`, near the bottom, immediately before `VehicleCommand.Stage`. So the snapshot is taken
in frame N and the census runs in frame N+1 — and clears `_awaitingStage` on that single pass,
whether or not anything of this craft's was found.

The comment states the assumption outright: *"the stage lands a frame later through the engine's
input buffer and the difference is what identifies what came off."* When the decouple takes longer
than one frame the vehicle does not exist yet, this craft's own stage is missed **permanently**, and
whatever the neighbours dropped inside that window is adopted in its place. Eight rockets staging
within moments of each other is what makes the world look tidy at all.

**Flown 2026-09-05, and this section's mechanism is REFUTED.** One rocket on `ICBM E2E`: three
stagings, **four disposals, all of its own stages** (`GeoSat FAT_1/_2/_3`), at **1.0 km**. The census
finds its own stage perfectly well, and the one-frame window is the same in both worlds, so the
window is not what fails.

**What actually happens with eight rockets is a race, and the owner loses it.**
`StageDisposal.MayDispose` measures clearance from the *disposing* craft. A neighbour that adopted
the stage is already 20 km from it, so its gate is open immediately; the owner has to wait for its
own 1 km of separation. The neighbour disposes it first, which is why 98% of disposals read as
foreign and why nothing is disposed nearer than the 19.2 km pad spacing.

So **disposal is not failing** — it is being done early, by the wrong computer, and logged against
it. That is a real attribution fault and it is not a stage-retention fault, which is what the rest of
this section assumed.

### What the fix has to do

Both halves, and either alone is wrong:

- **Keep looking**, bounded by frames rather than fixed at one, until something new appears; and
- **take only what is near**, which is what the reverted bound did.

The bound alone removed the accidental mechanism. Waiting alone would adopt more of the
neighbourhood, not less.

## 3au. The terminator is a shared physics bubble, and the engine has no way out — flown 2026-09-05

Twelve worlds on the vector probe. **Three diverged, nine did not, and the discriminator is exact.**

| | GAVE UP | probes sharing a bubble | off rails | max push | cross-track share |
| --- | --- | --- | --- | --- | --- |
| nine healthy | 0 | **0-1** | 0-1 | 0.0003-0.572 | -- |
| **006** | 16 | **538** | 537 | 4.206 | **0.90** |
| **009** | 16 | **531** | 531 | 4.205 | **0.90** |
| **011** | 16 | 519 | 519 | 4.084 | **0.90** |

The three push vectors agree across independent worlds with different bubble sizes (16, 23, 16):

```
006   r -1.661   a +0.801   c +3.780
009   r -1.662   a +0.801   c +3.779
011   r -1.618   a +0.766   c +3.671
```

So the push is a function of the trajectory, not of how many neighbours there are. Endings:
`noimprov` 37 at 0.03 km, `payback` 19 at 0.02, `clock` 16 at 0.01, **`trim` 24 at 88.64** -- 24 of
96 flights, which is 3 worlds of 12 at 8 rockets each.

### Sharing a bubble is NOT being off rails

> **Corrected in 3ax — 2026-09-05.** `PhysicsBubble.cs:1340`'s `NumVehicles < 2` gates the
> **`ConstraintSim`**, not rails. The rails decision is per vehicle at `:1239` and reads
> `anyActuatorCommanded || ...`. Measured: the three divergent worlds are shared **and on rails** for
> their first 224-241 probes, from 227 km. Sharing is harmless until an actuator is commanded, and
> what commands one is this mod's own attitude hold. The 538-of-538 below was measured on the
> filtered set, which drops exactly those early probes.

### It is direction, not magnitude, and shot 007 is why that is a finding

Shot 007 **passed** while sharing a bubble:

```
006  FAIL  bubble 16  push 4.206  c +3.780   cross-track 90%
007  PASS  bubble  2  push 0.572  c -0.026   radial 93%
```

A cross-track impulse moves the impact with an enormous lever and barely touches the conic; a radial
one does not. So `bubble > 1` is necessary and **not sufficient**, and shot 006 alone would have said
otherwise.

### And it is persistence, which the engine cannot undo

007 shared for **one probe** and recovered. The divergent worlds shared for the whole coast, and that
is structural:

> **Wrong, and corrected in 3aw — 2026-09-05.** `RemoveEligibleVehicles` is not the only exit.
> `VehicleUpdateTask.SplitBubbles()` splits a bubble on distance through
> `PhysicsBubble.CollectSplitClusters(scratch, 2.0)`, at `2.0 x Origin.GetRecommendedRadius()` =
> **4.194 km** here. There is no ratchet. What follows was read off `RemoveEligibleVehicles` alone.

### What that means for the fix

Nothing on the mod's side can un-merge a bubble, so the only lever is to **stop the merge happening**
— which means whatever is bringing vehicles close enough to merge. **That was attributed to item 18
and the attribution is withdrawn:** the one-rocket shot shows disposal works, and the three divergent
worlds disposed 153-160 against 158-167 healthy, which is the same range. Something else is putting
vehicles in one bubble, and finding it is the open question.

### The flight-plan hypothesis is dead

3ar's leading candidate, and the column was added to test it. The margin never approaches zero: 394 s
in a divergent world against 495-948 healthy, three orders clear. Refuted.

## 3av. Twelve clean worlds, and the rate itself is the instrument problem — flown 2026-09-05

The same save, the same aim, the same baseline, a build differing only by a log column and a
formatting fix. **0 of 12 diverged, against 3 of 12 six hours earlier.**

### What the weapon is when the terminator does not fire

96 flights, 32.0 deg arrival, 6,269 km:

| | |
| --- | --- |
| median | **0.02 km** |
| mean | **0.03 km** |
| worst of 96 | **0.11 km** |
| endings | `noimprov` 42, `payback` 39, `clock` 15 — **no `trim`** |

Every warhead inside 110 m. Against a mean of 16.95 and 22.04 km on the two nights that had
divergences, this is what the terminator costs and what sits underneath it.

### The healthy baseline for the bubble probe

Twelve worlds, ~753 filtered coast probes each: **zero bubble sharing**, and the nearest other
vehicle a steady median of **9.55 to 10.86 km**. Any divergent world now has something to differ
from.

### And the rate moves between sessions on identical code

The healthy populations of the two nights are the same to the digit — disposals median **167** both,
peak vehicles **24 against 23**. So nothing about the baseline shifted; only whether the event fired.

**A fix for the terminator therefore cannot be validated by comparing divergence counts between
nights.** The noise is the size of the effect. It has to be flown paired, with the fix and the
control on different rockets in the *same* world, which `Sim/ShotArms.cs` already supports and which
`SHOT-PROTOCOL.md` argues for on the miss distance for exactly this reason.

### Coast step is not the trigger either

`shot-report.py` reported a pooled coast step of 30.3 ms against 107.1 the night before, which
looked like the world running at a third of the warp — and `BubbleMergePredicate` merges on closest
approach predicted over `AnalyticHorizonFrames = 4.0`, so a longer step is a longer look-ahead and
more merges. It is an artifact: the column pools *samples*, and a shot at a 30 ms step contributes
about 3.5x as many per second of simulated time, so four such shots outweigh eight at 105 ms.

Per shot there is no separation at all:

```
divergent   90.8  102.0  110.8  105.2  107.6
healthy     21.6 ... 113.0
```

**A pooled median over samples is not a median over shots**, and this is the second time today a
count has been read as a mechanism.

## 3aw. The bubble exit is a distance after all, and it is 4.194 km — 2026-09-05

3au said bubbles merge by distance and never split by it, reading
`PhysicsBubble.RemoveEligibleVehicles` — which does only release on a parent or frame change. It is
not the only exit.

`VehicleUpdateTask.SplitBubbles()` runs every step and calls
`PhysicsBubble.CollectSplitClusters(scratch, 2.0)`, which clusters on

```
2.0 x Origin.GetRecommendedRadius()
GetRecommendedRadius() = max(2097.152, 9.313e-10 x |PositionBub|)
```

At this altitude the floor binds, so the split radius is **4.194 km**. A vehicle beyond it leaves.

### The rule is exact

Across three healthy worlds, 2,653 coast probes: **242 had a neighbour within 4.194 km, and 242 of
those 242 were sharing a bubble.** No exceptions either way.

That also resolves why the filtered reader showed `shared 0/753` on the same worlds: those 242 probes
are dropped by the trim-idle filter. Healthy worlds **do** share a bubble, briefly, immediately after
separation — nearest **0.010 km**, which is the just-dropped stack — and split out once it has drifted
past 4.194 km.

### So the trigger is whether the dropped stack gets clear

The bus's own discarded half is the one vehicle guaranteed to start 10 m away, and
`StageDisposal.MayDispose` refuses to remove it — `watchedByTheClearance` is a hard `false`, on the
safety argument that the trim must be able to measure a real distance to it. So it is the natural
candidate for what stays inside 4.194 km, and whether it drifts clear before the coast is under way
is set by the decoupler impulse and by what the trim does afterwards.

**Not yet confirmed on a divergent world**, because the `nearest` column was added after the last
one. The prediction is sharp and cheap: in a divergent world the nearest vehicle stays inside
4.194 km for the whole coast and is the dropped stack, and in a healthy one it passes outside within
a minute or two of separation.

### And it explains the healthy worlds' numbers

Median nearest 9.55-11.73 km, p10 4.42 km — sitting just outside the split radius. The population is
mostly *other rockets*, which is why a lone rocket also shares briefly and recovers: it has only its
own stack to shed.

## 3ax. The bubble is not the fault — the attitude hold inside it is — 2026-09-05

Three corrections converge on one fix, and it is already item 20.

### Rails is gated on actuators, not on bubble membership

`PhysicsBubble.cs:1340`'s `NumVehicles < 2` guards the `ConstraintSim`. The rails choice is per
vehicle at `:1239`:

```csharp
else if (anyActuatorCommanded || flag || flag2 || flag3 || flag4)
    newStates.Props.SetOnRails(isOnRails: false);
```

Flown, in all three divergent worlds of 2026-09-05-1348:

| shot | shared **and on rails** | shared and off rails | first shared-on-rails |
| --- | --- | --- | --- |
| 006 | **224** | 550 | 227 km |
| 009 | **233** | 587 | 227 km |
| 011 | **241** | 548 | 227 km |

The blob exists from 227 km and costs nothing for the first two hundred probes. **What ends the free
ride is a commanded actuator**, and the mod drives attitude through the whole coast to hold the line
the warheads leave along — the design fault 3aq found, filed, and could not attribute.

### The merge itself is unavoidable and is a 197 m race

`EvaluateLinear` merges two solo craft only within `5R + 2` with `R = 2 x EnvelopeRadius ~ 10.6 m`,
so **55 m** — there is no kinematic route to a 20 km merge. The route is that
`ComputeMergeStateCore` gives a multi-member bubble an envelope equal to *the spread of its own
members*, so a rocket holding its shed stage `s` away reaches `5s`. The pads here are **20.04 km**
apart, so the reach touches the neighbour at `s = 4.00 km` — against a split radius of **4.194 km**.

A **197 m band**, and `MergeBubbles` runs before the step while `SplitBubbles` runs after it, so
inside the band the merge always gets first refusal. Once joined it seals: the remainder envelope is
20 km, which demands over 100 km of clearance, and it cascades 20 → 40 → 140 km down the pad line.
That is the 15-16.

**So preventing the merge is not the lever.** It is a geometric coincidence of the launch site, it
would return with any pad spacing, and the engine gives no way to refuse it.

### What that makes the fix

**Stop commanding attitude during the coast** — item 20, and now the whole of it. The bus keeps its
release line by *pointing*, and pointing is what commands the thrusters. If the hold is released once
the line is held, the actuators go quiet, the engine keeps propagating the conic it was already
propagating for 224 probes, and the push never happens.

### And there is a third bubble exit if that is not enough

`Vehicle.Teleport(Orbit, null, null)` is public and calls `RemoveFromCurrentBubble()`
(`Vehicle.cs:2208, :2233`). Passing the vehicle's own `Orbit` is geometrically a no-op that
re-orphans it, and `IntakeOrphans` then re-tests with a **solo 10.6 m envelope** and gives it its own
bubble. It costs a full `ComputeCompleteTrajectory`, so it wants a gate rather than a per-frame call.
`GetDesiredBubFrame` is *not* a lever — it reads the bubble origin, so every member computes the same
answer.

### Keeping the spent stages does NOT force it — flown 2026-09-05

The obvious stressor was to stop disposing spent stages, on the argument that a stack separating at
about a metre a second stays inside the 4.194 km split radius for the whole coast. One shot with
`DisposeSpentStages` off, and disposal genuinely suppressed (0 lines against ~160):

| | |
| --- | --- |
| bubble | **1**, all 288 probes |
| rails | on 287, off 1 |
| nearest vehicle | **10.93 km** |
| cross-track push | 0.0002 m/s |

**No sharing, no push, nothing.** `DisposeSpentStages` governs the *ascent* stages, which are dropped
at 19-140 km and left far behind as the bus climbs to orbit; they were never candidates for a
4.194 km neighbour. The setting moves the wrong vehicles.

Worth stating because it also cuts the other way: an undisposed stage is not what merges a bubble,
which is the third piece of evidence against the stage census being implicated at all.

**A first attempt at this measured nothing at all** — the flag was passed as an environment variable
to a Windows process launched from WSL, which does not survive, as `ScenarioRunner.Requested`'s own
doc comment says three lines above where it was read. It travels on the scenario file now.

### The diagnostic, if the trigger is still wanted

`Vehicle.BubbleLeader` and `Vehicle.NearbyVehicles` are both public, and `NearbyVehicles` is the
membership by name. It has to be logged **from launch**: the blob is already present at the first
coast probe in all three divergent worlds, so nothing after cutoff can watch it form.

## 3ay. Paired works, and the first QuietCoast was half a fix — flown 2026-09-05

Twelve worlds, `base|quiet:QuietCoast=true`, four rockets an arm in every world. **One diverged**
(shot 008), median 0.02 km, mean 7.77 km, range 0.00-98.44.

### The instrument problem is solved

The eleven healthy worlds put the two arms on top of each other — 375-384 filtered coast probes an
arm, 1-6 off rails, `max|c|` 0.0001-0.0002 — which is the right null: in a world where nothing
merges there is nothing to quiet. The one divergent world is a **complete experiment on its own**,
because both arms sat in the same bubble, the same warp and the same world.

That is what the 3av problem needed. A between-night count could not see a fix through a rate that
ran 2/10, 3/12, 0/12, 1/12 on identical code; a within-world split does not care about the rate at
all.

### And the fix did nothing

| shot 008 | probes | off rails | shared | max \|c\| |
| --- | --- | --- | --- | --- |
| base | 380 | 282 | 380 | 3.8637 |
| quiet | 376 | **276** | 376 | **3.8649** |

Indistinguishable.

### Why, and it is not the mechanism

The gate fired. The quiet craft reads **`aimed=False`**, so `AttitudeHook.Hold` really was skipped.
Its flight computer still reads **`Auto/Custom`**.

Dropping the standing aim only stops this mod *writing* a target. KSA's computer keeps the one it
has and goes on firing thrusters to hold it, so `anyActuatorCommanded` stays set and the vehicle
stays off rails. **The hypothesis was untested, not refuted**, and a between-night comparison would
have recorded a failed fix and moved on.

`AttitudeHook.Quiet` now calls `VehicleCommand.ReleaseAttitude` every frame — Manual, None, target
cleared — inside the `PrepareWorker` window, because a write from anywhere else is discarded before
anything reads it. That window is the reason the hook exists, and it had been used for pointing but
not for stopping.

### One thing to watch when it re-flies

Shot 005, a healthy world, had the quiet arm alone take a 0.4318 m/s push on two probes where base
took none. Two probes of 381 is nothing on its own, but there is a mechanism that would make it
real: quieting trades frequency for magnitude, and `ReacquireCoastDeg = 2.0` lets the bus drift two
degrees before the hold takes it back — a far larger slew, and a far larger impulse, than the
continuous small corrections it replaces. If the corrected fix reduces off-rails probes without
reducing the push, that is the reason to look at first.

## 3az. QuietCoast fixes the divergence and wrecks everything else — flown 2026-09-06

> **Read this first: the fix as flown must not ship.** It helps the fifth of worlds that diverge and
> is **89x worse** on the four fifths that do not. Healthy worlds, 44 rockets an arm: base median
> **0.018 km**, quiet median **1.599 km**, quiet max 4.16 km against base's 0.11.
>
> The cause is a misreading of `ReleaseSequence`, which waits for the vehicle to be **steady** — and
> steady is not *pointed*. A bus drifting slowly is perfectly steady while aimed somewhere wrong,
> and the warheads leave along that line. `CLAUDE.md` states it directly: after cutoff the bus keeps
> the line the warheads leave along. Letting go of the attitude for the whole coast throws that away.
>
> **What it needs is to stop being quiet well before the release approach**, with time to re-point
> and re-settle on the committed line. `QuietDuringCoast` excludes `_salvoAway`, which is *after* the
> warheads are gone and far too late.
>
> **Built as `Sim/CoastQuiet.cs`, 2026-09-06. Not flown.** See 3bb.

## 3ba. QuietCoast works, is worth 0.73x, and is not the whole fault — flown 2026-09-06

The corrected fix (`AttitudeHook.Quiet` cancelling the attitude rather than merely not writing it)
flew paired, and its first divergent world separates the arms perfectly.

### Shot 003, four rockets an arm

| craft | arm | miss |
| --- | --- | --- |
| FAT 5 | **quiet** | **45.41 km** |
| FAT 7 | **quiet** | 62.29 |
| FAT 3 | **quiet** | 70.25 |
| FAT | **quiet** | 70.27 |
| FAT 8 | base | 76.72 |
| FAT 6 | base | 90.32 |
| FAT 2 | base | 92.32 |
| FAT 4 | base | 92.87 |

**Every quiet rocket beat every base rocket.** A perfect rank separation at 4 v 4 is p = 1/70 =
**0.014** one-sided, from one world — which is the whole point of the paired design. Median 66.3
against 91.3 km, **ratio 0.73**.

| | probes | off rails | max \|c\| |
| --- | --- | --- | --- |
| base | 376 | 270 | 3.8738 |
| quiet | 376 | **144** | **3.0066** |

And the mechanism is confirmed engaged: `aimed=False` now reads **`Manual/None`** where the first
version read `Auto/Custom`, 2,584 probes of shot 001 against 7,100 pointed.

### Three divergent worlds, and it is a dose-response

| shot | base off rails | quiet off rails | base miss | quiet miss | ratio |
| --- | --- | --- | --- | --- | --- |
| 003 | 270 | 144 | ~91 km | ~66 km | 0.73 |
| 005 | 273 | 140 | ~89 km | ~64 km | 0.72 |
| **006** | 265 | **0** | ~87 km | **~13 km** | **0.15** |

Shot 006's quiet arm went **completely silent** — zero off-rails probes, zero push — and landed
8.56 / 12.86 / 13.66 / 16.21 km against the base arm's 79.88 / 85.12 / 88.68 / 93.53.

**Every quiet rocket beat every base rocket in all three worlds.** Three independent perfect
separations at four a side is p = (1/70)^3, and the mechanism numbers replicate to two significant
figures across 003 and 005.

The dose-response is the strong part: partial quieting buys 1.4x, complete quieting buys 6.7x, and
what varies between them is exactly the off-rails count. **Driving off rails to zero is the target,
and 006 proves it is attainable.**

### It is not the band, and it is not re-acquisition

The obvious reading was that 003 and 005 kept drifting past `ReacquireCoastDeg = 2.0` and slewing
back. **They did not.** The gate behaves identically in all three worlds:

| shot | quiet arm: aimed | quiet | re-acquisitions |
| --- | --- | --- | --- |
| 003 | 1860 | 1691 | **0** |
| 005 | 1858 | 1700 | **0** |
| 006 | 1860 | 1692 | **0** |

One clean transition into quiet at the coast and never back, in every case. **There are no
re-acquisitions to widen a band against**, so the wide-band arm is not worth flying.

### What separates them is the trim, and the sign is backwards

| shot | `trim: trimming` lines | quiet arm off rails, trim idle |
| --- | --- | --- |
| 003 | **0** | 144 |
| 005 | **0** | 140 |
| 006 | **1467** | **2** |

**The world whose quiet arm went silent is the one where the trim ran.** The two that stayed at
~140 are the ones where the trim never ran at all.

So something commands actuators while the attitude is cancelled and the trim is idle, and it happens
in exactly the worlds where the trim never ran. `PhysicsBubble.cs:1239`'s remaining live condition is
`AnyActuatorActive()` — a nozzle physically firing rather than commanded — and the next question is
what is holding one open on a bus that is neither pointing nor trimming.

Even fully silent the shot lands at 13 km rather than the 0.02 km a healthy world gives, so the
shared bubble costs something beyond the push this fix removes. A separate question, and a much
smaller one.

Even fully silent the shot lands at 13 km rather than the 0.02 km a healthy world gives, so the
shared bubble costs something beyond the push this fix removes. That is a separate question and a
much smaller one.

### The numbers on the partial worlds

Off rails nearly halved, the push fell 22%, the miss fell 30%. **The attitude hold is one
contributor and about half the off-rails is something else.** The trim is already excluded by the
filter, so it is not that.

`PhysicsBubble.cs:1239` has five conditions, not one:

```csharp
anyActuatorCommanded || AnyActuatorActive() || <ocean> || KeyframeAnimationModule.AnyAnimating(..)
    || KittenWantsWake(..)
```

Ocean is impossible at 900 km. The two candidates are `AnyActuatorActive()` — a nozzle still firing
after the command stops — and the re-acquisition this fix builds in: `ReacquireCoastDeg = 2.0` lets
the bus drift two degrees and then slews it back, and that slew is off rails for as long as it
lasts.

**The next arm to fly is a much wider reacquire band**, which is the direct test of the
frequency-against-magnitude trade flagged in 3ay. If the release line does not in fact need holding
through the coast — `PostBoostAim` and `ReleasePointing` re-point before the warheads leave — then
the band can be very wide and the coast can be genuinely silent.

## 3bb. The quiet window is bounded at both ends — built 2026-09-06, NOT FLOWN

`Sim/CoastQuiet.cs`. Two bounds, and 3az/3ba between them say why each is needed:

* **Quiet begins only once the post-boost correction has finished.** `BusTrim` resolves onto the
  vehicle's *own control axes*, taking the attitude to **be** the release line — so a bus left to
  drift between passes thrusts along stale axes. That is the mechanism behind 3az's other half,
  which the commit body did not name: quiet ended on `clock` in **55 of 56** flights against 8 for
  the control, i.e. the correction loop stopped converging altogether. Steady-is-not-pointed
  explains the release; it does not explain that.
* **Quiet ends `QuietCoastEndsBeforeReleaseSeconds` (60) before the release approach**, which is
  3az's own prescription.

**Neither bound costs much of what the quiet is for.** Measured off 2026-09-05-2339's own logs: the
coast to release runs **~980 s** ("944 s since the aim last read" at a probe near release), and the
correction is over inside `PostBoostAim.MaxSeconds` = 120. So ~80% of the off-rails exposure is
still quiet, against 100% for the version that cost 89x.

**And re-pointing is nearly free**, which is the part worth not re-deriving: quiet means no actuator
commanded, which means *on rails*, which is exact conic propagation. The bus does not move while it
is quiet — the drift is attitude and nothing else — so taking the line back has no trajectory to
undo. A slew at `Strict`'s 30 deg/s is seconds even from the far side.

The predicate moved to `Sim/` because it is the thing that broke and `Ksa/` cannot be tested.
`CoastQuietTests` has 15 cases; the four that describe the bounds were checked failing against the
version that shipped.

**What to fly.** `QuietCoast=true` paired against base, scored with the mode split — the pooled
median cannot express this arm and never could. Two pre-registered endpoints:
the lost-mode rate (Fisher, base ran 12 of 56) and the healthy-mode median (base 0.017 km). The
claim is that it keeps 3ba's effect on the first and is a null on the second.

## 3bc. The `clock` terminator is not a failure — measured 2026-09-06

**It reads as one only because a broken arm wore the label.** 2026-09-05-2339's pooled terminator
table says `clock` n=63 median **1.92 km** against `noimprov`'s 0.02, which invites exactly one
conclusion: the loop is being cut off mid-convergence by `PostBoostAim.MaxSeconds = 120`, against a
`ReleaseBeforeArrivalSeconds` window of 420 it already owns. Raise the budget and more flights
converge.

**That is wrong.** 55 of those 63 are the `quiet` arm, whose correction loop was broken by the
unbounded quiet window (3bb) — not flights that ran out of time. Split the same terminator table by
arm and take **baseline behaviour only**, over 560 flights with a named ending across every night
2026-09-03 to 09-05:

| ending | n | median km | p90 km | share |
| --- | --- | --- | --- | --- |
| `noimprov` | 215 | 0.022 | 0.08 | 38% |
| `payback` | 133 | 0.016 | 0.06 | 24% |
| **`trim`** | **120** | **84.684** | **97.67** | **21%** |
| `clock` | 92 | **0.015** | 0.05 | 16% |

`clock` is the **best** of the four. A flight that corrects for its whole budget and is stopped by
the clock lands at 15 m; the three ending rules differ by 7 m between them and none of that is worth
a night.

**So there is one term and it is `trim`** — 21% of flights at 84.7 km, three thousand times
everything else, and 3ax/3ba have its mechanism. Nothing else in the terminator table is a lead.

**And this is the seventh entry for the pattern list.** A count was read as a mechanism: the label
was real, the median under it belonged to something else, and the fix it implied would have cost a
night to learn nothing. The rule that catches it is to split every pooled table by arm before
reading a mechanism out of it — which is also why `--paired` now prints the mode split per arm.

## 3bd. The correction occupies the whole coast, not the start of it — flown 2026-09-06

3bb bounded the quiet window at both ends. One paired verification shot says the **first** bound
makes the feature a no-op, and the diagnostic that says so is the reason it was shipped with it.

| hold state | probes |
| --- | --- |
| `holding (correcting)` | **417** |
| `holding (release approach)` | 12 |
| `holding (off the line)` | 0 |
| **`quiet`** | **0** |

The assumption was that the post-boost correction occupies the *first* ~120 s of an ~980 s coast,
leaving ~80% of it quiet. `PostBoostAim.MaxSeconds = 120` says so and the endings agree — one craft
logged "released after 120 s of correcting". **But those 120 seconds are not spent at the start.**
The coast runs at 100x and the loop cannot take passes there; it takes them once the warp ends for
the release approach, at 1x. So the correction finishes *inside* the approach — after the point the
second bound has already taken the line back — and `_postBoostSaid` is false for the entire warped
coast.

Both arms landed at 0.02 km, so the shot also confirms the second bound alone does no harm on a
healthy world, which is what 3ba destroyed (3.607 km there).

**So `QuietCoastAfterCorrection` becomes a setting, default off.** What ships is 3az's own
prescription and nothing more: quiet through the coast, gated by `TrimIsFiring` as before, and back
under command 60 s before the release approach. The correction bound is kept as a switch because
the concern behind it is real and untested — the trim is already excluded while it *fires*, and
whether it also needs the line held *between* passes is one arm of a night rather than something to
bake in.

**The lesson is about where a budget is spent, not how large it is.** `MaxSeconds = 120` was read as
"the first 120 seconds". A simulated-time budget inside a warped phase is spent wherever the warp
lets the loop run, which here is the far end. Nothing in the code says otherwise and nothing would
have caught it but the flight.

### Verified with the bound off — flown 2026-09-06

| arm | off rails | quiet | endings | misses |
| --- | --- | --- | --- | --- |
| base | 6% | 0% | clock 1, noimprov 3 | 13, 20, 21, 22 m |
| **quiet** | **4%** | **80%** | clock 1, noimprov 2, payback 1 | 5, 31, 42, 74 m |

Three things this establishes, none of which is that the fix works — one shot settles nothing:

* **The window engages, at exactly the ~80% of the coast it was designed for.**
* **The mechanism moves**: off rails 6% to 4%, which is the term 3ax identified.
* **The correction loop is intact.** Its endings are ordinary, against 3ba's `clock` on 55 of 56 —
  so whatever broke the loop there is not present here.

The arms sit on opposite seat parities and the quiet arm drew the bad half, so the raw miss
comparison is the terrain rather than the arm. That is what the seat levelling is for and what 14
blocks are for. All 8 arrived; the mod's log has no exception and KSA's has only its own
master-server ping.

## 3be. 5e is refuted — the exit reaches steeper than the latch can afford

Item 5e was to re-check the latched arrival floor against the state the burn *leaves*, on the
reading that no arc satisfying a steep floor exists from there. Measured headlessly
(`ArrivalFloorRestateTests`), bisecting on arc **existence** rather than on affordability:

| shot | affordable at latch | existence wall at cutoff |
| --- | --- | --- |
| 2,736 km | 67.8 deg | **77.8** |
| 6,269 km | 56.2 | **62.2** |
| 12,902 km | 37.5 | **40.9** |

**The wall is above the ceiling at every range.** The post-boost state reaches steeper arcs than
the latch state can pay for, so the latched floor is never the binding constraint and re-checking it
against the exit would *raise* the floor rather than lower it. `ArrivalFloorAffordabilityTests`
agrees from the other side: every floor from 0 to 30 deg is Reachable in the rig, with the arrival
tracking the floor once it binds and no fallback anywhere.

So 5e comes off the plan, and with it the standing explanation for why
`ArrivalPreference = 0.8` loses.

### What ends the control instead, from the flown night

2026-09-01-2148, re-read with the mode split and the seat levelling:

| arm | arrival | **owed m/s** | healthy med | lost | levelled ratio |
| --- | --- | --- | --- | --- | --- |
| base | 16.9 | 2.63 | 0.029 km | 0/24 | — |
| p50 | 34.2 | 2.56 | 0.013 | 0/24 | 0.56x [0.24, 1.52] |
| p65 | 44.4 | 2.60 | 0.017 | 0/24 | 0.68x [0.51, 1.14] |
| **p80** | **54.4** | **4.19** | **0.098** | 3/24 | **5.65x [3.88, 12.65]** |

**One column moves and it is the trim's debt** — what was still owed when the warheads left. It sits
at 2.6 for three arms and jumps to 4.19 at 0.8. The damage is in the *healthy* mode, 0.029 to 0.098
km; it is not the divergence, which is 3 of 24 at Fisher p=0.234.

The terminators say the same: p50 and p65 end `noimprov` **24 of 24** with no `clock` and no
`payback`, which is every flight converging. p80 ends 18 `noimprov`, 3 `clock`, 3 `trim`.

**So the arrival ceiling is the trim's ability to finish paying, somewhere between 44 and 54 deg.**
That is a measurement to make, not a fix to build, and it is worth making only after 20b: a steeper
floor is a longer transfer, and a longer coast is more exposure to whatever the coast is doing to
the bus.

### And a caveat on the seat levelling, from this same night

p50 levelled reads [0.24, 1.52] where un-levelled reads [0.26, 1.12] — **wider, not narrower**, the
only case measured that goes the wrong way. Four arms over eight seats is two flights per seat per
arm per shot, so each seat's level is estimated from a quarter of the data a two-arm night gives it
and the noise in the level outweighs the terrain it removes. **Levelling is for two-arm nights**,
which is what `--paired` is for and what the protocol already recommends; on a four-arm night read
the un-levelled line.

## 3bf. The bounded quiet window is safe, and its one divergent world showed nothing — flown 2026-09-06

> **Superseded in part by 3bh.** The healthy-world half stands: the 89x regression is gone. The
> conclusion that it does nothing for the divergence was drawn from **one** divergent world, and
> the next one flown gives 4 v 4 perfect separation. Read 3bh before acting on anything below.

14 paired blocks, 112 flights, `2026-09-06-1413`. Both pre-registered endpoints, and a third
reading that matters more than either.

**Endpoint 2 passes: the regression is gone.** `quiet vs base 0.97x [0.79, 1.16]`, 7 of 14 shots,
signed-rank p=0.542 — a tight null, healthy medians 0.015 against 0.018 km. 3ba's 89x on healthy
worlds was the missing release bound and nothing else, and 3bb's bound removes it completely.

**Endpoint 1 fails: it does not touch the divergence.** `4/56 lost against 4/56, Fisher p=1.0000`.
The night drew **one** divergent world of 14, which 3bb predicted would be unresolvable on the count
alone — but the world it drew answers the question outright, because the fix had no effect *inside*
it.

### Shot 005, the divergent world: quiet does not mean on rails

All eight rockets landed 88.3-103.2 km out, four of each arm. Per craft:

| craft | arm | probes | off rails | quiet |
| --- | --- | --- | --- | --- |
| GeoSat FAT | quiet | 96 | 70 (**72%**) | 85 (88%) |
| GeoSat FAT 2 | base | 96 | 70 (**72%**) | 0 |
| GeoSat FAT 3 | quiet | 96 | 70 (**72%**) | 85 (88%) |
| GeoSat FAT 4 | base | 96 | 69 (**71%**) | 0 |

**The quiet rockets were quiet for 88% of the coast and spent exactly as much of it off rails as the
ones that were never quiet at all.** Cross-tabulated on one craft: **59 probes off rails *while
quiet***, 26 on rails while quiet, 11 off rails while holding.

### What that refutes

Item 20's mechanism, as stated: *rails is gated on `anyActuatorCommanded`, and it is the mod's own
coast hold that commands it.* The mod stopped commanding — verified, 88% of the coast — and the
vehicle stayed off rails at an unchanged rate. **So this mod's attitude hold is not what holds a
bus off rails in a shared bubble**, or is not the only thing that does.

`PhysicsBubble.cs:1239` takes a vehicle off rails on `anyActuatorCommanded || AnyActuatorActive()
|| ...`. The first term is now eliminated in flight. The remainder is where the cause is, and
nothing here has looked at it.

That also re-frames 3ba, which read as a dose-response between off-rails fraction and miss. The
correlation stands; the causal direction does not follow from it, and the one world that reached
near-zero off-rails there did so for a reason other than being quiet.

### What to do

**The next thing is a diagnostic, not an arm.** Log *which* of `PhysicsBubble`'s conditions holds a
vehicle off rails, per coast probe. Every candidate after `anyActuatorCommanded` is untested, and
three nights have now been spent on a term that turns out not to be the one.

`QuietCoast` stays built and stays **off**: it is verified harmless and verified useless, so there
is nothing to ship and nothing to revert. If the real cause is later removed, it costs nothing to
re-ask whether the hold matters on top of that.

## 3bg. The off-rails diagnostic, and a third of it is not the actuators — flown 2026-09-06

19b, built and verified. `KsaWorld.OffRailsActuator` reads the two flags `PhysicsBubble` tests
first, off the `_threadWorkerUpdateState` the mod already reflects for part failures, and the coast
probe names which one holds it.

One shot, healthy world, 860 coast probes across 8 rockets:

| rails state | probes |
| --- | --- |
| on rails | 826 |
| off rails **(commanded+active)** | 21 |
| off rails **(neither actuator flag)** | **11** |
| off rails (active) | 2 |

The 21 are the trim firing, which genuinely commands actuators and is not a fault. **The 11 are the
finding**: neither flag is set and the vehicle is off rails anyway, so on a healthy world a third of
the off-rails time is already something else.

### The remaining terms, and which one it must be

`PhysicsBubble` tests, in order: `_forceOffRails`, then
`anyActuatorCommanded || AnyActuatorActive() || ocean || animating || KittenWantsWake`, then
`Freefall && FreefallNeedsFullPhysics`, then `Origin.HasAnalyticPrecisionDanger()`.

Ruled out by inspection for a coasting bus: it is not in an ocean; `KittenWantsWake` requires
`IsKitten`, an EVA character; and `HasAnalyticPrecisionDanger` is
`PositionBub.Length() / 2^52 > 0.0005`, i.e. a bubble origin past **2.2e12 m** — billions of
kilometres, not an Earth orbit.

That leaves `FreefallNeedsFullPhysics`, and it fits the healthy number arithmetically.
It is true when the patch **ends in Impact** and its end time falls within
`2 x SimStep.DeltaTime + 50/closing + boundingSphere/speed` of the step's end — and a ballistic
missile's patch *always* ends in impact. While the bus is still ascending the closing rate clamps to
1 m/s, making the lead ~50 s, plus 2 sim steps which at 100x warp is ~6 s. **So the last ~56 s of an
~980 s coast is off rails by construction: 6%, which is the healthy off-rails fraction measured all
week.**

### What that predicts, and how it gets tested for free

If the divergent world's 72% is also `neither actuator flag`, the cause is
`FreefallNeedsFullPhysics` and the lever is the warp — `2 x SimStep.DeltaTime` is the only term in
that lead the mod controls, and at 100x it is worth ~6 s against ~50. If instead it reads
`commanded` while the mod is quiet, the standing suspect is `FlightComputer.ZeroizeTvcs`, which sets
`AnyActuatorCommanded` when it finds a gimbal command that is not exactly zero — so zeroing a stale
gimbal counts as commanding one.

**No night needs to be spent on this.** The diagnostic is in the coast probe, so the next night
flown for any reason answers it the first time a world diverges — which is about one world in seven.

## 3bh. 3bf was wrong: the fix wins 4v4 on the next divergent world — flown 2026-09-06

**Correcting 3bf.** It concluded "safe and useless" from a single divergent world. The very next one
flown says the opposite, and says it as cleanly as this instrument can.

`2026-09-06-1730` shot 003, one world, four rockets an arm:

| craft | arm | miss | off rails |
| --- | --- | --- | --- |
| GeoSat FAT | quiet | **52.9 km** | 38% |
| GeoSat FAT 2 | base | 96.9 km | 71% |
| GeoSat FAT 3 | quiet | **53.0 km** | 38% |
| GeoSat FAT 4 | base | 81.7 km | 66% |
| GeoSat FAT 5 | quiet | **47.8 km** | 38% |
| GeoSat FAT 6 | base | 77.6 km | 66% |
| GeoSat FAT 7 | quiet | **43.8 km** | 38% |
| GeoSat FAT 8 | base | 90.0 km | 72% |

**Every quiet rocket beats every base rocket** — 4 v 4 perfect separation, p = 1/70 = 0.014, the
same shape 3ba measured three times. Off-rails halves (38% against ~68%) and so does the miss
(~49 km against ~86).

### So the divergent worlds disagree with each other, and that is the finding

| night | world | quiet fraction | off rails | result |
| --- | --- | --- | --- | --- |
| 3ba | three worlds | ~100% | reduced | perfect separation, 144/144 |
| 3bf | `1413` shot 005 | **88%** | **72%**, same as base | nothing, 4/56 v 4/56 |
| 3bh | `1730` shot 003 | **89%** | **38%** against 68% | perfect separation, 4 v 4 |

The two bounded-window worlds went quiet by the same amount — 88% and 89% — and one came back on
rails while the other did not. **Four of the five divergent worlds ever measured show the fix
working.** 3bf's single world is the outlier, and reading a flat "useless" off it was drawing a
conclusion at n=1 that this project's own protocol exists to forbid.

### What the flag says, and what it does not

Every off-rails probe in shot 003 reads `neither actuator flag` — **in both arms**, base included.
So at the instants sampled, the actuators are not what holds either arm off rails.

**That does not eliminate them, and the gap is sampling.** A coast probe is one reading every 10
simulated seconds and the rails decision is remade every sub-step, so a command that is brief
between probes is invisible here. What the reading does establish is that the *persistent* term is
something else, and that the quiet arm's residual 38% is entirely non-actuator.

By elimination that residual is `FreefallNeedsFullPhysics` — a ballistic patch always ends in
Impact, and the lead is `2 x SimStep.DeltaTime + 50/closing + r/speed`. What it does not yet explain
is why quieting moves the *total* from 68% to 38% when neither arm ever shows a flag set. Sampling
is the likely answer and the way to settle it is to read the flags at sub-step rate rather than at
probe rate.

### What to do

**Keep flying this batch.** It is 14 blocks and has drawn one divergent world in three; the
inconsistency between 3bf's world and this one is exactly what more of them settles, and every one
now carries the flag reading for free.

`QuietCoast` stays off until that is settled — but it is no longer "verified useless". It is
verified harmless on healthy worlds (3bf's 0.97x [0.79, 1.16] stands, and is the useful half of
that entry) and verified to win on four of five divergent ones.

## 3bi. It halves the damage without preventing it — `2026-09-06-1730`, 13 usable shots

The night 3bh's world came from, complete. One divergent world in thirteen, and the pre-registered
endpoint turns out to have been the wrong one.

| | base | quiet |
| --- | --- | --- |
| overall ratio | — | 0.95x [0.73, 1.67], unresolved |
| healthy median | 0.017 km | **0.016 km** |
| **lost rate** | 4/52 | **4/52**, Fisher p=1.0000 |
| **lost median** | **85.84 km** | **50.35 km** |
| off rails | 5% | 5% (quiet 79% of the coast) |

**The rate does not move and the magnitude does.** QuietCoast does not stop a world diverging; it
roughly halves the miss once one has — which is exactly 3bh's 4 v 4 seen through the mode table
rather than craft by craft.

**So the pre-registered endpoint was wrong, and it was wrong for a defensible reason.** 3bb declared
the lost-mode *count* because that is what 3ba's unbounded version moved: 12 of 56 to 1 of 56. The
bounded version does not move the count at all. That is a real behavioural difference between the
two versions and not a measurement artefact — being quiet for ~100% of the coast changed whether a
world was lost, being quiet for ~80% only changes how badly.

Across every divergent world ever flown: **four of five favour the fix** — 3ba's three, 3bh's one —
with `1413` shot 005 the lone exception.

### The session was in the slow regime, and it was the operator's other game

Median frame time **33.3 ms** for the night, against 23.3-29.2 for the first six shots. The machine
was running Counter-Strike: Source alongside from about shot 007, and the shot durations say so
too: 750, 760, 911, 777, 819, 900, 753, 903 s against 660-693 before.

**The paired comparison is not threatened by that** — both arms fly one world, sharing the frame
trace, the warp history and the solver load, which is the whole reason this instrument is paired.
What it costs is sensitivity, and it makes shots 7-14 poor company for 1-6 in any absolute reading.

**And it is now the leading candidate for why the two divergent worlds disagreed**, because they
fall on opposite sides of this project's own regime boundary of 24 ms:

| world | frame time | regime | result |
| --- | --- | --- | --- |
| `1730` shot 003 | **23.3 ms** | fast | quiet wins 4 v 4 |
| `1413` shot 005 | **26.5 ms** | slow | quiet does nothing |

`SLOW_FRAME_MS`'s own note records 0.23-0.25 correction passes per flight in the slow regime against
1.17-3.38 in the fast one, and an arm acting on the post-boost loop cannot be measured where the
loop does not run. **Not written up as a finding** — it is n=1 either side, and reading a mechanism
out of a count is the mistake this file already lists seven times. It is the first thing to check on
the next divergent world, and it costs nothing to check.

## 3bj. The divergence is very likely the harness, not the weapon — 2026-09-06

Read the divergence rate against what each night actually flew, over every batch since 2026-08-30:

| target | range | baseline divergence |
| --- | --- | --- |
| `10.622,-80.604` | 2,000 km | **0 of 24 shots** |
| `26.485S,68.148W` | 6,269 km | ~8-20% of shots, every night |

**Ninety-six shots before 2026-09-01 with no divergence at all**, then a fifth of them ever since.
That looks like a regression and is not one. Two candidates were tested against the existing logs
and both are refuted:

* **A code change in the window.** The three failures of `2026-09-01-2148` were **all the `p80`
  arm** — the steepest arrival, already a settled loss. No baseline flight diverged until the target
  moved.
* **The arrival preference shipping at 0.5** (`de9f81c`, 09-03 09:29). Divergence appears on the
  09-02 nights, *before* it shipped, and there the divergent worlds fail with **base and p50
  together, all eight rockets, at preference zero**. The angle is not the driver.

What changed on 2026-09-02 is the **target**, from a 2,000 km shot to a 6,269 km one — and with it
the coast, from a few minutes to ~980 s.

### Why that is the mechanism rather than a coincidence

3ax's account is a bubble envelope that grows with the spread of its own members: a rocket holding
its shed stage `s` away reaches `5s`, and touches the neighbouring pad at `s = 4.00 km` against a
4.194 km split radius. **The envelope grows with time, so a longer coast is more chances to touch.**
A 2,000 km shot never gets there; a 6,269 km one does, about a fifth of the time.

### The consequence, which is the point

**A player firing one ICBM has no neighbour to merge with.** `make-scaling-save.py` puts the rockets
20 km apart precisely so they "own bubbles and take the single-vehicle path" — that is its own
comment — and 20 km turns out to be too close for this coast. So the ~15% catastrophic mode is
plausibly a property of **the eight-rocket throughput harness**, not of the weapon, and the last
week of QuietCoast work has been aimed at an artefact of how the measurements are taken.

**This is a hypothesis with one cheap test and a large payoff.** Fly the same 6,269 km shot on
`SOLVER SCALE 1` — one rocket, no neighbour — several times. No divergence there confirms it. Then
regenerate the harness save at a wider spacing and the failure mode leaves every future night,
which is worth more than fixing it: a fifth of every night currently measures the harness.

**What it does not do is make QuietCoast pointless.** It is measured harmless on healthy worlds and
worth about half the miss on divergent ones, and if a player can ever produce a shared bubble —
two launches, a station, a spent stage held — it still earns its place. It stops being urgent.

## 3bk. Every target under ~800 km gets the same shot — measured 2026-09-06

Reported from play as the computer wanting to go orbital first even for a close target. It does not
go orbital, and what it does is worse: **it flies the identical trajectory for every target from
100 km to 800 km.**

Flown headlessly on `IcbmFlightTests`' own pad rig, varying only the aim:

| range | cutoff | speed | climb | % of circular | burn | left |
| --- | --- | --- | --- | --- | --- | --- |
| 100 km | 65.4 km | 3024 m/s | 30.3 deg | 38% | 92 s | 11,427 kg |
| 200 km | 65.4 | 3024 | 30.3 | 38% | 92 | 11,427 |
| 300 km | 65.4 | 3024 | 30.3 | 38% | 92 | 11,427 |
| 450 km | 65.4 | 3024 | 30.3 | 38% | 92 | 11,427 |
| 600 km | 65.4 | 3024 | 30.3 | 38% | 92 | 11,427 |
| 800 km | 65.4 | 3024 | 30.3 | 38% | 92 | 11,427 |
| 1,200 km | 86.9 | 3391 | 30.4 | 43% | 105 | 9,702 |
| 2,500 km | 138.0 | 4531 | 31.9 | 58% | 128 | 5,922 |
| 6,269 km | 238.8 | 6224 | 30.9 | 80% | 169 | 2,181 |

**Identical to every digit** below 800 km, then scaling normally above 1,200. What each of those
shots actually wants is not close to the others — the cheapest arc is 1,697 m/s with a 73 km apogee
at 300 km, and 2,372 m/s with 146 km at 600.

### Three candidates tested and excluded

* **The release gate.** `DeployAltitudeMetres = 100 km`, and a short arc apogees below it — 73 km at
  300 km range — so the vehicle must loft past it to be allowed to let go. Lowering it to 20 km
  changes **only the hold message**; the burn is unchanged.
* **The arrival floor.** `ArrivalPreference = 0` moves the 2,500 and 6,269 km shots and leaves
  100-800 km identical.
* **The pitch schedule.** `TurnEndMetres` from 55 km to 15 km moves the numbers about 6% and keeps
  them identical across the whole short range.

### What is left, and it is structural

**The handover.** Closed-loop guidance cannot fly the first minute — its answer near the pad is
"point downrange", which through thick air is flying the stack into its own slipstream — so the
ascent is an open-loop schedule and guidance takes over on dynamic pressure. By the time the air is
thin the stack is at ~65 km doing ~3 km/s, which is **already more than any of these shots needs**.
There is nothing left for the loop to decide, so it cuts off at once and every short target gets
whatever the ascent happened to deliver.

The floor is the ascent, and the ascent is flown before anything consults the target.

### What would fix it, in rough order of honesty

1. **Throttle the ascent on the solution.** The programme knows the required velocity from the pad —
   `BallisticArc.TryCheapest` answers at any time — so the open-loop phase could fly a lower
   throttle or a shorter first burn when the shot is small. This is the real fix and it is the one
   that changes the ascent.
2. **Refuse the shot.** A stack sized for 6,269 km is the wrong weapon for 100, and saying so is
   better than silently flying a 3 km/s lob at a target 100 km away. Cheap, honest, and no help to
   anyone who wants the shot.
3. **Leave it and document the floor.** The mod's own reach readout would then have a lower bound as
   well as an upper one, which it does not today.

**Nothing here is built.** `ShortRangeAscentTests` is the measurement and the record.

## 3bl. A lone rocket never diverges — flown 2026-09-06, 20 of 20

> **Its significance is withdrawn by 3bn.** The p=0.033 was computed against a pooled
> eight-rocket rate contaminated by other builds, aims, arms and two bad batches. Against a clean
> subset N=1 against N=8 is **p=0.145, not significant**; only N=1 against N=2 survives. The
> 20-of-20 clean flights and the 10 m median stand as measurements.

3bj's test, and it comes back clean.

| | 8 rockets, this target | **1 rocket** |
| --- | --- | --- |
| shots | 153 | **20** |
| divergent | 24 (**15.7%**) | **0** |
| median miss | 0.017 km healthy, 85 km lost | **0.010 km** |
| worst shot | — | **0.03 km** |
| shared-bubble probes | 18,048 | **0** |
| off-gravity max | 4.2333 m/s | **0.0011 m/s** |

**Zero divergent worlds in twenty**, against a rate of 15.7% established over 153 shots at the same
target: `p = 0.033`. And the two columns that carry the mechanism go to nothing — no shared
bubble at all, and the non-gravitational push falls by a factor of ~3,800.

**The frame-time confounder is ruled out rather than assumed.** 20c's worry was that a lone rocket
runs faster frames and that, not the missing neighbour, is what saves it. It does not: **30.5 ms**
here against 30.1 and 33.3 on the two eight-rocket nights. Same regime, no divergence.

### What this means

**The ~15% catastrophic mode is the test harness, not the weapon.** It needs a neighbouring vehicle
to merge bubbles with, and a player firing one ICBM has none. Every night since 2026-09-02 has spent
about a fifth of its flights measuring `make-scaling-save.py`'s 20 km pad spacing.

**And the shot without it is 10 m, worst 30, all twenty inside 35 m** — "too tight to measure the
ground", which is the terrain check giving up for the first time. That is the number to quote for
what the guidance actually does: the healthy eight-rocket median of 17 m carries the seat spread,
and seat 1 — the base aimpoint, which is what a lone rocket flies — reads 7-10 m across every night.
The two agree exactly.

### What follows

1. **Regenerate the harness save at a wider `--spacing`.** The flag exists. This removes the failure
   mode from every future night rather than fixing it, which is worth more.
2. **Re-read QuietCoast in that light.** It is a fix for an artefact — still harmless, still worth
   half the miss when a bubble *is* shared, and no longer on the critical path.
3. **Metre-level resumes from 10 m, not 17.** Rung C wants ~5 m, and the gap is the trim's debt at a
   steep arrival (5f), not the divergence.

## 3bm. One neighbour is enough — flown 2026-09-07, 0/20 against 8/20

> **Read 3bn first.** The 1-against-2 contrast holds (p=0.0033). The explanation offered here —
> `NumVehicles < 2` putting a merged pair off rails — is **wrong**, and the mechanism is debris
> left in the world by a missed staging census. The 6.32-against-4.23 push comparison is also an
> artefact of sampling the same declining profile 180 s apart.

The other half of 3bl. Same target, same build, same night, varying only how many rockets share the
world.

| rockets | divergent | rate | shared-bubble probes | off-gravity max |
| --- | --- | --- | --- | --- |
| **1** | **0/20** | **0%** | **0** | **0.0011 m/s** |
| **2** | **8/20** | **40%** | 3,008 | **6.3218 m/s** |
| 8 | 24/153 | 16% | 18,048 | 4.2333 m/s |

**Adding a single neighbour 20 km away takes divergence from 0% to 40%** — Fisher exact
**p = 0.0033**. The two-rocket batch's misses run to **162 km** with a mean of 63.50, so it is the
same failure and not a milder one.

**A neighbour is necessary, and one is sufficient.** That settles 3bj: the catastrophic mode belongs
to the harness, and `make-scaling-save.py`'s 20 km spacing is what produces it.

### Two rockets are worse than eight, which is worth not glossing over

40% against 16%, with a larger push (6.32 against 4.23 m/s). Fewer vehicles diverging *more* is the
opposite of what a "more neighbours, more merging" reading predicts, so the mechanism is not simply
proximity count. `PhysicsBubble` needs `NumVehicles < 2` for the rails path, so a pair that merges
puts **both** members off rails, where eight may form several bubbles of which only some merge.

**Not established, and not needed for the decision.** The 1-against-2 contrast is what the fix rests
on and it is unambiguous. This is filed as the reason not to assume the eight-rocket rate is the
worst case — a two-rocket save is the harsher instrument, which matters if anyone reaches for one
to go faster.

### The fix

Regenerate the harness save with `--spacing` well past the envelope. The envelope is `5s` for a
rocket holding its stage `s` away, so the spacing has to beat five times the largest stage
separation before disposal, not five times the split radius. **Measure it rather than guessing**:
fly two rockets at increasing spacings until the rate goes to zero, then take the next step up.

Until that lands, **a lone rocket is the honest instrument** — 20 of 20 clean, 10 m median, 30 m
worst — at the cost of the throughput the eight-rocket save was adopted for.

## 3bn. Correcting 3bl/3bm, and the real mechanism — 2026-09-07

Four investigations over the engine source, the flight logs, the disposal code and the comparison
itself. **The headline of 3bl does not survive; something better does.**

### 3bl's significance was computed against a contaminated pool

`p = 0.033` came from 0 of 20 against a pooled eight-rocket rate of 24/153. That pool is **12+
builds over five days, three aim points and about ten experimental arms**, and it contains
`1730` shot 002 (`CONTAMINATED.md`), the aborted `1331`, and a roster half of which flew
`QuietCoast`, which the tree itself records as a regression. Every one of those inflates the
eight-rocket rate.

Against a clean comparable subset:

| comparison | Fisher p | |
| --- | --- | --- |
| N=1 0/20 vs **N=2 8/20** | **0.0033** | real |
| N=2 8/20 vs N=8 pure-base 5/34 | 0.0505 | borderline |
| **N=1 0/20 vs N=8 pure-base 5/34** | **0.145** | **not significant** |

**So N=1 and N=8 are indistinguishable at this n, and N=2 is the outlier.** "A lone rocket never
diverges" is supported against two rockets and *not* against eight. 3bl and 3bm are corrected
accordingly, and the conclusion that the mode is "the harness" is downgraded to a hypothesis that
its own evidence does not yet carry.

**And the two batches were flown back to back, not interleaved** — 23:03 to 02:11, then 02:11 to
05:27 — which is exactly the comparison `SHOT-PROTOCOL.md` forbids, on a baseline that has read
14.49 km and 5.43 km three hours apart on identical code.

### What does survive, and it is much stronger

**Divergence is inherited from the ascent, not acquired during the coast.** Over 55,747 coast
probes: **100% of divergent flights are already merged at their first coast probe** (median 19 of
the world's 20 vehicles), and **100% of healthy flights start at bubble 1 and stay there**. No probe
in the whole corpus ever shows a merge happening mid-coast, and the merged count only falls
afterwards as members burn up.

**What keeps the ascent bubble alive is debris left in the world**, and at N=8 the discriminator is
exact: **6 of the 10 worlds that carried debris into the coast diverged; 0 of the 55 that did not.
Fisher p = 2.5e-6.**

**And the trigger is staging synchrony.** In a clean world all eight stage-2 commands land within
**5 ms** — one frame — so the next census pass holds every new stack, each computer adopts a
neighbour's, and 167 disposals fire at once. In a divergent world one rocket stages **171 ms** late,
misses that single pass (`_awaitingStage` is cleared on one pass, `IcbmComputer.cs:1047-1049`), and
the seven fragments its stack later breaks into are never claimed by anyone. Median stage-2 spread:
**182 ms divergent against 5 ms healthy**; every world at or above 100 ms diverged and every world
below it was clean.

This also inverts item 18: **the census adopting the neighbours is what makes disposal work.** 167
disposals a world at N=8, 99% of them foreign at a median 58.8 km, against 14 a world at N=2 where
there is only one neighbour to adopt. Stage survival goes as `(1-f_own)(1-f)^(N-1)` with `f ~ 0.43`
— exponential in the rocket count, in the *helpful* direction. That is why N=2 is the worst of the
three, and it is a better account than 3bm's `NumVehicles < 2` paragraph, which 3ax had already
corrected and which should be read as stale.

### Two things this kills

**Widening `--spacing` is probably not the fix.** The rockets never come within the 4.194 km split
radius in either population — minimum `nearest` is 8.79 km at N=2 and 5.19 km at N=8 — and the
eight-rocket worlds come *closer* while diverging *less*. What decides it is what is left in the
world at cutoff, not how far apart the pads are.

**"Two rockets are worse because a merged pair puts both off rails" is wrong.** Everything gated on
`NumVehicles` in `PhysicsBubble` is one-versus-many; nothing changes at 3, 4 or 8. Every
count-dependent term found points the other way — a contagious envelope, an unsplittable remainder,
and a *finer* sub-step with more pairs.

### A candidate for the push itself, worth checking

`PhysicsStates.ComputeDerivatives` puts the centrifugal and Coriolis terms inside
`if (environment.InPhysicsRadius)`, which is **per vehicle**, while the bubble frame is taken from
**vehicle 0 only** — the heaviest member, re-sorted every step. So a member above the atmosphere in
a bubble whose leader is still below it is integrated in a rotating frame **with the rotating-frame
accelerations switched off**. The deficit is `2w x v + w x (w x r)`: about 0.44 m/s^2 at 3 km/s and
1.0 at 7 km/s, which over a probe interval is the metres per second actually observed. A lone
rocket cannot reach this state because it *is* vehicle 0 — matching its 0.0011 m/s reading.

**Inferred from the source, not measured.** It predicts the push should scale with the member's own
speed and not with neighbour count, which the logs independently confirm: at matched probe index the
push is 3.91 against 3.83, 2.25 against 2.07, 1.81 against 1.58 for N=2 against N=8 — ratios of
1.02 to 1.15. **The 6.32-versus-4.23 headline was the same declining profile sampled 180 s apart**,
not a larger push.

### And two real defects found on the way

* **A neighbour can adopt and destroy another craft's separation stack.** `watchedByTheClearance` is
  `ReferenceEquals(stage, _separatedFrom)` on the *disposing* computer only, so craft A destroys
  craft B's stack at 20 km where B's own gate would have refused. B's clearance then reads NaN and
  falls to its blind clock — the exact failure `StageDisposal`'s own doc comment says the rule
  exists to prevent.
* **A live neighbouring bus can be adopted**, observed twice in `e602bea`'s own message at 39.9 and
  79.9 km, six minutes before those buses released.

### What to fly next

**Not the rate.** 40% against 10% needs ~20 shots an arm to reach p~0.06, about seven hours.

**Fly the held-frame fraction instead**, which is already logged and separates PASS from FAIL with
zero overlap in every population (N=2 PASS 10.9-23.2% against FAIL 33.0-48.2%). Four shots each of
`SOLVER SCALE 2` and `SOLVER SCALE 8`, **interleaved within one session on one build** — about 90
minutes, and it settles whether the N=2 excess is the rocket count or the session.

## 3bo. 5f answered: the trim is asked for 4.5x more, not failing to deliver — 2026-09-07

The arrival ceiling. 3be found the trim's debt at release jumping 2.6 to 4.19 m/s between a 44 and a
54 degree arrival, and left open whether the trim was being asked for more or failing to pay it.
Read off `2026-09-01-2148`'s own logs, per arm:

| arm | arrival | **owed at the split** | p90 | ceiling refusals | left on the bus when refused |
| --- | --- | --- | --- | --- | --- |
| base | 16.9 deg | 0.550 m/s | 1.10 | 0 | — |
| p50 | 34.2 | 0.610 | 0.78 | 0 | — |
| p65 | 44.4 | 0.760 | 0.94 | 0 | — |
| **p80** | **54.4** | **3.410** | **5.35** | **3** | **21.74 m/s** |

**The demand is 4.5x larger at 54 degrees than at 44**, and p80 is the only arm that ever hits
`BusTrim.MaxMetresPerSecond = 10.0` — three flights refused outright with a median 21.74 m/s still
owed. So this is not a delivery failure and widening the ceiling is not the fix: 21.74 m/s is more
than twice a ceiling that `TrimCeilingFromBudget` is already on the Dead list for widening.

Note the shape: 0.55, 0.61, 0.76 across 17, 34 and 44 degrees is nearly flat, then 3.41 at 54. **The
ceiling on the arrival angle is a cliff, not a slope**, and it sits between 44 and 54 degrees.

### And the debt is accumulated during the clearance wait, not at the split

The trim's own line says it: `owed 0.45 m/s at the split, 2.61 after 5 s of clearing`. `MayFire` is
false until `SeparationClearance` is satisfied, so the bus drifts off its solution while it waits
for the spent stack to get clear, and the trim inherits whatever that drift came to. That is why a
number measured "at the split" is the wrong one to fix and the growth rate is the right one.

### What it does not yet say

**Why the demand is larger.** The decoupler shove is the same at any arrival angle, so the extra
must come from the cutoff state or from the clearance wait. The obvious candidate is that a steeper
arrival costs more delta-v, so the burn runs longer, the stack is lighter at cutoff, and the last
frame adds more — `residual = accel x step x throttle`, and `accel` is the term that grows.

**It cannot be split from the logs as they stand**: the cutoff line is `CAPTURE cutoff: residual ...`
and does not name its craft, so the residual cannot be attributed to an arm in a paired night. One
line — naming the craft the way the post-boost line already does — makes this a free reading on the
next night that flies more than one arm.

### What this means for the ladder

Rung C wants 45 to 60 degrees. **44 degrees is affordable and 54 is not**, and the boundary is the
trim's demand rather than the arc's existence (3be) or the ascent's budget. So the next rung is not
bought by asking for a steeper floor; it is bought by making the post-cutoff state cheap enough to
correct at one. The two levers that follow are the cutoff residual itself and the clearance wait,
in that order.

## 3bp. 5g, half of it headless: the cutoff residual is a minor term — 2026-09-07

3bo left the trim's 4.5x demand at a steep arrival split between two candidates: a lighter stack at
cutoff, or the clearance wait. The first needs no flight — `IcbmProgram.ResidualAtCutoff` is public
and the flight rig runs the real program. `SteepCutoffResidualTests`, 2,000 km, varying only the
preference:

| pref | arrival | **residual at cutoff** | burn | propellant left | accel at cutoff |
| --- | --- | --- | --- | --- | --- |
| 0.00 | 34.7 deg | 0.061 m/s | 120 s | 6,989 kg | 24.2 g |
| 0.50 | 36.3 | 0.067 | 121 | 6,963 | 24.2 |
| 0.65 | 47.4 | 0.061 | 131 | 5,538 | 29.4 |
| **0.80** | **58.7** | **0.097** | 150 | 3,535 | **41.8** |

**The mechanism is confirmed and it is small.** A steeper arrival does burn longer, leave a lighter
stack and cut off at a higher acceleration — 24.2 g to 41.8 — and the residual tracks it: 1.59x
against the acceleration's 1.73x, which is `accel x step x throttle` behaving exactly as written.

**But it is a third of what is needed.** The flown demand rises 4.5x (0.760 to 3.410 m/s) where the
residual rises 1.59x, and in absolute terms the residual is 0.06 to 0.10 m/s against a demand of
0.76 to 3.41. **The cutoff residual is a minor term in the trim's debt at every angle.**

### It is not the clearance wait either — measured, and the inference was wrong

The obvious next candidate was the drift while `MayFire` is false, since the trim's own line reads
`owed 0.45 m/s at the split, 2.61 after 5 s of clearing`. **Measured per arm, it goes the other
way:**

| arm | arrival | at split | after clearing | growth | wait | drift rate |
| --- | --- | --- | --- | --- | --- | --- |
| base | 16.9 deg | 0.550 | 2.660 | 4.84x | 17.0 s | 0.124 m/s per s |
| p50 | 34.2 | 0.610 | 2.600 | 4.26x | 12.0 | 0.166 |
| p65 | 44.4 | 0.760 | 2.630 | 3.46x | 14.0 | 0.134 |
| **p80** | **54.4** | **3.410** | 4.700 | **1.38x** | 18.0 | **0.072** |

The steep arm drifts **slowest** during clearance and grows least. The clearance wait adds a similar
*absolute* 1.3 to 2.1 m/s to every arm; it is not what separates them.

**The whole difference is already present at the split**, and neither term measured so far accounts
for it. The cutoff residual is 0.097 m/s against 0.061 — 1.6x. The bus is lighter, so the same
decoupler impulse gives a bigger kick, but that is the same 1.7x mass ratio. **1.6x and 1.7x do not
make 6.2x** (3.410 against 0.550).

So what happens between cutoff and the split is the open question, and it is a narrow one: a few
seconds, one decoupler event, and a debt that arrives six times larger than the arms either side of
it can explain.

**5g's other half still needs a flight**, and it is now cheap: the cutoff line names its craft
(`2d28003`) and `shot-report` reads it per craft, so the next paired night with arrival arms gives
the flown residual per arm for nothing — and this entry predicts it will be small.

## 3bq. Every divergent shot is a trim over its ceiling — and the lever is already built

5h, traced to the end. Two findings, and the second makes the first actionable.

### The trim ceiling is the common final step of every divergence

`ReleaseAnArrivalTheTrimCannotFly` (`3d9cb55`) logs `arrival released on ...` when the trim is asked
for more than `BusTrim.MaxMetresPerSecond` having spent nothing. Counted against divergent shots,
per night:

**13 of 14 nights match exactly** — 0 and 0, 1 and 1, 3 and 3, 8 and 8. The single exception is
`2026-09-02-1508`, the first night after the guard landed. Across 20 single-rocket shots: 0 releases
and 0 divergent. Across 20 two-rocket shots: 8 and 8.

**So every divergent world is a trim asked for more than its ceiling, and the recovery fires and
does not save the shot.** That is a far tighter identification than the bubble census — one event,
already logged, on every affected flight.

### Three candidates measured and eliminated

* **The arc amplifying the kick.** `SteepSplitDebtTests`: give two conics an identical 1.1 m/s kick
  and they stay **1.100 m/s apart at every arrival angle and every delay to 40 s**. The trajectory
  does not amplify anything.
* **The clearance wait.** The steep arm drifts *slowest* through it (3bp).
* **The cutoff residual and the lighter bus.** 1.6x and 1.7x, and they share a cause so they do not
  multiply.

### And raising the ceiling is not the answer, by construction

`BusTrim.MaxMetresPerSecond`'s own docs: *"An answer in the tens is not a bus that has been shoved
off its arc — it is the solve being asked the wrong question, and thrusting at it makes the shot
worse rather than better while looking exactly like work."* It is a runaway guard, and the runaway
it guards against was flown — the aim correction and the trim winding each other up by ten every ten
cycles. `TrimCeilingFromBudget` is already on the Dead list.

### What asks the wrong question is the aim, and the bound already exists

`IcbmConfig.AimWithinTrimBudget`, off by default, and its documentation names this exact symptom:

> `AimCorrection.MaxMetres` is 300 km flat, and what the budget buys is 24 km on a 3,459 km shot —
> so the loop is licensed to walk somewhere the actuator can never follow. **The flown symptom is a
> demand that exceeds whatever is left of the ceiling on every pass until the budget is gone**, read
> until now as the solve diverging: it is not, it is an aim move being priced honestly.

That is the 20 to 26 m/s demand, described before it was traced. The setting is **the only one in
`IcbmConfig` that has never lost**: 0.85x over twelve paired shots, 9 wins of 12, interval
[0.53, 1.14] — unresolved for want of shots and nothing else.

**And the want of shots is now fixed.** The seat levelling takes a 14-block night from 9% power at
x0.60 to 90% (3bo's sibling, `fa4ac74`), and there is a sharper endpoint available than the median:
**the arrival-release count**, which is a count, resolves on far fewer shots, and is 1:1 with
divergence above.

**So the thing to do is fly it, on two pre-registered endpoints** — the levelled miss ratio, and the
arrival-release count by Fisher. That is item 10, and it was ranked fifth on a plan that did not
know the ceiling was the common failure.

## 3br. AimWithinTrimBudget does not help, and 3bq's inference was wrong — flown 2026-09-07

Item 10 flown to a verdict, `2026-09-07-1312`, 14 paired blocks at 6,269 km with the levelled
estimator.

**On the healthy mode it does not help.** `1.11x [0.94, 1.29]`, 5 wins of 14, signed-rank p=0.068 —
unresolved and trending the wrong way. The interval **rules out anything better than 0.94x**, which
excludes the 0.85x the earlier twelve shots suggested. Those twelve were un-levelled, at 2,000 km,
on a different build; this is the better instrument and it does not reproduce them. "The only
setting that has never lost" no longer holds.

**On the catastrophic mode it is untested.** The night drew **0 divergent worlds of 14** — about a
10% draw at the established rate — so the arrival-release endpoint had no events to count in either
arm.

### And that null exposes the error in 3bq

3bq found the arrival-release count 1:1 with divergent shots across 13 of 14 nights and concluded
the ceiling breach was the thing to attack, with `AimWithinTrimBudget` as the lever. **The
correspondence is real; the causal direction was wrong.**

A ceiling breach has two possible causes and they are not the same fault:

* **A runaway** — the aim correction and the trim winding each other up, which is what
  `BusTrim.MaxMetresPerSecond`'s docs describe and what `AimWithinTrimBudget` bounds.
* **An external push** — the bus being moved off its reference conic by something the guidance does
  not control, which is what the off-rails coast does at ~4-6 m/s.

**The divergent worlds are the second, and the evidence was already in hand.** Divergence is
world-level and takes every rocket with it regardless of what that rocket's computer is set to:

| night | base lost | other arm lost |
| --- | --- | --- |
| `1413` | 4/56 | 4/56 |
| `1730` | 4/52 | 4/52 |

Two arms, same worlds, identical counts. A per-craft setting cannot prevent something that hits
both arms equally — so bounding the aim was never going to reach it, and 3bq's "the lever is already
built" does not follow from its own correspondence.

**What the correspondence does say** stands and is still useful: the ceiling breach is a reliable
*marker* of a divergent world, on every affected flight, already logged. It is a detector, not a
cause.

### Where that leaves the ladder

Rung C is still behind the trim's demand at a steep arrival, and that demand is **not** the
divergence — 2148's p80 failures were arm-specific, 3 of 24 on the steep arm with base at 0, in
worlds that did not diverge. So there are two separate things ending at the same ceiling, and only
the steep-arrival one is on the path to rung C. It remains unexplained by a factor of ~3.6x after
the cutoff residual (1.6x) and the lighter bus (1.7x).

## 3bs. 5h: every geometric sensitivity FALLS with steepness — 2026-09-07

Every geometric sensitivity was measured against the arrival angle, and **all of them fall as the
arrival steepens.** So the trim's 6.2x demand at 54 degrees cannot come from the trajectory being
more sensitive to an error. It comes from the error itself.

| what the trim solves against | 39.9 deg | 44.0 | 54.0 | 60.0 |
| --- | --- | --- | --- | --- |
| a 1 km wrong departure, downrange | 1.000 m/s | 0.858 | **0.517** | 0.301 |
| a 1 km wrong departure, up | 1.895 | 1.810 | **1.622** | 1.511 |
| a 1 s wrong arrival | 5.173 | 4.581 | **3.332** | 2.607 |
| a 1.1 m/s kick, after any delay to 40 s | 1.100 | 1.100 | **1.100** | 1.100 |

The kick row is flat — two conics given the same shove stay the same distance apart at every angle,
so the arc amplifies nothing. The other three all shrink. **A steep arc is the more forgiving
geometry, not the less.**

### Dividing the flown demand by the measured sensitivity

Only one input looked as though it could be wrong, so I priced it:

| arm | arrival | demand at the split | m/s per second out | implied arrival error |
| --- | --- | --- | --- | --- |
| base | 16.9 deg | 0.550 | 5.173 | 0.11 s |
| p65 | 44.4 | 0.760 | 4.581 | 0.17 s |
| p80 | 54.4 | 3.410 | 3.332 | **1.02 s** |

**And the check refutes it.** `Arrivals()` prints the committed arrival beside the flown prediction
unconditionally, so the gap is already in every log: **0 s, on all six samples, at p80.** A 1.02 s
arrival error would have shown there and does not. The arrival-time route is out.

### So what is left is an input nobody has priced

`BusTrim.TrySolve` reads exactly four things:

```csharp
Kepler.TryCoast(mu, ReferencePositionCci, ReferenceVelocityCci, SecondsSinceReference, ...)
toGainCci = shouldBeDoing - now.VelocityCci;
```

Of the four, three are now measured and none of them explains it — the reference *position*
(sensitivity falls with steepness), the actual velocity against a kick (flat), and the arrival time
(refuted above, and it is not even an argument to this call).

**The untested one is `SecondsSinceReference`, and its sensitivity is gravity.** `shouldBeDoing`
moves at `|g|` per second along the reference conic — about 8.9 m/s^2 at 200 km — so **a clock error
of 0.1 s is 0.89 m/s of demand**, and 0.38 s is the whole of p80's 3.41. It is incremented only
while `Phase == IcbmPhase.Coast` (`IcbmProgram.cs:636`) while the reference is set during the burn
at the *predicted* cutoff (`:895`), so the two do not obviously share an epoch.

**That is a hypothesis with the right magnitude and no evidence yet.** What it needs is the same
treatment the others got: price the sensitivity per arrival angle, then find whether the quantity
itself differs. `docs/FRAMES-AND-EPOCHS.md` is the file to read first — a clock that starts at one
event and indexes a state belonging to another is the shape it exists to catch.

## 3bt. Eight candidates eliminated, and the rig cannot reach the ninth — 2026-09-07

5h taken as far as headless work goes. Everything measurable about the geometry has been measured
and **none of it explains the 6.2x demand at a steep arrival.**

| candidate | measured | verdict |
| --- | --- | --- |
| the arc amplifying a velocity kick | 1.100 m/s at every angle, every delay to 40 s | **flat** |
| the clearance wait | steep arm drifts *slowest*, 0.072 against 0.124-0.166 m/s per s | **wrong way** |
| the cutoff residual | 0.061 to 0.097 m/s | 1.6x |
| the lighter bus at cutoff | 24.2 g to 41.8 | 1.7x, and shares a cause with the row above |
| a wrong departure, downrange | 1.000 to 0.517 m/s per km | **falls with steepness** |
| a wrong departure, up | 1.895 to 1.622 m/s per km | **falls** |
| a wrong arrival time, sensitivity | 5.173 to 3.332 m/s per s | **falls** |
| a wrong arrival time, the error itself | committed against flown: **0 s**, 6 of 6 | **refuted** |
| the cutoff prediction | predicted against actual: **1 m**, every angle | **exact** |

**Every geometric sensitivity falls as the arrival steepens.** A steep arc is the more forgiving
geometry, which is worth having on its own — it removes "steep is intrinsically harder to correct"
from the reasons rung C might be unreachable.

### And the rig is out of reach of the answer

The headless flight rig is **clean at every arrival angle** — 1 m of prediction error, a residual
that moves 1.6x, and no sign of the flown 6.2x anywhere. That is not a null result about the
vehicle; it is a statement about the instrument. The rig **has no decoupler event, no split, and
does not run `BusTrim` at all** — and `_owedAtSplit` is by definition measured after a split.

So the term that is large is one the rig cannot produce, and more headless candidates would be
guessing.

### The diagnostic, which is what ships instead

`BusTrim` nulls `Kepler.TryCoast(reference, since).velocity - v`, so **exactly four inputs decide
the debt.** `SayTheSplitDebt` prints all four beside the answer, once, the first time the trim has
one:

```
split debt on <craft>: owed N m/s, T s since the reference, D km from it,
                       V m/s off its velocity, arc arrives A deg
```

That turns "the demand is large" into "*this term* is large", which is the difference between
another night of candidates and one reading. It costs one line per flight and is free on the next
night flown for any reason — including a steep-arrival arm, which is the one that would answer it
outright.

**Note the clock, because it is the one input with no headless bound.** `SecondsSinceReference`
is incremented only while `Phase == IcbmPhase.Coast` (`IcbmProgram.cs:636`), and `shouldBeDoing`
moves at gravity along the reference conic — about 8.9 m/s^2 — so **0.1 s of clock error is 0.89
m/s of demand and 0.38 s is the whole of p80's 3.41.** The reference is set during the *burn* at the
predicted cutoff (`:895`), which the measurement above shows lands within a metre, so the epoch
looks right; but it is the only term whose magnitude nothing has bounded, and
`docs/FRAMES-AND-EPOCHS.md` is the file for the shape.

## 3bu. 5h answered: the debt is the coast's length, not the arrival's angle — flown 2026-09-07

One shot, `base|p80:ArrivalPreference=0.8`, four rockets an arm, with `SayTheSplitDebt` printing all
four inputs.

| arm | arrival | owed at the split | **age of the reference** | distance from it |
| --- | --- | --- | --- | --- |
| base | 32.0-32.2 deg | 0.307 / 0.348 / 0.316 / 0.382 | **~969 s** | 4,300 km |
| p80 | 51.0-51.6 deg | 0.760 / 0.891 / 0.847 / 0.956 | **~1,861 s** | 5,300 km |

**The steep arm's reference is 1.92x older and its debt is 2.6x larger.** Per second of reference
age the two arms agree to within a third — 3.4e-4 against 4.7e-4 m/s per second.

**So the debt accumulates over the coast, and a steeper arrival simply has a longer one.** It is not
a property of the angle: 3bs already showed every geometric sensitivity *falls* as the arc steepens,
and this says what was left — the steep arc holds its reference for nearly twice as long, and the
divergence between the vehicle and a conic propagated from an old reference grows with the holding.

### What that corrects

**"At the split" is a much later instant than the name suggests.** The split here is the bus leaving
the spent stack near the release, not the booster staging: 969 s after cutoff on a shot that arrives
in ~1,390. Every reading in 3bo, 3bp and 3bs labelled "at the split" is a reading taken most of a
coast after cutoff, and the decompositions built on it were pricing terms at the wrong instant.

**And it explains why the headless rig was clean at every angle** (3bt). The rig has no split and no
trim, so it never holds a reference for 1,861 seconds — the one variable that turns out to matter is
the one it does not have.

### What it does not do is reproduce the 2148 failure

This shot is healthy in both arms, with no ceiling breach: 0.87 m/s at 51 degrees against a 10 m/s
ceiling. 2148's p80 read **3.410** median with three refusals at 20-26 m/s. So the accumulation
above is the ordinary behaviour, and the flown failure is something on top of it — on a build a week
older, at 2,000 km rather than 6,269, and at 54.4 degrees rather than 51.

**The next reading is therefore the same line on a night that actually breaches the ceiling**, which
is free now that it prints. What would confirm the account: a breaching flight showing a reference
far older still, or the age flat and one of the other three inputs large.

## 3bv. The bubble's FRAME is the divergence, and the existing logs already prove it

Four investigations over the decompiled engine. This one closes a question open since 3ar.

### There is no way back to rails from a Ccf bubble

`PhysicsStates.TryToPutOnRails` (`PhysicsStates.cs:803-825`):

```csharp
if (Environment.InPhysicsRadius) { ...motionless landing only... }
else if (Origin.BubFrame.IsCci()) { Props.Situation = ...WithOnRails(true); }
```

**A coasting vehicle returns to rails only when the bubble origin is `Cci`.** In a `Ccf` bubble
there is no return path at all. The other candidate, `TryToPutCoastingOnRails`, is **unsatisfiable
on any body with an atmosphere** — it demands `InPhysicsRadius` and simultaneously an altitude above
`AtmosphereRadius + boundingRadius`, and on Earth those differ by one metre in the wrong direction.

**So 3ay's null is explained.** QuietCoast releases the actuator, which is necessary and not
sufficient: in a `Ccf` bubble the release buys nothing because nothing will put the vehicle back.
Flown, base 282/380 off rails against quiet 276/376 — the actuator went quiet and rails never came
back, exactly as this predicts.

### And the missing acceleration is the rotating-frame terms

`ComputeDerivatives` (`PhysicsStates.cs:853-872`) puts **centrifugal and Coriolis inside
`if (environment.InPhysicsRadius)`**, which is *per vehicle*, while the frame is the *bubble's*,
taken from its heaviest member (`PhysicsBubble.cs:2211-2224`, re-sorted by mass at `:477`). A member
above the near-surface radius in a `Ccf` bubble is therefore integrated in a rotating frame **with
the rotating-frame accelerations switched off**. Earth's near-surface radius works out at
**167.41 km** altitude, derived from the 8 km scale height.

The deficit is `2w x v + w x (w x r)`: **0.42 m/s^2 at 2.9 km/s**, 0.77 at 5, 1.06 at 7.

### The corroboration is in logs already taken

3au's two rows, which it could not explain:

| shot | bubble | push | direction | over a 10 s probe |
| --- | --- | --- | --- | --- |
| 006 FAIL | 16 | 4.206 m/s | **90% cross-track** | **0.42 m/s^2** = `2wv` at 2.9 km/s |
| 007 PASS | 2 | 0.572 | **93% radial** | 0.057 m/s^2 = the centrifugal term alone |

`w x v` is perpendicular to the plane of `w` and `v`, so a near-polar arc puts it **cross-track**;
the centrifugal term lies in the `w`-`r` plane and reads **radial**. **3au's "the direction, not the
magnitude, is what repeats" is precisely what this mechanism predicts**, and it was measured before
anyone knew what to predict.

### The diagnostic, built

`KsaWorld.BubbleFrameOf` and `BubbleLeaderAltitudeMetres` — both off public API
(`Vehicle.BubbleOrigin`, `Vehicle.BubbleLeader`) — and the coast probe now prints
`Cci|Ccf origin at N km, leader at M km`.

**One flight settles it.** `Ccf` on divergent probes and `Cci` on healthy ones closes the account;
`Cci` on a divergent probe refutes it outright.

### And there is a lever, which is the part that matters

`vehicle.GetPhysicsStatesMutable().Props.SetOnRails(true)` is **fully public** (`Vehicle.cs:916`,
`VehicleProperties.cs:72`). It does not leave the bubble — it makes the bubble irrelevant, because a
rails `Freefall` vehicle takes `ApplyFreefallMotion` and an exact conic regardless of the frame, and
`UpdateFromAnalytic` handles a `Ccf` origin correctly.

Two things to respect if it is built. **Rails costs the attitude hold** — any commanded actuator
flips it straight back off — so it is only compatible with QuietCoast, and the natural shape is
rails for the long coast, released a fixed time before deployment so the hold can settle. And the
write reaches the worker from the `PrepareWorker` prefix the mod already patches, because
`GetNewProps()` re-seeds from `ReadOnlyVehicle.Props` each frame.

**That makes QuietCoast worth re-flying rather than retired** — it was the right idea missing its
other half.

### Two engine defects worth reporting upstream

* `TryToPutCoastingOnRails` is unsatisfiable on any atmospheric body (contradictory altitude tests).
* The bubble frame is a whole-bubble property while the rails rule and the fictitious-force rule are
  both per-vehicle and assume the frame matches the vehicle's own altitude band. A bubble straddling
  `GetNearSurfaceRadius()` breaks that, and `RemoveEligibleVehicles` cannot detect it because
  `GetDesiredBubFrame` reads the shared origin.

## 3bw. The miss is already there at release — traced 2026-09-07

The 10 m shot, decomposed. Two single-rocket flights with `Config.TraceWarhead` on, which nothing
had done since the shot got to 10 m, the 1 ms sub-step shipped, or the arrival went to 32 degrees.

| shot | probe **at release** | landed | walk during the fall |
| --- | --- | --- | --- |
| 001 | **8 m from the aim** | 12 m | **1 m** (+1 down, 0 cross) |
| 002 | **18 m from the aim** | 19 m | **4 m** (+4 down, 0 cross) |

**Everything downstream of the release is clean.** The round and its own predictor agree on the
landing surface to **0.0 m**; the round stops 1.3 m (001) and 3.7 m (002) under it; the flight time
agrees three ways to 10 ms — 311.68 s by the world clock, 311.69 by the round's own, 311.68 by the
probe — and the sampling lag is −0.6 ms, worth −3 m at 5,487 m/s.

**3ai is closed.** "About half the remaining miss is the round disagreeing with its own predictor",
measured at a 150 m median, is now **1 to 4 m of walk**. The pairing work that closed it shows in
one line: *"the round held −1.3 m off the true surface, unpaired it would have held −18.6 m"* over a
frame in which the body moved 894 m.

### So the whole budget was aimed at the wrong half

Everything priced for the current shot happens **after** the miss exists:

| term | priced at 32 deg | when it acts |
| --- | --- | --- |
| round integrator at 1 ms | 4.6 m | during the fall — **measured at 1-4 m of total walk** |
| cutoff residual x realised sensitivity | 3.5 m | before release, and already absorbed |
| ground sampled once a frame | 2.0 m | during the fall — **measured at 1.3-3.7 m** |
| height-field quantum, crossing tolerance | 0.9 m | at the stop |

The fall's terms are real and they are **the small half**. The aim is already 8 to 18 m off when the
warheads leave.

### And the cause is the correction's own stopping rule

`AimCorrection.ImprovedByMetres = 250.0`, and `PostBoostAim.PassesWithoutImprovement = 3`: the loop
stops when three passes fail to bring the predicted impact **250 m** closer. At a miss of 8 to 18 m
**no pass can ever improve by 250 m**, so the loop stops after three passes whatever it might have
achieved. `SteadyMetres = 2_000.0` is the same shape one level up.

**These are convergence thresholds twenty-five times larger than the entire miss**, sized when the
shot was kilometres and never re-sized as it came down.

The Dead list's *"`ImprovedByMetres` (50/250/1000 identical)"* is consistent with this rather than
against it: all three are far above the miss, so all three stop the loop identically. **The value
that was never tried is one below the miss.** An absolute threshold cannot be right across a shot
that has run from kilometres to metres — a fraction of the current predicted miss can.

**This is the largest single term in the shot and nothing has ever attacked it.**

## 3bx. The aim band was blind, and unblinding it does not move the miss — flown 2026-09-07

Item 25, 14 paired blocks. Both endpoints were pre-registered and they disagree, which is the useful
part.

**The mechanism is confirmed.** What ended each arm's corrections:

| arm | `clock` | `noimprov` | `payback` |
| --- | --- | --- | --- |
| base | 10 | **31** | 15 |
| band | 27 | **3** | 26 |

`noimprov` falls **31 to 3**. The loop really was stopping because no pass could close 250 m at an
8-18 m miss, and with the band following the miss it almost never stops that way — it runs to the
clock instead. 3bw's reading of the stopping rule was right.

**And the miss does not follow.** `0.88x [0.84, 1.10]`, 9 wins of 14, signed-rank p=0.241 —
unresolved, and the interval's own best end is a 16% gain. The trim's debt at release is unchanged
either way: 2.63 m/s against 2.65.

### What that means, and it is not what 3bw expected

3bw measured the aim 8 to 18 m off at release on shots landing at 12 and 19, and inferred that a
loop able to see below 250 m would close it. **The loop now runs far longer and closes nothing.**

So the 8-18 m is not a loop that stopped early. It is a floor the correction reaches and cannot get
under, and the band was merely hiding that behind a stopping rule that fired first. Removing the
blindfold shows the wall behind it.

**The next question is therefore what the correction converges *to*, not when it stops**, and one
column already argues against the obvious answer: the trim still owes **2.6 m/s at release in both
arms**, unchanged by any number of extra passes. A correction that is computed but not flown would
look exactly like this — the aim moves, the arc is re-solved, and the trim does not deliver it.

### Keep the change, off

`AimThresholdTracksTheMiss` stays built and off. It is measured harmless (the interval's upper bound
is 1.10 and the pooled medians are 0.02 against 0.01), it makes the loop's stopping rule mean
something at the scale the shot now flies, and any future work on what the correction converges to
needs it on to be measurable at all — a loop that stops on three passes cannot show whether a change
helped it converge.

**And it retires the Dead list's entry properly.** *"`ImprovedByMetres` 50/250/1000 identical"* was
right that the value does not matter above the miss, and this is the first measurement below it: the
loop's behaviour changes completely (31 to 3) and the shot does not.

## 3by. A one-block paired run confounds the arm with the seat — 2026-09-07

Item 26. Two single-block paired worlds were flown to read the aim trace, and **both are void as
arm comparisons.** `ShotArms` alternates the variants down the roster and flips them each shot, so a
*multi*-shot night balances seat against arm — and a **one-block run never flips**. Both worlds gave
band seats 1, 3, 5, 7 and base seats 2, 4, 6, 8, which makes "band vs base" and "odd vs even seat"
the same contrast.

The seat effect is far larger than anything being tested. Measured arm-neutral over the 14-shot
night: **s1=9 m, s2=16 m, s3=92 m, s4=19 m, s5=25 m, s6=10 m, s7=27 m, s8=9 m** — seat 3 is ten
times seat 1, and band was carrying it in both runs.

| | band (seats 1,3,5,7) | base (seats 2,4,6,8) | ratio |
| --- | --- | --- | --- |
| **expected from seats alone, zero arm effect** | | | **2.00x** |
| world 2159 observed | 22.0 m | 8.0 m | 2.75x |
| world 2255 observed | 25.0 m | 13.5 m | 1.85x |

Both bracket the seat prediction. **No arm effect is needed to explain either**, and the earlier
draft of this entry — "the arm whose score goes stale landed better" — is withdrawn.

### What the arm is actually worth, from the night that flips

`2026-09-07-1824`, 14 shots, 112 flights, seats levelled: **band 0.88x [0.84, 1.10] at 97%,
unresolved** — the point estimate favours **band**, the opposite direction to the confounded runs.

### And the predictor is uninformative rather than inverted

The confounded world read rho = -0.39 (n=8) between the loop's best score and the landing, which is
what suggested the judge ran backwards. Over the 14-shot night with seats levelled it is
**rho = +0.120, p = 0.205, n = 112** — the expected sign, weak, and not significant. So the claim
that survives is the narrower one: **the score the correction optimises carries little information
about where the rocket lands**, matching the +0.04 within-session correlation `shot-report.py`
already records. It is not evidence that reverting to the best aim is harmful.

### The mechanism measurement stands, because it is not a comparison

`aim frozen on <craft>` says what `Freeze()` discarded at each release, and that is a within-flight
reading of what the code does — no seat term in it:

| arm | freeze reverted, per flight |
| --- | --- |
| band (25% band) | 2.0, 2.5, 2.8, 4.8 m |
| base (flat 250 m) | 2.4, 27.4, 104.2, **153.3** m |

So the two bands do differ exactly as designed: `_bestMiss` stops ratcheting inside 250 m, so
`_bestBias` goes stale and **`Freeze()` throws away up to 153 m of the walking the loop did after
it**, where the proportional band discards ~2 m. That is a real and previously invisible behaviour.
Whether it costs anything is the open question, and 1824 is the only evidence: 0.88x, unresolved.

### And the trace still goes dark before the part that matters

With the craft named, the eight traces are readable — and every one of them **stops 77 to 120 s
before its own release**, all within five seconds of each other, which makes it a world event rather
than anything per-craft:

| craft | last trace | freeze | dark for | trace said | shipped |
| --- | --- | --- | --- | --- | --- |
| FAT 7 | 23:00:46 | 23:02:46 | 120 s | 1.32 km | **59.1 m** |
| FAT 8 | 23:00:45 | 23:02:45 | 120 s | 3.69 km | **136.7 m** |
| FAT 2 | 23:00:50 | 23:02:46 | 116 s | 3.78 km | 463.7 m |
| FAT 4 | 23:00:47 | 23:02:11 | 85 s | 3.81 km | 346.2 m |

**The bias moves by a factor of three to twenty inside the unlogged window**, so the trace covers
everything except the part that decides the shot. Item 26 is still open, and 26b is what closes it:
find what stops the prediction at that instant and log through it.

### The rule this cost, and it is a protocol rule

**A single-block paired run is a diagnostic, never a comparison.** It is the right shape for reading
an instrument — which is what both of these were for, and both delivered that — and it cannot rank
two arms at all. `shot-report.py --paired` now says so rather than printing arm medians that mean
nothing.

## 3bz. The aim loop has converged; what is left is the ground — 2026-09-07

**Item 26, answered.** With the craft named on the `aim:` line the whole coast is legible, and the
correction turns out not to be the limiter at all.

First, a retraction: 3by said the trace goes dark 77-120 s before release. **It does not.** At bus
separation the craft is renamed `GeoSat FAT 4` to `GeoSat FAT 4_1` — `PlatformHandover` working as
designed — and the trace continues under the new name 0.5 s later at the same cadence, ending at
exactly the bias the freeze ships. The grep matched only the bare name. Item 26b is withdrawn.

### The correction converges, monotonically, on every flight

Over the coast, cutoff to freeze, ~500 samples each:

| seat | bias at cutoff | bias at freeze | path walked | loop's final predicted miss | **landed** |
| --- | --- | --- | --- | --- | --- |
| 1 | 1.59 km | 932.9 m | 658 m | 2.5 m | 4 m |
| 2 | 3.78 km | 458.1 m | 3325 m | 11.3 m | 15 m |
| 3 | 1.39 km | 270.4 m | 1130 m | 7.5 m | **81 m** |
| 4 | 3.81 km | 345.7 m | 3464 m | 1.4 m | 23 m |
| 5 | 1.46 km | 449.1 m | 1195 m | 5.4 m | 19 m |
| 6 | 1.63 km | 1.27 km | 400 m | 7.0 m | 5 m |
| 7 | 1.32 km | 55.3 m | 1294 m | 9.6 m | 31 m |
| 8 | 3.69 km | 56.2 m | 3634 m | 12.4 m | 12 m |

**The path walked equals the net change on every flight** — so the bias descends monotonically and
does not wander. And the loop converges to **1.4 to 12.4 m by its own reckoning**, every time.

### And its own prediction has nothing to do with where the rocket lands

| landing correlates with | rho |
| --- | --- |
| the loop's own converged prediction | **+0.07** |
| **that seat's level, measured on a different night** | **+0.93** |

The seat levels come from `2026-09-07-1824` — a different night, different builds, 112 flights — and
they predict this night's landings in **metres**, not merely in rank: landed/level runs 0.44, 0.94,
0.88, 1.21, 0.76, 0.50, 1.15, 1.33, median 0.9.

**And a seat is a patch of ground.** `AimSpread` displaces each seat 12 km from the last, and every
night since 2026-09-04 has used the same aim point — so seat 3 has always been the same hillside.
That is why the level reproduces, and it is what `shot-report.py`'s own levelling docstring already
says: it is "a property of the world rather than of anything under test".

### What this means for the plan

**The aim correction is finished work.** It converges, monotonically, to single-digit metres against
an observer that is exact to 0.46 m — and the rocket then lands at whatever its ground dictates.
This is the same shape as the drag-free predictor (`a correction loop can only remove what its
observer can see`), one level down: the loop removes everything it can see, and what remains is
terrain its prediction does not resolve.

So items **24** (forced rails), **25** (the improvement band) and **20b** (quiet coast) are all
tuning of a loop that is already converging two orders of magnitude below the miss. **They cannot
pay.** The lever is the ground: what the predictor samples, at what wavelength, and how a 32 deg
arrival converts unresolved relief into downrange miss — `docs/KSA-TERRAIN.md` and 3ae's
sub-kilometre band.

**And the instrument checks one seat of eight.** The report's terrain line reads the scenario aim
point only — "downrange slope -0.02%, 1.0x flat ground, well conditioned" — which is seat 1's
ground, the best on the roster at 9 m. Seat 3, 24 km away and ten times worse, is never looked at.

## 3ca. The improvement ratchet is arithmetically dead below 250 m — 2026-09-07

Read out of the code and pinned by `AimRatchetTests`; no flight needed, though the flown reverts
match it exactly.

`AimCorrection.Observe` banks a new best aim only on `miss < _bestMiss - band`, and `_bestBias` is
written **only** in that branch. With the shipped flat band of 250 m:

* **Once `_bestMiss <= 250 m`, `_bestMiss - band <= 0`** and no non-negative miss can ever bank
  again. The ratchet is dead for the rest of the flight.
* **The `worse` arm needs `miss > _bestMiss + 250`**, which a converged loop never reaches — so
  `_worseFor` never counts, `Settled` never becomes true on its own, and `WorseBeforeStopping = 12`
  is unreachable. (Consistent with the flown maximum of 2 in 3by.)
* So the aim that ships is decided **entirely** by the terminal `Freeze()` at release, which reverts
  to whichever aim was current when the miss first fell under 250 m.

Everything the loop achieves after that point is discarded. Flown, that is the revert spread:

| arm | `Freeze()` discarded |
| --- | --- |
| flat 250 m | 2.4, 27.4, 104.2, **153.3 m** |
| 25% of best, 1 m floor | 2.0, 2.5, 2.8, 4.8 m |

The spread on the flat band is **how early the ratchet died**: a shot stepping 3 km straight to 40 m
banks 40 m and has nothing left to walk; one going 3 km to 600 m to 240 m banks at 240 m with
hundreds of metres of correction still to make, and loses all of it. And the steps stay full-size on
the way down — `Resume()` seeds `_response = 1.0`, `MinResponse` is 1.0, and `_response` stops being
re-measured once the aim moves less than `ResponseFromMetres = 500 m` per cycle, which at these
scales is immediately.

**Two things this is not.** It is not the `_worseFor` accumulation bug fixed in `fdfd325` — that was
the same comparison's other arm, and both are inert at 250 m. And it is **not** established as
costing anything: `AimThresholdTracksTheMiss` is off, unflown at **0.88x [0.84, 1.10]**, and 3bz
says the loop is already converging two orders of magnitude below what the ground contributes. The
defect is that a documented mechanism does not operate, which is worth knowing whether or not
switching it on pays.

### And `Resume()` is load-bearing and conditional

`_aim.Resume()` runs once, on the first non-burning frame after cutoff, guarded by
`_resumedForCoast`. For a pad launch the phase machine holds `IsBurning` true from arming to cutoff,
so the flag survives to be spent there — and `Resume()` is what un-settles the loop after the
mid-burn `Freeze()`, resets `_bestMiss` to infinity and makes the post-cutoff correction possible at
all. **A non-burning frame before cutoff would spend the flag and silently kill the coast
correction for that flight**: an orbital pickup returns `Holding`, and a first-frame solver failure
returns `NoSolution`. Neither happens on a pad launch, and neither is guarded against. Unflown, and
the cheap check is that the phase line reads `Rising` first with no `Holding`/`NoSolution` before it.

## 3cb. The miss is the walk after release, it is pure downrange, and it follows the ground — 2026-09-07

Headless, off logs already on disk. 3bz said the residual is the ground; this says by what mechanism
and what it is worth.

### The miss is what happens after the prediction, and it is all downrange

`WarheadTrace` records, per warhead, how far it ended up from what the release-time prediction said.
Over **1,822 warheads** across every night that had the trace on, grouped by aim point:

| aim point | seat | n | final miss | **walk from the release probe** | downrange | cross |
| --- | --- | --- | --- | --- | --- | --- |
| -26.7,-69.0 | 8 | 150 | 13 m | 2 m | 2 m | 0 m |
| -26.5,-68.1 | 1 | 174 | 14 m | 5 m | 5 m | 0 m |
| -26.6,-68.7 | 6 | 156 | 15 m | 11 m | 11 m | 1 m |
| -26.6,-68.5 | 4 | 150 | 15 m | 13 m | 13 m | 1 m |
| -26.5,-68.3 | 2 | 169 | 21 m | 11 m | 11 m | 1 m |
| -26.6,-68.9 | 7 | 162 | 29 m | 19 m | 19 m | 1 m |
| -26.6,-68.6 | 5 | 155 | 33 m | 23 m | 23 m | 1 m |
| **-26.5,-68.4** | **3** | 159 | **85 m** | **71 m** | 71 m | 4 m |

The seat mapping is not assumed: the aim points sit on a line 12 km apart from the scenario anchor,
and each one's median miss reproduces its seat's level from 3bz independently.

**Two things fall straight out.** The final miss *is* the walk plus about ten metres — so almost the
whole miss is made between release and the ground, not before it. And **the walk is entirely
downrange**: cross-range is 0-4 m at every aim point, on every night. That is the signature of a
**height** error at the impact point, not a lateral one.

### And it follows the sub-kilometre relief

| response | vs sub-km rms | vs sub-km peak-to-peak |
| --- | --- | --- |
| final miss | +0.61 (p=0.116) | +0.59 (p=0.134) |
| **walk from the release probe** | **+0.72 (p=0.052)** | +0.69 (p=0.063) |

The walk is the better-behaved response, as it should be — it has the guidance's own few metres
taken out of it. Eight aim points is all the power this roster has, so p=0.052 is the ceiling
available without a different spread.

**The height error implied by the geometry is about one rms of the sub-kilometre relief**, with no
fitting anywhere: `h = walk x tan(32 deg)` gives h/rms of 0.9, 1.3, 2.2, 0.7, 2.1, 0.4, 1.7, 0.4 —
median **1.1**, over ground running 2.9 m to 20.5 m rms.

### What it is not

**Not the crossing search.** `ImpactPredictor` bisects on *depth* to `CrossingToleranceMetres` =
0.25 m and samples `SurfaceUnder` at each trial point, so its vertical resolution is a quarter of a
metre and it does see the real height field. The fault is not that the predictor cannot find the
ground.

**More likely amplification than blindness.** A shallow arrival makes *where* an arc crosses the
ground acutely sensitive to the ground's own height, so any small difference between the predicted
arc and the flown warhead — release kick, drag, a metre of state — lands somewhere else entirely
when the surface underneath is bumpy. That reading fits the pure-downrange signature and the
h ~ rms scale, and it is the one to test next.

### What the lever is worth, priced off the measured walk

A height error becomes `h x cot(gamma)` of downrange miss. The mod arrives at **32.0 deg** and the
release summaries say the tanks could afford **63.8 deg** — it is flying at half its budget.

| arrival | cot | vs today | median walk | seat 3's 71 m |
| --- | --- | --- | --- | --- |
| **32 deg (flown)** | 1.60 | 1.00x | 12 m | 71 m |
| 40 deg | 1.19 | 0.74x | 9 m | 53 m |
| 44 deg | 1.04 | **0.65x** | 8 m | 46 m |
| 54 deg | 0.73 | 0.45x | 5 m | — |
| 63.8 deg (affordable) | 0.49 | 0.31x | 4 m | — |

**32 to 44 deg is a 35% cut in the dominant term and stays below the 44-54 deg coast cliff 3bo
measured.** That is the next thing to fly, and it is a one-line setting: `MinArrivalAngleDeg`.

### The instrument only ever looked at one seat

`shot-report.py`'s terrain section reads the scenario aim point alone and calls it "well
conditioned" — that is seat 1, the second-best ground of the eight. Seat 3, 24 km away and seven
times rougher, was never in the report. Fixed: it now reads all eight from the per-craft
`ground under the aim on <craft>` lines that were already in every log.

## 3cc. The arrival angle, re-tested where terrain is actually present — PREPARED, NOT FLOWN

3cb prices a steeper arrival at 0.65x-0.74x. **The angle has already been flown, and read a dead
heat** — so the first job was to find out why that does not settle it.

### The old test was run where the thing it fixes was absent

`2026-09-01-2148` flew four arms at aim point **10.622,-80.604**. Head-to-head over its 12 shots,
p50 (34.2 deg) beat p65 (44.4 deg) **7 shots to 5**, medians 14 m against 17 m — nothing.

But the two sites are not the same experiment, and the warhead traces say so:

| site | median miss | median walk after release | terrain's share |
| --- | --- | --- | --- |
| **10.6N, 80.x W** — where the angle was tested | 27 m | **2 m** | **7%** |
| **26.5S, 68.x W** — every night since 2026-09-04 | 27 m | **11 m** | **41%** |

**Identical overall miss, composed completely differently.** At 2148's target the arrival angle had
almost no terrain error to remove, so a dead heat there is what a working lever looks like when the
term it multiplies is 7% of the total. At the current target it is 41%, and 85% at seat 3. This is
the same shape as the drag-free predictor and the 250 m band: **the measurement was taken where the
effect could not appear.**

### The night

Two arms, because seat levelling is for two-arm nights (3be's own caveat: four arms over eight seats
made the interval *wider*). `ArrivalPreference = 0.65` is 0.65 x 63.8 = **41.5 deg** against the
shipped 31.9 — below the 44-54 deg cliff 3be located, and a value already flown without a trim
blow-up (`owed` 2.60 against p50's 2.56; it is p80 at 4.19 that breaks).

```bash
KSARMORY_SCENARIO_SAVE='SOLVER SCALE 8' ./tools/shot-batch.sh \
    --paired 'base|steep:ArrivalPreference=0.65' \
    --aim 26.485S,68.148W --blocks 14 --out ~/shots/<date>
```

`ShotArmsTests.TheArrivalRetestSpecParsesAndSetsThePreference` pins that spec so the night cannot be
lost to a typo in it.

### What it predicts, written down first

**Primary endpoint is the walk, not the miss.** The walk is what the angle acts on directly and it
is 41% of the miss, so scoring on the miss dilutes a 0.71x effect to about 0.88x — marginal at 14
blocks, where 1824 resolved 0.88x only as [0.84, 1.10].

1. **The walk falls to ~0.71x** — `cot(41.5) / cot(31.9)` = 1.132 / 1.600.
2. **The gain is graded by seat roughness.** Seats 3, 6 and 4 (20.5, 15.8, 11.9 m rms) should improve
   most; seats 8 and 1 (2.9, 3.3 m) have almost nothing to give and should barely move. **This is
   the strong test** — a uniform improvement across seats would mean something other than terrain.
3. **Cross-range stays flat** at 0-4 m in both arms. If cross-range moves, the mechanism is not the
   one 3cb describes.
4. **The miss falls to ~0.85-0.90x**, likely unresolved on its own at this n.

**What would refute it:** the walk unchanged, or improving as much at seat 8 as at seat 3.

### Risks to watch

* **p65 diverged on 4 of 12 shots at the old site** (98, 110, 122, 112 m against 12-26 m otherwise),
  with `owed` normal — so it is not the trim ceiling and it is unexplained. The report's mode split
  is what catches it; if it recurs here the night is about *that*, not about the angle.
* A steeper arrival is a **longer coast**, which is more exposure to whatever the coast does to the
  bus — 20b's open question, and the reason 3be deferred this measurement in the first place.

## 3cd. The steeper arrival did nothing, and the graded prediction is refuted — 2026-09-08

Item 27 flown. 14 blocks, 112 flights, `base|steep:ArrivalPreference=0.65` at 26.485S,68.148W,
`~/shots/2026-09-08-arrival`. Frame time 23.8 ms — the fastest session yet, and the machine was idle.

**The arm did what it was asked**: arrival **32.0 to 41.7 deg**, floor tracking it at 41.4.

### Against the predictions written down in 3cc

| # | prediction | outcome |
| --- | --- | --- |
| 1 | walk falls to 0.71x | **unmeasurable** — see below |
| 2 | **gain graded by seat roughness** | **REFUTED** — rho = +0.12, p = 0.793 |
| 3 | cross-range stays 0-4 m | held (0 m base, 1 m steep), but on the same unusable subset |
| 4 | miss ~0.85-0.90x, likely unresolved | **0.96x [0.66, 1.13]**, 7 of 14, sign p=1.000, unresolved |

**Prediction 2 was the strong test and it failed.** Per-seat ratios came out 0.86, 0.57, 0.56, 1.31,
0.46, 1.45, 2.50, 1.29 against roughness of 3.2, 5.1, 20.5, 11.9, 6.8, 15.8, 7.0, 2.9 m rms — no
relationship. Seat 3 did improve 84 to 47 m, which is what the hypothesis wants; seat 7 went 24 to
60 m, which it does not, and seven flights a cell is what that scatter looks like.

**So the steeper arrival is not worth anything at this target either**, and the terrain-amplification
story does not survive its own intervention. What stands from 3cb is the *correlational* half — the
miss is made after release, it is pure downrange, it tracks sub-km roughness at rho +0.72. What does
not stand is the inference that arriving steeper therefore fixes it.

The likeliest reading is a **trade that cancels**: a steeper arrival buys less terrain gain and pays
a longer coast, and `owed` moved 2.67 to 2.83 m/s in exactly that direction. That is the same
mechanism that makes 0.8 a settled loss, arriving earlier and smaller.

### The primary endpoint could not be measured, and that is a design fault of mine

`WarheadTrace` ran on **seats 1-4 only**, never 5-8, at about six warheads per seat per arm — 52 of
672. Declaring the walk the primary endpoint without first checking the trace's coverage made the
whole night's headline unmeasurable. The subset also disagrees with the full sample about the miss
(9 m against 18 m, where 56 v 56 reads level), so it is not merely small but unrepresentative.
**Anything scored on the walk needs the trace's coverage established first.**

### What else the night says

* **Off rails 15% to 3%.** The largest mechanism move of the night and nothing to do with what was
  being tested. A steeper arc spends far less of its coast off rails — worth knowing against 3aq and
  item 19c.
* **No divergence.** 4 of 56 lost in each arm, Fisher p=1.0. The 4-of-12 blow-up p65 showed at the
  old target did **not** recur, so that was the target or the harness rather than the angle.
* **One world lost whole.** Block 4 put all eight flights at 87-120 km, both arms together — a
  world-level failure, not an arm effect, and the mode split counts it in both columns.

### Where this leaves the ladder

Two levers have now been flown at this target and neither moved it: the improvement band (0.88x
[0.84, 1.10]) and the arrival angle (0.96x [0.66, 1.13]). 3bz says the aim loop is already
converging two orders of magnitude below the miss. **The next question is not which knob to turn but
what the 12 m actually consists of**, and the honest answer is that nothing currently measures it
per flight — the walk would, and its instrument covers half the roster.

## 3ce. The warhead trace was stranded, not sampled — 2026-09-08

Item 28. 3cd blamed its own unmeasurable primary endpoint on the trace covering "seats 1-4 only".
Both halves of that were wrong.

### It was never a sample

**All eight traces began; four finished.** `IcbmComputer._warhead` is re-read from the launcher every
frame, so it goes null the moment the launcher does — and `TraceSetup()` returning null did not end
the trace, it **stranded** it. `WarheadTrace.Finish` is reachable only from `Update`, so the round
lands with nothing watching and the flight is simply *absent* from the log, which is
indistinguishable from a flight nobody traced.

Fixed by latching the profile at `Begin` — a trace follows one round already in the air, and the
profile of a round in the air cannot change — and a setup that still fails now says so once at WARN.
**Flown: 8 begun, 7 finished, all named, no strandings.**

### And it was not seats 1-4

The trace line never named its craft, and the craft **cannot** be recovered from the landing
coordinate here: the aim points are 12 km apart but **seats 5 and 6 sit 100 m apart** (59.8 and
59.9 km from the anchor), so nearest-point matching mislabels three of the four survivors. The real
survivors were seats **1, 3, 6, 8**.

**So 3cd's walk figure of 1.50x is void** — it was computed on wrong arm labels. Redone with correct
attribution the same data splits 22 flights to 2, which supports nothing. The walk endpoint from
that night is *unmeasurable*, not measured-and-unfavourable. **3cd's other results are unaffected**:
the miss at 0.96x [0.66, 1.13] and the refuted roughness grading both come off the FLIGHT lines and
never touched the trace.

Both trace ends now carry `warhead trace on <craft>`. Third time this fault has been fixed, after
the cutoff line and the `aim:` line.

### A third loss path remains, and it is outside StepTrace

One flight in eight still begins and never finishes, with **no** stranded warning — so `StepTrace`
is not being reached at all. `IcbmComputer.Update` returns early on `!KsaWorld.IsAlive(Craft)`, and
once the bus is gone nothing steps the trace and nothing can report that. Closing it means the trace
outliving its craft the way `WeaponSystem.GoLoose` already lets a *round* outlive its launcher.
Not built; 7 of 8 is enough to decompose the miss, and the loss looks unbiased.

## 3cf. The miss splits in two, and only one half is the ground — 2026-09-08

The first decomposition of the miss, and the reason to have fixed the trace. Eight blocks, single
arm, `~/shots/2026-09-08-decompose`, **60 named traces of 64** (the stranding fix took it from 50%
to 91%). All 8 blocks PASS.

### The split

| | median |
| --- | --- |
| **total miss at the ground** | **15.0 m** |
| of which made **during the fall** (the walk) | **10.5 m — 70%** |
|   downrange | 10.5 m |
|   cross-range | **1.0 m** |
| implied error **before release** | **7.0 m** |

### The two halves are different terms, and the statistics say so

| component | vs seat roughness | exact p |
| --- | --- | --- |
| **the walk** | **+0.74** | **0.046** |
| the part made before release | +0.12 | 0.793 |
| the total miss | +0.62 | 0.115 |

**The walk follows the ground and the pre-release error does not.** That is the whole finding: the
miss is not one quantity to be reduced but two, and they answer to different things. The
before-release component runs 2, 8, 13, 0, 7, 4, 12, 9 m across the seats with no relation to the
terrain under them — and it sits right on the 1.4-12.4 m the aim loop converges to in 3bz, which is
what it should be if it *is* the loop's converged residual.

### And this explains 3cd's null

The arrival angle acts on the walk, which is 70% of the miss — so scoring it on the **total** miss
diluted a 0.71x effect to about 0.80x, on a night whose interval was [0.66, 1.13]. 3cd could not
have resolved it either way. **The night was not evidence that the angle does nothing; it was a
measurement of the wrong quantity**, because the right one was unmeasurable at the time.

Note also that the total miss vs roughness reads +0.62 (p=0.115) here against +0.61 (p=0.116) in
3cb, on independent flights — so 3cb's correlational half reproduces, and the walk sharpens it to
p=0.046 exactly as splitting a diluted signal should.

### What this makes the plan

Two budgets, and a metre needs both under a metre:

1. **The walk, 10.5 m, terrain-driven, pure downrange.** The arrival angle is the lever and it has
   never been scored against this quantity. **That is the next night**, and it is 3cc's original
   design finally executable: `base|steep:ArrivalPreference=0.65`, scored on the walk.
2. **The pre-release residual, ~7 m, ground-independent.** Nothing has attacked this; 3bz established
   the loop converges *to* it, not that it cannot go below it. Unexplored.

Neither is the aim loop's stopping rule, its band, or its improvement threshold — 3bz and 3ca
between them close that file.

**The first of those is prepared and ready to fly — 3cg**, which also has the three instrument
faults that had to be fixed before the walk could be scored at all.

## 3cg. The walk night, prepared — and the instrument built first this time — 2026-09-08

Item 29. **The same arm as 3cd and a different endpoint**, which is the whole of what makes it a
new measurement rather than a re-run: 3cd flew `base|steep:ArrivalPreference=0.65` and scored the
total miss, where the angle acts only on the walk. Simulated on the walk's own measured scatter,
that night had **0.37 power** against the effect it was looking for. Scored on the walk the same
14 blocks reads **0.85**.

### The endpoint was measured before it was declared, which is 3cd's lesson

3cd named the walk its primary endpoint and could not read its own headline, because the trace
covered half the roster. So this time the quantity was priced off `~/shots/2026-09-08-decompose`
first — 57 attributed traces, 8 seats — and it is a better endpoint than the miss for a reason
nobody had looked for:

**The walk is very nearly a per-seat constant, sign included.**

| seat | its |walk| over eight flights |
| --- | --- |
| 3 | −54, −54, −58, −63, −64, −83, −87, −87 |
| 8 | 0, 1, 1, 1, 2, 2, 2 |
| 4 | +14, +15, +15, +15, +20, +21 |

Within-seat log sd **0.448 (×1.57)** against the miss's fitted ×1.74, and the sign belongs to the
seat rather than to the flight. So an arm cannot move the walk's *sign*; what it can do is shrink
each seat's own bias toward zero, which is why the score is a magnitude and why pooling the signed
values would cancel two seats against each other.

Power of the report's own estimator — signed-rank over shots, seat-levelled, 88% trace coverage:

| effect | endpoint | 6 blocks | 10 | 14 |
| --- | --- | --- | --- | --- |
| 0.71× | **walk** | 0.31 | 0.70 | **0.85** |
| 0.71× | miss | 0.20 | 0.51 | 0.69 |
| 0.80× — what 0.71× dilutes to on the total | miss | 0.10 | 0.27 | **0.37** |

### Three instrument faults found on the way, all fixed

None of them is about the angle, and two were introduced by the fix that made this night possible.

1. **`--terrain` had been blind since `471f09f`.** `IMPACT` still matched `warhead trace: round N
   landed at`, which the line stopped saying when it started naming its craft — so the terrain
   check reported *no warhead traces in this night* on the very night 3cf's terrain correlations
   came from. 464 impacts read as none, silently, on every night from here on.
2. **Every trace line in a shot went to every flight in it**, so a per-flight walk was the whole
   roster's and identical across arms **by construction** — a ratio of exactly 1.00 on an interval
   of [1.00, 1.00]. `--endpoint walk` now refuses an unattributed trace rather than scoring it,
   because that dead heat is the absence of a measurement wearing the shape of one.
3. **The terrain fit counted each landing once per rocket in the world.** The slope was unmoved —
   duplicating a point does not move a least squares line — but the standard error was tight by
   exactly √8: `2026-09-08-decompose` reads 58 impacts at ±0.21% where it read 464 at ±0.07, and
   the "well conditioned" verdict rests on that bar.

`--paired` also now prints **3cc's strong test** directly: per seat, what the arm did to it against
how rough the ground under its own landings is, with the rank correlation between them. 3cd had to
assemble that by hand, on the miss.

### The night

```bash
KSARMORY_SCENARIO_SAVE="SOLVER SCALE 8" KSARMORY_SCENARIO_TRACE=1 ./tools/shot-batch.sh \
    --paired 'base|steep:ArrivalPreference=0.65' \
    --aim 26.485S,68.148W --blocks 14
./tools/shot-report.py --paired --endpoint walk ~/shots/<night>
./tools/shot-report.py --paired ~/shots/<night>            # the miss, for continuity with 3cd
```

`--plan-only` clean against HEAD. **`KSARMORY_SCENARIO_TRACE` is not optional here** — it is the
instrument the endpoint is read through, and a night flown without it scores nothing. `batch.tsv`
now records it and a resume refuses without it, for the reason it already refuses a lost save.

### What it predicts, written down first

1. **The walk falls to ~0.71×** — `cot(41.5) / cot(31.9)` = 1.132 / 1.600, unchanged from 3cc.
2. **Graded by the ground**: the rank correlation of the per-seat ratio against the per-seat relief
   is **negative**. This is still the strong test and it is now one line of the report rather than
   an afternoon of arithmetic.
3. **Cross-range stays flat.** The walk is 10.5 m downrange against 1.0 m across, and the mechanism
   is entirely downrange.
4. **The miss falls to ~0.85–0.90×, and is unresolved at this n** — which is 3cd's result, and is
   the point: reading the same night both ways is what shows the dilution rather than asserting it.

**What would refute it:** the walk unchanged, or improving as much at seat 8 (relief 0.2 m) as at
seat 3 (5.1 m).

### Risks to watch

* **Coverage first, before anything else in the report.** Some flights still begin a trace and
  never finish it, outside `StepTrace` (3ce). Under 75% the report says so and the night is a
  diagnostic. Measured: **89% over the eight blocks of `2026-09-08-decompose`**, and **6 of 8 on
  the smoke shot below** — one draw, and the two that went dark were the last two to release. If
  the night comes in under 75%, 3ce's third loss path is the thing to close, not the endpoint.
* A steeper arrival is a **longer coast**, which is 20b's open question. 3cd measured off rails
  **15% → 3%** on this same arm, so if anything the steep arm spends less of its coast exposed.
* **The one change since 3cd that reaches a warhead is the round reaper.** `MunitionProfile.
  HitsTerrain` rounds are now reaped on time spent below the arrival ceiling rather than on age,
  and a new `Approach.Impossible` reaps a conic that can never arrive. Both are permissive here —
  the Mk 21 flies ~350 s from release against a 1,800 s limit and its arc arrives — and every case
  the classification cannot answer, a hyperbolic arc included, falls back to the old clock. What it
  does change is that a **diverged** warhead thrown onto an arc that never arrives is reaped
  promptly instead of holding timewarp down for 1,800 s, which shortens a lost world rather than
  altering a scored one. Watch the `arrived` counts on block 1.

### And the rest of the session does not reach a night, which was checked rather than assumed

Fourteen commits landed between 3cf and this night, almost none of them about accuracy. Each was
read against the ballistic path so that the next night's baseline can be compared with 3cd's:

| what landed | why it cannot move the miss |
| --- | --- |
| a round body's roll carried from where it left | `TubeGeometry.BodyRotationPartFrame` is the drawn body's rotation and nothing reads it back |
| the chase camera, and driving cameras from the step | camera only, and the same work on the same frames — it moved within the frame, not into it |
| **a third step hook**, `PreRenderHook` on `OnFrameCelestials` | the GUI pass is phase #7 and this is #11, so `FrameLatch` is already claimed on every frame a night flies. Nothing in the mod hides the UI, and the batch's screenshots are Windows screen grabs rather than KSA's own capture — so on a night this prefix is a no-op every frame |
| the nuclear cloud growing on the step | moved out of the draw, same frames |
| the boost axis | `Interceptor` only. A Mk 21 is `!Powered`, so it is a `Slug` and never reaches that code |
| the fire ladder's auto-only rungs, the station the trigger reaches | `FireHold` is a `readonly record struct`, `Hold` still holds on any reason, and the ballistic release never consults the ladder — a store is released by hand |
| the sight's window, the camera-window lease | additions to `KsaWorld`; every line the session removed there is a camera or a projection |

**The one that does reach a warhead is the reaper above**, and it is permissive and fail-safe in
every direction that matters. **Flown, once, before the night rather than after it**: one
`scenario.sh mirv` at this night's aim on `SOLVER SCALE 8` — **PASS, 8 of 8 flights, 48 of 48
warheads arrived**, misses 4-69 m with a group spread of 1-4 m, nothing reaped, and no exception in
either log. That is the reaper in the loop for eight full ballistic flights, which is what the
commit that added it did not have.

## 3ch. The walk night flew and recorded nothing — the trace dies with the bus — 2026-09-08/09

Item 29 flew. **All fourteen blocks, 112 flights, 112 usable, every one PASS**, finished at 02:27
with no exception in either log. And its declared endpoint was empty:

```
coverage: 55 of 112 usable flights carry one (49%)
arm            flights   median m
base                55        7.00
steep: no shot flew both it and base
       so nothing above is an arm comparison
```

8 released and 4 traced to landing in **every one of the fourteen shots** (one had 3), and the four
were the same arm every time. The report refused to compare rather than printing a confounded
ratio, which is 3cg's coverage line doing its job — in the morning, after the night was spent.

### The bus breaks up before its own warheads arrive

`IcbmComputers.Retire` drops a computer when its craft dies, and `IcbmComputer.Update` returns
early on a dead craft, so nothing steps the trace it owns. A bus has no heat shield and its
warheads do:

```
10:43:26.215  GeoSat FAT 5_1 destroyed - 6 round(s) still in the air
10:43:38.778  CAPTURE impact: round 6 down 0.03 km from the aim point
```

The rounds go loose and land correctly — `GoLoose` already covers that. Only the measurement is
lost, and it is lost silently, because the flight still lands and is still scored.

**That is why it selects an arm rather than thinning both.** The steeper the arc, the sooner the
bus is destroyed relative to its own warheads. The steep arm loses every trace and the shallow one
loses none:

| | shot 001 releases | traced to landing |
| --- | --- | --- |
| base, 32.1 / 32.0 / 32.0 / 32.1 deg | 23:02:26 – 23:03:14 | **4 of 4** |
| steep, 41.4 / 41.6 / 41.6 / 41.8 deg | 23:06:36 – 23:06:47 | **0 of 4** |

It is the same fault 3ce found and half closed. That comment is still in `TraceSetup`: *Measured on
2026-09-08: all eight traces began, four finished.* Latching `_tracedWarhead` fixed the launcher
going away; the craft going away was the other half of it.

### Neither thing that looked could have seen it

Both checks were real and both were blind for the same reason.

* **The endpoint was priced on `2026-09-08-decompose`**, which is `paired <none>` — single arm.
  Eight rockets flying one variant land in one window, and it traced **60 of 64, 94%**.
* **The pre-night verification flight was a bare `scenario.sh mirv`** — also single arm, and it is
  in 3cg as *PASS, 8 of 8 flights, 48 of 48 warheads*.

**A single-arm run cannot produce this fault at any n.** It needs two arms whose buses die on
opposite sides of their own impacts, which is the design the endpoint was declared for and neither
check exercised.

### It was on the page, correctly described, and filed under the wrong cause

3cg's own risk list says: *6 of 8 on the smoke shot below — one draw, and the two that went dark
were the last two to release.* That is the mechanism, written down before the night. It was read as
a draw from 3ce's known-random loss path rather than as a deterministic selection on arc steepness,
and the 75% floor it set was checked in the morning.

**The protocol was right and the timing was wrong.** Coverage is a property of shot one; the night
spent three and a half hours confirming it.

### Fixed, and flown

* `IcbmComputers.Retire` **holds a computer whose trace is outstanding**, bounded by that round's
  flight rather than by the session, with the attitude hook released so it flies nothing meanwhile.
  `Update` takes a dead-craft path that steps the trace and nothing else, re-deriving only the aim —
  a place on a turning planet moves through Cci every frame, where `Parent`, `Body` and the warhead
  profile are latched and do not. Both delegates the prediction needs already read the planet.
* `ScenarioRunner` holds a `Settling` phase until no computer has an outstanding trace. This was
  built first, **on an inferred mechanism, and it fixed nothing** — the last impact and `END` did
  land in the same millisecond, but a trace that no longer exists does not need more frames. It is
  kept because it is a real second-order fault and because it is what gives the dead-craft path a
  frame to run in.
* `shot-report.py --instrument` counts landings against releases straight off the logs, so it
  answers after **one** shot, and `shot-batch.sh` **stops the night** below 75%. Reads 50% on the
  wasted night, 94% on the one that priced the endpoint, 100% after the fix.

Flown in the configuration that produced the fault — one paired block at 26.485S,68.148W on
`SOLVER SCALE 8`, trace on: **8 away, 8 landed, 100%**, PASS, no exception, and both arms carry a
walk where before only the baseline did.

**The shape this ought to be** is `GoLoose`: a trace holding a `Celestial` and a captured name and
never the dead `Vehicle`, stepped by something that outlives the bus. What landed instead retains
the computer, which keeps a destroyed craft reachable for the length of one flight — narrower, and
against the letter of the rule in `CLAUDE.md`. Worth revisiting if a second thing ever needs to
outlive a bus.

### What it still does not answer

Item 29 is **unflown, not refuted**. Nothing here is evidence about the arrival angle.

The miss endpoint was scored for all 112 flights and reads **0.83x [0.55, 1.12], signed-rank
p=0.042, 10 of 14 paired shots** — not significant at the protocol's 0.0294, with the interval
still admitting 0.6, so *UNRESOLVED, open*. That is what 3cg predicted for it (0.85–0.90x,
unresolved at this n, 0.37 power), and it is the one prediction the night did test and confirm.

What is solid at n=112 and needs no rank test:

| | base | steep |
| --- | --- | --- |
| arc flown | 32.0 deg | **41.7 deg** |
| off rails, median share of coast probes | 14% | **3%** |
| corrections ended by payback | 21 | **3** |
| corrections ended by the clock | 5 | **22** |

The arm does what it claims, and 20b's worry is answered again in the same direction as 3cd: the
steeper arm spends **less** of its coast off rails, not more.

## 3ci. Item 29 flown twice, and the endpoint it was declared on is the wrong instrument — 2026-09-09

Two 14-block paired nights at 26.485S,68.148W, `base|steep:ArrivalPreference=0.65`, the instrument
of 3ch working: `~/shots/2026-09-09-walk2` and `~/shots/2026-09-09-walk3`, 112 flights each, 100%
trace coverage on both.

**The pre-registered answer to item 29 is UNRESOLVED, open.** Not "no effect": the honest combined
read over 26 shots, with the nuisance parameter fitted out of sample and the p from the design's own
null, is **walk 0.86x [0.62, 1.23] p=0.40** and **miss 0.78x [0.55, 1.15] p=0.065**. The interval
still admits 3cg's predicted 0.71x and admits 1.2 as well.

### The two nights disagreed, and the disagreement is the estimator

| | walk2 | walk3 |
| --- | --- | --- |
| walk, as the report ships it | 0.72x p=0.027 | **1.12x** p=0.217 |
| walk, seat levels from the *other* night | 0.87x p=0.301 | **0.86x** p=0.761 |
| walk, levels from `2026-09-08-decompose` | 0.71x p=0.007 | 1.07x p=0.903 |
| walk, level-free per-seat estimator | 0.77x | 0.97x |
| un-levelled, which the report already prints | 1.43x | 1.07x |

`_seat_levels` fits its divisor from the night under test and documents it as *a property of the
world rather than of anything under test*. Take it out of sample and **the reversal disappears** —
two nights that read 0.72 and 1.12 read 0.87 and 0.86, agreeing to within 2%. The point estimate
moves further on the choice of that nuisance parameter than on anything the arm does.

**It is not seat 3**, despite dominating the raw magnitudes at 33-58 m against 1-7 m; dropping it
moves either night by under 0.02. It is the *flat* seats, and the reason is the log format: the
trace prints whole metres and about 30% of flights read |walk| <= 3 m, so seat 8 reads 0,0,1,1,1,1,1.
Dithering each value inside its own print bin swings the answer **+/-9%**. The median-of-four is not
scale-equivariant per seat either — changing one divisor changes *which* seat is the median.

### The signed-rank is anti-conservative here, by two to four times

Randomising the design's own null — flip which roster parity is "steep", per shot, refitting the
levels each time:

| | report's signed-rank | randomisation |
| --- | --- | --- |
| walk2 walk, 12 shots | 0.027 | **0.045** |
| walk2 miss, 12 shots | 0.042 | **0.134** |
| walk3 miss, 14 shots | 0.030 | **0.114** |
| walk3 walk, 14 shots | 0.217 | 0.399 |

**Under a valid null nothing on either night resolves at 0.0294, on either endpoint.** Every
"RESOLVED" printed today was an artefact of a test that does not account for the levels being
refitted from the same data.

### The walk is one warhead of six, and the miss is the mean of six

`WarheadTrace` follows **round 1 only** — one landing per flight. So the declared endpoint has the
variance of a single warhead while the endpoint it was meant to beat is a six-warhead mean, and
round 1's own miss correlates with its own walk at only rho +0.52 to +0.60. 3cg's power table
compared the two as though they were the same quantity measured two ways. They are not.

### And the walk is not 70% of the miss at this target

3cf priced it at 10.5 of 15.0 m on `2026-09-08-decompose`. Over these two nights the per-flight
median `|walk| / |total|` is **0.43** (walk3 base) and **0.53** (walk2 base). The dilution argument
that made the walk the preferred endpoint is roughly halved, and 3cg's 0.85-against-0.37 power
figure does not hold at this composition.

### What the arm actually does — the decomposition that closes

`miss = (release probe - target) + (walk from probe)`. The walk is measured **from the probe**, so
everything upstream of release is invisible to it — and on walk3 that is where the whole effect is.
Signed downrange, positive long, recovered by fitting each seat's aim point from its own 14 landings:

| walk3 | base (n=56) | steep (n=52) |
| --- | --- | --- |
| pre-release, median | **-8.00 m** | **+1.47 m** |
| pre-release, sign | **52 short / 4 long** (p = 5.5e-12) | 22 / 30 |
| walk, median | -2.50 m | -3.50 m |
| total, mean | -16.16 m | -5.38 m |

Of **10.8 m of systematic short bias removed, 7.7 m is pre-release and 3.1 m is walk**, against a
7.5 m fall in the median miss. The accounting closes, and the sign result reproduces on walk2
(base -8.30 m, 37 short / 11 long, p = 1.1e-4).

**3cg's `cot γ` was arithmetically right and attached to the wrong term.** `tan(32.0)/tan(41.7) =
0.701`; the measured *pre-release* ratio is 0.62x (walk3) and 0.70x (walk2). Both bracket it. The
walk does not.

The one thing the arm does consistently on both nights is tighten the group: **3.0 -> 2.0 m, 14 of
14, p=0.000** on walk3 and 10 of 12 on walk2.

### The confound no rotation can remove

**The two arms are never measured at the same time.** In all 28 shots the base rockets release 0-62 s
in and the steep ones 122-297 s in, with **zero overlap** — the lateness is caused by the arm under
test, so seat rotation cannot break it. Their warheads therefore fall in separate windows running at
different world steps:

| | base descent step | steep descent step |
| --- | --- | --- |
| walk2 | 22.8 ms @ 1.00x | 29.4 ms @ ~1.30x |
| walk3 | **17.6 ms** @ 1.00x | 29.6 ms @ ~1.72x |

The environment change between the nights (resolution lowered, clouds off — confounded with each
other) improved **only the baseline arm's** step, by 0.77; steep's was already pinned at the ceiling.
Applying `1/0.77` to walk2's 0.80x lands near 1.04, which is most of the reversal. **Not proven** —
the within-arm step range is too narrow to measure an elasticity, and rounds sub-step so a coarser
world step may not reach the integration.

**The number that bounds all of it:** three hours apart, same target, same seats, the *baseline
arm's own* walk moved **x0.758**. The baseline's session drift is larger than the 0.71x effect being
chased.

### The exclusion this night's own tooling applied was not legitimate on walk2

3ch's frame-time rule was committed at 15:15:48 and walk3 started at 15:15:59 — eleven seconds. It is
pre-registered for walk3 and **post-hoc for walk2, the night it was derived from**.

Worse, the claim made for it — that the two lost shots were arm-neutral — is false. The *count* of
lost flights was equal, 8 and 8, but the misses were not:

| | base | steep |
| --- | --- | --- |
| shot 004 | 85.2 / 95.4 / 84.7 / 73.1 km | 107.2 / 102.5 / 94.6 / 92.0 km |
| shot 014 | 101.9 / 82.8 / 80.0 / 73.6 | 113.3 / 107.9 / 97.2 / 99.3 |

They are **the two most anti-steep shots of the night on both endpoints**. Over all 91 ways of
dropping two shots the estimate ranges 0.697-0.978 with median 0.817; the pair that was dropped is
**5th of 91** and one of only **2 of 91** reaching p <= 0.0294. Neither shot dropped alone clears the
bar. Both are the same crossover phase, so dropping them breaks the 7/7 balance to 7/5.

**The pre-registered walk2 answer is 0.80x [0.56, 1.19] p=0.268.** The 0.72x should not stand.

What survives is that the two worlds were genuinely broken — 3ci's own cause below — and that
SHOT-PROTOCOL's pre-existing lost-mode split reaches the same two shots independently.

### Why those two worlds broke — and the frame-time gate was measuring a symptom

Not the frame. `burn_frame_ms` samples only the `dt=` lines `WarheadTrace` prints, and the trace
starts at the **first release** — measured 37 ms after the first release summary. The window opens
minutes after the trim has already given up. Ascent frame time, which could have been causal, does
not separate the shots at all: **21.6 and 21.7 ms on the two failures against 19.5-22.9 healthy**.

The cause is that **the coast left the inertial frame**. Disposing of a spent stage routes through
`PartFailure.ShedDebris(vehicle, 12)`, which replaces one vehicle with up to twelve; `CollectShedStages`
takes its census once, a frame later, so catching them is a race. The two lost shots ran 7 fewer
disposals and carried 7 more vehicles (16 against 9). A world that size stays **one physics bubble** —
`ComputeMergeStateCore` forces `IsRailsCoasting = false` for any multi-member bubble, so it can never
split again — its origin ends up on a landed craft at ~5 km, and `GetDesiredBubFrame` then returns the
**rotating** `Ccf`. In a `Ccf` bubble `ComputeDerivatives` puts the fictitious forces behind a
per-vehicle `InPhysicsRadius` test that a bus at 890 km fails, so it is advanced in a rotating frame
as though it were inertial, and `TryToPutOnRails` has no path back.

| | Cci + on rails | Ccf + off rails |
| --- | --- | --- |
| shot 004 | 200 probes, 0.0000 m/s | **721 probes, 11.36 m/s each** |
| shot 003 (sound) | 933 probes, 0.037 m/s | 56, all after release |

Summed over one craft's pre-split coast that is **104.2 m/s against a trim that owed 113.3** and
refused to fire. This is 3bv's predicted mechanism, and these two shots are the flight that settles it.

The gate now reads that directly. Summed `|off-gravity|` over pre-release coast probes, across all 28
shots: **34-54 on the twenty-six sound ones, 1305 and 1321 on the two lost** — a 24x gap with nothing
in it, against 1.3x for the frame time it replaces.

### A harness artefact worth fixing before the next night

Five warheads across the two nights ended `burst` rather than `landed`, **all steep, all seat 1**
(5 of 14 against 0 of 14, Fisher p=0.041). They hit the Pantsir: `BallisticScenario` moves the
defended site *to* the aim point so the impact has a camera on it, and `AimSpread` anchors seat 0 on
that same point. Confirmed by the trace's own stop height — all bursts stopped **2.0-7.6 m above their
own ground crossing** where all 264 landings read +0.0 or below, against a 4.7 m Pantsir.

Fratricide is ruled out: all six warheads of a group detonate within ~1 ms, so nothing is airborne
when the first goes off. Steepness is probably not the cause either — the shadow a craft casts up its
approach is `H·cot γ`, **7.5 m at 32° against 5.3 m at 41.5°**, so the shallow arm should clip it more.

It biases *against* steep, because the excluded warheads are steep's smallest walks at that seat
(3, 2, 2, 6 m). Re-scoring them as landings moves walk3 from 1.12x to 1.09x — about a third of the
excess, not the reversal. `shot-report.py` counts them, splits them per arm and says so loudly when
they land on one side.

### What to do before flying this again

1. **Fix the estimator first.** Declare the seat levels in advance from a prior single-arm night, or
   use the level-free per-seat form; validate p with the shot-flip randomisation rather than the
   signed-rank, which is anti-conservative here by two to four times.
2. **Log the walk sub-metre.** Whole metres costs +/-9% of the answer for nothing.
3. **Trace more than round 1**, so the declared endpoint is not a single warhead against a
   six-warhead mean.
4. **Record the per-arm descent step**, or hold the world step for the whole flight. Until then every
   arm that shifts release time is confounded with falling in a faster-running world — which is
   *every* `ArrivalPreference` night ever flown, 3cd and 3ch included.
5. **Stop stage disposal shedding debris.** `Universe.DestroyVehicle(v, CrewDisposition.EndMission)`
   removes a vehicle and sheds nothing, where `DestroyVehicleFromEvent` sheds twelve. Unflown, and it
   is the initiating term of the whole failure class.
6. **Spread seat 0 off the anchor, or exclude the defended site from contact on a scored run.**
7. **Do not trust 3cg's 0.85 power figure.** The achieved intervals were 2.1x and 1.49x wide on the
   same design.

**The estimator is the first of these and blocks the rest**: a night whose answer moves further on
its own nuisance parameter than on the arm cannot settle a 0.71x effect at any n.

### The new estimator, calibrated on identical code — 2026-09-09

Null nights built the way `ShotArms` builds real ones — split a single arm's roster into two
pseudo-arms by seat parity, flipped per shot — so the true ratio is **1.000** by construction and
anything called RESOLVED is a false positive.

| null | n | point, median (range) | interval | signed-rank | shot-flip |
| --- | --- | --- | --- | --- | --- |
| `2026-09-08-decompose`, 8 shots | 996 | 0.993 (0.81-1.24) | 1.64x | 3.5% | 1.7% |
| **`walk3` baseline arm, 14 shots** | 588 | 1.006 (**0.45-2.37**) | **2.33x** | **6.1%** | **3.1%** |

**At the design's own shot count the signed-rank is twice too permissive and the shot-flip is
nominal.** At eight shots it is the other way about — the rank test reads 3.5% and the flip test is
conservative at 1.7% — so the correction is a property of this n rather than of the test, and both
are recorded because one of them alone would have justified either choice. The verdict is read off
the flip test because 14 blocks is the design that is actually flown.

**And the range is the finding.** On *identical code*, fourteen blocks produce point estimates from
**0.45x to 2.37x**, with a median interval 2.33x wide. Both nights of this section — 0.72x and
1.12x — sit comfortably inside what the estimator returns when there is nothing there at all. That
is not a claim that the effect is absent; it is the statement that **this instrument cannot see
0.71x at fourteen blocks**, whatever it prints, and no amount of re-flying the same design changes
that.

## 3cj. The instrument flown against itself, and what fourteen blocks can see — 2026-09-09

`~/shots/2026-09-09-null`, 14 blocks of `base|control` — two arm names, no settings on either, so
every rocket flies identical code and the true ratio is **1.000** by construction. All 14 PASS,
112/112 traces, no bursts, no exception in either log, no shot near the coast floor.

| endpoint | reads | interval | width | shot-flip p |
| --- | --- | --- | --- | --- |
| miss | **1.00x** | [0.78, 1.15] | 1.47x | 0.969 |
| walk | 1.05x | [0.72, 1.26] | 1.75x | 0.608 |
| **release** | 0.93x | [0.85, 1.14] | **1.34x** | 0.578 |

**The instrument says "no difference" when there is none**, on all three endpoints, and it does so
tighter than the synthetic null of 3ci predicted (median width 2.33x). That is the first thing any
of the six changes built that day has actually demonstrated.

### What it can see, measured by injection rather than assumed

A known factor multiplied into one arm's flights **before** any permutation, on this night's own
scatter, with the whole estimator re-run — levels refitted, shot-flip null:

| endpoint | 0.60x | 0.70x | 0.80x | 1.00x |
| --- | --- | --- | --- | --- |
| **release** | **0.018** | 0.034 | 0.089 | 0.579 |
| miss | 0.054 | 0.072 | 0.128 | 0.971 |
| walk | 0.033 | 0.073 | 0.282 | 0.630 |

**`release` is the most sensitive of the three and the walk the least**, which is the opposite of
3cg's ordering and follows from where the effect lives. Fourteen blocks resolve 0.60x on it and
land at **0.034** for 0.70x — just outside the 0.0294 bar, so the design 3cd, 3ch and 3ci all flew
was **marginal for the effect they were looking for even with a working instrument**.

Two caveats on the table. It is one arrangement of one night rather than an expectation over
nights, so it is indicative and not a power calculation. And the injection is a uniform
multiplication where the real effect is a **bias removal** — the base arm carries 8 m of systematic
short and the steep arm carries none — which is not the same shape.

### Built the same day, all unflown

1, 2 and 5 landed on 2026-09-09, plus the seat-0 standoff. The shot-flip null is now what decides
the verdict — reproducing an independent computation to within 0.02 on all four readings — and
`--levels-from` fits the divisor out of sample. **Cross-levelled, the two nights read 0.87x and
0.88x.** The walk is logged to centimetres, the camera's site stands 250 m off the aim, and disposal
removes a stage rather than shedding twelve pieces of it.

What is **not** built is 3: the trace still follows round 1 only, so the declared endpoint is still
one warhead against a six-warhead mean. And 4 is recorded rather than fixed.

None of it has flown. The next night is the first evidence any of it works, and the first thing to
check is not the arm but whether the two nights' own baselines agree.

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

## 4b. Item 8's throughput is bought out of the trim's precision — priced 2026-09-06

Section 4 offers `minTargetFrameRate = 10` as **2.4x for a config line**, and the ranked plan says
to do it before 5b-7 if a night is short. It is not free, and what it spends is the one term 3be
just identified as the arrival ceiling.

`dtPlayer = min(elapsed, 1 / MinTargetFrameRate)` is the step the trim is integrated across, and
`BusTrim`'s own docs measure its precision as linear in that step: **0.118 m/s left on the bus at
33 ms, 0.245 at 66, 0.420 at 108, 0.750 at 200**. So the two are the same lever pulled in opposite
directions. On 5b's 8-rocket world, 78.7 ms frame:

| `MinTargetFrameRate` | step | trim residual | throughput | vs `BusTrim.MaxFaithfulStep` |
| --- | --- | --- | --- | --- |
| **30 — today** | 33.3 ms | **0.119 m/s** | 1.00x | inside |
| 20 | 50.0 | 0.183 | 1.50x | inside |
| **16** | 62.5 | **0.232** | **1.88x** | **inside** |
| 15 | 66.7 | 0.248 | 2.00x | outside |
| **10 — as proposed** | 78.7 | **0.298** | 2.36x | **outside** |

**At 10 the step leaves `BusTrim.MaxFaithfulStep = 0.066` and the residual is 2.5x today's.** That
constant is not a preference: it is the step at which the trim's stop threshold,
`max(SettledMetresPerSecond, 0.5 x accel x step)`, stops being reachable, and past it the trim
settles wide and reports itself done.

**Sixteen is the row that fits.** 1.88x of the 2.36x, with the step still inside the trim's own
bound — most of the throughput and none of the boundary violation.

**What it costs in metres is not derivable from here and must not be guessed.** The naive product
of residual and `dMiss/dV` is wrong: a night with 2.6 m/s still owed at release lands at 18.5 m,
so the mapping from the trim's books to the ground is not that. **Fly it, on the miss, against a
same-night baseline** — it is a paired arm like any other, not a setting to adopt on a throughput
number.

**And the free version is the one section 4 already names.** `WorldSpeed.ForStep` turns "I want this
step" into a speed request, so the burn and the trim can hold today's step while ascent and coast
run long. That is the shape that gets the throughput without paying for it — and it is code rather
than a config line, which is the part the "2.4x for a config line" headline hides.

Ordering: this collides with **5f** (why the trim owes 4.19 m/s at a 54 degree floor). Both are
about the same actuator's precision, and 5f should be measured on today's step before anything
coarsens it.

## 4c. Item 21 was already built, and the bus passes it

Item 21 asked for a gate that a *rotation* command's enrolled nozzle set has zero net force —
`checkring.py --translation` reads six-axis translation authority, and nothing was thought to check
the other direction.

**It does, and has since `333f7b0`.** `analyse` computes `leak` as net force per unit torque per
axis per side, marks it `<-- not a couple`, counts it as a problem, and `--check` exits non-zero
with the reason: *a rotation command that is not a pure couple shoves the vehicle every time it
corrects its attitude*. `check-all.sh` runs it on every push, so it has been green all along.

The shipped bus:

```
KSArmory_Prefab_MirvBus: 20 thrusters, mass seated at X=0.300 Y=0.000 Z=0.000
    roll  quantum  6.240   floor  0.000    8 enrolled   leak 0.000
    pitch quantum  4.412   floor  4.412    8 enrolled   leak 0.000
    yaw   quantum  4.412   floor  4.412    8 enrolled   leak 0.000
```

**Zero on all three axes**, so commanding attitude does not translate the bus. That is worth having
as a positive result rather than only as a closed item: it independently rules the mod's own
thrusters out of the coast push, leaving 3ax's shared-bubble integration as the account, which is
what 20b is flying against.

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
| ~~5e~~ | ~~Re-check the latched arrival floor against the state the burn **leaves** the vehicle in~~ | done | **refuted: the exit reaches steeper than the latch can afford, 77.8 against 67.8 — and the ceiling is the trim's debt, 2.6 to 4.19 m/s** — 3be |
| ~~5f~~ | ~~Why the trim owes 4.19 m/s at a 54 deg floor against 2.6 at 44~~ | done | **it is asked for 4.5x more, not failing to pay: 0.76 m/s owed at 44 deg against 3.41 at 54, and only 54 ever hits the 10 m/s ceiling** — 3bo |
| ~~5g~~ | ~~Name the craft on the cutoff line, read the residual per arm~~ | built `2d28003`; headless half done | **the cutoff residual is a minor term: 1.59x where the demand is 4.5x, and 0.06-0.10 m/s against 0.76-3.41** — 3bp |
| ~~5h~~ | ~~Read `split debt` on a steep-arrival arm~~ | done, 1 shot | **the debt is the coast's length: the steep arm holds its reference 1.92x longer and owes 2.6x more, agreeing per second. Not the angle** — 3bu |
| ~~25~~ | ~~The aim correction stops 25x above the miss~~ | done, 14 paired | **the stopping rule was blind and unblinding it changes nothing: `noimprov` 31 to 3, miss 0.88x [0.84, 1.10] unresolved** — 3bx |
| **26** | **Answered: the aim loop is not the limiter.** It converges monotonically to 1.4-12.4 m predicted on every flight; the landing correlates +0.07 with that and **+0.93 with the ground under that seat**, measured on another night | done | 3bz |
| ~~27~~ | ~~Re-fly the arrival angle where terrain is present~~ | done | **0.96x [0.66, 1.13], unresolved; the graded-by-roughness prediction is REFUTED at rho +0.12, p=0.79** — 3cd |
| **28** | ~~Make `WarheadTrace` cover the whole roster~~ | **mostly done** | **it was stranded, not sampled: 8 begun / 4 finished, now 7. Craft named; 3cd's 1.50x walk figure is void** — 3ce |
| ~~30~~ | ~~Fix the walk estimator~~ | **built 2026-09-09, unflown** | shot-flip null, `--levels-from`, centimetre logging. Cross-levelled the two nights read **0.87x and 0.88x** where in-sample they read 0.72x and 1.12x — **the nights never disagreed, the divisor did**. Still to do: trace more than round 1 |
| **30b** | **Trace more than round 1**, so the declared endpoint is not one warhead against a six-warhead mean | medium | **3ci** — the last of the estimator faults, and the one that needs a change to `WarheadTrace` rather than to the report |
| ~~31~~ | ~~Stop stage disposal shedding debris~~ | **built 2026-09-09, unflown** | `KsaWorld.Remove` → `Universe.DestroyVehicle`, which sheds nothing where `DestroyVehicleFromEvent` sheds twelve. **3ci/3bv** — inference, not measurement: it removes the only discriminator, but nothing yet proves it breaks the chain |
| **32** | **Record the per-arm descent step**, or hold the world step for the whole flight | small | **3ci** — the arms never overlap in time and steep always falls in a faster-running world, which confounds *every* `ArrivalPreference` night ever flown, 3cd and 3ch included |
| ~~29~~ | ~~Re-fly the arrival angle scored on the WALK~~ | **flown twice, 2026-09-09** | **UNRESOLVED, open — 0.86x [0.62, 1.23] on 26 shots, and the walk is the wrong endpoint: the effect is in the PRE-RELEASE term, where cot γ predicts 0.70 and it measures 0.62-0.70.** Blocked on the estimator, not on shots — **3ci** |
| **30** | **The pre-release residual, ~7 m, does not follow the ground.** A different term from the walk and nothing has attacked it | not started | 3cf |
| **26a** | **Name the craft on the `aim:` line** so a bias can be paired with its own rocket's miss per cycle rather than only at release | done | 3by |
| **5i** | **Read the same line on a flight that actually breaches the ceiling.** 3bu is the ordinary behaviour at 0.87 m/s; 2148 read 3.410 with refusals at 20-26. The failure is something on top | free on any night that breaches | 3bu |
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
| **8b** | `orbitSolvers`, the three offscreen viewports never cleared, the off-rails coast — the throughput levers that are **not** paid for out of the step | hours | section 4; unlike `minTargetFrameRate` these cost the trim nothing — 4b |
| ~~9~~ | ~~Hand the terminal fraction of the burn to `FlightComputer.Burn`~~ | days | **dropped** — abolishing the residual entirely buys ~9 m at 2,000 km and nothing at 12,902 (3x) |
| **13** | **Why the committed arrival drifts 26 s** — one shot in twelve, all eight rockets, 75-99 km, `trim` median 92.41 km | 0 shots then 12 | 3ak: the largest single item at this geometry, and unexplained — **but 3an bounds the drift to 0-8 s across all 52 divergences, so it is not what the `trim` terminator is; see 17** |
| ~~12~~ | ~~Why the epoch sign runs at −0.6~~ | done | **the diagnostic had the sign backwards; the fix was justified and is applied** — 3al |
| ~~2b'~~ | ~~Re-sample the ground per sub-step in the terminal phase~~ | done | **refuted headlessly: 0-2 m on smooth ground, and chaotic rather than convergent on rough (−2,781 m at 22 ms, −7 at 33, −2 at 50). Re-sampling changes which feature the round stops on; it does not converge** |
| ~~15~~ | ~~Log what `DensityRatioAt` returns through the coast~~ | done | **refuted: 0 of 2,009 samples non-zero through 52 divergences — the air and the drag model are cleared** — 3an |
| ~~17~~ | ~~**Why the aim bias walks to 94 km**~~ | done | **the coast is being integrated instead of propagated: the bus spends 70% of the coast off rails against 1% healthy, and accumulates ~2.4 m/s per probe of non-gravitational velocity, which at 91.5 km per m/s is the walk. Replicated in two independent worlds to six figures** — 3ar |
| **18** | **The stage census adopts the neighbours, and a distant adopter disposes the stage before its owner can.** `MayDispose` measures clearance from the disposing craft, so a neighbour 20 km away has its gate open at once while the owner waits for 1 km — 98% of disposals read foreign, none nearer than the 19.2 km pad spacing. An attribution fault, NOT a retention fault: one rocket disposes its own three stages at 1.0 km, and the divergent worlds dispose as much as the healthy ones | 0 shots | 3at |
| ~~16~~ | ~~Make each headless fixture state its own arrival geometry~~ | done | **`ArrivalPreference = 0.5` ships as the default; 15 cases across 7 classes now state their geometry through `FixtureGeometry`, 1,854 pass** — 3ao |
| ~~14~~ | ~~A per-craft coast probe~~ | done | **caught the failure: sharp onset at 505 km, accelerating, and the guard proven not to be the cause** — 3am |
| ~~20~~ | ~~**Stop driving attitude through the coast.** Rails is gated on `anyActuatorCommanded`, not on bubble membership~~ | done, twice | **works and was unbounded: 0.15x on divergent worlds, 89x on healthy ones** — 3ba |
| **20b** | **Fly the quiet window to a verdict on divergent worlds.** Healthy is settled (harmless, twice). The divergent case is 4 of 5 worlds in favour, and the endpoint is the lost-mode MEDIAN, not the rate | ~3 nights, or 1 per divergent world | 3bi: 85.84 km to 50.35 within the lost mode; the rate does not move |
| **20c** | **Check the frame-time regime on the next divergent world** — free, already logged | 0 shots | 3bi: the two disagreeing worlds sit either side of the 24 ms boundary, 23.3 against 26.5 |
| ~~19b~~ | ~~**Log which of `PhysicsBubble`'s conditions holds a bus off rails**~~ | done | **built and verified: on a healthy world 11 of 34 off-rails probes are `neither actuator flag`, and `FreefallNeedsFullPhysics` fits the 6% arithmetically** — 3bg |
| **19c** | **Read `Cci`/`Ccf` on a divergent world.** The frame decides whether off rails is recoverable at all, and the diagnostic is built and public | free on the next divergent world | 3bv: `Ccf` closes the account, `Cci` refutes it |
| **24** | **Force rails through the coast** — `Props.SetOnRails(true)`, public, makes the bubble irrelevant rather than escaping it. Only compatible with QuietCoast, and must be released before the deployment settles | build, then 14 paired | 3bv: this is QuietCoast's missing half |
| **8** | `minTargetFrameRate` — **16, not 10**, and flown as a paired arm on the miss rather than adopted on the throughput number | 14 paired shots | 1.88x throughput for 0.232 m/s of trim residual against today's 0.119; at 10 the step leaves `BusTrim.MaxFaithfulStep` — 4b |
| ~~21~~ | ~~**Gate a rotation command's nozzle set to zero net force**~~ | done, and already green | **`checkring.py` has measured it since `333f7b0` and `check-all.sh` gates it: the shipped bus leaks 0.000 on all three axes, so the coast push is not the mod's own thrusters** — 4c |
| **23** | **Make the ascent know how far the target is.** Every shot under ~800 km is identical because guidance takes over on dynamic pressure, by which point the stack has ~3 km/s and the shot needs less | 0 shots to reproduce | 3bk: a 100 km target is flown exactly as an 800 km one |
| ~~22~~ | ~~**What actually merges the bubbles**~~ | done | **a 197 m race that cannot be won: a bubble's envelope is the spread of its members, so a rocket holding its stage `s` away reaches `5s` and touches the 20.04 km neighbouring pad at s=4.00 km, against a 4.194 km split radius — and MergeBubbles runs before the step, SplitBubbles after. Not the lever; the actuator command is** — 3ax |
| ~~19~~ | ~~**What puts the bus off rails mid-coast**~~ | done | **a shared physics bubble. `PhysicsBubble.cs:1340` needs `NumVehicles < 2` for the rails path; bubbles merge on proximity and only ever leave on a parent or frame change, so it never ends. 3 of 12 worlds, 519-538 probes each, push 90% cross-track and identical across worlds. Not the flight plan (margin 394 s against 495-948 healthy)** — 3au |
| ~~10~~ | ~~Fly `AimWithinTrimBudget` to a verdict~~ | done, 14 paired | **does not help: 1.11x [0.94, 1.29], and the interval now excludes better than 0.94x. The divergent endpoint drew 0 of 14 worlds** — 3br |
| **5h** | **The steep-arrival trim demand, which is NOT the divergence.** 2148's p80 failures were arm-specific, 3 of 24 with base at 0, in worlds that did not diverge. ~3.6x of the 6.2x is unexplained after the cutoff residual and the lighter bus | 0 shots to price headlessly | 3br: **the one on the path to rung C** |
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
5. ~~**`KSA-TERRAIN.md`: "there is no raycast, no collider query."**~~ — **this correction was
   itself wrong, withdrawn 2026-09-07.** `BoundingVolumeHierarchy.LookupBvhDirection` is a
   direction-to-triangle lookup on the **undisplaced sphere mesh**, not a terrain query:
   `CubeMesh.cs:280` builds the BVH from unit-sphere LOD vertices, and
   `BoundingVolumeHierarchy.cs:643` normalises the hit vertex and then calls
   `GetTerrainHeightFromDirCcf` to find the radius. It cannot answer where the ground is without
   asking the height field anyway. `KSA-TERRAIN.md` keeps its "no raycast" line. The Bepu collider
   half stands but is not reachable — `TerrainPatch`'s entry points want a `ReadOnlyPhysicsStates`
   ref struct, and the patch only exists within 8 m of clearance.
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
The `clock` terminator as a cut-off loop worth more budget (a pooled median of 1.92 km that was 55
broken flights of another arm wearing the label; baseline `clock` is the *best* ending at 15 m --
3bc).
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
