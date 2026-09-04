# Getting the warheads inside a metre

**A plan, not a record — with one exception.** Nothing here has been flown except §5, whose
proposal was tested on 2026-08-25 and refuted; that section is now a record and the ladder stops at
rung C because of it. `docs/MIRV-NEXT.md` is what the shot costs
today; `docs/KINETIC-FLOOR.md` is what is left when every item on that list has landed;
`docs/ARRIVAL-ANGLE.md` is the lever this plan is mostly about. This file is the route between
them, the campaign that tests it, and the three pieces of harness the campaign needs before it can
run unattended.

**The headline, and it is not a guidance result.** Metre-level accuracy is a *trajectory* choice.
Every large term in the miss is a velocity error multiplied by the trajectory's own sensitivity, or
a height error multiplied by `cot γ` — and both of those belong to the arrival angle, not to the
loop. At the **12.9 to 17.5 degrees** every shot in this repository has actually arrived at —
logged 2026-09-02, where the seven was reconstructed and never flown (`docs/ACCURACY-PLAN.md`
3af) — **no amount of guidance work reaches a metre**: the drag-model term alone is 1.8 km and its only observer shares the model.
At forty-five degrees the same shot is inside six metres with the residual it already achieves.

So this plan is not "make the loop better". It is: **fly the geometry that makes a metre possible,
then remove the three terms that are still above one at that geometry.** In that order, because the
ranking of everything else changes when the geometry does.

---

## 1. The envelope, which is what "metre-level" has to be stated against

The whole shot in quadrature, from a 400 km platform, against the cutoff residual and the arrival
angle. Sensitivities and the drag column are `docs/ARRIVAL-ANGLE.md`'s table; the residual columns
are that table re-priced at four residuals, one metre of surface disagreement included.

| arrival | reach | brake | rms `dMiss/dV` | @0.070 m/s | @0.017 m/s | @0.005 m/s | @0.002 m/s |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 7.5° | 6,379 km | 473 m/s | 5,614 | 1,838 m | 1,798 m | 1,795 m | 1,795 m |
| 10° | 4,237 km | 929 | 2,345 | 417 m | 385 m | 383 m | 383 m |
| 15° | 2,736 km | 1,774 | 1,072 | 108 m | 79 m | 77 m | 77 m |
| 20° | 2,015 km | 2,576 | 686 | 56 m | 31 m | 29 m | 29 m |
| 30° | 1,272 km | 3,911 | 415 | 30 m | **11 m** | 8 m | 8 m |
| **45°** | **732 km** | 5,307 | 293 | 21 m | **5.5 m** | **2.7 m** | 2.3 m |
| **60°** | **418 km** | 6,277 | 254 | 18 m | 4.5 m | **1.7 m** | **1.3 m** |
| 88.7° | 0 km | 7,673 | 242 | 17 m | 4.1 m | 1.2 m | **0.5 m** |

Three things fall straight out of it, and each one decides part of the plan.

**At a graze the residual is irrelevant, and the flown rung is not quite there.** At 7.5° the four
columns are 1,838 / 1,798 / 1,795 / 1,795 — a forty-fold improvement in the burn buys 2 %. At the
**15°** row the mod actually sits nearest they are 108 / 79 / 77 / 77, so the same improvement buys
**29 %**: still not the lever, but not nothing, and the blanket "below twenty" this line used to
carry was priced off a seven-degree arrival nothing flies. The floor is the drag-model term, and
`docs/ARRIVAL-ANGLE.md` explains why no correction loop removes it: the loop's only observer is
`ImpactPredictor`, which runs the round's own `Medium.Drag`, so whatever that model has wrong
survives the loop intact. **Every hour spent on guidance at a shallow arrival is spent under this
ceiling.**

**Metre-level starts at forty-five degrees and needs a residual near 0.005 m/s.** Today's trimmed
0.017–0.023 m/s puts 45° at 5.5 m and 60° at 4.5 m. Getting to one metre is then one further
factor of three or four on the residual, and nothing else.

**And it costs the reach.** 45° reaches 732 km from a 400 km platform, 60° reaches 418 km. A metre
and an intercontinental range are not simultaneously available from one pass, because range sets a
floor under the sensitivity that no steepening reaches — `docs/ARRIVAL-ANGLE.md`'s loft table bottoms
out near 930 m per m/s on a 3,459 km shot however tall the arc, which is 16 m at today's residual and
about 5 m at 0.005. **So this is a ladder with rungs, not a single number**, and the plan states the
rung each phase is aiming at:

| rung | miss | arrival | reach from 400 km | what it needs beyond the rung below |
| --- | --- | --- | --- | --- |
| **A** | ~80 m | 15° | 2,736 km | the bus can pay for a steep arrival at all |
| **B** | ~30 m | 20° | 2,015 km | nothing new — the same fix, one row steeper |
| **C** | ~5 m | 45–60° | 418–732 km | a finer trim band; per-tube release |
| **D** | **~1 m** | 60–88° | 0–418 km | a 1 ms round sub-step; ground per sub-step; residual to 0.003 m/s |
| *(E)* | *~20 m at 2,000 km* | *20° lofted* | *intercontinental* | *everything in C and D, on a lever four times longer* |

**Rungs A and B are passed, on the healthy population — flown 2026-09-04.** Eighty flights at a
**32.0 degree** arrival landed at a median of **20 m** with **0.00 km** of group spread, which is past
rung B's thirty. The weapon is not past rung B, and the distinction is the whole of where this plan
now stands: 16 of those 80 landed at **84 km**, all of them from the two worlds in ten that threw the
`trim` terminator. `docs/ACCURACY-PLAN.md` 3ar has the mechanism — the coast is integrated rather
than propagated for 70% of its length in an affected world — and item 19 is the one thing left
between here and a reliable rung B.

