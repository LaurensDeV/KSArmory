# Porting weapons from KSP mods into a pack

**A plan, not a record.** Tick each step as it lands. Phase 0 is built; after it, only the reading half of step 5.

## Why

KSP's weapon mods carry years of tuned guns, missiles and radars, and somebody will want them in
KSA. What that costs was measured by porting one part on paper: a seven-barrel 30 mm CIWS with
a traversing gun, a fused round, and a search-and-track radar on a turret of its own. The part's
config was translated into a `KSArmory/Weapons.xml` and run through `PackReader` headlessly.

- **First read: 2 of 4 definitions accepted.** The mod's gun spread has no attribute here, so it was
  refused by name, as it should be — and the launcher naming that round was refused with it. One
  missing field cost the whole weapon, and the launcher's fault named the wrong cause. (Phase 0
  fixed the message.)
- **Spread removed: 4 of 4 accepted, `Trains` true.** The ballistic core carried over cleanly:
  muzzle speed, drag from mass, calibre and coefficient, charge, tracer interval, rate of fire, slew
  rates and elevation limits. An impact fuse is `FuseRadius="0"`, a timed one `TimedFuse`, and
  shooting down missiles is native.
- **Accepted is not the same as faithful.** The radar's own turret read as `RadarMarker`, which
  only rides the gun's traverse; nothing refused it.

What does not carry is listed below in order of what it unblocks for what it costs.

## What KSP's model has that ours does not

| KSP side | Here | Where it goes |
| --- | --- | --- |
| a part's modules say what it is | inert part, recognised by `PartId` across two files | KSA; stays |
| one part both stacks and surface-attaches | `IsAllowedAsRootPart` forces one or the other | KSA; stays |
| a Unity transform tree with implied pivots | flat sibling subparts, recentred, pivots typed in | step 12 |
| gun spread | none | step 4 |
| spool-up delaying the first round | `GunRotorSpinUpSeconds`, a pack attribute, drawn only | step 5 |
| heat and overheat | none | step 6 |
| detection range against cross-section as a curve, a second curve for lock | the fourth-root law, one reference | step 7 |
| several ammo types switched on one gun | one `GunMunition` | step 8 |
| a radar on its own turret, tracking independently | `RadarMarker` rides the traverse and spins on a clock | step 9 |
| ammo as a resource in separate boxes, shared by guns | a belt per launcher | step 10 |
| armour, penetration, hit points | part health, every part alike | step 11 |
| a ground-clutter *efficiency* factor | `ClutterFloorMetres`, a height cutoff | not planned |
| locks held at once, a data link, a radar-warning threat type | `Emits` and the `Emitters` scope only | `docs/SHARED-PICTURE.md` |
| per-weapon sound and explosion paths | KSA's explosion by charge, sounds per built-in | not planned |

**The radar curve is evidence that the source is tuned, not modelled.** The ported set's curve had
5 km at 5 m² and 10 km at 20 m²: four times the cross-section for twice the range, which is a square
root. The radar equation says fourth, which `Sim/RadarSignature.cs` holds. Step 7 is a way to match
somebody else's tuning, never a correction to ours.

## Steps

### Phase 0: the reader and the docs

- [x] **0.1** A launcher naming its own pack's refused round or sensor says why that was refused,
  not that it names nothing (`PackReader.Unresolved`).
- [x] **0.2** `tools/validate-parts.py --mod-root <pack>` checks a pack's folder offline: `mod.toml`,
  asset paths by case, `PartId`s declared, drive markers selecting one subpart, round bodies and fins
  on their own launcher, `BodyModel` templates. Not tube seats against the mesh.
- [x] **0.3** "No damage below destruction" corrected wherever it was current-tense.

### Phase 1: new numbers on behaviour we already have

Each is new attributes on schema 1, off by default, so a pack that does not mention them is
unchanged. Each needs a control (`check-tunables.py`), the pack surface regenerated
(`pack-api.py`), a row in `WEAPON-PACKS.md`, and a flight before it is called a feature.

- [ ] **4** **Gun spread**, `GunDispersionDeg`: a cone per round from a seeded generator, so tests
  repeat. Applied in the fire path and **not** in `BallisticLead`, which keeps aiming at the cone's
  centre. Flown with `scenario.sh gunnery`: the hit rate should fall as the cone's area over the
  target's predicts. The one most ports will hit first, because a refused attribute takes the
  launcher with it.
- [ ] **5** **Spool-up**: `GunRotorSpinUpSeconds` is read from a pack now (it came with the M197's
  move into `Guns.xml`) but only spins the drawn rotor; what is left is `GunChannel` holding the
  first round until the rotor is up. A rung in `FireLadder` if the hold is long enough to be read.
- [ ] **6** **Heat**: `GunHeatPerShot`, `GunHeatLossPerSecond`, `GunMaxHeat` in `GunChannel` on
  simulated time, and a `FireLadder` rung "overheated" so the strip says why the gun stopped.
- [ ] **7** **Sensitivity curves**: an optional `<DetectionCurve><Key Range="" CrossSection=""/>` and
  `<LockCurve>` on `<Sensor>`, read by `RadarSignature` in place of the fourth root. No curve is
  today's law exactly. Test it for monotonicity and refuse a curve that is not.
- [ ] **8** **Several belts**: `<Belt Munition="…"/>` children on a launcher, the operator switching
  between them on the gun's component row. Touches the magazine, the panel, `SystemSettings`
  persistence and the switch; one belt stays the default shape.

### Phase 2: new kinds of behaviour

- [ ] **9** **A radar that trains on its own**, `RadarTrains="true"`: a second `Turret` on
  `RadarMarker` following the lock and sweeping otherwise, a `DriveChannel` bit of its own so a
  refused write latches alone, and written in `WeaponSystem.Update` before any head reads its base.
  An authored mount's travel is swept in Blender.
- [ ] **10** **Ammo boxes**: a magazine part shared by the guns on one craft, held by part reference
  as `HeldPart` is so it survives a split, weighed by `StoreMass`. The biggest data-model change
  here; only when a pack asks.
- [ ] **11** **Armour and penetration** on top of `PartHealth`: a per-part `<Armour>` a pack declares
  and a penetration term on a round scaling the share a hit costs. The ledger is not saved, so a
  reload restores every armoured part whole. Last.

### Phase 3: art

- [ ] **12** **A Unity-hierarchy importer**, generalising the first LITENING's importer (`tools/model/import-litening.py` in git history): read the
  source with a Blender `.mu` import add-on, flatten the transform tree into sibling subparts
  recentred on their pivots, flip into KSA's axes, write the `_VM` and `_ColPrim_` nodes, and print
  the pivots and muzzles as a block for the pack's definition file. `checkmesh.py` and `--mod-root` then gate it.
  The largest cost per part, so the one where tooling pays most.

**Order:** 4–6, then 12, then 7–9 as packs show they need them; 10 and 11 wait for a pack that
needs them.

## Kept out of this repository

- **A converter from a particular mod's config files.** It has to name that mod's module types and
  fields, so it lives in a separate repository or in a porter's own pack, never here. Its list of
  fields it could not map is the backlog for Phases 1 and 2.
- **The ported art and sounds.** A port redistributes somebody else's work under their licence, so
  it ships as that porter's pack and never inside KSArmory. Check the licence before starting.
