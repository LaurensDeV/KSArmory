# Armour as a part setting

**A plan, not a record.** A plate's material and thickness chosen in the vehicle editor's part window, saved
with the craft by KSA's own serializers, weighed by KSA's own mass code and judged by the law in
`docs/ARMOUR-PLAN.md`. It replaces that plan's part per thickness: the plate family becomes a part per *shape*.
Researched against the decompiled 2026.10.25.5707 corpus and reviewed three times — the engine's claims, the
fit with this mod's code, and the design and its risks — with what each review changed folded in. Paths are
relative to `../ksa-game-assemblies/current/src/KSA/KSA/` unless they name this repository.

## What the player gets

- **Right-click a plate in the editor**: an *Armour* section in its part window — the material, and the
  thickness on a slider that snaps to the tiers (6, 12, 25, 50 mm) and takes any whole millimetre between the
  plate's limits. Beside it the plate's mass, its change from the default, its mass per square metre, and **what
  that thickness stops** of the rounds registered, worked out by `ArmourLayer` against each munition.
- **The thickness shown is the plate's real one**: the saved value is the thickness at 1x, and the window shows it
  times the editor's scale, since a plate scaled to 2x is twice as thick.
- **Symmetry follows**: a change reaches every part of the plate's symmetry group.
- **The part's mass, the craft's centre of mass and the editor's delta-v** follow at once.
- **In flight, and in the editor opened on a craft already launched**, the section is read-only: material,
  thickness, the deepest crater and the part's health.

## Why this is possible

`docs/BLOCKED-ON-KSA.md` says a mod cannot save per-part data. That holds for a *module*, not for *data*:

- **The save serializers are built after a mod's first hook.** `XmlHelper` fills its overrides in an explicit
  static constructor (`XmlHelper.cs:17-46`); `VehicleSaves` and `GameSaves` build the craft and universe
  serializers from them in theirs (`VehicleSaves.cs:181`, `GameSaves.cs:135`), first touched at `Program.cs:870`
  and `:873`. StarMap calls each mod's `[StarMapBeforeMain]` inside `core.Init()`
  (`StarMap-src/StarMap.Core/ModRepository/RuntimeMod.cs:167-170`) before KSA's entry point runs. A mod earlier
  in the manifest that touches either class freezes them first, which is what step 1's check catches.
- **One override entry takes a new element.** `XmlHelper.AttributeOverrides[typeof(PartInstance),
  "Components"]` returns the `XmlAttributes` the `Components` list is built from (`XmlHelper.cs:32`); adding an
  `XmlElementAttribute("KSArmoryArmour", typeof(ArmourSaveData))` to its `XmlElements` makes both serializers read
  and write it. It is added to in place: a second `Add` for the member throws.
- **Every save and load is one of them.** Library craft, universe saves, save-as and the network join
  (`KSA.Networking.Messages/JoinGameResponseMessage.cs:62`) all write through `GameSaves.UniverseSerializer` or
  `VehicleSaves.VehicleSerializer`; `PartTree.DeepCopy` (`PartTree.cs:419`) goes through `Serialize` and
  `Deserialize` in memory. There is no quicksave, autosave, revert or editor undo.
- **An element nobody knows is skipped.** KSA handles neither `UnknownElement` nor `UnknownNode`, so a craft
  written with the element loads where the element is not registered.

## The order of work

### 0. Merge `arm/part-scale`, flown

It holds the refusal of a plate on a launcher, `KsaWorld.ScaleOf` and the scaled tank reads this plan builds on.
Its three checks are `CHECKLIST.md` §17.

### 1. The spike: does saving work, end to end

**Build it against the build after 5707**, not this one: RocketWerkz have announced that build moves part
data into `Modify`/`Key` XML, which rewrites the loader this step hooks. The engine claims below are 5707's and
are rechecked first.

The one unknown. Two halves, and the second is the gate:

- **`[StarMapBeforeMain]`**: add the element to the override.
- **`[StarMapAllModsLoaded]`**: round-trip a one-part `VehicleSaveData` carrying the element through the real
  `VehicleSaves.VehicleSerializer`, and serialize a minimal universe carrying one through the real
  `GameSaves.UniverseSerializer`, through `XmlHelper.SerializeWithoutNaN` so the writer's filter runs. Only if
  both come back equal does the save hook ever add an element. A serializer built before the override would throw
  "type not expected" on every save carrying one, and saving is worth more than armour.

Then flown, each with a pass or fail recorded in `CHECKLIST.md`:

- **a** a library craft saved and reopened in the editor; **b** launched from the editor; **c** launched from the
  library menu; **d** a universe saved and loaded, and saved-as; **e** the `clone` console command; **f** a split
  and a docking; **g** copy, paste and a symmetry placement of a single plate;
- **h** the part's and the craft's mass after each of a–g equal to before;
- **i** a network join, both sides modded;
- **j** the mod removed: the save loads with no exception in KSA's log — the craft carrying a plate is dropped,
  since a plate is KSArmory's own part — and the mod put back: the value is back if nothing was saved meanwhile;
- **k** the time `[StarMapAllModsLoaded]`'s round trips add to startup.

If the serializers refuse the type, stop here and take *The fallback*.

### 1b. The armour definition

`<Armour>` gains `DefaultThicknessMm`, `MinThicknessMm` and `MaxThicknessMm`. `ThicknessMm` stays as a synonym
for `DefaultThicknessMm`, so a pack written today still reads; both together is a fault, as is
`Min ≤ Default ≤ Max` failing. **The area is derived, not declared**: the template's mass over the default
thickness and the default material's density, so mass and setting cannot disagree. `MaxThicknessMm` may not pass
the range BRL-MR-3960 fitted Lambert's limit over, read from the report before the number is written; past it the
law extrapolates a plate into invulnerability. `PackReader`, `Catalogue.ArmourLayerForPart`,
`validate-parts.py`'s plate check (the name no longer states a thickness; the mass box is the default thin),
`docs/WEAPON-PACKS.md` and `tools/pack-api.py` follow, and `ArmourPackTests` with them.

### 2. Save, load, copy — one unit

`Ksa/PartArmour.cs` owns the value, held per **`FullPart`** in a `ConditionalWeakTable`, so a split, which moves
the same `Part` objects (`PartTree.cs:1650-1715`), keeps it with no code, and so does the editor's launch, which
hands its live tree to `Vehicle.CreateVehicle` (`Vehicle.cs:1533-1535`) rather than serializing.

- **Save**: a postfix on the public `PartTree.Serialize()` (`PartTree.cs:398`) walks the part tree and the
  `PartInstance` tree together, as `Deserialize` pairs them (`PartTree.cs:315-345`), and appends an element to
  each armoured part's `Components`. It never takes a `LocalInstanceId`.
- **Load**: a prefix on the public `PartTree.Deserialize(PartInstance)` (`PartTree.cs:311`) takes the elements
  out of every `Components` list before the module loop sees them, and a postfix stores each against its part,
  recomputes that part's inert mass (`Part.RecomputeInertMass`, `Part.cs:1353`) and refreshes the tree's.
- **Copy**: a postfix on the public `Part(Part source, Part? parent)` (`Part.cs:1556`), which `PartCloner` uses for
  copy, paste and symmetry, copies the value and recomputes the part's inert mass — a clone root runs its rescale
  inside the constructor, before the postfix (`Part.cs:1633-1636`).
- **What is written**: `<KSArmoryArmour V="1" Material="..." ThicknessMm="..."/>`, the thickness in whole
  millimetres at 1x. `ArmourSaveData` is public and top level, derives from `ModuleBase.SaveDataBase`, carries
  `[XmlType("KSArmoryArmour")]`, a public parameterless constructor and a no-op `OnDataLoad`. Unknown attributes
  are ignored by the serializer, so a later version adds attributes.
- **What a reader does not understand is kept verbatim**: a material no loaded file defines — a pack
  uninstalled — and a thickness outside the plate's limits are written back as read, and the plate is judged and
  weighed at its default until they resolve. The window says `missing: <material>`.
