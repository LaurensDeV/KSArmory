# Six warheads, six targets

**Mostly a plan, and no longer entirely.** Phases 1 and 2 are built. `TargetSet` is the list of up to six
targets with their warhead counts and the release plan a flight would read, `TargetEdit` is when that list
may be edited, `IcbmComputer` holds one, a click on the world past cutoff adds to it, and the panel lists it
with per-target counts and a remove. `DivertFootprint` is the reach on the ground, taken off the sensitivity
columns `ReleaseFocus.FlownSensitivity` already flies once per salvo, and `ReachDisplay` is that reach as one
answer the outline, the cursor and the panel all read — so the region drawn and the clicks refused cannot
disagree. **And the list can now be assembled before launch**, because pinned the reach is the release epoch
rather than the arc: the booster is then aimed at the farthest target, which is what the release loop needs
of it. **What is still not built is the missile's own reach before target 1**: that is `IcbmReach` asked
along bearings per candidate point, tens of milliseconds a ring. **Phase 3 is built and unflown**: the bus
walks the itinerary, re-aiming and releasing each target's quota, and `Sim/ReleaseWalker.cs` is what makes a
set of one execute none of it — so that shot, which every accuracy measurement on this mod is taken against,
is the one it has always been.

**And phase 4's instruments are built beside it, also unflown.** `ShotBoard` scores a salvo one group per
target, `BallisticScenario` asks for several and prints a `TARGET` line each, and `shot-report.py` reads
them. A warhead is scored against the target `IcbmComputer.TargetOfRound` recorded for it at the release,
which is the only reading that survives the walk: the lead moves at the next handover, so asking at impact
names whichever target the bus finished on.

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
gate. So `DivertFootprint.OrientationRad` is meaningless pinned.

**Both clocks are built, and `DivertFootprint.TryFrom` has no default between them.** It takes an
`ArrivalClock` — `Pinned` for what the flight does, `Free` for what a loop re-committing the arrival could
offer — because drawing the free one where the flight pins the clock shows a region 2–12x too long, and
that is not a mistake a caller should be able to make by forgetting an argument. Pinning is **one
projection, not a second solve**: the arrival time responds to release velocity along one direction, so
the reach is the free-clock map with that direction taken out of each of its two ground rows. What it
needed was a third row nothing carried — `ReleaseFocus.FlownSensitivity` now flies
`ArrivalSecondsPerMetrePerSecond` beside the landing columns, which costs no extra flying because the
seven predictions already report their own arrival time, and the landing columns cannot supply it: they
are two crossings of one sphere, so the radial part that decides *when* is projected out of them. At the
flown 6,179 km release it reads **355 / 355 against the free clock's 688 / 355**, 1.94x along and 1.00x
across (`DivertFootprintTests`).

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
3. The first click places target 1. **Targets 2–6 can be placed before launch as well as during the
   coast**, because pinned the footprint is the release epoch and not the arc — see *The pad can draw
   the pinned reach* below. The region shrinks with each one added, and the ring sits around the last
   target placed rather than around the landing, because that is where the next hop starts.
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
  the 30–45 the budget suggests. At 6,179 km that is about **6.9 x 3.6 km** free-clock and **3.5 x 3.5 km**
  pinned, which is what the flight has. Drawing the larger region first and shrinking it when phase 3 raises
  the per-pass ceiling is the wrong order.
* **Before launch, the release epoch's reach — and it is a real boundary.** See the section below; this
  bullet used to say there was none, on a measurement about the *free* clock's long axis.
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
  it; a click is ignored. What adding a target would leave is on the panel's own line rather than beside the
  cursor — `Divert reach: 3.55 km from where the warheads land -- room for 4 more at 2.00 km apart`, with the
  budget under it.
* **Existing targets** are numbered rings, each the lethal radius, so two touching is the spacing floor. The
  lead keeps the aim ring's colour and the rest are dimmer. Clicking one to select it in the panel is not
  built.

