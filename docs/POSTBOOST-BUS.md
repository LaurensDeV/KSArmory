# A post-boost bus with a main engine

**A plan, not a record.** What a second MIRV bus with an axial engine and a bigger tank would buy, how big to make it,
and what the guidance needs before it can fly one. The art is on `dev` (`KSArmory_PostBoostBus.glb`, carrying its own
RV with two spin nozzles in a dished base). It is declared as `KSArmory_Prefab_PostBoostBus` and registered as
`Arsenal.PostBoostBus`; the guidance flies it exactly as the old bus, on its nozzles, and nothing commands its engine.

## Why a second part

The Heavy MIRV Bus (`KSArmory_Prefab_MirvBus`) diverts on twenty small nozzles from one 134 kg MMH/NTO sphere, and the
guidance spends at most 60 m/s of it (`PostBoostAim.MaxTrimMetresPerSecond`; ~70 on a walk with the split reserve).
That caps six targets at about 4.5 km apart on the shipped schedule and about 6 km with the walk from cutoff
(`ICBM-OUTSTANDING.md` 4.12) -- inside a Mk 21's 6 km blast radius, so barely separate targets at all.

A real post-boost vehicle diverts on an axial engine and keeps its small thrusters for attitude and the last
correction. A second part rather than a reworked one, because a shipped part's subpart list is append-only and its Id
is fixed: old saves keep the old bus, and "replacing" it later is changing what new designs carry.

## What spacing costs

A metre a second spent on the coast moves the landing by about the time left to arrival in metres -- pinned, it falls
off as `sin(wt)/w` (`ICBM-GUIDANCE.md`, "What bounds a set is its spacing"). Averaged over a walk from cutoff, priced on
the narrower axis as the planner prices a hop (6.1 km against 8.5 straight-line for six at 60 m/s):

| range | reach per m/s, planner-priced |
| --- | --- |
| 6,179 km | ~0.70 km |
| 12,902 km | ~1.06 km |

Seven RVs (the art seats seven) are six gaps, so a chain `S` apart is `6S` end to end and costs `6S / reach` in hops.
Every figure below adds 20 m/s for the split's null and the corrections, and nothing over the planner's price: a flown
hop costs 82-98% of it (`ICBM-OUTSTANDING.md` 4.12, "What a stop costs").

| spacing | 6,179 km | 12,902 km |
| --- | --- | --- |
| 10 km | 106 m/s | 77 m/s |
| 25 km | 234 m/s | 162 m/s |
| 50 km | 449 m/s | 303 m/s |
| 100 km | 877 m/s | 586 m/s |

## How big a tank

The rocket equation with a warhead's 250 kg leaving at each stop, seven RVs and 1.5 t of structure, and the exhaust
velocities measured in game (below): **2,996 m/s for the main engine** and **2,912 m/s for the nozzles**. The hops are
the engine's, so they are priced at 2,996:

| propellant | hops it flies | spacing at 6,179 km | at 12,902 km |
| --- | --- | --- | --- |
| 500 kg (declared: one 0.5247 m sphere on the axis) | 526 m/s | ~59 km | ~89 km |
| 1,000 kg | 979 m/s | ~112 km | ~169 km |
| 1,500 kg | 1,378 m/s | ~158 km | ~240 km |

Measured 2026-10-17 on the bus alone in a 300 km orbit, with the bridge's `burn`: 20 s on the engine took 45.4 kg for
34.30 m/s off the accelerometer (2.27 kg/s, ~6.8 kN), and 20 s on the four forward nozzles 10.7 kg for 7.92 m/s
(~390 N a nozzle). Both match the ve KSA's own rocket model declares to the m/s, so the declared figure can be read
for any later `ExitDiameter` without flying it.

The tank is one container on the axis, and the art draws none: KSA lists each `<Tank>` as its own propellant in the
part popup, with a flow rule between them, and four could drain unevenly and move the centre of mass off the axis. It sits at X 0.72 rather than
higher: the engine thrusts along the axis, so only the nozzles see the height, and the 3,450 kg seat is at the ring's
X 0.52. Full, the centre of mass is at 0.545 loaded and 0.565 with the RVs gone, against 0.564 and 0.600 at X 0.87, and
`checkring.py`'s analysis at those seats puts 0.107 and 0.193 of torque per unit of a lateral trim push against 0.188
and 0.337 -- nearly twice the turn coupled into every sideways correction. Its `RoleAffinity` is **`Engine Thruster`**, because a tank feeds only the roles it names
(`PartTree`) -- declared `Thruster` alone, as the old bus's is, the engine can never draw. And KSA puts any engine with
no sequence into stage 1, so a bus engine left unstaged lights with the first stage; `make-rocket.py` stages it with
the release, after cutoff has switched the main engines off.

**Flown 2026-10-17**, one target at 24.0 S 62.0 W (6,180 km) on `TEST PBB GeoSat FAT`, a GeoSat FAT stack
(`make-rocket.py --bus postboost`) carrying the bus: twice PASS, 7 of 7 within 3.8 mm and then 6.2 mm of the aim,
cutoff 0.07 and 0.04 m/s short, the trim nulled to 0.002 m/s. The second carried the engine staged with the release, and
it stayed idle from the pad to impact: the computer lets the bus go by firing its joint directly, not by staging, so
that stage is never reached. Each RV is spun by its base motors as it leaves (`LauncherProfile.ReleaseSpin`), drawn
only: released one at a time from the bus alone at 85 km and 0.02x, both jets light for the 0.6 s burn and the
body turns at half a turn a second after. Then seven targets 2.5 km apart in a line from 24.0 S 62.0 W, once
`TargetSet.MaxTargets` was raised to seven: PASS, every warhead to its own target and the worst 5.6 mm from it, the walk
flown on the nozzles exactly as the old bus flies one.

