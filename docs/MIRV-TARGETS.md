# Six warheads, six targets

**A plan, not a record.** Nothing here is built. **Phase 0 has been flown headlessly**
(`tests/KSArmory.Tests/MirvDivertTests.cs`), so the numbers below are measured rather than estimated —
and it moved three of the plan's decisions, each marked **priced** where it appears. What it did not
answer is marked *open*.

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
3. The first click places target 1. The area then shrinks to what the bus can still reach from there,
   and it shrinks again with every target added.
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
* **Recomputed only when the set of targets changes**, never per frame. Target 1's region is a few dozen
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
| 1 | **Targets as data**: a list of up to six `AimSite`s with counts on `IcbmComputer`; panel list with add/remove/counts; the scenario can name several. The flight still sends everything to target 1. | Unchanged |
| 2 | **Reach display**: the missile region before target 1, the footprint after, cursor refusal outside it, the panel's divert and time readout. | No |
| 3 | **The release loop**: re-aim per target, per-warhead target bookkeeping in the log, and the per-pass trim ceiling raised deliberately. Shared targets release together. | Yes |
| 4 | **Instruments and nights**: per-target scoring in `shot-report.py`, the matching check with one target, then 2/4/6. | Yes |
| 5 | **Later**: area targets (option C), saving the target list with the craft, reordering by hand. | — |

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
