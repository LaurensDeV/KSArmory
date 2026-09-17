# Six warheads, six targets

**A plan, not a record.** Nothing here is built. Every number marked *estimate* is to be priced
headlessly in phase 0 before anything is built on it.

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
to with the propellant it has left. That is small and lopsided:

| per m/s of divert, *estimate* | along the flight | across it |
| --- | --- | --- |
| 1,365 km shot | ~0.27 km | less |
| 6,179 km shot | ~2–3 km | ~0.4 km |
| 8,551 km shot | ~4.7 km | ~0.5 km |

(`docs/ICBM-GUIDANCE.md` gives the along-track figures; the across figure is the release kick's own
sensitivity, 2.3 mm/s for 0.86 m.) The trim is capped at 60 m/s and a flown trim spends 12–23 m/s of it, so
something like **30–45 m/s** is left to divert with — *estimate, and the bus's real propellant has to be
measured*. At 6,179 km that is on the order of **±100 km along the flight and ±15 km across it**: an ellipse,
not a circle, stretched along the ground track.

**It shrinks as targets are added, because divert is spent in sequence.** Going from target *i* to target
*i+1* costs roughly the velocity change that moves the landing between them. The reach for the next target is
whatever is left after the ones already chosen, taken along the cheapest order through them. Six points have
720 orders, so the cheapest is found by trying them all.

### How it is drawn

* A **ground outline**, draped on the terrain like the existing aim ring (`IcbmOverlay`), in the designation
  colour: the missile's reach before target 1, the bus footprint after.
* **Recomputed only when the set of targets changes**, never per frame. Target 1's region is a few dozen
  reach solves along bearings, cached until the rocket moves. The footprint is cheap: to first order it is the
  release sensitivity matrix (the same columns `ReleaseFocus` builds) applied to a sphere of the remaining
  divert, which is exactly an ellipse. Its edge is then checked with a real trajectory solve at a few points,
  because at tens of kilometres the first-order ellipse is not exact.
* The **cursor ring** turns `SiteDesignator`'s refused grey outside the region, with **outside reach** beside
  it; a click is ignored. Inside, it shows what adding this target would leave: "3 targets, 12 m/s left".
* **Existing targets** are numbered rings. Clicking one selects it in the panel.

## What to do with warheads when fewer than six targets are picked

**A warhead's burst destroys any warhead near it at that moment.** The blast sweep runs over rounds in the air
(`Sim/AimSpread.cs`: the first group down at 229 m took thirty warheads of five other rockets with it). With
millimetre accuracy, two warheads sent to the same point on the same trajectory arrive about 20 ms apart,
metres from each other, and only the first detonation counts. So "put the spare ones on the same target" has to
be designed rather than assumed.

Options:

| | What happens | For | Against |
| --- | --- | --- | --- |
| **A. Spread across the targets, staggered in time** | Spare warheads go to the chosen targets, the extra ones on each target timed to arrive after the previous burst | Every warhead is used; this is how "two on one" is actually done | Needs an arrival-time offset, which costs a little divert |
| B. Keep them aboard | Spare warheads are not released | Simplest | They ride the bus down and are lost with it; the player gets less than they launched |
| C. A pattern around each target | Spare warheads land around each target, spaced beyond the lethal radius | Area coverage | They do not land on the target, which is not what "target" means |
| D. All spare ones on the last target | | Simple | Fratricide unless staggered anyway, so it is A with a worse split |

**Recommended: A, with the counts editable.**

* **Default split**: all six spread evenly over the chosen targets, the remainder to the earliest-chosen
  (2 targets: 3 and 3; 4 targets: 2, 2, 1, 1).
* **Per-target count** in the panel, 1–6, total at most six. Warheads not assigned anywhere are **kept aboard**
  (option B) and the panel says so, so B stays available to a player who wants it.
