# What another flight computer does that the ballistic computer could take

**A plan, not a record.** The first version was drawn up on 2026-10-12. It was redrawn on 2026-11-21 against that
mod's tree at `f36ba18`, which targets KSA `2026.11.16.5901`, 454 commits after the first read, beside
`docs/ICBM-GUIDANCE.md` and the code under `Sim/Icbm/` and `Ksa/Icbm/`. Engine facts were checked against the 5901
decompiled corpus.

Nothing here has been built or flown. Each item says what would have to be measured before it is worth building.
The live ranking of ballistic work stays in `docs/ICBM-OUTSTANDING.md`, and an item moves there when somebody picks
it up.

That mod is a general flight computer for KSA, and it has no ballistic guidance. Four things it does overlap with
ours:
- its ascent to orbit, UPFG plus a convex (SCvx) ascent planner;
- its booster boostback and entry glide, which steer a drag-integrated impact point onto a site;
- an attitude controller of its own, which replaces KSA's on 5901;
- an RCS allocator.

Its numbers below are from a headless harness of its own unless they say otherwise.

## Ranked

| # | Item | Why it matters here | Cost | Evidence so far |
| --- | --- | --- | --- | --- |
| 1 | Guard the 200 kPa breakup limit | The ascent caps g and nothing caps dynamic pressure | small | engine read |
| 2 | Refresh the stage model ourselves | Stack and stage delta-v are stale for every rocket but the controlled one | small | traced in both sources, not flown |
| 3 | Fly the stack's own drag through an in-air coast | The bus alone owed the trim 287 m/s of drag at 500 km | medium | our flown logs |
| 4 | Read every remaining stage, by phase and throttle floor | Cutoff timing and reach are priced on the running stage alone | medium | read |
| 5 | An attitude controller that knows the air's moment | On 5901 the air turns the stack and KSA's controller does not know it | large | read; theirs measured headless |
| 6 | Hand back when the craft cannot be controlled | The computer never asks `IsControllable` | small | engine read |
| 7 | Staging that knows what the next sequence is | A stage that lights hot is lost; the next sequence may not be an engine | medium | read |
| 8 | Stop when the player takes the attitude | The command is rewritten every frame whatever the player does | small | read |
| 9 | An RCS allocator for the bus's turns and holds | Jets are what a long hold and a turn spend | large | read |
| 10 | Solve off the frame thread | Frame time is what sets the simulation rate | medium | read |
| 11 | Steering over the whole burn | Propellant, on a shot near the edge of reach | large | read; no measurement of what ours loses |
| 12 | Exact sensitivities through the predictor | Ours are flown differences: slower and noisier at the quantum | large | read |
| 13 | Reference fixtures for the solvers | Lambert and Kepler are not checked against an independent implementation | small | read |
| 14 | Fixed-step flights in the real game | Night results move with the frame rate | large | read |

## 1. Guard the 200 kPa breakup limit

**What is true in the engine.**
- `VehicleStructuralLimits.MaxDynamicPressure` is 200,000 Pa.
- `PhysicsBubble.cs:1250-1253` destroys the vehicle as `AerodynamicForces` when `StructuralLoad.DynamicPressureFraction`
  reaches one.
- Unlike the g-load beside it, the pressure is not lagged. It is the peak of ½ρv² over the step, against the denser of
  the air and the ocean.

**What they do.** They plan the ascent to 98% of that constant, read from the engine rather than typed. Their default
ceiling is 195 kPa, and they also limit q·α to 3,500 Pa·rad.

