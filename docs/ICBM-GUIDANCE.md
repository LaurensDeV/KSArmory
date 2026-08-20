# The ballistic computer

**What it does.** Fly any rocket a player has built, from a standing start on the pad to a place
they picked on a map, and let the warheads go over it. Nothing about the vehicle is declared to it:
it reads how much thrust the stack has, how fast it is consuming itself and how much is left, every
cycle, and stages when there is nothing to burn.

**Where it lives.** The whole of the decision-making is under `Sim/` and tested headlessly. `Ksa/`
contains two conversions and nothing else — the world into an `IcbmState`, and the answer into
writes on somebody else's vehicle.

---

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

---

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

---

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

---

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

---

## Cutting off is a timing problem

An engine can only be shut down on a frame boundary. A light upper stage at ten gravities changes
its velocity by more in one frame than any sensible tolerance allows, so **waiting for the velocity
still to gain to fall below a fixed number waits for something that cannot happen**: it overshoots,
turns round to brake, overshoots the other way, and burns the stage dry hunting.

So the program is **stepped every frame and solves a few times a second**. The solve sets a
countdown; the frame runs it down; the engines stop when less than half a frame of burning is left,
which puts the cutoff at the frame boundary nearest the ideal instant. Inside three quarters of a
second of cutoff it solves every step, because those thirty frames decide the shot.

**The throttle comes back for the last second and a half**, which divides the residual by the same
fraction. Nothing depends on the vehicle honouring it: the cutoff test is written against the
throttle the vehicle *reports having*, not the one that was asked for, so a stack with solid motors
gets the error it would have had rather than a wrong answer.

---

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
every body.

**The residual has exactly one lever.** An engine stops on a frame boundary, so the last frame adds
`acceleration x step x throttle` and the best any cutoff rule can do is stop at the nearer of the
two boundaries — leaving a quarter of that on average. The frame rate is given and the sensitivity
belongs to the trajectory, so the only term left is the throttle: coming back to
`MinCommandedThrottle` for the last couple of seconds divides the whole miss by the same fraction.
That is the entire reason the throttle ramp exists, and it is why it is written against the throttle
the vehicle *reports having* rather than the one commanded — a stack that cannot throttle gets the
error it would have had rather than a wrong cutoff.

## What it commands, and what it does not

`Ksa/VehicleCommand.cs` is the only place this mod flies somebody else's rocket, and every write in
it is one the game already makes for itself:

| | KSA call | What the engine uses it for |
| --- | --- | --- |
| attitude | `FlightComputer.AttitudeTrackTarget = Custom` | pointing a kitten's manoeuvring unit |
| aim frame | `VehicleReferenceFrameEx.GetTgt2Cci` | its own `Toward` track mode |
| ignition | `Vehicle.ProcessInput(MainEngineStartup, …)` | the keyboard |
| throttle | `Vehicle.ProcessInput(MainEngineThrottleUp/Down, …)` | the keyboard |
| staging | `SequenceList.ActivateNextSequence` | the keyboard |

Nothing is patched and nothing private is reached for, which is what turns a KSA update into a
build error rather than into a rocket that flies sideways.

**The attitude frame is `EclBody`** rather than the local horizon, because its frame rates are
zero. A commanded inertial direction wants no feed-forward, and the horizon frame's rates would
have the flight computer chasing a rotation nobody asked for.

**The rotation is built by the engine's own `GetTgt2Cci`** rather than by hand. Building one here
would mean guessing which body axis is the nose and which way the roll reference goes, and getting
it wrong gives a vehicle holding a perfectly steady attitude ninety degrees from the one asked for.

**Commands take a frame to arrive.** KSA copies a vehicle's control inputs into its worker state in
`PrepareWorker`, which runs before this mod's hook. That is the same latency the player's own
keypress has, and it is why the cutoff is timed rather than waiting to observe one.

