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
- [x] 2. Make a hop's time and budget belong to the hop (headless, flagged) -- 2026-10-18, rig only
- [ ] 3. Spend the Heavy bus's tank, rolled once for the walk
- [x] 4. Decide the route to 50 km (rig pricing, a decision, no build) -- 2026-10-18: A and B, C deferred
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
0.66-0.67), and **~0.95 at 12,902 km** (rig: six fit 10 km apart and not 11; flown hops 1.16). The rig's 12,902 km arc
coasts 2,726 s against the 1,772 s flown on GeoSat FAT, so the flight may get less there. Coasts above 100 km:
351 s at 2,000 km, 1,315 s at 6,179 and 1,772 s at 12,902 (`ICBM-GUIDANCE.md` ~1680).

Spacing each budget buys, *inferred* from that model (12,902 km at reach 1.0):

| bus, budget | 3 targets, 6,179 / 12,902 km | 6 targets | 7 targets |
| --- | --- | --- | --- |
| Heavy, 60 (today) | 17 / 27 km | 7 / 11 km | 6 / 9 km |
| Heavy, ~130 (step 3) | 40 / 62 km | 16 / 25 km | 13 / 21 km |
| post-boost, ~500 (engine) | 160 / 250 km | 64 / 99 km | 54 / 82 km |

**Measured in the rig with step 2 on and a 140 m/s budget** (`WideSpreadRigTests`, the Heavy bus at 0.55 m/s² and
2.9 t): six targets **16 km** apart and three **40 km** apart at 6,181 km; six **25 km** and three **60 km** apart at
12,902 km; every stop under a centimetre, none curtailed. Whether the tank pays 140 is step 3's question.

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

**Done 2026-10-18, rig only, `HopsSizeTheirOwnSlots` off.** Built as listed, with two departures. The slot is
`max(65.1, 48 + hop / a)`: 48 s is `MedianHopSeconds`' clearance and four passes without the split null only the first
stop pays (`ReleaseItinerary.HopOverheadSeconds`), and `a` is `LauncherProfile.TrimAccelerationMetresPerSecond2` --
0.56 on the Heavy bus (flown), 0.40 on the post-boost bus (its four forward nozzles, bridge). The budget the plan and
the passes read is the flight's own, an unlimited (negative) one as no bound (`ReleaseLoop.PlanningBudget`). Left
alone: the warp's `UnplannedWalkSeconds` (a set not yet planned), and the rig's two `MedianHopSeconds` calls, which
are the legacy `WalkAimsAtEpoch` path rather than a planned walk.

What it measured (`WideSpreadRigTests`, `WalkPricingTests`):

- **The clock is the part that matters.** Three targets 60 km apart at 6,181 km, budget 140: with the trim's clock
  left at 120 s the 91 m/s hop stops at 72 ("ran out of time") and stop 2 lands **10.3 km** from its target; with the
  clock following the burn, under a centimetre. The slots move the plans little at these spacings.
- **The budget is what widens the walk**: planned on 60, six at 16 km walk fewer than six; planned on 140, all six.
- **Price against flown, 140 m/s:** 0.83-1.07 at 6,181 km (over 1.0 only on the 40 km set's first hop, 60.0 against
  58.2), but **1.03-1.08 at 12,902 km** on every stop (22.2 against 21.2 at 25 km, 46.5 against 42.9 at 50 km). Not
  "well over", but a walk planned to the budget's edge at long range can run dry, so step 3's guard keeps a margin of
  at least 10% there. Why it is over at long range and under at 6,181 km is not known.

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

Pass: the flag ships, and the table above gets measured rows. **The goal has no "enough"** (the user, 2026-10-18): the
widest spacing physically possible with accuracy unharmed, so carry on to step 4 whatever this step reaches.

**Headless half built 2026-10-18, under `HopsSizeTheirOwnSlots`; the night is still to fly.**