**What it would change here.** `IcbmProgram.AccelerationCapGee` guards the load factor (`docs/ICBM-GUIDANCE.md`, *The
acceleration limit is the airframe's*), and nothing guards q. The computer logs q at a few events and never its peak.

**Next step: a diagnostic.** Log `StructuralLoad.PeakDynamicPressure` beside the g-load the ascent already prints,
across the existing scenario saves. A flat, fast stack on a short shot is the case at risk. Build a throttle cap in
`Sim/Icbm/IcbmProgram.Limits.cs` only if a stack comes within reach of the limit.

## 2. Refresh the stage model ourselves

**What is true in the engine.**
- `SequencePerformanceList.RecomputeForFlight` is called from `PhysicsBubble.cs:955` (5901). It is gated on
  `vehicleState.IsControlled && (ResourceGroups.IsOpen || Program.EngineControlGaugeOpen)`.
- `UpdateStagedBurn` (`:977`) also calls it, for a craft that has a burn plan. A computer-flown rocket has none.
- So the figures that `IcbmComputer.StackDeltaV` and `RunningStageDeltaV` read are refreshed only for the controlled
  craft, and only while the staging list or the engine-control gauge is open.
- The method is public. Its body reads only the part tree: the sequence list, the live tank masses, and each engine
  controller's `IsActive`.

**What they do.** They call `RecomputeForFlight(ambientPressure)` on each guided vehicle from a `Vehicle.PrepareWorker`
prefix, every 250 ms of wall clock, and keep the result on the vehicle it describes. A dirty flag forces an immediate
refresh after a staging. The prefix is safe because the solver job has been waited on by then.

**What it would change here.** Our docs say KSA reports stage delta-v for the controlled craft alone. The narrower
truth is that KSA only refreshes it there. That one fact explains three things:
- a shot short of propellant is flown and reported rather than refused;
- `AimWaitsForTheSolids` works on seat 1 only;
- the controlled craft's own reading is stale whenever both panels are closed.

**Next step: a diagnostic, not a fix.** Call the recompute from `AttitudeHook`'s window for a rocket nobody is flying.
Log `TotalDeltaV` and `FindActiveSequenceDeltaV` beside today's readings, across a staging. Gate it on wall clock, as
they do, because under warp a sim-time gate runs every step.

## 3. Fly the stack's own drag through an in-air coast

At 500 km the bus coasting alone through the air owed the trim 287 m/s of drag and landed 107 km out
(`docs/SHORT-RANGE.md`, `docs/ICBM-GUIDANCE.md` "Finish in the air"). The predictor flies the warhead's drag and
never the stack's or the bus's.

**What they do.** They sample the engine's own loads into body-axis force and moment tables over ±20° by passing the
part tree to `PhysicsStates.ComputeDerivatives`. They also sample each booster as the vehicle it becomes after a
separation, which they build from the split's root axes and `VehicleProperties.RecomputeAerodynamicProperties`.

**What it would change here.** `Ksa/AeroTables.cs` already samples a craft's loads for the gun's lead. What is
missing is feeding the stack's table into the coast that `ImpactPredictor` and `BurnoutGuidance` fly while there is
air.

**Measure first.** Work out from that night's logs how much of the 107 km the bus's drag accounts for, flown through
the table.

## 4. Read every remaining stage, by phase and throttle floor

`Sim/BoosterPerformance.cs` is four numbers about the stage that is burning. They build one constant-thrust stage per
`SequencePerformance.Phases` entry, so solids flaming out under a core are a phase boundary rather than a surprise.
Only the burning stage is scaled to ambient pressure.

Since the first read, each phase also carries its throttle floor. They get it by transpiling the private
`SequencePerformanceList.RunDrainSimulation` to record the active cores and `Rocket.MinimumThrottle`, which 5901 moved
onto `<Rocket>`. Stages with different floors are not merged. That bears on `PulsesBelowTheFloor`.

Before reading that list ourselves:
- Always read from index 0. The recompute drains its simulated tanks forward through the list, and a spent sequence
  carries no phases.
- For the active sequence, the headline `Thrust` and `MassFlowRate` are phase 0's live throttled values, while
  `DeltaV` is from design conditions. Do not combine the two.
- A solid's grain is not in a tank. The game paces it at the mean flow of the burn it has left.

What it would buy is a `timeToCutoff` and a reach that know the upper stages exist. It depends on item 2. Reading
the floor without a transpiler, from the parts, is the version worth trying first.

## 5. An attitude controller that knows the air's moment

**What they do.** Since 5901 their ascent and their entry glide no longer use KSA's attitude controller:
- KSA's controller is left in Manual.
- Their own PD runs at 2 rad/s, with a slow integral, a capture-rate cap and a gyroscopic term.
- It feeds forward the air's moment from their body-axis table at the Mach, α and β being flown.
- The torque goes to the gimbals first, then the fins, then RCS.
- It is written in a postfix on `FlightComputer.ComputeControl`, on the worker. The gimbals go through
  `outputs.Gimbals`, and the RCS pulses go into `ThrusterControllerState.CommandPulseTime` after stock has written
  them.

**What they measured.** Their harness runs on the real simulation. Holding a 3° tilt cost 163 kg of RCS on stock
against 46 kg on theirs. On a gliding booster, stock left about a degree of standing error and burned 11.9 kg/s of
RCS, against 1.1 kg/s on theirs.

**What it would change here.** We command a direction in `EclBody` precisely so that no feedforward is needed
(`Ksa/VehicleCommand.cs`). That holds for a fixed line in still air. It does not hold for a turning line, or for air
that pushes back, and 5901's air does push back: `docs/ICBM-GUIDANCE.md` "Short shots" already records that a short
shot steers at any angle of attack.

The cheaper half is the old turn-rate feedforward. They still use it outside the ascent and the glide: a postfix on
the private `FlightComputer.UpdateAttitudeTarget` adds the steering law's rate to `AttitudeTarget.RatesCci`, and a
stale rate is cleared on release.

**The cost is the patch.** Both methods are private. That makes this the `PreRenderHook` kind of risk, with no
signature to pin. It is worse than `PreRenderHook`, though: losing the patch here mid-flight leaves a craft in
Manual with nobody steering, so it cannot degrade to the status quo.

**Measure first.** Log the angle between the commanded and held direction through max q, and through a bus
re-point, on 5901. If the lag is a degree or two, neither patch is worth a hook.

## 6. Hand back when the craft cannot be controlled

`Vehicle.UpdateControl` releases the flight computer and forces 1x when a probe core is unpowered or failed. They
cancel guidance on `!vehicle.IsControllable`, gate their worker writes on it, and forecast how long probe power has
left. The ballistic computer asks none of this, so a bus whose power runs out mid-coast goes on being commanded with
nothing listening, and nobody is told why.

**Measure first.** Check whether the bus's power lasts a 12,900 km coast with its jets firing.

They also stand down when KSA's flight-software prototype (`KSA.Kfs.KfsPrototype.Enabled`, switched on by
`KFS_PROTOTYPE=1`) owns the craft. Ours would have to as well, if that prototype ever ships switched on.