So the first blocker is no longer B1. It is that a fifth of worlds do not get to fly the ladder at
all.

**One thing worth saying plainly:** the Mk 21 has a 2 km lethal radius, so none of this decides
whether the target survives. It is worth having for a conventional payload, for a kinetic rod — which
is `docs/KINETIC-FLOOR.md`'s whole subject — and for its own sake. This plan does not pretend
otherwise.

---

## 2. The blockers, in the order they bind

### B1 — the bus cannot pay for a steep arrival, and that is flown

> **Stale in two places, and the premise is now in doubt — 2026-08-26.**
>
> **Item 2 has landed.** `9b48bd1` made `BusTrim.MaxMetresPerSecond` a *floor* under a ceiling the
> caller sizes per job (`BusTrim.cs:419-421`, wired at `IcbmComputer.cs:1127-1133`, pinned by
> `BusTrimTests.TheCallerMaySizeThePerSolveCeilingButNeverBelowTheConstant`). The failure quoted
> below — `MaxMetresPerSecond declined it whole` — cannot recur.
>
> **Item 3 was answered headlessly and the answer was refuted in flight.** The rig said a floored
> shot needs no correction and the demand is the trajectory search still moving; four shots at a 15°
> floor said otherwise, by 150×. `docs/MIRV-NEXT.md` item 8q.
>
> **And the blocker itself did not reproduce.** Neither floored shot in that batch failed to pay a
> post-boost demand, and one of them landed at **47 m**. Re-read this section against those four
> shots before building item 1.

Two shots at `MinArrivalAngleDeg = 15` (`docs/ARRIVAL-ANGLE.md`, *Flown*) and one at 15 on
2026-08-25 (`docs/MIRV-NEXT.md` item 7g) say the same thing from two directions.

The **search half works**. The shot flies the constrained arc, reports `reach Reachable`, cuts off
0.01 m/s short of its own solution, and produces **the tightest group ever measured here — 0.00 km
of spread, six warheads on one point.** That is exactly what the geometry is supposed to buy, and it
arrived on the first attempt.

The **coast half refuses**, in two different ways on two different flights:

```
more than a separation could have cost, 10.97 m/s left on the bus
  -- owed 0.01 m/s at the split, 1.11 m/s on release          <- MaxMetresPerSecond declined it whole

nothing left aboard moves the bus, 6.68 m/s left on the bus   <- and this one ran the thrusters dry
```

A steep floor asks for a post-boost correction of **7–11 m/s** where the baseline needs 2.45. Three
separate things then go wrong, and all three are in `Sim/BusTrim.cs`:

- **`MaxMetresPerSecond = 10` declines the whole correction.** It exists to catch a runaway — the aim
  correction and the trim driving one vehicle through one prediction — and it cannot presently tell a
  runaway from a large legitimate correction. The jets added for exactly this case were never fired.
- **The give-up strikes off healthy axes.** `BusAuthorityTests` finds the threshold exactly: a
  reference drifting faster than the bus's own lateral authority reads as a dead thruster, the axis is
  struck off **permanently**, and 2.02 m/s is abandoned. The bus has all six directions —
  4.000 fore/aft and 4.243 each lateral, `tools/model/checkring.py --translation` — so every strike
  under a steep floor is a false positive.
- **A correction that size should not be arriving after cutoff at all.** 11 m/s times 1,072 m per m/s
  is a 12 km miss the loop is trying to fix with attitude jets, when the engine that could have fixed
  it for free stopped seconds earlier.

**What to build, in this order.**

1. **Separate "cannot push this way" from "cannot keep up".** The distinction is available and
   unused: the bus knows its own commanded acceleration and can measure what it achieved, so a
   component falling *slower than commanded* is a live axis losing a race and a component **not
   falling at all** is a dead one. Only the second is struck off, and only the second is permanent.
   `BusAuthorityTests` already has the sweep that pins where the current rule crosses over.
2. **Make the runaway guard a rate, not a magnitude.** What `MaxMetresPerSecond` is really guarding
   against is the reference *moving* — the aim correction re-solving under the trim's own thrust. So
   guard the derivative: refuse when the demand grows across passes, accept a large demand that is
   stationary. A steep arrival's 11 m/s is stationary; a wind-up is not.
3. **Then ask why the demand is 11 m/s**, which is the real question and belongs to B4. A burn that
   ends on its solution to 0.01 m/s should not need eleven metres a second afterwards. The candidate
   is that the aim bias committed before cutoff is priced against the *shallow* arc's sensitivity and
   re-expressed against the steep one after it.

**Gate:** rung A is reached when a 15° shot's post-boost demand is paid in full and the group lands
inside 150 m. Until it does, nothing below is worth flying, because every term below is priced at an
angle the shot cannot yet hold.

### B2 — the trim's settle band is a hard floor at about five metres

`BusTrim.SettledMetresPerSecond = 0.02`. At the steep sensitivities in the envelope table that is
**5.1 m at 45° and 4.4 m at 60°, exactly**, and it is a floor rather than a term: the trim stops
there by construction whatever else improves. Rungs C and D cannot be reached without moving it.

It cannot simply be lowered. Three things sit under it:

- **The thruster quantum.** `BusTrim` already takes `max(SettledMetresPerSecond, quantum)`, so the
  achievable band is whatever one control period of the minimum translational impulse produces on the
  bus's mass. That is readable off the XML the same way `checkring.py --translation` reads the
  authority, and it is the number that says whether 0.003 m/s is available at all.