- **All three or none.** Save, load and copy are one unit: if any patch fails to apply, none runs, since a load
  that works with a save that does not loses the value on the next save. That is separate from *using* the
  value: if mass or the window cannot apply, the value is still carried and the plate is judged at its default.
- **When the feature is off for the session**: a banner in the editor and a line in the log, a count of crafts
  carrying `KSArmoryArmour` from `XmlSerializer.UnknownElement` on the two public serializers, and the
  `universe.xml` or `vehicle.xml` about to be overwritten copied into `saves/<save>/KSArmory/` first — never
  beside the save, where KSA would list it as another save. Saving is never refused.

### 3. Mass

A postfix on the private `InertMass.ComputeTotalAsmb(Part, in ScaleFactors, out int)` (`InertMass.cs:16-32`),
where every recomputation of a part's inert mass starts from zero — at construction, rescale, clone and
`RecomputeInertMass` — rather than on `SetScale`, which is small enough to be inlined past a patch. It scales the
plate's template mass properties, centre and inertia included, by `t·ρ / (t₀·ρ₀)` — the setting's thickness and
density over the default's — as `StoreMass` scales a launcher's (`Ksa/StoreMass.cs:77-78`). The scale is
already in the template's terms there, so nothing else multiplies by it.

- **On `FullPart` alone**: `ComputeSubtreeInertMass` sums every sub-part (`Part.cs:1324-1333`), so a factor also
  written on a sub-part counts twice.
- **A plate is never a launcher**, which the audit refuses, so `StoreMass` and this never write one part.
- **KSA's crash tolerance follows** (`Part.CrashTolerancePascals`, `Part.cs:866`), since it is derived from this
  mass; steel plates sit at the 72 MPa ceiling either way, an aluminium one moves with its thickness.

### 4. The part window

A postfix on the public `Part.DrawPartInfo()` (`Part.cs:3473`): the editor calls it last inside its part window
(`VehicleEditor.cs:6805`), flight inside the details window `Part.OnDrawUi` opens (`Part.cs:3396`).

- **Editable** while `Program.Editor` is open on a craft not launched (`ExistingVehicle?.HasLaunched`, as the
  crew window checks at `VehicleEditor.cs:6783`); read-only otherwise.
- **A change** walks `part.SymmetryLink.Value.Symmetries[].Parts` itself, since KSA's own helper walks modules,
  recomputes each part's inert mass, and sets `PartTree.HasUnsavedChanges` (`PartTree.cs:112`).
- **The deepest crater** needs a query `WallCraters` does not have yet; it answers only at a point.
- **No `check-tunables.py` change**: the value lives on a part, not on a settings type.

### 5. The damage law reads the setting

- **`KsaWorld.ArmourLayerOf`** resolves through `Sim/ArmourSetting.cs` — the setting, else the definition's
  default; an unknown material, a switched-off Armour family or a value outside the limits fall back to the
  default — keyed on `FullPart`, the cache kept and invalidated by `PartArmour`'s setter. Every collect asks it of
  every part, and uncached it is two scans of the catalogue a part.
- **The thickness is scaled by the editor's scale of `FullPart`** (`KsaWorld.ScaleOf(part.FullPart)`), never by a
  sub-part's `ScaleTotal`, which includes the scale the mesh was authored at; the sub-part's belongs only to
  positions in its frame.
- **A plate's toughness**: `PartToughness.Joules(in DamageablePart)` as the bare part's
  `Joules(m_bare, tolerance)` plus `k · m_layer`, the layer's own mass, with no tolerance term on the layer, since
  KSA's derived tolerance moves with the thickness. For a plate `m_bare` is nothing; `k` keeps the 25 mm test
  plate at its present 217 kJ, fixed in `Sim/` after step 6 has changed its collider. The four sites that ask
  for toughness use it: `BlastDamage.Costs`, `StrikeInert`, `StrikesSkin` and `GoesThrough`'s crossings. A bare
  part's toughness does not change, and `DamageCostTests` keeps pinning it.