## 7. Staging that knows what the next sequence is

`ActivateNextSequence` fires whatever the player put next, and a stage that lights hot is still lost to the throttle
servo (`docs/ICBM-GUIDANCE.md`, *The cap is right and the actuator is too slow*). Their automatic staging reads the
sequence before firing it. It:
- will not stage away the last control module, and refuses any separation that would leave the retained parts
  uncontrollable, judged with `Vehicle.ComputeControl` over the retained selection;
- stages again when a sequence holds only decouplers;
- drops spent boosters only when no retained engine can still draw from them, counting only engines as crossfeed;
- holds the next row until every separating root has left the tree, plus a second, with a 2.5 s deadline;
- holds an engine's ignition for a per-part delay after the decoupler.

The ignition delay bears on the hot stage. Lighting the upper stage a second or two after the split, with the
throttle already down, is the cut our doc describes, without the dry-stage reading it worries about. Unverified. It
also bears on `IcbmComputer.StagingWouldDropTheLauncher`.

## 8. Stop when the player takes the attitude

They record the attitude command they last wrote and compare it against the flight computer on every step. A
mismatch means a player or another mod is steering, so guidance stops, keeps the player's fields, and restores only
what it replaced, including the deadband and rate limit that it changed around `SetAttitudeProfile`. They also count
failed steps and release the craft after a run of them. The last command is left in place, because cutting the engine
in the air costs more than one bad step.

Here `VehicleCommand.TryAim` rewrites the target every frame, and disarming is the only way out. Whether that is a
fault is a design question: a launch cannot be taken back, and `Armed` is the switch. It is listed because a player
who grabs the attitude mid-ascent today fights the computer without being told why.

## 9. An RCS allocator for the bus's turns and holds

They solve a linear programme (Bergmann–Weiler) over each thruster's live wrench. It minimises propellant against the
torque asked for, with a priced shortfall so that pitch and yaw come before roll. Each thruster's duty becomes a
pulse on a 0.1 s period, and a pulse shorter than the thruster's minimum carries forward to the next period.

