# What arrival angle buys

**The question.** A kinetic round has no lethal radius to hide inside, so precision *is* the weapon.
Every ballistic shot this mod has flown arrives between **12.9 and 17.5 degrees** — not the seven
this file was written around, which was reconstructed and never flown; `docs/ACCURACY-PLAN.md` 3af
has the logged numbers. The parametric tables below stand as arithmetic at the angle each states;
what does not is any claim about where the mod actually arrives. Every large term in
the miss budget is that angle multiplied by something. This is what a steeper arrival is worth, what
it costs, and how the guidance is told to fly one.

**The control is `IcbmConfig.MinArrivalAngleDeg`**, and it is **off by default**, so everything about
the shots described here is still what the mod does out of the box. *The floor, which is what
expresses it* is the section on how it works and what it costs.

**Everything below is measured** by `tests/KSArmory.Tests/ArrivalAngleTests.cs`,
`ArrivalFloorTests.cs` — the search — and `ArrivalFloorFlightTests.cs`, which flies the whole program
at a floor. Only the two shots under *Flown* have left the ground. `docs/ICBM-GUIDANCE.md` is the
guidance itself; `docs/KSA-TERRAIN.md` is where the surface numbers come from.

**What the rig cannot see.** The planet sits at the origin and does not move, which is the one case
where a frame carrier is identically zero — so nothing here can measure an epoch fault, including the
open one in `docs/ICBM-GUIDANCE.md` about the ground a round meets. What it *can* say is that such a
fault, if it is a height error, is multiplied by the same `cot γ` as everything else on this page,
and that is 8.1 at seven degrees and 1.7 at thirty.

---

## Seven degrees is the air's answer, not the guidance's

The cheapest deorbit leaves on a **3.6 degree** vacuum arc and arrives at **7.1**. Entry bends a graze
back up: drag kills the horizontal component faster than the vertical, so a shallow arrival is dragged
toward a terminal angle set by the round rather than by the trajectory it was put on.

That angle is a floor. Braking *less* makes the vacuum arc shallower and the flown arrival no
shallower at all — measured across every retrograde brake from 120 m/s to a full stop:

| platform | shallowest arrival any brake reaches |
| --- | --- |
| circular 300 km | **7.06°** |
| circular 400 km | **7.00°** |
| circular 500 km | **6.96°** |

So **five degrees is not reachable with a Mk 21**, and the seven the flights arrive at is not a choice
the guidance made. It is the shallowest thing that round can do.

The floor belongs to the *round*, and moves a long way with its sectional density:

| `DragK` | vs the Mk 21 | floor | at | keeping | a straight drop lands at |
| --- | --- | --- | --- | --- | --- |
| 1.5e-4 | 0.1x | 17.21° | 2,323 km | 784 m/s | 910 m/s |
| **1.5e-5** | **1x (Mk 21)** | **7.00°** | 8,565 km | 2,858 m/s | 2,414 m/s |
| 5e-6 | 3x | 4.43° | 12,857 km | 4,417 m/s | 2,613 m/s |
| 1.5e-6 | 10x | 2.76° | 15,607 km | 5,994 m/s | 2,687 m/s |
| 1.5e-7 | 100x | 1.40° | 15,897 km | 7,663 m/s | 2,716 m/s |

A denser rod — which is what "rods of god" means — makes the **shallow** entry survivable and cheap,
and makes precision worse, because it lowers the floor into the region where `cot γ` is largest. It
buys nothing at all on a vertical drop: 2,687 m/s against the Mk 21's 2,414, against 2,719 in vacuum.

---

## The control is bounded by what the tanks can pay for

Arrival angle is bought with propellant, and how much of it a given angle costs is a property of
*this* stack against *this* target — nothing an operator can be expected to know in advance. A
slider running to a round 45 degrees therefore lets them ask for an arc that cannot be flown, and
the mod does not refuse such a shot: it flies the shallowest arc it can afford and arrives, less
precisely. Correct behaviour — and it has to **say so at the time**, which is two separate things.

**Before the shot**, the slider's ceiling *is* the statement: it stops at what the stack can buy, and
the line under it reads `the stack can afford N deg from here`, amber once the setting is against it.

**During the shot**, `IcbmProgram.ArrivalFloorUnaffordable` says the arc being held is shallower than
what was asked for. It is on the panel with the unreachables rather than beside the slider — amber,
because the shot still arrives — and once in the log, because an unattended shot has no panel and
the log is the only place it can be read afterwards.

Flown 2026-08-27 on a 12,902 km shot with the floor forced to 85 degrees, which this stack cannot
buy: `cannot afford the 85 deg arrival asked for; flying 19.3 deg instead -- it will arrive, less
precisely`, once, and it did — 6 of 6 warheads, 4.80 km. **The threshold is not guessable**: 60
degrees turned out to be *affordable* on the same stack and shot, and the rocket lofted to a
2h51m arc to fly it. So an unaffordable floor makes the flight **shorter**, not longer, because
what it falls back to is the cheap arc.