**Throttle is a servo, not an assignment.** KSA exposes no way to set one outright — only the two
controls a player holds down, which move it at a fixed rate — so `DriveThrottle` works it toward
the wanted setting and reports what the vehicle actually has.

---

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

The coast afterwards is not held, because a coast is not being integrated by anything. Once the
warheads are away they are rounds, and the existing round machinery holds the world for them.

`IcbmFlightTests.AtAStepTooLongToCutOffOnItMissesBadly` is the test that fails if the limit is ever
loosened on the grounds that guidance "seems fine".

---

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
arrive within two kilometres.

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

**Where the plane change belongs falls out rather than being told.** Costing departures and taking
the cheapest puts the burn a quarter of an orbit before the target, and makes it 97% normal to the
plane — which is the textbook answer, arrived at without the textbook. `OrbitPlaneTests` pins it,
because if the search ever stops finding it the shots simply get expensive and nothing says why.

**What none of it fixes is an inclination.** A latitude the orbit never reaches is not reached by
waiting, so the search reports the *closest* the target ever comes to the plane across the whole
horizon, and the panel says which case it is: a number that falls to nothing later is a wait, and a
floor well above zero is an orbit that does not go there. Those two are indistinguishable from the
instantaneous angle, and only one of them has an answer.

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

Not only while an engine is lit. A hold can be an hour long with the vehicle pointed at a burn for
all of it, and after cutoff the bus has to keep the line it was cut off on for the warheads to
leave along it. Leaving either free is a vehicle drifting when it should be settled.

## The wait is handed to the game, and taken back carefully

A hold of an hour and a half is not something to sit through at one times, and KSA already has a
warp-to-a-time. `IcbmConfig.AutoWarpToWindow` asks for it. Only for the craft being flown: warping
the world is not something a computer on some other vehicle gets to decide.

**Getting out of that warp is the part that needs care**, and getting it wrong pauses the game. KSA's
auto-warp is still travelling when it reaches its target, so a hold that starts while it runs is
trying to brake the world from a thousand times speed in one frame — and the first speed the policy
computes from a step that size is nearly zero. Measured: `simulation speed 1213.07x -> 1.00x`, then
`timewarp held at 0.0x`, then `0.00x (paused)`, then the burn abandoned for the world not running
slow enough to simulate it.

Two rules come out of that, and they are the same rule twice:

- **The mod does not compete with the game's own warp.** While a computer is holding and an
  auto-warp is running, it asks for nothing.
- **It ends the warp itself** when the window is close, rather than letting it run out. Stopping an
  auto-warp resets the speed, so the hold starts from something it can work with.

And `MaxFaithfulStep` is deliberately no tighter than a round's. Asking the world to run slower than
anything else in the mod needs buys a few hundred metres and costs the shot, because the policy
answering that request is a control loop against an actuator shared with the player.

## A stable orbit is known not to come down

The impact prediction is flown, and a vehicle in a stable orbit never arrives — so flying it means
integrating the whole six-hour horizon, several times a second, to reach that conclusion. Which is
precisely the state a computer holding for a burn window sits in.

`Kepler.PeriapsisRadius` answers it from the conic instead, for nothing. The clearance required is
more than any mountain rather than merely positive, because terrain stands above the mean sphere the
conic is measured against.

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
round lands 2,709 km downrange — **54.6 km short**. With the warhead's own `DragK` in the predictor
the two agree to **40 m**.

`ImpactPredictor.Drag` carries the density lookup and the munition, and the acceleration goes
through `Medium.Drag` — the same call the round makes, deliberately, because a prediction that
models drag its own way is a second flight model to keep in step with the first. Airspeed is
measured against the turning air via `BallisticBody.GroundVelocityCci`. Two consequences worth
keeping:

**And *where* the air is sampled matters, though less than it first appeared.** The round's own
update read the air's velocity once, at the platform, and used it for every round it owned.
Against a *stationary* platform that is worth **29.4 km** on this shot — the air over a launch site
and over a target 2,700 km away moves 24° apart. Against a **bus**, which is what actually throws
these, it is worth almost nothing: the bus coasts on nearly the warheads' own arc, so it stays
beside them the whole way down. Flying it changed the impact by under a kilometre.

