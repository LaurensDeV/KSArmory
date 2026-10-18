# Releasing the RVs nose-first

**A record: built, flown, and on since 2026-10-18** ("What was flown"); how it works now is `docs/ICBM-GUIDANCE.md`. One turn of the bus after the split, so every reentry
vehicle leaves with its nose along the airflow it will meet, held through the whole walk. Drawn up 2026-10-17 from
three read-only surveys of the code, then revised twice after adversarial review: two reviews (one against the
source, one against what has been flown) found the first draft's blocking faults, and a third found three more in the
revision. Each is answered below where it applies.

## What was flown, 2026-10-17

**Built, flown, paired, and off by default** (`IcbmConfig.PointRvsAtEntry`), because of one open item below.

- **Step 0 answered: the bus follows a large turn.** `TurnTestDegrees` 90 on the post-boost bus: settled within
  0.35 deg in 5.1 s on 1.04 kg, nothing owed by it (0.002 -> 0.002 m/s), back in 7.3 s, 7 of 7 within 5.1 mm.
- **The allowance had to hold the trim, not just the turn.** At `TurnAllowanceSeconds` 20, sized on the turn alone,
  the first one-target flight cleared its stack in 5 s, nulled 1.67 m/s in another 23 and reached the turn 8 s past
  the 350 s gate: it kept the cutoff line and every RV met the air 164.8 deg off. At 60 s it turns.
- **Single flights, flag on, 6,181 km:** post-boost bus one target, 162.7 deg in 9.0 s on 1.45 kg, 0.1 deg at the air,
  7 of 7 within 3.8 mm; the seven-target walk, one turn of 165.2 deg held through all seven releases, 0.1-0.2 deg at
  the air, worst 6.0 mm; the old bus, 159.6 deg in 7.3 s on 1.05 kg, 0.1 deg, 6 of 6 within 2.8 mm.
- **The declared paired night**, `PBB SCALE 8`, 6 blocks of 8 a cell, `~/shots/2026-10-17-rvpoint/DECLARE.md`.
  One target: worst warhead 0.82x [0.65, 1.03], landing 0.97x [0.79, 1.12]. Seven targets 2.5 km apart: worst
  0.86x [0.62, 1.19], every bus walking all seven stops on both arms. All 336 entry RVs met the air within 0.30 deg
  (base 162.5-164.4), 48 turns, none capped or kept, a median 1.44-1.45 kg each. The worst-warhead ratio is
  `score.py` beside the declaration, a geometric mean per flight with a t interval, since `shot-report.py` has no
  such endpoint; its own landing interval for seven targets is [0.60, 1.68] at 97%. Frames ran 26-29 ms.

**The short-range walk, answered 2026-10-18.** With the flag on, `WalkRigTests`' forced four-stop walk at 2,000 km
lost stops 2-4. Two causes, both in the turn's timing. Its "no room" exit kept the cutoff line whenever the null
finished past the release gate -- after every pass was aimed for the kick along the entry line, so keeping the line is
what missed; at 2,000 km the coast is shorter than the gate and the exit always fired. And the 60 s allowance was coast
the planner took from the stops: on the planned walk, 10 s cost 2,000 km a stop and 60 s cost 3,000 km one. Now the
bus turns however late and splits at the gate (`TurnAllowanceSeconds` 0), and in the rig it walks at least as many
stops as without at every range from 1,500 to 12,900 km. The forced walk sits on the 10 m/s pass ceiling either way
and is pinned off. Flown on `TEST PBB GeoSat FAT`, four targets 4 km apart from 10.622N 80.604W: on and off walk the
same two stops, 0.9-2.1 mm; on, the bus turned 147 deg 28 s after the gate, settled in 6.8 s on 1.04 kg, every RV
0.1-0.2 deg off its airflow.

**The second paired night, 2026-10-18**, on that code, `~/shots/2026-10-18-rvpoint/DECLARE.md`, 6 blocks of 8 a cell:

| cell | worst warhead, entry/base | walk | at the air | |
| --- | --- | --- | --- | --- |
| 2,000 km, 4 x 4 km | 0.92x [0.64, 1.33] | 1 target a bus, both arms | 0.20 deg max | **fails the 1.25x bound** |
| 6,181 km, one | 0.89x [0.75, 1.06] | -- | 0.10 deg max | pass |
| 6,181 km, 7 x 2.5 km | 0.88x [0.62, 1.24] | 7 of 7 on every bus | 0.30 deg max | pass |

