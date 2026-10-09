# Short-range ballistic shots (25–1,000 km)

**Built and flown, with the plan it started from.** Every range from 25 to 2,000 km flies and lands on the game's
stack with `FlyAnyRange` and the short-shot settings on by default. On 2026-10-06's defaults four rockets land at a
median of 1.4-6.9 mm from 25 to 700 km, one flight a cell, with the 25 km liquids still hovering before they do; the
matrix and what is open are near the end. Start at "Any range, any stack" for what is built and why. The sections before
it are the 2026-10-03 plan, kept for its measurements; the sections after it are the next steps and are not
built.

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

**Built 2026-10-03 as `IcbmConfig.AscentReserveSeconds`, off at zero** *(since 2026-10-06 the constant
`IcbmProgram.AscentReserveSeconds`, 15 s, applied under `FlyAnyRange`)* — not the three levers above but one
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

### Any range, any stack — `IcbmConfig.FlyAnyRange`, on by default, flown 2026-10-04/05

A refusal is not the goal: every shot from the shortest up, on whatever stack is flying it. Built and flown
over one night, each flight's failure becoming the next rule; what stands:

1. **A solid's remaining delta-v is a floor on the velocity to gain** (`BallisticArc.TryCheapest`'s
   `minToGain`), read off KSA's staging display for the running stage alone (`RunningStageDeltaV`; the booster's
   figure puts the whole stack's propellant behind the SRBs and lofted the first flight for 5 km/s). Past the
   cheapest arc the need climbs with the flight time to escape, so the solver takes the lofted arc the solid can
   actually fly, the pitch is held at or above that arc's climb, and the program cuts off at the solid's burnout
   when what is left is inside the bus trim's reach.
2. **A stage that can stop is cut off when the shot is complete, wherever that is**, never held back or
   absorbed: it hands over to the closed loop once within the reserve (15 s) of finishing. Waiting for thin air
   instead is what overshot — the schedule cannot cut off, a floor still pushes, and in thick air the stack
   cannot point where a lofted arc wants it — and what flew 200 and 418 km into the upper stage burning back.
3. **Done in thick air, it pauses and coasts out**, the loop still solving, and relights in thin air or at the
   top of the climb, so the cutoff that counts is made where the vacuum arc is true. Where the airflow will not let
   it point usefully it waits while climbing and stops once the climb is over; while it waits to turn it burns at
   its floor rather than off, because a stack that steers by gimballing its engines has nothing else (flown at
   200 km, one tumbled into the sea with the engine held off until it was pointed).
4. **A short shot steers at any angle of attack** — KSA's drag has no turning moment and structural failure is
   g-load alone, so in this build an angle costs drag and nothing else (`docs/BLOCKED-ON-KSA.md`) — and **pins its
   arrival the moment the closed loop takes it**: left free, the cheapest arc from a point on a lofted one is a
   lower one, and the velocity to gain swung round to point backwards through a pause.
5. **Release.** A short shot drops the spent stack at cutoff (`IcbmConfig.SeparateAtCutoff`, which did the same for
   any shot, was removed unflown on 2026-10-06). One that cuts off in the air, or whose arc stays under `DeployAltitudeMetres`, releases at cutoff,
   at once, from the bus still on the stack — no trim, no post-boost hold, no wait to settle. Each of those was a
   flown failure: the stack coasting through air with the warheads aboard (13.7 km), separating then releasing
   beside it (a 2.2 / 10 km split), a release gate that opened on the descent (10 km), a 53 s wait for a stack
   with no engine to stop sweeping its tubes (8.9 km).

**Flown, `SOLVER SCALE 1`, one flight per range on the build named** (mechanisms, not accuracy; a group's spread
is metres, the miss is the shot):

| range | stock code | night's last build | how it ended |
| --- | --- | --- | --- |
| 25 km | — | **56 m** (`a8dce5a`) | SRBs to 35 km, closed loop finished at 55 km, released at once |
| 50 km | — | **0.11 km** (`ee963f9`) | SRBs alone, closed loop finished it, released at cutoff |
| 100 km | — | **0.21 km** (`ee963f9`) | core finished it at 16 km on the way down, released there |
| 150 km | — | **failed** (`a8dce5a`) | the core tumbled at its floor and burned dry, then the upper did not light: see below |
| 200 km | — | **42 m** (`f3ea4a1`), **1.6 km** (`e85e940`) | the 1.6 km is the drag the prediction named at cutoff |
| 300 km | — | 8.9 km, 2 m group (`e85e940`); **1.19 km** (`a8dce5a`) | the first waited 53 s to settle; released at once, it beat its own 1.43 km prediction |
| 418 km | **burned dry 119 and 61 m/s short** | **0.75 km** (`e85e940`) | arc under the release altitude, released at cutoff |
| 500 km | — | **7 m** (`e85e940`) | climbed above release altitude, normal release |
| 700 km | — | **2.2 mm** (`e85e940`) | normal release above the air |
| 1,000 km | **4.9 m** | **4.0 mm** (`ee963f9`) | normal release above the air |
| 2,000 km | — | **3.7 mm**, no short-shot path engaged (`ee963f9`) | normal release above the air |

