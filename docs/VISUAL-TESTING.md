# Seeing the game from a terminal

**Half built.** How an agent working from WSL checks what this mod draws, what that loop cost on
the night the nuclear burst was built (2026-09-22), and what would make it fast. Blender has an MCP
connector that lets an agent look at the open document, move a camera and render; KSA has nothing
of the kind, and every item here is a piece of one built out of what the engine and the mod
already allow.

## What is built

**The bridge** (`Ksa/Bridge.cs`, `Sim/BridgeCommand.cs`) and **the MCP server over it**
(`tools/ksa-mcp/server.py`, registered in `.mcp.json`), with **shader hot reload**, **capture
bundles**, **same-instant variants**, **frame series** and **the toolkit** (`tools/vis/vis.py`) —
items 1 to 5 and 10 below. From a shell, before a session has the MCP tools:

```bash
python3 tools/ksa-mcp/server.py cli launch '{"save":"B61-13"}'
python3 tools/ksa-mcp/server.py cli pause
python3 tools/ksa-mcp/server.py cli burst '{"kt":0.3,"east_m":2400}'
python3 tools/ksa-mcp/server.py cli camera '{"distance_m":2400,"elevation_deg":8}'
python3 tools/ksa-mcp/server.py cli capture '{"label":"flash","frames":6,"every_s":0.25}'
python3 tools/ksa-mcp/server.py cli capture '{"label":"ab","variants":[{"ShaderPass":0}]}'
python3 tools/ksa-mcp/server.py cli reload_shaders
```

The CLI writes what an MCP client would be shown inline to `tools/ksa-mcp/last/`, and every call
empties it first -- copy a capture out before the next call.

**What it found on its first night**, each in minutes where a flight cost three:

- **The whiteout reached 0.51, not full, at 2.4 km.** Captured at exact ages from a paused burst,
  the manifest's numbers showed the flash reckoned off the drawn ball's size — still small at the
  instant of the flash — and then a real bug under it: the whiteout kept the largest single rise
  rather than their sum, so a flash arriving over two frames stopped at the first one's 0.6. Fixed
  and re-measured the same way: 0.97 at 2.4 km, 0.44 at 10.
- **A save load really does clear the clouds**: one standing, the save loaded, none.
- **Three versions of the weather fade on one frozen instant**, swapped by hot reload between
  captures, showed the centred band washing out the cap wherever a deck sat behind it — the
  transparency reported from play — and the fade starting at the weather's distance not doing so.
- **What it could not show**: the flicker reported from play. Paused, all three fades are equally
  steady (0.14–0.15 of 255 between frames); the flicker needs the camera or the weather moving,
  and a paused series is blind to that by construction. With the world running and the camera
  held it was not reproduced either: two rounds of eight frames disagreed about which fade was the
  noisier, which says the scene's own motion is larger than the effect. The button in 8 is the way
  to get it: frames from the moment somebody sees it.

**Hot reload is one reflected call and has one trap.** `ShaderReference.DoLoad` is internal, and it
destroys the old module even when the new compile returns nothing, as it does for a missing file —
so the bridge checks the file first. A compile error throws before anything is destroyed, comes back
as the error with its line, and leaves the old shader drawing.

## The loop as it stands

`tools/scenario.sh drop:10,0,dumb` with `KSARMORY_SCENARIO_CLOUDS=1` boots the game, loads a save,
drops a store on the pad, pins `CloudWatch`'s camera on the burst, asks KSA's own `ScreenshotCapture`
for a picture at a list of burst ages, and exits pass or fail. The pictures land in
`<user dir>/exports/screenshots/`, and are read from here with Pillow: contact sheets, crops, diffs,
pixel samples. The options added that night — `watchelev`, `stillat`, `blackout`, `cloudwarp`,
`twoclouds` — are all this loop with a different camera or a different world.

**A flight is about three minutes**, and roughly half of it is watching the cloud rise in real
time. Every change to C# or to the shader costs one, because the DLL is loaded once and KSA compiles a
mod's shader only at load. So does every question, including the ones a person looking at the screen
would answer in a second.

