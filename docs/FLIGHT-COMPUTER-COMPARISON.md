# What another flight computer does that the ballistic computer could take

**A plan, not a record.** The first version was drawn up on 2026-10-12. On 2026-11-21 it was redrawn against that
mod's tree at `f36ba18` (KSA `2026.11.16.5901`), 454 commits later, beside `docs/ICBM-GUIDANCE.md` and the code
under `Sim/Icbm/` and `Ksa/Icbm/`. The same day, every candidate was priced against the flown logs in `~/shots`.

Engine facts were checked against the 5901 decompiled corpus. Nothing here has been built or flown. The live ranking
of ballistic work stays `docs/ICBM-OUTSTANDING.md`, and an item moves there when somebody picks it up.

That mod is a general flight computer for KSA, with no ballistic guidance. What overlaps:
- its ascent to orbit (UPFG plus a convex ascent planner);
- its booster boostback and entry glide, which steer a drag-integrated impact point onto a site;
- an attitude controller of its own, which replaces KSA's on 5901;
- an RCS allocator.

Their figures quoted below come from their own headless harness.

## What the logs say first

Most of what that mod builds would buy nothing here, because the flown logs do not show the problem it solves:

- **Propellant is not what limits a shot.** 89% of 9,556 release summaries could have arrived 30–40° steeper than they
  did, and no flown ascent logged a shot short of propellant. The `ShortOfPropellant` verdicts in the logs were pad
  verdicts on seats 2+, which read a stale stage figure (item 2 below).
- **The solve is a small share of the frame.**
  - Mean `icbm` cost is 1.8–5.2% of the median frame, measured on the 6,181, 1,000 and 12,900 km nights.
  - On the 12,900 km night `icbmdraw` costs 1.54–1.69 ms, twice the computer's own cost.
  - The predictor's sensitivity solve is 0.56 ms median.
- **The bus jets are not short.**
  - 148 turns on 11-18 and 11-19 all settled, at about 1.0–1.5 kg each, against a 688 kg tank shared with the engine.
  - No turn moved the trim's debt at its 0.001 m/s quantum.
  - The trim spent a median 4.85 m/s per flight, and every one finished.
- **Staging has not failed since 10-15.**
  - 1,112 SRB drops and 765 interstage drops; no `staging left no engine` and no launcher refusals.
  - All 285 staging-gap samples read `shed propellant False`. The hot stage has only ever been lost headlessly
    (`IcbmFlightTests`).