- **The guard** reads the bus's tank once it is alone (`KsaWorld.TryTankMassKg`, the craft's own mass) and holds the
  trim budget, the passes' and the planner's under `ReleaseLoop.TankCeilingMetresPerSecond`: what is spent plus nine
  tenths of the rest at `LauncherProfile.TrimWorstAxisExhaustVelocity` (2,906/1.414 on the Heavy bus, a lateral m/s
  costing 1.41x an axial one). Before the split it reads nothing and the setting stands. It binds at the hand-over:
  the hop it cannot pay is refused and the stop takes the warheads left (`CurtailTheWalk`).
- **Rig, with a tank** (`PastCutoffRig.PropellantKg`, 134.3 kg, nozzles stopping when it is empty), budget 200: six at
  10 and 16 km and three at 30 km from 6,181 km, and six at 10 and 16 km and three at 30 and 50 km from 12,902 km, all
  fly, none near the bottom (at most 108 of 134 kg). **Three 50 km apart from 6,181 km does not**: unguarded the jets
  run dry mid-hop and stop 3 lands **15.1 km** out; guarded the third hop is refused and every warhead released lands
  under a centimetre (`WideSpreadRigTests.ABudgetPastTheTankEndsTheWalkAStopEarlyRatherThanRunningDry`).
- **The slider** runs to 200 m/s with the flag on, its tooltip saying the tank bounds it.
- **The roll, deferred.** In the rig the 6,181 km hops are almost all axial (107 kg for 122.8 m/s, about what an
  axial burn costs), so one roll would buy nearly nothing there; at 12,902 km about a fifth of the propellant goes to
  the lateral premium (108 kg for 94.3 m/s). The rig rolls its frame to keep down toward the planet, so this is the
  rig's attitude, not the game's: read the axis split off a flown walk's trim lines before building it.
- **Left**: the night above, which needs the walk flags (step 1) or flies them as arms.

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

**Decided 2026-10-18: build A, fly B, defer C.** Measured (`WideSpreadRigTests`):

- **B needs nothing new.** The post-boost bus on its nozzles under steps 2-3 (0.40 m/s², 3.8 t, 500 kg at 2,912 m/s)
  walks **seven targets 50 km apart from 12,902 km**, every stop under a centimetre, 365 of 500 kg; at 70 km the guard
  ends it at six. From 6,181 km the coast binds before the tank: the slots run 270-390 s, so three at 50 km, five at
  30. The rig's 12,902 km arc coasts 2,726 s against 1,772 flown, so B's long-range figure is the optimistic one.
- **A is the largest lever there is.** A 50 km along-track hop from the coast's start costs 42.4 m/s pinned and 15.7
  free at 6,181 km (**2.7x**), 24.9 and 4.3 at 12,902 km (**5.8x**), the same ratio at 300 km, the arrival moving
  about 10 s per 50 km. Across the track it buys nothing. Every bus gains it, and since a cheaper hop is a shorter
  burn it also eases the coast that binds B at 6,181 km. *Inferred*: B with A spreads seven targets roughly 5x wider
  from 12,902 km before the tank binds, and the Heavy bus's six at 25 km becomes ~140 km.
- **C only buys time** at the ranges where the nozzles are too slow for the coast (6,181 km and under), and A cuts the
  burn it would be buying time for. Re-price it after A flies.

Accuracy is the open question for A: each warhead arrives at its own instant (seconds apart), and the aim correction,
the release gate and the post-boost passes all assume one. Step 5 has to show the landing does not move.

### 5. Build the chosen route

Headless, behind a flag. Per branch:

- **A** (chosen): free the arrival per stop through the five assumptions, each with a rig test that fails against the
  pinned form, and the walk's landing per warhead held against the pinned walk's at a spacing both fly.

  **Built 2026-10-19, rig only, not passed.** Behind `IcbmConfig.HopsFreeTheirArrival`, off: a hop calls
  `IcbmProgram.FreeTheHopArrival`, which on the next coast frame commits `TryCommitTheCheapestArrival` and falls back
  to the pinned `ResolveCoastArc`; the reach is flown free-clock and priced per axis on the minimum-norm solve
  (`DivertFootprint.TryAxisPrice(..., ArrivalClock.Free)`); and a slot keeps `(s/T)^1.2` of the measured reach
  (`ReleaseItinerary.FreeSlotExponent`: the free along-track reach never fell faster than `(s/T)^1.156` over 4,549
  pairs of epochs across 54 arcs, cross-track never faster than linear). The passes after a hop still re-solve pinned
  to the hop's new arrival, which is the intent. `PastCutoffRig` mirrors all of it.

  Measured in the rig, both buses on their own tanks (Heavy 134.3 kg, post-boost 500 kg), budget 1,000 so only the tank
  bounds it, every walk flag on, targets laid uprange of each other along the track. Every free walk lands under 1 mm
  but one (defect 2); the tank never runs dry, and every curtailment is `BeyondTheBudget` against the tank. Widest
  spacing walked in full, every stop under a centimetre:

  | Bus, range | Free | Pinned |
  | --- | --- | --- |
  | Heavy, 6,181 km, 6 targets | 40 km | 16 km |
  | Heavy, 12,902 km, 6 targets | 60 km | 25 km |
  | Post-boost, 6,181 km, 7 targets | 50 km | 3 stops at 50 km |
  | Post-boost, 12,902 km, 7 targets | 100 km | 50 km |

  A free hop costs 2-2.5x less than a pinned one, and at 12,902 km flies at or under its price.
  `WideSpreadRigTests.AFreeArrivalWalksEveryStopWhereThePinnedOneFallsShort` pins the first, second and fourth rows:
  at those spacings the pinned walk stops at 2, 2 and 4. The arrival moves 3-62 s earlier per hop.

  These numbers are after `faf487e5`: the rig held the stack keep-out on after the first release, which the game does
  not, and that was the cause of every kilometre-scale free miss seen before it.

  **Two defects keep the step from passing:**

  1. **The plan goes stale after a turning split**, in the game and the rig alike. Once a turning bus has split,
     only a plan with the same number of stops replaces the walker's; when the re-priced one has fewer, the walker
     keeps the plan made before the split and turn, priced on the pre-turn nose (axis-sum factor about 1.07 against
     the 1.47 flown). Heavy 6,181 km at 25 km: the walker held hop 1 at 10.4 m/s and it flew 15.0.

     **Built 2026-10-19, three research flags, rig only.** Letting a smaller plan replace it
     (`WalkShrinksAfterTheSplit`, the predicate `ReleaseLoop.MayReplan`) makes the prices honest but on its own
     shortens walks (Heavy 6,181 km at 60 km: 4 stops to 2). The stale plan was never bound by the tank at all,
     because the tank is read only after the split, so it promised more than the fresh one could. Two things priced
     the fresh plan too dear, and each now has its own flag:

     - **The tank at the bus's full mass.** `PriceTheTankStopByStop` charges each hop in kilograms at the mass the
       bus has by then, every earlier stop's warheads gone (`ReleaseItinerary.Bus.HopKg`, the 10% margin on the
       exhaust velocity, so at one mass it is exactly the old ceiling). Under it a slot's burn also follows the
       lighter bus. On its own it moves no walk: the slots, below, were the binding term.
     - **Every slot on the worst-case decay.** `(s/T)^1.2` is the fastest fall over 54 arcs, and at 12,902 km the
       flown hops are flat (45.5 to 46.1 m/s over six hops) while it priced them 46.8 to 89.0.
       `FreeHopsMeasureTheirSlots` flies the reach again from the coast Kepler-carried to four epochs down to the gate
       (`DivertFootprint.TryFreeLater`, evenly spaced on log axes, about 1 ms each every 5 s), and prices each hop on
       its own cost there (`ReleaseItinerary.SlotCurve`), never cheaper than now and the worst case past the gate.
       What the trim pays for an along-track hop on a held nose is convex in the time to go on log axes, 0.449 to
       1.130 m/s per km from 1,484 s to 396 s at 6,181 km, so each chord lies over it. The reach's length alone is
       nearly a straight line there (exponent about 1.06), which is why one exponent fitted between now and the gate
       priced the middle of the coast too dear.

     With all three, every hop flies within the tank's margin of its price
     (`WideSpreadRigTests.AFreeHopFliesWithinTheTanksMarginOfItsPriceOnceThePlanIsRemadeAfterTheSplit`, failing
     against the stale plan at 1.44x and 0.52x). Full walks, every stop under 1 cm in both columns:

     | Cell | A alone (stale plan) | A + the three flags |
     | --- | --- | --- |
     | Heavy 6,181 km, 6 at 25 km | 6, hops 1.44x their price | 6, 0.88-1.03x |
     | Heavy 6,181 km, 6 at 40 km | 6 | 4 planned |
     | Heavy 6,181 km, 6 at 60 km | 4, curtailed | 3 planned |
     | Heavy 12,902 km, 6 at 60 km | 6 | 5 planned |
     | Post-boost 6,181 km, 7 at 50 km | 7 | 7 |
     | Post-boost 6,181 km, 7 at 75 km | 5 | 5 |
     | Post-boost 12,902 km, 7 at 100 km | 7, last hop 0.52x | 7, 0.95-1.05x |
     | Post-boost 12,902 km, 7 at 150 km | 6, curtailed | 5 planned |
     | Post-boost 6,181 km, 2 at 300 km | stop 2 lands 24.8 km out | 1 stop |

     **Where the honest plan walks fewer stops, the rig is the likelier reason.** It flies a hop's overhead in 16-27 s
     against the 48 s the game measured over 96 buses (`ReleaseItinerary.HopOverheadSeconds`), so in the rig a stop
     releases before its planned slot (Heavy 6,181 km at 40 km: the fourth at 1,112 s to go against 1,064 planned),
     where the hop is cheaper. A game flight on the game's overhead would buy its hops where the plan prices them,
     and the stale plan would curtail or run the guard there. Which of the two the game agrees with is a flown
     question: step 6. Not yet known in the rig either: whether the fresh plan's first-hop price, about 10% over at
     Heavy 6,181 km and 40 km and under at 25 and 60, depends on when in the turn the last plan before the first
     release was made.

  2. **Post-boost 6,181 km, two targets 300 km apart: stop 2 landed 24.8 km out.** Not a shallow arc (33.5° before and
     after the hop, and an arrival floor of 25° changed nothing), and not the trim, which nulled cleanly. **Burn
     drift**: the hop's arc is an impulse at the hand-over, the trim nulls velocity alone, and a 180 m/s hop on
     0.40 m/s² nozzles burns for about 450 s, leaving the bus about `½·Δv·t` off the arc it is now matched to. The
     first reading after it was 44.5 km from the target, which the aim correction took for drag (bias 0.56 to 43.9
     km) and then oscillated on; the drift grows with the square of the hop, 11.9 km at 150 km apart and 20.6 at 200,
     where the passes did converge, at a price.

     **Built 2026-10-19, `HopsResolveAfterTheirBurn`, rig only.** Once a hop's trim is done, and before any pass may
     read a miss, the arc is solved again from where the burn left the bus (`FreeTheHopArrival`, or the pinned
     re-solve), while the round just flown spent more than one pass's 10 m/s, at most four rounds
     (`ReleaseLoop.ResolvesTheHopAgain`). The 300 km cell then lands at 0.0 m on 215.5 m/s against 305.6, and every
     free hop measured is 3-15% cheaper, because the passes no longer spend propellant chasing the drift: Heavy
     6,181 km at 40 km 115 kg against 122, post-boost 6,181 km at 100 km 286 against 363
     (`WideSpreadRigTests.ALongHopsBurnIsSolvedAwayBeforeTheAimCorrectionReadsItAsAMiss`). With it and the three
     flags above, the plan at 12,902 km prices every hop within about 1% of what it flies.

  Pass for A: flown hops at or under their price and every stop under a centimetre. Defect 1 is closed in the rig
  behind its flags, and defect 2 behind its own. What is left is flown: step 6, with all five flags on. Then re-price C
  at the post-boost bus's 6,181 km, where its coast binds.