**The millimetres at 700 km and up are real.** They were first read as the warheads striking `AA Defence Site`,
which the scenario parks near the aim. But the site stands 250 m off the aim, and the harness scores each burst
against the aim point (`BallisticScenario.MissFromAim`), not against the structure. The 700 km log shows a
normal shot: the release probe predicted 0.17 m, each warhead's kick cancelled that, and the trace from each
warhead read 0.000 m. That is the flat-ground floor long shots have had since 2026-09-17 (`ACCURACY-PLAN.md`
3el–3en).

**Why 150 km failed**, and why it is not about 150 km. The staging probe (`d32f7eb`) and nine flights on that
build (`~/shots/2026-10-04-probe/`, FlyAnyRange on and nothing else) settled it:

| # | range | result | spin at core separation | upper engine |
| --- | --- | --- | --- | --- |
| 1 | 150 km | **failed**, 109 m/s short | 78°/s | **knocked off** 0.12 s after |
| 2 | 200 km | 28 m | 109°/s | kept |
| 3 | 300 km | 1.39 km | core finished the burn | — |
| 4 | 150 km | 167 m | 75°/s | kept |
| 5 | 418 km | 1.12 km | core finished the burn | — |
| 6 | 200 km | **failed**, 68 m/s short | 82°/s | **knocked off** 0.17 s after |
| 7 | 500 km | 6 m | 93°/s | kept |
| 8 | 150 km | 107 m | 71°/s | kept |
| 9 | 25 km | 140 m | 88°/s | kept |

- **The stack spins up at the floor, every time the core is dropped.** Near cutoff the countdown ramp
  (`IcbmProgram`, `_countdown / ThrottleDownSeconds`) takes the core to its 11.6% floor with about 16 m/s still
  to gain, and steering is held only below about 3.5 m/s there. Every guidance pass points along what is left to
  gain, and at the floor the stack's own thrust moves that faster than the stack can turn. It flips (a 100°
  slew), and from then on the wait-to-turn holds the engine lit at the floor while pointing up to 90° off. That
  keeps the target moving for the rest of the core. Flown at 150 km: under 1°/s at 0.31 throttle, then 62°/s
  three seconds into the floor, 40-112°/s for 47 s. The TVC is not short of authority, which is about 25-45°/s²
  at the floor. It used about 10°/s² throughout, following a target that kept moving.
- **A spinning separation can break the upper's engine off.** KSA fails a part on contact pressure and removes
  it without a log line or a change in vehicle count. Here the part is the LR91 Vac (`EngineA3`, 200 kg) whose
  nozzle sits inside the interstage. Without it the stage has nothing to light, and the computer will not stage
  past the bus, so the burn ends short with the warheads held and the bus rides them into the ground. Two of
  seven spinning separations lost it, at 82 and 101°/s, while 109°/s once kept it. It is a collision, not a rate
  threshold.
- **Before the pause, the same runaway** left about 17°/s on the stack going into the pause, which RCS alone
  (about 0.1°/s²) could not stop in 40 s. The relight at 0.87 throttle stopped it, overshooting to about
  84°/s first.

So rule 3's "burn at the floor rather than off" holds a gimballed stack only while it is pointed. A rate limit
on staging cannot help, because a dry core has no TVC left. Neither can relighting at the floor, because the
full-throttle relight is what stopped the earlier spin.

**`IcbmConfig.ShortShotSlowsLineSeconds`, on by default at 0.5 s.** Once what is left to gain is within that many
seconds of the thrust being made, the line turns at most 5°/s (`IcbmProgram.SlowLineDegPerSec`), and the burn
runs on the part along it. The rig learned to tumble for this (`IcbmFlightRig.AttitudeHasInertia`: a
sampled controller with authority in proportion to thrust, and RCS alone with the engine off). Followed to
the end, the line spins the stack on 31 of 40 flights at 150-500 km across four frame steps. That matches the
night. At 0.5 s, 4 of 40 spin, the worst residual falls from 424 to 16 m/s, and the drag-flown miss moves
from a median of 0.96 km to 1.13 (`FloorHoldStudy`). The slowed line starts only from a fresh solve:
the first flight with it on relit at 0.77 throttle with all of what was left under the threshold. The line
turned from the direction held through the pause, and the burn ended 32 m/s square to it, 4.6 km out.

What did not work, in the rig:

- **Holding the line still** left 70-165 m/s ungained, because drag and gravity turn what is left too.
- **Seconds of floor thrust, or of full thrust,** scale the threshold wrongly. A 0.7/s throttle cannot follow
  the ramp down a 25 g core, so the chase starts while it is still making most of its thrust.
- **An instant throttle** still spins at the floor. Throttle lag is not the cause.
- **Staying lit at the floor until the stack stopped turning before a pause** made the floor chase worse.

What it does not reach: at 150 km the first closed-loop pass turns the line 13° from the pitch programme's.
The stack is still turning at about 20°/s when the burn finishes in thick air and pauses, and RCS cannot
stop it, so it relights 136° off and spins again.

