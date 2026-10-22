# KSArmory

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="branding/logo.png">
  <img src="branding/logo-light.png" alt="KSArmory: weapon systems for Kitten Space Agency" width="620">
</picture>

Weapon systems for **[Kitten Space Agency](https://ahwoo.com/app/100000/kitten-space-agency)**
— sensors, fire control, guided and unguided rounds, and the parts to mount them on.

The mod provides the machinery a weapon needs and keeps it separate from any particular weapon:
a search sensor with a threat model, fire control that decides when to commit, articulated mounts
that traverse and elevate, projectiles flown to sub-frame accuracy, and an operator's panel.
**A weapon system is then a registry entry plus its art** — nothing in the simulation or the game
binding names a specific vehicle.

What that machinery gives any system built on it:

- **Search sensor** with a steerable range and cone, lock-on dwell, and manual designation
- **Threat classification by closest point of approach**, so a target merely *passing by* is
  engaged rather than only one closing head-on
- **Identification friend-or-foe** with teams, allies, neutrals and an engage-unknown policy
- **Guided rounds** — proportional navigation, so they *lead* a crossing target instead of
  chasing it — and **unguided ones**, with a ballistic lead solved by the mount
- **Proximity and contact fuses**, solved analytically within each sub-step so nothing tunnels
  through the trigger radius between frames
- **Articulated mounts** — traverse, elevation, independent pointing heads, with travel limits
  and interlocks against the vehicle's own bodywork
- Full ImGui panel, and in-world drawing of the search volume, tracks and rounds

## What ships with it

KSArmory adds nine weapons, three sights, an infrared search sensor and two decoy dispensers.

**Pantsir-S1 Point Defence System.** A Russian short-range air-defence vehicle on an 8×8 truck chassis. It carries twelve 57E6 missiles in two pods of six, two twin-barrel 2A38M 30 mm cannon, a spinning 1RS1 search radar and a 1TPP1 electro-optical director on the turret roof that you can look through. The missiles reach from 1.2 km to 20 km. They are command-guided: the vehicle tracks the target and steers each missile, so a missile is lost if the vehicle loses the track. The cannon cover 200 m to 4 km. The pods refill 12 seconds after the last missile leaves, and the cannon 20 seconds after they run dry. The Pantsir stacks on a 3 m node and has its own command module, so a craft made of nothing but the Pantsir can be built and launched.

**LAU-7 Sidewinder rail.** A single launch rail carrying one AIM-9J Sidewinder, an infrared-guided air-to-air missile. It attaches to any surface. The rail does not move, so you aim it by pointing the craft. The missile has its own heat-seeking head and keeps guiding after launch even if your craft loses the target. Range 400 m to 14 km. The rail does not reload on its own.

**LAU-128 AMRAAM rail.** A single launch rail carrying one AIM-120C-7 AMRAAM, a radar-guided air-to-air missile. It attaches to any surface and is aimed by pointing the craft. The motor burns for a few seconds and takes the missile to about Mach 4, then it coasts. Range 900 m to 105 km. At long range it arrives slow and turns poorly, so close shots are more reliable. The rail does not reload on its own.

**LAU-118 HARM rail.** A single launch rail carrying one AGM-88 HARM, an anti-radiation missile. It homes on radar transmissions, so it can only engage a target whose radar is switched on. It cannot be used against aircraft. If the target radar goes quiet before launch, there is nothing to lock. A missile already in the air flies on to where the signal was last heard. A boost motor and a sustainer keep it fast over its full range of 1.2 km to 148 km. It attaches to any surface and is aimed by pointing the craft.

**Mk 15 Phalanx CIWS.** An American close-in weapon system: a six-barrel 20 mm rotary cannon with its own search and track radar. It stacks on a 3 m node. It holds 1,550 rounds, fires 4,500 a minute, and is effective to 1,486 m. The barrels elevate from -25° to +85°, and the radome moves with them so the radar stays lined up with the guns.

**5"/54 Mk 42.** An American five-inch naval gun in an enclosed mount, aimed by a Mk 68 fire-control radar. It stacks on a 3 m node. It holds 599 rounds, fires 40 a minute and reaches 15.7 km, elevating from -15° to +85°. The shells have a timed fuze and burst in the air at the predicted meeting point, so they do not need to hit the target directly. Each press of the trigger fires one shell. The mount, its shell and their textures were made for this mod by **Mallikas**.

**M197 chin turret.** A three-barrel 20 mm rotary cannon in a turret that hangs under an aircraft's nose. It attaches to a surface. It holds 750 rounds and fires 730 a minute, with an effective range of about 1.5 km. The turret traverses 110° either side of straight ahead and depresses to 50°. It is aimed through a telescopic sight. Model and textures by **Mallikas**.

**B61 bomb rack.** An ejector rack carrying one B61-12 nuclear bomb. It attaches to the underside of an aircraft. The bomb has a guided tail kit: designate a point on the ground and it steers onto it as it falls, anywhere inside the reach ring the sight draws. Without a designation it falls like an ordinary bomb. The bombsight draws a ring on the ground where the bomb will land if you release now. The yield is set at the lowest real setting, 0.3 kt, and can be raised to 340 kt.

**Heavy MIRV Bus.** The top stage of an ICBM: a post-boost vehicle carrying seven Mk 21 reentry vehicles. It stacks on a 3 m node and adds a ballistic computer to the craft. You choose the targets. The computer flies the whole rocket, cuts the engines at the right moment, corrects the bus's path with its own thrusters, and releases the warheads one at a time. See [`docs/ICBM-GUIDANCE.md`](docs/ICBM-GUIDANCE.md).

Three sights look at targets and let you watch through them:

**EO Director.** An electro-optical sight on a short mast. It does not search for targets. It tracks what it is given: a target you shift-click, a contact from a search sensor on the same craft, or a laser spot. You can watch through it in a camera window or the main view, and it needs no weapon on the craft, so a hull carrying only a director works as an observation post. The Pantsir carries the same director on its turret roof.

**Rafael LITENING pod.** An Israeli targeting pod that hangs under an aircraft. The whole nose of the pod rotates about its long axis while the sensor ball tilts inside it, so it can look almost anywhere below and around the aircraft. It sees out to 20 km, zooms to a 1° field of view, and carries a laser rangefinder and designator. The picture stays upright however far the nose has turned.

**YAL-1 nose turret.** The beam director from the Boeing YAL-1 Airborne Laser: a 1.5 m telescope in a rotating nose turret, behind a 1.8 m window. It stacks on a 3 m node. It tracks a target and fires a 1 MW chemical laser at whichever part it is held on. At 100 km a part burns through in about 3 to 5 seconds. One load of chemicals gives about twenty 5-second shots.

**Infrared Search and Track Sensor.** A passive heat sensor that finds targets for the sights. It sends out nothing, so it cannot be detected or homed on. It sees a contact by its heat: a large booster in thin air at about 250 km, and less against the ground. Each sensor covers a 120° cone, so several pointed different ways give all-round coverage. Every search sensor on a craft feeds one shared list of contacts, which the craft's sights can track.

Two parts protect a craft from missiles:

**AN/ALE-47 flare and chaff dispensers.** Two dispenser parts on the same 30-cell body. The flare dispenser holds fifteen MJU-7 flares, and the chaff dispenser holds thirty RR-170 chaff cartridges. Each attaches to any surface and fires out of its face, so you choose the loadout by which dispensers you fit. Flares decoy heat-seeking missiles. Chaff decoys radar-guided missiles, but a Doppler-radar seeker such as the AMRAAM's is only fooled while the target flies across its line of sight. Fire them by hand from the craft's countermeasures row, or let them fire automatically when a missile comes for the craft. See [Countermeasures](#countermeasures).

To add a weapon of your own, see [Adding a weapon system](#adding-a-weapon-system).

You can also add weapons without changing this mod. A *weapon pack* is an ordinary KSA mod with a `KSArmory/` folder of definition files and no code. KSArmory reads that folder in every installed mod. `KSArmory-example-mod` (installed as `KSArmoryExamplePack`) is a complete example: it contains a Mk 82 bomb rack and a working copy of every weapon in KSArmory. `docs/WEAPON-PACKS.md` is the reference.

KSArmory's own weapons are defined the same way, one file per family in its `KSArmory/` folder: `Pantsir.xml`, `Rails.xml`, `Guns.xml`, `Sensors.xml`, `Countermeasures.xml`, `DirectedEnergy.xml` and `Strategic.xml`. If you edited the old `Weapons.xml`, move your changes to the family file that now holds that weapon. You can switch any family off under **KSArmory > Built-in weapons**. The change takes effect the next time the game starts. A switched-off family's parts disappear from the editor but still load on saved craft as plain structure. Its rounds and sensors stay available to packs, and a pack can then define its own weapon on that family's part.

> Built against KSA build `2026.10.19.5638`. KSA is pre-release and has no official code-modding
> API. This mod uses the community [StarMap](https://github.com/StarMapLoader/StarMap) loader and
> may need updating when the game updates. The community
> [wiki](https://kittenspaceagency.wiki.gg/) is a useful reference for the game itself.

## Install

### With Borea (recommended)

[Borea](https://github.com/KSAModding/Borea/releases/latest) is the community mod manager for KSA,
on Windows, Linux and macOS. It installs the StarMap loader for you, registers the mod and
launches the game through the loader, so none of the manual steps below apply.

1. Install Borea from its [releases page](https://github.com/KSAModding/Borea/releases/latest).
2. Open **[KSArmory on Borea](https://ksamodding.github.io/Borea/mod/KSArmory/)** and click
   **Install with Borea**, or search for KSArmory on Borea's Discover page.
3. Start the game **from Borea**. Starting KSA directly bypasses the loader: the parts still
   appear in the editor, but none of the behaviour runs.

Borea also installs updates when a new release appears.

### By hand

You need **Kitten Space Agency**, built against build `2026.10.19.5638`. A different build may
need a rebuild of the mod. **Windows and Linux both work**: the mod is a portable .NET assembly
with no native code, so one release archive covers both. You also need
**[StarMap](https://github.com/StarMapLoader/StarMap/releases)**, the community mod loader. KSA has
no official code-modding API, so nothing here runs without it. Edit its `StarMapConfig.json` to
point at your KSA install. StarMap reads that file **relative to its own directory**, so launch it
from where it lives.

1. **Get the mod.** Download `KSArmory-<version>.zip` from
   [Releases](../../releases), or build it yourself with `./tools/package.sh`.

2. **Unzip it into your mods folder.** That folder lives inside KSA's user directory:

   | Platform | KSA user directory |
   | --- | --- |
   | Windows | `Documents\My Games\Kitten Space Agency\` |
   | Linux | wherever KSA keeps its user data, commonly `~/.local/share/Kitten Space Agency/`; the folder containing `manifest.toml` and `Logs/` is the one you want |
   | Proton / Wine | inside the prefix, at `.../drive_c/users/steamuser/Documents/My Games/Kitten Space Agency/` |

   You should end up with:

   ```
   <KSA user directory>/mods/KSArmory/
     KSArmory.dll
     mod.toml
     KSArmory{Assets,GameData,Particles,Sounds,Characters}.xml
     KSArmory/*.xml               (the built-in weapons, one file per family)
     Meshes/*.glb
     Textures/*.png
     Sounds/*.wav
   ```

   The folder layout matters, and on Linux so does the **case**. `KSArmoryAssets.xml` refers
   to `Meshes/` and `Textures/` by relative path. A case mismatch is silently tolerated on
   Windows and fails on Linux, so unzip rather than retyping the names.

3. **Register it in `manifest.toml`.** This step is required: *dropping the folder in is not
   enough*. Open `manifest.toml` in the same user directory and add:

   ```toml
   [[mods]]
   id = "KSArmory"
   enabled = true
   ```

   KSA discovers mods through that list, and StarMap walks the same list to find code mods.
   Without an entry, nothing loads and nothing tells you why.

4. **Launch through StarMap, not the game directly.** On Windows that is `StarMap.exe`. StarMap
   also ships `StarMap.dll`, a portable .NET assembly, so on Linux `dotnet StarMap.dll` from the
   same folder is the equivalent.

### Check it worked

The mod writes its own log to `Logs/KSArmory.log` under the KSA user directory, and prints
the path it chose to stdout on startup — handy if it ended up somewhere unexpected. **Under Borea
it is currently written to the shared profile in `Documents`, not to the instance's own folder.** The file is
truncated each session. You should see:

```
loading (mod id: KSArmory)
ready - Pantsir-S1, LAU-7 Sidewinder rail, ...
```

The `ready` line arrives roughly **20 seconds after** the first, because the loader waits for
the game to finish loading. That delay is normal, not a hang.

Then: build a craft with the **Pantsir-S1 Point Defence System** (under *Weapons*), launch,
and the **KSArmory** panel appears once you are in flight.

### If it doesn't

| Symptom | Cause |
| --- | --- |
| No `KSArmory.log` at all | StarMap never ran the mod. Under Borea, check the game was started from Borea. By hand, check the `manifest.toml` entry, and that you launched `StarMap.exe`. |
| Part missing from the editor | The asset XML did not load. KSA's own log, the newest `KittenSpaceAgency.*.log` in the same folder, is where XML and asset errors appear. |
| Part is there but nothing happens in flight | The DLL did not load, but the XML did — check `mod.toml`'s `EntryAssembly = "KSArmory"` matches the DLL name. |
| Part renders untextured or invisible | `Meshes/` or `Textures/` did not come across, or the folder layout was flattened. |
| Panel says `no weapons system on this craft` | The part is not on the craft you are flying, or its Id did not match a registered launcher. |
| Works on Windows, part untextured on Linux | A case mismatch in a file or folder name. Linux filesystems are case-sensitive; Windows is not. Re-unzip rather than renaming by hand. |

Developing rather than just playing? `./tools/deploy.sh` builds, installs and registers the mod
in one go — including the `manifest.toml` entry.

## Use

### Fitting a weapon

Every part is under **Weapons** in the editor, but for the EO director, the LITENING pod and the IRST, which are under **Sensors**. The big
mounts stack on a 3 m node: the Pantsir, the Phalanx, the 5"/54, the YAL-1 turret and the MIRV bus.
Everything else attaches to a surface: the missile rails, the B61 rack, the M197 under a nose, the EO
director, the LITENING pod, the IRST and the dispensers. The Pantsir is its own command source, so a
craft that is nothing but a Pantsir builds and launches.

### The panel

Open it from the **KSArmory** menu → **Panel**. The switcher lists every craft carrying something of
this mod's, grouped by team, with a row per weapon; the picker beside **FIRE** chooses which weapon the
trigger and the panel are pointed at. A weapon fitted twice, such as two rails, is one weapon with two
stations, and the trigger steps between them.

A weapon belongs to the craft carrying it, not to the one you are flying, so you can take control of
something else and watch a site defend itself.

### Firing

- **FIRE** shoots the selected weapon. There is nothing to arm first. A missile needs something locked;
  a gun fires where it is laid; a bomb rack lets its store go.
- **Auto engage** lets a weapon pick its own targets and fire. It starts off, so nothing opens fire on
  its own until you say so. The shield in the switcher is auto engage for every weapon on a craft.
- The line beside the trigger says why a weapon is not firing: no lock, out of reach, still slewing, out
  of rounds.

A contact becomes a *threat* when its closest point of approach falls inside **Threat radius** within
**Threat horizon** seconds, and shootable once held for **Lock time**. A launcher commits **Rounds per
target** before it looks again. A turret traverses and elevates onto its target and will not fire until
it has settled.

### Aiming

- **Shift-click** anything, a craft or a place on the ground, to designate it. The turret follows it, the
  trigger shoots at it, and a falling guided store is steered onto it.
- **Fire at the mouse** turns an ordinary click into a shot at what is under the cursor; its own tick
  box arms it.
- **Aim with the mouse** points the turret at the cursor without firing; auto engage still decides
  when to shoot.

A gun over the ground is laid to land where you point, not along the line to it, and the line beside
the trigger says when a place is beyond its reach.

### Sights

The EO director, the LITENING pod, the YAL-1 turret and the Pantsir's roof director are sights. Each
has its own row: **Main view** looks through it with the view you fly from, **Camera** opens it in a
window of its own, which can be dragged onto another monitor, and **Zoom** magnifies. **Track** slews
it onto what the craft's sensors hold, **Mouse** points it at the cursor, **Hand** with sliders, and
**Spot** finds a laser spot on its code. A sight finds nothing by itself: an IRST on the same craft is
what hands it contacts.

### Bombs and the ballistic bus

The B61 rack draws a ring where the bomb will land if released now, flown rather than solved, and a
blue ring of where its fins can still move it once it falls. The MIRV bus is flown by its ballistic
computer, on the **Ballistic** tab: designate the targets, by clicking the world or KSA's ground-track
map, arm it and launch. [`docs/ICBM-GUIDANCE.md`](docs/ICBM-GUIDANCE.md) has how it flies.

### Teams

A craft with no team is fair game. The flag in the switcher puts a craft on a team, and the **Teams**
window sets who each team is allied with, neutral to, and fires on. Every weapon on a team follows its
team's rules.

### KSArmory's own weapons

Every weapon family can be switched off under **KSArmory** → **Built-in weapons**, at the next start.
Its parts still load on a saved craft, as plain structure, and a weapon pack may then define a weapon of
its own on them.

### Tuning a guided round

| Setting | Effect |
| --- | --- |
| **Nav constant N** | 3–5 is the realistic band. Higher leads harder and pulls more g; too high oscillates. |
| **Max lateral g** | The airframe limit. Drop it and fast crossing targets start escaping. |
| **Seeker FOV** | How far off its own nose the round can still see. Narrow means broken locks on hard crossers. |
| **Fuse radius** | Trigger distance. Larger is more forgiving; it does not increase lethality. |
| **Explosive charge** | What actually kills. Lethal and blast radius are both read off it by the cube root, so doubling it multiplies the reach by 1.26. Between the two the target survives. |
| **Gravity compensation** | 1.0 makes guidance ignore the fall. Drop it for lobbed, ballistic-looking shots. |

### Countermeasures

A guided round's seeker is one of three kinds, and that is what decides which decoy can take it:

| Round | Seeker | Flares | Chaff |
| --- | --- | --- | --- |
| AIM-9J | infrared, no counter-countermeasures | usually decoyed | ignored |
| AIM-120C | radar, pulse-Doppler | ignored | only while the target beams |
| AGM-88 | homes on emission | ignored | ignored |
| 57E6 | command link from the launcher | ignored | breaks the Pantsir's track while you beam it — see below |

A decoy is in the running once it outshines the target in the seeker's field: heat falls with the
square of range and a radar return with the fourth power. A radar seeker holds its target in a range
gate, so only chaff within 150 m of the target counts; a heat seeker sees every flare in its field. Each decoy is judged **once**, the first
moment it does, and takes the seeker with an 80% chance less the round's resistance. A seeker that
is taken follows the decoy until it is spent, and its fuse fires on it, so a missile seduced close
to the craft can still hurt it. Then it looks for its target again, or flies on blind.

**Notching the Pantsir.** A 57E6 has nothing aboard to fool, but the set steering it can lose its
track. Turn square to the Pantsir so you are closing on it at under 30 m/s, and drop chaff: a bigger
return beside you with the same Doppler takes the set's gate, the track breaks, and any 57E6 already
in the air loses its uplink and flies on unguided. The set takes three seconds to find you again,
then holds you on its optical channel for as long as you stay in the notch — so it works once per
pass, and turning back towards it ends it. Flares do nothing to it. A sensor's `ChaffNotchMps`,
`ChaffReacquireSeconds` and `OpticalBackup` set this, on the Tuning tab under the sensor.

Every seeker setting is on the **Tuning** tab under Guidance, and a weapon pack sets them as
attributes on its `<Munition>`:

| Setting | Attribute | Default | |
| --- | --- | --- | --- |
| **Seeker** | `SeekerBand` | `Radar` | `Radar`, `Infrared`, or `None` for a seeker no decoy fools. Read only for `Guidance="Seeker"` |
| **Countermeasure resistance** | `CountermeasureResistance` | `0` | 0 to 1: how much of a decoy's chance the seeker refuses |
| **Looks again after a decoy** | `ReacquiresAfterDecoy` | `true` | whether a seeker goes back to its target once the decoy is spent |
| **Doppler gate** | `DopplerGateMps` | `0` | m/s; chaff is only seen within this of the target's closing speed. Zero is no gate |

Each dispenser's component row shows what it has left, and the first carries the craft's controls:
**Flares**, **Chaff** or **Both** throw a salvo of two cartridges a quarter of a second apart from
every dispenser holding that load, together, so a symmetric pair throws either side of the craft and
each dispenser fitted makes a press heavier. **Auto-dispense** throws on its own when a
missile whose seeker is on the craft closes inside 6 km: flares for a heat seeker, chaff for a radar
one, both for anything it cannot place, at most once a second. A dispenser is never reloaded.

## Testing

The system works end to end in game: the part loads and renders, the launcher tracks, missiles
intercept at 16–20 m, and the cannon kill at 6–8 m. [`CHECKLIST.md`](CHECKLIST.md) walks through
what is confirmed and what is still open, in risk order.

`./tools/test.sh` runs the headless suite — whole engagements at ecliptic speeds, the drives,
launch and lead geometry, the fuses and the registry — with no game present.
`./tools/model/checkswept.py` sweeps the mount through its travel and reports any assembly that
comes adrift or passes through another, needing neither Blender nor the game.

## Build

Requires the **.NET 10 SDK** — the mod targets `net10.0` because that is what KSA runs on.
Distro packages are usually still .NET 8, which fails with `NETSDK1045`. The scripts below
resolve an SDK from `~/.dotnet` automatically; `source tools/env.sh` if you want bare `dotnet`
to work in your shell.

```bash
./tools/sync-import.sh                 # copy game assemblies into Import/
./tools/build.sh                       # build the mod
./tools/test.sh                        # guidance and fuse tests, no game required
./tools/validate-parts.py              # check asset Ids, texture paths and launch geometry
./tools/deploy.sh                      # build and install into the mods folder
```

The model is committed, so a plain build does not need Blender. Rebuild it with
`./tools/model/build.sh` — see [`tools/model/README.md`](tools/model/README.md).

### Releasing

Releases are cut automatically by [semantic-release](https://semantic-release.gitbook.io/) when
something lands on `main`: it reads the Conventional Commits since the last tag, works out the
version, writes `CHANGELOG.md`, stamps the version into the project file, tags and publishes a
GitHub Release. No version number is ever edited by hand.

Building the archive needs KSA's assemblies, which CI gets from a private repository (see
below), so the second job builds and attaches it automatically. If that is ever unavailable the
release is still cut, just without a binary — attach one from a machine that has KSA:

```bash
./tools/publish-release.sh            # build and attach to the release for the current version
./tools/publish-release.sh v0.2.0     # ...or a specific tag
```

Or build the archive alone:

```bash
./tools/package.sh                     # dist/KSArmory-<version>.zip
./tools/package.sh --version 1.2.0     # override the version
```

#### What bumps what

`feat`, `fix` and `perf` all cut a **patch**. Minor versions are never automatic — tag one by hand
when something genuinely lands:

```bash
git tag -a v0.9.0 -m "second weapon system"
git push origin v0.9.0
```

semantic-release reads the newest tag and carries on from it, so the next `fix` after that is
`0.9.1`.

Tags are what anchor the whole sequence: semantic-release treats "no tags" as "no releases" and
publishes `1.0.0`, whatever the project file says.

#### Going 1.0.0

When it is ready, tag it by hand and let automation carry on from there:

```bash
git tag -a v1.0.0 -m "1.0.0"
git push origin v1.0.0
./tools/set-version.sh 1.0.0    # optional; the next release rewrites it anyway
```

The next `feat` after that gives `1.1.0`, the next `fix` `1.0.1`.

A `BREAKING CHANGE:` footer would also promote `0.x` straight to `1.0.0`, since that is what
semver says a major bump means. Worth knowing so it does not happen by accident before you
intend it — while pre-1.0, a breaking change is usually better expressed as a `feat`.

Release builds carry no debug symbols and start the log at `INFO` instead of `DEBUG`; tick
**Verbose log** in the panel to get the detail back when reporting a bug. The script refuses to
ship a `.pdb` or any assembly that is not ours.

> **CI needs KSA's assemblies, and they are not redistributable.** They live in a private
> repository and are checked out with a read-only deploy key held in the `KSA_ASSEMBLIES_KEY`
> secret — keeping your own licensed copy privately is fine, publishing it is not. Forks without
> the secret still get the tooling job: mesh checks, the `Sim/` boundary guard, case-exact asset
> paths, texture reproducibility, shellcheck and XML validation.

On WSL you can also drive the whole loop from the terminal:

```bash
./tools/setup-starmap.sh               # one-off: install StarMap, point it at KSA
./tools/run.sh                         # build, deploy, launch; mod output streams back
```

No .NET 10 SDK yet?

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir $HOME/.dotnet
```

`Import/` is gitignored — it holds the game's own binaries, which are not redistributable.
`sync-import.sh` reads them from your install (override with `KSA_DIR=...`).

`StarMap.API.dll` comes from a StarMap release rather than the game; drop it into `Import/` or
pass `STARMAP_DIR=...`.

## How it works

**Three of the parts are generated rather than authored.** The Pantsir, the LAU-7 rail and the
EO director have no `.blend` file: `tools/model/pantsir.py` builds them out of primitives
in headless Blender and exports the mesh atlas, while `palette.py` writes the textures as a grid of
flat swatches that every face is UV-mapped into. One command rebuilds all of it:

```bash
./tools/model/build.sh
```

It also renders previews, so a shape can be judged without the game — build, look, adjust, repeat
— and prints the launch geometry as a block to paste into the profile.
`tools/validate-parts.py` fails if a profile and the mesh ever disagree about where the tubes are,
and checks every asset Id and texture path, because all of those fail silently in-game.

Everything since is **authored** in Blender and its `.blend` is not in this repository, so a
committed asset cannot be rebuilt from a clean checkout — which is why `checkmesh.py` and
`validate-parts.py` matter more for those than for the generated three.

The part itself is inert — KSA sees structure with mass and a collider. The C# mod finds it on
the vehicle and mounts the battery there. That split avoids registering a custom module type
into the engine's internal update lists, which is not reachable without patching.

**Adding a weapon system is data, not code.** The files in `src/KSArmory/KSArmory/` define each
launcher, round and sensor, one file per family; discovery is by part Id, so nothing in the simulation
or the game binding names a particular vehicle. A new system is an entry there plus its art.

The source is split by whether it can see KSA at all: `Sim/` cannot, `Ksa/` does. The test
project links all of `Sim/` and references no game assembly, so the boundary enforces itself —
reach for a KSA type in `Sim/` and the tests stop compiling.

Rounds are simulated by the mod rather than being spawned as KSA vehicles. That gives
sub-frame integration accuracy — at 2 km/s of closing speed a single frame covers ~67 m, far
more than any sensible fuse radius — and keeps the mod from touching your save. They are still
*drawn* as real geometry: each round is a subpart of the launcher, hidden until fired and then
flown by writing its transform every frame.

Guidance is textbook true proportional navigation. With line-of-sight rate
`ω = (r × v) / (r · r)` and closing velocity `Vc = -v`, the commanded acceleration is
`a = N · (ω × Vc)`. Nulling the line-of-sight rotation puts the round on a collision triangle,
which is what makes it lead. Gravity is biased out, the command is projected perpendicular to
the flight path, and it is clipped to the structural g limit.

The fuse solves for closest approach analytically within each sub-step
(`t = -r·v / v·v`, clamped to the step) rather than sampling distance, so nothing tunnels
through the trigger radius between frames.

See [`docs/KSA-MODDING-NOTES.md`](docs/KSA-MODDING-NOTES.md) for the reverse-engineered game
API this is built on.

## Limitations

- The radar's search volume does not follow the turret. Where it points is a per-system setting —
  local "up", the launcher's own facing, or the tubes — but never the current aim.
- A *secondary* camera window driven by the optical head has no sky, clouds or terrain detail:
  KSA renders secondary viewports without the atmosphere pass. That is why the head borrows the
  player's own view instead. [`docs/BLOCKED-ON-KSA.md`](docs/BLOCKED-ON-KSA.md) has the mechanism
  and what would change it.
- The wheels are geometry. KSA has no wheel or suspension module, so the vehicle is placed rather
  than driven.
- Rounds hit terrain only where their profile asks for it, because it costs a height sample per
  round per frame and a CIWS burst is 150 shells: the bomb, the reentry vehicle and the 5"/54 shell
  do, so a 20 mm shell still passes through a hill and a missile that misses carries on into space. Structures are not
  collided with at all.
- The radar can mask against the real skyline and ships not doing it. `TerrainSamples` is the
  number of height lookups one contact may cost and defaults to zero, which is the body's mean
  sphere alone — so a craft behind a ridge is still seen until it is turned up.
- Damage is binary. KSA exposes no partial-damage model, only outright destruction.

## Contributing

Issues and pull requests welcome. **[`CONTRIBUTING.md`](CONTRIBUTING.md) is the short version** —
what you need, what you don't, and the handful of rules that are not style preferences.

The quickest start on any platform:

```bash
./tools/doctor.sh   # checks this machine and prints the fix for anything missing
```

**Work goes on `dev`. `main` is the release branch.** A push to `main` cuts a release and
publishes it to SpaceDock, so branch off `dev`, open pull requests against `dev`, and push there as
often as you like — CI runs on every branch and every pull request, so the build, the tests and the
other twenty checks gate each push either way. Releasing is then a deliberate act: `dev` is merged
into `main` and semantic-release cuts one release covering everything that accumulated. It is a
merge rather than a squash, because the changelog is built from the individual commit subjects.

Worth knowing up front: **most of this repository is testable without launching the game.**
Everything under `src/KSArmory/Sim/` is free of KSA types by construction, so guidance, threat
classification, tube geometry and the fuse can all be worked on headlessly. Building it still
needs KSA's assemblies — `double3` comes from `Brutal.Numerics.dll` — which is what
`./tools/doctor.sh` checks for first.

The rest of this section is the longer version.

### Read `CLAUDE.md` first

It is the working notes for this repo: the environment traps, the design decisions worth not
re-litigating, and the failure modes that are invisible from the code alone.
[`docs/KSA-MODDING-NOTES.md`](docs/KSA-MODDING-NOTES.md) is the reverse-engineered API
reference — reference frames, the loader contract, part XML.

### Setting up

```bash
./tools/install-hooks.sh   # commit-msg hook, so a bad message is caught before it exists
./tools/sync-import.sh     # copy KSA's assemblies into Import/ (gitignored, not redistributable)
                           # ...or skip it: the build also finds a local KSA install by itself
./tools/build.sh           # needs .NET 10; a distro dotnet 8 fails with NETSDK1045
./tools/test.sh            # the full suite, no game required
```

The wrapper scripts exist because bare `dotnet` picks up the system SDK and cannot target
`net10.0`. `source tools/env.sh` once if you want `dotnet` to work directly in your shell.

After a KSA update the assemblies CI builds against have to be refreshed too, or it silently
compiles the mod against a different game from the one you are testing on. `ksa-assemblies.lock`
records which build is expected and both CI and `sync-import.sh` check it — see the "After a KSA
update" section of [`CLAUDE.md`](CLAUDE.md) for the four commands.

### Commit messages

Releases are automatic, so commit messages are the input to versioning. Use
[Conventional Commits](https://www.conventionalcommits.org/):

```
feat(turret): elevate the pods on their trunnions
fix(rounds): anchor bodies to the tube they left, not the orbit position
docs: write an install guide
```

| Prefix | Effect on the version |
| --- | --- |
| `feat:`, `fix:`, `perf:`, `build:`, `revert:` | patch — `1.2.3` → `1.2.4` |
| any type with `!` or a `BREAKING CHANGE:` footer | major — `1.2.3` → `2.0.0` |
| `docs:`, `test:`, `chore:`, `ci:`, `style:`, `refactor:` | no release |
| a minor — `1.2.3` → `1.3.0` | never automatic; tag it by hand |

`feat` cutting a patch is deliberate. The type says what a change *is*, for the changelog; how big
a bump it earns is a separate decision, and a mod's routine flow of features, enhancements and
fixes together should not push the middle digit every time.

`docs` and `refactor` still appear in the changelog; they just do not cut a release on their
own. A commit that does not parse is treated as no release — so a stray `wip` will not publish
anything, but it will also vanish from the changelog without saying so. That silence is why the
format is enforced rather than merely suggested:

- **Locally**, `./tools/install-hooks.sh` enables a `commit-msg` hook that rejects a bad message
  before the commit exists. `git commit --no-verify` bypasses it for a one-off.
- **In CI**, the same script checks every commit in a push or pull request.

Both run `tools/check-commit-msg.sh`, so they cannot disagree about what is legal.

### Before opening a PR

```bash
./tools/check-all.sh          # everything CI runs, about 45 seconds
```

One script, and CI calls the same one, so they cannot disagree about what "the checks" are.
`--list` names them; `--with-sweep` adds the ~43 s drive sweep.

Checks needing the game assemblies are skipped with a notice rather than failed, so this is
worth running without a KSA install. On a fork the `tooling` job runs in full — it needs no
secret — and only the `build` job is gated, skipping with a notice.

`./tools/install-hooks.sh` also wires it to `pre-push`.

### How the code is laid out

`src/KSArmory/` splits by whether a file can see the game:

- **`Sim/`** — pure simulation and data. Guidance, the turret drives, launch geometry, and the
  weapon profiles. The test project links this folder wholesale and references no KSA assembly,
  so **reaching for a KSA type here breaks the test build**. That is deliberate: it is the only
  reason any of this is testable without the game running.
- **`Ksa/`** — everything that binds to KSA. Keep new game contact funnelled through `KsaWorld`
  where you can; the API moves between builds and one file is easier to fix than ten.

If something in `Ksa/` turns out to have real maths inside it, move the maths into `Sim/` rather
than leaving it unverifiable. `FireGeometry` is the worked example: launch angles are testable
because they live there rather than inside `LauncherPart`.

### Adding a weapon system

It is data, not code. Nothing in `Sim/` or `Ksa/` names a built-in weapon.

1. Model it — authored in Blender, following `.claude/skills/ksa-blender/SKILL.md`. The headless
   generator builds the three parts that predate that and is not extended.
2. Declare the part in `KSArmoryAssets.xml` and `KSArmoryGameData.xml`.
3. Define a `<Launcher>` for it in a file in [`src/KSArmory/KSArmory/`](src/KSArmory/KSArmory/),
   naming the `<Munition>` and `<Sensor>` it uses; [`docs/WEAPON-PACKS.md`](docs/WEAPON-PACKS.md) has
   every attribute. The definition also gives the part its row in the panel.

Discovery is by part Id, so the system picks up whatever is fitted. A launcher that cannot
train is the same profile with `TurretMarker` left null.

### Traps worth knowing about

- **Ecl is absolute.** Positions near Earth are ~1.5×10¹¹ m and sweep past at ~29.8 km/s, so an
  ecliptic value read as a local one is wrong by kilometres. Never orient or measure anything
  with `VelocityEcl` — use the frame-relative velocity.
- **Two instants, on purpose.** `DrawAnchor` samples the render position this frame and the
  geometry's position one update earlier. Collapsing them looks like a tidy-up and puts the
  whole overlay 500 m from the craft. `DrawAnchorTests` fails if they are collapsed.
- **Model defects are invisible in Blender.** Coplanar faces and zero-area UVs both render fine
  in a preview and make the vehicle crawl with flickering speckle in game. `checkmesh.py`
  catches both — run it, do not trust your eyes.
- **Bad asset Ids fail silently.** No log line, no error; the part just renders wrong.
  `validate-parts.py` is the only thing that will tell you.

### Style

Match what is there. Comments explain *why*, especially where the obvious approach is wrong —
several of the files carry a note saying "do not simplify this", and those notes are load-bearing.

## Licence

[MIT](LICENSE).

That covers the C# mod, the tooling, and the art generated or authored here. Two things came from
outside. The Phalanx's cannon recording under `tools/audio/` is CC0 and carries no condition on
redistribution — see the README beside it. And the 5"/54 Mk 42's model, shell and textures, and the
M197's model and textures, were made for this mod by Mallikas.

It does **not** cover Kitten Space Agency itself. The mod compiles against KSA's assemblies but
never redistributes them: `Import/` is gitignored, the project references them with
`Private=false`, and `tools/package.sh` refuses to build an archive containing any DLL but its
own. You need your own copy of the game.

Not affiliated with or endorsed by RocketWerkz.
