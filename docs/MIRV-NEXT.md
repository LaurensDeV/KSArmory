# What is left on the MIRV bus

Everything below is either measured in flight or explicitly marked as untested. `docs/ICBM-GUIDANCE.md` has the algorithm;
`CHECKLIST.md` §12.7 has the in-flight list. This file is only the backlog.

**Most of it is now a record rather than a plan**, and the item numbers are the addressing scheme —
forty-three references from other docs and from source comments point at them, so a section keeps
its number wherever its state ends up. What is still open:

| | |
| --- | --- |
| [0c](#0c-the-budget-stopped-the-trim-dead-and-a-stopped-trim-never-lets-the-warheads-go--fixed-unflown) | **the budget held every warhead aboard — fixed, and it wants the flight** |
| [0d](#0d-a-reloaded-launcher-gets-a-second-salvo-and-a-tenth--fixed-unflown) | **the bus fired ten salvos, so every group figure was over sixty warheads — fixed** |
| [1](#1-null-the-separation-impulse---reformulated-unflown) | null the separation impulse — reformulated, never flown as its own arm |
| [2](#2-the-walk-was-the-planets-own-fall-toward-the-sun--flown-fixed-and-a-quarter-of-what-it-was) | **flown and fixed — 2.88 to 0.72 km — and 0.48 km was flown on 2026-08-22.** The walk grew sevenfold in two days; bisect it |
| [2g](#2g-the-body-fall-is-summed-over-one-link-so-a-lunar-shot-keeps-most-of-it) | the body fall is summed over one link, so a lunar shot keeps most of the fault — unflown |
| [6](#6-point-the-bus-at-the-target-on-release---cosmetic-do-it-last) | point the bus at the target on release — cosmetic |
| [7e](#7e-that-05-km-is-one-latched-warp-decision-not-frame-pacing---measured-fixed-headlessly) | the warp latch — **flown and closed**, by the harness rather than by the constant |
| [7f](#7f-three-stopping-rules-were-sized-for-a-kilometre-shot--built-as-an-arm-unflown) | **three stopping rules sized for a kilometre shot — built as `arm/aim`, wants the night** |
| [7g](#7g-never-freezing-loses-and-the-target-is-on-a-crest--flown-2026-08-25) | **flown and closed — never-freezing loses 5.7x, and the target sits on a crest the arrival is parallel to** |
| [9](#9-the-budget-at-the-065-km-level) | the ranked budget, of which #2 (reopening the aim after cutoff) is the live one |

Everything else is flown, closed, or retired, and says so in its own heading.

> **The bus is 2,750 kg of structure, not 6,300.** Its mass was set from an Mk 21 at about 800 kg,
> where Peacekeeper's throw weight puts one nearer 250 — so every flown number below was measured
> on a vehicle 2.3 times heavier than the one that ships now. The mass ratio, the trim's authority
> (thrust over mass) and the pointing band all move with it, and `BusTrim`'s thresholds and the RCS
> nozzle sizing were both tuned against the old figure. **Re-baseline before comparing anything
> here against a new shot.**
>
> It was corrected because it was wrong, and because 6,300 kg on the nose made a stack chatter
> against the ground hard enough to be unflyable: measured at ±2,116 deg/s² of angular acceleration
> while in contact, against nothing measurable at all afterwards.
>
> **And the mass is not the only thing that moved.** On 2026-08-23 the shot also changed at five
> other points, each of which shifts where a warhead lands:
>
> | | |
> | --- | --- |
> | release | on time to arrival rather than on 100 km of altitude, so they leave near the end of the coast rather than near the start |
> | staging | a multi-stage stack stages at all; before, the guard refused every stage and the second one never lit |
> | reach | judged on the stack's own delta-v across every stage, not on the running stage over the whole load |
> | arrival floor | a floor nothing satisfies is flown at what is affordable rather than refused |
> | coast warp | 100x through the coast with a hand-back before release, where it was 8x from the first release |
>
> Two of those change the *frame the rounds are integrated at* and one changes *when they are let
> go*, so the walk, the spread and the group's own miss are all measured against a different flight
> from the one every table below records. **Nothing here is comparable to a shot flown after this
> date.** The night in item 7e is the one to re-fly first, and it wants re-flying whole rather than
> as one arm against a stale baseline.

## Where it stands

Flown 20 August, with the trim in and tube re-pointing off. Six warheads at one aim point:

| | miss |
| --- | --- |
| round 1 | **431 m** |
| round 2 | 537 m |
| round 6 | 607 m |
| round 3 | 1.1 km |
| round 5 | 1.2 km |
| round 4 | 1.4 km |

Against 3,100-4,100 m the flight before. All six left within 67 ms and landed within 32 ms of each
other, so every warhead shared a time-to-impact — which is what the old three-minute salvo was
costing.

**What is left is two terms, and they are separable.** The ~1 km *spread* is the tube cant, which
is item 5. The ~900 m *bias* — every round landed the same way off — is mostly the aim correction's
frozen residue, which is item 9; item 2c retired the epoch fault that was on top of it.

**Since then, all from one save at 26.5S 64.0W:** a median **0.38 km** over eight shots once the
pointing deadband was re-derived (item 5), and **0.80 km** over twelve after the retarget to KSA
`2026.8.22.5348` (item 2e, and item 2f for what was hiding inside that).

### What the trim cost to get there

The decoupler is worth about 1.1 m/s against a ~6,300 kg bus, arriving after the last thing that
could compensate for it, and at cutoff attitude most of it lands on the radial axis — the expensive
one at 3,401 m per m/s against 1,769 along-track and 780 cross-track. Before the trim existed that
was 3.5 km of the miss. It now goes out in under two seconds, `trimmed to 0.010 m/s`, off a cutoff
that was already `0.0 km off`.

Two failures on the way, both worth not repeating:

- **The trim and the aim correction wound each other up.** Both drive the vehicle and both read the
  same prediction, so the bias absorbed a displacement the trim had put there. 0.28 → 139 m/s in ten
  seconds, jumping every 0.51 s, which is `PredictIntervalSeconds` exactly.
- **Nulling the shove ends the separation**, because the shove *is* the separation velocity. The bus
  trimmed 130 ms after the split at 12 m and then sat against the booster it had just dropped.

## -2. Ten from ten is not chance, and the correction is not what does it

**Ten changes flown in one session, every one argued and measured beforehand, every one refused.**
Item -1a explains three and item -1 explains the shape of the rest. What follows is what those two
do *not* cover, and it changes where the next flight should be pointed.

The obvious suspect is the aim correction — a loop that converges the predictor onto the target
will absorb whatever it can see, so a physics fix might be re-expressed as bias rather than as
accuracy. **It is not that**, and the reason is structural rather than measured.

### The correction cannot see the round, so a round-only fix arrives undamped

`_aim.Observe(hit.GroundFixedPointCci, _trueAimCci)` is the only observation, and `hit` comes from
`ImpactPredictor.TryPredict` flown from `Program.CutoffPositionCci`, `Arc.RequiredVelocityCci`,
`ReleaseOffsetCci()` and `ReleaseImpulseCci()`. **No `Slug` appears anywhere on that path.** So for
a change confined to the round's own flight model, the bias, the trajectory, the arrival latch and
the release sequence are all unchanged, and

```
Δmiss = G × Δgap                 gap = round − its own predictor
```

The loop is a **subtractor**, not an absorber: it removes everything the predictor can see and
leaves exactly `gap` on the ground. That is why item 2's whole decomposition is the right frame for
six of the ten — the star's gravity, the per-sub-step gravity, the 1 ms sub-step, the capped
faithful step, the shorter coast frame and the force-sample phase are all round-only.

**So the hypothesis to abandon is that the correction converts improvements into regressions.** It
converts them into `G × Δgap`, and `G` is the term nobody has measured.

### `G` is singular at this arrival angle, and past the singularity it is negative

`docs/KINETIC-FLOOR.md` §5 has the gain as `s / tan(gamma)` with the amplification
`1/(1 − s/tan gamma)`. At the flown **7.1 degrees**, `tan gamma` is 0.1246 — so a downrange ground
slope of **12.5%** is a pole, and anything steeper inverts the sign: moving the arc further
downrange moves the *impact* uprange. That file already says there is no fixed point at gain one or
above, and that `EarthErosion` allows per-octave slopes to **0.30**, and that what the product
actually is "needs the game".

That un-measured number multiplies every headless prediction in this file. Three of its
consequences fit the flown record better than anything else on the list:

- **The sign is not predictable**, so a correct fix losing is the expected outcome rather than a
  surprise.
- **The magnitude is not bounded**, which is how 200-700 m of priced improvement comes back as
  1-3 km of flown regression.
- **It is re-drawn wherever the round happens to land**, which is a per-shot lottery and fits the
  run-to-run scatter without needing the loop to explain it.

The drag term scales the same way and is worse: `docs/ARRIVAL-ANGLE.md` prices a ten per cent
drag-model error at **1.8 km** at 7.5 degrees against **77 m** at 15 and **29 m** at 20 — a factor
of 62 — and a drag-model disagreement is precisely what `gap` is made of.

**The gain itself is no longer unmeasured**: item 2's trace reading prices it at **4 m** on the
flown shot, where the walk moves 1,939 -> 1,943 m over the last 394 m of altitude and the two
surfaces agree at the landing point to 0.0 m. So it is small on the ground this shot lands on. The
drag term is not.

### For the four loop-touching changes, the baseline is a selected survivor

Item 9 measures the frozen residue at **760 m** of an 850 m median, and the same flight with
`Freeze()` never called lands the group at **18 m**. Item 7 measures the loop's plant as its own
output history — two runs from an identical save, at the same 28.6 km of bias, predicted 54.3 km
and 0.06 km. So the residue is path-dependent, and the observer gate, the burn convergence, the
arrival floor and the lateral jets all re-draw it.

**The shipped build is not a random draw from that distribution — it is the survivor of roughly ten
previous accept/reject decisions**, each taken on six shots. It was therefore selected for a low
draw, and a re-draw loses in expectation whatever the change was worth. That is the winner's curse,
and it produces ten from ten without any of the ten being wrong.

### Two harness confounds, both of which change the physics under test

- **The coast speed used to depend on whether the magazine emptied — closed.**
  `BallisticScenario.WarpTheCoast` was called only from the `ammo <= 0` branch, so a shot that held
  a warhead back coasted at **1x** and one that emptied coasted at **8x**. Item 2's table puts
  `gap` at **−73 m** and **+394 m** respectively, so the term under test changed sign with an
  unrelated outcome. It now warps as soon as anything has left.
- **The 0.01x pick-up pin has no `else`.** It is asked for only when `KsaWorld.IsPaused`, so a world
  already running gets no pin at all, and the only thing separating the two cases in the log is the
  presence of `the world was paused; asked for 0.01x`. Item 7d prices a different pick-up at
  **164 km**.

And the miss the harness reports is a **3-D Euclidean distance** (`BallisticScenario.MissFromAim`),
so it is unsigned: short, long and cross-track are one number, and a gain inversion is invisible in
it. The decomposition exists only in `Ksa/WarheadTrace.cs`, which reports the walk downrange and
cross-track — and which `ScenarioRunner` already switches on for every `mirv` run.

### What settled it — flown 22 August

**The trace was read first**, off the log the scenario already writes: `release probe:`,
`warhead trace: probe from the round's own state`, the per-cycle
`walk from the release probe M m (+D down, +C cross)`, and the impact line against both the aim and
the probe. Item 2's reading section is what it said, and it separated `gap` from `G` before
anything flew.

Then the 2x2 at one aim point, twelve blocks budgeted. It did not need them: both ablated arms were
dropped at the gate after two blocks and the finer step after six shots.

| | shipped | + `Arsenal.ReentryVehicleMk21.PreferredStepSeconds = 0.05f` |
| --- | --- | --- |
| **shipped** | **1.65-2.03 km**, 22 shots | **3.30-3.72 km**, 6 shots |
| **`AimCorrection.MaxMetres = 0.0`** | **76.4 / 76.8 km** | **81.5 / 82.1 km** |

`MaxMetres = 0` is a clean total ablation — `Vec.ClampLength(v, 0)` returns zero, `Apply`
degenerates to the identity, and the post-boost loop goes with it because the bias is its actuator.
What it is not is a noise floor. **The loop is worth seventy-five kilometres**, so there is no scale
on which this shot's physics can be read raw, and every number in this file is one taken through the
correction.

**`Δmiss` is not the same in the two correction arms** — 1.6 km with the loop against 5.2 km
without it, the second on two shots a cell — so the loop is not simply subtracting what it can see.
Both are dwarfed by what the loop itself is worth, which is why that is as far as the reading goes.

The finer step is item 2's own candidate flown at the wrong value, and it doubled the miss. `f619daa`
later measured why: the round's disagreement with its predictor is two terms of opposite sign, so it
crosses zero near a 138 ms frame and overshoots the other way below it — shortening the coast frame
without bound is a regression. `PreferredStepSeconds` now sits at **0.225**, the reachable frame
nearest that crossing, flown five against five for a median 1.66 -> 1.06 km.

## -1a. Two errors were cancelling — and it was not the round against its predictor

`KsaWorld.GravityAt` gives a round only its parent body's pull, and Ecl is heliocentric — so the
engine carries the planet along its orbit and the round is not carried with it. That is a real
omission, worth 733 m over an eight-minute coast and up to 9.7 km at the Mk 21's flight-time limit.
Adding the star's pull makes the round's flight **more correct**.

Flown four times: **4.78 / 4.78 / 5.93 / 5.93 km** against a 1.67 km baseline. Reverted.

> **The mechanism below is wrong, and the same change has since won.** `8519c5d` is physically this
> term — they differ by the solar tide across a planet's radius, 0.0086%, or 3.6 cm over a 375 s coast
> — and it flew **10 from 10 at ratio 0.25, p 0.000**, taking the group from 2.88 km to 0.72. What was
> wrong here was not the flights but the reading of them, so the paragraph is kept and corrected
> rather than deleted: it is cited elsewhere as a general rule.

**`ImpactPredictor` does not model the term and does not need to**, because it works in the body's
own frame — and a body-centred non-rotating frame is *freely falling*, so the whole ancestral chain
cancels there at every depth, leaving only that 3.6 cm of tide.

**But that makes the predictor right and the round wrong, which is a difference and not a shared
error.** The round, integrated in `Ecl` against its parent body's pull alone, carried a spurious
`-a_body` relative to the ground for the whole coast; the predictor carried none. The claim that the
two models were "wrong identically" is the error in this section, and it inverts the conclusion:
correcting the round converts a *difference into agreement*, which is exactly what the flight
measured.

**What actually made these four flights lose is a second error of the opposite sign.** On the
2026-08-22 configuration the walk was only **-427 m**, not the ~-2,540 m this term is worth — because
the coast was still being warped, and a coarse coast frame walks a round *long*. Removing the solar
term alone therefore unmasked ~+2,100 m of coast-frame walk, which is the right sign and rough size
for the 4.78-5.93 km measured. Item 2 has the three-night table.

So the rule this section states is sound but was applied to the wrong pair. **The round and its
predictor were not the cancelling pair here; two independent errors in the round were.**

**The rule, which is more general than this one term:** the round and the instrument that aims it
are a pair. A change to either is only safe if the other moves with it, and an *improvement* to one
is a regression unless it is. Three of this session's failures are the same shape — this, the
per-sub-step gravity that removed the half of a cancelling pair (item 2d), and the force-sample phase
(`KSA-FRAME-ORDER.md` §5), which corrected the round by a whole frame where the flight behaves as if
its samples are a tenth of one stale.

## -1. The headless rig and the game disagree, and the game has won every time

**Seven changes this session were measured headlessly, argued from the code, and refused by
flight.** The force-sample phase correction, per-sub-step gravity, the lateral thrusters, a 1 ms
integration step, the observer-gate rewrite, the burn-convergence fix, and the 15-degree arrival
floor. Not one survived a shot.

Measured on one pick-up, medians over the batches flown:

| build | shots | median | mean | worst |
| --- | --- | --- | --- | --- |
| **shipped** | 16 | **0.85 km** | **1.20 km** | 3.43 km |
| + burn convergence | 11 | 1.29 km | 2.60 km | 6.48 km |
| + burn convergence + gate rewrite | 6 | 2.14 km | 3.28 km | 7.46 km |

Monotonic, and each of those two was predicted headlessly to be a large improvement — 717 m -> 26 m
for the first, 1,793 m against 4,483 m for the second.

**Why the rig is wrong in a particular direction.** It flies a planet at the origin with no orbital
motion, which is the one case where a frame carrier is identically zero. Every change that has
*worked* this session came from finding two quantities compared across different instants — the
prediction's epoch, the terrain sample's epoch, the air sample's epoch, the tube geometry. The rig
cannot see any of that. What it *can* see is control-loop arithmetic, and that is exactly the class
it has been confidently wrong about seven times.

**And there is a second reason, specific to the aim correction and now measured.** That loop's only
observer is `ImpactPredictor`, and every rig here flew it over a **mean sphere** — so the observer is
noiseless. A change that lets the loop run longer is then free averaging of a clean signal and cannot
lose, which is precisely the shape five of the seven refusals had.
`DeorbitShot.RoughGround` gives the predictor relief and the height field's own 0.2985 m quantum to
cross, and the *shipped* configuration moves by kilometres where the smooth rig reports tens of metres:

| floor off, as shipped | 2,000 km | 3,459 km | 7,645 km |
| --- | --- | --- | --- |
| mean sphere | 0.01 km | 0.38 km | 1.15 km |
| with relief | **4.31 km** | 1.35 km | **3.82 km** |

So a correction-loop result taken on a smooth planet is not merely unproven, it is measured against an
instrument the real one does not have.
`ArrivalFloorFlightTests.GroundUnderTheObserverMovesTheShippedShot` holds it, and the next attempt
here should start rough.

So the two instruments are not ranked, they are **complementary and each blind where the other is
sharp**. The rig is the only way to price a term; flight is the only way to find out whether the
term was the one that mattered.

**What follows for anyone working here.** A headless improvement is a hypothesis, not a result, and
the bar in `CLAUDE.md` — a behaviour change is unverified until flown — is doing more work than it
appears to. Budget flights for every behaviour change, expect roughly half to lose, and treat a
change that makes the loop act *more* on its own prediction as guilty until proven innocent: five of
the seven were exactly that shape.

**And note what a single batch cannot resolve.** Run-to-run scatter on an identical pick-up is
roughly +/- 700 m with a tail to 3.4 km, so six shots settle a change worth a kilometre and settle
nothing worth two hundred metres. Several of the reverted changes were priced below that line and
could never have been confirmed either way.

**`docs/SHOT-PROTOCOL.md` is what to do about that**, with the arithmetic done: on the ten shots
recorded here the distribution is lognormal about a 0.79 km median with a geometric sd of 1.74, so
a **300 m** difference costs **25 shots an arm** at four-in-five odds and a whole night buys one
such question — or two, flown as a 2x2 factorial, which is also the only way to see a change that
only helps in combination. `tools/shot-batch.sh` runs it and `tools/shot-report.py` says what it
settled.

## -0b. Capping the warhead's faithful step — flown, catastrophic, reverted

`MunitionProfile.MaxFaithfulStepSeconds` on the Mk 21, 0.32 -> 0.10, to shorten the coast frame the
round is integrated across. Priced headlessly at **394 m -> 87 m** through the real `WarpPolicy`.

Flown four times: **48.38 / 52.79 / 48.08 / 59.58 km.** Reverted.

**It is the integration clamp, not only the warp target**, and the two do opposite things. That field
feeds `WeaponSystems.FaithfulStep`, whose own remarks say it plainly: *"It bounds a clamp that
discards time, so tightening it does not slow anything down — it makes the round fall behind the
world."* Every coast step longer than the cap had its excess thrown away, and fifty kilometres is
what four hundred seconds of that costs.

**The same confusion cost 164 km earlier the same day**, from the other direction — feeding
`WarpTargetStep`'s question to `FaithfulStep`'s consumer. Twice now, so it is worth stating as a
rule: **`FaithfulStep` is how short a step the round can still integrate, and tightening it drops
time on the floor. `WarpTargetStep` is how short a step the round would prefer, and tightening it
slows the world.** Only the second buys accuracy. They are two questions with one shape and the
answer to one is never the answer to the other.

The headless price was not wrong about the flight model; it simply did not model the clamp, because
the rig integrates whatever step it is handed.

## 0c. The budget stopped the trim dead, and a stopped trim never lets the warheads go — fixed, unflown

**Nothing left the bus.** Flown 2026-08-24 from `AUTO NUKE DECOUPLER` at 26.5S 64.0W: `away from
tube` appears **zero** times in the whole log, and the vehicle rode to 7 km with all six aboard
reporting `holding, 215.73 m/s off the solution`. Seen from the panel it reads as a normal coast —
`IMPACT IF RELEASED NOW 2:39` beside a planned arc — which is why it survived a whole evening.

Three things are tangled in it and only the first is the fault.

**A stop expressed as withholding fire never lifts.** `IcbmComputer` enforced the flight-long trim
budget by clearing `TrimSituation.MayFire`. `BusTrim.Update` returns early on that *without*
finishing, and cannot finish afterwards: `_since` only advances while firing, and only firing spends
the tank. `IcbmComputer` then holds the salvo on `!_trim.Done`. So the budget, once reported spent,
held the warheads for the rest of the flight. `BusTrim`'s own contract already said what should
happen — **it gives up rather than holding warheads** — and the budget was the one stop that did not
obey it.

**And it was reported spent at about 10 m/s of 25.** `BusTrim.SpentMetresPerSecond` is cumulative
across every null: `Resume` re-arms onto a fresh reference and a tank does not refill with it, which
`WhatTheThrustersSpendIsCountedAcrossPassesRatherThanPerNull` has pinned since it was written.
`IcbmComputer` kept a second total beside it, banking that running figure whenever a null finished
and then adding the running figure to the bank — so each finished null was counted once more for
every null after it. The overcount grows with the square of the pass count rather than with the
propellant, so it arrives suddenly and never clears. Flown: twelve nulls at about 2.5 m/s each, and
`budget of 25 m/s spent` with roughly ten actually gone.

**The clearance line is downstream of it, not the cause.** `waiting to clear the spent stack, which
cannot be read` reads like the fault. `DriveTrim` nulls `_separatedFrom` the moment the trim reports
done, so every cycle after the first null has no distance left to measure; the check falls back to
the clock, which answers **clear**. It never withheld anything, and `going ahead with no clearance
reading after 260 s` is that fallback working as written.

**Fixed by giving the budget to the thing that owns the tank.** `TrimSituation.BudgetMetresPerSecond`
goes in, and `BusTrim` finishes on it — `gaveUp`, so the salvo leaves on the aim it has rather than
not at all. Nothing outside keeps a second total, which makes the double-count unreachable rather
than merely corrected.

Two tests, both of which fail against the old behaviour and were checked to:
`ASpentBudgetEndsTheTrim_WhereAClearanceWaitOnlyPausesIt` separates the stop that lifts from the one
that does not, and `TheBudgetIsWhatTheTankLost_NotWhatEachNullAddsToEveryNullAfterIt` asks the
question **every frame** — asked once at the end it passes against the defect, because the bank only
takes its step up on the frames a finished trim is sitting idle.

**What is still open.** Whether a trim allowed to keep running actually improves the group is a
flight question and not answered here: item 0 is the record of an actuator that worked and made the
shot four times worse. What is answered is that the shot could not be scored at all.

**And the log spam is worth a look while nearby.** `Say` de-duplicates on the sentence, and the
clearance sentence carries a running second count — so it logs every frame instead of once per
change: 21,000 lines from one coast. Frame time is what item 7e says sets the coast step, so this is
not only untidiness.

## 0d. A reloaded launcher gets a second salvo, and a tenth — fixed, unflown

**A six-tube bus put sixty warheads down.** Flown 2026-08-24 — the first flight on which anything
was released at all, item 0c being why. The salvo that matters is the first six: **2.88-2.90 km,
ten metres apart**. Everything after it is the launcher doing what a launcher does:

```
00:16:12.426  holding fire: reloading (3 s)
00:16:15.376  launcher reloaded
```

Three seconds after a salvo the magazine is full again. Nothing in the ballistic computer said a
deployment happens once, so the next six went the moment the first six landed, and the six after
that when *those* landed — at 44 s, then 37, 30, 23, 18, 12, 7, 3 and 1 seconds of flight, each
salvo from lower down. The last nine groups all read **606.5 km**, which is simply where the bus
itself came down.

**The reload is right and the second salvo is not**, which is why the latch is in the sequencer and
not in the magazine: a Pantsir reloading its pods is the same code, and a weapon pack's launcher may
reload however it likes. `ReleaseSequence.Emptied` latches on the magazine reaching empty — watched
rather than counted, because how many it started with is the launcher's business and a reload is
indistinguishable from a fuller load — and only `Reset` clears it, which is a new flight.

`AReloadedLauncherDoesNotGetASecondSalvo` reads *6 away, 6 after the reload* against the old code
and *6 away, 0* against this.

**And the latch did not work on its first flight, for a reason worth keeping.** `IcbmComputer` passed
`TubesLeft: Math.Max(1, weapon.TubesReadyToFire)`, so the magazine reported one round left forever
and the sequencer never saw it empty — sixty warheads again, from a build whose unit test said six.
The floor was there to protect a division that already guards zero itself. **A quantity with a floor
under it cannot express "none"**, so anything downstream that has to notice *none* is broken by the
floor rather than by the arithmetic it was protecting. The test could not catch it: it hands the
sequencer the count directly, and the distortion was in the caller, under `Ksa/`, where the test
project cannot reach. Every other `Math.Max(1, …)` in the mod guards a divisor or a stride, where
zero has no meaning and nothing downstream is watching for it.

**What it cost before it was found**: every figure a scenario run reports is taken over the whole
group, so with sixty warheads in it `worst`, `mean` and `spread` are meaningless and the verdict is
always FAIL. `best` was the only honest number on the sheet, and it was the real salvo's.

## 0a. A quieter ejection kick — flown, and it takes the tail off

`MunitionProfile.LaunchSpeed` on the Mk 21, 2 m/s -> **0.5**. Flown six times against the sixteen-shot
baseline:

| | shots | median | worst |
| --- | --- | --- | --- |
| 2 m/s | 16 | 0.85 km | **3.43 km** |
| **0.5 m/s** | 6 | 0.89 km | **1.13 km** |

**The median barely moves and the tail disappears**, which is the mechanism rather than luck. The
impact moves **3,979 m per m/s of kick**, measured constant to 0.3% from 0.1 to 2 — and the bad runs
were that leverage multiplied by a nose the engine will not hold still. Divide the leverage by four
and the tail divides with it; the median is set by other terms and does not.

Everything downstream scaled as predicted. Across the flown drift band the nose moves the reading
**8.7-13.5 km -> 2.17 km**; holding a warhead costs **26 -> ~6.5 m of miss per second**; and the
nose is no longer the largest term present — at 2.44 km it now sits *under* a wholly un-nulled
separation shove at 3.79.

**What makes it safe is a change already flown.** With the tubes on a 6-degree cone the kick *was*
the separation, and quietening it would have put six warheads on converging paths. Parallel on an
0.86 m bolt circle they never converge, so the kick only has to unseat them: at 0.5 m/s they are 5 m
clear after ten seconds and never approach again. It is also what a real post-boost vehicle does —
ejection is for clearance, and the aiming is done by manoeuvring.

**Four tests kept the loud specimen** (`tests/KSArmory.Tests/LoudKick.cs`), the same shape as
`CantedRing` after the tubes were straightened: they are about what a kick *does*, and a weapon pack
may still register a loud one. Two were claims about the shipped round and were retuned, one of them
because its ordering genuinely inverted.

## 0. Radial translation jets — flown, reverted, and back in squared

**They made the shot four times worse and were taken out again.** Seven shots with them against six
without, same pick-up, everything else identical:

| | shots | median | worst | failures |
| --- | --- | --- | --- | --- |
| without jets | 0.38 / 0.63 / 0.70 / 0.96 / 1.18 / 1.47 km | **0.83 km** | 1.47 km | 0 |
| **with jets** | 0.09 / 0.41 / 0.83 / 3.26 / 4.43 / 10.60 / 18.01 km | **3.26 km** | **18.01 km** | 2 |
| after the revert | 0.42 / 0.49 / 0.82 / 2.10 km | 0.66 km | 2.10 km | 0 |

The third row is the control: reverting restores the first, so the difference is the jets and not
something that drifted alongside them. Ten shots without them now, none failing, worst 2.10 km.

The best single result ever measured here is in that second row — 0.09 km — which is the trap. The
jets do work, and when the correction is good they beat anything else. What they remove is the
*floor* under how wrong it can go.

**Why more actuator was worse.** `BusTrim` used to strike lateral directions off as dead, and that
was accidentally protective: it bounded how far the post-boost correction could move the bus,
whatever it asked for. With the jets it follows — including onto a bad reading. And the readings are
known not to be trustworthy yet: the modelled release direction swings the predicted impact
8.7–12.8 km across the throw band the bus actually drifts through (item 8b), and the gate that
refuses those readings admits fewer of them the more the trim fires, because it waits for the
thrusters to go quiet. The last flight before the revert took **two** passes and released with 3.7 km
still on the table.

So the chain is: more authority → more trimming → fewer quiet windows → fewer passes → a correction
that stops early, now able to act on whatever it last believed.

**What would make them worth re-adding**, in order:

1. **A correction worth following.** The observer gate was the first half. What it has not fixed is
   that a bus with `control part NONE` and `roll Decoupled` inside a 22° dead zone rarely holds still
   long enough to be measured at all. Until a reading is reliable, authority amplifies.
2. **The steep-arrival case, which still needs them.** `docs/ARRIVAL-ANGLE.md` records a constrained
   shot asking the trim for eleven metres a second and being refused. It is **not** a price for
   arriving steeply — at a pinned arrival a kilometre of aim costs 1.468 m/s at 20° against 2.145 at
   3.6°, so steep is slightly cheaper. It is seven kilometres of aim error the burn handed over,
   built by the correction out of the trajectory search's own transient on a shot that needed no
   correction at all.
3. **The mass declaration** — now understood, and acted on. See below.

**They are back, and not by a revert.** The ring is now four clusters of five — an axial pair, an
exactly tangential roll pair and one radial jet on the cluster bisector — so a lateral command
reaches the radial four and nothing else, where the canted pair it replaced cleared KSA's 0.5
threshold on both axes and left a torque behind every shove. It shipped inside the arm that
re-derived the pointing deadband, which won as a unit; the jets in this form have never been scored
against a baseline on their own.

**The engine rule they established stands regardless**, and is the durable part of the work:
`ThrusterController.ComputeControlMap` flags a nozzle for a translation on any thrust component over
**0.5** — a 60° half cone, judged per nozzle with no reference to lever arms or to the layout as a
whole. That is why one radial jet per cluster serves every lateral direction, and why the whole ring
lights up for a single command.

### The rotation rule is a different rule, and it is what the mass seat was doing

**Rotation is enrolled at 0.1, not 0.5, and the enrolled set is summed.** A nozzle joins a rotation
axis when `dot(thrust, normalize(axis × r))` exceeds **0.1** — a far wider net than translation's
60° cone, and one that depends only on *direction*, not on how long the lever is. What is then
added to `MinRotationalImpulse` for that axis is the nozzle's **full torque**, accumulated across
every nozzle enrolled. So the quantum that sets the attitude deadband is a property of the whole
ring, and a nozzle nobody intended to steer with still coarsens it.

That is what the missing `<LocationAsmb>` was worth. Every non-axial nozzle sits at X = 0.30 and
the mass sat at X = 0, so all twelve carried a 0.30 m axial lever arm and enrolled in pitch and yaw:

| ring | pitch/yaw quantum | enrolled |
| --- | --- | --- |
| canted 16 | 4.976 | 12 |
| canted + radial 20 | 5.408 | 16 |
| squared + radial 20 | 5.685 | 20 |
| **any of them, mass seated at X = 0.30** | **4.412** | **8** |

4.412 is `4 × 1.103` — the axial nozzles alone, whose lever arm is in the radial plane whatever the
mass seat is. It is the floor for this ring, and seating the mass in the ring plane reaches it.

It also explains a flown result rather than only predicting one: the canted+radial arm carried
1.085× the quantum and flew with **26% more attitude walk** than the arm without it, 1,316 m against
1,044 m, while beating it on both cutoff residual and trim. More actuator, worse hold.

**The seat is a choice and is written down as one.** The loaded centroid is near X = 1.77, and
putting it there would give every nozzle a 1.4 m arm and a far worse quantum. It sits inside a mass
model that is already approximate — the six RVs are rounds this mod simulates and carry no KSA mass,
so the 6,300 kg never sheds as they leave.

**And what it costs is velocity, not pointing.** That is what made it invisible. All three arms
release their warheads at **0.00 degrees** off the salvo's line, and the arm that trims *tightest*
(0.016 m/s) misses *worst* — so neither release direction nor trim residual is the channel.

A correctly enrolled set is a pure couple. A wrongly enrolled one is not, so every attitude
correction is also a shove:

| command | base: nozzles | net force | seated: nozzles | net force |
| --- | --- | --- | --- | --- |
| pitch± | 6 | **1.879** | 4 | **0.000** |
| yaw± | 6 | **1.879** | 4 | **0.000** |
| roll± | 4 | 0.000 | 4 | 0.000 |

0.378 of net force per unit of commanded torque, against 0.000 once the mass is seated. Combined
with a deadband ratcheted wide by the separation transient — which makes those corrections both
large and frequent — that is the whole path from a missing `<LocationAsmb>` to a kilometre of miss.

`tools/model/checkring.py` gates it. Nothing else here could: the mesh is clean, the pivots agree,
`checkswept.py` finds no intersection, and the vehicle simply holds its nose less well than it
could. **Flown alone and UNRESOLVED** — ratio 1.18, p 0.505. It shipped inside the arm that
re-derived the pointing deadband, which won as a unit, so it is a correctness change rather than a
measured one.

## 1. Null the separation impulse — reformulated, unflown

**The mechanism is flown and confirmed.** KSA's translation flags reach the bus's nozzles, they were
measured at **0.9-2.2 m/s2**, and the loop closes at that rate: `trimming 1.23 m/s on the tail` →
`trimmed to 0.010 m/s` in 1.8 s.

**What was wrong was the question it asked.** It re-solved a transfer from wherever the bus happened
to be, to a latched arrival and the corrected aim. A transfer is parameterised by those two, and on
a deorbit it demands about **20 m/s more for every second the arrival is out** — so the answer
depended on *when* the trim ran and went stale while the bus coasted clear of its spent stack:

| flown | owed at the split | owed on release | result |
| --- | --- | --- | --- |
| trim at +0.13 s | 1.23 m/s | 1.23 | 431 m - 1.4 km |
| held 48 s | 0.21 m/s | **228.97** | refused, 5.7-6.5 km |
| held 90 s | 0.26 m/s | 1.47 | trimmed, but released late — 8.2-9.0 km |

It now nulls against the trajectory the guidance actually flew to: carry
`(CutoffPositionCci, Arc.RequiredVelocityCci)` forward by `SecondsSinceCutoff` with `Sim/Kepler.cs`
and subtract what the vehicle is doing. Same shove, asked at 0, 10, 60 and 300 s after cutoff:
**1.1000, 1.0999, 1.0972, 1.0370 m/s**. The few per cent of decay is the two conics genuinely
drifting apart. Against a reference that is not carried forward the same test reads 92 m/s after ten
seconds.

Two things fall out of it. The trim **takes no aim point**, so the loop that wound it together with
the aim correction is gone by construction rather than by rule — the correction now sits out only
while thrusters are actually firing. And **the clearance wait stops being expensive to the trim**,
because the answer no longer decays while it waits.

The wait is still expensive to the *shot* — see item 2b — so it is now sized off the discarded
stage's own bounding sphere, which is what the coarse contact test uses, and capped at **20 s**
rather than 90.

Never yet exercised: **whether the tank lasts.** ~183 kg of MMH/NTO against a few m/s is comfortable
on paper and nothing has spent it.

## 2. The walk was the planet's own fall toward the Sun — flown, fixed, and a quarter of what it was

The largest remaining term, and the one attempt at it made things worse.

> **The sign has changed, and the term that explains it is the one no rig can see.** One shot flown
> 2026-08-24 — the first with the warheads released once and the group scored over six — walks
> **-2,900 m (-2,896 down, -156 cross)**, which is *short* of the probe rather than beyond it.
> Everything else in that trace reads zero: the release probe **0.0 km** from the target, the round's
> own probe 20 m, the round's surface and the prediction's agreeing to **+0.0 m**, and `lag` zero, so
> no frame was clamped. What is left is a flight time of **372.44 s against the 373.03 s its own
> predictor gave it** — 0.59 s early, which against the ground track at a 7.1° arrival is most of the
> 2,900 m.
>
> **`ProbeGapTests` has the opposite sign at every frame it prices** — `+399 m (+0.054 s)` at 130 ms,
> `+1,239 m (+0.170 s)` at 320 — so whatever this is, it is not the coast frame and not the two
> flight models disagreeing. `ModelInputAgreementTests` prices the one term that is *identically zero
> in every headless rig and never zero in the game*: the round is integrated in `Ecl` about a planet
> KSA is pulling along its own orbit, and the prediction in `Cci` about a planet at rest. KSA's Earth
> falls at **5.936 mm/s²**, and where that lands depends only on where the Sun is:
>
> | the Sun lying | the impact moves |
> | --- | --- |
> | radially outward | **-9,654 m downrange** |
> | along the track | -239 m |
> | across the track | +654 m |
>
> Those are for the 497 s coast. The drift is linear in the acceleration and so goes as `t²` — pinned
> by `TheShiftIsLinearInTheBodysAccelerationSoItScalesAsTheSquareOfTheFlight` — so at this shot's
> 373 s a fully radial Sun is **-5,435 m**. The flown -2,896 m is 53% of that, which is a Sun about
> 60° off radial. **Nothing else has to be invoked**, and the sign, the size and the earliness all
> come out of one term.
>
> It is a fault in the *round*, not in the prediction. A real warhead and a real planet fall toward
> the Sun together and the effect cancels; this round feels its parent body's gravity and nothing
> else, so relative to the ground it carries a spurious `-a_sun` for the whole coast. The prediction,
> about a planet at rest, is the physically honest one.
>
> **Two things follow, in this order.** The measurement first — item 9's ranked entry 3 asked for
> exactly this and it is still not built: log where the Sun is at release, resolved onto the
> arrival's axes, which turns a 0-9.7 km unknown into a number on every shot. Then the fix, which is
> to carry the parent body's own acceleration in the round's integration; `DeorbitShot.bodyAccelCci`
> already exists to put that term back headlessly.
>
> **It is invisible to `~/shots/2026-08-24-coast`**, and that is a property worth knowing rather than
> a problem: every shot in that batch resumes the same save at the same pick-up, so the Sun is in the
> same place in all thirty and the term is a constant that cancels between the arms.

### Flown 2026-08-24 — WIN on the night, and not a record. Read the next section with it.

Ten against ten, interleaved, `base=dev` against `arm/bodyfall`, one line apart:

| | base | fall | ratio | 97% interval | p | |
| --- | --- | --- | --- | --- | --- | --- |
| **mean** | 2.88 km | **0.72 km** | **0.25** | 0.24-0.26 | 0.000 | **WIN** |
| spread | 0.01 km | 0.02 km | 2.00 | 2.00-5.00 | 0.001 | **LOSS** |

**The attribution table moves in exactly one place**, which is what makes it a measurement rather
than a coincidence:

```
arm      residual  own km  probe km  arr deg  band deg   walk m   early s   dt ms
base        0.040    1.66      0.10      7.1      0.72     2815     +0.59    17.3
fall        0.035    1.67      0.05      7.1      0.72      643     -0.14    17.2
```

Cutoff residual, own predicted miss, release probe, arrival angle, cant, lag and frame time are all
unchanged. The walk goes **2,815 -> 643 m** and the round stops beating its own predictor —
**0.59 s early becomes 0.14 s late**. The twenty shots never overlapped: every `fall` group came in
under 0.81 km and every `base` group over 2.72.

**The spread loss is real, significant, and small.** Six warheads that landed within 10-20 m of each
other now land within 20-60 m. It does not offset 2.16 km of mean and it is **not diagnosed**;
`docs/SHOT-PROTOCOL.md` analyses `spread` beside the mean rather than inside it for exactly this
case, and the mechanism — the tube cant against the attitude the burn left — is untouched by this
change, so something else is expressing itself now that the common-mode bias is gone.

**What is left is +678 m of walk the other way**, and the swing was larger than the term:
**-2,960 -> +678 m**, i.e. **+3,638 m** against a term measured at about 2,540 m. Two response gains
the rig cannot supply account for most of that difference — an 18% early gain measured at t=12.5 s
before terrain can matter, and a **terrain gain of 1.12-1.4**, because the two arms land 3.6 km apart
and the `fall` arm's ground is **55.5 m lower**, a 1.53% downhill at `cot 7.1 deg` = 8.03.

> **The terrain gain was retired on the wrong interval.** Item 9's ranked list prices it at 4 m, from
> the walk moving 1,939 -> 1,943 m across one arm's last 394 m of altitude. That interval cannot see
> the slope *between two arms' landing points*, which is the only slope a swing is amplified by.

### What the +678 m is — measured 2026-08-24, and it is the force sample's epoch

**Confirmed on four independent signatures.** The round's gravity is read at its **pre-step**
position against a celestial sample from the frame's **end**, so the pull centre sits
`bodyVelocity × dt` away — 513 m at the flown 30,190 m/s and 17 ms.
`docs/KSA-FRAME-ORDER.md` §5 has always stated the offset; what was never asked is where it lies
against the arrival, which is the only thing that decides what it costs.

The log line added in `0446df7` answers it:

```
the body travels at 30190 m/s, lying (-0.73 up, -0.64 downrange, -0.25 across) of the arrival
-- one 17 ms frame of it is 513 m of pull-centre offset
```

| | predicted | flown |
| --- | --- | --- |
| radial share of the displacement | 513 x 0.73 = **374 m** | — |
| downrange, scaled to the flown arc | **≈ +838 m** | **+678 m** (probe shot +698) |
| arrival timing | **late** | **+0.14 s late** |
| arrival speed against the probe's | unmoved | within **2 m/s** |

`BodySampleInvarianceTests` prices it headlessly, in a rig whose planet does not move and where the
term was therefore identically zero until it was asked for: **2,206 m of impact per 513 m of radial
displacement, 74 m per 513 m along the track** — a factor of thirty — and linear in the
displacement. Only the radial share costs anything, which is why the log resolves a direction rather
than reporting a speed.

**Long and late with the energy untouched is the signature no drag mechanism has.** Removing the
density carry is worth +66.5 m/s of arrival speed and an outward ground-centre error +79.5; this
shot's arrival matches its probe to 2 m/s.

**The obvious correction is already recorded as flown and lost**, in the comment above the call site
in `WeaponSystem`: putting the body back by `bodyVelocityEcl*dt` translates the whole field, so the
round is pulled toward a centre the ground test does not use. That objection survives inspection.
Gravity is computed once at the frame's start and held; the ground centre is sampled at the frame's
end and is exact at the last sub-step, which is where the crossing fires. Correcting gravity alone
pins the two to **different instants** rather than to one.

**So the fix is one body centre per sub-step, shared by both** — `Slug` already holds
`_groundCentre`, and would take the body's velocity and the frame length with it, using
`centre(t) = _groundCentre − v_body·(dt − elapsed)` for the crossing and computing gravity about that
same centre instead of being handed a vector. That is a change to what a projectile is given each
frame rather than an added carry, and it is the fourth attempt at a term that has beaten three.

### Flown 2026-08-24 — WIN. Aim the frame's gravity at the body's mid-frame position

One subtraction, and it goes where gravity is **composed** rather than where it is consumed:
`KsaWorld.GravityAt` takes a body offset and `WeaponSystem.GravityAtRound` passes half a frame of
the body's own travel. Aimed at the sample the vector is a whole frame out for the whole frame;
aimed at the middle it is half a frame out at each end and right on average. The evaluation count,
the sub-step count and the held-for-the-frame convention are untouched, and with a still body the
result is bit-identical — `BodyCentreEpochTests` asserts that, and asserts `Slug` has no opinion
about bodies at all.

Eight against eight, interleaved:

| | base | aim | ratio | 97% interval | p | |
| --- | --- | --- | --- | --- | --- | --- |
| **mean** | 0.77 km | **0.45 km** | **0.58** | 0.50-0.63 | 0.000 | **WIN** |
| spread | 0.02 km | 0.02 km | 1.00 | 0.50-2.50 | 0.959 | no change |

**No shot overlapped**: every base group above 0.72 km, every corrected one below 0.59. And unlike
`8519c5d` the spread costs nothing — this one is a straight gain.

The attribution moves in one place:

```
arm     residual  own km  probe km  arr deg  band deg   down m  cross m  early s
aim        0.030    1.67      0.10      7.1      0.72     +364      +14    -0.09
base       0.040    1.67      0.10      7.1      0.72     +694      +25    -0.14
```

The walk **halves, 694 -> 364 m**, which is exactly what aiming at the middle of a frame does to a
whole-frame offset, and the round's earliness against its own predictor goes 0.14 -> 0.09 s.

**The scatter is not in this term.** Across the sixteen shots the walk moved only +678 to +714 —
±2% — while the miss swung 0.72 to 0.89 km, ±11%. Whatever varies shot to shot is upstream of the
round, in what the aim correction leaves behind, and it is now the larger source of *variance* even
though the walk is still the larger source of *bias*.

**Two forms of this arm flew and failed first, and both were the change rather than the idea.**

- *3,512 m.* It also moved the ground crossing onto the back-dated centre. `elapsedInFrame` is the
  time at the **start** of a sub-step and the crossing tests the position at the **end** of it, so
  the last sub-step — the only one a crossing can fire on — was displaced by `v·h`, 129 m, where it
  had been exact. The crossing needs no correction: its staleness vanishes there by construction.
- *3,097 m.* Re-deriving gravity inside `Slug` **overwrote** the vector, and on `dev` that vector is
  `GravityAt + BodyFallEcl`. It silently reverted `8519c5d`, which is why it read the base arm's
  walk almost exactly. **A change that discards a term composed upstream looks like a large effect
  and is a revert.** Nothing headless could see it: the rig's planet does not move and nothing there
  composes that term.

**And the rig could not tell the first two forms from this one** — all three read 102 → 57 m in
`BodyCentreEpochTests`, because `DeorbitShot`'s planet sits at the origin and a back-dated ground
centre is identical to an un-back-dated one there. Three flights separated them.

### Flown 2026-08-24 — WIN. Per sub-step rather than once a frame

Six against six, interleaved, against a `dev` that already carried the mid-frame aim:

| | base | pss | ratio | 97% interval | p | |
| --- | --- | --- | --- | --- | --- | --- |
| **mean** | 0.44 km | **0.05 km** | **0.11** | 0.06-0.19 | 0.002 | **WIN** |
| spread | 0.02 km | 0.02 km | 1.00 | 0.50-1.00 | 0.485 | no change |

Nothing overlapped, and the spread costs nothing. **The walk is the mechanism and it lands where the
arithmetic put it:**

| correction | walk | early |
| --- | --- | --- |
| none | **+694 m** | 0.59 s |
| half the frame (`fe1df49`) | **+364 m** | 0.14 s |
| per sub-step | **-72 m** | **0.00 s** |

The round stops beating its own predictor at all.

**Why it had to wait for the mid-frame aim to fly first.** It necessarily re-reads gravity per
sub-step, which flew alone and lost — item 2d, priced by `ProbeGapTests` at -740 m. It loses alone
because it corrects one half of a cancelling pair; with the centre corrected too there is no pair
left to break. The delegate is `AirDensityAt`'s shape and convention, and the caller composes it, so
`BodyFallEcl` travels with it and cannot be lost by a callee re-deriving the pull.

**What is left is no longer the walk.** Across the twelve shots the walk held **-51 to -84 m** while
the miss moved 0.02 to 0.09 km. The residual is what the aim correction leaves behind — item 9's
open entry — and the round itself has stopped being the largest term for the first time.

### The walk grew sevenfold in two days, on an identical shot — and that is the real question

Same save, same aim point, same pick-up (`207 km, 7360 m/s -- identical` in all three reports), same
release state, same coast length, same arrival speed to 7 m/s:

| night | probe's own miss | walk (down) | group |
| --- | --- | --- | --- |
| 2026-08-22 | 912 m | **-427 m** | **0.48 km** (one arm 0.29) |
| 2026-08-23 | 416 m | **-1,196 m** | 0.80 km |
| 2026-08-24, `base` | 104 m | **-2,971 m** | 2.88 km |
| 2026-08-24, `fall` | 104 m | **+678 m** | 0.72 km |

**The aim correction improved steadily — 912 to 104 m — while the walk grew sevenfold.** Read
together they say the obvious thing: **0.72 km is not a record.** 2026-08-22 scored 0.48 km on this
shot, and its best arm 0.29 km. What has happened is that two large errors of opposite sign were
partly cancelling, one of them has been removed, and the other is now exposed.

**And the body-fall term was in the code the whole time**, unchanged until `8519c5d` — so it cannot
be what grew. Something between 2026-08-22 and 2026-08-24 removed roughly **+2,100 m** that had been
offsetting it. Candidates, in the order they landed: `23422a3` (the Mk 21's preferred step, which is
what decides whether the coast is warped at all), `1be43cc` (release timing, reach, the trim budget),
`5b15830` (the harness's coast warp and hand-back), `42fee35`, `e64f099`, `0002713`. The 08-22 coast
carried **127 warped frames at a mean 102.9 ms step and a maximum of 266.7 ms**; the 08-24 coast has
**none at all, maximum 33.3 ms** — and `ProbeGapTests` says a coarse coast frame walks a round
*long*, +399 m at 130 ms and +1,239 m at 320. So the leading suspect is item 7e's own closure:
putting the coast at 1x did not merely remove a lottery, it removed a **large positive walk that had
been masking the solar term**.

**This is the better-posed question**, and it is bisectable rather than speculative: six commits, and
a night of logs on both sides of them.

`Slug` holds the body centre `IGroundTest` gives it for the frame, and differences it against a
position moving through that frame — so on the face of it the planet's ~29.8 km/s of ecliptic travel
reads as a change of *altitude*, which on a shallow arrival is eleven times as much ground. Headless
it measures 850 m on a 16.7 ms frame, and carrying the centre forward removes it entirely: the round
lands 60 m from its own prediction instead of 790.

**Flown, it is the other way round.** The last build without the carry:

| | probe at release | impacts |
| --- | --- | --- |
| **without the carry** | 0.1-0.2 km | **431 m - 1.4 km** |
| with it, cleanest shot | **0.0 km** | 6.1-7.2 km |

That cleanest shot had a 0.24 m/s cutoff residual and a bus trimmed to 0.017 m/s, so nothing
upstream was wrong — only the round's own flight disagreed with the prediction of it. **Reverted.**

Two things worth keeping:

- The signature — a prediction that does not move while the rounds do — is one
  `docs/FRAMES-AND-EPOCHS.md` already records from an earlier flown failure, where a carry was
  applied to a sample that was already in phase. Same shape, and the size fits: the phase was
  measured as worth ~7 km taken the wrong way, and the flight lost 6.5.
- **Every headless rig flies the round about a planet at the origin**, which is the one case where a
  carrier fault is identically zero. A rig that introduces a carrier can measure a real sensitivity
  and still take the *sign* from an assumption nothing tests.

What settles it is the phase at which `IGroundTest` writes the centre relative to the round's own
position — a fact about the engine's frame order, not about this maths. **It needs a flight that
varies the phase, not more reasoning.** The KSA `2026.8.22.5348` retarget varied it for free and
the walk grew rather than changing sign, which item 2e records and does not explain. Until it is
explained the round keeps the behaviour that flew best.

### The gap taken apart — measured, unflown

Flown 21 August with the guidance essentially solved: the aim correction converged
**0.8 -> 0.3 -> 0.1 km**, the release probe reported **0.1 km**, and the warheads landed **1.7 km**
out. Four shots at 1.57 / 1.61 / 1.74 / 1.88 km with groups 0.02-0.03 km wide. So the whole
residual is one bias, and it is the round flying differently from the prediction of it.

`ProbeGapTests` flies one identical release state through both models and differences the impacts.
At the step the scenario actually uses — 8x through the coast, `Medium.FaithfulStepInAir` once
there is air — the round lands **394 m** downrange of its own probe on the mean sphere and
**400 m** over `DeorbitShot.RoughGround`. Each term measured on its own against that same baseline:

| removing | moves the impact |
| --- | --- |
| the ground held for a whole frame | **0 m** (2 m at 50 ms, 22 m at 320 ms, over relief only) |
| gravity held for a whole frame | **-545 m** |
| the air's motion held for a whole frame | -2 m |
| symplectic Euler at 5 ms against RK4 | **+165 m** |
| all four together | -402 m |
| **unaccounted for** | **-8 m** |

So the two flight models are **fully explained by two terms**, and the crossing rules
(0.39 m against 0.00 m), the air's motion, the terrain sampling and everything else are noise.

**The two terms have opposite signs and partly cancel**, which is the thing worth not
rediscovering. The gap is 394 m; the gravity freeze alone is worth 545 m the other way.

**And only one of them scales with the frame.** Against the coast's own step, everything else held:

| coast | frame | as flown | gravity per sub-step | converged sub-step | both |
| --- | --- | --- | --- | --- | --- |
| 1x | 17 ms | **-73 m** | -127 m | 65 m | -7 m |
| 2x | 33 ms | -20 m | -145 m | 138 m | -7 m |
| 4x | 67 ms | 121 m | -147 m | 281 m | -6 m |
| **8x** | 133 ms | **394 m** | -150 m | 559 m | -6 m |
| 16x | 267 ms | 950 m | -150 m | 1115 m | -6 m |
| 19x | 317 ms | **1159 m** | -151 m | 1324 m | **-6 m** |

Read the columns rather than the rows. The sub-step's own error is **flat at about -150 m** at every
frame size, because `steps = ceil(dt / SubStep)` keeps the sub-step at 5 ms whatever the frame. The
gravity freeze is **linear at 4.2 m of downrange per millisecond of frame**, on all six rows. And
with both removed the two models agree to **6 m at every coast speed**, which is what says the
decomposition is complete rather than merely plausible.

**That retires item 2d's unexplained sign.** Re-reading gravity per sub-step was priced headlessly
as a large win and flew worse three times out of three, and nothing explained it. It removes the one
term that was cancelling the round's own integration error: at 1x it takes the shot from -73 m to
-127 m, which is worse, and the cancellation is a coincidence of frame size rather than a design.
**Do not fix either half alone.**

### What the rig cannot see, and why 394 m is not 1,700

Four candidates for the remaining ~1.3 km, none of them measurable in a rig whose planet sits still:

- **The terrain's own gain — retired by the trace.** A round stopping past its prediction stops on
  ground that has itself fallen away, so the residual is re-multiplied by `1/(1 - s/tan y)` — and at
  this 7.1-degree arrival that is unbounded past about a 12% slope: 1.13x at 2%, 1.41x at 5%, 1.80x
  at 8%, 2.32x at 10%. Reaching 1,700 m from 394 needs about 4.3x, or a ~9.6% mean slope.

  **The flight says it is worth 4 m.** Between the last re-flight at 394 m of altitude and the
  impact the walk moves 1,939 -> 1,943 m, and the two surfaces agree at the landing point to
  **+0.0 m**. A gain multiplies ground distance and cannot touch a clock, so it would have raised
  the flight's metres-per-second-of-arrival-delay above the rig's; it is 12% **below**. Whatever is
  scaling the error is scaling the arc, not the last kilometre of it.
- **The ground centre's ecliptic carrier**, which is item 2's own hypothesis above. Identically zero
  in every rig here, because the planet sits at the origin. `Slug` holds `_groundCentre` for the
  frame while `PositionEcl` advances at the planet's ~29.8 km/s — 1,490 m of relative drift across a
  50 ms frame, of which the radial part is read as altitude at 8 m of ground per metre. Large
  enough, and both phases of the fix have been flown and neither beat leaving it alone.
- **The waterline** — **closed.** `IcbmComputer.SurfaceHeight` now routes `TerrainRadiusAt` and
  `SurfacePointEcl` through `GroundSurface.Height`, so all three surfaces clamp to sea level.
  `SurfaceAgreementTests` prices what it was worth (35 km of ground at Earth's mean depth) and
  `docs/KSA-TERRAIN.md` has the three call sites.
- **The planet's own fall toward the Sun.** See below — the only one of the four that is a
  disagreement about the *inputs* rather than about the flight models, and the only one that is
  exactly zero in every rig by construction rather than by the relief being gentle.

### The two models are handed the same world, except for one force

`ModelInputAgreementTests` is the other half of `ProbeGapTests`: that one hands both models one
world and prices what their integrators do differently, this one asks whether the game hands them
one world at all. In flight the round takes its inputs through `Ksa/WeaponSystem.cs` in `Ecl` and
the prediction takes its own through `Ksa/IcbmComputer.cs` in `Cci`, and those are different code
paths reading different engine calls.

**Four of the five agree, and three of them agree exactly rather than closely:**

| input | round | prediction | |
| --- | --- | --- | --- |
| gravitational parameter | `((IParentBody)body).Mu` | `Parent.Mass * 6.6743e-11` | the same expression — `Mu` is a default interface member with that body |
| air density | `MediumDensityRatioAt(body, posEcl)` | the same call, via `Cci -> Ecl` | the `+ parent.GetPositionEcl()` and the `- body.GetPositionEcl()` inside cancel, so the prediction's altitude is `\|pointCci\|` and carries **no epoch term at all** — which is why it needs none of `AirDensityIntoFrame`'s back-dating |
| the air's motion | `GetAngularVelocityCce() x (posEcl - bodyEcl)` | `(0,0,GetAngularVelocity()) x posCci` | `GetAngularVelocityCce()` *is* the second rotated into `Cce`, and the spin axis is exactly `+Z` in `Cci` |
| the ground | `GetTerrainHeightFromDirCce` | `GetTerrainHeightFromDirCcf` | one entry point, and the `Cce` one applies the rotation for you off the same per-frame `_ccf2Cci`. Both `accurate: true`, both waterline-clamped |

So the asymmetry that looks most suspicious from the code — `AirDensityIntoFrame` back-dates by
`_bodyVelocityEcl` and `DensityRatioAt` back-dates nothing — **is not one.** The prediction's density
lookup makes a round trip through `Ecl` that cancels the body sample exactly, so there is no epoch in
it to correct.

**The fifth does not agree, and it is not an epoch fault.** `KsaWorld.GravityAt` pulls the round
toward `body.GetPositionEcl()` — wherever KSA has moved the planet to — while the round itself feels
nothing else. KSA is meanwhile accelerating that planet along its own orbit. In the planet's frame the
round therefore carries an unmodelled `-a_body`, and `ImpactPredictor` in `Cci` has no such term.

`Ecl` really is heliocentric: `StellarBody.GetPositionEcl()` returns `double3.Zero` and
`Celestial.UpdatePerFrameData` sets `_positionEcl = _positionCce + Parent.GetPositionEcl()`, so
Earth's ecliptic position *is* its Keplerian orbit about Sol and its second derivative is `mu/r²`.

| | |
| --- | --- |
| KSA's Earth, `Mass Suns="1"` at `a = 1.4954e8 km` | **5.936 mm/s²** |
| drift over the 497 s coast, `a·T²/2` | **733 m** |
| worst case at the impact, over every direction the Sun could lie in | **9.68 km** |
| ...resolved radially / along the track / across it | -9,651 m / -239 m / +654 m |

Three things make it fit the flown signature better than anything else on the list. It is **common to
the whole salvo**, which is a bias with a 20-30 m group rather than a spread. It is **quadratic in the
flight time**, so it is 30 m for a 100 s shot and 9.6 km at the Mk 21's own `MaxFlightSeconds` — which
is why nothing before the ballistic weapon ever met it. And it is **invisible to every rig in this
repository**, because `DeorbitShot`'s planet sits at the origin and does not move, which is the same
blindness item -1 keeps warning about.

**What it is not** is an explanation of 1,700 m specifically: the fraction that lands downrange
depends entirely on where the Sun is relative to the arrival, and that is a per-shot fact this rig
cannot know. It is 0 to 9.7 km, sign included.

**What would settle it** is one line in the release probe: the direction from the parent to *its*
parent at release, resolved onto the arrival's up / along / cross. If the radial component is large
and its sign matches the miss, this is it; if it is near zero and the warheads still land 1.7 km
long, it is not.

**And the fix, if it is, is one term** — give the round the grandparent's gravity as well, which makes
its `Ecl` frame consistent with the planet's and leaves only the solar tide (0.009% of the term).
That is a behaviour change reaching every round the mod fires, and belongs in a flight.
- **The waterline — closed, and worth keeping as the shape.** `Ksa/GroundTest.cs` clamps the height
  field to sea level and `Ksa/IcbmComputer.cs`'s `TerrainRadiusAt` now does too, through the same
  `GroundSurface.Height`. While it did not, a round over water stopped kilometres above the surface
  the prediction flew to — 35 km of ground at the mean depth, and zero over dry land, so a shot that
  arrived inland saw none of it. `SurfaceAgreementTests` prices it. The class of fault is not closed:
  **any** disagreement between the surface the round stops on and the surface the prediction flies to
  is re-multiplied by the arrival's ~8 m of ground per metre of height, and only a flight can say
  whether there is one left.

### The instrument — flown 22 August, and what it read

`Ksa/WarheadTrace.cs`, behind `Config.TraceWarhead` (**Debug → Trace one warhead**, off; on for a
`tools/scenario.sh mirv` run). It follows the **first** warhead of a designation from the tube to the
ground and writes, in Cci:

- **at release** — the round's own position and velocity at full precision, so the same shot can be
  re-flown in `ProbeGapTests`, and `ImpactPredictor` flown from exactly that state. Not the same
  probe as the existing `release probe:` line, which departs from the bus's orbit state plus an
  assumed offset and kick; the gap between the two lines is what the tube did.
- **per frame** — `t` against the round's own `age`, so `lag` is the simulated time the integration
  clamp has discarded, and `dt`/`step` beside the simulation speed, so a single clamped frame or a
  warp transition can be pinned to the frame it happened on.
- **on a slow cadence** — the same predictor re-flown from where the round has got to, as `walk`
  from the release probe decomposed **downrange and cross-track**, and as the arrival instant it
  now predicts. If the round were still on the probe's arc both would be flat.
- **at impact** — where and when, against the aim and against the probe, and the surface the round
  stopped on beside the surface the prediction flew to.

**What each shape would mean.** A `walk` that ramps smoothly through the coast is the two flight
models integrating apart, which is item 2's own decomposition and is priced at 394 m — more than that
and something the rig cannot model is scaling it. A `walk` that is flat through the coast and ramps
at entry is drag or the air sample. A `walk` that **steps** is discrete, and the frame it steps on
says which: `lag` moving is the clamp, `sim` moving is a warp transition, neither moving is the
prediction's own inputs changing under it. A flat `walk` with a large final miss is the surfaces
disagreeing, which the impact line reads off directly.

Cadence: the cheap line every **2 s** of simulated time and every frame for the first 3 s, all the
while the round's own `FaithfulStepSeconds` says it is in air, and the last 10 s; the re-flight every
**10 s**, and **0.5 s** in those same stretches. About five hundred lines for a 400 s flight. The
per-frame half needs `Config.VerboseLog`; without it only the release and the impact are written,
and no re-flight is paid for.

#### The reading

One flight, round 1 of six, 410.27 s, 7,840 trace lines. The other five landed 1.67-1.74 km, so the
traced round is the group rather than an outlier.

**It is the smooth shape, and it is entirely the coarse frame.** The walk starts 12 m *uprange*,
crosses zero at t≈30 s, reaches **+2,206 m at t=249.1 s** — and stops there, because that is the
frame the warp came off at. Through the whole 1x entry it *decays* to +1,943 m.

| stretch | walk | mean frame |
| --- | --- | --- |
| release to 30 s | **-58 m** | 159 ms |
| 30-100 s | +298 m | 198 ms |
| 100-200 s | **+1,093 m** | 199 ms |
| 200 s to the warp coming off | +672 m | 238 ms |
| the whole 1x entry, 249-410 s | **-265 m** | 20 ms |

**The rate is proportional to the frame and to nothing else.** Over t=100-250 s the frame swings
197 -> 266 ms and `rate / frame` holds at **0.0614 m/s per ms**, ±5%. The one interval that looks
like a step — +13.5 to +18.1 m/s at t=234 — is the frame growing to 266.7 ms, which is the same
proportionality.

**Three of the four shapes are absent.** `lag` is **0.0 ms on every one of 7,491 sampled frames** and
`dt - step` never exceeds 0.1 ms, so the integration clamp discarded nothing and the round was never
short of the world. Cross-track never leaves ±26 m and ends at +8 m. The two surfaces agree at the
landing point to **+0.0 m**. Nothing steps.

**The warp ran unheld for 61% of the flight, and that is `MaxFaithfulStepSeconds` working as
written.** The Mk 21 takes the 0.32 s default, so `WarpPolicy` allowed 8x until `Slug` first read
air at 73 km — `timewarp held at 1.0x` is logged 249 s of simulated time after release, and the walk
stops growing on that frame.

#### 394 m against 1,943 m

**Three numbers get conflated and only one of them is the probe gap.** The round landed **1,675 m**
from the aim, its own release probe landed **269 m** from the aim *uprange*, and the gap between the
round and that probe — what `ProbeGapTests` models — is **1,943 m**. The 1.7 km everyone quotes is
the probe gap minus the correction's own residue.

**The rig's 394 m is at 133 ms, and the flight ran at 202 ms.** `DeorbitShot.ScenarioWarp x
NominalFrame` was 8 x 1/60 when that number was taken; the flight's unwarped frame during that
stretch was ~25 ms, not 16.7, so 8x delivered a **median 198.5 / mean 201.9 ms, peaking at 266.7**.
The rig's own table is linear at 4.15 m per ms above 67 ms, so the frame alone carries 394 ->
**~680 m**. `NominalFrame` is now **25 ms**, measured off that flight, so a re-run prices the frame
the coast actually gets.

| | |
| --- | --- |
| flown probe gap | **1,943 m** |
| rig at its assumed 133 ms | 394 m |
| rig at the frame the flight actually ran | ~680 m |
| **left over** | **~1,263 m, a factor of 2.9** |

**The residual is in the arrival time as well, which is what makes it a trajectory difference.** The
predictor's own answer for when the round arrives moves **+0.30 s** (peak +0.36 s) against the rig's
+0.054 s at 130 ms and +0.170 s at 320 ms — interpolated to 202 ms, +0.098 s. **The same 3.1x.** Two
columns, one from ground distance and one from a clock.

**And the perturbation is the same *character* as the rig's, ~3x larger.** Ground displacement per
second of arrival delay is 7,389 m/s in the rig at 130 ms, 7,288 at 320 ms, and **6,477 in flight** —
agreeing to 12%. Whatever scales the flight's error scales the arc, not the landing.

#### What it cannot say, and the four lines that would

**The trace records the round's state and the predictor's answer, and never the round's integration
inputs** — so the flight cannot be decomposed the way `ProbeGapTests` decomposes the rig. The rig
puts the whole frame term on the frozen gravity at 4.2 m per ms; the flight behaves like ~15. That
is the 2.9x, and the trace watches every consequence of it and none of its causes.

- **The gravity vector the round was handed.** `gravity` is a frame-level argument to `Slug.Step`
  and is never re-read or back-dated, while density beside it is
  (`AirDensityAt(PositionEcl, elapsed - dt)`). Log its magnitude and its angle from
  `-Vec.Unit(positionCci)`. The suspicion it would settle is an epoch one:
  `WeaponSystem.GravityAtRound` pairs the round's start-of-frame `PositionEcl` with
  `body.GetPositionEcl()`, which is the end-of-frame sample — **6.0 km of ecliptic carry in the
  lookup at a 202 ms frame**, and exactly the fault the density path back-dates away. One
  `Log.Debug`, and the highest-value line on this list.
- **A closed-form reference.** `Sim/Kepler.cs` propagated from the release state, differenced
  against the round in Cci and resolved radial / along / cross. The walk compares two *predictions*;
  nothing in it compares the round against anything, so "the round left the arc" and "the predictor
  disagrees about where the arc goes" are one number.
- **The sub-step count and `h`.** `steps = ceil(dt / SubStep)` capped at `MaxSubSteps`: 54 of 64 at
  266.7 ms, so it is not clamping — but the trace cannot show that, and the cap is where the next
  warp increase would land.
- **Where the Sun is at release**, on the arrival's axes. Already asked for above; the trace is its
  natural home, and it is the one candidate the flight's own shape neither confirms nor kills.

Two smaller things the flight turned up. `Walk`'s `{...:+0;-0}` renders a negative zero as **`-+0`**
— .NET prints the sign of `-0.0` and then applies the positive section's literal `+` — which made
one line in 7,836 unparseable; harmless to read, fatal to a script. And
`MunitionProfile.PreferredStepSeconds` claims **86 m per millisecond of frame**, which nothing here
reproduces: the rig gives 4.2 and this flight ~15.

### Ranked, cheapest first

1. ~~**Cap the warhead's coast frame.**~~ **DO NOT FLY THIS.** It was flown, four times, at
   **48-60 km** — item **-0b**, which is on this same page. `MaxFaithfulStepSeconds` bounds a clamp
   that *discards* time, so tightening it does not shorten the frame, it makes the round fall behind
   the world. `ReentryVehicleMk21.PreferredStepSeconds` is the field that asks the question this
   entry meant to ask; `docs/SHOT-PROTOCOL.md` says so in its arms table. Everything below the rule
   is the pricing as it stood before the flight, kept because the *shape* of the argument was right
   and only the field was wrong.

   ---

   `MunitionProfile.MaxFaithfulStepSeconds` on the Mk 21,
   0.32 -> **0.10**, which is what `WarpPolicy` holds the world down to while the salvo flies. Priced
   through the real policy from the scenario's own 8x request:

   | cap | world held to | frame | gap | the 497 s coast takes |
   | --- | --- | --- | --- | --- |
   | **320 ms**, as shipped | 8.0x | 133 ms | **394 m** | 62 s |
   | 200 ms | 8.0x | 133 ms | 394 m | 62 s |
   | **100 ms** | 3.6x | 60 ms | **87 m** | 138 s |
   | 50 ms | 1.8x | 30 ms | -43 m | 276 s |

   **Every frame in that table is 1.5x too small.** It was `warp x DeorbitShot.NominalFrame` at a
   hardcoded 60 fps; the flight ran the warped coast at a **median 198.5 ms**, because six warheads
   and their effects cost ~25 ms a frame rather than 16.7. So the shipped row is 202 ms and ~680 m
   rather than 133 ms and 394 m, and a cap bites *sooner* than the table says — 200 ms is a real
   reduction in flight where the rig reads it as a no-op. `NominalFrame` is 25 ms now.

   **307 m for 76 seconds of the player's evening**, and the next 44 m costs another 138. The
   sign risk this claimed not to have is real: the round's error is *not* monotone in the frame —
   it is two terms of opposite sign, crossing zero near 138 ms and overshooting the other way below
   it, which is why `PreferredStepSeconds` sits at 0.225 rather than lower. **Not done** — it
   trades against a decision `WarpPolicy` made deliberately. The trace now puts the frame-driven
   share at ~680 m of a 1,943 m gap, so this is worth more than the table claims and still not most
   of it.

2. **Steepen the arrival** — `IcbmConfig.MinArrivalAngleDeg`, already built. **Demoted**: it is the
   only lever on the terrain gain, and the trace prices that gain at 4 m. It still buys what a
   residual costs on the ground, so it is a lever on the whole miss and not on this term.

3. **Log where the Sun is at release**, resolved onto the arrival's axes. Costs nothing and is the
   only thing that separates the planet's own fall from every other candidate here — the term is
   0 to 9.7 km on this coast and the log says which end of that it is at.
3. **Route `TerrainRadiusAt` and `SurfacePointEcl` through `GroundSurface.Height`** — **done**, so
   the prediction now stops on the same surface the round does. Zero over land and tens of kilometres
   over water, so it was worth nothing on a shot that arrived inland and the whole miss on one that
   did not. What is not settled is whether the two surfaces agree over *land*; the trace's impact
   line reads both off at the one point where it matters.

4. **The ground centre's carrier.** Unchanged: the one flight that varied the phase is item 2e,
   and it made the walk larger without changing its sign.

5. **Both flight-model terms together** — gravity per sub-step *and* `SubStepSeconds` on the Mk 21 at
   about 1 ms — which the table above says lands at -6 m. Two coupled behaviour changes at once,
   one of which has three flights against it, and the cheaper item 1 gets most of it. Last.

One term is **zero and worth recording as zero**: `Slug` takes gravity through `Medium.Buoyancy` and
`ImpactPredictor` takes it raw, so a round declaring a `NeutralDensityRatio` is predicted by a model
that does not know it floats. Nothing in `Arsenal` declares one, but `Sim/PackReader.cs` reads the
field — so it is latent rather than absent.

## 2h. Predicting the warhead with the warhead's own integrator — demoted, unflown here

`be784ab`, an arm from 2026-08-22 that gave `ImpactPredictor` the round's integrator as an option so
the instrument matches what it measures. Headless it took the round-vs-predictor gap **591 m to 47**;
flown once it read 0.29 km against a 0.48 baseline, `UNRESOLVED` at n=8 with the interval admitting
0.23.

**Demoted rather than dropped.** It closes the same gap a finer sub-step closes, but from the other
side: a finer step makes the *round* more physically right, where this makes the *instrument* agree
with a round that is still coarse. Both land on the target, because the aim correction converges the
predictor onto it — only one is better physics. And the per-frame cost that argued against the finer
step turned out to be 0.3 ms, so the argument for this one is weaker than it looked.

**It is also no longer aimed at the largest term.** Since `aea3e2a` the walk is -72 m and the release
probe's own miss is about 50, so the aim correction is what is left rather than the round.

The branch is pruned; the commit is `be784ab` and the built payload is in
`~/shots/2026-08-22-2002/arms/faith/`.

## 2g. The body fall is summed over one link, so a lunar shot keeps most of it

`KsaWorld.BodyFallEcl` reads `body.Parent` and stops. A body's true ecliptic acceleration is the sum
over **every** link up to the star, because `Celestial.UpdatePerFrameData` builds its position
recursively — `_positionEcl = _positionCce; if (Parent != null) _positionEcl += Parent.GetPositionEcl()`.

For **Earth** that is one link and the term is exact: its parent is `Sol` directly, and
`StellarBody.GetPositionEcl()` is zero.

For **Luna** it supplies `mu_earth/r²` = 2.4-3.2 mm/s² and omits Earth's own **5.93 mm/s² toward
Sol** — more than twice the term it keeps, worth about **417 m of drift** over a 375 s coast, on arcs
*shallower* than Earth's and therefore with more `cot γ` to multiply it by. Phobos and Deimos have
the same shape.

**The walk terminates by construction**, which is what makes this a few lines rather than a design:
`Celestial` implements both `IOrbiter` and `IParentBody` while `StellarBody` implements only the
latter, so stepping up while the primary is still an `IOrbiter` reaches the star and stops.

Headless test: `BodyFallEcl(Luna)` should read ~8.6 mm/s² rather than 2.70, and `BodyFallEcl(Earth)`
must not move. The residual after the recursion is the solar tide over the Earth-Moon distance,
0.03 mm/s², or 0.5%.

**Unflown, and it cannot be flown on the shot this file is about** — it is provably a no-op on Earth.
It wants a lunar shot, which is a scenario that does not exist yet.

## 2c. The air was sampled a frame ahead of the body — fixed, flown

**This is what item 2 was measuring**, and it is why the answer kept depending on timewarp.

`Slug` asks `AirDensityAt(position, secondsIntoFrame)`, and the far side puts the body's own travel
back so that a round moving through a frame is not differenced against a body sampled once in it.
It was passing `elapsed` — the time *into* the frame, running 0 up to `dt`. Every other sample in
the same file is back-dated by `elapsedInFrame - frameSeconds`, running from minus a frame up to
zero, because the samples are end-of-frame and the round is part-way through.

The two differ by exactly one frame of the planet's ~30 km/s: **0.9 km at normal speed, 3.9 km at
eight times**, read as altitude, on air that falls off over 8 km. So it lands on drag, it grows with
the step, and it jumps when the step changes.

| coast | before | after |
| --- | --- | --- |
| 1x | 1.10 km | — |
| 8x | 4.76 / 5.27 km | **0.65 / 0.76 km** |

**The warp dependence is the whole of it.** Before, coasting at 8x cost about 4 km against 1x; after,
8x and 1x agree to within what a single run can resolve. That also retires the standing "every round
lands beyond its own release probe" gap: the probe said 0.2-0.4 km while the rounds landed 5 km out,
and the rounds now arrive where the probe says.

What is left of the warp dependence is a **different** term and is much smaller: gravity is held for
the whole frame, so a coarse coast integrates worse. Measured headlessly at **243 m** between 1x and
8x on one identical cutoff state, which is inside the half-kilometre a single flight can resolve.
Item 9 has it.

`AirSampleEpochTests` pins the convention and fails against the old form by a whole frame.

**What this does not settle** is item 2's own carry, which was reverted and stays reverted — both
phases of it were flown and neither beat leaving it alone, but those flights predate the harness fix
below and are inside its noise.

## 2d. Re-reading gravity per sub-step — flown, and it loses

`Slug` takes gravity as a frame-level argument and holds it across every 5 ms sub-step, while
`ImpactPredictor` re-evaluates it at each RK4 stage. Headless that costs 23 m at a 17 ms frame,
220 at 130 and 654 at 320, and re-reading it pins the error near 60 m at every frame size — about
**284 m at the step the shipped coast is held to**, and it should have removed the last of the
1x-versus-8x split.

Flown three times against the same pick-up: **1.80, 2.27, 2.01 km** (mean 2.03, spreads 2.49-2.59)
where the build without it flies **0.75-1.91 km** (mean 1.04, spreads 1.08-1.56). Worse on every
run and on both numbers. Reverted.

**It is not an implementation error, which is what makes it worth writing down.** The engine's own
gravity is a single-body inverse square about `body.GetPositionEcl()` (`KsaWorld.GravityAt`), so
scaling the frame's sample by `(r0/r)^2` about that same centre reproduces it exactly — the round
was getting the same vector the engine would have returned, just per sub-step instead of per frame.
The centre was deliberately **not** back-dated, after item 7b's lesson.

So the state is: a term the headless rig prices at 284 m of improvement costs about a kilometre in
flight, and nothing here explains the sign. The rig flies a planet at the origin with no carrier,
which is where it is known to be blind — but this fix does not involve a carrier, so that is not an
explanation either. **Do not retry it without a mechanism**; two flights' worth of evidence say the
frozen sample is somehow load-bearing.

## 2e. KSA 2026.8.22.5348 halved the residual and doubled the walk — measured

The retarget to KSA `2026.8.22.5348` was flown as a 12-shot baseline against the same save, the
same aim and the same `base=dev` arm as a 5-shot night on `2026.8.19.5261` twelve hours earlier,
with the pick-up `[207] km, [7360] m/s` identical across all seventeen. The only mod-side commits
between them are a log line, a drone-spawner fix and a tooling script, none of which a warhead can
reach — so the difference is the game's.

| | 2026.8.19.5261, n=5 | 2026.8.22.5348, n=12 | |
| --- | --- | --- | --- |
| **mean miss** | 0.37 km | **0.86 km** | x2.43 |
| cutoff residual | 0.320 m/s | **0.040 m/s** | x8 better |
| release-probe miss | 0.80 km | **0.40 km** | x2 better |
| walk from the probe | 503 m | **1339 m** | x2.7 |
| frame time | 23.4 ms | 17.7 ms | 24% faster |

Hodges-Lehmann x2.43 on the miss, exact rank-sum p 0.00065, one of twelve inside the old range.
That is a **drift comparison and not a test** — the protocol's baseline is an arm of the same batch
and this one is a night apart — but the pick-up is identical and the samples barely overlap.

**Everything upstream of the release got better and the whole loss is after it.** The burn now ends
eight times tighter, which is KSA's fix to a transposed row-times-column in
`FlightComputer.GenerateSingleAxisTvcTrackGains` and the Q retune that came with it, and the aim at
release is twice as good. Item 2 is what ate it: the walk roughly doubled, 92% of it downrange and
short, on a term that was already the largest one left.

**So this is the phase experiment item 2 said it needed and could not run.** It asks for "a flight
that varies the phase" of `IGroundTest`'s centre against the round's own position, and this update
varied it for free -- it moved physics-bubble ownership into `VehicleUpdateTask`, associated orphans
on the thread worker rather than the main thread, and took bubble merge checks out of the critical
section between frames. The walk did **not** change sign; it grew, ~770 m to ~1307 m per warhead,
while the frame it is differenced across got *shorter*. A pure `29.8 km/s x dt` carrier would have
shrunk by a quarter. That it grew instead is the most informative number here and is not yet
explained.

## 2f. The penetration was masking the walk, not causing it — flown, and it loses

`arm/ground-crossing` flown 23 August against a re-flown baseline, interleaved, same save and
pick-up. **It does exactly what it was built to do and loses badly.**

| arm | penetration | miss | |
| --- | --- | --- | --- |
| `base` | -12 to -27 m | **0.80 km** | n=12 |
| `ground` | **+-0.2 m** | **1.17 km** | n=6, dropped at the gate |

Ratio **x1.46**, interval 1.21-1.76, p 0.000, `LOSS`. The walk hardly moved (1267 -> 1322 m) while
the miss grew by almost exactly the penetration's own worth in ground.

**So the depth was cancelling the walk, not causing it.** A round burying ~16 m lands ~450 m long,
against a walk that is ~1.3 km short; removing the burial removes the cancellation and the full
walk shows. The true size of item 2e's regression is therefore **x3.2, not x2.3** — 0.37 km on the
old build against 1.17 once the round is made to stop honestly.

**The reasoning that got this wrong is worth more than the result.** Across 2e's twelve shots the
miss regressed on penetration depth at **R^2 = 0.957**, extrapolating to 0.313 km at zero depth
against the 0.370 km the old build flew — a fit good enough to look like a mechanism. It was
common cause with the arrow reversed: a round landing further short arrives on different ground,
where a frame-held sphere is more wrong, so it penetrates deeper. **Depth is a symptom of the miss.**
An R^2 of 0.96 across twelve shots, agreeing with an independent build's median to 60 m, was not
enough to establish direction, and nothing short of the flight would have been.

**Keep the arm.** A round must stop where its predictor says the ground is, and the two stopping on
one rule is the property item -1a exists to protect. It cannot land while it costs 0.37 km of
accidental cancellation, so it stays unmerged until the walk is answered.

**The fit that made the penetration look causal.** Fitted across the night's twelve shots,
`miss = 0.3135 - 0.02969 x depth_m` with **R^2 = 0.957**, and at zero penetration it predicts
**0.313 km** against the 0.370 km the previous build actually flew with its depth at +0.1 m. The
two agree inside the old night's own scatter, and nothing else in the shot correlates: frame time
r = -0.15, cutoff residual r = +0.34, arrival angle identical at 7.1 deg.

The slope is the part worth keeping. Geometry alone says a metre of depth is `cot 7.1 deg` = 8 m of
ground; the measured slope is **29.7 m per metre**, near four times that, so the depth is not merely
displacing the burst along its own arc -- a sphere that is wrong by more is also misplacing the
crossing along the track. Pricing this term by the arrival angle alone underestimates it by 4x,
which is the mistake that nearly left it unflown.

**How it arises.** The round now finishes **19.6 m below its own
surface** where it used to finish **0.1 m** off it, because KSA fixed the tiling-detail modifier's
sampling and restored terrain detail that `Slug`'s once-per-frame ground sphere had been standing
in for -- `IGroundTest` promises the sphere is the surface "over the few metres of ground track a
falling round covers in one frame", and a Mk 21 covers about 120 m of it. At a 7.1 deg arrival
19.6 m of penetration is ~157 m of ground, and it lands the round **long**, which is the opposite
sign to the regression: fixing it makes the walk slightly worse before it makes anything better.
`arm/ground-crossing` was built and flown on that reasoning. **It lost**, which is the result at
the top of this item. It turned out to be two faults rather than one: the sphere misplaces the
crossing, which bisecting against the real field fixes, and — sampled at the
top of the frame, where it reads the ground *behind* the round — it also fails to offer the crossing
at all when the ground rises, which no amount of refinement downstream can recover. A broad phase
that can miss is the thing `Ksa/HullTest.cs` is careful never to be. The arm samples both ends of
the frame's travel and keeps the higher surface; nothing on `dev` does. It was first priced at
~157 m from the arrival angle and nearly left unflown on that basis; the regression above is why it
was flown instead.

**Do not read the x2.43 as the mod getting worse.** Two of the three terms that make up a shot
improved sharply and are permanent; one regressed and is item 2, which was already the thing to fix
and is now both larger and better instrumented.

## 2b. Holding a warhead past cutoff costs ~26 m a second — understood

Same shot, cutoff prediction `0.1 km off` every time; release at +50 s and the probe said 0.1-0.2 km,
release at +106 s and it said **6.8 km**, impacts 8.2-9.0 km in a tight group.

**It is the ejection kick losing its leverage along the arc.** A control settles it: with the
separation spring switched off, the impact does not move *at all* across release epochs — 0.0 m at
+50 s, 0.1 m at +106 s. So this is not frame bookkeeping and not the predictor. What changes is what
the same 2 m/s is worth:

| the spring applied at | moves the impact |
| --- | --- |
| cutoff | 8.421 km |
| +50 s | 7.103 km |
| +106 s | 5.672 km |
| +200 s | 3.335 km |

The aim correction converges during the burn against a prediction flown from `CutoffPositionCci`
*with the kick already added* — a release the instant the engines stop — so it takes out the 8.4 km
the kick is worth **there**. Every second the bus then holds on is leverage already spent. The A/B is
symmetric: converged for t+0 is exact at t+0 and 2,748 m off at t+106; converged for t+106 is 2,745 m
off at t+0 and exact at t+106. And it scales exactly with the spring — 1.374 / 2.748 / 5.496 km at
1 / 2 / 4 m/s. About **26 m of miss per second of holding**, or 3,805 m at +106 s over rising terrain.

**Why it bit when it did.** During a coast `Predict` flies from the *live* state, so a correction
that is still observing re-converges for the release epoch the bus has actually reached. The flight
that lost 6.8 km had the correction frozen for the whole ninety-second clearance wait. Both halves
are now fixed: the correction sits out only while thrusters are firing, and the wait is capped at
20 s.

**What remains is scheduling.** A salvo spread over N seconds still spreads at ~26 m per second
whatever epoch the correction is tuned for, so release promptly. The mod's own probe was never
wrong about any of this — it reported 6.8 km accurately. It simply had no lever left, because the
correction can only act through an arc nothing is still burning.

**Not done, and the trap that comes with it.** `Predict` could coast the departure state to the
epoch the warhead will actually leave before adding the kick, which removes the term properly. But
three things downstream assume the prediction's epoch is *now* — the miss comparison against
`_trueAimCci`, `AimCorrection.Observe`, and `TerrainRadiusAt` through `GetCci2Ccf` — and all three
must be carried by the same delay. Measured cost of getting it wrong: **49.2 km at 106 s**, so it
trades a 2.7 km bias for a 49 km one. Worth doing only if prompt release turns out not to be enough.

A second mechanism is real and an order of magnitude smaller: `AimCorrection.BiasCci` is a free
vector frozen in the body's inertial frame while the target turns under it — 0 m at the equator,
**469 m at the flown 26.5°**, 910 m at 60°, over 106 s at a 136 km bias. It only accrues while the
correction is held out, since otherwise the loop re-converges twice a second.

## 3. The first round races the separation — fixed

Flown: `round 1 away` at 23:04:26.025, the split applied at `.071`. One warhead left the attached
stack and five left the shoved bus, which is why the salvo had a 163 m outlier and a 3.6 km group.

Separation now runs before anything decides whether a warhead may go, and the trim then holds the
release until the split has landed — `BusTrim.SettleSeconds`, gated on the decoupler's joint no
longer being there rather than on a timer. The state between "ready to deploy" and "releasing" is
the trim itself.

Confirmed in flight: the split lands at `00:05:59.352` and the first `round 1 away` at
`00:06:02.002`, well after it.

## 4. The view change — fixed, unflown

`KsaWorld.GoTo` ran four steps and claimed they were the four KSA's own "Control from here" runs.
Three of them are. The fourth, `UpdateAfterPartTreeModification`, reaches `UpdateCollisionGeometry`
and therefore the shapes registry, which is locked for the whole vehicle update — and every frame of
this mod runs inside that lock, which is why deferring it a frame could never have helped.

The engine does not call it on this path at all: `Camera.SetFollow` assigns `ControlledVehicle`
itself and stops, and the game's only callers of `UpdateAfterPartTreeModification` are the ones that
genuinely change a part tree. Pointing a camera at a craft changes no tree, so the call was never
needed. It is gone, and the failure with it.

## 5. Re-pointing — closed, and the reason changed under it

**Measured in flight on the separated bus: a pointing band of 22.11 degrees**, against a cant of
six. That is what closed the item: the vehicle's own controller would not make the turn, and no
command the mod can issue changes that — there is no convergence test to satisfy and nothing
refusing the request, the bang-bang tracker simply does not fire inside its dead zone.

**It now reads 0.31 degrees on the same vehicle.** `AngleDeadband` is a high-water mark KSA takes a
`max` against and never lowers, and at the frame the bus separates its rate bit momentarily reads
tens of degrees a second — so the guard was sized by one transient and held there for the whole
coast. `VehicleCommand.TryAim` assigns the attitude profile every frame and KSA's own `max`
re-establishes the real floor on the same frame. Flown 22 August, eight shots an arm interleaved:
band a median 9.62 -> 0.37 degrees, walk from the release probe 1169 -> 363 m, median miss
0.94 -> 0.38 km, ratio 0.37 (97% interval 0.26-0.57, p 0.002). `CLAUDE.md` has the engine mechanism.

So the reason this item was closed has gone: a six-degree turn is no longer inside the dead zone.
What closes it instead is that **there is no longer a cant to correct.** `ab6584d` straightened the
bus's six tubes — every axis is now `(1, 0, 0)` — and `ReleasePointing.Repoint` is
`RotationFromTo(tubeAxis, referenceAxis)`, so on the shipped bus it is the identity and re-pointing
between releases turns the vehicle by nothing. The cheaper answer was to stop creating the spread,
and it was taken.

The machinery stays, behind `RepointBetweenReleases` and still off, because a weapon pack may
register a launcher that *is* canted; `CantedRing` exists as the test specimen for exactly that,
and five suites fly against it.

```
Rocket_1 control: None/None/None, roll Decoupled, control part NONE
  | deadband 12.87 deg, turnaround 15.68/15.68 deg, rate bit 1.568/1.568 deg/s
  | pointing band 22.11 deg
```

Three things in that line, and each closes a question:

- **22.11 deg of band against a 6 deg turn.** Item 5a predicted this: the band is
  `0.5*AngleDeadband + AngleTurnaround`, and `AngleTurnaround` is about ten seconds of one minimum
  thruster pulse divided by inertia — so dropping the spent stack is exactly what widens it. That
  reading predates the deadband fix above; the rest of the line does not.
- **`roll Decoupled`.** Roll *rate* is damped and roll *angle* is free, so even a turn that took
  would not hold: a latched tube axis walks a cone at about 1.8 deg/s.
- **`control part NONE`.** Nothing re-elects a control part on the separated half, so which body
  axis is the nose is undefined on the vehicle the turn would be commanded against.

**What this also explains** is the flown scatter *before the tubes were straightened*. The release
probe reports the salvo thrown 95,
116 and 119 degrees from the platform's track on three otherwise identical runs — the bus drifting
freely inside the 22 degree band it then had. The cant is a cone about the nose, so where the nose
happens to sit decides whether the six kicks cancel or add, across a 141-1,684 m band. That is the
unaccounted spread in the budget and much of the run-to-run variation, and it is not something an
attitude command can reach.

**All three routes to the cant are now moot on the shipped bus**: firing on each tube's own
crossing (5b), trimming between releases (5c) and re-pointing (this item) all correct a spread that
straight tubes do not create. They are priced here against the cant that was — on the guided arc
this term was worth **233 m** of spread, not the 2.69 km an earlier pass priced it at on an arc
nobody flies — and that is what any canted launcher would be buying back.

The mod-side defect 5a found is still worth having and stays in, behind `RepointBetweenReleases`
and still off: the turn is now built from the live tube axis rather than the commanded one, so if a
bus ever exists that can hold the command, it will hold the right one.

## 5-old. Re-pointing — off again, and this time flown

**This is the finding that changes the plan.** Flown on a separated bus with the sequencer on,
commanding six degrees away from the held line made the vehicle *hunt* rather than settle: the
offset climbed monotonically from 5.9° to 10.3° — nearly twice the cant it was correcting — the
sweep never came under 0.08 m/s against a 0.05 gate, every release was a timeout, and the salvo took
three minutes. Against 1.7-0.3 km on the same shot without it. So `RepointBetweenReleases` defaults
**off**, and the ~1 km spread in the current group is the cant it would have removed.

**The command's arithmetic is sound and its *frame* was not**, which took reading the engine to
see — 5a below has it. `held` is genuinely frozen: `IcbmProgram._thrustDirCci` is latched at the
burn, `Coasting()` returns it unchanged, and nothing feeds `_deploy` back into it. The sense of the
rotation is right, `Repoint(a,R)*a == R` is pinned, and `VehicleCommand.TryAim` is equivariant under
any proper rotation applied to both its directions. What was wrong is that the turn was measured at
the launcher's *actual* attitude and applied to the attitude it was *asked* for — and KSA holds no
roll angle at all, so a latched tube axis goes stale as the bus rolls under it. Both terms show up
as an error that grows.

**The sequencer says which way it is failing either way**, instead of waiting out a 60 s timeout and
reporting only that it gave up:

| what the angle does | what it means | gate |
| --- | --- | --- |
| the cant and the sweep fit one budget | the turn worked | `LateralBudgetMetresPerSecond` |
| grows 1° past where it started | the vehicle is not holding what it was given | `NotFollowingDegrees` |
| fails to close 0.25° in 10 s | the vehicle is not turning at all | `ClosingDegrees` |

That distinction is the whole point of tomorrow's flight: *not following* means the bus is being
pushed off a command it accepted, which is residual tumble it cannot null and is fixed on the craft
or accepted as a limit; *stopped closing* means nothing is moving at all, which is authority or a
command that is not arriving.

**Three real defects fell out of building that instrument**, all unflown:

- **An unresolvable tube axis read as zero degrees off the line.** `Vec.AngleBetween(Zero, R)` is
  0.0, so a part tree that stopped answering released every warhead immediately. Same rule as an
  unreadable height field not standing in for flat ground.
- **One tube's timeout is evidence about the vehicle, not about that tube.** A tube that times out
  with the tubes still sweeping now latches "this one will not settle" and the rest go as offered.
  The flown numbers reproduced headlessly — 0.082 m/s sweep, 170 s window, six tubes — take **282 s**
  on the old code and under 30 s now.
- **Every release now leaves a record**, not only the failures: which tube, how far off the line,
  how fast the tubes were sweeping. Six impacts are only diagnosable against six release states, and
  those were silent.

**The gates are now one budget rather than two thresholds**, which is what this morning's flight
justified: the bus reached 0.5° and was refused for sweeping at 0.113 m/s, then released fifteen
seconds later at 5.1° — a *larger* lateral error, because both terms are lateral velocity at the
tube and the pair were being compared against separate limits. Priced in one currency,
`sweep + 2·v_eject·sin(θ/2)` against the old sweep gate's 0.05 m/s, the same flown numbers give
**4.4 s at 0.8° off and 0.139 m/s at the tube**, against 23.9 s at 5.1° and 0.291 m/s.

A vehicle that cannot reach the budget releases at the pointing's best rather than on a deadline,
and the "will not settle" latch now records a *floor* found above budget so the rest of the salvo
skips re-confirming it — 28 s to 3 s for the whole salvo on the flown case.

The ejection speed that prices a cant comes off the munition rather than being assumed, so a
launcher carrying nothing prices it at nothing and two canted launchers need not agree.

Headlessly the mechanism still collapses the tube-cant spread from **1,730 m to 0 m**, and is
roll-independent.

### 5a. What the engine actually does with the command — read out of the source

**The "unknown" in item 5 is closed on two counts, and neither of them is the command being wrong.**
Everything below is cited into `../ksa-game-assemblies/current/src/KSA/KSA`; none of it is flown.

#### The control law

`VehicleCommand.TryAim` sets `AttitudeMode = Auto`, `AttitudeTrackTarget = Custom`,
`AttitudeFrame = EclBody`. That routes to `FlightComputer.UpdateAttitudeTarget`
(`FlightComputer.cs:924-932`), which turns the Euler triple back into `AttitudeTarget.Target2Cci`
and sets `AttitudeTarget.RatesCci` from the frame — zero for `EclBody`
(`VehicleReferenceFrameEx.cs:61`), which is why the mod picked that frame.

The error is built in `UpdateAttitudeTrackError` (`FlightComputer.cs:1160-1209`) and consumed by
`ComputeRcsTrackControl` → `ComputeRcsTrackAxis` (`:1285-1400`), which is a **per-axis bang-bang
phase-plane tracker**: three switching lines (`EvaluateTargetLine`, `EvaluateUpperSwitchingLine`,
`EvaluateLowerSwitchingLine`, `:1431-1512`) and a latched `DeadbandState` per axis. It runs once per
sim step per vehicle from `PhysicsBubble` (`PhysicsBubble.cs:892`, `:1386`);
`MaxFlightComputerRate` (default **10 Hz**, `GameSettings.cs:983`) only schedules *extra* intra-step
recomputes and can never make it faster than the frame rate.

There is **no convergence or settling test anywhere in the engine**. The only reader of
`ErrorAngles` outside the loop is the auto-burn ignition gate (`FlightComputer.cs:356-359`), and
`PhysicsBubble` tests wake and on-rails rather than attitude. So nothing refuses a command for being
small; it is simply not acted on.

#### Why six degrees is not special in the law, and is on this vehicle

The tracker's dead zone is not `AngleDeadband`. It is

```
band = 0.5 * AngleDeadband + AngleTurnaround
```

(`ComputeRcsTrackAxis`, `FlightComputer.cs:1370`; the same offset shifts every switching line).
Inside it the target rate is a flat `±0.5 * RateDeadband` — a crawl, not a null — and the state
machine latches `Inside` and stops firing. And `AngleTurnaround` is floored at **ten times**
`RateDeadband` (`:1079-1083`), where `RateDeadband == RateBit` exactly in `EclBody` and

```
RateBit = MinRotationalImpulse / inertia            (FlightComputer.cs:1052)
MinRotationalImpulse = Σ MinimumPulseTime · torque  over the jets on that axis
                                                   (ThrusterController.cs:213-248)
```

So the pointing band is about **ten seconds of one minimum thruster pulse**, and it scales as
`1/inertia`. Dropping the spent stack is precisely what collapses the inertia, so **separation
widens the engine's own pointing deadband by the mass ratio** — the deadband is a property of the
vehicle, and the vehicle changed. Core's RCS thrusters declare `MinimumPulseTime = 0.00545 s`
(`Content/Core/CorePropulsionBGameData.xml`), which puts the band near `0.055 ×` the bus's RCS
angular authority in rad/s²: negligible at 0.05 rad/s², about 1.6° at 0.5, and past the cant
somewhere near 2. Which side of that the shipped bus is on has never been measured — see the probe
below.

`AngleDeadband` and `RateLimit` are also **ratcheted and never lowered** (`MaxIfRhsFinite`,
`:1071-1076`), copied back to the main-thread computer and persisted into the save; only an explicit
attitude-profile change resets them (`:1647-1651`). They are the smaller term, but they do not come
back down after a separation has inflated them.

#### What separation changes

- **Authority is re-derived correctly.** `ThrusterControllerGlobalState.IsCacheValid` keys on mass
  and centre of mass (`ThrusterControllerGlobalState.cs:51-72`), both of which move a lot at a split,
  and `FlightComputer.ReadUpdatedVehicleConfiguration` is called on both halves inside the same
  `Program.PrepareFrame` (`Vehicle.cs:1457`, `:1406`, `InputEvents.cs:993`). Nothing here is stale.
- **The split-off half gets a brand-new `FlightComputer`** (`Vehicle.cs:1329`) — `AttitudeMode` back
  to `Manual`, no target, deadbands back to Balanced. That costs this mod nothing: `AttitudeHook`
  rewrites the whole command every frame, and `Rehome` follows the launcher onto whichever half it
  went to.
- **`ControlPart` can end up null**, and then `Ctrl2Body` falls back to `Identity`
  (`Vehicle.cs:574`, `ValidateControlPart` at `:2047-2053`): the retained half drops it if the
  control part left, and the split-off half is never given one. Nothing re-elects a replacement. The
  probe reports it, because it silently redefines which body axis the engine calls the nose.
- **What does not change** is the inertia's effect above, which is the whole of it.

#### The real defect was on this side of the line, and it is fixed

**The turn was built in one frame and applied to another.** `Begin` latches the tube axes and the
reference at the launcher's *actual* attitude; the command was `turn · held`, where `held` is the
attitude the vehicle was *asked* for. Those differ by whatever the flight computer's deadband is
leaving on the vehicle, and that difference passes straight through into where the tube ends up —
the fixed point of the sequence is one pointing error off the line rather than on it.

**And KSA holds no roll angle at all in this mode.** `RollMode` defaults to `Decoupled`
(`FlightComputer.cs:48`), and `UpdateAttitudeTrackError` then rebuilds the error as a *pointing-only*
rotation about `cross(bodyX, targetX)` and leaves `ErrorAngles.X` at zero (`:1165-1180`,
`:1192-1204`). Roll *rate* is damped to about the rate deadband; roll *angle* is nobody's. So a
canted tube walks round a cone of twice the cant at whatever the residual roll rate is, and a latched
axis is stale within seconds. The flown 0.08 m/s sweep at the tube is of order 1.8 °/s of body rate,
which walks a tube from 6° off the line to 10° in the seconds the flight measured — **that is the
5.9° → 10.3° climb**, and it is not the vehicle refusing the command.

`ReleasePointing.TryAimTubeFromHere` rebuilds the turn from the **live** tube axis and applies it to
the **live** launcher axis, so the fixed point is the tube on the line and nothing else.
`ReleaseFrameTests` holds it, and fails against the latched form by exactly the pointing error
(3° → 3.0° off, 9° → 9.0°) and, under a free roll, by **12.0°** — twice the cant, which is the whole
cone. The latched form stays as the fallback for a part tree that will not say where it is pointing.

Only the *direction* is the answer: the roll is carried through so the command is a pose at all, and
the engine discards it.

#### The other three questions

- **A better command?** No. `AttitudeTrackTarget.None` is a rate command through
  `UpdateAttitudeRateError` and `ComputeRcsRateControl` (`:1313-1341`), which will not act below
  `0.75 · RateDeadband` — the same quantum, one derivative down. The direct rotation flags are
  cleared by `WithNoRotation()` while `AttitudeMode == Auto` (`:544-552`), so driving the jets by
  hand means taking the attitude to `Manual` and writing a control loop against an actuator that
  answers a frame late. TVC has a continuous proportional law with no deadband
  (`ComputeTvcControlAxis`, `:706-737`) and needs the engine lit, which a coast does not have.
  Setting the target once rather than every frame is not an option either: `PrepareWorker` is the
  only window in which the write survives, so it has to be re-made every frame regardless.
- **Coupling the roll** is the one thing that would let the axes be latched again.
  `FlightComputerRollMode.Up` makes the engine track the full attitude — but it clocks roll to the
  planet (`-atan2(-z.Y, z.Z)`, `:1195`), which is exactly the reference `Sim/AimFrame.cs` exists to
  avoid because it reverses when the nose points at or away from the planet, and its authority only
  fades in inside 15° of pointing (`:1196-1203`). Not built, and not worth building on a guess.
- **Releasing without waiting to settle** does not win. A release costs
  `sweep + 2·v_eject·sin(θ/2)` at the tube whatever the vehicle is doing, and the sweep under a
  constant residual rate is a floor rather than a transient — item 5b's arithmetic, which does not
  change here. What waiting buys is the *cant* term coming down, and only if the vehicle turns at
  all. On the flown numbers the cant is worth 0.209 m/s and the sweep floor 0.08, so a turn that
  works is worth roughly 0.9 km of spread and one that does not is worth nothing. There is no
  version of "release early" that recovers any of it.

#### What is shipped, and what still has to be flown

The re-point stays **off**, and what changed is the command it issues when it is switched on. That
much is arithmetic and is held headlessly; whether the bus will then *follow* a six-degree command is
the pointing band above, and only a flight measures it.

So `IcbmComputer.ProbeControlLimits` prints it. It runs through the coast as well as the burn under
a verbose log and quotes the engine's own numbers — `ActiveControlSystem`, `RollMode`, whether a
control part is still held, `AngleDeadband`, `AngleTurnaround`, `RateBit`, and the band the three of
them add up to, all in degrees.

**The flight with the verbose log settled it twice.** It first read **22.11 degrees** on the
separated bus, which closed the item: the correction needed a turn the vehicle's own controller
would not make. It now reads **0.31**, because the band was a deadband latched at the separation
transient rather than a property of what is aboard — item 5. So the corrected command is worth
switching on and the ~0.9 km of spread is back on the table.

## 5b. Firing each tube on its own crossing — tried on paper, does not win

The bus tumbles, so rather than commanding an attitude it cannot hold, wait for each tube's axis to
sweep past the reference and fire on the crossing. It loses, for three reasons any one of which is
fatal, and `TumblingBusTests` holds the arithmetic.

**The premise was wrong first.** The sweep at the tube is `mean |ω × (mouth − CoM)|`, which under a
constant tumble is a *constant* — a floor, not a transient — so it costs the same whenever the round
goes. There is no "the sweep is worst exactly where the cant is best" trade to exploit. That is only
true of a vehicle that is settling.

- **The case that looks ideal is the null case.** A roll about the bus's own axis carries all six
  tubes round one cone of the cant's half-angle, so each tube arrives exactly where the last one
  was: six degrees off, for ever. Measured at zero variation through two full rolls — there is no
  crossing to predict.
- **The ceiling is unreachable anyway.** Under a fixed-axis tumble a tube's nearest approach is one
  cant times the cosine of its clock angle from the tumble plane, so two tubes of six can reach the
  line and the two a quarter-turn away never leave the full cant. Best over every tumble axis is
  3.37°, or 0.117 m/s at the tube against 0.209 firing now — and that is an oracle with no window,
  no clock and no magazine order.
- **Waiting is self-defeating.** The reference is latched while the bus is on it, so from that
  instant the bus walks away at the tumble rate and every second spent on one tube adds drift to
  every tube not yet fired. The two cancel: over 0.5–6 °/s the mean release angle goes 6.0° to
  5.2–7.2°. It does not come down.

Priced with waiting free it is 76–94% of firing now, and one geometry at 6 °/s reaches 122% —
worse. A salvo runs 7.7–60 s, so item 2b's ~26 m per second of holding adds 200–1,560 m on top of
that, uncounted. Nearest-tube-first ordering is worse still, because it breaks the ring's symmetry
without shrinking its radius.

**What would reopen it:** the whole argument assumes the bus's residual motion is a fixed-axis
tumble at roughly constant rate. If a flown `SweepMetresPerSecond` turns out to *oscillate* — a
floor with peaks well above it — the bus is settling badly rather than tumbling, the third leg
weakens, and the geometry is a different problem.

## 5c. Trimming the bus between releases — priced, and the bus cannot fly it

The velocity-side version of item 5: rather than turning the bus so each tube lies on the line,
leave the attitude alone and change the bus's *velocity* before each release, so that
`bus velocity + this tube's kick` is what the arc wants. That is what a real post-boost vehicle
does, and the machinery for it already exists — `CorrectCoastArc`, `BusTrim.Resume` and
`PostBoostAim` were all flown this session.

**The trade is favourable and it is not close.** `PerTubeTrimTests` prices it on the 3,459 km shot
through the real predictor and drag model — on the *idealised* arc from a 200 km circular pickup,
held retrograde, which item 9 measures as about twice as sensitive to a metre a second as the one
the guidance actually leaves the bus on. Read the ratios between the rows rather than the metres:

| | spread across the six |
| --- | --- |
| as canted, released together | **2,692 m** |
| as canted, at the magazine's own 3 s pace | 2,521 m |
| per-tube trim, 3 s pace | **231 m** |
| per-tube trim, 8 s pace | 625 m |
| per-tube trim, 30 s pace | 2,410 m |

Holding costs `~15.4 m` of miss a second on this trajectory — the ejection kick losing its leverage,
item 2b's mechanism, which was 26 m/s on the flown one — and the salvo has five gaps in it, so the
ramp is about 77 m of spread per second of pace. Break-even is around **35 seconds a tube**, and
driving the real `BusTrim` against a bus that *has* lateral jets nulls one tube's 0.209 m/s in
**1.5 s**, which is `BusTrim.SettleSeconds` and nothing else. On paper it wins 2.3 km of spread.

**And it could not be flown, because the correction was exactly the one thing the bus could not
do.** A cant is a *cone*, so every tube's kick carries the same axial share and the difference
between any two of them is perpendicular to the bus's axis. Measured off `Arsenal.MirvBus.Tubes`:
each tube is 0.2093 m/s from the mean, of which **0.0110 is axial and 0.2091 lateral**, and every
tube-to-tube step is **1.5e-6 m/s axial** — a pure lateral translation to six significant figures.
The bus was then four clusters of four laid out for pitch, yaw, roll and axial thrust, with no
lateral translation at all, so the axial pair had literally nothing to fire at. Run against that
layout the real loop strikes off each lateral direction in turn and gives up after **4.0 s with
the whole 0.209 m/s still on the vehicle** — six times over, for 24 s of coast and no change to
the group. Those metres a second are priced at the 2 m/s ejection kick that then shipped and scale
with it: item 0a quartered it.

**Nothing rescues it.** Turning the bus so the axial pair points along the needed lateral is two
full settles per tube on a vehicle that item 5 measured *hunting* at a six-degree command — and a
vehicle that could do that could do the six-degree turn instead, which costs no propellant at all
and removes the term completely. Letting the arrival time float per tube is one parameter against a
two-dimensional lateral error. Firing opposite pairs cancels on the bus and not on the warheads.

**What would reopen it was lateral translation on the bus, and the bus now has it** — one radial
jet per cluster, item 0. The arithmetic above says that is worth about 2.3 km of spread; item 0 is
the other half of the trade, where authority acting on a reading that is not yet trustworthy is what
made the shot four times worse. `PerTubeTrimTests` is kept so it can be re-priced against a
different cant, ejection speed or trajectory without redoing any of this.

**So item 5 is still the answer to the cant.** Its open question — why a separated bus does not
hold a six-degree command — turned out to be a latched deadband, and the probe now prints 0.31
degrees where it printed 22.11. What is left is whether the bus follows the command, which is a
flight.

## 6. Point the bus at the target on release — cosmetic, do it last

Mechanically a couple of lines: the sequencer rotates whatever attitude it is handed, and the
reference is measured from wherever the bus holds, so the geometry and the prediction follow.

**But not yet.** The ejection is 0.5 m/s along the tubes, so the release attitude decides which
axis that error lands on, and nothing compensates post-cutoff. After item 1 the error is small
enough that the attitude stops mattering and this becomes free.

## 7. The aim correction is marginally stable — retired by 7b and 7c

Found by flying the scenario, which is what it is for. On a near-orbital pickup aimed 3,459 km
downrange the correction walked past its own best and kept going while the miss grew:

```
bias   0.0 km  miss  55.1 km
bias  76.9 km  miss  43.7 km   <- best
bias  98.7 km  miss  44.7 km
bias 172.0 km  miss  65.2 km
bias 300.0 km  miss 209.2 km   <- pinned at the limit
```

**The gain is right for one plant and not the other.** While the solver may pick its own flight
time, moving the aim moves the impact by about as much again and a half is stable — that is the
case `AimCorrectionTests` covers and it converges in a dozen cycles. Once the guidance latches the
arrival, the same aim change forces a different trajectory to arrive at the same *instant*, and on a
shallow arrival that amplifies the response past where a half holds.

Keeping the best and stopping when it cannot be improved took the shot from **226 km to 63 km**, and
that is in. What it does not do is find the good answer:

| | bias | miss |
| --- | --- | --- |
| clamp at 300 km, no guard | 300 (pinned) | 226 km |
| clamp at 300 km, guard | 77 (its best) | 63 km |
| clamp at 2,000 km as a probe | **29** | **0.06 km** |

The probe is the interesting row: the loop *can* reach a 29 km bias and a converged shot at the same
gain. So the miss-against-bias curve is not monotonic — there is a hump between 0 and 29, and a
guard patient enough to cross it is also patient enough to let a real divergence run. Two runs from
the identical save took different paths, so it is timing-dependent rather than structural.

**Measuring the step did not fix it, and the reason is the interesting part.** Sizing the step by
the response measured from consecutive cycles converges against any fixed plant — the test drives
0.5 through 8 — and in flight it changed nothing: 63 km with a fixed fraction, 60 km measured.

Because **the miss is not a function of the bias.** Two flights from the identical save, at the same
28.6 km of bias, one predicted 54.3 km of miss and the other 0.06 km. The correction moves the aim,
the guidance re-solves, a different arc comes back, and the arrival was latched against a state that
no longer applies — so the loop's plant is its own output history. Step sizing cannot fix that,
whatever the step.

**Sequencing the two loops is what worked.** Leaving the arrival free until the aim stops moving,
then committing and freezing the aim, took the same shot from **226 km to 11.3 km**:

| | miss |
| --- | --- |
| clamp only | 226 km |
| stop when it stops helping | 63 km |
| measured step, bounded by the error | 60 km |
| arrival left free until the aim is steady | 12.3 km |
| and the aim frozen when it commits | **11.3 km** |

**What is left is not the aim.** Flown with the freeze in, the correction converges to **1.2 km** of
predicted miss and holds its bias — and the prediction then drifts to 10.9 km with the aim
unchanged. The shot goes on evolving through the rest of the burn after the arrival is committed,
and a frozen correction cannot follow it.

So the next question is when to commit, not how to correct. The arrival is latched as soon as the
aim is steady, which on this shot is well before cutoff; latching at cutoff instead would let the
aim track the whole burn. What stops that being obviously right is the reason the latch exists —
a loft above one walks the answer outward every cycle, 162 km measured — so the case to check first
is whether that failure needs the latch *early* or merely needs it *at all*. With `Loft` at one the
cheapest arc is the natural one and there is nothing to chase.

**`IcbmConfig.MinArrivalAngleDeg` does not reopen that.** It constrains the flight-time search rather
than multiplying its answer, and a predicate is idempotent where a multiplier is not: the arc that
satisfied the floor satisfies it again, so seeding the next cycle with the constrained time returns
the same time. Measured over twelve cycles at loft 1.4 with an 18 degree floor: 639.6 s every one.
So a steep shot asked for with the floor is a shot with nothing to chase, and the question above is
still about `Loft`.

**The older structural candidate**, and the only one that addresses the cause rather than
damping it: `IcbmProgram.Resolve` latches `_arrivalFromLaunch` on the *first* closed-loop cycle,
before the correction has moved anything. Latching it after the correction has settled leaves both
loops solving the same problem instead of one solving against the other's leftovers. The risk is
named in `docs/ICBM-GUIDANCE.md`: the latch exists to stop a lofted shot chasing its own arc
outward, which cost 162 km, so the window before it is latched has to be bounded.

**Three older candidates, none tested:**

- **A lower gain.** Principled for a marginally stable loop, and the flight has 922 observations
  against the dozen the tests use — but four tests encode a convergence rate chosen for a half, and
  changing them to suit a tuning change wants better evidence than one run.
- **Not latching the arrival until the correction has settled.** This addresses the cause rather
  than the symptom: the plant only changes because the arrival is pinned first.
- **A guard on the trend rather than the value**, so a hump is crossed and a divergence is not.
  Answered by 7c, and by patience rather than by trend: the guard already keeps the best aim and
  reverts to it, so a hump is crossed by simply allowing more cycles.

## 7b. The correction was nulling the planet's rotation — fixed, flown

**This is what item 7 was actually about**, and it is not a stability problem.

`ImpactPredictor` un-carries its impact by its own flight time, which puts the ground point in the
body-fixed frame of the instant the arc **departs**. While the engines burn, `Predict` departs from
`CutoffPositionCci` — `SecondsToCutoff` in the future. It was then scored against `_trueAimCci`, the
target in the frame of **now**. The difference is the planet's turn over the rest of the burn:
465 m/s at the equator, 416 m/s at the flown -26.5 degrees.

The tell, headless over a 55 s burn with no correction running: the **true** miss is flat at
85.66 -> 85.02 km across the whole burn, while the **reported** miss walks 60.18 -> 85.02 km,
tracking the carry term for term. Nothing about the shot moves. Only the ruler does.

So the correction converged on the artefact, and whatever was left of it when the aim froze became a
permanent bias pointing the wrong way.

| range | uncorrected | as shipped | scored in one epoch |
| --- | --- | --- | --- |
| 2,000 km | 0.01 km | **191.59 km** | 0.01 km |
| 3,459 km | 19.56 km | 37.10 km | 0.38 km |
| 5,000 km | 64.48 km | 22.62 km | 1.16 km |
| 7,645 km | 186.13 km | 2.11 km | 15.74 km |

The 2,000 km row is the damning one: a shot that needed no correction at all was put 191 km wrong by
nulling the rotation. The 7,645 km row is the reverse coincidence — the artefact happened to point
the same way as a 186 km drag shortfall, which is how a broken loop can look like a working one.

**Flown at 3,459 km: 11.25 km mean -> 5.35 km mean**, six of six arriving.

**What this retires.** The diagnosis in `41dc88d` — that the arrival latch changes the plant and the
4.9 -> 13.4 km walk is the correction going unstable — is wrong. With the arrival latched and the aim
frozen the true miss does not move at all; only the reported one does, and by exactly the carry.
`AimCorrection`'s best-tracking and measured response are still worth having, but they were treating
a symptom.

**The 7,645 km row is a different limit** — headroom in the stopping rule rather than the ruler.
That is 7c.

**`TerrainRadiusAt` had the same fault**, reading the height field in the frame of now for a point
un-carried to the cutoff epoch — so the arrival was flown against ground a whole burn's rotation
away. Flown at 4.85 km mean against 5.35 before it, **which is inside the noise**: the harness was
not yet controlled (see below) and repeat runs on one build vary by about half a kilometre. The
change is right for the same reason the one above it is; the flight neither confirms nor refutes its
size.

## 7c. The stopping rule could not cross a hump — fixed, unflown

With the epoch fault gone, 7,645 km was the one range left at 15.74 km. The per-cycle trace says
why, and it is not stability: the loop finds a good aim, walks past it, and is stopped three cycles
into a patch that is **five** cycles long.

```
t       bias        predicted miss   worse
0.00      0.00 km   173.23 km        0
0.51     43.31 km   147.39 km        0
1.03    190.69 km     5.79 km        0
1.55    196.48 km     3.34 km        0   <- banked as the best
2.07    194.36 km     5.06 km        1
2.59    191.16 km     5.70 km        2
3.11    187.54 km     5.89 km        3   <- WorseBeforeStopping, revert to 196.48 km
3.63    196.48 km    15.86 km            <- the same aim, now worth five times the record
```

Let the same run continue and the patch turns over: 5.83, 3.64, 3.31, 3.10, 2.85 and on down to
1.73 km at a 158 km bias — **1.15 km flown**, against 15.74. So the miss is not a monotonic function
of the aim, and the best has to be walked past to reach the answer beyond it.

**Stopping early is not the conservative choice.** The last two rows are the whole finding. Giving
up is what makes `AimCorrection.IsSteady` true, which is what commits the arrival, and the aim the
loop kept is then being judged against a different trajectory — banked at 3.34 km, reverted to, and
worth 15.86 km one cycle later. The record it stopped for describes a plant that no longer exists,
and nothing re-opens the loop. Waiting, by contrast, costs cycles and nothing else: the best aim is
kept and reverted to either way, and `MaxMetres` bounds where the aim can go meanwhile.

So `WorseBeforeStopping` is 12 rather than 3 — twice the measured patch, and six seconds at the
half-second prediction interval, which leaves a real runaway back on its best well inside
`IcbmProgram.LatchArrivalWithinSeconds`. Past that window the arrival commits whatever the aim is
doing and the loop is frozen on it, so that is the bound at the other end. The threshold is sharp
and the result beyond it is flat: 3, 4 and 5 leave 15.74, 18.99 and 22.36 km; 6, 8, 12, 20 and 40
all leave 1.15 km at the same 158.1 km bias.

| range | uncorrected | before | after |
| --- | --- | --- | --- |
| 2,000 km | 0.01 km | 0.01 km | 0.01 km |
| 3,459 km | 19.56 km | 0.38 km | 0.38 km |
| 5,000 km | 64.48 km | 1.16 km | 1.16 km |
| 7,645 km | 186.13 km | **15.74 km** | **1.15 km** |

`AimConvergenceTests.TheLoopWalksPastItsOwnBestToReachTheAnswerBeyondIt` is the regression, and it
fails against the old constant on the aim the loop ends up holding rather than on the flown miss —
that is the reading that says *why*.

**Ruled out by measurement, each swept across all four ranges:**

- **`ImprovedByMetres`, absolute or relative.** The excursion is 1.7 km above the banked best, far
  outside any sane dead band. 50, 250 and 1,000 m all leave 7,645 km at 15.74 km; only 3,000 m — a
  band the size of the whole miss — crosses it, and that costs 3,459 km 0.38 -> 1.41 km. A bar
  relative to the best does not reach it either: five per cent of 3.34 km is 167 m, under the 250 m
  floor.
- **`ResponseFromMetres`.** Every step through the patch is 2.1-8.9 km, so the 500 m gate is open on
  every cycle. 50, 500 and 5,000 m are identical at 7,645 km.
- **`MaxResponse`.** 3, 6, 12 and 40 are identical at 7,645 km.
- **Running out of burn.** The shot gives 75 prediction cycles and the loop used seven of them.

**`MinResponse` and `Gain` do move the number, and neither is the cause.** They size the blind first
steps, so the loop meets the curve somewhere else and lands somewhere else: `MinResponse = 0.5`
leaves 7,645 km at 0.74 km and `Gain = 1.0` at 1.79 km. Neither is a fix. Both are one geometry's
luck and the neighbouring values are damaging — `MinResponse = 1.5` and `Gain = 0.1` put 5,000 km at
63.1 and 65.8 km — and the flown reason for `MinResponse = 1` (28.6 -> 192.9 km of bias in a single
cycle) is not visible in this rig at all. `WorseBeforeStopping` is the only one that leaves the other
three ranges bit-identical at every value swept.

**Unflown.** Headless across four ranges, and no more than that.

## 7f. Three stopping rules were sized for a kilometre shot — built as an arm, unflown

The shot lands at **0.05 km** and every constant that decides when a correction stops was chosen
against a miss twenty times that. The flown log of 2026-08-24 (`~/shots/2026-08-24-pss`,
`001-pss.log`) shows all of them binding, which is what separates this from arithmetic.

**The post-boost loop stopped on the improvement bar, not on the trade it exists to make.**

```
post-boost: correcting the aim, 1.7 km out (pass 1)
   ... 1.7, 0.7, 0.6, 0.4, 0.2, 0.2 ...
post-boost: correcting the aim, 0.1 km out (pass 8)
post-boost: 3 passes without beating 0.2 km
release probe: 0.1 km from the target
```

`AimCorrection.ImprovedByMetres` was a fixed 250 m, so below about 200 m no pass can register as
an improvement and `PassesWithoutImprovement` fires within three. The payback rule — the one that
is *meant* to stop it — was still open: passes ran 1.5 s apart, which at the real holding cost is
about 10 m against 100 m on the table.

**And the holding cost was four times what it costs.** `PostBoostAim.HoldingCostsMetresPerSecond`
is 26, measured at the 2 m/s ejection kick. Item 0a quartered the kick and *recorded the
consequence in its own section* — "holding a warhead costs 26 -> ~6.5 m of miss per second" — and
the constant never moved.

**The bar cannot simply shrink, because it is the observer's noise that sets it.** Every pass is
judged from a prediction flown off the bus's state with the kick added along its nose, so both the
trim's leavings and the nose's wander move the reading with nothing about the shot having changed.
Measured by `PostBoostObserverTests`:

| | moves the reading |
| --- | --- |
| a nose sitting on the 2 deg settle gate | 13 m (3-7 m per degree) |
| the trim at `BusTrim.SettledMetresPerSecond` = 0.02 | **70 m** |
| the trim at 0.01 | 30 m |

So the trim is the binding instrument, not the nose — and `SettledMetresPerSecond`'s own doc
comment claimed 68 m was "comfortably under the best a shot has flown", which was true when it was
written and is now above the whole miss. The arm moves the three together: a bar of five per cent
of the best (250 m at the five-kilometre miss it was chosen at, so nothing about a shot that size
changes) floored at 50 m, the holding cost to 6.5, and the trim's settle to 0.01. Safe because
`BusTrim` bands at `max(this, one frame of firing)`, so the quantum takes over rather than the trim
hunting.

**Headlessly it is invisible, and that is expected.** `MirvBudgetTests` does not model
`PostBoostAim` at all, and the boost loop reads bit-identical at 2,000, 3,459, 5,000 and 7,645 km
before and after — so the whole effect lands exactly where the log says it binds. The regression is
`PostBoostAimTests.PassesThatStopBeatingTheBestEndIt`, which fails against the old bar: on the flown
readings a fixed 250 m banks 400 m and stops, where the same readings go on to 100.

### `SteadyMetres` looks like the same fault and is not — measured, refused

The pre-cutoff correction froze at **1.7 km after 7 of about 77 cycles** and never moved again:

```
34.265  bias  0.0 km  miss 74.2 km
35.493  bias 76.8 km  miss  3.9 km
37.015  bias 86.8 km  miss  2.6 km
37.524  bias 89.4 km  miss  1.7 km   <- last move, then 39 s of burn with neither changing
```

The step sizes track the miss exactly, so the next step would have been 1.7 km — under
`AimCorrection.SteadyMetres` at 2,000. `IsSteady` is what latches the arrival and the latch is what
calls `Freeze()`, so **the threshold is the miss it stops at**: a converging loop trips it precisely
when its remaining miss falls under the number.

Tightening it is worse at every value swept, through `MirvBudgetTests` at 3,459 km:

| `SteadyMetres` | group bias |
| --- | --- |
| **2,000, as shipped** | **415 m** |
| 500 | 415 m |
| 100 | 2,329 m |
| 25 | 2,568 m |

**Latching early is right.** The loop wants to do its converging against the plant that actually
flies, and an aim optimised against a *free* arrival is worth several times more once the arrival is
pinned under it — which is item 7c's finding arriving by a second route, on the other branch of the
same `IsSteady`. What the rig does say is that not freezing at all is worth 415 -> 72 m at this
range; it is also worth 1.00 -> 16.59 km at 7,645, which is why the freeze exists.

`MaxResponse` is inert: 6, 12, 24 and 60 are bit-identical at all four ranges, confirming item 7c's
sweep from a different direction.

## 7g. Never-freezing loses, and the target is on a crest — flown 2026-08-25

**Flown and closed.** The hypothesis this section sets out is wrong, and the night that tested it
found something else. `docs/SHOT-PROTOCOL.md` is how it was spent; the shots are in
`~/shots/2026-08-25`.

### What the night settled — 48 shots, 2026-08-25

| arm | n | median | vs control | its own prediction | residual at cutoff |
| --- | --- | --- | --- | --- | --- |
| `base` | 23 | **1.37 km** | — | 2.02 km | 0.070 m/s |
| `ownint` | 21 | 1.57 km | 1.14x, **UNRESOLVED** (0.35-3.20, p 0.24) | 3.49 km | 0.070 m/s |
| `neverfreeze` | 2 | **5.72 km** | **5.7x**, dropped at shot 008 | 0.03 km | **0.580 m/s** |
| `ownint+neverfreeze` | 2 | **5.31 km** | **5.3x**, dropped at shot 008 | 0.03 km | **0.485 m/s** |

**Never-freezing loses, and the pair loses with it** — the *pair does not win* row below. So **do not
tune the freeze**. Two things the flight adds to what the rig already said:

**The never-frozen loop is precise and wrong.** Its groups are the tightest of the night — spread
0.01 km, six warheads inside ten metres — landing 5.7 km out while reporting 0.03 km. That is
`MirvBudgetTests.WhereTheNeverFrozenLoopGoes` reproducing in flight, and it settles the two rigs
against each other: `AimConvergenceTests` reads never-freezing as a win because it scores through the
predictor the loop converges. Fixing the predictor first does not rescue it — `ownint` is in the
pair, and the pair still lands 5.3 km out with its own reading at 0.03 km.

**And the freeze is load-bearing a second way nothing predicted: it is what lets the burn stop.** The
never-frozen arms cut off with **0.5 m/s** still to gain against 0.07 for the control, eight times the
residual — an aim that never settles is a cutoff condition that never settles. The miss is
`residual x dMiss/dV`, so that term alone is worth kilometres, and it is separate from the aim point
being wrong.

### The rest of the night measured the hill, not the guidance

`ownint` came back UNRESOLVED, and the comparison should not be believed at this target — because
**every impact of the night landed on one of five places on the ground**, and which one is decided by
the terrain rather than by the trajectory. Landing site against the ground height under it, all 44
live-arm shots:

| site | bearing from the aim point | ground | where shots landed | shots |
| --- | --- | --- | --- | --- |
| C | 1.51 km at 262° | 4.560 km | 1.40-1.50 km | 4 `base`, **19 `ownint`** |
| **A — the aim point** | — | **4.657 km** | 0.04-0.14 km | 4 `base` |
| B | 0.37 km at 053° | 4.646 km | 0.34-0.54 km | 7 `base`, 2 `ownint` |
| D | 4.43 km at 075° | 4.190 km | 4.50-4.60 km | 5 `base` |
| E | 5.54 km at 076° | 4.128 km | 5.40-6.00 km | 3 `base` |

Every site is on one line of bearing 075°/255° — the ground track — and **the aim point is on a
crest**. The ground rises to it from the WSW at a gradient of 0.064 and falls away to the ENE at
**0.1055**. The warheads arrive at **5.7°**, which is a gradient of **0.0998**.

**The far-side slope and the arrival gradient agree to 5.6%.** That is a degenerate intersection: past
the crest the trajectory and the ground are very nearly parallel, so a few tens of metres of
trajectory height at the crest decides between stopping on the crest and running four and a half
kilometres down the back of it. 467 m of fall times `cot 5.7°` = 10.02 is 4.68 km of ground, and the
measured run-out is 4.43-4.60 km.

So the night's two live arms differ mostly in **where their aim bias sits relative to that crest**:
`ownint` lands 19 of 21 shots at C, on the well-conditioned upslope short of it, and never once ran
past; `base` scatters across A, B, C and — on 8 of 23 shots — over the crest to D and E. Past 2 km:
`base` 8/23, `ownint` 0/21, Fisher exact **p = 0.0039**. That is a real difference in outcome and it
is **not** a claim that `ownint` guides better. It is a claim about which side of a hill each arm's
bias happens to fall on.

**Nothing in the aim loop can fix a degenerate intersection**, which is why this belongs to
`docs/ARRIVAL-ANGLE.md` rather than here: at 15° the arrival gradient is 0.268 against the same 0.1055
of terrain and the intersection is well conditioned again. `arm/arr15` is built and parked, and this
is the first flown evidence for the case that document makes.

### What to do next

1. **Do not merge `neverfreeze` or the pair.** Settled, twice over.
2. **Do not judge `ownint` at this target.** Its 21 shots measure a hillside. It is right on
   principle — `CLAUDE.md` already says a prediction modelling drag its own way is a second flight
   model — but this night neither confirms nor refutes it. Re-fly it somewhere the arrival is not
   parallel to the ground, or at a steeper arrival.
3. **`arm/arr15` is not refused, and it is expensive.** The headless check is done — see *A 15°
   floor is reachable, and costs 2.7x the propellant* below. It will not report
   `IcbmReach.TooShallow`; whether the stack can pay for it is the open half.
4. **Keep 26.5S 64.0W as the standard target.** It has now been measured and it is a plain — see
   *The old target is not a crest* below. This one flew 26.485S 68.148W and turned out to be sitting
   on a crest; the two are not interchangeable, and a second target has to be checked before it is
   adopted rather than after a night has been spent on it.

### A 15° floor is reachable, and costs 2.7x the propellant

`ScenarioArrivalFloorTests` sweeps `BallisticArc` at the geometry the scenario actually flies and
costs each floor as a difference between two required velocities — which is why none of it needs the
pick-up's flight path angle, a number no log records.

**The range is 3,459 km, not the 2,433 km written elsewhere in this document.** Every night's log
reports it from the craft's own position at pick-up: 247 shots across 21 nights at 3,441–3,461 km to
the old target, and 3,037–3,056 km to 08-25's. No shot in any kept log was flown at 2,433 km, and the
verbose trace quoted under *The root cause* below does not appear in one either. Both are unsourced
rather than known wrong — flag rather than correction.

The arc flown today is **3.58° in vacuum**, which is the 7.1° the warheads arrive at through the air;
`BallisticArc.Solution.ArrivalAngleDeg` records that drag bends a graze and leaves 10–30° alone, so a
floor of 15 constrains something the air will not then move.

| floor | arrives | flight | extra over the flown arc | propellant | vs the flown 85.9 t | precision gain |
| --- | --- | --- | --- | --- | --- | --- |
| — | 3.58° | 486 s | — | 85.9 t | 1.0x | 1.0x |
| 7° | 7.04° | 543 s | 814 m/s | 99.4 t | 1.2x | 2.0x |
| 10° | 10.04° | 591 s | 1,389 m/s | 159.1 t | 1.9x | 2.8x |
| **15°** | **15.03°** | **670 s** | **2,176 m/s** | **228.6 t** | **2.7x** | **4.3x** |
| 20° | 20.04° | 751 s | 2,833 m/s | 277.6 t | 3.2x | 5.8x |

Propellant is off the shipped stack's own throttle trace — 12,164 kN against 571.7 t burning 2.855
t/s, so 4,261 m/s of exhaust velocity, which reproduces the 85.9 t the base shot burns for its 655
m/s. The precision gain is the geometric `cot(g)` term, and every row buys more of it than it costs.

**What is still unknown is whether the tanks hold 228.6 t.** KSA reports only the running stage, so
the remaining propellant is not observable from a log — which is the same limit that stops the launch
gate refusing a short shot up front. One shot flown by hand settles it: a stack that cannot pay
reports `IcbmReach.ShortOfPropellant`, flies short, and says by how much.

### The clearance latch put the bus into the booster — do not build it again

7g records the trim nulling `_separatedFrom` on `trim.Done` as a defect worth up to 20 s a pass, and
proposes latching the clearance. **It is not a defect. It is the gate re-shutting, and it is load
bearing.** Flown 2026-08-25 with a latch in, and the bus hit its own spent stack.

The argument for latching is that separation only ever opens, so a gap once measured cannot close.
That is circular: it holds only while the gate is **shut**. Once the clearance answers clear the trim
runs, and the trim's whole job is to null the velocity difference — which *is* the separation. The
pair can close again, and only a fresh reading every pass can see it.

What the latch removed, from `003-base.log`'s own sequence:

```
+5 s    trimming 2.46 m/s on the tail        <- cleared on a measured gap, trim runs
+9 s    trimmed to 0.022 m/s                 <- trim.Done nulls the reference
+10 s   post-boost: correcting the aim, 2.9 km out (pass 1)
+10 s   trimming 7.29 m/s on the tail        <- with a latch: fires at once, ~25 m out
                                                without one: held to +20 s, ~24 m further away
```

Then 28 s of thrashing — tail, port, belly, port — until `nothing left aboard moves the bus, 6.68 m/s
left on the bus`. The warheads went anyway, off a bus that had run its thrusters dry and been driven
back into what it dropped.

**Two things were changed at once and only one is settled.** The same latch flew the floor-0 baseline
an hour earlier, which also trimmed 7.3 m/s right after `trim.Done` and did *not* hit — it converged
in ten seconds. What separates them is that the 15-degree shot asked for a correction far beyond what
the bus could make. So the latch is a necessary contributor rather than a proven sole cause, and the
size of the post-boost correction under a steep floor is its own open question — see below.

**The real defect behind 7g's note is still there**, and latching was the wrong fix for it. Keeping
`_separatedFrom` alive past `trim.Done` would let every pass measure a real distance instead of
falling back to a clock, which is what safety actually wants. Not built, and not to be built without
flying it.

### The bus has all six directions, and the trim strikes off the ones that lose a race

Two things were believed about the shipped bus and both are wrong. `CLAUDE.md` said its lateral
authority was none; `TrimBus` said `LateralAcceleration = 0` "is the vehicle that actually flies".
Read off the XML at KSA's 0.5 translation-enrolment threshold:

| command | enrolled | thrust along | off-axis | net torque |
| --- | --- | --- | --- | --- |
| forward / aft | 4 | **4.000** | 0.000 | 0.000 |
| port / starboard | 6 | **4.243** | 0.000 | 0.000 |
| belly / back | 6 | **4.243** | 0.000 | 0.000 |

Twenty nozzles: four clusters of an axial pair and two 45° diagonals, plus four more diagonals. The
diagonals are each a pure roll couple on their own, and the enrolled *set* for any translation
cancels to zero torque. All six directions are live, and the flight confirms it — the 2026-08-25
baseline trimmed starboard 294 times and back 292 and converged, which no bus without lateral
authority could do. `tools/model/checkring.py --translation` is the check.

**So the give-up is a false positive.** `BusTrim` strikes a direction off after
`DirectionStallSeconds` of firing without its own component falling by `ProgressMetresPerSecond`. On
a dead axis that reads correctly. On a live one it fires whenever the *reference* moves faster than
the bus can push, and `BusAuthorityTests` finds the threshold exactly there:

| reference drift | ÷ lateral authority | outcome |
| --- | --- | --- |
| 0.50 m/s² | 0.86 | converged, 21 s |
| 0.55 | 0.94 | converged, 51 s |
| 0.58 | 0.99 | converged, 120 s, 0.549 m/s left |
| **0.60** | **1.03** | **struck off starboard, 2.02 m/s abandoned** |
| 0.80 | 1.37 | struck off starboard, 4.94 m/s abandoned |

The axis is healthy and losing a race, and **the strike is permanent** — once struck it is excluded
for the rest of the flight, so a transient in the aim correction disables a working thruster for
good. That is the runaway `BusTrim.MaxMetresPerSecond` already names: the aim correction and the
trim driving the same vehicle through the same prediction.

It bites `arr15` and not the baseline because a steep floor asks for a much larger post-boost
correction — 7.3 m/s against 2.45 — which is what puts the reference's motion above what the bus can
chase.

**Not fixed.** The stall test cannot tell "cannot push this way" from "cannot keep up", and it needs
to: the first should be struck off for good and the second should not be struck off at all. The
distinction is available — the bus knows its own measured acceleration, and a component that is
falling *slower than commanded* is different from one that is not falling. Wants building and
flying, in that order.

### A 15-degree floor is reachable and the bus cannot fly it

`reach Reachable`, confirmed in flight 2026-08-25 10:20 — the floor is not refused, which was the gate
`arm/arr15` was parked behind. **2,345 m/s to gain** at the 207 km pick-up against the baseline's 655,
and `impact in 9:18` against `7:09`.

What it ran into is not the booster's propellant but the **bus's**. The post-boost correction came out
at 7.3 m/s and rising where the baseline needs 2.45, and the bus exhausted its thrusters part way
through: `nothing left aboard moves the bus, 6.68 m/s left on the bus`. Releasing 6.68 m/s off the
solution is a scattered group whatever the arrival angle buys.

So the next question for `arr15` is not reach and not booster propellant. It is whether the bus has
the authority for the correction a steep arrival demands, and that is a `MirvBudget` question rather
than a trajectory one.

**The headless prices are a ranking, not absolutes.** `ScenarioArrivalFloorTests` put the 15-degree
arc at 670 s and 2,176 m/s over the flown arc; it flew 558 s and 1,690 m/s. Right order and right
direction, roughly 20-30% high, which is what fixing the burnout at 207 km and letting the solver pick
everything else is worth.

### The old target is not a crest

The obvious worry after the above is that 26.5S 64.0W is ill-conditioned too, in which case a good
deal of what this document records — the 0.05 km headline included — would be measuring a hillside.
It is not. Pooling the warhead traces from all 21 nights flown there, 247 impacts, the ground is
flat at every scale the shots sample:

| impacts within | n | downrange span | ground downslope | amplification |
| --- | --- | --- | --- | --- |
| 200 m | 21 | 355 m | **-0.49 %** | 1.0x |
| 500 m | 75 | 962 m | +1.21 % | 1.1x |
| 2 km | 213 | 3.7 km | +1.75 % | 1.2x |
| 20 km | 243 | 14.2 km | +1.13 % | 1.1x |
| everything | 247 | 90.3 km | **+0.27 %** | 1.0x |

Against an arrival gradient of **0.1246** at 7.1°. The fit residual is 0.7 m rms within 200 m of the
aim point and 19.5 m over the whole 90 km, and the ground height varies by 15.9 m total across the 75
impacts inside 500 m. Nothing there is parallel to anything. For contrast the 68.148W target measures
**+6.36 % ± 0.50** with a **-25 %** crossrange and an 89.5 m residual.

So the two targets differ by a factor of about 25 in downrange gradient, and **the history stands**:
the pre-08-25 nights are measuring guidance. What made 08-25 special was the target, not the method.

One caveat the pooled number hides: a single night whose impacts fall inside a few hundred metres can
still land on a local feature. `2026-08-23-1304` fits **+6.99 %** over a 441 m span and flags as
ill-conditioned, where the regional slope through the same point is under 1%. A night's own footprint
is what conditions that night, which is why `shot-report.py --terrain` measures a night's own
impacts rather than a target once and for all. `docs/SHOT-PROTOCOL.md` has how to read it.

### The root cause: freezing the answer at the moment the question changes

`IcbmProgram` commits an arrival time once the aim goes steady, and `IcbmComputer` freezes the aim
on that same commitment — "one problem solved in two halves". But **committing the arrival is what
changes the trajectory**, so the banked aim is answering a question that is being replaced in the
same frame. Flown 2026-08-24 at 2,433 km, verbose:

```
bias  0.0 km  miss 37.1 km      converging
bias  9.3 km  miss 25.9 km
bias 30.8 km  miss  2.0 km      <- best banked here
bias 30.8 km  miss  1.2 km      <- frozen, and never moves again
bias 30.8 km  miss  2.9 / 3.3 / 3.6 / 3.8 km
```

Three cycles into a 35-second burn. **The trigger is perverse:** `AimCorrection.IsSteady` tests the
size of the last *step*, and a loop closing on its answer takes smaller steps because it is nearly
done — so converging well is what commits the arrival and invalidates the answer.

That also explains item 7f's result, which made no sense on its own: latching *later* measured worse
at every value swept (415 m of group bias at `SteadyMetres` 2,000 against 2,568 at 25). Waiting
longer means converging harder against a trajectory about to be discarded. **The latch timing was
never the problem; freezing the aim on it is.**

### Why the obvious fix cannot ship alone

`MirvBudgetTests.WhereTheNeverFrozenLoopGoes` traces both wirings at 7,645 km:

| | last reading | group |
| --- | --- | --- |
| as shipped | 0.98 km | **1.00 km** |
| never frozen | 0.00 km for 15 s, then 2.00 | **16.59 km** |

The frozen loop's instrument agrees with its outcome. The never-frozen one drives its own predicted
miss to zero and lands sixteen kilometres out — it converges *something*, and with the shipped
predictor that something is not where warheads go.

**So the freeze is load-bearing at long range, by accident**: it stops the loop before it can
converge onto its instrument's error. This is item 9's "a correction loop can only remove what its
observer can see" arriving a third time, and it is why `AimConvergenceTests` reads never-freezing as
a win (1.15 -> 0.56 km) — that rig scores *through the predictor the loop converges*, so it cannot
see the gap by construction. `MirvBudgetTests` scores the group independently and does.

Corollary worth keeping: **the 0.01 km never-freezing reads at 2,433 km is taken through the same
suspect instrument.** Discount it.

### The night

Four arms, because the interaction is the finding rather than a nuisance. Flown at **the operator's
own target**, not the scripted one, because 2,433 km is the geometry with a known failure — the
correction there makes the shot *worse* than not correcting at all (uncorrected 3.92 km, as shipped
4.23, never frozen 0.01; `AimConvergenceTests.TheSameFaultAtEveryRange`).

```bash
KSARMORY_SCENARIO_SAVE="AUTO NUKE DECOUPLER" \
./tools/shot-batch.sh --aim 26.485S,68.148W --blocks 12 --out ~/shots/2026-08-25 \
    --arms base=dev,ownint=arm/ownint,neverfreeze=arm/neverfreeze,ownint+neverfreeze=arm/ownint+neverfreeze

./tools/shot-batch.sh --resume ~/shots/2026-08-25        # if it was interrupted

./tools/shot-report.py ~/shots/2026-08-25
./tools/shot-report.py ~/shots/2026-08-25 --main ownint
./tools/shot-report.py ~/shots/2026-08-25 --main neverfreeze
./tools/shot-report.py ~/shots/2026-08-25 --shots
```

**The batch gate was re-sized for this target before the night flew.** Its wild-shot floor was a
flat 4 km, which is the widest baseline ever recorded on the *scripted* 26.5S,64.0W shot; here the
shipped build lands 4-6 km, and the baseline is never a candidate for dropping — so the floor would
have removed every arm that merely *matched* the control. It is now twice the night's own baseline
median once the baseline has two shots. `SHOT-PROTOCOL.md` has the rule.

| arm | what it changes | predicted |
| --- | --- | --- |
| `base` | dev | the control |
| `ownint` | `ImpactPredictor` flies the warhead's own integrator (item 2h) | modest — honest instrument, aim still freezes |
| `neverfreeze` | the aim keeps solving past the arrival latch | **possibly much worse** — converges onto a lying instrument |
| `ownint+neverfreeze` | both | the point of the night |

An arm expected to lose is deliberate: if `neverfreeze` alone is bad and the pair is good, the
mechanism is confirmed rather than inferred.

### Reading it in the morning

| outcome | what it means |
| --- | --- |
| pair wins, `neverfreeze` alone loses | **the mechanism is right.** Merge both to `dev` together, and never `neverfreeze` alone |
| pair wins, `neverfreeze` alone also wins | the instrument matters less than the trace says; merge both, and re-check 7,645 km headlessly before trusting long shots |
| pair does not win | the story in this section is wrong. Do **not** tune the freeze — go back to why the loop's prediction and its group disagree |
| everything UNRESOLVED | read the interval as what the night ruled out, and check the attribution table: `probe km` against the flown miss says whether the aim or the round is left |

### Do not

- **Do not merge any arm into `dev` before the batch finishes.** `base=dev`, so that replaces the
  control with the treatment and the night measures nothing.
- **Do not give the bus more RCS, and do not raise `BusTrim.MaxMetresPerSecond`.** The 2026-08-24
  flight spent ~3 m/s of a 40 m/s budget and then *refused* a 10.75 m/s request as "more than a
  separation could have cost". The guard was right: the aim had walked 7.6 km off and flying to it
  would have been worse. Raising it removes the only thing that noticed.
- **Do not tighten `AimCorrection.SteadyMetres`.** Measured worse at every value.

### Two defects found on the way and not fixed

- **The clearance latch — built, flown, and reverted.** It drove a bus into its own spent stack.
  The premise is circular and the gate re-shutting is protective; see *The clearance latch put the bus
  into the booster* below. The underlying defect — `trim.Done` nulling the reference, so later passes
  measure nothing — is real and still open, and the fix is to keep the reference rather than to
  remember the answer.
- **Nothing sizes a correction against the actuator.** `AimCorrection` steps as though moving the aim
  moves the impact one-for-one; during the coast the only actuator is a trim with a 10 m/s ceiling
  and a few m/s of budget. A full-error step after a bad burn asked for 10.75 m/s and was vetoed,
  leaving the correction inert for ten seconds. Likely moot if this item lands — the 3.8 km never
  reaches the coast — so keep it as a guard rather than building it first.

### Parked arms, built and pushed

`arm/aim` (item 7f, the stopping-rule constants), `arm/substep` (the Mk 21 at 1 ms — the rig argues
against it), `arm/subground` (per-sub-step ground, ~20 m), `arm/arr15` (the arrival floor, and
`SHOT-PROTOCOL.md` says it wants its own night).

## 7d. What a flight can actually resolve

Two things were being measured that belonged to the harness rather than to the mod.

**The save is resumed mid-flight**, so the vehicle kept moving while the scenario found it, aimed it
and armed it — and the state it was picked up in depended on how many frames that took. The same
save was picked up at 415 s of flight on one build and 450 s on another, and the second is a
worse-conditioned arc worth 164 km. Setting the shot up at `BallisticScenario.SetupSpeed` (0.01x)
pins it: every run since reports the identical pick-up, 207 km doing 7362 m/s.

**What is left is about 0.5 km**, run to run, on an identical pick-up — frame pacing during the
flight. So a single run each way cannot resolve anything under about a kilometre, and several of the
numbers recorded above were taken before this was known. Where a claim is inside that band it now
says so.

## 7e. That 0.5 km is one latched warp decision, not frame pacing — measured, fixed headlessly

> **The Mk 21's preferred step is now 180 ms, under the ~188 ms the coast runs at, so the hold
> engages on every frame and the 188 ms branch does not occur anywhere in the traced frame band.
> Measured through the rig only — **this has not been flown**, and the mechanism below is what says
> it should help rather than a shot that did. `CoastStepDeterminismTests` pins the engagement.

> **Flown 2026-08-24, and it is closed — by the harness rather than by the constant.** Five shots,
> `arm/coast225` (0.225) against `dev` (0.180), stopped once the logs said what they said:
>
> ```
> arm     walk m   early s   coast ms      ratio      interval
> base      2806      0.58        nan          —             —
> coast     2793      0.59        nan       1.00     0.97-1.03
> ```
>
> `coast ms` is `nan` because **there are no warped frames to take a median over**. Every trace
> sample in both arms reads `sim=1.00x` — 8,070 of them on one and 8,093 on the other — and neither
> log carries a single warp line. `WarpPolicy` acts only once the step *exceeds* `PreferredStep`, and
> a 17 ms frame is inside both 0.180 and 0.225, so the constant under test is never reached. **The
> two arms ship different assemblies that cannot behave differently**, which is what a walk agreeing
> to 13 m and an interval of 0.97-1.03 at n=5 are saying.
>
> The cause is this repository's own harness fix `5b15830`: the pre-release coast is warped at 100x
> and the world is **handed back before the first warhead leaves**, so the warheads fly their whole
> 372 s at 1x. Everything below was measured when they flew that coast under 8x. The lottery is not
> fixed — it is *unreachable*, which is a different thing and a fragile one: anything that puts warp
> back over a salvo brings all of it back.
>
> **So `ReentryVehicleMk21.PreferredStepSeconds` currently has no effect on a flown shot at either
> value**, and `CoastStepDeterminismTests` pins a headless behaviour the flown configuration never
> enters. That is worth knowing before the next person reads the 0.180 in `Arsenal.cs` as load-
> bearing. The five shots are kept at `~/shots/2026-08-24-coast-void`.

**The night that settles it**, flying since 2026-08-24 01:00. Both arms are commits, so nothing in
the working tree reaches a shot and the tree can be worked on while it flies:

```bash
KSARMORY_SCENARIO_SAVE="AUTO NUKE DECOUPLER" ./tools/shot-batch.sh \
    --aim 26.5S,64.0W --arms base=arm/coast225,coast=dev --blocks 16 \
    --out ~/shots/2026-08-24-coast
./tools/shot-report.py ~/shots/2026-08-24-coast          # in the morning
```

**Not the arms this section first named.** `base=4e8be19,coast=23422a3` are the commit before the
change and the change, and they cannot fly this save at all: both predate the staging fix, so a
stack carrying a decoupler under its bus never lights its second stage. Two batches launched against
them on 2026-08-23 recorded **zero shots**. `arm/coast225` is today's `dev` with
`PreferredStepSeconds` put back to 0.225, which asks the same question of the build that ships.

Sixteen an arm settles a factor of about 0.55; a smaller effect comes back `UNRESOLVED` with an
interval saying what was ruled out, and `--resume <dir>` carries the same batch on for more. Thirty
two shots at about nine and a half minutes each — the warheads are held until the arrival is close
now, so a shot is most of a coast — is a bit over five hours, and it holds the game for all of it.

**The setup is checked, and checking it was not a formality**: the first two attempts at this night
flew a build whose warheads never left the bus (item 0c) and the third scored a group of sixty
(item 0d). The shot it starts from now reads `PASS 6 of 6 arrived; worst 2.93 km, best 2.92 km,
mean 2.92 km, spread 0.01 km`. **A single shot settles nothing** against a scatter of ×1.74, which
is what the batch is for — but a single shot settles whether there is anything to measure.

**The coast's integration step is bimodal, and which mode a shot gets is decided by the length of a
single frame.** Conditioned on it, the run-to-run scatter in the walk falls from a standard
deviation of 91-1,155 m to a residual of **10-88 m**. Nothing else in the shot is doing anything.

`Slug.FaithfulStepSeconds` off the coast is `MunitionProfile.PreferredStep`, **225 ms** on the Mk 21,
and `WarpPolicy.Decide` returns `Nothing` while the step is inside it. At the scenario's 8x that
threshold is a wall-clock frame of **28.125 ms**, against a coast that runs at a median of
**23.1-24.5 ms**. So the mod does not hold the world down at all unless one frame lands in the top
of that band — and when one does, `_restoreTo` is set and nothing lifts it while a round is in the
air, so the whole rest of the coast runs at the speed computed from that one anomalous frame.

| | coast step | walk from the release probe |
| --- | --- | --- |
| never tripped, 8x throughout | **185-190 ms** | arr15 **+875 / +845 m**, com **+1,283 / +1,281 m** |
| tripped, 4.0-4.8x thereafter | **95-120 ms** | arr15 **+107 / +102 m**, com **-1,028 to -1,603 m** |

**The threshold separates 38 flown shots with no exceptions.** Taking the first full 8x frame after
the salvo, every shot above 225.0 ms of step had the world held down on it and every shot below did
not; the nearest pair either side is **223.5 ms and 225.4 ms**. The held speed is
`8 x 0.6 x 0.225 / dtSim` to the printed digit — 4.05x off a 266.7 ms frame, 4.79x off a 225.4 ms
one — so the whole flight's step is calibrated off the one outlier that tripped it, and varies 20%
for that reason alone.

**The walk is linear in the mean coast step**, at the arrival angle's own leverage:

| arm | arrival | m of walk per ms of coast step | R² on step alone | residual |
| --- | --- | --- | --- | --- |
| base / com / thr | 7.1° | **53.4 / 36.1 / 17.1** | 0.94 / 0.99 / 0.53 | 74 / 88 / 62 m |
| arr15 | 15.2° | **13.9** | 1.00 | 13 m |
| arr20 | 20.3° | **9.1** | 0.99 | 19 m |

Adding flight time as a second term takes every arm to **R² 0.98-0.99** with residuals of 10-88 m.
The ratio between the arms is `cot γ` to within 20%, which is what says it is one term seen through
three arrival geometries rather than three effects.

**It is worse than a nuisance, because it is correlated with the arm.** The release frame at
152-195 km hits KSA's own frame cap — `dt` 266.7 ms is exactly 8 x 1/30 s, and the p99 wall frame in
every flight is 33.3 ms — so a low release trips **every time**; at 282-340 km the same frame is
177-224 ms and trips **once in eight**. An arm that releases higher therefore flies a systematically
coarser coast, and half its measured effect is that rather than its guidance.

**And the instrument was pointing at it and could not see it.** `shot-report.py` medians `dt` over
every sampled frame, and the 1x entry supplies ~90% of them: 24.4 ms against 23.7 ms for two shots
whose *coast* medians are 185.4 ms and 101.9 ms. The number to condition on is the median step over
frames with `sim > 1`, which is already in the logs.

`WarpLatchScatterTests` is the headless reproduction: the same release state, one frame moved from
27.94 ms to 28.18 ms, two landing points. It reads **313 m** where the flight reads 773-2,564 —
the standing rig-versus-flight factor from item 2, so the mechanism reproduces and the size does
not.

### A second discrete event, rarer and unexplained

One shot in 38 shows the *other* shape the trace was built to tell apart. A single frame at 8x came
in at **56 ms** — 0.45 s of step, past `MunitionProfile.MaxFaithfulStepSeconds` — so the clamp
discarded **138.9 ms**, which `lag` then reported for the rest of the flight. On that one frame the
walk went from **-37 m to +11,446 m** and the round's own predicted arrival jumped **+2.09 s**;
everything after it is flat. It is a *step*, not a ramp, so it is not the latch and not the
integrator.

The arithmetic does not explain it. Gravity frozen across one 0.32 s step at 371 km is worth under a
metre of impact, and 138.9 ms of discarded flight is 0.14 s of arrival, not 2.09. It happened on the
2354 batch, whose base is the `arm/clearance` merge that was reverted, so that batch is excluded from
every figure above — but the event is the clamp's and would reach any build. **Worth its own look**;
until then a shot logging non-zero `lag` should be dropped rather than scored.

### Ranked, cheapest first

1. ~~**Condition on the coast step in `shot-report.py`**~~ — **done.** The median `step` over
   warped frames is the `coast ms` column, per arm rather than per shot, because it correlates with
   the arm and is a covariate rather than noise. It says immediately whether an arm is being scored
   on its release altitude: on the arrival-floor batch the floored arms coasted at 152 and 144 ms
   against the baseline's 97.
2. **Make the trip deterministic.** Any `PreferredStep` below `8 x` the median frame — under about
   **190 ms** — trips on the first full frame of every shot, which removes the branch without
   touching the policy. It does not remove the 20% calibration spread, because the target is still
   computed from whichever frame tripped it.
3. **Stop calibrating a whole flight off one frame.** `Decide` freezes the requested speed at the
   first overrun and never revisits it while the air is busy. Re-solving it against the step the
   world settled at would put every shot on `Margin x PreferredStep` regardless of what tripped it.
   This is a control loop with three flown lessons against it — `SettleSteps`, `OverridesBeforeYielding`
   and the abandon guard are all scar tissue — so it is a change to make deliberately or not at all.
4. **Hold the world unconditionally while rounds fly**, dropping the `dtSim <= faithfulStep` early
   return and letting the margin decide. Every coast then runs at `0.6 x PreferredStep` = 135 ms.
   Deterministic, and it takes timewarp away from a player who was inside the limit — which the
   early return exists to avoid.

**What would falsify it.** Fly a batch with `PreferredStepSeconds` at 0.19 and check that every shot
logs `timewarp held at` within the first second: if the trip becomes universal and the walk's
spread within an arm collapses toward the 10-88 m residual, this is the whole of it. If the spread
survives, something else is in the coast step.

## 8. The bus corrects its own aim after cutoff — flown

The correction has always been able to move the aim during the coast. What it had no way to do was
*act* on it: the warheads coast along whatever arc the bus is already on, so a bias moved after the
engines stop changes the readout and nothing else. Item 2b said as much — "it simply had no lever
left, because the correction can only act through an arc nothing is still burning."

The trim is that lever. It nulls to 0.017 m/s, measured in flight, which is enough to fly any arc
worth asking for. So `Sim/PostBoostAim.cs` sequences the two:

1. the trim nulls onto the arc the burn solved,
2. with the thrusters quiet, one measurement is taken and the aim moves,
3. `IcbmProgram.CorrectCoastArc` re-solves the transfer from where the bus *now is* to the corrected
   point, at the arrival the burn committed to,
4. the trim nulls onto that instead, and it repeats.

**They alternate rather than running together**, and that is the whole reason they can coexist. The
correction's only observer is a prediction flown from the vehicle's own state, so a measurement taken
while the thrusters fire reads the trim's own displacement as error and burns harder at it — the
runaway that produced 139 m/s of commanded trim. Waiting for the trim to settle removes the
interaction rather than damping it.

**The stopping rule is a payback, not a count.** Holding a warhead costs ~26 m of miss per second
(item 2b), so a pass has to remove more than the seconds it spends are worth. A shot already inside
that is one correcting makes worse.

Flown at 3,459 km: four passes, predicted miss 2.9 -> 2.9 -> 2.1 -> 1.2 km, then release.

**Three things stop it now, not one.** The payback rule alone never fired: at ~2 s cycles it is a
~52 m bar, and a miss wandering at 100-400 m clears it every time. Item 8b has the rest.

**What it does not fix** is anything the prediction cannot see. It flies the *bus*, so the six tubes'
6-degree ejection cone is invisible to it — 1.9 km of spread flown. The 2.6 km that figure was
measured against belongs to an idealised arc rather than to the one the guidance leaves the bus on;
item 9 has what the cant is worth on each.


## 8b. The correction was reading a moving instrument — apportioned, fixed, flown

> **Flown, and it does all three things it was built for.** Three shots at 3,459 km against three
> without it:
>
> | | without | with |
> | --- | --- | --- |
> | group miss | 0.45 / 0.59 / **6.85** km | 0.63 / 0.70 / **1.18** km |
> | frames with thrusters firing | 1,943 | **959** |
> | correction passes | 12 | **4** |
>
> The best case is slightly worse and the **worst case is 5.8x better**, which is the trade: a
> reading taken off a turning nose is what put the 6.85 there, and refusing those costs a little
> convergence. The passes now read 2.0, 2.0, 1.0, 0.3 km and stop — monotonic, where before they
> converged by pass 5 and then wandered inside 0.1-0.5 km for seven more. Half the propellant, and
> the tank is no longer the thing at risk.

**Flown symptom.** With the aim frozen at a constant 102.2 km of bias, the logged predicted miss
swung smoothly from 3.2 km up to 14.0 km and back down to 2.2 km. The correction did nothing during
that excursion. Two candidates, and a live log cannot separate them: the trim firing one direction
at a time, which deliberately leaves the bus on a wrong trajectory mid-sequence; or the bus's nose
drifting, because `Predict` adds `ReleaseImpulseCci()` — the modelled ejection kick, then 2 m/s —
along that nose before flying the prediction.

**It is the nose, by a factor of 126.** `PostBoostObserverTests` sweeps both terms on the guided
cutoff state at 3,459 km, with the aim converged to a 30.5 km bias and a baseline predicted miss of
0.72 km:

| what moved | predicted miss |
| --- | --- |
| nothing — kick on the nose | 0.72 km |
| nose turned 11.06° (half the band as then measured) | 0.88 / 1.08 km |
| nose turned 22.11° (the whole of it) | 1.33 / 1.72 km |
| **nose turned 95°** (low end of the flown throw band) | **9.40 / 10.50 km** |
| **nose turned 119°** (high end) | **12.57 / 13.54 km** |
| nose turned 180° | 16.68 km |
| bus 0.02 m/s off its arc (`BusTrim.SettledMetresPerSecond`) | 0.78 km |
| bus 0.05 m/s off | 0.89 km |
| bus 1.10 m/s off (a whole decoupler shove, un-nulled) | 4.51 km |

The 95-119° row is item 5's own evidence read back: the release probe reports the salvo thrown 95,
116 and 119 degrees from the platform's track on three otherwise identical runs, because the
separated bus then had a 22.11° pointing band, `roll Decoupled` and no elected control part — the
band is now 0.31°, and the other two are not. On this arc that band of *directions* is 8.7-12.8 km
of predicted miss with nothing about the shot having changed — which brackets the flown 14.0 km
peak.

**The trim cannot reach it, and never could.** At the readings the sequencer actually admits — it
already waits for `_trim.Done` — the residual is 0.02 m/s, worth **70 m**. Even at its worst, a
whole 1.1 m/s separation shove with nothing taken out yet, it is 3.79 km, and that is a state no
reading is ever taken in. The gate that mattered was never on the thrusters.

### The gate

`PostBoostAim` now watches the release direction itself, handed in as a `double3` so the
differencing happens in `Sim/` where a test can reach it and can be checked for invariance under a
rotation of both samples.

**Steady means the direction has stayed inside 2° of one anchor for 2 s.** An anchor rather than a
per-frame rate: a per-frame turn is an angle between two samples a frame apart, and KSA's step beats
8.33/25.0 ms on a 120 Hz display — a rate test rejects a bus holding perfectly still and accepts one
drifting slowly enough to stay under it for ever. The 2° is priced: the predicted impact moves
**14-22 m per degree** of nose near the boresight on this arc, so a reading admitted by the gate
carries at most 44 m — inside the 250 m (`AimCorrection.ImprovedByMetres`) a pass is judged by.

**And it gives up rather than waiting the tumble out.** `SettlesWithinSeconds` is 10 s, worth 260 m
of leverage at item 2b's 26 m/s. Holding out for `MaxSeconds` instead costs 3,120 m, and on the
flown bus there is nothing at the end of the wait to collect: its attitude is not tracked at all.
So the cost of the gate is **260 m in the bad case and nothing in the good one** — a bus whose nose
is held settles inside one pass.

### Two defects in `PostBoostAim` itself, both fixed

- **No best-tracking.** It stopped on a payback rule that at ~2 s cycles is a ~52 m bar, which a
  miss jittering at 100-400 m always clears. Flown, it converged by pass 5 (3.3 -> 0.4 km) and then
  oscillated 0.1-0.5 km for seven more. It now counts **failures to improve on the best**, which is
  the whole difference from `AimCorrection.WorseBeforeStopping`: that counts passes strictly *worse*
  than the best, and a reading oscillating inside the 250 m band is never worse by enough, so it
  never trips. Three failures in a row stops it — pass 8 rather than 12 on that flight, worth 208 m
  of leverage and a third of the propellant.

  `WorseBeforeStopping` is untouched at 12; item 7c is why, and it is load-bearing at 7,645 km.

  **The best is a stopping rule and not an aim that gets restored.** A bias only reaches the shot
  through an arc the trim then has to fly, so reverting to an earlier one costs a whole further
  pass. That is a different trade and is not made — and it should not be made off readings taken
  before the settle gate existed.

- **No propellant budget.** Measured in flight: **1,943 frames with thrusters firing against 24
  settled**, roughly 36 m/s of Delta-v on a bus carrying about 70-90. `BusTrim.SpentMetresPerSecond`
  now accumulates the measured acceleration across every null — surviving `Resume()`, because a tank
  does not refill with a fresh reference — and `PostBoostAim.MaxTrimMetresPerSecond` stops the
  passes at 40 m/s. That leaves 30 m/s on the smallest bus in the range: three nulls at the largest
  trim `BusTrim.MaxMetresPerSecond` will accept, or twenty-seven separation shoves. A bus that
  arrives dry cannot null the shove, and that is 3.8 km.

  Forty is above what a converged correction spends, so it is the backstop against a loop that will
  not stop rather than the thing that stops one — the best-tracking rule is what cuts the 36 m/s.

**Unflown.** All of it is headless. What a flight would show is whether the settle gate ever opens
on a separated bus at all: if it does not, every shot releases 10 s after the trim finishes with the
aim the burn earned, which is the honest outcome and is 260 m worse than a bus that holds still.


## 8c. The trim's refusal was ending the correction and calling it convergence — flown 2026-08-25

**A 5.2 km miss on a shot whose burn was perfect.** Cut off **0.42 m/s** short with a worst frame of
33 ms — the boost has nothing left to give. The miss was in the aim, and it was on the readout the
whole time: 4.2 km predicted at cutoff, 5.9 km at release, 5.2 km flown. The mod called it and let
go anyway.

The post-boost correction ran **one pass**, and this is the whole of it:

```
17:20:16.191  post-boost: correcting the aim, 5.9 km out (pass 1)
17:20:16.210  more than a separation could have cost, 14.91 m/s left on the bus
17:20:16.704  post-boost: aim settled 5.9 km out
17:20:16.706  deploying: releasing tube 1
```

515 milliseconds from first pass to release, and **the aim never settled**. `PostBoostSituation`
carried one flag for two facts — `AimHasSettled: _aim.Settled || _trim.GaveUp` — so the sequencer
read the trim's refusal as the correction having converged, printed a sentence with no true half in
it, and released.

The refusal itself is the guard working as designed. `BusTrim.MaxMetresPerSecond` is 10 because the
aim correction and the trim drove one vehicle through one prediction and wound each other up tenfold
every ten cycles; 14.91 m/s for a 5.9 km aim step is the shape it exists to catch. **What was wrong
is what happened next**, not the refusal.

Split into `AimHasSettled` and `TrimGaveUp`, with their own messages, and
`ATrimThatRefusesTheArcIsNotAnAimThatSettled` pins them apart. That is a diagnostic fix and it is
all that has been made: the flight now says *the trim would not fly the correction, so none of it
was applied*, which is a fact somebody can act on where "aim settled" is one nobody would look at
twice.

**What it does not do is correct the shot**, and the open question is which of these the 14.91 m/s
is:

- **a bad solve.** The trim solves to the committed arrival while the flown prediction disagrees by
  ~2 s (`[solving to an arrival 406 s away; the flown prediction says 408 s]`). Demanding an arrival
  two seconds early over a 406 s fall is worth metres a second on its own, and buys nothing about
  the miss. The gap is expected — one is a vacuum arc reaching a point, the other a warhead with
  drag reaching the ground — but whether the *trim* should be solving to the first is not settled.
- **a real correction the ceiling is too tight for.** `PostBoostAim.MaxTrimMetresPerSecond` is 40
  across all passes and the bus has the propellant, so 14.91 m/s in one go is affordable if it is
  genuine.

The two want opposite fixes and the log cannot currently tell them apart. **Next: log what the trim
solves against beside what the correction asked for, on the frame it refuses** — the same shape as
8b, where the correction turned out to be reading a moving instrument rather than being wrong.

## 8d. The correction ran end to end — flown 2026-08-25, 756 m

**Four shots at Mahia (39.262S 177.865E), 2,942 km, one evening.** Nothing about the guidance
changed between them except which limit was allowed to stop the post-boost correction.

| flown | released at | landed | what stopped the correction |
| --- | --- | --- | --- |
| 17:20 | 5.9 km | 5.2 km | the trim's refusal, read as a settled aim — 1 pass |
| 18:18 | 4.8 km | 3.6 km | same, 1 pass |
| 19:11 | 1.9 km | 2.6 km | `TrimBudgetMetresPerSecond`, 0.45 m/s short — 2 passes |
| **19:57** | **0.015 km** | **0.756 km** | **the payback rule, as designed — 4 passes** |

The last run in full:

```
19:57:02  correcting the aim, 1.6 km out (pass 1)      cycle 10 s
19:57:12  correcting the aim, 1.4 km out (pass 2)      cycle  6 s
19:57:18  correcting the aim, 0.7 km out (pass 3)
19:57:24  15 m out, under the 159 m another correction would cost
```

**Cycles shorten as the corrections do, and that is what beat the stride.** A 26-second pass at
3.5 km became a 6-second pass at 0.7, and since the stopping threshold is
`cycle x HoldingCostsMetresPerSecond` it fell from 676 m to 159 with them. The prediction that this
would stall in the 1–2 km band was wrong for that reason.

**The budget was the whole of it**, and it paid twice. Raised from a literal 25 to the derived 40,
the separation null had enough to finish — so the correction *started* at 1.6 km rather than 3.5 —
and then had enough to run four passes instead of two. Both halves came out of one number nobody
had derived.

### What is left is the predictor, and it is a bias rather than noise

Released at 15 m, landed at 756. The guidance has converged below what the predictor can resolve, so
every metre of the residue is now the predictor's. And it repeats:

| flown | miss | bearing |
| --- | --- | --- |
| 19:11 | 2.6 km | north-east |
| 19:57 | 0.756 km | **north-east** |

Same bearing twice, and the six warheads land inside **2 m** of each other — so this is one
systematic offset applied six times, not scatter. At the ~6 degree arrival these shots fly,
`cot gamma` is about ten, so 756 m of ground is roughly **75 m of height** in the prediction.

### Found: it is the round's own integrator, and the field for it was already there

`ProbeGapTests` had already decomposed it. The round lands **149 m** from its own probe on flat
ground, and of that:

| term | worth |
| --- | --- |
| the ground held for a whole frame | 0 m |
| the air's motion held for a whole frame | −2 m |
| **symplectic Euler at 5 ms** | **143 m** |
| unaccounted for | −8 m |

And `WhatTheGroundsOwnSlopeMultipliesTheGapBy` prices the rest: 149 m on the flat becomes 255 m at
2% downhill, 376 at 8%, **571 at 10%** — 3.84×. A shallow arrival over sloping ground is what turns
143 m of integrator into the 756 m that landed.

**`MunitionProfile.SubStepSeconds` already existed**, with the measurement written beside it — 30.6 m
per millisecond, 145.3 / 68.8 / 22.9 / 7.6 m at 5.00 / 2.50 / 1.00 / 0.50 — and the cost reasoning
for why a warhead may have it and a cannon shell may not. **No profile in `Arsenal.cs` ever set it.**
The Mk 21 now asks for 1 ms, which takes its integrator term from 145 m to 23.

`MaxSubSteps` scales with it so the round's faithful step does not move, and `WarpPolicy` is
untouched — `AFinerStepDoesNotMoveTheRoundsFaithfulStep` holds that for every munition, and
`TheReentryVehicleAsksForAFinerStepAndNothingElseDoes` holds the cost boundary.

**Unflown.** Expect the integrator to stop being the largest term rather than the miss to divide by
six: 143 → 23 m of the flat-ground gap, times whatever the slope at the arrival is worth. The
arrival-angle floor is the next lever and is worth spending reach on now that nothing of comparable
size hides behind it.

## 8e. Three ways the correction is discarded, and only one was known

The post-boost correction is abandoned wholesale by `_trimAbandoned`, and the same flag is set from
paths with nothing in common. All three fired within one evening on the same craft:

| what stopped it | seen | what it cost |
| --- | --- | --- |
| `MaxMetresPerSecond` — the solve is larger than a separation kick | 17:20, 18:18 | 1 pass, released 5.9 / 4.8 km out |
| `TrimBudgetMetresPerSecond` — the budget is spent | 19:11 | 2 passes, released 1.9 km out |
| **the separation clearance** — the bus never got 15 m from its stack | **6 of 6**, see below | **0 passes applied, released on the uncorrected arc** |

**The clearance costs the most when it fires**, because it discards a correction that was solved and
affordable. Flown back to back on one craft, one target and one build:

| | correction | landed |
| --- | --- | --- |
| hand-flown | 4 passes applied | **0.756 km** |
| harness | discarded at the clearance | **2.68 km** |

That is what the correction is worth on this shot, measured rather than argued.

**And the clearance path is a designed trade, not a defect.** `Sim/SeparationClearance.cs` already
has the whole of it: the decoupler's shove *is* the separation velocity, the trim's job is to null
the velocity difference, so **the trim stops the separation**. Clearance is deliberately never
latched for exactly that reason — "a latch drove a bus into its own spent stack", flown the same
day — and `TimeoutSeconds` is deliberately short because waiting costs accuracy of its own: a
ninety-second hold on a shot whose cutoff prediction was 0.1 km put the release probe 6.8 km out.

The gap reading `2 m`, then `15 m of 15`, then `still 6 m after 20 s` is that mechanism working, not
failing. Trimming closed the gap it had opened.

**The frequency question is now answered, and the answer is every shot.** Six shots of the shipped
build against the save's own defended site, `~/shots/2026-08-25-2026`: the clearance timed out and
released untrimmed in **all six**, with the gap standing at 4, 7, 7, 8, 8 and 10 m against the 15 m
it wants. So it is the usual outcome rather than a rare bad draw, and the hand-flown run that
cleared is the exception.

What those six do **not** settle is what a denied pass costs. Five of them got one correction pass
away before the timeout and one got none, and that one landed worst — 4.99 km against 0.46, 1.34,
2.00, 3.26 and 3.67 — but a single zero-pass draw is an anecdote, not a measurement. The passes
counted *after* the release are worth nothing at all; see the section below.

So the trade was changed, in `d4623ae`. The timeout's job is that a stuck bus does not hold the
salvo for ever, and that stands; the safety half of it did not have to be answered by refusing to
trim, because `TrimSituation.KeepOutTowardCci` is computed in the same pass as the clearance and is
the precise form of the same question. The timeout now lifts the wait and leaves the interlock
withholding only the directions that point at the stack. At 4–10 m of a 15 m keep-out there are
directions left to spend, which is why this is not the blunt version by another name.
**Flown, and it lost — see item 8h.** The mechanism works; what it uncovered underneath is a
correction loop that diverges, which the timeout had been cutting off after one pass.

### The trim goes on firing after the salvo is away

Same run, after `0 left`: it nulled 8.24 m/s, reached 0.020, then asked for 19.26 more. **Eight metres
a second of a forty-metre budget spent on a bus with nothing left to deliver**, and spent
manoeuvring six metres from the spent stack — which is the manoeuvre the clearance had just refused
on safety grounds. The abandon flag stops the trim before the release and not after.

The gate wants a **monotonic** "the salvo is finished", and the obvious one is a trap:
`TubesReadyToFire` comes back a few seconds after the salvo because the magazine reloads, which is
exactly what left the coast warp dead for weeks (`7d72f24`). `IcbmComputer.WarheadsAway` only ever
increases and is the right input.

### And corrections scale with range

19.26 m/s wanted on the 12,902 km shot against 8–15 on the 2,942 km ones, against a ceiling of 10.
The further the shot, the more often `MaxMetresPerSecond` is the thing that stops it — so whatever
replaces that ceiling has to be a function of the trajectory rather than a constant.

## 8f. The predicted miss does not track the flown one — batch, 2026-08-25 evening

Six shots of the shipped build against the save's own defended site, 12,902 km from the pad,
`~/shots/2026-08-25-2026`. The first three, with what the release predicted beside what landed:

| shot | passes | predicted at release | landed | what stopped the correction |
| --- | --- | --- | --- | --- |
| hand-flown | 4 | 0.015 km | 0.756 km | payback rule |
| harness | 0 | — | 2.68 km | separation clearance |
| 001 | 4 | **4.5 km** | **0.46 km** | `MaxMetresPerSecond`, 13.39 m/s |
| 002 | 1 | 6.3 km | 4.99 km | `MaxMetresPerSecond`, 12.83 m/s |
| 003 | 0 | — | 1.34 km | separation clearance |

**There is no relationship between the two middle columns.** One shot released expecting 15 m and
landed 756; another expected 4.5 km and landed 460 m. Every pass, every stopping rule and every
budget decision in the correction loop is driven by that number.

And the loop **diverges** where it runs: shot 001 read 2.1 km at pass 2, 6.0 at pass 3, 4.5 at pass 4.
`AimCorrection` keeps a best bias and reverts to it on `Freeze`, but the post-boost path stops on the
trim's refusal rather than a freeze — so a diverging run may release on the *worst* bias it found.
Worth checking directly; it is a few lines either way.

### The warhead trace says where it goes wrong, and it is the last kilometre

Shot 003, round 1, from `WarheadTrace`:

```
landed at -39.2714,177.8570 | 1304 m from the aim
walk from the release probe 2464 m (-2357 down, +719 cross)
flight 403.33s by the world clock, probe said 403.81s
```

At **0.86 s to go and 867 m up** the re-flown prediction still had it landing 2.4 km away, and the
round then arrived **0.48 s early** — 2.1 km of travel at 4,428 m/s, which is the downrange walk to
within noise. **It hit ground the prediction does not see.** That is not integrator drift over a
400-second flight; it is a surface disagreement in the final kilometre.

`docs/KSA-TERRAIN.md` is the file for it and already prices the sensitivity: at this six-to-seven
degree arrival **one metre of disagreement about the surface is 9.3 m of ground**. The 2,464 m walk
is about 265 m of height — larger than the 186 m worst case that file measures between the bicubic
and bilinear paths, so it is not only interpolation, but it is the right order and the right
mechanism.

**That was thought to be the next thing to chase**, ahead of the sub-step and ahead of the trim's
ceiling — **and it is not the surface: item 8i measures the two as +0.0 m apart on 24 shots.** The
walk is real and still the largest term; the mechanism named here is not.

## 8g. Six shots say the walk from the probe is the whole error — 2026-08-25 evening

`~/shots/2026-08-25-2026`, six shots, shipped build, the save's own defended site at 12,902 km.

```
0.46  1.34  2.00  3.26  3.67  4.99  km
median 2.63   mean 2.62   max/min 10.8x
```

All six passed the 5 km bar; one by ten metres. **This is not comparable to the 0.79 km baseline in
`SHOT-PROTOCOL.md`**, which was flown at 2,300–2,700 km — miss scales hard with range and this is
five times further.

### Three things this corrects

**The arrival is 13.7 degrees, not the six or seven assumed all evening.** `cot gamma` is 4.1, not
8–10, so every amplification argument made here tonight was about twice too pessimistic.

**The terrain is well conditioned.** `-0.10%` downrange slope against that arrival — `1.0x flat
ground`. The crest mechanism of item 7g is not in play at this target, and the north-east bias is not
a slope.

**And the integrator is not the story.** `ProbeGapTests` prices symplectic Euler at 149 m on flat
ground at 5 ms and about 23 at 1 ms. The measured walk is **2,352 m**. The sub-step was worth
fixing and explains a fifteenth of it.

### What the six shots agree on

Medians across all of them, from the attribution table:

| | |
| --- | --- |
| cutoff residual | **0.175 m/s** |
| walk from the release probe, downrange | **−2,352 m** |
| walk, cross-track | **+722 m** |
| arrives **early** by | **0.48 s** |

The burn is finished as a problem. The walk is not noise — six shots, same sign, same size, and the
0.48 s of early arrival is 2,227 m at 4,640 m/s, which is the downrange walk to within the scatter.

**So the round consistently stops about 2.35 km short of where its own probe says, on flat ground,
and it is not the integrator.** Every stopping rule and budget in the correction loop is driven by
that probe, which is why the predicted miss and the flown miss are unrelated (item 8f).

### And which limit stopped each shot

| shot | landed | stopped by |
| --- | --- | --- |
| 001 | 0.46 km | `MaxMetresPerSecond`, 13.39 m/s, after 4 passes |
| 002 | 4.99 km | `MaxMetresPerSecond`, 12.83 m/s, after 1 |
| 003 | 1.34 km | the trim's budget |
| 004 | 3.67 km | `MaxMetresPerSecond`, 12.11 m/s |
| 005 | 3.26 km | `MaxMetresPerSecond`, 11.51 m/s |
| 006 | 2.00 km | the trim's budget |

**Four of six stopped on the 10 m/s ceiling, all of them wanting 11.5–13.4.** At this range the
ceiling is the binding limit on every shot that gets that far, and it is a constant where the thing
it bounds scales with the trajectory.

**Next is the walk**, ahead of the ceiling and well ahead of anything else: a correction loop cannot
converge on an instrument that disagrees with the round by 2.35 km, and the ceiling only decides how
many wrong corrections get flown.

## 8h. The interlock lost, and what it lost to was the loop it let run — batch, 2026-08-26

`~/shots/2026-08-25-2214`, 24 shots, three arms interleaved against the save's own defended site at
12,902 km. `shipped` is `e0357a2`, `fixes` adds the trim ceiling and the best-aim keep, `keepout`
adds `d4623ae` — the clearance timeout handing its safety question to `KeepOutTowardCci`.

| arm | n | median | ratio | 97% interval | verdict |
| --- | --- | --- | --- | --- | --- |
| shipped | 10 | 3.04 km | — | — | baseline |
| fixes | 10 | 2.49 km | 0.88 | 0.54–2.45 | unresolved |
| **keepout** | **4** | **5.39 km** | **2.05** | 0.59–8.10 | **dropped by the gate** |

`keepout` never flew its last four: the batch's own gate struck it out after two failures and gave
its shots to the others. Four shots resolve nothing on their own, and the interval says so — but the
gate is the protocol, and the failure has a mechanism rather than a distribution.

**The change did exactly what it claimed.** One shot flown by hand beforehand confirmed the
mechanism: four completed correction passes before release against the one every shipped shot gets,
`releasing without trimming` gone, and — unasked for — zero passes wasted after the salvo, the
sub-section above's defect closed as a side effect. Every `keepout` shot in the batch shows the same.

**What it lost to is the correction loop diverging, which item 8f had already seen and this exposed.**
Two of the four shots, side by side:

| | 002 — 2.53 km | 008 — 8.26 km |
| --- | --- | --- |
| owed at the split | 1.33 m/s | 1.93 m/s |
| after pass 1 | 0.018 | 0.023 |
| pass 2 asks for | *converged* | **12.63** |
| after pass 2 | 0.027 | 0.026 |
| pass 3 asks for | *converged* | **15.61** |
| how it ended | budget spent, **0.58 m/s** left | over the 14 m/s ceiling, **15.61 m/s** left |

Each pass nulls its residual to a couple of hundredths. The *next* solve then demands an order of
magnitude more. Both 8 km shots end the same way — `more than the 14 m/s this pass may spend` — and
release with the whole of it outstanding.

**So the clearance timeout was load-bearing by accident.** Cutting the loop off after one pass is
what kept a diverging run from being flown, and removing it did not create the divergence so much as
stop hiding it. The two good `keepout` shots (2.14, 2.53 km) are the ones whose loop converged; both
beat the `shipped` median.

**The fix is the one 8f already named**, and it is still a few lines: `AimCorrection` keeps a best
bias and reverts to it on `Freeze`, but the post-boost path stops on the trim's *refusal* — so a run
that diverges releases on the worst state it found rather than the best one it banked. That is the
same lesson as `WorseBeforeStopping`, one actuator further down.

**Nothing else moved.** The terrain under the target is flat to `+0.00%` against a 13.8° arrival, so
the ground is not shaping any of it, and the walk from the release probe is −2,308 / −2,409 / −2,346 m
across the three arms — unchanged, still the largest single term, still the thing 8g points at.

**And `fixes` is a dead heat.** 0.88 with an interval spanning 1.0 both ways, over ten shots against
ten. The ceiling and the best-aim keep cost nothing and bought nothing measurable at this range.

## 8i. The surface disagreement is not there — the walk is in the last half-second

Item 8g concluded the round "hit ground the prediction does not see" and named the surface as the
next thing to chase. **`WarheadTrace.Surfaces` refutes it**, and it is the one instrument built to
answer the question:

```
surface at the landing point: the round stopped on 6371000.0 m,
the prediction flies to 6371000.0 m (+0.0 m apart)
```

**+0.0 m apart on all 24 shots of `~/shots/2026-08-25-2214`**, across all three arms. Not a
degenerate fallback either — shots 004 and 005 read 6371005.5 and 6371000.4, so the sampler is
answering with real values that happen to be sea. `TerrainRadiusAt` and `GroundTest` both ask
`accurate: true`, both clamp through `GroundSurface.Height` against the same ocean reference, and
both add `MeanRadius`. There is nothing between them to disagree about.

**And the trajectory is not drifting either.** Re-flown from where the warhead has got to, the
prediction tracks it to **7 m** — for 403 seconds and 550 km of descent, down to 888 m of altitude
with 0.86 s left to fly. A cause that had been accumulating over the flight cannot then produce 2.4
km; the two part in a **step**, which is what that discriminator exists to say.

**Where it goes is the last half-second.** Shot 019, the fine samples at the end:

```
t=402.70s  alt 888 m  v 4461 m/s   prediction: impact at t=403.57, -39.2678,178.1786
t=403.11s  alt 468 m  v 4338 m/s   <- last sample; the flight is recorded as ending here
landed at -39.2665,178.2063 | flight 403.11s by the world clock, 403.11s by its own,
                              probe said 403.57s | walk 2401 m (-2292 down, +716 cross)
```

The descent rate is honest throughout — 30 m per 28.7 ms frame is 1,045 m/s, which is 4,338 m/s at
the 13.8° arrival. The round is simply **scored as landed 0.46 s early**, and 0.46 s of horizontal
travel at 4,212 m/s is about 1,900 m, which is the bulk of the 2,292 m downrange walk.

**What is not yet settled** is the contradiction between two readings of the same landing: the last
trace sample puts the round 468 m *above* a surface at `MeanRadius`, while `Surfaces` reports it
51.8 m *below* that surface. Both cannot describe one instant. Either the trace stops sampling
before the last frames, or `BallisticBody.SurfaceRadius` — what `AltitudeOf` subtracts — is not the
sea-clamped radius the landing was scored against. **That is the next measurement**, and it is a
reading of two constants rather than a night of shots.

**No fix here, and deliberately.** What this changes is the target: 8f and 8g both point downstream
at the surface, and the surface is clean. Everything the correction loop reads is still driven by
the probe, so the 2.4 km is still the largest term — it is just not where it was thought to be.

## 9. The budget at the 0.65 km level

`MirvBudgetTests` re-measures the whole group now that every term the 11 km budget was dominated by
has been fixed. It flies the real `IcbmProgram` through `IcbmFlightRig` with the aim correction wired
as `Ksa/IcbmComputer.cs` wires it, takes the state the engines actually stopped in, and puts six
warheads off it — six real `Slug`s at the step `WarpPolicy` holds the world to.

**The rig reproduces the flown bias and does not reproduce the flown spread.** Flown as the game
flies it, the group's centre is **720 m** out at 1x and **963 m** at 8x, against a flown 650-760 m;
the scatter is **235 m** against a flown 860-970 m. So the bias is accounted for and about 600 m of
the spread is not — see the attitude entry below, which is the one candidate that fits.

| term | bias | spread | how measured |
| --- | --- | --- | --- |
| **the aim correction's frozen residue** | **760 m** | — | the same flight with `Freeze()` never called lands the group at **18 m**; the loop's own readout says 0.78 km either way |
| **the round against its own predictor, 8x coast** | **203 m** | — | one cutoff state through `ImpactPredictor` and through `Slug`; 40 m at 1x, 21 m at 50 ms, 636 m at 320 ms |
| **the tube cant** | 43 m | **233 m** | six kicks 6 deg off the mean at 2 m/s, through the drag predictor, at the attitude the burn left |
| the cutoff residual after the trim | 38 m | — | 0.017 m/s against 1,789 / 3,442 / 390 m per m/s on three axes, root mean square |
| the 5 ms sub-step floor | ~60 m | — | the round at any frame with gravity re-read per sub-step, against a 1 ms flight |
| the release gate's own budget | — | ≤ 55 m | two rounds a whole `LateralBudgetMetresPerSecond` apart square to the nose |
| release pacing, 100 ms across six | 1 m | 2 m | six impacts 20 ms apart, each un-carried by its own delay |
| the predictor's ground crossing | 1.5 m | — | it stops 18 cm under the surface on a 7.1 deg arrival, worth 8 m of ground per m of height |

The bias terms are vectors and partly cancel, which is why they sum to more than the 720/963 m the
group actually lands at.

### The trajectory is half the budget, and the old rig had the wrong one

Every velocity-side term is metres a second times a sensitivity, and the sensitivity belongs to the
trajectory. The cheapest arc from a 200 km circular pickup — what `ErrorBudgetTests` and
`PerTubeTrimTests` measure on — is **3,678 / 6,281 / 447** m per m/s prograde, radial and cross-track.
What the guidance actually leaves the bus on is **1,789 / 3,442 / 390**, which is the flown
3,401 / 1,769 / 780 to within a few per cent.

So the 2,692 m of cant spread in `PerTubeTrimTests` is an idealised arc's number, not the flown one.
On the guided trajectory the same cant is 233 m. Nothing removed two thirds of it; it was never
there.

### What the cant is worth depends on where the burn left the nose

The cant is a cone about the bus's axis, so the six kicks differ only square to it — and the impact's
sensitivity in that plane is whatever two directions the nose happens to leave there. Prograde and
radial move the impact the same way, so a nose tipped between them leaves a combination that barely
moves it at all. Swept over every attitude a bus could hold, on one trajectory and one cant:

| the nose held | spread |
| --- | --- |
| best case anywhere on the sphere | 141 m |
| as the burn left it (-0.32 prograde, -0.92 radial) | **233 m** |
| nose-down (-radial) | **860 m** |
| retrograde | 1,503 m |
| worst case anywhere on the sphere | 1,684 m |

**Nothing chooses this attitude for the cant's benefit** — it is where velocity-to-be-gained ran out.
A nose-down bus gives 860 m, which is the flown spread, so the gap is most likely this and not a
missing mechanism. It cannot be settled headlessly: the flown attitude is not recorded anywhere in
this repository, though the release probe already prints `thrown N deg from the platform's track`,
which is exactly that angle.

### The round's frame-step error is entirely the frozen gravity

`Slug.Update` takes gravity and the air's motion as frame-level arguments and holds both across every
5 ms sub-step; `ImpactPredictor` re-evaluates gravity at each Runge-Kutta stage. Against a 1 ms
flight of the same code:

| frame | as flown | gravity re-read per sub-step | air re-read per sub-step |
| --- | --- | --- | --- |
| 17 ms | 23 m | 51 m | 23 m |
| 50 ms | 38 m | 64 m | 37 m |
| 130 ms | 220 m | 62 m | 218 m |
| 320 ms | 654 m | 59 m | 647 m |

**The air's motion is worth nothing** — it is the planet's spin at the round's radius and barely
changes over a frame. **Gravity is the whole term**, and re-reading it pins the error at ~60 m at
every frame size, which is the 5 ms symplectic-Euler floor. Held, it grows linearly with the step.

At the step the world is actually held to — 8x through the coast, `Medium.FaithfulStepInAir` once
there is air — the freeze moves the impact **284 m**, and takes the round from 64 m off a converged
flight to 220 m. `Sim/BallisticBody.cs` already carries `Mu`, so an analytic per-sub-step gravity
needs no call into the game.

It is also the whole of the 1x-versus-8x difference: 720 m against 963 m of bias on one identical
cutoff state.

### An anomaly worth its own look

At **5,000 km** the correction observes 126 cycles of an 83 km miss, finds every aim it tries worse
than no aim at all, and reverts to a zero bias — with 34 t of propellant still aboard, so it is not
the short-shot case. 2,000, 3,459 and 7,645 km all correct normally. Headless and rig-dependent;
`AimConvergenceTests` reports 1.16 km at the same range through a loop that does not add the ejection
kick to its prediction.

### Ranked, cheapest first

1. ~~**Re-read gravity per sub-step, or cap the coast step.**~~ **Both halves have flown.** The
   gravity re-read lost three times out of three (item 2d), and item 2's decomposition says why: it
   removes the term that was cancelling the round's own integration error, so the two halves have to
   go together or neither. The coast step is capped — `PreferredStepSeconds` at **0.225**, flown for
   a median 1.66 -> 1.06 km — and is not the same field as `MaxFaithfulStepSeconds`, which flew at
   48-60 km. The pricing below stands; ~160 m of bias at 8x and the whole 1x/8x split with it, one
   argument becoming a lambda with `Mu` already to hand.
2. **Reopen the aim far enough after cutoff.** 740 m, the largest single term. `Sim/PostBoostAim.cs`
   is the lever and is not modelled headlessly — what the rig says is that a loop still observing
   ends at 18 m against 760. The flown passes went 2.9 -> 2.9 -> 2.1 -> 1.2 km, so the question is
   how far they actually get, not whether the mechanism works.

   Item 8b is between this and the answer: until the settle gate is flown, what those passes were
   reading is a nose direction as much as a shot.
3. ~~**Log the held nose in the velocity frame.**~~ **Done.** The release probe prints
   `thrown N deg from the platform's track` and `shot-report.py` medians it per arm, which is what
   read the 95/116/119° of item 5 and 8b. It turned the cant from a 141-1,684 m band into one
   number, as advertised.
4. **Re-pointing (item 5).** Removes the cant outright — 233 m at the attitude this rig flew,
   up to 1,684 m at the worst. No longer blocked: the dead zone that refused the six-degree command
   was a latched deadband and now reads 0.31°, so whether the bus follows it is a flight away.
5. Everything else is under 60 m and not worth a flight of its own.

**And every one of them is priced on a seven-degree arrival.** The velocity-side terms scale with the
trajectory's sensitivity and the surface-side terms with `cot γ`, so flying the shot in at fifteen to
twenty degrees divides the first group by eight and the second by nearly three — before anything on
this list is touched. `docs/ARRIVAL-ANGLE.md` has what that costs, and
`IcbmConfig.MinArrivalAngleDeg` is how the guidance is told to do it — off at zero, and flown as
the `arr15` and `arr20` arms.

**And there is a floor under all of it.** `docs/KINETIC-FLOOR.md` prices what is left when every
item above has landed: on this 7.1-degree arrival about **160 m**, dominated by the round's own 5 ms
symplectic-Euler step at 30.6 m per millisecond, and multiplied again by a terrain gain that at
shallow angles has no fixed point. The same budget at an 88-degree arrival is **1.2 m**. The arrival
angle is a larger lever than anything on this list.

**None of it is flown.** The rig's planet sits at the origin and carries no velocity, which is the one
case where a frame carrier is identically zero — so nothing above can see an epoch fault, and item 2c
is why that matters. And a single flight cannot resolve anything under about 0.5 km (item 7d), so the
bias terms are testable in flight and the spread terms mostly are not.

**Items 1 and 5 are under the line a night can settle, and item 2's is unknown** — 233 m of cant
against a 300 m floor at 25 shots an arm. Fly them *together* as one arm, or as the two factors of a
factorial, so their sum is what is being measured; `docs/SHOT-PROTOCOL.md` is the arithmetic.

## Smaller things

- **A warhead flew and never arrived, once in about sixty shots (2026-08-24).** The screening arm's
  `screen: tube 1 flies ...` line was written, so it was created and stepped, and no
  `round 1 detonated on the ground` line followed. **Not the defending site** — the Pantsir at the
  aim point was never armed, which removes the one benign explanation. So a round is being lost
  rather than intercepted, and the candidates left are a state nothing reports: expired, frozen on a
  NaN, or no longer being stepped.

  Worse than the loss is that **nothing said so**. `ShotGroup` is documented to score the worst
  warhead and to count one that never arrived, and the scenario reported `PASS` on five.
  `tools/ab-shot.py` warns about a short group only because it was written after being caught by
  this. **The diagnostic comes before the fix**: a round that does not arrive should name its own
  terminal state, and until it does this is one observation with no mechanism.



- **The load-frame warning** and **the `OpticalHeads` stranding bug** are both fixed and unflown;
  the first now goes to DEBUG with an empty sky, the second follows a director across a split
  through the same `PlatformHandover` decision the launcher roster uses.

- **Negative tube numbers in the log** — fixed, unflown. Not an off-by-one: `FireGun` assigns
  `-(barrel + 1)` deliberately, so a shell can never be reclaimed as a tube. The *display* was
  wrong, and inconsistently — three call sites already decoded it. `Sim/RoundLabel.cs` is the one
  place that does now, and a shell reads `shell from barrel 4`.

## What is already verified in flight

- Separation at cutoff, twice, once each time, on the joint holding the launcher.
- The weapon following onto the new craft with its magazine, rounds, settings and teams intact —
  `5 round(s) aboard, 1 in flight` after a mid-release split.
- The ballistic computer following it and continuing to deploy all six.
- The frozen release line: every round landed within 0.16-0.5 km of its own prediction, against
  5.5 km before it.
- The air-defence site intercepting two inbound warheads at 11 m and 15 m, having detected one at
  20 km and re-laid on the second at 4.1 km. Neither system knows the other exists.