**Built, and four things about it are the decisions.** The reach and the numbered rings are drawn for the
craft the panel is showing and no other — the same scoping `SiteDesignator` already had, because only one
computer is being clicked at. **Only one ring is re-draped a frame**, so a six-target set costs a frame what
a single-target shot has always cost; seven deadlines a second are met at any frame rate. The flight that
prices the ellipse — seven of `ImpactPredictor`, 2.6 ms headless, against the readout's one — runs on a
**five-second wall clock rather than only on a change to the set**, which is what the bullet above assumed:
the reach decays as the bus descends, 1,076 to 886 m per m/s over a 330 s schedule, so a footprint held from
the last click is a stale one. And it is flown **only while the list can still be edited**: a coast with
designate mode on, or a second target already placed. `IcbmConfig.ShowDivertReach` turns the whole thing off,
flights included.

**And the region is bounded by one hop, not by the whole budget**, which the bullet above already asks for
and is worth restating because the two numbers differ by four times: `ReleaseItinerary.LeftMetresPerSecond`
says what the trim has left and `BusTrim.MaxMetresPerSecond` what one solve will fly, and the ring is the
lower of the two. The panel quotes both, because "what is left in all" is the question a player asks about
the *set* and "how far from here" the one they ask about the *next click*.

## The pad can draw the pinned reach — measured 2026-09-20

**The refusal above was about the free clock, and the flight does not fly it.** Free-clock the long axis is
`450 · cot γ`, γ belongs to the arc actually flown, and the pad's estimate of it was out by 1.04x, 0.51x and
0.59x. Pinned, that axis is gone: both axes collapse onto the cross-track reach, which is `≈0.96 · t_go` and
has no γ in it at all. **So the one quantity the pad cannot know is the one the pinned reach does not use.**

Swept headlessly over 2,002–12,787 km of range, 13.3°–52.2° of arrival angle and loft 1.0 and 1.35 — 20
geometries, each arc coasted to a 420 s release and the columns flown from there
(`DivertFootprintTests.ThePinnedReachIsTheReleaseEpochAndTheArcCannotMoveIt`):

| | free long axis | pinned reach |
| --- | --- | --- |
| across the matrix | **497 → 1,816** m per m/s, 3.66x | **395 → 407**, 1.03x |

The low end is the 12,787 km shot arriving at 13.3°, the shallowest geometry in the matrix. The disc is a
disc too: major over minor runs 1.000 to 1.022 at that epoch.

**The ratio is the epoch, and it decays with it**, because the out-of-plane response is harmonic about the
body and saturates over a quarter period — `sinc(sqrt(mu/R³) · t_go)` reproduces it to a few per cent:

| t_go | 120 | 240 | 360 | **420** | 550 | 745 | 900 | 1,200 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| reach / t_go, Earth | .936–.993 | .946–.986 | .940–.975 | **.936–.967** | .924–.949 | .889–.917 | .848–.887 | .756–.823 |

The Moon reads **0.977–0.980** at 420 s against Earth's 0.936–0.967, so Earth is the binding body: it has
about the shortest surface-grazing period in the system, and everywhere else the estimate is more
conservative still.

**So `DivertFootprint.PinnedMetresPerSecondToGo` is 0.88 — the floor of the band, not its middle**, and it
is refused outside 60–760 s where it stops being one. At the shipped 420 s gate it draws 370 m per m/s
against a true 395–407, about 7% small: **every click the pad accepts is one the coast's own flown reach
accepts too**, which is the only direction this error may point. `t_go` is the gate exactly, because
`ReleaseItinerary` counts back from it — a new target is always the *last* stop and always releases there.

**What the pad still does not know is the coast's length**, which is what bounds how many stops fit
(`ReleaseItinerary.CoastFits`); at 2,000 km the whole coast is 351 s against a 420 s gate and only one fits.
That is not estimated: the bus is planned with `CoastSeconds` unknown, the panel says the count is settled at
cutoff, and `ReleaseLoop.TrimToWhatFits` drops what does not fit when it is.

