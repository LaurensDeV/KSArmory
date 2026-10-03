# Short-range ballistic shots (100–1,000 km)

**A plan, not a record.** Nothing here has been built or flown except where it says *measured*. Written
2026-10-03 from three research passes over the code and two adversarial reviews of a first draft; a
third review (every mechanism below checked line by line against the source) was still running when this
was written, and its verdicts are not folded in.

## What is known

**Measured headlessly** (`ShortRangeAscentTests`, ACCURACY-PLAN 3bk, backlog #23): in the rig, every target
from 100 to 800 km gets the same shot to every digit — cutoff at 65.4 km, 3,024 m/s, 30.3° climb, 92 s burn.
That state is a vacuum range of roughly 900–1,000 km, so in the rig every short target lands near the same far
point. From 1,200 km up the cutoff scales normally.

**But that rig is not the game's rocket.** `PadRig` is two liquid stages that throttle instantly to anything
and stop when told. The `SOLVER SCALE` saves fly three radial SRBs lit alone at ignition, a liquid core and an
upper stage. Step 0 below flies that stack: the SRBs overshoot only the shortest shots, and the core is what
overshoots the rest.

### Step 0 in the rig: the game's stack — measured headlessly 2026-10-03, not flown

`GameStackShortRangeTests` flies `GeoSat FAT` as read off a SCALE 1 shot flown at 1.00x
(`~/shots/2026-09-07-1751/shots/001-base.log`): three SRBs lit alone (249.8 t in 100.2 s, cannot throttle
or stop), a 25.55 MN core held to the scenario's 8 g, an LR91 Vac upper; 28.6° N, due south; scenario
settings. One drag area, **60 m²**, fitted to that shot, puts the rig's handover at 162.5 s / 74.8 km /
2,551 m/s to gain against a flown 166 s / 75 km / 2,524, and cutoff at 236.6 s / 201 km against ~235 s /
197 km. The rig's default drag reached thin air 24 s early, and with it the table below was milder.

**Flown 2026-10-03, today's code, SCALE 1, one flight each (mechanisms, not accuracy):**

- **418 km fails.** Handover at 76 km with **5,265 m/s** still to gain; the closed loop turned the stack round
  and burned it dry, **119 m/s short**; warheads held. One released by hand landed 41 km out.
- **1,000 km passes, the same way.** Handover at 76 km with **3,173 m/s** to gain, braked on the upper stage,
  cut off 0.10 m/s short at 162 km; six of six within **4.9 m**. The upper burned from 31.8 t to 6.4 t,
  still lit.

So both shots overshoot along the pitch programme and then spend the upper stage taking it off; whether one
lands is whether the upper has enough. **That is a margin, not guidance**, and the 08-31 418 km landing was
on the right side of it.

**The rig, with the upper stage bounded by that flight** (6.0 t dry; "pref" is `ArrivalPreference`, the
game's default 0.5 with the stack's delta-v reported is what flies):

| range | pref 0.5, stack delta-v | pref 0 | least to gain before the loop |
| --- | --- | --- | --- |
| 100–200 km | backstop on the handover frame, ~4.6 km/s, lands 1,600–2,100 km out | same | 1–2 m/s at 94–105 s, 19–25 km |
| 300–1,200 km | lands, except 500 km (burnout, 19 km) | lands at 418–500; burns dry at 700–1,200 | 118–1,021 m/s (pref 0.5), 3–26 (pref 0) |
| 2,000 km | `countdown`, 0.2 km | `countdown`, 4.5 km | 1,497 / 144 m/s |

It lands 418 km where the game fell 119 m/s short: 0.4 t of upper-stage dry mass is ~270 m/s here, and the
flights bound it from one side only. **Read the 300–1,200 km rows as "on the margin", not as "lands".**

Traced: **the core is what overshoots, not the SRBs.** The pitch programme flies the core at 8 g down to 0°
pitch for ~40 s after the shot has what it needs. The closed loop cannot brake until q < 200 Pa (~95 km),
because `HoldIntoTheAirflow` keeps thrust within 8° of the airflow; after that the upper burns retrograde.
The 2 m/s backstop decides 100–300 km: under it the loop cuts on the handover frame with the overshoot aboard.

**The SRB question, answered in the rig:** the least to gain falls *during* the SRB burn only at ~100 km
(94 s, burnout at 101 s); from ~200 km it falls after the core has lit, where the throttle reaches. So the
Step 2 throttle cap can serve this stack from ~200 km; 100 km needs the pitch schedule or a refusal.

### Mechanisms read off the code, not flown

| # | Mechanism | Where |
| --- | --- | --- |
| M1 | The pitch programme cannot cut off; `ShouldCutOff` and the throttle ramp live only in `ClosedLoop()`. Handover needs q ≤ 1,200 Pa, which a 1–2.5 km/s shot reaches at 54–65 km, long after it has the velocity it needs. `Resolve` runs during the pitch programme and drives `_lowestToGain` near zero; on the first closed-loop frame the 2 m/s "rising again" backstop cuts at once. Traced in the Step 0 rig: it fires on the handover frame at 100–200 km (300 km at pref 0); above that the loop takes over and brakes on the upper stage, which flown at 418 km ran dry 119 m/s short and at 1,000 km made it. | IcbmProgram ~935, 1078–1139, 1335 |
| M2 | When cutoff falls on the handover frame the arrival is never latched (`Resolve` ran while the phase was `PitchProgram`; the latch needs `ClosedLoop`). No committed arrival, so `ResolveCoastArc` returns at once, `Freeze` cannot run, `ReleaseAnArrivalTheTrimCannotFly` cannot help. Unreachable at long range: the closed loop always latches within `LatchArrivalWithinSeconds` (20 s). | IcbmProgram 678, 976–980, 1156–1159 |
| M3 | The aim correction sees nothing departing below ~73.7 km (`DepartureIsWorthObserving`, density 1e-4) — during the burn or after it. | AimCorrection 242–244; IcbmComputer ~4227 |
| M4 | With no reading, `PostBoostAim` (`DecideOnTheReading`) holds release for up to 120 s, about the whole fall of a 300 km lob. | PostBoostAim 287, 327–331 |
| M5 | The release gate's altitude floor applies only on the climb; under `DeployAltitudeMetres` (100 km) it opens just after apogee, inside the air. The comment at ~1188 ("stops a release inside the air") is false for apogees under 100 km. Nothing forces release before impact. | IcbmProgram 1188–1201 |
| M6 | `BusTrim` nulls onto a vacuum Kepler coast, so in air it reads drag as debt and spends its 60 m/s chasing it. | BusTrim ~1008 |
| M7 | The arrival floor (`ArrivalPreference` 0.5 × steepest affordable) is latched from the pad with the whole stack's Δv: ~40° at short range against a ~44° minimum-energy arrival. The rig passes no `StackDeltaV`, so its floor is much lower than the game's. | IcbmComputer 3374; IcbmFlightRig 261–270 |
| M8 | `BallisticArc`'s coarse flight-time scan steps ~158 s, about a whole 100 km flight. Matters only on the first unseeded solve and the floor's anchor fallback. | BallisticArc 181–237 |
| M9 | `HoldingCost` probes 106 s ahead and silently falls back to 26 m/s on a shorter coast. Walks collapse to one stop under ~485 s of coast. | HoldingCost; ReleaseWalker |
| M10 | `AimSpread` turns SCALE 8 into a range ladder below ~500 km (100 km seats fly 100–246 km). SCALE 1 and 2 are fine. | AimSpread |
| M11 | **The spent stack rides along until release**: `SeparateOnce` fires only on `ReadyToDeploy`, which on a short shot waits for the climb past 100 km or for apogee — so the coast is flown with the empty stage on, a high-drag body. | IcbmComputer 904 |

## The design decision: loft the short shot, don't teach the computer to fly in air

The first draft tried to extend vacuum-built code into the air: cut off from the pitch programme, observe
departures inside the air with a bus-drag predictor, stand the trim down. The design review rejected that, and
ACCURACY-PLAN 3bk already ranked the alternative first:

**Climb steeply and throttle so the velocity is not reached until q ≤ `HandoverPressurePa`.** The existing
closed loop then cuts off normally (~45–50 km), the arc's apogee clears ~120 km, and everything after cutoff
works as it does at long range — the density gate observes from the live state, the trim works in vacuum,
`PostBoostAim` gets its reading, and the release gate opens on the climb above 100 km. Only the warhead's
reentry drag remains, which `ImpactPredictor` already models. A 300 km shot would arrive at ~60°, which is a
better `cot γ` for precision, paid for with Δv a 7 km/s stack has spare.

Why not the first draft's version:

- **Cutting off in the pitch programme (old 2.1) does not work.** The pitch programme flies `AscentProfile.Aim`,
  not the velocity still to gain, so that quantity only reaches zero if the schedule happens to cross the arc
  family — which the ~40° floor forbids. `_countdown` assumes thrust along the solved direction, so the cutoff
  timing and the 2 s ramp mean nothing there. And a cutoff at 30–50 km (q 10–20 kPa) coasts with the stack on:
  roughly 30–60 m/s lost at ~350 m per m/s, **10–20 km short** at 300 km, uncorrected. If a cutoff in air is ever
  wanted, hand over to `ClosedLoop` early instead of duplicating `ShouldCutOff`.
- **A bus-drag predictor is a second flight model** — what CLAUDE.md warns against. Releasing at cutoff makes the
  warhead's drag the right model and removes the need.
- **A computed minimum range from the pad (old Step 1) is not computable honestly.** It needs the open-loop ascent
  flown through air with this stack's thrust, mass flow and staging; `BoosterPerformance` knows the running stage
  only and `StackDeltaV` is one number. And Step 2 moves the floor anyway.
- **The `AimSpread` fix is not needed** at SCALE 1/2; it belongs in front of any future SCALE 8 short-range night.

**What the loft cannot reach:** throttling cannot reach an SRB. In the rig the SRBs alone deliver what a ~100 km
shot needs before they burn out, so on this stack that range needs the pitch schedule or a refusal; from ~200 km
the excess comes from the core, where the throttle reaches.

## The plan

Every behaviour change sits behind an `IcbmConfig` flag, off, and is gated by a **regime test** (e.g. the pad
solve's Δv far below the stack's, or a range threshold), so a long shot never enters it.

### Step 0 — a rig that is the game's rocket, and a baseline. No behaviour change.

Items 1–4 are built (`GameStackShortRangeTests`, `IcbmFlightRig`'s `Solid`, `VacuumExhaustVelocity`,
`BurnoutMassFlowRatio`, `DragAreaM2` and `ReportsStackDeltaV`, all off by default) and 6 is flown at 418 and
1,000 km; results above. 5 is not done.

1. Build a pad rig from the `SOLVER SCALE 1` stack (read off the save or a flown log): stage masses and thrust,
   **SRBs as a stage that cannot throttle or stop**, three staging events. Flown actuator values:
   `CommandLatencyFrames = 1`, `MinThrottle` ~0.1, a finite throttle rate, `StepJitter = 0.5`, `StartsUnlit`.
   Launch at 28.6° N, due south. The rig needs a per-stage "cannot cut" option.
2. Fly 100 / 200 / 300 / 500 / 700 / 1,000 / 1,200 / 2,000 km, each with and without `StackDeltaV` and with
   `ArrivalPreference` 0.5 and 0. Log per shot: cut reason (backstop / `ShouldCutOff` / burnout / solution
   reached during the SRB phase), the minimum velocity still to gain during the pitch programme and where, whether
   the arrival latched, cutoff altitude, speed and q, apogee, and the miss from cutoff with
   `Arsenal.Mk21WithDragFromShape` (what the game flies), carried as `AimConvergenceTests.ActualMissMetres` does.
3. Label the table **the ascent's floor, not the shot**: the rig has no separation shove, trim, release or bus drag.
4. Tag the test `kind=study` and record it in `tests/KSArmory.Tests/STUDIES.md` (`ShortRangeAscentTests`
   currently asserts nothing and sits in the push loop).
5. Write the golden harness before Step 2: flag on against flag off **on the same build**, bit-equal cutoff state,
   over the pad fixtures (`IcbmFlightTests`, `ShortRangeAscentTests`' 1,200/2,500/6,269 km rows,
   `GroundLaunchArrivalTests`, `ArrivalPreferenceTests`) and the orbit fixtures (`AimConvergenceTests`,
   `CutoffResidualTests`, `DeorbitTests`, `PostBoostObserverTests`), plus a continuity sweep 800–1,600 km so the
   regime gate is not a cliff. Never compare against stored numbers.
6. **E1 baseline in game** (needs the user's go-ahead — it occupies the machine): today's code at 418 and
   1,000 km on SCALE 1, to reconcile the rig with the game.

### Step 1 — safety that ships alone

- Fix the release-floor comment and decide on purpose what a lob under 100 km does (M5).
- A forced-release guard: every warhead aboard is released by impact − T, T covering the separation clearance.
- Bound `PostBoostAim.MaxSeconds` by the time to reach ~100 km on the descent, less the release sequence (M4).

### Step 2 — the lofted short shot, behind one flag

Reusing machinery that exists:

- **Loft:** a range-derived arrival floor or minimum apogee (~120 km), through `RefreshArrivalBudget`/`FloorDeg`.
- **Pitch:** a floor on the pitch at the solved arc's burnout flight-path angle (not a rescaled `TurnEndMetres`,
  which 3bk measured moves nothing alone).
- **Throttle:** an internal acceleration cap from the pad solve, kept apart from the operator's
  `MaxAccelerationGee`, through the `ThrottleUnderAccelerationCap` that `Fly()` already applies in every phase —
  sized so the velocity still to gain at the handover pressure keeps ~15–20 s of burn in reserve.
- Gate in Step 0's rig: cut reason `ShouldCutOff` (not the backstop), q at cutoff ≤ 1.2 kPa, apogee > 100 km,
  predicted miss within a few km; long range bit-equal.

**Built 2026-10-03 as `IcbmConfig.AscentReserveSeconds`, off at zero** — not the three levers above but one
rule: once the velocity still to gain is less than that many seconds of burning, the pitch programme throttles
back in proportion **and steers along what is left to gain** instead of the schedule. Throttling alone was
tried first and made it worse (418 km: 990 km out at 10 s, 6,900 km at 15 s), because the schedule kept
pointing at the horizon and the excess grew sideways.

In the rig at 15 s: 418 km hands over with 479 m/s to gain instead of 4,734 and keeps 4.6 t in the upper
instead of 0.2; 418–1,200 km all cut off on `countdown` within 0.3 km. **100–300 km still fail**: the
velocity still to gain reaches zero anyway, because the core's 12% minimum throttle is still ~3 g — the
stack's limit, not the rule's. `AscentReserveGoldenTests` holds every fixture with more than 15 s to spare
bit-equal (2,000 km and beyond; the least burn left in the pitch programme runs smoothly from 3 s at 418 km
to 19 s at 2,000 and 65 s at 6,269), requires the engaged 1,000–1,600 km shots to land no worse, and
checks it can see a difference at 418 km.

**Flown once, 418 km, SCALE 1, 15 s:** handover at 72 km with **2,341 m/s** to gain (5,265 without it),
cutoff on the countdown at 120 km 0.09 m/s short, release ~25 s later with 1:24 to impact, six of six
within **21.9 m**, arriving at 73.8°. One flight: the mechanism, not the accuracy. The rig said 479 m/s at
handover, so the game still overshoots more than the rig does.

### Step 3 — honest refusal, reported in flight

- When the backstop fires on the handover frame with `_lowestToGain` having bottomed in the pitch programme,
  say so ("the ascent delivered more than this shot needs") rather than fly on silently.
- A stack that cannot throttle (`ThrottleAchieved` not following the command — check on the first throttled frame)
  is too close below the range its handover state delivers; reported in flight, not predicted on the pad.

### Step 4 — fly it (E1, cumulative)

- SOLVER SCALE 1, one build, configurations by `./tools/scenario.sh 'mirv:<lat>,-80.604,<bar km>' --arms
  'on:Flag=true'`, `KSARMORY_SCENARIO_TRACE=1`. Cumulative: Step 1, then 1+2, then 1+2+3.
- Ranges 1,000 → 500 → 300 → 200 → 100 km due south of the pad (28.608 N 80.604 W, R = 6,371 km). Aims, all
  checked: **19.6148 / 24.1114 / 25.9100 / 26.8094 / 27.7087 N**, lon −80.604. 500 and 1,000 km are sea, the rest
  flat land; 200 km is on Lake Okeechobee's shore — read the terrain line on each first flight.
- One flight per range claims **mechanisms and categorical outcomes only**: cut reason, cutoff state, latch,
  correction passes, release altitude and time before impact, warheads released against warheads aboard, and a miss
  of tens of km against a few km. Single-rocket scatter is ×1.74 geometric sd, so no accuracy claim and no floor
  from one flight: 3–6 flights at a candidate floor range before naming one.
- Per flight read the newest `KittenSpaceAgency.*.log` for exceptions and the releases against warheads aboard —
  a warhead riding the bus down can show as a TIMEOUT or "never arrived", not as an error.
- No build or test on the machine while a flight is running.

### Long-range non-inferiority

- If the headless golden harness is bit-equal and two long-range flights (2,000 km at `10.622,-80.604`, and the
  Chaco site 24.0 S 62.0 W) log no regime gate engaging, **no night is needed** (SHOT-PROTOCOL §0).
- Otherwise a declared paired night at 2,000 km or Chaco on SCALE 8 with seat levelling, the bar written into the
  docs before flying (e.g. 97.5% upper bound below 1.25 on the worst warhead), ≥14 blocks; compare only within the
  night (frame-rate break of 2026-10-03). `shot-report.py` has no non-inferiority option; read the bound off the
  paired interval.

### Short-range characterisation

Flags on only, 6–12 flights at the floor range and at 1,000 km: median, worst, and release completeness against
the bar. A paired flags-off arm only if flags-off is shown to land at all.

### Fallbacks, only if E1 shows a stack or range the loft cannot serve

In order: early handover to `ClosedLoop`; the M2 latch at cutoff (**without** re-latching after
`ReleaseArrival()`, which long shots do reach on purpose); the M3 gate widened as an OR ("in `ClosedLoop` and the
projected cutoff under ~10–20 s away and not below the vehicle"); release at cutoff instead of a bus-drag model.
Last: M8's scan refined only when the best coarse sample is in the first bins, and M9.

## Not in scope

Ranges under ~100 km (a tactical missile with its own stack); depressed or terrain-following trajectories;
multi-target walks under ~485 s of coast — they collapse to one stop, and the reach display should say so.
