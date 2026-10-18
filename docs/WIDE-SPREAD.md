# Wide MIRV spread

**A plan, not a record.** How one bus gets its warheads onto targets tens of kilometres apart instead of the ~7 km it
reaches today, in the order to build it. Each step says what it needs, what proves it, and where a failure sends the
plan. `ICBM-OUTSTANDING.md` 4.12 points here; `POSTBOOST-BUS.md` is the post-boost bus's sizing and art.

Drawn up 2026-10-18 from three surveys and two reviews of the draft. Numbers marked *inferred* are arithmetic or a
reading of code and have not been run; everything else names where it was measured.

## How to resume

1. Read **Progress** below; the first unticked step is the one to work. A step's commits carry its number in the body
   (`git log --grep "WIDE-SPREAD step"`).
2. Read **Baseline** and the step. Every step names its files, its tests and its pass/fail.
3. Check which research flags are on today in `src/KSArmory/Sim/IcbmConfig.cs` (step 1 flips three).
4. When a step lands: tick it, write what it measured into the step itself (not a new section), and update 4.12's
   status line in `ICBM-OUTSTANDING.md`. A step with no flight behind it is committed as `refactor` or `test`, behind a
   flag that ships off; it becomes `feat` in the commit that turns the flag on after a night.
5. Nights are bedtime batches (start one when the user hands off, never while they are at the machine), declared in
   writing first, `--paired`. Never send a bridge command to the user's game without asking.

## Progress

- [ ] 1. Flip night for the walk flags
- [ ] 2. Make a hop's time and budget belong to the hop (headless, flagged)
- [ ] 3. Spend the Heavy bus's tank, rolled once for the walk
- [ ] 4. Decide the route to 50 km (rig pricing, a decision, no build)
- [ ] 5. Build the chosen route (headless)
- [ ] 6. Fly it in game, once
- [ ] 7. The wide nights
- [ ] 8. What a player sees

## The goal, in numbers

"Tens of km" means nothing until the target count and the range are fixed, because the cost is gaps x spacing.
The planner charges `16.1 + gaps x S / reach` m/s (`ReleaseItinerary.SingleTargetMetresPerSecond`; reach per m/s on
the narrower axis, per-axis priced, arrival pinned) against the budget plus the 10 m/s split reserve.

**Baseline**: shipped ~4.5 km for six at 6,179 km; **7.0 km in the rig** (`HopCeilingRigTests`, six at 6,181 km, all
three flags on, at the 70 m/s edge). Planner reach **~0.65 km per m/s at 6,179 km** (rig, from that fit; flown hops
0.66-0.67). At 12,902 km it is *not measured*: the pinned straight-line reach at cutoff is 1,647 m per m/s
(`ICBM-GUIDANCE.md`), so planner-priced it is ~1.0-1.3; **step 2's first job is to measure it**. Coasts above 100 km:
351 s at 2,000 km, 1,315 s at 6,179 and 1,772 s at 12,902 (`ICBM-GUIDANCE.md` ~1680).

Spacing each budget buys, *inferred* from that model (12,902 km at reach 1.0):

| bus, budget | 3 targets, 6,179 / 12,902 km | 6 targets | 7 targets |
| --- | --- | --- | --- |
| Heavy, 60 (today) | 17 / 27 km | 7 / 11 km | 6 / 9 km |
| Heavy, ~130 (step 3) | 40 / 62 km | 16 / 25 km | 13 / 21 km |
| post-boost, ~500 (engine) | 160 / 250 km | 64 / 99 km | 54 / 82 km |

So **three targets 50 km apart at 12,902 km may need no engine at all** (~110 m/s, inside step 3), and six or seven
at 50 km need either the post-boost bus's propellant or a cheaper kilometre (step 4). Freeing the arrival per stop
multiplies the along-track reach by 2.56 at 6,179 km and 11.94 at 12,902 from cutoff (`DivertFootprint.ArrivalClock`),
and every wide set is laid along track anyway, so it is priced against the engine in step 4 before either is built.

## What bounds a wide set, whatever the bus