- **The frame.** A thrust that stops on a frame boundary leaves `accel × step × 1` behind, which is
  the same arithmetic as the cutoff residual and the same fix — see §5, where the step gets shorter
  for free.
- **The reading.** Trimming to 0.003 m/s against a prediction whose own noise is larger is the loop
  converging on its instrument, which is B4's failure mode in miniature.

So B2's real deliverable is a **measurement first**: `BusTrimTests` extended to report the smallest
band the shipped bus's own impulse quantum can hold at the shipped mass, and `checkring.py` extended
to print it. Then the constant follows from that rather than from a preference, and if the answer is
"0.02 is the quantum" the fix is a smaller nozzle rather than a smaller constant.

### B3 — the round's own integrator, and the ground it stops on

Both are `docs/KINETIC-FLOOR.md` §1 and §2, and both collapse at a steep arrival — which is why they
are third rather than first.

| | at 7.1° | at 66° | at 88° |
| --- | --- | --- | --- |
| 5 ms symplectic Euler (`Interceptor.SubStep`) | 82–153 m | 0.69 m | 1.11 m |
| ground sampled once a frame, 5 % slope | 38.6 m | ~1.6 m | 0.8 m |

At rung C both are under a metre already. At rung D they are the two largest remaining terms, and
both have a lever that costs nothing anyone will notice:

- `MunitionProfile.SubStepSeconds` on `ReentryVehicleMk21` — **the per-round field already exists and
  nothing in the arsenal set it.** 5 ms → 1 ms is first-order, 30.6 m per millisecond, and costs 300
  vector updates a frame for a six-warhead group. The measured objection is a 150-shell CIWS burst,
  which is a different profile and unaffected. `MaxSubSteps` scales with it, so
  `MaxFaithfulStepSeconds` does not move and the world's timewarp is untouched — the confusion that
  cost 164 km.
- Re-sampling `IGroundTest` per sub-step rather than once a frame. Flown headlessly through
  `ProbeGapTests` it moves the impact 2 m at a 50 ms frame; the cost is the terrain query ten times a
  frame instead of once, per round.

**The cancelling-pair warning does not reach the sub-step, and this section had that wrong.**
`docs/MIRV-NEXT.md` item 2d records the per-sub-step gravity re-read losing three times out of three
because it removed one half of a cancelling pair — but **the other half was the pull centre, not the
sub-step**, and both shipped together on 2026-08-24 for a mean miss of 0.44 → 0.05 km.

Against the round the game now flies, gravity's own marginal contribution is **zero** and the
sub-step is a lone term:

| | from the round's own release probe |
| --- | --- |
| before the per-sub-step gravity, pre-2026-08-24 | 591 m |
| **shipped** | **−149 m**, and flat from a 25 ms frame to a 320 ms one |
| shipped + a 1 ms sub-step | **−6 m** |

Two things follow. The sub-step is **flyable on its own** — `arm/sub1ms` is that arm, and it measures
−11 m against `dev`'s −149. And the shipped gap being *flat in the frame* is the larger result: it is
what retired `WarpLatchScatterTests`, whose whole subject was two shots from one release state landing
313 m apart on one frame's length, and which now measures 1 m. **Warping the coast costs 4 m**, where
before it cost hundreds — so the wall clock §5 could not buy with frame rate is available here
instead, and for the same reason the accuracy is.

The ground sampled once a frame is still untested and still second: 2 m at a 50 ms frame, headless.

### B4 — the aim correction's frozen residue, re-asked at a steep arrival

The largest single term in `docs/MIRV-NEXT.md` §9 at 760 m, and the one the rig cannot price. It is
also the term most likely to be **mostly gone for free** once the geometry moves: the loop is taking
out a drag loss that is 13.4 km of range at 7.5° and 0.3 km at 15°, so there is far less for it to
absorb and far less to freeze wrongly.

What is settled and must not be re-litigated:

- **Never-freezing loses, 5.7×, flown and closed** (item 7g). Its groups are the tightest of the
  night and land 5.7 km out while reporting 0.03 km, and it cuts off with eight times the residual —
  an aim that never settles is a cutoff condition that never settles. Do not tune the freeze on those
  grounds.
- **Stopping early is not the conservative choice.** `AimCorrection.WorseBeforeStopping` is patient
  because the miss is not monotonic in the aim: measured at 7,645 km, a loop that stopped inside a
  five-cycle patch kept a 3.34 km aim that measured 15.86 km one cycle later.

What is open, and is the actual work:

- **Re-measure the whole loop at a steep arrival before changing anything in it.** Every constant in
  `AimCorrection` and `PostBoostAim` was tuned against a shot whose observer had kilometres of drag
  loss to find. `arm/aim` (item 7f) is built and parked for exactly this and has never flown.
- **B1 step 3 lives here**: whether the pre-cutoff bias is being committed against the wrong
  sensitivity is what decides whether the post-boost demand is 11 m/s or 1.
- **Start rough.** `ArrivalFloorFlightTests.GroundUnderTheObserverMovesTheShippedShot` measures the
  shipped configuration moving by kilometres between a mean sphere and `DeorbitShot.RoughGround`.
  A correction-loop result taken on a smooth planet is measured against an instrument the real one
  does not have.

### B5 — the tube cant, which becomes the spread term at rung C

Six kicks 6° off the mean at 0.5 m/s. On the guided trajectory that is 233 m of spread today; at the
steep sensitivities it is about 13 m, and at rung D it is the difference between a group and a point.

Two candidate fixes are already priced and one is already refused:

- **Item 5, re-pointing between releases** — turn the bus so each tube lies on the salvo's line.
  Removes the cant outright, costs no propellant, and its only open question was why a separated bus
  would not hold a six-degree command. That turned out to be a latched pointing deadband, now
  re-derived every frame and reading **0.31°** where it read 22.11. Whether the bus follows the
  command is one flight.
- **Item 5c, per-tube velocity trim** — refused: every tube-to-tube step is a pure lateral translation
  to six significant figures, and the argument concluded that a bus able to fly it could do the
  six-degree turn instead, for free. That reasoning stands.
- **The cheapest thing of all is to predict each tube's own kick rather than the salvo's mean.**
  `ImpactPredictor` already takes a `ReleaseImpulseCci()`; giving it the *firing tube's* impulse turns
  a bias-plus-spread into spread alone, costs one argument, and is a per-round term — so it is
  screenable by `tools/ab-shot.py` on a single flight rather than costing a night.

### B6 — what is left, and cannot be moved

From `docs/KINETIC-FLOOR.md`, all re-priced at 60°:

| | at 60° | why it cannot move |
| --- | --- | --- |
| the height field's 16-bit quantum | 0.17 m | `R16_UNORM` over 19,561 m — it is the shipped texture |
| the float terrain staircase | ≤ 0.18 m | `float3.Pack` inside `Celestial.cs:833` |
| the predictor's ground crossing | 0.14 m | reducible, at more bisection steps |
| the engine clock | ≤ 0.10 m | `SimStep.DeltaTime` is an unrounded double |
| the harness's own ruler | 0.13 m | an arc cosine at planetary radius |

In quadrature about **0.32 m**, which is the honest floor of rung D. A plan that promises better than
half a metre is promising something the shipped height texture cannot express.

---

## 3. The campaign — orbits and targets, because one of each proves nothing

Two nights in this repository were spent measuring a hillside without knowing it
(`docs/MIRV-NEXT.md` item 7g). The rule that came out of it is the design rule here: **a shot group
measures guidance only where the ground is well conditioned for the angle it arrives at**, and at a
steep arrival almost all ground is. That is one more reason the geometry comes first — but it is not
a reason to keep testing at one place.

### The orbit axis

The pick-up state is what conditions everything downstream, and today it comes from one save at one
altitude. **`Vehicle.Teleport(Orbit, doubleQuat, double3)` and `Orbit.CreateFromStateCci` are both
public**, so the harness can place the launch vehicle on any orbit it likes with nobody clicking
anything. That is the single change that makes an orbit matrix possible at all, and §6 is where it
goes.

| axis | values | what it tests |
| --- | --- | --- |
| altitude | 200 / 400 / 800 km | the sensitivity scales with the fall; 800 km is where `BurnWindow`'s phasing search matters most |
| inclination | 0° / 51.6° / 98° (sun-synchronous) | the plane-change term, and `OrbitPlane`'s "waiting cannot fix an inclination" answer |
| target relative to the track | under it / 20° off / 45° off / just passed over | the last one is the case that has no affordable arc now and a cheap one an orbit later |
| departure | leave now / wait for the window | `BurnWindow` searching a day, and whether the wait is taken |
| **the ground launch** | pad to 2,000 km | the one case that *cannot* steepen cheaply — it pays the whole arrival angle out of the booster |

`DeorbitTests` already covers nine of these geometries headlessly and holds them to two kilometres.
The campaign's job is to re-fly them **at each rung's arrival floor**, tighten the assertion to the
rung, and then fly the two or three that the rig says are hardest.

### The target axis

Four targets, chosen for terrain rather than for interest, and screened **before** a night is spent
on one:

| | why |
| --- | --- |
| 26.5S 64.0W — the standard | 247 impacts across 21 nights say it is a plain at every scale: +0.27 % over 90 km, 15.9 m of height variation across the 75 impacts within 500 m |
| an ocean target | the sea clamp is the one surface term that is *removed* rather than steepened away, and it has never been the subject of a shot |
| a high plateau | tests that the aim point is placed on the real ground rather than the mean sphere, at a place where the two differ by kilometres |
| a deliberate crest — 26.485S 68.148W | the ill-conditioned control. At rung A it should stop being ill-conditioned: 0.268 of arrival gradient against 0.1055 of terrain |

**Screen every new target before flying it**, which `tools/shot-report.py --terrain` already does for
a night's own impacts. What it cannot do is screen a target that has never been shot at, so the
campaign needs the same regression run over a *predicted* ground track — a few hundred height
queries along the last twenty kilometres, compared against `tan γ`. That is cheap, it is offline, and
it is the difference between spending a night and wasting one.

### What each rung's night looks like

`docs/SHOT-PROTOCOL.md` is the arithmetic and none of it changes. What changes is that the numbers
get smaller, and the protocol's own table says what that costs: 25 shots an arm settles a **×0.62**
ratio. That is a ratio, not a distance — which is the one piece of luck in this plan. At rung C a
×0.62 is 2 m, not 300, so the same night resolves the same *fraction* however far down the ladder it
is flown. **The protocol does not get more expensive as the misses get smaller.** It gets more
expensive only if the scatter's geometric spread widens, and a steeper arrival should narrow it —
the flown 15° shots had 0.00–0.03 km of spread against 0.07–0.14.

---

## 4. Not driving the bus into the stack it just dropped

**It has happened, once, and it was diagnosed.** 2026-08-25: a clearance latch was added on the
argument that separation only ever opens, the trim then ran on a stale reading, and the bus hit its
own spent stack — 28 seconds of thrashing and `nothing left aboard moves the bus`. The latch was
reverted (`9ef0bc0`). `Sim/SeparationClearance.cs` carries the reasoning: **the shove is the
separation, so nulling it ends it, and a gate that has once said clear cannot stay clear.**

