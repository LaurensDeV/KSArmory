# Extensibility: a weapon pack is a mod, and this mod never learns it exists

BDArmory's answer is a part `.cfg` and a model, and no compiler. That works because KSP has
`PartModule`: a mod names a class in a config and the engine drives it. KSA has no such thing —
`docs/FROM-KSP-MODDING.md` and `docs/BLOCKED-ON-KSA.md` record why — so the mechanism here has to
be different even where the experience is the same.

**No pack depends on KSArmory, and KSArmory knows no pack by name.** A weapon pack is an ordinary
KSA content mod with a `KSArmory/` folder in it; KSArmory reads that folder inside every installed
mod and registers what it finds. The convention is the whole mechanism — the same relationship KSA
itself has with its mods folder.

**This reverses an earlier decision, and the reasoning is worth keeping.** The first design had
packs *push*: ship an assembly, depend on KSArmory through StarMap, call `Armoury.Register`. That
works, is still supported, and is what anything building definitions at runtime should do. What it
cost was a DLL for a mod that only wanted to declare data — a compiler, an SDK and a reference to
this mod's assembly, to hand over a string. Scanning by convention costs seven KSA bindings and
removes all of it, and it turns out to *improve* the thing push was meant to protect: KSArmory can
now report a pack that is installed but disabled, which under push-only was invisible by
construction.

**Cite symbols here, never file and line**, per `docs/MODULARITY.md`.

**Changes 0, 1, 2 and 2b have landed and a pack has registered in game; 5 has landed for one
weapon; 3, 4 and 6 are still a plan.** `docs/WEAPON-PACKS.md` is the author-facing contract; this
file is why it looks the way it does.

---

## The claim, and the one place it is false

The design note is that a weapon system is *data plus art*. Read against the code that is true of
the **content** and false only of the **delivery**:

- No part Id appears anywhere outside `Arsenal.cs` and the definitions file the mod reads at load.
  Every occurrence of "Pantsir", "Phalanx", "Sidewinder", "HARM", "B61" or "Litening" in `Sim/` and
  `Ksa/` is inside a comment. Nothing in fire control, guidance, the drives, the sight or the panel
  names a weapon.
- Discovery is `Catalogue.LauncherForPart` against a part Id, and `Catalogue.OpticForPart` for
  directors. Tube count, magazine depth, articulation markers, boresight, gimbal, guidance, fuse,
  warhead, medium and buoyancy are all profile members.
- The lookups already take the registry as an argument — `LauncherForPart(from, …)`,
  `LoadoutFor(launcher, munitions, sensors)`, `Named<T>` — written as `internal` overloads so
  `WeaponSystemSelectionTests` can run them against synthetic registries where "picked the right
  one" and "picked the only one" differ.

What was missing is that `Arsenal.Launchers` and its four siblings were **C# collection literals
compiled into the assembly**, with no path by which anything else could contribute — so a weapon
was data plus art plus a fork plus a .NET 10 SDK plus KSA's copyrighted assemblies, which is a
different offer entirely. `Sim/Catalogue.cs` is now what the mod reads, and `Arsenal` is one
contributor to it.

---

## StarMap already does the hard half

This is the finding that makes the inversion cheap, and it is not in
`docs/KSA-MODDING-NOTES.md`.

A StarMap mod's `mod.toml` may declare `ModDependencies`, `ExportedAssemblies` and
`ImportedAssemblies`. `RuntimeMod.AllDependenciesLoaded` holds a mod out of initialisation until
its dependencies are up, parking it in a waiting graph; `CheckForDependentMods` releases waiters as
each dependency lands; `ModLoader.TryLoadWaitingMods` iterates to a fixed point and finally loads
anything whose remaining dependencies are all `Optional`. `ModAssemblyLoadContext.Load` then
resolves a dependency's exported assemblies out of *its* load context before falling back to the
mod's own resolver — so the type-identity problem that separate contexts create is precisely the
problem those fields exist to solve.

And the defaults are the ones we want. `RuntimeMod.CalculateUseableAssemblies` shares the
dependency's **`EntryAssembly`** when neither side names anything. KSArmory's entry assembly is
`KSArmory`.

