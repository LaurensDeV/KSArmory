# Finding targets for the directors: an infrared search part

**A plan, not a record.** Tick each step as it lands. Steps 0–5 and 4b are built and none is flown;
the panel (step 6) is not built.

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
- `Sim/InfraredSearch.cs`: `Sees(profile, intensity, range, airAtEye, airAtTarget, againstGround)`.
  Detected when `I · τ / R² ≥ E_min`, and `E_min` multiplied by `LookDownPenalty` when the line from the
  eye past the contact meets the body (`SeenAgainstGround`); `τ` is in the built note below.
- The heat comes from `Sim/Signature.cs` unchanged (`HeatOfCraft`, `HeatOfRound`), so the IRST, the
  seekers and the flares all agree about how bright a thing is.
- New `SensorProfile` fields, all off by default: `Infrared` (detection by this law instead of by
  range and cross-section), `IrThresholdWPerM2`, `LookDownPenalty`.
- **Calibration targets**, each pinned by a test (`InfraredSearchTests`):
  - a fighter at military power (75 kN, 4 kW/sr) tail-on, both at 10 km altitude: seen at 50–90 km;
  - a 1 MN booster in thin air: seen beyond 200 km;
  - a craft with its engines out (1 kW/sr) at sea level: seen within a few kilometres, not tens;
  - the same craft looked down on: a fraction of that range;
  - no air at all: range limited by the threshold alone.
- Invariance test: the answer moves with neither craft's ecliptic velocity (CLAUDE.md, *a `Sim/` entry
  point differences its own inputs*).
- [x] Built. **The extinction is not linear in density**: no constant then gives both a fighter at 60 km
  from 10 km up and an idling craft at sea level at a few. It is 0.4/km at sea level times the density
  ratio to the fourth (water vapour's scale height is about a quarter of the air's), taken as the
  log-mean along the path. Threshold 6e-7 W/m², look-down x10: fighter 68 km, 1 MN booster 248 km,
  engines out 8 km (5 km looked down on), fighter in vacuum 82 km. The law takes no velocity, so the
  invariance test translates every position instead.

### 2. A set that scans by heat — about half a day
- `Ksa/Radar.cs` asks `InfraredSearch.Sees` instead of `ThreatModel`'s range test when the profile is
  `Infrared`; IFF, terrain masking and wreckage rules are unchanged. `Emits` stays false, so an
  anti-radiation round cannot home on it and `RadarSilent` does not apply.
- The contact's heat is read from the craft's live thrust (`KsaWorld`), as the seekers already do.
- [x] Built: `IContact.HeatKwPerSr`, asked after range and cone; `Radar.TooFaint` counts the rest.

### 3. A roster for standalone search sets — about a day
- Today a set only exists inside a launcher or a director. `Ksa/SearchSets.cs`: one `Radar` per IRST
  part fitted, crewed with the craft and followed across a split, the way `Ksa/OpticalHeads.cs` is.
- `ComponentProfile` with `Role = Sensor` for the part; a craft carrying only IRSTs is an observation
  post, not a weapons system.
- [x] Built: `SearchSetProfile` and `Arsenal.SearchSets` (empty until step 5), `Ksa/SearchSets.cs`. Keyed
  on the `Part` itself rather than crewed and handed over: a set carries no settings and no magazine.
  Its side is its craft's (`TeamRoster.For`), so a craft with only IRSTs has no flag to set.

### 4. One picture per craft — about a day
- `Ksa/CraftPicture.cs`, derived every frame and never pinned (the `Armaments` reasoning): the union of
  every searching set's tracks on the craft — IRSTs and launcher radars — keyed on the target handle,
  the best by `ThreatModel` priority.
- The choice itself lives in `Sim/` (`Sim/PictureChoice.cs`), so it is tested headlessly: the same
  contact from two sets is one entry, and a hostile beats a passer-by.
- Who reads it, below everything more specific (a laser spot found by step 4b ranks with these cues):
  - a director with **Track** on, after a shift-click and its own launcher's cue
    (`OpticalHead.AimPartFrame`);
  - the laser weapon's target when it has no designation (`WeaponSystem.CuedHandle`).