| Limit | Where | Wide-set consequence |
| --- | --- | --- |
| Planner budget fixed at 60 | `ReleaseItinerary.Bus.BudgetMetresPerSecond` defaults to `PostBoostAim.MaxTrimMetresPerSecond`; neither caller passes the config (`IcbmComputer.Reach.cs`, `PastCutoffRig.cs`). `BusTrim` and `CanFlyTheHop` already read the config (`IcbmComputer.Walk.cs`) | raising the setting widens nothing until it is plumbed |
| Correction passes' budget fixed at 60 + reserve | `PostBoostSituation` in `IcbmComputer.Trim.cs`; the rig passes 60 with **no** reserve | binds once spend passes 60; the rig and game disagree there |
| Panel slider stops at 60 | `Ksa/Ui/UiIcbm.cs`, tooltip says so | a player cannot raise it |
| Hop slot fixed at 65.1 s, burn not included | `ReleaseItinerary.MedianHopSeconds`; read by `GateFor`, `CoastFits`, `IcbmComputer.Warp`'s `UnplannedWalkSeconds`, and **hardcoded twice in the rig** (`PastCutoffRig.cs` ~357, ~474, whose `HandOver` also ignores `GateFor`) | late slots release late and lose priced reach; `CoastFits` promises stops that do not fit |
| 120 s per hop, counted while `MayFire` | `BusTrim.MaxSeconds`, reset on `Resume` | ~46-80 m/s hops on 0.56 m/s² jets run out of time |
| 120 s per stop for the passes | `PostBoostAim.MaxSeconds`, running through the hop | same |
| Coast length | above | **long shots only (6,000 km and up)** |
| Pre-cutoff outline | `DivertFootprint.TryAtTheEpoch` at the 420 s gate | refuses pad clicks the flight could fly; scenarios bypass it (`AddTarget` checks only `MaxTargets`) |
| Per-axis cost on jets | `DivertFootprint.WorstAxisFactor`, up to 1.73 | an along-track walk has one bearing, so one roll recovers it (step 3) |
| Scenario seat spread | `AimSpread`, 6 lethal radii, the same shift for a rocket's whole set | **lay sets along track**, or fly one rocket |
| Not binding | `TargetSet.MaxTargets` 7, save/load (7), Mk 21 lethal 2 km; pricing linearity (columns differenced at 0.01 m/s, the hop flown as an exact pinned re-solve) | |

**1.23** (the 1,000 km misses) is ranked above this work and owns the first-pass ceiling path
(`PostCutoffSequence.CeilingFor`, `ReleaseLoop.HopCeilingFor`). This plan reads those, never changes their meaning; if
1.23 changes them first, step 2 re-reads them. Wide spacing is above 6,000 km, where 1.23 does not reach.

## The steps

### 1. Flip night for the walk flags

A bedtime batch; nothing depends on the game being free otherwise, and steps 2-3 can be built meanwhile **in a
worktree** (shot-batch refuses a dirty tree and builds every arm before the first flies).

- Arms, all with the RV turn on (it is on for walks from cutoff since `fd621b3e`): `base`; `flags`
  (`WalkStartsAtCutoff=true,HopsFlyTheirOwnCeiling=true`); `flags+chord` (plus `PriceSlotsOnTheGateChord=true`).
- Cells: six targets 4 km apart and three 12 km apart, along track from 24.0 S 62.0 W (flat), `SOLVER SCALE 8`,
  6 blocks; and **a ~2,000 km walk cell**, the flip blocker 4.12 still lists.
- Template: `~/shots/scripts-2026-10-15/DECLARE-walk-coverage.md` and its `night.sh`; a command looks like
  `KSARMORY_SCENARIO_SAVE='SOLVER SCALE 8' ./tools/shot-batch.sh --aim '24.0S,62.0W;24.108S,62.0W;...' --paired 'base|flags:WalkStartsAtCutoff=true,HopsFlyTheirOwnCeiling=true|chord:...' --blocks 6 --out ~/shots/<night>`.
  Read with `shot-report.py --paired --endpoint landing` (and `trim`) and the `TARGET k of n` lines; grep every KSA log
  for exceptions.
- Bars, declared first: stops walked per bus, every warhead within lethal radius, landing ratio's upper bound under
  1.5 (six blocks bottom out at p=0.031: a bound, never a win).

Pass: the flags ship on, and the chord if its arm holds. Fail: steps 2-7 still build, flying the flags as arms, and
the failing cell is the next thing to understand.

### 2. Make a hop's time and budget belong to the hop

Headless, 1-2 days. Shared by every route. Everything new is behind one research flag (call it
`HopsSizeTheirOwnSlots`), so a shipped walk does not move; `GateFor` and `CoastFits` are shipped behaviour.

- **First, measure the planner's reach at 12,902 km** in the rig (the same fixture at 12,902 km) and write it into
  **The goal, in numbers**.
- **Failing tests first**: `HopCeilingRigTests`/`WalkRigTests` at 25 and 50 km at 6,181 and 12,902 km with the budget
  raised. They must fail today on the slot and the hop clock, not only the budget, or they prove nothing.
- **Budget plumbing**: `Config.TrimBudgetMetresPerSecond` into `ReleaseItinerary.Bus` (both callers) and into
  `PostBoostSituation`; the rig gets the game's split reserve. Its default stays 60, so nothing moves.
