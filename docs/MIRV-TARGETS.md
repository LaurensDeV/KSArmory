# Six warheads, six targets

**A record.** The reference for several targets is `docs/ICBM-GUIDANCE.md`, "Several targets"; what is open is
`docs/ICBM-OUTSTANDING.md`. What stays here is how each part was priced and the flights that shaped it.

**Phase 0 has been flown headlessly**
(`tests/KSArmory.Tests/MirvDivertTests.cs`), so the numbers below are measured rather than estimated —
and it moved three of the plan's decisions, each marked **priced** where it appears. What it did not
answer is marked *open*.

*The pinned penalties and the two clocks are in `docs/ICBM-GUIDANCE.md`, "Several targets".*

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

*Moved to `docs/ICBM-GUIDANCE.md`, "Several targets".*

## Why the reach area is an ellipse, and why it changes

*Moved to `docs/ICBM-GUIDANCE.md`, "Several targets".*

## The pad can draw the pinned reach — measured 2026-09-20

*Moved to `docs/ICBM-GUIDANCE.md`, "Several targets".*

## The booster is aimed at the farthest target

*Moved to `docs/ICBM-GUIDANCE.md`, "Several targets".*

## The hop is charged where it is measured

*Moved to `docs/ICBM-GUIDANCE.md`, "Several targets".*

## What to do with warheads when fewer than six targets are picked

*Moved to `docs/ICBM-GUIDANCE.md`, "Several targets".*

## How it flies

*Moved to `docs/ICBM-GUIDANCE.md`, "Several targets".*

### Phase 3 found two things, and the first blocks it — 2026-09-20

*Both are fixed: see* The booster is aimed at the farthest target *and* The hop is charged where it is measured *above.*

**The itinerary starts where the bus is not.** `ReleaseItinerary` orders farthest-reach-first and takes stop 0
to be the one "the bus already arrives on" — which needs the booster aimed at the *farthest* target. But
`TargetEdit.ClickDoes` only adds a target during the coast, so **the booster is always aimed at the first
chosen** and the farthest does not exist when it flies. `TargetSet.SetLead` is called by nothing in `src/`, and
before cutoff the set can never hold more than one place.

Flown in the itinerary's order from there, the bus diverts *out* to the far end and walks back: for three
collinear targets 4 km apart it flies 16 km of ground where the itinerary charges 8. Budgeting a walk at half
what it costs is exactly what `ReleaseItinerary` exists not to do, so `ReleaseLoop` refuses such a plan
(`ReleaseWalkHold.NotWhereTheBusIsAimed`) and the flight releases everything at the lead, as today.

Two ways out, and it is the repository owner's call because each moves a shipped surface:

| | What changes | Against |
| --- | --- | --- |
| **R1. Let the booster's aim be chosen** | `ClickDoes` adds before launch too; the booster flies to the itinerary's first stop | Targets 2–6 are then placed with **no footprint drawn** — `ReachHold.NotCoasting`, which phase 2 refused on purpose — and the booster's own reach along bearings is not built |
| **R2. Start the walk at the bus's landing** | `ReleaseItinerary.Plan` sorts nearest-first from the lead; `ReachDisplay.Order` with it | Gives up "the dearest reach in the earliest slot", which was measured — though for a chain every hop is the same length and the total does not move |

**R2 is what phase 2 already implements** and is the recommendation: `ReachDisplay.Verdict` tests a click's
offset *from where the warheads land now* against one hop's ceiling, which is a player placing target 2 within
one hop of target 1. The display and the schedule disagree, and the display is the one a player has used.

**And a set every click accepted can still hold a hop the bus cannot fly.** The ring bounds each click to one
hop from the *current landing*; the itinerary charges the hop between *consecutive stops*. Two targets on
opposite edges are twice the ring's radius apart — 20 m/s against a 10 m/s ring, at 410 m per m/s an 8.2 km
hop. `ReleaseLoop` refuses that too (`ReleaseWalkHold.HopBeyondOnePass`); closing it properly means the cursor
measuring from the nearest chosen target rather than from the landing, which is a phase 2 change.

### What the actuation is — built 2026-09-20

*Moved to `docs/ICBM-GUIDANCE.md`, "Several targets".*

### The clock cut the walk — flown 2026-09-21

**Rung 0 passed and rung 1 failed, and the fault was one input.** Two targets three warheads each,
`SOLVER SCALE 8`: every rocket sent all six warheads to the lead and target 1 got nothing, on all eight.
The plan was *made* — `walk on GeoSat FAT 2: 2 stops, 20.3 m/s of 60`, per frame, per rocket — and then
**zero** `re-aiming at target`, `released n of m` or `walk complete` lines. `TargetOfRound` reported every
round `for target 2`, so the seam was sound and the walk simply never ran.

**`ReleaseItinerary.Bus.CoastSeconds` was being handed the countdown.** The field means the coast's own
length — "from the earliest release the flight allows to arrival" — and `IcbmComputer` fed it
`SecondsToArrival`, which runs down. `CoastFits` is `1 + floor((coast - gate) / hop)`, so it drops to one
stop the moment `coast` falls under `gate + hop` — **485.1 s, which is exactly
`FirstBeforeArrivalSeconds`**. The set was therefore cut to a single stop on the very frame the first
release became due: `Plan` returned `OneStop`, `Walks` went false, the gate reverted to 420, and the
flight released as a normal single-stop shot minutes later.