That flag **describes the arc currently held rather than latching**. A stack is heaviest at lift-off
and the mid-ascent geometry is the worst it will be, so a cycle that cannot afford an arrival is
routinely followed by one that can; a flag that only ever goes true calls the shot compromised for
the rest of the flight. It clears on `Reset()` too, which it did not for its whole first life —
`Reset` cleared twenty-five fields and missed it, so a vehicle that once could not afford its
arrival carried the claim into every later flight.

`Sim/ArrivalBudget.cs` bisects on **affordability** rather than on cost — cost is not monotonic in
the floor, because the flight-time search jumps families, but affordable/not is the thing being
chosen between and the boundary is the honest reading either way. `IcbmProgram` refreshes it on a
slow *real*-time cadence, `ArrivalBudgetIntervalSeconds`, for the same reason the departure window
has one: it is a handful of trajectory solves. Measured at **9 ms** for a full bisection, so ten
seconds is conservative rather than tight.

Three readings, and they are three different sentences on the panel:

| answer | means |
| --- | --- |
| a number | the ceiling; the slider stops there |
| **zero** | the stack cannot afford *any* arc to that target |
| **NaN** | nothing costed yet — not the same as zero, and must not read as one |

**The maximum never falls below where the slider already is.** The ceiling drops as the tanks empty,
and a bound that walks down past a live setting rewrites the operator's choice mid-flight without
saying so.

Delta-v is taken from the **stack**, not the running stage, where the engine reports it. KSA reports
the engines that are lit, which understates a staged rocket badly enough to cap this control at a
few degrees on a vehicle that could comfortably fly thirty.

## The table

The family is a circular platform braking retrograde, which is the only degree of freedom a deorbit
has. How hard it brakes sets the arrival angle, the downrange, the flight time and the speed left
**all at once** — they cannot be traded against one another.

From **400 km**, through the real drag model and the real `ImpactPredictor`:

| arrival | brake | downrange | flight | impact | `cot γ` | dMiss/dV pro / rad / cross (rms) | a 10% drag error | miss @0.017 m/s | miss @0.5 m/s |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 5° | — | *unreachable: the air will not let this round arrive shallower than 7.00°* | | | | | | | |
| **7.5°** | 473 | 6,379 km | 914 s | 3,330 m/s | 7.60 | 7,559 / 6,072 / 745 (**5,614**) | **1,795 m** | 1,797 m | 3,332 m |
| 10° | 929 | 4,237 km | 646 s | 4,037 m/s | 5.67 | 2,703 / 2,974 / 583 (2,345) | 383 m | 385 m | 1,233 m |
| **15°** | 1,774 | 2,736 km | 475 s | **4,407 m/s** | 3.73 | 1,096 / 1,431 / 449 (1,072) | 77 m | 79 m | 542 m |
| **20°** | 2,576 | 2,015 km | 405 s | 4,321 m/s | 2.75 | 684 / 892 / 389 (686) | 29 m | 31 m | 344 m |
| 30° | 3,911 | 1,272 km | 346 s | 3,821 m/s | 1.73 | 433 / 466 / 336 (415) | 8 m | 11 m | 208 m |
| 45° | 5,307 | 732 km | 316 s | 3,144 m/s | 1.00 | 336 / 224 / 309 (293) | 2 m | 6 m | 147 m |
| 60° | 6,277 | 418 km | 306 s | 2,721 m/s | 0.58 | 306 / 101 / 299 (254) | 1 m | 4 m | 127 m |
| 88.7° | 7,673 | 0 km | 301 s | 2,414 m/s | 0.02 | 294 / 53 / 294 (242) | 0 m | 4 m | 121 m |

The last two columns are the whole shot in quadrature: the stated residual times the root-mean-square
sensitivity, plus the drag-model term, plus one metre of disagreement about where the surface is.

300 km and 500 km give the same shape with the downrange scaled — at 300 km a 20° arrival reaches
1,540 km and costs 2,956 m/s; at 500 km it reaches 2,472 km and costs 2,305.

**The idealised arc these numbers are built on is the same one the rest of the budget uses.** The
cheapest transfer from a 200 km pickup measures 3,678 / 6,281 / 447 m per m/s in
`docs/MIRV-NEXT.md`, and the same arc measures 4,234 rms here. The guided trajectory a real burn
leaves the bus on is 1,789 / 3,442 / 390, which sits between the 10° and 15° rows.

### Three separate things improve, at three different rates

| term | 7.5° → 20° |
| --- | --- |
| **velocity sensitivity** — every metre a second at cutoff, the trim residual, the release kick, the tube cant | 5,614 → 686, a factor of **8** |
| **surface** — the height quantum, the held terrain sample, the sea the clamp now catches, any height-shaped epoch fault | `cot γ` 7.60 → 2.75, a factor of **2.8** |
| **the drag model** — the part no correction loop can remove, because its only observer shares the model | 1,795 m → 29 m, a factor of **62** |