**Not ready: most of the rig's 4 of 40 is an artefact.** `Update` solves before `ClosedLoop` unpauses, so a
relight's first frame is solved on the line slowed through the coast, and its countdown is cut off on at
once. Flown twice at 200 km with the flag on (`~/shots/2026-10-04-slowline{,2}/01-on-200`), that left
30-32 m/s across the line and landed 4.6 km out, against 28 m and 2.1 km off. Making the relight wait for a
solve of its own puts the rig back at 29 of 40 spinning. Leaving the line free during the pause instead gives
18 of 40. So slowing the line stops the chase before a pause, and the same chase starts again after the
relight.

**So don't relight: finish in the air, and solve the arc with drag.** On by default, flown. Three settings
together:

- `ShortShotSlowsLineSeconds` (0.5).
- `IcbmConfig.ShortShotFinishesInTheAir`: a short shot's burn that ends in thick air cuts off and releases
  there instead of pausing. That removes the relight, and with it the chase.
- `IcbmConfig.ShortShotSolvesWithDrag`: in the last 20 s of the burn, each guidance pass flies the solved arc
  with the warhead's drag from its projected cutoff. It measures the landing against the target carried to the
  instant the solve carried it to (`BurnoutGuidance.Command.CarrySeconds`). It moves the aim east and north by
  the miss, solves again, and keeps the new solve only if it lands nearer, up to two times a pass. Under it the
  arrival floor is waived, because a moved aim's vacuum arc is shallower than the warhead's and a failed floor
  unlatches the arrival. Absorbing solids are excluded, because their flight time is re-picked whenever the aim
  moves.
- **All three apply only where the arc stays under `DeployAltitudeMetres`.** An arc that climbs above it still
  pauses and relights, so it cuts off above the air and gets the long-shot release that gives millimetres. Both
  ways of finishing such a shot in the air were flown and lost. Released at once from a stack still turning,
  700 km landed 0.80 km out against 2.2 mm, and 1,000 km 0.98 km out. Coasted on the bus alone through the air
  to the release altitude, 500 km owed the trim 287 m/s of drag and landed 107 km out. The slowed line is held
  to the same arcs: around a pause it makes the relight cut off on the line it slowed through the coast, and
  flown that left 700 km 73 m/s short and 1,000 km 1.0 km out. And such an arc coasts out until the air is
  under `Medium.NoticeableDensity`, not merely under `HandoverPressurePa`: between the two a cutoff still releases
  at once in the air. Flown at 700 km, a cutoff at 64 km released from a stack still turning landed 0.36 km out,
  against 2.2 mm from a cutoff at 98 km.

In `FloorHoldStudy`, 40 flights at 150-500 km over four frame steps:

| | spun | worst residual | median miss | worst miss |
| --- | --- | --- | --- | --- |
| all off | 31/40 | 424 m/s | 0.96 km | 2.39 km |
| slowed line, in the air, vacuum arc | 0/40 | 8 m/s | 3.99 km | 6.18 km |
| **all three** | **0/40** | **7.7 m/s** | **0.07 km** | **0.22 km** |
| drag solve alone (pausing) | 37/40 | 6 m/s | 0.01 km | 0.71 km |

The offset is still moving at hundreds of metres a second as cutoff nears. Frozen 0.75 s early it left the
miss at 2.5 km, so it now moves until the line is slowed. Two limits on these numbers:

- **The rig grades itself in part.** Its miss is flown with the same drag model the solve uses. The game's
  warhead goes through the same `Medium.Drag`, but at another step and order, and what the bus does between
  cutoff and release (release at cutoff here) is not modelled.
- **The rig's attitude is a stand-in for KSA's.** It reproduces the night's spin rate (31/40) but not its
  shape exactly.

Only a flight settles either.

**Flown 2026-10-04/05**, `SOLVER SCALE 1`, the three settings on as one arm (`ShortShotSlowsLineSeconds=0.5`,
`ShortShotFinishesInTheAir`, `ShortShotSolvesWithDrag`), worst warhead of six. No KSA exceptions on any flight.

| range | path | flown | build |
| --- | --- | --- | --- |
| 25 km | low arc, released at cutoff | 58 m | `3d1ed5d` |
| 150 km | low arc | 97-184 m, two flights (0.49-0.85 km before the clamp fix) | `1e62ba1` |
| 200 km | low arc | 176-240 m, three flights; 192 m | `3d1ed5d`; `1e62ba1` |
| 300 km | low arc | 218 m | `11ab4bb` |
| 418 km | low arc | 350 m | `11ab4bb` |
| 500 km | high arc, cut off above the air | 9 m; 7 m | `11ab4bb`; `5604099` |
| 700 km | high arc | 6 m, 5 m | `11ab4bb` |
| 1,000 km | high arc | under a metre | `1e62ba1` |
| 2,000 km | long shot, none of it engages | under a metre | `3d1ed5d` |

Paired against FlyAnyRange alone at 150 and 200 km (`~/shots/2026-10-04-inair/`): **the arm landed 6 of 6 and the
baseline 2 of 5**. All three baseline failures were the upper's engine knocked off at a spinning core separation,
and both baseline landings were 11 m and 150 m. With the settings on, no core separated during a burn.

**Where the low arcs' last few hundred metres come from**, read off the logs:

- The landing matches the computer's own prediction at cutoff: 0.17 km predicted against 0.24 landed at 200 km,
  and 0.24 against 0.39 at 500 before the high-arc fix. So the miss is set at cutoff, by what is left to gain
  (0.1-0.6 m/s) and by the drag offset freezing once the line is slowed.
- A low arc releases at cutoff, so no release probe runs and no warhead is kicked. Nothing after cutoff
  corrects anything. A high arc gets the long-shot release, and that is the whole difference between metres and
  hundreds of metres.

**Accuracy, next**, in order:

1. **Measured: the low arcs' miss is two terms of about the same size.** Read off the six low-arc flights on the
   final builds (25-418 km):
   - **A common offset of 100-220 m, set at cutoff.** Each group's mean lands where the computer predicted at
     cutoff (0.10-0.22 km). The drag solve stops moving its offset once the line is slowed, about the last half
     second, while the offset the shot needs is still moving at hundreds of metres a second. And 0.2-0.5 m/s is
     left ungained.
   - **A spread of 70-220 m within each group, set at release.** After an in-air cutoff the stack is still
     turning, and the tubes sweep at 1-3 m/s as the warheads leave one by one, each with its own sideways
     velocity. The per-warhead kicks are capped at 10 mm/s and cannot cancel it. At 25 km, with the tubes at
     1.0 m/s and a short fall, the spread is 3 m.

   Logged frame by frame (`~/shots/2026-10-05-cutoff/`), the freeze is not the offset: below the normal
   hold the line is followed again, and the drag solve's predicted miss is back to 0 m at cutoff. What it
   left out is the tube's throw. Every warhead leaves at 0.5 m/s along the line, and every low-arc group
   landed long by 69-229 m and 24-87 m to one side, which is 0.5 m/s times 212-378 m per m/s along
   (`LowArcSensitivityStudy`). The computer's own prediction adds the throw, which is why it matched the
   landings when the drag solve said zero. The drag solve now flies the throw too (`IcbmState.ReleaseImpulseCci`),
   and in the rig that takes 200-418 km from 77-165 m to 12-37 m (`TheDragSolveFliesTheTubesThrow`).

   **Flown with the throw** (`905cd3f`, `~/shots/2026-10-05-throw{,2}/`), worst warhead and the group's mean:

   | range | before | with the throw | group centre |
   | --- | --- | --- | --- |
   | 25 km | 58 m | **12 m** | +1 m downrange, +12 cross |
   | 150 km | 171-190 m | **75 m, 93 m** | +40/+9 downrange, +12/+62 cross |
   | 200 km | 192-241 m | **136 m, 99 m** | +80/+22 downrange, +30/+57 cross |
   | 300 km | 205-218 m | **140 m** | -37 downrange, +57 cross |
   | 418 km | 342-350 m | **221 m** | +111 downrange, +7 cross |

   The long bias is gone except at 418 km. What is left is a 30-60 m cross offset that now has the same sign
   on most flights, and the release spread of 38-162 m.

   **The offset left is the residual at cutoff, and it is not random.** On the seven throw flights the
   residual's cross part is positive every time (0.12-0.45 m/s), and every group lands on the positive cross
   side (+7 to +62 m). Its radial part decides the downrange: -0.34 to -0.37 m/s landed +40 to +111 m long,
   -0.03 to +0.20 within about 40 m either way. The likely cause: `BurnoutGuidance` projects the rest of the
   burn at full thrust (`BoosterPerformance.SecondsToGain`, and the countdown counts full-thrust seconds), but
   a short shot spends its last seconds at its 12% floor. The real time left is then about eight times
   longer, and gravity, plus the stack's drag that the projection does not model at all, act over it
   uncompensated. Long shots hold the line at full thrust above the air and barely see it.

   Next, in order:
   1. ~~Project the burn at the throttle the ramp will hold.~~ Tried 2026-10-05 and lost in the rig
      (`FloorHoldTests` at the low arcs, 20 and 25 ms): the mean residual went from 1.59 to 3.70 m/s and the
      mean miss from 61 to 96 m. The rig also does not reproduce the flown residual: it already ends at about
      0.1 m/s on the full-thrust projection where flown shots end at 0.45, mostly across the line. The flown
      log points elsewhere: in the last 0.1 s the drag solve was still moving the aim (by 26 m) and its
      predicted miss swung between 56 and 81 m, which would leave a residual across the line.
   2. ~~Release without the sweep.~~ Done 2026-10-05, and the sweep was not the cause. Read off the
      flown logs, each salvo's warheads landed on one line in release order, each a frame later on a
      stack the air had slowed (about 50 mm/s a frame on Liquid2, 110-140 on SRB4), and the release
      probe predicted every landing to centimetres while its kick was refused over the 10 mm/s cap —
      kicks of 96-168 mm/s at 300 km, with each group walking 28-104 m along its line.
      `IcbmConfig.ShortShotReleasesTogether` lets the salvo go in one frame and
      `ShortShotMissKickMetresPerSecond` raises that salvo's cap (`2c0413d9`, both off). Flown paired
      (`~/shots/2026-10-05-together/`), worst warhead, defaults against both on at 1 m/s:

      | | 150 km | 300 km |
      | --- | --- | --- |
      | Real Liquid2 | 46 m -> 1.5 mm | 93 m -> 0.20 m |
      | Real SRB4 | 114 m -> 2.6 mm | 134 m -> 0.66 m |
      | GeoSat FAT | 87 m -> 3.8 mm | 115 m -> 0.63 m |

      And on GeoSat FAT alone (`~/shots/2026-10-05-cover/`): 25 km 7.2 mm, 200 km 4.2 mm, 418 km
      7.1 mm. 500 km released above the air as before, 4 m. The cap alone at 0.3 m/s was not enough:
      the later warheads wanted 306-638 mm/s. With the solids handled below as well, GeoSat FAT at 300 km
      came in at 29 mm and Real SRB4 at 1.32 m, the group within 4 mm either way: a common offset at
      300 km on some stacks: the ground read at the wrong place after cutoff, fixed below.