**It said nothing, which is half the fault.** The plan line only printed while the walk *was* one, so the
downgrade was silent under a log full of lines announcing two stops. It now prints on every change of
shape — walking or not — and dedupes on the numbers-stripped sentence rather than the whole one, which
also ends the per-frame spam.

**All three of the walker's outputs took their not-walking branch, and that is one cause rather than
three.** The gate read 422 s against a setting of 420, six warheads left consecutively, and every
round was recorded against the lead — three readings of `ReleaseWalker.Walking`.

**The walker was armed, which the log proves.** `SayTheWalkIfItChanged` was reached only when
`_walker.Plan(...)` returned true and printed the *walker's own* `Walk.Say()`, so
`walk on …: 2 stops` is direct evidence that the flight's walker held a two-stop plan with `Walks`
true. It was later **disarmed** by a replacement, which `Plan` accepts freely until the first
release — and the replacement was `OneStop`.

**What would have caught it: stepping the decision, not sampling it.** Every fixture asked
`ReleaseLoop.Plan` at one instant, and the plan was correct at every instant anyone thought to test.
`ReleaseWalkClockTests` steps it down a coast the way a flight does, which is the general shape for
anything re-derived per frame from a quantity that moves.

**The reach is the same shape and is not fixed.** `ReachDisplay.For` prices every slot at the
*current* footprint's minor axis, which decays over the coast — about 950 m per m/s at cutoff to 410 at
the gate — so a set can fit `PostBoostAim.MaxTrimMetresPerSecond` early and not late, and `BudgetFits`
then cuts it exactly as `CoastFits` did. At two targets the threshold is a neighbour spacing of about
**18 km**: 4 km costs 25.9 m/s of 60 at the gate and cannot fire it, 18–42 km fits at cutoff and not at
the gate. Not fixed, because both ways out are worse than the fault — latching the reach at cutoff
over-promises a walk the bus cannot pay for at the gate, which is what `ReleaseLoop` exists to refuse,
and pricing each slot at its own reach needs a decay model nothing has measured. What is fixed is that
it can no longer happen **silently**. *Since priced per slot: `IcbmConfig.PriceEachHopAtItsSlot`, on — see* Both
levers built *below.*

**And the obvious fixture would not have.** `ReleaseWalkerTests.ASetOfTwoDrivesEveryNumberTheFlightReads`
is the one-target sweep at arity two — a bounded `TubesLeft`, a finite `GateOverrideSeconds`, an
advancing `TargetIndex` — and it is worth having, because nothing covered any of the three. But it is
handed a two-stop plan, and the walker was never the fault: against the flown code it **passes**. Only
the two tests that step the coast fail, which is what makes them the regression guard.

### The walk accepted a hop the trim then refused — flown 2026-09-21

**Three of eight rockets walked, which is the first time this project released at two places.** The
other five refused honestly on `ReleaseWalkHold.HopBeyondOnePass`. **Where it walked the warheads
landed 4.00 km out — exactly the target spacing.**

**The planner and the trim were judging different quantities.** `ReleaseItinerary` prices a hop as the
velocity change between two solutions — 9.73 m/s, under the 10 m/s ceiling, accepted. `BusTrim` is
handed the whole difference between the vehicle's velocity and the *new* solution's, so the residual
the release had just left on the bus is added: **10.29 m/s**, over the ceiling, and
`BusTrim.Update` refuses **the whole pass** rather than spending its 10. The bus never moved, and
three warheads already recorded against the next target left on the old solution.

**So the acceptance is now the trim's own question, on the trim's own ceiling.**
`BusTrim.CeilingFor` is one expression both call, and `ReleaseLoop.PassMustFly` is the hop plus what
the bus owes — a bound by the triangle inequality, so it refuses some hops the trim would have flown.
That is the direction to be wrong in: over-promising is what puts warheads where nobody aimed them.

**And a refused hop ends the walk at the stop the bus is on rather than handing over.**
`ReleaseWalker.StopHere` leaves the cursor there and gives that stop every warhead left, because the
bus is already trimmed and corrected onto it — `Finish` would be the wrong ending, taking `TubesLeft`
to zero and riding the rest down. The later targets get nothing, and the log and the panel say so.

**The ceiling is `BusTrim.MaxMetresPerSecond` for a hop and that is not incidental**: a hop is flown
as a fresh null with the correction's pass count back at zero, so `PostCutoffSequence.CeilingFor`
hands it the constant. At the flown 411 m per m/s reach a **4 km spacing is 9.7 m/s and sits on the
ceiling by construction** — the geometry was testing the guard rather than the loop.

**What pins it**: `ReleaseHopAffordableTests`, and the mutations were checked. Judging the hop alone —
what flew — fails three of the five; curtailing to `Finish` instead of `StopHere` fails the one that
asserts the remaining warheads are delivered.

**Two things bit during the wiring, and both are pinned.** `ReleaseWalkHold.Walking` is the enum's *zero*,
so a `default(ReleaseWalk)` — what a computer holds from designation until the reach is priced — claimed to
be a walk with no stops: it would hand over at once, finish, and report the salvo over before a warhead
left. `ReleaseWalk.Walks` now tests the stop count too. And six targets 4 km apart do not fit the 60 m/s cap
on a flat 410 m per m/s reach (64.9 m/s), so `TrimToWhatFits` cuts the set to five and the gate opens for
five stops rather than six — the doc's 55.1 m/s figure is against a reach that *decays* over the schedule.