The fix stands — gravity and density were already read at the round's own position and the air
should be too — but it is not what closed the miss, and the headless number was measured against
the wrong kind of platform.

Flown: 59 km short with no drag, **17-20 km** with it.


- **The step has to come down in the air.** The coarse step is sized for a vacuum arc where the
  acceleration barely changes across it; entry sheds most of the speed in tens of seconds.
  `AtmosphericStepSeconds` applies only once there is density worth integrating, so a coast pays
  nothing.
- **`DragK = 0` reproduces the vacuum answer exactly**, so nothing above the atmosphere moved.

**And this is what made the aim correction below inert.** It observes the *prediction*. A difference
the prediction cannot see is a difference no amount of correcting removes — the loop drove the
drag-free prediction onto the target, reported convergence, and the warheads went on landing 59 km
short. Flown. The instrument, not the loop, was the fault.

## The prediction is of the warhead's state, not the bus's

A warhead does not leave on the bus's velocity. Each is ejected along its own tube at
`MunitionProfile.LaunchSpeed`, and a bus's tube cants cancel in the mean — the MIRV bus's six point
along part `+X` with ±6° of cant — so what survives is the **whole** of that speed along the nose.

On a deorbit the nose is held **retrograde**, because that is the attitude the braking burn ended
on. So the ejection slows every warhead and they all fall short together. Measured on this
trajectory: **1.8 km per m/s** purely retrograde, and the radial axis is worth more again
(3.4–4.3 km per m/s), so 2 m/s off the tube is kilometres.

Predicting the bus's arc instead of the round's leaves that entirely invisible to the aim
correction — the same shape as the drag fault above, and found the same way, by measuring what the
prediction and the round disagreed about rather than reasoning about it.

**And the direction has to come from the tubes, not from the commanded attitude.** A vehicle
settles on an attitude command and stops a few degrees off it; the tubes go where the airframe is.
Measured in flight at **ten degrees apart** — the release line reported the tubes at a mean of 71°
from the platform's track while the prediction assumed 81°. Two metres a second misapplied by ten
degrees is 0.35 m/s thrown the wrong way, and radially that is **1,181 m** against a measured
common-mode miss of **1,088 m**. `IManualFire.TryLaunchAxisEcl` is the launcher's own axis, with the
tube cants cancelling in the average by construction.

Two other candidates were measured and killed rather than argued about: the spin the tube's lever
arm adds to a released round is 0.007–0.039 m/s (at most 133 m), and the rounds leave 10 m below
the craft's analytic orbit position the prediction is taken from (134 m).

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

## The correction is also right for one release epoch, and the bus lets go at another

**Unfixed. Measured headlessly; the mechanism is established and the flown magnitude is not.**

Flown, four deorbits of the same 2,300–2,700 km range, every one cutting off within 0.1 km of its
own prediction: the warheads landed further off the later they were let go — 431 m at a ~50 s
release, 8.2–9.0 km at ~106 s, and grouped tightly each time. A tight group far from the target is a
bias, and nothing was burning, so something moved the shot while the bus coasted.

**Nothing moved the arc.** `LateReleaseTests` flies one converged cutoff state and releases the same
warhead at t+0, t+50, t+106 and t+200 with the separation spring switched off: the impact does not
move at all — 0.0 m at t+50, 0.1 m at t+106. Every epoch term in the prediction cancels exactly, so
this is not the frame bookkeeping.

**What moved is the spring's leverage.** Two metres a second applied at cutoff walks the impact
**8.421 km**; the same two metres a second applied 106 s down the same arc walks it **5.672 km**.
The correction converges during the burn against a prediction flown from `CutoffPositionCci` with
the ejection kick already added — a release the instant the engines stop — so it takes out the
leverage the kick has *there*, and every second the bus then holds the warhead is leverage already
spent. Converged for t+0 the shot is exact at t+0 and **2,748 m** off at t+106; converged for t+106
it is 2,745 m off at t+0 and exact at t+106. One quantity, taken out at the wrong point on the arc,
and symmetric in the delay.