The turn starts 17-29 s after the gate, settles every time, and spends a median 1.05 kg at 2,000 km and 1.44 at
6,181. At 2,000 km on the eight-rocket save seven of eight buses release everything on one target in both arms; the
bus being flown walks two or three. **By the declared rule it would have stayed off**: the 2,000 km cell's interval
reaches 1.33x with a point estimate of 0.92x, one flight at 1.58x. Pooled over both nights' 30 paired flights -- not
declared, so evidence rather than a pass -- the worst warhead is 0.87x [0.79, 0.97], 20 of 30 won. **Turned on by the
user's decision on that pooled evidence**, with the missed cell stated rather than re-flown.

## What is wrong today, and what is not

Through the coast the bus holds the line it was cut off on (`IcbmProgram.Coast.cs:140`), fixed in inertial space,
and the warheads leave along it. By the release the velocity has turned away from that line -- two-body estimates put
the entry velocity 58 deg from the cutoff velocity on a 900 s flight at 6,181 km, 103 deg on a 1,300 s one, 123 deg at
12,902 km. So an RV leaves side-on or backwards, and **in vacuum nothing turns it**: `BodyAttitude.Turn` does nothing
while `Authority` is zero, and density is zero above KSA's air boundary. It swings onto the airflow from about 130 km
down (hand estimate at 7 km/s), which is the flip a player sees. A drawing-only fix breaks two stated rules and the
tests that pin them (`BodyAttitudeTests.AStoreReleasedInVacuumDoesNotWeathervaneOntoItsOrbitalVelocity`,
`ReleaseAttitudeTests.InVacuumTheReleaseAttitudeIsPermanent`) and would draw a body sideways out of its tube. The bus
has to turn.

**In flight a round is a point mass** (`Slug`); its attitude is drawn only. A turn reaches the landing through two
things:

- **The ejection**, 0.5 m/s along the tube (`Arsenal.cs:455`). Read live at the release (`MeasureRelease`,
  `Release.cs:362-394`), so the kick and the trim see it -- but the *pre-split* prediction adds it along the cutoff
  line (`Prediction.cs:95`, at the `ReleaseCoast` point), so the burn's aim is solved for a kick that the turn then
  moves by 0.5-4 km. That has to be predicted along the new line too (below).
- **The turn itself, if the ring is not a pure couple.** On the old bus an attitude correction leaked 0.378 of net force
  per unit of torque until its mass was moved into the ring plane (MIRV-NEXT 523-575). `checkring.py`'s analysis at
  the post-boost bus's seats -- 0.520, 0.545, 0.565 and 0.600 -- gives **no leak on pitch, yaw or roll** at any of them:
  every rotation command is a pure couple, by design. That is the declared geometry; whether the turn shoves the bus in
  flight is part of step 0.

## Step 0, before anything is built: does the bus follow a turn?

**This reopens CHECKLIST 12.7a**, whose verdict was "settled": a 6 deg command after the split made the bus hunt
(tubes 5.2-14.1 deg off, every release timed out). MIRV-NEXT 5a blames the frame defect and a 22.11 deg band, both
fixed since (the band is 0.31 deg), but nothing flown shows the separated bus following a large command under the
mod's Strict hold. KSA's own retrograde hold slewed the bus alone 100 deg in 45 s (2026-10-17), at a 1 deg deadband,
which is not the same controller.

So the first flight is an instrument, not the feature. The bridge cannot command the turn through `VehicleCommand`:
the computer writes `AttitudeHook.Hold` every frame (`IcbmComputer.cs:1052`) and the hook is last-writer-wins per
vehicle (`AttitudeHook.cs:43`). So a developer bridge command **offsets the computer's own held line** by 60, 90 and
120 deg and **holds the release** for the test, on the bus just off its stack with all seven RVs aboard, and logs,
every second,
the attitude error, the rates, `ProbeControlLimits`, the propellant, `ProximityWatch`'s distance to the stage and the
velocity change the trim's own estimator measures. It answers: does it settle, how long a turn takes (which sets every
number below), what it costs from the tank, and whether it shoves. **If it hunts, the plan stops here** and goes back
to the controller.

## Which direction

**The airflow at entry, not prograde at release.** Prograde at the release is still 17-54 deg from it, because the arc
keeps turning through 350-800 s of fall, so a prograde RV would still flip. Aimed at entry, the RV looks off its own
path at release and closes on it as it reaches the air; the entry is the judged end.