### Two budgets bound it

*Moved to `docs/ICBM-GUIDANCE.md`, "Several targets".*

### Accuracy per target

*Moved to `docs/ICBM-GUIDANCE.md`, "Several targets".*

## What a flight has to show

*The scenario and the scoring moved to `docs/ICBM-GUIDANCE.md`, "Several targets".*

* **Nights**: the current single-target build against the loop with one target (they must match — the loop
  must cost nothing when it has one target), then 2, 4 and 6 targets.

## How close two targets may be — decided

*Moved to `docs/ICBM-GUIDANCE.md`, "Several targets".*

## Phases

*The table moved to `docs/ICBM-GUIDANCE.md`, "Several targets".*

**Both gates that sat in front of phase 3 are now clear.**

**The arrival decision is settled: pinned.** Priced free-clock, the reach is a long ellipse; pinned — which
is what the flight actually does — it is small and nearly circular, because the penalty is entirely
along-track and runs 2.56x at 6,179 km to 11.94x at 12,902. Phase 2 shipped `ArrivalClock.Pinned`, and a hop
is solved pinned for free: `IcbmProgram.ResolveCoastArc` solves to `CommittedArrivalFromNow` and the coast
never reaches the latch. **So no re-latch is needed and none should be built** — the ground the player is
shown and the ground the bus can reach are one answer.

**And the trim has stopped failing.** `IcbmConfig.StallFallsBackToHolding` was flown and shipped **on**:
`hold vs base: 0/24 lost against 16/24, Fisher p=0.0000 RESOLVED`, 0 of 96 rockets stalled against 62, and a
second night at 6,135 km confirmed it costs nothing where it never fires — `docs/ACCURACY-PLAN.md` 3fh and
3fj. Six diverts on that loop are six passes rather than six chances to lose the shot.

**What blocked phase 3 after those** was that the itinerary's first stop was not where the
booster aimed, fixed by `TargetSet.ElectFarthestLead`. See *Phase 3 found two things* above.

## Open questions

* ~~The bus's propellant.~~ **Answered: 134.3 kg, and the 60 m/s guidance cap binds well before the tank
  does.** What is still unread is the *attitude-hold* propellant across a six-target coast, which is
  uncounted, and whether the 2,906 m/s exhaust velocity — computed through KSA's design chain, not observed —
  holds in flight. Watch tank mass against `SpentMetresPerSecond` on the first flight that burns for real.
* **Half of *when the release loop runs* is a schedule rather than a setting**, and that half is built:
  `Sim/ReleaseItinerary.cs` starts the itinerary `420 + (N-1) x 65.1` s before arrival, so the *last* release
  still lands on today's gate and a set of one is bit for bit unchanged — which is what stops it regressing the
  shot every other measurement here is taken against. It settles *Does the first target stay special?* below
  too: farthest reach first puts the dearest hop in the slot with the most leverage, and the first stop is the
  one the bus already arrives on, so **the booster is aimed at the farthest target** rather than at the first
  one chosen — which is now built, because the set can be assembled before the aim is committed. Flown, and
  it leaves the bullet below untouched: moving the gate *itself* still costs the first target.
* **What bounds a set is its spacing, not its count** — moved to `docs/ICBM-GUIDANCE.md`, "Several targets".
* **When the release loop runs is the design decision, not a tuning.** `ReleaseBeforeArrivalSeconds = 420` is
  a single-target optimisation: it shrinks the ejection kick's leverage, which is why the group lands
  millimetres from the aim. For six targets it costs **4x the reach at 6,179 km and 7x at 12,902**. Moving it
  multiplies every velocity residual by the same factor — the trim's floor goes 14 m → 55 m — which against a
  2 km lethal radius is nothing and against the batch scoring is everything. **A paired-arm question before
  phase 3, not an assumption.** Flown 2026-10-11 as `WalkStartsAtCutoff`, and it lost: the gate stays.
* ~~**Does the first target stay special?**~~ **Answered, and built: the booster flies to the farthest, not
  to the first clicked.** `TargetSet.ElectFarthestLead` re-elects the lead on every add while the aim is
  still free. Aiming at the middle of the set instead halves the largest divert and makes the first release
  a divert too — untried, and it would need the lead to stop being the stop that costs nothing, which is
  what `ReleaseLoop` is built around. The farthest lead is flown (2026-09-21); the middle is untried.
* **Release before or after the aim correction for target 1?** Today the correction runs once. With several
  targets it runs once per target; whether the first one's can be shortened is a phase 0 question.
* ~~**What the player sees in flight**~~: built -- the panel says which stop the bus is on, and each target's mark
  carries its own countdown (`Ksa/IcbmOverlay.cs`).

## Flown: a bus that went to two places — 2026-09-21

**The first multi-target MIRV flight.** `./tools/scenario.sh 'mirv:24.0S,62.0W;24.018S,62.0W'` against
`SOLVER SCALE 8`, 2 km apart at 6,135 km: **8 rockets, 8 walks, 16 releases, 0 refusals, PASS**. Three
warheads on each target, and the bus diverted 2 km between them.

| target | worst miss |
| --- | --- |
| the one the booster flew to | **0 m on all eight** |
| the one the bus diverted to | 0 m on five, **15–19 m** on three |

It took three flights, and the two faults are worth more than the success.