- [x] Built. The picture hands out a **handle**, which the taker's own cued-only set scans and judges on
  its own IFF: `OpticalHead.Pictured` becomes its set's cue under **Track** below a designation and the
  launcher's cue, and `WeaponSystem.CuedHandle` falls back to it for a beam on auto-engage. A launcher
  radar counts when its set is `Searches` (not cued-only, a search scope). A director's set beyond its
  own range still holds nothing, which is honest: a LITENING does not see what the IRST sees at 68 km.

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
- Modelled in Blender per `.claude/skills/ksa-blender/SKILL.md`, after the F-35's EOTS: **a low smooth
  fairing whose nose rolls in to a faceted window set flush** — seven flat panes, a hexagonal front one
  facing forward and 20° up ringed by six trapezoids only 6° off it, with 3 mm frame bars on the seams
  standing 0.6 mm proud — over a sensor head that turns in a round bay behind them. The nose runs to a
  round opening and takes the hexagon only at its edge, so the facets crease the skin at the window and
  nowhere else; its top stays level with the crown, so the top line runs in one curve to the tail. A
  bell-shaped section with a fillet crossing into the hull at about 20°, and a flat bottom with a 5 mm
  skirt below the mounting face, so it sits cleanly on a flat panel or a wing and a thin wall hides the
  skirt. A deep curved keel that closed the gaps on small round tanks was dropped: it poked through any
  thin host. On a 1.25 m tank with the part's length across the curve the ends show a gap, as most
  surface parts do. About 0.44 m long, 0.23 m wide and 0.19 m tall, the origin mid-way along
  where the fairing meets the skin. Three subparts: the body, the head (a lens on a yoke, at 0.85 scale
  so the hump can sit low) and the glass, a `PartModelGlass` — KSA draws that at a fixed 0.75 opacity,
  so the head shows dimly, as through tinted glass. Surface-attaching, so several turned different ways
  ring a hull for all-round cover — the YAL-1's own layout. Geometry signed off before any unwrap or
  bake.
  - The head looks at what its set holds, or sweeps a figure of eight across its field
    (`Sim/SearchHead.cs`), each set at its own phase. Drawn only: the set sees its whole cone.
  - A first blockout, a dome standing on a bolted octagonal plinth, was rejected: no airborne IRST
    looks like that, only a ship's deck mount does. A second, a ball in front of a fairing on a flat
    doubler, scored 4/10 for realism with three reviewers: the cowl stopped behind the ball, the
    flanks were concave, and a flat plate floats off a curved hull. A solid ball in a raked cowl
    scored 5/10, and a glass dome over a turning head read as too round. A faceted housing with a
    gem of a window read as too blocky: on the EOTS only the window is faceted, set nearly flush in
    smooth skin. A tall smooth fairing with a recessed window and 5 mm bars scored 4/10 as a helmet.
- Declared in `KSArmoryAssets.xml` / `KSArmoryGameData.xml`; registered as a `SensorProfile` with a
  forward cone of about 120° along the part's +Y (`BoresightMode.PartForward`) and a `ComponentProfile`; `validate-parts.py` and `checkmesh.py` clean.
- [x] Built: `KSArmory_Prefab_Irst`, subparts body, head and glass in that order — append-only from
  here — with `Arsenal.Irst` and a Sensor `ComponentProfile`. 40 kg, one 0.185 × 0.469 × 0.263 m
  collider box above the mounting face. `validate-parts.py` holds `Arsenal.Irst.HeadPivot` to the
  head's `<Position>`. The art's sources — `IRST.blend`, `irst_v21.py` and the prep, unwrap, bake,
  compose and export scripts beside it — are in the art folder, outside this repository.
- [x] Flown 2026-10-20 (CHECKLIST 7.6c-3): four on a craft crew, one sees a rocket under power at
  9.1-9.5 km and the picture hands it to the LITENING under **Track**; the window draws as glass and
  the heads sweep and hold, by readback.
- [x] The head shows behind the glass, seen in game from 4-5 m. KSA's glass shader takes a tenth of
  the tint into its own grey at a fixed 0.75, so the tint barely matters; what shows the head is its
  contrast against the bay, so the head is pale and the lens gold.
- Optional later: a pod form, a 2.5 m pylon store, for fighters.

### 6. The panel — about half a day
- The IRST's component row: on or off, how many contacts it holds, the best one.
- The Radar tab's scope shows IRST contacts, marked as heat contacts.
- A director's row says where its target came from: shift-click, its launcher, or the craft's picture.