2. **Correct after cutoff in the air.** The bus's thrusters can still act between cutoff and release, solved
   against the drag-flown probe rather than a vacuum arc. The trim reads drag as debt in the air (M6), and
   coasting the bus alone through the air is what cost 107 km.
3. **The per-warhead kicks against the release probe**, once what is left is inside their 10 mm/s cap.
4. **The floor:** the drag model in the air, separation inside the air, and the arrival angle. No amount of
   correction removes these.

**Flown on rockets this mod did not ship** (2026-10-05). `tools/make-rocket.py` built five designs: Liquid2,
Liquid3, SRB4 (four boosters), Light (2 m) and Heavy. Each was turned into a scenario save with the bridge
(`TEST <name>`) and flown on `dev`'s defaults at 150 km and 700 km (`~/shots/2026-10-05-stacks{,2}/`):

| rocket | 150 km, first | 150 km, after the fixes below | 700 km |
| --- | --- | --- | --- |
| Liquid2 | 3.35 km | 0.52 km | 6 m |
| Liquid3 | 390 m | 283 m | 6 m, after a long tumble |
| SRB4 | 62 m | 81 m | 5 m |
| Heavy | 1.04 km | 166 m | 10 m |
| Light | fails both: its first stage's 4.2 km/s is spent low in the air, and its 0.67 g upper cannot finish | | |

Each first flight found something GeoSat FAT never showed:

- **Liquid2 needed more than the drag offset's cap.** It cuts off at 19 km on its first stage. The cap is now
  max(40 km, 25% of the distance).
- **Heavy's line stayed slowed to cutoff, and the frozen offset let its predicted miss grow from 0 to 856 m in
  the last 0.8 s.** The offset now keeps moving.
- **Liquid3 tumbled at 700 km.** Its second stage is a full A2 making 16 g, so it chases at the floor on a high
  arc, where the slowed line is off. Its third stage had the margin to finish anyway. Not fixed yet.
- **Light is guidance on a light, hot stack, not the rocket alone.** Its first stage alone carries 4,207 m/s
  against a 1,205 m/s shot, so "Reachable" on the pad was right on delta-v. But the closed loop took it at 1 km,
  and 50 s at the scenario's 8 g cap took what was left to gain only from 1,088 to 628 m/s. The stack pointed
  where it was told, to within 0.3°, so the commanded direction itself is what spent the stage, against drag
  and gravity low in the air. Then the 0.67 g upper stage could not finish. Open: find what direction the loop
  asks for there, and why it hands over at 1 km.

**Realistic stacks** (`~/shots/2026-10-05-real/`), lifting off at 1.2-1.5 with every stage able to lift what
is above it. The first liquid designs stood on one A2's bell and toppled where they were parked, breaking in
three, so the liquid first stages stand on four A3s. All nine passed, with no exceptions in KSA's log:

| rocket | 150 km | 300 km | 700 km |
| --- | --- | --- | --- |
| Real Liquid2 (4xA3, 1.49) | 72 m, spread 62 | 104 m, spread 76 | 4 m |
| Real Liquid3 (4xA3, 1.21) | 57 m, spread 30 | 49 m, spread 7 | under 0.5 m |
| Real SRB4 (A2 + 4 SRBs) | 73 m, spread 56 | 157 m, spread 88 | 3 m |

Worst warhead, and the group's spread. Liquid3 does not tumble at 700 km: the tumble was the old Test Liquid3's
16 g second stage, not three stages.

**The spin after the boosters drop**, and the swing at 150-300 km, were both the solids. At 25 km GeoSat FAT's
boosters overshoot: matched to the arc exactly and flown on the schedule, they left 13 m/s pointing 67 deg off
the nose, and the core, lit at the 0.9 the lever was left at, chased it at up to 115 deg/s. At 150-300 km they
were carried through a tail-off under the stack's weight -- 0.86 to 0.59 g for 6 s -- which sagged the path,
took what was left from 560 to 3,100 m/s and turned the stack into the separation at 19 deg/s.

