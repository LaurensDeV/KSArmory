# How accurate a kinetic round could possibly be

Kinetic bombardment — a dense rod, no explosive, metre-level precision — is bounded by things this
mod cannot tune: an integrator's own truncation, an engine constant, a texture's bit depth, a pixel.
This file is that budget. It is **not** a plan, and it is not the same question as
`docs/MIRV-NEXT.md`, which prices what the shot costs *today* and what to fix next. Everything here
would still be there after every item on that list had landed.

Every number is either measured by `tests/KSArmory.Tests/KineticFloorTests.cs` or cited into the
decompiled corpus at `../ksa-game-assemblies/current/src` against build **2026.8.22.5348**. Where a
term could not be measured it says so rather than being estimated.

**The headline.** The single biggest lever is not a constant — it is the **arrival angle**. Five of
the eight real terms are heights, and a height becomes ground in proportion to `cot(gamma)`. A
7-degree deorbit multiplies every one of them by eight; a vertical drop multiplies them by nothing.
The mod's flown shot arrives at 7.1 degrees, which is the worst geometry available.

---

## The budget

Two columns of numbers, because the terms behave differently: what one costs on the **7.1-degree**
arrival the mod flies today, and on an **88-degree** one — the same 200 km pickup with the whole
orbital velocity taken out, which is what a rod actually is.