The third one is the surprise and it is the biggest. A correction loop can only remove what its
observer can see, and the observer is `ImpactPredictor` running the round's own `Medium.Drag`. So
whatever that model has wrong survives the loop intact. At 7.5° the drag is worth 13.4 km of range
and a ten per cent error in it is **1.8 km on the ground** — larger than the entire flown miss. At 15°
the drag is worth 0.3 km and the same error is 77 m. At 20° it is 29 m and stops being a term at all.

The surface terms, from `docs/KSA-TERRAIN.md` put through `cot γ`:

| arrival | ground per m of height | the field's own 0.2985 m quantum | one held terrain sample, 5% slope | the mean sea depth, which the clamp takes out |
| --- | --- | --- | --- | --- |
| 7° | 8.14 m | 2.43 m | 8.68 m | 30.8 km |
| 15° | 3.73 m | 1.11 m | 4.72 m | 14.1 km |
| 20° | 2.75 m | 0.82 m | 3.62 m | 10.4 km |
| 30° | 1.73 m | 0.52 m | 2.39 m | 6.5 km |
| 60° | 0.58 m | 0.17 m | 0.84 m | 2.2 km |

### And there is a fourth thing, which is not a multiplier but a degeneracy — flown 2026-08-25

Every term above scales an error by `cot γ`. Real ground does something worse: where the terrain
**falls away at the arrival gradient itself**, the trajectory and the surface are parallel and the
impact point stops being a well-defined function of the trajectory at all.

Forty-eight shots at 26.485S 68.148W, in `~/shots/2026-08-25` and written up as
`docs/MIRV-NEXT.md` item 7g. Every impact of the night landed at one of five places, all on one line
of bearing 075°/255° — the ground track — with **the aim point on a crest**:

| site | from the aim point | ground | shots landed |
| --- | --- | --- | --- |
| C | 1.51 km at 262° | 4.560 km | 1.40-1.50 km |
| **A — the aim point** | — | **4.657 km** | 0.04-0.14 km |
| B | 0.37 km at 053° | 4.646 km | 0.34-0.54 km |
| D | 4.43 km at 075° | 4.190 km | 4.50-4.60 km |
| E | 5.54 km at 076° | 4.128 km | 5.40-6.00 km |

The ground rises to the crest from the WSW at a gradient of 0.064 and falls away to the ENE at
**0.1055**. The warheads arrive at **5.7°**, a gradient of **0.0998** — the two agree to **5.6%**. So
a few tens of metres of trajectory height at the crest decides between stopping on it and running
four and a half kilometres down the back: 467 m of fall times `cot 5.7°` = 10.02 is 4.68 km, and the
measured run-out is 4.43-4.60 km. Eight of twenty-three baseline shots went over.

**No guidance work reaches this.** The aim loop, the predictor and the cutoff were all doing their
job on those shots — the release probe read 0.6 km and the warheads went 4.6, on the same trajectory
that landed 40 m out when it stopped on the crest. What separates the two outcomes is which side of a
hill a sub-kilometre aim difference falls on.

Steepening removes it outright rather than dividing it down: at 15° the arrival gradient is 0.268
against the same 0.1055 of terrain, and the intersection is well conditioned with room to spare. This
is the first *flown* evidence for anything on this page, and it arrived by accident — the target was
chosen for a guidance experiment, not for its relief.

**And it says something about how any target should be read.** A shot group is only a measurement of
guidance if the ground it lands on is well conditioned for the angle it arrives at. Before quoting a
miss distance at a new target, check the terrain gradient along the last few kilometres of the ground
track against `tan γ`; where they are within a few tens of per cent, the number measures the hill.

---

## What steepening costs

### From orbit: propellant, and the downrange with it

The energy is the platform's orbital velocity, so braking to arrive steeply is spending the thing
that does the damage. At the 3,100 m/s exhaust velocity `DeorbitTests` flies:

| arrival | downrange | brake | mass ratio | fraction of the stack that arrives |
| --- | --- | --- | --- | --- |
| 7.5° | 6,379 km | 473 m/s | 1.16 | 85.8% |
| 10° | 4,237 km | 929 m/s | 1.35 | 74.1% |
| **15°** | 2,736 km | 1,774 m/s | 1.77 | **56.4%** |
| **20°** | 2,015 km | 2,576 m/s | 2.30 | **43.6%** |
| 30° | 1,272 km | 3,911 m/s | 3.53 | 28.3% |
| 45° | 732 km | 5,307 m/s | 5.54 | 18.1% |
| 60° | 418 km | 6,277 m/s | 7.57 | 13.2% |
| 88.7° | 0 km | 7,671 m/s | 11.88 | 8.4% |

### At a fixed range: the same propellant, and it stops paying

`IcbmConfig.Loft` multiplies the cheapest flight time, which is the other route to a steep arrival:
hold the range and fly a taller arc. It works, and it has a knee. Same 3,459 km shot from a 200 km
cutoff:

| loft | arrival | Δv | apogee | mass ratio | rms sensitivity | what the last km/s bought |
| --- | --- | --- | --- | --- | --- | --- |
| 1.00 | 7.10° | 373 m/s | 200 km | 1.13 | 4,234 | — |
| 1.20 | 9.99° | 1,336 | 238 km | 1.54 | 2,210 | 2,103 |
| **1.40** | **15.57°** | 2,264 | 346 km | 2.08 | **1,692** | **558** |
| 1.60 | 21.37° | 2,997 | 473 km | 2.63 | 1,483 | 284 |
| 1.80 | 26.83° | 3,594 | 610 km | 3.19 | 1,373 | 185 |
| 2.50 | 41.63° | 5,051 | 1,146 km | 5.10 | 1,189 | 126 |
| 4.00 | 56.46° | 6,707 | 2,403 km | 8.70 | 1,033 | 94 |
| 9.00 | 65.75° | 8,546 | 6,464 km | 15.75 | 931 | 36 |
| 12.00 | 66.10° | 8,897 | 8,663 km | 17.64 | 1,027 | **−275** |

**Range sets a floor under the sensitivity that no amount of steepening reaches.** A shot that has to
cover 3,459 km has a long lever whatever angle it comes in at, so the curve bottoms out near 930 and
then *rises* — past loft 9 the arc is long enough that flight time gives the gain back. Inside the
panel's own 0.6–1.8 range, going from 7° to 27° costs 3.2 km/s and buys a factor of three.

**So the trade turns at about fifteen degrees, and it turns for the same reason on both routes.** The
first kilometre a second buys thousands of metres per metre a second; the next buys hundreds; past 20°
it buys tens. Nothing about the guidance gets worse as the shot steepens — the cutoff residual is one
frame of thrust and does not care how long the burn was — so there is no accuracy penalty to weigh.
What steepening costs is reach and propellant, and that is the whole trade.

---

## Impact speed, which is the other half of what a rod wants

| brake | downrange | arrival | impact | square to the ground | specific energy |
| --- | --- | --- | --- | --- | --- |
| 500 | 6,172 km | 7.62° | 3,389 m/s | 450 m/s | 5.74 MJ/kg |
| 1,100 | 3,804 km | 11.01° | 4,179 m/s | 798 m/s | 8.73 MJ/kg |
| 1,700 | 2,823 km | 14.56° | 4,400 m/s | 1,106 m/s | 9.68 MJ/kg |
| **2,000** | **2,495 km** | **16.37°** | **4,410 m/s** | 1,243 m/s | **9.72 MJ/kg** |
| 2,800 | 1,863 km | 21.50° | 4,259 m/s | 1,561 m/s | 9.07 MJ/kg |
| 4,200 | 1,148 km | 32.62° | 3,684 m/s | 1,986 m/s | 6.79 MJ/kg |
| 6,400 | 380 km | 62.23° | 2,677 m/s | 2,368 m/s | 3.58 MJ/kg |
| 7,673 | 0 km | 88.73° | 2,414 m/s | 2,414 m/s | 2.91 MJ/kg |

**Total impact speed peaks at 16°** and falls away either side: shallower and drag has eaten it,
steeper and the brake has. A vertical drop arrives with **a third of the energy** of the fastest
arrival, because a vertical drop threw the orbital velocity away to get there.

The component *square to the ground* does not peak — it climbs all the way to vertical. So which end
of this table a penetrator wants depends on whether it is limited by total energy or by the normal
component, and that is a question about the penetrator rather than about the trajectory.

---

## What `Loft` alone could do, and why it was not enough

**`IcbmConfig.MinArrivalAngleDeg` now asks for an arrival angle directly.** What follows is
why it had to exist: before it, the only knob that looked like it could ask for one would
sometimes do the opposite.

There was no arrival-angle parameter anywhere in `Sim/`. `BallisticArc.TryCheapest` minimises the
length of the velocity still to gain over flight time, `BurnWindow.TryFind` minimises the same thing
over departure time as well, and `IcbmConfig.Loft` multiplies the flight time the first of those
settled on. Arrival angle was an output of all three.

`BurnWindow` searches a day of departures and takes the earliest within `GoodEnoughFraction` of the
cheapest. The cheapest is the graze, always — so the search actively *avoids* the geometry a rod
wants, which is the target nearly underneath and a hard brake. Measured, a 400 km circular platform
against a fixed target:

| target | loft 1.0 | loft 1.4 | loft 1.8 |
| --- | --- | --- | --- |
| 556 km ahead | burns now, 3,974 m/s, **33.9°** | burns now, 4,308 m/s, **36.5°** | **waits 5,441 s**, 634 m/s, **6.2°** |
| 2,224 km ahead | burns now, 1,149 m/s, 10.3° | waits 5,016 s, 240 m/s, 3.1° | waits 4,652 s, 175 m/s, 2.2° |
| 5,004 km ahead | waits 4,849 s, 153 m/s, 1.7° | waits 5,372 s, 230 m/s, 3.2° | waits 4,759 s, 146 m/s, 1.5° |
| 10,008 km ahead | waits 4,627 s, 118 m/s, 0.0° | waits 4,678 s, 1,134 m/s, 8.0° | waits 4,852 s, 2,530 m/s, 17.8° |