**Fault 1 — the plan was right whenever anyone asked, and wrong when it mattered.** `ReleaseItinerary`
wants the coast's own *length*; `IcbmComputer` handed it `SecondsToArrival`, which runs down. Since
`CoastFits = 1 + floor((coast - gate) / hop)`, the set collapsed to one stop the instant the coast fell
below `gate + hop` = 485.1 s — **exactly `FirstBeforeArrivalSeconds`, the frame the first release became
due**. The walk planned all through the ascent, printed `2 stops, 20.3 m/s of 60`, and was silently
replaced by a one-stop plan at the moment of use: every warhead to the lead, no re-aim, no release line.
Every fixture asked `Plan` at one instant and it was correct at every instant anyone thought to test.
`ReleaseWalkClockTests` steps it down a coast instead, and `ReleaseWalker.CoastForPlanning` latches the
length at cutoff.

**Fault 2 — the walker and the trim judge different quantities, and the gap lands as a silent miss.**
At 4 km spacing the walk ran and the warheads landed **4.00 km out, scored as arrived**. The walker
prices the *hop* against `BusTrim.MaxMetresPerSecond` and accepts at 9.73 m/s; the trim is then asked
for the hop **plus the residual already on the bus**, 10.29, trips the same ceiling and refuses the
whole pass — so the bus never moves and the warheads leave on the old solution. **Open** that night, fixed
since (*The walk accepted a hop the trim then refused*); it was
dodged by choosing a spacing the hop could afford. The reach at that epoch is 411 m per m/s, so 4 km is
9.7 m/s and sits on the ceiling by construction.

**What this vindicates.** Per-target scoring is what made fault 2 visible at all: the old single-aim
score reads that flight as `6 of 6 arrived, spread 0.000` and **passes** it. `ShotBoard` seeding from
the *request's* target count rather than from what the flight reached is what made fault 1 legible —
`TARGET 1 of 2 … nothing was released` rather than a target that quietly did not exist.

**Still unflown**: four and six targets, a paired night against a single-target baseline, and every
in-game surface — the pre-launch reach drawn on the ground, the cursor refusing outside it, and the
panel's list. Nothing here has been touched by a human hand.

## Flown: four targets, and what the ceiling actually binds — 2026-09-21

**Two targets, 2 km apart, is a clean success** (above). **Four targets, 1 km apart, walks end to end**:
24 re-aims, 32 releases, no refusals, all 8 rockets PASS with 6 of 6 arriving. So three handovers in one
coast work, and `BusTrim` converges on a commanded divert as readily as on a separation shove — which
was the thing most expected to fail.

**But the per-pass ceiling, not the budget, is what bounds a set.** Four targets **2 km** apart refused
every hop: the walk planned **26.5 m/s of 60** — comfortably inside the budget — and then found single
hops wanting **10.0, 10.1 and 16.2 m/s** against `BusTrim.MaxMetresPerSecond` = 10. **The per-hop cost
grows along the walk** as the reach decays, so the last hop is the dearest and the first to be refused.

**The spacing tables (now `docs/ICBM-GUIDANCE.md`, "What bounds a set is its spacing") are therefore wrong about what fits.** They price a set
against the 60 m/s budget and conclude six targets fit 4 km apart at 55.1 m/s. A set also has to clear
the ceiling **hop by hop**, at the reach of the slot each hop is bought in, and that is far tighter.
They need re-deriving; until then read them as a budget check that a set must pass *in addition to* the
per-hop one, not as the answer.

**Two faults still open at four stops.**

1. **The last stop released with `divert 0.00 m/s`** — nothing refused, it simply released without
   hopping — and that stop is the worst group on six of eight rockets, **0.94 to 2.06 km**, which is the
   spacing. Same shape as the 4 km fault above.
2. **The flown order is a zigzag rather than a chain**: one rocket walked **3 → 0 → 1 → 2** where
   farthest-first on four collinear targets wants 3 → 2 → 1 → 0. That is 5 km of travel for a 3 km
   chain, it inflates every later hop, and it is plausibly the cause of (1). Cumulative divert reached
   31.72 m/s against a plan of 29.6, which is consistent with the flown walk being dearer than the
   priced one.

**Accuracy, for the record.** Two targets: 0 m on the booster's target, 0–19 m on the diverted one. Four
targets: sub-40 m on the stops that hopped, ~1 km on the stop that did not. The mechanism is sound; what
is unfinished is the bookkeeping around it.

### Both of those, diagnosed — and the plan is optimistic by a correction per stop

**(2) was the order the player clicked in.** `ReachDisplay.Order` put the lead first and then everything
else *in the order it was chosen*, and its own comment claimed that walks inward "because
`ElectFarthestLead` puts the lead at an end". That only holds if the player clicked in order. Clicked
near-to-far and led from the far end, `order` is `[3, 0, 1, 2]` — out to the near end and back. It now
walks **nearest-first from wherever the bus has got to**, which for a chain is the monotone walk: 3 km of
travel against 5. Greedy rather than the cheapest of the 720 orders, because this is re-derived every
frame for every rocket and greedy is exact for a chain walked from an end, which is what the lead being
farthest makes every set. (The earlier prose promising all 720 were tried was aspirational; nothing did.)

