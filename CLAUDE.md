# CLAUDE.md

A point-defence mod for **Kitten Space Agency** (KSA, RocketWerkz). Two weapon systems, both
generated from Blender scripts: a **Pantsir-S1** — search radar, proportional-navigation
interceptors, a proximity-fused warhead, twelve rounds in two pods of six on an 8×8 chassis — and
a **LAU-7 rail** carrying one AIM-9J, which surface-attaches to anything and is the shipped
example of a launcher with nothing that moves, and a **Mk 15 Phalanx CIWS** that stacks on a 3 m
node and is the one with no missiles at all.

## Read this first

**`docs/FRAMES-AND-EPOCHS.md` is the one to read before touching rounds, drawing or timing.**
Every hard bug this mod has had is a frame or epoch mismatch multiplied by 29.8 km/s of ecliptic
motion, and that file has the engine's actual contract, the rules that follow from it, and how to
tell the four failure shapes apart. It cost a full night and eight wrong theories.

**`docs/KSA-MODDING-NOTES.md` is the distilled result of reverse-engineering the game.** It has
the runtime, the loader contract, the type signatures, the reference frames and the gotchas.
Read it before touching anything KSA-facing — it will save you an hour of decompiling.

KSA has **no official code-modding API**. Everything is community tooling against a pre-release
game, so the API moves between builds.

## Comments and documentation

**Docs are part of the change, not a follow-up.** If a change makes a line in `CLAUDE.md`, a
`docs/` file, `README.md` or a comment untrue, fix it in the same commit. A stale line is worse
than a missing one: it is trusted. This file claimed "nothing has been verified in-game" for
months after most of it had been, and `Directory.Build.props` described a check that did not
exist — both were believed until someone happened to look.

**Comment why, never what.** The code says what it does. A comment earns its place only when the
reason is not recoverable from reading it — an engine contract, a measured number, a constraint
imposed from somewhere else in the frame, an ordering that looks arbitrary and is not. Everything
else is noise that goes stale.

```csharp
// Anchor to the tube, not the orbit position: those differ by metres on a landed craft.   ok
double3 travelPart = asmb2Part * (ecl2Asmb * travelEcl);

// Convert the travel into the part frame.                                                 delete
double3 travelPart = asmb2Part * (ecl2Asmb * travelEcl);
```

**Keep them short.** A sentence or two. If a comment needs paragraphs, the explanation belongs in
`docs/` with a one-line pointer to it — that is what `docs/FRAMES-AND-EPOCHS.md` and
`docs/MODULARITY.md` are for.

**State the fact, not the history.** A comment says what is true now. It does not narrate what the
code used to do, what broke, when it was reported, or which commit fixed it — that belongs in git,
and the reasoning belongs in `docs/`. History in a comment ages badly, buries the invariant in
storytelling, and is unreadable to anyone who was not there.

```csharp
// elapsed is incremented after the step so the round's position and the back-dated
// target share an instant. Splitting them costs ~142 m at 29.8 km/s.                      ok

// elapsed used to be incremented first, which paired the round with the END of the
// sub-step. Reported from play as rounds appearing sideways; caught by
// ProjectileContractTests on its first run. See commit 6351118.                           delete
```

Both say why. Only the first will still be true and useful in a year.

**When in doubt, delete.** An unnecessary comment is not neutral: it is another thing that can
drift out of step with the code and mislead the next reader. Deleting one is a real improvement,
not a loss.

What stays is the *invariant* and its consequence — the ordering that looks arbitrary, the
measured number, the engine contract. What goes is how anyone came to know it.

## Committing

