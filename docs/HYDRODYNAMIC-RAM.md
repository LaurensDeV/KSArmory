# Hydrodynamic ram

**Part built, part plan.** The first version is built and flown (`Sim/HydroRam.cs`, *What is built* below);
what is left is at the end. What a fast round does to a tank full of liquid, why the
mod's damage gets it wrong today, the model to build, how to calibrate it, and what it costs. Researched and
then reviewed twice, once for the physics and once for the code; what each review changed is folded in.

## What is wrong now

A tank part pays for a round the way any part does: a share of its toughness out of the round's energy
(`Sim/PartToughness.cs`, `KineticCoupling` 0.5), blind to whether the tank is full. So an empty Core 3 m tank
breaks off after about four Phalanx darts — four 15 mm holes that leave a real tank structurally whole —
and a full one breaks for the same reason rather than the real one, which is the liquid.

## What the tests say

Marked **read** where the research or a review read the source itself; the rest are leads.

- **Phases** (Varas, Zaera & López-Puente 2009, *Int J Impact Eng* 36:363, read): a **shock** falling off as
  the inverse of distance and loading the entry wall; **drag**, accelerating the liquid gradually; a
  **cavity** that grows, collapses and pulses; and **exit** through a wall the liquid has already loaded.
- **Drag** (Varas, read): `V(x) = V0·exp(−βx)`, `β = ρ·Cd·A/(2m)`, `Cd` 0.4 for a sphere at 600–900 m/s,
  within 10%. Tumbling raised the load up to fivefold (Lundstrom & Stull, via Varas's thesis).
- **Rupture is local** (Stepka, Dengler & Morse 1964, NASA TM X-52063, read twice): fracture began 27–40 µs
  after impact, and a 0.6 ft³ tank and a 15.1 ft³ one gave the same result. Tank volume does not enter.
- **"Prestressed" in that report means at the 0.2% yield strength.** Pre-loaded to yield, a water-filled
  0.8 mm 7075-T6 tank fractured from a 5.6 mm steel sphere at 1,006 m/s; unloaded, some walls held at nearly
  twice that. Doubling the wall raised the critical speed only 9–48%; 3.2 mm spheres of carbide and steel did
  not fracture loaded walls even at 2,190 m/s; and at a fixed size the projectile's density mattered
  (carbide 869, steel 1,006, aluminium 1,524 m/s). **So `V²·D/W` is not one constant** — the size effect is
  stronger than linear in `D` and the wall weaker than `1/W` — and Bless's correlation (A-10 tank replica, via
  Varas's thesis) is a trend, not a law.
- **The liquid** (NASA, read): liquid nitrogen needed more speed than water but broke brittle and completely,
  with the walls at yield; LH₂ was expected to need considerably more. LOX against aluminium showed no
  reaction; **against titanium it burned whenever the wall was holed**. Aluminium 2014 and 2219 get stronger
  and tougher at cryogenic temperatures, not more brittle.
- **Partly full** (Varas thesis, read): the wall bulges only below the level; a gas-only tank never fractured
  from ram in NASA's tests, only from tearing.
- **HE in liquid** is much worse than impact (Lundstrom & Anderson 1989, via the thesis). An 11 g charge's
  free bubble is about 0.35 m in **radius** at one atmosphere and 0.6 m at 0.2 bar (Cole–Swift, recalled).

## The fracture rule underneath is thin sheet's

**Found by the physics review, and fixed.** `TankWall`'s crack length came from a plane-strain toughness, but a
tank's wall is thin sheet, in plane stress, several times tougher. With the plane-strain 1.2 mm the rule
fractured NASA's loaded panels on any puncture, where NASA saw plain holes. It is now 3.9 mm, from Orange
1967 (NASA TN D-4017, read): 1.5 mm 2014-T6 sheet at 77 K, `K_c` 58.0 MPa√m transverse and 70.7
longitudinal, over a 0.2% yield of 524 and 518 MPa — the transverse figure, the weaker. Core's sheet is
4 mm, a little thicker than the tested 1.5, where `K_c` falls somewhat; and the ram's own fit below may want
the effective dynamic fracture stress rather than this static one.

## The model, as revised

**1. Local fracture at the holes — the term that splits tanks.** The liquid loads the wall round a hole with
`σ_ram = κ · ρ_liquid · v^a · D^b / W^c`, `v` the round's speed at that wall — the impact speed at entry, what
the drag left at exit — and the wall fractures if the hoop stress it carries plus `σ_ram` exceeds the
**thin-sheet** fracture stress `σ_c` at that hole's radius. The exponents and `κ` are fitted, with `σ_c`,
to NASA's Table I together: the loaded and unloaded pair, the thickness series, the size series, and liquid
nitrogen against water. A two-point fit of the simplest form already gives `κ ≤ 0.024` and
`σ_c ≥ 1.33 σ_y` — and with that, one dart into LOX loads a 4 mm 2014 wall to about 110 MPa against 550, and
nothing splits; with the first draft's `κ` 0.04, every LOX tank split from one dart. The fit decides the
outcome, which is why it comes before any code.

- **The exit wall** is where real tanks fail: a yawed round's exit hole is about a body length long (`a`
  far larger, `σ_c` lower) through a wall the liquid has pre-loaded. It gets its own judgement, with the
  exit speed and the exit hole.