**(1) was the trim budget running out, and it is arithmetic rather than a guess.** The four stops' diverts
were 11.21, 17.08 and 31.72 m/s — **per stop**, not cumulative — which sum to **60.01** against
`PostBoostAim.MaxTrimMetresPerSecond` = 60. `BusTrim.WithinBudget` then ends the trim, `PostBoostAim`
finishes on the same reading, `trimming` goes false and the release happens with no hop flown. The
`<left> m/s left` field on stop 3's line read `0.0`; the instrument said so and nobody was reading it.

**Nothing downstream refuses that**, which is what separates it from the per-pass ceiling: over the
ceiling `BusTrim` refuses the whole pass, over the budget it simply stops. So the handover now asks both
— `ReleaseLoop.CanFlyTheHop` returns `BeyondOnePass` or `BeyondTheBudget` — and the whole pass has to be
payable, because a budget that runs out part way leaves the warhead between two targets.

**And the plan is structurally optimistic by one aim correction per stop.** `ReleaseItinerary` charges
`SingleTargetMetresPerSecond` = 16.1 **once** — "the separation null and the aim correction" of a
single-target flight — and then charges each hop its divert alone. But every stop resets `PostBoostAim`
and runs a full correction, so four stops pay four of them. That is why a plan of 29.6 m/s flew at 60.01
with hops worth only a few m/s each: the corrections dominate, and the fix for (2) reduces the hops
without touching them. **Not charged, deliberately** — one flight is not enough to type a constant with,
and changing the planner's arithmetic changes which sets are accepted, which cannot be checked without
flying. It is the number to measure next, and it belongs in the re-derived spacing tables.

## Flown: the order and the last stop, fixed — 2026-09-21

`6b83417` — nearest-first walking and a walk that stops when the budget is gone — re-flown against the
geometry that found the fault: four targets 1 km apart on one meridian, 8 rockets, 2/2/1/1 warheads,
6,135–6,147 km. `~/shots/2026-09-21-rung2c.console`, declared beforehand in
`~/shots/scripts-2026-09-21/DECLARE-rung2c.md`.

**The walk order is fixed.** Every rocket released `1→4, 2→3, 3→2, 4→2, 5→1, 6→1` — monotone, walking
inward from the farthest. Before the fix all 8 flew `1→4, 2→1, 3→1, 4→2, 5→2, 6→3`: out to the near end
and back, which put the last stop on target 3.

**And the last stop's miss went with it.** Worst warhead per target, the same eight rockets:

| | before (`rung2b`) | after (`rung2c`) |
| --- | --- | --- |
| target 3 | **0.94–2.06 km on 6 of 8** | 0.000–0.066 km |
| worst anywhere | 2.063 km | **0.140 km** |
| rockets all-targets under 10 m | 0 of 8 | **6 of 8** |

**But the scenario reads FAIL, and the reason is the guard doing its job.** Two rockets — `FAT 2` and
`FAT 4` — ran the trim budget to 0.07 and 0.27 m/s of 60, so the walk refused its next hop and ended
where the bus was, leaving 3 of 32 targets with `nothing was released`. That is `ReleaseLoop`'s declared
refusal rather than a scatter: their warheads all arrived, on the stop they were already trimmed onto,
at 0.066 and 0.140 km.

**What spent the budget is the correction at a stop, not the hop.** The walk line prices the hop
itself at **1.93 m/s** — `the hop to target 1 is 1.93 m/s and the bus still owes 0.07, so the flight has
spent 60.02 m/s of its 60`. The figure on the release line is the whole trim spend at that stop, which
is dominated by that stop's `PostBoostAim` pass. A healthy rocket pays 13 + 5 + 5 + 5 ≈ 28 m/s of 60
for four stops; three rockets instead paid **35.01, 43.01 and 46.89 m/s at a single stop**:

| rocket | diverts, in release order | left |
| --- | --- | --- |
| `FAT 3`, `6`, `7`, `8`, `FAT` | 13.0, 4.9, 5.2, 5.2 | ~32 |
| `FAT 5` | 13.27, 4.84, 5.32, **35.01** | 1.6 |
| `FAT 4` | 12.91, **43.01**, 4.12 | 0.0 — walk ended |
| `FAT 2` | 13.13, **46.89** | 0.0 — walk ended |

**So the hops are cheap and the corrections are not.** Three hops of 1 km cost about 6 m/s between
them; the four corrections cost the other 22 on a healthy rocket and the whole 60 on a bad one. That is
the structural optimism above, met in flight: the planner charges one correction and the flight runs
one per stop, so a set of four stops is exposed to **four** draws from whatever distribution produces a
47 m/s correction.

**The open question is what makes a correction cost 47 m/s instead of 5**, and one flight cannot answer
it. *Answered 2026-10-01, in the next section: the simulation step.* It is not simply the loop reverting its walk — `shipping 0.0 m` after a full revert happens on the
healthy rockets too, four times on `FAT 6`, `FAT 7`, `FAT 8` and `FAT` between them. **Measure the
distribution before changing anything**: the per-stop spend is already on the release line, so a
handful of four-target flights gives it without new instrumentation. Until then the spacing tables
cannot be re-derived, because what bounds a set is the correction spend rather than the hop.

## Flown: what a stop's correction costs, and why — 2026-10-01

Two nights at four targets 1 km apart, `SOLVER SCALE 8`, 24.0S 62.0W, 6,135 km. Declared beforehand in
`~/shots/scripts-2026-09-21/DECLARE-correction-spend.md` and `~/shots/scripts-2026-10-01/`.

