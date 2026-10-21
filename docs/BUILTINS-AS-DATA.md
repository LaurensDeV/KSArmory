# Every built-in weapon as data — plan

*A plan, not a record. 2026-10-21. Built from five exploration reports, three reviews of an earlier
"split into six mods" plan, and three reviews of this one (code facts, design, tests/tooling). Every
file:line below was checked by at least one reviewer.*

## Decisions

1. **Every built-in becomes XML inside KSArmory.** Nothing is constructed in `Sim/Arsenal.cs` afterwards.
   Parts, art, part Ids, subpart Ids and template Ids do not move or change.
2. **One file per family** in `src/KSArmory/KSArmory/`. The reader takes every `.xml` in the folder;
   `Bodies.xml` is told apart by its root (`BodyReader.IsBodies`), not its name.
3. **No split into separate mods now.** The family files are the seam if that is ever wanted.
4. **The example mod gets a full reference copy** of the family files, outside its `KSArmory/` folder so
   the scanner never loads it, produced by a tool rather than by hand.
5. **A player can disable built-in families.** The part stays and stops being anything; the family's
   rounds, sensors and decoys stay registered as a library. Read at startup; restart to apply.
6. **Names stay bare** (`57E6`, `KSArmory:20MM`).

## Families (seven)

| Id (stable, persisted) | File | Part-bound (switched off when disabled) | Library (always registered) |
| --- | --- | --- | --- |
| `Pantsir` | `Pantsir.xml` | Launcher6 launcher, its roof `<Optic>` | 57E6, 30MM, 1RS1 |
| `Rails` | `Rails.xml` | LAU-7, LAU-128, LAU-118 | AIM9J, AIM120C, AGM88 + their seekers |
| `Guns` | `Guns.xml` | CIWS, Mk 42, M197 | 20MM, 5IN54, VPS2, MK68, TSU |
| `Sensors` | `Sensors.xml` | EO director, LITENING, IRST | EO, pod sensor, IR search sensor |
| `Countermeasures` | `Countermeasures.xml` | ALE-47 flare, ALE-47 chaff | MJU-7, RR-170 |
| `DirectedEnergy` | `DirectedEnergy.xml` | YAL-1 optic + beam launcher | beam-director sensor |
| `Strategic` | `Strategic.xml` | B61 rack, MIRV bus (incl. its Guidance row) | B61, MK21, BOMBSIGHT, bus designation |

Sensors are their own family because a tick box should turn off what its name says, and because sights
serve other people's weapons: disabling Rails for a replacement missile pack must not take the LITENING
that designates for it. The Pantsir's roof director names `EO` from `Sensors.xml`; that works because the
library is registered from every file before anything part-bound (below). `KSArmory_Prefab_Rail`, the bare
rail, belongs to no family and needs no profile.

## How the core is registered (the mechanism everything else rests on)

**Today** `Arsenal` is seeded into `Catalogue` statically (Catalogue.cs:15-19), and KSArmory's XML is read
in `[StarMapAllModsLoaded]` by `InstalledPacks.RegisterAll` in manifest order (KSArmoryMod.cs:154-163). A
**code** pack calls `Armoury.Register` from its own `[StarMapBeforeMain]`, which runs *before*
AllModsLoaded (EXTENSIBILITY.md:150-152), and resolves `KSArmory:X` against the catalogue as it stands
(Armoury.cs:305-307, PackReader.cs:616-620). Remove the static seed and every code pack naming a built-in
is refused — already true today of `57E6`, `30MM`, `1RS1`.