## Where it failed

Each of these happened, and each cost at least one flight:

- **A GLSL error was found in flight.** `flat` is a reserved word; the build passed, the game
  loaded, and the pass reported that it had failed to build. A compiler run from here takes a second.
- **Screenshots overwrote each other.** KSA names them to the second
  (`ksa_yyyyMMdd_HHmmss_WxH.png`), so two captures in one second are one file. Three frozen stills
  came back as two.
- **Pictures were paired with the wrong ages.** Nothing ties a file to what it was taken for, so ages
  were recovered by matching file times against log times — and a before-and-after of the glare halo
  was first judged on frames of different ages.
- **Crops were guessed.** Where the burst is on screen was read off a picture and typed in as pixel
  coordinates; a camera that stood anywhere else would have made every crop wrong.
- **Controls drifted.** Weather clouds move and the sun sets between two flights, so a diff between
  runs is mostly not the change. It took normalising against regions the burst cannot reach, and the
  paused stills, to get a clean answer.
- **Time was invisible.** Flicker, crawl and ghosting live between consecutive frames, and captures a
  second apart cannot see them. The weather mask crawled with the world paused and only a
  purpose-built freeze found it; the flicker the player reported later was never seen from here at all.
- **Measuring the renderer meant editing it.** How KSA's post chain maps a value to a pixel was
  found by hard-coding bands into the shader for one flight and deleting them after; turning the
  whiteout off to prove it was the cause was a line edited in and out of `CloudPass`. Each was a
  build, a flight and a revert.
- **Reports from play arrived as words.** "Super transparent", "flickering", "less blinding" came
  with no picture and no state, so each was a guess at a cause — and one guess, the weather band,
  made the transparency it was meant to fix.
- **The loop took the game from the player.** Deploying a fix meant killing the session they were
  testing in, three times running.

## What would make it smooth, ranked

### 1. A live session with a command channel, and an MCP server over it — built

The biggest change: **stop relaunching**. The mod already reads a request from a file beside its
log (`scenario.txt`); a command channel is the same idea kept open for the whole session.

