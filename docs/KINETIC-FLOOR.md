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

**The mod's flown shot arrives at 32.0 degrees** (`IcbmConfig.ArrivalPreference` 0.5, every night
since 2026-09-03), where `cot` is **1.600**. A released warhead sub-steps at **1 ms, second order**
(`MunitionProfile.SubStepSeconds` on the Mk 21, `IcbmConfig.SecondOrderWarheads`) and re-reads the
ground every sub-step within 200 m of it (`IcbmConfig.ResampleGroundAtImpact`). The budget below is
priced there. It was first priced at a 7.1-degree arrival that nothing flew and a 5 ms first-order
round; those figures stay beside it as what they measured.

**Priced at the shipped shot, the floor is millimetres** — about 5 mm of integrator bias and a few
millimetres of walk scatter on gentle ground (see [The two numbers](#the-two-numbers)). Flown at
6,181 km, the landing's median is **2.1–3.0 mm** (`docs/ICBM-OUTSTANDING.md` 1.14, 2026-10-12).

---

## The budget

Two columns: what a term cost as first priced — a **7.1-degree** arrival and a 5 ms first-order
round, `ErrorBudgetTests`' cheapest unfloored deorbit — and what it costs on the **shipped** shot,
32 degrees and 1 ms second order. A height-driven term converts as `ground = height x cot(gamma)`,
8.03 at 7.1 degrees and **1.600** at 32. Where a term was re-measured the shipped column is the
test's output, run 2026-10-12; slopes are the fixtures' own.

| limit | first priced: 7.1 deg, 5 ms | **shipped: 32 deg, 1 ms second order** | what sets it | reducible? |
| --- | --- | --- | --- | --- |
| **the round's integrator** | 82–153 m | **5.4 mm**, one-signed down; sd 0.002 mm across 20 release states (`SubStepConvergenceTests`) | `MunitionProfile.SubStepSeconds` = 1 ms on the Mk 21, `Slug.SecondOrder` | yes, linear in the step: 2.5 mm at 0.5 ms, 1.1 at 0.25 |
| **the ground sampled once a frame** | 10–39 m | **gone** — re-read every sub-step within 200 m; what is left is the crossing solved on the chord across one sub-step's 5.5 m, which is inside the walk below | `IcbmConfig.ResampleGroundAtImpact` | — |
| **the walk: landing against its own prediction over relief** | not priced | **sd 1.3 mm at slope 0.05, 5.8 mm at 0.15, 36 mm at 0.80**; bias −4.9, −6.6, +13.8 mm (`WalkFloorTests`, float-packed relief) | the chord crossing, the staircase and the integrator's bias together | partly — by arriving steeper (section 5) |
| **the height field's own quantum** | 2.40 m | 0.48 m (0.2985 x 1.600), **only if** the aim and the round read different texels; no disagreement has been seen | `R16_UNORM` over 19,561 m = 0.2985 m | **no** — it is the shipped texture |
| **the predictor's ground crossing** | 2.01 m | 0.40 m worst and 0.20 m mean with the flag off (0.25 x 1.600; flown 0.20 m long at 32°). **Shipped on: 0.3 mm** — the crossing's height 0.176 mm at slope 0.122 x 1.600 (`ProbeCrossingFloorTests`); flown, the landing's step fell from −0.212 m to −0.007 | `ImpactPredictor.CrossingToleranceMetres` = 0.25 m; `IcbmConfig.PredictionStopsOnTheSurface` | done |
| **the float terrain staircase** | ≤ 0.9 m | **adds 0.9 mm at slope 0.05 and 3.9 mm at 0.15** in quadrature to the walk's sd — packed against exact, 1.27–1.48x (`WalkFloorTests`). A horizontal tread (~0.38 m) times the slope, not a vertical quantum | `Celestial.cs:833` packs the direction to `float3` | **no** — inside the engine |
| **the ecliptic's own arithmetic** | 1.8 mm | **16.5 mm** — the rig's round at its 1 ms sub-step adds five times as often; re-run of `TheEclipticIsWhereAKineticRoundKeepsItsPosition` | `double3` at 1.5e11 m; ulp 30.5 um | no; deterministic, and how much of it the flight shares with its aim is not measured |
| **the engine clock** | ≤ 104 mm | ≤ 69.5 mm if every frame rounds one way, **0.49 mm** as a random walk (19,861 frames of 25 ms) | `UniverseTime` is Int128 ns; `SimStep.DeltaTime` is the unrounded double | no |
| **naming the target** | 0.2 m – 1.9 km | same | one pixel of the player's viewport, through `SiteDesignator` | yes — zoom, or the panel's coordinate entry |
| **the terrain's own gain** | 1.2x to unbounded | `s x 1.600`: 1.09x at 5%, **1.31x** at the 0.149 median measured in section 5; unbounded above slope 0.625 | `slope / tan(gamma)`, a fixed point with no solution above 1 | only by arriving steeply |
| *(the harness's ruler)* | *13.4 cm* | *none at this scale* — `Vec.AngleBetween` is now `atan2`, and reads 3.12 cm as 3.12 cm | the miss is scored as `R x Vec.AngleBetween` | *n/a* |

Everything below is that table with its evidence.

---

## 1. The round's own integrator

### As shipped: 5.4 mm at 32 degrees

A released warhead steps second order at the Mk 21's 1 ms (`IcbmConfig.SecondOrderWarheads`,
`Slug.SecondOrder`). Flown from an 877 km release arriving at about 32 degrees, 35 ms frames, against
its own 0.0625 ms flight (`SubStepConvergenceTests`, a study):

| sub-step | down bias | sd over 20 release states |
| --- | --- | --- |
| 4 ms | −21.9 mm | 0.012 mm |
| 2 ms | −10.8 mm | 0.004 mm |
| **1 ms** | **−5.4 mm** | **0.002 mm** |
| 0.5 ms | −2.5 mm | 0.001 mm |
| 0.25 ms | −1.1 mm | 0.001 mm |

Still linear in the step — 5.4 mm per ms — but the coefficient is 30.6 m per ms on the first-order
round below, so second order took the term down about five thousand fold. It is a bias, not a
scatter, and it does not move with the frame (8–300 ms frames all read −4.9 to −5.4 mm). Against
the conic in vacuum the second-order warhead lands 0.000 m off, where first order is 1.986 m short
(`RoundIntegratorOrderTests`).

### As first measured: the first-order 5 ms round

Everything from here to section 2 is that round, kept as measured. **`KineticFloorTests` no longer
reproduces it**: the Mk 21's own `SubStepSeconds` now caps the sub-step at 1 ms, so its "5 ms" rows
fly a first-order 1 ms round — re-run, 22.8 m on the 3,459 km deorbit, 0.16 m at 88.2 degrees, 0.50 m
at 36.9 and 0.09 m at 30.

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

**Re-flown 2026-10-12, and the table above is wrong where the step exceeds 1 ms.** The rig handed the
round its step as the frame, and the round sub-steps at its own 1 ms whatever the frame is, so every
row above 1 ms flew at 1 ms. Flown at the stated sub-step (`Arsenal.RoundAtSubStep`), on this harness's
first-order round: 5 ms 68.7 m, 2.5 ms 30.5 m, 1 ms 22.9 m, 0.5 ms 7.6 m -- not linear, and the
shipped Mk 21 is the 1 ms row, 22.9 m on this first-order flight. The 153 m and every figure built on
it below are an artefact; the integrator term the table at the top carries, 5.4 mm, is the
second-order round's and is unaffected.

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
1.10 m of ground rotation under a 2.4 ms arrival error. There is nothing else left. That residue is
first order in the step too: the same flight at a first-order 1 ms reads 0.16 m.

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
moved — and `Sim/MunitionProfile.cs` carries `SubStepSeconds`, so the per-round step exists; the
Mk 21 is the one round that asks for one, at 1 ms.

The sub-step clamp never binds on entry: `MunitionProfile.MaxSubSteps` is derived so a frame of
`MaxFaithfulStepSeconds` is always covered, and in air `WarpPolicy` holds the world well under that.

---

## 2. The ground, sampled once a frame and held as a sphere

`Slug.Update` asks `IGroundTest.TryGround` **before** the sub-step loop and holds the answer — a
centre and a radius — for the whole frame. That is deliberate and documented: the terrain query is
the expensive call. The consequence is that the round stops on the height that was under it at the
*top* of the frame, and over sloping ground that is stale by the track it covered.

A released warhead re-reads the ground every sub-step within 200 m of it
(`IcbmConfig.ResampleGroundAtImpact`, `Slug.GroundResampleBandMetres`), so `d` below is one 1 ms
sub-step rather than a frame, and the crossing is solved on the chord between the two samples — exact
on a constant slope. What the chord misses is the relief's curvature across it, which is part of the
walk measured in section 5. The table is the round without the re-read.

With slope `s`, arrival `gamma` and a frame's ground track `d`, the stopping error is
`s.d / (tan gamma + s)`. Measured through that at 2,713 m/s
(`TheGroundSphereIsSampledOnceAFrameAndHeldFlat`):

| frame | arrival | 1% slope | 5% slope | 20% slope |
| --- | --- | --- | --- | --- |
| 50 ms (`FaithfulStepInAir`) | 7.1 deg | 10.0 m | 38.6 m | 83.0 m |
| 50 ms | 30 deg | 2.0 m | 9.4 m | 30.2 m |
| 25 ms | 30 deg | 1.0 m | 4.7 m | 15.1 m |
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
the answer is always below the surface, so always downrange. `ErrorBudgetTests` measured it at 18 cm
actual at the 7.1-degree arrival. Flown at 32° it is 0.20 m long on average — half the tolerance
times 1.600. Placing the crossing between the two samples that bracket it removes it for no lookup —
`IcbmConfig.PredictionStopsOnTheSurface`, on: flown, the landing's step below the late re-flies went
from −0.212 m to −0.007 (`ACCURACY-PLAN.md` 3dg). Headless with it on, the crossing sits 0.002 mm,
0.176 mm and 0.710 mm off the ground at slopes 0.03, 0.122 and 0.35 (`ProbeCrossingFloorTests`):
0.003, 0.28 and 1.1 mm of ground at 32 degrees.

| arrival | m of ground per m of height | quantum | crossing | both |
| --- | --- | --- | --- | --- |
| 5 deg | 11.43 | 3.41 m | 2.86 m | 6.27 m |
| **7.1 deg** | 8.03 | 2.40 m | 2.01 m | **4.40 m** |
| 15 deg | 3.73 | 1.11 m | 0.93 m | 2.05 m |
| 30 deg | 1.73 | 0.52 m | 0.43 m | 0.95 m |
| **32 deg (shipped)** | 1.600 | 0.48 m | 0.40 m | 0.88 m |
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

**Not biasing is not the same as costing nothing.** The round brackets its ground crossing across one
sub-step and the prediction refines its own to under a metre, so the two land on *different treads*
and the difference is a scatter rather than an offset — the packing multiplies the walk's sd by
**1.1x to 1.6x** at the slopes under the target (`TerrainStaircaseTests`, `docs/ACCURACY-PLAN.md` 3eh).

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

### It is that steep, measured — 2026-09-17

That was open until the mod's own release log was read as a slope instrument. Every MIRV release
prints the ground's rise from the aim to the impact beside the probe's miss, so `|rise| / miss` is a
slope at the scale the miss actually spans — a **0.47 m median baseline**, which is the metre scale
this section was asking about. Over 558 warheads at the site flown then (26.485S, 68.148W, in the
Andes; the shots have flown the flat Chaco since):

| | local slope | loop gain `s/tan(gamma)` | amplification at the flown 31.7 deg |
| --- | --- | --- | --- |
| 10th percentile | 0.017 | 0.03 | 1.03x |
| median | **0.149** | 0.24 | 1.32x |
| 75th | 0.345 | 0.56 | 2.26x |
| 90th | **0.823** | **1.33** | **unbounded** |
| worst of 558 | 2.223 | 3.59 | **unbounded** |

**So a tenth of this site's ground is past gain one at the angle the mod flies.** Not most of it,
and the median is benign — but the tail is real, it is not a modelling assumption, and it is the
same tail that decides how much of the walk's scatter the ground accounts for
(`docs/ACCURACY-PLAN.md` 3eh).

### And the walk is one of the terms it multiplies

`WalkFloorTests` flies the round and its own prediction to relief summed over seven octaves, with
release states drawn at random. Sweeping the arrival angle at the site's median slope:

| arrival | `cot gamma` | walk scatter | scatter / cot |
| --- | --- | --- | --- |
| 20.28 deg | 2.706 | 18.461 mm | 6.823 |
| 31.73 deg | 1.617 | 5.772 mm | 3.569 |
| 49.82 deg | 0.844 | 1.358 mm | 1.608 |

**20 to 50 degrees is 13.6x on the walk**, where `cot gamma` alone accounts for 3.2x — a steeper
arrival shortens the ground one sub-step bracket covers as well as the height-to-ground conversion,
and the two compound. That makes the arrival angle a **stronger** lever on this term than the table
above would predict, and the strongest found on the walk in this programme.

---

## 6. Two candidates that turned out to be worth nothing

Both were worth checking. The clock is closed; the ecliptic, re-run at the shipped 1 ms, is not.

**Floating point in the ecliptic.** A `double` at 1 AU has a spacing of **30.5 um**, and a round's
`PositionEcl` is exactly that: every sub-step adds tens of metres to a number of 1.5e11, and every
ground test subtracts two such numbers. Flown identically with the planet moved out and left still
(`TheEclipticIsWhereAKineticRoundKeepsItsPosition`), first at a 5 ms sub-step and re-run at the Mk 21's
1 ms, which adds five times as often:

| planet at | at 5 ms | at 1 ms |
| --- | --- | --- |
| 1e9 m | 0.042 mm | 0.131 mm |
| 1 AU | 1.803 mm | **16.5 mm** |
| Saturn's 1.43e12 m | 224 mm | 1,092 mm |

At 1 ms that is **above the flown 2.1–3.0 mm**, so it cannot be landing whole on the flown shot;
it is deterministic for a given release, and whether the flight's aim loop shares it — or the
rig's first-order round accumulates it differently from a second-order one — is not measured. The
mod's own ballistic maths works in `Cci` (`Sim/BallisticBody.cs`), where the carrier is subtracted;
only the round itself is in `Ecl`, and this is the price of that.

CLAUDE.md's note about part transforms packed to `float` at Ego magnitude is **drawing only** —
confirmed: `Slug`, `Interceptor` and `ImpactPredictor` are `double3` throughout, and nothing in the
round's integration reads a drawn transform back.

**The engine clock.** `UniverseTime` is `Int128` **nanoseconds** (`KSA/UniverseTime.cs`), and
`UniverseTime operator +(UniverseTime, double)` rounds to a whole nanosecond — while
`SimStep.DeltaTime`, which is what `KsaWorld.ConsumeSimStep` hands the mod, is the *unrounded*
double that was added. So the mod integrates by one number and the world advanced by the other, by
up to half a nanosecond per frame. Over the 19,861 25 ms frames of a 497 s flight
(`TheEngineClockIsQuantisedToTheNanosecond`): **69.5 mm** if every frame rounded the same way,
**0.49 mm** as a random walk. And `GetElapsedSeconds()`'s own decay is 26 um at a year of universe
time, 3.3 mm at a century.

The clock is ruled out for Earth; the ecliptic is not, at the shipped sub-step.

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
- The aim point is persisted as JSON doubles in `ballistic.json` (`Sim/BallisticSettings.cs`), so
  saving it does not quantise it either. The panel also takes a typed latitude and longitude, as doubles.

The refinement loop in `TryCursorGroundPoint` has its own limit, and it is item 5's gain by another
name: its fixed-point iteration converges only while `slope / tan(depression) < 1`, which from
ground level is most of the screen. It keeps the better sample and stops, so it degrades rather than
diverging — but a click at a shallow depression angle onto rough ground is not resolved to the
pixel it was worth.

---

## 8. Terminal guidance has no floor of its own

`Sim/Slug.cs` already carries `GuidanceMode.Inertial` — the same tail kit the bomb uses,
`Sim/TailKit.cs`, steering the landing it predicts at whatever lateral limit the profile declares.
Flown from a 30-degree arrival with the release deliberately displaced across the track
(`WhatATerminallyGuidedRoundCanStillTakeOut`):

| displaced | 0.5 g | 2 g | 6 g | 20 g | unguided |
| --- | --- | --- | --- | --- | --- |
| 500 m | **2.32 m** | 2.32 m | 2.32 m | 2.32 m | 500 m |
| 5 km | 4,281 m | 2,125 m | **2.32 m** | 2.32 m | 4,999 m |
| 50 km | 49,268 m | 47,099 m | 41,281 m | 18,984 m | 49,990 m |

So: **half a g clears half a kilometre, 6 g clears five, and 20 g cannot clear fifty.** Terminal
guidance is a way of removing the guidance budget, not the flight-time budget. A law chasing the
line of sight needed 2 g for the same half kilometre, and left 411 m at half a g.

The couple of metres it settles on is **entirely the step**, not the law
(`WhereTheSteeredRoundsLastFewMetresComeFrom`):

| nav constant | 5 ms | 1 ms | 0.25 ms |
| --- | --- | --- | --- |
| 3 | 2.32 m | 0.46 m | 0.12 m |
| 4 | 2.32 m | 0.46 m | 0.12 m |
| 6 | 2.32 m | 0.46 m | 0.12 m |

First order in the sub-step at 0.465 m per ms, and flat in the nav constant. **The steering law
contributes nothing of its own** — a steered round is bounded by the integrator, by where it thinks
the target is, and by the same ground model as an unguided one, and by nothing else.

### And it removes the arrival angle, which is the whole of the unguided budget

The headline of this file is that five of the eight terms are heights and `cot(gamma)` turns them
into ground, so the first-order 5 ms round could not be precise at seven degrees at any price.
**That does not survive terminal guidance** (`WhetherTerminalGuidanceRemovesTheNeedForASteepArrival`,
a 500 m release error steered out at each arrival):

| arrival | `cot γ` | unguided | 2 g at 5 ms | at 1 ms | at 0.25 ms |
| --- | --- | --- | --- | --- | --- |
| **7.1 deg** | 8.03 | 498.40 m | **2.32 m** | 0.46 m | 0.12 m |
| 15 deg | 3.73 | 499.59 m | 2.32 m | 0.46 m | 0.12 m |
| 30 deg | 1.73 | 499.89 m | 2.32 m | 0.46 m | 0.12 m |
| 60 deg | 0.58 | 499.96 m | 2.32 m | 0.46 m | 0.12 m |

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

### And it stays that way, because that is what a Mk 21 is

**Decided, not pending.** The real Mk 21 — W87, Peacekeeper and now Minuteman III — is purely
ballistic: inertial only, no seeker, no control surfaces. So are the Mk 4A and Mk 5 on Trident. No
deployed strategic MIRV has terminally guided reentry vehicles, and the reason is mass and mission:
every RV would need its own seeker, power and actuators on a bus already trading throw-weight for
warhead count, against targets where inertial accuracy plus yield was enough.

Terminal guidance on a ballistic RV is real, and it has consistently been a **single**-warhead thing
— Pershing II's MaRV with RADAG radar area correlation, ~30 m CEP against ~400 m for the ballistic
Pershing 1a, and later the Chinese DF-21D and DF-26 anti-ship MaRVs. Trident's Mk 500 Evader
manoeuvred to defeat defences rather than to be accurate, and was not deployed.

Six independently steering RVs off one bus would therefore be a mod invention. Setting
`Guidance = GuidanceMode.Inertial` on the Mk 21 would collapse this whole file's budget to the
sub-step — 2.32 m from a 500 m release error, and the arrival angle worth centimetres instead of a
factor of eight — and it would be a lie about the part. **If a steering reentry vehicle is wanted it
is a second profile**, named as the MaRV it is, and the Mk 21 stays the hard case it exists to be.

---

## The two numbers

Both assume every *reducible* guidance term has already been driven to zero — the aim correction's
frozen residue (760 m), the tube cant (233 m), the frozen gravity (284 m) and the cutoff residual
(38 m) are all in `docs/MIRV-NEXT.md` and are engineering rather than floor. What is left is this
file.

### An unguided kinetic round

**About 5.5 mm at the shipped 32 degrees and 1 ms second order, on gentle ground** — 7.9 mm where the
ground runs at 15%.

Root-sum-square of the headless terms at 32 degrees and an exactly-known aim point:

| | slope 0.05 | slope 0.15 |
| --- | --- | --- |
| integrator bias (`SubStepConvergenceTests`) | 5.4 mm | 5.4 mm |
| walk sd: chord crossing, relief, staircase (`WalkFloorTests`) | 1.3 mm | 5.8 mm |
| predictor crossing, flag on | 0.1 mm | 0.3 mm |
| engine clock, random walk | 0.5 mm | 0.5 mm |
| **root sum square** | **≈ 5.5 mm** | **≈ 7.9 mm** |

The terrain gain multiplies the walk on top: 1.09x at 5% and 1.31x at the median slope of section 5.
Left out are the height quantum — 0.48 m if the aim and the round ever read different texels, which
a millimetre landing says they do not — and the ecliptic's 16.5 mm, which a millimetre landing says
cannot be arriving whole.

**Flown, the landing is 2.1–3.0 mm** (median, 6,181 km, the flat Chaco; `docs/ICBM-OUTSTANDING.md`
1.14). That is at the walk's floor for gentle ground and under the rig's 5.4 mm integrator bias. The
rig flies an exponential atmosphere on a planet at the origin, so the two are not the same flight,
and which of the one-signed terms cancel in flight has not been measured. No term in the shipped
column sits above the flown miss except the two just left out.

#### First priced: 88 degrees, 5 ms first order

**About 1.2 m, on a near-vertical arrival**, kept as measured. Root-sum-square at 88 degrees, a 17 ms
frame, 5% ground slope and an exactly-known aim point:

| | |
| --- | --- |
| integrator at 5 ms | 1.11 m |
| ground held for a frame | 0.28 m |
| height quantum + crossing tolerance | 0.02 m |
| float terrain staircase | ≤ 0.02 m |
| ecliptic doubles, engine clock | 0.002 m |
| **root sum square** | **≈ 1.2 m** |

**On a 7.1-degree arrival the same budget was ≈ 160 m** — 153 m of integrator, 4.4 m of quanta,
10–39 m of frame-held ground, the whole lot multiplied by a terrain gain of 1.2x to unbounded. What
took it to millimetres was not a steeper geometry alone: the second-order sub-step, the ground
re-read near impact and the crossing placed on the surface each removed a term that 32 degrees only
divides by five.

### A terminally guided kinetic round

**About 2.3 m as the flight model stands, and about 0.5 m at a 1 ms sub-step** — provided it has the
authority for its release error, which is half a g for a few hundred metres.

It is bounded by exactly the same integrator and the same ground model as the unguided round, and it
buys nothing at all against them. What it buys is immunity to everything *upstream*: the cant, the
cutoff residual, the aim residue and a bad release all become someone else's problem the moment the
round can pull 2 g in the last twenty seconds. That is the case for building one — not accuracy at
the floor, but reaching the floor at all from a shot that would otherwise be a kilometre out.

**And it is the cheap route to a metre, because it does not need the trajectory changed.** The
first-order 5 ms round's path to 1.2 m was a near-vertical arrival, which from orbit means taking out
essentially the whole orbital velocity — about 7.8 km/s, with downrange collapsing from 3,094 km to 96.
A steered round reaches 0.46 m on a seven-degree arrival, for a tail kit and a finer sub-step. The
shipped unguided round took the other cheap route — a second-order 1 ms step, which reaches
millimetres at 32 degrees for no propellant at all.

### The honest caveats on both

- **Neither is a CEP in the statistical sense.** Every term here is a deterministic bias of a
  deterministic simulation: the same shot flown twice gives the same answer to the bit. What makes
  a flown group scatter is the frozen gravity, whose error grows with the step — but the step's own
  run-to-run variation is **not** pacing. It is a single latched `WarpPolicy` decision that either
  happens or does not, worth 0.7-2.6 km; `docs/MIRV-NEXT.md` item 7e has the measurement and
  conditioning on the coast step leaves 10-88 m.
- **Nothing here is flown but the sub-step**, below. It is headless, and the rig's planet is at the origin.
- **The ruler.** Every budget in this repository scores a miss with `R * Vec.AngleBetween`. As an arc
  cosine that could not read under **13.4 cm**, and the 5 ms figures above were taken through it;
  `Vec.AngleBetween` is now `atan2`, and `TheHarnessReadsMillimetreMissesAsTheyAre` reads 3.12 cm as
  3.12 cm.

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
  they did not it was worth 35 km of ground at a seven-degree arrival, which was a defect rather
  than a floor, and it is excluded here.
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

**It is now enabled**, and the paragraphs below are the argument that was open at the time. The
Mk 21 ships `SubStepSeconds = 0.001f` (`Sim/Arsenal.cs`, commit `065907a`), so **the largest single
term in the budget above has been removed** and this file's headline number is stale by that much.
It is affordable on this round and nowhere else: six warheads at a millisecond is ~300 sub-steps a
frame, where a 150-shell CIWS burst at the same step would be 7,500.

The reasoning that held it back was the trade rather than the term — five times the integration work
per round for a gain five times under the noise floor, which is `CLAUDE.md`'s rule about unmeasured
per-frame costs. Both halves of that then moved.

**What let it in.** The noise floor came down with the miss — a 6-against-6 batch on 2026-08-24 read
a 0.05 km median with a 0.02-0.09 km range, and the rank test settles ratios rather than metres, so
122 m against a 50 m median is no longer under the instrument. The wall-clock cost is measured too,
at 0.3 ms a frame for a six-warhead group.

**But the rig now argues against it.** `ProbeGapTests` says a converged sub-step *alone* widens the
round-versus-probe gap, 591 m to 754, and only reaches -6 m paired with gravity re-read per
sub-step — the cancelling-pair shape that cost item 2d three flights. `MirvBudgetTests` agrees from
the other side, flying the group 379 -> 453 m at 1x. The counter-argument is that both rigs sit a
planet at the origin, where a frame carrier is identically zero, and the walk is exactly that term.

That objection was flown past rather than answered: the round-versus-probe gap has since been
attacked from the instrument's side, and the sub-step landed in `065907a`. What the rigs said
remains the reason to distrust a *headless* reading of this term — both sit a planet at the origin,
where a frame carrier is identically zero, and the walk is exactly that term.