- **The prestress is the tank's own pressure** — **decided: a separate operating pressure of a flight
  tank's 2–4 bar for the ram**, while the leak keeps its 0.2 bar so a hole's depth still decides a leak. At
  0.2 bar a 3 m tank's skin carries 3.6% of its yield by KSA's rating (twice the thin-wall `pR/t`, because
  KSA rates the tank at its doubled radius); at 2–4 bar about 27%, and that load is what lets a ram split a
  tank. Both numbers are the mod's because KSA gives a liquid no pressure; the day it does, the ram and the
  leak read the game's (`docs/BLOCKED-ON-KSA.md`, *A tank's pressure and its walls*).

**2. The cavity against the ullage and the walls — the term that bursts a nearly full tank.** An energy
balance, not a volume: `χ·E_dep` equals the adiabatic work on the ullage gas,
`p₀V₀/(γ−1)·[(V₀/V₁)^(γ−1) − 1]`, plus the elastic energy of the liquid and the wall,
`½·Δp²·V/K_eff`, `1/K_eff = 1/K_liquid + 2R(1 − ν/2)/(t·E)`. Finite at any fill: the full tank's bound is
`Δp = √(2χE·K_eff/V)`. For one dart into a 30 m³ tank that is about 0.1 bar at 10% ullage and 3.7 bar at 1% or
full — comparable to what a holed Core wall holds, so near-full tanks are genuinely marginal. `χ` for drag
deposit is 0.6–0.9 rather than an explosion bubble's 0.45, to be fitted. In a saturated cryogen the cavity
fills with vapour at the ullage pressure.

**Above the surface, nothing** — what makes an empty tank survive. **Coasting**, the liquid floats, and a
round meets it on a share of its path equal to the fill.

**The energy share stops applying to a tank's skin**, and the energy `GoesThrough` passes on is what the
liquid left, not what the skin's share left — otherwise the parts behind the tank are paid twice.

**An HE shell bursting on a wall with liquid behind it is the worst case and the commonest**: every
contact-fused M197 and 30 mm round bursts on the skin. Its charge's `χ·E_HE` goes into term 2 and its
burst loads term 1 at the skin. The first draft said it did nothing.

**Fragments**: one ram per tank per burst. `BlastDamage.Costs` already returns the fragments that got
through each wall; it also returns the joules per hit, and the ram runs in `Ksa/` after it, with no tank
state entering `Sim/`'s burst sweep. A fragment of 1.6–6 g gives up its energy over 8–13 cm of liquid. Term 1
is judged on the **merged** opening of the burst's holes (`Sim/HoleMerge.cs`), since a fragment pattern's
holes link up; which hits are below the level is the share of the tank's burst-facing area that is wetted.
Fragments make no recorded holes today, so a burst leaves no leak; that is its own step.

## Where it goes in the code

Found by the code review; the first draft's hook missed most of these.

- **Every crossed part with a wall, not only the first one struck.** `WeaponSystem.GoesThrough` runs the
  ram for each part along the path (`KsaWorld.TryPartsAlong`) that has a `Wall`, entering at that part's own
  near point in the burst-instant frame `GoesThrough` already builds (`InFrame.AtBurst`); `Penetration`'s
  hull point is used only for the part it names. A tank behind a raceway is then judged.