- **From the live coast state, in closed form.** No release probe exists at the split (`ProbeRelease` is built per
  warhead inside `Release()`, `SalvoProbe` is forgotten at every warhead and hop). It does not need one: above the air
  the coast is a conic, and energy and angular momentum are the same at every point of it, so the bus's own Cci state
  gives the entry state to within the trim's few m/s. `Sim/EntryDirection.cs` finds the first *descending* crossing of
  the entry radius -- `cos nu = (p/r_e - 1)/e`, then position and velocity there from the orbit's perifocal frame --
  and returns the velocity less the ground's, `BallisticBody.GroundVelocityCci(r_e)` (`BallisticBody.cs:48`), all in
  Cci. **Not `KsaWorld.GroundVelocityAt`**, which is Ecl and carries the planet's 29.8 km/s.
- **The entry radius is where the air first turns a body**: the radius at which `BodyAttitude.Authority` becomes
  non-zero along the arc, about 130 km on Earth, found from the body's air rather than typed.
- **A solve that can fail returns a fallback**: an open or radial conic, or a body with no air (no entry at all), keeps
  the line it is holding.
- **One direction per walk, checked per stop.** The entry direction moves 0.13-0.20 deg downrange and 0.35-0.51 deg
  cross-track per 20 km hop at a 420 s time to go, so one line is within about 1 deg of every RV across today's
  affordable chains (about 22 km end to end). Recomputed at each hand-over; turned again only past 5 deg, which no
  shipped walk reaches.

## Where it is held: in the program, on a signal from the computer

The line is held in `IcbmProgram`, at `Coast.cs:143`, not in the computer. `Coasting()` runs only after cutoff, and
after cutoff the cutoff line is read only by `WatchTheCutoffTail` (first four frames) and `PointLeadSeconds` (Holding
only); nothing in `Sim/Icbm` compares the attitude to the command during the coast. So the roll advance
(`IcbmComputer.cs:1014`), `DriveDeployment`'s held line (`Release.cs:276`), the ejection fallback (`Release.cs:400`) and
`PastCutoffRig` (`:240`, `:546`) all follow from the one command, and the closed form is in `Sim/` where it is tested.

**But the program cannot see the split.** `IcbmState` and `IcbmCommand` carry nothing about the split, the clearance or
the trim; only the computer knows (`_didSplit`, `_trim.Done`). So the latch is **a new program input**, set the way
`ReleaseGateSeconds` is: `IcbmProgram.TurnToEntry()` latches the entry line from the state of that frame, and
`IcbmProgram.HoldWhereItIs(direction)` re-latches the attitude actually reached at the cap. The computer calls
`TurnToEntry` on whichever comes first after the split: **the null done, the null abandoned, no trim at all
(`TrimBeforeRelease` off), or the clearance given up** -- so a shot whose null never ends still turns rather than
releasing on the cutoff line with nothing said. The rig twins the computer's trim (its own `_trim` and `_postBoost`,
`PastCutoffRig.cs:113-114`), so it calls the same input off its own `BusTrim` on the same rule.

After a 100 deg step, `AimFrame.Advance` can find the old roll reference along the new aim and fall back to down, which
rolls the bus. Both shipped buses have straight tubes, so the roll moves nothing; a pack's canted launcher would scatter
by it, and the fallback is logged.

## The prediction predicts the turn

With the flag on, `Prediction.cs:95` adds the ejection along the entry direction **at the `ReleaseCoast` state**
rather than along the cutoff line. The burn is then aimed for the kick the RVs will actually leave with, and the turn
moves the predicted impact by the small difference between the predicted and the latched line -- about 0.05 deg, since
the null is 2.65 m/s against 7 km/s -- instead of 0.5-4 km. Without this the trim buys the jump back out of the walk's
budget, and the affordable chains shrink.

**After the split** `ReleaseCoast` no longer coasts and `ReleaseImpulseCci` returns the kick `MeasureRelease` reads off
the tubes every frame (`IcbmComputer.cs:848`), which still points down the cutoff line until the bus has turned. So
from the split until the turn settles, the prediction uses that measured kick **rotated onto the latched line**, not
`entry x LaunchSpeed`: replacing it would drop the lever-arm spin term (`WeaponSystem.cs:3301`).

