# Finding targets for the directors: an infrared search part

**A plan, not a record.** Tick each step as it lands. Steps 0 and 4b are built; the rest is not.

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
- [x] Committed and on `dev` (feb8cf6e, with the shift-click fix 29be7514).
- [ ] Flown (CHECKLIST 7.6c-2).

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

## Handoff: how to resume in a fresh session

**Start here.** Read this file, then CLAUDE.md's *And it finds nothing by itself* and the director
paragraphs around it, then `docs/FRAMES-AND-EPOCHS.md` before touching anything that compares
positions. Work on a branch off `dev`; commit to `dev`, never `main`.

**What exists to build on.**
- `Sim/Signature.cs` — heat in kW/sr: `HeatOfCraft(thrust, throttle)` (0.04 kW/sr per kN, 1 kW/sr
  for an airframe with its engines out), `HeatOfRound(burning)`. The detection law reads these and
  nothing else, so seekers, flares and the IRST cannot disagree about brightness.
- `Ksa/Radar.cs` — the scan every set runs: range and cone (`ThreatModel.TryAssess`), terrain masking,
  IFF, wreckage, `CuedOnly`. Step 2 adds a heat test beside the range test, not a second scanner.
- `Sim/SensorProfile.cs` — where `Infrared`, `IrThresholdWPerM2` and `LookDownPenalty` go; every new
  field needs a control or an exemption in `tools/check-tunables.py`.
- `Ksa/OpticalHeads.cs` — the shape to copy for a roster of standalone sets (step 3): crewed with
  the craft, followed across a split by part reference, never by ordinal.
- `Ksa/Armaments.cs` — the shape to copy for the per-craft picture (step 4): derived every frame,
  never pinned.
- `Ksa/OpticalHead.AimPartFrame` and `Sim/OpticHold.cs` — the director's rung order. The picture's
  contact goes below the launcher's cue, under the tracking switch.
- `WeaponSystem.UpdateBeamFireControl` — where the laser picks its target; the picture is its
  fallback when nothing is designated.

**Order.** Steps 1 → 2 → 3 → 4 are code and can be done and tested headlessly before any art exists:
register the IRST against a placeholder part Id only once step 5 declares the real one. Step 5 is the
art and needs a person: model in Blender per `.claude/skills/ksa-blender/SKILL.md`, show the
blockout, and **wait for sign-off on the geometry before unwrapping or baking**. Step 7 is flying it,
per `.claude/skills/verify-in-game/SKILL.md`; a `fix` or `feat` is not called done until then.

**The part, for the modeller.** A low dome on a short faceted plinth, about 0.5 m across, that
surface-attaches (`ToSurface`), so several can ring a hull for all-round cover, as the YAL-1's six
did. One forward cone of about 120° per unit, out of its mounting face (`BoresightMode.MountNormal`).
No moving parts, so one body and one collider. Reference:
- the YAL-1's own sensors were small domed infrared windows along the forward fuselage, one each side
  below the ranging fairing and one under the nose turret — Lockheed Martin, derived from the F-14's
  AN/AAS-42 ([Boeing, 2001](https://boeing.mediaroom.com/2001-07-11-Team-Airborne-Laser-Delivers-Infrared-Sensors-for-Lab-Testing-and-Aircraft-Integration));
- the aircraft: [YAL-1A](https://commons.wikimedia.org/wiki/File:YAL-1A_Airborne_Laser_unstowed.jpg),
  [all photos](https://commons.wikimedia.org/wiki/Category:00-0001_(aircraft));
- shapes of the class: [IRST category](https://commons.wikimedia.org/wiki/Category:Infra-red_search_and_track),
  the [F-35's flush window](https://commons.wikimedia.org/wiki/File:Close_up_of_first_DOD_F-35_at_its_new_home.jpg),
  and IRST21 in a [Super Hornet's tank nose](https://commons.wikimedia.org/wiki/File:VX-9_FA-18F_with_AIM-174_missiles.jpg),
  which is the later pod variant.

**Open decisions — settle before step 1** (the proposed answers are in *Decisions to make* above):
range carried but drawn as a bearing; no visual range for sensorless craft; directors do not feed the
picture.

**Traps already known.**
- A `Sim/` entry point takes both positions and both velocities, never a relative velocity worked out
  in `Ksa/`, and is tested for invariance under a shared velocity.
- `WeaponSurvey.Match` takes the first component entry for a part Id, so the IRST gets exactly one.
- An IRST does not transmit: `Emits` stays false, so `RadarSilent` and anti-radiation rounds ignore it.
- A shipped part's subpart list is append-only and its Id is never renamed; get both right before the
  first commit that declares it.
