# Improving accuracy at eight rockets

**What this is.** Eight rockets in one world is now a usable measuring instrument — eight scored
shots for the wall-clock cost of one. This is the state of it, what is known about what limits the
accuracy, and what to do next, ranked. `docs/MIRV-NEXT.md` items **8s** through **8ab** are the
flown record; this is the plan.

**Read 8z, 8aa and 8ab first — between them they replace most of what follows.** A correction that
runs to completion lands at **140 m** and every other ending at 5–45 km, so there is no accuracy
work outside making the trim finish. The eight-rocket instrument could not measure any of it
between runs and now compares arms *inside* one. And the demands that read as a diverging solve are
aim movements priced honestly, which is a different problem with a different fix.

## Where it stands

Flown 2026-08-27, `SOLVER SCALE 8`, one shot per rocket at 12,902 km.

| run | build | passing | misses (km) |
| --- | --- | --- | --- |
| 1 | before the frame fixes | 3/8 | 0.043, 0.045, 0.053, 9.2, 11.1, 21.1, 25.8, 26.5 |
| 2 | before the frame fixes | 0/8 | 3.8, 5.3, 12.4, 27.7, 30.9, 33.4, 39.0, 40.6 |
| 3 | + bomb sight, + drape | 8/8 | 0.058, 1.1, 2.97, 3.16, 4.05, 4.10, 4.39, 4.59 |
| **4** | **+ camera claim** | **7/8** | **0.008, 0.023, 0.107, 0.145, 0.189, 0.583, 4.37, 5.86** |

For scale, four single-rocket runs the same day gave **3.75, 4.09, 4.80 and 5.04 km**. Run 4's
median is **0.15 km**, so eight rockets is not merely as good as one — the good runs are an order
of magnitude better, and one shot landed at **8 m**.

**The spread between runs is the problem, not the level.** Runs 1 and 2 were the same build and
differed completely. Any change flown from here has to be judged against that, which is what
`docs/SHOT-PROTOCOL.md` is for.

## Sixteen does not work, and why that matters here

| | vehicles | sim rate | passing |
| --- | --- | --- | --- |
| 8 | 17 | **1.00x** | 7/8 |
| 16 | 33 | 0.71–1.12x | **0/16** |

At sixteen the cruise is fine and the **terminal phase collapses**: `draw` measured **175 ms** a
frame with 96 warheads in the air. So the eight-rocket instrument is what there is, and widening it
is a drawing problem rather than a guidance one — see *What is not worth doing yet*.

## What limits the accuracy

**The rocket's place in the roster, by 175x.** Over 18 shots the first rocket lands at a median
**0.09 km** and the eighth at **15.81 km**, monotone, across every arm — `docs/MIRV-NEXT.md` **8y**.
It is not the burn: the cutoff residual is flat at 0.2-0.4 m/s and every computer predicts the same
4.60 km. It is `SeparationClearance` abandoning the trim on **87 of 144 flights**, which returns
early and applies no aim correction at all. A correction that runs lands at 1.4-3.8 km; one
abandoned lands at 20-25 km.

**The interlock was then flown and it is not the whole gap.** `arm/interlock` removed all 42
abandonments and doubled completions — and **55 of 64 corrections still did not finish**, dying on
the ceiling, the budget or the clock instead. `docs/MIRV-NEXT.md` **8z**: what the interlock loses
to is the loop it lets run. The gradient is a race between the trim converging and the trim
cancelling its own separation, and being first in the roster is only which flights win it.

**And every arm comparison at eight rockets used to be pooled across that gradient.** `--paired`
is what fixes that: alternating the variants down the roster puts each arm on both sides of the
gradient in every shot, and the comparison is within the shot rather than across the night.

## What limits the accuracy after that

The chain, from the flown logs, **as corrected by the refutation pass** — `docs/MIRV-NEXT.md` item
**8w**. Two of the four links 8v named turned out to be reading the log rather than the bus.

1. ~~**The separation clearance never succeeds.**~~ **It succeeds in 4.4 seconds, on 15 of 16.**
   The `clear of the spent stack` sentence is discarded by the only thing that reads it — `Say`
   prints the clearance line only while the gate is *shut* — so its absence from 94 flights measures
   the logger. The armed `ProximityWatch` says the gap reaches the 15.3 m keep-out at +4.4 s, and
   every one of the fourteen trim refusals is logged *without* a clearance prefix, which is proof
   the gate was open. One flight of sixteen timed out, and it is a genuine re-approach.
2. ~~**The gap is frame-rate sensitive.**~~ Not settled. The 2.1–4.8 m against 5.3–8.9 m readings
   are samples taken *during* the four seconds the gap is opening, so they measure when the frame
   landed as much as how fast the halves parted. The separation rate measured end to end is
   **3.0 m/s**, not the 0.65 m/s inferred from the timeouts.
