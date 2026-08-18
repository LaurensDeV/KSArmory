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

**The target is a latitude and a longitude, never a position.** `Sim/AimSite.cs`. The same rule
`AimpointKind.Ground` exists for, at a thousand times the flight time.

**And it is aimed at the real ground, not the mean sphere.** The solve is a transfer between two
*points*, so a target standing five kilometres up is hit by aiming at where it stands — no terrain
model anywhere in the guidance and no correction afterwards.

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

## Not done, and not verified

**None of this has been flown in game.** Everything above is measured headlessly. The parts most
likely to be wrong are the ones a test cannot reach:

- **Which way the nose points.** `GetTgt2Cci` is the engine's own aiming frame and is used exactly
  as the engine uses it, but whether KSA's idea of a vehicle's nose matches a player's rocket has
  not been seen. A wrong convention is a rocket that holds a steady attitude in the wrong
  direction, which the drawn trajectory would show immediately.
- **Whether the flight computer can hold the commanded attitude** on a stack with marginal control
  authority. The rig assumes a 12 deg/s slew; a real vehicle with no gimbal and no RCS has far less.
- **Staging.** `ActivateNextSequence` fires whatever the player put in the next stage, which is not
  necessarily an engine.
- **Timewarp.** Cutoff precision is bounded by the step, so a boost flown under warp is less
  accurate in direct proportion. Nothing holds warp down during a burn; `WarpPolicy` does that for
  rounds in the air and has not been extended to cover this.

**Other bodies are out of scope, and "anywhere" means anywhere on the body being flown around.** A
ballistic arc is a two-body problem about one planet. A target on another world is an interplanetary
transfer — escape, a heliocentric leg, capture — which is a different manoeuvre rather than a longer
one. The panel says so when the designated body is not the parent.

**Nothing is persisted.** The target and the settings are lost on a reload; `SettingsStore` keys per
craft and per launcher ordinal, and this roster keys per craft, so the two do not line up yet.

**The delta-v readout is one stage's.** KSA reports the running stage's engines, which is why a
shot short of the propellant is flown and reported rather than refused: a launch gate built on that
number would turn away every multi-stage rocket in the game.