It is exactly proportional to what the spring throws — 1.374 / 2.748 / 5.496 km at 1, 2 and 4 m/s —
which is the shape that says the ejection is being mis-timed rather than the arc being wrong.

**The other candidate is real and an order of magnitude too small.** `AimCorrection.BiasCci` is a
free vector in the body's *inertial* frame while the target it is added to is a point on a turning
planet, so an aim that is right at one instant goes stale at the next — and the loop stops
re-converging for the whole of the bus trim, which was 48 s in flight. Measured over 106 s with the
136 km bias rising ground drives: **0 m at the equator, 469 m at 26.5°, 910 m at 60°**. Most of what
goes stale is radial and `Apply` renormalises that away, which is why it is second-order rather than
`bias × ωt`. Correcting it needs the spin axis, so it is a signature change rather than a local one.

**The fix is to model the release at the epoch the release happens**, in `IcbmComputer.Predict`:
coast the cutoff state forward by the bus's own cutoff-to-release delay before adding
`ReleaseOffsetCci` and `ReleaseImpulseCci`. It can only be right for one warhead of a salvo — six
released over twenty seconds still spread by the same rate, about **26 m per second of delay** at
2 m/s from a 200 km deorbit — so the second half of the answer is releasing promptly.

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

**What that flight also showed is that the prediction is sound.** Against each round's own impact it
was out by a mean of **647 m** and as little as 100 m, on a salvo that missed by 7.4 km — so the
flight model was tracking the rounds while the aim was wrong. Separating those two is what the
release probe is for.

## What is left, and the shape of it

Flown, on a 2,500-2,800 km deorbit onto a target 4 km up: **best 371 m, CEP ~710 m**, from 59 km at
the start of the work. Every warhead's miss is one common offset plus its own tube cant, and the two
are separable by regressing the six misses against their tube's cant.

**The cant term is geometry and is not a defect.** Six tubes on a 6° cone, one aim, so each warhead
leaves on its own vector — ±400 to ±590 m depending on range. Removing it means aiming each tube
separately, which is what a real bus does by re-pointing between releases.

**The common term is a constant ~730 m, and its signature is the useful part:**

| flight | range | cutoff residual | common offset |
| --- | --- | --- | --- |
| 17:24 | 2,806 km | 0.29 m/s | 727 m |
| 17:36 | 2,521 km | 0.12 m/s | 734 m |

It does not move with the cutoff residual — halving that changed nothing — and it does not scale with
range, so it is a **fixed distance rather than a fixed angle**. The release probe reads 0.1 km on
both, so it is a difference between the prediction and the round rather than an error in the aim.

A term that is constant in metres, independent of trajectory and of how well the burn ended, is
per-frame rather than per-flight. That is the epoch family, and the item it turned out to be is the
next section.

## The ground a round meets, and a correction that did not survive flight

**Open.** A round lands further from its own release prediction than either the flight model or the
predictor can account for, and the obvious fix made it worse.

`IGroundTest` answers with a **body centre and a surface radius**, and the round holds both for the
frame — one terrain lookup instead of one per sub-step, which is what makes a 150-shell burst
affordable. The centre is a sample of something moving at ~29.8 km/s in the ecliptic and the round's
own position carries that same motion, so on the face of it a round crossing the frame against a
frozen centre reads the carrier as a change of **altitude**, which on a ~5° arrival is eleven times
as much ground.

Headless, in a frame translating at 29.8 km/s against the same shot at rest, that reads as 850 m on
a 16.7 ms frame and up to 10.8 km on a long one — and carrying the centre forward by the ground
frame's velocity removes it completely, leaving the round 60 m from its own prediction instead of
790.

