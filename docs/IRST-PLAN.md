# Finding targets for the directors: an infrared search part

**A plan, not a record.** Tick each step as it lands; nothing below step 0 is built.

## Why

A director is a telescope. Its view is a straw and its picture says nothing about which dot matters,
so every director's set is `CuedOnly` and holds only what it is handed (CLAUDE.md, *And it finds
nothing by itself*). Today the only things that hand it a target are a shift-click and the launcher
on its own part. The YAL-1 had six infrared search sensors along its fuselage that found boost plumes
and cued the turret; this is that part, and the plumbing that lets one part's contacts reach every
director and beam on the craft.

## The design in one paragraph

An **IRST** is a small, passive part that watches a wide cone for heat. A contact is seen when its
radiant intensity, thinned by the air on the path and divided by the range squared, clears the set's
threshold — so a burning rocket is seen from hundreds of kilometres, a jet at military power from
tens, and a craft with its engines out only close in. Looking down at a craft against warm ground is
harder. Every searching set on a craft — IRSTs, and a launcher's own radar — feeds **one picture per
craft**, derived every frame like `Ksa/Armaments.cs`. A director with **Track** on, and the YAL-1's
laser on auto-engage, take that picture's best contact when nothing more specific (a shift-click, the
launcher on their own part) has been handed to them.

## Steps

### 0. Directors are cued-only, and the laser is a weapon — built, not flown
- [x] `SensorProfile.CuedOnly`; the YAL-1, LITENING and EO sets carry it; a launcher cues the
      director on its own part.
- [x] The YAL-1 laser is a `LauncherProfile` with `FiresBeam`; shift-click reaches every director.
- [ ] Flown (CHECKLIST 7.6c-2), then committed.

### 1. The detection law, in `Sim/` — about half a day
- `Sim/InfraredSearch.cs`: `Sees(intensityKwPerSr, rangeMetres, meanAirRatio, lookingDown, profile)`.
  Detected when `I · τ / R² ≥ E_min`, with `τ = exp(−k · ρ̄ · R)` from the same extinction the beam
  uses (`BeamProfile.ExtinctionPerMetre`'s order), and `E_min` multiplied by `LookDownPenalty` when the
  contact lies below the horizon.
- The heat comes from `Sim/Signature.cs` unchanged (`HeatOfCraft`, `HeatOfRound`), so the IRST, the
  seekers and the flares all agree about how bright a thing is.
- New `SensorProfile` fields, all off by default: `Infrared` (detection by this law instead of by
  range and cross-section), `IrThresholdWPerM2`, `LookDownPenalty`.
- **Calibration targets**, each pinned by a test (`InfraredSearchTests`):
  - a fighter at military power (3 kW/sr) tail-on, both at 10 km altitude: seen at 50–90 km;
  - a 1 MN booster in thin air: seen beyond 200 km;
  - a craft with its engines out (1 kW/sr) at sea level: seen within a few kilometres, not tens;
  - the same craft looked down on: a fraction of that range;
  - no air at all: range limited by the threshold alone.
- Invariance test: the answer moves with neither craft's ecliptic velocity (CLAUDE.md, *a `Sim/` entry
  point differences its own inputs*).

### 2. A set that scans by heat — about half a day
- `Ksa/Radar.cs` asks `InfraredSearch.Sees` instead of `ThreatModel`'s range test when the profile is
  `Infrared`; IFF, terrain masking and wreckage rules are unchanged. `Emits` stays false, so an
  anti-radiation round cannot home on it and `RadarSilent` does not apply.
- The contact's heat is read from the craft's live thrust (`KsaWorld`), as the seekers already do.

### 3. A roster for standalone search sets — about a day
- Today a set only exists inside a launcher or a director. `Ksa/SearchSets.cs`: one `Radar` per IRST
  part fitted, crewed with the craft and followed across a split, the way `Ksa/OpticalHeads.cs` is.
- `ComponentProfile` with `Role = Sensor` for the part; a craft carrying only IRSTs is an observation
  post, not a weapons system.

