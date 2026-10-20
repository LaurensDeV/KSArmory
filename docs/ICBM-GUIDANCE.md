# The ballistic computer

**The reference for how the ballistic computer and the MIRV bus work now.** What is still to do, ranked, is
`docs/ICBM-OUTSTANDING.md`. The records of how it got here are `docs/ACCURACY-PLAN.md` (which also holds the
sections retired from this file, under "Moved from ICBM-GUIDANCE"), `docs/MIRV-NEXT.md`, `docs/METRE-LEVEL.md`,
and the flight records in `docs/MIRV-TARGETS.md` and `docs/SHORT-RANGE.md`. `docs/ARRIVAL-ANGLE.md`,
`docs/KINETIC-FLOOR.md`, `docs/SHOT-PROTOCOL.md` and `docs/GUIDANCE-SECTION.md` each take one question further.

**What it does.** Fly any rocket a player has built, from a standing start on the pad to a place
they picked on a map, and let the warheads go over it. Nothing about the vehicle is declared to it:
it reads how much thrust the stack has, how fast it is consuming itself and how much is left, every
cycle, and fires the next sequence when there is nothing to burn with — which on the pad is
the ignition and afterwards is the spent stage.

**Where it lives.** The guidance is under `Sim/` and tested headlessly. `Ksa/Icbm/` turns the world
into an `IcbmState` and the answer into writes on somebody else's vehicle, and carries what only the
game can answer: staging, separation, the trim's jets, the release and the warp.

**One computer per craft, not per launcher.** A craft can carry two rails and shoot them at different
things, but it has exactly one trajectory, so a second computer aboard is a second autopilot fighting
the first for the same engines. That is the one place `Ksa/Icbm/IcbmComputers.cs` and `WeaponSystems`
disagree about what to key on.

## Contents