**So KSArmory's own `mod.toml` does not change.** A pack declares one line:

```toml
[StarMap]
EntryAssembly = "MyWeaponPack"
ModDependencies = [ { ModId = "KSArmory" } ]
```

and gets, for free: initialisation strictly after KSArmory, `KSArmory.dll`'s types shared from
KSArmory's load context rather than duplicated, and a clean skip with a console line if KSArmory is
absent. Nothing on this side had to be arranged.

---

## What a pack is

An ordinary mod folder under `mods/`, registered in `manifest.toml` like any other, containing:

| | |
| --- | --- |
| `mod.toml` | its own — `assets` for KSA, and `[StarMap]` with the dependency above only if it ships code |
| `<Pack>Assets.xml`, `<Pack>GameData.xml` | parts, subparts, colliders, mass — listed in its own `assets`, loaded by KSA, nothing to do with us |
| `Meshes/`, `Textures/` | its own atlas and maps |
| `KSArmory/*.xml` | its weapon definitions, which we read out of that folder. **Not** listed in `assets`, or KSA hands them to its own asset loader |
| `MyWeaponPack.dll` | optional: the entry point, **about ten lines**, for a pack that has to compute rather than declare |

The whole entry point, for the packs that want one:

```csharp
[StarMapMod]
public sealed class MyWeaponPack
{
    [StarMapBeforeMain]
    public void OnLoad()
    {
        string here = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        Armoury.Register(File.ReadAllText(Path.Combine(here, "Weapons.xml")), "MyWeaponPack");
    }
}
```

### The registration surface

```csharp
namespace KSArmory;

/// What a mod depending on KSArmory calls. Public because they call it; nothing inside
/// KSArmory does.
public static class Armoury
{
    public static int Schema { get; }
    public static bool IsOpen { get; }
    public static PackResult Register(string definitions, string source);
}
```

`source` is the pack's own name — it supplies it, we never look it up. It qualifies the pack's keys
and attributes every diagnostic, which is the whole of what this mod knows about who is calling.

**`Register` takes a string and returns a result.** Both halves matter:

- **A string signature means a pack needs no KSA assemblies to build.** This is the property worth
  protecting above all others. `LauncherProfile.Tubes` is `Tube[]` over `double3` from
  `Brutal.Core.Numerics.dll`, so a pack constructing profiles in C# inherits KSArmory's own
  licensing friction — the assemblies are RocketWerkz's, must not be redistributed, and getting
  them is the step `tools/doctor.sh` exists for. A pack that passes text references `KSArmory.dll`
  and `StarMap.API.dll` and nothing else, and `StarMap.API.dll` ships with the loader. **A weapon
  pack is then buildable by anyone with a .NET SDK.** A typed overload can exist for authors who
  want it and should not be the documented path.
- **A result rather than an exception**, because `Register` is called from inside another mod's
  StarMap hook and throwing there is at best rude. The result carries what was accepted and a
  diagnostic per rejection; KSArmory logs it as well, because a pack is free to ignore a return
  value and the failure must not be silent either way.

### Registration is open until it is frozen, and the freeze does the second half of validation

A pack may register at any point before KSArmory's `[StarMapAllModsLoaded]`, which builds the
roster. `[StarMapBeforeMain]` is the hook to recommend: `RuntimeMod.InitializeMod` invokes it
inline, gated by `AllDependenciesLoaded`, so a pack's runs strictly after ours and long before the
freeze.

That timing has a consequence worth designing around rather than working around. `BeforeMain` runs
before `StarMapCore` has even applied its Harmony patches, so KSA has not loaded any asset bundle
yet and **no part exists to check against**. Validation therefore splits in two, and both halves are
worth having:

| | When | What it can answer |
| --- | --- | --- |
| **shape** | at `Register` | parses, required attributes present, enum names known, every munition and sensor key resolves |
| **existence** | at the freeze | the part Id is a declared part, markers name subparts of it, the body count matches the tube count |