3. **The bus is 7.77–17.13 m/s off its solution *at the split*** — before any waiting. Four seconds
   of clearing adds about 3 m/s. **11 of 14 were already over the trim's ceiling with zero wait.**
4. **The trim then refuses**, at 10.81–20.26 m/s against `BusTrim.MaxMetresPerSecond` of 10, because
   `IcbmComputer` only sizes the ceiling from the budget once `_postBoost.Cycles > 0`. This link
   stands, and it is the proximate cause.
5. **No aim correction is applied at all**, and the shot lands where the raw burn put it.

**And the debt in link 3 tracks the arrival, not the decoupler.** Across the fourteen, what the bus
owed at the split runs monotone with the gap between the committed arrival and the flown prediction
— 8 s to 14 s, at about **1.6 m/s per second**. A shove is ~1 m/s. That is the unexplained term now,
and it is printed on every one of those lines already.

## What to do, ranked

**0a. Fly it spread. Two thirds of every short-range shot was never measured.** `docs/MIRV-NEXT.md`
**8ah**: a Mk 21's 2.0 km lethal radius reaches other warheads, so eight groups on one aim point are
eight groups inside each other's kill radius and the first one down destroys the rest — three of
eight flights scored in eighteen of twenty shots. `Sim/AimSpread.cs` gives each rocket its own point
six lethal radii away, and the same shot then scores **8 of 8, 48 of 48 warheads, zero intercepts**,
at 52-121 m. It gets worse as the shots get better, which is why five nights at 12,902 km never saw
it: at that range nothing lands close enough to anything else.

**0b. Fly it paired. Nothing below is measurable otherwise.** 8aa put the same baseline at 14.49 km
and 5.43 km on identical code three hours apart — a 2.7x session swing against a backlog where
everything is under 3x — and the arm between them reversed. `tools/shot-batch.sh --paired` gives
each rocket in one world its own variant and `tools/shot-report.py --paired` scores the pair, which
cancels the frame trace, the warp history, the solver load and the target by construction. Six shots
reach p=0.031 there; the between-run test reaches nothing at any n this project can afford.

Both of the arms below are settings rather than branches, for exactly this reason —
`arm/trimceil` and `arm/interlock` as branches cannot be flown against each other in one world.

**1. ~~`interlock` + `trimceil`.~~ Flown 2026-08-30, and `trimceil` is harmful — do not fly it.**
`docs/MIRV-NEXT.md` **8ae**: over 96 flights it produced **zero `payback` endings** against twelve on
the baseline, converting every one into `settled`, which lands at 6.91 km where `payback` lands at
0.06. It also threw four shots of twelve at 54x to 105x the baseline. Off by default and it stays
off.

**And `payback` is a symptom rather than a lever.** Its rule fires when the predicted miss is
already under about 156 m — `lastCycleSeconds x HoldingCostsMetresPerSecond`, 6 x 26 — so its 0.06 km
median is a selection effect and "make more shots end on payback" would relabel them rather than
move them. The terminator table is a **diagnosis**: it says whether a correction converged.

What is left is the question it has always been: **why do most corrections not converge?** Making
the loop run is not enough (8ad), and letting it spend more is actively worse (8ae) — under
`trimceil` 25 of 32 ended with the aim settled 6.91 km out, which is the correction stopping while
still wrong.

**2. ~~`interlock` alone is unresolved.~~ RESOLVED, and shipped on — `docs/MIRV-NEXT.md` 8ag.**
Thirteen paired shots at 2,000 km: 11 wins, median **0.49x**, sign p=0.022, signed-rank p=0.017,
every one of 21 abandonments removed. The same arm at 12,902 km was 0.76x over 24 shots and would
not resolve — **the geometry was burying it**, which is the lesson of all five nights.

**3. The old ranking, kept for its reasoning.** `docs/MIRV-NEXT.md`
**8ad** flew `KeepOutCoversTheClearance` paired against a baseline: **22 abandonments to nought**
over 64 flights, and the miss did not move — 1.12x, p=0.727. The abandonments become `trim`
refusals, so **`trim` is now the dominant terminator at 16 of 32**, and `TrimCeilingFromBudget` is
the setting that addresses exactly that.

What the same night established is the prize, and it is not the arm comparison: **a correction that
reaches a natural finish lands at 60 m** and one cut off lands at 10–12 km. Two orders of magnitude,
on nine flights across both arms. Everything on this list is really about how many corrections
finish.

**2. `AimWithinTrimBudget`.** Still unflown against a baseline.

`TrimCeilingFromBudget` drops the `Cycles > 0` guard on the per-pass ceiling: it asks whether the
*aim* has moved when the question is whether the *bus* has separated, and 11 of 14 flown trims were
already over the ten-metre ceiling with no wait at all. `BusTrim.Stalled` and the budget still bound
the loop. It licenses a 10–20 m/s correction whose size tracks an arrival disagreement rather than a
separation shove, and the log already carries the number that tells those apart.