- **Frames and the solve**: [The algorithm, and why this one](#the-algorithm-and-why-this-one) · [Frames](#frames) · [Flight time is the parameter, and the arrival time gets latched](#flight-time-is-the-parameter-and-the-arrival-time-gets-latched) · [Arrival angle is asked for, not read off](#arrival-angle-is-asked-for-not-read-off) · [Nothing is flown open loop](#nothing-is-flown-open-loop) · [The prediction flies the warhead, not the bus](#the-prediction-flies-the-warhead-not-the-bus) · [The prediction is of the warhead's state, not the bus's](#the-prediction-is-of-the-warheads-state-not-the-buss) · [A stable orbit is known not to come down](#a-stable-orbit-is-known-not-to-come-down) · [The aim is corrected by what the flown prediction loses](#the-aim-is-corrected-by-what-the-flown-prediction-loses)
- **The ascent**: [The ascent](#the-ascent) · [It picks up from wherever the vehicle already is](#it-picks-up-from-wherever-the-vehicle-already-is) · [Lighting the first engine is a stage request](#lighting-the-first-engine-is-a-stage-request) · [The acceleration limit is the airframe's, not the operator's](#the-acceleration-limit-is-the-airframes-not-the-operators) · [What it commands, and what it does not](#what-it-commands-and-what-it-does-not) · [Roll, and the direction that has no answer](#roll-and-the-direction-that-has-no-answer) · [Attitude is driven for every phase that is doing something](#attitude-is-driven-for-every-phase-that-is-doing-something)
- **Cutoff**: [Cutting off is a timing problem](#cutting-off-is-a-timing-problem) · [Stop the burn along the line it is actually thrusting](#stop-the-burn-along-the-line-it-is-actually-thrusting) · [Hold the direction frames before cutoff, not seconds](#hold-the-direction-frames-before-cutoff-not-seconds) · [The attitude at cutoff is held, not solved](#the-attitude-at-cutoff-is-held-not-solved) · [Measured](#measured) · [Timewarp is held down for a burn](#timewarp-is-held-down-for-a-burn)
- **The hold and its window**: [When to burn is a separate question from how](#when-to-burn-is-a-separate-question-from-how) · [The wait is handed to the game, and taken back carefully](#the-wait-is-handed-to-the-game-and-taken-back-carefully)
- **The coast and the warp**: [Waiting is warped through, and it is the same wait twice](#waiting-is-warped-through-and-it-is-the-same-wait-twice) · [The world slows itself for the entry, not for the coast](#the-world-slows-itself-for-the-entry-not-for-the-coast)
- **The trim**: [Separating first, when the part tree offers a joint](#separating-first-when-the-part-tree-offers-a-joint) · [The split costs a metre a second, and only the bus can pay it back](#the-split-costs-a-metre-a-second-and-only-the-bus-can-pay-it-back)
- **Release and the kick**: [Each warhead is kicked off its own release probe](#each-warhead-is-kicked-off-its-own-release-probe) · [Each tube is aimed before it fires](#each-tube-is-aimed-before-it-fires) · [A tumbling bus does not deploy](#a-tumbling-bus-does-not-deploy) · [The ground a round meets](#the-ground-a-round-meets)
- **Several targets**: [Several targets](#several-targets)
- **Short shots**: [Short shots](#short-shots)
- **Constants that carry the frame**: [Constants that carry the frame](#constants-that-carry-the-frame)
- **Operating it**: [What it tells the operator](#what-it-tells-the-operator) · [Flying one without watching it](#flying-one-without-watching-it) · [Not done, and not verified](#not-done-and-not-verified)

## The algorithm, and why this one

An intercontinental shot decomposes into two problems that want different tools.

**Where must the engines stop?** — solved. A free fall from burnout to a target is Lambert's
problem: two points, a flight time, and a gravitational parameter give the velocity needed at each
end. `Sim/Lambert.cs` solves it in universal variables, by bisection on `psi`. Universal variables
because a depressed shot is genuinely hyperbolic while a lofted one is a tall ellipse, and a solver
that has to be told which is which fails at the boundary between them. Bisection because Newton's
method on this function runs away near the parabolic point.

**How does the rocket get there?** — flown. `Sim/BurnoutGuidance.cs` asks what velocity would put
a free fall on the target *from where the vehicle will be when the engines stop*, subtracts what it
will have by then, and thrusts along the difference until there is none left. Velocity-to-be-gained
guidance: what Titan and Minuteman actually used, and what the geometry here is asking for.

### The property that makes it robust is terminal, not gradual

The required velocity is re-solved against the vehicle's **actual** state a few times a second. So
the shot is exact at the instant the difference reaches zero, *whatever happened on the way there*
— a wrong pitch programme, an engine that underperforms, staging transients, air drag nobody
modelled, a vehicle whose attitude lags its command by five degrees a second. None of it
accumulates, because none of it is remembered.

What a bad ascent costs is propellant, not accuracy. `IcbmFlightTests` measures this directly:
quadrupling the drag and halving the attitude rate changes the ascent completely and moves the
impact by about a hundred metres.

This is also why there is no stored trajectory and no separate launch solver. The same call answers
on the pad and one second before cutoff.

### Numerical prediction beside it, sharing nothing

`Sim/ImpactPredictor.cs` steps the arc under gravity and reports where it comes down. It is the
same choice `BombSight` makes at a thousandth of the range and for the same reason: it can be flown
against the real height field, where the conic maths assumes a sphere.

Keeping the two apart is the point. A guidance loop that predicts with the same maths it steers by
can only ever agree with itself; this one can contradict it, and when the readout and the plan
disagree the plan is the thing that is wrong.

## Frames

**Everything is in the parent body's inertial frame — `Cci`, not `Ecl`.** A ballistic flight lasts
half an hour, over which the ecliptic carries ~54 million kilometres of the planet's own travel
through every term. `Cci` has that subtracted exactly rather than approximately, and it is the
frame KSA's own orbital mechanics are written in. `docs/FRAMES-AND-EPOCHS.md` has what happens
otherwise.

**A body's spin axis is exactly `+Z` in its own `Cci`.** KSA builds `Ccf` from `Cci` by rotating
about `UnitZ` and nothing else (`Celestial.GetCcf2Cci`), so there is no obliquity term to carry.
It is the *ecliptic* that sees the tilt.

**The aim point is carried forward, twice, and both matter.**

- By the flight time, because a place on the ground is a moving target: Earth turns 7.5 degrees
  over a half-hour flight, which is 830 km at the equator. `BallisticArc.TrySolve` does this, and
  it is deliberately not the caller's job — handing in an already-carried point applies it twice.
- By the *time still to burn*, because the arc departs at cutoff rather than now. Forty-odd
  kilometres a hundred seconds before cutoff. The loop converges anyway, since the term goes to
  zero as the burn ends, which is exactly why it is easy to leave out and never notice.

**Naming a place is a mode, not a button.** `Ksa/SiteDesignator.cs` arms and then takes a world
click. A button cannot do this job: pressing one puts the cursor over the panel, so what it samples
is whatever lies behind the control — silently, plausibly, and never where the player was pointing.
A ring follows the cursor while the tool is armed and greys out over a body the shot cannot reach,
which is the answer to "why did my click do nothing" given before the click rather than after it.

**The target is a latitude and a longitude, never a position.** `Sim/AimSite.cs`. The same rule
`AimpointKind.Ground` exists for, at a thousand times the flight time.

**And it is aimed at the real ground, not the mean sphere.** The solve is a transfer between two
*points*, so a target standing five kilometres up is aimed at where it stands. That is necessary
and not sufficient: a round stops at the ground rather than at a point, which the solver has no way
to know — see *The aim is corrected by what the flown prediction loses*.

## Flight time is the parameter, and the arrival time gets latched

The search is parameterised by **flight time** rather than by energy or launch angle, because that
is the one parameter that makes a rotating target tractable: the aim point's position at arrival
depends on how long the flight takes, so choosing the time first collapses a fixed point into a
single solve. Every flight time gives a valid arc; the search over them picks a shot.

`BallisticArc.TryCheapest` minimises the length of the velocity still to gain. That is both the
fuel the shot costs and, mid-boost, exactly what the steering law is driving to zero — so one
search serves the launch decision and the closed loop.

**`Loft` multiplies the cheapest flight time**, and doing that naively is a trap worth naming. The
cheapest arc from the vehicle's *current* state converges on the arc the vehicle is already flying,
so multiplying it by a loft factor every cycle walks the answer outward and the shot chases a
trajectory that runs away from it. Measured: a 1.4 loft flying a 6,335-second arc instead of a
1,749-second one, and landing 162 km out.

Two things fix it, and both are load-bearing:

- The **cheapest** flight time is carried out of the solver separately from the one actually flown,
  and the next search is seeded with the cheapest.
- The **arrival time is latched** the moment closed-loop guidance takes the vehicle. Before that
  the cheapest shot is the right thing to follow, because the state is changing far too much for
  any arrival chosen on the pad to still be the cheapest one.

`IcbmFlightTests.EveryLoftArrivesOnTheTarget` covers 0.85 through 1.4.

## Arrival angle is asked for, not read off

`Loft` moves the flight time and lets the arrival angle fall out. `IcbmConfig.MinArrivalAngleDeg`
does the opposite: it is the shallowest the warheads may come in, and it **bounds the search**
rather than nudging it. A candidate flight time whose arc arrives shallower costs infinity, so the
same golden section returns the cheapest arc that *satisfies* the floor, and `BurnWindow` threading
the same number into its per-departure cost turns "the earliest affordable departure" into "the
earliest affordable departure that satisfies it" with nothing else changed.

**And the bound is re-checked when the arrival latches**, which a loft could not be. Pinning the
arrival *instant* leaves the burn time and the flight time trading against each other inside it; the
cheaper split is the longer coast, and a longer coast onto the same target arrives shallower. So a
15° floor flew **11.42°** until the held arc had to satisfy the bound too. Refusing it hands the
cycle back to the constrained search, which re-commits — one transition, not a thrash, because a
predicate is idempotent.

**Why it had to be a bound rather than a second nudge.** Raising `Loft` makes leaving *now* dearer
as well as making the arc taller, so the window search re-optimises the departure under the new cost
and can defer to a cheap flat one instead — measured, a 556 km shot arriving at 33.9° at loft 1.0 and
**6.2°** at loft 1.8. The operator asked for steeper and got the shallowest arrival available.
`docs/ARRIVAL-ANGLE.md` has why that angle is worth eight times the precision and sixty-two times the
immunity to a drag-model error.

**A predicate is idempotent where a multiplier is not**, which is why the constrained flight time can
be seeded straight back into the next cycle: the arc that satisfied the floor satisfies it again.
That is the trap two paragraphs up, and the floor cannot fall into it.

**Where the two disagree the floor wins.** Loft multiplies whatever the constrained search settled
on, and a lofted time that no longer satisfies the floor is discarded for the constrained one —
loft below one *depresses* the shot, which is exactly the arrival the floor exists to refuse.

**Nothing satisfying it is its own answer.** `IcbmReach.TooShallow` sits beside `NoTrajectory` and
`ShortOfPropellant` because only one of the three is fixed by a control on this panel, and from
outside they are the same silence. Mid-ascent it is a real wall — from a 60 km pick-up a target 90°
round the planet reaches 46° and no further. From orbit it is a price rather than a wall: across a
day of departures every floor short of vertical is satisfiable, for up to the whole orbital velocity.

**Off by default.** `IcbmConfig.ArrivalPreference`, which ships at 0.5, latches a floor of its own on
every flight: half the steepest arrival the tanks can pay for.

## Nothing is flown open loop

`BurnoutGuidance.TrySteer` falls back to the cheapest arc when a **latched arrival** cannot be
solved, rather than returning failure. A pinned arrival pins the transfer angle, and a pinned angle
can land on the one geometry Lambert cannot answer — two points opposite each other about the
centre, where no plane is determined.

Returning failure there leaves the caller holding the previous cycle's answer, which is to say
flying the burn open loop with the velocity still to gain frozen at whatever it was when the trouble
started. Measured, on a half-orbit deorbit: **9,904 km** with the loop stuck, twelve kilometres with
the fallback. Any solve that can fail on some geometry needs an answer for that geometry, not a
`false`.

## The prediction flies the warhead, not the bus

The impact prediction and the round have to be flying the same trajectory, and for a long time they
were not: `ImpactPredictor` integrated gravity alone while the released warhead flew through air.
Above the atmosphere that is the same thing, which is why it survived so long — the bus cuts off at
200 km and everything the guidance reasons about happens up there.

**A deorbit arrival is where it stops being the same thing.** Path length through the atmosphere goes
as `1/sin(γ)`, so a grazing arrival is the *worst* case for drag rather than the mildest. At the ~5°
this shot arrives at, a Mk 21 keeps about a quarter of its speed:

| arrival angle | ground per height | speed left after entry |
| --- | --- | --- |
| 25° — an ICBM lob | 2.1 km/km | 75 % |
| 12° | 4.7 km/km | 56 % |
| **~5° — a deorbit** | **11.4 km/km** | **25 %** |

Measured, same cutoff state through both models: the vacuum arc lands 2,764 km downrange and the
constant-drag round 2,709 — **54.6 km short**. **The shipped round lands at 2,626 km, 138 km short**
(`ShapeDragArrivalTests`), because it flies 3.59x the drag since 2026-09-18. With the warhead's own `DragK` in the predictor
the two agree to **40 m**.

**And the arrival angle is the round's, not the trajectory's.** No brake from any circular orbit puts
a Mk 21 on the ground shallower than **12.0°** — 7.0° was the retired constant's floor — drag bends a graze back up to whatever that round's
sectional density can hold. So the angle every shot here arrives at was never chosen, and what it
costs in precision, impact speed and propellant to leave it is `docs/ARRIVAL-ANGLE.md`.

`ImpactPredictor.Drag` carries the density lookup and the munition, and the acceleration goes
through `Medium.Drag` — the same call the round makes, deliberately, because a prediction that
models drag its own way is a second flight model to keep in step with the first. Airspeed is
measured against the turning air via `BallisticBody.GroundVelocityCci`. Two consequences worth
keeping:

- **The step has to come down in the air.** The coarse step is sized for a vacuum arc where the
  acceleration barely changes across it; entry sheds most of the speed in tens of seconds.
  `AtmosphericStepSeconds` applies only once there is density worth integrating, so a coast pays
  nothing.
- **`DragK = 0` reproduces the vacuum answer exactly**, so nothing above the atmosphere moved.
- **It reads the air alone** (`KsaWorld.AirDensityRatioAt`), as the gun's lay does. The medium reads
  ocean anywhere under the mean sphere, land above it or not, so under 204 m of ground an air step's
  stages read 837x the air there and throw the warhead off the planet — the release probe's only flown
  failure (`docs/ACCURACY-PLAN.md` 3ep).

**The warhead's drag is its own shape's, and it ships with the kick flown through the air.** The Mk 21
is registered with a hand-typed `DragK` of 1.5e-5 — a ballistic coefficient near 8,400 lb/ft² against
the 100 to 5,000 published for reentry vehicles, **known unphysical** — and
`IcbmConfig.WarheadDragFromItsShape` swaps in the drag from its mass, calibre and coefficient; the
constant survives only as the arm a paired night flies against. It is **one change with
`IcbmConfig.KickThroughTheAir`, and neither ships alone**: a warhead at 3.6x the drag leaves 8.8 mm of
the ring's image in its group where the constant leaves 2.1. Together, over 20 paired blocks against a
declared non-inferiority bar, the worst warhead went **0.80x** with a 97.5% upper bound of 0.985 and the
group's spread was lower on 17 of 20 shots (`docs/ACCURACY-PLAN.md` 3eu; 3dk and 3eo are the two
losses that flew one without the other).

**And this is what made the aim correction below inert.** It observes the *prediction*. A difference
the prediction cannot see is a difference no amount of correcting removes — the loop drove the
drag-free prediction onto the target, reported convergence, and the warheads went on landing 59 km
short. Flown. The instrument, not the loop, was the fault.

## The prediction is of the warhead's state, not the bus's

A warhead does not leave on the bus's velocity. Each is ejected along its own tube at
`MunitionProfile.LaunchSpeed`, and a bus's tube cants cancel in the mean — the MIRV bus's six now
point along part `+X` with no cant at all — so what survives is the **whole** of that speed along
the nose.

On a deorbit the nose is held **retrograde**, because that is the attitude the braking burn ended
on. So the ejection slows every warhead and they all fall short together. Measured on this
trajectory: **1.8 km per m/s** purely retrograde, and the radial axis is worth more again
(3.4–4.3 km per m/s), so even the Mk 21's 0.5 m/s off the tube is a couple of kilometres. That
leverage is why the kick is 0.5 rather than the 2 m/s it shipped with: the impact moves 3,979 m per
m/s of it, constant to 0.3% from 0.1 to 2, and dividing the leverage by four divides the tail with
it.

Predicting the bus's arc instead of the round's leaves that entirely invisible to the aim
correction — the same shape as the drag fault above, and found the same way, by measuring what the
prediction and the round disagreed about rather than reasoning about it.

**And the direction has to come from the tubes, not from the commanded attitude.** A vehicle
settles on an attitude command and stops a few degrees off it; the tubes go where the airframe is.
Measured in flight at **ten degrees apart** — the release line reported the tubes at a mean of 71°
from the platform's track while the prediction assumed 81°. Two metres a second misapplied by ten
degrees is 0.35 m/s thrown the wrong way, and radially that is **1,181 m** against a measured
common-mode miss of **1,088 m**. `IManualFire.TryMeanReleaseStateEcl` reads where a round actually
starts and what it actually leaves with, averaged over the tubes, with the cants cancelling in that
mean by construction.

## A stable orbit is known not to come down

The impact prediction is flown, and a vehicle in a stable orbit never arrives — so flying it means
integrating the whole six-hour horizon, several times a second, to reach that conclusion. Which is
precisely the state a computer holding for a burn window sits in.

`Kepler.PeriapsisRadius` answers it from the conic instead, for nothing. The clearance required is
more than any mountain rather than merely positive, because terrain stands above the mean sphere the
conic is measured against.

## The aim is corrected by what the flown prediction loses

The transfer solver is exact, and exact for the wrong thing. It puts the arc through a **point**, and
a round does not stop at a point — it stops where the ground is. On a lofted shot those
are nearly the same. On a shallow arrival they are not remotely: the arc covers about **twelve
kilometres of ground per kilometre of height** near the end, so a target four kilometres up puts the
real impact tens of kilometres from a solution that is otherwise perfect.

Measured, from a near-orbital burnout 2,580 km out:

| target elevation | where the flown arc lands |
| --- | --- |
| 0 m | on the aim |
| 1,000 m | 11.8 km away |
| 4,000 m | 47.9 km away |

Nothing about the trajectory is wrong. It arrives exactly where it was asked to; the asking was
wrong. So the aim carries a **bias**, driven by the difference between where the flown prediction
lands and where the target is, taken at a fraction of the error each cycle because it is a feedback
loop against a solver that then moves the arc.

`Sim/AimCorrection.cs`, so it is testable: the same geometry lands **69.0 km** short uncorrected
and on the target corrected, and `AimCorrectionTests` holds both halves of that pair.

Four things make it honest rather than self-confirming, and the first two were each wrong once:

- **The prediction is of the arc being flown *to*, not of the state being flown *through*.** While
  the engines are burning, the vehicle is nowhere near its cutoff conic, so a prediction from the
  current state measures an arc nobody will fly and the correction it feeds back is meaningless.
  It is flown from `IcbmProgram.CutoffPositionCci` along the solved `RequiredVelocityCci` instead.
  Getting this wrong is invisible in the worst way: the correction still converges after cutoff,
  where the arc is already fixed and the warheads are gone.
- **The corrected aim stays on the target's own radius.** The bias is a free vector and the
  correction is a displacement *along the ground*, so adding it raw walks the aim off the surface
  and asks the solver for an arc to a point underground — which it refuses, and the whole computer
  then has no solution at all.
- The prediction is flown **against the real height field**, not the mean sphere. Without that it
  descends four kilometres past the mountain it was aimed at and reports a miss in the opposite
  direction. Flown: own prediction 60.4 km out, then 9.7 km.
- The miss is scored against the **target**, never against the biased aim. Scoring the correction
  against itself reports a perfect shot however far the rounds actually land.

**A correction loop can only remove what its observer can see**, and the observer has been wrong in
both directions — blind to a drag it did not model (see above), and seeing misses that were not there.
Three more rules come from that:

- **The prediction and the target are read on one clock.** Mid-burn the prediction departs from the
  *cutoff* state, seconds ahead, and `ImpactPredictor` un-carries its impact into the body-fixed frame
  of that instant while the target is known in the frame of *now*. Compared raw, they measure the
  planet's turn over the rest of the burn and call it miss — not a tunable bias, because it shrinks to
  nothing as cutoff arrives, so the correction chases a ruler moving at ~400 m/s against ground moving
  at 465. Headless at 2,000 km it put a shot needing no correction at all 191.6 km wrong; flown, closing
  it took the warheads from 11.25 km to 5.35 km. `TerrainRadiusAt` carries the same un-carry, and once
  the engines are off both reduce to one expression and agree exactly.
- **A prediction departing from inside the air is refused** (`AimCorrection.DepartureIsWorthObserving`,
  at `Medium.NoticeableDensity`, because that is the same question asked of the same model). Before the
  vehicle has flown, the projected cutoff is the pad, so the arc is flown with drag from sea level and
  reads **1,522 km** of miss for an aim that is exactly right. Unrefused, the loop pinned the bias at
  `AimCorrection.MaxMetres` on its first cycle and half the flights could never pay the trim to walk it
  back, landing 300 km out; refused, 8 of 8 landed within 0.33 km against a worst of 310.42.
- **The miss is not monotonic in the aim, so the loop walks past its best**
  (`AimCorrection.WorseBeforeStopping`, 12). At 7,645 km a best of 3.34 km was followed by a five-cycle
  patch out to 5.89 km and then 1.73 km at a bias 38 km further on — 1.15 km of flown miss against
  15.74 for a loop that gave up inside the patch. Stopping early is not conservative: stopping makes
  `IsSteady` true, which commits the arrival, and the 3.34 km it stopped for measured 15.86 km one cycle
  later. `IcbmProgram.LatchArrivalWithinSeconds` bounds it above.
- **Where solids burn out inside the closed loop, the latch waits them out** (`IcbmConfig.LatchWaitsOutTheSolids`).
  The jettison moves the prediction in one cycle -- 1.1 m to 1.57 km on a four-hour coast from orbit -- so a
  window counted from the loop's start latched on the step itself. The window and the steady aim count from the
  burnout instead, the aim forgets a best banked on the solid stage (`AimCorrection.Rebase`), and a steady aim may
  latch only `IcbmProgram.SteadyAfterTheSolidsSeconds` (12 s) later. From ORBIT 300 and ORBIT 800 to 40 N 10 E the
  cutoff came 0.03-0.06 km off its own prediction against 1.0-1.6 without it; a pad shot's solids burn out in the
  ascent, so it never fires there.

**And the best-aim ratchet is dead below 250 m.** `AimCorrection` banks a new best only on beating the
old by `ImprovedByMetres`, a flat 250 m, so once the best is 250 m or less improving on it would take a
negative miss, and the `worse` arm needs 250 m the other way (`AimRatchetTests`). The terminal `Freeze()`
then reverts the bias by a median 110 m — **and the revert moves no warhead**: they leave that same frame
on the trajectory the last trim pass flew, and over 536 flights the revert predicts the release probe at
+0.10. What the dead band did was stop the post-cutoff loop early on `PassesWithoutImprovement`; that loop
now judges its passes on a band that follows the miss wherever `IcbmConfig.ReleaseInsideTheTrimFloor` is on,
which ships (`docs/ACCURACY-PLAN.md` 3cu). The aim loop keeps the flat band: a switch giving it the tracking
one flew 0.88x [0.84, 1.10] on the miss and 0.78x [0.51, 1.11] on the release probe, unresolved, and was
removed (3cp, 3cq).

## The ascent

Closed-loop guidance cannot fly the first minute: its answer near the pad is roughly "point
downrange", which through thick air at increasing speed means flying the stack sideways into its
own slipstream. So `Sim/AscentProfile.cs` runs a schedule — straight up, then a pitch programme
that turns gently enough for the vehicle to follow — and guidance takes over when there is not
enough air left for the difference to matter.

**The handover is on dynamic pressure, not altitude.** It is the thing that actually does the
damage, and it is the same number on every body: a launch from the Moon hands over immediately and
correctly, with nothing having to know the Moon has no air.

**The angle-of-attack limiter is on dynamic pressure too**, and that one is easy to get wrong. Thin
air is not no load: at 35 km a rising stack has a hundredth of sea-level density on it and several
kilopascals, because by then it is doing two kilometres a second. A limiter that opens on density
alone lets go exactly where the vehicle is going fastest.

**The pitch schedule is a square root against altitude**, not a straight line. The vehicle has to
be turned hardest while it is slow and lowest; a linear programme spends the whole upper stage
nearly vertical and then hands guidance an enormous correction.

**There is a horizon floor under everything.** On an airless body the pitch programme is skipped
entirely, so without it the closed loop would take over at 250 m already pointing downhill.

## It picks up from wherever the vehicle already is

There is no assumption that a flight starts on a pad. When the computer is armed it looks at what
the vehicle is doing and joins the sequence at the right place:

| What it finds | Where it starts | Why |
| --- | --- | --- |
| low and not moving over the ground | **Rising** | it has to get off the ground before anything else means anything |
| dynamic pressure still significant | **PitchProgram** | an ascent already under way; the schedule and the angle-of-attack limiter are the same answer |
| above the air | **Holding** | nothing about *how* to burn is in question, only *when* |

So the same computer flies a pad launch, a pick-up halfway up an ascent, a deorbit from a circular
orbit, and a correction to something already on a ballistic arc. `DeorbitTests` covers nine orbital
geometries — off the ground track, from inclined orbits, from 150 km and from 800 km — and they
arrive within two kilometres. The one exception is the grazing entry, held to six: a shallow arc
stays in the atmosphere for thousands of kilometres, so a centimetre a second at cutoff is
kilometres at the far end, and that belongs to the trajectory rather than to guidance.

## Lighting the first engine is a stage request

KSA reports **no thrust and no propellant** for an engine the sequence list has not activated.
`EngineController.ComputeActivePerformance` returns zero unless the module `IsActive`, and
`IsAnyEnginePropellantAvailable` reads a global that only counts active engines — so a rocket
standing on its pad gives exactly the reading a spent stage gives. `MainEngineStartup` does not
change it either: it sets a `_manualControlInputs.EngineOn` bit and nothing more. **Firing the next
sequence is the only thing that lights an engine.**

Ignition and discarding a stage are therefore the same call, and the program tells them apart by
whether anything aboard has *ever* pushed:

| The reading | Which it is | How often it may be asked for |
| --- | --- | --- |
| nothing has ever thrust | **ignition** | again on the cooldown — the sequence a player put first is not necessarily the one with an engine in it |
| something has thrust and now does not | **discard** | once, then the new stage has to prove itself, or one that takes a moment to come up is thrown away on the next cooldown |

`DrySecondsBeforeGivingUp` bounds both, so neither can walk down the sequence list for ever.

**The rig starts unlit** (`IcbmFlightRig.StartsUnlit`), and so does the harness, which does not stage: a first
stage already pushing on frame zero flies every shot whether or not the computer can light one. See [Flying
one without watching it](#flying-one-without-watching-it).

**A shot that never lit says so**, rather than reading as one that ran out of propellant. The whole
velocity is still to gain either way, and the two want completely different things done about them.

`Ksa/Icbm/IcbmComputer.StagingWouldDropTheLauncher` still catches any sequence that would separate the
launcher. With `IcbmConfig.MovesAnEarlyRelease` (on) that stage is moved to the end of the staging
list and the burn goes on; with it off, or where the list refuses the move, the stage is not fired and
the log says so once, because the refusal is otherwise silent.

## The acceleration limit is the airframe's, not the operator's

KSA destroys a vehicle when its load factor reaches a limit it works out from the vehicle's **own
size**: `max(5, 50 x 5/radius)` off the bounding sphere, so a long stack is held to a fraction of
what a stubby one survives — 12.5 g at 20 m, 6 g at 42 m, and a floor of 5. That is not a number an
operator can be expected to know about somebody else's rocket, and it was the whole reason
`MaxAccelerationGee` shipped as a control that did nothing: it defaults to zero, zero meant *no
limit*, and only `tools/scenario.sh` ever set one. A stack armed from the panel flew wide open and
came apart once its boosters were gone and a light upper stage was left on a full-sized motor.

So `IcbmProgram.AccelerationCapGee` takes the **tighter** of two numbers that say different things.
The operator's is about this shot — a stack they know is fragile, or one they want flown gently.
The airframe's is what the engine will actually destroy it at, and it applies whether or not anybody
typed anything. Zero from the engine is the reading being *absent* — a vehicle outside a physics
bubble — which is not the same as there being no limit, and the two are kept apart.

`StructuralMarginFraction` is 0.9 because the test is on a **lag** of the load rather than the load
itself, with a time constant of the bounding sphere over 200 — a fifth of a second at the size
above. Flying at the number is flying on the boundary.

### The cap is right and the actuator is too slow

A stage that lights *hot* is still lost, and the measurement says exactly where the fault is. Both
arms below fly the identical stack — a 1.4 MN booster dropped onto a 1.94 MN upper on 13 t, which is
15 g the instant it lights — against the identical 6.00 g airframe, and only the throttle rate
differs:

| Throttle | Filtered load peak | |
| --- | --- | --- |
| instant | 5.79 g | survives |
| KSA's own 0.7 /s | **7.44 g** | destroyed |

So the cap computes the right setting on the frame the new stage appears, and the servo then takes
about a second to get there. `AStageThatLightsHotIsLostToTheThrottleServoAndNotToTheCap` pins both
arms.

**What is not built, and why it is a flight question.** The remedy has to be an instrument faster
than the throttle, and the game offers exactly one: `MainEngineStartup`/`Shutdown` sets
`_manualControlInputs.EngineOn`, which zeroes `EngineBurnDuration` and so every engine's commanded
throttle in the same frame. Holding it off while the servo catches up would be one cut of about a
second rather than a chatter, because the condition to relight is the *throttle* having arrived
rather than the load having dropped.

What stops it being written headlessly is that `RocketCore.ComputePropellantAvailable` takes an
`isBurning` flag and is abstract — a combustor and a solid motor answer it their own way. If a cut
engine reads as **no propellant available**, the program sees a dry stage and stages away a live one
on the next cooldown, which is far worse than the load it was avoiding.

The cut is now used below a liquid engine's throttle floor (`IcbmConfig.PulsesBelowTheFloor`, on), and
flown that way it staged nothing away (`docs/ICBM-OUTSTANDING.md` 1.11). It is not applied to a stage
that lights hot.

## What it commands, and what it does not

`Ksa/VehicleCommand.cs` is the only place this mod flies somebody else's rocket, and every write in
it is one the game already makes for itself:

| | KSA call | What the engine uses it for |
| --- | --- | --- |
| attitude | `FlightComputer.AttitudeTrackTarget = Custom` | pointing a kitten's manoeuvring unit |
| pointing guard | `FlightComputer.SetAttitudeProfile(Strict)` | its own attitude profiles |
| ignition | `Vehicle.ProcessInput(MainEngineStartup, …)` | the keyboard |
| throttle | `Vehicle.ProcessInput(MainEngineThrottleUp/Down, …)` | the keyboard |
| translation | `Vehicle.ProcessInput(Translate*, …)` | the keyboard, for the bus trim |
| pulse mode | `FlightComputer.SetManualThrustMode(Pulse)` | the keyboard's `ToggleManualThrustMode` |
| staging | `SequenceList.ActivateNextSequence` | the keyboard |

Nothing private is reached for, which is what turns a KSA update into a build error rather than
into a rocket that flies sideways. One thing *is* patched for it, one of the seven patches in the
mod: an attitude command written from a StarMap hook is overwritten before anything reads it,
so `Ksa/AttitudeHook.cs` prefixes `Vehicle.PrepareWorker` — a `public virtual` method, pinned into
`docs/KSA-API-SURFACE.md` so a signature change is a build error too.

**The attitude frame is `EclBody`** rather than the local horizon, because its frame rates are
zero. A commanded inertial direction wants no feed-forward, and the horizon frame's rates would
have the flight computer chasing a rotation nobody asked for.

**The rotation is laid out exactly as the engine's own `GetTgt2Cci` lays it out**, rather than
being invented. Inventing one means guessing which body axis is the nose, and getting it wrong
gives a vehicle holding a perfectly steady attitude ninety degrees from the one asked for. What is
*not* borrowed is the roll reference — see [Roll, and the direction that has no
answer](#roll-and-the-direction-that-has-no-answer).

**The pointing deadband is re-derived every frame**, because KSA only ever raises it. It widens
`AngleDeadband` to what one control period of the minimum thruster impulse can produce and takes a
`max` against the standing value, so a bus carries the guard sized for the boost RCS of the stack
it dropped — measured at 11.40° against a live rate bit worth 0.07. The transient that sets it is the
frame the bus separates: its mass properties resolve, the rate bit momentarily reads **55 deg/s**, and
`0.2 x 55` is 11.04°, held for the whole deployment — which is why cutting the impulse driving it by 35%
did not move it (it went slightly *up*). Assigning the profile in `VehicleCommand.TryAim` puts it back
and KSA's own `max` re-establishes the real floor on the same frame: a separated bus's pointing band
goes **9.63° to 0.37°**. `Strict` rather than `Balanced` because it is also the fastest of the three,
30 deg/s of rate limit against 5.

**And the quantum that floor is built from belongs to the whole ring.** KSA enrols a nozzle in a
rotation axis at a thrust share of **0.1**, not the 0.5 translation uses, and sums the enrolled set into
`MinRotationalImpulse` — so a nozzle nobody meant to steer with still coarsens the quantum that does.
What decides enrolment is the axial gap between the ring and where the part declares its mass, which
defaults to the mounting face where nothing physically is. `tools/model/checkring.py` gates it, and
nothing else can: the mesh is clean, the pivots agree and `checkswept.py` finds no intersection.
`docs/MIRV-NEXT.md` has the measurement.

**Commands other than the attitude take a frame to arrive.** KSA copies a vehicle's control inputs
into its worker state in `PrepareWorker`, which runs before this mod's hook, so an ignition,
throttle or staging write made now is acted on next frame. That is the same latency the player's
own keypress has, and it is why the cutoff is timed rather than waiting to observe one.

**Throttle is a servo, not an assignment.** KSA exposes no way to set one outright — only the two
controls a player holds down, which move it at a fixed rate — so `DriveThrottle` works it toward
the wanted setting and reports what the vehicle actually has.

## Roll, and the direction that has no answer

Pointing needs two directions. Where the nose goes leaves the roll about that nose undecided, and
KSA's own aiming frame decides it by putting the vehicle's belly toward the planet.

**That rule has no answer when the nose points at the planet or away from it — and does not merely
fail there, it reverses.** Sweep the nose up through the vertical and "belly down" swings through
half a turn, because the side the planet is on has changed. A vertical rise sits exactly there for
its whole duration, so a roll re-derived from the aim each frame commands a vehicle that spins on
its own axis for no reason. No threshold fixes it: the discontinuity is in the rule, not in the
arithmetic.

`Sim/AimFrame.cs` carries the reference instead — each frame's is the previous one squared up
against the new aim, which is continuous by construction because it never asks the question again.
It is re-seeded only when the aim has swung so far that the old reference is parallel to it, which
is a different attitude rather than a boundary being crossed. `AimFrameTests` pins both halves: the
carried reference stays inside a hundredth of a radian across the vertical, and the re-derived one
swings more than two radians, so the test says something was at stake.

This is the same shape as `Vec.PerpendicularTo` and the roll singularity in `OpticGeometry`. It is
the third time this mod has met it.

## Attitude is driven for every phase that is doing something

Not only while an engine is lit: after cutoff the bus has to keep the line it was cut off on for the
warheads to leave along it, and leaving it free is a vehicle drifting when it should be settled.

**A hold is the exception, and the reason is propellant.** Before cutoff the whole stack is pointed on
the bus's own jets, and at KSA's Strict deadband of 0.2 deg against a target turning with the orbit they
fire continuously: flown from `ORBIT 300`, a minute of pointing at 1x left them 0.55 m/s2 and four and a
half to five minutes left them at nothing, so the trim after cutoff could not pay 2.4-3.2 m/s and two
holds landed 1.2-1.7 km out. **A warp does not stop it**: with KSA's own attitude hold left on, the jets fired
through the warp to the window at about 19 kg an hour, and an 11 h warp from `ORBIT 800` emptied the bus's
133.8 kg. `IcbmConfig.HoldDriftsUntilIgnition` (on) leaves the attitude free until the
turn onto the burn is due -- `HoldPointing.LeadSeconds`, the turn's time from the nose's angle and the jets'
authority, with slack and a margin -- and lets it drift again if the window moves a minute or more away; the
computer's own warp stops a minute before that turn rather than before ignition. Flown on three holds, the jets
kept 0.54-0.57 m/s2 to the trim on all three. The drift drops KSA's own hold as well as the computer's,
written from `AttitudeHook`'s window every frame (`AttitudeHook.Drift`): a craft picked up holding one kept it,
which is how the 11 h warp emptied the jets; flown since, the tank held 133.8 kg through that hold. `HoldPointsLoosely` (off, five flights) points with the Balanced
profile's 1 deg until the burn lights.

## Cutting off is a timing problem

An engine can only be shut down on a frame boundary. A light upper stage at ten gravities changes
its velocity by more in one frame than any sensible tolerance allows, so **waiting for the velocity
still to gain to fall below a fixed number waits for something that cannot happen**: it overshoots,
turns round to brake, overshoots the other way, and burns the stage dry hunting.

So the program is **stepped every frame and solves a few times a second**. The solve sets a
countdown; the frame runs it down; the engines stop when less than half a frame of burning is left,
which puts the cutoff at the frame boundary nearest the ideal instant. Inside three quarters of a
second of cutoff it solves every step, because those thirty frames decide the shot.

**The throttle comes back for the last two seconds** — `IcbmProgram.ThrottleDownSeconds` — which
divides the residual by the same fraction. Nothing depends on the vehicle honouring it: the cutoff
test is written against the throttle the vehicle *reports having*, not the one that was asked for,
so a stack with solid motors gets the error it would have had rather than a wrong answer.

**A smaller frame is only worth having if the frame is still what sets the residual**, and the ramp
is what stops that being true — see [Hold the direction frames before cutoff, not
seconds](#hold-the-direction-frames-before-cutoff-not-seconds).

## Stop the burn along the line it is actually thrusting

Below `HoldDirectionBelow` the steering direction is frozen, so thrust is no longer parallel to
what is left to gain. The countdown was still the time to gain the whole **length** of it, so the
burn ran past the point where the component along the frozen line reached zero — and past that
point every further metre per second *grows* the residual. The backstop caught it, a full metre a
second late, because its floor is `Math.Max(oneStep, 1.0)`.

Counting down the **projection** onto the thrust line instead is identical whenever steering is
following and strictly better when it is not. Flown: the residual at cutoff fell from **2.1 m/s to
0.15 m/s**, which is below one frame's 0.44 m/s — the burn is now at its timing floor and there is
nothing further to win there.

**The rig could not see this, and the reason is worth keeping.** `IcbmFlightRig` swings at 12°/s,
fast enough to follow the required-velocity vector all the way down, so almost no perpendicular
component ever builds and the projection equals the length. The real vehicle is slower. A rig that
is *better* than the thing it models is not conservative — it is blind, in exactly the region the
fault lives in.

## Hold the direction frames before cutoff, not seconds

Freezing the steering leaves whatever is square to the frozen line in the residual for good, so what
matters is not only *that* the direction is held but **how long it is held for**. `HoldDirectionBelow`
is five metres a second, which is about ten frames of a stack at full thrust — and the throttle ramp
makes a frame an order of magnitude smaller, so on a throttled-down burn the same number holds the
direction for **seconds** rather than frames. Everything the required velocity does in those seconds
lands square to a line nothing can still thrust along.

The signature is unmistakable once it is looked for: the residual is several times what one frame
adds, and nearly all of it is perpendicular to the direction the burn ended on. Measured headlessly
over 90 shots — two thrusts, two planes, three step patterns, two slew rates, four ranges:

| | fixed five metres a second | counted in frames |
| --- | --- | --- |
| mean residual | 0.0653 m/s | **0.0184 m/s** |
| median | 0.0403 m/s | **0.0096 m/s** |
| 90th percentile | 0.1744 m/s | **0.0561 m/s** |
| worst | 0.2476 m/s | **0.1365 m/s** |
| mean multiple of one frame | 4.5x | **0.8x** |
| worst multiple of one frame | 25.2x | **3.0x** |
| mean share square to the thrust line | 71% | **6%** |

So `HoldDirectionFrames` is that same limit counted in frames of the burn *actually happening* —
`acceleration x step x throttle achieved` — capped by the old constant, so a stack at full thrust
holds exactly where it always did and only a throttled-down one holds later. Anywhere between ten
and eighty frames measures the same, which is why twenty is the middle of a plateau rather than a
tuned number.

**A constant step cannot see any of it.** With an even step the residual is a few thousandths of a
metre a second either way; the fault only appears once the step is uneven, because what the frozen
line leaves behind is driven by the solve moving between frames. KSA's step carries the display's
frame pacing — 8.33 ms against 25.0 ms measured in flight on a 120 Hz screen — so
`IcbmFlightRig.StepJitter` is that fact, and `CutoffResidualTests` sets it to 0.5 for exactly this.
It is the fourth thing the rig had to stop being better than the game at, after the command latency,
the throttle servo and the throttle floor.

**In flight the five metres a second binds first.** Read off 456 rockets at 12,902 km, the frame count
hits that cap on 93–100% of them, so the freeze lasts about 0.4 s at either frame rate and the
improvement above is headless only (`docs/ACCURACY-PLAN.md` 3fl).

## The attitude at cutoff is held, not solved

Velocity still to gain is a *difference*, so as it closes on zero its direction is the difference of
two nearly equal vectors and swings wildly — measured in flight at **161 degrees between consecutive
samples**, right at the cutoff instant. Steering to that spins the bus at the exact moment it should
be holding still, because the warheads leave along the line it was cut off on.

So below `IcbmProgram.HoldDirectionBelow` the last direction that meant something is held, and the
coast keeps it rather than swinging to prograde. **How near cutoff that happens is
`HoldDirectionFrames`**, because five metres a second is a different number of frames at full thrust
and at the bottom of the throttle ramp — see [Hold the direction frames before cutoff, not
seconds](#hold-the-direction-frames-before-cutoff-not-seconds).

## Measured

Flown headlessly by `tests/KSArmory.Tests/IcbmFlightRig.cs` — a rig with drag the solver knows
nothing about, an attitude that lags the command, staging that changes the vehicle underneath the
loop, and a coarser integration than anything in `Sim/`.

| Case | Range | Miss |
| --- | --- | --- |
| Equatorial, two stages | 5,000 km | 7 m |
| Across latitudes | 8,551 km | 52 m |
| 4x the drag, 5 deg/s attitude | 5,000 km | 48 m |
| Short shot | 1,365 km | 2 m |
| Lofted 1.4 | 5,000 km | 12 m |
| Depressed 0.85 | 5,000 km | 3 m |
| At a 60 fps step | 5,000 km | 11 m |
| At a 30 fps step | 5,000 km | 5 m |
| On the Moon | 1,042 km | 15 m |

The tests assert 500 m, an order of magnitude looser than the worst of these, because the failures
being guarded against were 150 km to 3,700 km — not tens of metres.

For scale: the Mk 21 the bus carries has a **2 km lethal radius**, so every number in that table is
deep inside it. The accuracy is worth having for a conventional payload and for its own sake; it is
not what decides whether the target survives.

### The error budget

There is no floor. Flown from the same cutoff position with the *exact* required velocity, the
integrator lands on the target to under a metre and within a tenth of a millisecond of the solved
arrival — so the miss is entirely

```
miss  =  velocity left to gain at cutoff  x  dMiss/dV
```

| term | measured |
| --- | --- |
| velocity still to gain at cutoff | 0.003 – 0.011 m/s |
| sensitivity `dMiss/dV` | 274 m per m/s at 1,365 km, 4,732 at 8,551 km |
| solver against integrator, over half an hour | under 1 m |
| impact crossing | under 0.25 m |

Both of the last two used to be tens of metres, and one of them was kilometres on the Moon. The
crossing search accepted the first sample **below** the ground, so a tolerance written as a *time
step* left the answer a few metres deep — which at 7 km/s on a shallow arc is tens of metres
downrange, always downrange, and reads as guidance error. `CrossingToleranceMetres` bisects on how
deep the answer is instead, which is the thing that actually matters and is the same number on
every body. That bounds the error without centring it: the answer is still under the ground, half
the tolerance deep on average, which is 0.20 m long at a 32° arrival.
`IcbmConfig.PredictionStopsOnTheSurface` places it between the samples that bracket the crossing
instead, and is on: flown, the landing's step below the late re-flies went from −0.212 m to −0.007.

**The residual has exactly one lever.** An engine stops on a frame boundary, so the last frame adds
`acceleration x step x throttle` and the best any cutoff rule can do is stop at the nearer of the
two boundaries — leaving a quarter of that on average. The frame rate is given and the sensitivity
belongs to the trajectory, so the only term left is the throttle: coming back to
`MinCommandedThrottle` for the last couple of seconds divides the whole miss by the same fraction.
That is the entire reason the throttle ramp exists, and it is why it is written against the throttle
the vehicle *reports having* rather than the one commanded — a stack that cannot throttle gets the
error it would have had rather than a wrong cutoff.

**That is the floor, not the residual.** A residual several times what one frame adds is not a
rounding at all and no amount of throttle will move it, because whatever is setting it is not the
frame. Flown residuals of 0.24–0.29 m/s against a 0.048–0.060 m/s frame are that shape, and the
term they are made of is in [Hold the direction frames before cutoff, not
seconds](#hold-the-direction-frames-before-cutoff-not-seconds).

## Timewarp is held down for a burn

A guided burn cannot be flown fast, and the reason is the cutoff. The engine stops on a frame
boundary, so the velocity left ungained is whatever the last step added — `acceleration x step x
throttle`. A one-second step already costs about 1.5 km at the far end. At the **170-second steps**
high warp hands out it is kilometres per *second*, and the warheads land on another continent.

So a burning computer registers with `Sim/WarpPolicy.cs` exactly as a round in the air does, through
`IcbmProgram.MaxFaithfulStep`. Same policy, same reasoning, same escape hatches: it stands down
rather than fighting the player for the speed control, and if a slowdown it asked for is never
observed it **abandons the burn** and says so — a shot the player is told about beats one flown into
the wrong ocean silently.

The coast afterwards is not held. Once the warheads are away they are rounds, and the existing
round machinery holds the world for them.

**A coast here is not un-integrated, though, and the cause is this mod.** `PhysicsBubble.cs:1085`
puts a vehicle off rails whenever an actuator is commanded or active, and attitude is driven for
the whole coast to hold the line the warheads leave along — so the bus leaves exact Kepler
propagation and is integrated with velocity Verlet at whatever step the warp hands out. Measured at
**17–20% of all coast probes**. That does not by itself justify holding the coast: the divergence it
was suspected of causing runs the wrong way against warp, with the worlds that threw it averaging
1.64x achieved against 1.97x for those that did not. `docs/ACCURACY-PLAN.md` 3aq is the reading.

`IcbmFlightTests.AtAStepTooLongToCutOffOnItMissesBadly` is the test that fails if the limit is ever
loosened on the grounds that guidance "seems fine".

## When to burn is a separate question from how

`BallisticArc` answers "what would it cost to leave from here, **now**". That is the whole question
on a pad, where waiting achieves nothing. It is not the question in orbit, where the vehicle is
being carried round and the cost of a shot swings by orders of magnitude across one revolution.

**Ignoring that does not give a worse shot; it gives a wild one.** A target the vehicle has just
passed over has no affordable arc at all: forward the short way means reversing the entire orbital
velocity, and the long way round passes through the planet. A search that can only leave now returns
the first of those — eleven kilometres a second — and a computer that believes it burns the tank dry
and lands on the wrong continent.

`Sim/BurnWindow.cs` searches departure time as well as flight time. It coasts the state forward with
`Sim/Kepler.cs` and solves from each candidate moment, across one revolution — the natural horizon,
since past it the geometry repeats. The same target that costs eleven kilometres a second now costs
two hundred metres a second most of a revolution later.

**Waiting is a fallback, not an optimisation.** A weapon whose point is arriving is not worth holding
in orbit to save ninety metres a second, so `WaitMustSaveMetresPerSecond` requires the saving to be
in kilometres per second. Below that it burns now. Above it, or when leaving now has no solution at
all, it holds — and says how long for.

**Closed form is what makes the search possible.** Each candidate departure is itself a trajectory
solve; integrating to each one would turn a search into an afternoon. `Kepler.TryCoast` is checked
against RK4 in `KeplerTests`, hyperbolic cases included.

**The horizon is sixteen revolutions, which is a day**, and the planet is the reason rather than the
orbit. A revolution takes about ninety minutes, in which the ground turns some twenty-two degrees
underneath — so within one orbit a target off the track stays off it, and a search bounded there can
only report the expensive answer: that reaching it costs a plane change of kilometres a second. Wait
sixteen and the planet has turned right round, bringing the target under the track, and the same
shot costs a deorbit.

Searching a day properly would be thousands of trajectory solves, so it is done in three passes.
The first revolution is costed at every step, because **phasing is not visible to geometry** — a
target just passed over is dead in the plane and still unreachable, since the arc to it would have
to go backwards. The rest of the day is scanned on geometry alone, which is a dot product, and only
the handful of moments it likes are solved for real. The vehicle's own state repeats every
revolution, so a coast of twenty hours is asked for as a coast of less than one — exact for a
two-body orbit, and it keeps the solver away from the many-revolution case where its iteration is
least well behaved.

**And the cheapest departure in a day is not the one to want.** Waiting twenty hours to save the
last few per cent is not a trade a weapon should make on its own, so the earliest window within
`GoodEnoughFraction` of the best wins.

**And once a hold has chosen a window it keeps it** (`BurnWindow.TryKeep`). Searched afresh every five
seconds, a near window whose cost drifted across that margin as it approached was swapped at the last
minute for a later or dearer one, and burns lit 32 and 56 degrees off the plane. A fresh search still
wins when it agrees with the kept window or finds an earlier one as good; when it has jumped later or
dearer, the kept window is refined instead — about the instant first chosen, never about its own last
refinement, which walks away the way the loft runaway did.

**Where the plane change belongs falls out rather than being told.** Costing departures and taking
the cheapest puts the burn a quarter of an orbit before the target, and makes it 97% normal to the
plane — which is the textbook answer, arrived at without the textbook. `OrbitPlaneTests` pins it,
because if the search ever stops finding it the shots simply get expensive and nothing says why.

**What none of it fixes is an inclination.** A latitude the orbit never reaches is not reached by
waiting, so the search reports the *closest* the target ever comes to the plane across the whole
horizon, and the panel says which case it is: a number that falls to nothing later is a wait, and a
floor well above zero is an orbit that does not go there. Those two are indistinguishable from the
instantaneous angle, and only one of them has an answer.

## The wait is handed to the game, and taken back carefully

A hold of an hour and a half is not something to sit through at one times, and KSA already has a
warp-to-a-time. **Warp to the burn window** on the panel asks for it, and it is a button rather
than a setting: taking the player's time control because a target happened to be designated is not
a thing a weapon gets to do — they may have set a tenth speed to watch something. Only for the
craft being flown, too: warping the world is not something a computer on some other vehicle gets
to decide.

**Getting out of that warp is the part that needs care**, and getting it wrong pauses the game. KSA's
auto-warp is still travelling when it reaches its target, so a hold that starts while it runs is
trying to brake the world from a thousand times speed in one frame — and the first speed the policy
computes from a step that size is nearly zero. Measured: `simulation speed 1213.07x -> 1.00x`, then
`timewarp held at 0.0x`, then `0.00x (paused)`, then the burn abandoned for the world not running
slow enough to simulate it.

**And the hold counts to ignition, not to the window.** The burn is centred on the window, so it lights
half its length early -- minutes, on GeoSat FAT -- and a margin, a short-step flag and a "burn in" counting
to the window left KSA's warp running at ignition: the first frame of the burn was 110-126 s and the
engines "never lit". All three count to ignition, the hold will not light on a step over
`IcbmProgram.LightOnStepsUnderSeconds`, and a warp the computer started is stopped once ignition is under
`WarpHoldLeadSeconds` away, since the window can move under a warp. **And it keeps the window it chose**
(`IcbmConfig.HoldKeepsItsWindow`): each re-solve otherwise asks again whether waiting still saves
`WaitMustSaveMetresPerSecond`, and nearing the window leaving now always passes -- flown, 2 of 2 holds left
mid-warp 7-47 deg off the plane, and 7 of 7 with it lit on their window.

Two rules come out of that, and they are the same rule twice:

- **The mod does not compete with the game's own warp.** While a computer is holding and an
  auto-warp is running, it asks for nothing.
- **It asks the warp to stop short**, rather than letting it run to the window. One press covers
  the whole wait in hops, each leaving a margin — a fifth of what is left, floored at
  `IcbmProgram.WarpHoldLeadSeconds` — because KSA scales its warp rate to the *span* it is asked
  to cover: a single jump to the end of a ninety-minute hold arrives doing thousands of times
  normal speed, where the last minute passes in under two frames and there is nowhere to hand
  over. Each hop covers a shorter span and so runs gentler. A warp the computer started is also
  one it ends outright the moment the hold is over.

And `MaxFaithfulStep` is 0.3 s, which is a round's 0.32 to within a rounding —
18.0x against 19.2x at 60 fps. Asking the world to run materially slower than
anything else in the mod needs buys a few hundred metres and costs the shot, because the policy
answering that request is a control loop against an actuator shared with the player.

## Waiting is warped through, and it is the same wait twice

Two stretches of a shot are minutes or hours of nothing happening: a **departure window** in orbit,
and the **ballistic coast** after cutoff. Both are a known instant with nothing to do until then, so
both are handed to KSA's own warp-to-a-time rather than to `WarpPolicy`, which only ever holds the
speed *down*.

`IcbmComputer.SecondsToTheNextThingThatMatters` is the one thing that differs between them, and
everything else — the hops, the margin, carrying a warp on after one lands, standing down when the
shot stops wanting it — is shared. **One press covers the whole wait, in hops**, because KSA scales
its warp rate to the *span* it is asked for: a single jump to the end of a ninety-minute hold arrives
doing thousands of times normal speed, where the last minute passes in under two frames and there is
nowhere to hand over.

The coast stops a **settling margin** short of the release rather than at it, and that margin is
`IcbmProgram.SteadyBeforeReleaseSeconds` — see [Arrival angle](#arrival-angle-is-asked-for-not-read-off)
for why the aim is still moving out there. Measured: opening the gate from 45 seconds to 20 runs the
coast at 100x and takes a release probe's own miss from **50 m to 520**, with the walk unmoved. The
harness reads the same constant, because the number and its measurement drifting apart across two
files is exactly what it is there to stop.

**It never starts while `NeedsShortSteps` is true** — the burn and the trim, on any computer —
**or while any round is in the air**, and a warp of its own stops when one appears. That matters more
than it looks: `WarpPolicy` cannot slow the world at all while an auto-warp is running, so a warp
begun over the top of one is a warp nothing can rein in. Flown 2026-10-19 without the round check, on
`PBB SCALE 8`: a bus whose walk had dropped two targets still had its plain gate to wait for, warped
to it with every released warhead falling, and the world reached 56x against a warhead's 0.32 s step.
Each frame's excess was discarded, the warheads lagged the world, and within a second every one was
predicted thousands of kilometres off; nothing landed on a target.

### Automatic only if somebody said so

`IcbmConfig.WarpTheCoast` is on by default. Taking the world's clock away because a target happened
to be designated is not a weapon's decision, so the button is how the action is taken. What the tick
box does is let an operator delegate the press — ticking it *is* the permission, given once instead of
every shot — and it stops in the same place the button does. It warps only the craft being flown.

Off would be a setting an operator makes once per craft, since a save keeps it (`ballistic.json`),
but a new craft starts from the default, and forgetting it there costs a ballistic coast in real time. On two
shots flown the same evening that was 3.5 minutes of wall clock with it and seventeen without.

## The world slows itself for the entry, not for the coast

**A round reads the air inside its sub-step loop** (`Slug`, through `AirDensityAt`), not once a frame: density
falls off on an 8 km scale height and a re-entering round covers more than a kilometre a frame. Measured
across 17, 170 and 320 ms frames, that halves the frame-length sensitivity. The 381 km a 10x flight once
landed long is a record, unexplained -- `docs/ACCURACY-PLAN.md`, "Moved from ICBM-GUIDANCE".

`Interceptor.MaxFaithfulStep` is the wrong guard for it. It is 0.32 s because that is
where a round starts stepping over its own **fuse radius** — a rule about proximity fusing, and
nothing to do with atmospheric entry, where the same step is worth hundreds of kilometres.

**So a round is asked what step it needs rather than its profile being consulted.**
`IProjectile.FaithfulStepSeconds` answers from where the round actually is: a warhead coasting in
vacuum allows its profile's `MunitionProfile.PreferredStep`, and the same warhead in air demands
`Medium.FaithfulStepInAir` — 50 ms. `WeaponSystems.WarpTargetStep` takes the minimum across
everything airborne, and that is what `WarpPolicy` holds the world to. Deliberately *not*
`WeaponSystems.FaithfulStep`, which is the integration clamp: tightening that one discards time and
makes the round fall behind the world, where this one slows the world instead.

That is what makes a six-minute fall watchable **and** accurate: the long coast above the atmosphere
warps as fast as the coasting round will allow, and the world slows itself for the minute of entry
that decides where the round lands. Nobody has to know to do it by hand, and the accuracy does not
depend on their remembering.

**And the coast step is a round's own choice, because its error is not monotonic in it.** A Mk 21's
disagreement with its own predictor is two terms of opposite sign — one proportional to the frame at
about 31 m per millisecond, one constant near −4.2 km — so the error crosses zero near a 138 ms
frame rather than falling to nothing as the frame shrinks: +1,806 m at 194 ms, −1,400 at 96, −3,493
at 24. `MunitionProfile.PreferredStepSeconds` is 0.180 s on the Mk 21 for that reason: `WarpPolicy`
lands the coast at 76–110 ms, near the 96 ms below the crossing (`docs/MIRV-NEXT.md` item 7e).

## Separating first, when the part tree offers a joint

Turning a launcher that is still bolted to a spent booster is correct and nearly useless: a
6,300 kg bus alone is I ≈ 3.6×10³ kg·m² against 10⁶–10⁷ for a spent stack, so the same thrusters
give **two to three orders of magnitude** less angular acceleration — and the tube lever arm, which
is what throws each round as the vehicle turns, collapses from tens of metres to under three.

So `Ksa/LauncherSeparation.cs` asks one question: **is there a decoupler on the joint holding my
launcher on?** A launcher declaring its own separates itself; one bolted to a stock decoupler
separates at that; one with neither deploys attached. Nothing in the guidance names a weapon, and no
shipped part declares a decoupler — so this is inert until a craft is built with one.

Only that joint. A decoupler one hop further up the tree is the interstage, and firing it drops the
whole upper stage rather than releasing the launcher, with the rounds still aboard on a trajectory
nobody solved. For the same reason the program's own auto-staging is interlocked against it: a stage
that runs dry asks for the next sequence every second and a half, and a shot that fell short must
hold its rounds rather than shed them.

**The rosters follow the launcher, they do not rediscover it.** Both key on the `Vehicle`, and a
decoupled booster is not destroyed — so without a handover the weapon stays pinned to a stack with
no launcher while a second battery is crewed on the far half with a full magazine and default
settings, and the ballistic computer goes on holding the dead ring's attitude for the rest of the
session. `WeaponSystems.Sync` runs the handover ahead of crewing and `IcbmComputers` consults that
same decision rather than making its own, because a disagreement about which craft the shot is on
breaks the release in either direction.

The computer is **moved rather than rebuilt**: a fresh one re-enters the phase machine at `Holding`,
and only `Coast` ever sets `ReadyToDeploy`, so it would never release a warhead at all.

## The split costs a metre a second, and only the bus can pay it back

Separating is worth two to three orders of magnitude of turning authority and it arrives with a
bill. `CoreCouplingA_Prefab_Decoupler3WA` declares `Force="7000"`, which against a ~6,300 kg bus is
about **1.1 m/s** — and it lands *after* the last thing that could compensate for it, because the
arc is solved at cutoff and `Coast` never re-solves.

Flown, six warheads off one vehicle on one trajectory inside 0.24 s, the only difference between
them being whether the decoupler had fired:

| | miss |
| --- | --- |
| the one that left **before** the split | **163 m** |
| the five that left **after** it | 3,100–4,100 m, tight to 1,000 m |

**The decoupler costs 3.5 km**, and at cutoff attitude most of it lands on the radial axis — the
expensive one, at 3,401 m of miss per m/s against 1,769 along-track and 780 cross-track.

`Sim/BusTrim.cs` is the answer, and it is the same loop as the burn against a different actuator:
re-solve the arc from where the bus is now to the arrival it was already committed to, take the
difference, thrust along it with the attitude-control jets, stop when less than one frame of firing
is left. Nothing accumulates because nothing is remembered, so it is exact at the instant it ends
however badly the split went. **It removes the cutoff residual too** — a bus with no decoupler at all
still arrives at the coast holding whatever the last frame of the upper stage added.

Headlessly, through the real transfer solver, on the flown 2,700 km deorbit: a 1.1 m/s shove is
**2.4 km of miss, trimmed to 2 m in 1.5 s**.

Six things it is careful about, and each is a way it went wrong first:

- **It nulls against the trajectory the guidance flew to, not a transfer solved from where the bus
  happens to be.** The pair `(CutoffPositionCci, Arc.RequiredVelocityCci)` is a conic; carry it
  forward by `IcbmProgram.SecondsSinceCutoff` with `Sim/Kepler.cs` and subtract what the vehicle is
  actually doing. **Re-solving looks equivalent and is not.** A transfer is parameterised by an
  arrival time and an aim point, and on a deorbit it demands about **20 m/s more for every second
  the arrival is out** — so the answer depends on *when* the trim is asked, and goes stale while
  the vehicle coasts clear of what it dropped. Flown: a bus that owed **0.21 m/s at the split owed
  228.97 after 48 s**, pushed by nothing at all. Propagated instead, the same shove reads 1.100,
  1.0999, 1.0972 and 1.0370 m/s at 0, 10, 60 and 300 seconds — the few per cent of decay being the
  two conics genuinely drifting apart rather than an error. `BusTrimTests` fails at 92 m/s after
  ten seconds against a reference that is not carried.

  It corrects velocity and not position, which is the right half: a shove displaces the vehicle by
  the shove times the delay — tens of metres over a minute — where the velocity it leaves behind is
  worth kilometres of miss per metre a second.

  It also takes no aim point at all, so the loop that once wound the trim and the aim correction
  together is gone by construction rather than by rule.

  **Except on a long coast, where the reference is moved once, at the release gate.** Pinned in time,
  a cutoff residual grows with the coast: 0.4 m/s short is seconds of arrival after four hours, and
  the trim then spends tens of m/s restoring a clock rather than a landing -- flown from 800 km to
  40 N 10 E, 0.22-0.32 m/s short owed 8-17 m/s at the split, the trim gave up and every warhead
  landed 2.2 km out. So past `IcbmConfig.LongCoastSeconds` (4,000 s; a pad shot's coast is under
  1,600) `IcbmProgram.FreeTheCoastArrival` solves the cheapest arc from the bus's own state, seeded
  at the committed arrival, and commits to it if it asks less than the pinned one; the trim then
  carries that conic forward as before. It is one solve with the arrival free, so it is not the
  stale re-solve above. Flown: 0.19-0.40 m/s short, the arrival moved 2-4 s, owed 3.9-6.1, landed
  within 2.6 mm (`LongCoastFreesTheArrival`, ICBM-OUTSTANDING 1.20).

  **And a trim that refuses anyway takes the same solve.** Over its ceiling having spent nothing, it commits the
  cheapest arc from the bus's state (`IcbmProgram.TryCommitTheCheapestArrival`) and resumes onto it
  (`RefusedTrimTakesTheCheapestArrival`). Giving the arrival up instead, as it once did, opened the release gate on
  the arc it had refused and stopped every later pass. It comes after the clearance, often 90 s and 260 s from
  arrival, so it saves a refusal that is mostly timing -- 17.1 m/s asked, landed within 1.3 mm -- and not one that
  is already kilometres of landing: 42.6 asked with 3.5 km in the miss still ships that miss.
- **It resolves onto the vehicle's own axes rather than pointing at the answer.** By the coast the
  attitude *is* the release line, so turning to burn and turning back costs the whole settle twice
  and disturbs the thing the sequencer is about to latch. KSA maps a nozzle to
  `TranslateForward`/`Right`/`Down` by which control-frame axis its thrust points along, so
  `KsaWorld.TryControlFrameCci` reads `Helm2Body ∘ Body2Cce ∘ Cce2Cci` and the flags fall out. The
  debt is mostly not axial: flown and decomposed onto the bus's axes, the radial term is three times
  the axial one — medians 1.71 m/s down against 0.63 along the nose — and only 20% of the total is
  axial (`docs/ACCURACY-PLAN.md` 3fh), so the lateral jets do most of the work.
- **One direction at a time, because the stop threshold is only knowable where it was measured.**
  Half a frame of thrust is the rule, and the acceleration in it is measured — the velocity change
  less what gravity did over the same interval — which is only attributable along the direction
  actually being fired. A bus's lateral authority is whatever its nozzle layout happened to give it,
  and KSA decides that per nozzle from its thrust direction alone — `ComputeControlMap` flags one
  for a translate axis on any thrust component over 0.5, a 60° half-cone with no reference to lever
  arms. A layout with none of it would leave a loop that assumed three-axis authority pushing at
  nothing for ever, so a direction that fires without moving its own component is struck off and the
  next one tried, which is what lets a bus with only an axial pair still get the axial error out.
  The shipped bus is four clusters of five — an axial pair, a tangential pair for roll, and one
  radial jet on the cluster bisector — so it does have lateral translation, and the roll pair is
  exactly tangential rather than canted precisely so a sideways command cannot reach it and leave a
  torque behind: 4.000 units fore and aft and 4.243 in each lateral direction, all six live, which
  `tools/model/checkring.py --translation` reads off the XML. What that lateral authority was wanted for — `docs/MIRV-NEXT.md` item 5c, trimming
  the bus between releases so each tube's kick lands where the last one's did — is moot on this bus
  either way, because straight tubes give every warhead the same kick.
- **It may not finish before the shove has arrived.** The split is deferred through the engine's
  input buffer, so for the first frames after it is asked for the bus is still exactly on its
  solution. Stopping there is stopping on a problem that has not arrived, and nothing afterwards
  looks again. `SettleSeconds` is the guard, and it is also why separation now runs *before*
  anything decides whether a warhead may go: flown, the first round left at `23:04:26.025` and the
  split landed at `.071`, which is how one warhead came off the attached stack and five off the
  shoved bus. So the trim is a **precondition of being ready to deploy**
  (`PostCutoffSequence.TrimHoldsTheRelease`) rather than a step inside the release sequence.
- **It counts the frame its last hold still owes before choosing again**
  (`IcbmConfig.TrimCountsTheCommandInFlight`, on), because a command reaches the engine a frame after
  it is written, and at a warped step half a frame is the whole stop band. Without it the trim chased its
  own overshoot between opposite jets: an aim pass at 84–117 ms steps spent 8.13 m/s against 0.70 with
  it.
- **It gives up rather than holding warheads**, on the same argument `ReleaseSequence` gives up on.
  A stall on the total — not on one direction — is what stops it: with a fixed arrival, a lateral
  error the bus cannot null grows a fresh axial requirement every cycle, so the axial jets chase it
  until something says stop. The overall clock has to be longer than the per-direction one, or the
  loop gives up before the direction that does not work has been struck off.

**The aim correction still sits out while thrusters are firing.** Not because the trim reads it —
it no longer does — but because the correction's only observer is a prediction taken from the
vehicle the thrusters are moving. Both loops drive the same vehicle and both
read the same prediction, so a bias that keeps observing absorbs a displacement the trim itself put
there, the trim reads the moved aim as a larger error, and the pair wind each other up. Flown, on a
shot that was **0.1 km from the target at cutoff**: jumps every 0.51 s — exactly
`PredictIntervalSeconds` — each larger than the last, closing correctly at the measured 1.0 m/s²
in between.

| | |
| --- | --- |
| 0.28 m/s | the cutoff residual, correctly seen |
| 1.23 m/s | the decoupler, correctly seen |
| 2.47 → 4.98 → 7.66 → … → 139 m/s | the two loops winding each other up, ten seconds |

It gave up at 139 m/s having applied about ten metres a second to the bus, and the release probe
went from 0.1 km to 9.9 km. Same shape as the release sequence's latched reference, and for the same
reason: *what a correction observes must not be something the thing it corrects is moving.*
`BusTrim.MaxMetresPerSecond` is the backstop under it — an answer in the tens is the solve being
asked the wrong question rather than a bus that was shoved, and it refuses rather than burning at
it.

**The residual is a timing limit, not a control error**, exactly as it is at cutoff: one step of the
thrusters is `acceleration × step`. Headlessly at 3 m/s², a 17 ms step leaves 0.001 m/s and a 300 ms
step leaves 0.202 m/s. So the trim registers with `WarpPolicy` alongside a burn — `IcbmComputer`
answers `NeedsShortSteps` for it — and the null is finished in KSA's own
`FlightComputerManualThrustMode.Pulse` (`IcbmConfig.PulseTrim`, on, with `PulseSeconds` 0.001): one
pulse of each thruster at most every 0.15 s, the same trick as the burn's throttle ramp
(`docs/ACCURACY-PLAN.md` 3cu).

## Each warhead is kicked off its own release probe

**Every warhead is kicked off the miss its own release probe predicts**, from its own release state
(`IcbmConfig.CancelProbeMissAtSeparation`, refused past `LongShotMissKickMetresPerSecond` or
`ShortShotMissKickMetresPerSecond`, both 1 m/s). So what the coast did by the instant a warhead leaves is
cancelled for that warhead, at whatever epoch it leaves. `docs/ACCURACY-PLAN.md` 3dh has the flight.

**A coast long enough outgrows the cap.** `IcbmConfig.KickAtTheRelease` (on) adds the separation kick to the
predicted arc at `IcbmProgram.ReleaseGate` before arrival (`Sim/ReleaseCoast.cs`) rather than at cutoff. It ships
with `FreezeKeepsABetterRecentAim` and a single target's own 350 s gate
(`SingleTargetReleaseBeforeArrivalSeconds`; a walk keeps 420): a fifth of the trim, and the four-hour coast from
orbit in millimetres where it landed 1.93 km out. `docs/ICBM-OUTSTANDING.md` 1.14 and 1.16 have the flights.

Tried and superseded: one correction for the whole salvo, right for one release epoch -- `docs/ACCURACY-PLAN.md`,
"Moved from ICBM-GUIDANCE".

## Each tube is aimed before it fires

A launcher's tubes can be canted, so rounds released from one attitude leave on different vectors and no single aim
removes it. `Sim/ReleasePointing.cs` turns the vehicle by one cant before each release so the tube about to fire lies
on the mean of the tube axes, which is the line the aim correction assumes a round is thrown along. Measured on the
canted bus: ACCURACY-PLAN, "Moved from ICBM-GUIDANCE".

**It costs nothing on a launcher it does not describe.** A single tube is the mean of its own axes,
so the rotation is the identity, the cant spends none of the release budget, and
`Sim/ReleaseSequence.cs` reduces to releasing when the vehicle is steady. No flag and no branch.

**The MIRV bus's six tubes are straight**, so every tube is the mean and the rotation is the identity for it
too (`docs/MIRV-NEXT.md` item 5).

The code stays because it is general: a weapon pack may register a canted launcher on a vehicle that
*can* hold the command, and nothing about it names the bus.

Two things it is careful about:

- **The command is rotated, not rebuilt.** Both the direction and the roll reference are turned by
  the same quaternion. Building an aiming frame here means deciding which body axis is the nose, and
  getting that wrong is a vehicle holding a perfectly steady attitude ninety degrees from the one
  asked for.
- **The tube axis is latched at the nominal attitude.** Feeding it the live axis instead never
  settles: the tube alternates between on the line and a full cant off it, and half the time it
  looks perfect.

## The bus turns once so every RV leaves nose-first

Held on the cutoff line, the bus throws its RVs 147-164 deg off the airflow they will meet, and each tumbles into the
air backwards. So once the split's shove is nulled, and before the first post-boost pass, the bus turns onto the
airflow at the entry altitude and holds that line through the whole walk (`IcbmConfig.PointRvsAtEntry`, on). Every
RV then meets the air within 0.30 deg of it. `Ksa/Icbm/IcbmComputer.Turn.cs` drives it, `Sim/BusTurn.cs` judges it on
the nose the engine reports, and `Sim/EntryDirection.cs` finds the airflow in closed form on the conic.

- **The prediction aims for the turned kick from the start.** Until the turn has settled, the kick the release would
  give is rotated onto the entry line, so the burn and every pass are aimed for the departure the warheads will
  actually have, and the turn moves the prediction by the difference between two estimates of one line.
- **So it turns however late, and only a conic with no entry keeps the cutoff line.** The split stays at the gate
  (`TurnAllowanceSeconds` 0) and a turn due past it delays the release by the turn's own seconds. Keeping the line
  instead misses by the whole swing of the kick, and an allowance is coast the walk planner takes from the stops.
- **The release, the trim and the post-boost passes all wait while it turns**, and the reach is re-flown once it has
  settled, so the walk is priced on the axes it is flown on; after the split only a plan of the same stops replaces it.

Turns of 147-165 deg settle in 6.8-9.1 s on 1.0-1.5 kg of the bus's propellant, with nothing owed by them. Paired
over 30 flights of eight at 2,000 and 6,181 km the worst warhead is 0.87x [0.79, 0.97] of the cutoff line's.
`docs/RV-POINTING.md` has the flights and the one declared cell that missed its bound.

### The release gate is one budget, not two thresholds

How far off the line the tube is and how fast the vehicle is sweeping it are **the same quantity**:
lateral velocity put on the round, in metres a second at the tube. A cant of `θ` puts
`2·sin(θ/2)` of the ejection speed there — 0.0044 m/s at half a degree on the bus's 0.5 m/s — and
the sweep is already measured in it. `ReleaseSequence.LateralBudgetMetresPerSecond` is the sum of
the two against 0.05 m/s, which on a deorbit's ~3,400 m per m/s is about 170 m of spread.

Two thresholds cannot see that one term is spent against the other, and flown on a canted bus they released at the worse
of two moments. **Where the budget cannot be met, the pointing is spent as far as it will go**: the release goes once the
sweep has stopped coming down and the angle has passed its own best by `ClosingDegrees`, and a vehicle that can hold is
released inside the budget (`ReleaseSequenceTests.AVehicleThatCanHoldIsStillReleasedInsideTheBudget`). The flown log and
its headless reproduction: ACCURACY-PLAN, "Moved from ICBM-GUIDANCE".

### Nothing paces a salvo it is not re-pointing

Releases are one at a time only because each tube wants a different attitude. With no axes latched,
every tube wants the same one, the only gate left is that
the vehicle is steady, and the magazine empties in consecutive frames: **five rounds in 67 ms**,
flown, and all six impacts inside 32 ms of each other.

That is the intent rather than an oversight. Warheads off one release state share a time of flight,
so their errors are one common offset plus each tube's own cant; a paced salvo gives each of them a
different flight time on a bus that is falling toward its release altitude, and that differential is
a larger error than anything the pause would buy. Nothing in this simulation is bought by the pause
either — rounds do not collide with each other, and the bus's six tubes sit parallel on an 0.86 m
bolt circle, so the 0.5 m/s kick only has to unseat them: 5 m clear after ten seconds, never
approaching again, whether they leave together or not.

`ReleaseSequenceTests.TheWarheadsGoTogetherWhenThereIsNothingToTurnFor` is that decision written
down, so nobody reads the one-at-a-time wording above as a rule about salvos.

### A turn that is not being followed is measured, not waited out

The command is one constant rotation of the frozen coast attitude, rebuilt from the same latched axis
every frame. So the off-line angle is a measurement of the *vehicle*, and three shapes are worth
telling apart — from one frame they all read as "N degrees to go":

| What the angle does | What it means |
| --- | --- |
| the pair fits inside the budget | the turn worked |
| passes its own best by `ClosingDegrees` | the turn is over, whatever it reached |
| grows past where it started, by `NotFollowingDegrees` | the vehicle is not holding what it was given |
| stops closing by `ClosingDegrees` for `NoProgressSeconds` | the vehicle is not turning at all |

The last two rows are given up on in seconds rather than at the sixty-second timeout, each says which it was, and the
rest of the salvo goes on the mean axis; every release records which tube went, how far off the line and how fast the
tubes swept. The flight that found it: ACCURACY-PLAN, "Moved from ICBM-GUIDANCE".

**An unresolvable tube axis makes no claim.** The angle between a degenerate direction and the
reference is zero, so reading it as a measurement releases every warhead the instant the launcher's
part tree stops answering — the same rule as an unreadable height field not standing in for flat
ground.

## A tumbling bus does not deploy

A tube sits metres from the centre of mass, so a turning bus throws each warhead at whatever speed
that lever arm is sweeping — and the tubes point different ways round the clock, so they are all
thrown differently. Flown at **113 deg/s**: five metres a second at the tube against a two metre a
second ejection, six warheads spread across 1.5 km of ground and the whole salvo 7.4 km from the
aim.

**The aim correction cannot anticipate it.** It converges on a release kick during the burn, and the
kick at release is a different one — so the error is not a bias it can remove but noise it chases.
`ReleaseSequence.LateralBudgetMetresPerSecond` holds the release until the tubes have stopped
sweeping, which is what a real bus does before every separation.

**Measured at the tube, not at the hull.** A warhead on top of a long stack is tens of metres from
the centre of mass, so one degree a second of hull rate is half a metre a second at the tube — a
quarter of the ejection speed, and kilometres of miss. A gate written in degrees a second passes a
vehicle that is throwing its warheads sideways, and does it differently for every stack length.

**And the spin stays out of the prediction.** It is what the vehicle happens to be doing this
instant, so feeding it to the loop that corrects the aim hands that loop a moving target: putting it
in took the cutoff residual from **0.15 m/s to 4.31** and the predicted miss from nothing to 6.1 km,
because guidance never settled. The steady terms — where the tube is, which way it throws — belong
in the prediction; the transient belongs only to the decision of whether to release at all.

It gives up after `ReleaseSequence.PerTubeTimeoutSeconds` and says so. A bus with no attitude
authority left would otherwise hold its warheads for ever, and that is the worse failure of the two:
the shot is already paid for.

**And it only gathers that evidence once.** A sweep found to be a floor above the budget answers the
question for the vehicle, not for the tube, so every later warhead goes as soon as its own pointing is
done rather than re-confirming the floor. Six of those waits is what turned a salvo into
**282 seconds** — reproduced headlessly in `ReleaseSequenceTests` at the sweep and window one flight
sat at, and now **3 seconds** — and each of them puts the next warhead on a different release state
and a different time of flight, which is the error the whole salvo is being kept short to avoid.

## The ground a round meets

**A released warhead re-reads the ground under every sub-step within 200 m of it**
(`IcbmConfig.ResampleGroundAtImpact`, on; `docs/ACCURACY-PLAN.md` 3cs), because a frame's first sample is the
height of ground it has already left. Tried and superseded: carrying the ground centre forward by the ground
frame's velocity -- `docs/ACCURACY-PLAN.md`, "Moved from ICBM-GUIDANCE".

## Several targets

**Up to one target per warhead aboard, seven at most, and a set of one is the shot everything else is measured against.**
`TargetSet` is the list with its warhead counts, `TargetEdit` is when it may be edited, `IcbmComputer`
holds one, and a click on the world adds to it before launch or during the coast. `DivertFootprint` is the
reach on the ground, taken off the sensitivity columns `ReleaseFocus.FlownSensitivity` already flies once
per salvo, and `ReachDisplay` is that reach as one answer the outline, the cursor and the panel all read.
The bus walks the itinerary, re-aiming and releasing each target's quota, and `Sim/ReleaseWalker.cs` makes
a set of one execute none of it. `ShotBoard` scores a salvo one group per target. The flights that shaped
it are `docs/MIRV-TARGETS.md`; what is open is `docs/ICBM-OUTSTANDING.md` (4.5, 4.8).

### What the player gets

A MIRV rocket carries seven reentry vehicles on the Heavy MIRV Bus. With one
target all of them go to it and land within a few millimetres of each other (`docs/ACCURACY-PLAN.md` 3en).
The player can pick **up to seven targets** (`TargetSet.MaxTargets`, one per RV), and the bus flies between releases so each warhead falls on its own.

From the player's side:

1. Turn on **Designate by clicking the world**.
2. The ground shows the area this rocket can reach. Hovering outside it greys the cursor ring and says
   **outside reach**; a click there does nothing.
3. The first click places target 1. **Targets 2–6 can be placed before launch as well as during the
   coast**, because pinned the footprint is the release epoch and not the arc — see *The pad can draw
   the pinned reach* below. The region shrinks with each one added, and the ring sits around the walk's last
   stop rather than around the landing, because that is where the next hop starts.
4. The panel lists the targets, how many warheads each gets, and whether the whole set fits the bus's
   fuel and the time left before reentry. Targets can be removed; removing one gives its reach back.
5. Launch. The rocket flies to target 1's trajectory, and after separation the bus releases,
   re-aims, releases, and so on until every warhead is away.

### The flight pins the arrival, so the reach is a disc

**A free-clock figure is a best case.** The divert figures below are priced with the **arrival clock
free**, and the flight pins it: `IcbmProgram` latches the arrival
at closed-loop handover and `ResolveCoastArc` solves every later arc to that same instant. Pinned, a divert
is an exactly-determined 3x3 rather than a minimum-norm 2x3, and the penalty is **entirely along-track** —
**2.56x at 6,179 km and 11.94x at 12,902**, against **1.00x across the track at every geometry**, because
moving a target across the track does not change when it is reached. Along-track is what made the footprint
an ellipse worth drawing, so **pinned, the reach is much smaller and close to circular**.

**And those two penalties are *cutoff* figures, not universal ones.** At the release gate the 6,179 km
penalty is 1.94x rather than 2.56x. Read every along-track number below as the free-clock best case.

**Pinned, the footprint is a circle**, radius the cross-track reach, about `0.96 · t_go` metres per m/s
at every geometry and epoch measured — along and across come out within 3% of each other at the release
gate. So `DivertFootprint.OrientationRad` is meaningless pinned.

**Both clocks are built, and `DivertFootprint.TryFrom` has no default between them.** It takes an
`ArrivalClock` — `Pinned` for what the flight does, `Free` for what a loop re-committing the arrival could
offer — because drawing the free one where the flight pins the clock shows a region 2–12x too long, and
that is not a mistake a caller should be able to make by forgetting an argument. Pinning is **one
projection, not a second solve**: the arrival time responds to release velocity along one direction, so
the reach is the free-clock map with that direction taken out of each of its two ground rows. What it
needed was a third row nothing carried — `ReleaseFocus.FlownSensitivity` now flies
`ArrivalSecondsPerMetrePerSecond` beside the landing columns, which costs no extra flying because the
seven predictions already report their own arrival time, and the landing columns cannot supply it: they
are two crossings of one sphere, so the radial part that decides *when* is projected out of them. At the
flown 6,179 km release it reads **355 / 355 against the free clock's 688 / 355**, 1.94x along and 1.00x
across (`DivertFootprintTests`).

### Why the reach area is an ellipse, and why it changes

There are two different reaches, kept apart because they are computed differently and
limited by different things.

**Target 1 is the missile's reach.** The booster decides it: whether a trajectory exists to that point that
the stack can pay for, arrives steeply enough (`IcbmConfig.MinArrivalAngleDeg`, `ArrivalPreference`), and
fits a burn window. The computer already answers this question for one point — `IcbmReach` is `Reachable`,
`ShortOfPropellant`, `NoTrajectory` or `TooShallow` — so the region is that answer asked along bearings
from the launch site until it changes.

**Targets 2–6 are the bus's divert footprint.** After separation the only engine left is the bus's attitude
and translation thrusters (`BusTrim`), so every later target is a place the bus can move its own trajectory
to with the propellant it has left. That is small and lopsided, and **how small depends on when the loop
runs rather than on how far the shot goes**.

**Priced.** Metres of landing per m/s of divert, in the ground frame, at the two epochs a release loop
could run in:

| | 2,000 km (19.8°) | 6,179 km (32.9°) | 12,902 km (9.1°) |
| --- | --- | --- | --- |
| **at today's release gate** (~360 s to go), along / across | 1,135 / 349 | **688 / 355** | 2,860 / 348 |
| 50 m/s reaches | 57 × 17 km | **34 × 18 km** | 143 × 17 km |
| **at cutoff**, along / across | 1,360 / 407 | **2,755 / 961** | 19,477 / 764 |
| 50 m/s reaches | 68 × 20 km | **138 × 48 km** | 974 × 38 km |

**The aspect ratio belongs to the arrival angle, not the range**: the major axis is about
`450 · cot γ` metres per m/s at 365 s to go across all three geometries, and the minor about `0.96 · t_go`.
So it is 25:1 at 9°, 3:1 at 20°, 2:1 at 33° — the ellipse has to be computed per shot and cannot be a
typed shape. First order holds well enough to draw with: flown against linear at 40 m/s it is off by
−1.1% at 6,179 km and +4.8% on the shallow 12,902 km major, and the minor axis is exact.

**The propellant, read rather than estimated**: the bus tank holds **134.3 kg** of MMH/NTO (`51.6452` kg
of MMH at a mass fraction of `0.3846`, straight out of the save), and KSA's own design chain gives an
exhaust velocity of 2,906 m/s. That is **143 m/s fore and aft, 101 on one lateral axis, and 72 across
both** — `BusTrim` fires one direction at a time, so a divert pays the L1 norm of its parts on the
bus's own axes, up to 1.73x its length across all three (`DivertFootprint.CostMetresPerSecond`). Against that,
`PostBoostAim.MaxTrimMetresPerSecond` is 60 and a flown single-target shot spends a median 16.1 m/s
(p90 17.9, worst 21.8). **The guidance cap binds; the tank does not**, and 30–45 m/s is left to divert
with under the cap against 56–127 in the tank.

One piece of luck: the bus's nose sits about 113° from the platform's track at release, so an
along-track divert is largely an *axial* burn — the cheap 143 m/s direction — while cross-track is
purely lateral. Cross-track is ~3x dearer in geometry and another 1.4x in propellant: about **4:1** in
what a player actually spends.

**It shrinks as targets are added, because divert is spent in sequence.** Going from target *i* to target
*i+1* costs roughly the velocity change that moves the landing between them. The reach for the next target is
whatever is left after the ones already chosen, taken along the cheapest order through them. Six points have
720 orders. Built, the order is greedy nearest-first from the lead, not the cheapest of the 720 —
`docs/MIRV-TARGETS.md`, *Both of those, diagnosed*.

#### How it is drawn

* A **ground outline**, painted by the ring pass like the aim ring (`IcbmOverlay`), in the designation
  colour: the missile's reach before target 1, the bus footprint after.
* **Drawn at the ceiling the loop can actually spend**, which is `BusTrim.MaxMetresPerSecond` — 10 m/s, not
  the 30–45 the budget suggests. At 6,179 km that is about **6.9 x 3.6 km** free-clock and **3.5 x 3.5 km**
  pinned, which is what the flight has. Drawing the larger region first and shrinking it when phase 3 raises
  the per-pass ceiling is the wrong order.
* **Before launch, the release epoch's reach — and it is a real boundary.** See the section below.
* **In orbit, nothing.** Reach from orbit is a band around the ground track over sixteen revolutions rather
  than a region around a point, the vehicle is not inside it, and an inclination bounds it in a way no
  bearing sweep can find. It is also unaffordable: `BurnWindow.TryFind` is 7.1 ms from a 400 km orbit, so a
  usable ring is **2.6 s**. Say so and draw nothing.
* **Target 1's region is not built, and should not be until it can be a floor** (2026-10-09). The solve sketched
  below compares the cheapest arc's velocity to gain against what the stack has, which leaves out the ascent's
  gravity and drag losses, so it would draw a *ceiling*: a place inside it that the stack cannot reach, the way the
  Light stack read Reachable on the pad and burned out 1,764 m/s short (`docs/SHORT-RANGE.md`). Every other reach
  this mod draws is a floor for that reason. What it needs first is an ascent-loss allowance measured off flown
  shots -- velocity to gain on the pad against the delta-v the stack actually spent by cutoff, by stack and range.
  The `release summary` line carries both from 2026-10-09 (`thrust spent N m/s against M to gain at ignition`).
* **Recomputed only when the set of targets changes**, never per frame. A pad ring is **37–68 ms** with the
  flight time seeded across the sweep and 112 ms without, so it is built a few bearings a frame rather than
  in one hitch — `IcbmOverlay.Draw` already costs 11.1 ms of a 15.1 ms mod frame. Target 1's region is a few dozen
  reach solves along bearings, cached until the rocket moves. The footprint is cheap, and **cheaper than
  this plan assumed: the Jacobian already exists.** `ReleaseFocus.FlownSensitivity` builds exactly these
  velocity columns through the air, once per salvo, so the ellipse is the two singular values of its
  velocity block projected onto the ground frame — phase 2 costs no new flying. Its edge is still worth
  checking with a real solve on a long shallow shot, where first order is 5% out.
* The **cursor ring** turns `SiteDesignator`'s refused grey outside the region, with **outside reach** beside
  it; a click is ignored. What adding a target would leave is on the panel's own line rather than beside the
  cursor — `Divert reach: 3.55 km from where the warheads land -- room for 4 more at 2.00 km apart`, with the
  budget under it.
* **Existing targets** are numbered rings, each the lethal radius, so two touching is the spacing floor. The
  lead keeps the aim ring's colour and the rest are dimmer. Clicking one to select it in the panel is not
  built.

**Built, and four things about it are the decisions.** The reach is drawn for the craft the panel is
showing and no other — the same scoping `SiteDesignator` has, because only one computer is being clicked
at. Every bus's numbered targets are drawn, and every ring and the reach are painted by the ring pass with
no draped fallback. The flight that
prices the ellipse — seven of `ImpactPredictor`, 2.6 ms headless, against the readout's one — runs on a
**five-second wall clock rather than only on a change to the set**, which is what the bullet above assumed:
the reach decays as the bus descends, 1,076 to 886 m per m/s over a 330 s schedule, so a footprint held from
the last click is a stale one. And it is flown **only while the list can still be edited**: a coast with
designate mode on, or a second target already placed. `IcbmConfig.ShowDivertReach` turns the whole thing off,
flights included.

**And the region is bounded by one hop, not by the whole budget**, which the bullet above already asks for
and is worth restating because the two numbers differ by four times: `ReleaseItinerary.LeftMetresPerSecond`
says what the trim has left and `BusTrim.MaxMetresPerSecond` what one solve will fly, and the ring is the
lower of the two. The panel quotes both, because "what is left in all" is the question a player asks about
the *set* and "how far from here" the one they ask about the *next click*.

### The pad can draw the pinned reach

**The pad cannot know the free clock's reach, and the flight does not fly it.** Measured 2026-09-20. Free-clock the long axis is
`450 · cot γ`, γ belongs to the arc actually flown, and the pad's estimate of it was out by 1.04x, 0.51x and
0.59x. Pinned, that axis is gone: both axes collapse onto the cross-track reach, which is `≈0.96 · t_go` and
has no γ in it at all. **So the one quantity the pad cannot know is the one the pinned reach does not use.**

Swept headlessly over 2,002–12,787 km of range, 13.3°–52.2° of arrival angle and loft 1.0 and 1.35 — 20
geometries, each arc coasted to a 420 s release and the columns flown from there
(`DivertFootprintTests.ThePinnedReachIsTheReleaseEpochAndTheArcCannotMoveIt`):

| | free long axis | pinned reach |
| --- | --- | --- |
| across the matrix | **497 → 1,816** m per m/s, 3.66x | **395 → 407**, 1.03x |

The low end is the 12,787 km shot arriving at 13.3°, the shallowest geometry in the matrix. The disc is a
disc too: major over minor runs 1.000 to 1.022 at that epoch.

**The ratio is the epoch, and it decays with it**, because the out-of-plane response is harmonic about the
body and saturates over a quarter period — `sinc(sqrt(mu/R³) · t_go)` reproduces it to a few per cent:

| t_go | 120 | 240 | 360 | **420** | 550 | 745 | 900 | 1,200 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| reach / t_go, Earth | .936–.993 | .946–.986 | .940–.975 | **.936–.967** | .924–.949 | .889–.917 | .848–.887 | .756–.823 |

The Moon reads **0.977–0.980** at 420 s against Earth's 0.936–0.967, so Earth is the binding body: it has
about the shortest surface-grazing period in the system, and everywhere else the estimate is more
conservative still.

**So `DivertFootprint.PinnedMetresPerSecondToGo` is 0.88 — the floor of the band, not its middle**, and it
is refused outside 60–760 s where it stops being one. At the shipped 420 s gate it draws 370 m per m/s
against a true 395–407, about 7% small: **every click the pad accepts is one the coast's own flown reach
accepts too**, which is the only direction this error may point. `t_go` is the gate exactly, because
`ReleaseItinerary` counts back from it — a new target is always the *last* stop and always releases there.

**What the pad still does not know is the coast's length**, which is what bounds how many stops fit
(`ReleaseItinerary.CoastFits`); at 2,000 km the whole coast is 351 s against a 420 s gate and only one fits.
That is not estimated: the bus is planned with `CoastSeconds` unknown, the panel says the count is settled at
cutoff, and `ReleaseLoop.TrimToWhatFits` drops what does not fit when it is.

**And nothing is flown before the burn is over.** The tempting implementation flies the columns from the
guidance's projected cutoff, and before the vehicle has flown *that projection is the pad* — the same
reading that gave the aim loop 1,522 km of phantom miss and that `AimCorrection.DepartureIsWorthObserving`
exists to refuse. Pinned there is nothing in the footprint the arc could have told us, so the epoch estimate
reads no state at all. The flown path now carries that guard too, so a column can never depart from inside
the air.

### The booster is aimed at the farthest target

**`TargetSet.ElectFarthestLead` re-elects the lead.** The lead is re-elected as the farthest from the launch site whenever a target is added, and **only while the aim is
still free** — `TargetEdit.LeadMayMove`, which is before cutoff and before the arrival is committed. Until
then guidance re-solves velocity-to-be-gained against the vehicle's actual state every cycle, so moving the
aim costs propellant and not accuracy. After it the arc is pinned to an instant chosen for somewhere else,
and past cutoff there is no engine left at all — so a coast add is a divert stop and never a new lead.

It has to be the farthest because the itinerary takes its first stop to be the one the bus already arrives
on; a walk whose first stop is not where the booster aimed is refused (`ReleaseWalkHold.NotWhereTheBusIsAimed`),
because it costs about twice what it is priced at.

**And the lead is never removable** (`TargetEdit.MayRemove`) — the lead rather than the first chosen,
because the release schedule may fly them in another order — since the arc, the aim correction and the
trim are all solved against it.

### The hop is charged where it is measured

**The ring is centred on the stop the next hop leaves from** — the landing while the lead is the last
stop, which is every set of one — so the region drawn, the click refused and the hop charged are one
measurement. `ReleaseItinerary` charges the hop *between consecutive stops*; a ring bounded *from the
landing* accepted two targets on opposite edges at one radius each, and the pair then cost two, **20 m/s
against a 10 m/s ring**, which `ReleaseLoop` refused.
`ReachDisplayTests.ASecondTargetIsMeasuredFromTheStopTheHopLeavesFrom` asserts the old form would have taken
the click, so it cannot pass against the code it exists to detect.

**The walk is ordered nearest-first from wherever the bus has got to** (`ReachDisplay.Order`), never in
the order the player clicked: four collinear targets clicked near-to-far and led from the far end
walked 3 → 0 → 1 → 2, out to the near end and back, 5 km of travel for a 3 km chain. **And the reach
bounds an add, never a designation**, because target 1 is the booster's question, not the bus's.

### Spare warheads, when fewer than six targets are picked

**Priced, and it inverts this section.** The blast sweep runs over rounds in the air, which is why
`Sim/AimSpread.cs` exists — the first group down at 229 m took thirty warheads of five *other* rockets
with it. But `WeaponSystem.FillIncoming` filters a system's **own** rounds out of the airborne list
before that sweep, so **a bus's warheads cannot destroy one another**. Flown: `GeoSat FAT_1`'s six land
0.02–0.13 s apart — 640 m apart at 4,923 m/s, well inside a 2 km lethal radius — and all six are recorded
as landing, with zero `intercepted` lines in the whole night. `AimSpread`'s fratricide is between
*rockets*, which is exactly what it says.

**The filter is load-bearing here and was not written for this.** Its comment says a launcher must not
shoot down its own missiles as they leave the tubes — it fills `_incoming` for the *radar*, and the splash
sweep in `WeaponSystem.Detonate` walks that same list. So this plan depends on a list built for targeting
also being the one the blast reads, which is true today and is nobody's stated invariant. That is what A'
is for.

So **"put the spare ones on the same target" is free today**: release them together, as the salvo
already does. Everything below about staggering is kept because it is what a stagger would cost if that
filter ever changed — and the number says to buy the separation **on the ground, not on the clock**.
At 6,179 km from the release gate, 0.5 s of stagger costs 8.33 m/s and 2 s costs 33.30 — half the whole
budget, per pair — where putting the second warhead 2.0 km clear along the ground costs **2.91 m/s**.

Options:

| | What happens | For | Against |
| --- | --- | --- | --- |
| **A. Spread across the targets, together** | Spare warheads go to the chosen targets; those sharing a target are released together, as the salvo already does | Every warhead is used, and it costs nothing at all | Nothing, while a system's own rounds are outside its blast sweep |
| A'. The same, separated on the ground | As A, but the extra warheads on a target are aimed 2 km clear of it | Survives a future change to that filter | 2.91 m/s a pair, and they do not land on the target |
| B. Keep them aboard | Spare warheads are not released | Simplest | They ride the bus down and are lost with it; the player gets less than they launched |
| C. A pattern around each target | Spare warheads land around each target, spaced beyond the lethal radius | Area coverage | They do not land on the target, which is not what "target" means |
| D. All spare ones on the last target | | Simple | The last target has the least reach left, so it is A with the split backwards |

**Recommended: A, with the counts editable** — and A' is the fallback if the blast sweep ever stops
filtering a system's own rounds, since the whole cost of A is that one line in `FillIncoming`.

* **Default split**: all six spread evenly over the chosen targets, the remainder to the earliest-chosen
  (2 targets: 3 and 3; 4 targets: 2, 2, 1, 1). *Built otherwise: a target is added with no warheads
  (`TargetSet.TryAdd`), so spares stay aboard until the panel's **Split evenly** (`TargetSet.Balance`).*
* **Per-target count** in the panel, 1–6, total at most six. Warheads not assigned anywhere are **kept aboard**
  (option B) and the panel says so, so B stays available to a player who wants it.
* **No stagger.** It was this plan's mechanism for "two on one" and phase 0 retired it: the warheads
  cannot hurt each other, and a stagger is the dearest way to buy a separation that is not needed — 8.33 m/s
  for half a second against 2.91 m/s for 2 km of ground. Worth keeping in mind for *why*: a round released
  later from a coasting bus shares its conic and arrives at the same instant, so a later arrival is a
  different trajectory rather than a later release, and trajectories are what cost.
* **Option C** as a later, separate feature if it is wanted: an "area" target with a radius.

### How it flies

With one target, after the burn: separate the spent stack, clear it, trim the bus onto the target's trajectory, run the
aim correction, release all six ~22 ms apart (`ReleaseSequence`), each with its separation kick (`ReleaseFocus`).

With several targets, the release step becomes a loop:

1. **Order** the targets by the cheapest divert sequence (the same order the reach display used). Built as
   nearest-first from the lead (`ReachDisplay.Order`).
2. **For each target in order**: solve the trajectory to it, trim the bus onto it (`BusTrim` against that
   solution), run the aim correction (`PostBoostAim`), release that target's warheads — all of them, together
   — with their kicks, and record which tube went where.
3. **Stop** when every assigned warhead is away, or when the time or divert runs out — and say which.

What already exists and is reused: the trajectory solve (`BallisticArc`, `Lambert`), the trim (`BusTrim`),
the correction loop (`PostBoostAim`, `AimCorrection`), the release and kick, the trace. What is new is the loop
around them and the bookkeeping of which warhead belongs to which target — `Sim/ReleaseLoop.cs`, built and
tested, and `Sim/ReleaseWalker.cs`, the cursor `Ksa/Icbm/IcbmComputer.cs` actuates it through — see *What the
actuation is* below.

**The per-pass ceiling does not need raising after all.** A hop is flown as a fresh null with the correction's
pass count back at zero, so `PostCutoffSequence.CeilingFor` hands it `BusTrim.MaxMetresPerSecond` — which is
the same 10 m/s the ring is drawn at, because `ReachDisplay` deliberately sizes it at
`min(left, BusTrim.MaxMetresPerSecond)`. Ceiling and picture already agree; the loop's job is to *check* a hop
against it and refuse, not to lift it.

#### What the actuation is

Every one of these is behind "a walk exists", so a set of one executes none of them.
`Sim/ReleaseWalker.cs` is where the first three live, so the `Ksa/` side has no branch of its own:

* **The release gate moves earlier**, and it is `IcbmProgram.ReleaseGateSeconds` — NaN unless a walk is
  running, so `Coasting` computes exactly what it always did and reads the setting *live*, which latching
  the number would not. `IcbmComputer.SecondsToRelease` reads the same `Program.ReleaseGate`, or the warp
  the correction needs held down comes off `(N-1) x 65 s` late.
* **The sequencer is given the stop's quota**, not the magazine (`ReleaseWalker.TubesLeft`).
  `ReleaseSequence.Emptied` latches at zero and is then the signal that a stop is done — and it is zero
  again once the walk is over, because **a rack reloads a few seconds after a salvo** and would otherwise
  send warheads the plan never assigned anywhere.
* **A handover puts back**: `TargetSet.SetLead`; the aim correction (the bias is the ground under the *old*
  aim); `IcbmProgram.CorrectCoastArc`; `BusTrim.Resume` — **never `Reset`**, which would zero
  `SpentMetresPerSecond` and hand the budget back; `PostBoostAim.Reset`; `ReleaseSequence.Reset` (its tube
  reference is the old attitude); `SalvoProbe.Forget`, the miss-kick sums and the salvo's flown kick columns,
  because a probe of the previous target's trajectory solves the wrong kick. And `_trimAbandoned`, or a stop
  that released untrimmed leaves every later one untrimmed too. The aim goes through
  `AimCorrection.Retarget` rather than `Reset`, which starts the stop from the bias the last one walked to
  while `IcbmConfig.CarryAimBiasAcrossHops` is on (it ships on) and from zero otherwise; the
  plant is the coast's, and `Reset` re-seeds it at the pre-burn `1 / Gain` — quarter steps, which buys a
  pass, and a coast pass is a median 65 s.
* **`SalvoFinished` and `_salvoAway` have to mean "the walk is over"**, not "a warhead has left": both gate
  things that must keep running between stops — `DriveTrim` on the first, `RefreshReach` on
  the second. `SalvoFinished` reads `ReleaseWalker.Done` because the magazine count cannot answer it: a walk
  that dropped a stop keeps those warheads aboard, so `WarheadsAway` never reaches the salvo's size and the
  trim would solve and fire at a bus with nothing left to release.
* **`PlacedTargets()` emits one entry per target**, including one the world cannot resolve — with no
  warheads, so `Plan` drops it as a stop while it keeps its index. `ReleaseItinerary.Stop.Target` and
  `TargetSet.LeadIndex` index the same list or the bus re-aims at somebody else's target and nothing says so.
* **The walk comes off `ReachDisplay.Flown`**, planned in `ReachDisplay.For` from the same ordered set the
  ring is drawn from, and **latched** by the computer: a frame whose footprint did not come down would
  otherwise take the walk away on the approach to the gate and shut `ReadyToDeploy` mid-walk.
* **The coast a set is planned against is its own length, latched at cutoff** —
  `ReleaseWalker.CoastForPlanning`, off `IcbmProgram.CommittedArrivalFromNow`, taken before anything can
  decide the reach is not wanted. `docs/MIRV-TARGETS.md`, *The clock cut the walk*. Handed the
  countdown instead, `CoastFits` cut the set to one stop at exactly `gate + hop`, the instant the first
  release was due, and the walk never started.
* **The plan is committed by the first warhead leaving.** Until then it is re-planned as the reach is
  re-flown; after it, re-ordering stops behind the bus would aim it at a place it has been.
* **A hop is accepted on the trim's own question, not the planner's** (`ReleaseLoop.CanFlyTheHop`): the
  hop *plus* what the bus still owes, against `PostCutoffSequence.CeilingFor`, because the trim refuses a
  whole pass over its ceiling — a 9.73 m/s hop arrived as 10.29 and left three warheads 4 km out. **And
  against the flight's whole budget**, which nothing downstream refuses at all: four stops spending
  11.21, 17.08 and 31.72 m/s reached 60.01 of 60, and the fourth released at `divert 0.00` a kilometre
  from where its warhead was sent.
* **A refused hop ends the walk where the bus is** (`ReleaseWalker.StopHere`) rather than finishing it:
  the stop the bus is on is already trimmed and corrected onto, so the warheads left are delivered there
  rather than riding the bus down.

**What scoring reads.** `IcbmComputer.TargetOfRound` answers which
of `Targets` a released warhead was sent to, recorded at the instant it left — nothing downstream can
recover it, because the lead moves on at the next handover and reading it at impact reports whichever
target the bus finished on. It answers for a single-target flight too, where every round is target zero.
`Ksa/BallisticScenario.cs` holds its coast warp to `computer.Program.ReleaseGate` rather than the setting,
so on a walk the world does not run fast past the first release; it is the same number when nothing is
walking.

### Two budgets bound it

* **Divert**: the sum over the sequence. Checked at designation, shown in the panel, re-checked in flight
  against what the trim actually spent.
* **Time — priced.** Over 96 flown buses a re-aim is a median **65.1 s** (p10 58.6, p90 73.2, worst 80.3):
  4 s of clearance, ~16 s of separation null, and a median four correction passes at ~11 s each. A divert burn
  adds its own at ~0.3–0.4 m/s² of Euclidean gain, so 10 m/s is ~30 s and 20 m/s ~55 s. Counting only the coast
  above the 100 km deploy floor:

  | | above 100 km | re-aims that fit | realistic targets |
  | --- | --- | --- | --- |
  | 2,000 km, from cutoff | 351 s | 5 | **3** |
  | 6,179 km, from cutoff | 1,315 s | 20 | **6, comfortably** |
  | 6,179 km, from today's release gate | 334 s | 5 | **3**, and the last two have almost no leverage |
  | 12,902 km, from cutoff | 1,772 s | 27 | **6, comfortably** |

  **The wall clock is the cost nobody has paid yet**: corrections cannot run under warp
  (`SteadyBeforeReleaseSeconds`), so six re-aims mean ~400 s of near-1x flight a shot against today's ~65 —
  **a night of six-target shots runs about three times as long as a night of these**.

### Accuracy per target

Each target gets its own full correction, so each should land as the single-target shot does today.

**Priced.** Holding cost *falls* as the bus descends — measured with the
flight's own `HoldingCost.TryMeasure` at 65 s slots, 1.14 → 1.00 → 0.92 m/s at 6,179 km from cutoff — and a
warhead that gets a fresh correction before release loses nothing by having waited, because the holding cost
is the staleness rate *within* a cycle rather than a debt that accrues. The later warheads in fact land
*better*: the trim's 0.02 m/s stop band is worth 14 m on the ground at the release gate against 55 m at
cutoff.

**What a later target actually loses is reach**, and steeply. At 6,179 km, 50 km of along-track divert costs,
free-clock, 18.2 m/s bought at cutoff, 26.7 at +400 s, 47.8 at +800 and **72.5 at today's gate**; the flight pins the
arrival, which costs 2.56x that along the track at cutoff, about 46.5 m/s for the same 50 km. So the order is by
reach, far first — not by which target is "hardest".

### Scoring a split salvo

* **Scenario**: `tools/scenario.sh mirv:<lat>,<lon>;<lat>,<lon>;...`, loading its own save as today
  (`KSARMORY_SCENARIO_SAVE`). **Built.** `ShotRequest` already parsed the `;`; `BallisticScenario` now
  designates the first place and `AddTarget`s the rest, balances the warheads over them, and carries the
  seat's own `AimSpread` displacement onto **every** target of the set rather than spreading each one — a set
  spread per target fans, and two rockets displaced toward each other is the overlap `AimSpread` exists to
  open.
* **Scoring**: each warhead against **its own** target, not the group's. **Built** — `Sim/ShotGroup.cs`'s
  `ShotBoard`, one `ShotGroup` per target, judged separately and reported together, with the flight's verdict
  a pass only when every target's is. A warhead is attributed through `IcbmComputer.TargetOfRound`, which is
  the stop the *computer* recorded at the release — **both the release and the arrival read it, and that is
  the part worth not re-deciding**. The lead index observed from outside is a warhead out: a handover lands on
  the same frame the last of a stop's quota leaves, so the release would be counted at the next stop while the
  arrival scored at this one, and both targets would then read a warhead that never arrived. A tube number
  cannot do it either, because the magazine reloads inside `QuietAfterReleaseSeconds` and a walk spans
  minutes.

  **The single-target flight is unchanged by construction, not by inspection.** `ShotGroup` is untouched;
  `ShotBoard.Judge` returns `_groups[0].Judge(bar)` for a set of one, the miss goes through
  `computer.TargetEcl()` exactly as it always did, and neither the `TARGET` line nor the `for target N` on a
  landing line is printed at all below two targets.
  `ShotBoardTests.OneTargetIsWordForWordTheGroupsOwnVerdict` compares the two strings ordinally over four
  shapes of outcome, and fails on all four against a board that takes the multi-target path.

  **And a split flight's FLIGHT line carries no group statistics on purpose.** `worst .. best .. mean ..
  spread` is the shape `tools/shot-report.py` reads a single-target group off, and twenty kilometres of
  intended separation in that shape is a twenty-kilometre miss to every night already flown. The report rebuilds
  those four from the per-target lines instead: `mean` warhead-weighted (still the mean distance of a warhead
  from where **it** was sent, which is what `miss` always scored), and every width measured within a target and
  reduced on the worst of them — the reduction the verdict itself uses, so the two cannot disagree about which
  target decided the flight.
* **Fratricide check**: every assigned warhead arrives and detonates; none is recorded as destroyed in the air.

### How close two targets may be

**Overlapping blast is the player's business, so the floor is the lethal radius rather than the blast
radius.** Decided 2026-09-20. It matters because it is what the spacing tables are read against: at a
6.0 km floor a six-target set only works if the itinerary starts near cutoff, and at 2.0 km it works on
the schedule that ends on today's gate and therefore leaves the single-target shot untouched.

What the region between the two radii actually does, since it is what the player is accepting: damage is
**per part and binary** — a part breaks or it does not, and nothing is dented. A part's failure radius is
`lethal x cbrt(9 MPa / its own strength)`, capped at the blast radius, with the strength KSA's own number
off the part's mass and volume. So a part of reference strength fails at exactly the lethal radius, a
dense one at 0.45x of it, and a flimsy one out to the blast radius. **Between the two radii a neighbour
loses light structure and keeps heavy**; outside the blast radius nothing is touched at all.
`Sim/BlastDamage.cs`.

So the shipped answer is **two to six targets on the gate-ending schedule**, 4.5 km apart at 6,179 km and
3.8 at 12,902, and the early start buys spacing rather than count.

### What bounds a set is its spacing

**What bounds a set is its spacing, not its count** — priced in `tests/KSArmory.Tests/ReleaseTradeTests.cs`
off the pinned footprint at each slot. Charging every hop `BusTrim.MaxMetresPerSecond` reads
**66.1 m/s for six against a 60 m/s cap**, and that is the worst case rather than the case: it is a ceiling
on one *solve*, where a hop's real price is its ground distance over the reach at the slot it is bought in.
Six 4 km apart at 6,179 km cost **36.9 m/s** started from cutoff and 55.1 ending on the gate.

The whole budget spent on hops, six targets -- **an upper bound, not the planner's answer**: these are along-track,
straight-line and 60 m/s with no per-pass ceiling. Priced as the planner prices a hop (what the jets pay, on the reach's
narrower axis) five fit at about 7.6 km from cutoff at 6,179 km and six at about 6.1 (`ICBM-OUTSTANDING.md` 4.12).

| | reach per m/s, first → last | neighbour spacing | chain end to end |
| --- | --- | --- | --- |
| 6,179 km, from cutoff | 1,076 → 886 m | **8.5 km** | 42.3 km |
| 6,179 km, ending on the gate | 688 → 410 m | **4.5 km** | 22.5 km |
| 12,902 km, from cutoff | 1,647 → 1,310 m | **12.6 km** | 63.0 km |
| 12,902 km, ending on the gate | 606 → 346 m | **3.8 km** | 19.2 km |

The same trade read as a count, at a spacing somebody would pick:

| spacing | 6,179 km cutoff | 6,179 km gate | 12,902 km cutoff | 12,902 km gate |
| --- | --- | --- | --- | --- |
| 4 km | 6 | 6 | 6 | 5 |
| 10 km | 5 | 2 | 6 | 2 |
| 25 km | 2 | 1 | 3 | 1 |
| 50 km | 1 | 1 | 2 | 1 |

**And the window the feature lives in closes from about five targets on.** `Warhead.BlastRadius` is 6.0 km
for the Mk 21, so anything nearer than that is option A' or option C rather than a second target. Ending on
the gate the widest affordable spacing is 6.8 km at four targets, **5.4 at five and 4.5 at six** (6,179 km),
and 5.8 / 4.6 / 3.8 at 12,902 — inside the warhead. From cutoff it stays open throughout, 8.5 km at six and
12.6 at 12,902. **So two to four targets are a real feature at either schedule, and five or six are only
real if the itinerary starts near cutoff** — which is the gate's paired-arm question again, now with a
number on what it buys rather than only on what it costs.

### Phases

| | What | Flies anything? |
| --- | --- | --- |
| ~~0~~ | **Done** — `tests/KSArmory.Tests/MirvDivertTests.cs`. ±100 km is real **only from cutoff**; at today's release gate the footprint is a 34 × 18 km box. `docs/MIRV-TARGETS.md` has the three findings. | No |
| 1 | **Targets as data**. **Done** — `Sim/TargetSet.cs`, `Sim/TargetEdit.cs`, `ShotRequest` naming several, the list on `IcbmComputer` with `Designate` split from `AddTarget`, clicks that add **before launch and during the coast**, and the panel's rows. Which entry the flight is aimed at is `TargetSet.LeadIndex`, re-elected to the farthest reach by `ElectFarthestLead` on every add while the aim is still free — so the flown order and the itinerary's agree, which is what unblocked phase 3. Where the warheads go is phase 3's; `BallisticScenario` designates the first place and adds the rest. | Unchanged |
| 2 | **Reach display**. **Done, both sides of cutoff** — `Sim/DivertFootprint.cs` is both clocks plus `TryAtTheEpoch` for a flight that has not flown, and `Sim/ReachDisplay.cs` is the drawn region, the cursor's verdict and the panel's readout as one answer; the ring pass paints the reach and `IcbmOverlay` numbers the targets, and `SiteDesignator` greys the ring and says **outside reach**. **The missile's own region before target 1 is not built** and is the only part of this row left: it is a reach solve per candidate point along a bearing sweep, 37–68 ms a ring, so it wants the few-bearings-a-frame build this row's prose describes. | No |
| 3 | **The release loop**: re-aim per target, per-warhead target bookkeeping in the log. Shared targets release together. **Built and flown** (2026-09-21 and 2026-10-01, below) — `Sim/ReleaseLoop.cs` plans the walk and `Sim/ReleaseWalker.cs` is the cursor the flight is actuated through: `IcbmComputer` opens the gate early, hands the sequencer a stop's quota, and on each handover re-aims, resets the seven things a stop owns and lets `BusTrim` null onto the new solution. The ceiling turned out not to need raising, six targets 4 km apart pricing at 55.1 m/s against the 60 cap. **Both blockers are gone**: the arrival needs no re-latch (`ResolveCoastArc` already solves pinned) and the booster flies to the farthest. **Nothing in it executes for a set of one** — `ReleaseWalker.Walking` is false, so the gate is NaN and the program reads the setting live. | Yes — flown at up to six targets |
| 4 | **Instruments and nights**: per-target scoring in `shot-report.py`, the matching check with one target, then 2/4/6. **The instruments are built and flown at four targets** — `ShotBoard`, the scenario's `TARGET` lines and per-warhead attribution, and the report's `== targets` section with `miss`, `spread`, `landing`, `centre` and `dispersion` all reduced per target. Checked on a synthetic night and against two real single-target nights that still read byte for byte. | Yes |
| 5 | **Later**: area targets (option C), reordering by hand. The list is saved, in the save's `ballistic.json` keyed on the craft's name. | — |

**A budget problem, and it is smaller than the worst case said.** Five hops at
`BusTrim.MaxMetresPerSecond` plus the 16.1 m/s a single-target flight already spends is **66 m/s against
`PostBoostAim.MaxTrimMetresPerSecond` = 60** — but that charges every hop the ceiling, and a hop of a few
kilometres does not cost it. Six targets 4 km apart at 6,179 km price at **55.1 m/s ending on the gate**, so
five of the six fit the cap and the sixth's warhead stays aboard; `ReleaseLoop` cuts the set to that and says
so rather than flying a stop it cannot pay for. The tank is not the binding constraint (143/101/72 kg).

## Short shots

**Every range from 25 to 2,000 km is flown on whatever stack is flying it**, with `IcbmConfig.FlyAnyRange` and
the short-shot settings on by default; at 1,000 km RealSRB4 and RealLiquid2 do not yet complete
(`docs/ICBM-OUTSTANDING.md` 1.17, 1.18). The in-air settings apply only where the arc stays under
`DeployAltitudeMetres`; an arc that climbs above it cuts off above the air and gets the long-shot release. The
flights, range by range and stack by stack, are `docs/SHORT-RANGE.md`; what else is open is ICBM-OUTSTANDING 3.2.

### Any range, any stack

A refusal is not the goal: every shot from the shortest up, on whatever stack is flying it. Each rule was
a flown failure first:

1. **A solid's remaining delta-v is a floor on the velocity to gain** (`BallisticArc.TryCheapest`'s
   `minToGain`), read off KSA's staging display for the running stage alone (`RunningStageDeltaV`; the booster's
   figure puts the whole stack's propellant behind the SRBs and lofted the first flight for 5 km/s). Past the
   cheapest arc the need climbs with the flight time to escape, so the solver takes the lofted arc the solid can
   actually fly, the pitch is held at or above that arc's climb, and the program cuts off at the solid's burnout
   when what is left is inside the bus trim's reach.
2. **A stage that can stop is cut off when the shot is complete, wherever that is**, never held back or
   absorbed: it hands over to the closed loop once within the reserve (15 s) of finishing. Waiting for thin air
   instead is what overshot — the schedule cannot cut off, a floor still pushes, and in thick air the stack
   cannot point where a lofted arc wants it — and what flew 200 and 418 km into the upper stage burning back.
   **`HandsOverWhenBurningAddsToTheShot` (on)** also hands over once the schedule has raised the velocity to
   gain past `OutrunShareOfTheStack` (0.6) of what the stack holds, on a stage that can stop. A stack flown flat and
   fast never reaches thin air in time: RealSRB4 at 1,000 km stayed on the schedule to 88 km and burned out 12.8-13.7
   km/s short, 3 of 3. The share is what separates it, read off flown logs: RealSRB4 passed 0.6 at 240.7 s and 0.83
   at 263 s, where a GeoSat FAT long shot's to-gain also rises on the schedule but peaks at 0.33. Neither the air
   (dynamic pressure still falling when the stage already holds less than is left) nor a projection of the rates (the
   core's 8 g spend extrapolated over the stack trips GeoSat FAT at its core's burnout) separated them in flight.
   Such a handover has most of its burn ahead of it, so its arrival latches as a long shot's does and the aim correction
   starts again: pinned like a short shot's, it froze the aim on the schedule's bias and landed 1.035 km out. Flown
   three times with both, every warhead within 2.5 mm, and it never tripped on four GeoSat FAT rockets at 1,983 km;
   `RisingToGainHandoverTests`, `docs/ICBM-OUTSTANDING.md` 1.17.
3. **Done in thick air, it pauses and coasts out**, the loop still solving, and relights in thin air or at the
   top of the climb, so the cutoff that counts is made where the vacuum arc is true. Where the airflow will not let
   it point usefully it waits while climbing and stops once the climb is over; while it waits to turn it burns at
   its floor rather than off, because a stack that steers by gimballing its engines has nothing else (flown at
   200 km, one tumbled into the sea with the engine held off until it was pointed).
4. **A short shot steers at any angle of attack** — KSA's drag has no turning moment and structural failure is
   g-load alone, so in this build an angle costs drag and nothing else (`docs/BLOCKED-ON-KSA.md`) — and **pins its
   arrival the moment the closed loop takes it**: left free, the cheapest arc from a point on a lofted one is a
   lower one, and the velocity to gain swung round to point backwards through a pause.
5. **Release.** A short shot drops the spent stack at cutoff. One that cuts off in the air, or whose arc
   stays under `DeployAltitudeMetres`, releases at cutoff,
   at once, from the bus still on the stack — no trim, no post-boost hold, no wait to settle. Each of those was a
   flown failure: the stack coasting through air with the warheads aboard (13.7 km), separating then releasing
   beside it (a 2.2 / 10 km split), a release gate that opened on the descent (10 km), a 53 s wait for a stack
   with no engine to stop sweeping its tubes (8.9 km).

### Finish in the air, and solve the arc with drag

**Don't relight.** On by default, flown. Three settings together; `docs/SHORT-RANGE.md`, "Why 150 km failed",
has the tumble they answer:

- `ShortShotSlowsLineSeconds` (0.5).
- `IcbmConfig.ShortShotFinishesInTheAir`: a short shot's burn that ends in thick air cuts off and releases
  there instead of pausing. That removes the relight, and with it the chase.
- `IcbmConfig.ShortShotSolvesWithDrag`: in the last 20 s of the burn, each guidance pass flies the solved arc
  with the warhead's drag from its projected cutoff. It measures the landing against the target carried to the
  instant the solve carried it to (`BurnoutGuidance.Command.CarrySeconds`). It moves the aim east and north by
  the miss, solves again, and keeps the new solve only if it lands nearer, up to two times a pass. Under it the
  arrival floor is waived, because a moved aim's vacuum arc is shallower than the warhead's and a failed floor
  unlatches the arrival. Absorbing solids are excluded, because their flight time is re-picked whenever the aim
  moves.
- **All three apply only where the arc stays under `DeployAltitudeMetres`.** An arc that climbs above it still
  pauses and relights, so it cuts off above the air and gets the long-shot release that gives millimetres. Both
  ways of finishing such a shot in the air were flown and lost. Released at once from a stack still turning,
  700 km landed 0.80 km out against 2.2 mm, and 1,000 km 0.98 km out. Coasted on the bus alone through the air
  to the release altitude, 500 km owed the trim 287 m/s of drag and landed 107 km out. The slowed line is held
  to the same arcs: around a pause it makes the relight cut off on the line it slowed through the coast, and
  flown that left 700 km 73 m/s short and 1,000 km 1.0 km out. And such an arc coasts out until the air is
  under `Medium.NoticeableDensity`, not merely under `HandoverPressurePa`: between the two a cutoff still releases
  at once in the air. Flown at 700 km, a cutoff at 64 km released from a stack still turning landed 0.36 km out,
  against 2.2 mm from a cutoff at 98 km.

### Solids

What is in (`IcbmConfig.SolidsLeaveMetresPerSecond` 30, `DropSolidsUnderWeight`, both on):

- **The solids are steered along what is left and asked to leave 30 m/s**, so what they leave lies ahead of
  the nose rather than anywhere.
- **The arrival is pinned from their burnout every solve**, so the stage after them finishes the arc they were
  steered onto. Left free, the cheapest arc from a lofted one swung what was left from 30 m/s along the nose
  to 6 m/s 75 deg off it; pinned from the uncapped time to gain it was five seconds late and 20 deg off.
- **The stage after them lights at its floor**, the lever set while only solids burn, which they ignore: lit
  at full, 30 m/s is a 0.3 s burn at 2 m/s a frame.
- **On a short shot, solids under the stack's weight are dropped** when a stage is left after them.

### A stalled cutoff

**A stall is said on the panel.** Once the throttle-down has run `IcbmProgram.StalledRampSeconds`
(30 s, against a closing ramp's 10-15), the computer's hold reads "cutoff stalled: the stack is holding its own
weight, N m/s still to gain", and the log says so once. Flown on Real Liquid2 at 25 km: reported five minutes into a
twelve-minute hover, which then landed 0.2-0.4 mm out.

`IcbmConfig.ShortShotPushesThroughAStall` (on) acts once the ramp has run
`IcbmProgram.PushesThroughAfterSeconds` (10 s) in the air: the push is measured from how the velocity to gain moved
against what the thrust should have done, its component along the line is added to the throttle, the line is led
across it by what it will add before the burn ends, and the steering freezes only under 1 m/s.
Two other gates are built and off -- `ShortShotPushesThroughAtDragShare` and `ShortShotCutsOffOnTheMiss`, the
second steering the last metres a second off the one direction that does not move the landing; SHORT-RANGE has the
flights.

### Release and the kick

* **A salvo released at cutoff in the air goes in one frame** (`IcbmConfig.ShortShotReleasesTogether`, on), and its
  probe-miss kick is capped at `ShortShotMissKickMetresPerSecond`, 1 m/s, rather than
  `ReleaseFocus.MaxMissKickMetresPerSecond`'s 10 mm/s: warheads let go a frame apart leave a stack the air is
  slowing, and the kick that cancels it is over 10 mm/s.
* **An arc that releases after the trim takes the same 1 m/s cap** (`IcbmConfig.ShortShotKickCapAfterTheTrim`, on).
* **A hot core's backstop arms at the trim's 10 m/s** rather than 2 on an arc that releases after the trim
  (`IcbmConfig.ShortShotBackstopsAtTheTrim`, on): the residual turning back up ends the burn, or in the air
  coasts it out, and the trim takes the rest.
* **The ground at the aim is carried back by the countdown only while the engines burn**, because a short shot
  releases at cutoff before the aim cycle clears it.

`docs/SHORT-RANGE.md` has the flights for each: "Accuracy, next", "The kick cap after the trim", "A hot core
overshot its cutoff" and "The ground at the aim after cutoff".

## Constants that carry the frame

**A decision belongs in seconds or in physical units.** How long to freeze the steering, how long to
wait, when to give up, when to stop trying — none of those may depend on how fast the host machine
draws. Only a quantity genuinely bounded by the frame may scale with the step: *the velocity one
more frame of thrust would add* is `accel x step x throttle`, and a threshold below it is one
nothing can reach. `IcbmProgram.HoldDirectionFrames` is what happens when the two are confused — it
was introduced to bound a residual, which is frame-bounded, and expresses a freeze duration, which
is not, so it lasts `Frames x step` seconds and grew the off-plane residual 5.9x over a 4x step
(`docs/ACCURACY-PLAN.md` 3fk).

**The test that separates them, in order:**

1. Write the quantity's effective value at a 17 ms, a 25 ms and a 40 ms step. If it moves and it
   expresses a policy, it is a bug.
2. Ask what the number is *made of*. A count of frames is a duration; a count of passes whose
   passes are seconds-gated is not; a per-frame filter weight is a time constant in disguise.
3. Ask what geometry makes the consequence visible. `HoldDirectionFrames` is **identically zero**
   equator to equator, which is where the cutoff fixtures fly — the blindness was one line of
   fixture geometry and it hid a real term for months.

Counts: **9 DECISION**, **13 FRAME-BOUNDED**, **27 CONVERGENCE**. None has a
seconds-valued sibling: the one built, `IcbmConfig.HoldDirectionSeconds`, was deleted unflown on 2026-10-09, since
in flight the 5 m/s cap makes the freeze step-independent already (`docs/ACCURACY-PLAN.md` 3fl).

**What a slow machine can do to these: nothing past 33 ms**, measured 2026-10-11. KSA advances the world at most
`1/minTargetFrameRate` per frame, 33 ms at the default, and a machine that cannot keep up slows the world rather than
lengthening the step -- pinned to two cores the game ran at 9 fps and cut off on a 14.6 ms step. Vsync already holds
this machine's eight- and sixteen-rocket nights to 33.3 ms at cutoff, so every night is flown at the bound a slower
machine would see. A player who raises `minTargetFrameRate` above 30 shortens the step; one who lowers it is the only
way past 33 ms. ICBM-OUTSTANDING 2.2.

### DECISION — a policy whose effective value moves with the step

| Constant | Made of | 17 ms / 25 ms / 40 ms | Consequence | Seconds sibling |
| --- | --- | --- | --- | --- |
| `IcbmProgram.HoldDirectionFrames` = 20 | `Frames x step` seconds of frozen thrust line | measured **0.167 s / — / 0.400 s**; nominal `20 x step` is 0.34/0.50/0.80, shorter in flight because `HoldDirectionBelow` caps the entry and the throttle ramp slows the burn-off | everything the required velocity does in the freeze is left square to a line nothing can still thrust along. 0–2% of the residual along the track, **59–93% at 26° off the plane**; headless the 40 ms residual goes 0.3299 → 0.1468 m/s, which at 4,700 m per m/s is ~0.9 km | none: `HoldDirectionSeconds` deleted 2026-10-09 (3fl, 3fo) |
| `WarpPolicy.OverridesBeforeYielding` = 2 | frames in which the speed reads above the request | 51 / 75 / 120 ms of tolerated contest | the condition tested is a *state*, not an event, so it counts frames. A yield is cleared only by an empty sky, so one spurious yield stands the policy down for the whole flight — 9 of 12 shots yielded, warheads landing on 117–267 ms frames against 18–33 for the 3 that held. An auto-warp ramp lasting 60 ms stands the mod down at 120 fps and not at 25 | none |
| `BusTrim.AccelerationGain` = 0.25 | a 4-frame EWMA time constant on the measured thruster acceleration | 0.068 / 0.100 / 0.160 s of lag | after every direction change `StopBand`, the budget charge and `Alive()` run on a stale acceleration for that long. Defensible — the noise rejected is a velocity difference over one step, whose size goes as `1/step` — but the lag is a duration and it grows | none |
| `SalvoProbe.MaxAgeSeconds` = 0.5 | a seconds bound over a span counted in frames | salvo span `6 x step` = 0.10 / 0.15 / 0.24 s | a salvo lets its six go one per frame. Past ~12 fps the last warhead's borrowed probe is refused as `TooOld` and it leaves unkicked, which lands 1.7–2.0 m out. The bound is right; what it bounds is frame-counted | none needed — the span is the fault |
| `ReleaseFocus.Columns.ReusableWithinSeconds` = 2.0 | the same, with 4x the headroom | same span | reverts to a fresh column solve rather than to a wrong answer | — |
| `_trimFloor` (`IcbmConfig.ReleaseInsideTheTrimFloor`) | `BusTrim.StopBand(accel, simStep, pulse) / perMetre`, sampled on one frame per pass | masked today | the step term binds above `0.02 / (0.5 x accel)` — **71 ms** on the shipped 0.56 m/s² bus, **13.3 ms** on a 3.0 m/s² one — and is masked entirely while `PulseTrim` is on, where the floor is `3 x accel x pulse` = 1.68 mm/s. Latent: uprate the bus's jets or turn pulsing off and *when to stop correcting and release* becomes frame-rate dependent with nothing saying so | none |
| `WeaponSystems.FruitlessSearchesBeforeRetiring` = 120 | frames of empty search | 2.0 / 3.0 / 4.8 s | a backstop far past any staging rebuild. Nothing observable | none |
| `SmoothedStep.Weight` = 0.15 | a ~6.7-frame EWMA time constant | 0.11 / 0.17 / 0.27 s | cosmetic — the chase transition's ease and nothing else. Defensible for the same reason as `AccelerationGain`: what it rejects is the display's per-frame pacing beat | none |
| `SightCamera`/`ChaseCamera.GiveUpAfterFrames` = 180 | frames of refused pose | 3.1 / 4.5 / 7.2 s | log cadence only; the camera keeps trying either way. Outside the ballistic surface | none |

`OverrunLog.ReportEvery` = 120 is the same shape in a diagnostic and is not counted above.

### FRAME-BOUNDED — the frame is the right unit

| Expression | Why the step belongs in it |
| --- | --- |
| `IcbmProgram.ShouldCutOff`: `_countdown <= 0.5 x _lastStep x throttleAchieved` | half of what one more frame of burning adds. An engine stops on a frame boundary, so this puts the cutoff at the boundary nearest the ideal instant |
| `IcbmProgram.ShouldCutOff`: `oneStep = accel x _lastStep`, floored at 1.0 | the rising-again backstop asks whether the velocity to gain grew by more than one frame's worth. The noise it discriminates against *is* one frame of thrust |
| `IcbmProgram.HoldDirectionThreshold`: `frame = accel x _lastStep x throttle` | the base quantity is correct and stays. Only the `x HoldDirectionFrames` multiplier on it is the bug |
| `BusTrim.StopBand(accel, step)` = `max(0.02, 0.5 x accel x step)` | half a frame of jets. A threshold below what a frame adds is one nothing can reach and the loop hunts round it. The 0.02 floor keeps the step out of it below 71 ms on the shipped bus |
| `BusTrim._firingFor >= 2` | a command reaches the engine's worker one frame after it is written, so the first interval after a change is a mixture. Two frames is the pipeline's own length, not a duration |
| `BusTrim._pulsedLast`/`_pulsedBefore`, `_pushDirLast`/`_pushDirBefore` | the same one-frame write latency, as shift registers |
| `WarpPolicy.SettleSteps` = 1 | exactly one step of write latency. Judging a request on the step that predates it reduces on top of a reduction already in flight |
| `WarpPolicy.FramesAwaitingWrite` = 4 | how long a write may go unobserved before it is a refusal. Write latency is a frame-count property of the engine |
| `StepGate.RoundingSeconds` = 1e-9 | KSA rounds its clock to whole nanoseconds. Not a duration |
| `SimClock.MaxStep`, `Interceptor.MaxFaithfulStep`, `IcbmProgram.MaxFaithfulStep`, `BusTrim.MaxFaithfulStep` | bounds *on* the step. The step is the subject, not the unit |
| `PostBoostAim._lastCycleSeconds = Math.Max(step, _elapsed - _cycleStartedAt)` | a one-frame floor under a measured cycle length, so a zero-length cycle cannot price the next one as free |
| KSA's own `nav.Time > LastThrusterPulseTime + 0.15` (`FlightComputer.ComputeRcsControl`) | the engine's pulse gate is **seconds**, so pulse authority is frame-independent — see below |

### CONVERGENCE — iteration and sample counts, unaffected

`BurnoutGuidance.RefinementPasses` 3 · `Lambert.MaxIterations` 48 · `Kepler.MaxIterations` 64 ·
`BallisticArc.ScanSamples` 96 · `BurnWindow.Revolutions` 16, `CoarseSamples` 256, `Candidates` 8,
`NearSamples` 32 · `ArrivalBudget.SteepestConsideredDeg` 80, `ResolutionDeg` 0.5 ·
`ImpactPredictor.TerrainCrossingSteps` 3, `MinRefineSeconds`, `CrossingToleranceMetres`,
`AtmosphericStepSeconds` 0.25 · `IcbmComputer.PredictStepSeconds` 2.0 ·
`ReleaseFocus.VelocityStepMetresPerSecond`, `PositionStepMetres`, `LeastAuthority` ·
`AimAuthority.ProbeMetres` 1000 · `HoldingCost.ProbeSeconds` 106 · `BombSight.MaxSteps` 2048 ·
`BallisticLead.MaxPasses` 32, `StallPasses` 4, `ReachHalvings` 12.

**And the pass counts that span frames belong here too**, because what they count is seconds-gated
rather than frame-gated: `PostBoostAim.MaxCycles` 20 and `PassesWithoutImprovement` 3, and
`AimCorrection.WorseBeforeStopping` 12. During the burn an observation is gated by
`_sincePredict >= IcbmComputer.PredictIntervalSeconds` (0.5 **simulated** seconds), and after cutoff
by `PostBoostAim`'s own settle and flown clocks, all of which accumulate `step` against a seconds
constant. What is left is the interval's quantisation — `ceil(0.5 / step) x step` is 0.51 s at
17 ms, 0.50 at 25 and 0.52 at 40, under 5%.

**The rest are latent.** `WarpPolicy.OverridesBeforeYielding` has never fired on a ballistic night,
`BusTrim.AccelerationGain` and `SalvoProbe.MaxAgeSeconds` have not been measured at two frame rates, and the
others cost nothing observable; what flying each would take is `docs/ACCURACY-PLAN.md`, "Moved from
ICBM-GUIDANCE".

### Ruled out, with the engine's own source

**The pulse phase's authority is not frame-rate dependent.** `BusTrim` commands a pulse on every
frame of a refine phase, which looks like an actuator whose rate follows the frame rate — and the
obvious reading is that a fast machine nulls faster, which would make `StallSeconds`,
`PulseSecondsPerNull` and `MaxSeconds` all buy different amounts of work on different machines.
`FlightComputer.ComputeRcsControl` gates it on `nav.Time > LastThrusterPulseTime + 0.15`, in
**seconds of universe time**, so the delivered interval is `ceil(0.15 / step) x step`:

| step | delivered interval | authority on the shipped bus |
| --- | --- | --- |
| 17 ms | 0.153 s | 3.66 mm/s per second |
| 25 ms | 0.150 s | 3.73 |
| 40 ms | 0.160 s | 3.50 |
| 100 ms | 0.200 s | 2.80 |
| 300 ms | 0.300 s | 1.87 |

Flat to ±7% across the band a machine actually runs at. It degrades only past ~60 ms, and the trim
registers with `WarpPolicy` for `IcbmProgram.MaxFaithfulStep` — 0.3 s, twice the engine's gate — so
that regime is reachable under a warped coast and costs up to 2x of the phase's authority. That is
the one thing here worth a line in a log rather than a night.

**What that leaves.** The frame-rate-dependent stall rate — a third at 63 fps and two thirds at
47 — is not the pulse cadence and, per 3fk, not `StopBand` and not `Stalled`. The standing candidate
is the off-rails coast: KSA integrates the bus at the frame step while `BusTrim` compares against an
exact Kepler propagation, which is a recession proportional to the step — 0.25 mm/s per second at
28 ms against 0.19 at 21, against 3.7 mm/s per second of authority. `IcbmConfig.RailsDuringCoast` was
the switch, removed unflown on 2026-10-06 with `QuietCoast`; `StallFallsBackToHolding` has since taken
the stall rate to zero (`docs/ACCURACY-PLAN.md` 3fh).

## What it tells the operator

- **`IMPACT IN mm:ss`** — a countdown, taken from the plan while the burn is running and from the
  flown prediction once it is not. The two disagree during the burn on purpose: the plan assumes it
  finishes, the prediction assumes it stops now.
- **`Burn starts in mm:ss`** while holding, so a computer sitting there doing nothing for an hour is
  distinguishable from a broken one.
- **`TARGET UNREACHABLE`**, in two flavours: no trajectory arrives there at all, or one does and the
  tanks cannot pay for it. The second is stated with the shortfall, against the engine's figure for
  the whole stack where it gives one — the craft being flown — and otherwise one stage's exhaust
  velocity over the whole vehicle's propellant, which understates a deeply staged rocket, and
  understating is the right way round to be wrong.
- **`NO ARC ARRIVES AT n DEG OR STEEPER`**, which is the third and is separate because it is the one
  a control on this panel fixes. It names the steepest arrival the search did find, so the operator
  can see how far the minimum has to come down.
- **The arrival angle asked for beside the one the planned arc achieves**, under the
  steepest-arrival slider. Those two differing is the whole reason the control exists.
- **`Delta-v: needs N m/s, has M m/s`** before the burn, marked as one stage's reckoning where the
  engine does not report the whole stack.
- **A mark on the target** that stays on screen wherever it is, clamped to the edge when it is out
  of view, with the countdown beside it.

**What it asks is almost nothing.** Arm, stage automatically, the steepest arrival — which
`NO ARC ARRIVES` names, so it cannot be folded away — and **Precision against range**
(`ArrivalPreference`), the one trade: a steeper arrival and a longer hold buy precision with reach
and time. Everything else on `IcbmConfig` is a property of the trajectory rather than a preference,
and sits under one **Engineering** fold that opens closed and appears only on a developer's install,
still settable from a shot spec.

## Flying one without watching it

`./tools/scenario.sh mirv` drives the whole shot: it finds whichever craft in the scene has a
ballistic computer and its wheels on the ground, designates, arms, and follows the flight to the
last impact. `mirv:26.485S,68.148W,2` moves the aim point and the bar it is judged against.

The operator still supplies the rocket — a mod cannot put one on the pad, see `CLAUDE.md` — so the
craft has to be the one the game boots into, in a save named in `KSARMORY_SCENARIO_SAVE`, or named in
`KSARMORY_SCENARIO_CRAFT`.

Two things about it are worth knowing before reading a result:

- **It asks the world for timewarp once and never again.** `WarpPolicy` owns the speed after that,
  and it holds the world down for the burn, the trim and the rounds in the air. Competing with it is
  a loop neither side wins.
- **It does not stage.** Ignition is the program's own first stage request, and a harness that
  lights the rocket by hand is one that passes whether or not the computer can launch at all —
  which is how a computer with no ignition flew every shot here. A stage that would let the bus off
  with stages still to burn is moved to the end of the staging list and the burn goes on
  (`IcbmConfig.MovesAnEarlyRelease`); one it cannot move without delaying an engine is refused, and the
  run says so.

The output is the report: cutoff residual and the computer's own predicted miss, the separation, the
trim's two numbers, every release with how far off the salvo's line its tube was, and every impact
with its miss. The verdict is the **worst** warhead of the group against a stated bar — the failures
above are all common offsets kilometres wide, and a mean would say the same thing about those while
hiding a group that scattered either side.

## Not done, and not verified

**The flight works end to end and the numbers above say how well.** `CHECKLIST.md` §12 is the list
of what has been confirmed and what has not, and it is the one to read — what follows is only the
parts a headless test cannot reach at all.

**The bus trim is flown and works.** KSA's translation flags reach the bus's nozzles, measured at
**0.9–1.1 m/s²**, and the decoupler's ~1.1 m/s goes out in under two seconds — `trimmed to
0.010 m/s`, off a cutoff already 0.0 km off. The one failure it had, the trim and the aim
correction winding each other up, is fixed and has flown since.

- **Whether the flight computer can hold a commanded *offset*** on a stack with marginal control
  authority. The rig assumes a 12 deg/s slew. A separated bus commanded six degrees off its held
  line hunted rather than settling, which is why re-pointing was left off and then removed (`RepointBetweenReleases`, 2026-10-06); the pointing
  band has since come down from 9.63° to 0.37° and nothing has asked it for a turn again.
- **Staging.** `ActivateNextSequence` fires whatever the player put in the next sequence, which is
  not necessarily an engine — including the ignition, where the program will try again on the
  cooldown until the dry timer ends the flight.
- **Staging under warp.** The world is now held down for a burn (see above), but whether KSA
  applies thrust and staging faithfully at the speeds it is held *to* has not been watched.

**Other bodies are out of scope, and "anywhere" means anywhere on the body being flown around.** A
ballistic arc is a two-body problem about one planet. A target on another world is an interplanetary
transfer — escape, a heliocentric leg, capture — which is a different manoeuvre rather than a longer
one. The panel says so when the designated body is not the parent.

**The arrival-angle floor flies its search half and not its coast half.** Two shots at a 15° floor
flew the constrained arc, reported `reach Reachable` and cut off 0.01 m/s short, with the tightest
group ever measured here — and then landed 2.28 and 2.68 km out, because the aim correction opens
on the trajectory search rather than on the drag shortfall a steep arrival abolishes.
`docs/ARRIVAL-ANGLE.md` has the per-cycle trace. `IcbmConfig.AimWaitsForCutoffUnderAFloor` is built and
off: flown neutral on a solid deorbit stage, its case is a liquid one picked up in orbit
(`docs/ICBM-OUTSTANDING.md` 1.3). One
known bound on the search half: the floor is applied to the *vacuum* arc, which agrees with the
flown arrival to under half a degree over 10–30° and diverges only for a graze.

**A stage that lights hot.** Cutting the engine below a liquid floor is flown without reading dry
(`IcbmConfig.PulsesBelowTheFloor`); applying the same cut to a stage that lights hot is not built —
see [The cap is right and the actuator is too slow](#the-cap-is-right-and-the-actuator-is-too-slow).

**A save keeps the setup and never the flight**, verified in game on 2026-10-11. Every `IcbmConfig`
setting moved off its default but `Armed`, the targets with their warhead counts and which one leads
are written into
`saves/<save>/KSArmory/ballistic.json` when the game saves, keyed on the craft's display name as
`systems.json` is (`Sim/BallisticSettings.cs`). A load puts them on the computer crewed for that
craft, **disarmed**, whatever it was doing when saved: a computer saved mid-burn or mid-coast comes
back idle with its targets and flies nothing until armed again. A setting left at its default is not
written, so a default changed in a later release reaches that save. Restoring a flight in progress — the
phase, the latched arrival, the aim bias, the trim's debt — is not built.

**The delta-v readout is one stage's except on the craft being flown.** KSA reports the whole stack
only for the controlled craft and otherwise the running stage's engines, which is why a shot short of
the propellant is flown and reported rather than refused: a launch gate built on that number would
turn away every multi-stage rocket nobody is flying. It flies, falls short, says by how much, and holds
its warheads, because releasing on a trajectory known to fall short scatters them over whatever is under
the short fall.