The existence half is the runtime equivalent of `validate-parts.py`'s registered-part-Id check —
which is exactly the check whose absence is otherwise a part that loads, appears in the editor, and
is not a weapon, with nothing in any log. It has to ask through `ModLibrary.Get<T>`, which throws:
`Has<T>` and `TryGet<T>` dispatch on the type argument through a branch chain with no
`PartTemplate` case and fall through to `false`, so they answer "no such part" for every Id in the
game including Core's. `Ksa/DeclaredParts.cs` is that seam, and `Sim/IPartCatalogue.cs` is what
keeps the rest of the audit answerable with no game.

Registering after the freeze is refused rather than accepted, and says so. A registry mutated while
systems are crewed is a magazine resized under a launcher mid-salvo.

### What the scan can and cannot report

**A pack whose mod is switched off is reported**, which under push-only was invisible by
construction. `PackScan.Of` separates "nothing to read" from "a pack, and its mod is disabled", and
the second is a warning naming the mod. That is the case worth catching, because **a newly
dropped-in mod arrives switched off**: `ModEntry(id, count)` sets `Enabled = false`, so KSA writes a
discovered folder into the manifest disabled and the player sees nothing. `tools/deploy.sh` writes
the entry itself for exactly this reason, and without the warning it is the first support question
anyone gets.

What is still invisible is a mod the manifest does not list at all, and a pack that ships an
assembly whose `Register` never ran. StarMap's own console output is where a mod that failed to load
appears, and that is the right place for it — it is StarMap's business, not ours. What the Content
window must be unambiguous about is that it lists **registrations, not installations**, so nobody
reads an empty list as "no pack is installed".

---

## The changes, in the order they should land

### 0. The registry becomes an object — nothing external involved

`Arsenal` stays what it is: the built-in catalogue of hand-written C# with the reasoning attached,
six launchers and six rounds of it now that change 5 has taken the Pantsir out. A new
`Sim/Catalogue.cs` is the *resolved* registry — built-ins, plus whatever registered — and every
consumer moves from `Arsenal.Launchers` to `Catalogue.Launchers`. There are 21 such sites across
`Sim/`, `Ksa/` and `Ksa/Ui/`, and most are one identifier.

Two hazards, both found by reading rather than guessed:

**`WeaponSystem` initialised three fields from `Arsenal.Launchers[0]`** — `Profile`, `Munition` and
`Sensor`, before any part is recognised. Safe against a compiled literal, unsafe against a registry
populated at runtime: an empty catalogue turns those into a type-initialiser crash rather than a
degraded mode. The fix is not to sequence the load carefully; it is to **stop indexing element zero
at all**. Those fields want to be unset until a launcher is adopted, which is what
`WeaponSystem.SampleWorld` already does the moment a part resolves.

**`Arsenal.Named` returns `from[0]` on an unknown key.** A defensible answer to a typo in a file the
author owns — its docstring says so, `docs/MODULARITY.md` lists it as the one open item, and
`ArsenalTests.UnknownNamesFallBackRatherThanThrow` pins it. It is the wrong answer the moment
strangers write the keys: a bomb rack naming a round that does not exist becomes a 20 kg
command-linked SAM under a 36 km search radar, in flight, with nothing in any log.

So **resolve every reference at registration and reject the definition that names something
missing**. Nothing a pack ships then reaches the fallback, because nothing unresolved was ever
registered — which is why `UnknownNamesFallBackRatherThanThrow` could be kept rather than changed:
it now guards only a typo in a profile this repository owns, which is what it was always for.

Ships alone, changes no behaviour, covered by the existing suite. `refactor(sim)`.

### 1. The reader, in `Sim/`, with no file access and no KSA types

`Sim/PackReader.cs` takes **text and a source name** and answers the profiles it accepted plus a
diagnostic per thing it rejected. No `System.IO`, no KSA types, so the test project links it
wholesale and the entire failure surface is testable with no game, no Blender and no Windows box.

That is where this system earns its correctness. A loader's interesting behaviour is almost all in
what it refuses and what it says about it, and every one of those cases is a headless test.