**After**, `CoreRegistration` runs from KSArmory's own **`[StarMapBeforeMain]`** (new; KSArmory has none
today). Checked in StarMap's source (StarMap-src 4afe14e): `ModLoader.PrepareMods` walks the manifest and
calls `RuntimeMod.InitializeMod`, which invokes a mod's `BeforeMain` inline; a mod declaring
`ModDependencies = [{ ModId = "KSArmory" }]` is parked by `AllDependenciesLoaded` until KSArmory has
initialised (`CheckForDependentMods`), so **KSArmory's `BeforeMain` runs before any code pack's**, whatever
the manifest order. A code pack cannot skip that dependency: it is what lets it load `KSArmory.dll` at all.
`[StarMapImmediateLoad]` is **too late** — it is a prefix on KSA's `Mod.PrepareSystems`
(`ModPatches.cs`), which runs in `ModLibrary.PrepareAll`, after every `BeforeMain`. `BeforeMain` gets no
`Mod`, so the folder comes from `typeof(KSArmoryMod).Assembly.Location`, as `Build.cs:26` already does.

1. Read `builtins.json` (disabled family ids; unknown ids logged and ignored).
2. List the core folder's `.xml`, **sorted ordinally**, skip the `Bodies` root.
3. **Pass 1:** register every file's `<Munition>`, `<Sensor>`, `<Decoy>` — whatever the family's state.
4. **Pass 2:** register `<Launcher>`, `<Optic>`, `<SearchSet>`, `<Dispenser>` and their component rows for
   enabled families only.
5. Always under `PackReader.BuiltInSource` — `InstalledPacks` passes `entry.Id` (InstalledPacks.cs:335),
   so the bare names depend on this being explicit, not on the mod folder being called `KSArmory`.

`InstalledPacks.RegisterAll` then skips KSArmory's own weapon files (else every definition faults
"already registered") but still reads its `Bodies.xml`, and sorts each pack's files ordinally
(`Directory.GetFiles` is unordered on ext4).

The two passes are what make cross-family references and the disable switch the same mechanism, and what
lets a third-party pack keep referencing a disabled family's rounds.

## Hazards, and where each is defused

| Hazard | Where | Defused in |
| --- | --- | --- |
| Name lookup falls back to element zero | `Arsenal.Named` (Arsenal.cs:1785-1792); `BuiltIns`; tests | Phase 0 |
| Code packs resolve before core XML is read | above | Phase 0 |
| File order is filesystem order | InstalledPacks.cs:343; `BuiltIns` | Phase 0 |
| Test race: `ArmouryTests` (in `"catalogue"` collection) registers into the global catalogue while `ArsenalTests`/`CatalogueTests` enumerate it, not in that collection | CatalogueCollection.cs:10 | Phase 0 |
| The build copies `KSArmory\Weapons.xml` by name | KSArmory.csproj:52 | Phase 0 (`KSArmory\*.xml`) |
| A renamed file survives on installs (`deploy.sh:79-86`, `package.sh:49-59`, players unzipping over) and double-registers | | Pantsir slice |
| `validate-parts.py` reads `Arsenal.cs` by regex in ~20 functions; `check_registered_part_ids` hard-fails once it is empty | 1072-1165, 1199, 1281-1366, 1383, 1491-1503, 1557-1644, 1684-1721, 1729-1750, 2094-2121 | Phase 1, both sources until the last slice |
| Minted component rows ≠ hand-written ones | PackReader.cs:402-431 vs Arsenal.cs:1581-1742 | first slice that needs each |
| `Catalogue.Register` drops component rows whose PartId is not a launcher | Catalogue.cs:104-107 | Sensors slice |
| Disabling renumbers launcher ordinals, so settings land on the wrong weapon and are overwritten on save | LauncherPart.cs:60-87; WeaponSystems.cs:815-816 | Phase 3 |
| Mk 21 shape-drag swap and sub-step override are **per-installation settings used as batch arms**, not static defaults | IcbmComputer.cs:811-826; IcbmConfig.cs:739,762; ShotArms.cs:76; shot-batch.sh:6 | drag: retired before Strategic (below); sub-step: kept |
| Generated XML loses `Arsenal.cs`'s 731 comment lines (provenance, measured numbers) | | every slice |
| Faults name the source, not the file — six files under `KSArmory` | `PackFault(source, …)` | Phase 0 |
| Root attributes are never checked, so `Family=` on a third-party file is silently ignored | PackReader.cs:58-74 | Phase 3 |

## Phases

### Phase 0 — mechanism (≈2 sessions, `refactor` / `fix(packs)`)

