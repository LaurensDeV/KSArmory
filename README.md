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

1. Install [StarMap](https://github.com/StarMapLoader/StarMap/releases) and point its
   `StarMapConfig.json` at your KSA directory.
2. Copy `AirDefence.dll`, `mod.toml`, both `AirDefence*.xml` files and the `Meshes/` and
   `Textures/` folders into `Documents/My Games/Kitten Space Agency/mods/AirDefence/`.
3. Launch the game with **`StarMap.exe`**, not `KSA.exe`.

`./tools/deploy.sh` does steps 2 and 3's legwork for you.

The **Air Defence** panel appears once you are in flight.

## Use

1. In the editor, attach the **Pantsir-S1 Point Defence System** to your craft. It is under
   *Structural* and surface-attaches to the side or top of a stack. It is also its own command
   source, so a craft consisting of nothing but the Pantsir builds and launches.
2. In flight, tick **Master arm**. Nothing launches while it is safe.
3. Tick **Auto engage** to let it fire on its own, or leave it off and use **FIRE** against the
   current lock.
4. **Pin to this vehicle** freezes the battery onto that craft, so you can switch control away
   and watch it defend itself.

The battery's boresight is local "up", which is what you want for a defence site — the part
does not aim independently. Green dots mark loaded tubes, grey ones spent.

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

Nothing here has been verified in-game. [`CHECKLIST.md`](CHECKLIST.md) walks through it in
risk order, with what each failure would mean.

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
`tools/validate-parts.py` fails if `LauncherPart.cs` and the mesh ever disagree about where
the tubes are, and checks every asset Id and texture path, because all of those fail silently
in-game.

The part itself is inert — KSA sees structure with mass and a collider. The C# mod finds it on
the vehicle and mounts the battery there. That split avoids registering a custom module type
into the engine's internal update lists, which is not reachable without patching.

Rounds are simulated by the mod rather than being spawned as KSA vehicles, and drawn with the
engine's gizmo renderer. That gives sub-frame integration accuracy — at 2 km/s of closing speed
a single frame covers ~67 m, far more than any sensible fuse radius — and keeps the mod from
touching your save. The trade-off is that rounds render as tracers with trails rather than
modelled rockets.

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

- Rounds render as tracers with trails, not modelled rockets.
- The launcher does not aim — boresight is local "up" however the part is mounted.
- Rounds only interact with their designated target; they ignore terrain and other craft.
- Radar has no occlusion or line-of-sight check.
- Damage is binary. KSA exposes no partial-damage model, only outright destruction.
- Settings are not persisted between sessions.

## Licence

MIT. Not affiliated with or endorsed by RocketWerkz.
