# What is left on the MIRV bus

Everything below is either measured in flight or explicitly marked as untested. `docs/ICBM-GUIDANCE.md` has the algorithm;
`CHECKLIST.md` §12.7 has the in-flight list. This file is only the backlog.

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
drag-model error at **1.8 km** at 7.5 degrees against **77 m** at 15 — a factor of 62 — and a
drag-model disagreement is precisely what `gap` is made of.

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

- **The coast speed depends on whether the magazine emptied.** `BallisticScenario.WarpTheCoast` is
  called only from the `ammo <= 0` branch, so a shot that holds a warhead back coasts at **1x** and
  one that empties coasts at **8x**. Item 2's table puts `gap` at **−73 m** and **+394 m**
  respectively — *the term under test changes sign with an unrelated outcome.*
- **The 0.01x pick-up pin has no `else`.** It is asked for only when `KsaWorld.IsPaused`, so a world
  already running gets no pin at all, and the only thing separating the two cases in the log is the
  presence of `the world was paused; asked for 0.01x`. Item 7d prices a different pick-up at
  **164 km**.

And the miss the harness reports is a **3-D Euclidean distance** (`BallisticScenario.MissFromAim`),
so it is unsigned: short, long and cross-track are one number, and a gain inversion is invisible in
it. The decomposition exists only in `Ksa/WarheadTrace.cs`, which reports the walk downrange and
cross-track — and which `ScenarioRunner` already switches on for every `mirv` run.

### What would settle it

**The trace has never been read.** It is on by default in the scenario and verbose logging is forced
on with it, so `gap` and the surfaces are already being written to
`<KSA user dir>/Logs/KSArmory.log` on every run — `release probe:`, `warhead trace: probe from the
round's own state`, the per-cycle `walk from the release probe M m (+D down, +C cross)`, and the
impact line against both the aim and the probe. Reading one existing log separates `gap` from `G`
before anything is flown.

Then a 2x2, six shots a cell, at one aim point:

| | shipped | + `Arsenal.ReentryVehicleMk21.PreferredStepSeconds = 0.05f` |
| --- | --- | --- |
| **shipped** | the baseline draw | the known failure, replicated |
| **`AimCorrection.MaxMetres = 0.0`** | the noise floor with no loop in it | does raw accuracy track the physics? |

`MaxMetres = 0` is a clean total ablation — `Vec.ClampLength(v, 0)` returns zero, `Apply`
degenerates to the identity, and the post-boost loop goes with it because the bias is its actuator.
It costs 17 headless tests, all of them assertions about a loop that is no longer there.
`PreferredStepSeconds` is a one-line restore of what `631f0ac` removed: round-only, and priced at
about 675 m of `gap` off item 2's frame table.

**The reading that matters is whether `Δmiss` is the same in both correction arms.** Equal says the
loop is blind to the round and the residue is `G`; unequal says the loop is re-expressing the fix
after all. Either way the scatter of the ablated arm is the first honest noise floor this shot has
had, and a sea aim point — where `GroundSurface.Height` clamps both surfaces to sea level and `G` is
one by construction — is the control that prices `G` directly.

## -1a. The round and its predictor must agree — being right alone is worth nothing

`KsaWorld.GravityAt` gives a round only its parent body's pull, and Ecl is heliocentric — so the
engine carries the planet along its orbit and the round is not carried with it. That is a real
omission, worth 733 m over an eight-minute coast and up to 9.7 km at the Mk 21's flight-time limit.
Adding the star's pull makes the round's flight **more correct**.

Flown four times: **4.78 / 4.78 / 5.93 / 5.93 km** against a 1.67 km baseline. Reverted.

**`ImpactPredictor` does not model the term either**, and does not need to: it works in the body's
own frame, where the star's pull and the frame's acceleration cancel to first order. So before the
change the two models were wrong *identically*, which is self-consistent — and the aim correction
converges the **predictor** onto the target, so what reaches the ground is the round's disagreement
with the predictor, not either one's disagreement with physics.

Correcting one side alone converts a shared error into a difference, and the difference is the miss.

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

## 0. Radial translation jets — built, flown, reverted

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