**Landed 2026-10-21** (`82df2fab`, `0e0b91c2`), flown with `head-on`. Where it differs from what is
written below:
- Loud lookups are `Catalogue.MissingNameThrows`, on for the whole suite and off in the game. The game
  keeps the fallback; the scenario and bridge lookups resolve a profile's own key, so need no change.
- "Two passes" is a names-first read: every core file is read once for its round and sensor names,
  then each is read against all of them and registered. The disable switch (Phase 3) filters the
  part-bound lists of that second read.
- A fault's file is in the log line (`Weapons.xml: …`) rather than in `PackFault`.
- `BuiltInDefinitionsTests` still names `Weapons.xml`; it moves with the Pantsir slice, which renames
  the file.

1. **Loud lookups.** `BuiltIns`, tests, `GunneryScenario.cs:202`, `Bridge.cs:960` use `Try*Named` or a
   throwing lookup; the element-zero fallback goes. Empty registries are tolerated.
2. **`CoreRegistration`** as above (both passes, sorted, `BuiltInSource`, own entry skipped by
   `InstalledPacks`). While `Arsenal` still exists, its seed and the XML coexist exactly as today. This
   changes outcomes for a code pack that conflicted with a built-in → `fix(packs)`.
3. **`BuiltIns`** loads the same way (same class, shared code path, so the tests cannot load differently
   from the game). `BuiltInDefinitionsTests.cs:23` stops naming `Weapons.xml`.
4. **csproj** copies `KSArmory\*.xml`.
5. **Test isolation:** every test that enumerates the global catalogue joins the `"catalogue"` collection,
   or `ArmouryTests` registers into a detached catalogue.
6. **Faults carry the file name.**

Everything else that used to be "groundwork" lands in the slice that first needs it, so no capability ships
before something uses it.

### Phase 1 — validation reads XML too (≈2–3 sessions, `refactor`/`chore`)

- Run the core folder through the existing pack path (`check_pack_weapons` 1859-1925, `validate_pack`
  1965) and extend that path with the geometry, pivot, muzzle, seat and search-set checks the regex
  functions do today. **Keep the `Arsenal.cs` regex alongside** until the last slice deletes it, so no
  family loses its art gate mid-programme. `check_registered_part_ids` reads both.
- `checkswept.py:253-357` reads every core XML file.
- The generators keep printing; what they print changes to XML fragments for families already moved
  (`pantsir.py:38,1053,1066`, `sidewinder.py:168`, `optic.py:137`, `import-litening.py:49,479`). They never
  wrote `Arsenal.cs`; `muzzles.json` stays their only written output.

### Phase 2 — the move, one family per slice

Each slice:

1. Reader additions for that family, each with a `PackReader` test, a `WEAPON-PACKS.md` line and a
   regenerated `PACK-API-SURFACE.md`.
2. The family's XML: **values emitted** from the C# profiles by a one-off emitter (round-trip float
   formatting); **comments ported by hand**, trimmed to CLAUDE.md's rules, reviewed in the slice diff.
3. **Equality test.** `PackReader.Read(text, BuiltInSource, …)` is pure and public; compare by reflection
   over public fields *and* init properties, recursing into `Tube`, `BoostStage`, `SpinNozzle`,
   `BuiltInComponent`, `RoundSpin`, `BeamProfile`. Exact equality (degree fields are 0; round-trip
   formatting makes the rest exact). Compare against the `Arsenal` statics before anything mutates them —
   no test assigns into them today; keep it that way. **Also compare the ordered post-registration
   `Catalogue.Launchers` and `Components` lists against the C# order**, so a row label, row shape or
   switcher order change is a test failure rather than a judgement call.
4. Delete the C# entries; re-point tests through `BuiltIns` accessors (mechanical); edit `ArsenalTests`'
   fixed lists deliberately.
5. `./tools/check-all.sh`; **`./tools/test.sh --all` for Strategic** (11 of 14 studies fly the Mk 21, and
   `check-all.sh:145` excludes studies).