So these are **closed with those numbers**: steering over the whole burn, solving off the frame thread, exact dual-number
sensitivities, an RCS torque allocator, an attitude controller of our own, a q·α limit, and the ignition delay after a
hot stage. The reasons are under [Closed](#closed).

What survives is one real bug, three cheap protections, two flagged features, and a test fixture. Two of the features
bear on the item that matters most in `docs/ICBM-OUTSTANDING.md`, which is 1.23 (GeoSat FAT at about 1,000 km misses by
kilometres). That item and 1.24 (the divert ring over-promises) outrank everything here. 5.7 (a negative trim budget
refuses every pass) is a smaller fix that also comes first.

## The plan

### Phase 0: one night of instruments, no behaviour change

These are all `refactor`s or `chore`s, each a line or two of log, flown together on one night so the later phases
start from numbers:

1. **Peak dynamic pressure.**
   - The engine destroys a vehicle at `VehicleStructuralLimits.MaxDynamicPressure`, 200 kPa (`PhysicsBubble.cs:1250`).
     It checks the unlagged peak of ½ρv² over the frame's sub-steps, reset each frame. That uses the surface velocity
     and the denser of air and ocean.
   - The throttle line already logs the mod's own q at the end of the frame. Add the engine's
     `StructuralLoad.PeakDynamicPressure` and limit beside it.
   - Add one INFO line per flight: the maximum, with altitude, airspeed, phase, throttle asked and achieved, and the nose
     angle off the airflow.
   - The highest flown so far is **123 kPa** (62%), on OneStage at 200 km, low and fast in the closed loop
     (`~/shots/2026-10-13-orbits`). The median across 415 logs is 35 kPa. No ascent has been lost to q.
2. **Attitude error by phase.**
   - `IcbmComputer.ProbeAttitude` already logs KSA's own error angles, at DEBUG.
   - Add altitude, q and the command's own turn rate to that line.
   - Add one INFO line per phase change with the phase's p95 and maximum error, so a night without verbose logging
     answers it.
3. **The stage model.** Behind `IcbmConfig.ProbeStageModel`, off. From `AttitudeHook`'s window, once per 250 ms of wall
   clock for each computer's craft:
   - read `TotalDeltaV` and `FindActiveSequenceDeltaV`;
   - call `SequencePerformanceList.RecomputeForFlight(ambient)` inside a try;
   - read both again, and log old and new with the call's time.
   - It has to be wall clock because a sim-time gate runs every step under warp.
   - It is safe from that prefix: `PrepareWorker` runs sequentially on the main thread before the job is queued, and the
     recompute publishes through an atomic swap.
4. **The bus's own drag where it coasts in air.** Every 10 s probe while the air is noticeable, log three things side by
   side:
   - the bus's measured non-gravitational acceleration;
   - q·`AeroTable` force over mass;
   - a second probe that flies the bus through its table to the release and then flies the warhead.
5. **Jets.** In the release summary, the tank's mass at the split, after the turn, after the trim, and at the last
   release. Today the split is inferred from the whole craft's mass.
6. **The drawing's cost.** Price `icbmdraw` before anything moves to a worker.

The night needs these flights:
- OneStage, ThreeStage and BusEarly at 200 km, plus a 25–100 km high-thrust cell nobody has flown, for item 1;
- a nine-seat 12,900 km world and a 1,000 km world, for items 2, 3 and 5;
- one 500 km shot on `SOLVER SCALE 1` with the in-air finish forced on, for item 4. The 107 km bus-drag miss was
  measured on 2026-10-05, on the old single-box drag. 5901 moved craft onto KSA's per-panel mesh, so the debt is not
  known for today's game.

Use a fresh boot, and stay off the CPU while it flies (`docs/SHOT-PROTOCOL.md`).

### Phase 1: small fixes, each with a test that fails against today's code

**1a. An unrefreshed stage figure reads 0, and the computer believes it.**
- `IcbmComputer.RunningStageDeltaV` accepts `stage >= 0`. KSA leaves the list at zero on any craft it never refreshed.
  On the 2026-11-19 reach night, 3,362 of 3,837 throttle lines read `stage dv 0 m/s`, and the 475 live ones were all one
  rocket.
- So on every seat but the one being flown:
  - `AimWaitsForTheSolids` is off;
  - the solids' unavoidable delta-v is read as nothing, so the arc lofts too little;
  - `SolidsUnderWeight` never fires.
- The fix: zero reads as no reading (NaN), so each consumer takes the fallback it already has.
- Rig test: a seat with no stage figure still lofts for its solids.
- It changes how seats 2+ fly, so it is flown paired before it is committed as a `fix`.
- The comment above `AimWaitsForTheSolids`, and `docs/ICBM-OUTSTANDING.md`'s "Seat 1 is the player's case", say KSA
  *reports* the figure for the controlled craft alone. What it does is *refresh* it there. Change both in the same
  commit.

**1b. A sequence that lights nothing strands the climb.**
- `IcbmProgram` stages again only once thrust has been seen (`_thrustSeen || !_everLit`). So a row holding only a
  decoupler, a fairing or a clamp is fired, lights nothing, and the climb waits for the dry timer.
- No flown stack has hit this. It is read from the code, and `docs/ICBM-GUIDANCE.md` records the same trap for ignition.
- The fix:
  - before staging, the `Ksa` side walks the next sequence's modules, as `LauncherSeparation.NextStageSeparates` does;
  - it passes `LastStageLitNothing` in `IcbmState`;
  - the program then treats that stage as proven, bounded to a few in a row;
  - an unreadable part counts as lit.
- Rig tests:
  - a decoupler-only row, which today strands;
  - an engine slow to light, which must still not be staged away, because a freshly lit engine reads dry until KSA's
    first rocket update.
- Fly one `make-rocket.py` stack with a decoupler on its own row, and confirm GeoSat FAT is unchanged.

**1c. Stand down when the craft cannot be controlled, or the player takes it.**
- `Vehicle.UpdateControl` releases the flight computer and forces 1x when a probe core loses power or fails. The engine
  does not stop a mod writing to it afterwards, and ours rewrites Auto/Custom every frame.
- `AttitudeHook` keeps what it last wrote per craft. Before rewriting, it compares that against the live computer,
  skipping the frame after its own Hold, Release or hand-back.
  - A mismatch on a craft that `!IsControllable` is a loss of control.
  - Otherwise somebody else took the attitude.
- Across 221,336 aimed probes on the November nights the read-back was always `Auto/Custom`, so the comparison has never
  fired falsely.
- Either way, `IcbmComputer.Yield(why)`:
  - hands the attitude back and releases the keys;
  - cancels pending stages and its own warp;
  - disarms;
  - leaves the engines as they are, because cutting a burn in the air costs more than a stood-down computer.
- The header strip latches the reason until the computer is re-armed. Whether a takeover should cut the engine as
  `Abort` does is a design decision for the owner.
- Measure in the same night: whether the bus's probe power lasts a 12,900 km coast with its jets firing.

### Phase 2: flagged features, each decided on a paired night

**2a. Refresh the stage model ourselves** (`IcbmConfig.RefreshStageModel`, off).
- Phase 0's call, plus an immediate refresh on the frame after a queued staging.
- `StackDeltaV` and `RunningStageDeltaV` then read what the hook captured.
- Price the call's milliseconds times the rocket count before the night. A CPU-costing arm changes the world's clock.
- This is what lets the reach, the arrival budget, `BoosterReach` and the panel's "one stage's reckoning" use the whole
  stack on every seat.

**2b. A stage plan for the countdown** (`Sim/StagePlan.cs`, `IcbmConfig.FlyTheStagePlan`, off; needs 2a).
- One constant-thrust phase per `SequencePerformance` phase, read from index 0, skipping spent sequences.
- The active phase takes the live thrust and flow from `BoosterPerformance`; later phases take design figures. The
  live headline thrust is never combined with the design delta-v.
- A solid's burn time comes from the recompute, since its grain is not in a tank.
- Each phase's throttle floor is read from the parts: the lowest `EngineController.MinimumThrottle` among its engines.
  That mod reads it with a transpiler on a private method; reading the parts avoids that.
- `BurnoutGuidance`'s `timeToCutoff` then knows the stagings.
- Test: a two-stage stack whose countdown on the single-stage model cuts off late.
- **Why it ranks here:** 1.23's open next step is to end the 1,000 km burn on the upper stage, where every cutoff has
  been clean, and a countdown that knows where the upper stage begins is what that needs.

**2c. Lead the attitude command through the one swing that lags.**
- On the November 5901 nights the nose follows its command closely everywhere but one place:
  - steady closed loop: 0.02° median, 0.46° p99;
  - through max q: under 2.6° p99.
- The exception is near 83 km, where `HoldIntoTheAirflow` lets go at 200 Pa. There the line swings about 100° at
  13–14°/s, and the nose trails it by a steady 11–13°: a tracker following a ramp with no rate feedforward.
  - It costs propellant, estimated at about 50 m/s thrown off the line.
  - It does not cost accuracy.
- The handover, the bus's per-tube re-points and `BusTurn` are commanded steps, which a feedforward cannot help.
- Two arms on one paired night, scored on propellant at cutoff and the swing's error:
  - **Lead in the command**, no patch: rotate the aim ahead by the command's smoothed rate times the measured lag (about
    0.9 s). Overshoot at the ramp's end is the risk.
  - **Rate feedforward**: a postfix on the private `FlightComputer.UpdateAttitudeTarget`, adding the command's rate to
    `AttitudeTarget.RatesCci`.
    - The rate is snapshotted in the `PrepareWorker` prefix for the worker to read, and cleared on release.
    - A patch that does not apply is today's flight, so it degrades like `PreRenderHook`, and is logged once.

