# What is left on the MIRV bus

Written at the end of the session that built separation and re-pointing. Everything below is either
measured in flight or explicitly marked as untested. `docs/ICBM-GUIDANCE.md` has the algorithm;
`CHECKLIST.md` §12.7 has the in-flight list. This file is only the backlog.

## The one number that matters

Six warheads, released inside **0.24 s** off the same vehicle on the same trajectory. The only
difference between the first and the other five is whether the decoupler had fired:

| | miss |
| --- | --- |
| round 1 — left **before** the split | **163 m** |
| rounds 2-6 — left **after** it | 3,100-4,100 m, tight to 1,000 m |

**The decoupler costs 3.5 km.** `CoreCouplingA_Prefab_Decoupler3WA` declares `Force="7000"`, which
against a ~6,300 kg bus is about **1.1 m/s**, and at cutoff attitude most of it lands on the radial
axis — the expensive one at 3,401 m per m/s against 1,769 along-track and 780 cross-track.

It arrives *after* the last thing that could compensate for it: the arc is solved at cutoff and
`Coast` never re-solves.

163 m is also the best honest result of the day, and it is what the guidance does when nothing
perturbs it.

## 1. Null the separation impulse — built, unflown

`Sim/BusTrim.cs`, on by default via `IcbmConfig.TrimBeforeRelease`, and it is a precondition of
being ready to deploy rather than a step inside the release sequence. It re-solves the arc from the
bus's state to `IcbmProgram.CommittedArrivalFromNow`, resolves the difference onto the vehicle's own
control axes, and holds the corresponding `TranslateForward`/`Right`/`Down` flags through
`Vehicle.ProcessInput` — the same channel the throttle already uses, so nothing new is patched.
`docs/ICBM-GUIDANCE.md` has the full account.

Headlessly on this trajectory: a 1.1 m/s shove is **2.4 km of miss, trimmed to 2 m in 1.5 s**.

**What has to be watched in flight**, because no headless test can reach it:

- **Do the translation flags move the shipped bus at all?** The four clusters are laid out for
  pitch, yaw, roll and axial thrust, so `ThrusterController.ComputeControlMap` should give the
  axial pair `TranslateForward`/`Backward` and the tangential jets whatever their thrust happens to
  point along. If nothing answers, the log says `nothing left aboard moves the bus` with the
  residual, and the warheads go anyway.
- **What acceleration they give it.** That number is the floor under the residual — one step of
  firing is `acceleration x step` — and it is logged (`thrusters measured at N m/s2`). If it is
  large enough that the residual lands above ~0.05 m/s at a sensible step, the next lever is KSA's
  own `FlightComputerManualThrustMode.Pulse`, which is a ~3.6% duty cycle and the exact analogue of
  the burn's throttle ramp.
- **Whether the tank lasts.** ~183 kg of MMH/NTO against a few m/s is comfortable on paper and has
  never been spent.

The warp hold now covers the trim as well as the burn (`IcbmComputer.NeedsShortSteps`), for the
same reason: the stop lands on a frame boundary.

## 2. The prediction under-reads the impulse by ~1.2 km

The release probe said 2.4 km for the shoved rounds; they landed at 3.1-4.1 km. So the prediction
sees *some* of the shove but not all of it — it is measuring the bus's post-split state, which
should be complete. Worth finding out why before trusting the probe on any separated shot.

## 3. The first round races the separation — fixed with item 1, unflown

Flown: `round 1 away` at 23:04:26.025, the split applied at `.071`. One warhead left the attached
stack and five left the shoved bus, which is why the salvo had a 163 m outlier and a 3.6 km group.

Separation now runs before anything decides whether a warhead may go, and the trim then holds the
release until the split has landed — `BusTrim.SettleSeconds`, gated on the decoupler's joint no
longer being there rather than on a timer. The state between "ready to deploy" and "releasing" is
the trim itself.

Worth confirming in flight that the first `round N away` line now follows the handover line rather
than preceding it.

## 4. The view change is still refused by the engine

```
could not go to Rocket_1: The shapes registry cannot be mutated while the vehicle update is
stepping; stage the change and apply it at the frame sync point
```

`KsaWorld.GoTo` calls `UpdateAfterPartTreeModification`. Deferring it one frame did **not** help,
because every frame of this mod runs inside that same pass — `IcbmComputer.CarryTheView` is called
from `Update`, which is inside `StepSimulation`. It needs a genuinely later hook, or a different way
to move the camera that does not rebuild derived data.

It fails safely: the camera stays on the spent stack and nothing else is lost.

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

- **Negative tube numbers in the log.** `round -4 was shot down` — tube indices are numbered from
  one, so something is handing out an index below zero. Probably cannon shells, which do not come
  from tubes at all. Cosmetic, likely a real off-by-one underneath.
- **`OpticalHeads` has the stranding bug the weapon roster had** — keyed on `(Vehicle, Ordinal)`,
  retired only on `!IsAlive`, and its own comment at `Ksa/OpticalHeads.cs:87` already claims it
  forgets a head "staged away". Untrue. Harmless until a director rides a separating craft.
- **The load-frame warning.** A 48 s first frame logs `rounds in flight will lag the world` with an
  empty sky. Should say nothing when nothing is airborne.

## What is already verified in flight

- Separation at cutoff, twice, once each time, on the joint holding the launcher.
- The weapon following onto the new craft with its magazine, rounds, settings and teams intact —
  `5 round(s) aboard, 1 in flight` after a mid-release split.
- The ballistic computer following it and continuing to deploy all six.
- The frozen release line: every round landed within 0.16-0.5 km of its own prediction, against
  5.5 km before it.
- The air-defence site intercepting two inbound warheads at 11 m and 15 m, having detected one at
  20 km and re-laid on the second at 4.1 km. Neither system knows the other exists.