`AimWithinTrimBudget` holds the correction to an aim the remaining budget can actually fly it to —
**8ab**, and it is the other half of the same failure. Without it the loop may walk 300 km where one
budget buys 24 to 113, so the trim spends its whole tank on a correction it can never complete and
the shot releases with all of it outstanding. With it the aim stops somewhere the trim can reach.

They are independent and should be flown as three arms in one world: neither, each, both.

**2. ~~`SeparationClearance.TimeoutSeconds` 20 → 25.~~ Do not fly this.** The timeout resolved 1 of
16 flights, not 27 of them. The `15.2 m of 15.3` line read as a refusal ten centimetres short is a
`waiting` line one frame *before* the gate opened. Nothing here is bounded by the clock.

**3. The structural defects found and not fixed.** Each is independent of 1:
   * the trim's refusal is a **single-frame verdict that is terminal for the whole flight**
   * the keep-out interlock is **provably dead** — `ProximityWatch.KeepOutFor` and the clearance's
     `wanted` are the same expression, so the interlock can only be armed while the gate is shut,
     and a shut gate is what stops the trim firing
   * the gate and the interlock share that one threshold, so there is **no hysteresis** at the
     moment clearance is achieved — which is how the one re-approach happened
   * `BusTrim.MaxMetresPerSecond` **cannot be raised** — `PostBoostAimTests` pins it against the
     separation reserve. The only loosening path is the call site, which is item 1.

**4. ~~Then the arrival floor.~~ Flown 2026-08-28, and it bought nothing here.** `arm/floor15`
measured **15.28 km against a 3.60 km baseline** over five shots — 4.24x, p=1.000, so worse and
unresolved. It is not that the floor failed: the shots did arrive steeper (13.6° → 17.8°) and the
cutoff residual improved threefold. **The 18x of item 8t was priced against a baseline arriving at
about seven degrees, and this geometry already arrives at 13.6 with the floor off**, so most of that
effect had been collected before the arm was flown. `docs/MIRV-NEXT.md` **8x** is the record. Any
future claim for the arrival floor has to name the baseline angle it is against.

**5. The bimodality, which is now the largest thing on this list.** `floor` flew 0.04 and 1.38 km
and then 15.28, 27.54 and 28.51 — two populations on one build and one save, not a distribution.
`base` hints at the same shape. Ruled out on the night: the arrival floor's affordability, the
propellant, and the terrain (−0.02% slope, 1.0x flat). **A 0.04 km shot exists**, and what separates
it from a 28 km one is worth more than anything else here. The logs are on disk.

## What is not worth doing yet

* **Scaling past eight.** The terminal `draw` cost is the wall, and the biggest piece of it is
  `IcbmOverlay`'s trajectory arc, which transforms a thousands-of-point path every frame before
  striding it to 96 segments. The aim ring beside it is already cached; the arc is not. Fix that
  before trying sixteen again.
* **More frame-budget spans.** Every call in the simulation path is wrapped and they total ~1.4 ms.
  What is left is in `draw`.

## Tools, and what to trust

* `tools/shot-report.py` scores **every rocket in a run** (`split_flights`). Its other columns —
  residual, trim, passes — are still read over the whole log and describe the world rather than one
  flight; per-flight attribution wants the log partitioned by craft name.
* **`shot-report.py`'s between-run comparison still pools per flight, so its p-values are
  inflated.** The gate aggregates per shot since `eb8a4eb`; `compare()` does not. A `LOSS` at
  p=0.003 over 45 flight records was 1.48x at p=0.836 over 6 shots. `--paired` does not have this
  problem — its statistic is a sign test over shots — and it is the one to use.
* **Eight rockets in one world are not eight independent draws.** They share frame pacing, warp
  decisions and solver load, so a rank test over them inflates n without inflating information.
  That sharing is exactly what makes them a good *paired* comparison, which is the whole of
  `--paired`.
* `Sim/FrameBudget.cs` has two known defects: `EndFrame` is called from inside the `sim` span, so
  per-name **worst** values are from different frames and must not be read against each other; and
  it sits inside the `Log.Threshold <= Debug` gate, so it cannot produce a reading with logging off.
* `KSARMORY_SCENARIO_SYSTEM` picks the system for a scripted run. KSA defaults to the 25-body
  `Sol`; `SolLite` is Earth and Moon. **Patched conics**, so dropping the outer planets does not
  change an Earth trajectory and stays comparable with every night already flown.
* **Before reading a log line's absence as an event's absence, check it can be printed.** Four
  findings on this mechanism have now been artefacts of an instrument with one output: the pre-arm
  `ProximityWatch` minimum, the `F0` rounding, 8p's inferred drift-back, and the clearance's success
  sentence that `Say` discards on exactly the branch producing it. A gate whose only observable is
  its failures reads as a gate that always fails.
* **Do not edit `tools/*.sh` while a run is in flight.** Bash reads a script incrementally and a
  running scenario resumes into the middle of a different file.