Three things fall out of that table.

**The steep shot is already reachable, and only by accident.** A target 556 km ahead is engaged at
33.9° at loft 1.0, because from close in the cheapest arc *is* the hard brake. Nothing chose that; it
is where the minimum happened to be.

**Loft is not an arrival-angle control from orbit — it can invert the arrival.** Raising it makes
leaving *now* more expensive as well as making the arc taller, and `BurnWindow` re-optimises the
departure under the new cost. At 556 km the saving from waiting is 4,117 m/s, far over
`IcbmProgram.WaitMustSaveMetresPerSecond`, so the computer defers an hour and a half and takes a
**6.2°** graze instead of the 33.9° shot it would have taken with loft off. The operator asked for
steeper and got the shallowest arrival on the page.

**Where loft does steepen, it is because the wait decision did not flip.** At 10,008 km the wait is
already chosen and loft then buys 0.0° → 17.8° for 2.4 km/s. Whether it helps or inverts depends on a
threshold nothing in the panel shows.

---

## The floor, which is what expresses it

`IcbmConfig.MinArrivalAngleDeg` is the shallowest the warheads may come in, in degrees below the
local horizontal. **Zero is off and off is the default**, so nothing above this line changed until
somebody moves the slider.

**It is a bound on the search rather than a nudge to it**, and that one distinction is the whole
design:

- `BallisticArc.TryCheapest` costs a candidate flight time at infinity if its arc arrives shallower
  than the floor, so the golden section returns the cheapest arc that *satisfies* it rather than the
  cheapest arc there is.
- `BurnWindow.TryFind` threads the same number into its per-departure cost, so a departure with no
  satisfying arc is not a window at all — and the machinery that already takes the earliest
  affordable departure then takes the earliest affordable *satisfying* one, with nothing else
  changed.
- Nothing anywhere satisfying it is `IcbmReach.TooShallow`, which is a third answer beside
  `NoTrajectory` and `ShortOfPropellant`. It says which of the three it is because only one of them
  is fixed by a control on the panel.

**A predicate is idempotent where a multiplier is not**, which is why the floor can be seeded back
into the next cycle's search and `Loft` cannot. The arc that satisfied the floor satisfies it again;
a flight time multiplied by 1.4 and fed back is multiplied again, which is the 162 km runaway
`docs/ICBM-GUIDANCE.md` records and the reason `CheapestFlightSeconds` is carried separately.
`ArrivalFloorTests.ReSolvingFromItsOwnAnswerDoesNotWalkTheShotOut` runs twelve cycles at loft 1.4
with an 18° floor and gets 639.6 s every time.

### The two coexist, and the floor wins

`Loft` is kept. It is load-bearing for the arrival latch — `docs/MIRV-NEXT.md` item 7 — and it is
still the way to ask for a taller arc at a range where the floor is already satisfied. What changed
is the order of precedence: loft multiplies whatever the constrained search settled on, and if the
lofted time no longer satisfies the floor the constrained time is flown instead. A nudge does not
get to walk out of a bound the operator set.

That is not a hypothetical. Loft below one *depresses* the shot, which is exactly the arrival the
floor exists to refuse: without the check, a 0.6 flattens a satisfying 20° arc back to **3.53°**.
`ArrivalFloorTests.LoftBelowOneCannotFlattenAnArcBackUnderTheFloor` fails with that number against
the old search.

And the inversion is gone. The same 556 km shot, floor at 15°:

| loft | floored | free |
| --- | --- | --- |
| 0.6 | waits 4,229 s, 1,851 m/s, **15.00°** | 5,525 m/s, 32.29° |
| 1.0 | waits 4,229 s, 1,851 m/s, **15.00°** | 3,974 m/s, 33.91° |
| 1.4 | waits 3,411 s, 3,233 m/s, **21.92°** | 4,308 m/s, 36.54° |
| 1.8 | waits 3,639 s, 3,973 m/s, **27.10°** | waits 5,441 s, 634 m/s, **6.24°** |

The bottom-right cell is the defect: asking for the panel's steepest loft got the shallowest arrival
on the page. With the floor set it is monotone.

### Measured on the arc, not through the air

The floor is applied to the vacuum arc, because the arc is what the search has — the drag model
lives in `ImpactPredictor`, and flying one per candidate flight time would put a trajectory
integration inside a golden section. What that approximation costs, from a 400 km platform onto a
target 2,224 km ahead:

| floor | arc arrives | flown arrives |
| --- | --- | --- |
| 10° | 10.29° | 10.44° |
| 12° | 12.00° | 12.01° |
| 15° | 15.00° | 14.85° |
| 20° | 20.00° | 19.68° |
| 30° | 30.00° | 29.51° |