### 6. The test plate

Its 617 kg is a 2 m disc's, and its collider and mass box a 2 m square. The collider becomes a cylinder 25 mm
thick rotated onto its face and the mass box with it, keeping the template's mass at the default's, so the drag
box, KSA's coming aero mesh and the derived tolerance see the plate the player sees. A save holding it with no
element loads at 25 mm steel and today's mass. Its name drops the thickness.

### 7. Docs and checks

`docs/BLOCKED-ON-KSA.md` (per-part data is possible, and how), `docs/ARMOUR-PLAN.md` (a part per shape; the
"11 t to shrug off the 5"/54" becomes about 4.2 t under the linear toughness), `docs/DAMAGE-PLAN.md`
(the toughness of a layer), `CLAUDE.md` (eighteen patches where it says thirteen, the layout rows, `Ksa/PartArmour.cs`,
`Sim/ArmourSetting.cs`), `README.md` and the wiki's armour pages (no fixed thickness), `docs/WEAPON-PACKS.md`,
`docs/PACK-API-SURFACE.md` regenerated, `docs/KSA-API-SURFACE.md` with the four public members pinned, and
`CHECKLIST.md` with step 1's checks as lines with expected values — e.g. "a plate set to 40 mm titanium, symmetry
x4: all four read 40 mm titanium and the craft X t after library save, reload, launch, universe save and load".

**`Sim/` tests**: `ArmourSettingTests` (resolve, limits, unknown material kept, the synonym and its conflict), the
mass factor, the layered toughness (linear in thickness; a bare part unchanged; the test plate at 217 kJ),
`PackReader`'s new faults.

## What it costs

- **Five patches**, the mod from thirteen to eighteen: `PartTree.Serialize`, `PartTree.Deserialize`, the `Part`
  copy constructor and `Part.DrawPartInfo`, all public and pinned through `docs/KSA-API-SURFACE.md`, so a change
  to any is a build error; and `InertMass.ComputeTotalAsmb`, private, which degrades: a plate at its default mass.
- **Startup**: two serializer round trips, measured by step 1k.
- **Frame time**: a cached lookup per part per collect, as today.

## Who can open a craft

A craft carrying a plate needs KSArmory installed, since a plate is its own part, and a KSArmory with this
feature for its setting: an older one loads every plate at its default and the next save drops the setting. A
craft shared through Borea declares that version as its minimum. Borea's preview reads only part names and
transforms, skips every other element and shows no mass, so a plate previews as its shape.

## The fallback, if step 1 fails

Variant parts, generated **at build time** from a table of thicknesses into a listed asset file — never when the
game starts, because Borea reads a mod's shipped files and a variant that exists only in a running game is a
placeholder box there. All but one per shape `Hidden`; the part window's thickness swaps the part by serializing
the tree, changing the `InstanceOf` of the part and its symmetry siblings, and deserializing it, the next frame;
variants of one shape share their connectors in order. Only one of the two routes ships: if step 1 passes, the
fallback is never built.

## Left for its own plan

**A layer on any part** — a cabin, a tank. Real rockets carry no monolithic armour; spacecraft use spaced
bumpers and aircraft armour the crew alone. If it is wanted, the reviews found what it needs beyond this plan:
the layer outside a tank's wall as a second crossing on the same part, with its own craters; `BulletHoles.Strike`
judging layer then wall, and a hole only where the wall gave; the part's own layer in `BlastDamage.Costs`' fragment
path; a plate told apart from a part merely carrying a layer in the shadowing; `StoreMass` kept off it; and an
area that collider surfaces do not give for overlapping colliders.

## Risks

- **A serializer built before the override**, by another mod touching `GameSaves` or `VehicleSaves` first:
  caught by step 1's round trip, and the feature stays off.
- **A save made while the feature is off** resets the plates it carries to their default; the banner and the
  backup in `saves/<save>/KSArmory/` cover it.
- **Renaming the element** loses every saved value, so `KSArmoryArmour` is permanent once shipped.
- **A KSA update** renaming a pinned member is a build error; the private one degrades.