**2d. Only if Phase 0 earns them.**
- **A dynamic-pressure cap**, if any stack goes past about 150 kPa (`IcbmConfig.CapsDynamicPressure`, off).
  - Throttle only, beside the g cap in `IcbmProgram.Limits.cs`, at 0.9 of the engine's limit, from a soft start at
    0.8.
  - `HoldBackTheAscent` and `ReserveBinds` must take the combined cap.
  - A solid cannot be throttled, so there the cap only logs.
  - The rig test must be shown to exceed 200 kPa with the flag off before it counts.
- **The vehicle's drag in the predictor**, if the 5901 debt is material (`IcbmConfig.PredictsTheVehicleDrag`, off).
  - `ImpactPredictor`'s drag term takes the craft's `AeroTable` force until the release, then the warhead's.
  - While in air, `BusTrim`'s reference becomes the drag-flown coast rather than the vacuum conic.
  - The table must exist at the split, so build the separated vehicle's table beforehand, or start the reference about
    2 s after the split.
  - What it would buy is retiring the pause and relight for 300–1,200 km arcs. That relight is half of 1.23.

### Phase 3: a test fixture

Built: `tests/KSArmory.Tests/Reference/make_reference.py` writes `lambert.csv` (105 cases) and `kepler.csv` (100), and
`LambertReferenceTests` and `KeplerReferenceTests` hold the solvers to 1 mm/s and 1 mm.
- Lambert is solved twice, by Izzo's Householder method and by Lagrange's equation bisected on 1/a; the two agree to
  7e-8 m/s, and each answer is flown onto its target to 2e-5 m. Elliptic, near-parabolic and hyperbolic, short and
  long way, 1° to 179°, at ICBM and LEO-to-GEO scale, on Earth and the Moon. Cases with |ψ| past 0.95 of
  `Lambert.cs`'s ±4π² bracket are left out.