What is *still* wrong is the other half, and `docs/MIRV-NEXT.md` item 7g names it and declines to
build it:

```csharp
if (trim.Done) _separatedFrom = null;        // Ksa/IcbmComputer.cs:982
```

Once the first trim pass reports done, the reference to the discarded stage is dropped. Every pass
after that reads `waiting to clear the spent stack, which cannot be read` and falls through to
`SeparationClearance`'s 20-second clock — which is visible in the flown log of shot 003, immediately
before the 9.71 m/s pass. **So the dangerous passes are exactly the ones flying blind**, and the
protection against hitting the stack is a timer.

Three pieces, and they are deliberately ordered diagnostic-first, per `CLAUDE.md`:

**1. Measure it, always, whether or not it is a problem.** A `Sim/ProximityWatch.cs` that takes the
separation each frame and keeps the *minimum ever reached* and the frame it happened on. It emits one
line per flight — `closest approach to the spent stack: N m at +T s, keep-out M m` — which
`tools/shot-report.py` medians per arm and shouts about when any shot goes inside the keep-out. This
is worth having on its own: the 2026-08-25 collision was inferred from a thrashing trim rather than
observed, and a shot that grazes the stack and survives currently leaves no trace at all.

**2. Keep the reference alive for the whole coast.** Delete the `trim.Done` null. This is what item 7g
says safety actually wants — "let every pass measure a real distance instead of falling back to a
clock" — and it is *not* the reverted latch: latching cached a stale **answer**, this keeps the
**question** askable. The distinction is the whole of why one is safe and one drove into a booster,
and it belongs in the commit message.

**3. Make it an interlock, not a gate.** Today `SeparationClearance` is consulted before a trim pass
starts. A pass that begins clear can still close the gap, because closing the gap is what nulling the
relative velocity *does*. So the check moves inside the thrust command: while the separation is under
the keep-out, **any commanded direction with a positive component toward the discarded stage is
refused for that frame**, and the trim spends the frame on the components that are safe. It never
stops the trim; it removes one direction from it, which on a bus with all six live is a delay rather
than a refusal.

**And it is testable headlessly, which is the point.** All three pieces are `Sim/`, so a
`ProximityTests` suite can replay shot 003's own sequence — trim to 0.023, `trim.Done`, a 9.71 m/s
demand arriving from `PostBoostAim` — and assert the bus never comes inside the keep-out. Per
`CLAUDE.md`, **check that test fails against the current code first**; if it passes against a tree
with the interlock removed, it is not testing the interlock.

---

## 5. Wall clock — flown, and the frame rate is not for sale

**Measured 2026-08-25, and it refutes this section's own proposal.** What follows is the flown
result first, because the rest of the section was written against an assumption the flight
disproved: that a shot costs 481 s because the renderer is holding the frame at 60 Hz. It is not.
The frame is spent on the CPU, and no render setting reaches it.

### What was flown

Two full MIRV shots at 26.5S 64.0W, tree at `98c3282`, against an unattended render profile:
`vsync = false` (so `VK_PRESENT_MODE_IMMEDIATE_KHR` rather than FIFO), `fpslimit = 0`, antialiasing
off, and shadows, clouds, ray tracing, ambient occlusion, bloom, godrays, stars, tessellation and
screen-space particles all off. `oceanHeightQueries` left `true`.

| | baseline (`vsync = true`, full graphics) | under the profile |
| --- | --- | --- |
| wall clock, START to END | 481 s | **532 s** and **525 s** |
| frame time through the coast | 16.5–17.4 ms, vsync-locked | **18.7–19.8 ms** |
| the shots themselves | — | PASS 6/6, mean **0.03 km**; PASS 6/6, mean **0.05 km**; spread 0.02 both |

**The profile made the shot slower.** The cap came off — frame time reached 11.0 ms median through
reentry and 8.4 ms at its fastest, which FIFO at 60 Hz cannot produce — so the setting worked and
bought nothing. Overall median 14.4 ms, p10 9.7, p90 33.3.

### Why: the game is CPU-bound during a flight, and the GPU is asleep

Sampled during the powered burn of a second shot under the same profile:

| | measured | |
| --- | --- | --- |
| GPU utilisation | **39 %** (32–44) | an RTX 3060 |
| GPU SM clock | **682 MHz** (592–840) | against ~1.78 GHz boost — it is downclocked because it is idle |
| StarMap CPU | **412 %** (206–503) | of one core, on an 8-core/16-thread i9-11900K |

And the renderer's own ceiling, measured the cheap way — same profile, idle orbital scene, KSA's
`interface.frameStatistics` read off a screenshot: **201 fps, 5 ms**. So rendering is at most a
quarter of the 19 ms frame the flight actually costs, and the other three quarters are on the CPU
where no graphics setting reaches them.

**The mod's own logging is not it either**, which was the other cheap suspect: a scenario's verbose
trace runs at **12 lines a second**, about a quarter of a line per frame.

### Three things in the engine this section had wrong

Each is in KSA's own source, and each would waste an afternoon for anyone who retries this.

- **`mode = "Borderless"` ignores `width` and `height` completely.** `GameSettings.ApplyTo` takes
  the monitor work area for that case, so a resolution drop written into the profile does nothing
  at all until the mode changes with it. Setting `Windowed` here still came up at 1920×1080;
  KSA's own `screen <w> <h>` terminal action, via `[console] onBoot`, is the path that is known to
  apply. **It was never tested at a reduced resolution**, and after the GPU numbers above there is
  no reason to: the card is idle at full resolution.