**And nothing is flown before the burn is over.** The tempting implementation flies the columns from the
guidance's projected cutoff, and before the vehicle has flown *that projection is the pad* — the same
reading that gave the aim loop 1,522 km of phantom miss and that `AimCorrection.DepartureIsWorthObserving`
exists to refuse. Pinned there is nothing in the footprint the arc could have told us, so the epoch estimate
reads no state at all. The flown path now carries that guard too, so a column can never depart from inside
the air.

## The booster is aimed at the farthest target

**`TargetSet.SetLead` existed and nothing called it; `ElectFarthestLead` is what does.** The lead is
re-elected as the farthest from the launch site whenever a target is added, and **only while the aim is
still free** — `TargetEdit.LeadMayMove`, which is before cutoff and before the arrival is committed. Until
then guidance re-solves velocity-to-be-gained against the vehicle's actual state every cycle, so moving the
aim costs propellant and not accuracy. After it the arc is pinned to an instant chosen for somewhere else,
and past cutoff there is no engine left at all — so a coast add is a divert stop and never a new lead.

That is what closes `ReleaseWalkHold.NotWhereTheBusIsAimed`, which was "every real set today": the itinerary
takes its first stop to be the one the bus already arrives on, and with adds only in the coast the booster
was always flying to the first chosen.

## The hop is charged where it is measured

**A plain bug, and it was accepting every one of these.** `ReachDisplay` bounded a click to one hop *from
the landing* while `ReleaseItinerary` charges the hop *between consecutive stops* — so two targets on
opposite edges of the ring were each accepted at one radius and the pair then cost two, **20 m/s against a
10 m/s ring**, and `ReleaseLoop` refused the walk it had been told was affordable.

The ring is now centred on the stop the next hop leaves from — the landing while the lead is the last stop,
which is every set of one — so the region drawn, the click refused and the hop charged are one measurement.
`ReachDisplayTests.ASecondTargetIsMeasuredFromTheStopTheHopLeavesFrom` asserts the old form would have taken
the click, so it cannot pass against the code it exists to detect.

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
   — with their kicks, and record which tube went where.
3. **Stop** when every assigned warhead is away, or when the time or divert runs out — and say which.

What already exists and is reused: the trajectory solve (`BallisticArc`, `Lambert`), the trim (`BusTrim`),
the correction loop (`PostBoostAim`, `AimCorrection`), the release and kick, the trace. What is new is the loop
around them and the bookkeeping of which warhead belongs to which target — `Sim/ReleaseLoop.cs`, built and
tested, and `Sim/ReleaseWalker.cs`, the cursor `Ksa/IcbmComputer.cs` actuates it through — see *What the
actuation is* below.

**The per-pass ceiling does not need raising after all.** A hop is flown as a fresh null with the correction's
pass count back at zero, so `PostCutoffSequence.CeilingFor` hands it `BusTrim.MaxMetresPerSecond` — which is
the same 10 m/s the ring is drawn at, because `ReachDisplay` deliberately sizes it at
`min(left, BusTrim.MaxMetresPerSecond)`. Ceiling and picture already agree; the loop's job is to *check* a hop
against it and refuse, not to lift it.

### Phase 3 found two things, and the first blocks it — 2026-09-20

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

Every one of these is behind "a walk exists", so a set of one executes none of them.
`Sim/ReleaseWalker.cs` is where the first three live, so the `Ksa/` side has no branch of its own:

* **The release gate moves earlier**, and it is `IcbmProgram.ReleaseGateSeconds` — NaN unless a walk is
  running, so `Coasting` computes exactly what it always did and reads the setting *live*, which latching
  the number would not. `IcbmComputer.SecondsToRelease` reads the same `Program.ReleaseGate`, or the warp
  the correction needs held down comes off `(N-1) x 65 s` late.