Under half a degree across the useful range, conservative at the shallow end and optimistic at the
steep. It only diverges where the arrival is already a graze, which is the 3.6° → 7.1° at the top of
this page and is the regime the floor exists to leave.

### What it costs, asked the other way round

The table near the top brakes a platform by hand and reads off the arrival. This asks the guidance
for the arrival and reads off the brake, which is the number an operator actually spends. 400 km
platform, `ExhaustVelocity` 3,100 m/s:

| target | floor | wait | burn | arc | flown | stack arriving | impact |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 2,224 km ahead | off | 0 s | 1,149 | 10.29° | 10.44° | 69.0% | 4,355 m/s |
| | 10° | 5,198 s | 1,021 | 10.00° | 10.22° | 71.9% | 4,187 m/s |
| | **15°** | 4,201 s | **1,927** | 15.00° | 14.81° | **53.7%** | 5,287 m/s |
| | **20°** | 4,303 s | **2,596** | 20.00° | 19.70° | **43.3%** | 5,818 m/s |
| | 30° | 4,505 s | 3,953 | 30.00° | 29.64° | 27.9% | 6,497 m/s |
| 5,004 km ahead | off | 4,849 s | 153 | 1.72° | 7.54° | 95.2% | 2,484 m/s |
| | 10° | 173 s | 972 | 10.00° | 10.25° | 73.1% | 4,077 m/s |
| | **15°** | 0 s | **1,691** | 15.00° | 14.83° | **58.0%** | 4,635 m/s |
| | **20°** | 0 s | **2,342** | 20.00° | 19.68° | **47.0%** | 4,890 m/s |
| | 30° | 0 s | 3,521 | 30.00° | 29.57° | 32.1% | 5,161 m/s |

So at a **fixed** target the trade is the one the recommendation already describes, priced in the
operator's own currency: 15° costs 0.8–1.5 km/s over the graze and about a third of the stack, and
20° costs 1.4–2.2 km/s and about half. The reach is not given up at all here, because the target is
where it is — what the earlier table calls "downrange" is what the *cheapest* shot at that angle
happens to reach, and constraining the angle at a fixed range spends the difference on propellant
instead.

### From orbit the floor is a price, not a wall

Searching a day of departures, every floor short of vertical is satisfiable from a 400 km circular
platform — measured at 45°, 60°, 75°, 85° and 89.5°, costing 5.0, 6.2, 7.5, 7.4 and 8.1 km/s, which
is the orbital velocity being spent. Only an exactly vertical arrival has no window at all, because a
transfer arriving radially at a point is the degenerate case where no plane is determined.

**Mid-ascent is where a floor is a wall.** From a 60 km pick-up already committed to a direction, a
target 90° round the planet reaches 46° of arrival and no further, and a 55° floor is refused with
`IcbmReach.TooShallow` and the line *"nothing arrives at 55 deg or steeper from here; the cheapest
arc arrives at 17 deg"*.

### What the floor is still not

- **It now constrains a latched arrival too, and the drift it was assumed to bound was not bounded.**
  Pinning the arrival *instant* leaves the burn time and the flight time free to trade against each
  other inside it; the cheaper split is the longer coast, and a longer coast onto the same target is
  a shallower arrival. Measured at 3,459 km, the flown arc against the floor asked for:

  | floor | flown, before | flown, now |
  | --- | --- | --- |
  | 10° | **9.21°** | 11.07° |
  | 15° | **11.42°** | 20.36° |
  | 20° | **15.51°** | 26.94° |

  So the operator was handed the arrival they had refused — a fifth of the sensitivity benefit at
  15°, which is the whole reason the control exists. `BurnoutGuidance.TrySteer` now refuses a held
  arc under the bound, which hands the cycle back to the constrained search and re-commits to an
  arrival that satisfies it. **Gated on the bound being set**, so nothing on the default path can
  reach it; `ArrivalFloorFlightTests.AHeldArrivalIsHeldWhateverItArrivesAtWhenNoFloorIsSet` is where
  that is decided rather than in a downstream miss. Unflown.
- **It does not know about propellant.** A floor that turns a 150 m/s deorbit into a 3.5 km/s one is
  accepted by the search and then reported as `ShortOfPropellant` by `AssessReach`, which is the
  right division of labour and still two readouts to look at rather than one.
- **The coast half of it does not work.** The search flies the constrained arc and the shot cuts off
  on it; what follows is *Flown* at the bottom of this page.

---

## Recommendation

**Fly a 15–20 degree arrival, deorbited by braking hard onto a target 2,000–2,700 km ahead.** Set
`MinArrivalAngleDeg` to 15 or 20 and leave `Loft` at one: the floor asks for the angle directly,
where loft asks for a flight time and lets the angle fall out of it.

