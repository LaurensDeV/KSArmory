# Coming from KSP modding

If you have written parts or weapons for Kerbal Space Program, most of your instincts transfer and
three of them will actively mislead you. This page is the translation, and it exists so that the
misleading three cost you a paragraph rather than an evening.

It maps *concepts* — `PartModule`, `ConfigNode`, ModuleManager, `GameData` — that anyone who has
modded that game already knows. No mod's source was consulted or copied; these are the parts of
KSP's own API that every part author has used.

## The short version

| In KSP | Here |
| --- | --- |
| `GameData/<Mod>/` scanned at startup | `mods/KSArmory/`, listed in the user directory's `manifest.toml` |
| `.cfg` in `ConfigNode` format | XML — `KSArmoryAssets.xml`, `KSArmoryGameData.xml` |
| `PART { }` | `<Part>` and `<SubPart>` |
| `MODULE { name = X }` binding a `PartModule` | **nothing. See below.** |
| module fields tuned per part | `LauncherProfile` / `MunitionProfile` / `SensorProfile` in `Sim/Arsenal.cs` |
| ModuleManager patching (`@`, `%`, `:NEEDS`) | nothing; there is no patch layer |
| `model.mu` | `.glb`, in a shared mesh atlas |
| `PartResource` / resource flow | nothing; ammunition is counted by the mod |
| KSP's own `Debug.Log` | `Ksa/Log.cs`, writing `Logs/KSArmory.log` |

## The three that will mislead you

### 1. There is no `PartModule`

This is the big one, and everything else in the architecture follows from it.

KSA has **no way for a mod to register a behaviour that the engine then calls on a part.** Its
per-frame update lists are internal and are not reachable without patching the game. So the KSP
reflex — write a class, name it in a config, let the game drive it — has no equivalent.

What this mod does instead: the part is **inert structure with mass and a collider**.
`Ksa/LauncherPart.cs` looks for it on a vehicle by part Id, and `Ksa/DefenceBattery.cs` runs the
behaviour from the mod's own frame hook. A part is a *thing to find*, not a thing that acts.

The consequence you will feel: **behaviour is not per-part-instance by default.** If you want two
launchers on one craft to behave differently, that is something you build, not something the
engine gives you. `Sim/BatteryConfig.cs` and `Ksa/BatteryRoster.cs` are that machinery.

`docs/BLOCKED-ON-KSA.md` lists this and everything else the mod wants and cannot have, with the
engine reason for each. If RocketWerkz ever ship a module system, that file is where the change
starts.

### 2. Rounds are not physics objects

In KSP a missile is a vessel: it has parts, colliders, and the physics engine moves it.

Here a round is a **number integrated by the mod**, drawn as a subpart body. This was deliberate —
see the design notes in `CLAUDE.md` — and it buys sub-frame fuse accuracy and an inability to
corrupt a save. It costs terrain collision, which the mod does not have.

So: do not look for a rigidbody, and do not expect `OnCollisionEnter`. Guidance lives in
`Sim/Interceptor.cs`, is pure maths, and is tested headlessly with no game running at all.

### 3. Everything is in a heliocentric frame, and it is moving at 29.8 km/s

KSP gives you one origin that is, for practical purposes, still. KSA does not.

Positions are in the ecliptic frame. Earth is travelling at about **29.8 km/s** through it, so a
one-frame error in *which instant* two positions were sampled at is not a rounding error — it is
**~500 m at 60 fps**. Every hard bug this mod has had is that mistake in a new disguise.

**Read `docs/FRAMES-AND-EPOCHS.md` before touching rounds, drawing or timing.** It is not general
advice; it is the engine's actual contract, the rules that follow from it, and how to tell the
four failure shapes apart.

## What is the same

- **Parts are declared as data and reference assets by Id.** The XML is stricter than
  `ConfigNode`, but the shape is familiar: declare a mesh, declare a material, declare a part that
  uses them.
- **Art is separate from behaviour.** `tools/model/` generates the vehicle from a Blender script;
  nothing in the simulation names it.
- **A weapon system is data plus art.** No fire-control, guidance or drive code names the Pantsir.
  It is named in `Sim/Arsenal.cs`, which is the registry and is meant to.

## Adding a weapon

In KSP this is a `.cfg` and a model, with no compiler. Here it is four steps, and step three needs
a rebuild:

1. **Model it.** Copy `tools/model/pantsir.py`, keep the group and pivot conventions, export into
   the same atlas. `tools/model/checkmesh.py` fails the build on the two defects that are only
   visible in game.
2. **Declare the part.** A `<SubPart>` per moving assembly plus a `<Part>` in
   `KSArmoryAssets.xml`, and a `<PartGameData>` with colliders and mass.
3. **Register it.** One `LauncherProfile` in `Sim/Arsenal.cs`, naming the munition and sensor it
   uses, with the geometry `tools/model/build.sh` prints. Add a `MunitionProfile` if the round
   differs.
4. **Nothing else.** `LauncherPart.Find` matches every registered part Id and the battery selects
   whichever profile it finds. `ArsenalTests` checks the registry hangs together;
   `tools/validate-parts.py` checks the geometry still matches the mesh.

**A launcher that does not train** is the same profile with `TurretMarker` and `PodsMarker` left
null. The drives are then skipped and `IsLaid` stays true, so fire control cannot deadlock waiting
for something that will never move.

### The step that should not need a compiler

Step 3 is the one that will annoy you, and rightly. `LauncherProfile` and friends are **pure data
with no logic in them** — they are C# object initialisers only because that is where they started,
not because anything requires it. Loading them from XML alongside the part definitions would make
a weapon a file rather than a rebuild, which is the workflow you are used to.

That is a genuinely good first contribution and the shape is already pinned: `ArsenalTests`
describes the invariants the registry must keep, and `tools/validate-parts.py` already parses the
part XML, so it would check the profiles in the same pass. The constraint to respect is that
`Sim/` must stay free of KSA types — the loader takes a stream or a string, and the file is found
by `Ksa/`.

## Where the rules are written down

| Read this | Before |
| --- | --- |
| `docs/FRAMES-AND-EPOCHS.md` | touching rounds, drawing or timing |
| `docs/KSA-MODDING-NOTES.md` | anything that calls into KSA |
| `docs/BLOCKED-ON-KSA.md` | proposing a feature that needs an engine hook |
| `CLAUDE.md` | committing — the message format is enforced and decides releases |
| `CONTRIBUTING.md` | setting up: `./tools/doctor.sh` tells you what is missing |

Two rules that are not negotiable, both because breaking them has already cost real time:

- **A behaviour fix is unverified until it has been flown.** Compiling, passing the suite and
  having a plausible mechanism are not evidence. The hardest bugs here live in the gap between the
  maths and what KSA actually does, and that gap is only visible in flight.
- **A regression test only counts if it fails against the old code.** Check that it does, every
  time. One written for the round-body zigzag passed against both implementations, which looked
  like proof and was worth nothing.

## What you do not need

You can do a great deal of useful work with **nothing but the .NET SDK** — no game, no assemblies,
no Blender. Everything under `src/KSArmory/Sim/` is free of KSA types by construction: the test
project links it wholesale and references no KSA assembly, so a `using KSA;` there fails the test
build. Guidance, fuses, threat modelling, lead solutions, IFF and the drives all live there and fly
whole engagements headlessly.

That is the easiest place to start, and the tests will tell you the truth without the game
installed.