- **`fpslimit = 240` is a limit, not "already unlimited".** Zero is unlimited — `if (FrameLimit > 0)`
  in `Core/App.cs`. And there are **two** limiters: `Time.FrameLimit`, which `GameSettings.ApplyTo`
  drives from the setting, and `App.FrameLimit`, which nothing drives and which stays at its default
  **200**. That is a hard ceiling of 200 fps no setting in the file reaches, and it is exactly the
  201 fps the idle scene measured.
- **The swapchain falls back to vsync in silence.** `Renderer.CreateSwapchain` does
  `if (!supported.Contains(requested)) presentMode = FifoKHR;` with no log line, so on a surface
  where `ImmediateKHR` is unavailable a profile with `vsync = false` behaves exactly like one
  without it. It was available here; a machine where it is not would read as "the profile did
  nothing", which is also what the profile genuinely doing nothing looks like.

### And the sim-speed lever runs the wrong way

`Universe.GetJobSimStep` is `step = dtPlayer × _achievedSpeedFraction × simulationSpeed`, which is
what suggested halving `dtPlayer` and doubling `simulationSpeed` for a bit-identical step. The
third term is the problem. `Universe.ApplyVehicleSolvers` sets it to

```
min(1, 0.9 × min(dtPlayer, 1/minTargetFrameRate) / vehicleSolverTickSeconds)
```

so it is **proportional to `dtPlayer`**: a faster frame *tightens* the vehicle solver's deadline
rather than relaxing it. Halve the frame time and any stack whose solver tick exceeds `0.9 × dtPlayer`
is throttled — the step shrinks, the flight takes the same wall clock, and nothing says so. The
ceiling on buying wall clock with frame rate is therefore set by the vehicle solver, which is
already what the 412 % is being spent on.

### What survives

**The step governor in `WarpPolicy` is not worth building for this**, and Phase 0 drops it. The
saving it was meant to unlock does not exist, and the risk it carries — a control loop raising a
speed against an actuator that answers late and is shared with the player — is the shape this
repository has already been bitten by twice.

Of the original ladder, what is left is the part that was never about frame rate:

| | saving | what it costs |
| --- | --- | --- |
| **the headless rig** — 17 full MIRV flights in 9 s | ~1000× | blind to every epoch fault, wrong seven times out of seven in one session. A screen, never a verdict |
| **split arms** — odd tubes shipped, even under test, one flight | 16× | only a **per-round** term can be split, and a null is silent when the term is upstream of the release |
| **the steeper trajectory itself** | 1.4× | free — rung C's flight is 306–316 s against 486 |
| **parallel game instances** | *2–4×, unmeasured* | now the **most promising** of these: one instance holds 412 % of a 1600 % budget and the GPU is at 39 %, so the headroom is real. Separate user directories are the mechanism. Contention would show as `_achievedSpeedFraction` falling — a slower sim rather than a wrong one — and `batch.tsv` would have to record the achieved step per arm for that to be visible |
| ~~the render profile + governor~~ | **none** | flown; it cost 51 s |
| **the 27 s load and 25 s teardown** | 1.1× | a second salvo per launch changes the pick-up state, so this is the one to leave alone |

**Compounded honestly, a rung-C night is ~50 shots in 6.9 hours, not 2.2.** That is the plan's own
first stopping condition arriving: §7 says that if the governor cannot hold the step the ladder
costs 3× more nights and phases 4–6 are not affordable. It cannot, because there is no step to
hold — so **the ladder stops at rung C** until parallel instances are measured, and rungs D and E
are gated on that measurement rather than on anything in `Sim/`. *(Measured 2026-08-26 — and there is a second lever that needs no second process: see 5b.)*

**One rule that does survive unchanged.** Everything above is a change to the *conditions*, not to
the shot, so whatever a batch ends up flying under goes into `batch.tsv` beside the seed and the
DLL hash, and `shot-report.py` shouts if two arms flew different ones. The new column that matters
is the **achieved step**, because that is the one a parallel run can quietly change.

---

## 5b. Parallel instances are not the only lever — several rockets in one world, measured

**Flown 2026-08-26.** Section 5 left parallel game instances as "the only untested wall-clock lever
worth anything" and stopped the ladder at rung C on it. There is a second lever, it needs no second
process, and it is now measured.

### The instrument section 5 asked for

`Sim/SolverLoad.cs`, logged per frame. **Do not read the engine's own
`Universe.GetAchivedSpeedFraction()` as "the world is keeping up"** — it is
`0.9 x min(frameTime, 1/30)` divided by what the vehicle solve took, so once a frame is longer than
a thirtieth of a second the numerator stops growing and the fraction pins at 1.000 however far
behind the world falls. Flown with eight rockets it read **1.000 median and 1.000 worst** while the
log's own timestamps showed **10 s of world per 24 s of wall clock**. The instrument now takes both
terms from outside the engine: the simulated step delivered, against a `Stopwatch`.

### What N rockets in one world costs

Four probes, `SOLVER SCALE 1/2/4/8`, every rocket staged and flying, measured over ascent and
warhead deployment:

| rockets | vehicles | sim per wall second | throughput | worst solver tick |
| --- | --- | --- | --- | --- |
| 1 | 6 | **1.00x** | 1.0x | 2.7-2.8 ms |
| 2 | 11 | 0.89x* | 1.8x* | 5.3-6.6 ms |
| 4 | 17 | 0.80x* | 3.2x* | 10.8-11.4 ms |
| 8 | 33 | **0.40x** | **3.2x** | 12.9-20.1 ms |

