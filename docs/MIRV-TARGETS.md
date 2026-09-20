# Six warheads, six targets

**Mostly a plan, and no longer entirely.** The `Sim/` half of phase 1 and all of phase 2's maths are
built and tested on `arm/mirv-targets` — `TargetSet` is the list of up to six targets with their warhead
counts and the release plan a flight would read, and `DivertFootprint` is the reach on the ground, taken
off the sensitivity columns `ReleaseFocus.FlownSensitivity` already flies once per salvo, so the display
costs no flying of its own. **Nothing is reachable in game**: no designator places a second target, no
panel lists them, nothing is drawn, and the flight still sends all six warheads to one place.
**Phase 0 has been flown headlessly**
(`tests/KSArmory.Tests/MirvDivertTests.cs`), so the numbers below are measured rather than estimated —
and it moved three of the plan's decisions, each marked **priced** where it appears. What it did not
answer is marked *open*.

**And one thing phase 0 itself got wrong, found 2026-09-18 while designing phase 3.** Every divert figure
below was priced with the **arrival clock free**, and the flight pins it: `IcbmProgram` latches the arrival
at closed-loop handover and `ResolveCoastArc` solves every later arc to that same instant. Pinned, a divert
is an exactly-determined 3x3 rather than a minimum-norm 2x3, and the penalty is **entirely along-track** —
**2.56x at 6,179 km and 11.94x at 12,902**, against **1.00x across the track at every geometry**, because
moving a target across the track does not change when it is reached. Along-track is what made the footprint
an ellipse worth drawing, so **pinned, the reach is much smaller and close to circular**.

**And those two penalties are *cutoff* figures, not universal ones.** At the release gate the 6,179 km
penalty is 1.94x rather than 2.56x. Read every along-track number below as the free-clock best case.

**Pinned, the footprint is a circle**, radius the cross-track reach, about `0.96 · t_go` metres per m/s
at every geometry and epoch measured — along and across come out within 3% of each other at the release
gate. So `DivertFootprint.OrientationRad` is meaningless pinned, and **`DivertFootprint.TryFrom`
computes only the free-clock ellipse**: drawing it as-is would show a region 2–12x too long. A pinned
mode is a prerequisite for phase 2, not a refinement of it.

**The decision this doc framed as "re-commit the arrival or ship a smaller reach" was the wrong one,
because the epoch dominates the latch.** Derived off the same columns `MirvDivertTests` prices (and
reproducing its flown 6,179 km case exactly), six hops of `BusTrim.MaxMetresPerSecond` 65 s apart:

| | from cutoff | from the 420 s gate |
| --- | --- | --- |
| hop 1 | 10.7 x 9.6 km | 3.5 x 3.5 km |
| hop 3 | 10.1 x 9.3 | 2.3 x 2.3 |
| hop 6 | 8.9 x 8.5 | **0.4 x 0.4** |

The Mk 21's `Warhead.LethalRadius` is **2.0 km**. **At today's gate, targets 4 to 6 land inside each
other's lethal circles** — that is option C wearing this feature's name. Started at cutoff it is a real
feature: 9–11 km hops and a ~58 km chain. **Moving the loop earlier is worth 3.0x where freeing the
clock is worth 2.56x**, and cutoff-pinned (1,074 m per m/s) beats gate-free (688) — so the cheap config
change beats the expensive guidance work, and pinning is affordable if the loop starts early.

**So: ship pinned, and make the gate a function of the set** — `ReleaseBeforeArrivalSeconds + (N-1) x 65 s`,
which leaves the last release where it is today and makes a set of one byte-identical to today's flight.
How far early is a paired-arm question, not a design one.