- **Slot per hop**: `max(65.1, base + burn time)` at the bus's acceleration, per stop, through `BeforeArrivalSeconds`,
  `GateFor`, `CoastFits`, the warp's `UnplannedWalkSeconds` and the rig's two hardcoded calls. Tests asserting
  `MedianHopSeconds` directly stay valid with the flag off.
- **Clocks per hop**: `BusTrim.MaxSeconds` and `PostBoostAim.MaxSeconds` scale with the hop's price.
- **On the profile, only what step 3 needs**: a budget and an acceleration on `LauncherProfile` (`BusPerformance`),
  the Heavy bus at today's values. The pass ceiling and hop clock move later, with an engine (`POSTBOOST-BUS.md` says
  so). Run `pack-api.py --check` and `check-docs.sh`.
- **Mass in the rig**: a warhead's mass off per release; jet acceleration scaled by mass.

Pass: the new tests pass with the flag on, every stop under a centimetre, price against flown spend 0.8-1.0, the full
suite green with the flag off. Fail on linearity (flown well over price at 50 km): stop and re-price before step 3.

### 3. Spend the Heavy bus's tank, rolled once for the walk

Headless 1 day, then one night. The first large hops, and possibly the whole goal for three targets.

- **Propellant guard first**: the budget the trim may plan is what the tank left can pay, read off the bus, so a budget
  past the tank is refused rather than found by the trim going dry. At full load the tank pays 143 m/s fore/aft, 101 on
  one lateral axis, 72 across both (`ICBM-GUIDANCE.md` ~1388), so the per-axis budget, not one number, is the guard.
- The Heavy bus's profile budget to what the guard allows (~120-140, *inferred*); the slider's limit from the profile,
  its tooltip rewritten. A budget over 60 also reaches single-target shots, through the correction passes only.
- **One roll for the whole walk**: an along-track walk's hops share a bearing, so rolling the bus once at the split
  puts them on one jet axis (1.0-1.41x instead of up to 1.73x). Check it leaves `PointRvsAtEntry`'s turn intact, which
  fixes the nose and leaves the roll free. Attitude propellant per stop is counted.
- Rig sweep: budgets 60/100/130 x spacings 7-60 km, three and six targets, 6,181 and 12,902 km, along track.
- Night: `base` (60, no roll) against `tank` (guard + roll), six targets at the spacing the rig says, 6,179 km, and
  three targets 50 km apart at 12,902 km if the rig flies it; keep one single-target cell. Bars: stops walked, every
  warhead within 2 km of its own target, no "ran out of time", no curtailed stop.

Pass: the flag ships, and the table above gets measured rows. If three at 50 km flies at 12,902 km, ask the user
whether that meets the goal before building more.

### 4. Decide the route to 50 km

Headless, ~2 days, **pricing only**: each branch gets a rig number before any is built.

- **A. Free arrival per stop** (4.12 step 5): along-track reach x2.56 at 6,179 km and x11.94 at 12,902 from cutoff.
  Five things assume a pinned arrival (`MIRV-TARGETS.md` opening section): the release gate's `toArrival`,
  `AimCorrection.IsSteady`, `_releasedTheArrival`, `PostBoostAim`'s per-flight counters, `ResolveCoastArc`; the
  precedent is `FreeTheCoastArrival`/`TryCommitTheCheapestArrival`. Price: a hop solved with `TryCheapest` per stop in
  the rig, its cost against the pinned one at 25 and 50 km, and how far each warhead's arrival moves.
- **B. The post-boost bus on its nozzles only**: 526 m/s, ~0.4 m/s² axial from four nozzles (*measured* 390 N each,
  `POSTBOOST-BUS.md`), seven at 50 km at 12,902 km (1,772 s coast). Burning ~300 m/s takes ~750 s: does it fit?
- **C. Engine hops, open-loop**: a rig engine (6.8 kN, ve 2,996 m/s, mass flow, a frame of latency), a centred
  impulsive burn for the hop's velocity, `BusTrim` and the passes nulling the rest. Measure the residual. If it is under
  one pass, closed-loop guidance is not needed. Check here, not later, whether seven stops at 50 km fit the 1,315 s
  coast (*inferred* ~640 s of burning and overhead).

Decide by the cheapest branch that reaches the target count the user wants, and write the decision and its numbers
here. A and C combine: a free arrival makes every engine hop cheaper.

### 5. Build the chosen route

Headless, behind a flag. Per branch:

- **A**: free the arrival per stop through the five assumptions, each with a rig test that fails against the pinned
  form.