**The `ReleaseCoast` point does not move with the gate.** The turn moves the split earlier, not the release, so the
point is taken at `ReleaseGate - T`, the instant the warheads actually go; the warp stop keeps the full gate.

## When, and what holds the release

**Only on a long shot that splits at the gate.** A short shot splits at cutoff, in air (`IsShortShot`,
`IcbmComputer.cs:995`), and a `ReleasesAtCutoff` shot never splits; with `WalkStartsAtCutoff`, or a coast with no room
for the gate plus the turn, the gate cannot open earlier. In all of those the flag does nothing and the line stays the
cutoff line, and the log says so once.

The order after the split:

1. **Clearance as today**, including the open-ended wait for the spent stack to open (`SeparationClearance.Check`,
   active through `TrimWaitsOutTheStack`). No turning a few metres from the stage: the bus's ends would sweep past it.
   The clearance usually ends in about 4 s, so it buys no turning time.
2. **The split null** as today -- the ~2.65 m/s shove nulled first, so the pinned conic does not drift through the
   turn (about 120 m in 45 s otherwise).
3. **The turn.** The program latches the entry line at the null's end, and the bus turns. **A term of its own in
   `DriveDeployment`**, beside `TrimHoldsTheRelease` (`Release.cs:279`), holds every release until the release
   direction has held within `PostBoostAim.SteadyWithinDegrees` (2 deg) for `SteadySeconds` (2 s) -- not only the
   trim's `Decide`, because an abandoned trim or `TrimBeforeRelease` off would otherwise let the warheads go mid-turn.
   The term returns before `_sequence.Update`, so the sequencer latches its reference after the turn and the release
   window is never reached while it holds.
   **`DriveTrim` skips `_postBoost.Update` while turning** (`Trim.cs:223`). Stepped, the turn is exactly the case its
   `_unsteadyFor` counts -- the null is done and the bus is not holding still -- and at `SettlesWithinSeconds`, 10 s, it
   ends the correction for the whole shot ("released after 10 s of the bus not holding still"). The latch also clears
   `_measureDue` and `_freshMiss`, because `_postBoost.Update` runs on the frame `_trim.Done` flips and a pre-turn
   reading could otherwise be the one the first pass consumes.
   It ends at a cap set from step 0's turn time, well under the 90 s holds the docs record failing (a ninety-second
   clearance hold put the probe 6.8 km out, `SeparationClearance`'s comment; the 1.20 stack wait had its first pass
   refused), and at the cap calls `HoldWhereItIs` with the attitude reached, so no jet keeps chasing a line through
   the trim.
4. **The stop count is latched at the split, and the gate with it while the walk walks.** The walk writes the gate
   every frame (`_walker.GateOverrideSeconds`, `IcbmComputer.cs:883`), and `_walker.Plan` accepts a new plan until the
   first warhead (`ReleaseWalker.cs:76`, `Reach.cs:75`), so pricing on turned axes can shrink the stop count and drop
   `closeEnough` (`Coast.cs:103`) on a bus already split. With the flag on, after the split the walker accepts **only a
   plan with the latched stop count** -- its prices refreshed, its stops not -- and the gate holds while it walks.
   **A walk that ends early** (`StopHere`, so `Walking` false) lets the gate go as today: it snaps back to 420 s and the
   remaining warheads wait for it. Latching it there too would release them at once, a confound between the arms of the
   night.
5. **The reach is re-flown on the turned axes, when there is a reach.** `FlyTheReach` (`Reach.cs:136`, every 5 s of
   real time) runs only with `ShowDivertReach` and a set to walk or a designation by click (`Reach.cs:21-30`); then the
   first release waits for one flight after the turn settles, and that flight refreshes the latched plan's prices.
   Otherwise nothing waits.
6. **The aim passes and the release** as today, and the held line lasts to the end of the salvo.

**The time is added once, to the gate.** `IcbmProgram.cs:941` returns `ReleaseGateSeconds`
(`Itinerary.FirstBeforeArrivalSeconds` for a walk, 350 s for one target); the turn's seconds go there, and in
`ReleaseItinerary` only into `CoastFits`' room and `StartsBeforeCutoff` -- never into the stop slots, where
`PriceEachHopAtItsSlot` would price every hop with too much fall and call it cheap. Moving the gate also moves the
`ReleaseCoast` point, the warp stop (`Warp.cs:158`) and the long-coast arrival freeing (`Coast.cs:106`); all three read
the gate and follow it. The longer fall costs about 2 m of stop band for 45 s, negligible beside the rest.