**What (a) would take, corrected.** The un-latch **does** exist: `IcbmProgram.ReleaseArrival()` is called
during coast from `IcbmComputer.ReleaseAnArrivalTheTrimCannotFly`. What is missing is the *commit* —
`Update` returns `Coasting` before `Resolve`, so the latch block never runs in coast, and the only
coast-time solve, `ResolveCoastArc`, is pinned by construction. A `RetargetCoast` would have to write
`_arrivalFromLaunch`, `Arc`, `ReferencePositionCci` and `SecondsSinceReference`, then `BusTrim.Begin()`
again. Downstream that assumes the arrival never moves: the release gate's `toArrival` test,
`AimCorrection.IsSteady` (which is what commits it), `IcbmComputer`'s `_releasedTheArrival` once-per-flight
latch, and `PostBoostAim`'s per-flight `Cycles`/`MaxSeconds`. **The real risk is that pinning during an
aim loop destabilises it on a shallow arrival** — the reason the latch is where it is — in the phase
where the trim is the actuator.

The three that moved, in one place:

* **The reach is set by the release epoch, not the range.** `IcbmConfig.ReleaseBeforeArrivalSeconds`
  holds the warheads until 420 s before arrival, and the divert bought there has a quarter of the
  leverage it has at cutoff. The plan's table was cutoff sensitivities against a release-epoch cross
  figure, which is why its ellipse looked 7:1 when at one epoch it is 2:1.
* **A bus's own warheads cannot kill each other**, so the arrival stagger this plan was built around
  is unnecessary — and it costs half the divert budget per pair.
* **Holding cost falls as the bus descends.** The later targets are not the dearer ones; they are the
  ones with less reach. Order the set by reach, far first.

## What the player gets

A MIRV rocket carries six reentry vehicles. Today all six go to one designation and land within a few
millimetres of each other (`docs/ACCURACY-PLAN.md` 3en). This plan lets the player pick **up to six
targets**, and flies the bus between releases so each warhead falls on its own.

From the player's side:

1. Turn on **Designate by clicking the world**, as today.
2. The ground shows the area this rocket can reach. Hovering outside it greys the cursor ring and says
   **outside reach**; a click there does nothing.
3. The first click places target 1. **Before launch that is the whole display** — the bus's own reach
   cannot honestly be drawn from the pad, because its long axis depends on the arc actually flown and the
   pad does not know it yet. Targets 2–6 are placed **during the coast**, which is when the footprint is
   real, and it shrinks with each one added.
4. The panel lists the targets, how many warheads each gets, and whether the whole set fits the bus's
   fuel and the time left before reentry. Targets can be removed; removing one gives its reach back.
5. Launch as today. The rocket flies to target 1's trajectory, and after separation the bus releases,
   re-aims, releases, and so on until every warhead is away.

## Why the reach area is an ellipse, and why it changes

There are two different reaches, and the doc keeps them apart because they are computed differently and
limited by different things.

**Target 1 is the missile's reach.** The booster decides it: whether a trajectory exists to that point that
the stack can pay for, arrives steeply enough (`IcbmConfig.MinArrivalAngleDeg`, `ArrivalPreference`), and
fits a burn window. The computer already answers this question for one point — `IcbmReach` is `Reachable`,
`ShortOfPropellant`, `NoTrajectory` or `TooShallow` — so the region is that answer asked along bearings
from the launch site until it changes.

**Targets 2–6 are the bus's divert footprint.** After separation the only engine left is the bus's attitude
and translation thrusters (`BusTrim`), so every later target is a place the bus can move its own trajectory
to with the propellant it has left. That is small and lopsided, and **how small depends on when the loop
runs rather than on how far the shot goes**.

**Priced.** Metres of landing per m/s of divert, in the ground frame, at the two epochs a release loop
could run in:

| | 2,000 km (19.8°) | 6,179 km (32.9°) | 12,902 km (9.1°) |
| --- | --- | --- | --- |
| **at today's release gate** (~360 s to go), along / across | 1,135 / 349 | **688 / 355** | 2,860 / 348 |
| 50 m/s reaches | 57 × 17 km | **34 × 18 km** | 143 × 17 km |
| **at cutoff**, along / across | 1,360 / 407 | **2,755 / 961** | 19,477 / 764 |
| 50 m/s reaches | 68 × 20 km | **138 × 48 km** | 974 × 38 km |