Both commits and both reverts are on `dev`, so the geometry is one `git revert` away when the
correction is ready for it.

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
could. **Unflown** — the arm carrying it is in the air as this is written.

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

## 2. Every round lands beyond its own release probe — decomposed, unflown

The largest remaining term, and the one attempt at it made things worse.

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
varies the phase, not more reasoning.** Until then the round keeps the behaviour that flew best.

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
NominalFrame` is 8 x 1/60; the flight's unwarped frame during that stretch was ~25 ms, not 16.7, so
8x delivered a **median 198.5 / mean 201.9 ms, peaking at 266.7**. The rig's own table is linear at
4.15 m per ms above 67 ms, so the frame alone carries 394 -> **~680 m**.

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

1. **Cap the warhead's coast frame.** `MunitionProfile.MaxFaithfulStepSeconds` on the Mk 21,
   0.32 -> **0.10**, which is what `WarpPolicy` holds the world down to while the salvo flies. Priced
   through the real policy from the scenario's own 8x request:

   | cap | world held to | frame | gap | the 497 s coast takes |
   | --- | --- | --- | --- | --- |
   | **320 ms**, as shipped | 8.0x | 133 ms | **394 m** | 62 s |
   | 200 ms | 8.0x | 133 ms | 394 m | 62 s |
   | **100 ms** | 3.6x | 60 ms | **87 m** | 138 s |
   | 50 ms | 1.8x | 30 ms | -43 m | 276 s |

   **Every frame in that table is 1.5x too small.** It is `warp x DeorbitShot.NominalFrame`, a
   hardcoded 60 fps; the flight ran the warped coast at a **median 198.5 ms**, because six warheads
   and their effects cost ~25 ms a frame rather than 16.7. So the shipped row is 202 ms and ~680 m
   rather than 133 ms and 394 m, and a cap bites *sooner* than the table says — 200 ms is a real
   reduction in flight where the rig reads it as a no-op.

   **307 m for 76 seconds of the player's evening**, and the next 44 m costs another 138. No sign
   risk: both models converge on one answer as the step shrinks, and the round's error is monotone
   in the frame across the whole table. **Not done** — it trades against a decision `WarpPolicy`
   made deliberately. The trace now puts the frame-driven share at ~680 m of a 1,943 m gap, so this
   is worth more than the table claims and still not most of it.

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

4. **The ground centre's carrier.** Unchanged: it needs a flight that varies the phase.

5. **Both flight-model terms together** — gravity per sub-step *and* `SubStepSeconds` on the Mk 21 at
   about 1 ms — which the table above says lands at -6 m. Two coupled behaviour changes at once,
   one of which has three flights against it, and the cheaper item 1 gets most of it. Last.

One term is **zero and worth recording as zero**: `Slug` takes gravity through `Medium.Buoyancy` and
`ImpactPredictor` takes it raw, so a round declaring a `NeutralDensityRatio` is predicted by a model
that does not know it floats. Nothing in `Arsenal` declares one, but `Sim/PackReader.cs` reads the
field — so it is latent rather than absent.

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

**The penetration is not a second small term — it is the whole regression.** Fitted across the
night's twelve shots, `miss = 0.3135 - 0.02969 x depth_m` with **R^2 = 0.957**, and at zero
penetration it predicts **0.313 km** against the 0.370 km the previous build actually flew with its
depth at +0.1 m. The two agree inside the old night's own scatter, and nothing else in the shot
correlates: frame time r = -0.15, cutoff residual r = +0.34, arrival angle identical at 7.1 deg.

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
`arm/ground-crossing` is built and **flying**, and on that regression it should be worth x0.36 --
0.86 km back to about 0.31 -- which 12 shots an arm resolves comfortably. It turned out to be two faults rather than one: the
sphere misplaces the crossing, which bisecting against the real field fixes, and — sampled at the
top of the frame, where it reads the ground *behind* the round — it also fails to offer the crossing
at all when the ground rises, which no amount of refinement downstream can recover. A broad phase
that can miss is the thing `Ksa/HullTest.cs` is careful never to be. Both ends of the frame's travel
are now sampled and the higher surface kept. It was first priced at ~157 m from the arrival angle and nearly left unflown
on that basis; the regression above is why it is being flown instead.

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

## 5. Re-pointing — closed. The turn is inside the engine's own dead zone

**Measured in flight on the separated bus: a pointing band of 22.11 degrees.** The cant it would
have to correct is six. The vehicle's own controller will not make that turn, and no command the
mod can issue changes that — there is no convergence test to satisfy and nothing refusing the
request, the bang-bang tracker simply does not fire inside its dead zone.

```
Rocket_1 control: None/None/None, roll Decoupled, control part NONE
  | deadband 12.87 deg, turnaround 15.68/15.68 deg, rate bit 1.568/1.568 deg/s
  | pointing band 22.11 deg