### 7. Flown — one session
- A YAL-1 craft with IRSTs against a rocket under power at 100–300 km: the IRST sees it, the turret
  swings on with **Track** on, and auto-engage burns it. The same rocket with its engines out is not
  seen until close.
  - [x] At 8 km, flown 2026-10-20 (CHECKLIST 7.6c-3): two rockets climbing from the ground, each seen
    by a different IRST, both burnt from the picture with nothing designated, and their pieces left
    alone. 100–300 km and the engines-out case are not flown.
- A LITENING on a craft with an IRST picks up a jet under power with no shift-click.
- The Pantsir's director still follows the Pantsir's radar.
- CHECKLIST entries ticked with the numbers seen.

## Decisions — settled 2026-10-20, all as proposed

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
  IFF, wreckage, `CuedOnly`, and the heat test beside the range test for an `Infrared` set.
- `Sim/SensorProfile.cs` — `Infrared`, `IrThresholdWPerM2` and `LookDownPenalty`; every field needs a
  control or an exemption in `tools/check-tunables.py`.
- `Ksa/OpticalHeads.cs` — the shape to copy for a roster of standalone sets (step 3): crewed with
  the craft, followed across a split by part reference, never by ordinal.
- `Ksa/Armaments.cs` — the shape to copy for the per-craft picture (step 4): derived every frame,
  never pinned.
- `Ksa/OpticalHead.AimPartFrame` — the director's rung order. The picture's contact is its set's cue
  below the launcher's, under the tracking switch.
- `WeaponSystem.CuedHandle` — where the laser takes the picture's contact when nothing is designated.

**Order.** Steps 1 → 2 → 3 → 4 are code and can be done and tested headlessly before any art exists:
register the IRST against a placeholder part Id only once step 5 declares the real one. Step 5 is the
art and needs a person: model in Blender per `.claude/skills/ksa-blender/SKILL.md`, show the
blockout, and **wait for sign-off on the geometry before unwrapping or baking**. Step 7 is flying it,
per `.claude/skills/verify-in-game/SKILL.md`; a `fix` or `feat` is not called done until then.

**The part.** A faceted window set flush in a low smooth fairing, over a turning sensor head; it
surface-attaches (`ToSurface`), so several can ring a hull for all-round cover, as the YAL-1's six did.
Three subparts (body, head, glass) and one ~120° cone along the part's +Y (`BoresightMode.PartForward`)
— step 5 has the rest. Its generator is `irst_v21.py` beside `IRST.blend` in the art folder, outside
this repository. Reference:
- the YAL-1's own sensors were small domed infrared windows along the forward fuselage, one each side
  below the ranging fairing and one under the nose turret — Lockheed Martin, derived from the F-14's
  AN/AAS-42 ([Boeing, 2001](https://boeing.mediaroom.com/2001-07-11-Team-Airborne-Laser-Delivers-Infrared-Sensors-for-Lab-Testing-and-Aircraft-Integration));
- the aircraft: [YAL-1A](https://commons.wikimedia.org/wiki/File:YAL-1A_Airborne_Laser_unstowed.jpg),
  [all photos](https://commons.wikimedia.org/wiki/Category:00-0001_(aircraft));
- shapes of the class: [IRST category](https://commons.wikimedia.org/wiki/Category:Infra-red_search_and_track),
  the [F-35's flush window](https://commons.wikimedia.org/wiki/File:Close_up_of_first_DOD_F-35_at_its_new_home.jpg),
  and IRST21 in a [Super Hornet's tank nose](https://commons.wikimedia.org/wiki/File:VX-9_FA-18F_with_AIM-174_missiles.jpg),
  which is the later pod variant.

**Decisions** (settled, see above): range carried but drawn as a bearing; no visual range for
sensorless craft; directors do not feed the picture.

**Traps already known.**
- A `Sim/` entry point takes both positions and both velocities, never a relative velocity worked out
  in `Ksa/`, and is tested for invariance under a shared velocity.
- `WeaponSurvey.Match` takes the first component entry for a part Id, so the IRST gets exactly one.
- An IRST does not transmit: `Emits` stays false, so `RadarSilent` and anti-radiation rounds ignore it.
- A shipped part's subpart list is append-only and its Id is never renamed; get both right before the
  first commit that declares it.