**The aspect ratio belongs to the arrival angle, not the range**: the major axis is about
`450 · cot γ` metres per m/s at 365 s to go across all three geometries, and the minor about `0.96 · t_go`.
So it is 25:1 at 9°, 3:1 at 20°, 2:1 at 33° — the ellipse has to be computed per shot and cannot be a
typed shape. First order holds well enough to draw with: flown against linear at 40 m/s it is off by
−1.1% at 6,179 km and +4.8% on the shallow 12,902 km major, and the minor axis is exact.

**The propellant, read rather than estimated**: the bus tank holds **134.3 kg** of MMH/NTO (`51.6452` kg
of MMH at a mass fraction of `0.3846`, straight out of the save), and KSA's own design chain gives an
exhaust velocity of 2,906 m/s. That is **143 m/s fore and aft, 101 on one lateral axis, and 72 across
both** — `BusTrim` fires one direction at a time, so a two-axis divert pays the L1 norm. Against that,
`PostBoostAim.MaxTrimMetresPerSecond` is 60 and a flown single-target shot spends a median 16.1 m/s
(p90 17.9, worst 21.8). **The guidance cap binds; the tank does not**, and 30–45 m/s is left to divert
with under the cap against 56–127 in the tank.

One piece of luck: the bus's nose sits about 113° from the platform's track at release, so an
along-track divert is largely an *axial* burn — the cheap 143 m/s direction — while cross-track is
purely lateral. Cross-track is ~3x dearer in geometry and another 1.4x in propellant: about **4:1** in
what a player actually spends.

**It shrinks as targets are added, because divert is spent in sequence.** Going from target *i* to target
*i+1* costs roughly the velocity change that moves the landing between them. The reach for the next target is
whatever is left after the ones already chosen, taken along the cheapest order through them. Six points have
720 orders, so the cheapest is found by trying them all.

### How it is drawn

* A **ground outline**, draped on the terrain like the existing aim ring (`IcbmOverlay`), in the designation
  colour: the missile's reach before target 1, the bus footprint after.
* **Drawn at the ceiling the loop can actually spend**, which is `BusTrim.MaxMetresPerSecond` — 10 m/s, not
  the 30–45 the budget suggests. At 6,179 km that is about **6.9 x 3.6 km** free-clock, and less pinned.
  Drawing the larger region first and shrinking it when phase 3 raises the per-pass ceiling is the wrong
  order.
* **Before launch, only the missile's reach.** The bus footprint's long axis is `450 · cot γ`, and γ belongs
  to the arc actually flown rather than the cheapest arc from the pad — measured against three flown
  geometries the pad's estimate is out by **1.04x, 0.51x and 0.59x**, while the short axis is exact. So
  there is no honest boundary to draw on the ground before the burn is over, and the footprint belongs to
  the coast, which is also the only time targets 2–6 are being placed.
* **In orbit, nothing.** Reach from orbit is a band around the ground track over sixteen revolutions rather
  than a region around a point, the vehicle is not inside it, and an inclination bounds it in a way no
  bearing sweep can find. It is also unaffordable: `BurnWindow.TryFind` is 7.1 ms from a 400 km orbit, so a
  usable ring is **2.6 s**. Say so and draw nothing.
* **Recomputed only when the set of targets changes**, never per frame. A pad ring is **37–68 ms** with the
  flight time seeded across the sweep and 112 ms without, so it is built a few bearings a frame rather than
  in one hitch — `IcbmOverlay.Draw` already costs 11.1 ms of a 15.1 ms mod frame. Target 1's region is a few dozen
  reach solves along bearings, cached until the rocket moves. The footprint is cheap, and **cheaper than
  this plan assumed: the Jacobian already exists.** `ReleaseFocus.FlownSensitivity` builds exactly these
  velocity columns through the air, once per salvo, so the ellipse is the two singular values of its
  velocity block projected onto the ground frame — phase 2 costs no new flying. Its edge is still worth
  checking with a real solve on a long shallow shot, where first order is 5% out.
