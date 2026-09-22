# Seeing the game from a terminal

**A plan, not a record.** How an agent working from WSL checks what this mod draws, what that
loop cost on the night the nuclear burst was built (2026-09-22), and what would make it fast. Blender
has an MCP connector that lets an agent look at the open document, move a camera and render; KSA
has nothing of the kind, and every item here is a piece of one built out of what the engine and
the mod already allow.

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

### 1. A live session with a command channel, and an MCP server over it

The biggest change: **stop relaunching**. The mod already reads a request from a file beside its
log (`scenario.txt`); a command channel is the same idea kept open for the whole session.

- **The mod** polls `<user dir>/KSArmory/bridge/in/` a few times a second, runs each command file
  it finds, and writes a result beside it — JSON, plus any pictures. Commands: *status*, *pause*,
  *resume*, *speed*, *step* (run so many simulated seconds, then pause), *camera* (a preset or a pose
  relative to the burst), *burst* (a yield at a place, as the panel's burst tool does), *capture*
  (see 3–5), *set* (a `Config` or profile field), *view* (see 6), *tune* (see 7), *reload shaders*
  (see 2), *clear* (forget the clouds), *load* (a save), *log* (lines since a mark, filtered).
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

### 2. Shader hot reload, from the mod

KSA has a shader hot-reloader (`KSA.AssetReloader.ShaderReloader`) and it cannot serve a mod: it maps
a path by searching for `Content`, which a mod's path does not contain — that is the
`Error resolving include path … startIndex ('-1')` line in every session's log, and it is harmless.
What the reloader does is small: `ShaderReference.DoLoad()` recompiles the module. `DoLoad` is
internal, so it is one verified reflection away, like the handful the mod already makes; the mod
then drops its pipelines (`CloudPass.Release`) and they rebuild next frame against the new module.

Triggered by *reload shaders* on the channel, or by the file's timestamp changing. A compile error is
caught and logged, and the old pipeline stays. **Shader iteration goes from three minutes to about
one second**, which is most of what the burst's look took.

C# still needs a relaunch: an assembly cannot be unloaded. That is why 7 matters — it moves tuning
out of code.

### 3. Capture bundles: labelled pictures with a manifest

Every capture becomes a bundle: `<run>/<seq>-<label>.png` and a JSON sidecar with the burst's age,
the camera's position and orientation and field of view, **where the burst and the cap centre fall
on screen**, the sun's elevation, the flash's level and glare, the pass's GPU milliseconds, which
weather images were bound, and every setting that shapes the picture.

- Names that cannot collide: the mod renames KSA's file the moment it appears, and does not ask for
  the next capture until it has.
- **Crops come from the manifest**, not from a guess: the burst's screen position is projected by the
  same camera that took the picture.
- **Age is in the file**, so a pairing error cannot happen.

### 4. Controls taken in the same instant

Pause, capture, change one thing, capture again, resume. With the world stopped, the weather, the
sun and the camera are identical, so **the diff is the change and nothing else**. *capture* takes a
list of variants — pass on and off, weather mask on and off, the history on and off, a tunable at two
values — and returns the pictures and their diffs together. This is what `stillat` and the
elimination edits did by hand, reduced to one command.

### 5. Frame series: a video in simulated time

No video recorder is installed, and a screen recorder would need the window in front anyway. But
captures are cued on the burst's simulated age, so **slowing the world makes any capture rate free**:
at 0.1x, a picture every simulated twentieth of a second is one every half second of wall clock. A
series becomes:

- **An animation for the player**: an animated GIF or WebP from Pillow, which judges motion the way a
  contact sheet cannot — the rise, the roll, the curtains, the flash's decay.
- **A temporal metric for the agent**: consecutive frames with the world paused give a per-pixel
  standard deviation map. Flicker, crawl and ghosting light up in it; a still cannot show them. The
  night's weather crawl and the reported flicker are both exactly this.

### 6. Debug views in the shader

One `Config` field chooses what the pass writes instead of the picture: density, the layer's
transmittance, the cloud's weighted depth, the weather mask and the weather's distance, the history's
weight and where it was rejected, the burn mask. False colour, same camera. The blocky squares at the
fireball's edge would have been one picture of the weather mask instead of an elimination flight.

### 7. Live tunables

The look is set by constants in GLSL — `HaloGain`, `GlareFloorNits`, `FootFlare`, `FalloutReach`,
dozens more — and every one is a rebuild. A small storage buffer of named values
(`ComputePipelineWrapper` binds storage buffers) lets *tune* change one while the game runs, the
picture be captured at each value, and the chosen number be written back into the source once. That
is Blender's loop — build, look, adjust — for the shader.

### 8. "Capture for Claude", in play

A button in the panel, and a key, that writes a bundle the moment the player sees something: eight
consecutive frames, the manifest, the settings, the last lines of the log. "Clouds flicker through
my nuke" then arrives with the frames it is about, and the temporal map of them. Local only — it
writes a folder and sends nothing, so it is not the report window and is not bound by its rules.

### 9. The shader compiled in `check-all.sh`

`glslangValidator` (or shaderc, which is what KSA uses) against both `.comp` files, with
`CoreAtmosphere.glsl` generated against the install the way `Ksa/CoreShaderInclude.cs` writes it at
load. Seconds, and it catches the whole class of error the night found in flight.

### 10. A toolkit for the pictures

`tools/vis/`, so what was typed as throwaway Pillow on the night is written once: contact sheets
labelled from manifests, crops around the projected burst, same-instant diffs, the grain measure
(high-pass energy in a region), the temporal map, colour at named points, and GIF assembly. Every
number quoted on the night — grain 0.648 to 0.231, the corners at (232, 212, 176), the bands through
the post chain — came out of code like this, rewritten each time.

### 11. Scenes that hold still

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

1. **Bundles, series and the toolkit** (3, 5, 10) and **the compile check** (9) — all inside the
   loop as it is, and each removes a failure the night actually had.
2. **The command channel and the MCP server** (1), with **same-instant controls** (4) as its first
   command.
3. **Shader hot reload** (2), then **debug views** (6) and **tunables** (7), which only pay once a
   change no longer costs a relaunch.
4. **Capture for Claude** (8), **fixed scenes** (11) and **the reference library** (12).