6. **Fly it before the next slice** (below).
7. Commit. `refactor` if the ordered-lists comparison holds; `feat(packs): …` when the slice opens a pack
   capability (articulation, beam, decoys, search sets), committed after the flight. Docs in the same
   commit.

| # | Family | Reader / engine work in the slice | Test refs | Flight |
| --- | --- | --- | --- | --- |
| 1 | Rails | `FireControlLabel` (preserves today's labels exactly) | 8 files / 28 | a rail shot each, LAU-118 vs a silent and an emitting set |
| 2 | Guns | (after the M197 label fix) Mk 42: `GunBarrelMarker GunRecoilMetres GunRecoilSeconds GunReturnSeconds GunshotSoundId`; M197: `GunCylinderMarker GunRodMarker GunFeedMarker GunRotorMarker` + four pins/pivots, `GunFeedRatio GunRotorSpinUpSeconds GunRotorSpinDownSeconds TraverseLimitDeg GunSoundId`; fault a marker without its pin | 16 / 30 | `gunnery` on each gun |
| 3 | Pantsir | rename `Weapons.xml` → `Pantsir.xml` + roof `<Optic>`; **ship a tombstone `Weapons.xml`** (`<WeaponPack Schema="1"/>`) for a few releases, and `deploy.sh`/`package.sh` clear `KSArmory/*.xml` before copying | 23 / 75 (already `BuiltIns`) | `head-on` |
| 4 | Sensors | `<Sensor CuedOnly Infrared IrThresholdWPerM2 LookDownPenalty>`; `<SearchSet PartId DisplayName Sensor HeadMarker HeadPivot HeadTravelDeg HeadSlewDegPerSecond ScanHalfWidthDeg ScanPeriodSeconds ScanNodDeg>`; `<Optic>`/`<SearchSet>` mint their rows and `Catalogue.Register` accepts rows on non-launcher parts (row text exactly as today, e.g. "Infrared Search and Track Sensor"); Catalogue owns search sets (`Catalogue.cs:205`, `SearchSets.cs:182`); `OpticalHead.cs:28,30` defaults → `None`; `PackAudit` covers search sets | small | director look, LITENING spot, IRST picture |
| 5 | Countermeasures | `<Decoy Name DisplayName Kind MassKg PeakSignature RiseSeconds LifeSeconds DragPerMetre>`; `<Dispenser PartId DisplayName Kind Decoy Count SalvoSize SalvoIntervalSeconds EjectDirection EjectSpeed>` (`Kind` checked against the decoy's); `PackContents` lists; `knownDecoys` passed to the reader; Catalogue owns dispensers/decoys (`Catalogue.cs:218,227`); minted Countermeasures row; `PackAudit` covers dispensers | 4 / 55 | flare vs AIM-9, chaff vs AIM-120 |
| 6 | DirectedEnergy | `<Optic>` child `<Beam …8 fields>` (an empty `<Beam/>` still yields an instance); `<Launcher FiresBeam BeamArmamentLabel>`; `Munition` optional when `FiresBeam` and returned as `""` (`LoadoutFor` keys on length 0); no-tube/no-gun fault accepts a beam; fault a beam launcher whose part has no `<Beam>`; a way to declare a part's rows exactly (YAL-1 has no Sensor row) | 1 / 3 | lase a target |
| 7 | Strategic | `<Munition SubStepSeconds PreferredStepSeconds MaxChargeKg>`; bus `TrimAccelerationMetresPerSecond2 TrimWorstAxisExhaustVelocity HopEngineThrustNewtons HopEngineExhaustVelocity HopEngineAccelerationMetresPerSecond2`, `<ReleaseSpin Rate SpinUp Ignition><Nozzle Mouth Exhaust/>`. The Mk 21 moves with physical drag only (the comparator was retired before this slice, below); `WarheadSubStepMs` stays, and `RoundAtSubStep` becomes a generic helper on `MunitionProfile`/`Catalogue`. `Bridge.cs:1400` burst template → core-owned. Then delete `Arsenal.cs` and the regex in `validate-parts.py`. | 64 / 172 | `drop`, `mirv` |

**Before the Strategic slice — retire the Mk 21's drag comparator** (its own commit; **landed 2026-10-21**. It
exposed three things the constant had hidden: the divert reach over-promises on the round the game flies
(`ICBM-OUTSTANDING.md` 1.24), two aim rigs read wrongly on it (2.8), and `PredictorStepGapTests`' rig never
had the midpoint drag the game ships. Tests that pin an algorithm on, or reproduce a flight with, the
retired round now fly `RetiredRounds.Mk21Constant`.) Shape drag won its
paired night on 2026-09-18 and has shipped on since v0.9.0, so the hand-typed constant is flown by nothing
but the tests and a batch arm nobody plans to fly. The Mk 21 declares `MassKg=270 CalibreMm=550
DragCoefficient=0.1` directly and `DragK` goes; `IcbmConfig.WarheadDragFromItsShape`, its control and
`check-tunables.py` entry, `Arsenal.Mk21WithDragFromShape`, the name test and swap at
`IcbmComputer.cs:811-817`, and the drag arm in `docs/SHOT-PROTOCOL.md` go with it. MK21 leaves
`ArsenalTests`' hand-typed-drag list. **The ~114 test references that flew the constant now fly what players
fly**, so every expectation that moves is re-derived, not rubber-stamped, under `./tools/test.sh --all`.
A save's `ballistic.json` carrying the removed field must still load (check `BallisticSettings` ignores an
unknown key). Type: `refactor` if no player-visible flight changes (the flag shipped on, so none should),
flown once with `mirv` to confirm. Fresh experiments later start from the physical drag.