* The **cursor ring** turns `SiteDesignator`'s refused grey outside the region, with **outside reach** beside
  it; a click is ignored. Inside, it shows what adding this target would leave: "3 targets, 12 m/s left".
* **Existing targets** are numbered rings. Clicking one selects it in the panel.

## What to do with warheads when fewer than six targets are picked

**Priced, and it inverts this section.** The blast sweep runs over rounds in the air, which is why
`Sim/AimSpread.cs` exists — the first group down at 229 m took thirty warheads of five *other* rockets
with it. But `WeaponSystem.FillIncoming` filters a system's **own** rounds out of the airborne list
before that sweep, so **a bus's warheads cannot destroy one another**. Flown: `GeoSat FAT_1`'s six land
0.02–0.13 s apart — 640 m apart at 4,923 m/s, well inside a 2 km lethal radius — and all six are recorded
as landing, with zero `intercepted` lines in the whole night. `AimSpread`'s fratricide is between
*rockets*, which is exactly what it says.

**The filter is load-bearing here and was not written for this.** Its comment says a launcher must not
shoot down its own missiles as they leave the tubes — it fills `_incoming` for the *radar*, and the splash
sweep at `WeaponSystem.cs:3399` walks that same list. So this plan depends on a list built for targeting
also being the one the blast reads, which is true today and is nobody's stated invariant. That is what A'
is for.

So **"put the spare ones on the same target" is free today**: release them together, as the salvo
already does. Everything below about staggering is kept because it is what a stagger would cost if that
filter ever changed — and the number says to buy the separation **on the ground, not on the clock**.
At 6,179 km from the release gate, 0.5 s of stagger costs 8.33 m/s and 2 s costs 33.30 — half the whole
budget, per pair — where putting the second warhead 2.0 km clear along the ground costs **2.91 m/s**.

Options:

| | What happens | For | Against |
| --- | --- | --- | --- |
| **A. Spread across the targets, together** | Spare warheads go to the chosen targets; those sharing a target are released together, as the salvo already does | Every warhead is used, and it costs nothing at all | Nothing, while a system's own rounds are outside its blast sweep |
| A'. The same, separated on the ground | As A, but the extra warheads on a target are aimed 2 km clear of it | Survives a future change to that filter | 2.91 m/s a pair, and they do not land on the target |
| B. Keep them aboard | Spare warheads are not released | Simplest | They ride the bus down and are lost with it; the player gets less than they launched |
| C. A pattern around each target | Spare warheads land around each target, spaced beyond the lethal radius | Area coverage | They do not land on the target, which is not what "target" means |
| D. All spare ones on the last target | | Simple | The last target has the least reach left, so it is A with the split backwards |

**Recommended: A, with the counts editable** — and A' is the fallback if the blast sweep ever stops
filtering a system's own rounds, since the whole cost of A is that one line in `FillIncoming`.

* **Default split**: all six spread evenly over the chosen targets, the remainder to the earliest-chosen
  (2 targets: 3 and 3; 4 targets: 2, 2, 1, 1).
* **Per-target count** in the panel, 1–6, total at most six. Warheads not assigned anywhere are **kept aboard**
  (option B) and the panel says so, so B stays available to a player who wants it.
* **No stagger.** It was this plan's mechanism for "two on one" and phase 0 retired it: the warheads
  cannot hurt each other, and a stagger is the dearest way to buy a separation that is not needed — 8.33 m/s
  for half a second against 2.91 m/s for 2 km of ground. Worth keeping in mind for *why*: a round released
  later from a coasting bus shares its conic and arrives at the same instant, so a later arrival is a
  different trajectory rather than a later release, and trajectories are what cost.
* **Option C** as a later, separate feature if it is wanted: an "area" target with a radius.

## How it flies

Today, after the burn: separate the spent stack, clear it, trim the bus onto the target's trajectory, run the
aim correction, release all six ~22 ms apart (`ReleaseSequence`), each with its separation kick (`ReleaseFocus`).

With several targets, the release step becomes a loop:

1. **Order** the targets by the cheapest divert sequence (the same order the reach display used).
2. **For each target in order**: solve the trajectory to it, trim the bus onto it (`BusTrim` against that
   solution), run the aim correction (`PostBoostAim`), release that target's warheads — all of them, together
   — with their kicks, and record which tube went where. **`BusTrim.MaxMetresPerSecond` is 10 per solve and
   will refuse a bigger hop**, so the loop has to raise that ceiling deliberately rather than discover it.
3. **Stop** when every assigned warhead is away, or when the time or divert runs out — and say which.

What already exists and is reused: the trajectory solve (`BallisticArc`, `Lambert`), the trim (`BusTrim`),
the correction loop (`PostBoostAim`, `AimCorrection`), the release and kick, the trace. What is new is the loop
around them and the bookkeeping of which warhead belongs to which target.

### Two budgets bound it

* **Divert**: the sum over the sequence. Checked at designation, shown in the panel, re-checked in flight
  against what the trim actually spent.
* **Time — priced.** Over 96 flown buses a re-aim is a median **65.1 s** (p10 58.6, p90 73.2, worst 80.3):
  4 s of clearance, ~16 s of separation null, and a median four correction passes at ~11 s each. A divert burn
  adds its own at ~0.3–0.4 m/s² of Euclidean gain, so 10 m/s is ~30 s and 20 m/s ~55 s. Counting only the coast
  above the 100 km deploy floor:

  | | above 100 km | re-aims that fit | realistic targets |
  | --- | --- | --- | --- |
  | 2,000 km, from cutoff | 351 s | 5 | **3** |
  | 6,179 km, from cutoff | 1,315 s | 20 | **6, comfortably** |
  | 6,179 km, from today's release gate | 334 s | 5 | **3**, and the last two have almost no leverage |
  | 12,902 km, from cutoff | 1,772 s | 27 | **6, comfortably** |

  **The wall clock is the cost nobody has paid yet**: corrections cannot run under warp
  (`SteadyBeforeReleaseSeconds`), so six re-aims mean ~400 s of near-1x flight a shot against today's ~65 —
  **a night of six-target shots runs about three times as long as a night of these**.

### Accuracy per target

Each target gets its own full correction, so each should land as the single-target shot does today.

**Priced, and this plan had it backwards.** Holding cost *falls* as the bus descends — measured with the
flight's own `HoldingCost.TryMeasure` at 65 s slots, 1.14 → 1.00 → 0.92 m/s at 6,179 km from cutoff — and a
warhead that gets a fresh correction before release loses nothing by having waited, because the holding cost
is the staleness rate *within* a cycle rather than a debt that accrues. The later warheads in fact land
*better*: the trim's 0.02 m/s stop band is worth 14 m on the ground at the release gate against 55 m at
cutoff.

**What a later target actually loses is reach**, and steeply. At 6,179 km, 50 km of along-track divert costs
18.2 m/s bought at cutoff, 26.7 at +400 s, 47.8 at +800 and **72.5 at today's gate**. So the order is by
reach, far first — not by which target is "hardest".

## What a flight has to show

* **Scenario**: `tools/scenario.sh mirv:<lat>,<lon>;<lat>,<lon>;...` with per-target counts, loading its own
  save as today (`KSARMORY_SCENARIO_SAVE`).
* **Scoring**: each warhead against **its own** target, not the group's. The report prints per-target misses,
  which warheads went where, arrival spacing on shared targets, divert spent against budget, and the time from
  separation to the last release.
* **Fratricide check**: every assigned warhead arrives and detonates; none is recorded as destroyed in the air.
* **Nights**: the current single-target build against the loop with one target (they must match — the loop
  must cost nothing when it has one target), then 2, 4 and 6 targets.

## Phases