```

Three things in that line, and each closes a question:

- **22.11 deg of band against a 6 deg turn.** Item 5a predicted this: the band is
  `0.5*AngleDeadband + AngleTurnaround`, and `AngleTurnaround` is about ten seconds of one minimum
  thruster pulse divided by inertia — so dropping the spent stack is exactly what widens it. This
  bus lands far on the wrong side.
- **`roll Decoupled`.** Roll *rate* is damped and roll *angle* is free, so even a turn that took
  would not hold: a latched tube axis walks a cone at about 1.8 deg/s.
- **`control part NONE`.** Nothing re-elects a control part on the separated half, so which body
  axis is the nose is undefined on the vehicle the turn would be commanded against.

**What this also explains** is the flown scatter. The release probe reports the salvo thrown 95,
116 and 119 degrees from the platform's track on three otherwise identical runs — the bus drifting
freely inside that 22 degree band. The cant is a cone about the nose, so where the nose happens to
sit decides whether the six kicks cancel or add, across a 141-1,684 m band. That is the
unaccounted spread in the budget and much of the run-to-run variation, and it is not something an
attitude command can reach.

**So the cant stays.** All three routes to it are now closed with numbers: firing on each tube's own
crossing (5b), trimming between releases (5c), and re-pointing (here). Reopening any of them means
a different bus — finer RCS, more inertia, or thrusters placed for translation — which is a craft
design change rather than a mod change. On the guided arc the term is worth **233 m** of spread,
not the 2.69 km an earlier pass priced it at on an arc nobody flies.

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

**One flight with the verbose log settles the rest.** If the pointing band on the separated bus reads
well under six degrees, the corrected command is worth switching on and the ~0.9 km of spread is
recoverable. If it reads at or above six degrees, item 5 closes the way 5b and 5c did — the
correction needs a turn the vehicle's own controller will not make — and the way to reopen it is a
bus with finer RCS or more inertia, which is a craft-design change rather than a mod change.

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

**And it cannot be flown, because the correction is exactly the one thing the bus cannot do.** A
cant is a *cone*, so every tube's kick carries the same axial share and the difference between any
two of them is perpendicular to the bus's axis. Measured off `Arsenal.MirvBus.Tubes`: each tube is
0.2093 m/s from the mean, of which **0.0110 is axial and 0.2091 lateral**, and every tube-to-tube
step is **1.5e-6 m/s axial** — a pure lateral translation to six significant figures. The shipped
bus is four clusters of four laid out for pitch, yaw, roll and axial thrust, with no lateral
translation at all, so the axial pair has literally nothing to fire at. Run against that layout the
real loop strikes off each lateral direction in turn and gives up after **4.0 s with the whole
0.209 m/s still on the vehicle** — six times over, for 24 s of coast and no change to the group.

**Nothing rescues it.** Turning the bus so the axial pair points along the needed lateral is two
full settles per tube on a vehicle that item 5 measured *hunting* at a six-degree command — and a
vehicle that could do that could do the six-degree turn instead, which costs no propellant at all
and removes the term completely. Letting the arrival time float per tube is one parameter against a
two-dimensional lateral error. Firing opposite pairs cancels on the bus and not on the warheads.

**What would reopen it:** lateral translation on the bus. That is a craft-design change — RCS
blocks placed for translation rather than for attitude — not a mod change, and the arithmetic above
says the moment one exists this is worth about 2.3 km of spread. `PerTubeTrimTests` is kept so it
can be re-priced against a different cant, ejection speed or trajectory without redoing any of
this.

**So item 5 is still the answer to the cant.** Its open question — why a separated bus does not hold
a six-degree command — is answered in 5a as far as reading can take it, and what is left is one
number the probe there prints in flight.

## 6. Point the bus at the target on release — cosmetic, do it last

Mechanically a couple of lines: the sequencer rotates whatever attitude it is handed, and the
reference is measured from wherever the bus holds, so the geometry and the prediction follow.

**But not yet.** The ejection is 2 m/s along the tubes, so the release attitude decides which axis
that error lands on, and nothing compensates post-cutoff. After item 1 the error is small enough
that the attitude stops mattering and this becomes free.

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

## 7e. That 0.5 km is one latched warp decision, not frame pacing — measured, unfixed

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

1. **Condition on the coast step in `shot-report.py`** — median `step` over `sim > 1` frames, per
   shot, beside the walk. Costs a line, changes no behaviour, and turns the largest nuisance in the
   protocol into a covariate. It also says immediately whether an arm is being scored on its
   release altitude.
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


## 8b. The correction was reading a moving instrument — apportioned, fixed, unflown

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
drifting, because `Predict` adds `ReleaseImpulseCci()` — the modelled 2 m/s ejection kick — along
that nose before flying the prediction.

**It is the nose, by a factor of 126.** `PostBoostObserverTests` sweeps both terms on the guided
cutoff state at 3,459 km, with the aim converged to a 30.5 km bias and a baseline predicted miss of
0.72 km:

| what moved | predicted miss |
| --- | --- |
| nothing — kick on the nose | 0.72 km |
| nose turned 11.06° (half the measured band) | 0.88 / 1.08 km |
| nose turned 22.11° (the whole band) | 1.33 / 1.72 km |
| **nose turned 95°** (low end of the flown throw band) | **9.40 / 10.50 km** |
| **nose turned 119°** (high end) | **12.57 / 13.54 km** |
| nose turned 180° | 16.68 km |
| bus 0.02 m/s off its arc (`BusTrim.SettledMetresPerSecond`) | 0.78 km |
| bus 0.05 m/s off | 0.89 km |
| bus 1.10 m/s off (a whole decoupler shove, un-nulled) | 4.51 km |

The 95-119° row is item 5's own evidence read back: the release probe reports the salvo thrown 95,
116 and 119 degrees from the platform's track on three otherwise identical runs, because a separated
bus has a 22.11° pointing band, `roll Decoupled` and no elected control part. On this arc that band
of *directions* is 8.7-12.8 km of predicted miss with nothing about the shot having changed — which
brackets the flown 14.0 km peak.

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

1. **Re-read gravity per sub-step, or cap the coast step.** ~160 m of bias at 8x and the whole
   1x/8x split with it. One argument becomes a lambda and `Mu` is already to hand. A step cap is the
   same win for free but costs the player their warp.
2. **Reopen the aim far enough after cutoff.** 740 m, the largest single term. `Sim/PostBoostAim.cs`
   is the lever and is not modelled headlessly — what the rig says is that a loop still observing
   ends at 18 m against 760. The flown passes went 2.9 -> 2.9 -> 2.1 -> 1.2 km, so the question is
   how far they actually get, not whether the mechanism works.

   Item 8b is between this and the answer: until the settle gate is flown, what those passes were
   reading is a nose direction as much as a shot.
3. **Log the held nose in the velocity frame.** Costs nothing, and turns the cant from a
   141-1,684 m band into one number. Until it is logged, item 5's payoff is unknown by a factor of
   twelve.
4. **Re-pointing (item 5).** Removes the cant outright — 233 m at the attitude this rig flew,
   up to 1,684 m at the worst. Still blocked on why a separated bus will not hold a 6 degree command.
5. Everything else is under 60 m and not worth a flight of its own.

**And every one of them is priced on a seven-degree arrival.** The velocity-side terms scale with the
trajectory's sensitivity and the surface-side terms with `cot γ`, so flying the shot in at fifteen to
twenty degrees divides the first group by eight and the second by nearly three — before anything on
this list is touched. `docs/ARRIVAL-ANGLE.md` has what that costs and whether the guidance can be
told to do it, which today it cannot.

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
