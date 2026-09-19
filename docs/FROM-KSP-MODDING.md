# Coming from KSP modding

If you have written parts or weapons for Kerbal Space Program, most of your instincts transfer and
three of them will actively mislead you. This page is the translation, and the three that mislead
are named first.

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
| module fields tuned per part | `LauncherProfile` / `MunitionProfile` / `SensorProfile`, written in `Sim/Arsenal.cs` or declared in a `Weapons.xml` |
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
`Ksa/LauncherPart.cs` looks for it on a vehicle by part Id, and `Ksa/WeaponSystem.cs` runs the
behaviour from the mod's own frame hook. A part is a *thing to find*, not a thing that acts.

The consequence you will feel: **behaviour is not per-part-instance by default.** If you want two
launchers on one craft to behave differently, that is something you build, not something the
engine gives you. `Sim/SystemConfig.cs` and `Ksa/WeaponSystems.cs` are that machinery.

`docs/BLOCKED-ON-KSA.md` lists this and everything else the mod wants and cannot have, with the
engine reason for each. If RocketWerkz ever ship a module system, that file is where the change
starts.

### 2. Rounds are not physics objects

In KSP a missile is a vessel: it has parts, colliders, and the physics engine moves it.

Here a round is a **number integrated by the mod**, drawn as a subpart body. That is deliberate —
see the design notes in `CLAUDE.md` — and it buys sub-frame fuse accuracy and an inability to
corrupt a save. Terrain collision is then something a round opts into rather than something it
gets: `MunitionProfile.HitsTerrain` costs a height-map sample per round per frame, so a bomb is
stopped by the ground and a cannon shell passes through a hill.

So: do not look for a rigidbody, and do not expect `OnCollisionEnter`. Guidance lives in
`Sim/Interceptor.cs`, is pure maths, and is tested headlessly with no game running at all.

### 3. Everything is in a heliocentric frame, and it is moving at 29.8 km/s

KSP gives you one origin that is, for practical purposes, still. KSA does not.

Positions are in the ecliptic frame. Earth is travelling at about **29.8 km/s** through it, so a
one-frame error in *which instant* two positions were sampled at is not a rounding error — it is
**~500 m at 60 fps**. It surfaces as a jitter, a constant offset, a guidance error or a drift,
which are four disguises for one mistake.

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
  It is named in `src/KSArmory/KSArmory/Weapons.xml`, which is the mod's own definitions file and
  is meant to; the weapons still written in C# are in `Sim/Arsenal.cs`, which is the registry.

## Adding a weapon

In KSP this is a `.cfg` and a model, with no compiler. Here it is five steps, and step three is the
only one that can still want a rebuild:

1. **Model it.** Copy `tools/model/pantsir.py`, keep the group and pivot conventions, export into
   the same atlas. `tools/model/checkmesh.py` fails the build on the two defects that are only
   visible in game.
2. **Declare the part.** A `<SubPart>` per moving assembly plus a `<Part>` in
   `KSArmoryAssets.xml`, and a `<PartGameData>` with colliders and mass.
3. **Register it.** One `LauncherProfile` in `Sim/Arsenal.cs`, naming the munition and sensor it
   uses, with the geometry `tools/model/build.sh` prints — or a `<Launcher>` in
   `src/KSArmory/KSArmory/Weapons.xml`, which is the same profile as data and goes through the
   reader a third-party pack uses. Add a `MunitionProfile` if the round differs.
4. **Teach the validator.** `tools/validate-parts.py` compares the profile's geometry against
   `muzzles.json`, and it is scoped per launcher — a new one gets no check until you add it. The
   generator emitting those numbers and the profile holding them are the same numbers in two
   files, and geometry duplicated across a boundary drifts unless something reads it back.
5. **Nothing else.** `LauncherPart.Find` matches every registered part Id and the weapon system
   selects whichever profile it finds. `ArsenalTests` checks the registry hangs together, and
   `validate-parts.py` also checks that every registered `PartId` is declared in the XML.

**A launcher that does not train** is the same profile with `TurretMarker` and `PodsMarker` left
null. The drives are then skipped and `IsLaid` stays true, so fire control cannot deadlock waiting
for something that will never move.

### The step that no longer needs a compiler

`LauncherProfile` and friends are **pure data with no logic in them** — C# object initialisers,
with nothing about them that requires code — so they are also expressible as a file.
`Sim/PackReader.cs` reads them out of XML and `Ksa/InstalledPacks.cs` finds that XML in a
`KSArmory/` folder inside every installed mod, this mod included. A whole weapon system is then art
plus a file, with nothing to compile and no fork of this repository, which is the workflow you are
used to. `docs/WEAPON-PACKS.md` is the author's reference; `docs/EXTENSIBILITY.md` is why it looks
the way it does.

The constraint that shaped it is the one under *What you do not need*: `Sim/` must stay free of KSA
types, so the reader takes a string and the file is found by `Ksa/`.

## Where the rules are written down

| Read this | Before |
| --- | --- |
| `docs/FRAMES-AND-EPOCHS.md` | touching rounds, drawing or timing |
| `docs/KSA-MODDING-NOTES.md` | anything that calls into KSA |
| `docs/BLOCKED-ON-KSA.md` | proposing a feature that needs an engine hook |
| `CLAUDE.md` | committing — the message format is enforced and decides releases |
| `CONTRIBUTING.md` | setting up: `./tools/doctor.sh` tells you what is missing |

Two rules that are not negotiable:

- **A behaviour fix is unverified until it has been flown.** Compiling, passing the suite and
  having a plausible mechanism are not evidence. The hardest bugs here live in the gap between the
  maths and what KSA actually does, and that gap is only visible in flight.
- **A regression test only counts if it fails against the old code.** Check that it does, every
  time. A test that advances the platform by exactly the `v*dt` it passes in cancels its own error
  and passes against the broken implementation too, which looks like proof and is worth nothing.

## What you do not need

You do not need **the game running, Blender, or a Windows box**. Everything under
`src/KSArmory/Sim/` is free of KSA types by construction: the test project links it wholesale and
references no KSA assembly, so a `using KSA;` there fails the test build. Guidance, fuses, threat
modelling, lead solutions, IFF and the drives all live there and fly whole engagements headlessly.

What you *do* still need is **KSA's assemblies**, even for that half: `double3` comes from
`Brutal.Core.Numerics.dll`, so the test project references it and `tools/test.sh` will not run
without it. `./tools/doctor.sh` says what is missing and how to get it.

That is the easiest place to start, and the tests will tell you the truth without the game
installed.