| | at the flown 7.1° | at 15° | at 20° |
| --- | --- | --- | --- |
| velocity sensitivity, rms | 5,614 | 1,072 | 686 |
| a 10% error in the drag model | 1,795 m | 77 m | 29 m |
| one metre of surface disagreement | 8.1 m | 3.7 m | 2.8 m |
| whole shot at the trimmed 0.017 m/s residual | **1,797 m** | **79 m** | **31 m** |
| whole shot at a 0.5 m/s residual | 3,332 m | 542 m | 344 m |
| downrange from a 400 km platform | 6,379 km | 2,736 km | 2,015 km |
| brake | 473 m/s | 1,774 m/s | 2,576 m/s |
| fraction of the stack that arrives | 85.8% | 56.4% | 43.6% |
| impact speed | 3,330 m/s | **4,407 m/s** | 4,321 m/s |

Fifteen to twenty degrees is where four separate things are simultaneously true, which is why it is
the answer rather than "steeper is better":

- **The drag model stops mattering** — 62x down, from a term larger than the whole flown miss to one
  smaller than the height field's own quantisation.
- **Impact speed is at its maximum.** 4,410 m/s at 16.4°, against 3,330 at seven degrees and 2,414
  straight down. The steep end of the table is the *slow* end.
- **The sensitivity has taken most of the fall it is going to take.** 8x by 20°; another 2.8x costs
  4 km/s more and the whole of the reach.
- **The downrange is still a weapon.** 2,000–2,700 km from one pass, against 418 km at 60°.

**What the miss would be** is not measurable here, because the flown 0.45–0.59 km is made of terms
this rig does not all carry — `docs/MIRV-NEXT.md` item 9 has them. What can be said is how each
scales: every velocity-side term (the trim residual, the release kick, the tube cant, the frozen
gravity) falls with the sensitivity, so by **8x**; every surface-side term falls with `cot γ`, so by
**2.8x**; and the aim correction's own frozen residue — the largest single term at 760 m — is the
loop taking out a drag loss that is 13.4 km at 7.5° and 0.3 km at 15°, so it should very nearly
vanish. A few hundred metres becoming a few tens is the shape to expect, and it has to be flown.

**Steeper than 20° only if the rod is normal-component limited.** 45° gives 2,204 m/s square to the
ground against 1,410 at 20°, for 2.7 km/s and two thirds of the reach. That is a penetration
decision, not a precision one — precision past 20° is nearly free of charge and nearly free of
benefit.

**Do not fly the vertical drop.** It is the most accurate row on the page and it arrives at 2,414 m/s
with 8.4% of the stack, directly under the platform. Everything a rod is for is in the orbital
velocity it throws away to get there.

---

## What is not settled

- **Only the floor has been flown, twice.** Everything else here is measured through the real
  solver, the real drag model and the real `ImpactPredictor`, on a planet that sits at the origin —
  which is exactly the case a frame carrier cannot show up in.
- **The drag model itself is exponential and unvalidated.** The 10% column prices an error in it; it
  does not say the model has one. What it does say is that at seven degrees the question matters and
  at fifteen it does not, which is a reason to move rather than a reason to measure.
- **The sea clamp is what the last column of that table used to cost.** All three surfaces now go
  through `GroundSurface.Height`, so it is taken out rather than steepened away. What is left is the
  wave displacement KSA's own physics uses and the mod does not — metres, which is tens of metres of
  ground at seven degrees.
- **The floor is the Mk 21's.** A dedicated rod would want its own `MunitionProfile`, and a denser one
  moves the floor the wrong way — see the table at the top.
- **What a constrained arc does to the aim-correction loop is the thing a flight had to show**, and
  it did: *Flown*, below. It is also where the remaining misses come from — `docs/MIRV-NEXT.md`
  item 9.


## Flown: the floor works and the coast correction will not fly it

Two shots at `MinArrivalAngleDeg = 15`, against the same pick-up every other measurement here uses.

| | miss | spread |
| --- | --- | --- |
| floor off | 0.38 / 0.96 / 1.47 km | 0.07 / 0.08 / 0.14 km |
| **floor 15 deg** | **2.28 / 2.68 km** | **0.03 / 0.00 km** |

**The search half works.** The shot flies the constrained arc, reports `reach Reachable`, and cuts
off **0.01 m/s short** — the burn ends on its solution. And the group is the tightest ever measured
here: 0.00 km of spread on the second shot, six warheads on one point, which is what a steep arrival
is supposed to buy.

**The coast half refuses.** `BusTrim` reports

```
more than a separation could have cost, 10.97 m/s left on the bus
  -- owed 0.01 m/s at the split, 1.11 m/s on release
```

so the post-boost correction is asking for **eleven metres a second** on a bus that separated owing
one hundredth of one. That trips `BusTrim.MaxMetresPerSecond`, the guard that exists to catch a
runaway, and the trim declines entirely — the lateral jets added for exactly this are never fired.

**What it is not.** Not authority: the jets were never asked. Not the separation: 0.01 m/s. Not the
burn: 0.01 m/s short of its own solution.


### It is the aim correction, and it is not a price

Both explanations written down when this was first flown are wrong, and each was checked.