**Every commit message must be a [Conventional Commit](https://www.conventionalcommits.org/).**
This is not a style preference — semantic-release parses these to decide the next version, so a
message that does not parse silently produces no release and never appears in the changelog.

```
feat(turret): elevate the pods on their trunnions
fix(rounds): anchor bodies to the tube they left, not the orbit position
docs: write an install guide
refactor(sim): split launch geometry out of LauncherPart
```

| Type | Version effect |
| --- | --- |
| `feat`, `fix`, `perf`, `build`, `revert` | **patch** |
| `!` after the type, or a `BREAKING CHANGE:` footer | **major** — see the 1.0.0 note below before using this |
| `docs`, `refactor`, `test`, `chore`, `ci`, `style` | no release, and none of them appear in the changelog |
| a **minor** | never automatic — tag it by hand |

**The type says what a change is; it does not decide how big the version bump is.** `feat` cuts a
patch like everything else, so labelling something a feature is a changelog decision rather than a
release-size one. That split is deliberate: a mod's routine flow is features, enhancements and
fixes together, and bumping minor for each of them makes the middle digit a commit counter.

For calibration: a patch is the right size for a guidance overhaul, a new targeting UI and a dozen
fixes shipped together. A minor means more than that — a new weapon system, or a compatibility
milestone.

**A minor is a deliberate act.** When something genuinely lands — a new weapon system, a KSA
compatibility milestone — tag it:

```bash
git tag -a v0.9.0 -m "second weapon system"
git push origin v0.9.0
```

semantic-release reads the newest tag and carries on from it, so the next `fix` after that is
0.9.1. The same mechanism anchored the very first release.

Scope is optional but useful; prefer the area touched — `turret`, `rounds`, `radar`, `sim`,
`model`, `ci`. Keep the subject in the imperative and under ~72 characters, and use the body to
say *why* when the reason is not obvious from the diff.

**`feat` still means a player can observe the difference in the shipped archive.** Capability that
nothing in `Arsenal.cs` or the panel yet uses is `refactor` — it becomes a feature in the commit
that uses it, which is also the first commit whose behaviour anyone can check. This matters for
the changelog rather than for the version now: a Features section listing things nobody can reach
is worse than a short one.

Scope is not consulted either: `feat(tools)` on a developer script is still a `feat` and still
cuts a release, publishing an archive identical to the last but for its version string. Developer
tooling is `chore`, `ci`, `test` or `refactor`. The
commit-msg hook warns when a `feat`/`fix`/`perf` commit touches nothing under `src/KSArmory/`;
it only warns, because a packaging fix in `tools/package.sh` genuinely changes what ships without
touching `src/` and no mechanical rule gets that right.

**No `Co-Authored-By` trailer, and no other attribution footer.** Commits carry the repository
owner's name and nothing else, whoever or whatever drafted them. This overrides any default to add
one.

Split unrelated work into separate commits rather than one large one: the changelog is generated
from these, so a commit that does three things describes none of them well.

**Do not commit a behaviour fix as a fix until it has been verified in game.** Compiling, passing
the suite, and having a plausible mechanism are not evidence — this mod's hardest bugs live in
the gap between the maths and what KSA actually does, and that gap is only visible in flight. The
round-body zigzag cost three such commits: a sim-step-gating change and an offset-extrapolation
change, both shipped as fixes for a cause not yet diagnosed, and neither was it. The answer was in
a log the whole time.

So: **ship the diagnostic, not the guess.** Instrumentation that will find a cause is worth
committing — say that is what it is. A speculative fix labelled as a fix buries the real cause and
makes the history lie about what was wrong. If something is unverified, write that in the commit
message and leave the decision to the user.

And a regression test only counts **if it fails against the old code**. Check that it does, every
time. One written for the zigzag passed against both implementations — it advanced the platform by
exactly the `v*dt` it passed in, so the error cancelled — which looked like proof and was worth
nothing.

**This is enforced.** `tools/check-commit-msg.sh` runs both as a local `commit-msg` hook
(`./tools/install-hooks.sh`, using `core.hooksPath` so hooks arrive with a pull) and as a CI job
over every commit in a push or PR. One script drives both, so they cannot drift apart. It skips
merges, reverts, `fixup!`/`squash!` and semantic-release's own `chore(release):` commit.

## Environment

- **KSA install**: `/mnt/c/Program Files/Kitten Space Agency` (Windows game, WSL dev)
- **KSA build these notes were taken against**: `2026.8.5.5168`
- The system `dotnet` is 8.0 and **cannot build this** — the mod targets **net10.0**
  (`error NETSDK1045`). A .NET 10 SDK is installed at `~/.dotnet`.
  **Use `tools/build.sh` / `tools/test.sh`**, which source `tools/env.sh` to fix PATH. Bare
  `dotnet` commands fail. In an interactive shell, `source tools/env.sh` once.

- `Import/` holds the game's assemblies and is **gitignored**. Repopulate with
  `./tools/sync-import.sh`. Nothing builds without it — though the build also finds a game
  install or a `ksa-game-assemblies` checkout on its own, see `Directory.Build.props`.
- **When KSA updates, four things have to move together**, not just `Import/`. See
  [After a KSA update](#after-a-ksa-update); getting it wrong makes CI build the mod against a
  different game from the one you are testing on, silently.
- **The game is launchable from WSL** — interop is enabled, so `tools/run.sh` starts
  `StarMap.exe` directly. `run.sh` finds it under the Windows user profile — override with
  `STARMAP_DIR`. It reads `./StarMapConfig.json` **relative to its own directory**, so it must be
  launched from there.
- **The mod writes its own log** to `<KSA user dir>/Logs/KSArmory.log`, readable from WSL;
  `./tools/ksa-user-dir.sh` prints that directory and `./tools/run.sh --attach` follows the log.
  `Console.WriteLine` only reaches stdout, and KSA's `KittenSpaceAgency.log` is written by its
  internal logger which mods cannot reach — so the mod's own file is the debugging channel.
  KSA's log is still the place to look for mod discovery and asset/XML errors.

## Commands

```bash
./tools/doctor.sh                          # can this machine build, test and run it? -- start here
./tools/check-all.sh                       # everything CI runs (~8 s); also the pre-push hook
./tools/build.sh                           # build the mod (handles the SDK PATH)
./tools/test.sh                            # guidance + fuse tests, no game needed
./tools/validate-parts.py                  # part XML, launch geometry, registered PartIds; runs in deploy.sh
./tools/model/build.sh                     # rebuild every part's mesh and textures (needs Blender)
./tools/model/checkswept.py                # does any assembly pass through another in its travel?
./tools/check-boundary.sh                  # Sim/ must not reference KSA types
./tools/check-network.sh                   # the mod only reaches the network when Send is clicked
./tools/check-comments.sh                  # history in comments, XML docs on privates, ratios
./tools/check-docs.sh                      # layout table, API counts and KSA build vs reality
./tools/package.sh                         # release zip into dist/ -- no symbols, no game DLLs
./tools/deploy.sh                          # build and install into the KSA mods folder
./tools/run.sh                             # build, deploy, launch, show the mod's output
./tools/run.sh --attach                    # follow a game that's already running
./tools/scenario.sh head-on                # fly one engagement unattended and report pass/fail
./tools/ksa-user-dir.sh                    # where KSA keeps Logs/, mods/ and saves on this box
./tools/setup-starmap.sh                   # one-off: install StarMap and write its config
./tools/check-assemblies.sh --game         # has the installed game moved past the lock?
./tools/check-ksa-version.sh               # has RocketWerkz published a newer build?
./tools/api-surface.sh                     # record the KSA API this mod binds to
./tools/api-surface.sh --check             # ...and fail if the record is stale
./tools/decompile-assemblies.sh ../ksa-game-assemblies   # refresh the decompiled corpus
./tools/ksa-api-diff.sh ../ksa-game-assemblies           # which KSA changes hit this mod?
./tools/sync-import.sh                     # refresh Import/ -- NOT the whole story after a
                                           #   KSA update; see "After a KSA update" below

source tools/env.sh                        # then bare dotnet works in this shell
cd tools/apidump && dotnet run -- ../../Import members KSA.Vehicle   # inspect the game API
./tools/meshinfo.py "<KSA>/Content/Core/Meshes/CoreStructuralA_MeshAtlas.glb" Tube  # mesh bounds
```

## Layout

**The source is split by whether it touches KSA.** `Sim/` cannot; `Ksa/` does. That is not a
convention to remember — the test project links `Sim/**` wholesale and references no KSA
assembly, so a `using KSA;` under `Sim/` fails the test build. It also means a new file under
`Sim/` is tested the moment it exists, with no build plumbing to add.

| Path | What |
| --- | --- |
| **`src/KSArmory/Sim/`** | **no KSA types, linked into the tests wholesale** |
| `Sim/Arsenal.cs` | **the registry — add a weapon system here** |
| `Sim/WeaponSurvey.cs` | reads a weapons system off a craft the mod did not design |
| `Sim/LauncherProfile.cs` | one launch platform: part Id, tube geometry, drives |
| `Sim/MunitionProfile.cs` | one round: boost, guidance, fuse, warhead |
| `Sim/Warhead.cs` | explosive charge to lethal, blast and fireball radius |
| `Sim/SensorProfile.cs` | one sensor: range, cone, threat model |
| `Sim/Config.cs` | session-wide settings — team names, drawing, logging |
| `Sim/SystemConfig.cs` | one installation's own settings — arm, engage, turret mode, IFF |
| `Sim/SystemSettings.cs` | those settings flattened, so they can be written down and read back |
| `Sim/IProjectile.cs` | **what everything in the air must be** — a weapon kind is an implementation, not a profile field |
| `Sim/Interceptor.cs` | guided round: proportional navigation, boost, fuse |
| `Sim/Slug.cs` | unguided kinetic round: ballistics and a contact fuse |
| `Sim/Magazine.cs` | which tubes hold a round, which fires next, what each body does |
| `Sim/TubeGeometry.cs` | tube positions and directions, pod and radar pose, body placement |
| `Sim/Turret.cs` | rate-limited traverse and elevation drives |
| `Sim/PointingDrive.cs` | a head that points rather than trains — two degrees of freedom, no axes of its own |
| `Sim/FireGeometry.cs` | launch direction and round-body orientation |
| `Sim/FireGate.cs` | whether the launcher is pointing where it is about to shoot |
| `Sim/DriveStatus.cs` | which drives the engine is still accepting writes for, latched per channel |
| `Sim/GunChannel.cs` | the cannon's belt, burst position and next-round timing |
| `Sim/BallisticLead.cs` | where an unguided round must be aimed to arrive where the target will be |
| `Sim/Aimpoint.cs` | what a round is shooting at — craft, component or coordinate |
| `Sim/ThreatModel.cs` | CPA threat classification, priority, engagement envelope |
| `Sim/TrackState.cs` | one contact, as the threat model sees it |
| `Sim/Iff.cs` | which side a contact is on, and whether it may be engaged |
| `Sim/LineOfSight.cs` | whether a body is between the viewer and something |
| `Sim/Picking.cs` | what the cursor's ray meets, and what is nearest it on screen |
| `Sim/Reticle.cs` | the gunner's sight as strokes on a screen — geometry only |
| `Sim/CursorAim.cs` | cursor to viewport coordinates, and the bearing from a mount to what it points at |
| `Sim/WeaponFit.cs` | **what a weapons system is fitted with** — the panel asks this rather than testing profile fields |
| `Sim/StepGate.cs` | hands a simulation step out once and only once |
| `Sim/SimClock.cs` | classifies a step: usable, paused, or too long to integrate |
| `Sim/WarpPolicy.cs` | holds timewarp down while rounds fly, and gives it back after |
| `Sim/ChaseView.cs` | where to put a camera riding behind a round |
| `Sim/ViewClaim.cs` | who may hold the player's main view, and what that means for the loser |
| `Sim/OrbitAim.cs` | the orbit-camera angles that would point the view at something |
| `Sim/ReportDraft.cs` | a bug report or idea being written, and whether it is worth sending |
| `Sim/Vec.cs`, `Sim/DrawAnchor.cs` | vector helpers, the two-instant draw anchor |
| **`src/KSArmory/Ksa/`** | **everything that binds to the game** |
| `Ksa/KSArmoryMod.cs` | StarMap entry point and frame hooks |
| `Ksa/KsaWorld.cs` | most KSA contact is funnelled here — keep it that way |
| `Ksa/WeaponSystems.cs` | one system per weapon fitted, crewed and forgotten with the craft |
| `Ksa/WeaponSystem.cs` | fire control, salvo logic, warhead effects, drives |
| `Ksa/WeaponSystemRoles.cs` | **the slices consumers take** — effects, sights and cameras get a role, not the whole system |
| `Ksa/Radar.cs` | cone search, CPA threat model, lock |
| `Ksa/LauncherPart.cs` | finds a registered launcher, resolves tubes and subparts |
| `Ksa/Ui/Ui.cs` | the panel's shell: system list, panes, and which system they read |
| `Ksa/Ui/UiSystem.cs` | what one system is, sees and is doing |
| `Ksa/Ui/UiTuning.cs` | IFF, and the sensor, guidance and warhead numbers |
| `Ksa/Ui/UiDebug.cs` | test targets, moving craft, hand-fired bursts, the log |
| `Ksa/Ui/UiReport.cs` | the one window behind **Report bug** and **Feedback** |
| `Ksa/Ui/ModMenuEntry.cs` | a copied attribute so ModMenu can list us — **wanted gone**, see `docs/BLOCKED-ON-KSA.md` |
| `Ksa/FeedbackClient.cs` | posts a report to the endpoint, off the frame thread |
| `Ksa/Visuals.cs` | gizmo rendering |
| `Ksa/Detonation.cs` | the fireball where a warhead goes off, through KSA's particle system |
| `Ksa/MotorSound.cs` | the rocket motor you can hear, one spatialised channel per burning round |
| `Ksa/MotorPlume.cs` | the flame at the nozzle, one pooled emitter per burning round |
| `Ksa/MuzzleFlash.cs` | the flash at the cannon's muzzles, one pooled emitter per firing system |
| `Ksa/GunSound.cs` | the cannon you can hear, one looping channel pitched by its fire rate |
| `Ksa/TracerTrail.cs` | tracers, an emitter riding a shell rather than thrown from the muzzle |
| `Ksa/Sight.cs` | paints the gunner's sight over the camera the optical head drives |
| `Ksa/SightCamera.cs` | borrows the main view to look through the optical head, and gives it back |
| `Ksa/Markers.cs` | on-screen brackets over every weapons system, labelled on hover or when pinned |
| `Ksa/RoundFollowable.cs` | a round, presented to the engine as something a camera can follow |
| `Ksa/ChaseHud.cs` | brackets around what a chased round is flying at |
| `Ksa/ChaseCamera.cs` | rides the main view behind a round, and gives it back |
| `Ksa/LevelHorizonController.cs` | KSA's fixed camera controller, with an up vector it does not otherwise offer |
| `Ksa/WatchCamera.cs` | nudges the main view round onto one system, then lets go |
| `Ksa/Contact.cs` | **what a sensor can hold** — a craft, or anything else that can be seen |
| `Ksa/RoundContact.cs` | somebody else's round in the air, as a thing a radar can see |
| `Ksa/Track.cs` | one contact, with the kinematics the threat model reasons about |
| `Ksa/TestTarget.cs` | spawns drones to shoot at, from the panel |
| `Ksa/ScenarioRunner.cs` | flies a scripted engagement with nobody watching, and says what happened |
| `Ksa/CraftMover.cs` | picks a craft up and sets it down elsewhere, from the panel |
| `Ksa/BurstTool.cs` | click the world to set off a warhead there, from the panel |
| `Ksa/Designator.cs` | click the world to shoot at that spot, with no target and no lock |
| `Ksa/Diagnostics.cs` | the periodic world dump — what the system can see and why |
| `Ksa/Build.cs` | what build this is, read off the assembly rather than written down |
| `Ksa/SettingsStore.cs` | per-craft settings across sessions, in JSON beside the log |
| `Ksa/Log.cs` | the mod's own log file, which is the only debugging channel it has |
| `src/KSArmory/KSArmory*.xml` | the launcher part, the armed character and the warhead effects — at the mod root, mirroring Core |
| `src/KSArmory/Meshes/`, `Textures/` | generated art; rebuild with `tools/model/build.sh` |
| `src/KSArmory/Sounds/` | the explosions, generated by `tools/sounds.py`; the cannon, cut from a recording by `tools/cut-cannon.py` |
| `src/KSArmory/mod.toml` | serves as both the content-mod and StarMap manifest |
| `tests/KSArmory.Tests/` | links the KSA-free sources and flies engagements headlessly |
| `KSArmory.sln` | both projects, for editors only — every script builds a csproj directly |
| `infra/dns/` | ksarmory.com's Cloudflare records, as OpenTofu; see `infra/README.md` |
| `infra/services/` | Caddy and the feedback service on the VPS, same |
| `services/feedback/` | the endpoint that takes in-game bug reports and files them as issues |
| `tests/Feedback.Tests/` | its text rules and the log gate, needing neither the game nor the model |
| `tools/apidump/` | reflection dumper for the game assemblies |
| `tools/apisurface/` | reads the KSA API this mod binds to out of its own metadata |
| `docs/KSA-CAMERAS.md` | what the engine does with cameras and viewports, from the decompiled source |
| `docs/KSA-API-SURFACE.md` | **generated** — the 303 members an upgrade has to preserve |
| `docs/AUDIT-2026-08.md` | a 26-agent review of where the code and tools mislead; the ranked list at the end is the backlog, and items come off it as they land |
| `docs/BLOCKED-ON-KSA.md` | **what we want and cannot build**, with the engine reason and what would unblock it |
| `docs/FROM-KSP-MODDING.md` | the concept map for anyone arriving from KSP part modding |
| `docs/MODULARITY.md` | how far the profile/registry split actually generalises, and the test gaps to close before widening it |
| `docs/BATTERY-SPLIT.md` | what `WeaponSystem` should be split into, what to call it instead, and in what order |
| `.claude/skills/upgrade-ksa/` | the whole KSA-update procedure, as a skill |
| `tools/meshinfo.py` | prints mesh bounds from a KSA `.glb` atlas |
| `tools/validate-parts.py` | checks asset Ids, texture paths, and launch geometry vs the mesh |
| `tools/model/` | headless Blender scripts that generate the parts |
| `tools/model/pantsir.py` | the Pantsir, and the entry point that builds the whole atlas |
| `tools/model/sidewinder.py` | the LAU-7 rail and its AIM-9J, into that same atlas |
| `tools/model/ciws.py` | the Phalanx CIWS: a gun with no missiles, on a 3 m stack node |
| `tools/model/checkmesh.py` | finds zero-UV-area triangles and coplanar faces in a `.glb`; `--compare` diffs two atlases by geometry *and* node transform |
| `tools/model/checkswept.py` | sweeps the drives and reports any assembly passing through another |
| `tools/model/kittengun.py` | the kitten's shoulder cannon — a character attachment, not a part |
| `tools/model/smokepuff.py` | the soft sprite the billboard smoke is drawn with |
| `tools/screenshot.sh` | captures the Windows screen; readable from here |
| `tools/scenario.sh` | drives one engagement end to end and exits pass/fail; screenshots on cue |
| `tools/sounds.py` | synthesises the explosion samples, and the fallback cannon behind `--synth-cannon` |
| `tools/cut-cannon.py` | cuts a gunfire recording into spin-up, loop and tail, on measured envelope boundaries |
| `tools/audio/` | the CC0 Phalanx recording the cannon is cut from, and its provenance |
| `tools/logo.py` | the Kessler Systems wordmark and icon, into `branding/` |
| `branding/` | the generated logo the README and SpaceDock point at |

## 3D model pipeline (Blender, headless)

Blender **5.2** is installed at
`/mnt/c/Program Files/Blender Foundation/Blender 5.2/blender.exe` and is driven entirely from
scripts — no viewport work. See `tools/model/README.md`; run `tools/model/smoketest.py` first
after any toolchain change.

```bash
BL="/mnt/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
"$BL" --background --python "$(wslpath -w tools/model/smoketest.py)" -- 'C:\Windows\Temp\out.png'
```

**This loop is verified working**: build geometry → render a PNG → read the PNG here → adjust →
repeat, with `./tools/meshinfo.py` checking exported GLB bounds. Model work is therefore
*visually iterable* rather than blind — use it the same way the diagnostic dump was used for the
simulation bugs. **The Pantsir was built this way**, over about six render-and-adjust rounds.

`tools/model/README.md` has the full pipeline, the coordinate system and five traps that have
each already cost time. Three worth repeating here:

- Blender is a **Windows** binary, so `--python` needs `wslpath -w` and outputs want `C:\...`.
- Blender 5.2 has no `BLENDER_EEVEE_NEXT` — use `BLENDER_EEVEE`.
- **Every face needs UV area.** Collapsing a face's loops onto one swatch centre — the obvious
  way to use a palette atlas — gives a zero UV derivative, hence a zero-length tangent, hence
  `normalize()` → NaN, hence garbage shading. `NaN * 0` is still NaN, so a flat normal map does
  not save it. The vehicle sparkles. `project_to_swatch()` gives each face a small projected
  patch instead.
- **Never let two primitives share a face plane.** Coplanar faces z-fight. `box()` inflates
  every box by a skin *plus a per-box jitter* — a uniform skin only separates faces pointing at
  each other, and does nothing for two boxes whose outer faces both sit on the same constant.
  `cyl()` does not inflate at all, and radius alone will not save a coaxial pair: use a
  different facet count or a `cone()`.
- **The jitter runs off one seed, so moving a `box()` call reshuffles every box after it.** Adding
  or removing one is enough, and the damage lands somewhere else entirely — pushing two faces in
  an unrelated assembly onto the same plane. To change which group a primitive belongs to without
  disturbing anything, set `_group` around the existing call rather than moving the call.

**A character attachment is authored in centimetres, a part in metres.** The kitten is drawn
through `CharacterAvatar.Core.Scale = 0.01`, and `GetBoneTransform` returns a bone matrix that
already carries it — so a mesh exported in metres arrives a hundred times too small. Core's own
attachments measure 80.6 glTF units (helmet) and 48.3 (MMU); ours is 33.8. At metre scale the gun
rendered 3.4 mm long, buried in the fur: it loaded, registered, drew every frame, and was
invisible, with nothing in any log.

**And the scale must be baked into the vertices.** `StaticMeshRenderable.Draw` writes one instance
transform per asset and never reads the glTF's node transforms — `GltfPbrAssetRef.SceneGraph` is
assigned and never read anywhere in the engine. A scale left on the Blender object is silently
discarded. `tools/model/kittengun.py` applies `CHARACTER_SPACE` and then `transform_apply`s it for
exactly that reason.

**An attachment's axes are composed in a different order from the body's.** The body gets
`RotX(-90) * RotZ(-90)` applied *after* the scale (`KittenRenderable:184`); an attachment gets
`RotZ(-90) * RotX(-90)` applied *before* the bone matrix (`:207`). So a mesh that is the right
size can still arrive rotated, and the `<Rotation>` in the attachment XML is where that is
corrected.

**Keep an attachment to one mesh with one primitive.** `GltfPbrSystem` aliases the index buffer
across primitives and then disposes it (`:102` against `:112`), so the second primitive frees a
list the first still points at. One mesh is the only shape that is not walking on freed memory.

**Nothing else in that pipeline fails quietly.** A bad material Id, a missing bone, a null material
slot and a failed asset load all throw, and `AssetManager.GetOrLoad` rethrows rather than
swallowing. The only silent no-draws are `Visible == false` and a glTF with no mesh primitives. So
an attachment that is present but unseen is a *geometry* problem — wrong units, wrong winding, or
wrapped around the camera — not a materials or registration one.

**The atlas is not byte-reproducible.** Blender's exporter does not emit triangles in a stable
order, so a rebuild from unchanged sources gives a different file — same positions, normals and
UVs, permuted index buffer. `git status` showing it modified after a build therefore means
nothing. Ask `./tools/model/checkmesh.py <new> --compare <old>`, which compares the surface
rather than the bytes, and **revert the atlas** if it says the geometry is unchanged.

- **Two bodies can share a plane, and `checkmesh.py` alone will not see it.** It analyses one
  mesh at a time, so a turntable resting exactly on the cap of its mast z-fights like any other
  coincident pair and reports clean — worse when the pair spins, because the fight then rotates.
  The cross-body pass lives in `validate-parts.py`, because the atlas carries **no node
  transforms** and only the part XML knows where each body sits.
- **A render only shows the poses you thought to ask for.** Every geometry defect this model has
  shipped was at some other pose: the pods passing through the gun sponsons at all twelve
  o'clock positions, the tubes through the APU box at bearing 50°. `tools/model/checkswept.py`
  sweeps the drives and reports the metres one assembly would have to move to leave another.
  It needs neither Blender nor the game — the atlas is a library of bodies in their own local
  frames, so any pose is reconstructible from it plus `muzzles.json`.
- **It sweeps every articulated vehicle, and a new one has to be added to `vehicles()` by hand.**
  A body set it does not name is simply not swept, and the tool still prints "clear" — the CIWS
  had a traverse, an elevating head and no coverage at all for exactly that reason. This is the
  same shape as the launcher registry and the travel reader before it: a tool that reads the
  first entry looks correct until there is a second. **When a weapon system stops being the only
  one, check what still assumes it is.**
- **A piece can come adrift and every other check still passes.** The mesh is clean, the pivots
  agree, nothing intersects — the part simply stops touching what carried it and hangs in the
  air. `checkswept.py` requires every primitive of the assembled vehicle to reach the chassis
  through overlap. Per-*body* connectivity is the wrong test and was tried first: the cannon are
  legitimately two islands that never touch each other, and the fins are twelve.
- **A cover must not stand proud of what it covers.** A cap a few millimetres wider than its tube
  catches the light as a rim all the way round, and one with fewer facets makes that rim visibly
  polygonal. It is far too small to see in a preview. `checkswept.py` flags a *short* coaxial
  primitive slightly wider than the long one it caps — short is what separates a mistake from a
  design, because a booster stage is legitimately fatter than its sustainer.
- **Bearing cancels for any pair of bodies that both ride the turret.** The traverse is one rigid
  motion applied to the whole group, so pods-vs-guns, pods-vs-turret and guns-vs-turret are
  one-degree-of-freedom problems in elevation alone. That is a fact about the chain, not a
  sampling shortcut, and it is what keeps the sweep cheap.
- **Elevation turns about +Z, so a gap in Z holds at every pose.** Separating two turret-riding
  bodies in Z is the only separation that survives both drives; separating them in X or Y only
  works at the elevation you checked.

**Neither shows up in Blender's preview render**, so a clean preview proves nothing.
`./tools/model/checkmesh.py <atlas.glb>` catches both and exits non-zero — run it after any
model change. Both defects look identical in game (flickering white speckle), so *diagnose with
the checker, not by eye*: the first attempt at this was spent inflating geometry that was
already fine, because the symptom pointed at z-fighting and the real cause was the UVs.

### Runtime part transforms work — the turret traverses and the pods elevate

**Confirmed in-game: writing a subpart's transform each frame moves it.** That was the last big
"the API allows it, does the engine agree" unknown, and the answer is yes.

**Subparts are `Part` objects in their own right.** `Part.SubParts` is a `ReadOnlySpan<Part>`,
each with settable `Asmb2ParentAsmb` *and* `PositionParentAsmb` — so the launcher stays a single
part in the editor and still articulates.

What it depends on, all worth not rediscovering:

- **`ResetCachedPosMatrixValues()` must be called after the write.** `Part` caches
  `_matrixAsmb2Parent` and friends; without the reset the new value is stored and ignored.
- **A subpart rotates about its own mesh origin.** The turret and pod meshes are exported
  recentred on their pivots (`TURRET_PIVOT`, `POD_PIVOT` in `pantsir.py`) and put back with
  `<Position>` in the XML. Without that, a subpart swings round the chassis like a wrecking ball.
- **SubParts do not nest.** The asset XML places every `<SubPart>` against the `<Part>`, so the
  pods and the search array are *siblings* of the turret, not children. The mod composes the
  rotations itself and rewrites their `PositionParentAsmb` every frame — otherwise they would
  turn on the spot while the turret rotated out from under them.
- **The pods are modelled at their working elevation, not flat**, and runtime elevation is
  applied as a rotation *away* from that reference. A refused write then leaves the vehicle in
  a pose that looks right rather than with its tubes through the tracking radar.
- **`Vehicle.Asmb2Ego` doubles as world→part orientation.** Ego is a pure translation of Ecl, so
  for a *direction* the two agree exactly, and its conjugate turns a world bearing into the part
  frame the drives work in.

Tube offsets are emitted **pod-local**, so the launch markers ride both axes for free — and
`Visuals.DrawLoadedTubes` must resolve them through `PodsPart`, **not** `TurretPart`. Passing
the turret compiles, looks right, and is very nearly right: the markers still follow the
traverse and simply refuse to go up and down with the pods.

**The pods will not depress into the vehicle's own bodywork.** `Turret.DepressionFloorAt`
raises the elevation floor across a forward arc, where the pods would otherwise swing down
through the APU box behind the cab. It eases in across the arc edge, and is enforced against
the *current* bearing in `Update` so traversing into the forward sector lifts the pods on the
way round rather than on arrival. A flat depression limit everywhere would be simpler and
worse — off the beam the pods can legitimately come down to level, which is the shot against
anything skimming the horizon.

`Visuals` draws a cyan line along where the drives think they point, on `Config.DrawTurretFacing`
— its own switch rather than riding on the radar volume, because it is what separates "the maths
is wrong" from "the engine ignored the write" and is the one line worth keeping when everything
else is off. It cost a restart to learn that distinction matters.

The overlay is drawn for **one** system by default. There are as many overlays as there are crewed
systems, and four search cones around four craft is not four times as useful;
`Config.DrawOverlayForFocusedOnly` off draws them all, which is the case for comparing two sites.

Four moving pieces now: chassis (fixed), turret (traverses), pods (traverse + elevate), and the
**search array**, a double-sided hexagonal wedge that turns continuously off the clock rather
than off the track — it is a search set, so it never stops and never aims. Its two hex faces are
clocked 30° apart because hexagons rotated alike put their flats on the same planes, and the
faces lean toward each other far enough for those planes to overlap.

The Pantsir's boresight is local "up" — its set sweeps a hemisphere regardless of where the tubes
are aimed, and the spinning array is cosmetic. That is its `SensorProfile`'s choice rather than a
limit: `BoresightMode` also offers `PartForward`, which the Sidewinder rail uses because a seeker
head looks where the rail points, and `TurretAxis`, which follows the traverse.

## Adding a weapon system

The mod is built around three profile types and a registry, so a new launcher, round or sensor
is **data plus art**, not new logic. No fire-control, guidance or drive code names the Pantsir —
it is named in `Sim/Arsenal.cs`, which is the registry and is meant to. Anything else naming it is
a doc comment citing what it was modelled on. **`tools/model/sidewinder.py` plus the
`SidewinderRail` entry in `Sim/Arsenal.cs` is the worked example** — a whole second system, and
neither of them is longer than a page.

1. **Model it.** A module beside `tools/model/sidewinder.py` exporting into the same atlas,
   called from `pantsir.py`'s `main()` — **after** everything already there, because the box
   jitter runs off one seed and inserting a primitive reshuffles every one drawn after it. Run
   `tools/model/checkmesh.py` and `tools/model/checkswept.py`; between them they catch the
   defects that are invisible in a preview render and obvious in game.
2. **Declare the part.** A `<SubPart>` per moving assembly plus a `<Part>` in
   `KSArmoryAssets.xml`, and a `<PartGameData>` with its colliders and mass.
3. **Register it.** One `LauncherProfile` in `Sim/Arsenal.cs`, naming the munition and sensor it
   uses, with the geometry `build.sh` prints. Add a `MunitionProfile` and a `SensorProfile` too if
   the round or the set differ. Then teach `validate-parts.py` to compare that geometry against
   `muzzles.json` — the generator emitting it and the profile holding it are the same numbers in
   two files, and every previous instance of that in this repo drifted.
4. **Nothing else.** `LauncherPart.Find` matches against every registered part Id, and the
   system selects whichever profile it finds. `ArsenalTests` checks the registry hangs
   together; `validate-parts.py` checks the geometry still matches the mesh, and that every
   registered `PartId` is declared in the XML — a profile naming a part that exists nowhere
   used to pass every gate and simply find no launcher in game.

**A launcher need not carry missiles.** The CIWS declares `Tubes = []`, so `TubeCount` is zero and
the magazine holds nothing; it fires entirely through `GunMunition` and `GunMuzzles`. Two registry
tests encoded the opposite — every launcher has a tube, every turret has pods — and both were
assumptions rather than invariants. What is actually required is that a launcher can shoot with
*something*, and that a traverse carries something that moves with it.

**Its radome elevates with the gun, and that is the whole articulation.** The dome carries the
track antenna, which has to stay boresighted with the barrels, so the housing, the barrels and the
dome are one rigid body swinging on a trunnion between two cheeks that traverse. Splitting them —
dome held upright by the traverse, barrels elevating alone — is what the first version did, and it
reads as a mount that articulates in a way no real one does. The clearance that makes it work is
**a gap in Z**: elevation turns about +Z, so the dome being narrower than the gap between the
cheeks holds at every pose, and nothing else does.

**And it stacks rather than surface-attaching.** A `<Connector>` with no `<Flags>` is a node
connector; `ToSurface` is the opt-in for radial. So the CIWS sits on top of any 3 m tank, decoupler
or adapter, and has one connector because nothing stacks on a gun.

A launcher that does not train is the same `LauncherProfile` with `TurretMarker` and `PodsMarker`
left null — `Trains` is then false, the drives are skipped and `IsLaid` stays true, so fire
control cannot deadlock waiting for something that will never move.
`ArsenalTests.AFixedLauncherIsJustAProfileWithNothingThatMoves` pins that shape, and
`DriveFailureTests` pins the difference between that and a drive the engine refused.

**The mod reaches the network exactly once, and only because a player clicked Send.** There is no
version ping, no update check, no usage count — anything of that shape would be a request nobody
agreed to, arriving as a surprise in someone's firewall log. The click is the permission, which is
why there is no consent dialog to dismiss: the window says what will be sent before it is sent.
`tools/check-network.sh` enforces it textually, the way `check-boundary.sh` guards the Sim/Ksa
split, and it fails the build if any networking type appears outside `Ksa/FeedbackClient.cs`.

That is also why **the "report only against the latest version" rule lives on the server**. The mod
sends its version inside the report it was already sending, and the endpoint answers 426 with the
number needed. Asking the game to check whether it is current would mean a request at startup, for
a rule the server can apply for free.

**A control that opens a window is a button, never a tick box.** A checkmark reads as "this
setting is on", so a window arriving instead is unannounced and the tick says nothing about where
it went. Opening a window is an action; tick boxes are for state — armed, auto-engage, what to
draw, a tool being active. Tint the button if open/closed is worth showing.

**A setting belongs to a system or to the session, and which one is the whole distinction.**
`SystemConfig` holds what can differ between two launchers in the same world — armed,
auto-engage, which weapons are live, turret mode, the optical head's viewport, whether the craft
being flown is protected, and **the IFF policy**, because two sites on opposite sides is exactly
the case. `Config` holds what cannot: the
roster of team names, what gets drawn, how much is logged. The test to apply is not importance but
whether two sites could sensibly disagree — a name labels a craft the same way whoever is looking
at it, and what that name *means* is each system's own.

Weapon *performance* is neither: range, guidance and fuse live on the profiles, because two
Pantsirs on opposite sides of the map share a flight model and disagree about whether they are
armed.

**Every weapons system in the world runs its own fire control.** `Ksa/WeaponSystems.cs` crews a craft
the moment a survey recognises a part on it, pins the system there and forgets it when the craft
dies. Each carries its own `SystemConfig`, so arming one site, sending it a target or putting it
on a team says nothing about any other.

**A weapon system belongs to the installation running it, not to the session.** `WeaponSystem`
carries its own `Profile`, `Munition` and `Sensor`, paired by `Arsenal.LoadoutFor` when it
recognises the part; `Config` deliberately holds none of the three. A session-wide selection is
what makes two different systems in one world impossible — every reader outside a system's own
update gets whichever system resolved last, silently. The profiles are the shared `Arsenal`
instances, so panel tuning still reaches every system running that loadout, which is the intent.

**What is deliberately *not* general yet:** one system per *craft*.
`WeaponSystem.LauncherOrdinal` is pinned to the first launcher found and `WeaponSystems` keys on
the vehicle, so a craft carrying two Sidewinder rails fires one of them. See `docs/MODULARITY.md`
change 2.

## CI and releases

Building needs KSA's own assemblies — `KSA.dll`, `Brutal.Core.Numerics.dll` and friends — and
the tests need them too, for `double3`. They are RocketWerkz's copyrighted files and **must
never be committed here or published anywhere**.

They live instead in the private repository **`LaurensDeV/ksa-game-assemblies`**, checked out by
CI with a **read-only deploy key** held in the `KSA_ASSEMBLIES_KEY` secret. Keeping your own
licensed copy privately is fine; publishing it is not. `tools/sync-assemblies.sh` refreshes the
mirror after a KSA update, refusing if a csproj references something it does not know to copy.
It mirrors the whole SDK by default — see *The mirror is a general KSA SDK* below for why, and
for the `--subset` flag that restores the nine assemblies this repository alone references.

`Directory.Build.props` resolves the folder in tiers, first match wins: `KSA_DLL_DIR` (what CI
sets), then `Import/`, then a sibling `ksa-game-assemblies` checkout, then the game install. So
nothing local changed.

### After a KSA update

**You will be told when this happens**, twice over:

- **Before the update is installed** — RocketWerkz publish the current build at
  `http://ksa-master1.rocketwerkz.com:8082/version`, which returns
  `{"Version": "...", "Url": "..."}`. `./tools/check-ksa-version.sh` compares it to the lock,
  and the `ksa-version.yml` workflow does the same daily and **opens an issue** when they
  diverge. It opens an issue rather than failing: nothing is broken when the game moves, there
  is just work to do, and a red cross on an unrelated commit says that badly.
- **After it is installed** — `./tools/build.sh` checks the install against the lock on every
  build, silent unless it has moved. `./tools/check-assemblies.sh --game` asks on demand.

The version check is deliberately *not* wired into `build.sh`: a network call on every build is
slow, breaks offline, and would be the first thing anyone disabled.

That check deliberately looks at the *install*, not at whatever the build resolved: `Import/` is
a copy, so it still matches the lock after a game update and would report all-clear. It also
compares only what the install ships — `StarMap.API.dll` comes from the loader, and would
otherwise report a KSA update every single run.

The assemblies exist in two places that drift apart silently: your `Import/`, and the
private repo CI compiles against. Update one and not the other and CI is building the mod
against a different game from the one you are testing against — which surfaces as behaviour
nobody can reproduce, not as an error.

`ksa-assemblies.lock` records the expected SHA-256 of each referenced assembly plus the game
build. It holds hashes and names only, so it is safe in a public repository. Both CI jobs check
the assemblies against it and fail if they disagree, and `sync-import.sh` reports the same thing
locally the moment you refresh.

The dance, in order — **or just run the `upgrade-ksa` skill, which is this written out with the
reasoning attached**:

```bash
./tools/sync-import.sh                                    # refresh Import/; it reports the drift
./tools/sync-assemblies.sh      ../ksa-game-assemblies    # the mirror's DLLs
./tools/decompile-assemblies.sh ../ksa-game-assemblies    # the mirror's sources
#   set current/KSA_BUILD, commit BOTH together, push there
./tools/ksa-api-diff.sh ../ksa-game-assemblies      # what actually broke — read this
./tools/check-assemblies.sh --update                # record the new digests
./tools/api-surface.sh                              # the surface moves if the fixes did
#   edit the `build` line in ksa-assemblies.lock, commit it here
```

Then **recheck `docs/BLOCKED-ON-KSA.md`**. It lists what this mod wants and cannot build, with the
engine reason for each; a KSA update is the only thing that changes any of them, and none will show
up in `ksa-api-diff.sh` because they are calls that do not happen rather than members that moved.

Do the private repo *before* pushing here, or CI fails on the lock it cannot satisfy yet.

**The compiler only finds half of it.** A renamed member is a build error you fix in seconds. A
member that keeps its name and signature and changes its *meaning* — a different reference
frame, different units, a reordered enum — compiles clean and is wrong in flight. This
repository has shipped that bug three times from its own code, and a KSA update can reintroduce
any of them. That is what the decompiled corpus is for, and `ksa-api-diff.sh` narrows it from
660,000 lines to the files defining the 115 types this mod actually uses.

**The mirror is a general KSA SDK, not this mod's dependencies.** It carries all 35 RocketWerkz
first-party assemblies plus the loader and the game-shipped third-party — 44 in total, 12 MB —
so any KSA mod can build against it. `sync-assemblies.sh --subset` restores the old minimal set.
Per-mod pinning still comes from that mod's own `ksa-assemblies.lock`, which covers only what it
references, so the drift check stays exact without the mirror being narrow.

The build number is written down in three places, so update all three: `ksa-assemblies.lock`,
`current/KSA_BUILD` in the private repo, and the **KSA build** line under Environment above. The
lock is the one that is actually enforced; the other two are for humans reading.

CI is split the same way the source is:

- **`tooling` (hosted, always runs)** — everything that does not need the game, which is more
  than it sounds. `checkmesh.py` on the committed atlas catches the two defect classes that are
  invisible outside the game; `check-boundary.sh` guards the Sim/Ksa split textually;
  `palette.py` is re-run and the textures diffed, so hand-edited PNGs are caught before the next
  model build silently reverts them. Plus shellcheck, XML well-formedness and a check that no
  `.dll` is tracked.
- **`build` (hosted)** — the real build, the tests, `validate-parts.py` and the package,
  against the checked-out assemblies. If the secret is absent — a fork — the job skips with a
  notice instead of failing on something a contributor cannot fix.

shellcheck runs at `-S warning`. At the default level it flags every `source tools/env.sh` as
unfollowable, which it is, and CI would fail on nothing.

**Versioning is automatic and commit messages are the input.** semantic-release runs on every
push to `main`, reads the Conventional Commits since the last tag, and cuts the release: version,
`CHANGELOG.md`, the `<Version>` in the csproj (via `tools/set-version.sh`), the tag and the
GitHub Release.

**The changelog is written for players, not for this repository.** Those same notes are what
SpaceDock shows, so only `feat`, `fix`, `perf` and `build` appear in it; a refactor or a docs
change tells a player nothing and is in `git log` for anyone who wants it. That is a reason to
label a commit by what a player can observe rather than by which files it touched. **Never edit a version by hand** — it will be overwritten. `feat` is a minor,
`fix`/`perf`/`build`/`revert` a patch, `!` or a `BREAKING CHANGE:` footer a major; `docs`,
`chore`, `ci`, `test`, `style` and `refactor` cut no release. A commit that does not parse is
treated as no release, so a stray `wip` cannot publish anything.

That workflow is in two jobs: the first decides the version and creates the release, the second
builds the archive, attaches it, and **publishes it to SpaceDock**. Both hosted. The release commit
carries `[skip ci]` so it does not retrigger CI.

SpaceDock needs three settings, and the step skips with a notice if any is missing — a fork cannot
have them, and the GitHub release is the real artefact either way:

| | |
| --- | --- |
| `SPACEDOCK_MOD_ID` | repository **variable** — the number in the mod's SpaceDock URL |
| `SPACEDOCK_USERNAME` | repository **variable** |
| `SPACEDOCK_PASSWORD` | repository **secret** |

The KSA version it claims compatibility with comes from `ksa-assemblies.lock`, so what SpaceDock
advertises cannot drift from the build CI actually enforced. `SPACEDOCK_GAME_VERSION` overrides it
for the case where SpaceDock does not list that build yet. It notifies followers on every upload.

Three things that will bite:

- **Branch protection on `main`** blocks the release commit unless the token can bypass it.
- **A shallow checkout** makes semantic-release believe every push is a first release — hence
  `fetch-depth: 0`.
- **A release with no prior tag is 1.0.0.** semantic-release reads "no tags" as "no releases" and
  the `<Version>` in the csproj has no bearing on it. Tags anchor everything, which is also how a
  minor is cut here — see the Committing section. Promotion to 1.0.0 is a deliberate
  `git tag -a v1.0.0`; a `BREAKING CHANGE:` footer would also do it, which is worth avoiding
  until it is meant.

**Releases** are `./tools/package.sh`, locally or from the release workflow — the archive is
identical either way. `./tools/publish-release.sh` does both halves from a machine with KSA:
build, then attach to the release semantic-release created. That is the fallback for when the
assemblies secret is unavailable, and it refuses rather than guessing if the tag or the release
does not exist.

**One archive covers Windows and Linux.** The mod is a portable `net10.0` assembly: no
`RuntimeIdentifier`, no P/Invoke, no Windows-only API. There is nothing to build twice, and
adding a per-platform build would produce two identical files. What *does* differ by platform:

- **Case sensitivity.** A mismatched filename loads on Windows and fails on Linux. The hosted CI
  job runs `validate-parts.py --offline` for exactly this — on Linux, where case matters — and
  it compares against the real directory listing rather than trusting `is_file()`.
- **Where KSA's user directory is.** `Log.cs` tries Documents, `XDG_DATA_HOME`, `~/.local/share`
  and `~/.config` in turn, only ever using one that already exists, and falls back to temp.
  `deploy.sh` searches the same set.
- **The loader.** StarMap ships `StarMap.exe` plus a portable `StarMap.dll`, so `dotnet
  StarMap.dll` is the Linux equivalent. *Not verified here* — this machine is WSL and runs the
  Windows build. Release builds carry **no debug symbols** (`DebugType=none` in the csproj)
and the log starts at `INFO` rather than `DEBUG`, because the developer detail runs to hundreds
of lines per engagement. `Log.Threshold` and the panel's **Verbose log** tick box put it back
without needing a different build, which is what you want from a bug report. The packaging
script refuses to ship a `.pdb` or any DLL that is not ours — that is the last gate before an
archive leaves the machine.

## Design decisions worth not re-litigating

**Rounds are simulated by this mod, not by KSA's vehicle physics.** They are drawn with
`GizmosRenderer` rather than being real part-based vehicles. This was deliberate: spawning real
vehicles needs a part template (GLB model, XML schema, and registering a module type into
engine-internal update lists that would require Harmony patching), and steering them means
writing kinematics from a worker-thread update. Self-simulating gives sub-frame accuracy for
free and cannot corrupt a save. The cost is that rounds look like tracer spheres with trails.
Swapping in real part-based missiles later means replacing `Visuals.DrawRounds` and the
integration in `Interceptor`, nothing else.

**Everything is computed in the ecliptic (`Ecl`) frame** and converted to camera-relative `Ego`
only at draw time. `Ego` is a pure translation of `Ecl`, so this is exact — see the notes.

**Threat classification uses closest point of approach, not closing speed.** That is what makes
targets *passing by* engageable and not just ones flying straight at the launcher. This was an
explicit requirement.

**`Sim/` must stay free of KSA types**, and this enforces itself — see the Layout note. When
something KSA-facing turns out to have testable maths inside it, move the maths into `Sim/`
rather than leaving it unverifiable; `FireGeometry` came out of `LauncherPart` exactly that way,
and the launch-angle bug was only testable afterwards.

**And a `Sim/` entry point differences its own inputs.** It takes both frame-carrying terms —
`(shooterPos, shooterVel, targetPos, targetVel)` — never a `relativeVelocity` computed in `Ksa/`,
because that moves the subtraction carrying the whole frame contract to a call site no test
reaches. Test it for *invariance*: add the same velocity to both inputs, assert the answer does
not move. `docs/FRAMES-AND-EPOCHS.md` has why, and `BallisticLead` is the one that was wrong.

**Weapon performance lives on profiles, not in `Config`.** `Config` is the *player's* settings:
armed, auto-engage, what to draw. Range, guidance, fuse and launcher geometry belong to a
weapon system and vary per system, so they sit on `SensorProfile`, `MunitionProfile` and
`LauncherProfile`. The panel edits the profiles of whichever system it is showing, so live
tuning still works — it just tunes that system rather than the whole mod.

**Rounds are drawn as real subparts, anchored to the tube they left.** Twelve `Missile`
subparts, scaled to nothing until fired, with their transform written each frame. Two rules,
both learned the hard way:

- **Anchor to the tube, add only the travel *since* launch.** `OffsetFromPlatform` is measured
  from the platform's *analytic* orbit position; a subpart is placed against the vehicle's
  *physics* origin. Those differ by metres on a landed craft — the same distinction
  `DrawAnchor` exists to preserve — and using the absolute offset put every round inside the
  search radar. `Interceptor.TravelSinceLaunch` is a difference between two positions in one
  frame, so it carries none of that.
- **Orient off `VelocityLocal`, never `VelocityEcl`.** The latter carries ~29.8 km/s of ecliptic
  motion and points every round the same way.

`RoundBodyAnchorTests` and `FireGeometryTests` hold both, and both were checked by
reintroducing the bug and watching them fail.

**A fully self-contained scenario is not possible from a mod.** `LoadVehicleFromLibrary` in a
system XML resolves through `DefaultVehicleSaves`, whose `SaveFolderPath` is **hardcoded** to
`Content/Core/defaultvehicles` under the game install — not per-mod, and not writable without
elevation. So a one-click "everything placed and ready" scenario would mean writing into
Program Files. Instead: `tools/install-testcraft.sh` writes a craft into the *user's* vehicle
folder (which is writable), and `TestTarget` spawns drones on demand from the panel.

**A system mounts to the craft carrying the launcher part, and stays there.** It does not
follow the player's control. It used to, from before the part existed, and that meant taking the
target's seat re-homed the system onto the target — which then could not be shot at, because
the kill path refuses to destroy its own platform. Four confirmed 22 m hits looked like misses.
`PinPlatform` is how `WeaponSystems` mounts each system on creation, and nothing moves it after:
`ResolvePlatform` returns early for a pinned platform, so without that every system would elect
the craft being flown and they would all pile onto it.

**A round's drawn offset is `PositionEcl − platformEcl`, measured *after* the step against the
platform sample from the *same* frame, with no extrapolation.** Write the update index as `k`,
the platform sample as `Q(k)` and the round's position after its step as `P(k)`. A probe in the
frame hook, where both are produced, measured over thousands of frames:

```
( P(k) − P(k−1) ) − ( Q(k) − Q(k−1) )  ==  localVelocity * dt(k)      violated on 2 frames
```

So `P(k)` and `Q(k)` advance in lockstep, and `P(k) − Q(k)` therefore changes by exactly the
round's own flight each frame. That is the entire requirement, and it is why this form cannot
jitter.

**The phase is the whole story, and it is not what it looks like.** The platform sample arriving
at update `k` has already moved by `v * dt(k)` — the step *that same* update is given, not the
previous one. That follows from what `Universe.GetLastSimStep()` means: at frame `k` it reports
the step the engine has just finished applying, which is precisely the interval the sample moved
across.

Three forms have shipped. Two pair mismatched instants, and both leak the same term:

| | Symptom |
| --- | --- |
| `P(k−1) − Q(k)` — measured before the step | the round's motion at frame `k−1` against the platform's at frame `k` |
| `P(k) − (Q(k) + frameVel*dt)` — extrapolated | re-projects `Q` by a `dt` that has already changed |
| **`P(k) − Q(k)` — after the step, no extrapolation** | **correct; confirmed in game** |

Each of the first two differences to `local*dt − v*dstep`. At ~29.8 km/s a 1 ms wobble in the
step is 30 m, and changing simulation speed swings the step by ~17 ms, which is **500 m in a
single frame** — measured at 507.37 m. Run side by side in flight the two agreed to 0.6 m, which
is what proved they share a cause rather than being alternatives.

**The tests encoded the opposite phase for months.** `RoundOffsetStabilityTests` and
`FrameRegressionTests` used to advance the platform *after* the update, i.e. `Q(k+1) − Q(k) ==
v*dt(k)`. With a constant step that is indistinguishable; it only separates when the step
changes. That is why the whole suite passed against both broken forms — it advanced the platform
by exactly the `v*dt` it passed in, so the error cancelled — and why an earlier version of this
file insisted the ordering was correct and must not be "fixed". It was wrong, and it cost six
wrong theories. They now advance the platform *before* the update, and all eight offset tests
were checked to fail against both predecessors.

`OffsetPhaseTests` holds the measurement and varies the step the way changing simulation speed
does, which is the case a constant-step test cannot see.

**The draw anchor uses two different instants on purpose.** `DrawAnchor.Ego` is sampled this
frame; `DrawAnchor.Ecl` is the platform position the geometry was measured against, one update
earlier. The difference between them *is* the frame's ecliptic motion (~500 m at 60 fps), and
differencing against the older reference is what cancels it. **Collapsing them into one sample
looks like a tidy-up and puts the entire overlay beside the craft.** That has now happened
twice. `DrawAnchorTests` fails if it happens again — read `DrawAnchor.cs` before touching it.

**Fire control runs on simulated time, never on player time.** StarMap's frame hook hands you
`currentPlayerTime` and a player-time delta, and both are deliberately ignored. Player time is
wall-clock, which is wrong twice over and both were seen in game: it keeps running while the
game is **paused**, so the radar accumulated dwell, matured a firing solution and launched into
a frozen world; and it ignores **timewarp**, so at 10× the world moved ten times further per
frame than the rounds did and tracking fell apart. `KsaWorld.SimTimeSeconds` differenced by
`Sim/SimClock.cs` is the fix, and it is `Universe.GetElapsedSimTime()` plus `Universe.IsPaused()`.

`SimClock` classifies steps it cannot integrate. `Interceptor` subdivides internally but clamps
at 64 sub-steps, so beyond `Interceptor.MaxFaithfulStep` (0.32 s) a round at 700 m/s starts
stepping over its own fuse radius, and `SimClock.Classify` answers `Skipped`.

**Past that step the world is slowed rather than the round being lied to.** `Sim/WarpPolicy.cs`
holds timewarp down while anything is in the air and gives the player's speed back when it lands
— so the unsimulatable state is prevented instead of being chosen between. The limit is a *step*,
not a warp factor: it is `MaxFaithfulStep / frameTime`, about 19× at 60 fps and lower on a slow
frame, and the policy calibrates off the step it was just handed rather than assuming a frame
rate.

**It is a control loop against an actuator that answers late and is shared with the player**, and
both halves of that cost a flight to learn. The first version reduced 30× to 9.9× and then
straight on to 3.2×, and oscillated for the whole salvo:

- **Never judge a request on a step that predates it.** The step arriving on the frame a write
  takes effect still measures the interval *before* it, so dividing by it again reduces on top of
  a reduction already in flight. `SettleSteps` waits it out; `AStaleStepDoesNotReduceTheSpeedTwice`
  fails against the version that shipped.
- **Stop competing.** The player's warp control and KSA's auto-warp write the same field, and
  trading writes frame by frame is a loop neither side wins. After `OverridesBeforeYielding` the
  mod stands down for the rest of the salvo and says so — it is the guest.
- **A request never observed is a refusal.** KSA rejects a speed change outright while auto-warp
  runs, which is indistinguishable from a slow write until `FramesAwaitingWrite` have passed.
  Then the salvo is abandoned: a lost salvo the player is told about beats the silent
  alternative, measured in flight at **124 km closest approach against 15–20 m unwarped**.

A player who moves the speed while it is held has overridden the mod, so the held value is not
restored over the top of a deliberate choice. `Config.LimitWarpInFlight` turns the whole thing
off, and then rounds lag the world exactly as they used to.

The clamp is still there and still discards time: the frame that overran cannot be un-run, and
the policy only takes effect from the next one. What it stops is the next thousand frames doing
the same thing silently.

**Kills are binary.** KSA exposes no partial-damage model, only
`Universe.DestroyVehicleFromEvent`. `LethalRadius` destroys; between lethal and `BlastRadius`
the mod logs a near miss and the target survives.

**A warhead is one number: `MunitionProfile.ChargeKg`.** Lethal radius, blast radius and the size
of the fireball are all read off it in `Sim/Warhead.cs`, as the **cube root** — Hopkinson–Cranz,
`R = Z · W^(1/3)`. Doubling a warhead multiplies its reach by 1.26, not by 2, which is the one
thing about explosives worth encoding rather than leaving to whoever types the next profile. Three
free radii could also describe a round whose lethal radius exceeds its blast radius;
`WarheadTests` pins that it cannot. The scaled distances are calibrated to the 57E6's flown
numbers (20 kg → 20 m lethal, 60 m blast), so nothing that has been tested in flight moved.

The *drawn* size has a floor and the radii do not. A 0.16 kg cannon shell scales to 0.2 by the
same law, which draws 5 cm particles — proportionate and invisible at any range anyone watches
from, which is the same as no effect at all. `Warhead.MinimumEffectScale` applies to decoration
only.

**The launcher ships its own art, and the asset XML lives at the mod root.** It used to
instance Core's meshes by Id and ship nothing — that worked, and is still the right answer for
a part that can be assembled from Core's kit, but a Pantsir cannot. The mod now carries
`Meshes/KSArmory_MeshAtlas.glb` and three PNGs, declared with `<MeshAtlas>` and
`<PbrMaterial>` exactly as Core does.

The XML sits at `src/KSArmory/*.xml` rather than in an `Assets/` subfolder **on purpose**.
`<MeshAtlas Path="Meshes/…">` is relative, and it is not documented whether it resolves against
the mod root or against the XML's own directory. With the XML at the root those are the same
directory, so the question never has to be answered. Moving it back into a subfolder reopens a
silent-failure mode.

`Textures` are **PNG, not `.ktx2`** — KSA loads both, and `CharacterAssets.xml` mixes them in
one material. No `toktx` needed.

Run `./tools/validate-parts.py` after touching any of it: a bad Id or path is a *silent*
in-game failure. It now also checks mesh Ids against the atlas and texture paths against disk.

**The part is inert; the behaviour is in C#.** KSA sees structure with mass and a collider.
`LauncherPart.Find` looks for it on the vehicle and the system mounts there. This sidesteps
registering a custom module type into the engine's internal update lists, which is not
reachable without patching.

**Launch and slew geometry live in the Blender script, not in the C#.**
`tools/model/pantsir.py` places the containers and writes `muzzles.json`;
the `LauncherProfile` in `Sim/Arsenal.cs` is pasted from what it prints.
`validate-parts.py` **fails if any of them disagree** — this is the third piece of geometry in
the repo duplicated across a boundary, and the first two both drifted. Change the pods, rerun
`tools/model/build.sh`, paste the block. The tube count is `LauncherProfile.Tubes.Length`, so it
follows the block you paste.

**A system will not fire while its launcher is slewing.** `WeaponSystem.IsLaid` requires
both axes on target for `TurretSettleSeconds` first. Before that gate existed it launched the
instant it had a lock, out of tubes still pointing somewhere else — guidance recovered and the
intercepts still landed, so nothing measured it and only watching it caught it.

**A launcher with nothing to aim and one that cannot aim are different, and `FireGate` keeps them
apart.** A profile declaring no training gear is always laid, so fire control cannot deadlock on
a launcher that will never move. A profile that declares gear whose transform the engine then
refuses is frozen wherever it stopped, and holds fire — treating that as laid ejects rounds along
a stale tube transform, which guidance recovers from well enough that nothing but the drawn
facing line shows it happened.

**Drive failures latch per assembly, not for the whole launcher.** `DriveStatus` carries one bit
per `DriveChannel`, so a refused search-array spin — cosmetic — no longer freezes the traverse,
the pods and the cannon with it. `Reset()` clears the latches, because they record what one
vehicle's part tree refused and a new platform deserves a fresh assessment.

**Being laid is asked per weapon, for the same reason.** The cannon and the pods share only the
traverse, so `GunsAreLaid` reads `GunAimingAccepted` and the guns' own subpart while `IsLaid`
reads the pods'. Pointing both at one flag silenced a working cannon whenever a pod elevation was
refused — or whenever the pods marker resolved to nothing, which needs no engine refusal at all.

**Mouse aim points the launcher, it does not fire it.** `Config.MouseAim` sends the turret and
the optical head at whatever the cursor is over, ahead of the radar *and* ahead of the tracking
switch — with it on the operator is the sensor, so needing to enable radar tracking first would be
surprising. Auto-engage still decides when to shoot, and `Aiming` counts mouse aim so `IsLaid`
still makes the drives settle: without that, rounds leave along a tube that is still swinging.

The conversion is the part worth being careful with, and it is **three** corrections rather than
one. `Camera.ScreenToEgoRay` divides by *its own* `FramebufferSize` while ImGui reports the cursor
across every window, so the cursor has to be offset into the viewport **and scaled from viewport
pixels into framebuffer pixels** — a render or display scale makes those different sizes, and
getting only the offset right leaves an error that is zero at the top-left corner and grows across
the screen. `Sim/CursorAim.TryToFramebuffer` does both and is tested against an offset viewport
rendered at twice its size.

The third is that **a cursor gives a ray, and a drive needs a bearing from the mount.** They are
not the same thing: the camera stands well away from the launcher, so its direction and the
launcher's coincide only for something infinitely far away. Against sky they agree; against
ground a few tens of metres off they disagree by tens of degrees, which reads as a turret that
follows the cursor perfectly above the horizon and points somewhere else entirely below it. So
`KsaWorld.TryCursorAimEcl` takes a mount, resolves the ray to a *point* against the terrain, and
`Sim/CursorAim.TryAimFromMount` does the subtraction. **There is deliberately no direction-only
form of it** — the two are indistinguishable wherever anyone tests them first, which is at the
sky.

The ray comes back in Ego, and a *direction* is identical in Ecl because the two differ by a
translation, so nothing converts it. An **origin** is not: `KsaWorld.TryCursorRayEcl` takes both
off one camera, because pairing a direction from the viewport under the pointer with a position
from `GetMainCamera()` puts the ray a viewport away from the cursor.

The overlay itself — search volume, tracks, tracers, drive facing — is diagnostic and off by
default (`Config.DrawOverlays`, under **Debug → Draw debug lines**). Round *bodies* are real
subparts and are unaffected.

**The optical head drives the main view, because it is the only one that draws a planet.** A
secondary viewport renders a starfield over a featureless grey ball — the planet, lighting, ocean
and atmosphere passes all run only for the frame viewport, which is KSA's and is recorded in
`docs/BLOCKED-ON-KSA.md`. So `Ksa/SightCamera.cs` borrows the player's view instead, and the
secondary path stays as the option for watching a site while flying something else.

**Two things borrow that view, and the loser waits rather than tidying up.** `Sim/ViewClaim.cs` is
the ladder: the player reclaiming the view outranks everything, then the chase camera, then the
sight. The rung that is not obvious is **Yield** — a sight that is no longer wanted must *not*
restore while the chase is driving. Both keep their own recording of what the view was doing, and
they were made in order, so restoring the older one undoes a takeover that happened this frame and
leaves the chase holding a recording of the *sight* to hand back at the end. The player is then
returned to a borrowed pose that nothing is driving. `ViewClaimTests` fails against that shape,
which is the shape this was first written as.

**The camera's roll is the engine's, and getting it needs the one extension point KSA leaves
open.** `FixedController` derives up by crossing the view with the camera reference frame's +Z,
and `GetFrame2Ecl` dispatches on the followed object's *type* — a followable that is not a
`Vehicle` or a `Celestial` gets the Identity frame and its declared `CameraReferenceFrame` is
never read. `RoundFollowable` is one, so the axis is ecliptic +Z and the horizon arrives rolled by
the site's angle from that pole, snapping to it the instant the view is taken.
`Ksa/LevelHorizonController.cs` subclasses the controller and supplies the up vector instead —
`Viewport.FixedController` is a public writable field and `OnFrame` is virtual, so this is
subclassing rather than patching. **Following the launching craft instead does not work**: its
frame would give local vertical, but `PrepareFrame` advances vehicle positions before the viewport
pass while a round's is integrated after it, so the engine would add a frame-newer platform
position to an offset built against the older one — ~500 m per frame, which is what
`RoundFollowable` exists to prevent.

That controller is the one place the mod stands on something nobody promised. It is bound through
`docs/KSA-API-SURFACE.md`, so a signature change is caught; if it ever cannot be installed the
engine's own stays and the roll comes back, which is a worse picture and not a crash.

**A view a mod is driving cannot be taken back by mouse or keyboard**, so anything that borrows it
owes the player a way out and has to say what it is. `FixedController` reads no input at all, and
`Shift+C` routes through `Viewport.NextCameraMode`, whose switch has no `Fixed` case and returns
false — so both reflexes are dead and only KSA's **View** menu sets a mode outright. The panel
says so beside the control, and **off** is the mod's own route back. `docs/KSA-CAMERAS.md` has the
evidence. This is why the chase camera releasing itself matters: it holds for seconds, where the
sight holds until told otherwise.

**A camera move that tracks a round runs on simulated time too.** The rule in *Fire control runs
on simulated time* is not only about fire control: the chase transition advances on the step, so
it holds still through a pause and slows with the panel's slow-motion buttons, which is the whole
point of having them. On player time it slides the view across a world that is not moving.
Lingering on a burst is the exception and stays on wall clock — that is a viewing duration, not
something tracking an object.

**Attaching the view to a round moves the camera before the mod gets a say.** `Camera.SetFollow`
sets `PositionEcl` to the followed object plus 2.5 mean radii *before* switching what is followed,
and a round's mean radius is one metre — so the camera lands next to the missile, and the stored
offset is re-read against a different body frame on the way out, which measured 9.8 m to 164 m of
displacement depending on the craft's attitude. Read the pose you mean to ease away from **before**
the follow swap, and put the camera back afterwards: this frame's view matrix is already built and
the controller does not pick the offset up until the next one.

**The chase travels onto the round rather than cutting to it, and only its position really moves.**
The player is looking at the target and the chase looks along a round flying at it, so the two aim
points are close and the view barely turns — which is the whole reason it reads as calm.
`ChaseView.TryBlend` takes those aims as two **points**, not two directions: directions turn at a
wildly uneven rate and collapse to zero length when opposed, which is a round fired back over the
launcher. Both ends are rebuilt from this frame's samples, held as separations from the craft and
the round; a point stored in the ecliptic at the take falls half a kilometre behind per frame.

**`ChaseView.TryPose` decides where the chase stands and is not the transition's business.** The
transition lerps towards whatever it says. Changing the pose to improve the transition changes the
shot everyone actually watches, and that is a separate decision.

**Reclaiming it has to switch the setting off, not just release the view once.** The setting is
what asks for it, so a borrower that stands down and leaves the request standing takes the view
straight back on the next frame — one frame of the player's camera, then the mod's again, which
reads as it refusing to let go. `StandDown` therefore restores what the view was *following*,
because the mod changed that, and leaves the *mode* alone, because the player chose it.

The camera follows the launcher's own craft while the sight holds it, whatever the player was
following. `FixedController` places the camera at `following.GetPositionEcl() + CameraOffset`
during its own pass, so the offset handed over must be a pure separation: `eye − PlatformEcl`,
both from one sample. Measured from any other craft it carries a frame of that craft's motion
every frame and the sight shivers. Same reason `ChaseCamera` follows the round it rides.

**Only one weapon can own the bearing, and the cannon win the overlap.** The turret lays on the
gun's *ballistic lead* whenever `FireGate.GunsHaveTheEngagement`, and rounds leave along the tube
— so a missile released in that state departs ~18° off a 300 m/s crosser. `FireGate.MissilesMayFire`
holds them. The gate asks where the ring actually points rather than whether the guns are in
range, because a lead solve that fails leaves the ring on the target, which the missiles can use.
Proportional navigation recovered from the off-axis launch well enough that only arithmetic found
it, which is the whole reason the condition is now a tested function rather than an assumption.

**The class is `WeaponSystem`, not `Battery` and no longer `DefenceBattery`.** Two reasons, and
only the first is about names colliding: `KSA.Battery` is the game's electrical battery and these
files have `using KSA;`. The second is that a battery is an air-defence *fire unit*, several
launchers under one fire control, which a rail bolted to a booster and a gun on a stack node are
not. `docs/BATTERY-SPLIT.md` has the argument, and the word for a launcher that engages on its own
if a craft ever carries two.

**Consumers take a role, not the system.** `Ksa/WeaponSystemRoles.cs` names what each one actually
needs: rounds in flight, an effect source, an optical head, a manual trigger, a read-only view.
Ten of the thirteen consumers take one. The three that do not are the frame hook, the panel and the
scenario runner, which command it rather than read it.

## Testing

**The system works end to end in game** — search, lock, slew, salvo, intercept, kill, and the
overlay. `CHECKLIST.md` records what has been confirmed and what has not; update its tick-boxes
and the risk table as items are proved or disproved. It is the list of what is *still* unverified
that matters, not the headline.

That does **not** weaken the rule in [Committing](#committing): a behaviour change is unverified
until it has been flown, whatever the suite says. The two facts sit together — most of the mod is
confirmed working, and each new change still has to earn that separately.

`tests/KSArmory.Tests` flies whole engagements headlessly. Three suites are load-bearing and
should not be weakened without understanding what they buy:

- `GuidanceDiscriminationTests` asserts the crossing-target scenario **misses** with the nav
  constant off. Without it a hit test can pass on a geometry that never needed a lead.
- `ProjectileContractTests` runs the frame and epoch rules against **every** `IProjectile`. Those
  rules belong to the engine, not to a weapon, so a new projectile type inherits the whole trap
  list — it caught a 142 m sub-step phase error in `Slug` on its first run.
- `OffsetPhaseTests` varies the step the way a simulation-speed change does. A constant `dt`
  cannot distinguish the right phase from the wrong one, which is how two broken implementations
  passed for months.

## Not done

- Round bodies survive at long range: measured in flight to **79.5 km with 0.0 m drift**, never
  dropping the subpart link and never culled or clamped. The gizmo tracers stay on as a fallback
  anyway, and `WeaponSystem.RoundBodiesWork` still turns the whole thing off if a write is
  refused — the engine is under no obligation to keep behaving this way.
- The guns elevate on the same solution as the pods — one turret, one aim. What they do not have
  is a firing solution of their own, so the cannon cannot engage a different target from the
  missiles.
- The Pantsir's search volume does not follow its turret, because its profile boresights on local
  "up". `BoresightMode.TurretAxis` exists and nothing registered uses it yet.
- The model has no normal or occlusion detail — flat palette swatches only. Faceted lighting is
  the whole look, which suits KSA's art style, but it is a floor not a ceiling.
- Rounds do not collide with terrain or structures, only their designated target.
- The radar masks contacts the planet hides, but against the body's **mean sphere**: a craft
  behind a ridge is still seen, and the limb is geometric rather than the skyline.
  `SensorProfile.TerrainMarginMetres` inflates the sphere to buy some of that back without
  sampling a height map per contact per scan, which is a cost nobody has measured.
- Battery settings live **inside the save**, at `saves/<save>/KSArmory/systems.json`. KSA's save
  format cannot be extended (`UniverseData` is a fixed XML-mapped class) and StarMap has no save or
  load hook — but a save is a *directory*, so the file sits beside the `universe.xml` it belongs
  to, under a mod-named folder so several mods can do this without agreeing on filenames.

  That placement is chosen because it makes the awkward cases vanish rather than need code:
  deleting a save deletes its settings, copying copies them, renaming takes them along. A session
  with no save open falls back to `<user dir>/KSArmory/`, and the first save opened adopts it.

  **Settings are written when the game writes its save**, detected by watching `universe.xml`'s
  timestamp — not continuously. A continuous write is what stops a reload restoring anything: the
  file is always already up to date with the session, so there is nothing older to go back to.
  What is *not* persisted is system *state* — ammo, tracks, rounds in flight all start fresh.