**In flight it did the opposite.** The last build without the carry put six warheads 431 m – 1.4 km
out, each within 0.3–1.2 km of its own release probe. Every build with it has been worse, and the
cleanest of them is unambiguous: cutoff residual 0.24 m/s, bus trimmed to 0.017 m/s, **release probe
0.0 km**, warheads down at **6.1–7.2 km**. Nothing upstream was wrong; only the round's own flight
disagreed with the prediction of it.

So the carry is reverted. Two things are worth keeping from the attempt:

- **The symptom is diagnostic on its own.** A prediction that does not move while the rounds do is
  the signature `docs/FRAMES-AND-EPOCHS.md` already records from an earlier flown failure — a carry
  applied to a sample that was already in phase. It is the same shape, and the size is right: the
  phase was measured as worth about 7 km taken the wrong way, and the flight lost 6.5.
- **Every headless rig flies the round about a planet sitting still at the origin**, which is the one
  case where a carrier fault is identically zero — so a rig that introduces a carrier can measure a
  real sensitivity and still get the *sign* from an assumption nothing tests.

What settles it is the phase at which `IGroundTest` writes the centre relative to the round's own
position, and that is a fact about the engine's frame order rather than about this maths. Until it
is measured in flight rather than reasoned about, the round keeps the behaviour that flew best.

## The world slows itself for the entry, not for the coast

Flown at 10x simulation speed, with no clamp and no discarded time — the step stayed inside what a
round can integrate — six warheads landed **381 km long**. At 1x the same shot lands within a
kilometre. Long means too little drag, and the step was ~170 ms against ~17 ms at 1x.

Part of that is a round reading the air **once a frame** and holding it across every 5 ms sub-step:
density falls off on an 8 km scale height and a re-entering round covers more than a kilometre a
frame, so it flies the whole frame through the thinner air it had at the top of it. `Slug` now
re-samples inside the sub-step loop through `AirDensityAt`, which halves the frame-length
sensitivity — measured across 17, 170 and 320 ms frames.

**The 381 km is not in the fall, and that is now measured rather than assumed.** Sweeping the frame
step from 1 ms to 320 ms on this trajectory moves the impact **smoothly and linearly, about 1.7 m
per millisecond of frame** — 249 m at 170 ms, 550 m at 320 ms. No threshold anywhere. Three
candidates were killed outright:

- **Sub-step saturation** cannot fire inside the legal range: `steps = min(64, ceil(dt/0.005))`
  holds the effective sub-step at exactly 5.00 ms at both 170 ms and 320 ms, and only grows past
  `dt > 0.32 s`, which is where the clamp already refuses.
- **Atmospheric skip** is real as a regime but nowhere near this shot: the flown arc's perigee is
  **675 km below the surface**, a committed entry. The 17 ms and 170 ms trajectories lie on top of
  each other the whole way down. The worst frame-length error obtainable anywhere in the skip family
  is 57.7 km, on a 12,500 km grazing arc unlike this one in every respect.
- **A missed ground crossing** is bounded by one frame of travel — 561 m at 170 ms — because the
  stale radius is re-read at the top of the next frame and the round detonates on the first sub-step
  below it. It cannot tunnel: a sub-step is tens of metres against a 6,371 km sphere.

So the cause is **still unidentified**, and it is somewhere other than the round's own integration.
What it would take is large: +371 km needs 203 m/s prograde, or 90 m/s radial, or 28 km of release
altitude.

`Interceptor.MaxFaithfulStep` was the wrong guard for it either way. It is 0.32 s because that is
where a round starts stepping over its own **fuse radius** — a rule about proximity fusing, and
nothing to do with atmospheric entry, where the same step is worth hundreds of kilometres.

**So a round is asked what step it needs rather than its profile being consulted.**
`IProjectile.FaithfulStepSeconds` answers from where the round actually is: a warhead coasting in
vacuum still allows a third of a second, and the same warhead in air demands
`Medium.FaithfulStepInAir`. `WeaponSystems.FaithfulStep` takes the minimum across everything
airborne, which is what `WarpPolicy` already holds the world to.

