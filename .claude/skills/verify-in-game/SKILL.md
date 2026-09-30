---
name: verify-in-game
description: Confirm a change works in Kitten Space Agency itself, not just in the suite. Use before committing a behaviour change as a fix or feat, when the user asks to fly, test or confirm something in game, or when CODE-HEALTH or CHECKLIST says an item "wants a flight". Covers whose game you may use, which scenario exercises what, driving a running game through the bridge, catching effects that last a fraction of a second, and what counts as evidence.
---

# Verifying a change in game

CLAUDE.md's hardest rule is that a behaviour change is unverified until it has been flown: this
mod's worst bugs live between the maths and what KSA actually does. This is the procedure. Every
tool it names exists; the skill is the order and the traps.

## 0. Whose game is it

**The bridge reaches any running KSA**, including one the user is playing. Before any command:

```bash
tasklist.exe | grep -iE 'starmap|kittenspace'
```

- **Nothing running**: a scenario launches its own game and loads its own save. Go ahead.
- **A game is running and you did not start it this session**: ask before sending anything, and
  never restart it. A scenario run would take it over.
- **You started it (`--keep`)**: it is yours until the user starts playing in it. Once they have,
  treat it as theirs.

## 1. Know what is installed

`tools/scenario.sh` builds and deploys the working tree unless given `--no-deploy`. The installed
build names its commit, and **the commit is all it names**: uncommitted edits ship without changing
it, so the hash says where the tree started, not what is in it.

```bash
U=$(./tools/ksa-user-dir.sh)
strings "$U/mods/KSArmory/KSArmory.dll" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+\+[0-9a-f]+'
```

From a worktree, check `git merge-base --is-ancestor <installed-hash> HEAD` first. Deploying a
stale worktree over a newer install rolls `dev`'s work out of the game.

## 2. Pick the flight that exercises the change

| Change touches | Fly | What it exercises |
| --- | --- | --- |
| missiles, guidance, fuse, motor plume or sound | `./tools/scenario.sh head-on` | an AIM-9J off a LAU-7 rail at a drone |
| guns, lead, shells, muzzle flash, gun sound | `./tools/scenario.sh gunnery` | the Mk 42 against crossing drones, every shell scored |
| stores, bomb sight, tail kit, bursts | `./tools/scenario.sh drop` | a B61 off a climbing rocket, against the sight |
| the ballistic computer, the bus | `./tools/scenario.sh mirv` | a whole ballistic shot; 20+ minutes |
| countermeasures | bridge, save `GUIDED MISSILE TEST - CHAFF` | a craft carrying flare and chaff dispensers |

`tools/scenario.sh`'s header lists every variant. Add `--keep` to go on driving the game through
the bridge afterwards, and `KSARMORY_SCENARIO_VERBOSE=1` for the developer detail.

**A scenario overrides the session's settings for its flight** -- the shader pass, clouds,
overlays -- and puts them back at END. While it runs, tracers are debug lines and ground rings are
not painted unless `KSARMORY_SCENARIO_CLOUDS=1`.

## 3. A PASS is not a clean run

Read three things after every flight:

```bash
L="$U/Logs/KSArmory.log"
K=$(ls -t "$U"/Logs/KittenSpaceAgency.*.log | head -1)

grep -E 'WARN|ERROR' "$L"                                  # the mod's own complaints
grep -iE 'exception|Update task failed' "$K"               # the engine's; the master-server line is noise
python3 tools/ksa-mcp/server.py cli status                 # what is actually in the world now
```

- **`head-on`'s PASS means the engagement ended, not that anything died.** `status` lists the
  other craft: a drone that broke up is several entries with `_1`, `_2` suffixes.
- **A mod exception can be the engine's**: one thrown inside a hook lands in KSA's log, not in
  `KSArmory.log`.

## 4. Drive it through the bridge