They leave the side force free. Ours would have to constrain it, so that a turn does not translate the bus after
`BusTrim` has nulled the split. It bears on `Sim/BusTurn.cs`, on `Sim/HoldPointing.cs`'s rule that pointing on the
jets empties them, and on `tools/model/checkring.py`'s quantum. It needs the same worker postfix as item 5.

**Measure first, from flown logs.** The jet propellant each turn and hold spends, and the translation a turn leaves
for the trim to null.

## 10. Solve off the frame thread

On the shared thread pool, their 40 ms entry re-solves sat queued for up to 3.6 s behind the game's own work. So they
moved the solves, and the aero sweep, onto threads of their own. The sweep alone had been costing them stalls of
about a second on the frame thread.

Every solve of ours runs on the frame thread, and frame time is the only thing that buys simulation rate
(`Sim/StageDisposal.cs`). Nothing here is known to be slow. Read `FrameBudget`'s solve share on a long shot before
weighing what a worker's latency would cost the cutoff countdown.

## 11. Steering over the whole burn

They solve UPFG's linear-tangent steering across every remaining stage, and now also plan the ascent as a convex
programme with q and q·α bounds and lift. We fly a square-root pitch schedule and then thrust along
velocity-to-be-gained. Ours is robust by construction and costs propellant.

Nobody has measured how much it costs. So the first step is a number: the delta-v a flown shot spends against the
Lambert requirement at handover, on the stacks in `docs/ICBM-OUTSTANDING.md` that fall short or barely reach. If that
is a few percent, the item closes.

One finding of theirs applies to any such work. UPFG called every sim step wound its integrator up sixty times faster
than it is damped for, so they solve once a second and evaluate the law every step.

## 12. Exact sensitivities through the predictor

`ReleaseFocus.FlownSensitivity` flies the predictor seven times, at a 1 m step and a small velocity step, and
differences the landings. Their predictor carries dual numbers through the integrator, so one sweep per input gives
the exact derivative, including how the time of flight changes with the input.

Our differenced landings already carry that term, because each flight arrives at its own instant. So this item is
about cost and noise at the millimetre quantum, not about a missing term. It would mean a second numeric type running
through `Slug` and `Medium`.

## 13. Reference fixtures

Their solvers are checked against Python reference implementations kept in the test tree, which now include their
three-degree-of-freedom flight model. `Sim/Lambert.cs` and `Sim/Kepler.cs` are checked only against each other and
against flown arcs. A table of published Lambert cases, Vallado's or a short script's output committed beside the
test, would be the first check that does not share an author with the code.

## 14. Fixed-step flights in the real game

They test against the real simulation with no window and no GPU, stepping it from the test. One trial holds both arms,
stock against theirs, in the same run. A flight stepped that way would take the frame rate out of a night's result,
which we know moves with it.

Their warning: a long run with `Vehicle.PrepareWorker` unpatched let the JIT inline it, and a later patch then never
ran. Before anything else, find out whether such a driver reaches this mod's step at all: the GUI pass, `PreRenderHook`
and `AttitudeHook`.

## Off this topic, found on the way

The engine divides the aerodynamic torque by `1 + I⁻¹·dt·q·(½·dt·MomentStiffnessBound + MomentDampingBound/V)`
(`PhysicsStates.cs:1201`). They measured that this divides the moment by 3.7 on a booster at 6–7 kPa, and now carry
the term. `Ksa/AeroTables.cs` takes `MomentPerDynamicPressure` from `ComputeLoads` undivided. So the gun lead's turn
may overstate the moment, which is worth one check against a flown turn.

## What is not worth taking

- **Private writes.** They set `Vehicle._manualControlInputs` through reflection for throttle and ignition. Our
  throttle servo is slower and is a public call. The cost of theirs is a KSA update that compiles and stops steering.
- **The impact model.** Theirs aims at one sphere at the site's terrain height. Ours flies the warhead against the
  height field.
- **Their warp handling.** Their warp to a burn and their prompt before it suit a mod that warps on a predicted burn
  point. Ours hands the wait to KSA's own warp in hops, owns it only while `Universe.AutoWarpTime` is the end it set,
  and ends it itself.