**The distribution** (`~/shots/2026-10-01-correction-spend`, 5 shots, 40 rockets, one arm): 25 of 40 ran
the whole 60 m/s and 9 ended their walk early. Per stop, median [max]: **12.6 [15.4], 4.7 [41.0],
18.8 [43.2], 11.7 [28.6]** m/s. Stop 1 is never expensive.

**The expensive mode is the step, not the walk.** Every aim pass that moved the aim 100–400 m, split by
whether the simulation speed rose above 1x during it:

| | passes | trim spend, median | over 3 m/s |
| --- | --- | --- | --- |
| 1x throughout | 47 | **0.81 m/s** | 0 |
| sped up | 56 | **6.85 m/s** | 44 |

The aim move is worth about 0.43 m/s at 510 m of landing per m/s. What raised the speed was the scenario
itself: `BallisticScenario.WarpTheCoast` asks for 8x once releases have been quiet for five seconds, which a
walk is between its stops, and `WarpPolicy` holds that to 4.47x for the warheads already in the air — so
every inner stop trimmed at 84–117 ms steps and stop 1 at 15. At those steps `BusTrim.StopBand` is half a
frame of thrust, and a command reaches the engine a frame after it is written: a hold chosen without the
frame still in flight lands outside the band on the opposite side, and the trim chatters between opposite
jets at 0.04–0.08 m/s for the rest of the pass, a hundred commands to a pass. **A player warping through a
walk meets the same thing**; the scenario only made it reproducible.

**Two more things made every inner stop pay a pass.** `AimCorrection.Retarget` zeroed the bias, and the
loop walks back to 210–360 m at every stop, so every inner stop's first reading was ~220 m out — over the
156 m that triggers a pass — and that reading banked as the best under the flat 250 m band, so nothing later
could score as better and the loop ran on to `PassesWithoutImprovement`.

**The headless rig does not see the step at all.** `TrimBus` applies a command the step it is chosen; made to
apply it a frame late, with pulses and jittered 84–117 ms steps, it spends 1.3–2.0 m/s on 0.38 owed at
*every* step, 1x included. `TrimLatencyTests` pins the arithmetic only.

**The 2x2** (`~/shots/2026-10-01-carry-bias`, 8 shots, 64 rockets, paired, `arm/carry-bias`):
`IcbmConfig.CarryAimBiasAcrossHops` starts each stop from the bias the last one walked to, and
`IcbmConfig.TrimCountsTheCommandInFlight` takes the frame a hold still owes off what is left before choosing.

| arm | walk spend vs base, per shot | lower on | landing, median | ran out / truncated |
| --- | --- | --- | --- | --- |
| base | — | — | 18.7 m | 8 / 2 of 16 |
| carry | 0.40–0.73 | 8 of 8 | 11.0 m | 1 / 0 |
| in flight | 0.42–0.73 | 8 of 8 | 13.9 m | 0 / 0 |
| **both** | **0.36–0.64** | **8 of 8** | **5.4 m** | **0 / 0** |

On the landing, `both` vs base is **0.35x [0.18, 0.73], RESOLVED**; carry is 0.61x [0.34, 1.03] on 6 of 8
shots, and in flight 0.75x [0.36, 0.86] on 8 of 8, both unresolved by the report's shot-flip test. With the frame counted, a
pass at the long step cost **0.70 m/s** over 42 passes with none over 3 — base at 1x is 0.77.

**And on one target** (`~/shots/2026-10-02-inflight-single`, 8 shots, 64 rockets, paired,
`base|inflight`, declared non-inferior below 1.25x): the in-flight count is **0.88x [0.64, 1.13]** on the
landing, both arms grouping within millimetres (medians 2.88 and 2.30 mm). The default endpoint reads every
rocket as 0 m at that level and pairs nothing, so it is read with `--endpoint landing`.

**Both ship on**, in `3b4b4f6f`.

**What it leaves.** The planner's charge per stop (`SingleTargetMetresPerSecond` once, then hops) is now
roughly right rather than structurally optimistic: a walk on `both` spends 13 + 3.4 + 3.3 + 3.3. The spacing
tables in `docs/ICBM-GUIDANCE.md` can be re-derived off that. `BallisticScenario` no longer warps until every bus has walked
(`9c2e77ad`), so a scenario walk is now measured at 1x: flown on `dev` with both switches on
(`~/shots/2026-10-02-walk-smoke`, 2 shots, 16 rockets), the 8x arrives 5 s after the last release, both
PASS, a walk spends a median 23.9 m/s, and every inner stop 3.5–4.2. It also takes the long step out of the
scenario, so a regression in counting the command in flight would no longer show in a walk.

## A full set never walked, and starting a walk at cutoff — 2026-10-02

**Six targets never walked, and that is fixed** (`a7f49a4c`). Six is `TargetSet.MaxTargets`, so the reach
reads `Full`, and `HasRegion` is false for anything but `Drawn`. The computer measured each target against
the reach only while `HasRegion` held, so a full set reached the planner with no warheads on any target:
nothing was planned, nothing logged a walk, and all six went to the lead. Every earlier night flew four
targets or fewer and never got there. Measuring now asks `ReachDisplay.HasFootprint`; the ring and the click
refusal still go by `HasRegion`. Flown on six targets 6.5 km apart (`~/shots/scripts-2026-10-02/verify-six.log`):
every rocket plans `6 stops, 49.8 m/s of 60` once the coast begins. Stopped before release, so **no six-stop
walk has been flown to the ground yet**.