```bash
S="timeout 120 python3 tools/ksa-mcp/server.py cli"
$S load   '{"save":"rocket missile"}'
$S system '{"craft":"NewRocket_1","auto_engage":false}'      # stop it firing on its own
$S fire   '{"craft":"NewRocket_1","east_m":3000,"up_m":1500}'
$S watch  '{"craft":"NewRocket_1","distance_m":10,"azimuth_deg":150,"elevation_deg":15}'
$S capture '{"label":"what","crop":false,"width":1200}'
```

Every call goes under a `timeout`: a bridge `step` blocks, and a hung call otherwise hangs the
shell. A capture saves a PNG under `$U/Logs/bridge/out/<id>/`; it is RGBA, so convert before
saving it as a JPEG to look at.

**Setting a scene up, then keeping it.** `site {"craft":...,"lat":...,"lon":...}` sets any craft
down, `spawn {"craft":"Rocket","name":...,"lat":...,"lon":...}` parks an uncrewed stock craft as a
target, `ground {"lat","lon","to_lat","to_lon","steps"}` reads the height against sea level along a
line without placing anything, and `save {"name":...}` writes it all to a save as KSA's console
does. `status` gives every craft's situation, which is how to tell `Landed` from in the surf.

- **Survey with `ground` before `site`.** A place at sea sets a craft on the seabed, 4 km down in the
  open ocean, and a place too shallow for it grounds it; setting a grounded craft down again and
  again destroyed one. KSA's coastlines and depths are not the atlas's.
- **`status`'s `agl_m` is height over the ground under the craft**, which at sea is the seabed.
- **A save keeps the live throttle** (`<EngineThrottle>`): zero it in the file after a `save`, or the
  craft sets off the moment it loads.

**`fire` lays a gun before it shoots**: the point is designated, as a shift-click would, and the
reply comes once the burst has begun, with `laid_after_s`; its rounds appear on the next step. `"lay": false` fires along wherever the
barrel points, which for an idle gun is its rest line. A missile's `"fired": 0` means the tubes
were still slewing.

## 5. Catching something that lasts a moment

Each bridge call costs about a second, so an effect shorter than that is gone before a capture
lands. A muzzle flash is 0.12 s and an AIM-9J's motor burns for 2.2.

- **Slow the world first, then act**: `speed {"x":0.05}`, fire, capture, `speed {"x":1}`. A
  0.12 s flash becomes 2.4 s of wall clock.
- **Or pause straight after**: fire, `pause`, then `watch` and capture a still frame.
- **Never judge flicker or shimmer paused**: a paused frame freezes the render noise. Measure it
  at 0.02x with a `frames` series and read the temporal map `capture` returns.
- **Take away what hides it**: leftover smoke hides flare cores; `set {"name":"MotorSmoke",
  "value":false}` for the shot, and set it back.

## 6. Test the failure, not just the success

A picture proves the effect appears once. Most failures in this mod are the second time, the
twentieth, or the one after a limit:

- **Pools leak slowly**: repeat the action past the pool's limit, then confirm the effect still
  appears -- twenty separate gun bursts, or more flares burning than `DecoyEffects.MaxLit`, then
  one more salvo after they burn out.
- **A change that removes something needs the old build flown too.** An effect missing after the
  change may have been missing before it; fly `dev` without the change on the same scenario.
- **Sweep the parameter a scenario fixes**: a fixed-code PASS at one range can exercise nothing.

## 7. Write down what was seen

- **The commit message says what was flown and what was seen**, with numbers where there are
  some. Anything not flown is said to be unflown. A fix nobody has seen working is committed as
  diagnostic or unverified, never as a fix.
- **Tick `CHECKLIST.md`** for every item the flight actually confirmed, and only those.
- **An item in `docs/CODE-HEALTH.md` that "wants a flight" is ticked** with one line of what the
  flight showed.
- **Hand back the game as you found it**: settings changed through `set` restored, and the view
  released with `watch {"release":true}`.
