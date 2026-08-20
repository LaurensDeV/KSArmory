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
is item 5. The ~900 m *bias* — every round landed the same way off — is item 2, and it is the same
shot's release probe reading 0.1-0.2 km for all six while they landed at 0.4-1.4 km.

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

## 5. Re-pointing — off again, and this time flown

**This is the finding that changes the plan.** Flown on a separated bus with the sequencer on,
commanding six degrees away from the held line made the vehicle *hunt* rather than settle: the
offset climbed monotonically from 5.9° to 10.3° — nearly twice the cant it was correcting — the
sweep never came under 0.08 m/s against a 0.05 gate, every release was a timeout, and the salvo took
three minutes. Against 1.7-0.3 km on the same shot without it. So `RepointBetweenReleases` defaults
**off**, and the ~1 km spread in the current group is the cant it would have removed.

**The command is exonerated, by reading rather than by flying.** `held` is genuinely frozen —
`IcbmProgram._thrustDirCci` is latched at the burn, `Coasting()` returns it unchanged, and nothing
feeds `_deploy` back into it. The sense of the rotation is right: `Repoint(a,R)*a == R` is pinned,
and `VehicleCommand.TryAim` builds the attitude purely from cross products of `(direction, roll)`,
so it is equivariant under any proper rotation applied to both — rotating the command by `turn`
rotates the body by `turn`. Since `TryAim` demonstrably works during boost, it cannot be inverted
here. The latched-axis-to-command, live-axis-to-error pairing is also correct.

**So a monotonically growing error is the vehicle, and the sequencer now says which way it is
failing** instead of waiting out a 60 s timeout and reporting only that it gave up:

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
through the real predictor and drag model:

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

**So item 5 is still the answer to the cant**, and its open question is unchanged: why a separated
bus does not hold a six-degree command.

## 6. Point the bus at the target on release — cosmetic, do it last

Mechanically a couple of lines: the sequencer rotates whatever attitude it is handed, and the
reference is measured from wherever the bus holds, so the geometry and the prediction follow.

**But not yet.** The ejection is 2 m/s along the tubes, so the release attitude decides which axis
that error lands on, and nothing compensates post-cutoff. After item 1 the error is small enough
that the attitude stops mattering and this becomes free.

## 7. The aim correction is marginally stable, and that is the open one

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

**Still open at long range.** At 7,645 km one epoch leaves 15.74 km, and it is a different limit:
the loop moves the aim 196 km and stops on `WorseBeforeStopping` before the burn ends. That is
headroom in the stopping rule, not the ruler.

**`TerrainRadiusAt` had the same fault**, reading the height field in the frame of now for a point
un-carried to the cutoff epoch — so the arrival was flown against ground a whole burn's rotation
away. Flown: **5.35 km mean -> 4.85 km**.

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

**What it does not fix** is anything the prediction cannot see. It flies the *bus*, so the six tubes'
6-degree ejection cone is invisible to it — 2.6 km of spread by measurement, 1.9 km flown.

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