**No `dev`→`main` merge mid-programme**: each merge publishes. And start the slices outside auto mode —
long weapon-data writes have been stopped by the classifier before.

### Phase 3 — the disable switch (≈1–1.5 sessions, `feat`, after its flight)

- **Storage:** `<user dir>/KSArmory/builtins.json`, beside the loose `systems.json` (`SettingsStore.LoosePath`).
  Global, not per-save: registration happens before any save opens and the catalogue then freezes.
- **Read** in `CoreRegistration`'s step 1. **Root `Family=`** is required on core files and refused on a
  third-party file (add the root `Sound()` check).
- **What a disabled family registers:** its library only. No launcher, optic, search set, dispenser and
  **no component rows** — including the bus's Guidance row, or a craft becomes a weapons system that cannot
  fire.
- **Hide disabled parts in the editor**: add `Hidden` to their templates at startup, as
  `KSArmory_Prefab_Rail` already carries (`PartTemplate.IsHidden`); saves still load them.
- **Ordinals:** the settings key is craft name + launcher ordinal, and the ordinal counts *registered*
  launcher parts, so disabling one family moves the others' settings. Count a disabled family's launcher
  parts as placeholders in `LauncherPart.FindAll/FindNth` so ordinals, and therefore saved settings, do not
  move. Test: CIWS + B61 rack, disable Guns, the rack keeps its own settings; re-enable, both intact.
- **A third-party launcher on a disabled family's part Id is accepted** (the total-conversion case). The
  refusal while it is enabled names the family ("claimed by built-in family Rails").
- **Control:** a "Built-in weapons" submenu in `Ui.DrawMenuContents` (shared by the mod's bar and
  ModMenu), one tick per family, "restart to apply" shown only when the ticks differ from what loaded. The
  startup log and every scenario's first line name the families loaded.
- **`KSARMORY_NO_BUILTINS=1`** (developer-only): nothing from the core files, library included; every
  KSArmory part inert and hidden.
- **Rounds in flight** are not saved anyway, so a restart loses nothing new.

### Phase 4 — the example mod's reference copy (≈0.5 session)

