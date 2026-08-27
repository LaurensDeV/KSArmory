# Improving accuracy at eight rockets

**What this is.** Eight rockets in one world is now a usable measuring instrument — eight scored
shots for the wall-clock cost of one. This is the state of it, what is known about what limits the
accuracy, and what to do next, ranked. `docs/MIRV-NEXT.md` items **8s**, **8t**, **8u** and **8v**
are the flown record; this is the plan.

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

The chain, from the flown logs. **Item 8v states this and a refutation pass says 8v's version is
not self-consistent against the code — re-read it before building on it.**

1. **The separation clearance never succeeds.** Across 94 single-rocket flights the
   `clear of the spent stack` branch has never once fired; every clearance ends on the 20 s
   timeout. `SeparationClearance.Check` wants `stageRadius + 10`, which is 15.3 m for the 5.3 m
   stack, and the gap opens at roughly 0.65 m/s from 2.0 m — about 20.5 s of separating against a
   20 s clock.
2. **The gap is frame-rate sensitive.** Measured 2.1–4.8 m when frames were slow and 5.3–8.9 m once
   they were faster, on eight of eight flights. This is why every performance fix has moved
   accuracy.
3. **The bus drifts while it waits** — 4.11 to 9.94 m/s off its solution by the timeout.
4. **The trim then refuses**, because `BusTrim.MaxMetresPerSecond` is 10 and
   `IcbmComputer` only sizes the ceiling from the budget once `_postBoost.Cycles > 0`.
5. **No aim correction is applied at all**, and the shot lands where the raw burn put it.

## What to do, ranked

**1. Drop the `Cycles > 0` condition on the trim ceiling.** `Ksa/IcbmComputer.cs:1252`. One line.
Named by the investigation as the proximate cause: the guard asks whether the *aim* has moved when
the question is whether the *bus* has separated. `BusTrim.Stalled` and the budget still bound the
loop, which is the argument `9b48bd1` already made for later passes.

**2. `SeparationClearance.TimeoutSeconds` 20 → 25.** One constant. Covers the 27 recorded timeouts
and the flight refused at **`15.2 m of 15.3`** — ten centimetres. It does nothing for a bus still at
2.1 m, so it is second rather than first.

**Fly them separately.** They address different halves and stacking them loses which worked.

**3. Re-read item 8v against the code.** A refutation pass found its causal chain not
self-consistent. Everything above inherits from it.

**4. The structural defects found and not fixed.** Each is independent of 1 and 2:
   * the trim's refusal is a **single-frame verdict that is terminal for the whole flight**
   * the keep-out interlock is **provably dead** under the current wiring, so the mechanism meant to
     let the trim fire safely while close never engages
   * the gate and the interlock share one threshold, so there is **no hysteresis** at the moment
     clearance is achieved
   * `BusTrim.MaxMetresPerSecond` **cannot be raised** — `PostBoostAimTests` pins it against the
     separation reserve. The only loosening path is the call site, which is item 1.

**5. Then the arrival floor.** `IcbmConfig.MinArrivalAngleDeg` ships at 0 and is worth about **18x**
on the mean (3.74 → 0.21 km, `docs/MIRV-NEXT.md` 8t). `arm/floor15` exists. At eight rockets that
night costs about an hour rather than eight. **This is the largest single accuracy lever left and it
is a config default, not a code change.**

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
* **Eight rockets in one world are not eight independent draws.** They share frame pacing, warp
  decisions and solver load, so a rank test over them inflates n without inflating information.
* `Sim/FrameBudget.cs` has two known defects: `EndFrame` is called from inside the `sim` span, so
  per-name **worst** values are from different frames and must not be read against each other; and
  it sits inside the `Log.Threshold <= Debug` gate, so it cannot produce a reading with logging off.
* `KSARMORY_SCENARIO_SYSTEM` picks the system for a scripted run. KSA defaults to the 25-body
  `Sol`; `SolLite` is Earth and Moon. **Patched conics**, so dropping the outer planets does not
  change an Earth trajectory and stays comparable with every night already flown.
* **Do not edit `tools/*.sh` while a run is in flight.** Bash reads a script incrementally and a
  running scenario resumes into the middle of a different file.