- **B**: step 2's profile plus the post-boost bus's budget and acceleration; nothing else new.
- **C**: re-priced 2026-10-19 in the rig as an upper bound: the post-boost bus at 6,181 km with A and
  `HopsResolveAfterTheirBurn`, its hops pushed at 1.8 m/s² for the 6.8 kN engine on 3.8 t, no turn onto a hop's line
  or back onto the entry line, the rig's own hop overhead. Seven targets, every stop under 1 mm:

  | Spacing | Nozzles | Engine-rate hops | ...costed along the hop, not per axis |
  | --- | --- | --- | --- |
  | 50 km | 7, 246 kg | 7, 228 kg | 7, 175 kg |
  | 75 km | 5, the coast binds | 7, 342 kg | 7, 264 kg |
  | 100 km | 4, the coast binds | 7, 456 kg; 6 on the honest plan | 7, 355 kg |
  | 150 km | 3 | 5 of 7, curtailed | 6 of 7, curtailed; 5 on the honest plan |

  So at 6,181 km C moves the post-boost bus from five stops at 75 km to seven at 75-100 km, where the tank binds
  instead of the coast. It is worth building, and the turn each way per hop is the first thing its price leaves out.

  **Built 2026-10-19, behind `IcbmConfig.HopsBurnTheEngine`, off.** `Sim/HopBurn.cs` turns the bus onto the hop's
  velocity to gain, lights the engine once the nose has held on it (`BusTurn`'s band), steers the line onto what is
  left each frame and re-solves the arc every 2 s from where the burn has carried the bus, stops with 1 m/s left for
  the nozzles (or with what is left 20 deg off the nose), and turns back onto the line the warheads leave along. The
  trim, the passes and the release wait while it runs. `Ksa/Icbm/IcbmComputer.Hop.cs` actuates it: the engine made
  Active and lit for the burn, inactive after, the coast's throttle at full. The planner prices an engine hop along
  its length at the engine's 2,996 m/s, sizes its burn at the 1.70 m/s2 measured on the loaded bus
  (`LauncherProfile.HopEngineAccelerationMetresPerSecond2`), and adds `HopBurn.TurnsSeconds`, 27 s: the upper quartile
  of what 86 engine hops flown on 2026-10-19 took between releases besides their burn (68.4 s) less the 41 s every hop
  takes. It was 38 s before those flew. The bus holds its line until the warhead it has just let go of is 10 m away, twice
  the 4.4 m its other warheads sweep, or the turn carries them through it -- seen in the first flight, before the wait
  was in. In the rig, the
  post-boost bus's 500 kg, A with `HopsResolveAfterTheirBurn` and the plan flags, every stop under 0.1 mm:

  | Range, 7 targets | Nozzles | Engine |
  | --- | --- | --- |
  | 6,181 km at 75 km | 6 | 7, 191 kg |
  | 6,181 km at 100 km | 4 | 7, 257 kg |
  | 6,181 km at 150 km | 3 | 7, 398 kg |
  | 6,181 km at 200 km | 2 | 5 planned, the tank binding |
  | 12,902 km at 200 km | 4 of 5 planned | 7, 282 kg |
  | 12,902 km at 300 km | 2 of 3 planned | 7, 406 kg |

  An engine hop costs about two-thirds of the same hop on the nozzles (39 against 60 m/s at 100 km from 6,181 km):
  no axis sum, and no drift to chase. Priced within about 5% of what flew. Guarded by
  `WideSpreadRigTests.TheEngineWalksSevenTargetsAHundredKilometresApartWhereTheNozzlesRunOutOfCoast` and
  `HopBurnTests`. Not built: the bus engine's sequence made active, so KSA's Δv gauge reads (below).

  **Flown 2026-10-19 on `PBB SCALE 8`** (`~/shots/2026-10-19-engine100`): seven targets 100 km apart in longitude at
  24 S from 6,180 km, arms `nozzles` and `engine` (both with A, `HopsResolveAfterTheirBurn` and the plan flags),
  seat by seat. **The engine buses walked four stops each, the nozzle buses two**, every warhead released arriving.
  Twelve engine hops of 99.8-112.5 m/s, each burnt 49-55 s and stopped within 1.01 ± 0.03 m/s of what was asked.
  The hops cost about 100 m/s, not the rig's 39: the targets step in longitude, which here lies largely across the
  track, where a free arrival buys nothing, and the tank (about 360 m/s at this mass) took the walk to four. The next
  flight lays them along the track. 22 of 24 landed within 2 cm, one at 4.9 cm, and **one at 0.79 m**: bus 6's last
  stop, where the post-boost passes oscillated -- the landing moved about twice as far as each aim correction (254 m
  of aim, 621 m of impact) against 0.55-0.9 on its earlier stops, and `AimCorrection` re-learns its response only
  from moves over 500 m, so the bias swung 130-491 m for three passes and the release's own kick took 320 m back.
  Why the response doubled there is not known; one stop in twelve. The bus also swung its warheads through the one
  it had just released, turning straight onto the hop; it now holds until that one is 10 m away (above).

  **Flown again with the targets in latitude, 2026-10-19** (`~/shots/2026-10-19-engine100-lat`), 100 km apart
  southward from 24 S, the 10 m wait in: **every engine bus walked all seven and passed, 28 of 28 warheads within
  9.5 mm** (0.3-9.5 mm); the nozzle buses walked three or four, every warhead within 9.1 mm. 24 engine hops of
  45.8-77.9 m/s, under half the morning's across the track, each stopped with 1.00-1.07 m/s for the nozzles; the two
  turns took a median 12.4 s (9.4-12.9) against the 18 the slot allows. No pass oscillated. So at 6,180 km along
  the track the post-boost bus on its engine reaches seven targets 100 km apart in millimetres, against 4.5 km for
  six on the Heavy bus's shipped settings.

  **Flown at 12,902 km to Botswana, 2026-10-19**, targets along the track from each pad (`mirv:...;track:6xKM`),
  arms as above:

  - **200 km apart** (`~/shots/2026-10-19-engine-12900`): every nozzle bus walked 7, 28 of 28 within 18 cm; the
    engine buses 26 of 28 within 5 cm, but the first warhead on buses 2 and 6 landed 104 km out. Not the split and not
    the engine: on the pad the aim loop accepted a 250 km reading departed from the pad's solved cutoff, because its
    density gate was asked about that departure *coasted to the release*, which is out of the air. The loop ran to its
    300 km clamp on all four engine buses and, best while clamped, froze there on two; the nozzle arm's earlier walk
    gate happened to keep its coast in the air. Fixed in `a9de731a`; both rigs already gated on the cutoff itself.
  - **300 km apart, the fix in** (`-300`): **every engine bus walked all seven, 28 of 28 within 2.1 cm**, 24 hops of
    41-61 m/s burnt 19-23 s, each stopped with 0.99-1.07 m/s for the nozzles, 281-291 kg of the 500 kg tank spent.
    The nozzle buses walked four, within 16 cm. No bus touched the clamp; the same 254 km pad reading was refused.
  - **400 km apart, engine on all eight** (`-400`): four walked all seven (28 within 0.42 m); four were curtailed at
    the seventh hop, the tank 8 m/s short of it, and the stop-6 target took that warhead, released 424 s before
    arrival and landing 1.13-1.28 km out (inside the 5 km bar, a thousand times the rest).

  **What the 400 km night says is open:** every bus planned seven at about 377 m/s, and the hops flew dearer than
  priced by a factor growing stop by stop, 1.00, 1.09, 1.18, 1.29, 1.42 (bus 5: 54.9 to 86.4 flown on 55.1 to 64.1
  priced; it was already 1.25 on the last hop at 300 km). The stops were not late -- 69-94 s apart against a slot of
  about 115 -- but each free hop committed an arrival about 80 s earlier (92, 88, 83, 79, 77 s on bus 5), so the
  time to go fell by about 165 s a stop and each later hop was flown nearer arrival than it was priced at. The rig
  does not reproduce it: at 400 km it plans five, honestly, and flies 0.89-0.94 of each price with the arrival moving
  about 35 s a hop. The game's hops are also half the rig's (55 against 107 m/s priced at 400 km), so the rig's
  geometry (targets on a meridian, `GameStack`) is not this flight's (Cape Canaveral to Botswana along a 291 deg
  track); reproducing the night needs the flown geometry in the rig first. Separately, a curtailed walk's leftover
  warhead releases late and lands a kilometre out, where every walked stop lands in centimetres.

  **The cause, measured the next morning:** each free hop moves the arrival by its own amount, which the free solve
  already holds -- its minimum-norm velocity per metre dotted with the arc's arrival sensitivity, 0.234 s per km
  uprange at the first stop and 0.221 at the second, -93.5 and -88.4 s for 400 km against -92 and -88 flown
  (`FreeArrivalShiftTests`). `FreeHopsCountTheArrivalTheyMove` (`6c904b4f`) carries it into each stop's time to go.

  **The night of 2026-10-19/20** (`~/shots/2026-10-19-night`, declared in
  `~/shots/scripts-2026-10-19/DECLARE-wide-engine.md`, eight post-boost buses a block, paired):

  | Cell | Flags | Stops walked | Landed | Flown over priced, last hop |
  | --- | --- | --- | --- | --- |
  | A: 12,902 km, 200 km apart, nozzles v engine, 4 blocks | step 5 | 32 of 32 walked 7 | 224 within 0.35 m | engine 1.08 mean, 1.15 worst |
  | B: 12,902 km, 300 km apart, engine A/A, 4 blocks | step 5 + engine | 32 of 32 walked 7 | 224 within 0.52 m | 1.22 mean, 1.37 worst |
  | C: 6,180 km, 150 km apart, engine A/A, 3 blocks | step 5 + engine | 22 walked the 6 planned, 2 curtailed at the 6th | 142 within 0.17 m | -- |
  | D: 12,902 km, 400 km apart, engine v shift, 3 blocks | + `TurnsSeconds` 27 | 19 walked 7, 5 curtailed | none over 2 km; 5 leftovers 1.2-1.3 km | engine 1.41/1.46, **shift 1.02/1.05** |
  | E: 12,902 km, 200 km apart, nozzles v wait, 2 blocks | step 5 | 16 of 16 walked 7 | 112 within 0.23 m | -- |

  Engine against nozzles at 200 km landed 0.86x [0.59, 1.18] levelled, 0.87x [0.48, 0.93] un-levelled: no worse,
  about 14% tighter. Counting the arrival shift holds every hop within the tank's 1.11 margin of its price where the
  plain engine walk climbs to 1.46. What curtailed D's five buses was not pricing: **each one's first hop struck its
  own spent stack**. All 11 first hops of A, B and D burnt with the stack 36-187 m away were knocked off their line
  (rates from 0.0004 to 1.4 rad/s in half a second on the one read closely), the nozzles flying the rest for about
  76 m/s, and none of the 61 from 209 m or more; seats 2-4 carry it, by how slowly their stacks drift off.
  `EngineHopsClearTheStack` (`c9143f1f`) holds a burn whose path passes inside the stack's keep-out, at most 240 s;
  every engine hop now logs the predicted closest approach, so the next flight checks the prediction first.

  **A bus flies back through its first warheads** -- seen in game on FAT_1, then measured by the per-release line
  (`f4eea913`): on both arms of E the first two warheads of every bus come back to 0.9-1.1 m of the bus's origin, and
  warheads 4-7 never come nearer than they left. `HopsWaitForTheirWarhead` (`9f10c8c8`) removes it from warhead 3
  only, so the hop's trim is not the whole cause; not found yet. It costs no landing: every warhead in E is within
  0.23 m.

  **Flown 2026-10-20 morning**, 12,902 km, seven targets along the track, `PBB SCALE 8`:

  - **400 km, the shift on every bus, the stack hold on half** (`~/shots/2026-10-20-400-guard`): 7 of 8 walked
    seven. All four held buses waited 6-18 s for the path to clear the 15 m keep-out and none struck; of the four
    not held, the one whose path was predicted to pass its stack at 3 m struck it and was curtailed, while 4 m and
    12 m passed. Every landing within 0.75 m but that bus's leftover.
  - **500 km, both on every bus** (`-500-guard`): every bus planned six before its first release, prices held to
    0.95-1.02 of what flew and every first burn was clean -- and all eight were curtailed at the sixth hop, 2-4 m/s
    short. The tank check charged the hops alone; the nozzles also spend a median 2.90 m/s a stop (upper quartile
    3.04, 192 stops of the 300 km night). `4326a238` reserves it. So at 12,902 km on this tank the widest walk of
    seven is **400 km apart, 2,400 km end to end**, and 500 km apart is five.
  - **200 km on the nozzles, with and without the warhead wait** (`-200-nozzles`), the per-release line saying what
    the bus was doing: **the bus overtakes its warheads down its own axis.** At its closest every returning warhead
    sits at 0.0 m along the nose and 0.9-1.0 m off it -- a tube's offset -- with the trim firing Forward: the warheads
    leave forward along the nose at 0.5 m/s and the first stops' nozzle hops push the bus forward along the same
    line, overtaking them about 5 s after the release, or about 37 s after it with the wait. From stop 4 the hop
    fires Down and nothing comes back; an engine hop turns onto its own line first and never meets its warhead. So
    the cure is a sidestep before a forward hop, or turning the bus as the engine does, not a wait.

  What C was planned with, kept for the reasoning:
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
  - **Engine risks: retired 2026-10-19.** The centre of mass never leaves the axis in KSA: a released RV's 250 kg comes
    off the bus part's declared mass where it is (`Ksa/StoreMass.cs`), so the 6 cm and ~400 N·m inferred from the real
    hardware cannot occur. Flown on the bridge, `PBB Bus Alone` on a 300 km orbit under KSA's prograde hold: 56 s on
    the engine loaded, 95.3 m/s, error within ±0.50 deg the whole burn; one RV released (3,863 to 3,613 kg) and the same
    burn, 102.3 m/s, within ±0.48 deg -- the deadband's own slow swing either way, ve 2,995 against 2,996 declared.
  - **KSA's own Δv gauge reads 0 for the bus**: it shows the active sequence's Δv (`FindActiveSequenceDeltaV`), and the
    bus engine is staged with a release the flight never stages. C activates it per part, so make its sequence the
    active one at the split too, or a player watching the panel sees no Δv through every hop.