**Format: XML.** A pack author is already writing `<Part>` and `<SubPart>` XML in the same folder,
so the definitions are one more file in a syntax they have open; `tools/validate-parts.py` already
parses part XML with `ElementTree`, so the validator gains no second parser — see change 3, which is
the real prize; and comments are free, which matters in a repository whose own rule is that a
measured number without its reason is noise.

Flat attributes, nested elements only where they carry structure:

```xml
<WeaponPack Schema="1">
  <Munition Name="AIM-9X" DisplayName="AIM-9X Sidewinder II" BodyMarker="Aim9x"
            Guidance="Seeker" NavConstant="4" SeekerFovDeg="90"
            LaunchSpeed="25" BoostSeconds="2.0" BoostAccel="480" MaxFlightSeconds="60"
            DragK="2.4e-5" MinRange="300" MaxRange="18000"
            ChargeKg="9.4" FuseRadius="12" FuseArmSeconds="0.5" />

  <Sensor Name="Seeker9X" DisplayName="AIM-9X seeker" Range="18000" ConeDeg="90"
          BoresightSource="PartForward" LockSeconds="1.0" />

  <Launcher PartId="MyPack_Prefab_Lau7x" DisplayName="LAU-7 rail (AIM-9X)"
            Munition="AIM-9X" Sensor="Seeker9X"
            EjectAwayFromMount="1.5" ReloadSeconds="0">
    <Tube Position="0, 0, 0.9" Direction="1, 0, 0" />
  </Launcher>
</WeaponPack>
```

**An absent attribute means the profile's own default**, which is how `Arsenal.cs` already reads —
every entry there states what differs and nothing else. That is also what makes the schema
additive: a field added to `MunitionProfile` next year is absent from every existing pack and
behaves as it did before it existed, which is the rule `SensorProfile`'s discrimination fields
already obey.

**The component entry is derived, not written.** CLAUDE.md's own instructions warn that a launcher
missing from `Arsenal.Components` "loads, resolves its tubes, matches `LauncherForPart` and is then
completely invisible", and `ArsenalTests` has two tests holding the registries in agreement. Handing
that trap to strangers would be indefensible. A `<Launcher>` mints its own `ComponentProfile` with a
`FireControl` row, a `Sensor` row, and a `Gun` row where it has a belt; a `<Provides>` child stays
available for gear the launcher carries as a subpart, which no reader could infer. **A failure that
can be made unrepresentable should not be made testable instead** — the preference `TubeVisual`
already records.

**Names are qualified across packs and bare within one.** `Munition="AIM-9X"` means *this pack's*
round; `Munition="KSArmory:30MM"` means somebody else's. Two things fall out and both are worth
having: a pack can never silently capture a built-in by naming a round the same thing, and a pack
*can* deliberately reuse one — a new gun mount firing KSArmory's 20 mm shell is then a launcher and
no munition at all. The built-ins are the one namespace that keeps bare keys, because they are what
every qualified reference is resolved against: `KSArmory:` is stripped on the way in rather than
added on the way out, which is also what let this mod's own weapons move into a definitions file
without renaming a key any saved setting already holds.

Part Ids stay one flat space, because KSA's already is: `SerializedCollection.Register` is
first-registrant-wins and silent, so two mods claiming an Id is already a problem with an existing
shape. A duplicate reaching us is rejected and reported.

### 2. `Armoury`, and the freeze

The public surface above, the merge behind it, and the existence checks at
`[StarMapAllModsLoaded]`. Small, because everything difficult is on the other side of the `Sim/`
seam and everything about discovery no longer exists.

**Rejection is per definition — never per file, and never per pack.** One malformed round should not
take out the other five in the same file, and a pack of a dozen weapons should not be lost to one
bad attribute.

**A pack declaring a schema newer than the runtime is refused whole, with the version it needs.**
Partially reading a file written against a contract we do not have is how a weapon flies with half
its fields defaulted. Same shape as the feedback endpoint answering 426 rather than guessing.

**And `Armoury` is now a versioned public API with consumers outside this repository**, which
nothing here has had before. `tools/api-surface.sh` records the KSA API this mod binds to; the
mirror of it — recording the KSArmory API packs bind to, and failing when it moves — is the same
tool pointed the other way, and it is what stops a `refactor` from quietly breaking every pack.
Worth building with change 2 rather than after the first breakage.