That is what makes a six-minute fall watchable **and** accurate: the long coast above the atmosphere
warps at whatever the player asks for, and the world slows itself for the minute of entry that
decides where the round lands. Nobody has to know to do it by hand, and the accuracy does not depend
on their remembering.

## Each tube is aimed before it fires, and the launcher may let go of its stack first

A launcher's tubes can be canted — a MIRV bus's six sit six degrees off its own axis at six clock
positions — so rounds released from one attitude leave on six different vectors. There is one aim
for all of them, so no aim correction can remove it. Measured in flight: **about 1,200 m across six
warheads**, and regressing each warhead's miss against its own tube's cant gives **−3,246 m per m/s**
of lateral impulse against an independently measured radial sensitivity of −3,401. It is the tube
geometry printing itself onto the ground.

`Sim/ReleasePointing.cs` turns the vehicle by one cant before each release so the tube about to fire
lies on the mean of the tube axes — which is the line the aim correction already assumes a round is
thrown along. Flown headlessly through the real drag model from one cutoff state: **1,730 m of
spread as canted, 0 m re-pointed**, and unchanged by the vehicle's roll.

**It costs nothing on a launcher it does not describe.** A single tube is the mean of its own axes,
so the rotation is the identity, the cant spends none of the release budget, and
`Sim/ReleaseSequence.cs` reduces to releasing when the vehicle is steady. No flag and no branch.

Two things it is careful about:

- **The command is rotated, not rebuilt.** Both the direction and the roll reference are turned by
  the same quaternion. Building an aiming frame here means deciding which body axis is the nose, and
  getting that wrong is a vehicle holding a perfectly steady attitude ninety degrees from the one
  asked for.
- **The tube axis is latched at the nominal attitude.** Feeding it the live axis instead never
  settles: the tube alternates between on the line and a full cant off it, and half the time it
  looks perfect.

### The release gate is one budget, not two thresholds

How far off the line the tube is and how fast the vehicle is sweeping it are **the same quantity**:
lateral velocity put on the round, in metres a second at the tube. A cant of `θ` puts
`2·sin(θ/2)` of the ejection speed there — 0.017 m/s at half a degree on the bus's 2 m/s — and the
sweep is already measured in it. `ReleaseSequence.LateralBudgetMetresPerSecond` is the sum of the
two against 0.05 m/s, which on a deorbit's ~3,400 m per m/s is about 170 m of spread.

Two independent thresholds compare nothing, and a marginal vehicle is where that shows. Flown on a
separated bus whose sweep floored at **0.113 m/s** against a 0.05 gate:

```
09:36:27.212  turning onto tube 1, 6.0 deg to go
09:36:31.411  turning onto tube 1, 0.5 deg to go      <- on the line, refused for being unsteady
09:36:35.726  tube 1 is not following the turn, 7.0 deg off against 6.0 when it started
09:36:51.044  releasing tube 1 late, 5.1 deg off the line and tubes sweeping 0.058 m/s
```

The moment it refused was **0.130 m/s** at the tube; the one it took fifteen seconds later was
**0.291**. It chose the worse of the two, and no tightening or loosening of either threshold fixes
that — the pair cannot see that one term is spent against the other.

**Where the budget cannot be met, the pointing is spent as far as it will go.** A sweep that is a
floor is not a wait: the release goes at the pointing's own best, which is recognisable only by it
passing — `ClosingDegrees` past the closest the turn ever got. Reproduced headlessly on those flown
numbers, the release moves from **23.9 s at 5.1° to 4.4 s at 0.8°**, and 0.291 m/s at the tube to
0.139.