- **B**: step 2's profile plus the post-boost bus's budget and acceleration; nothing else new.
- **C**: first the engine risks below, then:
  - `Sim/HopBurn.cs` only if step 4 said open-loop leaves more than a pass: point (`BusTurn`, capped), burn re-solving
    every ~0.25 s with `BurnoutGuidance.TrySteer` **from the current position** (a ~90 m/s burn at ~1.8 m/s² carries the
    bus ~2 km off the hand-over reference and `BusTrim` nulls velocity only), stop by its own rule leaving ~1 m/s for
    the nozzles (`TrySteer`'s cutoff is 0.01 m/s). **Refuse when `HeldTheArrival` is false**: `TrySteer` falls back to
    `TryCheapest` and abandons the committed arrival without failing.
  - Sequencing: hand-over, the burn when the bus has an engine and the hop is over the nozzles' ceiling, then
    `CorrectCoastArc`, `_trim.Resume`, the turn back onto the new target's entry line, the residual trim, the passes,
    release. The turn state resets every hand-over (`_turnLeg` latches at 3 and `TurnIsDue` also needs
    `_postBoost.Cycles == 0`). While burning: release held, `_mayTrim` false, **`BusTrim.Update` not stepped** (its
    best-acceleration memory and 25% rule would strike off live nozzles), and predictions and `_measureDue` held,
    since a thrusting coast state predicts nothing. A new flag rather than `IsBurning` (which freezes the aim
    correction), reaching every `IsBurning` reader that must see it: the warp policy, `IcbmComputers.AbandonBurns`,
    `SecondsToArrival`, `ArrivalIsIfReleasedNow`, `IcbmComputer.Prediction`.
  - The profile's pass ceiling, hop clock and acceleration for an engine; the planner reads them.
  - **Engine risks, retired before C's code, on the bridge with the user's permission (~1 h)**: one ring RV released
    moves the centre of mass ~6 cm off axis (*inferred*), ~400 N·m at 6.8 kN against ~390 N nozzles and no gimbal —
    burn 10-20 s on the bus alone and read the hold back with `attitude`. The engine on/off path already passed on the
    bridge (`POSTBOOST-BUS.md`, 2026-10-17). Fail: paired releases, a smaller engine, or a gimbal, and step 4 again.

Pass: the branch's rig tests, every stop under a centimetre, the suite green with the flag off.

### 6. Fly it in game, once

On `TEST PBB GeoSat FAT` (or the Heavy bus for A), one two-target set 50 km apart along track: both stops walked, each
warhead within 2 km of its own target, the warp held through any burn, no exception in the KSA log. Still `refactor`,
flag off. For C, first:

- **Ignition**: `VehicleCommand.SetEnginesActive` switches every engine on the craft. Add a per-part form for the bus's
  own engine after the split, and rewrite its "bridge only" rule to name this use.
- **A player's own craft**: KSA puts an unsequenced engine in stage 1, so a design `make-rocket.py` did not stage lights
  the bus engine at liftoff. Handle it (refuse to arm, or hold the engine inactive) and say so on the panel.
- The coast branch's `SetEngine(false)` becomes `SetEngine(true)` and the burn attitude while burning (it already
  drives the throttle to 1). Read `BoosterPerformance` off the bus. One log line per hop: velocity asked, burn seconds,
  residual, kilograms.

### 7. The wide nights

- A paired night against the nozzle walk at a spacing both fly, then the wide cell alone: the chosen target count 50 km
  apart along track at 6,179 km, then 12,902 (~19 min a block x1.6 for re-flies, ~3 h a cell for 6 blocks).
- Bars: stops walked, every warhead within 2 km of its own target, no curtailed stop, no ATTRIBUTION FAULT, and the
  per-warhead landing stated (an earlier release falls further: the 0.02 m/s stop band is 55 m on the ground at cutoff
  against 14 m at the gate).
- Pass: the flag ships on, as `feat`.

### 8. What a player sees

Each a small change after 7:

- **The pad outline at the walk's epoch** (4.12 step 3): the floor to 1,700 s is measured,
  `0.88 - 0.00034 (t - 760)` of `t_go`; it needs a coast estimate that comes in short. Pass: no pad click refused that
  the coast's own reach accepts, on the step 7 sets.
- **The reach ring and the planet map at hundreds of km**, where per-target rings go sub-pixel (`ICBM-MAP.md`). Pass:
  readable in a capture at both ranges.

## Risks

- **Warp**: KSA rejects a speed change while its own auto-warp runs, and `WarpPolicy` then abandons the burn. A long
  hop burn must be covered by the guided-burn path (`IcbmComputer.Warp`).
- **Attitude propellant**: a stop that burns turns three times (onto the burn, onto the airflow, back); none of it is in
  the budget today.
- **Linearity at 90 m/s hops** is argued, not shown: step 2's price-against-flown at 50 km is the check.
- **The Heavy bus's margin is thin early**: the first ~16 m/s is always spent fully loaded.
