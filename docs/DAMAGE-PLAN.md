# Damage: how a hit becomes a broken part

**Part record, part plan.** Step 1 is built and flown (2026-10-28, below). Steps 2 and 3 are built and flown (2026-10-28), the fire's look still being judged. Steps 4 and 5 are built; step 5's vacuum case is tested but not flown.

## Why every weapon used to kill outright

These were the reasons, most important first. All of them are measured against the code before step 1.

1. **Every proximity burst counted as a confirmed lethal hit.** Every round's fuse radius sat inside its
   lethal radius (57E6: 15 m against 20 m; AIM-120C: 14 m against 20 m). On a confirmed hit the struck
   part was topped up to a whole share, so at 1x the nearest part always broke.
2. **The lethal radius was a fragment number applied as overpressure.** "20 kg → 20 m" is a scaled
   distance of 7.37 m/kg^⅓. The real overpressure there is about 15 kPa, which breaks windows, not
   airframes. At the fuse distance the shares came out at 5–60 times what breaks a part.
3. **Size bought nothing.** KSA derives a part's crash tolerance from its density alone. A hollow tank
   sits on the 0.9 MPa floor, so it failed 2.15 times further out than a part of reference strength:
   43 m from a 20 kg warhead. A 20 t tank and a 10 kg fairing were equally fragile.
4. **A 50 g, 20 mm charge touching any part is a whole share**, so every hull hit took off a part.
5. **The engine finishes the job.** Its fragment guard destroys the craft once half its parts fail
   together. A central part that fails severs every connection it had, so the craft falls apart.

## Step 1: fragments, a blast ceiling, toughness by size (built)

A burst costs each part three things against the energy that breaks it (`Sim/BlastDamage.Costs`).

**Toughness** (`Sim/PartToughness.cs`): `J = 150 kJ · (m / 1 t)^⅔ · (T / 9 MPa)^⅓`. Mass to the two
thirds is the skin a hit has to get through, so a part ten times heavier takes 4.6 times as much. The
tolerance enters by its cube root, the same way the blast's reach does.

**Blast**: the old overpressure share, held under the energy the part intercepts:
`0.2 · W · 4.184 MJ · min(A / 4πr², ½) / J`, where A is the presented area. That ceiling is what lets
a 20 mm round hole a tank rather than remove it. A nuclear charge (1 t or more) skips the ceiling and is
judged on overpressure alone, which is what its reach is calibrated on.

**Fragments** (`Sim/Fragmentation.cs`):
- **The band.** The warhead throws N fragments of mass m at speed v. They fly in a band about its nose,
  centred on `SprayAngleDeg` and `SprayHalfWidthDeg` wide either side.
- **Into the target's frame.** A fragment's static direction u becomes `v·u + w`, where w is the
  warhead's velocity less the part's.
- **Speed along the line to a part.** It solves `|s·d − w| = v`.
- **Expected hits.** Density scales by the Jacobian `s² / (v·(s − d·w))`:
  `λ = N / Ω · J · A / r²`. The actual count is a Poisson draw about λ.
- **What a hit costs.** Each hit delivers `½ m v_r²`, where `v_r` is the impact speed after drag over
  the range (a 6 g cube decays over 66 m at sea level). It counts at `FragmentCoupling` = 0.5, because
  about half of a hull's presented area covers nothing a fragment can disable.
- **No penetration law.** Above a gram, any fragment perforates 2 mm of 2024-T3. THOR puts the ballistic
  limit of a 2 g cube at about 160 m/s (JESTR 2014, table 1).
- **The presented area** is a quarter of the part's box surface, its average over every direction
  (Cauchy).
- **Shadowing is ignored.** Each part draws its own fragments, so a part behind another is hit as
  though nothing were in front of it.