### 3. The validator stops parsing C#

The largest payoff, and invisible from the outside.

`tools/validate-parts.py` opens `src/KSArmory/Sim/Arsenal.cs` **as text, in twelve places**, and
recovers the registry by regex: `Launchers = [...]` split on commas, profile bodies matched as
`X = new() { … };`, scalars with the `f` suffix required in some fields and forbidden in others,
vectors required to be positional `new(x, y, z)`. Any expression — a named constant, a
`float.DegreesToRadians(…)` — silently fails to match and is reported as a missing field. Three
hand-written tables plus `OPTIC_GEOMETRY` are keyed on **C# field names in this repository**, and an
unlisted launcher is a hard failure rather than a skip.

Half of it now runs against a bridge rather than against data: a `<Launcher>` in
`KSArmory/Weapons.xml` is rendered back into the C# initialiser shape those regexes already expect,
so a weapon moving out of `Arsenal.cs` costs no coverage and buys none either. The gates read the
XML directly when the last one has moved, and the bridge goes.

Move the definitions into data and the validator reads *the same file the game reads*. Give it
`--mod-root <dir>` and it becomes a tool a pack author runs against their own folder, gating what is
otherwise silent in game: a mesh Id absent from the atlas, a texture path whose case is wrong, a
marker matching two subparts or none, a tube seat disagreeing with the mesh.

The generated half stays here. `muzzles.json` is an artefact of `tools/model/pantsir.py` and means
nothing to a pack; the path that generalises is the **authored-launcher** one, which reads mesh
bounds and the XML seat and needs no generator. `tools/model/checkswept.py` has the same shape —
`vehicles()` hard-codes two entries by C# field name — and the same answer, lower value, can wait.

### 4. Nothing registered silently

Every link in KSA's asset chain fails without a word, which is why `KSArmoryMod` already logs the
particle chain on startup: *"without a record the only symptom is a kitten with no gun — and that
looks identical whether the XML never loaded, a reference did not resolve, or the mesh did."*

A **Content** window off the panel: every registration, the pack that made it, whether it survived
the freeze, and the reason if not. Per CLAUDE.md's placement rule it belongs to the session rather
than to any system — one install, one answer, whatever craft is being flown — and per the depth rule
it is a **button**, not a tick box, because it opens a window rather than holding a state.

`SerializedId.Mod` is public and the engine sets it on every asset as it loads, so a part can be
attributed to the mod that shipped it without that mod being asked to say so — which is what lets
the window tell "this pack registered a launcher for a part that does not exist" from "…for a part
another mod claimed first".

And, per *What this mod gives up* above, the window says **registrations** and not installations, in
those words. The list being empty means nothing called us.

---

## What a pack can express, and what it cannot

Stated plainly, because the boundary is most of this document's value to whoever reads it next.

**Data — a new instance needs no code beyond the ten-line entry point.** Every flight, warhead,
sensor and optic number; tube positions *and* per-tube directions, so splayed tubes, a VLS and an
MLRS are all expressible; magazine depth, salvo spacing, reload; the cannon belt, its muzzles and
its rate; every articulation marker; part Ids; which round and which sensor a launcher pairs with;
buoyancy and medium, so a torpedo is an ordinary munition with a small `DragK` and a
`NeutralDensityRatio` near 840.

**A choice among kinds that exist.** `GuidanceMode` has five values, `BoresightMode` four,
`GimbalKind` two. A pack names one. It cannot add one — and none of the three is dispatched through
an exhaustive `switch`, so a sixth value would silently take the default path rather than failing to
compile. `ArsenalTests.EveryGuidanceModeIsAccountedFor` asserts the count for exactly that reason.

**Still C#, and honestly so:**

- a new **weapon kind** — a beam, a hitscan, anything with no discrete round. `IProjectile` is a
  thing with a position, a flight and a fuse; `docs/MODULARITY.md` costs the sibling abstraction.
- a new **guidance law**, as opposed to a new set of numbers for an existing one.
- a new **gimbal**, which is five branches in `OpticGeometry` plus the mesh axis and two panel
  labels.