* **Stagger**: warheads sharing a target arrive a fixed interval apart. The interval has to put the next
  warhead outside the lethal radius at the previous burst: 2.0 km for the 20 kt Mk 21 at ~2.8–5 km/s is under a
  second, so **2 s** with margin — *estimate*, and derived from `Warhead.LethalRadius` and the arrival speed
  rather than typed. A later arrival needs a slightly different trajectory, not just a later release — a round
  released later from a coasting bus shares its conic and arrives at the same instant — so each stagger is a
  small divert, priced like any other.
* **Option C** as a later, separate feature if it is wanted: an "area" target with a radius.

## How it flies

Today, after the burn: separate the spent stack, clear it, trim the bus onto the target's trajectory, run the
aim correction, release all six ~22 ms apart (`ReleaseSequence`), each with its separation kick (`ReleaseFocus`).

With several targets, the release step becomes a loop:

1. **Order** the targets by the cheapest divert sequence (the same order the reach display used).
2. **For each target in order**: solve the trajectory to it (and its arrival time, for staggered warheads),
   trim the bus onto it (`BusTrim` against that solution), run the aim correction (`PostBoostAim`), release
   that target's warheads with their kicks, record which tube went where.
3. **Stop** when every assigned warhead is away, or when the time or divert runs out — and say which.

What already exists and is reused: the trajectory solve (`BallisticArc`, `Lambert`), the trim (`BusTrim`),
the correction loop (`PostBoostAim`, `AimCorrection`), the release and kick, the trace. What is new is the loop
around them and the bookkeeping of which warhead belongs to which target.

### Two budgets bound it

* **Divert**: the sum over the sequence. Checked at designation, shown in the panel, re-checked in flight
  against what the trim actually spent.
* **Time**: a re-aim is currently about 60–75 s of trim and correction (`2026-09-17-shape2`: separation to
  release ~75 s including clearance). Six targets is several minutes, and every release has to happen before the
  bus reaches the air. A 6,000 km shot coasts for about 15 minutes, so six fits; a 1,500 km shot may not. The
  panel says how many targets fit, and the reach display drops targets that would not.

### Accuracy per target

Each target gets its own full correction, so each should land as the single-target shot does today, with one
known cost: **warheads still aboard wait longer**, and holding costs precision (`Sim/HoldingCost.cs`). The last
target's warheads wait longest. Phase 0 prices it; if it matters, the order can put the hardest targets first.

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
| **0** | **Price it headlessly**: divert per km along and across at 2,000, 6,179 and 12,900 km; the bus's real propellant; the stagger's divert; the holding cost of the later targets; time per re-aim against coast length. Decides whether ±100 km is real. | No |
| 1 | **Targets as data**: a list of up to six `AimSite`s with counts on `IcbmComputer`; panel list with add/remove/counts; the scenario can name several. The flight still sends everything to target 1. | Unchanged |
| 2 | **Reach display**: the missile region before target 1, the footprint after, cursor refusal outside it, the panel's divert and time readout. | No |
| 3 | **The release loop**: re-aim per target, staggered arrivals on shared targets, per-warhead target bookkeeping in the log. | Yes |
| 4 | **Instruments and nights**: per-target scoring in `shot-report.py`, the matching check with one target, then 2/4/6. | Yes |
| 5 | **Later**: area targets (option C), saving the target list with the craft, reordering by hand. | — |

## Open questions

* **The bus's propellant.** The trim is capped at 60 m/s by guidance, but what the bus's tanks can actually
  deliver has not been read. That single number sets how big the footprint is.
* **Does the first target stay special?** Here the booster flies to target 1 and the bus diverts to the rest.
  Aiming the booster at the middle of the set instead halves the largest divert, but makes the first release a
  divert too.
* **Release before or after the aim correction for target 1?** Today the correction runs once. With several
  targets it runs once per target; whether the first one's can be shortened is a phase 0 question.
* **What the player sees in flight**: which target the bus is currently aimed at, and a countdown per target.
