# KSA Air Defence

A point-defence mod for **[Kitten Space Agency](https://kittenspaceagency.wiki.gg/)**: a
**Pantsir-S1** that searches, tracks and engages vehicles flying at or past your craft.

- **A buildable part** — *Pantsir-S1 Point Defence System*, an 8×8 vehicle with two pods of six
  missile containers, twin autocannon, tracking array and search radar; surface-attachable in
  the editor
- **Twelve rounds**, salvo-fired with configurable spacing and reload
- **Search radar** with a steerable range/cone, lock-on dwell, and manual designation
- **Proportional-navigation guidance** — the rounds *lead* a crossing target instead of chasing it
- **Proximity-fused warhead** with a lethal radius, armed a set time after launch
- **Auto-engage** using a closest-point-of-approach threat model, so targets merely *passing by*
  are engaged, not only ones closing head-on
- Full ImGui control panel and in-world visualisation of the search volume, tracks and rounds

> Built against KSA build `2026.8.3.5117`. KSA is pre-release and has no official code-modding
> API; this uses the community [StarMap](https://github.com/StarMapLoader/StarMap) loader and
> may need updating when the game does.

## Install

### What you need first

- **Kitten Space Agency.** Built against build `2026.8.3.5117`; a different build may need a
  rebuild of the mod. **Windows and Linux both work** — the mod is a portable .NET assembly with
  no native code, so the single release archive is the same on either.
- **[StarMap](https://github.com/StarMapLoader/StarMap/releases)**, the community mod loader.
  KSA has no official code-modding API, so nothing here runs without it. Edit its
  `StarMapConfig.json` to point at your KSA install — StarMap reads that file **relative to its
  own directory**, so it has to be launched from where it lives.

### Steps

1. **Get the mod.** Download `AirDefence-<version>.zip` from
   [Releases](../../releases), or build it yourself with `./tools/package.sh`.

2. **Unzip it into your mods folder.** That folder lives inside KSA's user directory:

   | Platform | KSA user directory |
   | --- | --- |
   | Windows | `Documents\My Games\Kitten Space Agency\` |
   | Linux | wherever KSA keeps its user data — commonly `~/.local/share/Kitten Space Agency/`; the folder containing `manifest.toml` and `Logs/` is the one you want |
   | Proton / Wine | inside the prefix, at `.../drive_c/users/steamuser/Documents/My Games/Kitten Space Agency/` |

   You should end up with:

   ```
   <KSA user directory>/mods/AirDefence/
     AirDefence.dll
     mod.toml
     AirDefenceAssets.xml
     AirDefenceGameData.xml
     Meshes/AirDefence_MeshAtlas.glb
     Textures/AirDefence_{Diffuse,Normal,PBR}.png
   ```

   The folder layout matters, and on Linux so does the **case**. `AirDefenceAssets.xml` refers
   to `Meshes/` and `Textures/` by relative path; a case mismatch is silently tolerated on
   Windows and fails on Linux. Unzip rather than retyping the names.

3. **Register it in `manifest.toml`.** This is the step everyone misses — *dropping the folder
   in is not enough*. Open `manifest.toml` in the same user directory and add:

   ```toml
   [[mods]]
   id = "AirDefence"
   enabled = true
   ```

   KSA discovers mods through that list, and StarMap walks the same list to find code mods.
   Without an entry, nothing loads and nothing tells you why.

4. **Launch through StarMap, not the game directly.** Starting KSA directly bypasses the loader
   entirely: the part will still appear in the editor, but none of the behaviour will run.

   On Windows that is `StarMap.exe`. StarMap also ships `StarMap.dll` — a portable .NET
   assembly — so on Linux `dotnet StarMap.dll` from the same folder is the equivalent. Either
   way it must run from its own directory, because it reads `StarMapConfig.json` relative to
   itself.

### Check it worked

The mod writes its own log to `Logs/AirDefence.log` under the KSA user directory, and prints
the path it chose to stdout on startup — handy if it ended up somewhere unexpected. The file is
truncated each session. You should see:

```
loading (mod id: AirDefence)
ready - 12 tubes, safe. Open the 'Air Defence' panel to arm.
```

The `ready` line arrives roughly **20 seconds after** the first, because the loader waits for
the game to finish loading. That delay is normal, not a hang.

Then: build a craft with the **Pantsir-S1 Point Defence System** (under *Structural*), launch,
and the **Air Defence** panel appears once you are in flight.

### If it doesn't

| Symptom | Cause |
| --- | --- |
| No `AirDefence.log` at all | StarMap never ran the mod. Check the `manifest.toml` entry, and that you launched `StarMap.exe`. |
| Part missing from the editor | The asset XML did not load. `KittenSpaceAgency.log` is where XML and asset errors appear. |
| Part is there but nothing happens in flight | The DLL did not load, but the XML did — check `mod.toml`'s `EntryAssembly = "AirDefence"` matches the DLL name. |
| Part renders untextured or invisible | `Meshes/` or `Textures/` did not come across, or the folder layout was flattened. |
| Panel says `Launcher: none fitted` | The part is not on the craft you are flying. Untick **Require launcher part** to test anyway. |
| Works on Windows, part untextured on Linux | A case mismatch in a file or folder name. Linux filesystems are case-sensitive; Windows is not. Re-unzip rather than renaming by hand. |

Developing rather than just playing? `./tools/deploy.sh` builds, installs and registers the mod
in one go — including the `manifest.toml` entry.

## Use

1. In the editor, attach the **Pantsir-S1 Point Defence System** to your craft. It is under
   *Structural* and surface-attaches to the side or top of a stack. It is also its own command
   source, so a craft consisting of nothing but the Pantsir builds and launches.
2. In flight, tick **Master arm**. Nothing launches while it is safe.
3. Tick **Auto engage** to let it fire on its own, or leave it off and use **FIRE** against the
   current lock.
4. **Pin to this vehicle** freezes the battery onto that craft, so you can switch control away
   and watch it defend itself.

The launcher **traverses and elevates onto the target**, and will not fire until it has settled
on the aim point. The radar's own boresight stays local "up" — a hemisphere is what you want for
a defence site. Green dots mark loaded tubes, grey ones spent.

Testing without opening the editor? Untick **Require launcher part** and the battery works on
any craft, firing from the hull.

A contact becomes a *threat* when its closest point of approach falls inside **Threat radius**
within **Threat horizon** seconds. It becomes shootable once held for **Lock time**. The
launcher commits **Rounds per target** rounds before re-evaluating.

### Tuning that actually matters

| Setting | Effect |
| --- | --- |
| **Nav constant N** | 3–5 is the realistic band. Higher leads harder and pulls more g; too high oscillates. |
| **Max lateral g** | The airframe limit. Drop it and fast crossing targets start escaping. |
| **Seeker FOV** | How far off its own nose the round can still see. Narrow means broken locks on hard crossers. |
| **Fuse radius** | Trigger distance. Larger is more forgiving; it does not increase lethality. |
| **Lethal radius** | What actually kills. Between this and blast radius the target survives. |
| **Gravity compensation** | 1.0 makes guidance ignore the fall. Drop it for lobbed, ballistic-looking shots. |
| **Drag k** | `a = -k·|v|·v`. Zero gives vacuum flight; raise it for atmospheric bleed-off. |

## Testing

The system works end to end in game: the part loads and renders, the launcher tracks, and
proportional navigation intercepts at 22–23 m. [`CHECKLIST.md`](CHECKLIST.md) walks through what
is confirmed and what is still open, in risk order.

`./tools/test.sh` runs 74 headless tests — whole engagements at ecliptic speeds, the turret
drives, launch geometry and the registry — with no game present.

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

Building the archive still needs the game, so that half runs on a self-hosted runner and
attaches the file to the release. Without one you get versioning, changelogs and releases —
just no attached binary. Build and upload it yourself:

```bash
./tools/package.sh                     # dist/AirDefence-<version>.zip
./tools/package.sh --version 1.2.0     # override the version
```

#### Starting at 0.x

The project is pre-1.0, and this needs one bootstrap step. **semantic-release publishes 1.0.0
for its first ever release** — it takes "no tags yet" to mean "no releases yet", and the version
in the project file has no say in it. Anchor it with a tag before the first automated run:

```bash
git tag -a v0.1.0 -m "0.1.0"
git push origin v0.1.0
```

From there it behaves normally: `feat` gives `0.2.0`, `fix` gives `0.1.1`.

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

> **CI cannot build this mod on a hosted runner.** It needs KSA's assemblies, which are not
> redistributable. The always-on CI job runs everything that does not need the game — mesh
> checks, the `Sim/`-boundary guard, texture reproducibility, shellcheck, XML validation — and
> the full build and tests run on a self-hosted runner with KSA installed, enabled with the
> repository variable `KSA_SELF_HOSTED`.

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

**The model is generated, not authored.** There is no `.blend` file: `tools/model/pantsir.py`
builds the entire vehicle out of primitives in headless Blender and exports the mesh atlas,
while `palette.py` writes the textures as a grid of flat swatches that every face is UV-mapped
into. One command rebuilds all of it:

```bash
./tools/model/build.sh
```

It also renders five preview images, which is how the shape was iterated on — build, look,
adjust, repeat — and prints the launch geometry as a C# block.
`tools/validate-parts.py` fails if `Sim/Arsenal.cs` and the mesh ever disagree about where the
tubes are, and checks every asset Id and texture path, because all of those fail silently
in-game.

The part itself is inert — KSA sees structure with mass and a collider. The C# mod finds it on
the vehicle and mounts the battery there. That split avoids registering a custom module type
into the engine's internal update lists, which is not reachable without patching.

**Adding a weapon system is data, not code.** `src/AirDefence/Sim/Arsenal.cs` registers each
launcher, round and sensor as a profile; discovery is by part Id, so nothing in the simulation
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

- The autocannons are decorative — they traverse with the turret but never elevate or fire.
- The radar's search volume is a hemisphere about local "up"; it does not follow the turret.
- Rounds only interact with their designated target; they ignore terrain and other craft.
- Radar has no occlusion or line-of-sight check.
- Damage is binary. KSA exposes no partial-damage model, only outright destruction.
- Settings are not persisted between sessions.
- One battery per craft: if several launchers are fitted, the first one found wins.

## Contributing

Issues and pull requests welcome. A few things will save you time.

### Read `CLAUDE.md` first

It is the working notes for this repo: the environment traps, the design decisions worth not
re-litigating, and the bugs that have already been found and fixed. Most of them cost hours to
diagnose and are invisible from the code alone. [`docs/KSA-MODDING-NOTES.md`](docs/KSA-MODDING-NOTES.md)
is the reverse-engineered API reference — reference frames, the loader contract, part XML —
and will save you an evening with a decompiler.

### Setting up

```bash
./tools/install-hooks.sh   # commit-msg hook, so a bad message is caught before it exists
./tools/sync-import.sh     # copy KSA's assemblies into Import/ (gitignored, not redistributable)
./tools/build.sh           # needs .NET 10; a distro dotnet 8 fails with NETSDK1045
./tools/test.sh            # 74 tests, no game required
```

The wrapper scripts exist because bare `dotnet` picks up the system SDK and cannot target
`net10.0`. `source tools/env.sh` once if you want `dotnet` to work directly in your shell.

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
| `fix:`, `perf:`, `build:`, `revert:` | patch — `1.2.3` → `1.2.4` |
| `feat:` | minor — `1.2.3` → `1.3.0` |
| any type with `!` or a `BREAKING CHANGE:` footer | major — `1.2.3` → `2.0.0` |
| `docs:`, `test:`, `chore:`, `ci:`, `style:`, `refactor:` | no release |

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
./tools/test.sh                                              # simulation
./tools/check-boundary.sh                                    # Sim/ stays free of KSA types
./tools/validate-parts.py                                    # asset Ids, texture paths, launch geometry
./tools/model/checkmesh.py src/AirDefence/Meshes/*.glb       # only if you touched the model
```

Of those, CI's always-on job runs only `check-boundary.sh` and `checkmesh.py`. The other two need
the game — the tests reference KSA's numerics assembly, and `validate-parts.py` reads Core's
asset library — and a GitHub-hosted runner has neither, because those files are not
redistributable. **So running the tests is on you**; nothing upstream will catch a regression in
them unless the repo has a self-hosted runner with KSA installed.

### How the code is laid out

`src/AirDefence/` splits by whether a file can see the game:

- **`Sim/`** — pure simulation and data. Guidance, the turret drives, launch geometry, and the
  weapon profiles. The test project links this folder wholesale and references no KSA assembly,
  so **reaching for a KSA type here breaks the test build**. That is deliberate: it is the only
  reason any of this is testable without the game running.
- **`Ksa/`** — everything that binds to KSA. Keep new game contact funnelled through `KsaWorld`
  where you can; the API moves between builds and one file is easier to fix than ten.

If something in `Ksa/` turns out to have real maths inside it, move the maths into `Sim/` rather
than leaving it unverifiable. `FireGeometry` came out of `LauncherPart` exactly that way, and a
launch-angle bug only became testable afterwards.

### Adding a weapon system

It is data, not code. Nothing in `Sim/` or `Ksa/` names the Pantsir.

1. Model it — copy `tools/model/pantsir.py`, keep the group and pivot conventions, export into
   the same atlas.
2. Declare the part in `AirDefenceAssets.xml` and `AirDefenceGameData.xml`.
3. Register a `LauncherProfile` in [`src/AirDefence/Sim/Arsenal.cs`](src/AirDefence/Sim/Arsenal.cs),
   with the geometry `tools/model/build.sh` prints. Add a `MunitionProfile` if the round differs.

Discovery is by part Id, so the battery picks up whatever is fitted. A launcher that cannot
train is the same profile with `TurretMarker` left null.

### Traps worth knowing about

These have all bitten at least once:

- **Ecl is absolute.** Positions near Earth are ~1.5×10¹¹ m and sweep past at ~29.8 km/s. Six
  separate bugs came from treating an ecliptic value as a local one. Never orient or measure
  anything with `VelocityEcl` — use the frame-relative velocity.
- **Two instants, on purpose.** `DrawAnchor` samples the render position this frame and the
  geometry's position one update earlier. Collapsing them looks like a tidy-up and puts the
  whole overlay 500 m from the craft. It has happened twice; `DrawAnchorTests` fails if it
  happens again.
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

That covers everything in this repository: the C# mod, the tooling, and the art — the mesh and
textures are generated by `tools/model/`, so there is no third-party asset licence to inherit.

It does **not** cover Kitten Space Agency itself. The mod compiles against KSA's assemblies but
never redistributes them: `Import/` is gitignored, the project references them with
`Private=false`, and `tools/package.sh` refuses to build an archive containing any DLL but its
own. You need your own copy of the game.

Not affiliated with or endorsed by RocketWerkz.