- **articulation topology.** `LauncherProfile` offers five markers on one fixed chain — traverse
  about part +X, elevate about +Z at a trunnion, radar about +X — and `TubeGeometry.ElevatingPose`
  composes exactly two levels. A drum, a translating rail, per-tube motion or a radar that trains
  independently of the turret is code, and `docs/MODULARITY.md` change 4 says why it should not be
  built before something needs it.

That is the same line `docs/MODULARITY.md` reaches from the inside: **modular for rounds, mostly
modular for launchers, not modular for mounts.** None of this moves it. It removes the compiler from
the side that was already data.

### Letting a pack add a *kind* is a later change, and now a smaller one

The four items above are the natural next ask, and the plumbing they would need — a pack shipping
code, ordered after us, sharing our types — is no longer hypothetical: it is the mechanism every
pack already uses from day one. What is missing is only the seam itself: something like
`Armoury.Register(IProjectileKind)` with `ProjectileContractTests` runnable against a stranger's
implementation, which is the part that makes it safe rather than the part that makes it possible.

Still last. The shape of that seam is least knowable before a second implementation exists that
wants it — the pattern `docs/AUDIT-2026-08.md` names, and why `docs/MODULARITY.md` keeps its change
4 in last place. Ship 0–5, watch what packs actually ask for, design against that.

---

## Risks and things this does not fix

- **Recognition matches `Part.Id`, which is the *instance* Id and not the template's.**
  `Part.ResolveRuntimeId` answers the instance name when there is one and the template Id only when
  there is not, so every match in this mod works because nothing names its part instances. A pack
  author who does — or a KSA build that starts naming them — silently stops being recognised, with
  the part present in the editor and no weapon on the craft. `Part.Template` is public and
  `PartTemplate.Id` is the key that actually means "what kind of part is this". Worth fixing while
  the registry is being touched anyway, and worth a test either way.
- **Removing a `<SubPart>` terminates the game** on every save holding that part — KSA bounds the
  pairing loop by the save and indexes the definition. A pack author iterating on their own part
  will hit this, and it presents as KSA dying inside `Popup.DrawAll` with no mention of their mod.
  First warning in `docs/WEAPON-PACKS.md`, and `tools/repair-saves.py` should learn `--mod-root`.
- **Marker resolution is a case-insensitive substring, first hit wins.** `"Missile"` also matches
  `MissileRail`. In this repository that is a convention everyone knows; handed to strangers it is a
  trap, and it is why the *part-level* registry uses exact equality. `PackAudit` at the freeze is
  the mitigation — exactly one subpart matches, or it is named in the log — and it runs over the
  built-ins too.
- **A munition's markers are still silent.** `FinMarker` unresolved is invisible and `BodyMarker`
  null falls back to the literal string `"Missile"`; the audit walks launchers and heads, which are
  the profiles that name a part, and a round names none. Survivable in a repository with a
  validator over every profile; not survivable in a pack. Fixing it is small and belongs with
  change 3.
- **`OpticalHead` ignores `SensorProfile.BoresightSource` and always uses the mount normal**, so
  that field is dead data on a director. A pack author will set it and it will do nothing. Either
  wire it or reject it at registration — either way, not silence.
- **Type identity across load contexts is a real hazard and the string API dodges it.**
  `FeedbackClient` already reads `HttpResponseMessage.StatusCode` by reflection because of exactly
  this. A pack passing text touches none of it; a typed overload puts `LauncherProfile` across the
  boundary, which `ExportedAssemblies` is designed to make safe and which nothing here has yet
  proven in flight.
- **Live tuning writes the shared profile instance by reference**, and nothing persists it. A pack's
  profile behaves identically, which is right, but a player's tuning of a third-party weapon is lost
  on reload exactly as it is for a built-in.
- **`tools/check-tunables.py` cannot see a pack**, and should not: its `TUNABLE` list is literal, its
  receiver identifiers are hard-coded, and a pack cannot add fields to any of the five types it
  guards. Worth stating so nobody wires it up.