Two things tried first and not in, in the rig with the lever carried through the solid stage as KSA's is:
lighting the core at its floor alone still chased (13 -> 56 m/s, 54 deg/s), because floor thrust 67 deg off
the line still pushes the wrong way; holding the engine off to turn stopped the spin and left 2-8 m/s, because
in the air the stack is not allowed to point 100 deg off its path.

What is in (`IcbmConfig.SolidsLeaveMetresPerSecond` 30, `DropSolidsUnderWeight`, both on):

- **The solids are steered along what is left and asked to leave 30 m/s**, so what they leave lies ahead of
  the nose rather than anywhere.
- **The arrival is pinned from their burnout every solve**, so the stage after them finishes the arc they were
  steered onto. Left free, the cheapest arc from a lofted one swung what was left from 30 m/s along the nose
  to 6 m/s 75 deg off it; pinned from the uncapped time to gain it was five seconds late and 20 deg off.
- **The stage after them lights at its floor**, the lever set while only solids burn, which they ignore: lit
  at full, 30 m/s is a 0.3 s burn at 2 m/s a frame.
- **On a short shot, solids under the stack's weight are dropped** when a stage is left after them.

Flown (`~/shots/2026-10-05-{solids,drop}/`), peak turn from the booster drop to cutoff and worst warhead:

| | before | now | worst warhead |
| --- | --- | --- | --- |
| GeoSat FAT 25 km | 115 deg/s | 6.4 deg/s | 37 mm |
| GeoSat FAT 60 km | | 9.7 deg/s | 14 mm |
| GeoSat FAT 150 km | 42-63 deg/s | 10.2 deg/s | 3.3 mm |
| GeoSat FAT 200 km | 48 deg/s | 8.0 deg/s | 5.1 mm |
| GeoSat FAT 300 km | | 15.9 deg/s | 29 mm |
| Real SRB4 150 km | 27-36 deg/s | 9.9 deg/s | 5.0 mm |
| Real SRB4 300 km | | 15.3 deg/s | 1.32 m |
| Real Liquid2 150 km, no solids | | 7.4 deg/s | 3.3 mm |

What is left at 150-300 km is one re-point of about 16 deg as the core takes over, then within 0.3 deg to
cutoff. `SolidsLeaveTests` holds both halves in the rig.

GeoSat FAT at 200 km on the same build landed 197 m, as before. And the bus is now let go once its salvo is away:
it used to hold its attitude, firing its thrusters, for the rest of its fall. That was flown on all five flights.

**The ground at the aim after cutoff** (2026-10-06). `TerrainRadiusAt` carried the aim back by the countdown to
cutoff whether or not the engines were still burning, and a short shot releases at cutoff before the aim cycle
clears it, so the release probe read ground up to a fraction of a second of the planet's turn away: 1.13 m lower
at 300 km. Read only while burning, Real SRB4 at 300 km went from 1.22-1.32 m to 5.4-7.7 mm.

**The kick cap after the trim** (`IcbmConfig.ShortShotKickCapAfterTheTrim`, on). An arc that climbs above the
air releases after the bus trim rather than at cutoff, so it kept the long shot's 10 mm/s cap on the kick that
cancels the release probe's miss. On ten flights at 500 and 700 km the probe predicted the landing to
centimetres, 2-10 m out, and every 19-50 mm/s kick was refused. With the short shot's 1 m/s cap
(`~/shots/2026-10-06-loss/`), worst warhead:

| rocket | 500 km | 700 km |
| --- | --- | --- |
| GeoSat FAT | 1.7 mm (probe 10.9 m) | 3.0 mm (probe 5.0 m) |
| Real Liquid2 | 1.8 mm (probe 0.7 m) | 2.7 mm (probe 3.4 m) |
| Real Liquid3 | 1.5 mm (probe 5.1 m) | 1.4 mm (probe 2.9 m) |
| Real SRB4 | 108 m, floor chase; **3.9 mm** with the backstop below | 156 m; **2.8 mm** |

**Every range from 100 to 418 km, four rockets, on `d1ec327c`'s defaults** (`~/shots/2026-10-06-matrix/`),
worst warhead of six, one flight each, every kick taken and no exceptions in KSA's log:

| rocket | 25 km | 100 km | 150 km | 200 km | 300 km | 418 km |
| --- | --- | --- | --- | --- | --- | --- |
| GeoSat FAT | 6.3 mm | 5.2 mm | 4.3 mm | 9.0 mm | 4.3 mm | 14.1 mm |
| Real SRB4 | 1.4 mm | 3.5 mm | 4.1 mm | 18.6 mm | 5.2 mm | 2.9 mm |
| Real Liquid2 | 0.5 mm, after hovering | 4.3 mm | 3.2 mm | 3.4 mm | 3.5 mm | 4.7 mm |
| Real Liquid3 | hovered, stopped | 4.2 mm | 5.7 mm | 4.8 mm | 3.6 mm | 3.5 mm |

The 25 km column was flown on the build before the kick cap, which does not reach a release at cutoff.

