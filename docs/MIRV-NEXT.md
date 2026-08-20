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

## 6. Point the bus at the target on release — cosmetic, do it last

Mechanically a couple of lines: the sequencer rotates whatever attitude it is handed, and the
reference is measured from wherever the bus holds, so the geometry and the prediction follow.

**But not yet.** The ejection is 2 m/s along the tubes, so the release attitude decides which axis
that error lands on, and nothing compensates post-cutoff. After item 1 the error is small enough
that the attitude stops mattering and this becomes free.

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