- `tools/export-reference.py` copies the core family files into `../KSArmory-example-mod/reference/`,
  each headed `Generated from KSArmory <commit> (<date>) — do not edit; the live file is
  src/KSArmory/KSArmory/<name>.xml`, plus a hand-written `reference/README.md` index ("rotary cannon
  linkage → `Guns.xml`, `GunCylinderMarker`…", "a beam → `DirectedEnergy.xml`, `<Beam>`") that also links
  each file on GitHub at that commit.
- **Run by hand** when the example mod is updated, with `--check` on demand. Not in `check-all.sh` (that
  is the pre-push hook, and would block every family edit on a sibling repo) and not in CI (the example mod
  has no remote for CI to push to). A copy that falls behind says which commit it was taken from, so it is
  dated rather than wrong.
- The example README says the file name is the author's choice and any `.xml` in `KSArmory/` is read.

### Phase 5 — release

- Each slice already flew. Before the merge, one pass: an existing save loads with every craft; disable
  Guns, restart, the CIWS craft loads as hidden-inert parts and a third-party pack using `KSArmory:20MM`
  still loads; re-enable, settings intact. Grep the newest `KittenSpaceAgency.*.log` for exceptions and
  `KSArmory.log` for pack faults.
- Changelog/README note: "if you edited `Weapons.xml`, your edits now belong in `Pantsir.xml`"; where the
  tick boxes are. Whether the widened pack surface is a minor tag is your call.

## Docs, in the commits that falsify them

- **CLAUDE.md:** intro; :183 (`feat` rule names `Arsenal.cs`); 342-343; 725; 919-968 ("Adding a weapon
  system", the two registries); 1025; 1227-1230 (`Arsenal.LoadoutFor`, shared `Arsenal` instances); 2306;
  2371; Layout rows for `Arsenal.cs`, `Catalogue.cs`, `KSArmory/Weapons.xml`; the `ArsenalTests.*` names if
  that class is renamed. `check-docs.sh:50-60` will fail until the Layout rows change — that is the gate.
- `README.md:164,439-440,606`; `CONTRIBUTING.md:187`; `docs/WEAPON-PACKS.md` (new elements, file naming,
  the total-conversion case; "What a pack cannot do" loses *a beam* and *articulation beyond
  traverse-then-elevate*); `docs/PACK-API-SURFACE.md`; `docs/EXTENSIBILITY.md`; `docs/MODULARITY.md`;
  `docs/FROM-KSP-MODDING.md:19,81-82,94-96`; `docs/SHOT-PROTOCOL.md:701-709` (where an arm edits the
  Mk 21); `docs/WEAPON-TAXONOMY.md:20,248`; `docs/POSTBOOST-BUS.md:109-110`; `docs/GUIDANCE-SECTION.md:4`;
  `docs/PACK-PORTING.md:10,100`; `tools/model/README.md:74`; `.claude/skills/ksa-blender/SKILL.md:366,466`;
  `CHECKLIST.md:456,2249` + the disable-switch items.
- Stale comments and 16 `cref="Arsenal…"` (no compiler warning, `GenerateDocumentationFile` is off):
  `WeaponSurvey.cs:83`, `LauncherPart.cs:27`, `UiTuning.cs:84`, `Ui.cs:936`, `IcbmConfig.cs:735`,
  `Catalogue.cs:3-11,226`.
- **Records are left alone** (ACCURACY-PLAN, MIRV-NEXT, KINETIC-FLOOR, RV-POINTING, METRE-LEVEL, AUDIT).

## Settled questions

1. **The hook** is KSArmory's own `[StarMapBeforeMain]`, from StarMap's source (above).
2. **"M97 turret fire control"** (Arsenal.cs:1714) is fixed to "M197 turret fire control" as its own
   `fix(ui)` commit **before** the Guns slice, so the move preserves the corrected label.
3. **Mk 21 drag:** the hand-typed comparator is retired; the Mk 21 carries its physical drag and nothing else.
4. **Reference copy** kept, generated by hand-run tool with a commit header and GitHub links.

## Size

| Phase | Sessions |
| --- | --- |
| 0 mechanism | 2 |
| 1 validation | 2–3 |
| 2 seven slices incl. flights | 8–10 (Guns ~1.5, Countermeasures ~1, Strategic ~2.5) |
| 3 disable switch | 1–1.5 |
| 4 reference copy | 0.5 |
| 5 release pass | 0.5–1 |
| **total** | **~14–18** |