**The fuze is matched to the warhead** (`Fragmentation.FuzeLead`). The missile's fuse fires at closest
approach, with the target abeam. A fast pass then throws the forward-tilted band ahead of the target. A
real fuze looks forward and delays, so the fragments leave from earlier along the pass: by
`miss / tan θ_d`, where θ_d is the moving band's centre angle. This takes the nose along the relative
velocity, so a pass at an angle to the nose is approximate.

**A shell's own impact**: `0.5 · ½ m v²` against the struck part. This applies both to a shell that
burst on contact and to a dud.

**Rounds in the air** pay through the same costs, keyed on the round in the same ledger. Each is one
part of its munition's body: mass, `BodyLength`, and a diameter from `CalibreMm` or a fifteenth of the
length. Its toughness is `RoundVulnerability` = 0.15 of a part of its mass, because a fragment through
the warhead, motor or seeker ends it. A gun shell's proximity fuse firing on the round it was aimed at
counts as striking it.

**A fuse's verdict no longer breaks anything by itself.** The top-up is gone.

### The numbers (step 1)

Sources: GlobalSecurity, designation-systems.net, weaponsystems.net, NavWeaps, FM 1-140. "Estimate"
means no open source gives the figure.

| Round | ChargeKg (explosive) | Fragments | Mass | Speed | Band |
| --- | --- | --- | --- | --- | --- |
| 57E6 | 5 (of a 20 kg warhead) | 1,500 (estimate) | 6 g | 1,500 m/s | 90° ± 5° |
| AIM-9J | 1.5 (estimate; the 4.5 kg AIM-9E head) | 800 (estimate) | 3 g | 1,800 m/s | 85° ± 8° |
| AIM-120C | 8.6 (one source; 18 kg WDU-41/B) | 4,000 (estimate) | 2 g | 2,100 m/s | 85° ± 7° |
| AGM-88 | 22 (estimate; 66 kg WDU-21/B) | 25,000 | 1.6 g | 1,700 m/s | 80° ± 10° |
| Zuni Mk 63 | 6.8 | 2,500 (estimate) | 5 g | 1,400 m/s | 90° ± 30° |
| 5"/54 HC | 3.5 | 1,500 (estimate) | 10 g | 1,000 m/s | 80° ± 30° |

The 20 mm and 30 mm rounds are unchanged in step 1 and throw no fragments.

### Calibration targets (from the research, not yet flown)

- **20 mm HEI hits to kill:** a fighter 5, a helicopter 4, a medium UAV 3, a small UAV 1. With the
  current 50 g charge a contact hit is about 21 kJ of blast plus about 30 kJ of impact. A 100 kg part
  (J = 32 kJ) breaks in 1 hit, and a 1.5 t part (J = 197 kJ) in about 4.
- **Missile 50% kill radius against a fighter:** about 5 m for a 4.5 kg head and 10–12 m for an AIM-120.
  In the rig, a 1.5 t part 10 m off an AIM-120's beam breaks, and the same part 50 m out keeps over half
  its health (`DamageCostTests`).
- **Combat single-shot Pk:**
  - AIM-9B/E: 0.08–0.16
  - AIM-9L in the Falklands: about 0.8
  - AIM-120: 0.6–0.77
  - Pantsir against drones: about 0.2 (a partisan source)

## What step 1 did in flight (2026-10-28)

- **`head-on`**: the AIM-9J (1.5 kg, 800 fragments) burst 15 m from the drone. It damaged 20 of its 21
  parts, the worst left at 87%, and broke nothing. Before step 1 a burst there broke most of the drone.
- **`gunnery`**: a 5"/54 bursting on contact, 2.6 m from the capsule, broke 17 of a drone's 21 parts,
  and KSA's fragment guard took the craft. On another drone a contact burst holed a 3 m tank, which then
  leaked at 295 kg/s rather than breaking off on the hit.
- **Phalanx against a 57E6, launched 1.4 km out**: 4 of 4 shot down, about 1.7 s after launch, each on
  one shell's strike or proximity burst.