## Instruments

- **At each release**: the nose against the RV's own velocity and against its entry airflow, in Cci (`Kick.cs:370`
  already prints the platform-track angle in Cci; `WeaponSystem.cs:3304`'s is in Ecl).
- **One line per RV as it crosses the entry radius**: nose against airflow, on the live path and on the loose one. The
  verbose `attitude t<n>` line (`WeaponSystem.cs:2140`) gives no altitude, reads `VelocityLocal`, prints every 15th
  frame and stops once the bus is gone, so it cannot carry the bar.
- **The turn**: start, settle, cap, seconds, propellant spent from the tank, and the distance to the stage.

## Tests

- `EntryDirectionTests`: the closed form against `Kepler` propagation to the entry radius from several points of one
  coast (the same answer from each), the descending crossing chosen, the fallback on an open and a radial conic and an
  airless body, and invariance to one velocity added to the state and the ground.
- **A rig guard that is not tautological.** The rig has no tubes, and comparing the latched line with the flown
  round's entry airflow only re-tests `EntryDirection`. So the rig **models the bus's attitude**: it slews from the
  cutoff line toward the commanded one at the rate step 0 measures, its trim fires along the modelled attitude and
  each round leaves along it. The guard asserts the *modelled attitude at each release* against that round's
  ground-relative airflow at the entry radius -- under 2 deg on, 58-123 deg off, so it fails against the old code and
  against a release made mid-turn -- and every landing inside today's walk tolerance. The split shove uses the
  pre-turn nose (`PastCutoffRig.cs:352`).
- `ShotArmsTests.TheBaselineArmIsTheShippedBehaviour` asserts the default off.

## Flying it

1. **Step 0** above.
2. **Single shots** on `TEST PBB GeoSat FAT` (6,180 km), one target and the seven-target walk, verbose, then the old
   bus, since the flag is the computer's. Bridge captures at release and at 120 km for the picture -- the picture is
   the point, and not a statistic.
3. **A declared paired night.** `TEST PBB GeoSat FAT` carries one rocket and a one-block paired run ranks seats, not
   arms, so first an eight-rocket post-boost save (`make-scaling-save.py`). Declare the bound and the count before
   flying -- 6 blocks of 8 a cell, one-target and seven-target cells, as 4.12's nights were -- with the bar **no worse
   on the worst warhead** under the declared ratio, every stop walked, every RV within 5 deg of its airflow at the
   entry radius, and the tank's spend beside the landing. An eight-rocket world also changes the tumble at the split
   (40 against 5.6 deg/s, ICBM-OUTSTANDING 1.23), so step 2's single shots do not predict it.

## Flag and files

`IcbmConfig.PointRvsAtEntry`, on, in `check-tunables.py`'s `RESEARCH`; `BallisticSettings` and `ShotArms` take it by
reflection. New: `Sim/EntryDirection.cs`, its tests, and the step-0 bridge command. Changed: `IcbmProgram.Coast.cs`
(the latch), `IcbmProgram.cs:941` and `ReleaseItinerary` (the gate), `IcbmComputer.Prediction.cs` (the predicted kick),
`IcbmComputer.Release.cs` (the release term, the instruments), `IcbmComputer.cs` / `Walk.cs` (the latched gate, the
per-stop check), `IcbmComputer.Reach.cs` (the re-fly), `PastCutoffRig.cs`, and the CLAUDE.md layout table.

## Risks

- **The bus may not follow** -- step 0, and the reason it comes first.
- **The turn costs propellant from the tank**, not from the 60-70 m/s trim budget; holding the new line costs what
  holding the cutoff line costs, both inertial at Strict. CHECKLIST 12.7e's "watch RCS propellant to the last release"
  is still unticked and should be read on the same flights.
- **The turn time**, measured since: 7.3-9.1 s for 160-165 deg, which `TurnCapSeconds` and the allowance are sized on.
- **The trim tips the bus off the line.** The ring is a pure couple for rotation, but not the other way: a lateral
  trim push carries 0.107-0.193 of torque per unit at these seats (`POSTBOOST-BUS.md`), so every pass after the turn
  nudges the attitude, as it does today off the cutoff line. The steadiness gate sees it; step 0 says how much.
- **Jitter sensitivity moves**: along-track, a residual nose wobble lands on the radial axis, the dearest at 3.4 km
  per m/s. Measured at the release, so second order.