* **The sequencer is given the stop's quota**, not the magazine (`ReleaseWalker.TubesLeft`).
  `ReleaseSequence.Emptied` latches at zero and is then the signal that a stop is done — and it is zero
  again once the walk is over, because **a rack reloads a few seconds after a salvo** and would otherwise
  send warheads the plan never assigned anywhere.
* **A handover puts back**: `TargetSet.SetLead`; `AimCorrection.Reset` (the bias is the ground under the *old*
  aim); `IcbmProgram.CorrectCoastArc`; `BusTrim.Resume` — **never `Reset`**, which would zero
  `SpentMetresPerSecond` and hand the budget back; `PostBoostAim.Reset`; `ReleaseSequence.Reset` (its tube
  reference is the old attitude); `SalvoProbe.Forget`, the miss-kick sums and the salvo's flown kick columns,
  because a probe of the previous target's trajectory solves the wrong kick. And `_trimAbandoned`, or a stop
  that released untrimmed leaves every later one untrimmed too. The aim goes through
  `AimCorrection.Retarget` rather than `Reset`: the bias belongs to the old aim and has to go, but the
  plant is the coast's, and `Reset` re-seeds it at the pre-burn `1 / Gain` — quarter steps, which buys a
  pass, and a coast pass is a median 65 s.
* **`SalvoFinished` and `_salvoAway` have to mean "the walk is over"**, not "a warhead has left": both gate
  things that must keep running between stops — `DriveTrim` on the first, `RefreshReach` and `CoastQuiet` on
  the second. `SalvoFinished` reads `ReleaseWalker.Done` because the magazine count cannot answer it: a walk
  that dropped a stop keeps those warheads aboard, so `WarheadsAway` never reaches the salvo's size and the
  trim would solve and fire at a bus with nothing left to release.
* **`PlacedTargets()` emits one entry per target**, including one the world cannot resolve — with no
  warheads, so `Plan` drops it as a stop while it keeps its index. `ReleaseItinerary.Stop.Target` and
  `TargetSet.LeadIndex` index the same list or the bus re-aims at somebody else's target and nothing says so.
* **The walk comes off `ReachDisplay.Flown`**, planned in `ReachDisplay.For` from the same ordered set the
  ring is drawn from, and **latched** by the computer: a frame whose footprint did not come down would
  otherwise take the walk away on the approach to the gate and shut `ReadyToDeploy` mid-walk.

**What scoring reads, and the one thing still on the setting.** `IcbmComputer.TargetOfRound` answers which
of `Targets` a released warhead was sent to, recorded at the instant it left — nothing downstream can
recover it, because the lead moves on at the next handover and reading it at impact reports whichever
target the bus finished on. It answers for a single-target flight too, where every round is target zero.
`Ksa/BallisticScenario.cs` still reads `Config.ReleaseBeforeArrivalSeconds` for its coast warp, which on a
walk lets the world run fast past the first release: it wants `computer.Program.ReleaseGate`, which is the
same number when nothing is walking.

**Two things bit during the wiring, and both are pinned.** `ReleaseWalkHold.Walking` is the enum's *zero*,
so a `default(ReleaseWalk)` — what a computer holds from designation until the reach is priced — claimed to
be a walk with no stops: it would hand over at once, finish, and report the salvo over before a warhead
left. `ReleaseWalk.Walks` now tests the stop count too. And six targets 4 km apart do not fit the 60 m/s cap
on a flat 410 m per m/s reach (64.9 m/s), so `TrimToWhatFits` cuts the set to five and the gate opens for
five stops rather than six — the doc's 55.1 m/s figure is against a reach that *decays* over the schedule.

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

* **Scenario**: `tools/scenario.sh mirv:<lat>,<lon>;<lat>,<lon>;...`, loading its own save as today
  (`KSARMORY_SCENARIO_SAVE`). **Built.** `ShotRequest` already parsed the `;`; `BallisticScenario` now
  designates the first place and `AddTarget`s the rest, balances the warheads over them, and carries the
  seat's own `AimSpread` displacement onto **every** target of the set rather than spreading each one — a set
  spread per target fans, and two rockets displaced toward each other is the overlap `AimSpread` exists to
  open.