- **What has been flown is registration, not use.** `CHECKLIST.md` 1.3 records a pack found,
  registered, audited and listed in the roster, with its part right way up in the editor. Releasing
  one of its stores and driving it from the panel are still unticked.

---

## Prove it by eating it

**Change 5: move one shipped weapon out of `Arsenal.cs` and register it over the same reader a
stranger's pack goes through.** The Pantsir went, not the LAU-7 rail the plan named — the largest
system rather than the smallest, so twelve tubes, a cannon, five articulation markers and a declared
subpart role all had to survive the translation. It is not even registered specially: this mod's own
`KSArmory/Weapons.xml` is found by `InstalledPacks` inside this mod's folder exactly as a stranger's
is inside theirs.

Until a shipped weapon goes through that path, it is exercised only by files written by whoever
wrote the reader. This repository already knows what that is worth: *"a registry test written
against a single launcher passes against it"*, because "picked the right one" and "picked the only
one" are then the same assertion. A format exercised only by its author fails the same way at a
larger scale, and the ways it is wrong are discovered by strangers.

It also settles what the format cannot answer on paper — whether an entry loses anything in
translation. `Arsenal.cs` carries a paragraph per number on which figures are the real weapon's and
which are gameplay, and that is what the move was judged on: `KSArmory/Weapons.xml` carries the same
paragraphs as XML comments, which is most of why the format is XML rather than something terser. The
remaining six launchers have not moved, so **the built-ins stay in C# and packs are XML** — two
spellings of one catalogue — is still a live answer rather than a rejected one.

---

## Order, and what each is worth alone

| # | Change | Size | Shippable alone? |
| --- | --- | --- | --- |
| 0 | ~~`Catalogue` replaces the static registries~~ | medium | **landed** |
| 1 | ~~`PackReader` in `Sim/`: text and a source name in, profiles and diagnostics out~~ | medium | **landed** |
| 2 | ~~`Armoury`: the public surface, the freeze, the existence checks and the API record~~ | small | **landed** |
| 2b | ~~`PackScan` + `InstalledPacks`: read a `KSArmory/` folder inside every mod~~ | small | **landed** — what makes a pack assets-only |
| 3 | `validate-parts.py --mod-root`, plus the silent-marker fixes | medium | yes — improves the built-ins too |
| 4 | The **Content** window, and the pack template repository | small | yes |
| 5 | ~~Register a shipped weapon through the reader instead of compiling it in~~ | small | **landed** — the Pantsir, not the LAU-7 the plan named |
| 6 | Let a pack register a new *kind* | large | **not yet** — see above |

**Both of 2's loose ends have since landed.** `tools/pack-api.py` records the vocabulary and the
entry point and fails the build when either moves — renaming `ChargeKg` compiles, passes every test,
breaks every pack in the wild, and now fails CI. And `Sim/PackAudit.cs` runs at the freeze, which is
the first moment the question can be asked: a pack registers before KSA has loaded a single asset
bundle, so there is no part in the world to check a profile against until then. It covers the
built-ins too, which nothing previously did.

One thing landed that was not in the plan, because writing the tests found it: a launcher is refused
when a name it uses was **taken**, not when it fails to resolve. A refused round leaves its name in
the catalogue carrying somebody else's profile, so checking that the name resolves — the obvious
form, and the one written first — accepts a launcher that flies a weapon its author never shipped.

**What this plan gained back, deliberately:** the manifest walk an earlier draft dropped. It costs
seven members in `docs/KSA-API-SURFACE.md` — `ModLibrary.Manifest`, `ModManifest.Mods`,
`ModEntry.Id`, `ModEntry.Enabled`, `ModLibrary.Find`, `Mod.DirectoryPath` and
`ModLibrary.LocalModsFolderPath` — and buys a pack that is a folder of files rather than a
folder of files plus a compiler. That is the right trade for a mod whose whole claim is that a
weapon is data plus art.

It is a *convention*, not a list: KSArmory looks in the same place inside every mod and never learns
one by name. Both paths are live, and a pack picks — assets only for anything declarative, an
assembly and `Armoury.Register` for anything that has to compute.