- **57E6 against an incoming 57E6, 6.6 km out**: in three shots the defender's two rounds each never
  fuzed and flew into the ground. That is upstream of damage, since nothing in guidance or the fuse
  reads the charge, but the old build was not flown to confirm it predates step 1. The incoming
  rounds burst 11–13 m over the defending Pantsir and cost it 2–5% of one part.
- **Not exercised**: a missile's fragments on a round in the air, because no interceptor fuzed.

## Step 2: damage that is not a broken part (built)

KSA has no partial-damage state: a part is whole or removed (read off the decompiled 2026.10.19.5638
source). `Ksa/DamagedEngines.cs` writes what a hurt engine can still do (`Sim/EngineDamage.cs`)
through the engine's own levers, in `AttitudeHook`'s window:

| Health | Effect | Lever |
| --- | --- | --- |
| above 0.8 | none | |
| 0.8 to 0.2 | thrust falls linearly to nothing | `Combustor.Config.CombustionPressureMax` (or a thermal core's `ChamberPressureMax`) times the share. Chamber pressure is throttle times that maximum, so this is a throttle ceiling the engine's model already handles |
| below 0.5 | gimbal holds centred | `Gimbal.AxisY/Z.MaxAngle` set to 0 |
| 0.2 and below | shut down | `EngineController.SetIsActive(false)`, sent again whenever the player relights it |

After each write, each engine's cached `VacuumData` and `SeaLevelData` and the gimbal controllers'
static data are re-derived, as `PartTree.RecomputeRocketControls` does on a structural change.
Solid motors and antimatter cores are only shut down. Nothing is saved, which matches the health it
follows.

Tanks need nothing new. The holes `BulletHoles` makes already leak through `Leaks.cs`, and a struck
tank no longer breaks on the hit that holed it.

## Step 3: fire (built)

**Ignition** (`Sim/PartFire.cs`):
- **Per hit.** A round striking a part holding propellant lights it at its `IgnitionChance`, and
  each of its fragments at its `FragmentIgnitionChance`.
  - The 20 mm and 30 mm HEI strikes use 0.35, the incendiary figure; the Phalanx's dart uses none.
  - Every fragmenting warhead's fragments use 0.08, for steel. The M56's half-gram slivers light
    nothing: flown with 0.35 for them, one shell lit four tanks, and with 0.02 still three.
- **Only in air.** It needs more than KSA's own 100 Pa floor at the craft.
- **Burning.** The fire costs its part a rate drawn between 0.01 and 0.05 of its health per second, so
  20–100 s from whole. It goes out when the air or the tank runs out.
- **Going up at once.** A fifth of fires (`FlashoverChance`, an estimate) skip burning: the tank's
  vapour space lights and the part breaks on the spot, whatever the damage scale. KSA's failure queue
  then explodes it by the propellant still in it.

**Drawn** (`Ksa/PartFires.cs`), as a flare is:
- **Flame.** A pooled `KSArmoryPartFire` flame placed on the part's box every frame and sized from it.
  Its particles live long enough to stream off a moving craft, with a black billboard smoke child.
- **Smoke.** A strand of the booster smoke renderer, laid dark through the colour parameter
  `PlumeSmoke.Lay` now takes.
- **Cap.** At most twelve flames, the flare cores' budget. Past that a fire still smokes and burns.
- **No light yet.** `Fireball` owns the only light the mod pushes, through the tail of `LightDebug.Lights`,
  and a second owner there needs its bookkeeping shared first.

**Flown 2026-10-28**, a Phalanx against a parked 575 t stock rocket 800 m out, and `gunnery`:
- **Fires.** Single 20 mm strikes lit 3 m tanks. One burned 18 s before the tank broke off, and one
  went up at once. Fires are lit only on parts that survive the hit.
- **Engine damage.** It wrote and logged thrust shares from 71% to 97% on the stock rocket's engines and
  RCS blocks. No exception in KSA's log.
- **The look.** The first flame sat inside the tank and read as a jumping ball. It now sits on the skin
  where the ray from the burst meets it and streams out of it. Seen once, small, in a gap between
  boosters, so whether it reads well is still open.

**Ideas from players (built and flown):**
- **A burning leak.** A holed tank on fire draws its leak stream as flame rather than liquid, the
  *Project Hail Mary* shot. The stream is emissive orange, yellow at the hole and redder as it burns
  out over one and a half times its whole length. It spreads to three times its width.
- **Cryogenic fog.** A leak from a tank holding a cryogen is still the rope of liquid, wrapped in white
  fog: what that cold chills out of the air round it, the way a real liquid-oxygen leak pours boiling
  inside its own cloud. The cryogens are liquid oxygen at 90 K, methane at 111 K and hydrogen at 20 K, all
  stored below 120 K. The fog is as thick as the tank's cryogen share by mass, spreads to six times the
  stream's width, fades over three of its whole lengths, and is lit by daylight. KSA's stock tanks are
  mostly cryogen, so theirs is thick; kerosene, hydrazine or peroxide pour a bare rope. The first version
  drew the whole stream as vapour, so no stock tank ever showed liquid, which the player caught.
- **A pressure-vessel breach.** The moment a hole starts leaking, `KSArmoryBreach` throws a burst of
  spray out of it at 25 m/s on top of the craft's own speed. Drag stops and spreads it. A cryogen's is
  1.8 times the size and lives three times as long. Into a tank already burning at the hole the spray catches
  instead, `KSArmoryBreachFlame`: shown first with white vapour beside the flame, which nothing at one
  hole would do.

All three are picked in `Leaks.Fly`: the stream's `Look` rides in `Flow.w` to `Shaders/KSArmoryLeak.comp`,
and `CloudPass.Spread` widens it before the patch is sized. The tank's own pressure is still the mod's
0.2 bar, kept for the reason CLAUDE.md gives, so the steady stream's speed is unchanged.

**Holes close together merge into one opening** (`Sim/HoleMerge.cs`, built, not yet flown). Holes on
the same sub-part nearer than twice their radii together, or 15 cm, are one opening: their whole area at
its area-weighted middle, chaining along a row of hits. So a burst leaks as one jet rather than a bundle
of threads, and the flow is what the holes let through between them. The stream is kept under the group's
first hole, so a later hit joining it does not restart it.

**Flown 2026-10-28**, the M197 against a parked small rocket at quarter speed:
- **A cold breach.** One shell holed its lower tank below the liquid: a white breach puff, then a steady
  white vapour leak at 7.3 kg/s, which the player saw.
- **A burning leak.** A second shell lit that same tank, and its stream turned to curling orange flame.
- **A fix from flight.** The vapour was first lit by whatever was behind it, and against dark grass it
  came out black. It is now lit by daylight, as the grey gun rounds are.
- **Brightness.** The fire's particles were drawn at ten times full brightness and read white after the
  tone map. They are now at four, with the breach flame at five, which is not yet re-flown.

## Paintball

The damage slider's far left (`Config.Paintball`). A hit marks what it struck with the shooter's paint
and harms nothing: no part damage, no dents or push, no fire, leak or engine damage, nothing destroyed,
no round in the air shot down, and the beam still lases without burning. A shell's strike leaves a
splat where it struck; a burst leaves one on every craft inside its lethal reach, where its spray first
meets the hull, and a burst of paint in place of the explosion and its cloud. The splats are hull marks
like bullet holes, held on the part and drawn by the same pass, with a hue in place of the hole and
skipped by the leaks. A round going off on or over the ground leaves a splat there too
(`Ksa/GroundSplats.cs`, painted by the ring pass): a few metres for a shell, and for a nuclear charge
about 1.3 times its lethal radius to the tips of its splash rays, several hundred metres for a B61.
**Paint on paint grows one coat**: a splat overlapping one of the same colour on the same part, or on the
same patch of ground, grows it by its area, up to six times the largest splat in it (4 m at most on a
hull), and the coat counts as the newest mark. First flown as separate splats, a Phalanx's stream put
1,313 on three craft in a minute, filled the 400 marks kept, and the oldest paint vanished. The
colour is the shooter's team's, or its craft's on no team, hashed so it is the
same every session; it is not the team colour the panel shows.

## Waiting on KSA: tank pressure and walls

KSA's revision 5704, announced and unreleased on 2026-10-28, gives tanks a rated pressure from their wall
material and thickness, and lets them hold gas. That is what four guesses here stand in for: the leak's
fixed 0.2 bar, a gas tank that leaks nothing, toughness from mass with no penetration law, and the
one-in-five tank going up. `docs/BLOCKED-ON-KSA.md`, *A tank's pressure and its walls*, has what each
would become and what to check when it ships.

## Step 4: the gun rounds split (built)

The Phalanx and the M197 shared one `20MM` round with a 50 g charge. Now:

| Round | Fired by | What it is | In the model |
| --- | --- | --- | --- |
| `MK244` | Phalanx | Mk 244 ELC APDS: a tungsten dart in a sabot, 100 g in flight at 1,113 m/s, no explosive (NavWeaps) | `ChargeKg="0"`: no blast, no fragments, no fire. The parts on its path each keep half of what it carries and pass the rest on, until under 6 kJ is left. A strike draws KSA's `MetalBurst` spark |
| `20MM` | M197, and any pack naming `KSArmory:20MM` | M56 HEI: 102 g, 10.7 g of RDX plus 1.3 g of incendiary (FM 1-140) | `ChargeKg="0.011"`, 60 half-gram fragments (an estimate), `IgnitionChance="0.35"` |

**The pass-through** (`WeaponSystem.GoesThrough`) casts the dart's path from 2 m back along it with
`KsaWorld.TryPartsAlong`, which lists every part a straight path crosses, nearest first.

**The Phalanx's ballistics change with it.** Its 12.75 mm and 100 g carry less drag than the 20 mm
shell's, so it flies flatter.

**Flown 2026-10-28:**
- **Phalanx darts against a parked stock rocket.** Each damaged three parts in a row: the first lost
  about a quarter of its health, the ones behind less. None lit a fire. A strike draws KSA's
  `MetalBurst` spark.
- **Phalanx against a 57E6 launched 1.4 km out.** 4 of 4 shot down with the flatter dart.
- **M197 against the same rocket.** Sixteen strikes, four ignitions: two fires, two tanks gone up at
  once.

**Kept as it was:** the 2.5 m proximity fuse on both rounds. Against a round in the air it stands in for
the hit a real one needs (`docs/CIWS-REALISM.md`).

## Step 5: blast in thin air (built)

A conventional charge's shock used to be judged at sea level at any height, so in vacuum a missile
broke parts by an air shock that was not there. Now (`BlastDamage.Costs`):

- **Outside the fireball**, the blast's share follows Sachs scaling by the air's pressure at the burst
  (`BlastDamage.ShareInAir`). The distance is scaled by the cube root of the pressure and the
  overpressure by the pressure, measured against the same breaking overpressure. It is exactly the old
  share at sea level, and nothing in vacuum.
- **Inside the fireball** (`Warhead.FireballRadius`), the detonation products load the part whatever the
  air, still held under the energy the part intercepts. So a shell bursting on a skin in space still holes it.
- **Fragments are unchanged**, beyond the drag they already met. In space they are all a missile has past
  a few metres.

The air is read at the burst carried to the sample, as `JudgedAs` reads it for a nuclear charge. Air
that cannot be read counts as sea level's. Dents are unchanged and still follow the sea-level law.

**Flown 2026-10-28:** `gunnery`, drones at about 4.5 km in air at about 57% of sea level's pressure,
destroyed by contact bursts as at sea level, which is right inside the fireball. **The vacuum case is
not flown:** no save has a weapons system in orbit, and the bridge's burst needs one to judge damage.
`DamageCostTests.InVacuumABlastOutsideItsFireballCostsNothing` fails when the air is ignored.