### 4. One picture per craft — about a day
- `Ksa/CraftPicture.cs`, derived every frame and never pinned (the `Armaments` reasoning): the union of
  every searching set's tracks on the craft — IRSTs and launcher radars — keyed on the target handle,
  the best by `ThreatModel` priority.
- The choice itself lives in `Sim/` (`Sim/PictureChoice.cs`), so it is tested headlessly: the same
  contact from two sets is one entry, and a hostile beats a passer-by.
- Who reads it, below everything more specific (a laser spot found by step 4b ranks with these cues):
  - a director with **Track** on, after a shift-click and its own launcher's cue
    (`OpticalHead.AimPartFrame`, and the rung order in `Sim/OpticHold.cs`);
  - the laser weapon's target when it has no designation (`WeaponSystem.UpdateBeamFireControl`).

### 4b. Laser spot search — built, not flown
Somebody else marks a target with a coded laser — a mast director on the ground, a drone, another
jet's pod — and a director with **Spot search** on finds the reflected spot carrying its own code and
locks on, without seeing the target itself.
- `Ksa/LaserDesignators.cs` already lists every spot in the world each frame with its code, position
  and the velocity of what it lies on, because laser-guided weapons home on them.
- `OpticConfig.SpotSearch`, a switch on the director's row beside the laser code. With it on and
  nothing more specific handed over, the director takes the nearest spot with its code that lies
  inside its field of regard, within its set's range and in line of sight, as a cue
  (`Aimpoint` carried by the spot's velocity, as a weapon replays it).
- Its own spot is excluded, so a pod does not chase the mark it is making.
- The choice is `Sim/` (`Sim/SpotSearch.cs`): the code must match, the travel must reach it, the
  nearest wins, and a spot that goes out releases it.
- [x] Built: `Sim/SpotSearch.cs`, the **Spot** switch on the director's Aim row, the bridge's `spot`.
  It ranks below a designation and a weapon firing through the head, and above the launcher's cue.
- [ ] Flown: an EO mast on the ground lases a hilltop; a jet's LITENING on the same code, with Spot
  search on, swings onto it unaided.

### 5. The part — art first, a few sessions
- Modelled in Blender per `.claude/skills/ksa-blender/SKILL.md`: a **low dome on a faceted plinth**,
  about 0.5 m across, surface-attaching, so several can be fitted round a hull for all-round cover —
  the YAL-1's own layout. Geometry signed off before any unwrap or bake.
- Declared in `KSArmoryAssets.xml` / `KSArmoryGameData.xml`; registered as a `SensorProfile` with a
  forward cone of about 120° and a `ComponentProfile`; `validate-parts.py` and `checkmesh.py` clean.
- Optional later: a pod form, a 2.5 m pylon store, for fighters.

### 6. The panel — about half a day
- The IRST's component row: on or off, how many contacts it holds, the best one.
- The Radar tab's scope shows IRST contacts, marked as heat contacts.
- A director's row says where its target came from: shift-click, its launcher, or the craft's picture.

### 7. Flown — one session
- A YAL-1 craft with IRSTs against a rocket under power at 100–300 km: the IRST sees it, the turret
  swings on with **Track** on, and auto-engage burns it. The same rocket with its engines out is not
  seen until close.
- A LITENING on a craft with an IRST picks up a jet under power with no shift-click.
- The Pantsir's director still follows the Pantsir's radar.
- CHECKLIST entries ticked with the numbers seen.

## Decisions to make before step 1

1. **Bearing only, or range too?** A real IRST gives a direction; range comes from a laser ranger or a
   radar. The mod knows the range anyway. Proposed: the track carries it, and the scope draws an IRST
   contact as a bearing line, so the picture is honest without the fire control being blind.
2. **A visual range for craft with no sensors?** Without one, a director with nothing to hand it
   targets waits for a shift-click. Proposed: no — the IRST is the answer, and a shift-click always
   works.
3. **Does a director's own set feed the craft's picture?** It holds only what it was handed, so it
   would add nothing new. Proposed: no.
