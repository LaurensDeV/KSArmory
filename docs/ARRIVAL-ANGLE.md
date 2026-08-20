# What arrival angle buys

**The question.** A kinetic round has no lethal radius to hide inside, so precision *is* the weapon.
Every ballistic shot this mod has flown arrives at about **seven degrees**, and every large term in
the miss budget is that angle multiplied by something. This is what a steeper arrival is worth, what
it costs, and whether the guidance can be told to fly one.

**Everything below is measured** by `tests/KSArmory.Tests/ArrivalAngleTests.cs` and nothing has been
flown. `docs/ICBM-GUIDANCE.md` is the guidance itself; `docs/KSA-TERRAIN.md` is where the surface
numbers come from.

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
| **surface** — the height quantum, the held terrain sample, the sea-clamp defect, any height-shaped epoch fault | `cot γ` 7.60 → 2.75, a factor of **2.8** |
| **the drag model** — the part no correction loop can remove, because its only observer shares the model | 1,795 m → 29 m, a factor of **62** |

The third one is the surprise and it is the biggest. A correction loop can only remove what its
observer can see, and the observer is `ImpactPredictor` running the round's own `Medium.Drag`. So
whatever that model has wrong survives the loop intact. At 7.5° the drag is worth 13.4 km of range
and a ten per cent error in it is **1.8 km on the ground** — larger than the entire flown miss. At 15°
the drag is worth 0.3 km and the same error is 77 m. At 20° it is 29 m and stops being a term at all.

The surface terms, from `docs/KSA-TERRAIN.md` put through `cot γ`:

| arrival | ground per m of height | the field's own 0.2985 m quantum | one held terrain sample, 5% slope | the mean sea depth the clamp is missing |
| --- | --- | --- | --- | --- |
| 7° | 8.14 m | 2.43 m | 8.68 m | 30.8 km |
| 15° | 3.73 m | 1.11 m | 4.72 m | 14.1 km |
| 20° | 2.75 m | 0.82 m | 3.62 m | 10.4 km |
| 30° | 1.73 m | 0.52 m | 2.39 m | 6.5 km |
| 60° | 0.58 m | 0.17 m | 0.84 m | 2.2 km |

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

## Can the guidance be told to arrive steeply today?

**No, and the one knob that looks like it can will sometimes do the opposite.**

There is no arrival-angle parameter anywhere in `Sim/`. `BallisticArc.TryCheapest` minimises the
length of the velocity still to gain over flight time, `BurnWindow.TryFind` minimises the same thing
over departure time as well, and `IcbmConfig.Loft` multiplies the flight time the first of those
settled on. Arrival angle is an output of all three.

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

### What would express it

The machinery is already there and the objective is wrong. `BurnWindow` coasts the state through
`Kepler.TryCoast` and costs departures across sixteen revolutions — everything needed to wait for the
pass a steep shot requires. What is missing is a term that is not `|Δv|`: a *minimum arrival angle*
that constrains `BallisticArc`'s flight-time search, and a window search that takes the earliest
departure whose cheapest satisfying arc is affordable rather than the cheapest arc there is.

That is a behaviour change and is not made here.

---

## Recommendation

**Fly a 15–20 degree arrival, deorbited by braking hard onto a target 2,000–2,700 km ahead. Do not
loft.**

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

- **None of it is flown.** The measurements are through the real solver, the real drag model and the
  real `ImpactPredictor`, on a planet that sits at the origin.
- **The drag model itself is exponential and unvalidated.** The 10% column prices an error in it; it
  does not say the model has one. What it does say is that at seven degrees the question matters and
  at fifteen it does not, which is a reason to move rather than a reason to measure.
- **The sea-clamp defect in `docs/KSA-TERRAIN.md` is not fixed and is worth 30.8 km at seven degrees**
  against 10.4 at twenty. Steepening reduces it by a factor of three; it does not remove it.
- **The floor is the Mk 21's.** A dedicated rod would want its own `MunitionProfile`, and a denser one
  moves the floor the wrong way — see the table at the top.