So **the declared 500 kg is the 50 km bus at long range**, and 100 km at 6,179 km wants about 1.2 t. The same arithmetic
on the old bus (1.27 t structure, six RVs, 134 kg) gives **~167 m/s** of physical divert against the 60-70 the
guidance allows itself: the current bus is already capped by the code, not the tank.

## How big an engine

A hop has to fit its slot on the coast (65 s today, `ReleaseItinerary`), and at 50 km spacing a hop is ~90 m/s with
the margin. At ~3.8 t wet that is 3 m/s2 to burn it in 30 s, so **~10-15 kN**: 4-5 kg/s at 2,900 m/s, about two
minutes of burning for the whole 500 kg. Today's twenty nozzles push ~0.56 m/s2, which is why a large hop runs into
`BusTrim.MaxSeconds`' 120 s. The bell is modelled at a 0.44 m exit; its `ExitDiameter` sets the thrust and is chosen
to match.

## What the guidance needs, in order

1. **The bus's numbers on its profile** (refactor, headless). The divert budget, the per-pass ceiling
   (`BusTrim.MaxMetresPerSecond`, 10), the per-hop clock (`BusTrim.MaxSeconds`, 120) and the hop slot are constants
   today. Each becomes a field the bus part provides, the old bus keeping today's values exactly, so nothing it flies
   moves. Also worth flying on its own: the old bus's budget raised toward its ~167 m/s, with the budget-edge reserve.
   **Wanted with step 3, not before it.** The budget is already a per-craft setting
   (`IcbmConfig.TrimBudgetMetresPerSecond`), and the pass ceiling, the hop clock and the slot bind only once a hop is
   flown on the engine; moved onto the profile alone they change nothing anything can check.
2. **The step-3 reach** (`ICBM-OUTSTANDING.md` 4.12): a walk from cutoff priced at the epoch it opens, with the
   `sin(wt)/w` falloff. At these spacings the first hops are long before 760 s.
3. **Hops as burns.** Point the bus, fire the main engine for the hop's velocity, null the remainder on the small
   nozzles. The trim's passes were built to null a few m/s; a 90 m/s hop chained through 10 m/s passes is the wrong
   tool. Rig-testable on `PastCutoffRig` before the part exists.
4. **The part**: the draft declaration, the tank and `ExitDiameter` from this note, both registries in
   `Sim/Arsenal.cs`, `checkring.py` on the ring at X 0.52, and `validate-parts.py`.
5. **Fly it** paired against the old bus at 6,179 and 12,902 km: stops walked, worst warhead, spacing reached.

## Art notes for the next art session

- `checkmesh.py` finds 256 near-coplanar pairs on the engine, all 1.76 mm apart (the bell's inner and outer skins),
  and 35 on the RV, 0.6-1.7 mm apart near the base. Core's `EngineACompactVacAssembly` carries 447 of its own at
  0.7-2.9 mm, so thin bell walls are Core's practice too and the count alone does not explain the fixed pattern seen
  end-on: compare the two by eye before thickening anything.
- The RV's orange dish edge is stair-stepped in the texture: more texels across it, or the colour change on a mesh edge.

## Unknowns to settle before sizing is final

- ~~The exhaust velocities.~~ Measured: engine 2,996 m/s, nozzles 2,912 m/s (above).
- **Whether switching the engine on and off breaks the nozzles' attitude hold.** Seen once by hand: after a measured
  engine burn on the bus alone, KSA's own prograde and retrograde holds stopped working until the tank's Propellant Use
  was toggled (which rebuilds the part tree's resource managers, `TankPropellantUseChangeData`). **Not reproduced on
  the bridge's path, 2026-10-17**, bus alone at 300 km: prograde held inside its 1 deg deadband through a 10 s burn
  (`burn` activates the engine, fires it and deactivates it) and after it, retrograde then slewed 100 deg and settled
  to 0.3 deg in 45 s, and a jets burn either side of the engine burn gave the same 1.95 m/s at ve 2,911-2,913, so the
  nozzles stay fed. The deadband stayed 1 deg throughout; the rate bit rose from 0.041 to 0.068 deg/s across the burn
  and stayed, unexplained and harmless. The hand sighting went through the popup's Active boxes, which the bridge
  does not, so that is what is left to try; `attitude` reads the tracker back. Nothing in flight switches the engine
  on today: `make-rocket.py` stages it with the release, but the computer lets the bus go by firing its joint, so that
  stage is never reached.
- **The reach at hundreds of m/s.** The per-m/s figures are small-divert slopes; a 90 m/s hop is still under 2% of the
  velocity at cutoff, but the planner should be checked against a flown chain rather than trusted at 100 km.
- **The coast.** Seven stops need a coast of the gate plus six slots, about 3,100 km and up today; faster hops shorten
  the slot and lower that floor.