**On the pad the same set is refused, and that is expected.** Before cutoff the reach is the epoch floor, and
the planner prices a 6.5 km hop at **17.6 m/s** against the one pass's 10, so it says
`a hop wants 17.6 m/s and one pass will fly 10`. The coast re-flies the reach at ~955 m per m/s, prices
the same hop at 6.8, and the plan is replaced, because nothing commits it until a warhead leaves.

**`IcbmConfig.WalkStartsAtCutoff`** (`5f8e9e87`; flown 2026-10-11 and lost, `ICBM-OUTSTANDING.md` 4.3; removed 2026-10-12) put a walk's first release at the start of the
coast instead of ending its last on `ReleaseBeforeArrivalSeconds`. A hop bought early moves the landing
further per m/s (1,076 m against 688 at 6,179 km), so a chain could be spaced ~8.5 km rather than ~4.5, for a
per-warhead floor nearer 55 m than 14.

**The night declared for it is void, and the declaration needs revising before it flies.**
`~/shots/2026-10-02-walk-cutoff` flew one shot before the full-set fault was found: both arms released everything
on target 6. Two things to change in `~/shots/scripts-2026-10-02/DECLARE-walk-cutoff.md` before re-flying it:

- **At 6.5 km base is not expected to refuse.** The planner prices every hop at one reach for the whole coast
  (`ReachDisplay.For`'s single `atEachSlot`), so the switch moves *when* the hops are flown and not what they
  are priced at. Base and early both plan six stops at 6.8 m/s a hop. What separates the arms is what the hops
  **cost when flown**, which is what the night has to measure: per-hop divert, total spend, truncations. To make
  base refuse, the spacing has to exceed one pass at the flown reach, ~9.5 km, or the planner has to price each
  slot at its own reach.
- **The primary endpoint is then spend and truncation, not stops planned**, and the landing secondary stands
  (every warhead inside the lethal radius; early's floor is expected to rise).

**Next, in order:** re-fly a six-target walk on `dev` with the switch off to see one complete; revise the
declaration as above (**done 2026-10-09**: `~/shots/scripts-2026-10-02/DECLARE-walk-cutoff-v2.md`, with that walk
as its step 0, flown spend and truncation primary, the landing pooled because one warhead a stop cannot carry a
per-target verdict); fly the paired night. Then the levers: spend the trim reserve kept back for the split's
null after stop 1, and price each slot at its own reach.

## Both levers built, off and unflown — 2026-10-09

*Both ship on now: `PriceEachHopAtItsSlot` since 2026-10-10, `SpendTheSplitReserveAfterTheFirstStop` since 2026-10-11.*

**`IcbmConfig.PriceEachHopAtItsSlot`** prices each hop at the reach of the slot it is flown in. The reach
the planner is handed is measured at one time to go (`DivertFootprint.FlightSeconds`: the columns' own fall
while coasting, the gate on the pad), and each slot gets that reach times `t_slot / t_measured`, never above
one (`ReleaseItinerary.Bus.SlotReachScale`). The pinned reach is concave in the time to go, about
`sin(ωt)/ω`, so scaling it down linearly is a floor; holding it at one for an earlier slot keeps the pad,
whose epoch estimate is measured at the gate, exactly where it was. The ring is drawn and a click refused at
the slot the next add would be flown in, the last of one more stop, so the picture still agrees with the
plan. Coasting it is the honest direction and the refusing one: a 4 km chain the coast prices at 4.19 m/s a
hop off 955 m per m/s at 1,200 s wants 12.0 for its last hop at the gate, more than one pass flies
(`WalkPricingTests`).

**`IcbmConfig.SpendTheSplitReserveAfterTheFirstStop`** releases the 10 m/s the tank keeps beyond the 60 m/s
budget for nulling the split (`ReleaseLoop.SplitNullReserveMetresPerSecond`, the margin
`PostBoostAimTests.TheBudgetLeavesEnoughToNullASeparation` holds) once the first stop's whole quota has gone
(`ReleaseWalker.FirstStopAway`). The planner charges stops after the first against 70; the flight raises the
trim's budget, the hop check and the post-boost cutoff by the same 10 from that frame, and never for a set of
one. A six-stop chain 9.5 km apart costs 63.6 m/s: five stops without it, six with. The planner still prices
against `PostBoostAim.MaxTrimMetresPerSecond` rather than `TrimBudgetMetresPerSecond`, as it did before. **On since 2026-10-11**: flown paired on the 4 km six-target walk, 12 of 12 buses walked five stops against four on all 12 without it, every released warhead within 5 mm.

**Flying them.** The first wants a four-target walk at a spacing the coast-now price accepts and the slot
price refuses, flown with it off, to see which of the two the per-hop spend on the release lines follows. The
second wants a six-target walk the budget cuts, flown on the smallest bus the rack is fitted to, reading the
trim's spend against what the tank actually held: the reserve was sized to be left over, so spending it
leaves the smallest bus nothing, and nothing has measured how often a walk's last hop runs to its ceiling.

## Flown: six stops to the ground, and a stop costs twice its hop — 2026-10-10

Step 0 of `DECLARE-walk-cutoff-v2.md`: one world of `SOLVER SCALE 8`, six targets one warhead each along the
meridian from 24.0S 62.0W, shipped settings, `fadfdd84` (`~/shots/2026-10-10-walk-step0*`). The world ran at 1x
on 13 ms steps throughout, so this is not 10-01's warped trim.

**At the declared 6.5 km no rocket walks.** Planned after cutoff at six and then five stops, the first hop
priced 10.0 m/s against the 10 a pass may fly, `HopBeyondOnePass` held the walk, and all six warheads went to the
lead on all eight rockets. That is the planner refusing as designed. The declaration expected 6.8 m/s a hop; the widest set ending on the
gate is the 4.5 km `docs/ICBM-GUIDANCE.md`'s spacing table gives.

**At 4 km every rocket walks five of six stops, and the walking is exact**: every warhead released landed
2.8-44 mm from its own target. Every walk then ends at stop 5 on the 60 m/s budget, `the walk ends here`, and the
nearest target gets nothing.

| stop | flown, median [range] | priced |
| --- | --- | --- |
| 1, the lead | 12.58 [7.37, 12.98] m/s | the remainder of 36.9 m/s for six stops |
| 2 | 11.38 [11.20, 12.21] | 6.43-6.88 |
| 3 | 12.18 [11.84, 12.91] | 6.43-6.88 |
| 4 | 12.82 [12.53, 13.89] | 6.43-6.88 |
| 5 | 11.30 [10.95, 13.61] | 6.43-6.88 |

**The hop's own pass costs what it is priced at; what follows it does not.** On one rocket the pass after stop 1
opened at 6.44 m/s against 6.47 priced, took that down to 3.5 m/s in a few seconds, and spent the next 50 s
alternating nose and belly every frame as the demand fell from 3.5 to 0.1, for 11.29 m/s in all. The trim fires
one direction at a time, so a demand split across the bus's axes is paid in the sum of its components while the
planner prices the straight line between landings: up to 1.41x across two axes and 1.73x across three. That
could account for the factor; it is not verified headless, and the tail's slowness is not explained.

**The night is not flown**: a stop that never releases is the declaration's own void condition. What would let a
six-stop walk finish is pricing a hop in what the trim pays (or letting the trim fire the straight line), and stop
1's correction in the budget; the 4.4 per-slot pricing makes hops dearer, not cheaper, so on its own it would
refuse earlier rather than finish.

### Fixed, and flown: a hop priced at what the trim pays, at the slot it is flown in

**The axis sum is confirmed headless.** `HopTrimPriceTests` flies a hop's velocity change through `BusTrim` at 13
ms on six 0.55 m/s² jets: along one axis it costs its length, across two 1.41x, across three 1.73x, at every
step. `DivertFootprint.TryAxisPrice` inverts the pinned divert's three rows and resolves the result on the bus's
control frame, so a 2.5 km hop the trim flies for 11.90 m/s is priced at 11.95, where its length says 7.05.
`IcbmConfig.PriceHopsAtWhatTheTrimPays` prices hops so; a hop also carries its length, because `BusTrim`'s
10 m/s ceiling bounds a pass by length and it flew that 11.90 in one.

**Alone it is not enough**, flown on the same walk paired against the shipped price (`~/shots/2026-10-10-walk-trimprice`):
hops priced at 10.2-11.2 m/s, but each stop flew 1.4-4.2 m/s over its price and the excess grew stop by stop,
which is the reach shrinking as the hops come nearer arrival -- 4.4's `PriceEachHopAtItsSlot`. Flown together
they refused every walk (`walk-both`): the pass was judged at the stop's own release, 10.1-10.8 m/s, where the
hop is flown straight after the stop before and its first pass flew 6.4-7.0. `ReleaseItinerary.PassOnto` now
judges the pass at the slot it is flown in, and the budget keeps the stop's own, which overcharges by about what
each stop's own correction costs.

**Flown together, both on** (`~/shots/2026-10-10-walk-both2`, two worlds, four rockets an arm):

| | planned | flew every stop planned | per stop, flown against priced | left |
| --- | --- | --- | --- | --- |
| shipped | 6 stops | 0 of 8, every walk dry at stop 5 | 11.0-14.2 against 6.4-6.9 | 0.0-1.7 m/s |
| both prices | 4 stops, 2 targets dropped, their warheads aboard | 7 of 8 | stops 2-4: 13.3-16.0 against 11.6-15.3 | 2.3-4.5 m/s |

Every warhead the seven released landed within millimetres of its own target. Both switches ship on. **The eighth
is a different fault**: seat 1, whose hop after stop 1 pointed back at the spent stack, so `TrimWaitsOutTheStack`
held it two minutes for the stack to open to 680 m; the hop was flown late, stop 2's correction ran out its 120 s
and released 773 m out, and stop 3's correction asked for more than one pass and was refused whole, 3.2 km. The
keep-out and the walk were built apart and have not met before; one of eight, so not yet a rate.

**Fixed with the long-range stack wait** (`ACCURACY-PLAN.md` 3fr): the wait now holds only the split's own null and
ends once any warhead has left, since a walk's handover restarts the post-boost passes and read every hop as the split
again. Flown on the shipped code, two 4 km worlds (`~/shots/2026-10-10-walk-hopwait`): 16 of 16 buses flew all four
stops planned, none held, every warhead within millimetres; before the hand-over rule, seat 1 was held and ended
3.3 km off in both worlds.
