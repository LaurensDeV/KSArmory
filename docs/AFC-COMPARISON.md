# What AdvancedFlightComputer does that the ballistic computer could take

**A plan, not a record.** Drawn up on 2026-10-12 by reading
[KSA-AdvancedFlightComputer](https://github.com/Maximilian-Nesslauer/KSA-AdvancedFlightComputer) at commit
`4cfdfe5` (2026-10-10, KSA `2026.10.10.5554`) beside `docs/ICBM-GUIDANCE.md` and the code under `Sim/Icbm/` and
`Ksa/Icbm/`. Nothing here has been built or flown. Every item says what was read and what would have to be
measured before it is worth building; the live ranking of ballistic work stays `docs/ICBM-OUTSTANDING.md`, and an
item moves there when somebody picks it up.

That mod has no ballistic guidance. What overlaps is its ascent to orbit (UPFG, from cairn5's PoweredGuidance) and
its booster boostback, which steers a drag-integrated impact point onto a landing site. Its own docs say only the
single-craft ascent has been flown in game from that tree, so its numbers below are headless ones.

## Ranked

| # | Item | Why it matters here | Cost | Evidence so far |
| --- | --- | --- | --- | --- |
| 1 | Refresh the stage model ourselves | Stack and stage delta-v are stale for every rocket but the controlled one | small | traced in both sources, not flown |
| 2 | Read every remaining stage, by phase | Cutoff timing and reach are priced on the running stage alone | medium | read |
| 3 | A turn-rate feedforward on the attitude command | The attitude lags its command; a bus asked for an offset hunted | medium, needs a private patch | read |
| 4 | Staging that knows what the next sequence is | A stage that lights hot is lost; the next sequence may not be an engine | medium | read |
| 5 | Stop when the player takes the attitude | The command is rewritten every frame whatever the player does | small | read |
| 6 | A drag table against Mach for the ascent and the rounds | Five rounds still carry hand-typed drag; KSA's aerodynamics are about to change | medium | read |
| 7 | Steering over the whole burn, not along velocity-to-gain | Propellant, on a shot near the edge of reach | large | read; no measurement of what ours loses |
| 8 | Exact sensitivities through the predictor | Ours are flown differences; fine today, slower and noisier at the quantum | large | read |
| 9 | Reference fixtures for the solvers | Lambert and Kepler are checked against flights, not an independent implementation | small | read |

## 1. Refresh the stage model ourselves

**What is true in the engine.** `SequencePerformanceList.RecomputeForFlight` is called from
`PhysicsBubble.cs:813` (KSA `2026.10.19.5638`), behind `vehicleState.IsControlled && (ResourceGroups.IsOpen ||
Program.EngineControlGaugeOpen)`. Since 5638 `UpdateStagedBurn` (`:833`) also calls it, for a craft that is
controlled or has a burn plan, when it is replanning with an engine thrusting; a computer-flown rocket has no
burn plan, so this was not re-checked against the conclusion below. So the figures `IcbmComputer.StackDeltaV` and `RunningStageDeltaV` read are
refreshed only for the controlled craft, and only while the staging list or the engine-control gauge is open. The
method is public, and its body reads the part tree alone: the sequence list, the live tank masses, each engine
controller's `IsActive`. Nothing in it names the controlled craft.

**What they do.** `RefreshStageModel` (`Modes/Autopilot.cs`) calls `RecomputeForFlight(ambientPressure)` on each
guided vehicle every 250 ms of wall clock, from their `Vehicle.PrepareWorker` prefix, and keeps the result on the
vehicle it describes. Their comment says the game's own recompute can run on a vehicle worker thread, and that the
prefix is safe because the solver job has been waited on by then.

**What it would change here.** `docs/ICBM-GUIDANCE.md` "Not done" and `docs/ICBM-OUTSTANDING.md` both say KSA
reports stage delta-v for the controlled craft alone. The narrower statement is that KSA only refreshes it there.
That is why a shot short of propellant is flown and reported rather than refused, why `AimWaitsForTheSolids` works
on seat 1 only, and it also means the reading on the controlled craft is stale with both panels closed.

**Next step: a diagnostic, not a fix.** Call the recompute from `AttitudeHook`'s window for a rocket nobody is
flying and log `TotalDeltaV` and `FindActiveSequenceDeltaV` beside what is read today, across a staging. Wall-clock
gated, as theirs is, because under warp a sim-time gate runs every step. If the numbers hold, the three doc lines
and the comment above `AimWaitsForTheSolids` change in the same commit as the fix.

## 2. Read every remaining stage, by phase

`Sim/BoosterPerformance.cs` is four numbers about the stage that is burning. Their `Upfg/KsaVehicleAdapter.cs`
builds one constant-thrust stage per `SequencePerformance.Phases` entry, so solids flaming out under a core are a
phase boundary rather than a surprise, and scales only the burning stage to ambient pressure. Three things in it
are worth knowing before reading that list ourselves:

- Always read from index 0. The recompute drains its simulated tanks forward through the list, and a spent
  sequence carries no phases.
- The headline `Thrust` and `MassFlowRate` are phase 0's, and for the active sequence they are the live throttled
  values while `DeltaV` is from design conditions. The two must not be combined.
- A solid's grain is not in a tank, and the game paces it at the mean flow of the burn it has left.

What it would buy is a `timeToCutoff` and a reach that know the upper stages exist. It depends on item 1.

## 3. A turn-rate feedforward on the attitude command

We command a direction in `EclBody` precisely so that no feedforward is wanted (`Ksa/VehicleCommand.cs`). That
holds for a fixed line and not for a line that is turning: through the pitch programme and the closed loop the
commanded direction moves, and the flight computer chases it from behind. They evaluate the steering law every
step between solves and publish the rate it implies; a postfix on `FlightComputer.UpdateAttitudeTarget` adds it to
`AttitudeTarget.RatesCci` on the worker.

**The cost is the patch.** `UpdateAttitudeTarget` is private, so this is the `PreRenderHook` kind of risk and not
the `AttitudeHook` kind: no signature to pin. Their rule that a stale rate must be cleared on release applies, since
a rate left behind is a standing instruction to keep turning.

**Measure first.** Log the angle between the commanded and held direction through a pitch programme and through a
bus re-point. If the lag is a degree or two the patch is not worth another hook.

## 4. Staging that knows what the next sequence is

`ActivateNextSequence` fires whatever the player put next, and a stage that lights hot is still lost to the
throttle servo (`docs/ICBM-GUIDANCE.md`, *The cap is right and the actuator is too slow*). Their AutoStage reads
the sequence before firing it: it will not stage away the last control module, stages again when a sequence holds
only decouplers, drops spent boosters only when no retained engine can still draw from them and no enabled fuel
link crosses the separation, and holds an engine's ignition for a per-part delay after the decoupler.

The delay is the piece that bears on the hot stage: lighting the upper stage a second or two after the split, with
the throttle already brought down, is the cut our doc describes without the dry-stage reading it worries about.
Unverified. `JettisonAnalysis.cs` and `StagingDetector.cs` are the files to read.

## 5. Stop when the player takes the attitude

They record the attitude command they last wrote and compare it against the flight computer on every step. A
mismatch means a player or another mod is steering, so guidance stops, keeps the player's fields, and restores only
what it replaced. They also count failed steps and release the craft after a run of them, leaving the last command
in place because an engine cut in the air costs more than one bad step.

Here `VehicleCommand.TryAim` rewrites the target every frame, and the only way out is to disarm. Whether that is a
fault is a design question: a launch cannot be taken back, and `Armed` is the switch. It is listed because a player
who grabs the attitude mid-ascent today fights the computer without being told why.

## 6. A drag table against Mach

They sample the game's own aerodynamics once onto a Mach and angle-of-attack grid and fit a spline, spaced finely
through 0.85–1.30 for the day the game's drag stops being flat in Mach. Ours is one coefficient per round, five
rounds are still typed by hand (`ArsenalTests.EveryRoundsDragComesFromWhatItIsUnlessNamedHere`), and
`Sim/DragShape.cs` is a copy of today's `PhysicsStates.ComputeDrag` that nothing fails on when the engine's changes.
Sampling the engine instead of copying it is the part worth taking; `Adapters/KsaAeroSweep.cs` is how they do it.

## 7. Steering over the whole burn

UPFG solves a linear-tangent steering law across every remaining stage; we fly a square-root pitch schedule and
then thrust along velocity-to-be-gained. Ours is robust by construction and costs propellant. Nobody has measured
how much, so the first step is a number: the delta-v a flown shot spends against the Lambert requirement at
handover, on the stacks in `docs/ICBM-OUTSTANDING.md` that fall short or barely reach. If that is a few percent the
item closes.

One finding of theirs applies to any such work: UPFG called every sim step wound its integrator up sixty times
faster than it is damped for. They solve once a second and evaluate the law every step.

## 8. Exact sensitivities through the predictor

`ReleaseFocus.FlownSensitivity` flies the predictor seven times at a 1 m and a small velocity step and differences
the landings. Their predictor carries dual numbers through the integrator, so one sweep per input gives the
derivative exactly, including how the time of flight changes with the input: in their example that term is half the
answer, and they land on the surface by Newton in duals to get it. Our differenced landings carry that term
already, because the two flights arrive at their own instants. So this is about cost and noise at the millimetre
quantum, not about a missing term, and it would mean a second numeric type through `Slug` and `Medium`.

## 9. Reference fixtures

Their solvers are checked against Python reference implementations kept in the test tree. `Sim/Lambert.cs` and
`Sim/Kepler.cs` are checked against each other and against flown arcs. A table of published Lambert cases
(Vallado's, or a short script's output committed beside the test) would be the first check that does not share an
author with the code.

## What is not worth taking

- **Private writes.** They set `Vehicle._manualControlInputs` through reflection for throttle and ignition. Our
  throttle servo is slower and is a public call; the cost of theirs is a KSA update that compiles and stops
  steering.
- **The impact model.** Theirs holds a fixed angle of attack through the coast and aims at one sphere at the site's
  terrain height. Ours flies the warhead against the height field.
- **Asking before warping.** Their prompt is right for a mod that warps on a predicted burn point. Ours hands the
  wait to KSA's own warp in hops and ends it itself.
