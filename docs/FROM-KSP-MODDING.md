# Coming from KSP modding

If you have made parts or weapons for Kerbal Space Program, most of what you know carries over.
This page maps KSP's modding concepts onto KSA and KSArmory, and covers the three differences that
will trip you up.

## Quick map

| In KSP | In KSA with KSArmory |
| --- | --- |
| `GameData/<Mod>/`, scanned at startup | `mods/<Mod>/` in the KSA user folder, enabled in its `manifest.toml` |
| `.cfg` files in `ConfigNode` format | XML files |
| `PART { }` | `<Part>`, with a `<SubPart>` for each piece that moves |
| `MODULE { name = X }` | nothing (see below) |
| module fields set per part | `<Launcher>`, `<Munition>`, `<Sensor>` and friends, in your mod's `KSArmory/` folder |
| ModuleManager patches (`@`, `%`, `:NEEDS`) | nothing; there is no patch layer |
| `model.mu` | `.glb` mesh atlas |
| `PartResource` and resource flow | nothing; KSArmory counts ammunition itself, and can lighten the part as it fires |
| `KSP.log` | `Logs/KSArmory.log` in the KSA user folder |

## Three differences that matter

### 1. There is no PartModule

KSA does not let a mod attach behaviour to a part. Its per-part update lists are internal, so
"write a class, name it in the part config, let the game call it" is not possible.

KSArmory works the other way round. Your part is plain structure with mass and a collider.
KSArmory looks at every craft, finds parts whose Id matches a weapon definition, and runs the
weapon itself. That is why your weapon file names a `PartId`, and why the Id in your asset files and
the Id in your weapon file must match exactly.

It also means there is no per-part config node to tweak. Every part with the same Id behaves the
same way. Settings that differ between two launchers on one craft (auto-engage, team) are set by
the player in KSArmory's panel.

### 2. Rounds are not vessels

In KSP a missile is a vessel with parts and colliders, moved by the physics engine. In KSArmory a
round is simulated by the mod and drawn using a subpart of the launcher. This makes proximity fuses
accurate at high speed and keeps rounds out of save files.

Consequences for a pack author:

- A round's mesh is a `<SubPart>` on the launcher part, named by the round's `BodyMarker`. A gun
  shell can instead name a `<SubPart>` template with `BodyModel`, so any number can be drawn.
- A round has no collider. Hitting the ground is opt-in: missiles always stop on the ground, other
  rounds only with `HitsTerrain="true"`, which costs a terrain lookup per round per frame. The 20 mm
  and 30 mm cannon rounds leave it off and pass through hills.

### 3. Positions move fast

KSP's origin is effectively fixed. KSA works in a frame centred on the Sun, and Earth moves through
it at about 29.8 km/s. A pack author never deals with this directly, since all positions in a weapon
file are in part space. It matters if you write code against KSA: two positions sampled one frame
apart are about 500 m apart at 60 fps. Read
[FRAMES-AND-EPOCHS.md](FRAMES-AND-EPOCHS.md) before touching rounds, drawing or timing.

## What works the same

- Parts are data that reference meshes and materials by Id. The XML is stricter than `ConfigNode`,
  but the shape is familiar: declare a mesh, a material, then a part that uses them.
- Art and behaviour are separate. Nothing in KSArmory's code names a particular weapon; every
  built-in weapon is defined in an XML file the same way a pack's is.
- A new weapon needs no compiler. It is art plus a definition file.

## Making a weapon

1. **Model it** in Blender and export a `.glb` with one node per moving piece.
2. **Declare the part** in your asset files: a `<SubPart>` for each moving piece (and each round's
   body), a `<Part>`, and a `<PartGameData>` with mass and colliders.
3. **Define the weapon** in your mod's `KSArmory/` folder: a `<Launcher>` on your part Id, naming
   the `<Munition>` and `<Sensor>` it uses.
4. **Check it** with KSArmory's `tools/validate-parts.py --mod-root <your mod>`, then start the game
   and read `KSArmory.log`.

A launcher that does not move is a `<Launcher>` with no `TurretMarker` or `PodsMarker`. KSArmory
then never waits for it to aim.

The [weapon pack reference](WEAPON-PACKS.md) has a complete example and every element and
attribute.

## Changing KSArmory itself

This part is for working on KSArmory's own C# code, not for making a pack.

- [CONTRIBUTING.md](../CONTRIBUTING.md) covers setup. `./tools/doctor.sh` lists anything missing.
- [CLAUDE.md](../CLAUDE.md) sets the commit message format, which decides releases.
- [KSA-MODDING-NOTES.md](KSA-MODDING-NOTES.md) covers calling into KSA, and
  [BLOCKED-ON-KSA.md](BLOCKED-ON-KSA.md) lists what the engine does not allow yet.
- New art is authored in Blender, not generated. `tools/model/pantsir.py` builds three existing
  parts and is not extended.
- Code under `src/KSArmory/Sim/` uses no KSA types, and the test project runs it without the game.
  You still need KSA's assemblies to build the tests, because `double3` comes from
  `Brutal.Numerics.dll`.
- A behaviour fix is not verified until it has been flown in game, and a regression test only
  counts if it fails against the old code.
