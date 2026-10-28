# Damage: how a hit becomes a broken part

**Part record, part plan.** Step 1 is built and flown (2026-10-28, below). Steps 2 and 3 are built and flown (2026-10-28), the fire's look still being judged. Steps 4 and 5 are not built.

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
- **Per hit.** Each fragment or strike on a part holding propellant has the munition's
  `IgnitionChance` of lighting it.
  - The 20 mm and 30 mm rounds use 0.35, the incendiary figure.
  - Every fragmenting warhead uses 0.08, for steel fragments.
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

**Ideas from players, not built:**
- **A burning leak.** A holed tank on fire draws its leak stream as flame rather than liquid, the
  *Project Hail Mary* shot. `Shaders/KSArmoryLeak.comp` already draws every stream; it would need a
  burning flag per stream.
- **Cryogenic vapour.** A tank holding a cryogenic propellant vents white vapour where the stream
  boils off. The tank's moles say what it holds.
- **A pressure-vessel breach, not a barrel.** Today's stream is liquid falling out of a hole under the
  mod's own 0.2 bar, so it reads as water from a drum. A flight tank holds a few bar, which throws a
  fast jet that atomises into spray and, for a cryogen, flashes to vapour at the hole. That means
  raising the tank pressure in `Sim/TankLeak.cs`, which was kept low on purpose (CLAUDE.md has why), or
  drawing a burst of spray and vapour at the moment of the hit, apart from the steady stream.

## Step 4: the gun rounds split (not built)

The Phalanx fires the Mk 149/Mk 244 APDS: a 70–105 g tungsten penetrator at 1,113 m/s with no
explosive. The M197 fires the M56 HEI: 102 g, with 10.7 g of HE and 1.3 g of incendiary. Today both
share the `20MM` round with a 50 g charge.

The split would give each its own munition:
- **APDS:** impact only, with a chance of going through into the next part on its path.
- **HEI:** a small fragment puff, plus the fire chance from step 3.

`docs/CIWS-REALISM.md` has why the proximity fuse stands in for hitting at all.

## Step 5: blast in thin air (not built)

A conventional charge is judged on its whole charge at any height, including in vacuum. Sachs scaling
(overpressure by the ambient pressure, distance by its cube root) would leave fragments as the only
thing that kills in space. `BlastAltitude` already does this for nuclear charges.