**What arms that is the sweep having stopped coming down**, over `StoppedImprovingSeconds` and with
the vehicle at that floor now rather than at the top of a swing. Both are needed and neither alone
would do: a vehicle still braking its own rotation improves its floor every few tenths of a second,
and one still oscillating is *on the line* and *still* at different instants, so it can satisfy the
pair only once it has stopped. A rigid stack therefore never reaches the fallback and is released
inside the budget, which `ReleaseSequenceTests.AVehicleThatCanHoldIsStillReleasedInsideTheBudget`
pins — against the two thresholds that flew, the same vehicle went at **0.062 m/s**, outside a budget
it could have met.

The alignment threshold stops existing as a number of its own: what a cant costs is the whole
budget, 1.4°. An absolute angle picked beside a separate sweep gate cannot be right, because which of
the two binds depends on the vehicle.

**None of this has been flown.** It is arithmetic and a headless reproduction of one flight's log.

### Nothing paces a salvo it is not re-pointing

Releases are one at a time only because each tube wants a different attitude. With no axes latched —
`IcbmConfig.RepointBetweenReleases` off — every tube wants the same one, the only gate left is that
the vehicle is steady, and the magazine empties in consecutive frames: **five rounds in 67 ms**,
flown, and all six impacts inside 32 ms of each other.

That is the intent rather than an oversight. Warheads off one release state share a time of flight,
so their errors are one common offset plus each tube's own cant; a paced salvo gives each of them a
different flight time on a bus that is falling toward its release altitude, and that differential is
a larger error than anything the pause would buy. Nothing in this simulation is bought by the pause
either — rounds do not collide with each other, and six tubes ejecting at 2 m/s on a 6° cone separate
at 0.2–0.4 m/s regardless of whether they leave together.

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

Flown on a separated ~6,300 kg bus, the reported angle climbed **monotonically from 5.9° to 10.3°
over seven seconds** and the sequencer spent the whole per-tube clock on it. The last two rows are
given up on in seconds rather than at the sixty-second timeout, and each says which one it was — the
rest of the salvo then goes on the mean axis, which is the same shot the setting-off case gets.

Whether that flight's drift was residual tumble from the decoupler, or something moving the command,
is **not established**: the sequencer's own command is provably constant, so the log now separates
"still coming round" from "not following", and every release records which tube went, how far off the
line, and how fast the tubes were sweeping. Six impact points are only diagnosable against the six
release states that produced them.

**An unresolvable tube axis makes no claim.** The angle between a degenerate direction and the
reference is zero, so reading it as a measurement releases every warhead the instant the launcher's
part tree stops answering — the same rule as an unreadable height field not standing in for flat
ground.

### Separating first, when the part tree offers a joint

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

Five things it is careful about, and each is a way it went wrong first:

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
- **It resolves onto the vehicle's own axes rather than pointing at the answer.** By the coast the
  attitude *is* the release line, so turning to burn and turning back costs the whole settle twice
  and disturbs the thing the sequencer is about to latch. KSA maps a nozzle to
  `TranslateForward`/`Right`/`Down` by which control-frame axis its thrust points along, so
  `KsaWorld.TryControlFrameCci` reads `Ctrl2Body ∘ Body2Cce ∘ Cce2Cci` and the flags fall out. And
  the dominant component is axial anyway: a decoupler pushes along the joint it came apart at.
- **One direction at a time, because the stop threshold is only knowable where it was measured.**
  Half a frame of thrust is the rule, and the acceleration in it is measured — the velocity change
  less what gravity did over the same interval — which is only attributable along the direction
  actually being fired. The shipped bus is four clusters of four laid out for pitch, yaw, roll and
  axial thrust, with **no lateral translation at all**, so a loop that assumed three-axis authority
  would push at nothing for ever. A direction that fires without moving its own component is struck
  off and the next one tried, which is what lets a bus with only an axial pair still get the axial
  error out.