| limit | at 7.1 deg | at 88 deg | what sets it | reducible? |
| --- | --- | --- | --- | --- |
| **the round's integrator** | **82–153 m** | **1.1 m** | `Interceptor.SubStep` = 5 ms, symplectic Euler in `Sim/Slug.cs` | yes, linear in the step — 10x the sub-steps for 10x the accuracy |
| **the ground sampled once a frame** | 10–39 m | 0.2–0.8 m | `Slug.Update` calls `IGroundTest` before the sub-step loop | yes, at one height query per sub-step |
| **the height field's own quantum** | 2.40 m | 0.01 m | `R16_UNORM` over 19,561 m = 0.2985 m | **no** — it is the shipped texture |
| **the predictor's ground crossing** | 2.01 m | 0.01 m | `ImpactPredictor.CrossingToleranceMetres` = 0.25 m | yes, at more bisection steps |
| **the float terrain staircase** | ≤ 0.9 m | ≤ 0.02 m | `Celestial.cs:833` packs the direction to `float3` | **no** — inside the engine |
| **the ecliptic's own arithmetic** | 1.8 mm | 1.8 mm | `double3` at 1.5e11 m; ulp 30.5 um | no, and it does not matter |
| **the engine clock** | ≤ 104 mm | ≤ 104 mm | `UniverseTime` is Int128 ns; `SimStep.DeltaTime` is the unrounded double | no, and it does not matter |
| **naming the target** | 0.2 m – 1.9 km | same | one pixel of the player's viewport, through `SiteDesignator` | yes — zoom, or a coordinate entry that does not exist |
| **the terrain's own gain** | 1.2x to unbounded | 1.02–1.12x | `slope / tan(gamma)`, a fixed point with no solution above 1 | only by arriving steeply |
| *(the harness's ruler)* | *13.4 cm* | *13.4 cm* | `R * Vec.AngleBetween` is an arc cosine | *n/a — a floor on the measurement, not the shot* |

Everything below is that table with its evidence.

---

## 1. The round's own integrator — the largest real term

`Sim/Slug.cs` integrates with **symplectic Euler** at `Interceptor.SubStep` (5 ms), holding gravity
and the air's motion across the sub-steps of one frame. `Sim/ImpactPredictor.cs` is **RK4** and
re-evaluates gravity at each stage, so the two are different schemes rather than the same one at
different steps — the gap between them does not close as the *predictor* gets finer, only as the
round does.

Handing `Slug.Update` a step under 5 ms *is* the round with a finer sub-step, because
`steps = ceil(dt / SubStep)` bottoms out at one. So the sweep needs no production change.

On the 3,459 km deorbit every budget in this repository is measured on
(`KineticFloorTests.TheSubStepIsTheWholeOfTheIntegratorsOwnError`):

| sub-step | from a 0.25 ms reference |
| --- | --- |
| 5.00 ms | 145.3 m |
| 2.50 ms | 68.8 m |
| 1.00 ms | 22.9 m |
| 0.50 ms | 7.6 m |

**First order and clean: 30.6 m per millisecond of step, on every row.** So the shipped 5 ms is
**153 m** from a converged flight of the same round, and the extrapolation is a fit rather than a
guess.

### It is a wrong place, not a wrong time — and that is why the angle helps

A 5 ms round arrives 20.6 ms early. The ground turns 9.6 m under it in that, against a 145 m miss,
so **93% of the term is a genuinely different trajectory** rather than a timing error
(`WhetherTheIntegratorsErrorIsAPlaceOrATime`). A trajectory error is mostly along the flight path,
and how much of that becomes ground is the arrival angle.

Flown whole, from the same 200 km pickup, with the deorbit burn deciding the angle
(`TheWholeShotAtTheAngleTheDeorbitBurnLeavesIt`):

| deorbit burn | downrange | arrival | flight | 5 ms against 0.25 ms | of which the ground's turn |
| --- | --- | --- | --- | --- | --- |
| **0% of circular** | 96 km | **88.2 deg** | 208 s | **1.11 m** | 1.10 m |
| 10% | 64 km | 66.1 deg | 209 s | 0.69 m | 1.11 m |
| 30% | 401 km | 36.9 deg | 218 s | 4.72 m | 1.20 m |
| 60% | 1,078 km | 17.6 deg | 261 s | 15.25 m | 1.67 m |
| 90% | 3,094 km | 7.8 deg | 489 s | 81.66 m | 5.65 m |

**At a vertical arrival the whole term collapses onto the timing residue** — 1.11 m of miss against
1.10 m of ground rotation under a 2.4 ms arrival error. There is nothing else left. And that
residue is first order in the step too, so a 1 ms sub-step takes it to about 0.2 m.

### What a finer sub-step costs

The step is shared by every round in the air, which is the whole of the objection to shrinking it
(`WhatAFinerSubStepWouldCost`). Per round per frame, at the 50 ms the world is already held to in
air by `Medium.FaithfulStepInAir`:

| sub-step | per round per frame | six warheads | whole 497 s flight |
| --- | --- | --- | --- |
| 5.0 ms | 10 | 60 | 99,305 |
| 1.0 ms | 50 | 300 | 496,525 |
| 0.5 ms | 100 | 600 | 993,050 |
| 0.1 ms | 500 | 3,000 | 4,965,251 |

Three hundred vector updates a frame for a six-warhead group is not a lot. **A hundred and fifty
CIWS shells at 1 ms is 7,500**, which is the case that would have to be measured before the constant
moved — and `Sim/MunitionProfile.cs` carries `SubStepSeconds`, so the per-round step exists and
nothing in the arsenal asks for one.

The 64-sub-step clamp never binds on entry: it caps one `Update` at 0.32 s, which is six times the
step `WarpPolicy` already holds the world to once there is air.

---

## 2. The ground, sampled once a frame and held as a sphere

`Slug.Update` asks `IGroundTest.TryGround` **before** the sub-step loop and holds the answer — a
centre and a radius — for the whole frame. That is deliberate and documented: the terrain query is
the expensive call. The consequence is that the round stops on the height that was under it at the
*top* of the frame, and over sloping ground that is stale by the track it covered.

With slope `s`, arrival `gamma` and a frame's ground track `d`, the stopping error is
`s.d / (tan gamma + s)`. Measured through that at 2,713 m/s
(`TheGroundSphereIsSampledOnceAFrameAndHeldFlat`):

| frame | arrival | 1% slope | 5% slope | 20% slope |
| --- | --- | --- | --- | --- |
| 50 ms (`FaithfulStepInAir`) | 7.1 deg | 10.0 m | 38.6 m | 83.0 m |
| 50 ms | 30 deg | 2.0 m | 9.4 m | 30.2 m |
| 50 ms | 70 deg | 0.17 m | 0.83 m | 3.15 m |
| 17 ms | 7.1 deg | 3.3 m | 12.9 m | 27.7 m |
| 320 ms | 7.1 deg | 64.0 m | 246.8 m | 530.9 m |

Note the sphere itself is **concentric with the body**, so planetary curvature is exact and only the
slope term is left. Reducible by re-sampling per sub-step, which costs a bicubic plus the whole
modifier stack ten times a frame per round instead of once.

**Measured end to end it is smaller still**, because the table above prices the *stopping* error
against ground the round has already reached rather than the whole flight: flown through
`ProbeGapTests`, re-sampling per sub-step moves the impact 2 m at 50 ms and 22 m at 320 ms over
`DeorbitShot.RoughGround`. That relief is about 1%, so read it against the first column.

---

## 3. The two fixed vertical quanta

Both are heights, so both are `cot(gamma)` of ground
(`TheTwoFixedVerticalQuantaAreGroundInProportionToCotangentGamma`).

**The height field's bit depth** is `R16_UNORM` over a declared -10.930 km to 8.631 km, which is
**0.2985 m per level** — `docs/KSA-TERRAIN.md` has the derivation off the shipped
`Earth_Height.ktx2`. It cannot be argued down: it is what RocketWerkz shipped.

**The predictor's crossing tolerance** is `ImpactPredictor.CrossingToleranceMetres` = 0.25 m, a
deliberate bias rather than a symmetric error — the bisection stops on how deep the answer is, and
the answer is always below the surface, so always downrange. `ErrorBudgetTests` measures it at 18 cm
actual on the flown arrival.

| arrival | m of ground per m of height | quantum | crossing | both |
| --- | --- | --- | --- | --- |
| 5 deg | 11.43 | 3.41 m | 2.86 m | 6.27 m |
| **7.1 deg** | 8.03 | 2.40 m | 2.01 m | **4.40 m** |
| 15 deg | 3.73 | 1.11 m | 0.93 m | 2.05 m |
| 30 deg | 1.73 | 0.52 m | 0.43 m | 0.95 m |
| 45 deg | 1.00 | 0.30 m | 0.25 m | 0.55 m |
| 80 deg | 0.18 | 0.05 m | 0.04 m | 0.10 m |
| **90 deg** | 0.00 | **0** | **0** | **0** |

---

## 4. The engine evaluates procedural terrain on a `float` direction

**Not previously recorded anywhere.** `Celestial.GetTerrainHeightFromDirCcf` computes the base
bicubic in `double` — exactly, from 16-bit integer texel samples — and then, on a body that has both
a normal cubemap and biome materials, packs the direction down:

```csharp
float3 float6 = float3.Pack(in vector);        // Celestial.cs:833
...
float heightKm = (float)num2;                  // Celestial.cs:842
modData = new ... { Position = float6, TextureNormal = float6, ... };
```

Earth has both, so Earth takes that path. Every procedural modifier — erosion, four tiling details,
dunes — is therefore evaluated on a single-precision unit vector.

A `float` unit vector's neighbours are `2^-24` to `2^-23` apart, which on Earth is **0.38 m to
0.76 m of ground**. Measured by walking a great circle and counting distinct packed directions
(`TheProceduralTerrainIsEvaluatedOnAFloatDirection`): **0.31 m per tread** at a 0.1 m walk, 0.38 m
at 0.25 m, and a worst displacement of **0.307 m** over 20,000 random directions.

So below about a third of a metre the modifier stack returns one value: **the surface is a
staircase**. It is deterministic and identical for every caller, so it does not bias a shot — the
round, the prediction and the aim point all read the same treads. What it does is put a hard floor
under how finely the surface can be *asked about*, and turn any residual miss into a height jump of
`tread x local slope`.

Nothing in this mod can reach it.

### What is between the samples, and whether it is ground

The user-facing version of the question. On Earth, in order
(`WhereTheProceduralTerrainRunsOutOfDetail`):

| scale | what shapes it |
| --- | --- |
| coarser than **3,111 m** | the R16 base cubemap — real elevation data, Catmull-Rom between texels |
| 3,111 m down to **166 m** | `EarthErosion`: 7 octaves, lacunarity 2, gain 0.5, from 10.6 km to 166 m of wavelength |
| 166 m down to **7–19 m** | the four `TilingDetail` modifiers — 4096-square `R16` textures, 7.4 m/texel (Alpine) to 20.5 m/texel (Desert Mountains) |
| **10 m down to 0.38 m** | nothing. A bilinear ramp between two texels of a detail texture: locally a tilted plane |
| below **0.38 m** | the float staircase above |

**At a rod's own scale the ground is a tilted plane with third-of-a-metre treads.** That is not a
defect — it is simply what the answer is, and it is the same answer for everything that asks.

---

## 5. The terrain amplifies whatever is left, and on a shallow arrival without bound

The terrain adds no bias of its own: `docs/KSA-TERRAIN.md` establishes that the aim point's
placement and the round's stopping surface are an exact round trip through the same field. What it
does is **multiply**.

A residual miss `d` lands on ground `s.d` higher or lower than the aim point, which the arrival angle
turns back into `s.d / tan(gamma)` of further miss. The loop's gain is `s / tan(gamma)`
(`TheTerrainAmplifiesWhateverMissIsLeftAndAtAShallowArrivalWithoutBound`):

| arrival | 2% slope | 5% | 10% | 30% |
| --- | --- | --- | --- | --- |
| 6 deg | 1.24x | 1.91x | **20.59x** | **unbounded** |
| 15 deg | 1.08x | 1.23x | 1.60x | **unbounded** |
| 30 deg | 1.04x | 1.09x | 1.21x | 2.08x |
| 45 deg | 1.02x | 1.05x | 1.11x | 1.43x |
| 70 deg | 1.01x | 1.02x | 1.04x | 1.12x |

At gain one or above **there is no fixed point at all** — where the round stops is decided by the
terrain rather than by the shot, and re-aiming walks away from the answer instead of onto it. This is
the same failure, with the same gain, as the cursor's ground-point iteration in
`KsaWorld.TryCursorGroundPoint`, which guards against it by keeping the better sample and breaking.

Whether Earth's terrain is actually that steep at the metre scale is **not measured here** — see
[What could not be measured](#what-could-not-be-measured).

---

## 6. Two candidates that turned out to be worth nothing

Both were worth checking and both are now closed.

**Floating point in the ecliptic.** A `double` at 1 AU has a spacing of **30.5 um**, and a round's
`PositionEcl` is exactly that: every sub-step adds tens of metres to a number of 1.5e11, and every
ground test subtracts two such numbers. Flown identically with the planet moved out and left still
(`TheEclipticIsWhereAKineticRoundKeepsItsPosition`):

| planet at | miss against the origin flight |
| --- | --- |
| 1e9 m | 0.042 mm |
| 1 AU | **1.803 mm** |
| Saturn's 1.43e12 m | 224 mm |

Two millimetres at Earth. It becomes a real term past Jupiter and nowhere nearer. The mod's own
ballistic maths already works in `Cci` (`Sim/BallisticBody.cs`), where the carrier is subtracted;
only the round itself is in `Ecl`, and this is the price of that.

CLAUDE.md's note about part transforms packed to `float` at Ego magnitude is **drawing only** —
confirmed: `Slug`, `Interceptor` and `ImpactPredictor` are `double3` throughout, and nothing in the
round's integration reads a drawn transform back.

**The engine clock.** `UniverseTime` is `Int128` **nanoseconds** (`KSA/UniverseTime.cs`), and
`UniverseTime operator +(UniverseTime, double)` rounds to a whole nanosecond — while
`SimStep.DeltaTime`, which is what `KsaWorld.ConsumeSimStep` hands the mod, is the *unrounded*
double that was added. So the mod integrates by one number and the world advanced by the other, by
up to half a nanosecond per frame. Over the 29,792 frames of a 497 s flight
(`TheEngineClockIsQuantisedToTheNanosecond`): **104 mm** if every frame rounded the same way,
**0.6 mm** as a random walk. And `GetElapsedSeconds()`'s own decay is 26 um at a year of universe
time, 3.3 mm at a century.

Both are ruled out for Earth.

---

## 7. Naming the target is a floor nothing downstream can beat

`Ksa/SiteDesignator.cs` takes a click, so an aim point begins as **one pixel** resolved against the
height field by `KsaWorld.TryCursorGroundPoint`. The angle a pixel subtends times the range is a
length across the line of sight, and the depression angle turns that into ground
(`WhatOnePixelOfDesignationIsWorthOnTheGround`, at 1080 lines):

| field | per pixel | 2 km at 30 deg | 20 km at 20 deg | 200 km straight down | 2,000 km straight down |
| --- | --- | --- | --- | --- | --- |
| 60 deg (unzoomed) | 970 urad | 3.9 m | 56.7 m | **194 m** | 1.94 km |
| 15 deg (4x) | 242 urad | 1.0 m | 14.2 m | 48.5 m | 485 m |
| 3 deg (20x) | 48.5 urad | 0.2 m | 2.8 m | 9.7 m | 97 m |

**Designating an intercontinental target from orbit at the default field costs more than every other
term in this file put together.** The sight's magnification is the lever, and `SightZoom` already
reaches 20x.

Two things that are *not* floors, checked and cleared:

- `Sim/AimSite.cs` stores latitude and longitude as `double`. `Describe()` prints three decimal
  places — 0.001 degrees of latitude is 111 m — but that is display only and is never read back.
- The aim point is **not persisted at all**: `Sim/IcbmConfig.cs` carries no site, so there is no
  serialisation to quantise it.

The refinement loop in `TryCursorGroundPoint` has its own limit, and it is item 5's gain by another
name: its fixed-point iteration converges only while `slope / tan(depression) < 1`, which from
ground level is most of the screen. It keeps the better sample and stops, so it degrades rather than
diverging — but a click at a shallow depression angle onto rough ground is not resolved to the
pixel it was worth.

---

## 8. Terminal guidance has no floor of its own

`Sim/Slug.cs` already carries `GuidanceMode.Inertial` — the same proportional navigation the bomb
uses, at whatever lateral limit the profile declares. Flown from a 30-degree arrival with the
release deliberately displaced across the track
(`WhatATerminallyGuidedRoundCanStillTakeOut`):

| displaced | 0.5 g | 2 g | 6 g | 20 g | unguided |
| --- | --- | --- | --- | --- | --- |
| 500 m | 411 m | **2.32 m** | 2.32 m | 2.32 m | 500 m |
| 5 km | 4,291 m | 2,218 m | **2.33 m** | 2.33 m | 4,999 m |
| 50 km | 49,268 m | 47,102 m | 41,307 m | 19,260 m | 49,990 m |

So: **2 g clears half a kilometre, 6 g clears five, and 20 g cannot clear fifty.** Terminal guidance
is a way of removing the guidance budget, not the flight-time budget.

The couple of metres it settles on is **entirely the step**, not the law
(`WhereTheSteeredRoundsLastFewMetresComeFrom`):

| nav constant | 5 ms | 1 ms | 0.25 ms |
| --- | --- | --- | --- |
| 3 | 2.32 m | 0.47 m | 0.09 m |
| 4 | 2.32 m | 0.47 m | 0.09 m |
| 6 | 2.32 m | 0.46 m | 0.09 m |

First order in the sub-step at 0.465 m per ms, and flat in the nav constant. **Proportional
navigation contributes nothing of its own** — a steered round is bounded by the integrator, by where
it thinks the target is, and by the same ground model as an unguided one, and by nothing else.

### And it removes the arrival angle, which is the whole of the unguided budget

The headline of this file is that five of the eight terms are heights and `cot(gamma)` turns them
into ground, so an unguided round cannot be precise at seven degrees at any price.
**That does not survive terminal guidance** (`WhetherTerminalGuidanceRemovesTheNeedForASteepArrival`,
a 500 m release error steered out at each arrival):

| arrival | `cot γ` | unguided | 2 g at 5 ms | at 1 ms | at 0.25 ms |
| --- | --- | --- | --- | --- | --- |
| **7.1 deg** | 8.03 | 498.51 m | **2.32 m** | 0.45 m | 0.00 m |
| 15 deg | 3.73 | 499.60 m | 2.32 m | 0.46 m | 0.09 m |
| 30 deg | 1.73 | 499.89 m | 2.32 m | 0.47 m | 0.09 m |
| 60 deg | 0.58 | 499.96 m | 2.33 m | 0.47 m | 0.13 m |

**The arrival angle is worth a couple of centimetres to a steered round and a factor of eight to a
ballistic one.** The reason is what `cot(gamma)` actually multiplies: errors the round *cannot see*.
A ballistic round stops wherever its arc happens to cross the ground, so every height error upstream
becomes range; a round steering at the target flies at the target from whatever direction it
arrives, and what is left is the sub-step alone. Two g is enough for 500 m and 6 and 20 are
identical to it.

**One term is absent from that table and is the reason not to read it as "arrival angle no longer
matters".** The ground in this rig is a smooth ball, so the terrain gain of section 5 —
`slope / tan(gamma)`, which passes one and stops having a fixed point below about fifteen degrees —
is not in these numbers. It *multiplies* whatever is left rather than adding to it, so it costs a
0.46 m residue far less than a 153 m one; but it is the one reason left to prefer a steeper arrival
for a round that steers.

Nothing in `Sim/Arsenal.cs` is such a round today: the Mk 21 is `GuidanceMode.None`.

---

## The two numbers

Both assume every *reducible* guidance term has already been driven to zero — the aim correction's
frozen residue (760 m), the tube cant (233 m), the frozen gravity (284 m) and the cutoff residual
(38 m) are all in `docs/MIRV-NEXT.md` and are engineering rather than floor. What is left is this
file.

### An unguided kinetic round

**About 1.2 m, on a near-vertical arrival, as the mod ships.**

Root-sum-square at 88 degrees, a 17 ms frame, 5% ground slope and an exactly-known aim point:

| | |
| --- | --- |
| integrator at 5 ms | 1.11 m |
| ground held for a frame | 0.28 m |
| height quantum + crossing tolerance | 0.02 m |
| float terrain staircase | ≤ 0.02 m |
| ecliptic doubles, engine clock | 0.002 m |
| **root sum square** | **≈ 1.2 m** |

Take `Interceptor.SubStep` to 1 ms and it becomes **≈ 0.4 m**; to 0.25 ms, **≈ 0.3 m**, at which
point the frame-held ground sample and the float staircase are the whole of it and further
integration accuracy buys nothing.

**On the 7.1-degree arrival the mod actually flies, the same budget is ≈ 160 m** — 153 m of
integrator, 4.4 m of quanta, 10–39 m of frame-held ground, the whole lot multiplied by a terrain
gain of 1.2x to unbounded. That is not a precision weapon and no amount of guidance work makes it
one; the geometry has to change.

### A terminally guided kinetic round

**About 2.3 m as the flight model stands, and about 0.5 m at a 1 ms sub-step** — provided it has at
least 2 g of authority and is told within a few hundred metres where to go.

It is bounded by exactly the same integrator and the same ground model as the unguided round, and it
buys nothing at all against them. What it buys is immunity to everything *upstream*: the cant, the
cutoff residual, the aim residue and a bad release all become someone else's problem the moment the
round can pull 2 g in the last twenty seconds. That is the case for building one — not accuracy at
the floor, but reaching the floor at all from a shot that would otherwise be a kilometre out.

**And it is the cheap route to a metre, because it does not need the trajectory changed.** The
unguided path to 1.2 m is a near-vertical arrival, which from orbit means taking out essentially the
whole orbital velocity — about 7.8 km/s, with downrange collapsing from 3,094 km to 96. A steered
round reaches 0.46 m on the seven-degree arrival the mod already flies, for a tail kit and a finer
sub-step. The propellant a steep arrival costs buys accuracy only for a round that cannot steer.

### The honest caveats on both

- **Neither is a CEP in the statistical sense.** Every term here is a deterministic bias of a
  deterministic simulation: the same shot flown twice gives the same answer to the bit. What makes
  a flown group scatter is the frozen gravity, whose error grows with the step — but the step's own
  run-to-run variation is **not** pacing. It is a single latched `WarpPolicy` decision that either
  happens or does not, worth 0.7-2.6 km; `docs/MIRV-NEXT.md` item 7e has the measurement and
  conditioning on the coast step leaves 10-88 m.
- **Nothing here is flown.** It is all headless, and the rig's planet is at the origin.
- **The 13 cm ruler.** Every budget in this repository scores a miss with
  `R * Vec.AngleBetween`, an arc cosine, whose resolution at Earth's radius is **13.4 cm**
  (`TheHarnessCannotScoreAMissUnderThirteenCentimetres`). No measurement taken through it can report
  a smaller miss than that, whatever the round did. The sub-metre numbers above are near it and
  anything under about 30 cm should be read as "below the ruler".

---

## What could not be measured

- **The real slope of Earth's terrain at the metre scale.** Item 5's gain is the difference between
  a 1.2x multiplier and no fixed point at all, and it turns on a number this rig cannot get.
  `EarthErosion`'s declared parameters give each octave a slope of up to **0.30** undamped, seven
  octaves of it — but `ErosionModifierReference.Evaluate` multiplies every octave by the biome
  weight, by a gradient-falloff power of the angle between texture and surface normal, and by
  `1 - |dot|` of those two again, all of which are near zero over flat ground. What the product
  actually is needs the game. The arithmetic is in `WhereTheProceduralTerrainRunsOutOfDetail` and is
  labelled an upper bound.
- **Anything with a frame carrier.** The rig's planet is at the origin and does not move, which is
  the one case where an epoch fault is identically zero. `docs/MIRV-NEXT.md` item 2 is the standing
  example of a term that measures one way headlessly and the other way in flight.
- **The per-frame cost of a finer sub-step.** Counted in sub-steps, never timed in a frame. The
  CIWS case — 150 shells — is the one that decides whether the constant can move, and it has never
  been profiled.
- **Whether the aim point and the round ever land in different height-field neighbourhoods.** The
  round trip is exact for *one* direction; the quantum in the table above is what a disagreement
  costs, not a measurement that one occurs.
- **The ocean.** `GroundTest`, `IcbmComputer.TerrainRadiusAt` and the aim point all clamp to the
  waterline through `GroundSurface.Height` — `docs/KSA-TERRAIN.md` has the three call sites. While
  they did not it was worth 35 km of ground at the flown arrival, which was a defect rather than a
  floor, and it is excluded here.
- **The designation numbers assume 1080 lines.** A different render or display scale moves them in
  proportion, and `Sim/CursorAim.cs` already exists because the viewport and the framebuffer need
  not be the same size.


## Flown: the integrator term is real and below what a shot can resolve

`MunitionProfile.SubStepSeconds` was set to 1 ms on the Mk 21 and flown four times against four at
the shared 5 ms, same pick-up:

| | shots | mean |
| --- | --- | --- |
| 5 ms (shipped) | 0.42 / 0.49 / 0.82 / 2.10 km | 0.96 km |
| 1 ms | 0.13 / 2.02 / 1.28 / 2.75 km | 1.55 km |

**No effect visible, and none should have been.** The headless figure is 30.6 m per millisecond, so
5 ms → 1 ms is about **122 m** — against run-to-run scatter of roughly **±700 m** on an identical
pick-up. The term is real, first-order and cleanly measured; it is simply five times under the
noise floor of the only instrument that can confirm it.

**So it is not enabled**, and the reason is the trade rather than the term: it multiplies the
integration work per round by five, that wall-clock cost has never been timed, and `CLAUDE.md`'s
rule about unmeasured per-frame costs applies. Paying an unmeasured cost for an unmeasurable gain is
the wrong way round.

**When to turn it on.** That condition is now met on one half and refused on the other, so read
both. The noise floor came down with the miss — a 6-against-6 batch on 2026-08-24 read a 0.05 km
median with a 0.02-0.09 km range, and the rank test settles ratios rather than metres, so 122 m
against a 50 m median is no longer under the instrument. The wall-clock cost is measured too, at
0.3 ms a frame for a six-warhead group.

**But the rig now argues against it.** `ProbeGapTests` says a converged sub-step *alone* widens the
round-versus-probe gap, 591 m to 754, and only reaches -6 m paired with gravity re-read per
sub-step — the cancelling-pair shape that cost item 2d three flights. `MirvBudgetTests` agrees from
the other side, flying the group 379 -> 453 m at 1x. The counter-argument is that both rigs sit a
planet at the origin, where a frame carrier is identically zero, and the walk is exactly that term.

So it is built as `arm/substep` and is **not** in the first factorial: `docs/MIRV-NEXT.md` item 2h
attacks the same gap from the instrument's side and the rig likes it four times better, so that one
flies first. The mechanism is shipped and costs nothing until a profile asks.