**Not the coast re-solve.** `IcbmProgram.ResolveCoastArc` goes through `BallisticArc.TrySolve`, which
takes no floor — because there is none to take: the bound lives in the search over flight *time*, and
Lambert between two points in a stated time is unique. Solving one cutoff state both ways and
differencing the required velocities gives **0.0 m/s** at every floor from 10° to 20°.

**And not the arrival angle.** `dMiss/dV` in the table above is measured with the trajectory
*free*, which is a different question from what a coast correction faces: `ResolveCoastArc` pins the
arrival, and at a pinned arrival one kilometre of aim costs **2.145 m/s at 3.6° and 1.468 at 20°** —
slightly *cheaper* the steeper it comes in. So the trim inherits the benefit rather than paying eight
times for it, and eleven metres a second is about seven kilometres of aim at the ordinary rate.

**Those seven kilometres are the aim correction's, and the shot never needed them.** The loop exists
to remove the drag shortfall between the point the arc was solved to and where a warhead flying it
lands, and that is exactly what a steep arrival abolishes — 22 km on the graze, **0.11 km** at
fifteen degrees, which is this page's own prediction arriving. Flown headlessly at 3,459 km with a
15° floor: **0.003 km uncorrected against 9.98 km corrected**, on a shot whose burn ends 0.01 m/s
short of its own solution.

**What it opens on instead is the trajectory search.** The instant the guidance expects to cut off at
is revised by under a second per cycle with no floor set, and by **145 seconds** under one. Per
prediction cycle at 3,459 km, 15° floor:

```
t      flight   arrival   cutoff revised   shortfall   bias    reported miss
2.00   602 s    46.04°           --         10.90 km   0.00 km   10.90 km
2.51   335 s    15.00°       144.80 s       24.70 km   2.72 km   21.97 km
3.03   360 s    15.00°         2.23 s       12.99 km   8.22 km    4.77 km   <- banked as the best
3.55   398 s    11.54°        43.92 s        0.22 km   8.22 km    8.43 km   <- arrival latched, aim frozen
4.07   397 s    11.51°         0.06 s        0.11 km   8.22 km    8.32 km
```

The first three readings are of three different shots. By the fourth the search has settled and the
thing being corrected has gone — and 8.22 km of bias, built to cancel a 12.99 km shortfall that no
longer exists, is frozen onto a shot that needed nothing. `AimCorrection.IsSteady` is a step-size
test, so a small step between two large ones reads as convergence; that commits the arrival, and the
freeze follows it.

With no floor the same trace opens on a shortfall of 22.75 km that is still 21.96 km six cycles
later. That is a real, steady, physical quantity, and the loop converges on it in four cycles. **The
loop is not unstable and its gain is not wrong. Its signal goes to nothing under a floor while its
noise does not.**

### What is not being done about it, and why

Two changes fix it headlessly and neither is shipped.

| | 2,000 km | 3,459 km | 5,000 km | 7,645 km |
| --- | --- | --- | --- | --- |
| shipped, floor 15° | 0.06 | **9.98** | **10.49** | 0.52 |
| correction off | 0.10 | 0.00 | 0.56 | 0.47 |
| correction run to cutoff | 0.02 | 0.28 | 0.11 | 0.09 |
| shipped, floor off | 0.01 | 0.38 | 1.16 | 1.15 |
| correction off, floor off | 0.01 | **19.56** | **64.48** | **186.13** |

Letting the loop run to cutoff wins **all twenty** cells of that sweep, worst case 0.56 km against
20.83. It is also exactly the change `docs/MIRV-NEXT.md` item -1 records being flown eleven times and
losing — 1.29 km median against 0.85, with three shots past 5 km. Turning the correction off under a
floor scores nearly as well and is a switch rather than a fix; the bottom row is why it cannot simply
be removed.

**And the rig has a new reason to be disbelieved here, which is the durable finding.** The
correction's only observer is `ImpactPredictor`, and every headless rig flies it over a **mean
sphere** — so the observer is noiseless, and any change that lets the loop run longer is free
averaging of a clean signal. `DeorbitShot.RoughGround` gives the predictor relief and the height
field's own quantum to cross, and the *shipped* configuration with the floor off moves by kilometres
at ranges where the smooth rig reports tens of metres:

| floor off, as shipped | 2,000 km | 3,459 km | 7,645 km |
| --- | --- | --- | --- |
| mean sphere | 0.01 km | 0.38 km | 1.15 km |
| with relief | **4.31 km** | 1.35 km | **3.82 km** |

Five of the seven refused changes made the loop act more on its own prediction. That is the shape of
change a smooth planet cannot price, and `ArrivalFloorFlightTests.GroundUnderTheObserverMovesTheShippedShot`
is there so the next attempt starts from a rig that can see it.

**So the floor half is fixed and the correction half is only written down.** What ships is the bound
surviving the latch — gated on the bound, unreachable by default. Anything touching the correction
wants flights, and `docs/MIRV-NEXT.md` item 7d says six of them settle a change worth a kilometre and
settle nothing worth two hundred metres.