- **It may not finish before the shove has arrived.** The split is deferred through the engine's
  input buffer, so for the first frames after it is asked for the bus is still exactly on its
  solution. Stopping there is stopping on a problem that has not arrived, and nothing afterwards
  looks again. `SettleSeconds` is the guard, and it is also why separation now runs *before*
  anything decides whether a warhead may go: flown, the first round left at `23:04:26.025` and the
  split landed at `.071`, which is how one warhead came off the attached stack and five off the
  shoved bus.
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
answers `NeedsShortSteps` for it — and the known next lever, if the flown number turns out too
coarse, is KSA's own `FlightComputerManualThrustMode.Pulse`: a 5.4 ms pulse every 0.15 s is the same
trick as the burn's throttle ramp at a ~3.6% duty cycle.

## The attitude at cutoff is held, not solved

Velocity still to gain is a *difference*, so as it closes on zero its direction is the difference of
two nearly equal vectors and swings wildly — measured in flight at **161 degrees between consecutive
samples**, right at the cutoff instant. Steering to that spins the bus at the exact moment it should
be holding still, because the warheads leave along the line it was cut off on.

So below `IcbmProgram.HoldDirectionBelow` the last direction that meant something is held, and the
coast keeps it rather than swinging to prograde.

## What it tells the operator

- **`IMPACT IN mm:ss`** — a countdown, taken from the plan while the burn is running and from the
  flown prediction once it is not. The two disagree during the burn on purpose: the plan assumes it
  finishes, the prediction assumes it stops now.
- **`Burn starts in mm:ss`** while holding, so a computer sitting there doing nothing for an hour is
  distinguishable from a broken one.
- **`TARGET UNREACHABLE`**, in two flavours: no trajectory arrives there at all, or one does and the
  tanks cannot pay for it. The second is stated with the shortfall and with the caveat that it is
  measured with one stage's exhaust velocity over the whole vehicle's propellant — which understates
  a deeply staged rocket, and understating is the right way round to be wrong.
- **A mark on the target** that stays on screen wherever it is, clamped to the edge when it is out
  of view, with the countdown beside it.

## Not done, and not verified

**The flight works end to end and the numbers above say how well.** `CHECKLIST.md` §12 is the list
of what has been confirmed and what has not, and it is the one to read — what follows is only the
parts a headless test cannot reach at all.

**The bus trim has been flown once, and it destroyed the shot.** The mechanism works: KSA's
translation flags do reach the bus's nozzles, and they were measured at **0.9–1.1 m/s²** — so the
thruster half of it is confirmed, and the loop closed at exactly that rate between disturbances. The
feedback loop above is fixed and **that fix has not been flown**. Nothing else about the trim has
been seen working end to end yet.

- **Which way the nose points.** `GetTgt2Cci` is the engine's own aiming frame and is used exactly
  as the engine uses it, but whether KSA's idea of a vehicle's nose matches a player's rocket has
  not been seen. A wrong convention is a rocket that holds a steady attitude in the wrong
  direction, which the drawn trajectory would show immediately.
- **Whether the flight computer can hold the commanded attitude** on a stack with marginal control
  authority. The rig assumes a 12 deg/s slew; a real vehicle with no gimbal and no RCS has far less.
- **Staging.** `ActivateNextSequence` fires whatever the player put in the next stage, which is not
  necessarily an engine.
- **Staging under warp.** The world is now held down for a burn (see below), but whether KSA
  applies thrust and staging faithfully at the speeds it is held *to* has not been watched.

**Other bodies are out of scope, and "anywhere" means anywhere on the body being flown around.** A
ballistic arc is a two-body problem about one planet. A target on another world is an interplanetary
transfer — escape, a heliocentric leg, capture — which is a different manoeuvre rather than a longer
one. The panel says so when the designated body is not the parent.

**Nothing is persisted.** The target and the settings are lost on a reload; `SettingsStore` keys per
craft and per launcher ordinal, and this roster keys per craft, so the two do not line up yet.

**The delta-v readout is one stage's.** KSA reports the running stage's engines, which is why a
shot short of the propellant is flown and reported rather than refused: a launch gate built on that
number would turn away every multi-stage rocket in the game.