*The starred rows are averages over a probe, and a probe **ramps**: a bus deploys its warheads part
way through, so most of it was flown at fewer vehicles than the row names. They overstate the rate.
The 1 and 8 rows are measured at steady full load with the corrected instrument, and 8 rockets came
out at **0.40x where the ramp-average said 0.70x**. A real nine-minute shot is mostly coast with
every warhead out, so the steady figure is the one that applies -- read the starred rows as upper
bounds and re-measure them before planning on either.

**Reason about vehicles, not rockets.** One rocket becomes six — the bus deploys six warheads —
plus spent stages, which is why eight rockets is thirty-three vehicles and why the cost climbs
during a flight rather than being fixed at launch.

**There is no cliff.** The cost starts at two rockets and rises smoothly. Eight rockets buys
**3.2x**, against ~1.9x for two game instances — and it needs no per-instance user directory, no PID-based process management and no
scheduler.

### The world advances at most a thirtieth of a second per frame, and that is the whole model

**Frame time is the cost, and nothing else is.** The simulated step is capped at 1/30 s however long
a frame takes, so once frames are slower than that:

> **sim rate = 33.3 ms / frame time**

It fits both measured points exactly:

| | frame time | predicted | measured |
| --- | --- | --- | --- |
| 1 rocket, 6 vehicles | 24.1 ms | 1.00x | **1.00x** |
| 8 rockets, 33 vehicles | 78.7 ms | 0.42x | **0.41x** |

So **throughput is bought by frame time and by nothing else**: get frames under 33 ms and eight
rockets is a straight 8x rather than 3.2x. Frame time grows at about **2.0 ms per vehicle**.

**What that 2 ms is has not been found.** The vehicle solver's tick is only 13-20 ms of the 78.7,
so roughly 60 ms is elsewhere, and two suspects are eliminated:

- **verbose logging** — cut 54x, from 7,567 lines to 138. No change to frame time or tick.
- **the warhead trace** — `ScenarioRunner` turns it on for every scripted run and it re-flies a whole
  `ImpactPredictor` trajectory per computer, so eight rockets is eight re-flights a cycle. Turned
  off: **0.36-0.43x against 0.38-0.43x.** No change.

- **drawing the predicted arcs** — `IcbmOverlay` calls `PathEcl`, which transforms the *whole*
  predicted path each frame before striding it down to 96 segments, and the path runs to thousands
  of points. Suppressed for every rocket: **0.38-0.48x, unchanged.** (The transform-then-stride is
  still wasteful and worth fixing on its own; it is simply not what bounds this.)

**And the decline during a run is the deployment ramp, not a leak.** A bus releases six warheads part
way through, so the world goes 9 vehicles to 33 mid-flight and the solver tick goes **6.4 ms to
21.9 ms** with it. It plateaus at 33.

What is left is engine-side per-vehicle work — `PrepareVehicleWorkers` is serial per vehicle, and
`VehicleUpdateTask` has three more per-vehicle loops on the main thread after the parallel apply —
or rendering 33 part trees. **That wants a profiler rather than another guess**, and it is the one
thing standing between 3.2x and 8x.

**And the engine's frame-rate counter is not the thing to watch either** — turning rendering
features *down* took the observed rate from 20 fps to 14, exactly as section 5 measured. What
matters is the frame time above, which is CPU.

### The spent stages were most of the cost, and they can be taken out — flown 2026-08-27

**Three of the six vehicles a shot creates are ascent stages**, and nothing reads them once they are
dropped. `Config.DisposeSpentStages` takes them out of the world past a kilometre. Flown, one
rocket:

| | vehicles just before the split | START to verdict |
| --- | --- | --- |
| disposal off | **5** | 588 s |
| **disposal on** | **2** | **528 s** |

A tenth of the wall clock on a *single* rocket, where the vehicle count was never the binding cost.
The lever is at N: eight rockets shed twenty-four stages, and at the measured ~2.0 ms per vehicle
that is roughly 48 ms off a 78.7 ms frame — which is the whole distance between the 3.2x above and
the straight 8x that "get frames under 33 ms" promises. **Not yet measured at N**, which is the next
thing to fly.

**The half a MIRV bus drops is never taken**, whatever the setting says: `SeparationClearance` reads
an unreadable distance as a blind clock rather than as clearance, so removing the stack would
authorise the trim while the bus is still metres from it. That is why this is three of four rather
than four of four. `Sim/StageDisposal.cs` holds the rules.

### And several rockets have to be launched together

`WarpPolicy` is session-wide — it holds timewarp down while *anything* is in the air. A shot spends
245 s of wall at 1x on ascent and then compresses ~25 minutes of coast into 210 s at 100x, so two
rockets flying out of phase means one's falling warheads pin the world at low warp while the other
is trying to coast, and the flights serialise rather than overlap. Launching them in the same frame
is a requirement of the design rather than a convenience, and it is not one of the four traps below.

### What it does not yet say

The probes cover the first ~2.5 minutes of each flight — ascent through deployment. A real shot is
nine minutes and mostly coast, where warheads are cheap, so 5.6x is likely conservative. And nothing
here measures instances and rockets together; they spend from one CPU budget and the product is
bounded by it.

### Four traps, each of which cost a probe

The mod already crews one `IcbmComputer` per craft, so N guided rockets is the existing architecture.
The harness is what fought back:

1. **`BallisticScenario.Find` latches the first computer and returns**, leaving the rest on the pad —
   where a landed vehicle takes the cheap analytic path and costs almost nothing. A sweep run that
   way measures one flying rocket and some furniture, and reports the idea as free.
2. **`FindDefendedSite` returns the first non-launching crewed craft**, which with the defence site
   removed from the save is *another rocket*.
3. **An explicit `--aim` then moves that craft to the aim point.** The nominated rocket was
   teleported 6,269 km and dropped at 5 km altitude under power. It never launched; it was thrown.