- Kepler is solved by the change in anomaly, and checked against RK4 to 2e-5 m. Eccentricity 0 to 1.5.
- Kepler passes everywhere. Lambert failed one case at 48 halvings of the ψ bracket: near-parabolic, 1°, 9.8 s at ICBM
  scale, 1.68 mm/s off, against the 0.63 and 0.39 mm/s of the next two, all hops of 5–21 s. At 64
  (`Lambert.MaxIterations`) every case is within 0.03 mm/s.
- Still to add: two textbook cases, taken from the books rather than from memory.
- It answers 2.8 (two rigs that read wrongly on the shipped round) in a day.

## Closed

| Idea | Why not |
| --- | --- |
| UPFG or a convex ascent | No flown shot is propellant-bound (above). Reopen only if a stack runs out. |
| Solves off the frame thread | 1.8–5.2% of the frame. The last 0.75 s before cutoff must stay on the frame thread: one frame of staleness on the core at 1,000 km is 5–8 m/s. |
| Dual-number sensitivities | 0.56 ms a solve, and 40 of 40 warheads at 6,181 km inside the trace's 1 cm quantum. |
| An RCS torque allocator | About 1 kg a turn to save, and turns leave no measurable translation. Revisit for a bus with no axial engine whose walks bind on the tank. |
| An attitude controller of our own | The lag is one ramp, which 2c answers. `ComputeControl` is public and pinnable, but it leaves KSA in Manual, and gimbal, fin and RCS allocation is a large job the data does not ask for. |
| A q·α limit | The engine has no q·α kill. 8° at 123 kPa is about 17 kPa·rad, and staging into the airflow is already held. |
| Ignition delay after a hot stage | The shipped stack never stages hot in flight. Build a stack that does first, and read its peak load. |
| Profile or deadband tuning for the lag | The deadband is 0.2° against a 12° lag, and the rate limit is not what binds. |

## Off this topic, found on the way

`Ksa/AeroTables.cs` keeps the engine's force and moment per q and drops `SurfaceLoads`' two bounds. The engine applies
both:
- it divides the torque by `1 + ‖I⁻¹‖·dt·q·(½·dt·MomentStiffnessBound + MomentDampingBound/V)` (`PhysicsStates.cs:1201`);
- it saturates the force by `1/(1 + F·dt/(m·V))` (`:1195`).

So `Sim/AttitudeTrack.cs` over-predicts a turning target's torque wherever the limiter bites, by an amount that depends
on the frame time. That mod measured a factor of 3.7 on a booster at 6–7 kPa. The fix is to store both bounds as
channels and apply the engine's divisor with the step's dt, with a test that varies dt. It bears on the gun's lead, not
the ballistic computer.

## What is not worth taking

- **Private writes.** They set `Vehicle._manualControlInputs` through reflection for throttle and ignition. Our throttle
  servo is slower and is a public call. The cost of theirs is a KSA update that compiles and stops steering.
- **The impact model.** Theirs aims at one sphere at the site's terrain height. Ours flies the warhead against the height
  field.
- **Their warp handling.** It warps on a predicted burn point and prompts first. Ours hands the wait to KSA's own warp in
  hops, owns it only while `Universe.AutoWarpTime` is the end it set, and ends it itself.