- **The mod** polls `<user dir>/Logs/bridge/in/` ten times a second, runs each command file it
  finds, oldest first, and answers in `.../out/` — JSON, plus any pictures. Commands: *status*,
  *pause*, *resume*, *speed*, *step* (run so many simulated seconds, then pause), *camera* (a pose
  relative to the newest burst, or *release*), *burst* (a yield east and north of the craft, as the
  panel's burst tool does, with no damage), *capture* (3–5), *set* and *get* (a `Config` field),
  *reload_shaders* (2), *tune* (6, 7), *cost* (the pass's GPU time against the frame's, since a
  reset), *site*, *player_capture*, *clear* (forget the clouds) and *load* (a save). The log is
  read by the server directly.
- **An MCP server** in `tools/ksa-mcp/`, registered in `.mcp.json`, turns those into tools an agent
  calls the way it calls Blender's. It writes the command, waits for the result, and hands back
  pictures **inline, downscaled**, so a capture arrives in the conversation without a separate read.
- **Files, not a socket, on purpose.** The mod reaches the network exactly once, when a player clicks
  Send, and `tools/check-network.sh` enforces it; a localhost listener is still a listener. A folder
  in the user dir keeps that promise, needs no port and no firewall, and works across the WSL
  boundary as it is.
- **The session is the player's.** The server launches a game only if none is running and never
  closes one it did not launch — the rule the night broke three times.

What it costs: the poll is a directory listing a few times a second; commands run in the mod's own
step, so nothing new happens inside the engine's render loop. What it buys: the three-minute flight
becomes a command that answers in the time the thing takes — a paused capture in a second.

### 2. Shader hot reload, from the mod — built

KSA has a shader hot-reloader (`KSA.AssetReloader.ShaderReloader`) and it cannot serve a mod: it maps
a path by searching for `Content`, which a mod's path does not contain — that is the
`Error resolving include path … startIndex ('-1')` line in every session's log, and it is harmless.
What the reloader does is small: `ShaderReference.DoLoad()` recompiles the module. `DoLoad` is
internal, so it is one verified reflection away, like the handful the mod already makes; the mod
then drops its pipelines (`CloudPass.Release`) and they rebuild next frame against the new module.

Triggered by *reload shaders* on the channel, or by the file's timestamp changing. A compile error is
caught and logged, and the old pipeline stays. **The MCP tool copies the tree's shaders over the
installed ones first**, keeping their timestamps, so a variant has to be edited in the tree: one copied
into the mods folder by hand is overwritten before the game reads it, and the pass draws the tree's
version with nothing to say so. **And a write from WSL reaches the game late**: the game has been
seen compiling the file as it was before the copy, one reload behind. So the tool flushes each file,
checks the size and hash the game reports compiling against the tree's, and reloads again until they
match. Before that, a same-instant comparison of two shaders could run one of them twice. **Shader iteration goes from three minutes to about
one second**, which is most of what the burst's look took.

C# still needs a relaunch: an assembly cannot be unloaded. That is why 7 matters — it moves tuning
out of code.

### 3. Capture bundles: labelled pictures with a manifest — built

Every capture becomes a bundle: `out/<command id>/<seq>-<label>.png` and a JSON sidecar with the
burst's age and yield, the camera's range, elevation and field of view, **where the burst, the cap
centre, the top and the cloud as it stands now fall on screen**, the sun's elevation, the flash's
whiteout and glare, the world's speed and pause, and the settings that shape the picture. The
pass's GPU time and which weather images were bound are not in it yet.

- Names that cannot collide: the mod renames KSA's file the moment it appears, and does not ask for
  the next capture until it has.
- **Crops come from the manifest**, not from a guess: the burst's screen position is projected by the
  same camera that took the picture.
- **Age is in the file**, so a pairing error cannot happen.

### 4. Controls taken in the same instant — built

Pause, capture, change one thing, capture again, resume. With the world stopped, the weather, the
sun and the camera are identical, so **the diff is the change and nothing else**. *capture* takes a
list of variants — pass on and off, weather mask on and off, the history on and off, a tunable at two
values — and returns the pictures and their diffs together. This is what `stillat` and the
elimination edits did by hand, reduced to one command.

### 5. Frame series: a video in simulated time — built

No video recorder is installed, and a screen recorder would need the window in front anyway. But
captures are cued on the burst's simulated age, so **slowing the world makes any capture rate free**:
at 0.1x, a picture every simulated twentieth of a second is one every half second of wall clock. A
series becomes:

- **An animation for the player**: an animated GIF or WebP from Pillow, which judges motion the way a
  contact sheet cannot — the rise, the roll, the curtains, the flash's decay.
- **A temporal metric for the agent**: consecutive frames with the world paused give a per-pixel
  standard deviation map. Flicker, crawl and ghosting light up in it; a still cannot show them. The
  night's weather crawl and the reported flicker are both exactly this.

### 6. Debug views in the shader — built

One `Config` field chooses what the pass writes instead of the picture: density, the layer's
transmittance, the cloud's weighted depth, the weather mask and the weather's distance, the history's
weight and where it was rejected, the burn mask. False colour, same camera. The blocky squares at the
fireball's edge would have been one picture of the weather mask instead of an elimination flight.

### 7. Live tunables — built

The look is set by constants in GLSL — `HaloGain`, `GlareFloorNits`, `FootFlare`, `FalloutReach`,
dozens more — and every one is a rebuild. A small storage buffer of named values
(`ComputePipelineWrapper` binds storage buffers) lets *tune* change one while the game runs, the
picture be captured at each value, and the chosen number be written back into the source once. That
is Blender's loop — build, look, adjust — for the shader.

### 8. "Capture for Claude", in play — built

A button in the panel, and a key, that writes a bundle the moment the player sees something: eight
consecutive frames, the manifest, the settings, the last lines of the log. "Clouds flicker through
my nuke" then arrives with the frames it is about, and the temporal map of them. Local only — it
writes a folder and sends nothing, so it is not the report window and is not bound by its rules.

### 9. The shader compiled in `check-all.sh` — built

`glslangValidator` (or shaderc, which is what KSA uses) against both `.comp` files, with
`CoreAtmosphere.glsl` generated against the install the way `Ksa/CoreShaderInclude.cs` writes it at
load. Seconds, and it catches the whole class of error the night found in flight.

### 10. A toolkit for the pictures — built

`tools/vis/`, so what was typed as throwaway Pillow on the night is written once: contact sheets
labelled from manifests, crops around the projected burst, same-instant diffs, the grain measure
(high-pass energy in a region), the temporal map, colour at named points, and GIF assembly. Every
number quoted on the night — grain 0.648 to 0.231, the corners at (232, 212, 176), the bands through
the post chain — came out of code like this, rewritten each time.

### 11. Scenes that hold still — built, but for the weather switch

A save and a site chosen so the world does not change between runs: weather off for anything that
is not about weather, a fixed time of day, and named camera presets — side-on at the watch distance,
under the deck, overhead, ten kilometres out, and night. With a manifest for each, a picture from
today and one from next month are the same shot.

### 12. A reference library

Public-domain frames from the Nevada tests with their yield, range and time after burst, and a
sheet that puts one beside a capture at the same age and framing. "How does ours differ from a real
one" was answered from memory on the night; this answers it from pictures.

## What cannot be had

- **Headless rendering.** KSA ships Windows-only natives and runs its simulation through a Vulkan
  renderer; there is no headless mode to run it in, so a picture always means a running game.
- **A true video.** Nothing records the window, and the frame series in 5 is the substitute. It is
  also the better instrument: its frames are spaced in simulated time, so a slow machine takes the
  same shots as a fast one.
- **Unloading the DLL.** C# changes still need a relaunch, which is why 2 and 7 move as much as
  possible out of it.

## Order

**Debug views and tunables are one mechanism.** Both are specialization constants
(`Sim/ShaderTunables.cs`): declared with a `constant_id` and a default in the GLSL, overridden when
the pipeline is built, so `tune` is a pipeline rebuild — milliseconds, no recompile — and a debug
view costs the picture nothing. `DebugView` 1–5 draws coverage, depth, the weather mask, sunlight
and the fireball's share of the light; the rest are the look's constants.
`ShaderTunablesTests` holds the list and the GLSL to one another. The first use of the sunlight view
turned up a defect: the stem under the cap read fully lit, because the shadow march's four taps step
over a cap a few hundred metres thick a kilometre up the ray. A point under the cap now adds the
ray's closed-form chord through the dome (`CapChord`), for 0.17 ms.

**Capture for Claude** is a button in the debug tools window. It writes `out/player-<time>/`: a
note of the state, every `Config` field and every tunable at the moment it was pressed, the log's
last 300 lines, and eight frames with their manifests. `ksa_player_captures` reads the newest back
as the state, a sheet and the temporal map — with the world running, so motion shows in it too, and
on its first use it picked out a distant piece of scenery flickering on its own.

**Scenes that hold still** are named camera poses in the server — `side`, `under`, `overhead`,
`far`, `downwind` — and a `site` command that sets the craft down anywhere and says the sun's
height there, which is how night is had: the time of day is where the sun is from where the craft
stands. Flown: a 0.3 kt burst 10 km out burns the view to 0.97 at night against 0.44 by day. **Weather
cannot be switched off from here**: KSA reads its cloud setting when it builds the renderer, and its
own settings screen treats the change as one needing a restart. The same-instant captures of 4 are
what removes the drift a weatherless scene was for.

Built: 1–11. `tools/check-shaders.sh` is glslang rather than shaderc — nothing installable
here runs KSA's — with the include given as a search path and the directive's extension named, and
it caught the night's `flat` with its line. Left, in order:

1. **The reference library** (12). Not built on purpose: it is a set of photographs committed to the
   repository, and which ones, and under what licence, is the owner's call rather than a night's.