4. **Arming is not enough.** A computer with no `Designate` has no solution and never leaves the pad.

The defence site in the save is not decoration: it is what the target finder points at. Keep it.

## 6. What a human still has to do

The answer today is: put a rocket on the pad, start the batch, read the table in the morning. Two of
those three are already irreducible for good reasons and one is not.

**Irreducible, and stated in `CLAUDE.md`.** A mod cannot place a craft through the system XML —
`LoadVehicleFromLibrary` resolves through `DefaultVehicleSaves`, whose `SaveFolderPath` is hardcoded
under the game install. So the *first* craft is the operator's.

**Not irreducible, and this plan needs it gone.** Everything *after* the craft exists:

- **The orbit.** `Vehicle.Teleport(Orbit?, doubleQuat?, double3?)` and
  `Orbit.CreateFromStateCci(parent, time, positionCci, velocityCci, colour)` are both public. Given
  one craft in one save, the harness can put it on any altitude, inclination and phase the matrix
  asks for. This is the one new capability the campaign genuinely requires, and it collapses "nine
  orbital geometries" from nine saves somebody has to build into nine lines in a batch file.
- **The target.** Already solved and worth not rebuilding: `Ksa/Ui/UiIcbm.cs` takes a latitude and
  longitude to seven decimals, and `scenario.sh mirv:26.5S,64.0W` drives it. The pixel-picking term
  in `docs/KINETIC-FLOOR.md` — 0.2 m to 1.9 km — applies to a player with a mouse, not to the
  campaign.
- **The verdict.** `shot-report.py` already prints the rank test, the Hodges–Lehmann ratio with an
  interval, the per-arm terrain conditioning and the binary hashes. What it needs is one more column
  per §4 — the closest approach to the spent stack — and one more per §5 — the step actually
  achieved, so a night that quietly ran at a different fidelity says so rather than being read as a
  regression.

**Which leaves exactly two moments that want a person**, and they should be the only two:

1. Once, at the start: a craft with a MIRV bus, saved.
2. Once per campaign phase: reading what the last phase settled and choosing the next arm — which is
   a judgement about what to spend a night on, and is not a thing to automate.

Everything between them is `shot-batch.sh --orbits <matrix> --arms <...> --blocks <n>` and a
`shot-report.py` in the morning. And per the batch protocol's own hard-won rule, the batch builds and
stashes every arm **before the first shot flies**, so the tree is free all night and nothing done to
it can reach a shot in flight.

---

## 7. The sequence, with the gate on each phase

Each phase ends on a number, and the number decides whether the next one is worth flying. No phase
starts until the one above it has passed, because every price below is quoted at an arrival angle the
shot has to be able to hold first.

| # | phase | build | gate |
| --- | --- | --- | --- |
| **0** | **Instrument and de-risk.** §4's proximity watch and interlock; the orbit matrix from §6. ~~§5's render profile and step governor~~ — **flown and dropped**, see §5. | `Sim/ProximityWatch.cs`, the `trim.Done` deletion, `Vehicle.Teleport` in the harness | the suite is green, the closest-approach line appears in every log, and one shot flies unattended onto a placed orbit |
| **1** | **Rung A — make the steep arrival payable.** B1: the stall test that separates a dead axis from a slow one; the runaway guard as a rate. | `Sim/BusTrim.cs` | a 15° shot pays its post-boost demand in full; group inside **150 m**; 25 shots an arm against a 15° control |
| **2** | **Rung B — one row steeper, nothing new.** | none expected | 20°, group inside **60 m**. If it needs new code, phase 1 was not finished |
| **3** | **Rung C — the trim band and the release.** B2's quantum measurement, then the band; B5's per-tube release impulse (screenable on one flight). | `Sim/BusTrim.cs`, `Sim/ImpactPredictor.cs`, `Sim/IcbmConfig.cs` | 45–60°, group inside **15 m**, spread inside 5 m |
| **4** | **Rung D — the round itself.** B3's 1 ms sub-step **paired** with the gravity re-read; ground per sub-step. Screened as split arms first, flown as one arm. | `Sim/Arsenal.cs`, `Sim/Slug.cs` | 60–88°, group inside **3 m** |
| **5** | **Re-ask B4 where it now lives.** The aim loop's constants, re-measured at the rung the shot actually flies, starting rough. | `Sim/AimCorrection.cs`, `Sim/PostBoostAim.cs` | whatever is left above **1 m**, or the finding that nothing is |
| **6** | **Rung E — take it back out to range.** The lofted 20° intercontinental shot, with everything C and D bought. | none expected | 2,000 km, group inside **20 m** |

**Two things that would end the plan early, and both are results rather than failures. The first has
now happened.** §5's governor was to be the thing that made phases 4–6 affordable, and the flight
says there is no step to buy: the game is CPU-bound and the frame rate is not for sale. So the
ladder **stops at rung C** — phases 4, 5 and 6 stay written down and are gated on parallel game
instances being measured, which is now the only untested wall-clock lever worth anything. The
second is still open: if B2's measurement says 0.02 m/s *is* the bus's impulse quantum, then rung D
needs a smaller nozzle before it needs anything in `Sim/`, which is a model change and a different
kind of afternoon.

**What this plan does not promise.** Half a metre, ever — §B6's irreducibles are 0.32 m in quadrature
and the height texture is RocketWerkz's. Metre-level *and* intercontinental range from one pass —
§1's ladder says rung E is 20 m, not 1. And any of it before it has been flown: seven headless
improvements were argued from the code and refused by flight in one session, and this document is
currently in exactly that category.