**A liquid stack hovered at 25 km; fixed 2026-10-09, below.** Real Liquid2 and Real Liquid3 both did, every time, and the rig does
not. Traced on 2026-10-06 (`~/shots/2026-10-06-hover/liquid2-diag`, altitude, climb and committed arrival on the
`cutoff approach` line): the closed loop enters its throttle-down at 6 km climbing 194 m/s, closes slowly at
0.2-0.4 throttle while the stack coasts up to 8.8 km and falls back, and ends **stood still 1.6 km over the
target**. There the committed arrival alternates between pinned 20.1-20.4 s ahead and released, solve by solve --
judged unsolvable, given up, and re-pinned at once because a short shot pins whatever the loop has. A 20 s fall
from 1.6 km needs an 18-22 m/s toss upward; with the arrival re-pinned the required velocity does not fall with
gravity, the ramp asks for what is left over two seconds, and it settles at thrust = weight. Liquid2 held 22 m/s at
1.00 g for twelve minutes, burned out most of two stages, and finished only when the throttle it asked for fell
under the engine's 0.112 floor; it then landed 0.5 mm out.

The likely root is the geometry rather than the throttle: a transfer from directly over the target is nearly
radial, which the docs name as a case the Lambert solve cannot answer, so the pin cannot hold there. Not in, all
flown or measured: the throttle-down plus gravity along the line, always (0.33 throttle at cutoff in the rig, where
the ramp ends at 0.03); plus the loss measured between solves (Liquid3 5.5 mm, Liquid2 **119 m**, 2.16 m/s short);
holding the drag offset once the ramp passes 30 s (still hovered: with the drag solve off the shot closed in 79 s
and landed 336 m out, so the moving offset is how the stack is slowed into the hover, not what holds it there);
and gravity added only once the ramp passes 30 s (no hover, closed in 2.5-3 min, but cut off 2.9 m/s short with
the arrival still flipping: 24.8 m and 26.1 m; GeoSat FAT untouched at 2.5 mm).

**It reproduces in the rig** (`ShortShotHoverTests`, a study): the Real Liquid2 fixture with the Mk 21 aboard, so the
drag solve runs, closes at 12 and 20 m^2 of drag and hovers at 30 and 45 m^2, stood still at 1.67 km with the arrival
re-pinned every other solve, as flown. Without the warhead the drag solve never runs and nothing stalls, which is why
the first fixture did not. Two phases: first the drag offset walks the aim at 100-250 m/s while the stack climbs out of
thick air, a loss the two-second ramp settles against; then gravity under the re-pinned arrival.

Also not in, from the rig and three flights: the loss measured over half a second and carried once the ramp has run
10 s, with the hold and the slowed line both counting the net closure rather than the thrust. In the rig it closed all
four drag areas at 0.10-0.18 m/s, and 1.51 at 45 m^2; flown, both liquids stopped hovering -- 46-54 s instead of twelve
minutes -- but bottomed out 1.5 m/s short with the aim still walking at ~100 m/s, and the 2 m/s backstop cut them off
2.1-2.5 m/s short: 65-116 m. Real SRB4 at 25 km and Real Liquid2 at 150 km were untouched, 1.8 and 7.1 mm.
Also not in: never re-pinning once the pin has been given up. In the rig the arrival is then free, and the stack
hovers exactly as long (1,108 s at 30 m^2): stood still over the target even the cheapest arc wants a ~20 m/s toss,
so the re-pin is not what holds it there, and the near-radial transfer is refuted as the cause.

**What ships instead is the truth on the panel.** Once the throttle-down has run `IcbmProgram.StalledRampSeconds`
(30 s, against a closing ramp's 10-15), the computer's hold reads "cutoff stalled: the stack is holding its own
weight, N m/s still to gain", and the log says so once. Flown on Real Liquid2 at 25 km: reported five minutes into a
twelve-minute hover, which then landed 0.2-0.4 mm out.

**Traced in the rig on 2026-10-09, and a candidate behind a switch.** The aim walk was not the missing term: the
rig's offset walks at 90-250 m/s through the throttle-down, as the flown `cutoff approach` lines do. Two things hold
the hover, one after the other:

* **The stall is the ramp's steady error.** The throttle asks for what is left over `ThrottleDownSeconds`, a
  proportional law, so against a steady push it settles with two seconds of that push still to gain. The push is the
  stack's own drag, which the vacuum arc does not see, plus the drag solve walking the aim. Freezing the drag offset
  from the start of the ramp makes it worse, not better (every area hovers, 12 m^2 lands 5 km out): with the offset
  held, to-gain still crawls from 15 to 1 m/s over thirty seconds at 0.1-0.27 throttle.
* **The hover is `BallisticArc.MinFlightSeconds`.** A stall long enough runs the pinned arrival down to 20 s; the
  pinned solve is then refused, and the cheapest arc cannot be shorter either, so the arrival reads 20.4 s for good,
  as flown. A toss onto a 20 s fall from 1.6 km is ~20 m/s, which is two seconds of one gravity: the ramp then
  commands exactly the stack's weight. Lowering the floor to 5 s only moves the hover (to a 5.5 s arrival, the same
  19.6 m/s to gain); the floor is where it sits, not why it starts.