| | What | Flies anything? |
| --- | --- | --- |
| ~~0~~ | **Done** — `tests/KSArmory.Tests/MirvDivertTests.cs`. ±100 km is real **only from cutoff**; at today's release gate the footprint is a 34 × 18 km box. See the three findings at the top. | No |
| 1 | **Targets as data**. `Sim/TargetSet.cs` **done** on `arm/mirv-targets` with `ShotRequest` able to name several; what remains is Ksa-facing — hold the list on `IcbmComputer`, let `SiteDesignator` place more than one, and draw the panel list with add/remove/counts. The flight still sends everything to target 1. | Unchanged |
| 2 | **Reach display**. `Sim/DivertFootprint.cs` **done** — the ellipse, the cost of a displacement and whether a budget reaches it. What remains is drawing it: the missile region before target 1, the footprint after, cursor refusal outside it, the panel's divert and time readout. | No |
| 3 | **The release loop**: re-aim per target, per-warhead target bookkeeping in the log, and the per-pass trim ceiling raised deliberately. Shared targets release together. | Yes |
| 4 | **Instruments and nights**: per-target scoring in `shot-report.py`, the matching check with one target, then 2/4/6. | Yes |
| 5 | **Later**: area targets (option C), saving the target list with the craft, reordering by hand. | — |

**A budget problem phase 3 has to solve.** Five hops at `BusTrim.MaxMetresPerSecond` plus the 16.1 m/s a
single-target flight already spends is **66 m/s against `PostBoostAim.MaxTrimMetresPerSecond` = 60**, so
that cap moves or the last warhead gets no divert. The tank is not the binding constraint (143/101/72 kg).

**Two gates sit in front of phase 3, and neither is code.**

**The arrival decision, and it sizes the whole feature.** Priced free-clock, the reach is a long ellipse;
pinned — which is what the flight actually does — it is small and nearly circular, because the penalty is
entirely along-track and runs 2.56x at 6,179 km to 11.94x at 12,902. Either the release loop re-commits
the arrival per destination, which needs a re-latch after cutoff that does not exist today, or the
feature advertises the smaller reach. **Everything drawn in phase 2 is the wrong size until this is
settled**, so it is the first thing to decide rather than the last.

**And the trim has to stop failing first.** Phase 3 runs the post-cutoff trim once per target where today
it runs once. At 12,902 km that trim currently gives up on **24 of 80 flights**, and each give-up
forfeits the whole aim correction and costs about 2 km — `docs/ACCURACY-PLAN.md` 3fg. Six diverts on a
loop that fails three times in ten is six chances to lose the shot. `IcbmConfig.StallFallsBackToHolding`
is the candidate fix, merged off and unflown; `~/shots/scripts-2026-09-18/DECLARE-fallback.md` is the
night that decides it. **That night is not part of this plan — it is the accuracy thread — but it is
upstream of phase 3.**

## Open questions

* ~~The bus's propellant.~~ **Answered: 134.3 kg, and the 60 m/s guidance cap binds well before the tank
  does.** What is still unread is the *attitude-hold* propellant across a six-target coast, which is
  uncounted, and whether the 2,906 m/s exhaust velocity — computed through KSA's design chain, not observed —
  holds in flight. Watch tank mass against `SpentMetresPerSecond` on the first flight that burns for real.
* **When the release loop runs is the design decision, not a tuning.** `ReleaseBeforeArrivalSeconds = 420` is
  a single-target optimisation: it shrinks the ejection kick's leverage, which is why the group lands
  millimetres from the aim. For six targets it costs **4x the reach at 6,179 km and 7x at 12,902**. Moving it
  multiplies every velocity residual by the same factor — the trim's floor goes 14 m → 55 m — which against a
  2 km lethal radius is nothing and against the batch scoring is everything. **A paired-arm question before
  phase 3, not an assumption.**
* **Does the first target stay special?** Here the booster flies to target 1 and the bus diverts to the rest.
  Aiming the booster at the middle of the set instead halves the largest divert, but makes the first release a
  divert too.
* **Release before or after the aim correction for target 1?** Today the correction runs once. With several
  targets it runs once per target; whether the first one's can be shortened is a phase 0 question.
* **What the player sees in flight**: which target the bus is currently aimed at, and a countdown per target.