Pass: the branch's rig tests, every stop under a centimetre, the suite green with the flag off.

### 6. Fly it in game, once

On `TEST PBB GeoSat FAT` (or the Heavy bus for A), one two-target set 50 km apart along track: both stops walked, each
warhead within 2 km of its own target, the warp held through any burn, no exception in the KSA log. Still `refactor`,
flag off.

**Flown 2026-10-19 for A, twice, on `PBB SCALE 8`**: eight post-boost buses at 6,180 km, seven targets 50 km apart in
longitude at 24 S, arms `stale` (A, the walk flags, `HopsResolveAfterTheirBurn`) and `honest` (those and the three plan
flags), alternating seat by seat (`~/shots/2026-10-19-wide50`, `-wide50b`).

- **The first landed nothing.** A bus whose walk had dropped two targets still had its gate to wait for, warped to it
  with every warhead falling, and the world reached 56x against a warhead's 0.32 s step. Fixed in `43a28da4`: the
  coast warp never starts while a round flies (ICBM-GUIDANCE, "Waiting is warped through").
- **The second landed every warhead released, 25 of them, 1.2-6.9 mm from targets 50 km apart and one at 31.8 mm.**
  No coast warp, no step overrun, the world at the scenario's 8x. Hops flew 67.8-68.2 m/s, then 74.3-75.1, one 86.1, a
  re-solve round after each.
- **Both arms walked three of the seven** (one honest bus four). Every bus planned five at cutoff at 952 m/s, then cut
  to three before the split, which is the last re-plan the stale arm gets: hops priced 76.8 and 116.4 against 68 and
  75 flown, so the coast on 0.40 m/s2 nozzles binds -- what C's engine is for. The cut from five to three wrote no
  line, because the stop count was collapsed with the other numbers; `39b342b6` keeps it, not yet seen in flight.

For C, first:

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
