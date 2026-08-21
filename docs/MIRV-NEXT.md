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
3. **The mass declaration.** `<SolidSphereMass>` carries no `<LocationAsmb>`, so KSA puts all
   6,300 kg on the mounting face while the geometry centres near X ≈ 1.4. Every lever arm on the bus
   is set by that.

Both commits and both reverts are on `dev`, so the geometry is one `git revert` away when the
correction is ready for it.

**The engine rule they established stands regardless**, and is the durable part of the work:
`ThrusterController.ComputeControlMap` flags a nozzle for a translation on any thrust component over
**0.5** — a 60° half cone, judged per nozzle with no reference to lever arms or to the layout as a
whole. That is why one radial jet per cluster serves every lateral direction, and why the whole ring
lights up for a single command.

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

## 2. Every round lands beyond its own release probe — reverted, still open

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
varies the phase, not more reasoning.** Until then the round keeps the behaviour that flew best, and
a ~0.3-1.2 km round-versus-probe gap is the standing error.

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
