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

## 1. Null the separation impulse — flown and working

`Sim/BusTrim.cs`, on by default via `IcbmConfig.TrimBeforeRelease`, and it is a precondition of
being ready to deploy rather than a step inside the release sequence. It re-solves the arc from the
bus's state to `IcbmProgram.CommittedArrivalFromNow`, resolves the difference onto the vehicle's own
control axes, and holds the corresponding `TranslateForward`/`Right`/`Down` flags through
`Vehicle.ProcessInput` — the same channel the throttle already uses, so nothing new is patched.
`docs/ICBM-GUIDANCE.md` has the full account.

Headlessly on this trajectory: a 1.1 m/s shove is **2.4 km of miss, trimmed to 2 m in 1.5 s**.

**Flown and working.** The translation flags reach the bus's nozzles, they were measured at
**0.9-2.2 m/s2**, and the trim closes at that rate: `trimming 1.23 m/s on the tail` →
`trimmed to 0.010 m/s` in 1.8 s.

**Unflown since:** the clearance wait added afterwards (`Sim/SeparationClearance.cs`, 50 m or 90 s),
which delays the trim by roughly thirty-five seconds. Watch that the release window still has room
for it, and what standoff it reports — the log prints the measured distance, so 50 m stops being a
guess after one flight.

Never yet exercised: **whether the tank lasts.** ~183 kg of MMH/NTO against a few m/s is comfortable
on paper and nothing has spent it.

## 2. Every round landed beyond its own release probe — cause found, fixed, unflown

Six samples from one shot, and all of them the **same way** off (aimed `-26.485,-68.148`; landed
between `-26.486,-68.152` and `-26.487,-68.161`) — a bias of roughly 900 m superimposed on the
cant's ~1 km spread:

| predicted at release | landed |
| --- | --- |
| 0.2 km | 431 m |
| 0.1 km | 537 m |
| 0.1 km | 607 m |
| 0.1 km | 1.1 km |
| 0.1 km | 1.2 km |
| 0.1 km | 1.4 km |

**`Slug` differenced a frozen body centre against a position moving through the frame.** `IGroundTest`
answers with a centre and a surface radius, and the round holds both for the frame — one terrain
lookup rather than one per sub-step, which is what makes a 150-shell burst affordable. But the centre
carries the planet's ~29.8 km/s of ecliptic travel and so does `PositionEcl`, so
`Len(PositionEcl - centre) - radius` reads the carrier as a change of *altitude*: up to 500 m across
a 16.7 ms frame. On a ~5° arrival every metre of that is about eleven metres of ground.

Invisible from both sides, which is why it survived. `ImpactPredictor` integrates in the body's own
frame, where there is no carrier at all — so the prediction was right and the round was wrong, and
the aim correction reads the prediction. And **every headless rig flew the round about a planet
sitting still at the origin**, which is the one case where the fault is identically zero.

Measured headlessly on the flown deorbit, carrier straight up at the impact point: **850 m** at a
16.7 ms frame, **1,027 m** at 50 ms, and **790 m** between the round and its own release prediction.
Without a carrier the two agree to 60 m, so the integrator, the step size and the ground model were
never the problem.

The centre is now carried across the frame with the round's own frame. The phase is the whole
correction and is worth 7 km taken the other way: the celestial state belongs to the *start* of the
step, so it is carried forward from there — the same phase `AirDensityIntoFrame` and `GravityAtRound`
already use. `GroundFrameTests` fails against the frozen centre at exactly the numbers above.

**Unflown.** If the flown bias does not close, the next term is already measured and is a different
shape: the terrain *radius* is held for the frame too, so the round crosses a sphere sampled behind
the impact point — 20-275 m on a ±10% local slope, and it does **not** scale with the frame, which
is what tells the two apart.

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

## 5. Re-pointing destabilises the vehicle — off by default

**This is the finding that changes the plan.** Flown on a separated bus with the sequencer on,
commanding six degrees away from the held line made the vehicle *hunt* rather than settle:

```
turning onto tube 1, 6.0 deg to go ... 1.3 ... back up to 10.3 ... back down ...
releasing tube 2 with the tubes sweeping 0.082 m/s - the warheads will scatter
(the same for tubes 3, 4, 5, 6)
probe: 9.9, 8.6, 7.8, 7.0, 6.2, 6.6 km
```

It swung *past* the reference to 10.3° — nearly twice the cant it was correcting — never brought the
sweep below 0.08 m/s against a 0.05 gate, so every release was a timeout and the sequence gave up
after the first tube. Against 1.7-0.3 km on the same shot without it.

So `IcbmConfig.RepointBetweenReleases` now defaults **off**. The give-up paths worked exactly as
designed — it released rather than holding warheads, and said why every time — which is the only
reason this was a bad salvo rather than a lost one.

**What to investigate before turning it back on:** whether the oscillation is KSA's attitude
controller, the RCS authority, or the command being rotated every frame while the reference is
fixed in Cci and the vehicle's own frame is rotating under it. The last is the one I would look at
first — a fixed target in an inertial frame is not a fixed target in the body frame, and the
controller may be chasing a moving one.

Note also the slew itself is fine: 6° in about four seconds, ~1.5°/s. **The 28 s per tube measured
earlier was entirely settling**, so this is a stability problem and not an authority one.

## 5b. Re-pointing is built and headlessly proven

`Sim/ReleasePointing.cs` and `Sim/ReleaseSequence.cs`, on by default via
`IcbmConfig.RepointBetweenReleases`. Headlessly it collapses the tube-cant spread from **1,730 m to
0 m** and is roll-independent; in flight its benefit was buried under item 1, which is now built and
wants flying before this can be judged.

Two things to look at once the impulse is nulled:

- **~28 s per tube.** Measured, and it is genuinely the settle rather than a cadence
  (`SalvoSpacing` is 0.45 s). The gates — `ReleaseSequence.AlignedDegrees` 0.5° and
  `SteadyMetresPerSecond` 0.05 — were calibrated against an attached stack. On a 6,300 kg bus with
  a 2.6 m lever arm they are probably far too cautious. The log now says what it is waiting for.
- **Three minutes of salvo means every warhead gets its error amplified by a different
  time-to-impact.** That was the 2.4 → 0.3 km ramp. Faster settling fixes it for free.

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