* **Scoring**: each warhead against **its own** target, not the group's. **Built** — `Sim/ShotGroup.cs`'s
  `ShotBoard`, one `ShotGroup` per target, judged separately and reported together, with the flight's verdict
  a pass only when every target's is. A warhead is attributed through `IcbmComputer.TargetOfRound`, which is
  the stop the *computer* recorded at the release — **both the release and the arrival read it, and that is
  the part worth not re-deciding**. The lead index observed from outside is a warhead out: a handover lands on
  the same frame the last of a stop's quota leaves, so the release would be counted at the next stop while the
  arrival scored at this one, and both targets would then read a warhead that never arrived. A tube number
  cannot do it either, because the magazine reloads inside `QuietAfterReleaseSeconds` and a walk spans
  minutes.

  **The single-target flight is unchanged by construction, not by inspection.** `ShotGroup` is untouched;
  `ShotBoard.Judge` returns `_groups[0].Judge(bar)` for a set of one, the miss goes through
  `computer.TargetEcl()` exactly as it always did, and neither the `TARGET` line nor the `for target N` on a
  landing line is printed at all below two targets.
  `ShotBoardTests.OneTargetIsWordForWordTheGroupsOwnVerdict` compares the two strings ordinally over four
  shapes of outcome, and fails on all four against a board that takes the multi-target path.

  **And a split flight's FLIGHT line carries no group statistics on purpose.** `worst .. best .. mean ..
  spread` is the shape `tools/shot-report.py` reads a single-target group off, and twenty kilometres of
  intended separation in that shape is a twenty-kilometre miss to every night already flown. The report rebuilds
  those four from the per-target lines instead: `mean` warhead-weighted (still the mean distance of a warhead
  from where **it** was sent, which is what `miss` always scored), and every width measured within a target and
  reduced on the worst of them — the reduction the verdict itself uses, so the two cannot disagree about which
  target decided the flight.
* **Fratricide check**: every assigned warhead arrives and detonates; none is recorded as destroyed in the air.
* **Nights**: the current single-target build against the loop with one target (they must match — the loop
  must cost nothing when it has one target), then 2, 4 and 6 targets.

## How close two targets may be — decided

**Overlapping blast is the player's business, so the floor is the lethal radius rather than the blast
radius.** Decided 2026-09-20. It matters because it is what the spacing tables are read against: at a
6.0 km floor a six-target set only works if the itinerary starts near cutoff, and at 2.0 km it works on
the schedule that ends on today's gate and therefore leaves the single-target shot untouched.

What the region between the two radii actually does, since it is what the player is accepting: damage is
**per part and binary** — a part breaks or it does not, and nothing is dented. A part's failure radius is
`lethal x cbrt(9 MPa / its own strength)`, capped at the blast radius, with the strength KSA's own number
off the part's mass and volume. So a part of reference strength fails at exactly the lethal radius, a
dense one at 0.45x of it, and a flimsy one out to the blast radius. **Between the two radii a neighbour
loses light structure and keeps heavy**; outside the blast radius nothing is touched at all.
`Sim/BlastDamage.cs`.

So the shipped answer is **two to six targets on the gate-ending schedule**, 4.5 km apart at 6,179 km and
3.8 at 12,902, and the early start buys spacing rather than count.

## Phases

| | What | Flies anything? |
| --- | --- | --- |
| ~~0~~ | **Done** — `tests/KSArmory.Tests/MirvDivertTests.cs`. ±100 km is real **only from cutoff**; at today's release gate the footprint is a 34 × 18 km box. See the three findings at the top. | No |
| 1 | **Targets as data**. **Done** — `Sim/TargetSet.cs`, `Sim/TargetEdit.cs`, `ShotRequest` naming several, the list on `IcbmComputer` with `Designate` split from `AddTarget`, clicks that add **before launch and during the coast**, and the panel's rows. Which entry the flight is aimed at is `TargetSet.LeadIndex`, re-elected to the farthest reach by `ElectFarthestLead` on every add while the aim is still free — so the flown order and the itinerary's agree, which is what unblocked phase 3. Where the warheads go is phase 3's, and what `BallisticScenario` designates is still the first place alone. | Unchanged |
| 2 | **Reach display**. **Done, both sides of cutoff** — `Sim/DivertFootprint.cs` is both clocks plus `TryAtTheEpoch` for a flight that has not flown, and `Sim/ReachDisplay.cs` is the drawn region, the cursor's verdict and the panel's readout as one answer; `IcbmOverlay` drapes the ellipse and numbers the targets, and `SiteDesignator` greys the ring and says **outside reach**. **The missile's own region before target 1 is not built** and is the only part of this row left: it is a reach solve per candidate point along a bearing sweep, 37–68 ms a ring, so it wants the few-bearings-a-frame build this row's prose describes. | No |
<<<<<<< HEAD
| 3 | **The release loop**: re-aim per target, per-warhead target bookkeeping in the log. Shared targets release together. **Built and unflown** — `Sim/ReleaseLoop.cs` plans the walk and `Sim/ReleaseWalker.cs` is the cursor the flight is actuated through: `IcbmComputer` opens the gate early, hands the sequencer a stop's quota, and on each handover re-aims, resets the seven things a stop owns and lets `BusTrim` null onto the new solution. The ceiling turned out not to need raising, six targets 4 km apart pricing at 55.1 m/s against the 60 cap. **Both blockers are gone**: the arrival needs no re-latch (`ResolveCoastArc` already solves pinned) and the booster flies to the farthest. **Nothing in it executes for a set of one** — `ReleaseWalker.Walking` is false, so the gate is NaN and the program reads the setting live. | Yes, and never flown |
| 4 | **Instruments and nights**: per-target scoring in `shot-report.py`, the matching check with one target, then 2/4/6. | Yes |
=======
| 3 | **The release loop**: re-aim per target, per-warhead target bookkeeping in the log. Shared targets release together. **The decision half is done and nothing actuates it** — `Sim/ReleaseLoop.cs` plans the walk, cuts a set to what the budget and the coast reach, counts a stop's quota out and refuses honestly; the ceiling turned out not to need raising, six targets 4 km apart pricing at 55.1 m/s against the 60 cap. **Both blockers are gone**: the arrival needs no re-latch (`ResolveCoastArc` already solves pinned) and the booster now flies to the farthest. What remains is the actuation in `Ksa/IcbmComputer.cs`, and it is the first thing here that changes what flies. | Not yet |
| 4 | **Instruments and nights**: per-target scoring in `shot-report.py`, the matching check with one target, then 2/4/6. **The instruments are built and nothing has been flown** — `ShotBoard`, the scenario's `TARGET` lines and per-warhead attribution, and the report's `== targets` section with `miss`, `spread`, `landing`, `centre` and `dispersion` all reduced per target. Checked on a synthetic night and against two real single-target nights that still read byte for byte. | Yes |
>>>>>>> d412631 (test(mirv): score a split salvo one group per target)
| 5 | **Later**: area targets (option C), saving the target list with the craft, reordering by hand. | — |

**A budget problem, and it is smaller than the worst case said.** Five hops at
`BusTrim.MaxMetresPerSecond` plus the 16.1 m/s a single-target flight already spends is **66 m/s against
`PostBoostAim.MaxTrimMetresPerSecond` = 60** — but that charges every hop the ceiling, and a hop of a few
kilometres does not cost it. Six targets 4 km apart at 6,179 km price at **55.1 m/s ending on the gate**, so
five of the six fit the cap and the sixth's warhead stays aboard; `ReleaseLoop` cuts the set to that and says
so rather than flying a stop it cannot pay for. The tank is not the binding constraint (143/101/72 kg).

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

**What blocks phase 3 now is neither of those**: it is that the itinerary's first stop is not where the
booster aimed. See *Phase 3 found two things* above.

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
  one chosen — which is now built, because the set can be assembled before the aim is committed. Unflown, and
  it leaves the bullet below untouched: moving the gate *itself* still costs the first target.
* **What bounds a set is its spacing, not its count** — priced in `tests/KSArmory.Tests/ReleaseTradeTests.cs`
  off the pinned footprint at each slot. Charging every hop `BusTrim.MaxMetresPerSecond` reads
  **66.1 m/s for six against a 60 m/s cap**, and that is the worst case rather than the case: it is a ceiling
  on one *solve*, where a hop's real price is its ground distance over the reach at the slot it is bought in.
  Six 4 km apart at 6,179 km cost **36.9 m/s** started from cutoff and 55.1 ending on the gate.

  The whole budget spent on hops, six targets:

  | | reach per m/s, first → last | neighbour spacing | chain end to end |
  | --- | --- | --- | --- |
  | 6,179 km, from cutoff | 1,076 → 886 m | **8.5 km** | 42.3 km |
  | 6,179 km, ending on the gate | 688 → 410 m | **4.5 km** | 22.5 km |
  | 12,902 km, from cutoff | 1,647 → 1,310 m | **12.6 km** | 63.0 km |
  | 12,902 km, ending on the gate | 606 → 346 m | **3.8 km** | 19.2 km |

  The same trade read as a count, at a spacing somebody would pick:

  | spacing | 6,179 km cutoff | 6,179 km gate | 12,902 km cutoff | 12,902 km gate |
  | --- | --- | --- | --- | --- |
  | 4 km | 6 | 6 | 6 | 5 |
  | 10 km | 5 | 2 | 6 | 2 |
  | 25 km | 2 | 1 | 3 | 1 |
  | 50 km | 1 | 1 | 2 | 1 |

  **And the window the feature lives in closes from about five targets on.** `Warhead.BlastRadius` is 6.0 km
  for the Mk 21, so anything nearer than that is option A' or option C rather than a second target. Ending on
  the gate the widest affordable spacing is 6.8 km at four targets, **5.4 at five and 4.5 at six** (6,179 km),
  and 5.8 / 4.6 / 3.8 at 12,902 — inside the warhead. From cutoff it stays open throughout, 8.5 km at six and
  12.6 at 12,902. **So two to four targets are a real feature at either schedule, and five or six are only
  real if the itinerary starts near cutoff** — which is the gate's paired-arm question again, now with a
  number on what it buys rather than only on what it costs.
* **When the release loop runs is the design decision, not a tuning.** `ReleaseBeforeArrivalSeconds = 420` is
  a single-target optimisation: it shrinks the ejection kick's leverage, which is why the group lands
  millimetres from the aim. For six targets it costs **4x the reach at 6,179 km and 7x at 12,902**. Moving it
  multiplies every velocity residual by the same factor — the trim's floor goes 14 m → 55 m — which against a
  2 km lethal radius is nothing and against the batch scoring is everything. **A paired-arm question before
  phase 3, not an assumption.**
* ~~**Does the first target stay special?**~~ **Answered, and built: the booster flies to the farthest, not
  to the first clicked.** `TargetSet.ElectFarthestLead` re-elects the lead on every add while the aim is
  still free. Aiming at the middle of the set instead halves the largest divert and makes the first release
  a divert too — untried, and it would need the lead to stop being the stop that costs nothing, which is
  what `ReleaseLoop` is built around. **Unflown either way.**
* **Release before or after the aim correction for target 1?** Today the correction runs once. With several
  targets it runs once per target; whether the first one's can be shortened is a phase 0 question.
* **What the player sees in flight**: which target the bus is currently aimed at, and a countdown per target.