- **HE shells** (`StrikesSkin`) and **duds** (`StrikeInert`, which today throws the strike's verdict away)
  each get the ram, the first with its charge.
- **Not on a drawing switch.** `BulletHoles.Strike` returned no verdict while bullet holes were off — a
  setting, or `CloudPass` switching them off when the hole shader is missing — so the tank-wall penetration
  and the craters turned off with the hole drawing. Fixed: a dart's verdict is judged either way, and only
  the marks go with the switch. The same for `Config.TankLeaks`: the liquid's level is read
  whether or not leaks are drawn.
- **The liquid at hit time**: `Leaks.TryLiquid` sharing the cached shape and settled acceleration, with a
  simulated-time stamp so a tank never holed settles on read rather than holding its first seed.
- **The split** goes into `_failedParts` as a flashover does — `PartHealth.Hit(part, 1, 1)` and the fire put
  out — and through `ApplyPendingKills`, never applied from the hit. No double failure with `Leaks.Burst`,
  which bursts gas tanks only. The fragment guard is asked by `Leaks` and `Damage` against different part
  totals; a merged failure event can break more than the guard allows. That exists today and is worth fixing
  alongside.
- **No ram with `DamageIndividualParts` off**, where a hit is a whole-craft kill; paintball does nothing.
- The ram takes the hit point and the path in the tank's own frame, never ecliptic positions, so its
  invariance test is a rigid shift and rotation; the entry point is never built from `round.PositionEcl`
  against a part's end-of-step position.

## Performance

**Nothing here is measured yet.** Estimates from reading the code.

| Step | Cost | How often |
|---|---|---|
| `TryCollectDamageableParts`, a wall read by reflection per part, `Costs` over every part | 10–50 µs on a 50-part craft | **already paid per hit** |
| Hull ray casts (`TryHullHit`, `TryPartsAlong`), each over every part | unmeasured | already paid per hit |
| Reading the tank's shape (`FormOf`, ~400 candidate points on a Core tank) | 20–100 µs | once per tank, cached |
| The liquid's surface (`SurfaceHeight`: 256 points and a full sort) | 3–8 µs; ~0.5 µs with a quickselect or Leaks' own 0.1 s surface | per hit |
| Mass, matrices, felt acceleration, chord, the two closed forms | 2–4 µs | per hit |

**New per hit about 5–12 µs warm, a one-off ~100 µs the first time a tank is struck; with what each hit already
costs, about 0.15 ms a frame at the worst realistic rate** — four Phalanx mounts hitting, five hits a frame —
and more under warp, where hits per frame scale up. Fragments add at most one ram per tank per burst.

Cheap to take on the way: cache the tank wall per part rather than reflecting every hit, and stop the
per-call closures (`_settled.GetValue` with a capturing lambda, the `Log.Debug` lambdas in `Strike`, the
`damaged` string built whether or not it is logged).

**How to measure**: the `fire` bucket also holds the gun-lay solves and cannot resolve a tenth of a
millisecond, so add nested `ram` and `damage` measures to `FrameBudget`, fly `gunnery` at a tank stack before
and after, and benchmark `SurfaceHeight` and `FillPoints` headlessly.

## The order of work

0. **Fix what the reviews found in shipped code**: ~~the tank-wall verdict worked out with the hole drawing
   off~~ (done); ~~the thin-sheet crack length~~ (done, 3.9 mm); the wall read cached per part (not yet).
1. **Fit** `σ_c`, `κ` and the exponents to NASA's Table I, and `χ`; write the fits and sources here. Tests that
   pin NASA's loaded panels fracturing above the critical speed and holding below it, and the unloaded ones
   holding at twice it.
2. **`Sim/HydroRam.cs`**: the deposit along a path, term 1 at entry and exit, term 2's energy balance, and a
   ray's chord through a `TankShape` cut at the surface — closed forms, tested headlessly, with the
   invariance test, and a path above the surface and an empty tank giving nothing.
3. **`Leaks.TryLiquid`**, time-stamped.
4. **`Ksa/`**: every crossed walled part in `GoesThrough`, `StrikesSkin` and `StrikeInert`; the fragment ram
   after `Costs`; the energy passed on after the liquid; the nested `FrameBudget` measures.
5. **Fly it**: a full tank and an empty one under the same Phalanx burst, then the M197, LOX against LH₂, and
   the `ram` and `damage` measures before and after.

## What is built

`Sim/HydroRam.cs`, called from `WeaponSystem.Ram` for the tank a round struck: a dart through its wall
(`GoesThrough`) and a shell bursting on its skin (`StrikesSkin`, half the charge into the liquid).

- **The crack** is a critical speed, `CrackingSpeed`: table I's 1,463 m/s for a 5.56 mm steel sphere through
  a 0.794 mm 2014-T6 wall loaded to yield, times `(W/W₀)^0.27` (the 1.588 mm wall's 1,768 m/s), the size —
  `(D₀/D)^0.5` above the tests' sphere by Bless's `V²D/W`, `^1.4` below by table I's 3.2 mm spheres — a load
  factor running from 1 at yield to 1.85 unloaded (7075's pair), `(ρ/1000)^−0.1` for the liquid (nitrogen
  against water), mass per area to the −0.75 down to 0.6 (tungsten's 884 m/s, which it reproduces within 3%),
  and the damage scale as its square root. Above 5.56 mm the size term is an extrapolation from Bless's trend,
  not from table I.
- **The cavity** is `CavityPascals`, the energy balance above with `χ` 0.7, the liquid taken as stiff against
  the wall's give, and the result judged with the 3 bar load against `TankWall.HoldsPascals` of the round's
  hole.
- **The liquid at hit time** is `Leaks.TryLiquidPath`: the cached shape, the settled acceleration stepped
  over the simulated time since it was last read, and the wet length of the path marched through the tank.
- **The tank's skin pays no energy share** for a dart; the liquid takes what `DepositedShare` says, and the
  parts behind are passed what is left.

**Flown at 1x, 2026-10-30**, the Phalanx at 800 m against the `CIWS` save's rocket, full and emptied:
- Full: darts arrived at 925 m/s against 1,389 m/s to crack a 3 m tank, and none cracked it; each crossed
  about 3 m of liquid, gave it 55% of its energy, and raised the full tank 1.71 bar over its 3 bar load,
  past the 3.99 bar its 15 mm hole holds. Ten splits.
- Emptied: no ram, 60 holes, and the tanks stayed on.

**Then the rest, flown the same night**:
- **Tanks behind another part**: `KsaWorld.TryPartsAlong` keeps where it enters each part, so a dart rams every
  tank on its path, losing speed through each — flown, one dart into three tanks at 925, 623 and 415 m/s.
- **The exit wall** (`HydroRam.ExitCracks`): the round leaves tumbled, `TumbledCalibres` 3 across — a guess,
  not a measurement — through a wall the cavity has loaded on top of the 3 bar. Flown, Phalanx darts cracked
  the far walls of 3 m tanks at about 620 m/s through walls loaded to 92% of yield.
- **Fragments** (`WeaponSystem.RamFragments`): `BlastDamage.Costs` hands back the joules per fragment for each
  wall they got through, and each tank is rammed once, entered on its side facing the burst and heading for
  its middle, with every fragment's energy in the cavity. Flown, a 5"/54 burst sent 208 to 743 fragments into
  each of a rocket's 80% full LOX tanks and raised them about 20 bar.
- **Duds**: a shell struck before its fuze armed is judged as a dart, with a dart's hole; flown, a 5"/54
  dud at 250 m cracked a full LOX tank going in at 793 m/s.
- **An HE shell bursting on a tank's skin**: half its charge into the cavity, and its own crack at entry;
  flown, a 5"/54 shell cracked a tank going in at 647 m/s against 391.

## Still to build

- **The nested `ram` and `damage` measures in `FrameBudget`**: a flight's `fire` bucket mixes the ram with
  the kill it causes, so the ram's own cost is still an estimate.
- **Fragments' wetted share**: a tank's fragments are all counted wet when the line from the burst to its
  middle is; a burst level with a half-full tank's surface is all or nothing.

## What it leaves out

- A tank's internal structure — baffles, a common bulkhead — which stops much of the cavity's push.
- The titanium-and-LOX fire: Core's tanks are aluminium, but a pack's titanium tank would want it.
- Cryogenic boil-off from a holed tank, which would make a LOX leak a spray rather than a stream.
- Tumbling as anything but a range of drag: a tumbling dart is broadside to the liquid, five or six times its
  nose area.