`IcbmConfig.ShortShotPushesThroughAStall` (on since 2026-10-09) acts once the ramp has run
`IcbmProgram.PushesThroughAfterSeconds` (10 s) in the air: the push is measured from how the velocity to gain moved
against what the thrust should have done, its component along the line is added to the throttle, the line is led
across it by what it will add before the burn ends, and the steering freezes only under 1 m/s. All three are needed:
the throttle alone closes every area but leaves 0.5-1.7 m/s **across** the frozen line (31.7 m at 12 m^2), which is
plausibly the 1.5 m/s the 10-06 feed-forward flights bottomed out on. In `ShortShotHoverTests`:

| drag area | off | on |
| --- | --- | --- |
| 12 m^2 | 32.7 s, 0.11 m/s, 6.1 m | 32.5 s, 0.12 m/s, 6.4 m |
| 20 m^2 | 41.8 s, 0.37 m/s, 4.0 m | 36.8 s, 0.28 m/s, 7.0 m |
| 30 m^2 | **1,108 s**, 0.13 m/s | 41.0 s, 0.55 m/s, 2.8 m |
| 45 m^2 | **1,087 s**, 0.15 m/s | 45.0 s, 0.53 m/s, 1.6 m |

Closed-loop seconds, residual at cutoff, landing from the cutoff state. The whole suite passes with it on. Gated on
the stalled ramp because ungated it spins `FloorHoldTests`' stacks at their floor and moves two completion bounds.
**Flown 2026-10-09**, one flight each on `agent/hover` (`~/shots/2026-10-09-push`, the switch set by the scenario's
arm, verbose):

| flight | throttle-down | cut off short | six warheads |
| --- | --- | --- | --- |
| Real Liquid2, 25 km, on | **18 s**, cut at 8.6 km climbing | 0.51 m/s | 0.4-1.8 mm |
| Real Liquid3, 25 km, on | **18 s**, cut at 9 km | 0.41 m/s | 8.6-10.1 mm |
| Real Liquid2, 150 km, on | 15 s | 0.06 m/s | 2.2-4.0 mm (7.1 on 10-06) |
| Real Liquid2, 25 km, off | **12.5 min**, "cutoff stalled" | 0.14 m/s | 0.1-0.4 mm |

No exceptions in any KSA log. The hover is gone on both liquids, and Liquid3, which on 10-06 hovered and never
finished, lands. What it costs is the hover's accidental precision: off, the stack finishes at its floor, stood still
1.6 km over the target, and lands closer. **Shipped on.** The 150 km ramp ran past the 10 s gate, so the switch may have acted there
too; it says no harm on a ramp that closes, not that it stays out of one.

**A hot core overshot its cutoff, fixed** (Real SRB4, 500 and 700 km; `IcbmConfig.ShortShotBackstopsAtTheTrim`,
on). Its core makes 7-13 g, and at its 0.12 floor still 0.8 g, so the ramp's last seconds are spent on the
floor. Flown at 500 km it closed from 65 m/s to 2.4 m/s, with its thrust lagging the line by enough to leave
that much square to it, missed the 2 m/s backstop by 0.4, and drove straight through: what was left grew to
53 m/s, came back to 8, overshot again, and the stack chased its own line at up to 50 deg/s for minutes,
finishing inside the air and releasing at cutoff -- 108 m at 500 km, 156 m at 700 (`~/shots/2026-10-06-loss/`).
On a short shot whose arc releases after the trim the backstop now arms at the trim's 10 m/s rather than 2: the
residual turning back up ends the burn -- or, in the air, coasts it out -- and the trim takes the rest. Flown
(`~/shots/2026-10-06-srb4/`): cut off at 87 km with 10.9 m/s left, the bus trimmed 7.2 m/s, **3.9 mm** at 500 km
and **2.8 mm** at 700; GeoSat FAT at both never tripped it, 0.9 and 2.7 mm. Not in: cutting off instead of
stopping the engine to turn once under the trim's reach above the air -- it never fired, because the overshoot
happens in the air and above 10 m/s.

**In the rig**, five stacks (the game's, an all-solid three-stage, the SRBs alone, a liquid pair, a 25 MN core
with a 40% floor), 25–5,000 km: every stack cuts off at every range it can reach, 0.0–3.1 km out, bar two at about
5 km (all-solid 200 km, SRBs alone 2,000 km). With its default attitude the rig cannot start a tumble, so it
did not reproduce two of the night's flown failures; `AttitudeHasInertia` can.
`AscentReserveGoldenTests` holds every long fixture bit-equal with it on.

**Still open.** Correcting the aim from inside the air was tried and is not in: worse for liquids, mixed for
solids. The core still spends propellant at its floor while turning (200 km used most of the core that way), and
in-air cutoffs leave the drag the prediction already names — 1.6 km at 200 km against a prediction of 1.63. A
drag-aware arc, solved rather than corrected after, is the lever for both.

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
  **Met 2026-10-05:** the golden harness is bit-equal with every short-shot setting on, and both flights logged
  none of the new code. 2,000 km landed under a metre (`3d1ed5d`). Chaco, on `dev` with the settings on by
  default (`3cc3572`), cut off at 183 km and landed six of six 0.2-2.4 mm from the aim.
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

Depressed or terrain-following trajectories;
multi-target walks under ~485 s of coast — they collapse to one stop, and the reach display should say so.
